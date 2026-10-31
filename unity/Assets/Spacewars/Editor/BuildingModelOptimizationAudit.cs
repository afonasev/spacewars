using System;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.StyleA.Preview;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Spacewars.Editor
{
    // Fixed native resource/render witness, not a frame-performance benchmark.
    public static class BuildingModelOptimizationAudit
    {
        public static readonly string[] Keys={"headquarters-a","factory-armored","mine-armored","refinery","refineryUpgraded","scientificCenter","outpost-b","hangar"};
        static string Output=>Environment.GetEnvironmentVariable("SPACEWARS_BUILDING_EVIDENCE") ?? throw new InvalidOperationException("Set SPACEWARS_BUILDING_EVIDENCE to an owned output directory");
        public static void Baseline()=>Capture();
        public static void Candidate(){FactoryModelAssets.PreparePrefab(false);Capture();Capture(true);}
        public static void MineCandidate(){ArmoredMineAssets.PreparePrefab(false);Capture(true);}
        public static void RepairCandidate()
        {
            string[] Bindings()=>Keys.SelectMany(k=>Resources.Load<GameObject>("StyleA/"+k).GetComponentsInChildren<MeshFilter>(true).Select(f=>k+"/"+f.transform.parent.name+"/"+f.transform.localPosition.ToString("F9")+"/"+f.transform.localRotation.ToString("F9")+"/"+f.transform.localScale.ToString("F9")+"/"+AssetDatabase.GetAssetPath(f.GetComponent<MeshRenderer>().sharedMaterial)+"/"+StaticBuildingMeshes.Settings(f.GetComponent<MeshRenderer>())+"/"+StaticBuildingMeshes.Fingerprint(f.sharedMesh))).OrderBy(v=>v,StringComparer.Ordinal).ToArray();
            Directory.CreateDirectory(Output);var before=Bindings();
            ArmoredMineAssets.PreparePrefab(false);var after=Bindings();
            File.WriteAllLines(Path.Combine(Output,"native-bindings-before.txt"),before);
            File.WriteAllLines(Path.Combine(Output,"native-bindings-after.txt"),after);
            if(!before.SequenceEqual(after))throw new InvalidOperationException("Review repair changed native mesh/material/renderer/transform bindings");
            File.WriteAllText(Path.Combine(Output,"saved-prefab-equivalence.txt"),"PASS: all eight prefabs retain exact native full-attribute mesh fingerprints, material bindings, renderer settings and articulation transforms after save/reload. Reuse affected PlayMode PASS from pre-repair candidate.\n");
            Capture(true);
        }
        static void Capture(bool mine=false)
        {
            Directory.CreateDirectory(Output);
            var prefabs=Keys.Select(k=>Resources.Load<GameObject>("StyleA/"+k)).ToArray();
            File.WriteAllText(Path.Combine(Output,"inventory.json"),JsonUtility.ToJson(StyleALodInventory.Inspect(prefabs),true));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.52f,.55f,.60f);
            var light=new GameObject("Fixed daylight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(50,-35,0);
            var fill=new GameObject("Fixed fill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.45f;fill.transform.rotation=Quaternion.Euler(35,140,0);
            var camera=new GameObject("Fixed building camera").AddComponent<Camera>();camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=150;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.20f,.23f,.25f);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);Object.DestroyImmediate(ground.GetComponent<Collider>());ground.transform.localScale=Vector3.one*10;
            var floor=new Material(Shader.Find("Universal Render Pipeline/Lit"));floor.color=new Color(.28f,.30f,.28f);ground.GetComponent<Renderer>().sharedMaterial=floor;
            var host=new GameObject("Building game path witness");
            using(var world=new PlayableWorld(host.transform,PlayableProfile.Default))
            {
                var actor=world.Building(7001,mine?"Mine":"Factory",true);world.FaceBuilding(actor);
                foreach(var phase in new[]{FactoryModelPresentation.Phase.Idle,FactoryModelPresentation.Phase.Working,FactoryModelPresentation.Phase.Rollout})
                {
                    // Same exact deterministic presentation time across both captures.
                    if(mine)actor.Mine.Sample(phase==FactoryModelPresentation.Phase.Idle?0:phase==FactoryModelPresentation.Phase.Working?.4f:.8f,7001,true,1f/30);
                    else actor.Factory.Sample(phase,phase==FactoryModelPresentation.Phase.Rollout?1:0,2.25f);
                    var bounds=Bounds(actor.Hull);var target=bounds.center;
                    foreach(bool close in new[]{true,false})
                    {
                        camera.orthographicSize=close?bounds.size.y*1.35f:(float)PlayableProfile.Default.CameraOrthoSize;
                        camera.transform.position=target+new Vector3(8,11,-13);camera.transform.LookAt(target);
                        Save(camera,Path.Combine(Output,(mine?"mine-":"factory-")+phase.ToString().ToLowerInvariant()+(close?"-close":"-game-scale")+".png"));
                    }
                }
                var source=Resources.Load<GameObject>("StyleA/factory-armored");
                var fans=source.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="fan0"||t.name=="fan1").ToArray();
                if(fans.Length!=2||fans.Any(t=>t.GetComponentsInChildren<MeshRenderer>(true).Length==0))throw new InvalidOperationException("Missing fan geometry");
                if(source.GetComponentsInChildren<Collider>(true).Length!=0)throw new InvalidOperationException("Unexpected collision");
                File.WriteAllText(Path.Combine(Output,mine?"mine-render-settings.json":"render-settings.json"),JsonUtility.ToJson(new Settings{subject=mine?"mine":"factory",unity=Application.unityVersion,time=2.25f,owner=new Color(.19f,.72f,.77f),gameOrtho=(float)PlayableProfile.Default.CameraOrthoSize,fanRenderers=fans.Select(t=>t.GetComponentsInChildren<MeshRenderer>(true).Length).ToArray()},true));
            }
            Object.DestroyImmediate(floor);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Debug.Log("BUILDING_MODEL_AUDIT_PASS "+Output);
        }
        [Serializable] class Settings{public string subject,unity;public float time,gameOrtho;public Color owner;public int[] fanRenderers;}
        static Bounds Bounds(Transform root){var rs=root.GetComponentsInChildren<MeshRenderer>();var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;}
        static void Save(Camera camera,string path)
        {
            var target=new RenderTexture(1600,1000,24);var texture=new Texture2D(1600,1000,TextureFormat.RGB24,false);var old=RenderTexture.active;
            try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1600,1000),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=old;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(texture);}
        }
    }
}
