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
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed class KeyboardHudCameraTests : InputTestFixture
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        GameObject host;PlayableBootstrap hud;PlayableInput input;Keyboard keyboard;Camera camera;
        T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(hud);
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(hud,value);
        void Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethods(Flags).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(hud,args);
        VisualElement Root=>Get<VisualElement>("root");
        void Keys(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.Update();input.Poll();}
        static void SendKey(VisualElement target,KeyCode key){using(var evt=KeyDownEvent.GetPooled('\0',key,EventModifiers.None)){evt.target=target;target.SendEvent(evt);}}
        [SetUp] public override void Setup()
        {
            base.Setup();keyboard=InputSystem.AddDevice<Keyboard>();keyboard.MakeCurrent();var mouse=InputSystem.AddDevice<Mouse>();mouse.MakeCurrent();
            host=new GameObject("keyboard HUD camera fixture");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;Set("profile",PlayableProfile.Default);Call("CreateHud");Call("CreateSpectatorHud");
            camera=new GameObject("camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.transform.position=new Vector3(0,40,-20);camera.transform.LookAt(Vector3.zero);Set("cameraView",camera);
            input=host.AddComponent<PlayableInput>();input.AssignedKeyboard=keyboard;input.AssignedMouse=mouse;input.SetFocus(true);Set("input",input);Call("BindLocalKeyboardInput");
            input.EdgePanAxis=_=>Vector2.zero;Root.style.width=1280;Root.style.height=720;
        }
        [TearDown] public override void TearDown(){UnityEngine.Object.DestroyImmediate(host);base.TearDown();}
        void Show(bool spectator)
        {
            Set("spectatorMode",spectator);input.CommandInputEnabled=!spectator;
            var players=Enumerable.Range(0,2).Select(i=>new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,30,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,840,null,
                Array.Empty<PlayableEntitySnapshot>(),new[]{new PlayableBuildingSnapshot(i+1,(PlayableOwner)i,PlayableBuildingKind.Headquarters,new NavPoint(i*20,10),1000,1,0,0,default)},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                incomePerSecond:12,population:new PlayablePopulationSnapshot(0,0,100),owner:(PlayableOwner)i,ownerId:"player-"+(i+1))).ToArray();
            Set("view",players[0]);Set("spectatorFrame",Activator.CreateInstance(typeof(PlayableSpectatorFrame),Flags,null,new object[]{players[0],players},null));Call("UpdateHud");
        }
        [UnityTest] public IEnumerator SpectatorTableFocusKeepsArrowsOnCamera()=>CheckHudFocus(true);
        [UnityTest] public IEnumerator PlayerHudFocusKeepsArrowsOnCamera()=>CheckHudFocus(false);
        IEnumerator CheckHudFocus(bool spectator)
        {
            Show(spectator);yield return null;yield return null;
            var button=spectator?Root.Q<Button>("spectator-player-player-1"):Root.Q<Button>("army-capacity");Assert.NotNull(button);button.Focus();yield return null;
            Assert.True(input.IsKeyboardInUi(),"HUD submit focus must still gate world commands");Assert.False(input.IsKeyboardBlockingCamera());
            var keys=new[]{Key.LeftArrow,Key.RightArrow,Key.UpArrow,Key.DownArrow};var codes=new[]{KeyCode.LeftArrow,KeyCode.RightArrow,KeyCode.UpArrow,KeyCode.DownArrow};
            var directions=new[]{NavigationMoveEvent.Direction.Left,NavigationMoveEvent.Direction.Right,NavigationMoveEvent.Direction.Up,NavigationMoveEvent.Direction.Down};
            for(int i=0;i<keys.Length;i++)
            {
                var before=camera.transform.position;Keys(keys[i]);SendKey(button,codes[i]);
                using(var move=NavigationMoveEvent.GetPooled(directions[i])){move.target=button;button.SendEvent(move);}
                yield return null;Assert.AreSame(button,Root.focusController.focusedElement,"Arrows must never walk the HUD/table focus");
                Assert.AreNotEqual(before,camera.transform.position,"A physical keyboard state must move the real presentation camera while HUD is focused");Keys();
            }
            int stops=0;input.Stop=()=>stops++;Keys(Key.S);Keys();Assert.Zero(stops,"Camera ownership must not enable command hotkeys through a focused HUD");
            input.WorldInputEnabled=false;var held=camera.transform.position;Keys(Key.RightArrow);Keys();Assert.AreEqual(held,camera.transform.position);input.WorldInputEnabled=true;
            input.SetFocus(false);Keys(Key.RightArrow);Keys();Assert.AreEqual(held,camera.transform.position);input.SetFocus(true);button.Focus();
            if(spectator){SendKey(button,KeyCode.Return);Assert.AreEqual(PlayableOwner.Player,Get<PlayableOwner?>("spectatorPerspective"),"Explicit activation must still select the player perspective");}
        }
        [UnityTest] public IEnumerator RealMenuAndTextEditingKeepKeyboardOwnership()
        {
            Show(true);var menu=new VisualElement();var a=new Button{text="First"};var b=new Button{text="Second"};menu.Add(a);menu.Add(b);Root.Add(menu);yield return null;
            Set("paused",true);Get<NativeMenuNavigation>("menuNavigation").SetScope(menu,null,a);a.Focus();Assert.True(input.IsKeyboardBlockingCamera());
            var before=camera.transform.position;Keys(Key.DownArrow);Keys();Assert.AreEqual(before,camera.transform.position);SendKey(a,KeyCode.DownArrow);Assert.AreSame(b,Root.focusController.focusedElement,"Actual pause/menu navigation must remain available");
            Get<NativeMenuNavigation>("menuNavigation").SetScope(null);Set("paused",false);menu.RemoveFromHierarchy();var field=new TextField{value="editing"};Root.Add(field);yield return null;field.Focus();
            Assert.True(input.IsKeyboardBlockingCamera(),"Text entry (including its inner input element) keeps caret arrows");Keys(Key.LeftArrow);Keys();Assert.AreEqual(before,camera.transform.position);
        }
    }
}
