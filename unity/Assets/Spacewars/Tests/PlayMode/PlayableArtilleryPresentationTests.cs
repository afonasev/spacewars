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
    private void Render(PlayableProjectileSnapshot[] shots,PlayableImpactSnapshot[] impacts=null,bool paused=false)
    {
        Set("view",new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,30,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),shots,new PlayableRuntimeMetrics(0,0,0,0,0),null,impacts:impacts));
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
        Render(new[]{shot});Assert.Zero(Get("impactMarkers").Count);var rocket=(GameObject)Get("shells")[9];Assert.AreEqual(4,rocket.transform.position.y);Assert.Less(rocket.transform.GetChild(1).localPosition.x,-p.ShkvalProjectileLength/2);
        Vector3 direction=new Vector3(Mathf.Cos(.5f)*Mathf.Cos(.3f),Mathf.Sin(.3f),Mathf.Sin(.5f)*Mathf.Cos(.3f));Assert.Less(Vector3.Distance(direction,rocket.transform.right),.00001);
        var impact=new PlayableImpactSnapshot(9,PlayableOwner.Player,new BallisticPoint(8,0,3),1.4,30,1);Render(Array.Empty<PlayableProjectileSnapshot>(),new[]{impact});Assert.Zero(Get("shells").Count);Assert.AreEqual(1,Get("impactEffects").Count);
        Render(Array.Empty<PlayableProjectileSnapshot>());Assert.Zero(Get("impactEffects").Count);
    }
    [Test]public void OriginalLauncherHasDistinctPitchPivot()
    {
        var a=world.Tank(1,true,PlayableEntityKind.Shkval);Assert.NotNull(a.Launcher);Assert.True(a.Launcher.IsChildOf(a.Turret));Assert.AreEqual(PlayableProfile.Default.ShkvalModelRadius*PlayableProfile.Default.ShkvalModelScale/2.05,a.Hull.localScale.x,.00001);
    }
}
