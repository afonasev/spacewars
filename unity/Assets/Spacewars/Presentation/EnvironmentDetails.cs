using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Reusable authored module geometry. All coordinates are local; no map or collider dependency.
    public static class EnvironmentDetails
    {
        public sealed class Builder
        {
            readonly List<Vector3> vertices=new List<Vector3>();
            readonly List<int> triangles=new List<int>();
            readonly List<Color> colors=new List<Color>();
            public void Triangle(Vector3 a,Vector3 b,Vector3 c,Color tint)
            {int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);colors.Add(tint);colors.Add(tint);colors.Add(tint);triangles.Add(i);triangles.Add(i+1);triangles.Add(i+2);}
            public void Box(Vector3 center,Vector3 size,Color tint)
            {
                var p=new Vector3[8];for(int i=0;i<8;i++)p[i]=center+Vector3.Scale(size*.5f,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                int[] faces={0,2,3,1,4,5,7,6,0,4,6,2,1,3,7,5,2,6,7,3,0,1,5,4};
                for(int i=0;i<faces.Length;i+=4){Triangle(p[faces[i]],p[faces[i+1]],p[faces[i+2]],tint);Triangle(p[faces[i]],p[faces[i+2]],p[faces[i+3]],tint);}
            }
            public void Append(Mesh mesh,Matrix4x4 transform)
            {
                int offset=vertices.Count;var source=mesh.vertices;var tint=mesh.colors;
                for(int i=0;i<source.Length;i++){vertices.Add(transform.MultiplyPoint3x4(source[i]));colors.Add(tint.Length==source.Length?tint[i]:Color.white);}
                foreach(int i in mesh.triangles)triangles.Add(offset+i);
            }
            public Mesh Mesh(string name)
            {var mesh=new Mesh{name=name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetColors(colors);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;}
        }
        public static Mesh Boulder(int variant)
        {
            // Fixed topology and ratios describe the authored module, not gameplay tuning.
            var b=new Builder();const int sides=9;var rings=new Vector3[3,sides];
            for(int j=0;j<3;j++)for(int i=0;i<sides;i++)
            {
                float a=(i+.12f*j)*Mathf.PI*2/sides;
                float r=(j==0?.83f:j==1?1:.57f)*(.84f+.16f*Mathf.Sin(i*7.3f+variant*2.1f));
                rings[j,i]=new Vector3(Mathf.Cos(a)*r,j==0?-.13f:j==1?.24f:.72f+.10f*Mathf.Sin(i*3.7f+variant),Mathf.Sin(a)*r*.77f);
            }
            for(int i=0;i<sides;i++)
            {
                int n=(i+1)%sides;for(int j=0;j<2;j++){b.Triangle(rings[j,i],rings[j+1,i],rings[j+1,n],Color.white);b.Triangle(rings[j,i],rings[j+1,n],rings[j,n],Color.white);}
                b.Triangle(rings[2,i],new Vector3(.12f,.86f,-.08f),rings[2,n],Color.white);
            }
            return b.Mesh("Fractured boulder "+variant);
        }
        public static Mesh Service(string kind)
        {
            var b=new Builder();var casing=new Color(.56f,.62f,.62f);var dark=new Color(.19f,.23f,.25f);var edge=new Color(.73f,.69f,.55f);
            b.Box(new Vector3(0,.12f,0),new Vector3(2.5f,.24f,1.7f),dark);
            if(kind=="PumpNode")
            {
                b.Box(new Vector3(-.45f,.67f,0),new Vector3(1.25f,1.1f,1.2f),casing);
                b.Box(new Vector3(.65f,.48f,0),new Vector3(.75f,.6f,.95f),edge);
                b.Box(new Vector3(0,.28f,-.71f),new Vector3(2.25f,.23f,.22f),dark);
                b.Box(new Vector3(.8f,.28f,.85f),new Vector3(.26f,.26f,1.25f),casing);
            }
            else
            {
                float height=kind=="CableCabinet"?1.45f:.9f;
                b.Box(new Vector3(0,.24f+height/2,0),new Vector3(2.1f,height,1.3f),casing);
                b.Box(new Vector3(0,.27f+height,0),new Vector3(2.26f,.12f,1.46f),edge);
                for(int i=0;i<6;i++)b.Box(new Vector3(0,.43f+i*.10f,-.667f),new Vector3(1.65f,.045f,.035f),dark);
                b.Box(new Vector3(.8f,.98f,-.69f),new Vector3(.055f,.23f,.06f),edge);
                // Tiny painted status lens, deliberately non-emissive and unlike faction colors.
                b.Box(new Vector3(-.78f,1.00f,-.69f),new Vector3(.10f,.055f,.035f),new Color(.56f,.44f,.18f));
            }
            return b.Mesh(kind);
        }
    }
}
