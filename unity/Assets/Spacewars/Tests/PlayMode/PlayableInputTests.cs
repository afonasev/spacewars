using NUnit.Framework;
using Spacewars.Input;
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

        [Test] public void HoldAndStopAreDistinctAndRespectUiPauseAndFocus()
        {
            int hold=0,stop=0;input.Hold=()=>hold++;input.Stop=()=>stop++;
            KeyboardState(Key.H);KeyboardState();KeyboardState(Key.S);KeyboardState();Assert.AreEqual(1,hold);Assert.AreEqual(1,stop);
            input.IsKeyboardInUi=()=>true;KeyboardState(Key.H);KeyboardState();Assert.AreEqual(1,hold);
            input.IsKeyboardInUi=()=>false;input.WorldInputEnabled=false;KeyboardState(Key.H);KeyboardState();Assert.AreEqual(1,hold);
            input.WorldInputEnabled=true;input.SetFocus(false);KeyboardState(Key.H);KeyboardState();Assert.AreEqual(1,hold);
            input.SetFocus(true);KeyboardState(Key.H);Assert.AreEqual(2,hold);
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
            Assert.AreEqual(0,stop);Assert.AreEqual(1,pause);Assert.AreEqual(1,restart);
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
            int world=0,map=0,selected=0;input.Order=(p,a)=>world++;input.Select=(a,b,s)=>world++;
            input.IsPointerOverUi=p=>true;input.MapAt=p=>p.x<100?1:0;input.MapOrder=(k,p,a)=>map++;input.MapSelect=(k,a,b,s)=>selected++;
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
            input.MapAt=p=>p.x<100?2:3;input.MapSelect=(k,a,b,s)=>{selected++;shift=s;};input.Select=(a,b,s)=>world++;input.Order=(p,a)=>world++;
            input.ToggleMap=()=>toggle++;input.CloseMap=()=>closed++;
            KeyboardState(Key.Tab);KeyboardState(Key.LeftShift);MouseState(new Vector2(20,20),1);MouseState(new Vector2(80,80),0);Assert.True(shift);Assert.AreEqual(1,selected);
            MouseState(new Vector2(20,20),1);MouseState(new Vector2(150,80),0);Assert.AreEqual(1,selected);
            MouseState(new Vector2(20,20),1);input.WorldInputEnabled=false;MouseState(new Vector2(80,80),0);Assert.AreEqual(1,selected);
            input.WorldInputEnabled=true;MouseState(new Vector2(20,20),1);input.SetFocus(false);input.SetFocus(true);MouseState(new Vector2(80,80),0);Assert.AreEqual(1,selected);
            Assert.Zero(world);Assert.AreEqual(1,toggle);Assert.Greater(closed,0);
        }

        [Test] public void MixedButtonBatchPreservesPositionsAndDispatchOrder()
        {
            var calls = new System.Collections.Generic.List<string>();
            input.Order=(p,a)=>calls.Add("order:"+p.x);
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
            input.MapOrder=(k,p,a)=>calls.Add("map:"+p.x);
            input.Order=(p,a)=>calls.Add("world:"+p.x);
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
            int orders=0; input.Order=(p,a)=>orders++;
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
            Vector2 target=default; input.Order=(p,a)=>target=p;
            InputSystem.QueueDeltaStateEvent(mouse.position,new Vector2(120,80));
            QueueMouse(120,2);
            InputSystem.QueueDeltaStateEvent(mouse.position,new Vector2(400,200));
            InputSystem.Update(); input.Poll();
            Assert.AreEqual(new Vector2(120,80),target);
        }
        [Test] public void AssignedRemovedDevicesCannotBeReadAndLiveDeviceGateBlocksBeforeDispatch()
        {
            input.AssignedMouse=mouse;input.AssignedKeyboard=keyboard;int commands=0;input.Order=(p,a)=>commands++;
            input.CanReadWorld=()=>false;MouseState(new Vector2(120,80),2);Assert.AreEqual(0,commands);
            InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);Assert.DoesNotThrow(()=>input.Poll());
        }
        [Test] public void SourceSeatPersistentAttackModeCancelsRmbWithoutOrderAndCtrlAddsSelection()
        {
            input.SourceSeatSemantics=true;int commands=0;bool attack=false,add=false;input.Order=(p,a)=>{commands++;attack=a;};input.Select=(a,b,s)=>add=s;
            KeyboardState(Key.A);KeyboardState();KeyboardState(Key.A);KeyboardState();MouseState(new Vector2(120,80),1);MouseState(new Vector2(120,80),0);Assert.AreEqual(1,commands);Assert.True(attack);
            KeyboardState(Key.A);KeyboardState();MouseState(new Vector2(120,80),2);MouseState(new Vector2(120,80),0);Assert.AreEqual(1,commands);
            KeyboardState(Key.LeftCtrl);MouseState(new Vector2(120,80),1);MouseState(new Vector2(140,80),0);Assert.True(add);
        }
        private void QueueMouse(float x,ushort buttons)
        { InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(x,80),buttons=buttons}); }

        private void MouseState(Vector2 position,ushort buttons){InputSystem.QueueStateEvent(mouse,new MouseState{position=position,buttons=buttons});InputSystem.Update();input.Poll();}
        private void KeyboardState(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.Update();input.Poll();}
    }
}
