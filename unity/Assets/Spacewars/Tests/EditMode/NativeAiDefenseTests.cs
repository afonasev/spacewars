using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiInitiativeTests
    {
        private static PlayableSnapshot DefenseView(long tick,bool threat,int count=6,bool proof=true)
        {
            var o=Observation(tick,count:count,target:threat?101:0,proof:proof);
            var center=new PlayableBuildingSnapshot(60,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(0,0),P.HeadquartersHealth,1,0,0,default);
            return new PlayableSnapshot(P.ProfileId,P.Revision,71,o.Seed,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,o.Entities.ToArray(),o.Buildings.Concat(new[]{center}).ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:o.Vision,routeProofs:o.RouteProofs.ToArray());
        }
        [Test] public void DefenseRecovery_PreemptsAcceptedOffenseBeforeItsDueTick()
        {
            var type=typeof(AiArmyPlanner).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop");
            var loop=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{P,Opening(),71L,null},null);
            void Review(PlayableSnapshot s)=>type.GetMethod("Review",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(loop,new object[]{s,0L});
            var o=Observation(60,count:6);var noCenter=new PlayableSnapshot(P.ProfileId,P.Revision,71,o.Seed,60,60,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,o.Entities.ToArray(),o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:o.Vision,routeProofs:o.RouteProofs.ToArray());
            Review(noCenter);
            var checkpoint=(PlayableAiOwnerCheckpoint)type.GetProperty("Checkpoint",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(loop);
            Assert.True(checkpoint.Records.Any(r=>r.Policy==AiArmyPlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Scheduled));
            Review(DefenseView(61,true));
            checkpoint=(PlayableAiOwnerCheckpoint)type.GetProperty("Checkpoint",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(loop);
            Assert.True(checkpoint.Records.Any(r=>r.Policy==AiArmyPlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Cancelled),"A3: a new observed center threat must invalidate accepted offensive delivery even while its reaction delay is pending");
            Assert.True(checkpoint.Armies.Armies.Any(a=>a.Role==AiArmyRole.MobileDefense&&a.Phase!=AiArmyPhase.Disbanded),"A3 mobile defense must have a major army slot");
        }
        [Test] public void DefenseRecovery_ReservesNearestSufficientMajorArmyWithoutScoutBypass()
        {
            var view=DefenseView(60,true,9);var o=PlayableAiObservation.From(view);
            var foes=Enumerable.Range(102,3).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(8,(id-101)*1.5),100,false,0,0,0)).ToArray();
            var own=o.Entities.Where(u=>u.Owner==o.Owner).Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,new NavPoint(u.Id<=3?0:-8,(u.Id-1)*1.5),100,false,0,0,0)).ToArray();
            var es=own.Concat(o.Entities.Where(u=>u.Owner!=o.Owner)).Concat(foes).ToArray();
            var proofs=own.SelectMany(u=>es.Where(e=>e.Owner!=o.Owner).Select(e=>new PlayableRouteProof(u.Id,u.Owner,71,60,1,u.Position,P.TankCollisionRadius,PlayableRouteTargetKind.VisibleEnemy,e.Id,e.Position,e.Position,new[]{e.Position}))).ToArray();
            o=PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,o.Seed,60,60,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,es,o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:o.Vision,routeProofs:proofs));
            var r=Registry();Assert.True(r.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var small));Assert.True(r.TryCreate(o,AiArmyRole.Escort,"expansion",new[]{4,5,6,7,8,9},out var enough));
            var d=new AiDefensePlanner("player-1",71);var action=d.Propose(o,P,AiProfile.Initial,r);Assert.NotNull(action);CollectionAssert.AreEquivalent(new[]{4,5,6,7,8,9},action.EntityIds,"The nearer insufficient army must not replace the sufficient army");
            Assert.AreEqual("expansion",r.Capture().Armies.Single(a=>a.Id==enough).TacticalOwner,"Proposal is pure");
            var defense=new AiIntent("defend",AiDefensePlanner.Policy,action,120,0,0,action.EntityIds.Select(id=>"recipient:"+id),2,1,"threat");
            var attack=new AiIntent("attack",AiArmyPlanner.Policy,action,120,0,0,action.EntityIds.Select(id=>"recipient:"+id),0,1,"offense");
            var arbiter=new AiDecisionArbiter(AiProfile.Initial);Assert.AreEqual(defense,arbiter.Select(new[]{attack,defense},60,1000,0,3,armies:r).Single());
            d.Commit(o,action,r);Assert.AreEqual(enough,d.ArmyId);Assert.AreEqual(small,r.ArmyFor(1));Assert.AreEqual(AiArmyRole.MobileDefense,r.Capture().Armies.Single(a=>a.Id==enough).Role);Assert.AreEqual("unit tactical owner conflict",r.Reject(attack));r.AssertInvariants();
            d.ObserveReceipt(Receipt(action,PlayableAiDeliveryStatus.Applied,90));
            d.Observe(PlayableAiObservation.From(DefenseView(91,false,9)),r,P);Assert.False(d.Active);Assert.True(action.EntityIds.All(id=>r.ArmyFor(id)==0));Assert.AreEqual(small,r.ArmyFor(1));
        }
        [Test] public void DefenseRecovery_FreeForceUsesOnlyNeededMembersAndRejectsMissingRoutes()
        {
            var o=PlayableAiObservation.From(DefenseView(60,true));var r=Registry();var d=new AiDefensePlanner("player-1",71);
            var a=d.Propose(o,P,AiProfile.Initial,r);Assert.NotNull(a);Assert.AreEqual(AiProfile.Initial.Value("armies.minimumUnits"),a.EntityIds.Count);Assert.IsEmpty(r.Capture().Armies);
            d.Commit(o,a,r);Assert.AreEqual(1,r.Capture().Armies.Count(g=>g.Major&&g.Phase!=AiArmyPhase.Disbanded));Assert.True(a.EntityIds.All(id=>r.ArmyFor(id)==d.ArmyId));
            var noRoute=PlayableAiObservation.From(DefenseView(61,true,proof:false));Assert.IsNull(new AiDefensePlanner("player-1",71).Propose(noRoute,P,AiProfile.Initial,Registry()));
            Assert.Throws<ArgumentException>(()=>d.Propose(Observation(62),P,AiProfile.Initial,new AiArmyRegistry("other",71,AiProfile.Initial,AiDifficulty.Fighter)),"Foreign registry cannot be used for defense");
        }
        private static OfflineMatchConfiguration DefenseConfig()
        {
            var basis=Config(6);var scenario=Enumerable.Range(0,6).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-20+(i%3)*2,-8-(i/3)*3)))
                .Concat(new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-10,0))}).ToArray();
            return new OfflineMatchConfiguration(P,"a3-defense","a3-03-a3-05-unassisted-native","UnityHostRouteService",basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:scenario,scenarioBuildings:basis.ScenarioBuildings.ToArray());
        }
        [Test] public void DefenseRecovery_ActualCombatClearsThreatResumesPressureAndKeepsMacroLive()
        {
            var c=DefenseConfig();var a=new PlayableAuthorityTick(c,71);var owner=Owner(a);var d=(AiDefensePlanner)Get(owner,"defense");
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<120&&!d.Active;i++)Step(a,host);Assert.True(d.Active,"Native routes and ordinary arbitration must reserve mobile defense");
                var initial=a.CaptureDiagnosticCheckpoints().Single();Assert.True(initial.Armies.Armies.Any(g=>g.Id==d.ArmyId&&g.Major&&g.Role==AiArmyRole.MobileDefense));
                var bytes=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes(bytes,c);CollectionAssert.AreEqual(bytes,b.CaptureBytes());
                var tick=a.Tick;for(int i=0;i<15;i++){Assert.True(a.TryAdvance(paused:true));Assert.True(b.TryAdvance(paused:true));}Assert.AreEqual(tick,a.Tick);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                bool defended=false,macro=false,recovered=false;long clear=-1;
                for(int i=0;i<1500;i++)
                {
                    Step(a,host);Step(b,host);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                    var checkpoint=a.CaptureDiagnosticCheckpoints().Single();var observation=PlayableAiObservation.From(a.ParticipantView("west-owner"));
                    defended|=checkpoint.Records.Any(r=>r.Policy==AiDefensePlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied);
                    macro|=checkpoint.Records.Any(r=>(r.Policy=="production"||r.Policy=="economy"||r.Policy=="research")&&r.Status==PlayableAiDeliveryStatus.Applied);
                    if(defended&&AiDefensePlanner.Threats(observation,P).Length==0&&clear<0)clear=a.Tick;
                    if(clear>=0&&checkpoint.Records.Any(r=>r.Policy==AiArmyPlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>clear)){recovered=true;break;}
                }
                TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Scenario="A3-03/A3-05",Profile=P.ProfileId,Ai=AiProfile.Initial.Hash,a.Tick,clear,defended,macro,recovered,Checkpoint=a.CaptureDiagnosticCheckpoints().Single()}));
                Assert.True(defended);Assert.True(macro,"Defense must not stop affordable macro decisions");Assert.GreaterOrEqual(clear,0,"Combat must actually clear the owner-visible threat, with no injected money/health or enemy retreat");Assert.True(recovered,"Pressure must resume after observed threat clearance");
                var data=AiProfile.Initial.CopyData();data.revision++;var ai=new AiProfile(data);Assert.IsNull(a.ApplyProfile(c.Profile,ai));var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,ai);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());
                a.Stop();b.Stop();restored.Stop();Assert.False(d.Active);var fresh=new PlayableAuthorityTick(c,72);Assert.False(((AiDefensePlanner)Get(Owner(fresh),"defense")).Active);fresh.Stop();
            }
        }
        [Test] public void DefenseRecovery_StrictPrivateDefenseBindingAndLateGenerationCallback()
        {
            var c=DefenseConfig();var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                var d=(AiDefensePlanner)Get(Owner(a),"defense");for(int i=0;i<120&&!d.Active;i++)Step(a,host);Assert.True(d.Active);Assert.Greater(d.PendingId,0);
                var bytes=a.CaptureBytes();Assert.DoesNotThrow(()=>PlayableAuthorityTick.RestoreBytes(bytes,c));
                typeof(AiDefensePlanner).GetField("targetId",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(d,999999);
                Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c),"Defense target and army objective must bind strictly");
                var b=PlayableAuthorityTick.RestoreBytes(bytes,c);var original=(AiDefensePlanner)Get(Owner(b),"defense");var pending=original.PendingId;
                original.ObserveReceipt(new PlayableAiTraceRecord("fixture",pending,0,0,b.Tick,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"foreign generation",ownerId:"west-owner",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity,receiptIdentity:new AiReceiptIdentity(72,"west-owner",1,1)));Assert.AreEqual(pending,original.PendingId);
                var data=AiProfile.Initial.CopyData();data.revision++;var next=new AiProfile(data);Assert.IsNull(b.ApplyProfile(c.Profile,next));Assert.Zero(original.PendingId);Assert.True(original.Active);var restored=PlayableAuthorityTick.RestoreBytes(b.CaptureBytes(),c,next);CollectionAssert.AreEqual(b.CaptureBytes(),restored.CaptureBytes());
                a.Stop();b.Stop();restored.Stop();
            }
        }
        [Test] public void DefenseRecovery_NoForceUsesOneEmergencyAdmissionThenOrdinaryCadence()
        {
            var type=typeof(AiArmyPlanner).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop");var loop=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{P,Opening(),71L,null},null);
            void Review(long tick)=>type.GetMethod("Review",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(loop,new object[]{DefenseView(tick,true,0),0L});
            Review(60);var first=(long)Get(loop,"lastDecisionTick");Assert.AreEqual(60,first);
            for(long tick=61;tick<75;tick++)Review(tick);
            Assert.AreEqual(first,(long)Get(loop,"lastDecisionTick"),"Persistent threat with no admissible defense must not become a new emergency decision each tick");
            Assert.IsEmpty(((AiArmyRegistry)Get(loop,"armies")).Capture().Armies);
        }
        private static PlayableAiObservation DefenseForces(int ownCount,int hostileCount,bool spread=false)
        {
            var baseView=DefenseView(60,true,ownCount);var o=PlayableAiObservation.From(baseView);
            var own=o.Entities.Where(u=>u.Owner==o.Owner).Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,new NavPoint(spread?(u.Id<=3?-8:u.Id<=6?0:-20):0,(u.Id-1)*1.5),100,false,0,0,0)).ToArray();
            var hostile=Enumerable.Range(101,hostileCount).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(8,(id-101)*1.5),100,false,0,0,0)).ToArray();
            var routes=own.SelectMany(u=>hostile.Select(e=>new PlayableRouteProof(u.Id,u.Owner,71,60,1,u.Position,P.TankCollisionRadius,PlayableRouteTargetKind.VisibleEnemy,e.Id,e.Position,e.Position,new[]{e.Position}))).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,o.Seed,60,60,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,own.Concat(hostile).ToArray(),o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:o.Vision,routeProofs:routes));
        }
        [Test] public void DefenseRecovery_InsufficientForceDoesNotCreateAnUnsupportedDefenseMission()
        {
            var o=DefenseForces(3,4);var r=Registry();var d=new AiDefensePlanner("player-1",71);
            Assert.Less(AiDefensePlanner.Force(o.Entities.Where(u=>u.Owner==o.Owner).ToArray(),P,AiProfile.Initial),AiDefensePlanner.Force(o.Entities.Where(u=>u.Owner!=o.Owner).ToArray(),P,AiProfile.Initial));
            Assert.IsNull(d.Propose(o,P,AiProfile.Initial,r),"A3 requires a sufficient army; no new suicidal fallback is approved");Assert.IsEmpty(r.Capture().Armies);
        }
        [Test] public void DefenseRecovery_NearestSufficientFreeSubsetBeatsDistantExistingArmy()
        {
            var o=DefenseForces(9,1,true);var r=Registry();Assert.True(r.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var existing));
            var action=new AiDefensePlanner("player-1",71).Propose(o,P,AiProfile.Initial,r);Assert.NotNull(action);
            CollectionAssert.AreEquivalent(new[]{4,5,6},action.EntityIds,"Nearest force is the actual reserved sufficient subset, not the average position of every free tank");Assert.AreEqual(existing,r.ArmyFor(1));
        }
        [Test] public void DefenseRecovery_AggregatePendingCapacityRoundtripsAcrossReactionLongerThanCadence()
        {
            var c=DefenseConfig();var data=AiProfile.Initial.CopyData();data.revision++;
            data.fields.Single(f=>f.path=="difficulty.fighter.reactionSeconds").value=2;
            data.fields.Single(f=>f.path=="difficulty.fighter.actionsPerDecision").value=1;
            data.fields.Single(f=>f.path=="difficulty.recruit.actionsPerDecision").value=1;
            var ai=new AiProfile(data);var a=new PlayableAuthorityTick(c,71,ai);
            using(var host=new UnityHostRouteService())
            {
                bool pending=false,macro=false;
                for(int tick=0;tick<100;tick++)
                {
                    Step(a,host);var owner=Owner(a);var items=((System.Collections.IEnumerable)Get(owner,"pendingItems")).Cast<object>().ToArray();
                    pending|=items.Any(item=>(string)Get(item,"PolicyName")==AiDefensePlanner.Policy);
                    macro|=items.Any(item=>new[]{"economy","production","research","scout"}.Contains((string)Get(item,"PolicyName")));
                    Assert.LessOrEqual(items.Length,1,"Aggregate accepted pending actions must obey the existing C9 capacity, including old actions plus newly selected policies");
                    Assert.DoesNotThrow(()=>{var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,ai);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());b.Stop();});
                }
                Assert.True(pending);Assert.True(macro);a.Stop();
            }
        }
        private static byte[] LoopBytes(object loop)
        {
            using(var stream=new System.IO.MemoryStream())using(var writer=new System.IO.BinaryWriter(stream))
            {loop.GetType().GetMethod("WriteState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(loop,new object[]{writer});writer.Flush();return stream.ToArray();}
        }
        private static object RestoreLoop(object loop,byte[] bytes,long tick,AiProfile ai=null)
        {
            using(var stream=new System.IO.MemoryStream(bytes))using(var reader=new System.IO.BinaryReader(stream))return loop.GetType().GetMethod("ReadState",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{reader,P,ai??AiProfile.Initial,71L,Opening().MatchSeed,tick});
        }
        [Test] public void DefenseRecovery_LastMemberLossCancelsPendingAndRestoresImmediately()
        {
            var type=typeof(AiArmyPlanner).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop");var loop=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{P,Opening(),71L,null},null);
            void Review(long tick,int count)=>type.GetMethod("Review",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(loop,new object[]{DefenseView(tick,true,count),0L});
            Review(60,6);var defense=(AiDefensePlanner)Get(loop,"defense");Assert.True(defense.Active);Assert.Greater(defense.PendingId,0);
            Assert.DoesNotThrow(()=>RestoreLoop(loop,LoopBytes(loop),60));
            Review(61,0);Assert.False(defense.Active,"Defense must reconcile with same-tick registry membership loss");Assert.Zero(defense.PendingId,"Lost defensive army cannot retain accepted delivery/callback");
            Assert.DoesNotThrow(()=>RestoreLoop(loop,LoopBytes(loop),61));
        }
        [Test] public void DefenseRecovery_ProfileCapCancelsPendingAndDropsOrphanDefenseSynchronously()
        {
            var type=typeof(AiArmyPlanner).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop");var loop=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{P,Opening(),71L,null},null);
            var r=(AiArmyRegistry)Get(loop,"armies");var o=DefenseForces(9,1,true);Assert.True(r.TryCreate(o,AiArmyRole.Escort,"escort",new[]{1,2,3},out var first));Assert.True(r.TryCreate(o,AiArmyRole.AlliedSupport,"allied-support",new[]{7,8,9},out var second));
            var snapshot=new PlayableSnapshot(P.ProfileId,P.Revision,71,o.Seed,60,60,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,o.Entities.ToArray(),o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:o.Vision,routeProofs:o.RouteProofs.ToArray());
            type.GetMethod("Review",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(loop,new object[]{snapshot,0L});var d=(AiDefensePlanner)Get(loop,"defense");Assert.True(d.Active);Assert.Greater(d.ArmyId,second);Assert.Greater(d.PendingId,0);
            var data=AiProfile.Initial.CopyData();data.revision++;data.fields.Single(f=>f.path=="difficulty.fighter.majorArmies").value=2;var next=new AiProfile(data);
            type.GetMethod("Rebind",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(AiProfile)},null).Invoke(loop,new object[]{next});Assert.False(d.Active);Assert.Zero(d.PendingId);Assert.AreEqual(first,r.ArmyFor(1));Assert.AreEqual(second,r.ArmyFor(7));
            Assert.DoesNotThrow(()=>RestoreLoop(loop,LoopBytes(loop),60,next));
        }
        [TestCase(6)][TestCase(7)][TestCase(8)][TestCase(9)][TestCase(10)][TestCase(11)] public void DefenseRecovery_PrivateSchemaRejectsHistoricalVersionsWithoutMutation(int schema)
        {
            var c=DefenseConfig();var a=new PlayableAuthorityTick(c,71);var original=a.CaptureBytes();var state=PlayableWorldState.Decode(original);
            Assert.AreEqual(12,BitConverter.ToInt32(state.AiAuthority,4));Buffer.BlockCopy(BitConverter.GetBytes(schema),0,state.AiAuthority,4,4);
            Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(state.Encode(),c));CollectionAssert.AreEqual(original,a.CaptureBytes());a.Stop();
        }
        [Test] public void DefenseRecovery_ProgressClockUsesObservedMovementAndDamageNotReceipts()
        {
            var r=Registry();var d=new AiDefensePlanner("player-1",71);var o=PlayableAiObservation.From(DefenseView(60,true));var action=d.Propose(o,P,AiProfile.Initial,r);d.Commit(o,action,r);
            d.ObserveReceipt(Receipt(action,PlayableAiDeliveryStatus.Applied,61));d.Observe(PlayableAiObservation.From(DefenseView(61,true)),r,P);Assert.AreEqual(60,r.Capture().Armies.Single(a=>a.Id==d.ArmyId).ProgressTick);
            PlayableAiObservation Changed(long tick,bool movement,bool damage)
            {
                var basis=DefenseView(tick,true);var es=basis.Entities.Select(e=>new PlayableEntitySnapshot(e.Id,e.Owner,e.Kind,movement&&e.Owner==PlayableOwner.Player?new NavPoint(e.Position.X+1,e.Position.Z):e.Position,damage&&e.Owner==PlayableOwner.Enemy?e.Health-1:e.Health,false,0,0,0)).ToArray();
                return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,basis.Seed,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,es,basis.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:basis.Vision));
            }
            d.Observe(Changed(62,true,false),r,P);Assert.AreEqual(62,r.Capture().Armies.Single(a=>a.Id==d.ArmyId).ProgressTick);
            d.Observe(Changed(63,true,true),r,P);Assert.AreEqual(63,r.Capture().Armies.Single(a=>a.Id==d.ArmyId).ProgressTick);
            d.Observe(Changed(64,true,true),r,P);Assert.AreEqual(63,r.Capture().Armies.Single(a=>a.Id==d.ArmyId).ProgressTick);
        }
        [Test] public void DefenseRecovery_ActualOrdinaryApproachInvalidatesPendingOffenseBeforeDueTick()
        {
            var basis=Config(6);var scenario=Enumerable.Range(0,6).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-12+(i%3)*2,-8-(i/3)*3))).Concat(new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(0,0),Math.PI)}).ToArray();
            var c=new OfflineMatchConfiguration(P,"a3-pending-approach","a3-authored-ordinary-approach","UnityHostRouteService",basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:scenario,scenarioBuildings:basis.ScenarioBuildings.ToArray());var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<45;i++)Step(a,host);var checkpoint=a.CaptureDiagnosticCheckpoints().Single();var pending=checkpoint.Records.Single(r=>r.Policy==AiArmyPlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Scheduled);var firstArmy=checkpoint.ArmyMission.ArmyId;
                var enemyView=a.ParticipantView("east-owner");var enemy=enemyView.Entities.Single(e=>e.Owner==enemyView.Owner&&e.Kind==PlayableEntityKind.Tank);
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,500,"east-owner",PlayableCommandKind.Move,new[]{enemy.Id},new NavPoint(-4,0))).Status);
                long cancelled=-1;
                while(a.Tick<100)
                {
                    Step(a,host);checkpoint=a.CaptureDiagnosticCheckpoints().Single();if(cancelled<0&&checkpoint.Records.Any(r=>r.ActionId==pending.ActionId&&r.Status==PlayableAiDeliveryStatus.Cancelled))cancelled=a.Tick;
                    var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());restored.Stop();
                }
                TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Scenario="A3 accepted-offense preemption",EnemyMoveTick=45,EnemyMoveTarget=new NavPoint(-4,0),pending.DueTick,cancelled,Checkpoint=checkpoint}));
                Assert.GreaterOrEqual(cancelled,0);Assert.Less(cancelled,pending.DueTick,"Owner-visible ordinary approach must invalidate stale offensive delivery before its old due tick");Assert.False(checkpoint.Records.Any(r=>r.ActionId==pending.ActionId&&r.Status==PlayableAiDeliveryStatus.Applied));
                Assert.True(checkpoint.Armies.Armies.Any(g=>g.Id==firstArmy&&g.Role==AiArmyRole.MobileDefense&&g.TacticalOwner==AiDefensePlanner.Policy));a.Stop();
            }
        }
    }
}
