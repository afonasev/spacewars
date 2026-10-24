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
        var upgrade=new PlayableRefineryUpgradeSnapshot(active,complete,200,5,20,reason);
        var lifecycle=new PlayableBuildingLifecycleSnapshot(selling,0,false,false,0,0,null,null,1,300,false);
        Call("UpdateScienceHud",new PlayableBuildingSnapshot(1,own?PlayableOwner.Player:PlayableOwner.Enemy,PlayableBuildingKind.Refinery,default,200,1,0,0,default,includePrivateState:own,lifecycle:lifecycle,upgrade:upgrade,refineryUpgraded:complete));
    }
    [Test] public void BlockedActionRemainsFocusableAndExplainsRequirementWithoutCommand()
    {
        Show(reason:"Нужен Научный центр");var action=root.Q<Button>("upgrade-refinery");
        Assert.True(action.focusable);Assert.True(action.enabledSelf);Assert.False((bool)Get("scienceActionAllowed"));
        Assert.That(action.tooltip,Does.Contain("Научный центр"));Assert.That(action.tooltip,Does.Contain("200"));Assert.That(action.tooltip,Does.Contain("20"));
        Assert.That(root.Q<Label>("refinery-upgrade-reason").text,Does.Contain("Научный центр"));
        Assert.AreEqual(DisplayStyle.None,root.Q<ProgressBar>("refinery-upgrade-progress").style.display.value);
    }
    [Test] public void ProgressIsCancellableButPauseSaleAndEnemyAreNotInteractive()
    {
        Show(active:true,reason:"Улучшение уже начато");Assert.True((bool)Get("scienceActionAllowed"));
        Assert.AreEqual(25,root.Q<ProgressBar>("refinery-upgrade-progress").value);Assert.That(root.Q<Button>("upgrade-refinery").text,Does.Contain("Отменить"));
        Set("paused",true);Show(active:true);Assert.False((bool)Get("scienceActionAllowed"));
        Set("paused",false);Show(active:true,selling:true);Assert.False((bool)Get("scienceActionAllowed"));
        Show(own:false);Assert.AreEqual(DisplayStyle.None,root.Q<VisualElement>("science-controls").style.display.value);Assert.Zero((int)Get("scienceRefinery"));
        Show(complete:true,reason:"Завод уже улучшен");Assert.False((bool)Get("scienceActionAllowed"));Assert.That(root.Q<Label>("refinery-upgrade-reason").text,Does.Contain("15"));
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
        var salvo=root.Q<Button>("research-HawkSalvoSystems");Assert.NotNull(salvo);Assert.AreEqual("hawk-salvo",salvo.Q<OrbitalGlyph>().Kind);Assert.NotNull(salvo.Q<OrbitalGlyph>().Q<Image>("glyph-artwork").image);Assert.False(salvo.enabledSelf);Assert.That(salvo.tooltip,Does.Contain("ангар"));Assert.That(salvo.tooltip,Does.Contain("300"));Assert.That(salvo.text,Does.Contain("Системы залпового огня"));
        Assert.False(root.Q<Button>("research-TankChassis").enabledSelf);Assert.That(root.Q<Button>("research-TankChassis").tooltip,Does.Contain("фабрика"));
        Assert.AreEqual(6,root.Q<VisualElement>("research-queue").Query<Button>().ToList().Count);
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

    [Test] public void SalvoIconLoadsDedicatedTransparentArtworkForMouseAndGamepad()
    {
        Assert.AreEqual("hawk-salvo",OrbitalPrecision.ResearchGlyph(PlayableResearchKind.HawkSalvoSystems));
        Assert.AreEqual("hawk-salvo",OrbitalPrecision.Glyph("7:HawkSalvoSystems"));
        Assert.AreEqual("guidance",OrbitalPrecision.ResearchGlyph(PlayableResearchKind.ShkvalGuidance));
        var icon=OrbitalGlyph.TextureFor("hawk-salvo");Assert.NotNull(icon);Assert.AreNotSame(icon,OrbitalGlyph.TextureFor("guidance"));
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
