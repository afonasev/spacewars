using System;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // One exposed face per surface: hazard paint is a material role, never a second solid.
    public static class NullDockRampMesh
    {
        public static Mesh Create(MapSupport ramp,float bottom,Color deck)
        {
            var vertices=new List<Vector3>();var colors=new List<Color>();var roles=new List<Vector2>();var indices=new List<int>();
            var warning=new Color(.86f,.62f,.19f);var b=ramp.Bounds;const double strip=.4;
            void Triangle(Vector3 a,Vector3 c,Vector3 d,bool hazard){
                if(Vector3.Cross(c-a,d-a).sqrMagnitude<1e-12f)return;
                int start=vertices.Count;vertices.Add(a);vertices.Add(c);vertices.Add(d);
                for(int i=0;i<3;i++){colors.Add(hazard?warning:deck);roles.Add(new Vector2(hazard?6:1,0));indices.Add(start+i);}
            }
            Vector3 At(NavPoint p,double height)=>new Vector3((float)p.X,(float)height,(float)p.Z);
            void Top(NavObstacle box,bool hazard){
                var p=box.Footprint.Vertices;
                var a=At(p[0],ramp.HeightAt(p[0]));var c=At(p[1],ramp.HeightAt(p[1]));
                var d=At(p[2],ramp.HeightAt(p[2]));var e=At(p[3],ramp.HeightAt(p[3]));
                Triangle(a,d,c,hazard);Triangle(a,e,d,hazard);
            }
            if(Math.Abs(ramp.Gradient.X)>0){
                Top(new NavObstacle(b.MinX,b.MinZ,b.MaxX,b.MinZ+strip),true);
                Top(new NavObstacle(b.MinX,b.MinZ+strip,b.MaxX,b.MaxZ-strip),false);
                Top(new NavObstacle(b.MinX,b.MaxZ-strip,b.MaxX,b.MaxZ),true);
            }else{
                Top(new NavObstacle(b.MinX,b.MinZ,b.MinX+strip,b.MaxZ),true);
                Top(new NavObstacle(b.MinX+strip,b.MinZ,b.MaxX-strip,b.MaxZ),false);
                Top(new NavObstacle(b.MaxX-strip,b.MinZ,b.MaxX,b.MaxZ),true);
            }
            var outline=b.Footprint.Vertices;
            Triangle(At(outline[0],bottom),At(outline[1],bottom),At(outline[2],bottom),false);
            Triangle(At(outline[0],bottom),At(outline[2],bottom),At(outline[3],bottom),false);
            for(int i=0;i<outline.Count;i++){
                var a=outline[i];var c=outline[(i+1)%outline.Count];double ah=ramp.HeightAt(a),ch=ramp.HeightAt(c);
                bool side=Math.Abs(ah-ch)>1e-6;
                // Split the actual side at deck height; the old paint prism ended here.
                double lowerA=side?0:ah,lowerC=side?0:ch;
                Triangle(At(a,bottom),At(a,lowerA),At(c,lowerC),false);
                Triangle(At(a,bottom),At(c,lowerC),At(c,bottom),false);
                if(side){Triangle(At(a,0),At(a,ah),At(c,ch),true);Triangle(At(a,0),At(c,ch),At(c,0),true);}
            }
            var mesh=new Mesh{name="Null Dock ramp without overlapping paint"};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);
            mesh.SetColors(colors);mesh.SetUVs(1,roles);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
