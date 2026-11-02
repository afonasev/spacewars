using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
public sealed class ProvingGroundContractTests
{
    private static void Destroy(OfflineParticipantAuthority authority)
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var domain=typeof(OfflineParticipantAuthority).GetField("domain",flags).GetValue(authority);
        authority.Navigation.ReleaseCommittedHostRoot();
        domain.GetType().GetMethod("DestroyOwnedNavigation",flags).Invoke(domain,null);
    }
    private static ProvingGroundSetup ResearchChainSetup(bool upgraded=false)
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);
        for(int side=0;side<2;side++){
            var site=setup.Profile.AuthoredMap.Sites(setup.Profile)[side];
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.ScientificCenter,PlayableBuildingKind.Hangar};
            for(int slot=0;slot<kinds.Length;slot++)Assert.IsNull(setup.PlaceBuilding(side,kinds[slot],site.Slots[slot].Position));
            if(upgraded)Assert.IsNull(setup.UpgradeBuilding(site.Id,site.Slots[1].Id));
        }
        return setup;
    }
    private static object ResearchChainCall(OfflineParticipantAuthority authority,string method,params object[] args)
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var domain=typeof(OfflineParticipantAuthority).GetField("domain",flags).GetValue(authority);
        return domain.GetType().GetMethod(method,flags).Invoke(domain,args);
    }
    [Test] public void RemovingUpperDefenseLevelsNeverDisplaysPhantomHealthAboveTheMaximum()
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);setup.SetResearch(0,PlayableResearchKind.UltimateDefensiveSystems,true);
        Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,new NavPoint(-12,-9)));
        var authority=new OfflineParticipantAuthority(setup.Configuration(),109);
        try{
            Assert.AreEqual(150,authority.View("player-1").Entities.Single().ExactHealth);
            Assert.IsNull(ResearchChainCall(authority,"SetProvingResearch",0,PlayableResearchKind.AdvancedDefensiveSystems,false));
            var tank=authority.View("player-1").Entities.Single();Assert.AreEqual(115,tank.MaxHealth);Assert.AreEqual(115,tank.ExactHealth);Assert.AreEqual(115,tank.Health);
            Assert.IsNull(ResearchChainCall(authority,"SetProvingResearch",0,PlayableResearchKind.TankChassis,true));
            tank=authority.View("player-1").Entities.Single();Assert.AreEqual(140,tank.MaxHealth);Assert.AreEqual(140,tank.ExactHealth);Assert.AreEqual(140,tank.Health);
        }finally{Destroy(authority);}
    }
    [Test] public void PanelCanRemoveCompletedAndQueuedCenterResearchAndReapplyWithoutDuplication()
    {
        var setup=ResearchChainSetup(upgraded:true);var authority=new OfflineParticipantAuthority(setup.Configuration(),106);
        try{
            int center=authority.View("player-1").Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.ScientificCenter).Id;
            double baseline=(double)ResearchChainCall(authority,"CurrentProvingResearchCost",0);
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(106,1,"player-1",PlayableCommandKind.QueueResearch,new[]{center},researchKind:PlayableResearchKind.TankChassis)).Status);
            ResearchChainCall(authority,"AdvanceResearch",setup.Profile.ProvingGroundActionSeconds);
            Assert.True(authority.View("player-1").OwnerResearch.Single(o=>o.Kind==PlayableResearchKind.TankChassis).Complete);
            Assert.IsNull(ResearchChainCall(authority,"SetProvingResearch",0,PlayableResearchKind.TankChassis,false));
            Assert.False(authority.View("player-1").OwnerResearch.Any(o=>o.Kind==PlayableResearchKind.TankChassis));
            Assert.AreEqual(baseline,ResearchChainCall(authority,"CurrentProvingResearchCost",0));
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(106,2,"player-1",PlayableCommandKind.QueueResearch,new[]{center},researchKind:PlayableResearchKind.UltimateDefensiveSystems)).Status);
            Assert.IsNull(ResearchChainCall(authority,"SetProvingResearch",0,PlayableResearchKind.ImprovedDefensiveSystems,false));
            Assert.False(authority.View("player-1").OwnerResearch.Any(o=>PlayableResearchRules.Defense(o.Kind)));
            for(int n=0;n<2;n++)Assert.IsNull(ResearchChainCall(authority,"SetProvingResearch",0,PlayableResearchKind.TankChassis,true));
            Assert.AreEqual(baseline+setup.Profile.TankChassisCost,(double)ResearchChainCall(authority,"CurrentProvingResearchCost",0));
        }finally{Destroy(authority);}
    }
    [TestCase(0)] [TestCase(1)]
    public void ConcreteIsFreeOwnerSpecificAndResetDoesNotDoubleApply(int side)
    {
        var setup=ResearchChainSetup();var authority=new OfflineParticipantAuthority(setup.Configuration(),97);
        try{
            string owner=side==0?"player-1":"enemy-1";var before=authority.View(owner);int center=before.Buildings.Single(b=>(int)b.Owner==side&&b.Kind==PlayableBuildingKind.ScientificCenter).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(97,1,owner,PlayableCommandKind.QueueResearch,new[]{center},researchKind:PlayableResearchKind.ReinforcedConcrete)).Status);
            Assert.AreEqual(setup.Profile.ProvingGroundActionSeconds,authority.View(owner).OwnerResearch.Single().Duration);Assert.Zero(authority.View(owner).Credits);
            ResearchChainCall(authority,"AdvanceResearch",5d);var after=authority.View(owner);
            foreach(var b in after.Buildings)Assert.AreEqual(PlayableStatRounding.MaximumHealth(setup.Profile,TerritoryRules.Health(setup.Profile,b.Kind)*((int)b.Owner==side?1.25:1)),b.MaxHealth);
            Assert.AreEqual(200,(double)ResearchChainCall(authority,"ProvingCost",side,MatchFactKind.ResearchCompleted));
            var reset=(ProvingGroundSetup)ResearchChainCall(authority,"CaptureProvingSetup");var restored=new OfflineParticipantAuthority(reset.Configuration(),98);
            try{foreach(var b in restored.View(owner).Buildings){Assert.AreEqual(PlayableStatRounding.MaximumHealth(setup.Profile,TerritoryRules.Health(setup.Profile,b.Kind)*((int)b.Owner==side?1.25:1)),b.MaxHealth);Assert.AreEqual(b.MaxHealth,b.ExactHealth??b.Health);}Assert.AreEqual(200,(double)ResearchChainCall(restored,"ProvingCost",side,MatchFactKind.ResearchCompleted));}
            finally{Destroy(restored);}
        }finally{Destroy(authority);}
    }
    [TestCase(0,PlayableResearchKind.AdvancedWeaponSystems)] [TestCase(1,PlayableResearchKind.AdvancedWeaponSystems)]
    [TestCase(0,PlayableResearchKind.AdvancedDefensiveSystems)] [TestCase(1,PlayableResearchKind.AdvancedDefensiveSystems)]
    public void ProvingLevelTwoIncludesPrerequisite(int side,PlayableResearchKind second)
    {
        var setup=ResearchChainSetup(upgraded:true);var authority=new OfflineParticipantAuthority(setup.Configuration(),94);
        double initialResearchCost=(double)ResearchChainCall(authority,"ProvingCost",side,MatchFactKind.ResearchCompleted);
        try{
            string owner=side==0?"player-1":"enemy-1";var view=authority.View(owner);int center=view.Buildings.Single(b=>(int)b.Owner==side&&b.Kind==PlayableBuildingKind.ScientificCenter).Id;
            var first=PlayableResearchRules.Previous(second).Value;
            Assert.True(view.ResearchAvailability.Single(r=>r.Kind==second).Available);
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(94,1,owner,PlayableCommandKind.QueueResearch,new[]{center},researchKind:second)).Status);
            var orders=authority.View(owner).OwnerResearch.Where(o=>!PlayableResearchRules.Local(o.Kind)).ToArray();CollectionAssert.AreEqual(new[]{first,second},orders.Select(o=>o.Kind));Assert.True(orders[0].Active);Assert.False(orders[1].Active);
            Assert.AreEqual(PlayableCommandStatus.Rejected,authority.Apply(new PlayableCommand(94,2,owner,PlayableCommandKind.QueueResearch,new[]{center},researchKind:second)).Status);
            ResearchChainCall(authority,"AdvanceResearch",5d);orders=authority.View(owner).OwnerResearch.Where(o=>!PlayableResearchRules.Local(o.Kind)).ToArray();Assert.True(orders[0].Complete);Assert.True(orders[1].Active);Assert.False(orders[1].Complete);
            ResearchChainCall(authority,"AdvanceResearch",4d);Assert.False(authority.View(owner).OwnerResearch.Where(o=>!PlayableResearchRules.Local(o.Kind)).ToArray()[1].Complete);
            ResearchChainCall(authority,"AdvanceResearch",1d);Assert.True(authority.View(owner).OwnerResearch.Where(o=>!PlayableResearchRules.Local(o.Kind)).ToArray().All(o=>o.Complete));
            Assert.AreEqual(PlayableResearchRules.Cost(setup.Profile,first)+PlayableResearchRules.Cost(setup.Profile,second),(double)ResearchChainCall(authority,"ProvingCost",side,MatchFactKind.ResearchCompleted)-initialResearchCost);
        }finally{Destroy(authority);}
    }
    [TestCase(false)] [TestCase(true)]
    public void ProvingResearchChainCancellationNeverRunsOrphanLevelTwo(bool interrupt)
    {
        var setup=ResearchChainSetup(upgraded:true);var authority=new OfflineParticipantAuthority(setup.Configuration(),95);
        double initialResearchCost=(double)ResearchChainCall(authority,"ProvingCost",0,MatchFactKind.ResearchCompleted);
        try{
            int center=authority.View("player-1").Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.ScientificCenter).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(95,1,"player-1",PlayableCommandKind.QueueResearch,new[]{center},researchKind:PlayableResearchKind.AdvancedWeaponSystems)).Status);
            var active=authority.View("player-1").OwnerResearch.Where(o=>!PlayableResearchRules.Local(o.Kind)).ToArray().Single(o=>o.Active);
            if(interrupt)ResearchChainCall(authority,"InterruptResearchCenter",center);
            else Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(95,2,"player-1",PlayableCommandKind.CancelResearch,new[]{center},productionOrderId:active.Id)).Status);
            ResearchChainCall(authority,"AdvanceResearch",10d);Assert.IsEmpty(authority.View("player-1").OwnerResearch.Where(o=>!PlayableResearchRules.Local(o.Kind)).ToArray());
            Assert.Zero((double)ResearchChainCall(authority,"ProvingCost",0,MatchFactKind.ResearchCompleted)-initialResearchCost);
        }finally{Destroy(authority);}
    }
    [Test] public void ProvingResearchChainCapacityIsAtomic()
    {
        var setup=ResearchChainSetup(upgraded:true);var authority=new OfflineParticipantAuthority(setup.Configuration(),96);
        try{
            int center=authority.View("player-1").Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.ScientificCenter).Id;long sequence=0;
            foreach(var kind in new[]{PlayableResearchKind.TankChassis,PlayableResearchKind.ExplorerAssaultGuns,PlayableResearchKind.ShkvalGuidance,PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.HawkSalvoSystems})
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(96,++sequence,"player-1",PlayableCommandKind.QueueResearch,new[]{center},researchKind:kind)).Status);
            Assert.AreEqual(PlayableCommandStatus.Overflow,authority.Apply(new PlayableCommand(96,++sequence,"player-1",PlayableCommandKind.QueueResearch,new[]{center},researchKind:PlayableResearchKind.AdvancedDefensiveSystems)).Status);
            Assert.AreEqual(5,authority.View("player-1").OwnerResearch.Where(o=>!PlayableResearchRules.Local(o.Kind)).ToArray().Length);Assert.False(authority.View("player-1").OwnerResearch.Where(o=>!PlayableResearchRules.Local(o.Kind)).ToArray().Any(o=>PlayableResearchRules.Defense(o.Kind)));
        }finally{Destroy(authority);}
    }
    [Test] public void CenterUpgradeRecipeIsLocalAndRetainsResearchCostAcrossReset()
    {
        var setup=ResearchChainSetup();var authority=new OfflineParticipantAuthority(setup.Configuration(),101);
        try{
            int id=authority.View("player-1").Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.ScientificCenter).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(101,1,"player-1",PlayableCommandKind.QueueResearch,new[]{id},researchKind:PlayableResearchKind.ScientificCenterUpgrade)).Status);
            ResearchChainCall(authority,"AdvanceResearch",5d);Assert.True(authority.View("player-1").Buildings.Single(b=>b.Id==id).ScientificCenterUpgraded);
            Assert.False(authority.View("enemy-1").Buildings.Single(b=>b.Owner==PlayableOwner.Enemy&&b.Kind==PlayableBuildingKind.ScientificCenter).ScientificCenterUpgraded);
            Assert.AreEqual(300,(double)ResearchChainCall(authority,"ProvingCost",0,MatchFactKind.ResearchCompleted));
            var captured=(ProvingGroundSetup)ResearchChainCall(authority,"CaptureProvingSetup");var restored=new OfflineParticipantAuthority(captured.Configuration(),102);
            try{Assert.True(restored.View("player-1").Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.ScientificCenter).ScientificCenterUpgraded);Assert.AreEqual(300,(double)ResearchChainCall(restored,"ProvingCost",0,MatchFactKind.ResearchCompleted));}
            finally{Destroy(restored);}
        }finally{Destroy(authority);}
    }
    [TestCase(0)] [TestCase(1)] public void AllNewProvingUpgradesAreFreeAndSurviveRecipeReset(int side)
    {
        var setup=ResearchChainSetup();var authority=new OfflineParticipantAuthority(setup.Configuration(),103);
        try{
            string owner=side==0?"player-1":"enemy-1";var view=authority.View(owner);int center=view.Buildings.Single(b=>(int)b.Owner==side&&b.Kind==PlayableBuildingKind.ScientificCenter).Id;long seq=0;
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(103,++seq,owner,PlayableCommandKind.QueueResearch,new[]{center},researchKind:PlayableResearchKind.ScientificCenterUpgrade)).Status);ResearchChainCall(authority,"AdvanceResearch",5d);
            foreach(var target in new[]{PlayableResearchKind.UltimateWeaponSystems,PlayableResearchKind.UltimateDefensiveSystems,PlayableResearchKind.AdvancedReinforcedConcrete}){
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(103,++seq,owner,PlayableCommandKind.QueueResearch,new[]{center},researchKind:target)).Status);
                for(int i=0;i<3;i++)ResearchChainCall(authority,"AdvanceResearch",5d);
                Assert.True(authority.View(owner).OwnerResearch.Single(o=>o.Kind==target).Complete);
            }
            view=authority.View(owner);Assert.Zero(view.Credits);Assert.AreEqual(9,view.OwnerResearch.Count);Assert.True(view.OwnerResearch.All(o=>o.Complete));Assert.AreEqual(2500,(double)ResearchChainCall(authority,"ProvingCost",side,MatchFactKind.ResearchCompleted));
            var captured=(ProvingGroundSetup)ResearchChainCall(authority,"CaptureProvingSetup");var reset=new OfflineParticipantAuthority(captured.Configuration(),104);
            try{var after=reset.View(owner);Assert.AreEqual(9,after.OwnerResearch.Count);Assert.True(after.Buildings.Single(b=>b.Id==center).ScientificCenterUpgraded);Assert.AreEqual(2500,(double)ResearchChainCall(reset,"ProvingCost",side,MatchFactKind.ResearchCompleted));foreach(var b in after.Buildings)Assert.AreEqual(TerritoryRules.Health(setup.Profile,b.Kind)*((int)b.Owner==side?1.5:1),b.MaxHealth);}
            finally{Destroy(reset);}
        }finally{Destroy(authority);}
    }
    [TestCase(0)] [TestCase(1)] public void NewRefineryUpgradeOnProvingRequiresOwnImprovedCenter(int side)
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);var site=setup.Profile.AuthoredMap.Sites(setup.Profile)[side];
        foreach(var row in new[]{(PlayableBuildingKind.Factory,0),(PlayableBuildingKind.ScientificCenter,1),(PlayableBuildingKind.Refinery,2)})Assert.IsNull(setup.PlaceBuilding(side,row.Item1,site.Slots[row.Item2].Position));
        var a=new OfflineParticipantAuthority(setup.Configuration(),106);
        try{string owner=side==0?"player-1":"enemy-1";var before=a.View(owner);int refinery=before.Buildings.Single(b=>(int)b.Owner==side&&b.Kind==PlayableBuildingKind.Refinery).Id,center=before.Buildings.Single(b=>(int)b.Owner==side&&b.Kind==PlayableBuildingKind.ScientificCenter).Id;
            Assert.AreEqual(PlayableCommandStatus.Rejected,a.Apply(new PlayableCommand(106,1,owner,PlayableCommandKind.UpgradeRefinery,new[]{refinery})).Status);Assert.Zero(a.View(owner).Credits);
            var blocked=OfflinePadWorldActions.Building(a.View(owner),setup.Profile,refinery,freeEconomy:true).Single(x=>x.Sector.Id.EndsWith(":upgrade"));Assert.Null(blocked.Command);Assert.That(blocked.Detail,Does.Contain("улучшенный"));
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(106,2,owner,PlayableCommandKind.QueueResearch,new[]{center},researchKind:PlayableResearchKind.ScientificCenterUpgrade)).Status);ResearchChainCall(a,"AdvanceResearch",5d);
            Assert.NotNull(OfflinePadWorldActions.Building(a.View(owner),setup.Profile,refinery,freeEconomy:true).Single(x=>x.Sector.Id.EndsWith(":upgrade")).Command);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(106,3,owner,PlayableCommandKind.UpgradeRefinery,new[]{refinery})).Status);ResearchChainCall(a,"AdvanceRefineryUpgrades",5d);Assert.True(a.View(owner).Buildings.Single(b=>b.Id==refinery).RefineryUpgraded);
        }finally{Destroy(a);}
    }
    [Test] public void ProvingMenusAdmitFreeActionsWithoutPricesAndKeepOrdinaryAffordability()
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);var site=setup.Profile.AuthoredMap.Sites(setup.Profile)[0];
        var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.ScientificCenter,PlayableBuildingKind.Refinery};
        for(int i=0;i<kinds.Length;i++)Assert.IsNull(setup.PlaceBuilding(0,kinds[i],site.Slots[i].Position));
        Assert.IsNull(setup.UpgradeBuilding(site.Id,site.Slots[1].Id));
        var authority=new OfflineParticipantAuthority(setup.Configuration(),93);
        try{var view=authority.View("player-1");Assert.AreEqual(0,view.Credits);
            foreach(var building in view.Buildings.Where(b=>b.Owner==PlayableOwner.Player)){
                var actions=OfflinePadWorldActions.Building(view,setup.Profile,building.Id,freeEconomy:true);
                Assert.True(actions.All(a=>!a.Price.HasValue));
                foreach(var action in actions){Assert.False(action.Detail.ToLowerInvariant().Contains("кредит"),action.Detail);Assert.False(action.Detail.Contains("возврат"),action.Detail);}
                Assert.False(PlayableMenuDescriptions.Building(setup.Profile,building.Kind,freeEconomy:true).ToLowerInvariant().Contains("кредит"));
                if(building.Kind==PlayableBuildingKind.Factory){
                    Assert.NotNull(actions.Single(a=>a.Sector.Id.EndsWith(":tank")).Command);
                    Assert.IsNull(OfflinePadWorldActions.Building(view,setup.Profile,building.Id).Single(a=>a.Sector.Id.EndsWith(":tank")).Command,"Ordinary zero-credit buying remains blocked");
                }
                if(building.Kind==PlayableBuildingKind.ScientificCenter)Assert.NotNull(actions.Single(a=>a.Sector.Id.EndsWith(":tank-chassis")).Command);
                if(building.Kind==PlayableBuildingKind.Refinery)Assert.NotNull(actions.Single(a=>a.Sector.Id.EndsWith(":upgrade")).Command);
            }
            var outpost=setup.Profile.AuthoredMap.Sites(setup.Profile)[2];var build=OfflinePadWorldActions.Build(view,setup.Profile,outpost.Id,1,freeEconomy:true);
            Assert.IsNotEmpty(build);Assert.True(build.All(a=>a.Command!=null&&!a.Price.HasValue&&!a.Detail.ToLowerInvariant().Contains("кредит")));
            Assert.False(PlayableMenuDescriptions.Unit(setup.Profile,PlayableEntityKind.Tank,freeEconomy:true).Contains("кредит"));
        }finally{Destroy(authority);}
    }
    [Test] public void LiveConstructedBuildingsProduceAValidResetRecipeWithAuthoredSlotHeading()
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);var a=new OfflineParticipantAuthority(setup.Configuration(),91);
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var domain=typeof(OfflineParticipantAuthority).GetField("domain",flags).GetValue(a);
        object Call(string method,params object[] args)=>domain.GetType().GetMethod(method,flags).Invoke(domain,args);
        try{
            int parent=a.View("player-1").Buildings.Single(b=>b.SiteId==1&&b.SlotId==0).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(91,1,"player-1",PlayableCommandKind.BuildAt,null,siteId:1,slotId:1,parentId:parent,buildingKind:PlayableBuildingKind.Factory)).Status);
            Call("AdvanceFoundations");Call("AdvanceBuildings",5d);
            Assert.True(a.View("player-1").Buildings.Single(b=>b.SiteId==1&&b.SlotId==1).Progress>=1);
            var captured=(ProvingGroundSetup)Call("CaptureProvingSetup");
            Assert.AreEqual(setup.Profile.AuthoredMap.Sites(setup.Profile)[0].Slots[0].Heading,captured.Buildings.Single().Heading);
            OfflineMatchConfiguration config=null;Assert.DoesNotThrow(()=>config=captured.Configuration(),"Keep the strict ordinary placement validator");
            var reset=new OfflineParticipantAuthority(config,92);try{Assert.AreEqual(1,reset.View("player-1").Buildings.Count(b=>b.Kind==PlayableBuildingKind.Factory&&b.Progress>=1));}finally{Destroy(reset);}
        }finally{Destroy(a);}
    }
    [Test] public void ActionDurationUsesTheLabMetadataAndHardRangeValidation()
    {
        var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="provingGround.actionSeconds");
        Assert.AreEqual("с",field.Unit);Assert.AreEqual(.1,field.Minimum);Assert.AreEqual(60,field.Maximum);Assert.AreEqual(.1,field.Step);
        var data=PlayableProfile.Default.CopyData();Assert.AreEqual(5,field.Read(data));
        foreach(double invalid in new[]{0d,60.1,double.NaN,double.PositiveInfinity}){data.provingGroundActionSeconds=invalid;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));}
        data.provingGroundActionSeconds=7;Assert.AreEqual(7,new ProvingGroundSetup(PlayableProfile.Create(data)).Profile.ProvingGroundActionSeconds);
    }
    [Test] public void FreeActionsUseFiveSecondTermsAndCumulativeOriginalPrices()
    {
        var data=PlayableProfile.Default.CopyData();data.tankCreditCost=12345;data.tankChassisCost=4000;
        var setup=new ProvingGroundSetup(PlayableProfile.Create(data));
        for(int side=0;side<2;side++){
            var site=setup.Profile.AuthoredMap.Sites(setup.Profile)[side];
            Assert.IsNull(setup.PlaceBuilding(side,PlayableBuildingKind.Factory,site.Slots[0].Position));
            Assert.IsNull(setup.PlaceBuilding(side,PlayableBuildingKind.ScientificCenter,site.Slots[1].Position));
        }
        var authority=new OfflineParticipantAuthority(setup.Configuration(),89);
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var domain=typeof(OfflineParticipantAuthority).GetField("domain",flags).GetValue(authority);
        object Call(string method,params object[] args)=>domain.GetType().GetMethod(method,flags).Invoke(domain,args);
        try{for(int side=0;side<2;side++){
            string owner=side==0?"player-1":"enemy-1";var view=authority.View(owner);
            var factory=view.Buildings.Single(b=>(int)b.Owner==side&&b.Kind==PlayableBuildingKind.Factory);
            var center=view.Buildings.Single(b=>(int)b.Owner==side&&b.Kind==PlayableBuildingKind.ScientificCenter);
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(89,1,owner,PlayableCommandKind.QueueTank,new[]{factory.Id})).Status);
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(89,2,owner,PlayableCommandKind.QueueResearch,new[]{center.Id},researchKind:PlayableResearchKind.TankChassis)).Status);
            view=authority.View(owner);Assert.AreEqual(0,view.Credits);Assert.AreEqual(12345,setup.Profile.TankCreditCost);
            Assert.AreEqual(5,view.Buildings.Single(b=>b.Id==factory.Id).PrivateState.Orders.Single().Duration);
            Assert.AreEqual(5,view.OwnerResearch.Single().Duration);
            Assert.AreEqual(0,(double)Call("ProvingCost",side,MatchFactKind.UnitCompleted));
            Assert.AreEqual(0,(double)Call("ProvingCost",side,MatchFactKind.ResearchCompleted));
            Assert.IsNull(Call("PlaceProvingUnit",side,PlayableEntityKind.Tank,new NavPoint(side==0?-15:15,20)));
            Assert.AreEqual(12345,(double)Call("ProvingCost",side,MatchFactKind.UnitCompleted));
            Call("RemoveProvingUnits",side,true);
            Assert.AreEqual(12345,(double)Call("ProvingCost",side,MatchFactKind.UnitCompleted),"Loss/removal never reduces investment");
        }
        Call("AdvanceResearch",4.99d);Assert.AreEqual(0,(double)Call("ProvingCost",0,MatchFactKind.ResearchCompleted));
        Call("AdvanceResearch",.01d);Assert.AreEqual(4000,(double)Call("ProvingCost",0,MatchFactKind.ResearchCompleted));
        Assert.AreEqual(4000,(double)Call("ProvingCost",1,MatchFactKind.ResearchCompleted));
        Assert.AreEqual(0,authority.View("player-1").Credits);
        }finally{Destroy(authority);}
    }
    [Test] public void PlacementResearchCostsAndResetUseIndependentSnapshot()
    {
        var data=PlayableProfile.Default.CopyData();data.tankCreditCost=321;
        var setup=new ProvingGroundSetup(PlayableProfile.Create(data));data.tankCreditCost=999;
        Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,new NavPoint(-10,0)));
        Assert.NotNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,new NavPoint(10,0)));
        Assert.NotNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,new NavPoint(-10,0)));
        var slot=setup.Profile.AuthoredMap.Sites(setup.Profile)[0].Slots[0];
        Assert.NotNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,slot.Position));
        Assert.IsNull(setup.PlaceBuilding(0,PlayableBuildingKind.Factory,slot.Position));
        Assert.NotNull(setup.PlaceBuilding(0,PlayableBuildingKind.Factory,slot.Position));
        var right=setup.Profile.AuthoredMap.Sites(setup.Profile)[1].Slots[0];Assert.IsNull(setup.PlaceBuilding(1,PlayableBuildingKind.ScientificCenter,right.Position));
        setup.SetResearch(0,PlayableResearchKind.AdvancedDefensiveSystems,true);setup.SetResearch(0,PlayableResearchKind.TankChassis,true);
        Assert.AreEqual(321,setup.ArmyCost(0));Assert.AreEqual(0,setup.ArmyCost(1));Assert.AreEqual(0,setup.ResearchCost(1));
        Assert.AreEqual(setup.Profile.DefensiveSystemsLevel1Cost+setup.Profile.DefensiveSystemsLevel2Cost+setup.Profile.TankChassisCost,setup.ResearchCost(0));
        var config=setup.Configuration();var a=new OfflineParticipantAuthority(config,82);
        try{
            var v=a.View("player-1");Assert.AreEqual(0,v.Credits);Assert.AreEqual(8,v.Buildings.Count);Assert.AreEqual(3,v.OwnerResearch.Count);Assert.True(v.Entities.Single().Upgraded);
            Assert.AreEqual(155,v.Entities.Single().MaxHealth,1e-6);
            Assert.AreEqual(0,a.View("enemy-1").OwnerResearch.Count);
            setup.SetResearch(0,PlayableResearchKind.ImprovedDefensiveSystems,false);Assert.False(setup.Research(0).Contains(PlayableResearchKind.AdvancedDefensiveSystems));Assert.AreEqual(3,config.ProvingGround.Research(0).Count,"Running recipe is a snapshot");
            var map=(ProvingGroundMap)setup.Profile.AuthoredMap;Assert.Greater(map.AttackDestination(PlayableOwner.Player).X,map.Headquarters(PlayableOwner.Enemy).X);Assert.Less(map.AttackDestination(PlayableOwner.Enemy).X,map.Headquarters(PlayableOwner.Player).X);
        }finally{Destroy(a);}
        var reset=new OfflineParticipantAuthority(config,83);try{Assert.AreEqual(0,reset.Tick);Assert.AreEqual(0,reset.View("player-1").Credits);Assert.AreEqual(3,reset.View("player-1").OwnerResearch.Count);Assert.AreEqual(new NavPoint(-10,0),reset.View("player-1").Entities.Single().Position);}finally{Destroy(reset);}
    }
    [Test] public void BothSideFreeInstantUpgradesRemainInFrozenResetRecipe()
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);
        for(int side=0;side<2;side++){
            var site=setup.Profile.AuthoredMap.Sites(setup.Profile)[side];
            Assert.IsNull(setup.PlaceBuilding(side,PlayableBuildingKind.Refinery,site.Slots[0].Position));
            Assert.IsNull(setup.UpgradeBuilding(site.Id,site.Slots[0].Id));
            Assert.NotNull(setup.UpgradeBuilding(site.Id,site.Slots[0].Id));
        }
        var config=setup.Configuration();setup.RemoveBuilding(2,1);
        for(int generation=84;generation<=85;generation++){
            var authority=new OfflineParticipantAuthority(config,generation);
            try{foreach(var owner in new[]{"player-1","enemy-1"}){
                var view=authority.View(owner);Assert.AreEqual(0,view.Tick);Assert.AreEqual(0,view.Credits);
                var b=view.Buildings.Single(x=>x.Kind==PlayableBuildingKind.Refinery&&x.Owner==view.Owner);
                Assert.True(b.RefineryUpgraded);Assert.True(b.PrivateState.Upgrade.Complete);Assert.False(b.PrivateState.Upgrade.Active);
                Assert.AreEqual(0,b.PrivateState.Upgrade.PaidCost);Assert.AreEqual(0,view.OwnerResearch.Count,"No science/research prerequisite");
            }}finally{Destroy(authority);}
        }
        Assert.False(setup.BuildingUpgraded(2,1),"Removing a building removes its upgrade");
    }
    [Test] public void InitialMixedAttackPublishesGroundRecipients()
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);
        var kinds=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk};
        for(int side=0;side<2;side++)for(int i=0;i<kinds.Length;i++)Assert.IsNull(setup.PlaceUnit(side,kinds[i],new NavPoint(side==0?-12:12,-9+6*i)));
        var a=new OfflineParticipantAuthority(setup.Configuration(),87);
        try{
            foreach(var owner in new[]{"player-1","enemy-1"}){
                var view=a.View(owner);var ids=view.Entities.Where(u=>u.Owner==view.Owner).Select(u=>u.Id).ToArray();
                var receipt=a.Apply(new PlayableCommand(87,1,owner,PlayableCommandKind.AttackMove,ids,((ProvingGroundMap)setup.Profile.AuthoredMap).AttackDestination(view.Owner)));
                TestContext.WriteLine("INITIAL "+owner+" status="+receipt.Status+" pending="+a.Navigation.PendingCount+" requests="+a.Navigation.Requests.Count+" units="+Newtonsoft.Json.JsonConvert.SerializeObject(a.View(owner).Entities));
                Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status);
                foreach(var id in ids.Where(id=>view.Entities.Single(u=>u.Id==id).Kind!=PlayableEntityKind.Hawk))Assert.True(a.Navigation.IsPending(id),"Ground recipient "+id+" must have a pending route");
            }
        }finally{Destroy(a);}
    }
    [Test] public void RearAttackLaneKeepsRearObjectiveAndClearGroundApproach()
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);var map=(ProvingGroundMap)setup.Profile.AuthoredMap;
        var authority=new OfflineParticipantAuthority(setup.Configuration(),88);
        try{for(int side=0;side<2;side++){
            var owner=(PlayableOwner)side;var goal=map.AttackDestination(owner,setup.Profile);var enemy=map.Headquarters((PlayableOwner)(1-side));
            Assert.Greater(Math.Abs(goal.X),Math.Abs(enemy.X),"Objective remains behind opposing headquarters");
            for(int i=0;i<3;i++){
                var start=new NavPoint(side==0?-12:12,-9+6*i);var radius=PlayableUnitRules.Radius(setup.Profile,(PlayableEntityKind)i);
                Assert.False(authority.NavigationBinding.Geometry.SegmentFree(start,map.AttackDestination(owner,PlayableProfile.Create(LegacyRearLane())),radius),"Old collinear goal intersects HQ footprint");
                Assert.True(authority.NavigationBinding.Geometry.SegmentFree(start,goal,radius),"Rear lane has a clear direct approach for ground kind "+i);
            }
        }}finally{Destroy(authority);}
    }
    private static PlayableProfileData LegacyRearLane(){var data=PlayableProfile.Default.CopyData();data.provingGroundAttackRearLaneOffset=0;return data;}
    [UnityTest] public IEnumerator LaboratoryLaunchRetainsUnsavedDraftWithoutWritingVersion()
    {
        var folder=Path.Combine(Application.temporaryCachePath,"proving-lab-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var host=new GameObject("Proving lab contract");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;OrbitalTheme.Install(root);var nav=new NativeMenuNavigation(root);
        try{
            var store=new NativeBalanceStore(PlayableProfile.Default,Path.Combine(folder,"balance.json"));PlayableProfile launched=null;
            var lab=new NativeBalanceView(root,nav,store,()=>{},openProvingGround:(p,a)=>launched=p);yield return null;
            var field=lab.Page.Q<TextField>("lab-field-tankHealth");Assert.NotNull(field);field.value="222";
            int versions=store.Versions.Count;string contents=File.Exists(store.StoragePath)?File.ReadAllText(store.StoragePath):null;
            lab.Page.Q<Button>("lab-proving-ground").Focus();nav.Activate();Assert.NotNull(launched);Assert.AreEqual(222,launched.TankHealth);
            lab.Page.style.display=DisplayStyle.None;lab.RestoreFromProvingGround();yield return null;
            Assert.AreEqual("222",field.value);Assert.True(lab.Page.Q<Button>("lab-save").enabledSelf);Assert.AreEqual(versions,store.Versions.Count);Assert.AreEqual(contents,File.Exists(store.StoragePath)?File.ReadAllText(store.StoragePath):null);
            field.value="223";lab.Page.Q<Button>("lab-proving-ground").Focus();nav.Activate();Assert.AreEqual(223,launched.TankHealth);
            field.value="invalid";lab.Page.Q<Button>("lab-proving-ground").Focus();nav.Activate();Assert.AreEqual(223,launched.TankHealth,"Invalid draft must not launch");
        }finally{nav.Dispose();UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);Directory.Delete(folder,true);}
    }
}
