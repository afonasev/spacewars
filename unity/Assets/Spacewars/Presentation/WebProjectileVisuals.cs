using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace Spacewars.Presentation
{
    // Port of the procedural web meshes at e5ca56b2^: manifest.ts / renderer.ts / rocketVisual.ts.
    // Art dimensions are structural asset constants; simulation dimensions remain in PlayableProfile.
    internal sealed class WebProjectileVisuals : IDisposable
    {
        private readonly List<UnityEngine.Object> resources=new List<UnityEngine.Object>();
        private readonly Dictionary<string,Mesh> meshes=new Dictionary<string,Mesh>();
        private readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
        private Material Material(string hex,bool lit,float metal=0,float rough=.4f,bool transparent=false,bool additive=false)
        {
            string key=hex+lit+metal+rough+transparent+additive;
            if(materials.TryGetValue(key,out var found))return found;
            var m=new Material(Shader.Find(lit?"Universal Render Pipeline/Lit":"Universal Render Pipeline/Unlit"));
            ColorUtility.TryParseHtmlString(hex,out var color);m.SetColor("_BaseColor",color);
            if(lit){m.SetFloat("_Metallic",metal);m.SetFloat("_Smoothness",1-rough);}
            if(transparent){m.SetFloat("_Surface",1);m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)(additive?BlendMode.One:BlendMode.OneMinusSrcAlpha));m.SetFloat("_ZWrite",0);m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.renderQueue=3000;}
            m.SetFloat("_Cull",(float)CullMode.Off);resources.Add(m);materials.Add(key,m);return m;
        }
        // Cylinder/cone axis is +X, matching the web model and native flight tangent.
        private Mesh Axial(string key,float rear,float front,float length,int segments)
        {
            if(meshes.TryGetValue(key,out var found))return found;
            var v=new List<Vector3>();var t=new List<int>();
            for(int i=0;i<=segments;i++){float a=2*Mathf.PI*i/segments;v.Add(new Vector3(-length/2,rear*Mathf.Cos(a),rear*Mathf.Sin(a)));v.Add(new Vector3(length/2,front*Mathf.Cos(a),front*Mathf.Sin(a)));}
            for(int i=0;i<segments;i++){int n=i*2;t.AddRange(new[]{n,n+1,n+2,n+1,n+3,n+2});}
            int back=v.Count;v.Add(new Vector3(-length/2,0,0));int tip=v.Count;v.Add(new Vector3(length/2,0,0));
            for(int i=0;i<segments;i++){t.AddRange(new[]{back,i*2,(i+1)*2,tip,(i+1)*2+1,i*2+1});}
            for(int i=0;i<t.Count;i+=3){int swap=t[i+1];t[i+1]=t[i+2];t[i+2]=swap;}
            var mesh=new Mesh{name=key};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();resources.Add(mesh);meshes.Add(key,mesh);return mesh;
        }
        private GameObject Part(string name,Transform parent,Mesh mesh,Material material,float x=0)
        {
            var g=new GameObject(name);g.transform.SetParent(parent,false);g.transform.localPosition=Vector3.right*x;
            g.AddComponent<MeshFilter>().sharedMesh=mesh;var r=g.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;return g;
        }
        public GameObject Tracer(Transform parent,float length,float thickness)
        {
            const string key="explorer-box";
            if(!meshes.TryGetValue(key,out var mesh)){
                var primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);mesh=primitive.GetComponent<MeshFilter>().sharedMesh;meshes.Add(key,mesh);
                if(Application.isPlaying)UnityEngine.Object.Destroy(primitive);else UnityEngine.Object.DestroyImmediate(primitive);
            }
            var g=Part("Tracer",parent,mesh,Material("#ffd97a",false));g.transform.localScale=new Vector3(length,thickness,thickness);return g;
        }
        public GameObject Tank(Transform parent)
        {
            var g=new GameObject("Kinetic shell");g.transform.SetParent(parent,false);
            Part("kinetic-shell-body",g.transform,Axial("tank-body",.06f*.82f,.06f,.28f,10),Material("#24282d",true,.86f,.38f));
            Part("kinetic-shell-nose",g.transform,Axial("tank-nose",.06f,0,.06f*1.7f,10),Material("#b6bdc4",true,.95f,.22f),.14f+.06f*1.7f/2);
            var tail=Part("kinetic-shell-tracer",g.transform,Axial("tank-tracer",.06f*.2f,.06f*.34f,.34f,8),Material("#ff5a1f",false,transparent:true),-.14f-.17f);Opacity(tail,.9f);return g;
        }
        public GameObject Rocket(Transform parent,float radius,float length,Color owner)
        {
            var g=new GameObject("Rocket");g.transform.SetParent(parent,false);
            Part("rocket-body",g.transform,Axial("rocket-body:"+radius+":"+length,radius,radius,length*.72f,8),Material("#"+ColorUtility.ToHtmlStringRGB(owner),true,.65f,.4f));
            Part("rocket-nose",g.transform,Axial("rocket-nose:"+radius+":"+length,radius,0,length*.28f,8),Material("#30343a",true,.8f,.3f),length*.5f);
            var exhaust=new GameObject("rocket-exhaust");exhaust.transform.SetParent(g.transform,false);exhaust.transform.localPosition=Vector3.left*length*.72f/2;
            // Unit cone spans -1..0 along X: its base meets the nozzle and tip trails behind.
            var flame=Axial("exhaust",0,1,1,12);
            foreach(var part in new[]{("exhaust-edge","#ff791b"),("exhaust-core","#fff0b0")})Part(part.Item1,exhaust.transform,flame,Material(part.Item2,false,transparent:true,additive:true),-.5f);
            return g;
        }
        public void Exhaust(GameObject rocket,float length,float radius,float core,float opacity)
        {
            var exhaust=rocket.transform.Find("rocket-exhaust");
            for(int i=0;i<2;i++){var flame=exhaust.GetChild(i);float ratio=i==1?core:1;flame.localScale=new Vector3(length*ratio,radius*ratio,radius*ratio);flame.localPosition=Vector3.left*length*ratio/2;Opacity(flame.gameObject,opacity);}
        }
        public GameObject GroundCircle(Transform parent,Color color)
        {
            const string key="web-ground-impact-disc";
            if(!meshes.TryGetValue(key,out var mesh)){
                // Web CircleGeometry(1,64): filled triangle fan, not an annulus.
                const int segments=64;var v=new List<Vector3>{Vector3.zero};var t=new List<int>();
                for(int i=0;i<=segments;i++){float a=i*2*Mathf.PI/segments;v.Add(new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)));}
                for(int i=0;i<segments;i++)t.AddRange(new[]{0,i+2,i+1});
                mesh=new Mesh{name=key};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();resources.Add(mesh);meshes.Add(key,mesh);
            }
            var g=Part("Predicted filled impact",parent,mesh,Material("#"+ColorUtility.ToHtmlStringRGB(color),false,transparent:true));Opacity(g,color.a);return g;
        }
        public GameObject ImpactFlash(Transform parent)
        {
            const string key="web-impact-sphere";
            if(!meshes.TryGetValue(key,out var mesh)){
                var primitive=GameObject.CreatePrimitive(PrimitiveType.Sphere);mesh=primitive.GetComponent<MeshFilter>().sharedMesh;meshes.Add(key,mesh);
                if(Application.isPlaying)UnityEngine.Object.Destroy(primitive);else UnityEngine.Object.DestroyImmediate(primitive);
            }
            return Part("Web impact flash",parent,mesh,Material("#ffd183",false,transparent:true));
        }
        public static void Opacity(GameObject g,float alpha)
        {
            var r=g.GetComponent<Renderer>();var b=new MaterialPropertyBlock();r.GetPropertyBlock(b);var color=r.sharedMaterial.GetColor("_BaseColor");color.a=alpha;b.SetColor("_BaseColor",color);r.SetPropertyBlock(b);
        }
        public void Dispose(){foreach(var r in resources)if(Application.isPlaying)UnityEngine.Object.Destroy(r);else UnityEngine.Object.DestroyImmediate(r);resources.Clear();meshes.Clear();materials.Clear();}
    }
}
