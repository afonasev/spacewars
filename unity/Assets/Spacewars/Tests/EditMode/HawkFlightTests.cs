using System;
using System.Linq;
using System.Reflection;
using Spacewars.Runtime;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class HawkFlightTests
    {
        [Test] public void MatchProfileRegistersAirAndKeepsGroundProductionTyped()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;
            Assert.Greater(p.HawkSpeed,p.ExplorerSpeed);Assert.Less(p.HawkSpeed,p.ExplorerSpeed*1.2);
            Assert.Greater(p.HawkPreparationSeconds,p.TankPreparationSeconds);Assert.Less(p.HawkHealth,p.TankHealth);Assert.Greater(p.HawkCreditCost,p.TankCreditCost);
            Assert.AreEqual(p.HawkHealth,p.HawkDamage*6);
            Assert.AreEqual(p.ExplorerVision,PlayableUnitRules.Vision(p,PlayableEntityKind.Hawk));
            Assert.AreEqual(PlayableCommandKind.QueueHawk,PlayableUnitRules.ProductionCommand(PlayableEntityKind.Hawk));
            Assert.True(PlayableProfileMetadata.Fields.Any(x=>x.Path=="hawk.clearance"));
        }
        [Test] public void TallBuildingProfileControlsLookaheadAndActualSafeClearance()
        {
            var data=PlayableProfile.Default.CopyData();data.headquartersModelHeightMeters=20;var p=PlayableProfile.Create(data);var d=new PlayableDomain(p,1,false);const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,flags).Invoke(d,args);
            try
            {
                var hq=p.Headquarters(PlayableOwner.Player);int id=(int)Call("SpawnUnit",new NavPoint(hq.X-6,hq.Z),PlayableOwner.Player,PlayableEntityKind.Hawk);var goal=new NavPoint(hq.X+6,hq.Z);Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{id},goal),out _));bool crossed=false;
                for(int i=0;i<900;i++){Call("AdvanceHawks",1d/30);var air=d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7).Entities.Single(e=>e.Id==id);if(Math.Sqrt(Math.Pow(air.Position.X-hq.X,2)+Math.Pow(air.Position.Z-hq.Z,2))<=p.HeadquartersFootprintRadius+p.HawkRadius){crossed=true;Assert.GreaterOrEqual(air.Altitude,p.HeadquartersModelHeightMeters*p.HeadquartersModelScale+p.HawkClearance-1e-7);}}
                Assert.True(crossed);var final=d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7).Entities.Single(e=>e.Id==id);Assert.AreEqual(NavigationOutcome.Arrived,final.NavigationOutcome);
            }
            finally{d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();}
        }
        [Test] public void HangarCreatesIndependentAirActorAndRallyArrives()
        {
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var p=PlayableProfile.Default;var d=Activator.CreateInstance(type,flags,null,new object[]{p,1L},null);
            object Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(d,args);
            PlayableSnapshot View()=>(PlayableSnapshot)Call("Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            Call("AddCredits",PlayableOwner.Player,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Hangar,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",(double)p.HangarBuildSeconds+1);
            var b=View().Buildings.Single(x=>x.Kind==PlayableBuildingKind.Hangar);var goal=new NavPoint(b.Position.X+10,b.Position.Z);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.SetRally,new[]{b.Id},goal),null));
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,2,"player-1",PlayableCommandKind.QueueHawk,new[]{b.Id}),null));
            for(int i=0;i<1800;i++){Call("AdvanceProduction",1d/30);Call("AdvanceHawks",1d/30);}
            var hawk=View().Entities.Single(x=>x.Kind==PlayableEntityKind.Hawk);
            Assert.AreEqual(NavigationOutcome.Arrived,hawk.NavigationOutcome);Assert.Less(Math.Sqrt(Math.Pow(hawk.Position.X-goal.X,2)+Math.Pow(hawk.Position.Z-goal.Z,2)),p.Navigation.ArrivalTolerance+.01);
            var nav=(NavigationSession)type.GetProperty("Navigation",flags).GetValue(d);Assert.False(nav.Crowd.Units.Any(x=>x.Id==hawk.Id));
            Assert.Greater(hawk.Altitude,0);Assert.IsEmpty(View().Buildings.Single(x=>x.Id==b.Id).PrivateState.Orders);
            nav.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");
        }
    }
}
