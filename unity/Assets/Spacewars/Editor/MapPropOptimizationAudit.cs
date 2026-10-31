using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Spacewars.Editor
{
    // Explicit Editor evidence only: actual constructors, no simulation/router or Player build.
    public static class MapPropOptimizationAudit
    {
        static string Output=>Environment.GetEnvironmentVariable("SPACEWARS_PROP_EVIDENCE")??throw new InvalidOperationException("Set SPACEWARS_PROP_EVIDENCE");
        static readonly string[] Maps={"ThreeCrossings","WhiteMeridian","CrimsonGrove","MossHighland","BasaltCrown","Foundry","AiTest","ProvingGround","Diagnostic","Campaign","Nereida"};
        static T Profile<T>(string name)=>JsonUtility.FromJson<T>(Resources.Load<TextAsset>(name).text);
        static IPlayableTerrain Map(string key)
        {
            switch(key){
                case "ThreeCrossings":return new ThreeCrossingsMap(Profile<ThreeCrossingsProfileData>(key+"Profile"));
                case "WhiteMeridian":return new WhiteMeridianMap(Profile<WhiteMeridianProfileData>(key+"Profile"));
                case "CrimsonGrove":return new CrimsonGroveMap(Profile<CrimsonGroveProfileData>(key+"Profile"));
                case "MossHighland":return new MossHighlandMap(Profile<MossHighlandProfileData>(key+"Profile"));
                case "BasaltCrown":return new BasaltCrownMap();
                case "Foundry":return new FoundryMap(Profile<FoundryProfileData>(key+"Profile"));
                case "Campaign":return new CampaignOpeningMap();case "AiTest":return new AiTestMap();case "ProvingGround":return new ProvingGroundMap();default:return null;
            }
        }
        static string Hash(Action<BinaryWriter> emit)
        {
            using var sha=SHA256.Create();using var stream=new CryptoStream(Stream.Null,sha,CryptoStreamMode.Write);
            using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){emit(writer);writer.Flush();}
            stream.FlushFinalBlock();return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
        }
        public static string Expanded(Mesh mesh)
        {
            return Hash(writer=>{
                foreach(var a in mesh.GetVertexAttributes()){writer.Write((int)a.attribute);writer.Write((int)a.format);writer.Write(a.dimension);writer.Write(a.stream);}
                using var data=Mesh.AcquireReadOnlyMeshData(mesh);var bytes=new byte[mesh.vertexBufferCount][];
                for(int s=0;s<bytes.Length;s++)bytes[s]=data[0].GetVertexData<byte>(s).ToArray();
                for(int sub=0;sub<mesh.subMeshCount;sub++){
                    writer.Write((int)mesh.GetTopology(sub));var indices=mesh.GetIndices(sub,true);writer.Write(indices.Length);
                    foreach(int index in indices)for(int s=0;s<bytes.Length;s++)writer.Write(bytes[s],index*mesh.GetVertexBufferStride(s),mesh.GetVertexBufferStride(s));
                }
            });
        }
        static string PathOf(Transform t)=>t.parent?PathOf(t.parent)+"/"+t.name+"#"+t.GetSiblingIndex():t.name;
        static string Bits(float v)=>BitConverter.SingleToInt32Bits(v).ToString("x8");
        static string Vector(Vector4 v)=>Bits(v.x)+Bits(v.y)+Bits(v.z)+Bits(v.w);
        static string Settings(MeshRenderer r)
        {
            var block=new MaterialPropertyBlock();r.GetPropertyBlock(block);
            var values=new List<string>{r.enabled.ToString(),r.shadowCastingMode.ToString(),r.receiveShadows.ToString(),r.lightProbeUsage.ToString(),r.reflectionProbeUsage.ToString(),r.motionVectorGenerationMode.ToString(),r.gameObject.layer.ToString(),string.Join("",Enumerable.Range(0,16).Select(i=>Bits(r.transform.localToWorldMatrix[i])))};
            foreach(string key in new[]{"_BaseColor","_Role","_SurfaceRole","_Dressing","_Kind","_DetailKind","_RoadSurface","_PadOrigin","_SiteBounds"})values.Add(key+":"+Vector(block.GetVector(key))+"/"+Bits(block.GetFloat(key)));
            foreach(var m in r.sharedMaterials){
                values.Add(m.name+"/"+m.shader.name+"/"+m.renderQueue+"/"+string.Join(",",m.shaderKeywords.OrderBy(x=>x)));
                for(int i=0;i<m.shader.GetPropertyCount();i++){
                    string name=m.shader.GetPropertyName(i);var type=m.shader.GetPropertyType(i);
                    if(type==ShaderPropertyType.Texture){var t=m.GetTexture(name);values.Add(name+":"+(t?t.name+"/"+t.width+"/"+t.height:"null"));}
                    else if(type==ShaderPropertyType.Color||type==ShaderPropertyType.Vector)values.Add(name+":"+Vector(m.GetVector(name)));
                    else values.Add(name+":"+m.GetFloat(name).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            return string.Join(";",values);
        }
        static string BoundsBits(Bounds b)=>string.Join("/",new[]{Bits(b.center.x),Bits(b.center.y),Bits(b.center.z),Bits(b.extents.x),Bits(b.extents.y),Bits(b.extents.z)});
        public static void CaptureFoundryRepresentative()
        {
            if(!Application.isPlaying)throw new InvalidOperationException("Foundry framing requires actual PlayMode lifecycle");
            Directory.CreateDirectory(Output);RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.52f,.55f,.60f);
            var sun=new GameObject("Fixed daylight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.8f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(50,-35,0);
            var camera=new GameObject("Complete Foundry prop camera").AddComponent<Camera>();camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=900;camera.backgroundColor=new Color(.075f,.10f,.13f);camera.clearFlags=CameraClearFlags.SolidColor;camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var host=new GameObject("Foundry");PlayableWorld world=null;
            try{
                var p=PlayableProfile.Create(Profile<PlayableProfileData>("PlayableProfile"),Map("Foundry"));world=new PlayableWorld(host.transform,p);world.InspectionReveal();
                var renderers=host.GetComponentsInChildren<MeshRenderer>();var hall=renderers.First(r=>r.name=="Foundry grey industrial hall");var annex=renderers.First(r=>r.name=="Foundry grey industrial annex");
                var center=hall.bounds.center;float radius=new[]{hall,annex}.Max(r=>Vector3.Distance(center,r.bounds.center)+r.bounds.extents.magnitude);
                camera.orthographicSize=radius*1.15f;camera.transform.position=center+new Vector3(0,90,-70).normalized*Mathf.Max(120,radius*4);camera.transform.LookAt(center);
                // Every corner must remain strictly inside the frame, before saving.
                foreach(var r in new[]{hall,annex})for(int mask=0;mask<8;mask++){
                    var b=r.bounds;var v=b.center+Vector3.Scale(b.extents,new Vector3((mask&1)==0?-1:1,(mask&2)==0?-1:1,(mask&4)==0?-1:1));var screen=camera.WorldToViewportPoint(v);
                    if(screen.z<=0||screen.x<=0||screen.x>=1||screen.y<=0||screen.y>=1)throw new InvalidOperationException("Foundry prop corner is clipped: "+r.name);
                }
                Save(camera,System.IO.Path.Combine(Output,"Foundry-hall-annex-complete.png"));
                File.WriteAllText(System.IO.Path.Combine(Output,"camera.json"),JsonConvert.SerializeObject(new{centerSource="hall.renderer.bounds.center",centerBits=Vector(new Vector4(center.x,center.y,center.z,0)),orthographicSizeBits=Bits(camera.orthographicSize),hallBounds=BoundsBits(hall.bounds),annexBounds=BoundsBits(annex.bounds),direction="0/90/-70",resolution="1600x1000",light="ambient.52/.55/.60;sun50/-35,intensity1.8,soft shadows",fog="InspectionReveal",fullHallAndAnnexViewportCorners="PASS"},Formatting.Indented));
            }finally{world?.Dispose();Object.DestroyImmediate(host);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(sun.gameObject);}
        }
        public static void CaptureBounds()
        {
            if(!Application.isPlaying)throw new InvalidOperationException("Bounds qualification requires actual PlayMode lifecycle");
            Directory.CreateDirectory(Output);
            var control=new Bounds(Vector3.zero,Vector3.one);var changed=control;changed.center=new Vector3(BitConverter.Int32BitsToSingle(1),0,0);
            if(BoundsBits(control)==BoundsBits(changed))throw new InvalidOperationException("Bounds recorder lost a one-bit center change");
            foreach(string key in Maps){
                var host=new GameObject(key);PlayableWorld world=null;
                try{
                    var p=PlayableProfile.Create(Profile<PlayableProfileData>("PlayableProfile"),Map(key));
                    if(key=="Nereida")host.AddComponent<NereidaPreview>().Build(NereidaPreview.LoadProfile());else world=new PlayableWorld(host.transform,p);
                    var rows=host.GetComponentsInChildren<MeshFilter>(true).Select(f=>new{path=PathOf(f.transform),mesh=f.sharedMesh.name,vertices=f.sharedMesh.vertexCount,expanded=Expanded(f.sharedMesh),bounds=BoundsBits(f.sharedMesh.bounds),submeshBounds=Enumerable.Range(0,f.sharedMesh.subMeshCount).Select(i=>BoundsBits(f.sharedMesh.GetSubMesh(i).bounds)).ToArray()}).ToArray();
                    File.WriteAllText(System.IO.Path.Combine(Output,key+".json"),JsonConvert.SerializeObject(new{key,boundsFormat="Float32 bits centerXYZ/extentsXYZ",negativeControl="one-bit center change detected",rows},Formatting.Indented));
                    Debug.Log("MAP_PROP_BOUNDS_PASS "+key+" "+rows.Length);
                }finally{world?.Dispose();Object.DestroyImmediate(host);}
            }
        }
        public static void Capture()
        {
            Directory.CreateDirectory(Output);if(!Application.isPlaying)EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.52f,.55f,.60f);
            var sun=new GameObject("Fixed daylight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.8f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(50,-35,0);
            var camera=new GameObject("Fixed prop camera").AddComponent<Camera>();camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=900;camera.backgroundColor=new Color(.075f,.10f,.13f);camera.clearFlags=CameraClearFlags.SolidColor;camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var selected=(Environment.GetEnvironmentVariable("SPACEWARS_PROP_MAPS")??string.Join(",",Maps)).Split(',');
            foreach(string key in selected)
            {
                var host=new GameObject(key);PlayableWorld world=null;
                try{
                    var map=Map(key);var p=PlayableProfile.Create(Profile<PlayableProfileData>("PlayableProfile"),map);
                    if(key=="Nereida")host.AddComponent<NereidaPreview>().Build(NereidaPreview.LoadProfile());
                    else{world=new PlayableWorld(host.transform,p);world.InspectionReveal();}
                    var filters=host.GetComponentsInChildren<MeshFilter>(true);var rows=new List<object>();
                    foreach(var filter in filters){var m=filter.sharedMesh;var r=filter.GetComponent<MeshRenderer>();long vertexBytes=0;
                        using(var data=Mesh.AcquireReadOnlyMeshData(m))for(int s=0;s<m.vertexBufferCount;s++)vertexBytes+=data[0].GetVertexData<byte>(s).Length;
                        var clone=Object.Instantiate(m);int possibleVertices;long possibleVertexBytes=0;
                        try{FinalProceduralMesh.IndexOwned(clone);possibleVertices=clone.vertexCount;using(var data=Mesh.AcquireReadOnlyMeshData(clone))for(int s=0;s<clone.vertexBufferCount;s++)possibleVertexBytes+=data[0].GetVertexData<byte>(s).Length;if(Expanded(m)!=Expanded(clone))throw new InvalidOperationException("Corner attributes changed "+filter.name);}
                        finally{Object.DestroyImmediate(clone);}
                        rows.Add(new{path=PathOf(filter.transform),mesh=m.name,vertices=m.vertexCount,triangles=Enumerable.Range(0,m.subMeshCount).Sum(s=>(long)m.GetIndexCount(s)/3),submeshes=m.subMeshCount,indexFormat=m.indexFormat.ToString(),vertexBytes,indexBytes=Enumerable.Range(0,m.subMeshCount).Sum(s=>(long)m.GetIndexCount(s))*(m.indexFormat==IndexFormat.UInt16?2:4),runtimeMemory=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m),possibleVertices,possibleVertexBytes,expanded=Expanded(m),bounds=BoundsBits(m.bounds),submeshBounds=Enumerable.Range(0,m.subMeshCount).Select(i=>BoundsBits(m.GetSubMesh(i).bounds)).ToArray(),settings=Settings(r),colliders=filter.GetComponents<Collider>().Length});
                    }
                    File.WriteAllText(System.IO.Path.Combine(Output,key+".json"),JsonConvert.SerializeObject(new{key,map=map?.Id,revision=map?.Revision,rows,uniqueMeshes=filters.Select(f=>f.sharedMesh).Distinct().Count(),renderers=host.GetComponentsInChildren<Renderer>(true).Length,materials=host.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().Count()},Formatting.Indented));
                    void Frame(string label,Vector3 center,float size){if(Environment.GetEnvironmentVariable("SPACEWARS_PROP_SKIP_FRAMES")=="1")return;camera.orthographicSize=size;camera.transform.position=center+new Vector3(0,90,-70);camera.transform.LookAt(center);Save(camera,System.IO.Path.Combine(Output,key+"-"+label+".png"));}
                    Frame("overview",Vector3.zero,(float)p.ArenaHalfExtent*1.12f);
                    if(key=="ThreeCrossings")foreach(string label in new[]{"heavy tractor wrecks","parked service equipment","corporate extraction scenery","perimeter building compounds","service infrastructure"}){
                        var r=host.GetComponentsInChildren<MeshRenderer>().First(x=>x.name=="Detail — "+label);Vector3 center=r.bounds.center;
                        // Combined groups cover the map: focus an actual planned member.
                        if(label!="parked service equipment"){
                            var filter=r.GetComponent<MeshFilter>();var mesh=filter.sharedMesh;
                            center=filter.transform.TransformPoint(mesh.vertices[mesh.GetIndices(0,true)[0]]);
                        }
                        Frame(label+"-game",center,(float)p.CameraOrthoSize);Frame(label+"-close",center,label=="perimeter building compounds"?15:label=="heavy tractor wrecks"?8:5);
                    }
                    if(key!="ThreeCrossings"){
                        string[] targets=key=="WhiteMeridian"?new[]{"Wreck ","Meridian decor expedition ","Meridian foundation concrete"}:key=="CrimsonGrove"?new[]{"Faceted crimson crystal clusters","Complete construction apron"}:key=="BasaltCrown"?new[]{"Basalt details — Kerb","Basalt details — Ore"}:key=="Foundry"?new[]{"Foundry grey industrial hall"}:key=="Nereida"?new[]{"Station laboratory"}:Array.Empty<string>();
                        int targetIndex=0;foreach(string prefix in targets){var filter=filters.FirstOrDefault(f=>f.name.StartsWith(prefix,StringComparison.Ordinal));if(!filter)continue;
                            var m=filter.sharedMesh;var center=filter.transform.TransformPoint(m.vertices[m.GetIndices(0,true)[0]]);
                            Frame("prop"+targetIndex+"-game",center,(float)p.CameraOrthoSize);Frame("prop"+targetIndex+"-close",center,prefix=="Wreck "?12:8);targetIndex++;
                        }
                    }
                    if(world!=null){world.InspectionHide();Frame("fog",Vector3.zero,(float)p.ArenaHalfExtent*1.12f);}
                    Debug.Log("MAP_PROP_AUDIT_PASS "+key+" "+Output);
                }finally{world?.Dispose();Object.DestroyImmediate(host);}
            }
            File.WriteAllText(System.IO.Path.Combine(Output,"settings.txt"),"Unity="+Application.unityVersion+"; actual shipped resources/profiles; decoration seed307 unchanged; fixed daylight50/-35,intensity1.8,ambient.52/.55/.60; 1600x1000; ortho offset0/90/-70; no FPS/draw-call claim; Editor evidence only\n");
            Object.DestroyImmediate(sun.gameObject);Object.DestroyImmediate(camera.gameObject);
            if(!Application.isPlaying)EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        }
        static void Save(Camera camera,string file)
        {
            var target=new RenderTexture(1600,1000,24);var texture=new Texture2D(1600,1000,TextureFormat.RGB24,false);var old=RenderTexture.active;
            try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1600,1000),0,0);texture.Apply();File.WriteAllBytes(file,texture.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=old;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(texture);}
        }
    }
}
