using System;
using System.Collections;
using System.Linq;
using System.Reflection;
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
        [UnityTest] public IEnumerator SingleDetailsUseRosterRtBackAndStayLocal()
        {
            compactBases.Clear();yield return StartOrdinary(2);CompactContext(1,PlayableEntityKind.Tank,true);var first=Seats[0];var other=Seats[1];var pad=pads[0];
            Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(first,"selectionStats").style.display.value);float zoom=Get<Camera>(first,"cameraView").orthographicSize;int receipts=Runtime.OfflineFrame.Receipts.Count;
            Press(pad.rightTrigger);InputSystem.Update();Call(first,"PollOrdinaryPad");
            Assert.False(Get<bool>(first,"orbitalOpen"),"RT expands details without opening orbital support");Assert.True(Get<bool>(first,"selectionRosterExpanded"));Assert.True(Get<bool>(first,"selectionRosterPadFocus"));Assert.False(Get<bool>(other,"selectionRosterExpanded"));Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(first,"selectionStats").style.display.value);StringAssert.Contains("RT",Get<Button>(first,"selectionExpand").tooltip);
            Release(pad.rightTrigger);InputSystem.Update();Call(first,"PollOrdinaryPad");Press(pad.dpad.down);InputSystem.Update();Call(first,"PollOrdinaryPad");Assert.AreEqual(zoom,Get<Camera>(first,"cameraView").orthographicSize);Release(pad.dpad.down);InputSystem.Update();Call(first,"PollOrdinaryPad");
            Press(pad.buttonEast);InputSystem.Update();Call(first,"PollOrdinaryPad");Assert.False(Get<bool>(first,"selectionRosterPadFocus"));Assert.True(Get<bool>(first,"selectionRosterExpanded"),"B leaves details open, matching existing roster focus semantics");Release(pad.buttonEast);InputSystem.Update();Call(first,"PollOrdinaryPad");
            Press(pad.rightTrigger);InputSystem.Update();Call(first,"PollOrdinaryPad");Assert.False(Get<bool>(first,"selectionRosterExpanded"));Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(first,"selectionStats").style.display.value);Release(pad.rightTrigger);InputSystem.Update();Call(first,"PollOrdinaryPad");
            Call(first,"ToggleSelectionRoster",false);Assert.True(Get<bool>(first,"selectionRosterExpanded"));CompactContext(2,reset:true);Selection(first).Clear();Selection(first).Add(20001);Call(first,"UpdateHud");Assert.False(Get<bool>(first,"selectionRosterExpanded"),"replacement starts collapsed");
            Assert.AreEqual(receipts,Runtime.OfflineFrame.Receipts.Count);Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [UnityTest] public IEnumerator RtSingleDetailsKeepCameraAndCursorMoving()=>RtCardKeepsStickMotion(1);
        [UnityTest] public IEnumerator RtGroupRosterKeepsCameraAndCursorMoving()=>RtCardKeepsStickMotion(50);
        private IEnumerator RtCardKeepsStickMotion(int count)
        {
            compactBases.Clear();yield return StartOrdinary(2);CompactContext(count,reset:true);
            // The pad-only seat avoids unrelated keyboard-seat mouse edge panning.
            var seat=Seats[1];var other=Seats[0];var pad=pads[1];var camera=Get<Camera>(seat,"cameraView");
            var otherCamera=Get<Camera>(other,"cameraView").transform.position;var otherCursor=Get<Vector2>(other,"battleCursor");
            var selection=Selection(seat).ToArray();long sequence=Get<long>(seat,"sequence");float zoom=camera.orthographicSize;
            PadCursor(seat,(NavPoint)Call(seat,"Ground",camera.pixelRect.center+new Vector2(-60,40)));
            Press(pad.rightTrigger);InputSystem.Update();Call(seat,"PollOrdinaryPad");Release(pad.rightTrigger);InputSystem.Update();Call(seat,"PollOrdinaryPad");
            Assert.True(Get<bool>(seat,"selectionRosterPadFocus"));Assert.True(Get<bool>(seat,"selectionRosterExpanded"));
            var start=camera.transform.position;var cursor=Get<Vector2>(seat,"battleCursor");
            Set(pad.leftStick,Vector2.right);Set(pad.rightStick,Vector2.up);InputSystem.Update();
            for(int i=0;i<3;i++){yield return null;Call(seat,"PollOrdinaryPad");}
            Assert.Greater(Vector3.Distance(start,camera.transform.position),.01f,"LS must pan while RT card is focused");
            Assert.Greater(Vector2.Distance(cursor,Get<Vector2>(seat,"battleCursor")),1,"RS must move the field cursor while card is focused");
            Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(seat,"battleCursorVisual").style.display.value,"cursor must be rendered at its new position");
            Assert.True(Get<bool>(seat,"selectionRosterPadFocus"));Assert.AreEqual(zoom,camera.orthographicSize);
            CollectionAssert.AreEqual(selection,Selection(seat));Assert.AreEqual(sequence,Get<long>(seat,"sequence"));
            yield return Capture("rt-card-motion-"+count,()=>Call(seat,"PollOrdinaryPad"),1600,1000,captureChecks:()=>
            {
                Assert.True(Get<bool>(seat,"selectionRosterExpanded"));Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(seat,"battleCursorVisual").style.display.value);
            },refreshWorld:false);
            // Keep both sticks held across B, its release gate, and the next field frame.
            start=camera.transform.position;Press(pad.buttonEast);InputSystem.Update();Call(seat,"PollOrdinaryPad");
            Assert.False(Get<bool>(seat,"selectionRosterPadFocus"));Release(pad.buttonEast);InputSystem.Update();Call(seat,"PollOrdinaryPad");
            for(int i=0;i<3;i++){yield return null;Call(seat,"PollOrdinaryPad");}
            Assert.Greater(Vector3.Distance(start,camera.transform.position),.01f,"closing must not wait for neutral sticks");
            Assert.False(Get<OfflinePadGestures>(seat,"battleGestures").Blocked,"real neutral buttons rearm field gestures even while panning");
            Assert.AreEqual(sequence,Get<long>(seat,"sequence"),"B/release cannot issue Stop or any field command");
            // Reopen then close with RT while the same sticks remain held.
            Press(pad.rightTrigger);InputSystem.Update();Call(seat,"PollOrdinaryPad");Release(pad.rightTrigger);InputSystem.Update();Call(seat,"PollOrdinaryPad");
            Assert.False(Get<bool>(seat,"selectionRosterExpanded"));start=camera.transform.position;
            for(int i=0;i<3;i++){yield return null;Call(seat,"PollOrdinaryPad");}
            Assert.Greater(Vector3.Distance(start,camera.transform.position),.01f);Assert.AreEqual(sequence,Get<long>(seat,"sequence"));
            Assert.AreEqual(otherCamera,Get<Camera>(other,"cameraView").transform.position);Assert.AreEqual(otherCursor,Get<Vector2>(other,"battleCursor"));
            // A real focus loss still cancels card ownership and preserves the
            // normal neutral-stick gate; it must not use the card-close shortcut.
            Press(pad.rightTrigger);InputSystem.Update();Call(seat,"PollOrdinaryPad");Release(pad.rightTrigger);InputSystem.Update();Call(seat,"PollOrdinaryPad");
            Assert.True(Get<bool>(seat,"selectionRosterPadFocus"));Put(seat,"localInputFocused",false);start=camera.transform.position;Call(seat,"PollOrdinaryPad");
            Assert.AreEqual(start,camera.transform.position);Assert.False(Get<bool>(seat,"selectionRosterPadFocus"));Assert.False(Get<bool>(seat,"selectionRosterReleaseGate"));
            Put(seat,"localInputFocused",true);Call(seat,"PollOrdinaryPad");Assert.AreEqual(start,camera.transform.position);Assert.True(Get<OfflinePadGestures>(seat,"battleGestures").Blocked);
            Set(pad.leftStick,Vector2.zero);Set(pad.rightStick,Vector2.zero);InputSystem.Update();Call(seat,"PollOrdinaryPad");
            Assert.False(Get<OfflinePadGestures>(seat,"battleGestures").Blocked);Assert.AreEqual(sequence,Get<long>(seat,"sequence"));
        }
        [UnityTest] public IEnumerator MouseUsesIconDirectlyAndLevelOpensOnlyPendingCards()
        {
            yield return StartOrdinary(2,configureRoster:d=>d.Participants[0].DeviceId=0);var seat=Seats[0];var other=Seats[1];var initial=Get<PlayableSnapshot>(seat,"view");var selected=Selection(seat).ToArray();
            Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"paused fixture setup");
            // Fixture initialization at the paused authority boundary. Real command execution below
            // uses the ordinary threaded runtime, not a substituted presentation snapshot.
            var domain=typeof(PlayableRuntime).GetField("domain",F).GetValue(Runtime);var type=domain.GetType();
            type.GetMethod("CreditOrbital",F).Invoke(domain,new object[]{(PlayableOwner?)initial.Owner,Get<PlayableSnapshot>(other,"view").Owner,750d});
            Assert.AreEqual(PlayableCommandStatus.Applied,type.GetMethod("ChooseOrbital",F).Invoke(domain,new object[]{initial.Owner,1}));
            Runtime.RequestPause(false);yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.Abilities.Count==1,"seed published");Call(seat,"UpdateOrbitalHud");
            Assert.AreEqual(DisplayStyle.None,Get<Label>(seat,"orbitalLtHint").style.display.value);Assert.AreEqual(DisplayStyle.None,Get<Label>(seat,"orbitalXpTooltip").style.display.value);
            var level=Get<Button>(seat,"orbitalLevelButton");using(var enter=PointerEnterEvent.GetPooled()){enter.target=level;level.SendEvent(enter);}Call(seat,"UpdateOrbitalHud");Assert.AreEqual(DisplayStyle.Flex,Get<Label>(seat,"orbitalXpTooltip").style.display.value);StringAssert.Contains("0 / 1000",Get<Label>(seat,"orbitalXpTooltip").text);
            yield return Capture("floating-mouse-hover",()=>{Put(seat,"orbitalLevelHovered",true);Call(seat,"UpdateOrbitalHud");},1600,1000,captureChecks:()=>Inside(Get<VisualElement>(seat,"root").worldBound,Get<Label>(seat,"orbitalXpTooltip").worldBound,"XP tooltip"));
            using(var leave=PointerLeaveEvent.GetPooled()){leave.target=level;level.SendEvent(leave);}Call(seat,"UpdateOrbitalHud");Assert.AreEqual(DisplayStyle.None,Get<Label>(seat,"orbitalXpTooltip").style.display.value);
            var nav=Get<NativeMenuNavigation>(seat,"menuNavigation");nav.ActivateElement(Get<VisualElement>(seat,"orbitalSlots").Q<Button>("orbital-slot-1"));
            Assert.True(Get<bool>(seat,"scanTargeting"));Assert.Zero(Get<PlayableSnapshot>(seat,"view").Orbital.Abilities[0].Remaining,"choosing the icon only enters targeting");Call(seat,"ConfirmScan",new NavPoint(0,0),false);yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.Abilities[0].Remaining>0,"mouse use applied");Assert.False(Get<bool>(seat,"orbitalOpen"));CollectionAssert.AreEqual(selected,Selection(seat));Assert.AreEqual(0,Get<PlayableSnapshot>(other,"view").Orbital.Level);
            yield return Capture("floating-mouse-cooldown",captureWidth:1600,captureHeight:1000,captureChecks:()=>Assert.False(Get<bool>(seat,"orbitalOpen")));
            long sequence=Get<long>(seat,"sequence");nav.ActivateElement(Get<VisualElement>(seat,"orbitalSlots").Q<Button>("orbital-slot-1"));Assert.AreEqual(sequence,Get<long>(seat,"sequence"),"cooling click cannot submit another command");
            nav.ActivateElement(level);Assert.True(Get<bool>(seat,"orbitalOpen"));Assert.AreEqual(3,Get<VisualElement>(seat,"orbitalCards").childCount);Assert.AreEqual(DisplayStyle.None,Get<OrbitalBattleRing>(seat,"orbitalRing").style.display.value);Assert.AreEqual(DisplayStyle.None,Get<Button>(seat,"orbitalSwitch").style.display.value);
            yield return Capture("floating-mouse-cards",captureWidth:1600,captureHeight:1000,captureChecks:()=>Assert.AreEqual(DisplayStyle.None,Get<OrbitalBattleRing>(seat,"orbitalRing").style.display.value));
            nav.ActivateElement(Get<VisualElement>(seat,"orbitalCards").Q<Button>("orbital-card-3"));yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.Abilities.Count==2,"mouse choice applied");Assert.False(Get<bool>(seat,"orbitalOpen"));Assert.False((bool)Call(seat,"OpenOrbital",false),"mouse cannot open a wheel when no choice is pending");CollectionAssert.AreEqual(selected,Selection(seat));
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
    }
}
