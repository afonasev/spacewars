using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    [Serializable] public sealed class AntennaCityProfileData
    {
        public string id="antenna-city-greybox-v1"; public int revision=3;
        public double scale=1, plazaHeight=4, blockHeight=6;
        public double mainStreetWidth=16,sideStreetWidth=12,baseYardRadius=18,outpostYardRadius=14,mineYardRadius=6,blockGap=6,districtPassageWidth=16;
        public double keyPointPadScale=.85,keyPointPadBorder=.8;
    }
    public sealed class AntennaCityField
    {
        readonly System.Reflection.FieldInfo field;
        public string Path{get;} public string Group=>"Antenna City terrain"; public string Label{get;} public string Description{get;} public string Unit{get;}
        public double Minimum{get;} public double Maximum{get;} public double Step{get;}
        public AntennaCityField(string key,string label,string description,string unit,double min,double max,double step){field=typeof(AntennaCityProfileData).GetField(key);Path="maps.antennaCity."+key;Label=label;Description=description;Unit=unit;Minimum=min;Maximum=max;Step=step;}
        public double Read(AntennaCityProfileData p)=>(double)field.GetValue(p);
        public void Write(AntennaCityProfileData p,double value){if(double.IsNaN(value)||double.IsInfinity(value)||value<Minimum||value>Maximum)throw new ArgumentOutOfRangeException(Path);field.SetValue(p,value);}
    }
    /// <summary>Approved start anchors with F2 dense city blocks. Bases and outposts are on the street plane; only three mine plazas rise above it.</summary>
    public sealed partial class AntennaCityMap:IPlayableTerrain,INavArrivalPointTerrain
    {
        public const string DisplayName="Нова-Сити (6)";
        public static readonly IReadOnlyList<AntennaCityField> Fields=Array.AsReadOnly(new[]{
            new AntennaCityField("scale","City layout scale","Scale author coordinates and clearance, without scaling units or buildings.","ratio",1,1.25,.05),
            new AntennaCityField("plazaHeight","Inner plaza height","Height of three mine plazas; headquarters and outposts remain at street height.","m",2,5,.25),
            new AntennaCityField("blockHeight","Quarter height","Visible and authoritative height of the blocked city quarters.","m",4,8,.25),
            new AntennaCityField("mainStreetWidth","Main street width","Clear width of the ring boulevard and base streets.","m",14,20,.5),
            new AntennaCityField("sideStreetWidth","Center street width","Clear width of independent ground approaches and side streets.","m",10,14,.5),
            new AntennaCityField("baseYardRadius","Base yard half size","Reserve street-level space for headquarters, pads and exits.","m",16,20,.5),
            new AntennaCityField("outpostYardRadius","Outpost yard half size","Reserve construction and access around neutral outposts.","m",13,16,.5),
            new AntennaCityField("mineYardRadius","Mine yard half size","Reserve access and fighting space around ground mines.","m",4,8,.5),
            new AntennaCityField("blockGap","Building separation","Clear gaps between adjacent authored city lots.","m",2,8,.25),
            new AntennaCityField("districtPassageWidth","Inner cross passage width","Ground passage between inner and middle city blocks; widens both edges equally.","m",9,18,.5),
            new AntennaCityField("keyPointPadScale","Key point pad scale","Visual-only apron size relative to reserved yard; does not change movement or construction.","ratio",.65,.9,.05),
            new AntennaCityField("keyPointPadBorder","Key point pad border","Visual-only border inset for key-point aprons.","m",.4,1.5,.1)});
        public string Id=>"antenna-city-greybox-v1"; public int Revision{get;} public double Scale{get;} public double PlazaHeight{get;} public double BlockHeight{get;}
        public double HalfExtent=>170*Scale; public double DirectFireHeight=>1; public int SurfaceSemanticsVersion=>1;
        public IReadOnlyList<MapSupport> Supports{get;} public IReadOnlyList<NavObstacle> Solids{get;} public IReadOnlyList<NavObstacle> MovementBlockers{get;}
        public IReadOnlyList<NavObstacle> Blocks{get;} public IReadOnlyList<NavObstacle> Boundary{get;}
        public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions=>locations.Transitions;
        readonly TerrainLocations locations;
        TerrainLocations INavArrivalPointTerrain.ArrivalLocations=>locations;
        // Angles follow the approved top-down drawing clockwise; world Z points north.
        public NavPoint PointAt(double angle,double radius){double a=angle*Math.PI/180;return new NavPoint(Math.Cos(a)*radius*Scale,-Math.Sin(a)*radius*Scale);}
        public NavPoint Local(double angle,double radius,double tangent,double radial){double a=angle*Math.PI/180;return new NavPoint((Math.Cos(a)*(radius+radial)-Math.Sin(a)*tangent)*Scale,(-Math.Sin(a)*(radius+radial)-Math.Cos(a)*tangent)*Scale);}
        NavObstacle Box(double a,double r,double t0,double t1,double r0,double r1,double height=0)=>new NavObstacle(new NavPolygon(new[]{Local(a,r,t0,r0),Local(a,r,t1,r0),Local(a,r,t1,r1),Local(a,r,t0,r1)}),0,height);
        public NavPoint RampFoot(int plaza,int side,double beyond=0)=>Local(-90+120*plaza,52,side*(33+beyond),0);
        public NavPoint RampTop(int plaza,int side,double inside=0)=>Local(-90+120*plaza,52,side*(17-inside),0);
        public AntennaCityMap(AntennaCityProfileData data=null)
        {
            data=data??new AntennaCityProfileData();if(data.id!=Id||data.revision<1)throw new ArgumentException("Invalid Antenna City profile");
            foreach(var f in Fields)f.Write(data,f.Read(data));Revision=data.revision;Scale=data.scale;PlazaHeight=data.plazaHeight;BlockHeight=data.blockHeight;MainStreetWidth=data.mainStreetWidth;SideStreetWidth=data.sideStreetWidth;BaseYardRadius=data.baseYardRadius;OutpostYardRadius=data.outpostYardRadius;MineYardRadius=data.mineYardRadius;BlockGap=data.blockGap;DistrictPassageWidth=data.districtPassageWidth;KeyPointPadScale=data.keyPointPadScale;KeyPointPadBorder=data.keyPointPadBorder;
            var supports=new List<MapSupport>();
            for(int i=0;i<3;i++)foreach(int side in new[]{-1,1}){
                var lo=RampFoot(i,side);var hi=RampTop(i,side);double dx=hi.X-lo.X,dz=hi.Z-lo.Z,d2=dx*dx+dz*dz;
                string id="city/ramp/"+i+"/"+side;
                supports.Add(new MapSupport(id,Box(-90+120*i,52,side*17,side*33,-7,7),false,0,new NavPoint(dx*PlazaHeight/d2,dz*PlazaHeight/d2),lo,id));
            }
            for(int i=0;i<3;i++)supports.Add(new MapSupport("city/plaza/"+i,Box(-90+120*i,52,-17,17,-14,14),false,PlazaHeight,surfaceId:"city/plaza/"+i));
            supports.Add(new MapSupport("city/street",new NavObstacle(new NavPolygon(Enumerable.Range(0,6).Select(i=>PointAt(i*60,170)))),false,surfaceId:"city/street"));
            var blocks=CityBlocks(supports);var boundary=new List<NavObstacle>();
            for(int i=0;i<6;i++){
                boundary.Add(new NavObstacle(new NavPolygon(new[]{PointAt(i*60,170),PointAt((i+1)*60,170),PointAt((i+1)*60,340),PointAt(i*60,340)}),-2,0));
            }
            blocks.Add(new NavObstacle(new NavPolygon(Enumerable.Range(0,3).Select(i=>PointAt(-90+120*i,14))),0,BlockHeight));
            Supports=supports.AsReadOnly();Blocks=blocks.AsReadOnly();Boundary=boundary.AsReadOnly();Solids=blocks.Concat(boundary).ToArray();
            var links=new List<NavSurfaceTransition>();for(int i=0;i<supports.Count;i++)for(int j=i+1;j<supports.Count;j++)links.Add(new NavSurfaceTransition(supports[i].SurfaceId+"~"+supports[j].SurfaceId,supports[i].SurfaceId,supports[j].SurfaceId));
            var partition=new TerrainLocations(new NavGeometry(HalfExtent,Solids.ToArray(),Revision),Supports,links);
            var blockers=Solids.ToList();const double epsilon=.0001; // Numerical seam thickness; no gameplay clearance tuning.
            foreach(var seam in partition.Seams.Where(s=>!s.Allowed)){
                var a=seam.From;var b=seam.To;double dx=b.X-a.X,dz=b.Z-a.Z,len=Math.Sqrt(dx*dx+dz*dz);if(len<epsilon)continue;
                double nx=-dz/len*epsilon,nz=dx/len*epsilon;
                blockers.Add(new NavObstacle(new NavPolygon(new[]{new NavPoint(a.X+nx,a.Z+nz),new NavPoint(b.X+nx,b.Z+nz),new NavPoint(b.X-nx,b.Z-nz),new NavPoint(a.X-nx,a.Z-nz)})));
            }
            MovementBlockers=blockers.AsReadOnly();locations=new TerrainLocations(new NavGeometry(HalfExtent,blockers.ToArray(),Revision),Supports,links);
        }
        public MapSupport SupportAt(NavPoint p){for(int i=0;i<Supports.Count;i++)if(Supports[i].Contains(p))return Supports[i];return null;}
        public double SurfaceHeight(NavPoint p)=>SupportAt(p)?.HeightAt(p)??0;
        public NavPoint SurfaceGradient(NavPoint p)=>SupportAt(p)?.Gradient??default(NavPoint);
        public bool SupportsFootprint(NavPoint p,double r)=>locations.TryLocate(p,r,out _);
        public bool SupportsSweep(NavPoint a,NavPoint b,double r)=>locations.TryLocate(a,r,out var start)&&locations.Traverse(start,b,r,out _);
        public bool TryLocate(NavPoint p,double r,out NavLocation l)=>locations.TryLocate(p,r,out l);
        public bool IsValidLocation(NavLocation l,double r)=>locations.Valid(l,r);
        public bool TryTraverse(NavLocation l,NavPoint p,double r,out NavLocation end)=>locations.Traverse(l,p,r,out end);
        public bool CompatibleCombatSurface(NavLocation a,NavLocation b)=>locations.Combat(a,b);
        public NavPoint Headquarters(PlayableOwner owner){int i=(int)owner;if(i<0||i>=6)throw new ArgumentOutOfRangeException(nameof(owner));return PointAt(-120+60*i,120);}
        public TerritorySite[] Sites(PlayableProfile p){
            var sites=new List<TerritorySite>();
            for(int i=0;i<6;i++)sites.Add(new TerritorySite(i+1,PlayableBuildingKind.Headquarters,Headquarters((PlayableOwner)i),p));
            for(int i=0;i<6;i++)sites.Add(new TerritorySite(i+7,PlayableBuildingKind.Outpost,PointAt(-120+60*i,82),p));
            for(int i=0;i<6;i++)sites.Add(new TerritorySite(i+13,PlayableBuildingKind.Mine,PointAt(-120+60*i,142),p));
            for(int i=0;i<6;i++)sites.Add(new TerritorySite(i+19,PlayableBuildingKind.Mine,PointAt(-90+60*i,124),p));
            for(int i=0;i<3;i++)sites.Add(new TerritorySite(i+25,PlayableBuildingKind.Mine,PointAt(-90+120*i,52),p));
            return sites.ToArray();
        }
        public OfflineMatchConfiguration Configuration(PlayableProfile p,int seed,OfflineParticipant[] roster){
            if(roster==null||roster.Length<2||roster.Length>6)throw new ArgumentException("Antenna City requires two to six participants");
            var starts=Enumerable.Range(0,6).Select(i=>{var home=Headquarters((PlayableOwner)i);return new OfflineStart("city-"+(i+1),i+1,home,PointAt(-120+60*i,106),Math.Atan2(-home.Z,-home.X));}).ToArray();
            // Ordinary lobby assigns peripheral seats by team grouping/seed, not fake route costs.
            return new OfflineMatchConfiguration(p,Id,Id,"antenna-city-f3-wide-passages",seed,roster,starts,Sites(p),MovementBlockers.ToArray(),new double[6,6],terrain:Id);
        }
    }
}
