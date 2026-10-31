using System;
using System.Collections;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Spacewars.Presentation
{
    // Staged asset inspection only. No combat, navigation or research simulation.
    public sealed class ExplorerTwinCannonPreview : MonoBehaviour
    {
        static void Hide(PlayableWorld.Actor a)
        {
            if(a.Health)a.Health.parent.gameObject.SetActive(false);
            if(a.Selection)a.Selection.SetActive(false);
        }
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();
            if(!args.Contains("-noaudio")||!args.Contains("-muted"))throw new Exception("Muted launch required");
            int at=Array.IndexOf(args,"-weaponEvidence");if(at<0)throw new Exception("Evidence directory required");
            string output=args[at+1];Directory.CreateDirectory(output);
            AudioListener.volume=0;Application.runInBackground=true;QualitySettings.vSyncCount=0;
            var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
            var sun=new GameObject("Production sunlight").AddComponent<Light>();sun.transform.SetParent(transform);
            IndustrialLightingProfile.Load(profile.AuthoredMap.Id).ApplyScene(sun);
            var world=new PlayableWorld(transform,profile);world.InspectionReveal();
            var site=TerritoryRules.Sites(profile).First(s=>s.Kind==PlayableBuildingKind.Outpost);
            var center=world.Point(site.Position)+new Vector3(0,0,-6);var owner=new Color(.12f,.42f,.72f);
            var outpost=world.Building(501,"Outpost",true);outpost.Root.position=world.Point(site.Position);world.FaceBuilding(outpost);Hide(outpost);PlayableWorld.PaintOwner(outpost,owner);
            var tank=world.Tank(502,true);tank.Root.position=center+new Vector3(4,0,1);Hide(tank);PlayableWorld.PaintOwner(tank,owner);
            var basic=world.Tank(503,true,PlayableEntityKind.Explorer);basic.Root.position=center+Vector3.left*.95f;basic.Hull.rotation=Quaternion.Euler(0,180,0);Hide(basic);PlayableWorld.PaintOwner(basic,owner);
            var candidate=Instantiate(Resources.Load<GameObject>("StyleA/explorer-twin-cannon"),transform);
            candidate.transform.localScale=basic.Hull.localScale;candidate.transform.position=center+Vector3.right*.95f;candidate.transform.rotation=Quaternion.Euler(0,180,0);
            foreach(var r in candidate.GetComponentsInChildren<Renderer>())for(int i=0;i<r.sharedMaterials.Length;i++)ActorMaterialProfile.Load().Apply(r,i,owner);
            var pitch=candidate.GetComponentsInChildren<Transform>().Single(t=>t.name=="weaponPitch");
            var camera=new GameObject("Matched production camera").AddComponent<Camera>();camera.transform.SetParent(transform);
            camera.orthographic=true;camera.aspect=1.6f;camera.nearClipPlane=.05f;camera.farClipPlane=600;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var target=center+Vector3.up*.3f;camera.transform.position=target+new Vector3(8,12,-16);camera.transform.LookAt(target);
            var rt=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32);rt.antiAliasing=4;rt.Create();camera.targetTexture=rt;
            var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            yield return null;yield return new WaitForEndOfFrame();
            foreach(var shot in new[]{("close",1.65f,false),("game-scale",5.5f,false),("vertical",1.65f,true)})
            {
                camera.orthographicSize=shot.Item2;pitch.localRotation=Quaternion.Euler(shot.Item3?-90:0,0,0);
                yield return new WaitForEndOfFrame();camera.Render();RenderTexture.active=rt;
                image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,shot.Item1+".png"),image.EncodeToPNG());
            }
            File.WriteAllText(Path.Combine(output,"result.json"),"{\"result\":\"PASS\",\"scope\":\"Native staged model review, production lighting/materials/scales; not ordinary match or human acceptance\",\"left\":\"base\",\"right\":\"twin cannon candidate\"}\n");
            RenderTexture.active=null;camera.targetTexture=null;rt.Release();Destroy(rt);Destroy(image);Application.Quit();
        }
    }
}
