using NUnit.Framework;
using Spacewars.Input;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Spacewars.Input.Tests
{
    public sealed class PlayableInputTests : InputTestFixture
    {
        private GameObject host; private PlayableInput input; private Keyboard keyboard; private Mouse mouse;
        [SetUp] public override void Setup(){base.Setup();host=new GameObject("PlayableInputTests");input=host.AddComponent<PlayableInput>();keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();keyboard.MakeCurrent();mouse.MakeCurrent();input.SetFocus(true);}
        [TearDown] public override void TearDown(){Object.DestroyImmediate(host);base.TearDown();}

        [Test] public void ArrowCameraScrollNormalizesDiagonalsAndRespectsUiPauseAndFocus()
        {
            int calls=0;Vector2 axis=Vector2.zero;input.Pan=value=>{calls++;axis=value;};
            KeyboardState(Key.RightArrow,Key.UpArrow);Assert.AreEqual(1,calls);Assert.AreEqual(Vector2.one.normalized,axis);
            KeyboardState();input.IsKeyboardInUi=()=>true;KeyboardState(Key.LeftArrow);Assert.AreEqual(1,calls);
            KeyboardState();input.IsKeyboardInUi=()=>false;input.WorldInputEnabled=false;KeyboardState(Key.DownArrow);Assert.AreEqual(1,calls);
            KeyboardState();input.WorldInputEnabled=true;input.SetFocus(false);KeyboardState(Key.UpArrow);Assert.AreEqual(1,calls);
            input.SetFocus(true);KeyboardState();KeyboardState(Key.LeftArrow);Assert.AreEqual(2,calls);Assert.AreEqual(Vector2.left,axis);
        }
        [Test] public void MouseEdgeScrollUsesViewportAndBlocksHudMapMenusAndFocusLoss()
        {
            int calls=0;Vector2 axis=Vector2.zero;input.EdgePan=value=>{calls++;axis=value;};
            input.EdgePanAxis=p=>AngularCommandCursor.EdgePan(p,new Rect(0,0,100,80),20);
            MouseState(new Vector2(10,40),0);Assert.AreEqual(1,calls);Assert.AreEqual(new Vector2(-.5f,0),axis);
            MouseState(new Vector2(50,40),0);MouseState(new Vector2(-1,40),0);Assert.AreEqual(1,calls);
            input.IsPointerOverUi=_=>true;MouseState(new Vector2(10,40),0);Assert.AreEqual(1,calls);
            input.IsPointerOverUi=_=>false;input.MapAt=_=>1;MouseState(new Vector2(10,40),0);Assert.AreEqual(1,calls);
            input.MapAt=_=>0;input.IsKeyboardInUi=()=>true;MouseState(new Vector2(10,40),0);Assert.AreEqual(1,calls);
            input.IsKeyboardInUi=()=>false;input.WorldInputEnabled=false;MouseState(new Vector2(10,40),0);Assert.AreEqual(1,calls);
            input.WorldInputEnabled=true;input.CanReadWorld=()=>false;MouseState(new Vector2(10,40),0);Assert.AreEqual(1,calls);
            input.CanReadWorld=()=>true;input.SetFocus(false);MouseState(new Vector2(10,40),0);Assert.AreEqual(1,calls);
            input.SetFocus(true);MouseState(new Vector2(90,40),0);Assert.AreEqual(2,calls);Assert.AreEqual(new Vector2(.5f,0),axis);
        }

        [Test] public void HoldAndStopAreDistinctAndRespectUiPauseAndFocus()
        {
            int hold=0,stop=0;input.Hold=()=>hold++;input.Stop=()=>stop++;
            KeyboardState(Key.H);KeyboardState();KeyboardState(Key.S);KeyboardState();Assert.AreEqual(1,hold);Assert.AreEqual(1,stop);
            input.IsKeyboardInUi=()=>true;KeyboardState(Key.H);KeyboardState();Assert.AreEqual(1,hold);
            input.IsKeyboardInUi=()=>false;input.WorldInputEnabled=false;KeyboardState(Key.H);KeyboardState();Assert.AreEqual(1,hold);
            input.WorldInputEnabled=true;input.SetFocus(false);KeyboardState(Key.H);KeyboardState();Assert.AreEqual(1,hold);
            input.SetFocus(true);KeyboardState(Key.H);Assert.AreEqual(2,hold);
        }

        [Test] public void EscapeClosesContextBeforePausingAndKeepsAttackCancelPriority()
        {
            int cancelled=0,paused=0;bool context=true;input.CancelContext=()=>{if(!context)return false;context=false;cancelled++;return true;};input.TogglePause=()=>paused++;
            KeyboardState(Key.A);KeyboardState();KeyboardState(Key.Escape);KeyboardState();Assert.AreEqual(0,cancelled);Assert.AreEqual(0,paused);
            KeyboardState(Key.Escape);KeyboardState();Assert.AreEqual(1,cancelled);Assert.AreEqual(0,paused);
            KeyboardState(Key.Escape);KeyboardState();Assert.AreEqual(1,paused);
        }

        [Test] public void UiOriginDragCannotSelectWorldEvenWhenReleasedOverWorld()
        {
            int selected=0,dragged=0; input.Select=(a,b,shift)=>selected++;input.Drag=(a,b,active)=>{if(active)dragged++;};input.IsPointerOverUi=p=>p.x<100;
            MouseState(new Vector2(40,40),1); MouseState(new Vector2(240,180),1); MouseState(new Vector2(240,180),0);
            Assert.AreEqual(0,selected);Assert.AreEqual(0,dragged);
        }

        [Test] public void WorldDragPreservesShiftAdditiveIntent()
        {
            Vector2 from=default(Vector2),to=default(Vector2);bool additive=false; input.Select=(a,b,shift)=>{from=a;to=b;additive=shift;};
            KeyboardState(Key.LeftShift);MouseState(new Vector2(120,80),1);MouseState(new Vector2(250,190),1);MouseState(new Vector2(250,190),0);KeyboardState();
            Assert.AreEqual(new Vector2(120,80),from);Assert.AreEqual(new Vector2(250,190),to);Assert.IsTrue(additive);
        }

        [Test] public void KeyboardUiFocusAndDisabledWorldSuppressStopButNotUiCommands()
        {
            int stop=0,pause=0,restart=0;input.Stop=()=>stop++;input.TogglePause=()=>pause++;input.Restart=()=>restart++;input.IsKeyboardInUi=()=>true;
            KeyboardState(Key.S,Key.Escape,Key.R);Assert.AreEqual(0,stop);Assert.AreEqual(0,pause);Assert.AreEqual(0,restart);KeyboardState();
            input.IsKeyboardInUi=()=>false;input.WorldInputEnabled=false;KeyboardState(Key.S,Key.Escape,Key.R);
            Assert.AreEqual(0,stop);Assert.AreEqual(1,pause);Assert.AreEqual(0,restart,"R is not a web gameplay restart binding.");
        }

        [Test] public void BatchedDragRetainsOriginalPressPosition()
        {
            Vector2 from=default(Vector2),to=default(Vector2);input.Select=(a,b,shift)=>{from=a;to=b;};
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(120,80),buttons=1});
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(250,190),buttons=1});
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(250,190),buttons=0});
            InputSystem.Update();input.Poll();
            Assert.AreEqual(new Vector2(120,80),from);Assert.AreEqual(new Vector2(250,190),to);
        }

        [Test] public void MapInputWinsOverUiAndNeverDuplicatesWorldOrders()
        {
            int world=0,map=0,selected=0;input.Order=(p,a,append)=>world++;input.Select=(a,b,s)=>world++;
            input.IsPointerOverUi=p=>true;input.MapAt=p=>p.x<100?1:0;input.MapOrder=(k,p,a,append)=>map++;input.MapSelect=(k,a,b,s)=>selected++;
            MouseState(new Vector2(40,40),1);MouseState(new Vector2(40,40),0);
            MouseState(new Vector2(40,40),2);MouseState(new Vector2(40,40),0);
            Assert.AreEqual(1,selected);Assert.AreEqual(1,map);Assert.Zero(world);
            KeyboardState(Key.A);KeyboardState();MouseState(new Vector2(40,40),2);MouseState(new Vector2(40,40),0);
            Assert.AreEqual(1,map,"A+RMB cancels, with no movement order.");
            KeyboardState(Key.A);KeyboardState();MouseState(new Vector2(40,40),1);MouseState(new Vector2(40,40),0);
            Assert.AreEqual(2,map);Assert.AreEqual(1,selected);Assert.Zero(world);
        }
        [Test] public void TacticalBackdropDragExitPauseAndFocusCannotLeakGestures()
        {
            int selected=0,world=0,toggle=0,closed=0;bool shift=false;
            input.MapAt=p=>p.x<100?2:3;input.MapSelect=(k,a,b,s)=>{selected++;shift=s;};input.Select=(a,b,s)=>world++;input.Order=(p,a,append)=>world++;
            input.ToggleMap=()=>toggle++;input.CloseMap=()=>closed++;
            KeyboardState(Key.Tab);KeyboardState(Key.LeftShift);MouseState(new Vector2(20,20),1);MouseState(new Vector2(80,80),0);Assert.True(shift);Assert.AreEqual(1,selected);
            MouseState(new Vector2(20,20),1);MouseState(new Vector2(150,80),0);Assert.AreEqual(1,selected);
            MouseState(new Vector2(20,20),1);input.WorldInputEnabled=false;MouseState(new Vector2(80,80),0);Assert.AreEqual(1,selected);
            input.WorldInputEnabled=true;MouseState(new Vector2(20,20),1);input.SetFocus(false);input.SetFocus(true);MouseState(new Vector2(80,80),0);Assert.AreEqual(1,selected);
            Assert.Zero(world);Assert.AreEqual(1,toggle);Assert.Greater(closed,0);
        }

        [Test] public void ObserverKeepsCameraMapAndTacticalNavigationWithoutCommands()
        {
            int selected=0,ordered=0,mapFocused=0,toggled=0,panned=0;
            input.CommandInputEnabled=false;input.Select=(a,b,s)=>selected++;input.Order=(p,a,s)=>ordered++;input.MapAt=p=>p.x<100?1:0;input.MapSelect=(k,a,b,s)=>mapFocused++;input.ToggleMap=()=>toggled++;input.Pan=_=>panned++;
            KeyboardState(Key.RightArrow);KeyboardState();KeyboardState(Key.Tab);KeyboardState();
            MouseState(new Vector2(180,80),1);MouseState(new Vector2(180,80),0);
            MouseState(new Vector2(40,80),1);MouseState(new Vector2(40,80),0);
            Assert.Zero(selected);Assert.Zero(ordered);Assert.AreEqual(1,mapFocused);Assert.AreEqual(1,toggled);Assert.Greater(panned,0);
        }

        [Test] public void MixedButtonBatchPreservesPositionsAndDispatchOrder()
        {
            var calls = new System.Collections.Generic.List<string>();
            input.Order=(p,a,append)=>calls.Add("order:"+p.x);
            input.Select=(a,b,s)=>calls.Add("select:"+a.x+":"+b.x);
            QueueMouse(120,2); QueueMouse(120,0);
            QueueMouse(240,1); QueueMouse(250,0);
            QueueMouse(360,2); QueueMouse(360,0);
            InputSystem.Update(); input.Poll(); input.Poll();
            CollectionAssert.AreEqual(new[]{"order:120","select:240:250","order:360"},calls);
        }
        [Test] public void BatchRoutesEachEdgeAgainstItsOwnMapAndUiPosition()
        {
            var calls = new System.Collections.Generic.List<string>();
            input.MapAt=p=>p.x<100?1:0; input.IsPointerOverUi=p=>p.x>=300;
            input.MapOrder=(k,p,a,append)=>calls.Add("map:"+p.x);
            input.Order=(p,a,append)=>calls.Add("world:"+p.x);
            QueueMouse(40,2); QueueMouse(40,0);
            QueueMouse(200,2); QueueMouse(200,0);
            QueueMouse(350,2); QueueMouse(350,0);
            InputSystem.Update(); input.Poll();
            CollectionAssert.AreEqual(new[]{"map:40","world:200"},calls);
        }
        [Test] public void MultipleClicksAndReleasePositionSurviveLaterMovement()
        {
            var ends = new System.Collections.Generic.List<float>();
            input.Select=(a,b,s)=>ends.Add(b.x);
            QueueMouse(120,1); QueueMouse(140,0);
            QueueMouse(220,1); QueueMouse(240,0); QueueMouse(500,0);
            InputSystem.Update(); input.Poll();
            CollectionAssert.AreEqual(new[]{140f,240f},ends);
        }
        [Test] public void QueuedEdgesAreDiscardedOnFocusLossAndDisable()
        {
            int orders=0; input.Order=(p,a,append)=>orders++;
            QueueMouse(120,2); QueueMouse(120,0); InputSystem.Update();
            input.SetFocus(false); input.SetFocus(true); input.Poll();
            QueueMouse(220,2); QueueMouse(220,0); InputSystem.Update();
            input.enabled=false; input.enabled=true; input.Poll();
            QueueMouse(320,2); QueueMouse(320,0); InputSystem.Update();
            input.ClearMode(); input.Poll();
            Assert.Zero(orders);
        }
        [Test] public void BatchedShiftReleaseDoesNotChangeEarlierSelection()
        {
            bool additive=false; input.Select=(a,b,s)=>additive=s;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftShift));
            QueueMouse(120,1); QueueMouse(140,0);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            InputSystem.Update(); input.Poll();
            Assert.True(additive);
        }
        [Test] public void DeltaPositionEventsKeepThePositionAtTheEdge()
        {
            Vector2 target=default; input.Order=(p,a,append)=>target=p;
            InputSystem.QueueDeltaStateEvent(mouse.position,new Vector2(120,80));
            QueueMouse(120,2);
            InputSystem.QueueDeltaStateEvent(mouse.position,new Vector2(400,200));
            InputSystem.Update(); input.Poll();
            Assert.AreEqual(new Vector2(120,80),target);
        }
        [Test] public void AssignedRemovedDevicesCannotBeReadAndLiveDeviceGateBlocksBeforeDispatch()
        {
            input.AssignedMouse=mouse;input.AssignedKeyboard=keyboard;int commands=0;input.Order=(p,a,append)=>commands++;
            input.CanReadWorld=()=>false;MouseState(new Vector2(120,80),2);Assert.AreEqual(0,commands);
            InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);Assert.DoesNotThrow(()=>input.Poll());
        }
        [Test] public void SourceSeatPersistentAttackModeCancelsRmbWithoutOrderAndCtrlAddsSelection()
        {
            input.SourceSeatSemantics=true;int commands=0;bool attack=false,add=false;input.Order=(p,a,append)=>{commands++;attack=a;};input.Select=(a,b,s)=>add=s;
            KeyboardState(Key.A);KeyboardState();KeyboardState(Key.A);KeyboardState();MouseState(new Vector2(120,80),1);MouseState(new Vector2(120,80),0);Assert.AreEqual(1,commands);Assert.True(attack);
            KeyboardState(Key.A);KeyboardState();MouseState(new Vector2(120,80),2);MouseState(new Vector2(120,80),0);Assert.AreEqual(1,commands);
            KeyboardState(Key.LeftCtrl);MouseState(new Vector2(120,80),1);MouseState(new Vector2(140,80),0);Assert.True(add);
        }
        [TestCase(0)][TestCase(1)][TestCase(2)] public void MomentaryShiftIsCapturedAtEachWorldOrMapOrderEdge(int map)
        {
            var calls=new System.Collections.Generic.List<string>();input.MapAt=p=>map;
            input.Order=(p,a,append)=>calls.Add("world:"+a+":"+append);
            input.MapOrder=(k,p,a,append)=>calls.Add("map:"+a+":"+append);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftShift));QueueMouse(120,2);QueueMouse(120,0);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            InputSystem.Update();input.Poll();input.Poll();
            CollectionAssert.AreEqual(new[]{(map==0?"world":"map")+":False:True"},calls);
        }
        [TestCase(0)][TestCase(1)][TestCase(2)] public void BatchedKeyboardAttackMoveUsesClickModifierAndOneOrder(int map)
        {
            int count=0;bool mode=false,append=false;input.MapAt=p=>map;
            input.Order=(p,a,s)=>{count++;mode=a;append=s;};input.MapOrder=(k,p,a,s)=>{count++;mode=a;append=s;};
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A));
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftShift));QueueMouse(120,1);QueueMouse(120,0);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            InputSystem.Update();input.Poll();input.Poll();Assert.AreEqual(1,count);Assert.True(mode);Assert.True(append);
        }
        [Test] public void ControlSelectionModifierDoesNotAppendOrdersAndSeatKeyboardIsIsolated()
        {
            input.SourceSeatSemantics=true;input.AssignedKeyboard=keyboard;input.AssignedMouse=mouse;
            var foreign=InputSystem.AddDevice<Keyboard>();bool append=true;int count=0;input.Order=(p,a,s)=>{count++;append=s;};
            InputSystem.QueueStateEvent(foreign,new KeyboardState(Key.LeftShift));
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftCtrl));QueueMouse(120,2);
            InputSystem.Update();input.Poll();Assert.AreEqual(1,count);Assert.False(append);
        }
        private void QueueMouse(float x,ushort buttons)
        { InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(x,80),buttons=buttons}); }

        [Test] public void OrdinaryAttackModeStaysArmedOnSecondAAndRmbOnlyCancels()
        {
            int orders=0;input.Order=(p,a,s)=>orders++;
            KeyboardState(Key.A);KeyboardState();KeyboardState(Key.A);KeyboardState();Assert.True(input.AttackMode);
            MouseState(new Vector2(120,80),2);MouseState(new Vector2(120,80),0);
            Assert.False(input.AttackMode);Assert.Zero(orders);
        }
        [Test] public void BatchedSelectionKeysRetainModifiersAndInterleaveWithMouse()
        {
            var calls=new System.Collections.Generic.List<string>();
            input.SelectionKey=(i,a,t)=>calls.Add("key:"+i+":"+a);input.Select=(a,b,s)=>calls.Add("click:"+s);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftCtrl,Key.Digit3));
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftCtrl));QueueMouse(120,1);QueueMouse(120,0);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Numpad3));
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F2));
            InputSystem.Update();input.Poll();input.Poll();
            CollectionAssert.AreEqual(new[]{"key:3:True","click:True","key:3:False","key:0:False"},calls);
        }
        [TestCase(Key.Digit1,1)][TestCase(Key.Digit9,9)][TestCase(Key.Numpad1,1)][TestCase(Key.Numpad9,9)]
        public void DigitsAndNumpadUseTheSameNineSlots(Key key,int expected)
        {int index=-1;input.SelectionKey=(i,a,t)=>index=i;KeyboardState(key);Assert.AreEqual(expected,index);}
        [TestCase(Key.F2)][TestCase(Key.Digit0)][TestCase(Key.Numpad0)] public void WholeArmyAliasesAndUiOwnership(Key key)
        {
            int calls=0;input.SelectionKey=(i,a,t)=>{Assert.Zero(i);calls++;};KeyboardState(key);KeyboardState();Assert.AreEqual(1,calls);
            input.IsKeyboardInUi=()=>true;KeyboardState(key);KeyboardState();Assert.AreEqual(1,calls);
            input.IsKeyboardInUi=()=>false;KeyboardState(Key.LeftShift,key);KeyboardState();Assert.AreEqual(1,calls);
        }
        [Test] public void DoubleClickTypeGestureUsesEventTimeAndDoesNotFollowDragOrPause()
        {
            int selected=0,types=0;input.Select=(a,b,s)=>selected++;input.SelectSameType=p=>types++;
            MouseState(new Vector2(120,80),1);MouseState(new Vector2(120,80),0);MouseState(new Vector2(120,80),1);MouseState(new Vector2(120,80),0);
            Assert.AreEqual(1,selected);Assert.AreEqual(1,types);
            MouseState(new Vector2(120,80),1);MouseState(new Vector2(250,80),0);MouseState(new Vector2(250,80),1);MouseState(new Vector2(250,80),0);
            Assert.AreEqual(1,types);input.ClearMode();MouseState(new Vector2(250,80),1);MouseState(new Vector2(250,80),0);Assert.AreEqual(1,types);
        }
        [Test] public void MissingAttackSelectionKeepsModeAndHomeRespectsFieldFocus()
        {
            int orders=0,home=0;input.CanIssueAttack=()=>false;input.Order=(p,a,s)=>orders++;input.ResetCamera=()=>home++;
            KeyboardState(Key.A);KeyboardState();MouseState(new Vector2(120,80),1);MouseState(new Vector2(120,80),0);Assert.True(input.AttackMode);Assert.Zero(orders);
            KeyboardState(Key.Home);KeyboardState();Assert.AreEqual(1,home);input.IsKeyboardInUi=()=>true;KeyboardState(Key.Home);Assert.AreEqual(1,home);
        }
        [Test] public void RecallThenStopAndClickThenHoldKeepDeviceEventOrder()
        {
            var calls=new System.Collections.Generic.List<string>();input.SelectionKey=(i,a,t)=>calls.Add("group:"+i);input.Stop=()=>calls.Add("stop");input.Hold=()=>calls.Add("hold");input.Select=(a,b,s)=>calls.Add("select");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Digit2));InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.S));
            QueueMouse(120,1);QueueMouse(120,0);InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.H));InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            InputSystem.Update();input.Poll();CollectionAssert.AreEqual(new[]{"group:2","stop","select","hold"},calls);
        }
        [Test] public void BatchedAttackThenEscapeCancelsWithoutPauseOrLosingLaterClick()
        {
            int pause=0,selected=0;input.TogglePause=()=>pause++;input.Select=(a,b,s)=>selected++;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A));InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));
            QueueMouse(120,1);QueueMouse(120,0);InputSystem.Update();input.Poll();Assert.Zero(pause);Assert.False(input.AttackMode);Assert.AreEqual(1,selected);
        }
        [Test] public void ContextOrMapClickBreaksWorldDoubleClickGesture()
        {
            int types=0;input.SelectSameType=p=>types++;
            MouseState(new Vector2(120,80),1);MouseState(new Vector2(120,80),0);MouseState(new Vector2(120,80),2);MouseState(new Vector2(120,80),0);
            MouseState(new Vector2(120,80),1);MouseState(new Vector2(120,80),0);Assert.Zero(types);
            input.MapAt=p=>p.x<100?1:0;MouseState(new Vector2(40,80),1);MouseState(new Vector2(40,80),0);
            MouseState(new Vector2(120,80),1);MouseState(new Vector2(120,80),0);Assert.Zero(types);
        }

        private void MouseState(Vector2 position,ushort buttons){InputSystem.QueueStateEvent(mouse,new MouseState{position=position,buttons=buttons});InputSystem.Update();input.Poll();}
        private void KeyboardState(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.Update();input.Poll();}
    }
}
