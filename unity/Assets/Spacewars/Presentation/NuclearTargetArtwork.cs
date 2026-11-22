using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    // The same projected radius and symbols serve the world, compact map and TAB.
    public static class NuclearTargetArtwork
    {
        private static NuclearTargetingProfile style;
        public static NuclearTargetingProfile Style=>style??(style=NuclearTargetingProfile.Load());
        public static void Draw(MeshGenerationContext context,Func<float,Vector2> perimeter,Vector2 center,bool valid,bool compact,string reason=null)
        {
            var s=Style;var p=context.painter2D;var color=valid?s.validColor:s.invalidColor;
            const int steps=128; // Circle tessellation accuracy, independent of gameplay radius.
            if(valid)
            {
                p.fillColor=new Color(color.r,color.g,color.b,s.fillOpacity);p.BeginPath();
                for(int i=0;i<=steps;i++){var point=perimeter(i*Mathf.PI*2/steps);if(i==0)p.MoveTo(point);else p.LineTo(point);}
                p.ClosePath();p.Fill();
            }
            p.strokeColor=color;p.lineWidth=s.ringWidth;p.BeginPath();
            for(int i=0;i<steps;i++)
            {
                if(!valid&&i%8>=5)continue;
                p.MoveTo(perimeter(i*Mathf.PI*2/steps));p.LineTo(perimeter((i+1)*Mathf.PI*2/steps));
            }
            p.Stroke();
            if(valid)
            {
                p.lineWidth=s.ringWidth*.5f;p.BeginPath();
                for(int quadrant=0;quadrant<4;quadrant++)
                {
                    float start=(quadrant*90+45-s.innerArcDegrees/2)*Mathf.Deg2Rad;
                    for(int i=0;i<=8;i++){var at=Vector2.Lerp(center,perimeter(start+s.innerArcDegrees*Mathf.Deg2Rad*i/8),1-s.innerInset);if(i==0)p.MoveTo(at);else p.LineTo(at);}
                }
                p.Stroke();p.lineWidth=s.ringWidth;p.BeginPath();
                for(int i=0;i<4;i++){var at=perimeter(i*Mathf.PI/2);var direction=(at-center).normalized;p.MoveTo(at-direction*s.tickPixels*.4f);p.LineTo(at+direction*s.tickPixels*.6f);}
                p.Stroke();
            }
            // Keep the badge inside the projected perimeter, especially on a small map radius.
            float projectedRadius=float.PositiveInfinity;
            for(int i=0;i<4;i++)projectedRadius=Mathf.Min(projectedRadius,Vector2.Distance(center,perimeter(i*Mathf.PI/2)));
            float size=Mathf.Min(compact?s.compactIconPixels:s.iconPixels,Mathf.Max(s.minIconPixels,projectedRadius*s.maxIconRadiusRatio));
            // The dot is the exact projected radius center. Offset only the missile, with a clear gap above it.
            float dotRadius=Mathf.Min(s.aimDotPixels,Mathf.Max(1,size*.12f));
            var rocket=center+new Vector2(0,-size*.72f-dotRadius-s.aimDotGapPixels);
            if(!valid)
            {
                var plateMin=center+new Vector2(-size*.95f,-size*1.65f);var plateMax=center+new Vector2(size*.95f,size*1.4f);
                p.fillColor=s.panelColor;p.BeginPath();p.MoveTo(plateMin);p.LineTo(new Vector2(plateMax.x,plateMin.y));p.LineTo(plateMax);p.LineTo(new Vector2(plateMin.x,plateMax.y));p.ClosePath();p.Fill();
            }
            if(valid)
            {
                // Nose-down missile silhouette; normalized artwork vertices are shape invariants.
                p.fillColor=s.glyphColor;p.BeginPath();
                var vertices=new[]{new Vector2(-.2f,-.88f),new Vector2(.2f,-.88f),new Vector2(.2f,-.76f),new Vector2(.48f,-.96f),new Vector2(.48f,-.43f),new Vector2(.2f,-.23f),new Vector2(.2f,.2f),new Vector2(.14f,.5f),new Vector2(0,.72f),new Vector2(-.14f,.5f),new Vector2(-.2f,.2f),new Vector2(-.2f,-.23f),new Vector2(-.48f,-.43f),new Vector2(-.48f,-.96f),new Vector2(-.2f,-.76f)};
                for(int i=0;i<vertices.Length;i++){var at=rocket+vertices[i]*size;if(i==0)p.MoveTo(at);else p.LineTo(at);}p.ClosePath();
                // Stroke before fill keeps the white core intact; the compact minimum is a raster accuracy bound.
                var join=p.lineJoin;p.lineJoin=LineJoin.Round;p.strokeColor=s.missileOutlineColor;
                p.lineWidth=2*Mathf.Min(s.missileOutlinePixels,Mathf.Max(.75f,size*.12f));p.Stroke();p.lineJoin=join;p.Fill();
                // A dark engine opening and warhead seam keep the solid silhouette readable at small sizes.
                p.fillColor=s.missileOutlineColor;p.BeginPath();p.MoveTo(rocket+new Vector2(-.11f,-.88f)*size);p.LineTo(rocket+new Vector2(.11f,-.88f)*size);p.LineTo(rocket+new Vector2(.09f,-.65f)*size);p.LineTo(rocket+new Vector2(-.09f,-.65f)*size);p.ClosePath();p.Fill();
                p.strokeColor=s.missileOutlineColor;p.lineWidth=Mathf.Max(.6f,size*.065f);p.BeginPath();p.MoveTo(rocket+new Vector2(-.18f,.17f)*size);p.LineTo(rocket+new Vector2(.18f,.17f)*size);p.Stroke();
                p.fillColor=color;p.BeginPath();p.Arc(center,dotRadius,0,360);p.Fill();
            }
            else
            {
                p.strokeColor=color;p.lineWidth=s.ringWidth;p.BeginPath();float cross=size*.55f;
                p.MoveTo(center-new Vector2(cross,cross));p.LineTo(center+new Vector2(cross,cross));p.MoveTo(center+new Vector2(cross,-cross));p.LineTo(center+new Vector2(-cross,cross));p.Stroke();
            }
            if(compact)return;
            // The standard match chat runs below screen center; keep the aim title above its perimeter.
            float y=Mathf.Max(s.reasonFontPixels*2,center.y-projectedRadius-s.labelFontPixels*2.4f),width=s.labelWidthPixels;
            p.fillColor=s.panelColor;p.BeginPath();p.MoveTo(new Vector2(center.x-width/2,y));p.LineTo(new Vector2(center.x+width/2,y));p.LineTo(new Vector2(center.x+width/2,y+s.labelFontPixels*1.8f));p.LineTo(new Vector2(center.x-width/2,y+s.labelFontPixels*1.8f));p.ClosePath();p.Fill();
            string title=valid?"ЯДЕРНЫЙ УДАР":"УДАР НЕДОСТУПЕН";
            context.DrawText(title,new Vector2(center.x-title.Length*s.labelFontPixels*.3f,y+s.labelFontPixels*.25f),s.labelFontPixels,color,null);
            if(!valid&&!string.IsNullOrEmpty(reason))context.DrawText(reason,new Vector2(center.x-reason.Length*s.reasonFontPixels*.26f,y-s.reasonFontPixels*1.5f),s.reasonFontPixels,color,null);
        }
    }
}
