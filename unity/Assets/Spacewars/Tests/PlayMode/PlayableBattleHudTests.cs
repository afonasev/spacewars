using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Input;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class PlayableBattleHudTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    GameObject host;PlayableBootstrap hud;
    T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(hud);
    void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(hud,value);
    void Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethods(Flags).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(hud,args);
    [SetUp] public void Setup(){host=new GameObject("battle HUD test");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;Set("profile",PlayableProfile.Default);Call("CreateHud");}
    [TearDown] public void Cleanup(){Get<PlayableRuntime>("runtime")?.RequestStop();UnityEngine.Object.DestroyImmediate(host);}
    PlayableSnapshot Snapshot(PlayableEntitySnapshot[] units=null,PlayableBuildingSnapshot[] buildings=null,int credits=1000)=>new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,null,units??Array.Empty<PlayableEntitySnapshot>(),buildings??Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,population:new PlayablePopulationSnapshot(4,3,100));
    [Test] public void RoutineStatusesStayQuietButErrorsAndDisconnectStayVisible()
    {
        foreach(var status in new[]{PlayableCommandStatus.Accepted,PlayableCommandStatus.Applied,PlayableCommandStatus.Cancelled})
            Assert.AreEqual("",typeof(PlayableBootstrap).GetMethod("Friendly",Flags).Invoke(hud,new object[]{status}));
        Set("view",Snapshot());Set("notice","");Call("UpdateHud");Assert.AreEqual(DisplayStyle.None,Get<Label>("noticeLabel").style.display.value);
        foreach(var text in new[]{"Недостаточно кредитов","Геймпад отключён. Подключите устройство и продолжите матч."})
        {Set("notice",text);Call("UpdateHud");Assert.AreEqual(text,Get<Label>("noticeLabel").text);Assert.AreEqual(DisplayStyle.Flex,Get<Label>("noticeLabel").style.display.value);}
    }
    [Test] public void PaintedMapRetainsTerrainAndVisibilityWithoutMutatingFog()
    {
        using var a=new PlayableFogMask(PlayableProfile.Default);using var b=new PlayableFogMask(PlayableProfile.Default);
        var vision=new PlayableVision(0,64,64,4,PlayableProfile.Default.FogEdgeFeather);vision.Refresh(new[]{new VisionSource(new NavPoint(-20,0),12)},Array.Empty<KnownBuilding>());a.Update(vision.Snapshot(),1);
        var other=new PlayableVision(1,64,64,4,PlayableProfile.Default.FogEdgeFeather);other.Refresh(new[]{new VisionSource(new NavPoint(20,0),12)},Array.Empty<KnownBuilding>());b.Update(other.Snapshot(),1);
        var beforeA=a.Texture.GetPixels32();var beforeB=b.Texture.GetPixels32();using var map=new PlayableMapTerrain(PlayableProfile.Default);
        Assert.True(map.UpdateUnion(new[]{a,b}));Assert.False(map.UpdateUnion(new[]{a,b}),"stable frame does not upload");
        var colors=map.Texture.GetPixels32();Assert.Greater(colors.Distinct().Count(),2);Assert.True(colors.Any(c=>c.r>30||c.g>30||c.b>30),"visible landscape contrast");
        CollectionAssert.AreEqual(beforeA,a.Texture.GetPixels32());CollectionAssert.AreEqual(beforeB,b.Texture.GetPixels32());
    }
    [Test] public void ArmyPopoverUsesWholeOwnLivingArmyAndFocus()
    {
        var units=new[]{new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Explorer,default,100,false,0,0,0),new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,default,100,false,0,0,0),new PlayableEntitySnapshot(3,PlayableOwner.Enemy,PlayableEntityKind.Shkval,default,100,false,0,0,0)};
        Get<HashSet<int>>("selection").Add(1);Set("view",Snapshot(units));Call("UpdateHud");
        var button=Get<Button>("hudFocusButton");button.Focus();
        Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>("armyComposition").style.display.value);
        var counts=Get<Label[]>("armyCounts");Assert.AreEqual("1",counts[0].text);Assert.AreEqual("1",counts[1].text);Assert.AreEqual(DisplayStyle.None,counts[2].style.display.value);
        Assert.AreEqual("4 / 100",button.text);Assert.AreEqual(1,Get<VisualElement>("selectionRoster").childCount);
        Get<VisualElement>("root").Focus();Assert.AreEqual(DisplayStyle.None,Get<VisualElement>("armyComposition").style.display.value);
    }
    [Test] public void TacticalCompositionStaysVisibleWithoutHoverThenRestoresPopover()
    {
        Set("view",Snapshot(new[]{new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,default,100,false,0,0,0),new PlayableEntitySnapshot(2,PlayableOwner.Enemy,PlayableEntityKind.Explorer,default,100,false,0,0,0)}));
        var layerOrder=Get<VisualElement>("root").Children().ToArray();
        Call("ToggleMap");Call("ShowArmyComposition",false);Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>("armyComposition").style.display.value);
        var counts=Get<Label[]>("armyCounts");CollectionAssert.AreEqual(new[]{"0","1","0"},counts.Select(label=>label.text));Assert.True(counts.All(label=>label.style.display.value==DisplayStyle.Flex));
        Call("CloseMap");CollectionAssert.AreEqual(layerOrder,Get<VisualElement>("root").Children().ToArray(),"Closing restores the ordinary HUD/modal layer order.");Assert.AreEqual(DisplayStyle.None,Get<VisualElement>("armyComposition").style.display.value);Assert.AreEqual(DisplayStyle.None,counts[0].style.display.value);
        Get<Button>("hudFocusButton").Focus();Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>("armyComposition").style.display.value);Get<VisualElement>("root").Focus();Assert.AreEqual(DisplayStyle.None,Get<VisualElement>("armyComposition").style.display.value);
    }
    [Test] public void RingKeepsRepeatWhenPurchaseUnavailableAndBlocksStaleAction()
    {
        var lifecycle=new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false);
        var b=new PlayableBuildingSnapshot(7,PlayableOwner.Player,PlayableBuildingKind.Factory,default,250,1,0,0,default,lifecycle:lifecycle);
        Set("view",Snapshot(buildings:new[]{b},credits:0));Get<HashSet<int>>("selection").Add(7);Call("UpdateHud");
        var actions=Get<OfflinePadAction[]>("battleActions");var production=actions.First(a=>a.Sector.Hold=="repeat");Assert.IsNull(production.Command);Assert.NotNull(production.HoldCommand);
        var settings=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);var gestures=new OfflinePadGestures();gestures.SetMode("buildingWheel");
        gestures.StepDetailed(0,0,true,true,settings,false,production.Sector);gestures.StepDetailed(1,1,true,true,settings,false,production.Sector);
        Assert.AreEqual("repeat",gestures.StepDetailed(settings.holdMs+2,1,true,true,settings,false,production.Sector).Single().Kind);
        Assert.IsEmpty(gestures.StepDetailed(settings.holdMs+3,0,true,true,settings,false,production.Sector),"Hold release must not emit a tap purchase.");
        gestures.StepDetailed(settings.holdMs+4,1,true,true,settings,false,null);Assert.IsEmpty(gestures.StepDetailed(settings.holdMs+5,0,true,true,settings,false,null),"Controller neutral does not execute a sector.");
        var ring=Get<OrbitalBattleRing>("battleRing");Assert.AreEqual(DisplayStyle.Flex,ring.style.display.value);
        int activated=0;ring.InvokeAction=(id,repeat)=>{Assert.AreEqual(production.Sector.Id,id);Assert.False(repeat);activated++;};var control=ring.Q<Button>("radial-"+production.Sector.Id);control.Focus();
        using(var submit=NavigationSubmitEvent.GetPooled()){submit.target=control;control.SendEvent(submit);}Assert.AreEqual(1,activated,"Focused radial button submits exactly one ordinary activation.");
        Set("runtime",new PlayableRuntime(PlayableProfile.Default,1,7));Set("generation",1L);Set("view",Snapshot(buildings:Array.Empty<PlayableBuildingSnapshot>()));Call("ExecuteBattleAction",production.Sector.Id,false,false);
        Assert.AreEqual(0,Get<long>("sequence"),"A vanished building cannot submit its old action.");
    }
    [UnityTest] public IEnumerator CircularActionsStayInsideCompactViewport()
    {
        var root=Get<VisualElement>("root");root.style.width=800;root.style.height=620;
        var lifecycle=new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false);
        Set("view",Snapshot(buildings:new[]{new PlayableBuildingSnapshot(7,PlayableOwner.Player,PlayableBuildingKind.Factory,default,250,1,0,0,default,lifecycle:lifecycle)}));Get<HashSet<int>>("selection").Add(7);
        Call("UpdateHud");yield return null;yield return null;Call("UpdateHud");yield return null;
        foreach(var button in Get<OrbitalBattleRing>("battleRing").Query<Button>().ToList()){Assert.GreaterOrEqual(button.worldBound.xMin,0);Assert.LessOrEqual(button.worldBound.xMax,800);Assert.GreaterOrEqual(button.worldBound.yMin,0);Assert.LessOrEqual(button.worldBound.yMax,620);}
        Assert.LessOrEqual(Get<OrbitalBattleRing>("battleRing").worldBound.yMax,Get<VisualElement>("armyRegion").worldBound.yMin+1);
    }
    [Test] public void PaintedSectorsHaveExclusivePointerTargets()
    {
        const float size=280;
        for(int count=1;count<=7;count++)
        {
            for(int i=0;i<count;i++)
            {
                double angle=i*Math.PI*2/count;
                var point=new Vector2(size/2+(float)Math.Sin(angle)*size*.33f,size/2-(float)Math.Cos(angle)*size*.33f);
                Assert.True(OrbitalBattleRing.SectorContains(point,size,i,count));
                Assert.AreEqual(1,Enumerable.Range(0,count).Count(n=>OrbitalBattleRing.SectorContains(point,size,n,count)),"Painted target must pick one sector only.");
            }
            Assert.False(OrbitalBattleRing.SectorContains(new Vector2(size/2,size/2),size,0,count),"Centre blocks world input without activating a sector.");
            Assert.False(OrbitalBattleRing.SectorContains(Vector2.zero,size,0,count),"Square corners are outside the disc.");
        }
    }
    [Test] public void ApprovedIconsKeepSemanticMappingsAndQueueStates()
    {
        var names=new[]{"tank","explorer","shkval","headquarters","outpost","mine","factory","refinery","science","rally","repair","sale","upgrade","chassis","assault","guidance","army"};
        foreach(var name in names)Assert.NotNull(OrbitalGlyph.TextureFor(name),name);
        Assert.AreEqual(17,names.Select(OrbitalGlyph.TextureFor).Distinct().Count());
        Assert.AreSame(OrbitalGlyph.TextureFor("sale"),OrbitalGlyph.TextureFor("credits"));
        Assert.AreSame(OrbitalGlyph.TextureFor("sale"),OrbitalGlyph.TextureFor("income"));
        foreach(PlayableBuildingKind kind in Enum.GetValues(typeof(PlayableBuildingKind)))
        {
            var glyph=OrbitalPrecision.BuildingGlyph(kind);
            Assert.AreNotEqual("building",glyph,kind.ToString());
            Assert.AreEqual(glyph,OrbitalPrecision.Glyph("build:1:2:"+kind));
            var building=new PlayableBuildingSnapshot(7,PlayableOwner.Player,kind,default,250,1,0,0,default);
            Set("view",Snapshot(buildings:new[]{building}));Get<HashSet<int>>("selection").Add(7);Call("UpdateHud");
            Assert.AreEqual(glyph,Get<OrbitalGlyph>("contextIcon").Kind);
            var ring=Get<OrbitalBattleRing>("battleRing");
            ring.Set(Array.Empty<OfflinePadAction>(),"localized title",contextGlyph:glyph);
            Assert.AreEqual(glyph,ring.Children().OfType<OrbitalGlyph>().Single().Kind);
        }
        var actions=new[]{"rally","7:sale","7:repair","7:upgrade","7:tank","7:explorer","7:shkval","7:tank-chassis","7:explorer-assault-guns","7:shkval-guidance"};
        var expected=new[]{"rally","sale","repair","upgrade","tank","explorer","shkval","chassis","assault","guidance"};
        CollectionAssert.AreEqual(expected,actions.Select(OrbitalPrecision.Glyph).ToArray());
        Assert.AreEqual("assault",OrbitalPrecision.ResearchGlyph(PlayableResearchKind.ExplorerAssaultGuns));
        var card=new Button();OrbitalPrecision.QueueCard(card,"assault",.4,true,true);var icon=card.Q<OrbitalGlyph>();
        Assert.AreEqual("assault",icon.Kind);Assert.AreEqual(.4,icon.Progress);Assert.AreEqual(DisplayStyle.Flex,card.Q<Label>("repeat-badge").style.display.value);
        OrbitalPrecision.QueueCard(card,"guidance",-1,true);Assert.AreSame(icon,card.Q<OrbitalGlyph>());Assert.AreEqual(-1,icon.Progress);Assert.AreEqual(DisplayStyle.None,card.Q<Label>("repeat-badge").style.display.value);
        OrbitalPrecision.QueueCard(card,"science",-1,false);Assert.AreEqual(DisplayStyle.None,icon.style.display.value);
        icon.Set("sale",OrbitalTheme.Cyan);Assert.AreEqual(OrbitalTheme.Cyan,icon.Tint);Assert.AreEqual(ScaleMode.ScaleToFit,icon.Q<Image>().scaleMode);Assert.AreEqual("glyph-progress",icon.Children().Last().name);
        icon.Set("repair",OrbitalTheme.Muted);Assert.Less(icon.Q<Image>().tintColor.r,1);Assert.AreEqual(PickingMode.Ignore,icon.pickingMode);Assert.AreEqual(PickingMode.Ignore,icon.Q<Image>().pickingMode);
    }
    [UnityTest] public IEnumerator ApprovedIconsRenderAtHudSizes()
    {
        var root=Get<VisualElement>("root");root.Clear();root.style.width=1200;root.style.height=1130;root.style.backgroundColor=OrbitalTheme.Background;
        var panel=host.GetComponent<UIDocument>().panelSettings;
        var texture=new RenderTexture(1200,1130,24);texture.Create();panel.targetTexture=texture;
        var names=new[]{"tank","explorer","shkval","headquarters","outpost","mine","factory","refinery","science","rally","repair","sale","upgrade","chassis","assault","guidance","army"};
        var title=new Label("APPROVED PNG  /  UNITY GLYPH  /  40 px  /  28 px  /  MUTED + PROGRESS");title.style.color=OrbitalTheme.Ink;title.style.fontSize=20;title.style.marginLeft=20;title.style.marginTop=10;root.Add(title);
        for(int i=0;i<names.Length;i++)
        {
            string name=names[i];var row=new VisualElement();row.style.position=Position.Absolute;row.style.left=20+(i%2)*590;row.style.top=55+(i/2)*118;row.style.width=570;row.style.height=110;row.style.backgroundColor=OrbitalTheme.Panel;root.Add(row);
            var label=new Label(name);label.style.color=OrbitalTheme.Ink;label.style.fontSize=15;row.Add(label);
            var approved=new Image{image=OrbitalGlyph.TextureFor(name),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};approved.style.position=Position.Absolute;approved.style.left=8;approved.style.top=23;approved.style.width=100;approved.style.height=80;row.Add(approved);
            foreach(var layout in new[]{new Vector3(140,100,80),new Vector3(290,40,32),new Vector3(370,28,23),new Vector3(445,40,32)})
            {
                var glyph=new OrbitalGlyph(name);glyph.style.position=Position.Absolute;glyph.style.left=layout.x;glyph.style.top=23+(80-layout.z)/2;glyph.style.width=layout.y;glyph.style.height=layout.z;
                if(layout.x==445)glyph.Set(name,OrbitalTheme.Muted,.4);row.Add(glyph);
            }
        }
        yield return null;yield return null;yield return null;
        var previous=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(1200,1130,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1200,1130),0,0);pixels.Apply();RenderTexture.active=previous;
        var folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/precision-hud"));System.IO.Directory.CreateDirectory(folder);System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,"approved-vs-unity.png"),pixels.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(pixels);panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);
    }
    [UnityTest] public IEnumerator StartingCameraAndZoomKeepUnitsClose()
    {
        var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),
            new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
        Assert.AreEqual(PlayableProfile.Default.CameraOrthoSize,profile.CameraOrthoSize);
        Assert.AreEqual(PlayableProfile.Default.CameraHeight,profile.CameraHeight);
        Assert.AreEqual(PlayableProfile.Default.CameraOffsetX,profile.CameraOffsetX);
        Assert.AreEqual(PlayableProfile.Default.CameraOffsetZ,profile.CameraOffsetZ);
        Assert.AreEqual(PlayableProfile.Default.CameraMinZoom,profile.CameraMinZoom);
        Assert.AreEqual(PlayableProfile.Default.CameraMaxZoom,profile.CameraMaxZoom);
        Set("profile",profile);
        var priorLights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Select(x=>x.GetInstanceID()).ToArray();
        Call("CreateWorld");var camera=Get<Camera>("cameraView");var start=profile.Headquarters(PlayableOwner.Player);
        var origin=new Vector3((float)start.X,0,(float)start.Z);var initialPosition=camera.transform.position;var initialRotation=camera.transform.rotation;
        Assert.True(camera.orthographic);Assert.That(camera.orthographicSize,Is.EqualTo(9.6).Within(.001));
        Assert.AreEqual(origin+new Vector3(0,26,-37),initialPosition);
        Assert.That(camera.transform.eulerAngles.x,Is.EqualTo(Mathf.Atan(26f/37f)*Mathf.Rad2Deg).Within(.01));
        Assert.That(camera.transform.eulerAngles.y,Is.EqualTo(0).Within(.01));
        var world=Get<PlayableWorld>("world");world.Building(7,"Headquarters",true).Root.position=world.Point(start);
        var buildings=new List<PlayableBuildingSnapshot>{new PlayableBuildingSnapshot(7,PlayableOwner.Player,PlayableBuildingKind.Headquarters,start,250,1,0,0,default,heading:.7)};
        int buildingId=20;
        foreach(var kind in new[]{"Factory","Refinery","ScientificCenter","Outpost","Mine"})
        {
            var building=world.Building(buildingId,kind,true);building.Root.position=world.Point(new NavPoint(start.X+(buildingId-22)*5,start.Z+8));buildingId++;
            buildings.Add(new PlayableBuildingSnapshot(buildingId-1,PlayableOwner.Player,(PlayableBuildingKind)Enum.Parse(typeof(PlayableBuildingKind),kind),new NavPoint(start.X+(buildingId-23)*5,start.Z+8),250,1,0,0,default,heading:1.3));
            Assert.That(Vector3.Dot(building.Hull.forward,Vector3.back),Is.GreaterThan(.999));
        }
        var tank=world.Tank(10,true,PlayableEntityKind.Tank);tank.Root.position=world.Point(new NavPoint(start.X-4,start.Z+4));
        world.Tank(11,true,PlayableEntityKind.Explorer).Root.position=world.Point(new NavPoint(start.X+4,start.Z+4));
        var vision=new PlayableVision(0,profile.ArenaHalfExtent,profile.ArenaHalfExtent,4,profile.FogEdgeFeather);
        vision.Refresh(new[]{new VisionSource(start,22)},Array.Empty<KnownBuilding>());world.Fog.Update(vision.Snapshot(),1);
        var units=new[]{new PlayableEntitySnapshot(10,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(start.X-4,start.Z+4),100,false,0,0,0),new PlayableEntitySnapshot(11,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(start.X+4,start.Z+4),70,false,0,0,0)};
        Set("view",new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot()));
        Call("Render");
        foreach(var building in buildings)Assert.That(Vector3.Dot(world.Actors[building.Id].Hull.forward,Vector3.back),Is.GreaterThan(.999),"Snapshot domain headings must not turn fronts away from the camera.");
        var folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/isometric-camera"));System.IO.Directory.CreateDirectory(folder);
        var texture=new RenderTexture(1280,800,24);texture.Create();camera.targetTexture=texture;
        // Use the actual startup camera and Zoom path; the former default is only a scale reference.
        yield return null;
        Call("Pan",Vector2.right);var displacement=camera.transform.position-initialPosition;
        Assert.Greater(displacement.magnitude,0);Assert.That(Vector3.Dot(displacement.normalized,camera.transform.right),Is.GreaterThan(.999));
        camera.transform.position=initialPosition;Call("Pan",Vector2.up);displacement=camera.transform.position-initialPosition;
        Assert.Greater(displacement.magnitude,0);Assert.That(Vector3.Dot(displacement.normalized,Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up).normalized),Is.GreaterThan(.999));
        camera.transform.position=initialPosition;
        float referenceWidth=0,startWidth=0;
        try
        {
            foreach(var state in new[]{"before","start","near","far"})
            {
                camera.transform.position=initialPosition;
                camera.transform.rotation=initialRotation;
                camera.orthographicSize=state=="before"?11.52f:(float)profile.CameraOrthoSize;
                if(state=="near")Call("Zoom",100000f);
                if(state=="far")Call("Zoom",-100000f);
                if(state=="near")Assert.That(camera.orthographicSize,Is.EqualTo(profile.CameraMinZoom).Within(.001));
                if(state=="far")Assert.That(camera.orthographicSize,Is.EqualTo(profile.CameraMaxZoom).Within(.001));
                yield return null;yield return null;
                var point=tank.Root.position;float width=(camera.WorldToScreenPoint(point+camera.transform.right)-camera.WorldToScreenPoint(point)).magnitude;
                if(state=="before")referenceWidth=width;
                if(state=="start"){startWidth=width;Assert.That(width/referenceWidth,Is.EqualTo(1.2).Within(.01));}
                if(state=="near")Assert.Greater(width,startWidth*2);
                if(state=="far")Assert.That(width/startWidth,Is.InRange(.7f,.8f));
                var previous=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(1280,800,TextureFormat.RGBA32,false);
                pixels.ReadPixels(new Rect(0,0,1280,800),0,0);pixels.Apply();RenderTexture.active=previous;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,state+".png"),pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
        finally
        {
            camera.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);Set("cameraView",null);UnityEngine.Object.DestroyImmediate(camera.gameObject);
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(x=>!priorLights.Contains(x.GetInstanceID())))UnityEngine.Object.DestroyImmediate(light.gameObject);
        }
    }
    [UnityTest] public IEnumerator PrecisionPanelsRenderPopulatedQueuesAtBothViewports()
    {
        var root=Get<VisualElement>("root");var panel=host.GetComponent<UIDocument>().panelSettings;
        Set("notice","");Get<Label>("mapClock").text="03:42";Get<PlayableMapSurface>("compactMap").SetPublic(Array.Empty<PlayableMapMark>(),64);
        var folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/precision-hud"));System.IO.Directory.CreateDirectory(folder);
        root.style.backgroundColor=new Color(.055f,.08f,.10f);
        // Real runtime world artwork is rendered by an Editor camera, never a Player build.
        var priorLights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Select(x=>x.GetInstanceID()).ToArray();
        Call("CreateWorld");var camera=Get<Camera>("cameraView");camera.orthographicSize=18;camera.transform.position=new Vector3(0,28,-22);camera.transform.LookAt(Vector3.zero);
        var world=Get<PlayableWorld>("world");var vision=new PlayableVision(0,64,64,4,PlayableProfile.Default.FogEdgeFeather);vision.Refresh(new[]{new VisionSource(default,22)},Array.Empty<KnownBuilding>());world.Fog.Update(vision.Snapshot(),1);Get<PlayableMapTerrain>("mapTerrain").ShowPublicTerrain();Get<PlayableMapSurface>("compactMap").SetPublic(new[]{new PlayableMapMark(7,default,PlayableBuildingKind.Factory,MapMarkState.Ready,PlayableOwner.Player),new PlayableMapMark(10,new NavPoint(-4,1),default,MapMarkState.Unit,PlayableOwner.Player)},64);world.Building(7,"Factory",true).Root.position=Vector3.zero;
        world.Tank(10,true,PlayableEntityKind.Tank).Root.position=new Vector3(-4,0,1);world.Tank(11,true,PlayableEntityKind.Explorer).Root.position=new Vector3(4,0,2);
        var scene=new VisualElement{pickingMode=PickingMode.Ignore};scene.style.position=Position.Absolute;scene.style.left=0;scene.style.right=0;scene.style.top=0;scene.style.bottom=0;root.Insert(0,scene);
        var lifecycle=new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,225,false);
        var units=new[]{new PlayableEntitySnapshot(10,PlayableOwner.Player,PlayableEntityKind.Tank,default,100,false,0,0,0),new PlayableEntitySnapshot(11,PlayableOwner.Player,PlayableEntityKind.Explorer,default,PlayableUnitRules.Health(PlayableProfile.Default,PlayableEntityKind.Explorer),false,0,0,0)};
        try
        {
            foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(800,620)})
            {
                var texture=new RenderTexture(size.x,size.y,24);var backdrop=new RenderTexture(size.x,size.y,24);texture.Create();backdrop.Create();camera.targetTexture=backdrop;panel.targetTexture=texture;scene.style.backgroundImage=new StyleBackground(Background.FromRenderTexture(backdrop));root.style.width=size.x;root.style.height=size.y;
                Set("cameraView",null); // Stable screen-centre menu anchor for this UI fixture.
                foreach(bool science in new[]{false,true})
                {
                    var orders=new[]{new PlayableProductionOrderSnapshot(1,PlayableEntityKind.Tank,150,3,20,12,true),new PlayableProductionOrderSnapshot(2,PlayableEntityKind.Shkval,200,4,20,20,false),new PlayableProductionOrderSnapshot(3,PlayableEntityKind.Explorer,100,2,15,15,false)};
                    var research=new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,7,300,12,30,true,false),new PlayableResearchOrderSnapshot(2,PlayableResearchKind.ExplorerAssaultGuns,7,0,0,30,false,false),new PlayableResearchOrderSnapshot(3,PlayableResearchKind.ShkvalGuidance,7,0,0,30,false,false)};
                    var building=new PlayableBuildingSnapshot(7,PlayableOwner.Player,science?PlayableBuildingKind.ScientificCenter:PlayableBuildingKind.Factory,default,250,1,science?0:3,.4,default,orders:science?null:orders,repeat:!science,lifecycle:lifecycle,research:science?research:null);
                    Set("view",Snapshot(units,new[]{building},9286));Get<HashSet<int>>("selection").Clear();Get<HashSet<int>>("selection").Add(7);Set("battleDismissed",false);
                    Call("ApplyResponsiveHud",(float)size.x);Call("UpdateHud");yield return null;yield return null;Call("UpdateHud");
                    Get<Button>("hudFocusButton").Focus();yield return null;yield return null;
                    var ring=Get<OrbitalBattleRing>("battleRing");var context=Get<VisualElement>("armyRegion");
                    var header=Get<VisualElement>("top");Assert.That(header.worldBound.width,Is.InRange(120f,230f));Assert.LessOrEqual(header.worldBound.height,32);Assert.LessOrEqual(Get<Button>("hudFocusButton").worldBound.xMax,header.worldBound.xMax);Assert.That(Get<Button>("hudFocusButton").worldBound.width,Is.InRange(50f,95f));
                    Assert.That(Get<Label>("creditsLabel").worldBound.center.y,Is.EqualTo(Get<Button>("hudFocusButton").worldBound.center.y).Within(1));
                    Assert.That(root.Q("credits-icon").worldBound.center.y,Is.EqualTo(root.Q("army-icon").worldBound.center.y).Within(1));
                    Assert.LessOrEqual(ring.worldBound.width,science?212:282);Assert.LessOrEqual(ring.worldBound.yMax,context.worldBound.yMin+1);
                    Assert.GreaterOrEqual(context.worldBound.xMin,root.Q("tactical-minimap-frame").worldBound.xMax);
                    Assert.LessOrEqual(context.worldBound.xMax,size.x);Assert.LessOrEqual(context.worldBound.yMax,size.y);
                    Assert.That(root.Q<Button>(science?"research-slot-0":"production-slot-0").worldBound.height,Is.InRange(42f,46f));
                    var old=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,size.x,size.y),0,0);pixels.Apply();RenderTexture.active=old;
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,(science?"research-":"production-")+size.x+".png"),pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);
                }
                Set("view",Snapshot(units));Get<HashSet<int>>("selection").Clear();Get<HashSet<int>>("selection").Add(11);Get<VisualElement>("root").Focus();Call("UpdateHud");yield return null;yield return null;
                Assert.AreEqual("Исследователь",Get<Label>("selectionLabel").text);Assert.Less(Get<Label>("contextStats").resolvedStyle.fontSize,Get<Label>("selectionLabel").resolvedStyle.fontSize);
                foreach(bool pause in new[]{false,true})
                {
                    Set("paused",pause);Call("UpdateHud");yield return null;yield return null;
                    if(pause)CollectionAssert.AreEqual(new[]{"Продолжить","Управление","Настройки","Начать заново","Завершить матч"},Get<VisualElement>("modalCard").Query<Button>().ToList().Where(b=>b.style.display.value!=DisplayStyle.None).Select(b=>b.text).ToArray());
                    var previous=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,size.x,size.y),0,0);pixels.Apply();RenderTexture.active=previous;
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,(pause?"pause-":"unit-")+size.x+".png"),pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);
                }
                Set("paused",false);Call("UpdateHud");
                scene.style.backgroundImage=StyleKeyword.None;panel.targetTexture=null;camera.targetTexture=null;texture.Release();backdrop.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(backdrop);
            }
        }
        finally
        {
            Set("cameraView",null);UnityEngine.Object.DestroyImmediate(camera.gameObject);
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(x=>!priorLights.Contains(x.GetInstanceID())))UnityEngine.Object.DestroyImmediate(light.gameObject);
            panel.targetTexture=null;
        }
    }

}
