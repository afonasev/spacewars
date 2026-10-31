using System;
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
        private const float DeviceFpsWidth=64;
        public static void ConfigurePanel(PanelSettings panel,VisualElement root)
        {
            panel.scaleMode=PanelScaleMode.ConstantPixelSize;
            // A fixed device overlay gutter; no HUD panel or gameplay tuning belongs to the FPS label.
            var fps=new Label(){name="device-fps",pickingMode=PickingMode.Ignore};
            fps.style.position=Position.Absolute;fps.style.left=8;fps.style.top=8;fps.style.display=NativeDevicePreferences.ShowFps?DisplayStyle.Flex:DisplayStyle.None;
            fps.style.marginLeft=fps.style.marginRight=fps.style.marginTop=fps.style.marginBottom=0;fps.style.paddingLeft=fps.style.paddingRight=0;
            fps.style.width=DeviceFpsWidth;fps.style.whiteSpace=WhiteSpace.NoWrap;fps.style.overflow=Overflow.Hidden;
            fps.style.color=Color.white;fps.style.fontSize=12;fps.style.unityTextAlign=TextAnchor.UpperLeft;
            fps.style.textShadow=new TextShadow{color=Color.black,offset=new Vector2(0,1),blurRadius=2};root.Add(fps);
            void Resize()
            {
                var target=panel.targetTexture;
                float scale=ReadableScale(target!=null?target.width:UnityEngine.Screen.width,target!=null?target.height:UnityEngine.Screen.height);
                scale*=NativeDevicePreferences.UiScale;root.EnableInClassList("high-contrast",NativeDevicePreferences.HighContrast);
                if(!Mathf.Approximately(panel.scale,scale))panel.scale=scale;
                LayoutDeviceDiagnostics(root);
            }
            float last=Time.realtimeSinceStartup;int frames=Time.frameCount;
            root.schedule.Execute(()=>{fps.style.display=NativeDevicePreferences.ShowFps?DisplayStyle.Flex:DisplayStyle.None;float now=Time.realtimeSinceStartup;fps.text=Mathf.RoundToInt((Time.frameCount-frames)/Mathf.Max(.001f,now-last))+" FPS";last=now;frames=Time.frameCount;fps.BringToFront();}).Every(500);
            fps.RegisterCallback<GeometryChangedEvent>(_=>LayoutDeviceDiagnostics(root));
            Resize();root.RegisterCallback<GeometryChangedEvent>(_=>Resize());root.schedule.Execute(Resize).Every(100);
        }
        public static void LayoutDeviceDiagnostics(VisualElement root)
        {
            if(root==null)return;
            var fps=root.Q<Label>("device-fps");if(fps==null)return;
            fps.style.display=NativeDevicePreferences.ShowFps?DisplayStyle.Flex:DisplayStyle.None;
            fps.style.left=8;fps.style.top=8;fps.style.marginLeft=0;
            fps.style.backgroundColor=Color.clear;fps.style.unityTextAlign=TextAnchor.UpperLeft;
            if(root.ClassListContains("local-split-screen"))
            {
                fps.style.left=Length.Percent(50);fps.style.marginLeft=-DeviceFpsWidth/2;
                fps.style.backgroundColor=new Color(Panel.r,Panel.g,Panel.b,1);fps.style.unityTextAlign=TextAnchor.MiddleCenter;
                fps.BringToFront();return;
            }
            var table=root.Q("spectator-summary");if(table==null||table.resolvedStyle.display==DisplayStyle.None||table.parent==null)return;
            const float gutter=8;
            var parent=table.parent;float width=parent.resolvedStyle.width;
            table.style.left=gutter;table.style.top=gutter;
            if(width>0)table.style.maxWidth=Mathf.Max(0,width-2*gutter);
            var controls=table.Q("spectator-controls");if(controls==null)return;
            fps.style.left=root.WorldToLocal(controls.worldBound.min).x+gutter;
            // Reserve the same inner gutter before FPS and between FPS and speeds.
            controls.style.paddingLeft=NativeDevicePreferences.ShowFps?DeviceFpsWidth+2*gutter:gutter;
            if(NativeDevicePreferences.ShowFps&&controls.worldBound.height>0)
                fps.style.top=root.WorldToLocal(controls.worldBound.center).y-fps.resolvedStyle.height/2;
            var resources=root.Q("match-header");float rightPadding=gutter;
            if(resources!=null&&resources.resolvedStyle.display!=DisplayStyle.None&&resources.worldBound.height>0
                &&resources.worldBound.Overlaps(controls.worldBound))
                rightPadding=Mathf.Max(gutter,controls.worldBound.xMax-resources.worldBound.xMin+gutter);
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
