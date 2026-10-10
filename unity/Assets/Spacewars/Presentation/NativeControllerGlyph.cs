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
            style.width=style.height=28;style.minWidth=28;style.marginRight=9;
            // A Label's default padding makes its glyph look offset in a circular badge.
            // Give the symbol the exact badge rectangle so both axes share one centre.
            symbol=new Label(button){pickingMode=PickingMode.Ignore,name="controller-glyph-symbol"};
            symbol.style.position=Position.Absolute;symbol.style.left=0;symbol.style.right=0;symbol.style.top=0;symbol.style.bottom=0;
            symbol.style.marginLeft=symbol.style.marginRight=symbol.style.marginTop=symbol.style.marginBottom=0;
            symbol.style.paddingLeft=symbol.style.paddingRight=symbol.style.paddingTop=symbol.style.paddingBottom=0;
            symbol.style.unityTextAlign=TextAnchor.MiddleCenter;symbol.style.fontSize=18;symbol.style.unityFontStyleAndWeight=FontStyle.Bold;Add(symbol);
            generateVisualContent+=ctx=>{var p=ctx.painter2D;p.fillColor=new Color(.06f,.08f,.11f);p.strokeColor=tint;p.lineWidth=1.5f;p.BeginPath();p.Arc(new Vector2(14,14),12,0,360);p.ClosePath();p.Fill();p.Stroke();};
            schedule.Execute(()=>Refresh(this.device())).Every(80);Refresh(this.device());
        }
        public static string Symbol(string button,Gamepad pad)
        {
            bool sony=pad!=null&&(pad.layout.IndexOf("Dual",StringComparison.OrdinalIgnoreCase)>=0||pad.description.manufacturer?.IndexOf("Sony",StringComparison.OrdinalIgnoreCase)>=0);
            bool nintendo=pad!=null&&(pad.layout.IndexOf("Switch",StringComparison.OrdinalIgnoreCase)>=0||pad.description.manufacturer?.IndexOf("Nintendo",StringComparison.OrdinalIgnoreCase)>=0);
            return sony?(button=="A"?"✕":button=="B"?"○":button=="X"?"□":"△"):nintendo?(button=="A"?"B":button=="B"?"A":button=="X"?"Y":"X"):button;
        }
        public void Refresh(Gamepad pad)
        {
            symbol.text=Symbol(button,pad);
            tint=button=="A"?new Color(.56f,.84f,.48f):button=="B"?new Color(.97f,.45f,.54f):button=="X"?new Color(.40f,.68f,1f):new Color(1f,.80f,.35f);
            symbol.style.color=tint;
            if(contextual&&button=="A")style.display=parent!=null&&(parent.focusController?.focusedElement==parent)?DisplayStyle.Flex:DisplayStyle.None;
            MarkDirtyRepaint();
        }
    }
}
