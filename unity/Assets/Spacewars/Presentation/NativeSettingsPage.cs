using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    public sealed class NativeSettingsPage
    {
        public VisualElement Page {get;}
        private readonly VisualElement root,card,content;
        private readonly NativeMenuNavigation nav;
        private readonly Action closed;
        private readonly NativeLocalControlSettings controls;
        private readonly ScrollView scroll;
        private string section="Экран";
        private readonly Dictionary<string,Button> tabs=new Dictionary<string,Button>();
        private readonly Dictionary<string,Vector2> offsets=new Dictionary<string,Vector2>();
        private readonly Label status;
        private readonly Dictionary<string,VisualElement> groups=new Dictionary<string,VisualElement>();
        private static readonly string[] Sections={"Экран","Графика","Звук","Управление","Интерфейс"};
        public NativeSettingsPage(VisualElement root,NativeMenuNavigation navigation,Action closed,NativeLocalControlSettings controls)
        {
            this.root=root;nav=navigation;this.closed=closed;this.controls=controls;
            Page=OrbitalTheme.Screen(root,"settings-screen");card=OrbitalTheme.Card(Page);card.AddToClassList("orbital-settings-card");
            card.Add(OrbitalTheme.Text("НАСТРОЙКИ","orbital-title"));
            var body=new VisualElement(){name="settings-body"};card.Add(body);
            var rail=new VisualElement(){name="settings-tabs"};body.Add(rail);
            foreach(var name in Sections)
            {
                var selected=name;
                var tab=OrbitalTheme.Action(name,()=>SelectSection(selected),"settings-tab-"+name);
                tab.AddToClassList("settings-tab");rail.Add(tab);tabs.Add(name,tab);
            }
            scroll=new ScrollView(ScrollViewMode.Vertical){name="settings-scroll",horizontalScrollerVisibility=ScrollerVisibility.Hidden,verticalScrollerVisibility=ScrollerVisibility.Auto};scroll.AddToClassList("settings-scroll");scroll.style.minHeight=0;scroll.style.flexShrink=1;body.Add(scroll);content=scroll.contentContainer;
            status=OrbitalTheme.Text("Настройки общие для этого устройства. Применяются автоматически.","orbital-muted");status.style.whiteSpace=WhiteSpace.Normal;card.Add(status);
            var actions=new VisualElement();actions.style.flexDirection=FlexDirection.Row;actions.style.flexWrap=Wrap.Wrap;card.Add(actions);
            actions.Add(OrbitalTheme.Action("Сбросить все настройки",Reset,"settings-reset-all"));
            actions.Add(OrbitalTheme.Action("Назад",Back,"settings-back"));
            Action<string> failed=message=>status.text=message;
            Page.RegisterCallback<AttachToPanelEvent>(_=>NativePreferenceWriter.Failed+=failed);
            Page.RegisterCallback<DetachFromPanelEvent>(_=>NativePreferenceWriter.Failed-=failed);
            if(Page.panel!=null)NativePreferenceWriter.Failed+=failed;
            Func<UnityEngine.InputSystem.Gamepad,bool> north=pad=>{
                var focus=root.focusController?.focusedElement as VisualElement;
                if(nav.Scope!=Page||focus is TextField||focus?.GetFirstAncestorOfType<TextField>()!=null)return false;
                Reset();return true;
            };
            nav.North=north;Page.RegisterCallback<DetachFromPanelEvent>(_=>{if(nav.North==north)nav.North=null;});
            Build();nav.SetScope(Page,Back,Page.Q("settings-resolution"));
        }
        private void Back(){Page.RemoveFromHierarchy();closed();}
        private void Run(Action action){try{action();status.text="Настройки применены на этом устройстве.";}catch(Exception e){NativeDisplaySettings.ApplyDisplay();Build();status.text="Не удалось сохранить настройку. Повторите изменение: "+e.Message;}}
        private void SelectSection(string name)
        {
            offsets[section]=scroll.scrollOffset;section=name;ShowSection();
            scroll.scrollOffset=offsets.TryGetValue(section,out var offset)?offset:Vector2.zero;
        }
        private void ShowSection()
        {
            foreach(var pair in groups)pair.Value.style.display=pair.Key==section?DisplayStyle.Flex:DisplayStyle.None;
            foreach(var pair in tabs)
            {
                bool active=pair.Key==section;pair.Value.EnableInClassList("settings-tab-selected",active);
                pair.Value.tooltip=active?pair.Key+" · выбранный раздел":pair.Key;
            }
        }
        private void Build()
        {
            content.Clear();groups.Clear();foreach(var name in Sections){var group=new VisualElement(){name="settings-group-"+name};content.Add(group);groups.Add(name,group);}
            var screen=groups["Экран"];var modes=(new[]{Vector2Int.zero}).Concat(NativeDisplaySettings.AvailableResolutions).ToArray();
            var choices=modes.Select(m=>m==Vector2Int.zero?"Auto · текущий экран":m.x+" × "+m.y).ToList();
            var resolution=new DropdownField("Разрешение",choices,Mathf.Max(0,Array.IndexOf(modes,NativeDisplaySettings.SavedResolution))){name="settings-resolution"};screen.Add(resolution);
            resolution.RegisterValueChangedCallback(_=>Preview(modes[resolution.index],NativeDisplaySettings.Fullscreen));
            Toggle(screen,"Полный экран","settings-fullscreen",NativeDisplaySettings.Fullscreen,v=>Preview(NativeDisplaySettings.SavedResolution,v));
            Toggle(screen,"Вертикальная синхронизация","settings-vsync",NativeDisplaySettings.VSync,v=>NativeDisplaySettings.VSync=v);
            var limits=new[]{0,-1,30,60,120,144,240};var fps=new DropdownField("Ограничение FPS",new List<string>{"По профилю","Без ограничения","30 FPS","60 FPS","120 FPS","144 FPS","240 FPS"},Array.IndexOf(limits,NativeDevicePreferences.FrameLimit)){name="settings-fps-limit"};fps.RegisterValueChangedCallback(_=>Run(()=>NativeDevicePreferences.FrameLimit=limits[fps.index]));screen.Add(fps);
            screen.Add(OrbitalTheme.Text("При включённом VSync частоту определяет экран; ограничитель FPS может не действовать.","orbital-muted"));
            var graphics=groups["Графика"];Toggle(graphics,"Тени","settings-shadows",NativeDisplaySettings.Shadows,v=>NativeDisplaySettings.Shadows=v);NativeGraphicsSettingsView.AddBasic(graphics);
            var audio=groups["Звук"];Slider(audio,"Общая громкость","settings-volume",0,1,NativeUserSettings.Volume,"%",v=>NativeUserSettings.Volume=v,100);NativeAudioSettings.AddSliders(audio);
            var input=groups["Управление"];NativeCameraScrollSettings.AddSliders(input);controls.AddSliders(input);
            Slider(input,"Мёртвая зона стиков","settings-deadzone",0,.5f,NativeDevicePreferences.Deadzone,"%",v=>NativeDevicePreferences.Deadzone=v,100);
            Toggle(input,"Инверсия камеры · X","settings-invert-x",NativeDevicePreferences.InvertX,v=>NativeDevicePreferences.InvertX=v);Toggle(input,"Инверсия камеры · Y","settings-invert-y",NativeDevicePreferences.InvertY,v=>NativeDevicePreferences.InvertY=v);
            input.Add(OrbitalTheme.Action("Управление",()=>{Page.style.display=DisplayStyle.None;NativeControlsHelp.Open(root,nav,()=>{Page.style.display=DisplayStyle.Flex;nav.SetScope(Page,Back,Page.Q("settings-controls"));});},"settings-controls"));
            var ui=groups["Интерфейс"];Toggle(ui,"Показывать здоровье постоянно","settings-health",NativeUserSettings.AlwaysHealth,v=>NativeUserSettings.AlwaysHealth=v);
            Toggle(ui,"Повышенный контраст","settings-contrast",NativeDevicePreferences.HighContrast,v=>NativeDevicePreferences.HighContrast=v);Toggle(ui,"Счётчик FPS","settings-show-fps",NativeDevicePreferences.ShowFps,v=>NativeDevicePreferences.ShowFps=v);
            Slider(ui,"Масштаб интерфейса","settings-ui-scale",1,1.25f,NativeDevicePreferences.UiScale,"%",v=>NativeDevicePreferences.UiScale=v,100);
            ShowSection();
        }
        private void Toggle(VisualElement parent,string label,string name,bool value,Action<bool> changed){var field=new Toggle(label){name=name,value=value};field.RegisterValueChangedCallback(e=>Run(()=>changed(e.newValue)));parent.Add(field);}
        private void Slider(VisualElement parent,string title,string name,float min,float max,float current,string unit,Action<float> changed,float factor=1)
        {var slider=new Slider(title,min,max){name=name,value=current};var label=OrbitalTheme.Text(Mathf.RoundToInt(current*factor)+unit,"orbital-muted");label.AddToClassList("settings-value");slider.RegisterValueChangedCallback(e=>Run(()=>{changed(e.newValue);label.text=Mathf.RoundToInt(e.newValue*factor)+unit;}));parent.Add(slider);parent.Add(label);}
        private void Preview(Vector2Int size,bool fullscreen,Action after=null)
        {
            NativeDisplaySettings.Preview(size,fullscreen);
            Confirm("Сохранить новый режим экрана? Без подтверждения прежний режим вернётся через 15 секунд.",()=>Run(()=>{if(after!=null)after();else NativeDisplaySettings.Commit(size,fullscreen);Build();}),()=>{NativeDisplaySettings.ApplyDisplay();Build();},15);
        }
        private void Reset()
        {
            string scope="все пользовательские настройки";
            Confirm("Сбросить "+scope+"? Прогресс, история, лабораторные профили и согласия не меняются.",()=>{
                Action reset=()=>{NativePreferenceWriter.Apply(()=>{foreach(var name in Sections)ResetGroup(name);});NativeDisplaySettings.ApplyVSync();NativeDisplaySettings.ApplyShadows();NativeGraphicsSettings.Refresh();if(!NativeSilentStartup.IsSilent)AudioListener.volume=NativeUserSettings.Volume;Build();};
                Preview(Vector2Int.zero,true,reset);
            },()=>{});
        }
        private static void ResetGroup(string group)
        {
            string[] keys=Array.Empty<string>();
            if(group=="Экран")keys=new[]{"spacewars.display.width","spacewars.display.height","spacewars.display.fullscreen","spacewars.display.vsync",NativeDevicePreferences.Prefix+"frameLimit"};
            if(group=="Графика"){foreach(var key in NativeGraphicsSettings.PreferenceKeys)PlayerPrefs.DeleteKey(key);keys=new[]{"spacewars.display.shadows"};}
            if(group=="Звук")keys=new[]{"spacewars.ui.volume","spacewars.audio.music","spacewars.audio.effects","spacewars.audio.notifications"};
            if(group=="Управление")keys=new[]{"spacewars.controls.cursorSpeed","spacewars.controls.cameraSpeed","spacewars.controls.cursorRecenterIdleMs",NativeCameraScrollSettings.EdgeKey,NativeCameraScrollSettings.ArrowKey,NativeDevicePreferences.Prefix+"deadzone",NativeDevicePreferences.Prefix+"invertX",NativeDevicePreferences.Prefix+"invertY"};
            if(group=="Интерфейс")keys=new[]{"spacewars.ui.alwaysHealth",NativeDevicePreferences.Prefix+"uiScale",NativeDevicePreferences.Prefix+"highContrast",NativeDevicePreferences.Prefix+"showFps"};
            foreach(var key in keys)PlayerPrefs.DeleteKey(key);
        }
        private void Confirm(string message,Action yes,Action no,int seconds=0)
        {
            var focus=root.focusController?.focusedElement as VisualElement;
            var overlay=OrbitalTheme.Screen(root,"settings-confirm");var box=OrbitalTheme.Card(overlay);box.Add(OrbitalTheme.Text(message,"orbital-muted"));
            bool finished=false;Action<bool> finish=accept=>{if(finished)return;finished=true;overlay.RemoveFromHierarchy();nav.SetScope(Page,Back,tabs[section]);if(accept)yes();else no();if(nav.Scope==Page)nav.SetScope(Page,Back,focus!=null?Page.Q(focus.name)??tabs[section]:tabs[section]);};
            var cancel=OrbitalTheme.Action("Отменить",()=>finish(false),"settings-confirm-cancel");box.Add(cancel);box.Add(OrbitalTheme.Action("Подтвердить",()=>finish(true),"settings-confirm-accept"));nav.SetScope(overlay,()=>finish(false),cancel);
            if(seconds>0){float deadline=Time.realtimeSinceStartup+seconds;overlay.schedule.Execute(()=>{if(Time.realtimeSinceStartup>=deadline)finish(false);}).Every(100);}
            Page.RegisterCallback<DetachFromPanelEvent>(_=>{if(!finished)finish(false);});
        }
    }
}
