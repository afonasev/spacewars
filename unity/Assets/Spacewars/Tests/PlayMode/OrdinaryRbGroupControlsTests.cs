using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        [UnityTest] public IEnumerator RbAssignXAddRecallAndRtIdleStayLocal()
        {
            yield return StartOrdinary(2,false,true);
            var seat=Seats[0];var other=Seats[1];var pad=pads[0];var v=Get<PlayableSnapshot>(seat,"view");
            var ids=v.Entities.Where(e=>e.Owner==v.Owner).Select(e=>e.Id).Take(1).ToArray();Assert.AreEqual(1,ids.Length);
            var groups=Get<KeyboardControlGroups>(seat,"keyboardGroups");var entities=(KeyboardSelectable[])Call(seat,"KeyboardEntities");
            var otherBefore=Selection(other).ToArray();long sequence=Get<long>(seat,"sequence");
            Selection(seat).Clear();Selection(seat).Add(ids[0]);Call(app,"UpdateFrame");
            ButtonFrame(pad,pad.rightShoulder,true);Assert.AreEqual("groupWheel",Get<OfflinePadGestures>(seat,"battleGestures").Mode);
            Set(pad.leftStick,Vector2.up);InputSystem.Update();Call(app,"UpdateFrame");yield return new WaitForSecondsRealtime(.35f);Call(app,"UpdateFrame");
            Assert.IsEmpty(groups.Members(1,entities),"hold prepares assignment, release confirms");ButtonFrame(pad,pad.rightShoulder,false);CollectionAssert.AreEqual(new[]{ids[0]},groups.Members(1,entities));Set(pad.leftStick,Vector2.zero);InputSystem.Update();Call(app,"UpdateFrame");
            Selection(seat).Clear();Selection(seat).Add(ids[0]);Call(app,"UpdateFrame");ButtonFrame(pad,pad.rightShoulder,true);Set(pad.leftStick,Vector2.up);InputSystem.Update();Call(app,"UpdateFrame");
            yield return Capture("rb-group-actions",captureWidth:1600,captureHeight:1000,captureChecks:()=>
            {
                var ring=Get<OrbitalBattleRing>(seat,"padAuxiliaryRing");Inside(Get<VisualElement>(seat,"root").worldBound,ring.Q<RadialActionCard>().worldBound,"group action card");
                Assert.True(ring.Query<Label>().ToList().Any(l=>l.text.Contains("Добавить")));Assert.False(ring.Query<Label>().ToList().Any(l=>l.text.Contains("RT")));
            });
            Tap(pad,pad.buttonWest);CollectionAssert.AreEquivalent(ids,groups.Members(1,entities));CollectionAssert.AreEqual(new[]{ids[0]},Selection(seat));
            yield return Capture("rb-group-added",captureWidth:1600,captureHeight:1000,captureChecks:()=>
            {
                var ring=Get<OrbitalBattleRing>(seat,"padAuxiliaryRing");var root=Get<VisualElement>(seat,"root");Inside(root.worldBound,ring.Q<RadialActionCard>().worldBound,"group card");
                Assert.True(ring.Query<Label>().ToList().Any(l=>l.text.Contains("Сохранено")));Assert.False(ring.Query<Label>().ToList().Any(l=>l.text.Contains("RT")));
            });
            ButtonFrame(pad,pad.rightShoulder,false);Set(pad.leftStick,Vector2.zero);InputSystem.Update();Call(app,"UpdateFrame");CollectionAssert.AreEqual(new[]{ids[0]},Selection(seat));
            Selection(seat).Clear();Call(app,"UpdateFrame");ButtonFrame(pad,pad.rightShoulder,true);Set(pad.leftStick,Vector2.up);InputSystem.Update();Call(app,"UpdateFrame");ButtonFrame(pad,pad.rightShoulder,false);Set(pad.leftStick,Vector2.zero);InputSystem.Update();Call(app,"UpdateFrame");CollectionAssert.AreEquivalent(ids,Selection(seat));
            ButtonFrame(pad,pad.rightTrigger,true);yield return new WaitForSecondsRealtime(.35f);Call(app,"UpdateFrame");ButtonFrame(pad,pad.rightTrigger,false);Assert.AreEqual("world",Get<OfflinePadGestures>(seat,"battleGestures").Mode);
            Assert.AreEqual(sequence,Get<long>(seat,"sequence"),"group buttons/X/RT cannot issue simulation commands");CollectionAssert.AreEquivalent(otherBefore,Selection(other));Assert.IsEmpty(Get<KeyboardControlGroups>(other,"keyboardGroups").Members(1,(KeyboardSelectable[])Call(other,"KeyboardEntities")));
        }
        [UnityTest] public IEnumerator AHeldTwoSecondsShowsOwnViewportRectangleAndSelectsOnce()
        {
            yield return StartOrdinary(2,false,true);var seat=Seats[1];var other=Seats[0];var pad=pads[1];var before=Selection(other).ToArray();
            // Use the pad-only seat: the synthetic mouse starts at the keyboard seat edge
            // and otherwise pans that camera during the real two-second hold.
            var own=Get<PlayableSnapshot>(seat,"view").Entities.First(e=>e.Owner==Get<PlayableSnapshot>(seat,"view").Owner);
            var camera=Get<Camera>(seat,"cameraView");var point=Get<PlayableWorld>(seat,"world").Point(own.Position);camera.transform.position=point-camera.transform.forward*((camera.transform.position.y-point.y)/-camera.transform.forward.y);Call(app,"UpdateFrame");
            Assert.True(OfflinePadSelection.InViewport(camera,point),"controlled own unit must be visible before A");
            ButtonFrame(pad,pad.buttonSouth,true);yield return new WaitForSecondsRealtime(.5f);Call(app,"UpdateFrame");var preview=Get<VisualElement>(seat,"padAreaPreview");Assert.Greater(preview.style.borderTopLeftRadius.value.value,0);
            yield return new WaitForSecondsRealtime(1.55f);Call(app,"UpdateFrame");Assert.True(Get<OfflinePadGestures>(seat,"battleGestures").ScreenPreview);Assert.IsNotEmpty(Selection(seat),"ground="+Get<NavPoint>(seat,"padCursorGround")+" visible="+string.Join(",",((OfflinePadSelectable[])Call(seat,"OrdinaryPadUnits")).Select(u=>u.Id+":"+u.Own+":"+u.Visible)));
            var expected=OfflinePadSelection.Area((OfflinePadSelectable[])Call(seat,"OrdinaryPadUnits"),0,0,double.PositiveInfinity,false);CollectionAssert.AreEquivalent(expected,Selection(seat));
            yield return Capture("a-full-viewport",captureWidth:1600,captureHeight:1000,captureChecks:()=>
            {
                var root=Get<VisualElement>(seat,"root");Assert.AreEqual(root.contentRect.width,preview.resolvedStyle.width,.5f);Assert.AreEqual(root.contentRect.height,preview.resolvedStyle.height,.5f);Assert.AreEqual(0,preview.resolvedStyle.borderTopLeftRadius);Assert.AreEqual(root.worldBound.xMin,preview.worldBound.xMin,.5f);Assert.AreEqual(root.worldBound.yMin,preview.worldBound.yMin,.5f);
                Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(other,"padAreaPreview").style.display.value);
            });
            Selection(seat).Clear();Call(app,"UpdateFrame");Assert.IsEmpty(Selection(seat),"threshold cannot reselect on later frames");ButtonFrame(pad,pad.buttonSouth,false);Assert.IsEmpty(Selection(seat),"release cannot duplicate screen select");CollectionAssert.AreEquivalent(before,Selection(other));
        }
    }
}
