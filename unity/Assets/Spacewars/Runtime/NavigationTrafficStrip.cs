using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // A temporary certificate derived from an installed typed corridor. Unsupported
    // partitions remain ordinary joint-motion paths; no map or bridge identity selects it.
    internal readonly struct NavigationTrafficStrip
    {
        internal readonly NavPartitionSeam First,Second;
        internal readonly bool AlongX;
        internal readonly double Entry,Exit,Lane,Radius;
        internal readonly int Direction;
        private NavigationTrafficStrip(NavPartitionSeam first,NavPartitionSeam second,bool alongX,double entry,double exit,double lane,double radius,int direction)
        {First=first;Second=second;AlongX=alongX;Entry=entry;Exit=exit;Lane=lane;Radius=radius;Direction=direction;}
        internal NavPoint Point(double progress)=>AlongX?new NavPoint(progress,Lane):new NavPoint(Lane,progress);
        internal double Progress(NavPoint point)=>AlongX?point.X:point.Z;
        internal double Lateral(NavPoint point)=>AlongX?point.Z:point.X;
        internal bool SamePassage(NavigationTrafficStrip other)=>First.Id==other.First.Id&&Second.Id==other.Second.Id;
        internal bool Cleared(NavUnit actor)=>Direction>0?Progress(actor.Position)-actor.Radius>Exit:Progress(actor.Position)+actor.Radius<Entry;
        internal bool Occupies(NavUnit actor)=>Progress(actor.Position)+actor.Radius>=Entry&&Progress(actor.Position)-actor.Radius<=Exit&&Math.Abs(Lateral(actor.Position)-Lane)<actor.Radius+Radius;
        internal NavPoint Wait=>Point(Direction>0?Entry-Radius:Exit+Radius);
        internal NavPoint Downstream(int seat)=>Point(Direction>0?Exit+Radius+2*Radius*seat:Entry-Radius-2*Radius*seat);
        private static double Along(NavPoint p,bool x)=>x?p.X:p.Z;
        private static double Across(NavPoint p,bool x)=>x?p.Z:p.X;
        private static NavPartitionSeam Find(PartitionNavigationProvider provider,string id)
        {foreach(var seam in provider.Seams)if(seam.Id==id)return seam;return null;}
        private static bool FullSweep(PartitionNavigationProvider provider,NavGeometry geometry,IPlayableTerrain terrain,NavClearanceProfile clearance,NavPoint from,NavPoint to)
            =>geometry.SegmentFree(from,to,clearance.Radius)&&terrain.SupportsFootprint(from,clearance.Radius)&&terrain.SupportsFootprint(to,clearance.Radius)&&terrain.SupportsSweep(from,to,clearance.Radius)&&provider.TryLocate(from,clearance,out var location)&&provider.TryTraverse(location,to,clearance,out _);
        internal bool ExitSeatSafe(PartitionNavigationProvider provider,NavGeometry geometry,IPlayableTerrain terrain,NavClearanceProfile clearance,int seat)
            =>seat>=0&&FullSweep(provider,geometry,terrain,clearance,Wait,Downstream(seat));
        internal static bool TryCreate(NavigationInstalledExecutionOwner installed,IPlayableTerrain terrain,NavClearanceProfile clearance,double[] left,double[] right,out NavigationTrafficStrip value)
            =>TryCreate(installed?.Corridor,terrain,clearance,left,right,out value);
        internal static bool TryCreate(NavigationHostCorridor corridor,IPlayableTerrain terrain,NavClearanceProfile clearance,double[] left,double[] right,out NavigationTrafficStrip value)
        {
            value=default;double radius=clearance?.Radius??0;if(corridor==null||terrain==null||!(corridor.ProviderOwner.Provider is PartitionNavigationProvider provider)||radius<=0)return false;
            var legs=corridor.TypedRoute.Legs;NavTypedLeg a=null,b=null;
            foreach(var leg in legs)if(leg.Kind==NavTypedLegKind.Portal){if(a==null)a=leg;else if(b==null)b=leg;else return false;}
            if(a==null||b==null)return false;var sa=Find(provider,a.PortalId);var sb=Find(provider,b.PortalId);
            if(sa==null||sb==null||!sa.Allowed||!sb.Allowed)return false;
            bool alongX=sa.From.X==sa.To.X&&sb.From.X==sb.To.X;
            if(!alongX&&!(sa.From.Z==sa.To.Z&&sb.From.Z==sb.To.Z))return false;
            double aa=Along(sa.From,alongX),bb=Along(sb.From,alongX);if(aa==bb)return false;
            var first=aa<bb?sa:sb;var second=aa<bb?sb:sa;double entry=Math.Min(aa,bb),exit=Math.Max(aa,bb);
            string shared=sa.FromSurface==sb.FromSurface||sa.FromSurface==sb.ToSurface?sa.FromSurface:sa.ToSurface==sb.FromSurface||sa.ToSurface==sb.ToSurface?sa.ToSurface:null;
            if(shared==null)return false;NavLayerSurface surface=null;foreach(var s in provider.Surfaces)if(s.Id==shared){surface=s;break;}
            if(surface==null||surface.Area.Boundary.Vertices.Count!=4)return false;
            double low=Math.Max(Math.Min(Across(sa.From,alongX),Across(sa.To,alongX)),Math.Min(Across(sb.From,alongX),Across(sb.To,alongX)));
            double high=Math.Min(Math.Max(Across(sa.From,alongX),Across(sa.To,alongX)),Math.Max(Across(sb.From,alongX),Across(sb.To,alongX)));
            if(high<=low+2*radius)return false;
            // Only a complete rectangular intermediate support is certified here.
            int corners=0;foreach(var p in surface.Area.Boundary.Vertices){double x=Along(p,alongX),z=Across(p,alongX);if((x!=entry&&x!=exit)||(z!=low&&z!=high))return false;corners|=(x==entry?1:2)*(z==low?1:4);}
            if(corners!=15)return false;
            double lane=Across(a.From.Position,alongX);if(lane!=Across(b.From.Position,alongX))return false;
            var geometry=corridor.RequestOwner.Request.Geometry;
            // A cut through an actual circular blocker proves that all possible full
            // circle centres fit in one interval narrower than a second diameter.
            // Rectangular/polygon blockers never overstate this capacity proof.
            double cut=double.NaN;foreach(var obstacle in geometry.Obstacles)if(obstacle.CircleRadius>0){double x=Along(obstacle.CircleCenter,alongX);if(x>entry&&x<exit){cut=x;break;}}
            if(double.IsNaN(cut)||left.Length<geometry.Obstacles.Count||right.Length<geometry.Obstacles.Count)return false;
            int count=0;foreach(var obstacle in geometry.Obstacles)if(obstacle.CircleRadius>0){double dx=Along(obstacle.CircleCenter,alongX)-cut,r=radius+obstacle.CircleRadius;if(Math.Abs(dx)>r)continue;double reach=Math.Sqrt(r*r-dx*dx),z=Across(obstacle.CircleCenter,alongX);left[count]=z-reach;right[count++]=z+reach;}
            for(int i=1;i<count;i++){double l=left[i],r=right[i];int j=i;while(j>0&&left[j-1]>l){left[j]=left[j-1];right[j]=right[j-1];j--;}left[j]=l;right[j]=r;}
            double cursor=low+radius,min=double.PositiveInfinity,max=double.NegativeInfinity,upper=high-radius;
            for(int i=0;i<count;i++){if(left[i]>cursor){min=Math.Min(min,cursor);max=Math.Max(max,Math.Min(left[i],upper));}cursor=Math.Max(cursor,right[i]);if(cursor>=upper)break;}
            if(cursor<upper){min=Math.Min(min,cursor);max=upper;}
            if(double.IsInfinity(min)||max-min>=2*radius||lane<min||lane>max)return false;
            value=new NavigationTrafficStrip(first,second,alongX,entry,exit,lane,radius,aa<bb?1:-1);
            if(!value.ExitSeatSafe(provider,geometry,terrain,clearance,0)){value=default;return false;}return true;
        }
    }
}
