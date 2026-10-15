using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class BasaltCrownArtProfile
    {
        public string id="basalt-crown-warm-stone-v1"; public int revision=4;
        [SurfaceSetting("Basalt Crown textures","Stone tile","World-space scale of fine stone albedo, independent of material paint.","m",2,16,.25f)] public float detailTile=7;
        [SurfaceSetting("Basalt Crown textures","Variant region","Size of irregular sibling regions; no square selector grid.","m",8,50,1)] public float variantScale=23;
        [SurfaceSetting("Basalt Crown textures","Variant transition","Width of soft transitions between sibling images.","ratio",.02f,.3f,.01f)] public float variantBlend=.06f;
        [SurfaceSetting("Basalt Crown textures","Detail contrast","Fine albedo variation around calibrated family brightness.","ratio",0,1,.05f)] public float detailContrast=.8f;
        [SurfaceSetting("Basalt Crown textures","Micro relief","Maximum finite normal perturbation; does not displace terrain.","ratio",0,.4f,.025f)] public float normalSlope=.075f;
        [SurfaceSetting("Basalt Crown textures","Shadow receiver offset","Small normal offset that prevents self-shadow stripes on flat ground.","m",0,.1f,.005f)] public float receiverBias=.025f;
        [SurfaceSetting("Basalt Crown paint","Road feather","World-space feather of pale authored roads.","m",.1f,2,.1f)] public float roadFeather=.4f;
        [SurfaceSetting("Basalt Crown paint","Natural feather","World-space feather of scree, mineral and stone patches.","m",.1f,3,.1f)] public float naturalFeather=1;
        [SurfaceSetting("Basalt Crown palette","Warm hue","Rust and sand hue; local to this map.","turn",0,.15f,.005f)] public float warmHue=.055f;
        [SurfaceSetting("Basalt Crown palette","Warm saturation","Muted warm mineral saturation; textures retain their own colors.","ratio",0,.6f,.025f)] public float warmSaturation=.28f;
        [SurfaceSetting("Basalt Crown palette","Dark stone value","Dark volcanic ground brightness.","ratio",.15f,.5f,.01f)] public float darkValue=.29f;
        [SurfaceSetting("Basalt Crown palette","Stone value","Middle stone/ring brightness.","ratio",.3f,.7f,.01f)] public float stoneValue=.48f;
        [SurfaceSetting("Basalt Crown palette","Road value","Pale mineral road brightness.","ratio",.5f,.85f,.01f)] public float lightValue=.69f;
        [SurfaceSetting("Basalt Crown palette","Scree value","Crushed mineral scree brightness.","ratio",.35f,.75f,.01f)] public float screeValue=.56f;
        [SurfaceSetting("Basalt Crown palette","Cooled basalt value","Central cooled basalt brightness.","ratio",.12f,.5f,.01f)] public float cooledValue=.25f;
        [SurfaceSetting("Basalt Crown palette","Mineral value","Restrained vein and ridge mineral brightness.","ratio",.3f,.75f,.01f)] public float mineralValue=.54f;
        [SurfaceSetting("Basalt Crown palette","Basalt sand value","Warm basalt sand brightness.","ratio",.4f,.8f,.01f)] public float sandValue=.62f;
        [SurfaceSetting("Basalt Crown roads","Bypass width","Visible stone paving width within the existing outer bypass; no navigation change.","m",3,8,.25f)] public float bypassWidth=6;
        [SurfaceSetting("Basalt Crown roads","Slab length","Length of dressed stone slabs along each existing route.","m",1,5,.25f)] public float slabLength=3.5f;
        [SurfaceSetting("Basalt Crown roads","Slab width","Width of longitudinal stone courses.","m",1,4,.25f)] public float slabWidth=2.5f;
        [SurfaceSetting("Basalt Crown roads","Joint width","Dark recessed joint appearance without physical displacement.","m",.015f,.15f,.005f)] public float jointWidth=.065f;
        [SurfaceSetting("Basalt Crown roads","Joint contrast","Visibility of mortar and transverse ramp joints.","ratio",0,.8f,.025f)] public float jointContrast=.55f;
        [SurfaceSetting("Basalt Crown roads","Flush edging width","Width of intermittent flat kerb stones; never adds a collider.","m",.1f,.5f,.025f)] public float edgingWidth=.25f;
        [SurfaceSetting("Basalt Crown roads","Paving grain","Fine stone texture contrast inside the dressed slabs.","ratio",0,1,.025f)] public float pavingGrain=.27f;
        [SurfaceSetting("Basalt Crown roads","Dust amount","Irregular dust across the paved edge and approaches.","ratio",0,1,.05f)] public float roadDust=.25f;
        [SurfaceSetting("Basalt Crown geology","Ridge erosion","Inward-only fracture of existing solid ridges; preserves gameplay bounds.","m",0,.8f,.05f)] public float ridgeErosion=.55f;
        [SurfaceSetting("Basalt Crown geology","Strata spacing","Vertical spacing of restrained mineral layers on cliff faces.","m",.15f,1,.05f)] public float strataSpacing=.4f;
        [SurfaceSetting("Basalt Crown geology","Strata contrast","Strength of geological layers; horizontal terrain is unaffected.","ratio",0,.5f,.025f)] public float strataContrast=.2f;
        [SurfaceSetting("Basalt Crown details","Kerb height","Low segmented edging height above terrain; no colliders.","m",.04f,.3f,.02f)] public float kerbHeight=.18f;
        [SurfaceSetting("Basalt Crown details","Kerb length","Length of individual dressed edge stones.","m",.6f,3,.1f)] public float kerbLength=1.8f;
        [SurfaceSetting("Basalt Crown details","Kerb gap","Open spacing between dressed edge stones.","m",.1f,1,.05f)] public float kerbGap=.3f;
        [SurfaceSetting("Basalt Crown details","Kerb width","Width of edge stones outside paved road surfaces.","m",.15f,.7f,.05f)] public float kerbWidth=.4f;
        [SurfaceSetting("Basalt Crown details","Rubble size","Maximum full width of traversable shoulder rubble.","m",.2f,1,.05f)] public float rubbleSize=.7f;
        [SurfaceSetting("Basalt Crown details","Rubble height","Maximum small rubble height, independent of collision.","m",.05f,.4f,.025f)] public float rubbleHeight=.25f;
        [SurfaceSetting("Basalt Crown details","Rubble shoulder band","Distance from existing rock feet eligible for small rubble.","m",1,5,.25f)] public float rubbleBand=2.5f;
        [SurfaceSetting("Basalt Crown details","Rubble density","Fraction of deterministic eligible shoulder samples used.","ratio",0,1,.05f)] public float rubbleDensity=.75f;
        [SurfaceSetting("Basalt Crown details","Detail clearance","Extra empty space beyond full construction and unit envelopes.","m",.2f,2,.1f)] public float detailClearance=.4f;
        [SurfaceSetting("Basalt Crown mines","Apron radius","Compact octagonal stone and mineral zone around all mines.","m",3.5f,6,.25f)] public float mineApronRadius=4.75f;
        [SurfaceSetting("Basalt Crown mines","Apron feather","Dust transition from mine apron to surrounding terrain.","m",.25f,2,.1f)] public float mineApronFeather=.85f;
        [SurfaceSetting("Basalt Crown mines","Apron value","Brightness of local quarry stone; does not indicate ownership.","ratio",.3f,.7f,.025f)] public float mineApronValue=.52f;
        [SurfaceSetting("Basalt Crown roads","Paving saturation","Saturation multiplier of dressed limestone; natural basalt stays warm.","ratio",.2f,1,.05f)] public float pavingSaturation=.45f;
        [SurfaceSetting("Basalt Crown sites","Pad margin","Stone margin beyond the actual headquarters, outpost and slot footprints.","m",.5f,2,.25f)] public float siteApronMargin=1.25f;
        [SurfaceSetting("Basalt Crown sites","Link width","Flat stone paths connecting slot foundations to the main pad.","m",1,3,.25f)] public float siteLinkWidth=2;
        [SurfaceSetting("Basalt Crown sites","Pad feather","Dust transition around compound site paving.","m",.15f,1,.05f)] public float siteApronFeather=.4f;
        [SurfaceSetting("Basalt Crown sites","Pad value","Brightness of neutral construction pads, independent of ownership.","ratio",.35f,.7f,.025f)] public float siteApronValue=.56f;
        public IEnumerable<SurfaceParameter> Parameters(){foreach(var field in GetType().GetFields()){var a=field.GetCustomAttribute<SurfaceSettingAttribute>();if(a==null)continue;yield return new SurfaceParameter("visual.basaltCrown."+field.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)field.GetValue(this),v=>field.SetValue(this,v));}}
        public void Validate(){if(id!="basalt-crown-warm-stone-v1"||revision<1)throw new ArgumentException("Invalid Basalt Crown art identity");foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException("Invalid Basalt Crown art: "+p.Path);}
        public static BasaltCrownArtProfile Load(){var p=JsonUtility.FromJson<BasaltCrownArtProfile>(Resources.Load<TextAsset>("BasaltCrownArtProfile").text);p.Validate();return p;}
        // Stable authored material IDs, not procedural selectors or simulation state.
        public static readonly string[] Roles={"mountain.base-dark","mountain.base-mid","mountain.base-light","mountain.transition-scree","cooled-basalt","mountain.detail-mineral-veins","archipelago-coastal.transition-basalt-sand"};
        public static int Role(string id){int i=Array.IndexOf(Roles,id);if(i<0)throw new ArgumentException("Unknown Basalt Crown paint: "+id);return i;}
        public Color Palette(int role){float[] values={darkValue,stoneValue,lightValue,screeValue,cooledValue,mineralValue,sandValue};return Color.HSVToRGB(warmHue,warmSaturation*(role==2?pavingSaturation:1),values[role]);}
        public Material Material(Texture fog,Vector4 bounds,Color fogColor){
            Validate();var shader=Resources.Load<Shader>("BasaltCrownSurface");if(!shader||!shader.isSupported)throw new InvalidOperationException("Missing/unsupported Basalt Crown shader");
            var m=new Material(shader){name=id+" @"+revision};
            // Actual linear source luminance measurements in BasaltCrownArt/provenance.json.
            EnvironmentArt.BindFamily(m,"Dark","BasaltCrownArt/Dark",new Vector3(.037111337f,.046513473f,.044236980f));
            EnvironmentArt.BindFamily(m,"Stone","BasaltCrownArt/Stone",new Vector3(.112148526f,.110026358f,.120162146f));
            EnvironmentArt.BindFamily(m,"Sand","BasaltCrownArt/Sand",new Vector3(.450260818f,.420652896f,.432685528f));
            m.SetVector("_DetailSettings",new Vector4(detailTile,variantScale,variantBlend,detailContrast));m.SetFloat("_NormalSlope",normalSlope);m.SetFloat("_ReceiverBias",receiverBias);
            var colors=new Vector4[Roles.Length];for(int i=0;i<colors.Length;i++){var c=Palette(i).linear;colors[i]=new Vector4(c.r,c.g,c.b,1);}m.SetVectorArray("_RoleTints",colors);
            m.SetVector("_RoadLayout",new Vector4(BasaltCrownRoads.RingX,BasaltCrownRoads.RingZ,BasaltCrownRoads.CausewayHalfWidth,bypassWidth*.5f));
            m.SetVector("_RoadSlabs",new Vector4(slabLength,slabWidth,jointWidth,jointContrast));
            m.SetVector("_RoadFinish",new Vector4(edgingWidth,roadDust,roadFeather,pavingGrain));
            m.SetVector("_Geology",new Vector4(strataSpacing,strataContrast,0,0));
            m.SetVector("_SiteFinish",new Vector4(siteLinkWidth,siteApronFeather,0,0));
            var siteTint=Color.HSVToRGB(warmHue,warmSaturation*pavingSaturation,siteApronValue).linear;m.SetVector("_SiteTint",new Vector4(siteTint.r,siteTint.g,siteTint.b,1));
            m.SetVector("_MineFinish",new Vector4(mineApronRadius,mineApronFeather,0,0));
            var mineTint=Color.HSVToRGB(warmHue,warmSaturation*pavingSaturation,mineApronValue).linear;m.SetVector("_MineTint",new Vector4(mineTint.r,mineTint.g,mineTint.b,1));
            m.SetTexture("_FogMask",fog);m.SetVector("_FogBounds",bounds);m.SetColor("_FogColor",fogColor);return m;
        }
    }
}
