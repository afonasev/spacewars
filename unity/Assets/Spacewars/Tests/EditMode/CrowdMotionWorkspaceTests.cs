using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CrowdMotionWorkspaceTests
    {
        private static NavigationSession Create()
        {
            var n=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),NavigationProfile.Default);n.Crowd.Add(1,new NavPoint(-3,0));Assert.True(n.Crowd.SetRoute(1,new NavPoint(3,0),new[]{new NavPoint(3,0)}));return n;
        }
        private static byte[] Bytes(NavigationSession n){using(var state=n.CaptureOwnedState())return WorldWire.Pack(w=>WorldWire.Write(w,state));}
        private static void Destroy(NavigationSession n){n.DestroyNavigationAuthority();Assert.Zero(n.PhysicalHostBudget.PhysicalBytes);Assert.Zero(n.PhysicalHostBudget.HeldBytes);Assert.Zero(n.PhysicalHostBudget.ActiveScopes);}
        [TestCase(false)][TestCase(true)] public void DirectOwnedAndReachableCompatibilityUseTheSamePaidArraysAndReleaseTheirLease(bool direct)
        {
            var n=Create();try{
                var budget=n.PhysicalHostBudget;long physical=budget.PhysicalBytes,retained=budget.RetainedBytes;
                using(var workspace=((INavCrowdStorageSource)n).CreateMotionWorkspace(n.Crowd)){
                    Assert.AreEqual(1,workspace.Motion.Length);Assert.AreEqual(1,workspace.Proposals.Length);Assert.AreEqual(NavCrowdMotion.NeighborCapacity,workspace.Neighbors.Length);Assert.AreEqual(1,workspace.NeighborCounts.Length);Assert.AreEqual(1,workspace.ComponentLabels.Length);Assert.AreEqual(1,workspace.OriginalSpeeds.Length);
                    Assert.True(workspace.SourceCurrent(n.Crowd));Assert.GreaterOrEqual(budget.RetainedBytes-retained,NavStorageScratch.LeaseBytes+NavMotionWorkspace.HeaderBytes+NavMotionWorkspace.ArrayBytes(1));Assert.Greater(budget.ActiveScopes,0);
                }
                Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);int admissions=0;
                try{budget.AllocationAdmission=_=>{if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().Name=="CreateCompatibilityMotionWorkspace"))admissions++;return true;};if(direct)n.Crowd.Step(1d/30);else n.Step(1d/30);}finally{budget.AllocationAdmission=null;}
                Assert.Greater(admissions,0,"Reachable owned compatibility/direct entry must admit before motion");Assert.Greater(n.Crowd.Units[0].Position.X,-3);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(n),budget.PhysicalBytes);
            }finally{Destroy(n);}
        }
        [TestCase(false)][TestCase(true)] public void WorkspaceRefusalKeepsIndexCursorCounterAndExactSessionBeforeAnyDirectOrCompatibilityMutation(bool direct)
        {
            var n=Create();try{
                var prefix=Bytes(n);var spatial=n.Crowd.SpatialStorage;int neighbor=n.Crowd.NeighborCandidates;var budget=n.PhysicalHostBudget;long physical=budget.PhysicalBytes;int denied=0;
                try{budget.AllocationAdmission=_=>{if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().Name=="CreateCompatibilityMotionWorkspace"&&f.GetMethod().DeclaringType==typeof(NavigationSession))){denied++;return false;}return true;};if(direct)Assert.Throws<InvalidOperationException>(()=>n.Crowd.Step(1d/30));else n.Step(1d/30);}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied);Assert.AreSame(spatial,n.Crowd.SpatialStorage);Assert.AreEqual(neighbor,n.Crowd.NeighborCandidates);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(prefix,Bytes(n));
            }finally{Destroy(n);}
        }
        [Test] public void ChangedMembershipRefusesThePreparedWorkspaceWithoutResizingOrMoving()
        {
            var n=Create();try{using(var workspace=((INavCrowdStorageSource)n).CreateMotionWorkspace(n.Crowd)){
                n.Crowd.Add(2,new NavPoint(8,8));var prefix=Bytes(n);Assert.False(workspace.SourceCurrent(n.Crowd));Assert.AreEqual(1,workspace.Motion.Length);Assert.Throws<InvalidOperationException>(()=>n.Crowd.Step(1d/30,workspace));CollectionAssert.AreEqual(prefix,Bytes(n));
            }Assert.Zero(n.PhysicalHostBudget.HeldBytes);Assert.Zero(n.PhysicalHostBudget.ActiveScopes);}finally{Destroy(n);}
        }
    }
}
