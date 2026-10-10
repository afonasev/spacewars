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
        public static readonly Color Line = new Color(.23f,.33f,.39f);
        // One scale authority for installed menu routes and HUD, including Retina backing pixels.
        public static float ReadableScale(int width,int height)=>Mathf.Max(1f,Mathf.Min(width/1280f,height/800f));
        public static void ConfigurePanel(PanelSettings panel,VisualElement root)
        {
            panel.scaleMode=PanelScaleMode.ConstantPixelSize;
            void Resize()
            {
                var target=panel.targetTexture;
                float scale=ReadableScale(target!=null?target.width:UnityEngine.Screen.width,target!=null?target.height:UnityEngine.Screen.height);
                if(!Mathf.Approximately(panel.scale,scale))panel.scale=scale;
            }
            Resize();root.RegisterCallback<GeometryChangedEvent>(_=>Resize());root.schedule.Execute(Resize).Every(100);
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
        public static Label Footer(VisualElement parent,bool inline=false)
        {
            var footer=Text(Copyright,"orbital-copyright");footer.name="menu-copyright";
            footer.pickingMode=PickingMode.Ignore;
            if(inline){footer.style.position=Position.Relative;footer.style.left=footer.style.right=StyleKeyword.Auto;footer.style.bottom=StyleKeyword.Auto;footer.style.marginTop=12;}
            parent.Add(footer);return footer;
        }
        public static VisualElement Loading(VisualElement root,string status)
        {
            var screen=Screen(root,"loading-screen",true);screen.AddToClassList("orbital-loading");
            var shade=new VisualElement{pickingMode=PickingMode.Ignore};shade.AddToClassList("orbital-loading-shade");screen.Insert(0,shade);
            var stack=new VisualElement();stack.AddToClassList("orbital-loading-stack");screen.Add(stack);
            stack.Add(Text("SPACEWARS","orbital-logo"));
            var label=Text(status,"orbital-loading-status");label.name="loading-status";stack.Add(label);
            var line=new VisualElement{pickingMode=PickingMode.Ignore};line.AddToClassList("orbital-loading-line");stack.Add(line);
            line.generateVisualContent+=ctx=>{
                float w=line.contentRect.width,h=line.contentRect.height;if(w<=0||h<=0)return;
                var painter=ctx.painter2D;painter.lineWidth=h;painter.strokeColor=Line;
                painter.BeginPath();painter.MoveTo(new Vector2(0,h/2));painter.LineTo(new Vector2(w,h/2));painter.Stroke();
                float phase=(Mathf.Sin(Time.realtimeSinceStartup*1.8f)+1)/2;
                float start=phase*w*.72f;painter.strokeColor=Cyan;
                painter.BeginPath();painter.MoveTo(new Vector2(start,h/2));painter.LineTo(new Vector2(start+w*.28f,h/2));painter.Stroke();
            };
            line.schedule.Execute(line.MarkDirtyRepaint).Every(33);return screen;
        }
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
