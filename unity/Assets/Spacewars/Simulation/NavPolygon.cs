using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    // Immutable simple polygon; the triangulation and collision describe the same closed footprint.
    public sealed class NavPolygon
    {
        private readonly NavPoint[] vertices; private readonly int[] triangles;
        public IReadOnlyList<NavPoint> Vertices{get;} public IReadOnlyList<int> Triangles{get;}
        public double MinX{get;} public double MaxX{get;} public double MinZ{get;} public double MaxZ{get;}
        private const double Epsilon=1e-9; // Numerical predicate tolerance, not a map parameter.
        public NavPolygon(IEnumerable<NavPoint> source)
        {
            vertices=source.ToArray();if(vertices.Length<3||vertices.Any(p=>double.IsNaN(p.X)||double.IsInfinity(p.X)||double.IsNaN(p.Z)||double.IsInfinity(p.Z)))throw new ArgumentException("Invalid polygon vertices");
            double area=0;for(int i=0;i<vertices.Length;i++){var a=vertices[i];var b=vertices[(i+1)%vertices.Length];if(Distance(a,b)<Epsilon)throw new ArgumentException("Zero polygon edge");area+=a.X*b.Z-b.X*a.Z;
                for(int j=i+2;j<vertices.Length;j++){if(i==0&&j==vertices.Length-1)continue;if(Intersects(a,b,vertices[j],vertices[(j+1)%vertices.Length]))throw new ArgumentException("Self-intersecting polygon");}}
            if(Math.Abs(area)<Epsilon)throw new ArgumentException("Zero polygon area");if(area<0)Array.Reverse(vertices);
            MinX=vertices.Min(p=>p.X);MaxX=vertices.Max(p=>p.X);MinZ=vertices.Min(p=>p.Z);MaxZ=vertices.Max(p=>p.Z);
            var remaining=Enumerable.Range(0,vertices.Length).ToList();var indices=new List<int>();
            while(remaining.Count>3){bool clipped=false;for(int k=0;k<remaining.Count;k++){int a=remaining[(k+remaining.Count-1)%remaining.Count],b=remaining[k],c=remaining[(k+1)%remaining.Count];if(Cross(vertices[a],vertices[b],vertices[c])<=Epsilon)continue;
                    bool occupied=false;foreach(int q in remaining)if(q!=a&&q!=b&&q!=c&&InTriangle(vertices[q],vertices[a],vertices[b],vertices[c])){occupied=true;break;}if(occupied)continue;
                    indices.Add(a);indices.Add(b);indices.Add(c);remaining.RemoveAt(k);clipped=true;break;}
                if(!clipped)throw new ArgumentException("Polygon cannot be triangulated");}
            indices.AddRange(remaining);triangles=indices.ToArray();Vertices=Array.AsReadOnly(vertices);Triangles=Array.AsReadOnly(triangles);
        }
        public bool Contains(NavPoint p)
        {
            if(p.X<MinX||p.X>MaxX||p.Z<MinZ||p.Z>MaxZ)return false;bool inside=false;
            for(int i=0,j=vertices.Length-1;i<vertices.Length;j=i++){var a=vertices[j];var b=vertices[i];if(PointSegmentSquared(p,a,b)<=Epsilon*Epsilon)return true;if((a.Z>p.Z)!=(b.Z>p.Z)&&p.X<(b.X-a.X)*(p.Z-a.Z)/(b.Z-a.Z)+a.X)inside=!inside;}return inside;
        }
        public bool StrictlyContains(NavPolygon other)
        {
            foreach(var p in other.Vertices)if(!Contains(p))return false;
            for(int i=0;i<vertices.Length;i++)for(int j=0;j<other.vertices.Length;j++)if(Intersects(vertices[i],vertices[(i+1)%vertices.Length],other.vertices[j],other.vertices[(j+1)%other.vertices.Length]))return false;
            return true;
        }
        public double BoundaryDistanceSquared(NavPoint p){double best=double.PositiveInfinity;for(int i=0;i<vertices.Length;i++)best=Math.Min(best,PointSegmentSquared(p,vertices[i],vertices[(i+1)%vertices.Length]));return best;}
        public double DistanceSquared(NavPoint p)=>Contains(p)?0:BoundaryDistanceSquared(p);
        public double SegmentDistanceSquared(NavPoint a,NavPoint b)
        {
            if(Contains(a)||Contains(b))return 0;double best=double.PositiveInfinity;
            for(int i=0;i<vertices.Length;i++){var c=vertices[i];var d=vertices[(i+1)%vertices.Length];if(Intersects(a,b,c,d))return 0;best=Math.Min(best,Math.Min(Math.Min(PointSegmentSquared(a,c,d),PointSegmentSquared(b,c,d)),Math.Min(PointSegmentSquared(c,a,b),PointSegmentSquared(d,a,b))));}return best;
        }
        private static bool InTriangle(NavPoint p,NavPoint a,NavPoint b,NavPoint c)=>Cross(a,b,p)>=-Epsilon&&Cross(b,c,p)>=-Epsilon&&Cross(c,a,p)>=-Epsilon;
        private static double Cross(NavPoint a,NavPoint b,NavPoint c)=>(b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X);
        private static double Distance(NavPoint a,NavPoint b)=>(a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z);
        private static bool On(NavPoint a,NavPoint b,NavPoint p)=>p.X>=Math.Min(a.X,b.X)-Epsilon&&p.X<=Math.Max(a.X,b.X)+Epsilon&&p.Z>=Math.Min(a.Z,b.Z)-Epsilon&&p.Z<=Math.Max(a.Z,b.Z)+Epsilon;
        private static bool Intersects(NavPoint a,NavPoint b,NavPoint c,NavPoint d){double x=Cross(a,b,c),y=Cross(a,b,d),z=Cross(c,d,a),w=Cross(c,d,b);return x*y<0&&z*w<0||Math.Abs(x)<=Epsilon&&On(a,b,c)||Math.Abs(y)<=Epsilon&&On(a,b,d)||Math.Abs(z)<=Epsilon&&On(c,d,a)||Math.Abs(w)<=Epsilon&&On(c,d,b);}
        private static double PointSegmentSquared(NavPoint p,NavPoint a,NavPoint b){double x=b.X-a.X,z=b.Z-a.Z,t=Math.Max(0,Math.Min(1,((p.X-a.X)*x+(p.Z-a.Z)*z)/(x*x+z*z)));return Distance(p,new NavPoint(a.X+t*x,a.Z+t*z));}
    }
}
