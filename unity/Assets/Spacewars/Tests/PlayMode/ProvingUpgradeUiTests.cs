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
                var toggle=root.Q<Toggle>("proving-research-"+side+"-TankChassis");toggle.value=true;
                yield return Until(()=>runtime.ParticipantView(side==0?"player-1":"enemy-1").OwnerResearch.Any(o=>o.Kind==PlayableResearchKind.TankChassis&&o.Complete));
                Call("UpdateProvingGroundUi");Assert.True(toggle.enabledSelf);Assert.True(toggle.value);
                Assert.AreEqual(setup.Profile.TankChassisCost,runtime.ProvingGroundCosts(side).Research);
                toggle.value=false;yield return Until(()=>!runtime.ParticipantView(side==0?"player-1":"enemy-1").OwnerResearch.Any(o=>o.Kind==PlayableResearchKind.TankChassis));
                yield return Until(()=>runtime.ProvingGroundCosts(side).Research==0);
            }
            var tier=root.Q<Toggle>("proving-research-0-UltimateDefensiveSystems");tier.value=true;
            yield return Until(()=>runtime.ParticipantView("player-1").OwnerResearch.Count(o=>o.Complete&&PlayableResearchRules.Defense(o.Kind))==3);
            Call("UpdateProvingGroundUi");root.Q<Toggle>("proving-research-0-ImprovedDefensiveSystems").value=false;
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
                foreach(var kind in new[]{PlayableResearchKind.ReinforcedConcrete,PlayableResearchKind.AdvancedReinforcedConcrete,PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.AdvancedWeaponSystems,PlayableResearchKind.UltimateWeaponSystems,PlayableResearchKind.ImprovedDefensiveSystems,PlayableResearchKind.AdvancedDefensiveSystems,PlayableResearchKind.UltimateDefensiveSystems}){
                    Assert.NotNull(root.Q<Toggle>("proving-research-"+side+"-"+kind));Assert.NotNull(root.Q<Label>("proving-research-label-"+side+"-"+kind));
                    var bounds=root.Q<Toggle>("proving-research-"+side+"-"+kind).worldBound;Assert.GreaterOrEqual(bounds.xMin,0);Assert.LessOrEqual(bounds.xMax,1600/panel.scale+.5f);
                }
                string owner=side==0?"player-1":"enemy-1";var view=runtime.ParticipantView(owner);int center=view.Buildings.Single(b=>(int)b.Owner==side&&b.Kind==PlayableBuildingKind.ScientificCenter).Id;
                Assert.True(OfflinePadWorldActions.Building(view,setup.Profile,center,freeEconomy:true).Any(a=>a.Command?.Kind==PlayableCommandKind.QueueResearch&&a.Command.Research==PlayableResearchKind.ScientificCenterUpgrade),"local center upgrade action on both sides");
                var row=root.Q<VisualElement>("proving-research-row-"+side+"-AdvancedReinforcedConcrete");root.Q<VisualElement>("proving-side-"+side).Q<ScrollView>().ScrollTo(row);
            }
            Call("UpdateProvingGroundUi");
            foreach(int side in new[]{0,1})root.Q<VisualElement>("proving-side-"+side).Q<ScrollView>().scrollOffset=Vector2.zero;
            yield return null;yield return null;
            var old=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(1600,1000,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();RenderTexture.active=old;
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/qa/proving-controls"));Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"proving-both-sides.png"),pixels.EncodeToPNG());Object.Destroy(pixels);
        }finally{runtime.RequestStop();Set("runtime",null);if(target){target.Release();Object.Destroy(target);}Object.Destroy(host);}
    }
}
