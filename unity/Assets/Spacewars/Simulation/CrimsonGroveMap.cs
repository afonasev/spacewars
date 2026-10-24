using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    [Serializable] public sealed class CrimsonGroveProfileData
    {
        public string id="crimson-grove-v1"; public int revision=1;
        public double scale=1, terraceHeight=3, ridgeHeight=7;
    }
    public sealed class CrimsonGroveField
    {
        private readonly System.Reflection.FieldInfo field;
        public string Path{get;} public string Group=>"Crimson Grove terrain"; public string Label{get;} public string Description{get;} public string Unit{get;}
        public double Minimum{get;} public double Maximum{get;} public double Step{get;}
        public CrimsonGroveField(string key,string label,string description,string unit,double min,double max,double step){field=typeof(CrimsonGroveProfileData).GetField(key);Path="maps.crimsonGrove."+key;Label=label;Description=description;Unit=unit;Minimum=min;Maximum=max;Step=step;}
        public double Read(CrimsonGroveProfileData data)=>(double)field.GetValue(data);
        public void Write(CrimsonGroveProfileData data,double value){if(double.IsNaN(value)||double.IsInfinity(value)||value<Minimum||value>Maximum)throw new ArgumentOutOfRangeException(Path);field.SetValue(data,value);}
    }
    // Approved metric v3/A geometry is immutable authored content, like MossHighlandLayout.
    // Each ramp top is an exact edge of its terrace. No overlapping-height shortcut is legal.
    public sealed class CrimsonGroveMap:IPlayableTerrain,INavArrivalPointTerrain
    {
        public const string DisplayName="Рубиновая котловина (8)";
        public static readonly IReadOnlyList<CrimsonGroveField> Fields=Array.AsReadOnly(new[]{
            new CrimsonGroveField("scale","Layout scale","Scale the authored plan without changing buildings or units.","ratio",1,1.25,.05),
            new CrimsonGroveField("terraceHeight","Outer terrace height","Elevation of bases and outer pockets; ramps retain their authored length.","m",2,4,.25),
            new CrimsonGroveField("ridgeHeight","Wooded ridge height","Height of the solid pale rock masses above their local terraces.","m",5,9,.25)});
        public string Id=>"crimson-grove-v1"; public int Revision{get;} public double Scale{get;} public double TerraceHeight{get;} public double RidgeHeight{get;}
        public double HalfExtent=>160*Scale; public double DirectFireHeight=>1; public int SurfaceSemanticsVersion=>1;
        public IReadOnlyList<MapSupport> Supports{get;} public IReadOnlyList<NavObstacle> Solids{get;} public IReadOnlyList<NavObstacle> MovementBlockers{get;}
        public IReadOnlyList<NavObstacle> WoodedRidges{get;} public IReadOnlyList<NavObstacle> Border{get;}
        public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions=>locations.Transitions;
        private readonly TerrainLocations locations;
        TerrainLocations INavArrivalPointTerrain.ArrivalLocations=>locations;
        public CrimsonGroveMap(CrimsonGroveProfileData data=null)
        {
            data=data??new CrimsonGroveProfileData();if(data.id!=Id||data.revision<1)throw new ArgumentException("Invalid Crimson Grove profile");
            foreach(var f in Fields)f.Write(data,f.Read(data));Revision=data.revision;Scale=data.scale;TerraceHeight=data.terraceHeight;RidgeHeight=data.ridgeHeight;
            var supports=new List<MapSupport>();var rocks=new List<NavObstacle>();var border=new List<NavObstacle>();
            for(int i=0;i<8;i++){
                double start=12.5+i*45,end=start+45;
                foreach(var rw in new[]{new[]{22.5+i*45,20.0},new[]{42.5+i*45,16.0}}){
                    double a=rw[0],w=rw[1];var u=Polar(a,1);var low=Polar(a,82);var gradient=new NavPoint(u.X*TerraceHeight/(12*Scale*Scale),u.Z*TerraceHeight/(12*Scale*Scale));
                    var ramp=Shape(new[]{RampCorner(a,82,-w/2),RampCorner(a,94,-w/2),RampCorner(a,94,w/2),RampCorner(a,82,w/2)});
                    supports.Add(new MapSupport("grove-ramp",ramp,false,0,gradient,low,"grove/ramp/"+i+"/"+w));
                }
                var inner=new List<NavPoint>();double cursor=start;
                foreach(var rw in new[]{new[]{22.5+i*45,20.0},new[]{42.5+i*45,16.0}}){
                    double a=rw[0],w=rw[1],delta=Math.Atan(w/2/94)*180/Math.PI;
                    Arc(inner,cursor,a-delta,94);inner.Add(RampCorner(a,94,-w/2));inner.Add(RampCorner(a,94,w/2));cursor=a+delta;
                }
                Arc(inner,cursor,end,94);var ring=new List<NavPoint>();Arc(ring,start,end,153);ring.AddRange(inner.AsEnumerable().Reverse());
                supports.Add(new MapSupport("grove-terrace",Shape(ring),false,TerraceHeight,surfaceId:"grove/terrace/"+i));
            }
            // Lower support is behind the terrace/ramp priority, never a second route under it.
            var basin=new List<NavPoint>();Arc(basin,0,360,153);basin.RemoveAt(basin.Count-1);
            supports.Add(new MapSupport("grove-basin",Shape(basin),false,surfaceId:"grove/basin"));
            for(int i=0;i<8;i++){
                double a=42.5+45*i;
                rocks.Add(Radial(a,new double[]{-7,132,-9,141,-5,150,4,153,10,145,8,132,3,130,-3,131},TerraceHeight));
                rocks.Add(Radial(45*i,new double[]{48,153,55,154,57.5,145,57.5,132,60,119,59,111,55,110,52,119,51,133,48,144},TerraceHeight));
            }
            for(int i=0;i<4;i++){
                rocks.Add(Radial(90*i,new double[]{25,25,17,31,15,43,18,52,25,54,35,53,38,47,35,39,32,35},0));
                rocks.Add(Radial(90*i,new double[]{18,70,28,76,30,82,30,95,35,95,35,82,39,73,35,70},0));
            }
            var center=new List<NavPoint>();for(int a=0;a<360;a+=40)center.Add(Polar(a,10+a%3));rocks.Add(new NavObstacle(Shape(center).Footprint,0,RidgeHeight));
            // Border polygons close the square navigation domain around the circular garden.
            for(int i=0;i<16;i++){
                var edge=new List<NavPoint>();double a=i*22.5;Arc(edge,a,a+22.5,153);edge.Add(Polar(a+22.5,240));edge.Add(Polar(a,240));
                border.Add(new NavObstacle(Shape(edge).Footprint,0,TerraceHeight+RidgeHeight));
            }
            Supports=supports.AsReadOnly();WoodedRidges=rocks.AsReadOnly();Border=border.AsReadOnly();Solids=rocks.Concat(border).ToArray();
            var links=new List<NavSurfaceTransition>();for(int i=0;i<supports.Count;i++)for(int j=i+1;j<supports.Count;j++)links.Add(new NavSurfaceTransition(supports[i].SurfaceId+"~"+supports[j].SurfaceId,supports[i].SurfaceId,supports[j].SurfaceId));
            var partition=new TerrainLocations(new NavGeometry(HalfExtent,Solids.ToArray(),Revision),Supports,links);
            var blockers=Solids.ToList();const double epsilon=.0001;
            foreach(var seam in partition.Seams.Where(s=>!s.Allowed)){
                var a=seam.From;var b=seam.To;double dx=b.X-a.X,dz=b.Z-a.Z,len=Math.Sqrt(dx*dx+dz*dz);if(len<epsilon)continue;
                double nx=-dz/len*epsilon,nz=dx/len*epsilon;
                blockers.Add(new NavObstacle(new NavPolygon(new[]{new NavPoint(a.X+nx,a.Z+nz),new NavPoint(b.X+nx,b.Z+nz),new NavPoint(b.X-nx,b.Z-nz),new NavPoint(a.X-nx,a.Z-nz)})));
            }
            MovementBlockers=blockers.AsReadOnly();locations=new TerrainLocations(new NavGeometry(HalfExtent,blockers.ToArray(),Revision),Supports,links);
        }
        public NavPoint Polar(double degrees,double radius){double a=degrees*Math.PI/180;return new NavPoint(Math.Sin(a)*radius*Scale,Math.Cos(a)*radius*Scale);}
        private NavPoint RampCorner(double a,double r,double side){var p=Polar(a,r);var t=Polar(a+90,side);return new NavPoint(p.X+t.X,p.Z+t.Z);}
        private void Arc(List<NavPoint> points,double a,double b,double radius){int n=Math.Max(1,(int)Math.Ceiling((b-a)/2.5));for(int i=0;i<=n;i++){var p=Polar(a+(b-a)*i/n,radius);if(points.Count==0||Distance(points[points.Count-1],p)>1e-8)points.Add(p);}}
        private static NavObstacle Shape(IEnumerable<NavPoint> points)=>new NavObstacle(new NavPolygon(points));
        private NavObstacle Radial(double angle,double[] ar,double floor){var pts=Enumerable.Range(0,ar.Length/2).Select(i=>Polar(angle+ar[i*2],ar[i*2+1]));return new NavObstacle(new NavPolygon(pts),0,floor+RidgeHeight);}
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        public MapSupport SupportAt(NavPoint p){foreach(var s in Supports)if(s.Contains(p))return s;return null;}
        public double SurfaceHeight(NavPoint p)=>SupportAt(p)?.HeightAt(p)??0;
        public NavPoint SurfaceGradient(NavPoint p)=>SupportAt(p)?.Gradient??default(NavPoint);
        public bool SupportsFootprint(NavPoint p,double r)=>locations.TryLocate(p,r,out _);
        public bool SupportsSweep(NavPoint a,NavPoint b,double r)=>locations.TryLocate(a,r,out var l)&&locations.Traverse(l,b,r,out _);
        public bool TryLocate(NavPoint p,double r,out NavLocation l)=>locations.TryLocate(p,r,out l);
        public bool IsValidLocation(NavLocation l,double r)=>locations.Valid(l,r);
        public bool TryTraverse(NavLocation l,NavPoint p,double r,out NavLocation end)=>locations.Traverse(l,p,r,out end);
        public bool CompatibleCombatSurface(NavLocation a,NavLocation b)=>locations.Combat(a,b);
        public NavPoint Headquarters(PlayableOwner owner){int i=(int)owner;if(i<0||i>=8)throw new ArgumentOutOfRangeException(nameof(owner));return Polar(22.5+45*i,128);}
        public TerritorySite[] Sites(PlayableProfile p){
            var sites=new List<TerritorySite>();
            for(int i=0;i<8;i++)sites.Add(new TerritorySite(i+1,PlayableBuildingKind.Headquarters,Headquarters((PlayableOwner)i),p,Math.PI/2-(22.5+45*i+180)*Math.PI/180));
            for(int i=0;i<8;i++)sites.Add(new TerritorySite(i+9,PlayableBuildingKind.Mine,Polar(16.5+45*i,145),p));
            for(int i=0;i<8;i++)sites.Add(new TerritorySite(i+17,PlayableBuildingKind.Outpost,Polar(42.5+45*i,112),p,Math.PI/2-(42.5+45*i+180)*Math.PI/180));
            for(int i=0;i<4;i++)sites.Add(new TerritorySite(i+25,PlayableBuildingKind.Outpost,Polar(90*i,54),p,Math.PI/2-(90*i+180)*Math.PI/180));
            for(int i=0;i<4;i++)sites.Add(new TerritorySite(i+29,PlayableBuildingKind.Mine,Polar(45+90*i,62),p));
            return sites.ToArray();
        }
        public OfflineMatchConfiguration Configuration(PlayableProfile p,int seed,OfflineParticipant[] roster){
            var starts=Enumerable.Range(0,8).Select(i=>new OfflineStart("grove-"+(i+1),i+1,Headquarters((PlayableOwner)i),Polar(22.5+45*i,116),Math.Atan2(-Headquarters((PlayableOwner)i).Z,-Headquarters((PlayableOwner)i).X),i+1)).ToArray();
            return new OfflineMatchConfiguration(p,"native-crimson-grove-v1",Id,"authored-ring-v3-A",seed,roster,starts,Sites(p),MovementBlockers.ToArray(),new double[8,8],terrain:Id);
        }
    }
}
