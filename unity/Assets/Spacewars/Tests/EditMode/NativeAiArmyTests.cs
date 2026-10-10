using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Headless;
using Spacewars.Presentation;
using Newtonsoft.Json;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiArmyTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static object Field(object o,string key)=>o.GetType().GetField(key,F).GetValue(o);
        private static PlayableAiObservation Observation(int tanks=30,int scouts=4,long tick=10,long generation=71,bool foreign=false)
        {
            var p=PlayableProfile.Default;
            var entities=Enumerable.Range(1,tanks).Select(id=>new PlayableEntitySnapshot(id,foreign?PlayableOwner.Enemy:PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(id,0),100,false,0,0,0)).Concat(Enumerable.Range(101,scouts).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(id,0),100,false,0,0,0))).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,generation,7,1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,100,null,entities,Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
        }
        private static AiArmyRegistry Registry(AiDifficulty difficulty=AiDifficulty.Fighter)=>new AiArmyRegistry("player-1",71,AiProfile.Initial,difficulty);
        private static AiIntent Intent(PlayableAiObservation o,string policy,PlayableCommandKind kind,int[] ids,string suffix="")=>new AiIntent(policy+suffix,policy,new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,kind,ids,new NavPoint(10,10)),100,0,0,ids.Select(x=>"recipient:"+x));
        [TestCase(AiDifficulty.Recruit,2)][TestCase(AiDifficulty.Fighter,3)][TestCase(AiDifficulty.Veteran,4)]
        public void Registry_CapAndStableIdsIncludeDefenseEscortAndAlliedSupport(AiDifficulty difficulty,int cap)
        {
            var r=Registry(difficulty);var o=Observation();var roles=new[]{AiArmyRole.Attack,AiArmyRole.MobileDefense,AiArmyRole.Escort,AiArmyRole.AlliedSupport,AiArmyRole.Raid};
            for(int i=0;i<cap;i++){Assert.True(r.TryCreate(o,roles[i],"mission-defense",Enumerable.Range(i*3+1,3),out var id));Assert.AreEqual(i+1,id);}
            Assert.False(r.TryCreate(o,roles[cap],"mission-defense",Enumerable.Range(cap*3+1,3),out _));
            var before=r.Capture();for(int i=0;i<20;i++)r.Observe(Observation(tick:11+i));CollectionAssert.AreEqual(before.Armies.Select(a=>a.Id),r.Capture().Armies.Select(a=>a.Id));Assert.AreEqual(cap,r.MajorCap);r.AssertInvariants();
        }
        [Test] public void Registry_SmallForceRejectsFragmentationAtomically()
        {
            var r=Registry();var o=Observation(tanks:5);Assert.True(r.TryCreate(o,AiArmyRole.Attack,"attack",Enumerable.Range(1,3),out var first));
            Assert.False(r.TryCreate(o,AiArmyRole.Raid,"raid",new[]{4,5},out _));Assert.Zero(r.ArmyFor(4));Assert.AreEqual(2,r.Capture().NextId);
            Assert.False(r.TryCreate(o,AiArmyRole.Escort,"expansion",new[]{3,4,5},out _));Assert.AreEqual(first,r.ArmyFor(3));Assert.Zero(r.ArmyFor(5));
        }
        [Test] public void Registry_AtomicTransferKeepsBothGroupsViable()
        {
            var r=Registry();var o=Observation();Assert.True(r.TryCreate(o,AiArmyRole.Attack,"attack",Enumerable.Range(1,4),out var a));Assert.True(r.TryCreate(o,AiArmyRole.MobileDefense,"defense",Enumerable.Range(5,3),out var b));
            Assert.False(r.TryTransfer(o,a,b,new[]{1,2}));Assert.AreEqual(a,r.ArmyFor(1));Assert.AreEqual(a,r.ArmyFor(2));
            Assert.True(r.TryTransfer(o,a,b,new[]{1}));Assert.AreEqual(b,r.ArmyFor(1));Assert.AreEqual(3,r.Capture().Armies.Single(x=>x.Id==a).Members.Length);r.AssertInvariants();
        }
        [Test] public void Registry_OneTacticalOwnerWinsDefenseAttackArtilleryExpansion()
        {
            var r=Registry();var o=Observation();Assert.True(r.TryCreate(o,AiArmyRole.Attack,"attack",new[]{1,2,3},out var army));
            Assert.True(r.TryClaim(army,"attack","defense"));Assert.False(r.TryClaim(army,"attack","artillery"));
            var candidates=new[]{"defense","attack","artillery","expansion"}.Select(p=>Intent(o,p,PlayableCommandKind.AttackMove,new[]{1,2,3})).ToArray();
            var arbiter=new AiDecisionArbiter(AiProfile.Initial);var selected=arbiter.Select(candidates,o.Tick,100,100,4,null,r);Assert.AreEqual("defense",selected.Single().Policy);
            foreach(var p in new[]{"attack","artillery","expansion"})Assert.AreEqual("unit tactical owner conflict",arbiter.Rejections[p]);
            var reverse=new AiDecisionArbiter(AiProfile.Initial).Select(candidates.Reverse(),o.Tick,100,100,4,null,r);CollectionAssert.AreEqual(selected.Select(x=>x.Id),reverse.Select(x=>x.Id));
        }
        [TestCase(AiArmyRole.Scout)][TestCase(AiArmyRole.Transport)] public void Registry_SpecialAssignmentsCannotBypassCombatCap(AiArmyRole role)
        {
            var r=Registry();var o=Observation();Assert.False(r.TryCreate(o,role,"scout",new[]{1},out _));Assert.True(r.TryCreate(o,role,"scout",new[]{101},out _));
            Assert.IsNull(r.Reject(Intent(o,"scout",PlayableCommandKind.Move,new[]{101})));Assert.IsNotNull(r.Reject(Intent(o,"scout",PlayableCommandKind.Attack,new[]{101})));Assert.IsNotNull(r.Reject(Intent(o,"scout",PlayableCommandKind.AttackMove,new[]{101})));Assert.IsNotNull(r.Reject(Intent(o,"scout",PlayableCommandKind.Follow,new[]{101})));
        }
        [Test] public void Registry_ScoutCapAndCatalogForceRejectReconOnlyMajor()
        {
            var r=Registry(AiDifficulty.Recruit);var o=Observation();Assert.True(r.TryCreate(o,AiArmyRole.Scout,"scout",new[]{101},out _));Assert.False(r.TryCreate(o,AiArmyRole.Scout,"scout",new[]{102},out _));Assert.False(r.TryCreate(o,AiArmyRole.Attack,"attack",new[]{102,103,104},out _));Assert.False(r.TryCreate(o,AiArmyRole.Attack,"attack",new[]{1,102,103},out _));
        }
        [Test] public void Registry_ForeignAndStaleClaimsNeverMutateMembership()
        {
            var r=Registry();Assert.False(r.TryCreate(Observation(foreign:true),AiArmyRole.Attack,"attack",new[]{1,2,3},out _));Assert.False(r.TryCreate(Observation(generation:72),AiArmyRole.Attack,"attack",new[]{1,2,3},out _));Assert.AreEqual(1,r.Capture().NextId);Assert.IsEmpty(r.Capture().Armies);
        }
        [Test] public void Registry_LossDisbandReleasesMembershipWithoutReusingId()
        {
            var r=Registry();Assert.True(r.TryCreate(Observation(),AiArmyRole.Attack,"attack",new[]{1,2,3},out var first));r.Observe(Observation(tanks:0));Assert.Zero(r.ArmyFor(1));Assert.AreEqual(AiArmyPhase.Disbanded,r.Capture().Armies.Single().Phase);
            Assert.True(r.TryCreate(Observation(),AiArmyRole.Attack,"attack",new[]{1,2,3},out var next));Assert.Greater(next,first);r.Disband(next);Assert.Zero(r.ArmyFor(1));r.AssertInvariants();
        }
        [Test] public void Registry_RestoreRejectsCorruptClaimsAndDetachedCopiesDoNotMutate()
        {
            var r=Registry();Assert.True(r.TryCreate(Observation(),AiArmyRole.Attack,"attack",new[]{1,2,3},out var id));var copy=r.Capture();copy.Armies[0].Members[0]=99;Assert.AreEqual(id,r.ArmyFor(1));
            var valid=r.Capture();var restored=AiArmyRegistry.Restore(valid,AiProfile.Initial,AiDifficulty.Fighter,10);Assert.AreEqual(id,restored.ArmyFor(1));valid.Armies[0].Members=new[]{1,1,3};Assert.Throws<InvalidDataException>(()=>AiArmyRegistry.Restore(valid,AiProfile.Initial,AiDifficulty.Fighter,10));
            valid=r.Capture();valid.NextId=id;Assert.Throws<InvalidDataException>(()=>AiArmyRegistry.Restore(valid,AiProfile.Initial,AiDifficulty.Fighter,10));valid=r.Capture();valid.Armies[0].ProgressTick=11;Assert.Throws<InvalidDataException>(()=>AiArmyRegistry.Restore(valid,AiProfile.Initial,AiDifficulty.Fighter,10));
        }
        [Test] public void Registry_ProfileBarrierPreservesValidIdsAndReleasesExcess()
        {
            var r=Registry();var o=Observation();for(int i=0;i<3;i++)Assert.True(r.TryCreate(o,AiArmyRole.Attack,"attack",Enumerable.Range(i*3+1,3),out _));r.Rebind(AiProfile.Initial,AiDifficulty.Recruit);Assert.AreEqual(1,r.ArmyFor(1));Assert.AreEqual(2,r.ArmyFor(4));Assert.Zero(r.ArmyFor(7));Assert.AreEqual(AiArmyPhase.Disbanded,r.Capture().Armies.Single(a=>a.Id==3).Phase);r.AssertInvariants();
        }
        [TestCase(AiArmyRole.Scout)][TestCase(AiArmyRole.Transport)] public void Registry_StrictWorldRestoreRejectsCombatSpecialRole(AiArmyRole role)
        {
            var c=OfflineParticipantAuthorityTests.Config(2,false);
            c=new OfflineMatchConfiguration(c.Profile,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.Select(p=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,OfflineControl.Ai)).ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),new double[c.Starts.Count,c.Starts.Count],c.Spectators.ToArray(),scenario:Enumerable.Range(0,3).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-12+i*3,-8))).ToArray());
            var a=new PlayableAuthorityTick(c,71);var r=(AiArmyRegistry)Field(Owners(a)[0],"armies");var o=PlayableAiObservation.From(a.ParticipantView(r.OwnerId));var ids=o.Entities.Where(e=>e.Owner==o.Owner&&e.Kind==PlayableEntityKind.Tank).Select(e=>e.Id).ToArray();Assert.AreEqual(3,ids.Length);
            Assert.True(r.TryCreate(o,AiArmyRole.Attack,"attack",ids,out var id));
            var map=(System.Collections.Generic.Dictionary<long,AiArmyState>)Field(r,"armies");map[id].Role=role;
            // Corrupt the actual authority section, retaining real combat unit IDs.
            var bytes=a.CaptureBytes();var error=Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(bytes,c));StringAssert.Contains("army scout/transport world classification",error.Message);
        }
        [TestCase(true)][TestCase(false)] public void Registry_StrictReinforcementCanonicalRestore(bool duplicate)
        {
            var r=Registry();Assert.True(r.TryCreate(Observation(),AiArmyRole.Attack,"attack",new[]{1,2,3},out _));
            var state=r.Capture();state.Armies[0].Reinforcements=new[]{1,2};
            Assert.DoesNotThrow(()=>AiArmyRegistry.Restore(state,AiProfile.Initial,AiDifficulty.Fighter,10));
            state.Armies[0].Reinforcements=duplicate?new[]{1,1}:new[]{2,1};
            Assert.Throws<InvalidDataException>(()=>AiArmyRegistry.Restore(state,AiProfile.Initial,AiDifficulty.Fighter,10));
        }
        [Test] public void Registry_ValidMajorRemnantRestoresAfterRealLossObservation()
        {
            var r=Registry();Assert.True(r.TryCreate(Observation(),AiArmyRole.Attack,"attack",new[]{1,2,3},out var id));r.Observe(Observation(tanks:1));var restored=AiArmyRegistry.Restore(r.Capture(),AiProfile.Initial,AiDifficulty.Fighter,10);Assert.AreEqual(id,restored.ArmyFor(1));Assert.AreEqual(1,restored.Capture().Armies.Single().Members.Length);
        }
        private static void NativeStep(PlayableAuthorityTick a,UnityHostRouteService routes)
        {for(int i=0;!a.TryAdvance();i++){Assert.Less(i,4096);routes.Service(a,64);}}
        [TestCase("transfer")][TestCase("new-assignment")][TestCase("unchanged")]
        public void Registry_AcceptedPendingRechecksAtomicOwnershipAndRestore(string change)
        {
            var manifest=JsonConvert.DeserializeObject<MatchManifest>(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures/e6-lost-hq/manifest.json")));
            var c=HeadlessFixtures.Create(manifest);var a=new PlayableAuthorityTick(c,manifest.Generation);var owner=Owners(a).Single();var registry=(AiArmyRegistry)Field(owner,"armies");object pending=null;
            using(var routes=new UnityHostRouteService())
            {
                for(int i=0;i<700&&pending==null;i++)
                {
                    NativeStep(a,routes);var view=a.ParticipantView(registry.OwnerId);
                    pending=((IEnumerable)Field(owner,"pendingItems")).Cast<object>().FirstOrDefault(item=>{var action=(PlayableAiAction)Field(item,"Action");return action.Kind==PlayableCommandKind.Move&&action.EntityIds.Count==1&&view.Entities.Any(e=>e.Id==action.EntityIds[0]&&e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Explorer);});
                }
                Assert.NotNull(pending,"Actual owner must schedule a detached unit Move");
                var old=(PlayableAiAction)Field(pending,"Action");string policy=(string)Field(pending,"PolicyName");var identity=(AiReceiptIdentity)Field(pending,"Identity");var observation=PlayableAiObservation.From(a.ParticipantView(registry.OwnerId));
                Assert.True(a.CaptureDiagnosticBudgets().Single().Unpaid.Any(e=>e.Receipt.Id==identity.Id),"Accepted pending claim must exist before reassignment");
                Assert.True(registry.TryCreate(observation,AiArmyRole.Transport,change=="new-assignment"?"new-owner":policy,old.EntityIds,out var army));
                if(change=="transfer")Assert.True(registry.TryClaim(army,policy,"new-owner"));
                var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);long due=(long)Field(pending,"DueTick");
                while(a.Tick<=due+1){NativeStep(a,routes);NativeStep(restored,routes);ExactByteAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());}
                var terminal=a.CaptureDiagnosticCheckpoints().Single().Records.Single(r=>r.ActionId==old.ActionId&&r.Status!=PlayableAiDeliveryStatus.Scheduled);
                Assert.AreEqual(change=="unchanged"?PlayableAiDeliveryStatus.Applied:PlayableAiDeliveryStatus.Cancelled,terminal.Status);
                if(change!="unchanged"){StringAssert.Contains("unit tactical owner conflict",terminal.Message);Assert.Zero(terminal.CommandSequence);Assert.AreEqual(PlayableCommandStatus.Rejected,terminal.RuntimeStatus);}else Assert.Greater(terminal.CommandSequence,0);
                Assert.False(a.CaptureDiagnosticBudgets().Single().Unpaid.Any(e=>e.Receipt.Id==identity.Id));
                Assert.AreEqual(a.CaptureDiagnosticBudgets().Single().PaidTotal,restored.CaptureDiagnosticBudgets().Single().PaidTotal);
                restored.Stop();a.Stop();
            }
        }
        [TestCase(true)][TestCase(false)] public void Registry_TypedDtoRestoreRejectsNonFiniteNavigationPoints(bool anchor)
        {
            var registry=Registry();Assert.True(registry.TryCreate(Observation(),AiArmyRole.Attack,"attack",new[]{1,2,3},out _));var state=registry.Capture();
            if(anchor)state.Armies[0].Anchor=new NavPoint(double.NaN,0);else state.Armies[0].Rally=new NavPoint(0,double.PositiveInfinity);
            Assert.Throws<InvalidDataException>(()=>AiArmyRegistry.Restore(state,AiProfile.Initial,AiDifficulty.Fighter,10));
        }
        private static object[] Owners(PlayableAuthorityTick a)=>((IEnumerable)Field(Field(a,"scheduler"),"owners")).Cast<object>().ToArray();
        private static void Step(PlayableAuthorityTick a){while(a.Requests.TryDequeue(out var request))Assert.True(a.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>())));if(!a.TryAdvance()){while(a.Requests.TryDequeue(out var request))Assert.True(a.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>())));Assert.True(a.TryAdvance());}}
        [Test] public void Registry_ActualAuthorityRoundtripPauseRestartAndProfileBarrier()
        {
            var c=OfflineParticipantAuthorityTests.Config(2,false);c=new OfflineMatchConfiguration(c.Profile,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.Select(p=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,OfflineControl.Ai)).ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),new double[c.Starts.Count,c.Starts.Count],c.Spectators.ToArray());
            var a=new PlayableAuthorityTick(c,71);var owner=Owners(a)[0];var r=(AiArmyRegistry)Field(owner,"armies");var o=PlayableAiObservation.From(a.ParticipantView(r.OwnerId));var unit=o.Entities.First(e=>e.Owner==o.Owner&&e.Kind==PlayableEntityKind.Explorer);
            Assert.True(r.TryCreate(o,AiArmyRole.Transport,"expansion",new[]{unit.Id},out var id));
            for(int i=0;i<10;i++)Step(a);var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c);ExactByteAssert.AreEqual(bytes,restored.CaptureBytes());
            for(int i=0;i<40;i++){Step(a);Step(restored);ExactByteAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());}
            var before=r.Capture().Armies.Single().ProgressTick;for(int i=0;i<30;i++)Assert.True(a.TryAdvance(paused:true));Assert.AreEqual(before,r.Capture().Armies.Single().ProgressTick);
            var data=AiProfile.Initial.CopyData();data.revision++;var next=new AiProfile(data);Assert.IsNull(a.ApplyProfile(c.Profile,next));Assert.AreEqual(id,r.ArmyFor(unit.Id));var after=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,next);ExactByteAssert.AreEqual(a.CaptureBytes(),after.CaptureBytes());
            a.Stop();Assert.Zero(r.ArmyFor(unit.Id));Assert.AreEqual(AiArmyPhase.Disbanded,r.Capture().Armies.Single().Phase);
            var fresh=new PlayableAuthorityTick(c,72);Assert.True(Owners(fresh).All(x=>((AiArmyRegistry)Field(x,"armies")).Capture().Armies.Length==0));
        }
    }
}
