using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class NativeCompactLabTests
{
    [UnityTest] public IEnumerator CatalogSectionsUpgradesHelpAndResponsiveLayout()
    {
        var host=new GameObject("compact laboratory QA");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;
        var nav=new NativeMenuNavigation(root);var directory=Path.Combine(Path.GetTempPath(),"spacewars-compact-lab-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        RenderTexture texture=null;
        try
        {
            var view=new NativeBalanceView(root,nav,new NativeBalanceStore(PlayableProfile.Default,Path.Combine(directory,"balance.json")),()=>{},null);
            foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(800,620)})
            {
                texture=new RenderTexture(size.x,size.y,24);texture.Create();panel.targetTexture=texture;root.style.width=size.x;root.style.height=size.y;OrbitalTheme.ConfigurePanel(panel,root);
                for(int i=0;i<5;i++)yield return null;
                var card=view.Page.Q(className:"orbital-lab-card");Assert.That(card.worldBound.width,Is.EqualTo(size.x).Within(1));
                Assert.LessOrEqual(card.worldBound.yMax,size.y);Assert.Greater(card.worldBound.height,size.y-40);
                foreach(var key in new[]{"lab-save","lab-reset","lab-apply","lab-select","lab-back","lab-search","lab-tree"})
                {var e=view.Page.Q(key);Assert.Greater(e.worldBound.width,0,key);Assert.LessOrEqual(e.worldBound.xMax,size.x+.5f,key);Assert.LessOrEqual(e.worldBound.yMax,size.y+.5f,key);}
                foreach(var key in new[]{"tankHealth","tankChassisCost","tankChassisSeconds","tankChassisSpeed","tankChassisGlowIntensity"})Assert.NotNull(view.Page.Q<TextField>("lab-field-"+key),key);
                Assert.NotNull(view.Page.Q<Button>("lab-object-Танк").Q<OrbitalGlyph>());
                Assert.AreEqual("Поиск",view.Page.Q<TextField>("lab-search").label);Assert.IsNull(view.Page.Q("lab-help"));Assert.IsNull(view.Page.Q(className:"orbital-hints"));
                CollectionAssert.AreEqual(new[]{"lab-name","lab-reset-name"},view.Page.Q("lab-profile-controls").Children().Select(e=>e.name));
                CollectionAssert.AreEqual(new[]{"lab-revisions","lab-save","lab-reset","lab-apply","lab-select","lab-back"},view.Page.Q("lab-revision-controls").Children().Select(e=>e.name));
                Assert.GreaterOrEqual(view.Page.Q<Button>("lab-save").worldBound.xMin,view.Page.Q("lab-revisions").worldBound.xMax+6,"Revision and its actions have a visible gap");
                Assert.GreaterOrEqual(view.Page.Q<Button>("lab-reset-name").worldBound.xMin,view.Page.Q("lab-name").worldBound.xMax+6,"Profile and its action have a visible gap");
                Assert.LessOrEqual(view.Page.Q<TextField>("lab-field-tankHealth").worldBound.height,34);
                Assert.LessOrEqual(view.Page.Q<ScrollView>("lab-tree").verticalScroller.worldBound.xMax,view.Page.Q<ScrollView>("lab-parameters").worldBound.xMin+.5f,"Tree scrollbar must not overlap parameter editors");
                Capture(texture,"tank-"+size.x+"x"+size.y);
                panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);texture=null;
            }
            texture=new RenderTexture(1280,800,24);texture.Create();panel.targetTexture=texture;root.style.width=1280;root.style.height=800;
            foreach(var pair in new[]{new[]{"Исследователь","explorerAssaultCost"},new[]{"Шквал","shkvalGuidanceCost"}})
            {nav.ActivateElement(view.Page.Q<Button>("lab-object-"+pair[0]));Assert.NotNull(view.Page.Q<TextField>("lab-field-"+pair[1]));}
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Здания"));nav.ActivateElement(view.Page.Q<Button>("lab-object-Переработчик"));
            Assert.NotNull(view.Page.Q<TextField>("lab-field-refineryUpgradeCost"));Assert.NotNull(view.Page.Q<Button>("lab-object-Переработчик").Q<OrbitalGlyph>());
            for(int i=0;i<5;i++)yield return null;Capture(texture,"refinery-1280x800");
            var seen=new System.Collections.Generic.HashSet<string>();
            foreach(var section in new[]{"Юниты","Здания","Экономика","Графика","Правила"})
            {
                nav.ActivateElement(view.Page.Q<Button>("lab-section-"+section));
                var objects=view.Page.Query<Button>().ToList().Where(b=>b.name!=null&&b.name.StartsWith("lab-object-")).Select(b=>b.name).ToArray();
                foreach(var obj in objects)
                {
                    nav.ActivateElement(view.Page.Q<Button>(obj));
                    foreach(var field in view.Page.Query<TextField>().ToList().Where(f=>f.name.StartsWith("lab-field-")))
                    {
                        Assert.True(seen.Add(field.name),"Duplicated field: "+field.name);Assert.False(string.IsNullOrWhiteSpace(field.tooltip));StringAssert.Contains("Диапазон",field.tooltip);
                        Assert.True(field.tooltip.Split('\n')[0].Any(c=>c>='А'&&c<='я'),field.name);
                        field.Focus();
                        var focused=root.focusController.focusedElement as VisualElement;Assert.True(field==focused||field.Contains(focused),"Parameters remain focusable with native hover help");
                    }
                }
            }
            CollectionAssert.AreEquivalent(PlayableProfileMetadata.Fields.Where(NativeBalanceFields.Editable).Select(f=>"lab-field-"+f.FieldName),seen);
            view.Page.Q<TextField>("lab-search").value="tankChassis";Assert.NotNull(view.Page.Q<TextField>("lab-field-tankChassisCost"));
            view.Page.Q<TextField>("lab-search").value="missing-parameter";Assert.AreEqual(0,view.Page.Query<TextField>().ToList().Count(f=>f.name.StartsWith("lab-field-")));
            view.Page.Q<TextField>("lab-search").value="";nav.ActivateElement(view.Page.Q<Button>("lab-section-Графика"));
            for(int i=0;i<5;i++)yield return null;Capture(texture,"graphics-1280x800");
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Звук"));var audio=root.Q("audio-laboratory-screen");Assert.NotNull(audio);
            for(int i=0;i<5;i++)yield return null;
            Assert.That(audio.Q(className:"orbital-lab-card").worldBound.width,Is.EqualTo(1280).Within(1));
            Assert.AreEqual(GameplayAudioMetadata.Fields.Length,audio.Query<Slider>().ToList().Count(s=>s.name!=null&&s.name.StartsWith("audio.")));
            foreach(var slider in audio.Query<Slider>().ToList().Where(s=>s.name!=null&&s.name.StartsWith("audio."))) {Assert.False(string.IsNullOrWhiteSpace(slider.tooltip));Assert.LessOrEqual(slider.worldBound.xMax,1280);}
            Assert.IsNull(audio.Q(className:"lab-help"));Capture(texture,"audio-1280x800");nav.ActivateElement(root.Q<Button>("audio-lab-back"));Assert.AreEqual(DisplayStyle.Flex,view.Page.style.display.value);
        }
        finally
        {
            nav.Dispose();panel.targetTexture=null;if(texture!=null){texture.Release();UnityEngine.Object.DestroyImmediate(texture);}UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);Directory.Delete(directory,true);
        }
    }
    [UnityTest] public IEnumerator AccordionGlobalSearchAndExactUnsavedChanges()
    {
        var host=new GameObject("laboratory tree QA");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var texture=new RenderTexture(1280,800,24);texture.Create();panel.targetTexture=texture;
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;root.style.width=1280;root.style.height=800;OrbitalTheme.ConfigurePanel(panel,root);
        var nav=new NativeMenuNavigation(root);var directory=Path.Combine(Path.GetTempPath(),"spacewars-lab-tree-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var baseline=PlayableProfile.Default;var store=new NativeBalanceStore(baseline,Path.Combine(directory,"balance.json"));var view=new NativeBalanceView(root,nav,store,()=>{},_=>null);
            for(int i=0;i<5;i++)yield return null;
            Assert.IsNull(view.Page.Q("lab-sections"),"No horizontal section toolbar");
            Assert.AreEqual("lab-section-Несохранённые изменения",view.Page.Q("lab-tree").Query<Button>().First().name);
            Assert.AreEqual(1,view.Page.Query(className:"lab-tree-children").ToList().Count);
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Юниты"));Assert.AreEqual(0,view.Page.Query(className:"lab-tree-children").ToList().Count,"Clicking the open branch collapses it");
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Здания"));Assert.NotNull(view.Page.Q("lab-children-Здания"));Assert.IsNull(view.Page.Q("lab-children-Юниты"));
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Юниты"));Assert.NotNull(view.Page.Q("lab-children-Юниты"));Assert.IsNull(view.Page.Q("lab-children-Здания"));
            view.Page.Q<TextField>("lab-field-tankSpeed").value=(baseline.TankSpeed+1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Здания"));nav.ActivateElement(view.Page.Q<Button>("lab-object-Штаб"));
            view.Page.Q<TextField>("lab-field-headquartersHealth").value=(baseline.HeadquartersHealth+10).ToString();view.Page.Q<TextField>("lab-name").value="Черновик дерева";
            Assert.True(view.Page.Q<Button>("lab-section-Юниты").ClassListContains("lab-dirty"));Assert.True(view.Page.Q<Button>("lab-section-Здания").ClassListContains("lab-dirty"));Assert.False(view.Page.Q<Button>("lab-section-Экономика").ClassListContains("lab-dirty"));
            Assert.True(view.Page.Q<Button>("lab-object-Штаб").ClassListContains("lab-dirty"));Assert.False(view.Page.Q<Button>("lab-object-Фабрика").ClassListContains("lab-dirty"));
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Юниты"));
            for(int i=0;i<5;i++)yield return null;
            Assert.True(view.Page.Q<Button>("lab-object-Танк").ClassListContains("lab-dirty"));
            Assert.That(view.Page.Q<Button>("lab-section-Здания").resolvedStyle.color.r,Is.EqualTo(1f).Within(.01));
            Assert.That(view.Page.Q<Button>("lab-object-Танк").Q<Label>().resolvedStyle.color.g,Is.EqualTo(197f/255).Within(.01));
            Capture(texture,"dirty-tree-1280x800");
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Несохранённые изменения"));
            for(int i=0;i<5;i++)yield return null;
            StringAssert.Contains("3",view.Page.Q<Button>("lab-section-Несохранённые изменения").text);Assert.NotNull(view.Page.Q("lab-name-change"));
            Assert.AreEqual(2,view.Page.Query<TextField>().ToList().Count(f=>f.name.StartsWith("lab-field-")));Assert.NotNull(view.Page.Q("lab-before-tankSpeed"));Assert.NotNull(view.Page.Q("lab-before-headquartersHealth"));
            Assert.True(view.Page.Q<Button>("lab-save").enabledSelf);Assert.False(view.Page.Q<Button>("lab-apply").enabledSelf);
            Assert.AreEqual(DisplayStyle.None,view.Page.Q(className:"lab-context").style.display.value,"Unsaved section title is already in the sidebar; do not repeat it above the tree");Capture(texture,"unsaved-1280x800");
            view.Page.Q<TextField>("lab-search").value="здоровье";
            for(int i=0;i<5;i++)yield return null;
            foreach(var key in new[]{"tankHealth","explorerHealth","shkvalHealth","headquartersHealth","scienceHealth"})Assert.NotNull(view.Page.Q<TextField>("lab-field-"+key),key);
            Assert.Greater(view.Page.Query(className:"lab-subheading").ToList().Count,2);Assert.AreEqual(DisplayStyle.Flex,view.Page.Q(className:"lab-context").style.display.value,"Search result summary remains useful");Capture(texture,"global-search-1280x800");
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Экономика"));Assert.AreEqual("здоровье",view.Page.Q<TextField>("lab-search").value);Assert.NotNull(view.Page.Q("lab-field-tankHealth"),"Tree selection never restricts active search");
            view.Page.Q<TextField>("lab-search").value="";nav.ActivateElement(view.Page.Q<Button>("lab-section-Несохранённые изменения"));
            view.Page.Q<TextField>("lab-field-tankSpeed").value=baseline.TankSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture);yield return null;yield return null;
            Assert.IsNull(view.Page.Q("lab-field-tankSpeed"));StringAssert.Contains("2",view.Page.Q<Button>("lab-section-Несохранённые изменения").text);
            view.Page.Q<TextField>("lab-field-headquartersHealth").value=baseline.HeadquartersHealth.ToString();view.Page.Q<TextField>("lab-name").value=baseline.DisplayName;
            yield return null;yield return null;
            StringAssert.Contains("0",view.Page.Q<Button>("lab-section-Несохранённые изменения").text);Assert.False(view.Page.Q<Button>("lab-save").enabledSelf);Assert.True(view.Page.Q<Button>("lab-apply").enabledSelf);Assert.IsNull(view.Page.Q("lab-name-change"));Assert.False(view.Page.Q<Button>("lab-section-Юниты").ClassListContains("lab-dirty"));Assert.False(view.Page.Q<Button>("lab-section-Здания").ClassListContains("lab-dirty"));
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Юниты"));view.Page.Q<TextField>("lab-field-tankSpeed").value=(baseline.TankSpeed+1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            view.Page.Q<TextField>("lab-name").value="Временное имя";nav.ActivateElement(view.Page.Q<Button>("lab-reset-name"));Assert.AreEqual(baseline.DisplayName,view.Page.Q<TextField>("lab-name").value);StringAssert.Contains("1",view.Page.Q<Button>("lab-section-Несохранённые изменения").text);
            nav.ActivateElement(view.Page.Q<Button>("lab-save"));nav.ActivateElement(view.Page.Q<Button>("lab-section-Несохранённые изменения"));
            StringAssert.Contains("0",view.Page.Q<Button>("lab-section-Несохранённые изменения").text);Assert.AreEqual(0,view.Page.Query<TextField>().ToList().Count(f=>f.name.StartsWith("lab-field-")));
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Юниты"));view.Page.Q<TextField>("lab-field-tankSpeed").value=(baseline.TankSpeed+2).ToString(System.Globalization.CultureInfo.InvariantCulture);
            nav.ActivateElement(view.Page.Q<Button>("lab-reset"));StringAssert.Contains("0",view.Page.Q<Button>("lab-section-Несохранённые изменения").text);Assert.False(view.Page.Q<Button>("lab-save").enabledSelf);
        }
        finally{nav.Dispose();panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);Directory.Delete(directory,true);}
    }
    private static void Capture(RenderTexture texture,string name)
    {
        var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../artifacts/playtest/compact-game-design-lab/f3"));Directory.CreateDirectory(folder);
        var previous=RenderTexture.active;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),pixels.EncodeToPNG());}
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
    }
}
