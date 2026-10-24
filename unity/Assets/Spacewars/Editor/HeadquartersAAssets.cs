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
    public static class HeadquartersAAssets
    {
        const string Folder="Assets/Spacewars/Content/Resources/StyleA/";
        const string Content="Assets/Spacewars/Content/StyleA/";
        static readonly string[] Keys={"headquarters-a"};
        static readonly Color Owner=new Color(.19f,.72f,.77f);
        static string Evidence=>Path.GetFullPath("../.local/headquarters-a/previews");
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}

        [MenuItem("Spacewars/Assets/Headquarters A/Prepare and Render")]
        public static void Prepare()
        {
            if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            Directory.CreateDirectory(Evidence);
            foreach(string key in Keys)Import(key);
            AssetDatabase.SaveAssets();
            Render();
            Debug.Log("HEADQUARTERS_A_NATIVE_PASS imports, geometry, pivots, materials, owner colors and saved renders");
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
                Validate(model,key);
                PrefabUtility.SaveAsPrefabAsset(model,Folder+key+".prefab");
            }
            finally{Object.DestroyImmediate(model);}
        }
        static Material Material(string role)
        {
            string path=Folder+"headquarters-a-"+role+".mat";
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
            else if(role=="system-emissive"||role=="team-emissive")
            {
                mat.SetColor("_BaseColor",new Color(.12f,.8f,.87f));
                mat.SetFloat("_Metallic",.05f);mat.SetFloat("_Smoothness",.3f);
                mat.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
                mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",new Color(.12f,.8f,.87f)*1.3f);
            }
            else throw new InvalidOperationException("Unknown role "+role);
            // Keep authored GLB role/path stable; native exterior lights use owner paint and emission.
            mat.name=role=="system-emissive"?"headquarters-a-system-team-emissive":"headquarters-a-"+role;EditorUtility.SetDirty(mat);return mat;
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
            foreach(string node in new[]{"modelRoot","antennaYaw","entrySocket","footprintMin","footprintMax"})Node(model,node);
            foreach(string landmark in new[]{"command-entry","command-service-bays","heavy-buttresses","layered-octagonal-bunker","roof-beacon","twin-panel-antenna"})Node(model,"landmark__hq__"+landmark);
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
            Require(tri>1000&&tri<80000,"Mesh budget "+tri);
            Require(Mathf.Abs(bounds.min.y)<.001f&&bounds.size.x<=3.69f&&bounds.size.z<=3.12f&&Mathf.Abs(bounds.size.y-4.2f)<.002f,"Envelope "+bounds);
            var footprint=Node(model,"footprintMax").position-Node(model,"footprintMin").position;
            Require(Mathf.Abs(Mathf.Abs(footprint.x)-3.68f)<.001f&&Mathf.Abs(Mathf.Abs(footprint.z)-3.10f)<.001f,"Footprint changed");
            // Exercise the authored joints and restore exact bind pose. This validates
            // hierarchy only, not runtime animation or collision-free motion.
            var tilt=Node(model,"antennaYaw");var initial=tilt.localRotation;
            tilt.localRotation=initial*Quaternion.Euler(0,25,0);
            Require(float.IsFinite(ExactBounds(model).size.sqrMagnitude),"Invalid articulated bounds");
            tilt.localRotation=initial;
            File.WriteAllText(Path.Combine(Evidence,key+"-unity.json"),JsonUtility.ToJson(new Report{result="PASS",unity=Application.unityVersion,key=key,meshes=meshes.Length,triangles=tri,dimensions=bounds.size,minimum=bounds.min,footprint=new Vector2(3.68f,3.10f)},true));
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
            string path=Folder+"headquarters-a-preview-ground.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
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
            float scale=(float)(p.HeadquartersModelHeightMeters*p.HeadquartersModelScale/4.2);
            var camera=Setup();
            foreach(string key in new[]{"headquarters","headquarters-a"})
            {
                var go=Spawn(key,Vector3.zero,scale,Owner);
                Shot(camera,key+"-front",new Vector3(5,4.3f,7),new Vector3(0,2.05f*scale,0),2.95f*scale,1440,1440);
                Shot(camera,key+"-rear",new Vector3(-5,4.3f,-7),new Vector3(0,2.05f*scale,0),2.95f*scale,1440,1440);
                Shot(camera,key+"-top",new Vector3(0,9,.001f),Vector3.zero,2.2f*scale,1280,1280);
                Object.DestroyImmediate(go);
            }
            // Positive X appears on the left from this inspection camera.
            var before=Spawn("headquarters",new Vector3(2.25f*scale,0,0),scale,Owner);
            var after=Spawn("headquarters-a",new Vector3(-2.25f*scale,0,0),scale,Owner);
            Shot(camera,"before-after",new Vector3(1,5,10),new Vector3(0,1.6f*scale,0),3.0f*scale,2200,1200);
            Object.DestroyImmediate(before);Object.DestroyImmediate(after);
            var colors=new[]{Owner,new Color(.82f,.22f,.16f),new Color(.25f,.66f,.30f),new Color(.66f,.34f,.8f)};
            var models=colors.Select((c,i)=>Spawn("headquarters-a",new Vector3((i-1.5f)*4.1f*scale,0,0),scale,c)).ToArray();
            Shot(camera,"owner-colors",new Vector3(1,6,13),new Vector3(0,1.2f*scale,0),4.8f*scale,2400,1150);
            foreach(var go in models)Object.DestroyImmediate(go);
            Spawn("headquarters-a",Vector3.zero,scale,Owner);
            Spawn("headquarters",new Vector3(-4.3f,0,0),scale,Owner);
            Spawn("factory-armored",new Vector3(3.7f,0,-.4f),(float)(p.FactoryModelHeightMeters*p.FactoryModelScale/2.06),Owner);
            Spawn("research-scanner",new Vector3(-2.6f,0,-4),(float)(p.ScienceModelHeightMeters*p.ScienceModelScale/1.6407),Owner);
            Spawn("hangar",new Vector3(1.5f,0,-4),(float)(p.HangarModelHeightMeters*p.HangarModelScale/1.25),Owner);
            Spawn("tank",new Vector3(.3f,0,2.5f),(float)(p.TankModelRadius*p.TankModelScale/1.585),Owner);
            Shot(camera,"context",new Vector3(7,11,14),new Vector3(0,.7f,-1),6.2f,2200,1400);
            EditorSceneManager.SaveScene(camera.gameObject.scene,Content+"Preview/HeadquartersAContext.unity");
            AssetDatabase.SaveAssets();
        }
    }
}
