using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Spacewars.Presentation
{
    // A separate QA scene only. No component or candidate is used in ordinary games.
    public sealed class MapModelLodGallery : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        string output;Camera cameraView;RenderTexture target;
        readonly Dictionary<string,Mesh[]> cache=new Dictionary<string,Mesh[]>();
        readonly List<object> measurements=new List<object>();
        public static string Identity(Mesh mesh)
        {
            using var sha=SHA256.Create();using var stream=new CryptoStream(Stream.Null,sha,CryptoStreamMode.Write);
            using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){
                foreach(var a in mesh.GetVertexAttributes()){writer.Write((int)a.attribute);writer.Write((int)a.format);writer.Write(a.dimension);writer.Write(a.stream);}
                using var data=Mesh.AcquireReadOnlyMeshData(mesh);var bytes=new byte[mesh.vertexBufferCount][];
                for(int s=0;s<bytes.Length;s++)bytes[s]=data[0].GetVertexData<byte>(s).ToArray();
                for(int sub=0;sub<mesh.subMeshCount;sub++){
                    writer.Write((int)mesh.GetTopology(sub));var indices=mesh.GetIndices(sub,true);writer.Write(indices.Length);
                    foreach(int index in indices)for(int s=0;s<bytes.Length;s++)writer.Write(bytes[s],index*mesh.GetVertexBufferStride(s),mesh.GetVertexBufferStride(s));
                }
                writer.Flush();
            }
            stream.FlushFinalBlock();return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
        }
        static T Profile<T>(string name)=>JsonUtility.FromJson<T>(Resources.Load<TextAsset>(name).text);
        static IPlayableTerrain Map(string key)
        {
            switch(key){
                case "ThreeCrossings":return new ThreeCrossingsMap(Profile<ThreeCrossingsProfileData>(key+"Profile"));
                case "WhiteMeridian":return new WhiteMeridianMap(Profile<WhiteMeridianProfileData>(key+"Profile"));
                case "CrimsonGrove":return new CrimsonGroveMap(Profile<CrimsonGroveProfileData>(key+"Profile"));
                case "MossHighland":return new MossHighlandMap(Profile<MossHighlandProfileData>(key+"Profile"));
                case "BasaltCrown":return new BasaltCrownMap();case "Foundry":return new FoundryMap(Profile<FoundryProfileData>(key+"Profile"));
                case "Campaign":return new CampaignOpeningMap();case "AiTest":return new AiTestMap();case "ProvingGround":return new ProvingGroundMap();default:return null;
            }
        }
        Mesh Candidate(string hash,string tier,Mesh original)
        {
            var row=JObject.Parse(File.ReadAllText(Path.Combine(output,"reduced",hash+"-"+tier+".json")));
            var vs=row["vertices"].ToObject<float[][]>();var ns=row["normals"].ToObject<float[][]>();var uv=row["uv"].ToObject<float[][]>();var cs=row["colors"].ToObject<float[][]>();var subs=row["submeshes"].ToObject<int[][]>();
            var mesh=new Mesh{name=original.name+"-review-"+tier,indexFormat=IndexFormat.UInt32};mesh.vertices=vs.Select(a=>new Vector3(a[0],a[1],a[2])).ToArray();mesh.normals=ns.Select(a=>new Vector3(a[0],a[1],a[2])).ToArray();
            if(uv.Length>0)mesh.uv=uv.Select(a=>new Vector2(a[0],a[1])).ToArray();if(cs.Length>0)mesh.colors=cs.Select(a=>new Color(a[0],a[1],a[2],a[3])).ToArray();
            if(subs.Length!=original.subMeshCount)throw new InvalidOperationException("Review material layout changed "+hash);
            mesh.subMeshCount=subs.Length;for(int i=0;i<subs.Length;i++)mesh.SetIndices(subs[i],MeshTopology.Triangles,i,false);
            mesh.bounds=original.bounds;if(row.Value<bool>("recalculateTangents")&&uv.Length>0)mesh.RecalculateTangents();FinalProceduralMesh.IndexOwned(mesh);
            if(mesh.vertexCount==0||mesh.normals.Length!=mesh.vertexCount||mesh.triangles.Any(i=>i<0||i>=mesh.vertexCount))throw new InvalidOperationException("Invalid candidate "+hash);
            return mesh;
        }
        Mesh[] Tiers(Mesh source)
        {
            string hash=Identity(source);if(cache.TryGetValue(hash,out var tiers))return tiers;
            tiers=File.Exists(Path.Combine(output,"reduced",hash+"-normal.json"))&&File.Exists(Path.Combine(output,"reduced",hash+"-far.json"))?new[]{source,Candidate(hash,"normal",source),Candidate(hash,"far",source)}:new[]{source,source,source};
            long Tri(Mesh m)=>Enumerable.Range(0,m.subMeshCount).Sum(s=>(long)m.GetIndexCount(s)/3);
            if(Tri(tiers[0])<Tri(tiers[1])||Tri(tiers[1])<Tri(tiers[2]))throw new InvalidOperationException("Review geometry order "+source.name);
            cache[hash]=tiers;measurements.Add(new{hash,name=source.name,triangles=tiers.Select(Tri).ToArray(),vertices=tiers.Select(m=>m.vertexCount).ToArray(),bytes=tiers.Select(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m)).ToArray(),submeshes=tiers.Select(m=>m.subMeshCount).ToArray(),status=tiers[0]==tiers[2]?"UNCHANGED_NO_CANDIDATE":"REVIEW_ONLY"});return tiers;
        }
        void Frame(string name,Vector3 center,float size)
        {
            cameraView.orthographicSize=Mathf.Max(.1f,size);cameraView.transform.position=center+new Vector3(0,90,-70).normalized*Mathf.Max(100,size*4);cameraView.transform.LookAt(center);cameraView.farClipPlane=Mathf.Max(1000,size*8);
            cameraView.Render();var previous=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try{pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,"frames",name+".png"),pixels.EncodeToPNG());}
            finally{RenderTexture.active=previous;Object.DestroyImmediate(pixels);}
        }
        void Review(string group,GameObject host,bool wreck)
        {
            var filters=host.GetComponentsInChildren<MeshFilter>(true);var originals=filters.Select(f=>f.sharedMesh).ToArray();var tiers=originals.Select(Tiers).ToArray();
            var selected=wreck?new[]{filters.First()}:filters.Where((f,i)=>tiers[i][0]!=tiers[i][2]).GroupBy(f=>new string(f.name.TakeWhile(c=>!char.IsDigit(c)).ToArray())).Select(g=>g.First()).ToArray();
            var all=host.GetComponentsInChildren<Renderer>(true);Bounds full=all[0].bounds;foreach(var r in all.Skip(1))full.Encapsulate(r.bounds);
            for(int level=0;level<3;level++){
                for(int i=0;i<filters.Length;i++)filters[i].sharedMesh=tiers[i][level];
                if(wreck)Frame(group+"-lod"+level,full.center,Mathf.Max(full.size.x,full.size.y,full.size.z)*.7f);
                else{
                    Frame(group+"-overview-lod"+level,full.center,Mathf.Max(full.size.x,full.size.z)*.6f);
                    for(int i=0;i<selected.Length;i++){
                        var f=selected[i];var bounds=f.GetComponent<Renderer>().bounds;float size=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
                        bool combined=size>40;var center=combined?f.transform.TransformPoint(f.sharedMesh.vertices[0]):bounds.center;
                        Frame(group+"-detail"+i+"-lod"+level,center,combined?10:Mathf.Max(2,size*.7f));
                    }
                }
            }
            for(int i=0;i<filters.Length;i++)filters[i].sharedMesh=originals[i];
            File.WriteAllText(Path.Combine(output,"frames",group+"-labels.json"),JsonConvert.SerializeObject(selected.Select((f,i)=>new{index=i,node=f.name}),Formatting.Indented));
        }
        void Start()
        {
            try{
                if(!NativeSilentStartup.IsSilent)throw new InvalidOperationException("Review requires startup mute");
                var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-mapModelLodReview");if(index<0)throw new InvalidOperationException("Missing owned review root");output=args[index+1];Directory.CreateDirectory(Path.Combine(output,"frames"));
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.52f,.55f,.60f);
                var sun=new GameObject("Review light").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.8f;sun.transform.rotation=Quaternion.Euler(50,-35,0);
                cameraView=new GameObject("Review camera").AddComponent<Camera>();cameraView.gameObject.AddComponent<UniversalAdditionalCameraData>();cameraView.orthographic=true;cameraView.nearClipPlane=.01f;cameraView.clearFlags=CameraClearFlags.SolidColor;cameraView.backgroundColor=new Color(.075f,.10f,.13f);target=new RenderTexture(640,480,24);target.Create();cameraView.targetTexture=target;
                foreach(var prefab in Resources.LoadAll<GameObject>("Wrecks").OrderBy(p=>p.name)){
                    var model=Object.Instantiate(prefab);try{Review("wreck-"+prefab.name,model,true);}finally{Object.DestroyImmediate(model);}
                }
                foreach(string key in new[]{"ThreeCrossings","WhiteMeridian","CrimsonGrove","MossHighland","BasaltCrown","Foundry","Campaign","AiTest","ProvingGround","Diagnostic","Nereida"}){
                    var host=new GameObject(key);PlayableWorld world=null;
                    try{if(key=="Nereida")host.AddComponent<NereidaPreview>().Build(NereidaPreview.LoadProfile());else{world=new PlayableWorld(host.transform,PlayableProfile.Create(Profile<PlayableProfileData>("PlayableProfile"),Map(key)));world.InspectionReveal();}Review(key,host,false);}
                    finally{world?.Dispose();Object.DestroyImmediate(host);}Debug.Log("MAP_LOD_REVIEW_PLAYER "+key);
                }
                File.WriteAllText(Path.Combine(output,"player-result.json"),JsonConvert.SerializeObject(new{result="PASS",classification="NATIVE_PLAYER_CANDIDATES_NOT_ACTIVE_GAME_LOD_OR_FPS",muted=true,meshes=measurements},Formatting.Indented));
                foreach(var pair in cache)foreach(var m in pair.Value.Skip(1).Distinct().Where(m=>m!=pair.Value[0]))Object.DestroyImmediate(m);
                cameraView.targetTexture=null;target.Release();Object.DestroyImmediate(target);Application.Quit(0);
            }catch(Exception e){Debug.LogException(e);Application.Quit(1);}
        }
#endif
    }
}
