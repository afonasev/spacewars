using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Input;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed class GamepadAttackQueueTests : InputTestFixture
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        GameObject host;PlayableBootstrap app;Gamepad[] pads;
        T Get<T>(PlayableBootstrap seat,string name)=>(T)typeof(PlayableBootstrap).GetField(name,F).GetValue(seat);
        void Put(PlayableBootstrap seat,string name,object value)=>typeof(PlayableBootstrap).GetField(name,F).SetValue(seat,value);
        object Call(PlayableBootstrap seat,string name,params object[] args)=>typeof(PlayableBootstrap).GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(seat,args);
        NativeLocalInputProfile Profile=>JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);
        [TearDown] public override void TearDown(){if(host!=null)UnityEngine.Object.DestroyImmediate(host);base.TearDown();}

        [TestCase("world",2,"context")][TestCase("world",256,"attackMove")]
        [TestCase("tacticalMap",2,"context")][TestCase("tacticalMap",256,"attackMove")]
        public void ShortReplacesAndLongAppendsExactlyOnce(string mode,int bit,string kind)
        {
            var p=Profile;var g=new OfflinePadGestures();g.SetMode(mode);g.Step(0,0,true,true,p);
            Assert.IsEmpty(g.Step(1,bit,true,true,p,false));Assert.AreEqual(bit==256,g.AttackPreview);
            var shortOrder=g.StepDetailed(2,0,true,true,p,false).Single();Assert.AreEqual(kind,shortOrder.Kind);Assert.IsFalse(shortOrder.Append);
            g.Step(10,bit,true,true,p,false);Assert.IsEmpty(g.Step(10+p.holdMs,bit,true,true,p,false));Assert.IsTrue(g.QueuePreview);Assert.AreEqual(bit==256,g.AttackPreview);
            Assert.IsEmpty(g.Step(10+p.holdMs*3,bit,true,true,p,false));
            var longOrder=g.StepDetailed(11+p.holdMs*3,0,true,true,p,false).Single();Assert.AreEqual(kind,longOrder.Kind);Assert.IsTrue(longOrder.Append);Assert.IsFalse(g.QueuePreview);Assert.IsFalse(g.AttackPreview);
            Assert.IsEmpty(g.Step(12+p.holdMs*3,0,true,true,p,false));
        }

        [TestCase(2)][TestCase(256)]
        public void InterruptedHoldRequiresNeutralAndOtherSeatStaysIndependent(int bit)
        {
            var p=Profile;var g=new OfflinePadGestures();var other=new OfflinePadGestures();g.Step(0,0,true,true,p);other.Step(0,0,true,true,p);
            g.Step(1,bit,true,true,p);Assert.IsEmpty(g.Step(2+p.holdMs,bit,true,false,p));Assert.IsFalse(g.AttackPreview);Assert.IsFalse(g.QueuePreview);
            Assert.IsEmpty(g.Step(3+p.holdMs,bit,true,true,p));Assert.IsEmpty(g.Step(4+p.holdMs,0,true,true,p));
            Assert.IsEmpty(other.Step(5+p.holdMs,0,true,true,p));other.Step(6+p.holdMs,256,true,true,p);Assert.IsFalse(other.StepDetailed(7+p.holdMs,0,true,true,p).Single().Append);
            g.Step(8+p.holdMs,bit,true,true,p);g.Step(9+p.holdMs,bit,false,true,p);Assert.IsEmpty(g.Step(10+p.holdMs,0,true,true,p));
            g.Step(11+p.holdMs,bit,true,true,p);g.SetMode("buildingWheel");Assert.IsEmpty(g.Step(12+p.holdMs,0,true,true,p));
        }

        [TestCase("buildingWheel")][TestCase("rallyTarget")][TestCase("rallyMap")][TestCase("groupWheel")]
        public void YDoesNotIssueBattleCommandsInOtherModes(string mode)
        {
            var p=Profile;var g=new OfflinePadGestures();g.SetMode(mode);g.Step(0,0,true,true,p);
            Assert.IsEmpty(g.Step(1,256,true,true,p));Assert.IsEmpty(g.Step(1+p.holdMs,256,true,true,p));Assert.IsFalse(g.AttackPreview);Assert.IsFalse(g.QueuePreview);Assert.IsEmpty(g.Step(2+p.holdMs,0,true,true,p));
        }

        IEnumerator StartTwoHumans()
        {
            var keyboard=InputSystem.AddDevice<Keyboard>();var mouse=InputSystem.AddDevice<Mouse>();keyboard.MakeCurrent();mouse.MakeCurrent();pads=new[]{InputSystem.AddDevice<Gamepad>(),InputSystem.AddDevice<Gamepad>()};
            host=new GameObject("B Y queue ordinary adapter");app=host.AddComponent<PlayableBootstrap>();app.enabled=false;Put(app,"profile",PlayableProfile.ThreeCrossingsDefault);Call(app,"CreateWorld");Call(app,"CreateHud");Put(app,"input",host.AddComponent<PlayableInput>());Call(app,"BindLocalKeyboardInput");Call(app,"CreateLobby");Call(app,"ShowLobby");
            Get<VisualElement>(app,"lobbyScreen").Q<DropdownField>("lobby-map-choice").value="Огненный разлом";
            var draft=Get<NativeLobbyConfiguration>(app,"lobbySetup");draft.HasExplicitSeed=true;draft.ExplicitSeed=19092026;
            for(int i=0;i<2;i++){draft.Participants[i].Human=true;draft.Participants[i].DeviceId=i==0?0:pads[i].deviceId;draft.Participants[i].Team=i+1;}
            Call(app,"RebuildRoster");Call(app,"LaunchLobbyMatch");float until=Time.realtimeSinceStartup+15;
            while((Get<PlayableRuntime>(app,"runtime")==null||Get<bool>(app,"preparing"))&&Time.realtimeSinceStartup<until){Call(app,"UpdateFrame");yield return null;}
            Assert.NotNull(Get<PlayableRuntime>(app,"runtime"));Assert.IsFalse(Get<bool>(app,"preparing"));Call(app,"OnApplicationFocus",true);Call(app,"UpdateFrame");yield return null;
        }
        void Button(PlayableBootstrap seat,UnityEngine.InputSystem.Controls.ButtonControl button,bool down)
        {if(down)Press(button);else Release(button);InputSystem.Update();Call(seat,"PollOrdinaryPad");}
        PlayableCommand Submitted(PlayableBootstrap seat,long sequence)
        {
            var markers=Get<PlayableOrderMarkers>(seat,"orderMarkers");var pending=(IDictionary)typeof(PlayableOrderMarkers).GetField("candidates",F).GetValue(markers);Assert.IsTrue(pending.Contains(sequence));
            return (PlayableCommand)pending[sequence].GetType().GetField("Command",BindingFlags.Instance|BindingFlags.Public).GetValue(pending[sequence]);
        }
        [UnityTest] public IEnumerator OrdinaryPadDispatchesMixedChainMapAndReplacementToItsOwner()
        {
            yield return StartTwoHumans();var seats=Get<List<PlayableBootstrap>>(app,"localPresentations");Assert.AreEqual(2,seats.Count);var seat=seats[1];var v=Get<PlayableSnapshot>(seat,"view");var own=v.Entities.First(e=>e.Owner==v.Owner);Get<HashSet<int>>(seat,"selection").Add(own.Id);
            var point=new NavPoint(own.Position.X+4,own.Position.Z+4);Put(seat,"padCursorGround",point);Put(seat,"padCursorInitialized",true);Put(seat,"battleCursor",(Vector2)Get<Camera>(seat,"cameraView").WorldToScreenPoint(Get<PlayableWorld>(seat,"world").Point(point)));Call(seat,"PollOrdinaryPad");
            long other=Get<long>(seats[0],"sequence"),before=Get<long>(seat,"sequence");
            Button(seat,pads[1].buttonEast,true);Button(seat,pads[1].buttonEast,false);var command=Submitted(seat,before+1);Assert.AreEqual(PlayableCommandKind.Move,command.Kind);Assert.AreEqual(PlayableOrderMode.Replace,command.Mode);
            foreach(var button in new[]{pads[1].buttonEast,pads[1].buttonNorth,pads[1].buttonEast})
            {
                before=Get<long>(seat,"sequence");Button(seat,button,true);yield return new WaitForSecondsRealtime(Profile.holdMs/1000f+.05f);Call(seat,"PollOrdinaryPad");Assert.AreEqual(before,Get<long>(seat,"sequence"));Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(seat,"battleCursorVisual").Q<Label>("pad-queue-preview").style.display.value);
                Button(seat,button,false);command=Submitted(seat,before+1);Assert.AreEqual(button==pads[1].buttonNorth?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,command.Kind);Assert.AreEqual(PlayableOrderMode.Append,command.Mode);Assert.AreEqual(v.OwnerId,command.PlayerId);
            }
            before=Get<long>(seat,"sequence");Button(seat,pads[1].buttonNorth,true);Button(seat,pads[1].buttonNorth,false);command=Submitted(seat,before+1);Assert.AreEqual(PlayableCommandKind.AttackMove,command.Kind);Assert.AreEqual(PlayableOrderMode.Replace,command.Mode);
            Button(seat,pads[1].selectButton,true);Button(seat,pads[1].selectButton,false);Assert.IsTrue(Get<bool>(seat,"mapOpen"));Put(seat,"padCursorGround",point);
            before=Get<long>(seat,"sequence");Button(seat,pads[1].buttonNorth,true);yield return new WaitForSecondsRealtime(Profile.holdMs/1000f+.05f);Button(seat,pads[1].buttonNorth,false);command=Submitted(seat,before+1);Assert.AreEqual(PlayableCommandKind.AttackMove,command.Kind);Assert.AreEqual(PlayableOrderMode.Append,command.Mode);Assert.AreEqual(point,command.Target);Assert.IsTrue(Get<bool>(seat,"mapOpen"));Assert.AreEqual(other,Get<long>(seats[0],"sequence"));
            Get<PlayableRuntime>(app,"runtime").RequestStop();
        }

        [UnityTest] public IEnumerator FinishedControlsDiagramsKeepApprovedHelpAndSettingsSizes()
        {
            host=new GameObject("controls diagram preview");app=host.AddComponent<PlayableBootstrap>();app.enabled=false;Put(app,"profile",PlayableProfile.Default);Call(app,"CreateHud");
            yield return NativeControlsHelpImageScenario.RenderAndNavigate(host,Get<VisualElement>(app,"root"));
        }
    }
}
