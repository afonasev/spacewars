using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class WhiteMeridianArtProfile
    {
        public string id="white-meridian-astra-art-v1";public int revision=17;
        [SurfaceSetting("Salt","Ivory value","Brightness of the salt crust.","ratio",.5f,1,.025f)] public float saltValue=.88f;
        [SurfaceSetting("Salt","Dust coverage","Amount of peach dust in broad continuous deposits.","ratio",0,1,.05f)] public float dustCoverage=.52f;
        [SurfaceSetting("Salt","Dust scale","Size of irregular dust deposits, independent of fine detail.","m",5,40,1)] public float dustScale=18;
        [SurfaceSetting("Detail","Texture scale","Fine albedo scale shared across mesh seams.","m",2,12,.25f)] public float detailTile=5;
        [SurfaceSetting("Detail","Variant scale","Irregular world-space sibling regions.","m",10,40,1)] public float variantScale=21;
        [SurfaceSetting("Detail","Variant blend","Narrow soft transitions between dominant siblings.","ratio",.02f,.2f,.01f)] public float variantBlend=.06f;
        [SurfaceSetting("Detail","Texture contrast","Retain crisp fine mineral texture without dark noise.","ratio",0,1,.05f)] public float detailContrast=.3f;
        [SurfaceSetting("Detail","Normal limit","Maximum finite micro-relief normal slope.","ratio",0,.2f,.005f)] public float normalSlope=.045f;
        [SurfaceSetting("Gypsum","Erosion","Inward retreat of physical ridge dressing.","m",.2f,3f,.1f)] public float erosion=2.2f;
        [SurfaceSetting("Gypsum","Stratum spacing","Horizontal gypsum layer thickness.","m",.3f,2,.1f)] public float strataSpacing=.8f;
        [SurfaceSetting("Gypsum","Stratum contrast","Subtle layer darkening on vertical walls.","ratio",0,.35f,.025f)] public float strataContrast=.16f;
        [SurfaceSetting("Resources","Copper radius","Surface pigment around neutral mines; no capture effect.","m",3,7,.25f)] public float copperRadius=4.5f;
        [SurfaceSetting("Wreck","Hull value","Salt-bleached ship panel brightness.","ratio",.3f,.9f,.025f)] public float hullValue=.66f;
        [SurfaceSetting("Foundations","Concrete value","Brightness of the shared HQ and outpost foundation.","ratio",.5f,.9f,.025f)] public float foundationValue=.75f;
        [SurfaceSetting("Foundations","Slot value","Lighter construction apron concrete on this map only.","ratio",.4f,.8f,.025f)] public float slotValue=.65f;
        [SurfaceSetting("Foundations","Outer margin","Visual margin beyond the site slots; does not change building or movement space.","m",.5f,2,.1f)] public float foundationMargin=1.2f;
        [SurfaceSetting("Foundations","Corner cut","Clipped corner length relative to the shorter side.","ratio",.1f,.3f,.025f)] public float foundationCorner=.225f;
        [SurfaceSetting("Foundations","Copper trim","Width of the flush perimeter strip.","m",.05f,.25f,.025f)] public float foundationTrim=.125f;
        [SurfaceSetting("Foundations","Edge wear","Subtle mineral scuffing on frequently crossed foundation edges.","ratio",0,.7f,.05f)] public float foundationWear=.4f;
        [SurfaceSetting("Foundations","Hairline cracks","Restrained, shallow-looking concrete stress marks; presentation only.","ratio",0,.6f,.05f)] public float foundationCracks=.3f;
        [SurfaceSetting("Landscape","Rock face inset","Inward retreat of the crown above the existing rock foot; creates broad slanted chipped faces without entering passages.","m",.2f,2,.05f)] public float rockFaceInset=1.2f;
        [SurfaceSetting("Landscape","Rock rim drop","Shallow eroded bevel below the continuous massif roof, inside the authored footprint.","m",.2f,1,.05f)] public float rockRimDrop=.65f;
        [SurfaceSetting("Landscape","Rock height ratio","Visible massif height above its base; lowers unit occlusion without changing physical obstacles.","ratio",.5f,1,.05f)] public float rockHeightRatio=.75f;
        [SurfaceSetting("Landscape","Rock shoulder","Width of eroded rock shoulders inside the existing obstacle.","m",1,4,.25f)] public float rockShoulder=3;
        [SurfaceSetting("Landscape","Rock fractures","Depth of shallow wind-cut fractures on the visible gypsum crown; no collider or blocker changes.","m",0,.8f,.05f)] public float rockFractureDepth=.1f;
        [SurfaceSetting("Landscape","Rock layering","Relief of broad sediment layers following each rock contour; cosmetic only.","m",0,.35f,.025f)] public float rockLayerRelief=.075f;
        [SurfaceSetting("Landscape","Rock layer spacing","Distance between broad visible gypsum strata.","m",.5f,2.5f,.1f)] public float rockLayerSpacing=1.4f;
        [SurfaceSetting("Landscape","Crown variation","Broad deterministic height variation between existing rock forms; no physical blocker changes.","m",0,1.6f,.05f)] public float rockCrownVariation=1.2f;
        [SurfaceSetting("Landscape","Ramp relief","Maximum visual relief on ramp interiors; zero on joins.","m",0,.08f,.01f)] public float rampRelief=.06f;
        [SurfaceSetting("Landscape","Soil variety","Amount of exposed compact soil among salt and sand.","ratio",0,1,.05f)] public float soilVariety=.7f;
        [SurfaceSetting("Landscape","Foundation sand","Amount of windblown sand at concrete edges.","ratio",0,1,.05f)] public float foundationSand=.55f;
        [SurfaceSetting("Tracks","Track corridor","Width of the worn vehicle corridor.","m",2,4,.25f)] public float roadWidth=3;
        [SurfaceSetting("Tracks","Wheel separation","Separation of the paired tire marks.","m",.8f,1.8f,.1f)] public float wheelSeparation=1.3f;
        [SurfaceSetting("Tracks","Sand burial","How often windblown sand interrupts road and tire marks.","ratio",0,1,.05f)] public float roadBurial=.55f;
        [SurfaceSetting("Tracks","Track contrast","Darkening of exposed wheel ruts.","ratio",.1f,.5f,.025f)] public float roadContrast=.3f;
        [SurfaceSetting("Landscape","Cliff erosion","Depth of smooth sidewall weathering; simulation geometry is unchanged.","m",0,1,.05f)] public float cliffErosion=.65f;
        [SurfaceSetting("Landscape","Mineral patches","Contrast of asymmetric salt and compact-soil deposits.","ratio",0,1,.05f)] public float mineralPatches=.75f;
        [SurfaceSetting("Landscape","Rim wander","Maximum irregular variation in the steep cosmetic collar width; no physical traversal.","m",0,.8f,.05f)] public float rimWander=.2f;
        [SurfaceSetting("Landscape","Cliff shoulder","Width of the steep cosmetic gypsum collar outside the physical edge.","m",0,1.2f,.05f)] public float cliffShoulder=.85f;
        [SurfaceSetting("Connections","Junction round","Radius of the shared terrain-envelope smoothing; affects only the narrow cosmetic edge and landing joins.","m",.08f,.35f,.01f)] public float connectionRound=.22f;
        [SurfaceSetting("Connections","Foot apron","Width of shallow windblown deposits beyond the steep cosmetic cliff collar; not physical traversal.","m",.1f,.6f,.05f)] public float footApron=.4f;
        [SurfaceSetting("Connections","Foot relief","Maximum shallow scree and sand relief at the cliff foot.","m",.02f,.2f,.01f)] public float footRelief=.12f;
        [SurfaceSetting("Connections","Ramp side inset","Target width of the filtered cosmetic shelf edge beside ramps; includes the filter radius, plus one mesh cell of sampling tolerance.","m",.16f,.5f,.01f)] public float rampEdgeInset=.5f;
        [SurfaceSetting("Selected style","Split spacing","Scale of broad crown facets and sparse visual mineral fractures; no movement effect.","m",4,16,1)] public float splitSpacing=8;
        [SurfaceSetting("Selected style","Split contrast","Darkening of sparse gypsum splits.","ratio",0,.4f,.025f)] public float splitContrast=.22f;
        [SurfaceSetting("Selected style","Wreck burial","Height of windblown pigment along the old hull.","m",.2f,2,.1f)] public float wreckBurial=1.1f;
        [SurfaceSetting("Selected style","Skid width","Half-width of the faded flat crash scar.","m",2,8,.5f)] public float skidWidth=6;
        [SurfaceSetting("Wreck fire","Scorch intensity","Restrained ground soot darkening around the existing wreck; no hull or fog changes.","ratio",0,.6f,.025f)] public float scorchIntensity=.6f;
        [SurfaceSetting("Wreck fire","Scorch reach","Distance of irregular burned sand beyond each section envelope.","m",0,6,.25f)] public float scorchReach=3.5f;
        [SurfaceSetting("Wreck fire","Scorch softness","Soft eroded transition width relative to the local fire patch.","ratio",.2f,.8f,.05f)] public float scorchSoftness=.55f;
        [SurfaceSetting("Map decor","Mineral density","Sparse gypsum and salt fragments inside existing rock boundaries.","ratio",0,1,.05f)] public float decorDensity=.65f;
        [SurfaceSetting("Map decor","Detail size","Size of low cosmetic fragments and expedition props at rock feet and in wreck pockets.","ratio",.6f,1.1f,.05f)] public float decorScale=1;
        public IEnumerable<SurfaceParameter> Parameters(){foreach(var f in GetType().GetFields()){var a=f.GetCustomAttribute<SurfaceSettingAttribute>();if(a!=null)yield return new SurfaceParameter("visual.whiteMeridian."+f.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)f.GetValue(this),v=>f.SetValue(this,v));}}
        [SurfaceSetting("Wreck","Hull height ratio","Flatten the aged hull into a low ship silhouette inside the existing blocker.","ratio",.4f,1,.05f)] public float hullHeightRatio=.6f;
        [SurfaceSetting("Wreck","End pocket","Length of impassable sand and scree inside the outer wreck footprint.","m",1,4,.25f)] public float wreckEndPocket=4f;
        [SurfaceSetting("Wreck","Cliff pocket retreat","Visual retreat of cliff tips into existing solids; sand and scree preserve the impassable boundary.","m",2,6,.5f)] public float cliffPocketRetreat=4;
        [SurfaceSetting("Wreck","Cockpit damage","Depth of authored broad cockpit dents; keeps the enclosed glazed silhouette.","m",.1f,.7f,.05f)] public float cockpitDamage=.4f;
        [SurfaceSetting("Wreck","Exhaust flare","Rear nozzle radius relative to its engine body.","ratio",1.1f,1.6f,.05f)] public float exhaustFlare=1.45f;
        [SurfaceSetting("Foundations","Outpost margin","Compact outpost border around all unchanged building slots.","m",.3f,1.2f,.1f)] public float outpostMargin=.5f;
        [SurfaceSetting("Tracks","Pit relief","Cosmetic worn-pit normal strength; never changes terrain heights.","ratio",0,.25f,.01f)] public float pitRelief=.12f;
        [SurfaceSetting("Foundations","Outpost core","Central slab extent relative to the slot envelope; building slots and their own construction aprons stay unchanged.","ratio",.65f,1,.025f)] public float outpostCoreRatio=.72f;
        public void Validate(){if(id!="white-meridian-astra-art-v1"||revision<1)throw new ArgumentException("Invalid White Meridian art identity");foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException(p.Path);}
        public static WhiteMeridianArtProfile Load(){var p=JsonUtility.FromJson<WhiteMeridianArtProfile>(Resources.Load<TextAsset>("WhiteMeridianArtProfile").text);p.Validate();return p;}
        public Color Salt=>new Color(saltValue,saltValue*.94f,saltValue*.82f);
        public Color Dust=>new Color(.71f,.49f,.35f);public Color Gypsum=>new Color(.86f,.79f,.65f);public Color Copper=>new Color(.47f,.25f,.13f);
        public Material Material(Texture fog,Vector4 bounds,Color fogColor){
            Validate();var shader=Resources.Load<Shader>("WhiteMeridianSurface");if(!shader||!shader.isSupported)throw new InvalidOperationException("White Meridian shader missing");
            var m=new Material(shader){name=id};
            EnvironmentArt.BindFamily(m,"Salt","Environment/NaturalFrontier/Concrete",new Vector3(.38497328f,.37142344f,.37949659f));
            EnvironmentArt.BindFamily(m,"Rock","Environment/NaturalFrontier/Rock",new Vector3(.13677501f,.13289979f,.14225178f));
            EnvironmentArt.BindFamily(m,"Soil","Environment/NaturalFrontier/Earth",new Vector3(.13535270f,.15587468f,.14469715f));
            m.SetVector("_Scorch",new Vector4(scorchIntensity,scorchReach,scorchSoftness,0));
            m.SetFloat("_PitRelief",pitRelief);
            m.SetVector("_SelectedStyle",new Vector4(splitSpacing,splitContrast,wreckBurial,skidWidth));
            m.SetVector("_Landscape",new Vector4(soilVariety,foundationSand,roadBurial,roadContrast));m.SetVector("_FoundationArt",new Vector4(foundationWear,foundationCracks,.72f,.69f));m.SetFloat("_MineralPatches",mineralPatches);
            m.SetTexture("_FogMask",fog);m.SetVector("_FogBounds",bounds);m.SetColor("_FogColor",fogColor);m.SetColor("_SaltTint",Salt);m.SetColor("_DustTint",Dust);m.SetColor("_RockTint",Gypsum);m.SetColor("_CopperTint",Copper);
            m.SetVector("_Detail",new Vector4(detailTile,variantScale,variantBlend,detailContrast));m.SetVector("_Geology",new Vector4(dustScale,dustCoverage,strataSpacing,strataContrast));m.SetFloat("_NormalSlope",normalSlope);m.SetFloat("_CopperRadius",copperRadius);return m;
        }
    }
}
