using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // Shared approved artwork; layout and progress remain procedural.
    public sealed class OrbitalGlyph : VisualElement
    {
        static readonly System.Collections.Generic.Dictionary<string, Texture2D> Textures = new();
        readonly Image artwork;
        readonly VisualElement progressOverlay;
        string kind;
        Color tint = OrbitalTheme.Ink;
        public string Kind { get => kind; set { if(kind==value)return;kind=value;artwork.image=TextureFor(value); } }
        public Color Tint { get => tint; set { tint=value;artwork.tintColor=new Color(value.r/OrbitalTheme.Ink.r,value.g/OrbitalTheme.Ink.g,value.b/OrbitalTheme.Ink.b,value.a); } }
        double progress=-1;
        public double Progress { get => progress; set { progress=value;progressOverlay.MarkDirtyRepaint(); } }
        public static Texture2D TextureFor(string kind)
        {
            string asset=kind switch { "credits" or "income" or "sale" => "dollar", "science" => "scientific-center", "building" => "headquarters", _ => kind };
            if(string.IsNullOrEmpty(asset))return null;
            if(!Textures.TryGetValue(asset,out var texture)){
                texture=Resources.Load<Texture2D>("OrbitalIcons/"+asset);
                if(texture!=null)Textures.Add(asset,texture);
            }
            return texture;
        }
        public OrbitalGlyph(string kind)
        {
            pickingMode=PickingMode.Ignore;style.width=32;style.height=26;
            artwork=new Image {name="glyph-artwork",pickingMode=PickingMode.Ignore,scaleMode=ScaleMode.ScaleToFit};
            artwork.style.position=Position.Absolute;artwork.style.left=0;artwork.style.right=0;artwork.style.top=0;artwork.style.bottom=0;
            Add(artwork);
            // Paint after the texture so opaque silhouette pixels cannot hide the existing arc.
            progressOverlay=new VisualElement {name="glyph-progress",pickingMode=PickingMode.Ignore};
            progressOverlay.style.position=Position.Absolute;progressOverlay.style.left=0;progressOverlay.style.right=0;progressOverlay.style.top=0;progressOverlay.style.bottom=0;
            progressOverlay.generateVisualContent+=Draw;Add(progressOverlay);
            Kind=kind;Tint=OrbitalTheme.Ink;
        }
        public void Set(string kind,Color tint,double progress=-1){Kind=kind;Tint=tint;Progress=progress;MarkDirtyRepaint();}
        void Draw(MeshGenerationContext ctx)
        {
            float w=contentRect.width,h=contentRect.height;
            if(w<=0||h<=0||Progress<0)return;
            var p=ctx.painter2D;p.strokeColor=OrbitalTheme.Cyan;p.lineWidth=3;p.BeginPath();
            p.Arc(new Vector2(w/2,h/2),Mathf.Min(w,h)*.47f,-90,-90+(float)Math.Min(1,Progress)*360);p.Stroke();
        }
    }

    public static class OrbitalPrecision
    {
        public static string BuildingGlyph(Spacewars.Simulation.PlayableBuildingKind kind)=>kind switch {
            Spacewars.Simulation.PlayableBuildingKind.Headquarters=>"headquarters",
            Spacewars.Simulation.PlayableBuildingKind.Outpost=>"outpost",
            Spacewars.Simulation.PlayableBuildingKind.Mine=>"mine",
            Spacewars.Simulation.PlayableBuildingKind.Hangar=>"hangar",
            Spacewars.Simulation.PlayableBuildingKind.Factory=>"factory",
            Spacewars.Simulation.PlayableBuildingKind.Refinery=>"refinery",
            Spacewars.Simulation.PlayableBuildingKind.ScientificCenter=>"science",
            _=>"building"
        };
        public static string ResearchGlyph(Spacewars.Simulation.PlayableResearchKind kind)=>kind switch {
            Spacewars.Simulation.PlayableResearchKind.TankChassis=>"chassis",
            Spacewars.Simulation.PlayableResearchKind.ExplorerAssaultGuns=>"assault",
            Spacewars.Simulation.PlayableResearchKind.ShkvalGuidance=>"guidance",
            Spacewars.Simulation.PlayableResearchKind.HawkSalvoSystems=>"hawk-salvo",
            Spacewars.Simulation.PlayableResearchKind.ImprovedWeaponSystems=>"weapon-systems-1",
            Spacewars.Simulation.PlayableResearchKind.AdvancedWeaponSystems=>"weapon-systems-2",
            Spacewars.Simulation.PlayableResearchKind.ImprovedDefensiveSystems=>"defensive-systems-1",
            Spacewars.Simulation.PlayableResearchKind.AdvancedDefensiveSystems=>"defensive-systems-2",
            _=>"science"
        };
        public static string Glyph(string id)
        {
            if(string.IsNullOrEmpty(id))return "building";
            var suffix=id.Substring(id.LastIndexOf(':')+1);
            if(id.StartsWith("build:",StringComparison.Ordinal)&&Enum.TryParse<Spacewars.Simulation.PlayableBuildingKind>(suffix,out var building))return BuildingGlyph(building);
            return suffix switch {
                "rally"=>"rally", "sale"=>"sale", "repair"=>"repair", "upgrade"=>"upgrade",
                "tank"=>"tank", "explorer"=>"explorer", "shkval"=>"shkval", "hawk"=>"hawk",
                "tank-chassis"=>"chassis", "explorer-assault-guns"=>"assault", "shkval-guidance"=>"guidance", "HawkSalvoSystems"=>"hawk-salvo",
                "ImprovedWeaponSystems"=>"weapon-systems-1", "AdvancedWeaponSystems"=>"weapon-systems-2",
                "ImprovedDefensiveSystems"=>"defensive-systems-1", "AdvancedDefensiveSystems"=>"defensive-systems-2",
                _=>"building"
            };
        }
        public static void Frame(VisualElement element,Color? outline=null,bool flush=false)
        {
            element.style.backgroundColor=Color.clear;
            element.style.borderTopWidth=element.style.borderBottomWidth=element.style.borderLeftWidth=element.style.borderRightWidth=0;
            element.generateVisualContent+=ctx=>{
                float w=element.contentRect.width+element.resolvedStyle.paddingLeft+element.resolvedStyle.paddingRight;
                float h=element.contentRect.height+element.resolvedStyle.paddingTop+element.resolvedStyle.paddingBottom;
                if(w<=0||h<=0)return;var p=ctx.painter2D;p.fillColor=OrbitalTheme.Panel;p.strokeColor=outline??OrbitalTheme.Line;p.lineWidth=1;
                p.BeginPath();
                if(flush){p.MoveTo(Vector2.zero);p.LineTo(new Vector2(w,0));p.LineTo(new Vector2(w,h));p.LineTo(new Vector2(0,h));}
                else{p.MoveTo(new Vector2(9,0));p.LineTo(new Vector2(w-9,0));p.LineTo(new Vector2(w,9));p.LineTo(new Vector2(w,h-9));p.LineTo(new Vector2(w-9,h));p.LineTo(new Vector2(9,h));p.LineTo(new Vector2(0,h-9));p.LineTo(new Vector2(0,9));}
                p.ClosePath();p.Fill();p.Stroke();
                p.strokeColor=outline??OrbitalTheme.Cyan;p.BeginPath();p.MoveTo(new Vector2(flush?0:9,0));p.LineTo(new Vector2(Mathf.Min(w-9,58),0));p.Stroke();
            };
        }
        public static void QueueCard(Button button,string glyph,double progress,bool filled,bool repeat=false,string title=null,string status=null)
        {
            bool active=filled&&progress>=0;
            button.AddToClassList("dock-order");button.EnableInClassList("dock-spare",!filled);
            // Fixed round waiting wells keep their shape; narrow queues wrap instead of squeezing portraits.
            float width=active?172:filled?64:28,height=active?76:filled?64:28;
            button.style.width=width;button.style.minWidth=width;button.style.maxWidth=width;
            button.style.height=height;button.style.minHeight=height;button.style.maxHeight=height;
            button.style.alignSelf=Align.Center;button.style.flexGrow=0;button.style.flexShrink=0;button.style.flexBasis=StyleKeyword.Auto;
            button.style.color=Color.clear;button.style.fontSize=active?11:9;button.style.unityTextAlign=active?TextAnchor.MiddleRight:TextAnchor.LowerCenter;
            button.style.paddingBottom=filled?5:0;button.style.paddingTop=active?14:0;
            button.style.paddingLeft=active?36:1;button.style.paddingRight=active?8:1;
            button.style.marginLeft=0;button.style.marginRight=4;button.style.marginTop=button.style.marginBottom=0;
            button.style.backgroundColor=active?new Color(.14f,.27f,.31f,.55f):new Color(.2f,.3f,.35f,filled?.10f:0);
            button.style.borderTopLeftRadius=button.style.borderTopRightRadius=button.style.borderBottomLeftRadius=button.style.borderBottomRightRadius=height/2;
            var edge=active?new Color(.4f,.78f,.85f,.45f):new Color(.5f,.65f,.7f,filled?.16f:.10f);
            button.style.borderTopColor=button.style.borderBottomColor=button.style.borderLeftColor=button.style.borderRightColor=edge;
            button.style.borderTopWidth=button.style.borderBottomWidth=button.style.borderLeftWidth=button.style.borderRightWidth=1;
            var icon=button.Q<OrbitalGlyph>();if(icon==null){icon=new OrbitalGlyph(glyph);icon.style.position=Position.Absolute;button.Add(icon);}
            var portrait=button.Q<Image>("order-portrait");if(portrait==null){portrait=new Image{name="order-portrait",pickingMode=PickingMode.Ignore,scaleMode=ScaleMode.ScaleToFit};portrait.style.position=Position.Absolute;button.Add(portrait);}
            var texture=filled?DockArtwork.TextureFor(glyph):null;
            portrait.image=texture;portrait.style.display=texture!=null?DisplayStyle.Flex:DisplayStyle.None;
            icon.style.display=filled&&texture==null?DisplayStyle.Flex:DisplayStyle.None;icon.Set(glyph,OrbitalTheme.Ink,progress);icon.Q("glyph-progress").style.display=DisplayStyle.None;
            foreach(var art in new VisualElement[]{icon,portrait})
            {
                float size=active?76:64;
                art.style.left=active?new StyleLength(0f):new StyleLength(Length.Percent(50));
                art.style.marginLeft=active?0:-size/2;art.style.top=0;art.style.width=size;art.style.height=size;
            }
            // Repeat is part of the title, so it follows short names without a vacant fixed-width gap.
            var titleRow=button.Q("order-title-row");if(titleRow==null){titleRow=new VisualElement{name="order-title-row",pickingMode=PickingMode.Ignore};titleRow.style.position=Position.Absolute;titleRow.style.flexDirection=FlexDirection.Row;titleRow.style.alignItems=Align.Center;titleRow.style.height=18;button.Add(titleRow);}
            titleRow.style.left=active?76:14;titleRow.style.right=active?8:14;titleRow.style.top=active?14:42;titleRow.style.display=active||repeat?DisplayStyle.Flex:DisplayStyle.None;
            var caption=button.Q<Label>("order-name");if(caption==null){caption=new Label{name="order-name",pickingMode=PickingMode.Ignore};caption.style.flexShrink=1;caption.style.flexGrow=0;caption.style.minWidth=0;caption.style.marginLeft=caption.style.marginRight=caption.style.marginTop=caption.style.marginBottom=0;caption.style.paddingLeft=caption.style.paddingRight=0;caption.style.unityTextAlign=TextAnchor.MiddleLeft;caption.style.fontSize=10;caption.style.color=OrbitalTheme.Ink;caption.style.whiteSpace=WhiteSpace.NoWrap;caption.style.overflow=Overflow.Hidden;caption.style.textOverflow=TextOverflow.Ellipsis;titleRow.Add(caption);}
            caption.text=title??glyph;
            var state=button.Q<Label>("order-status");if(state==null){state=new Label{name="order-status",pickingMode=PickingMode.Ignore};state.style.position=Position.Absolute;state.style.whiteSpace=WhiteSpace.NoWrap;button.Add(state);}
            state.text=active?status??button.text:filled?"":"·";state.style.display=filled&&!active?DisplayStyle.None:DisplayStyle.Flex;state.style.color=active?OrbitalTheme.Ink:OrbitalTheme.Muted;
            state.style.fontSize=active?11:9;state.style.unityTextAlign=active?TextAnchor.MiddleLeft:TextAnchor.MiddleCenter;
            state.style.left=active?76:0;state.style.right=active?5:0;state.style.top=active?37:5;
            var rail=button.Q("order-progress-rail");if(rail==null){rail=new VisualElement{name="order-progress-rail",pickingMode=PickingMode.Ignore};rail.style.position=Position.Absolute;rail.style.left=28;rail.style.right=28;rail.style.bottom=10;rail.style.overflow=Overflow.Hidden;rail.style.borderTopLeftRadius=rail.style.borderTopRightRadius=rail.style.borderBottomLeftRadius=rail.style.borderBottomRightRadius=2;rail.style.height=3;rail.style.backgroundColor=OrbitalTheme.Line;var fill=new VisualElement{name="order-progress",pickingMode=PickingMode.Ignore};fill.style.height=3;fill.style.backgroundColor=OrbitalTheme.Cyan;rail.Add(fill);button.Add(rail);}
            rail.style.display=active?DisplayStyle.Flex:DisplayStyle.None;rail[0].style.width=Length.Percent((float)(Math.Max(0,Math.Min(1,progress))*100));
            var badge=button.Q<Label>("repeat-badge");if(badge==null){badge=new Label("∞"){name="repeat-badge",pickingMode=PickingMode.Ignore};badge.style.flexShrink=0;badge.style.width=14;badge.style.fontSize=13;badge.style.marginLeft=3;badge.style.marginRight=badge.style.marginTop=badge.style.marginBottom=0;badge.style.paddingLeft=badge.style.paddingRight=0;badge.style.color=OrbitalTheme.Cyan;titleRow.Add(badge);}badge.style.display=repeat?DisplayStyle.Flex:DisplayStyle.None;

        }
    }
}
