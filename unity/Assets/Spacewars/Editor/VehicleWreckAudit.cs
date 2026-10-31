using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Spacewars.Presentation;
namespace Spacewars.Editor
{
    // Asset-buffer accounting, not an FPS or whole-match benchmark.
    public static class VehicleWreckAudit
    {
        static long Buffer(Mesh m)=>Enumerable.Range(0,m.vertexBufferCount).Sum(i=>(long)m.vertexCount*m.GetVertexBufferStride(i))+Enumerable.Range(0,m.subMeshCount).Sum(i=>(long)m.GetIndexCount(i))*(m.indexFormat==UnityEngine.Rendering.IndexFormat.UInt16?2:4);
        public static void Measure()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_WRECK_AUDIT")??Path.GetFullPath("../.local/vehicle-wreck-detail/candidate");Directory.CreateDirectory(output);
            var rows=new List<string>{"model,path,vertices,triangles,renderers,submeshes,materials,meshBufferBytes,uniqueMeshRuntimeBytes,shadowRenderers,instancingMaterials"};
            foreach(bool upgraded in new[]{false,true})foreach(string kind in new[]{"tank","explorer","shkval","hawk"})for(int v=1;v<=(kind=="hawk"?4:3);v++)
            {
                string key=kind+(upgraded?"-upgraded-":"-")+v;
                foreach(bool prepared in new[]{false,true})
                {
                    var go=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spacewars/Content/"+(prepared?"Resources/Wrecks/":"Wrecks/")+key+(prepared?".prefab":".glb"));
                    if(!go)throw new InvalidOperationException("Missing wreck "+key);
                    var meshes=go.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).ToArray();var renderers=go.GetComponentsInChildren<MeshRenderer>(true);var mats=renderers.SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
                    if(renderers.Length!=3||go.GetComponentsInChildren<Collider>(true).Length!=0||go.GetComponentsInChildren<Rigidbody>(true).Length!=0)throw new InvalidOperationException("Wreck render-only shape contract "+key);
                    rows.Add($"{key},{(prepared?"prepared":"imported")},{meshes.Sum(m=>m.vertexCount)},{meshes.Sum(m=>m.triangles.Length/3)},{renderers.Length},{meshes.Sum(m=>m.subMeshCount)},{mats.Length},{meshes.Sum(Buffer)},{meshes.Distinct().Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m))},{renderers.Count(r=>r.shadowCastingMode!=UnityEngine.Rendering.ShadowCastingMode.Off)},{mats.Count(m=>m.enableInstancing)}");
                }
            }
            File.WriteAllLines(Path.Combine(output,"mesh-buffers.csv"),rows);
            CompareFrames(output);Debug.Log("CURRENT_MODEL_WRECK_AUDIT_PASS 26 render-only variants; imported versus immutable prepared buffers");
        }
        public static void MeasureBaseline()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_WRECK_AUDIT")??Path.GetFullPath("../.local/vehicle-wreck-detail/baseline");Directory.CreateDirectory(output);
            var rows=new List<string>{"model,path,vertices,triangles,renderers,submeshes,materials,meshBufferBytes,uniqueMeshRuntimeBytes,shadowRenderers,instancingMaterials"};
            foreach(string kind in new[]{"tank","explorer","shkval","hawk"})for(int v=1;v<=(kind=="hawk"?4:3);v++)
            {
                string key=kind+"-"+v;var go=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spacewars/Content/WreckComparison/Prefabs/"+key+".prefab");if(!go)throw new InvalidOperationException("Restore exact baseline prefab sources before measurement: "+key);
                var meshes=go.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).ToArray();if(meshes.Any(m=>!m))throw new InvalidOperationException("Baseline replay mesh missing "+key);
                var renderers=go.GetComponentsInChildren<MeshRenderer>(true);var mats=renderers.SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
                rows.Add($"{key},baseline,{meshes.Sum(m=>m.vertexCount)},{meshes.Sum(m=>m.triangles.Length/3)},{renderers.Length},{meshes.Sum(m=>m.subMeshCount)},{mats.Length},{meshes.Sum(Buffer)},{meshes.Distinct().Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m))},{renderers.Count(r=>r.shadowCastingMode!=UnityEngine.Rendering.ShadowCastingMode.Off)},{mats.Count(m=>m.enableInstancing)}");
            }
            File.WriteAllLines(Path.Combine(output,"mesh-buffers.csv"),rows);Debug.Log("WRECK_BASELINE_REPLAY_MEASURED 13 exact original resource bindings; compare against original baseline CSV before accepting replay");
        }
        public static void CaptureDetails()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_WRECK_AUDIT")??Path.GetFullPath("../.local/vehicle-wreck-detail/details");Directory.CreateDirectory(output);
            var cameraHost=new GameObject("Current wreck detail camera");var lightHost=new GameObject("Current wreck detail light");var target=new RenderTexture(960,720,24);target.Create();
            var camera=cameraHost.AddComponent<Camera>();camera.targetTexture=target;camera.orthographic=true;camera.cullingMask=1<<29;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.18f,.20f);camera.transform.position=new Vector3(4,4.5f,6);camera.transform.LookAt(new Vector3(0,.55f,0));
            var light=lightHost.AddComponent<Light>();var lighting=IndustrialLightingProfile.Load("three-crossings");lighting.ApplyScene(light);light.cullingMask=1<<29;var finish=ActorMaterialProfile.Load();
            void Layer(GameObject go){go.layer=29;foreach(Transform child in go.transform)Layer(child.gameObject);}
            try
            {
                foreach(string kind in new[]{"tank","explorer","shkval","hawk"})foreach(bool upgraded in new[]{false,true})for(int v=1;v<=(kind=="hawk"?4:3);v++)
                {
                    string key=kind+(upgraded?"-upgraded-":"-")+v;camera.orthographicSize=kind=="hawk"?2.05f:kind=="shkval"?2.1f:kind=="tank"?1.8f:1.5f;
                    var prepared=Resources.Load<GameObject>("Wrecks/"+key);var mats=prepared.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().ToDictionary(m=>m.name);
                    foreach(bool baseline in new[]{false,true})
                    {
                        if(baseline&&(upgraded||v!=1||!AssetDatabase.IsValidFolder("Assets/Spacewars/Content/WreckComparison")))continue;
                        var prefab=baseline?AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spacewars/Content/WreckComparison/"+kind+"-1.glb"):prepared;if(!prefab)throw new InvalidOperationException("Missing detail source "+key);
                        var go=UnityEngine.Object.Instantiate(prefab);Layer(go);
                        try
                        {
                            foreach(var r in go.GetComponentsInChildren<MeshRenderer>()){r.sharedMaterials=r.sharedMaterials.Select(m=>mats[m.name]).ToArray();finish.ApplyWreck(r,light.color);}
                            var previous=RenderTexture.active;var pixels=new Texture2D(960,720,TextureFormat.RGB24,false);
                            try{camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,960,720),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,key+(baseline?"-baseline":"-candidate")+".png"),pixels.EncodeToPNG());}
                            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
                        }
                        finally{UnityEngine.Object.DestroyImmediate(go);}
                    }
                }
                File.WriteAllText(Path.Combine(output,"scope.txt"),"Controlled Unity Editor model detail; asset scale1, matched camera960x720, current ThreeCrossings lighting and unchanged F3/revision2 wreck finish. Baseline78c395daf old GLBs and candidate Resources prefabs; current actor scale is independently verified in WreckVisualReviewTests. Not Player, ordinary-match or human acceptance.");
            }
            finally{UnityEngine.Object.DestroyImmediate(cameraHost);UnityEngine.Object.DestroyImmediate(lightHost);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
        static void CompareFrames(string output)
        {
            var stage=new GameObject("Wreck optimization frame control");var camHost=new GameObject("Wreck audit camera");var lightHost=new GameObject("Wreck audit light");
            var target=new RenderTexture(640,480,24);target.Create();var camera=camHost.AddComponent<Camera>();camera.targetTexture=target;camera.orthographic=true;camera.orthographicSize=2.7f;camera.cullingMask=1<<29;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.14f,.16f,.18f);camera.transform.position=new Vector3(4,5,-6);camera.transform.LookAt(new Vector3(0,.5f,.2f));
            var light=lightHost.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.6f;light.color=Color.white;light.cullingMask=1<<29;light.transform.rotation=Quaternion.Euler(45,-30,0);
            var lines=new List<string>{"model,identicalPng,bytes"};var finish=ActorMaterialProfile.Load();
            void Layer(GameObject go){go.layer=29;foreach(Transform child in go.transform)Layer(child.gameObject);}
            byte[] Capture(string key,GameObject prefab,Dictionary<string,Material> materials)
            {
                var go=UnityEngine.Object.Instantiate(prefab,stage.transform);Layer(go);
                try
                {
                    foreach(var r in go.GetComponentsInChildren<MeshRenderer>())
                    {
                        r.sharedMaterials=r.sharedMaterials.Select(m=>materials[m.name]).ToArray();finish.ApplyWreck(r,Color.white);
                    }
                    var previous=RenderTexture.active;var pixels=new Texture2D(640,480,TextureFormat.RGB24,false);
                    try{camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,640,480),0,0);pixels.Apply();return pixels.EncodeToPNG();}
                    finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
                }
                finally{UnityEngine.Object.DestroyImmediate(go);}
            }
            try
            {
                foreach(bool upgraded in new[]{false,true})foreach(string kind in new[]{"tank","explorer","shkval","hawk"})for(int v=1;v<=(kind=="hawk"?4:3);v++)
                {
                    string key=kind+(upgraded?"-upgraded-":"-")+v;
                    var imported=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spacewars/Content/Wrecks/"+key+".glb");var prepared=Resources.Load<GameObject>("Wrecks/"+key);
                    var mats=prepared.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().ToDictionary(m=>m.name);
                    byte[] before=Capture(key,imported,mats),after=Capture(key,prepared,mats);bool equal=before.SequenceEqual(after);
                    File.WriteAllBytes(Path.Combine(output,key+"-unoptimized.png"),before);File.WriteAllBytes(Path.Combine(output,key+"-optimized.png"),after);lines.Add($"{key},{equal},{after.Length}");
                    if(!equal)throw new InvalidOperationException("Optimized wreck frame differs: "+key);
                }
                File.WriteAllLines(Path.Combine(output,"optimization-frames.csv"),lines);
            }
            finally{UnityEngine.Object.DestroyImmediate(stage);UnityEngine.Object.DestroyImmediate(camHost);UnityEngine.Object.DestroyImmediate(lightHost);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
    }
}
