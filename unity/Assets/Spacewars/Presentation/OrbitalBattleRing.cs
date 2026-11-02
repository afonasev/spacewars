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
        private readonly Label centre;
        private readonly RadialActionCard detailFrame;
        private bool controller,compact;
        private UnityEngine.InputSystem.Gamepad device;
        private string inputMode="buildingWheel";
        public void ConfigureInput(bool gamepad,UnityEngine.InputSystem.Gamepad pad=null,string mode="buildingWheel"){if(controller!=gamepad)highlighted=null;controller=gamepad;device=pad;inputMode=mode;}
        private readonly RadialArtwork centreIcon;
        private readonly VisualElement face,centreStack;
        private readonly List<SectorButton> buttons=new List<SectorButton>();
        private string highlighted,heading;
        private double held;
        private float size=280;
        readonly RadialCardProfile cardProfile=RadialCardProfile.Load();
        bool SingleDisc=>SingleCapturedSiteButton||inputMode=="baseWheel"&&actions.Length==1;
        public Action<string,bool> InvokeAction;
        public static bool IsCapturedSiteBuild(OfflinePadAction[] next)=>next.Length==1&&next[0].Sector.Id.StartsWith("build:",StringComparison.Ordinal)&&new[]{Spacewars.Simulation.PlayableBuildingKind.Mine,Spacewars.Simulation.PlayableBuildingKind.Outpost,Spacewars.Simulation.PlayableBuildingKind.Headquarters}.Any(kind=>next[0].Sector.Id.EndsWith(":"+kind,StringComparison.Ordinal));
        public bool SingleCapturedSiteButton=>IsCapturedSiteBuild(actions);
        public static bool DiscContains(Vector2 point,float diameter)=>(point-new Vector2(diameter/2,diameter/2)).magnitude<=diameter*OuterRatio;
        // Inner/outer radii are fixed artwork packing ratios, not input/gameplay tuning.
        public const float InnerRatio=.18f,OuterRatio=.49f;
        public static bool SectorContains(Vector2 point,float diameter,int index,int count)
        {
            if(count<=0)return false;var profile=RadialCardProfile.Load();
            return RadialPetalGeometry.Contains(RadialPetalGeometry.Outline(diameter,index,count,profile.sectorGap,profile.petalCorner),point);
        }

        private sealed class SectorButton : Button
        {
            public int Index,Count;public float Diameter;public bool Disc;
            Vector2[] outline=Array.Empty<Vector2>();float gap,corner;
            public void Geometry(int index,int count,float diameter,bool disc,float nextGap,float nextCorner)
            {
                if(Index!=index||Count!=count||Diameter!=diameter||gap!=nextGap||corner!=nextCorner){outline=RadialPetalGeometry.Outline(diameter,index,count,nextGap,nextCorner);fitCached=false;utilityCached=false;}
                Index=index;Count=count;Diameter=diameter;Disc=disc;gap=nextGap;corner=nextCorner;
            }
            public readonly RadialArtwork Icon;public readonly Label Title,Price,Badge;public readonly VisualElement Lock;
            public SectorButton(){Icon=new RadialArtwork();Title=new Label{name="radial-sector-title"};Price=new Label{name="radial-sector-count"};Badge=new Label();Lock=new VisualElement{name="radial-sector-lock"};Lock.generateVisualContent+=DrawLock;
                foreach(var v in new VisualElement[]{Icon,Title,Price,Badge,Lock}){v.pickingMode=PickingMode.Ignore;v.style.position=Position.Absolute;Add(v);}
                foreach(var v in new[]{Title,Price,Badge}){v.style.unityTextAlign=TextAnchor.MiddleCenter;v.style.fontSize=13;v.style.marginLeft=v.style.marginRight=v.style.marginTop=v.style.marginBottom=0;v.style.paddingLeft=v.style.paddingRight=v.style.paddingTop=v.style.paddingBottom=0;}
                Price.style.color=OrbitalTheme.Muted;Badge.style.color=OrbitalTheme.Amber;Lock.style.display=DisplayStyle.None;
                style.backgroundColor=Color.clear;style.borderTopWidth=style.borderBottomWidth=style.borderLeftWidth=style.borderRightWidth=0;
                style.marginLeft=style.marginRight=style.marginTop=style.marginBottom=0;style.paddingLeft=style.paddingRight=style.paddingTop=style.paddingBottom=0;
            }
            static void DrawLock(MeshGenerationContext context)
            {
                // Fixed normalized padlock artwork, independent of fonts and emoji support.
                var p=context.painter2D;var r=context.visualElement.contentRect;
                Vector2 At(float x,float y)=>new Vector2(r.x+x*r.width,r.y+y*r.height);
                p.strokeColor=OrbitalTheme.Muted;p.lineWidth=1.2f;
                p.BeginPath();p.MoveTo(At(.28f,.45f));p.LineTo(At(.28f,.25f));p.BezierCurveTo(At(.28f,-.02f),At(.72f,-.02f),At(.72f,.25f));p.LineTo(At(.72f,.45f));p.Stroke();
                p.BeginPath();p.MoveTo(At(.12f,.45f));p.LineTo(At(.88f,.45f));p.LineTo(At(.88f,.94f));p.LineTo(At(.12f,.94f));p.ClosePath();p.Stroke();
            }
            public Vector2 TextSpan(float top,float height,float anchor,float inset)
            {
                float left=0,right=Diameter;
                // Intersect both horizontal edges with the actual painted petal, including
                // its rounded corners. The anchor lies in this sector's row interval.
                foreach(float y in new[]{top,top+height})
                {
                    float rowLeft=0,rowRight=Diameter;
                    for(int i=0,j=outline.Length-1;i<outline.Length;j=i++)
                    {
                        var a=outline[j];var b=outline[i];if((a.y>y)==(b.y>y))continue;
                        float x=a.x+(y-a.y)*(b.x-a.x)/(b.y-a.y);
                        if(x<=anchor)rowLeft=Mathf.Max(rowLeft,x);else rowRight=Mathf.Min(rowRight,x);
                    }
                    left=Mathf.Max(left,rowLeft);right=Mathf.Min(right,rowRight);
                }
                return new Vector2(left+inset,right-inset);
            }
            bool fitCached;
            (Vector2 anchor,float width,float height,float padding,float gap,float priceHeight,float rowWidth) fitKey;
            Rect fittedIcon,fittedPrice;
            public void FitPortrait(Vector2 anchor,float desiredWidth,float desiredHeight,float padding,float rowGap)
            {
                float priceHeight=string.IsNullOrEmpty(Price.text)?0:Price.style.height.value.value;
                float priceWidth=priceHeight==0?0:Price.MeasureTextSize(Price.text,0,MeasureMode.Undefined,0,MeasureMode.Undefined).x;
                bool repeat=!string.IsNullOrEmpty(Badge.text);
                float rowWidth=priceWidth+(repeat?Badge.style.width.value.value+rowGap:0);
                var key=(anchor,desiredWidth,desiredHeight,padding,rowGap,priceHeight,rowWidth);
                if(!fitCached||!fitKey.Equals(key)){
                    bool Fits(Rect rect){rect.xMin-=padding;rect.xMax+=padding;rect.yMin-=padding;rect.yMax+=padding;return RadialPetalGeometry.ContainsRectangle(outline,rect);}
                    var centre=Vector2.one*Diameter/2;var direction=(anchor-centre).normalized;
                    var rows=new List<Rect>();
                    for(int radialStep=0;radialStep<=32;radialStep++){
                        var at=centre+direction*(Diameter*Mathf.Lerp(InnerRatio,OuterRatio,radialStep/32f));
                        var row=new Rect(at.x-rowWidth/2,at.y-priceHeight/2,rowWidth,priceHeight);
                        if(priceHeight==0||Fits(row))rows.Add(row);
                    }
                    bool found=false;fittedIcon=fittedPrice=default;
                    // Cache the bounded normalized search until geometry or price
                    // changes. The grid is a convergence invariant, not UI tuning.
                    // Prices use the inner band; artwork can use the wider outer
                    // band instead of shrinking to an upright icon/price stack.
                    for(int scaleStep=32;scaleStep>0&&!found;scaleStep--){
                        float scale=scaleStep/32f;
                        for(int radialStep=32;radialStep>=0&&!found;radialStep--){
                            var at=centre+direction*(Diameter*Mathf.Lerp(InnerRatio,OuterRatio,radialStep/32f));
                            var icon=new Rect(at.x-desiredWidth*scale/2,at.y-desiredHeight*scale/2,desiredWidth*scale,desiredHeight*scale);
                            if(!Fits(icon))continue;
                            var separated=icon;separated.xMin-=rowGap;separated.xMax+=rowGap;separated.yMin-=rowGap;separated.yMax+=rowGap;
                            foreach(var row in rows){
                                if(priceHeight>0&&separated.Overlaps(row))continue;
                                fittedIcon=icon;fittedPrice=row;found=true;break;
                            }
                        }
                    }
                    // Text metrics can be unavailable before the first panel layout.
                    // Retry empty fits instead of permanently caching zero-size artwork.
                    fitKey=key;fitCached=found;
                }
                Icon.style.left=fittedIcon.x;Icon.style.top=fittedIcon.y;Icon.style.width=fittedIcon.width;Icon.style.height=fittedIcon.height;
                Price.style.left=fittedPrice.x;Price.style.top=fittedPrice.y;Price.style.width=priceWidth;
                Badge.style.left=fittedPrice.x+priceWidth+rowGap;Badge.style.top=fittedPrice.y;
            }
            (Vector2 anchor,float width,float height,float titleWidth,float titleHeight,float priceWidth,float priceHeight,float gap,float padding) utilityKey;
            string utilityTitle;
            bool utilityCached;
            Rect utilityIcon,utilityLabel,utilityPrice;
            public void FitUtility(Vector2 anchor,float iconWidth,float iconHeight,float rowGap,float padding)
            {
                // Fit the rendered text metrics, not the default padded label rows.
                var titleSize=Title.MeasureTextSize(Title.text,0,MeasureMode.Undefined,0,MeasureMode.Undefined);
                var priceSize=string.IsNullOrEmpty(Price.text)?Vector2.zero:Price.MeasureTextSize(Price.text,0,MeasureMode.Undefined,0,MeasureMode.Undefined);
                float titleHeight=titleSize.y,titleWidth=titleSize.x,priceHeight=priceSize.y,priceWidth=priceSize.x;
                var key=(anchor,iconWidth,iconHeight,titleWidth,titleHeight,priceWidth,priceHeight,rowGap,padding);
                if(!utilityCached||!utilityKey.Equals(key)||utilityTitle!=Title.text)
                {
                    bool Fits(Rect rect){rect.xMin-=padding;rect.xMax+=padding;rect.yMin-=padding;rect.yMax+=padding;return RadialPetalGeometry.ContainsRectangle(outline,rect);}
                    float left=outline.Min(v=>v.x),right=outline.Max(v=>v.x),top=outline.Min(v=>v.y),bottom=outline.Max(v=>v.y);
                    bool found=false;
                    // Search resolution is a bounded fitting invariant, not UI tuning.
                    // Keep text readable and move the complete stack to the nearest
                    // valid location. Test each row against the actual curved petal.
                    for(int scaleStep=8;scaleStep>=4&&!found;scaleStep--)
                    {
                        float scale=scaleStep/8f,w=iconWidth*scale,h=iconHeight*scale;
                        float stackHeight=h+rowGap+titleHeight+(priceHeight>0?rowGap+priceHeight:0);
                        float best=float.PositiveInfinity;
                        for(int iy=0;iy<=32;iy++)for(int ix=0;ix<=32;ix++)
                        {
                            var at=new Vector2(Mathf.Lerp(left,right,ix/32f),Mathf.Lerp(top,bottom,iy/32f));
                            float distance=(at-anchor).sqrMagnitude;if(distance>=best)continue;
                            var icon=new Rect(at.x-w/2,at.y-stackHeight/2,w,h);
                            var label=new Rect(at.x-titleWidth/2,icon.yMax+rowGap,titleWidth,titleHeight);
                            var price=new Rect(at.x-priceWidth/2,label.yMax+rowGap,priceWidth,priceHeight);
                            if(!Fits(icon)||!Fits(label)||priceHeight>0&&!Fits(price))continue;
                            utilityIcon=icon;utilityLabel=label;utilityPrice=price;best=distance;found=true;
                        }
                    }
                    if(!found)
                    {
                        // Diagonal wedges can have room for each row without room
                        // for a straight stack. Preserve text size and let rows follow
                        // the petal independently, with the same separation margin.
                        float stackHeight=iconHeight+rowGap+titleHeight+(priceHeight>0?rowGap+priceHeight:0);
                        var iconTarget=new Vector2(anchor.x,anchor.y-stackHeight/2+iconHeight/2);
                        var labelTarget=new Vector2(anchor.x,iconTarget.y+iconHeight/2+rowGap+titleHeight/2);
                        var priceTarget=new Vector2(anchor.x,labelTarget.y+titleHeight/2+rowGap+priceHeight/2);
                        List<Rect> Candidates(float w,float h,Vector2 target)
                        {
                            var result=new List<Rect>();
                            for(int iy=0;iy<=32;iy++)for(int ix=0;ix<=32;ix++)
                            {
                                var rect=new Rect(Mathf.Lerp(left,right,ix/32f)-w/2,Mathf.Lerp(top,bottom,iy/32f)-h/2,w,h);
                                if(Fits(rect))result.Add(rect);
                            }
                            return result.OrderBy(r=>(r.center-target).sqrMagnitude).ToList();
                        }
                        bool Separate(Rect a,Rect b){a.xMin-=rowGap;a.xMax+=rowGap;a.yMin-=rowGap;a.yMax+=rowGap;return !a.Overlaps(b);}
                        var labels=Candidates(titleWidth,titleHeight,labelTarget);
                        var prices=priceHeight>0?Candidates(priceWidth,priceHeight,priceTarget):new List<Rect>{default};
                        float best=float.PositiveInfinity;
                        for(int scaleStep=8;scaleStep>=4&&!found;scaleStep--)
                        {
                            var icons=Candidates(iconWidth*scaleStep/8f,iconHeight*scaleStep/8f,iconTarget);
                            foreach(var label in labels)foreach(var icon in icons)
                            {
                                if(icon.center.y>=label.center.y||!Separate(icon,label))continue;
                                float score=(icon.center-iconTarget).sqrMagnitude+(label.center-labelTarget).sqrMagnitude;
                                if(score>=best)continue;
                                foreach(var price in prices)
                                {
                                    if(priceHeight>0&&(price.center.y<=label.center.y||!Separate(icon,price)||!Separate(label,price)))continue;
                                    float total=score+(priceHeight>0?(price.center-priceTarget).sqrMagnitude:0);
                                    if(total<best){best=total;utilityIcon=icon;utilityLabel=label;utilityPrice=price;found=true;}
                                    break;
                                }
                            }
                        }
                    }
                    utilityKey=key;utilityTitle=Title.text;utilityCached=found&&titleWidth>0;
                }
                if(!utilityCached)return;
                Icon.style.left=utilityIcon.x;Icon.style.top=utilityIcon.y;Icon.style.width=utilityIcon.width;Icon.style.height=utilityIcon.height;
                Title.style.left=utilityLabel.x;Title.style.top=utilityLabel.y;Title.style.width=utilityLabel.width;Title.style.height=utilityLabel.height;
                Price.style.left=utilityPrice.x;Price.style.top=utilityPrice.y;Price.style.width=utilityPrice.width;Price.style.height=utilityPrice.height;
            }
            public override bool ContainsPoint(Vector2 point)=>Disc?DiscContains(point,Diameter):RadialPetalGeometry.Contains(outline,point);
        }
        public float PreferredDetailWidth=>detailFrame.PreferredWidth;
        public bool ContainsPanelPoint(Vector2 point)=>worldBound.Contains(point)||(detailFrame.style.display.value==DisplayStyle.Flex&&detailFrame.worldBound.Contains(point));
        public void PositionDetail(bool left,float room,float viewportHeight=0,float ringTop=0,float minimumTop=0)
        {
            compact=viewportHeight>0&&viewportHeight<420;
            detailFrame.Set(actions.FirstOrDefault(a=>a.Sector.Id==highlighted),controller,device,inputMode,compact);
            float width=Mathf.Min(detailFrame.PreferredWidth,Mathf.Max(230,room-12));
            detailFrame.style.width=width;
            float height=detailFrame.PreferredHeight;
            float top=0;
            if(room>=width+6)detailFrame.style.left=left?-width-6:size+6;
            else {width=Mathf.Max(230,size);detailFrame.style.width=width;detailFrame.style.left=left?size-width:0;top=ringTop>=height+6?-height-6:size+6;}
            detailFrame.style.top=viewportHeight>0?Mathf.Clamp(top,minimumTop+6-ringTop,Mathf.Max(minimumTop+6-ringTop,viewportHeight-ringTop-height-6)):top;
            detailFrame.style.maxHeight=viewportHeight>0?Mathf.Max(42,viewportHeight-12):StyleKeyword.None;
        }
        // Only auxiliary wheels move as a pair when their centred placement would clip the card.
        public void FitAuxiliary(float viewportWidth,float viewportHeight,Rect? obstacle=null)
        {
            const float gap=RadialViewportLayout.Gap,edge=RadialViewportLayout.Edge;
            compact=viewportHeight-100<420;
            float diameter=Mathf.Min(size,Mathf.Max(cardProfile.minimumWheelSize,viewportWidth-230-gap-edge*2));
            if(inputMode=="baseWheel"&&actions.Length==1)diameter=Mathf.Min(diameter,cardProfile.singleBaseSize);
            if(diameter!=size){Resize(diameter);Set(actions,heading,highlighted,held,inputMode=="baseWheel"?"headquarters":"army");}
            detailFrame.Set(actions.FirstOrDefault(a=>a.Sector.Id==highlighted),controller,device,inputMode,compact);
            float width=Mathf.Min(detailFrame.PreferredWidth,viewportWidth-size-gap-edge*2);detailFrame.style.width=width;
            float height=detailFrame.PreferredHeight;
            var desired=new Rect((viewportWidth-size)/2,Mathf.Max(50,(viewportHeight-size)/2),size+gap+width,Mathf.Max(size,height));
            var placed=RadialViewportLayout.Fit(desired,new Vector2(viewportWidth,viewportHeight),obstacle);
            style.left=placed.x;style.top=placed.y;detailFrame.style.left=size+gap;detailFrame.style.top=0;detailFrame.style.maxHeight=StyleKeyword.None;
        }
        public void Resize(float diameter)
        {
            size=diameter;style.width=size;style.height=size;face.style.width=size;face.style.height=size;
            // The icon/title stack is centred as one block inside the neutral disc.
            float inset=cardProfile.sectorGap,diameterInside=size*InnerRatio*2-inset*2;
            centreStack.style.left=centreStack.style.top=(size-diameterInside)/2;centreStack.style.width=centreStack.style.height=diameterInside;
            centre.style.fontSize=cardProfile.centreTextSize;centre.style.display=size<200?DisplayStyle.None:DisplayStyle.Flex;
            centreIcon.style.width=centreIcon.style.height=Mathf.Min(cardProfile.centreIconSize,diameterInside);
            centre.style.marginTop=size<200?0:cardProfile.labelGap;
            face.MarkDirtyRepaint();
        }
        public OrbitalBattleRing()
        {
            name="building-action-ring";pickingMode=PickingMode.Ignore;style.position=Position.Absolute;style.display=DisplayStyle.None;
            face=new VisualElement{pickingMode=PickingMode.Ignore};face.style.position=Position.Absolute;face.style.left=0;face.style.top=0;face.generateVisualContent+=Draw;Add(face);
            centreStack=new VisualElement{name="radial-centre",pickingMode=PickingMode.Ignore};centreStack.style.position=Position.Absolute;centreStack.style.alignItems=Align.Center;centreStack.style.justifyContent=Justify.Center;Add(centreStack);
            centreIcon=new RadialArtwork{name="radial-centre-artwork"};centreIcon.style.flexShrink=0;centreStack.Add(centreIcon);
            centre=new Label{name="radial-centre-label",pickingMode=PickingMode.Ignore};centre.style.flexShrink=0;centre.style.width=Length.Percent(100);centre.style.marginLeft=centre.style.marginRight=centre.style.marginTop=centre.style.marginBottom=0;centre.style.paddingLeft=centre.style.paddingRight=centre.style.paddingTop=centre.style.paddingBottom=0;centre.style.whiteSpace=WhiteSpace.Normal;centre.style.unityTextAlign=TextAnchor.MiddleCenter;centre.style.color=OrbitalTheme.Ink;centreStack.Add(centre);
            detailFrame=new RadialActionCard();detailFrame.style.position=Position.Absolute;Add(detailFrame);Resize(size);
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
            // Compact defense menus keep complete words inside the centre disc.
            bool defenseTitle=contextGlyph=="repair-tower"||contextGlyph=="sentry-turret";
            centre.text=title=="Строительство"?"ПОСТРОИТЬ":defenseTitle?title.ToUpperInvariant().Replace(" ","\n"):title.ToUpperInvariant();
            centre.style.fontSize=defenseTitle||title=="Строительство"?9:cardProfile.centreTextSize;centreIcon.Set(contextGlyph);
            centre.style.display=SingleDisc||size<200?DisplayStyle.None:DisplayStyle.Flex;centreIcon.style.display=SingleDisc?DisplayStyle.None:DisplayStyle.Flex;
            for(int i=0;i<next.Length;i++){
                var a=next[i];var b=buttons[i];b.Geometry(i,next.Length,size,SingleDisc,cardProfile.sectorGap,cardProfile.petalCorner);b.style.width=size;b.style.height=size;
                double angle=i/(double)next.Length*Math.PI*2-Math.PI/2;
                float radius=SingleDisc?0:next.Length==2?(InnerRatio+OuterRatio)/2:next.Length==1?.28f:.33f;
                float x=size/2+(float)Math.Cos(angle)*size*radius,y=size/2+(float)Math.Sin(angle)*size*radius;
                bool dense=size<220;float width=size*(SingleDisc?.9f:dense?.36f:.30f);bool portrait=a.Presentation?.Artwork!=null;float iconWidth=portrait?(dense?54:78):(dense?22:32),iconHeight=portrait?(dense?48:70):(dense?18:28);
                if(OrbitalGlyph.IsUtilityArtwork(OrbitalPrecision.Glyph(a.Sector.Id)))iconWidth=iconHeight=cardProfile.utilityIconSize;
                b.Icon.style.left=x-iconWidth/2;b.Icon.style.top=y-(portrait?(dense?35:49):(dense?23:32));b.Icon.style.width=iconWidth;b.Icon.style.height=iconHeight;
                b.Title.style.fontSize=b.Price.style.fontSize=SingleDisc?13:dense?10:13;
                b.Title.style.left=x-width/2;b.Title.style.top=y-(SingleDisc?7:4);b.Title.style.width=width;b.Title.style.height=SingleDisc?34:22;b.Title.style.whiteSpace=SingleDisc?WhiteSpace.Normal:WhiteSpace.NoWrap;
                b.Price.style.left=x-width/2;b.Price.style.top=y+(SingleDisc?30:dense?12:16);b.Price.style.width=width;b.Price.style.height=19;
                // Repeat belongs to the price row, with room for the longest production price.
                b.Price.style.left=x-width/2-(a.Repeat?8:0);
                if(portrait)b.Price.style.top=y+(dense?16:24);
                b.Badge.style.left=x+14;b.Badge.style.top=y+(portrait?(dense?16:24):(dense?12:16));b.Badge.style.width=22;b.Badge.style.height=19;b.Badge.style.fontSize=dense?13:16;
                if(next.Length==2){
                    // Centre the complete icon/title/price stack in the annulus, not just its title.
                    // This leaves equal clearance from the inner disc and the outer edge.
                    for(int part=0;part<3;part++){
                        var textPart=part==0?b.Title:part==1?b.Price:b.Badge;
                        textPart.style.marginLeft=textPart.style.marginRight=textPart.style.marginTop=textPart.style.marginBottom=0;
                        textPart.style.paddingLeft=textPart.style.paddingRight=textPart.style.paddingTop=textPart.style.paddingBottom=0;
                    }
                    float titleHeight=dense?14:22,priceHeight=dense?14:19;
                    float stackTop=y-(iconHeight+titleHeight+priceHeight+4)/2;
                    b.Icon.style.top=stackTop;
                    b.Title.style.top=stackTop+iconHeight+2;b.Title.style.height=titleHeight;
                    b.Price.style.top=b.Badge.style.top=stackTop+iconHeight+titleHeight+4;b.Price.style.height=priceHeight;
                }
                bool group=contextGlyph=="army"&&int.TryParse(a.Sector.Id,out int groupIndex)&&groupIndex>=1&&groupIndex<=9;b.Icon.style.display=group?DisplayStyle.None:DisplayStyle.Flex;string label=SingleDisc?a.Label.Replace("Построить: ","Построить\n"):group?a.Sector.Id:ShortName(a);b.Title.text=label;b.Title.style.display=portrait&&inputMode!="baseWheel"?DisplayStyle.None:DisplayStyle.Flex;b.Price.text=a.Price.HasValue?a.Price.Value.ToString("0"):a.Progress.HasValue?((int)(a.Progress.Value*100))+"%":"";
                if(group)
                {
                    int count=a.Presentation?.Count??0;
                    float numberHeight=cardProfile.groupNumberSize+4,countHeight=cardProfile.textSize+4,stackHeight=count>0?numberHeight+cardProfile.labelGap+countHeight:numberHeight;
                    b.Title.style.fontSize=cardProfile.groupNumberSize;b.Title.style.top=y-stackHeight/2;b.Title.style.height=numberHeight;
                    b.Price.text=count>0?count+" ед.":"";b.Price.style.fontSize=cardProfile.textSize;b.Price.style.top=y-stackHeight/2+numberHeight+cardProfile.labelGap;b.Price.style.height=countHeight;
                }
                if(inputMode=="baseWheel")
                {
                    // Centre the portrait and one-line identity as a single stack inside the annulus.
                    float portraitHeight=cardProfile.compositionSize,titleHeight=cardProfile.textSize+4;
                    float stackTop=y-(portraitHeight+titleHeight+2)/2;
                    b.Icon.style.width=portraitHeight*78/70;b.Icon.style.height=portraitHeight;b.Icon.style.left=x-portraitHeight*78/70/2;b.Icon.style.top=stackTop;
                    b.Title.text=a.Label=="Первый штаб"?"Штаб 1":a.Label;b.Title.style.top=stackTop+portraitHeight+2;b.Title.style.whiteSpace=WhiteSpace.NoWrap;b.Title.style.height=titleHeight;b.Title.style.fontSize=cardProfile.textSize;
                }
                bool construction=a.Sector.Id.StartsWith("build:",StringComparison.Ordinal)&&!SingleDisc;
                b.Lock.style.display=construction&&!a.Sector.Enabled?DisplayStyle.Flex:DisplayStyle.None;
                if(construction)
                {
                    // The approved composition is authored at 360 px. This reference is a
                    // coordinate invariant; readable sizes are exposed in RadialCardProfile.
                    float scale=size/360f,font=cardProfile.constructionTextSize*scale,gap=cardProfile.labelGap*scale;
                    float line=font+gap/2,portraitHeight=cardProfile.constructionPortraitSize*scale;
                    float radiusMiddle=size*(InnerRatio+OuterRatio)/2;
                    x=size/2+(float)Math.Cos(angle)*radiusMiddle;y=size/2+(float)Math.Sin(angle)*radiusMiddle;
                    // Fit the entire stack to the radial band, including the outer seam clearance.
                    float band=size*(OuterRatio-InnerRatio)-cardProfile.sectorGap*2;
                    portraitHeight=Mathf.Min(portraitHeight,band-line*(a.Price.HasValue?2:1)-gap);
                    float stackHeight=portraitHeight+line*(a.Price.HasValue?2:1)+gap,top=y-stackHeight/2;
                    b.Icon.style.left=x-portraitHeight/2;b.Icon.style.top=top;b.Icon.style.width=b.Icon.style.height=portraitHeight;
                    b.Title.text=ConstructionName(a);b.Title.style.display=DisplayStyle.Flex;b.Title.style.fontSize=font;b.Title.style.unityFontStyleAndWeight=FontStyle.Bold;
                    b.Title.style.left=x-width/2-gap;b.Title.style.width=width+gap*2;b.Title.style.top=top+portraitHeight+gap;b.Title.style.height=line;
                    var span=b.TextSpan(b.Title.style.top.value.value,line,x,gap/2);
                    float measured=b.Title.MeasureTextSize(b.Title.text,0,MeasureMode.Undefined,0,MeasureMode.Undefined).x;
                    float titleWidth=Mathf.Min(width+gap*2,span.y-span.x);
                    if(measured>titleWidth)b.Title.style.fontSize=font*titleWidth/measured;
                    b.Title.style.width=titleWidth;b.Title.style.left=Mathf.Clamp(x-titleWidth/2,span.x,span.y-titleWidth);
                    b.Price.style.fontSize=font;b.Price.style.left=x-width/2;b.Price.style.top=top+portraitHeight+gap+line;b.Price.style.height=line;
                    float priceWidth=b.Price.MeasureTextSize(b.Price.text,0,MeasureMode.Undefined,0,MeasureMode.Undefined).x;
                    b.Lock.style.width=b.Lock.style.height=font;b.Lock.style.left=x+(a.Price.HasValue?priceWidth/2+gap:-font/2);
                    b.Lock.style.top=top+portraitHeight+gap+line;
                }
                b.Badge.text=a.Repeat?"∞":"";
                if(portrait&&!construction&&!SingleDisc&&inputMode!="baseWheel"&&!group&&a.Sector.Hold!="repeat")
                {
                    float desired=a.Presentation.Upgrade?cardProfile.upgradePortraitSize:cardProfile.compositionSize;
                    float aspect=iconWidth/iconHeight;
                    if(a.Presentation.Upgrade)
                    {
                        var texture=DockArtwork.TextureFor(a.Presentation.Artwork);var frame=PortraitFraming.UvFor(a.Presentation.Artwork);
                        if(texture!=null)aspect=texture.width*frame.width/(texture.height*frame.height);
                    }
                    b.FitPortrait(new Vector2(x,y),aspect*desired,desired,cardProfile.labelGap,cardProfile.labelGap);
                }
                bool utility=a.Sector.Id=="rally"||a.Sector.Id.EndsWith(":repair")||a.Sector.Id.EndsWith(":sale")||a.Sector.Id.EndsWith(":remove");
                if(utility&&!SingleDisc)b.FitUtility(new Vector2(x,y),iconWidth,iconHeight,cardProfile.labelGap,cardProfile.sectorGap/2);
                bool available=a.Sector.Enabled&&(a.Command!=null||a.Sector.Hold!="repeat");b.Title.style.color=available?OrbitalTheme.Ink:OrbitalTheme.Muted;
                b.Icon.Set(a.Presentation?.Artwork??OrbitalPrecision.Glyph(a.Sector.Id),a.Presentation?.Upgrade==true,a.Sector.Id==highlighted,available);
                b.tooltip=a.Detail;
                // Disabled reasons remain discoverable; host rejects unavailable and stale actions.
            }
            Highlight(controller?selected:selected??highlighted);
        }
        static string ConstructionName(OfflinePadAction action)
        {
            string name=action.Label.Replace("Построить: ","");
            return name=="Пулемётная турель"?"Пулемётная":name=="Ремонтная вышка"?"Ремонтная":name=="Завод переработки"?"Переработка":name;
        }
        static string ShortName(OfflinePadAction a)
        {
            string id=a.Sector.Id;if(id=="rally")return "Сбор";if(id.EndsWith(":sale"))return a.Label.StartsWith("✓")?"Подтвердить":"Продать";
            if(id.EndsWith(":remove"))return a.Label.StartsWith("✓")?"Подтвердить":"Убрать";
            if(id.EndsWith(":repair"))return a.Label.StartsWith("Отменить")?"Стоп ремонт":"Ремонт";
            if(id.EndsWith(":tank-chassis"))return "Шасси";if(id.EndsWith(":explorer-assault-guns"))return "Орудия";if(id.EndsWith(":shkval-guidance"))return "Наведение";
            if(id.EndsWith(":ReinforcedConcrete"))return "Бетон I";if(id.EndsWith(":AdvancedReinforcedConcrete"))return "Бетон II";if(id.EndsWith(":HawkSalvoSystems"))return "Залп";
            if(id.EndsWith(":UltimateWeaponSystems"))return "Оружие III";if(id.EndsWith(":UltimateDefensiveSystems"))return "Защита III";if(id.EndsWith(":ScientificCenterUpgrade"))return "Улучшить центр";
            if(id.EndsWith(":ImprovedWeaponSystems"))return "Оружие I";if(id.EndsWith(":AdvancedWeaponSystems"))return "Оружие II";
            if(id.EndsWith(":ImprovedDefensiveSystems"))return "Защита I";if(id.EndsWith(":AdvancedDefensiveSystems"))return "Защита II";
            if(id.EndsWith(":explorer"))return "Исслед.";if(id.EndsWith(":upgrade"))return "Улучшить";
            return a.Label.Replace("Произвести: ","").Replace("Построить: ","").Replace(" ∞","").Replace("Завод переработки","Перераб.").Replace("Научный центр","Научный");
        }
        void Highlight(string id)
        {
            highlighted=actions.Any(a=>a.Sector.Id==id)?id:null;var a=actions.FirstOrDefault(x=>x.Sector.Id==highlighted);
            // The small centre keeps building identity; hover detail holds full names/reasons.
            detailFrame.Set(a,controller,device,inputMode,compact);detailFrame.style.display=DisplayStyle.Flex;
            for(int i=0;i<buttons.Count;i++){var item=actions[i];buttons[i].Icon.Set(item.Presentation?.Artwork??OrbitalPrecision.Glyph(item.Sector.Id),item.Presentation?.Upgrade==true,item.Sector.Id==highlighted,item.Sector.Enabled&&(item.Command!=null||item.Sector.Hold!="repeat"));}
            face.MarkDirtyRepaint();
        }
        Color OpticalFill(bool active,bool repeat=false)
        {
            if(repeat)return OrbitalTheme.RepeatFill;
            var color=Color.Lerp(OrbitalTheme.Panel,OrbitalTheme.Line,cardProfile.opticalSurfaceLift);
            if(active)color=Color.Lerp(color,OrbitalTheme.Cyan,cardProfile.opticalSelectionTint);
            color.a=1;return color;
        }
        void OpticalSurface(Painter2D p,Color body,Action<float> contour,bool repeat=false)
        {
            // Fixed gradient sampling is a vector-rendering quality invariant. Strength is profile-owned.
            // Nested opaque contours keep the original silhouette and its background occlusion exact.
            const int steps=16;
            for(int step=steps;step>=1;step--)
            {
                float t=(float)step/steps;
                p.fillColor=repeat?body:Color.Lerp(body,OrbitalTheme.Ink,cardProfile.opticalSheen*t*t);
                contour(t);p.Fill();
                if(repeat||cardProfile.opticalSheen==0)break;
            }
        }
        void OpticalEdge(Painter2D p,Action path,bool active)
        {
            if(active&&cardProfile.opticalGlowOpacity>0)
            {
                var glow=OrbitalTheme.Cyan;glow.a=cardProfile.opticalGlowOpacity;
                p.strokeColor=glow;p.lineWidth=cardProfile.opticalGlowWidth;path();p.Stroke();
            }
            p.strokeColor=active?OrbitalTheme.Cyan:OrbitalTheme.Line;p.lineWidth=active?2:1;path();p.Stroke();
        }
        void OpticalHighlight(Painter2D p,Vector2 centre,float radius,double start,double end)
        {
            var light=OrbitalTheme.Ink;light.a=cardProfile.opticalEdgeLight;p.strokeColor=light;p.lineWidth=1;
            // Fixed upper-left light direction is an artwork invariant, independent of input and camera.
            // Clip each existing arc to the upper semicircle; the inset never alters the hit contour.
            for(int turn=-1;turn<=1;turn++)
            {
                double lo=Math.Max(start,turn*2*Math.PI-Math.PI),hi=Math.Min(end,turn*2*Math.PI);
                if(hi<=lo)continue;
                p.BeginPath();p.Arc(centre,radius,(float)(lo*180/Math.PI),(float)(hi*180/Math.PI));p.Stroke();
            }
        }
        void Draw(MeshGenerationContext context)
        {
            if(actions.Length==0)return;var panel=OrbitalTheme.Panel;panel.a=1;var p=context.painter2D;var c=new Vector2(size/2,size/2);
            void Disc(float radius){p.BeginPath();p.Arc(c,radius,0,360);p.ClosePath();}
            if(SingleDisc)
            {
                bool active=actions[0].Sector.Id==highlighted;
                OpticalSurface(p,OpticalFill(active),t=>Disc(size*OuterRatio*t));
                OpticalEdge(p,()=>Disc(size*OuterRatio),active);
                OpticalHighlight(p,c,size*OuterRatio-cardProfile.opticalEdgeInset,-Math.PI,0);return;
            }
            // Keep the opaque backing behind all seams. Optical depth is painted, not world transparency.
            p.fillColor=panel;Disc(size*OuterRatio);p.Fill();
            Vector2 At(double a,float r)=>c+new Vector2((float)Math.Cos(a),(float)Math.Sin(a))*r*size;
            for(int i=0;i<actions.Length;i++){
                var a=actions[i];double start=(i-.5)/actions.Length*Math.PI*2-Math.PI/2,end=(i+.5)/actions.Length*Math.PI*2-Math.PI/2;
                bool active=a.Sector.Id==highlighted;
                void Petal()=>RadialPetalGeometry.Path(p,c,start,end,size*InnerRatio,size*OuterRatio,cardProfile.sectorGap,cardProfile.petalCorner);
                OpticalSurface(p,OpticalFill(active,a.Repeat),t=>RadialPetalGeometry.Path(p,c,start,end,size*InnerRatio,Mathf.Lerp(size*InnerRatio,size*OuterRatio,t),cardProfile.sectorGap,cardProfile.petalCorner),a.Repeat);
                OpticalEdge(p,Petal,active);
                float rim=size*OuterRatio-cardProfile.opticalEdgeInset;
                double trim=RadialPetalGeometry.Inset(rim,cardProfile.sectorGap)+cardProfile.petalCorner/rim;
                OpticalHighlight(p,c,rim,start+trim,end-trim);
                // Semantic feedback is painted last so surface light cannot wash out hold/repeat or progress.
                if(active&&a.Sector.Enabled&&held>0)RadialHoldFill.Draw(p,c,start,end,size*InnerRatio,size*OuterRatio,held,cardProfile.sectorGap,cardProfile.petalCorner);
                double progress=a.Progress??0;if(progress>0){p.strokeColor=OrbitalTheme.Cyan;p.lineWidth=4;p.BeginPath();p.MoveTo(At(start,.48f));for(int n=1;n<=24;n++)p.LineTo(At(start+(end-start)*Math.Min(1,progress)*n/24,.48f));p.Stroke();}
            }
            float centreRadius=size*InnerRatio-cardProfile.sectorGap/2;
            OpticalSurface(p,OpticalFill(false),t=>Disc(centreRadius*t));OpticalEdge(p,()=>Disc(centreRadius),false);
            OpticalHighlight(p,c,centreRadius-cardProfile.opticalEdgeInset,-Math.PI,0);
        }

    }

    // Shared presentation geometry; it neither owns gesture timing nor submits actions.
    internal static class RadialHoldFill
    {
        public static void Draw(Painter2D painter,Vector2 centre,double start,double end,float inner,float outer,double progress,float gap=0,float corner=0)
        {
            float radius=Mathf.Lerp(inner,outer,Mathf.Clamp01((float)progress));
            Vector2 At(double angle,float r)=>centre+new Vector2((float)Math.Cos(angle),(float)Math.Sin(angle))*r;
            if(gap>0)
            {
                if(radius<=inner)return;
                painter.fillColor=OrbitalTheme.HoldFill;RadialPetalGeometry.Path(painter,centre,start,end,inner,radius,gap,corner);painter.Fill();
                double inset=RadialPetalGeometry.Inset(radius,gap);painter.strokeColor=OrbitalTheme.Amber;painter.lineWidth=2;painter.BeginPath();painter.Arc(centre,radius,(float)((start+inset)*180/Math.PI),(float)((end-inset)*180/Math.PI));painter.Stroke();return;
            }
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
