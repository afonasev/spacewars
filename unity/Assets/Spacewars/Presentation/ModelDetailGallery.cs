using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Spacewars.Presentation
{
    // Bounded visual QA scene. No match, authority simulation or performance claim.
    public sealed class ModelDetailGallery : MonoBehaviour
    {
        public string[] keys;public GameObject[] originals,candidates;
        Camera view;RenderTexture target;string output;
        byte[] Capture(string name)
        {
            view.Render();var previous=RenderTexture.active;RenderTexture.active=target;
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try{pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());return pixels.GetRawTextureData().ToArray();}
            finally{RenderTexture.active=previous;Object.DestroyImmediate(pixels);}
        }
        static PlayableSnapshot Frame(long tick)=>new PlayableSnapshot("lod-gallery",1,1,7,tick+1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,new NavGeometry(20,Array.Empty<NavObstacle>(),1),Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
        static void Pose(GameObject model,bool upgrade,bool articulated)
        {
            if(articulated&&model.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="trackFrontGuide")){
                var mechanism=model.AddComponent<TankMechanismPresentation>();mechanism.Initialize();
                for(int tick=0;tick<4;tick++)mechanism.Drive(new PlayableDisplayPose(new NavPoint(tick*.13,tick*.2),tick*.18,tick*.12),Frame(tick),3);
            }
            foreach(var t in model.GetComponentsInChildren<Transform>(true)){
                if(t.name=="baseOnly")t.gameObject.SetActive(!upgrade);if(t.name=="upgradedOnly")t.gameObject.SetActive(upgrade);
                if(articulated){if(t.name=="turretYaw")t.localRotation*=Quaternion.Euler(0,47,0);if(t.name=="weaponPitch"||t.name=="launcherPitch")t.localRotation*=Quaternion.Euler(-22,0,0);if(t.name=="mainRotor"||t.name=="tailRotor")t.localRotation*=Quaternion.Euler(0,33,0);if(t.name=="turbineYaw")t.localRotation*=Quaternion.Euler(0,0,41);}
            }
        }
        void ScienceReplacement()
        {
            var before=Object.Instantiate(Resources.Load<GameObject>("StyleA/scientificCenter"));
            var after=Object.Instantiate(Resources.Load<GameObject>("StyleA/Lod/research-scanner"));
            try{
                float scale=(float)BuildingCombatGeometry.ModelScale(PlayableProfile.Default,PlayableBuildingKind.ScientificCenter);
                foreach(var model in new[]{before,after}){model.transform.localScale=Vector3.one*scale;model.transform.rotation=Quaternion.LookRotation(Vector3.back,Vector3.up);}
                var rows=after.GetComponentsInChildren<Renderer>(true);var bounds=rows[0].bounds;foreach(var r in rows.Skip(1))bounds.Encapsulate(r.bounds);
                var center=bounds.center;float size=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
                view.transform.position=center+new Vector3(1.2f,1.25f,-1.65f)*size;view.transform.LookAt(center);view.orthographicSize=size*.72f;
                after.SetActive(false);Capture("science-game-before");before.SetActive(false);after.SetActive(true);
                var lod=after.GetComponent<ModelDetailLod>();for(int level=0;level<3;level++){lod.SetDetail(level);Capture("science-game-after-lod"+level);}
            }finally{Object.DestroyImmediate(before);Object.DestroyImmediate(after);}
        }
        void Start()
        {
            try{
                if(!NativeSilentStartup.IsSilent)throw new InvalidOperationException("Gallery requires startup mute flags");
                var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-modelDetailOutput");if(index<0||index+1>=args.Length)throw new InvalidOperationException("Missing owned gallery output");output=args[index+1];Directory.CreateDirectory(output);
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.54f,.59f);
                foreach(var row in new[]{(new Vector3(50,-35,0),1.6f),(new Vector3(30,140,0),.4f)}){var light=new GameObject("Gallery light").AddComponent<Light>();light.transform.SetParent(transform);light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(row.Item1);light.intensity=row.Item2;}
                view=new GameObject("Gallery camera").AddComponent<Camera>();view.transform.SetParent(transform);view.gameObject.AddComponent<UniversalAdditionalCameraData>();view.orthographic=true;view.nearClipPlane=.01f;view.farClipPlane=200;view.clearFlags=CameraClearFlags.SolidColor;view.backgroundColor=new Color(.15f,.19f,.22f);
                target=new RenderTexture(480,400,24);target.Create();view.targetTexture=target;
                int comparisons=0;
                for(int k=0;k<keys.Length;k++)foreach(bool upgrade in keys[k]=="hawk"?new[]{false,true}:new[]{false})foreach(bool articulated in new[]{false,true}){
                    var original=Object.Instantiate(originals[k]);var candidate=Object.Instantiate(candidates[k]);candidate.SetActive(false);
                    try{
                        Pose(original,upgrade,articulated);Pose(candidate,upgrade,articulated);
                        var rows=original.GetComponentsInChildren<Renderer>(true).Where(r=>r.gameObject.activeInHierarchy).ToArray();var bounds=rows[0].bounds;foreach(var r in rows.Skip(1))bounds.Encapsulate(r.bounds);
                        var center=bounds.center;float size=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);view.transform.position=center+new Vector3(1.2f,1.25f,-1.65f)*size;view.transform.LookAt(center);view.orthographicSize=size*.72f;
                        string prefix=keys[k]+(upgrade?"-upgraded":"")+(articulated?"-pose":"-rest");var baseline=Capture(prefix+"-source");original.SetActive(false);candidate.SetActive(true);var lod=candidate.GetComponent<ModelDetailLod>();
                        for(int level=0;level<3;level++){lod.SetDetail(level);var pixels=Capture(prefix+"-lod"+level);if(level==0&&!baseline.SequenceEqual(pixels))throw new InvalidOperationException("Original near pixels changed: "+prefix);}
                        comparisons++;
                    }finally{Object.DestroyImmediate(original);Object.DestroyImmediate(candidate);}
                }
                if(keys.Contains("research-scanner"))ScienceReplacement();
                File.WriteAllText(Path.Combine(output,"result.json"),"{\"result\":\"PASS\",\"matched_near_comparisons\":"+comparisons+",\"mute\":true,\"classification\":\"PLAYER_ASSET_GALLERY_NOT_MATCH_OR_PERFORMANCE\"}\n");
                Debug.Log("MODEL_DETAIL_GALLERY_PASS comparisons="+comparisons);view.targetTexture=null;target.Release();Object.DestroyImmediate(target);Application.Quit(0);
            }catch(Exception e){Debug.LogException(e);Application.Quit(1);}
        }
    }
}
