using UnityEngine;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    public sealed class OrbitalProgressRing:VisualElement
    {
        private float fraction,width=2;private Color color=OrbitalTheme.Cyan;private bool pending;
        public bool Pending{get=>pending;set{if(pending==value)return;pending=value;MarkDirtyRepaint();}}
        public float Fraction{get=>fraction;set{float next=Mathf.Clamp01(value);if(fraction==next)return;fraction=next;MarkDirtyRepaint();}}
        public float LineWidth{get=>width;set{if(width==value)return;width=value;MarkDirtyRepaint();}}
        public Color Color{get=>color;set{if(color==value)return;color=value;MarkDirtyRepaint();}}
        public OrbitalProgressRing(){pickingMode=PickingMode.Ignore;generateVisualContent+=Draw;}
        private void Draw(MeshGenerationContext context)
        {
            var p=context.painter2D;var center=contentRect.center;float radius=Mathf.Max(0,Mathf.Min(contentRect.width,contentRect.height)/2-width);
            if(radius<=0)return;
            var optics=RadialCardProfile.Load();
            // Renderer layers share the existing optical profile; no separate gameplay state.
            if(pending){p.lineWidth=width+optics.opticalGlowWidth*2;p.strokeColor=new Color(color.r,color.g,color.b,optics.opticalGlowOpacity);p.BeginPath();p.Arc(center,radius,0,360);p.Stroke();}
            p.fillColor=OrbitalTheme.Panel;p.BeginPath();p.Arc(center,radius-width/2,0,360);p.Fill();
            p.lineWidth=width;p.strokeColor=OrbitalTheme.Line;p.BeginPath();p.Arc(center,radius,0,360);p.Stroke();
            p.lineWidth=width/2;p.strokeColor=new Color(color.r,color.g,color.b,optics.opticalEdgeLight);p.BeginPath();p.Arc(center,radius-width*2,0,360);p.Stroke();
            if(pending){p.lineWidth=width/2;p.strokeColor=color;p.BeginPath();p.Arc(center,radius+width,0,360);p.Stroke();}
            if(fraction<=0)return;p.lineWidth=width;p.lineCap=LineCap.Round;p.strokeColor=OrbitalTheme.Cyan;p.BeginPath();p.Arc(center,radius,-90,-90+fraction*360,ArcDirection.Clockwise);p.Stroke();
        }
    }
    public sealed class OrbitalCooldownSweep:VisualElement
    {
        private float fraction;
        public float Fraction{get=>fraction;set{float next=Mathf.Clamp01(value);if(next==fraction)return;fraction=next;MarkDirtyRepaint();}}
        public OrbitalCooldownSweep(){pickingMode=PickingMode.Ignore;generateVisualContent+=Draw;}
        private void Draw(MeshGenerationContext context)
        {
            if(fraction<=0)return;var p=context.painter2D;var center=contentRect.center;float rx=contentRect.width/2,ry=contentRect.height/2;if(rx<=0||ry<=0)return;
            p.fillColor=OrbitalTheme.Line;p.BeginPath();p.MoveTo(center);p.LineTo(center+new Vector2(0,-ry));
            // Fixed tessellation is a renderer accuracy invariant; clockwise screen Y grows downward.
            int steps=Mathf.Max(1,Mathf.CeilToInt(fraction*64));
            for(int i=1;i<=steps;i++){
                float angle=(-90+fraction*360*i/steps)*Mathf.Deg2Rad,x=Mathf.Cos(angle),y=Mathf.Sin(angle);
                float edge=Mathf.Min(Mathf.Abs(x)<1e-6f?float.PositiveInfinity:rx/Mathf.Abs(x),Mathf.Abs(y)<1e-6f?float.PositiveInfinity:ry/Mathf.Abs(y));
                p.LineTo(center+new Vector2(x,y)*edge);
            }
            p.ClosePath();p.Fill();
        }
    }
}
