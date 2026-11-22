using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Layout coordinates follow the approved overhead plan, with north at the top.
    public sealed class DeafMirrorMap : IPlayableTerrain, INavArrivalPointTerrain
    {
        public const string DisplayName = "Море Молчания (8)";
        public string Id => "deaf-mirror-v1";
        public int Revision => 2;
        public double HalfExtent => 224;
        public double DirectFireHeight => 1;
        public int SurfaceSemanticsVersion => 1;
        public static NavPoint Plan(double x, double y) => new NavPoint((x-600)*.4,(350-y)*.4);
        public IReadOnlyList<MapSupport> Supports { get; }
        public IReadOnlyList<NavObstacle> Solids { get; }
        public IReadOnlyList<NavObstacle> MovementBlockers { get; }
        public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions => locations.Transitions;
        TerrainLocations INavArrivalPointTerrain.ArrivalLocations => locations;
        readonly TerrainLocations locations;
        readonly List<NavObstacle> ground = new List<NavObstacle>(), cuts = new List<NavObstacle>(),
            bridges = new List<NavObstacle>(), rocks = new List<NavObstacle>(), facilities = new List<NavObstacle>();
        public IReadOnlyList<NavObstacle> Ground => ground;
        public IReadOnlyList<NavObstacle> Cuts => cuts;
        public IReadOnlyList<NavObstacle> Bridges => bridges;
        public IReadOnlyList<NavObstacle> Rocks => rocks;
        public IReadOnlyList<NavObstacle> Facilities => facilities;

        static NavObstacle Box(double x,double y,double w,double h,double height=0)
        {
            var p=Plan(x,y);
            var box=new NavObstacle(p.X-w*.2,p.Z-h*.2,p.X+w*.2,p.Z+h*.2);
            return height==0?box:new NavObstacle(box.Footprint,0,height);
        }

        public DeafMirrorMap()
        {
            // Cross sections (plan x, north bank, south bank). A deck exactly replaces
            // one slice of the void; its usable width is the distance between the cuts.
            int[,] main={{40,220,220},{258,220,220},{300,197,225},{345,205,239},{397,192,230},
                {472,199,235},{515,209,229},{559,194,241},{615,204,249},{666,185,230},
                {705,204,246},{752,198,231},{785,210,241},{819,196,223},{879,204,227},{942,218,218},{1160,218,218}};
            int[,] inner={{40,365,365},{453,372,372},{469,355,369},{480,357,365},{493,344,358},
                {503,345,349},{525,336,336},{600,336,336}};
            foreach(bool rotate in new[]{false,true}){
                Band(main,77,260,rotate,true);
                Band(inner,260,440,rotate,false);
            }
            foreach(var p in new[]{(339,338),(481,146),(310,158),(898,133),(560,273)}){
                foreach(bool rotate in new[]{false,true}){
                    var outline=new[]{(-17,3),(-11,-7),(1,-10),(5,-6),(13,-8),(20,2),(8,8),(-7,9)};
                    rocks.Add(new NavObstacle(new NavPolygon(outline.Select(v=>Point(p.Item1+v.Item1,p.Item2+v.Item2,rotate))),0,3));
                }
            }
            foreach(int x in new[]{71,1129})foreach(int y in new[]{100,238,462,600})
                facilities.Add(Box(x,y,32,23,4));
            var outer=Box(600,350,1120,546);
            var supports=new List<MapSupport>();
            var transitions=new List<NavSurfaceTransition>();
            foreach(int side in new[]{-1,1}){
                AddHill(supports,transitions,side*40,side*82,25,16,1.65);
                AddHill(supports,transitions,side*131,-side*14,20,15,1.35);
            }
            supports.Add(new MapSupport("lunar-plain",outer,false,surfaceId:"lunar-plain"));
            Supports=supports.AsReadOnly();
            Solids=rocks.Concat(facilities).ToArray();
            var limit=outer.MaxZ;
            MovementBlockers=Solids.Concat(cuts).Concat(new[]{
                new NavObstacle(-HalfExtent,limit,HalfExtent,HalfExtent),
                new NavObstacle(-HalfExtent,-HalfExtent,HalfExtent,-limit)}).ToArray();
            locations=new TerrainLocations(new NavGeometry(HalfExtent,MovementBlockers.ToArray(),Revision),Supports,transitions);
        }
        // Nested contour lines give each low mound a gentle crown. Adjacent pieces
        // share exact edge points. They feed both the navigation partition and the mesh.
        static void AddHill(List<MapSupport> supports,List<NavSurfaceTransition> links,
            double x,double z,double radiusX,double radiusZ,double height)
        {
            const int sides=6;
            var center=new NavPoint(x,z);
            var rim=new NavPoint[sides];
            var shoulder=new NavPoint[sides];
            string prefix="rise-"+supports.Count;
            for(int i=0;i<sides;i++){
                double theta=i*Math.PI*2/sides;
                double dx=radiusX*Math.Cos(theta),dz=radiusZ*Math.Sin(theta);
                rim[i]=new NavPoint(x+dx,z+dz);
                shoulder[i]=new NavPoint(x+.46*dx,z+.46*dz);
            }
            void Link(string a,string b)=>links.Add(new NavSurfaceTransition(a+"~"+b,a,b));
            void Face(string id,NavPoint[] points,double h0,double h1,double h2){
                var a=points[0];var b=points[1];var c=points[2];
                double bx=b.X-a.X,bz=b.Z-a.Z,cx=c.X-a.X,cz=c.Z-a.Z,det=bx*cz-cx*bz;
                var gradient=new NavPoint(((h1-h0)*cz-(h2-h0)*bz)/det,
                    (bx*(h2-h0)-cx*(h1-h0))/det);
                supports.Add(new MapSupport("regolith-rise",new NavObstacle(new NavPolygon(points)),
                    false,h0,gradient,a,id));
            }
            for(int i=0;i<sides;i++){
                int next=(i+1)%sides;
                string lower=prefix+"-lower-"+i,upper=prefix+"-upper-"+i;
                Face(lower,new[]{rim[i],rim[next],shoulder[next],shoulder[i]},0,0,height*.78);
                Face(upper,new[]{shoulder[i],shoulder[next],center},height*.78,height*.78,height);
                Link(lower,"lunar-plain");Link(lower,upper);
                Link(lower,prefix+"-lower-"+next);Link(upper,prefix+"-upper-"+next);
            }
        }
        static NavPoint Point(double x,double y,bool rotate) => rotate?Plan(1200-x,700-y):Plan(x,y);
        void Band(int[,] rows,int top,int bottom,bool rotate,bool hasDeck)
        {
            for(int i=1;i<rows.GetLength(0);i++){
                int x=rows[i-1,0],u=rows[i,0],a=rows[i-1,1],b=rows[i,1],c=rows[i-1,2],d=rows[i,2];
                NavObstacle Slice(int leftTop,int rightTop,int leftBottom,int rightBottom){
                    var vertices=new[]{Point(x,leftTop,rotate),Point(u,rightTop,rotate),Point(u,rightBottom,rotate),Point(x,leftBottom,rotate)};
                    return new NavObstacle(new NavPolygon(vertices.Distinct()),-8,0);
                }
                ground.Add(Slice(top,top,a,b));ground.Add(Slice(c,d,bottom,bottom));
                if(a==c&&b==d)continue;
                var gap=Slice(a,b,c,d);
                if(hasDeck&&x==397)bridges.Add(gap);else cuts.Add(gap);
            }
        }
        public MapSupport SupportAt(NavPoint p){
            for(int i=0;i<cuts.Count;i++)if(cuts[i].Contains(p))return null;
            return locations.Owner(p);
        }
        public double SurfaceHeight(NavPoint p)=>SupportAt(p)?.HeightAt(p)??-8;
        public NavPoint SurfaceGradient(NavPoint p)=>SupportAt(p)?.Gradient??default;
        public bool SupportsFootprint(NavPoint p,double r)=>locations.TryLocate(p,r,out _);
        public bool SupportsSweep(NavPoint a,NavPoint b,double r)=>locations.TryLocate(a,r,out var start)&&locations.Traverse(start,b,r,out _);
        public bool TryLocate(NavPoint p,double r,out NavLocation l)=>locations.TryLocate(p,r,out l);
        public bool IsValidLocation(NavLocation l,double r)=>locations.Valid(l,r);
        public bool TryTraverse(NavLocation l,NavPoint p,double r,out NavLocation end)=>locations.Traverse(l,p,r,out end);
        public bool CompatibleCombatSurface(NavLocation a,NavLocation b)=>locations.Combat(a,b);
        static readonly int[] Rows={143,300,400,557};
        static int[] RowsForTeam(int count,int center,int corner){
            switch(count){
                case 1:return new[]{center};
                case 2:return new[]{center,3-center};
                case 3:return new[]{corner,3-corner,center};
                case 4:return new[]{center,3-center,corner,3-corner};
                default:throw new ArgumentOutOfRangeException(nameof(count));
            }
        }

        public int[] StartingSites(IReadOnlyList<OfflineParticipant> roster,int seed,int layoutVersion=4){
            var teams=roster.GroupBy(p=>p.Team).OrderBy(g=>g.Key)
                .Select(g=>g.OrderBy(p=>p.LogicalPlayer).ToArray()).ToArray();
            if(teams.Length!=2||roster.Any(p=>p.Team<1||p.Team>2)||teams.Any(team=>team.Length>4))
                throw new ArgumentException("Море Молчания рассчитано на две команды по 1–4 игрока.");
            var random=new Random(seed);
            int firstBank=random.Next(2),firstCenter=1+random.Next(2),firstCorner=3*random.Next(2);
            bool singleCenters=teams.All(team=>team.Length==1||team.Length==3);
            int secondCenter=singleCenters||layoutVersion<4?3-firstCenter:1+random.Next(2);
            int secondCorner=layoutVersion<4?3-firstCorner:3*random.Next(2);
            var sites=new Dictionary<int,int>();
            for(int t=0;t<2;t++){
                var team=teams[t];
                int center=t==0?firstCenter:secondCenter,corner=t==0?firstCorner:secondCorner;
                // Historical three-player starts used the first three seats of a full bank.
                bool historicalTrio=team.Length==3&&(layoutVersion==2||layoutVersion==3&&roster.Count!=6);
                var rows=RowsForTeam(historicalTrio?4:team.Length,center,corner);
                for(int seat=0;seat<team.Length;seat++)
                    sites.Add(team[seat].LogicalPlayer,1+4*(firstBank^t)+rows[seat]);
            }
            return roster.Select(p=>sites[p.LogicalPlayer]).ToArray();
        }

        public NavPoint Headquarters(PlayableOwner owner){
            int i=(int)owner;
            if(i<0||i>=8)throw new ArgumentOutOfRangeException(nameof(owner));
            return Plan(i<4?92:1108,Rows[i%4]);
        }
        public TerritorySite[] Sites(PlayableProfile profile){
            var sites=new List<TerritorySite>();
            void Add(PlayableBuildingKind kind,NavPoint p)=>sites.Add(new TerritorySite(sites.Count+1,kind,p,profile));
            for(int i=0;i<8;i++)Add(PlayableBuildingKind.Headquarters,Headquarters((PlayableOwner)i));
            foreach(var p in new[]{(209,211),(209,489),(435,413)})foreach(bool rotate in new[]{false,true})
                Add(PlayableBuildingKind.Outpost,Point(p.Item1,p.Item2,rotate));
            foreach(int x in new[]{175,1025})foreach(int y in Rows)Add(PlayableBuildingKind.Mine,Plan(x,y));
            foreach(var p in new[]{(388,112),(812,112),(506,301)})foreach(bool rotate in new[]{false,true})
                Add(PlayableBuildingKind.Mine,Point(p.Item1,p.Item2,rotate));
            foreach(int y in new[]{146,350,554})Add(PlayableBuildingKind.RelayTower,Plan(600,y));
            return sites.ToArray();
        }
    }
}
