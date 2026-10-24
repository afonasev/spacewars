using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class CampaignDisputedTerritoryStateTests
    {
        static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static OfflineMatchConfiguration Config()=>CampaignReconOpening.Configuration(PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap()));
        static object Domain(OfflineParticipantAuthority a)=>typeof(OfflineParticipantAuthority).GetField("domain",F).GetValue(a);
        static byte[] Guards(OfflineParticipantAuthority a)=>(byte[])Domain(a).GetType().GetField("campaignGuards",F).GetValue(Domain(a));
        static void Damage(OfflineParticipantAuthority a,int id,int amount=100000)=>OfflineParticipantAuthorityTests.Call(a,"Damage",id,amount);
        static void Place(OfflineParticipantAuthority a,int id,NavPoint point)
        {
            Assert.True(a.Navigation.Crowd.TryGet(id,out var u));var args=new object[]{point,u.Radius,default(NavLocation)};
            Assert.True((bool)typeof(NavCrowd).GetMethod("Locate",F).Invoke(a.Navigation.Crowd,args));
            typeof(NavUnit).GetProperty("Position").SetValue(u,point);typeof(NavUnit).GetProperty("Location").SetValue(u,(NavLocation)args[2]);
        }
        static int Player(OfflineParticipantAuthority a)=>a.View("player-1").Entities.First(e=>e.Owner==PlayableOwner.Player).Id;
        static void Opening(OfflineParticipantAuthority a)
        {
            int id=Player(a);Place(a,id,CampaignReconOpening.Rendezvous);a.Step(1d/30);Assert.True(a.View("player-1").Mission.Reinforced);
            Place(a,id,CampaignReconOpening.Explore);a.Step(1d/30);Damage(a,a.View("corporation").Buildings.Single(b=>b.SiteId==1).Id);a.Step(1d/30);
            Place(a,id,CampaignReconOpening.Fork);a.Step(1d/30);Assert.True(a.View("player-1").Mission.ForkRevealed);
        }
        static void Clear(OfflineParticipantAuthority a,int group)
        {
            foreach(var u in a.View("corporation").Entities.Where(u=>u.Owner==PlayableOwner.Enemy&&CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[group],u.Position)).ToArray())Damage(a,u.Id);
            foreach(var b in a.View("corporation").Buildings.Where(b=>CampaignReconOpening.GuardSites[group].Contains(b.SiteId)).ToArray())Damage(a,b.Id);
            a.Step(1d/30);
        }
        [TestCase(1)] [TestCase(4)] public void LethalDirectHitRetainsProjectileAndBuildingWhenGeometryRefuses(int site)
        {
            var config=Config();var a=new OfflineParticipantAuthority(config,115);var domain=Domain(a);var type=domain.GetType();
            const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(115,1,"player-1",PlayableCommandKind.Move,new[]{Player(a)},CampaignReconOpening.Rendezvous)).Status);
            var target=a.View("corporation").Buildings.Single(b=>b.SiteId==site&&b.SlotId==0);
            var buildings=(System.Collections.IDictionary)type.GetField("buildings",fields).GetValue(domain);
            var expected=buildings.Keys.Cast<int>().OrderBy(x=>x).ToArray();
            var shot=Activator.CreateInstance(type.GetNestedType("Projectile",BindingFlags.NonPublic));
            void Set(string name,object value)=>shot.GetType().GetField(name,fields).SetValue(shot,value);
            int next=(int)type.GetField("nextProjectile",fields).GetValue(domain);type.GetField("nextProjectile",fields).SetValue(domain,next+1);
            Set("Id",next);Set("TermsRevision",config.Profile.Revision);Set("Owner",Player(a));Set("Faction",PlayableOwner.Player);Set("Target",target.Id);Set("Kind",PlayableEntityKind.Explorer);Set("Position",target.Position);Set("Height",1d);Set("Speed",config.Profile.ExplorerProjectileSpeed);Set("DirectionX",1d);Set("Damage",100000d);Set("Remaining",config.Profile.ExplorerRange);
            var shots=(System.Collections.IList)type.GetField("projectiles",fields).GetValue(domain);shots.Add(shot);
            var geometry=a.Navigation.AuthorityGeometry;var guardBefore=(byte[])Guards(a).Clone();
            a.Navigation.PhysicalHostBudget.AllocationAdmission=_=>false;
            try{type.GetMethod("AdvanceProjectiles",F).Invoke(domain,new object[]{1d/30});}
            finally{a.Navigation.PhysicalHostBudget.AllocationAdmission=null;}
            Assert.AreEqual(1,shots.Count);Assert.AreSame(shot,shots[0]);Assert.AreEqual(target.Position,shot.GetType().GetField("Position",fields).GetValue(shot));
            Assert.AreSame(geometry,a.Navigation.AuthorityGeometry);CollectionAssert.AreEqual(expected,buildings.Keys.Cast<int>().OrderBy(x=>x));CollectionAssert.AreEqual(guardBefore,Guards(a));
            Assert.AreEqual(target.ExactHealth,(double)buildings[target.Id].GetType().GetField("Health",fields).GetValue(buildings[target.Id]));
            type.GetMethod("AdvanceProjectiles",F).Invoke(domain,new object[]{1d/30});
            Assert.Zero(shots.Count);Assert.False(buildings.Contains(target.Id));Assert.AreEqual(geometry.Revision+1,a.Navigation.AuthorityGeometry.Revision);
            var bytes=a.CaptureBytes();CollectionAssert.AreEqual(bytes,OfflineParticipantAuthority.Restore(bytes,config).CaptureBytes(),"successful removal including children has exact matching geometry");
        }
        [Test] public void CapturedFinalBuildingDestructionPublishesGeometry()
        {
            string input=Environment.GetEnvironmentVariable("CAMPAIGN_FINAL_WORLD");if(string.IsNullOrEmpty(input))Assert.Ignore("Explicit authentic final-world diagnostic input required.");
            var config=Config();var bytes=System.IO.File.ReadAllBytes(input);
            var a=PlayableAuthorityTick.RestoreBytes(bytes,config);
            try
            {
                var domain=typeof(PlayableAuthorityTick).GetField("domain",F).GetValue(a);
                var table=(System.Collections.IDictionary)domain.GetType().GetField("buildings",F).GetValue(domain);
                var buildings=table.Values.Cast<object>().Where(b=>(PlayableOwner)b.GetType().GetField("Owner",F|BindingFlags.Public).GetValue(b)==PlayableOwner.Enemy).Select(b=>(int)b.GetType().GetField("Id",F|BindingFlags.Public).GetValue(b)).ToArray();
                Assert.Greater(buildings.Length,0,"authentic captured final world retains required targets");
                foreach(var b in buildings)domain.GetType().GetMethod("Damage",F).Invoke(domain,new object[]{b,100000});
                var beforeRefresh=a.CaptureBytes();domain.GetType().GetMethod("RefreshVision",F).Invoke(domain,null);
                var cold=a.CaptureBytes();
                using(var before=PlayableWorldState.Decode(beforeRefresh))using(var after=PlayableWorldState.Decode(cold))
                {
                    foreach(string section in new[]{"Domain","Navigation","AiAuthority","RuntimeTransport","Binding","Participants"})
                        CollectionAssert.AreEqual((byte[])typeof(PlayableWorldState).GetField(section).GetValue(before),(byte[])typeof(PlayableWorldState).GetField(section).GetValue(after),"RefreshVision changes no "+section+" state");
                    TestContext.WriteLine("Final-world capture after direct Damage requires the ordinary RefreshVision boundary before exact cold restore.");
                }
                var restored=PlayableAuthorityTick.RestoreBytes(cold,config);
                try{CollectionAssert.AreEqual(cold,restored.CaptureBytes(),"real destruction publishes matching world/navigation geometry");}finally{restored.Stop();}
            }finally{a.Stop();}
        }
        [Test] public void AssaultLoadoutIsLocalAndHistoricalConfigurationRemainsDistinct()
        {
            var c=Config();var old=CampaignReconOpening.Configuration(c.Profile,false);
            Assert.AreEqual(18,c.ScenarioUnits.Count);
            for(int i=0;i<c.ScenarioUnits.Count;i++){
                Assert.AreEqual(i<8,c.ScenarioUnits[i].Upgraded,"authored loadout index="+i);
                Assert.False(old.ScenarioUnits[i].Upgraded);
                Assert.AreEqual(old.ScenarioUnits[i].Position,c.ScenarioUnits[i].Position);
                Assert.AreEqual(old.ScenarioUnits[i].Kind,c.ScenarioUnits[i].Kind);
                Assert.AreEqual(old.ScenarioUnits[i].LogicalPlayer,c.ScenarioUnits[i].LogicalPlayer);
            }
            var wire=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.OfflineConfigurationWire").GetMethod("Binding",BindingFlags.Static|BindingFlags.NonPublic);
            var historical=(byte[])wire.Invoke(null,new object[]{old,null,false,false});var current=(byte[])wire.Invoke(null,new object[]{c,null,false,false});
            Assert.AreEqual(historical.Length,current.Length);int changed=0;
            for(int i=0;i<historical.Length;i++)if(historical[i]!=current[i]){Assert.AreEqual(0,historical[i]);Assert.AreEqual(1,current[i]);changed++;}
            Assert.AreEqual(8,changed,"only existing eight authored upgrade bits change; profile/schema/enemies remain exact");
            var a=new OfflineParticipantAuthority(old,114);var bytes=a.CaptureBytes();
            CollectionAssert.AreEqual(bytes,OfflineParticipantAuthority.Restore(bytes,old).CaptureBytes());
            Assert.Throws<ArgumentException>(()=>OfflineParticipantAuthority.Restore(bytes,c),"never reinterpret historical checkpoint as new loadout");
        }
        [TestCase(true)] [TestCase(false)] public void BothOrdersGateFourNewUnitsLossesAndExactSecondState(bool westFirst)
        {
            var config=Config();var a=new OfflineParticipantAuthority(config,111);Opening(a);
            var before=a.View("player-1").Entities.Where(e=>e.Owner==PlayableOwner.Player).ToArray();Damage(a,before.Last().Id);Damage(a,before.First().Id,7);
            Clear(a,westFirst?1:2);var m=a.View("player-1").Mission;Assert.AreEqual(westFirst,m.WestCleared);Assert.AreEqual(!westFirst,m.EastCleared);Assert.False(m.GateOpen);
            Assert.False(a.NavigationBinding.Geometry.IsFree(new NavPoint((CampaignReconOpening.Gate.MinX+CampaignReconOpening.Gate.MaxX)/2,(CampaignReconOpening.Gate.MinZ+CampaignReconOpening.Gate.MaxZ)/2),config.Profile.ExplorerCollisionRadius));
            Assert.AreEqual(0,a.View("player-1").Entities.Count(e=>e.Owner==PlayableOwner.Third));
            Clear(a,westFirst?2:1);m=a.View("player-1").Mission;Assert.True(m.GateOpen);Assert.True(a.NavigationBinding.Geometry.IsFree(new NavPoint((CampaignReconOpening.Gate.MinX+CampaignReconOpening.Gate.MaxX)/2,(CampaignReconOpening.Gate.MinZ+CampaignReconOpening.Gate.MaxZ)/2),config.Profile.ExplorerCollisionRadius));
            Assert.AreEqual(4,a.View("player-1").Entities.Count(e=>e.Owner==PlayableOwner.Third));
            Place(a,Player(a),CampaignReconOpening.Convergence);a.Step(1d/30);Assert.True(a.View("player-1").Mission.FinalReinforced);
            Assert.AreEqual(7,a.View("player-1").Entities.Count(e=>e.Owner==PlayableOwner.Player),"four new units; previous loss remains");
            Assert.True(a.View("player-1").Entities.Where(e=>e.Owner==PlayableOwner.Player).All(e=>e.Upgraded),"both transferred reserves retain authored assault equipment");
            var health=a.View("player-1").Entities.Where(e=>e.Owner==PlayableOwner.Player).ToDictionary(e=>e.Id,e=>e.ExactHealth);
            Assert.Less(a.View("player-1").Entities.Single(e=>e.Id==before.First().Id).Health,before.First().Health);
            var bytes=a.CaptureBytes();var restored=OfflineParticipantAuthority.Restore(bytes,config);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());
            foreach(var e in restored.View("player-1").Entities.Where(e=>e.Owner==PlayableOwner.Player)){Assert.True(e.Upgraded);Assert.AreEqual(health[e.Id],e.ExactHealth);}
            for(int i=0;i<3;i++)restored.Step(1d/30);Assert.AreEqual(7,restored.View("player-1").Entities.Count(e=>e.Owner==PlayableOwner.Player));Assert.True(restored.View("player-1").Mission.FinalReinforced);
            Clear(restored,0);Clear(restored,3);Assert.AreEqual(PlayableMatchOutcome.PlayerWon,restored.View("player-1").Outcome);Assert.True(restored.View("player-1").Mission.Victory);
            var won=restored.CaptureBytes();CollectionAssert.AreEqual(won,OfflineParticipantAuthority.Restore(won,config).CaptureBytes());
        }
        [TestCase(true)] [TestCase(false)] public void ActualDamageActivatesWholeLocalGroupAndSurvivesCapture(bool building)
        {
            var config=Config();var a=new OfflineParticipantAuthority(config,112);int player=Player(a);
            var enemy=a.View("corporation");int target=building?enemy.Buildings.Single(b=>b.SiteId==2).Id:enemy.Entities.First(e=>e.Owner==PlayableOwner.Enemy&&CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[1],e.Position)).Id;
            OfflineParticipantAuthorityTests.Call(a,"DamageWithOwner",target,1d,player,null);
            Assert.AreEqual(1,Guards(a)[1]);Assert.AreEqual(0,Guards(a)[0]);Assert.AreEqual(0,Guards(a)[2]);Assert.AreEqual(0,Guards(a)[3]);
            var bytes=a.CaptureBytes();var restored=OfflineParticipantAuthority.Restore(bytes,config);CollectionAssert.AreEqual(Guards(a),Guards(restored));CollectionAssert.AreEqual(bytes,restored.CaptureBytes());
        }
        [Test] public void IdleRosterHasNoProductionOrStrategicAiAndNoFutureObjectiveLeak()
        {
            var c=Config();Assert.AreEqual(18,c.ScenarioUnits.Count);Assert.AreEqual(9,c.ScenarioBuildings.Count);Assert.True(c.Roster.Where(r=>r.Control!=OfflineControl.Human).All(r=>r.Control==OfflineControl.Scenario));
            var a=new OfflineParticipantAuthority(c,113);var enemy=a.View("corporation");var homes=enemy.Entities.Where(e=>e.Owner==PlayableOwner.Enemy).ToDictionary(e=>e.Id,e=>e.Position);
            for(int i=0;i<30;i++)a.Step(1d/30);
            foreach(var u in a.View("corporation").Entities.Where(e=>e.Owner==PlayableOwner.Enemy)){Assert.AreEqual(homes[u.Id],u.Position);Assert.False(u.Moving);}
            Assert.AreEqual(1,a.View("player-1").Mission.Objectives.Count);Assert.False(a.View("player-1").Buildings.Any());Assert.False(a.View("player-1").Entities.Any(e=>e.Owner==PlayableOwner.Enemy));
            Assert.AreEqual(14,a.Navigation.Crowd.Units.Count,"ten guards plus initial one and waiting three; second reserve not spawned");
        }
    }
}
