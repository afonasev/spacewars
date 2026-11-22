using System;
using System.Linq;
using System.Reflection;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    internal static class RecastMovedReadyFixture
    {
        internal static void Run(PlayableProfile profile)
        {
        var map=profile.AuthoredMap;var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
        var session=new NavigationSession(1,geometry,profile.Navigation,null,map){UsesRecastGroundRoutes=true};
        var kernel=new CertifiedRouteKernel();ulong native=RecastNative.sw_recast_allocation_count();
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
        void AwaitAnswer(){for(int i=0;i<20000&&session.Answers.Count==0;i++)kernel.Pump(binding,64);Check(session.Answers.Count==1,"one native answer required");}
        try{
            session.ConfigureMarch(profile.GroupMarchMaximumStretch,profile.Revision);
            var actor=session.Crowd.Add(1,new NavPoint(-30.5,45.58),profile.ExplorerCollisionRadius,4,4.5,canYield:true);
            Check(session.MoveGroup(new[]{1},new[]{new NavPoint(-28,45.58)}),"original movement refused");AwaitAnswer();session.ApplyResults();Check(actor.Moving,"old motion missing");
            var start=actor.Location;var goal=new NavPoint(-23.674625,29.405675000000002);
            Check(session.MoveGroup(new[]{1},new[]{goal}),"replacement refused");AwaitAnswer();
            Check(session.Answers.TryDequeue(out var answer),"answer unavailable");
            ((NavigationTransportOwner)typeof(NavigationSession).GetField("transportOwner",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session)).Consume(answer.Request);
            Check(answer.Status==NavSolveStatus.Ready,"replacement route was not ready");
            for(int i=0;i<120&&actor.Location.SurfaceId==start.SurfaceId;i++)session.Step(1d/30);
            Console.WriteLine("MOVED_TYPED_READY start="+start.SurfaceId+" current="+actor.Location.SurfaceId+" pos="+actor.Position.X+","+actor.Position.Z+" typedOrigin="+answer.StorageOwner.CorridorOwner.TypedRoute.Subscription.Origin.SurfaceId);
            Check(actor.Location.SurfaceId!=start.SurfaceId,"ordinary old-route movement must cross actual support before installing replacement");
            Check(session.Answers.TryEnqueue(answer),"answer restore refused");
            session.Step(1d/30);
            Check(session.PendingRequestFor(1)!=null,"moved typed origin must be reissued rather than installing an unbound cursor");
            Check(session.PendingRequestFor(1).StartLocation.Value.SurfaceId==actor.Location.SurfaceId,"replacement must capture current support");
            for(int i=0;i<2000&&actor.Outcome!=NavigationOutcome.Arrived;i++){kernel.Pump(binding,64);session.Step(1d/30);}
            Check(actor.Outcome==NavigationOutcome.Arrived,"same original intent must arrive after owned reissue");
            Check(actor.Goal.Equals(goal),"reissue changed the commanded destination");
        }finally{kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Check(session.PhysicalHostBudget.PhysicalBytes==0&&session.PhysicalHostBudget.HeldBytes==0,"owned source leaked");Check(RecastNative.sw_recast_allocation_count()==native,"native map leaked");}
        }
    }
}
