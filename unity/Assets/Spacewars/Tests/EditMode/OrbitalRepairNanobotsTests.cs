using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class OrbitalRepairNanobotsTests
    {
        [Test]
        public void DifferentFloorsAndActualAircraftAltitudeDetermineEligibility()
        {
            var map = new StackedNavigationFixture();
            var point = new NavPoint(0,0);
            var lower = new NavLocation(point,"lower");
            var upper = new NavLocation(point,"upper");
            Assert.True(OrbitalRepairNanobots.Contains(map,lower,5,point,lower,null));
            Assert.False(OrbitalRepairNanobots.Contains(map,lower,5,point,upper,null));
            Assert.False(OrbitalRepairNanobots.Contains(map,upper,5,point,lower,null));
            Assert.True(OrbitalRepairNanobots.Contains(map,lower,5,new NavPoint(3,0),null,4));
            Assert.False(OrbitalRepairNanobots.Contains(map,lower,5,new NavPoint(3,0),null,4.1));
            Assert.False(OrbitalRepairNanobots.Contains(map,upper,5,point,null,3));
        }

        [Test]
        public void OverlappingFieldsRestoreOneRateToCompletedBuilding()
        {
            var p = PlayableProfile.Default;
            var domain = new PlayableDomain(p, 1, false);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object Call(string name, params object[] args) => typeof(PlayableDomain).GetMethod(name, flags).Invoke(domain, args);
            PlayableSnapshot View() => domain.Snapshot(1, RuntimeStatus.Running, false,
                new PlayableRuntimeMetrics(0, 0, 0, 0, 0), null, 7);
            try
            {
                var hq = View().Buildings.Single(b => b.Id == 1);
                Call("Damage", hq.Id, 40);
                int wounded = View().Buildings.Single(b => b.Id == 1).Health;
                var center = new NavLocation(hq.Position, NavLocation.FlatSurface);
                Call("AddRepairField", PlayableOwner.Player, center, 10d, 5d, 5d);
                Call("AddRepairField", PlayableOwner.Player, center, 10d, 5d, 5d);
                Call("AdvanceRepairNanobots", 1d);
                Assert.AreEqual(wounded + 5, View().Buildings.Single(b => b.Id == hq.Id).Health);
            }
            finally
            {
                domain.Navigation.ReleaseCommittedHostRoot();
                domain.DestroyOwnedNavigation();
            }
        }

        [Test]
        public void VisibleFieldRestoresExactHealthAndCooldownThroughSave()
        {
            var map = new AiTestMap();
            var data = PlayableProfile.Default.CopyData();
            var profile = PlayableProfile.Create(data,map);
            var config = map.Configuration(profile,7,new[] {
                new OfflineParticipant("player-1",1,1,OfflineControl.Human),
                new OfflineParticipant("player-2",2,2,OfflineControl.Human)
            });
            var domain = new PlayableDomain(profile,1L,config);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(PlayableDomain);
            object Call(PlayableDomain target,string name,params object[] args) =>
                type.GetMethod(name,flags).Invoke(target,args);
            PlayableSnapshot View(PlayableDomain target) => (PlayableSnapshot)Call(target,"PlayerSnapshot",
                1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player);
            long sequence = 0;
            PlayableCommandStatus Send(PlayableCommandKind kind,int id,NavPoint point = default) =>
                (PlayableCommandStatus)Call(domain,"Apply",
                    new PlayableCommand(1,++sequence,"player-1",kind,null,point,targetId:id),null);
            const int ability = OrbitalAbilities.RepairNanobots;
            try
            {
                Call(domain,"CreditOrbital",(PlayableOwner?)PlayableOwner.Player,PlayableOwner.Enemy,
                    data.orbitalThreshold1 + data.orbitalThreshold2 + data.orbitalThreshold3);
                foreach (int choice in new[] {1,2,ability})
                    Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.ChooseOrbital,choice));
                var unit = View(domain).Entities.First(entity => entity.Owner == PlayableOwner.Player);
                Call(domain,"Damage",unit.Id,20);
                double Health(PlayableDomain target) => View(target).Entities.Single(entity => entity.Id == unit.Id).ExactHealth.Value;
                Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.UseOrbital,ability,new NavPoint(0,0)));
                Assert.Zero(View(domain).Orbital.CommonRemaining);
                Assert.IsFalse(View(domain).Sounds.Any(sound => sound.Kind == PlayableSoundKind.OrbitalNanobotsDeployed));
                double wounded = Health(domain);
                Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,ability,unit.Position));
                Assert.AreEqual(1,View(domain).Sounds.Count(sound => sound.Kind == PlayableSoundKind.OrbitalNanobotsDeployed));
                Assert.AreEqual(60,View(domain).Orbital.Abilities.Single(slot => slot.Id == ability).Remaining);
                Assert.AreEqual(10,View(domain).Orbital.CommonRemaining);
                Assert.IsNotEmpty(View(domain).Orbital.RepairRims);
                domain.Step(1d/30);
                Assert.AreEqual(wounded+5d/30,Health(domain),1e-9);
                CollectionAssert.Contains(View(domain).Orbital.RepairedUnits,unit.Id);
                var bytes = (byte[])Call(domain,"CaptureWorldBytes",7,config.SourceIdentity);
                var restored = OfflineParticipantAuthority.Restore(bytes,config);
                var restoredDomain = (PlayableDomain)typeof(OfflineParticipantAuthority).GetProperty("Domain",flags).GetValue(restored);
                try
                {
                    CollectionAssert.AreEqual(bytes,restored.CaptureBytes());
                    for (int i=0;i<30;i++)
                    {
                        domain.Step(1d/30);
                        restoredDomain.Step(1d/30);
                    }
                    Assert.AreEqual(wounded+31*5d/30,Health(domain),1e-9);
                    Assert.AreEqual(Health(domain),Health(restoredDomain));
                    Assert.AreEqual(View(domain).Orbital.CommonRemaining,View(restoredDomain).Orbital.CommonRemaining);
                }
                finally { restoredDomain.DestroyOwnedNavigation(); }
            }
            finally { domain.DestroyOwnedNavigation(); }
        }
    }
}
