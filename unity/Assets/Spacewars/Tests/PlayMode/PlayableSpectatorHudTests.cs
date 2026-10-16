using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class PlayableSpectatorHudTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    GameObject host;PlayableBootstrap hud;
    T Get<T>(string n)=>(T)typeof(PlayableBootstrap).GetField(n,Flags).GetValue(hud);
    void Set(string n,object v)=>typeof(PlayableBootstrap).GetField(n,Flags).SetValue(hud,v);
    void Call(string n,params object[] args)=>typeof(PlayableBootstrap).GetMethods(Flags).Single(m=>m.Name==n&&m.GetParameters().Length==args.Length).Invoke(hud,args);
    [SetUp] public void Setup(){host=new GameObject("spectator HUD fixture");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;Set("profile",PlayableProfile.Default);Call("CreateHud");Call("CreateSpectatorHud");Set("spectatorMode",true);Set("notice","");}
    [TearDown] public void Cleanup(){UnityEngine.Object.DestroyImmediate(host);}
    PlayableSpectatorFrame Fixture(int count)
    {
        Set("matchSetup",new NativeLobbyConfiguration{Participants=Enumerable.Range(0,count).Select(i=>new NativeLobbyParticipant{Name="Игрок "+(i+1),Team=i%2,Color=i,Human=false}).ToList()});
        var players=Enumerable.Range(0,count).Select(i=>{
            var owner=(PlayableOwner)i;var units=new[]{new PlayableEntitySnapshot(100+i,owner,PlayableEntityKind.Tank,new NavPoint(i*4,0),150,false,0,0,0)};
            var research=new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,10+i,300,30,30,false,true),new PlayableResearchOrderSnapshot(2,PlayableResearchKind.ExplorerAssaultGuns,10+i,300,12,30,true,false),new PlayableResearchOrderSnapshot(3,PlayableResearchKind.ShkvalGuidance,10+i,0,0,30,false,false)};
            if(i==0)research=new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,70+i,300,12,30,true,false),new PlayableResearchOrderSnapshot(2,PlayableResearchKind.ExplorerAssaultGuns,70+i,0,0,30,false,false),new PlayableResearchOrderSnapshot(3,PlayableResearchKind.ShkvalGuidance,70+i,0,0,30,false,false)};
            var orders=new[]{new PlayableProductionOrderSnapshot(1,PlayableEntityKind.Tank,150,3,20,10,true),new PlayableProductionOrderSnapshot(2,PlayableEntityKind.Explorer,100,2,20,20,false)};
            var buildings=new[]{new PlayableBuildingSnapshot(50+i,owner,PlayableBuildingKind.Headquarters,new NavPoint(i*4,12),1000,1,0,0,default),new PlayableBuildingSnapshot(10+i,owner,PlayableBuildingKind.Factory,new NavPoint(i*4,4),250,1,2,.5,default,orders:orders,repeat:true),new PlayableBuildingSnapshot(30+i,owner,PlayableBuildingKind.Refinery,new NavPoint(i*4,8),250,.4,0,0,default,phase:ConstructionPhase.Constructing),new PlayableBuildingSnapshot(70+i,owner,PlayableBuildingKind.ScientificCenter,new NavPoint(i*4,16),250,1,0,0,default,research:research)};
            var vision=new PlayableVision(i%2,64,64,4,PlayableProfile.Default.FogEdgeFeather);vision.Refresh(new[]{new VisionSource(new NavPoint(i*4,0),12)},Array.Empty<KnownBuilding>());
            return new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,15660,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,840+i*100,null,units,buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,incomePerSecond:12,population:new PlayablePopulationSnapshot(3,3,100),ownerResearch:i==1?Array.Empty<PlayableResearchOrderSnapshot>():research,owner:owner,ownerId:"Игрок "+(i+1),team:i%2,ownerEliminated:i==7,vision:vision.Snapshot(),activeProfile:PlayableProfile.Default);
        }).ToArray();
        return (PlayableSpectatorFrame)Activator.CreateInstance(typeof(PlayableSpectatorFrame),Flags,null,new object[]{players[0],players},null);
    }
    void Show(PlayableSpectatorFrame frame){Set("spectatorFrame",frame);Set("view",frame.Overview);Call("UpdateHud");Call("UpdateSpectatorHud");}
    [Test] public void AllRowsSurvivePerspectiveCollapseAndInspectionIsReadOnly()
    {
        var frame=Fixture(8);Show(frame);var root=Get<VisualElement>("root");Assert.AreEqual(8,Get<VisualElement>("spectatorRows").childCount);
        Call("SetSpectatorPerspective",PlayableOwner.Enemy);Call("UpdateHud");Call("UpdateSpectatorHud");Assert.AreEqual(PlayableOwner.Enemy,Get<PlayableOwner?>("spectatorPerspective"));Assert.AreEqual(8,Get<VisualElement>("spectatorRows").childCount);
        Set("spectatorCollapsed",true);Call("UpdateSpectatorCollapse");Assert.AreEqual(DisplayStyle.None,Get<VisualElement>("spectatorRows").style.display.value);Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>("top").style.display.value);
        Call("ToggleMap");Assert.True(Get<bool>("mapOpen"));Call("ToggleMap");Assert.False(Get<bool>("mapOpen"));
        Set("inspectionEntityId",10);Call("ValidateInspection");Assert.AreEqual(0,Get<int>("inspectionEntityId"));
        Set("inspectionEntityId",11);Call("UpdateHud");Assert.False(root.Q<Button>("production-slot-0").enabledSelf);Assert.False(root.Q<Button>("production-slot-0").tooltip.Contains("отмен"));Assert.AreEqual(DisplayStyle.None,Get<OrbitalBattleRing>("battleRing").style.display.value);
        Call("ResearchClick",PlayableResearchKind.TankChassis);Call("ExecuteBattleAction","tank",false,false);Assert.AreEqual(0,Get<long>("sequence"));
        Call("SetSpectatorPerspective",PlayableOwner.Enemy);Assert.IsNull(Get<PlayableOwner?>("spectatorPerspective"));
        Assert.AreEqual(0,Get<System.Collections.Generic.HashSet<int>>("selection").Count);
    }
    [UnityTest] public IEnumerator SpeedButtonsDriveActualRuntimeAndKeepPlayerPaint()
    {
        using(var runtime=new PlayableRuntime(PlayableProfile.Default,91,7,autonomousOwnerAi:false,autonomousEnemyAi:false,spectator:true))
        {
            Set("runtime",runtime);var root=Get<VisualElement>("root");Show(Fixture(2));root.style.width=1280;root.style.height=720;Call("ApplyResponsiveHud",1280f);
            try
            {
                yield return null;yield return null;
                long slow=0,fast=0;
                foreach(double rate in new[]{.5,4d,1d,2d})
                {
                    var button=root.Query<Button>().ToList().Single(b=>b.userData is double value&&value==rate);button.Focus();
                    if(rate==4)
                    {
                        var point=button.worldBound.center;Assert.AreSame(button,root.panel.Pick(point),"Speed button must receive pointer input at its visible centre");
                        using(var down=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=point})){down.target=button;button.SendEvent(down);}
                        using(var up=PointerUpEvent.GetPooled(new Event{type=EventType.MouseUp,button=0,mousePosition=point})){up.target=button;button.SendEvent(up);}
                    }
                    else using(var submit=NavigationSubmitEvent.GetPooled()){submit.target=button;button.SendEvent(submit);}
                    Assert.AreEqual(rate,Get<double>("spectatorRate"));
                    long start=runtime.Latest.Tick;double deadline=Time.realtimeSinceStartupAsDouble+.5;
                    while(Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                    long ticks=runtime.Latest.Tick-start;TestContext.WriteLine(rate+"x: "+ticks+" ticks");if(rate==.5)slow=ticks;if(rate==4)fast=ticks;
                }
                Assert.Greater(slow,0);Assert.Greater(fast,slow*3,"Speed buttons must change actual fixed-tick progress, not just button colour");
                Call("SetSpectatorPerspective",PlayableOwner.Enemy);Call("UpdateHud");yield return null;yield return null;
                var credits=Get<Label>("creditsLabel");Assert.True(credits.text.StartsWith("940"));Assert.AreEqual(DisplayStyle.None,Get<Label>("objective").style.display.value);Assert.LessOrEqual(credits.worldBound.xMax,root.worldBound.xMax);Assert.GreaterOrEqual(credits.worldBound.xMin,0);
                Call("ApplyResponsiveHud",800f);root.style.width=800;yield return null;yield return null;Call("UpdateHud");yield return null;
                Assert.False(Get<VisualElement>("spectatorPanel").worldBound.Overlaps(Get<VisualElement>("top").worldBound),"Perspective resources must remain clear of the table on narrow screens");
                foreach(var row in Get<VisualElement>("spectatorRows").Children())
                {var name=(Button)row[0];Assert.AreEqual(name.style.borderLeftColor.value,name.style.color.value);}
            }
            finally{runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));Set("runtime",null);}
        }
    }
    // Wall-clock diagnostic: run this exact method through tools/unity.sh exclusive.
    // Shared-host workload can mask the target cadence; retain failures as evidence.
    [UnityTest] public IEnumerator LobbySpectatorSpeedAdvancesTheAuthoredMatchWithBots()
    {
        var profile=PlayableProfile.ThreeCrossingsDefault;var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();foreach(var player in setup.Participants)player.Human=false;
        using(var runtime=PlayableRuntime.CreateLobbyMatch(profile,92,setup))using(var routes=new UnityHostRouteService())
        {
            Set("runtime",runtime);Set("matchSetup",setup);var root=Get<VisualElement>("root");
            try
            {
                double ready=Time.realtimeSinceStartupAsDouble+45;
                while(runtime.Latest.Tick<3&&Time.realtimeSinceStartupAsDouble<ready){routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
                Assert.IsNull(runtime.Latest.Failure);Assert.GreaterOrEqual(runtime.Latest.Tick,3,"Actual lobby must finish startup and run");
                long slow=0,fast=0;
                foreach(double rate in new[]{.5,4d,1d,2d})
                {
                    var button=root.Query<Button>().ToList().Single(b=>b.userData is double value&&value==rate);button.Focus();
                    using(var submit=NavigationSubmitEvent.GetPooled()){submit.target=button;button.SendEvent(submit);}
                    double warm=Time.realtimeSinceStartupAsDouble+.25;
                    while(Time.realtimeSinceStartupAsDouble<warm){routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
                    long start=runtime.Latest.Tick;double deadline=Time.realtimeSinceStartupAsDouble+1;
                    while(Time.realtimeSinceStartupAsDouble<deadline){routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
                    long ticks=runtime.Latest.Tick-start;TestContext.WriteLine("lobby "+rate+"x: "+ticks+" ticks; cpu="+runtime.Latest.Metrics.TickCpuMilliseconds+"ms; interval="+runtime.Latest.Metrics.TickIntervalMilliseconds+"ms; tick="+runtime.Latest.Tick+"; status="+runtime.Latest.Status+"; outcome="+runtime.Latest.Outcome+"; paused="+runtime.Latest.Paused+"; stopped="+runtime.IsStopped);
                    Assert.IsNull(runtime.Latest.Failure);Assert.AreEqual(PlayableMatchOutcome.Playing,runtime.Latest.Outcome);Assert.Greater(ticks,0,"Every speed switch must keep the actual match ticking");if(rate==.5)slow=ticks;if(rate==4)fast=ticks;
                }
                Assert.Greater(slow,0);Assert.Greater(fast,slow*3,"Actual bot match must respond to spectator speed");
            }
            finally{runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));Set("runtime",null);}
        }
    }
    [Test] public void FogPerspectiveRefreshesEvenWhenTeamsShareRevisionNumber()
    {
        var a=new PlayableVision(1,64,64,4,PlayableProfile.Default.FogEdgeFeather);var b=new PlayableVision(2,64,64,4,PlayableProfile.Default.FogEdgeFeather);
        a.Refresh(new[]{new VisionSource(new NavPoint(-20,0),8)},Array.Empty<KnownBuilding>());b.Refresh(new[]{new VisionSource(new NavPoint(20,0),8)},Array.Empty<KnownBuilding>());
        Assert.AreEqual(a.Snapshot().FogRevision,b.Snapshot().FogRevision);
        using(var fog=new PlayableFogMask(PlayableProfile.Default))using(var terrain=new PlayableMapTerrain(PlayableProfile.Default))
        {
            var overview=new PlayableVision(-1,64,64,4,PlayableProfile.Default.FogEdgeFeather);overview.Refresh(new[]{new VisionSource(default,192)},Array.Empty<KnownBuilding>());
            fog.Update(overview.Snapshot(),1);terrain.Update(fog);var publicPixels=terrain.Texture.GetPixels32();
            fog.Update(a.Snapshot(),1);terrain.Update(fog);long builds=fog.TargetBuilds;
            fog.Update(b.Snapshot(),1);Assert.AreEqual(builds+1,fog.TargetBuilds);terrain.Update(fog);var ownerPixels=terrain.Texture.GetPixels32();Assert.False(publicPixels.SequenceEqual(ownerPixels));
            fog.Update(b.Snapshot(),1);Assert.AreEqual(builds+1,fog.TargetBuilds);Assert.False(terrain.Update(fog));
            fog.Update(overview.Snapshot(),1);terrain.Update(fog);CollectionAssert.AreEqual(publicPixels,terrain.Texture.GetPixels32(),"Returning from an owner perspective must restore full overview terrain");
        }
    }
    [UnityTest] public IEnumerator NativeR7FramesAndTooltip()
    {
        var root=Get<VisualElement>("root");var panel=host.GetComponent<UIDocument>().panelSettings;var texture=new RenderTexture(1600,900,24);texture.Create();panel.targetTexture=texture;float scale=OrbitalTheme.ReadableScale(1600,900);root.style.width=1600/scale;root.style.height=900/scale;root.style.backgroundColor=new Color(.055f,.08f,.1f);
        var priorLights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Select(x=>x.GetInstanceID()).ToArray();Call("CreateWorld");var camera=Get<Camera>("cameraView");camera.orthographicSize=18;camera.transform.position=new Vector3(0,28,-22);camera.transform.LookAt(Vector3.zero);
        var world=Get<PlayableWorld>("world");world.Building(10,"Factory",true).Root.position=new Vector3(0,0,4);world.Tank(100,true,PlayableEntityKind.Tank).Root.position=Vector3.zero;var vision=new PlayableVision(-1,64,64,4,PlayableProfile.Default.FogEdgeFeather);vision.Refresh(new[]{new VisionSource(default,192)},Array.Empty<KnownBuilding>());world.Fog.Update(vision.Snapshot(),1);
        var backdrop=new RenderTexture(1600,900,24);backdrop.Create();camera.targetTexture=backdrop;var scene=new VisualElement{pickingMode=PickingMode.Ignore};scene.style.position=Position.Absolute;scene.style.left=0;scene.style.right=0;scene.style.top=0;scene.style.bottom=0;scene.style.backgroundImage=new StyleBackground(Background.FromRenderTexture(backdrop));root.Insert(0,scene);
        Get<PlayableMapTerrain>("mapTerrain").ShowPublicTerrain();Get<PlayableMapSurface>("compactMap").SetPublic(new[]{new PlayableMapMark(1,default,PlayableBuildingKind.Headquarters,MapMarkState.Ready,PlayableOwner.Player)},64);Get<Label>("mapClock").text="08:42";
        var folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/spectator-native"));System.IO.Directory.CreateDirectory(folder);
        try
        {
            foreach(int count in new[]{2,8})
            {
                var frame=Fixture(count);Get<System.Collections.Generic.Dictionary<string,VisualElement>>("spectatorRowCache").Clear();Get<VisualElement>("spectatorRows").Clear();Set("spectatorSequence",-1L);Show(frame);Call("SetSpectatorPerspective",PlayableOwner.Player);Set("inspectionEntityId",10);Call("ApplyResponsiveHud",1600/scale);Call("UpdateHud");Call("UpdateSpectatorHud");yield return null;yield return null;
                Assert.AreEqual(count,Get<VisualElement>("spectatorRows").childCount);var rows=Get<VisualElement>("spectatorRows");Assert.LessOrEqual(rows.worldBound.xMax,660);Assert.Less(rows.worldBound.yMax,500);
                Capture(texture,System.IO.Path.Combine(folder,"r7-"+count+"-players.png"));
                var upgrade=rows[0][5][0];upgrade.Focus();yield return null;Assert.AreEqual(DisplayStyle.Flex,Get<Label>("spectatorTooltip").style.display.value);Assert.True(Get<Label>("spectatorTooltip").text.Contains("Улучшает"));Capture(texture,System.IO.Path.Combine(folder,"r7-"+count+"-tooltip.png"));root.Focus();
                Set("inspectionEntityId",70);Call("UpdateHud");Call("UpdateSpectatorHud");yield return null;yield return null;Assert.AreEqual(3,root.Q("research-slots").Children().Count(e=>e.tooltip!="Свободный слот"));Capture(texture,System.IO.Path.Combine(folder,"r7-"+count+"-research.png"));
                Set("spectatorCollapsed",true);Call("UpdateSpectatorCollapse");yield return null;Capture(texture,System.IO.Path.Combine(folder,"r7-"+count+"-collapsed.png"));Set("spectatorCollapsed",false);Call("UpdateSpectatorCollapse");Set("spectatorPerspective",null);
            }
        }
        finally{panel.targetTexture=null;camera.targetTexture=null;texture.Release();backdrop.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(backdrop);UnityEngine.Object.DestroyImmediate(camera.gameObject);Set("cameraView",null);foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(x=>!priorLights.Contains(x.GetInstanceID())))UnityEngine.Object.DestroyImmediate(light.gameObject);}
    }
    static void Capture(RenderTexture texture,string path){var old=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();RenderTexture.active=old;System.IO.File.WriteAllBytes(path,pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);}
}
