using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class MemoryOwnerColorTests
    {
        static Color Paint(PlayableOwner owner)=>Color.HSVToRGB((int)owner/8f,1,1);
        static PlayableSnapshot View(PlayableProfile p,PlayableVision vision)=>new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot());
        static PlayableVision Remember(PlayableProfile p,KnownBuilding[] buildings)
        {
            var vision=new PlayableVision(0,32,32,4,p.FogEdgeFeather);
            vision.Refresh(new[]{new VisionSource(default,32)},buildings);
            vision.Refresh(Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());return vision;
        }
        [TestCase(PlayableOwner.Player)][TestCase(PlayableOwner.Enemy)][TestCase(PlayableOwner.Third)][TestCase(PlayableOwner.Fourth)]
        [TestCase(PlayableOwner.Fifth)][TestCase(PlayableOwner.Sixth)][TestCase(PlayableOwner.Seventh)][TestCase(PlayableOwner.Eighth)]
        public void MemoryUsesLastKnownOwnerPaletteWithoutDesaturatingTeamSurfaces(PlayableOwner owner)
        {
            var p=PlayableProfile.Default;var host=new GameObject("Memory owner paint");PlayableWorld world=null;
            try
            {
                world=new PlayableWorld(host.transform,p){OwnerPaint=o=>Paint(o.Value)};
                var known=new KnownBuilding(22,(int)owner+1,owner,PlayableBuildingKind.Headquarters,new NavPoint(10,0),0);
                var vision=Remember(p,new[]{known});world.RenderMemories(View(p,vision));Assert.AreEqual(1,world.MemoryCount);Assert.IsEmpty(world.Actors);
                var anchor=host.GetComponentsInChildren<Transform>().Single(t=>t.name=="Remembered building 22");int painted=0;
                foreach(var renderer in anchor.GetComponentsInChildren<Renderer>())for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)
                {
                    string role=renderer.sharedMaterials[slot].name;if(!role.EndsWith("team-primary")&&!role.EndsWith("team-emissive"))continue;
                    var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);var actual=block.GetColor("_BaseColor");var expected=Paint(owner)*(float)p.FogMemoryBrightness;
                    Assert.That(actual.r,Is.EqualTo(expected.r).Within(.00001));Assert.That(actual.g,Is.EqualTo(expected.g).Within(.00001));Assert.That(actual.b,Is.EqualTo(expected.b).Within(.00001));
                    Assert.AreEqual(Color.black,block.GetColor("_EmissionColor"));painted++;
                }
                Assert.Greater(painted,0);Assert.False(vision.IsVisible(known.Position));Assert.AreEqual(owner,vision.Snapshot().KnownBuildings.Single().Owner);
            }
            finally{world?.Dispose();UnityEngine.Object.DestroyImmediate(host);}
        }
        [UnityTest] public IEnumerator CaptureRememberedOwnerColorGallery()
        {
            string output=Environment.GetEnvironmentVariable("FOG_OWNER_COLOR_IMAGE");
            if(string.IsNullOrEmpty(output))output=Path.GetFullPath("../.local/fog-building-owner-colors/default-gallery.png");
            var p=PlayableProfile.Default;var host=new GameObject("Remembered owner color gallery");PlayableWorld world=null;var oldAmbient=RenderSettings.ambientLight;
            var cameraHost=new GameObject("Memory comparison camera");var camera=cameraHost.AddComponent<Camera>();var target=new RenderTexture(1600,650,24);var oldTarget=RenderTexture.active;
            try
            {
                world=new PlayableWorld(host.transform,p){OwnerPaint=o=>Paint(o.Value)};
                var owners=new[]{PlayableOwner.Player,PlayableOwner.Third,PlayableOwner.Fifth,PlayableOwner.Seventh};
                var known=owners.Select((owner,i)=>new KnownBuilding(22+i,(int)owner+1,owner,PlayableBuildingKind.Headquarters,new NavPoint(-18+i*12,0),0)).ToArray();
                world.RenderMemories(View(p,Remember(p,known)));Assert.AreEqual(4,world.MemoryCount);
                // Diagnostic gallery camera/layer only; product presentation settings are unchanged.
                foreach(var t in host.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Remembered building ")))foreach(var child in t.GetComponentsInChildren<Transform>())child.gameObject.layer=31;
                camera.cullingMask=1<<31;camera.orthographic=true;camera.orthographicSize=12;camera.aspect=1600f/650;camera.transform.position=new Vector3(0,28,-36);camera.transform.LookAt(new Vector3(0,2,0));camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.09f,.1f);
                RenderSettings.ambientLight=new Color(.6f,.6f,.6f);var light=new GameObject("Memory comparison light").AddComponent<Light>();light.transform.SetParent(cameraHost.transform);light.type=LightType.Directional;light.intensity=1.7f;light.transform.rotation=Quaternion.Euler(50,-35,0);
                yield return null;target.Create();camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                var image=new Texture2D(1600,650,TextureFormat.RGB24,false);try{image.ReadPixels(new Rect(0,0,1600,650),0,0);image.Apply();Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllBytes(output,image.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(image);}
            }
            finally{RenderTexture.active=oldTarget;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);world?.Dispose();UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(cameraHost);RenderSettings.ambientLight=oldAmbient;}
        }
    }
}
