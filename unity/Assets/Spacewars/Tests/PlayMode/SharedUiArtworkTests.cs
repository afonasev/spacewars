using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class SharedUiArtworkTests
{
    [TestCase("tank","tankHybrid")] [TestCase("explorer","explorer")] [TestCase("shkval","shkval")] [TestCase("hawk","hawk")]
    [TestCase("headquarters","headquarters")] [TestCase("outpost","outpost")] [TestCase("mine","mine")] [TestCase("factory","factory")]
    [TestCase("refinery","refinery")] [TestCase("science","scientificCenter")] [TestCase("hangar","hangar")]
    [TestCase("gauss-tower","gauss-tower")] [TestCase("sentry-turret","sentry-turret")] [TestCase("repair-tower","repair-tower")]
    [TestCase("chassis","tankHybridUpgraded")] [TestCase("assault","explorerUpgraded")] [TestCase("guidance","shkval-guidance-3")]
    [TestCase("hawk-salvo","hawkUpgraded")] [TestCase("upgrade","refineryUpgraded")]
    public void SharedConsumerUsesCurrentPortraitAndOwnerOutline(string key,string asset)
    {
        var expected=Resources.Load<Texture2D>("DockPortraits/"+asset);Assert.NotNull(expected);
        var glyph=new OrbitalGlyph(key);glyph.Set(key,Color.red,.4);
        Assert.AreSame(expected,OrbitalGlyph.TextureFor(key));Assert.AreSame(expected,glyph.Q<Image>("glyph-portrait").image);
        Assert.AreEqual(Color.white,glyph.Q<Image>("glyph-portrait").tintColor,"preserve actual model colors");
        Assert.AreEqual(Color.red,glyph.Q<Image>("portrait-outline").tintColor,"owner stays identifiable");
        Assert.AreEqual(DisplayStyle.None,glyph.Q<Image>("glyph-artwork").style.display.value);Assert.AreEqual(.4,glyph.Progress);
        glyph.Set("credits",Color.white);Assert.AreEqual(DisplayStyle.None,glyph.Q("glyph-model").style.display.value);Assert.NotNull(glyph.Q<Image>("glyph-artwork").image);
    }
    [TestCase("weapon-systems-1")] [TestCase("weapon-systems-2")] [TestCase("defensive-systems-1")] [TestCase("defensive-systems-2")] [TestCase("reinforced-concrete")]
    public void SharedResearchUsesWheelEmblem(string key)
    {
        var glyph=new OrbitalGlyph(key);Assert.AreEqual(DisplayStyle.Flex,glyph.Q("glyph-emblem").style.display.value);
        Assert.AreEqual(DisplayStyle.None,glyph.Q("glyph-model").style.display.value);Assert.IsNull(glyph.Q<Image>("glyph-artwork").image);
        glyph.Set("tank",OrbitalTheme.Ink);Assert.AreEqual(DisplayStyle.None,glyph.Q("glyph-emblem").style.display.value);Assert.NotNull(glyph.Q<Image>("glyph-portrait").image);
    }
    static MatchResult Result()
    {
        var facts=new List<MatchFact>();long sequence=0;
        void Add(PlayableOwner owner,MatchFactKind kind,int entity,double value=100)=>facts.Add(new MatchFact(300,++sequence,10,owner,kind,value,entity));
        foreach(var owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy})
        {
            foreach(var kind in new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk}){Add(owner,MatchFactKind.UnitOrdered,(int)kind);Add(owner,MatchFactKind.UnitCompleted,(int)kind);}
            foreach(var kind in new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Hangar,PlayableBuildingKind.ScientificCenter,PlayableBuildingKind.GaussTower}){Add(owner,MatchFactKind.BuildingOrdered,(int)kind);Add(owner,MatchFactKind.BuildingCompleted,(int)kind);}
            foreach(var kind in new[]{PlayableResearchKind.TankChassis,PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.AdvancedDefensiveSystems}){Add(owner,MatchFactKind.ResearchOrdered,(int)kind);Add(owner,MatchFactKind.ResearchCompleted,(int)kind);}
            Add(owner,MatchFactKind.Army,0,20);Add(owner,MatchFactKind.Income,0,500);
        }
        var players=new[]{new MatchResultPlayer(0,PlayableOwner.Player,1,"one","Игрок 1",false,"",facts,10),new MatchResultPlayer(1,PlayableOwner.Enemy,2,"two","Игрок 2",false,"",facts,10)};
        return new MatchResult(1800,60,19092026,"UI artwork fixture",null,true,players,facts.ToArray());
    }
    [UnityTest] public IEnumerator ResultsTablesGraphsAndLogUseCurrentArtwork()
    {
        var host=new GameObject("Shared result artwork UI");var panel=ScriptableObject.CreateInstance<PanelSettings>();var target=new RenderTexture(1280,720,24);target.Create();
        try
        {
            var doc=host.AddComponent<UIDocument>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ConstantPixelSize;panel.targetTexture=target;doc.panelSettings=panel;
            var root=doc.rootVisualElement;root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");OrbitalTheme.Install(root);
            var ui=new PostMatchResultsView(Result(),()=>{},()=>{},()=>{});root.Add(ui.Root);
            foreach(int column in new[]{1,2,6})
            {
                ui.ShowTab(0);yield return null;yield return null;
                var cell=ui.Root.Q<Label>("result-cell-0-"+column);Assert.NotNull(cell);((Action)cell.userData)();yield return null;yield return null;
                var icons=ui.Root.Q("result-popup-scroll").Query<OrbitalGlyph>().ToList();Assert.AreEqual(column==6?3:4,icons.Count);
                foreach(var icon in icons)
                {
                    Assert.True(icon.Q<Image>("glyph-portrait").image!=null||icon.Q("glyph-emblem").style.display.value==DisplayStyle.Flex);
                    if(column==1){Assert.AreEqual(OrbitalTheme.Ink,icon.Q<Image>("portrait-outline").tintColor);Assert.AreEqual(Color.white,icon.Q<Image>("glyph-portrait").tintColor);}
                    var viewport=ui.Root.Q<ScrollView>("result-popup-scroll").contentViewport.worldBound;
                    Assert.True(viewport.Contains(icon.worldBound.min)&&viewport.Contains(icon.worldBound.max),"all totals icons fit the popup");
                }
                Save(target,"results-"+(column==1?"units":column==2?"buildings":"research"));
            }
            ui.ShowTab(1);yield return null;yield return null;ui.InspectTime(10);yield return null;yield return null;Save(target,"results-graphs");
            ui.ShowTab(2);yield return null;yield return null;
            Assert.AreEqual(DisplayStyle.None,ui.Root.Q("result-graph-tooltip").style.display.value);
            var unitIcons=ui.Root.Query<OrbitalGlyph>().ToList().Where(g=>new[]{"explorer","tank","shkval","hawk"}.Contains(g.Kind)).ToArray();Assert.AreEqual(8,unitIcons.Length);
            foreach(var icon in unitIcons){Assert.AreEqual(OrbitalTheme.Ink,icon.Q<Image>("portrait-outline").tintColor);Assert.AreEqual(Color.white,icon.Q<Image>("glyph-portrait").tintColor);}
            Assert.GreaterOrEqual(ui.Root.Query<OrbitalGlyph>().ToList().Count,22,"action log uses images too");Save(target,"results-action-log");
        }
        finally{panel.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);}
    }
    static void Save(RenderTexture target,string name)
    {
        string output=Environment.GetEnvironmentVariable("UI_ICON_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
        var old=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try{image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();Assert.Greater(image.GetPixels32().Where((_,i)=>i%173==0).Distinct().Count(),8);File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());}
        finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
    }
}
