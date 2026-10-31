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
        public int revision=2;
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
        [SurfaceSetting("Wrecks","Металличность","Отклик покрытой сажей поверхности остовов; не влияет на живую технику.","ratio",0,1,.01f)] public float wreckMetallic=.03f;
        [SurfaceSetting("Wrecks","Гладкость","Блеск поверхности остовов.","ratio",0,1,.01f)] public float wreckSmoothness=.15f;
        [SurfaceSetting("Wrecks","Компенсация тёплого света","Убирает коричневый оттенок сажи под тёплым солнцем; белый и холодный свет сохраняются.","ratio",0,1,.01f)] public float wreckWarmthCompensation=.7f;
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
        public void ApplyWreck(Renderer renderer,Color sun)
        {
            // Only counter a warm key light. The epsilon prevents division by zero;
            // it is a numerical invariant, not an art control.
            var correction=new Color(Mathf.Min(1,sun.b/Mathf.Max(.0001f,sun.r)),Mathf.Min(1,sun.b/Mathf.Max(.0001f,sun.g)),1,1);
            var tint=Color.Lerp(Color.white,correction,wreckWarmthCompensation);
            var block=new MaterialPropertyBlock();
            for(int slot=0;slot<renderer.sharedMaterials.Length;slot++){
                renderer.GetPropertyBlock(block,slot);{RecordColorInput(renderer,slot,"_BaseColor",tint);block.SetColor("_BaseColor",tint);}{RecordFloatInput(renderer,slot,"_Metallic",wreckMetallic);block.SetFloat("_Metallic",wreckMetallic);}{RecordFloatInput(renderer,slot,"_Smoothness",wreckSmoothness);block.SetFloat("_Smoothness",wreckSmoothness);}renderer.SetPropertyBlock(block,slot);
            }
        }
        public float TeamEmissionFor(string role)=>role switch
        {
            "refinery-beacon-team-emissive"=>refineryBeaconEmission,
            "headquarters-a-system-team-emissive" or "headquarters-a-team-emissive" or "refinery-body-team-emissive" or "factory-armored-lamp-team-emissive" or "outpost-comparison-team-emissive"=>buildingLampEmission,
            _=>teamEmission
        };
        // QA-only observation of typed producer arguments, before Unity's color conversion.
#if UNITY_EDITOR
        public static Action<Renderer,int,string,Color> ColorInputObserver;
        public static Action<Renderer,int,string,float> FloatInputObserver;
#endif
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void RecordColorInput(Renderer renderer,int slot,string property,Color value)
        {
#if UNITY_EDITOR
            ColorInputObserver?.Invoke(renderer,slot,property,value);
#endif
        }
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void RecordFloatInput(Renderer renderer,int slot,string property,float value)
        {
#if UNITY_EDITOR
            FloatInputObserver?.Invoke(renderer,slot,property,value);
#endif
        }
        public void Apply(Renderer renderer,int slot,Color? owner=null,bool preserveOwnerPaint=false)
        {
            var material=renderer.sharedMaterials[slot];var role=material.name;
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);
            if(role.EndsWith("team-primary")){{RecordFloatInput(renderer,slot,"_Metallic",armorMetallic);block.SetFloat("_Metallic",armorMetallic);}{RecordFloatInput(renderer,slot,"_Smoothness",armorSmoothness);block.SetFloat("_Smoothness",armorSmoothness);}}
            else if(role.Contains("neutral-metal")||role.EndsWith("copper-metal")||role.EndsWith("guidance-copper")){{RecordFloatInput(renderer,slot,"_Metallic",metalMetallic);block.SetFloat("_Metallic",metalMetallic);}{RecordFloatInput(renderer,slot,"_Smoothness",metalSmoothness);block.SetFloat("_Smoothness",metalSmoothness);}}
            else if(role.EndsWith("structure")||role.EndsWith("foundation-concrete"))
            {{RecordFloatInput(renderer,slot,"_Metallic",0);block.SetFloat("_Metallic",0);}{RecordFloatInput(renderer,slot,"_Smoothness",structureSmoothness);block.SetFloat("_Smoothness",structureSmoothness);}if(material.HasProperty("_BaseColor")){var color=material.GetColor("_BaseColor");var lifted=Color.Lerp(color,Color.gray,darkLift);lifted.a=color.a;{RecordColorInput(renderer,slot,"_BaseColor",lifted);block.SetColor("_BaseColor",lifted);}}}
            if(role=="hangar-platform-neutral-metal"){RecordColorInput(renderer,slot,"_BaseColor",new Color(hangarPlatformGray,hangarPlatformGray,hangarPlatformGray,1));block.SetColor("_BaseColor",new Color(hangarPlatformGray,hangarPlatformGray,hangarPlatformGray,1));}
            if(role.EndsWith("emissive")&&material.HasProperty("_EmissionColor")){RecordColorInput(renderer,slot,"_EmissionColor",material.GetColor("_EmissionColor")*emissionGain);block.SetColor("_EmissionColor",material.GetColor("_EmissionColor")*emissionGain);}
            if(owner.HasValue&&(role.EndsWith("team-primary")||role.EndsWith("team-emissive")))
            {if(!preserveOwnerPaint){RecordColorInput(renderer,slot,"_BaseColor",owner.Value);block.SetColor("_BaseColor",owner.Value);}if(role.EndsWith("team-emissive")){RecordColorInput(renderer,slot,"_EmissionColor",owner.Value*TeamEmissionFor(role));block.SetColor("_EmissionColor",owner.Value*TeamEmissionFor(role));}}
            renderer.SetPropertyBlock(block,slot);
        }
    }
}
