using System;
using System.Collections;
using System.Collections.Generic;
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

namespace Spacewars.Tests.PlayMode
{
    public sealed class DefenseTowerMenuReviewTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        [UnityTest] public IEnumerator EmptyAndBuiltPadsUseDistinctSilhouettesAndCorrectActions()
        {
            var p=PlayableProfile.Default;var site=TerritoryRules.Sites(p).First(s=>s.Kind==PlayableBuildingKind.Headquarters);
            var pad=site.DefenseSlots(p).First();
            var center=new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,site.Position,400,1,0,0,default,siteId:site.Id);
            var sites=new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)};
            var host=new GameObject("Defense menu review");var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
            void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,F).SetValue(hud,value);
            T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,F).GetValue(hud);
            void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,F).Invoke(hud,null);
            Set("profile",p);Call("CreateHud");var root=Get<VisualElement>("root");root.style.width=960;root.style.height=640;root.style.backgroundColor=OrbitalTheme.Background;
            var panel=host.GetComponent<UIDocument>().panelSettings;panel.scale=1;
            var texture=new RenderTexture(960,640,24);texture.Create();panel.targetTexture=texture;
            var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/repair-tower-f2"));Directory.CreateDirectory(folder);
            var rings=new List<OrbitalBattleRing>();
            void Capture(string name)
            {
                var old=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(960,640,TextureFormat.RGBA32,false);
                try{pixels.ReadPixels(new Rect(0,0,960,640),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),pixels.EncodeToPNG());}
                finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(pixels);}
            }
            try
            {
                var sentry=OrbitalGlyph.TextureFor("sentry-turret");var repair=OrbitalGlyph.TextureFor("repair-tower");var wrench=OrbitalGlyph.TextureFor("repair");
                Assert.NotNull(sentry);Assert.NotNull(repair);Assert.NotNull(wrench);Assert.AreNotSame(sentry,repair);Assert.AreNotSame(repair,wrench);
                foreach(var kind in new[]{PlayableBuildingKind.SentryTurret,PlayableBuildingKind.RepairTower})
                {var glyph=OrbitalPrecision.BuildingGlyph(kind);Assert.AreSame(kind==PlayableBuildingKind.SentryTurret?sentry:repair,OrbitalGlyph.TextureFor(glyph));}
                for(int state=0;state<3;state++)
                {
                    var kind=state==1?PlayableBuildingKind.RepairTower:PlayableBuildingKind.SentryTurret;
                    double max=TerritoryRules.Health(p,kind);
                    var life=new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,(int)Math.Floor(TerritoryRules.Cost(p,kind)*p.BuildingSaleRefundRatio),false);
                    var tower=new PlayableBuildingSnapshot(10,PlayableOwner.Player,kind,pad.Position,(int)max,1,0,0,default,siteId:site.Id,slotId:pad.Id,parentId:1,lifecycle:life,maxHealth:max);
                    var buildings=state==0?new[]{center}:new[]{center,tower};
                    var view=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,Array.Empty<PlayableEntitySnapshot>(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:sites,activeProfile:p);
                    Set("view",view);Set("selectedSite",state==0?site.Id:0);Set("selectedSlot",state==0?pad.Id:0);
                    var selection=Get<HashSet<int>>("selection");selection.Clear();if(state!=0)selection.Add(10);
                    Call("UpdateHud");yield return null;yield return null;Call("UpdateHud");yield return null;
                    var actions=Get<OfflinePadAction[]>("battleActions");Assert.AreEqual(2,actions.Length);
                    if(state==0)CollectionAssert.AreEqual(new[]{PlayableBuildingKind.SentryTurret,PlayableBuildingKind.RepairTower},actions.Select(a=>a.Command.Building).ToArray());
                    else
                    {
                        Assert.IsEmpty(OfflinePadWorldActions.Build(view,p,site.Id,pad.Id));
                        Assert.True(actions.Any(a=>a.Sector.Id.EndsWith(":repair")&&!a.Sector.Enabled),"Healthy tower cannot be repaired.");
                        Assert.True(actions.Any(a=>a.Sector.Id.EndsWith(":sale")&&a.Sector.Enabled));
                    }
                    var ring=Get<OrbitalBattleRing>("battleRing");Assert.AreEqual(DisplayStyle.Flex,ring.style.display.value);
                    foreach(var action in actions)Assert.NotNull(ring.Q<Button>("radial-"+action.Sector.Id));
                    var caption=ring.Query<Label>().ToList().Single(l=>l.text==(state==0?"ПОСТРОИТЬ":state==1?"РЕМОНТНАЯ\nВЫШКА":"ПУЛЕМЁТНАЯ\nТУРЕЛЬ"));
                    Assert.LessOrEqual(caption.MeasureTextSize(caption.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x,caption.resolvedStyle.width,"Centre words fit without automatic mid-word wrapping.");
                    for(int sector=0;sector<actions.Length;sector++)
                    {
                        var button=ring.Q<Button>("radial-"+actions[sector].Sector.Id);
                        var c=ring.worldBound.center;float inner=ring.resolvedStyle.width*OrbitalBattleRing.InnerRatio,outer=ring.resolvedStyle.width*OrbitalBattleRing.OuterRatio;
                        foreach(var element in button.Children().Where(e=>e is OrbitalGlyph||e is Label l&&!string.IsNullOrEmpty(l.text)))
                        {
                            var bounds=element.worldBound;
                            TestContext.WriteLine($"state={state} sector={sector} kind={element.GetType().Name} text={(element as Label)?.text} bounds={bounds} centre={c} inner={inner} outer={outer}");
                            if(sector==0)Assert.LessOrEqual(bounds.yMax,c.y-inner-4,"Upper sector content must clear the centre circle.");
                            else Assert.GreaterOrEqual(bounds.yMin,c.y+inner+4,"Lower sector content must clear the centre circle.");
                            Assert.GreaterOrEqual(bounds.yMin,c.y-outer+2,"Content stays inside the outer edge.");
                            Assert.LessOrEqual(bounds.yMax,c.y+outer-2,"Content stays inside the outer edge.");
                        }
                    }
                    Capture(state==0?"empty-pad-hud":state==1?"repair-tower-hud":"sentry-tower-hud");
                    ring.RemoveFromHierarchy();rings.Add(ring);var next=new OrbitalBattleRing();Set("battleRing",next);root.Add(next);
                }
                // Put the actual HUD controls side by side, without changing their size or contents.
                root.Clear();root.style.height=640;
                var titles=new[]{"ПУСТАЯ ПЛОЩАДКА","РЕМОНТНАЯ ВЫШКА","ПУЛЕМЁТНАЯ ТУРЕЛЬ"};
                for(int i=0;i<3;i++)
                {
                    var title=new Label(titles[i]);title.style.position=Position.Absolute;title.style.left=i*320;title.style.top=110;title.style.width=320;title.style.unityTextAlign=TextAnchor.MiddleCenter;title.style.fontSize=16;title.style.color=OrbitalTheme.Ink;root.Add(title);
                    var ring=rings[i];ring.style.left=i*320+55;ring.style.top=180;root.Add(ring);
                }
                var note=new Label("Построенные башни: полное здоровье, вне боя");note.style.position=Position.Absolute;note.style.top=440;note.style.width=960;note.style.unityTextAlign=TextAnchor.MiddleCenter;note.style.color=OrbitalTheme.Muted;note.style.fontSize=14;root.Add(note);
                yield return null;yield return null;yield return null;Capture("radial-menus");
            }
            finally{panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(host);}
        }
    }
}
