using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
public sealed class CampaignPresentationStateTests
{
    [UnityTest] public IEnumerator CampaignExitRestoresCreditsAndKeepsSpectatorAndDefeatLabelsCorrect()
    {
        var host=new GameObject("campaign presentation transition");var b=host.AddComponent<PlayableBootstrap>();b.enabled=false;
        const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,f).SetValue(b,value);
        T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,f).GetValue(b);
        void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,f).Invoke(b,null);
        var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());var config=CampaignReconOpening.Configuration(profile);var runtime=new PlayableRuntime(config,94,startPaused:true);
        PlayableSnapshot View(bool mission,PlayableMatchOutcome outcome=PlayableMatchOutcome.Playing)=>new PlayableSnapshot(profile.ProfileId,profile.Revision,94,config.Seed,1,1,RuntimeStatus.Running,false,outcome,900,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,activeProfile:profile,mission:mission?new CampaignMissionView(false,false,false):null);
        try
        {
            Set("profile",profile);Call("CreateHud");Set("runtime",runtime);Set("campaignConfiguration",config);Set("view",View(true));Call("UpdateCampaignHud");Assert.AreEqual(DisplayStyle.None,Get<Label>("creditsLabel").style.display.value);
            // The main/lobby transition clears the scenario; a subsequent ordinary perspective restores its header.
            Set("inLobby",true);Set("campaignConfiguration",null);Set("view",View(false));Call("UpdateCampaignHud");Set("inLobby",false);Call("UpdateCampaignHud");Assert.AreEqual(DisplayStyle.Flex,Get<Label>("creditsLabel").style.display.value);Assert.AreEqual(DisplayStyle.None,Get<Label>("campaignGoal").style.display.value);
            Set("matchSetup",new NativeLobbyConfiguration{Participants=new List<NativeLobbyParticipant>{new NativeLobbyParticipant{Human=false,Team=1}}});Call("ResetSpectatorPresentation");Get<Label>("creditsLabel").style.display=DisplayStyle.None;Call("UpdateCampaignHud");Assert.AreEqual(DisplayStyle.None,Get<Label>("creditsLabel").style.display.value,"spectator overview must not expose credits");
            Set("matchSetup",null);Call("ResetSpectatorPresentation");Set("campaignConfiguration",config);Set("view",View(true,PlayableMatchOutcome.PlayerLost));Call("UpdateHud");Assert.AreEqual("АРМИЯ ПОТЕРЯНА",Get<Label>("modalTitle").text);
            Set("campaignConfiguration",null);Set("view",View(false,PlayableMatchOutcome.PlayerLost));Call("UpdateHud");Assert.AreEqual("ЦЕНТРЫ ПОТЕРЯНЫ",Get<Label>("modalTitle").text);
            yield return null;
        }
        finally{runtime.RequestStop();UnityEngine.Object.DestroyImmediate(host);}
    }
    [UnityTest] public IEnumerator CompletionPersistsOnlyForWonMissionAndMenuAllowsReplay()
    {
        const string key="campaign.disputed-territory.completed";bool had=PlayerPrefs.HasKey(key);int previous=PlayerPrefs.GetInt(key,0);PlayerPrefs.DeleteKey(key);
        var host=new GameObject("campaign progress presentation");var b=host.AddComponent<PlayableBootstrap>();b.enabled=false;var menuHost=new GameObject("campaign progress menu");NativeMainMenu menu=null;
        const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,f).SetValue(b,value);
        void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,f).Invoke(b,null);
        var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());var c=CampaignReconOpening.Configuration(p);var r=new PlayableRuntime(c,109,startPaused:true);
        PlayableSnapshot View(bool victory,PlayableMatchOutcome outcome)=>new PlayableSnapshot(p.ProfileId,p.Revision,109,c.Seed,1,1,RuntimeStatus.Running,false,outcome,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,activeProfile:p,mission:new CampaignMissionView(true,true,true,true,true,true,true,true,victory));
        try
        {
            Set("profile",p);Call("CreateHud");Set("runtime",r);Set("campaignConfiguration",c);
            Set("view",View(false,PlayableMatchOutcome.PlayerLost));Call("UpdateCampaignHud");Assert.AreEqual(0,PlayerPrefs.GetInt(key,0));
            Set("view",View(true,PlayableMatchOutcome.Playing));Call("UpdateCampaignHud");Assert.AreEqual(0,PlayerPrefs.GetInt(key,0),"projection alone is not authoritative victory");
            Set("view",View(true,PlayableMatchOutcome.PlayerWon));Call("UpdateCampaignHud");Assert.AreEqual(1,PlayerPrefs.GetInt(key,0));
            Set("view",View(false,PlayableMatchOutcome.PlayerLost));Call("UpdateCampaignHud");Assert.AreEqual(1,PlayerPrefs.GetInt(key,0),"replay/defeat preserve completion");
            menu=menuHost.AddComponent<NativeMainMenu>();double end=Time.realtimeSinceStartupAsDouble+30;while(menu.ScreenRoot==null&&Time.realtimeSinceStartupAsDouble<end)yield return null;Assert.IsNotNull(menu.ScreenRoot);
            menu.Navigation.ActivateElement(menu.ScreenRoot.Q<Button>("main-campaign"));yield return null;var root=menuHost.GetComponent<UIDocument>().rootVisualElement;
            var mission=root.Q<Button>("campaign-mission-1");Assert.That(mission.text,Does.Contain("пройдена"));Assert.AreEqual(1,root.Query<Button>().ToList().Count(x=>x.name=="campaign-mission-1"));menu.Navigation.ActivateElement(mission);yield return null;Assert.True(root.Q<Button>("campaign-start").enabledSelf,"completed mission remains replayable");
        }
        finally{r.RequestStop();UnityEngine.Object.DestroyImmediate(menuHost);UnityEngine.Object.DestroyImmediate(host);if(had)PlayerPrefs.SetInt(key,previous);else PlayerPrefs.DeleteKey(key);PlayerPrefs.Save();}
    }

}
