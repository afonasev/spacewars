using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    public readonly struct BallisticPoint
    {
        public readonly double X,Y,Z;
        public BallisticPoint(double x,double y,double z){X=x;Y=y;Z=z;}
        public NavPoint Ground=>new NavPoint(X,Z);
    }
    public sealed class BallisticFlight
    {
        public readonly BallisticPoint Start,End;
        public readonly double ArcHeight,Duration;
        public BallisticFlight(BallisticPoint start,BallisticPoint end,double arc,double speed){Start=start;End=end;ArcHeight=arc;Duration=PlayableBallistics.Length(start,end,arc)/speed;}
        // Exact hydration: preserve saved duration rather than rerunning floating-point length/speed arithmetic.
        public static BallisticFlight Restore(BallisticPoint start,BallisticPoint end,double arc,double duration)
        { if(duration<=0||double.IsNaN(duration)||double.IsInfinity(duration))throw new ArgumentException("Invalid saved flight duration.");return new BallisticFlight(start,end,arc,duration,true); }
        private BallisticFlight(BallisticPoint start,BallisticPoint end,double arc,double duration,bool restored){Start=start;End=end;ArcHeight=arc;Duration=duration;}
        public BallisticPoint Point(double u)=>new BallisticPoint(Start.X+(End.X-Start.X)*u,Start.Y+(End.Y-Start.Y)*u+4*ArcHeight*u*(1-u),Start.Z+(End.Z-Start.Z)*u);
        public BallisticPoint Tangent(double u)=>new BallisticPoint(End.X-Start.X,End.Y-Start.Y+4*ArcHeight*(1-2*u),End.Z-Start.Z);
    }
    public readonly struct BallisticBody
    {
        public readonly int Id; public readonly PlayableOwner Owner; public readonly NavPoint Position,Velocity;
        public readonly double Radius,Height,Health,BaseHeight; public readonly bool Building;
        public BallisticBody(int id,PlayableOwner owner,NavPoint position,NavPoint velocity,double radius,double height,double health,bool building,double baseHeight=0){BaseHeight=baseHeight;Id=id;Owner=owner;Position=position;Velocity=velocity;Radius=radius;Height=height;Health=health;Building=building;}
        public BallisticBody Predict(double time,Func<NavPoint,double> terrain=null){var point=new NavPoint(Position.X+Velocity.X*time,Position.Z+Velocity.Z*time);return new BallisticBody(Id,Owner,point,Velocity,Radius,Height,Health,Building,terrain?.Invoke(point)??BaseHeight);}
    }
    public readonly struct BallisticContact
    {
        public readonly double Progress;public readonly BallisticPoint Point;public readonly int BuildingId;
        public BallisticContact(double progress,BallisticPoint point,int building=0){Progress=progress;Point=point;BuildingId=building;}
    }
    // Flat native arena adapter of src/sim/ballistics.ts. Numeric eps/sample spacing
    // are reference solver invariants, not gameplay tuning. Authored sheets remain U7.
    public static class PlayableBallistics
    {
        private const double Eps=1e-5;
        public static double Length(BallisticPoint start,BallisticPoint end,double arc)
        {
            double h=Math.Sqrt((end.X-start.X)*(end.X-start.X)+(end.Z-start.Z)*(end.Z-start.Z)),a=end.Y-start.Y+4*arc,b=8*arc;
            if(Math.Abs(b)<Eps)return Math.Max(Eps,Math.Sqrt(h*h+a*a));
            double Primitive(double v)=>h<Eps?v*Math.Abs(v)/2:(v*Math.Sqrt(h*h+v*v)+h*h*Math.Log(v/h+Math.Sqrt(v*v/(h*h)+1)))/2;
            return Math.Max(Eps,(Primitive(a)-Primitive(a-b))/b);
        }
        public static BallisticFlight Aim(NavPoint from,NavPoint target,NavPoint velocity,double height,double arc,double speed,bool guided,double threshold,double falloff,double maxTime,Func<NavPoint,double> terrain=null)
        {
            var start=new BallisticPoint(from.X,(terrain?.Invoke(from)??0)+height,from.Z);
            double magnitude=Math.Sqrt(velocity.X*velocity.X+velocity.Z*velocity.Z);
            double u=Math.Max(0,Math.Min(1,(magnitude-(threshold-falloff))/(2*falloff)));
            double lead=guided?1-u*u*(3-2*u):0;
            BallisticPoint End(double t)=>new BallisticPoint(target.X+velocity.X*lead*t,terrain?.Invoke(new NavPoint(target.X+velocity.X*lead*t,target.Z+velocity.Z*lead*t))??0,target.Z+velocity.Z*lead*t);
            double Time(double t)=>Length(start,End(t),arc)/speed;
            double aimTime=0;
            if(magnitude*lead>=Eps)
            {
                double high=Math.Min(Time(0),maxTime);bool found=false;
                // Exact reference solver iteration limits are numerical invariants.
                for(int i=0;i<24;i++){if(Time(high)<=high){found=true;break;}if(high>=maxTime)break;high=Math.Min(high*2,maxTime);}
                if(found){double low=0;for(int i=0;i<48;i++){double mid=(low+high)/2;if(Time(mid)>mid)low=mid;else high=mid;}aimTime=high;}
            }
            return new BallisticFlight(start,End(aimTime),arc,speed);
        }
        private static bool Overlap(NavObstacle s,BallisticPoint p,double radius)
        {return p.X+radius>=s.MinX&&p.X-radius<=s.MaxX&&p.Z+radius>=s.MinZ&&p.Z-radius<=s.MaxZ&&s.DistanceSquared(p.Ground)<=radius*radius;}
        private static int Cover(BallisticPoint p,double radius,IReadOnlyList<NavObstacle> solids,double wallHeight,IReadOnlyList<BallisticBody> bodies,int ignored)
        {
            foreach(var s in solids)if(p.Y+radius>=s.Bottom&&p.Y-radius<=(double.IsNaN(s.Top)?wallHeight:s.Top)&&Overlap(s,p,radius))return -1;
            foreach(var b in bodies)if(b.Building&&b.Id!=ignored&&p.Y+radius>=b.BaseHeight&&p.Y-radius<=b.BaseHeight+b.Height)
            {double x=p.X-b.Position.X,z=p.Z-b.Position.Z,r=b.Radius+radius;if(x*x+z*z<=r*r)return b.Id;}
            return 0;
        }
        public static BallisticContact? FirstContact(BallisticFlight f,double radius,IReadOnlyList<NavObstacle> solids,double wallHeight,IReadOnlyList<BallisticBody> bodies,int source,double from=0,double to=1,Func<NavPoint,double> terrain=null)
        {
            var middle=new BallisticPoint((f.Start.X+f.End.X)/2,0,(f.Start.Z+f.End.Z)/2);
            double reach=Math.Sqrt((f.End.X-f.Start.X)*(f.End.X-f.Start.X)+(f.End.Z-f.Start.Z)*(f.End.Z-f.Start.Z))/2+radius;
            solids=solids.Where(s=>Overlap(s,middle,reach)).ToArray();
            bodies=bodies.Where(b=>b.Building&&b.Id!=source&&Math.Sqrt((b.Position.X-middle.X)*(b.Position.X-middle.X)+(b.Position.Z-middle.Z)*(b.Position.Z-middle.Z))<=reach+b.Radius).ToArray();
            if(solids.Count==0&&bodies.Count==0&&terrain==null)
            {
                BallisticContact? first=null;double a=-4*f.ArcHeight,b=f.End.Y-f.Start.Y+4*f.ArcHeight;
                foreach(double offset in new[]{-radius,radius})
                {
                    double c=f.Start.Y+offset,disc=b*b-4*a*c;
                    var roots=Math.Abs(a)<Eps?(Math.Abs(b)<Eps?Array.Empty<double>():new[]{-c/b}):disc<0?Array.Empty<double>():new[]{(-b+Math.Sqrt(disc))/(2*a),(-b-Math.Sqrt(disc))/(2*a)};
                    foreach(double u in roots){double derivative=b+2*a*u;if(u<=from+Eps||u>to+Eps||(offset<0&&derivative>=0)||(offset>0&&derivative<=0))continue;var point=f.Point(u);if(first==null||u<first.Value.Progress)first=new BallisticContact(u,new BallisticPoint(point.X,0,point.Z));}
                }
                return first??(to>=1-Eps?new BallisticContact(1,f.End):(BallisticContact?)null);
            }
            double dx=f.End.X-f.Start.X,dz=f.End.Z-f.Start.Z,dy=Math.Abs(f.End.Y-f.Start.Y)+4*f.ArcHeight;
            double bound=Math.Sqrt(dx*dx+dz*dz+dy*dy);int steps=Math.Max(1,(int)Math.Ceiling((to-from)*bound/Math.Min(.025,Math.Max(.005,radius/2))));
            var prev=f.Point(from);
            for(int i=1;i<=steps;i++)
            {
                double u=from+(to-from)*i/steps;var p=f.Point(u);int hit=Cover(p,radius,solids,wallHeight,bodies,source);
                if(hit!=0)return new BallisticContact(u,p,Math.Max(0,hit));
                double ground=terrain?.Invoke(p.Ground)??0,previousGround=terrain?.Invoke(prev.Ground)??0;
                if((prev.Y-radius>previousGround+Eps&&p.Y-radius<=ground+Eps)||(prev.Y+radius<previousGround-Eps&&p.Y+radius>=ground-Eps))return new BallisticContact(u,new BallisticPoint(p.X,ground,p.Z));
                prev=p;
            }
            return to>=1-Eps?new BallisticContact(1,f.End):(BallisticContact?)null;
        }
        public static bool TerrainLineClear(BallisticPoint from,BallisticPoint to,double radius,IReadOnlyList<NavObstacle> solids,double wallHeight,Func<NavPoint,double> terrain)
        {
            double distance=Math.Sqrt(Math.Pow(to.X-from.X,2)+Math.Pow(to.Z-from.Z,2)+Math.Pow(to.Y-from.Y,2));
            // Same geometric sample tolerance as the native ballistic solver, independent of gameplay tuning.
            int steps=Math.Max(1,(int)Math.Ceiling(distance/.025));
            for(int i=0;i<=steps;i++){double t=(double)i/steps;var p=new BallisticPoint(from.X+(to.X-from.X)*t,from.Y+(to.Y-from.Y)*t,from.Z+(to.Z-from.Z)*t);if(p.Y-radius<terrain(p.Ground)-Eps)return false;foreach(var s in solids)if(p.Y+radius>=s.Bottom&&p.Y-radius<=(double.IsNaN(s.Top)?wallHeight:s.Top)&&Overlap(s,p,radius))return false;}
            return true;
        }
        public static bool BlastHits(BallisticPoint impact,double radius,BallisticBody target,IReadOnlyList<NavObstacle> solids,double wallHeight,IReadOnlyList<BallisticBody> bodies,Func<NavPoint,double> terrain=null)
        {
            double x=target.Position.X-impact.X,z=target.Position.Z-impact.Z,y=Math.Max(target.BaseHeight,Math.Min(target.BaseHeight+target.Height,impact.Y));
            double horizontal=Math.Max(0,Math.Sqrt(x*x+z*z)-target.Radius);
            if(Math.Sqrt(horizontal*horizontal+(y-impact.Y)*(y-impact.Y))>radius+Eps)return false;
            double destY=Math.Max(target.BaseHeight+Eps,Math.Min(target.BaseHeight+target.Height,impact.Y+Eps)),dy=destY-impact.Y;
            int steps=Math.Max(1,(int)Math.Ceiling(Math.Sqrt(x*x+z*z+dy*dy)/.025));
            for(int i=1;i<steps;i++){double u=(double)i/steps;var p=new BallisticPoint(impact.X+x*u,impact.Y+Eps+dy*u,impact.Z+z*u);if(Cover(p,0,solids,wallHeight,bodies,target.Id)!=0||(terrain!=null&&p.Y<terrain(p.Ground)-Eps))return false;}
            return true;
        }
        public static int BestTarget(NavPoint from,PlayableOwner owner,double range,double launchHeight,double arc,double speed,double radius,double blast,double damage,double penalty,IReadOnlyList<NavObstacle> solids,double wallHeight,IReadOnlyList<BallisticBody> observed,Func<NavPoint,double> terrain=null,bool guided=false,double leadThreshold=0,double leadFalloff=1,double maxLeadTime=0,Func<PlayableOwner,PlayableOwner,bool> hostile=null)
        {
            int best=0;double bestScore=0,bestDistance=double.PositiveInfinity;
            foreach(var target in observed)
            {
                if(hostile==null?target.Owner==owner:!hostile(target.Owner,owner))continue;double x=target.Position.X-from.X,z=target.Position.Z-from.Z,d=Math.Sqrt(x*x+z*z);if(d>range)continue;
                var flight=guided?Aim(from,target.Position,target.Velocity,launchHeight,arc,speed,true,leadThreshold,leadFalloff,maxLeadTime,terrain):new BallisticFlight(new BallisticPoint(from.X,(terrain?.Invoke(from)??0)+launchHeight,from.Z),new BallisticPoint(target.Position.X,target.BaseHeight,target.Position.Z),arc,speed);
                var impact=FirstContact(flight,radius,solids,wallHeight,observed,0,terrain:terrain).Value;
                var predicted=new BallisticBody[observed.Count];for(int i=0;i<predicted.Length;i++)predicted[i]=observed[i].Predict(flight.Duration*impact.Progress,terrain);
                double score=0;foreach(var t in predicted)if(BlastHits(impact.Point,blast,t,solids,wallHeight,predicted,terrain))score+=Math.Min(t.Health,damage)*((hostile==null?t.Owner==owner:!hostile(t.Owner,owner))?-penalty:1);
                // IDs have stable decimal wire keys; retain reference lexical rather than numeric tie order.
                if(score>bestScore||(score>0&&score==bestScore&&(d<bestDistance||(d==bestDistance&&string.CompareOrdinal(target.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),best.ToString(System.Globalization.CultureInfo.InvariantCulture))<0)))){best=target.Id;bestScore=score;bestDistance=d;}
            }
            return best;
        }
    }
}
