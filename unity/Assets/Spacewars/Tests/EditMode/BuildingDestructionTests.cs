using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class BuildingDestructionTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private object domain;private PlayableProfile profile;
        private object Call(string name,params object[] args)=>D.GetMethod(name,F).Invoke(domain,args);
        private PlayableSnapshot View()=>(PlayableSnapshot)Call("Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        private PlayableSnapshot Player(PlayableOwner owner)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        private void Tick(long value)=>D.GetProperty("Tick",F).SetValue(domain,value);
        private PlayableCommandStatus Build(int slot=1)=>(PlayableCommandStatus)Call("BuildAt",1,slot,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null);
        private int Factory(bool complete=true)
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Build());Call("AdvanceFoundations");
            if(complete)Call("AdvanceBuildings",(double)profile.FactoryBuildSeconds);
            return View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;
        }
        [SetUp] public void Setup()
        {
            profile=PlayableProfile.Default;domain=Activator.CreateInstance(D,F,null,new object[]{profile,1L},null);Call("AddCredits",PlayableOwner.Player,10000d);
        }
        [Test] public void FoundationStartsAtQuarterHealthAndCompletesAtOriginalDeadlineWithDamagePreserved()
        {
            int id=Factory(false);var first=Player(PlayableOwner.Player).Buildings.Single(b=>b.Id==id);
            Assert.AreEqual(0,first.Progress);Assert.AreEqual(profile.FactoryHealth*.25,first.ExactHealth.Value,1e-9);
            Call("Damage",id,1);double damage=profile.ConstructionDamageMultiplier;
            Call("AdvanceBuildings",profile.FactoryBuildSeconds/2d);
            var half=Player(PlayableOwner.Player).Buildings.Single(b=>b.Id==id);
            Assert.AreEqual(.5,half.Progress,1e-9);Assert.AreEqual(profile.FactoryHealth*.625-damage,half.ExactHealth.Value,1e-9);
            Call("AdvanceBuildings",profile.FactoryBuildSeconds/2d-1d/30d);
            Assert.AreEqual(ConstructionPhase.Constructing,Player(PlayableOwner.Player).Buildings.Single(b=>b.Id==id).Phase);
            Call("AdvanceBuildings",1d/30d);
            var final=Player(PlayableOwner.Player).Buildings.Single(b=>b.Id==id);Assert.AreEqual(ConstructionPhase.Ready,final.Phase);Assert.AreEqual(profile.FactoryHealth-damage,final.ExactHealth.Value,1e-8);
        }
        [Test] public void NewlyMaterializedFactorySurvivesAnOrdinaryTankHit()
        {
            int id=Factory(false);Call("Damage",id,profile.TankWeaponDamage);
            var building=Player(PlayableOwner.Player).Buildings.Single(b=>b.Id==id);
            Assert.Greater(building.Health,0);Assert.IsEmpty(View().BuildingDestructions);
            Assert.AreEqual(profile.FactoryHealth*.25-profile.TankWeaponDamage*profile.ConstructionDamageMultiplier,building.ExactHealth.Value,1e-9);
        }
        [Test] public void DestroyedSlotRejectsSpendingUntilExactAnimationEnd()
        {
            int id=Factory();Call("Damage",id,100000);var collapse=View().BuildingDestructions.Single();
            Assert.IsFalse(View().Buildings.Any(b=>b.Id==id));double credits=(double)Call("Balance",PlayableOwner.Player);
            Assert.AreEqual(PlayableCommandStatus.OccupiedPad,Build());Assert.AreEqual(credits,(double)Call("Balance",PlayableOwner.Player));
            Tick(collapse.EndTick-1);Assert.AreEqual(PlayableCommandStatus.OccupiedPad,Build());
            Tick(collapse.EndTick);Assert.IsEmpty(View().BuildingDestructions);Assert.AreEqual(PlayableCommandStatus.Applied,Build());
        }
        [Test] public void CollapseReservationSurvivesCurrentFormatRoundtrip()
        {
            int id=Factory();Call("Damage",id,100000);var before=View().BuildingDestructions.Single();for(int frame=0;frame<13;frame++)Call("Step",1d/30d);
            byte[] bytes=(byte[])Call("CaptureWorldBytes",7,"building-collapse-a-v1");
            domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,profile,7,"building-collapse-a-v1"});
            var after=View().BuildingDestructions.Single();Assert.AreEqual(before.Id,after.Id);Assert.AreEqual(before.StartTick,after.StartTick);Assert.AreEqual(before.EndTick,after.EndTick);
            Assert.AreEqual(PlayableCommandStatus.OccupiedPad,Build());Tick(after.EndTick);Assert.AreEqual(PlayableCommandStatus.Applied,Build());
        }
        [Test] public void CascadedBuildingsCollapseButPendingCancellationDoesNot()
        {
            Factory();Call("Damage",1,100000);Assert.AreEqual(2,View().BuildingDestructions.Count);
            Assert.AreEqual(2,View().Sounds.Count(s=>s.Kind==PlayableSoundKind.Destroyed&&s.Building));
        }
        [Test] public void CancellationLeavesSlotImmediatelyReusableWithoutCollapse()
        {
            int id=Factory(false);Call("CancelBuilding",id,PlayableOwner.Player,null);
            Assert.IsEmpty(View().BuildingDestructions);Assert.AreEqual(PlayableCommandStatus.Applied,Build());
        }
        [Test] public void HiddenDestructionNeverAppearsInOpponentProjection()
        {
            int id=Factory();Call("Damage",id,100000);Assert.IsNotEmpty(Player(PlayableOwner.Player).BuildingDestructions);
            Assert.IsEmpty(Player(PlayableOwner.Enemy).BuildingDestructions);
        }
        [Test] public void CollapseClipUsesExistingSpatialDeduplicationAndExplosionLimit()
        {
            var plan=new GameplayAudioPlan(new GameplayAudioProfile());var initial=Player(PlayableOwner.Player);
            var focus=initial.Buildings.Single(b=>b.Id==1).Position;
            plan.Observe(new[]{new GameplayAudioScreen(initial,focus,new NavPoint(1,0))},0,true);
            int id=Factory();Call("Damage",id,100000);var view=Player(PlayableOwner.Player);var screen=new GameplayAudioScreen(view,focus,new NavPoint(1,0));
            var cues=plan.Observe(new[]{screen,screen},.1,true).Where(c=>c.Kind==PlayableSoundKind.Destroyed).ToArray();
            Assert.AreEqual(1,cues.Length);Assert.AreEqual("building-collapse",cues[0].Clip);
            Assert.IsEmpty(plan.Observe(new[]{screen,screen},.2,true).Where(c=>c.Kind==PlayableSoundKind.Destroyed));
        }
        [Test] public void CollapseTunablesHaveFiniteRangeValidatedMetadata()
        {
            foreach(var f in PlayableProfileMetadata.Fields.Where(f=>f.Path.Contains("buildingDestruction")||f.Path.Contains("constructionInitialHealth")))
            {
                Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);
                foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,f.Minimum-1,f.Maximum+1})
                {var data=profile.CopyData();Assert.Throws<ArgumentOutOfRangeException>(()=>f.Write(data,invalid));typeof(PlayableProfileData).GetField(f.FieldName).SetValue(data,invalid);Assert.Catch<ArgumentException>(()=>PlayableProfile.Create(data));}
            }
        }
    }
}
