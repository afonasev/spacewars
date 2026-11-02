using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Spacewars.Editor
{
    // Maintained native source for direction B. Existing model meshes, rigs,
    // materials and scale are referenced, never regenerated or modified.
    public static class ProducerEnergyModelAssets
    {
        const string Folder = "Assets/Spacewars/Content/Resources/ProducerEnergy/";
        static readonly List<CombineInstance>[] parts = { new(), new(), new() };
        static readonly List<Mesh> transient = new();
        static void Face(List<Vector3> v, List<int> t, params Vector3[] face)
        {
            int start=v.Count;v.AddRange(face);
            for(int i=1;i<face.Length-1;i++){t.Add(start);t.Add(start+i);t.Add(start+i+1);}
        }
        static void Extrude(int role, Vector2[] outline, float bottom, float top)
        {
            var v=new List<Vector3>();var t=new List<int>();
            var upper=outline.Select(p=>new Vector3(p.x,top,p.y)).ToArray();
            var lower=outline.Select(p=>new Vector3(p.x,bottom,p.y)).ToArray();
            Face(v,t,upper.Reverse().ToArray());Face(v,t,lower);
            for(int i=0;i<outline.Length;i++){int j=(i+1)%outline.Length;Face(v,t,lower[i],upper[i],upper[j],lower[j]);}
            var mesh=new Mesh{name="energy-part"};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();
            var normals=mesh.normals;mesh.uv=v.Select((p,i)=>Mathf.Abs(normals[i].y)>.5f?new Vector2(p.x,p.z):new Vector2(p.x+p.z,p.y)).ToArray();
            mesh.RecalculateTangents();mesh.RecalculateBounds();transient.Add(mesh);
            parts[role].Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.identity});
        }
        static void Block(int role,float x,float y,float z,float width,float height,float depth,float bevel=.04f)
        {
            float a=width/2,b=depth/2,c=Mathf.Min(bevel,Mathf.Min(a,b)*.4f);
            Extrude(role,new[]{new Vector2(x-a+c,z-b),new Vector2(x+a-c,z-b),new Vector2(x+a,z-b+c),new Vector2(x+a,z+b-c),new Vector2(x+a-c,z+b),new Vector2(x-a+c,z+b),new Vector2(x-a,z+b-c),new Vector2(x-a,z-b+c)},y-height/2,y+height/2);
        }
        static void Arc(int role,float inner,float outer,float bottom,float top,float start,float sweep,int steps)
        {
            var outline=new List<Vector2>();
            for(int i=0;i<=steps;i++){float a=(start+sweep*i/steps)*Mathf.Deg2Rad;outline.Add(new Vector2(outer*Mathf.Cos(a),outer*Mathf.Sin(a)));}
            for(int i=steps;i>=0;i--){float a=(start+sweep*i/steps)*Mathf.Deg2Rad;outline.Add(new Vector2(inner*Mathf.Cos(a),inner*Mathf.Sin(a)));}
            // Concave annular patches are emitted as convex radial quads.
            for(int i=0;i<steps;i++)Extrude(role,new[]{outline[outline.Count-1-i],outline[i],outline[i+1],outline[outline.Count-2-i]},bottom,top);
        }
        static void Geometry(bool factory,int tier)
        {
            foreach(var row in parts)row.Clear();
            if(factory)
            {
                foreach(int side in new[]{-1,1})
                {
                    float x=side*1.19f;
                    Block(1,x,2.23f,.02f,.46f,.24f,2.05f,.075f);
                    Block(0,x,2.35f,.02f,.40f,.20f,1.97f,.07f);
                    for(int i=0;i<3;i++)
                    {
                        float z=-.61f+i*.63f;
                        Block(1,x,2.455f,z,.32f,.045f,.54f,.04f);
                        Block(2,x,2.481f,z,.235f,.018f,.43f,.025f);
                        if(tier==0)foreach(int sign in new[]{-1,1})Block(1,x+sign*.17f,2.40f,z,.025f,.06f,.38f,.004f);
                    }
                    foreach(float z in new[]{-.96f,-.31f,.32f,.96f})Block(1,x,2.46f,z,.42f,.09f,.10f,.02f);
                    // Distinct neutral border keeps light panels from becoming white paint.
                    Block(1,side*1.28f,1.58f,1.31f,.10f,.49f,.045f,.015f);
                    Block(2,side*1.28f,1.58f,1.338f,.045f,.37f,.013f,.008f);
                }
                Block(1,0,2.42f,-1.25f,1.35f,.27f,.29f,.055f);
                Block(0,0,2.55f,-1.25f,1.20f,.10f,.24f,.035f);
                for(int i=0;i<3;i++)Block(2,-.37f+i*.37f,2.46f,-1.091f,.28f,.055f,.015f,.008f);
            }
            else
            {
                int steps=tier==0?12:tier==1?6:3;
                for(int i=0;i<4;i++)
                {
                    float a=i*90+7;
                    Arc(1,.99f,1.35f,1.16f,1.27f,a,76,steps);
                    Arc(0,1.02f,1.32f,1.27f,1.32f,a+2,72,steps);
                    Arc(1,1.065f,1.285f,1.32f,1.335f,a+5,66,steps);
                    Arc(2,1.10f,1.25f,1.335f,1.342f,a+7,62,steps);
                    Arc(1,.99f,1.35f,1.16f,1.36f,i*90-5,10,2);
                    // Compact wall service equipment stays inside existing foundation radius.
                    float angle=(i*90+45)*Mathf.Deg2Rad;
                    float x=1.20f*Mathf.Cos(angle),z=1.20f*Mathf.Sin(angle);
                    Block(1,x,.73f,z,.22f,.54f,.22f,.035f);
                    Block(0,x,.99f,z,.23f,.08f,.23f,.04f);
                    if(tier<2)for(int j=0;j<3;j++)Block(1,x,.55f+j*.12f,z,.24f,.04f,.24f,.045f);
                }
            }
        }
        static Mesh Save(Mesh mesh,string path)
        {
            var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(old){EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);return old;}
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static void BuildModel(string key)
        {
            bool factory=key=="factory-armored";
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spacewars/Content/Resources/StyleA/Lod/"+key+".prefab");
            if(!source)throw new InvalidOperationException("Missing baseline "+key);
            var model=Object.Instantiate(source);model.name=key+"-energy";
            try
            {
                var lod=model.GetComponent<ModelDetailLod>();
                var renderers=lod.Renderers.ToList();var full=lod.OriginalMeshes.ToList();var normal=lod.NormalMeshes.ToList();var far=lod.FarMeshes.ToList();
                var parent=factory?model.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="modelRoot"):model.transform;
                var newRenderers=new List<Renderer>();
                var mats=new[]{"factory-armored-team-primary","factory-armored-structure","factory-armored-lamp-emissive"}.Select(n=>AssetDatabase.LoadAssetAtPath<Material>("Assets/Spacewars/Content/Resources/StyleA/"+n+".mat")).ToArray();
                if(mats.Any(m=>!m))throw new InvalidOperationException("Missing baseline materials");
                for(int tier=0;tier<3;tier++)
                {
                    Geometry(factory,tier);
                    for(int role=0;role<3;role++)
                    {
                        var mesh=new Mesh{name=model.name+"-"+tier+"-"+role};mesh.CombineMeshes(parts[role].ToArray());mesh.RecalculateBounds();
                        mesh=Save(mesh,Folder+mesh.name+".asset");
                        (tier==0?full:tier==1?normal:far).Add(mesh);
                        if(tier==0){var go=new GameObject("energy-"+role);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=mats[role];renderers.Add(r);newRenderers.Add(r);}
                    }
                    foreach(var m in transient)Object.DestroyImmediate(m);transient.Clear();
                }
                var bounds=lod.ReferenceBounds;
                foreach(var r in newRenderers){var b=r.bounds;bounds.Encapsulate(b.min);bounds.Encapsulate(b.max);}
                lod.Configure(renderers.ToArray(),full.ToArray(),normal.ToArray(),far.ToArray(),bounds);
                PrefabUtility.SaveAsPrefabAsset(model,Folder+model.name+".prefab");
            }
            finally{Object.DestroyImmediate(model);}
        }
        [MenuItem("Spacewars/Assets/Producer Energy/Prepare Models")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            BuildModel("factory-armored");BuildModel("hangar");AssetDatabase.SaveAssets();
            Validate();Debug.Log("PRODUCER_ENERGY_ASSETS_PASS");
        }
        [Serializable] class Row {public string model;public int tier,vertices,triangles,renderers,materials,submeshes;public long meshBytes;}
        [Serializable] class Report {public string result="PASS";public Row[] rows;public string checks="Base meshes/materials/rig unchanged; three LOD bindings; attachment bounds; no colliders; same gate and iris nodes";}
        public static void Validate()
        {
            var rows=new List<Row>();
            foreach(string key in new[]{"factory-armored","hangar"})
            {
                var baseline=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spacewars/Content/Resources/StyleA/Lod/"+key+".prefab");
                var candidate=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+key+"-energy.prefab");
                var a=baseline.GetComponent<ModelDetailLod>();var b=candidate.GetComponent<ModelDetailLod>();
                if(candidate.GetComponentsInChildren<Collider>(true).Length!=0)throw new InvalidOperationException("Unexpected collider");
                foreach(var pair in new[]{(a.OriginalMeshes,b.OriginalMeshes),(a.NormalMeshes,b.NormalMeshes),(a.FarMeshes,b.FarMeshes)})
                    if(!pair.Item1.SequenceEqual(pair.Item2.Take(pair.Item1.Length)))throw new InvalidOperationException("Baseline geometry changed");
                foreach(var source in baseline.GetComponentsInChildren<Transform>(true).Skip(1))
                {
                    string path=AnimationUtility.CalculateTransformPath(source,baseline.transform);var target=candidate.transform.Find(path);
                    if(!target||target.localPosition!=source.localPosition||target.localRotation!=source.localRotation||target.localScale!=source.localScale)throw new InvalidOperationException("Rig changed: "+path);
                }
                foreach(var model in new[]{baseline,candidate})
                {
                    var lod=model.GetComponent<ModelDetailLod>();int tier=0;
                    foreach(var meshes in new[]{lod.OriginalMeshes,lod.NormalMeshes,lod.FarMeshes})
                    {
                        var distinct=meshes.Distinct().ToArray();
                        rows.Add(new Row{model=model.name,tier=tier++,vertices=meshes.Sum(m=>m.vertexCount),triangles=meshes.Sum(m=>m.triangles.Length/3),renderers=lod.Renderers.Length,materials=lod.Renderers.SelectMany(r=>r.sharedMaterials).Distinct().Count(),submeshes=meshes.Sum(m=>m.subMeshCount),meshBytes=distinct.Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m))});
                    }
                }
                foreach(var r in b.Renderers.Skip(a.Renderers.Length))
                {
                    var mesh=r.GetComponent<MeshFilter>().sharedMesh;
                    foreach(var vertex in mesh.vertices)
                    {
                        var p=candidate.transform.InverseTransformPoint(r.transform.TransformPoint(vertex));
                        if(float.IsNaN(p.x)||Mathf.Abs(p.x)>a.ReferenceBounds.extents.x+.02f||Mathf.Abs(p.z)>Mathf.Max(Mathf.Abs(a.ReferenceBounds.min.z),Mathf.Abs(a.ReferenceBounds.max.z))+.02f)throw new InvalidOperationException("Attachment exceeds footprint: "+key+" "+p);
                        if(key=="hangar"&&new Vector2(p.x,p.z).magnitude<.98f)throw new InvalidOperationException("Iris obstructed");
                    }
                }
            }
            string output=Environment.GetEnvironmentVariable("SPACEWARS_ENERGY_EVIDENCE")??throw new InvalidOperationException("Set owned evidence path");Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"asset-validation.json"),JsonUtility.ToJson(new Report{rows=rows.ToArray()},true));
        }
        public static void PrepareAndBuild(){Prepare();Build();}
        public static void Build()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_ENERGY_APP")??throw new InvalidOperationException("Set owned QA app path");
            const string scene="Assets/Spacewars/Content/ProducerEnergyContext.generated.unity";
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Producer energy model review").AddComponent<ProducerEnergyContext>();
            try
            {
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),scene);
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene},locationPathName=output,target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
                if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(report.summary.result.ToString());
                Debug.Log("PRODUCER_ENERGY_BUILD_PASS");
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);AssetDatabase.DeleteAsset(scene);}
        }
    }
}
