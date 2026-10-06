using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    [Serializable]
    public sealed partial class ThreeCrossingsProfileData
    {
        public string id="three-crossings-greybox-v1";
        public int revision=3;
        public double halfExtent=64, riverHalfWidth=8, crossingZ=38, sideBridgeWidth=10, centralBridgeWidth=14, centralBridgeHalfSpan=12;
        public double baseCoordinate=46, mineX=24, mineZ=48, pocketX=40;
        public double waterDepth=3, deckThickness=1, directFireHeight=1;
        public double railHeight=1.1,railThickness=.6,postHeight=2,postWidth=1.2;
        public double overviewSize=70, overviewHeight=145, overviewOffset=-90, platformInspectionSize=22;
    }
    public sealed class ThreeCrossingsField
    {
        private readonly System.Reflection.FieldInfo field;
        public ThreeCrossingsField(string key,string group,string label,string description,double min,double max,double step)
        {field=typeof(ThreeCrossingsProfileData).GetField(key);Path="maps.threeCrossings."+key;Group=group;Label=label;Description=description;Minimum=min;Maximum=max;Step=step;}
        public string Path{get;} public string Group{get;} public string Label{get;} public string Description{get;} public string Unit=>"m";
        public double Minimum{get;} public double Maximum{get;} public double Step{get;}
        public double Read(ThreeCrossingsProfileData data)=>(double)field.GetValue(data);
        public void Write(ThreeCrossingsProfileData data,double value)=>field.SetValue(data,value);
    }
    public sealed class MapSupport
    {
        public MapSupport(string id,NavObstacle bounds,bool bridge,double height=0,NavPoint gradient=default(NavPoint),NavPoint origin=default(NavPoint)){Id=id;Bounds=bounds;IsBridge=bridge;Height=height;Gradient=gradient;Origin=origin;}
        public string Id{get;} public NavObstacle Bounds{get;} public bool IsBridge{get;}
        public double Height{get;} public NavPoint Gradient{get;} public NavPoint Origin{get;}
        public double HeightAt(NavPoint p)=>Height+(p.X-Origin.X)*Gradient.X+(p.Z-Origin.Z)*Gradient.Z;
        public bool Contains(NavPoint p)=>Bounds.Contains(p);
    }
    public sealed partial class ThreeCrossingsMap
    {
        public static readonly IReadOnlyList<ThreeCrossingsField> Fields=Array.AsReadOnly(new[]{
            new ThreeCrossingsField("halfExtent","Layout","Half extent","Overall bank and route reserve.",48,96,1),
            new ThreeCrossingsField("riverHalfWidth","River","River half width","Bridge span and water separation.",4,12,.5),
            new ThreeCrossingsField("crossingZ","Crossings","Side crossing position","Distance of north and south bridges from center.",28,60,1),
            new ThreeCrossingsField("sideBridgeWidth","Crossings","Side bridge width","Clear width for groups at outer bridges.",8,20,.5),
            new ThreeCrossingsField("centralBridgeHalfSpan","Crossings","Central half span","Contact-to-contact width of the broad central bridge.",10,15,.5),
            new ThreeCrossingsField("centralBridgeWidth","Crossings","Central bridge width","Clear width of broad central crossing.",12,28,.5),
            new ThreeCrossingsField("baseCoordinate","Sites","Base coordinate","Distance of corner base centers along each axis.",32,76,1),
            new ThreeCrossingsField("mineX","Sites","Outer mine X","Lateral coordinate of start and flank mines.",16,36,1),
            new ThreeCrossingsField("mineZ","Sites","Outer mine Z","North/south coordinate of start and flank mines.",32,76,1),
            new ThreeCrossingsField("pocketX","Pockets","Pocket center X","Lateral pocket center and mine position.",28,64,1),
            new ThreeCrossingsField("directFireHeight","Combat height","Direct fire height","Tank and Explorer launch and target height above local support.",.5,2,.05),
            new ThreeCrossingsField("waterDepth","Height","Water depth","Visible water surface below the common support plane.",1,6,.25),
            new ThreeCrossingsField("deckThickness","Height","Deck thickness","Bridge structural thickness below its support plane.",.5,2,.25),
            new ThreeCrossingsField("railHeight","Bridge silhouette","Rail height","Presentation-only rail above support.",.4,1.2,.05),
            new ThreeCrossingsField("railThickness","Bridge silhouette","Rail thickness","Rail width outside the full driveable corridor.",.2,.8,.05),
            new ThreeCrossingsField("postHeight","Bridge silhouette","Post height","End posts higher than bridge rails.",1,2,.1),
            new ThreeCrossingsField("postWidth","Bridge silhouette","Post width","End posts outside the driveable corridor.",.4,1.2,.1),
            new ThreeCrossingsField("platformRadius","Platforms","Platform half width","Full cluster reserve and octagonal silhouette.",11,15,.25),
            new ThreeCrossingsField("platformChamfer","Platforms","Corner cut","Octagonal corner depth.",2,5,.25),
            new ThreeCrossingsField("platformHeight","Platforms","Deck height","Authoritative platform support above banks.",1,3,.25),
            new ThreeCrossingsField("rampLength","Platforms","Ramp length","Continuous gate approach slope.",4,8,.25),
            new ThreeCrossingsField("rampWidth","Platforms","Gate width","Full supported width of each inward entrance.",7,12,.25),
            new ThreeCrossingsField("platformWallWidth","Platforms","Perimeter width","Blocking platform and ramp side edge.",.3,1,.1),
            new ThreeCrossingsField("platformWallHeight","Platforms","Perimeter height","Solid parapet above deck.",.4,1.2,.1),
            new ThreeCrossingsField("rockHeight","Mountains","Lower mountain height","Authoritative first rock tier.",3,9,.25),
            new ThreeCrossingsField("rockCrownHeight","Mountains","Upper mountain height","Additional solid crown height.",1,6,.25),
            new ThreeCrossingsField("rockCrownInset","Mountains","Crown inset","Inner mountain tier setback from the authored contour.",.2,2,.05),
            new ThreeCrossingsField("platformInspectionSize","Camera","Platform inspection size","Frames the complete platform and both ramps for visual review.",18,30,1),
            new ThreeCrossingsField("overviewSize","Camera","Overview size","Orthographic half height for map inspection.",50,100,1),
            new ThreeCrossingsField("overviewHeight","Camera","Overview height","Inspection camera altitude.",100,190,1),
            new ThreeCrossingsField("overviewOffset","Camera","Overview offset","Oblique inspection camera south offset.",-100,-50,1)
        });
        public string Id{get;} public int Revision{get;}
        public double SurfaceHeight(NavPoint p)=>SupportAt(p)?.HeightAt(p)??-WaterDepth;
        public NavPoint SurfaceGradient(NavPoint p)=>SupportAt(p)?.Gradient??default(NavPoint);
        public double HalfExtent{get;} public double RiverHalfWidth{get;} public double CrossingZ{get;} public double SideBridgeWidth{get;} public double CentralBridgeWidth{get;} public double CentralBridgeHalfSpan{get;}
        public double BaseCoordinate{get;} public double MineX{get;} public double MineZ{get;} public double PocketX{get;}
        public double RailHeight{get;} public double RailThickness{get;} public double PostHeight{get;} public double PostWidth{get;}
        public double DirectFireHeight{get;} public double WaterDepth{get;} public double DeckThickness{get;} public double PlatformInspectionSize{get;} public double OverviewSize{get;} public double OverviewHeight{get;} public double OverviewOffset{get;}
        public IReadOnlyList<MapSupport> Supports{get;} public IReadOnlyList<NavObstacle> Water{get;} public IReadOnlyList<NavObstacle> Solids{get;} public IReadOnlyList<NavObstacle> MovementBlockers{get;}
        private readonly NavGeometry geometry;
        public ThreeCrossingsMap(ThreeCrossingsProfileData d)
        {
            if(d==null||d.id!="three-crossings-greybox-v1"||d.revision<1)throw new ArgumentException("Invalid map profile identity.");
            foreach(var f in Fields){double v=f.Read(d);if(double.IsNaN(v)||double.IsInfinity(v)||v<f.Minimum||v>f.Maximum)throw new ArgumentException("Invalid map field: "+f.Path);}
            if(d.postHeight<=d.railHeight||d.centralBridgeWidth<=d.sideBridgeWidth||d.waterDepth<=d.deckThickness||d.crossingZ+d.sideBridgeWidth/2>=d.halfExtent||d.crossingZ-d.sideBridgeWidth/2<=d.centralBridgeWidth/2)throw new ArgumentException("Invalid map topology.");
            RailHeight=d.railHeight;RailThickness=d.railThickness;PostHeight=d.postHeight;PostWidth=d.postWidth;Id=d.id;Revision=d.revision;HalfExtent=d.halfExtent;RiverHalfWidth=d.riverHalfWidth;CrossingZ=d.crossingZ;SideBridgeWidth=d.sideBridgeWidth;CentralBridgeWidth=d.centralBridgeWidth;CentralBridgeHalfSpan=d.centralBridgeHalfSpan;BaseCoordinate=d.baseCoordinate;MineX=d.mineX;MineZ=d.mineZ;PocketX=d.pocketX;DirectFireHeight=d.directFireHeight;WaterDepth=d.waterDepth;DeckThickness=d.deckThickness;PlatformInspectionSize=d.platformInspectionSize;OverviewSize=d.overviewSize;OverviewHeight=d.overviewHeight;OverviewOffset=d.overviewOffset;
            BuildContours(d,out var supports,out var water,out var rocks);
            Supports=Array.AsReadOnly(supports.ToArray());Water=Array.AsReadOnly(water.ToArray());
            Solids=Array.AsReadOnly(rocks.ToArray());MovementBlockers=Array.AsReadOnly(rocks.Concat(water).ToArray());geometry=new NavGeometry(HalfExtent,MovementBlockers.ToArray(),Revision);
        }
        public static ThreeCrossingsMap Default=>new ThreeCrossingsMap(new ThreeCrossingsProfileData());
        public MapSupport SupportAt(NavPoint point)=>Supports.FirstOrDefault(s=>s.Contains(point));
        public bool SupportsFootprint(NavPoint point,double radius)=>geometry.IsFree(point,radius);
        public bool SupportsSweep(NavPoint from,NavPoint to,double radius)=>geometry.SegmentFree(from,to,radius);
        public TerritorySite[] Sites(PlayableProfile p)=>new[]{
            new TerritorySite(1,PlayableBuildingKind.Headquarters,new NavPoint(-BaseCoordinate,-BaseCoordinate),p),
            new TerritorySite(2,PlayableBuildingKind.Headquarters,new NavPoint(BaseCoordinate,BaseCoordinate),p,Math.PI),
            new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(-BaseCoordinate,BaseCoordinate),p),
            new TerritorySite(4,PlayableBuildingKind.Outpost,new NavPoint(BaseCoordinate,-BaseCoordinate),p,Math.PI),
            new TerritorySite(5,PlayableBuildingKind.Mine,new NavPoint(-MineX,-MineZ),p),new TerritorySite(6,PlayableBuildingKind.Mine,new NavPoint(MineX,MineZ),p),
            new TerritorySite(7,PlayableBuildingKind.Mine,new NavPoint(-PocketX,0),p),new TerritorySite(8,PlayableBuildingKind.Mine,new NavPoint(PocketX,0),p),
            new TerritorySite(9,PlayableBuildingKind.Mine,new NavPoint(-MineX,MineZ),p),new TerritorySite(10,PlayableBuildingKind.Mine,new NavPoint(MineX,-MineZ),p)};
    }
}
