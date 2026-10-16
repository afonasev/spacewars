using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Spacewars.Presentation
{
    // Restore the last correct in-memory preferences if the storage operation fails.
    public static class NativePreferenceWriter
    {
        public static Action Flush=PlayerPrefs.Save;
        public static event Action<string> Failed;
        private static readonly string[] Ints={"spacewars.display.width","spacewars.display.height","spacewars.display.fullscreen","spacewars.display.shadows","spacewars.display.vsync","spacewars.ui.alwaysHealth",NativeDevicePreferences.Prefix+"frameLimit",NativeDevicePreferences.Prefix+"highContrast",NativeDevicePreferences.Prefix+"showFps",NativeDevicePreferences.Prefix+"invertX",NativeDevicePreferences.Prefix+"invertY",NativeGraphicsSettings.BloomKey,NativeGraphicsSettings.OcclusionKey};
        private static readonly string[] Floats={"spacewars.ui.volume","spacewars.audio.music","spacewars.audio.effects","spacewars.audio.notifications","spacewars.controls.cursorSpeed","spacewars.controls.cameraSpeed","spacewars.controls.cursorRecenterIdleMs",NativeCameraScrollSettings.EdgeKey,NativeCameraScrollSettings.ArrowKey,NativeDevicePreferences.Prefix+"uiScale",NativeDevicePreferences.Prefix+"deadzone"};
        public static void SetFloat(string key,float value)=>Apply(()=>PlayerPrefs.SetFloat(key,value));
        public static void SetInt(string key,int value)=>Apply(()=>PlayerPrefs.SetInt(key,value));
        public static Action Capture()
        {
            var intKeys=Ints.Concat(NativeGraphicsSettings.PreferenceKeys.Where(k=>k.EndsWith("bloomEnabled")||k.EndsWith("occlusionEnabled"))).Distinct().ToArray();
            var ints=intKeys.ToDictionary(k=>k,k=>new KeyValuePair<bool,int>(PlayerPrefs.HasKey(k),PlayerPrefs.GetInt(k)));
            var floats=Floats.Concat(NativeGraphicsSettings.PreferenceKeys.Except(intKeys)).Distinct().ToDictionary(k=>k,k=>new KeyValuePair<bool,float>(PlayerPrefs.HasKey(k),PlayerPrefs.GetFloat(k)));
            return ()=>{
                foreach(var pair in ints)if(pair.Value.Key)PlayerPrefs.SetInt(pair.Key,pair.Value.Value);else PlayerPrefs.DeleteKey(pair.Key);
                foreach(var pair in floats)if(pair.Value.Key)PlayerPrefs.SetFloat(pair.Key,pair.Value.Value);else PlayerPrefs.DeleteKey(pair.Key);
            };
        }
        public static void Apply(Action write)
        {
            var restore=Capture();
            try {write();Flush();}
            catch(Exception e)
            {
                restore();
                Failed?.Invoke("Не удалось сохранить настройки. Прежние значения сохранены; повторите изменение. "+e.Message);
                throw;
            }
        }
    }
}
