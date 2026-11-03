using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;

public sealed class PlayableBuildingLifecycleHudTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private GameObject host;private PlayableBootstrap hud;private VisualElement root;
    private void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(hud,value);
    private object Get(string name)=>typeof(PlayableBootstrap).GetField(name,Flags).GetValue(hud);
    private void Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(hud,args);
    [SetUp] public void Setup()
    {
        host=new GameObject("lifecycle HUD test");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        root=new VisualElement();Set("root",root);Set("profile",PlayableProfile.Default);Call("CreateBuildingLifecycleHud",root);
        Set("view",new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,900,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
    }
    [TearDown] public void Cleanup()=>UnityEngine.Object.DestroyImmediate(host);
    [UnityTest] public IEnumerator ActiveAndWaitingRepairMarkersRenderWithVariantC()
    {
        var priorLights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Select(l=>l.GetInstanceID()).ToArray();
        Call("CreateHud");Call("CreateWorld");root=(VisualElement)Get("root");
        foreach(var child in root.Children())child.style.display=DisplayStyle.None;
        int width=Screen.width,height=Screen.height;var panel=host.GetComponent<UIDocument>().panelSettings;
        var output=new RenderTexture(width,height,24);output.Create();panel.targetTexture=output;panel.scaleMode=PanelScaleMode.ConstantPixelSize;panel.scale=1;root.style.width=width;root.style.height=height;
        var sceneTexture=new RenderTexture(width,height,24);sceneTexture.Create();var camera=(Camera)Get("cameraView");camera.targetTexture=sceneTexture;camera.orthographicSize=14;camera.transform.position=new Vector3(0,25,-18);camera.transform.LookAt(Vector3.zero);
        var world=(PlayableWorld)Get("world");var profile=PlayableProfile.Default;
        var left=new NavPoint(-6,0);var right=new NavPoint(6,0);
        world.Building(901,"Factory",true).Root.position=world.Point(left);world.Building(902,"Headquarters",true).Root.position=world.Point(right);
        var vision=new PlayableVision(-1,64,64,4,profile.FogEdgeFeather);vision.Refresh(new[]{new VisionSource(default,192)},Array.Empty<KnownBuilding>());world.Fog.Update(vision.Snapshot(),1);
        PlayableBuildingSnapshot Building(int id,PlayableBuildingKind kind,NavPoint at,bool repairing,bool waiting=false,bool selling=false)=>new PlayableBuildingSnapshot(id,PlayableOwner.Player,kind,at,125,1,0,0,default,lifecycle:new PlayableBuildingLifecycleSnapshot(selling,.5,repairing,waiting,1,15,null,repairing?"Ремонт уже активен":null,3,400,true));
        void Frame(params PlayableBuildingSnapshot[] buildings)=>Set("view",new PlayableSnapshot(PlayableProfile.RequiredProfileId,profile.Revision,1,7,1,8,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,900,null,Array.Empty<PlayableEntitySnapshot>(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
        try
        {
            for(int i=0;i<3;i++)yield return null;
            Frame(Building(901,PlayableBuildingKind.Factory,left,true),Building(902,PlayableBuildingKind.Headquarters,right,true,true));Call("UpdateLifecycleMarkers");
            var scene=new VisualElement{pickingMode=PickingMode.Ignore};scene.style.position=Position.Absolute;scene.style.left=scene.style.right=scene.style.top=scene.style.bottom=0;scene.style.backgroundImage=Background.FromRenderTexture(sceneTexture);root.Insert(0,scene);
            var preview=new Image{image=Resources.Load<Texture2D>("OrbitalIcons/building-repair-active"),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};preview.style.position=Position.Absolute;preview.style.right=12;preview.style.top=12;preview.style.width=preview.style.height=90;root.Add(preview);
            var caption=new Label("C · ремонт / ожидание кредитов · справа: иконка ×3"){pickingMode=PickingMode.Ignore};caption.style.position=Position.Absolute;caption.style.left=12;caption.style.bottom=12;caption.style.fontSize=13;caption.style.color=Color.white;root.Add(caption);
            for(int i=0;i<5;i++)yield return null;
            var markers=(IDictionary)Get("lifecycleMarkers");Assert.AreEqual(2,markers.Count);
            foreach(int id in new[]{901,902})
            {
                var marker=(VisualElement)markers[id];Assert.AreEqual(Color.clear,marker.style.backgroundColor.value,"Approved repair marker has no backplate");Assert.AreEqual(PickingMode.Ignore,marker.pickingMode);Assert.NotNull(marker.Q<Image>("repair-marker-artwork").image);
                Assert.Greater(marker.worldBound.width,0);Assert.GreaterOrEqual(marker.worldBound.xMin,0);Assert.LessOrEqual(marker.worldBound.xMax,width);Assert.GreaterOrEqual(marker.worldBound.yMin,0);Assert.LessOrEqual(marker.worldBound.yMax,height);
            }
            var folder=Environment.GetEnvironmentVariable("SPACEWARS_REPAIR_MARKER_EVIDENCE");
            if(!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);var previous=RenderTexture.active;var pixels=new Texture2D(width,height,TextureFormat.RGB24,false);
                try{RenderTexture.active=output;pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,"active-and-waiting-repair-c.png"),pixels.EncodeToPNG());}
                finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
            }
            Frame(Building(901,PlayableBuildingKind.Factory,left,false,selling:true),Building(902,PlayableBuildingKind.Headquarters,right,false));Call("UpdateLifecycleMarkers");Assert.AreEqual(1,markers.Count);
            var sale=(VisualElement)markers[901];Assert.AreEqual(DisplayStyle.None,sale.Q<Image>("repair-marker-artwork").style.display.value);Assert.AreEqual("$",((Label)sale).text);Assert.Greater(sale.style.backgroundColor.value.a,0);
            Frame(Building(901,PlayableBuildingKind.Factory,left,false));Call("UpdateLifecycleMarkers");Assert.AreEqual(0,markers.Count,"Completed/cancelled repair removes its marker");
        }
        finally
        {
            panel.targetTexture=null;camera.targetTexture=null;output.Release();sceneTexture.Release();UnityEngine.Object.DestroyImmediate(output);UnityEngine.Object.DestroyImmediate(sceneTexture);UnityEngine.Object.DestroyImmediate(camera.gameObject);Set("cameraView",null);
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l=>!priorLights.Contains(l.GetInstanceID())))UnityEngine.Object.DestroyImmediate(light.gameObject);
        }
    }
    [Test] public void AutonomousRouteQaKeepsClockRunningOnFocusLossWhileOrdinaryGamePauses()
    {
        var runtime=new Spacewars.Runtime.PlayableRuntime(PlayableProfile.Default,71,7,autonomousOwnerAi:false,autonomousEnemyAi:false);
        try{
            Set("runtime",runtime);Set("routePerformanceEvidence",true);Call("OnApplicationFocus",false);
            Assert.False((bool)Get("menuPaused"));Assert.False((bool)Get("localInputFocused"));
            Set("routePerformanceEvidence",false);Call("OnApplicationFocus",true);Call("OnApplicationFocus",false);
            Assert.True((bool)Get("menuPaused"));Assert.True((bool)Get("paused"));
        }finally{runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));runtime.Dispose();var domain=typeof(Spacewars.Runtime.PlayableRuntime).GetField("domain",Flags).GetValue(runtime);domain.GetType().GetMethod("DestroyOwnedNavigation",Flags).Invoke(domain,null);Set("runtime",null);}
    }
    private void Show(int id=1,string blocked=null,bool repairing=false,bool selling=false,bool waiting=false)
    {
        var state=new PlayableBuildingLifecycleSnapshot(selling,.5,repairing,waiting,1,15,blocked,repairing?"Ремонт уже активен":null,3,400,true);
        Call("UpdateBuildingLifecycleHud",new PlayableBuildingSnapshot(id,PlayableOwner.Player,PlayableBuildingKind.Factory,default,125,1,0,0,default,lifecycle:state));
    }
    [Test] public void ConfirmationResetsOnSelectionAvailabilityFocusAndPause()
    {
        Show();Call("ConfirmBuildingSale");Assert.AreEqual(1,Get("confirmSaleBuilding"));Assert.That(root.Q<Button>("sell-building").text,Does.Contain("✓"));
        Show(2);Assert.Zero((int)Get("confirmSaleBuilding"));Call("ConfirmBuildingSale");Show(2,"В бою");Assert.Zero((int)Get("confirmSaleBuilding"));Assert.False(root.Q<Button>("sell-building").enabledSelf);
        Show();Call("ConfirmBuildingSale");Call("OnApplicationFocus",false);Assert.Zero((int)Get("confirmSaleBuilding"));
        Call("ConfirmBuildingSale");Set("paused",true);Show();Assert.Zero((int)Get("confirmSaleBuilding"));Assert.False(root.Q<Button>("repair-building").enabledSelf);
    }
    [Test] public void WaitingRepairRemainsCancellableAndSaleShowsConsequences()
    {
        Show(repairing:true,waiting:true);Assert.True(root.Q<Button>("repair-building").enabledSelf);Assert.That(root.Q<Button>("repair-building").text,Does.Contain("Отменить"));Assert.That(root.Q<Label>("building-lifecycle-status").text,Does.Contain("ожидает кредитов"));
        Show();Assert.That(root.Q<Label>("building-lifecycle-status").text,Does.Contain("Последний центр"));Assert.That(root.Q<Label>("building-lifecycle-status").text,Does.Contain("3"));
        Show(blocked:"Демонтаж необратим",selling:true);Assert.False(root.Q<Button>("sell-building").enabledSelf);Assert.That(root.Q<Label>("building-lifecycle-status").text,Does.Contain("Демонтаж"));
    }
}
