using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    public sealed class BasaltMaterialPatch
    {
        public BasaltMaterialPatch(double x,double z,double xx,double zz,string material){Bounds=new NavObstacle(x,z,xx,zz);Material=material;}
        public NavObstacle Bounds{get;} public string Material{get;}
    }
    // Immutable authored map content, not new global balance or camera tuning.
    public sealed class BasaltCrownMap : IPlayableTerrain
    {
        public string Id=>"basalt-crown-native-v1";
        public int Revision=>3;
        public double HalfExtent=>56;
        public double DirectFireHeight=>1;
        public int SurfaceSemanticsVersion=>1;
        public const string DisplayName="Базальтовый венец · 1×1";
        public string SourceRevision=>BasaltCrownLayout.SourceRevision;
        public string SourceSha256=>BasaltCrownLayout.SourceSha256;
        public IReadOnlyList<MapSupport> Supports{get;}
        public IReadOnlyList<NavObstacle> Solids{get;}
        public IReadOnlyList<NavObstacle> MovementBlockers{get;}
        public IReadOnlyList<BasaltMaterialPatch> Materials{get;}=Array.AsReadOnly(BasaltCrownLayout.Materials);
        public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions=>locations.Transitions;
        private readonly NavGeometry geometry;
        private readonly TerrainLocations locations;
        public BasaltCrownMap()
        {
            var supports=new List<MapSupport>();var solids=new List<NavObstacle>();
            // Web height=1m, maximum ramp slope=22 degrees; retain both as content.
            double halfRamp=1/Math.Tan(22*Math.PI/180)/2;
            foreach(int side in new[]{-1,1})supports.Add(new MapSupport("citadel-ramp",new NavObstacle(side*22-halfRamp,-4,side*22+halfRamp,4),false,0,new NavPoint(-side/(2*halfRamp),0),new NavPoint(side*(22+halfRamp),0),"ramp/"+side));
            for(int i=0;i<BasaltCrownLayout.Plateau.GetLength(0);i++)supports.Add(new MapSupport("citadel",Box(BasaltCrownLayout.Plateau,i),false,1,surfaceId:"citadel/"+i));
            supports.Add(new MapSupport("outer-ring",new NavObstacle(-56,-48,56,48),false,surfaceId:"outer-ring"));
            for(int i=0;i<BasaltCrownLayout.Ridges.GetLength(0);i++){
                var b=Box(BasaltCrownLayout.Ridges,i);solids.Add(new NavObstacle(b.Footprint,0,BasaltCrownLayout.Ridges[i,4]));
            }
            // Unsupported north/south reserve closes the square navigation domain.
            solids.Add(new NavObstacle(-56,-56,56,-48));solids.Add(new NavObstacle(-56,48,56,56));
            Supports=supports.AsReadOnly();Solids=solids.AsReadOnly();
            var blockers=new List<NavObstacle>(solids);
            // The central cliffs are visible support edges. Zero-thickness segments
            // use a numerical width solely for the geometry's polygon contract.
            const double epsilon=.00001;
            foreach(var s in supports.Where(s=>s.Id=="citadel")){
                var b=s.Bounds;
                AddEdge(new NavPoint(b.MinX,b.MinZ),new NavPoint(b.MaxX,b.MinZ));
                AddEdge(new NavPoint(b.MaxX,b.MinZ),new NavPoint(b.MaxX,b.MaxZ));
                AddEdge(new NavPoint(b.MaxX,b.MaxZ),new NavPoint(b.MinX,b.MaxZ));
                AddEdge(new NavPoint(b.MinX,b.MaxZ),new NavPoint(b.MinX,b.MinZ));
            }
            void AddEdge(NavPoint a,NavPoint b){
                double length=Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Z-a.Z,2));int count=(int)Math.Round(length/2);
                for(int i=0;i<count;i++){
                    var u=new NavPoint(a.X+(b.X-a.X)*i/count,a.Z+(b.Z-a.Z)*i/count);
                    var v=new NavPoint(a.X+(b.X-a.X)*(i+1)/count,a.Z+(b.Z-a.Z)*(i+1)/count);
                    var mid=new NavPoint((u.X+v.X)/2,(u.Z+v.Z)/2);
                    if(supports.Any(r=>r.Id=="citadel-ramp"&&r.Contains(mid)))continue;
                    bool vertical=u.X==v.X;
                    var left=new NavPoint(mid.X+(vertical?-epsilon:0),mid.Z+(vertical?0:-epsilon));
                    var right=new NavPoint(mid.X+(vertical?epsilon:0),mid.Z+(vertical?0:epsilon));
                    if(supports.Any(r=>r.Id=="citadel"&&r.Contains(left))&&supports.Any(r=>r.Id=="citadel"&&r.Contains(right)))continue;
                    blockers.Add(new NavObstacle(Math.Min(u.X,v.X)-epsilon,Math.Min(u.Z,v.Z)-epsilon,Math.Max(u.X,v.X)+epsilon,Math.Max(u.Z,v.Z)+epsilon));
                }
            }
            MovementBlockers=blockers.AsReadOnly();geometry=new NavGeometry(HalfExtent,blockers.ToArray(),Revision);
            // TerrainLocations only authorizes physically incident, equal-height seams.
            // Background overlap never becomes a shortcut across the one-metre cliff.
            var links=new List<NavSurfaceTransition>();
            for(int i=0;i<supports.Count;i++)for(int j=i+1;j<supports.Count;j++)links.Add(new NavSurfaceTransition(supports[i].SurfaceId+"~"+supports[j].SurfaceId,supports[i].SurfaceId,supports[j].SurfaceId));
            locations=new TerrainLocations(geometry,Supports,links);
        }
        private static NavObstacle Box(double[,] a,int i)=>new NavObstacle(a[i,0],a[i,1],a[i,2],a[i,3]);
        public MapSupport SupportAt(NavPoint p)=>Supports.FirstOrDefault(s=>s.Contains(p));
        public double SurfaceHeight(NavPoint p)=>SupportAt(p)?.HeightAt(p)??0;
        public NavPoint SurfaceGradient(NavPoint p)=>SupportAt(p)?.Gradient??default(NavPoint);
        public bool SupportsFootprint(NavPoint p,double radius)=>locations.TryLocate(p,radius,out _);
        public bool SupportsSweep(NavPoint a,NavPoint b,double radius)=>locations.TryLocate(a,radius,out var origin)&&locations.Traverse(origin,b,radius,out _);
        public bool TryLocate(NavPoint p,double r,out NavLocation l)=>locations.TryLocate(p,r,out l);
        public bool IsValidLocation(NavLocation l,double r)=>locations.Valid(l,r);
        public bool TryTraverse(NavLocation from,NavPoint to,double r,out NavLocation l)=>locations.Traverse(from,to,r,out l);
        public bool CompatibleCombatSurface(NavLocation a,NavLocation b)=>locations.Combat(a,b);
        public NavPoint Headquarters(PlayableOwner owner){if((int)owner<0||(int)owner>1)throw new ArgumentOutOfRangeException(nameof(owner));return owner==PlayableOwner.Player?new NavPoint(-40,-32):new NavPoint(40,32);}
        // Native flank mines sit outside the roads; five-slot HQ rings use their validated
        // native orientation, independent of the diagonal Explorer start heading.
        public TerritorySite[] Sites(PlayableProfile p)=>new[]{
            new TerritorySite(1,PlayableBuildingKind.Headquarters,Headquarters(PlayableOwner.Player),p),
            new TerritorySite(2,PlayableBuildingKind.Headquarters,Headquarters(PlayableOwner.Enemy),p,Math.PI),
            new TerritorySite(3,PlayableBuildingKind.Mine,new NavPoint(0,-12),p),
            new TerritorySite(4,PlayableBuildingKind.Mine,new NavPoint(0,14),p),
            new TerritorySite(5,PlayableBuildingKind.Outpost,new NavPoint(40,-32),p,3*Math.PI/4),
            new TerritorySite(6,PlayableBuildingKind.Outpost,new NavPoint(-40,32),p,7*Math.PI/4),
            new TerritorySite(7,PlayableBuildingKind.Mine,new NavPoint(-48,0),p),
            new TerritorySite(8,PlayableBuildingKind.Mine,new NavPoint(48,0),p)};
        public OfflineMatchConfiguration Configuration(PlayableProfile p,int seed,OfflineParticipant[] roster){
            var starts=new[]{new OfflineStart("start-west",1,Headquarters(PlayableOwner.Player),new NavPoint(-40+10/Math.Sqrt(2),-32+10/Math.Sqrt(2)),Math.PI/4,1),new OfflineStart("start-east",2,Headquarters(PlayableOwner.Enemy),new NavPoint(40-10/Math.Sqrt(2),32-10/Math.Sqrt(2)),5*Math.PI/4,2)};
            return new OfflineMatchConfiguration(p,"native-basalt-crown-v3",Id,"native-authored-diagonal-v1",seed,roster,starts,Sites(p),MovementBlockers.ToArray(),new double[2,2],terrain:Id);
        }
    }
}
