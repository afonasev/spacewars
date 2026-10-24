using System;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Spacewars.Editor
{
    // Asset-only authoring. Ordinary game resource keys and source normalization remain unchanged.
    public static class RefineryModelAssets
    {
        const string Sources = "Assets/Spacewars/Content/StyleA/";
        const string ResourcesFolder = "Assets/Spacewars/Content/Resources/StyleA/";
        const string ScenePath = Sources + "Preview/RefineryTwinTowerContext.unity";
        static readonly Color Owner = new Color(.19f,.72f,.77f);
        static string Evidence => Path.GetFullPath("../design/refinery-twin-towers/previews");
        static void Require(bool ok,string message) { if(!ok) throw new InvalidOperationException(message); }

        [MenuItem("Spacewars/Assets/Refinery/Prepare approved tower pair")]
        public static void Prepare()
        {
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            Directory.CreateDirectory(Evidence);
            var camera=Setup();
            // Temporary baseline GLBs are extracted from recorded Git baseline by the authoring command.
            foreach(string key in new[]{"refinery","refineryUpgraded"})
            {
                var beforePath=Sources+key+"-before.generated.glb";
                if(File.Exists(beforePath))
                {
                    AssetDatabase.ImportAsset(beforePath,ImportAssetOptions.ForceSynchronousImport);
                    var old=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(beforePath));
                    BindMaterials(old);Paint(old,Owner);Shot(camera,"before-"+key,new Vector3(5,4.7f,7),new Vector3(0,1.12f,0),2.15f);
                    Object.DestroyImmediate(old);
                }
                string path=Sources+key+".glb";
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);Require(source,"Missing imported "+key);
                var model=Object.Instantiate(source);model.name=key;
                try
                {
                    BindMaterials(model,true);Validate(model,key);
                    PrefabUtility.SaveAsPrefabAsset(model,ResourcesFolder+key+".prefab");
                    Paint(model,Owner);Shot(camera,key,new Vector3(5,4.7f,7),new Vector3(0,1.12f,0),2.15f);
                }
                finally { Object.DestroyImmediate(model); }
            }
            AssetDatabase.SaveAssets();
            RenderContext(camera);
            DockPortraitBaker.BakeRefineries();
            Debug.Log("REFINERY_NATIVE_IMPORT_RENDER_PASS");
        }

        static void BindMaterials(GameObject model,bool detailed=false)
        {
            foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    string role=materials[i].name;
                    string path=ResourcesFolder+"refinery-"+role+".mat";
                    if(detailed && (role=="neutral-metal" || role=="foundation-concrete"))path=ResourcesFolder+"refinery-towers-"+role+".mat";
                    var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(!material && detailed && role=="foundation-concrete")
                    {
                        material=new Material(AssetDatabase.LoadAssetAtPath<Material>(ResourcesFolder+"refinery-foundation-concrete.mat")){name="refinery-towers-foundation-concrete"};
                        material.SetTexture("_BaseMap",Resources.Load<Texture2D>("Environment/NaturalFrontier/Concrete"));AssetDatabase.CreateAsset(material,path);
                    }
                    if(!material && detailed && role=="neutral-metal")
                    {
                        var template=AssetDatabase.LoadAssetAtPath<Material>(ResourcesFolder+"refinery-neutral-metal.mat");
                        material=new Material(template){name="refinery-towers-neutral-metal"};
                        // Authored near-black trim multiplier; shared ActorMaterialProfile still controls metal response.
                        material.SetColor("_BaseColor",new Color(.035f,.045f,.055f));AssetDatabase.CreateAsset(material,path);
                    }
                    if(!material && role=="system-emissive")
                    {
                        material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="refinery-system-emissive"};
                        // Authored source color is replaced by owner paint/emission through the native role below.
                        material.SetColor("_BaseColor",new Color(1,.45f,.08f));material.SetColor("_EmissionColor",new Color(1,.45f,.08f));material.EnableKeyword("_EMISSION");
                        material.SetFloat("_Smoothness",.25f);AssetDatabase.CreateAsset(material,path);
                    }
                    Require(material,"Missing existing refinery material role "+role);
                    if(role=="system-emissive")
                    {material.name="refinery-body-team-emissive";material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;material.EnableKeyword("_EMISSION");EditorUtility.SetDirty(material);}
                    if(role=="team-emissive")
                    {material.name="refinery-beacon-team-emissive";material.SetFloat("_SpecularHighlights",0);EditorUtility.SetDirty(material);}
                    materials[i]=material;
                }
                renderer.sharedMaterials=materials;
            }
        }
        static void Paint(GameObject model,Color owner)
        {
            var finish=ActorMaterialProfile.Load();
            foreach(var r in model.GetComponentsInChildren<Renderer>())for(int i=0;i<r.sharedMaterials.Length;i++)finish.Apply(r,i,owner);
        }
        static Bounds BoundsOf(GameObject model)
        {
            var rs=model.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;
            foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;
        }
        static void Validate(GameObject model,string key)
        {
            Require(model.GetComponentsInChildren<Collider>(true).Length==0,"No collider may be added");
            var nodes=model.GetComponentsInChildren<Transform>(true);
            Require(nodes.Count(t=>t.name=="modelRoot")==1,"Missing modelRoot");
            Require(nodes.Count(t=>t.name=="turbineYaw")== (key=="refineryUpgraded"?1:0),"Tier/pivot mismatch");
            var meshes=model.GetComponentsInChildren<MeshFilter>(true);
            Require(meshes.Length>=7 && meshes.Length<=8,"Static grouping unexpectedly changed");
            foreach(var m in meshes)Require(m.sharedMesh && m.sharedMesh.uv.Length==m.sharedMesh.vertexCount && m.sharedMesh.vertexCount>0,"Invalid UV/mesh "+m.name);
            var support=meshes.Single(m=>m.name.EndsWith(":foundation-concrete"));
            Require(Mathf.Abs(support.sharedMesh.bounds.size.x-2.7f)<.001f && Mathf.Abs(support.sharedMesh.bounds.size.z-2.35f)<.001f,"Existing support datum changed");
            var b=BoundsOf(model);
            Require(Mathf.Abs(b.size.y-2.545f)<.002f && Mathf.Abs(b.min.y)<.001f,"Authored height/pivot changed: "+b);
            Require(b.size.x<=2.702f && b.size.z<=2.414f,"Existing footprint envelope exceeded: "+b);
            int triangles=meshes.Sum(m=>m.sharedMesh.triangles.Length/3);Require(triangles<35000,"Unexpected geometry expansion");
            File.WriteAllText(Path.Combine(Evidence,key+"-import.json"),JsonUtility.ToJson(new ImportReport{key=key,unity=Application.unityVersion,triangles=triangles,meshes=meshes.Length,dimensions=b.size,result="PASS"},true));
        }
        [Serializable] class ImportReport { public string key,unity,result; public int triangles,meshes; public Vector3 dimensions; }
        static Camera Setup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.52f,.55f,.60f);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.name="Neutral model review ground";Object.DestroyImmediate(ground.GetComponent<Collider>());ground.transform.localScale=Vector3.one*4;ground.transform.position=new Vector3(0,-.012f,0);
            const string path=ResourcesFolder+"refinery-preview-ground.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.25f,.27f,.28f));material.SetFloat("_Smoothness",.08f);AssetDatabase.CreateAsset(material,path);}
            ground.GetComponent<Renderer>().sharedMaterial=material;
            var light=new GameObject("Review key").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(50,-35,0);
            var fill=new GameObject("Review fill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.45f;fill.transform.rotation=Quaternion.Euler(35,140,0);
            var camera=new GameObject("Refinery review camera").AddComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.25f,.27f,.28f);camera.nearClipPlane=.05f;camera.farClipPlane=100;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;return camera;
        }
        static void Shot(Camera camera,string name,Vector3 position,Vector3 target,float size,int width=1600,int height=1000)
        {
            camera.transform.position=position;camera.transform.LookAt(target);camera.orthographicSize=size;
            var rt=new RenderTexture(width,height,24);var image=new Texture2D(width,height,TextureFormat.RGB24,false);var old=RenderTexture.active;
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Evidence,name+".png"),image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
        }
        static GameObject Spawn(string key,Vector3 position,float scale,Color owner)
        {
            var model=Object.Instantiate(Resources.Load<GameObject>("StyleA/"+key));model.name=key;model.transform.position=position;model.transform.localScale=Vector3.one*scale;Paint(model,owner);return model;
        }
        static void RenderContext(Camera camera)
        {
            var profile=PlayableProfile.Default;float scale=(float)(profile.RefineryModelHeightMeters*profile.RefineryModelScale/2.545);
            var basic=Spawn("refinery",new Vector3(-1.8f,0,0),scale,Owner);
            var upgraded=Spawn("refineryUpgraded",new Vector3(1.8f,0,0),scale,Owner);
            Shot(camera,"pair-close",new Vector3(6,7,11),new Vector3(0,.8f,0),3.9f);
            Spawn("headquarters",new Vector3(-4,0,-4),(float)(profile.HeadquartersModelHeightMeters*profile.HeadquartersModelScale/4.2),Owner);
            Spawn("factory",new Vector3(0,0,-4),(float)(profile.FactoryModelHeightMeters*profile.FactoryModelScale/2.06),Owner);
            Spawn("tank",new Vector3(3,0,2.5f),(float)(profile.TankModelRadius*profile.TankModelScale/1.585),Owner);
            Shot(camera,"context-game-scale",new Vector3(10,13,18),new Vector3(0,.6f,-1),7);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);
            // Remaining colors are presentation-only, using unchanged owner-material pipeline.
            foreach(var o in new[]{basic,upgraded})Object.DestroyImmediate(o);
            var colors=new[]{new Color(.93f,.34f,.23f),new Color(.44f,.75f,.20f),new Color(.62f,.35f,.85f)};
            for(int i=0;i<3;i++)Spawn("refineryUpgraded",new Vector3((i-1)*3.5f,0,1),scale,colors[i]);
            Shot(camera,"owner-colors",new Vector3(6,8,15),new Vector3(0,.8f,1),6.3f);
            AssetDatabase.SaveAssets();
        }
    }
}
