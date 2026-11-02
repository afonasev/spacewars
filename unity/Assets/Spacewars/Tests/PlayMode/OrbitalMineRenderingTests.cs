using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Tests.PlayMode
{
    public sealed class OrbitalMineRenderingTests
    {
        [Test] public void ModelTiersReduceGeometryAndBurstNeverExceedsDamageRadius()
        {
            var meshes=Enumerable.Range(0,3).Select(OrbitalMineModel.Build).ToArray();var effect=new Mesh();
            try{
                Assert.Greater(meshes[0].vertexCount,meshes[1].vertexCount);Assert.Greater(meshes[1].vertexCount,meshes[2].vertexCount);
                foreach(var mesh in meshes){Assert.AreEqual(1,mesh.subMeshCount);Assert.LessOrEqual(mesh.bounds.max.y,1.02f);Assert.True(mesh.normals.Any(n=>n.y>.99f),"upward plate normals");}
                Debug.Log("MINE_MODEL_MEMORY_BYTES="+string.Join(",",meshes.Select(UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong)));
                var p=PlayableProfile.Default.CopyData();
                foreach(float phase in new[]{0f,.1f,.4f,.8f,1f}){
                    PlayableWorld.BuildMineBurst(effect,phase,(float)p.orbitalMineBlastRadius,p,7);
                    float max=effect.vertices.Max(v=>new Vector2(v.x,v.z).magnitude);Assert.AreEqual((float)p.orbitalMineBlastRadius,max,.00001f,"visible rim exactly matches gameplay radius at "+phase);
                }
                Assert.IsNotNull(Resources.Load<Shader>("TrapMine"));Assert.IsNotNull(Resources.Load<Shader>("TrapMineBurst"));
                Debug.Log("MINE_MODEL_METRICS vertices="+string.Join(",",meshes.Select(m=>m.vertexCount))+" triangles="+string.Join(",",meshes.Select(m=>m.triangles.Length/3))+" renderers=1 materials=1 submissions=1 per model");
            }finally{foreach(var mesh in meshes)Object.DestroyImmediate(mesh);Object.DestroyImmediate(effect);}
        }
    }
}
