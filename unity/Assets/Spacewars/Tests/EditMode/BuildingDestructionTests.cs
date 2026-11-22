using System;
using System.Linq;
using System.Reflection;
using System.Threading;
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
            const double damage=2;
            Call("Damage",id,1);
            Call("AdvanceBuildings",profile.FactoryBuildSeconds/2d);
            var half=Player(PlayableOwner.Player).Buildings.Single(b=>b.Id==id);
            Assert.AreEqual(.5,half.Progress,1e-9);Assert.AreEqual(profile.FactoryHealth*.625-damage,half.ExactHealth.Value,1e-9);
            Call("AdvanceBuildings",profile.FactoryBuildSeconds/2d-1d/30d);
            Assert.AreEqual(ConstructionPhase.Constructing,Player(PlayableOwner.Player).Buildings.Single(b=>b.Id==id).Phase);
            Call("AdvanceBuildings",1d/30d);
            var final=Player(PlayableOwner.Player).Buildings.Single(b=>b.Id==id);Assert.AreEqual(ConstructionPhase.Ready,final.Phase);Assert.AreEqual(profile.FactoryHealth-damage,final.ExactHealth.Value,1e-8);
            Call("Damage",id,1);
            Assert.AreEqual(profile.FactoryHealth-damage-1,Player(PlayableOwner.Player).Buildings.Single(b=>b.Id==id).ExactHealth.Value,1e-8);
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
        [TestCase(false)]
        [TestCase(true)]
        public void SolidBuildingRemovalWaitsForTheRouteHostWithoutPartialEffects(bool sale)
        {
            int id=Factory(sale);
            var d=(PlayableDomain)domain;
            var footprint=View().Buildings.Single(b=>b.Id==id).Position;
            Assert.IsFalse(d.Geometry.IsFree(footprint,0));
            if(sale)
            {
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("SellBuilding",id,PlayableOwner.Player,null));
                Call("AdvanceBuildingLifecycle",(double)profile.BuildingSaleDemolitionSec-1d/60);
            }
            int mover=(int)Call("SpawnUnit",new NavPoint(-18,-14),PlayableOwner.Player,PlayableEntityKind.Tank);
            Assert.AreEqual(PlayableCommandStatus.Applied,
                d.Apply(new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{mover},new NavPoint(-8,-14)),out _));
            Assert.Greater(d.Navigation.PendingCount,0);
            var geometry=d.Geometry;
            var before=d.CaptureWorldBytes(7,"building-removal-route-host");
            double credits=(double)Call("Balance",PlayableOwner.Player);
            int sounds=View().Sounds.Count(s=>s.Kind==PlayableSoundKind.ConstructionCancelled||s.Kind==PlayableSoundKind.Demolished);
            Exception workerError=null;
            using var entered=new ManualResetEventSlim();
            using var release=new ManualResetEventSlim();
            var host=new Thread(()=>{
                try{
                    long floor=d.Navigation.RoutePort.BeginHostAllocation();
                    try{entered.Set();if(!release.Wait(5000))throw new TimeoutException("Release the route allocation boundary.");}
                    finally{d.Navigation.RoutePort.EndHostAllocation(floor);}
                }catch(Exception error){workerError=error;entered.Set();}
            });
            host.Start();
            try
            {
                Assert.True(entered.Wait(5000));Assert.IsNull(workerError);
                if(sale)Assert.DoesNotThrow(()=>Call("AdvanceBuildingLifecycle",1d/30));
                else Assert.AreEqual(PlayableCommandStatus.Rejected,Call("CancelBuilding",id,PlayableOwner.Player,null));
                Assert.AreSame(geometry,d.Geometry);
                Assert.True(View().Buildings.Any(b=>b.Id==id));
                Assert.AreEqual(credits,(double)Call("Balance",PlayableOwner.Player));
                Assert.AreEqual(sounds,View().Sounds.Count(s=>s.Kind==PlayableSoundKind.ConstructionCancelled||s.Kind==PlayableSoundKind.Demolished));
            }
            finally{release.Set();Assert.True(host.Join(5000));}
            Assert.IsNull(workerError);
            CollectionAssert.AreEqual(before,d.CaptureWorldBytes(7,"building-removal-route-host"));
            var restored=PlayableDomain.RestoreWorldBytes(before,profile,7,"building-removal-route-host");
            try
            {
                CollectionAssert.AreEqual(before,restored.CaptureWorldBytes(7,"building-removal-route-host"));
                if(sale)Call("AdvanceBuildingLifecycle",1d/30);
                else Assert.AreEqual(PlayableCommandStatus.Applied,Call("CancelBuilding",id,PlayableOwner.Player,null));
                Assert.IsFalse(View().Buildings.Any(b=>b.Id==id));
                Assert.True(d.Geometry.IsFree(footprint,0));
                Assert.AreEqual(geometry.Revision+1,d.Geometry.Revision);
                Assert.AreEqual(sale?credits:credits+profile.FactoryCreditCost*profile.BuildingCancellationRefundRatio,
                    (double)Call("Balance",PlayableOwner.Player),1e-9);
                Call("AdvanceBuildingLifecycle",1d/30);
                Assert.AreEqual(sounds+1,View().Sounds.Count(s=>s.Kind==PlayableSoundKind.ConstructionCancelled||s.Kind==PlayableSoundKind.Demolished));
                Assert.AreEqual(geometry.Revision+1,d.Geometry.Revision);
            }
            finally{restored.DestroyOwnedNavigation();}
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
