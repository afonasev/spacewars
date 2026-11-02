using System;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Spacewars.Editor
{
    // Standalone asset preparation and bounded native render/animation checks.
    // Does not register a site or change any map, simulation or game save.
    public static class RelayTowerAssets
    {
        const string Content="Assets/Spacewars/Content/StyleA/";
        const string Folder="Assets/Spacewars/Content/Resources/StyleA/";
        static readonly Color Owner=new Color(.19f,.72f,.77f);
        static string Evidence=>Environment.GetEnvironmentVariable("SPACEWARS_RELAY_EVIDENCE")??Path.GetFullPath("../.local/relay-tower/native");
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        static Transform Node(GameObject go,string name)=>go.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);

        [MenuItem("Spacewars/Assets/Relay Tower/Prepare and Render")]
        public static void Prepare()
        {
            if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            Directory.CreateDirectory(Evidence);
            var source=new GameObject[3];
            string[] keys={"relay-tower","relay-tower-normal","relay-tower-far"};
            for(int i=0;i<keys.Length;i++)
            {
                string path=Content+keys[i]+".glb";
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                source[i]=AssetDatabase.LoadAssetAtPath<GameObject>(path);Require(source[i],"Missing "+path);
            }
            var model=Object.Instantiate(source[0]);model.name="relay-tower";
            try
            {
                var meshes=model.GetComponentsInChildren<MeshFilter>(true).OrderBy(m=>m.name).ToArray();
                var tiers=source.Select(s=>s.GetComponentsInChildren<MeshFilter>(true).OrderBy(m=>m.name).ToArray()).ToArray();
                int[] triangles=new int[3];
                for(int i=0;i<3;i++)
                {
                    Require(tiers[i].Select(m=>m.name).SequenceEqual(meshes.Select(m=>m.name)),"LOD articulation/material layout");
                    foreach(var mf in tiers[i])
                    {
                        var m=mf.sharedMesh;
                        Require(m&&m.vertexCount>0&&m.uv.Length==m.vertexCount&&m.normals.Length==m.vertexCount,"Mesh data "+mf.name);
                        Require(m.vertices.All(v=>float.IsFinite(v.sqrMagnitude))&&m.uv.All(v=>float.IsFinite(v.sqrMagnitude)),"Nonfinite mesh/UV");
                        Require(m.normals.All(v=>float.IsFinite(v.sqrMagnitude)&&v.sqrMagnitude>.1f),"Invalid normals");
                        triangles[i]+=m.triangles.Length/3;
                    }
                    Require(Vector3.Distance(Node(source[i],"antennaYaw").localPosition,Node(source[0],"antennaYaw").localPosition)<.0001f,"LOD pivot drift");
                    // Mesh swapping requires exactly the same articulation-space transforms.
                    for(int j=0;j<meshes.Length;j++)Require(MatrixEqual(tiers[0][j].transform.localToWorldMatrix,tiers[i][j].transform.localToWorldMatrix),"LOD transform drift "+meshes[j].name);
                }
                Require(triangles[2]<triangles[1]&&triangles[1]<triangles[0],"No LOD reduction");
                foreach(var mf in meshes)
                {
                    var renderer=mf.GetComponent<Renderer>();
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>Material(m.name)).ToArray();
                }
                Require(model.GetComponentsInChildren<Collider>(true).Length==0,"Unexpected collider");
                var bounds=BoundsOf(model);
                Require(bounds.min.y>=-.001f&&Mathf.Abs(bounds.size.x-3.4f)<.002f&&Mathf.Abs(bounds.size.y-4.42f)<.002f,"Asset envelope "+bounds);
                var lod=model.AddComponent<ModelDetailLod>();
                lod.Configure(meshes.Select(m=>m.GetComponent<Renderer>()).ToArray(),tiers[0].Select(m=>m.sharedMesh).ToArray(),tiers[1].Select(m=>m.sharedMesh).ToArray(),tiers[2].Select(m=>m.sharedMesh).ToArray(),bounds);
                model.AddComponent<RelayTowerPresentation>();
                PrefabUtility.SaveAsPrefabAsset(model,Folder+"relay-tower.prefab");
                File.WriteAllText(Path.Combine(Evidence,"native-validation.json"),JsonUtility.ToJson(new Report{result="PASS",unity=Application.unityVersion,triangles=triangles,renderers=meshes.Length,materials=meshes.SelectMany(m=>m.GetComponent<Renderer>().sharedMaterials).Distinct().Count(),bounds=bounds.size},true));
            }
            finally{Object.DestroyImmediate(model);}
            AssetDatabase.SaveAssets();
            Render();
            Debug.Log("RELAY_TOWER_NATIVE_PASS: import, material stacks, three LODs, owner/neutral/pause/recapture rotation, native frames");
        }
        [Serializable] class Report{public string result,unity;public int[] triangles;public int renderers,materials;public Vector3 bounds;}
        static bool MatrixEqual(Matrix4x4 a,Matrix4x4 b)
        {for(int i=0;i<16;i++)if(Mathf.Abs(a[i]-b[i])>.0001f)return false;return true;}
        static Material Material(string role)
        {
            string path=Folder+"relay-tower-"+role+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
            if(role=="team-emissive")
            {
                mat.SetColor("_BaseColor",Owner);mat.SetFloat("_Metallic",.05f);mat.SetFloat("_Smoothness",.3f);
                mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",Owner);
                mat.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else
            {
                string donor=role=="foundation-concrete"?"headquarters-foundation-concrete":"factory-armored-"+role;
                var template=AssetDatabase.LoadAssetAtPath<Material>(Folder+donor+".mat");Require(template,"Missing approved material "+donor);
                mat.CopyPropertiesFromMaterial(template);
                Require(mat.GetTexture("_BaseMap")==template.GetTexture("_BaseMap"),"Texture stack changed");
                if(role=="team-primary")mat.SetColor("_BaseColor",Owner);
            }
            mat.name="relay-tower-"+role;EditorUtility.SetDirty(mat);return mat;
        }
        static Bounds BoundsOf(GameObject model)
        {
            var points=model.GetComponentsInChildren<MeshFilter>(true).SelectMany(m=>m.sharedMesh.vertices.Select(v=>m.transform.TransformPoint(v))).ToArray();
            Require(points.Length>0,"Empty mesh");var bounds=new Bounds(points[0],Vector3.zero);foreach(var p in points)bounds.Encapsulate(p);return bounds;
        }
        static GameObject Spawn(Vector3 position)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"relay-tower.prefab");Require(prefab,"Missing prefab");
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);go.transform.position=position;
            go.GetComponent<RelayTowerPresentation>().Drive(null,0);return go;
        }
        static Camera Setup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.59f,.64f);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.name="Standalone asset inspection ground";ground.transform.position=Vector3.down*.012f;ground.transform.localScale=Vector3.one*6;
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Folder+"relay-tower-foundation-concrete.mat");ground.GetComponent<Renderer>().sharedMaterial=mat;
            var light=new GameObject("Industrial daylight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.7f;light.color=new Color(1,.93f,.84f);light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(50,-35,0);
            var fill=new GameObject("Cool fill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.4f;fill.color=new Color(.78f,.87f,1);fill.transform.rotation=Quaternion.Euler(35,140,0);
            var camera=new GameObject("Relay asset review camera").AddComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.25f,.28f,.30f);camera.nearClipPlane=.05f;camera.farClipPlane=100;camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;return camera;
        }
        static void Shot(Camera camera,string name,Vector3 position,Vector3 target,float size,int width=1200,int height=1100)
        {
            camera.transform.position=position;camera.transform.LookAt(target);camera.orthographicSize=size;
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);var texture=new Texture2D(width,height,TextureFormat.RGB24,false);var old=RenderTexture.active;
            try
            {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Evidence,name+".png"),texture.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);}
        }
        static void Render()
        {
            var camera=Setup();var model=Spawn(Vector3.zero);var motion=model.GetComponent<RelayTowerPresentation>();var lod=model.GetComponent<ModelDetailLod>();
            var eye=new Vector3(6,5.8f,8);var look=new Vector3(0,2,0);
            motion.Drive(null,4);Require(motion.YawDegrees==0,"Initially neutral rotates");
            Shot(camera,"neutral",eye,look,2.65f);
            motion.Drive(Owner,0);Shot(camera,"controlled",eye,look,2.65f);
            motion.Drive(Owner,2);float angle=motion.YawDegrees;Require(Mathf.Abs(angle-motion.Profile.rotationDegreesPerSecond*2)<.001f,"Controlled yaw speed");
            motion.Drive(null,5);Require(motion.YawDegrees==angle,"Neutral must freeze");
            motion.Drive(Owner,0);Require(motion.YawDegrees==angle,"Paused match must freeze");
            motion.Drive(Owner,1);Require(Mathf.Abs(motion.YawDegrees-angle-motion.Profile.rotationDegreesPerSecond)<.001f,"Recapture jumps/catches up");
            // Show every LOD and verify rotation survives mesh swaps.
            for(int i=0;i<3;i++)
            {
                lod.SetDetail(i);Require(lod.Level==i,"LOD selection");
                motion.Drive(Owner,0);
                Shot(camera,"lod-"+i,eye,look,2.65f);
                float before=motion.YawDegrees;motion.Drive(null,1);Require(motion.YawDegrees==before,"Neutral moves after LOD swap");
            }
            lod.SelectScreenRatio(1,ModelDetailProfile.Current);Require(lod.Level==0,"Close LOD threshold");
            lod.SelectScreenRatio(0,ModelDetailProfile.Current);Require(lod.Level==2,"Far LOD threshold");lod.SetDetail(0);
            Shot(camera,"rear",new Vector3(-6,5.8f,-8),look,2.65f);
            Shot(camera,"top",new Vector3(0,10,.001f),Vector3.zero,2);
            Object.DestroyImmediate(model);
            // Native animation strip: 1s neutral, 4s captured, 1s neutral freeze.
            Directory.CreateDirectory(Path.Combine(Evidence,"motion"));model=Spawn(Vector3.zero);motion=model.GetComponent<RelayTowerPresentation>();
            for(int frame=0;frame<72;frame++)
            {motion.Drive(frame>=12&&frame<60?(Color?)Owner:null,1d/12);Shot(camera,"motion/frame-"+frame.ToString("D3"),eye,look,2.65f,640,600);}
            Object.DestroyImmediate(model);
            var a=Spawn(new Vector3(-2.3f,0,0));var b=Spawn(new Vector3(2.3f,0,0));b.GetComponent<RelayTowerPresentation>().Drive(Owner,0);
            Shot(camera,"states",new Vector3(5,7,12),new Vector3(0,1.8f,0),3.6f,1800,1100);
            Object.DestroyImmediate(a);Object.DestroyImmediate(b);
            // Existing game prefabs at their native authoring scale demonstrate palette.
            var relay=Spawn(Vector3.zero);relay.GetComponent<RelayTowerPresentation>().Drive(Owner,0);
            foreach(var entry in new[]{("factory",new Vector3(-4,0,-1)),("tank",new Vector3(3,0,2.5f))})
            {
                var prefab=Resources.Load<GameObject>("StyleA/"+entry.Item1);Require(prefab,"Missing context model "+entry.Item1);
                var go=Object.Instantiate(prefab,entry.Item2,Quaternion.identity);go.name="Current game asset: "+entry.Item1;
                foreach(var r in go.GetComponentsInChildren<Renderer>())for(int i=0;i<r.sharedMaterials.Length;i++)ActorMaterialProfile.Load().Apply(r,i,Owner);
            }
            Shot(camera,"context",new Vector3(10,11,17),new Vector3(-.7f,1.6f,0),5.7f,1800,1100);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Content+"Preview/RelayTowerContext.unity");
            File.WriteAllText(Path.Combine(Evidence,"animation-validation.json"),"{\"result\":\"PASS\",\"neutral\":\"frozen\",\"controlled\":\"match delta only\",\"pause\":\"frozen\",\"recapture\":\"resumes without jump\",\"frames\":72,\"fps\":12,\"timeline\":\"0-1s neutral, 1-5s controlled, 5-6s neutral\"}\n");
            AssetDatabase.SaveAssets();
        }
    }
}
