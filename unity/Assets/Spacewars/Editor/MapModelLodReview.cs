using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Spacewars.Editor
{
    // Explicit geometry-review export. Never rewrites active resources or generators.
    public static class MapModelLodReview
    {
        static string Output=>Environment.GetEnvironmentVariable("SPACEWARS_MAP_LOD_REVIEW")??throw new InvalidOperationException("Set owned review output");
        static readonly string[] Maps={"ThreeCrossings","WhiteMeridian","CrimsonGrove","MossHighland","BasaltCrown","Foundry","Campaign","AiTest","ProvingGround","Diagnostic","Nereida"};
        static T Profile<T>(string name)=>JsonUtility.FromJson<T>(Resources.Load<TextAsset>(name).text);
        public static IPlayableTerrain Map(string key)
        {
            switch(key){
                case "ThreeCrossings":return new ThreeCrossingsMap(Profile<ThreeCrossingsProfileData>(key+"Profile"));
                case "WhiteMeridian":return new WhiteMeridianMap(Profile<WhiteMeridianProfileData>(key+"Profile"));
                case "CrimsonGrove":return new CrimsonGroveMap(Profile<CrimsonGroveProfileData>(key+"Profile"));
                case "MossHighland":return new MossHighlandMap(Profile<MossHighlandProfileData>(key+"Profile"));
                case "NullDock":return new NullDockMap();
                case "BasaltCrown":return new BasaltCrownMap();
                case "Foundry":return new FoundryMap(Profile<FoundryProfileData>(key+"Profile"));
                case "Campaign":return new CampaignOpeningMap();case "AiTest":return new AiTestMap();case "ProvingGround":return new ProvingGroundMap();default:return null;
            }
        }
        // Explicit map surface exclusions: everything else is modeled content,
        // including already minimal boxes. This is an inventory, not a polygon budget.
        public static bool Modeled(string group,string name)=>ModelDetailCatalog.IsModeled(group,name);
        static float[][] V(Vector3[] a)=>a.Select(v=>new[]{v.x,v.y,v.z}).ToArray();
        static float[][] V(Vector2[] a)=>a.Select(v=>new[]{v.x,v.y}).ToArray();
        static float[][] V(Vector4[] a)=>a.Select(v=>new[]{v.x,v.y,v.z,v.w}).ToArray();
        static float[][] V(Color[] a)=>a.Select(v=>new[]{v.r,v.g,v.b,v.a}).ToArray();
        public static void ExportInPlayMode()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            EditorApplication.playModeStateChanged-=ExportAfterEnter;
            EditorApplication.playModeStateChanged+=ExportAfterEnter;
            EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] static void RestoreExportCallback()
        {
            if(Environment.GetCommandLineArgs().Contains("Spacewars.Editor.MapModelLodReview.ExportInPlayMode"))EditorApplication.playModeStateChanged+=ExportAfterEnter;
        }
        static void ExportAfterEnter(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredPlayMode)return;
            EditorApplication.playModeStateChanged-=ExportAfterEnter;
            try{Export();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void BuildGallery()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_MAP_LOD_APP")??throw new InvalidOperationException("Set owned gallery app");
            const string scene="Assets/Spacewars/Content/MapModelLodReview.generated.unity";
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            new GameObject("Explicit LOD review").AddComponent<MapModelLodGallery>();
            try{
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),scene);
                var report=UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions{scenes=new[]{scene},locationPathName=output,target=UnityEditor.BuildTarget.StandaloneOSX,options=UnityEditor.BuildOptions.Development});
                if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new InvalidOperationException(report.summary.result.ToString());
                Debug.Log("MAP_MODEL_LOD_GALLERY_BUILD_PASS");
            }finally{UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);AssetDatabase.DeleteAsset(scene);}
        }
        public static void Export()
        {
            Directory.CreateDirectory(Output+"/source");var inventory=new List<object>();var saved=new HashSet<string>();
            void Inspect(string group,GameObject host){
                foreach(var f in host.GetComponentsInChildren<MeshFilter>(true)){
                    var m=f.sharedMesh;if(!m)continue;
                    string hash=MapRockOptimizationAudit.Expanded(m);long triangles=Enumerable.Range(0,m.subMeshCount).Sum(i=>(long)m.GetIndexCount(i)/3);
                    var attached=f.GetComponentInParent<ModelDetailLod>();bool existing=attached&&!attached.IsCatalogBound;
                    bool modeled=triangles>0&&Modeled(group,f.name);
                    inventory.Add(new{group,node=f.name,hash,vertices=m.vertexCount,triangles,submeshes=m.subMeshCount,modeled,existingLod=existing,reason=existing?"existing-authored-three-tiers":modeled?(triangles<100?"already-small-measured-source":"review-reduction"):triangles==0?"empty-authored-part-not-rendered":"support-road-water-surface-kept-exact"});
                    if(existing||!modeled||triangles<100||!saved.Add(hash)||File.Exists(Output+"/source/"+hash+".json"))continue;
                    if(m.blendShapeCount>0)throw new InvalidOperationException("Blend shape needs explicit preservation "+f.name);
                    File.WriteAllText(Output+"/source/"+hash+".json",JsonConvert.SerializeObject(new{hash,name=f.name,vertices=V(m.vertices),normals=V(m.normals),uv=V(m.uv),colors=V(m.colors),tangents=V(m.tangents),submeshes=Enumerable.Range(0,m.subMeshCount).Select(i=>m.GetIndices(i,true)).ToArray()},Formatting.None));
                }
            }
            foreach(var prefab in Resources.LoadAll<GameObject>("Wrecks").OrderBy(p=>p.name))Inspect("Wrecks",prefab);
            foreach(string key in Maps){
                var host=new GameObject(key);PlayableWorld world=null;
                try{
                    if(key=="Nereida")host.AddComponent<NereidaPreview>().Build(NereidaPreview.LoadProfile());
                    else{var p=PlayableProfile.Create(Profile<PlayableProfileData>("PlayableProfile"),Map(key));world=new PlayableWorld(host.transform,p);}
                    Inspect(key,host);Debug.Log("MAP_LOD_EXPORT "+key);
                }finally{world?.Dispose();Object.DestroyImmediate(host);}
            }
            File.WriteAllText(Output+"/inventory.json",JsonConvert.SerializeObject(inventory,Formatting.Indented));
            Debug.Log("MAP_LOD_REVIEW_EXPORT_PASS meshes="+saved.Count);
        }
    }
}
