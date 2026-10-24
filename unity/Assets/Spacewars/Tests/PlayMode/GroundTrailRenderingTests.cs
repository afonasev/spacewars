using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
 public sealed class GroundTrailRenderingTests
 {
  [UnityTest] public IEnumerator MossImpressionsAreActuallyVisibleOnGround()
  {
   var output=Environment.GetEnvironmentVariable("SPACEWARS_TRAIL_RENDER_OUTPUT");if(string.IsNullOrEmpty(output))output=Path.Combine(Application.temporaryCachePath,"ground-trail-rendering");Directory.CreateDirectory(output);
   bool previous=NativeGraphicsSettings.GroundTrailsEnabled;var host=new GameObject("trail material diagnostic");var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new MossHighlandMap());var world=new PlayableWorld(host.transform,p,staged:true);yield return world.Preparation;
   var light=new GameObject("sun").AddComponent<Light>();light.transform.SetParent(host.transform);IndustrialLightingProfile.Load(p.AuthoredMap.Id).ApplyScene(light);
   var cam=new GameObject("camera").AddComponent<Camera>();cam.transform.SetParent(host.transform);cam.orthographic=true;cam.orthographicSize=8;cam.transform.position=new Vector3(7,20,-37);cam.transform.LookAt(new Vector3(4,0,-19));cam.nearClipPlane=.1f;cam.farClipPlane=300;
   var rt=new RenderTexture(960,640,24);rt.Create();cam.targetTexture=rt;var image=new Texture2D(960,640,TextureFormat.RGB24,false);
   var fact=new PlayableGroundTrail(new NavPoint(-3,-19),new NavPoint(11,-19),0,0,3,PlayableEntityKind.Tank);
   var view=new PlayableSnapshot(p.ProfileId,p.Revision,1,7319,1,5,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,groundTrails:new[]{fact});
   try{
    world.Fog.ShowPublic();NativeGraphicsSettings.GroundTrailsEnabled=true;world.RenderGroundTrails(view);Assert.Greater(world.GroundTrailQuadCount,0);
    Color32[] withTrails=null,withoutTrails=null;
    foreach(bool enabled in new[]{true,false}){
     NativeGraphicsSettings.GroundTrailsEnabled=enabled;world.RenderGroundTrails(view);yield return null;cam.Render();var prev=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();RenderTexture.active=prev;
     if(enabled)withTrails=image.GetPixels32();else withoutTrails=image.GetPixels32();
     File.WriteAllBytes(Path.Combine(output,enabled?"enabled.png":"disabled.png"),image.EncodeToPNG());
    }
    int changed=0,darker=0;for(int i=0;i<withTrails.Length;i++){int difference=withoutTrails[i].r+withoutTrails[i].g+withoutTrails[i].b-withTrails[i].r-withTrails[i].g-withTrails[i].b;if(Math.Abs(difference)>15)changed++;if(difference>15)darker++;}
    TestContext.WriteLine("VISIBLE_PIXELS changed="+changed+" darker="+darker);Assert.Greater(changed,1000,"Visible impressions must change actual rendered ground, not only populate geometry");Assert.Greater(darker,changed*.9,"Impressions must retain dark ground lighting");
   }finally{NativeGraphicsSettings.GroundTrailsEnabled=previous;world.Dispose();cam.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(host);}
  }
 }
}
