using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Sole background authority. Transport capacity is a technical memory bound, not an army cap.
    public sealed class PlayableRuntime : IDisposable
    {
        public const int MaxOutstandingCommands=256;
        private const double TickSeconds=1d/30d;
        private sealed class Pending { public PlayableCommand Command; public double Submitted; }
        private readonly object gate=new object();
        private readonly Queue<Pending> inbox=new Queue<Pending>();
        private readonly Queue<PlayableCommandReceipt> receipts=new Queue<PlayableCommandReceipt>();
        private readonly List<PlayableCommandReceipt> receiptHistory=new List<PlayableCommandReceipt>();
        private readonly AutoResetEvent wake=new AutoResetEvent(false);
        private readonly Thread worker;
        private readonly PlayableDomain domain;
        private readonly PlayableAiOpeningCompositionAuthority openingCompositionAuthority;
        private readonly PlayableAiOwnerLoop ownerAi;
        private readonly PlayableAiOwnerLoop enemyAi;
        private readonly int seed;
        private PlayableSnapshot latest;
        private NavGeometry navigationGeometry;
        private int stopping,stopped,paused,outstanding,errors;
        private long snapshotSequence,lastAcceptedSequence;
        private double lastCommandLatency,lastCpu,maxCpu;
        private int missedDeadlines;
        // Ordinary Player: human owns the first army; native AI owns the opponent.
        public static PlayableRuntime CreateHumanMatch(PlayableProfile profile,long generation,int seed)=>new PlayableRuntime(profile,generation,seed,humanControlledPlayer:true);
        private NativeLobbyConfiguration lobbyConfiguration;
        public NativeLobbyConfiguration LobbyConfiguration => lobbyConfiguration?.Copy();
        public static PlayableRuntime CreateLobbyMatch(PlayableProfile profile,long generation,NativeLobbyConfiguration setup,bool startPaused=false)
        {
            if(setup==null)throw new ArgumentNullException(nameof(setup));
            var error=setup.Validate(profile,true,true);
            if(error!=null)throw new ArgumentException(error,nameof(setup));
            var runtime=new PlayableRuntime(profile,generation,NativeLobbyConfiguration.Seed,humanControlledPlayer:true,startPaused:startPaused);
            runtime.lobbyConfiguration=setup.Copy();
            return runtime;
        }

        public PlayableRuntime(PlayableProfile profile,long generation,int seed,bool autonomousOwnerAi=true,bool autonomousEnemyAi=true,bool humanControlledPlayer=false,bool startPaused=false)
        {
            if(profile==null)throw new ArgumentNullException(nameof(profile));
            if(generation<1)throw new ArgumentOutOfRangeException(nameof(generation));
            this.seed=seed;Generation=generation;paused=startPaused?1:0;
            bool runEnemyAi=autonomousOwnerAi&&autonomousEnemyAi;
            domain=TwoOwnerDiagnosticAdapter.Create(profile,generation,patrol:!runEnemyAi);
            openingCompositionAuthority=new PlayableAiOpeningCompositionAuthority();OpeningComposition=openingCompositionAuthority.Initialize(seed,PlayableDomain.PlayerId);
            if(autonomousOwnerAi&&!humanControlledPlayer){ownerAi=new PlayableAiOwnerLoop(profile,OpeningComposition,generation);aiCheckpoint=ownerAi.Checkpoint;}
            if(runEnemyAi){enemyAi=new PlayableAiOwnerLoop(profile,PlayableAiOpeningComposition.Initialize(seed,"enemy-1"),generation);enemyAiCheckpoint=enemyAi.Checkpoint;}
            navigationGeometry=domain.Geometry;
            latest=domain.PlayerSnapshot(0,RuntimeStatus.Starting,false,Metrics(0,0),null,seed);
            worker=new Thread(Loop){IsBackground=true,Name="Spacewars.Playable.MatchRuntime"};worker.Start();
        }
        private readonly OfflineMatchConfiguration offlineConfiguration;
        private OfflinePresentationFrame offlineFrame;
        private long receiptOrdinal;
        private readonly List<OfflineReceipt> offlineReceipts=new List<OfflineReceipt>();
        public OfflinePresentationFrame OfflineFrame=>Volatile.Read(ref offlineFrame)??throw new InvalidOperationException("Not a configured offline runtime.");
        private readonly Dictionary<string,long> acceptedByOwner=new Dictionary<string,long>();
        public PlayableRuntime(OfflineMatchConfiguration configuration,long generation,bool startPaused=false)
        {
            offlineConfiguration=configuration??throw new ArgumentNullException(nameof(configuration));if(generation<1)throw new ArgumentOutOfRangeException(nameof(generation));
            seed=configuration.Seed;Generation=generation;paused=startPaused?1:0;domain=new PlayableDomain(configuration.Profile,generation,configuration);
            navigationGeometry=domain.Geometry;latest=domain.PlayerSnapshot(0,RuntimeStatus.Starting,false,Metrics(0,0),null,seed);PublishParticipantViews(RuntimeStatus.Starting,null);
            worker=new Thread(Loop){IsBackground=true,Name="Spacewars.Offline.SharedAuthority"};worker.Start();
        }
        public PlayableSnapshot ParticipantView(string ownerId)
        {
            var views=Volatile.Read(ref offlineFrame)?.Views;if(views==null||!views.TryGetValue(ownerId,out var view))throw new ArgumentException("No gameplay perspective for this participant.");return view;
        }
        private void PublishParticipantViews(RuntimeStatus status,string failure)
        {
            if(offlineConfiguration==null)return;bool framePaused=Volatile.Read(ref paused)!=0;var frameMetrics=Metrics(lastCpu,0);var views=new Dictionary<string,PlayableSnapshot>();foreach(var p in offlineConfiguration.Roster)views.Add(p.Id,domain.PlayerSnapshot(snapshotSequence,status,framePaused,frameMetrics,failure,seed,domain.OwnerFor(p.Id)));OfflineReceipt[] history;long ordinal;lock(gate){history=offlineReceipts.ToArray();ordinal=receiptOrdinal;}Volatile.Write(ref offlineFrame,new OfflinePresentationFrame(views,history,ordinal));
        }
        // Trusted local route service only; never a map/HUD or network view.
        public NavGeometry NavigationGeometry=>Volatile.Read(ref navigationGeometry);
        public PlayableSnapshot Latest=>Volatile.Read(ref latest);
        public PlayableAiOpeningCompositionAuthority OpeningCompositionAuthority=>openingCompositionAuthority;
        public PlayableAiOpeningCompositionState OpeningComposition{get;}
        public PlayableAiOwnerCheckpoint AiCheckpoint=>ownerAi==null?null:Volatile.Read(ref aiCheckpoint);
        private PlayableAiOwnerCheckpoint aiCheckpoint;
        // Authority diagnostic only. Enemy policy state must never reach player presentation.
        internal PlayableAiOwnerCheckpoint EnemyAiCheckpoint=>enemyAi==null?null:Volatile.Read(ref enemyAiCheckpoint);
        private PlayableAiOwnerCheckpoint enemyAiCheckpoint;
        public long Generation{get;}
        public bool IsStopped=>Volatile.Read(ref stopped)!=0;
        public NavMailbox<NavigationRequest> Requests=>domain.Navigation.Requests;
        public NavMailbox<NavigationAnswer> Answers=>domain.Navigation.Answers;
        public PlayableCommandSubmitResult TrySubmit(PlayableCommand command)
        {
            // This is the human ingress. It cannot impersonate a trusted AI adapter.
            if(command!=null&&(command.Origin!=PlayableOrderOrigin.Unknown&&command.Origin!=PlayableOrderOrigin.Human||command.Source!=null||command.JobId!=0||command.ActionId!=0))return new PlayableCommandSubmitResult(PlayableCommandStatus.Rejected);
            return Submit(command?.AsHuman());
        }
        internal PlayableCommandSubmitResult TrySubmitAi(PlayableCommand command)
        {
            if(command==null||command.Origin!=PlayableOrderOrigin.Ai)return new PlayableCommandSubmitResult(PlayableCommandStatus.Rejected);
            return Submit(command);
        }
        private PlayableCommandSubmitResult Submit(PlayableCommand command)
        {
            lock(gate)
            {
                if(stopping!=0||stopped!=0)return new PlayableCommandSubmitResult(PlayableCommandStatus.Stopped);
                if(command==null)return new PlayableCommandSubmitResult(PlayableCommandStatus.InvalidEntity);
                if(command.SchemaVersion!=PlayableCommand.CurrentSchemaVersion||command.Sequence<=0||(offlineConfiguration==null?command.Sequence<=lastAcceptedSequence:acceptedByOwner.TryGetValue(command.PlayerId??"",out var prior)&&command.Sequence<=prior))return new PlayableCommandSubmitResult(PlayableCommandStatus.InvalidSequence);
                if(command.Generation!=Generation)return new PlayableCommandSubmitResult(PlayableCommandStatus.StaleGeneration);
                if(offlineConfiguration==null?command.PlayerId!=PlayableDomain.PlayerId:!offlineConfiguration.Roster.Any(p=>p.Id==command.PlayerId&&(command.Origin==PlayableOrderOrigin.Ai?p.Control==OfflineControl.Ai:p.Control==OfflineControl.Human)))return new PlayableCommandSubmitResult(PlayableCommandStatus.InvalidOwner);
                if(paused!=0)return new PlayableCommandSubmitResult(PlayableCommandStatus.Rejected);
                if(outstanding>=MaxOutstandingCommands)return new PlayableCommandSubmitResult(PlayableCommandStatus.Overflow);
                lastAcceptedSequence=Math.Max(lastAcceptedSequence,command.Sequence);if(offlineConfiguration!=null)acceptedByOwner[command.PlayerId]=command.Sequence;outstanding++;
                inbox.Enqueue(new Pending{Command=command,Submitted=Timestamp()});
            }
            Signal();return new PlayableCommandSubmitResult(PlayableCommandStatus.Accepted);
        }
        public IReadOnlyList<PlayableCommandReceipt> DrainReceipts(){lock(gate){var result=receipts.ToArray();receipts.Clear();outstanding-=result.Count(r=>r.Status!=PlayableCommandStatus.Accepted);return result;}}
        // Non-destructive diagnostic read: adapter consumers must not steal presentation receipts.
        public IReadOnlyList<PlayableCommandReceipt> ReceiptsAfter(long sequence){lock(gate)return receiptHistory.Where(r=>r.Sequence>sequence).ToArray();}
        public void RequestPause(bool pause){lock(gate){if(stopping==0&&stopped==0)Volatile.Write(ref paused,pause?1:0);}Signal();}
        public void RequestStop(){lock(gate)Volatile.Write(ref stopping,1);Signal();}
        public void Dispose()=>RequestStop();
        private void Loop()
        {
            string failure=null;
            try
            {
                var clock=Stopwatch.StartNew();double next=0,lastAt=0;
                while(Volatile.Read(ref stopping)==0)
                {
                    double now=clock.Elapsed.TotalSeconds;
                    if(now<next){wake.WaitOne((int)Math.Min(20,Math.Ceiling((next-now)*1000)));continue;}
                    var timer=Stopwatch.StartNew();bool isPaused=Volatile.Read(ref paused)!=0;
                    domain.SetRallyPaused(isPaused);ApplyCommands(isPaused);
                    var snapshot=PlayableAiAuthorityCycle.Advance(domain,ownerAi,enemyAi,LastHumanSequence,isPaused,++snapshotSequence,seed,
                        ()=>Metrics(lastCpu,lastAt==0?0:(now-lastAt)*1000));
                    foreach(var receipt in domain.DrainRallyReceipts())RecordReceipt(receipt);
                    if(ownerAi!=null)Volatile.Write(ref aiCheckpoint,ownerAi.Checkpoint);
                    if(enemyAi!=null)Volatile.Write(ref enemyAiCheckpoint,enemyAi.Checkpoint);
                    PublishParticipantViews(RuntimeStatus.Running,null);Volatile.Write(ref navigationGeometry,domain.Geometry);Volatile.Write(ref latest,snapshot);lastCpu=timer.Elapsed.TotalMilliseconds;maxCpu=Math.Max(maxCpu,lastCpu);if(lastCpu>TickSeconds*1000)missedDeadlines++;lastAt=now;
                    next+=TickSeconds;if(next<clock.Elapsed.TotalSeconds)next=clock.Elapsed.TotalSeconds;
                }
            }
            catch(Exception ex){failure=ex.ToString();Interlocked.Increment(ref errors);}
            finally
            {
                domain.CancelAllRally();foreach(var receipt in domain.DrainRallyReceipts())RecordReceipt(receipt);
                ownerAi?.Stop(domain.Tick);if(ownerAi!=null)Volatile.Write(ref aiCheckpoint,ownerAi.Checkpoint);
                enemyAi?.Stop(domain.Tick);if(enemyAi!=null)Volatile.Write(ref enemyAiCheckpoint,enemyAi.Checkpoint);
                lock(gate)
                {
                    stopping=1;
                    while(inbox.Count>0){var p=inbox.Dequeue();RecordReceipt(new PlayableCommandReceipt(p.Command.Sequence,domain.Tick,PlayableCommandStatus.Cancelled,"Runtime stopped.",0,p.Command.PlayerId));}
                }
                Volatile.Write(ref latest,domain.PlayerSnapshot(++snapshotSequence,failure==null?RuntimeStatus.Stopped:RuntimeStatus.Failed,paused!=0,Metrics(lastCpu,0),failure,seed));
                PublishParticipantViews(failure==null?RuntimeStatus.Stopped:RuntimeStatus.Failed,failure);Volatile.Write(ref stopped,1);wake.Dispose();
            }
        }
        private void ApplyCommands(bool isPaused)
        {
            int count;lock(gate)count=inbox.Count;
            for(int i=0;i<count;i++)
            {
                Pending p;lock(gate)p=inbox.Dequeue();
                double latency=(Timestamp()-p.Submitted)*1000;lastCommandLatency=latency;
                PlayableCommandStatus status;string message;
                try {if(isPaused){status=PlayableCommandStatus.Rejected;message="Paused.";}else status=domain.Apply(p.Command,out message);}
                catch {RecordReceipt(new PlayableCommandReceipt(p.Command.Sequence,domain.Tick,PlayableCommandStatus.Rejected,"Command failed.",latency,p.Command.PlayerId));throw;}
                RecordReceipt(new PlayableCommandReceipt(p.Command.Sequence,domain.Tick,status,message,latency,p.Command.PlayerId));
            }
        }
        private void RecordReceipt(PlayableCommandReceipt receipt){lock(gate){receipts.Enqueue(receipt);receiptHistory.Add(receipt);if(receiptHistory.Count>512)receiptHistory.RemoveAt(0);
            if(offlineConfiguration!=null){offlineReceipts.Add(new OfflineReceipt(++receiptOrdinal,receipt));if(offlineReceipts.Count>512)offlineReceipts.RemoveAt(0);}
        }}
        private long LastHumanSequence(){lock(gate)return lastAcceptedSequence;}
        private PlayableRuntimeMetrics Metrics(double cpu,double interval){int backlog;lock(gate)backlog=inbox.Count;return new PlayableRuntimeMetrics(cpu,interval,lastCommandLatency,backlog,Volatile.Read(ref errors),domain.Navigation.PendingCount,missedDeadlines,maxCpu);}
        private static double Timestamp()=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
        private void Signal(){try{wake.Set();}catch(ObjectDisposedException){}}
    }
}
