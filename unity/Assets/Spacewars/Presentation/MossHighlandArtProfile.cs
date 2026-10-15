using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class MossHighlandArtProfile
    {
        public string id="moss-highland-wet-v1";public int revision=1;
        [SurfaceSetting("Highland textures","Detail tile","Fine albedo scale, independent of geology and paint.","m",4,20,.25f)] public float detailTile=9;
        [SurfaceSetting("Highland textures","Variant region","Irregular continuous sibling regions.","m",8,50,1)] public float variantScale=19;
        [SurfaceSetting("Highland textures","Variant blend","Only region boundaries blend sibling images.","ratio",.02f,.3f,.01f)] public float variantBlend=.06f;
        [SurfaceSetting("Highland textures","Detail contrast","Calibrated texture contrast.","ratio",0,1,.05f)] public float detailContrast=.85f;
        [SurfaceSetting("Highland ground","Natural feather","Soft natural role boundaries; metal stays sharp.","m",.1f,3,.1f)] public float naturalFeather=.9f;
        [SurfaceSetting("Highland ground","Damp soil patches","World scale of irregular moss/soil pockets.","m",6,30,.5f)] public float soilScale=13;
        [SurfaceSetting("Highland ground","Damp soil coverage","Amount of exposed damp earth in moss.","ratio",0,.65f,.025f)] public float soilCoverage=.3f;
        [SurfaceSetting("Highland ground","Moss hue","Restrained wet green hue.","turn",.17f,.35f,.005f)] public float mossHue=.235f;
        [SurfaceSetting("Highland ground","Moss saturation","Green moss saturation.","ratio",.1f,.8f,.025f)] public float mossSaturation=.42f;
        [SurfaceSetting("Highland ground","Moss value","Moss brightness under scene lighting.","ratio",.2f,.7f,.025f)] public float mossValue=.42f;
        [SurfaceSetting("Highland geology","Rock value","Cool grey rock brightness.","ratio",.2f,.7f,.025f)] public float rockValue=.47f;
        [SurfaceSetting("Highland geology","Ridge erosion","Inward-only authored rock fracture.","m",0,.8f,.05f)] public float ridgeErosion=.6f;
        [SurfaceSetting("Highland geology","Strata spacing","Cliff mineral layer spacing.","m",.2f,1,.05f)] public float strataSpacing=.5f;
        [SurfaceSetting("Highland geology","Strata contrast","Restrained cliff band contrast.","ratio",0,.4f,.025f)] public float strataContrast=.12f;
        [SurfaceSetting("Highland detail","Micro relief","Bounded finite shading normals, no displacement of traversable ground.","ratio",0,.3f,.025f)] public float normalSlope=.1f;
        [SurfaceSetting("Highland detail","Detail density","Sparse small rock clusters inside authored solid areas.","ratio",0,1,.05f)] public float detailDensity=.45f;
        [SurfaceSetting("Highland detail","Pebble size","Maximum half width of decorative rock pockets.","m",.1f,.6f,.025f)] public float pebbleSize=.35f;
        [SurfaceSetting("Highland detail","Shadow receiver offset","Normal offset against shadow striping.","m",0,.1f,.005f)] public float receiverBias=.025f;
        public IEnumerable<SurfaceParameter> Parameters(){foreach(var field in GetType().GetFields()){var a=field.GetCustomAttribute<SurfaceSettingAttribute>();if(a!=null)yield return new SurfaceParameter("visual.mossHighland."+field.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)field.GetValue(this),v=>field.SetValue(this,v));}}
        public void Validate(){if(id!="moss-highland-wet-v1"||revision<1)throw new ArgumentException("Invalid highland art identity");foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException("Invalid highland art: "+p.Path);}
        public static MossHighlandArtProfile Load(){var p=JsonUtility.FromJson<MossHighlandArtProfile>(Resources.Load<TextAsset>("MossHighlandArtProfile").text);p.Validate();return p;}
        // Source roles remain content identifiers; added soil/scree are presentation only.
        public static readonly string[] Roles={"mountain.base-dark","mountain.base-mid","metal-deck","scree","moss-ground","damp-soil","pebbles"};
        public static int Role(string id){int i=Array.IndexOf(Roles,id);if(i<0)throw new ArgumentException("Unknown highland material: "+id);return i;}
        public Color Palette(int role){switch(role){case 0:return new Color(rockValue*.8f,rockValue*.85f,rockValue*.83f);case 1:return new Color(rockValue,rockValue*1.04f,rockValue*.98f);case 2:return new Color(.40f,.44f,.45f);case 3:return new Color(.40f,.41f,.35f);case 4:return Color.HSVToRGB(mossHue,mossSaturation,mossValue);case 5:return Color.HSVToRGB(.10f,.27f,.31f);case 6:return new Color(.45f,.46f,.38f);default:throw new ArgumentOutOfRangeException(nameof(role));}}
        public Material Material(Texture fog,Vector4 bounds,Color fogColor){
            Validate();var shader=Resources.Load<Shader>("MossHighlandSurface");if(!shader||!shader.isSupported)throw new InvalidOperationException("Missing highland shader");var m=new Material(shader){name=id+" @"+revision};
            EnvironmentArt.BindFamily(m,"Dark","Environment/NaturalFrontier/Rock",new Vector3(.13677501f,.13289979f,.14225178f));
            EnvironmentArt.BindFamily(m,"Stone","MossHighlandArt/Moss",new Vector3(.048031445f,.039747567f,.047499432f));
            EnvironmentArt.BindFamily(m,"Sand","Environment/NaturalFrontier/Earth",new Vector3(.13535270f,.15587468f,.14469715f));
            m.SetVector("_DetailSettings",new Vector4(detailTile,variantScale,variantBlend,detailContrast));m.SetVector("_NaturalPattern",new Vector4(soilScale,soilCoverage,naturalFeather,0));m.SetVector("_Geology",new Vector4(strataSpacing,strataContrast,0,0));m.SetFloat("_NormalSlope",normalSlope);m.SetFloat("_ReceiverBias",receiverBias);
            var colors=new Vector4[7];for(int i=0;i<7;i++){var c=Palette(i).linear;colors[i]=new Vector4(c.r,c.g,c.b,1);}m.SetVectorArray("_RoleTints",colors);m.SetTexture("_FogMask",fog);m.SetVector("_FogBounds",bounds);m.SetColor("_FogColor",fogColor);return m;
        }
    }
}
