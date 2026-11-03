using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        [UnityTest] public IEnumerator HackMouseChoiceAndCaptureRespectLocalViews()=>HackInteraction(false);
        [UnityTest] public IEnumerator HackPadTargetCancelAndCaptureRespectLocalViews()=>HackInteraction(true);
        private IEnumerator HackInteraction(bool pad)
        {
            yield return StartOrdinary(2,configureRoster:d=>{if(!pad)d.Participants[0].DeviceId=0;},mapName:"Переправа (4)");
            var seat=Seats[0];var other=Seats[1];Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"arrange hack pause");
            var initial=Get<PlayableSnapshot>(seat,"view");var enemy=Get<PlayableSnapshot>(other,"view").Owner;
            var domain=typeof(PlayableRuntime).GetField("domain",F).GetValue(Runtime);var type=domain.GetType();
            var origin=initial.Entities.First(e=>e.Owner==initial.Owner).Position;
            var nav=(NavigationSession)type.GetProperty("Navigation",F).GetValue(domain);
            NavPoint point=default;bool found=false;
            foreach(var offset in new[]{new NavPoint(4,0),new NavPoint(-4,0),new NavPoint(0,4),new NavPoint(0,-4),new NavPoint(5,3)}){
                point=new NavPoint(origin.X+offset.X,origin.Z+offset.Z);if(nav.Crowd.CanPlace(point,PlayableUnitRules.Radius(Get<PlayableProfile>(seat,"profile"),PlayableEntityKind.Tank))){found=true;break;}
            }
            Assert.True(found,"supported visible target placement");int id=(int)type.GetMethod("SpawnUnit",F).Invoke(domain,new object[]{point,enemy,PlayableEntityKind.Tank});
            var units=(System.Collections.IDictionary)type.GetField("units",F).GetValue(domain);
            foreach(var u in units.Values){u.GetType().GetField("Reload").SetValue(u,1000d);if((int)u.GetType().GetField("Id").GetValue(u)==id)u.GetType().GetField("Health").SetValue(u,40d);}
            type.GetMethod("CreditOrbital",F).Invoke(domain,new object[]{(PlayableOwner?)initial.Owner,enemy,250d});
            Runtime.RequestPause(false);yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.PendingLevel==1,"hack choice published");
            Assert.True((bool)Call(seat,"OpenOrbital",pad));
            var cards=Get<VisualElement>(seat,"orbitalCards");Assert.AreEqual(3,cards.childCount);Assert.AreEqual("Взлом управления",cards.Q("orbital-choice-8").Q<Label>("orbital-choice-title").text);
            yield return Capture(pad?"hack-pad-three-choices":"hack-mouse-three-choices",captureWidth:1600,captureHeight:1000,captureChecks:()=>{foreach(var c in cards.Children())Inside(Get<VisualElement>(seat,"root").worldBound,c.worldBound,"choice");});
            Call(seat,"SubmitOrbital",true,OrbitalAbilities.ControlHack);yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.Abilities.Count==1,"hack selected");
            var otherCamera=Get<Camera>(other,"cameraView").transform.position;
            if(pad){Assert.True((bool)Call(seat,"OpenOrbital",true));Call(seat,"SubmitOrbital",false,OrbitalAbilities.ControlHack);}
            else Call(seat,"ApplyOrbitalFromHud",OrbitalAbilities.ControlHack);
            Assert.True(Get<bool>(seat,"scanTargeting"));Assert.True((bool)Call(seat,"CancelScanTarget"));Assert.Zero(Get<PlayableSnapshot>(seat,"view").Orbital.Abilities[0].Remaining);
            if(pad){Assert.True((bool)Call(seat,"OpenOrbital",true));Call(seat,"SubmitOrbital",false,OrbitalAbilities.ControlHack);Put(seat,"padCursorGround",point);Call(seat,"PollScanTargetPad",pads[0]);Press(pads[0].buttonSouth);InputSystem.Update();Call(seat,"PollScanTargetPad",pads[0]);Release(pads[0].buttonSouth);InputSystem.Update();}
            else {Call(seat,"ApplyOrbitalFromHud",OrbitalAbilities.ControlHack);var entity=Get<PlayableSnapshot>(seat,"view").Entities.Single(e=>e.Id==id);var at=(Vector2)Get<Camera>(seat,"cameraView").WorldToScreenPoint(Get<PlayableWorld>(seat,"world").EntityPoint(entity)+Vector3.up*.5f);Assert.True((bool)Call(seat,"ScanPointer",at,true,true,false));}
            yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Entities.Any(e=>e.Id==id&&e.Owner==initial.Owner),"hack live owner transfer");
            Assert.Greater(Get<PlayableSnapshot>(seat,"view").Orbital.Abilities[0].Remaining,0);Assert.AreEqual(otherCamera,Get<Camera>(other,"cameraView").transform.position);
            yield return Wait(()=>Get<NativeMatchNotifications>(other,"matchNotifications").Latest?.Text=="Юнит захвачен","former owner ordinary notice");
            yield return Capture(pad?"hack-pad-captured":"hack-mouse-captured",captureWidth:1600,captureHeight:1000,captureChecks:()=>Assert.Greater(Get<PlayableSnapshot>(seat,"view").Orbital.Abilities[0].Remaining,0));
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"hack stop");
        }
    }
}
