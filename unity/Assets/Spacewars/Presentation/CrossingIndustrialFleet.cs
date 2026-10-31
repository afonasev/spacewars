using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Authored local model coordinates and topology, not simulation/balance values.
    // Positive Z is the tool end. All tiers retain the hull, tool and contact points.
    public static class CrossingIndustrialFleet
    {
        public static readonly string[] Kinds={"AutonomousQuarryModule","ColonyTractor","WalkingLoader"};
        public static bool IsVehicle(string kind)=>Array.IndexOf(Kinds,kind)>=0;
        static Color Paint=new Color(.55f,.36f,.14f,0),Faded=new Color(.60f,.51f,.32f,0),Steel=new Color(.32f,.37f,.39f,1f/3),Bright=new Color(.54f,.58f,.59f,1f/3),Dark=new Color(.10f,.13f,.14f,2f/3),Glass=new Color(.13f,.25f,.27f,1);
        sealed class Builder
        {
            readonly List<Vector3> v=new List<Vector3>();readonly List<Color> c=new List<Color>();readonly List<Vector4> uv=new List<Vector4>();readonly List<int> ix=new List<int>();
            void Tri(Vector3 a,Vector3 b,Vector3 d,Vector2 ua,Vector2 ub,Vector2 ud,float w,float h,Color color)
            {int n=v.Count;v.Add(a);v.Add(b);v.Add(d);for(int j=0;j<3;j++){c.Add(color);ix.Add(n+j);}uv.Add(new Vector4(ua.x,ua.y,w,h));uv.Add(new Vector4(ub.x,ub.y,w,h));uv.Add(new Vector4(ud.x,ud.y,w,h));}
            public void Face(Vector3 a,Vector3 b,Vector3 d,Vector3 e,Color color)
            {float w=Mathf.Max(Vector3.Distance(a,e),Vector3.Distance(b,d)),h=Mathf.Max(Vector3.Distance(a,b),Vector3.Distance(e,d));Tri(a,b,d,Vector2.zero,new Vector2(0,h),new Vector2(w,h),w,h,color);Tri(a,d,e,Vector2.zero,new Vector2(w,h),new Vector2(w,0),w,h,color);}
            public void Box(Vector3 center,Vector3 size,Color color,Quaternion rotation=default)
            {
                if(rotation==default)rotation=Quaternion.identity;
                var p=new Vector3[8];for(int i=0;i<8;i++)p[i]=center+rotation*Vector3.Scale(size*.5f,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                int[] f={0,2,3,1,4,5,7,6,0,4,6,2,1,3,7,5,2,6,7,3,0,1,5,4};for(int i=0;i<f.Length;i+=4)Face(p[f[i]],p[f[i+1]],p[f[i+2]],p[f[i+3]],color);
            }
            // Beveled eight-sided shell with an inset roof: large slopes survive far LOD.
            public void Hull(Vector3 center,Vector3 size,float inset,Color color)
            {
                Vector2[] ring={new Vector2(-.36f,-.5f),new Vector2(.36f,-.5f),new Vector2(.5f,-.36f),new Vector2(.5f,.36f),new Vector2(.36f,.5f),new Vector2(-.36f,.5f),new Vector2(-.5f,.36f),new Vector2(-.5f,-.36f)};
                var low=new Vector3[8];var high=new Vector3[8];for(int i=0;i<8;i++){low[i]=center+new Vector3(ring[i].x*size.x,-size.y/2,ring[i].y*size.z);high[i]=center+new Vector3(ring[i].x*(size.x-inset),size.y/2,ring[i].y*(size.z-inset));}
                for(int i=0;i<8;i++){int j=(i+1)%8;Face(low[i],high[i],high[j],low[j],color);}
                // Planar top UVs remain continuous across the fan triangles.
                Vector2 UV(Vector3 p)=>new Vector2(p.x-center.x+size.x/2,p.z-center.z+size.z/2);
                for(int i=1;i<7;i++)Tri(high[0],high[i+1],high[i],UV(high[0]),UV(high[i+1]),UV(high[i]),size.x,size.z,color);
                for(int i=1;i<7;i++)Tri(low[0],low[i],low[i+1],UV(low[0]),UV(low[i]),UV(low[i+1]),size.x,size.z,Dark);
            }
            public void Cylinder(Vector3 a,Vector3 z,float radius,Color color,int segments)
            {
                var q=Quaternion.FromToRotation(Vector3.forward,(z-a).normalized);var mid=(a+z)/2;
                for(int i=0;i<segments;i++)
                {
                    float t=i*Mathf.PI*2/segments,u=(i+1)*Mathf.PI*2/segments;var r=q*new Vector3(Mathf.Cos(t),Mathf.Sin(t),0)*radius;var s=q*new Vector3(Mathf.Cos(u),Mathf.Sin(u),0)*radius;
                    Face(a+r,a+s,z+s,z+r,color);
                    Tri(a,a+s,a+r,Vector2.one*radius,new Vector2(radius*2,radius),Vector2.zero,radius*2,radius*2,color);
                    Tri(z,z+r,z+s,Vector2.one*radius,Vector2.zero,new Vector2(radius*2,radius),radius*2,radius*2,color);
                }
            }
            public void Beam(Vector3 a,Vector3 z,float w,float d,Color color)=>Box((a+z)/2,new Vector3(w,(z-a).magnitude,d),color,Quaternion.FromToRotation(Vector3.up,z-a));
            public Mesh Mesh(string name)
            {var m=new Mesh{name=name};m.SetVertices(v);m.SetColors(c);m.SetUVs(0,uv);m.SetTriangles(ix,0);m.RecalculateNormals();m.RecalculateBounds();FinalProceduralMesh.IndexOwned(m);return m;}
        }
        public static Mesh Build(string kind,int tier)
        {
            if(!IsVehicle(kind))throw new ArgumentException(kind);if(tier<0||tier>2)throw new ArgumentOutOfRangeException(nameof(tier));
            var b=new Builder();int round=tier==0?12:tier==1?8:6;
            void Box(float x,float y,float z,float w,float h,float d,Color c)=>b.Box(new Vector3(x,y,z),new Vector3(w,h,d),c);
            void Shell(float x,float y,float z,float w,float h,float d,float bevel,Color c)=>b.Hull(new Vector3(x,y,z),new Vector3(w,h,d),bevel,c);
            void Bolt(float x,float y,float z)=>b.Cylinder(new Vector3(x,y,z),new Vector3(x,y+.035f,z),.045f,Bright,6);
            void Ram(Vector3 a,Vector3 z,float r)
            {var mid=Vector3.Lerp(a,z,.60f);b.Cylinder(a,mid,r,Steel,round);b.Cylinder(mid,z,r*.52f,Bright,round);if(tier<2)b.Cylinder(mid-((z-a).normalized*.07f),mid,.13f,Dark,round);}
            void Tracks(float x,float z,float length,float width)
            {
                // Continuous capsule tread, steel grouser plates and visible recessed wheels.
                float r=.41f,y=.51f,straight=length/2-r;
                int arc=tier==0?6:tier==1?4:2;
                var ring=new List<Vector2>();for(int i=0;i<=arc;i++){float t=-Mathf.PI/2+i*Mathf.PI/arc;ring.Add(new Vector2(straight+Mathf.Cos(t)*r,y+Mathf.Sin(t)*r));}
                for(int i=0;i<=arc;i++){float t=Mathf.PI/2+i*Mathf.PI/arc;ring.Add(new Vector2(-straight+Mathf.Cos(t)*r,y+Mathf.Sin(t)*r));}
                for(int i=0;i<ring.Count;i++){var a=ring[i];var e=ring[(i+1)%ring.Count];b.Face(new Vector3(x-width/2,a.y,a.x+z),new Vector3(x+width/2,a.y,a.x+z),new Vector3(x+width/2,e.y,e.x+z),new Vector3(x-width/2,e.y,e.x+z),Steel);}
                Box(x,y,z,width*.78f,.60f,length-.32f,Dark);
                if(tier<2)
                {
                    int n=tier==0?Mathf.CeilToInt(length/.24f):Mathf.CeilToInt(length/.50f);
                    for(int i=0;i<n;i++){float at=z-straight+i*2*straight/(n-1);Box(x,.94f,at,width+.025f,.065f,.09f,Bright);Box(x,.075f,at,width+.025f,.055f,.09f,Steel);}
                    for(int i=0;i<(tier==0?4:2);i++){float at=z-straight+i*2*straight/(tier==0?3:1);b.Cylinder(new Vector3(x-width*.52f,y,at),new Vector3(x+width*.52f,y,at),.27f,Steel,round);}
                }
                Shell(x,.97f,z,width*.75f,.18f,length*.76f,.10f,Paint);
            }
            void DeckDetails(float width,float rear,float top)
            {
                Box(0,top,rear,width*.67f,.055f,.62f,Dark);
                if(tier<2)for(int i=0;i<(tier==0?9:4);i++)Box(-width*.28f+i*width*.56f/(tier==0?8:3),top+.04f,rear,.045f,.045f,.54f,Steel);
                if(tier==0)foreach(int side in new[]{-1,1}){Bolt(side*width*.37f,top,rear+.42f);Bolt(side*width*.37f,top,rear-.42f);Box(side*width*.38f,top+.05f,rear,.055f,.06f,.34f,Bright);}
            }
            void Blade(float width,float at)
            {
                foreach(int side in new[]{-1,1})
                {
                    float x=side*(width*.25f+.035f);b.Box(new Vector3(x,.65f,at),new Vector3(width*.49f,1.02f,.20f),Steel,Quaternion.Euler(-12,side*-5,0));
                    Box(x,.16f,at+.13f,width*.49f,.13f,.27f,Bright);
                    b.Cylinder(new Vector3(side*.48f,.64f,at-.20f),new Vector3(side*.48f,.98f,at-.20f),.12f,Dark,round);
                    Ram(new Vector3(side*.88f,1.0f,.6f),new Vector3(side*1.15f,.56f,at-.10f),.115f);
                    if(tier<2)for(int i=0;i<3;i++)Box(x-width*.19f+i*width*.19f,.62f,at-.18f,.09f,.79f,.12f,Paint);
                }
            }
            if(kind=="AutonomousQuarryModule")
            {
                Tracks(-1.15f,-.15f,3.95f,.72f);Tracks(1.15f,-.15f,3.95f,.72f);
                Shell(0,.95f,-.25f,1.92f,.65f,3.45f,.22f,Paint);
                Shell(0,1.52f,-1.03f,1.76f,.70f,1.50f,.25f,Faded);
                Shell(0,1.37f,.45f,1.62f,.48f,1.48f,.35f,Paint);
                Box(0,1.43f,1.16f,1.05f,.15f,.055f,Glass);
                DeckDetails(1.65f,-1.03f,1.89f);Blade(3.50f,2.55f);
                if(tier==0)foreach(int side in new[]{-1,1}){Box(side*.7f,1.67f,.37f,.055f,.06f,.48f,Steel);Bolt(side*.65f,1.64f,.85f);}
            }
            else if(kind=="ColonyTractor")
            {
                foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1}){Tracks(side*1.23f,end*1.13f,1.91f,.79f);b.Cylinder(new Vector3(side*.76f,.58f,end*1.13f),new Vector3(side*1.23f,.58f,end*1.13f),.20f,Steel,round);}
                Shell(0,.98f,0,2.00f,.55f,3.55f,.25f,Paint);
                Shell(0,1.48f,-1.17f,1.88f,.65f,1.3f,.22f,Faded);DeckDetails(1.8f,-1.17f,1.82f);
                Shell(0,1.56f,.40f,1.86f,1.05f,1.92f,.55f,Paint);
                b.Box(new Vector3(0,1.80f,1.20f),new Vector3(1.18f,.28f,.055f),Glass,Quaternion.Euler(-17,0,0));
                foreach(int side in new[]{-1,1})Box(side*.77f,1.80f,.35f,.035f,.25f,.69f,Glass);
                Box(0,2.10f,.30f,1.14f,.07f,.66f,Faded);Blade(3.75f,2.59f);
                if(tier<2)foreach(int side in new[]{-1,1}){Ram(new Vector3(side*.94f,.87f,-.6f),new Vector3(side*.94f,1.3f,.62f),.10f);Box(side*.55f,1.36f,1.29f,.17f,.11f,.06f,Faded);}
                if(tier==0)foreach(int side in new[]{-1,1}){Bolt(side*.57f,2.14f,.08f);Bolt(side*.57f,2.14f,.54f);}
            }
            else
            {
                Shell(0,1.43f,-.12f,2.15f,.75f,2.45f,.34f,Paint);
                Shell(0,1.94f,-.35f,1.7f,.38f,1.65f,.25f,Faded);DeckDetails(1.65f,-.70f,2.15f);
                Box(0,1.72f,1.08f,1.1f,.22f,.06f,Glass);
                foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1})
                {
                    var hip=new Vector3(side*.94f,1.51f,end*.77f);var knee=new Vector3(side*1.59f,1.02f,end*1.24f);var foot=new Vector3(side*1.71f,.20f,end*1.48f);
                    b.Cylinder(hip-new Vector3(.12f,0,0),hip+new Vector3(.12f,0,0),.32f,Steel,round);
                    b.Beam(hip,knee,.42f,.53f,Paint);b.Beam(knee,foot,.29f,.36f,Faded);
                    b.Cylinder(knee-new Vector3(.18f,0,0),knee+new Vector3(.18f,0,0),.23f,Steel,round);
                    Shell(foot.x,.12f,foot.z,.72f,.24f,.94f,.10f,Steel);
                    if(tier<2)Ram(hip+new Vector3(0,.10f,.13f),foot+new Vector3(0,.16f,.13f),.075f);
                }
                foreach(int side in new[]{-1,1})
                {
                    var a=new Vector3(side*.54f,1.25f,.80f);var elbow=new Vector3(side*.85f,.83f,1.82f);var tip=new Vector3(side*.65f,.33f,2.58f);
                    b.Beam(a,elbow,.26f,.32f,Paint);b.Beam(elbow,tip,.22f,.25f,Steel);Box(side*.56f,.24f,2.62f,.47f,.30f,.50f,Bright);
                    b.Cylinder(elbow-new Vector3(.12f,0,0),elbow+new Vector3(.12f,0,0),.20f,Steel,round);
                    if(tier<2)Ram(a+new Vector3(0,.19f,0),tip+new Vector3(0,.18f,-.13f),.085f);
                }
                if(tier==0)foreach(int side in new[]{-1,1}){Bolt(side*.62f,2.15f,.16f);Box(side*.61f,2.16f,-.12f,.055f,.07f,.32f,Bright);}
            }
            return b.Mesh("Crossing fleet "+kind+" LOD"+tier);
        }
    }
}
