using System.Collections.Generic;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Reproducible approved B: hexagonal bevelled puck, recessed plate, three feet.
    // One colored mesh/material per tier; detail topology is an asset invariant.
    public static class OrbitalMineModel
    {
        public static Mesh Build(int level)
        {
            var vertices=new List<Vector3>();var colors=new List<Color>();var triangles=new List<int>();
            Color steel=new Color(.26f,.29f,.31f,0),edge=new Color(.43f,.46f,.48f,0),dark=new Color(.10f,.12f,.13f,0);
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color){int n=vertices.Count;vertices.AddRange(new[]{a,b,c,d});for(int i=0;i<4;i++)colors.Add(color);triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
            Vector3 At(int step,float radius,float y)=>new Vector3(Mathf.Cos(step*Mathf.PI/3)*radius,y,Mathf.Sin(step*Mathf.PI/3)*radius);
            void Ring(float outer,float y,float inner,float y2,Color color){for(int i=0;i<6;i++)Quad(At(i,outer,y),At(i,inner,y2),At(i+1,inner,y2),At(i+1,outer,y),color);}
            Ring(.88f,.12f,1,.3f,dark);Ring(1,.3f,1,.72f,steel);
            if(level<2){Ring(1,.72f,.83f,1,edge);Ring(.83f,1,.64f,1,steel);Ring(.64f,1,.60f,.78f,dark);Ring(.60f,.78f,0,.78f,steel);}
            else {Ring(1,.72f,.72f,.9f,edge);Ring(.72f,.9f,0,.9f,steel);}
            for(int i=0;i<3;i++){
                float a=i*Mathf.PI*2/3;Vector3 front=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),side=new Vector3(-front.z,0,front.x)*.19f;
                Vector3 x=front*.74f,y=front*1.13f;
                Quad(x-side,y-side,y+side,x+side,steel);
                Quad(x-side+Vector3.up*.32f,x+side+Vector3.up*.32f,y+side+Vector3.up*.32f,y-side+Vector3.up*.32f,edge);
                Quad(y-side,y-side+Vector3.up*.32f,y+side+Vector3.up*.32f,y+side,dark);
                Quad(x-side,x-side+Vector3.up*.32f,y-side+Vector3.up*.32f,y-side,steel);
                Quad(y+side,y+side+Vector3.up*.32f,x+side+Vector3.up*.32f,x+side,steel);
            }
            if(level==0)for(int i=0;i<6;i++){
                var center=At(i,.73f,1.015f);float radius=.04f;
                Quad(center+new Vector3(-radius,0,-radius),center+new Vector3(-radius,0,radius),center+new Vector3(radius,0,radius),center+new Vector3(radius,0,-radius),dark);
            }
            Quad(new Vector3(-.18f,.99f,-.47f),new Vector3(.18f,.99f,-.47f),new Vector3(.18f,.99f,-.40f),new Vector3(-.18f,.99f,-.40f),new Color(1,1,1,1));
            for(int i=0;i<vertices.Count;i++){var v=vertices[i];vertices[i]=new Vector3(v.x/1.15f,v.y,v.z/1.15f);}
            var mesh=new Mesh{name="Orbital mine B LOD "+level};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
