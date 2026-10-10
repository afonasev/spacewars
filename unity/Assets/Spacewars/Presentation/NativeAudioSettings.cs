using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public static class NativeAudioSettings
    {
        public const string Prefix="spacewars.audio.";
        private static float Get(string kind)=>Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix+kind,1));
        private static void Set(string kind,float value){PlayerPrefs.SetFloat(Prefix+kind,Mathf.Clamp01(value));PlayerPrefs.Save();}
        public static float Music{get=>Get("music");set=>Set("music",value);}
        public static float Effects{get=>Get("effects");set=>Set("effects",value);}
        public static float Notifications{get=>Get("notifications");set=>Set("notifications",value);}
        public static void AddSliders(VisualElement content)
        {
            Add(content,"Музыка","music",Music);Add(content,"Эффекты","effects",Effects);Add(content,"Подтверждения и уведомления","notifications",Notifications);
        }
        private static void Add(VisualElement content,string label,string key,float current)
        {
            var slider=new Slider(label,0,1){name="settings-audio-"+key,value=current};
            var value=OrbitalTheme.Text(Mathf.RoundToInt(current*100)+"%","orbital-muted");value.AddToClassList("settings-value");
            slider.RegisterValueChangedCallback(e=>{Set(key,e.newValue);value.text=Mathf.RoundToInt(e.newValue*100)+"%";});content.Add(slider);content.Add(value);
        }
    }
}
