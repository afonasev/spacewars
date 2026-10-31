using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Input;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
namespace Spacewars.Input.Tests
{
    public sealed class TeamMapSignalInputTests:InputTestFixture
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        GameObject host;PlayableBootstrap hud;PlayableInput input;Keyboard keyboard;Mouse mouse;Gamepad pad;PlayableRuntime runtime;
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,F).SetValue(hud,value);
        T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,F).GetValue(hud);
        object Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(hud,args);
        [SetUp]public override void Setup()
        {
            base.Setup();keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();pad=InputSystem.AddDevice<Gamepad>();
            host=new GameObject("team signal fixture");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;Set("profile",PlayableProfile.Default);Call("CreateHud");Call("CreateWorld");
            input=host.AddComponent<PlayableInput>();Set("input",input);input.SourceSeatSemantics=true;input.AssignedKeyboard=keyboard;input.AssignedMouse=mouse;Call("BindKeyboardCommands");
            runtime=new PlayableRuntime(PlayableProfile.Default,1,7,false,false,true);Set("runtime",runtime);Set("generation",1L);Set("view",runtime.Latest);Set("inLobby",false);Set("preparing",false);Set("localInputFocused",true);
            input.MapAt=p=>(int)Call("MapAt",p);input.IsPointerOverUi=p=>false;input.IsKeyboardInUi=()=>false;
        }
        [TearDown]public override void TearDown(){runtime?.RequestStop();UnityEngine.Object.DestroyImmediate(host);base.TearDown();}
        IEnumerator Ready(){for(int i=0;i<60&&runtime.Latest.Tick<1;i++)yield return null;Assert.Greater(runtime.Latest.Tick,0);Set("view",runtime.Latest);yield return null;}
        void MouseEdge(Vector2 at,bool down){InputSystem.QueueStateEvent(mouse,new MouseState{position=at,buttons=(ushort)(down?1:0)});InputSystem.Update();input.Poll();}
        void PadFrame(GamepadState state){InputSystem.QueueStateEvent(pad,state);InputSystem.Update();Call("PollOrdinaryPad");}
        [UnityTest]public IEnumerator AltClickAndControllerWheelPreserveSelectionAndNeverIssueUnitOrders()
        {
            yield return Ready();var camera=Get<Camera>("cameraView");camera.orthographicSize=20;camera.transform.position=new Vector3(0,30,-25);camera.transform.LookAt(Vector3.zero);
            var at=(Vector2)camera.WorldToScreenPoint(new Vector3(-4,0,0));var selection=Get<HashSet<int>>("selection");int own=runtime.Latest.Entities.First(e=>e.Owner==runtime.Latest.Owner).Id;selection.Add(own);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftAlt));InputSystem.Update();MouseEdge(at,true);Assert.True(Get<bool>("signalWheelOpen"));MouseEdge(at,false);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();input.Poll();Assert.True(Get<bool>("signalWheelOpen"),"Alt release is not confirmation");
            var centre=Get<Vector2>("signalWheelCentre");Vector2 target=new Vector2(centre.x,Screen.height-(centre.y-90));MouseEdge(target,true);MouseEdge(target,false);Assert.False(Get<bool>("signalWheelOpen"));
            for(int i=0;i<60&&runtime.Latest.TeamSignals.Count==0;i++)yield return null;
            Assert.AreEqual(1,runtime.Latest.TeamSignals.Count);Assert.AreEqual(TeamSignalKind.Attack,runtime.Latest.TeamSignals[0].Kind);CollectionAssert.AreEqual(new[]{own},selection);Assert.Null(runtime.Latest.Entities.Single(e=>e.Id==own).CurrentOrder);
            Set("view",runtime.Latest);Set("padCursorInitialized",true);Set("battleCursor",at);PadFrame(new GamepadState());PadFrame(new GamepadState().WithButton(GamepadButton.East));PadFrame(new GamepadState().WithButton(GamepadButton.East).WithButton(GamepadButton.RightStick));Assert.True(Get<bool>("signalWheelOpen"));
            PadFrame(new GamepadState());PadFrame(new GamepadState{leftStick=new Vector2(-1,-1)});Assert.AreEqual(2,Get<TeamSignalWheel>("signalWheel").Selected);
            PadFrame(new GamepadState{leftStick=new Vector2(-1,-1)}.WithButton(GamepadButton.South));Assert.False(Get<bool>("signalWheelOpen"));
            for(int i=0;i<60&&runtime.Latest.TeamSignals.Count<2;i++)yield return null;
            Assert.True(runtime.Latest.TeamSignals.Any(s=>s.Kind==TeamSignalKind.Defend));CollectionAssert.AreEqual(new[]{own},selection);Assert.Null(runtime.Latest.Entities.Single(e=>e.Id==own).CurrentOrder);
            PadFrame(new GamepadState());PadFrame(new GamepadState().WithButton(GamepadButton.RightStick));Assert.True(Get<bool>("signalWheelOpen"));PadFrame(new GamepadState().WithButton(GamepadButton.East));Assert.False(Get<bool>("signalWheelOpen"));Assert.AreEqual(2,runtime.Latest.TeamSignals.Count);
        }
        [UnityTest]public IEnumerator ThreadedPrivateFramesKeepSignalsOutOfSpectatorAndUnionMap()
        {
            runtime.RequestStop();for(int i=0;i<60&&!runtime.IsStopped;i++)yield return null;Assert.True(runtime.IsStopped);
            var p=PlayableProfile.Default;
            var starts=new[]{new OfflineStart("one",1,new NavPoint(-24,-24),new NavPoint(-18,-24),pin:1),new OfflineStart("two",2,new NavPoint(24,24),new NavPoint(18,24),pin:2),new OfflineStart("three",3,new NavPoint(-24,24),new NavPoint(-18,24),pin:3)};
            var roster=new[]{new OfflineParticipant("alpha",1,3,OfflineControl.Human),new OfflineParticipant("beta",2,7,OfflineControl.Human),new OfflineParticipant("gamma",3,3,OfflineControl.Human)};
            var c=new OfflineMatchConfiguration(p,"signal-fanout-test","signal-flat","signal-fixture",7,roster,starts,starts.Select(s=>new TerritorySite(s.SiteId,PlayableBuildingKind.Headquarters,s.Position,Array.Empty<TerritorySlot>())).ToArray(),Array.Empty<NavObstacle>(),new double[,]{{0,1,1},{1,0,1},{1,1,0}},new[]{"viewer"});
            runtime=new PlayableRuntime(c,71);Set("runtime",runtime);yield return Ready();
            Assert.True(runtime.TrySubmit(new PlayableCommand(71,1,"alpha",PlayableCommandKind.SignalRecon,null,default)).Accepted);
            for(int i=0;i<60&&runtime.OfflineFrame.Views["gamma"].TeamSignals.Count==0;i++)yield return null;
            var frame=runtime.OfflineFrame;Assert.AreEqual(1,frame.Views["alpha"].TeamSignals.Count);Assert.AreEqual(1,frame.Views["gamma"].TeamSignals.Count);Assert.Zero(frame.Views["beta"].TeamSignals.Count);
            var spectator=runtime.SpectatorFrame("viewer");Assert.NotNull(spectator);Assert.Zero(spectator.Overview.TeamSignals.Count);Assert.True(spectator.Players.All(v=>v.TeamSignals.Count==0));
            var map=Get<PlayableMapSurface>("compactMap");map.SetTeamSignals(frame.Views["alpha"].TeamSignals,frame.Tick);map.SetPublic(Array.Empty<PlayableMapMark>(),p.ArenaHalfExtent);
            var privateMarks=(IReadOnlyList<PlayableTeamSignal>)typeof(PlayableMapSurface).GetField("teamSignals",F).GetValue(map);Assert.Zero(privateMarks.Count,"Union projection clears the private signal layer");
        }
        [UnityTest]public IEnumerator WheelLabelsFitSectorsAndNeutralCentreMasksUnderlyingDefense()
        {
            var root=Get<VisualElement>("root");root.Clear();root.style.width=1280;root.style.height=560;root.style.backgroundColor=new Color(.035f,.06f,.08f);
            var panel=host.GetComponent<UIDocument>().panelSettings;panel.scale=1;
            var texture=new RenderTexture(1280,560,24);texture.Create();panel.targetTexture=texture;
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="teamSignals.wheelPixels");
            var sizes=new[]{(float)field.Minimum,(float)PlayableProfile.Default.TeamSignalWheelPixels,(float)field.Maximum};
            var centres=new[]{new Vector2(205,280),new Vector2(610,280),new Vector2(1015,280)};
            var underlay=new VisualElement{pickingMode=PickingMode.Ignore};underlay.style.position=Position.Absolute;underlay.style.left=underlay.style.right=underlay.style.top=underlay.style.bottom=0;
            underlay.generateVisualContent+=ctx=>{for(int i=0;i<sizes.Length;i++)TeamSignalArtwork.Glyph(ctx.painter2D,centres[i],TeamSignalKind.Defend,sizes[i]*.25f,Color.cyan);};root.Add(underlay);
            var wheels=new TeamSignalWheel[3];
            for(int i=0;i<wheels.Length;i++){
                var label=new Label(sizes[i]+" px"){pickingMode=PickingMode.Ignore};label.style.position=Position.Absolute;label.style.left=centres[i].x-70;label.style.top=35;label.style.width=140;label.style.unityTextAlign=TextAnchor.MiddleCenter;root.Add(label);
                var wheel=new TeamSignalWheel();wheel.style.width=wheel.style.height=sizes[i];wheel.style.left=centres[i].x-sizes[i]/2;wheel.style.top=centres[i].y-sizes[i]/2;wheel.Choose(new Vector2(0,-1),.1f);root.Add(wheel);wheels[i]=wheel;
            }
            Texture2D pixels=null;
            try{
                for(int i=0;i<5;i++)yield return null;
                foreach(var wheel in wheels){
                    float size=wheel.contentRect.width;var centre=new Vector2(size/2,size/2);var labels=wheel.Query<Label>().ToList();Assert.AreEqual(3,labels.Count);
                    foreach(var label in labels){
                        var measured=label.MeasureTextSize(label.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined);Assert.LessOrEqual(measured.x,label.contentRect.width+.5f,label.text+" must not overflow horizontally");Assert.LessOrEqual(measured.y,label.contentRect.height+.5f,label.text+" must not overflow vertically");
                        var box=label.worldBound;foreach(var corner in new[]{box.min,box.max,new Vector2(box.xMin,box.yMax),new Vector2(box.xMax,box.yMin)}){
                            var local=wheel.WorldToLocal(corner)-centre;Assert.LessOrEqual(local.magnitude,size*.49f,label.text+" must fit inside the outer arc");Assert.GreaterOrEqual(local.magnitude,size*.20f,label.text+" must not cover the centre");
                        }
                    }
                }
                var previous=RenderTexture.active;RenderTexture.active=texture;pixels=new Texture2D(1280,560,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1280,560),0,0);pixels.Apply();RenderTexture.active=previous;
                for(int i=0;i<centres.Length;i++){
                    var centre=centres[i];Assert.Greater(pixels.GetPixel((int)centre.x,560-(int)centre.y).r,.35f,"Neutral radio dot must render");int radius=(int)(sizes[i]*.12f);
                    for(int y=-radius;y<=radius;y++)for(int x=-radius;x<=radius;x++){var color=pixels.GetPixel((int)centre.x+x,560-(int)centre.y+y);Assert.Less(color.g-color.r,.25f,"Defense glyph must not show through neutral centre");}
                }
                var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/team-wheel-fix/screens"));Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"wheel-sizes-radio-centre.png"),pixels.EncodeToPNG());
            }finally{if(pixels)UnityEngine.Object.DestroyImmediate(pixels);panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
        }
        [UnityTest]public IEnumerator FieldMinimapAndTacticalWheelRenderActualNativeArtwork()
        {
            yield return Ready();
            var root=Get<VisualElement>("root");int w=Screen.width,h=Screen.height;Assert.GreaterOrEqual(w,640);Assert.GreaterOrEqual(h,480);
            var panel=host.GetComponent<UIDocument>().panelSettings;var ui=new RenderTexture(w,h,24);var background=new RenderTexture(w,h,24);ui.Create();background.Create();panel.targetTexture=ui;panel.scale=1;root.style.width=w;root.style.height=h;
            var camera=Get<Camera>("cameraView");camera.targetTexture=background;camera.orthographicSize=19;camera.transform.position=new Vector3(-10,35,-32);camera.transform.LookAt(new Vector3(-10,0,0));
            var scene=new VisualElement{pickingMode=PickingMode.Ignore};scene.style.position=Position.Absolute;scene.style.left=scene.style.right=scene.style.top=scene.style.bottom=0;scene.style.backgroundImage=new StyleBackground(Background.FromRenderTexture(background));root.Insert(0,scene);
            var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/team-map-signals/screens"));Directory.CreateDirectory(folder);
            void Capture(string name){camera.Render();var prior=RenderTexture.active;RenderTexture.active=ui;var pixels=new Texture2D(w,h,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,w,h),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),pixels.EncodeToPNG());RenderTexture.active=prior;UnityEngine.Object.DestroyImmediate(pixels);}
            try{
                long sequence=0;foreach(var row in new[]{(PlayableCommandKind.SignalAttack,new NavPoint(-15,1)),(PlayableCommandKind.SignalDefend,new NavPoint(-6,4)),(PlayableCommandKind.SignalRecon,new NavPoint(-2,-6))})Assert.True(runtime.TrySubmit(new PlayableCommand(1,++sequence,"player-1",row.Item1,null,row.Item2)).Accepted);
                for(int i=0;i<60&&runtime.Latest.TeamSignals.Count<3;i++)yield return null;Assert.AreEqual(3,runtime.Latest.TeamSignals.Count);runtime.RequestPause(true);for(int i=0;i<60&&!runtime.Latest.Paused;i++)yield return null;
                Set("view",runtime.Latest);Set("sequence",sequence);Call("Render");Call("UpdateMaps");Call("UpdateTeamSignalPresentation");camera.Render();for(int i=0;i<4;i++)yield return null;Capture("signals-field");
                var at=(Vector2)camera.WorldToScreenPoint(new Vector3(-10,0,0));Assert.True((bool)Call("OpenTeamSignal",at,false));Get<TeamSignalWheel>("signalWheel").Choose(new Vector2(0,-90),1);for(int i=0;i<3;i++)yield return null;Capture("wheel-field");Call("CancelTeamSignal");
                Call("ToggleMap");for(int i=0;i<4;i++)yield return null;Call("UpdateMaps");Call("UpdateTeamSignalPresentation");for(int i=0;i<3;i++)yield return null;Capture("signals-map");
                var map=Get<PlayableMapSurface>("tacticalMap");var centre=map.worldBound.center;Assert.True((bool)Call("OpenTeamSignal",new Vector2(centre.x,Screen.height-centre.y),false));Get<TeamSignalWheel>("signalWheel").Choose(new Vector2(-1,1),.1f);for(int i=0;i<3;i++)yield return null;Capture("wheel-map");Call("CancelTeamSignal");Assert.True(Get<bool>("mapOpen"));
            }finally{panel.targetTexture=null;camera.targetTexture=null;ui.Release();background.Release();UnityEngine.Object.DestroyImmediate(ui);UnityEngine.Object.DestroyImmediate(background);}
        }
    }
}
