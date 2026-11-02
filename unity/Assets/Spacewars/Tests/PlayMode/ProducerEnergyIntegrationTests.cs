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
using Object=UnityEngine.Object;

namespace Spacewars.Tests.PlayMode
{
    public sealed class ProducerEnergyIntegrationTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static object Call(object value,string method,params object[] args)=>value.GetType().GetMethod(method,F).Invoke(value,args);
        static readonly string Output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/producer-energy-integration/images"));
        static void Capture(RenderTexture target,string name)
        {
            Directory.CreateDirectory(Output);var old=RenderTexture.active;RenderTexture.active=target;
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try{pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),pixels.EncodeToPNG());}
            finally{RenderTexture.active=old;Object.Destroy(pixels);}
        }
        [UnityTest] public IEnumerator CompletedDomainUpgradeSelectsApprovedLiveModelsAndAllTiers()
        {
            var profile=PlayableProfile.Default;var domain=new PlayableDomain(profile,819,false);
            PlayableSnapshot View()=>domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            var host=new GameObject("Producer energy integration");PlayableWorld world=null;RenderTexture target=null;
            var ambient=RenderSettings.ambientLight;var mode=RenderSettings.ambientMode;
            try
            {
                Call(domain,"AddCredits",PlayableOwner.Player,10000d);
                int Build(PlayableBuildingKind kind,int slot){Assert.AreEqual(PlayableCommandStatus.Applied,Call(domain,"BuildAt",1,slot,kind,1,PlayableOwner.Player,null));Call(domain,"AdvanceFoundations");Call(domain,"AdvanceBuildings",100d);return View().Buildings.Single(b=>b.SlotId==slot).Id;}
                int factory=Build(PlayableBuildingKind.Factory,1),hangar=Build(PlayableBuildingKind.Hangar,2);Build(PlayableBuildingKind.ScientificCenter,3);
                world=new PlayableWorld(host.transform,profile,staged:true);yield return world.Preparation;world.Fog.ShowPublic();
                var light=new GameObject("Review light").AddComponent<Light>();light.transform.SetParent(host.transform);IndustrialLightingProfile.Load().ApplyScene(light);
                var camera=new GameObject("Review camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.orthographicSize=2.7f;camera.nearClipPlane=.1f;camera.farClipPlane=500;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.12f,.15f);
                target=new RenderTexture(1100,900,24);target.Create();camera.targetTexture=target;
                var actors=new System.Collections.Generic.Dictionary<int,PlayableWorld.Actor>();
                foreach(var b in View().Buildings){var actor=world.Building(b.Id,b.Kind.ToString(),true,b.ModelUpgraded);actor.Root.position=world.Point(b.Position);world.FaceBuilding(actor);actors.Add(b.Id,actor);}
                void Frame(int id,string name){var a=actors[id];var p=a.Root.position;camera.transform.position=p+new Vector3(4,6,-7);camera.transform.LookAt(p+Vector3.up*.7f);camera.Render();Capture(target,name);}
                yield return null;Frame(factory,"factory-before");Frame(hangar,"hangar-before");long seq=0;
                foreach(int id in new[]{factory,hangar})Assert.AreEqual(PlayableCommandStatus.Applied,Call(domain,"Apply",new PlayableCommand(819,++seq,"player-1",PlayableCommandKind.UpgradeProducer,new[]{id}),null));
                Call(domain,"AdvanceProduction",.1d);Assert.False(View().Buildings.Single(b=>b.Id==factory).ModelUpgraded);
                Call(domain,"AdvanceProduction",Math.Max(profile.ProducerUpgradeSeconds(PlayableBuildingKind.Factory),profile.ProducerUpgradeSeconds(PlayableBuildingKind.Hangar)));
                var view=View();
                foreach(int id in new[]{factory,hangar})
                {
                    var b=view.Buildings.Single(x=>x.Id==id);Assert.True(b.ProducerUpgraded);var a=actors[id];var scale=a.Hull.localScale;var selection=a.Selection.transform.localScale;
                    // Same entry point used by both ordinary and legacy local rendering.
                    world.UpdateFactoryModel(a,b,view);world.UpdateHangar(a,b,view.Tick);world.FaceBuilding(a);
                    PlayableWorld.PaintOwner(a,id==factory?new Color(.35f,.85f,1):new Color(1,.35f,.45f));
                    Assert.AreEqual(scale,a.Hull.localScale);Assert.AreEqual(selection,a.Selection.transform.localScale);
                    Assert.AreEqual(1,a.Hull.GetComponentsInChildren<Transform>(true).Count(t=>t.name=="energy-2"));Assert.NotNull(a.DetailLod);
                    Assert.AreSame(PlayableWorld.ModelPrefab(OrbitalPrecision.BuildingArtwork(b)).GetComponent<ModelDetailLod>().OriginalMeshes.Last(),a.DetailLod.OriginalMeshes.Last());
                    if(id==factory){Assert.NotNull(a.Factory);Assert.True(a.Factory.ExternallyDriven);}else Assert.NotNull(a.Hull.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="launchPlatform"));
                    yield return null;
                    for(int tier=0;tier<3;tier++){a.DetailLod.SetDetail(tier);Frame(id,(id==factory?"factory":"hangar")+"-upgraded-lod"+tier);}
                    a.DetailLod.SelectScreenRatio(float.PositiveInfinity,ModelDetailProfile.Current);Assert.AreEqual(0,a.DetailLod.Level);
                    a.DetailLod.SelectScreenRatio(0,ModelDetailProfile.Current);Assert.AreEqual(2,a.DetailLod.Level);
                    var restored=world.Building(id+100,b.Kind.ToString(),false,b.ModelUpgraded);Assert.AreEqual(a.Hull.name,restored.Hull.name,"New/restored actor must use the same variant");
                }
                Call(domain,"FinishManually");var result=(MatchResult)domain.GetType().GetProperty("Result",F).GetValue(domain);
                var resultUi=new PostMatchResultsView(result,()=>{},()=>{},()=>{});
                var facts=result.Facts.Where(f=>f.Kind==MatchFactKind.RefineryUpgradeCompleted).ToArray();Assert.AreEqual(2,facts.Length);
                foreach(var fact in facts)Assert.AreEqual(OrbitalPrecision.BuildingArtwork((PlayableBuildingKind)fact.EntityKind,true),Call(resultUi,"GlyphOf",fact));
            }
            finally{domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();world?.Dispose();RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=mode;if(target){target.Release();Object.Destroy(target);}Object.Destroy(host);}
        }
        [UnityTest] public IEnumerator UpgradeActionsAndSelectionUseBakedProducerPortraits()
        {
            var host=new GameObject("Producer upgrade UI");RenderTexture target=null;
            try
            {
                var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
                typeof(PlayableBootstrap).GetField("profile",F).SetValue(hud,PlayableProfile.Default);Call(hud,"CreateHud");
                var root=(VisualElement)typeof(PlayableBootstrap).GetField("root",F).GetValue(hud);root.Clear();root.style.backgroundColor=new Color(.035f,.055f,.07f);
                var title=new Label("Улучшения производителей · портреты с принятых моделей B");title.style.fontSize=24;title.style.color=Color.white;title.style.marginLeft=24;title.style.marginTop=16;root.Add(title);
                var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.marginTop=24;row.style.marginLeft=24;root.Add(row);
                var refresh=new System.Collections.Generic.List<Action>();
                foreach(var kind in new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Hangar})
                {
                    var b=new PlayableBuildingSnapshot(7,PlayableOwner.Player,kind,default,250,1,0,0,default,upgrade:new PlayableRefineryUpgradeSnapshot(false,false,0,0,30,null),lifecycle:new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false));
                    var view=new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,Array.Empty<PlayableEntitySnapshot>(),new[]{b},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,activeProfile:PlayableProfile.Default);
                    var actions=OfflinePadWorldActions.Building(view,PlayableProfile.Default,7);var action=actions.Single(a=>a.Sector.Id.EndsWith(":producer-upgrade"));var key=OrbitalPrecision.BuildingArtwork(kind,true);
                    Assert.AreEqual(key,action.Presentation.Artwork);Assert.True(action.Presentation.Upgrade);
                    Assert.NotNull(DockArtwork.TextureFor(key));Assert.NotNull(DockArtwork.TextureFor(key+"-outline"));Assert.AreNotSame(DockArtwork.TextureFor(OrbitalPrecision.BuildingGlyph(kind)),DockArtwork.TextureFor(key));
                    var frame=PortraitFraming.UvFor(key);Assert.That(frame.width,Is.InRange(.1f,.99f));Assert.That(frame.height,Is.InRange(.1f,.99f));
                    var panel=new VisualElement();panel.style.width=550;panel.style.marginRight=25;row.Add(panel);
                    var heading=new Label(kind==PlayableBuildingKind.Factory?"ФАБРИКА":"АНГАР");heading.style.color=Color.white;heading.style.fontSize=20;panel.Add(heading);
                    var ring=new OrbitalBattleRing();ring.style.position=Position.Relative;ring.style.display=DisplayStyle.Flex;ring.Resize(460);ring.Set(actions,heading.text,null,0,OrbitalPrecision.BuildingGlyph(kind));panel.Add(ring);refresh.Add(()=>ring.Set(actions,heading.text,null,0,OrbitalPrecision.BuildingGlyph(kind)));
                    var comparison=new VisualElement();comparison.style.flexDirection=FlexDirection.Row;panel.Add(comparison);
                    foreach(bool upgraded in new[]{false,true}){var portrait=new ModelPortrait();portrait.style.width=portrait.style.height=130;portrait.Set(OrbitalPrecision.BuildingArtwork(kind,upgraded));comparison.Add(portrait);}
                    var selected=new PlayableBuildingSnapshot(7,PlayableOwner.Enemy,kind,default,250,1,0,0,default,includePrivateState:false,producerUpgraded:true);
                    Assert.AreEqual(key,OrbitalPrecision.BuildingArtwork(selected));
                    typeof(PlayableBootstrap).GetField("view",F).SetValue(hud,view);Call(hud,"UpdateBuildingDock",selected,null);
                    Assert.AreSame(DockArtwork.TextureFor(key),((ModelPortrait)typeof(PlayableBootstrap).GetField("dockPortrait",F).GetValue(hud)).Texture);
                }
                var settings=host.GetComponent<UIDocument>().panelSettings;settings.scaleMode=PanelScaleMode.ConstantPixelSize;settings.scale=1;
                target=new RenderTexture(1200,760,24);target.Create();settings.targetTexture=target;
                // Ordinary HUD refreshes descriptors every frame after panel text metrics exist.
                for(int i=0;i<10;i++){yield return null;foreach(var update in refresh)update();}
                foreach(var ring in root.Query<OrbitalBattleRing>().ToList()){
                    var art=ring.Q<Button>("radial-7:producer-upgrade").Q<RadialArtwork>();
                    Assert.Greater(art.resolvedStyle.width,30);Assert.Greater(art.resolvedStyle.height,30);
                }
                Capture(target,"producer-upgrade-actions");settings.targetTexture=null;
            }
            finally{if(target){target.Release();Object.Destroy(target);}Object.Destroy(host);}
        }
    }
}
