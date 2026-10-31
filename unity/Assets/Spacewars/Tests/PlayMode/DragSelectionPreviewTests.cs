using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Input;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed class DragSelectionPreviewTests : InputTestFixture
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        GameObject host;PlayableBootstrap hud;PlayableInput input;PlayableWorld world;Camera camera;
        Keyboard keyboard;Mouse mouse;PanelSettings panel;RenderTexture sceneTexture,uiTexture;
        Color oldAmbient;
        T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Hidden).GetValue(hud);
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Hidden).SetValue(hud,value);
        object Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethods(Hidden).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(hud,args);
        HashSet<int> Selection=>Get<HashSet<int>>("selection");
        VisualElement Root=>Get<VisualElement>("root");
        static PlayableEntitySnapshot Unit(int id,PlayableEntityKind kind,double x,double z,PlayableOwner owner=PlayableOwner.Player,int hp=100)=>new PlayableEntitySnapshot(id,owner,kind,new NavPoint(x,z),hp,false,0,0,0);
        PlayableSnapshot Snapshot(double secondX=1)=>new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,
            new[]{Unit(1,PlayableEntityKind.Tank,-3,0),Unit(2,PlayableEntityKind.Explorer,secondX,2),Unit(3,PlayableEntityKind.Shkval,6,-1),Unit(4,PlayableEntityKind.Tank,-1,1,PlayableOwner.Enemy),Unit(5,PlayableEntityKind.Explorer,0,-1,PlayableOwner.Player,0)},
            new[]{new PlayableBuildingSnapshot(10,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(-8,8),100,1,0,0,default)},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,population:new PlayablePopulationSnapshot(3,0,100),activeProfile:PlayableProfile.Default);
        Vector2 Screen(double x,double z)=>camera.WorldToScreenPoint(new Vector3((float)x,0,(float)z));
        (Vector2 a,Vector2 b) Box()
        {
            var points=new[]{Screen(-4,-2),Screen(2,3)};
            return (new Vector2(Mathf.Min(points[0].x,points[1].x),Mathf.Min(points[0].y,points[1].y)),new Vector2(Mathf.Max(points[0].x,points[1].x),Mathf.Max(points[0].y,points[1].y)));
        }
        void MouseAt(Vector2 p,bool down){InputSystem.QueueStateEvent(mouse,new MouseState{position=p,buttons=(ushort)(down?1:0)});InputSystem.Update();input.Poll();}
        void Keys(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.Update();input.Poll();}
        void Render(){Call("Render");world.Fog.ShowPublic();camera.Render();}
        void Drag(Vector2 a,Vector2 b){MouseAt(a,true);MouseAt(b,true);Render();}
        void AssertRings(params int[] ids)
        {
            CollectionAssert.AreEquivalent(ids,world.Actors.Where(a=>a.Value.Selection.activeSelf).Select(a=>a.Key));
        }
        [SetUp] public override void Setup()
        {
            base.Setup();keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();keyboard.MakeCurrent();mouse.MakeCurrent();
            host=new GameObject("Native drag-selection inspection");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
            Set("profile",PlayableProfile.Default);Call("CreateHud");
            panel=host.GetComponent<UIDocument>().panelSettings;panel.scaleMode=PanelScaleMode.ConstantPixelSize;panel.scale=1;
            sceneTexture=new RenderTexture(1600,1000,24);sceneTexture.Create();uiTexture=new RenderTexture(1600,1000,24);uiTexture.Create();panel.targetTexture=uiTexture;
            // Test capture adapter: camera and panel both use this offscreen resolution.
            panel.SetScreenToPanelSpaceFunction(p=>new Vector2(p.x,p.y+1000-UnityEngine.Screen.height));
            // Keep the root fitted to the native panel: ConfigurePanel owns UI scale.
            world=new PlayableWorld(host.transform,PlayableProfile.Default);Set("world",world);
            camera=new GameObject("Gameplay camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.orthographicSize=(float)PlayableProfile.Default.CameraOrthoSize;
            camera.transform.position=new Vector3(0,26,-37);camera.transform.LookAt(Vector3.zero);camera.nearClipPlane=.1f;camera.farClipPlane=700;camera.targetTexture=sceneTexture;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.1f,.13f);Set("cameraView",camera);
            var background=new Image{image=sceneTexture,scaleMode=ScaleMode.StretchToFill,pickingMode=PickingMode.Ignore};background.style.position=Position.Absolute;background.style.left=background.style.right=background.style.top=background.style.bottom=0;Root.Insert(0,background);
            input=host.AddComponent<PlayableInput>();input.AssignedKeyboard=keyboard;input.AssignedMouse=mouse;input.SetFocus(true);input.SelectionDragPixels=(float)PlayableProfile.Default.SelectionDragPixels;Set("input",input);Call("BindLocalKeyboardInput");
            input.MapAt=_=>0;input.IsPointerOverUi=_=>false;input.IsKeyboardInUi=()=>false;input.FocusLost=()=>{};input.TogglePause=()=>{};input.CancelContext=()=>false;
            Set("view",Snapshot());oldAmbient=RenderSettings.ambientLight;RenderSettings.ambientLight=new Color(.65f,.65f,.66f);
            var light=new GameObject("Scene light").AddComponent<Light>();light.transform.SetParent(host.transform);light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(50,-30,0);
            Render();
        }
        [TearDown] public override void TearDown()
        {
            RenderSettings.ambientLight=oldAmbient;panel.targetTexture=null;camera.targetTexture=null;
            UnityEngine.Object.DestroyImmediate(host);sceneTexture.Release();uiTexture.Release();UnityEngine.Object.DestroyImmediate(sceneTexture);UnityEngine.Object.DestroyImmediate(uiTexture);base.TearDown();
        }
        IEnumerator Capture(string name)
        {
            for(int i=0;i<4;i++){Render();yield return null;}
            Assert.That(Root.worldBound.width*panel.scale,Is.EqualTo(1600).Within(1),"Capture must not stretch the world behind the frame");
            Assert.That(Root.worldBound.height*panel.scale,Is.EqualTo(1000).Within(1));
            if(Get<bool>("dragSelectionActive"))
            {
                var box=Root.Q("drag-selection-box").worldBound;var a=Get<Vector2>("dragSelectionFrom");var b=Get<Vector2>("dragSelectionTo");
                Assert.That(box.xMin*panel.scale,Is.EqualTo(Mathf.Min(a.x,b.x)).Within(1));
                Assert.That(box.yMin*panel.scale,Is.EqualTo(1000-Mathf.Max(a.y,b.y)).Within(1));
            }
            string output=Environment.GetEnvironmentVariable("SPACEWARS_DRAG_OUTPUT");if(string.IsNullOrEmpty(output))yield break;
            Directory.CreateDirectory(output);var before=RenderTexture.active;var pixels=new Texture2D(1600,1000,TextureFormat.RGBA32,false);
            try{RenderTexture.active=uiTexture;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());}
            finally{RenderTexture.active=before;UnityEngine.Object.DestroyImmediate(pixels);}
        }
        [UnityTest] public IEnumerator RealMouseDragShowsPreviewThenCommitsOnlyOnRelease()
        {
            yield return null;yield return null;
            var (a,b)=Box();Selection.Add(3);Render();AssertRings(3);
            MouseAt(a,true);MouseAt(a+Vector2.one,true);Render();Assert.False(Get<bool>("worldDragPreviewActive"));AssertRings(3);
            MouseAt(b,true);Render();AssertRings(1,2);CollectionAssert.AreEquivalent(new[]{3},Selection);
            Assert.AreEqual(DisplayStyle.Flex,Root.Q("drag-selection-box").style.display.value);
            Assert.AreEqual(PickingMode.Ignore,Root.Q("drag-selection-box").pickingMode);
            Assert.AreEqual(4,Root.Q("drag-selection-box").childCount);
            yield return Capture("drag-preview");
            MouseAt(b,false);Render();CollectionAssert.AreEquivalent(new[]{1,2},Selection);AssertRings(1,2);
            Assert.AreEqual(DisplayStyle.None,Root.Q("drag-selection-box").style.display.value);
            yield return Capture("drag-committed");
        }
        [UnityTest] public IEnumerator ReverseDragShiftAddsOwnUnitsAndCancelRestoresPriorSelection()
        {
            yield return null;yield return null;
            var (a,b)=Box();Selection.Add(3);Selection.Add(10);Keys(Key.LeftShift);Drag(b,a);AssertRings(1,2,3);
            CollectionAssert.AreEquivalent(new[]{3,10},Selection);
            yield return Capture("drag-shift-preview");
            input.ClearMode();Render();AssertRings(3,10);CollectionAssert.AreEquivalent(new[]{3,10},Selection);
            MouseAt(a,false);Keys();Selection.Clear();Selection.Add(3);Drag(a,b);AssertRings(1,2);
            Keys(Key.Escape);Render();AssertRings(3);CollectionAssert.AreEquivalent(new[]{3},Selection);
            Assert.AreEqual(DisplayStyle.None,Root.Q("drag-selection-box").style.display.value);
        }
        [UnityTest] public IEnumerator MovingUnitsAndCameraReevaluatePreviewWithoutCommitting()
        {
            yield return null;yield return null;
            var (a,b)=Box();Drag(a,b);AssertRings(1,2);
            Set("view",Snapshot(7));Render();AssertRings(1);Assert.IsEmpty(Selection);
            camera.transform.position+=camera.transform.right*40;Render();AssertRings();Assert.IsEmpty(Selection);
        }
        [TestCase("map")][TestCase("ui")][TestCase("spectator")][TestCase("placement")][TestCase("focus")][TestCase("blocked")]
        public void RestrictedContextsDoNotLeakWorldPreview(string context)
        {
            var (a,b)=Box();Selection.Add(3);Call("DrawDrag",a,b,true);Render();AssertRings(1,2);
            if(context=="map")input.MapAt=_=>2;else if(context=="ui")input.IsPointerOverUi=_=>true;else if(context=="spectator")Set("spectatorMode",true);else if(context=="placement")input.PlacementInputEnabled=true;else if(context=="focus")input.SetFocus(false);else input.WorldInputEnabled=false;
            Render();Assert.False(Get<bool>("worldDragPreviewActive"));AssertRings(3);CollectionAssert.AreEquivalent(new[]{3},Selection);
        }
        [Test] public void FrameControlsValidateAndAreEditable()
        {
            foreach(var field in PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("presentation.selectionFrame.")))
            {
                Assert.True(NativeBalanceFields.Editable(field));Assert.IsNotEmpty(field.Description);Assert.IsNotEmpty(field.Unit);Assert.Greater(field.Step,0);
                var data=PlayableProfile.Default.CopyData();
                foreach(double bad in new[]{double.NaN,double.PositiveInfinity,field.Minimum-field.Step,field.Maximum+field.Step})
                {Assert.Throws<ArgumentOutOfRangeException>(()=>field.Write(data,bad));typeof(PlayableProfileData).GetField(field.FieldName).SetValue(data,bad);Assert.Throws<ArgumentException>(()=>PlayableProfile.Validate(data));}
            }
        }
    }
}
