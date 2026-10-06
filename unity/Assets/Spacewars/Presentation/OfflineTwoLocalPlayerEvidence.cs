#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace Spacewars.Presentation
{
    public sealed partial class OfflineTwoLocalBootstrap
    {
        // Explicit synthetic device route through the real Player input and render
        // path. This cannot substitute for two physical devices/human acceptance.
        private IEnumerator SyntheticPlayerEvidence()
        {
            Directory.CreateDirectory(syntheticEvidence);Screen.SetResolution(1920,1080,false);
            Application.logMessageReceived+=(condition,stack,type)=>{if(type==LogType.Exception||type==LogType.Error){File.WriteAllText(Path.Combine(syntheticEvidence,"player-failure.txt"),condition+"\n"+stack);Application.Quit(2);}};
            assignedKeyboard=InputSystem.AddDevice<Keyboard>();assignedMouse=InputSystem.AddDevice<Mouse>();assignedPad=InputSystem.AddDevice<Gamepad>();
            assignedKeyboard.MakeCurrent();assignedMouse.MakeCurrent();assignedPad.MakeCurrent();TryBind();
            for(int i=0;i<4;i++)yield return null;
            Resume();for(int i=0;i<4;i++)yield return null;
            var first=views[0].View.Entities.Single(e=>e.Owner==views[0].View.Owner);var second=views[1].View.Entities.Single(e=>e.Owner==views[1].View.Owner);
            var screen=Spacewars.Input.OfflinePadCamera.ProjectScreen(views[0].Camera,views[0].World.Point(first.Position));
            InputSystem.QueueStateEvent(assignedMouse,new MouseState{position=new Vector2(screen.x,screen.y)}.WithButton(MouseButton.Left));
            views[1].Cursor=second.Position;InputSystem.QueueStateEvent(assignedPad,new GamepadState().WithButton(GamepadButton.South));
            yield return null;yield return null;
            InputSystem.QueueStateEvent(assignedMouse,new MouseState{position=new Vector2(screen.x,screen.y)});
            InputSystem.QueueStateEvent(assignedPad,new GamepadState());
            yield return null;yield return null;
            if(!views[0].Selection.Contains(first.Id)||!views[1].Selection.Contains(second.Id))throw new InvalidOperationException("Synthetic devices did not select own source-fixture units.");
            // Both devices press stop in the same input frame. Source pad stop is
            // emitted on short release; keyboard stop emits on down.
            InputSystem.QueueStateEvent(assignedKeyboard,new KeyboardState(Key.S));InputSystem.QueueStateEvent(assignedPad,new GamepadState().WithButton(GamepadButton.West));
            yield return null;yield return null;InputSystem.QueueStateEvent(assignedKeyboard,new KeyboardState());InputSystem.QueueStateEvent(assignedPad,new GamepadState());
            // Render cadence can exceed the 30 Hz authority cadence. Wait for actual
            // semantic receipts rather than assuming a count of rendered frames.
            double deadline=Time.realtimeSinceStartupAsDouble+5;
            while(Time.realtimeSinceStartupAsDouble<deadline&&!(session.Frame.Receipts.Any(r=>r.Receipt.OwnerId=="owner-11"&&r.Receipt.Sequence==1&&r.Receipt.Status==PlayableCommandStatus.Applied)&&session.Frame.Receipts.Any(r=>r.Receipt.OwnerId=="owner-28"&&r.Receipt.Sequence==1&&r.Receipt.Status==PlayableCommandStatus.Applied)))yield return null;
            if(!session.Frame.Receipts.Any(r=>r.Receipt.OwnerId=="owner-11"&&r.Receipt.Sequence==1&&r.Receipt.Status==PlayableCommandStatus.Applied)||!session.Frame.Receipts.Any(r=>r.Receipt.OwnerId=="owner-28"&&r.Receipt.Sequence==1&&r.Receipt.Status==PlayableCommandStatus.Applied))throw new InvalidOperationException("Missing both owner receipts.");
            if(FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c=>c.enabled)!=2||views.Any(v=>v.View.Tick!=session.Frame.Tick||v.View.Generation!=session.Frame.Generation))throw new InvalidOperationException("Incoherent Player camera/frame fanout.");
            yield return CaptureSynthetic("two-owner-world-shared-map");
            yield return MirrorSyntheticEvidence();
            Focus(0,first.Position);Focus(1,first.Position);for(int i=0;i<4;i++)yield return null;
            yield return CaptureSynthetic("same-area-owner-fog");
            InputSystem.QueueStateEvent(assignedKeyboard,new KeyboardState(Key.Tab));InputSystem.QueueStateEvent(assignedPad,new GamepadState().WithButton(GamepadButton.Select));yield return null;yield return null;
            InputSystem.QueueStateEvent(assignedKeyboard,new KeyboardState());InputSystem.QueueStateEvent(assignedPad,new GamepadState());for(int i=0;i<4;i++)yield return null;
            yield return CaptureSynthetic("personal-maps");
            // Close the map, then expose real source gesture rings with synthetic
            // assigned-device samples. These are rendering checks, not hardware acceptance.
            InputSystem.QueueStateEvent(assignedPad,new GamepadState().WithButton(GamepadButton.Select));yield return null;yield return null;InputSystem.QueueStateEvent(assignedPad,new GamepadState());for(int i=0;i<4;i++)yield return null;
            InputSystem.QueueStateEvent(assignedPad,new GamepadState().WithButton(GamepadButton.RightShoulder));yield return new WaitForSecondsRealtime(padProfile.holdMs/1000f+.15f);
            if(gestures.Mode!="groupWheel")throw new InvalidOperationException("Group ring gesture failed.");yield return CaptureSynthetic("gamepad-group-ring");
            InputSystem.QueueStateEvent(assignedPad,new GamepadState());for(int i=0;i<4;i++)yield return null;
            InputSystem.QueueStateEvent(assignedPad,new GamepadState().WithButton(GamepadButton.LeftShoulder));yield return new WaitForSecondsRealtime(padProfile.holdMs/1000f+.15f);
            if(gestures.Mode!="baseWheel")throw new InvalidOperationException("Base ring gesture failed.");yield return CaptureSynthetic("gamepad-base-ring");
            InputSystem.QueueStateEvent(assignedPad,new GamepadState());for(int i=0;i<4;i++)yield return null;

            if(!views[1].View.IsHostile(first.Owner)){
                var follow=session.Submit("local-gamepad",PlayableCommandKind.Follow,new[]{second.Id},targetId:first.Id);if(!follow.Accepted)throw new InvalidOperationException("Allied Follow admission failed.");
                double followDeadline=Time.realtimeSinceStartupAsDouble+5;while(Time.realtimeSinceStartupAsDouble<followDeadline&&views[1].View.Entities.FirstOrDefault(e=>e.Id==second.Id)?.CurrentOrder?.Kind!=PlayableTacticalOrderKind.Follow)yield return null;
                if(views[1].View.Entities.FirstOrDefault(e=>e.Id==second.Id)?.CurrentOrder?.TargetId!=first.Id)throw new InvalidOperationException("Allied persistent Follow authority missing.");yield return CaptureSynthetic("allied-persistent-follow");
            }
            long ordinal=session.Frame.ReceiptOrdinal;InputSystem.QueueStateEvent(assignedPad,new GamepadState().WithButton(GamepadButton.South));yield return null;InputSystem.RemoveDevice(assignedPad);for(int i=0;i<3;i++)yield return null;
            if(!session.Paused||session.WaitingSeat!="local-gamepad")throw new InvalidOperationException("Device disconnect did not pause shared match for expected owner.");
            InputSystem.AddDevice(assignedPad);InputSystem.QueueStateEvent(assignedPad,new GamepadState().WithButton(GamepadButton.South));for(int i=0;i<10;i++)yield return null;
            if(!session.Paused||session.Frame.ReceiptOrdinal!=ordinal)throw new InvalidOperationException("Held reconnect emitted accidental command.");
            yield return CaptureSynthetic("held-reconnect-paused");
            InputSystem.QueueStateEvent(assignedPad,new GamepadState());for(int i=0;i<3;i++)yield return null;
            // Exercise the actual noncombat menu route and its consumed press.
            InputSystem.QueueStateEvent(assignedPad,new GamepadState().WithButton(GamepadButton.Start));for(int i=0;i<3;i++)yield return null;
            if(!session.Paused||session.Frame.ReceiptOrdinal!=ordinal)throw new InvalidOperationException("Menu confirmation must wait for neutral and emit no world command.");
            InputSystem.QueueStateEvent(assignedPad,new GamepadState());double resumeDeadline=Time.realtimeSinceStartupAsDouble+5;while(session.Paused&&Time.realtimeSinceStartupAsDouble<resumeDeadline)yield return null;for(int i=0;i<3;i++)yield return null;
            if(session.Paused||session.Frame.ReceiptOrdinal!=ordinal)throw new InvalidOperationException("Neutral return failed or emitted command.");
            File.WriteAllText(Path.Combine(syntheticEvidence,"player-verdict.json"),$"{{\"result\":\"passed\",\"syntheticDevices\":true,\"physicalAcceptance\":false,\"worldRuntimes\":1,\"cameraCount\":{views.Count(v=>v.Camera!=null&&v.Camera.enabled)},\"generation\":{session.Frame.Generation},\"tick\":{session.Frame.Tick},\"receiptOrdinal\":{session.Frame.ReceiptOrdinal},\"seed\":19092026,\"profile\":\"{profile.ProfileId}@{profile.Revision}\"}}");
            Debug.Log("TWO_LOCAL_SYNTHETIC_PLAYER_PASS");Application.Quit();
        }
        private IEnumerator CaptureSynthetic(string name)
        {
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(syntheticEvidence,name+".png"));
            File.WriteAllText(Path.Combine(syntheticEvidence,name+".json"),$"{{\"syntheticDevices\":true,\"generation\":{session.Frame.Generation},\"tick\":{session.Frame.Tick},\"sequence\":{session.Frame.Sequence},\"paused\":{session.Paused.ToString().ToLowerInvariant()},\"seed\":19092026,\"owners\":[\"owner-11\",\"owner-28\"]}}");
            for(int i=0;i<3;i++)yield return null;
        }
    }
}
#endif
