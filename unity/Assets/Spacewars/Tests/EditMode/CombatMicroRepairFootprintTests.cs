using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class CombatMicroRepairFootprintTests
    {
        const long Gen=906;
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile Ai=AiProfile.Initial;
        static readonly NavPoint BadGoal=new NavPoint(-20.000944116409144,10.12668499358524);
        static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
        static AiCombatMicroPlan Plan(PlayableAiObservation o)
        {var m=new AiCombatMicroPolicy();var r=new AiArmyRegistry(o.OwnerId,Gen,Ai,AiDifficulty.Veteran);m.Observe(o,P,Ai,AiDifficulty.Veteran,r);return m.Plan(o,P,Ai,AiDifficulty.Veteran,r);}
        sealed class World:IDisposable
        {
            internal readonly PlayableDomain D;
            internal readonly int Patient,Tower;
            internal World(bool air=false)
            {
                var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,10),new NavPoint(-18,-12),pin:1),new OfflineStart("east",2,new NavPoint(24,21),new NavPoint(25,27),pin:2)};
                var site=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,P);var pad=site.DefenseSlots(P).Single(x=>x.Id==1003);
                var config=new OfflineMatchConfiguration(P,"repair-footprint-control","flat-seed7","native-raw-target-control",7,
                    new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,
                    new[]{site,new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())},Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},
                    scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-17.010470583976183,10.171260311769055)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-15,25)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-11,25))},
                    scenarioBuildings:new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.RepairTower,1,pad.Id,pad.Position,pad.Heading)});
                D=new PlayableDomain(P,Gen,config);
                var view=View();Patient=air?(int)typeof(PlayableDomain).GetMethod("SpawnUnit",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(D,new object[]{new NavPoint(-17.010470583976183,10.171260311769055),PlayableOwner.Player,PlayableEntityKind.Hawk}):view.Entities.Where(u=>u.Owner==view.Owner&&u.Kind==PlayableEntityKind.Tank).OrderBy(u=>u.Id).First().Id;
                var patient=View().Entities.Single(u=>u.Id==Patient);
                typeof(PlayableDomain).GetMethod("Damage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(D,new object[]{Patient,(int)Math.Round(patient.MaxHealth*.72)});
                Tower=view.Buildings.Single(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.RepairTower).Id;
            }
            internal PlayableSnapshot View()=>D.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player);
            internal PlayableAiAction Action(long id,NavPoint goal)=>new PlayableAiAction(id,"player-1",P.ProfileId,P.Revision,Gen,0,PlayableCommandKind.Move,new[]{Patient},goal,parentId:Tower,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            internal PlayableCommand Command(long sequence,PlayableAiAction action)=>new PlayableCommand(Gen,sequence,"player-1",action.Kind,action.CopyEntityIds(),action.Target,parentId:action.ParentId,origin:PlayableOrderOrigin.Ai,source:action.SourceIdentity,jobId:action.ActionId,actionId:action.ActionId);
            public void Dispose(){D.Navigation.ReleaseCommittedHostRoot();D.DestroyOwnedNavigation();}
        }
        [Test]
        public void NativeRawHeadquartersTargetIsRejectedWhileNewRepairCandidateAppliesAndMoves()
        {
            using var w=new World();var before=w.View();var patient=before.Entities.Single(u=>u.Id==w.Patient);var old=w.Action(80,BadGoal);
            Assert.False(w.D.Geometry.IsFree(BadGoal,0));Assert.IsNull(w.D.ArmyMoveRejection(old,true),"Allocated safety slot does not prove raw target legality");
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,w.D.Apply(w.Command(1,old),out var rejected));Assert.AreEqual("Invalid destination.",rejected);Assert.IsNull(w.View().Entities.Single(u=>u.Id==w.Patient).OrderStamp);
            var o=PlayableAiObservation.From(w.View());Assert.False(o.Buildings.Any(b=>b.Owner==PlayableOwner.Enemy),"Hidden foreign HQ is absent from actual owner observation");
            var repair=Plan(o);Assert.NotNull(repair);Assert.AreEqual(AiCombatMicroPhase.Repair,repair.Phase);Assert.AreEqual(w.Tower,repair.Action.ParentId);Assert.False(repair.Action.Target.Equals(BadGoal));
            Assert.True(w.D.Geometry.IsFree(repair.Action.Target,PlayableUnitRules.Radius(P,patient.Kind)));Assert.IsNull(w.D.ArmyMoveRejection(repair.Action,true));
            var accepted=w.Action(81,repair.Action.Target);Assert.AreEqual(PlayableCommandStatus.Applied,w.D.Apply(w.Command(2,accepted),out var message),message);
            var host=new CertifiedRouteKernel();bool moved=false;
            try{
                for(int tick=0;tick<90&&!moved;tick++){
                    for(int n=0;n<128&&w.D.Navigation.PendingCount>0;n++){host.Pump(new PlayableRouteBinding(w.D.Geometry,P,w.D.Navigation.Admission,w.D.Navigation.RoutePort),4);w.D.Navigation.ApplyResults();}
                    w.D.Step(1d/30);var actor=w.View().Entities.Single(u=>u.Id==w.Patient);
                    Assert.AreEqual(81,actor.OrderStamp.ActionId);Assert.AreEqual(2,actor.OrderStamp.Sequence);
                    moved=Distance(actor.Position,patient.Position)>1&&Distance(actor.Position,accepted.Target)<Distance(patient.Position,accepted.Target)-1;
                }
                Assert.True(moved,"Real native command installation and domain steps must move the patient toward the alternative raw goal");
                TestContext.WriteLine($"NATIVE_REPAIR bad={BadGoal.X:R},{BadGoal.Z:R} alternative={accepted.Target.X:R},{accepted.Target.Z:R} motion={moved} tick={w.D.Tick}");
            }finally{host.Clear();}
        }
        [Test]
        public void NativeHawkRepairKeepsOverflightOfGroundBuildingFootprints()
        {
            using var w=new World(true);var o=PlayableAiObservation.From(w.View());var plan=Plan(o);Assert.NotNull(plan);Assert.AreEqual(AiCombatMicroPhase.Repair,plan.Phase);Assert.Contains(w.Patient,plan.Action.CopyEntityIds());
            Assert.False(w.D.Geometry.IsFree(plan.Action.Target,0),"This real air repair candidate crosses the ground HQ footprint");
            Assert.AreEqual(PlayableCommandStatus.Applied,w.D.Apply(w.Command(1,w.Action(82,plan.Action.Target)),out var message),message);
            Assert.AreEqual(82,w.View().Entities.Single(u=>u.Id==w.Patient).OrderStamp.ActionId);
        }
        static PlayableAiObservation Observation(PlayableBuildingSnapshot blocker=null)
        {
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),50)},Array.Empty<KnownBuilding>());
            var patient=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(13,0),28,false,0,0,0,maxHealth:100);
            var tower=new PlayableBuildingSnapshot(200,PlayableOwner.Player,PlayableBuildingKind.RepairTower,new NavPoint(0,0),100,1,0,0,default,phase:ConstructionPhase.Ready);
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,0,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,new[]{patient},blocker==null?new[]{tower}:new[]{tower,blocker},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot()));
        }
        [TestCase(PlayableOwner.Player,ConstructionPhase.Ready,100,true)]
        [TestCase(PlayableOwner.Enemy,ConstructionPhase.Ready,100,true)]
        [TestCase((PlayableOwner)2,ConstructionPhase.Constructing,100,true)]
        [TestCase(PlayableOwner.Player,ConstructionPhase.Constructing,100,true)]
        [TestCase(PlayableOwner.Player,ConstructionPhase.Pending,100,false)]
        [TestCase(PlayableOwner.Player,ConstructionPhase.Ready,0,false)]
        public void CurrentObservedNativeSolidPhasesExcludeRawGoalRegardlessOfOwner(PlayableOwner owner,ConstructionPhase phase,int health,bool solid)
        {
            var baseline=Plan(Observation());Assert.NotNull(baseline);var goal=baseline.Action.Target;
            var blocker=new PlayableBuildingSnapshot(201,owner,PlayableBuildingKind.Headquarters,goal,health,1,0,0,default,phase:phase);
            var plan=Plan(Observation(blocker));Assert.NotNull(plan);Assert.AreEqual(!solid,plan.Action.Target.Equals(goal));
            if(solid){double half=TerritoryRules.Radius(P,blocker.Kind);var native=new NavGeometry(P.ArenaHalfExtent,new[]{new NavObstacle(goal.X-half,goal.Z-half,goal.X+half,goal.Z+half)},1);Assert.False(native.IsFree(goal,P.TankCollisionRadius));Assert.True(native.IsFree(plan.Action.Target,P.TankCollisionRadius));}
        }
        [TestCase(-.000001)][TestCase(0d)][TestCase(.000001)]
        public void RawGoalClearanceAndTangencyAgreeWithNativeSquareGeometry(double delta)
        {
            var baseline=Plan(Observation());var goal=baseline.Action.Target;double half=P.HeadquartersFootprintRadius;
            var centre=new NavPoint(goal.X+half+P.TankCollisionRadius+delta,goal.Z);var blocker=new PlayableBuildingSnapshot(201,PlayableOwner.Player,PlayableBuildingKind.Headquarters,centre,400,1,0,0,default,phase:ConstructionPhase.Ready);
            var native=new NavGeometry(P.ArenaHalfExtent,new[]{new NavObstacle(centre.X-half,centre.Z-half,centre.X+half,centre.Z+half)},1);var plan=Plan(Observation(blocker));Assert.NotNull(plan);
            Assert.AreEqual(native.IsFree(goal,P.TankCollisionRadius),plan.Action.Target.Equals(goal),"Use the production geometry as the independent clearance oracle, including exact floating boundary semantics");
            Assert.True(native.IsFree(plan.Action.Target,P.TankCollisionRadius));
        }
    }
}
