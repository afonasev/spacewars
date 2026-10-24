using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // A painted sector is also its pointer target; there are no rectangular button overlays.
    public sealed class OrbitalBattleRing : VisualElement
    {
        private OfflinePadAction[] actions=Array.Empty<OfflinePadAction>();
        private readonly Label centre,detail;
        private readonly VisualElement detailFrame;
        private readonly OrbitalGlyph centreIcon;
        private readonly VisualElement face;
        private readonly List<SectorButton> buttons=new List<SectorButton>();
        private string highlighted,heading;
        private double held;
        private float size=280;
        public Action<string,bool> InvokeAction;
        // Inner/outer radii are fixed artwork packing ratios, not input/gameplay tuning.
        public const float InnerRatio=.18f,OuterRatio=.49f;
        public static bool SectorContains(Vector2 point,float diameter,int index,int count)
        {
            if(count<=0)return false;var d=point-new Vector2(diameter/2,diameter/2);float r=d.magnitude;
            if(r<diameter*InnerRatio||r>diameter*OuterRatio)return false;
            double angle=Math.Atan2(d.x,-d.y);if(angle<0)angle+=Math.PI*2;
            double delta=Math.Abs(Math.IEEERemainder(angle-index*Math.PI*2/count,Math.PI*2));
            return delta<=Math.PI/count-.025;
        }
        private sealed class SectorButton : Button
        {
            public int Index,Count;public float Diameter;
            public readonly OrbitalGlyph Icon;public readonly Label Title,Price,Badge;
            public SectorButton(){Icon=new OrbitalGlyph("building");Title=new Label();Price=new Label();Badge=new Label();
                foreach(var v in new VisualElement[]{Icon,Title,Price,Badge}){v.pickingMode=PickingMode.Ignore;v.style.position=Position.Absolute;Add(v);}
                foreach(var v in new[]{Title,Price,Badge}){v.style.unityTextAlign=TextAnchor.MiddleCenter;v.style.fontSize=13;}
                Price.style.color=OrbitalTheme.Muted;Badge.style.color=OrbitalTheme.Amber;
                style.backgroundColor=Color.clear;style.borderTopWidth=style.borderBottomWidth=style.borderLeftWidth=style.borderRightWidth=0;
                style.marginLeft=style.marginRight=style.marginTop=style.marginBottom=0;style.paddingLeft=style.paddingRight=style.paddingTop=style.paddingBottom=0;
            }
            public override bool ContainsPoint(Vector2 point)=>SectorContains(point,Diameter,Index,Count);
        }
        public bool ContainsPanelPoint(Vector2 point)=>worldBound.Contains(point)||(detailFrame.style.display.value==DisplayStyle.Flex&&detailFrame.worldBound.Contains(point));
        public void PositionDetail(bool left,float room)
        {
            // Compact tooltip above the disc; long consequence text fits beside it when space allows.
            float width=Mathf.Min(280,Mathf.Max(150,room-12));detailFrame.style.width=width;
            detailFrame.style.left=left?-width-6:size+6;detailFrame.style.top=6;
            if(room<180){detailFrame.style.width=size;detailFrame.style.left=0;detailFrame.style.top=-50;}
        }
        public void Resize(float diameter)
        {
            size=diameter;style.width=size;style.height=size;face.style.width=size;face.style.height=size;
            centre.style.left=size*.34f;centre.style.top=size*.51f;centre.style.width=size*.32f;centre.style.height=size*.13f;centre.style.fontSize=11;centre.style.display=size<200?DisplayStyle.None:DisplayStyle.Flex;
            centreIcon.style.left=size*.43f;centreIcon.style.top=size*.40f;centreIcon.style.width=size*.14f;centreIcon.style.height=size*.11f;
            face.MarkDirtyRepaint();
        }
        public OrbitalBattleRing()
        {
            name="building-action-ring";pickingMode=PickingMode.Ignore;style.position=Position.Absolute;style.display=DisplayStyle.None;
            face=new VisualElement{pickingMode=PickingMode.Ignore};face.style.position=Position.Absolute;face.style.left=0;face.style.top=0;face.generateVisualContent+=Draw;Add(face);
            centreIcon=new OrbitalGlyph("building");centreIcon.style.position=Position.Absolute;Add(centreIcon);
            centre=new Label{pickingMode=PickingMode.Ignore};centre.style.position=Position.Absolute;centre.style.whiteSpace=WhiteSpace.Normal;centre.style.unityTextAlign=TextAnchor.MiddleCenter;centre.style.color=OrbitalTheme.Ink;Add(centre);
            // Frame geometry belongs to the parent: drawing it on a Label paints over its text.
            detailFrame=new VisualElement{pickingMode=PickingMode.Ignore};detailFrame.style.position=Position.Absolute;detailFrame.style.display=DisplayStyle.None;OrbitalPrecision.Frame(detailFrame);Add(detailFrame);
            detail=new Label{pickingMode=PickingMode.Ignore,name="radial-detail"};detail.style.whiteSpace=WhiteSpace.Normal;detail.style.color=OrbitalTheme.Ink;detail.style.paddingLeft=10;detail.style.paddingRight=10;detail.style.paddingTop=6;detail.style.paddingBottom=6;detail.style.fontSize=13;detailFrame.Add(detail);Resize(size);
        }
        public void Set(OfflinePadAction[] next,string title,string selected=null,double hold=0,string contextGlyph="building")
        {
            bool rebuild=actions.Length!=next.Length||!actions.Select(a=>a.Sector.Id).SequenceEqual(next.Select(a=>a.Sector.Id));actions=next;heading=title;held=hold;
            if(rebuild){foreach(var b in buttons)b.RemoveFromHierarchy();buttons.Clear();for(int i=0;i<next.Length;i++){
                string id=next[i].Sector.Id;var b=new SectorButton{name="radial-"+id};b.style.position=Position.Absolute;b.style.left=0;b.style.top=0;
                b.clicked+=()=>InvokeAction?.Invoke(id,false);
                b.RegisterCallback<PointerDownEvent>(e=>{if(e.button==1){InvokeAction?.Invoke(id,true);e.StopImmediatePropagation();}},TrickleDown.TrickleDown);
                b.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());b.RegisterCallback<PointerUpEvent>(e=>e.StopPropagation());b.RegisterCallback<ClickEvent>(e=>e.StopPropagation());
                b.RegisterCallback<PointerEnterEvent>(_=>Highlight(id));b.RegisterCallback<PointerLeaveEvent>(_=>Highlight(null));b.RegisterCallback<FocusInEvent>(_=>Highlight(id));b.RegisterCallback<FocusOutEvent>(_=>Highlight(null));
                buttons.Add(b);Add(b);
            }}
            centre.text=title.ToUpperInvariant();centreIcon.Set(contextGlyph,OrbitalTheme.Ink);
            for(int i=0;i<next.Length;i++){
                var a=next[i];var b=buttons[i];b.Index=i;b.Count=next.Length;b.Diameter=size;b.style.width=size;b.style.height=size;
                double angle=i/(double)next.Length*Math.PI*2-Math.PI/2;
                float radius=next.Length<=2?.28f:.33f;
                float x=size/2+(float)Math.Cos(angle)*size*radius,y=size/2+(float)Math.Sin(angle)*size*radius;
                bool dense=size<220;float width=size*(dense?.36f:.30f);float iconWidth=dense?22:36,iconHeight=dense?18:28;
                b.Icon.style.left=x-iconWidth/2;b.Icon.style.top=y-(dense?23:32);b.Icon.style.width=iconWidth;b.Icon.style.height=iconHeight;
                b.Title.style.fontSize=b.Price.style.fontSize=dense?10:13;
                b.Title.style.left=x-width/2;b.Title.style.top=y-4;b.Title.style.width=width;b.Title.style.height=22;
                b.Price.style.left=x-width/2;b.Price.style.top=y+(dense?12:16);b.Price.style.width=width;b.Price.style.height=19;
                // Repeat belongs to the price row, with room for the longest production price.
                b.Price.style.left=x-width/2-(a.Repeat?8:0);
                b.Badge.style.left=x+14;b.Badge.style.top=y+(dense?12:16);b.Badge.style.width=22;b.Badge.style.height=19;b.Badge.style.fontSize=dense?13:16;
                bool group=contextGlyph=="army"&&int.TryParse(a.Sector.Id,out int groupIndex)&&groupIndex>=1&&groupIndex<=9;b.Icon.style.display=group?DisplayStyle.None:DisplayStyle.Flex;string label=group?a.Sector.Id:ShortName(a);b.Title.text=label;b.Price.text=a.Price.HasValue?a.Price.Value.ToString("0"):a.Progress.HasValue?((int)(a.Progress.Value*100))+"%":"";
                b.Badge.text=a.Repeat?"∞":"";bool available=a.Sector.Enabled&&(a.Command!=null||a.Sector.Hold!="repeat");b.Title.style.color=available?OrbitalTheme.Ink:OrbitalTheme.Muted;
                b.Icon.Set(OrbitalPrecision.Glyph(a.Sector.Id),available?OrbitalTheme.Ink:OrbitalTheme.Muted);
                b.tooltip=a.Detail;
                // Disabled reasons remain discoverable; host rejects unavailable and stale actions.
            }
            Highlight(selected??highlighted);
        }
        static string ShortName(OfflinePadAction a)
        {
            string id=a.Sector.Id;if(id=="rally")return "Сбор";if(id.EndsWith(":sale"))return a.Label.StartsWith("✓")?"Подтвердить":"Продать";
            if(id.EndsWith(":repair"))return a.Label.StartsWith("Отменить")?"Стоп ремонт":"Ремонт";
            if(id.EndsWith(":tank-chassis"))return "Шасси";if(id.EndsWith(":explorer-assault-guns"))return "Орудия";if(id.EndsWith(":shkval-guidance"))return "Наведение";
            if(id.EndsWith(":ImprovedWeaponSystems"))return "Оружие I";if(id.EndsWith(":AdvancedWeaponSystems"))return "Оружие II";
            if(id.EndsWith(":ImprovedDefensiveSystems"))return "Защита I";if(id.EndsWith(":AdvancedDefensiveSystems"))return "Защита II";
            if(id.EndsWith(":explorer"))return "Исслед.";if(id.EndsWith(":upgrade"))return "Улучшить";
            return a.Label.Replace("Произвести: ","").Replace("Построить: ","").Replace(" ∞","").Replace("Завод переработки","Перераб.").Replace("Научный центр","Научный");
        }
        void Highlight(string id)
        {
            highlighted=actions.Any(a=>a.Sector.Id==id)?id:null;var a=actions.FirstOrDefault(x=>x.Sector.Id==highlighted);
            // The small centre keeps building identity; hover detail holds full names/reasons.
            detail.text=a?.Detail??"";detailFrame.style.display=a==null?DisplayStyle.None:DisplayStyle.Flex;
            face.MarkDirtyRepaint();
        }
        void Draw(MeshGenerationContext context)
        {
            if(actions.Length==0)return;var p=context.painter2D;var c=new Vector2(size/2,size/2);
            Vector2 At(double a,float r)=>c+new Vector2((float)Math.Cos(a),(float)Math.Sin(a))*r*size;
            for(int i=0;i<actions.Length;i++){
                var a=actions[i];double start=(i-.5)/actions.Length*Math.PI*2-Math.PI/2+.025,end=(i+.5)/actions.Length*Math.PI*2-Math.PI/2-.025;
                bool active=a.Sector.Id==highlighted;
                p.fillColor=a.Repeat?OrbitalTheme.RepeatFill:active?new Color(.065f,.10f,.13f,.98f):OrbitalTheme.Panel;p.strokeColor=active?OrbitalTheme.Cyan:OrbitalTheme.Line;p.lineWidth=active?2:1;
                p.BeginPath();p.MoveTo(At(start,OuterRatio));for(int n=1;n<=24;n++)p.LineTo(At(start+(end-start)*n/24,OuterRatio));p.LineTo(At(end,InnerRatio));for(int n=23;n>=0;n--)p.LineTo(At(start+(end-start)*n/24,InnerRatio));p.ClosePath();p.Fill();p.Stroke();
                if(active&&a.Sector.Enabled&&held>0)RadialHoldFill.Draw(p,c,start,end,size*InnerRatio,size*OuterRatio,held);
                // Production and holding are independent, even when both have progress.
                double progress=a.Progress??0;if(progress>0){p.strokeColor=OrbitalTheme.Cyan;p.lineWidth=4;p.BeginPath();p.MoveTo(At(start,.48f));for(int n=1;n<=24;n++)p.LineTo(At(start+(end-start)*Math.Min(1,progress)*n/24,.48f));p.Stroke();}
            }
            p.fillColor=OrbitalTheme.Panel;p.strokeColor=OrbitalTheme.Line;p.lineWidth=1;p.BeginPath();p.Arc(c,size*InnerRatio-2,0,360);p.Fill();p.Stroke();
        }
    }

    // Shared presentation geometry; it neither owns gesture timing nor submits actions.
    internal static class RadialHoldFill
    {
        public static void Draw(Painter2D painter,Vector2 centre,double start,double end,float inner,float outer,double progress)
        {
            float radius=Mathf.Lerp(inner,outer,Mathf.Clamp01((float)progress));
            Vector2 At(double angle,float r)=>centre+new Vector2((float)Math.Cos(angle),(float)Math.Sin(angle))*r;
            painter.fillColor=OrbitalTheme.HoldFill;
            painter.BeginPath();painter.MoveTo(At(start,radius));
            // Same tessellation as the petal: a technical packing invariant, not a timing control.
            for(int n=1;n<=24;n++)painter.LineTo(At(start+(end-start)*n/24,radius));
            painter.LineTo(At(end,inner));for(int n=23;n>=0;n--)painter.LineTo(At(start+(end-start)*n/24,inner));
            painter.ClosePath();painter.Fill();
            // A moving amber edge makes partial fill legible without covering labels.
            painter.strokeColor=OrbitalTheme.Amber;painter.lineWidth=2;
            painter.BeginPath();painter.MoveTo(At(start,radius));for(int n=1;n<=24;n++)painter.LineTo(At(start+(end-start)*n/24,radius));painter.Stroke();
        }
    }
}
