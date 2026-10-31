using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // Variant A: one text-only card; gameplay values and gesture eligibility remain authoritative.
    public sealed class RadialActionCard : VisualElement
    {
        readonly RadialCardProfile profile=RadialCardProfile.Load();
        readonly VisualElement body,stats,actions,footer,cost,composition,noteStrip,noteIcon;
        readonly Label title,price,description,note;
        Gamepad pad;bool controller;string signature;
        public float PreferredWidth=>profile.width;
        public float PreferredHeight {get;private set;}=42;
        public bool HasAction {get;private set;}
        public RadialActionCard()
        {
            name="radial-context-card";pickingMode=PickingMode.Ignore;
            // An opaque surface prevents world markers and chat behind the card from competing with text.
            var surface=Color.Lerp(OrbitalTheme.Panel,OrbitalTheme.Line,profile.cardSurfaceLift);surface.a=1;style.backgroundColor=surface;
            // Border/radius are fixed shape details of the approved artwork, not readability controls.
            style.borderTopLeftRadius=style.borderTopRightRadius=style.borderBottomLeftRadius=style.borderBottomRightRadius=10;
            style.borderTopWidth=style.borderRightWidth=style.borderBottomWidth=style.borderLeftWidth=1;
            style.borderTopColor=style.borderRightColor=style.borderBottomColor=style.borderLeftColor=Color.Lerp(OrbitalTheme.Line,OrbitalTheme.Cyan,profile.cardRimTint);
            body=Box();Add(body);
            var header=Box();header.style.flexDirection=FlexDirection.Row;header.style.alignItems=Align.Center;body.Add(header);
            var accent=Box("radial-title-accent");accent.style.width=profile.titleAccentWidth;accent.style.height=profile.titleSize;accent.style.flexShrink=0;accent.style.marginRight=profile.statColumnPadding;accent.style.backgroundColor=OrbitalTheme.Cyan;
            accent.style.borderTopLeftRadius=accent.style.borderTopRightRadius=accent.style.borderBottomLeftRadius=accent.style.borderBottomRightRadius=2;header.Add(accent);
            title=Text("radial-title",profile.titleSize);title.style.color=OrbitalTheme.Ink;title.style.flexGrow=1;title.style.flexShrink=1;title.style.minWidth=0;title.style.marginRight=10;title.style.unityFontStyleAndWeight=FontStyle.Bold;header.Add(title);
            cost=Box();cost.style.flexDirection=FlexDirection.Row;cost.style.alignItems=Align.Center;cost.style.flexShrink=0;header.Add(cost);
            var currency=Box("radial-resource");currency.style.width=currency.style.height=profile.valueSize;currency.style.marginRight=7;currency.generateVisualContent+=DrawResource;cost.Add(currency);
            price=Text("radial-price",profile.valueSize);price.style.color=OrbitalTheme.Ink;price.style.unityFontStyleAndWeight=FontStyle.Bold;cost.Add(price);
            composition=Box("radial-composition");composition.style.flexDirection=FlexDirection.Row;composition.style.flexWrap=Wrap.Wrap;body.Add(composition);
            description=Text("radial-detail",profile.textSize);description.style.marginTop=8;body.Add(description);
            stats=Box("radial-stats");stats.style.flexDirection=FlexDirection.Row;stats.style.flexWrap=Wrap.Wrap;Rule(stats);stats.style.marginTop=12;stats.style.paddingTop=4;body.Add(stats);
            actions=Box("radial-actions");Rule(actions);actions.style.marginTop=10;actions.style.paddingTop=5;body.Add(actions);
            noteStrip=Box("radial-note-strip");noteStrip.style.flexDirection=FlexDirection.Row;noteStrip.style.alignItems=Align.Center;noteStrip.style.marginTop=profile.notePadding;
            noteStrip.style.borderTopLeftRadius=noteStrip.style.borderTopRightRadius=noteStrip.style.borderBottomLeftRadius=noteStrip.style.borderBottomRightRadius=6;
            noteIcon=Icon("lock",OrbitalTheme.Amber);noteStrip.Add(noteIcon);
            note=Text("radial-note",profile.textSize);note.style.color=OrbitalTheme.Amber;note.style.flexGrow=1;note.style.flexShrink=1;note.style.minWidth=0;noteStrip.Add(note);body.Add(noteStrip);
            footer=Box("radial-footer");footer.style.flexDirection=FlexDirection.Row;footer.style.justifyContent=Justify.SpaceBetween;Add(footer);
            // Yoga measures wrapped descriptions, long research names and warnings. Subsequent placement
            // uses that real height instead of assuming every label occupies a fixed number of lines.
            RegisterCallback<GeometryChangedEvent>(e=>{if(e.newRect.height>0)PreferredHeight=e.newRect.height;});
        }
        static VisualElement Box(string name=null)=>new VisualElement{name=name,pickingMode=PickingMode.Ignore};
        static void Rule(VisualElement element){element.style.borderTopWidth=1;element.style.borderTopColor=OrbitalTheme.Line;}
        static Label Text(string name,float font)
        {
            var label=new Label{name=name,pickingMode=PickingMode.Ignore};label.style.fontSize=font;label.style.color=OrbitalTheme.Muted;label.style.whiteSpace=WhiteSpace.Normal;label.style.flexShrink=0;
            label.style.marginLeft=label.style.marginRight=label.style.marginTop=label.style.marginBottom=0;label.style.paddingLeft=label.style.paddingRight=label.style.paddingTop=label.style.paddingBottom=0;return label;
        }
        void Hint(VisualElement parent,string button,string text,bool hold=false)
        {
            parent.Add(new RadialControlHint(button,text,controller,pad,hold,parent==actions&&!hold));
        }
        void Stat(string raw,int index,bool wide,bool lastRow,bool featured)
        {
            int split=raw.IndexOf(':');string label=split<0?raw:raw.Substring(0,split),value=split<0?"":raw.Substring(split+1).Trim();
            if(label=="Урон за попадание")label="Урон";
            var cell=Box("radial-stat");cell.style.width=Length.Percent(wide?100:50);cell.style.minHeight=featured?profile.featuredStatHeight:profile.statHeight;cell.style.flexDirection=FlexDirection.Row;cell.style.alignItems=Align.Center;cell.style.borderBottomWidth=lastRow?0:1;cell.style.borderBottomColor=OrbitalTheme.Line;
            cell.style.paddingRight=!wide&&index%2==0?profile.statColumnPadding:0;cell.style.paddingLeft=!wide&&index%2!=0?profile.statColumnPadding:0;
            if(!wide&&index%2!=0){cell.style.borderLeftWidth=1;cell.style.borderLeftColor=OrbitalTheme.Line;}
            VisualElement text=cell;
            if(featured){cell.Add(Icon(index==0?"health":"build",OrbitalTheme.Muted));text=Box();text.style.flexGrow=1;text.style.flexShrink=1;text.style.minWidth=0;cell.Add(text);}
            var key=Text("radial-stat-label",profile.textSize);key.text=label;key.style.flexGrow=1;key.style.flexShrink=1;key.style.minWidth=0;key.style.marginRight=6;text.Add(key);
            var number=Text("radial-stat-value",profile.valueSize);number.text=value;number.style.color=value.Contains("→")?OrbitalTheme.Cyan:OrbitalTheme.Ink;number.style.unityFontStyleAndWeight=FontStyle.Bold;number.style.unityTextAlign=featured?TextAnchor.MiddleLeft:TextAnchor.MiddleRight;text.Add(number);stats.Add(cell);
        }
        public void Set(OfflinePadAction action,bool gamepad,Gamepad device,string mode="buildingWheel",bool compact=false)
        {
            var data=action?.Presentation;
            string next=data?.Title+"|"+data?.Count+"|"+data?.AssignmentComplete+"|"+data?.ReplaceCount+"|"+data?.AddCount+"|"+data?.ResultCount+"|"+string.Join(";",data?.Composition.Select(v=>v.Signature)??Array.Empty<string>())+"|"+action?.Sector.Enabled+"|"+action?.Sector.Id+"|"+action?.Label+"|"+action?.Price+"|"+action?.Repeat+"|"+(action?.Command!=null)+"|"+action?.Detail+"|"+data?.Note+"|"+string.Join(";",data?.Stats??Array.Empty<string>())+"|"+gamepad+"|"+device?.deviceId+"|"+mode+"|"+compact;
            if(next==signature)return;signature=next;
            pad=device;controller=gamepad;HasAction=action!=null;body.style.display=HasAction?DisplayStyle.Flex:DisplayStyle.None;
            style.paddingLeft=style.paddingRight=compact?12:profile.padding;style.paddingTop=style.paddingBottom=HasAction?(compact?8:profile.padding):4;
            actions.Clear();footer.Clear();stats.Clear();composition.Clear();footer.style.borderTopWidth=HasAction?1:0;footer.style.borderTopColor=OrbitalTheme.Line;footer.style.marginTop=HasAction?9:0;footer.style.paddingTop=HasAction?6:0;
            if(data?.AssignmentComplete!=true)Hint(footer,gamepad?"LS":"Мышь","Выбор");Hint(footer,gamepad?"B":"Esc","Закрыть");
            PreferredHeight=profile.actionHeight+10;if(action==null)return;
            var c=action.Presentation??new RadialActionPresentation{Title=action.Label,Description=action.Detail};title.text=c.Title.Replace("Произвести: ","").Replace("Построить: ","");title.style.fontSize=compact?profile.titleSize-4:profile.titleSize;
            price.text=action.Price.HasValue&&!action.Sector.Id.EndsWith(":sale")?action.Price.Value.ToString("0"):"";cost.style.display=price.text.Length>0?DisplayStyle.Flex:DisplayStyle.None;
            foreach(var item in c.Composition)
            {
                var cell=Box("radial-composition-item");cell.style.width=Length.Percent(50);cell.style.flexDirection=FlexDirection.Row;cell.style.alignItems=Align.Center;
                var art=new RadialArtwork();art.style.width=art.style.minWidth=profile.compositionSize;art.style.height=profile.compositionSize;art.Set(item.Artwork,item.Upgraded);cell.Add(art);
                var label=Text("radial-composition-count",profile.textSize);label.text=item.Title+" ×"+item.Count;label.style.flexShrink=1;cell.Add(label);composition.Add(cell);
            }
            composition.style.display=c.Composition.Length>0?DisplayStyle.Flex:DisplayStyle.None;
            description.text=c.Description;description.style.display=compact||string.IsNullOrEmpty(c.Description)?DisplayStyle.None:DisplayStyle.Flex;
            bool featured=c.Stats.Length==2&&c.Stats[0].StartsWith("Здоровье:")&&c.Stats[1].StartsWith("Постройка:");
            for(int i=0;i<c.Stats.Length;i++)Stat(c.Stats[i],i,!featured&&c.Stats.Length<=3,featured||i>=(c.Stats.Length<=3?c.Stats.Length-1:(c.Stats.Length-1)/2*2),featured);
            stats.style.display=c.Stats.Length>0?DisplayStyle.Flex:DisplayStyle.None;
            note.text=(c.Note??"").Replace("Включение ∞ отменит ручную очередь, включая начатый заказ, с возвратом по правилам","Заменит ручную очередь, включая начатый заказ. Средства вернутся по правилам возврата.");note.style.color=note.text=="Доступно к исследованию"?OrbitalTheme.Muted:OrbitalTheme.Amber;note.style.display=string.IsNullOrEmpty(note.text)?DisplayStyle.None:DisplayStyle.Flex;
            bool warning=note.text.Length>0&&note.text!="Доступно к исследованию";
            // Blend an opaque swatch explicitly: UI alpha compositing otherwise over-brightens the amber strip.
            var noteFill=Color.Lerp(Color.Lerp(OrbitalTheme.Panel,OrbitalTheme.Line,profile.cardSurfaceLift),OrbitalTheme.Amber,profile.noteTint);noteFill.a=1;
            noteStrip.style.display=note.style.display;noteStrip.style.backgroundColor=warning?noteFill:Color.clear;
            noteStrip.style.paddingLeft=noteStrip.style.paddingRight=noteStrip.style.paddingTop=noteStrip.style.paddingBottom=warning?profile.notePadding:0;
            noteIcon.style.display=warning&&!action.Sector.Enabled?DisplayStyle.Flex:DisplayStyle.None;
            // Availability/utility warnings lead into confirmation; repeat consequences sit under repeat.
            noteStrip.RemoveFromHierarchy();if(action.Sector.Hold=="repeat")body.Add(noteStrip);else body.Insert(body.IndexOf(actions),noteStrip);
            if(mode=="groupWheel"||mode=="groupAssign")
            {
                if(c.AssignmentComplete)Hint(actions,"RB","Отпустить — закрыть");
                else
                {
                    if(action.Sector.RecallEnabled)Hint(actions,"RB","Отпустить — выбрать");
                    if(action.Sector.CanAssign)Hint(actions,"RB","Удержать и отпустить — назначить: "+c.ReplaceCount,true);
                    if(action.Sector.CanAdd)Hint(actions,"X","Добавить +"+c.AddCount+" → "+c.ResultCount);
                }
            }
            else if(mode=="baseWheel"){if(action.Sector.Enabled)Hint(actions,"LB","Отпустить — перейти");}
            else
            {
                if(action.Sector.Hold=="sell")Hint(actions,gamepad?"A":"ЛКМ",action.Sector.Id.EndsWith(":remove")?(gamepad?"Удерживать — убрать":"Дважды — подтвердить удаление"):(gamepad?"Удерживать — продать":"Дважды — подтвердить продажу"),gamepad);
                else if(action.Command!=null||action.Sector.Id=="rally")Hint(actions,gamepad?"A":"ЛКМ",action.Sector.Id=="rally"?"Указать точку":action.Sector.Hold=="repeat"?"Заказать":action.Sector.Id.EndsWith(":repair")?action.Label:"Подтвердить");
                if(action.HoldCommand!=null&&action.Sector.Hold=="repeat")Hint(actions,gamepad?"A":"ПКМ",(gamepad?"Удерживать · ":"")+(action.Repeat?"выключить повтор":"автоповтор"),gamepad);
            }
            actions.style.display=actions.childCount>0?DisplayStyle.Flex:DisplayStyle.None;
            PreferredHeight=Mathf.Ceil(c.Composition.Length/2f)*profile.compositionSize+profile.padding*2+profile.titleSize+10+(compact?0:50)+(featured?profile.featuredStatHeight:(c.Stats.Length<=3?c.Stats.Length:Mathf.Ceil(c.Stats.Length/2f))*profile.statHeight)+(string.IsNullOrEmpty(c.Note)?0:50+profile.notePadding*2)+actions.childCount*profile.actionHeight+profile.actionHeight+35;
        }
        VisualElement Icon(string kind,Color color)
        {
            var icon=Box("radial-card-icon");icon.style.width=icon.style.height=profile.statIconSize;icon.style.flexShrink=0;icon.style.marginRight=profile.statColumnPadding;
            icon.generateVisualContent+=ctx=>
            {
                // Normalized vector silhouettes are fixed artwork; size and spacing use the visual profile.
                var p=ctx.painter2D;var r=ctx.visualElement.contentRect;Vector2 At(float x,float y)=>new Vector2(r.x+x*r.width,r.y+y*r.height);
                p.fillColor=color;p.strokeColor=color;p.lineWidth=2;
                p.BeginPath();
                if(kind=="health")
                {
                    p.MoveTo(At(.5f,.92f));p.BezierCurveTo(At(-.25f,.4f),At(.1f,-.12f),At(.5f,.22f));p.BezierCurveTo(At(.9f,-.12f),At(1.25f,.4f),At(.5f,.92f));p.ClosePath();p.Fill();
                }
                else if(kind=="build")
                {
                    p.MoveTo(At(.1f,.78f));p.LineTo(At(.55f,.33f));p.BezierCurveTo(At(.4f,.06f),At(.7f,-.06f),At(.88f,.08f));p.LineTo(At(.7f,.25f));p.LineTo(At(.78f,.36f));p.LineTo(At(.98f,.2f));p.BezierCurveTo(At(1.04f,.5f),At(.83f,.65f),At(.67f,.52f));p.LineTo(At(.24f,.94f));p.BezierCurveTo(At(.07f,1),At(0,.86f),At(.1f,.78f));p.ClosePath();p.Fill();
                }
                else
                {
                    p.MoveTo(At(.28f,.45f));p.LineTo(At(.28f,.25f));p.BezierCurveTo(At(.28f,-.02f),At(.72f,-.02f),At(.72f,.25f));p.LineTo(At(.72f,.45f));p.Stroke();
                    p.BeginPath();p.MoveTo(At(.14f,.45f));p.LineTo(At(.86f,.45f));p.LineTo(At(.86f,.94f));p.LineTo(At(.14f,.94f));p.ClosePath();p.Fill();
                }
            };return icon;
        }
        static void DrawResource(MeshGenerationContext ctx)
        {
            var r=ctx.visualElement.contentRect;float w=r.width,h=r.height;var p=ctx.painter2D;
            void Face(Color c,params Vector2[] points){p.fillColor=c;p.BeginPath();p.MoveTo(points[0]);for(int i=1;i<points.Length;i++)p.LineTo(points[i]);p.ClosePath();p.Fill();}
            Face(OrbitalTheme.Ink,new Vector2(w*.5f,0),new Vector2(w,h*.25f),new Vector2(w*.5f,h*.5f),new Vector2(0,h*.25f));
            Face(OrbitalTheme.Muted,new Vector2(0,h*.32f),new Vector2(w*.45f,h*.57f),new Vector2(w*.45f,h),new Vector2(0,h*.75f));
            Face(OrbitalTheme.Ink,new Vector2(w,h*.32f),new Vector2(w*.55f,h*.57f),new Vector2(w*.55f,h),new Vector2(w,h*.75f));
        }
    }
}
