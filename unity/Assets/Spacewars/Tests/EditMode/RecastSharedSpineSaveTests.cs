using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode
{
    public sealed class RecastSharedSpineSaveTests
    {
        [Test] public void ReadyDecisionAndAuthoritativePhaseRestoreExactlyWithSharedOwnership()
        {
            var s=RecastSharedSpineTests.Ready(out var geometry);NavigationSession restored=null;
            try{
                s.Step(1d/30);var bytes=WorldWire.Pack(w=>WorldWire.Write(w,s.CaptureState()));
                var saved=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
                Assert.AreEqual(2,saved.RecastCoordinationVersion);
                restored=new NavigationSession(81,geometry,PlayableProfile.RecastGroupCorridorDefault.Navigation,null,PlayableProfile.RecastGroupCorridorDefault.AuthoredMap){UsesRecastGroundRoutes=true};
                restored.ConfigureMarch(4,PlayableProfile.RecastGroupCorridorDefault.Revision);restored.RestoreState(saved);
                CollectionAssert.AreEqual(bytes,WorldWire.Pack(w=>WorldWire.Write(w,restored.CaptureState())));
                var a=restored.InstalledExecutionFor(1).Corridor.RecastPlan;var b=restored.InstalledExecutionFor(2).Corridor.RecastPlan;
                Assert.AreSame(a.Spine,b.Spine);Assert.AreEqual(s.InstalledExecutionFor(1).Corridor.RecastPlan.Spine.Binding,a.Spine.Binding);
                foreach(int id in new[]{1,2})Assert.True(s.GroupFor(id).Members.Single(m=>m.Entity==id).Recast.Equals(restored.GroupFor(id).Members.Single(m=>m.Entity==id).Recast));
                s.Step(1d/30);restored.Step(1d/30);
                foreach(int id in new[]{1,2}){Assert.AreEqual(s.Crowd.Units.Single(u=>u.Id==id).Position,restored.Crowd.Units.Single(u=>u.Id==id).Position);Assert.True(s.GroupFor(id).Members.Single(m=>m.Entity==id).Recast.Equals(restored.GroupFor(id).Members.Single(m=>m.Entity==id).Recast));}
            }finally{restored?.ReleaseCommittedHostRoot();s.ReleaseCommittedHostRoot();}
        }
        [Test] public void PendingRestartPreservesInputsPriorityAndPositionsDuringPreparation()
        {
            var profile=PlayableProfile.RecastGroupCorridorDefault;var map=profile.AuthoredMap;var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var s=new NavigationSession(81,geometry,profile.Navigation,null,map){UsesRecastGroundRoutes=true};NavigationSession restored=null;
            try{
                s.ConfigureMarch(4,profile.Revision);s.Crowd.Add(1,new NavPoint(-31,-46),.72,2,4.5,canYield:true);
                Assert.True(s.MoveGroup(new[]{1},new[]{new NavPoint(-46,-46)}));
                var kernel=new CertifiedRouteKernel();try{kernel.Continue=()=>s.Requests.Count==0;var binding=new PlayableRouteBinding(geometry,profile,s.Admission,s.RoutePort);for(int i=0;i<200&&s.Requests.Count==0;i++)kernel.Pump(binding,8);}finally{kernel.Clear();}
                Assert.AreEqual(1,s.Requests.Count,"Reach actual pending query input after accepted slot preparation.");
                using var state=s.CaptureState();var bytes=WorldWire.Pack(w=>WorldWire.Write(w,state));
                using var saved=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
                Assert.AreEqual(1,saved.PendingRequestIndices.Length);Assert.AreEqual(2,saved.RecastCoordinationVersion);
                restored=new NavigationSession(81,geometry,profile.Navigation,null,map){UsesRecastGroundRoutes=true};restored.ConfigureMarch(4,profile.Revision);restored.RestoreState(saved);
                using var captured=restored.CaptureState();CollectionAssert.AreEqual(bytes,WorldWire.Pack(w=>WorldWire.Write(w,captured)));
                Assert.AreEqual(s.GroupFor(1).IssuedTick,restored.GroupFor(1).IssuedTick);Assert.AreEqual(s.GroupFor(1).CommandSequence,restored.GroupFor(1).CommandSequence);
                var position=restored.Crowd.Units.Single().Position;
                using(var host=new UnityHostRouteService()){
                    var binding=new PlayableRouteBinding(geometry,profile,restored.Admission,restored.RoutePort);
                    for(int i=0;i<200&&restored.AppliedResults<1;i++){host.Service(binding,8);restored.ApplyResults();}
                }
                Assert.AreEqual(1,restored.AppliedResults);Assert.AreEqual(position,restored.Crowd.Units.Single().Position,"Navigation preparation never advances physical motion/time.");
            }finally{restored?.ReleaseCommittedHostRoot();s.ReleaseCommittedHostRoot();}
        }
        [Test] public void StaleProfileKnowledgeClearanceAndGeometryBindingsAreRejected()
        {
            var s=RecastSharedSpineTests.Ready(out _);try{
                var route=s.InstalledExecutionFor(1).Corridor;var p=route.RecastPlan;
                p.ValidateBindings(route.GraphKey,route.ProviderBinding,route.ProfileBinding,route.HoldBinding,route.Radius,p.Spine.GeometryRevision);
                Assert.Throws<ArgumentException>(()=>p.ValidateBindings(new string('0',64),route.ProviderBinding,route.ProfileBinding,route.HoldBinding,route.Radius,p.Spine.GeometryRevision));
                Assert.Throws<ArgumentException>(()=>p.ValidateBindings(route.GraphKey,route.ProviderBinding,new string('0',64),route.HoldBinding,route.Radius,p.Spine.GeometryRevision));
                Assert.Throws<ArgumentException>(()=>p.ValidateBindings(route.GraphKey,route.ProviderBinding,route.ProfileBinding,"foreign-knowledge",route.Radius,p.Spine.GeometryRevision));
                Assert.Throws<ArgumentException>(()=>p.ValidateBindings(route.GraphKey,route.ProviderBinding,route.ProfileBinding,route.HoldBinding,route.Radius+.1,p.Spine.GeometryRevision));
                Assert.Throws<ArgumentException>(()=>p.ValidateBindings(route.GraphKey,route.ProviderBinding,route.ProfileBinding,route.HoldBinding,route.Radius,p.Spine.GeometryRevision+1));
            }finally{s.ReleaseCommittedHostRoot();}
        }
    }
}
