using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    [Serializable] public sealed class NullDockProfileData
    {
        public string id="null-dock-a04-v1"; public int revision=2;
        public double terraceHeight=2, moduleHeight=6;
    }
    public sealed class NullDockField
    {
        private readonly System.Reflection.FieldInfo field;
        public string Path{get;} public string Group=>"Null Dock terrain"; public string Label{get;} public string Description{get;}
        public string Unit=>"m"; public double Minimum{get;} public double Maximum{get;} public double Step=>.25;
        public NullDockField(string key,string label,string description,double min,double max){field=typeof(NullDockProfileData).GetField(key);Path="maps.nullDock."+key;Label=label;Description=description;Minimum=min;Maximum=max;}
        public double Read(NullDockProfileData p)=>(double)field.GetValue(p);
        public void Write(NullDockProfileData p,double value){if(double.IsNaN(value)||double.IsInfinity(value)||value<Minimum||value>Maximum)throw new ArgumentOutOfRangeException(Path);field.SetValue(p,value);}
    }
    /// <summary>Approved A–04 station. Authored horizontal coordinates are content, not tunable balance.</summary>
    public sealed class NullDockMap:IPlayableTerrain,INavArrivalPointTerrain
    {
        public const string DisplayName="Нулевой док (2)";
        public static readonly IReadOnlyList<NullDockField> Fields=Array.AsReadOnly(new[]{
            new NullDockField("terraceHeight","Raised deck height","Height of home and outpost decks and their connecting ramps.",1,4),
            new NullDockField("moduleHeight","Technical module height","Solid and visible height of sealed station modules.",4,10)});
        public string Id=>"null-dock-a04-v1"; public int Revision{get;}
        public double TerraceHeight{get;} public double ModuleHeight{get;}
        public double HalfExtent=>96; public double DirectFireHeight=>1; public int SurfaceSemanticsVersion=>1;
        public IReadOnlyList<MapSupport> Supports{get;} public IReadOnlyList<NavObstacle> Solids{get;} public IReadOnlyList<NavObstacle> MovementBlockers{get;}
        public IReadOnlyList<NavObstacle> Modules{get;} public IReadOnlyList<NavObstacle> Space{get;}
        public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions=>locations.Transitions;
        private readonly TerrainLocations locations;
        TerrainLocations INavArrivalPointTerrain.ArrivalLocations=>locations;
        // Schematic origin is top left: 192 m across, 160 m down.
        public NavPoint Point(double x,double y)=>new NavPoint(x-96,80-y);
        public NullDockMap(NullDockProfileData data=null)
        {
            data=data??new NullDockProfileData();if(data.id!=Id||data.revision<1)throw new ArgumentException("Invalid Null Dock profile");
            foreach(var field in Fields)field.Write(data,field.Read(data));Revision=data.revision;TerraceHeight=data.terraceHeight;ModuleHeight=data.moduleHeight;
            var supports=new List<MapSupport>();
            void Ramp(string id,double x,double y,double xx,double yy,double lowX,double lowY,double highX,double highY){
                var lo=Point(lowX,lowY);var hi=Point(highX,highY);double dx=hi.X-lo.X,dz=hi.Z-lo.Z,d2=dx*dx+dz*dz;
                supports.Add(new MapSupport(id,Box(x,y,xx,yy),false,0,new NavPoint(dx*TerraceHeight/d2,dz*TerraceHeight/d2),lo,id));
            }
            Ramp("R1",24,52,36,60,30,52,30,60);
            Ramp("R2",24,100,36,108,30,108,30,100);
            Ramp("R3",48,68,56,92,56,80,48,80);
            Ramp("R4",156,52,168,60,162,52,162,60);
            Ramp("R5",156,100,168,108,162,108,162,100);
            Ramp("R6",136,68,144,92,136,80,144,80);
            Ramp("R7",88,20,96,32,88,26,96,26);
            Ramp("R8",156,18,164,30,164,24,156,24);
            Ramp("R9",96,128,104,140,104,134,96,134);
            Ramp("R10",28,130,36,142,28,136,36,136);
            supports.Add(new MapSupport("H-A",Box(14,60,48,100),false,TerraceHeight,surfaceId:"H-A"));
            supports.Add(new MapSupport("H-B",Box(144,60,178,100),false,TerraceHeight,surfaceId:"H-B"));
            supports.Add(new MapSupport("F-N",Box(96,0,156,52),false,TerraceHeight,surfaceId:"F-N"));
            supports.Add(new MapSupport("F-S",Box(36,108,96,160),false,TerraceHeight,surfaceId:"F-S"));
            supports.Add(new MapSupport("station-deck",Polygon(new double[]{14,14,96,14,96,0,192,0,192,52,178,52,178,146,96,146,96,160,0,160,0,108,14,108},0),false,surfaceId:"station-deck"));
            var modules=new[]{
                Polygon(new double[]{60,36,72,36,84,48,84,60,72,60,60,48},ModuleHeight),
                Polygon(new double[]{132,124,120,124,108,112,108,100,120,100,132,112},ModuleHeight),
                Polygon(new double[]{100,62,108,66,92,98,84,94},ModuleHeight),
                Polygon(new double[]{0,108,18,108,18,114,0,114},ModuleHeight),
                Polygon(new double[]{174,46,192,46,192,52,174,52},ModuleHeight),
                Polygon(new double[]{36,100,48,100,48,108,36,108},ModuleHeight),
                Polygon(new double[]{144,52,156,52,156,60,144,60},ModuleHeight)};
            var space=new[]{Box(0,0,96,14),Box(0,14,14,108),Box(96,146,192,160),Box(178,52,192,146),Box(0,-16,192,0),Box(0,160,192,176)};
            Supports=supports.AsReadOnly();Modules=Array.AsReadOnly(modules);Space=Array.AsReadOnly(space);
            var solids=modules.Concat(space).ToArray();Solids=Array.AsReadOnly(solids);
            var links=new List<NavSurfaceTransition>();
            for(int i=0;i<supports.Count;i++)for(int j=i+1;j<supports.Count;j++)links.Add(new NavSurfaceTransition(supports[i].SurfaceId+"~"+supports[j].SurfaceId,supports[i].SurfaceId,supports[j].SurfaceId));
            var partition=new TerrainLocations(new NavGeometry(HalfExtent,solids,Revision),Supports,links);
            var blockers=solids.ToList();const double epsilon=.0001; // Numeric seam thickness, greater than squared-edge tolerance.
            foreach(var seam in partition.Seams.Where(seam=>!seam.Allowed)){
                var a=seam.From;var b=seam.To;double dx=b.X-a.X,dz=b.Z-a.Z,len=Math.Sqrt(dx*dx+dz*dz);if(len<epsilon)continue;
                double nx=-dz/len*epsilon,nz=dx/len*epsilon;
                blockers.Add(new NavObstacle(new NavPolygon(new[]{new NavPoint(a.X+nx,a.Z+nz),new NavPoint(b.X+nx,b.Z+nz),new NavPoint(b.X-nx,b.Z-nz),new NavPoint(a.X-nx,a.Z-nz)})));
            }
            MovementBlockers=blockers.AsReadOnly();locations=new TerrainLocations(new NavGeometry(HalfExtent,blockers.ToArray(),Revision),Supports,links);
        }
        private NavObstacle Box(double x,double y,double xx,double yy)=>new NavObstacle(x-96,80-yy,xx-96,80-y);
        private NavObstacle Polygon(double[] xy,double height)=>new NavObstacle(new NavPolygon(Enumerable.Range(0,xy.Length/2).Select(i=>Point(xy[i*2],xy[i*2+1]))),0,height);
        public MapSupport SupportAt(NavPoint p){for(int i=0;i<Supports.Count;i++)if(Supports[i].Contains(p))return Supports[i];return null;}
        public double SurfaceHeight(NavPoint p)=>SupportAt(p)?.HeightAt(p)??0;public NavPoint SurfaceGradient(NavPoint p)=>SupportAt(p)?.Gradient??default(NavPoint);
        public bool SupportsFootprint(NavPoint p,double r)=>locations.TryLocate(p,r,out _);
        public bool SupportsSweep(NavPoint a,NavPoint b,double r)=>locations.TryLocate(a,r,out var start)&&locations.Traverse(start,b,r,out _);
        public bool TryLocate(NavPoint p,double r,out NavLocation l)=>locations.TryLocate(p,r,out l);
        public bool IsValidLocation(NavLocation l,double r)=>locations.Valid(l,r);
        public bool TryTraverse(NavLocation l,NavPoint p,double r,out NavLocation end)=>locations.Traverse(l,p,r,out end);
        public bool CompatibleCombatSurface(NavLocation a,NavLocation b)=>locations.Combat(a,b);
        public NavPoint Headquarters(PlayableOwner owner){if(owner!=PlayableOwner.Player&&owner!=PlayableOwner.Enemy)throw new ArgumentOutOfRangeException(nameof(owner));return Point(owner==PlayableOwner.Player?30:162,80);}
        public TerritorySite[] Sites(PlayableProfile p)=>new[]{
            new TerritorySite(1,PlayableBuildingKind.Headquarters,Point(30,80),p,0),
            new TerritorySite(2,PlayableBuildingKind.Headquarters,Point(162,80),p,Math.PI),
            new TerritorySite(3,PlayableBuildingKind.Outpost,Point(124,30),p,-Math.PI/2),
            new TerritorySite(4,PlayableBuildingKind.Outpost,Point(68,130),p,-Math.PI/2),
            new TerritorySite(5,PlayableBuildingKind.Mine,Point(12,146),p),
            new TerritorySite(6,PlayableBuildingKind.Mine,Point(180,14),p),
            new TerritorySite(7,PlayableBuildingKind.Mine,Point(22,22),p),
            new TerritorySite(8,PlayableBuildingKind.Mine,Point(170,138),p),
            new TerritorySite(9,PlayableBuildingKind.Mine,Point(82,72),p),
            new TerritorySite(10,PlayableBuildingKind.Mine,Point(110,88),p)
        };
        public OfflineMatchConfiguration Configuration(PlayableProfile p,int seed,OfflineParticipant[] roster){
            if(roster.Length!=2||roster[0].Team==roster[1].Team)throw new ArgumentException("Null Dock requires two hostile participants.");
            var starts=new[]{new OfflineStart("dock-A",1,Point(30,80),Point(30,66),0,roster[0].LogicalPlayer),new OfflineStart("dock-B",2,Point(162,80),Point(162,94),Math.PI,roster[1].LogicalPlayer)};
            return new OfflineMatchConfiguration(p,"native-null-dock-a04-v1",Id,"authored-station-a04",seed,roster,starts,Sites(p),MovementBlockers.ToArray(),new double[2,2],terrain:Id);
        }
    }
}
