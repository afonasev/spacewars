using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    public sealed partial class AntennaCityMap
    {
        public double MainStreetWidth{get;} public double SideStreetWidth{get;} public double BaseYardRadius{get;} public double OutpostYardRadius{get;} public double MineYardRadius{get;} public double BlockGap{get;} public double DistrictPassageWidth{get;} public double KeyPointPadScale{get;} public double KeyPointPadBorder{get;}
        public IReadOnlyList<NavObstacle> Streets{get;private set;}
        public IReadOnlyList<NavObstacle> Yards{get;private set;}
        public IReadOnlyList<IReadOnlyList<NavObstacle>> Quarters{get;private set;}
        // Authored F2 route vertices, not solver or movement tuning. Both sides skirt
        // the OUTSIDE of a ramp foot before reaching the ground loop at the center.
        public NavPoint[] CenterApproach(int start)
        {
            if(start<0||start>=6)throw new ArgumentOutOfRangeException(nameof(start));
            int plaza=start/2,side=start%2==0?-1:1;double a=-90+120*plaza;
            return new[]{PointAt(-120+60*start,82),Local(a,52,side*42,8),Local(a,52,side*42,-16),Local(a,52,side*25,-25),PointAt(-120+60*start,24)};
        }
        NavObstacle Street(NavPoint a,NavPoint b,double width)
        {
            double dx=b.X-a.X,dz=b.Z-a.Z,len=Math.Sqrt(dx*dx+dz*dz),nx=-dz/len*width/2,nz=dx/len*width/2;
            double ex=dx/len*width/2,ez=dz/len*width/2;a=new NavPoint(a.X-ex,a.Z-ez);b=new NavPoint(b.X+ex,b.Z+ez);
            return new NavObstacle(new NavPolygon(new[]{new NavPoint(a.X+nx,a.Z+nz),new NavPoint(b.X+nx,b.Z+nz),new NavPoint(b.X-nx,b.Z-nz),new NavPoint(a.X-nx,a.Z-nz)}));
        }
        List<NavObstacle> CityBlocks(List<MapSupport> supports)
        {
            var roads=new List<NavObstacle>();var yards=new List<NavObstacle>();
            void Road(NavPoint a,NavPoint b,double width)=>roads.Add(Street(a,b,width*Scale));
            void Yard(NavPoint p,double r){r*=Scale;yards.Add(new NavObstacle(p.X-r,p.Z-r,p.X+r,p.Z+r));}
            for(int i=0;i<6;i++){
                double a=-120+60*i;
                Road(PointAt(a,124),PointAt(a+60,124),MainStreetWidth);
                Road(PointAt(a,148),PointAt(a,82),MainStreetWidth);
                Road(PointAt(a+30,106),PointAt(a+30,124),SideStreetWidth);
                Road(PointAt(a,24),PointAt(a+60,24),SideStreetWidth);
                var approach=CenterApproach(i);for(int j=1;j<approach.Length;j++)Road(approach[j-1],approach[j],SideStreetWidth);
                Yard(PointAt(a,120),BaseYardRadius);Yard(PointAt(a,82),OutpostYardRadius);
                Yard(PointAt(a,142),MineYardRadius);Yard(PointAt(a+30,124),MineYardRadius);
            }
            for(int i=0;i<3;i++)foreach(int side in new[]{-1,1})Road(Local(-90+120*i,52,side*42,0),RampFoot(i,side),SideStreetWidth);
            Streets=roads.AsReadOnly();Yards=yards.AsReadOnly();
            // Fixed author-space lot bands preserve the approved city silhouette;
            // profile controls govern street/yard clearance, height and overall scale.
            var lots=new List<NavPoint[]>();
            void Lot(double a,double lo,double hi,double f0,double f1){
                double gap=BlockGap/2;
                double Edge(double r,double f)=>((2*f-1)*(r/Math.Sqrt(3)-gap));
                lots.Add(new[]{Local(a,0,Edge(lo,f0)+gap,lo),Local(a,0,Edge(hi,f0)+gap,hi),Local(a,0,Edge(hi,f1)-gap,hi),Local(a,0,Edge(lo,f1)-gap,lo)});
            }
            for(int i=0;i<6;i++){
                double a=-90+60*i;
                for(int j=0;j<4;j++)Lot(a,133,145,j/4d,(j+1)/4d);
                for(int j=0;j<2;j++){Lot(a,115,130,j/2d,(j+1)/2d);Lot(a,71.5+DistrictPassageWidth/2,99,j/2d,(j+1)/2d);Lot(a,32,71.5-DistrictPassageWidth/2,j/2d,(j+1)/2d);}
            }
            var cuts=roads.Concat(yards).Concat(supports.Where(s=>s.Id!="city/street").Select(s=>s.Bounds)).ToArray();
            var result=new List<NavObstacle>();var quarters=new List<IReadOnlyList<NavObstacle>>();
            foreach(var lot in lots){
                var pieces=new List<NavPoint[]>{new NavPolygon(lot).Vertices.ToArray()};
                foreach(var cut in cuts){var next=new List<NavPoint[]>();foreach(var piece in pieces)Subtract(piece,cut,next);pieces=next;if(pieces.Count==0)break;}
                int first=result.Count;foreach(var piece in pieces)result.Add(new NavObstacle(new NavPolygon(piece),0,BlockHeight));
                if(result.Count>first)quarters.Add(result.Skip(first).ToArray());
            }
            Quarters=quarters.AsReadOnly();return result;
        }
        // Convex authoring cuts keep every resulting footprint convex. This is
        // static map construction only; navigation continues through the existing provider.
        static void Subtract(NavPoint[] polygon,NavObstacle cut,List<NavPoint[]> output)
        {
            if(polygon.Max(p=>p.X)<=cut.MinX||polygon.Min(p=>p.X)>=cut.MaxX||polygon.Max(p=>p.Z)<=cut.MinZ||polygon.Min(p=>p.Z)>=cut.MaxZ){output.Add(polygon);return;}
            var remainder=polygon;var edges=cut.Footprint.Vertices;
            for(int i=0;i<edges.Count&&remainder.Length>=3;i++){
                var a=edges[i];var b=edges[(i+1)%edges.Count];var outside=Clip(remainder,a,b,false);
                if(outside.Length>=3)output.Add(outside);remainder=Clip(remainder,a,b,true);
            }
        }
        static NavPoint[] Clip(NavPoint[] points,NavPoint a,NavPoint b,bool inside)
        {
            const double epsilon=1e-7; // Numerical geometry cleanup, not a playable passage size.
            double Side(NavPoint p)=>((b.X-a.X)*(p.Z-a.Z)-(b.Z-a.Z)*(p.X-a.X))*(inside?1:-1);
            var result=new List<NavPoint>();var prev=points[points.Length-1];double prior=Side(prev);
            foreach(var point in points){double value=Side(point);if((value>=0)!=(prior>=0)){double t=prior/(prior-value);result.Add(new NavPoint(prev.X+(point.X-prev.X)*t,prev.Z+(point.Z-prev.Z)*t));}if(value>=0)result.Add(point);prev=point;prior=value;}
            for(int i=result.Count-1;i>=0&&result.Count>=3;i--){var p=result[(i+result.Count-1)%result.Count];var q=result[i];var n=result[(i+1)%result.Count];if(Math.Abs((q.X-p.X)*(n.Z-q.Z)-(q.Z-p.Z)*(n.X-q.X))<epsilon)result.RemoveAt(i);}
            double area=0;for(int i=0;i<result.Count;i++){var p=result[i];var q=result[(i+1)%result.Count];area+=p.X*q.Z-q.X*p.Z;}
            return result.Count<3||Math.Abs(area)<epsilon?Array.Empty<NavPoint>():result.ToArray();
        }
    }
}
