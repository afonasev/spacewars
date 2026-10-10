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
using Cursor = UnityEngine.Cursor;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests : InputTestFixture
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private GameObject host;private PlayableBootstrap app;private Gamepad[] pads;
        private T Get<T>(PlayableBootstrap seat,string name)=>(T)typeof(PlayableBootstrap).GetField(name,F).GetValue(seat);
        private void Put(PlayableBootstrap seat,string name,object value)=>typeof(PlayableBootstrap).GetField(name,F).SetValue(seat,value);
        private object Call(PlayableBootstrap seat,string name,params object[] args)=>typeof(PlayableBootstrap).GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(seat,args);
        private PlayableBootstrap[] Seats=>Get<List<PlayableBootstrap>>(app,"localPresentations").ToArray();
        private PlayableRuntime Runtime=>Get<PlayableRuntime>(app,"runtime");
        private HashSet<int> Selection(PlayableBootstrap seat)=>Get<HashSet<int>>(seat,"selection");
        private readonly Dictionary<string,float> savedControls=new Dictionary<string,float>();
        private readonly string[] controlKeys={"spacewars.controls.cursorSpeed","spacewars.controls.cameraSpeed"};
        private bool savedCursorVisible;
        [SetUp] public override void Setup()
        {savedCursorVisible=Cursor.visible;base.Setup();savedControls.Clear();foreach(var key in controlKeys){if(PlayerPrefs.HasKey(key))savedControls[key]=PlayerPrefs.GetFloat(key);PlayerPrefs.DeleteKey(key);}}
        [TearDown] public override void TearDown()
        {if(host!=null)UnityEngine.Object.DestroyImmediate(host);Cursor.visible=savedCursorVisible;foreach(var key in controlKeys){if(savedControls.TryGetValue(key,out var value))PlayerPrefs.SetFloat(key,value);else PlayerPrefs.DeleteKey(key);}PlayerPrefs.Save();base.TearDown();}
        private IEnumerator StartOrdinary(int humans,bool humanAfterBots=false)
        {
            var keyboard=InputSystem.AddDevice<Keyboard>();var mouse=InputSystem.AddDevice<Mouse>();keyboard.MakeCurrent();mouse.MakeCurrent();pads=Enumerable.Range(0,humans).Select(_=>InputSystem.AddDevice<Gamepad>()).ToArray();
            host=new GameObject("Ordinary lobby gamepad test");app=host.AddComponent<PlayableBootstrap>();app.enabled=false;Put(app,"profile",PlayableProfile.ThreeCrossingsDefault);Call(app,"CreateWorld");Call(app,"CreateHud");Put(app,"input",host.AddComponent<PlayableInput>());Call(app,"BindLocalKeyboardInput");Call(app,"CreateLobby");Call(app,"ShowLobby");
            Get<UnityEngine.UIElements.VisualElement>(app,"lobbyScreen").Q<UnityEngine.UIElements.DropdownField>("lobby-map-choice").value="Огненный разлом";
            var draft=Get<NativeLobbyConfiguration>(app,"lobbySetup");draft.HasExplicitSeed=true;draft.ExplicitSeed=19092026;
            for(int i=0;i<humans;i++){draft.Participants[i].Human=true;draft.Participants[i].DeviceId=i==0?0:pads[i].deviceId;draft.Participants[i].Name="Человек "+(i+1);draft.Participants[i].Team=humans==2?i+1:humans==4?i+1:1;}
            if(humanAfterBots){foreach(var participant in draft.Participants)participant.Human=false;draft.Participants[2].Human=true;draft.Participants[2].DeviceId=0;}
            Call(app,"RebuildRoster");Call(app,"LaunchLobbyMatch");yield return Wait(()=>Runtime!=null&&!Get<bool>(app,"preparing"));
            Call(app,"UpdateFrame");Assert.AreEqual(humans,Seats.Length);Assert.AreEqual(humans,Runtime.Latest.Participants.Count(p=>p.Control==OfflineControl.Human));
            foreach(var seat in Seats){Assert.AreSame(Runtime,Get<PlayableRuntime>(seat,"runtime"));Assert.AreEqual(Runtime.Generation,Get<PlayableSnapshot>(seat,"view").Generation);Assert.True(Get<PlayableInput>(seat,"input").WorldInputEnabled);}
            Call(app,"OnApplicationFocus",true);Call(app,"UpdateFrame");yield return null;
        }
        private IEnumerator Wait(Func<bool> ready,string phase="runtime")
        {float end=Time.realtimeSinceStartup+15;int progress=-1;while(!ready()){int current=(Runtime!=null?10:0)+Seats.Length;if(current!=progress){progress=current;end=Time.realtimeSinceStartup+15;}if(Time.realtimeSinceStartup>=end)break;if(Runtime!=null&&!Get<bool>(app,"preparing"))Call(app,"OnApplicationFocus",true);Call(app,"UpdateFrame");yield return null;}Assert.True(ready(),"ordinary local scenario deadline: "+phase+" paused="+Get<bool>(app,"paused")+" preparing="+Get<bool>(app,"preparing")+" stopped="+Runtime?.IsStopped+" failure="+Runtime?.Latest?.Failure+RallyDiagnostics()+" receipts="+string.Join(",",Runtime?.OfflineFrame?.Receipts.Select(r=>r.Receipt.OwnerId+":"+r.Receipt.Sequence+":"+r.Receipt.Status)??Array.Empty<string>()));}
        private void PadCursor(PlayableBootstrap seat,NavPoint point)
        {Put(seat,"padCursorGround",point);Put(seat,"padCursorInitialized",true);Put(seat,"battleCursor",(Vector2)Get<Camera>(seat,"cameraView").WorldToScreenPoint(Get<PlayableWorld>(seat,"world").Point(point)));}
        private IEnumerator Capture(string label,Action context=null,int captureWidth=1920,int captureHeight=1200)
        {
            string output=Environment.GetEnvironmentVariable("ORDINARY_PAD_EVIDENCE");if(string.IsNullOrEmpty(output))yield break;
            var panel=host.GetComponent<UnityEngine.UIElements.UIDocument>().panelSettings;var image=new RenderTexture(captureWidth,captureHeight,24);image.Create();panel.targetTexture=image;
            var scenes=new List<UnityEngine.UIElements.VisualElement>();var targets=new List<RenderTexture>();
            try
            {
                foreach(var seat in Seats)
                {
                    var camera=Get<Camera>(seat,"cameraView");var rect=camera.rect;int width=(int)(captureWidth*rect.width),height=(int)(captureHeight*rect.height);var scene=new RenderTexture(width,height,24);scene.Create();targets.Add(scene);
                    camera.targetTexture=scene;camera.rect=new Rect(0,0,1,1);camera.Render();camera.targetTexture=null;camera.rect=rect;
                    var backdrop=new UnityEngine.UIElements.VisualElement{pickingMode=UnityEngine.UIElements.PickingMode.Ignore};backdrop.style.position=UnityEngine.UIElements.Position.Absolute;backdrop.style.left=backdrop.style.right=backdrop.style.top=backdrop.style.bottom=0;backdrop.style.backgroundImage=new UnityEngine.UIElements.StyleBackground(UnityEngine.UIElements.Background.FromRenderTexture(scene));Get<UnityEngine.UIElements.VisualElement>(seat,"root").Insert(0,backdrop);scenes.Add(backdrop);
                }
                for(int i=0;i<8;i++){yield return null;Call(app,"UpdateFrame");context?.Invoke();}
                if(label.EndsWith("-humans"))foreach(var seat in Seats){var bounds=Get<VisualElement>(seat,"root").worldBound;var header=Get<VisualElement>(seat,"top");Assert.AreEqual(DisplayStyle.Flex,header.style.display.value);Assert.True(bounds.Contains(header.worldBound.center),"header must remain in seat: "+bounds+" header="+header.worldBound);}

                AssertCompactBounds();
                Directory.CreateDirectory(output);var old=RenderTexture.active;RenderTexture.active=image;var png=new Texture2D(captureWidth,captureHeight,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,captureWidth,captureHeight),0,0);png.Apply();File.WriteAllBytes(Path.Combine(output,label+".png"),png.EncodeToPNG());RenderTexture.active=old;UnityEngine.Object.Destroy(png);
            }
            finally{panel.targetTexture=null;foreach(var scene in scenes)scene.RemoveFromHierarchy();foreach(var target in targets){target.Release();UnityEngine.Object.Destroy(target);}image.Release();UnityEngine.Object.Destroy(image);}
        }
        private static void Inside(Rect outer,Rect inner,string name)
        {Assert.GreaterOrEqual(inner.xMin,outer.xMin-.5f,name);Assert.LessOrEqual(inner.xMax,outer.xMax+.5f,name);Assert.GreaterOrEqual(inner.yMin,outer.yMin-.5f,name);Assert.LessOrEqual(inner.yMax,outer.yMax+.5f,name);}
        private void AssertCompactBounds()
        {
            if(Get<bool>(app,"paused")||Get<bool>(app,"inLobby")||Get<bool>(app,"restarting")||Runtime?.Result!=null)return;
            foreach(var seat in Seats)
            {
                var root=Get<VisualElement>(seat,"root");var header=Get<VisualElement>(seat,"top");Inside(root.worldBound,header.worldBound,"header");
                Assert.Less(header.worldBound.width,root.worldBound.width*.55f,"compact header");Assert.AreEqual(Color.clear,header.style.backgroundColor.value,"transparent resources");
                Assert.LessOrEqual(Get<Button>(seat,"hudFocusButton").worldBound.xMin-Get<Label>(seat,"creditsLabel").worldBound.xMax,10,"resource/population gap");
                foreach(var child in new TextElement[]{Get<Label>(seat,"creditsLabel"),Get<Button>(seat,"hudFocusButton")}){Inside(header.worldBound,child.worldBound,"header content");float textWidth=child.MeasureTextSize(child.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x;Assert.GreaterOrEqual(child.contentRect.width+.5f,textWidth,"resource text fits its numeric slot "+child.GetType().Name+" inline="+child.style.width+" resolved="+child.resolvedStyle.width+" header="+header.worldBound+" display="+header.resolvedStyle.display);}
                if(Get<bool>(seat,"mapOpen")){AssertTacticalScreen(seat);continue;}
                var context=Get<VisualElement>(seat,"armyRegion");if(context.resolvedStyle.display==DisplayStyle.None)continue;
                Inside(root.worldBound,context.worldBound,"context");Assert.That(context.worldBound.yMax,Is.EqualTo(root.worldBound.yMax).Within(.5),"flush bottom context");Assert.False(context.worldBound.Overlaps(header.worldBound),"context/header overlap");
                foreach(var button in context.Query<Button>().ToList())if(button.resolvedStyle.display!=DisplayStyle.None&&button.worldBound.height>0&&button.visible)Inside(context.worldBound,button.worldBound,"queue "+button.name);
                var ring=Get<OrbitalBattleRing>(seat,"battleRing");if(ring.resolvedStyle.display!=DisplayStyle.None){Inside(root.worldBound,ring.worldBound,"action ring");Assert.False(ring.worldBound.Overlaps(context.worldBound),"ring/context overlap");Assert.False(ring.worldBound.Overlaps(header.worldBound),"ring/header overlap");}
                if(Seats.Length>1&&Array.IndexOf(Seats,seat)%2==0)Assert.That(context.worldBound.xMin,Is.EqualTo(root.worldBound.xMin).Within(.5),"flush left context");
                else Assert.That(context.worldBound.xMax,Is.EqualTo(root.worldBound.xMax).Within(.5),"flush right context");
            }
            var map=Get<VisualElement>(app,"sharedMapFrame");if(Seats.Any(seat=>Get<bool>(seat,"mapOpen"))){if(map!=null)Assert.AreEqual(DisplayStyle.None,map.style.display.value,"Shared minimap is hidden during tactical views.");return;}if(map==null){var single=Get<VisualElement>(app,"root").Q("tactical-minimap-frame");var bounds=Get<VisualElement>(app,"root").worldBound;Assert.That(single.worldBound.xMin,Is.EqualTo(bounds.xMin).Within(.5));Assert.That(single.worldBound.yMax,Is.EqualTo(bounds.yMax).Within(.5));return;}
            var panel=host.GetComponent<UIDocument>().rootVisualElement.worldBound;Inside(panel,map.worldBound,"shared map");
            var surface=Get<PlayableMapSurface>(app,"sharedLocalMap");Assert.That(surface.worldBound.width,Is.EqualTo(surface.worldBound.height).Within(.5),"map proportions");
            if(Seats.Length==3){Assert.Greater(surface.worldBound.height,panel.height*.4f,"map fills spare quadrant");Inside(new Rect(panel.width/2,panel.height/2,panel.width/2,panel.height/2),map.worldBound,"spare quadrant");}
            if(Seats.Length==2)Assert.That(map.worldBound.yMax,Is.EqualTo(panel.yMax).Within(.5),"flush bottom map");
            Assert.That(map.worldBound.center.x,Is.EqualTo(panel.width*(Seats.Length==3?.75f:.5f)).Within(1));
            if(Seats.Length>2)Assert.That(map.worldBound.center.y,Is.EqualTo(panel.height*(Seats.Length==3?.75f:.5f)).Within(1));
            Assert.GreaterOrEqual(Get<Label>(app,"sharedMapClock").worldBound.yMin,Get<PlayableMapSurface>(app,"sharedLocalMap").worldBound.yMax,"clock below map");
            foreach(var seat in Seats){Assert.False(map.worldBound.Overlaps(Get<VisualElement>(seat,"top").worldBound),"map/header overlap");var ring=Get<OrbitalBattleRing>(seat,"battleRing");if(ring.resolvedStyle.display!=DisplayStyle.None)Assert.False(map.worldBound.Overlaps(ring.worldBound),"map/action overlap");var context=Get<VisualElement>(seat,"armyRegion");if(context.resolvedStyle.display!=DisplayStyle.None)Assert.False(map.worldBound.Overlaps(context.worldBound),"map/context overlap");}
        }
        private void QueueContext(bool science)
        {
            foreach(var seat in Seats)
            {
                var current=Get<PlayableSnapshot>(seat,"view");int id=9000+Array.IndexOf(Seats,seat);var point=current.Entities.First(e=>e.Owner==current.Owner).Position;
                var orders=Enumerable.Range(0,6).Select(i=>new PlayableProductionOrderSnapshot(i+1,(PlayableEntityKind)(i%3),150,3,15,i==0?7.5:15,i==0)).ToArray();
                var research=Enumerable.Range(0,3).Select(i=>new PlayableResearchOrderSnapshot(i+1,(PlayableResearchKind)i,id,300,i==0?15:0,30,i==0,false)).ToArray();
                var lifecycle=new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false);
                var building=new PlayableBuildingSnapshot(id,current.Owner,science?PlayableBuildingKind.ScientificCenter:PlayableBuildingKind.Factory,point,250,1,science?3:6,.5,point,orders:science?null:orders,research:science?research:null,lifecycle:lifecycle);
                Put(seat,"view",new PlayableSnapshot(current.ProfileId,current.ProfileRevision,current.Generation,current.Seed,current.Sequence,current.Tick,current.Status,false,current.Outcome,current.Credits,current.Geometry,current.Entities.ToArray(),new[]{building},current.Projectiles.ToArray(),current.Metrics,null,incomePerSecond:current.IncomePerSecond,vision:current.Vision,population:current.Population,owner:current.Owner,ownerResearch:science?research:null,ownerId:current.OwnerId));
                Selection(seat).Clear();Selection(seat).Add(id);Put(seat,"notice","");Call(seat,"UpdateHud");
            }
        }
        private IEnumerator RoutingAndLayout(int humans)
        {
            yield return StartOrdinary(humans);var seats=Seats;Set(Mouse.current.position,Get<Camera>(seats[0],"cameraView").pixelRect.center);yield return null;var before=seats.Select(seat=>Get<Camera>(seat,"cameraView").transform.position).ToArray();
            for(int i=0;i<seats.Length;i++)
            {
                var seat=seats[i];var view=Get<PlayableSnapshot>(seat,"view");var own=view.Entities.First(e=>e.Owner==view.Owner);Selection(seat).Add(own.Id);
                var target=new NavPoint(own.Position.X+2,own.Position.Z);
                Call(seat,"Submit",PlayableCommandKind.Move,new[]{own.Id},target,0,0L,PlayableEntityKind.Tank,PlayableResearchKind.TankChassis,PlayableOrderMode.Replace);
            }
            yield return Wait(()=>Runtime.OfflineFrame.Receipts.Count(r=>r.Receipt.Sequence==1&&r.Receipt.Status==PlayableCommandStatus.Applied&&r.Receipt.OwnerId.StartsWith("player-1")||r.Receipt.Sequence==1&&r.Receipt.Status==PlayableCommandStatus.Applied&&r.Receipt.OwnerId.StartsWith("foundry-"))>=humans,"initial owner moves");
            long capturedTick=Get<PlayableSnapshot>(seats[0],"view").Tick;foreach(var seat in seats)Assert.AreEqual(capturedTick,Get<PlayableSnapshot>(seat,"view").Tick);
            for(int i=0;i<seats.Length;i++){Assert.AreEqual(before[i],Get<Camera>(seats[i],"cameraView").transform.position);Assert.AreEqual(1,Selection(seats[i]).Count);Assert.AreEqual(8+i,Get<Camera>(seats[i],"cameraView").cullingMask==1<<(8+i)?8+i:-1);}
            if(humans>1)
            {
                var foreign=Get<PlayableSnapshot>(seats[1],"view").Entities.First(e=>e.Owner==Get<PlayableSnapshot>(seats[1],"view").Owner);
                Call(seats[0],"Submit",PlayableCommandKind.Stop,new[]{foreign.Id},default(NavPoint),0,0L,PlayableEntityKind.Tank,PlayableResearchKind.TankChassis,PlayableOrderMode.Replace);
                yield return Wait(()=>Runtime.OfflineFrame.Receipts.Any(r=>r.Receipt.OwnerId=="player-1"&&r.Receipt.Sequence==2&&r.Receipt.Status==PlayableCommandStatus.InvalidEntity),"foreign rejection");
                Assert.AreEqual(1,Selection(seats[1]).Count);
                var old=Get<Camera>(seats[0],"cameraView").transform.position;Set(pads[1].leftStick,Vector2.zero);InputSystem.Update();Call(app,"UpdateFrame");Set(pads[1].leftStick,Vector2.right);InputSystem.Update();yield return null;Call(app,"UpdateFrame");
                Assert.AreEqual(old,Get<Camera>(seats[0],"cameraView").transform.position);Assert.AreNotEqual(before[1],Get<Camera>(seats[1],"cameraView").transform.position);Set(pads[1].leftStick,Vector2.zero);InputSystem.Update();
            }
            foreach(var seat in seats)Put(seat,"notice","");
            yield return Capture("ordinary-"+humans+"-humans");
            yield return Capture("ordinary-"+humans+"-production",()=>QueueContext(false));
            yield return Capture("ordinary-"+humans+"-research",()=>QueueContext(true));
            if(humans>=3)yield return Capture("ordinary-"+humans+"-research-1280x720",()=>QueueContext(true),1280,720);
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [UnityTest] public IEnumerator OneHumanUsesOrdinaryRoster()=>RoutingAndLayout(1);
        [UnityTest] public IEnumerator TwoHumansHaveIndependentOrdinaryOwners()=>RoutingAndLayout(2);
        [UnityTest] public IEnumerator ThreeHumansHaveIndependentOrdinaryOwners()=>RoutingAndLayout(3);
        [UnityTest] public IEnumerator FourHumansHaveIndependentOrdinaryOwners()=>RoutingAndLayout(4);
        [UnityTest] public IEnumerator KeyboardAssignedGroupCanBeRecalledByPadAndDisconnectCancelsHold()
        {
            yield return StartOrdinary(2);var seats=Seats;var primary=seats[0];var own=Get<PlayableSnapshot>(primary,"view").Entities.First(e=>e.Owner==PlayableOwner.Player);Selection(primary).Add(own.Id);Call(primary,"KeyboardSelection",1,true,Time.unscaledTimeAsDouble);Selection(primary).Clear();
            Set(pads[0].leftStick,Vector2.zero);InputSystem.Update();Call(app,"UpdateFrame");Press(pads[0].rightShoulder);InputSystem.Update();Call(app,"UpdateFrame");Release(pads[0].rightShoulder);InputSystem.Update();Call(app,"UpdateFrame");yield return new WaitForSecondsRealtime(.5f);Call(app,"UpdateFrame");CollectionAssert.AreEqual(new[]{own.Id},Selection(primary),"RB mode="+Get<OfflinePadGestures>(primary,"battleGestures").Mode+" blocked="+Get<OfflinePadGestures>(primary,"battleGestures").Blocked+" active="+Get<KeyboardControlGroups>(primary,"keyboardGroups").Active+" ready="+Get<PlayableInput>(primary,"input").WorldInputEnabled);Assert.AreEqual(1,Get<KeyboardControlGroups>(primary,"keyboardGroups").Active);
            var building=Get<PlayableSnapshot>(primary,"view").Buildings.First(b=>b.Owner==PlayableOwner.Player);Selection(primary).Clear();Selection(primary).Add(building.Id);Call(primary,"KeyboardSelection",9,true,Time.unscaledTimeAsDouble);Selection(primary).Clear();
            Press(pads[0].rightShoulder);InputSystem.Update();Call(app,"UpdateFrame");yield return new WaitForSecondsRealtime(.35f);Call(app,"UpdateFrame");float angle=8*Mathf.PI*2/9;Set(pads[0].leftStick,new Vector2(Mathf.Sin(angle),Mathf.Cos(angle)));InputSystem.Update();Call(app,"UpdateFrame");yield return Capture("ordinary-nine-groups");Release(pads[0].rightShoulder);InputSystem.Update();Call(app,"UpdateFrame");Set(pads[0].leftStick,Vector2.zero);InputSystem.Update();CollectionAssert.AreEqual(new[]{building.Id},Selection(primary));
            var secondary=seats[1];long sequence=Get<long>(secondary,"sequence");Press(pads[1].buttonWest);InputSystem.Update();Call(app,"UpdateFrame");InputSystem.RemoveDevice(pads[1]);Call(app,"UpdateFrame");Assert.True(Get<bool>(app,"paused"));Assert.AreEqual(sequence,Get<long>(secondary,"sequence"));
            Assert.True(Get<OfflinePadGestures>(secondary,"battleGestures").Blocked);Assert.False(Get<PlayableInput>(primary,"input").WorldInputEnabled);
            int assigned=pads[1].deviceId;InputSystem.AddDevice(pads[1]);Assert.AreEqual(assigned,pads[1].deviceId);Release(pads[1].buttonWest);InputSystem.Update();Call(app,"UpdateFrame");Call(secondary,"Pause",false);Call(app,"UpdateFrame");Assert.False(Get<bool>(app,"paused"));Assert.AreEqual(sequence,Get<long>(secondary,"sequence"));
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        private void ButtonFrame(Gamepad pad,UnityEngine.InputSystem.Controls.ButtonControl button,bool down)
        {if(down)Press(button);else Release(button);InputSystem.Update();Call(app,"UpdateFrame");}
        private void Tap(Gamepad pad,UnityEngine.InputSystem.Controls.ButtonControl button)
        {ButtonFrame(pad,button,true);ButtonFrame(pad,button,false);}
        private void AssertCursor(bool padCursor)
        {
            Assert.AreEqual(padCursor,Get<bool>(app,"systemCursorHidden"));
            Assert.AreEqual(!padCursor,Cursor.visible,"Hardware mouse cursor visibility");
            Assert.AreEqual(padCursor?DisplayStyle.Flex:DisplayStyle.None,Get<VisualElement>(app,"battleCursorVisual").style.display.value);
        }
        [UnityTest] public IEnumerator PadCursorReplacesMouseAndRestoresItAcrossLifecycle()
        {
            Cursor.visible=true;yield return StartOrdinary(1);
            Set(pads[0].rightStick,new Vector2(.7f,0));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            yield return Capture("single-pad-cursor",captureWidth:1280,captureHeight:800);
            Set(pads[0].rightStick,Vector2.zero);InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            Set(Mouse.current.delta,new Vector2(4,0));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(false);
            yield return Capture("mouse-control",captureWidth:1280,captureHeight:800);
            InputSystem.Update();Set(pads[0].rightStick,new Vector2(.7f,0));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            Set(pads[0].rightStick,Vector2.zero);InputSystem.Update();Press(Mouse.current.rightButton);InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(false);Release(Mouse.current.rightButton);InputSystem.Update();
            Set(pads[0].rightStick,new Vector2(.7f,0));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            Call(app,"Pause",true);Call(app,"UpdateFrame");AssertCursor(false);
            Set(pads[0].rightStick,Vector2.zero);InputSystem.Update();Call(app,"Pause",false);Call(app,"UpdateFrame");
            Set(pads[0].rightStick,new Vector2(.7f,0));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            Call(app,"OnApplicationFocus",false);Assert.IsTrue(Cursor.visible);Assert.IsFalse(Get<bool>(app,"systemCursorHidden"));
            Set(pads[0].rightStick,Vector2.zero);InputSystem.Update();Call(app,"OnApplicationFocus",true);Call(app,"Pause",false);Call(app,"UpdateFrame");
            Set(pads[0].rightStick,new Vector2(.7f,0));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            InputSystem.RemoveDevice(pads[0]);Call(app,"UpdateFrame");AssertCursor(false);
            InputSystem.AddDevice(pads[0]);Set(pads[0].rightStick,Vector2.zero);InputSystem.Update();Call(app,"Pause",false);Call(app,"UpdateFrame");
            Set(pads[0].rightStick,new Vector2(.7f,0));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
            UnityEngine.Object.DestroyImmediate(host);host=null;Assert.IsTrue(Cursor.visible,"Destroy restores hardware cursor even on a disabled bootstrap");
        }
        [UnityTest] public IEnumerator OtherLocalPadDoesNotHideMousePlayersCursor()
        {
            Cursor.visible=true;yield return StartOrdinary(2);
            Set(pads[1].rightStick,new Vector2(.7f,0));InputSystem.Update();Call(app,"UpdateFrame");
            Assert.IsFalse(Get<bool>(app,"systemCursorHidden"));Assert.IsTrue(Cursor.visible);
            Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(Seats[1],"battleCursorVisual").style.display.value);
            Set(pads[0].rightStick,new Vector2(.7f,0));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            Set(pads[0].rightStick,Vector2.zero);InputSystem.Update();Set(Mouse.current.delta,new Vector2(4,0));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(false);
            Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(Seats[1],"battleCursorVisual").style.display.value);
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        private void AssertTacticalScreen(PlayableBootstrap seat)
        {
            var root=Get<VisualElement>(seat,"root");var overlay=Get<VisualElement>(seat,"tacticalOverlay");var map=Get<PlayableMapSurface>(seat,"tacticalMap");
            Assert.That(overlay.worldBound.xMin,Is.EqualTo(root.worldBound.xMin).Within(.5));Assert.That(overlay.worldBound.yMin,Is.EqualTo(root.worldBound.yMin).Within(.5));
            Assert.That(overlay.worldBound.width,Is.EqualTo(root.worldBound.width).Within(.5));Assert.That(overlay.worldBound.height,Is.EqualTo(root.worldBound.height).Within(.5));
            var frame=overlay.Q("tactical-map-background");Assert.AreEqual(1,frame.resolvedStyle.backgroundColor.a);Inside(overlay.worldBound,frame.worldBound,"opaque map frame");
            Assert.That(frame.worldBound.width-map.worldBound.width,Is.EqualTo(20).Within(.5));Assert.That(frame.worldBound.height-map.worldBound.height,Is.EqualTo(20).Within(.5));
            Assert.That(map.worldBound.xMin-frame.worldBound.xMin,Is.EqualTo(10).Within(.5));Assert.That(map.worldBound.yMin-frame.worldBound.yMin,Is.EqualTo(10).Within(.5));
            foreach(float border in new[]{frame.resolvedStyle.borderLeftWidth,frame.resolvedStyle.borderRightWidth,frame.resolvedStyle.borderTopWidth,frame.resolvedStyle.borderBottomWidth})Assert.AreEqual(2,border);
            foreach(var color in new[]{frame.resolvedStyle.borderLeftColor,frame.resolvedStyle.borderRightColor,frame.resolvedStyle.borderTopColor,frame.resolvedStyle.borderBottomColor})Assert.AreEqual(1,color.a);
            Assert.Greater(overlay.resolvedStyle.backgroundColor.a,.9);Assert.That(map.worldBound.width,Is.EqualTo(map.worldBound.height).Within(.5));Inside(overlay.worldBound,map.worldBound,"tactical map");
            Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(seat,"bottom").style.display.value);Assert.AreEqual(DisplayStyle.None,Get<PlayableMapSurface>(seat,"compactMap").parent.style.display.value);
            var top=Get<VisualElement>(seat,"top");var composition=Get<VisualElement>(seat,"armyComposition");Assert.AreEqual(DisplayStyle.Flex,top.style.display.value);Assert.AreEqual(DisplayStyle.Flex,composition.style.display.value);
            var children=root.Children().ToList();Assert.Greater(children.IndexOf(top),children.IndexOf(overlay));Assert.Greater(children.IndexOf(composition),children.IndexOf(overlay));Inside(root.worldBound,top.worldBound,"tactical resources");Inside(root.worldBound,composition.worldBound,"tactical composition");Assert.False(map.worldBound.Overlaps(composition.worldBound));
            var view=Get<PlayableSnapshot>(seat,"view");var counts=Get<Label[]>(seat,"armyCounts");var kinds=new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval};
            for(int i=0;i<kinds.Length;i++){Assert.AreEqual(view.Entities.Count(e=>e.Owner==view.Owner&&e.Kind==kinds[i]).ToString(),counts[i].text);Assert.AreEqual(DisplayStyle.Flex,counts[i].style.display.value,"Tactical counts include zero types.");}
        }
        [UnityTest] public IEnumerator SharedMinimapReturnsOnlyAfterAllTacticalScreensClose()
        {
            yield return StartOrdinary(2);var first=Seats[0];var second=Seats[1];var minimap=Get<VisualElement>(app,"sharedMapFrame");
            Call(first,"ToggleMap");Call(second,"ToggleMap");Call(app,"UpdateFrame");Assert.AreEqual(DisplayStyle.None,minimap.style.display.value);
            yield return Capture("two-tactical-screens");AssertTacticalScreen(first);AssertTacticalScreen(second);
            Call(first,"ToggleMap");Call(app,"UpdateFrame");Assert.AreEqual(DisplayStyle.None,minimap.style.display.value);Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(first,"bottom").style.display.value);
            Call(second,"ToggleMap");Call(app,"UpdateFrame");Assert.AreEqual(DisplayStyle.Flex,minimap.style.display.value);Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(second,"bottom").style.display.value);
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [UnityTest] public IEnumerator TacticalMapUsesLeftStickWithoutHintsOrCameraPan()
        {
            yield return StartOrdinary(1);var seat=app;var pad=pads[0];
            Call(seat,"ToggleMap");Call(app,"UpdateFrame");
            Assert.True(Get<bool>(seat,"mapOpen"));
            var overlay=Get<VisualElement>(seat,"tacticalOverlay");
            Assert.IsEmpty(overlay.Query<Label>().ToList(),"Tactical map has no keyboard or controller hint row.");
            var camera=Get<Camera>(seat,"cameraView");var cameraPosition=camera.transform.position;
            PadCursor(seat,default);Set(pad.leftStick,new Vector2(.7f,.7f));InputSystem.Update();
            yield return null;Call(app,"UpdateFrame");
            var moved=Get<NavPoint>(seat,"padCursorGround");Assert.Greater(moved.X,0);Assert.Less(moved.Z,0);
            Assert.AreEqual(cameraPosition,camera.transform.position,"Left stick moves the map cursor without panning the world camera.");
            Set(pad.leftStick,Vector2.zero);Set(pad.rightStick,new Vector2(-.7f,-.7f));InputSystem.Update();
            yield return null;Call(app,"UpdateFrame");Assert.AreEqual(moved,Get<NavPoint>(seat,"padCursorGround"),"Right stick does not move the tactical map cursor.");
            Assert.AreEqual(cameraPosition,camera.transform.position);
            Set(pad.rightStick,Vector2.zero);InputSystem.Update();
            yield return Capture("tactical-screen",captureWidth:1280,captureHeight:800);
            AssertTacticalScreen(seat);
            Put(seat,"battleRally",true);Call(seat,"UpdateMaps");Assert.IsEmpty(overlay.Query<Label>().ToList(),"Rally mode does not restore key hints.");Put(seat,"battleRally",false);
            Call(seat,"ToggleMap");Call(app,"UpdateFrame");Assert.False(Get<bool>(seat,"mapOpen"));
            Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(seat,"bottom").style.display.value);Assert.AreEqual(DisplayStyle.Flex,Get<PlayableMapSurface>(seat,"compactMap").parent.style.display.value);
            Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(seat,"armyComposition").style.display.value);
            var before=Get<NavPoint>(seat,"padCursorGround");Set(pad.rightStick,new Vector2(.7f,0));InputSystem.Update();
            yield return null;Call(app,"UpdateFrame");Assert.AreNotEqual(before,Get<NavPoint>(seat,"padCursorGround"),"World cursor still uses the right stick.");
            Set(pad.rightStick,Vector2.zero);InputSystem.Update();Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [UnityTest] public IEnumerator OrdinaryPadCommandsMapAndNeutralFocusUseTheSameOwnerSequence()
        {
            yield return StartOrdinary(1);var seat=app;var v=Get<PlayableSnapshot>(seat,"view");var own=v.Entities.First(e=>e.Owner==v.Owner);PadCursor(seat,own.Position);Call(app,"UpdateFrame");Tap(pads[0],pads[0].buttonSouth);CollectionAssert.AreEqual(new[]{own.Id},Selection(seat));
            var point=new NavPoint(own.Position.X+4,own.Position.Z+4);PadCursor(seat,point);Tap(pads[0],pads[0].buttonEast);long moved=Get<long>(seat,"sequence");Assert.Greater(moved,0);
            yield return Wait(()=>Runtime.OfflineFrame.Receipts.Any(r=>r.Receipt.OwnerId=="player-1"&&r.Receipt.Sequence==moved&&r.Receipt.Status==PlayableCommandStatus.Applied),"pad B move");
            Tap(pads[0],pads[0].buttonWest);Assert.AreEqual(moved+1,Get<long>(seat,"sequence"));
            ButtonFrame(pads[0],pads[0].buttonWest,true);yield return new WaitForSecondsRealtime(.35f);Call(app,"UpdateFrame");ButtonFrame(pads[0],pads[0].buttonWest,false);long held=Get<long>(seat,"sequence");Assert.AreEqual(moved+2,held);
            yield return Wait(()=>Runtime.OfflineFrame.Receipts.Any(r=>r.Receipt.Sequence==held&&r.Receipt.Status==PlayableCommandStatus.Applied),"pad X hold");
            Call(app,"UpdateFrame");Assert.True(Get<PlayableSnapshot>(seat,"view").Entities.First(e=>e.Id==own.Id).Held);
            Tap(pads[0],pads[0].selectButton);Assert.True(Get<bool>(seat,"mapOpen"));Call(app,"UpdateFrame");Tap(pads[0],pads[0].buttonSouth);Assert.False(Get<bool>(seat,"mapOpen"));
            Call(app,"UpdateFrame");Tap(pads[0],pads[0].rightShoulder);yield return new WaitForSecondsRealtime(.1f);Tap(pads[0],pads[0].rightShoulder);CollectionAssert.AreEquivalent(Get<PlayableSnapshot>(seat,"view").Entities.Where(e=>e.Owner==v.Owner).Select(e=>e.Id),Selection(seat));
            Call(app,"OnApplicationFocus",false);Assert.True(Get<bool>(app,"paused"));ButtonFrame(pads[0],pads[0].buttonWest,true);Call(app,"OnApplicationFocus",true);Call(app,"Pause",false);Call(app,"UpdateFrame");Assert.AreEqual(held,Get<long>(seat,"sequence"));ButtonFrame(pads[0],pads[0].buttonWest,false);Assert.AreEqual(held,Get<long>(seat,"sequence"));Call(app,"UpdateFrame");Tap(pads[0],pads[0].buttonWest);Assert.AreEqual(held+1,Get<long>(seat,"sequence"));
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [UnityTest] public IEnumerator LocalPauseSettingsRestartResultAndReturnStayWithOrdinaryCoordinator()
        {
            yield return StartOrdinary(2);var secondary=Seats[1];Call(app,"PauseFrom",secondary,true);Call(app,"UpdateFrame");Assert.AreSame(secondary,Get<PlayableBootstrap>(app,"localPauseOwner"));Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(secondary,"modal").style.display.value);Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(app,"root").style.display.value);
            Tap(pads[0],pads[0].buttonSouth);Assert.True(Get<bool>(app,"paused"),"another pad cannot resume the menu owner");
            Call(secondary,"OpenPauseSettings");var settings=Get<VisualElement>(secondary,"childMenu");var speed=settings.Q<Slider>("settings-pad-cursorSpeed");Assert.NotNull(speed);float before=speed.value;((Action<int>)speed.userData)(1);Assert.Greater(speed.value,before);Assert.AreEqual(Get<NativeLocalControlSettings>(app,"localControlSettings")?.CursorSpeed??20,20);Assert.AreEqual(20,Get<NativeLocalInputProfile>(app,"battleInputProfile").cursorSpeed);
            Get<NativeMenuNavigation>(secondary,"menuNavigation").Back();Call(app,"UpdateFrame");yield return Capture("ordinary-local-pause");Call(secondary,"OpenPauseHelp");Assert.NotNull(Resources.Load<Texture2D>("ControlsHelp/field"));yield return Capture("ordinary-controls-help");var navigation=Get<NativeMenuNavigation>(secondary,"menuNavigation");navigation.Move(Vector2.down);navigation.Move(Vector2.down);navigation.Activate();Assert.AreSame(Resources.Load<Texture2D>("ControlsHelp/groups"),navigation.Scope.Q<Image>("controls-help-image").image);yield return Capture("ordinary-controls-groups");Get<NativeMenuNavigation>(secondary,"menuNavigation").Back();
            Call(secondary,"Pause",false);Call(app,"UpdateFrame");long generation=Runtime.Generation;Call(secondary,"Restart");yield return Wait(()=>Runtime.Generation>generation&&!Runtime.IsStopped,"restart");Call(app,"UpdateFrame");Assert.AreEqual(2,Seats.Length);Assert.AreSame(Runtime,Get<PlayableRuntime>(Seats[1],"runtime"));
            Runtime.RequestFinish();yield return Wait(()=>Runtime.Result!=null,"finish");Call(app,"UpdateFrame");Assert.True((bool)typeof(PlayableBootstrap).GetProperty("ResultsVisible",F).GetValue(app));
            Assert.AreEqual(100,Get<VisualElement>(app,"root").style.width.value.value);yield return Capture("ordinary-local-results");
            Call(app,"ReturnToLobby");yield return Wait(()=>Get<bool>(app,"inLobby")&&!Get<bool>(app,"returningToLobby"),"return lobby");Assert.AreEqual(0,Seats.Length);Assert.AreEqual(1,host.GetComponent<UIDocument>().rootVisualElement.Query<VisualElement>("local-seat-hud").ToList().Count);
            Get<NativeLobbyConfiguration>(app,"lobbySetup").Participants[1].Human=false;Call(app,"RebuildRoster");Call(app,"LaunchLobbyMatch");yield return Wait(()=>Runtime!=null&&!Get<bool>(app,"preparing"),"one-human relaunch");Call(app,"UpdateFrame");yield return null;
            Assert.AreEqual(1,Seats.Length);Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(app,"root").Q("tactical-minimap-frame").style.display.value);Assert.AreEqual(0,Get<VisualElement>(app,"armyRegion").style.marginRight.value.value);Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [Test] public void SharedPadRecallPreservesKeyboardTapTimingAndUnitOnlyAssignment()
        {
            var entities=new[]{new KeyboardSelectable(1,0,true,false,0,0),new KeyboardSelectable(2,0,true,false,1,0),new KeyboardSelectable(3,0,true,true,2,0),new KeyboardSelectable(4,0,false,false,3,0)};var groups=new KeyboardControlGroups();
            groups.Apply(1,true,0,new[]{1},entities,450,out _);groups.Apply(9,true,0,new[]{3},entities,450,out _);Assert.AreEqual(9,groups.SlotCount(entities));Assert.False(groups.PadActions(new[]{3},entities,true,9).Any(s=>s.Enabled));
            groups.Apply(1,false,1,new int[0],entities,450,out bool first);Assert.False(first);CollectionAssert.AreEqual(new[]{3},groups.RecallPad(9,entities));groups.Apply(1,false,1.2,new[]{3},entities,450,out bool second);Assert.True(second,"pad recall must not rewrite keyboard tap timing");
            Assert.True(groups.Add(1,new[]{2,4},entities));CollectionAssert.AreEqual(new[]{1,2},groups.RecallPad(1,entities));Assert.AreEqual(1,groups.Active);
        }
        [UnityTest] public IEnumerator HumanAfterBotsUsesOwnOrdinaryPerspectiveFromPreparation()
        {
            yield return StartOrdinary(1,true);var perspective=Get<PlayableSnapshot>(app,"view");Assert.AreEqual((PlayableOwner)2,perspective.Owner);Assert.AreEqual("foundry-3",(string)typeof(PlayableBootstrap).GetProperty("LocalOwnerId",F).GetValue(app));
            var own=perspective.Entities.First(e=>e.Owner==perspective.Owner);Call(app,"Submit",PlayableCommandKind.Stop,new[]{own.Id},default(NavPoint),0,0L,PlayableEntityKind.Tank,PlayableResearchKind.TankChassis,PlayableOrderMode.Replace);
            yield return Wait(()=>Runtime.OfflineFrame.Receipts.Any(r=>r.Receipt.OwnerId=="foundry-3"&&r.Receipt.Sequence==1&&r.Receipt.Status==PlayableCommandStatus.Applied),"non-first human stop");
            Call(app,"UpdateFrame");Assert.AreEqual((PlayableOwner)2,Get<PlayableSnapshot>(app,"view").Owner);Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
    }
}
