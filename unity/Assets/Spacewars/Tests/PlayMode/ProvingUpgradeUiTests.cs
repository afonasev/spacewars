using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

public sealed class ProvingUpgradeUiTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    [UnityTest] public IEnumerator RoundedStatsStayConsistentInLiveCombatAndTooltips()
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);
        setup.SetResearch(0,PlayableResearchKind.TankChassis,true);setup.SetResearch(0,PlayableResearchKind.ImprovedDefensiveSystems,true);setup.SetResearch(0,PlayableResearchKind.ImprovedWeaponSystems,true);setup.SetResearch(1,PlayableResearchKind.ImprovedWeaponSystems,true);
        Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,new NavPoint(-3,0)));Assert.IsNull(setup.PlaceUnit(1,PlayableEntityKind.Explorer,new NavPoint(3,0)));
        var runtime=new PlayableRuntime(setup.Configuration(),108,startPaused:false);var routes=new UnityHostRouteService();
        var host=new GameObject("Rounded stats HUD review");var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;RenderTexture target=null;
        var type=typeof(PlayableBootstrap);void Set(string n,object v)=>type.GetField(n,F).SetValue(hud,v);T Get<T>(string n)=>(T)type.GetField(n,F).GetValue(hud);object Call(string n,params object[] a)=>type.GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==a.Length).Invoke(hud,a);
        try{
            float deadline=Time.realtimeSinceStartup+30;PlayableEntitySnapshot tank=null;
            while(Time.realtimeSinceStartup<deadline){
                routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);Assert.False(runtime.IsStopped,runtime.Latest?.Failure);
                tank=runtime.Latest?.Entities.FirstOrDefault(u=>u.Kind==PlayableEntityKind.Tank);
                if(tank?.ExactHealth.HasValue==true&&Math.Abs(tank.ExactHealth.Value-Math.Round(tank.ExactHealth.Value))>1e-6)break;
                yield return null;
            }
            Assert.NotNull(tank);Assert.AreEqual(140,tank.MaxHealth);Assert.Less(tank.ExactHealth.Value,140);Assert.AreNotEqual(Math.Round(tank.ExactHealth.Value),tank.ExactHealth.Value,"Fractional live hit survives integer display");
            runtime.RequestPause(true);while(!runtime.Latest.Paused&&Time.realtimeSinceStartup<deadline)yield return null;Assert.True(runtime.Latest.Paused);
            var view=runtime.ParticipantView("player-1");tank=view.Entities.Single(u=>u.Kind==PlayableEntityKind.Tank);
            Set("profile",setup.Profile);Call("CreateHud");Set("runtime",runtime);Set("view",view);Call("UpdateSelectionStats",tank);
            var root=Get<VisualElement>("root");var stats=Get<VisualElement>("selectionStats");stats.RemoveFromHierarchy();root.Add(stats);stats.style.display=DisplayStyle.Flex;stats.style.position=Position.Absolute;stats.style.left=Length.Percent(25);stats.style.top=150;stats.style.width=Length.Percent(50);
            var label=stats.Query<Label>().ToList().Select(l=>l.text).ToArray();StringAssert.Contains(Math.Ceiling(tank.ExactHealth.Value).ToString()+" / 140",string.Join(" ",label));
            var text=PlayableMenuDescriptions.Unit(setup.Profile,tank.Kind,view.OwnerResearch,tank);StringAssert.DoesNotContain(tank.ExactHealth.Value.ToString()+" /",text);
            TestContext.WriteLine("ROUNDING_LIVE exact="+tank.ExactHealth.Value+" displayed="+tank.Health+" max="+tank.MaxHealth+" weapon=9.2");
            var panel=host.GetComponent<UIDocument>().panelSettings;target=new RenderTexture(1600,1000,24);target.Create();panel.targetTexture=target;root.style.backgroundColor=OrbitalTheme.Background;
            yield return null;yield return null;root.style.width=1600/panel.scale;root.style.height=1000/panel.scale;yield return null;yield return null;
            var old=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(1600,1000,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();RenderTexture.active=old;
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/qa/rounding-ui"));Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"rounded-live-stats.png"),pixels.EncodeToPNG());Object.Destroy(pixels);
        }finally{runtime.RequestStop();routes.Dispose();Set("runtime",null);if(target){target.Release();Object.Destroy(target);}Object.Destroy(host);}
    }
    [UnityTest] public IEnumerator DirectPanelResearchAndAirPlacementUseTheLiveAuthorityWithoutBuildings()
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);
        var point=new NavPoint(-12,-9);Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,point));
        var runtime=new PlayableRuntime(setup.Configuration(),107,startPaused:false);
        var host=new GameObject("Direct proving controls");var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        var type=typeof(PlayableBootstrap);void Set(string n,object v)=>type.GetField(n,F).SetValue(hud,v);T Get<T>(string n)=>(T)type.GetField(n,F).GetValue(hud);object Call(string n,params object[] a)=>type.GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==a.Length).Invoke(hud,a);
        var cameraHost=new GameObject("Placement projection camera");var camera=cameraHost.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=20;camera.transform.position=new Vector3(-12,30,-30);camera.transform.LookAt(new Vector3(-12,0,-9));
        var routes=new UnityHostRouteService();
        IEnumerator Until(Func<bool> predicate){float end=Time.realtimeSinceStartup+30;while(!predicate()&&Time.realtimeSinceStartup<end){routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);Assert.False(runtime.IsStopped,runtime.Latest?.Failure);yield return null;}Assert.True(predicate(),"Live sandbox edit did not publish");}
        try{
            yield return Until(()=>runtime.IsPrepared&&runtime.Latest?.Tick>0);
            Set("profile",setup.Profile);Call("CreateHud");Set("runtime",runtime);Set("provingGround",setup);Set("view",runtime.Latest);Set("cameraView",camera);
            var tabs=Get<string[]>("provingTabs");tabs[0]=tabs[1]="Исследования";Call("CreateProvingGroundUi");var root=Get<VisualElement>("root");
            Assert.AreEqual("Начать бой",root.Q<Button>("proving-phase").text);
            foreach(int side in new[]{0,1}){
                var control=root.Q<Button>("proving-research-"+side+"-TankChassis");Get<NativeMenuNavigation>("menuNavigation").ActivateElement(control);
                yield return Until(()=>runtime.ParticipantView(side==0?"player-1":"enemy-1").OwnerResearch.Any(o=>o.Kind==PlayableResearchKind.TankChassis&&o.Complete));
                Call("UpdateProvingGroundUi");Assert.True(control.enabledSelf);Assert.AreEqual("I",control.Q<Label>("proving-research-stage-"+side+"-TankChassis").text);
                Assert.AreEqual(setup.Profile.TankChassisCost,runtime.ProvingGroundCosts(side).Research);
                Get<NativeMenuNavigation>("menuNavigation").ActivateElement(control);yield return Until(()=>!runtime.ParticipantView(side==0?"player-1":"enemy-1").OwnerResearch.Any(o=>o.Kind==PlayableResearchKind.TankChassis));
                yield return Until(()=>runtime.ProvingGroundCosts(side).Research==0);
            }
            var tier=root.Q<Button>("proving-research-0-ImprovedDefensiveSystems");for(int click=0;click<3;click++)Get<NativeMenuNavigation>("menuNavigation").ActivateElement(tier);
            yield return Until(()=>runtime.ParticipantView("player-1").OwnerResearch.Count(o=>o.Complete&&PlayableResearchRules.Defense(o.Kind))==3);
            Call("UpdateProvingGroundUi");Get<NativeMenuNavigation>("menuNavigation").ActivateElement(tier);
            yield return Until(()=>!runtime.ParticipantView("player-1").OwnerResearch.Any(o=>PlayableResearchRules.Defense(o.Kind)));
            Set("view",runtime.Latest);Set("provingUnit",(PlayableEntityKind?)PlayableEntityKind.Hawk);
            var screen=camera.WorldToScreenPoint(new Vector3((float)point.X,(float)(setup.Profile.HawkClearance+PlayableUnitRules.HawkBodyHeight(setup.Profile)/2),(float)point.Z));
            var placed=(NavPoint)Call("ProvingPlacementPoint",new Vector2(screen.x,screen.y));
            Assert.AreEqual(point.X,placed.X,1e-4);Assert.AreEqual(point.Z,placed.Z,1e-4);
            Assert.True(runtime.PlaceProvingGroundUnit(0,PlayableEntityKind.Hawk,placed));yield return Until(()=>runtime.Latest.Entities.Any(e=>e.Kind==PlayableEntityKind.Hawk));
            var hawk=runtime.Latest.Entities.Single(e=>e.Kind==PlayableEntityKind.Hawk);Assert.AreEqual(point.X,hawk.Position.X,1e-4);Assert.AreEqual(point.Z,hawk.Position.Z,1e-4);
            Assert.True(runtime.PlaceProvingGroundUnit(0,PlayableEntityKind.Hawk,placed));yield return Until(()=>runtime.ProvingGroundEditStatus!=null);
            Assert.AreEqual(1,runtime.Latest.Entities.Count(e=>e.Kind==PlayableEntityKind.Hawk),"Aircraft still reject air overlap");
            Set("provingGroundBattle",true);Call("CreateProvingGroundUi");Assert.AreEqual("К расстановке",root.Q<Button>("proving-phase").text);
        }finally{runtime.RequestStop();routes.Dispose();Set("runtime",null);Object.Destroy(host);Object.Destroy(cameraHost);}
    }
    [UnityTest]
    public IEnumerator SkatCanBePlacedAndOrderedFreeFromTheOtherSidesHangar()
    {
        const int generation=126;
        var setup=new ProvingGroundSetup(PlayableProfile.Default);
        var profile=setup.Profile;
        var site=profile.AuthoredMap.Sites(profile)[1];
        Assert.IsNull(setup.PlaceBuilding(1,PlayableBuildingKind.Hangar,site.Slots[0].Position));
        var point=new NavPoint(12,-9);
        Assert.IsNull(setup.PlaceUnit(1,PlayableEntityKind.Tank,point));
        using var runtime=new PlayableRuntime(setup.Configuration(),generation,startPaused:false);
        using var routes=new UnityHostRouteService();
        var host=new GameObject("Proving Skat controls");
        var hud=host.AddComponent<PlayableBootstrap>();
        hud.enabled=false;
        var cameraHost=new GameObject("Proving Skat placement camera");
        var camera=cameraHost.AddComponent<Camera>();
        camera.orthographic=true;camera.orthographicSize=20;
        camera.transform.position=new Vector3(12,30,-30);
        camera.transform.LookAt(new Vector3(12,0,-9));
        var type=typeof(PlayableBootstrap);
        void Set(string name,object value)=>type.GetField(name,F).SetValue(hud,value);
        T Get<T>(string name)=>(T)type.GetField(name,F).GetValue(hud);
        object Call(string name,params object[] args)=>type.GetMethods(F)
            .Single(method=>method.Name==name&&method.GetParameters().Length==args.Length).Invoke(hud,args);
        PlayableSnapshot EnemyView()=>runtime.ParticipantView("enemy-1");
        IEnumerator Until(Func<bool> done)
        {
            float deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline)
            {
                routes.Service(runtime,profile.NavigationRequestsPerFrame);
                Assert.False(runtime.IsStopped,runtime.Latest?.Failure);
                if(done())yield break;
                yield return null;
            }
            Assert.Fail("The proving-ground state did not reach the expected checkpoint.");
        }
        try
        {
            yield return Until(()=>runtime.IsPrepared&&runtime.Latest.Tick>0);
            Set("profile",profile);Call("CreateHud");Set("runtime",runtime);Set("provingGround",setup);
            Set("view",runtime.Latest);Set("cameraView",camera);Set("generation",(long)generation);
            Call("CreateProvingGroundUi");
            var root=Get<VisualElement>("root");
            var menu=Get<NativeMenuNavigation>("menuNavigation");
            var place=root.Q<Button>("proving-unit-1-Skat");
            Assert.NotNull(place);
            menu.ActivateElement(place);
            var suspended=camera.WorldToScreenPoint(new Vector3((float)point.X,
                (float)(profile.HawkClearance+PlayableUnitRules.AircraftBodyHeight(profile,PlayableEntityKind.Skat)/2),
                (float)point.Z));
            var projected=(NavPoint)Call("ProvingPlacementPoint",new Vector2(suspended.x,suspended.y));
            Assert.AreEqual(point.X,projected.X,1e-4);
            Assert.AreEqual(point.Z,projected.Z,1e-4);
            Call("PlaceProvingGround",projected);
            yield return Until(()=>EnemyView().Entities.Any(unit=>unit.Kind==PlayableEntityKind.Skat));
            int placed=EnemyView().Entities.Single(unit=>unit.Kind==PlayableEntityKind.Skat).Id;
            Assert.True(runtime.PlaceProvingGroundUnit(1,PlayableEntityKind.Hawk,projected));
            yield return Until(()=>runtime.ProvingGroundEditStatus!=null);
            Assert.False(EnemyView().Entities.Any(unit=>unit.Kind==PlayableEntityKind.Hawk),"Aircraft share the air placement layer.");

            var hangar=EnemyView().Buildings.Single(building=>building.Kind==PlayableBuildingKind.Hangar);
            Set("view",runtime.Latest);
            Assert.AreEqual(true,Call("OpenProvingConstruction",hangar.Position));
            var order=root.Q<Button>("proving-produce-Skat");
            Assert.NotNull(order);
            Assert.False(order.enabledSelf);
            Assert.True(runtime.EditProvingGround(new PlayableCommand(generation,1,"enemy-1",PlayableCommandKind.QueueSkat,new[]{hangar.Id})));
            yield return Until(()=>runtime.ReceiptsAfter(0).Any(receipt=>receipt.Sequence==1));
            Assert.AreEqual(PlayableCommandStatus.Rejected,runtime.ReceiptsAfter(0).Single(receipt=>receipt.Sequence==1).Status);

            Assert.True(runtime.EditProvingGround(new PlayableCommand(generation,2,"enemy-1",PlayableCommandKind.BuildAt,null,
                siteId:site.Id,slotId:site.Slots[1].Id,parentId:hangar.ParentId,buildingKind:PlayableBuildingKind.ScientificCenter)));
            yield return Until(()=>EnemyView().Buildings.Any(building=>building.Kind==PlayableBuildingKind.ScientificCenter&&building.Phase==ConstructionPhase.Ready));
            Set("view",runtime.Latest);Set("sequence",2L);Call("UpdateProvingGroundUi");
            Assert.True(order.enabledSelf);
            StringAssert.Contains("бесплатно",order.text);
            menu.ActivateElement(order);
            yield return Until(()=>EnemyView().Buildings.Single(building=>building.Id==hangar.Id).PrivateState.Orders
                .Any(unit=>unit.Kind==PlayableEntityKind.Skat));
            Assert.Zero(EnemyView().Credits);
            yield return Until(()=>EnemyView().Entities.Any(unit=>unit.Kind==PlayableEntityKind.Skat&&unit.Id!=placed&&!unit.Departing));
            Assert.Zero(EnemyView().Credits);
        }
        finally
        {
            runtime.RequestStop();
            Set("runtime",null);
            Object.Destroy(host);
            Object.Destroy(cameraHost);
        }
    }

    [UnityTest]
    public IEnumerator SkatFliesNoseFirstInProvingGround()
    {
        const int generation=127;
        var output=Environment.GetEnvironmentVariable("SPACEWARS_SKAT_FLIGHT_EVIDENCE");
        var storage=Path.Combine(Path.GetTempPath(),"spacewars-skat-flight-"+Guid.NewGuid().ToString("N"));
        var store=NativeBalanceStore.ForApplication(PlayableBootstrap.LoadNormalProfile(),Path.Combine(storage,"lab.json"));
        var setup=new ProvingGroundSetup(store.Active);
        var start=new NavPoint(-12,-9);
        var goal=new NavPoint(-4,-9);
        Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Skat,start));

        using var runtime=new PlayableRuntime(setup.Configuration(),generation,startPaused:false);
        using var routes=new UnityHostRouteService();
        var scene=new GameObject("Skat proving ground");
        using var world=new PlayableWorld(scene.transform,setup.Profile,staged:true);
        var renderer=new GameObject("World view").AddComponent<PlayableBootstrap>();
        renderer.transform.SetParent(scene.transform);
        renderer.ConfigureShkvalEvidence(world,setup.Profile);
        var light=new GameObject("Sun").AddComponent<Light>();
        light.transform.SetParent(scene.transform);
        IndustrialLightingProfile.Load(null).ApplyScene(light);

        var camera=new GameObject("Skat capture camera").AddComponent<Camera>();
        camera.transform.SetParent(scene.transform);
        camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        camera.orthographic=true;
        camera.orthographicSize=.95f;
        camera.enabled=false;
        try
        {
            yield return world.Preparation;
            float deadline=Time.realtimeSinceStartup+30;
            while(!runtime.IsPrepared&&Time.realtimeSinceStartup<deadline)
            {
                routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);
                yield return null;
            }
            Assert.True(runtime.IsPrepared,runtime.Latest?.Failure);
            var initial=runtime.ParticipantView("player-1");
            int id=initial.Entities.Single(unit=>unit.Kind==PlayableEntityKind.Skat).Id;
            Assert.True(runtime.TrySubmit(new PlayableCommand(generation,1,"player-1",PlayableCommandKind.Move,new[]{id},goal).AsHuman()).Accepted);

            var previous=start;
            while(Time.realtimeSinceStartup<deadline)
            {
                routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);
                yield return null;
                var view=runtime.ParticipantView("player-1");
                Assert.False(runtime.IsStopped,view.Failure);
                var drone=view.Entities.Single(unit=>unit.Id==id);
                var movement=new Vector3((float)(drone.Position.X-previous.X),0,(float)(drone.Position.Z-previous.Z));
                previous=drone.Position;
                if(drone.Position.X<start.X+1||movement.sqrMagnitude<.0001f)continue;

                renderer.RenderShkvalEvidence(view,camera);
                var actor=world.Actors[id];
                // The authored emitter sits ahead of the origin, below the nose.
                var forward=Vector3.ProjectOnPlane(actor.RepairEmitter.position-actor.Hull.position,Vector3.up).normalized;
                var direction=movement.normalized;
                TestContext.WriteLine($"profile={store.ActiveIdentity}, map={setup.Profile.AuthoredMap.Id}, tick={view.Tick}, noseDot={Vector3.Dot(forward,direction):F4}");
                if(!string.IsNullOrEmpty(output))
                {
                    camera.transform.position=actor.Root.position+new Vector3(0,3,-2);
                    camera.transform.LookAt(actor.Root.position);
                    CaptureSkatFlight(camera,output);
                }
                Assert.Greater(direction.x,.99f,"The live sandbox command moves the drone to the right.");
                Assert.Greater(Vector3.Dot(forward,direction),.99f,"The nose should lead the actual movement.");
                yield break;
            }
            Assert.Fail("The Skat did not move far enough to measure its flight orientation.");
        }
        finally
        {
            runtime.RequestStop();
            renderer.ConfigureShkvalEvidence(null,setup.Profile);
            Object.Destroy(scene);
            if(Directory.Exists(storage))Directory.Delete(storage,true);
        }
    }

    private static void CaptureSkatFlight(Camera camera,string output)
    {
        var target=new RenderTexture(960,540,24);
        var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        var previous=RenderTexture.active;
        try
        {
            camera.targetTexture=target;
            camera.Render();
            RenderTexture.active=target;
            pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);
            pixels.Apply();
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output,"skat-flight.png"),pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active=previous;
            camera.targetTexture=null;
            target.Release();
            Object.Destroy(target);
            Object.Destroy(pixels);
        }
    }

    [UnityTest] public IEnumerator BothSidesExposeEveryNewResearchAndLocalCenterUpgrade()
    {
        var source=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text));
        Assert.AreEqual(20,source.TankChassisHealthBonusPercent,"Shipped resource profile includes chassis HP");
        var setup=new ProvingGroundSetup(source);
        setup.SetResearch(0,PlayableResearchKind.TankChassis,true);setup.SetResearch(1,PlayableResearchKind.HawkSalvoSystems,true);
        for(int side=0;side<2;side++){
            var site=setup.Profile.AuthoredMap.Sites(setup.Profile)[side];
            Assert.IsNull(setup.PlaceBuilding(side,PlayableBuildingKind.Factory,site.Slots[0].Position));
            Assert.IsNull(setup.PlaceBuilding(side,PlayableBuildingKind.ScientificCenter,site.Slots[1].Position));
        }
        var runtime=new PlayableRuntime(setup.Configuration(),105,startPaused:true);var host=new GameObject("Proving upgrades UI review");var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        var type=typeof(PlayableBootstrap);void Set(string n,object v)=>type.GetField(n,F).SetValue(hud,v);T Get<T>(string n)=>(T)type.GetField(n,F).GetValue(hud);object Call(string n,params object[] a)=>type.GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==a.Length).Invoke(hud,a);
        RenderTexture target=null;
        try{
            float deadline=Time.realtimeSinceStartup+30;
            while((!runtime.IsPrepared||runtime.Latest==null)&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.True(runtime.IsPrepared);Assert.NotNull(runtime.Latest);
            Set("profile",setup.Profile);Call("CreateHud");Set("runtime",runtime);Set("provingGround",setup);Set("view",runtime.Latest);
            var root=Get<VisualElement>("root");var panel=host.GetComponent<UIDocument>().panelSettings;target=new RenderTexture(1600,1000,24);target.Create();panel.targetTexture=target;root.style.width=1600/panel.scale;root.style.height=1000/panel.scale;root.style.backgroundColor=OrbitalTheme.Background;
            var tabs=Get<string[]>("provingTabs");tabs[0]=tabs[1]="Исследования";Call("CreateProvingGroundUi");yield return null;yield return null;
            // ConfigurePanel applies the target-texture scale on the first layout event.
            root.style.width=1600/panel.scale;root.style.height=1000/panel.scale;yield return null;yield return null;
            foreach(int side in new[]{0,1}){
                foreach(var kind in new[]{PlayableResearchKind.ReinforcedConcrete,PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.ImprovedDefensiveSystems}){
                    Assert.NotNull(root.Q<Button>("proving-research-"+side+"-"+kind));Assert.NotNull(root.Q<Label>("proving-research-label-"+side+"-"+kind));
                    var bounds=root.Q<Button>("proving-research-"+side+"-"+kind).worldBound;Assert.GreaterOrEqual(bounds.xMin,0);Assert.LessOrEqual(bounds.xMax,1600/panel.scale+.5f);
                }
                string owner=side==0?"player-1":"enemy-1";var view=runtime.ParticipantView(owner);int center=view.Buildings.Single(b=>(int)b.Owner==side&&b.Kind==PlayableBuildingKind.ScientificCenter).Id;
                Assert.True(OfflinePadWorldActions.Building(view,setup.Profile,center,freeEconomy:true).Any(a=>a.Command?.Kind==PlayableCommandKind.QueueResearch&&a.Command.Research==PlayableResearchKind.ScientificCenterUpgrade),"local center upgrade action on both sides");
                var row=root.Q<VisualElement>("proving-research-row-"+side+"-ReinforcedConcrete");root.Q<VisualElement>("proving-side-"+side).Q<ScrollView>().ScrollTo(row);
            }
            Call("UpdateProvingGroundUi");
            foreach(int side in new[]{0,1})root.Q<VisualElement>("proving-side-"+side).Q<ScrollView>().scrollOffset=Vector2.zero;
            yield return null;yield return null;
            var old=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(1600,1000,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();RenderTexture.active=old;
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/qa/proving-controls"));Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"proving-both-sides.png"),pixels.EncodeToPNG());Object.Destroy(pixels);
        }finally{runtime.RequestStop();Set("runtime",null);if(target){target.Release();Object.Destroy(target);}Object.Destroy(host);}
    }
}
