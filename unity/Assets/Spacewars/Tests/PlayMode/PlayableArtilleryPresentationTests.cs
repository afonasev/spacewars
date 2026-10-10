using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
public sealed class PlayableArtilleryPresentationTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private GameObject host;private PlayableBootstrap hud;private PlayableWorld world;
    private void Set(string key,object value)=>typeof(PlayableBootstrap).GetField(key,Flags).SetValue(hud,value);
    private IDictionary Get(string key)=>(IDictionary)typeof(PlayableBootstrap).GetField(key,Flags).GetValue(hud);
    private void Render(PlayableProjectileSnapshot[] shots,PlayableImpactSnapshot[] impacts=null,bool paused=false,long tick=30)
    {
        Set("view",new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,tick,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),shots,new PlayableRuntimeMetrics(0,0,0,0,0),null,impacts:impacts));
        typeof(PlayableBootstrap).GetMethod("RenderProjectiles",Flags).Invoke(hud,null);
    }
    [SetUp]public void Setup(){host=new GameObject("artillery projection test");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;Set("profile",PlayableProfile.Default);world=new PlayableWorld(host.transform,PlayableProfile.Default);Set("world",world);}
    [TearDown]public void Cleanup(){UnityEngine.Object.DestroyImmediate(host);}
    [Test]public void MarkerOnlyNeverMaterializesHiddenMissileAndPausedTransformsStayFixed()
    {
        var marker=new PlayableImpactMarker(new NavPoint(3,4),1.6,.22);var shot=new PlayableProjectileSnapshot(7,0,0,default,PlayableEntityKind.Shkval,visible:false,marker:marker);
        Render(new[]{shot});Assert.Zero(Get("shells").Count);Assert.AreEqual(1,Get("impactMarkers").Count);
        var g=(GameObject)Get("impactMarkers")[7];var before=g.transform.position;Render(new[]{shot},paused:true);Assert.AreEqual(before,g.transform.position);
        Render(Array.Empty<PlayableProjectileSnapshot>());Assert.Zero(Get("impactMarkers").Count);
    }
    [Test]public void RocketFollowsTangentWithRearExhaustAndPersistentImpact()
    {
        var p=PlayableProfile.Default;var shot=new PlayableProjectileSnapshot(9,1,2,new NavPoint(2,3),PlayableEntityKind.Shkval,.5,4,.3,age:1);
        Render(new[]{shot});Assert.Zero(Get("impactMarkers").Count);var rocket=(GameObject)Get("shells")[9];Assert.AreEqual(4,rocket.transform.position.y);var exhaust=rocket.transform.Find("rocket-exhaust");Assert.That(exhaust.localPosition.x,Is.EqualTo(-p.ShkvalProjectileLength*.72/2).Within(.00001));Assert.Less(rocket.transform.InverseTransformPoint(exhaust.GetChild(0).position).x,-p.ShkvalProjectileLength/2);
        Vector3 direction=new Vector3(Mathf.Cos(.5f)*Mathf.Cos(.3f),Mathf.Sin(.3f),Mathf.Sin(.5f)*Mathf.Cos(.3f));Assert.Less(Vector3.Distance(direction,rocket.transform.right),.00001);
        var impact=new PlayableImpactSnapshot(9,PlayableOwner.Player,new BallisticPoint(8,0,3),1.4,30,1);Render(Array.Empty<PlayableProjectileSnapshot>(),new[]{impact});Assert.Zero(Get("shells").Count);Assert.AreEqual(1,Get("impactEffects").Count);
        Render(Array.Empty<PlayableProjectileSnapshot>());Assert.Zero(Get("impactEffects").Count);
    }
    [Test]public void WebShellShapesAndImpactExpansionUseAuthoritativeTicks()
    {
        var shots=new[]{new PlayableProjectileSnapshot(1,1,2,new NavPoint(0,0),PlayableEntityKind.Tank,.4,1,.2),new PlayableProjectileSnapshot(2,1,2,new NavPoint(1,0),PlayableEntityKind.Explorer)};
        var hit=new PlayableImpactSnapshot(3,PlayableOwner.Player,new BallisticPoint(3,2,4),1.4,30,1);
        Render(shots,new[]{hit},tick:31);
        var tank=(GameObject)Get("shells")[1];Assert.NotNull(tank.transform.Find("kinetic-shell-nose"));Assert.NotNull(tank.transform.Find("kinetic-shell-tracer"));
        Assert.That(tank.transform.GetChild(0).GetComponent<MeshFilter>().sharedMesh.bounds.size.x,Is.EqualTo(.28f).Within(.00001));
        var circle=(GameObject)Get("impactEffects")[3];Assert.That(circle.transform.position.y,Is.EqualTo(2f).Within(.00001));
        Assert.Greater(circle.GetComponent<MeshFilter>().sharedMesh.bounds.size.y,0);
        Assert.That(circle.transform.localScale.x,Is.EqualTo(PlayableProfile.Default.ImpactEffectRadius*2*(1d/30)/PlayableProfile.Default.ImpactEffectSec).Within(.00001));Assert.IsNull(circle.GetComponent<Collider>());
        float early=circle.transform.localScale.x;var b=new MaterialPropertyBlock();circle.GetComponent<Renderer>().GetPropertyBlock(b);float earlyAlpha=b.GetColor("_BaseColor").a;
        Render(shots,new[]{hit},tick:33);Assert.Greater(circle.transform.localScale.x,early);circle.GetComponent<Renderer>().GetPropertyBlock(b);Assert.Less(b.GetColor("_BaseColor").a,earlyAlpha);
        var scale=circle.transform.localScale;Render(shots,new[]{hit},paused:true,tick:33);Assert.AreEqual(scale,circle.transform.localScale);
        Render(Array.Empty<PlayableProjectileSnapshot>());Assert.Zero(Get("impactEffects").Count);Assert.Zero(Get("shells").Count);
    }
    [Test]public void FilledPredictionKeepsWebOpacityAndRadiusUntilImpact()
    {
        var profile=PlayableProfile.Default;
        PlayableProjectileSnapshot Shot(double radius)=>new PlayableProjectileSnapshot(8,1,2,new NavPoint(2,3),PlayableEntityKind.Shkval,marker:new PlayableImpactMarker(new NavPoint(3,4),radius,profile.ShkvalMarkerOpacity));
        Render(new[]{Shot(profile.ShkvalMarkerStartRadius)});var marker=(GameObject)Get("impactMarkers")[8];
        var mesh=marker.GetComponent<MeshFilter>().sharedMesh;Assert.AreEqual(Vector3.zero,mesh.vertices[0]);Assert.AreEqual(64*3,mesh.triangles.Length);Assert.Zero(mesh.bounds.size.y);Assert.IsNull(marker.GetComponent<Collider>());
        Assert.That(marker.transform.localScale.x,Is.EqualTo(profile.ShkvalMarkerStartRadius).Within(.00001));
        var b=new MaterialPropertyBlock();marker.GetComponent<Renderer>().GetPropertyBlock(b);Assert.That(b.GetColor("_BaseColor").a,Is.EqualTo(profile.ShkvalMarkerOpacity).Within(.00001));
        Render(new[]{Shot(profile.ShkvalBlastRadius)});Assert.That(marker.transform.localScale.x,Is.EqualTo(profile.ShkvalBlastRadius).Within(.00001));marker.GetComponent<Renderer>().GetPropertyBlock(b);Assert.That(b.GetColor("_BaseColor").a,Is.EqualTo(profile.ShkvalMarkerOpacity).Within(.00001));
        var scale=marker.transform.localScale;Render(new[]{Shot(profile.ShkvalBlastRadius)},paused:true);Assert.AreEqual(scale,marker.transform.localScale);
        Render(Array.Empty<PlayableProjectileSnapshot>(),new[]{new PlayableImpactSnapshot(8,PlayableOwner.Player,new BallisticPoint(3,0,4),profile.ShkvalBlastRadius,30,1)},tick:31);Assert.Zero(Get("impactMarkers").Count);Assert.AreEqual(1,Get("impactEffects").Count);
    }
    [UnityEngine.TestTools.UnityTest]public IEnumerator RenderWebProjectileEvidence()
    {
        var vision=new PlayableVision(0,PlayableProfile.Default.ArenaHalfExtent,PlayableProfile.Default.ArenaHalfExtent,4,PlayableProfile.Default.FogEdgeFeather);vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),80)},Array.Empty<KnownBuilding>());world.Fog.Update(vision.Snapshot(),1);
        world.Part("Evidence ground",host.transform,new Vector3(0,-.05f,0),new Vector3(30,.1f,30),new Color(.19f,.21f,.23f),PrimitiveType.Cube);
        foreach(var item in new[]{(1,PlayableEntityKind.Tank,-3f),(2,PlayableEntityKind.Explorer,0f),(3,PlayableEntityKind.Shkval,3f)}){
            var actor=world.Tank(item.Item1,true,item.Item2);actor.Root.position=new Vector3(item.Item3,0,1);}
        var cameraHost=new GameObject("Projectile evidence camera");cameraHost.transform.SetParent(host.transform,false);var camera=cameraHost.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=4;camera.transform.position=new Vector3(7,9,-11);camera.transform.LookAt(new Vector3(0,.5f,0));camera.backgroundColor=new Color(.06f,.08f,.1f);camera.clearFlags=CameraClearFlags.SolidColor;
        var lightHost=new GameObject("Evidence light");lightHost.transform.SetParent(host.transform,false);var light=lightHost.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-30,0);
        var texture=new RenderTexture(1400,900,24);texture.Create();camera.targetTexture=texture;
        var folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/qa/shkval-web-rules/frames"));System.IO.Directory.CreateDirectory(folder);
        try{
            var shots=new[]{new PlayableProjectileSnapshot(21,1,10,new NavPoint(-3,-1),PlayableEntityKind.Tank,-Math.PI/2,.5),new PlayableProjectileSnapshot(22,2,10,new NavPoint(0,-1),PlayableEntityKind.Explorer,-Math.PI/2,.5),new PlayableProjectileSnapshot(23,3,10,new NavPoint(3,-1),PlayableEntityKind.Shkval,-Math.PI/2,2,.25,age:.2)};
            var hit=new PlayableImpactSnapshot(24,PlayableOwner.Player,new BallisticPoint(0,0,-3),1.4,30,1);
            for(int tick=0;tick<=35;tick++){
                if(tick<30){float progress=tick/30f;var radius=PlayableProfile.Default.ShkvalMarkerStartRadius+(PlayableProfile.Default.ShkvalBlastRadius-PlayableProfile.Default.ShkvalMarkerStartRadius)*progress;
                    var markerShot=new PlayableProjectileSnapshot(23,3,10,new NavPoint(3,-1),PlayableEntityKind.Shkval,-Math.PI/2,2,.25,age:progress,marker:new PlayableImpactMarker(new NavPoint(0,-3),radius,PlayableProfile.Default.ShkvalMarkerOpacity));Render(new[]{markerShot},tick:tick);}
                else Render(Array.Empty<PlayableProjectileSnapshot>(),new[]{hit},tick:tick);
                yield return null;camera.Render();Save(texture,System.IO.Path.Combine(folder,"web-tick-"+tick.ToString("D2")+".png"));
            }
            camera.orthographicSize=(float)PlayableProfile.Default.CameraOrthoSize;yield return null;camera.Render();Save(texture,System.IO.Path.Combine(folder,"game-scale.png"));
        }finally{camera.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
    }
    private static void Save(RenderTexture texture,string path){var previous=RenderTexture.active;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();System.IO.File.WriteAllBytes(path,pixels.EncodeToPNG());}finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}}
    [Test]public void OriginalLauncherHasDistinctPitchPivot()
    {
        var a=world.Tank(1,true,PlayableEntityKind.Shkval);Assert.NotNull(a.Launcher);Assert.True(a.Launcher.IsChildOf(a.Turret));Assert.AreEqual(PlayableProfile.Default.ShkvalModelRadius*PlayableProfile.Default.ShkvalModelScale/2.05,a.Hull.localScale.x,.00001);
    }
}
