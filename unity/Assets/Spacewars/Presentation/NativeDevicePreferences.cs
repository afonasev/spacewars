using System;
using System.Linq;
using UnityEngine;
using Spacewars.Input;
namespace Spacewars.Presentation
{
    // Device preferences are deliberately independent of simulation/balance profiles.
    public static class NativeDevicePreferences
    {
        public const string Prefix="spacewars.device.";
        private static float Finite(string key,float fallback,float min,float max){float v=PlayerPrefs.GetFloat(Prefix+key,fallback);return float.IsNaN(v)||float.IsInfinity(v)?fallback:Mathf.Clamp(v,min,max);}
        public static int FrameLimit {get {int v=PlayerPrefs.GetInt(Prefix+"frameLimit",0);return new[]{-1,0,30,60,120,144,240}.Contains(v)?v:0;}set {NativePreferenceWriter.SetInt(Prefix+"frameLimit",value);NativeDisplaySettings.ApplyVSync();}}
        public static float UiScale {get=>Finite("uiScale",1,1,1.25f);set {NativePreferenceWriter.SetFloat(Prefix+"uiScale",value);}}
        public static bool HighContrast {get=>PlayerPrefs.GetInt(Prefix+"highContrast",0)!=0;set {NativePreferenceWriter.SetInt(Prefix+"highContrast",value?1:0);}}
        public static bool ShowFps {get=>PlayerPrefs.GetInt(Prefix+"showFps",0)!=0;set {NativePreferenceWriter.SetInt(Prefix+"showFps",value?1:0);}}
        private static NativeLocalInputProfile input;
        public static float DefaultDeadzone => (input??(input=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text))).deadzone;
        public static float Deadzone {get=>Finite("deadzone",DefaultDeadzone,0,.5f);set {NativePreferenceWriter.SetFloat(Prefix+"deadzone",value);}}
        public static bool InvertX {get=>PlayerPrefs.GetInt(Prefix+"invertX",0)!=0;set {NativePreferenceWriter.SetInt(Prefix+"invertX",value?1:0);}}
        public static bool InvertY {get=>PlayerPrefs.GetInt(Prefix+"invertY",0)!=0;set {NativePreferenceWriter.SetInt(Prefix+"invertY",value?1:0);}}
    }
}
