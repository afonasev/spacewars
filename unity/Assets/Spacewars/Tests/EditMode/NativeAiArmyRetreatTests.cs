using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiArmyTests
    {
        private static PlayableAiObservation A5Observation(string change="local",long tick=100,bool move=false,bool arrived=false)
        {
            var p=PlayableProfile.Default;var old=A4Observation(tick,effective:true);var goal=new NavPoint(-25,0);
            var entities=old.Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>new PlayableEntitySnapshot(e.Id,e.Owner,e.Kind,arrived&&change!="unphysical"?new NavPoint(-25,(e.Id-1)*2):e.Position,e.Health,false,0,0,0,
                move&&change!="cleared"&&change!="unphysical"?new PlayableTacticalOrderSnapshot(e.Id,e.Owner,71,2,100,PlayableTacticalOrderKind.Move,goal,0):move?null:e.CurrentOrder,
                navigationOutcome:arrived?NavigationOutcome.Arrived:NavigationOutcome.Moving,
                orderStamp:move?new PlayableOrderStamp(e.Id,e.Owner,71,2,change=="sequence"?3:2,100,change=="human"?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,PlayableCommandKind.Move,change=="source"?"foreign":PlayableAiOpeningComposition.SourceIdentity,2,2):null)).ToList();
            entities.AddRange(Enumerable.Range(101,6).Select(id=>new PlayableEntitySnapshot(id,change=="allied"?PlayableOwner.Player:PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(change=="remote"?40:6,(id-101)*.5),p.TankHealth,false,0,0,0)));
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(change=="hidden"?new NavPoint(-30,0):default,change=="hidden"?5:50)},Array.Empty<KnownBuilding>());
            var proofs=entities.Where(e=>e.Id<=3).SelectMany(u=>entities.Where(e=>e.Owner==PlayableOwner.Enemy).Select(e=>new PlayableRouteProof(u.Id,u.Owner,71,tick,1,u.Position,PlayableUnitRules.Radius(p,u.Kind),PlayableRouteTargetKind.VisibleEnemy,e.Id,e.Position,e.Position,new[]{e.Position}))).ToList();
            if(change!="missing")foreach(var u in entities.Where(e=>e.Id<=3))
            {
                var path=change=="unsafe"?new[]{new NavPoint(7,0),goal}:new[]{goal};
                proofs.Add(new PlayableRouteProof(u.Id,u.Owner,change=="stale"?70:71,tick,1,u.Position,PlayableUnitRules.Radius(p,u.Kind),PlayableRouteTargetKind.FriendlyAnchor,move&&change!="cleared"&&change!="unphysical"?u.Id:50,move&&change!="cleared"&&change!="unphysical"?goal:old.Buildings.Single(b=>b.Id==50).Position,goal,path));
            }
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,old.Seed,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,entities.ToArray(),old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:proofs.ToArray()));
        }
        private static PlayableAiTraceRecord A5Receipt(string change="valid")=>new PlayableAiTraceRecord("withdrawal",2,100,2,100,change=="unapplied"?PlayableAiDeliveryStatus.Cancelled:PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"fixture",policy:change=="policy"?"foreign":AiArmyPlanner.Policy,kind:PlayableCommandKind.Move,ownerId:"player-1",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity,receiptIdentity:new AiReceiptIdentity(71,"player-1",1,2));
        [TestCase("local",true)][TestCase("remote",false)][TestCase("hidden",false)][TestCase("allied",false)]
        public void Retreat_OnlyLocalVisibleHostileCatalogForce(string change,bool expected)
        {
            var r=Registry();var mission=A4Mission(r);var o=A5Observation(change);var army=r.Capture().Armies.Single();
            Assert.AreEqual(expected,AiTacticalExecutor.Withdraw(o,army,PlayableProfile.Default,AiProfile.Initial));
            Assert.AreEqual(expected,AiTacticalExecutor.Withdrawal(o,army,PlayableProfile.Default,AiProfile.Initial,2)!=null);
        }
        [TestCase("local",PlayableCommandKind.Move)][TestCase("unsafe",PlayableCommandKind.Attack)][TestCase("missing",PlayableCommandKind.Attack)][TestCase("stale",PlayableCommandKind.Attack)]
        public void Retreat_UnsafeOrMissingNativeFallbackDefendsInsteadOfIdle(string change,PlayableCommandKind expected)
        {
            var r=Registry();var mission=A4Mission(r);var o=A5Observation(change);mission.Observe(o,r,AiProfile.Initial);var id=mission.Capture().ArmyId;
            var action=mission.Propose(o,A4Opening(),PlayableProfile.Default,AiProfile.Initial,r);Assert.NotNull(action);Assert.AreEqual(expected,action.Kind);Assert.AreEqual(id,r.ArmyFor(action.EntityIds.Single()));
            Assert.AreEqual(AiArmyPhase.Regrouping,mission.Capture().Phase);
        }
        [TestCase("valid",true)][TestCase("human",false)][TestCase("source",false)][TestCase("sequence",false)][TestCase("unapplied",false)][TestCase("policy",false)][TestCase("missing",false)][TestCase("unsafe",false)][TestCase("stale",false)]
        public void Retreat_EffectiveMoveRequiresAuthorityAndCurrentCertificate(string change,bool expected)
        {
            var r=Registry();A4Mission(r);var o=A5Observation(change,101,move:true);var army=r.Capture().Armies.Single();
            bool actual=(bool)typeof(AiTacticalExecutor).GetMethod("EffectiveWithdrawal",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{o,army,o.Entities.First(),new[]{A5Receipt(change)},PlayableProfile.Default,true});
            Assert.AreEqual(expected,actual);
        }
        [Test] public void Retreat_StallFallsBackOnceAndStillHasBoundedRelease()
        {
            var r=Registry();var mission=A4Mission(r);long due=10+AiProfile.SecondsToTicks(AiProfile.Initial.Value("armies.stallSeconds"),30);
            var o=A5Observation("remote",due);mission.Observe(o,r,AiProfile.Initial);Assert.True(mission.Active);Assert.AreEqual(AiArmyPhase.Regrouping,mission.Capture().Phase);
            mission.Observe(A5Observation("remote",due+AiProfile.SecondsToTicks(AiProfile.Initial.Value("armies.stallSeconds"),30)),r,AiProfile.Initial);Assert.False(mission.Active);Assert.AreEqual("withdrawal no meaningful progress deadline",mission.Capture().Reason);
        }
        [Test] public void Retreat_ActualAuthorityMovesAndPreservesPendingRestore()
        {
            var c=A4NativeConfig();var a=new PlayableAuthorityTick(c,71);var owner=Owners(a).Single();var registry=(AiArmyRegistry)Field(owner,"armies");var planner=(AiArmyPlanner)Field(owner,"armyPlanner");var domain=Field(a,"domain");
            using(var routes=new UnityHostRouteService())
            {
                for(int i=0;i<300&&(!planner.Active||planner.Capture().Phase!=AiArmyPhase.Advancing);i++)NativeStep(a,routes);
                Assert.True(planner.Active);long army=planner.Capture().ArmyId;
                // Exercise the actual bounded stall branch while leaving all gameplay bodies intact.
                var state=(AiArmyMissionState)Field(planner,"state");
                planner.SetPhase(registry,AiArmyPhase.Regrouping,a.Tick);state.Reason="withdrawal";
                bool moved=false,restored=false;NavPoint? before=null;
                for(int i=0;i<300&&!moved;i++)
                {
                    NativeStep(a,routes);var view=a.ParticipantView("player-1");var unit=view.Entities.FirstOrDefault(u=>registry.ArmyFor(u.Id)==army&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai);
                    if(unit==null)continue;
                    Assert.AreEqual(army,planner.Capture().ArmyId);Assert.AreEqual(AiArmyPhase.Retreating,planner.Capture().Phase);
                    if(!restored){var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());restored=true;}
                    if(before.HasValue&&!before.Value.Equals(unit.Position))moved=true;before=unit.Position;
                }
                Assert.True(restored,"Actual retreat world/AI wire must roundtrip");Assert.True(moved,"A receipt is insufficient: a body must move through the native route barrier");
            }
        }
        [TestCase("away",true)][TestCase("toward",false)][TestCase("reentry",false)][TestCase("new-envelope",false)][TestCase("endpoint-exposed",false)]
        public void Retreat_MonotonicSegmentsCannotCrossOrReenterWeaponEnvelopes(string path,bool expected)
        {
            var o=A5Observation();var points=path=="away"?new[]{new NavPoint(-12,0),new NavPoint(-25,0)}:
                path=="toward"?new[]{new NavPoint(7,0),new NavPoint(-25,0)}:
                path=="reentry"?new[]{new NavPoint(-25,0),new NavPoint(7,0),new NavPoint(-25,0)}:
                path=="new-envelope"?new[]{new NavPoint(0,30),new NavPoint(7,0),new NavPoint(-25,0)}:new[]{new NavPoint(-.1,0)};
            bool actual=(bool)typeof(AiTacticalExecutor).GetMethod("WithdrawalPath",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{o,default(NavPoint),points,PlayableProfile.Default.TankCollisionRadius,PlayableProfile.Default});Assert.AreEqual(expected,actual);
        }
        [Test] public void Retreat_OrdinaryArtillerySupportMoveDoesNotHijackMissionPhase()
        {
            var r=Registry();var mission=A4Mission(r);var old=A4Observation(100,wave:true,gun:true,near:true);long id=mission.Capture().ArmyId;AiTacticalExecutor.Observe(old,r,id,PlayableProfile.Default);
            var fact=new PlayableArtillerySupportSnapshot(4,71,100,true,false,false,true,new NavPoint(-5,4));
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,71,old.Seed,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,old.Entities.ToArray(),old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:old.RouteProofs.ToArray(),artillerySupport:new[]{fact}));
            var action=mission.Propose(o,A4Opening(),PlayableProfile.Default,AiProfile.Initial,r);Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);CollectionAssert.AreEqual(new[]{4},action.EntityIds);var phase=mission.Capture().Phase;var anchor=r.Capture().Armies.Single().Anchor;
            mission.Commit(o,action,r);Assert.AreEqual(phase,mission.Capture().Phase);Assert.AreEqual(anchor,r.Capture().Armies.Single().Anchor);Assert.AreEqual(id,mission.Capture().ArmyId);
        }
        private static OfflineMatchConfiguration A5VisibleRecoveryConfig()
        {
            var c=A4NativeConfig();var observer=new NavPoint(20,-8);
            // Positive recovery has a legitimately observed objective. Only this local
            // fixture copy moves its existing refinery/declared slot; A4 is unchanged.
            var sites=c.Sites.Select(s=>s.Id==1?new TerritorySite(s.Id,s.Kind,s.Position,s.Slots.Select(x=>x.Id==2?new TerritorySlot(x.Id,observer,x.Heading):x).ToArray()):s).ToArray();
            var buildings=c.ScenarioBuildings.Select(b=>b.LogicalPlayer==1&&b.Kind==PlayableBuildingKind.Refinery?new OfflineScenarioBuilding(b.LogicalPlayer,b.Kind,b.SiteId,b.SlotId,observer,b.Heading):b).ToArray();
            return new OfflineMatchConfiguration(c.Profile,c.SourceIdentity+"-a5-visible-recovery",c.MapIdentity+"-a5-visible-recovery",c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),sites,c.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},spectators:c.Spectators.ToArray(),scenario:c.ScenarioUnits.ToArray(),scenarioBuildings:buildings);
        }
        [TestCase(true)][TestCase(false)] public void Retreat_ActualVisibleThreatCompletesNativeArrivalRecoveryAndReplay(bool injectReinforcements)
            =>A5RecoveryCycle(injectReinforcements,injectReinforcements);
        [Test] public void Retreat_OccludedObjectiveAfterActualRecoveryKeepsSameArmyAndDoesNotAttackHiddenTarget()
            =>A5RecoveryCycle(true,false);
        private static void A5RecoveryCycle(bool injectReinforcements,bool visibleObjective)
        {
            var c=visibleObjective?A5VisibleRecoveryConfig():A4NativeConfig();var a=new PlayableAuthorityTick(c,71);var owner=Owners(a).Single();var registry=(AiArmyRegistry)Field(owner,"armies");var planner=(AiArmyPlanner)Field(owner,"armyPlanner");var domain=Field(a,"domain");
            using(var routes=new UnityHostRouteService())
            {
                for(int i=0;i<300&&(!planner.Active||planner.Capture().Phase!=AiArmyPhase.Advancing);i++)NativeStep(a,routes);
                Assert.True(planner.Active);long army=planner.Capture().ArmyId;var front=a.ParticipantView("player-1").Entities.Where(u=>registry.ArmyFor(u.Id)==army).OrderBy(u=>u.Id).First().Position;
                for(int i=0;i<6;i++)domain.GetType().GetMethod("SpawnUnit",F).Invoke(domain,new object[]{new NavPoint(front.X+c.Profile.TankRange/2,front.Z+(i-3)*2),PlayableOwner.Enemy,PlayableEntityKind.Tank});
                bool retreat=false,recover=false,resume=false,moved=false,pendingRestore=false,effectiveRestore=false,injected=false;long lastSeenAtRetreat=-1,recoveredTick=-1;var injectedIds=new System.Collections.Generic.List<int>();PlayableAuthorityTick twin=null;var initial=a.ParticipantView("player-1").Entities.Where(u=>registry.ArmyFor(u.Id)==army).ToDictionary(u=>u.Id,u=>u.Position);
                for(int i=0;i<(injectReinforcements?900:1600)&&!resume&&(!retreat||planner.Active);i++)
                {
                    NativeStep(a,routes);if(twin!=null){NativeStep(twin,routes);ExactByteAssert.AreEqual(a.CaptureBytes(),twin.CaptureBytes(),"Exact native replay after withdrawal checkpoint");}
                    var phase=planner.Capture().Phase;retreat|=phase==AiArmyPhase.Retreating;recover|=phase==AiArmyPhase.Recovering;resume=recover&&(phase==AiArmyPhase.Staging||phase==AiArmyPhase.Advancing||phase==AiArmyPhase.Engaging);
                    if(retreat&&lastSeenAtRetreat<0)lastSeenAtRetreat=planner.Capture().LastSeenTick;
                    if(planner.Capture().Reason=="recovered"&&recoveredTick<0)recoveredTick=a.Tick;
                    if(retreat&&planner.Active)Assert.AreEqual(army,planner.Capture().ArmyId,"Original army owns the entire retreat/recovery cycle");
                    if(recover&&injectReinforcements&&!injected)
                    {
                        // Fixture-injected healthy reinforcements through the existing
                        // produced-unit spawn API; this does not prove production itself.
                        for(int n=0;n<3;n++)
                        {
                            var point=new NavPoint(-23,-14-n*3);
                            var units=(System.Collections.IDictionary)Field(domain,"units");var oldIds=units.Keys.Cast<int>().ToArray();
                            domain.GetType().GetMethod("SpawnProduced",F).Invoke(domain,new object[]{point,(NavPoint?)new NavPoint(8,-8),PlayableOwner.Player,PlayableEntityKind.Tank});
                            injectedIds.Add(units.Keys.Cast<int>().Except(oldIds).Single());
                            if(twin!=null){var td=Field(twin,"domain");td.GetType().GetMethod("SpawnProduced",F).Invoke(td,new object[]{point,(NavPoint?)new NavPoint(8,-8),PlayableOwner.Player,PlayableEntityKind.Tank});}
                        }
                        injected=true;TestContext.WriteLine("Fixture-injected 3 healthy reinforcements; actual native assembly and viability remain required");
                    }
                    var view=a.ParticipantView("player-1");moved|=view.Entities.Any(u=>initial.ContainsKey(u.Id)&&!initial[u.Id].Equals(u.Position));
                    if(phase==AiArmyPhase.Retreating&&!pendingRestore&&(long)Field(planner,"pendingId")!=0)
                    {twin=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);ExactByteAssert.AreEqual(a.CaptureBytes(),twin.CaptureBytes());pendingRestore=true;}
                    if(phase==AiArmyPhase.Retreating&&!effectiveRestore&&view.Entities.Any(u=>registry.ArmyFor(u.Id)==army&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai))
                    {var copy=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);ExactByteAssert.AreEqual(a.CaptureBytes(),copy.CaptureBytes());effectiveRestore=true;long tick=a.Tick;long progress=planner.Capture().ProgressTick;for(int n=0;n<3;n++){Assert.True(a.TryAdvance(paused:true));if(twin!=null)Assert.True(twin.TryAdvance(paused:true));}Assert.AreEqual(tick,a.Tick);Assert.AreEqual(progress,planner.Capture().ProgressTick);if(twin!=null)ExactByteAssert.AreEqual(a.CaptureBytes(),twin.CaptureBytes());}
                    if(i%60==0)TestContext.WriteLine("A5 actual lifecycle "+Newtonsoft.Json.JsonConvert.SerializeObject(new{a.Tick,Mission=planner.Capture(),Army=registry.Capture().Armies.FirstOrDefault(x=>x.Id==army),Units=view.Entities.Where(u=>initial.ContainsKey(u.Id)),Records=a.CaptureDiagnosticCheckpoints().Single().Records.TakeLast(8)}));
                }
                TestContext.WriteLine("A5 final lifecycle "+Newtonsoft.Json.JsonConvert.SerializeObject(new{a.Tick,Mission=planner.Capture(),Registry=registry.Capture(),InjectedIds=injectedIds.ToArray(),Snapshot=a.ParticipantView("player-1").Entities,Records=a.CaptureDiagnosticCheckpoints().Single().Records.TakeLast(12)}));
                Assert.True(retreat);Assert.True(moved);Assert.True(pendingRestore);Assert.True(effectiveRestore);Assert.True(recover,"Physical safe native arrival is required");if(injectReinforcements)
                {
                    var g=registry.Capture().Armies.Single(x=>x.Id==army);Assert.True(injectedIds.All(id=>g.Members.Contains(id)&&!g.Reinforcements.Contains(id)),"All three healthy fixture reinforcements must actually join through strict native assembly");
                    var view=PlayableAiObservation.From(a.ParticipantView("player-1"));var line=view.Entities.Where(u=>g.Members.Contains(u.Id)&&!g.Reinforcements.Contains(u.Id)&&AiRosterCatalog.Initial.For(u.Kind).lineWeight>0).ToArray();
                    Assert.GreaterOrEqual(AiDefensePlanner.Force(line,c.Profile,AiProfile.Initial),AiDefensePlanner.Force(new[]{new PlayableEntitySnapshot(999,view.Owner,PlayableEntityKind.Tank,default,c.Profile.TankHealth,false,0,0,0)},c.Profile,AiProfile.Initial)*AiProfile.Initial.Value("armies.minimumUnits"));
                    if(visibleObjective)
                    {
                        Assert.True(resume,"Recovered original army must resume pressure/retask after native reinforcement assembly");Assert.True(view.Vision.IsVisible(planner.Capture().Target));Assert.True(view.Buildings.Any(b=>b.Id==planner.Capture().TargetId&&view.IsHostile(b.Owner)&&b.Health>0),"The original objective is genuinely owner-visible at native resume");
                    }
                    else
                    {
                        Assert.False(resume);Assert.AreEqual("recovered",planner.Capture().Reason);Assert.AreEqual(AiArmyPhase.Regrouping,planner.Capture().Phase);Assert.False(planner.Capture().TargetVisible);Assert.AreEqual(lastSeenAtRetreat,planner.Capture().LastSeenTick);Assert.GreaterOrEqual(recoveredTick,0);
                        Assert.False(a.CaptureDiagnosticCheckpoints().Single().Records.Any(r=>r.Policy==AiArmyPlanner.Policy&&r.Kind==PlayableCommandKind.Attack&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>=recoveredTick),"Recovery cannot issue a new attack on an occluded objective");
                    }
                }
                else
                {
                    Assert.False(planner.Active,"No fixture reinforcements: underpowered native recovery must release within the existing stall bound");
                    Assert.AreEqual("withdrawal no meaningful progress deadline",planner.Capture().Reason);
                }
                a.Stop();Assert.False(planner.Active);Assert.True(registry.Capture().Armies.All(x=>x.Phase==AiArmyPhase.Disbanded));twin?.Stop();
            }
        }
        [TestCase("cleared",true)][TestCase("unphysical",false)][TestCase("missing",false)][TestCase("human",false)][TestCase("unapplied",false)]
        public void Retreat_NativeArrivalClearsOrderButStillRequiresAppliedPhysicalProof(string change,bool expected)
        {
            var r=Registry();A4Mission(r);var o=A5Observation(change,101,move:true,arrived:true);var army=r.Capture().Armies.Single();
            bool actual=(bool)typeof(AiTacticalExecutor).GetMethod("EffectiveWithdrawal",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{o,army,o.Entities.First(),new[]{A5Receipt(change)},PlayableProfile.Default,true});Assert.AreEqual(expected,actual);
        }
        [Test] public void Retreat_EscapeCannotEnterAnInitiallyUnexposedSeparateEnemyEnvelope()
        {
            var original=A5Observation();var p=PlayableProfile.Default;var e=new PlayableEntitySnapshot(200,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-25,0),p.TankHealth,false,0,0,0);
            Assert.Greater(Math.Abs(e.Position.X),p.TankRange+2*p.TankCollisionRadius,"New threat is genuinely outside the origin envelope");
            var o=PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,original.Seed,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,original.Entities.Concat(new[]{e}).ToArray(),original.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:original.Vision,routeProofs:original.RouteProofs.ToArray()));
            var escape=new[]{new NavPoint(-12,0),new NavPoint(-40,0)};
            var method=typeof(AiTacticalExecutor).GetMethod("WithdrawalPath",BindingFlags.NonPublic|BindingFlags.Static);
            Assert.True((bool)method.Invoke(null,new object[]{original,default(NavPoint),escape,p.TankCollisionRadius,p}),"This departure is safe against the initial frontline");
            Assert.False((bool)method.Invoke(null,new object[]{o,default(NavPoint),escape,p.TankCollisionRadius,p}),"A separate initially unexposed weapon envelope cannot be crossed");
        }
        [TestCase(false)][TestCase(true)] public void Retreat_ActualPendingWithdrawalRechecksMovingVisibleEnemyBeforeSequence(bool intrusion)
        {
            var c=A4NativeConfig();var a=new PlayableAuthorityTick(c,71);var owner=Owners(a).Single();var domain=Field(a,"domain");var registry=(AiArmyRegistry)Field(owner,"armies");var planner=(AiArmyPlanner)Field(owner,"armyPlanner");
            using(var routes=new UnityHostRouteService())
            {
                for(int i=0;i<300&&(!planner.Active||planner.Capture().Phase!=AiArmyPhase.Advancing);i++)NativeStep(a,routes);
                Assert.True(planner.Active);var front=a.ParticipantView("player-1").Entities.Where(u=>registry.ArmyFor(u.Id)==planner.Capture().ArmyId).OrderBy(u=>u.Id).First().Position;
                for(int i=0;i<6;i++)domain.GetType().GetMethod("SpawnUnit",F).Invoke(domain,new object[]{new NavPoint(front.X+c.Profile.TankRange/2,front.Z+(i-3)*2),PlayableOwner.Enemy,PlayableEntityKind.Tank});
                object pending=null;
                for(int i=0;i<150&&pending==null;i++)
                {NativeStep(a,routes);if(planner.Capture().Phase==AiArmyPhase.Retreating)pending=((System.Collections.IEnumerable)Field(owner,"pendingItems")).Cast<object>().FirstOrDefault(x=>(string)Field(x,"PolicyName")==AiArmyPlanner.Policy&&((PlayableAiAction)Field(x,"Action")).Kind==PlayableCommandKind.Move);}
                Assert.NotNull(pending,"A genuine accepted withdrawal candidate is required");var action=(PlayableAiAction)Field(pending,"Action");long due=(long)Field(pending,"DueTick");long army=planner.Capture().ArmyId;
                var actor=a.ParticipantView("player-1").Entities.Single(u=>u.Id==action.EntityIds.Single());var origin=actor.Position;var delta=new NavPoint(action.Target.X-origin.X,action.Target.Z-origin.Z);double length=Math.Sqrt(delta.X*delta.X+delta.Z*delta.Z);var middle=new NavPoint(origin.X+delta.X*.1,origin.Z+delta.Z*.1);var normal=new NavPoint(-delta.Z/length,delta.X/length);
                var start=new NavPoint(middle.X+normal.X*(c.Profile.TankRange+2*c.Profile.TankCollisionRadius+1),middle.Z+normal.Z*(c.Profile.TankRange+2*c.Profile.TankCollisionRadius+1));
                int enemy=(int)domain.GetType().GetMethod("SpawnUnit",F).Invoke(domain,new object[]{start,PlayableOwner.Enemy,PlayableEntityKind.Tank});
                Assert.True((bool)domain.GetType().GetMethod("ArmyWithdrawalSafe",F).Invoke(domain,new object[]{action}),"The newly visible intruder starts outside this accepted escape path");
                if(intrusion)
                {
                    var target=new NavPoint(middle.X+normal.X*c.Profile.TankRange*.8,middle.Z+normal.Z*c.Profile.TankRange*.8);
                    Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,"enemy-1",PlayableCommandKind.Move,new[]{enemy},target)).Status);
                }
                var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                while(a.Tick<due){NativeStep(a,routes);NativeStep(b,routes);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                var before=PlayableAiObservation.From(a.ParticipantView("player-1"));Assert.True(before.Entities.Any(u=>u.Id==enemy));Assert.IsEmpty(AiDefensePlanner.Threats(before,c.Profile),"The path intrusion must remain outside base-defense preemption");
                Assert.AreEqual(!intrusion,(bool)domain.GetType().GetMethod("ArmyWithdrawalSafe",F).Invoke(domain,new object[]{action}));
                while(a.Tick<=due+1){NativeStep(a,routes);NativeStep(b,routes);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                var terminal=a.CaptureDiagnosticCheckpoints().Single().Records.Single(r=>r.ActionId==action.ActionId&&r.Policy==AiArmyPlanner.Policy&&r.Status!=PlayableAiDeliveryStatus.Scheduled);
                Assert.AreEqual(intrusion?PlayableAiDeliveryStatus.Cancelled:PlayableAiDeliveryStatus.Applied,terminal.Status);Assert.AreEqual(army,registry.ArmyFor(actor.Id));
                if(intrusion){Assert.Zero(terminal.CommandSequence);StringAssert.Contains("Current observed army path",terminal.Message);}
                else
                {
                    for(int n=0;n<120&&a.ParticipantView("player-1").Entities.Single(u=>u.Id==actor.Id).Position.Equals(origin);n++){NativeStep(a,routes);NativeStep(b,routes);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                    Assert.False(a.ParticipantView("player-1").Entities.Single(u=>u.Id==actor.Id).Position.Equals(origin),"Safe control must physically move through native navigation");
                }
                TestContext.WriteLine("A5 withdrawal due "+Newtonsoft.Json.JsonConvert.SerializeObject(new{intrusion,due,a.Tick,Action=action,Terminal=terminal,Before=before.Entities}));b.Stop();
            }
            a.Stop();
        }
        [Test] public void Retreat_DueProofSubsetKeepsCoreHomeAndExactCurrentLegWithoutUnrelatedGather()
        {
            var r=Registry();var mission=A4Mission(r);var o=A5Observation("local",101,move:true);mission.SetPhase(r,AiArmyPhase.Retreating,101);
            var state=r.Capture();var method=typeof(AiTacticalExecutor).GetMethod("RouteRequests",BindingFlags.NonPublic|BindingFlags.Static);
            var requests=(PlayableRouteRequest[])method.Invoke(null,new object[]{o.Entities,o.Buildings,state,o.Owner,o.Generation,o.Tick,PlayableProfile.Default});
            Assert.True(requests.Any(x=>x.UnitId==1&&x.TargetId==50),"Live ready home is available for a core retreat");
            Assert.True(requests.Any(x=>x.UnitId==1&&x.TargetId==1&&x.Target.Equals(o.Entities.First().CurrentOrder.Destination)),"The exact effective current core leg is re-certified");
            Assert.False(requests.Any(x=>x.UnitId==1&&x.TargetId==2),"An existing core is not a newly gathering reinforcement");
            Assert.True(state.Armies.Single().Members.All(id=>requests.Where(x=>x.UnitId==id).All(x=>x.TargetId==id||o.Buildings.Any(b=>b.Id==x.TargetId))));
            Assert.IsEmpty((PlayableRouteRequest[])method.Invoke(null,new object[]{o.Entities,o.Buildings,state,PlayableOwner.Enemy,o.Generation,o.Tick,PlayableProfile.Default}),"Another participant cannot route these members");
            state.Generation++;
            Assert.IsEmpty((PlayableRouteRequest[])method.Invoke(null,new object[]{o.Entities,o.Buildings,state,o.Owner,o.Generation,o.Tick,PlayableProfile.Default}),"A stale registry generation cannot request current certificates");
        }
        [TestCase("ready",true)][TestCase("applied",false)][TestCase("unapplied",true)][TestCase("human",true)][TestCase("cadence",false)][TestCase("missing",false)][TestCase("wrong-goal",false)][TestCase("stale-proof",false)][TestCase("unsafe",false)][TestCase("foreign-leader",false)]
        public void Retreat_RecoveryAssemblyBootstrapsOnlyCurrentCertifiedOwnFollow(string mode,bool expected)
        {
            var p=PlayableProfile.Default;var r=Registry();var mission=A4Mission(r);var old=A4Observation(400,wave:true,near:true,danger:mode=="unsafe");long id=mission.Capture().ArmyId;AiTacticalExecutor.Observe(old,r,id,p);mission.SetPhase(r,AiArmyPhase.Recovering,400);
            var goal=PlayableUnitRules.FollowGoal(p,old.Entities.Single(u=>u.Id==1).Position,0);bool order=mode=="applied"||mode=="unapplied"||mode=="human"||mode=="cadence";long issued=mode=="cadence"?399:10;
            var units=old.Entities.Select(u=>u.Id==4?new PlayableEntitySnapshot(4,u.Owner,u.Kind,u.Position,u.Health,false,0,0,0,order?new PlayableTacticalOrderSnapshot(4,u.Owner,71,2,issued,PlayableTacticalOrderKind.Follow,default,1):null,navigationOutcome:NavigationOutcome.Arrived,orderStamp:order?new PlayableOrderStamp(4,u.Owner,71,2,2,issued,mode=="human"?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,PlayableCommandKind.Follow,PlayableAiOpeningComposition.SourceIdentity,2,2):null):u.Id==1&&mode=="foreign-leader"?new PlayableEntitySnapshot(u.Id,PlayableOwner.Enemy,u.Kind,u.Position,u.Health,false,0,0,0):u).ToArray();
            var proof=new PlayableRouteProof(4,PlayableOwner.Player,mode=="stale-proof"?70:71,400,1,units.Single(u=>u.Id==4).Position,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,1,goal,mode=="wrong-goal"?new NavPoint(goal.X-1,goal.Z):goal,new[]{mode=="wrong-goal"?new NavPoint(goal.X-1,goal.Z):goal});
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,71,old.Seed,400,400,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:old.RouteProofs.Concat(mode=="missing"?Array.Empty<PlayableRouteProof>():new[]{proof}).ToArray(),activeProfile:p));
            var records=new[]{new PlayableAiTraceRecord(o.Identity,2,issued,2,issued,mode=="unapplied"?PlayableAiDeliveryStatus.Cancelled:PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"fixture",ownerId:o.OwnerId,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity,receiptIdentity:new AiReceiptIdentity(71,o.OwnerId,1,2),policy:AiArmyPlanner.Policy,kind:PlayableCommandKind.Follow)};
            var army=r.Capture().Armies.Single();var before=Newtonsoft.Json.JsonConvert.SerializeObject(r.Capture());long repeat=AiProfile.SecondsToTicks(AiProfile.Initial.Value("decision.repeatOrderSeconds"),30);
            var action=(PlayableAiAction)typeof(AiTacticalExecutor).GetMethod("RecoveryAssembly",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{o,army,p,99L,repeat,records});
            Assert.AreEqual(expected,action!=null);if(expected){Assert.AreEqual(PlayableCommandKind.Follow,action.Kind);Assert.AreEqual(1,action.TargetId);CollectionAssert.AreEqual(new[]{4},action.EntityIds);}
            Assert.AreEqual(before,Newtonsoft.Json.JsonConvert.SerializeObject(r.Capture()),"No fake promotion before applied native assembly");
            if(mode=="ready")Assert.True(AiTacticalExecutor.Ready(o,army,p).Any(u=>u.Id==4),"Physical Ready alone still needs an effective assembly order during recovery");
        }
    }
}
