using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
namespace Spacewars.Editor
{
 public static class ShkvalAssetMetrics
 {
  [Serializable] public sealed class Entry{public string key;public int vertices,triangles,meshResources,renderers,submeshes,materials;public long meshMemoryBytes,meshMemoryWithoutReuseBytes;public int shadowRenderers,textures;public long textureMemoryBytes;}
  [Serializable] public sealed class Report{public Entry[] models;}
  public static void Measure(){
   var result=new System.Collections.Generic.List<Entry>();
   foreach(var key in new[]{"shkval","shkval-guidance-3","baseline-shkval","baseline-shkval-guidance-3"}){
    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(key.StartsWith("baseline-")?"Assets/ShkvalMetricsTemporary/"+key+".glb":"Assets/Spacewars/Content/Resources/StyleA/"+key+".prefab");if(!prefab)continue;
    var rs=prefab.GetComponentsInChildren<MeshRenderer>(true);var ms=prefab.GetComponentsInChildren<MeshFilter>(true).Select(x=>x.sharedMesh).ToArray();var unique=ms.Distinct().ToArray();
    var materials=rs.SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
    if(key.StartsWith("baseline-"))materials=new[]{"team-primary","neutral-metal","structure","system-emissive"}.Select(role=>AssetDatabase.LoadAssetAtPath<Material>("Assets/ShkvalMetricsTemporary/baseline-"+role+".mat")).ToArray();
    var textures=materials.Where(m=>m).SelectMany(m=>m.GetTexturePropertyNames().Select(p=>m.GetTexture(p))).Where(t=>t).Distinct().ToArray();
    result.Add(new Entry{textures=textures.Length,textureMemoryBytes=textures.Sum(t=>Profiler.GetRuntimeMemorySizeLong(t)),key=key,vertices=ms.Sum(m=>m.vertexCount),triangles=ms.Sum(m=>m.triangles.Length/3),meshResources=unique.Length,renderers=rs.Length,submeshes=ms.Sum(m=>m.subMeshCount),materials=rs.SelectMany(r=>r.sharedMaterials).Distinct().Count(),meshMemoryWithoutReuseBytes=ms.Sum(m=>Profiler.GetRuntimeMemorySizeLong(m)),meshMemoryBytes=unique.Sum(m=>Profiler.GetRuntimeMemorySizeLong(m)),shadowRenderers=rs.Count(r=>r.shadowCastingMode!=UnityEngine.Rendering.ShadowCastingMode.Off)});
   }
   string path=Environment.GetEnvironmentVariable("SPACEWARS_SHKVAL_METRICS");if(string.IsNullOrEmpty(path))throw new Exception("Set SPACEWARS_SHKVAL_METRICS");File.WriteAllText(path,JsonUtility.ToJson(new Report{models=result.ToArray()},true));
  }
  public static void PrepareAndMeasure(){ShkvalMonolithAssets.Prepare();Measure();}
 }
}
