using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.PlayMode
{
    public sealed class OrbitalRepairNanobotsThreadedTests
    {
        [Test, Timeout(60000)]
        public void BotAppliesRepairThroughOrdinaryThreadedAuthority()
        {
            var map = new AiTestMap();
            var data = PlayableProfile.Default.CopyData();
            var profile = PlayableProfile.Create(data,map);
            var config = map.Configuration(profile,7,new[] {
                new OfflineParticipant("player-1",1,1,OfflineControl.Ai),
                new OfflineParticipant("player-2",2,2,OfflineControl.Human)
            });
            var authority = new PlayableAuthorityTick(config,907,difficulty:AiDifficulty.Veteran);
            var domain = authority.Domain;
            CertifiedRouteWorker worker = null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object Call(string name,params object[] args) => typeof(PlayableDomain).GetMethod(name,flags).Invoke(domain,args);
            try
            {
                const int repair = OrbitalAbilities.RepairNanobots;
                var state = (OrbitalEscalationState)Call("Orbital",PlayableOwner.Player);
                state.Damage(data.orbitalThreshold1 + data.orbitalThreshold2 + data.orbitalThreshold3,data);
                foreach (int ability in new[] { 1,2,repair }) Assert.True(state.Choose(ability));
                var staging = new NavPoint(-6,-90);
                var ids = new int[3];
                for (int i=0;i<ids.Length;i++)
                {
                    ids[i] = (int)Call("SpawnUnit",new NavPoint(staging.X+2*i,staging.Z),PlayableOwner.Player,PlayableEntityKind.Tank);
                    Call("Damage",ids[i],25);
                }
                worker = new CertifiedRouteWorker(authority.NavigationBinding,profile.NavigationRequestsPerFrame);
                var timeout = Stopwatch.StartNew();
                bool applied = false;
                while (domain.Tick < 360 && timeout.Elapsed.TotalSeconds < 45)
                {
                    if (!authority.TryAdvance()) { Thread.Sleep(1); continue; }
                    Assert.IsNull(worker.Publication.Failure);
                    var view = authority.Latest;
                    if (view.Orbital.Abilities.Any(ability=>ability.Id==repair && ability.Remaining>0))
                    {
                        applied = true;
                        Assert.That(view.Orbital.RepairedUnits.Intersect(ids).Count(),Is.EqualTo(3));
                        break;
                    }
                }
                Assert.True(applied,"The normal AI scheduler should cast on three wounded allies.");
            }
            finally
            {
                worker?.Dispose();
                if (worker != null) Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,10000));
                authority.Stop();
                domain.Navigation.ReleaseCommittedHostRoot();
                domain.DestroyOwnedNavigation();
            }
        }
    }
}
