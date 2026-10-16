using System;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class FoundrySurfaceProfile
    {
        public string id="foundry-warm-slag-basalt-v1";public int revision=10;
        [SurfaceSetting("Foundry routes","Climb value","Neutral exposed aggregate on existing flank climbs; no light or geometry change.","ratio",.3f,.55f,.01f)] public float climbValue=.46f;
        [SurfaceSetting("Foundry routes","Climb wear spacing","Distance between broad transverse wear bands on flank climbs.","m",2,8,.25f)] public float climbWearSpacing=4;
        [SurfaceSetting("Foundry routes","Climb wear contrast","Restrained transverse wear in the aggregate, without raised bars or emission.","ratio",0,.3f,.01f)] public float climbWearContrast=.16f;
        [SurfaceSetting("Foundry routes","Connection value","Worn slag leading from central crossbars into the actual open sides of outposts.","ratio",.3f,.5f,.01f)] public float connectionValue=.40f;
        [SurfaceSetting("Foundry routes","Connection width","Width of the surface wear accent; underlying road and clearance remain unchanged.","m",6,12,.5f)] public float connectionWidth=10;
        [SurfaceSetting("Foundry routes","Route edge fade","Soft transition of climb and connection accents into existing slag.","m",1,4,.25f)] public float routeEdgeFade=2;
        [SurfaceSetting("Foundry roads","Compacted slag value","Neutral road brightness, separated from warm ash without road markings.","ratio",.2f,.55f,.01f)] public float roadValue=.34f;
        [SurfaceSetting("Foundry roads","Compacted detail","Retained road grain and relief relative to surrounding ash.","ratio",.1f,1,.05f)] public float roadDetail=.8f;
        [SurfaceSetting("Foundry concrete","Entry wear length","Flush approach wear inside the real pad edge, only where an authored road meets it.","m",1,6,.25f)] public float padEntryLength=3;
        [SurfaceSetting("Foundry concrete","Entry wear","Restrained darkening of concrete at road entries; no geometry or slot change.","ratio",0,.3f,.01f)] public float padEntryWear=.12f;
        [SurfaceSetting("Foundry concrete","Pour joint width","Flush narrow construction joints between a few large pour bays; no repeating tile grid.","m",.02f,.15f,.01f)] public float padJointWidth=.07f;
        [SurfaceSetting("Foundry concrete","Pour joint contrast","Subordinate dark joints on home and outpost concrete; zero disables them.","ratio",0,.35f,.01f)] public float padJointContrast=.20f;
        [SurfaceSetting("Foundry concrete","Pour bay variation","Restrained value difference between large concrete pours; retains crisp source grain.","ratio",0,.12f,.01f)] public float padBayVariation=.05f;
        [SurfaceSetting("Foundry concrete","Edge ageing width","Irregular inward ageing from the actual pad boundary, softened at road entries.","m",.5f,4,.25f)] public float padAgeWidth=2.5f;
        [SurfaceSetting("Foundry concrete","Edge ageing strength","Neutral weathering beneath existing edge sand; no raised border or added obstruction.","ratio",0,.4f,.01f)] public float padAgeStrength=.22f;
        [SurfaceSetting("Foundry geology","Face contrast","Separates basalt caps from steep faces without changing lighting or support geometry.","ratio",0,.5f,.05f)] public float rockFaceContrast=.25f;
        [SurfaceSetting("Foundry geology","Mineral warmth","Retained warm mineral coloration; lower values keep basalt neutral against the warm ash.","ratio",0,1,.05f)] public float rockWarmth=.25f;
        [SurfaceSetting("Foundry lava","Fissure depth","Distance below the enclosing ridge rim; presentation only, no navigation change.","m",1,8,.25f)] public float lavaDepth=3.5f;
        [SurfaceSetting("Foundry lava","Molten glow","Emitted molten brightness and restrained lower-wall glow.","ratio",0,3,.1f)] public float lavaGlow=1.2f;
        [SurfaceSetting("Foundry lava","Crust scale","World size of cooled fragments over moving molten material.","m",.25f,5,.25f)] public float lavaCrustScale=1.5f;
        [SurfaceSetting("Foundry lava","Flow speed","Visual lava flow; independent of simulation.","m/s",0,1,.05f)] public float lavaFlowSpeed=.18f;
        [SurfaceSetting("Textures","Relief slope limit","Maximum micro-normal slope; prevents dark derivative spikes while retaining fine relief.","ratio",.05f,.8f,.05f)] public float reliefSlopeLimit=.35f;
        [SurfaceSetting("Foundry palette","Broad deposits","World scale of ash deposits and oxidation.","m",4,30,.5f)] public float depositScale=13;
        [SurfaceSetting("Foundry palette","Grain size","Restrained material grain in world meters.","m",.05f,1,.05f)] public float grainScale=.3f;
        [SurfaceSetting("Foundry palette","Deposit contrast","Variation shared by both halves.","ratio",0,.4f,.01f)] public float depositContrast=.18f;
        [SurfaceSetting("Foundry palette","Grain contrast","Fine surface texture contrast.","ratio",0,.3f,.01f)] public float grainContrast=.12f;
        [SurfaceSetting("Foundry palette","Road feather","Irregular soft edge of compacted slag.","m",.25f,4,.25f)] public float roadFeather=1.5f;
        [SurfaceSetting("Foundry palette","Cooled lava rim","Width of the dark cooled edge around molten channels.","m",.25f,3,.25f)] public float lavaCrustWidth=1.5f;
        [SurfaceSetting("Foundry geology","Crown scale","World extent of major uneven cliff silhouettes.","m",2,18,.5f)] public float crownScale=7;
        [SurfaceSetting("Foundry geology","Crown relief","Height variation within existing blocked rock volumes.","m",.1f,3,.1f)] public float crownRelief=1.5f;
        [SurfaceSetting("Foundry geology","Edge erosion","Inset irregular crowns; base footprint remains fully blocked.","m",.1f,1.5f,.05f)] public float edgeErosion=1.2f;
        [SurfaceSetting("Foundry geology","Strata spacing","Vertical separation of broad mineral layers.","m",.3f,3,.1f)] public float strataSpacing=1.2f;
        [SurfaceSetting("Foundry geology","Strata contrast","Mineral bands without emissive veins.","ratio",0,.6f,.05f)] public float strataContrast=.3f;
        [SurfaceSetting("Foundry geology","Basalt value","Readable grey basalt albedo.","ratio",.2f,.6f,.01f)] public float basaltValue=.31f;
        [SurfaceSetting("Foundry textures","Texture strength","Quality stored surface texture detail.","ratio",0,1,.05f)] public float textureStrength=.85f;
        [SurfaceSetting("Foundry textures","Earth tile","World scale of ash and slag texture.","m",2,20,.5f)] public float earthTile=9;
        [SurfaceSetting("Foundry textures","Rock tile","World scale of crisp oblique rock texture.","m",2,20,.5f)] public float rockTile=8;
        [SurfaceSetting("Foundry textures","Concrete tile","World scale of pad texture.","m",2,20,.5f)] public float concreteTile=6;
        [SurfaceSetting("Foundry textures","Variant patch scale","Extent of irregular sibling material regions.","m",4,50,1)] public float variantPatchScale=22;
        [SurfaceSetting("Foundry textures","Variant transition","Noise-space width of sibling blending; lower keeps single-image detail.","ratio",.02f,.3f,.01f)] public float variantBlendWidth=.08f;
        [SurfaceSetting("Foundry concrete","Detail contrast","Contrast around calibrated concrete mean; preserves average pad brightness.","ratio",0,3,.1f)] public float concreteDetailContrast=2;
        [SurfaceSetting("Foundry textures","Relief","Material normal variation; no collision change.","ratio",0,1,.05f)] public float relief=.3f;
        [SurfaceSetting("Foundry textures","Rock foot blend","Smooth natural deposit width around basalt.","m",.5f,6,.25f)] public float rockBlend=3;
        [SurfaceSetting("Foundry industry","Volume height","Simple grey box masses in already blocked corners.","m",1,8,.25f)] public float industrialHeight=4;
        [SurfaceSetting("Foundry geology","Organic shoulder width","Inward sculpting of large crowns; complete blocked foot remains visible.","m",1,10,.25f)] public float shoulderWidth=6;
        [SurfaceSetting("Foundry geology","Basalt foot height","Visible continuous blocked foot above local terrain beneath organic shoulders.","m",.5f,3,.25f)] public float footHeight=1.5f;
        [SurfaceSetting("Foundry palette","Support light smoothing","Reduces long planar lighting seams across continuous shallow ramps; geometry remains unchanged.","ratio",0,1,.05f)] public float supportLightSmoothing=.85f;
        [SurfaceSetting("Foundry roads","Road meander","Smooth visual road displacement, independent of navigation.","m",0,5,.25f)] public float roadMeander=3;
        [SurfaceSetting("Foundry roads","Meander scale","Length of broad bends and changing road shoulders.","m",8,50,1)] public float roadBendScale=22;
        [SurfaceSetting("Foundry textures","Texture variation","Strength of broad deposit variation, independent of crisp sibling albedo detail.","ratio",0,1,.05f)] public float textureVariation=.8f;
        [SurfaceSetting("Foundry industry","Home hex extent","Circumscribed home pad extent relative to full square envelope.","ratio",1.5f,1.8f,.05f)] public float homeHexExtent=1.5f;
        [SurfaceSetting("Foundry concrete","Concrete value","Neutral exposed concrete brightness.","ratio",.3f,.7f,.01f)] public float concreteValue=.5f;
        [SurfaceSetting("Foundry concrete","Sand edge width","Width of irregular sand deposits over pad edges.","m",.5f,4,.25f)] public float padSandWidth=2.25f;
        [SurfaceSetting("Foundry concrete","Sand deposit scale","World size of uneven edge deposits.","m",1,8,.5f)] public float padSandScale=3;
        [SurfaceSetting("Foundry concrete","Sand value","Warm sand brightness at concrete edges.","ratio",.35f,.7f,.01f)] public float padSandValue=.55f;
        [SurfaceSetting("Foundry concrete","Central foundation radius","Distinct dark foundation beneath headquarters and outposts.","m",2,5,.25f)] public float padCenterRadius=3.5f;
        [SurfaceSetting("Foundry concrete","Central foundation value","Brightness of the separate dark central concrete texture.","ratio",.15f,.4f,.01f)] public float padCenterValue=.35f;
        [SurfaceSetting("Foundry concrete","Center transition","Soft texture transition at the central foundation edge.","m",.1f,1,.05f)] public float padCenterFeather=.25f;
        public System.Collections.Generic.IEnumerable<SurfaceParameter> Parameters()
        {
            foreach(var field in GetType().GetFields())
            {var a=field.GetCustomAttribute<SurfaceSettingAttribute>();if(a==null)continue;yield return new SurfaceParameter("visual.foundry."+field.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)field.GetValue(this),v=>field.SetValue(this,v));}
        }
        public static FoundrySurfaceProfile Load()=>JsonUtility.FromJson<FoundrySurfaceProfile>(Resources.Load<TextAsset>("FoundrySurfaceProfile").text);
        public void Validate()
        {
            if(id!="foundry-warm-slag-basalt-v1"||revision<1)throw new ArgumentException("Foundry surface identity");
            foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException("Invalid foundry surface: "+p.Path);
        }
        public void Apply(Material material)
        {
            Validate();
            EnvironmentArt.Apply(material,EnvironmentArtProfile.Load());
            EnvironmentArt.BindFamily(material,"Rock","FoundryArt/Basalt",new Vector3(.09498128f,.09463170f,.09153116f));
            EnvironmentArt.BindFamily(material,"Earth","FoundryArt/SlagAsh",new Vector3(.07655369f,.06494773f,.06200674f));
            material.SetVector("_TextureVariants",new Vector4(variantPatchScale,variantBlendWidth,concreteDetailContrast,0));
            material.SetVector("_ArtTiles",new Vector4(earthTile,rockTile,concreteTile,4));
            material.SetFloat("_ArtReliefLimit",reliefSlopeLimit);
            material.SetVector("_ArtDetail",new Vector4(textureStrength,relief,0,1));
            material.SetVector("_Geology",new Vector4(depositScale,0,strataSpacing,strataContrast));
            material.SetColor("_BasaltTint",new Color(basaltValue*.92f,basaltValue*.95f,basaltValue));
            material.SetVector("_FoundryVariation",new Vector4(textureVariation,depositScale,supportLightSmoothing,0));
            material.SetColor("_RoadTint",new Color(roadValue,roadValue,roadValue));
            material.SetVector("_FoundryReadability",new Vector4(rockFaceContrast,roadDetail,padEntryLength,padEntryWear));
            material.SetFloat("_FoundryBasaltWarmth",rockWarmth);
            material.SetColor("_FoundryClimbTint",new Color(climbValue,climbValue,climbValue));
            material.SetColor("_FoundryConnectionTint",new Color(connectionValue,connectionValue,connectionValue));
            material.SetVector("_FoundryRouteFinish",new Vector4(climbWearSpacing,climbWearContrast,0,0));
            material.SetVector("_FoundryPadPours",new Vector4(padJointWidth,padJointContrast,padBayVariation,0));
            material.SetVector("_FoundryPadAge",new Vector4(padAgeWidth,padAgeStrength,0,0));
            material.SetVector("_FoundryPadFinish",new Vector4(padSandWidth,padSandScale,padCenterRadius,padCenterFeather));
            material.SetColor("_FoundrySandTint",new Color(padSandValue,padSandValue*.86f,padSandValue*.66f));
            // The scalar central albedo needs the same sRGB conversion as SetColor tints.
            material.SetFloat("_FoundryCenterValue",Mathf.GammaToLinearSpace(padCenterValue));
            material.SetVector("_FoundryLavaFinish",new Vector4(lavaDepth,lavaGlow,lavaCrustScale,lavaFlowSpeed));
            material.SetColor("_FoundryLavaTint",PlayableWorld.FoundryLava);
            material.SetVector("_FoundryGrain",new Vector4(depositScale,grainScale,depositContrast,grainContrast));
        }
    }
}
