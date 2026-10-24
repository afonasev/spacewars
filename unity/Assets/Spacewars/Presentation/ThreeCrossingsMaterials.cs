using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SurfaceSettingAttribute : Attribute
    {
        public readonly string Group, Label, Description, Unit;
        public readonly float Minimum, Maximum, Step;
        public SurfaceSettingAttribute(string group,string label,string description,string unit,float min,float max,float step)
        {Group=group;Label=label;Description=description;Unit=unit;Minimum=min;Maximum=max;Step=step;}
    }

    [Serializable] public sealed class SurfaceRoadPoint
    {
        public float x,z;
        public SurfaceRoadPoint(float x,float z){this.x=x;this.z=z;}
    }
    [Serializable] public sealed class SurfaceRoad
    {
        public string id;
        public SurfaceRoadPoint[] points;
    }

    // Presentation-only profile: never consumed by simulation, navigation or AI.
    [Serializable] public sealed class ThreeCrossingsSurfaceProfile
    {
        public string id="three-crossings-materials-v1";
        public int revision=1;
        [SurfaceSetting("Ground","Hue","Ground palette hue.","turn",0,1,.01f)] public float groundHue=.18f;
        [SurfaceSetting("Ground","Saturation","Muted olive saturation.","ratio",0,.5f,.01f)] public float groundSaturation=.16f;
        [SurfaceSetting("Ground","Brightness","Ground base value.","ratio",.15f,.8f,.01f)] public float groundBrightness=.48f;
        [SurfaceSetting("Ground","Broad scale","Size of broad earth patches.","m",3,30,.5f)] public float broadScale=11;
        [SurfaceSetting("Ground","Broad contrast","Strength of large tonal variation.","ratio",0,.8f,.01f)] public float broadContrast=.38f;
        [SurfaceSetting("Ground","Grain scale","Fine ground grain size.","m",.05f,1,.01f)] public float grainScale=.24f;
        [SurfaceSetting("Ground","Grain contrast","Fine surface roughness contrast.","ratio",0,.5f,.01f)] public float grainContrast=.16f;
        [SurfaceSetting("Roads","Width","Painted road width; never affects navigation.","m",2,6,.1f)] public float roadWidth=4.2f;
        [SurfaceSetting("Roads","Arterial width","Primary roads widen into available clear support; narrow routes retain their validated core.","m",4.2f,12,.2f)] public float arterialWidth=8;
        [SurfaceSetting("Roads","Approach length","Smooth transition from arterial road to full bridge deck width.","m",4,20,.5f)] public float approachLength=12;
        [SurfaceSetting("Roads","Visual meander","Maximum visual drift of the dirt-road centerline; never changes route geometry.","m",0,1.75f,.05f)] public float roadMeander=1.25f;
        [SurfaceSetting("Roads","Shoulder width","Feather width inside the painted road edge; preserves the compacted center.","m",.1f,1.5f,.05f)] public float roadFeather=.8f;
        [SurfaceSetting("Roads","Center compaction","Darken the compressed road center against its softer shoulders.","ratio",0,.4f,.02f)] public float roadCoreShade=.22f;
        [SurfaceSetting("Roads","Shoulder blend","Blend amount of road tint retained at the shoulder.","ratio",.35f,1,.05f)] public float roadShoulderTone=.78f;
        [SurfaceSetting("Roads","Rut strength","Scale of existing wheel-path wear on road surfaces.","ratio",0,1,.05f)] public float roadRutStrength=.72f;
        [SurfaceSetting("Roads","Edge variation","Natural variation of the dirt-road edges.","ratio",0,2.5f,.05f)] public float roadEdgeVariation=2.2f;
        [SurfaceSetting("Roads","Hue","Compacted earth hue.","turn",0,1,.01f)] public float roadHue=.10f;
        [SurfaceSetting("Roads","Saturation","Compacted earth saturation.","ratio",0,.6f,.01f)] public float roadSaturation=.28f;
        [SurfaceSetting("Roads","Brightness","Road contrast against surrounding ground.","ratio",.15f,.9f,.01f)] public float roadBrightness=.62f;
        [SurfaceSetting("Concrete","Brightness","Concrete surface base value.","ratio",.2f,.9f,.01f)] public float concreteBrightness=.69f;
        [SurfaceSetting("Concrete","Detail contrast","Pore and crack contrast around the calibrated albedo mean.","ratio",0,3,.1f)] public float concreteDetailContrast=1.8f;
        [SurfaceSetting("Concrete","Slab size","Spacing of concrete expansion joints.","m",2,8,.25f)] public float slabSize=4;
        [SurfaceSetting("Concrete","Joint width","Width of concrete expansion joints.","m",.015f,.12f,.005f)] public float jointWidth=.045f;
        [SurfaceSetting("Concrete","Joint contrast","Darkness of expansion joints.","ratio",0,.7f,.01f)] public float jointContrast=.22f;
        [SurfaceSetting("Metal","Brightness","Steel bridge deck base value.","ratio",.15f,.8f,.01f)] public float metalBrightness=.41f;
        [SurfaceSetting("Metal","Panel length","Steel panel spacing along bridge travel.","m",1,5,.25f)] public float metalPanelLength=2;
        [SurfaceSetting("Metal","Panel width","Steel panel spacing across bridge travel.","m",1,5,.25f)] public float metalPanelWidth=2.5f;
        [SurfaceSetting("Metal","Seam contrast","Metal panel edge contrast.","ratio",0,.7f,.01f)] public float metalSeamContrast=.32f;
        [SurfaceSetting("Metal","Brushed grain","Strength of fine brushed metal grain.","ratio",0,.3f,.01f)] public float metalGrain=.09f;
        [SurfaceSetting("Metal","Highlight strength","Intensity of steel reflections.","ratio",0,.7f,.01f)] public float metalHighlight=.28f;
        [SurfaceSetting("Metal","Highlight focus","Focus of steel reflections.","exponent",8,128,1)] public float metalShininess=48;
        [SurfaceSetting("Ground finish","Open detail","Fraction of shared texture detail retained on open ground; preserves broad palette patches.","ratio",0,1,.05f)] public float groundOpenDetail=.8f;
        [SurfaceSetting("Ground finish","Foot detail","Fraction of shared texture detail retained near cliff feet and existing scree.","ratio",0,1,.05f)] public float groundFootDetail=1;
        [SurfaceSetting("Ground finish","Fine grain retention","Fraction of existing fine grain contrast retained on ground only.","ratio",0,1,.05f)] public float groundGrainRetention=.3f;
        [SurfaceSetting("Rock foot","Contact width","Narrow grounding shade outside authoritative geological footprints.","m",.2f,2,.05f)] public float rockFootContactWidth=.65f;
        [SurfaceSetting("Rock foot","Contact strength","Restrained contact shade at cliffs and existing fragments.","ratio",0,.5f,.02f)] public float rockFootContactStrength=.2f;
        [SurfaceSetting("Rock foot","Dust reach","Maximum ground dust transition beyond geological feet; protected roads and yards stay clear.","m",.5f,4,.1f)] public float rockFootDustReach=2.2f;
        [SurfaceSetting("Rock foot","Dust strength","Opacity of local mineral dust, independent of platform sand.","ratio",0,.6f,.02f)] public float rockFootDustStrength=.2f;
        [SurfaceSetting("Rock foot","Patch scale","World-space extent of irregular foot dust patches.","m",1,12,.5f)] public float rockFootPatchScale=5;
        [SurfaceSetting("Rock","Projection transition","Narrow blend between well-covered texture planes on cliff faces; wider softens axis changes. Rock material only, without changing ground or geometry.","ratio",.02f,.2f,.01f)] public float rockProjectionTransition=.06f;
        [SurfaceSetting("Rock","Ledge wavelength","Broad connected roof relief, independent of texture detail.","m",4,30,1)] public float rockLedgeWavelength=14;
        [SurfaceSetting("Rock","Passage reveal","Taper the rendered rear slope inside the original rock to expose supported ground at the gameplay camera angle. Does not change cover or navigation.","ratio",0,1,.05f)] public float rockPassageReveal=1;
        [SurfaceSetting("Rock","Reveal margin","Inward screen-space ground margin at the rear rock edge.","m",0,2,.1f)] public float rockRevealMargin=.6f;
        [SurfaceSetting("Rock","Brightness multiplier","Preserve authored rock tier colors while scaling brightness.","ratio",.5f,1.5f,.01f)] public float rockBrightness=1;
        [SurfaceSetting("Frontier palette","Transition angle","Direction of the continuous graphite-to-quarry region.","deg",0,360,5)] public float paletteAngle=45;
        [SurfaceSetting("Frontier palette","Transition width","World width of the A-to-B palette transition.","m",16,120,2)] public float paletteWidth=64;
        [SurfaceSetting("Frontier palette","Region irregularity","Organic displacement of the palette boundary.","m",0,30,1)] public float paletteIrregularity=18;
        [SurfaceSetting("Frontier palette","Region scale","Size of geological bends in the palette transition.","m",8,60,1)] public float paletteScale=28;
        [SurfaceSetting("Frontier palette","Graphite hue","Cold region rock hue.","turn",0,1,.01f)] public float graphiteHue=.59f;
        [SurfaceSetting("Frontier palette","Graphite saturation","Restrained blue-grey rock saturation.","ratio",0,.4f,.01f)] public float graphiteSaturation=.12f;
        [SurfaceSetting("Frontier palette","Graphite value","Readable graphite rock brightness.","ratio",.2f,.6f,.01f)] public float graphiteValue=.34f;
        [SurfaceSetting("Frontier palette","Quarry hue","Warm region olive-slate hue.","turn",0,1,.01f)] public float quarryHue=.145f;
        [SurfaceSetting("Frontier palette","Quarry saturation","Muted quarry rock saturation.","ratio",0,.4f,.01f)] public float quarrySaturation=.18f;
        [SurfaceSetting("Frontier palette","Quarry value","Warm region rock brightness.","ratio",.2f,.6f,.01f)] public float quarryValue=.39f;
        [SurfaceSetting("Frontier palette","Quarry dust hue","Beige mineral dust and road hue in region B.","turn",0,1,.01f)] public float quarryDustHue=.095f;
        [SurfaceSetting("Frontier palette","Quarry dust saturation","Muted warm sand saturation.","ratio",0,.4f,.01f)] public float quarryDustSaturation=.23f;
        [SurfaceSetting("Frontier palette","Quarry dust value","Warm road and mineral deposit brightness.","ratio",.25f,.75f,.01f)] public float quarryDustValue=.55f;
        [SurfaceSetting("Used construction","Metal strip width","Flush steel reinforcement along concrete slab seams.","m",.08f,.6f,.02f)] public float platformMetalWidth=.14f;
        [SurfaceSetting("Used construction","Age contrast","Patchy concrete stains and steel oxidation contrast.","ratio",0,.5f,.02f)] public float platformAge=.24f;
        [SurfaceSetting("Used construction","Interior sand","Separated sand deposits across old base platforms.","ratio",0,.8f,.05f)] public float platformSand=.8f;
        [SurfaceSetting("Truck traffic","Wheel spacing","Distance between paired truck wheel paths.","m",1.5f,4,.1f)] public float truckWheelSpacing=2.8f;
        [SurfaceSetting("Truck traffic","Wheel strip width","Width of each worn tire strip.","m",.2f,1,.05f)] public float truckTrackWidth=.55f;
        [SurfaceSetting("Truck traffic","Tread pitch","Longitudinal spacing of tire tread impressions.","m",.3f,1.2f,.05f)] public float truckTreadPitch=.65f;
        [SurfaceSetting("Truck traffic","Wear contrast","Contrast of worn truck tracks; roads and construction only.","ratio",0,.5f,.02f)] public float truckWear=.32f;
        [SurfaceSetting("Truck traffic","Sand removal","How much passing wheels expose concrete and steel under sand.","ratio",0,1,.05f)] public float truckSandRemoval=.9f;
        [SurfaceSetting("Truck traffic","Turning radius","Radius of old truck turning arcs on base platforms.","m",3,7,.25f)] public float truckTurnRadius=4.75f;
        [SurfaceSetting("Platform steel","Service lane width","Width of straight tread zones in whole steel panels.","panels",1,4,1)] public float platformSteelLanePanels=2;
        [SurfaceSetting("Platform steel","Finish contrast","Tonal separation of clean, tread and worn steel.","ratio",0,.6f,.05f)] public float platformSteelContrast=.4f;
        [SurfaceSetting("Platform steel","Tread strength","Raised diamond tread contrast on the second steel texture.","ratio",0,.8f,.05f)] public float platformSteelTread=.55f;
        [SurfaceSetting("Platform fence","Member width","Width of steel fence posts inside existing blockers.","m",.04f,.2f,.01f)] public float platformFenceWidth=.10f;
        [SurfaceSetting("Platform fence","Panel spacing","Maximum spacing between platform fence posts.","m",1,3,.1f)] public float platformFenceSpacing=2;
        [SurfaceSetting("Industrial landmarks","Frame width","Visible steel perimeter band inside constructed platforms.","m",.3f,2,.1f)] public float platformFrameWidth=.9f;
        [SurfaceSetting("Industrial landmarks","Frame value","Brightness of exposed steel platform frames.","ratio",.2f,.6f,.01f)] public float platformFrameValue=.30f;
        [SurfaceSetting("Industrial landmarks","Sand opacity limit","Maximum sand opacity on construction; retains substrate identity.","ratio",.15f,.75f,.05f)] public float constructionSandLimit=.38f;
        [SurfaceSetting("Industrial landmarks","Mine yard radius","Industrial apron extent around the actual mine site.","m",3,6,.25f)] public float mineYardRadius=4.75f;
        [SurfaceSetting("Industrial landmarks","Mine yard value","Surface value of the concrete mine apron, separate from base platforms.","ratio",.35f,.75f,.01f)] public float mineYardValue=.4f;
        [SurfaceSetting("Industrial landmarks","Mine edge width","Width of the narrow contact band following the existing mine yard boundary.","m",.1f,.8f,.05f)] public float minePadContactWidth=.4f;
        [SurfaceSetting("Industrial landmarks","Mine contact shade","Strength of the grounded contact shade at the mine yard edge.","ratio",0,.6f,.02f)] public float minePadContactStrength=.46f;
        [SurfaceSetting("Industrial landmarks","Mine access width","Painted mine approach width; no navigation change.","m",2,4.2f,.1f)] public float mineAccessWidth=3.2f;
        [SurfaceSetting("Industrial landmarks","Bridge deposit reach","Sand brought in from bridge approaches, never random deck islands.","m",1,5,.25f)] public float bridgeSandReach=3.25f;
        [SurfaceSetting("Industrial landmarks","Bridge deposit strength","Sand in sheltered entry shoulders and rail corners.","ratio",.2f,.8f,.05f)] public float bridgeSandStrength=.65f;
        [SurfaceSetting("Edge compounds","Scale","Size of perimeter industrial building clusters.","ratio",.7f,1.2f,.05f)] public float edgeCompoundScale=1;
        [SurfaceSetting("Edge compounds","Size variation","Size variation between perimeter compounds.","ratio",0,.2f,.02f)] public float edgeCompoundVariation=.10f;
        [SurfaceSetting("Corporate scenery","Module scale","Size of cargo and extraction machinery.","ratio",.5f,1.2f,.05f)] public float industrialScale=.85f;
        [SurfaceSetting("Corporate scenery","Site reach","Maximum distance of industrial scenery from its actual mine.","m",7,14,.5f)] public float industrialReach=10;
        [SurfaceSetting("Industrial landmarks","Tractor scale","Scale of the two heavy industrial wrecks; full mesh envelope is protected.","ratio",.75f,1.25f,.05f)] public float tractorScale=1;
        [SurfaceSetting("Hangar logistics","Access width","Width of supported painted hangar spurs.","m",2.8f,4,.1f)] public float hangarAccessWidth=3.2f;
        [SurfaceSetting("Hangar logistics","Apron length","Loading pad depth from the actual gate.","m",3,5,.25f)] public float hangarApronLength=3.75f;
        [SurfaceSetting("Hangar logistics","Apron width","Loading pad width at hangar gates.","m",3.2f,5,.1f)] public float hangarApronWidth=3.6f;
        [SurfaceSetting("Hangar logistics","Access reach","Maximum short service connection length.","m",10,32,1)] public float hangarAccessReach=28;
        [SurfaceSetting("Mine utilities","Service scale","Size of service hardware within the existing cargo/extraction footprints.","ratio",.5f,1.2f,.05f)] public float mineUtilityScale=.8f;
        [SurfaceSetting("Bridge fittings","Member width","Width of external bridge support members and fasteners.","m",.04f,.12f,.01f)] public float bridgeMemberWidth=.07f;
        [SurfaceSetting("Bridge fittings","Joint spacing","Longitudinal spacing of deck joints and outer attachment plates.","m",1.5f,4,.25f)] public float bridgeJointSpacing=2.5f;
        [SurfaceSetting("Bridge fittings","Entry wear","Exposed steel scuffing at bridge entry transitions.","ratio",0,.6f,.05f)] public float bridgeEntryWear=.3f;
        public SurfaceRoad[] roads=DefaultRoads();
        private static SurfaceRoad Road(string id,params float[] xy)=>new SurfaceRoad{id=id,points=Enumerable.Range(0,xy.Length/2).Select(i=>new SurfaceRoadPoint(xy[i*2],xy[i*2+1])).ToArray()};
        private static SurfaceRoad[] DefaultRoads()=>new[]{
            Road("west-spine",-18,-38,-19,-18,-19,0,-19,18,-18,38),
            Road("west-south-gate",-46,-46,-30,-46,-23,-43,-18,-38),
            Road("west-north-gate",-46,46,-30,46,-23,43,-18,38),
            Road("west-pocket",-46,-46,-46,-30,-43,-22,-39,-19,-44,-8,-44,8,-40,19,-44,23,-46,30,-46,46),
            Road("west-south-link",-39,-19,-33,-20,-25,-19,-19,-18),
            Road("west-north-link",-40,19,-35,18.5f,-30,17,-25,16,-19,18),
            Road("south-crossing",-18,-38,18,-38),
            Road("central-crossing",-19,0,19,0),
            Road("north-crossing",-18,38,18,38)
        };
        [SurfaceSetting("Service scenery","Bay count","Maximum number of supported clear service compositions; does not affect gameplay.","count",0,4,1)] public float serviceBayCount=1;
        [SurfaceSetting("Service scenery","Equipment scale","Scale of static service tug, crates and markings.","ratio",.5f,1.5f,.05f)] public float serviceEquipmentScale=1;
        [SurfaceSetting("Service scenery","Search reach","Distance from hangars used to find a protected service pocket.","m",6,40,.5f)] public float serviceBayReach=24;
        [SurfaceSetting("Service scenery","Search spread","Spread of deterministic candidate pockets beyond the reach.","m",.1f,1,.05f)] public float serviceBaySpread=.55f;
        public IEnumerable<SurfaceParameter> Parameters()
        {
            foreach(var field in GetType().GetFields())
            {
                var a=field.GetCustomAttribute<SurfaceSettingAttribute>();if(a==null)continue;
                yield return new SurfaceParameter("visual.threeCrossings."+field.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)field.GetValue(this),v=>field.SetValue(this,v));
            }
            if(roads==null)yield break;
            foreach(var road in roads){if(road?.points==null)continue;for(int i=0;i<road.points.Length;i++)
            {
                var point=road.points[i];if(point==null)continue;
                string path="visual.threeCrossings.roads."+road.id+"."+i;
                yield return new SurfaceParameter(path+".x","Road layout",road.id+" "+i+" X","Authored road coordinate; mirrored by half turn.","m",-64,64,.25f,()=>point.x,v=>point.x=v);
                yield return new SurfaceParameter(path+".z","Road layout",road.id+" "+i+" Z","Authored road coordinate; mirrored by half turn.","m",-64,64,.25f,()=>point.z,v=>point.z=v);
            }}
        }
        public void Validate()
        {
            if(id!="three-crossings-materials-v1"||revision<1)throw new ArgumentException("Invalid surface profile identity");
            if(roads==null||roads.Length==0||roads.Any(r=>r==null||string.IsNullOrWhiteSpace(r.id)||r.points==null||r.points.Length<2||r.points.Any(p=>p==null))||roads.Select(r=>r.id).Distinct().Count()!=roads.Length)throw new ArgumentException("Invalid road network");
            foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException("Invalid surface field: "+p.Path);
            if(serviceBayCount!=Mathf.Round(serviceBayCount))throw new ArgumentException("Service bay count must be integral");
            if(roadFeather>=roadWidth/2)throw new ArgumentException("Road feather must fit inside its width");
        }
        public static ThreeCrossingsSurfaceProfile Load()
        {
            var resource=Resources.Load<TextAsset>("ThreeCrossingsSurfaceProfile");
            if(!resource)throw new InvalidOperationException("Missing Three Crossings surface profile");
            var data=JsonUtility.FromJson<ThreeCrossingsSurfaceProfile>(resource.text);data.Validate();return data;
        }
    }
    public sealed class SurfaceParameter
    {
        public readonly string Path,Group,Label,Description,Unit;
        public readonly float Minimum,Maximum,Step;
        public readonly Func<float> Read;
        public readonly Action<float> Write;
        public SurfaceParameter(string path,string group,string label,string description,string unit,float min,float max,float step,Func<float> read,Action<float> write)
        {Path=path;Group=group;Label=label;Description=description;Unit=unit;Minimum=min;Maximum=max;Step=step;Read=read;Write=write;}
    }

    public sealed partial class ThreeCrossingsMaterials : IDisposable
    {
        // Raster precision and sample count are technical quality bounds, not visual tuning.
        public const int MaskSize=512;
        public ThreeCrossingsSurfaceProfile Profile{get;}
        public Texture2D RoadMask{get;private set;}
        public Texture2D ShoreMask{get;private set;}
        public Texture2D RockMask{get;private set;}
        public Texture2D ConstructionMask{get;private set;}
        public MineAccess[] MineAccesses{get;private set;}
        public readonly List<HangarAccess> HangarAccesses=new List<HangarAccess>();
        public EnvironmentArtProfile Art{get;private set;}
        public Texture2D WeatheringMask{get;private set;}
        public readonly struct Segment
        {
            public readonly NavPoint A,B;
            public readonly double StartDistance;
            public Segment(NavPoint a,NavPoint b,double startDistance=0){A=a;B=b;StartDistance=startDistance;}
        }
        public static IEnumerable<Segment> Segments(ThreeCrossingsSurfaceProfile p)
        {
            foreach(var road in p.roads)foreach(int turn in new[]{-1,1})
            {
                double distance=0;
                for(int i=1;i<road.points.Length;i++)
                {
                    var a=new NavPoint(road.points[i-1].x*turn,road.points[i-1].z*turn);
                    var b=new NavPoint(road.points[i].x*turn,road.points[i].z*turn);
                    yield return new Segment(a,b,distance);
                    distance+=Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Z-a.Z,2));
                }
            }
        }
        public readonly struct MineAccess
        {
            public readonly TerritorySite Site;
            public readonly NavPoint Connection,Entry;
            public MineAccess(TerritorySite site,NavPoint connection,NavPoint entry){Site=site;Connection=connection;Entry=entry;}
        }
        public static MineAccess[] PlanMineAccess(ThreeCrossingsSurfaceProfile p,ThreeCrossingsMap map,PlayableProfile gameplay)
        {
            var result=new List<MineAccess>();var segments=Segments(p).ToArray();
            foreach(var mine in TerritoryRules.Sites(gameplay).Where(s=>s.Kind==PlayableBuildingKind.Mine))
            {
                double best=double.MaxValue;MineAccess selected=default;
                foreach(var road in segments)
                {
                    double dx=road.B.X-road.A.X,dz=road.B.Z-road.A.Z,length=dx*dx+dz*dz;if(length<=0)continue;
                    double t=Math.Max(0,Math.Min(1,((mine.Position.X-road.A.X)*dx+(mine.Position.Z-road.A.Z)*dz)/length));
                    var connection=new NavPoint(road.A.X+dx*t,road.A.Z+dz*t);
                    double x=connection.X-mine.Position.X,z=connection.Z-mine.Position.Z,d=Math.Sqrt(x*x+z*z);
                    double reserve=gameplay.MineFootprintRadius+p.mineAccessWidth/2;
                    if(d<=reserve||d>=best)continue;
                    var entry=new NavPoint(mine.Position.X+x/d*reserve,mine.Position.Z+z/d*reserve);
                    if(!map.SupportsSweep(connection,entry,p.mineAccessWidth/2))continue;
                    selected=new MineAccess(mine,connection,entry);best=d;
                }
                if(double.IsPositiveInfinity(best)||best==double.MaxValue)throw new ArgumentException("No supported industrial access to mine "+mine.Id);
                result.Add(selected);
            }
            return result.ToArray();
        }
        private System.Collections.IEnumerator BuildConstruction(ThreeCrossingsMap map,Material material,PlayableProfile gameplay)
        {
            var budget=new LoadingFrameBudget();
            ConstructionMask=new Texture2D(MaskSize,MaskSize,TextureFormat.RGBA32,false,true){name="Constructed frames and mine yards",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            var pixels=new Color[MaskSize*MaskSize];var platforms=map.Supports.Where(s=>s.Id.StartsWith("platform-")).ToArray();
            for(int y=0;y<MaskSize;y++)for(int x=0;x<MaskSize;x++)
            {
                if(x==0&&budget.Expired){yield return null;budget.Restart();}
                var point=new NavPoint(((x+.5)/MaskSize*2-1)*map.HalfExtent,((y+.5)/MaskSize*2-1)*map.HalfExtent);
                float frame=0,yard=0,edge=0,traffic=0;
                foreach(var platform in platforms)if(platform.Bounds.Contains(point))
                {
                    float distance=(float)Math.Sqrt(platform.Bounds.BoundaryDistanceSquared(point));
                    frame=Mathf.Max(frame,1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Profile.platformFrameWidth*.7f,Profile.platformFrameWidth,distance)));
                }
                if(map.SupportsFootprint(point,0))foreach(var access in MineAccesses)
                {
                    double dx=point.X-access.Site.Position.X,dz=point.Z-access.Site.Position.Z;
                    double ax=access.Connection.X-access.Site.Position.X,az=access.Connection.Z-access.Site.Position.Z,len=Math.Sqrt(ax*ax+az*az);ax/=len;az/=len;
                    float along=(float)(dx*ax+dz*az),across=(float)(-dx*az+dz*ax);
                    // Chamfered loading apron aligns with the access, not with a random global patch.
                    float distance=Profile.mineYardRadius-Mathf.Max(Mathf.Max(Mathf.Abs(along),Mathf.Abs(across)),(Mathf.Abs(along)+Mathf.Abs(across))*.72f);
                    float edgeWidth=Profile.minePadContactWidth;
                    float fill=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,edgeWidth,distance));yard=Mathf.Max(yard,fill);
                    edge=Mathf.Max(edge,fill*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(edgeWidth,edgeWidth*2,distance))));
                    float wheel=Mathf.Exp(-Mathf.Pow((Mathf.Abs(across)-Profile.truckWheelSpacing*.5f)/Profile.truckTrackWidth,2));
                    traffic=Mathf.Max(traffic,wheel*fill*Mathf.SmoothStep(0,1,Mathf.InverseLerp((float)gameplay.MineFootprintRadius,(float)gameplay.MineFootprintRadius+1,along)));
                }
                pixels[y*MaskSize+x]=new Color(frame,yard,edge,traffic);
            }
            ConstructionMask.SetPixels(pixels);ConstructionMask.Apply(false,false);material.SetTexture("_ConstructionMask",ConstructionMask);
        }
        public static void ValidateRoads(ThreeCrossingsSurfaceProfile p,ThreeCrossingsMap map)
        {
            p.Validate();
            foreach(var s in Segments(p))
                if(!map.SupportsSweep(s.A,s.B,p.roadWidth/2))throw new ArgumentException("Road corridor hits unsupported terrain: "+s.A.X+","+s.A.Z+" -> "+s.B.X+","+s.B.Z);
        }
        public ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile p,ThreeCrossingsMap map,Material material,PlayableProfile gameplay,bool staged=false)
        {
            Profile=p;Preparation=Initialize(p,map,material,gameplay);
            if(!staged){try{LoadingPreparation.Drain(Preparation);}catch{Dispose();throw;}}
        }
        public System.Collections.IEnumerator Preparation{get;}
        private System.Collections.IEnumerator Initialize(ThreeCrossingsSurfaceProfile p,ThreeCrossingsMap map,Material material,PlayableProfile gameplay)
        {
            var budget=new LoadingFrameBudget();
            ValidateRoads(p,map);MineAccesses=PlanMineAccess(p,map,gameplay);
            RoadMask=new Texture2D(MaskSize,MaskSize,TextureFormat.R8,false,true){name="Three Crossings dirt road mask",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            var pixels=new byte[MaskSize*MaskSize];var segments=Segments(p).ToArray();
            var bridges=map.Supports.Where(s=>s.IsBridge).ToArray();
            var mineReserves=TerritoryRules.Sites(gameplay).Where(s=>s.Kind==PlayableBuildingKind.Mine).ToArray();
            for(int y=0;y<MaskSize;y++)for(int x=0;x<MaskSize;x++)
            {
                if(x==0&&budget.Expired){yield return null;budget.Restart();}
                float wx=(float)(((x+.5)/MaskSize*2-1)*map.HalfExtent),wz=(float)(((y+.5)/MaskSize*2-1)*map.HalfExtent);
                double best=double.MaxValue;
                foreach(var s in segments)
                {
                    double dx=s.B.X-s.A.X,dz=s.B.Z-s.A.Z,length=dx*dx+dz*dz;
                    double t=length>0?Math.Max(0,Math.Min(1,((wx-s.A.X)*dx+(wz-s.A.Z)*dz)/length)):0;
                    double centerX=s.A.X+dx*t,centerZ=s.A.Z+dz*t;
                    float alongLength=(float)Math.Sqrt(length);
                    float endFade=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,4,(float)Math.Min(t,1-t)*alongLength));
                    float along=(float)(s.StartDistance+t*alongLength);
                    float phase=(float)(s.A.X*.17+s.A.Z*.11);
                    float meander=(Mathf.Sin(along/13+phase)*.65f+(Mathf.PerlinNoise((float)((centerX+143.7)/12),(float)((centerZ-87.2)/12))-.5f)*.7f)*p.roadMeander*endFade;
                    double lateral=length>0?meander/Math.Sqrt(length):0;
                    double axisX=centerX-dz*lateral,axisZ=centerZ+dx*lateral;
                    double ax=wx-axisX,az=wz-axisZ;best=Math.Min(best,ax*ax+az*az);
                }
                // Expanded paint is clipped by the same authoritative movement geometry.
                // This does not widen physical passages or paint a route through rock/water.
                float value=Mathf.Clamp01((p.arterialWidth/2-(float)Math.Sqrt(best))/p.roadFeather);
                foreach(var bridge in bridges)
                {
                    float end=(float)bridge.Bounds.MaxX;
                    float away=Mathf.Max(0,Mathf.Abs(wx)-end);
                    float flare=1-Mathf.SmoothStep(0,1,away/p.approachLength);
                    float half=Mathf.Lerp(p.arterialWidth/2,(float)(bridge.Bounds.MaxZ-bridge.Bounds.MinZ)/2+p.roadFeather,flare);
                    float center=(float)(bridge.Bounds.MinZ+bridge.Bounds.MaxZ)/2;
                    float approach=Mathf.Clamp01((half-Mathf.Abs(wz-center))/p.roadFeather);
                    if(away<p.approachLength)value=Mathf.Max(value,approach*flare);
                }
                foreach(var access in MineAccesses)
                {
                    double dx=access.Entry.X-access.Connection.X,dz=access.Entry.Z-access.Connection.Z,length=dx*dx+dz*dz;
                    double t=length>0?Math.Max(0,Math.Min(1,((wx-access.Connection.X)*dx+(wz-access.Connection.Z)*dz)/length)):0;
                    double ax=wx-access.Connection.X-dx*t,az=wz-access.Connection.Z-dz*t;
                    float spur=Mathf.Clamp01((p.mineAccessWidth/2-(float)Math.Sqrt(ax*ax+az*az))/.5f);
                    value=Mathf.Max(value,spur);
                }
                var point=new NavPoint(wx,wz);
                if(!map.SupportsFootprint(point,0))value=0;
                foreach(var mine in mineReserves)
                {
                    double dx=wx-mine.Position.X,dz=wz-mine.Position.Z;
                    value*=Mathf.Clamp01(((float)Math.Sqrt(dx*dx+dz*dz)-(float)gameplay.MineFootprintRadius)/p.roadFeather);
                }
                pixels[y*MaskSize+x]=(byte)Mathf.RoundToInt(Mathf.SmoothStep(0,1,value)*255);
            }
            RoadMask.LoadRawTextureData(pixels);RoadMask.Apply(false,false);
            material.SetTexture("_RoadMask",RoadMask);
            material.SetColor("_GroundTint",Color.HSVToRGB(p.groundHue,p.groundSaturation,p.groundBrightness));
            material.SetColor("_RoadTint",Color.HSVToRGB(p.roadHue,p.roadSaturation,p.roadBrightness));
            material.SetVector("_CrossingsRoadFinish",new Vector4(p.roadCoreShade,p.roadShoulderTone,p.roadRutStrength,p.roadEdgeVariation));
            material.SetVector("_CrossingsMinePad",new Vector4(Linear(p.mineYardValue),p.minePadContactStrength,p.minePadContactWidth,0));
            material.SetVector("_SurfaceNoise",new Vector4(p.broadScale,p.broadContrast,p.grainScale,p.grainContrast));
            // SetVector bypasses the sRGB conversion performed by color properties.
            float Linear(float value)=>QualitySettings.activeColorSpace==ColorSpace.Linear?Mathf.GammaToLinearSpace(value):value;
            material.SetVector("_Concrete",new Vector4(Linear(p.concreteBrightness),p.slabSize,p.jointWidth,p.jointContrast));
            material.SetVector("_Metal",new Vector4(Linear(p.metalBrightness),p.metalPanelLength,p.metalPanelWidth,p.metalSeamContrast));
            material.SetVector("_SurfaceExtras",new Vector4(p.metalGrain,p.rockBrightness,p.metalHighlight,p.metalShininess));
            Art=EnvironmentArtProfile.Load();EnvironmentArt.Apply(material,Art);
            material.SetVector("_CrossingsRockProjection",new Vector4(1,p.rockProjectionTransition,0,0));
            material.SetVector("_CrossingsGroundDetail",new Vector4(1,p.groundOpenDetail,p.groundFootDetail,p.groundGrainRetention));
            material.SetVector("_CrossingsRockFoot",new Vector4(p.rockFootContactWidth,p.rockFootContactStrength,p.rockFootDustReach,p.rockFootDustStrength));
            material.SetFloat("_CrossingsFootPatch",p.rockFootPatchScale);
            // Opt-in map adapter; the shared shader retains its original defaults elsewhere.
            float angle=p.paletteAngle*Mathf.Deg2Rad;
            material.SetVector("_CrossingsPalette",new Vector4(1,Mathf.Cos(angle),Mathf.Sin(angle),p.paletteWidth));
            material.SetVector("_CrossingsRegion",new Vector4(p.paletteIrregularity,p.paletteScale,0,0));
            material.SetColor("_BasaltTint",Color.HSVToRGB(p.graphiteHue,p.graphiteSaturation,p.graphiteValue));
            material.SetColor("_RustTint",Color.HSVToRGB(p.quarryHue,p.quarrySaturation,p.quarryValue));
            material.SetColor("_CrossingsDustB",Color.HSVToRGB(p.quarryDustHue,p.quarryDustSaturation,p.quarryDustValue));
            material.SetVector("_CrossingsConstruction",new Vector4(p.platformMetalWidth,p.platformAge,p.truckWear,p.truckSandRemoval));
            material.SetVector("_CrossingsInfrastructure",new Vector4(Linear(p.platformFrameValue),p.constructionSandLimit,p.concreteDetailContrast,0));
            ShoreMask=new Texture2D(MaskSize,MaskSize,TextureFormat.R8,false,true){name="Map shore distance",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            // Only the two land masses define the riverbank. Inland platforms and ramps
            // overlap those masses and must not create wet stripes around their edges.
            var banks=map.Supports.Where(s=>s.Id=="west-bank"||s.Id=="east-bank").ToArray();
            for(int y=0;y<MaskSize;y++)for(int x=0;x<MaskSize;x++)
            {
                if(x==0&&budget.Expired){yield return null;budget.Restart();}
                var point=new NavPoint(((x+.5)/MaskSize*2-1)*map.HalfExtent,((y+.5)/MaskSize*2-1)*map.HalfExtent);
                double distance=double.MaxValue;
                foreach(var bank in banks)distance=Math.Min(distance,bank.Bounds.BoundaryDistanceSquared(point));
                pixels[y*MaskSize+x]=(byte)Mathf.RoundToInt(Mathf.Clamp01((float)Math.Sqrt(distance)/EnvironmentArt.ShoreDistanceRange)*255);
            }
            RockMask=new Texture2D(MaskSize,MaskSize,TextureFormat.R8,false,true){name="Natural rock footprint distance",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            var rockPixels=new byte[MaskSize*MaskSize];
            // Base geological masses only: raised crowns and constructed platform walls
            // must not paint extra soil bands. Distances are presentation data only.
            var rocks=map.Solids.Where(r=>r.Top>=map.WaterDepth&&r.Bottom<=0).ToArray();
            for(int y=0;y<MaskSize;y++)for(int x=0;x<MaskSize;x++)
            {
                if(x==0&&budget.Expired){yield return null;budget.Restart();}
                var point=new NavPoint(((x+.5)/MaskSize*2-1)*map.HalfExtent,((y+.5)/MaskSize*2-1)*map.HalfExtent);
                double distance=double.MaxValue;
                foreach(var rock in rocks)distance=Math.Min(distance,rock.Contains(point)?0:rock.BoundaryDistanceSquared(point));
                rockPixels[y*MaskSize+x]=(byte)Mathf.RoundToInt(Mathf.Clamp01((float)Math.Sqrt(distance)/EnvironmentArt.ShoreDistanceRange)*255);
            }
            RockMask.LoadRawTextureData(rockPixels);RockMask.Apply(false,false);material.SetTexture("_RockMask",RockMask);
            ShoreMask.LoadRawTextureData(pixels);ShoreMask.Apply(false,false);material.SetTexture("_ShoreMask",ShoreMask);
            yield return BuildConstruction(map,material,gameplay);
        }
        public void BuildWeathering(ThreeCrossingsMap map,IEnumerable<ThreeCrossingsDetails.Placement> details,Material material)=>LoadingPreparation.Drain(PrepareWeathering(map,details,material));
        public System.Collections.IEnumerator PrepareWeathering(ThreeCrossingsMap map,IEnumerable<ThreeCrossingsDetails.Placement> details,Material material)
        {
            var budget=new LoadingFrameBudget();
            WeatheringMask=new Texture2D(MaskSize,MaskSize,TextureFormat.RGBA32,false,true){name="Dust contact and wear mask",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            var pixels=new Color[MaskSize*MaskSize];
            var constructions=map.Supports.Where(s=>s.IsBridge||s.Id.StartsWith("platform-")).ToArray();
            var segments=Segments(Profile).Concat(MineAccesses.Select(a=>new Segment(a.Connection,a.Entry))).Concat(HangarAccesses.SelectMany(a=>a.Segments)).ToArray();
            for(int y=0;y<MaskSize;y++)for(int x=0;x<MaskSize;x++)
            {
                if(x==0&&budget.Expired){yield return null;budget.Restart();}
                float wx=(float)(((x+.5)/MaskSize*2-1)*map.HalfExtent),wz=(float)(((y+.5)/MaskSize*2-1)*map.HalfExtent);
                var point=new NavPoint(wx,wz);float dust=0,wear=0,tread=0;
                foreach(var support in constructions)
                {
                    if(!support.Bounds.Contains(point))continue;
                    if(support.IsBridge)
                    {
                        float entry=(float)Math.Min(Math.Abs(wx-support.Bounds.MinX),Math.Abs(support.Bounds.MaxX-wx));
                        float centerZ=(float)(support.Bounds.MinZ+support.Bounds.MaxZ)*.5f;
                        float half=(float)(support.Bounds.MaxZ-support.Bounds.MinZ)*.5f;
                        float shoulder=Mathf.SmoothStep(0,1,Mathf.InverseLerp(Profile.truckWheelSpacing*.5f+Profile.truckTrackWidth,half-.1f,Mathf.Abs(wz-centerZ)));
                        float entryReach=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,Profile.bridgeSandReach,entry));
                        dust=Mathf.Max(dust,entryReach*shoulder*Profile.bridgeSandStrength);
                        wear=Mathf.Max(wear,entryReach*(1-shoulder)*Profile.bridgeEntryWear);
                        continue;
                    }
                    float distance=(float)Math.Sqrt(support.Bounds.BoundaryDistanceSquared(point));
                    float windX=Mathf.Cos(Art.dustWind*Mathf.Deg2Rad),windZ=Mathf.Sin(Art.dustWind*Mathf.Deg2Rad);
                    float along=(wx*windX+wz*windZ)/Art.dustPatchLength;
                    float across=(-wx*windZ+wz*windX)/Art.dustPatchLength;
                    // Warped, stretched noise describes the reusable tongue shape. Unlike an
                    // edge-distance band, its zero regions remain clean even at the perimeter.
                    float warp=Mathf.PerlinNoise(wx/Art.blendPatchSize+7,wz/Art.blendPatchSize+23)-.5f;
                    float deposit=Mathf.PerlinNoise(along*.65f+31,across*1.7f+warp*.45f+19);
                    float coverage=Mathf.SmoothStep(0,1,Mathf.InverseLerp(1-Art.dustCoverage,1-Art.dustCoverage+.18f,deposit));
                    float reach=Art.dustReach*coverage;
                    float tongue=reach>0?Mathf.Clamp01(1-distance/reach):0;
                    dust=Mathf.Max(dust,coverage*tongue);
                    if(support.Id.StartsWith("platform-")&&!support.Id.EndsWith("-ramp"))
                    {
                        // Sparse interior deposits, never a uniform beige slab or an edge ring.
                        float patch=Mathf.PerlinNoise(along+57,across*.8f+13);
                        dust=Mathf.Max(dust,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.42f,.68f,patch))*Profile.platformSand);
                        float cx=(float)(support.Bounds.MinX+support.Bounds.MaxX)*.5f;
                        float cz=(float)(support.Bounds.MinZ+support.Bounds.MaxZ)*.5f;
                        float dx=wx-cx,dz=wz-cz,radius=Mathf.Sqrt(dx*dx+dz*dz);
                        float arc=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.5f,.25f,Mathf.Sin(Mathf.Atan2(dz,dx)+.6f)));
                        float tireDistance=Mathf.Abs(Mathf.Abs(radius-Profile.truckTurnRadius)-Profile.truckWheelSpacing*.5f);
                        float turnWear=Mathf.Exp(-Mathf.Pow(tireDistance/Profile.truckTrackWidth,2))*arc;
                        float straightDistance=Mathf.Abs(Mathf.Abs(dz)-Profile.truckWheelSpacing*.5f);
                        float entryWear=Mathf.Exp(-Mathf.Pow(straightDistance/Profile.truckTrackWidth,2));
                        wear=Mathf.Max(wear,Mathf.Max(turnWear,entryWear)*(.65f+.35f*Mathf.PerlinNoise(wx*.3f,wz*.3f)));
                        tread=Mathf.Max(tread,.5f+.5f*Mathf.Cos(dx/Profile.truckTreadPitch*Mathf.PI*2));
                    }
                }
                float road=RoadMask.GetPixel(x,y).r;
                var construction=ConstructionMask.GetPixel(x,y);
                wear=Mathf.Max(wear,construction.a);
                if(road>.1f)
                {
                    double distance=double.MaxValue;float phase=0;
                    foreach(var segment in segments)
                    {
                        double dx=segment.B.X-segment.A.X,dz=segment.B.Z-segment.A.Z;
                        double t=Math.Max(0,Math.Min(1,((wx-segment.A.X)*dx+(wz-segment.A.Z)*dz)/(dx*dx+dz*dz)));
                        double ax=wx-segment.A.X-dx*t,az=wz-segment.A.Z-dz*t,d=Math.Sqrt(ax*ax+az*az);
                        if(d<distance){distance=d;phase=(float)(segment.StartDistance+t*Math.Sqrt(dx*dx+dz*dz));}
                    }
                    float wheel=Mathf.Exp(-Mathf.Pow(((float)distance-Profile.truckWheelSpacing*.5f)/Profile.truckTrackWidth,2));
                    wear=Mathf.Max(wear,wheel*road*(.65f+.35f*Mathf.PerlinNoise(wx*.3f,wz*.3f)));
                    tread=Mathf.Max(tread,.5f+.5f*Mathf.Cos(phase/Profile.truckTreadPitch*Mathf.PI*2));
                }
                // Alpha is tread modulation inside the blue wheel corridor; RGB contracts stay intact.
                pixels[y*MaskSize+x]=new Color(dust,0,wear,tread);
            }
            foreach(var item in details)
            {
                if(item.Kind.StartsWith("Perimeter"))continue; // Building wear is local to service footprints.
                float radius=item.Radius*1.4f;
                int cx=Mathf.RoundToInt((item.Position.x/(float)(map.HalfExtent*2)+.5f)*MaskSize),cy=Mathf.RoundToInt((item.Position.z/(float)(map.HalfExtent*2)+.5f)*MaskSize);
                int extent=Mathf.CeilToInt(radius/(float)(map.HalfExtent*2)*MaskSize)+1;
                for(int y=Math.Max(0,cy-extent);y<Math.Min(MaskSize,cy+extent+1);y++)for(int x=Math.Max(0,cx-extent);x<Math.Min(MaskSize,cx+extent+1);x++)
                {
                    float wx=(float)(((x+.5)/MaskSize*2-1)*map.HalfExtent),wz=(float)(((y+.5)/MaskSize*2-1)*map.HalfExtent);
                    float t=Vector2.Distance(new Vector2(wx,wz),new Vector2(item.Position.x,item.Position.z))/radius;
                    pixels[y*MaskSize+x].g=Mathf.Max(pixels[y*MaskSize+x].g,1-Mathf.SmoothStep(0,1,t));
                }
            }
            WeatheringMask.SetPixels(pixels);WeatheringMask.Apply(false,false);material.SetTexture("_WeatheringMask",WeatheringMask);
        }
        public void Dispose(){if(ConstructionMask)UnityEngine.Object.Destroy(ConstructionMask);if(WeatheringMask)UnityEngine.Object.Destroy(WeatheringMask);if(RoadMask)UnityEngine.Object.Destroy(RoadMask);if(ShoreMask)UnityEngine.Object.Destroy(ShoreMask);if(RockMask)UnityEngine.Object.Destroy(RockMask);}
    }
}
