using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class PlayableIncomeMarkerTests
{
    private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    private GameObject host;private PlayableBootstrap hud;private VisualElement root;private PanelSettings panel;private RenderTexture image,backdrop;
    private void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,F).SetValue(hud,value);
    private void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,F).Invoke(hud,null);
    [UnityTest] public IEnumerator IncomeLabelRendersAtBuildingAndFreezesWithSimulationTick()
        =>IncomeMarkerScenario(false);
    [UnityTest] public IEnumerator RelayIncomeLabelUsesTowerTopAndFreezesWithSimulationTick()=>IncomeMarkerScenario(true);
    private IEnumerator IncomeMarkerScenario(bool relay)
    {
        host=new GameObject("income marker review");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        var profile=PlayableProfile.Default;Set("profile",profile);
        panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;
        root=new VisualElement{name="offset-viewport"};root.style.position=Position.Absolute;root.style.left=30;root.style.top=20;root.style.right=20;root.style.bottom=20;
        doc.rootVisualElement.Add(root);OrbitalTheme.ConfigurePanel(panel,doc.rootVisualElement);root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");Set("root",root);
        var world=new PlayableWorld(host.transform,profile);Set("world",world);
        var d=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        object domain;
        if(relay)
        {
            var points=new[]{new NavPoint(-20,-20),new NavPoint(20,20)};
            var sites=points.Select((v,i)=>new TerritorySite(i+1,PlayableBuildingKind.Headquarters,v,Array.Empty<TerritorySlot>())).Concat(new[]{new TerritorySite(4,PlayableBuildingKind.RelayTower,default,Array.Empty<TerritorySlot>())}).ToArray();
            var starts=points.Select((v,i)=>new OfflineStart("start-"+i,i+1,v,new NavPoint(v.X+5,v.Z),pin:i+1)).ToArray();
            var roster=new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)};
            var config=new OfflineMatchConfiguration(profile,"income-relay","income-flat","test",7,roster,starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,40},{40,0}});
            domain=Activator.CreateInstance(d,F,null,new object[]{profile,1L,config},null);
            d.GetMethod("SpawnUnit",F).Invoke(domain,new object[]{new NavPoint(2.5,0),PlayableOwner.Player,PlayableEntityKind.Explorer});
            for(int i=0;i<180;i++)d.GetMethod("AdvanceCapture",F).Invoke(domain,new object[]{1d/30});
        }
        else domain=Activator.CreateInstance(d,F,null,new object[]{profile,1L},null);
        for(int i=0;i<150;i++)d.GetMethod("Step",F).Invoke(domain,new object[]{1d/30});
        var view=(PlayableSnapshot)d.GetMethod("PlayerSnapshot",F).Invoke(domain,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player});
        Set("view",view);var e=relay?view.IncomeEvents.Single(f=>f.Kind==PlayableBuildingKind.RelayTower):view.IncomeEvents[0];
        int markerId=relay?-e.BuildingId:e.BuildingId;
        if(!relay){var actor=world.Building(e.BuildingId,e.Kind.ToString(),true);actor.Root.position=world.Point(e.Position);}
        var camera=new GameObject("income review camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.orthographicSize=9;
        camera.transform.position=world.Point(e.Position)+new Vector3(0,12,-14);camera.transform.LookAt(world.Point(e.Position));Set("cameraView",camera);
        if(relay)
        {
            world.RenderSites(view,camera);
            Assert.True(world.TryRelayIncomeAnchor(e.BuildingId,out var tip));Assert.Greater(tip.y,world.Point(e.Position).y);
        }
        RenderSettings.ambientLight=Color.white;camera.backgroundColor=new Color(.03f,.06f,.08f);
        image=new RenderTexture(Screen.width,Screen.height,24);image.Create();panel.targetTexture=image;
        backdrop=new RenderTexture(Screen.width,Screen.height,24);backdrop.Create();camera.targetTexture=backdrop;
        var scene=new VisualElement{pickingMode=PickingMode.Ignore};scene.style.position=Position.Absolute;scene.style.left=scene.style.right=scene.style.top=scene.style.bottom=0;
        scene.style.backgroundImage=new StyleBackground(Background.FromRenderTexture(backdrop));doc.rootVisualElement.Insert(0,scene);
        yield return null;yield return null;camera.Render();Call("UpdateIncomeMarkers");for(int i=0;i<6;i++)yield return null;
        var label=root.Q<Label>("income-"+markerId);Assert.NotNull(label);Assert.AreEqual(relay?"+10":"+20",label.text);
        Assert.AreEqual(PickingMode.Ignore,label.pickingMode);Assert.AreEqual(Overflow.Hidden,root.Q("income-markers").style.overflow.value);
        Assert.AreEqual(1,label.resolvedStyle.opacity);Assert.True(root.worldBound.Contains(label.worldBound.center));
        float top=label.resolvedStyle.top;yield return null;Call("UpdateIncomeMarkers");yield return null;
        Assert.AreEqual(top,label.resolvedStyle.top,"Render frames alone must not advance paused simulation visuals.");
        string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/relay-income-markers/screenshots/"+(relay?"relay-income.png":"building-income.png")));Directory.CreateDirectory(Path.GetDirectoryName(path));
        var old=RenderTexture.active;RenderTexture.active=image;var pixels=new Texture2D(image.width,image.height,TextureFormat.RGB24,false);
        pixels.ReadPixels(new Rect(0,0,image.width,image.height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());RenderTexture.active=old;UnityEngine.Object.Destroy(pixels);Assert.True(File.Exists(path),path);
        var events=relay?new[]{e,new PlayableIncomeEvent(e.Tick,e.BuildingId,e.Owner,PlayableBuildingKind.Headquarters,e.Position,20)}:new[]{e};
        var advanced=new PlayableSnapshot(view.ProfileId,view.ProfileRevision,view.Generation,7,2,165,RuntimeStatus.Paused,true,PlayableMatchOutcome.Playing,view.Credits,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),view.Metrics,null,incomeEvents:events);
        Set("view",advanced);Call("UpdateIncomeMarkers");yield return null;
        Assert.Less(label.resolvedStyle.top,top);Assert.AreEqual(.5f,label.resolvedStyle.opacity,.01f);
        if(relay){Assert.AreEqual("+20",root.Q<Label>("income-"+e.BuildingId).text);Assert.AreEqual("+10",label.text,"A building and relay with the same numeric ID keep separate labels.");}
        Set("view",new PlayableSnapshot(view.ProfileId,1,2,7,3,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,500,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),view.Metrics,null));Call("UpdateIncomeMarkers");
        Assert.IsNull(root.Q<Label>("income-"+markerId),"Restart clears markers from the preceding generation.");
    }
    [TearDown] public void Cleanup(){if(host)UnityEngine.Object.DestroyImmediate(host);if(panel)UnityEngine.Object.DestroyImmediate(panel);if(image){image.Release();UnityEngine.Object.DestroyImmediate(image);}if(backdrop){backdrop.Release();UnityEngine.Object.DestroyImmediate(backdrop);}}
}
