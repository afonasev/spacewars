using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    [Serializable] public sealed class MossHighlandProfileData
    {
        public string id="moss-highland-native-v1";public int revision=4;public double outpostOffset=6;
    }
    public sealed class MossHighlandPlacementField
    {
        public string Path=>"maps.mossHighland.outpostOffset";public string Group=>"Moss Highland placement";
        public string Label=>"Side outpost offset";public string Description=>"Move complete side sites outward for the next match; limited by edge cliff exit clearances.";
        public string Unit=>"m";public double Minimum=>0;public double Maximum=>6;public double Step=>1;
        public double Read(MossHighlandProfileData data)=>data.outpostOffset;
        public void Write(MossHighlandProfileData data,double value){if(double.IsNaN(value)||double.IsInfinity(value)||value<Minimum||value>Maximum)throw new ArgumentOutOfRangeException(Path);data.outpostOffset=value;}
    }
    public sealed class MossMaterialPatch
    {
        public MossMaterialPatch(double x,double z,double xx,double zz,string material){Bounds=new NavObstacle(x,z,xx,zz);Material=material;}
        public NavObstacle Bounds{get;} public string Material{get;}
    }
    // Immutable authored map content, not new global balance or camera tuning.
    public sealed class MossHighlandMap : IPlayableTerrain, INavArrivalPointTerrain
    {
        public string Id=>"moss-highland-native-v1";
        public int Revision{get;}
        public double OutpostOffset{get;}
        public static readonly MossHighlandPlacementField PlacementField=new MossHighlandPlacementField();
        public double HalfExtent=>64;
        public double DirectFireHeight=>1;
        public int SurfaceSemanticsVersion=>1;
        public const string DisplayName="Забытое нагорье (4)";
        public string SourceRevision=>MossHighlandLayout.SourceRevision;
        public string SourceSha256=>MossHighlandLayout.SourceSha256;
        public IReadOnlyList<MapSupport> Supports{get;}
        public IReadOnlyList<NavObstacle> Solids{get;}
        public IReadOnlyList<NavObstacle> OriginalRidges{get;}
        // Authored revision-4 content, like MossHighlandLayout; not global balance.
        // Both borders block ground movement. Rivers have no raised combat wall.
        public IReadOnlyList<NavObstacle> BorderMountains{get;}=CreateBorderMountains();
        public IReadOnlyList<NavObstacle> BorderRivers{get;}=CreateBorderRivers();
        private static IReadOnlyList<NavObstacle> CreateBorderMountains(){
            var result=new List<NavObstacle>();
            var corner=new[]{new NavPoint(64,64),new NavPoint(30,64),new NavPoint(34,61),new NavPoint(43,58),new NavPoint(49,56),new NavPoint(53,52),new NavPoint(56,45),new NavPoint(58,37),new NavPoint(61,32),new NavPoint(64,30)};
            foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1})result.Add(new NavObstacle(new NavPolygon(corner.Select(p=>new NavPoint(sx*p.X,sz*p.Z))),0,8));
            return result.AsReadOnly();
        }
        private static IReadOnlyList<NavObstacle> CreateBorderRivers(){
            var west=new[]{new NavPoint(-64,44),new NavPoint(-60,43),new NavPoint(-57,39),new NavPoint(-58,34),new NavPoint(-55,30),new NavPoint(-54,25),new NavPoint(-56,21),new NavPoint(-59,18),new NavPoint(-61,16),new NavPoint(-64,17)};
            return Array.AsReadOnly(new[]{new NavObstacle(new NavPolygon(west),-1,0),new NavObstacle(new NavPolygon(west.Select(p=>new NavPoint(-p.X,-p.Z))),-1,0)});
        }
        public IReadOnlyList<NavObstacle> MovementBlockers{get;}
        public IReadOnlyList<MossMaterialPatch> Materials{get;}=Array.AsReadOnly(MossHighlandLayout.Materials);
        // Presentation-only unions, separate from the unchanged movement solids.
        public IReadOnlyList<NavObstacle> VisualRidges{get;}=Array.AsReadOnly(MossHighlandLayout.Geology);
        public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions=>locations.Transitions;
        private readonly NavGeometry geometry;
        private readonly TerrainLocations locations;
        TerrainLocations INavArrivalPointTerrain.ArrivalLocations=>locations;
        public MossHighlandMap(MossHighlandProfileData settings=null)
        {
            settings=settings??new MossHighlandProfileData();
            if(settings.id!=Id||settings.revision<4)throw new ArgumentException("Invalid Moss Highland profile");
            PlacementField.Write(settings,PlacementField.Read(settings));Revision=settings.revision;OutpostOffset=settings.outpostOffset;
            var supports=new List<MapSupport>();var solids=new List<NavObstacle>();
            // Web height=1m, maximum ramp slope=22 degrees; retain both as content.
            double halfRamp=1/Math.Tan(22*Math.PI/180)/2;
            foreach(int side in new[]{-1,1}){
                supports.Add(new MapSupport("plateau-ramp",new NavObstacle(side*12-halfRamp,-4,side*12+halfRamp,4),false,0,new NavPoint(-side/(2*halfRamp),0),new NavPoint(side*(12+halfRamp),0),"ramp/x/"+side));
                supports.Add(new MapSupport("plateau-ramp",new NavObstacle(-4,side*12-halfRamp,4,side*12+halfRamp),false,0,new NavPoint(0,-side/(2*halfRamp)),new NavPoint(0,side*(12+halfRamp)),"ramp/z/"+side));
            }
            for(int i=0;i<MossHighlandLayout.Plateau.GetLength(0);i++)supports.Add(new MapSupport("plateau",Box(MossHighlandLayout.Plateau,i),false,1,surfaceId:"plateau/"+i));
            supports.Add(new MapSupport("outer-ring",new NavObstacle(-64,-64,64,64),false,surfaceId:"outer-ring"));
            for(int i=0;i<MossHighlandLayout.Ridges.GetLength(0);i++){
                var b=Box(MossHighlandLayout.Ridges,i);solids.Add(new NavObstacle(b.Footprint,0,MossHighlandLayout.Ridges[i,4]));
            }
            OriginalRidges=solids.ToArray();solids.AddRange(BorderMountains);
            Supports=supports.AsReadOnly();Solids=solids.AsReadOnly();
            var blockers=new List<NavObstacle>(solids);blockers.AddRange(BorderRivers);
            // The central cliffs are visible support edges. Zero-thickness segments
            // use a numerical width solely for the geometry's polygon contract.
            const double epsilon=.00001;
            foreach(var s in supports.Where(s=>s.Id=="plateau")){
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
                    if(supports.Any(r=>r.Id=="plateau-ramp"&&r.Contains(mid)))continue;
                    bool vertical=u.X==v.X;
                    var left=new NavPoint(mid.X+(vertical?-epsilon:0),mid.Z+(vertical?0:-epsilon));
                    var right=new NavPoint(mid.X+(vertical?epsilon:0),mid.Z+(vertical?0:epsilon));
                    if(supports.Any(r=>r.Id=="plateau"&&r.Contains(left))&&supports.Any(r=>r.Id=="plateau"&&r.Contains(right)))continue;
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
        public MapSupport SupportAt(NavPoint p)
        {
            // Height queries run at every ballistic sample. Preserve authored support
            // precedence without allocating a capturing predicate/enumerator per point.
            for(int i=0;i<Supports.Count;i++){var support=Supports[i];if(support.Contains(p))return support;}
            return null;
        }
        public double SurfaceHeight(NavPoint p)=>SupportAt(p)?.HeightAt(p)??0;
        public NavPoint SurfaceGradient(NavPoint p)=>SupportAt(p)?.Gradient??default(NavPoint);
        public bool SupportsFootprint(NavPoint p,double radius)=>locations.TryLocate(p,radius,out _);
        public bool SupportsSweep(NavPoint a,NavPoint b,double radius)=>locations.TryLocate(a,radius,out var origin)&&locations.Traverse(origin,b,radius,out _);
        public bool TryLocate(NavPoint p,double r,out NavLocation l)=>locations.TryLocate(p,r,out l);
        public bool IsValidLocation(NavLocation l,double r)=>locations.Valid(l,r);
        public bool TryTraverse(NavLocation from,NavPoint to,double r,out NavLocation l)=>locations.Traverse(from,to,r,out l);
        public bool CompatibleCombatSurface(NavLocation a,NavLocation b)=>locations.Combat(a,b);
        public NavPoint Headquarters(PlayableOwner owner){int i=(int)owner;if(i<0||i>=4)throw new ArgumentOutOfRangeException(nameof(owner));return new NavPoint(MossHighlandLayout.Sites[i,0],MossHighlandLayout.Sites[i,1]);}
        public TerritorySite[] Sites(PlayableProfile p){
            var sites=new TerritorySite[13];
            for(int i=0;i<sites.Length;i++){
                double x=MossHighlandLayout.Sites[i,0],z=MossHighlandLayout.Sites[i,1];
                // Native revision 3 moves complete side sites; retained web source stays immutable.
                if(i>=8&&i<12){x+=Math.Sign(x)*OutpostOffset;z+=Math.Sign(z)*OutpostOffset;}
                sites[i]=new TerritorySite(i+1,i<4?PlayableBuildingKind.Headquarters:i<8?PlayableBuildingKind.Mine:PlayableBuildingKind.Outpost,new NavPoint(x,z),p,(i>=8?90:MossHighlandLayout.Sites[i,2])*Math.PI/180);
            }
            return sites;
        }
        public OfflineMatchConfiguration Configuration(PlayableProfile p,int seed,OfflineParticipant[] roster){
            var starts=Enumerable.Range(0,4).Select(i=>{var home=Headquarters((PlayableOwner)i);return new OfflineStart("start-"+(i+1),i+1,home,new NavPoint(home.X,home.Z+Math.Sign(home.Z)*10),Math.Atan2(-home.Z,-home.X));}).ToArray();
            // Costs are measured on checked native routes, never guessed across cliffs.
            var router=new SharedFlowRouter(geometry,p.Navigation);var costs=new double[4,4];
            for(int a=0;a<4;a++)for(int b=a+1;b<4;b++){
                var route=router.FindPath(starts[a].Position,starts[b].Position);if(route.Length==0)throw new InvalidOperationException("Disconnected highland starts");
                double cost=0;var before=starts[a].Position;foreach(var next in route){if(!SupportsSweep(before,next,p.Navigation.Radius))throw new InvalidOperationException("Unsupported start route");cost+=Math.Sqrt(Math.Pow(next.X-before.X,2)+Math.Pow(next.Z-before.Z,2));before=next;}costs[a,b]=costs[b,a]=cost;
            }
            return new OfflineMatchConfiguration(p,"native-moss-highland-v1",Id,"native-checked-ground-routes-v1",seed,roster,starts,Sites(p),MovementBlockers.ToArray(),costs,terrain:Id);
        }
    }
}
