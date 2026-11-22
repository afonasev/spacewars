using System;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public static class OrbitalTheme
    {
        public static readonly Color Background = new Color(.035f,.05f,.065f);
        public static readonly Color Panel = new Color(.035f,.065f,.083f,.97f);
        public static readonly Color Ink = new Color(.89f,.94f,.97f);
        public static readonly Color Muted = new Color(.53f,.64f,.70f);
        public static readonly Color Cyan = new Color(.27f,.85f,.94f);
        public static readonly Color Amber = new Color(.93f,.73f,.38f);
        public static readonly Color RepeatFill = new Color(.235f,.173f,.098f,.98f);
        public static readonly Color HoldFill = new Color(.31f,.225f,.115f,.98f);
        public static readonly Color Line = new Color(.23f,.33f,.39f);
        // One scale authority for installed menu routes and HUD, including Retina backing pixels.
        public static float ReadableScale(int width,int height)=>Mathf.Max(1f,Mathf.Min(width/1280f,height/800f));
        private const float DeviceFpsWidth=64, DeviceTpsWidth=88;
        internal static float SplitDiagnosticsInset()=>
            ((NativeDevicePreferences.ShowFps?DeviceFpsWidth:0)+(NativeDevicePreferences.ShowTps?DeviceTpsWidth:0))/2;
        private static Label DeviceCounter(VisualElement root,string name,float width)
        {
            var label=new Label{name=name,pickingMode=PickingMode.Ignore};
            label.style.position=Position.Absolute;
            label.style.display=DisplayStyle.None;
            label.style.marginLeft=label.style.marginRight=label.style.marginTop=label.style.marginBottom=0;
            label.style.paddingLeft=label.style.paddingRight=0;
            label.style.width=width;
            label.style.whiteSpace=WhiteSpace.NoWrap;
            label.style.overflow=Overflow.Hidden;
            label.style.color=Color.white;
            label.style.fontSize=12;
            label.style.textShadow=new TextShadow{color=Color.black,offset=new Vector2(0,1),blurRadius=2};
            root.Add(label);
            return label;
        }
        public static void ConfigurePanel(PanelSettings panel,VisualElement root,
            Func<(PlayableSnapshot snapshot,double target)> tickSource=null)
        {
            panel.scaleMode=PanelScaleMode.ConstantPixelSize;
            var fps=DeviceCounter(root,"device-fps",DeviceFpsWidth);
            var tps=DeviceCounter(root,"device-tps",DeviceTpsWidth);
            void Resize()
            {
                var target=panel.targetTexture;
                float scale=ReadableScale(target!=null?target.width:UnityEngine.Screen.width,target!=null?target.height:UnityEngine.Screen.height);
                scale*=NativeDevicePreferences.UiScale;root.EnableInClassList("high-contrast",NativeDevicePreferences.HighContrast);
                if(!Mathf.Approximately(panel.scale,scale))panel.scale=scale;
                LayoutDeviceDiagnostics(root);
            }
            double lastFrameAt=Time.realtimeSinceStartupAsDouble;
            int frames=Time.frameCount;
            double sampleAt=0,sampleTarget=0;
            long sampleTick=0,sampleGeneration=0;
            void ReadCounters()
            {
                double now=Time.realtimeSinceStartupAsDouble;
                fps.text=Mathf.RoundToInt((float)((Time.frameCount-frames)/Math.Max(.001,now-lastFrameAt)))+" FPS";
                lastFrameAt=now;
                frames=Time.frameCount;
                var (snapshot,target)=NativeDevicePreferences.ShowTps&&tickSource!=null?tickSource():default;
                if(snapshot==null||snapshot.Outcome!=PlayableMatchOutcome.Playing||
                    (snapshot.Status!=RuntimeStatus.Running&&snapshot.Status!=RuntimeStatus.Paused))
                {
                    tps.text="";
                    sampleTarget=0;
                }
                else if(snapshot.Paused||snapshot.Status==RuntimeStatus.Paused)
                {
                    tps.text="TPS: пауза";
                    sampleTarget=0;
                }
                else if(sampleTarget!=target||sampleGeneration!=snapshot.Generation||snapshot.Tick<sampleTick)
                {
                    sampleAt=now;
                    sampleTick=snapshot.Tick;
                    sampleTarget=target;
                    sampleGeneration=snapshot.Generation;
                    tps.text="— TPS";
                }
                else if(now-sampleAt>=1)
                {
                    double measured=(snapshot.Tick-sampleTick)/(now-sampleAt);
                    tps.text=Mathf.RoundToInt((float)measured)+" TPS";
                    sampleAt=now;
                    sampleTick=snapshot.Tick;
                }
                LayoutDeviceDiagnostics(root);
                if(!root.ClassListContains("local-split-screen")){
                    fps.BringToFront();
                    tps.BringToFront();
                }
            }
            root.schedule.Execute(ReadCounters).Every(500);
            fps.RegisterCallback<GeometryChangedEvent>(_=>LayoutDeviceDiagnostics(root));
            tps.RegisterCallback<GeometryChangedEvent>(_=>LayoutDeviceDiagnostics(root));
            Resize();root.RegisterCallback<GeometryChangedEvent>(_=>Resize());root.schedule.Execute(Resize).Every(100);
        }
        public static void LayoutDeviceDiagnostics(VisualElement root)=>LayoutDeviceDiagnostics(root,null,null);
        internal static void LayoutDeviceDiagnostics(VisualElement root,VisualElement divider,VisualElement map)
        {
            if(root==null)return;
            var fps=root.Q<Label>("device-fps");
            var tps=root.Q<Label>("device-tps");
            if(fps==null||tps==null){divider?.BringToFront();map?.BringToFront();return;}
            bool showFps=NativeDevicePreferences.ShowFps;
            bool showTps=NativeDevicePreferences.ShowTps&&!string.IsNullOrEmpty(tps.text);
            fps.style.display=showFps?DisplayStyle.Flex:DisplayStyle.None;
            tps.style.display=showTps?DisplayStyle.Flex:DisplayStyle.None;
            float fpsWidth=showFps?DeviceFpsWidth:0;
            float countersWidth=fpsWidth+(showTps?DeviceTpsWidth:0);
            const float gutter=8;
            bool split=root.ClassListContains("local-split-screen");
            void Place(Label label,float offset)
            {
                label.style.left=split?Length.Percent(50):new Length(gutter+offset);
                var flag=root.Q("domination-flag");
                float flagClearance=flag!=null&&flag.style.display.value!=DisplayStyle.None?35:0;
                label.style.marginLeft=split?offset-(flagClearance>0?countersWidth+flagClearance:countersWidth/2):0;
                label.style.top=gutter;
                label.style.backgroundColor=split?new Color(Panel.r,Panel.g,Panel.b,1):Color.clear;
                label.style.unityTextAlign=split?TextAnchor.MiddleCenter:TextAnchor.UpperLeft;
            }
            Place(fps,0);
            Place(tps,fpsWidth);
            if(split){
                // Keep the same overlay order without removing/reinserting
                // the same elements on each rendered frame.
                int last=root.childCount-1;
                bool ordered=root[last]==tps&&root[last-1]==fps;
                if(ordered&&map!=null)ordered=root[last-2]==map;
                if(ordered&&divider!=null)ordered=root[last-(map!=null?3:2)]==divider;
                if(!ordered){divider?.BringToFront();map?.BringToFront();fps.BringToFront();tps.BringToFront();}
                return;
            }
            var table=root.Q("spectator-summary");if(table==null||table.resolvedStyle.display==DisplayStyle.None||table.parent==null)return;
            float width=table.parent.resolvedStyle.width;
            var flagOverlay=root.Q("domination-flag");
            table.style.left=gutter;
            table.style.top=flagOverlay!=null&&flagOverlay.style.display.value!=DisplayStyle.None
                ?flagOverlay.style.height.value.value+gutter:gutter;
            var resources=root.Q("match-header");
            float reserved=resources!=null&&resources.resolvedStyle.display!=DisplayStyle.None?Mathf.Max(0,resources.resolvedStyle.width)+gutter:0;
            if(width>0)table.style.maxWidth=Mathf.Max(220,width-2*gutter-reserved);
            var controls=table.Q("spectator-controls");if(controls==null)return;
            float counterLeft=root.WorldToLocal(controls.worldBound.min).x+gutter;
            float centerY=root.WorldToLocal(controls.worldBound.center).y;
            float rightPadding=gutter;
            if(resources!=null&&resources.resolvedStyle.display!=DisplayStyle.None&&resources.worldBound.height>0
                &&resources.worldBound.Overlaps(controls.worldBound))
                rightPadding=Mathf.Max(gutter,controls.worldBound.xMax-resources.worldBound.xMin+gutter);
            bool stacked=false;
            if(showFps&&showTps)
            {
                float controlsWidth=0;
                foreach(var child in controls.Children())
                    if(child is Button)controlsWidth+=child.style.width.value.value+child.style.marginRight.value.value;
                stacked=controls.worldBound.width<controlsWidth+countersWidth+2*gutter+rightPadding;
            }
            fps.style.left=counterLeft;
            tps.style.left=counterLeft+(stacked?0:fpsWidth);
            if(controls.worldBound.height>0)
            {
                if(showFps)fps.style.top=centerY-fps.resolvedStyle.height*(stacked?1:.5f);
                if(showTps)tps.style.top=centerY-(stacked?0:tps.resolvedStyle.height*.5f);
            }
            float diagnosticWidth=stacked?DeviceTpsWidth:countersWidth;
            controls.style.paddingLeft=diagnosticWidth>0?diagnosticWidth+2*gutter:gutter;
            controls.style.paddingRight=rightPadding;
            if(resources!=null&&resources.resolvedStyle.display!=DisplayStyle.None)resources.BringToFront();
        }
        public static void Install(VisualElement root)
        {
            var sheet=Resources.Load<StyleSheet>("OrbitalCommand");
            if(sheet!=null&&!root.styleSheets.Contains(sheet))root.styleSheets.Add(sheet);
            // Dropdown popups are siblings of UIDocument roots, so their sheet belongs to the panel.
            void InstallPanelStyle(IPanel panel)
            {
                var popupSheet=Resources.Load<StyleSheet>("NativeLobby");
                if(panel!=null&&popupSheet!=null&&!panel.visualTree.styleSheets.Contains(popupSheet))panel.visualTree.styleSheets.Add(popupSheet);
            }
            InstallPanelStyle(root.panel);
            root.RegisterCallback<AttachToPanelEvent>(evt=>InstallPanelStyle(evt.destinationPanel));
            root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            root.style.color=Ink;root.AddToClassList("orbital-root");
        }
        // Keep wheel/touch/focus scrolling available without visible scrollbar controls.
        // The laboratory creates ordinary ScrollViews and retains its scrollbars.
        public static ScrollView Scroll(ScrollViewMode mode=ScrollViewMode.Vertical)=>new ScrollView(mode)
        {horizontalScrollerVisibility=ScrollerVisibility.Hidden,verticalScrollerVisibility=ScrollerVisibility.Hidden};
        public static VisualElement Screen(VisualElement root,string name,bool artwork=false)
        {
            Install(root);var screen=new VisualElement{name=name};screen.AddToClassList("orbital-screen");
            if(artwork){screen.style.backgroundImage=Resources.Load<Texture2D>("OrbitalBackdrop");screen.AddToClassList("orbital-art");}
            root.Add(screen);Footer(screen);return screen;
        }
        public const string Copyright = "© 2026 Evgeniy Afonasev · afonasev.tech · Made with Codex";
        [Serializable] private sealed class ReleaseInfo { public string date; }
        public static string FooterText
        {
            get
            {
                var metadata=Resources.Load<TextAsset>("NativeReleaseInfo");
                var date=metadata==null?null:JsonUtility.FromJson<ReleaseInfo>(metadata.text)?.date;
                return FormatFooter(Application.version,date);
            }
        }
        public static string FormatFooter(string version,string date)
        {
            var info="v"+version;
            if(DateTime.TryParseExact(date,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var released))
                info+=" · "+released.ToString("dd.MM.yyyy",System.Globalization.CultureInfo.InvariantCulture);
            return info+" · "+Copyright;
        }
        public static Label Footer(VisualElement parent,bool inline=false)
        {
            var footer=Text(FooterText,"orbital-copyright");footer.name="menu-copyright";
            footer.pickingMode=PickingMode.Ignore;
            if(inline){footer.style.position=Position.Relative;footer.style.left=footer.style.right=StyleKeyword.Auto;footer.style.bottom=StyleKeyword.Auto;footer.style.marginTop=12;}
            parent.Add(footer);return footer;
        }
        public static VisualElement Loading(VisualElement root,string status)=>OrbitalLoadingView.Create(root,status);
        public static VisualElement Card(VisualElement parent,string name=null)
        {var card=new VisualElement{name=name};card.AddToClassList("orbital-card");parent.Add(card);return card;}
        public static Label Text(string text,string style="orbital-body")
        {var label=new Label(text);label.AddToClassList(style);return label;}
        public static Button Action(string text,Action action,string name=null,bool primary=false)
        {
            var button=new Button(action){text=text,name=name};StyleButton(button);
            if(primary)button.AddToClassList("orbital-primary");return button;
        }
        public static void StyleButton(Button button)
        {
            if(button.ClassListContains("orbital-button"))return;
            button.AddToClassList("orbital-button");
            button.clicked+=GameplayAudioPlayer.Ui;
            button.RegisterCallback<FocusInEvent>(_=>Focus(button,true));
            button.RegisterCallback<FocusOutEvent>(_=>Focus(button,false));
            button.RegisterCallback<PointerEnterEvent>(_=>Focus(button,true));
            button.RegisterCallback<PointerLeaveEvent>(_=>Focus(button,button.focusController?.focusedElement==button));
        }
        private static void Focus(VisualElement element,bool focused)
        {element.EnableInClassList("orbital-focused",focused);}
    }
}
