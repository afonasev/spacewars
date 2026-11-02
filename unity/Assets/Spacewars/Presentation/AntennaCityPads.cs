using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Flat markings only: no supports, colliders or ownership colors are added.
    public static class AntennaCityPads
    {
        public sealed class Pad
        {
            public int SiteId;
            public PlayableBuildingKind Kind;
            public NavObstacle Outer,Inner;
            public double Height;
        }
        // Greybox material swatches; hue identifies site function, never ownership.
        public static readonly Color Edge=new Color(.13f,.19f,.21f);
        public static Color Fill(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.Headquarters?new Color(.77f,.82f,.82f):kind==PlayableBuildingKind.Outpost?new Color(.59f,.73f,.71f):new Color(.80f,.72f,.51f);
        public static Pad[] Create(AntennaCityMap map,PlayableProfile profile)
        {
            return map.Sites(profile).Select(site=>{
                double radius=(site.Kind==PlayableBuildingKind.Headquarters?map.BaseYardRadius:site.Kind==PlayableBuildingKind.Outpost?map.OutpostYardRadius:map.MineYardRadius)*map.Scale*map.KeyPointPadScale;
                NavObstacle Shape(double r){
                    if(site.Kind==PlayableBuildingKind.Headquarters)return new NavObstacle(site.Position.X-r,site.Position.Z-r,site.Position.X+r,site.Position.Z+r);
                    int corners=site.Kind==PlayableBuildingKind.Outpost?4:8; // Distinct minimal planar glyphs.
                    return new NavObstacle(new NavPolygon(Enumerable.Range(0,corners).Select(i=>{double a=i*Math.PI*2/corners;return new NavPoint(site.Position.X+Math.Cos(a)*r,site.Position.Z+Math.Sin(a)*r);})));
                }
                return new Pad{SiteId=site.Id,Kind=site.Kind,Outer=Shape(radius),Inner=Shape(radius-map.KeyPointPadBorder*map.Scale),Height=map.SurfaceHeight(site.Position)};
            }).ToArray();
        }
        public static Color At(Pad pad,NavPoint point)=>pad.Inner.Contains(point)?Fill(pad.Kind):Edge;
        public static Mesh Mesh(IEnumerable<Pad> pads,bool border)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            void Triangle(Vector3 a,Vector3 b,Vector3 c){
                if(Vector3.Cross(b-a,c-a).y<0){var swap=b;b=c;c=swap;}
                int start=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
            }
            foreach(var pad in pads){
                // Small depth separation from existing road decals, not a raised gameplay level.
                Vector3 P(NavPoint p)=>new Vector3((float)p.X,(float)pad.Height+.025f,(float)p.Z);
                var inner=pad.Inner.Footprint;var outer=pad.Outer.Footprint;
                if(border){
                    for(int i=0;i<outer.Vertices.Count;i++){int j=(i+1)%outer.Vertices.Count;
                        Triangle(P(outer.Vertices[i]),P(outer.Vertices[j]),P(inner.Vertices[j]));
                        Triangle(P(outer.Vertices[i]),P(inner.Vertices[j]),P(inner.Vertices[i]));
                    }
                }else for(int i=0;i<inner.Triangles.Count;i+=3)Triangle(P(inner.Vertices[inner.Triangles[i]]),P(inner.Vertices[inner.Triangles[i+1]]),P(inner.Vertices[inner.Triangles[i+2]]));
            }
            var mesh=new Mesh{name=border?"City pad borders":"City pad fill"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
