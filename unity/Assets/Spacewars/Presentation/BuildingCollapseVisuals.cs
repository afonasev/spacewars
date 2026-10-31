using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spacewars.Presentation
{
    // Authored billboard particles, evaluated from snapshot phase. No independent
    // particle-system clock, colliders, lights or allocations per particle/frame.
    internal sealed class BuildingCollapseVisuals : IDisposable
    {
        private Mesh mesh;
        private Material material;
        private readonly MaterialPropertyBlock block=new MaterialPropertyBlock();
        public GameObject Create(Transform parent,int seed)
        {
            if(!material)
            {
                var shader=Resources.Load<Shader>("BuildingCollapse");
                if(!shader||!shader.isSupported)throw new InvalidOperationException("BuildingCollapse shader unavailable.");
                material=new Material(shader){name="Concept A collapse"};
                var vertices=new List<Vector3>();var uv=new List<Vector2>();var seeds=new List<Vector2>();var triangles=new List<int>();
                // Particle layout/counts are fixed authored VFX content.
                for(int particle=0;particle<35;particle++)
                {
                    int start=vertices.Count;vertices.AddRange(new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)});
                    uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
                    for(int v=0;v<4;v++)seeds.Add(new Vector2(particle<8?0:particle<24?1:particle<32?2:3,particle));
                    triangles.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
                }
                mesh=new Mesh{name="Collapse flame smoke and sparks"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetUVs(1,seeds);mesh.SetTriangles(triangles,0);
                mesh.bounds=new Bounds(Vector3.zero,Vector3.one*128);
            }
            var obj=new GameObject("Collapse fire and dust");obj.transform.SetParent(parent,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            block.Clear();block.SetFloat("_Seed",seed*.37f);renderer.SetPropertyBlock(block);return obj;
        }
        public void Update(GameObject obj,float phase,float radius,float roof,float fire,float smoke)
        {
            var renderer=obj.GetComponent<Renderer>();renderer.GetPropertyBlock(block);block.SetFloat("_Phase",phase);block.SetFloat("_Radius",radius);
            block.SetFloat("_Roof",roof);block.SetFloat("_FireScale",fire);block.SetFloat("_SmokeOpacity",smoke);renderer.SetPropertyBlock(block);
        }
        public void Dispose(){Release(mesh);Release(material);mesh=null;material=null;}
        private static void Release(UnityEngine.Object obj){if(!obj)return;if(Application.isPlaying)UnityEngine.Object.Destroy(obj);else UnityEngine.Object.DestroyImmediate(obj);}
    }
}
