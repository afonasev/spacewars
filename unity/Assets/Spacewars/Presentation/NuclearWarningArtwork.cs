using System;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public static class NuclearWarningArtwork
    {
        // Bounded tessellation, independent of gameplay radius and vision raster resolution.
        private const int Sides=96, Rings=16;
        private static NuclearWarningProfile style;
        public static NuclearWarningProfile Style=>style??(style=NuclearWarningProfile.Load());

        public sealed class FillMesh
        {
            public readonly NavPoint[] Points;
            public readonly float[] RadiusFractions;
            public readonly ushort[] Indices;
            public FillMesh(NavPoint[] points,float[] fractions,ushort[] indices)
            {Points=points;RadiusFractions=fractions;Indices=indices;}
        }

        // A circle is convex: every point of a triangle whose vertices share one vision source
        // is visible. Testing only the vertices against the union could bridge a hidden gap.
        public static bool VisibleTriangle(NavPoint a,NavPoint b,NavPoint c,IReadOnlyList<VisionSource> sources)
        {
            foreach(var source in sources)
            {
                bool Inside(NavPoint p){double x=p.X-source.Position.X,z=p.Z-source.Position.Z;return x*x+z*z<=source.Radius*source.Radius;}
                if(Inside(a)&&Inside(b)&&Inside(c))return true;
            }
            return false;
        }

        public static FillMesh BuildFill(NavPoint center,float radius,IReadOnlyList<VisionSource> sources)
        {
            var points=new NavPoint[1+Sides*Rings];var fractions=new float[points.Length];points[0]=center;
            for(int ring=1;ring<=Rings;ring++)for(int side=0;side<Sides;side++)
            {
                int index=1+(ring-1)*Sides+side;float fraction=(float)ring/Rings,angle=side*Mathf.PI*2/Sides;
                points[index]=new NavPoint(center.X+Mathf.Cos(angle)*radius*fraction,center.Z+Mathf.Sin(angle)*radius*fraction);
                fractions[index]=fraction;
            }
            var indices=new List<ushort>(Sides*(Rings*2-1)*3);
            void Triangle(int a,int b,int c)
            {
                if(!VisibleTriangle(points[a],points[b],points[c],sources))return;
                indices.Add((ushort)a);indices.Add((ushort)b);indices.Add((ushort)c);
            }
            for(int side=0;side<Sides;side++)Triangle(0,1+side,1+(side+1)%Sides);
            for(int ring=1;ring<Rings;ring++)for(int side=0;side<Sides;side++)
            {
                int a=1+(ring-1)*Sides+side,b=1+(ring-1)*Sides+(side+1)%Sides;
                int c=1+ring*Sides+side,d=1+ring*Sides+(side+1)%Sides;
                Triangle(a,c,d);Triangle(a,d,b);
            }
            return new FillMesh(points,fractions,indices.ToArray());
        }

        public static void DrawInterior(MeshGenerationContext context,NavPoint center,float radius,float progress,
            bool descent,TeamVisionSnapshot vision,Func<NavPoint,Vector2> project)
        {
            var s=Style;var geometry=BuildFill(center,radius,vision.Sources);
            if(geometry.Indices.Length>0)
            {
                var mesh=context.Allocate(geometry.Points.Length,geometry.Indices.Length);
                var projected=new Vector2[geometry.Points.Length];
                // UI Toolkit expects clockwise projected triangles. Projection may flip with camera heading.
                for(int i=0;i<geometry.Points.Length;i++)
                {
                    var point=projected[i]=project(geometry.Points[i]);var color=s.color;float f=geometry.RadiusFractions[i];
                    color.a=f>.94f?Mathf.Lerp(s.edgeOpacity,s.lipOpacity,(f-.94f)/.06f):Mathf.Lerp(s.centerOpacity,s.edgeOpacity,f*f);
                    mesh.SetNextVertex(new Vertex{position=new Vector3(point.x,point.y,Vertex.nearZ),tint=color});
                }
                for(int i=0;i<geometry.Indices.Length;i+=3)
                {
                    ushort a=geometry.Indices[i],b=geometry.Indices[i+1],c=geometry.Indices[i+2];
                    var ab=projected[b]-projected[a];var ac=projected[c]-projected[a];
                    mesh.SetNextIndex(a);mesh.SetNextIndex(ab.x*ac.y-ab.y*ac.x>=0?b:c);mesh.SetNextIndex(ab.x*ac.y-ab.y*ac.x>=0?c:b);
                }
            }
            var p=context.painter2D;float fraction=descent?s.innerEnd:Mathf.Lerp(s.innerStart,s.innerEnd,Mathf.SmoothStep(0,1,progress));
            NavPoint At(float angle,float scale)=>new NavPoint(center.X+Mathf.Cos(angle)*radius*scale,center.Z+Mathf.Sin(angle)*radius*scale);
            var inner=s.color;inner.a=s.innerOpacity;p.strokeColor=inner;p.lineWidth=s.innerWidth;p.BeginPath();
            for(int i=0;i<Sides;i++)
            {
                var a=At(i*Mathf.PI*2/Sides,fraction);var b=At((i+1)*Mathf.PI*2/Sides,fraction);
                if(VisibleTriangle(a,b,a,vision.Sources)){p.MoveTo(project(a));p.LineTo(project(b));}
            }
            p.Stroke();
            // Constant pixel ticks are converted along projected radial directions and clipped in world space.
            p.strokeColor=s.color;p.BeginPath();
            for(int i=0;i<4;i++)
            {
                float angle=i*Mathf.PI/2;var edge=At(angle,1);var tip=project(edge);var offset=tip-project(center);
                float half=s.tickPixels*.5f/Mathf.Max(1,offset.magnitude);
                var a=At(angle,1-half);var b=At(angle,1+half);
                if(VisibleTriangle(a,b,a,vision.Sources)){p.MoveTo(project(a));p.LineTo(project(b));}
            }
            p.Stroke();
            if(vision.IsVisible(center))
            {
                var at=project(center);p.fillColor=s.outline;p.BeginPath();p.Arc(at,s.dotPixels+s.outlinePixels*.5f,0,360);p.Fill();
                p.fillColor=s.color;p.BeginPath();p.Arc(at,s.dotPixels,0,360);p.Fill();
            }
        }
    }
}
