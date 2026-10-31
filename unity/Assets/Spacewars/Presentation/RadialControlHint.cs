using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // Shared artwork and typography for building cards, auxiliary cards and signals.
    public sealed class RadialControlHint : VisualElement
    {
        public RadialControlHint(string button,string caption,bool controller,Gamepad pad,bool hold=false,bool bold=false)
        {
            var profile=RadialCardProfile.Load();pickingMode=PickingMode.Ignore;
            name=hold?"radial-hold-action":null;style.flexDirection=FlexDirection.Row;style.alignItems=Align.Center;style.minHeight=profile.actionHeight;style.flexShrink=1;
            var badge=new VisualElement{name=hold?"radial-hold-glyph":button=="LS"?"radial-stick-glyph":null,pickingMode=PickingMode.Ignore};
            // Fixed badge packing matches the established 28px controller artwork + 3px inset.
            badge.style.width=badge.style.minWidth=badge.style.height=34;badge.style.marginRight=8;Add(badge);
            if(controller&&button!="LS")
            {
                var glyph=new NativeControllerGlyph(button,()=>pad,false);glyph.style.position=Position.Absolute;glyph.style.left=glyph.style.top=3;glyph.style.marginRight=0;badge.Add(glyph);
                if(hold)badge.generateVisualContent+=ctx=>{var p=ctx.painter2D;p.strokeColor=OrbitalTheme.Amber;p.lineWidth=1.5f;p.BeginPath();p.Arc(new Vector2(17,17),16,-70,230);p.Stroke();};
            }
            else
            {
                var key=Label("radial-key",button,profile.textSize);key.style.unityFontStyleAndWeight=FontStyle.Bold;key.style.unityTextAlign=TextAnchor.MiddleCenter;key.style.position=Position.Absolute;key.style.left=key.style.right=key.style.top=key.style.bottom=0;badge.Add(key);
                if(button=="LS")badge.generateVisualContent+=DrawStick;else badge.style.width=badge.style.minWidth=button=="Мышь"?44:34;
            }
            var text=Label("radial-hint",caption,profile.textSize);text.style.flexShrink=1;text.style.minWidth=0;text.style.whiteSpace=WhiteSpace.Normal;text.style.unityFontStyleAndWeight=bold?FontStyle.Bold:FontStyle.Normal;Add(text);
        }
        static Label Label(string name,string value,float size)
        {
            var label=new Label(value){name=name,pickingMode=PickingMode.Ignore};label.style.fontSize=size;label.style.color=OrbitalTheme.Ink;
            label.style.marginLeft=label.style.marginRight=label.style.marginTop=label.style.marginBottom=0;label.style.paddingLeft=label.style.paddingRight=label.style.paddingTop=label.style.paddingBottom=0;return label;
        }
        static void DrawStick(MeshGenerationContext ctx)
        {
            var p=ctx.painter2D;p.strokeColor=OrbitalTheme.Ink;p.fillColor=OrbitalTheme.Ink;p.lineWidth=1.25f;p.BeginPath();p.Arc(new Vector2(17,17),11,0,360);p.Stroke();
            for(int i=0;i<4;i++){float a=i*Mathf.PI/2;var d=new Vector2(Mathf.Sin(a),-Mathf.Cos(a));var side=new Vector2(-d.y,d.x);var c=new Vector2(17,17);p.BeginPath();p.MoveTo(c+d*17);p.LineTo(c+d*13+side*3);p.LineTo(c+d*13-side*3);p.ClosePath();p.Fill();}
        }
    }
}
