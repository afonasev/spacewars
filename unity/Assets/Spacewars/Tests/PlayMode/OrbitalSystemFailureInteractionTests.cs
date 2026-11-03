using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        [UnityTest] public IEnumerator SystemFailureDisablesQueuesAndShowsPowerOffInLocalViewsMouse()=>SystemFailureInteraction(false);
        [UnityTest] public IEnumerator SystemFailureDisablesQueuesAndShowsPowerOffInLocalViewsPad()=>SystemFailureInteraction(true);
        private IEnumerator SystemFailureInteraction(bool pad)
        {
            // The manually constructed test host intentionally skips Start. Pump only
            // its existing preparation Update route; enabling the component would run
            // production Start a second time over the fixture's UIDocument.
            IEnumerator ServicePreparation(){yield return null;while(app!=null&&Get<bool>(app,"preparing")){Call(app,"UpdateFrame");yield return null;}}
            yield return StartOrdinary(2,configureRoster:d=>{app.StartCoroutine(ServicePreparation());if(!pad)d.Participants[0].DeviceId=0;},mapName:"Переправа (4)");var caster=Seats[0];var victim=Seats[1];
            Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"arrange system failure");
            var domain=typeof(PlayableRuntime).GetField("domain",F).GetValue(Runtime);var type=domain.GetType();var profile=Get<PlayableProfile>(caster,"profile");
            object Domain(string method,params object[] args)=>type.GetMethod(method,F).Invoke(domain,args);
            var victimView=Get<PlayableSnapshot>(victim,"view");var enemy=victimView.Owner;var owner=Get<PlayableSnapshot>(caster,"view").Owner;
            var home=victimView.Buildings.First(b=>b.Owner==enemy&&b.Kind==PlayableBuildingKind.Headquarters);
            Domain("AddCredits",enemy,10000d);
            foreach(var pair in new[]{(PlayableBuildingKind.Factory,1),(PlayableBuildingKind.ScientificCenter,2)}){Assert.AreEqual(PlayableCommandStatus.Applied,Domain("BuildAt",home.SiteId,pair.Item2,pair.Item1,home.Id,enemy,null));Domain("AdvanceFoundations");Domain("AdvanceBuildings",100d);}
            var arranged=(PlayableSnapshot)Domain("PlayerSnapshot",1L,RuntimeStatus.Running,true,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,enemy);
            var factory=arranged.Buildings.Single(b=>b.SiteId==home.SiteId&&b.Kind==PlayableBuildingKind.Factory);var science=arranged.Buildings.Single(b=>b.SiteId==home.SiteId&&b.Kind==PlayableBuildingKind.ScientificCenter);
            for(int i=0;i<2;i++)Assert.AreEqual(PlayableCommandStatus.Applied,Domain("QueueUnit",factory.Id,enemy,PlayableEntityKind.Tank,null));
            foreach(var research in new[]{PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.ImprovedDefensiveSystems,PlayableResearchKind.ReinforcedConcrete})Assert.AreEqual(PlayableCommandStatus.Applied,Domain("QueueResearch",science.Id,enemy,research,null));
            Domain("AdvanceProduction",.5d);Domain("AdvanceResearch",.5d);
            var point=new NavPoint((factory.Position.X+science.Position.X)/2,(factory.Position.Z+science.Position.Z)/2);
            var data=profile.CopyData();Domain("CreditOrbital",(PlayableOwner?)owner,enemy,data.orbitalThreshold1+data.orbitalThreshold2+data.orbitalThreshold3);
            foreach(int id in new[]{1,2,3})Assert.AreEqual(PlayableCommandStatus.Applied,Domain("ChooseOrbital",owner,id));
            Assert.False(Get<PlayableSnapshot>(caster,"view").Vision.IsVisible(point));Put(caster,"scanTargetAbility",3);
            StringAssert.Contains("текущем обзоре",(string)Call(caster,"AbilityTargetFeedback",(NavPoint?)point,0,false));
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Domain("UseOrbital",owner,3,point));Assert.Zero(((OrbitalEscalationState)Domain("Orbital",owner)).CommonRemaining);
            // Arrange a live shared scan to make the enemy target preview observable.
            Assert.AreEqual(PlayableCommandStatus.Applied,Domain("UseOrbital",owner,1,point));var orbital=Domain("Orbital",owner);orbital.GetType().GetMethod("Advance").Invoke(orbital,new object[]{10d,data});
            Runtime.RequestPause(false);yield return Wait(()=>!Runtime.Latest.Paused&&!Get<bool>(caster,"paused")&&Get<PlayableSnapshot>(caster,"view").Orbital.Abilities.Count==3,"advanced ability published and resumed");
            Call(caster,"FocusScanPoint",point);Call(victim,"FocusScanPoint",point);if(!pad){Set(UnityEngine.InputSystem.Mouse.current.position,Get<Camera>(caster,"cameraView").pixelRect.center);UnityEngine.InputSystem.InputSystem.Update();}if(pad){Assert.True((bool)Call(caster,"OpenOrbital",true));Call(caster,"SubmitOrbital",false,3);}else Call(caster,"ApplyOrbitalFromHud",3);Assert.True(Get<bool>(caster,"scanTargeting"));Call(caster,"CancelScanTarget");
            Assert.Zero(Get<PlayableSnapshot>(caster,"view").Orbital.Abilities.Single(a=>a.Id==3).Remaining);
            Call(caster,"BeginOrbitalTarget",pad,3);PadCursor(caster,point);Call(caster,"UpdateScanPresentation");
            yield return Capture("system-failure-"+(pad?"pad":"mouse")+"-preview",()=>{PadCursor(caster,point);Call(caster,"UpdateScanPresentation");},captureWidth:1600,captureHeight:1000,captureChecks:()=>{StringAssert.Contains("Сбой систем",Get<VisualElement>(caster,"root").Q<Label>("ability-target-title").text);StringAssert.Contains("Отключить",Get<VisualElement>(caster,"root").Q<Label>("ability-target-apply").text);});
            if(pad){Call(caster,"PollScanTargetPad",pads[0]);Press(pads[0].buttonSouth);UnityEngine.InputSystem.InputSystem.Update();Call(caster,"PollScanTargetPad",pads[0]);Release(pads[0].buttonSouth);UnityEngine.InputSystem.InputSystem.Update();}
            else {var at=(Vector2)Get<Camera>(caster,"cameraView").WorldToScreenPoint(Get<PlayableWorld>(caster,"world").Point(point));Assert.True((bool)Call(caster,"ScanPointer",at,true,true,false));}
            yield return Wait(()=>Get<PlayableSnapshot>(victim,"view").Buildings.Any(b=>b.Id==factory.Id&&b.SystemsDisabled),"threaded system disable");
            Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"freeze system failure capture");
            Selection(victim).Clear();Selection(victim).Add(factory.Id);Call(app,"UpdateFrame");
            var frozen=Get<PlayableSnapshot>(victim,"view").Buildings.Single(b=>b.Id==factory.Id);Assert.AreEqual(2,frozen.PrivateState.Orders.Count);Assert.Greater(frozen.PrivateState.Orders[0].Progress,0);
            Assert.IsEmpty(OfflinePadWorldActions.Building(Get<PlayableSnapshot>(victim,"view"),profile,factory.Id));
            yield return Capture("system-failure-"+(pad?"pad":"mouse")+"-frozen-production",()=>{Selection(victim).Clear();Selection(victim).Add(factory.Id);Call(app,"UpdateFrame");},captureWidth:1600,captureHeight:1000,captureChecks:()=>{
                var root=Get<VisualElement>(victim,"root");Assert.Greater(root.Query("system-failure-marker").ToList().Count,0);Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(victim,"battleRing").style.display.value);Assert.False(root.Q<Button>("production-slot-0").enabledSelf);
            });
            Selection(victim).Clear();Selection(victim).Add(science.Id);Call(app,"UpdateFrame");
            yield return Capture("system-failure-"+(pad?"pad":"mouse")+"-frozen-research",()=>{Selection(victim).Clear();Selection(victim).Add(science.Id);Call(app,"UpdateFrame");},captureWidth:1600,captureHeight:1000,captureChecks:()=>Assert.AreEqual(3,Get<PlayableSnapshot>(victim,"view").OwnerResearch.Count(o=>!o.Complete)));
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"system failure stop");
        }
    }
}
