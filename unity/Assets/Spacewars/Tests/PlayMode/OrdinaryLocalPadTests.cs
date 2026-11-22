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
        private readonly string[] controlKeys={"spacewars.controls.cursorSpeed","spacewars.controls.cameraSpeed","spacewars.controls.cursorRecenterIdleMs","spacewars.controls.mapCursorSpeed"};
        private bool savedCursorVisible;
        [SetUp] public override void Setup()
        {savedCursorVisible=Cursor.visible;base.Setup();savedControls.Clear();foreach(var key in controlKeys){if(PlayerPrefs.HasKey(key))savedControls[key]=PlayerPrefs.GetFloat(key);PlayerPrefs.DeleteKey(key);}}
        [TearDown] public override void TearDown()
        {if(host!=null)UnityEngine.Object.DestroyImmediate(host);Cursor.visible=savedCursorVisible;foreach(var key in controlKeys){if(savedControls.TryGetValue(key,out var value))PlayerPrefs.SetFloat(key,value);else PlayerPrefs.DeleteKey(key);}PlayerPrefs.Save();base.TearDown();}
        private IEnumerator StartOrdinary(int humans,bool humanAfterBots=false,bool allied=false,Action<NativeLobbyConfiguration> configureRoster=null,string mapName=null,bool technicalCrossings=false)
        {
            var keyboard=InputSystem.AddDevice<Keyboard>();var mouse=InputSystem.AddDevice<Mouse>();keyboard.MakeCurrent();mouse.MakeCurrent();pads=Enumerable.Range(0,humans).Select(_=>InputSystem.AddDevice<Gamepad>()).ToArray();
            host=new GameObject("Ordinary lobby gamepad test");app=host.AddComponent<PlayableBootstrap>();app.enabled=false;Put(app,"profile",PlayableProfile.ThreeCrossingsDefault);Call(app,"CreateWorld");Call(app,"CreateHud");Put(app,"input",host.AddComponent<PlayableInput>());Call(app,"BindLocalKeyboardInput");Call(app,"CreateLobby");Call(app,"ShowLobby");
            Get<UnityEngine.UIElements.VisualElement>(app,"lobbyScreen").Q<UnityEngine.UIElements.DropdownField>("lobby-map-choice").value=mapName??"Огненный разлом (6)";
            var draft=Get<NativeLobbyConfiguration>(app,"lobbySetup");draft.HasExplicitSeed=true;draft.ExplicitSeed=19092026;
            while (draft.Participants.Count < humans)
                Assert.IsNull(draft.AddParticipant(false, 0, draft.Capacity(Get<PlayableProfile>(app, "profile"))));
            for(int i=0;i<humans;i++){draft.Participants[i].Human=true;draft.Participants[i].DeviceId=pads[i].deviceId;draft.Participants[i].Name="Человек "+(i+1);draft.Participants[i].Team=allied?1:humans==2?i+1:humans==4?i+1:1;}
            if(humanAfterBots){foreach(var participant in draft.Participants)participant.Human=false;draft.Participants[2].Human=true;draft.Participants[2].DeviceId=0;}
            configureRoster?.Invoke(draft);Call(app,"RebuildRoster");Call(app,technicalCrossings?"LaunchTechnicalLobbyMatch":"LaunchLobbyMatch");yield return Wait(()=>Runtime!=null&&!Get<bool>(app,"preparing"));
            Call(app,"UpdateFrame");Assert.AreEqual(Math.Max(1,humans),Seats.Length);Assert.AreEqual(humans,Runtime.Latest.Participants.Count(p=>p.Control==OfflineControl.Human));
            foreach(var seat in Seats){Assert.IsTrue(Get<Camera>(seat,"cameraView").enabled,"Prepared local seat must render.");Assert.AreSame(Runtime,Get<PlayableRuntime>(seat,"runtime"));Assert.AreEqual(Runtime.Generation,Get<PlayableSnapshot>(seat,"view").Generation);Assert.True(Get<PlayableInput>(seat,"input").WorldInputEnabled);}
            Call(app,"OnApplicationFocus",true);Call(app,"UpdateFrame");yield return null;
        }
        [UnityTest]
        public IEnumerator OrbitalHudRefreshesCooldownsAndLayoutForEachLocalSeat()
        {
            yield return StartOrdinary(4, configureRoster: roster =>
            {
                for (int i = 0; i < roster.Participants.Count; i++)
                    roster.Participants[i].Team = i / 2 + 1;
            }, mapName: WhiteMeridianMap.DisplayName);
            var seats = Seats;
            var snapshots = seats.Select(seat => Get<PlayableSnapshot>(seat, "view")).ToArray();
            try
            {
                SetCooldown(5.2);
                UpdateFour();
                Assert.True(seats.All(seat => Cooldown(seat).style.display.value==DisplayStyle.Flex && Panel(seat).style.display.value == DisplayStyle.Flex));
                foreach(var seat in seats)Assert.AreEqual(.74f,Cooldown(seat).Fraction,.001f);
                var originalWidths = seats.Select(Width).ToArray();

                SetCooldown(2.1);
                UpdateFour();
                foreach(var seat in seats)Assert.AreEqual(.895f,Cooldown(seat).Fraction,.001f);

                var source = Get<PlayableProfile>(seats[1], "profile");
                var updated = source.CopyData();
                updated.orbitalPanelWidth = 120;
                var replacement = PlayableProfile.Create(updated, source.AuthoredMap, source.DisplayName);
                Put(seats[1], "profile", replacement);
                UpdateFour();
                Assert.Greater(Width(seats[1]), originalWidths[1]);
                foreach (int i in new[] { 0, 2, 3 }) Assert.AreEqual(originalWidths[i], Width(seats[i]));
                Put(seats[1], "profile", source);
                UpdateFour();
                CollectionAssert.AreEqual(originalWidths, seats.Select(Width).ToArray());
            }
            finally
            {
                Runtime.RequestStop();
            }
            yield return Wait(()=>Runtime.IsStopped, "stop");

            void SetCooldown(double remaining)
            {
                for (int i = 0; i < seats.Length; i++)
                    Put(seats[i], "view", snapshots[i].WithOrbital(new OrbitalEscalationSnapshot(
                        1, 25, 100, 0, 0, new[] { new OrbitalAbilitySnapshot(1, remaining, 20, 1) }, Array.Empty<int>())));
            }

            void UpdateFour()
            {
                foreach (var seat in seats) Call(seat, "UpdateOrbitalHud");
            }

            VisualElement Panel(PlayableBootstrap seat) => Get<VisualElement>(seat, "orbitalPanel");
            float Width(PlayableBootstrap seat) => Panel(seat).style.width.value.value;
            OrbitalCooldownSweep Cooldown(PlayableBootstrap seat) => Panel(seat).Q<OrbitalCooldownSweep>();
        }
        [UnityTest] public IEnumerator PadZoomOrbitAndResetKeepAimAtTheOwningViewportCenter()
        {
            yield return StartOrdinary(2);
            var seat=Seats[1];var pad=pads[1];var camera=Get<Camera>(seat,"cameraView");
            var other=Get<Camera>(Seats[0],"cameraView");float otherZoom=other.orthographicSize;var otherRotation=other.transform.rotation;
            var p=Get<PlayableProfile>(seat,"profile");
            var homePosition=Get<Vector3>(seat,"keyboardHomePosition");
            var homeRotation=Get<Quaternion>(seat,"keyboardHomeRotation");
            float homeZoom=Get<float>(seat,"keyboardHomeZoom");
            camera.orthographicSize=(float)((p.CameraMinZoom+p.CameraMaxZoom)/2);
            float startZoom=camera.orthographicSize;
            var start=camera.transform.position;
            Set(pad.leftStick,Vector2.right);Set(pad.rightStick,Vector2.up);InputSystem.Update();
            yield return null;Call(app,"UpdateFrame");
            Assert.AreNotEqual(start,camera.transform.position,"LS pans");
            Assert.Less(camera.orthographicSize,startZoom,"RS up zooms in");
            Assert.AreEqual(camera.pixelRect.center,Get<Vector2>(seat,"battleCursor"));
            Assert.AreEqual(otherZoom,other.orthographicSize,"Zoom is seat-local");

            Set(pad.leftStick,Vector2.zero);Set(pad.rightStick,Vector2.down);InputSystem.Update();
            float closer=camera.orthographicSize;
            yield return null;Call(app,"UpdateFrame");
            Assert.Greater(camera.orthographicSize,closer,"RS down zooms out");
            float zoom=camera.orthographicSize;
            var pivot=Get<NavPoint>(seat,"padCursorGround");
            foreach(var direction in new[]{Vector2.right,Vector2.left})
            {
                var heading=camera.transform.forward;
                Set(pad.rightStick,direction);InputSystem.Update();yield return null;Call(app,"UpdateFrame");
                float angle=Vector3.SignedAngle(Vector3.ProjectOnPlane(heading,Vector3.up),
                    Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up),Vector3.up);
                Assert.Greater(angle*direction.x,0,"Each horizontal direction turns the camera accordingly");
                Assert.AreEqual(zoom,camera.orthographicSize);
                Assert.AreEqual(otherRotation,other.transform.rotation,"Rotation is seat-local");
                Assert.AreEqual(camera.pixelRect.center,Get<Vector2>(seat,"battleCursor"));
                var after=Get<NavPoint>(seat,"padCursorGround");
                Assert.That(after.X,Is.EqualTo(pivot.X).Within(.01));
                Assert.That(after.Z,Is.EqualTo(pivot.Z).Within(.01));
            }
            var position=camera.transform.position;
            Set(pad.rightStick,Vector2.zero);InputSystem.Update();
            Tap(pad,pad.leftStickButton);Tap(pad,pad.dpad.up);
            Assert.AreEqual(position,camera.transform.position,"L3 and D-pad up have no field action");
            Assert.AreEqual(zoom,camera.orthographicSize);Assert.False(Get<bool>(seat,"signalWheelOpen"));
            Tap(pad,pad.rightStickButton);
            Assert.AreEqual(homePosition,camera.transform.position);Assert.AreEqual(homeRotation,camera.transform.rotation);
            Assert.AreEqual(homeZoom,camera.orthographicSize);Assert.False(Get<bool>(seat,"signalWheelOpen"));
            yield return Capture("pad-centered-aim",captureWidth:1280,captureHeight:800,captureChecks:()=>
                Assert.AreEqual(camera.pixelRect.center,Get<Vector2>(seat,"battleCursor")));
        }
        private IEnumerator Wait(Func<bool> ready,string phase="runtime")
        {float end=Time.realtimeSinceStartup+15;int progress=-1;while(!ready()){int current=(Runtime!=null?10:0)+Seats.Length;if(current!=progress){progress=current;end=Time.realtimeSinceStartup+15;}if(Time.realtimeSinceStartup>=end)break;if(Runtime!=null&&!Get<bool>(app,"preparing"))Call(app,"OnApplicationFocus",true);Call(app,"UpdateFrame");yield return null;}Assert.True(ready(),"ordinary local scenario deadline: "+phase+" paused="+Get<bool>(app,"paused")+" preparing="+Get<bool>(app,"preparing")+" stopped="+Runtime?.IsStopped+" failure="+Runtime?.Latest?.Failure+RallyDiagnostics()+" receipts="+string.Join(",",Runtime?.OfflineFrame?.Receipts.Select(r=>r.Receipt.OwnerId+":"+r.Receipt.Sequence+":"+r.Receipt.Status)??Array.Empty<string>()));}
        private void PadCursor(PlayableBootstrap seat,NavPoint point)
        {Put(seat,"padCursorGround",point);Put(seat,"padCursorInitialized",true);if(!Get<bool>(seat,"mapOpen"))Put(seat,"battleCursor",(Vector2)Get<Camera>(seat,"cameraView").WorldToScreenPoint(Get<PlayableWorld>(seat,"world").Point(point)));}
        private IEnumerator Capture(string label,Action context=null,int captureWidth=1920,int captureHeight=1200,Action beforeFrames=null,Action captureChecks=null,bool refreshWorld=true,int warmFrames=0)
        {
            string output=Environment.GetEnvironmentVariable("ORDINARY_PAD_EVIDENCE");if(string.IsNullOrEmpty(output))yield break;
            var panel=host.GetComponent<UnityEngine.UIElements.UIDocument>().panelSettings;var image=new RenderTexture(captureWidth,captureHeight,24);image.Create();panel.targetTexture=image;
            var scenes=new List<UnityEngine.UIElements.VisualElement>();var targets=new List<RenderTexture>();var cameras=new List<Camera>();var aspects=new List<float>();
            try
            {
                foreach(var seat in Seats)
                {
                    var camera=Get<Camera>(seat,"cameraView");var rect=camera.rect;int width=(int)(captureWidth*rect.width),height=(int)(captureHeight*rect.height);var scene=new RenderTexture(width,height,24);scene.Create();targets.Add(scene);cameras.Add(camera);aspects.Add(camera.aspect);camera.aspect=(float)width/height;
                    camera.targetTexture=scene;camera.rect=new Rect(0,0,1,1);camera.Render();camera.targetTexture=null;camera.rect=rect;
                    var backdrop=new UnityEngine.UIElements.VisualElement{pickingMode=UnityEngine.UIElements.PickingMode.Ignore};backdrop.style.position=UnityEngine.UIElements.Position.Absolute;backdrop.style.left=backdrop.style.right=backdrop.style.top=backdrop.style.bottom=0;backdrop.style.backgroundImage=new UnityEngine.UIElements.StyleBackground(UnityEngine.UIElements.Background.FromRenderTexture(scene));Get<UnityEngine.UIElements.VisualElement>(seat,"root").Insert(0,backdrop);scenes.Add(backdrop);
                }
                for(int i=0;i<8+warmFrames;i++){
                    if(i==warmFrames)beforeFrames?.Invoke();
                    yield return null;if(refreshWorld)Call(app,"UpdateFrame");if(i>=warmFrames)context?.Invoke();
                    // The UI signal projection follows the current camera. Refresh its
                    // scene backdrop too, instead of compositing onto a stale first frame.
                    for(int j=0;j<cameras.Count;j++){var camera=cameras[j];var rect=camera.rect;camera.targetTexture=targets[j];camera.rect=new Rect(0,0,1,1);camera.Render();camera.targetTexture=null;camera.rect=rect;}
                }
                if(label.EndsWith("-humans"))foreach(var seat in Seats){var bounds=Get<VisualElement>(seat,"root").worldBound;var header=Get<VisualElement>(seat,"top");Assert.AreEqual(DisplayStyle.Flex,header.style.display.value);Assert.True(bounds.Contains(header.worldBound.center),"header must remain in seat: "+bounds+" header="+header.worldBound);}

                if(captureChecks==null)AssertCompactBounds();else captureChecks();
                Directory.CreateDirectory(output);var old=RenderTexture.active;RenderTexture.active=image;var png=new Texture2D(captureWidth,captureHeight,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,captureWidth,captureHeight),0,0);png.Apply();File.WriteAllBytes(Path.Combine(output,label+".png"),png.EncodeToPNG());RenderTexture.active=old;UnityEngine.Object.Destroy(png);
            }
            finally{panel.targetTexture=null;for(int j=0;j<cameras.Count;j++)cameras[j].aspect=aspects[j];foreach(var scene in scenes)scene.RemoveFromHierarchy();foreach(var target in targets){target.Release();UnityEngine.Object.Destroy(target);}image.Release();UnityEngine.Object.Destroy(image);}
        }
        private static void Inside(Rect outer,Rect inner,string name)
        {Assert.GreaterOrEqual(inner.xMin,outer.xMin-.5f,name);Assert.LessOrEqual(inner.xMax,outer.xMax+.5f,name);Assert.GreaterOrEqual(inner.yMin,outer.yMin-.5f,name);Assert.LessOrEqual(inner.yMax,outer.yMax+.5f,name);}
        private void AssertCompactBounds()
        {
            if(Get<bool>(app,"paused")||Get<bool>(app,"inLobby")||Get<bool>(app,"restarting")||Runtime?.Result!=null)return;
            foreach(var seat in Seats)
            {
                if(Get<bool>(seat,"mapOpen")){AssertTacticalScreen(seat);continue;}
                var root=Get<VisualElement>(seat,"root");var header=Get<VisualElement>(seat,"top");Inside(root.worldBound,header.worldBound,"header");
                Assert.Less(header.worldBound.width,root.worldBound.width*.55f,"compact header");Assert.AreEqual(Color.clear,header.style.backgroundColor.value,"transparent resources");
                Assert.LessOrEqual(Get<Button>(seat,"hudFocusButton").worldBound.xMin-Get<Label>(seat,"creditsLabel").worldBound.xMax,10,"resource/population gap");
                foreach(var child in new TextElement[]{Get<Label>(seat,"creditsLabel"),Get<Button>(seat,"hudFocusButton")}){Inside(header.worldBound,child.worldBound,"header content");float textWidth=child.MeasureTextSize(child.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x;Assert.GreaterOrEqual(child.contentRect.width+.5f,textWidth,"resource text fits its numeric slot "+child.GetType().Name+" inline="+child.style.width+" resolved="+child.resolvedStyle.width+" header="+header.worldBound+" display="+header.resolvedStyle.display);}
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
        [UnityTest]
        public IEnumerator FourSeatTerrainOptimizationFollowsViewportLifetime()
        {
            yield return StartOrdinary(4, configureRoster: roster =>
            {
                for (int i = 0; i < roster.Participants.Count; i++)
                    roster.Participants[i].Team = i / 2 + 1;
            }, mapName: "Переправа (2)", technicalCrossings: true);

            var worldMaterial = typeof(PlayableWorld).GetProperty("TerrainCacheMaterial", F);
            bool cacheSupported = (bool)typeof(TerrainMaterialCacheCamera)
                .GetProperty("DeviceSupportsCache", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            bool instancingSupported = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Metal &&
                SystemInfo.supportsInstancing;
            TestContext.WriteLine($"Terrain cache supported={cacheSupported}, depth instancing supported={instancingSupported}");
            var seats = Seats;
            foreach (var seat in seats)
            {
                var camera = Get<Camera>(seat, "cameraView");
                var world = Get<PlayableWorld>(seat, "world");
                var material = (Material)worldMaterial.GetValue(world);
                var cache = camera.GetComponent<TerrainMaterialCacheCamera>();
                Assert.AreEqual(instancingSupported, material.enableInstancing);
                Assert.AreEqual(cacheSupported, cache != null);
                if (cache) Assert.True(cache.isActiveAndEnabled);
                Call(seat, "ConfigureLocalViewport", Get<int>(seat, "localViewportIndex"), 4);
                Assert.AreSame(cache, camera.GetComponent<TerrainMaterialCacheCamera>());
            }

            Runtime.RequestStop();
            yield return Wait(() => Runtime.IsStopped, "stop");
            foreach (var seat in seats)
            {
                var camera = Get<Camera>(seat, "cameraView");
                var cache = camera.GetComponent<TerrainMaterialCacheCamera>();
                Call(seat, "ResetLocalViewport");
                Assert.False(((Material)worldMaterial.GetValue(Get<PlayableWorld>(seat, "world"))).enableInstancing);
                if (cache) Assert.False(cache.isActiveAndEnabled);
            }
            yield return null;
            foreach (var seat in seats)
                Assert.False(Get<Camera>(seat, "cameraView").GetComponent<TerrainMaterialCacheCamera>());
        }
        [UnityTest] public IEnumerator AssignedPadRecallsSharedGroupsAndDisconnectCancelsHold()
        {
            yield return StartOrdinary(2);var seats=Seats;var primary=seats[0];var own=Get<PlayableSnapshot>(primary,"view").Entities.First(e=>e.Owner==PlayableOwner.Player);Selection(primary).Add(own.Id);Call(primary,"KeyboardSelection",1,true,Time.unscaledTimeAsDouble);Selection(primary).Clear();
            Set(pads[0].leftStick,Vector2.zero);InputSystem.Update();Call(app,"UpdateFrame");Press(pads[0].rightShoulder);InputSystem.Update();Call(app,"UpdateFrame");Release(pads[0].rightShoulder);InputSystem.Update();Call(app,"UpdateFrame");yield return new WaitForSecondsRealtime(.5f);Call(app,"UpdateFrame");CollectionAssert.AreEqual(new[]{own.Id},Selection(primary),"RB mode="+Get<OfflinePadGestures>(primary,"battleGestures").Mode+" blocked="+Get<OfflinePadGestures>(primary,"battleGestures").Blocked+" active="+Get<KeyboardControlGroups>(primary,"keyboardGroups").Active+" ready="+Get<PlayableInput>(primary,"input").WorldInputEnabled);Assert.AreEqual(1,Get<KeyboardControlGroups>(primary,"keyboardGroups").Active);
            var building=Get<PlayableSnapshot>(primary,"view").Buildings.First(b=>b.Owner==PlayableOwner.Player);Selection(primary).Clear();Selection(primary).Add(building.Id);Call(primary,"KeyboardSelection",9,true,Time.unscaledTimeAsDouble);Selection(primary).Clear();
            ButtonFrame(pads[0],pads[0].rightShoulder,true);yield return new WaitForSecondsRealtime(RadialHoldWait(primary));Call(app,"UpdateFrame");
            var slots=Get<int[]>(primary,"padGroupSlots");int slotIndex=Array.IndexOf(slots,9);
            Assert.GreaterOrEqual(slotIndex,0,"The sparse wheel must expose the occupied slot 9");
            float angle=slotIndex*Mathf.PI*2/slots.Length;
            Set(pads[0].leftStick,new Vector2(Mathf.Sin(angle),Mathf.Cos(angle)));InputSystem.Update();Call(app,"UpdateFrame");
            yield return Capture("ordinary-nine-groups");Tap(pads[0],pads[0].buttonSouth);ButtonFrame(pads[0],pads[0].rightShoulder,false);
            Set(pads[0].leftStick,Vector2.zero);InputSystem.Update();CollectionAssert.AreEqual(new[]{building.Id},Selection(primary));
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
            Assert.AreEqual(padCursor?DisplayStyle.Flex:DisplayStyle.None,(Get<VisualElement>(app,"battleCursorVisual")?.style.display.value??DisplayStyle.None));
        }
        [UnityTest] public IEnumerator AssignedPadIgnoresMouseInCombatAndRestoresCursorAcrossLifecycle()
        {
            Cursor.visible=true;yield return StartOrdinary(1);
            Set(pads[0].rightStick,new Vector2(0,.7f));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            yield return Capture("single-pad-cursor",captureWidth:1280,captureHeight:800);
            Set(pads[0].rightStick,Vector2.zero);InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            Set(Mouse.current.delta,new Vector2(4,0));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            Assert.False(Get<PlayableInput>(app,"input").enabled,"Mouse is menu-only for a pad-bound human");
            yield return Capture("mouse-does-not-steal-combat",captureWidth:1280,captureHeight:800);
            InputSystem.Update();Set(pads[0].rightStick,new Vector2(0,.7f));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            Set(pads[0].rightStick,Vector2.zero);InputSystem.Update();Press(Mouse.current.rightButton);InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);Release(Mouse.current.rightButton);InputSystem.Update();
            Set(pads[0].rightStick,new Vector2(0,.7f));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            Call(app,"Pause",true);Call(app,"UpdateFrame");AssertCursor(false);
            Set(pads[0].rightStick,Vector2.zero);InputSystem.Update();Call(app,"Pause",false);Call(app,"UpdateFrame");
            Set(pads[0].rightStick,new Vector2(0,.7f));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            Call(app,"OnApplicationFocus",false);Assert.IsTrue(Cursor.visible);Assert.IsFalse(Get<bool>(app,"systemCursorHidden"));
            Set(pads[0].rightStick,Vector2.zero);InputSystem.Update();Call(app,"OnApplicationFocus",true);Call(app,"Pause",false);Call(app,"UpdateFrame");
            Set(pads[0].rightStick,new Vector2(0,.7f));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            InputSystem.RemoveDevice(pads[0]);Call(app,"UpdateFrame");AssertCursor(false);
            InputSystem.AddDevice(pads[0]);Set(pads[0].rightStick,Vector2.zero);InputSystem.Update();Call(app,"Pause",false);Call(app,"UpdateFrame");
            Set(pads[0].rightStick,new Vector2(0,.7f));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(true);
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
            UnityEngine.Object.DestroyImmediate(host);host=null;Assert.IsTrue(Cursor.visible,"Destroy restores hardware cursor even on a disabled bootstrap");
        }
        [UnityTest] public IEnumerator OtherLocalPadDoesNotHideMousePlayersCursor()
        {
            Cursor.visible=true;yield return StartOrdinary(2,configureRoster:draft=>draft.Participants[0].DeviceId=0);
            Set(pads[1].rightStick,new Vector2(0,.7f));InputSystem.Update();Call(app,"UpdateFrame");
            Assert.IsFalse(Get<bool>(app,"systemCursorHidden"));Assert.IsTrue(Cursor.visible);
            Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(Seats[1],"battleCursorVisual").style.display.value);
            Set(pads[0].rightStick,new Vector2(0,.7f));InputSystem.Update();Call(app,"UpdateFrame");AssertCursor(false);
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
            foreach(float padding in new[]{frame.style.paddingLeft.value.value,frame.style.paddingRight.value.value,frame.style.paddingTop.value.value,frame.style.paddingBottom.value.value})Assert.AreEqual(8,padding);
            // Pixel rounding at fractional panel scale can move each rendered edge by a pixel.
            float pixel=1/host.GetComponent<UIDocument>().panelSettings.scale;
            float extentTolerance=2*pixel+.01f;
            Assert.That(frame.worldBound.width-map.worldBound.width,Is.EqualTo(20).Within(extentTolerance));Assert.That(frame.worldBound.height-map.worldBound.height,Is.EqualTo(20).Within(extentTolerance));
            Assert.That(map.worldBound.xMin-frame.worldBound.xMin,Is.EqualTo(10).Within(pixel+.01f));Assert.That(map.worldBound.yMin-frame.worldBound.yMin,Is.EqualTo(10).Within(pixel+.01f));
            foreach(float border in new[]{frame.style.borderLeftWidth.value,frame.style.borderRightWidth.value,frame.style.borderTopWidth.value,frame.style.borderBottomWidth.value})Assert.AreEqual(2,border);
            foreach(var color in new[]{frame.resolvedStyle.borderLeftColor,frame.resolvedStyle.borderRightColor,frame.resolvedStyle.borderTopColor,frame.resolvedStyle.borderBottomColor})Assert.AreEqual(1,color.a);
            Assert.Greater(overlay.resolvedStyle.backgroundColor.a,.9);
            Assert.AreEqual(map.style.width.value.value,map.style.height.value.value,"Logical tactical map dimensions are square");
            Assert.That(map.worldBound.width,Is.EqualTo(map.worldBound.height).Within(pixel+.01f));
            Inside(overlay.worldBound,map.worldBound,"tactical map");
            Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(seat,"bottom").style.display.value);Assert.AreEqual(DisplayStyle.None,Get<PlayableMapSurface>(seat,"compactMap").parent.style.display.value);
            var table=overlay.Q("tactical-team");
            if(Get<bool>(seat,"spectatorMode")||Get<bool>(seat,"inLobby"))
            {
                Assert.AreEqual(DisplayStyle.None,table.style.display.value);
                Assert.That(frame.worldBound.center.x,Is.EqualTo(root.worldBound.center.x).Within(pixel+.01f));
                Assert.That(frame.worldBound.center.y,Is.EqualTo(root.worldBound.center.y).Within(pixel+.01f));
                return;
            }
            Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(seat,"top").style.display.value,"The table owns the tactical economy");
            Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(seat,"armyComposition").style.display.value,"The table owns the tactical army count");
            Inside(root.worldBound,table.worldBound,"team table");
            Assert.False(frame.worldBound.Overlaps(table.worldBound),"Team panel must leave the entire map usable");
            var diagnostics=Get<VisualElement>(app,"panelRoot");
            foreach(var counterName in new[]{"device-fps","device-tps"})
            {
                var counter=diagnostics.Q<Label>(counterName);
                if(counter?.resolvedStyle.display!=DisplayStyle.Flex)continue;
                Assert.False(counter.worldBound.Overlaps(table.worldBound),"Counters must clear the RT toggle");
                Assert.False(counter.worldBound.Overlaps(frame.worldBound),"Counters must clear the tactical map");
            }
        }
        [UnityTest] public IEnumerator SharedMinimapReturnsOnlyAfterAllTacticalScreensClose()
        {
            yield return StartOrdinary(2);var first=Seats[0];var second=Seats[1];var minimap=Get<VisualElement>(app,"sharedMapFrame");
            Call(first,"ToggleMap");Call(second,"ToggleMap");Call(app,"UpdateFrame");Assert.AreEqual(DisplayStyle.None,minimap.style.display.value);
            var terrain=Get<PlayableTacticalMapTerrain>(first,"tacticalTerrain");
            Assert.IsTrue(terrain.IsOverhead,"The normal split-screen route uses the prepared scene image.");
            Assert.Greater(terrain.Texture.width,Get<PlayableMapTerrain>(first,"mapTerrain").Texture.width);
            Assert.IsNull(Get<PlayableTacticalMapTerrain>(second,"tacticalTerrain"),"Local seats share static terrain.");
            var fogA=Get<PlayableTacticalMapFog>(first,"tacticalFog").Texture;
            var fogB=Get<PlayableTacticalMapFog>(second,"tacticalFog").Texture;
            Assert.AreNotSame(fogA,fogB,"Hostile local seats need separate fog outputs.");
            Assert.IsTrue(fogA.IsCreated()&&fogB.IsCreated(),"Both TABs render on their first open.");
            yield return Capture("two-tactical-screens");AssertTacticalScreen(first);AssertTacticalScreen(second);
            yield return Capture("two-tactical-screens-1080",captureWidth:1920,captureHeight:1080,captureChecks:()=>
            {
                // This additional fractional-scale viewport checks placement; the full
                // fixed border/padding contract remains above at its original resolution.
                foreach(var seat in new[]{first,second})
                {
                    AssertTacticalScreen(seat);
                }
            });
            Call(first,"ToggleMap");Call(app,"UpdateFrame");Assert.AreEqual(DisplayStyle.None,minimap.style.display.value);Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(first,"bottom").style.display.value);
            Call(second,"ToggleMap");Call(app,"UpdateFrame");Assert.AreEqual(DisplayStyle.Flex,minimap.style.display.value);Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(second,"bottom").style.display.value);
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [UnityTest] public IEnumerator TacticalTerrainRebindsAfterChangingMatchMap()
        {
            yield return StartOrdinary(1);
            Call(app,"ToggleMap");
            yield return null;
            var originalTerrain=Get<PlayableTacticalMapTerrain>(app,"tacticalTerrain").Texture;
            Assert.IsTrue(Get<PlayableTacticalMapFog>(app,"tacticalFog").Texture.IsCreated());
            var originalRuntime=Runtime;
            Call(app,"ReturnToLobby");
            yield return Wait(()=>originalRuntime.IsStopped&&Get<bool>(app,"inLobby"),"return to lobby");

            var lobby=Get<VisualElement>(app,"lobbyScreen");
            lobby.Q<DropdownField>("lobby-map-choice").value=NullDockMap.DisplayName;
            Call(app,"LaunchLobbyMatch");
            yield return Wait(()=>Runtime!=null&&!Get<bool>(app,"preparing"),"second map");
            Call(app,"UpdateFrame");
            Assert.IsInstanceOf<NullDockMap>(Get<PlayableProfile>(app,"profile").AuthoredMap);
            Assert.AreNotSame(originalTerrain,Get<PlayableTacticalMapTerrain>(app,"tacticalTerrain").Texture);

            Call(app,"ToggleMap");
            yield return null;
            var fog=Get<PlayableTacticalMapFog>(app,"tacticalFog").Texture;
            Assert.IsTrue(fog.IsCreated());
            var map=Get<PlayableMapSurface>(app,"tacticalMap");
            var view=Get<PlayableSnapshot>(app,"view");
            var home=view.ActiveProfile.Headquarters(view.Owner);
            var ground=map.Ground(map.Point(home));
            Assert.AreEqual(home.X,ground.X,.001);
            Assert.AreEqual(home.Z,ground.Z,.001);
            yield return Capture("null-dock-tactical-screen",captureWidth:1280,captureHeight:800,
                captureChecks:()=>AssertTacticalScreen(app));
            Call(app,"ToggleMap");
            yield return Capture("null-dock-compact-with-fog",captureWidth:1280,captureHeight:800);
            Call(app,"ToggleMap");
            Assert.AreSame(fog,Get<PlayableTacticalMapFog>(app,"tacticalFog").Texture);
            Runtime.RequestStop();
            yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [UnityTest] public IEnumerator TacticalMapUsesLeftStickWithoutHintsOrCameraPan()
        {
            yield return StartOrdinary(1);var seat=app;var pad=pads[0];
            Call(seat,"ToggleMap");Call(app,"UpdateFrame");
            Assert.True(Get<bool>(seat,"mapOpen"));
            var overlay=Get<VisualElement>(seat,"tacticalOverlay");
            IEnumerable<Label> VisibleMapLabels()=>overlay.Q("tactical-map-background").Query<Label>().ToList()
                .Where(label=>label.resolvedStyle.display!=DisplayStyle.None);
            Assert.IsEmpty(VisibleMapLabels(),"Map has no visible keyboard or controller hint row.");
            var camera=Get<Camera>(seat,"cameraView");var cameraPosition=camera.transform.position;
            PadCursor(seat,default);Set(pad.leftStick,new Vector2(.7f,.7f));InputSystem.Update();
            yield return null;Call(app,"UpdateFrame");
            var moved=Get<NavPoint>(seat,"padCursorGround");Assert.Greater(moved.X,0);Assert.Greater(moved.Z,0,"Up on the map stick must move north/top");
            Assert.AreEqual(cameraPosition,camera.transform.position,"Left stick moves the map cursor without panning the world camera.");
            Set(pad.leftStick,Vector2.zero);Set(pad.rightStick,new Vector2(-.7f,-.7f));InputSystem.Update();
            yield return null;Call(app,"UpdateFrame");Assert.AreEqual(moved,Get<NavPoint>(seat,"padCursorGround"),"Right stick does not move the tactical map cursor.");
            Assert.AreEqual(cameraPosition,camera.transform.position);
            Set(pad.rightStick,Vector2.zero);InputSystem.Update();
            yield return Capture("tactical-screen",captureWidth:1280,captureHeight:800);
            AssertTacticalScreen(seat);
            Put(seat,"battleRally",true);Call(seat,"UpdateMaps");Assert.IsEmpty(VisibleMapLabels(),"Rally mode does not restore key hints on the map.");Put(seat,"battleRally",false);
            Call(seat,"ToggleMap");Call(app,"UpdateFrame");Assert.False(Get<bool>(seat,"mapOpen"));
            Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(seat,"bottom").style.display.value);Assert.AreEqual(DisplayStyle.Flex,Get<PlayableMapSurface>(seat,"compactMap").parent.style.display.value);
            Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(seat,"armyComposition").style.display.value);
            Set(pad.rightStick,new Vector2(0,.7f));InputSystem.Update();
            yield return null;Call(app,"UpdateFrame");Assert.AreEqual(camera.pixelRect.center,Get<Vector2>(seat,"battleCursor"),"World aim is centered after closing the map.");
            Set(pad.rightStick,Vector2.zero);InputSystem.Update();Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        private static void AssertTacticalUnitCounts(VisualElement row,PlayableSnapshot participant)
        {
            foreach(var kind in new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk})
            {
                var cell=row.Q("tactical-unit-"+kind);
                int count=participant.Entities.Count(e=>e.Owner==participant.Owner&&e.Kind==kind&&e.Health>0);
                Assert.AreEqual(count>0?DisplayStyle.Flex:DisplayStyle.None,cell.style.display.value,kind.ToString());
                Assert.AreEqual(count.ToString(),cell.Q<Label>().text);
                Assert.AreSame(DockArtwork.TextureFor(RadialActionPresentation.UnitArtwork(kind,false)),cell.Q<ModelPortrait>().Texture);
                if(count>0)Inside(row.worldBound,cell.worldBound,"visible unit group");
            }
        }

        [UnityTest] public IEnumerator TacticalTeamRosterKeepsEliminatedPlayersWithSpectatorEffect()
        {
            yield return StartOrdinary(3,configureRoster:roster=>{for(int i=0;i<roster.Participants.Count;i++)roster.Participants[i].Team=i<2?1:2;});
            var self=Seats[0];var ally=Get<PlayableSnapshot>(Seats[1],"view");var enemy=Get<PlayableSnapshot>(Seats[2],"view");
            Call(self,"ToggleMap");yield return null;Call(app,"UpdateFrame");
            var list=Get<ScrollView>(self,"tacticalTeamRows");
            Assert.AreEqual(1,list.Q("tactical-player-"+ally.OwnerId).Q("tactical-player-status").style.opacity.value);
            Assert.AreEqual(1,list.Q("tactical-player-"+enemy.OwnerId).Q<Label>("tactical-player-name").style.opacity.value);
            yield return Capture("team-roster-before-elimination",captureWidth:1920,captureHeight:1200,captureChecks:()=>AssertTacticalScreen(self));
            Assert.True(Runtime.RequestSurrender(ally.OwnerId));Assert.True(Runtime.RequestSurrender(enemy.OwnerId));
            yield return Wait(()=>Runtime.OfflineFrame.Views[ally.OwnerId].OwnerEliminated&&Runtime.OfflineFrame.Views[enemy.OwnerId].OwnerEliminated,"ally and enemy surrender");
            Call(app,"UpdateFrame");
            var frame=Get<OfflinePresentationFrame>(self,"ownerFrame");
            CollectionAssert.Contains(frame.EliminatedOwners,ally.OwnerId);CollectionAssert.Contains(frame.EliminatedOwners,enemy.OwnerId);
            var rows=list.contentContainer.Children().Where(row=>row.style.display.value==DisplayStyle.Flex).ToArray();
            Assert.AreEqual(Get<PlayableSnapshot>(self,"view").Participants.Count,rows.Length,"Eliminated players must remain in their teams");
            var allied=list.Q("tactical-player-"+ally.OwnerId);var hostile=list.Q("tactical-player-"+enemy.OwnerId);
            var gray=new Color(.13f,.15f,.16f,.95f);
            foreach(var status in new[]{allied.Q("tactical-player-status"),(VisualElement)hostile.Q<Label>("tactical-player-name")})
            {
                Assert.AreEqual(.5f,status.style.opacity.value);Assert.AreEqual(gray,status.style.backgroundColor.value);
                Assert.True(status.ClassListContains("roster-eliminated"));
            }
            Assert.IsNull(hostile.Q("tactical-player-population"));Assert.IsNull(hostile.Q("tactical-player-abilities"));
            Assert.IsEmpty(hostile.Query<ModelPortrait>().ToList());
            Assert.AreEqual(1,list.Q("tactical-player-"+Get<PlayableSnapshot>(self,"view").OwnerId).Q("tactical-player-status").style.opacity.value);
            yield return Capture("team-roster-eliminated",captureWidth:1920,captureHeight:1200,captureChecks:()=>{
                AssertTacticalScreen(self);
                Assert.AreEqual(1,list.Q("tactical-allies-heading").resolvedStyle.opacity);
                Assert.AreEqual(1,hostile.Q("tactical-opponent-team").resolvedStyle.opacity);
            });
        }
        [UnityTest] public IEnumerator TacticalTeamRosterFiltersSortsAndTogglesPerSeat()
        {
            yield return StartOrdinary(2,allied:true,configureRoster:roster=>{for(int i=2;i<roster.Participants.Count;i++)roster.Participants[i].Team=2+i%2;});
            var first=Seats[0];var second=Seats[1];
            Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"pause to seed ability unlock");
            var secondOwner=Get<PlayableSnapshot>(second,"view");
            var hostile=Runtime.OfflineFrame.Views.Values.First(p=>p.Team!=secondOwner.Team);
            var domain=typeof(PlayableRuntime).GetField("domain",F).GetValue(Runtime);
            domain.GetType().GetMethod("CreditOrbital",F).Invoke(domain,new object[]{(PlayableOwner?)secondOwner.Owner,hostile.Owner,250d});
            Runtime.RequestPause(false);yield return Wait(()=>Get<PlayableSnapshot>(second,"view").Orbital.PendingLevel==1,"unlock ally ability");
            Assert.True((bool)Call(second,"OpenOrbital",true));
            Call(second,"SubmitOrbital",true,1);yield return Wait(()=>Get<PlayableSnapshot>(second,"view").Orbital.Abilities.Count==1,"choose ally scan");
            yield return null;Call(app,"UpdateFrame");
            Tap(pads[0],pads[0].selectButton);Tap(pads[1],pads[1].selectButton);
            yield return null;Call(app,"UpdateFrame");
            foreach(var seat in Seats)
            {
                AssertTacticalScreen(seat);
                var own=Get<PlayableSnapshot>(seat,"view");
                var frame=Get<OfflinePresentationFrame>(seat,"ownerFrame");
                var visible=Get<ScrollView>(seat,"tacticalTeamRows").contentContainer.Children().Where(r=>r.style.display.value==DisplayStyle.Flex).ToArray();
                var rows=visible.Where(r=>!r.ClassListContains("tactical-opponent")).ToArray();
                var expected=own.Participants.Where(p=>p.Team==own.Team).Select(p=>frame.Views[p.Id])
                    .OrderByDescending(p=>p.Owner==own.Owner).ThenByDescending(p=>p.Population.Living).ToArray();
                Assert.Greater(expected.Length,1);
                Assert.Less(expected.Length,own.Participants.Count);
                CollectionAssert.AreEqual(expected.Select(p=>"tactical-player-"+p.OwnerId),rows.Select(r=>r.name));
                Assert.That(rows[0].Q<Label>("tactical-player-name").text,Does.StartWith("Вы"));
                for(int i=0;i<rows.Length;i++)
                {
                    Assert.AreEqual(expected[i].Population.Living+"/"+expected[i].Population.Capacity,rows[i].Q<Label>("tactical-player-population").text);
                    AssertTacticalUnitCounts(rows[i],expected[i]);
                    Assert.AreEqual(expected[i].Owner==secondOwner.Owner,rows[i].Q("tactical-ability-1")!=null);
                }
                var opponents=visible.Where(r=>r.ClassListContains("tactical-opponent")).ToArray();
                var publicTeams=own.Participants.Where(p=>p.Team!=own.Team).GroupBy(p=>p.Team).ToArray();
                Assert.Greater(publicTeams.Length,1,"Fixture must exercise distinct hostile team groups");
                CollectionAssert.AreEqual(publicTeams.SelectMany(g=>g).Select(p=>"tactical-player-"+p.Id),opponents.Select(r=>r.name));
                Assert.AreEqual(own.Participants.Count,visible.Length);
                Assert.AreEqual("СОЮЗНИКИ",rows[0].Q("tactical-allies-heading").Q<Label>().text);
                Assert.Less(System.Array.IndexOf(visible,rows.Last()),System.Array.IndexOf(visible,opponents.First()));
                int index=0;
                foreach(var team in publicTeams)
                {
                    var shade=opponents[index].style.backgroundColor.value;
                    int inTeam=0;
                    foreach(var member in team)
                    {
                        var enemy=opponents[index++];
                        Assert.AreEqual(team.Key,enemy.userData);
                        Assert.AreEqual(shade,enemy.style.backgroundColor.value);
                        Assert.AreEqual(inTeam++==0?DisplayStyle.Flex:DisplayStyle.None,enemy.Q("tactical-opponent-team").style.display.value);
                        Assert.AreEqual("КОМАНДА "+team.Key,enemy.Q<Label>("tactical-opponent-team").text);
                        Assert.AreEqual(Get<NativeLobbyConfiguration>(seat,"matchSetup").ParticipantName(member.LogicalPlayer-1),enemy.Q<Label>("tactical-player-name").text);
                        Assert.IsNull(enemy.Q("tactical-player-population"));
                        Assert.IsNull(enemy.Q("tactical-player-abilities"));
                        Assert.IsEmpty(enemy.Query<ModelPortrait>().ToList());
                        CollectionAssert.AreEquivalent(new[]{"tactical-opponent-team","tactical-player-name"},enemy.Query<Label>().ToList().Select(label=>label.name));
                    }
                }
                // A frame containing allied perspectives only must still render all public opponents.
                var alliedViews=frame.Views.Where(pair=>pair.Value.Team==own.Team).ToDictionary(pair=>pair.Key,pair=>pair.Value);
                var limited=(OfflinePresentationFrame)typeof(OfflinePresentationFrame).GetConstructors(F).Single().Invoke(new object[]{alliedViews,System.Array.Empty<OfflineReceipt>(),0L,null});
                Put(seat,"ownerFrame",limited);Call(seat,"RefreshTacticalTeam");
                CollectionAssert.AreEqual(publicTeams.SelectMany(g=>g).Select(p=>"tactical-player-"+p.Id),Get<ScrollView>(seat,"tacticalTeamRows").contentContainer.Children().Where(r=>r.style.display.value==DisplayStyle.Flex&&r.ClassListContains("tactical-opponent")).Select(r=>r.name));
                Put(seat,"ownerFrame",frame);Call(seat,"RefreshTacticalTeam");
            }

            var map=Get<PlayableMapSurface>(first,"tacticalMap");
            float openWidth=0;
            yield return Capture("team-table-open",captureWidth:1920,captureHeight:1080,captureChecks:()=>
            {
                AssertTacticalScreen(first);
                openWidth=map.worldBound.width;
            });
            var point=new NavPoint(4,-6);
            PadCursor(first,point);
            Tap(pads[0],pads[0].rightTrigger);
            yield return null;Call(app,"UpdateFrame");
            Assert.True(Get<bool>(first,"tacticalTeamCollapsed"));
            Assert.False(Get<bool>(second,"tacticalTeamCollapsed"));
            Assert.AreEqual(point,Get<NavPoint>(first,"padCursorGround"));
            Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(first,"tacticalTeamBody").style.display.value);
            yield return Capture("team-table-collapsed",captureWidth:1920,captureHeight:1080,captureChecks:()=>
            {
                AssertTacticalScreen(first);
                Assert.Greater(map.worldBound.width,openWidth,"RT should enlarge the map in this split viewport");
            });
            Set(pads[0].leftStick,Vector2.right);InputSystem.Update();
            yield return null;Call(app,"UpdateFrame");
            Assert.Greater(Get<NavPoint>(first,"padCursorGround").X,point.X);
            Set(pads[0].leftStick,Vector2.zero);InputSystem.Update();
            Tap(pads[0],pads[0].rightTrigger);
            Assert.False(Get<bool>(first,"tacticalTeamCollapsed"));
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [UnityTest] public IEnumerator OrdinaryPadCommandsMapAndNeutralFocusUseTheSameOwnerSequence()
        {
            yield return StartOrdinary(1);var seat=app;var v=Get<PlayableSnapshot>(seat,"view");var own=v.Entities.First(e=>e.Owner==v.Owner);PadCursor(seat,own.Position);Call(app,"UpdateFrame");Tap(pads[0],pads[0].buttonSouth);CollectionAssert.AreEqual(new[]{own.Id},Selection(seat));
            var point=new NavPoint(own.Position.X+4,own.Position.Z+4);PadCursor(seat,point);Tap(pads[0],pads[0].buttonWest);long moved=Get<long>(seat,"sequence");Assert.Greater(moved,0);
            yield return Wait(()=>Runtime.OfflineFrame.Receipts.Any(r=>r.Receipt.OwnerId=="player-1"&&r.Receipt.Sequence==moved&&r.Receipt.Status==PlayableCommandStatus.Applied),"pad X move");
            Tap(pads[0],pads[0].buttonEast);Assert.AreEqual(moved+1,Get<long>(seat,"sequence"));
            ButtonFrame(pads[0],pads[0].buttonEast,true);yield return new WaitForSecondsRealtime(.35f);Call(app,"UpdateFrame");ButtonFrame(pads[0],pads[0].buttonEast,false);long held=Get<long>(seat,"sequence");Assert.AreEqual(moved+2,held);
            yield return Wait(()=>Runtime.OfflineFrame.Receipts.Any(r=>r.Receipt.Sequence==held&&r.Receipt.Status==PlayableCommandStatus.Applied),"pad B hold");
            Call(app,"UpdateFrame");Assert.True(Get<PlayableSnapshot>(seat,"view").Entities.First(e=>e.Id==own.Id).Held);
            Tap(pads[0],pads[0].selectButton);Assert.True(Get<bool>(seat,"mapOpen"));Call(app,"UpdateFrame");Tap(pads[0],pads[0].buttonSouth);Assert.False(Get<bool>(seat,"mapOpen"));
            Call(app,"UpdateFrame");Tap(pads[0],pads[0].rightShoulder);yield return new WaitForSecondsRealtime(.1f);Tap(pads[0],pads[0].rightShoulder);CollectionAssert.AreEquivalent(Get<PlayableSnapshot>(seat,"view").Entities.Where(e=>e.Owner==v.Owner).Select(e=>e.Id),Selection(seat));
            Call(app,"OnApplicationFocus",false);Assert.True(Get<bool>(app,"paused"));ButtonFrame(pads[0],pads[0].buttonEast,true);Call(app,"OnApplicationFocus",true);Call(app,"Pause",false);Call(app,"UpdateFrame");Assert.AreEqual(held,Get<long>(seat,"sequence"));ButtonFrame(pads[0],pads[0].buttonEast,false);Assert.AreEqual(held,Get<long>(seat,"sequence"));Call(app,"UpdateFrame");Tap(pads[0],pads[0].buttonEast);Assert.AreEqual(held+1,Get<long>(seat,"sequence"));
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [UnityTest] public IEnumerator LocalPauseSettingsRestartResultAndReturnStayWithOrdinaryCoordinator()
        {
            yield return StartOrdinary(2);var secondary=Seats[1];Call(app,"PauseFrom",secondary,true);Call(app,"UpdateFrame");Assert.AreSame(app,Get<PlayableBootstrap>(app,"localPauseOwner"));Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(app,"modal").style.display.value);Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(secondary,"root").style.display.value);
            Tap(pads[0],pads[0].buttonSouth);Assert.True(Get<bool>(app,"paused"),"another pad cannot resume the menu owner");
            Call(app,"OpenPauseSettings");var settings=Get<VisualElement>(app,"childMenu");var speed=settings.Q<Slider>("settings-pad-mapCursorSpeed");Assert.NotNull(speed);float before=speed.value;((Action<int>)speed.userData)(1);Assert.Greater(speed.value,before);Assert.AreEqual(speed.value,new NativeLocalControlSettings().MapCursorSpeed);Assert.AreEqual(before,Get<NativeLocalInputProfile>(app,"battleInputProfile").mapCursorSpeed);
            Get<NativeMenuNavigation>(app,"menuNavigation").Back();Call(app,"UpdateFrame");yield return Capture("ordinary-local-pause");Call(app,"OpenPauseHelp");Assert.NotNull(Resources.Load<Texture2D>("ControlsHelp/field-events-up"));yield return Capture("ordinary-controls-help");var navigation=Get<NativeMenuNavigation>(app,"menuNavigation");navigation.Move(Vector2.down);navigation.Move(Vector2.down);navigation.Activate();Assert.AreSame(Resources.Load<Texture2D>("ControlsHelp/groups"),navigation.Scope.Q<Image>("controls-help-image").image);yield return Capture("ordinary-controls-groups");Get<NativeMenuNavigation>(app,"menuNavigation").Back();
            Call(secondary,"Pause",false);Call(app,"UpdateFrame");long generation=Runtime.Generation;Call(secondary,"Restart");yield return Wait(()=>Runtime.Generation>generation&&!Runtime.IsStopped,"restart");Call(app,"UpdateFrame");Assert.AreEqual(2,Seats.Length);Assert.AreSame(Runtime,Get<PlayableRuntime>(Seats[1],"runtime"));
            Runtime.RequestFinish();yield return Wait(()=>Runtime.Result!=null,"finish");Call(app,"UpdateFrame");Assert.True((bool)typeof(PlayableBootstrap).GetProperty("ResultsVisible",F).GetValue(app));
            Assert.AreEqual(100,Get<VisualElement>(app,"root").style.width.value.value);yield return Capture("ordinary-local-results");
            Call(app,"ReturnToLobby");yield return Wait(()=>Get<bool>(app,"inLobby")&&!Get<bool>(app,"returningToLobby"),"return lobby");Assert.AreEqual(0,Seats.Length);Assert.AreEqual(1,host.GetComponent<UIDocument>().rootVisualElement.Query<VisualElement>("local-seat-hud").ToList().Count);
            Get<NativeLobbyConfiguration>(app,"lobbySetup").Participants[1].Human=false;Call(app,"RebuildRoster");Call(app,"LaunchLobbyMatch");yield return Wait(()=>Runtime!=null&&!Get<bool>(app,"preparing"),"one-human relaunch");Call(app,"UpdateFrame");yield return null;
            Assert.AreEqual(1,Seats.Length);Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(app,"root").Q("tactical-minimap-frame").style.display.value);Assert.AreEqual(0,Get<VisualElement>(app,"armyRegion").style.marginRight.value.value);Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [UnityTest] public IEnumerator PrimaryOwnsPauseAndKeyboardMouseRemainMenuFallback()
        {
            yield return StartOrdinary(2);var secondary=Seats[1];Call(app,"PauseFrom",secondary,true);Call(app,"UpdateFrame");
            Assert.AreSame(app,Get<PlayableBootstrap>(app,"localPauseOwner"));Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(app,"modal").style.display.value);Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(secondary,"root").style.display.value);
            var nav=Get<NativeMenuNavigation>(app,"menuNavigation");Assert.AreSame(pads[0],nav.CurrentGamepad);
            var settingsButton=Get<VisualElement>(app,"root").Q<Button>("pause-settings");settingsButton.Focus();
            using(var key=KeyDownEvent.GetPooled('\0',KeyCode.Return,EventModifiers.None)){key.target=settingsButton;settingsButton.SendEvent(key);}
            using(var key=KeyUpEvent.GetPooled('\0',KeyCode.Return,EventModifiers.None)){key.target=settingsButton;settingsButton.SendEvent(key);}
            Assert.NotNull(Get<VisualElement>(app,"childMenu"),"Keyboard opens settings for a gamepad-bound first human");
            nav.Back();Call(app,"UpdateFrame");settingsButton.Focus();var scope=nav.Scope;Tap(pads[1],pads[1].buttonSouth);Tap(pads[1],pads[1].buttonEast);Assert.AreSame(scope,nav.Scope);Assert.True(Get<bool>(app,"paused"));
            using(var key=KeyDownEvent.GetPooled('\0',KeyCode.Escape,EventModifiers.None)){key.target=settingsButton;settingsButton.SendEvent(key);}
            Call(app,"UpdateFrame");Assert.False(Get<bool>(app,"paused"));Assert.False(Get<PlayableInput>(app,"input").enabled,"Menu fallback does not enable mouse combat input");
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [Test] public void SharedPadRecallPreservesKeyboardTapTimingAndUnitOnlyAssignment()
        {
            var entities=new[]{new KeyboardSelectable(1,0,true,false,0,0),new KeyboardSelectable(2,0,true,false,1,0),new KeyboardSelectable(3,0,true,true,2,0),new KeyboardSelectable(4,0,false,false,3,0)};var groups=new KeyboardControlGroups();
            groups.Apply(1,true,0,new[]{1},entities,450,out _);groups.Apply(9,true,0,new[]{3},entities,450,out _);CollectionAssert.AreEqual(new[]{1,2,3,9},groups.VisibleSlots(entities,3,2));Assert.False(groups.PadActions(new[]{3},entities,true,groups.VisibleSlots(entities,3,2)).Any(s=>s.Enabled));
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
