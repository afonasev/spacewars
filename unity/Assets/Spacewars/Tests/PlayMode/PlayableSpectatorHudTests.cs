using System;
using System.Collections;
using System.Collections.Generic;
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
    PlayableSpectatorFrame Fixture(int count,bool armySystems=false,bool allCompleted=false,bool hawks=false,int eliminatedMask=1<<7,long sequence=1,Func<int,double> income=null,int firstTeam=1,int secondTeam=2)
    {
        int Team(int index)=>index%2==0?firstTeam:secondTeam;
        Set("matchSetup",new NativeLobbyConfiguration{Participants=Enumerable.Range(0,count).Select(i=>new NativeLobbyParticipant{Name="Игрок "+(i+1),Team=Team(i),Color=i,Human=false}).ToList()});
        var players=Enumerable.Range(0,count).Select(i=>{
            var owner=(PlayableOwner)i;var units=new[]{new PlayableEntitySnapshot(100+i,owner,hawks?PlayableEntityKind.Hawk:PlayableEntityKind.Tank,new NavPoint(i*4,0),150,false,0,0,0)};
            var research=new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,10+i,300,30,30,false,true),new PlayableResearchOrderSnapshot(2,PlayableResearchKind.ExplorerAssaultGuns,10+i,300,12,30,true,false),new PlayableResearchOrderSnapshot(3,PlayableResearchKind.ShkvalGuidance,10+i,0,0,30,false,false)};
            if(i==0)research=new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,70+i,300,12,30,true,false),new PlayableResearchOrderSnapshot(2,PlayableResearchKind.ExplorerAssaultGuns,70+i,0,0,30,false,false),new PlayableResearchOrderSnapshot(3,PlayableResearchKind.ShkvalGuidance,70+i,0,0,30,false,false)};
            if(armySystems)research=i==0?new[]{
                new PlayableResearchOrderSnapshot(11,PlayableResearchKind.ImprovedWeaponSystems,70+i,300,30,30,false,true),
                new PlayableResearchOrderSnapshot(12,PlayableResearchKind.AdvancedWeaponSystems,70+i,500,9,45,true,false)}:new[]{
                new PlayableResearchOrderSnapshot(13,PlayableResearchKind.ImprovedDefensiveSystems,70+i,300,30,30,false,true),
                new PlayableResearchOrderSnapshot(14,PlayableResearchKind.AdvancedDefensiveSystems,70+i,500,45,45,false,true)};
            if(allCompleted)research=Enum.GetValues(typeof(PlayableResearchKind)).Cast<PlayableResearchKind>().Select((k,n)=>new PlayableResearchOrderSnapshot(n+1,k,70+i,PlayableResearchRules.Cost(PlayableProfile.Default,k),PlayableResearchRules.Duration(PlayableProfile.Default,k),PlayableResearchRules.Duration(PlayableProfile.Default,k),false,true)).ToArray();
            var orders=new[]{new PlayableProductionOrderSnapshot(1,hawks?PlayableEntityKind.Hawk:PlayableEntityKind.Tank,150,3,20,10,true),new PlayableProductionOrderSnapshot(2,hawks?PlayableEntityKind.Hawk:PlayableEntityKind.Explorer,100,2,20,20,false)};
            var buildings=new[]{new PlayableBuildingSnapshot(50+i,owner,PlayableBuildingKind.Headquarters,new NavPoint(i*4,12),1000,1,0,0,default),new PlayableBuildingSnapshot(10+i,owner,hawks?PlayableBuildingKind.Hangar:PlayableBuildingKind.Factory,new NavPoint(i*4,4),250,1,2,.5,default,orders:orders,repeat:true,repeatKind:hawks?PlayableEntityKind.Hawk:PlayableEntityKind.Tank),new PlayableBuildingSnapshot(30+i,owner,PlayableBuildingKind.Refinery,new NavPoint(i*4,8),250,.4,0,0,default,phase:ConstructionPhase.Constructing),new PlayableBuildingSnapshot(70+i,owner,PlayableBuildingKind.ScientificCenter,new NavPoint(i*4,16),250,1,0,0,default,research:research)};
            var vision=new PlayableVision(Team(i),64,64,4,PlayableProfile.Default.FogEdgeFeather);vision.Refresh(new[]{new VisionSource(new NavPoint(i*4,0),12)},Array.Empty<KnownBuilding>());
            return new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,sequence,15660,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,840+i*100,null,units,buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,incomePerSecond:income?.Invoke(i)??12,population:new PlayablePopulationSnapshot(3,3,100),ownerResearch:i==1&&!armySystems?Array.Empty<PlayableResearchOrderSnapshot>():research,owner:owner,ownerId:"Игрок "+(i+1),team:Team(i),ownerEliminated:(eliminatedMask & (1<<i))!=0,vision:vision.Snapshot(),activeProfile:PlayableProfile.Default);
        }).ToArray();
        return (PlayableSpectatorFrame)Activator.CreateInstance(typeof(PlayableSpectatorFrame),Flags,null,new object[]{players[0],players},null);
    }
    void Show(PlayableSpectatorFrame frame){Set("spectatorFrame",frame);Set("view",frame.Overview);Call("UpdateHud");Call("UpdateSpectatorHud");}
    [Test] public void CollapsedTableDefersHiddenRowsAndReopensAtLatestFrame()
    {
        Set("localHumanCount",4);Set("spectatorCollapsed",true);Call("UpdateSpectatorCollapse");Show(Fixture(8,sequence:1));
        Assert.AreEqual(0,Get<VisualElement>("spectatorRows").childCount);
        Show(Fixture(8,sequence:2,income:i=>50+i));Assert.AreEqual(0,Get<VisualElement>("spectatorRows").childCount);
        Assert.Less(Get<VisualElement>("spectatorPanel").style.width.value.value,Get<VisualElement>("spectatorTable").style.width.value.value,"collapsed controls must leave room for split-view resources");
        Set("spectatorCollapsed",false);Call("UpdateSpectatorCollapse");Call("UpdateSpectatorHud");
        Assert.AreEqual(2,Get<long>("spectatorSequence"));Assert.GreaterOrEqual(Get<VisualElement>("spectatorRows").childCount,8);
        var row=Get<VisualElement>("spectatorRows").Q("spectator-row-Игрок 1");StringAssert.Contains("50",((Label)row[1]).text);
        Assert.AreEqual(1,row.style.opacity.value);
        var eliminated=Get<VisualElement>("spectatorRows").Q("spectator-row-Игрок 8");
        Assert.AreEqual(.5f,eliminated.style.opacity.value);Assert.AreEqual(new Color(.13f,.15f,.16f,.95f),eliminated.style.backgroundColor.value);
    }
    [Test] public void ManualZoomAndPanSuspendAutoViewUntilReentryFromOverview()
    {
        Show(Fixture(2));
        var camera=new GameObject("Observer camera").AddComponent<Camera>();
        camera.transform.SetParent(host.transform);
        camera.orthographic=true;
        camera.orthographicSize=(float)PlayableProfile.Default.CameraMinZoom;
        Set("cameraView",camera);
        Call("SetSpectatorPerspective",PlayableOwner.Player);
        Call("Zoom",1f);
        Assert.False(Get<bool>("spectatorManualCamera"),"A clamped zoom does not interrupt follow");

        Call("Zoom",-1f);
        float zoom=camera.orthographicSize;
        Assert.Greater(zoom,PlayableProfile.Default.CameraMinZoom);
        Assert.True(Get<bool>("spectatorManualCamera"));
        Assert.AreEqual(PlayableOwner.Player,Get<PlayableSnapshot>("view").Owner);
        var position=camera.transform.position;
        Call("UpdateBotCamera");
        Assert.AreEqual(position,camera.transform.position);
        Assert.AreEqual(zoom,camera.orthographicSize);

        Call("SetSpectatorPerspective",PlayableOwner.Enemy);
        Assert.True(Get<bool>("spectatorManualCamera"),"Changing player directly keeps manual control");
        Call("SetSpectatorPerspective",PlayableOwner.Enemy);
        Call("SetSpectatorPerspective",PlayableOwner.Enemy);
        Assert.False(Get<bool>("spectatorManualCamera"),"Entering from overview restores follow");
        Call("PanCameraManually",Vector3.right*3);
        Assert.AreEqual(position+Vector3.right*3,camera.transform.position);
        Assert.True(Get<bool>("spectatorManualCamera"));
        Assert.AreEqual(PlayableOwner.Enemy,Get<PlayableSnapshot>("view").Owner);
    }
    Camera CreateTravelCamera(float zoom, float aspect)
    {
        var camera = new GameObject("Observer travel camera").AddComponent<Camera>();
        camera.transform.SetParent(host.transform);
        camera.orthographic = true;
        camera.orthographicSize = zoom;
        camera.aspect = aspect;
        camera.transform.SetPositionAndRotation(Vector3.up * 20, Quaternion.Euler(90, 0, 0));
        Set("cameraView", camera);
        Set("botCameraProfile", new SplitScreenHudProfile { botCameraSpeed = 8 });
        return camera;
    }

    [Test] public void ActivityPanUsesCurrentMatchRateWithBaseSpeedFloor()
    {
        var camera = CreateTravelCamera(16, 2);
        var target = new NavPoint(96, 0);
        foreach (var step in new[] { (Rate: .5, Position: 1), (Rate: 1d, Position: 2), (Rate: 2d, Position: 4), (Rate: 4d, Position: 8) })
        {
            Set("spectatorRate", step.Rate);
            Call("MoveBotCamera", target, false, .125f);
            Assert.That(camera.transform.position.x, Is.EqualTo(step.Position).Within(.001f));
        }
    }

    [TestCase(8f, 2f, 32f, 0f, false)]
    [TestCase(8f, 2f, 64f, 0f, false)]
    [TestCase(8f, 2f, 64.5f, 0f, true)]
    [TestCase(16f, 2f, 64.5f, 0f, false)]
    [TestCase(8f, 1f, 64f, 0f, true)]
    [TestCase(8f, 2f, 64f, 32f, false)]
    [TestCase(8f, 2f, 64f, 32.5f, true)]
    public void ActivitySnapsOnlyBeyondTwoCurrentViewportSpans(float zoom, float aspect, float x, float z, bool snap)
    {
        var camera = CreateTravelCamera(zoom, aspect);
        var start = camera.transform.position;
        var target = new NavPoint(x, z);
        Set("spectatorRate", .5d);
        Call("MoveBotCamera", target, false, .125f);
        var offset = new Vector3(x, 0, z);
        var expected = start + (snap ? offset : offset.normalized);
        Assert.That(Vector3.Distance(expected, camera.transform.position), Is.LessThan(.001f));
    }

    [Test] public void AssignedBotCameraPansAtBaseSpeedAcrossDistantActivity()
    {
        var camera = CreateTravelCamera(8, 2);
        Set("spectatorMode", false);
        Set("spectatorRate", 4d);
        Call("MoveBotCamera", new NavPoint(96, 0), false, .125f);
        Assert.That(camera.transform.position.x, Is.EqualTo(1).Within(.001f));
    }

    [Test] public void ObserverRendersNeutralPublicMinePadInUndiscoveredGround()
    {
        var p=PlayableProfile.Default;
        var site=new TerritorySite(74,PlayableBuildingKind.Mine,new NavPoint(16,14),p);
        var vision=new PlayableVision(1,64,64,4,p.FogEdgeFeather);
        vision.Refresh(Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());
        var snapshot=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,1,RuntimeStatus.Running,false,
            PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),
            Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
            vision:vision.Snapshot(),publicSites:new[]{site});
        using(var world=new PlayableWorld(host.transform,p,staged:true)){
            world.RenderSites(snapshot);
            var pad=world.GraphicsRoot.Find("Site pad 74:0");
            Assert.NotNull(pad);
            Assert.Greater(pad.Find("Technical apron").GetComponent<MeshFilter>().sharedMesh.vertexCount,0);
            Assert.Zero(pad.Find("Capture").GetComponent<MeshFilter>().sharedMesh.vertexCount);
            var block=new MaterialPropertyBlock();
            pad.Find("Technical apron").GetComponent<MeshRenderer>().GetPropertyBlock(block);
            Assert.AreEqual((float)p.FogExploredOpacity,block.GetFloat("_FogLimit"));
        }
    }
    [Test] public void TeamTotalsUseOriginalRosterAndUpdateFromActivePlayers()
    {
        Show(Fixture(3,firstTeam:3,secondTeam:7,income:i=>i==1?90:4.44));
        var rows=Get<VisualElement>("spectatorRows");
        var total=rows.Q("spectator-team-total-3");
        CollectionAssert.AreEqual(new[]{total.name,"spectator-row-Игрок 1","spectator-row-Игрок 3","spectator-row-Игрок 2"},rows.Children().Select(row=>row.name));
        Assert.AreEqual("▾ Team 3",((Label)total[0]).text);
        Assert.AreEqual("+"+8.88.ToString("0.#")+"/с",((Label)total[1]).text);
        Assert.AreEqual("6/200",((Label)total[2]).text);
        Assert.AreEqual(PickingMode.Position,total.pickingMode);
        Assert.IsTrue(total.focusable);

        Call("SetSpectatorPerspective",PlayableOwner.Enemy);
        Show(Fixture(3,firstTeam:3,secondTeam:7,eliminatedMask:1<<2,sequence:2,income:i=>i==0?7:90));
        Assert.AreSame(total,rows.Q("spectator-team-total-3"));
        Assert.AreEqual("+7/с",((Label)total[1]).text);
        Assert.AreEqual("3/100",((Label)total[2]).text);

        Show(Fixture(3,firstTeam:3,secondTeam:7,eliminatedMask:(1<<2)|1,sequence:3));
        Assert.AreEqual("+0/с",((Label)total[1]).text);
        Assert.AreEqual("0/0",((Label)total[2]).text);
    }
    [TestCase(false)][TestCase(true)] public void EscalationUsesEachPlayersOwnLevelProgressAndBothCooldowns(bool compact)
    {
        int secondAbility=compact?3:2;
        var source=Fixture(8);var ready=new OrbitalEscalationSnapshot(2,125,250,0,0,new[]{new OrbitalAbilitySnapshot(1,0,30,1)},Array.Empty<int>());
        var cooling=new OrbitalEscalationSnapshot(3,40,200,3,2,new[]{new OrbitalAbilitySnapshot(1,0,30,1),new OrbitalAbilitySnapshot(secondAbility,8.2,30,2)},Array.Empty<int>());
        var maximum=new OrbitalEscalationSnapshot(compact?5:6,0,0,0,0,(compact?new[]{1,3,4,6}:new[]{1,2,3,4,6}).Select(id=>new OrbitalAbilitySnapshot(id,0,30,1)).ToArray(),Array.Empty<int>());
        var players=source.Players.Select((player,i)=>player.WithOrbital(i==0?ready:i==1?cooling:i==7?maximum:OrbitalEscalationSnapshot.Empty)).ToArray();
        var frame=(PlayableSpectatorFrame)Activator.CreateInstance(typeof(PlayableSpectatorFrame),Flags,null,new object[]{players[0],players},null);Show(frame);
        var rows=Get<Dictionary<string,VisualElement>>("spectatorRowCache");var a=rows["Игрок 1"][6];var b=rows["Игрок 2"][6];var max=rows["Игрок 8"][6];
        Assert.AreEqual("2",a.Q<Label>("spectator-escalation-number").text);Assert.AreEqual(.5f,a.Q<OrbitalProgressRing>().Fraction);
        Assert.That(a.Q("spectator-ability-1").tooltip,Does.Contain("готово"));Assert.AreEqual("✓",a.Q<Label>("seconds").text);Assert.IsEmpty(a.Query<Button>().ToList(),"Read-only observer abilities must never expose commands");
        Assert.AreEqual("2",b.Q("spectator-ability-1").Q<Label>("seconds").text,"Common cooldown also gates an otherwise ready ability");
        Assert.AreEqual("5",b.Q("spectator-ability-"+secondAbility).Q<Label>("seconds").text,"Use speed-adjusted personal remaining time, rounded upward");
        Assert.True(b.Q<OrbitalProgressRing>().Pending);Assert.That(b[0].tooltip,Does.Contain("Ожидается выбор"));
        Assert.AreEqual("MAX",max.Q<Label>("spectator-escalation-number").text);Assert.AreEqual(1,max.Q<OrbitalProgressRing>().Fraction);Assert.AreEqual(compact?4:5,max[1].childCount,"All selected abilities remain visible even for an eliminated player");
        Assert.AreEqual(0,rows["Игрок 3"][6][1].childCount);Call("SetSpectatorPerspective",PlayableOwner.Enemy);Call("UpdateSpectatorHud");
        Assert.AreEqual("2",a.Q<Label>("spectator-escalation-number").text);Assert.AreEqual("5",b.Q("spectator-ability-"+secondAbility).Q<Label>("seconds").text);
        Call("UpdateSpectatorEscalation",b,ready);Assert.AreEqual(1,b[1].childCount);Assert.AreEqual("✓",b.Q<Label>("seconds").text);Assert.AreEqual(DisplayStyle.None,b.Q<OrbitalCooldownSweep>().style.display.value);
    }
    [UnityTest] public IEnumerator EscalationColumnStaysAccessibleInNarrowPerspective()
    {
        var root=Get<VisualElement>("root");root.style.width=640;root.style.height=620;Show(Fixture(8));Call("SetSpectatorPerspective",PlayableOwner.Enemy);Call("UpdateHud");Call("UpdateSpectatorHud");
        yield return null;yield return null;Call("UpdateSpectatorHud");yield return null;yield return null;
        var panel=Get<VisualElement>("spectatorPanel");var scroll=Get<ScrollView>("spectatorTableScroll");var table=Get<VisualElement>("spectatorTable");var cell=Get<Dictionary<string,VisualElement>>("spectatorRowCache")["Игрок 1"][6];
        Assert.False(panel.worldBound.Overlaps(Get<VisualElement>("top").worldBound));Assert.Greater(table.worldBound.width,scroll.contentViewport.worldBound.width);
        scroll.ScrollTo(cell);yield return null;yield return null;Assert.LessOrEqual(cell.worldBound.xMax,scroll.contentViewport.worldBound.xMax+1);Assert.GreaterOrEqual(cell.worldBound.xMin,scroll.contentViewport.worldBound.xMin-1);
        var total=table.Q("spectator-team-total-1");
        Assert.AreEqual(Get<VisualElement>("spectatorHead")[1].worldBound.xMin,total[1].worldBound.xMin,.5f);
        Assert.AreEqual("12/400",((Label)total[2]).text);
        Set("spectatorCollapsed",true);Call("UpdateSpectatorCollapse");Assert.AreEqual(DisplayStyle.None,scroll.style.display.value);Assert.AreEqual(8,Get<Dictionary<string,VisualElement>>("spectatorRowCache").Count);
        Set("spectatorCollapsed",false);Call("UpdateSpectatorCollapse");Assert.AreEqual(DisplayStyle.Flex,scroll.style.display.value);
    }
    [Test] public void ProducerUpgradeQueueUsesBuildingPortraitAndNeverUnitRepeatBadge()
    {
        var players=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Hangar}.Select((kind,i)=>{
            var owner=(PlayableOwner)i;var unit=i==0?PlayableEntityKind.Tank:PlayableEntityKind.Hawk;
            var orders=new[]{new PlayableProductionOrderSnapshot(1,unit,300,0,30,15,true,true),new PlayableProductionOrderSnapshot(2,unit,100,3,30,30,false)};
            var buildings=new[]{new PlayableBuildingSnapshot(10+i,owner,PlayableBuildingKind.Headquarters,default,1000,1,0,0,default),new PlayableBuildingSnapshot(20+i,owner,kind,default,250,1,2,.5,default,orders:orders,repeat:true,repeatKind:unit)};
            return new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,Array.Empty<PlayableEntitySnapshot>(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,owner:owner,ownerId:"player-"+i,population:new PlayablePopulationSnapshot(0,0,100),activeProfile:PlayableProfile.Default);
        }).ToArray();
        var frame=(PlayableSpectatorFrame)Activator.CreateInstance(typeof(PlayableSpectatorFrame),Flags,null,new object[]{players[0],players},null);Show(frame);
        var rows=Get<VisualElement>("spectatorRows");
        for(int i=0;i<2;i++){
            var icons=rows[i][4].Query<OrbitalGlyph>().ToList();Assert.AreEqual(i==0?"factoryUpgraded":"hangarUpgraded",icons[0].Kind);Assert.AreEqual(i==0?"tank":"hawk",icons[1].Kind);
            Assert.False(rows[i][4][0].Query<Label>().ToList().Any(l=>l.text=="∞"));Assert.True(rows[i][4][1].Query<Label>().ToList().Any(l=>l.text=="∞"));
        }
    }
    [Test] public void HangarQueueAndLivingHawksAppearInSpectatorSummary()
    {
        Show(Fixture(2,hawks:true));
        var rows=Get<VisualElement>("spectatorRows");Assert.AreEqual(2,rows.childCount);
        foreach(var row in rows.Children())
        {
            Assert.AreEqual("hawk",row[3].Q<OrbitalGlyph>().Kind);
            var queue=row[4].Query<OrbitalGlyph>().ToList();Assert.AreEqual(3,queue.Count);
            Assert.AreEqual(2,queue.Count(g=>g.Kind=="hawk"));
            Assert.That(row[4].Query<Label>().ToList().Select(x=>x.text),Does.Contain("∞"));
        }
    }
    [Test] public void ArmySystemsShowCompletedLevelsAndActiveProgressForTheirOwnPlayers()
    {
        Show(Fixture(2,armySystems:true));
        var rows=Get<Dictionary<string,VisualElement>>("spectatorRowCache");
        var weapons=rows["Игрок 1"][5];var defense=rows["Игрок 2"][5];
        Assert.AreEqual(2,weapons.childCount);Assert.AreEqual(2,defense.childCount);
        CollectionAssert.AreEqual(new[]{"weapon-systems-1","weapon-systems-2"},weapons.Query<OrbitalGlyph>().ToList().Select(g=>g.Kind));
        CollectionAssert.AreEqual(new[]{"defensive-systems-1","defensive-systems-2"},defense.Query<OrbitalGlyph>().ToList().Select(g=>g.Kind));
        Assert.That(weapons[0].tooltip,Does.Contain("Улучшенные оружейные системы").And.Contain("15%"));
        Assert.That(weapons[1].tooltip,Does.Contain("Передовые оружейные системы").And.Contain("30%"));
        Assert.That(defense[1].tooltip,Does.Contain("Передовые защитные системы").And.Contain("30%"));
        Assert.True(weapons[0].Query<Label>().ToList().Any(l=>l.text=="✓"));
        Assert.False(weapons[1].Query<Label>().ToList().Any(l=>l.text=="✓"));
        Assert.AreEqual(20,weapons[1].Children().Last().style.width.value.value,.001,"active level 2 has 9 / 45 progress");
        Call("SetSpectatorPerspective",PlayableOwner.Enemy);Call("UpdateHud");Call("UpdateSpectatorHud");
        Assert.AreEqual(2,rows["Игрок 1"][5].childCount);Assert.That(rows["Игрок 1"][5][0].tooltip,Does.Contain("оружейные"));
    }
    [Test] public void CompletedArmySystemsRemainDiscoverableWhenUpgradeColumnOverflows()
    {
        Show(Fixture(2,allCompleted:true));var upgrades=Get<Dictionary<string,VisualElement>>("spectatorRowCache")["Игрок 1"][5];
        int completeResearchCount=Enum.GetValues(typeof(PlayableResearchKind)).Length;
        Assert.AreEqual(completeResearchCount,upgrades.childCount);Assert.AreEqual("+"+(completeResearchCount-3),upgrades[3].Q<Label>().text);
        foreach(var name in new[]{"Улучшенные оружейные системы","Передовые оружейные системы","Улучшенные защитные системы","Передовые защитные системы"})Assert.That(upgrades[3].tooltip,Does.Contain(name));
        Assert.AreEqual(DisplayStyle.None,upgrades[4].style.display.value);
    }
    [Test] public void RepeatedPublicationsReuseArtworkAndRefreshUnitAndQueueKinds()
    {
        Show(Fixture(2));
        var row=Get<Dictionary<string,VisualElement>>("spectatorRowCache")["Игрок 1"];
        var unit=row[3].Q<OrbitalGlyph>();var queued=row[4][1].Q<OrbitalGlyph>();
        Show(Fixture(2,hawks:true,sequence:2));
        Assert.AreSame(unit,row[3].Q<OrbitalGlyph>());Assert.AreEqual("hawk",unit.Kind);
        Assert.AreSame(queued,row[4][1].Q<OrbitalGlyph>());Assert.AreEqual("hawk",queued.Kind);
        Assert.AreEqual("1",((Label)row[3][0][1]).text);
        Show(Fixture(2,sequence:3));
        Assert.AreSame(unit,row[3].Q<OrbitalGlyph>());Assert.AreEqual("tank",unit.Kind);
        Assert.AreSame(queued,row[4][1].Q<OrbitalGlyph>());Assert.AreEqual("tank",queued.Kind);
    }
    [Test] public void ReusedIconRefreshesProgressBadgeAndOverflowWithoutStaleContent()
    {
        var icons=new VisualElement();
        void Populate(int count,double progress,string badge)
        {
            icons.userData=0;
            for(int i=0;i<count;i++)Call("SpectatorIcon",icons,"tank","Order "+i,progress,badge);
            typeof(PlayableBootstrap).GetMethod("FinishIcons",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{icons});
        }
        Populate(4,.2,"∞");var tile=icons[3];var glyph=tile.Q<OrbitalGlyph>();var bar=tile[1];var badgeLabel=tile.Children().OfType<Label>().Single();
        Populate(4,.8,null);
        Assert.AreSame(glyph,tile.Q<OrbitalGlyph>());Assert.AreSame(bar,tile[1]);
        Assert.AreEqual(80,bar.style.width.value.value,.001);Assert.IsEmpty(tile.Children().OfType<Label>());
        Populate(6,-1,"✓");Assert.AreSame(tile,icons[3]);Assert.AreEqual("+3",tile.Q<Label>().text);
        Assert.AreEqual("Order 3\nOrder 4\nOrder 5",tile.tooltip);Assert.IsNull(tile.Q<OrbitalGlyph>());
        Populate(4,-1,"✓");Assert.AreSame(glyph,tile.Q<OrbitalGlyph>());Assert.AreSame(badgeLabel,tile.Children().OfType<Label>().Single());
        Assert.AreEqual("✓",badgeLabel.text);Assert.AreEqual(2,tile.childCount);Assert.AreEqual("Order 3",tile.tooltip);
        Populate(5,.5,null);Assert.AreEqual("+2",tile.Q<Label>().text);Assert.AreEqual("Order 3\nOrder 4",tile.tooltip);
    }
    [Test] public void AllRowsSurvivePerspectiveCollapseAndInspectionIsReadOnly()
    {
        var priorLights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Select(x=>x.GetInstanceID()).ToArray();Call("CreateWorld");
        try{
        var frame=Fixture(8);Show(frame);var root=Get<VisualElement>("root");Assert.AreEqual(8,Get<Dictionary<string,VisualElement>>("spectatorRowCache").Count);
        Call("SetSpectatorPerspective",PlayableOwner.Enemy);Call("UpdateHud");Call("UpdateSpectatorHud");Assert.AreEqual(PlayableOwner.Enemy,Get<PlayableOwner?>("spectatorPerspective"));Assert.AreEqual(8,Get<Dictionary<string,VisualElement>>("spectatorRowCache").Count);
        Set("spectatorCollapsed",true);Call("UpdateSpectatorCollapse");Assert.AreEqual(DisplayStyle.None,Get<VisualElement>("spectatorRows").style.display.value);Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>("top").style.display.value);
        Call("ToggleMap");Assert.True(Get<bool>("mapOpen"));Call("ToggleMap");Assert.False(Get<bool>("mapOpen"));
        Set("inspectionEntityId",10);Call("ValidateInspection");Assert.AreEqual(0,Get<int>("inspectionEntityId"));
        Set("inspectionEntityId",11);Call("UpdateHud");Assert.False(root.Q<Button>("production-slot-0").enabledSelf);Assert.False(root.Q<Button>("production-slot-0").tooltip.Contains("отмен"));Assert.AreEqual(DisplayStyle.None,Get<OrbitalBattleRing>("battleRing").style.display.value);
        Call("ResearchClick",PlayableResearchKind.TankChassis);Call("ExecuteBattleAction","tank",false,false);Assert.AreEqual(0,Get<long>("sequence"));
        Call("SetSpectatorPerspective",PlayableOwner.Enemy);Assert.IsNull(Get<PlayableOwner?>("spectatorPerspective"));
        Assert.AreEqual(0,Get<System.Collections.Generic.HashSet<int>>("selection").Count);
        }finally{
            var camera=Get<Camera>("cameraView");if(camera!=null)UnityEngine.Object.DestroyImmediate(camera.gameObject);Set("cameraView",null);
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(x=>!priorLights.Contains(x.GetInstanceID())))UnityEngine.Object.DestroyImmediate(light.gameObject);
        }
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
                var raw=Fixture(count);var seeded=raw.Players.Select((player,i)=>player.WithOrbital(new OrbitalEscalationSnapshot(i==0?3:6,i==0?125:0,i==0?250:0,0,0,Enumerable.Range(1,i==0?3:5).Select(id=>new OrbitalAbilitySnapshot(id,id==2?17:id==3?42:0,60,1)).ToArray(),Array.Empty<int>()))).ToArray();var frame=(PlayableSpectatorFrame)Activator.CreateInstance(typeof(PlayableSpectatorFrame),Flags,null,new object[]{seeded[0],seeded},null);Get<System.Collections.Generic.Dictionary<string,VisualElement>>("spectatorRowCache").Clear();Get<VisualElement>("spectatorRows").Clear();Set("spectatorSequence",-1L);Show(frame);Call("SetSpectatorPerspective",PlayableOwner.Player);Set("inspectionEntityId",10);Call("ApplyResponsiveHud",1600/scale);Call("UpdateHud");Call("UpdateSpectatorHud");yield return null;yield return null;
                Assert.AreEqual(count,Get<Dictionary<string,VisualElement>>("spectatorRowCache").Count);var rows=Get<VisualElement>("spectatorRows");Assert.LessOrEqual(rows.worldBound.xMax,root.worldBound.xMax);var firstPlayer=Get<Dictionary<string,VisualElement>>("spectatorRowCache")["Игрок 1"];Assert.AreEqual(7,firstPlayer.childCount);Assert.Less(rows.worldBound.yMax,500);
                Capture(texture,System.IO.Path.Combine(folder,"r7-"+count+"-players.png"));
                if(count==8)
                {
                    var scroll=Get<ScrollView>("spectatorTableScroll");
                    var table=Get<VisualElement>("spectatorTable");
                    var blue=rows.Q<Button>("spectator-team-total-1");
                    var purple=rows.Q<Button>("spectator-team-total-2");
                    Assert.AreEqual(ScrollerVisibility.Hidden,scroll.horizontalScrollerVisibility);
                    Assert.AreEqual(table.worldBound.yMax,scroll.worldBound.yMax,1f,"No empty strip under the expanded table");
                    float expandedHeight=scroll.worldBound.height;
                    Submit(blue);yield return null;yield return null;
                    Assert.AreEqual("▸ Team 1",((Label)blue[0]).text);
                    Assert.AreEqual("▾ Team 2",((Label)purple[0]).text);
                    Assert.AreEqual("+48/с",((Label)blue[1]).text);
                    Assert.AreEqual(DisplayStyle.None,firstPlayer.resolvedStyle.display);
                    Assert.AreEqual(DisplayStyle.Flex,Get<Dictionary<string,VisualElement>>("spectatorRowCache")["Игрок 2"].resolvedStyle.display);
                    Assert.AreEqual(expandedHeight-4*firstPlayer.style.height.value.value,scroll.worldBound.height,1f);
                    Assert.AreEqual(table.worldBound.yMax,scroll.worldBound.yMax,1f,"No empty strip under a collapsed team");
                    Capture(texture,System.IO.Path.Combine(folder,"r7-team-collapsed.png"));
                    Call("SetSpectatorPerspective",PlayableOwner.Enemy);Call("UpdateSpectatorHud");
                    Assert.AreEqual(DisplayStyle.None,firstPlayer.style.display.value);
                    Set("spectatorCollapsed",true);Call("UpdateSpectatorCollapse");
                    Set("spectatorCollapsed",false);Call("UpdateSpectatorCollapse");
                    Assert.AreEqual(DisplayStyle.None,firstPlayer.style.display.value);
                    Submit(purple);Assert.AreEqual("▸ Team 2",((Label)purple[0]).text);
                    Submit(blue);Assert.AreEqual(DisplayStyle.Flex,firstPlayer.style.display.value);
                    Assert.AreEqual("▸ Team 2",((Label)purple[0]).text);
                    Submit(purple);Call("SetSpectatorPerspective",PlayableOwner.Player);Set("inspectionEntityId",10);Call("UpdateHud");Call("UpdateSpectatorHud");
                    Assert.AreEqual(PlayableOwner.Player,Get<PlayableOwner?>("spectatorPerspective"));
                    yield return null;yield return null;
                }
                var upgrade=firstPlayer[5][0];upgrade.Focus();yield return null;Assert.AreEqual(DisplayStyle.Flex,Get<Label>("spectatorTooltip").style.display.value);StringAssert.Contains("Улучшенное шасси",Get<Label>("spectatorTooltip").text);StringAssert.Contains("Скорость:",Get<Label>("spectatorTooltip").text);StringAssert.Contains(" → ",Get<Label>("spectatorTooltip").text);Capture(texture,System.IO.Path.Combine(folder,"r7-"+count+"-tooltip.png"));root.Focus();
                Set("inspectionEntityId",70);Call("UpdateHud");Call("UpdateSpectatorHud");yield return null;yield return null;Assert.AreEqual(3,root.Q("research-slots").Children().Count(e=>e.tooltip!="Свободный слот"));Capture(texture,System.IO.Path.Combine(folder,"r7-"+count+"-research.png"));
                Set("spectatorCollapsed",true);Call("UpdateSpectatorCollapse");yield return null;Capture(texture,System.IO.Path.Combine(folder,"r7-"+count+"-collapsed.png"));Set("spectatorCollapsed",false);Call("UpdateSpectatorCollapse");Set("spectatorPerspective",null);
            }
        }
        finally{panel.targetTexture=null;camera.targetTexture=null;texture.Release();backdrop.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(backdrop);UnityEngine.Object.DestroyImmediate(camera.gameObject);Set("cameraView",null);foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(x=>!priorLights.Contains(x.GetInstanceID())))UnityEngine.Object.DestroyImmediate(light.gameObject);}
    }
    [UnityTest] public IEnumerator FpsPlacementInSpectatorOverviewAndPerspective()
    {
        bool saved=NativeDevicePreferences.ShowFps;NativeDevicePreferences.ShowFps=true;
        var root=Get<VisualElement>("root");var panel=host.GetComponent<UIDocument>().panelSettings;
        var texture=new RenderTexture(1280,800,24);texture.Create();panel.targetTexture=texture;
        var folder=Environment.GetEnvironmentVariable("SPACEWARS_FPS_EVIDENCE");
        try
        {
            root.style.backgroundColor=new Color(.055f,.08f,.1f);
            foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(800,620),new Vector2Int(640,620)})
            {
                panel.targetTexture=null;texture.Release();texture.width=size.x;texture.height=size.y;texture.Create();panel.targetTexture=texture;
                root.style.width=size.x;root.style.height=size.y;Set("spectatorMode",true);Show(Fixture(8));Call("ApplyResponsiveHud",(float)size.x);
                foreach(bool perspective in new[]{false,true})foreach(bool collapsed in new[]{false,true})
                {
                    Set("spectatorPerspective",perspective?(PlayableOwner?)PlayableOwner.Player:null);
                    Set("spectatorSequence",-1L);Call("UpdateHud");Call("UpdateSpectatorHud");
                    var table=root.Q("spectator-summary");var fold=table.Q<Button>("spectator-collapse");
                    if(Get<bool>("spectatorCollapsed")!=collapsed)
                        using(var submit=NavigationSubmitEvent.GetPooled()){submit.target=fold;fold.SendEvent(submit);}
                    yield return new WaitForSecondsRealtime(.6f);yield return null;
                    Assert.AreEqual(collapsed,Get<bool>("spectatorCollapsed"));
                    var fps=root.panel.visualTree.Q<Label>("device-fps");Assert.NotNull(fps);
                    Assert.AreEqual(DisplayStyle.Flex,fps.resolvedStyle.display);StringAssert.EndsWith(" FPS",fps.text);
                    Assert.AreEqual(Color.white,fps.resolvedStyle.color);Assert.AreEqual(Color.black,fps.style.textShadow.value.color);
                    Assert.Greater(fps.style.textShadow.value.blurRadius,0);Assert.AreEqual(PickingMode.Ignore,fps.pickingMode);
                    Assert.AreEqual(table.worldBound.xMin+8,fps.worldBound.xMin,.5f,"FPS keeps its inner panel gutter when expanded or collapsed");
                    Assert.GreaterOrEqual(fps.worldBound.yMin,root.worldBound.yMin+8);
                    Assert.AreEqual(root.worldBound.xMin+8,table.worldBound.xMin,.5f,"Table keeps its outer gutter");
                    Assert.LessOrEqual(table.worldBound.xMax,root.worldBound.xMax-7.5f);
                    Assert.AreEqual(root.worldBound.yMin+8,table.worldBound.yMin,.5f,"FPS never pushes the table down");
                    var header=root.Q("match-header");if(perspective)
                    {

                        Assert.AreEqual(root.worldBound.yMin+8,header.worldBound.yMin,.5f);Assert.AreEqual(root.worldBound.xMax-8,header.worldBound.xMax,.5f);
                    }
                    var controls=table.Q("spectator-controls");var buttons=controls.Query<Button>().ToList();
                    var speedPositions=buttons.Select(button=>button.worldBound.position).ToArray();
                    foreach(var reading in new[]{"9 FPS","99 FPS","999 FPS","9999 FPS"})
                    {
                        fps.text=reading;OrbitalTheme.LayoutDeviceDiagnostics(root);yield return null;yield return null;
                        Assert.AreEqual(64,fps.resolvedStyle.width,.5f,"FPS owns a fixed-width column");
                        for(int i=0;i<buttons.Count;i++)Assert.AreEqual(speedPositions[i],buttons[i].worldBound.position,"Changing FPS digits must not move controls");
                    }
                    Assert.AreEqual(fps.worldBound.xMax+8,buttons[0].worldBound.xMin,1f);
                    Assert.AreEqual(buttons[0].worldBound.center.y,fps.worldBound.center.y,.5f,"FPS and speeds share one row");
                    Assert.AreEqual(controls.worldBound.xMax-controls.resolvedStyle.paddingRight,fold.worldBound.xMax,.5f);
                    Assert.AreEqual(controls.worldBound.yMin+8,buttons[0].worldBound.yMin,.5f);Assert.AreEqual(controls.worldBound.yMax-8,buttons[0].worldBound.yMax,.5f);
                    foreach(var button in buttons)
                    {
                        Assert.GreaterOrEqual(button.worldBound.xMin,table.worldBound.xMin+7.5f);Assert.LessOrEqual(button.worldBound.xMax,table.worldBound.xMax-7.5f);
                        Assert.AreEqual(buttons[0].worldBound.yMin,button.worldBound.yMin,.5f,"All speed/fold buttons share their vertical alignment");
                        Assert.False(fps.worldBound.Overlaps(button.worldBound));
                        if(perspective)Assert.False(header.worldBound.Overlaps(button.worldBound));
                        Assert.AreSame(button,root.panel.Pick(button.worldBound.center),"FPS must not intercept a spectator button");
                    }
                    Assert.AreEqual(collapsed?DisplayStyle.None:DisplayStyle.Flex,Get<VisualElement>("spectatorRows").resolvedStyle.display);
                    if(!string.IsNullOrEmpty(folder)){System.IO.Directory.CreateDirectory(folder);Capture(texture,System.IO.Path.Combine(folder,"spectator-"+(perspective?"perspective":"overview")+"-"+(collapsed?"collapsed":"expanded")+"-"+size.x+"x"+size.y+".png"));}
                }
                Set("spectatorMode",false);Call("UpdateSpectatorHud");yield return new WaitForSecondsRealtime(.6f);yield return null;
                var overlay=root.panel.visualTree.Q<Label>("device-fps");Assert.AreEqual(DisplayStyle.None,root.Q("spectator-summary").resolvedStyle.display);
                Assert.AreEqual(root.worldBound.xMin+8,overlay.worldBound.xMin,.5f);Assert.AreEqual(root.worldBound.yMin+8,overlay.worldBound.yMin,.5f);
                Assert.Zero(overlay.resolvedStyle.backgroundColor.a,"Without the table FPS remains a transparent field overlay");Assert.AreEqual(PickingMode.Ignore,overlay.pickingMode);
                if(!string.IsNullOrEmpty(folder))Capture(texture,System.IO.Path.Combine(folder,"fps-without-table-"+size.x+"x"+size.y+".png"));
            }
            Set("spectatorMode",true);Set("spectatorPerspective",null);Call("UpdateSpectatorHud");NativeDevicePreferences.ShowFps=false;
            yield return new WaitForSecondsRealtime(.6f);yield return null;
            Assert.AreEqual(DisplayStyle.None,root.panel.visualTree.Q("device-fps").resolvedStyle.display);
            Assert.AreEqual(root.worldBound.yMin+8,root.Q("spectator-summary").worldBound.yMin,.5f,"Disabling FPS releases the diagnostic row");
        }
        finally{NativeDevicePreferences.ShowFps=saved;panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
    }
    [Test] public void SettingsKeepFpsAndTpsIndependent()
    {
        bool savedFps=NativeDevicePreferences.ShowFps,savedTps=NativeDevicePreferences.ShowTps;
        NativeSettingsPage settings=null;
        try
        {
            NativeDevicePreferences.ShowFps=false;
            NativeDevicePreferences.ShowTps=false;
            settings=new NativeSettingsPage(Get<VisualElement>("root"),Get<NativeMenuNavigation>("menuNavigation"),
                ()=>{},new NativeLocalControlSettings());
            var fps=settings.Page.Q<Toggle>("settings-show-fps");
            var tps=settings.Page.Q<Toggle>("settings-show-tps");
            Assert.IsFalse(fps.value);Assert.IsFalse(tps.value);
            tps.value=true;
            Assert.IsTrue(NativeDevicePreferences.ShowTps);Assert.IsFalse(NativeDevicePreferences.ShowFps);
            fps.value=true;
            Assert.IsTrue(NativeDevicePreferences.ShowTps);Assert.IsTrue(NativeDevicePreferences.ShowFps);
            tps.value=false;
            Assert.IsFalse(NativeDevicePreferences.ShowTps);Assert.IsTrue(NativeDevicePreferences.ShowFps);
        }
        finally
        {
            settings?.Page.RemoveFromHierarchy();
            NativeDevicePreferences.ShowFps=savedFps;NativeDevicePreferences.ShowTps=savedTps;
        }
    }
    [UnityTest] public IEnumerator TickCounterTracksWallTimePauseAndTargetIndependently()
    {
        bool savedFps=NativeDevicePreferences.ShowFps,savedTps=NativeDevicePreferences.ShowTps;
        var overlayHost=new GameObject("tick counter fixture");
        var panel=ScriptableObject.CreateInstance<PanelSettings>();
        panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var document=overlayHost.AddComponent<UIDocument>();
        document.panelSettings=panel;
        var overlay=document.rootVisualElement;
        PlayableSnapshot Frame(long generation,long tick,bool paused=false)
            =>new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,generation,7,tick,tick,
                paused?RuntimeStatus.Paused:RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,0,null,
                Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),
                Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
        PlayableSnapshot snapshot=null;
        double target=30;
        OrbitalTheme.ConfigurePanel(panel,overlay,()=> (snapshot,target));
        var fps=overlay.Q<Label>("device-fps");
        var tps=overlay.Q<Label>("device-tps");
        IEnumerator Wait(Func<bool> ready)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+6;
            while(!ready()&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Assert.True(ready(),$"counter did not reach expected state, actual {tps.text}");
        }
        try
        {
            NativeDevicePreferences.ShowFps=false;
            NativeDevicePreferences.ShowTps=true;
            Assert.AreEqual(DisplayStyle.None,tps.style.display.value,"No active match");
            snapshot=Frame(1,100);
            yield return Wait(()=>tps.text=="— TPS");
            snapshot=Frame(1,120);
            yield return Wait(()=>int.TryParse(tps.text.Replace(" TPS",""),out _));
            StringAssert.IsMatch(@"^\d+ TPS$",tps.text);
            int measured=int.Parse(tps.text.Split(' ')[0]);
            Assert.That(measured,Is.GreaterThan(0).And.LessThan(30));
            Assert.AreEqual(DisplayStyle.None,fps.style.display.value);
            Assert.AreEqual(8,tps.style.left.value.value);
            Assert.AreEqual(PickingMode.Ignore,tps.pickingMode);
            yield return Wait(()=>tps.text=="0 TPS");

            NativeDevicePreferences.ShowFps=true;
            yield return Wait(()=>fps.style.display.value==DisplayStyle.Flex);
            Assert.AreEqual(72,tps.style.left.value.value);
            Assert.AreEqual(DisplayStyle.Flex,tps.style.display.value);
            target=60;
            yield return Wait(()=>tps.text=="— TPS");
            snapshot=Frame(1,120,true);
            yield return Wait(()=>tps.text=="TPS: пауза");
            snapshot=Frame(2,0);
            yield return Wait(()=>tps.text=="— TPS");
            NativeDevicePreferences.ShowTps=false;
            yield return Wait(()=>tps.style.display.value==DisplayStyle.None);
            Assert.AreEqual(DisplayStyle.Flex,fps.style.display.value);
        }
        finally
        {
            NativeDevicePreferences.ShowFps=savedFps;
            NativeDevicePreferences.ShowTps=savedTps;
            UnityEngine.Object.DestroyImmediate(overlayHost);
            UnityEngine.Object.DestroyImmediate(panel);
        }
    }
    [UnityTest] public IEnumerator LiveSpectatorShowsBothCountersBesideSpeedControls()
    {
        bool savedFps=NativeDevicePreferences.ShowFps,savedTps=NativeDevicePreferences.ShowTps;
        var root=Get<VisualElement>("root");
        var panel=host.GetComponent<UIDocument>().panelSettings;
        var texture=new RenderTexture(1280,800,24);
        texture.Create();panel.targetTexture=texture;
        using var runtime=new PlayableRuntime(PlayableProfile.Default,91,7,autonomousOwnerAi:false,autonomousEnemyAi:false,spectator:true);
        try
        {
            NativeDevicePreferences.ShowFps=true;NativeDevicePreferences.ShowTps=true;
            Set("runtime",runtime);
            root.style.width=1280;root.style.height=800;root.style.backgroundColor=new Color(.055f,.08f,.1f);
            Show(Fixture(8));
            var fps=root.panel.visualTree.Q<Label>("device-fps");
            var tps=root.panel.visualTree.Q<Label>("device-tps");
            double deadline=Time.realtimeSinceStartupAsDouble+15;
            while((string.IsNullOrEmpty(tps.text)||tps.text.StartsWith("—"))&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            StringAssert.IsMatch(@"^\d+ TPS$",tps.text);
            Assert.False(tps.text.StartsWith("—"),"Actual match counter must have sampled the tick stream");
            yield return null;yield return null;
            var controls=root.Q("spectator-controls");
            var speed=controls.Query<Button>().First();
            Assert.AreEqual(DisplayStyle.Flex,tps.resolvedStyle.display);
            Assert.AreEqual(fps.worldBound.xMax,tps.worldBound.xMin,.5f);
            Assert.AreEqual(tps.worldBound.xMax+8,speed.worldBound.xMin,.5f);
            Assert.AreEqual(speed.worldBound.center.y,tps.worldBound.center.y,.5f);
            var folder=Environment.GetEnvironmentVariable("SPACEWARS_TPS_EVIDENCE");
            if(!string.IsNullOrEmpty(folder))
            {
                System.IO.Directory.CreateDirectory(folder);
                Capture(texture,System.IO.Path.Combine(folder,"spectator-fps-tps.png"));
            }
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
            deadline=Time.realtimeSinceStartupAsDouble+5;
            while(tps.text!="— TPS"&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Assert.AreEqual("— TPS",tps.text,"Changing speed starts a new measurement");
            Assert.LessOrEqual(tps.MeasureTextSize("120 TPS",0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x,tps.contentRect.width);
            root.style.width=640;
            Call("SetSpectatorPerspective",PlayableOwner.Player);Call("UpdateHud");Call("UpdateSpectatorHud");
            yield return null;yield return null;Call("UpdateSpectatorHud");yield return null;yield return null;
            var header=root.Q("match-header");
            Assert.False(tps.worldBound.Overlaps(fps.worldBound));
            foreach(var button in controls.Query<Button>().ToList())
            {
                Assert.False(tps.worldBound.Overlaps(button.worldBound));
                Assert.False(header.worldBound.Overlaps(button.worldBound));
            }
        }
        finally
        {
            Set("runtime",null);
            runtime.RequestStop();
            NativeDevicePreferences.ShowFps=savedFps;NativeDevicePreferences.ShowTps=savedTps;
            panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);
        }
    }
    static void Submit(Button button)
    {
        button.Focus();
        using var submit=NavigationSubmitEvent.GetPooled();
        submit.target=button;
        button.SendEvent(submit);
    }
    static void Capture(RenderTexture texture,string path){var old=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();RenderTexture.active=old;System.IO.File.WriteAllBytes(path,pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);}
}
