using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class PlayableProductionHudTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private GameObject host;
    private PlayableBootstrap hud;
    private VisualElement root;
    private void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(hud,value);
    private object Get(string name)=>typeof(PlayableBootstrap).GetField(name,Flags).GetValue(hud);
    private void Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(hud,args);
    [SetUp] public void Setup()
    {
        host=new GameObject("production HUD test");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        root=new VisualElement();Set("root",root);Set("profile",PlayableProfile.Default);
        Set("shkvalButton",new Button());Set("explorerButton",new Button());Set("tankButton",new Button());Set("queueLabel",new Label());Set("selectionLabel",new Label());Set("progress",new ProgressBar());
        Call("CreateProductionHud",root);
    }
    [TearDown] public void Cleanup()=>UnityEngine.Object.DestroyImmediate(host);
    private void Show(long[] ids,bool paused=false,PlayableEntityKind kind=PlayableEntityKind.Tank)
    {
        var orders=ids.Select((id,i)=>new PlayableProductionOrderSnapshot(id,kind,150,3,15,i==0?7.5:15,i==0)).ToArray();
        var factory=new PlayableBuildingSnapshot(5,PlayableOwner.Player,PlayableBuildingKind.Factory,default,250,1,ids.Length,0.5,default,orders:orders);
        var view=new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,900,null,Array.Empty<PlayableEntitySnapshot>(),new[]{factory},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,population:new PlayablePopulationSnapshot(9,ids.Length>0?3:0,100));
        Set("view",view);Set("paused",paused);((Label)Get("selectionLabel")).text="ФАБРИКА";Call("UpdateProductionHud",factory);
    }
    [Test] public void ShkvalQueueShowsActualKindAndPause()
    {
        Show(new long[]{20,21},kind:PlayableEntityKind.Shkval);var slots=root.Query<Button>().ToList();Assert.AreEqual("50%\n8с",slots[0].text);Assert.AreEqual("Ждёт",slots[1].text);Assert.AreEqual("shkval",slots[0].Q<OrbitalGlyph>().Kind);Assert.That(slots[0].tooltip,Does.Contain("Шквал"));Assert.True(((Button)Get("shkvalButton")).enabledSelf);
        Show(new long[]{20,21},true,PlayableEntityKind.Shkval);Assert.False(((Button)Get("shkvalButton")).enabledSelf);
    }
    [Test] public void ExplorerQueueShowsKindProgressAndPausedControl()
    {
        Show(new long[]{20,21},kind:PlayableEntityKind.Explorer);var slots=root.Query<Button>().ToList();
        Assert.AreEqual("50%\n8с",slots[0].text);Assert.AreEqual("Ждёт",slots[1].text);
        Assert.AreEqual("explorer",slots[0].Q<OrbitalGlyph>().Kind);Assert.That(slots[0].tooltip,Does.Contain("Исследователь"));Assert.That(((ProgressBar)Get("progress")).title,Does.Contain("Исследователь"));Assert.True(((Button)Get("explorerButton")).enabledSelf);
        Show(new long[]{20,21},true,PlayableEntityKind.Explorer);Assert.False(((Button)Get("explorerButton")).enabledSelf);
    }
    [Test] public void SixControlsKeepIdentityWhileOrdersShiftAndPauseDisablesActions()
    {
        Show(new long[]{10,11,12});var before=root.Query<Button>().ToList();Assert.AreEqual(6,before.Count);
        Assert.That(((ProgressBar)Get("progress")).title,Does.Contain("8 с"));
        Assert.AreEqual("50%\n8с",before[0].text);Assert.AreEqual("Ждёт",before[1].text);
        Assert.True(before[2].enabledSelf);Assert.False(before[3].enabledSelf);
        Assert.That(((Label)Get("selectionLabel")).text,Does.Contain("9 (+3) / 100"));
        Show(new long[]{11,12});var after=root.Query<Button>().ToList();
        for(int i=0;i<6;i++)Assert.AreSame(before[i],after[i],"Snapshot publication must not replace clickable controls.");
        Assert.That(after[0].tooltip,Does.Contain("№11"));Assert.That(after[1].tooltip,Does.Contain("№12"));
        Show(new long[]{11,12},true);Assert.True(after.All(b=>!b.enabledSelf));
        Show(Array.Empty<long>());Assert.True(after.All(b=>!b.enabledSelf&&b.text=="—"));
    }
}
