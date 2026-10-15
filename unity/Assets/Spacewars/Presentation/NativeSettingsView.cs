using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public static class NativeUserSettings
    {
        public static bool AlwaysHealth { get=>PlayerPrefs.GetInt("spacewars.ui.alwaysHealth",1)!=0; set {PlayerPrefs.SetInt("spacewars.ui.alwaysHealth",value?1:0);PlayerPrefs.Save();} }
        public static float Volume { get=>PlayerPrefs.GetFloat("spacewars.ui.volume",0); set {if(NativeSilentStartup.IsSilent)return;PlayerPrefs.SetFloat("spacewars.ui.volume",Mathf.Clamp01(value));PlayerPrefs.Save();AudioListener.volume=Mathf.Clamp01(value);} }
    }
    public static class NativeDisplaySettings
    {
        public const string Prefix="spacewars.display.";
        public static bool Fullscreen
        {
            get=>PlayerPrefs.GetInt(Prefix+"fullscreen",1)!=0;
            set {PlayerPrefs.SetInt(Prefix+"fullscreen",value?1:0);PlayerPrefs.Save();ApplyDisplay();}
        }
        public static bool Shadows
        {
            get=>PlayerPrefs.GetInt(Prefix+"shadows",1)!=0;
            set {PlayerPrefs.SetInt(Prefix+"shadows",value?1:0);PlayerPrefs.Save();ApplyShadows();}
        }
        public static bool VSync
        {
            get=>PlayerPrefs.GetInt(Prefix+"vsync",0)!=0;
            set {PlayerPrefs.SetInt(Prefix+"vsync",value?1:0);PlayerPrefs.Save();ApplyVSync();}
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
            if(!AvailableResolutions.Contains(size))return;
            PlayerPrefs.SetInt(Prefix+"width",size.x);PlayerPrefs.SetInt(Prefix+"height",size.y);PlayerPrefs.Save();ApplyDisplay();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize(){ApplyVSync();ApplyDisplay();if(!NativeSilentStartup.IsSilent)AudioListener.volume=NativeUserSettings.Volume;}
        public static void ApplyVSync()=>QualitySettings.vSyncCount=VSync?1:0;
        public static void ApplyDisplay()
        {
            // Never resize the shared Editor host or its Game view during tests.
            if(Application.isEditor)return;
            var size=Resolution;
            Screen.SetResolution(size.x,size.y,Fullscreen?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed);
        }
        public static void ConfigureSun(Light sun)=>sun.shadows=Shadows?LightShadows.Soft:LightShadows.None;
        private static void ApplyShadows()
        {
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if(light.type==LightType.Directional)ConfigureSun(light);
        }
    }
    public static class NativeSettingsView
    {
        public static VisualElement Open(VisualElement root,NativeMenuNavigation navigation,Action closed,NativeLocalControlSettings controls=null)
        {
            var page=OrbitalTheme.Screen(root,"settings-screen");var card=OrbitalTheme.Card(page);
            card.AddToClassList("orbital-settings-card");
            card.Add(OrbitalTheme.Text("НАСТРОЙКИ","orbital-title"));
            var scroll=new ScrollView(ScrollViewMode.Vertical){name="settings-scroll",horizontalScrollerVisibility=ScrollerVisibility.Hidden,verticalScrollerVisibility=ScrollerVisibility.Auto};
            scroll.AddToClassList("settings-scroll");scroll.style.flexShrink=1;scroll.style.minHeight=0;card.Add(scroll);var content=scroll.contentContainer;
            var modes=NativeDisplaySettings.AvailableResolutions;
            var resolution=new DropdownField("Разрешение",modes.Select(m=>m.x+" × "+m.y).ToList(),Array.IndexOf(modes,NativeDisplaySettings.Resolution)){name="settings-resolution"};
            resolution.RegisterValueChangedCallback(_=>{if(resolution.index>=0)NativeDisplaySettings.SetResolution(modes[resolution.index]);});content.Add(resolution);
            var fullscreen=new Toggle("Полный экран"){name="settings-fullscreen",value=NativeDisplaySettings.Fullscreen};
            fullscreen.RegisterValueChangedCallback(e=>NativeDisplaySettings.Fullscreen=e.newValue);content.Add(fullscreen);
            var vsync=new Toggle("Вертикальная синхронизация"){name="settings-vsync",value=NativeDisplaySettings.VSync};
            vsync.RegisterValueChangedCallback(e=>NativeDisplaySettings.VSync=e.newValue);content.Add(vsync);
            var shadows=new Toggle("Тени"){name="settings-shadows",value=NativeDisplaySettings.Shadows};
            shadows.RegisterValueChangedCallback(e=>NativeDisplaySettings.Shadows=e.newValue);content.Add(shadows);
            NativeGraphicsSettingsView.AddBasic(content);
            var health=new Toggle("Показывать здоровье постоянно"){name="settings-health",value=NativeUserSettings.AlwaysHealth};
            health.RegisterValueChangedCallback(e=>NativeUserSettings.AlwaysHealth=e.newValue);content.Add(health);
            var volume=new Slider("Общая громкость",0,1){name="settings-volume",value=NativeUserSettings.Volume,showInputField=false};
            var value=OrbitalTheme.Text(Mathf.RoundToInt(volume.value*100)+"%","orbital-muted");value.AddToClassList("settings-value");
            volume.RegisterValueChangedCallback(e=>{NativeUserSettings.Volume=e.newValue;value.text=Mathf.RoundToInt(e.newValue*100)+"%";});content.Add(volume);content.Add(value);
            NativeAudioSettings.AddSliders(content);
            NativeCameraScrollSettings.AddSliders(content);
            controls?.AddSliders(content);
            var help=OrbitalTheme.Action("Управление",()=>{page.style.display=DisplayStyle.None;NativeControlsHelp.Open(root,navigation,()=>{page.style.display=DisplayStyle.Flex;navigation.SetScope(page,()=>{page.RemoveFromHierarchy();closed();},card.Q<Button>("settings-controls"));});},"settings-controls");content.Add(help);
            content.Add(OrbitalTheme.Text("Изменения применяются и сохраняются автоматически на этом устройстве.","orbital-muted"));
            Action back=()=>{page.RemoveFromHierarchy();closed();};
            card.Add(OrbitalTheme.Action("Назад",back,"settings-back"));
            navigation.SetScope(page,back,resolution);return page;
        }
    }
}
