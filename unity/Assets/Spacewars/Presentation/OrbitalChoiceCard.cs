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
        internal static string Description(int id) => id switch
        {
            OrbitalAbilities.AcceleratedProduction => "Когда срочно нужны кредиты, техника или исследование, форсируйте работу своего или союзного здания. На пределе оно изнашивается, а ремонт останавливается.",
            OrbitalAbilities.NuclearStrike => "Запрашивайте удар по скоплению войск или укреплений. Держите цель в обзоре до спуска ракеты и выводите своих из зоны взрыва.",
            OrbitalAbilities.RepairNanobots => "Разверните ремонтное поле там, где подбитый отряд держит позицию. Нанороботы чинят союзную технику и готовые здания прямо под огнём.",
            OrbitalAbilities.Stasis => "Задержите рейд или спасите подбитый отряд. Поле останавливает ближайших бойцов с обеих сторон и защищает их от урона.",
            OrbitalAbilities.Reinforcement => "Когда резерв нужен раньше, чем его выпустит фабрика, вызовите с орбиты три Исследователя или два Вепря. Состав волны чередуется.",
            OrbitalAbilities.SystemFailure => "Перед штурмом запросите глушение с орбиты. Оно временно отключит вражеские башни и ближайшие крупные здания в зоне обзора.",
            OrbitalAbilities.ControlHack => "Перехватите управление, пока цель ещё цела. Видимая шахта или техника врага навсегда переходит к вам, если её здоровье ниже порога.",
            1 => "Перед вылазкой запросите разведку с орбиты. Сканер временно откроет район вам и союзникам даже в тумане войны.",
            2 => "Оставьте мину на подступах к базе или на пути рейда. Она даёт обзор и взрывается под вражеской наземной машиной.",
            _ => "Эффект способности пока не задан."
        };

        internal static (string Label,string Value)[] Stats(int id,PlayableProfileData data)
        {
            string N(double value)=>value.ToString("0.##");
            string Seconds(double value)=>N(value)+" с";
            string Meters(double value)=>N(value)+" м";
            string Health(double value)=>N(value)+" HP";
            switch(id)
            {
                case 1: return new[]{("Радиус",Meters(data.orbitalScanRadius)),("Время",Seconds(data.orbitalScanSeconds))};
                case 2: return new[]{
                    ("Обзор",Meters(data.orbitalMineVision)),("Радиус",Meters(data.orbitalMineBlastRadius)),
                    ("Прямой урон",Health(data.orbitalMineDirectDamage)),("Урон в зоне",Health(data.orbitalMineSplashDamage)),
                    ("Лимит мин",N(data.orbitalMineCapacity))};
                case OrbitalAbilities.ControlHack: return new[]{("Здоровье цели","< "+N(data.orbitalHackHealthPercent)+"%")};
                case OrbitalAbilities.SystemFailure: return new[]{
                    ("Радиус",Meters(data.orbitalSystemFailureRadius)),("Время",Seconds(data.orbitalSystemFailureSeconds)),
                    ("Лимит зданий",N(data.orbitalSystemFailureMaxTargets))};
                case OrbitalAbilities.Reinforcement: return new[]{("Прибытие",Seconds(data.orbitalReinforcementSeconds))};
                case OrbitalAbilities.Stasis: return new[]{
                    ("Радиус",Meters(data.orbitalStasisRadius)),("Время",Seconds(data.orbitalStasisSeconds)),
                    ("Лимит целей",N(data.orbitalStasisTargets))};
                case OrbitalAbilities.RepairNanobots: return new[]{
                    ("Радиус",Meters(data.orbitalNanobotsRadius)),("Время",Seconds(data.orbitalNanobotsSeconds)),
                    ("Ремонт",Health(data.orbitalNanobotsHealthPerSecond)+"/с")};
                case OrbitalAbilities.AcceleratedProduction: return new[]{
                    ("Доход","×"+N(data.orbitalBoostIncomeMultiplier)),("Очередь","×"+N(data.orbitalBoostProductionMultiplier)),
                    ("Время",Seconds(data.orbitalBoostSeconds))};
                case OrbitalAbilities.NuclearStrike: return new[]{
                    ("Радиус",Meters(data.orbitalNuclearRadius)),("Урон",Health(data.orbitalNuclearDamage)),
                    ("Наведение",Seconds(data.orbitalNuclearWarningSeconds)),("Спуск ракеты",Seconds(data.orbitalNuclearFlightSeconds))};
                default: return Array.Empty<(string,string)>();
            }
        }

        internal static VisualElement Create(int id,Action choose)
        {
            var art=RadialCardProfile.Load();var column=new VisualElement{name="orbital-choice-"+id};column.style.marginLeft=column.style.marginRight=4;column.style.flexShrink=0;
            var button=new Button(choose){name="orbital-card-"+id};button.style.position=Position.Relative;button.style.paddingLeft=button.style.paddingRight=art.padding;button.style.paddingTop=art.padding;button.style.paddingBottom=art.padding;
            button.style.marginLeft=button.style.marginRight=button.style.marginTop=button.style.marginBottom=0;button.style.minHeight=0;button.style.flexShrink=0;
            button.style.backgroundImage=StyleKeyword.None;button.style.backgroundColor=Color.clear;button.style.borderLeftWidth=button.style.borderRightWidth=button.style.borderTopWidth=button.style.borderBottomWidth=0;
            button.Add(new OrbitalCardFinish());
            var artwork=new OrbitalPlaceholderImage(id){name="orbital-choice-artwork",pickingMode=PickingMode.Ignore};artwork.style.alignSelf=Align.Center;artwork.style.flexShrink=0;button.Add(artwork);
            var header=new VisualElement{name="orbital-choice-header",pickingMode=PickingMode.Ignore};header.style.flexDirection=FlexDirection.Row;header.style.alignItems=Align.Center;header.style.backgroundColor=OrbitalTheme.Panel;header.style.paddingLeft=header.style.paddingRight=art.statColumnPadding;header.style.paddingTop=header.style.paddingBottom=art.statColumnPadding;
            var accent=new VisualElement{name="orbital-choice-accent",pickingMode=PickingMode.Ignore};accent.style.width=art.titleAccentWidth;accent.style.height=art.titleSize;accent.style.backgroundColor=OrbitalTheme.Cyan;accent.style.marginRight=art.statColumnPadding;header.Add(accent);
            var title=new Label(OrbitalAbilities.Name(id)){name="orbital-choice-title",pickingMode=PickingMode.Ignore};title.style.color=OrbitalTheme.Ink;title.style.unityFontStyleAndWeight=FontStyle.Bold;title.style.whiteSpace=WhiteSpace.Normal;title.style.flexShrink=1;title.style.marginLeft=title.style.marginRight=0;title.style.unityTextAlign=TextAnchor.MiddleCenter;header.Add(title);button.Add(header);column.Add(button);
            return column;
        }

        internal static void PopulateDetails(VisualElement panel,int id,PlayableProfileData data)
        {
            var art=RadialCardProfile.Load();
            panel.Clear();
            var desc=new Label(Description(id)){name="orbital-choice-description",pickingMode=PickingMode.Ignore};
            desc.style.whiteSpace=WhiteSpace.Normal;
            desc.style.color=OrbitalTheme.Ink;
            desc.style.marginLeft=desc.style.marginRight=desc.style.marginTop=desc.style.marginBottom=0;
            desc.style.flexBasis=Length.Percent(61);
            desc.style.flexShrink=1;
            desc.style.paddingRight=10;
            panel.Add(desc);
            var stats=new VisualElement{name="orbital-choice-stats",pickingMode=PickingMode.Ignore};
            stats.style.flexDirection=FlexDirection.Column;stats.style.flexGrow=1;stats.style.minWidth=0;
            stats.style.borderLeftWidth=1;stats.style.borderLeftColor=OrbitalTheme.Line;stats.style.paddingLeft=8;panel.Add(stats);
            foreach(var stat in Stats(id,data))
            {
                var cell=new VisualElement{pickingMode=PickingMode.Ignore};
                cell.style.flexDirection=FlexDirection.Row;cell.style.alignItems=Align.Center;
                var key=new Label(stat.Label){pickingMode=PickingMode.Ignore};key.style.fontSize=art.textSize;key.style.color=OrbitalTheme.Muted;
                key.style.whiteSpace=WhiteSpace.Normal;key.style.flexGrow=1;key.style.flexShrink=1;key.style.minWidth=0;
                key.style.marginLeft=key.style.marginTop=key.style.marginBottom=0;key.style.marginRight=4;
                key.style.paddingLeft=key.style.paddingRight=key.style.paddingTop=key.style.paddingBottom=0;cell.Add(key);
                var number=new Label(stat.Value){pickingMode=PickingMode.Ignore};number.style.fontSize=art.textSize;number.style.color=OrbitalTheme.Ink;
                number.style.unityFontStyleAndWeight=FontStyle.Bold;number.style.flexShrink=0;
                number.style.marginLeft=number.style.marginRight=number.style.marginTop=number.style.marginBottom=0;
                number.style.paddingLeft=number.style.paddingRight=number.style.paddingTop=number.style.paddingBottom=0;
                cell.Add(number);stats.Add(cell);
            }
        }
    }
}
