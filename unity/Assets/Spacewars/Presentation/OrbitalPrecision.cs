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
            Spacewars.Simulation.PlayableBuildingKind.Factory=>"factory",
            Spacewars.Simulation.PlayableBuildingKind.Refinery=>"refinery",
            Spacewars.Simulation.PlayableBuildingKind.ScientificCenter=>"science",
            _=>"building"
        };
        public static string ResearchGlyph(Spacewars.Simulation.PlayableResearchKind kind)=>kind switch {
            Spacewars.Simulation.PlayableResearchKind.TankChassis=>"chassis",
            Spacewars.Simulation.PlayableResearchKind.ExplorerAssaultGuns=>"assault",
            Spacewars.Simulation.PlayableResearchKind.ShkvalGuidance=>"guidance",
            _=>"science"
        };
        public static string Glyph(string id)
        {
            if(string.IsNullOrEmpty(id))return "building";
            var suffix=id.Substring(id.LastIndexOf(':')+1);
            if(id.StartsWith("build:",StringComparison.Ordinal)&&Enum.TryParse<Spacewars.Simulation.PlayableBuildingKind>(suffix,out var building))return BuildingGlyph(building);
            return suffix switch {
                "rally"=>"rally", "sale"=>"sale", "repair"=>"repair", "upgrade"=>"upgrade",
                "tank"=>"tank", "explorer"=>"explorer", "shkval"=>"shkval",
                "tank-chassis"=>"chassis", "explorer-assault-guns"=>"assault", "shkval-guidance"=>"guidance",
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
        public static void QueueCard(Button button,string glyph,double progress,bool filled,bool repeat=false)
        {
            button.style.height=filled?44:22;button.style.minHeight=filled?44:22;
            button.style.alignSelf=Align.Center;button.style.flexGrow=filled?1:0;button.style.flexBasis=filled?new StyleLength(0f):new StyleLength(14);
            button.style.fontSize=11;button.style.unityTextAlign=TextAnchor.LowerCenter;button.style.paddingBottom=filled?3:0;button.style.paddingTop=filled?18:0;button.style.paddingLeft=button.style.paddingRight=filled?2:1;button.style.marginRight=3;button.style.marginBottom=0;
            button.style.backgroundColor=filled?OrbitalTheme.Panel:Color.clear;
            button.style.borderTopColor=button.style.borderBottomColor=button.style.borderLeftColor=button.style.borderRightColor=progress>=0?OrbitalTheme.Cyan:OrbitalTheme.Line;
            var icon=button.Q<OrbitalGlyph>();if(icon==null){icon=new OrbitalGlyph(glyph);icon.style.position=Position.Absolute;icon.style.left=Length.Percent(50);icon.style.marginLeft=-10;icon.style.top=2;icon.style.width=20;icon.style.height=15;button.Add(icon);}
            icon.style.display=filled?DisplayStyle.Flex:DisplayStyle.None;icon.Set(glyph,OrbitalTheme.Ink,progress);
            var badge=button.Q<Label>("repeat-badge");if(badge==null){badge=new Label("∞"){name="repeat-badge",pickingMode=PickingMode.Ignore};badge.style.position=Position.Absolute;badge.style.right=3;badge.style.top=0;badge.style.color=OrbitalTheme.Cyan;button.Add(badge);}badge.style.display=repeat?DisplayStyle.Flex:DisplayStyle.None;
        }
    }
}
