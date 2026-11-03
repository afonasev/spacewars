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
        [Test] public void ScanMapStrokeWidthRefreshesOnProfileRebind()
        {
            var p=PlayableProfile.Default;var map=new PlayableMapSurface(p,null);
            float Width()=>(float)typeof(PlayableMapSurface).GetField("scanLineWidth",F).GetValue(map);
            Assert.AreEqual((float)p.CopyData().orbitalScanLineWidth,Width());
            var data=p.CopyData();data.orbitalScanLineWidth=2.5;map.Rebind(PlayableProfile.Create(data),null);Assert.AreEqual(2.5f,Width());
        }
        [UnityTest] public IEnumerator ScanCardsAndTabTargetUseLiveAuthority()
        {
            yield return StartOrdinary(2,configureRoster:d=>d.Participants[0].DeviceId=0);
            var seat=Seats[0];var other=Seats[1];Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"seed pause");
            var initial=Get<PlayableSnapshot>(seat,"view");var domain=typeof(PlayableRuntime).GetField("domain",F).GetValue(Runtime);var type=domain.GetType();
            type.GetMethod("CreditOrbital",F).Invoke(domain,new object[]{(PlayableOwner?)initial.Owner,Get<PlayableSnapshot>(other,"view").Owner,250d});
            Runtime.RequestPause(false);yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.PendingLevel==1,"unlock");
            Assert.True((bool)Call(seat,"OpenOrbital",false));
            Action CheckCards=()=>{
                var cards=Get<VisualElement>(seat,"orbitalCards");var root=Get<VisualElement>(seat,"root");
                foreach(var column in cards.Children())Inside(root.worldBound,column.worldBound,"choice block");
                var scan=cards.Q("orbital-choice-1");Assert.AreEqual("Сканирование",scan.Q<Label>("orbital-choice-title").text);
                var desc=scan.Q<Label>("orbital-choice-description");StringAssert.DoesNotContain("секунд",desc.text);StringAssert.DoesNotContain("3",desc.text);
                Assert.GreaterOrEqual(desc.worldBound.yMin,scan.Q<Button>().worldBound.yMax,"description below card");
                CollectionAssert.Contains(scan.Q("orbital-choice-stats").Query<Label>().ToList().Select(l=>l.text).ToArray(),"3 с");
            };
            yield return Capture("scan-choice-1600",captureWidth:1600,captureHeight:1000,captureChecks:CheckCards);
            yield return Capture("scan-choice-1280",captureWidth:1280,captureHeight:720,captureChecks:CheckCards);
            Call(seat,"SubmitOrbital",true,1);yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.Abilities.Count==1,"choice applied");
            Call(seat,"ApplyOrbitalFromHud",1);Assert.True(Get<bool>(seat,"scanTargeting"));Assert.Zero(Get<PlayableSnapshot>(seat,"view").Orbital.Abilities[0].Remaining);
            Assert.True((bool)Call(seat,"CancelScanTarget"));Assert.Zero(Get<PlayableSnapshot>(seat,"view").Orbital.Abilities[0].Remaining);
            Call(seat,"ApplyOrbitalFromHud",1);Call(seat,"ToggleMap");Assert.True(Get<bool>(seat,"mapOpen"));Assert.True(Get<bool>(seat,"scanTargeting"),"opening TAB retains targeting");
            for(int i=0;i<3;i++){yield return null;Call(app,"UpdateFrame");}
            var map=Get<PlayableMapSurface>(seat,"tacticalMap");var bounds=map.worldBound;var panel=host.GetComponent<UIDocument>().panelSettings;
            var screen=new Vector2((bounds.xMin+bounds.width*.6f)*panel.scale,Screen.height-(bounds.yMin+bounds.height*.4f)*panel.scale);
            var target=(NavPoint)Call(seat,"MapGround",2,screen);var camera=Get<Camera>(seat,"cameraView");var otherCamera=Get<Camera>(other,"cameraView");var otherBefore=otherCamera.transform.position;
            Set(Mouse.current.position,screen);InputSystem.Update();Call(seat,"MapSelect",2,screen,screen,false);
            yield return Wait(()=>!Get<bool>(seat,"scanTargeting"),"TAB receipt applied");
            Assert.False(Get<bool>(seat,"mapOpen"));var centered=(NavPoint)Call(seat,"Ground",Get<Vector2>(seat,"battleCursor"));
            Assert.AreEqual(target.X,centered.X,.1);Assert.AreEqual(target.Z,centered.Z,.1);Assert.AreEqual(otherBefore,otherCamera.transform.position,"other camera unchanged");
            Assert.True(Get<PlayableSnapshot>(seat,"view").Vision.IsVisible(target));Assert.Greater(Get<PlayableSnapshot>(seat,"view").ScanLines.Count,0);
            yield return Capture("scan-active-tab",captureWidth:1600,captureHeight:1000,captureChecks:()=>Assert.False(Get<bool>(seat,"mapOpen")));
            yield return new WaitForSeconds(.25f);
            yield return Capture("scan-light-sweep",captureWidth:1600,captureHeight:1000,captureChecks:()=>
            {
                var current=Get<PlayableSnapshot>(seat,"view");Assert.Greater(current.ScanTriangles.Count,0,"light field is visible during active scan");
                var projected=Get<System.Collections.Generic.Dictionary<(double,double),Vector2>>(seat,"scanProjectionCache");
                Assert.Greater(projected.Count,0);Assert.Less(projected.Count,current.ScanTriangles.Count*3,"shared world points are projected once, without dropping faces");
                var shared=Get<PlayableMapSurface>(app,"sharedLocalMap");
                var layers=(System.Collections.Generic.IReadOnlyList<OrbitalScanTriangle>[])typeof(PlayableMapSurface).GetField("scanTriangleLayers",F).GetValue(shared);
                Assert.True(layers.Any(layer=>ReferenceEquals(layer,current.ScanTriangles)),"shared minimap retains original clipped buffers instead of cloning vertices");
            });
            yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").ScanLines.Count==0,"scan expired");
            Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(seat,"scanLayer").style.display.value,"expired scan overlay is not continuously repainted");
            Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"reset cooldown fixture");type.GetMethod("AdvanceOrbital",F).Invoke(domain,new object[]{30d});Runtime.RequestPause(false);
            yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.Abilities[0].Remaining==0,"ready again");
            Call(seat,"ApplyOrbitalFromHud",1);var beforeMini=camera.transform.position;var mini=Get<PlayableMapSurface>(app,"sharedLocalMap");var mb=mini.worldBound;var screenMini=new Vector2(mb.center.x*panel.scale,Screen.height-mb.center.y*panel.scale);Call(seat,"MapSelect",1,screenMini,screenMini,false);
            yield return Wait(()=>!Get<bool>(seat,"scanTargeting"),"minimap command receipt");Assert.AreEqual(beforeMini,camera.transform.position,"minimap confirmation preserves camera");
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [UnityTest] public IEnumerator ScanGamepadCursorMovesAndTabConfirmationIsOwnerBound()
        {
            yield return StartOrdinary(2);var seat=Seats[0];var other=Seats[1];var pad=pads[0];
            Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"seed pause");
            var owner=Get<PlayableSnapshot>(seat,"view").Owner;var domain=typeof(PlayableRuntime).GetField("domain",F).GetValue(Runtime);var type=domain.GetType();
            type.GetMethod("CreditOrbital",F).Invoke(domain,new object[]{(PlayableOwner?)owner,Get<PlayableSnapshot>(other,"view").Owner,250d});type.GetMethod("ChooseOrbital",F).Invoke(domain,new object[]{owner,1});
            Runtime.RequestPause(false);yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.Abilities.Count==1,"ready scan");
            Put(seat,"battleController",true);Assert.True((bool)Call(seat,"OpenOrbital",true));Call(seat,"SubmitOrbital",false,1);Assert.True(Get<bool>(seat,"scanTargeting"));
            InputSystem.Update();Call(seat,"PollOrdinaryPad");Call(seat,"PollOrdinaryPad");
            var before=Get<Vector2>(seat,"battleCursor");Set(pad.rightStick,new Vector2(.7f,.2f));InputSystem.Update();yield return null;Call(seat,"PollOrdinaryPad");
            Assert.Greater((Get<Vector2>(seat,"battleCursor")-before).magnitude,0,"scan cursor moves despite ordinary gesture neutral gate");
            Set(pad.rightStick,Vector2.zero);InputSystem.Update();Call(seat,"PollOrdinaryPad");
            ButtonFrame(pad,pad.selectButton,true);Assert.True(Get<bool>(seat,"mapOpen"));ButtonFrame(pad,pad.selectButton,false);
            var mapBefore=Get<NavPoint>(seat,"padCursorGround");Set(pad.leftStick,Vector2.right);InputSystem.Update();yield return null;Call(seat,"PollOrdinaryPad");
            Assert.Greater(Get<NavPoint>(seat,"padCursorGround").X,mapBefore.X,"TAB left stick moves scan target");Set(pad.leftStick,Vector2.zero);InputSystem.Update();
            var otherBefore=Get<Camera>(other,"cameraView").transform.position;
            ButtonFrame(pad,pad.buttonSouth,true);ButtonFrame(pad,pad.buttonSouth,false);yield return Wait(()=>!Get<bool>(seat,"scanTargeting"),"controller scan applied");
            Assert.False(Get<bool>(seat,"mapOpen"));Assert.AreEqual(otherBefore,Get<Camera>(other,"cameraView").transform.position);Assert.Greater(Get<PlayableSnapshot>(seat,"view").ScanLines.Count,0);
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
    }
}
