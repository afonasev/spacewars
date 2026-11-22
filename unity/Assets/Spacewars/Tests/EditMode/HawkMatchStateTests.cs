using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class HawkMatchStateTests
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        sealed class Scene : IDisposable
        {
            internal PlayableProfile Profile=PlayableProfile.Default;
            internal PlayableDomain Domain;
            internal int Hangar;internal long Sequence;
            internal Scene(PlayableProfile profile=null)
            {
                Profile=profile??Profile;
                Domain=new PlayableDomain(Profile,1,false);Call("AddCredits",PlayableOwner.Player,10000d);
                Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.BuildAt,Array.Empty<int>(),site:1,slot:1,parent:1));Call("AdvanceFoundations");Call("AdvanceBuildings",(double)Profile.HangarBuildSeconds+1);Hangar=View.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Hangar).Id;
            }
            internal object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,Hidden).Invoke(Domain,args);
            internal PlayableSnapshot View=>Domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            internal PlayableBuildingSnapshot Building=>View.Buildings.Single(b=>b.Id==Hangar);
            internal PlayableCommandStatus Send(PlayableCommandKind kind,int[] ids,NavPoint target=default,int site=0,int slot=0,int parent=0,int targetId=0,PlayableEntityKind unit=PlayableEntityKind.Tank)=>Domain.Apply(new PlayableCommand(1,++Sequence,"player-1",kind,ids,target,siteId:site,slotId:slot,parentId:parent,buildingKind:PlayableBuildingKind.Hangar,targetId:targetId,unitKind:unit),out _);
            internal void Advance(int ticks=1){for(int i=0;i<ticks;i++){Call("AdvanceProduction",1d/30);Call("AdvanceHawks",1d/30);}}
            internal void Order(){Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueHawk,new[]{Hangar}));}
            public void Dispose(){Domain.Navigation.ReleaseCommittedHostRoot();Domain.DestroyOwnedNavigation();}
        }
        [TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)]
        public void CurrentFormatRoundTripAndExactContinuationOfEachLaunchPhase(int phase)
        {
            using var s=new Scene();s.Order();int steps=0;while(s.Building.LaunchPhase!=phase&&steps++<1800)s.Advance();Assert.AreEqual(phase,s.Building.LaunchPhase);
            var bytes=s.Domain.CaptureWorldBytes(7,"hawk-current-format");var restored=PlayableDomain.RestoreWorldBytes(bytes,s.Profile,7,"hawk-current-format");var original=s.Domain;
            try
            {
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"hawk-current-format"));
                for(int i=0;i<60;i++){s.Domain=original;s.Advance();s.Domain=restored;s.Advance();}
                CollectionAssert.AreEqual(original.CaptureWorldBytes(7,"hawk-current-format"),restored.CaptureWorldBytes(7,"hawk-current-format"));
            }
            finally{s.Domain=original;restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }
        [Test] public void AircraftExistsOnlyAfterPlatformAndSpinAndStartsBelowRim()
        {
            using var s=new Scene();s.Order();
            for(int step=0;s.Building.LaunchPhase!=4&&step<1800;step++)
            {
                Assert.False(s.View.Entities.Any(e=>e.Kind==PlayableEntityKind.Hawk));
                s.Advance();
            }
            Assert.AreEqual(4,s.Building.LaunchPhase);
            var hawk=s.View.Entities.Single(e=>e.Kind==PlayableEntityKind.Hawk);
            // The production step may include one vertical flight tick, never a
            // jump from the roof or horizontal departure before safe clearance.
            Assert.That(hawk.Altitude,Is.InRange(HawkHangarGeometry.LaunchHeight(s.Profile),HawkHangarGeometry.LaunchHeight(s.Profile)+s.Profile.HawkClimbSpeed/30+1e-6));
            Assert.Less(hawk.Altitude,s.Profile.HangarModelHeightMeters*s.Profile.HangarModelScale);
            Assert.AreEqual(s.Building.Position,hawk.Position);
        }
        [Test] public void HangarSixPaidSlotsRepeatAndCancellationUseFactoryQueueContract()
        {
            using var s=new Scene();double credits=s.View.ExactCredits;
            for(int i=0;i<6;i++)s.Order();
            Assert.AreEqual(credits-6*s.Profile.HawkCreditCost,s.View.ExactCredits);
            Assert.AreEqual(PlayableCommandStatus.Overflow,s.Send(PlayableCommandKind.QueueHawk,new[]{s.Hangar}));
            var ids=s.Building.PrivateState.Orders.Select(o=>o.Id).ToArray();s.Advance();
            var head=s.Building.PrivateState.Orders[0];Assert.True(head.Active);
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(PlayableCommandKind.ToggleRepeatProduction,new[]{s.Hangar},unit:PlayableEntityKind.Hawk));
            Assert.True(s.Building.PrivateState.Repeat);Assert.AreEqual(PlayableEntityKind.Hawk,s.Building.PrivateState.RepeatKind);
            CollectionAssert.AreEqual(ids,s.Building.PrivateState.Orders.Select(o=>o.Id));Assert.AreEqual(head.Remaining,s.Building.PrivateState.Orders[0].Remaining);
            var cancel=new PlayableCommand(1,++s.Sequence,"player-1",PlayableCommandKind.CancelProductionOrder,new[]{s.Hangar},productionOrderId:ids[2]);
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Domain.Apply(cancel,out _));
            Assert.True(s.Building.PrivateState.Repeat);CollectionAssert.AreEqual(ids.Where(id=>id!=ids[2]),s.Building.PrivateState.Orders.Select(o=>o.Id));
            double refunded=s.View.ExactCredits;Assert.AreEqual(credits-6*s.Profile.HawkCreditCost+Math.Floor(s.Profile.HawkCreditCost*s.Profile.UnitCancellationRefundRatio),refunded,1e-8);
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,s.Domain.Apply(new PlayableCommand(1,++s.Sequence,"player-1",PlayableCommandKind.CancelProductionOrder,new[]{s.Hangar},productionOrderId:ids[2]),out _));Assert.AreEqual(refunded,s.View.ExactCredits);
        }
        [Test] public void HangarRejectsGroundOrdersAndFactoryRejectsAircraft()
        {
            using var s=new Scene();Assert.AreEqual(PlayableCommandStatus.InvalidEntity,s.Send(PlayableCommandKind.QueueTank,new[]{s.Hangar}));Assert.AreEqual(PlayableCommandStatus.InvalidEntity,s.Send(PlayableCommandKind.ToggleRepeatProduction,new[]{s.Hangar}));s.Order();Assert.AreEqual(1,s.Building.PrivateState.Orders.Count);
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(PlayableCommandKind.ToggleRepeatProduction,new[]{s.Hangar},unit:PlayableEntityKind.Hawk));Assert.AreEqual(PlayableEntityKind.Hawk,s.Building.PrivateState.RepeatKind);
        }
        [Test] public void CancelOnPlatformNeverCreatesAnUnpaidAircraft()
        {
            using var s=new Scene();s.Order();for(int i=0;i<1800&&s.Building.LaunchPhase!=3;i++)s.Advance();Assert.AreEqual(3,s.Building.LaunchPhase);
            var order=s.Building.PrivateState.Orders.Single();Assert.AreEqual(PlayableCommandStatus.Applied,s.Domain.Apply(new PlayableCommand(1,++s.Sequence,"player-1",PlayableCommandKind.CancelProductionOrder,new[]{s.Hangar},productionOrderId:order.Id),out _));s.Advance(300);Assert.False(s.View.Entities.Any(e=>e.Kind==PlayableEntityKind.Hawk));Assert.Zero(s.Building.LaunchPhase);
        }
        [Test] public void MixedSelectionAdmitsSeparateAirIntentAndGroundRoute()
        {
            using var s=new Scene();int air=(int)s.Call("SpawnUnit",new NavPoint(0,15),PlayableOwner.Player,PlayableEntityKind.Hawk);var scout=s.View.Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer);var goal=new NavPoint(8,15);
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(PlayableCommandKind.Move,new[]{air,scout.Id},goal));s.Advance(120);var current=s.View.Entities.Single(e=>e.Id==air);Assert.Greater(current.Position.X,0);Assert.True(s.Domain.Navigation.IsPending(scout.Id)||s.Domain.Navigation.Crowd.Units.Single(n=>n.Id==scout.Id).Moving);
        }
        [Test] public void MixedFormationAlignsHawkWithTankAndPlacesSkatBehindTheFront()
        {
            var data = PlayableProfile.Default.CopyData();
            data.groundRouteBackend = 1;
            using var s = new Scene(PlayableProfile.Create(data));
            int scout = (int)s.Call("SpawnUnit", new NavPoint(-18, -14), PlayableOwner.Player, PlayableEntityKind.Explorer);
            int tank = (int)s.Call("SpawnUnit", new NavPoint(-18, -10), PlayableOwner.Player, PlayableEntityKind.Tank);
            int hawk = (int)s.Call("SpawnUnit", new NavPoint(-18, -6), PlayableOwner.Player, PlayableEntityKind.Hawk);
            int skat = (int)s.Call("SpawnUnit", new NavPoint(-18, -2), PlayableOwner.Player, PlayableEntityKind.Skat);
            var goal = new NavPoint(-10, -12);
            var units = (System.Collections.IDictionary)typeof(PlayableDomain).GetField("units", Hidden).GetValue(s.Domain);
            NavPoint AirGoal(int id)
            {
                var unit = units[id];
                var state = unit.GetType().GetField("Air").GetValue(unit);
                return ((NavUnit)state.GetType().GetField("Actor").GetValue(state)).Goal;
            }

            Assert.AreEqual(PlayableCommandStatus.Applied, s.Send(PlayableCommandKind.Move, new[] { hawk, scout, tank }, goal));
            var ground = s.Domain.Navigation;
            var tankSlot = ground.PendingRequestFor(tank).Goal;
            Assert.That(Distance(tankSlot, AirGoal(hawk)), Is.LessThan(1e-8));

            Assert.AreEqual(PlayableCommandStatus.Applied, s.Send(PlayableCommandKind.Move,
                new[] { scout, tank, skat }, new NavPoint(-6, -20)));
            var front = ground.PendingRequestFor(scout).Goal;
            var repair = AirGoal(skat);
            var direction = ground.GroundFormationDirection(new NavPoint(-6, -20), new[] { scout, tank });
            double behind = NavFormationLayout.Dot(new NavPoint(front.X - repair.X, front.Z - repair.Z), direction);
            Assert.Greater(behind, 0);
        }

        [Test] public void CentralGroundClickUsesTheRetainedApproachBeforeAverageHeading()
        {
            var data = PlayableProfile.Default.CopyData();
            data.groundRouteBackend = 1;
            using var s = new Scene(PlayableProfile.Create(data));
            int first = (int)s.Call("SpawnUnit", new NavPoint(-18, -14), PlayableOwner.Player, PlayableEntityKind.Explorer);
            int second = (int)s.Call("SpawnUnit", new NavPoint(-18, -10), PlayableOwner.Player, PlayableEntityKind.Tank);
            var actors = new[] { first, second };
            var center = new NavPoint(-18, -12);
            var navigation = s.Domain.Navigation;
            foreach (var actor in navigation.Crowd.Units.Where(unit => actors.Contains(unit.Id))) actor.Heading = Math.PI / 2;
            Assert.That(Distance(new NavPoint(0, 1), navigation.GroundFormationDirection(center, actors)),
                Is.LessThan(1e-8));
            Assert.AreEqual(PlayableCommandStatus.Applied, s.Send(PlayableCommandKind.Move, actors, new NavPoint(-10, -12)));
            Assert.That(Distance(new NavPoint(1, 0), navigation.GroundFormationDirection(center, actors)),
                Is.LessThan(1e-8));
        }
        [Test, Timeout(30000)] public void OccupiedGroundSlotReassignsOnlyTheBlockedMemberAndRestores()
        {
            var data = PlayableProfile.Default.CopyData();
            data.groundRouteBackend = 1;
            using var s = new Scene(PlayableProfile.Create(data));
            int scout = (int)s.Call("SpawnUnit", new NavPoint(-18, -14), PlayableOwner.Player, PlayableEntityKind.Explorer);
            int tank = (int)s.Call("SpawnUnit", new NavPoint(-18, -10), PlayableOwner.Player, PlayableEntityKind.Tank);
            Assert.AreEqual(PlayableCommandStatus.Applied,
                s.Send(PlayableCommandKind.Move, new[] { scout, tank }, new NavPoint(-10, -12)));
            var navigation = s.Domain.Navigation;
            var kernel = new CertifiedRouteKernel();
            try
            {
                for (int i = 0; i < 20000 && (navigation.IsPending(scout) || navigation.IsPending(tank)); i++)
                {
                    kernel.Pump(new PlayableRouteBinding(s.Domain.Geometry, s.Domain.RoutingProfile,
                        navigation.Admission, navigation.RoutePort), 16);
                    navigation.ApplyResults();
                }
                Assert.True(navigation.Crowd.TryGet(scout, out var actor) && actor.Moving);
                Assert.True(navigation.Crowd.TryGet(tank, out var partner) && partner.Moving);
                var assigned = actor.GoalLocation.Value;
                long group = actor.InstalledGroupId;
                long partnerRequest = partner.InstalledRequest;
                var partnerGoal = partner.GoalLocation;

                // Stage the physical result of a sustained obstruction after
                // both routes were installed, with a peer now in the old slot.
                s.Call("SpawnUnit", assigned.Position, PlayableOwner.Player, PlayableEntityKind.Tank);
                actor.Position = new NavPoint(assigned.Position.X - 2, assigned.Position.Z);
                actor.Location = new NavLocation(actor.Position, assigned.SurfaceId);
                navigation.Crowd.Reject(scout, NavigationOutcome.Blocked);
                var units = (System.Collections.IDictionary)typeof(PlayableDomain).GetField("units", Hidden).GetValue(s.Domain);
                s.Call("RetireExhaustedTacticalMove", units[scout], actor);

                var replacement = navigation.PendingRequestFor(scout);
                Assert.NotNull(replacement);
                Assert.AreEqual(group, replacement.MemberIdentity.GroupId);
                Assert.AreEqual(assigned.SurfaceId, replacement.GoalLocation.Value.SurfaceId);
                Assert.AreEqual(assigned.Position.X, replacement.Goal.X, 1e-8);
                Assert.AreNotEqual(assigned.Position.Z, replacement.Goal.Z);
                Assert.False(navigation.IsPending(tank));
                Assert.AreEqual(partnerRequest, partner.InstalledRequest);
                Assert.AreEqual(partnerGoal, partner.GoalLocation);

                var bytes = s.Domain.CaptureWorldBytes(7, "blocked-formation");
                var restored = PlayableDomain.RestoreWorldBytes(bytes, s.Profile, 7, "blocked-formation");
                try { CollectionAssert.AreEqual(bytes, restored.CaptureWorldBytes(7, "blocked-formation")); }
                finally { restored.Navigation.ReleaseCommittedHostRoot(); restored.DestroyOwnedNavigation(); }
            }
            finally { kernel.Clear(); }
        }
        [Test] public void AirGroupHasDistinctArrivalsAndNeverOverlaps()
        {
            using var s=new Scene();int a=(int)s.Call("SpawnUnit",new NavPoint(0,15),PlayableOwner.Player,PlayableEntityKind.Hawk),b=(int)s.Call("SpawnUnit",new NavPoint(0,18),PlayableOwner.Player,PlayableEntityKind.Hawk);
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(PlayableCommandKind.Move,new[]{a,b},new NavPoint(15,15)));
            for(int i=0;i<900;i++){s.Advance();var air=s.View.Entities.Where(e=>e.Kind==PlayableEntityKind.Hawk).ToArray();double distance=Math.Sqrt(Math.Pow(air[0].Position.X-air[1].Position.X,2)+Math.Pow(air[0].Position.Z-air[1].Position.Z,2));Assert.GreaterOrEqual(distance,2*s.Profile.HawkRadius+s.Profile.HawkSeparation-1e-9);}
            Assert.True(s.View.Entities.Where(e=>e.Kind==PlayableEntityKind.Hawk).All(e=>e.NavigationOutcome==NavigationOutcome.Arrived));
        }
        [TestCase(PlayableCommandKind.Move)]
        [TestCase(PlayableCommandKind.AttackMove)]
        public void SeparateAirOrdersReserveDestinationsBeforeEitherAircraftArrives(PlayableCommandKind kind)
        {
            using var s=new Scene();
            int first=(int)s.Call("SpawnUnit",new NavPoint(0,15),PlayableOwner.Player,PlayableEntityKind.Hawk);
            int second=(int)s.Call("SpawnUnit",new NavPoint(0,23),PlayableOwner.Player,PlayableEntityKind.Hawk);
            var goal=new NavPoint(15,15);
            var spacing=2*s.Profile.HawkRadius+s.Profile.HawkSeparation;
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(kind,new[]{first},goal));
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(kind,new[]{second},goal));
            var domainUnits=(System.Collections.IDictionary)typeof(PlayableDomain).GetField("units",Hidden).GetValue(s.Domain);
            NavPoint Destination(int id)
            {
                var unit=domainUnits[id];
                return ((PlayableTacticalOrderSnapshot)unit.GetType().GetField("CurrentOrder").GetValue(unit)).Destination;
            }
            Assert.AreEqual(goal,Destination(first));
            Assert.GreaterOrEqual(Distance(Destination(first),Destination(second)),spacing+2*s.Profile.Navigation.ArrivalTolerance-1e-9);
            s.Advance(900);
            var air=s.View.Entities.Where(e=>e.Id==first||e.Id==second).ToArray();
            Assert.That(air.Select(e=>e.NavigationOutcome),Is.All.EqualTo(NavigationOutcome.Arrived));
            Assert.GreaterOrEqual(Distance(air[0].Position,air[1].Position),spacing-1e-9);
        }
        [Test] public void RecordedAirDestinationsReproduceMoveAndHoldWithoutSlotSearch()
        {
            using var source = new Scene();
            using var replay = new Scene();
            int[] Spawn(Scene s) => new[] { new NavPoint(0,15), new NavPoint(0,23) }
                .Select(p => (int)s.Call("SpawnUnit", p, PlayableOwner.Player, PlayableEntityKind.Hawk)).ToArray();
            var ids = Spawn(source);
            CollectionAssert.AreEqual(ids, Spawn(replay));
            string record = null;
            source.Domain.AirAssignmentRecorded = row => record = Newtonsoft.Json.JsonConvert.SerializeObject(row);
            var goal = new NavPoint(15,15);
            Assert.AreEqual(PlayableCommandStatus.Applied, source.Send(PlayableCommandKind.Move, ids, goal));
            Assert.IsNotNull(record);
            var row = Newtonsoft.Json.JsonConvert.DeserializeObject<RecordedAirAssignment>(record);
            replay.Domain.ReplayAirAssignments = new System.Collections.Generic.Queue<RecordedAirAssignment>(new[] { row });
            replay.Domain.AirAssignmentRecorded = _ => Assert.Fail("Replay ran the formation planner.");
            Assert.AreEqual(PlayableCommandStatus.Applied, replay.Send(PlayableCommandKind.Move, ids, goal));
            Assert.AreEqual(0, replay.Domain.ReplayAirAssignments.Count);
            void CompareAfter(int ticks)
            {
                source.Advance(ticks);
                replay.Advance(ticks);
                ExactByteAssert.AreEqual(source.Domain.CaptureWorldBytes(7, "air-replay"),
                    replay.Domain.CaptureWorldBytes(7, "air-replay"));
            }
            CompareAfter(60);
            Assert.AreEqual(PlayableCommandStatus.Applied, source.Send(PlayableCommandKind.Hold, ids));
            Assert.AreEqual(PlayableCommandStatus.Applied, replay.Send(PlayableCommandKind.Hold, ids));
            CompareAfter(30);
        }
        [Test] public void AirFormationLeavesRoomForBrakingNearOtherArrivalSlots()
        {
            using var s=new Scene();
            var ids=new[]{new NavPoint(0,15),new NavPoint(0,18),new NavPoint(0,21),new NavPoint(0,24)}
                .Select(from=>(int)s.Call("SpawnUnit",from,PlayableOwner.Player,PlayableEntityKind.Hawk)).ToArray();
            var goal=new NavPoint(15,15);
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(PlayableCommandKind.Move,ids,goal));
            s.Advance(900);
            var air=s.View.Entities.Where(e=>ids.Contains(e.Id)).ToArray();
            Assert.That(air.Select(e=>e.NavigationOutcome),Is.All.EqualTo(NavigationOutcome.Arrived));
            Assert.That(air.Select(e=>Distance(e.Position,goal)),Is.All.LessThan(2*s.Profile.HawkRadius+
                s.Profile.HawkSeparation+3*s.Profile.Navigation.ArrivalTolerance+1e-9));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void HawkCanBypassAStoppedPeerOnItsPreferredSide(bool swapParity)
        {
            using var s=new Scene();
            if(swapParity)s.Call("SpawnUnit",new NavPoint(-15,15),PlayableOwner.Player,PlayableEntityKind.Hawk);
            var start=new NavPoint(0,15);
            var goal=new NavPoint(15,15);
            int mover=(int)s.Call("SpawnUnit",start,PlayableOwner.Player,PlayableEntityKind.Hawk);
            var spacing=2*s.Profile.HawkRadius+s.Profile.HawkSeparation;
            var contact=spacing+1e-8;
            var sign=(mover&1)==0?1:-1;
            var stationary=new NavPoint(start.X+contact/2,start.Z+sign*contact*Math.Sqrt(3)/2);
            int peer=(int)s.Call("SpawnUnit",stationary,PlayableOwner.Player,PlayableEntityKind.Hawk);
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(PlayableCommandKind.Move,new[]{mover},goal));
            var last=start;
            for(int i=0;i<900;i++){
                s.Advance();
                var units=s.View.Entities;
                var flying=units.Single(e=>e.Id==mover);
                Assert.AreEqual(stationary,units.Single(e=>e.Id==peer).Position);
                double dx=flying.Position.X-last.X,dz=flying.Position.Z-last.Z;
                double fraction=dx==0&&dz==0?0:Math.Max(0,Math.Min(1,((stationary.X-last.X)*dx+(stationary.Z-last.Z)*dz)/(dx*dx+dz*dz)));
                Assert.GreaterOrEqual(Distance(stationary,new NavPoint(last.X+dx*fraction,last.Z+dz*fraction)),spacing-1e-9);
                last=flying.Position;
            }
            Assert.AreEqual(NavigationOutcome.Arrived,s.View.Entities.Single(e=>e.Id==mover).NavigationOutcome);
            Assert.LessOrEqual(Distance(last,goal),s.Profile.Navigation.ArrivalTolerance+1e-9);
        }
        [TestCase(PlayableCommandKind.Move)]
        [TestCase(PlayableCommandKind.AttackMove)]
        public void HawkClimbsForABypassBetweenAParkedPeerAndMossRidge(PlayableCommandKind kind)
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new MossHighlandMap());
            var domain=new PlayableDomain(profile,1,false);
            try
            {
                var start=new NavPoint(52.884754696311617,20.963180084891796);
                var parked=new NavPoint(50.853861247099324,19.977842363729067);
                var goal=new NavPoint(48.55,18);
                var spawn=typeof(PlayableDomain).GetMethod("SpawnUnit",Hidden);
                int mover=(int)spawn.Invoke(domain,new object[]{start,PlayableOwner.Player,PlayableEntityKind.Hawk});
                int peer=(int)spawn.Invoke(domain,new object[]{parked,PlayableOwner.Player,PlayableEntityKind.Hawk});
                var units=(System.Collections.IDictionary)typeof(PlayableDomain).GetField("units",Hidden).GetValue(domain);
                var unit=units[mover];
                var air=unit.GetType().GetField("Air").GetValue(unit);
                var actor=(NavUnit)air.GetType().GetField("Actor").GetValue(air);
                actor.Heading=Math.Atan2(goal.Z-start.Z,goal.X-start.X);
                Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(
                    1,1,"player-1",kind,new[]{mover},goal),out _));
                Assert.AreEqual(goal,actor.Goal);
                var advance=(Action<double>)typeof(PlayableDomain).GetMethod("AdvanceHawks",Hidden).CreateDelegate(typeof(Action<double>),domain);
                var sweep=(Func<NavPoint,NavPoint,double,bool>)typeof(PlayableDomain).GetMethod("AirSweepClear",Hidden).CreateDelegate(typeof(Func<NavPoint,NavPoint,double,bool>),domain);
                var last=start;
                double highest=profile.HawkClearance;
                double spacing=2*profile.HawkRadius+profile.HawkSeparation;
                for(int i=0;i<900&&actor.Outcome!=NavigationOutcome.Arrived;i++)
                {
                    advance(1d/30);
                    double height=(double)air.GetType().GetField("Height").GetValue(air);
                    highest=Math.Max(highest,height);
                    Assert.True(sweep(last,actor.Position,height),"The accepted bypass step must clear the authored ridge.");
                    double dx=actor.Position.X-last.X,dz=actor.Position.Z-last.Z;
                    double t=dx==0&&dz==0?0:Math.Max(0,Math.Min(1,((parked.X-last.X)*dx+(parked.Z-last.Z)*dz)/(dx*dx+dz*dz)));
                    Assert.GreaterOrEqual(Distance(parked,new NavPoint(last.X+dx*t,last.Z+dz*t)),spacing-1e-9);
                    last=actor.Position;
                }
                Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome);
                Assert.LessOrEqual(Distance(actor.Position,goal),profile.Navigation.ArrivalTolerance+1e-9);
                Assert.Greater(highest,profile.HawkClearance);
                var view=domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
                Assert.AreEqual(parked,view.Entities.Single(e=>e.Id==peer).Position);
            }
            finally
            {
                domain.Navigation.ReleaseCommittedHostRoot();
                domain.DestroyOwnedNavigation();
            }
        }

        [TestCase(PlayableCommandKind.Move)]
        [TestCase(PlayableCommandKind.AttackMove)]
        public void HawkCompletesMossMoveAroundTwoParkedPeers(PlayableCommandKind kind)
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new MossHighlandMap());
            var domain=new PlayableDomain(profile,1,false);
            PlayableDomain restored=null;
            try
            {
                var start=new NavPoint(8.8958871406796458,19.704196962683838);
                var parked=new[]{new NavPoint(6.43691295355718,19.433132747494788),new NavPoint(8.9906761269379931,21.982136519000903)};
                var goal=new NavPoint(6.42,22);
                var spawn=typeof(PlayableDomain).GetMethod("SpawnUnit",Hidden);
                typeof(PlayableDomain).GetField("nextId",Hidden).SetValue(domain,84);
                int mover=(int)spawn.Invoke(domain,new object[]{start,PlayableOwner.Player,PlayableEntityKind.Hawk});
                var peers=parked.Select(point=>(int)spawn.Invoke(domain,new object[]{point,PlayableOwner.Player,PlayableEntityKind.Hawk})).ToArray();
                var units=(System.Collections.IDictionary)typeof(PlayableDomain).GetField("units",Hidden).GetValue(domain);
                var air=units[mover].GetType().GetField("Air").GetValue(units[mover]);
                var actor=(NavUnit)air.GetType().GetField("Actor").GetValue(air);
                actor.Heading=2.3257500312439547;
                Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(
                    1,1,"player-1",kind,new[]{mover},goal),out _));
                Assert.AreEqual(goal,actor.Goal);
                var advance=(Action<double>)typeof(PlayableDomain).GetMethod("AdvanceHawks",Hidden).CreateDelegate(typeof(Action<double>),domain);
                var sweep=(Func<NavPoint,NavPoint,double,bool>)typeof(PlayableDomain).GetMethod("AirSweepClear",Hidden).CreateDelegate(typeof(Func<NavPoint,NavPoint,double,bool>),domain);
                var last=start;
                double spacing=2*profile.HawkRadius+profile.HawkSeparation;
                const string source="hawk-moss-traffic";
                var restoreAdvance=default(Action<double>);
                int i=0;
                for(;i<300&&actor.Outcome!=NavigationOutcome.Arrived;i++)
                {
                    advance(1d/30);
                    restoreAdvance?.Invoke(1d/30);
                    if(i==45&&kind==PlayableCommandKind.AttackMove)
                    {
                        var bytes=domain.CaptureWorldBytes(7,source);
                        restored=PlayableDomain.RestoreWorldBytes(bytes,profile,7,source);
                        CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,source));
                        restoreAdvance=(Action<double>)typeof(PlayableDomain).GetMethod("AdvanceHawks",Hidden)
                            .CreateDelegate(typeof(Action<double>),restored);
                    }
                    Assert.AreEqual(goal,actor.Goal,"A detour must preserve the assigned endpoint.");
                    double height=(double)air.GetType().GetField("Height").GetValue(air);
                    Assert.True(sweep(last,actor.Position,height));
                    foreach(var position in parked)
                    {
                        double dx=actor.Position.X-last.X,dz=actor.Position.Z-last.Z;
                        double length=dx*dx+dz*dz;
                        double t=length==0?0:Math.Max(0,Math.Min(1,((position.X-last.X)*dx+(position.Z-last.Z)*dz)/length));
                        Assert.GreaterOrEqual(Distance(position,new NavPoint(last.X+dx*t,last.Z+dz*t)),spacing-1e-9);
                    }
                    last=actor.Position;
                }
                Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome,
                    FormattableString.Invariant($"Hawk must arrive within ten seconds, last pose ({last.X:R}, {last.Z:R})."));
                TestContext.WriteLine("Arrival after "+i+" ticks.");
                if(kind==PlayableCommandKind.AttackMove)
                {
                    Assert.IsNotNull(restored,"Capture must occur during the detour.");
                    CollectionAssert.AreEqual(domain.CaptureWorldBytes(7,source),restored.CaptureWorldBytes(7,source));
                }
                Assert.LessOrEqual(Distance(actor.Position,goal),profile.Navigation.ArrivalTolerance+1e-9);
                var view=domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
                for(int peer=0;peer<peers.Length;peer++)Assert.AreEqual(parked[peer],view.Entities.Single(e=>e.Id==peers[peer]).Position);
            }
            finally
            {
                restored?.Navigation.ReleaseCommittedHostRoot();
                restored?.DestroyOwnedNavigation();
                domain.Navigation.ReleaseCommittedHostRoot();
                domain.DestroyOwnedNavigation();
            }
        }

        [TestCase(0d,0d,0d)]
        [TestCase(-4d,-4d,0d)]
        [TestCase(4d,4d,0d)]
        [TestCase(-4d,4d,0d)]
        [TestCase(4d,-4d,0d)]
        [TestCase(-4d,-4d,-4d)]
        [TestCase(-4d,-4d,4d)]
        [TestCase(0d,-7.5d,0d)]
        public void HawkFinishesMossApproachBetweenThreeParkedPeers(double vx,double vz,double yaw)
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new MossHighlandMap());
            var domain=new PlayableDomain(profile,1,false);
            try
            {
                var start=new NavPoint(46.035067014346822,17.735035307350884);
                var goal=new NavPoint(46,18);
                var parked=new[]{new NavPoint(43.420504865238669,18.005856382815342),
                    new NavPoint(46.000677766817851,20.579992691385321),new NavPoint(48.584656726180143,18.022214556857215)};
                var spawn=typeof(PlayableDomain).GetMethod("SpawnUnit",Hidden);
                typeof(PlayableDomain).GetField("nextId",Hidden).SetValue(domain,51);
                int mover=(int)spawn.Invoke(domain,new object[]{start,PlayableOwner.Player,PlayableEntityKind.Hawk});
                var peers=parked.Select(point=>(int)spawn.Invoke(domain,new object[]{point,PlayableOwner.Player,PlayableEntityKind.Hawk})).ToArray();
                var units=(System.Collections.IDictionary)typeof(PlayableDomain).GetField("units",Hidden).GetValue(domain);
                var air=units[mover].GetType().GetField("Air").GetValue(units[mover]);
                var actor=(NavUnit)air.GetType().GetField("Actor").GetValue(air);
                actor.Heading=1.94191508569596;
                air.GetType().GetField("Height").SetValue(air,5.5d);
                var command=new PlayableCommand(1,1,"player-1",PlayableCommandKind.AttackMove,new[]{mover},goal);
                Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(command,out _));
                Assert.AreEqual(goal,actor.Goal);
                air.GetType().GetField("Velocity").SetValue(air,new NavPoint(vx,vz));
                air.GetType().GetField("YawVelocity").SetValue(air,yaw);
                var advance=(Action<double>)typeof(PlayableDomain).GetMethod("AdvanceHawks",Hidden)
                    .CreateDelegate(typeof(Action<double>),domain);
                var sweep=(Func<NavPoint,NavPoint,double,bool>)typeof(PlayableDomain).GetMethod("AirSweepClear",Hidden)
                    .CreateDelegate(typeof(Func<NavPoint,NavPoint,double,bool>),domain);
                var last=start;
                double spacing=2*profile.HawkRadius+profile.HawkSeparation;
                int tick=0;
                for(;tick<300&&actor.Outcome!=NavigationOutcome.Arrived;tick++)
                {
                    advance(1d/30);
                    var height=(double)air.GetType().GetField("Height").GetValue(air);
                    Assert.True(sweep(last,actor.Position,height));
                    foreach(var position in parked)
                    {
                        double dx=actor.Position.X-last.X,dz=actor.Position.Z-last.Z;
                        double length=dx*dx+dz*dz;
                        double t=length==0?0:Math.Max(0,Math.Min(1,((position.X-last.X)*dx+(position.Z-last.Z)*dz)/length));
                        Assert.GreaterOrEqual(Distance(position,new NavPoint(last.X+dx*t,last.Z+dz*t)),spacing-1e-9);
                    }
                    last=actor.Position;
                }
                Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome,FormattableString.Invariant($"Last pose ({last.X:R}, {last.Z:R})."));
                Assert.LessOrEqual(Distance(last,goal),profile.Navigation.ArrivalTolerance+1e-9);
                TestContext.WriteLine("Arrival after "+tick+" ticks.");
                var view=domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
                for(int peer=0;peer<peers.Length;peer++)Assert.AreEqual(parked[peer],view.Entities.Single(e=>e.Id==peers[peer]).Position);
            }
            finally
            {
                domain.Navigation.ReleaseCommittedHostRoot();
                domain.DestroyOwnedNavigation();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HawkKeepsApproachToRecordedSouthwestMossSlot(bool retryFromPocket)
        {
            var map = new MossHighlandMap();
            var basis = PlayableProfile.Create(PlayableProfile.Default.CopyData(), map);
            var store = new Spacewars.Presentation.NativeBalanceStore(basis, authoring: false, startOnRelease: true);
            // The route and positions below were recorded with this historical profile.
            store.Select(Spacewars.Presentation.NativeBalanceStore.DefaultId, 13);
            Assert.AreEqual(13, store.SelectedVersion);
            var profile = PlayableProfile.Create(store.Active.CopyData(), map, store.Active.DisplayName);
            var roster = new[] { new OfflineParticipant("player-1", 1, 1, OfflineControl.Human),
                new OfflineParticipant("peer-2", 2, 2, OfflineControl.Scenario) };
            var authored = map.Configuration(profile, 7, roster);
            var starts = authored.Starts.Select(start => start.SiteId != 1 ? start :
                new OfflineStart(start.Id,start.SiteId,start.Position,start.ExplorerAnchor,start.Heading,1)).ToArray();
            var site = authored.Sites.Single(x => x.Id == 1);
            var kinds = new[]{PlayableBuildingKind.Refinery, PlayableBuildingKind.Factory, PlayableBuildingKind.Hangar, PlayableBuildingKind.Refinery};
            var buildings = Enumerable.Range(1,4).Select(id => site.Slots.Single(slot => slot.Id == id))
                .Select((slot,i) => new OfflineScenarioBuilding(1,kinds[i],site.Id,slot.Id,slot.Position,slot.Heading)).ToArray();
            var costs = new double[starts.Length, starts.Length];
            for (int i = 0; i < starts.Length; i++)
                for (int j = 0; j < starts.Length; j++)
                    costs[i,j] = authored.RouteCost(i,j);
            var setup = new OfflineMatchConfiguration(profile,"moss-hawk55-southwest-journal-fixture",
                authored.MapIdentity,authored.RouteProvenance,7,roster,starts,authored.Sites.ToArray(),
                authored.Obstacles.ToArray(),costs,terrain:map.Id,scenarioBuildings:buildings);
            var domain = new PlayableDomain(profile, 1, setup);
            try
            {
                var start = retryFromPocket ? new NavPoint(-36.95951532089081,-35.718559542231425)
                    : new NavPoint(-38.791100447668605,-34.16238774535561);
                var goal = new NavPoint(-35.45,-38);
                // The unstamped parked reinforcement also occupies airspace.
                var parked = new[] { new NavPoint(-38.129933482236794,-37.95396407079466),
                    new NavPoint(-38.73715782358936,-30.60799016408309),
                    new NavPoint(-35.072495520190586,-34.19964582438712) };
                var spawn = typeof(PlayableDomain).GetMethod("SpawnUnit", Hidden);
                typeof(PlayableDomain).GetField("nextId", Hidden).SetValue(domain, 55);
                int id = (int)spawn.Invoke(domain, new object[] { start, PlayableOwner.Player, PlayableEntityKind.Hawk });
                var peers = parked.Select(point => (int)spawn.Invoke(domain, new object[] {
                    point, PlayableOwner.Player, PlayableEntityKind.Hawk })).ToArray();
                var units = (System.Collections.IDictionary)typeof(PlayableDomain).GetField("units", Hidden).GetValue(domain);
                var unit = units[id];
                var air = unit.GetType().GetField("Air").GetValue(unit);
                var actor = (NavUnit)air.GetType().GetField("Actor").GetValue(air);
                actor.Heading = retryFromPocket ? -.9725378443998627 : -.4822609114832236;
                air.GetType().GetField("Height").SetValue(air, retryFromPocket ? 6.22d : 3d);
                Assert.AreEqual(PlayableCommandStatus.Applied, domain.Apply(
                    new PlayableCommand(1, 1, "player-1", PlayableCommandKind.Move, new[] { id }, goal), out _));
                Assert.AreEqual(goal, actor.Goal);
                var advance = (Action<double>)typeof(PlayableDomain).GetMethod("AdvanceHawks", Hidden)
                    .CreateDelegate(typeof(Action<double>), domain);
                var sweep = (Func<NavPoint,NavPoint,double,bool>)typeof(PlayableDomain).GetMethod("AirSweepClear", Hidden)
                    .CreateDelegate(typeof(Func<NavPoint,NavPoint,double,bool>), domain);
                int tick = 0;
                var last = start;
                double spacing = 2 * profile.HawkRadius + profile.HawkSeparation;
                while (tick < 450 && actor.Outcome != NavigationOutcome.Arrived)
                {
                    advance(1d/30);
                    tick++;
                    double height = (double)air.GetType().GetField("Height").GetValue(air);
                    Assert.True(sweep(last,actor.Position,height));
                    foreach (var position in parked)
                    {
                        double dx = actor.Position.X - last.X, dz = actor.Position.Z - last.Z;
                        double len = dx * dx + dz * dz;
                        double t = len == 0 ? 0 : Math.Max(0, Math.Min(1, ((position.X-last.X)*dx+(position.Z-last.Z)*dz)/len));
                        Assert.GreaterOrEqual(Distance(position,new NavPoint(last.X+dx*t,last.Z+dz*t)),spacing-1e-9);
                    }
                    last = actor.Position;
                }
                Assert.AreEqual(NavigationOutcome.Arrived, actor.Outcome);
                Assert.LessOrEqual(Distance(actor.Position, goal), profile.Navigation.ArrivalTolerance + 1e-9);
                var view = domain.Snapshot(1, RuntimeStatus.Running, false, new PlayableRuntimeMetrics(0,0,0,0,0), null, 7);
                for (int i=0;i<peers.Length;i++) Assert.AreEqual(parked[i], view.Entities.Single(e => e.Id==peers[i]).Position);
                TestContext.WriteLine("Arrival after " + tick + " ticks.");
            }
            finally
            {
                domain.Navigation.ReleaseCommittedHostRoot();
                domain.DestroyOwnedNavigation();
            }
        }

        private static double Distance(NavPoint a,NavPoint b)
        {double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        [Test] public void ShortReverseMayRepeatAfterFullStopAndMidManoeuvreOrderTurns()
        {
            using var s=new Scene();int id=(int)s.Call("SpawnUnit",new NavPoint(0,15),PlayableOwner.Player,PlayableEntityKind.Hawk);
            PlayableEntitySnapshot Air()=>s.View.Entities.Single(e=>e.Id==id);
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(PlayableCommandKind.Move,new[]{id},new NavPoint(-2,15)));s.Advance(120);var first=Air();Assert.False(first.Moving);Assert.Less(first.Position.X,0);Assert.AreEqual(0,first.HullHeading,1e-9);
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(PlayableCommandKind.Move,new[]{id},new NavPoint(first.Position.X-2,15)));s.Advance(3);double oldHeading=Air().HullHeading;
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(PlayableCommandKind.Move,new[]{id},new NavPoint(-12,15)));s.Advance(10);Assert.Greater(Math.Abs(Air().HullHeading-oldHeading),.01);
        }
        [Test] public void ShortBudgetCannotReportArrivalBeforeRequestedGoal()
        {
            using var s=new Scene();var from=new NavPoint(0,15);var goal=new NavPoint(-4,15);int id=(int)s.Call("SpawnUnit",from,PlayableOwner.Player,PlayableEntityKind.Hawk);Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(PlayableCommandKind.Move,new[]{id},goal));s.Advance(600);var air=s.View.Entities.Single(e=>e.Id==id);Assert.AreEqual(NavigationOutcome.Arrived,air.NavigationOutcome);Assert.LessOrEqual(Math.Sqrt(Math.Pow(air.Position.X-goal.X,2)+Math.Pow(air.Position.Z-goal.Z,2)),s.Profile.Navigation.ArrivalTolerance+1e-7);
        }
        [Test] public void RockCrossingClimbsWithoutEnteringItsVolumeAndDescends()
        {
            using var s=new Scene();var rock=PlayableMap.SolidObstacles(s.Profile).First();double z=(rock.MinZ+rock.MaxZ)/2;var from=new NavPoint(rock.MinX-4,z);var to=new NavPoint(rock.MaxX+4,z);int id=(int)s.Call("SpawnUnit",from,PlayableOwner.Player,PlayableEntityKind.Hawk);
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(PlayableCommandKind.Move,new[]{id},to));bool crossed=false;double maximum=0;
            for(int i=0;i<1200;i++){s.Advance();var air=s.View.Entities.Single(e=>e.Id==id);maximum=Math.Max(maximum,air.Altitude);if(rock.Contains(air.Position)){crossed=true;Assert.GreaterOrEqual(air.Altitude,s.Profile.BallisticWallHeight+s.Profile.HawkClearance-1e-7);}}
            var final=s.View.Entities.Single(e=>e.Id==id);Assert.True(crossed);Assert.AreEqual(NavigationOutcome.Arrived,final.NavigationOutcome);Assert.Greater(maximum,final.Altitude);Assert.AreEqual(s.Profile.HawkClearance,final.Altitude,1e-7);
        }
        [Test] public void FullAirAppendQueueRejectsMixedOrderBeforeGroundMutation()
        {
            using var s=new Scene();int air=(int)s.Call("SpawnUnit",new NavPoint(0,15),PlayableOwner.Player,PlayableEntityKind.Hawk);int scout=s.View.Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(PlayableCommandKind.Move,new[]{air},new NavPoint(15,15)));
            for(int i=0;i<s.Profile.UnitOrderQueueLimit;i++)Assert.AreEqual(PlayableCommandStatus.Applied,s.Domain.Apply(new PlayableCommand(1,++s.Sequence,"player-1",PlayableCommandKind.Move,new[]{air},new NavPoint(10,15),mode:PlayableOrderMode.Append),out _));
            var before=s.View.Entities.Single(e=>e.Id==scout);
            Assert.AreEqual(PlayableCommandStatus.Overflow,s.Domain.Apply(new PlayableCommand(1,++s.Sequence,"player-1",PlayableCommandKind.Move,new[]{air,scout},new NavPoint(10,15),mode:PlayableOrderMode.Append),out _));
            var after=s.View.Entities.Single(e=>e.Id==scout);Assert.AreEqual(before.Position,after.Position);Assert.AreEqual(before.CurrentOrder,after.CurrentOrder);Assert.False(s.Domain.Navigation.IsPending(scout));
        }
        [Test] public void AppendedAirGroupRetainsDistinctSecondDestinations()
        {
            using var s=new Scene();int a=(int)s.Call("SpawnUnit",new NavPoint(0,15),PlayableOwner.Player,PlayableEntityKind.Hawk),b=(int)s.Call("SpawnUnit",new NavPoint(0,18),PlayableOwner.Player,PlayableEntityKind.Hawk);
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(PlayableCommandKind.Move,new[]{a,b},new NavPoint(15,15)));
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Domain.Apply(new PlayableCommand(1,++s.Sequence,"player-1",PlayableCommandKind.Move,new[]{a,b},new NavPoint(-10,15),mode:PlayableOrderMode.Append),out _));
            s.Advance(1800);var air=s.View.Entities.Where(e=>e.Kind==PlayableEntityKind.Hawk).ToArray();Assert.True(air.All(e=>e.NavigationOutcome==NavigationOutcome.Arrived));Assert.True(air.All(e=>e.Position.X<0));Assert.GreaterOrEqual(Math.Sqrt(Math.Pow(air[0].Position.X-air[1].Position.X,2)+Math.Pow(air[0].Position.Z-air[1].Position.Z,2)),2*s.Profile.HawkRadius+s.Profile.HawkSeparation-1e-9);
        }
        [Test] public void BotAircraftRemainsExplicitlyExcluded()
        {
            var descriptor=Spacewars.Simulation.Ai.AiRosterCatalog.Initial.For(PlayableEntityKind.Hawk);Assert.IsNotEmpty(descriptor.exclusionReason);Assert.Zero(descriptor.reconWeight+descriptor.lineWeight+descriptor.supportWeight+descriptor.scoutingUtility);Assert.IsNull(Spacewars.Simulation.Ai.AiRosterCatalog.Initial.Production(PlayableCommandKind.QueueHawk));
        }
    }
}
