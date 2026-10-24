using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // Vector artwork for the selected Orbital A concept. No bitmap/logo import or audio.
    public sealed class OrbitalLoadingEmblem:VisualElement
    {
        public int PaintCount{get;private set;}
        public float LastPaintedPhase{get;private set;}
        public float AnimationPhase=>Time.realtimeSinceStartup;
        public OrbitalLoadingEmblem(){name="loading-emblem";pickingMode=PickingMode.Ignore;generateVisualContent+=Paint;}
        private void Paint(MeshGenerationContext context)
        {
            PaintCount++;LastPaintedPhase=AnimationPhase;
            var p=context.painter2D;float size=Mathf.Min(contentRect.width,contentRect.height);if(size<=0)return;
            float k=size/240;var center=contentRect.center;
            Vector2 V(float x,float y)=>center+new Vector2(x-120,y-120)*k;
            void Ring(float radius,float start,float length,Color color,float width)
            {
                p.strokeColor=color;p.lineWidth=width*k;p.BeginPath();
                int count=Mathf.Max(2,Mathf.CeilToInt(length/4));
                for(int i=0;i<=count;i++){float a=(start+length*i/count)*Mathf.Deg2Rad;var v=center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius*k;if(i==0)p.MoveTo(v);else p.LineTo(v);}p.Stroke();
            }
            void Fill(Color color,params Vector2[] points){p.fillColor=color;p.BeginPath();p.MoveTo(points[0]);for(int i=1;i<points.Length;i++)p.LineTo(points[i]);p.ClosePath();p.Fill();}
            float t=AnimationPhase;
            Ring(103,0,360,new Color(.47f,.77f,.84f,.4f),.8f);
            Ring(103,t*45,70,new Color(.51f,.9f,.95f),2);
            for(int i=0;i<4;i++)Ring(88,-t*360/14+i*90,48,new Color(.51f,.83f,.89f,.5f),1);
            float dot=t*45*Mathf.Deg2Rad;var at=center+new Vector2(Mathf.Cos(dot),Mathf.Sin(dot))*103*k;
            p.fillColor=new Color(.7f,.97f,1);p.BeginPath();for(int i=0;i<=12;i++){float a=i*Mathf.PI/6;var v=at+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*2.6f*k;if(i==0)p.MoveTo(v);else p.LineTo(v);}p.ClosePath();p.Fill();
            Fill(new Color(.77f,.98f,1),V(120,54),V(120,136),V(83,154));
            Fill(new Color(.3f,.72f,.81f),V(120,54),V(157,154),V(120,136));
            Fill(new Color(.025f,.09f,.13f),V(120,76),V(120,121),V(98,137));
            p.strokeColor=new Color(.58f,.86f,.91f,.55f);p.lineWidth=k;
            foreach(var ends in new[]{new[]{V(74,169),V(166,169)},new[]{V(120,35),V(120,44)},new[]{V(120,196),V(120,205)},new[]{V(35,120),V(44,120)},new[]{V(196,120),V(205,120)}}){p.BeginPath();p.MoveTo(ends[0]);p.LineTo(ends[1]);p.Stroke();}
        }
    }
    internal static class OrbitalLoadingView
    {
        public static VisualElement Create(VisualElement root,string status)
        {
            var screen=OrbitalTheme.Screen(root,"loading-screen",true);screen.AddToClassList("orbital-loading");
            // Match preparation reparents this overlay above the local-seat HUD.
            // Keep its theme with the overlay instead of inheriting from that HUD.
            OrbitalTheme.Install(screen);
            var shade=new VisualElement{pickingMode=PickingMode.Ignore};shade.AddToClassList("orbital-loading-shade");screen.Insert(0,shade);
            shade.generateVisualContent+=ctx=>{
                float w=shade.contentRect.width,h=shade.contentRect.height;
                var mesh=ctx.Allocate(4,6);
                Color32 top=new Color(.012f,.047f,.078f,.44f),bottom=new Color(.012f,.047f,.078f,1);
                mesh.SetNextVertex(new Vertex{position=new Vector3(0,0,Vertex.nearZ),tint=top});
                mesh.SetNextVertex(new Vertex{position=new Vector3(w,0,Vertex.nearZ),tint=top});
                mesh.SetNextVertex(new Vertex{position=new Vector3(w,h,Vertex.nearZ),tint=bottom});
                mesh.SetNextVertex(new Vertex{position=new Vector3(0,h,Vertex.nearZ),tint=bottom});
                foreach(ushort index in new ushort[]{0,1,2,2,3,0})mesh.SetNextIndex(index);
            };
            var stack=new VisualElement();stack.AddToClassList("orbital-loading-stack");screen.Add(stack);
            var emblem=new OrbitalLoadingEmblem();stack.Add(emblem);
            var wordmark=new VisualElement{name="loading-wordmark"};wordmark.AddToClassList("orbital-loading-wordmark");stack.Add(wordmark);
            var space=OrbitalTheme.Text("SPACE","orbital-loading-title");var wars=OrbitalTheme.Text("WARS","orbital-loading-title");wars.style.color=new Color(.39f,.87f,.92f);wordmark.Add(space);wordmark.Add(wars);
            stack.Add(OrbitalTheme.Text("КОНТРОЛЬ ОРБИТЫ","orbital-loading-caption"));
            var activity=new VisualElement();activity.AddToClassList("orbital-loading-activity");screen.Add(activity);
            var label=OrbitalTheme.Text(status,"orbital-loading-status");label.name="loading-status";activity.Add(label);
            var line=new VisualElement{pickingMode=PickingMode.Ignore};line.AddToClassList("orbital-loading-line");activity.Add(line);
            line.generateVisualContent+=ctx=>{
                float w=line.contentRect.width,h=line.contentRect.height;if(w<=0||h<=0)return;var p=ctx.painter2D;p.lineWidth=h;p.strokeColor=new Color(.62f,.84f,.9f,.17f);
                void Stroke(float a,float b){p.BeginPath();p.MoveTo(new Vector2(a,h/2));p.LineTo(new Vector2(b,h/2));p.Stroke();}
                Stroke(0,w);float phase=Mathf.Repeat(Time.realtimeSinceStartup/2.4f,1),start=Mathf.SmoothStep(0,1,phase)*1.32f-.32f;
                for(int i=0;i<32;i++){float a=(start+i*.01f)*w,b=a+w*.01f;if(b<0||a>w)continue;p.strokeColor=new Color(.55f,.92f,.97f,Mathf.Sin(Mathf.PI*(i+.5f)/32));Stroke(Mathf.Max(0,a),Mathf.Min(w,b));}
            };
            screen.schedule.Execute(()=>{if(screen.resolvedStyle.display==DisplayStyle.None)return;emblem.MarkDirtyRepaint();line.MarkDirtyRepaint();}).Every(16);
            screen.RegisterCallback<GeometryChangedEvent>(_=>{
                float w=screen.contentRect.width,h=screen.contentRect.height;float size=Mathf.Min(w*.15f,h*.26f);
                emblem.style.width=emblem.style.height=size;float font=Mathf.Clamp(Mathf.Min(w*.05f,h*.09f),32,80);
                space.style.fontSize=wars.style.fontSize=font;space.style.letterSpacing=wars.style.letterSpacing=font*.14f;wordmark.style.marginTop=h*.022f;
                activity.style.width=Mathf.Min(400,w*.65f);
            });
            screen.Q("menu-copyright").BringToFront();return screen;
        }
    }
}
