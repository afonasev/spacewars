using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed class RadialArtwork : VisualElement
    {
        readonly ModelPortrait portrait;readonly OrbitalGlyph glyph;readonly Label check;
        string kind;bool emblem,initialized,lastUpgrade,lastSelected,lastAvailable;
        public RadialArtwork()
        {
            pickingMode=PickingMode.Ignore;
            portrait=new ModelPortrait();glyph=new OrbitalGlyph("building");
            foreach(var item in new VisualElement[]{portrait,glyph}){item.style.width=item.style.height=StyleKeyword.Auto;item.style.position=Position.Absolute;item.style.left=item.style.right=item.style.top=item.style.bottom=0;Add(item);}
            check=new Label("✓"){name="upgrade-mark",pickingMode=PickingMode.Ignore};check.style.position=Position.Absolute;check.style.right=1;check.style.bottom=1;check.style.fontSize=13;check.style.unityFontStyleAndWeight=FontStyle.Bold;check.style.color=OrbitalTheme.Cyan;check.style.backgroundColor=OrbitalTheme.Panel;check.style.borderTopLeftRadius=check.style.borderTopRightRadius=check.style.borderBottomLeftRadius=check.style.borderBottomRightRadius=8;Add(check);
            // The approval mark belongs to the artwork and scales with it. Its
            // original 13/70 reference proportions must not obscure a fitted model.
            RegisterCallback<GeometryChangedEvent>(_=>check.style.fontSize=13*Mathf.Min(1,contentRect.height/70f));
            generateVisualContent+=DrawEmblem;
        }
        public void Set(string key,bool upgraded=false,bool selected=false,bool available=true)
        {
            if(initialized&&kind==key&&lastUpgrade==upgraded&&lastSelected==selected&&lastAvailable==available)return;
            initialized=true;lastUpgrade=upgraded;lastSelected=selected;lastAvailable=available;
            kind=key;emblem=ResearchEmblem.Supports(key);
            portrait.Set(emblem?null:key,selected,available);var texture=portrait.Texture;
            portrait.style.display=texture==null?DisplayStyle.None:DisplayStyle.Flex;
            glyph.style.display=texture==null&&!emblem?DisplayStyle.Flex:DisplayStyle.None;glyph.Set(key,available?OrbitalTheme.Ink:OrbitalTheme.Muted);
            check.style.display=upgraded?DisplayStyle.Flex:DisplayStyle.None;MarkDirtyRepaint();
        }
        void DrawEmblem(MeshGenerationContext ctx)
        {
            if(emblem)ResearchEmblem.Draw(ctx,kind,contentRect,OrbitalTheme.Cyan);
        }
    }
    public static class ResearchEmblem
    {
        public static bool Supports(string key)=>key!=null&&(key.Contains("weapon-systems")||key.Contains("defensive-systems")||key=="reinforced-concrete");
        public static void Draw(MeshGenerationContext ctx,string kind,Rect rect,Color accent)
        {
            var p=ctx.painter2D;float s=Mathf.Min(rect.width,rect.height);var c=rect.center;
            Vector2 At(float x,float y)=>c+new Vector2(x,y)*s;
            p.lineWidth=1.5f;p.fillColor=new Color(.10f,.18f,.23f);p.strokeColor=new Color(.55f,.72f,.78f);
            p.BeginPath();p.MoveTo(At(0,-.43f));p.LineTo(At(.36f,-.23f));p.LineTo(At(.30f,.21f));p.LineTo(At(0,.44f));p.LineTo(At(-.30f,.21f));p.LineTo(At(-.36f,-.23f));p.ClosePath();p.Fill();p.Stroke();
            p.strokeColor=accent;
            if(kind.Contains("weapon"))
            {
                p.BeginPath();p.Arc(At(0,-.08f),s*.17f,0,360);p.Stroke();
                foreach(var v in new[]{Vector2.up,Vector2.down,Vector2.left,Vector2.right}){p.BeginPath();p.MoveTo(At(0,-.08f)+v*s*.10f);p.LineTo(At(0,-.08f)+v*s*.24f);p.Stroke();}
            }
            else if(kind=="reinforced-concrete")
            {
                p.BeginPath();p.MoveTo(At(-.18f,.15f));p.LineTo(At(-.18f,-.13f));p.LineTo(At(0,-.25f));p.LineTo(At(.18f,-.13f));p.LineTo(At(.18f,.15f));p.ClosePath();p.Stroke();p.BeginPath();p.MoveTo(At(0,-.13f));p.LineTo(At(0,.15f));p.Stroke();
            }
            int count=kind.EndsWith("3")?3:kind.EndsWith("2")?2:1;
            if(kind!="reinforced-concrete")for(int i=0;i<count;i++){float y=(kind.Contains("weapon")?.22f:.02f)+i*.12f;p.BeginPath();p.MoveTo(At(-.12f,y));p.LineTo(At(0,y-.08f));p.LineTo(At(.12f,y));p.Stroke();}
        }
    }
}
