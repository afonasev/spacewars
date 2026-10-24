using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Spacewars.Presentation
{
    // Device presentation preferences never modify authored Resources or simulation profiles.
    public static class NativeGraphicsSettings
    {
        public const string Prefix="spacewars.graphics.";
        public const string AaPath="visual.cameraAntialiasing.mode";
        public const string BloomKey=Prefix+"bloomEnabled",OcclusionKey=Prefix+"occlusionEnabled";
        public static event Action<string> Changed;
        // Editor and Player QA may inject a namespace instead of touching real device preferences.
        private static string StoragePrefix=>Prefix+(Environment.GetEnvironmentVariable("SPACEWARS_GRAPHICS_PREFS_NAMESPACE")??"");
        private static string Key(string path)=>StoragePrefix+path;
        private static SurfaceParameter[] catalog;
        private static T Raw<T>(string name){var asset=Resources.Load<TextAsset>(name);if(!asset)throw new InvalidOperationException("Missing graphics defaults: "+name);return JsonUtility.FromJson<T>(asset.text);}
        public static IReadOnlyList<SurfaceParameter> Parameters=>catalog??(catalog=Raw<CameraAntialiasingProfile>("CameraAntialiasingProfile").Parameters()
            .Concat(Raw<ContactOcclusionProfile>("ContactOcclusionProfile").Parameters()).Concat(Raw<ActorMaterialProfile>("ActorMaterialProfile").Parameters())
            .Concat(Raw<IndustrialLightingProfile>("IndustrialLightingProfile").Parameters()).Concat(Raw<IndustrialLightingProfile>("ThreeCrossingsLightingProfile").Parameters()).ToArray());
        public static IEnumerable<string> PreferenceKeys=>Parameters.Select(p=>Key(p.Path)).Concat(new[]{Key("bloomEnabled"),Key("occlusionEnabled"),Key("groundTrailsEnabled")});
        public static bool GroundTrailsEnabled{get=>PlayerPrefs.GetInt(Key("groundTrailsEnabled"),1)!=0;set{NativePreferenceWriter.SetInt(Key("groundTrailsEnabled"),value?1:0);Changed?.Invoke("visual.groundTrails.enabled");}}
        public static bool BloomEnabled{get=>PlayerPrefs.GetInt(Key("bloomEnabled"),1)!=0;set{NativePreferenceWriter.SetInt(Key("bloomEnabled"),value?1:0);Changed?.Invoke("visual.actorFinish.bloomEnabled");}}
        public static bool OcclusionEnabled{get=>PlayerPrefs.GetInt(Key("occlusionEnabled"),1)!=0;set{NativePreferenceWriter.SetInt(Key("occlusionEnabled"),value?1:0);Changed?.Invoke("visual.contactOcclusion.enabled");}}
        public static float Read(SurfaceParameter p)
        {
            // Art tuning belongs to authored Balance Lab profiles. Ignore legacy player sliders.
            if(p.Path!=AaPath)return p.Read();
            float value=PlayerPrefs.GetFloat(Key(p.Path),p.Read());
            return Valid(p,value)?value:p.Read();
        }
        private static bool Valid(SurfaceParameter p,float value)=>!float.IsNaN(value)&&!float.IsInfinity(value)&&value>=p.Minimum&&value<=p.Maximum&&(p.Path!=AaPath||value==Mathf.Round(value));
        public static void Set(string path,float value)
        {
            if(path!=AaPath)throw new ArgumentException("Developer graphics controls belong to Balance Lab",nameof(path));
            var p=Parameters.Single(x=>x.Path==path);if(!Valid(p,value))throw new ArgumentOutOfRangeException(nameof(value),"Invalid graphics value: "+path);
            NativePreferenceWriter.SetFloat(Key(path),value);Changed?.Invoke(path);
        }
        public static void Apply(IEnumerable<SurfaceParameter> parameters){foreach(var p in parameters)p.Write(Read(p));}
        public static void Refresh()=>Changed?.Invoke("*");
        public static void Reset(){NativePreferenceWriter.Apply(()=>{foreach(var key in PreferenceKeys)PlayerPrefs.DeleteKey(key);});Refresh();}
        public static string Group(SurfaceParameter p)
        {
            if(p.Path==AaPath)return "Сглаживание";
            if(p.Path.StartsWith("visual.threeCrossings.lighting."))return "Освещение · Three Crossings";
            if(p.Path.StartsWith("visual.lighting."))return "Освещение · остальные карты";
            if(p.Path.StartsWith("visual.contactOcclusion."))return "Контактные тени (SSAO)";
            return p.Group=="Refinery"?"Огни доходного завода":p.Group=="Hangar"?"Платформа ангара":p.Group=="Armor"?"Окрашенная броня":p.Group=="Metal"?"Металлические детали":p.Group=="Structure"?"Тёмные конструкции":p.Group=="Lights"?"Подсветка техники":"Свечение (Bloom)";
        }
        public static string Label(SurfaceParameter p)
        {
            string key=p.Path.Substring(p.Path.LastIndexOf('.')+1);
            switch(key){
                case "mode":return "Сглаживание";
                case "armorMetallic":case "metalMetallic":return "Металличность";
                case "armorSmoothness":case "metalSmoothness":case "structureSmoothness":return "Гладкость";
                case "buildingLampEmission":return "Яркость огней зданий";
                case "refineryBeaconEmission":return "Яркость угловых огней завода";
                case "hangarPlatformGray":return "Светлота платформы ангара";
                case "darkLift":return "Осветление тёмных деталей";
                case "emissionGain":return "Яркость системных огней";case "teamEmission":return "Яркость командных огней";
                case "bloomIntensity":return "Сила свечения";case "bloomThreshold":return "Порог яркости свечения";case "bloomScatter":return "Рассеивание свечения";
                case "intensity":return "Сила контактных теней";case "radius":return "Радиус контактных теней, м";case "falloff":return "Дистанция контактных теней, м";
                case "sunHue":return "Оттенок солнечного света";case "sunSaturation":return "Насыщенность солнечного света";case "sunIntensity":return "Яркость солнца";
                case "sunElevation":return "Высота солнца, °";case "sunAzimuth":return "Направление солнца, °";
                case "ambientHue":return "Оттенок окружающего света";case "ambientSaturation":return "Насыщенность окружающего света";case "ambientValue":return "Яркость окружающего света";
                case "shadowStrength":return "Плотность теней";case "shadowDistance":return "Минимальная дальность теней, м";case "shadowCoveragePadding":return "Запас покрытия теней";
                case "steelHighlight":return "Сила блика на стали";case "steelExponent":return "Концентрация блика на стали";case "concreteHighlight":return "Сила блика на бетоне";case "concreteExponent":return "Концентрация блика на бетоне";
                default:throw new InvalidOperationException("Untranslated graphics control: "+p.Path);
            }
        }
    }
}
