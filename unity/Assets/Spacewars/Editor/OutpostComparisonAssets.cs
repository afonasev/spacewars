using System;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Spacewars.Editor
{
    public static class OutpostComparisonAssets
    {
        const string Folder="Assets/Spacewars/Content/Resources/StyleA/";
        const string Content="Assets/Spacewars/Content/StyleA/";
        public static void Prepare()
        {
            Directory.CreateDirectory("../.local/outpost-comparison");
            foreach(var key in new[]{"outpost-a","outpost-b"})
            {
                AssetDatabase.ImportAsset(Content+key+".glb",ImportAssetOptions.ForceSynchronousImport);
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(Content+key+".glb");
                if(!source)throw new Exception("Missing imported "+key);
                var go=Object.Instantiate(source);go.name=key;
                try
                {
                    foreach(var r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        var materials=r.sharedMaterials;
                        for(int i=0;i<materials.Length;i++)
                        {
                            var role=materials[i].name;
                            string path=Folder+"outpost-comparison-"+role+".mat";
                            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                            if(!mat)
                            {
                                string template=role=="foundation-concrete"?"headquarters-foundation-concrete":role=="team-emissive"?"headquarters-a-team-emissive":"factory-armored-"+role;
                                var sourceMat=AssetDatabase.LoadAssetAtPath<Material>(Folder+template+".mat");
                                if(!sourceMat)throw new Exception("Missing material "+template);
                                mat=new Material(sourceMat);mat.name="outpost-comparison-"+role;AssetDatabase.CreateAsset(mat,path);
                            }
                            materials[i]=mat;
                        }
                        r.sharedMaterials=materials;
                    }
                    if(go.GetComponentsInChildren<Collider>().Length!=0)throw new Exception("Unexpected collider");
                    var meshes=go.GetComponentsInChildren<MeshFilter>();int triangles=0;var bounds=new Bounds();bool first=true;
                    foreach(var mf in meshes)
                    {
                        var mesh=mf.sharedMesh;
                        if(mesh.uv.Length!=mesh.vertexCount||mesh.normals.Length!=mesh.vertexCount)throw new Exception("Missing UV/normals");
                        if(mesh.uv.Any(v=>!float.IsFinite(v.x)||!float.IsFinite(v.y)))throw new Exception("Invalid UV");
                        foreach(var v in mesh.vertices)
                        {
                            var p=mf.transform.TransformPoint(v);if(!float.IsFinite(p.sqrMagnitude))throw new Exception("Invalid geometry");
                            if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);
                        }
                        triangles+=mesh.triangles.Length/3;
                    }
                    if(Mathf.Abs(bounds.min.y)>.001f||Mathf.Abs(bounds.max.y-4.5094f)>.002f||bounds.size.x>2.06f||bounds.size.z>1.85f||triangles>80000)throw new Exception("Model envelope "+bounds);
                    foreach(var name in new[]{"modelRoot","antennaYaw","entrySocket","footprintMin","footprintMax"})
                        if(go.GetComponentsInChildren<Transform>().Count(t=>t.name==name)!=1)throw new Exception("Node "+name);
                    PrefabUtility.SaveAsPrefabAsset(go,Folder+key+".prefab");
                    File.WriteAllText("../.local/outpost-comparison/"+key+"-unity.txt",$"PASS bounds={bounds} triangles={triangles} meshes={meshes.Length} colliders=0 UV/normals present\n");
                }finally{Object.DestroyImmediate(go);}
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Outpost comparison - staged production map context").AddComponent<OutpostComparisonCapture>();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Content+"Preview/OutpostComparison.unity");
            Debug.Log("OUTPOST_COMPARISON_IMPORT_PASS");
        }
        public static void Build()
        {
            Prepare();
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Content+"Preview/OutpostComparison.unity"},locationPathName="../.local/outpost-comparison/OutpostComparison.app",target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception(report.summary.result.ToString());
            Debug.Log("OUTPOST_COMPARISON_BUILD_PASS");
        }
    }
}
