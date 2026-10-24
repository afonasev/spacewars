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
            internal Scene()
            {
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
        [Test] public void AirGroupHasDistinctArrivalsAndNeverOverlaps()
        {
            using var s=new Scene();int a=(int)s.Call("SpawnUnit",new NavPoint(0,15),PlayableOwner.Player,PlayableEntityKind.Hawk),b=(int)s.Call("SpawnUnit",new NavPoint(0,18),PlayableOwner.Player,PlayableEntityKind.Hawk);
            Assert.AreEqual(PlayableCommandStatus.Applied,s.Send(PlayableCommandKind.Move,new[]{a,b},new NavPoint(15,15)));
            for(int i=0;i<900;i++){s.Advance();var air=s.View.Entities.Where(e=>e.Kind==PlayableEntityKind.Hawk).ToArray();double distance=Math.Sqrt(Math.Pow(air[0].Position.X-air[1].Position.X,2)+Math.Pow(air[0].Position.Z-air[1].Position.Z,2));Assert.GreaterOrEqual(distance,2*s.Profile.HawkRadius+s.Profile.HawkSeparation-1e-9);}
            Assert.True(s.View.Entities.Where(e=>e.Kind==PlayableEntityKind.Hawk).All(e=>e.NavigationOutcome==NavigationOutcome.Arrived));
        }
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
