using System;
using System.Collections;
using System.IO;
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
    {
        host=new GameObject("income marker review");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        var profile=PlayableProfile.Default;Set("profile",profile);
        panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;
        root=new VisualElement{name="offset-viewport"};root.style.position=Position.Absolute;root.style.left=30;root.style.top=20;root.style.right=20;root.style.bottom=20;
        doc.rootVisualElement.Add(root);OrbitalTheme.ConfigurePanel(panel,doc.rootVisualElement);root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");Set("root",root);
        var world=new PlayableWorld(host.transform,profile);Set("world",world);
        var d=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        var domain=Activator.CreateInstance(d,F,null,new object[]{profile,1L},null);
        for(int i=0;i<150;i++)d.GetMethod("Step",F).Invoke(domain,new object[]{1d/30});
        var view=(PlayableSnapshot)d.GetMethod("PlayerSnapshot",F).Invoke(domain,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player});
        Set("view",view);var e=view.IncomeEvents[0];
        var actor=world.Building(e.BuildingId,e.Kind.ToString(),true);actor.Root.position=world.Point(e.Position);
        var camera=new GameObject("income review camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.orthographicSize=9;
        camera.transform.position=world.Point(e.Position)+new Vector3(0,12,-14);camera.transform.LookAt(world.Point(e.Position));Set("cameraView",camera);
        RenderSettings.ambientLight=Color.white;camera.backgroundColor=new Color(.03f,.06f,.08f);
        image=new RenderTexture(Screen.width,Screen.height,24);image.Create();panel.targetTexture=image;
        backdrop=new RenderTexture(Screen.width,Screen.height,24);backdrop.Create();camera.targetTexture=backdrop;
        var scene=new VisualElement{pickingMode=PickingMode.Ignore};scene.style.position=Position.Absolute;scene.style.left=scene.style.right=scene.style.top=scene.style.bottom=0;
        scene.style.backgroundImage=new StyleBackground(Background.FromRenderTexture(backdrop));doc.rootVisualElement.Insert(0,scene);
        yield return null;yield return null;camera.Render();Call("UpdateIncomeMarkers");for(int i=0;i<6;i++)yield return null;
        var label=root.Q<Label>("income-"+e.BuildingId);Assert.NotNull(label);Assert.AreEqual("+20",label.text);
        Assert.AreEqual(PickingMode.Ignore,label.pickingMode);Assert.AreEqual(Overflow.Hidden,root.Q("income-markers").style.overflow.value);
        Assert.AreEqual(1,label.resolvedStyle.opacity);Assert.True(root.worldBound.Contains(label.worldBound.center));
        float top=label.resolvedStyle.top;yield return null;Call("UpdateIncomeMarkers");yield return null;
        Assert.AreEqual(top,label.resolvedStyle.top,"Render frames alone must not advance paused simulation visuals.");
        string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/qa/building-income-ui/income-marker.png"));Directory.CreateDirectory(Path.GetDirectoryName(path));
        var old=RenderTexture.active;RenderTexture.active=image;var pixels=new Texture2D(image.width,image.height,TextureFormat.RGB24,false);
        pixels.ReadPixels(new Rect(0,0,image.width,image.height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());RenderTexture.active=old;UnityEngine.Object.Destroy(pixels);Assert.True(File.Exists(path),path);
        var advanced=new PlayableSnapshot(view.ProfileId,view.ProfileRevision,view.Generation,7,2,165,RuntimeStatus.Paused,true,PlayableMatchOutcome.Playing,view.Credits,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),view.Metrics,null,incomeEvents:new[]{e});
        Set("view",advanced);Call("UpdateIncomeMarkers");yield return null;
        Assert.Less(label.resolvedStyle.top,top);Assert.AreEqual(.5f,label.resolvedStyle.opacity,.01f);
        Set("view",new PlayableSnapshot(view.ProfileId,1,2,7,3,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,500,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),view.Metrics,null));Call("UpdateIncomeMarkers");
        Assert.IsNull(root.Q<Label>("income-"+e.BuildingId),"Restart clears markers from the preceding generation.");
    }
    [TearDown] public void Cleanup(){if(host)UnityEngine.Object.DestroyImmediate(host);if(panel)UnityEngine.Object.DestroyImmediate(panel);if(image){image.Release();UnityEngine.Object.DestroyImmediate(image);}if(backdrop){backdrop.Release();UnityEngine.Object.DestroyImmediate(backdrop);}}
}
