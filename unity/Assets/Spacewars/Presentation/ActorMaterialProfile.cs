using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class ActorMaterialProfile
    {
        public string id="actor-industrial-finish-v1";
        public string variant="restrained";
        public int revision=1;
        [SurfaceSetting("Armor","Metallic","Metal response of owner-colored armor.","ratio",0,.6f,.01f)] public float armorMetallic=.12f;
        [SurfaceSetting("Armor","Smoothness","Broad highlights on painted armor.","ratio",0,.8f,.01f)] public float armorSmoothness=.28f;
        [SurfaceSetting("Metal","Metallic","Response of existing neutral and copper parts.","ratio",0,.8f,.01f)] public float metalMetallic=.4f;
        [SurfaceSetting("Metal","Smoothness","Highlight width on existing exposed metal.","ratio",0,.8f,.01f)] public float metalSmoothness=.42f;
        [SurfaceSetting("Hangar","Platform gray","Light-gray tone of the inner metal elevator; does not affect the hangar walls.","ratio",.3f,.9f,.01f)] public float hangarPlatformGray=.72f;
        [SurfaceSetting("Structure","Smoothness","Matte chassis and structural surfaces.","ratio",0,.5f,.01f)] public float structureSmoothness=.16f;
        [SurfaceSetting("Structure","Dark lift","Lift dark structural paint toward mid-gray to retain readable shaded faces.","ratio",0,.3f,.01f)] public float darkLift=.1f;
        [SurfaceSetting("Lights","Emission gain","Gain on existing system/hot/chassis emission; no added light objects.","ratio",0,3,.05f)] public float emissionGain=1.2f;
        [SurfaceSetting("Lights","Team emission","Owner color emission strength on existing team lamps.","ratio",0,8,.1f)] public float teamEmission=2.8f;
        [SurfaceSetting("Lights","Building lamp emission","Owner-colored exterior lamps on headquarters, refinery, factory and outpost; lower gain preserves hue.","ratio",0,2,.05f)] public float buildingLampEmission=.7f;
        [SurfaceSetting("Refinery","Corner light emission","Keeps corner lamps visibly owner-colored instead of clipping to white.","ratio",0,2,.05f)] public float refineryBeaconEmission=.65f;
        [SurfaceSetting("Bloom","Intensity","Soft glow from existing HDR emission.","ratio",0,1,.01f)] public float bloomIntensity=.16f;
        [SurfaceSetting("Bloom","Threshold","Brightness threshold before glow; preserve ground contrast.","linear",1,3,.05f)] public float bloomThreshold=1.2f;
        [SurfaceSetting("Bloom","Scatter","Glow spread around existing bright elements.","ratio",0,1,.01f)] public float bloomScatter=.45f;
        public IEnumerable<SurfaceParameter> Parameters()
        {
            foreach(var field in GetType().GetFields())
            {var a=field.GetCustomAttribute<SurfaceSettingAttribute>();if(a!=null)yield return new SurfaceParameter("visual.actorFinish."+field.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)field.GetValue(this),v=>field.SetValue(this,v));}
        }
        public void Validate()
        {
            if(id!="actor-industrial-finish-v1"||revision<1||(variant!="restrained"&&variant!="luminous"))throw new ArgumentException("Invalid actor material identity");
            foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException("Invalid actor finish: "+p.Path);
        }
        public static ActorMaterialProfile Load(string variant="restrained")
        {if(variant!="restrained"&&variant!="luminous")throw new ArgumentException("Unknown material variant");var asset=Resources.Load<TextAsset>(variant=="restrained"?"ActorMaterialProfile":"ActorMaterialLuminousProfile");if(!asset)throw new InvalidOperationException("Missing actor finish profile");var p=JsonUtility.FromJson<ActorMaterialProfile>(asset.text);NativeGraphicsSettings.Apply(p.Parameters());if(!NativeGraphicsSettings.BloomEnabled)p.bloomIntensity=0;p.Validate();return p;}
        public float TeamEmissionFor(string role)=>role switch
        {
            "refinery-beacon-team-emissive"=>refineryBeaconEmission,
            "headquarters-a-system-team-emissive" or "headquarters-a-team-emissive" or "refinery-body-team-emissive" or "factory-armored-lamp-team-emissive" or "outpost-comparison-team-emissive"=>buildingLampEmission,
            _=>teamEmission
        };
        public void Apply(Renderer renderer,int slot,Color? owner=null,bool preserveOwnerPaint=false)
        {
            var material=renderer.sharedMaterials[slot];var role=material.name;
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);
            if(role.EndsWith("team-primary")){block.SetFloat("_Metallic",armorMetallic);block.SetFloat("_Smoothness",armorSmoothness);}
            else if(role.Contains("neutral-metal")||role.EndsWith("copper-metal")||role.EndsWith("guidance-copper")){block.SetFloat("_Metallic",metalMetallic);block.SetFloat("_Smoothness",metalSmoothness);}
            else if(role.EndsWith("structure")||role.EndsWith("foundation-concrete"))
            {block.SetFloat("_Metallic",0);block.SetFloat("_Smoothness",structureSmoothness);if(material.HasProperty("_BaseColor")){var color=material.GetColor("_BaseColor");var lifted=Color.Lerp(color,Color.gray,darkLift);lifted.a=color.a;block.SetColor("_BaseColor",lifted);}}
            if(role=="hangar-platform-neutral-metal")block.SetColor("_BaseColor",new Color(hangarPlatformGray,hangarPlatformGray,hangarPlatformGray,1));
            if(role.EndsWith("emissive")&&material.HasProperty("_EmissionColor"))block.SetColor("_EmissionColor",material.GetColor("_EmissionColor")*emissionGain);
            if(owner.HasValue&&(role.EndsWith("team-primary")||role.EndsWith("team-emissive")))
            {if(!preserveOwnerPaint)block.SetColor("_BaseColor",owner.Value);if(role.EndsWith("team-emissive"))block.SetColor("_EmissionColor",owner.Value*TeamEmissionFor(role));}
            renderer.SetPropertyBlock(block,slot);
        }
    }
}
