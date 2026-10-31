using System.Linq;
using System;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class PlayableScienceHudTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private GameObject host;private PlayableBootstrap hud;private VisualElement root;
    private void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(hud,value);
    private object Get(string name)=>typeof(PlayableBootstrap).GetField(name,Flags).GetValue(hud);
    private void Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(hud,args);
    [SetUp] public void Setup()
    {
        host=new GameObject("science HUD test");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        root=new VisualElement();Set("root",root);Set("profile",PlayableProfile.Default);Call("CreateScienceHud",root);
        Set("view",new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,900,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
    }
    [TearDown] public void Cleanup()=>UnityEngine.Object.DestroyImmediate(host);
    private void Show(bool active=false,bool complete=false,string reason=null,bool own=true,bool selling=false)
    {
        var profile=(PlayableProfile)Get("profile");
        var upgrade=new PlayableRefineryUpgradeSnapshot(active,complete,profile.RefineryUpgradeCost,profile.RefineryUpgradeSeconds/4,profile.RefineryUpgradeSeconds,reason);
        var lifecycle=new PlayableBuildingLifecycleSnapshot(selling,0,false,false,0,0,null,null,1,300,false);
        Call("UpdateScienceHud",new PlayableBuildingSnapshot(1,own?PlayableOwner.Player:PlayableOwner.Enemy,PlayableBuildingKind.Refinery,default,200,1,0,0,default,includePrivateState:own,lifecycle:lifecycle,upgrade:upgrade,refineryUpgraded:complete));
    }
    [UnityEngine.TestTools.UnityTest] public System.Collections.IEnumerator CaptureTwoIndependentScienceQueues()
    {
        Call("CreateHud");root=(VisualElement)Get("root");root.Clear();
        root.style.width=1200;root.style.height=700;root.style.backgroundColor=new Color(.055f,.08f,.10f);root.style.paddingTop=24;root.style.paddingLeft=24;root.style.paddingRight=24;
        var title=new Label("Два научных центра · независимые очереди");title.style.fontSize=26;title.style.color=Color.white;title.style.marginBottom=20;root.Add(title);
        var note=new Label("Снимок компонентов HUD на заданных состояниях: по одному активному исследованию в каждом центре");note.style.fontSize=15;note.style.color=Color.gray;root.Add(note);
        var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.marginTop=24;root.Add(row);
        var orders=new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,7,300,12,30,true,false),new PlayableResearchOrderSnapshot(2,PlayableResearchKind.ExplorerAssaultGuns,7,0,0,30,false,false),new PlayableResearchOrderSnapshot(3,PlayableResearchKind.ImprovedWeaponSystems,7,0,0,30,false,false),new PlayableResearchOrderSnapshot(4,PlayableResearchKind.ShkvalGuidance,8,300,18,30,true,false),new PlayableResearchOrderSnapshot(5,PlayableResearchKind.ReinforcedConcrete,8,0,0,30,false,false)};
        foreach(int id in new[]{7,8})
        {
            var column=new VisualElement();column.style.width=550;column.style.marginRight=22;row.Add(column);
            var heading=new Label(id==7?"Центр A · шасси 40%":"Центр B · наведение 60%");heading.style.fontSize=22;heading.style.color=Color.white;heading.style.marginBottom=16;column.Add(heading);
            Call("CreateScienceHud",column);Call("UpdateScienceHud",new PlayableBuildingSnapshot(id,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,default,250,1,0,0,default,lifecycle:new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false),research:orders));
            Assert.That(column.Q<Button>("research-slot-0").text,Does.Contain(id==7?"40%":"60%"));Assert.That(column.Q<Button>("research-slot-1").text,Does.Contain("ждёт"));
        }
        var panel=host.GetComponent<UIDocument>().panelSettings;panel.scaleMode=PanelScaleMode.ConstantPixelSize;panel.scale=1;
        var texture=new RenderTexture(1200,700,24);texture.Create();panel.targetTexture=texture;
        try
        {
            for(int i=0;i<8;i++)yield return null;
            var old=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(1200,700,TextureFormat.RGBA32,false);
            try{pixels.ReadPixels(new Rect(0,0,1200,700),0,0);pixels.Apply();var folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/balance-research-projectiles"));System.IO.Directory.CreateDirectory(folder);System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,"parallel-science-hud.png"),pixels.EncodeToPNG());}
            finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(pixels);}
        }
        finally{panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
    }
    [Test] public void SelectedCenterShowsOnlyItsQueueButKeepsGlobalTechnologyStatus()
    {
        var orders=new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,7,300,6,30,true,false),new PlayableResearchOrderSnapshot(2,PlayableResearchKind.ExplorerAssaultGuns,8,300,15,30,true,false),new PlayableResearchOrderSnapshot(3,PlayableResearchKind.ShkvalGuidance,7,0,0,0,false,false)};
        PlayableBuildingSnapshot Center(int id)=>new PlayableBuildingSnapshot(id,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,default,250,1,0,0,default,lifecycle:new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false),research:orders);
        Call("UpdateScienceHud",Center(7));
        Assert.That(root.Q<Button>("research-slot-0").tooltip,Does.Contain("Шасси"));
        Assert.That(root.Q<Button>("research-slot-1").tooltip,Does.Contain("Наведение"));
        Assert.AreEqual("Свободный слот",root.Q<Button>("research-slot-2").tooltip);
        Assert.False(root.Q<Button>("research-ExplorerAssaultGuns").enabledSelf,"Other center's technology cannot be duplicated or cancelled here");
        Call("UpdateScienceHud",Center(8));
        Assert.That(root.Q<Button>("research-slot-0").tooltip,Does.Contain("Штурмовые орудия"));
        Assert.AreEqual("Свободный слот",root.Q<Button>("research-slot-1").tooltip);
        Assert.True(root.Q<Button>("research-ExplorerAssaultGuns").enabledSelf);Assert.False(root.Q<Button>("research-TankChassis").enabledSelf);
    }
    // Exercise the shipped profile and distinct authoring values: HUD text follows
    // the active profile, while cancellation/progress follows the order snapshot.
    private static System.Collections.Generic.IEnumerable<PlayableProfile> RefineryProfiles()
    {
        yield return PlayableProfile.Default;
        var data=PlayableProfile.Default.CopyData();
        data.refineryUpgradeCost=375;data.refineryUpgradeSeconds=28;data.refineryUpgradedIncome=35;data.incomePeriodSeconds=7;
        yield return PlayableProfile.Create(data);
    }
    [Test] public void BlockedActionRemainsFocusableAndExplainsRequirementWithoutCommand()
    {
        foreach(var profile in RefineryProfiles())
        {
            Set("profile",profile);
            Show(reason:"Нужен готовый собственный улучшенный научный центр");var action=root.Q<Button>("upgrade-refinery");
            Assert.True(action.focusable);Assert.True(action.enabledSelf);Assert.False((bool)Get("scienceActionAllowed"));
            Assert.AreEqual("Улучшить завод · "+profile.RefineryUpgradeCost.ToString("0"),action.text);
            Assert.AreEqual("Цена: "+profile.RefineryUpgradeCost.ToString("0")+" · Время: "+profile.RefineryUpgradeSeconds.ToString("0.#")+" с · Нужен готовый собственный улучшенный научный центр",action.tooltip);
            Assert.That(root.Q<Label>("refinery-upgrade-reason").text,Does.Contain("улучшенный научный центр"));
            Assert.AreEqual(DisplayStyle.None,root.Q<ProgressBar>("refinery-upgrade-progress").style.display.value);
        }
    }
    [UnityEngine.TestTools.UnityTest] public System.Collections.IEnumerator CaptureImprovedCenterRequirement()
    {
        Call("CreateHud");root=(VisualElement)Get("root");root.Clear();Call("CreateScienceHud",root);var profile=PlayableProfile.Default;Set("profile",profile);
        var panel=host.GetComponent<UIDocument>().panelSettings;var target=new RenderTexture(1000,500,24);target.Create();panel.targetTexture=target;root.style.backgroundColor=OrbitalTheme.Background;
        yield return null;yield return null;root.style.width=1000/panel.scale;root.style.height=500/panel.scale;
        try{Show(reason:"Нужен готовый собственный улучшенный научный центр");yield return null;yield return null;
            Assert.That(root.Q<Label>("refinery-upgrade-reason").text,Does.Contain("улучшенный научный центр"));Assert.False((bool)Get("scienceActionAllowed"));
            var old=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(1000,500,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1000,500),0,0);pixels.Apply();RenderTexture.active=old;
            var output=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/qa/army-prices"));System.IO.Directory.CreateDirectory(output);System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"refinery-blocked.png"),pixels.EncodeToPNG());UnityEngine.Object.Destroy(pixels);
        }finally{panel.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);}
    }
    [Test] public void ProgressIsCancellableButPauseSaleAndEnemyAreNotInteractive()
    {
        foreach(var profile in RefineryProfiles())
        {
            Set("profile",profile);
            Show(active:true,reason:"Улучшение уже начато");Assert.True((bool)Get("scienceActionAllowed"));
            Assert.AreEqual(25,root.Q<ProgressBar>("refinery-upgrade-progress").value);
            Assert.AreEqual("Отменить улучшение · +"+profile.RefineryUpgradeCost.ToString("0"),root.Q<Button>("upgrade-refinery").text);
            Set("paused",true);Show(active:true);Assert.False((bool)Get("scienceActionAllowed"));
            Set("paused",false);Show(active:true,selling:true);Assert.False((bool)Get("scienceActionAllowed"));
            Show(own:false);Assert.AreEqual(DisplayStyle.None,root.Q<VisualElement>("science-controls").style.display.value);Assert.Zero((int)Get("scienceRefinery"));
            Show(complete:true,reason:"Завод уже улучшен");Assert.False((bool)Get("scienceActionAllowed"));
            Assert.AreEqual("Доход: "+profile.RefineryUpgradedIncome.ToString("0")+" за "+profile.IncomePeriodSeconds+" с",root.Q<Label>("refinery-upgrade-reason").text);
        }
    }
    [Test] public void CompletedRefinerySwapsOnlyModelAndUsesAuthoredTurbineAxis()
    {
        var parent=new GameObject("refinery model test");
        try
        {
            using var world=new PlayableWorld(parent.transform,PlayableProfile.Default);var a=world.Building(1,"Refinery",true);var rootBefore=a.Root;var facing=a.Hull.rotation;
            var snapshot=new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Refinery,default,123,1,0,0,default,refineryUpgraded:true);
            world.UpdateRefineryModel(a,snapshot,30);Assert.AreSame(rootBefore,a.Root);Assert.True(a.RefineryUpgraded);Assert.NotNull(a.Turbine);Assert.Less(Quaternion.Angle(facing,a.Hull.rotation),.001f);
            a.Root.rotation=Quaternion.Euler(0,73,0);world.FaceBuilding(a);Assert.Less(Quaternion.Angle(facing,a.Hull.rotation),.001f);
            Assert.Less(Quaternion.Angle(a.Turbine.localRotation,Quaternion.Euler(0,0,-90)),.001f);
            var rotation=a.Turbine.localRotation;world.UpdateRefineryModel(a,snapshot,30);Assert.AreEqual(rotation,a.Turbine.localRotation);
            var camera=new GameObject("seat camera").AddComponent<Camera>();camera.transform.SetParent(parent.transform);camera.transform.rotation=Quaternion.Euler(45,0,0);
            world.FaceBuilding(a,camera);Assert.That(Vector3.Dot(a.Hull.forward,Vector3.back),Is.GreaterThan(.999));
            camera.transform.rotation=Quaternion.Euler(45,90,0);world.FaceBuilding(a,camera);
            Assert.That(Vector3.Dot(a.Hull.forward,Vector3.back),Is.GreaterThan(.999),"Camera orbit never rotates the building");
            Assert.NotNull(world.Building(2,"ScientificCenter",true).Hull);
        }
        finally{UnityEngine.Object.DestroyImmediate(parent);}
    }
    [Test] public void ScienceCenterShowsSixResearchSlotsWithActiveAndWaitingOrders()
    {
        var research=new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,7,300,15,30,true,false),new PlayableResearchOrderSnapshot(2,PlayableResearchKind.ExplorerAssaultGuns,7,0,0,30,false,false),new PlayableResearchOrderSnapshot(3,PlayableResearchKind.ShkvalGuidance,7,0,0,30,false,false)};
        var lifecycle=new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false);
        Call("UpdateScienceHud",new PlayableBuildingSnapshot(7,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,default,250,1,0,0,default,lifecycle:lifecycle,research:research));
        Assert.AreEqual(DisplayStyle.Flex,root.Q<VisualElement>("research-slots").style.display.value);
        Assert.That(root.Q<Button>("research-slot-0").text,Does.Contain("50%"));Assert.That(root.Q<Button>("research-slot-1").text,Does.Contain("ждёт"));Assert.AreEqual("—",root.Q<Button>("research-slot-5").text);
        Assert.True(root.Q<Button>("research-slot-0").enabledSelf);Assert.False(root.Q<Button>("research-slot-1").enabledSelf);
    }
    [Test] public void SalvoResearchUsesSixthActionAndExplainsProducerRequirement()
    {
        void Availability(bool ready)=>Set("view",new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,900,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,researchAvailability:new[]{new PlayableResearchAvailabilitySnapshot(PlayableResearchKind.HawkSalvoSystems,300,ready,ready?null:"Нужен готовый собственный ангар"),new PlayableResearchAvailabilitySnapshot(PlayableResearchKind.TankChassis,300,ready,ready?null:"Нужна готовая собственная фабрика")}));
        var center=new PlayableBuildingSnapshot(7,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,default,250,1,0,0,default,lifecycle:new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false),research:Array.Empty<PlayableResearchOrderSnapshot>());
        Availability(false);Call("UpdateScienceHud",center);
        var salvo=root.Q<Button>("research-HawkSalvoSystems");Assert.NotNull(salvo);Assert.AreEqual("hawk-salvo",salvo.Q<OrbitalGlyph>().Kind);Assert.NotNull(salvo.Q<OrbitalGlyph>().Q<Image>("glyph-portrait").image);Assert.False(salvo.enabledSelf);Assert.That(salvo.tooltip,Does.Contain("ангар"));Assert.That(salvo.tooltip,Does.Contain("300"));Assert.That(salvo.text,Does.Contain("Системы залпового огня"));
        Assert.False(root.Q<Button>("research-TankChassis").enabledSelf);Assert.That(root.Q<Button>("research-TankChassis").tooltip,Does.Contain("фабрика"));
        Assert.AreEqual(8,root.Q<VisualElement>("research-queue").Query<Button>().ToList().Count);
        Availability(true);Call("UpdateScienceHud",center);Assert.True(salvo.enabledSelf);Assert.True(root.Q<Button>("research-TankChassis").enabledSelf);
        Set("paused",true);Call("UpdateScienceHud",center);Assert.False(salvo.enabledSelf);
    }

    [Test] public void GamepadSalvoActionUsesOwnerAvailabilityAndExplainsMissingHangar()
    {
        var p=PlayableProfile.Default;var center=new PlayableBuildingSnapshot(7,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,default,250,1,0,0,default,lifecycle:new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false));
        foreach(bool ready in new[]{false,true}){
            var view=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,900,null,Array.Empty<PlayableEntitySnapshot>(),new[]{center},Array.Empty<PlayableProjectileSnapshot>(),null,null,researchAvailability:new[]{new PlayableResearchAvailabilitySnapshot(PlayableResearchKind.HawkSalvoSystems,300,ready,ready?null:"Нужен готовый собственный ангар")});
            var action=OfflinePadWorldActions.Building(view,p,7).Single(a=>a.Label=="Системы залпового огня");
            Assert.AreEqual(ready,action.Sector.Enabled);
            if(ready){Assert.NotNull(action.Command);Assert.AreEqual(PlayableCommandKind.QueueResearch,action.Command.Kind);Assert.AreEqual(PlayableResearchKind.HawkSalvoSystems,action.Command.Research);CollectionAssert.AreEqual(new[]{7},action.Command.Entities);}
            else{Assert.IsNull(action.Command);Assert.That(action.Detail,Does.Contain("ангар"));}
        }
    }

    [Test] public void SalvoUiUsesCurrentPortraitAndLegacySourceRetainsTransparency()
    {
        Assert.AreEqual("hawk-salvo",OrbitalPrecision.ResearchGlyph(PlayableResearchKind.HawkSalvoSystems));
        Assert.AreEqual("hawk-salvo",OrbitalPrecision.Glyph("7:HawkSalvoSystems"));
        Assert.AreEqual("guidance",OrbitalPrecision.ResearchGlyph(PlayableResearchKind.ShkvalGuidance));
        var current=OrbitalGlyph.TextureFor("hawk-salvo");Assert.AreSame(Resources.Load<Texture2D>("DockPortraits/hawkUpgraded"),current);Assert.NotNull(current);Assert.AreNotSame(current,OrbitalGlyph.TextureFor("guidance"));
        // Preserve the legacy source/import transparency contract independently of the new UI mapping.
        var icon=Resources.Load<Texture2D>("OrbitalIcons/hawk-salvo");Assert.NotNull(icon);
        Assert.AreEqual(400,icon.width);Assert.AreEqual(300,icon.height);
        var previous=RenderTexture.active;var target=RenderTexture.GetTemporary(400,300,0,RenderTextureFormat.ARGB32);
        var pixels=new Texture2D(400,300,TextureFormat.RGBA32,false);
        try{
            Graphics.Blit(icon,target);RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,400,300),0,0);pixels.Apply();
            Assert.Less(pixels.GetPixel(0,0).a,.01f,"True transparent canvas, not a black background");
            Assert.Greater(pixels.GetPixel(155,245).a,.99f,"Rotor hub is opaque");
            Assert.Less(pixels.GetPixel(155,166).a,.01f,"Cabin window is a transparent cutout");
            var colors=pixels.GetPixels32();var source=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try{
                Assert.True(source.LoadImage(System.IO.File.ReadAllBytes(System.IO.Path.Combine(Application.dataPath,"Spacewars/Content/Resources/OrbitalIcons/hawk-salvo.png"))));
                CollectionAssert.AreEqual(source.GetPixels32().Select(c=>c.a).ToArray(),colors.Select(c=>c.a).ToArray(),"GPU import/readback preserves every source silhouette alpha pixel");
            }
            finally{UnityEngine.Object.DestroyImmediate(source);}
            // Unity dilates RGB into alpha-zero texels for filtering. Remove only hidden
            // color from the evidence image so viewers that ignore alpha cannot show it.
            for(int i=0;i<colors.Length;i++)if(colors[i].a==0)colors[i]=new Color32(0,0,0,0);
            pixels.SetPixels32(colors);pixels.Apply();
            string output=System.Environment.GetEnvironmentVariable("SPACEWARS_HAWK_ICON_EVIDENCE");
            if(!string.IsNullOrEmpty(output)){System.IO.Directory.CreateDirectory(output);System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"imported-hawk-salvo.png"),pixels.EncodeToPNG());}
        }
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(pixels);}
    }

}
