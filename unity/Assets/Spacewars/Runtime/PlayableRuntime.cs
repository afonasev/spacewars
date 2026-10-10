using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Sole background authority. Transport capacity is a technical memory bound, not an army cap.
    public sealed partial class PlayableRuntime : IDisposable
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
        private readonly AiAuthorityScheduler aiScheduler;
        private readonly PlayableAuthorityTick authorityTick;
        private readonly int seed;
        private PlayableSnapshot latest;
        private PlayableProfile requestedBalance;
        private AiProfile requestedAiProfile;
        private int requestedBalanceBase;
        private string completedBalanceStatus;
        private string balanceApplyStatus;
        public string BalanceApplyStatus {get{lock(gate)return balanceApplyStatus;}}
        public string RequestBalance(PlayableProfile candidate,long generation,int expectedRevision)
        {
            lock(gate){if(stopping!=0||stopped!=0)return "Матч остановлен.";
                if(candidate==null||generation!=Generation||latest==null||latest.ProfileRevision!=expectedRevision)return "Матч или ревизия изменились.";
                if(requestedBalance!=null||requestedAiProfile!=null)return "Предыдущее применение ещё ожидает продолжения.";
                requestedBalance=candidate;requestedBalanceBase=expectedRevision;balanceApplyStatus="Применение после продолжения";}
            PresentationChanged();Signal();return null;
        }
        public string RequestAiProfile(AiProfile candidate,long generation,int expectedRevision)
        {
            lock(gate){if(stopping!=0||stopped!=0)return "Матч остановлен.";
                if(candidate==null||generation!=Generation||NativeAiProfile.Revision!=expectedRevision)return "Матч или AI ревизия изменились.";
                if(requestedBalance!=null||requestedAiProfile!=null)return "Предыдущее применение ещё ожидает продолжения.";
                if(candidate.Revision==NativeAiProfile.Revision&&candidate.Hash!=NativeAiProfile.Hash)return "Номер текущей AI ревизии уже занят.";
                requestedAiProfile=candidate;balanceApplyStatus="Применение после продолжения";}
            PresentationChanged();Signal();return null;
        }
        private void ApplyRequestedBalance(bool isPaused)
        {
            if(isPaused)return;PlayableProfile next;int expected;lock(gate){next=requestedBalance;expected=requestedBalanceBase;}if(next==null){AiProfile requested;lock(gate)requested=requestedAiProfile;if(requested==null)return;
                aiScheduler.Rebind(requested);NativeAiProfile=requested;EnemyAiConfig=new AiOwnerConfig(EnemyAiConfig.OwnerId,EnemyAiConfig.Difficulty,seed,requested);
                completedBalanceStatus="Применена AI ревизия "+requested.Revision;return;}
            var error=expected!=domain.CurrentBalanceRevision?"Ревизия матча изменилась; повторите применение.":domain.ValidateBalance(next);
            if(error==null){foreach(var owner in aiScheduler.Owners)owner.ReconcileBudget(domain);domain.ApplyBalance(next);aiScheduler.Rebind(next);}
            completedBalanceStatus=error??("Применена ревизия "+next.DisplayName+" · "+next.Revision);
        }
        private NavGeometry navigationGeometry;
        private PlayableRouteBinding navigationBinding;
        public PlayableRouteBinding NavigationBinding=>Volatile.Read(ref navigationBinding);
        private void PublishNavigationBinding(){Volatile.Write(ref navigationBinding,authorityTick.NavigationBinding);Volatile.Write(ref navigationGeometry,domain.Geometry);}
        private int stopping,stopped,paused,outstanding,errors;
        private int finishRequested;
        private readonly Dictionary<string,int> humanActions=new Dictionary<string,int>();
        private MatchResult matchResult;
        public MatchResult Result=>Volatile.Read(ref matchResult);
        public void RecordHumanAction(string ownerId){lock(gate){if(stopping==0&&stopped==0&&paused==0&&latest?.Outcome==PlayableMatchOutcome.Playing&&(offlineConfiguration==null?ownerId==PlayableDomain.PlayerId:offlineConfiguration.Roster.Any(p=>p.Id==ownerId&&p.Control==OfflineControl.Human)))humanActions[ownerId]=checked((humanActions.TryGetValue(ownerId,out var count)?count:0)+1);}Signal();}
        public void RequestFinish(){lock(gate){if(stopping==0&&stopped==0)finishRequested=1;}Signal();}
        private void ApplyMatchActions(bool isPaused)
        {
            KeyValuePair<string,int>[] actions;bool finish;
            lock(gate){actions=humanActions.OrderBy(x=>x.Key,StringComparer.Ordinal).ToArray();humanActions.Clear();finish=finishRequested!=0;finishRequested=0;}
            if(actions.Length!=0||finish)PresentationChanged();
            foreach(var row in actions)for(int i=0;i<row.Value;i++)domain.RecordHumanAction(row.Key);
            if(finish)domain.FinishManually();
            Volatile.Write(ref matchResult,domain.Result);
        }
        private long snapshotSequence,lastAcceptedSequence;
        private long presentationRevision,lastPublishedRevision=-1;
        private bool lastPublishedPause;
        private long barrierPolls,barrierReuses,barrierPublications;
        public long BarrierPolls=>Interlocked.Read(ref barrierPolls);
        public long BarrierPayloadReuses=>Interlocked.Read(ref barrierReuses);
        public long BarrierPayloadPublications=>Interlocked.Read(ref barrierPublications);
        private long payloadBuildTicks;
        private int diagnosticUnits,diagnosticBuildings;
        // Aggregate counts for trusted diagnostics, never owner/AI observation.
        public int DiagnosticGlobalUnits=>Volatile.Read(ref diagnosticUnits);
        public int DiagnosticGlobalBuildings=>Volatile.Read(ref diagnosticBuildings);
        public double PayloadBuildElapsedMilliseconds=>Interlocked.Read(ref payloadBuildTicks)*1000d/Stopwatch.Frequency;
        private void PresentationChanged()=>Interlocked.Increment(ref presentationRevision);
        private double lastCommandLatency,lastCpu,maxCpu;
        private int missedDeadlines;
        // Ordinary Player: human owns the first army; native AI owns the opponent.
        public static PlayableRuntime CreateHumanMatch(PlayableProfile profile,long generation,int seed)=>new PlayableRuntime(profile,generation,seed,humanControlledPlayer:true);
        private NativeLobbyConfiguration lobbyConfiguration;
        public NativeLobbyConfiguration LobbyConfiguration => lobbyConfiguration?.Copy();
        public static PlayableRuntime CreateLobbyMatch(PlayableProfile profile,long generation,NativeLobbyConfiguration setup,bool startPaused=false)
        {
            if(setup==null)throw new ArgumentNullException(nameof(setup));setup=setup.Copy();
            var error=setup.Validate(profile,true,true);
            if(error!=null)throw new ArgumentException(error,nameof(setup));
            PlayableRuntime runtime;
            if(setup.Participants!=null)
            {
                runtime=new PlayableRuntime(NativeLobbyMatch.Create(profile,setup),generation,startPaused,setup.Difficulty,setup.Participants.Where(p=>!p.Human).Select(p=>p.Difficulty).ToArray(),spectator:setup.Spectator);
            }
            else if(profile.AuthoredMap is FoundryMap foundry)
            {
                runtime=new PlayableRuntime(foundry.Configuration(profile,setup.ResolveSeed(),humanId:"player-1"),generation,startPaused,setup.Difficulty);
            }
            else runtime=new PlayableRuntime(profile,generation,setup.ResolveSeed(),humanControlledPlayer:true,startPaused:startPaused,aiDifficulty:setup.Difficulty);
            runtime.lobbyConfiguration=setup.Copy();
            return runtime;
        }

        public PlayableRuntime(PlayableProfile profile,long generation,int seed,bool autonomousOwnerAi=true,bool autonomousEnemyAi=true,bool humanControlledPlayer=false,bool startPaused=false,AiProfile aiProfile=null,AiDifficulty aiDifficulty=AiDifficulty.Fighter,bool spectator=false,AiDifficulty? firstAiDifficulty=null)
        {
            if(profile==null)throw new ArgumentNullException(nameof(profile));
            if(generation<1)throw new ArgumentOutOfRangeException(nameof(generation));
            localSpectator=spectator;
            NativeAiProfile=aiProfile??AiProfile.Initial;NativeAiCatalog=AiRosterCatalog.Initial;
            EnemyAiConfig=new AiOwnerConfig("enemy-1",aiDifficulty,seed,NativeAiProfile);
            this.seed=seed;Generation=generation;paused=startPaused?1:0;
            bool runEnemyAi=autonomousOwnerAi&&autonomousEnemyAi;
            domain=TwoOwnerDiagnosticAdapter.Create(profile,generation,patrol:!runEnemyAi);
            openingCompositionAuthority=new PlayableAiOpeningCompositionAuthority();OpeningComposition=openingCompositionAuthority.Initialize(seed,PlayableDomain.PlayerId,aiProfile:NativeAiProfile);
            if(autonomousOwnerAi&&!humanControlledPlayer){ownerAi=new PlayableAiOwnerLoop(profile,OpeningComposition,generation,audit:null,aiProfile:NativeAiProfile,aiDifficulty:firstAiDifficulty??aiDifficulty);aiCheckpoint=ownerAi.Checkpoint;}
            if(runEnemyAi){enemyAi=new PlayableAiOwnerLoop(profile,PlayableAiOpeningComposition.Initialize(seed,"enemy-1",aiProfile:NativeAiProfile),generation,audit:null,aiProfile:NativeAiProfile,aiDifficulty:aiDifficulty);enemyAiCheckpoint=enemyAi.Checkpoint;}
            aiScheduler=new AiAuthorityScheduler(new[]{ownerAi,enemyAi});
            authorityTick=new PlayableAuthorityTick(domain,aiScheduler,seed);
            PublishNavigationBinding();
            latest=domain.PlayerSnapshot(0,RuntimeStatus.Starting,false,Metrics(0,0),null,seed);
            PublishParticipantViews(RuntimeStatus.Starting,null);
            worker=new Thread(Loop){IsBackground=true,Name="Spacewars.Playable.MatchRuntime"};worker.Start();
        }
        private readonly OfflineMatchConfiguration offlineConfiguration;
        private OfflinePresentationFrame offlineFrame;
        private long receiptOrdinal;
        private readonly List<OfflineReceipt> offlineReceipts=new List<OfflineReceipt>();
        public OfflinePresentationFrame OfflineFrame=>Volatile.Read(ref offlineFrame)??throw new InvalidOperationException("Not a configured offline runtime.");
        private readonly Dictionary<string,long> acceptedByOwner=new Dictionary<string,long>();
        public PlayableRuntime(OfflineMatchConfiguration configuration,long generation,bool startPaused=false,AiDifficulty aiDifficulty=AiDifficulty.Fighter,AiDifficulty[] participantDifficulties=null,bool spectator=false)
        {
            localSpectator=spectator;offlineConfiguration=configuration??throw new ArgumentNullException(nameof(configuration));if(generation<1)throw new ArgumentOutOfRangeException(nameof(generation));
            seed=configuration.Seed;Generation=generation;paused=startPaused?1:0;domain=new PlayableDomain(configuration.Profile,generation,configuration);
            NativeAiProfile=AiProfile.Initial;NativeAiCatalog=AiRosterCatalog.Initial;EnemyAiConfig=new AiOwnerConfig(configuration.Roster.FirstOrDefault(p=>p.Control==OfflineControl.Ai)?.Id??"enemy-1",aiDifficulty,seed,NativeAiProfile);
            aiScheduler=new AiAuthorityScheduler(configuration.Roster.Where(p=>p.Control==OfflineControl.Ai).Select((p,i)=>new PlayableAiOwnerLoop(configuration.Profile,PlayableAiOpeningComposition.Initialize(seed,p.Id,aiProfile:NativeAiProfile),generation,null,NativeAiProfile,participantDifficulties==null?aiDifficulty:participantDifficulties[i])));
            authorityTick=new PlayableAuthorityTick(domain,aiScheduler,seed,configuration);
            PublishNavigationBinding();latest=domain.PlayerSnapshot(0,RuntimeStatus.Starting,false,Metrics(0,0),null,seed);PublishParticipantViews(RuntimeStatus.Starting,null);
            worker=new Thread(Loop){IsBackground=true,Name="Spacewars.Offline.SharedAuthority"};worker.Start();
        }
        public PlayableSnapshot ParticipantView(string ownerId)
        {
            var views=Volatile.Read(ref offlineFrame)?.Views;if(views==null||!views.TryGetValue(ownerId,out var view))throw new ArgumentException("No gameplay perspective for this participant.");return view;
        }
        private void PublishParticipantViews(RuntimeStatus status,string failure,bool? publicationPaused=null)
        {
            long began=Stopwatch.GetTimestamp();
            Volatile.Write(ref diagnosticUnits,domain.DiagnosticUnitCount);Volatile.Write(ref diagnosticBuildings,domain.DiagnosticBuildingCount);
            try{PublishParticipantViewsCore(status,failure,publicationPaused);}
            finally{Interlocked.Add(ref payloadBuildTicks,Stopwatch.GetTimestamp()-began);}
        }
        private void PublishParticipantViewsCore(RuntimeStatus status,string failure,bool? publicationPaused)
        {
            bool framePaused=publicationPaused??(Volatile.Read(ref paused)!=0);
            PublishSpectatorFrame(status,failure,framePaused);
            if(offlineConfiguration==null)return;var frameMetrics=Metrics(lastCpu,0);var views=new Dictionary<string,PlayableSnapshot>();foreach(var p in offlineConfiguration.Roster)views.Add(p.Id,domain.PlayerSnapshot(snapshotSequence,status,framePaused,frameMetrics,failure,seed,domain.OwnerFor(p.Id)));OfflineReceipt[] history;long ordinal;lock(gate){history=offlineReceipts.ToArray();ordinal=receiptOrdinal;}Volatile.Write(ref offlineFrame,new OfflinePresentationFrame(views,history,ordinal));
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
        public AiProfile NativeAiProfile {get;private set;}
        public AiRosterCatalog NativeAiCatalog {get;}
        public AiOwnerConfig EnemyAiConfig {get;private set;}
        public long Generation{get;}
        public bool IsStopRequested=>Volatile.Read(ref stopping)!=0;
        private Exception routeFailure;
        public void ReportRouteFailure(Exception failure){Volatile.Write(ref routeFailure,failure??throw new ArgumentNullException(nameof(failure)));Signal();}
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
                if(localSpectator&&command.Origin!=PlayableOrderOrigin.Ai)return new PlayableCommandSubmitResult(PlayableCommandStatus.InvalidOwner);
                if(command.SchemaVersion!=PlayableCommand.CurrentSchemaVersion||command.Sequence<=0||(offlineConfiguration==null?command.Sequence<=lastAcceptedSequence:acceptedByOwner.TryGetValue(command.PlayerId??"",out var prior)&&command.Sequence<=prior))return new PlayableCommandSubmitResult(PlayableCommandStatus.InvalidSequence);
                if(command.Generation!=Generation)return new PlayableCommandSubmitResult(PlayableCommandStatus.StaleGeneration);
                if(offlineConfiguration==null?command.PlayerId!=PlayableDomain.PlayerId:!offlineConfiguration.Roster.Any(p=>p.Id==command.PlayerId&&(command.Origin==PlayableOrderOrigin.Ai?p.Control==OfflineControl.Ai:p.Control==OfflineControl.Human)))return new PlayableCommandSubmitResult(PlayableCommandStatus.InvalidOwner);
                if(paused!=0)return new PlayableCommandSubmitResult(PlayableCommandStatus.Rejected);
                if(outstanding>=MaxOutstandingCommands)return new PlayableCommandSubmitResult(PlayableCommandStatus.Overflow);
                lastAcceptedSequence=Math.Max(lastAcceptedSequence,command.Sequence);if(offlineConfiguration!=null)acceptedByOwner[command.PlayerId]=command.Sequence;outstanding++;
                inbox.Enqueue(new Pending{Command=command,Submitted=Timestamp()});
                if(command.Origin==PlayableOrderOrigin.Human&&Enum.IsDefined(typeof(PlayableCommandKind),command.Kind)&&command.Kind!=PlayableCommandKind.Restart&&latest?.Outcome==PlayableMatchOutcome.Playing)humanActions[command.PlayerId]=checked((humanActions.TryGetValue(command.PlayerId,out var actions)?actions:0)+1);
            }
            PresentationChanged();Signal();return new PlayableCommandSubmitResult(PlayableCommandStatus.Accepted);
        }
        public IReadOnlyList<PlayableCommandReceipt> DrainReceipts(){lock(gate){var result=receipts.ToArray();receipts.Clear();outstanding-=result.Count(r=>r.Status!=PlayableCommandStatus.Accepted);return result;}}
        // Non-destructive diagnostic read: adapter consumers must not steal presentation receipts.
        public IReadOnlyList<PlayableCommandReceipt> ReceiptsAfter(long sequence){lock(gate)return receiptHistory.Where(r=>r.Sequence>sequence).ToArray();}
        public void RequestPause(bool pause){lock(gate){if(stopping==0&&stopped==0&&paused!=(pause?1:0)){Volatile.Write(ref paused,pause?1:0);PresentationChanged();}}Signal();}
        public void RequestStop(){lock(gate)Volatile.Write(ref stopping,1);Signal();NavigationBinding?.RoutePort?.SignalHost();}
        public void Dispose()=>RequestStop();
        private void Loop()
        {
            string failure=null;
            try
            {
                var clock=Stopwatch.StartNew();double next=0,lastAt=0;
                while(Volatile.Read(ref stopping)==0)
                {
                    if(Volatile.Read(ref routeFailure) is Exception routeError)throw new InvalidOperationException("Certified route worker failed.",routeError);
                    double now=clock.Elapsed.TotalSeconds;
                    if(now<next){wake.WaitOne((int)Math.Min(20,Math.Ceiling((next-now)*1000)));continue;}
                    var timer=Stopwatch.StartNew();bool isPaused=Volatile.Read(ref paused)!=0;
                    if(!authorityTick.AwaitingRoutes){ApplyRequestedBalance(isPaused);domain.SetRallyPaused(isPaused);ApplyCommands(isPaused);}
                    ApplyMatchActions(isPaused);
                    // Publish command-generated geometry before main-thread service admission.
                    PublishNavigationBinding();
                    long revision=Volatile.Read(ref presentationRevision);
                    bool publishWaiting=!authorityTick.AwaitingRoutes||revision!=lastPublishedRevision||isPaused!=lastPublishedPause;
                    if(!authorityTick.TryAdvance(isPaused,snapshotSequence+1,LastHumanSequenceFor,
                        ()=>Metrics(lastCpu,lastAt==0?0:(now-lastAt)*1000),out var snapshot,publishWaiting))
                    {
                        PublishNavigationBinding();
                        Interlocked.Increment(ref barrierPolls);
                        if(snapshot!=null){
                            if(ownerAi!=null)Volatile.Write(ref aiCheckpoint,ownerAi.Checkpoint);
                            if(enemyAi!=null)Volatile.Write(ref enemyAiCheckpoint,enemyAi.Checkpoint);
                            snapshotSequence++;PublishParticipantViews(RuntimeStatus.Running,null,isPaused);Volatile.Write(ref latest,snapshot);
                            lastPublishedRevision=revision;lastPublishedPause=isPaused;Interlocked.Increment(ref barrierPublications);
                        }else Interlocked.Increment(ref barrierReuses);
                        CompleteCapture();
                        wake.WaitOne(1);continue;
                    }
                    snapshotSequence++;
                    Volatile.Write(ref matchResult,domain.Result);
                    foreach(var receipt in domain.DrainRallyReceipts())RecordReceipt(receipt);
                    if(ownerAi!=null)Volatile.Write(ref aiCheckpoint,ownerAi.Checkpoint);
                    if(enemyAi!=null)Volatile.Write(ref enemyAiCheckpoint,enemyAi.Checkpoint);
                    PublishParticipantViews(snapshot.Status,null,isPaused);lastPublishedRevision=revision;lastPublishedPause=isPaused;PublishNavigationBinding();Volatile.Write(ref latest,snapshot);if(completedBalanceStatus!=null){lock(gate){balanceApplyStatus=completedBalanceStatus;requestedBalance=null;requestedAiProfile=null;completedBalanceStatus=null;}}lastCpu=timer.Elapsed.TotalMilliseconds;maxCpu=Math.Max(maxCpu,lastCpu);if(lastCpu>TickSeconds*1000)missedDeadlines++;lastAt=now;CompleteCapture();
                    next+=TickSeconds/Volatile.Read(ref spectatorSpeed);if(next<clock.Elapsed.TotalSeconds)next=clock.Elapsed.TotalSeconds;
                }
            }
            catch(Exception ex){failure=ex.ToString();Interlocked.Increment(ref errors);}
            finally
            {
                domain.CancelAllRally();foreach(var receipt in domain.DrainRallyReceipts())RecordReceipt(receipt);
                aiScheduler.Stop(domain.Tick);if(ownerAi!=null)Volatile.Write(ref aiCheckpoint,ownerAi.Checkpoint);
                enemyAi?.Stop(domain.Tick);if(enemyAi!=null)Volatile.Write(ref enemyAiCheckpoint,enemyAi.Checkpoint);
                lock(gate)
                {
                    stopping=1;captureRequest?.TrySetException(new InvalidOperationException("Runtime stopped before capture."));captureRequest=null;
                    while(inbox.Count>0){var p=inbox.Dequeue();RecordReceipt(new PlayableCommandReceipt(p.Command.Sequence,domain.Tick,PlayableCommandStatus.Cancelled,"Runtime stopped.",0,p.Command.PlayerId));}
                }
                NavigationBinding?.RoutePort?.SignalHost();
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
                try {if(isPaused){status=PlayableCommandStatus.Rejected;message="Paused.";}else status=domain.ApplyWithoutHumanStatistics(p.Command,out message);}
                catch {RecordReceipt(new PlayableCommandReceipt(p.Command.Sequence,domain.Tick,PlayableCommandStatus.Rejected,"Command failed.",latency,p.Command.PlayerId));throw;}
                RecordReceipt(new PlayableCommandReceipt(p.Command.Sequence,domain.Tick,status,message,latency,p.Command.PlayerId));
            }
        }
        private void RecordReceipt(PlayableCommandReceipt receipt){PresentationChanged();lock(gate){receipts.Enqueue(receipt);receiptHistory.Add(receipt);if(receiptHistory.Count>512)receiptHistory.RemoveAt(0);
            if(offlineConfiguration!=null){offlineReceipts.Add(new OfflineReceipt(++receiptOrdinal,receipt));if(offlineReceipts.Count>512)offlineReceipts.RemoveAt(0);}
        }}
        private long LastHumanSequenceFor(string owner){lock(gate)return offlineConfiguration!=null?(acceptedByOwner.TryGetValue(owner,out var value)?value:0):owner==PlayableDomain.PlayerId?lastAcceptedSequence:0;}
        private PlayableRuntimeMetrics Metrics(double cpu,double interval){int backlog;lock(gate)backlog=inbox.Count;return new PlayableRuntimeMetrics(cpu,interval,lastCommandLatency,backlog,Volatile.Read(ref errors),domain.Navigation.PendingCount,missedDeadlines,maxCpu);}
        private static double Timestamp()=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
        private void Signal(){try{wake.Set();}catch(ObjectDisposedException){}}
    }
}
