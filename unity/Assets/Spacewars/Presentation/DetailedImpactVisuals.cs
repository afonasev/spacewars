using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spacewars.Presentation
{
    // Shared procedural particle meshes: two draws/contact, no particle objects or
    // independent clock. UV1 stores authored particle kind/seed, never gameplay data.
    internal sealed class DetailedImpactVisuals : IDisposable
    {
        private Mesh hotMesh,smokeMesh;
        private Material hot,smoke;
        private MaterialPropertyBlock block;
        private Mesh Particles(bool clouds)
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var particles=new List<Vector2>();var triangles=new List<int>();
            void Quad(float mode,float seed)
            {
                int start=vertices.Count;
                vertices.AddRange(new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)});
                uv.AddRange(new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)});
                for(int i=0;i<4;i++)particles.Add(new Vector2(mode,seed));
                triangles.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
            }
            // Particle count/fan breakup is authored VFX content, like mesh topology.
            if(clouds){Quad(4,3);Quad(5,19);Quad(4,31);}
            else{Quad(0,0);Quad(1,1);for(int i=0;i<14;i++)Quad(2,i*7+2);for(int i=0;i<7;i++)Quad(3,i*11+4);}
            var mesh=new Mesh{name=clouds?"Armour wisps":"Hot-metal core sparks and embers"};
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetUVs(1,particles);mesh.SetTriangles(triangles,0);
            // Vertex shader expands particles beyond their source quads. Conservative
            // bounds cover the legal profile ratios, avoiding camera-edge culling.
            mesh.bounds=new Bounds(Vector3.zero,Vector3.one*128);return mesh;
        }
        private void Initialize()
        {
            if(hot)return;
            // This factory is a MonoBehaviour field: native Unity objects are created
            // lazily on the render thread, never in the owning component constructor.
            block=new MaterialPropertyBlock();
            var shader=Resources.Load<Shader>("DirectImpact");if(!shader||!shader.isSupported)throw new InvalidOperationException("Missing/supported DirectImpact shader required.");
            hot=new Material(shader){name="Detailed hot metal"};hot.SetFloat("_DstBlend",(float)BlendMode.One);
            smoke=new Material(shader){name="Faint armour smoke"};smoke.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            hotMesh=Particles(false);smokeMesh=Particles(true);
        }
        public GameObject Create(Transform parent,int seed)
        {
            Initialize();var root=new GameObject("Detailed armour impact");root.transform.SetParent(parent,false);
            Make("Hot core sparks embers",hotMesh,hot);Make("Faint smoke",smokeMesh,smoke);
            // Stable contact variation is copied into both renderers once; all later
            // animation is evaluated solely from the authoritative snapshot age.
            foreach(var renderer in root.GetComponentsInChildren<Renderer>()){block.Clear();block.SetFloat("_Seed",seed*.618f);renderer.SetPropertyBlock(block);}
            return root;
            void Make(string name,Mesh mesh,Material material)
            {
                var part=new GameObject(name);part.transform.SetParent(root.transform,false);part.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=part.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            }
        }
        public void Update(GameObject root,float age,float radius,float length,float width,float glow,float smokeOpacity)
        {
            foreach(Transform part in root.transform)
            {
                part.localScale=Vector3.one*radius;
                var renderer=part.GetComponent<Renderer>();renderer.GetPropertyBlock(block);
                block.SetFloat("_Phase",age);block.SetFloat("_SparkLength",length/radius);block.SetFloat("_SparkWidth",width);
                block.SetFloat("_Intensity",glow);block.SetFloat("_SmokeOpacity",smokeOpacity);renderer.SetPropertyBlock(block);
            }
        }
        public void Dispose(){Release(hot);Release(smoke);Release(hotMesh);Release(smokeMesh);hot=null;smoke=null;hotMesh=null;smokeMesh=null;}
        private static void Release(UnityEngine.Object value){if(!value)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
    }
}
