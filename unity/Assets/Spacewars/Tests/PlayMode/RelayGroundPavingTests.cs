using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class RelayGroundPavingTests
{
    static readonly string[] Maps = {"MossHighland","WhiteMeridian","Foundry","AntennaCity","CrimsonGrove","AiTest","ThreeCrossings","NullDock","BasaltCrown"};

    [UnityTest]
    public IEnumerator InspectRelaySurface([ValueSource(nameof(Maps))] string mapKey)
    {
        var author = AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Spacewars.Editor.PreparedMapsAuthoring")).First(t=>t!=null);
        var p = (PlayableProfile)author.GetMethod("Profile").Invoke(null,new object[]{mapKey});
        var root = new GameObject("Relay finish inspection");
        PlayableWorld world = null;
        try
        {
            world = new PlayableWorld(root.transform,p,staged:true);
            yield return world.Preparation;
            var sites = p.AuthoredMap.Sites(p).Where(s=>s.Kind==PlayableBuildingKind.RelayTower).ToArray();
            Assert.IsNotEmpty(sites);
            bool overlay = mapKey!="NullDock" && mapKey!="BasaltCrown";
            foreach(var site in sites)
            {
                Assert.AreEqual(overlay,world.GraphicsRoot.Find("Relay ground "+site.Id)!=null);
                Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(site.Position));
                if(mapKey=="ThreeCrossings")
                    Assert.AreEqual(GroundTrailSurfaceKind.Soil,world.TrailSurface(new NavPoint(site.Position.X,site.Position.Z+p.RelayCaptureRadius*.7)));
            }
            if(mapKey=="AiTest")
            {
                var at=sites[0].Position;
                Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(new NavPoint(at.X+p.RelayCaptureRadius*.95,at.Z)));
                Assert.AreEqual(GroundTrailSurfaceKind.Sand,world.TrailSurface(new NavPoint(at.X+p.RelayCaptureRadius*1.05,at.Z)));
                var data=p.CopyData();data.relayCaptureRadius=p.RelayCaptureRadius*.5;
                world.Rebind(PlayableProfile.Create(data,p.AuthoredMap));
                Assert.AreEqual(GroundTrailSurfaceKind.Sand,world.TrailSurface(new NavPoint(at.X+p.RelayCaptureRadius*.75,at.Z)));
                world.Rebind(p);
            }
            string output=Environment.GetEnvironmentVariable("RELAY_PAVING_EVIDENCE");
            if(string.IsNullOrEmpty(output))yield break;
            Directory.CreateDirectory(output);
            var frame=new PlayableSnapshot(p.ProfileId,p.Revision,1,3112026,1,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,
                Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),default,null,activeProfile:p,publicSites:sites);
            world.RenderSites(frame);
            world.Fog.ShowPublic();
            var light=new GameObject("Relay sun");light.transform.SetParent(root.transform);
            IndustrialLightingProfile.Load(p.AuthoredMap.Id).ApplyScene(light.AddComponent<Light>());
            var camera=new GameObject("Relay camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);camera.orthographic=true;camera.farClipPlane=900;
            NullDockArtTests.ConfigureInspectionCamera(camera,root.transform,p.AuthoredMap.Id);
            yield return null;
            var target=world.Point(sites[0].Position);
            NullDockArtTests.SaveFrame(camera,output,mapKey,target,(float)p.RelayCaptureRadius*2,new Vector3(0,90,-45));
            NullDockArtTests.SaveFrame(camera,output,mapKey+"-normal",target,(float)p.CameraOrthoSize,
                new Vector3((float)p.CameraOffsetX,(float)p.CameraHeight,(float)p.CameraOffsetZ));
            if(mapKey=="ThreeCrossings")
                NullDockArtTests.SaveFrame(camera,output,"ThreeCrossings-roads",Vector3.zero,(float)p.ArenaHalfExtent,new Vector3(0,145,-1));
            File.WriteAllText(Path.Combine(output,mapKey+".txt"),p.ProfileId+" @"+p.Revision+" radius="+p.RelayCaptureRadius+" prepared="+world.PreparationCacheHit);
        }
        finally {world?.Dispose();UnityEngine.Object.Destroy(root);}
    }
}
