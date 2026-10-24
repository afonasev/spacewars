using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class DirectProjectilePresentationTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    GameObject host;PlayableBootstrap hud;PlayableWorld world;PlayableProfile profile;
    void Set(string key,object value)=>typeof(PlayableBootstrap).GetField(key,Flags).SetValue(hud,value);
    IDictionary Hits=>(IDictionary)typeof(PlayableBootstrap).GetField("directContacts",Flags).GetValue(hud);
    object Field(object value,string name)=>value.GetType().GetField(name,Flags).GetValue(value);
    void Render(long tick,PlayableSoundEvent[] sounds=null,PlayableProjectileSnapshot[] shots=null,bool paused=false,long generation=1)
    {
        Set("view",new PlayableSnapshot(profile.ProfileId,profile.Revision,generation,7,tick,tick,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,0,null,
            Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),shots??Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sounds:sounds));
        typeof(PlayableBootstrap).GetMethod("RenderDirectContacts",Flags).Invoke(hud,null);
        typeof(PlayableBootstrap).GetMethod("RenderProjectiles",Flags).Invoke(hud,null);
    }
    PlayableSoundEvent Hit(long id,PlayableEntityKind weapon,int target,float x,long tick=30)
        =>new PlayableSoundEvent(id,tick,PlayableSoundKind.Impact,new NavPoint(x,0),weapon,contact:new PlayableDirectContact((int)id,target,new BallisticPoint(x,1,-1.8)));
    [SetUp] public void Setup()
    {
        profile=PlayableProfile.Default;host=new GameObject("Direct projectile fixture");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        Set("profile",profile);world=new PlayableWorld(host.transform,profile);Set("world",world);world.SetPresentationLayer(8);
        foreach(var item in new[]{(1,-1.4f),(2,1.4f)}){var actor=world.Tank(item.Item1,true,PlayableEntityKind.Tank);actor.Root.position=new Vector3(item.Item2,0,0);}
    }
    [TearDown] public void Cleanup(){UnityEngine.Object.DestroyImmediate(host);}
    [Test] public void EachBulletHasItsOwnWeakerEffectAndTerminalSegmentIncludingLateFrame()
    {
        var events=new[]{Hit(100,PlayableEntityKind.Tank,1,-1.4f),Hit(101,PlayableEntityKind.Explorer,2,1.4f),Hit(102,PlayableEntityKind.Explorer,2,1.4f)};
        Render(31,events);Assert.AreEqual(3,Hits.Count);
        var tank=(GameObject)Field(Hits[100L],"Root");var explorer=(GameObject)Field(Hits[101L],"Root");
        Assert.Less(explorer.transform.GetChild(0).localScale.x,tank.transform.GetChild(0).localScale.x);
        foreach(var hit in Hits.Values)
        {
            var root=(GameObject)Field(hit,"Root");var trail=(GameObject)Field(hit,"Trail");Assert.NotNull(trail,"First rendered contact must bridge the gap even when its simulation tick was skipped.");
            Vector3 endpoint=trail.transform.position+trail.transform.right*trail.transform.localScale.x/2;
            Assert.Less(Vector3.Distance(endpoint,root.transform.position),.0001);
            // F1 replaces six primitive draws with exactly two shared particle batches.
            Assert.AreEqual(2,root.transform.childCount);Assert.AreEqual(2,root.GetComponentsInChildren<Renderer>().Length);
            Assert.IsEmpty(root.GetComponentsInChildren<Collider>());
            foreach(var renderer in root.GetComponentsInChildren<Renderer>())
            {Assert.AreEqual("Spacewars/DirectImpact",renderer.sharedMaterial.shader.name);Assert.IsTrue(renderer.sharedMaterial.shader.isSupported);}
            foreach(var part in root.GetComponentsInChildren<Transform>())Assert.AreEqual(8,part.gameObject.layer);
        }
        var point=tank.transform.position;
        var properties=new MaterialPropertyBlock();var hot=tank.transform.GetChild(0).GetComponent<Renderer>();hot.GetPropertyBlock(properties);float phase=properties.GetFloat("_Phase");
        Render(31,events,paused:true);Assert.AreEqual(point,tank.transform.position);hot.GetPropertyBlock(properties);Assert.AreEqual(phase,properties.GetFloat("_Phase"));
        var otherHot=explorer.transform.GetChild(0).GetComponent<Renderer>();Assert.AreSame(hot.sharedMaterial,otherHot.sharedMaterial,"Per-bullet particles share their material/mesh resources.");
        world.Actors[1].Root.position+=Vector3.right;Render(32,events);Assert.AreEqual(point+Vector3.right,tank.transform.position);
        Assert.IsFalse((GameObject)Field(Hits[100L],"Trail"));
        Render(40,events);Assert.Zero(Hits.Count);
    }
    [Test] public void SurfaceIsOnReadableModelTrianglesAndHiddenFactsRestartAndMissClearEffects()
    {
        var actor=world.Actors[1];Assert.IsTrue(actor.Hull.GetComponentsInChildren<MeshFilter>().All(m=>m.sharedMesh.isReadable),"Approved glTF model meshes must support presentation surface intersection.");
        Render(30,new[]{Hit(100,PlayableEntityKind.Tank,1,-1.4f)});var root=(GameObject)Field(Hits[100L],"Root");
        Assert.Less(root.transform.position.z,0);Assert.Greater(root.transform.position.z,-1.2f);
        Assert.Greater(root.transform.position.y,0);Assert.Less(root.transform.position.y,1);
        Render(31);Assert.Zero(Hits.Count,"Filtered/hidden contact must disappear on the same projection.");
        Render(30,new[]{Hit(100,PlayableEntityKind.Tank,1,-1.4f)});Assert.AreEqual(1,Hits.Count);
        Render(0,generation:2);Assert.Zero(Hits.Count);
    }
    [UnityTest] public IEnumerator NativeContactFrames()
    {
        var vision=new PlayableVision(0,profile.ArenaHalfExtent,profile.ArenaHalfExtent,4,profile.FogEdgeFeather);
        vision.Refresh(new[]{new VisionSource(default,80)},Array.Empty<KnownBuilding>());world.Fog.Update(vision.Snapshot(),1);
        world.Part("Contact evidence ground",host.transform,new Vector3(0,-.06f,0),new Vector3(14,.1f,14),new Color(.17f,.20f,.22f));
        var lightHost=new GameObject("Contact light");lightHost.transform.SetParent(host.transform,false);var light=lightHost.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-30,0);
        var cameraHost=new GameObject("Contact camera");cameraHost.transform.SetParent(host.transform,false);var camera=cameraHost.AddComponent<Camera>();
        camera.orthographic=true;camera.orthographicSize=2.3f;camera.transform.position=new Vector3(4,6,-8);camera.transform.LookAt(new Vector3(0,.2f,0));camera.cullingMask=1<<8;camera.backgroundColor=new Color(.06f,.08f,.1f);camera.clearFlags=CameraClearFlags.SolidColor;
        var texture=new RenderTexture(1400,800,24);texture.Create();camera.targetTexture=texture;
        var folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/qa/direct-contact/frames"));System.IO.Directory.CreateDirectory(folder);
        var events=new[]{Hit(100,PlayableEntityKind.Tank,1,-1.4f),Hit(101,PlayableEntityKind.Explorer,2,1.4f)};
        try
        {
            // Reconstruct the previous renderer path at the identical camera: shell gone,
            // damage/impact sound published but no direct visual contact fact consumed.
            Render(30);yield return null;Capture("legacy-removal-reconstruction");
            Render(30,events);yield return null;Capture("terminal-contact");
            Render(31,events);yield return null;Capture("early-sparks");
            Render(32,events);yield return null;Capture("warm-sparks");
            Render(34,events);yield return null;Capture("cooling-embers");
            Render(32,events);
            camera.orthographicSize=(float)profile.CameraOrthoSize;yield return null;Capture("ordinary-camera-scale");
        }
        finally{camera.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
        void Capture(string name)
        {
            camera.Render();var previous=RenderTexture.active;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,name+".png"),pixels.EncodeToPNG());}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
        }
    }
}
