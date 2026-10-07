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
            root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            root.style.color=Ink;root.AddToClassList("orbital-root");
        }
        public static VisualElement Screen(VisualElement root,string name,bool artwork=false)
        {
            Install(root);var screen=new VisualElement{name=name};screen.AddToClassList("orbital-screen");
            if(artwork){screen.style.backgroundImage=Resources.Load<Texture2D>("OrbitalBackdrop");screen.AddToClassList("orbital-art");}
            root.Add(screen);return screen;
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
            button.RegisterCallback<FocusInEvent>(_=>Focus(button,true));
            button.RegisterCallback<FocusOutEvent>(_=>Focus(button,false));
            button.RegisterCallback<PointerEnterEvent>(_=>Focus(button,true));
            button.RegisterCallback<PointerLeaveEvent>(_=>Focus(button,button.focusController?.focusedElement==button));
        }
        private static void Focus(VisualElement element,bool focused)
        {element.EnableInClassList("orbital-focused",focused);}
    }
}
