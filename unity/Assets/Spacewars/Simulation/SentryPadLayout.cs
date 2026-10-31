using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    // One radius per base. Individual plots move along it; enlarged profile constraints
    // may expand the complete ring together, never a single plot.
    // Candidate angles are analytic envelope intersections, not a tunable search grid.
    internal static class SentryPadLayout
    {
        readonly struct Rect
        {
            internal readonly double X0,X1,Z0,Z1,Margin;
            internal Rect(double x0,double x1,double z0,double z1,double margin=0){X0=x0;X1=x1;Z0=z0;Z1=z1;Margin=margin;}
            internal bool Contains(NavPoint p)
            {
                if(Margin==0)return p.X>X0&&p.X<X1&&p.Z>Z0&&p.Z<Z1;
                double dx=Math.Max(X0-p.X,Math.Max(0,p.X-X1)),dz=Math.Max(Z0-p.Z,Math.Max(0,p.Z-Z1));
                return dx*dx+dz*dz<Margin*Margin;
            }
        }
        static double Positive(double a){while(a<0)a+=Math.PI*2;while(a>=Math.PI*2)a-=Math.PI*2;return a;}
        internal static TerritorySlot[] Create(TerritorySite site,PlayableProfile p)
        {
            if(site.Slots.Count<2)return Array.Empty<TerritorySlot>();
            double minimum=site.Slots.Average(slot=>Math.Sqrt(Math.Pow(slot.Position.X-site.Position.X,2)+Math.Pow(slot.Position.Z-site.Position.Z,2)))+p.TurretPadOffset;
            if(TryCreate(site,p,minimum,out var slots))return slots;
            // Retain the previous layout's outer search envelope, but apply it to the
            // whole ring. Refinement is numerical precision, not a balance control.
            double lower=minimum,upper=minimum+OrdinaryRadius(p);
            if(!TryCreate(site,p,upper,out slots))throw new ArgumentException("No supported common defensive ring fits base "+site.Id);
            const int refinementSteps=48; // Sub-nanometre precision over the bounded metre-scale envelope.
            for(int step=0;step<refinementSteps;step++)
            {
                double middle=(lower+upper)/2;
                if(TryCreate(site,p,middle,out var candidate)){upper=middle;slots=candidate;}else lower=middle;
            }
            return slots;
        }
        static double OrdinaryRadius(PlayableProfile p)=>Math.Max(p.HangarFootprintRadius,Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius)));
        static bool TryCreate(TerritorySite site,PlayableProfile p,double ring,out TerritorySlot[] slots)
        {
            slots=null;
            double radius=p.TurretFootprintRadius,ordinary=OrdinaryRadius(p);
            double moving=Math.Max(p.TankCollisionRadius,Math.Max(p.ExplorerCollisionRadius,p.ShkvalCollisionRadius));
            var forbidden=new List<Rect>();var result=new TerritorySlot[site.Slots.Count];
            void Block(NavPoint center,double half,double margin)=>forbidden.Add(new Rect(center.X-half,center.X+half,center.Z-half,center.Z+half,margin));
            Block(site.Position,TerritoryRules.Radius(p,site.Kind)+radius,2*p.TankCollisionRadius);
            foreach(var slot in site.Slots)
            {
                // The Minkowski sum of the two square foundations has rounded clearance
                // corners. An expanded square incorrectly pushes diagonal plots outward.
                Block(slot.Position,ordinary+radius,Math.Max(2*p.TankCollisionRadius,p.TurretPassageWidth));
                double width=radius+moving;
                forbidden.Add(new Rect(slot.Position.X-width,slot.Position.X+width,slot.Position.Z-p.FactoryExitDistance-width,slot.Position.Z));
            }
            var ground=new NavGeometry(p.ArenaHalfExtent,PlayableMap.StaticObstacles(p),1);
            for(int i=0;i<site.Slots.Count;i++)
            {
                var a=site.Slots[i].Position;var b=site.Slots[(i+1)%site.Slots.Count].Position;
                double ax=a.X-site.Position.X,az=a.Z-site.Position.Z,bx=b.X-site.Position.X,bz=b.Z-site.Position.Z;
                double start=Math.Atan2(az,ax),span=Positive(Math.Atan2(bz,bx)-start),angle=start+span/2;
                var nominal=new NavPoint(site.Position.X+Math.Cos(angle)*ring,site.Position.Z+Math.Sin(angle)*ring);
                // Epsilon only separates a closed boundary from floating point rounding.
                const double epsilon=1e-6;
                var cuts=new List<double>{0,span,span/2};
                void Cut(double t){double local=Positive(t-start);if(local<span)cuts.Add(local);}
                void Vertical(double x)
                {
                    double dx=x-site.Position.X;if(Math.Abs(dx)>ring)return;
                    double z=Math.Sqrt(Math.Max(0,ring*ring-dx*dx));Cut(Math.Atan2(z,dx));Cut(Math.Atan2(-z,dx));
                }
                void Horizontal(double z)
                {
                    double dz=z-site.Position.Z;if(Math.Abs(dz)>ring)return;
                    double x=Math.Sqrt(Math.Max(0,ring*ring-dz*dz));Cut(Math.Atan2(dz,x));Cut(Math.Atan2(dz,-x));
                }
                void Corner(double x,double z,double margin)
                {
                    if(margin==0)return;
                    double dx=x-site.Position.X,dz=z-site.Position.Z,d=Math.Sqrt(dx*dx+dz*dz);
                    if(d==0||d>ring+margin||d<Math.Abs(ring-margin))return;
                    double along=(ring*ring-margin*margin+d*d)/(2*d),across=Math.Sqrt(Math.Max(0,ring*ring-along*along));
                    Cut(Math.Atan2((along*dz+across*dx)/d,(along*dx-across*dz)/d));
                    Cut(Math.Atan2((along*dz-across*dx)/d,(along*dx+across*dz)/d));
                }
                foreach(var r in forbidden)
                {
                    Vertical(r.X0-r.Margin);Vertical(r.X1+r.Margin);Horizontal(r.Z0-r.Margin);Horizontal(r.Z1+r.Margin);
                    Corner(r.X0,r.Z0,r.Margin);Corner(r.X0,r.Z1,r.Margin);Corner(r.X1,r.Z0,r.Margin);Corner(r.X1,r.Z1,r.Margin);
                }
                var boundaries=cuts.Distinct().OrderBy(t=>t).ToArray();
                var candidates=new List<double>{span/2};
                for(int j=0;j<boundaries.Length;j++)
                {
                    candidates.Add(boundaries[j]);candidates.Add(boundaries[j]-epsilon);candidates.Add(boundaries[j]+epsilon);
                    if(j>0)candidates.Add((boundaries[j-1]+boundaries[j])/2);
                }
                NavPoint chosen=default;double best=double.PositiveInfinity;
                foreach(double local in candidates)
                {
                    if(local<0||local>=span)continue;
                    double x=site.Position.X+Math.Cos(start+local)*ring,z=site.Position.Z+Math.Sin(start+local)*ring;
                    double dx=x-nominal.X,dz=z-nominal.Z,score=dx*dx+dz*dz;if(score>=best)continue;
                    var candidate=new NavPoint(x,z);
                    if(forbidden.Any(r=>r.Contains(candidate)))continue;
                    if(!ground.IsFree(candidate,radius*(p.AuthoredMap==null?1:Math.Sqrt(2))+p.TankCollisionRadius))continue;
                    if(p.AuthoredMap!=null)
                    {
                        double y=p.AuthoredMap.SurfaceHeight(candidate);bool supported=true;
                        foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1}){var corner=new NavPoint(x+sx*radius,z+sz*radius);var surface=p.AuthoredMap.SupportAt(corner);if(surface==null||Math.Abs(surface.HeightAt(corner)-y)>1e-9||surface.Gradient.X!=0||surface.Gradient.Z!=0)supported=false;}
                        if(!supported)continue;
                    }
                    chosen=candidate;best=score;
                }
                if(double.IsPositiveInfinity(best))return false;
                result[i]=new TerritorySlot(TerritoryRules.DefenseSlotOffset+i+1,chosen,angle);
                Block(chosen,radius*2,2*p.TankCollisionRadius);
            }
            slots=result;return true;
        }
    }
}
