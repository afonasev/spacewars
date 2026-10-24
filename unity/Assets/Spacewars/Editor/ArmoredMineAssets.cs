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
    // Asset authoring/inspection only. No model registration or simulation mutation.
    public static class ArmoredMineAssets
    {
        const string Folder="Assets/Spacewars/Content/Resources/StyleA/";
        const string Content="Assets/Spacewars/Content/StyleA/";
        static readonly string[] Keys={"mine-armored"};
        static readonly Color Owner=new Color(.19f,.72f,.77f);
        static string Evidence=>Path.GetFullPath("../.local/armored-mine/previews");
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}

        [MenuItem("Spacewars/Assets/Armored Mine/Prepare and Render")]
        public static void Prepare()
        {
            if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            Directory.CreateDirectory(Evidence);
            foreach(string key in Keys)Import(key);
            AssetDatabase.SaveAssets();
            Render();
            Debug.Log("ARMORED_MINE_NATIVE_PASS imports, geometry, pivots, materials, owner colors and saved renders");
        }
        static void Import(string key)
        {
            string path=Content+key+".glb";
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Require(source,"Missing GLB "+key);
            var model=Object.Instantiate(source);model.name=key;
            try
            {
                foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    var materials=renderer.sharedMaterials;
                    for(int i=0;i<materials.Length;i++)materials[i]=Material(materials[i].name);
                    renderer.sharedMaterials=materials;
                }
                model.AddComponent<ArmoredMinePresentation>();
                Validate(model,key);
                PrefabUtility.SaveAsPrefabAsset(model,Folder+key+".prefab");
            }
            finally{Object.DestroyImmediate(model);}
        }
        static Material Material(string role)
        {
            string path=Folder+"mine-armored-"+role+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
            if(role=="team-primary"||role=="armor"||role=="neutral-metal"||role=="structure"||role=="rubber"||role=="warning")
            {
                var template=AssetDatabase.LoadAssetAtPath<Material>(Folder+"factory-armored-"+role+".mat");
                Require(template,"Missing approved factory material "+role);
                mat.CopyPropertiesFromMaterial(template);
                if(role=="team-primary")mat.SetColor("_BaseColor",Owner);
                Require(mat.GetTexture("_BaseMap")==template.GetTexture("_BaseMap"),"Surface texture mismatch");
            }
            else if(role=="foundation-concrete")
            {
                var template=AssetDatabase.LoadAssetAtPath<Material>(Folder+"headquarters-foundation-concrete.mat");
                Require(template,"Missing current concrete material");mat.CopyPropertiesFromMaterial(template);
                Require(mat.GetTexture("_BaseMap")==template.GetTexture("_BaseMap"),"Concrete texture mismatch");
            }
            else if(role=="well-hot-emissive")
            {
                mat.SetColor("_BaseColor",new Color(.45f,.009f,.004f));mat.SetFloat("_Metallic",.05f);mat.SetFloat("_Smoothness",.15f);
                mat.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",new Color(1,.015f,.008f)*ArmoredMineProfile.Load().wellEmission);
            }
            else if(role=="system-emissive"||role=="team-emissive")
            {
                mat.SetColor("_BaseColor",new Color(.12f,.8f,.87f));
                mat.SetFloat("_Metallic",.05f);mat.SetFloat("_Smoothness",.3f);
                mat.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
                mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",new Color(.12f,.8f,.87f)*1.3f);
            }
            else throw new InvalidOperationException("Unknown role "+role);
            // Keep authored GLB role/path stable; native exterior lights use owner paint and emission.
            mat.name=role=="system-emissive"?"mine-armored-system-team-emissive":"mine-armored-"+role;EditorUtility.SetDirty(mat);return mat;
        }
        static Transform Node(GameObject model,string name)
        {
            var matches=model.GetComponentsInChildren<Transform>(true).Where(n=>n.name==name).ToArray();
            Require(matches.Length==1,"Missing/duplicate node "+name);return matches[0];
        }
        static Bounds ExactBounds(GameObject model)
        {
            bool found=false;var result=new Bounds();
            foreach(var mf in model.GetComponentsInChildren<MeshFilter>(true))
                foreach(var v in mf.sharedMesh.vertices)
                {var p=mf.transform.TransformPoint(v);if(!found){result=new Bounds(p,Vector3.zero);found=true;}else result.Encapsulate(p);}
            Require(found,"Empty geometry");return result;
        }
        static void Validate(GameObject model,string key)
        {
            Require(model.GetComponentsInChildren<Collider>(true).Length==0,"Unexpected collider");
            foreach(string node in new[]{"modelRoot","rockerPivot","drillSlider","rodTip","hydraulicBase0","hydraulicTip0","hydraulicBarrel0","hydraulicRod0","footprintMin","footprintMax"})Node(model,node);
            var meshes=model.GetComponentsInChildren<MeshFilter>(true);
            int tri=0;
            foreach(var m in meshes)
            {
                var mesh=m.sharedMesh;Require(mesh&&mesh.vertexCount>0&&mesh.uv.Length==mesh.vertexCount,"Missing mesh/UV "+m.name);
                Require(mesh.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),"Nonfinite vertex");
                Require(mesh.uv.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)),"Nonfinite UV");
                Require(mesh.normals.Length==mesh.vertexCount&&mesh.normals.All(v=>float.IsFinite(v.sqrMagnitude)&&v.sqrMagnitude>.1f),"Invalid normals");
                tri+=mesh.triangles.Length/3;
            }
            var bounds=ExactBounds(model);
            Require(tri>1000&&tri<100000,"Mesh budget "+tri);
            Require(bounds.min.y>=-.001f&&bounds.max.y<=1.9945f&&bounds.size.x<=2.961f&&bounds.size.z<=2.961f,"Envelope "+bounds);
            File.WriteAllText(Path.Combine(Evidence,key+"-unity.json"),JsonUtility.ToJson(new Report{result="PASS",unity=Application.unityVersion,key=key,meshes=meshes.Length,triangles=tri,dimensions=bounds.size,minimum=bounds.min,footprint=new Vector2(2.96f,2.96f)},true));
        }
        [Serializable] class Report{public string result,unity,key;public int meshes,triangles;public Vector3 dimensions,minimum;public Vector2 footprint;}
        static void Paint(GameObject go,Color owner)
        {
            var profile=ActorMaterialProfile.Load();
            foreach(var r in go.GetComponentsInChildren<Renderer>(true))
                for(int i=0;i<r.sharedMaterials.Length;i++)
                {
                    profile.Apply(r,i,owner);
                    if(r.sharedMaterials[i].name.EndsWith("team-primary")||r.sharedMaterials[i].name.EndsWith("team-emissive"))
                    {var block=new MaterialPropertyBlock();r.GetPropertyBlock(block,i);Require(block.GetColor("_BaseColor")==owner,"Owner paint mismatch");
                     if(r.sharedMaterials[i].name.EndsWith("team-emissive"))Require(block.GetColor("_EmissionColor")==owner*profile.TeamEmissionFor(r.sharedMaterials[i].name),"Owner emission mismatch");}
                }
        }
        static GameObject Spawn(string key,Vector3 position,float scale,Color owner)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+key+".prefab");Require(prefab,"Missing prefab "+key);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);go.transform.position=position;go.transform.localScale=Vector3.one*scale;Paint(go,owner);return go;
        }
        static Camera Setup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.name="Neutral asset inspection ground";
            Object.DestroyImmediate(ground.GetComponent<Collider>());ground.transform.localScale=Vector3.one*5;ground.transform.position=Vector3.down*.012f;
            string path=Folder+"mine-armored-preview-ground.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
            mat.SetColor("_BaseColor",new Color(.30f,.32f,.34f));mat.SetFloat("_Smoothness",.1f);EditorUtility.SetDirty(mat);ground.GetComponent<Renderer>().sharedMaterial=mat;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.60f,.62f,.65f);
            var light=new GameObject("Soft daylight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.6f;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(50,-35,0);
            var fill=new GameObject("Fill light").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.5f;fill.transform.rotation=Quaternion.Euler(35,140,0);
            var camera=new GameObject("Headquarters model inspection camera").AddComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.30f,.32f,.34f);camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;return camera;
        }
        static void Shot(Camera camera,string name,Vector3 position,Vector3 target,float size,int width=1600,int height=1000)
        {
            camera.transform.position=position;camera.transform.LookAt(target);camera.orthographicSize=size;
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);var image=new Texture2D(width,height,TextureFormat.RGB24,false);var old=RenderTexture.active;
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Evidence,name+".png"),image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
        }
        static void Render()
        {
            var p=PlayableProfile.Default;
            float scale=(float)(p.MineModelHeightMeters*p.MineModelScale/1.9945);
            var camera=Setup();
            var mine=Spawn("mine-armored",Vector3.zero,scale,Owner);
            var rig=mine.GetComponent<ArmoredMinePresentation>();rig.Initialize();
            for(int i=0;i<3;i++)
            {
                rig.Sample(i*1.25f,0,true,0);
                Shot(camera,"mine-pose-"+i,new Vector3(4,3.3f,5),new Vector3(0,.85f*scale,0),1.95f*scale,1440,1200);
            }
            Shot(camera,"mine-rear",new Vector3(-4,3.3f,-5),new Vector3(0,.85f*scale,0),1.95f*scale,1440,1200);
            Shot(camera,"mine-top",new Vector3(0,7,.001f),Vector3.zero,1.75f*scale,1280,1280);
            mine.transform.position=new Vector3(-1.7f*scale,0,0);
            var before=Spawn("mine",new Vector3(1.7f*scale,0,0),scale,Owner);
            Shot(camera,"before-after",new Vector3(1,4,8),new Vector3(0,.7f*scale,0),2.4f*scale,2200,1200);
            Object.DestroyImmediate(before);mine.transform.position=Vector3.zero;
            Directory.CreateDirectory(Path.Combine(Evidence,"close"));Directory.CreateDirectory(Path.Combine(Evidence,"normal"));
            for(int i=0;i<120;i++)
            {
                rig.Sample(i/24f,0,true,1/24f);
                Shot(camera,"close/frame"+i.ToString("D4"),new Vector3(4,3.3f,5),new Vector3(0,.85f*scale,0),1.95f*scale,960,720);
            }
            Spawn("headquarters-a",new Vector3(-3.6f,0,-3.5f),(float)(p.HeadquartersModelHeightMeters*p.HeadquartersModelScale/4.2),Owner);
            Spawn("factory-armored",new Vector3(3.4f,0,-3.5f),(float)(p.FactoryModelHeightMeters*p.FactoryModelScale/2.06),Owner);
            Spawn("tank",new Vector3(2.1f,0,1.7f),(float)(p.TankModelRadius*p.TankModelScale/1.585),Owner);
            var second=Spawn("mine-armored",new Vector3(-3.8f,0,1),scale,Owner).GetComponent<ArmoredMinePresentation>();
            for(int i=0;i<120;i++)
            {
                rig.Sample(i/24f,0,true,1/24f);second.Sample(i/24f,1,true,1/24f);
                Shot(camera,"normal/frame"+i.ToString("D4"),new Vector3(7,10,13),new Vector3(0,.5f,-1),6.0f,1280,800);
            }
            Shot(camera,"context",new Vector3(7,10,13),new Vector3(0,.5f,-1),6.0f,1800,1200);
            AssetDatabase.SaveAssets();
        }
    }
}
