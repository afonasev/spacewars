using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class OrbitalControlHackTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        readonly Type type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        object domain;PlayableProfile profile;OfflineMatchConfiguration config;long sequence;
        object Call(string name,params object[] args)=>type.GetMethod(name,F).Invoke(domain,args);
        IDictionary Rows(string name)=>(IDictionary)type.GetField(name,F).GetValue(domain);
        static void Set(object value,string name,object field)=>value.GetType().GetField(name).SetValue(value,field);
        static double Number(object value,string name)=>(double)value.GetType().GetField(name).GetValue(value);
        PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        PlayableCommandStatus Send(PlayableCommandKind kind,int id=0,string owner="player-1")=>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,owner,kind,id==0?null:new[]{id},targetId:OrbitalAbilities.ControlHack),null);
        int Spawn(PlayableOwner owner,NavPoint point,PlayableEntityKind kind=PlayableEntityKind.Tank)=>(int)Call("SpawnUnit",point,owner,kind);
        NavPoint NearHome { get {var p=View().Entities.First(e=>e.Owner==PlayableOwner.Player).Position;return new NavPoint(p.X+4,p.Z); } }
        void Hurt(int id,double fraction=.4){var u=Rows("units")[id];Set(u,"Health",(double)Call("UnitMaximumHealth",u.GetType().GetField("Owner").GetValue(u),u.GetType().GetField("Kind").GetValue(u))*fraction);}
        void Research(PlayableOwner owner,PlayableResearchKind kind)
        {
            var orders=(IList)Call("Research",owner);var t=type.GetNestedType("ResearchOrder",BindingFlags.NonPublic);var order=Activator.CreateInstance(t);
            long id=(long)type.GetField("nextResearchSequence",F).GetValue(domain);type.GetField("nextResearchSequence",F).SetValue(domain,id+1);
            Set(order,"Id",id);Set(order,"Kind",kind);Set(order,"CenterId",View(owner).Buildings.First(b=>b.Owner==owner).Id);Set(order,"Complete",true);Set(order,"Duration",1d);Set(order,"Elapsed",1d);orders.Add(order);
        }
        [SetUp] public void Setup()
        {
            var map=new AiTestMap();var data=PlayableProfile.Default.CopyData();data.armyCapacity=PlayableUnitRules.Population(PlayableProfile.Default,PlayableEntityKind.Explorer);profile=PlayableProfile.Create(data,map);
            config=map.Configuration(profile,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human),new OfflineParticipant("player-3",3,1,OfflineControl.Human)});
            domain=Activator.CreateInstance(type,F,null,new object[]{profile,1L,config},null);
            Call("CreditOrbital",(PlayableOwner?)PlayableOwner.Player,PlayableOwner.Enemy,250d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.ChooseOrbital));
        }
        [TearDown] public void Cleanup()=>Call("DestroyOwnedNavigation");
        [Test] public void ThirdBasicChoicePreservesOtherIdsAndRestoresCooldown()
        {
            var state=new OrbitalEscalationState();var data=profile.CopyData();state.Damage(250,data);CollectionAssert.AreEqual(new[]{1,2,8},state.Options());Assert.True(state.Choose(8));Assert.True(state.Use(8,data));
            var restored=OrbitalEscalationState.Restore(state.Level,state.Progress,state.CommonRemaining,new[]{8,0,0,0,0},new[]{state.Recovery(0),0,0,0,0},data);
            Assert.AreEqual(1,OrbitalAbilities.Tier(8));Assert.AreEqual(2,OrbitalAbilities.Tier(3));Assert.AreEqual(3,OrbitalAbilities.Tier(7));Assert.AreEqual(data.orbitalBasicSeconds,restored.Snapshot(data).Abilities[0].Remaining);
        }
        [TestCase(.5)] [TestCase(.75)] public void ThresholdRejectionSpendsNothing(double fraction)
        {
            int id=Spawn(PlayableOwner.Enemy,NearHome);Hurt(id,fraction);
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.UseOrbital,id));Assert.Zero(View().Orbital.CommonRemaining);Assert.Zero(View().Orbital.Abilities[0].Remaining);
        }
        [Test] public void InvisibleAlliedAndOtherBuildingTargetsRejectWithoutCooldown()
        {
            var remote=View(PlayableOwner.Enemy).Entities.First(e=>e.Owner==PlayableOwner.Enemy).Id;Hurt(remote);
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.UseOrbital,remote));
            int ally=Spawn(PlayableOwner.Third,NearHome);Hurt(ally);Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.UseOrbital,ally));
            var hq=View().Buildings.First(b=>b.Owner==PlayableOwner.Player);Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.UseOrbital,hq.Id));Assert.Zero(View().Orbital.Abilities[0].Remaining);
        }
        [TestCase(PlayableEntityKind.Tank)] [TestCase(PlayableEntityKind.Explorer)] [TestCase(PlayableEntityKind.Shkval)] [TestCase(PlayableEntityKind.Hawk)]
        public void OwnershipRecalculatesHealthAndClearsOrdersIncludingAir(PlayableEntityKind kind)
        {
            Research(PlayableOwner.Player,PlayableResearchKind.ImprovedDefensiveSystems);Research(PlayableOwner.Player,PlayableResearchKind.ImprovedWeaponSystems);Research(PlayableOwner.Player,PlayableResearchKind.TankChassis);
            int id=Spawn(PlayableOwner.Enemy,NearHome,kind);Hurt(id);var old=Rows("units")[id];
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,id));var u=View().Entities.Single(e=>e.Id==id);
            Assert.AreEqual(PlayableOwner.Player,u.Owner);Assert.AreEqual(u.MaxHealth*.4,u.ExactHealth.Value,1e-9);Assert.Greater(u.DefensiveSystemsLevel,0);Assert.Greater(u.WeaponSystemsLevel,0);Assert.Null(u.CurrentOrder);Assert.AreEqual(PlayableOwner.Player,u.OrderStamp.Owner);Assert.AreEqual(PlayableCommandKind.Stop,u.OrderStamp.Kind);Assert.False(u.Held);Assert.False(u.Moving);
            Assert.Greater(View().Population.Living,View().Population.Capacity,"capture permits overflow");Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.Stop,id,"player-2"));
            var former=View(PlayableOwner.Enemy);Assert.AreEqual(MatchEventKind.UnitCaptured,former.MatchEvents.Last().Kind);Assert.False(View().MatchEvents.Any(e=>e.Kind==MatchEventKind.UnitCaptured));
            var n=new NativeMatchNotifications();n.Update(former,false,0,profile);Assert.AreEqual("Юнит захвачен",n.Latest.Text);
            var bytes=(byte[])Call("CaptureWorldBytes",7,config.SourceIdentity);var restored=OfflineParticipantAuthority.Restore(bytes,config);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());Assert.IsEmpty(restored.View("player-2").MatchEvents,"restoring does not replay transient notifications");
        }
        [Test] public void SharedAlliedScanProvidesVisibilityAndStaleHealthIsRechecked()
        {
            var enemy=View(PlayableOwner.Enemy).Entities.First(e=>e.Owner==PlayableOwner.Enemy);Hurt(enemy.Id);
            Call("CreditOrbital",(PlayableOwner?)PlayableOwner.Third,PlayableOwner.Enemy,250d);Call("ChooseOrbital",PlayableOwner.Third,1);Call("UseOrbital",PlayableOwner.Third,1,enemy.Position);
            Assert.True(View().Vision.IsVisible(enemy.Position));Hurt(enemy.Id,.5);Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.UseOrbital,enemy.Id));Hurt(enemy.Id,.4);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,enemy.Id));
        }
        [Test] public void MineTransfersIncomeSiteBindingConcreteHealthAndNotification()
        {
            var site=Rows("sites").Values.Cast<object>().First(s=>((TerritorySite)s.GetType().GetField("Site").GetValue(s)).Kind==PlayableBuildingKind.Mine);var descriptor=(TerritorySite)site.GetType().GetField("Site").GetValue(site);
            Set(site,"Claimant",(PlayableOwner?)PlayableOwner.Enemy);Set(site,"Progress",1d);Call("AddCredits",PlayableOwner.Enemy,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",descriptor.Id,0,PlayableBuildingKind.Mine,0,PlayableOwner.Enemy,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            var mine=Rows("buildings").Values.Cast<object>().Single(b=>(int)b.GetType().GetField("SiteId").GetValue(b)==descriptor.Id);int id=(int)mine.GetType().GetField("Id").GetValue(mine);Set(mine,"Health",(double)Call("BuildingMaximumHealth",PlayableOwner.Enemy,PlayableBuildingKind.Mine,null)*.4);
            Research(PlayableOwner.Player,PlayableResearchKind.ReinforcedConcrete);Spawn(PlayableOwner.Player,new NavPoint(descriptor.Position.X+4,descriptor.Position.Z),PlayableEntityKind.Explorer);
            double ownBefore=View().IncomePerSecond,enemyBefore=View(PlayableOwner.Enemy).IncomePerSecond;
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,id));var captured=View().Buildings.Single(b=>b.Id==id);
            Assert.AreEqual(captured.MaxHealth*.4,captured.ExactHealth.Value,1e-9);Assert.AreEqual(PlayableOwner.Player,captured.Owner);Assert.Greater(View().IncomePerSecond,ownBefore);Assert.Less(View(PlayableOwner.Enemy).IncomePerSecond,enemyBefore);
            Assert.AreEqual(MatchEventKind.MineCaptured,View(PlayableOwner.Enemy).MatchEvents.Last().Kind);var bytes=(byte[])Call("CaptureWorldBytes",7,config.SourceIdentity);CollectionAssert.AreEqual(bytes,OfflineParticipantAuthority.Restore(bytes,config).CaptureBytes());
        }
        [Test] public void CapturedOverflowDoesNotStartWaitingProduction()
        {
            Call("AddCredits",PlayableOwner.Player,10000d);
            var home=View().Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",home.SiteId,5,PlayableBuildingKind.Factory,home.Id,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            int factory=View().Buildings.Single(b=>b.SiteId==home.SiteId&&b.Kind==PlayableBuildingKind.Factory).Id;
            int id=Spawn(PlayableOwner.Enemy,NearHome);Hurt(id);Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,id));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueTank,factory));
            Call("AdvanceProduction",100d);var waiting=View().Buildings.Single(b=>b.Id==factory).PrivateState.Orders.Single();Assert.False(waiting.Active);Assert.Zero(View().Population.Reserved);
        }
        [Test] public void CaptureCancelsPendingAndAppendedEnemyMovement()
        {
            int id=Spawn(PlayableOwner.Enemy,NearHome);Hurt(id);var goal=new NavPoint(NearHome.X+3,NearHome.Z+3);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++sequence,"player-2",PlayableCommandKind.Move,new[]{id},target:goal),null));
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++sequence,"player-2",PlayableCommandKind.Move,new[]{id},target:new NavPoint(goal.X,goal.Z+3),mode:PlayableOrderMode.Append),null));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,id));
            var captured=View().Entities.Single(e=>e.Id==id);Assert.Null(captured.CurrentOrder);Assert.False(captured.Moving);Assert.False(Rows("tacticalQueues").Contains(id));
            var bytes=(byte[])Call("CaptureWorldBytes",7,config.SourceIdentity);CollectionAssert.AreEqual(bytes,OfflineParticipantAuthority.Restore(bytes,config).CaptureBytes());
        }
        [Test] public void RelayLossNotifiesAtNeutralizationOnlyAndUsesOrdinaryCameraHistory()
        {
            Cleanup();var points=new[]{new NavPoint(-16,-14),new NavPoint(16,14),new NavPoint(-16,14)};
            var sites=points.Select((v,i)=>new TerritorySite(i+1,PlayableBuildingKind.Headquarters,v,Array.Empty<TerritorySlot>())).Concat(new[]{new TerritorySite(4,PlayableBuildingKind.RelayTower,default,Array.Empty<TerritorySlot>())}).ToArray();
            var starts=points.Select((v,i)=>new OfflineStart("start-"+i,i+1,v,new NavPoint(v.X+5,v.Z),pin:i+1)).ToArray();
            var costs=new double[3,3];for(int i=0;i<3;i++)for(int j=0;j<3;j++)costs[i,j]=i==j?0:40;
            profile=PlayableProfile.Default;config=new OfflineMatchConfiguration(profile,"relay-hack-loss-v1","relay-flat","authored test geometry",7,config.Roster.ToArray(),starts,sites,Array.Empty<NavObstacle>(),costs);
            domain=Activator.CreateInstance(type,F,null,new object[]{profile,1L,config},null);
            int own=Spawn(PlayableOwner.Player,new NavPoint(5,0),PlayableEntityKind.Explorer);
            for(int i=0;i<180;i++)Call("AdvanceCapture",1d/30);
            Assert.AreEqual(PlayableOwner.Player,View().Sites.Single(x=>x.Site.Id==4).Owner);
            Call("Damage",own,100000);Spawn(PlayableOwner.Enemy,new NavPoint(-5,0),PlayableEntityKind.Explorer);
            for(int i=0;i<180;i++)Call("AdvanceCapture",1d/30);
            Assert.Null(View().Sites.Single(x=>x.Site.Id==4).Owner);Assert.AreEqual(1,View().MatchEvents.Count(e=>e.Kind==MatchEventKind.RelayControlLost));
            var n=new NativeMatchNotifications();n.Update(View(),false,0,profile);Assert.AreEqual("Вышка потеряна",n.Latest.Text);Assert.AreEqual(default(NavPoint),n.Focus(false,0,profile).Value);
            for(int i=0;i<180;i++)Call("AdvanceCapture",1d/30);
            Assert.AreEqual(PlayableOwner.Enemy,View().Sites.Single(x=>x.Site.Id==4).Owner);Assert.AreEqual(1,View().MatchEvents.Count(e=>e.Kind==MatchEventKind.RelayControlLost));
        }
        [Test] public void AlreadyPaidProductionPausesDuringCapturedOverflowAndResumesWithoutLosingWork()
        {
            Cleanup();var map=new AiTestMap();var data=PlayableProfile.Default.CopyData();data.armyCapacity=PlayableUnitRules.Population(PlayableProfile.Default,PlayableEntityKind.Explorer)+PlayableUnitRules.Population(PlayableProfile.Default,PlayableEntityKind.Tank);
            profile=PlayableProfile.Create(data,map);config=map.Configuration(profile,7,config.Roster.ToArray());domain=Activator.CreateInstance(type,F,null,new object[]{profile,1L,config},null);sequence=0;
            Call("CreditOrbital",(PlayableOwner?)PlayableOwner.Player,PlayableOwner.Enemy,250d);Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.ChooseOrbital));Call("AddCredits",PlayableOwner.Player,10000d);
            var home=View().Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",home.SiteId,5,PlayableBuildingKind.Factory,home.Id,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            int factory=View().Buildings.Single(b=>b.SiteId==home.SiteId&&b.Kind==PlayableBuildingKind.Factory).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueTank,factory));Call("AdvanceProduction",.5d);
            var paid=View().Buildings.Single(b=>b.Id==factory).PrivateState.Orders.Single();Assert.True(paid.Active);double remaining=paid.Remaining;
            int id=Spawn(PlayableOwner.Enemy,NearHome);Hurt(id);Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,id));Call("AdvanceProduction",100d);
            var blocked=View().Buildings.Single(b=>b.Id==factory).PrivateState.Orders.Single();Assert.AreEqual(paid.Id,blocked.Id);Assert.AreEqual(remaining,blocked.Remaining);Assert.AreEqual("Превышен лимит армии",blocked.BlockedReason);
            Call("Damage",id,100000);Call("AdvanceProduction",.5d);var resumed=View().Buildings.Single(b=>b.Id==factory).PrivateState.Orders.Single();Assert.AreEqual(remaining-.5,resumed.Remaining);Assert.Null(resumed.BlockedReason);
        }
    }
}
