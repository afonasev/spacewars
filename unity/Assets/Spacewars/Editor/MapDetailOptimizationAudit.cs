using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace Spacewars.Editor
{
    // Bounded native resource inspection: constructors only, never a match/router.
    public static class MapDetailOptimizationAudit
    {
        static string Output=>Environment.GetEnvironmentVariable("SPACEWARS_ROCK_OUTPUT")??throw new InvalidOperationException("Set SPACEWARS_ROCK_OUTPUT");
        public static void PrepareWreckMeshes()
        {
            int count=UnitWreckAssets.OptimizePreparedMeshes();
            Debug.Log("WRECK_OPTIMIZATION_PREPARED "+count);
        }
        public static void Capture()
        {
            if(!Application.isPlaying)throw new InvalidOperationException("Actual PlayMode required");
            Directory.CreateDirectory(Output);var rows=new List<object>();
            void Frame(string key,GameObject source){
                if(Environment.GetEnvironmentVariable("SPACEWARS_DETAIL_FRAMES")!="1"||key.StartsWith("wreck-source/"))return;
                var clone=Object.Instantiate(source);clone.transform.position=new Vector3(10000,0,10000);clone.SetActive(true);
                var camera=new GameObject("Detail camera").AddComponent<Camera>();var light=new GameObject("Detail sunlight").AddComponent<Light>();
                try{
                    RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.52f,.55f,.60f);light.type=LightType.Directional;light.intensity=1.8f;light.transform.rotation=Quaternion.Euler(50,-35,0);
                    var renderers=clone.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)return;var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
                    camera.orthographic=true;camera.aspect=1.6f;camera.nearClipPlane=.01f;camera.farClipPlane=2000;camera.backgroundColor=new Color(.075f,.10f,.13f);camera.clearFlags=CameraClearFlags.SolidColor;
                    float size=Mathf.Max(.5f,bounds.extents.magnitude)*1.2f;camera.orthographicSize=size;camera.transform.position=bounds.center+new Vector3(0,90,-70).normalized*Mathf.Max(10,size*4);camera.transform.LookAt(bounds.center);
                    for(int mask=0;mask<8;mask++){var v=bounds.center+Vector3.Scale(bounds.extents,new Vector3((mask&1)==0?-1:1,(mask&2)==0?-1:1,(mask&4)==0?-1:1));var q=camera.WorldToViewportPoint(v);if(q.z<=0||q.x<=0||q.x>=1||q.y<=0||q.y>=1)throw new InvalidOperationException("Clipped detail "+key);}
                    MapRockOptimizationAudit.Save(camera,Path.Combine(Output,key.Replace('/','-')+".png"));
                }finally{Object.DestroyImmediate(clone);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(light.gameObject);}
            }
            void Inspect(string key,GameObject root){foreach(var f in root.GetComponentsInChildren<MeshFilter>(true)){
                var m=f.sharedMesh;if(!m)throw new InvalidOperationException("Missing mesh "+key);var r=f.GetComponent<MeshRenderer>();
                long Bytes(Mesh mesh){long n=0;using var data=Mesh.AcquireReadOnlyMeshData(mesh);for(int i=0;i<mesh.vertexBufferCount;i++)n+=data[0].GetVertexData<byte>(i).Length;return n;}
                long Indices(Mesh mesh)=>Enumerable.Range(0,mesh.subMeshCount).Sum(s=>(long)mesh.GetIndexCount(s))*(mesh.indexFormat==IndexFormat.UInt16?2:4);
                var clone=Object.Instantiate(m);try{
                    FinalProceduralMesh.IndexOwned(clone);
                    var expanded=MapRockOptimizationAudit.Expanded(m);var bounds=MapRockOptimizationAudit.BoundsBits(m.bounds);
                    var subbounds=Enumerable.Range(0,m.subMeshCount).Select(s=>MapRockOptimizationAudit.BoundsBits(m.GetSubMesh(s).bounds)).ToArray();
                    if(expanded!=MapRockOptimizationAudit.Expanded(clone)||bounds!=MapRockOptimizationAudit.BoundsBits(clone.bounds)||!subbounds.SequenceEqual(Enumerable.Range(0,clone.subMeshCount).Select(s=>MapRockOptimizationAudit.BoundsBits(clone.GetSubMesh(s).bounds))))throw new InvalidOperationException("Complete detail representation changed "+key);
                    rows.Add(new{key,node=f.name,mesh=m.name,asset=AssetDatabase.GetAssetPath(m),vertices=m.vertexCount,triangles=Enumerable.Range(0,m.subMeshCount).Sum(s=>(long)m.GetIndexCount(s)/3),submeshes=m.subMeshCount,indexFormat=m.indexFormat.ToString(),vertexBytes=Bytes(m),indexBytes=Indices(m),possibleVertices=clone.vertexCount,possibleVertexBytes=Bytes(clone),possibleIndexBytes=Indices(clone),expanded,bounds,submeshBounds=subbounds,settings=r?MapRockOptimizationAudit.Settings(r):"none",colliders=f.GetComponents<Collider>().Length});
                }finally{Object.DestroyImmediate(clone);}
            }Frame(key,root);}
            var prefabs=Resources.LoadAll<GameObject>("Wrecks").OrderBy(p=>p.name).ToArray();if(prefabs.Length!=13)throw new InvalidOperationException("Expected current 13 wreck resources");
            foreach(var prefab in prefabs){Inspect("wreck-resource/"+prefab.name,prefab);var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spacewars/Content/Wrecks/"+prefab.name+".glb");if(!source)throw new InvalidOperationException("Missing wreck source");Inspect("wreck-source/"+prefab.name,source);}
            Inspect("rallyFlag",Resources.Load<GameObject>("StyleA/rallyFlag"));
            var host=new GameObject("Remaining detail constructor witness");var assembly=typeof(PlayableWorld).Assembly;
            object Make(string type)=>Activator.CreateInstance(assembly.GetType("Spacewars.Presentation."+type,true),true);
            object Call(object instance,string method,params object[] args)=>instance.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(instance,args);
            var visuals=Make("WebProjectileVisuals");var smoke=Make("WreckSmokeVisuals");PlayableWorld world=null;bool oldTrails=NativeGraphicsSettings.GroundTrailsEnabled;
            try{
                void Effect(string key,string method,params object[] args){var obj=(GameObject)Call(visuals,method,args);Inspect(key,obj);}
                Effect("explorer-tracer","Tracer",host.transform,1f,.1f);
                foreach(bool detailed in new[]{false,true}){Effect("tank-shell/"+detailed,"Tank",host.transform,detailed);Effect("shkval-flight/"+detailed,"Rocket",host.transform,.12f,1.2f,Color.cyan,detailed);}
                Effect("shkval-loaded","LoadedRocket",host.transform,.12f,1.2f,Color.cyan);Effect("hawk-projectile","HawkRocket",host.transform,.08f,.8f,Color.cyan);
                Effect("ground-circle","GroundCircle",host.transform,Color.cyan);Effect("impact-flash","ImpactFlash",host.transform);Effect("direct-impact","DirectImpact",host.transform,307,false);Effect("tank-impact","DirectImpact",host.transform,307,true);
                var puff=(GameObject)Call(smoke,"Create",host.transform,307);Call(smoke,"Update",puff,.5f,1f,1f,.5f);Inspect("wreck-smoke",puff);
                var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());world=new PlayableWorld(host.transform,p);world.Fog.ShowPublic();NativeGraphicsSettings.GroundTrailsEnabled=true;
                var facts=new[]{new PlayableGroundTrail(new NavPoint(0,0),new NavPoint(2,0),0,0,1,PlayableEntityKind.Tank)};
                var view=new PlayableSnapshot(p.ProfileId,p.Revision,1,307,2,2,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,groundTrails:facts);
                world.RenderGroundTrails(view);var trail=host.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name=="Ground vehicle trails");Inspect("dynamic-ground-trail",trail.gameObject);
                var apron=ConstructionApronMesh.Build(new Vector2(5,5),EnvironmentArtProfile.Load());try{var g=new GameObject("Construction apron");g.transform.SetParent(host.transform);g.AddComponent<MeshFilter>().sharedMesh=apron;Inspect("construction-apron",g);}finally{Object.DestroyImmediate(apron);}
                var selection=(GameObject)typeof(PlayableWorld).GetMethod("UnitRing",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(world,new object[]{host.transform,PlayableEntityKind.Tank});Inspect("unit-selection-ring",selection);
                // Aircraft fragments use the shared native cube (same path as Part).
                var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);try{Inspect("hawk-destruction-fragment-shared-cube",cube);}finally{Object.DestroyImmediate(cube);}
            }finally{world?.Dispose();((IDisposable)visuals).Dispose();((IDisposable)smoke).Dispose();Object.DestroyImmediate(host);NativeGraphicsSettings.GroundTrailsEnabled=oldTrails;}
            File.WriteAllText(Path.Combine(Output,"remaining-details.json"),JsonConvert.SerializeObject(new{wreckResources=prefabs.Select(p=>p.name),source="actual current Resources plus maintained GLB imports; native effect constructors; no gameplay/physics claims",rows},Formatting.Indented));
            Debug.Log("MAP_DETAIL_AUDIT_PASS "+rows.Count);
        }
    }
}
