using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        bool campaignPreferenceCaptured,campaignPreferenceHad;int campaignPreferencePrior;
        [TearDown] public void RestoreCampaignPreference()
        {
            if(!campaignPreferenceCaptured)return;
            const string key="campaign.disputed-territory.completed";
            if(campaignPreferenceHad)PlayerPrefs.SetInt(key,campaignPreferencePrior);else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();campaignPreferenceCaptured=false;
        }
        void CampaignFrame()
        {
            if(app.enabled)return;
            if(Runtime!=null&&!Get<bool>(app,"preparing"))Call(app,"OnApplicationFocus",true);
            Call(app,"UpdateFrame");
        }
        IEnumerator CampaignWait(Func<bool> ready,string phase,double seconds=60,bool allowStopped=false)
        {
            double end=Time.realtimeSinceStartupAsDouble+seconds;
            while(!ready()&&Time.realtimeSinceStartupAsDouble<end)
            {
                CampaignFrame();Assert.False(!allowStopped&&Runtime?.IsStopped==true,"campaign host failed: "+Runtime?.Latest?.Failure);yield return null;
            }
            CampaignFrame();yield return null;CampaignFrame();
            Assert.True(ready(),phase+" tick="+Runtime?.Latest?.Tick+" queued="+Runtime?.Latest?.Mission?.TutorialQueued+" paused="+Runtime?.Latest?.Paused+" failure="+Runtime?.Latest?.Failure);
        }
        IEnumerator StartCampaignSnapshot(string variable)
        {
            InputSystem.AddDevice<Keyboard>();InputSystem.AddDevice<Mouse>();pads=new[]{InputSystem.AddDevice<Gamepad>()};
            host=new GameObject("authentic campaign UI and gamepad witness");app=host.AddComponent<PlayableBootstrap>();
            yield return CampaignWait(()=>Get<NativeMainMenu>(app,"nativeMainMenu")?.ScreenRoot!=null&&!Get<bool>(app,"initializingMenu")&&Get<GameObject>(app,"startupLoadingHost")==null,"ordinary main menu",120);
            app.enabled=false;var menu=Get<NativeMainMenu>(app,"nativeMainMenu");var root=Get<VisualElement>(app,"root");
            menu.Navigation.ActivateElement(root.Q<Button>("main-campaign"));yield return null;menu.Navigation.ActivateElement(root.Q<Button>("campaign-mission-1"));yield return null;menu.Navigation.ActivateElement(root.Q<Button>("campaign-start"));
            yield return CampaignWait(()=>Runtime?.Latest?.Mission!=null&&!Get<bool>(app,"preparing"),"ordinary campaign entry",120);
            var previous=Runtime;previous.RequestStop();
            double end=Time.realtimeSinceStartupAsDouble+30;while(!previous.IsStopped&&Time.realtimeSinceStartupAsDouble<end)yield return null;Assert.True(previous.IsStopped);
            string path=Environment.GetEnvironmentVariable(variable);Assert.IsNotEmpty(path);var bytes=File.ReadAllBytes(path);
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());
            Put(app,"profile",profile);Put(app,"campaignConfiguration",CampaignReconOpening.Configuration(profile));Put(app,"campaignRetryBytes",bytes);Put(app,"campaignRetryRequested",true);
            Call(app,"StartSessionCore",true);Put(app,"inLobby",false);Call(app,"SetMatchUi",true);
            yield return (IEnumerator)Call(app,"PrepareMapPresentation");
            CampaignFrame();yield return CampaignWait(()=>Runtime.IsPrepared&&Get<PlayableSnapshot>(app,"view")?.Mission!=null,"authentic captured world bound to presentation");
            var own=Runtime.Latest.Entities.First(e=>e.Owner==PlayableOwner.Player);Call(app,"FocusPadPoint",own.Position);CampaignFrame();
        }
        IEnumerator CampaignCapture(string name)
        {
            string output=Environment.GetEnvironmentVariable("ORDINARY_PAD_EVIDENCE");if(string.IsNullOrEmpty(output))yield break;
            var panel=host.GetComponent<UIDocument>().panelSettings;var camera=Get<Camera>(app,"cameraView");var root=Get<VisualElement>(app,"root");
            var image=new RenderTexture(1280,800,24);var scene=new RenderTexture(1280,800,24);image.Create();scene.Create();
            var oldPanel=panel.targetTexture;var oldCamera=camera.targetTexture;var oldRect=camera.rect;VisualElement backdrop=null;var prior=RenderTexture.active;
            try
            {
                camera.targetTexture=scene;camera.rect=new Rect(0,0,1,1);camera.Render();camera.targetTexture=oldCamera;camera.rect=oldRect;
                backdrop=new VisualElement{pickingMode=PickingMode.Ignore};backdrop.style.position=Position.Absolute;backdrop.style.left=backdrop.style.right=backdrop.style.top=backdrop.style.bottom=0;backdrop.style.backgroundImage=new StyleBackground(Background.FromRenderTexture(scene));root.Insert(0,backdrop);
                panel.targetTexture=image;for(int i=0;i<8;i++){CampaignFrame();yield return null;}
                RenderTexture.active=image;var pixels=new Texture2D(1280,800,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1280,800),0,0);pixels.Apply();Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());UnityEngine.Object.Destroy(pixels);
            }
            finally{RenderTexture.active=prior;panel.targetTexture=oldPanel;camera.targetTexture=oldCamera;camera.rect=oldRect;backdrop?.RemoveFromHierarchy();image.Release();scene.Release();UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(scene);}
        }
        [UnityTest,Timeout(300000)] public IEnumerator CampaignAuthenticCheckpointPadHudHistoryAndTeaching()
        {
            yield return StartCampaignSnapshot("CAMPAIGN_UI_SECOND_CHECKPOINT");var root=Get<VisualElement>(app,"root");
            Assert.True(Runtime.Latest.Mission.FinalReinforced);Assert.False(Runtime.Latest.Mission.Victory);
            var radio=Runtime.Latest.Mission.Radio.ToArray();Assert.That(radio.Last(),Does.Contain("Четыре машины на связи"));Assert.AreEqual(radio.Length,radio.Distinct().Count());
            yield return CampaignWait(()=>Runtime.Latest.Mission.TutorialQueued==7&&Runtime.Latest.Paused,"lore for all devices on the real restored host");
            Assert.That(root.Q<Label>("campaign-tutorial-copy").text,Does.Contain("лёгкая разведывательная боевая машина"));
            Call(app,"DismissCampaignTutorial");yield return CampaignWait(()=>Runtime.Latest.Mission.TutorialQueued<0&&!Runtime.Latest.Paused,"mouse lore dismissal");
            Put(app,"battleController",true);pads[0].MakeCurrent();
            foreach(int topic in new[]{0,2,5,6})
            {
                yield return CampaignWait(()=>Runtime.Latest.Mission.TutorialQueued==topic&&Runtime.Latest.Paused,"live controller lesson "+topic);
                Assert.AreEqual(DisplayStyle.Flex,root.Q<VisualElement>("campaign-tutorial-layer").resolvedStyle.display);
                if(topic==6){Assert.AreEqual(DisplayStyle.None,root.Q<ScrollView>("campaign-radio").style.display.value);yield return CampaignCapture("campaign-final-army-lesson");}
                Call(app,"DismissCampaignTutorial");yield return CampaignWait(()=>Runtime.Latest.Mission.TutorialQueued!=topic,"dismiss lesson "+topic);
            }
            yield return CampaignWait(()=>!Runtime.Latest.Paused&&Runtime.Latest.Mission.TutorialQueued<0,"all current lessons dismissed; applied move/attack/attack-move topics stay suppressed");
            CollectionAssert.AreEqual(radio,Runtime.Latest.Mission.Radio,"tutorial UI does not duplicate or reorder restored radio");
            Assert.That(root.Q<Label>("campaign-current-goal").text,Does.Contain("Остановить добычу"));Assert.That(root.Q<Label>("campaign-current-goal").text,Does.Not.Contain("Добраться"));
            yield return CampaignCapture("campaign-authentic-second-hud");
            Tap(pads[0],pads[0].selectButton);Assert.True(Get<bool>(app,"mapOpen"));CampaignFrame();
            var history=root.Q<Label>("campaign-revealed-goals");Assert.IsNotNull(history);Assert.That(history.text,Does.Contain("✓ Добраться"));Assert.That(history.text,Does.Contain("Зачистить западный карьер"));Assert.That(history.text,Does.Contain("Зачистить восточный форпост"));
            var map=Get<PlayableMapSurface>(app,"tacticalMap");var goals=(System.Collections.Generic.IReadOnlyList<CampaignObjective>)typeof(PlayableMapSurface).GetField("campaignGoals",F).GetValue(map);CollectionAssert.AreEqual(Runtime.Latest.Mission.Objectives.Select(g=>g.Id),goals.Select(g=>g.Id));Assert.AreEqual(1,goals.Count(g=>!g.Complete));
            yield return CampaignCapture("campaign-authentic-second-tab");Tap(pads[0],pads[0].selectButton);Assert.False(Get<bool>(app,"mapOpen"));
            var own=Get<PlayableSnapshot>(app,"view").Entities.First(e=>e.Owner==PlayableOwner.Player);Call(app,"FocusPadPoint",own.Position);PadCursor(app,own.Position);CampaignFrame();Tap(pads[0],pads[0].buttonSouth);CollectionAssert.AreEqual(new[]{own.Id},Selection(app));
            ButtonFrame(pads[0],pads[0].rightTrigger,true);yield return new WaitForSecondsRealtime(.35f);CampaignFrame();Set(pads[0].leftStick,Vector2.up);InputSystem.Update();CampaignFrame();ButtonFrame(pads[0],pads[0].rightTrigger,false);Set(pads[0].leftStick,Vector2.zero);InputSystem.Update();
            yield return CampaignWait(()=>(Runtime.Latest.Mission.TutorialCompleted&(1<<5))!=0,"actual RT group assignment");
            Tap(pads[0],pads[0].rightShoulder);yield return new WaitForSecondsRealtime(.5f);CampaignFrame();yield return CampaignWait(()=>(Runtime.Latest.Mission.TutorialCompleted&(1<<2))!=0,"actual RB group recall");
            Tap(pads[0],pads[0].rightShoulder);yield return new WaitForSecondsRealtime(.1f);Tap(pads[0],pads[0].rightShoulder);yield return CampaignWait(()=>(Runtime.Latest.Mission.TutorialCompleted&(1<<6))!=0,"actual RB double-tap all army");Assert.AreEqual(Runtime.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player),Selection(app).Count);
            var point=new NavPoint(0,150);Call(app,"FocusPadPoint",point);PadCursor(app,point);Tap(pads[0],pads[0].buttonEast);long move=Get<long>(app,"sequence");
            yield return CampaignWait(()=>Runtime.ReceiptsAfter(0).Any(r=>r.Sequence==move&&r.Status==PlayableCommandStatus.Applied),"actual B move applied by restored threaded host");
            point=new NavPoint(0,184);Call(app,"FocusPadPoint",point);PadCursor(app,point);Tap(pads[0],pads[0].buttonNorth);long advance=Get<long>(app,"sequence");Assert.Greater(advance,move);
            yield return CampaignWait(()=>Runtime.ReceiptsAfter(0).Any(r=>r.Sequence==advance&&r.Status==PlayableCommandStatus.Applied),"actual Y attack-move");
            yield return CampaignWait(()=>Get<PlayableSnapshot>(app,"view").Entities.Any(e=>e.Owner==PlayableOwner.Enemy),"actual visible final contact",90);
            var enemy=Get<PlayableSnapshot>(app,"view").Entities.First(e=>e.Owner==PlayableOwner.Enemy);Call(app,"FocusPadPoint",enemy.Position);PadCursor(app,enemy.Position);Tap(pads[0],pads[0].buttonEast);long attack=Get<long>(app,"sequence");Assert.Greater(attack,advance);
            yield return CampaignWait(()=>Runtime.ReceiptsAfter(0).Any(r=>r.Sequence==attack&&r.Status==PlayableCommandStatus.Applied),"actual B attack on visible defender");
            Assert.AreEqual(0xff,Runtime.Latest.Mission.TutorialCompleted,"all seven applied control topics plus lore persist on the actual host");
        }
        [UnityTest,Timeout(240000)] public IEnumerator CampaignActualVictoryPersistsAndReplayStartsClean()
        {
            const string key="campaign.disputed-territory.completed";campaignPreferenceHad=PlayerPrefs.HasKey(key);campaignPreferencePrior=PlayerPrefs.GetInt(key);campaignPreferenceCaptured=true;
            TestContext.WriteLine("Campaign preference baseline present="+campaignPreferenceHad+" value="+campaignPreferencePrior);PlayerPrefs.DeleteKey(key);
            try
            {
                yield return StartCampaignSnapshot("CAMPAIGN_UI_VICTORY_WORLD");Put(app,"battleController",false);
                yield return CampaignWait(()=>Runtime.Latest.Outcome==PlayableMatchOutcome.PlayerWon&&PlayerPrefs.GetInt(key)==1,"actual authoritative victory persists completion");
                Assert.True(Runtime.Latest.Mission.Victory);Assert.IsNotNull(Runtime.Result);Assert.That(Runtime.Latest.Mission.Radio.Last(),Does.Contain("Территория под нашим контролем"));
                yield return CampaignWait(()=>Runtime.Latest.Mission.TutorialQueued==7,"unseen lore from headless victory source");Call(app,"DismissCampaignTutorial");yield return CampaignWait(()=>Runtime.Latest.Mission.TutorialQueued<0,"victory lore dismissed");
                CampaignFrame();Assert.That(Get<Label>(app,"modalTitle").text,Does.Contain("ПОБЕД"));Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(app,"root").Q<ScrollView>("campaign-radio").style.display.value);yield return CampaignCapture("campaign-actual-victory");
                Call(app,"ReturnToMainMenu");yield return CampaignWait(()=>Get<bool>(app,"inLobby")&&Get<NativeMainMenu>(app,"nativeMainMenu")?.ScreenRoot!=null,"completed campaign returns to ordinary menu",allowStopped:true);
                var menu=Get<NativeMainMenu>(app,"nativeMainMenu");var root=Get<VisualElement>(app,"root");menu.Navigation.ActivateElement(root.Q<Button>("main-campaign"));yield return null;
                var mission=root.Q<Button>("campaign-mission-1");Assert.That(mission.text,Does.Contain("пройдена"));menu.Navigation.ActivateElement(mission);yield return null;Assert.True(root.Q<Button>("campaign-start").enabledSelf);menu.Navigation.ActivateElement(root.Q<Button>("campaign-start"));
                yield return CampaignWait(()=>Runtime?.Latest?.Mission!=null&&!Get<bool>(app,"preparing"),"completed mission actually replays from ordinary menu",120);
                Assert.False(Runtime.Latest.Mission.Victory);Assert.False(Runtime.Latest.Mission.Reinforced);Assert.AreEqual(1,Runtime.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player));Assert.AreEqual(1,PlayerPrefs.GetInt(key),"fresh replay preserves earned completion");
            }
            finally{RestoreCampaignPreference();}
        }
    }
}
