using System;
using System.Collections.Generic;
namespace Spacewars.Simulation
{
    public sealed partial class NavTypedLeg
    {
        internal ILayeredNavigationProvider RecastAuthorityProvider=>CanonicalProvider(out _,out _,out _);
    }
    internal interface INavLayerSweepAuthority {bool TraceLayerSweep(ILayeredNavigationProvider provider,NavLocation from,NavPoint to,double radius,NavTypedRoute route,int cursor,INavStorageReservation r,out NavLocation reached,out IReadOnlyList<NavTypedLeg> trace);}
    internal interface INavRecastSweepAuthority { bool AllowsRecastSweep(int actor,NavLocation from,NavPoint to,double radius); }
    internal readonly struct NavRecastStepState
    {
        internal readonly int Phase,Cursor;internal readonly double Station,HighWater;
        internal NavRecastStepState(int phase,int cursor,double station,double high){Phase=phase;Cursor=cursor;Station=station;HighWater=high;}
    }
    internal struct NavRecastSweepReceipt
    {
        internal bool Verified,ProductionUnmapped;internal NavRecastStepState Before,After;
        internal NavPoint From,To,FirstCrossing,LastCrossing;
        internal string FromSurface,ToSurface,FirstSeam,LastSeam;
        internal int FirstPortalLeg,LastPortalLeg,PortalCount;
    }
    // Ordered correspondence within an accepted decision, not a new path search.
    // Semantic stages are separated by the accepted real seam IDs. Surface
    // sampling points are not mandatory rendezvous or crossing coordinates.
    internal static class NavRecastIntervals
    {
        internal static NavRecastStepState Initial(NavTypedRoute route)
        {var p=route.RecastPlan;double s=p.Spine.Station(p.Join);return new NavRecastStepState(0,0,s,s);}
        private static ILayeredNavigationProvider Provider(NavTypedRoute route)=>route.Legs[0].RecastAuthorityProvider;
        private static NavClearanceProfile Clearance(NavTypedRoute r)=>new NavClearanceProfile("ground",1,"ground",(int)r.Subscription.MobilityRevision,r.RecastPlan.Spine.Radius);
        private static int NextPortal(NavTypedRoute r,int cursor){for(int i=Math.Max(0,cursor);i<r.Legs.Count;i++)if(r.Legs[i].Kind==NavTypedLegKind.Portal)return i;return r.Legs.Count;}
        private static int PreviousPortal(NavTypedRoute r,int cursor){for(int i=Math.Min(cursor-1,r.Legs.Count-1);i>=0;i--)if(r.Legs[i].Kind==NavTypedLegKind.Portal)return i;return -1;}
        private static bool SameDirection(NavTypedLeg accepted,NavTypedLeg actual,bool forward)=>accepted.PortalId==actual.PortalId&&actual.From.SurfaceId==(forward?accepted.From:accepted.To).SurfaceId&&actual.To.SurfaceId==(forward?accepted.To:accepted.From).SurfaceId;
        private static bool Trace(NavTypedRoute route,NavRecastStepState before,NavLocation from,NavPoint to,INavStorageReservation reservation,out NavLocation reached,out int stage,out NavRecastSweepReceipt receipt,INavRecastSweepAuthority authority=null,int actor=0)
        {
            reached=default;stage=before.Cursor;receipt=new NavRecastSweepReceipt{Before=before,From=from.Position,To=to,FromSurface=from.SurfaceId,FirstPortalLeg=-1,LastPortalLeg=-1};
            if(authority!=null&&!authority.AllowsRecastSweep(actor,from,to,route.RecastPlan.Spine.Radius))return false;
            var provider=Provider(route);if(provider==null||stage<0||stage>=route.Legs.Count||route.Legs[stage].From.SurfaceId!=from.SurfaceId)return false;
            IReadOnlyList<NavTypedLeg> trace;
            if(provider is PartitionNavigationProvider partition){if(!partition.TryTraceOwned(from,to,Clearance(route),out reached,out trace,reservation))return false;}
            else if(!(authority is INavLayerSweepAuthority layered)||!layered.TraceLayerSweep(provider,from,to,route.RecastPlan.Spine.Radius,route,before.Cursor,reservation,out reached,out trace))return false;
            try{
                foreach(var leg in trace)if(leg.Kind==NavTypedLegKind.Portal){
                    int next=NextPortal(route,stage),previous=PreviousPortal(route,stage),matched;
                    if(next<route.Legs.Count&&SameDirection(route.Legs[next],leg,true)){matched=next;stage=Math.Min(route.Legs.Count-1,next+1);}
                    else if(previous>=0&&SameDirection(route.Legs[previous],leg,false)){matched=previous;stage=Math.Max(0,previous-1);}
                    else return false;
                    if(receipt.PortalCount==0){receipt.FirstPortalLeg=matched;receipt.FirstSeam=leg.PortalId;receipt.FirstCrossing=leg.From.Position;}
                    receipt.PortalCount++;receipt.LastPortalLeg=matched;receipt.LastSeam=leg.PortalId;receipt.LastCrossing=leg.From.Position;
                }
                receipt.ToSurface=reached.SurfaceId;receipt.Verified=true;return true;
            }finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
        }
        private static bool Connected(NavTypedRoute route,NavLocation from,NavLocation target,INavStorageReservation r,INavRecastSweepAuthority authority,int actor)
        {
            if(authority!=null&&!authority.AllowsRecastSweep(actor,from,target.Position,route.RecastPlan.Spine.Radius))return false;
            if(from.Equals(target))return true;var provider=Provider(route);
            if(!(provider is PartitionNavigationProvider partition))return provider.CanSweep(from,target,Clearance(route));
            if(!partition.TryTraceOwned(from,target.Position,Clearance(route),out var reached,out var trace,r))return false;
            try{if(!reached.Equals(target))return false;foreach(var l in trace)if(l.Kind==NavTypedLegKind.Portal)return false;return true;}
            finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
        }
        internal static bool InTerminalFan(NavTypedRoute route,NavLocation location)
        {
            var p=route.RecastPlan;var s=p.Spine;
            if(location.SurfaceId!=s.Node(p.Exit).SurfaceId)return false;
            // The analytically constructed fan boundary itself is an accepted
            // closed point even if its squared-distance computation rounds out.
            if(location.Equals(s.Node(s.FanNode)))return true;
            double x=location.Position.X-s.FanCenter.X,z=location.Position.Z-s.FanCenter.Z;
            return x*x+z*z<=s.FanRadius*s.FanRadius;
        }
        internal static int FirstTargetSurface(NavTypedRoute route,NavRecastStepState state)
        {int first=PreviousPortal(route,state.Cursor)+1,count=0;for(int i=0;i<first;i++)if(route.Legs[i].Kind==NavTypedLegKind.Surface)count++;return count;}
        internal static bool CanTarget(NavTypedRoute route,NavRecastStepState state,NavLocation from,int surfaceIndex,INavStorageReservation r,INavRecastSweepAuthority authority=null,int actor=0)
        {
            int typed=-1,seen=0;for(int i=0;i<route.Legs.Count;i++)if(route.Legs[i].Kind==NavTypedLegKind.Surface&&seen++==surfaceIndex){typed=i;break;}
            if(typed<0){if(surfaceIndex!=seen)return false;typed=route.Legs.Count-1;}
            // Targets may advance only through the accepted interval. A consumed
            // seam cannot make an earlier bank target the next own destination.
            int start=PreviousPortal(route,state.Cursor)+1;if(typed<start)return false;
            if(typed>=route.RecastPlan.TerminalLeg&&state.Phase!=2&&!InTerminalFan(route,from))return false;
            if(!Trace(route,state,from,route.Legs[typed].To.Position,r,out var reached,out _,out _,authority,actor))return false;
            return reached.Equals(route.Legs[typed].To);
        }
        internal static bool Advance(NavTypedRoute route,NavRecastStepState before,NavLocation from,NavPoint to,INavStorageReservation r,out NavRecastSweepReceipt receipt,INavRecastSweepAuthority authority=null,int actor=0)
        {
            receipt=default;
            if(!Trace(route,before,from,to,r,out var reached,out int cursor,out receipt,authority,actor))return false;
            var p=route.RecastPlan;int lo=PreviousPortal(route,cursor)+1,hi=NextPortal(route,cursor);
            double station=p.Spine.Station(p.Join);int phase=0,chosen=cursor;
            // The final fan is derived once from accepted slots and footprints.
            // Entering it still needs a safe own accepted terminal rejoin.
            bool terminal=false;
            if(InTerminalFan(route,reached)){
                if(Trace(route,new NavRecastStepState(before.Phase,cursor,before.Station,before.HighWater),reached,route.Subscription.AssignedEndpoint.Position,r,out var terminalEnd,out _,out _,authority,actor)&&terminalEnd.Equals(route.Subscription.AssignedEndpoint))terminal=true;
            }
            if(terminal){phase=2;station=p.Spine.Station(p.Exit);chosen=Math.Max(p.TerminalLeg,lo);}
            else{
                double best=double.PositiveInfinity;bool found=false;
                for(int i=Math.Max(lo,p.OriginLegs);i<Math.Min(hi,p.TerminalLeg);i++){
                    var leg=route.Legs[i];if(leg.Kind!=NavTypedLegKind.Surface||leg.From.SurfaceId!=reached.SurfaceId)continue;
                    double dx=leg.To.Position.X-leg.From.Position.X,dz=leg.To.Position.Z-leg.From.Position.Z,len2=dx*dx+dz*dz;
                    if(len2==0&&!reached.Equals(leg.From))continue;
                    double fraction=len2==0?0:((to.X-leg.From.Position.X)*dx+(to.Z-leg.From.Position.Z)*dz)/len2;
                    // First physical admission uses an actual interval cross-section,
                    // with full authority connection, not a distance tube or XZ alone.
                    if(before.Phase==0&&(fraction<0||fraction>1))continue;
                    fraction=Math.Max(0,Math.Min(1,fraction));var point=new NavPoint(leg.From.Position.X+fraction*dx,leg.From.Position.Z+fraction*dz);
                    double x=to.X-point.X,z=to.Z-point.Z,distance=x*x+z*z;if(distance>=best||!Connected(route,reached,new NavLocation(point,leg.From.SurfaceId),r,authority,actor))continue;
                    best=distance;chosen=i;found=true;station=p.Spine.Station(p.Join+i-p.OriginLegs)+fraction*Math.Sqrt(len2);
                }
                if(found)phase=1;else{chosen=cursor;phase=cursor>=p.OriginLegs?1:0;station=phase==0?p.Spine.Station(p.Join):p.Spine.Station(Math.Min(p.Exit,p.Join+Math.Max(0,lo-p.OriginLegs)));}
            }
            receipt.After=new NavRecastStepState(phase,chosen,station,Math.Max(before.HighWater,station));return true;
        }
    }
}
