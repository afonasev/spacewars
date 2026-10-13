using UnityEngine;

namespace Spacewars.Presentation
{
    // Neutral static scenery, never a selectable gameplay unit.
    public static class ServiceEquipmentKit
    {
        public static Mesh Build()
        {
            // User-selected authored model proportions/topology; map scale and placement are profile controls.
            var b=new EnvironmentDetails.Builder();
            var steel=new Color(.52f,.57f,.57f);var dark=new Color(.19f,.22f,.23f);var paint=new Color(.76f,.64f,.35f);
            b.Box(new Vector3(0,.46f,0),new Vector3(1.8f,.35f,3),dark);
            b.Box(new Vector3(0,.8f,-.72f),new Vector3(1.55f,.65f,1.35f),steel);
            b.Box(new Vector3(0,1.16f,-.83f),new Vector3(1.1f,.62f,.95f),paint);
            b.Box(new Vector3(0,1.23f,-1.32f),new Vector3(.85f,.3f,.025f),dark);
            b.Box(new Vector3(0,.91f,.72f),new Vector3(1.35f,.55f,1.25f),steel);
            for(int i=0;i<3;i++)b.Box(new Vector3(0,1.22f,.25f+i*.45f),new Vector3(1.4f,.035f,.08f),paint);
            foreach(float x in new[]{-.94f,.94f})foreach(float z in new[]{-1f,1f})
            {
                const int n=12;
                for(int i=0;i<n;i++)
                {
                    float a=i*Mathf.PI*2/n,c=(i+1)*Mathf.PI*2/n;
                    var p=new Vector3(x-.13f,.35f+Mathf.Sin(a)*.35f,z+Mathf.Cos(a)*.35f);
                    var q=new Vector3(x-.13f,.35f+Mathf.Sin(c)*.35f,z+Mathf.Cos(c)*.35f);
                    b.Triangle(p,q,q+Vector3.right*.26f,dark);b.Triangle(p,q+Vector3.right*.26f,p+Vector3.right*.26f,dark);
                    b.Triangle(p+Vector3.right*.26f,q+Vector3.right*.26f,new Vector3(x+.13f,.35f,z),steel);
                }
            }
            // Loading stencil belongs to this prospective equipment bay, not a travel route.
            for(int i=0;i<7;i++)b.Box(new Vector3(-1.3f,.025f,-1.6f+i*.5f),new Vector3(.16f,.015f,.3f),paint);
            b.Box(new Vector3(.2f,.024f,2.3f),new Vector3(1.7f,.012f,.5f),new Color(.28f,.25f,.20f));
            for(int i=0;i<2;i++)
            {
                b.Box(new Vector3(1.5f,.45f,.15f+i*1.15f),new Vector3(.75f,.9f,.8f),steel);
                b.Box(new Vector3(1.5f,.91f,.15f+i*1.15f),new Vector3(.82f,.05f,.85f),dark);
            }
            return b.Mesh("Parked service tug, cargo crates and loading stencil");
        }
    }
}
