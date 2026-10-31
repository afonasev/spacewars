using System;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace Spacewars.Editor
{
    // Presentation-only studio: synthetic shot event and straight flight, no match or damage claims.
    public static class TankShotPreview
    {
        static PlayableSnapshot Frame(int tick) => new PlayableSnapshot("shot-studio",1,1,7319,tick+1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,new NavGeometry(20,Array.Empty<NavObstacle>(),1),Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sounds:tick>=15?new[]{new PlayableSoundEvent(1,15,PlayableSoundKind.Shot,new NavPoint(0,0),PlayableEntityKind.Tank,unitId:3)}:Array.Empty<PlayableSoundEvent>());
        public static void Render()
        {
            string output=Path.GetFullPath("../.local/tank-shot-preview");Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.57f,.60f,.65f);
            var light=new GameObject("Key").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.65f;light.transform.rotation=Quaternion.Euler(45,-35,0);
            var camera=new GameObject("Studio camera").AddComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.18f,.20f,.23f);camera.nearClipPlane=.01f;camera.farClipPlane=100;
            var rt=new RenderTexture(1280,720,24){antiAliasing=4};camera.targetTexture=rt;
            void Save(string file){camera.Render();RenderTexture.active=rt;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,file),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=null;}
            var root=new GameObject("Studio");
            var type=typeof(TankMechanismPresentation).Assembly.GetType("Spacewars.Presentation.WebProjectileVisuals",true);
            var visuals=Activator.CreateInstance(type,true);
            object Call(string method,params object[] args)=>type.GetMethod(method).Invoke(visuals,args);
            var shell=(GameObject)Call("Tank",root.transform,true);
            camera.orthographicSize=.36f;camera.transform.position=new Vector3(.2f,.6f,2);camera.transform.LookAt(new Vector3(-.25f,0,0));
            Save("shell-closeup.png");shell.SetActive(false);
            var tank=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("StyleA/tankHybridUpgraded"));tank.transform.rotation=Quaternion.Euler(0,90,0);tank.transform.position=new Vector3(-2.4f,0,0);
            foreach(var r in tank.GetComponentsInChildren<Renderer>())for(int s=0;s<r.sharedMaterials.Length;s++)ActorMaterialProfile.Load().Apply(r,s,new Color(.12f,.30f,.46f));
            var mechanism=tank.AddComponent<TankMechanismPresentation>();mechanism.Initialize();
            var muzzle=tank.GetComponentsInChildren<Transform>().Single(t=>t.name=="muzzle");Vector3 origin=muzzle.position;
            var plate=GameObject.CreatePrimitive(PrimitiveType.Cube);plate.name="Studio target plate";plate.transform.position=new Vector3(3,origin.y,0);plate.transform.localScale=new Vector3(.18f,1.6f,1.6f);
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.27f,.30f,.33f));material.SetFloat("_Metallic",.7f);plate.GetComponent<Renderer>().sharedMaterial=material;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.transform.position=Vector3.down*.035f;floor.GetComponent<Renderer>().sharedMaterial=material;
            var impact=(GameObject)Call("DirectImpact",root.transform,7319,true);impact.transform.position=new Vector3(2.86f,origin.y,0);impact.transform.rotation=Quaternion.LookRotation(Vector3.left);impact.SetActive(false);
            var profile=JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text);
            profile.directImpactSeconds=TankMechanismProfile.Load().impactSeconds;
            float flight=(2.9f-origin.x)/(float)profile.tankProjectileSpeed;
            camera.orthographicSize=2.5f;camera.transform.position=new Vector3(-.4f,3.2f,9);camera.transform.LookAt(new Vector3(-.4f,.55f,0));
            var pose=new PlayableDisplayPose(new NavPoint(0,0),0,0);
            for(int i=0;i<75;i++){
                float time=i/30f;mechanism.Drive(pose,Frame(i),3);float travel=time-.5f;
                shell.SetActive(travel>=0&&travel<flight);shell.transform.position=origin+Vector3.right*travel*(float)profile.tankProjectileSpeed;
                float age=(travel-flight)/(float)profile.directImpactSeconds;impact.SetActive(age>=0&&age<1);
                if(impact.activeSelf)Call("UpdateDirectImpact",impact,age,(float)profile.directImpactRadius,(float)profile.directImpactSparkLength,(float)profile.directImpactSparkWidth,(float)profile.directImpactGlow,(float)profile.directImpactSmokeOpacity);
                Save("shot-"+i.ToString("D3")+".png");
            }
            tank.SetActive(false);shell.SetActive(false);impact.SetActive(true);
            camera.orthographicSize=.72f;camera.transform.position=impact.transform.position+new Vector3(-5,1,1);camera.transform.LookAt(impact.transform.position);
            for(int i=0;i<30;i++){
                Call("UpdateDirectImpact",impact,i/30f,(float)profile.directImpactRadius,(float)profile.directImpactSparkLength,(float)profile.directImpactSparkWidth,(float)profile.directImpactGlow,(float)profile.directImpactSmokeOpacity);
                Save("impact-"+i.ToString("D3")+".png");
            }
            ((IDisposable)visuals).Dispose();camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(material);
            Debug.Log("TANK_SHOT_STUDIO_RENDER_PASS; synthetic presentation only; impact seconds="+profile.directImpactSeconds);
        }
    }
}
