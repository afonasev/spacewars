using System;
using System.Collections.Generic;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class WhiteMeridianLightingVariant
    {public string id,label;public IndustrialLightingProfile lighting;}
    [Serializable] public sealed class WhiteMeridianLightingVariants
    {
        public string id;public int revision;public WhiteMeridianLightingVariant[] variants;
        private static readonly string[] Ids={"soft-daylight-v1","amber-side-light-v1","cold-morning-v1","gold-blue-backlight-v1"};
        public static int Index(int seed){
            // Stable seed mixing for presentation only. Never draws from simulation
            // RNG or process-dependent string hashes. Four slots have equal weight.
            unchecked {uint value=(uint)seed;value^=value>>16;value*=0x7feb352d;value^=value>>15;value*=0x846ca68b;value^=value>>16;return (int)(value&3);}
        }
        public static bool Supports(string mapId)=>mapId=="white-meridian-astra-v1";
        public WhiteMeridianLightingVariant Select(int seed){Validate();return variants[Index(seed)];}
        public void Validate(){
            if(id!="white-meridian-lighting-variants-v1"||revision<1||variants==null||variants.Length!=Ids.Length)throw new ArgumentException("Invalid Meridian lighting set");
            for(int i=0;i<variants.Length;i++){
                var variant=variants[i];
                if(variant==null||variant.id!=Ids[i]||string.IsNullOrWhiteSpace(variant.label)||variant.lighting==null||variant.lighting.id!="white-meridian-warm-v1")throw new ArgumentException("Invalid Meridian lighting slot "+i);
                variant.lighting.Validate();
            }
        }
        public IEnumerable<SurfaceParameter> Parameters(int index){
            var variant=variants[index];
            foreach(var parameter in variant.lighting.Parameters()){
                string field=parameter.Path.Substring(parameter.Path.LastIndexOf('.')+1);
                yield return new SurfaceParameter("visual.whiteMeridian.lighting.variants."+variant.id+"."+field,variant.label+" / "+parameter.Group,parameter.Label,parameter.Description,parameter.Unit,parameter.Minimum,parameter.Maximum,parameter.Step,parameter.Read,parameter.Write);
            }
        }
        public static WhiteMeridianLightingVariants Load(){
            var text=Resources.Load<TextAsset>("WhiteMeridianLightingVariants");if(!text)throw new InvalidOperationException("Missing Meridian lighting variants");
            var result=JsonUtility.FromJson<WhiteMeridianLightingVariants>(text.text);result.Validate();return result;
        }
    }
}
