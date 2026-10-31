using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed class ScienceConstructionThreadedTests
    {
        [Test,Timeout(60000)] public void SeparateCentersResearchConcurrentlyThroughAuthorityHost()
        {
            var p=PlayableProfile.Default;var domain=new PlayableDomain(p,813,false);CertifiedRouteWorker worker=null;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(domain,args);
            PlayableSnapshot View()=>domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            try
            {
                Call("AddCredits",PlayableOwner.Player,3000d);
                int Build(PlayableBuildingKind kind,int slot){Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,slot,kind,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);return View().Buildings.Single(b=>b.SlotId==slot).Id;}
                Build(PlayableBuildingKind.Factory,5);int a=Build(PlayableBuildingKind.ScientificCenter,1),b=Build(PlayableBuildingKind.ScientificCenter,2);
                var authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(813,1,"player-1",PlayableCommandKind.QueueResearch,new[]{a},researchKind:PlayableResearchKind.TankChassis)).Status);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(813,2,"player-1",PlayableCommandKind.QueueResearch,new[]{b},researchKind:PlayableResearchKind.ShkvalGuidance)).Status);
                Assert.AreEqual(2,View().OwnerResearch.Count(o=>o.Active));
                bool concurrent=false,complete=false;var clock=Stopwatch.StartNew();
                while(domain.Tick<1200&&clock.Elapsed.TotalSeconds<45)
                {
                    authority.TryAdvance();Assert.IsNull(worker.Publication.Failure);var view=authority.Latest;
                    if(view==null){Thread.Sleep(1);continue;}
                    concurrent|=view.OwnerResearch.Count(o=>o.Active&&o.Elapsed>0)==2;
                    complete=view.OwnerResearch.Count(o=>o.Complete)==2;if(complete)break;Thread.Sleep(1);
                }
                TestContext.WriteLine($"PARALLEL_RESEARCH tick={domain.Tick} concurrent={concurrent} complete={complete} wall={clock.Elapsed.TotalSeconds:R}");
                Assert.True(concurrent&&complete);Assert.True(domain.Navigation.HasCertifiedHost);
            }
            finally{worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}
        }
        [TestCase(PlayableBuildingKind.Factory)] [TestCase(PlayableBuildingKind.Hangar)] [Timeout(60000)]
        public void ProducerCompletionUnlocksScienceThroughOrdinaryHost(PlayableBuildingKind producer)
        {
            var p=PlayableProfile.Default;var domain=new PlayableDomain(p,813,false);CertifiedRouteWorker worker=null;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(domain,args);
            PlayableSnapshot View()=>domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            try
            {
                Call("AddCredits",PlayableOwner.Player,1000d);
                var authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);long sequence=0;
                PlayableCommand Build(PlayableBuildingKind kind,int slot)=>new PlayableCommand(813,++sequence,"player-1",PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:1,slotId:slot,buildingKind:kind,parentId:1);
                double bank=(double)Call("Balance",PlayableOwner.Player);
                Assert.AreEqual(PlayableCommandStatus.Rejected,authority.Apply(Build(PlayableBuildingKind.ScientificCenter,3)).Status);
                Assert.AreEqual(bank,Call("Balance",PlayableOwner.Player));Assert.False(View().Buildings.Any(b=>b.Kind==PlayableBuildingKind.ScientificCenter));
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(Build(producer,1)).Status);
                bank=(double)Call("Balance",PlayableOwner.Player);
                Assert.AreEqual(PlayableCommandStatus.Rejected,authority.Apply(Build(PlayableBuildingKind.ScientificCenter,3)).Status);
                Assert.AreEqual(bank,Call("Balance",PlayableOwner.Player));
                var friendly=View().Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(813,++sequence,"player-1",PlayableCommandKind.Move,new[]{friendly.Id},new NavPoint(-11,-8))).Status);
                bool ordered=false,ready=false,moved=false;var clock=Stopwatch.StartNew();
                while(domain.Tick<1800&&clock.Elapsed.TotalSeconds<50)
                {
                    authority.TryAdvance();Assert.IsNull(worker.Publication.Failure);var view=authority.Latest;
                    if(view==null){Thread.Sleep(1);continue;}
                    Assert.IsNull(view.Failure);moved|=view.Entities.Any(e=>e.Id==friendly.Id&&!e.Position.Equals(friendly.Position));
                    if(!ordered&&view.Buildings.Any(b=>b.Owner==PlayableOwner.Player&&b.Kind==producer&&b.Phase==ConstructionPhase.Ready))
                    {
                        Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(Build(PlayableBuildingKind.ScientificCenter,3)).Status);ordered=true;
                    }
                    ready|=view.Buildings.Any(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.Phase==ConstructionPhase.Ready);
                    if(ready&&moved&&worker.Publication.Pumps>0)break;Thread.Sleep(1);
                }
                TestContext.WriteLine($"SCIENCE_THREADED producer={producer} seed=7 tick={domain.Tick} ordered={ordered} ready={ready} moved={moved} wall={clock.Elapsed.TotalSeconds:R}");
                Assert.True(ordered&&ready,"A completed producer unlocks an actual paid science command and construction");
                Assert.True(moved);Assert.True(domain.Navigation.HasCertifiedHost);Assert.Greater(worker.Publication.Pumps,0);
            }
            finally
            {
                worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));
                domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();
                Assert.Zero(domain.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(domain.Navigation.PhysicalHostBudget.HeldBytes);
            }
        }
    }
}
