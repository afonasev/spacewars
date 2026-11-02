using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class OrbitalMineTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        PlayableDomain domain;PlayableProfile profile;long sequence;
        object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(domain,args);
        PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        int Spawn(NavPoint p,PlayableOwner owner=PlayableOwner.Player,PlayableEntityKind kind=PlayableEntityKind.Tank)=>(int)Call("SpawnUnit",p,owner,kind);
        PlayableCommandStatus Send(PlayableCommandKind kind,NavPoint point=default,int target=2)=>domain.Apply(new PlayableCommand(1,++sequence,"player-1",kind,null,point,targetId:target),out _);
        void Ready(){Call("AdvanceOrbital",30d);}
        [SetUp] public void Setup(){profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());domain=new PlayableDomain(profile,1,false);Call("CreditOrbital",(PlayableOwner?)PlayableOwner.Player,PlayableOwner.Enemy,250d);Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.ChooseOrbital));}
        [TearDown] public void Cleanup(){domain?.DestroyOwnedNavigation();}
        [Test] public void InvalidPointSpendsNothingAndFourthRetiresOldestWithoutExplosion()
        {
            Spawn(new NavPoint(-10,0));Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.UseOrbital,new NavPoint(80,80)));Assert.Zero(View().Orbital.Abilities.Single().Remaining);
            for(int i=0;i<4;i++){Ready();Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,new NavPoint(-10,i*2)));}
            var view=View();Assert.AreEqual(3,view.Mines.Count);CollectionAssert.AreEqual(new[]{2,3,4},view.Mines.Select(m=>m.Id));Assert.Zero(view.MineBlasts.Count);Assert.AreEqual(30,view.Orbital.Abilities.Single().Remaining);
            Ready();Call("AdvanceOrbital",3600d);Assert.AreEqual(3,View().Mines.Count,"no lifetime expiry");
            Assert.AreEqual(view.Entities.Count,PlayableMapView.Marks(view).Count(mark=>mark.State==MapMarkState.Unit),"only units enter minimap marks");
        }
        [Test] public void VisionExtendsOwnerOnlyAndEnemyDoesNotReceiveHiddenMines()
        {
            var p=new NavPoint(-10,0);int scout=Spawn(p);Call("RefreshVision");var minePoint=new NavPoint(-20,0);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,minePoint));
            Call("DamageWithOwner",scout,10000d,0,(PlayableOwner?)null);Call("RefreshVision");
            Assert.True(View().Vision.IsVisible(new NavPoint(-24.9,0)));Assert.False(View().Vision.IsVisible(new NavPoint(-25.1,0)));Assert.AreEqual(1,View().Mines.Count);Assert.Zero(View(PlayableOwner.Enemy).Mines.Count);
            Spawn(new NavPoint(-20,3),PlayableOwner.Enemy);Call("RefreshVision");Assert.AreEqual(1,View(PlayableOwner.Enemy).Mines.Count);
        }
        [TestCase(PlayableEntityKind.Tank,20)][TestCase(PlayableEntityKind.Explorer,0)][TestCase(PlayableEntityKind.Shkval,0)]
        public void ContactDealsOnlyEightyToPrimaryAndFiftyToFriendlySplash(PlayableEntityKind kind,int remaining)
        {
            int friendly=Spawn(new NavPoint(-10,0));int enemy=Spawn(new NavPoint(-8,0),PlayableOwner.Enemy,kind);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,new NavPoint(-8,0)));Call("AdvanceMines");Call("RefreshVision");var view=View();
            Assert.Zero(view.Mines.Count);Assert.AreEqual(1,view.MineBlasts.Count);Assert.AreEqual(1.4,view.MineBlasts[0].Radius);
            Assert.AreEqual(remaining,view.Entities.SingleOrDefault(e=>e.Id==enemy)?.Health??0);Assert.AreEqual(50,view.Entities.Single(e=>e.Id==friendly).Health);
        }
        [Test] public void FriendlyContactDoesNotTriggerAndAreaDamageChainsEachMineOnce()
        {
            int friendly=Spawn(new NavPoint(-10,0));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,new NavPoint(-10,0)));Call("AdvanceMines");Assert.AreEqual(1,View().Mines.Count);
            Ready();Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,new NavPoint(-9,0)));
            Call("MarkMinesForAreaDamage",new BallisticPoint(-10,.1,0),.1d);Call("AdvanceMines");Call("RefreshVision");
            Assert.Zero(View().Mines.Count);Assert.AreEqual(2,domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7).MineBlasts.Count);Assert.False(View().Entities.Any(e=>e.Id==friendly));
            Call("AdvanceMines");Assert.AreEqual(2,domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7).MineBlasts.Count,"no repeat damage/effects");
        }
        [Test] public void CurrentSavePreservesOrderVisionAndEveryContinuationTick()
        {
            Spawn(new NavPoint(-10,0));Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,new NavPoint(-10,0)));
            var bytes=domain.CaptureWorldBytes(7,"mine-current");var restored=PlayableDomain.RestoreWorldBytes(bytes,profile,7,"mine-current");
            try{CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"mine-current"));for(int i=0;i<3;i++){domain.Step(1d/30);restored.Step(1d/30);CollectionAssert.AreEqual(domain.CaptureWorldBytes(7,"mine-current"),restored.CaptureWorldBytes(7,"mine-current"),"continuation "+i);}}
            finally{restored.DestroyOwnedNavigation();}
        }
        [Test] public void RefusedChainPreservesHealthEffectsAndMineStateThenRetries()
        {
            Spawn(new NavPoint(-10,0));Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,new NavPoint(-10,0)));Ready();Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,new NavPoint(-9,0)));
            Call("MarkMinesForAreaDamage",new BallisticPoint(-10,.1,0),.1d);var before=domain.CaptureWorldBytes(7,"mine-refusal");var budget=domain.Navigation.PhysicalHostBudget;int denied=0;
            try{budget.AllocationAdmission=bytes=>{denied++;return false;};Call("AdvanceMines");Assert.Greater(denied,0);}
            finally{budget.AllocationAdmission=null;}
            CollectionAssert.AreEqual(before,domain.CaptureWorldBytes(7,"mine-refusal"));
            Call("AdvanceMines");Assert.Zero(View().Mines.Count);Assert.AreEqual(2,domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7).MineBlasts.Count);
            var bytes=domain.CaptureWorldBytes(7,"mine-refusal");var restored=PlayableDomain.RestoreWorldBytes(bytes,profile,7,"mine-refusal");try{CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"mine-refusal"));}finally{restored.DestroyOwnedNavigation();}
        }
        [Test] public void FractionalCapacityRejectedAndMineRadiusIsIndependent()
        {
            var data=profile.CopyData();data.orbitalMineCapacity=2.5;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));data.orbitalMineCapacity=3;data.shkvalBlastRadius=.7;
            var edited=PlayableProfile.Create(data);Assert.AreEqual(1.4,edited.CopyData().orbitalMineBlastRadius);Assert.AreEqual(.7,edited.ShkvalBlastRadius);
        }
    }
}
