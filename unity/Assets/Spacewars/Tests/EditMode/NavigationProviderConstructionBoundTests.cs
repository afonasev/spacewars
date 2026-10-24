using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationProviderConstructionBoundTests
    {
        static IPlayableTerrain Map(string name)
        {
            switch(name){case "crossings":return ThreeCrossingsMap.Default;case "foundry":return new FoundryMap(new FoundryProfileData());case "basalt":return new BasaltCrownMap();case "moss":return new MossHighlandMap();case "campaign":return new CampaignOpeningMap();default:throw new ArgumentException(name);}
        }
        [TestCase("crossings",false)][TestCase("crossings",true)]
        [TestCase("foundry",false)][TestCase("foundry",true)]
        [TestCase("basalt",false)][TestCase("basalt",true)]
        [TestCase("moss",false)][TestCase("moss",true)]
        [TestCase("campaign",false)][TestCase("campaign",true)]
        public void PreconstructionBoundCoversRawProviderWithMovementOverlays(string name,bool extra)
        {
            var map=Map(name);var obstacles=map.MovementBlockers.ToArray();if(extra)obstacles=obstacles.Concat(new[]{new NavObstacle(new NavPoint(0,0),.8),new NavObstacle(-3,-3,-2,-2)}).ToArray();
            var geometry=new NavGeometry(map.HalfExtent,obstacles,2);
            long bound=NavigationProviderConstructionBound.Estimate(map,geometry);
            var raw=new PartitionNavigationProvider(map,geometry,extra?"held-overlay":"none");
            Assert.GreaterOrEqual(bound,raw.EstimatedRetainedBytes,name+" preconstruction reserve must cover actual raw payload");
            Assert.GreaterOrEqual(bound,geometry.SpatialIndexRetainedBytes+((INavArrivalPointTerrain)map).ArrivalLocations.FootprintCacheRetainedBytes+((INavArrivalPointTerrain)map).ArrivalLocations.RegionIndexRetainedBytes);
        }
        [Test] public void ApplicabilityRejectsUnsupportedTerrainAndDifferentExtent()
        {
            Assert.Throws<ArgumentException>(()=>NavigationProviderConstructionBound.Estimate(new AiTestMap(),new NavGeometry(144,Array.Empty<NavObstacle>(),1)));
            var map=new CampaignOpeningMap();Assert.Throws<ArgumentException>(()=>NavigationProviderConstructionBound.Estimate(map,new NavGeometry(map.HalfExtent+1,map.MovementBlockers.ToArray(),1)));
        }
        static byte[] Bytes(NavigationSession session)
        {using(var capture=session.CaptureOwnedState())using(var bytes=WorldWire.PackOwned(w=>WorldWire.Write(w,capture),capture.CaptureBudget))return bytes.TransferPublic();}
        [Test] public void AuthoredOrdinaryIngressRefusalKeepsMailboxBytesAndOwnershipThenResumes()
        {
            var map=new CampaignOpeningMap();var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),1);var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation,terrain:map);var kernel=new CertifiedRouteKernel();
            var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
            try
            {
                session.Crowd.Add(1,CampaignReconOpening.Landing);Assert.True(session.Move(1,new NavPoint(0,-70)));
                kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Transfer;kernel.Pump(binding,4);
                Assert.AreEqual(CertifiedRouteKernel.PumpStage.Transfer,kernel.CapturePumpFrontier().Stage);Assert.AreEqual(1,session.Requests.Count);Assert.True(session.Requests.TryPeek(out var request));
                var before=Bytes(session);long physical=session.PhysicalHostBudget.PhysicalBytes;kernel.PauseAtPhysicalBoundary=null;session.PhysicalHostBudget.AllocationAdmission=_=>false;
                kernel.Pump(binding,4);Assert.True(session.Requests.TryPeek(out var same));Assert.AreSame(request,same);Assert.AreEqual(physical,session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);session.PhysicalHostBudget.AllocationAdmission=null;CollectionAssert.AreEqual(before,Bytes(session));
                for(int i=0;i<20000&&session.Answers.Count==0;i++)kernel.Pump(binding,4);
                Assert.AreEqual(1,session.Answers.Count,"ordinary ingress resumes with sufficient reserved construction bytes");
            }
            finally{session.PhysicalHostBudget.AllocationAdmission=null;kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
            Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
        }
        [Test] public void AuthoredArrivalProviderRefusalPrecedesConstructionAndKeepsAcceptedBytes()
        {
            var map=new CampaignOpeningMap();var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),1);var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation,terrain:map);
            try
            {
                session.Crowd.Add(1,CampaignReconOpening.Landing);using(session.CaptureOwnedState()){}
                var command=new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{1},new NavPoint(0,-70)).AsHuman();session.PrepareCommandGroup(command,1,0,0);Assert.True(session.MoveGroup(new[]{1},new[]{command.Target}));session.FinishCommandGroup(true,new[]{1});
                var before=Bytes(session);int constructions=0;session.BeforeArrivalProviderConstruction=()=>constructions++;long physical=session.PhysicalHostBudget.PhysicalBytes;
                session.PhysicalHostBudget.AllocationAdmission=_=>false;
                Assert.False(session.PreparePhysicalArrivalAttempt());Assert.Zero(constructions);Assert.AreEqual(1,session.AllocationCount);Assert.AreEqual(physical,session.PhysicalHostBudget.PhysicalBytes);session.PhysicalHostBudget.AllocationAdmission=null;CollectionAssert.AreEqual(before,Bytes(session));
                Assert.True(session.PreparePhysicalArrivalAttempt());Assert.AreEqual(1,constructions,"arrival provider constructed only after reservation succeeds");
            }
            finally{session.PhysicalHostBudget.AllocationAdmission=null;session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
            Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
        }
    }
}
