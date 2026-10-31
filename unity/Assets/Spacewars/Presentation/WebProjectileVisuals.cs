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
        private readonly Dictionary<string,Material[]> loadedMaterials=new Dictionary<string,Material[]>();
        private readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
        private Material Material(string hex,bool lit,float metal=0,float rough=.4f,bool transparent=false,bool additive=false,bool launchClip=false)
        {
            string key=hex+lit+metal+rough+transparent+additive+launchClip;
            if(materials.TryGetValue(key,out var found))return found;
            var shader=launchClip?Resources.Load<Shader>("ShkvalExhaust"):Shader.Find(lit?"Universal Render Pipeline/Lit":"Universal Render Pipeline/Unlit");
            if(!shader)throw new InvalidOperationException("Missing projectile shader");var m=new Material(shader);
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
            var mesh=new Mesh{name=key};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();FinalProceduralMesh.IndexOwned(mesh);resources.Add(mesh);meshes.Add(key,mesh);return mesh;
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
                // Destroy is deferred in a Player: the mesh template must never render.
                primitive.SetActive(false);
                if(Application.isPlaying)UnityEngine.Object.Destroy(primitive);else UnityEngine.Object.DestroyImmediate(primitive);
            }
            var g=Part("Tracer",parent,mesh,Material("#ffd97a",false));g.transform.localScale=new Vector3(length,thickness,thickness);return g;
        }
        public GameObject Tank(Transform parent,bool detailed=false)
        {
            var g=new GameObject("Kinetic shell");g.transform.SetParent(parent,false);
            if(detailed){
                var art=TankMechanismProfile.Load();float length=art.shellLength;
                // Radius remains the existing shell art radius; added bands describe a sabot jacket.
                Part("Tungsten penetrator",g.transform,Axial("candidate-body:"+length,.043f,.043f,length,16),Material("#8f9da5",true,.8f,.3f));
                Part("Tungsten point",g.transform,Axial("candidate-tip",.043f,0,.14f,16),Material("#dce7e9",true,.8f,.2f),length/2+.07f);
                foreach(float x in new[]{-.12f,.04f})Part("Copper driving band",g.transform,Axial("candidate-band",.058f,.058f,.025f,16),Material("#bb7942",true,.7f,.3f),x);
                var wake=GameObject.CreatePrimitive(PrimitiveType.Quad);wake.name="Soft hot wake";
                if(Application.isPlaying)UnityEngine.Object.Destroy(wake.GetComponent<Collider>());else UnityEngine.Object.DestroyImmediate(wake.GetComponent<Collider>());
                wake.transform.SetParent(g.transform,false);wake.transform.localPosition=Vector3.left*length/2;
                wake.transform.localRotation=Quaternion.Euler(0,-90,0);wake.transform.localScale=Vector3.one*art.tracerLength;
                const string wakeKey="soft-tank-wake";
                if(!materials.TryGetValue(wakeKey,out var wakeMaterial)){wakeMaterial=new Material(Resources.Load<Shader>("TankWake"));materials.Add(wakeKey,wakeMaterial);resources.Add(wakeMaterial);}
                wake.GetComponent<Renderer>().sharedMaterial=wakeMaterial;wake.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;wake.GetComponent<Renderer>().receiveShadows=false;
                return g;
            }
            Part("kinetic-shell-body",g.transform,Axial("tank-body",.06f*.82f,.06f,.28f,10),Material("#24282d",true,.86f,.38f));
            Part("kinetic-shell-nose",g.transform,Axial("tank-nose",.06f,0,.06f*1.7f,10),Material("#b6bdc4",true,.95f,.22f),.14f+.06f*1.7f/2);
            var tail=Part("kinetic-shell-tracer",g.transform,Axial("tank-tracer",.06f*.2f,.06f*.34f,.34f,8),Material("#ff5a1f",false,transparent:true),-.14f-.17f);Opacity(tail,.9f);return g;
        }
        // Geometry invariant: nose center is .5L, its half-length is .28L / 2.
        public static float RocketTipOffset(float length)=>length*(.5f+.28f/2);
        public GameObject Rocket(Transform parent,float radius,float length,Color owner,bool detailed=false)
        {
            var g=new GameObject("Rocket");g.transform.SetParent(parent,false);
            RocketParts(g.transform,radius,length,owner,detailed);
            IgniteRocket(g,length);return g;
        }
        public void IgniteRocket(GameObject rocket,float length,bool launchClip=false)
        {
            var anchor=rocket.transform.Find("rocket-exhaust");
            if(!anchor){anchor=new GameObject("rocket-exhaust").transform;anchor.SetParent(rocket.transform,false);}
            anchor.localPosition=Vector3.left*length*.72f/2;
            if(anchor.childCount==0)
            {
                // Add flame renderers only after the loaded body has become a flying projectile.
                var flame=Axial("exhaust",0,1,1,12);
                foreach(var part in new[]{("exhaust-edge","#ff791b"),("exhaust-core","#fff0b0")})Part(part.Item1,anchor,flame,Material(part.Item2,false,transparent:true,additive:true,launchClip:launchClip),-.5f);
            }
            if(launchClip)for(int i=0;i<2;i++)anchor.GetChild(i).GetComponent<Renderer>().sharedMaterial=Material(i==0?"#ff791b":"#fff0b0",false,transparent:true,additive:true,launchClip:true);
            anchor.gameObject.SetActive(true);
        }
        private MaterialPropertyBlock exhaustClipProperties;
        public void ClipRocketExhaust(GameObject rocket,Vector4 plane,bool clip,bool visible)
        {
            if(exhaustClipProperties==null)exhaustClipProperties=new MaterialPropertyBlock();
            var anchor=rocket.transform.Find("rocket-exhaust");
            for(int i=0;i<anchor.childCount;i++){
                var r=anchor.GetChild(i).GetComponent<Renderer>();r.enabled=visible;
                r.GetPropertyBlock(exhaustClipProperties);exhaustClipProperties.SetVector("_LaunchClipPlane",plane);exhaustClipProperties.SetFloat("_LaunchClipEnabled",clip?1:0);r.SetPropertyBlock(exhaustClipProperties);
            }
        }
        private void RocketParts(Transform parent,float radius,float length,Color owner,bool detailed)
        {
            Part("rocket-body",parent,Axial("rocket-body:"+radius+":"+length,radius,radius,length*.72f,8),Material("#"+ColorUtility.ToHtmlStringRGB(owner),true,.65f,.4f));
            Part("rocket-nose",parent,Axial("rocket-nose:"+radius+":"+length,radius,0,length*.28f,8),Material("#30343a",true,.8f,.3f),length*.5f);
            if(detailed)
            {
                // Structural rocket artwork ratios; tip, center and exhaust anchor keep the flight contract.
                Part("rocket-forward-band",parent,Axial("rocket-band:"+radius+":"+length,radius*1.04f,radius*1.04f,length*.045f,16),Material("#b8c1c8",true,.8f,.3f),length*.29f);
                Part("rocket-nozzle-collar",parent,Axial("rocket-nozzle:"+radius+":"+length,radius*.72f,radius*1.03f,length*.12f,16),Material("#65727e",true,.8f,.3f),-length*.30f);
                string finKey="shkval-fins:"+radius+":"+length;
                if(!meshes.TryGetValue(finKey,out var fins))
                {
                    var vertices=new List<Vector3>();var triangles=new List<int>();
                    for(int i=0;i<4;i++){
                        float a=i*Mathf.PI/2;var radial=new Vector3(0,Mathf.Cos(a),Mathf.Sin(a));int k=vertices.Count;
                        vertices.Add(Vector3.left*length*.09f+radial*radius*.88f);
                        vertices.Add(Vector3.left*length*.30f+radial*radius*1.8f);
                        vertices.Add(Vector3.left*length*.34f+radial*radius*.88f);
                        triangles.AddRange(new[]{k,k+1,k+2});
                    }
                    fins=new Mesh{name=finKey};fins.SetVertices(vertices);fins.SetTriangles(triangles,0);fins.RecalculateNormals();fins.RecalculateBounds();meshes.Add(finKey,fins);resources.Add(fins);
                }
                Part("rocket-stabilizers",parent,fins,Material("#a1acb5",true,.75f,.35f));
            }
        }
        // Loaded copies never animate individual parts. Keep all five material submissions and
        // exact triangles/normals, sharing one combined geometry across racks and owner colors.
        public GameObject LoadedRocket(Transform parent,float radius,float length,Color owner)
        {
            string geometryKey="loaded-shkval:"+radius+":"+length;
            string materialKey=geometryKey+":"+ColorUtility.ToHtmlStringRGB(owner);
            if(!loadedMaterials.TryGetValue(materialKey,out var palette))
            {
                var template=new GameObject("Hidden loaded rocket template");template.SetActive(false);
                try
                {
                    RocketParts(template.transform,radius,length,owner,true);
                    var filters=template.GetComponentsInChildren<MeshFilter>(true);
                    palette=new Material[filters.Length];var combine=new CombineInstance[filters.Length];
                    for(int i=0;i<filters.Length;i++)
                    {
                        var f=filters[i];palette[i]=f.GetComponent<MeshRenderer>().sharedMaterial;
                        combine[i]=new CombineInstance{mesh=f.sharedMesh,subMeshIndex=0,transform=f.transform.localToWorldMatrix};
                    }
                    if(!meshes.ContainsKey(geometryKey))
                    {
                        var mesh=new Mesh{name=geometryKey};mesh.CombineMeshes(combine,false,true);resources.Add(mesh);meshes.Add(geometryKey,mesh);
                    }
                    loadedMaterials.Add(materialKey,palette);
                }
                finally{if(Application.isPlaying)UnityEngine.Object.Destroy(template);else UnityEngine.Object.DestroyImmediate(template);}
            }
            var g=Part("Loaded rocket",parent,meshes[geometryKey],palette[0]);g.GetComponent<MeshRenderer>().sharedMaterials=palette;
            // Preserve the public anchor/visibility contract without allocating hidden flame parts.
            var exhaust=new GameObject("rocket-exhaust");exhaust.transform.SetParent(g.transform,false);
            exhaust.transform.localPosition=Vector3.left*length*.72f/2;exhaust.SetActive(false);return g;
        }
        // Authored fin/nozzle proportions preserve the existing missile envelope and tip contact.
        public GameObject HawkRocket(Transform parent,float radius,float length,Color owner)
        {
            var rocket=Rocket(parent,radius,length,owner);
            Part("hawk-nozzle-rim",rocket.transform,Axial("hawk-nozzle:"+radius+":"+length,radius*.85f,radius*.85f,length*.055f,16),Material("#7a858f",true,.8f,.3f),-length*.35f);
            string key="hawk-fin:"+radius+":"+length;
            if(!meshes.TryGetValue(key,out var fin))
            {
                fin=new Mesh{name=key};fin.vertices=new[]{new Vector3(-length*.30f,radius,0),new Vector3(-length*.29f,radius*1.9f,0),new Vector3(-length*.04f,radius,0)};fin.triangles=new[]{0,1,2,2,1,0};fin.RecalculateNormals();fin.RecalculateBounds();resources.Add(fin);meshes.Add(key,fin);
            }
            for(int i=0;i<4;i++){var part=Part("hawk-fin",rocket.transform,fin,Material("#394652",true,.6f,.35f));part.transform.localRotation=Quaternion.Euler(i*90,0,0);}
            Part("hawk-band",rocket.transform,Axial("hawk-band:"+radius+":"+length,radius*1.01f,radius*1.01f,length*.04f,16),Material("#d79a40",true,.4f,.4f),length*.12f);
            return rocket;
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
                // Destroy is deferred in a Player: the mesh template must never render.
                primitive.SetActive(false);
                if(Application.isPlaying)UnityEngine.Object.Destroy(primitive);else UnityEngine.Object.DestroyImmediate(primitive);
            }
            return Part("Web impact flash",parent,mesh,Material("#ffd183",false,transparent:true));
        }
        private readonly DetailedImpactVisuals detailedImpact=new DetailedImpactVisuals();
        public GameObject DirectImpact(Transform parent,int seed=0,bool tankBurst=false)
        {
            var root=detailedImpact.Create(parent,seed);
            if(tankBurst)foreach(var renderer in root.GetComponentsInChildren<Renderer>())
            {var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);block.SetFloat("_TankBurst",1);renderer.SetPropertyBlock(block);}
            return root;
        }
        public void UpdateDirectImpact(GameObject root,float age,float radius,float sparkLength,float width,float glow,float smoke)
            =>detailedImpact.Update(root,age,radius,sparkLength,width,glow,smoke);
        public static void Opacity(GameObject g,float alpha)
        {
            var r=g.GetComponent<Renderer>();var b=new MaterialPropertyBlock();r.GetPropertyBlock(b);var color=r.sharedMaterial.GetColor("_BaseColor");color.a=alpha;b.SetColor("_BaseColor",color);r.SetPropertyBlock(b);
        }
        public void Dispose(){detailedImpact.Dispose();foreach(var r in resources)if(Application.isPlaying)UnityEngine.Object.Destroy(r);else UnityEngine.Object.DestroyImmediate(r);resources.Clear();meshes.Clear();materials.Clear();loadedMaterials.Clear();}
    }
}
