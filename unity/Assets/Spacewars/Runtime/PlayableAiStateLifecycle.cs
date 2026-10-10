using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using static Spacewars.Runtime.WorldWire;
namespace Spacewars.Runtime
{
    public sealed partial class PlayableAuthorityTick
    {
        private OfflineMatchConfiguration savedConfiguration;
        private string SourceIdentity=>savedConfiguration?.SourceIdentity??PlayableAiOpeningComposition.SourceIdentity;
        internal PlayableDomain Domain=>domain;
        internal AiAuthorityScheduler Scheduler=>scheduler;
        internal long PublicationSequence=>sequence;
        internal IEnumerable<KeyValuePair<string,long>> HumanWatermarks=>humans;
        // Authority caller owns route quiescence. Outstanding external jobs explicitly refuse capture.
        public byte[] CaptureBytes()=>CaptureBytes(sequence,id=>humans.TryGetValue(id,out var value)?value:0);
        internal byte[] CaptureBytes(long publication,Func<string,long> human)
        {
            foreach(var owner in scheduler.Owners)owner.ReconcileBudget(domain);
            var state=domain.CaptureWorldState(seed,SourceIdentity);
            state.AiAuthority=Pack(w=>{
                w.Write(0x41495331);w.Write(AiStateWire.Version);String(w,RouteDeliveryPolicy);w.Write(publication);w.Write(prepared);
                WorldWire.Array(w,domain.OwnerIds.OrderBy(x=>x,StringComparer.Ordinal).ToArray(),id=>{String(w,id);w.Write(human(id));});
                scheduler.WriteState(w);
            });return state.Encode();
        }
        public static PlayableAuthorityTick RestoreBytes(byte[] bytes,OfflineMatchConfiguration config,AiProfile aiProfile=null)
        {
            if(config==null)throw new ArgumentNullException(nameof(config));
            return RestoreBytes(bytes,config.Profile,config.Seed,config.SourceIdentity,config,aiProfile??AiProfile.Initial,config.Roster.Where(p=>p.Control==OfflineControl.Ai).Select(p=>p.Id));
        }
        internal static PlayableAuthorityTick RestoreBytes(byte[] bytes,PlayableProfile profile,int seed,string source,OfflineMatchConfiguration config,AiProfile ai,IEnumerable<string> owners)
        {
            var state=PlayableWorldState.Decode(bytes);
            AiStateWire.Require(state.AiAuthority.Length>0,"missing live AI authority section");
            var d=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,seed,source,config,true);
            return Unpack(state.AiAuthority,r=>{
                AiStateWire.Require(r.ReadInt32()==0x41495331&&r.ReadInt32()==AiStateWire.Version&&String(r)==RouteDeliveryPolicy,"unsupported authority schema/route policy");
                long publication=r.ReadInt64();bool barrier=Boolean(r);AiStateWire.Require(publication>=0,"publication sequence");
                var h=WorldWire.Array(r,()=>new KeyValuePair<string,long>(String(r),r.ReadInt64()));
                AiStateWire.Require(h.Select(p=>p.Key).SequenceEqual(d.OwnerIds.OrderBy(x=>x,StringComparer.Ordinal))&&h.All(p=>p.Value>=0),"human watermarks");
                var scheduler=AiAuthorityScheduler.ReadState(r,profile,ai,state.Generation,seed,state.Tick,owners);
                foreach(var owner in scheduler.Owners)owner.ValidateBudget(d);
                var result=new PlayableAuthorityTick(d,scheduler,seed){sequence=publication,prepared=barrier,savedConfiguration=config};
                foreach(var pair in h)result.humans.Add(pair.Key,pair.Value);
                result.Latest=d.PlayerSnapshot(publication,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,seed);
                return result;
            });
        }
        // Revision changes use the same barrier as commands. Host tuning runs simply do not call this API.
        public string ApplyProfile(PlayableProfile gameplay,AiProfile ai=null)
        {
            if(prepared)return "Navigation barrier in progress.";
            if(gameplay==null)throw new ArgumentNullException(nameof(gameplay));
            var error=domain.ValidateAuthorityBalance(gameplay,true);if(error!=null)return error;
            foreach(var owner in scheduler.Owners)owner.ReconcileBudget(domain);
            domain.ApplyBalance(gameplay);scheduler.Rebind(gameplay);if(ai!=null)scheduler.Rebind(ai);return null;
        }
        public void Stop()=>scheduler.Stop(domain.Tick);
    }
    public sealed partial class PlayableRuntime
    {
        private TaskCompletionSource<byte[]> captureRequest;
        // Trusted local save API. Bytes include all owners' private state; never transport in ParticipantView.
        public Task<byte[]> RequestCaptureBytes()
        {
            lock(gate){if(stopping!=0||stopped!=0)throw new InvalidOperationException("Runtime stopped.");
                if(captureRequest!=null)throw new InvalidOperationException("Capture already pending.");
                captureRequest=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);PresentationChanged();Signal();return captureRequest.Task;}
        }
        internal Action BeforeCaptureSerialization;
        private void CompleteCapture()
        {
            TaskCompletionSource<byte[]> request;
            bool capturedPause;long accepted,ordinal;int capturedOutstanding;
            PlayableCommandReceipt[] queued,history;OfflineReceipt[] offline;
            Dictionary<string,long> watermarks;
            lock(gate){
                if(captureRequest==null)return;
                request=captureRequest;captureRequest=null;
                if(inbox.Count!=0||humanActions.Count!=0||finishRequested!=0||requestedBalance!=null||requestedAiProfile!=null){
                    request.SetException(new InvalidOperationException("Capture requires an empty command/profile ingress barrier."));return;}
                capturedPause=paused!=0;accepted=lastAcceptedSequence;capturedOutstanding=outstanding;ordinal=receiptOrdinal;
                queued=receipts.ToArray();history=receiptHistory.ToArray();offline=offlineReceipts.ToArray();
                watermarks=offlineConfiguration==null?new Dictionary<string,long>{{PlayableDomain.PlayerId,lastAcceptedSequence}}:new Dictionary<string,long>(acceptedByOwner);
            }
            // The single authority is stationary during serialization. Later ingress
            // belongs after this checkpoint; do not read its live watermarks or ledger.
            try{
                BeforeCaptureSerialization?.Invoke();
                var state=PlayableWorldState.Decode(authorityTick.CaptureBytes(snapshotSequence,id=>watermarks.TryGetValue(id,out var value)?value:0));
                state.RuntimeTransport=Pack(w=>{
                    w.Write(1);w.Write(capturedPause);w.Write(accepted);w.Write(capturedOutstanding);w.Write(ordinal);
                    WorldWire.Array(w,queued,x=>WorldWire.Write(w,x));WorldWire.Array(w,history,x=>WorldWire.Write(w,x));
                    WorldWire.Array(w,offline,x=>{w.Write(x.Ordinal);WorldWire.Write(w,x.Receipt);});
                });request.SetResult(state.Encode());
            }catch(Exception ex){request.SetException(ex);}
        }
        public static PlayableRuntime RestoreBytes(byte[] bytes,PlayableProfile profile,int expectedSeed,AiProfile aiProfile=null,bool autonomousOwnerAi=true,bool autonomousEnemyAi=true,bool humanControlledPlayer=false)
        {
            var ai=aiProfile??AiProfile.Initial;var owners=new List<string>();
            if(autonomousOwnerAi&&!humanControlledPlayer)owners.Add(PlayableDomain.PlayerId);if(autonomousOwnerAi&&autonomousEnemyAi)owners.Add("enemy-1");
            return new PlayableRuntime(PlayableAuthorityTick.RestoreBytes(bytes,profile,expectedSeed,PlayableAiOpeningComposition.SourceIdentity,null,ai,owners),bytes,ai,null);
        }
        public static PlayableRuntime RestoreBytes(byte[] bytes,OfflineMatchConfiguration config,AiProfile aiProfile=null)
        {var ai=aiProfile??AiProfile.Initial;return new PlayableRuntime(PlayableAuthorityTick.RestoreBytes(bytes,config,ai),bytes,ai,config);}
        private PlayableRuntime(PlayableAuthorityTick restored,byte[] bytes,AiProfile ai,OfflineMatchConfiguration config)
        {
            var state=PlayableWorldState.Decode(bytes);AiStateWire.Require(state.RuntimeTransport.Length>0,"missing runtime transport state");
            domain=restored.Domain;aiScheduler=restored.Scheduler;authorityTick=restored;offlineConfiguration=config;
            seed=state.Seed;Generation=state.Generation;snapshotSequence=restored.PublicationSequence;NativeAiProfile=ai;NativeAiCatalog=AiRosterCatalog.Initial;
            ownerAi=aiScheduler.Owners.SingleOrDefault(o=>o.OwnerId==PlayableDomain.PlayerId);enemyAi=aiScheduler.Owners.SingleOrDefault(o=>o.OwnerId=="enemy-1");
            openingCompositionAuthority=new PlayableAiOpeningCompositionAuthority();openingCompositionAuthority.Initialize(seed,PlayableDomain.PlayerId,aiProfile:ai);OpeningComposition=ownerAi?.Checkpoint.Opening??PlayableAiOpeningComposition.Initialize(seed,PlayableDomain.PlayerId,aiProfile:ai);
            EnemyAiConfig=new AiOwnerConfig("enemy-1",enemyAi?.Checkpoint.Difficulty??AiDifficulty.Fighter,seed,ai);
            Unpack(state.RuntimeTransport,r=>{
                AiStateWire.Require(r.ReadInt32()==1,"unsupported runtime schema");paused=Boolean(r)?1:0;lastAcceptedSequence=r.ReadInt64();outstanding=r.ReadInt32();receiptOrdinal=r.ReadInt64();
                receipts.Clear();foreach(var receipt in WorldWire.Array(r,()=>ReadPlayableCommandReceipt(r)))receipts.Enqueue(receipt);
                receiptHistory.AddRange(WorldWire.Array(r,()=>ReadPlayableCommandReceipt(r)));
                offlineReceipts.AddRange(WorldWire.Array(r,()=>new OfflineReceipt(r.ReadInt64(),ReadPlayableCommandReceipt(r))));
                AiStateWire.Require(lastAcceptedSequence>=0&&outstanding>=0&&outstanding<=MaxOutstandingCommands&&outstanding==receipts.Count(x=>x.Status!=PlayableCommandStatus.Accepted)+domain.CaptureRallyIntents().Count(i=>restored.HumanWatermarks.Any(h=>h.Value>0&&domain.OwnerFor(h.Key)==i.Owner&&i.Sequence<=h.Value))&&receiptHistory.Count<=512&&offlineReceipts.Count<=512&&receiptOrdinal>=0&&receipts.Concat(receiptHistory).All(x=>x!=null&&x.Sequence>0&&x.AppliedTick<=state.Tick),"runtime receipt ledger");return true;
            });
            foreach(var p in restored.HumanWatermarks)acceptedByOwner.Add(p.Key,p.Value);
            aiCheckpoint=ownerAi?.Checkpoint;enemyAiCheckpoint=enemyAi?.Checkpoint;matchResult=domain.Result;
            PublishNavigationBinding();latest=domain.PlayerSnapshot(snapshotSequence,paused!=0?RuntimeStatus.Paused:RuntimeStatus.Running,paused!=0,Metrics(0,0),null,seed);PublishParticipantViews(latest.Status,null);
            worker=new Thread(Loop){IsBackground=true,Name="Spacewars.Restored.SharedAuthority"};worker.Start();
        }
    }
}
