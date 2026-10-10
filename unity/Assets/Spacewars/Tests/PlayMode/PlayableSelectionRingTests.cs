using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;

public sealed class PlayableSelectionRingTests
{
    [TestCase(PlayableEntityKind.Explorer)]
    [TestCase(PlayableEntityKind.Tank)]
    [TestCase(PlayableEntityKind.Shkval)]
    public void RingFitsModelAndSurvivesOwnerPaintAndSlope(PlayableEntityKind kind)
    {
        var host=new GameObject("Ring test");var profile=PlayableProfile.Default;
        var world=new PlayableWorld(host.transform,profile);
        try {
            var actor=world.Tank(1,true,kind);Assert.IsFalse(actor.Selection.activeSelf);
            Assert.IsNull(actor.Selection.GetComponent<Collider>());
            var renderer=actor.Selection.GetComponent<Renderer>();
            Assert.AreEqual("Spacewars/UnitSelectionRing",renderer.sharedMaterial.shader.name);
            double modelRadius=kind==PlayableEntityKind.Explorer?profile.ExplorerModelRadius*profile.ExplorerModelScale:kind==PlayableEntityKind.Shkval?profile.ShkvalModelRadius*profile.ShkvalModelScale:profile.TankModelRadius*profile.TankModelScale;
            Assert.That(actor.Selection.transform.localScale.x,Is.EqualTo(modelRadius+profile.SelectionRingPadding+profile.SelectionRingWidth).Within(.0001));
            actor.Selection.SetActive(true);actor.Root.rotation=Quaternion.Euler(12,73,8);
            Assert.That(Vector3.Dot(actor.Selection.transform.up,actor.Root.up),Is.GreaterThan(.9999));
            PlayableWorld.PaintOwner(actor,Color.magenta);
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
            Assert.AreEqual(Color.clear,block.GetColor("_BaseColor"));
            Assert.That(block.GetFloat("_RingWidth")*actor.Selection.transform.localScale.x,Is.EqualTo(profile.SelectionRingWidth).Within(.0001));
            actor.Selection.SetActive(false);Assert.IsFalse(renderer.gameObject.activeSelf);
        } finally {world.Dispose();UnityEngine.Object.DestroyImmediate(host);}
    }

    [Test]
    public void RingSettingsValidateAndRemainEditable()
    {
        var data=PlayableProfile.Default.CopyData();
        foreach(var field in PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("presentation.selectionRing."))) {
            Assert.IsNotEmpty(field.Description);Assert.Greater(field.Step,0);Assert.That(field.Read(data),Is.InRange(field.Minimum,field.Maximum));
        }
        data.selectionRingWidth=0;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
    }

    [Test]
    public void RenderSelectedAndDeselectedUnitsAtTwoCameraScales()
    {
        var host=new GameObject("Selection render");var cameraHost=new GameObject("Selection camera");
        var lightHost=new GameObject("Selection light");var light=lightHost.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(50,-30,0);
        var world=new PlayableWorld(host.transform,PlayableProfile.Default);
        var camera=cameraHost.AddComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.15f,.17f);
        var target=new RenderTexture(1400,800,24);target.antiAliasing=4;target.Create();camera.targetTexture=target;
        var output=Environment.GetEnvironmentVariable("SPACEWARS_SELECTION_EVIDENCE");
        try {
            world.Fog.ShowPublic();
            var actors=new[]{world.Tank(1,true,PlayableEntityKind.Explorer),world.Tank(2,true),world.Tank(3,true,PlayableEntityKind.Shkval)};
            for(int i=0;i<actors.Length;i++){actors[i].Root.position=new Vector3((i-1)*3,0,0);actors[i].Hull.localRotation=Quaternion.Euler(0,25+i*45,0);PlayableWorld.PaintOwner(actors[i],i==0?Color.cyan:i==1?Color.blue:Color.red);actors[i].Selection.SetActive(true);actors[i].Health.parent.gameObject.SetActive(false);}
            camera.transform.position=new Vector3(0,10,-9);camera.transform.LookAt(Vector3.zero);
            foreach(float size in new[]{7f,4f}) {
                camera.orthographicSize=size;camera.Render();
                var pixels=Read(target);Assert.Greater(GreenPixels(pixels),100,"Selection circles must be visible in real render");
                if(!string.IsNullOrEmpty(output)){Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,size==7?"selected-normal.png":"selected-close.png"),pixels.EncodeToPNG());}
                UnityEngine.Object.DestroyImmediate(pixels);
            }
            foreach(var actor in actors)actor.Selection.SetActive(false);camera.Render();var deselected=Read(target);
            Assert.Less(GreenPixels(deselected),30,"Deselection removes ground circles");UnityEngine.Object.DestroyImmediate(deselected);
            // A ring alone must have an empty center, rather than the old filled cylinder.
            foreach(var actor in actors)actor.Hull.gameObject.SetActive(false);
            actors[1].Selection.SetActive(true);camera.transform.position=new Vector3(0,10,0);camera.transform.rotation=Quaternion.Euler(90,0,0);camera.orthographicSize=2;camera.Render();var ring=Read(target);
            var center=ring.GetPixel(700,400);Assert.IsFalse(center.g>center.r*1.5f&&center.g>center.b*1.5f);
            Assert.Greater(GreenPixels(ring),100);UnityEngine.Object.DestroyImmediate(ring);
            world.Fog.Reset();camera.Render();var hidden=Read(target);Assert.Less(GreenPixels(hidden),30,"Unseen fog hides the mark");UnityEngine.Object.DestroyImmediate(hidden);
        } finally {camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);world.Dispose();UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(cameraHost);UnityEngine.Object.DestroyImmediate(lightHost);}
    }
    private static int GreenPixels(Texture2D texture)=>texture.GetPixels().Count(c=>c.g>.45f&&c.g>c.r*1.5f&&c.g>c.b*1.5f);
    private static Texture2D Read(RenderTexture target) {
        var previous=RenderTexture.active;RenderTexture.active=target;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();RenderTexture.active=previous;return texture;
    }
}
