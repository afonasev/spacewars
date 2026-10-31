using System;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    // Fixed silhouette proportions are artwork, not gameplay tuning.
    public static class TeamSignalArtwork
    {
        public static readonly TeamSignalKind[] Sectors={TeamSignalKind.Attack,TeamSignalKind.Recon,TeamSignalKind.Defend};
        public static string Title(TeamSignalKind kind)=>kind==TeamSignalKind.Attack?"Атака":kind==TeamSignalKind.Defend?"Защита":"Разведка";
        public static Color Paint(TeamSignalKind kind)=>kind==TeamSignalKind.Attack?new Color(1,.48f,.35f):kind==TeamSignalKind.Defend?new Color(.32f,.85f,1):new Color(1,.76f,.30f);
        public static void Glyph(Painter2D p,Vector2 at,TeamSignalKind kind,float size,Color color)
        {
            float r=size*.34f;p.strokeColor=color;p.fillColor=color;p.lineWidth=Mathf.Max(1.5f,size*.085f);
            if(kind==TeamSignalKind.Attack){foreach(float y in new[]{-.14f,.18f}){p.BeginPath();p.MoveTo(at+new Vector2(-r,size*y+r*.25f));p.LineTo(at+new Vector2(0,size*y-r*.65f));p.LineTo(at+new Vector2(r,size*y+r*.25f));p.Stroke();}}
            else if(kind==TeamSignalKind.Defend){p.BeginPath();p.MoveTo(at+new Vector2(-r,-r));p.LineTo(at+new Vector2(0,-r*.7f));p.LineTo(at+new Vector2(r,-r));p.LineTo(at+new Vector2(r,r*.2f));p.LineTo(at+new Vector2(0,r*1.1f));p.LineTo(at+new Vector2(-r,r*.2f));p.ClosePath();p.Stroke();}
            else {p.BeginPath();for(int i=0;i<=24;i++){float a=i*Mathf.PI*2/24;var q=at+new Vector2(Mathf.Cos(a)*r*1.2f,Mathf.Sin(a)*r*.72f);if(i==0)p.MoveTo(q);else p.LineTo(q);}p.ClosePath();p.Stroke();p.BeginPath();p.Arc(at,r*.28f,0,360);p.Fill();}
        }
        public static void Mark(Painter2D p,Vector2 ground,TeamSignalKind kind,float size,float alpha,bool map)
        {
            if(alpha<=0)return;var color=Paint(kind);color.a=alpha;
            p.strokeColor=color;p.lineWidth=Mathf.Max(1,size*.07f);
            if(map){p.fillColor=new Color(.035f,.06f,.08f,.92f*alpha);p.BeginPath();p.Arc(ground,size*.62f,0,360);p.Fill();p.Stroke();Glyph(p,ground,kind,size*.85f,color);}
            else{p.BeginPath();for(int i=0;i<=32;i++){float a=i*Mathf.PI*2/32;var q=ground+new Vector2(Mathf.Cos(a)*size*.65f,Mathf.Sin(a)*size*.23f);if(i==0)p.MoveTo(q);else p.LineTo(q);}p.ClosePath();p.Stroke();Glyph(p,ground-new Vector2(0,size*1.05f),kind,size,color);}
        }
    }
    public sealed class TeamSignalWheel:VisualElement
    {
        public int Selected{get;private set;}=-1;
        private readonly Label[] titles=new Label[3];
        public TeamSignalWheel()
        {
            name="team-signal-wheel";pickingMode=PickingMode.Ignore;style.position=Position.Absolute;generateVisualContent+=Draw;
            for(int i=0;i<titles.Length;i++){
                var title=new Label(TeamSignalArtwork.Title(TeamSignalArtwork.Sectors[i])){name="team-signal-label-"+TeamSignalArtwork.Sectors[i].ToString().ToLowerInvariant(),pickingMode=PickingMode.Ignore};
                title.style.position=Position.Absolute;title.style.unityTextAlign=TextAnchor.MiddleCenter;title.style.whiteSpace=WhiteSpace.NoWrap;title.style.color=OrbitalTheme.Ink;
                title.style.marginLeft=title.style.marginRight=title.style.marginTop=title.style.marginBottom=0;
                title.style.paddingLeft=title.style.paddingRight=title.style.paddingTop=title.style.paddingBottom=0;
                Add(title);titles[i]=title;
            }
            RegisterCallback<GeometryChangedEvent>(_=>LayoutTitles());
        }
        private void LayoutTitles()
        {
            float size=contentRect.width;if(size<=0)return;
            // Fixed artwork packing: rectangles fit between the inner/outer arcs even
            // at the governed minimum diameter; UI Toolkit centres the actual text.
            for(int i=0;i<titles.Length;i++){
                var title=titles[i];float x=i==0?.50f:i==1?.74f:.26f;
                title.style.left=Mathf.Round(size*(x-.145f));title.style.top=Mathf.Round(size*(i==0?.205f:.71f));
                title.style.width=Mathf.Round(size*.29f);title.style.height=Mathf.Round(size*.08f);title.style.fontSize=Mathf.Max(10,Mathf.Round(size*.052f));
            }
        }
        public void Choose(Vector2 direction,float deadzone){Selected=direction.magnitude<=deadzone?-1:((int)Math.Floor((Math.Atan2(direction.x,-direction.y)+Math.PI*2+Math.PI/3)%(Math.PI*2)/(Math.PI*2/3)));MarkDirtyRepaint();}
        private void Draw(MeshGenerationContext ctx)
        {
            float size=contentRect.width;var at=new Vector2(size/2,size/2);var p=ctx.painter2D;
            for(int i=0;i<3;i++){
                float start=-150+i*120,end=start+120,outer=size*.49f,inner=size*.20f;
                p.fillColor=i==Selected?new Color(.08f,.27f,.33f,.97f):new Color(.035f,.06f,.08f,.94f);p.strokeColor=i==Selected?OrbitalTheme.Cyan:new Color(.38f,.48f,.54f);p.lineWidth=i==Selected?2:1;
                p.BeginPath();for(int k=0;k<=24;k++){float a=(start+(end-start)*k/24)*Mathf.Deg2Rad;var q=at+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*outer;if(k==0)p.MoveTo(q);else p.LineTo(q);}for(int k=24;k>=0;k--){float a=(start+(end-start)*k/24)*Mathf.Deg2Rad;p.LineTo(at+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*inner);}p.ClosePath();p.Fill();p.Stroke();
                float angle=(-90+i*120)*Mathf.Deg2Rad;var c=at+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*size*.34f;
                TeamSignalArtwork.Glyph(p,c-new Vector2(0,size*.035f),TeamSignalArtwork.Sectors[i],size*.13f,TeamSignalArtwork.Paint(TeamSignalArtwork.Sectors[i]));
            }
            // Opaque centre masks field/map marks underneath: a Defense shield must
            // never appear to be the wheel's neutral communication symbol.
            p.fillColor=new Color(.035f,.06f,.08f,1);p.strokeColor=new Color(.38f,.48f,.54f);p.lineWidth=1;
            p.BeginPath();p.Arc(at,size*.20f,0,360);p.Fill();p.Stroke();
            p.fillColor=OrbitalTheme.Ink;p.strokeColor=OrbitalTheme.Ink;p.lineWidth=Mathf.Max(1.2f,size*.006f);
            p.BeginPath();p.Arc(at,Mathf.Max(1.5f,size*.008f),0,360);p.Fill();
            foreach(float radius in new[]{size*.035f,size*.065f})foreach(float start in new[]{-42f,138f}){p.BeginPath();p.Arc(at,radius,start,start+84);p.Stroke();}
        }
    }
}
