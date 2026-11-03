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
        public readonly BuildingCombatPose? Combat;
        public BallisticBody(int id,PlayableOwner owner,NavPoint position,NavPoint velocity,double radius,double height,double health,bool building,double baseHeight=0,BuildingCombatPose? combat=null){Combat=combat;BaseHeight=baseHeight;Id=id;Owner=owner;Position=position;Velocity=velocity;Radius=radius;Height=height;Health=health;Building=building;}
        public BallisticBody Predict(double time,Func<NavPoint,double> terrain=null){var point=new NavPoint(Position.X+Velocity.X*time,Position.Z+Velocity.Z*time);return new BallisticBody(Id,Owner,point,Velocity,Radius,Height,Health,Building,terrain?.Invoke(point)??BaseHeight,Combat);}
    }
    public readonly struct BallisticContact
    {
        public readonly double Progress;public readonly BallisticPoint Point;public readonly int BuildingId,BodyId;
        public BallisticContact(double progress,BallisticPoint point,int building=0,int body=0){Progress=progress;Point=point;BuildingId=building;BodyId=body==0?building:body;}
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
        private static bool Overlap(NavObstacle s,BallisticPoint p,double radius)
        {return p.X+radius>=s.MinX&&p.X-radius<=s.MaxX&&p.Z+radius>=s.MinZ&&p.Z-radius<=s.MaxZ&&s.DistanceSquared(p.Ground)<=radius*radius;}
        private static int Cover(BallisticPoint p,double radius,IReadOnlyList<NavObstacle> solids,double wallHeight,IReadOnlyList<BallisticBody> bodies,int ignored,bool includeUnits=false)
        {
            // IReadOnlyList foreach boxes array/list enumerators on this hot per-sample
            // path. Index in the same order; keep every exact contact predicate.
            for(int i=0;i<solids.Count;i++){var s=solids[i];if(p.Y+radius>=s.Bottom&&p.Y-radius<=(double.IsNaN(s.Top)?wallHeight:s.Top)&&Overlap(s,p,radius))return -1;}
            for(int i=0;i<bodies.Count;i++){var b=bodies[i];if((!includeUnits&&!b.Building)||b.Id==ignored)continue;
                if(b.Combat.HasValue){if(b.Combat.Value.Overlaps(p,radius))return b.Id;continue;}
                if(p.Y+radius>=b.BaseHeight&&p.Y-radius<=b.BaseHeight+b.Height){double x=p.X-b.Position.X,z=p.Z-b.Position.Z,r=b.Radius+radius;if(x*x+z*z<=r*r)return b.Id;}}

            return 0;
        }
        public static BallisticContact? FirstContact(BallisticFlight f,double radius,IReadOnlyList<NavObstacle> solids,double wallHeight,IReadOnlyList<BallisticBody> bodies,int source,double from=0,double to=1,Func<NavPoint,double> terrain=null,Func<PlayableOwner,bool> canContact=null)
        {
            var middle=new BallisticPoint((f.Start.X+f.End.X)/2,0,(f.Start.Z+f.End.Z)/2);
            double reach=Math.Sqrt((f.End.X-f.Start.X)*(f.End.X-f.Start.X)+(f.End.Z-f.Start.Z)*(f.End.Z-f.Start.Z))/2+radius;
            solids=solids.Where(s=>Overlap(s,middle,reach)).ToArray();
            bodies=bodies.Where(b=>b.Id!=source&&(canContact==null||canContact(b.Owner))&&Math.Sqrt((b.Position.X-middle.X)*(b.Position.X-middle.X)+(b.Position.Z-middle.Z)*(b.Position.Z-middle.Z))<=reach+(b.Combat?.Reach??b.Radius)).ToArray();
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
                double u=from+(to-from)*i/steps;var p=f.Point(u);int hit=Cover(p,radius,solids,wallHeight,bodies,source,true);
                if(hit!=0){var at=p;for(int j=0;j<bodies.Count;j++)if(bodies[j].Id==hit&&bodies[j].Combat.HasValue){at=bodies[j].Combat.Value.Closest(p);break;}return new BallisticContact(u,at,hit>0&&bodies.Any(b=>b.Id==hit&&b.Building)?hit:0,Math.Max(0,hit));}
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
            // Conservative broad phase only: retain the original sample positions,
            // height/footprint predicates and obstacle order. Rounded interpolation
            // can lie just outside endpoint bounds, so widen by the existing epsilon.
            double minX=Math.Min(from.X,to.X)-radius-Eps,maxX=Math.Max(from.X,to.X)+radius+Eps;
            double minZ=Math.Min(from.Z,to.Z)-radius-Eps,maxZ=Math.Max(from.Z,to.Z)+radius+Eps;
            var candidates=new List<NavObstacle>();
            for(int j=0;j<solids.Count;j++){var solid=solids[j];
                if(solid.MaxX>=minX&&solid.MinX<=maxX&&solid.MaxZ>=minZ&&solid.MinZ<=maxZ)candidates.Add(solid);
            }
            for(int i=0;i<=steps;i++){
                double t=(double)i/steps;var p=new BallisticPoint(from.X+(to.X-from.X)*t,from.Y+(to.Y-from.Y)*t,from.Z+(to.Z-from.Z)*t);
                if(p.Y-radius<terrain(p.Ground)-Eps)return false;
                for(int j=0;j<candidates.Count;j++){var solid=candidates[j];
                    if(p.Y+radius>=solid.Bottom&&p.Y-radius<=(double.IsNaN(solid.Top)?wallHeight:solid.Top)&&Overlap(solid,p,radius))return false;
                }
            }
            return true;
        }
        public static bool BlastHits(BallisticPoint impact,double radius,BallisticBody target,IReadOnlyList<NavObstacle> solids,double wallHeight,IReadOnlyList<BallisticBody> bodies,Func<NavPoint,double> terrain=null)
        {
            double x=target.Position.X-impact.X,z=target.Position.Z-impact.Z,y=Math.Max(target.BaseHeight,Math.Min(target.BaseHeight+target.Height,impact.Y));
            // A salvo checks every building for splash. Its articulated bounds can rule out
            // distant buildings without asking for a nearest point on every mesh triangle.
            if(target.Combat.HasValue&&target.Combat.Value.OutsideBlastSquare(impact.Ground,radius+2*Eps))return false;
            if(target.Combat.HasValue){var nearest=target.Combat.Value.Closest(impact);double dx=nearest.X-impact.X,dy0=nearest.Y-impact.Y,dz=nearest.Z-impact.Z;if(dx*dx+dy0*dy0+dz*dz>(radius+Eps)*(radius+Eps))return false;}
            double horizontal=Math.Max(0,Math.Sqrt(x*x+z*z)-target.Radius);
            if(!target.Combat.HasValue&&Math.Sqrt(horizontal*horizontal+(y-impact.Y)*(y-impact.Y))>radius+Eps)return false;
            double destY=Math.Max(target.BaseHeight+Eps,Math.Min(target.BaseHeight+target.Height,impact.Y+Eps)),dy=destY-impact.Y;
            int steps=Math.Max(1,(int)Math.Ceiling(Math.Sqrt(x*x+z*z+dy*dy)/.025));
            for(int i=1;i<steps;i++){double u=(double)i/steps;var p=new BallisticPoint(impact.X+x*u,impact.Y+Eps+dy*u,impact.Z+z*u);if(Cover(p,0,solids,wallHeight,bodies,target.Id)!=0||(terrain!=null&&p.Y<terrain(p.Ground)-Eps))return false;}
            return true;
        }
    }
}
