using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Spacewars.Tests.PlayMode
{
    public sealed class FoundryReadabilityRenderingTests
    {
        [UnityTest] public IEnumerator CaptureRealSitesRoutesAndConcealedLava()
        {
            var output=Environment.GetEnvironmentVariable("SPACEWARS_FOUNDRY_ART_EVIDENCE")??Path.GetFullPath("../.local/foundry-readability/captures");
            Directory.CreateDirectory(output);
            var profile=FoundryGreybox.LoadProfile();var map=(FoundryMap)profile.AuthoredMap;
            var root=new GameObject("Foundry readability inspection");
            var previousAmbient=RenderSettings.ambientLight;float previousTime=Time.timeScale;Time.timeScale=0;
            var world=new PlayableWorld(root.transform,profile);world.InspectionReveal();world.MaterialFinish=ActorMaterialProfile.Load();
            var camera=new GameObject("Readability camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);
            camera.orthographic=true;camera.fieldOfView=48;camera.nearClipPlane=.1f;camera.farClipPlane=800;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.10f,.13f);
            var target=new RenderTexture(1600,1000,24){antiAliasing=4};target.Create();camera.targetTexture=target;
            camera.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());
            camera.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());
            camera.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());
            var lighting=IndustrialLightingProfile.Load(map.Id);
            var sun=new GameObject("Accepted light").AddComponent<Light>();sun.transform.SetParent(root.transform);lighting.ApplyScene(sun);sun.shadows=LightShadows.Soft;
            camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,lighting);
            var sites=map.Sites(profile);var home=sites[0];
            var buildings=sites.Select(s=>new PlayableBuildingSnapshot(s.Id,PlayableOwner.Player,s.Kind,s.Position,250,1,0,0,default,s.Id,0,phase:ConstructionPhase.Ready)).ToList();
            for(int i=0;i<3;i++)buildings.Add(new PlayableBuildingSnapshot(100+i,PlayableOwner.Player,new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter}[i],home.Slots[i].Position,250,1,0,0,default,home.Id,home.Slots[i].Id,phase:ConstructionPhase.Ready));
            var snapshot=new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,Array.Empty<PlayableEntitySnapshot>(),buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites.Select(s=>new TerritorySiteSnapshot(s,PlayableOwner.Player,PlayableOwner.Player,1,false,s.Id,true)).ToArray());
            foreach(var b in buildings){var actor=world.Building(b.Id,b.Kind.ToString(),true);actor.Root.position=world.Point(b.Position);actor.Health.parent.gameObject.SetActive(false);}
            for(int i=0;i<3;i++){var actor=world.Tank(900+i,true);actor.Root.position=world.Point(new NavPoint(home.Position.X-3+i*3,home.Position.Z-9));actor.Health.parent.gameObject.SetActive(false);}
            var material=root.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Lava river / fissure").sharedMaterial;
            var lava=material.GetVector("_FoundryLavaFinish");lava.w=0;material.SetVector("_FoundryLavaFinish",lava); // Identical frozen visual time, not game tuning.
            try
            {
                Assert.IsEmpty(root.GetComponentsInChildren<Collider>(),"Presentation must not add gameplay blockers");
                var poses=new[]{
                    ("overview",new Vector3(0,3,0),new Vector3(100,250,-180),150f),
                    ("home",world.Point(home.Position),new Vector3(12,24,-24),18f),
                    ("home-close",world.Point(home.Position),new Vector3(12,24,-24),10f),
                    ("home-reverse",world.Point(home.Position),new Vector3(-12,24,24),18f),
                    ("outpost",world.Point(new NavPoint(-38,70)),new Vector3(18,28,-28),23f),
                    ("ramp",world.Point(new NavPoint(-43,35)),new Vector3(32,45,-35),30f),
                    ("flank",world.Point(new NavPoint(77,0)),new Vector3(24,34,-30),25f),
                    ("east-connection",world.Point(new NavPoint(43,54)),new Vector3(32,45,-35),32f),
                    ("south-connections",world.Point(new NavPoint(0,-50)),new Vector3(-45,70,55),64f),
                    ("rear-flank",world.Point(new NavPoint(-65,80)),new Vector3(25,32,-28),25f),
                    ("ramp-low",world.Point(new NavPoint(-44,35)),new Vector3(30,14,-25),24f),
                    ("lava",new Vector3(44,13,0),new Vector3(15,12,-19),23f)};
                foreach(var pose in poses)
                {
                    camera.orthographic=pose.Item1!="lava";camera.orthographicSize=pose.Item4;
                    camera.transform.position=pose.Item2+pose.Item3;camera.transform.LookAt(pose.Item2);
                    world.RenderSites(snapshot,camera);
                    foreach(var b in buildings)world.FaceBuilding(world.Actors[b.Id],camera);
                    for(int frame=0;frame<8;frame++)yield return null;
                    Save(target,Path.Combine(output,pose.Item1+".png"));
                }
                material.SetTexture("_FogMask",Texture2D.whiteTexture);material.SetColor("_FogColor",Color.black);
                foreach(var actor in world.Actors.Values)actor.Root.gameObject.SetActive(false);
                for(int frame=0;frame<8;frame++)yield return null;
                var concealed=Read(target);
                try{var c=concealed.GetPixel(800,500);Assert.Less(Mathf.Max(c.r,Mathf.Max(c.g,c.b)),.03f,"Fog must conceal molten emission");File.WriteAllBytes(Path.Combine(output,"concealed.png"),concealed.EncodeToPNG());}
                finally{UnityEngine.Object.Destroy(concealed);}
#if UNITY_EDITOR
                foreach(var message in ShaderUtil.GetShaderMessages(Resources.Load<Shader>("TerritoryFog")))Assert.AreNotEqual("Error",message.severity.ToString(),message.message);
#endif
                File.WriteAllText(Path.Combine(output,"capture.json"),"{\"scene\":\"real PlayableWorld and Foundry sites; staged ready buildings\",\"seed\":7,\"profile\":"+JsonUtility.ToJson(FoundrySurfaceProfile.Load())+",\"unity\":\""+Application.unityVersion+"\",\"gpu\":\""+SystemInfo.graphicsDeviceName+"\"}");
            }
            finally
            {
                camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);world.Dispose();UnityEngine.Object.Destroy(root);Time.timeScale=previousTime;RenderSettings.ambientLight=previousAmbient;
            }
            yield return null;
        }
        private static Texture2D Read(RenderTexture target)
        {
            var old=RenderTexture.active;
            try{RenderTexture.active=target;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();return pixels;}
            finally{RenderTexture.active=old;}
        }
        private static void Save(RenderTexture target,string path)
        {
            var pixels=Read(target);
            try
            {
                var colors=pixels.GetPixels32();Assert.Greater(colors.Where((_,i)=>i%173==0).Select(c=>((int)c.r<<16)|((int)c.g<<8)|c.b).Distinct().Count(),16,"Scene must render");
                Assert.Less(colors.Count(c=>c.r>200&&c.b>200&&c.g<40),16,"No error pink");File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally{UnityEngine.Object.Destroy(pixels);}
        }
    }
}
