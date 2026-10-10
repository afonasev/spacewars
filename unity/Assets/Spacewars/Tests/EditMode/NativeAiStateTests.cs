using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiStateTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Get(object o,string name)=>o.GetType().GetField(name,F).GetValue(o);
        private static object[] Owners(PlayableAuthorityTick t)=>((IEnumerable)Get(Get(t,"scheduler"),"owners")).Cast<object>().ToArray();
        private static PlayableAiOwnerCheckpoint Check(object o)=>(PlayableAiOwnerCheckpoint)o.GetType().GetProperty("Checkpoint",F).GetValue(o);
        private static OfflineMatchConfiguration Config(int count=3,PlayableProfile profile=null)
        {
            var c=OfflineParticipantAuthorityTests.Config(count,false);var costs=new double[c.Starts.Count,c.Starts.Count];
            for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)costs[i,j]=i==j?0:1;
            return new OfflineMatchConfiguration(profile??c.Profile,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.Select(p=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,OfflineControl.Ai)).ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),costs,c.Spectators.ToArray());
        }
        // Lifecycle fixtures deliberately return an explicit no-route answer. Movement/NavMesh
        // acceptance belongs to the shared Unity-host fixture, never a synthetic tank solver here.
        private static void Routes(PlayableAuthorityTick t){while(t.Requests.TryDequeue(out var r))Assert.True(t.Answers.TryEnqueue(new NavigationAnswer(r,Array.Empty<NavPoint>())));}
        private static void Step(PlayableAuthorityTick t){Routes(t);if(!t.TryAdvance()){Routes(t);Assert.True(t.TryAdvance());}}
        private static void Run(PlayableAuthorityTick t,int ticks){for(int i=0;i<ticks;i++)Step(t);}
        private static PlayableProfile Changed(PlayableProfile p)
        {var d=p.CopyData();d.revision++;d.factoryCreditCost+=50;d.tankCreditCost+=10;return PlayableProfile.Create(d,p.AuthoredMap,"F6 revised");}
        private static AiProfile ChangedAi(){var d=AiProfile.Initial.CopyData();d.revision++;return new AiProfile(d);}

        [TestCase(2)][TestCase(3)][TestCase(6)] public void Roundtrip(int count)
        {
            var c=Config(count);var a=new PlayableAuthorityTick(c,71,difficulty:AiDifficulty.Veteran);Run(a,15);
            Assert.True(Owners(a).All(o=>Check(o).PendingActionId>0));
            Assert.Greater(Owners(a).Sum(o=>((IList)Get(o,"pendingItems")).Count),count,"All concurrent pending intents must be represented.");
            var bytes=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes((byte[])bytes.Clone(),c);
            Assert.True(bytes.SequenceEqual(b.CaptureBytes()));
            for(int i=0;i<900;i++){Step(a);Step(b);Assert.True(a.CaptureBytes().SequenceEqual(b.CaptureBytes()),"suffix tick "+i);}
            Assert.True(Owners(b).All(o=>Check(o).Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied)));
            foreach(var o in Owners(b))Assert.True(Check(o).Opening.SemanticallyEquals(Check(Owners(a).Single(p=>Check(p).OwnerId==Check(o).OwnerId)).Opening));
        }
        [Test] public void Pause()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);Run(a,45);
            var before=Owners(a).Select(o=>Check(o)).ToArray();Assert.True(before.All(o=>o.PendingActionId>0));
            for(int i=0;i<300;i++)Assert.True(a.TryAdvance(paused:true));
            Assert.AreEqual(45,a.Tick);
            var after=Owners(a).Select(o=>Check(o)).ToArray();
            for(int i=0;i<after.Length;i++){Assert.AreEqual(before[i].DecisionTick,after[i].DecisionTick);Assert.AreEqual(before[i].Strategy.ReviewedTick,after[i].Strategy.ReviewedTick);Assert.Zero(after[i].PendingActionId);Assert.AreEqual(before[i].Records.Count(r=>r.Status==PlayableAiDeliveryStatus.Scheduled),after[i].Records.Count(r=>r.Status==PlayableAiDeliveryStatus.Cancelled));}
            var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);for(int i=0;i<80;i++){Step(a);Step(b);}Assert.True(a.CaptureBytes().SequenceEqual(b.CaptureBytes()));
        }
        [Test] public void Restart()
        {
            var c=Config();var old=new PlayableAuthorityTick(c,71);Run(old,45);var stale=Owners(old).Select(o=>Check(o).Records.Single(r=>r.Status==PlayableAiDeliveryStatus.Scheduled&&r.Policy=="economy")).ToArray();old.Stop();
            var fresh=new PlayableAuthorityTick(c,72);Run(fresh,45);
            foreach(var o in Owners(fresh))
            {
                var policy=Get(o,"economy");Assert.True((bool)policy.GetType().GetProperty("HasPendingObligation").GetValue(policy));
                var r=stale.Single(x=>x.OwnerId==Check(o).OwnerId);
                policy.GetType().GetMethod("ObserveReceipt").Invoke(policy,new object[]{new PlayableAiTraceRecord(r.ObservationIdentity,1,r.DueTick,0,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"late generation callback",r.OwnerId,r.SourceIdentity,r.ReceiptIdentity)});
                Assert.True((bool)policy.GetType().GetProperty("HasPendingObligation").GetValue(policy),"late callback cannot clear new generation local ID 1");
            }
            Assert.AreEqual(PlayableCommandStatus.StaleGeneration,fresh.Apply(new PlayableCommand(71,99,c.Roster[0].Id,PlayableCommandKind.Stop,Array.Empty<int>())).Status);
            Assert.True(Owners(fresh).All(o=>Check(o).Generation==72&&Check(o).Records.All(r=>r.ReceiptIdentity.Generation==72)));
        }
        [Test] public void ProfileChange()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);Run(a,76);
            Assert.True(Owners(a).All(o=>Check(o).Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied)));
            Run(a,14);Assert.True(Owners(a).All(o=>Check(o).PendingActionId>0));
            var before=Owners(a).Select(Check).ToArray();var next=Changed(c.Profile);var ai=ChangedAi();var state=PlayableWorldState.Decode(a.CaptureBytes());
            Assert.IsNull(a.ApplyProfile(next,ai));
            var after=Owners(a).Select(Check).ToArray();
            for(int i=0;i<after.Length;i++){Assert.Zero(after[i].PendingActionId);Assert.True(before[i].Opening.SemanticallyEquals(after[i].Opening));Assert.AreEqual(before[i].Strategy.Strategy,after[i].Strategy.Strategy);Assert.AreEqual(before[i].Strategy.DecisionSequence,after[i].Strategy.DecisionSequence);Assert.AreEqual(next.Revision,after[i].Strategy.ProfileRevision);Assert.AreEqual(ai.Hash,after[i].AiHash);Assert.AreEqual(1,after[i].Records.Count(r=>r.Status==PlayableAiDeliveryStatus.Cancelled));}
            var updated=Config(profile:next);var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),updated,ai);
            for(int i=0;i<100;i++){Step(a);Step(b);}Assert.True(a.CaptureBytes().SequenceEqual(b.CaptureBytes()));
            // Historical paid terms remain in the domain registry and building transactions.
            var domain=Get(a,"domain");var termProfiles=(IDictionary)Get(domain,"termProfiles");Assert.True(termProfiles.Contains(c.Profile.Revision));Assert.True(termProfiles.Contains(next.Revision));
            var buildings=((IEnumerable)Get(domain,"buildings").GetType().GetProperty("Values").GetValue(Get(domain,"buildings"))).Cast<object>().ToArray();
            Assert.True(buildings.Any(x=>(int)x.GetType().GetField("TermsRevision").GetValue(x)==c.Profile.Revision&&(int)x.GetType().GetField("PaidCost").GetValue(x)==c.Profile.FactoryCreditCost));
        }
        [Test] public void StrictSchemaAndProfilesRefuseWithoutMutation()
        {
            var c=Config();var t=new PlayableAuthorityTick(c,71);Run(t,45);var bytes=t.CaptureBytes();
            Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(bytes,Config(profile:Changed(c.Profile))));
            Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(bytes,c,ChangedAi()));
            var state=PlayableWorldState.Decode(bytes);state.Version--;Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(state.Encode(),c));
            state=PlayableWorldState.Decode(bytes);state.AiAuthority[4]=99;Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(state.Encode(),c));
            state=PlayableWorldState.Decode(bytes);state.AiAuthority=Array.Empty<byte>();Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(state.Encode(),c));
            Assert.Throws<ArgumentException>(()=>OfflineParticipantAuthority.Restore(bytes,c));
            CollectionAssert.AreEqual(bytes,t.CaptureBytes());
        }
        [TestCase("PersonalitySeed")][TestCase("ProfileIdentity")][TestCase("SourceIdentity")]
        public void RestoreRejectsCorruptOrHistoricalOpeningBinding(string property)
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);Run(a,45);
            var opening=Check(Owners(a)[0]).Opening;var field=typeof(PlayableAiOpeningCompositionState).GetField("<"+property+">k__BackingField",F);
            object bad=property=="PersonalitySeed"?(object)(opening.PersonalitySeed^1u):property=="ProfileIdentity"?"adaptive-strategic-ai-v1@release:8":"source:96a32f63:ai-release-c569e3a03045";
            // The serialized loop owns the same immutable opening object; deliberately corrupt
            // its backing field in this diagnostic fixture, then recompute world checksums.
            field.SetValue(Get(Owners(a)[0],"opening"),bad);
            var bytes=a.CaptureBytes();Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(bytes,c));
        }
        [Test] public void AuthorityAndGlobalAllocatorsContinueBeyondHumanWatermark()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);long high=1L<<62;
            var own=a.Latest.Entities.Single(e=>e.Owner==a.Latest.Owner);Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,high,c.Roster[0].Id,PlayableCommandKind.Stop,new[]{own.Id})).Status);
            Run(a,45);var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);Run(a,30);Run(b,30);
            var records=Owners(b).SelectMany(o=>Check(o).Records).ToArray();var applied=records.Where(r=>r.Status==PlayableAiDeliveryStatus.Applied).ToArray();Assert.True(applied.All(r=>r.CommandSequence>high));Assert.AreEqual(applied.Length,applied.Select(r=>r.CommandSequence).Distinct().Count());
            Run(a,60);Run(b,60);Assert.True(a.CaptureBytes().SequenceEqual(b.CaptureBytes()));
            var scheduled=Owners(b).SelectMany(o=>Check(o).Records).Where(r=>r.Status==PlayableAiDeliveryStatus.Scheduled).ToArray();Assert.AreEqual(scheduled.Length,scheduled.Select(r=>r.ActionId).Distinct().Count());Assert.Greater(scheduled.Max(r=>r.ReceiptIdentity.DecisionOrdinal),1);
        }
        [Test] public void ArbiterRestoresAgingAndRetryExhaustion()
        {
            AiIntent Intent(string id,int priority=0)=>new AiIntent(id,"test",new PlayableAiAction(1,"a","p",1,71,1,PlayableCommandKind.Stop),100000,0,0,Array.Empty<string>(),priority);
            var a=new AiDecisionArbiter(AiProfile.Initial);var rejected=Intent("reject",2);var useful=Intent("useful");
            int limit=(int)AiProfile.Initial.Value("decision.retryLimit");long retry=AiProfile.SecondsToTicks(AiProfile.Initial.Value("decision.retrySeconds"),30);
            for(int i=0;i<limit;i++){a.Select(new[]{rejected,useful},i*retry,0,0,1);a.Terminal(rejected,i*retry,PlayableAiDeliveryStatus.Rejected);}
            var b=new AiDecisionArbiter(AiProfile.Initial);using(var stream=new MemoryStream())
            {using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))typeof(AiDecisionArbiter).GetMethod("WriteState",F).Invoke(a,new object[]{w});stream.Position=0;using(var r=new BinaryReader(stream))typeof(AiDecisionArbiter).GetMethod("ReadState",F).Invoke(b,new object[]{r,limit*retry});}
            long aging=AiProfile.SecondsToTicks(AiProfile.Initial.Value("decision.intentAgingSeconds"),30);
            CollectionAssert.AreEqual(a.Select(new[]{rejected,Intent("micro",1),useful},aging,0,0,1).Select(i=>i.Id),b.Select(new[]{rejected,Intent("micro",1),useful},aging,0,0,1).Select(i=>i.Id));Assert.AreEqual("useful",b.Select(new[]{rejected,Intent("micro",1),useful},aging,0,0,1).Single().Id);
        }
        [Test] public void SnapshotPrivacyDoesNotPublishAuthorityState()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);Run(a,45);
            Assert.IsEmpty(typeof(PlayableSnapshot).GetProperties().Where(p=>p.PropertyType==typeof(PlayableAiOwnerCheckpoint)||p.Name.Contains("AiAuthority")));
            Assert.IsEmpty(typeof(PlayableAiObservation).GetProperties().Where(p=>p.PropertyType==typeof(PlayableAiOwnerCheckpoint)||p.Name.Contains("AiAuthority")));
            Assert.True(a.Latest.Buildings.Where(b=>b.Owner!=a.Latest.Owner).All(b=>b.PrivateState==null));
            Assert.Greater(PlayableWorldState.Decode(a.CaptureBytes()).AiAuthority.Length,0);
        }
        private static void Stop(PlayableRuntime r){r.RequestStop();Assert.True(SpinWait.SpinUntil(()=>r.IsStopped,5000));}
        [Test] public void RuntimeRestoresUnreadAcceptedAndPendingRallyReceipts()
        {
            var c=Config(2);var owner=c.Roster[0];var start=c.Starts[c.Assignments[0]];var slot=c.Sites.Single(x=>x.Id==start.SiteId).Slots.First();
            var costs=new double[c.Starts.Count,c.Starts.Count];for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)costs[i,j]=c.RouteCost(i,j);
            var config=new OfflineMatchConfiguration(c.Profile,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.Select(p=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,OfflineControl.Human)).ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),costs,scenarioBuildings:new[]{new OfflineScenarioBuilding(owner.LogicalPlayer,PlayableBuildingKind.Factory,start.SiteId,slot.Id,slot.Position,slot.Heading)});
            var a=new PlayableRuntime(config,71,startPaused:true);PlayableRuntime b=null;
            try
            {
                Assert.True(SpinWait.SpinUntil(()=>a.Latest.Paused,5000));int factory=a.ParticipantView(owner.Id).Buildings.Single(x=>x.Kind==PlayableBuildingKind.Factory).Id;
                a.RequestPause(false);Assert.AreEqual(PlayableCommandStatus.Accepted,a.TrySubmit(new PlayableCommand(71,1,owner.Id,PlayableCommandKind.SetRally,new[]{factory},new NavPoint(0,10))).Status);
                Assert.True(SpinWait.SpinUntil(()=>a.ReceiptsAfter(0).Any(r=>r.Status==PlayableCommandStatus.Accepted)&&a.Requests.Count>0,5000));
                var save=a.RequestCaptureBytes();Assert.True(save.Wait(5000));b=PlayableRuntime.RestoreBytes(save.Result,config);
                Assert.True(b.ReceiptsAfter(0).Any(r=>r.Sequence==1&&r.Status==PlayableCommandStatus.Accepted));
                Assert.AreEqual(1,b.DrainReceipts().Count(r=>r.Sequence==1&&r.Status==PlayableCommandStatus.Accepted));
                b.RequestPause(true);Assert.True(SpinWait.SpinUntil(()=>b.Latest.Paused,5000));
                Assert.AreEqual(0,b.Latest.Metrics.Errors);
            }
            finally{Stop(a);if(b!=null)Stop(b);}
        }
        [Test] public void RuntimeAiRevisionWaitsForResumeAndPreservesSeed()
        {
            var r=new PlayableRuntime(PlayableProfile.Default,71,19092026,startPaused:true,humanControlledPlayer:true);
            try
            {
                Assert.True(SpinWait.SpinUntil(()=>r.Latest.Paused,5000));var original=r.Latest.Seed;var next=ChangedAi();
                Assert.IsNull(r.RequestAiProfile(next,71,AiProfile.Initial.Revision));Thread.Sleep(80);
                Assert.AreEqual(AiProfile.Initial.Hash,r.NativeAiProfile.Hash);Assert.AreEqual(0,r.Latest.Tick);
                r.RequestPause(false);Assert.True(SpinWait.SpinUntil(()=>r.NativeAiProfile.Hash==next.Hash,5000));
                Assert.AreEqual(original,r.Latest.Seed);Assert.AreEqual(next.Hash,r.NativeAiProfile.Hash);
                Assert.AreEqual(AiProfile.Initial.Revision+1,r.NativeAiProfile.Revision);
            }
            finally{Stop(r);}
        }
        [TestCase(false)][TestCase(true)] public void RuntimeSaveRestoreUsesSameLifecycle(bool offline)
        {
            var c=Config();var a=offline?new PlayableRuntime(c,71,startPaused:true):new PlayableRuntime(PlayableProfile.Default,71,c.Seed,startPaused:true,humanControlledPlayer:true);PlayableRuntime b=null;
            try
            {
                Assert.True(SpinWait.SpinUntil(()=>a.Latest.Paused,5000));var task=a.RequestCaptureBytes();Assert.True(task.Wait(5000));
                b=offline?PlayableRuntime.RestoreBytes(task.Result,c):PlayableRuntime.RestoreBytes(task.Result,PlayableProfile.Default,c.Seed,humanControlledPlayer:true);
                Assert.True(SpinWait.SpinUntil(()=>b.Latest.Paused,5000));Assert.AreEqual(a.Generation,b.Generation);Assert.AreEqual(a.Latest.Tick,b.Latest.Tick);
                Assert.True(offline?b.OfflineFrame.Views.Count==c.Roster.Count:b.EnemyAiConfig.OwnerId=="enemy-1");
                if(offline)foreach(var p in c.Roster)Assert.AreEqual(a.ParticipantView(p.Id).Credits,b.ParticipantView(p.Id).Credits);
            }
            finally{Stop(a);if(b!=null)Stop(b);}
        }
    }
}
