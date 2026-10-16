using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public static class NativeUserSettings
    {
        public static bool AlwaysHealth { get=>PlayerPrefs.GetInt("spacewars.ui.alwaysHealth",1)!=0; set {NativePreferenceWriter.SetInt("spacewars.ui.alwaysHealth",value?1:0);} }
        public static float Volume { get {float value=PlayerPrefs.GetFloat("spacewars.ui.volume",0);return float.IsNaN(value)||float.IsInfinity(value)?0:Mathf.Clamp01(value);} set {if(NativeSilentStartup.IsSilent)return;NativePreferenceWriter.SetFloat("spacewars.ui.volume",Mathf.Clamp01(value));AudioListener.volume=Mathf.Clamp01(value);} }
    }
    public static class NativeDisplaySettings
    {
        public const string Prefix="spacewars.display.";
        public static bool Fullscreen
        {
            get=>PlayerPrefs.GetInt(Prefix+"fullscreen",1)!=0;
            set {NativePreferenceWriter.SetInt(Prefix+"fullscreen",value?1:0);ApplyDisplay();}
        }
        public static bool Shadows
        {
            get=>PlayerPrefs.GetInt(Prefix+"shadows",1)!=0;
            set {NativePreferenceWriter.SetInt(Prefix+"shadows",value?1:0);ApplyShadows();}
        }
        public static bool VSync
        {
            get=>PlayerPrefs.GetInt(Prefix+"vsync",0)!=0;
            set {NativePreferenceWriter.SetInt(Prefix+"vsync",value?1:0);ApplyVSync();}
        }
        public static Vector2Int SavedResolution=>new Vector2Int(PlayerPrefs.GetInt(Prefix+"width",0),PlayerPrefs.GetInt(Prefix+"height",0));
        public static Vector2Int[] ResolutionOptions(IEnumerable<Vector2Int> modes,Vector2Int desktop)
            =>modes.Append(desktop).Where(m=>m.x>0&&m.y>0).Distinct().OrderBy(m=>m.x).ThenBy(m=>m.y).ToArray();
        public static Vector2Int ResolveResolution(Vector2Int saved,Vector2Int[] modes,Vector2Int desktop)
            =>modes.Contains(saved)?saved:desktop;
        public static Vector2Int DesktopResolution
        {
            get {var desktop=Screen.currentResolution;return desktop.width>0&&desktop.height>0?new Vector2Int(desktop.width,desktop.height):new Vector2Int(Mathf.Max(1,Screen.width),Mathf.Max(1,Screen.height));}
        }
        public static Vector2Int[] AvailableResolutions=>ResolutionOptions(Screen.resolutions.Select(r=>new Vector2Int(r.width,r.height)),DesktopResolution);
        public static Vector2Int Resolution=>ResolveResolution(SavedResolution,AvailableResolutions,DesktopResolution);
        public static void SetResolution(Vector2Int size)
        {
            if(size!=Vector2Int.zero&&!AvailableResolutions.Contains(size))return;
            Commit(size,Fullscreen);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize(){ApplyVSync();ApplyDisplay();if(!NativeSilentStartup.IsSilent)AudioListener.volume=NativeUserSettings.Volume;}
        private static int defaultFrameRate=60;
        public static void ApplyFrameRate(int fallback){defaultFrameRate=fallback;ApplyVSync();}
        public static void ApplyVSync(){QualitySettings.vSyncCount=VSync?1:0;Application.targetFrameRate=NativeDevicePreferences.FrameLimit==0?defaultFrameRate:NativeDevicePreferences.FrameLimit;}
        public static void Preview(Vector2Int size,bool fullscreen)
        {if(!Application.isEditor){var resolved=ResolveResolution(size,AvailableResolutions,DesktopResolution);Screen.SetResolution(resolved.x,resolved.y,fullscreen?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed);}}
        public static void Commit(Vector2Int size,bool fullscreen)
        {NativePreferenceWriter.Apply(()=>{PlayerPrefs.SetInt(Prefix+"width",size.x);PlayerPrefs.SetInt(Prefix+"height",size.y);PlayerPrefs.SetInt(Prefix+"fullscreen",fullscreen?1:0);});ApplyDisplay();}

        public static void ApplyDisplay()
        {
            // Never resize the shared Editor host or its Game view during tests.
            if(Application.isEditor)return;
            var size=Resolution;
            Screen.SetResolution(size.x,size.y,Fullscreen?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed);
        }
        public static void ConfigureSun(Light sun)=>sun.shadows=Shadows?LightShadows.Soft:LightShadows.None;
        public static void ApplyShadows()
        {
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if(light.type==LightType.Directional)ConfigureSun(light);
        }
    }
    public static class NativeSettingsView
    {
        public static VisualElement Open(VisualElement root,NativeMenuNavigation navigation,Action closed,NativeLocalControlSettings controls=null)
            =>new NativeSettingsPage(root,navigation,closed,controls??new NativeLocalControlSettings()).Page;
    }
}
