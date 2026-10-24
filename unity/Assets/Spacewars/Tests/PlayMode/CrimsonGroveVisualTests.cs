using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
public sealed class CrimsonGroveVisualTests
{
    [UnityTest] public IEnumerator AuthoredTerrainOverview(){
        Assert.Contains("-noaudio",Environment.GetCommandLineArgs());Assert.Contains("-muted",Environment.GetCommandLineArgs());
        var output=Environment.GetEnvironmentVariable("CRIMSON_GROVE_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
        var host=new GameObject("Crimson Grove static render inspection");PlayableWorld world=null;
        try{
            var map=new CrimsonGroveMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var sun=new GameObject("Sun").AddComponent<Light>();sun.transform.SetParent(host.transform);IndustrialLightingProfile.Load(map.Id).ApplyScene(sun);
            world=new PlayableWorld(host.transform,p);world.InspectionSites();world.InspectionReveal();
            var camera=new GameObject("Map overview camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=800;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.06f,.07f);
            yield return null;
            Shot(camera,output,"map-overview",new Vector3(0,270,-200),Vector3.zero,178);
            Shot(camera,output,"map-topology",new Vector3(0,320,0),Vector3.zero,165);
            Shot(camera,output,"map-pocket",new Vector3(94,80,20),new Vector3(94,3,80),37);
            Shot(camera,output,"fort-close",new Vector3(0,40,24),new Vector3(0,0,54),20);
            Shot(camera,output,"ground-close",new Vector3(25,26,28),new Vector3(25,0,54),12);
            Shot(camera,output,"crystal-close",new Vector3(104,19,78),new Vector3(104,8,87),7);
            Shot(camera,output,"terrace-transition",new Vector3(64,80,10),new Vector3(64,0,75),40);
            Shot(camera,output,"ramp-close",new Vector3(74,24,35),new Vector3(74,1,54),12);
            Shot(camera,output,"base-close",new Vector3(49,42,88),new Vector3(49,3,118),22);
            Assert.True(Resources.Load<Shader>("CrimsonGroveSurface").isSupported,"map shader supported by rendering device");
            Assert.IsEmpty(host.GetComponentsInChildren<Collider>(),"terrain dressing has no colliders");
            world.InspectionHide();Shot(camera,output,"fog-hidden",new Vector3(94,80,20),new Vector3(94,3,80),37);
            File.WriteAllText(Path.Combine(output,"render.txt"),"Static Unity production terrain render with diagnostic site/slot markers. No runtime, AI, gameplay or human acceptance is inferred.\n");
        }finally{world?.Dispose();UnityEngine.Object.Destroy(host);}
    }
    private static void Shot(Camera camera,string output,string name,Vector3 position,Vector3 target,float size){
        camera.transform.position=position;camera.transform.LookAt(target);camera.orthographicSize=size;
        var rt=new RenderTexture(1600,1200,24);var old=RenderTexture.active;rt.Create();camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
        var png=new Texture2D(1600,1200,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,1600,1200),0,0);png.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),png.EncodeToPNG());
        camera.targetTexture=null;RenderTexture.active=old;UnityEngine.Object.Destroy(png);UnityEngine.Object.Destroy(rt);
    }
}
