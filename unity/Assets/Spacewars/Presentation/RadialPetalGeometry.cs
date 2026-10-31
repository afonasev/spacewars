using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // One contour definition feeds vector painting and the pointer polygon.
    public static class RadialPetalGeometry
    {
        static Vector2 At(Vector2 centre,float radius,double angle)=>centre+new Vector2((float)Math.Cos(angle),(float)Math.Sin(angle))*radius;
        public static double Inset(float radius,float gap)=>Math.Asin(Math.Min(.9,gap/(2*radius)));
        static void Trace(Vector2 centre,double start,double end,float inner,float outer,float gap,float corner,
            Action<Vector2> move,Action<Vector2> line,Action<float,double,double> arc,Action<Vector2,Vector2,Vector2> curve)
        {
            double os=start+Inset(outer,gap),oe=end-Inset(outer,gap),ins=start+Inset(inner,gap),ine=end-Inset(inner,gap);
            float round=Mathf.Min(corner,(outer-inner)/2,(float)((ine-ins)*inner/4));
            double ro=round/outer,ri=round/inner;
            var a=At(centre,outer,os);var b=At(centre,outer,oe);var c=At(centre,inner,ine);var d=At(centre,inner,ins);
            var first=At(centre,outer,os+ro);
            move(first);arc(outer,os+ro,oe-ro);
            curve(b,b,Vector2.MoveTowards(b,c,round));line(Vector2.MoveTowards(c,b,round));
            curve(c,c,At(centre,inner,ine-ri));arc(inner,ine-ri,ins+ri);
            curve(d,d,Vector2.MoveTowards(d,a,round));line(Vector2.MoveTowards(a,d,round));curve(a,a,first);
        }
        public static void Path(Painter2D painter,Vector2 centre,double start,double end,float inner,float outer,float gap,float corner)
        {
            painter.BeginPath();
            Trace(centre,start,end,inner,outer,gap,corner,painter.MoveTo,painter.LineTo,
                (r,a,b)=>painter.Arc(centre,r,(float)(a*180/Math.PI),(float)(b*180/Math.PI),b>=a?ArcDirection.Clockwise:ArcDirection.CounterClockwise),painter.BezierCurveTo);
            painter.ClosePath();
        }
        public static Vector2[] Outline(float diameter,int index,int count,float gap,float corner)
        {
            var points=new List<Vector2>();var centre=Vector2.one*diameter/2;
            double start=(index-.5)*Math.PI*2/count-Math.PI/2,end=(index+.5)*Math.PI*2/count-Math.PI/2;
            Trace(centre,start,end,diameter*OrbitalBattleRing.InnerRatio,diameter*OrbitalBattleRing.OuterRatio,gap,corner,points.Add,points.Add,
                (r,a,b)=>{ // Pointer-only flattening: <=4 px chords keep deviation below a screen pixel.
                    int n=Math.Max(1,(int)Math.Ceiling(Math.Abs(b-a)*r/4));for(int i=1;i<=n;i++)points.Add(At(centre,r,a+(b-a)*i/n));
                },(a,b,c)=>{
                    var from=points[points.Count-1];for(int i=1;i<=8;i++){float t=i/8f,u=1-t;points.Add(u*u*u*from+3*u*u*t*a+3*u*t*t*b+t*t*t*c);}
                });return points.ToArray();
        }
        // Containment includes the concave inner arc: corner checks alone can miss it.
        public static bool ContainsRectangle(Vector2[] outline,Rect rect)
        {
            if(!Contains(outline,rect.min)||!Contains(outline,new Vector2(rect.xMax,rect.yMin))||!Contains(outline,rect.max)||!Contains(outline,new Vector2(rect.xMin,rect.yMax)))return false;
            for(int i=0,j=outline.Length-1;i<outline.Length;j=i++)if(CrossesRectangle(outline[j],outline[i],rect))return false;
            return true;
        }
        static bool CrossesRectangle(Vector2 a,Vector2 b,Rect rect)
        {
            float enter=0,exit=1;var d=b-a;
            bool Clip(float direction,float distance)
            {
                if(direction==0)return distance>=0;
                float t=distance/direction;
                if(direction<0){if(t>exit)return false;enter=Mathf.Max(enter,t);}
                else {if(t<enter)return false;exit=Mathf.Min(exit,t);}return true;
            }
            return Clip(-d.x,a.x-rect.xMin)&&Clip(d.x,rect.xMax-a.x)&&Clip(-d.y,a.y-rect.yMin)&&Clip(d.y,rect.yMax-a.y)&&enter<=exit;
        }
        public static bool Contains(Vector2[] outline,Vector2 point)
        {
            bool inside=false;
            for(int i=0,j=outline.Length-1;i<outline.Length;j=i++)
            {
                var a=outline[i];var b=outline[j];
                if((a.y>point.y)!=(b.y>point.y)&&point.x<(b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x)inside=!inside;
            }
            return inside;
        }
    }
}
