using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class BasaltCrownArtProfile
    {
        public string id="basalt-crown-warm-stone-v1"; public int revision=1;
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
        public IEnumerable<SurfaceParameter> Parameters(){foreach(var field in GetType().GetFields()){var a=field.GetCustomAttribute<SurfaceSettingAttribute>();if(a==null)continue;yield return new SurfaceParameter("visual.basaltCrown."+field.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)field.GetValue(this),v=>field.SetValue(this,v));}}
        public void Validate(){if(id!="basalt-crown-warm-stone-v1"||revision<1)throw new ArgumentException("Invalid Basalt Crown art identity");foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException("Invalid Basalt Crown art: "+p.Path);}
        public static BasaltCrownArtProfile Load(){var p=JsonUtility.FromJson<BasaltCrownArtProfile>(Resources.Load<TextAsset>("BasaltCrownArtProfile").text);p.Validate();return p;}
        // Stable authored material IDs, not procedural selectors or simulation state.
        public static readonly string[] Roles={"mountain.base-dark","mountain.base-mid","mountain.base-light","mountain.transition-scree","cooled-basalt","mountain.detail-mineral-veins","archipelago-coastal.transition-basalt-sand"};
        public static int Role(string id){int i=Array.IndexOf(Roles,id);if(i<0)throw new ArgumentException("Unknown Basalt Crown paint: "+id);return i;}
        public Color Palette(int role){float[] values={darkValue,stoneValue,lightValue,screeValue,cooledValue,mineralValue,sandValue};return Color.HSVToRGB(warmHue,warmSaturation,values[role]);}
        public Material Material(Texture fog,Vector4 bounds,Color fogColor){
            Validate();var shader=Resources.Load<Shader>("BasaltCrownSurface");if(!shader||!shader.isSupported)throw new InvalidOperationException("Missing/unsupported Basalt Crown shader");
            var m=new Material(shader){name=id+" @"+revision};
            // Actual linear source luminance measurements in BasaltCrownArt/provenance.json.
            EnvironmentArt.BindFamily(m,"Dark","BasaltCrownArt/Dark",new Vector3(.037111337f,.046513473f,.044236980f));
            EnvironmentArt.BindFamily(m,"Stone","BasaltCrownArt/Stone",new Vector3(.112148526f,.110026358f,.120162146f));
            EnvironmentArt.BindFamily(m,"Sand","BasaltCrownArt/Sand",new Vector3(.450260818f,.420652896f,.432685528f));
            m.SetVector("_DetailSettings",new Vector4(detailTile,variantScale,variantBlend,detailContrast));m.SetFloat("_NormalSlope",normalSlope);m.SetFloat("_ReceiverBias",receiverBias);
            var colors=new Vector4[Roles.Length];for(int i=0;i<colors.Length;i++){var c=Palette(i).linear;colors[i]=new Vector4(c.r,c.g,c.b,1);}m.SetVectorArray("_RoleTints",colors);
            m.SetTexture("_FogMask",fog);m.SetVector("_FogBounds",bounds);m.SetColor("_FogColor",fogColor);return m;
        }
    }
}
