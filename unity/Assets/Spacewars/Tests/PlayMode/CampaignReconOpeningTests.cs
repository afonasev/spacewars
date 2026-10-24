using System;
using System.Collections.Generic;
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

public sealed class CampaignReconOpeningTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,F).GetValue(target);
    static object AddGamepad(){var input=Type.GetType("UnityEngine.InputSystem.InputSystem, Unity.InputSystem");var pad=Type.GetType("UnityEngine.InputSystem.Gamepad, Unity.InputSystem");var method=input.GetMethods(BindingFlags.Public|BindingFlags.Static).First(m=>m.Name=="AddDevice"&&m.IsGenericMethodDefinition&&m.GetParameters().All(p=>p.IsOptional));return method.MakeGenericMethod(pad).Invoke(null,method.GetParameters().Select(p=>p.DefaultValue).ToArray());}
    static void RemoveInputDevice(object device){if(device==null)return;var input=Type.GetType("UnityEngine.InputSystem.InputSystem, Unity.InputSystem");var inputDevice=Type.GetType("UnityEngine.InputSystem.InputDevice, Unity.InputSystem");input.GetMethod("RemoveDevice",new[]{inputDevice}).Invoke(null,new[]{device});}
    static IEnumerator Until(Func<bool> condition,double seconds,string reason,Func<string> diagnostic=null)
    {double until=Time.realtimeSinceStartupAsDouble+seconds;while(!condition()&&Time.realtimeSinceStartupAsDouble<until)yield return null;Assert.True(condition(),reason+" "+diagnostic?.Invoke());}
    static IEnumerator Capture(PlayableBootstrap b,string name)
    {
        string output=Environment.GetEnvironmentVariable("CAMPAIGN_EVIDENCE");string only=Environment.GetEnvironmentVariable("CAMPAIGN_EVIDENCE_ONLY");if(string.IsNullOrEmpty(output)||!string.IsNullOrEmpty(only)&&only!=name)yield break;
        var panel=b.GetComponent<UIDocument>().panelSettings;var camera=Get<Camera>(b,"cameraView");
        var ui=new RenderTexture(1280,800,24,RenderTextureFormat.ARGB32);var scene=new RenderTexture(1280,800,24,RenderTextureFormat.ARGB32);ui.Create();scene.Create();
        var oldPanel=panel.targetTexture;var oldCamera=camera.targetTexture;panel.targetTexture=ui;camera.targetTexture=scene;
        var pixels=new Texture2D(1280,800,TextureFormat.RGBA32,false);var overlay=new Texture2D(1280,800,TextureFormat.RGBA32,false);var prior=RenderTexture.active;
        try
        {
            var root=Get<VisualElement>(b,"root");foreach(var element in root.Query<VisualElement>().ToList())element.MarkDirtyRepaint();foreach(string id in new[]{"campaign-current-goal","campaign-radio-history"})if(root.Q<Label>(id) is Label label){var text=label.text;label.text="";label.text=text;label.MarkDirtyRepaint();}
            for(int frame=0;frame<5;frame++)yield return null;
            camera.Render();
            RenderTexture.active=scene;pixels.ReadPixels(new Rect(0,0,1280,800),0,0);pixels.Apply();
            RenderTexture.active=ui;overlay.ReadPixels(new Rect(0,0,1280,800),0,0);overlay.Apply();
            var background=pixels.GetPixels32();var foreground=overlay.GetPixels32();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,name+"-camera.png"),pixels.EncodeToPNG());System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,name+"-ui.png"),overlay.EncodeToPNG());
            bool tutorialOverlay=name=="campaign-tutorial-lore-preview";for(int i=0;i<background.Length;i++){if(tutorialOverlay)background[i]=Color.Lerp(background[i],Color.black,.52f);if(foreground[i].r+foreground[i].g+foreground[i].b>2)background[i]=foreground[i];background[i].a=255;}
            pixels.SetPixels32(background);pixels.Apply();System.IO.Directory.CreateDirectory(output);System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,name+".png"),pixels.EncodeToPNG());
        }
        finally{RenderTexture.active=prior;panel.targetTexture=oldPanel;camera.targetTexture=oldCamera;ui.Release();scene.Release();UnityEngine.Object.Destroy(ui);UnityEngine.Object.Destroy(scene);UnityEngine.Object.Destroy(pixels);UnityEngine.Object.Destroy(overlay);}
    }
    [UnityTest] public IEnumerator CampaignRadioClearsMinimapInOrdinaryRenderedEntry()
    {
        var host=new GameObject("campaign radio layout witness");var b=host.AddComponent<PlayableBootstrap>();
        try
        {
            yield return Until(()=>Get<NativeMainMenu>(b,"nativeMainMenu")?.ScreenRoot!=null&&!Get<bool>(b,"initializingMenu")&&Get<GameObject>(b,"startupLoadingHost")==null,120,"human-visible menu ready");
            var menu=Get<NativeMainMenu>(b,"nativeMainMenu");var root=Get<VisualElement>(b,"root");
            menu.Navigation.ActivateElement(root.Q<Button>("main-campaign"));yield return null;menu.Navigation.ActivateElement(root.Q<Button>("campaign-mission-1"));yield return null;menu.Navigation.ActivateElement(root.Q<Button>("campaign-start"));
            yield return Until(()=>Get<PlayableRuntime>(b,"runtime")?.Latest?.Mission!=null&&!Get<bool>(b,"preparing"),120,"normal mission rendered entry");
            var tutorial=root.Q<VisualElement>("campaign-tutorial-layer");var runtime=Get<PlayableRuntime>(b,"runtime");
            yield return Until(()=>runtime.Latest.Tick>0&&tutorial.resolvedStyle.display==DisplayStyle.Flex,10,"first campaign tick and vision are published before the lore card");Assert.AreEqual(DisplayStyle.Flex,tutorial.resolvedStyle.display,"campaign lore should be the first live card");Assert.True(runtime.Latest.Paused,"the lore card pauses the ordinary singleplayer host");
            Assert.Greater(runtime.Latest.Tick,0,"the first world/vision step is published before showing lore");Assert.True(runtime.Latest.Vision.IsVisible(runtime.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Position),"the initial unit view is ready before the lore overlay");
            Assert.That(root.Q<Label>("campaign-tutorial-copy").text,Does.Contain("Исследователь — лёгкая разведывательная боевая машина."));
            yield return Capture(b,"campaign-tutorial-lore-preview");
            typeof(PlayableBootstrap).GetMethod("Pause",F).Invoke(b,new object[]{true});yield return null;yield return null;
            Assert.AreEqual(DisplayStyle.None,tutorial.resolvedStyle.display,"menu pause hides the card without discarding its queued state");Assert.True(runtime.Latest.Paused);
            typeof(PlayableBootstrap).GetMethod("Pause",F).Invoke(b,new object[]{false});yield return null;yield return null;
            Assert.AreEqual(DisplayStyle.Flex,tutorial.resolvedStyle.display,"resuming the menu restores the still queued lore card");Assert.True(runtime.Latest.Paused,"menu resume must preserve the tutorial pause owner");
            var radio=root.Q<ScrollView>("campaign-radio");Assert.LessOrEqual(radio.worldBound.yMax,Get<VisualElement>(b,"compactMap").parent.worldBound.yMin,"radio history must remain above minimap");
            typeof(PlayableBootstrap).GetMethod("DismissCampaignTutorial",F).Invoke(b,null);yield return Until(()=>!runtime.Latest.Paused,10,"dismissing the lore card releases its pause");
            Assert.AreEqual(DisplayStyle.None,tutorial.resolvedStyle.display);Assert.That(root.Q<Label>("campaign-radio-history").text,Does.Contain("Контроль: Вектор-1"),"mission radio remains visible after dismissal");
            yield return Capture(b,"campaign-radio-feed-preview");
        }
        finally{Get<PlayableRuntime>(b,"runtime")?.RequestStop();UnityEngine.Object.DestroyImmediate(host);}
    }
    [UnityTest] public IEnumerator OrdinaryThreadedCampaignOpeningResumesAfterLoreAndReachesFirstReinforcement()
    {
        var host=new GameObject("campaign lore threaded opening witness");var b=host.AddComponent<PlayableBootstrap>();PlayableRuntime runtime=null;object pad=null;var restoreHosts=new List<PlayableRuntime>();
        try
        {
            yield return Until(()=>Get<NativeMainMenu>(b,"nativeMainMenu")?.ScreenRoot!=null&&!Get<bool>(b,"initializingMenu")&&Get<GameObject>(b,"startupLoadingHost")==null,120,"ordinary main menu ready");
            var menu=Get<NativeMainMenu>(b,"nativeMainMenu");var root=Get<VisualElement>(b,"root");
            menu.Navigation.ActivateElement(root.Q<Button>("main-campaign"));yield return null;
            menu.Navigation.ActivateElement(root.Q<Button>("campaign-mission-1"));yield return null;
            menu.Navigation.ActivateElement(root.Q<Button>("campaign-start"));
            yield return Until(()=>Get<PlayableRuntime>(b,"runtime")?.Latest?.Mission!=null&&!Get<bool>(b,"preparing"),120,"ordinary threaded campaign host prepared");
            runtime=Get<PlayableRuntime>(b,"runtime");yield return Until(()=>runtime.Latest.Mission.TutorialQueued==7&&runtime.Latest.Paused,10,"lore is the first tutorial and holds the actual campaign host");var initial=runtime.Latest;Assert.Greater(initial.Tick,0);var unit=initial.Entities.Single(e=>e.Owner==PlayableOwner.Player);Assert.True(initial.Vision.IsVisible(unit.Position),"tutorial opens only after initial campaign vision is published");
            typeof(PlayableBootstrap).GetMethod("DismissCampaignTutorial",F).Invoke(b,null);
            yield return Until(()=>runtime.Latest.Mission.TutorialQueued<0&&!runtime.Latest.Paused,10,"typed lore dismiss releases tutorial pause");
            pad=AddGamepad();typeof(PlayableBootstrap).GetField("battleController",F).SetValue(b,true);
            yield return Until(()=>runtime.Latest.Mission.TutorialQueued==0&&runtime.Latest.Paused,10,"first controller lesson queues for active gamepad");
            Assert.AreEqual(DisplayStyle.Flex,root.Q<VisualElement>("campaign-tutorial-layer").resolvedStyle.display);Assert.AreEqual("ВЫДЕЛЕНИЕ",root.Q<Label>("campaign-tutorial-title").text);Assert.That(root.Q<Label>("campaign-tutorial-copy").text,Does.Contain("выбрать машину"));
            var config=CampaignReconOpening.Configuration(initial.ActiveProfile);
            var activeCapture=runtime.RequestCaptureBytes();yield return Until(()=>activeCapture.IsCompleted,20,"capture active tutorial card");Assert.AreEqual(System.Threading.Tasks.TaskStatus.RanToCompletion,activeCapture.Status,activeCapture.Exception?.ToString());
            var restoredActive=PlayableRuntime.RestoreBytes(activeCapture.Result,config);restoreHosts.Add(restoredActive);Assert.AreEqual(0,restoredActive.Latest.Mission.TutorialQueued);Assert.True(restoredActive.Latest.Mission.TutorialPauseHeld);Assert.True(restoredActive.Latest.Paused);Assert.AreEqual(0,Get<int>(restoredActive,"menuPaused"));restoredActive.RequestStop();yield return Until(()=>restoredActive.IsStopped,10,"active-card restore host stop");restoreHosts.Remove(restoredActive);
            typeof(PlayableBootstrap).GetMethod("Pause",F).Invoke(b,new object[]{true});yield return null;yield return null;
            var menuCardCapture=runtime.RequestCaptureBytes();yield return Until(()=>menuCardCapture.IsCompleted,20,"capture menu plus queued tutorial card");Assert.AreEqual(System.Threading.Tasks.TaskStatus.RanToCompletion,menuCardCapture.Status,menuCardCapture.Exception?.ToString());
            var restoredMenuCard=PlayableRuntime.RestoreBytes(menuCardCapture.Result,config);restoreHosts.Add(restoredMenuCard);Assert.AreEqual(1,Get<int>(restoredMenuCard,"menuPaused"));Assert.True(restoredMenuCard.Latest.Mission.TutorialPauseHeld);Assert.True(restoredMenuCard.Latest.Paused);restoredMenuCard.RequestPause(false);Assert.True(restoredMenuCard.Latest.Paused,"menu resume retains the tutorial pause owner");restoredMenuCard.RequestStop();yield return Until(()=>restoredMenuCard.IsStopped,10,"menu-card restore host stop");restoreHosts.Remove(restoredMenuCard);
            typeof(PlayableBootstrap).GetMethod("Pause",F).Invoke(b,new object[]{false});yield return null;yield return null;
            var gate=(object)Get<object>(runtime,"gate");System.Threading.Tasks.Task<byte[]> blockedCapture=null;
            lock(gate){blockedCapture=runtime.RequestCaptureBytes();Assert.True(runtime.RequestCampaignTutorialControl(runtime.Generation,new CampaignTutorialControl(CampaignTutorialControlKind.ResumeForDevice)));typeof(PlayableRuntime).GetMethod("CompleteCapture",F).Invoke(runtime,null);}
            Assert.AreEqual(System.Threading.Tasks.TaskStatus.Faulted,blockedCapture.Status);StringAssert.Contains("tutorial ingress barrier",blockedCapture.Exception?.GetBaseException().Message);
            typeof(PlayableBootstrap).GetField("battleController",F).SetValue(b,false);Assert.True(runtime.RequestCampaignTutorialControl(runtime.Generation,new CampaignTutorialControl(CampaignTutorialControlKind.SuspendForDevice)));
            yield return Until(()=>runtime.Latest.Mission.TutorialQueued==0&&!runtime.Latest.Mission.TutorialPauseHeld&&!runtime.Latest.Paused,10,"device-suspended queued controller lesson");
            var suspendedCapture=runtime.RequestCaptureBytes();yield return Until(()=>suspendedCapture.IsCompleted,20,"capture suspended controller card");Assert.AreEqual(System.Threading.Tasks.TaskStatus.RanToCompletion,suspendedCapture.Status,suspendedCapture.Exception?.ToString());
            var restoredSuspended=PlayableRuntime.RestoreBytes(suspendedCapture.Result,config);restoreHosts.Add(restoredSuspended);Assert.AreEqual(0,restoredSuspended.Latest.Mission.TutorialQueued);Assert.False(restoredSuspended.Latest.Mission.TutorialPauseHeld);Assert.False(restoredSuspended.Latest.Paused);Assert.AreEqual(0,Get<int>(restoredSuspended,"menuPaused"));restoredSuspended.RequestStop();yield return Until(()=>restoredSuspended.IsStopped,10,"suspended-card restore host stop");restoreHosts.Remove(restoredSuspended);
            typeof(PlayableBootstrap).GetField("battleController",F).SetValue(b,true);Assert.True(runtime.RequestCampaignTutorialControl(runtime.Generation,new CampaignTutorialControl(CampaignTutorialControlKind.ResumeForDevice)));
            yield return Until(()=>runtime.Latest.Mission.TutorialQueued==0&&runtime.Latest.Mission.TutorialPauseHeld&&runtime.Latest.Paused,10,"gamepad resume restores queued card pause");
            Assert.True(runtime.RequestCampaignTutorialControl(runtime.Generation,new CampaignTutorialControl(CampaignTutorialControlKind.Dismiss,0)));Assert.True(runtime.RequestCampaignTutorialControl(runtime.Generation,new CampaignTutorialControl(CampaignTutorialControlKind.Dismiss,7)));Assert.True(runtime.RequestCampaignTutorialControl(runtime.Generation,new CampaignTutorialControl(CampaignTutorialControlKind.Dismiss,0)));
            yield return Until(()=>runtime.Latest.Mission.TutorialQueued<0&&!runtime.Latest.Paused,10,"FIFO duplicate and stale dismiss controls are harmless");
            Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,1,"player-1",PlayableCommandKind.Move,new[]{unit.Id},CampaignReconOpening.Rendezvous)).Accepted,"move command accepted through the ordinary host");
            yield return Until(()=>runtime.Latest.Entities.Any(e=>e.Id==unit.Id&&Math.Abs(e.Position.Z-unit.Position.Z)>.1),30,"command -> threaded host -> first movement",()=>"tick="+runtime.Latest.Tick+" paused="+runtime.Latest.Paused+" status="+runtime.Latest.Status+" failure="+runtime.Latest.Failure+" route="+(runtime.NavigationBinding.RoutePort!=null)+" position="+runtime.Latest.Entities.Single(e=>e.Id==unit.Id).Position+" publication="+Get<UnityHostRouteService>(b,"routeService").CertifiedPublication);
            Assert.IsNotNull(Get<UnityHostRouteService>(b,"routeService").CertifiedPublication,"ordinary campaign route host remains connected");
            yield return Until(()=>((runtime.Latest.Mission.TutorialCompleted&2)!=0),10,"an actually applied early Move completes its topic even before it is offered");
            yield return Until(()=>runtime.Latest.Mission.Reinforced,60,"arrival transfers the three waiting vehicles");
            Assert.AreEqual(4,runtime.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player));Assert.AreEqual(PlayableMatchOutcome.Playing,runtime.Latest.Outcome);
            yield return Until(()=>runtime.CampaignCheckpoint!=null,30,"authentic reinforcement checkpoint");
            var expectedRadio=runtime.Latest.Mission.Radio.ToArray();var restored=PlayableAuthorityTick.RestoreBytes(runtime.CampaignCheckpoint,CampaignReconOpening.Configuration(initial.ActiveProfile));
            try{CollectionAssert.AreEqual(expectedRadio,restored.Latest.Mission.Radio,"CAM3 checkpoint restore keeps the ordered radio history");Assert.AreEqual(1,restored.Latest.Mission.Radio.Count(line=>line=="Контроль: Вектор-1, добро пожаловать во Внешние миры. Ваш отряд ждёт у старого геодезического поста."));Assert.AreEqual(1,restored.Latest.Mission.Radio.Count(line=>line=="Дозор: Командир, три машины исправны. Переходим под ваше управление."));Assert.AreEqual(0x80,restored.Latest.Mission.TutorialCompleted&0x80,"completed lore persists");Assert.AreEqual(-1,restored.Latest.Mission.TutorialQueued);Assert.False(restored.Latest.Mission.TutorialPauseHeld);var domain=typeof(PlayableAuthorityTick).GetField("domain",F).GetValue(restored);var domainType=domain.GetType();domainType.GetField("campaignTutorialQueued",F).SetValue(domain,7);domainType.GetField("campaignTutorialPauseHeld",F).SetValue(domain,true);var validate=domainType.GetMethod("ValidateCampaignState",F);var invalid=Assert.Throws<TargetInvocationException>(()=>validate.Invoke(domain,null));Assert.IsInstanceOf<ArgumentException>(invalid.InnerException,"completed tutorial topics cannot remain queued");}
            finally{restored.Stop();}
        }
        finally{foreach(var restored in restoreHosts)restored.RequestStop();runtime?.RequestStop();RemoveInputDevice(pad);UnityEngine.Object.DestroyImmediate(host);}
    }
    [UnityTest] public IEnumerator FirstCheckpointRetryRetainsOriginalEnvelope()
    {
        var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());var config=CampaignReconOpening.Configuration(profile);
        PlayableRuntime runtime=new PlayableRuntime(config,91);UnityHostRouteService routes=new UnityHostRouteService();
        try
        {
            yield return Until(()=>{routes.Service(runtime,profile.NavigationRequestsPerFrame);return runtime.IsPrepared;},60,"checkpoint fixture preparation");
            int id=runtime.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Id;
            Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,1,"player-1",PlayableCommandKind.Move,new[]{id},CampaignReconOpening.Rendezvous)).Accepted);
            yield return Until(()=>{routes.Service(runtime,profile.NavigationRequestsPerFrame);Assert.False(runtime.IsStopped,runtime.Latest.Failure);return runtime.CampaignCheckpoint!=null;},60,"actual first checkpoint");
            Assert.True(runtime.HasCampaignCheckpoint);var bytes=runtime.CampaignCheckpoint;runtime.RequestStop();yield return Until(()=>runtime.IsStopped,30,"original host shutdown");routes.Dispose();
            runtime=PlayableRuntime.RestoreBytes(bytes,config);runtime.RequestPause(true);routes=new UnityHostRouteService();
            yield return Until(()=>{routes.Service(runtime,profile.NavigationRequestsPerFrame);return runtime.IsPrepared&&runtime.Latest.Paused;},30,"restored paused runtime");
            Assert.True(runtime.Latest.Mission.Reinforced);Assert.False(runtime.Latest.Mission.MineDestroyed);Assert.AreEqual(4,runtime.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player));Assert.AreEqual(1,runtime.HumanSequenceWatermark);
            for(int i=0;i<10;i++){routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
            CollectionAssert.AreEqual(bytes,runtime.CampaignCheckpoint,"retry must retain the original checkpoint, rather than recapture a later state");
        }
        finally{runtime.RequestStop();routes.Dispose();}
    }
    [UnityTest] public IEnumerator OrdinaryMenuThreadedOpeningAndAuthenticCheckpoint()
    {
        var host=new GameObject("campaign ordinary entry witness");var b=host.AddComponent<PlayableBootstrap>();PlayableRuntime runtime=null;
        try
        {
            yield return Until(()=>Get<NativeMainMenu>(b,"nativeMainMenu")?.ScreenRoot!=null&&!Get<bool>(b,"initializingMenu")&&Get<GameObject>(b,"startupLoadingHost")==null,120,"ordinary main menu ready after startup cover is removed");
            var menu=Get<NativeMainMenu>(b,"nativeMainMenu");var root=Get<VisualElement>(b,"root");
            menu.Navigation.ActivateElement(root.Q<Button>("main-campaign"));yield return null;
            yield return Capture(b,"campaign-list");Assert.IsNotNull(root.Q("campaign-menu"));Assert.IsNull(Get<PlayableRuntime>(b,"runtime"),"campaign list does not launch a match");
            menu.Navigation.ActivateElement(root.Q<Button>("campaign-mission-1"));yield return null;
            Assert.AreEqual(CampaignReconOpening.Briefing,root.Q("campaign-briefing").Query<Label>().ToList().Last().text);
            yield return Capture(b,"campaign-briefing");menu.Navigation.ActivateElement(root.Q<Button>("campaign-start"));
            yield return Until(()=>Get<PlayableRuntime>(b,"runtime")?.Latest?.Mission!=null&&!Get<bool>(b,"preparing"),120,"normal campaign preparation");
            runtime=Get<PlayableRuntime>(b,"runtime");var p=runtime.Latest.ActiveProfile;var initial=runtime.Latest;
            Assert.AreEqual(1,initial.Entities.Count(e=>e.Owner==PlayableOwner.Player));Assert.AreEqual(0,initial.Buildings.Count(x=>x.Owner==PlayableOwner.Player));Assert.AreEqual(9,runtime.DiagnosticGlobalBuildings,"full authored mission, no hidden HQ");
            Assert.AreEqual(PlayableMatchOutcome.Playing,initial.Outcome);Assert.False(initial.Mission.MineDiscovered);Assert.AreEqual(1,initial.Mission.Objectives.Count);
            yield return Capture(b,"campaign-first-goal");Assert.LessOrEqual(root.Q<Label>("campaign-radio").worldBound.yMax,Get<VisualElement>(b,"compactMap").parent.worldBound.yMin,"radio must not cover minimap");
            var unit=initial.Entities.Single(e=>e.Owner==PlayableOwner.Player);long sequence=0;
            Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,++sequence,"player-1",PlayableCommandKind.Move,new[]{unit.Id},CampaignReconOpening.Rendezvous)).Accepted);
            yield return Until(()=>runtime.Latest.Entities.Any(e=>e.Id==unit.Id&&Math.Abs(e.Position.Z-unit.Position.Z)>.1),30,"command -> real threaded host -> first motion",()=>"enabled="+b.enabled+" active="+b.gameObject.activeInHierarchy+" init="+Get<bool>(b,"initializingMenu")+" port="+(runtime.NavigationBinding.RoutePort!=null)+" tick="+runtime.Latest.Tick+" paused="+runtime.Latest.Paused+" status="+runtime.Latest.Status+" failure="+runtime.Latest.Failure+" bootstrapPaused="+Get<bool>(b,"paused")+" inLobby="+Get<bool>(b,"inLobby")+" entities="+string.Join(";",runtime.Latest.Entities.Select(e=>e.Id+":"+e.Position.X+","+e.Position.Z+":"+e.NavigationOutcome+":"+e.Moving))+" receipts="+string.Join(";",runtime.ReceiptsAfter(0).Select(r=>r.Status+":"+r.Message))+" hostEpoch="+Get<UnityHostRouteService>(b,"routeService").SolverEpoch+" projection="+Get<UnityHostRouteService>(b,"routeService").LastProjectionFailure);
            Assert.IsNotNull(Get<UnityHostRouteService>(b,"routeService").CertifiedPublication,"ordinary background host must be attached");Debug.Log("campaign witness: threaded first motion PASS");
            yield return Until(()=>runtime.Latest.Mission.Reinforced,60,"arrival transfers waiting squad");
            Assert.AreEqual(4,runtime.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player));Assert.AreEqual(PlayableMatchOutcome.Playing,runtime.Latest.Outcome);
            yield return Until(()=>runtime.CampaignCheckpoint!=null,30,"authentic runtime checkpoint");
            typeof(PlayableBootstrap).GetMethod("ToggleMap",F).Invoke(b,null);yield return null;yield return null;
            var history=root.Q<Label>("campaign-revealed-goals");var map=root.Q("tactical-map-background");
            Assert.LessOrEqual(history.worldBound.xMax,map.worldBound.xMin,"objectives left of square TAB map");Assert.AreEqual(map.worldBound.width,map.worldBound.height,1);
            Assert.That(history.text,Does.Contain("Добраться до сборного пункта"));Assert.That(history.text,Does.Not.Contain("шахту"));
            yield return Capture(b,"campaign-tab-after-reinforcement");typeof(PlayableBootstrap).GetMethod("ToggleMap",F).Invoke(b,null);
            Debug.Log("campaign witness: 1+3 and checkpoint PASS");
            var checkpoint=runtime.CampaignCheckpoint;Assert.Greater(checkpoint.Length,0);
            var config=CampaignReconOpening.Configuration(p);
            // Two independent restores of the actual runtime envelope retain the exact authority bytes and continuation.
            var a=PlayableAuthorityTick.RestoreBytes(checkpoint,config);var c=PlayableAuthorityTick.RestoreBytes(checkpoint,config);
            using(var routesA=new UnityHostRouteService())using(var routesC=new UnityHostRouteService())
            try
            {
                CollectionAssert.AreEqual(a.CaptureBytes(),c.CaptureBytes(),"exact initial mission/world/navigation continuation");
                long target=a.Tick+30;
                yield return Until(()=>{routesA.Service(a,p.NavigationRequestsPerFrame);routesC.Service(c,p.NavigationRequestsPerFrame);if(a.Tick<target)a.TryAdvance();if(c.Tick<target)c.TryAdvance();return a.Tick==target&&c.Tick==target;},30,"restored continuation");
                CollectionAssert.AreEqual(a.CaptureBytes(),c.CaptureBytes(),"exact replay including mission and full navigation");
            }
            finally{a.Stop();c.Stop();}
            var ids=runtime.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>e.Id).ToArray();
            Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,++sequence,"player-1",PlayableCommandKind.Move,ids,CampaignReconOpening.Explore)).Accepted);
            yield return Until(()=>runtime.Latest.Mission.MineDiscovered,90,"fog-qualified discovery");
            Assert.AreEqual(4,runtime.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player),"no repeated +3");
            var mine=runtime.Latest.Buildings.Single(x=>x.Kind==PlayableBuildingKind.Mine&&x.SiteId==1);
            Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,++sequence,"player-1",PlayableCommandKind.Attack,ids,mine.Position,targetId:mine.Id)).Accepted);
            yield return Until(()=>runtime.Latest.Mission.MineDestroyed,120,"authentic mine combat destruction");
            Assert.AreEqual(PlayableMatchOutcome.Playing,runtime.Latest.Outcome,"opening does not declare mission victory");Assert.IsNull(runtime.Result);
            Assert.True(runtime.Latest.Mission.Objectives.Single(g=>g.Id=="first-mine").Complete);
            // Retry through the actual pause action; no synthetic starting world.
            typeof(PlayableBootstrap).GetMethod("RetryCampaign",F).Invoke(b,null);
            yield return Until(()=>Get<PlayableRuntime>(b,"runtime")!=runtime&&!Get<bool>(b,"restarting"),60,"checkpoint retry rebinds real host");
            runtime=Get<PlayableRuntime>(b,"runtime");Assert.True(runtime.Latest.Mission.Reinforced);Assert.False(runtime.Latest.Mission.MineDestroyed);Assert.AreEqual(4,runtime.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player));
            Assert.AreEqual(PlayableMatchOutcome.Playing,runtime.Latest.Outcome);
        }
        finally{runtime?.RequestStop();UnityEngine.Object.DestroyImmediate(host);}
    }
}
