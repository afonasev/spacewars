#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed class RepairTowerReviewCapture:MonoBehaviour
    {
        PlayableWorld world;RepairTowerReviewDriver driver;GameObject root;Camera cam;RenderTexture target;Texture2D pixels;string output;
        readonly List<string> health=new List<string>{"frame,tick,target,hp"};
        IEnumerator Start()
        {
            output=Environment.GetEnvironmentVariable("SPACEWARS_REPAIR_REVIEW");if(string.IsNullOrEmpty(output)){Application.Quit(1);yield break;}Directory.CreateDirectory(output);
            var run=Run();while(true){bool more;object next;try{more=run.MoveNext();next=more?run.Current:null;}catch(Exception e){File.WriteAllText(Path.Combine(output,"FAIL.txt"),e.ToString());Debug.LogException(e);Application.Quit(1);yield break;}if(!more)break;yield return next;}
            File.WriteAllLines(Path.Combine(output,"health.csv"),health);File.WriteAllText(Path.Combine(output,"PASS.txt"),(Application.isEditor?"Unity Editor PlayMode":"Native Player")+": actual repair authority and threaded worker, scripted scene setup/damage, 24 seconds simulated. Not an ordinary-match or physical/human acceptance claim.\n");Application.Quit(0);
        }
        IEnumerator Run()
        {
            var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text));
            root=new GameObject("Repair tower native review");world=new PlayableWorld(root.transform,p,staged:true);yield return world.Preparation;world.Fog.ShowPublic();driver=new RepairTowerReviewDriver(p);
            var lighting=IndustrialLightingProfile.Load("three-crossings");var sun=new GameObject("Review sun").AddComponent<Light>();sun.transform.SetParent(root.transform);lighting.ApplyScene(sun);
            cam=new GameObject("Review camera").AddComponent<Camera>();cam.transform.SetParent(root.transform);cam.orthographic=true;cam.nearClipPlane=.1f;cam.farClipPlane=500;cam.backgroundColor=new Color(.1f,.12f,.14f);cam.clearFlags=CameraClearFlags.SolidColor;
            cam.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,lighting);cam.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());cam.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());cam.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());
            target=new RenderTexture(1280,800,24){antiAliasing=CameraAntialiasingProfile.Load().Samples};target.Create();cam.targetTexture=target;pixels=new Texture2D(1280,800,TextureFormat.RGB24,false);
            var startup=Time.realtimeSinceStartup;while(driver.View==null){driver.Step();if(Time.realtimeSinceStartup-startup>30)throw new TimeoutException("Review first snapshot");yield return null;}
            var view=driver.View;var tower=view.Buildings.Single(b=>b.Kind==PlayableBuildingKind.RepairTower);var home=view.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Headquarters&&b.Owner==PlayableOwner.Player);
            void CameraAt(NavPoint focus,float size){cam.orthographicSize=size;var point=world.Point(focus);cam.transform.position=point+new Vector3(16,23,-25);cam.transform.LookAt(point+Vector3.up*1.1f);}
            void Render(PlayableSnapshot v)
            {
                foreach(var b in v.Buildings){if(b.Phase==ConstructionPhase.Pending)continue;if(!world.Actors.TryGetValue(b.Id,out var a))a=world.Building(b.Id,b.Kind.ToString(),b.Owner==PlayableOwner.Player,b.RefineryUpgraded);a.Root.position=world.Point(b.Position);a.Root.rotation=Quaternion.Euler(0,-(float)b.Heading*Mathf.Rad2Deg,0);world.FaceBuilding(a,cam);a.Sentry?.Drive(b,v,p);a.Healing?.Drive(b,v,p);a.Selection.SetActive(false);PlayableWorld.UpdateHealth(a,cam,(float)(b.Health/b.MaxHealth));}
                foreach(var u in v.Entities){if(!world.Actors.TryGetValue(u.Id,out var a))a=world.Tank(u.Id,u.Owner==PlayableOwner.Player,u.Kind);a.Root.position=world.Point(u.Position);if(u.Kind==PlayableEntityKind.Hawk)a.Root.position=new Vector3((float)u.Position.X,(float)u.Altitude,(float)u.Position.Z);a.Root.rotation=Quaternion.Euler(0,90-(float)u.HullHeading*Mathf.Rad2Deg,0);a.Selection.SetActive(false);PlayableWorld.UpdateHealth(a,cam,(float)((u.ExactHealth??u.Health)/u.MaxHealth));}
                world.RenderSites(v,cam);
            }
            void Capture(string file){cam.Render();var old=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,800),0,0);pixels.Apply();RenderTexture.active=old;File.WriteAllBytes(Path.Combine(output,file),pixels.EncodeToPNG());}
            CameraAt(home.Position,14);Render(view);yield return null;Capture("01-base-context.png");
            CameraAt(tower.Position,2.6f);Render(view);yield return null;Capture("02-manipulator-close.png");
            var lod=world.Actors[tower.Id].DetailLod;
            for(int level=0;level<3;level++){lod.SetDetail(level);Capture("lod-"+level+".png");}lod.SetDetail(0);
            bool groundSeen=false,airSeen=false;Directory.CreateDirectory(Path.Combine(output,"frames"));var wall=Time.realtimeSinceStartup;
            for(int frame=0;frame<480;frame++)
            {
                if(frame==0||frame==400)CameraAt(home.Position,14);if(frame==60)CameraAt(new NavPoint(tower.Position.X+1,tower.Position.Z),5.8f);
                long next=(long)Math.Ceiling((frame+1)*1.5);while(driver.View.Tick<next){driver.Step();if(Time.realtimeSinceStartup-wall>180)throw new TimeoutException("Repair review progress");yield return null;}
                view=driver.View;Render(view);var b=view.Buildings.Single(x=>x.Id==tower.Id);var unit=view.Entities.FirstOrDefault(x=>x.Id==b.Healing?.TargetId);groundSeen|=unit!=null&&unit.Kind!=PlayableEntityKind.Hawk;airSeen|=unit?.Kind==PlayableEntityKind.Hawk;health.Add(frame+","+view.Tick+","+(unit?.Id??0)+","+(unit?.ExactHealth?.ToString("R",System.Globalization.CultureInfo.InvariantCulture)??""));
                if(frame==80)Capture("03-ground-healing.png");if(frame==320){Capture("04-air-healing.png");CameraAt(tower.Position,2.6f);Capture("05-air-manipulator-close.png");CameraAt(new NavPoint(tower.Position.X+1,tower.Position.Z),5.8f);}Capture("frames/"+frame.ToString("D4")+".png");
            }
            if(!groundSeen||!airSeen)throw new InvalidOperationException("Review must show authoritative ground AND air healing");
        }
        void OnDestroy(){driver?.Dispose();world?.Dispose();if(target){target.Release();Destroy(target);}if(pixels)Destroy(pixels);if(root)Destroy(root);}
    }
}
#endif
