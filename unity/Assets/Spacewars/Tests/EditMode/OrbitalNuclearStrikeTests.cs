using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class OrbitalNuclearStrikeTests
    {
        [Test]
        public void VisibleStrikeSpendsCooldownSavesDuringDescentAndDamagesExactlyOnce()
        {
            var map=new AiTestMap();
            var terms=PlayableProfile.Default.CopyData();
            var profile=PlayableProfile.Create(terms,map);
            var config=map.Configuration(profile,7,new[]
            {
                new OfflineParticipant("player-1",1,1,OfflineControl.Human),
                new OfflineParticipant("player-2",2,2,OfflineControl.Human)
            });
            var domain=new PlayableDomain(profile,1,config);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var type=typeof(PlayableDomain);
            object Call(PlayableDomain instance,string method,params object[] args) => type.GetMethod(method,flags).Invoke(instance,args);
            PlayableSnapshot View(PlayableDomain instance,PlayableOwner owner=PlayableOwner.Player) =>
                (PlayableSnapshot)Call(instance,"PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
            long sequence=0;
            PlayableCommandStatus Send(PlayableCommandKind kind,int id,NavPoint point=default) =>
                (PlayableCommandStatus)Call(domain,"Apply",new PlayableCommand(1,++sequence,"player-1",kind,null,point,targetId:id),null);
            try
            {
                Call(domain,"CreditOrbital",(PlayableOwner?)PlayableOwner.Player,PlayableOwner.Enemy,
                    terms.orbitalThreshold1+terms.orbitalThreshold2+terms.orbitalThreshold3+terms.orbitalThreshold4+terms.orbitalThreshold5);
                foreach(int id in new[]{1,2,3,4,OrbitalAbilities.NuclearStrike})
                    Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.ChooseOrbital,id));
                var view=View(domain);
                var building=view.Buildings.First(candidate=>candidate.Owner==PlayableOwner.Player && candidate.Health>terms.orbitalNuclearDamage);
                Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.UseOrbital,OrbitalAbilities.NuclearStrike,new NavPoint(999,999)));
                Assert.Zero(View(domain).Orbital.CommonRemaining);
                Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,OrbitalAbilities.NuclearStrike,building.Position));
                Assert.AreEqual(90,View(domain).Orbital.Abilities.Last().Remaining);
                var warning=View(domain).Orbital.NuclearStrikes.Single();
                Assert.AreEqual(OrbitalNuclearPhase.Warning,warning.Phase);
                Assert.False(View(domain).Sounds.Any(fact=>fact.Kind==PlayableSoundKind.OrbitalNuclearDescent));
                var enemyView=View(domain,(PlayableOwner)1);
                Assert.False(enemyView.Orbital.NuclearStrikes.Single().AlliedSignal);
                Assert.IsNull(enemyView.Orbital.NuclearStrikes.Single().Center);
                Assert.AreEqual(default(NavPoint),enemyView.MatchEvents.Single(fact=>fact.Kind==MatchEventKind.NuclearLaunch).Point);
                for(int i=0;i<91;i++)domain.Step(1d/30);
                Assert.AreEqual(OrbitalNuclearPhase.Descent,View(domain).Orbital.NuclearStrikes.Single().Phase);
                var descent=View(domain).Sounds.Single(fact=>fact.Kind==PlayableSoundKind.OrbitalNuclearDescent);
                Assert.AreEqual(building.Position,descent.Position);
                Assert.False(View(domain,(PlayableOwner)1).Sounds.Any(fact=>fact.Kind==PlayableSoundKind.OrbitalNuclearDescent));
                var bytes=(byte[])Call(domain,"CaptureWorldBytes",7,config.SourceIdentity);
                var restored=OfflineParticipantAuthority.Restore(bytes,config);
                var restoredDomain=(PlayableDomain)typeof(OfflineParticipantAuthority).GetProperty("Domain",flags).GetValue(restored);
                try
                {
                    CollectionAssert.AreEqual(bytes,restored.CaptureBytes());
                    double Health(PlayableDomain instance)=>View(instance).Buildings.Single(candidate=>candidate.Id==building.Id).ExactHealth.Value;
                    double before=Health(domain);
                    for(int i=0;i<20;i++){domain.Step(1d/30);restoredDomain.Step(1d/30);}
                    Assert.AreEqual(before-terms.orbitalNuclearDamage,Health(domain),1e-9);
                    Assert.AreEqual(Health(domain),Health(restoredDomain),1e-9);
                    Assert.AreEqual(OrbitalNuclearPhase.Impact,View(domain).Orbital.NuclearStrikes.Single().Phase);
                    Assert.AreEqual(descent.PhaseEndTick,
                        View(domain).Sounds.Single(fact=>fact.Kind==PlayableSoundKind.OrbitalNuclearImpact).Tick);
                    Assert.False(View(restoredDomain).Sounds.Any(fact=>fact.Kind==PlayableSoundKind.OrbitalNuclearDescent));
                    for(int i=0;i<45;i++)domain.Step(1d/30);
                    Assert.IsEmpty(View(domain).Orbital.NuclearStrikes);
                    Assert.AreEqual(before-terms.orbitalNuclearDamage,Health(domain),1e-9);
                }
                finally { restoredDomain.DestroyOwnedNavigation(); }
            }
            finally { domain.DestroyOwnedNavigation(); }
        }

        [Test]
        public void BuildingOnlyStrikeUpdatesGeometryWithoutRemovingGroundActors()
        {
            var map = new AiTestMap();
            var terms = PlayableProfile.Default.CopyData();
            var profile = PlayableProfile.Create(terms, map);
            var roster = new[]
            {
                new OfflineParticipant("player-1", 1, 1, OfflineControl.Human),
                new OfflineParticipant("player-2", 2, 2, OfflineControl.Human)
            };
            var basis = map.Configuration(profile, 7, roster);
            var start = basis.Starts[0];
            var slot = basis.Sites.Single(site => site.Id == start.SiteId).Slots
                .OrderByDescending(s => AiOpponentSelector.Distance(s.Position, start.ExplorerAnchor)).First();
            var costs = new double[basis.Starts.Count,basis.Starts.Count];
            for (int a = 0; a < basis.Starts.Count; a++)
                for (int b = 0; b < basis.Starts.Count; b++)
                    costs[a,b] = basis.RouteCost(a,b);
            var config = new OfflineMatchConfiguration(profile, basis.SourceIdentity, basis.MapIdentity,
                basis.RouteProvenance, basis.Seed, roster, basis.Starts.ToArray(), basis.Sites.ToArray(),
                basis.Obstacles.ToArray(), costs, terrain:map.Id, scenarioBuildings:new[]
                {
                    new OfflineScenarioBuilding(1, PlayableBuildingKind.Factory, start.SiteId,
                        slot.Id, slot.Position, slot.Heading)
                });
            var domain = new PlayableDomain(profile, 1, config);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(PlayableDomain);
            object Call(string method, params object[] args) => type.GetMethod(method, flags).Invoke(domain, args);
            PlayableSnapshot View() => (PlayableSnapshot)Call("PlayerSnapshot", 1L, RuntimeStatus.Running, false,
                new PlayableRuntimeMetrics(0,0,0,0,0), null, 7, PlayableOwner.Player);
            long sequence = 0;
            PlayableCommandStatus Send(PlayableCommandKind kind, int id, NavPoint point = default) =>
                (PlayableCommandStatus)Call("Apply", new PlayableCommand(1, ++sequence, "player-1", kind,
                    null, point, targetId:id), null);

            try
            {
                var factory = View().Buildings.Single(b => b.Kind == PlayableBuildingKind.Factory);
                var ownActors = View().Entities.Where(u => u.Owner == PlayableOwner.Player).ToArray();
                Assert.IsNotEmpty(ownActors);
                Assert.That(ownActors.All(u => AiOpponentSelector.Distance(u.Position, factory.Position) >
                    terms.orbitalNuclearRadius + PlayableUnitRules.Radius(profile, u.Kind)), Is.True);
                Call("Damage", factory.Id, factory.Health - (int)terms.orbitalNuclearDamage);
                Assert.That(View().Buildings.Single(b => b.Id == factory.Id).Health,
                    Is.InRange(1, (int)terms.orbitalNuclearDamage));
                Call("CreditOrbital", (PlayableOwner?)PlayableOwner.Player, PlayableOwner.Enemy,
                    terms.orbitalThreshold1 + terms.orbitalThreshold2 + terms.orbitalThreshold3 +
                    terms.orbitalThreshold4 + terms.orbitalThreshold5);
                foreach (int id in new[] {1, 2, 3, 4, OrbitalAbilities.NuclearStrike})
                    Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.ChooseOrbital, id));

                int revision = domain.Geometry.Revision;
                Assert.AreEqual(PlayableCommandStatus.Applied,
                    Send(PlayableCommandKind.UseOrbital, OrbitalAbilities.NuclearStrike, factory.Position));
                for (int tick = 0; tick < 110; tick++)
                    domain.Step(1d/30);
                var after = View();
                Assert.AreEqual(OrbitalNuclearPhase.Impact, after.Orbital.NuclearStrikes.Single().Phase);
                Assert.False(after.Buildings.Any(b => b.Id == factory.Id));
                Assert.Greater(domain.Geometry.Revision, revision);
                CollectionAssert.AreEquivalent(ownActors.Select(u => u.Id),
                    after.Entities.Where(u => u.Owner == PlayableOwner.Player).Select(u => u.Id));
            }
            finally { domain.DestroyOwnedNavigation(); }
        }

        [TestCase(1,false)]
        [TestCase(3.25,true)]
        public void ScanExpiryCancelsOnlyBeforeDescent(double scanSeconds,bool commits)
        {
            var data=PlayableProfile.Default.CopyData();
            data.orbitalScanSeconds=scanSeconds;
            data.orbitalCommonSeconds=0;
            var map=new AiTestMap();
            var profile=PlayableProfile.Create(data,map);
            var config=map.Configuration(profile,7,new[]
            {
                new OfflineParticipant("player-1",1,1,OfflineControl.Human),
                new OfflineParticipant("player-2",2,2,OfflineControl.Human)
            });
            var domain=new PlayableDomain(profile,1,config);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var type=typeof(PlayableDomain);
            PlayableSnapshot View()=>(PlayableSnapshot)type.GetMethod("PlayerSnapshot",flags).Invoke(domain,
                new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player});
            var target=new NavPoint(0,0);
            long sequence=0;
            PlayableCommandStatus Send(PlayableCommandKind kind,int ability)=>(PlayableCommandStatus)type.GetMethod("Apply",flags).Invoke(domain,
                new object[]{new PlayableCommand(1,++sequence,"player-1",kind,null,target,targetId:ability),null});
            try
            {
                type.GetMethod("CreditOrbital",flags).Invoke(domain,new object[]{(PlayableOwner?)PlayableOwner.Player,PlayableOwner.Enemy,
                    data.orbitalThreshold1+data.orbitalThreshold2+data.orbitalThreshold3+data.orbitalThreshold4+data.orbitalThreshold5});
                foreach(int id in new[]{1,2,3,4,OrbitalAbilities.NuclearStrike})
                    Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.ChooseOrbital,id));
                Assert.False(View().Vision.IsVisible(target));
                Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,1));
                Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,OrbitalAbilities.NuclearStrike));
                for(int i=0;i<106;i++)domain.Step(1d/30);
                Assert.False(View().Vision.IsVisible(target));
                Assert.Greater(View().Orbital.Abilities.Single(slot=>slot.Id==OrbitalAbilities.NuclearStrike).Remaining,0);
                if(commits)Assert.AreEqual(OrbitalNuclearPhase.Impact,View().Orbital.NuclearStrikes.Single().Phase);
                else
                {
                    Assert.IsEmpty(View().Orbital.NuclearStrikes);
                    Assert.False(View().Sounds.Any(fact=>fact.Kind==PlayableSoundKind.OrbitalNuclearDescent));
                }
            }
            finally { domain.DestroyOwnedNavigation(); }
        }

        [Test]
        public void LaunchFocusUsesOnlyAnActiveAlliedSignal()
        {
            var profile=PlayableProfile.Default;
            var point=new NavLocation(new NavPoint(6,7),"test");
            PlayableSnapshot View(long tick,bool allied,bool active)
            {
                var view=new PlayableSnapshot("test",1,1,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,
                    Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),null,null);
                var strike=new OrbitalNuclearSnapshot(12,allied?point:(NavLocation?)null,allied,OrbitalNuclearPhase.Warning,6,0,Array.Empty<OrbitalRepairRim>());
                var state=new OrbitalEscalationSnapshot(0,0,250,0,0,Array.Empty<OrbitalAbilitySnapshot>(),Array.Empty<int>(),
                    nuclearStrikes:active?new[]{strike}:null);
                return view.WithOrbital(state).WithMatchEvents(
                    new[]{new PlayableMatchEvent(1,1,PlayableOwner.Player,MatchEventKind.NuclearLaunch,default,13)});
            }
            var enemy=new NativeMatchNotifications();
            enemy.Update(View(1,false,true),false,0,profile);
            Assert.AreEqual("Запущена ядерная ракета",enemy.Latest.Text);
            Assert.IsNull(enemy.Focus(false,0,profile));
            var ally=new NativeMatchNotifications();
            ally.Update(View(1,true,true),false,0,profile);
            Assert.AreEqual(point.Position,ally.Focus(false,0,profile));
            ally.Update(View(2,true,false),false,.1,profile);
            Assert.IsNull(ally.Focus(false,.1,profile));
        }
    }
}
