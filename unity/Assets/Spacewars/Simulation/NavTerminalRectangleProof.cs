using System;
namespace Spacewars.Simulation
{
    // Encloses the *rounded binary64 expression* used by NavGeometry.Cross.
    // Every primitive is widened by one adjacent representable number. No
    // corner visibility assumption, epsilon, inflated obstacle or exact-real
    // replacement for the existing floating point predicate is used.
    internal static class NavTerminalRectangleProof
    {
        private struct Interval
        {
            internal double Low,High;
            internal Interval(double low,double high){Low=low;High=high;}
            internal bool Valid=>Finite(Low)&&Finite(High)&&Low<=High;
            internal static Interval Point(double x)=>new Interval(x,x);
            internal static Interval Sub(Interval a,Interval b)=>Widen(a.Low-b.High,a.High-b.Low);
            internal static Interval Mul(Interval a,Interval b)
            {
                double p=a.Low*b.Low,q=a.Low*b.High,r=a.High*b.Low,s=a.High*b.High;
                if(!a.Valid||!b.Valid||!Finite(p)||!Finite(q)||!Finite(r)||!Finite(s))return new Interval(double.NaN,double.NaN);
                return Widen(Math.Min(Math.Min(p,q),Math.Min(r,s)),Math.Max(Math.Max(p,q),Math.Max(r,s)));
            }
            private static Interval Widen(double low,double high)
            {
                if(!Finite(low)||!Finite(high))return new Interval(double.NaN,double.NaN);
                return new Interval(Adjacent(low,false),Adjacent(high,true));
            }
        }
        private static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        private static double Adjacent(double x,bool up)
        {
            if(x==0)return up?double.Epsilon:-double.Epsilon;
            long bits=BitConverter.DoubleToInt64Bits(x);
            return BitConverter.Int64BitsToDouble(bits+((x>0)==up?1:-1));
        }
        private static Interval Cross(Interval ax,Interval az,Interval bx,Interval bz,Interval px,Interval pz)
            =>Interval.Sub(Interval.Mul(Interval.Sub(bx,ax),Interval.Sub(pz,az)),Interval.Mul(Interval.Sub(bz,az),Interval.Sub(px,ax)));
        private static bool Opposite(Interval a,Interval b)=>a.Valid&&b.Valid&&(a.Low>0&&b.High<0||a.High<0&&b.Low>0);
        private static bool Edge(NavPoint a,NavHeuristicBox box,NavPoint c,NavPoint d)
        {
            var ax=Interval.Point(a.X);var az=Interval.Point(a.Z);var bx=new Interval(box.MinX,box.MaxX);var bz=new Interval(box.MinZ,box.MaxZ);
            var cx=Interval.Point(c.X);var cz=Interval.Point(c.Z);var dx=Interval.Point(d.X);var dz=Interval.Point(d.Z);
            return Opposite(Cross(ax,az,bx,bz,cx,cz),Cross(ax,az,bx,bz,dx,dz))&&Opposite(Cross(cx,cz,dx,dz,ax,az),Cross(cx,cz,dx,dz,bx,bz));
        }
        private static bool Corner(NavPoint p,NavHeuristicBox box,NavPoint a,NavPoint k,NavPoint b)
        {
            // For each target T, Cross(P,T,K) either has one strict sign or is
            // zero. Opposite outer signs select AK or KB; zero uses the actual
            // SegmentsIntersect on-segment branch, hence the RAW bbox guard.
            if(!(Math.Max(p.X,box.MinX)>=k.X&&Math.Min(p.X,box.MaxX)<=k.X&&Math.Max(p.Z,box.MinZ)>=k.Z&&Math.Min(p.Z,box.MaxZ)<=k.Z))return false;
            var px=Interval.Point(p.X);var pz=Interval.Point(p.Z);var tx=new Interval(box.MinX,box.MaxX);var tz=new Interval(box.MinZ,box.MaxZ);
            var ax=Interval.Point(a.X);var az=Interval.Point(a.Z);var kx=Interval.Point(k.X);var kz=Interval.Point(k.Z);var bx=Interval.Point(b.X);var bz=Interval.Point(b.Z);
            return Cross(px,pz,tx,tz,kx,kz).Valid&&Opposite(Cross(px,pz,tx,tz,ax,az),Cross(px,pz,tx,tz,bx,bz))
                &&Opposite(Cross(ax,az,kx,kz,px,pz),Cross(ax,az,kx,kz,tx,tz))
                &&Opposite(Cross(kx,kz,bx,bz,px,pz),Cross(kx,kz,bx,bz,tx,tz));
        }
        internal static bool Reject(NavPoint from,NavHeuristicBox box,double radius,NavObstacle r,bool corners=false)
        {
            if(r.Polygon!=null||r.CircleRadius!=0||!Finite(radius)||radius<0||!Finite(from.X)||!Finite(from.Z)||!Finite(box.MinX)||!Finite(box.MinZ)||!Finite(box.MaxX)||!Finite(box.MaxZ)||box.MinX>box.MaxX||box.MinZ>box.MaxZ||!Finite(r.MinX)||!Finite(r.MinZ)||!Finite(r.MaxX)||!Finite(r.MaxZ)||r.MinX>=r.MaxX||r.MinZ>=r.MaxZ)return false;
            // The least expanded broadphase overlap over every endpoint in
            // the box must pass the same rounded min/max +/- radius checks.
            if(!(Math.Max(from.X,box.MinX)+radius>=r.MinX&&Math.Min(from.X,box.MaxX)-radius<=r.MaxX&&Math.Max(from.Z,box.MinZ)+radius>=r.MinZ&&Math.Min(from.Z,box.MaxZ)-radius<=r.MaxZ))return false;
            var p1=new NavPoint(r.MinX,r.MinZ);var p2=new NavPoint(r.MaxX,r.MinZ);var p3=new NavPoint(r.MaxX,r.MaxZ);var p4=new NavPoint(r.MinX,r.MaxZ);
            return Edge(from,box,p1,p2)||Edge(from,box,p2,p3)||Edge(from,box,p3,p4)||Edge(from,box,p4,p1)
                ||corners&&(Corner(from,box,p4,p1,p2)||Corner(from,box,p1,p2,p3)||Corner(from,box,p2,p3,p4)||Corner(from,box,p3,p4,p1));
        }
    }
}
