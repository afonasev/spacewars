using System;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Authored local geometry and fixed construction proportions. Scale is profile-owned.
    // Broken machinery is scenery, never a simulation entity, collider or selectable unit.
    public static class HeavyTractorWreck
    {
        private static readonly Color Tire=new Color(.11f,.12f,.13f),Steel=new Color(.72f,.76f,.76f),Dark=new Color(.28f,.31f,.32f),Paint=new Color(.92f,.68f,.28f),Glass=new Color(.08f,.15f,.18f);
        private static void Wheel(EnvironmentDetails.Builder b,Vector3 center,bool broken)
        {
            const int sides=24;const float radius=1.08f,halfWidth=.38f;
            var lug=new EnvironmentDetails.Builder();lug.Box(Vector3.zero,new Vector3(.73f,.10f,.19f),Dark);var treadMesh=lug.Mesh("Tire lug");
            for(int i=0;i<sides;i++)
            {
                float a=i*Mathf.PI*2/sides,n=(i+1)*Mathf.PI*2/sides;
                Vector3 P(float x,float angle,float r)=>center+new Vector3(x,Mathf.Cos(angle)*r,Mathf.Sin(angle)*r);
                var p=P(-halfWidth,a,radius);var q=P(halfWidth,a,radius);var r=P(halfWidth,n,radius);var t=P(-halfWidth,n,radius);
                b.Triangle(p,r,q,Tire);b.Triangle(p,t,r,Tire);
                foreach(int side in new[]{-1,1})
                {
                    float x=side*halfWidth;
                    float first=side>0?n:a,second=side>0?a:n;
                    b.Triangle(P(x,first,radius),P(x,second,radius),center+new Vector3(x,0,0),Tire);
                    b.Triangle(P(x+.025f*side,first,.46f),P(x+.025f*side,second,.46f),center+new Vector3(x+.025f*side,0,0),Steel);
                }
                // Angled raised lugs, rather than smooth toy wheels.
                var transform=Matrix4x4.TRS(center+new Vector3(0,Mathf.Cos(a)*1.11f,Mathf.Sin(a)*1.11f),Quaternion.Euler(a*Mathf.Rad2Deg,0,12),Vector3.one);
                b.Append(treadMesh,transform);
            }
            UnityEngine.Object.Destroy(treadMesh);
            if(broken)b.Box(center+new Vector3(.46f,.16f,0),new Vector3(.13f,.45f,.85f),Dark);
        }
        private static void BevelHull(EnvironmentDetails.Builder b,Vector3 center,Vector3 size,Color tint)
        {
            var bottom=new[]{new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,.5f),new Vector3(-.5f,-.5f,.5f)};
            var top=new[]{new Vector3(-.36f,.5f,-.42f),new Vector3(.36f,.5f,-.42f),new Vector3(.36f,.5f,.42f),new Vector3(-.36f,.5f,.42f)};
            for(int i=0;i<4;i++){int j=(i+1)%4;var a=center+Vector3.Scale(bottom[i],size);var d=center+Vector3.Scale(bottom[j],size);var c=center+Vector3.Scale(top[j],size);var e=center+Vector3.Scale(top[i],size);b.Triangle(a,e,c,tint);b.Triangle(a,c,d,tint);}
            b.Triangle(center+Vector3.Scale(top[0],size),center+Vector3.Scale(top[2],size),center+Vector3.Scale(top[1],size),tint);
            b.Triangle(center+Vector3.Scale(top[0],size),center+Vector3.Scale(top[3],size),center+Vector3.Scale(top[2],size),tint);
        }
        public static Mesh Build()
        {
            var b=new EnvironmentDetails.Builder();
            b.Box(new Vector3(0,.72f,0),new Vector3(2.35f,.52f,9.6f),Dark);
            BevelHull(b,new Vector3(0,1.35f,2.45f),new Vector3(2.7f,1.3f,4.5f),Paint);
            BevelHull(b,new Vector3(-.12f,2.7f,3.35f),new Vector3(2.5f,2.1f,2.1f),Steel);
            b.Box(new Vector3(-.12f,3.01f,4.27f),new Vector3(1.85f,.67f,.08f),Glass);
            b.Box(new Vector3(-1.15f,2.95f,3.28f),new Vector3(.06f,.69f,1.2f),Glass);
            // Offset rear power module, exposed articulation and torn panels make damage legible.
            BevelHull(b,new Vector3(.22f,1.45f,-2.85f),new Vector3(2.5f,1.2f,3.6f),Dark);
            b.Box(new Vector3(0,1.83f,-3.2f),new Vector3(2.65f,.18f,2.3f),Paint);
            for(int i=0;i<7;i++)b.Box(new Vector3(.23f,1.05f,-4.62f+i*.24f),new Vector3(2.05f,.13f,.08f),Steel);
            for(int side=-1;side<=1;side+=2)
            {
                foreach(float z in new[]{3.45f,1.05f,-2.05f,-4.05f})Wheel(b,new Vector3(side*1.4f,1.08f,z),side==1&&z< -3);
                b.Box(new Vector3(side*1.26f,1.85f,-.65f),new Vector3(.15f,.13f,2.25f),Steel);
                b.Box(new Vector3(side*1.36f,2.33f,2.8f),new Vector3(.10f,.12f,1.5f),Paint);
                b.Box(new Vector3(side*.9f,1.07f,4.82f),new Vector3(.39f,.43f,.28f),Steel);
            }
            b.Box(new Vector3(0,1.35f,5.03f),new Vector3(2.7f,.58f,.28f),Dark);
            b.Box(new Vector3(0,1.01f,-5.15f),new Vector3(1.2f,.38f,.75f),Steel);
            b.Box(new Vector3(.74f,3.4f,2.01f),new Vector3(.24f,1.45f,.24f),Dark);
            // One fallen armour plate and open cab scar, with all debris inside the same envelope.
            var plate=new EnvironmentDetails.Builder();plate.Box(Vector3.zero,new Vector3(1.75f,.14f,1.4f),Paint);var panel=plate.Mesh("Torn panel");b.Append(panel,Matrix4x4.TRS(new Vector3(-1.85f,.12f,-.3f),Quaternion.Euler(0,24,12),Vector3.one));UnityEngine.Object.Destroy(panel);
            b.Box(new Vector3(.55f,2.66f,4.35f),new Vector3(.57f,.74f,.13f),Dark);
            return b.Mesh("Broken frontier heavy tractor");
        }
        public static float FootprintRadius(float scale)
        {
            var mesh=Build();float radius=0;
            foreach(var v in mesh.vertices)radius=Mathf.Max(radius,new Vector2(v.x,v.z).magnitude*scale);
            UnityEngine.Object.Destroy(mesh);return radius;
        }
    }
}
