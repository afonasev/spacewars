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
