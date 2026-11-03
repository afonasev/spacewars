using System;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    internal sealed class OrbitalCardFinish:VisualElement
    {
        private bool focused;
        internal bool Focused{set{if(focused==value)return;focused=value;MarkDirtyRepaint();}}
        internal OrbitalCardFinish(){pickingMode=PickingMode.Ignore;style.position=Position.Absolute;style.left=style.right=style.top=style.bottom=0;generateVisualContent+=Draw;}
        private void Draw(MeshGenerationContext ctx)
        {
            var p=ctx.painter2D;var profile=RadialCardProfile.Load();float w=contentRect.width,h=contentRect.height;
            if(w<12||h<12)return;
            void Shape(float inset){float x=inset,y=inset,right=w-inset,bottom=h-inset,r=8;p.BeginPath();p.MoveTo(new Vector2(x+r,y));p.LineTo(new Vector2(right-r,y));p.Arc(new Vector2(right-r,y+r),r,270,360);p.LineTo(new Vector2(right,bottom-r));p.Arc(new Vector2(right-r,bottom-r),r,0,90);p.LineTo(new Vector2(x+r,bottom));p.Arc(new Vector2(x+r,bottom-r),r,90,180);p.LineTo(new Vector2(x,y+r));p.Arc(new Vector2(x+r,y+r),r,180,270);p.ClosePath();}
            var body=Color.Lerp(OrbitalTheme.Panel,OrbitalTheme.Line,profile.opticalSurfaceLift);
            if(focused)body=Color.Lerp(body,OrbitalTheme.Cyan,profile.opticalSelectionTint);
            p.fillColor=body;Shape(1);p.Fill();
            // Optical sheen follows the existing radial-card material, beneath content.
            for(int i=0;i<64;i++){float t=1-i/64f;var sheen=OrbitalTheme.Ink;sheen.a=profile.opticalSheen*t*t;p.fillColor=sheen;p.BeginPath();p.MoveTo(new Vector2(7,7+i*(h-14)/64));p.LineTo(new Vector2(w-7,7+i*(h-14)/64));p.LineTo(new Vector2(w-7,7+(i+1)*(h-14)/64));p.LineTo(new Vector2(7,7+(i+1)*(h-14)/64));p.ClosePath();p.Fill();}
            if(focused){var glow=OrbitalTheme.Cyan;glow.a=profile.opticalGlowOpacity;p.strokeColor=glow;p.lineWidth=profile.opticalGlowWidth;Shape(2);p.Stroke();}
            p.strokeColor=focused?OrbitalTheme.Cyan:OrbitalTheme.Line;p.lineWidth=focused?2:1;Shape(2);p.Stroke();
            var rim=Color.Lerp(OrbitalTheme.Line,OrbitalTheme.Ink,profile.opticalEdgeLight);p.strokeColor=rim;p.lineWidth=1;Shape(5);p.Stroke();
            p.BeginPath();p.MoveTo(new Vector2(16,6));p.LineTo(new Vector2(w-16,6));p.Stroke();
            var quiet=OrbitalTheme.Line;quiet.a=.5f;p.strokeColor=quiet;p.BeginPath();p.Arc(new Vector2(w/2,h*.42f),Mathf.Min(w*.44f,h*.38f),205,335);p.Stroke();
        }
    }
    internal static class OrbitalChoiceCard
    {
        internal static VisualElement Create(int id,PlayableProfileData data,Action choose)
        {
            var art=RadialCardProfile.Load();var column=new VisualElement{name="orbital-choice-"+id};column.style.marginLeft=column.style.marginRight=8;column.style.flexShrink=0;
            var button=new Button(choose){name="orbital-card-"+id};button.style.position=Position.Relative;button.style.paddingLeft=button.style.paddingRight=art.padding;button.style.paddingTop=art.padding;button.style.paddingBottom=art.padding;
            button.style.marginLeft=button.style.marginRight=button.style.marginTop=button.style.marginBottom=0;button.style.minHeight=0;button.style.flexShrink=0;
            button.style.backgroundImage=StyleKeyword.None;button.style.backgroundColor=Color.clear;button.style.borderLeftWidth=button.style.borderRightWidth=button.style.borderTopWidth=button.style.borderBottomWidth=0;
            button.Add(new OrbitalCardFinish());
            var artwork=new OrbitalPlaceholderImage(id){name="orbital-choice-artwork",pickingMode=PickingMode.Ignore};artwork.style.alignSelf=Align.Center;artwork.style.flexGrow=1;artwork.style.flexShrink=1;button.Add(artwork);
            var header=new VisualElement{name="orbital-choice-header",pickingMode=PickingMode.Ignore};header.style.flexDirection=FlexDirection.Row;header.style.alignItems=Align.Center;header.style.backgroundColor=OrbitalTheme.Panel;header.style.paddingLeft=header.style.paddingRight=art.statColumnPadding;header.style.paddingTop=header.style.paddingBottom=art.statColumnPadding;
            var accent=new VisualElement{name="orbital-choice-accent",pickingMode=PickingMode.Ignore};accent.style.width=art.titleAccentWidth;accent.style.height=art.titleSize;accent.style.backgroundColor=OrbitalTheme.Cyan;accent.style.marginRight=art.statColumnPadding;header.Add(accent);
            var title=new Label(OrbitalAbilities.Name(id)){name="orbital-choice-title",pickingMode=PickingMode.Ignore};title.style.color=OrbitalTheme.Ink;title.style.unityFontStyleAndWeight=FontStyle.Bold;title.style.whiteSpace=WhiteSpace.Normal;title.style.flexShrink=1;title.style.marginLeft=title.style.marginRight=0;header.Add(title);button.Add(header);column.Add(button);
            var desc=new Label(id==OrbitalAbilities.SystemFailure?"Отключает два ближайших больших здания и все башни врага в области текущего обзора. Очереди сохраняются, действия недоступны.":id==OrbitalAbilities.ControlHack?"Навсегда захватывает видимую шахту или юнита врага. Сохраняет долю HP с вашими улучшениями.":id==1?"Открывает область вам и союзникам, в том числе в тумане войны.":id==2?"Даёт обзор и взрывается при наезде врага. Площадной урон подрывает мину.":"Эффект способности пока не задан."){name="orbital-choice-description",pickingMode=PickingMode.Ignore};desc.style.whiteSpace=WhiteSpace.Normal;desc.style.color=OrbitalTheme.Ink;desc.style.marginTop=art.padding;desc.style.marginBottom=art.statColumnPadding;desc.style.marginLeft=desc.style.marginRight=0;column.Add(desc);
            var stats=new VisualElement{name="orbital-choice-stats",pickingMode=PickingMode.Ignore};stats.style.flexDirection=FlexDirection.Row;stats.style.borderTopWidth=1;stats.style.borderTopColor=OrbitalTheme.Line;stats.style.paddingTop=art.notePadding;column.Add(stats);if(id==2)stats.style.flexWrap=Wrap.Wrap;
            void Stat(string label,string value){var cell=new VisualElement{pickingMode=PickingMode.Ignore};cell.style.flexGrow=id==2?0:1;cell.style.flexBasis=id==2?Length.Percent(50):new Length(0);if(id==2){cell.style.flexDirection=FlexDirection.Row;cell.style.alignItems=Align.Center;cell.style.paddingRight=art.statColumnPadding;}var key=new Label(label){pickingMode=PickingMode.Ignore};key.style.color=OrbitalTheme.Muted;key.style.fontSize=art.textSize;if(id==2){key.style.flexGrow=1;key.style.marginLeft=key.style.marginRight=key.style.marginTop=key.style.marginBottom=0;}cell.Add(key);var number=new Label(value){pickingMode=PickingMode.Ignore};number.style.color=OrbitalTheme.Ink;number.style.fontSize=id==2?art.textSize:art.valueSize;if(id==2){number.style.marginLeft=art.labelGap;number.style.marginRight=number.style.marginTop=number.style.marginBottom=0;}number.style.unityFontStyleAndWeight=FontStyle.Bold;cell.Add(number);stats.Add(cell);}
            if(id==2){Stat("Обзор",data.orbitalMineVision.ToString("0.#"));Stat("Урон",data.orbitalMineDirectDamage+"/"+data.orbitalMineSplashDamage);Stat("Радиус",data.orbitalMineBlastRadius.ToString("0.#"));Stat("Лимит",data.orbitalMineCapacity.ToString("0"));}
            if(id==OrbitalAbilities.SystemFailure){Stat("Радиус",data.orbitalSystemFailureRadius.ToString("0.#")+" м");Stat("Длительность",data.orbitalSystemFailureSeconds.ToString("0.#")+" с");}
            else if(id==1){Stat("Радиус",data.orbitalScanRadius.ToString("0.#"));Stat("Длительность",data.orbitalScanSeconds.ToString("0.#")+" с");}
            else if(id==OrbitalAbilities.ControlHack){Stat("HP цели","< "+data.orbitalHackHealthPercent.ToString("0.#")+"%");Stat("Откат",OrbitalAbilities.Duration(id,data).ToString("0.#")+" с");}
            else Stat(id==2?"Откат":"Перезарядка",OrbitalAbilities.Duration(id,data).ToString("0.#")+" с");
            return column;
        }
    }
}
