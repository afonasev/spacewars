using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
public sealed class CampaignHorizontalVisualTests
{
    [UnityTest] public IEnumerator ApprovedMapRender()
    {
        var output=Environment.GetEnvironmentVariable("CAMPAIGN_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
        var host=new GameObject("B geometry render fixture");var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());
        var world=new PlayableWorld(host.transform,p);var cameraHost=new GameObject("B render camera");var camera=cameraHost.AddComponent<Camera>();
        var sunHost=new GameObject("B render sun");var sun=sunHost.AddComponent<Light>();IndustrialLightingProfile.Load(p.AuthoredMap.Id).ApplyScene(sun);
        var target=new RenderTexture(1600,1400,24);var pixels=new Texture2D(1600,1400,TextureFormat.RGB24,false);var prior=RenderTexture.active;
        try
        {
            // QA public geometry overview only: does not modify actual mission fog.
            world.Fog.ShowPublic();var config=CampaignReconOpening.Configuration(p);int id=1;
            foreach(var unit in config.ScenarioUnits){var actor=world.Tank(id++,unit.LogicalPlayer!=2,unit.Kind);actor.Root.position=new Vector3((float)unit.Position.X,0,(float)unit.Position.Z);}
            foreach(var building in config.ScenarioBuildings){var actor=world.Building(id++,building.Kind.ToString(),false);actor.Root.position=new Vector3((float)building.Position.X,0,(float)building.Position.Z);}
            target.Create();camera.targetTexture=target;camera.orthographic=true;camera.orthographicSize=250;camera.nearClipPlane=.1f;camera.farClipPlane=1200;camera.backgroundColor=Color.black;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.transform.position=new Vector3(0,600,0);camera.transform.rotation=Quaternion.Euler(90,0,0);
            for(int i=0;i<3;i++)yield return null;
            void Save(string name){camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1400),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());}
            Save("b-unity-overview");
            camera.orthographicSize=90;camera.transform.position=new Vector3(-154,170,4);camera.transform.LookAt(new Vector3(-154,0,107));Save("b-unity-opening");
            Assert.Greater(new FileInfo(Path.Combine(output,"b-unity-overview.png")).Length,10000);
        }
        finally{RenderTexture.active=prior;camera.targetTexture=null;world.Dispose();target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(pixels);UnityEngine.Object.Destroy(cameraHost);UnityEngine.Object.Destroy(sunHost);UnityEngine.Object.Destroy(host);}
    }
}
