using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public static class NativeGameplayAudioProfile
    {
        public const string Key="spacewars.audio.hybrid-profile-v1";
        private static GameplayAudioProfile current;
        public static GameplayAudioProfile Current
        {
            get
            {
                if(current!=null)return current;
                var baseline=Resources.Load<TextAsset>("GameplayAudioProfile").text;
                try{current=JsonUtility.FromJson<GameplayAudioProfile>(PlayerPrefs.GetString(Key,baseline));current.Validate();}
                catch{current=JsonUtility.FromJson<GameplayAudioProfile>(baseline);current.Validate();}
                return current;
            }
        }
        public static void Save(GameplayAudioProfile candidate)
        {candidate.Validate();var text=JsonUtility.ToJson(candidate);PlayerPrefs.SetString(Key,text);PlayerPrefs.Save();current=JsonUtility.FromJson<GameplayAudioProfile>(text);}
        public static void UseSession(GameplayAudioProfile profile){profile.Validate();current=JsonUtility.FromJson<GameplayAudioProfile>(JsonUtility.ToJson(profile));}
        public static void Reload()=>current=null;
        public static VisualElement Open(VisualElement root,NativeMenuNavigation navigation,Action closed)
        {
            var draft=JsonUtility.FromJson<GameplayAudioProfile>(JsonUtility.ToJson(Current));
            var page=OrbitalTheme.Screen(root,"audio-laboratory-screen");var card=OrbitalTheme.Card(page);card.AddToClassList("orbital-lab-card");
            card.Add(OrbitalTheme.Text("ЗВУК · СМЕШАННЫЙ","orbital-title"));
            card.Add(OrbitalTheme.Text("Профиль "+draft.id+". Настройки слышимости и микса применяются отдельно от правил матча.","orbital-muted"));
            var scroll=new ScrollView(){name="audio-lab-fields"};scroll.style.flexGrow=1;scroll.style.minHeight=0;card.Add(scroll);
            foreach(var field in GameplayAudioMetadata.Fields)
            {
                var slider=new Slider(field.Label+" · "+field.Unit,(float)field.Minimum,(float)field.Maximum){name=field.Path,value=(float)field.Read(draft)};
                var row=new VisualElement();row.AddToClassList("lab-parameter-row");
                slider.style.flexGrow=1;slider.style.minWidth=0;
                var value=OrbitalTheme.Text(field.Read(draft).ToString("0.##"),"lab-unit");slider.tooltip=field.Description+" · Диапазон: "+field.Minimum+"–"+field.Maximum+" · шаг "+field.Step;
                slider.RegisterValueChangedCallback(e=>{field.Write(draft,e.newValue);slider.SetValueWithoutNotify((float)field.Read(draft));value.text=field.Read(draft).ToString("0.##");});
                row.Add(slider);row.Add(value);scroll.Add(row);
            }
            var status=OrbitalTheme.Text("","orbital-muted");card.Add(status);
            card.Add(OrbitalTheme.Action("Сохранить и применить",()=>{try{Save(draft);status.text="Профиль звука сохранён.";}catch(Exception){status.text="Проверьте дальность и лимит групп движения относительно общего числа источников.";}},"audio-lab-save"));
            Action back=()=>{page.RemoveFromHierarchy();closed();};card.Add(OrbitalTheme.Action("Назад",back,"audio-lab-back"));navigation.SetScope(page,back,scroll.Q<Slider>());return page;
        }
    }
}
