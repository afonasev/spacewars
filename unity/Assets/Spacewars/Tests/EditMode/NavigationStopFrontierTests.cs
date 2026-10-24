using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationStopFrontierTests
    {
        [Test] public void ActualHostOnlyFrontierAdvanceDoesNotInvalidateIdleRemoval()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var s=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var kernel=new CertifiedRouteKernel();NavigationHostRoot newest=null;int hook=0,boundaries=0;
            PlayableRouteBinding Binding()=>new PlayableRouteBinding(geometry,PlayableProfile.Default,s.Admission,s.RoutePort);
            bool At(CertifiedRouteKernel.PumpStage stage){var root=s.PinCommittedHostRoot();try{return root.Frontier.Stage==stage;}finally{root.Release();}}
            try
            {
                s.Crowd.Add(1,new NavPoint(-5,-3));s.Crowd.Add(2,new NavPoint(-5,3));Assert.True(s.Move(1,new NavPoint(5,-3)));
                kernel.PauseAtPhysicalBoundary=()=>At(CertifiedRouteKernel.PumpStage.Retirement);kernel.Pump(Binding(),4);Assert.True(At(CertifiedRouteKernel.PumpStage.Retirement));
                s.AfterStopPreparationForQa=_=>{s.AfterStopPreparationForQa=null;hook++;kernel.PauseAtPhysicalBoundary=()=>++boundaries>1;kernel.Pump(Binding(),4);newest=s.PinCommittedHostRoot();Assert.AreEqual(CertifiedRouteKernel.PumpStage.ArrivalAdvance,newest.Frontier.Stage);};
                Assert.True(s.Remove(2));Assert.AreEqual(1,hook);Assert.AreEqual(2,boundaries);Assert.False(s.Crowd.TryGet(2,out _));Assert.True(s.Crowd.TryGet(1,out _));
                var current=s.PinCommittedHostRoot();try{Assert.AreSame(newest,current,"current frontier is retained rather than rewound by removal");}finally{current.Release();}
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(s),s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);
            }
            finally{newest?.Release();s.AfterStopPreparationForQa=null;kernel.PauseAtPhysicalBoundary=null;kernel.Clear();s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);}
        }
        static byte[] Bytes(NavigationSession s){using(var state=s.CaptureOwnedState())return WorldWire.Pack(w=>WorldWire.Write(w,state));}
        static void Destroy(NavigationSession s,CertifiedRouteKernel k)
        {s.AfterStopPreparationForQa=null;k.PauseAtPhysicalBoundary=null;k.Clear();s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);}
        [Test] public void RoutedMemberRemovalRetainsFrontierNeighborAndColdSuffix()
        {
            var g=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var s=new NavigationSession(1,g,PlayableProfile.Default.Navigation);var k=new CertifiedRouteKernel();NavigationHostRoot newest=null;int calls=0,boundaries=0;
            PlayableRouteBinding Binding()=>new PlayableRouteBinding(g,PlayableProfile.Default,s.Admission,s.RoutePort);
            bool At(CertifiedRouteKernel.PumpStage stage){var root=s.PinCommittedHostRoot();try{return root!=null&&root.Frontier.Stage==stage;}finally{root?.Release();}}
            try
            {
                s.Crowd.Add(1,new NavPoint(-5,-3));s.Crowd.Add(2,new NavPoint(-5,3));Assert.True(s.MoveGroup(new[]{1,2},new[]{new NavPoint(5,-3),new NavPoint(5,3)}));
                k.PauseAtPhysicalBoundary=()=>At(CertifiedRouteKernel.PumpStage.Retirement);k.Pump(Binding(),4);Assert.True(At(CertifiedRouteKernel.PumpStage.Retirement));
                s.AfterStopPreparationForQa=_=>{s.AfterStopPreparationForQa=null;calls++;k.PauseAtPhysicalBoundary=()=>++boundaries>1;k.Pump(Binding(),4);newest=s.PinCommittedHostRoot();Assert.AreEqual(CertifiedRouteKernel.PumpStage.ArrivalAdvance,newest.Frontier.Stage);};
                Assert.True(s.Remove(2));Assert.AreEqual(1,calls);Assert.AreEqual(2,boundaries);Assert.False(s.Crowd.TryGet(2,out _));Assert.True(s.Crowd.TryGet(1,out var neighbor));Assert.AreEqual(new NavPoint(-5,-3),neighbor.Position);Assert.AreEqual(1,s.GroupFor(1).Members.Length);Assert.AreEqual(1,s.GroupFor(1).Members.Single().Entity);
                var current=s.PinCommittedHostRoot();try{Assert.AreSame(newest,current);}finally{current.Release();}newest.Release();newest=null;
                k.PauseAtPhysicalBoundary=null;for(int n=0;n<20;n++){k.Pump(Binding(),4);s.Step(1d/30);}Assert.False(s.Crowd.TryGet(2,out _),"late route cannot resurrect removed member");
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(s),s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);
                var bytes=Bytes(s);var cold=new NavigationSession(1,g,PlayableProfile.Default.Navigation);var coldKernel=new CertifiedRouteKernel();
                try{cold.RestoreState(WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState));CollectionAssert.AreEqual(bytes,Bytes(cold));for(int n=0;n<30;n++){k.Pump(Binding(),4);s.Step(1d/30);coldKernel.Pump(new PlayableRouteBinding(g,PlayableProfile.Default,cold.Admission,cold.RoutePort),4);cold.Step(1d/30);}CollectionAssert.AreEqual(Bytes(s),Bytes(cold),"exact warm/cold suffix after routed member removal");Assert.False(cold.Crowd.TryGet(2,out _));Assert.True(cold.Crowd.TryGet(1,out _));}
                finally{Destroy(cold,coldKernel);}
            }
            finally{newest?.Release();Destroy(s,k);}
        }
        [Test] public void ActualNewIngressRejectsStaleRemovalWithoutLosingNewState()
        {
            var g=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var s=new NavigationSession(1,g,PlayableProfile.Default.Navigation);var k=new CertifiedRouteKernel();byte[] afterDrift=null;int calls=0;
            try
            {
                s.Crowd.Add(1,new NavPoint(-5,-3));s.Crowd.Add(2,new NavPoint(-5,3));Assert.True(s.MoveGroup(new[]{1,2},new[]{new NavPoint(5,-3),new NavPoint(5,3)}));
                s.AfterStopPreparationForQa=_=>{s.AfterStopPreparationForQa=null;calls++;Assert.True(s.Move(1,new NavPoint(7,-3)));afterDrift=Bytes(s);};
                Assert.Throws<InvalidOperationException>(()=>s.Remove(2));Assert.AreEqual(1,calls);CollectionAssert.AreEqual(afterDrift,Bytes(s),"stale removal cannot overwrite actual new ingress");Assert.True(s.Crowd.TryGet(2,out _));Assert.AreEqual(new NavPoint(7,-3),s.GroupFor(1).Members.Single(m=>m.Entity==1).Goal);
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(s),s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);
            }
            finally{Destroy(s,k);}
        }
    }
}
