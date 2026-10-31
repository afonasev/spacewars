using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace Spacewars.Presentation
{
    // Snapshot-driven billboard mesh: no wall-time particle clock, collider or light.
    internal sealed class WreckSmokeVisuals:IDisposable
    {
        Mesh mesh;Material material;readonly MaterialPropertyBlock block=new MaterialPropertyBlock();
        public GameObject Create(Transform parent,int seed)
        {
            if(!material){
                material=new Material(Resources.Load<Shader>("WreckSmoke"));
                var v=new List<Vector3>();var uv=new List<Vector2>();var ids=new List<Vector2>();var t=new List<int>();
                // Eight puffs are fixed authored topology; controls govern their envelope.
                for(int i=0;i<8;i++){int s=v.Count;v.AddRange(new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)});uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});for(int n=0;n<4;n++)ids.Add(new Vector2(i,0));t.AddRange(new[]{s,s+1,s+2,s,s+2,s+3});}
                mesh=new Mesh{name="Wreck residual smoke"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetUVs(1,ids);mesh.SetTriangles(t,0);mesh.bounds=new Bounds(Vector3.zero,Vector3.one*128);
            }
            var g=new GameObject("Wreck residual smoke");g.transform.SetParent(parent,false);g.AddComponent<MeshFilter>().sharedMesh=mesh;var r=g.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            block.Clear();block.SetFloat("_Seed",seed);r.SetPropertyBlock(block);return g;
        }
        public void Update(GameObject g,float phase,float size,float rise,float opacity){var r=g.GetComponent<Renderer>();r.GetPropertyBlock(block);block.SetFloat("_Phase",Mathf.Clamp01(phase));block.SetFloat("_Size",size);block.SetFloat("_Rise",rise);block.SetFloat("_Opacity",opacity);r.SetPropertyBlock(block);}
        public void Dispose(){Release(mesh);Release(material);mesh=null;material=null;}
        static void Release(UnityEngine.Object o){if(!o)return;if(Application.isPlaying)UnityEngine.Object.Destroy(o);else UnityEngine.Object.DestroyImmediate(o);}
    }
}
