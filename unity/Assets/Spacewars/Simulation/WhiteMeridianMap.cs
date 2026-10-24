using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    [Serializable] public sealed class WhiteMeridianProfileData
    {
        public string id="white-meridian-astra-v1"; public int revision=4;
        public double scale=1, terraceHeight=5, ridgeHeight=7, secondaryRampWidth=8, wreckSpread=10.5, middleWreckExtension=5.5, middleWreckWidth=1.6;
    }
    public sealed class WhiteMeridianField
    {
        private readonly System.Reflection.FieldInfo field;
        public string Path{get;} public string Group=>"White Meridian terrain"; public string Label{get;} public string Description{get;}
        public string Unit{get;} public double Minimum{get;} public double Maximum{get;} public double Step{get;}
        public WhiteMeridianField(string key,string label,string description,string unit,double min,double max,double step){field=typeof(WhiteMeridianProfileData).GetField(key);Path="maps.whiteMeridian."+key;Label=label;Description=description;Unit=unit;Minimum=min;Maximum=max;Step=step;}
        public double Read(WhiteMeridianProfileData p)=>(double)field.GetValue(p);
        public void Write(WhiteMeridianProfileData p,double value){if(double.IsNaN(value)||double.IsInfinity(value)||value<Minimum||value>Maximum)throw new ArgumentOutOfRangeException(Path);field.SetValue(p,value);}
    }
    // Original v03 polygons are immutable authored content, not global balance tuning.
    // Independent implementation: no geometry, art or evidence from another attempt.
    public sealed class WhiteMeridianMap:IPlayableTerrain,INavArrivalPointTerrain
    {
        public const string DisplayName="Белый меридиан (4)";
        public static readonly IReadOnlyList<WhiteMeridianField> Fields=Array.AsReadOnly(new[]{
            new WhiteMeridianField("scale","Layout scale","Scale the authored map while retaining unit and building sizes.","ratio",1,1.4,.05),
            new WhiteMeridianField("terraceHeight","Allied terrace height","Height of both continuous home shelves and their eight ramps.","m",3,6,.25),
            new WhiteMeridianField("secondaryRampWidth","Secondary ramp width","Straight secondary entry width; mine remains outside its travel axis.","m",6,10,.5),
            new WhiteMeridianField("wreckSpread","Wreck separation","Extra spacing between central wreck sections; barrier ends follow the outer pieces.","m",4,12,.5),
            new WhiteMeridianField("middleWreckWidth","Middle hull width","Width of the central solid and fallen-wing envelope; does not reduce the lateral crossing gaps.","ratio",1,1.6,.1),
            new WhiteMeridianField("middleWreckExtension","Middle hull extension","Length added at each end of the middle hull; outer sections move with wreck separation.","m",0,7,.5),
            new WhiteMeridianField("ridgeHeight","Gypsum ridge height","Physical and visible height of the main impassable gypsum ridges.","m",4,10,.25)});
        public string Id=>"white-meridian-astra-v1"; public int Revision{get;}
        public double Scale{get;} public double TerraceHeight{get;} public double RidgeHeight{get;}
        public double SecondaryRampWidth{get;} public double WreckSpread{get;} public double MiddleWreckExtension{get;} public double MiddleWreckWidth{get;}
        public double HalfExtent=>80*Scale; public double DirectFireHeight=>1; public int SurfaceSemanticsVersion=>1;
        public IReadOnlyList<MapSupport> Supports{get;} public IReadOnlyList<NavObstacle> Solids{get;} public IReadOnlyList<NavObstacle> MovementBlockers{get;}
        public IReadOnlyList<NavObstacle> Ridges{get;} public IReadOnlyList<NavObstacle> Wreck{get;} public IReadOnlyList<NavObstacle> BorderRocks{get;}
        public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions=>locations.Transitions;
        private readonly TerrainLocations locations;
        TerrainLocations INavArrivalPointTerrain.ArrivalLocations=>locations;
        public WhiteMeridianMap(WhiteMeridianProfileData data=null)
        {
            data=data??new WhiteMeridianProfileData();if(data.id!=Id||data.revision<1)throw new ArgumentException("Invalid White Meridian profile");
            foreach(var field in Fields)field.Write(data,field.Read(data));Revision=data.revision;Scale=data.scale;TerraceHeight=data.terraceHeight;RidgeHeight=data.ridgeHeight;SecondaryRampWidth=data.secondaryRampWidth;WreckSpread=data.wreckSpread;MiddleWreckExtension=data.middleWreckExtension;MiddleWreckWidth=data.middleWreckWidth;
            var supports=new List<MapSupport>();var ridges=new List<NavObstacle>();var border=new List<NavObstacle>();
            foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1}){
                string suffix="/"+sx+"/"+sz;
                supports.Add(new MapSupport("inner-ramp",Box(sx*36,sz*42,sx*44,sz*58),false,0,new NavPoint(sx*TerraceHeight/(8*Scale),0),Point(sx*36,0),"inner-ramp"+suffix));
                supports.Add(new MapSupport("home-ramp",Box(sx*(58-SecondaryRampWidth/2),sz*24,sx*(58+SecondaryRampWidth/2),sz*32),false,0,new NavPoint(0,sz*TerraceHeight/(8*Scale)),Point(0,sz*24),"home-ramp"+suffix));
                ridges.Add(Polygon(new double[]{14,37,18,34,24,36,29,34,35,38,34,42,30,44,26,43,23,47,18,45,14,42},sx,sz,RidgeHeight));
                ridges.Add(Polygon(new double[]{32,37,36,35,44,34,44,41,35,42,32,40},sx,sz,RidgeHeight));
                border.Add(Polygon(new double[]{17,65,21,62,26,65,31,63,38,67,42,72,17,72},sx,sz,RidgeHeight));
            }
            // Each side is ONE polygon: graphical terrace/shelf seams cannot become walls.
            var shelf=new double[]{44,-63,48,-67,56,-71,64,-68,73,-69,78,-65,78,65,73,69,64,68,56,71,48,67,44,63,44,34,49,31,54,32,70,32,70,-32,54,-32,49,-31,44,-34};
            foreach(int sx in new[]{-1,1}){
                supports.Add(new MapSupport("allied-shelf",Polygon(shelf,sx,1,TerraceHeight),false,TerraceHeight,surfaceId:"allied-shelf/"+sx));
                ridges.Add(Polygon(new double[]{26+WreckSpread,5,31+WreckSpread,2,44,-6,50,-7,56,-5,64,-7,70,-6,70,6,63,7,57,5,50,7,44,6,32+WreckSpread,10,26+WreckSpread,9},sx,sx,RidgeHeight));
                // Solid outer edge: there is no low road behind or underneath the shelf.
                border.Add(Polygon(new double[]{78,-72,80,-72,80,72,78,72},sx,1,TerraceHeight+RidgeHeight));
            }
            var shoulder=new double[]{-27,10,-25,15,-20,18,-16,16,-15,12,-19,9,-23,8};
            ridges.Add(Polygon(shoulder,1,1,RidgeHeight*.65));ridges.Add(Polygon(shoulder,-1,-1,RidgeHeight*.65));
            supports.Add(new MapSupport("salt-basin",Box(-80,-72,80,72),false,surfaceId:"salt-basin"));
            border.Add(new NavObstacle(new NavPolygon(new[]{Point(-80,72),Point(80,72),Point(80,80),Point(-80,80)}),0,RidgeHeight));
            border.Add(new NavObstacle(new NavPolygon(new[]{Point(-80,-80),Point(80,-80),Point(80,-72),Point(-80,-72)}),0,RidgeHeight));
            var bow=new double[]{-29-WreckSpread,-8,-27-WreckSpread,-4,-16-WreckSpread,-3,-15-WreckSpread,-8,-24-WreckSpread,-11};
            var wreck=new[]{Polygon(bow,1,1,4.5),Polygon(new double[]{-6-MiddleWreckExtension,-3*MiddleWreckWidth,-6-MiddleWreckExtension,3*MiddleWreckWidth,-3,5*MiddleWreckWidth,6+MiddleWreckExtension,3*MiddleWreckWidth,6+MiddleWreckExtension,-3*MiddleWreckWidth,3,-5*MiddleWreckWidth},1,1,5.5),Polygon(bow,-1,-1,4.5)};
            Supports=supports.AsReadOnly();Ridges=ridges.AsReadOnly();BorderRocks=border.AsReadOnly();Wreck=Array.AsReadOnly(wreck);
            var solids=ridges.Concat(border).Concat(wreck).ToArray();Solids=Array.AsReadOnly(solids);
            var links=new List<NavSurfaceTransition>();
            for(int i=0;i<supports.Count;i++)for(int j=i+1;j<supports.Count;j++)links.Add(new NavSurfaceTransition(supports[i].SurfaceId+"~"+supports[j].SurfaceId,supports[i].SurfaceId,supports[j].SurfaceId));
            // TerrainLocations validates height incidence. Export denied seams as visible
            // cliff boundaries to the flat broad-phase too; epsilon is numeric thickness.
            var partition=new TerrainLocations(new NavGeometry(HalfExtent,solids,Revision),Supports,links);
            var blockers=solids.ToList();const double epsilon=.0001; // Width must exceed NavPolygon squared-edge tolerance.
            foreach(var seam in partition.Seams.Where(s=>!s.Allowed)){
                var a=seam.From;var b=seam.To;double dx=b.X-a.X,dz=b.Z-a.Z,len=Math.Sqrt(dx*dx+dz*dz);if(len<epsilon)continue;
                double nx=-dz/len*epsilon,nz=dx/len*epsilon;
                blockers.Add(new NavObstacle(new NavPolygon(new[]{new NavPoint(a.X+nx,a.Z+nz),new NavPoint(b.X+nx,b.Z+nz),new NavPoint(b.X-nx,b.Z-nz),new NavPoint(a.X-nx,a.Z-nz)})));
            }
            MovementBlockers=blockers.AsReadOnly();locations=new TerrainLocations(new NavGeometry(HalfExtent,blockers.ToArray(),Revision),Supports,links);
        }
        public NavPoint Point(double x,double z)=>new NavPoint(x*Scale,z*Scale);
        private NavObstacle Box(double x,double z,double xx,double zz)=>new NavObstacle(Math.Min(x,xx)*Scale,Math.Min(z,zz)*Scale,Math.Max(x,xx)*Scale,Math.Max(z,zz)*Scale);
        private NavObstacle Polygon(double[] xy,int sx,int sz,double height)=>new NavObstacle(new NavPolygon(Enumerable.Range(0,xy.Length/2).Select(i=>Point(sx*xy[i*2],sz*xy[i*2+1]))),0,height);
        public MapSupport SupportAt(NavPoint p){for(int i=0;i<Supports.Count;i++)if(Supports[i].Contains(p))return Supports[i];return null;}
        public double SurfaceHeight(NavPoint p)=>SupportAt(p)?.HeightAt(p)??0;public NavPoint SurfaceGradient(NavPoint p)=>SupportAt(p)?.Gradient??default(NavPoint);
        public bool SupportsFootprint(NavPoint p,double r)=>locations.TryLocate(p,r,out _);
        public bool SupportsSweep(NavPoint a,NavPoint b,double r)=>locations.TryLocate(a,r,out var start)&&locations.Traverse(start,b,r,out _);
        public bool TryLocate(NavPoint p,double r,out NavLocation l)=>locations.TryLocate(p,r,out l);
        public bool IsValidLocation(NavLocation l,double r)=>locations.Valid(l,r);
        public bool TryTraverse(NavLocation l,NavPoint p,double r,out NavLocation end)=>locations.Traverse(l,p,r,out end);
        public bool CompatibleCombatSurface(NavLocation a,NavLocation b)=>locations.Combat(a,b);
        public NavPoint Headquarters(PlayableOwner owner){int i=(int)owner;if(i<0||i>3)throw new ArgumentOutOfRangeException(nameof(owner));return Point(i<2?-62:62,i%2==0?50:-50);}
        public TerritorySite[] Sites(PlayableProfile p){
            var sites=new List<TerritorySite>();
            for(int i=0;i<4;i++)sites.Add(new TerritorySite(i+1,PlayableBuildingKind.Headquarters,Headquarters((PlayableOwner)i),p,i<2?Math.PI:0));
            foreach(int sx in new[]{-1,1})foreach(int sz in new[]{1,-1})sites.Add(new TerritorySite(sites.Count+1,PlayableBuildingKind.Outpost,Point(sx*40,sz*20),p,sx<0?Math.PI:0));
            foreach(int sz in new[]{1,-1})sites.Add(new TerritorySite(sites.Count+1,PlayableBuildingKind.Outpost,Point(0,sz*58),p,sz*Math.PI/2));
            foreach(int sx in new[]{-1,1})foreach(int sz in new[]{1,-1})sites.Add(new TerritorySite(sites.Count+1,PlayableBuildingKind.Mine,Point(sx*64,sz*20),p));
            foreach(int sx in new[]{-1,1})foreach(int sz in new[]{1,-1})sites.Add(new TerritorySite(sites.Count+1,PlayableBuildingKind.Mine,Point(sx*17,sz*26),p));
            return sites.ToArray();
        }
        public OfflineMatchConfiguration Configuration(PlayableProfile p,int seed,OfflineParticipant[] roster){
            if(roster.Length!=4||roster.GroupBy(r=>r.Team).Count()!=2||roster.GroupBy(r=>r.Team).Any(g=>g.Count()!=2))throw new ArgumentException("White Meridian requires two teams of two.");
            var teams=roster.Select(r=>r.Team).Distinct().ToArray();var starts=new OfflineStart[4];
            for(int side=0;side<2;side++){var allies=roster.Where(r=>r.Team==teams[side]).ToArray();for(int slot=0;slot<2;slot++){int i=side*2+slot;var h=Headquarters((PlayableOwner)i);starts[i]=new OfflineStart("meridian-"+i,i+1,h,Point(side==0?-62:62,slot==0?39:-39),slot==0?-Math.PI/2:Math.PI/2,allies[slot].LogicalPlayer);}}
            return new OfflineMatchConfiguration(p,"native-white-meridian-astra-v1",Id,"authored-paired-shelves-v1",seed,roster,starts,Sites(p),MovementBlockers.ToArray(),new double[4,4],terrain:Id);
        }
    }
}
