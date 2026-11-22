using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
namespace Spacewars.Presentation
{
    public sealed class NativeSettingsPage : INativeMenuScope
    {
        public VisualElement Page {get;}
        private readonly VisualElement root,card,content,rail,actions;
        private readonly NativeMenuNavigation nav;
        private readonly Action closed;
        private readonly NativeLocalControlSettings controls;
        private readonly ScrollView scroll;
        private string section="Видео";
        private Slider editingSlider;
        private readonly Dictionary<string,Button> tabs=new Dictionary<string,Button>();
        private readonly Dictionary<string,Vector2> offsets=new Dictionary<string,Vector2>();
        private readonly Label status,help,editHint;
        private readonly Dictionary<string,VisualElement> groups=new Dictionary<string,VisualElement>();
        private static readonly string[] Sections={"Видео","Звук","Управление","Интерфейс"};
        public NativeSettingsPage(VisualElement root,NativeMenuNavigation navigation,Action closed,NativeLocalControlSettings controls,Action<Action> openHistory=null)
        {
            this.root=root;nav=navigation;this.closed=closed;this.controls=controls;
            Page=OrbitalTheme.Screen(root,"settings-screen");Page.userData=this;card=OrbitalTheme.Card(Page);card.AddToClassList("orbital-settings-card");
            card.Add(OrbitalTheme.Text("НАСТРОЙКИ","orbital-title"));
            Page.RegisterCallback<GeometryChangedEvent>(evt=>
                Page.EnableInClassList("settings-compact",evt.newRect.height<540||evt.newRect.width<700));
            var body=new VisualElement(){name="settings-body"};card.Add(body);
            rail=new VisualElement(){name="settings-tabs"};body.Add(rail);
            foreach(var name in Sections)
            {
                var selected=name;
                var tab=MenuAction(name,()=>SelectSection(selected),"settings-tab-"+name);
                tab.AddToClassList("settings-tab");rail.Add(tab);tabs.Add(name,tab);
            }
            scroll=new ScrollView(ScrollViewMode.Vertical){name="settings-scroll",horizontalScrollerVisibility=ScrollerVisibility.Hidden,verticalScrollerVisibility=ScrollerVisibility.Auto};scroll.AddToClassList("settings-scroll");scroll.style.minHeight=0;scroll.style.flexShrink=1;body.Add(scroll);content=scroll.contentContainer;
            help=OrbitalTheme.Text("Выберите настройку, чтобы увидеть, на что она влияет.","orbital-muted");help.name="settings-help";help.style.whiteSpace=WhiteSpace.Normal;card.Add(help);
            editHint=OrbitalTheme.Text("","settings-edit-hint");card.Add(editHint);
            status=OrbitalTheme.Text("Изменения применяются автоматически.","orbital-muted");status.name="settings-status";status.style.whiteSpace=WhiteSpace.Normal;card.Add(status);
            actions=new VisualElement(){name="settings-actions"};card.Add(actions);
            actions.Add(MenuAction("Сбросить все настройки",Reset,"settings-reset-all"));
            if(openHistory!=null)actions.Add(MenuAction("Что нового",()=>{
                Page.style.display=DisplayStyle.None;
                openHistory(()=>{Page.style.display=DisplayStyle.Flex;nav.SetScope(Page,Back,Page.Q("settings-history"));});
            },"settings-history"));
            actions.Add(MenuAction("Назад",Back,"settings-back"));
            Action<string> failed=message=>status.text=message;
            Page.RegisterCallback<AttachToPanelEvent>(_=>NativePreferenceWriter.Failed+=failed);
            Page.RegisterCallback<DetachFromPanelEvent>(_=>NativePreferenceWriter.Failed-=failed);
            if(Page.panel!=null)NativePreferenceWriter.Failed+=failed;
            Func<UnityEngine.InputSystem.Gamepad,bool> north=pad=>{
                var focus=root.focusController?.focusedElement as VisualElement;
                if(nav.Scope!=Page||editingSlider!=null||focus is TextField||focus?.GetFirstAncestorOfType<TextField>()!=null)return false;
                Reset();return true;
            };
            nav.North=north;Page.RegisterCallback<DetachFromPanelEvent>(_=>{if(nav.North==north)nav.North=null;});
            Page.RegisterCallback<PointerDownEvent>(_=>EndEditing(),TrickleDown.TrickleDown);
            Page.RegisterCallback<DetachFromPanelEvent>(_=>EndEditing());
            Build();nav.SetScope(Page,Back,Page.Q("settings-resolution"));
        }
        private static Button MenuAction(string title,Action action,string name)
        {
            var button=OrbitalTheme.Action("",action,name);
            button.tooltip=title;
            button.AddToClassList("settings-action");
            button.Add(new Label(title){name="settings-action-caption",pickingMode=PickingMode.Ignore});
            return button;
        }
        bool INativeMenuScope.IncludesControl(VisualElement control)=>control.GetFirstAncestorOfType<Scroller>()==null;
        bool INativeMenuScope.ReadGamepad(Gamepad pad,float now)=>false;
        string INativeMenuScope.Hints(Gamepad pad)
        {
            UpdateEditHint();
            return "";
        }
        bool INativeMenuScope.Move(Vector2 direction)
        {
            if(editingSlider!=null)
            {
                if(direction.x!=0)nav.Adjust(editingSlider,(int)Mathf.Sign(direction.x));
                return true;
            }
            var all=nav.Controls();
            var current=nav.Current(all);
            if(current==null)return true;
            var currentColumn=rail.Contains(current)?rail:actions.Contains(current)?actions:content;
            var column=all.Where(currentColumn.Contains).ToList();
            int index=column.IndexOf(current);
            VisualElement next=null;
            if(direction.x!=0)
            {
                if(currentColumn==actions)next=column.ElementAtOrDefault(index+(direction.x>0?1:-1));
                else if(currentColumn==rail&&direction.x>0)next=all.FirstOrDefault(content.Contains);
                else if(currentColumn==content&&direction.x<0)next=tabs[section];
            }
            else if(direction.y!=0)
            {
                if(currentColumn==actions)
                {
                    if(direction.y>0)next=all.LastOrDefault(content.Contains)??tabs[section];
                }
                else
                {
                    next=column.ElementAtOrDefault(index+(direction.y>0?-1:1));
                    if(next==null&&direction.y<0)next=all.FirstOrDefault(actions.Contains);
                }
            }
            if(next!=null)nav.Focus(next);
            UpdateEditHint();
            return true;
        }
        bool INativeMenuScope.Submit(VisualElement focused)
        {
            if(focused is not Slider slider)return false;
            if(editingSlider==null)
            {
                editingSlider=slider;
                slider.AddToClassList("settings-editing");
                UpdateEditHint();
            }
            return true;
        }
        bool INativeMenuScope.Cancel()
        {
            bool wasEditing = editingSlider != null;
            EndEditing();
            var focused = root.focusController?.focusedElement as VisualElement;
            if (nav.UsingGamepad && nav.CurrentGamepad?.buttonEast.wasPressedThisFrame == true &&
                focused != null && content.Contains(focused))
            {
                nav.Focus(tabs[section]);
                return true;
            }
            return wasEditing;
        }
        private void EndEditing()
        {
            if(editingSlider==null)return;
            editingSlider.RemoveFromClassList("settings-editing");
            editingSlider=null;
            UpdateEditHint();
        }
        private void UpdateEditHint()
        {
            if(editingSlider==null){editHint.text="";return;}
            var done = nav.UsingGamepad
                ? NativeControllerGlyph.Symbol("B",nav.CurrentGamepad)+" К разделам"
                : "Esc Готово";
            editHint.text=$"← → Изменить    {done}";
        }
        private static void Heading(VisualElement group,string title)
        {
            group.Add(OrbitalTheme.Text(title,"settings-heading"));
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
            EndEditing();content.Clear();groups.Clear();foreach(var name in Sections){var group=new VisualElement(){name="settings-group-"+name};content.Add(group);groups.Add(name,group);}
            var screen=groups["Видео"];Heading(screen,"Режим экрана");var modes=(new[]{Vector2Int.zero}).Concat(NativeDisplaySettings.AvailableResolutions).ToArray();
            var choices=modes.Select(m=>m==Vector2Int.zero?"Auto · текущий экран":m.x+" × "+m.y).ToList();
            var resolution=new DropdownField("Разрешение",choices,Mathf.Max(0,Array.IndexOf(modes,NativeDisplaySettings.SavedResolution))){name="settings-resolution"};screen.Add(resolution);
            resolution.RegisterValueChangedCallback(_=>Preview(modes[resolution.index],NativeDisplaySettings.Fullscreen));
            Toggle(screen,"Полный экран","settings-fullscreen",NativeDisplaySettings.Fullscreen,v=>Preview(NativeDisplaySettings.SavedResolution,v));
            Heading(screen,"Частота кадров");
            Toggle(screen,"Вертикальная синхронизация","settings-vsync",NativeDisplaySettings.VSync,v=>NativeDisplaySettings.VSync=v);
            var limits=new[]{0,-1,30,60,120,144,240};var fps=new DropdownField("Ограничение FPS",new List<string>{"По профилю","Без ограничения","30 FPS","60 FPS","120 FPS","144 FPS","240 FPS"},Array.IndexOf(limits,NativeDevicePreferences.FrameLimit)){name="settings-fps-limit"};fps.RegisterValueChangedCallback(_=>Run(()=>NativeDevicePreferences.FrameLimit=limits[fps.index]));screen.Add(fps);
            screen.Add(OrbitalTheme.Text("При включённом VSync частоту определяет экран; ограничитель FPS может не действовать.","orbital-muted"));
            Heading(screen,"Качество изображения");
            Toggle(screen,"Тени","settings-shadows",NativeDisplaySettings.Shadows,v=>NativeDisplaySettings.Shadows=v);
            var quality=new VisualElement();screen.Add(quality);
            Heading(screen,"Эффекты");
            var effects=new VisualElement();screen.Add(effects);
            NativeGraphicsSettingsView.AddBasic(quality,effects);
            var audio=groups["Звук"];Slider(audio,"Общая громкость","settings-volume",0,1,NativeUserSettings.Volume,"%",v=>NativeUserSettings.Volume=v,100);NativeAudioSettings.AddSliders(audio);
            var input=groups["Управление"];Heading(input,"Клавиатура и мышь");NativeCameraScrollSettings.AddSliders(input);Heading(input,"Геймпад");controls.AddSliders(input);
            Slider(input,"Мёртвая зона стиков","settings-deadzone",0,.5f,NativeDevicePreferences.Deadzone,"%",v=>NativeDevicePreferences.Deadzone=v,100);
            Toggle(input,"Инверсия камеры · X","settings-invert-x",NativeDevicePreferences.InvertX,v=>NativeDevicePreferences.InvertX=v);Toggle(input,"Инверсия камеры · Y","settings-invert-y",NativeDevicePreferences.InvertY,v=>NativeDevicePreferences.InvertY=v);
            input.Add(MenuAction("Схема управления",()=>{Page.style.display=DisplayStyle.None;NativeControlsHelp.Open(root,nav,()=>{Page.style.display=DisplayStyle.Flex;nav.SetScope(Page,Back,Page.Q("settings-controls"));});},"settings-controls"));
            var ui=groups["Интерфейс"];
            Heading(ui,"Внешний вид");
            Slider(ui,"Масштаб интерфейса","settings-ui-scale",1,1.25f,NativeDevicePreferences.UiScale,"%",v=>NativeDevicePreferences.UiScale=v,100);
            Toggle(ui,"Повышенный контраст","settings-contrast",NativeDevicePreferences.HighContrast,v=>NativeDevicePreferences.HighContrast=v);
            Heading(ui,"Полоски здоровья");
            Toggle(ui,"Показывать здоровье постоянно","settings-health",NativeUserSettings.AlwaysHealth,v=>NativeUserSettings.AlwaysHealth=v);
            Toggle(ui,"Скрывать здоровье при отдалении","settings-hide-distant-health",NativeUserSettings.HideDistantHealth,v=>NativeUserSettings.HideDistantHealth=v);
            Heading(ui,"Счётчики");
            Toggle(ui,"Счётчик FPS","settings-show-fps",NativeDevicePreferences.ShowFps,v=>NativeDevicePreferences.ShowFps=v);
            Toggle(ui,"Счётчик TPS","settings-show-tps",NativeDevicePreferences.ShowTps,v=>NativeDevicePreferences.ShowTps=v);
            foreach(var group in groups.Values)BindHints(group);
            ShowSection();
        }
        private void BindHints(VisualElement group)
        {
            foreach(var field in group.Query<VisualElement>().ToList().Where(e=>e is Toggle||e is Slider||e is DropdownField)){
                if(!SettingHints.TryGetValue(field.name,out var text))throw new InvalidOperationException("Missing setting explanation: "+field.name);
                field.tooltip=text;
                field.RegisterCallback<PointerEnterEvent>(_=>help.text=text);
                field.RegisterCallback<FocusInEvent>(_=>help.text=text);
                if(field is Slider slider)
                    slider.RegisterCallback<FocusOutEvent>(_=>{if(editingSlider==slider)EndEditing();});
            }
        }
        private static readonly Dictionary<string,string> SettingHints=new Dictionary<string,string>{
            {"settings-resolution","Разрешение всего окна. Меньше пикселей — ниже нагрузка на GPU, но менее чёткие сцена и интерфейс. Применяется ко всем локальным экранам; новый режим нужно подтвердить."},
            {"settings-fullscreen","Разворачивает игру на весь дисплей. Само по себе не снижает сложность сцены; для FPS также важны выбранное разрешение и Render Scale."},
            {"settings-vsync","Согласует кадры с частотой дисплея и убирает разрывы изображения. Может ограничить FPS и увеличить задержку ввода; скорость симуляции не меняется."},
            {"settings-fps-limit","Ограничивает частоту отрисовки и расход GPU. «Без ограничения» полезно для замеров; VSync может задать более низкий предел. Скорость матча не меняется."},
            {"settings-shadows","Тени от солнца у техники, зданий и окружения. Выключение уменьшает работу GPU, особенно при нескольких видах, но сцена становится более плоской."},
            {"settings-graphics-aa","Смягчает ступеньки на краях объектов. MSAA4× увеличивает расход памяти и GPU; SMAA обрабатывает готовый кадр. Выключение помогает FPS, но края становятся зубчатыми."},
            {"settings-graphics-render-scale","Разрешение только 3D-сцены всех локальных видов. 75% означает примерно на 44% меньше пикселей. Ниже — легче GPU и мягче картинка; интерфейс, окно и скорость матча не меняются."},
            {"settings-graphics-ssao","Затемняет стыки и области под объектами, добавляя объём. Дополнительная обработка каждого вида нагружает GPU; выключение может увеличить FPS."},
            {"settings-graphics-bloom","Добавляет ореолы вокруг ярких огней. Выключение убирает свечение и уменьшает постобработку GPU, не выключая сами огни."},
            {"settings-graphics-trails","Показывает отпечатки колёс и гусениц. Выключение скрывает старые и новые следы и пропускает их отрисовку; движение и физические препятствия не меняются."},
            {"settings-graphics-wrecks","Полностью выключает остовы: удаляет существующие модели, дым, обломки и физические препятствия; запрещает новые. Разгружает графику и обработку препятствий, меняет тактику. Действует на весь матч, включая паузу. Включение создаёт остовы только от будущих смертей."},
            {"settings-volume","Общая громкость всех игровых звуков. Не меняет громкость других приложений и параметры производительности."},
            {"settings-audio-music","Громкость фоновой музыки относительно общей громкости."},
            {"settings-audio-effects","Громкость оружия, взрывов, движения и других игровых эффектов."},
            {"settings-audio-notifications","Громкость подтверждений приказов и уведомлений об игровых событиях."},
            {"settings-audio-voice-enabled","Включает речевые сигналы об игровых событиях. Остальные звуки продолжают работать."},
            {"settings-audio-unitVoice-enabled","Операторы и экипажи отвечают на выбор юнита и приказы игрока."},
            {"settings-audio-unitVoice","Громкость ответов юнитов относительно общей громкости."},
            {"settings-audio-voice","Громкость речевых сигналов относительно общей громкости; слышны, если голосовые сигналы включены."},
            {"settings-camera-edge","Скорость перемещения камеры при подведении курсора к краю своего экрана, в метрах в секунду."},
            {"settings-camera-arrows","Скорость перемещения камеры стрелками клавиатуры, в метрах в секунду."},
            {"settings-pad-mapCursorSpeed","Скорость курсора на открытой карте от левого стика геймпада."},
            {"settings-pad-cameraSpeed","Скорость перемещения игровой камеры от левого стика геймпада."},
            {"settings-deadzone","Размер центральной зоны стиков, в которой отклонение игнорируется. Больше — меньше дрейф, но позже начинается движение."},
            {"settings-invert-x","Меняет направление горизонтального перемещения камеры от стика на противоположное."},
            {"settings-invert-y","Меняет направление вертикального перемещения камеры от стика на противоположное."},
            {"settings-health","Показывает полоски здоровья постоянно, а не только по обычным правилам выделения и повреждений. Учитывает скрытие при отдалении."},
            {"settings-hide-distant-health","Убирает полоски здоровья при далёком обзоре, уменьшая визуальный шум и работу интерфейса. При приближении полоски возвращаются; HP не меняется."},
            {"settings-contrast","Усиливает контраст поддерживаемых элементов интерфейса для лучшей читаемости. Не меняет команды и правила матча."},
            {"settings-show-fps","Показывает текущую частоту отрисовки. Помогает сравнивать настройки, но не показывает скорость симуляции в TPS."},
            {"settings-show-tps","Показывает фактические тики симуляции в секунду. На обычной скорости ожидаемый темп — 30 TPS."},
            {"settings-ui-scale","Масштаб элементов интерфейса. Больше — легче читать, но меньше свободного места для меню; разрешение 3D-сцены не меняется."}
        };
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
            if(group=="Видео")
            {
                foreach(var key in NativeGraphicsSettings.PreferenceKeys)PlayerPrefs.DeleteKey(key);
                keys=new[]{"spacewars.display.width","spacewars.display.height","spacewars.display.fullscreen","spacewars.display.vsync",NativeDevicePreferences.Prefix+"frameLimit","spacewars.display.shadows"};
            }
            if(group=="Звук")keys=new[]{"spacewars.ui.volume","spacewars.audio.music","spacewars.audio.effects","spacewars.audio.notifications","spacewars.audio.voice","spacewars.audio.voiceEnabled","spacewars.audio.unitVoice","spacewars.audio.unitVoiceEnabled"};
            if(group=="Управление")keys=new[]{"spacewars.controls.cursorSpeed","spacewars.controls.cameraSpeed","spacewars.controls.cursorRecenterIdleMs",NativeCameraScrollSettings.EdgeKey,NativeCameraScrollSettings.ArrowKey,NativeDevicePreferences.Prefix+"deadzone",NativeDevicePreferences.Prefix+"invertX",NativeDevicePreferences.Prefix+"invertY"};
            if(group=="Интерфейс")keys=new[]{"spacewars.ui.alwaysHealth",NativeUserSettings.HideDistantHealthKey,NativeDevicePreferences.Prefix+"uiScale",NativeDevicePreferences.Prefix+"highContrast",NativeDevicePreferences.Prefix+"showFps",NativeDevicePreferences.Prefix+"showTps"};
            foreach(var key in keys)PlayerPrefs.DeleteKey(key);
        }
        private void Confirm(string message,Action yes,Action no,int seconds=0)
        {
            var focus=root.focusController?.focusedElement as VisualElement;
            var overlay=OrbitalTheme.Screen(root,"settings-confirm");var box=OrbitalTheme.Card(overlay);box.Add(OrbitalTheme.Text(message,"orbital-muted"));
            bool finished=false;Action<bool> finish=accept=>{if(finished)return;finished=true;overlay.RemoveFromHierarchy();nav.SetScope(Page,Back,tabs[section]);if(accept)yes();else no();if(nav.Scope==Page)nav.SetScope(Page,Back,focus!=null?Page.Q(focus.name)??tabs[section]:tabs[section]);};
            var cancel=MenuAction("Отменить",()=>finish(false),"settings-confirm-cancel");box.Add(cancel);box.Add(MenuAction("Подтвердить",()=>finish(true),"settings-confirm-accept"));nav.SetScope(overlay,()=>finish(false),cancel);
            if(seconds>0){float deadline=Time.realtimeSinceStartup+seconds;overlay.schedule.Execute(()=>{if(Time.realtimeSinceStartup>=deadline)finish(false);}).Every(100);}
            Page.RegisterCallback<DetachFromPanelEvent>(_=>{if(!finished)finish(false);});
        }
    }
}
