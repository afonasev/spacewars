using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public static class NativeUserSettings
    {
        public static bool AlwaysHealth { get=>PlayerPrefs.GetInt("spacewars.ui.alwaysHealth",1)!=0; set {PlayerPrefs.SetInt("spacewars.ui.alwaysHealth",value?1:0);PlayerPrefs.Save();} }
        public static float Volume { get=>PlayerPrefs.GetFloat("spacewars.ui.volume",0); set {PlayerPrefs.SetFloat("spacewars.ui.volume",Mathf.Clamp01(value));PlayerPrefs.Save();AudioListener.volume=Mathf.Clamp01(value);} }
    }
    public static class NativeSettingsView
    {
        public static VisualElement Open(VisualElement root,NativeMenuNavigation navigation,Action closed)
        {
            var page=OrbitalTheme.Screen(root,"settings-screen");var card=OrbitalTheme.Card(page);
            card.Add(OrbitalTheme.Text("НАСТРОЙКИ","orbital-title"));
            var health=new Toggle("Показывать здоровье постоянно"){name="settings-health",value=NativeUserSettings.AlwaysHealth};
            health.RegisterValueChangedCallback(e=>NativeUserSettings.AlwaysHealth=e.newValue);card.Add(health);
            var volume=new Slider("Общая громкость",0,1){name="settings-volume",value=NativeUserSettings.Volume,showInputField=false};
            var value=OrbitalTheme.Text(Mathf.RoundToInt(volume.value*100)+"%","orbital-muted");
            volume.RegisterValueChangedCallback(e=>{NativeUserSettings.Volume=e.newValue;value.text=Mathf.RoundToInt(e.newValue*100)+"%";});card.Add(volume);card.Add(value);
            card.Add(OrbitalTheme.Text("Изменения сохраняются автоматически на этом устройстве.","orbital-muted"));
            card.Add(OrbitalTheme.Text("Геймпад: крестовина или левый стик — выбор; влево и вправо — изменить значение.","orbital-muted"));
            Action back=()=>{page.RemoveFromHierarchy();closed();};
            card.Add(OrbitalTheme.Action("Назад",back,"settings-back"));
            var hints=OrbitalTheme.Text("","orbital-hints");card.Add(hints);navigation.SetScope(page,back,health,hints);return page;
        }
    }
}
