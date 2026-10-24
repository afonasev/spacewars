using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class CrimsonGroveArtProfile
    {
        public string id="crimson-grove-art-v1";public int revision=1;
        [SurfaceSetting("Ground","Ground value","Dark lower-basin soil under units.","ratio",.2f,.7f,.025f)] public float groundValue=.46f;
        [SurfaceSetting("Rock","Cool stone value","Brightness of exposed cool pale rock.","ratio",.4f,.9f,.025f)] public float rockValue=.72f;
        [SurfaceSetting("Grove","Crystal hue","Crimson mineral hue.","turn",0,1,.01f)] public float leafHue=.97f;
        [SurfaceSetting("Grove","Crystal saturation","Mineral color saturation.","ratio",.2f,.9f,.025f)] public float leafSaturation=.65f;
        [SurfaceSetting("Grove","Crystal value","Mineral brightness, subordinate to units.","ratio",.2f,.7f,.025f)] public float leafValue=.43f;
        [SurfaceSetting("Grove","Cluster spacing","Deterministic scatter within already blocked rock caps.","m",3,9,.5f)] public float treeSpacing=4.5f;
        [SurfaceSetting("Grove","Cluster radius","Maximum crystal cluster radius; reserved inside blocked cap.","m",1,3,.1f)] public float crownRadius=2.2f;
        [SurfaceSetting("Grove","Crystal height","Dominant crystalline spire above the rock cap.","m",2,6,.25f)] public float treeHeight=3.5f;
        [SurfaceSetting("Rock","Erosion","Inward cosmetic bevel, preserving the physical foot.","m",.2f,2,.1f)] public float erosion=1.2f;
        [SurfaceSetting("Textures","Tile size","World-space scale of stored material detail.","m",2,12,.5f)] public float textureTile=7;
        [SurfaceSetting("Textures","Patch size","Extent of irregular sibling texture regions.","m",10,40,1)] public float patchSize=23;
        [SurfaceSetting("Textures","Detail contrast","Fine detail contrast around the material mean.","ratio",0,.8f,.05f)] public float detailContrast=.35f;
        [SurfaceSetting("Boundary","Visible rim width","Decorative width beyond the unchanged physical inner wall.","m",4f,14f,0.5f)] public float outerRimWidth=8f;
        [SurfaceSetting("Ground","Soil hue","Lower-basin soil hue without changing lighting.","turn",0f,1f,0.01f)] public float groundHue=0.105f;
        [SurfaceSetting("Ground","Soil saturation","Muted soil color; units retain contrast.","ratio",0f,0.6f,0.01f)] public float groundSaturation=0.32f;
        [SurfaceSetting("Rock","Rock hue","Cool mineral tint.","turn",0f,1f,0.01f)] public float rockHue=0.105f;
        [SurfaceSetting("Rock","Rock saturation","Muted pale stone saturation.","ratio",0f,0.5f,0.01f)] public float rockSaturation=0.18f;
        [SurfaceSetting("Ground","Deposit scale","Organic large clay and compacted soil regions, independent of crisp detail.","m",8f,40f,1f)] public float macroScale=18f;
        [SurfaceSetting("Ground","Deposit contrast","Large natural color variation.","ratio",0f,0.5f,0.01f)] public float macroContrast=0.22f;
        [SurfaceSetting("Ground","Worn route width","Flat surface wear along actual approaches; no navigation effect.","m",2f,7f,0.25f)] public float roadWidth=4f;
        [SurfaceSetting("Ground","Packed soil value","Restrained darker vehicle approaches.","ratio",0.2f,0.65f,0.01f)] public float roadValue=0.37f;
        [SurfaceSetting("Ground","Cliff foot dust","Soft mineral deposits around the authoritative rock feet.","m",0.5f,6f,0.25f)] public float footDustReach=3f;
        [SurfaceSetting("Pads","Apron margin","Full flat apron beyond each building and construction slot.","m",0.2f,1.5f,0.1f)] public float padMargin=0.7f;
        [SurfaceSetting("Pads","Concrete value","Readability of base and fort concrete against soil.","ratio",0.35f,0.85f,0.01f)] public float padValue=0.63f;
        [SurfaceSetting("Pads","Concrete pour size","Sparse functional construction seams, not small paving tiles.","m",3f,9f,0.5f)] public float padJointSize=5f;
        [SurfaceSetting("Pads","Concrete wear","Subordinate scuffing and mineral dust.","ratio",0f,0.5f,0.01f)] public float padWear=0.22f;
        [SurfaceSetting("Rock","Layer height","Broad irregular limestone bedding height.","m",0.5f,3f,0.1f)] public float rockStrata=1.2f;
        [SurfaceSetting("Rock","Fracture contrast","Restrained mineral cracks on exposed rock.","ratio",0f,0.4f,0.01f)] public float rockFractures=0.2f;
        [SurfaceSetting("Rock","Fracture scale","Size of broken mineral faces.","m",2f,7f,0.25f)] public float rockFractureScale=3.5f;
        [SurfaceSetting("Textures","Micro normal limit","Finite bounded relief on ground, concrete and rock.","ratio",0f,0.15f,0.005f)] public float normalSlope=0.075f;
        [SurfaceSetting("Ground","Upper terrace value","Pale cool upper level, distinct from the dark lower basin.","ratio",.4f,.8f,.01f)] public float terraceValue=.62f;
        [SurfaceSetting("Ground","Upper terrace hue","Cool mineral soil on the upper level.","turn",0,1,.01f)] public float terraceHue=.57f;
        [SurfaceSetting("Ground","Upper terrace saturation","Desaturated blue grey mineral dust.","ratio",0,.4f,.01f)] public float terraceSaturation=.12f;
        [SurfaceSetting("Roads","Buried fraction","Soil deposits interrupt the old inter-base road surface.","ratio",.15f,.85f,.01f)] public float roadBurial=.48f;
        [SurfaceSetting("Roads","Deposit length","Extent of soil sheets over old roads.","m",1,12,.25f)] public float roadDepositScale=4.5f;
        [SurfaceSetting("Rock","Bench depth","Irregular inward cuts in the cliff wall; physical foot preserved.","m",.2f,2,.1f)] public float benchDepth=1.1f;
        [SurfaceSetting("Rock","Bench height","Average height of broken geological ledges.","m",.4f,2,.1f)] public float benchHeight=.85f;
        [SurfaceSetting("Textures","Pebble size","Small angular gravel and mineral inclusions.","m",.1f,.8f,.05f)] public float pebbleSize=.35f;
        [SurfaceSetting("Textures","Gravel coverage","Ground gravel density independent of fine albedo.","ratio",0,.8f,.05f)] public float gravelCoverage=.4f;
        [SurfaceSetting("Grove","Crystal shards","Faceted shards per cluster including the dominant spire.","count",3,12,1)] public float crystalShards=9;
        [SurfaceSetting("Grove","Crystal lean","Maximum angle of secondary crystalline growth.","deg",0,35,1)] public float crystalLean=23;
        [SurfaceSetting("Rock","Rock fragment size","Angular rubble within blocked rock caps, without colliders.","m",.2f,1.2f,.05f)] public float rockChipSize=.7f;
        [SurfaceSetting("Rock","Rock fragment spacing","Irregular density of broken stone on existing blocked caps.","m",2,6,.2f)] public float rockChipSpacing=3.2f;
        [SurfaceSetting("Ground","Exposed bedrock coverage","Broad coherent stone fields beneath dust; independent of texture detail.","ratio",0,.8f,.05f)] public float bedrockCoverage=.5f;
        [SurfaceSetting("Ground","Mineral sheet contrast","Contrast of large pale dust sheets on upper terraces.","ratio",0,.6f,.05f)] public float mineralSheetContrast=.3f;
        [SurfaceSetting("Ramps","Side face recess","Cosmetic cuts inside support volume; top and foot contacts unchanged.","m",.05f,.8f,.05f)] public float rampRecess=.45f;
        [SurfaceSetting("Ramps","Side facet width","Broad irregular facets along the existing support boundary.","m",1,5,.25f)] public float rampFacetWidth=2.5f;
        [SurfaceSetting("Pads","Flush edge band","Dark perimeter band within the complete construction apron.","m",.2f,1.2f,.05f)] public float padBorder=.55f;
        [SurfaceSetting("Pads","Panel seam","Recessed seam width between large prefab panels.","m",.03f,.2f,.01f)] public float padSeam=.09f;
        [SurfaceSetting("Pads","Fastener radius","Small recessed fastening rings in panel corners.","m",.04f,.2f,.01f)] public float padFastener=.12f;
        public Color Terrace=>Color.HSVToRGB(terraceHue,terraceSaturation,terraceValue);
        public IEnumerable<SurfaceParameter> Parameters(){foreach(var f in GetType().GetFields()){var a=f.GetCustomAttribute<SurfaceSettingAttribute>();if(a!=null)yield return new SurfaceParameter("visual.crimsonGrove."+f.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)f.GetValue(this),v=>f.SetValue(this,v));}}
        public void Validate(){if(id!="crimson-grove-art-v1"||revision<1)throw new ArgumentException("Invalid Crimson Grove art");foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException(p.Path);}
        public static CrimsonGroveArtProfile Load(){var p=JsonUtility.FromJson<CrimsonGroveArtProfile>(Resources.Load<TextAsset>("CrimsonGroveArtProfile").text);p.Validate();return p;}
        public Color Ground=>Color.HSVToRGB(groundHue,groundSaturation,groundValue);
        public Color Rock=>Color.HSVToRGB(rockHue,rockSaturation,rockValue);
        public Color Leaves=>Color.HSVToRGB(leafHue,leafSaturation,leafValue);
    }
}
