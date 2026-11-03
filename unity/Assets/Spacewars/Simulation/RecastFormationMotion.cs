using System;
namespace Spacewars.Simulation
{
    // One paid, frame-local bridge. A voluntary hold never changes the order,
    // route, clock, work credits, or physical collision proposal contract.
    internal struct NavFormationFrameHint
    {
        internal NavPoint Target;
        internal int TargetLeg;
        internal double ForwardLimit,DirectionalBankReach;
        internal byte Flags;
        internal const byte Guide=1,Wait=2,Bound=4,PolicyWait=8,StagingFeed=16,PreEntry=32,Directional=64;
    }
    internal static class RecastFormationMotion
    {
        // Borrowed immutable geometry for ONE Select or repair invocation. Fresh
        // endpoints, flags, role limits and receiver eligibility remain inputs.
        internal readonly struct PreparedDomain
        {
            private readonly NavTypedRoute route;
            private readonly int leg;
            private readonly byte flags;
            private readonly bool prepared,axis,bounds;
            private readonly NavPoint origin,direction;
            private readonly double length,low,high,radius;
            internal bool IsPrepared=>prepared;
            internal PreparedDomain(NavFormationFrameHint hint,NavTypedRoute route)
            {
                this=default;
                if(route==null||!route.IsImmutableCaptureRoot||hint.TargetLeg<0||hint.TargetLeg>=route.Legs.Count||
                    !(route.Legs[hint.TargetLeg].RecastAuthorityProvider is PartitionNavigationProvider provider)||provider.StorageRoot==null||!provider.StorageRoot.IsImmutableCaptureRoot)return;
                this.route=route;leg=hint.TargetLeg;flags=(byte)(hint.Flags&(NavFormationFrameHint.Bound|NavFormationFrameHint.Directional));prepared=true;
                axis=Axis(route,leg,out var a,out _,out var d,out double size);origin=a;direction=d;length=size;
                if(axis&&(flags&NavFormationFrameHint.Directional)!=0){bounds=Bounds(route,leg,a,d,out double l,out double h,out double r);low=l;high=h;radius=r;}
            }
            private bool Matches(NavFormationFrameHint hint,NavTypedRoute current)=>prepared&&ReferenceEquals(route,current)&&leg==hint.TargetLeg&&
                ((hint.Flags&(NavFormationFrameHint.Bound|NavFormationFrameHint.Directional))&~flags)==0;
            internal bool Allows(NavFormationFrameHint hint,NavTypedRoute current,NavPoint from,NavPoint to,bool receiver=false)
            {
                if(!Matches(hint,current))return RecastFormationMotion.Allows(hint,current,from,to,receiver);
                if(receiver)return true;
                if((hint.Flags&NavFormationFrameHint.Wait)!=0)return from.Equals(to);
                if(from.Equals(to))return true;
                if((hint.Flags&NavFormationFrameHint.Bound)!=0){
                    if(!axis)return false;
                    double before=Station(from,origin,direction),after=Station(to,origin,direction);
                    if(after>Math.Max(before,hint.ForwardLimit)+1e-9)return false;
                }
                return DirectionalAllows(hint,current,from,to);
            }
            internal bool DirectionalAllows(NavFormationFrameHint hint,NavTypedRoute current,NavPoint from,NavPoint to)
            {
                if(!Matches(hint,current))return RecastFormationMotion.DirectionalAllows(hint,current,from,to);
                if((hint.Flags&NavFormationFrameHint.Directional)==0||from.Equals(to))return true;
                return axis&&bounds&&DirectionalPrepared(hint,from,to,origin,direction,length,low,high,radius);
            }
            internal bool TailAllows(NavFormationFrameHint hint,NavTypedRoute current,NavPoint from,System.Collections.Generic.IReadOnlyList<NavPoint> tail,int index,NavPoint goal)
            {
                for(int i=index;i<tail.Count;i++){if(!DirectionalAllows(hint,current,from,tail[i]))return false;from=tail[i];}
                return DirectionalAllows(hint,current,from,goal);
            }
        }
        internal static bool Axis(NavTypedRoute route,int entryLeg,out NavPoint start,out NavPoint end,out NavPoint direction,out double length)
        {
            start=end=direction=default;length=0;
            if(route==null||entryLeg<0||entryLeg>=route.Legs.Count||route.Legs[entryLeg].Kind!=NavTypedLegKind.Portal||!(route.Legs[entryLeg].RecastAuthorityProvider is PartitionNavigationProvider p))return false;
            NavPartitionSeam a=null,b=null;string first=route.Legs[entryLeg].PortalId,last=null;
            for(int i=entryLeg+1;i<route.Legs.Count;i++)if(route.Legs[i].Kind==NavTypedLegKind.Portal){last=route.Legs[i].PortalId;break;}
            foreach(var s in p.Seams){if(s.Id==first)a=s;if(s.Id==last)b=s;}
            if(a==null||b==null)return false;start=new NavPoint((a.From.X+a.To.X)/2,(a.From.Z+a.To.Z)/2);end=new NavPoint((b.From.X+b.To.X)/2,(b.From.Z+b.To.Z)/2);double x=end.X-start.X,z=end.Z-start.Z;length=Math.Sqrt(x*x+z*z);if(length<=0)return false;direction=new NavPoint(x/length,z/length);return true;
        }
        // The same accepted seams/footprint define both lane layout and sweeps.
        internal static bool Bounds(NavTypedRoute route,int entryLeg,NavPoint origin,NavPoint direction,out double low,out double high,out double radius)
        {
            low=double.NegativeInfinity;high=double.PositiveInfinity;radius=route?.RecastPlan?.Spine.Radius??0;
            if(route?.RecastPlan==null||entryLeg<0||entryLeg>=route.Legs.Count||!(route.Legs[entryLeg].RecastAuthorityProvider is PartitionNavigationProvider provider))return false;
            string first=route.Legs[entryLeg].PortalId,last=null;for(int i=entryLeg+1;i<route.Legs.Count;i++)if(route.Legs[i].Kind==NavTypedLegKind.Portal){last=route.Legs[i].PortalId;break;}
            var normal=new NavPoint(-direction.Z,direction.X);int count=0;
            foreach(var seam in provider.Seams)if(seam.Id==first||seam.Id==last){double a=Station(seam.From,origin,normal),b=Station(seam.To,origin,normal);low=Math.Max(low,Math.Min(a,b)+radius);high=Math.Min(high,Math.Max(a,b)-radius);count++;}
            return count==2&&NavRecastSpine.Finite(low)&&NavRecastSpine.Finite(high)&&low<=high;
        }
        internal static bool DirectionalAllows(NavFormationFrameHint hint,NavTypedRoute route,NavPoint from,NavPoint to)
        {
            if((hint.Flags&NavFormationFrameHint.Directional)==0||from.Equals(to))return true;
            if(!Axis(route,hint.TargetLeg,out var a,out _,out var d,out double length)||!Bounds(route,hint.TargetLeg,a,d,out double low,out double high,out double radius))return false;
            return DirectionalPrepared(hint,from,to,a,d,length,low,high,radius);
        }
        private static bool DirectionalPrepared(NavFormationFrameHint hint,NavPoint from,NavPoint to,NavPoint a,NavPoint d,double length,double low,double high,double radius)
        {
            high=(low+high)/2-radius;var normal=new NavPoint(-d.Z,d.X);
            double reach=hint.DirectionalBankReach;
            if(!NavRecastSpine.Finite(reach)||reach<0)return false;
            double before=Station(from,a,d),after=Station(to,a,d);
            double lateral=Station(from,a,normal),next=Station(to,a,normal);
            bool Span(double first,double last,bool bankEntryRecovery=false){
                double start=0,end=1,delta=after-before;
                if(delta==0){if(before<first||before>last)return true;}
                else{double t0=(first-before)/delta,t1=(last-before)/delta;start=Math.Max(0,Math.Min(t0,t1));end=Math.Min(1,Math.Max(t0,t1));if(start>end)return true;}
                double at=lateral+(next-lateral)*start,until=lateral+(next-lateral)*end;
                bool Inside(double x)=>x>=low-1e-9&&x<=high+1e-9;
                if(Inside(at)&&Inside(until))return true;
                if(before<first||before>last){
                    // The extra bank domain admits the same monotonic recovery
                    // from just before its entry as from just after it. Otherwise
                    // an already displaced incoming body can hit an artificial
                    // one-step barrier while aligning. The separate ORIGINAL
                    // core span below still requires full-footprint alignment.
                    if(!bankEntryRecovery||before>=first)return false;
                    if(lateral<low)return next>lateral&&next<=high+1e-9;
                    if(lateral>high)return next<lateral&&next>=low-1e-9;
                    return false;
                }
                if(lateral<low)return next>=lateral&&next<=high+1e-9&&(next>lateral||after<before);
                if(lateral>high)return next<=lateral&&next>=low-1e-9&&(next<lateral||after<before);
                return false;
            }
            // A bank recovery is additional. It cannot admit an unaligned body
            // through the original full-footprint shared-passage boundary.
            return Span(-radius,length+radius)&&(reach==0||Span(-radius-reach,length+radius+reach,true));
        }

        internal static bool DirectionalTailAllows(NavFormationFrameHint hint,NavTypedRoute route,NavPoint from,System.Collections.Generic.IReadOnlyList<NavPoint> tail,int index,NavPoint goal)
        {
            for(int i=index;i<tail.Count;i++){if(!DirectionalAllows(hint,route,from,tail[i]))return false;from=tail[i];}
            return DirectionalAllows(hint,route,from,goal);
        }
        internal static double Station(NavPoint p,NavPoint origin,NavPoint direction)=>(p.X-origin.X)*direction.X+(p.Z-origin.Z)*direction.Z;
        internal static bool Allows(NavFormationFrameHint hint,NavTypedRoute route,NavPoint from,NavPoint to,bool matchedReceiverYield=false)
        {
            if(matchedReceiverYield)return true;
            if((hint.Flags&NavFormationFrameHint.Wait)!=0)return from.Equals(to);
            if(from.Equals(to))return true;
            if((hint.Flags&NavFormationFrameHint.Bound)!=0){
                if(!Axis(route,hint.TargetLeg,out var a,out _,out var d,out _))return false;
                double before=Station(from,a,d),after=Station(to,a,d);
                if(after>Math.Max(before,hint.ForwardLimit)+1e-9)return false;
            }
            // Recovering releases the row cap, not the physical re-entry domain.
            return DirectionalAllows(hint,route,from,to);
        }
    }
}
