using System.Collections;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode {
public class TerrainReliefRenderingTests {
[UnityTest] public IEnumerator BridgeMicroReliefDoesNotProduceDarkSpeckles() {
var map=new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
var root=new GameObject("Relief regression");var world=new PlayableWorld(root.transform,profile);world.InspectionReveal();
var ambient=RenderSettings.ambientLight;var volume=AudioListener.volume;AudioListener.volume=0;
var target=new RenderTexture(1600,1000,24);target.Create();Texture2D pixels=null;
try {
var sun=new GameObject("Relief sun").AddComponent<Light>();sun.transform.SetParent(root.transform);sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(48,-30,0);RenderSettings.ambientLight=new Color(.65f,.65f,.66f);
var camera=new GameObject("Relief camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.fieldOfView=48;camera.nearClipPlane=.1f;camera.farClipPlane=700;camera.clearFlags=CameraClearFlags.SolidColor;camera.targetTexture=target;
const float zoom=6;float height=(float)map.SurfaceHeight(new NavPoint(0,0));camera.transform.position=new Vector3(0,height+zoom*1.35f,-zoom*.72f);camera.transform.LookAt(new Vector3(0,height,0));
for(int i=0;i<5;i++)yield return null;
var previous=RenderTexture.active;RenderTexture.active=target;pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();RenderTexture.active=previous;
// Authored bridge interior, excluding rails/water. Previous derivative spikes
// produced isolated near-black pixels amid grey metal; real panel seams remain brighter.
var colors=pixels.GetPixels32();int dark=0;
for(int y=150;y<800;y++)for(int x=320;x<1280;x++){var c=colors[y*1600+x];if(Mathf.Max(c.r,Mathf.Max(c.g,c.b))<40)dark++;}
Assert.Less(dark,4,"Micro relief introduced dark speckles in exposed bridge metal");
}finally{RenderSettings.ambientLight=ambient;AudioListener.volume=volume;if(pixels)Object.Destroy(pixels);target.Release();Object.Destroy(target);world.Dispose();Object.Destroy(root);}
}
}}
