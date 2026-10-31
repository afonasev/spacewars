using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    public sealed class MatchNotificationInputTests:InputTestFixture
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        GameObject host;PlayableBootstrap hud;PlayableInput input;Keyboard keyboard;Mouse mouse;Gamepad pad;PlayableRuntime runtime;
        T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,F).GetValue(hud);
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,F).SetValue(hud,value);
        object Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(hud,args);
        Camera Camera=>Get<Camera>("cameraView");NativeMatchNotifications Notes=>Get<NativeMatchNotifications>("matchNotifications");
        [SetUp]public override void Setup()
        {
            base.Setup();keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();pad=InputSystem.AddDevice<Gamepad>();
            host=new GameObject("match event focus fixture");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;Set("profile",PlayableProfile.Default);Call("CreateHud");Call("CreateWorld");
            input=host.AddComponent<PlayableInput>();Set("input",input);input.SourceSeatSemantics=true;input.AssignedKeyboard=keyboard;input.AssignedMouse=mouse;Call("BindKeyboardCommands");
            runtime=new PlayableRuntime(PlayableProfile.Default,1,7,false,false,true);Set("runtime",runtime);Set("generation",1L);Set("view",runtime.Latest);Set("inLobby",false);Set("preparing",false);Set("localInputFocused",true);
            input.IsKeyboardInUi=()=>false;input.IsTextEntryActive=()=>Get<bool>("chatOpen");input.EdgePanAxis=_=>Vector2.zero;
            Camera.orthographicSize=(float)((PlayableProfile.Default.CameraMinZoom+PlayableProfile.Default.CameraMaxZoom)/2);Camera.transform.position=new Vector3(0,35,-28);Camera.transform.LookAt(Vector3.zero);
        }
        [TearDown]public override void TearDown(){runtime?.RequestStop();UnityEngine.Object.DestroyImmediate(host);base.TearDown();}
        IEnumerator Ready(){for(int i=0;i<60&&runtime.Latest.Tick<1;i++)yield return null;Assert.Greater(runtime.Latest.Tick,0);Set("view",runtime.Latest);yield return null;Call("UpdateMatchNotifications");}
        void Keys(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.Update();input.Poll();}
        void Pad(GamepadState state){InputSystem.QueueStateEvent(pad,state);InputSystem.Update();Call("PollOrdinaryPad");}
        [UnityTest]public IEnumerator KeyboardAndPadNavigateSameHistoryWithoutChangingSelectionOrZoomAndRespectModals()
        {
            yield return Ready();var selected=Get<HashSet<int>>("selection");int own=runtime.Latest.Entities.First(e=>e.Owner==PlayableOwner.Player).Id;selected.Add(own);
            var view=Get<PlayableSnapshot>("view");Set("view",view.WithMatchEvents(new[]{new PlayableMatchEvent(900,view.Tick,view.Owner,MatchEventKind.BuildingCompleted,new NavPoint(-12,0)),new PlayableMatchEvent(901,view.Tick,view.Owner,MatchEventKind.UnitReady,new NavPoint(12,0))}));Call("UpdateMatchNotifications");
            float zoom=Camera.orthographicSize;Keys(Key.Space);var latest=Camera.transform.position;Keys();
            Pad(new GamepadState());Pad(new GamepadState().WithButton(GamepadButton.DpadLeft));var previous=Camera.transform.position;Assert.Less(previous.x,latest.x-20);
            Pad(new GamepadState());Pad(new GamepadState().WithButton(GamepadButton.DpadRight));Assert.Less(Vector3.Distance(latest,Camera.transform.position),.02f);
            CollectionAssert.AreEqual(new[]{own},selected);Assert.AreEqual(zoom,Camera.orthographicSize);
            Set("chatOpen",true);Keys(Key.Space);Assert.Less(Vector3.Distance(latest,Camera.transform.position),.02f);Keys();Set("chatOpen",false);
            var gestures=Get<OfflinePadGestures>("battleGestures");gestures.SetMode("baseWheel");Pad(new GamepadState());Pad(new GamepadState().WithButton(GamepadButton.DpadLeft));Assert.Less(Vector3.Distance(latest,Camera.transform.position),.02f);Assert.AreEqual("baseWheel",gestures.Mode);
            Set("paused",true);Call("FocusMatchNotification",true);Assert.Less(Vector3.Distance(latest,Camera.transform.position),.02f);
        }
        [UnityTest]public IEnumerator LatestSingleOutlinedLineRendersOverActualNativeFieldAndExpiresAtThreeSeconds()
        {
            yield return Ready();var view=Get<PlayableSnapshot>("view");Set("view",view.WithMatchEvents(new[]{new PlayableMatchEvent(900,view.Tick,view.Owner,MatchEventKind.BuildingAttacked,new NavPoint(-12,0))}));
            Call("Render");Call("UpdateMaps");Call("UpdateMatchNotifications");var label=Get<Label>("matchNotification");Assert.AreEqual("Здание атаковано",label.text);Assert.AreEqual(1,Get<VisualElement>("root").Query<Label>("match-notification").ToList().Count);
            var root=Get<VisualElement>("root");int w=Screen.width,h=Screen.height;var panel=host.GetComponent<UIDocument>().panelSettings;
            var ui=new RenderTexture(w,h,24);var background=new RenderTexture(w,h,24);ui.Create();background.Create();panel.targetTexture=ui;panel.scale=1;root.style.width=w;root.style.height=h;Camera.targetTexture=background;
            var scene=new VisualElement{pickingMode=PickingMode.Ignore};scene.style.position=Position.Absolute;scene.style.left=scene.style.right=scene.style.top=scene.style.bottom=0;scene.style.backgroundImage=new StyleBackground(Background.FromRenderTexture(background));root.Insert(0,scene);
            try{
                Camera.Render();for(int i=0;i<5;i++)yield return null;
                Assert.AreEqual(0,label.resolvedStyle.backgroundColor.a);Assert.AreEqual(0,label.resolvedStyle.borderTopWidth);
                var minimap=Get<PlayableMapSurface>("compactMap").parent;
                Assert.GreaterOrEqual(label.worldBound.xMin,minimap.worldBound.xMax);
                Assert.AreEqual(minimap.worldBound.yMax-minimap.resolvedStyle.borderBottomWidth,label.worldBound.yMax,.5f);
                float originalWidth=minimap.style.width.value.value;var originalMargin=minimap.style.marginBottom;minimap.style.width=originalWidth+40;minimap.style.marginBottom=20;
                for(int i=0;i<3;i++)yield return null;
                Assert.GreaterOrEqual(label.worldBound.xMin,minimap.worldBound.xMax,"Notification follows resized minimap frame");
                Assert.AreEqual(minimap.worldBound.yMax-minimap.resolvedStyle.borderBottomWidth,label.worldBound.yMax,.5f,"Notification follows the minimap lower border when moved");
                minimap.style.width=originalWidth;minimap.style.marginBottom=originalMargin;for(int i=0;i<3;i++)yield return null;
                var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/match-event-focus/screens"));Directory.CreateDirectory(folder);
                var prior=RenderTexture.active;RenderTexture.active=ui;var pixels=new Texture2D(w,h,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,w,h),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,"notification-field.png"),pixels.EncodeToPNG());RenderTexture.active=prior;UnityEngine.Object.DestroyImmediate(pixels);
                Assert.Null(Notes.Visible(Notes.Latest.ShownAt+3,PlayableProfile.Default));Assert.NotNull(Notes.Focus(false,Notes.Latest.ShownAt+4,PlayableProfile.Default));
            }finally{panel.targetTexture=null;Camera.targetTexture=null;ui.Release();background.Release();UnityEngine.Object.DestroyImmediate(ui);UnityEngine.Object.DestroyImmediate(background);}
        }
    }
}
