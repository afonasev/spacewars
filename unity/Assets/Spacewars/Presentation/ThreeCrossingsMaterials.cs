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
        [SurfaceSetting("Roads","Soft edge","Feather width inside the road boundary.","m",.1f,2,.1f)] public float roadFeather=.9f;
        [SurfaceSetting("Roads","Hue","Compacted earth hue.","turn",0,1,.01f)] public float roadHue=.10f;
        [SurfaceSetting("Roads","Saturation","Compacted earth saturation.","ratio",0,.6f,.01f)] public float roadSaturation=.28f;
        [SurfaceSetting("Roads","Brightness","Road contrast against surrounding ground.","ratio",.15f,.9f,.01f)] public float roadBrightness=.62f;
        [SurfaceSetting("Concrete","Brightness","Concrete surface base value.","ratio",.2f,.9f,.01f)] public float concreteBrightness=.69f;
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
        [SurfaceSetting("Rock","Brightness multiplier","Preserve authored rock tier colors while scaling brightness.","ratio",.5f,1.5f,.01f)] public float rockBrightness=1;
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

    public sealed class ThreeCrossingsMaterials : IDisposable
    {
        // Raster precision and sample count are technical quality bounds, not visual tuning.
        public const int MaskSize=512;
        public ThreeCrossingsSurfaceProfile Profile{get;}
        public Texture2D RoadMask{get;}
        public Texture2D ShoreMask{get;}
        public Texture2D RockMask{get;}
        public EnvironmentArtProfile Art{get;}
        public Texture2D WeatheringMask{get;private set;}
        public readonly struct Segment
        {
            public readonly NavPoint A,B;
            public Segment(NavPoint a,NavPoint b){A=a;B=b;}
        }
        public static IEnumerable<Segment> Segments(ThreeCrossingsSurfaceProfile p)
        {
            foreach(var road in p.roads)foreach(int turn in new[]{-1,1})for(int i=1;i<road.points.Length;i++)
                yield return new Segment(new NavPoint(road.points[i-1].x*turn,road.points[i-1].z*turn),new NavPoint(road.points[i].x*turn,road.points[i].z*turn));
        }
        public static void ValidateRoads(ThreeCrossingsSurfaceProfile p,ThreeCrossingsMap map)
        {
            p.Validate();
            foreach(var s in Segments(p))
                if(!map.SupportsSweep(s.A,s.B,p.roadWidth/2))throw new ArgumentException("Road corridor hits unsupported terrain: "+s.A.X+","+s.A.Z+" -> "+s.B.X+","+s.B.Z);
        }
        public ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile p,ThreeCrossingsMap map,Material material,PlayableProfile gameplay)
        {
            ValidateRoads(p,map);Profile=p;
            RoadMask=new Texture2D(MaskSize,MaskSize,TextureFormat.R8,false,true){name="Three Crossings dirt road mask",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            var pixels=new byte[MaskSize*MaskSize];var segments=Segments(p).ToArray();
            var bridges=map.Supports.Where(s=>s.IsBridge).ToArray();
            var mineReserves=TerritoryRules.Sites(gameplay).Where(s=>s.Kind==PlayableBuildingKind.Mine).ToArray();
            for(int y=0;y<MaskSize;y++)for(int x=0;x<MaskSize;x++)
            {
                float wx=(float)(((x+.5)/MaskSize*2-1)*map.HalfExtent),wz=(float)(((y+.5)/MaskSize*2-1)*map.HalfExtent);
                double best=double.MaxValue;
                foreach(var s in segments)
                {
                    double dx=s.B.X-s.A.X,dz=s.B.Z-s.A.Z,length=dx*dx+dz*dz;
                    double t=length>0?Math.Max(0,Math.Min(1,((wx-s.A.X)*dx+(wz-s.A.Z)*dz)/length)):0;
                    double ax=wx-s.A.X-dx*t,az=wz-s.A.Z-dz*t;best=Math.Min(best,ax*ax+az*az);
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
            material.SetVector("_SurfaceNoise",new Vector4(p.broadScale,p.broadContrast,p.grainScale,p.grainContrast));
            // SetVector bypasses the sRGB conversion performed by color properties.
            float Linear(float value)=>QualitySettings.activeColorSpace==ColorSpace.Linear?Mathf.GammaToLinearSpace(value):value;
            material.SetVector("_Concrete",new Vector4(Linear(p.concreteBrightness),p.slabSize,p.jointWidth,p.jointContrast));
            material.SetVector("_Metal",new Vector4(Linear(p.metalBrightness),p.metalPanelLength,p.metalPanelWidth,p.metalSeamContrast));
            material.SetVector("_SurfaceExtras",new Vector4(p.metalGrain,p.rockBrightness,p.metalHighlight,p.metalShininess));
            Art=EnvironmentArtProfile.Load();EnvironmentArt.Apply(material,Art);
            ShoreMask=new Texture2D(MaskSize,MaskSize,TextureFormat.R8,false,true){name="Map shore distance",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            // Only the two land masses define the riverbank. Inland platforms and ramps
            // overlap those masses and must not create wet stripes around their edges.
            var banks=map.Supports.Where(s=>s.Id=="west-bank"||s.Id=="east-bank").ToArray();
            for(int y=0;y<MaskSize;y++)for(int x=0;x<MaskSize;x++)
            {
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
                var point=new NavPoint(((x+.5)/MaskSize*2-1)*map.HalfExtent,((y+.5)/MaskSize*2-1)*map.HalfExtent);
                double distance=double.MaxValue;
                foreach(var rock in rocks)distance=Math.Min(distance,rock.Contains(point)?0:rock.BoundaryDistanceSquared(point));
                rockPixels[y*MaskSize+x]=(byte)Mathf.RoundToInt(Mathf.Clamp01((float)Math.Sqrt(distance)/EnvironmentArt.ShoreDistanceRange)*255);
            }
            RockMask.LoadRawTextureData(rockPixels);RockMask.Apply(false,false);material.SetTexture("_RockMask",RockMask);
            ShoreMask.LoadRawTextureData(pixels);ShoreMask.Apply(false,false);material.SetTexture("_ShoreMask",ShoreMask);
        }
        public void BuildWeathering(ThreeCrossingsMap map,IEnumerable<ThreeCrossingsDetails.Placement> details,Material material)
        {
            WeatheringMask=new Texture2D(MaskSize,MaskSize,TextureFormat.RGBA32,false,true){name="Dust contact and wear mask",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            var pixels=new Color[MaskSize*MaskSize];
            var constructions=map.Supports.Where(s=>s.IsBridge||s.Id.StartsWith("platform-")).ToArray();
            var segments=Segments(Profile).ToArray();
            for(int y=0;y<MaskSize;y++)for(int x=0;x<MaskSize;x++)
            {
                float wx=(float)(((x+.5)/MaskSize*2-1)*map.HalfExtent),wz=(float)(((y+.5)/MaskSize*2-1)*map.HalfExtent);
                var point=new NavPoint(wx,wz);float dust=0,wear=0;
                foreach(var support in constructions)
                {
                    if(!support.Bounds.Contains(point))continue;
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

                }
                float road=RoadMask.GetPixel(x,y).r;
                if(road>.1f)
                {
                    double distance=double.MaxValue;
                    foreach(var segment in segments)
                    {
                        double dx=segment.B.X-segment.A.X,dz=segment.B.Z-segment.A.Z;
                        double t=Math.Max(0,Math.Min(1,((wx-segment.A.X)*dx+(wz-segment.A.Z)*dz)/(dx*dx+dz*dz)));
                        double ax=wx-segment.A.X-dx*t,az=wz-segment.A.Z-dz*t;distance=Math.Min(distance,Math.Sqrt(ax*ax+az*az));
                    }
                    // Tire spacing and thin track profile are authored surface marks, not unit dimensions.
                    wear=Mathf.Exp(-Mathf.Pow(((float)distance-1.25f)/.24f,2))*road*Mathf.PerlinNoise(wx*.4f,wz*.4f);
                }
                pixels[y*MaskSize+x]=new Color(dust,0,wear,1);
            }
            foreach(var item in details)
            {
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
        public void Dispose(){if(WeatheringMask)UnityEngine.Object.Destroy(WeatheringMask);if(RoadMask)UnityEngine.Object.Destroy(RoadMask);if(ShoreMask)UnityEngine.Object.Destroy(ShoreMask);if(RockMask)UnityEngine.Object.Destroy(RockMask);}
    }
}
