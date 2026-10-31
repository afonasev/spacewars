using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // A graphical button badge; south is contextual confirmation, never another shortcut.
    public sealed class NativeControllerGlyph : VisualElement
    {
        private readonly string button;
        private readonly Func<Gamepad> device;
        private readonly bool contextual;
        private readonly Label symbol;
        private Color tint;
        public NativeControllerGlyph(string button,Func<Gamepad> device=null,bool contextual=true)
        {
            this.button=button;this.device=device??(()=>Gamepad.current);this.contextual=contextual;pickingMode=PickingMode.Ignore;
            style.height=28;style.width=style.minWidth=button.Length>2?56:28;style.marginRight=9;
            // A Label's default padding makes its glyph look offset in a circular badge.
            // Give the symbol the exact badge rectangle so both axes share one centre.
            symbol=new Label(button){pickingMode=PickingMode.Ignore,name="controller-glyph-symbol"};
            symbol.style.position=Position.Absolute;symbol.style.left=0;symbol.style.right=0;symbol.style.top=0;symbol.style.bottom=0;
            symbol.style.marginLeft=symbol.style.marginRight=symbol.style.marginTop=symbol.style.marginBottom=0;
            symbol.style.paddingLeft=symbol.style.paddingRight=symbol.style.paddingTop=symbol.style.paddingBottom=0;
            symbol.style.unityTextAlign=TextAnchor.MiddleCenter;symbol.style.fontSize=button.Length>1?RadialCardProfile.Load().textSize:18;symbol.style.unityFontStyleAndWeight=FontStyle.Bold;Add(symbol);
            generateVisualContent+=ctx=>{var p=ctx.painter2D;p.fillColor=new Color(.06f,.08f,.11f);p.strokeColor=tint;p.lineWidth=1.5f;p.BeginPath();if(button.Length>2){p.MoveTo(new Vector2(2,3));p.LineTo(new Vector2(54,3));p.LineTo(new Vector2(54,25));p.LineTo(new Vector2(2,25));}else p.Arc(new Vector2(14,14),12,0,360);p.ClosePath();p.Fill();p.Stroke();};
            schedule.Execute(()=>Refresh(this.device())).Every(80);Refresh(this.device());
        }
        private Button attachedButton;
        private const float CaptionGap=6;
        public void AttachToButton(Button target)
        {
            attachedButton=target;
            bool inline=string.IsNullOrEmpty(target.text);
            target.style.paddingLeft=12;target.style.paddingRight=inline?12:12+(button.Length>2?56:28)+CaptionGap;
            target.style.flexDirection=FlexDirection.Row;target.style.alignItems=Align.Center;target.style.justifyContent=Justify.Center;
            style.marginRight=0;style.flexShrink=0;
            if(inline){style.position=Position.Relative;style.marginLeft=CaptionGap;style.marginTop=0;}
            else{style.position=Position.Absolute;style.left=Length.Percent(50);style.top=Length.Percent(50);style.marginTop=-14;}
            target.Add(this);PlaceAfterCaption();
        }
        private void PlaceAfterCaption()
        {
            if(attachedButton==null||string.IsNullOrEmpty(attachedButton.text))return;
            // Button.text is rendered by the button itself. Keep its public text and
            // activation/accessibility intact while centring the caption + badge pair.
            float textWidth=attachedButton.MeasureTextSize(attachedButton.text,0,MeasureMode.Undefined,0,MeasureMode.Undefined).x;
            style.marginLeft=(textWidth-(button.Length>2?56:28)+CaptionGap)/2;
        }
        private bool? menuVisible;
        public void SetMenuVisibility(bool visible,Gamepad pad){menuVisible=visible;Refresh(pad);}
        public static string TopSymbol(Gamepad pad)=>Symbol("Y",pad);
        public static string Symbol(string button,Gamepad pad)
        {
            bool sony=pad!=null&&(pad.layout.IndexOf("Dual",StringComparison.OrdinalIgnoreCase)>=0||pad.description.manufacturer?.IndexOf("Sony",StringComparison.OrdinalIgnoreCase)>=0);
            bool nintendo=pad!=null&&(pad.layout.IndexOf("Switch",StringComparison.OrdinalIgnoreCase)>=0||pad.description.manufacturer?.IndexOf("Nintendo",StringComparison.OrdinalIgnoreCase)>=0);
            if(button=="RB"||button=="LB"||button=="RT"||button=="LT")return sony?(button=="RB"?"R1":button=="LB"?"L1":button=="RT"?"R2":"L2"):nintendo?(button=="RB"?"R":button=="LB"?"L":button=="RT"?"ZR":"ZL"):button;
            if(button!="A"&&button!="B"&&button!="X"&&button!="Y")return button;
            return sony?(button=="A"?"✕":button=="B"?"○":button=="X"?"□":"△"):nintendo?(button=="A"?"B":button=="B"?"A":button=="X"?"Y":"X"):button;
        }
        public void Refresh(Gamepad pad)
        {
            symbol.text=Symbol(button,pad);PlaceAfterCaption();
            tint=button=="A"?new Color(.56f,.84f,.48f):button=="B"?new Color(.97f,.45f,.54f):button=="X"?new Color(.40f,.68f,1f):button=="Y"?new Color(1f,.80f,.35f):OrbitalTheme.Ink;
            symbol.style.color=tint;
            if(contextual&&button=="A"||menuVisible==false)style.display=DisplayStyle.None;else style.display=DisplayStyle.Flex;
            MarkDirtyRepaint();
        }
    }
}
