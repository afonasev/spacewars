using System;
using System.Collections;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace Spacewars.Presentation
{
    // Explicit standalone visual comparison scene only. Never attached by normal match boot.
    // Buildings and units are staged at production site/slot positions, not a simulated match.
    public sealed class OutpostComparisonCapture:MonoBehaviour
    {
        PlayableWorld world;Camera view;PlayableProfile profile;string output;GameObject compound;
        readonly Color owner=new Color(.19f,.72f,.77f);
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();
            if(!args.Contains("-noaudio")||!args.Contains("-muted"))throw new InvalidOperationException("Muted comparison launch required");
            int index=Array.IndexOf(args,"-outpostEvidence");output=index>=0&&index+1<args.Length?args[index+1]:Path.GetFullPath("../.local/outpost-comparison/captures");Directory.CreateDirectory(output);
            AudioListener.volume=0;Application.runInBackground=true;
            profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
            var sun=new GameObject("Production map sunlight").AddComponent<Light>();sun.transform.SetParent(transform);IndustrialLightingProfile.Load(profile.AuthoredMap.Id).ApplyScene(sun);
            world=new PlayableWorld(transform,profile);world.InspectionReveal();
            view=new GameObject("Comparison camera").AddComponent<Camera>();view.transform.SetParent(transform);view.orthographic=true;view.nearClipPlane=.1f;view.farClipPlane=600;view.clearFlags=CameraClearFlags.SolidColor;view.backgroundColor=new Color(.07f,.09f,.12f);view.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var sites=TerritoryRules.Sites(profile).Where(s=>s.Kind==PlayableBuildingKind.Outpost).Take(2).ToArray();
            foreach(var site in sites)
            {
                compound=new GameObject("Staged outpost "+site.Id);compound.transform.SetParent(transform);
                var target=world.Point(site.Position);int id=site.Id*100;
                string[] kinds={"Factory","Refinery","ScientificCenter"};
                for(int i=0;i<site.Slots.Count;i++)
                {
                    var slot=site.Slots[i];var actor=world.Building(++id,kinds[i%kinds.Length],true);
                    actor.Root.SetParent(compound.transform,true);actor.Root.position=world.Point(slot.Position);world.FaceBuilding(actor);HideHealth(actor);PlayableWorld.PaintOwner(actor,owner);
                }
                for(int i=0;i<4;i++)
                {
                    var unit=world.Tank(++id,true,i==0?PlayableEntityKind.Explorer:i==3?PlayableEntityKind.Shkval:PlayableEntityKind.Tank);
                    unit.Root.SetParent(compound.transform,true);unit.Root.position=world.Point(new NavPoint(site.Position.X-2.4+i*1.6,site.Position.Z-3.4));unit.Hull.localRotation=Quaternion.Euler(0,25+i*12,0);HideHealth(unit);PlayableWorld.PaintOwner(unit,owner);
                }
                foreach(var key in new[]{"outpost","outpost-a","outpost-b"})
                {
                    var prefab=Resources.Load<GameObject>("StyleA/"+key);if(!prefab)throw new Exception("Missing model "+key);
                    var model=Instantiate(prefab,compound.transform);model.transform.position=target+Vector3.up*.06f;
                    model.transform.localScale=Vector3.one*(float)(profile.OutpostModelHeightMeters*profile.OutpostModelScale/4.5094);
                    model.transform.rotation=Quaternion.LookRotation(new Vector3((float)profile.CameraOffsetX,0,(float)profile.CameraOffsetZ));
                    Paint(model,owner);
                    yield return null;yield return null;
                    Shot($"site-{site.Id}-{key}-context",target+new Vector3(8,15,-20),target+Vector3.up*.8f,10.5f);
                    Shot($"site-{site.Id}-{key}-close",target+new Vector3(5,6,-9),target+Vector3.up*1.7f,3.7f);
                    Shot($"site-{site.Id}-{key}-gameplay",target+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ),target,(float)profile.CameraOrthoSize);
                    Destroy(model);yield return null;
                }
                foreach(var actorId in world.Actors.Keys.ToArray())world.Remove(actorId);
                Destroy(compound);yield return null;
            }
            File.WriteAllText(Path.Combine(output,"result.txt"),"PASS native Player staged visual comparison; production map/slots/models/scales/materials. No normal-match, AI, performance or human acceptance.\n"+profile.AuthoredMap.Id+"\n"+Application.unityVersion);
            world.Dispose();world=null;yield return null;
            Debug.Log("OUTPOST_COMPARISON_CAPTURE_PASS");Application.Quit();
        }
        static void HideHealth(PlayableWorld.Actor actor){if(actor.Health)actor.Health.parent.gameObject.SetActive(false);}
        static void Paint(GameObject model,Color color)
        {
            var finish=ActorMaterialProfile.Load();foreach(var r in model.GetComponentsInChildren<Renderer>())for(int i=0;i<r.sharedMaterials.Length;i++){
                finish.Apply(r,i,color);
                if(r.sharedMaterials[i].name=="outpost-comparison-team-emissive")
                {var block=new MaterialPropertyBlock();r.GetPropertyBlock(block,i);block.SetColor("_EmissionColor",color*finish.buildingLampEmission);r.SetPropertyBlock(block,i);}
            }
        }
        void Shot(string name,Vector3 position,Vector3 target,float size)
        {
            view.transform.position=position;view.transform.LookAt(target);view.orthographicSize=size;
            var rt=new RenderTexture(1920,1200,24,RenderTextureFormat.ARGB32);var image=new Texture2D(1920,1200,TextureFormat.RGB24,false);var old=RenderTexture.active;
            try{view.targetTexture=rt;view.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1200),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());}
            finally{view.targetTexture=null;RenderTexture.active=old;rt.Release();Destroy(rt);Destroy(image);}
        }
        void OnDestroy(){world?.Dispose();}
    }
}
