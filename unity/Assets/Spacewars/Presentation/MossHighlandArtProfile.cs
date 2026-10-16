using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class MossHighlandArtProfile
    {
        public string id="moss-highland-wet-v1";public int revision=11;
        [SurfaceSetting("Highland textures","Detail tile","Fine albedo scale, independent of geology and paint.","m",4,20,.25f)] public float detailTile=9;
        [SurfaceSetting("Highland textures","Variant region","Irregular continuous sibling regions.","m",8,50,1)] public float variantScale=13;
        [SurfaceSetting("Highland textures","Variant blend","Only region boundaries blend sibling images.","ratio",.02f,.3f,.01f)] public float variantBlend=.06f;
        [SurfaceSetting("Highland textures","Detail contrast","Calibrated texture contrast.","ratio",0,1,.05f)] public float detailContrast=.78f;
        [SurfaceSetting("Highland ground","Natural feather","Soft natural role boundaries; metal stays sharp.","m",.1f,3,.1f)] public float naturalFeather=.9f;
        [SurfaceSetting("Highland ground","Damp soil patches","World scale of irregular moss/soil pockets.","m",6,30,.5f)] public float soilScale=16;
        [SurfaceSetting("Highland ground","Damp soil coverage","Amount of exposed damp earth in moss.","ratio",0,.65f,.025f)] public float soilCoverage=.5f;
        [SurfaceSetting("Highland ground","Moss hue","Restrained wet green hue.","turn",.17f,.35f,.005f)] public float mossHue=.235f;
        [SurfaceSetting("Highland ground","Moss saturation","Green moss saturation.","ratio",.1f,.8f,.025f)] public float mossSaturation=.42f;
        [SurfaceSetting("Highland ground","Moss value","Moss brightness under scene lighting.","ratio",.2f,.7f,.025f)] public float mossValue=.38f;
        [SurfaceSetting("Highland geology","Rock value","Cool grey rock brightness.","ratio",.2f,.7f,.025f)] public float rockValue=.47f;
        [SurfaceSetting("Highland geology","Ridge erosion","Inward-only authored rock fracture.","m",0,.8f,.05f)] public float ridgeErosion=.6f;
        [SurfaceSetting("Highland geology","Strata spacing","Cliff mineral layer spacing.","m",.2f,1,.05f)] public float strataSpacing=.5f;
        [SurfaceSetting("Highland geology","Strata contrast","Restrained cliff band contrast.","ratio",0,.4f,.025f)] public float strataContrast=.12f;
        [SurfaceSetting("Highland detail","Micro relief","Bounded finite shading normals, no displacement of traversable ground.","ratio",0,.3f,.025f)] public float normalSlope=.075f;
        [SurfaceSetting("Highland detail","Detail density","Sparse small rock clusters inside authored solid areas.","ratio",0,1,.05f)] public float detailDensity=.45f;
        [SurfaceSetting("Highland detail","Pebble size","Maximum half width of decorative rock pockets.","m",.1f,.6f,.025f)] public float pebbleSize=.35f;
        [SurfaceSetting("Highland detail","Shadow receiver offset","Normal offset against shadow striping.","m",0,.1f,.005f)] public float receiverBias=.025f;
        [SurfaceSetting("Highland ground","Exposed stone coverage","Irregular flat rock outcrops within the moss, without collision changes.","ratio",0,.5f,.025f)] public float stoneCoverage=.18f;
        [SurfaceSetting("Highland ground","Patch feather","Natural transition softness in normalized patch noise.","ratio",.03f,.3f,.01f)] public float patchFeather=.09f;
        [SurfaceSetting("Highland ground","Moss macro variation","Broad tonal variation independent of fine albedo.","ratio",0,.5f,.025f)] public float macroContrast=.25f;
        [SurfaceSetting("Highland apron","Slab size","World-space concrete slab width and length.","m",1,6,.25f)] public float slabSize=3;
        [SurfaceSetting("Highland apron","Joint width","Flush concrete expansion joint width.","m",.015f,.15f,.005f)] public float jointWidth=.045f;
        [SurfaceSetting("Highland apron","Joint contrast","Darkening at slab joints.","ratio",0,.7f,.025f)] public float jointContrast=.38f;
        [SurfaceSetting("Highland apron","Slab variation","Restrained value variation between concrete slabs.","ratio",0,.3f,.025f)] public float slabVariation=.12f;
        [SurfaceSetting("Highland apron","Concrete value","Neutral concrete brightness before scene lighting.","ratio",.2f,.7f,.025f)] public float concreteValue=.46f;
        [SurfaceSetting("Highland apron","Drain width","Flush drainage strip inside construction edges.","m",.1f,.8f,.05f)] public float drainWidth=.35f;
        [SurfaceSetting("Highland apron","Drain pitch","Spacing of drainage grate bars.","m",.08f,.4f,.02f)] public float drainPitch=.16f;
        [SurfaceSetting("Highland apron","Drain contrast","Darkness of recessed drain slots.","ratio",0,.8f,.025f)] public float drainContrast=.8f;
        [SurfaceSetting("Highland geology","Crevice contrast","Broad irregular weathering on vertical rock.","ratio",0,.5f,.025f)] public float creviceContrast=.22f;
        [SurfaceSetting("Highland apron","Service cover size","Width of flush service access covers.","m",.3f,1.2f,.05f)] public float coverSize=.65f;
        [SurfaceSetting("Highland apron","Service cover frequency","Fraction of slabs with a service cover.","ratio",0,.3f,.025f)] public float coverFrequency=.125f;
        [SurfaceSetting("Highland textures","Rock tile","Rock texture scale, independent of moss and macro patches.","m",1,12,.25f)] public float rockTile=5;
        [SurfaceSetting("Highland textures","Soil tile","Dry stony soil texture scale.","m",1,12,.25f)] public float soilTile=10;
        [SurfaceSetting("Highland textures","Concrete tile","Concrete pores and crack texture scale.","m",1,12,.25f)] public float concreteTile=3;
        [SurfaceSetting("Highland textures","Source colour","Preserve original moss/stone pigment rather than tint every stone green.","ratio",0,1,.05f)] public float sourceColorStrength=1;
        [SurfaceSetting("Highland apron","Weathering","Concrete texture contrast, stains and edge wear.","ratio",0,1,.05f)] public float apronWeathering=.65f;
        [SurfaceSetting("Highland apron","Moss ingress reach","Maximum flush moss reach inside existing construction footprints.","m",.1f,1.5f,.05f)] public float apronMossReach=.7f;
        [SurfaceSetting("Highland apron","Moss ingress strength","Sparse moss at weathered apron edges; leaves drainage visible.","ratio",0,.8f,.05f)] public float apronMossStrength=.5f;
        [SurfaceSetting("Highland textures","Pattern warp","Slow continuous distortion breaks repeated rock alignment while retaining sharp detail.","m",0,3,.1f)] public float textureWarp=1.8f;
        [SurfaceSetting("Highland apron","Building margin","Visual apron margin beyond all building footprints; factory exits may continue onto natural ground.","m",.75f,2,.25f)] public float apronMargin=.75f;
        [SurfaceSetting("Highland ground","Dry soil value","Brightness of approved dry stony soil, independent of moss and rock.","ratio",.25f,.65f,.025f)] public float soilValue=.41f;
        [SurfaceSetting("Highland transitions","Scree reach","Width of fine cliff-foot debris fading into soil.","m",0.5f,4f,0.1f)] public float screeReach=2.4f;
        [SurfaceSetting("Highland transitions","Scree density","Maximum chip coverage beside cliffs.","ratio",0f,1f,0.05f)] public float screeDensity=0.8f;
        [SurfaceSetting("Highland transitions","Scree size","World spacing of small irregular shaded chips; no geometry.","m",0.08f,0.4f,0.02f)] public float screeSize=0.22f;
        [SurfaceSetting("Highland transitions","Compacted edge width","Narrow soil transition outside unchanged apron footprints.","m",0.25f,1.5f,0.05f)] public float apronEdgeReach=0.75f;
        [SurfaceSetting("Highland transitions","Edge dust","Restrained dust on concrete near drainage.","ratio",0f,0.5f,0.025f)] public float apronDust=0.18f;
        [SurfaceSetting("Highland transitions","Drain moss","Sparse moss beside drain lips, preserving open slots.","ratio",0f,0.5f,0.025f)] public float drainMoss=0.18f;
        [SurfaceSetting("Highland transitions","Strata tilt","Variation in rock layer direction across irregular regions.","ratio",0f,1f,0.05f)] public float strataTilt=0.55f;
        [SurfaceSetting("Highland transitions","Rock region","World scale of independently oriented cliff texture regions.","m",4f,20f,0.5f)] public float strataRegion=8f;
        [SurfaceSetting("Highland transitions","Compacted texture","Reduction of loose clod contrast in trampled edge soil.","ratio",0,1,.05f)] public float apronCompaction=.55f;
        [SurfaceSetting("Highland transitions","Compacted value","Relative brightness of trampled edge soil.","ratio",.5f,1,.025f)] public float apronSoilValue=.85f;
        [SurfaceSetting("Highland geology","Cliff face contrast","Material value separation of vertical rock faces from upper shelves, independent of cast shadows.","ratio",0,.6f,.025f)] public float cliffFaceContrast=.35f;
        [SurfaceSetting("Highland cliff sample","Edge retreat","Inward-only lip retreat in the single southwest review sample. Zero restores the accepted edge.","m",0,.45f,.025f)] public float cliffEdgeInset=.4f;
        [SurfaceSetting("Highland cliff sample","Lip chips","Vertical chips below the original top in the review sample.","m",0,.8f,.05f)] public float cliffChipDepth=.55f;
        [SurfaceSetting("Highland cliff sample","Fracture scale","World scale of irregular lip cuts and face facets in the review sample.","m",.75f,4,.25f)] public float cliffFractureScale=1.75f;
        [SurfaceSetting("Highland routes","Ramp soil strength","Compacted ground on the four real ramps and their approaches.","ratio",0,1,.05f)] public float rampSoilStrength=.9f;
        [SurfaceSetting("Highland routes","Ramp approach","Continuation beyond each ramp onto level ground.","m",1,6,.25f)] public float rampApproach=4;
        [SurfaceSetting("Highland apron","Role contrast","Distinct HQ, side and centre finish without changing construction envelopes.","ratio",0,.5f,.025f)] public float siteRoleContrast=.3f;
        [SurfaceSetting("Highland border","Mountain relief","Relief within the fixed authoritative mountain volume; footprint never changes.","ratio",.5f,1,.05f)] public float mountainRelief=1;
        [SurfaceSetting("Highland border","Mountain moss","Moss on broad mountain shoulders, leaving steep faces exposed.","ratio",0,.8f,.05f)] public float mountainMoss=.55f;
        [SurfaceSetting("Highland border","Water value","Deep river brightness; rivers remain impassable at every setting.","ratio",.15f,.55f,.025f)] public float waterValue=.32f;
        [SurfaceSetting("Highland border","Water ripples","Restrained current highlights on the river surface.","ratio",0,.4f,.025f)] public float waterRipples=.18f;
        [SurfaceSetting("Highland border","Mountain shadow offset","Shadow receiver offset for steep mountain facets; independent of the original cliffs.","m",.025f,.2f,.005f)] public float mountainShadowBias=.12f;
        public Color RiverColor=>new Color(waterValue*.36f,waterValue*.85f,waterValue);
        public IEnumerable<SurfaceParameter> Parameters(){foreach(var field in GetType().GetFields()){var a=field.GetCustomAttribute<SurfaceSettingAttribute>();if(a!=null)yield return new SurfaceParameter("visual.mossHighland."+field.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)field.GetValue(this),v=>field.SetValue(this,v));}}
        public void Validate(){if(id!="moss-highland-wet-v1"||revision<1)throw new ArgumentException("Invalid highland art identity");foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException("Invalid highland art: "+p.Path);}
        public static MossHighlandArtProfile Load(){var p=JsonUtility.FromJson<MossHighlandArtProfile>(Resources.Load<TextAsset>("MossHighlandArtProfile").text);p.Validate();return p;}
        // Source roles remain content identifiers; added soil/scree are presentation only.
        public static readonly string[] Roles={"mountain.base-dark","mountain.base-mid","metal-deck","scree","moss-ground","damp-soil","pebbles"};
        public static int Role(string id){int i=Array.IndexOf(Roles,id);if(i<0)throw new ArgumentException("Unknown highland material: "+id);return i;}
        public Color Palette(int role){switch(role){case 0:return new Color(rockValue*.8f,rockValue*.85f,rockValue*.83f);case 1:return new Color(rockValue,rockValue*1.04f,rockValue*.98f);case 2:return new Color(.40f,.44f,.45f);case 3:return new Color(.40f,.41f,.35f);case 4:return Color.HSVToRGB(mossHue,mossSaturation,mossValue);case 5:return new Color(soilValue,soilValue,soilValue);case 6:return new Color(.45f,.46f,.38f);default:throw new ArgumentOutOfRangeException(nameof(role));}}
        public Material Material(Texture fog,Vector4 bounds,Color fogColor){
            Validate();var shader=Resources.Load<Shader>("MossHighlandSurface");if(!shader||!shader.isSupported)throw new InvalidOperationException("Missing highland shader");var m=new Material(shader){name=id+" @"+revision};
            EnvironmentArt.BindFamily(m,"Dark","Environment/NaturalFrontier/Rock",new Vector3(.13677501f,.13289979f,.14225178f));
            EnvironmentArt.BindFamily(m,"Stone","MossHighlandArt/RockyMoss",new Vector3(.074716752f,.069029041f,.071670501f));
            EnvironmentArt.BindFamily(m,"Sand","MossHighlandArt/DrySoil",new Vector3(.18059205f,.15571835f,.16584551f));
            EnvironmentArt.BindFamily(m,"Concrete","Environment/NaturalFrontier/Concrete",new Vector3(.38497328f,.37142344f,.37949659f));
            m.SetVector("_BorderSettings",new Vector4(mountainMoss,waterValue,waterRipples,mountainShadowBias));
            m.SetVector("_RouteReadability",new Vector4(rampSoilStrength,siteRoleContrast,0,0));
            m.SetVector("_Scree",new Vector4(screeReach,screeDensity,screeSize,apronSoilValue));m.SetVector("_EdgeTransition",new Vector4(apronEdgeReach,apronDust,drainMoss,apronCompaction));m.SetVector("_CliffVariation",new Vector4(strataTilt,strataRegion,cliffFaceContrast,0));
            m.SetVector("_RoleDetail",new Vector4(rockTile,soilTile,concreteTile,sourceColorStrength));m.SetVector("_ApronWeather",new Vector4(apronWeathering,apronMossReach,apronMossStrength,0));
            m.SetVector("_DetailSettings",new Vector4(detailTile,variantScale,variantBlend,detailContrast));m.SetVector("_NaturalPattern",new Vector4(soilScale,soilCoverage,naturalFeather,textureWarp));m.SetVector("_Geology",new Vector4(strataSpacing,strataContrast,creviceContrast,0));m.SetVector("_HighlandPattern",new Vector4(stoneCoverage,patchFeather,macroContrast,0));m.SetVector("_ApronSlabs",new Vector4(slabSize,jointWidth,jointContrast,slabVariation));m.SetVector("_ApronFinish",new Vector4(concreteValue,drainWidth,drainPitch,drainContrast));m.SetVector("_ApronCovers",new Vector4(coverSize,coverFrequency,0,0));m.SetFloat("_NormalSlope",normalSlope);m.SetFloat("_ReceiverBias",receiverBias);
            var colors=new Vector4[7];for(int i=0;i<7;i++){var c=Palette(i).linear;colors[i]=new Vector4(c.r,c.g,c.b,1);}m.SetVectorArray("_RoleTints",colors);m.SetTexture("_FogMask",fog);m.SetVector("_FogBounds",bounds);m.SetColor("_FogColor",fogColor);return m;
        }
    }
}
