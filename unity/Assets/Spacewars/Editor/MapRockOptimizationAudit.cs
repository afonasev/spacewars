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
    public static class MapRockOptimizationAudit
    {
        static string Output=>Environment.GetEnvironmentVariable("SPACEWARS_ROCK_OUTPUT")??throw new InvalidOperationException("Set SPACEWARS_ROCK_OUTPUT");
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
        internal static string Settings(MeshRenderer r)
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
        internal static string BoundsBits(Bounds b)=>string.Join("/",new[]{Bits(b.center.x),Bits(b.center.y),Bits(b.center.z),Bits(b.extents.x),Bits(b.extents.y),Bits(b.extents.z)});
        public static void Capture()
        {
            if(!Application.isPlaying)throw new InvalidOperationException("Actual PlayMode lifecycle required");
            Directory.CreateDirectory(Output);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.52f,.55f,.60f);
            var sun=new GameObject("Fixed daylight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.8f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(50,-35,0);
            var camera=new GameObject("Rock evidence camera").AddComponent<Camera>();camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=2000;camera.backgroundColor=new Color(.075f,.10f,.13f);camera.clearFlags=CameraClearFlags.SolidColor;camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            try{
                foreach(string key in (Environment.GetEnvironmentVariable("SPACEWARS_ROCK_MAPS")??string.Join(",",Maps)).Split(',')){
                    var host=new GameObject(key);PlayableWorld world=null;
                    try{
                        var p=PlayableProfile.Create(Profile<PlayableProfileData>("PlayableProfile"),Map(key));
                        if(key=="Nereida")host.AddComponent<NereidaPreview>().Build(NereidaPreview.LoadProfile());else{world=new PlayableWorld(host.transform,p);world.InspectionReveal();}
                        var filters=host.GetComponentsInChildren<MeshFilter>(true);var rows=new List<object>();
                        foreach(var filter in filters){
                            var m=filter.sharedMesh;var r=filter.GetComponent<MeshRenderer>();if(!m||!r)continue;
                            long VertexBytes(Mesh mesh){long n=0;using var data=Mesh.AcquireReadOnlyMeshData(mesh);for(int s=0;s<mesh.vertexBufferCount;s++)n+=data[0].GetVertexData<byte>(s).Length;return n;}
                            long IndexBytes(Mesh mesh)=>Enumerable.Range(0,mesh.subMeshCount).Sum(s=>(long)mesh.GetIndexCount(s))*(mesh.indexFormat==IndexFormat.UInt16?2:4);
                            var clone=Object.Instantiate(m);int possibleVertices;long possibleVertexBytes,possibleIndexBytes;
                            try{FinalProceduralMesh.IndexOwned(clone);possibleVertices=clone.vertexCount;possibleVertexBytes=VertexBytes(clone);possibleIndexBytes=IndexBytes(clone);
                                if(Expanded(m)!=Expanded(clone)||BoundsBits(m.bounds)!=BoundsBits(clone.bounds)||Enumerable.Range(0,m.subMeshCount).Any(s=>BoundsBits(m.GetSubMesh(s).bounds)!=BoundsBits(clone.GetSubMesh(s).bounds)))throw new InvalidOperationException("Exact final geometry/bounds changed "+filter.name);
                            }finally{Object.DestroyImmediate(clone);}
                            rows.Add(new{path=PathOf(filter.transform),mesh=m.name,vertices=m.vertexCount,triangles=Enumerable.Range(0,m.subMeshCount).Sum(s=>(long)m.GetIndexCount(s)/3),submeshes=m.subMeshCount,indexFormat=m.indexFormat.ToString(),vertexBytes=VertexBytes(m),indexBytes=IndexBytes(m),runtimeMemory=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m),possibleVertices,possibleVertexBytes,possibleIndexBytes,expanded=Expanded(m),bounds=BoundsBits(m.bounds),submeshBounds=Enumerable.Range(0,m.subMeshCount).Select(s=>BoundsBits(m.GetSubMesh(s).bounds)).ToArray(),settings=Settings(r),colliders=filter.GetComponents<Collider>().Length});
                        }
                        File.WriteAllText(System.IO.Path.Combine(Output,key+".json"),JsonConvert.SerializeObject(new{key,rows,uniqueMeshes=filters.Select(f=>f.sharedMesh).Distinct().Count(),renderers=host.GetComponentsInChildren<Renderer>(true).Length,materials=host.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().Count()},Formatting.Indented));
                        if(Environment.GetEnvironmentVariable("SPACEWARS_ROCK_SKIP_FRAMES")!="1"){
                            var targets=host.GetComponentsInChildren<MeshRenderer>().Where(r=>r.GetComponent<MeshFilter>()).Where(r=>r.name.IndexOf("geological debris",StringComparison.OrdinalIgnoreCase)>=0||r.name.IndexOf("rock",StringComparison.OrdinalIgnoreCase)>=0||r.name.IndexOf("ridge",StringComparison.OrdinalIgnoreCase)>=0||r.name.IndexOf("massif",StringComparison.OrdinalIgnoreCase)>=0||r.name.IndexOf("terrain",StringComparison.OrdinalIgnoreCase)>=0||r.name.IndexOf("bank",StringComparison.OrdinalIgnoreCase)>=0||r.name.IndexOf("snow",StringComparison.OrdinalIgnoreCase)>=0||r.name.IndexOf("ice",StringComparison.OrdinalIgnoreCase)>=0||r.name.IndexOf("canyon",StringComparison.OrdinalIgnoreCase)>=0||r.name.IndexOf("fissure",StringComparison.OrdinalIgnoreCase)>=0||r.name.IndexOf("side faces",StringComparison.OrdinalIgnoreCase)>=0||r.name.IndexOf("fragments",StringComparison.OrdinalIgnoreCase)>=0||r.GetComponent<MeshFilter>()?.sharedMesh?.name=="Shared terrain prism"||r.GetComponent<MeshFilter>()?.sharedMesh?.name=="Crash pocket sand"||r.GetComponent<MeshFilter>()?.sharedMesh?.name.StartsWith("Meridian continuous",StringComparison.Ordinal)==true).GroupBy(r=>{var block=new MaterialPropertyBlock();r.GetPropertyBlock(block);return r.GetComponent<MeshFilter>()?.sharedMesh?.name+"/"+block.GetFloat("_SurfaceRole")+"/"+block.GetFloat("_Role")+"/"+block.GetFloat("_Kind")+"/"+block.GetFloat("_BorderSurface")+"/"+r.sharedMaterial.name;}).Select(g=>g.First()).ToArray();
                            foreach(var r in targets){
                                var b=r.bounds;var center=b.center;float size=Mathf.Max(1,b.extents.magnitude)*1.2f;
                                camera.orthographicSize=size;camera.transform.position=center+new Vector3(0,90,-70).normalized*Mathf.Max(120,size*4);camera.transform.LookAt(center);
                                camera.aspect=1.6f;
                                for(int mask=0;mask<8;mask++){var v=center+Vector3.Scale(b.extents,new Vector3((mask&1)==0?-1:1,(mask&2)==0?-1:1,(mask&4)==0?-1:1));var screen=camera.WorldToViewportPoint(v);if(screen.z<=0||screen.x<=0||screen.x>=1||screen.y<=0||screen.y>=1)throw new InvalidOperationException("Clipped renderer "+r.name);}
                                string label=key+"-"+Array.IndexOf(targets,r);Save(camera,System.IO.Path.Combine(Output,label+"-complete.png"));
                                File.WriteAllText(System.IO.Path.Combine(Output,label+"-camera.json"),JsonConvert.SerializeObject(new{path=PathOf(r.transform),center=BoundsBits(b),size=Bits(size),resolution="1600x1000",direction="0/90/-70",viewportCorners="PASS"},Formatting.Indented));
                            }
                        }
                        Debug.Log("MAP_ROCK_AUDIT_PASS "+key+" "+rows.Count);
                    }finally{world?.Dispose();Object.DestroyImmediate(host);}
                }
                File.WriteAllText(System.IO.Path.Combine(Output,"settings.txt"),"Unity="+Application.unityVersion+"; shipped resources; seed307; ambient .52/.55/.60; sun50/-35 intensity1.8 soft; fog InspectionReveal; renderer bounds framing; native Editor PlayMode; no FPS/draw-call claim");
            }finally{Object.DestroyImmediate(sun.gameObject);Object.DestroyImmediate(camera.gameObject);}
        }
        internal static void Save(Camera camera,string file)
        {
            var target=new RenderTexture(1600,1000,24);var texture=new Texture2D(1600,1000,TextureFormat.RGB24,false);var old=RenderTexture.active;
            try{Shader.SetGlobalVector("_Time",new Vector4(.05f,1,2,3));Shader.SetGlobalVector("_SinTime",Vector4.zero);Shader.SetGlobalVector("_CosTime",Vector4.one);camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1600,1000),0,0);texture.Apply();File.WriteAllBytes(file,texture.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=old;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(texture);}
        }
    }
}
