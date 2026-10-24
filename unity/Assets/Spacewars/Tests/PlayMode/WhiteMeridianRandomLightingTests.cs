using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
public sealed class WhiteMeridianRandomLightingTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    [Test] public void FourStableSlotsValidateMetadataAndPreserveUnityRandom(){
        var set=WhiteMeridianLightingVariants.Load();var state=UnityEngine.Random.state;
        var ids=Enumerable.Range(0,4096).Select(s=>set.Select(s).id).Distinct().ToArray();Assert.AreEqual(4,ids.Length);
        foreach(int seed in new[]{int.MinValue,-1,0,7,7108,int.MaxValue})Assert.AreEqual(set.Select(seed).id,WhiteMeridianLightingVariants.Load().Select(seed).id);
        Assert.AreEqual(JsonUtility.ToJson(state),JsonUtility.ToJson(UnityEngine.Random.state));
        Assert.False(WhiteMeridianLightingVariants.Supports(null));Assert.False(WhiteMeridianLightingVariants.Supports("three-crossings-greybox-v1"));
        var paths=Enumerable.Range(0,4).SelectMany(i=>set.Parameters(i)).ToArray();Assert.AreEqual(paths.Length,paths.Select(p=>p.Path).Distinct().Count());
        foreach(var p in paths){float value=p.Read();Assert.IsNotEmpty(p.Description);Assert.Greater(p.Step,0);p.Write(p.Maximum+p.Step);Assert.Throws<ArgumentException>(()=>set.Validate());p.Write(value);}set.Validate();
    }
    [UnityTest] public IEnumerator SavedMatchSeedRestoresTheSameLightingWithoutChangingSaveFormat(){
        var map=new WhiteMeridianMap();var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
        var roster=Enumerable.Range(0,4).Select(i=>new OfflineParticipant("seat-"+i,i+1,i<2?1:2,OfflineControl.Human)).ToArray();
        var config=map.Configuration(profile,7108,roster);
        using(var original=new PlayableRuntime(config,1,startPaused:true)){
            double until=Time.realtimeSinceStartupAsDouble+30;
            while(!original.IsPrepared&&Time.realtimeSinceStartupAsDouble<until)yield return null;
            Assert.True(original.IsPrepared);var capture=original.RequestCaptureBytes();
            while(!capture.IsCompleted&&Time.realtimeSinceStartupAsDouble<until)yield return null;
            Assert.True(capture.IsCompleted);Assert.False(capture.IsFaulted,capture.Exception?.ToString());
            using(var restored=PlayableRuntime.RestoreBytes(capture.Result,config)){
                Assert.AreEqual(config.Seed,restored.MatchSeed);
                Assert.AreEqual(WhiteMeridianLightingVariants.Index(config.Seed),WhiteMeridianLightingVariants.Index(restored.MatchSeed));
            }
        }
    }
    [UnityTest] public IEnumerator OrdinaryLobbyBindsSelectedLightThroughPauseAndGraphicsReset(){
        var host=new GameObject("Meridian random light ordinary lobby");var app=host.AddComponent<PlayableBootstrap>();
        T Get<T>(string n)=>(T)typeof(PlayableBootstrap).GetField(n,Flags).GetValue(app);
        void Call(string n)=>typeof(PlayableBootstrap).GetMethod(n,Flags).Invoke(app,null);
        try{
            double until=Time.realtimeSinceStartupAsDouble+120;
            while(Get<VisualElement>("lobbyScreen")==null&&Time.realtimeSinceStartupAsDouble<until)yield return null;
            Assert.NotNull(Get<VisualElement>("lobbyScreen"));Call("ShowLobby");
            Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice").value=WhiteMeridianMap.DisplayName;
            var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=7108;
            Assert.False(Get<VisualElement>("lobbyScreen").Query<DropdownField>().ToList().Any(e=>e.name.IndexOf("lighting",StringComparison.OrdinalIgnoreCase)>=0));
            Call("LaunchLobbyMatch");until=Time.realtimeSinceStartupAsDouble+120;
            while((Get<bool>("inLobby")||Get<bool>("preparing"))&&Time.realtimeSinceStartupAsDouble<until)yield return null;
            Assert.False(Get<bool>("inLobby"));Assert.False(Get<bool>("preparing"));
            var runtime=Get<PlayableRuntime>("runtime");Assert.AreEqual(7108,runtime.MatchSeed);
            var selected=Get<IndustrialLightingProfile>("selectedMatchLighting");var expected=WhiteMeridianLightingVariants.Load().Select(runtime.MatchSeed).lighting;
            Assert.AreEqual(JsonUtility.ToJson(expected),JsonUtility.ToJson(selected));
            typeof(PlayableBootstrap).GetMethod("Pause",Flags).Invoke(app,new object[]{true});yield return null;
            var camera=Get<Camera>("cameraView");var binding=camera.GetComponent<NativeGraphicsBinding>();
            typeof(NativeGraphicsBinding).GetMethod("Refresh",Flags).Invoke(binding,new object[]{"*"});
            var sun=host.GetComponentsInChildren<Light>().First(l=>l.type==LightType.Directional);
            Assert.AreEqual(expected.sunIntensity,sun.intensity);Assert.AreEqual(Color.HSVToRGB(expected.sunHue,expected.sunSaturation,1),sun.color);
            Assert.AreSame(selected,Get<IndustrialLightingProfile>("selectedMatchLighting"));
            var childHost=new GameObject("Second local light binding");childHost.transform.SetParent(host.transform);
            var child=childHost.AddComponent<PlayableBootstrap>();child.enabled=false;
            typeof(PlayableBootstrap).GetField("localCoordinator",Flags).SetValue(child,app);
            Assert.AreSame(selected,typeof(PlayableBootstrap).GetProperty("ActiveLighting",Flags).GetValue(child));
        }finally{UnityEngine.Object.Destroy(host);}
        yield return null;
    }
}
