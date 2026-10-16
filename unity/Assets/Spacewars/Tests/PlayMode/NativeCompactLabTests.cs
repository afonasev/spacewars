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
            var store=new NativeBalanceStore(PlayableProfile.Default,Path.Combine(directory,"balance.json"));var view=new NativeBalanceView(root,nav,store,()=>{});
            foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(800,620)})
            {
                texture=new RenderTexture(size.x,size.y,24);texture.Create();panel.targetTexture=texture;root.style.width=size.x;root.style.height=size.y;OrbitalTheme.ConfigurePanel(panel,root);
                for(int i=0;i<5;i++)yield return null;
                var card=view.Page.Q(className:"orbital-lab-card");Assert.That(card.worldBound.width,Is.EqualTo(size.x).Within(1));
                Assert.LessOrEqual(card.worldBound.yMax,size.y);Assert.Greater(card.worldBound.height,size.y-40);
                foreach(var key in new[]{"lab-save","lab-reset","lab-back","lab-search","lab-tree"})
                {var e=view.Page.Q(key);Assert.Greater(e.worldBound.width,0,key);Assert.LessOrEqual(e.worldBound.xMax,size.x+.5f,key);Assert.LessOrEqual(e.worldBound.yMax,size.y+.5f,key);}
                foreach(var key in new[]{"tankHealth","tankChassisCost","tankChassisSeconds","tankChassisSpeed","tankChassisGlowIntensity"})Assert.NotNull(view.Page.Q<TextField>("lab-field-"+key),key);
                Assert.NotNull(view.Page.Q<Button>("lab-object-Танк").Q<OrbitalGlyph>());
                Assert.AreEqual("Поиск",view.Page.Q<TextField>("lab-search").label);Assert.IsNull(view.Page.Q("lab-help"));Assert.IsNull(view.Page.Q(className:"orbital-hints"));
                CollectionAssert.AreEqual(new[]{"lab-profiles","lab-copy","lab-rename","lab-delete"},view.Page.Q("lab-profile-controls").Children().Select(e=>e.name));
                CollectionAssert.AreEqual(new[]{"lab-revisions","lab-save","lab-reset","lab-release"},view.Page.Q("lab-revision-controls").Children().Select(e=>e.name));
                Assert.GreaterOrEqual(view.Page.Q<Button>("lab-save").worldBound.xMin,view.Page.Q("lab-revisions").worldBound.xMax+6,"Revision and its actions have a visible gap");
                Assert.GreaterOrEqual(view.Page.Q<Button>("lab-copy").worldBound.xMin,view.Page.Q("lab-profiles").worldBound.xMax+6,"Profile and its action have a visible gap");
                Assert.LessOrEqual(view.Page.Q<TextField>("lab-field-tankHealth").worldBound.height,34);
                Assert.LessOrEqual(view.Page.Q<ScrollView>("lab-tree").verticalScroller.worldBound.xMax,view.Page.Q<ScrollView>("lab-parameters").worldBound.xMin+.5f,"Tree scrollbar must not overlap parameter editors");
                var back=view.Page.Q<Button>("lab-back");Assert.AreEqual("lab-actions",back.parent.name);Assert.GreaterOrEqual(back.worldBound.yMin,view.Page.Q("lab-parameters").worldBound.yMax,"Back stays below scrollable content");Assert.That(back.worldBound.xMin,Is.EqualTo(view.Page.Q("lab-profiles").worldBound.xMin).Within(.5f));
                var inputX=view.Page.Q("lab-profiles").Q(className:"unity-base-field__input").worldBound.xMin;
                foreach(var key in new[]{"lab-revisions","lab-search"})Assert.That(view.Page.Q(key).Q(className:"unity-base-field__input").worldBound.xMin,Is.EqualTo(inputX).Within(.5f),key);
                Assert.AreEqual("Сохранить",view.Page.Q<Button>("lab-save").text);Assert.AreEqual("Очистить",view.Page.Q<Button>("lab-reset").text);Assert.IsNull(view.Page.Q("lab-test"));Assert.IsNull(view.Page.Q("lab-default"));Assert.IsNull(view.Page.Q("lab-clear-search"));
                var release=view.Page.Q<Button>("lab-release");bool marked=store.IsMarked;var active=store.Active;
                Assert.AreEqual(marked,release.ClassListContains("lab-release-selected"));
                nav.ActivateElement(release);Assert.AreEqual(!marked,store.IsMarked);Assert.AreEqual(!marked,release.ClassListContains("lab-release-selected"));Assert.AreSame(active,store.Active);
                for(int i=0;i<3;i++)yield return null;
                Capture(texture,"release-off-"+size.x+"x"+size.y);
                nav.ActivateElement(release);Assert.AreEqual(marked,store.IsMarked);Assert.AreEqual(marked,release.ClassListContains("lab-release-selected"));Assert.AreSame(active,store.Active);
                for(int i=0;i<3;i++)yield return null;
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
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Звук"));
            Assert.AreEqual(GameplayAudioMetadata.Fields.Length,view.Page.Query<TextField>().ToList().Count(f=>f.name!=null&&f.name.StartsWith("audio.")));
            foreach(var editor in view.Page.Query<TextField>().ToList().Where(f=>f.name!=null&&f.name.StartsWith("audio."))){Assert.False(string.IsNullOrWhiteSpace(editor.tooltip));editor.Focus();StringAssert.Contains("следующая игра",view.Page.Q<Label>("lab-focus-help").text);}
            for(int i=0;i<5;i++)yield return null;StringAssert.Contains(GameplayAudioMetadata.Fields.Length+" параметров",view.Page.Q<Label>(className:"lab-context").text);Capture(texture,"audio-1280x800");
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
            var baseline=PlayableProfile.Default;var store=new NativeBalanceStore(baseline,Path.Combine(directory,"balance.json"));var view=new NativeBalanceView(root,nav,store,()=>{});
            for(int i=0;i<5;i++)yield return null;
            Assert.IsNull(view.Page.Q("lab-sections"),"No horizontal section toolbar");
            Assert.AreEqual("lab-section-Изменения",view.Page.Q("lab-tree").Query<Button>().First().name);
            Assert.AreEqual(1,view.Page.Query(className:"lab-tree-children").ToList().Count);
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Юниты"));Assert.AreEqual(0,view.Page.Query(className:"lab-tree-children").ToList().Count,"Clicking the open branch collapses it");
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Здания"));Assert.NotNull(view.Page.Q("lab-children-Здания"));Assert.IsNull(view.Page.Q("lab-children-Юниты"));
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Юниты"));Assert.NotNull(view.Page.Q("lab-children-Юниты"));Assert.IsNull(view.Page.Q("lab-children-Здания"));
            view.Page.Q<TextField>("lab-field-tankSpeed").value=(baseline.TankSpeed+1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Здания"));nav.ActivateElement(view.Page.Q<Button>("lab-object-Штаб"));
            view.Page.Q<TextField>("lab-field-headquartersHealth").value=(baseline.HeadquartersHealth+10).ToString();
            Assert.True(view.Page.Q<Button>("lab-section-Юниты").ClassListContains("lab-dirty"));Assert.True(view.Page.Q<Button>("lab-section-Здания").ClassListContains("lab-dirty"));Assert.False(view.Page.Q<Button>("lab-section-Экономика").ClassListContains("lab-dirty"));
            Assert.True(view.Page.Q<Button>("lab-object-Штаб").ClassListContains("lab-dirty"));Assert.False(view.Page.Q<Button>("lab-object-Фабрика").ClassListContains("lab-dirty"));
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Юниты"));
            for(int i=0;i<5;i++)yield return null;
            Assert.True(view.Page.Q<Button>("lab-object-Танк").ClassListContains("lab-dirty"));
            Assert.That(view.Page.Q<Button>("lab-section-Здания").resolvedStyle.color.r,Is.EqualTo(1f).Within(.01));
            Assert.That(view.Page.Q<Button>("lab-object-Танк").Q<Label>().resolvedStyle.color.g,Is.EqualTo(197f/255).Within(.01));
            Capture(texture,"dirty-tree-1280x800");
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Изменения"));
            for(int i=0;i<5;i++)yield return null;
            StringAssert.Contains("2",view.Page.Q<Button>("lab-section-Изменения").text);
            Assert.AreEqual(2,view.Page.Query<TextField>().ToList().Count(f=>f.name.StartsWith("lab-field-")));Assert.NotNull(view.Page.Q("lab-before-tankSpeed"));Assert.NotNull(view.Page.Q("lab-before-headquartersHealth"));
            Assert.True(view.Page.Q<Button>("lab-save").enabledSelf);Assert.True(view.Page.Q<Button>("lab-back").enabledSelf,"Return must guard a dirty draft");
            Assert.AreEqual(DisplayStyle.None,view.Page.Q(className:"lab-context").style.display.value,"Unsaved section title is already in the sidebar; do not repeat it above the tree");Capture(texture,"unsaved-1280x800");
            view.Page.Q<TextField>("lab-search").value="здоровье";
            for(int i=0;i<5;i++)yield return null;
            foreach(var key in new[]{"tankHealth","explorerHealth","shkvalHealth","headquartersHealth","scienceHealth"})Assert.NotNull(view.Page.Q<TextField>("lab-field-"+key),key);
            Assert.Greater(view.Page.Query(className:"lab-subheading").ToList().Count,2);Assert.AreEqual(DisplayStyle.Flex,view.Page.Q(className:"lab-context").style.display.value,"Search result summary remains useful");Capture(texture,"global-search-1280x800");
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Экономика"));Assert.AreEqual("здоровье",view.Page.Q<TextField>("lab-search").value);Assert.NotNull(view.Page.Q("lab-field-tankHealth"),"Tree selection never restricts active search");
            view.Page.Q<TextField>("lab-search").value="";nav.ActivateElement(view.Page.Q<Button>("lab-section-Изменения"));
            view.Page.Q<TextField>("lab-field-tankSpeed").value=baseline.TankSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture);yield return null;yield return null;
            Assert.IsNull(view.Page.Q("lab-field-tankSpeed"));StringAssert.Contains("1",view.Page.Q<Button>("lab-section-Изменения").text);
            view.Page.Q<TextField>("lab-field-headquartersHealth").value=baseline.HeadquartersHealth.ToString();
            yield return null;yield return null;
            StringAssert.Contains("0",view.Page.Q<Button>("lab-section-Изменения").text);Assert.False(view.Page.Q<Button>("lab-save").enabledSelf);Assert.IsNull(view.Page.Q("lab-test"));Assert.IsNull(view.Page.Q("lab-default"));Assert.IsNull(view.Page.Q("lab-clear-search"));Assert.IsNull(view.Page.Q("lab-name-change"));Assert.False(view.Page.Q<Button>("lab-section-Юниты").ClassListContains("lab-dirty"));Assert.False(view.Page.Q<Button>("lab-section-Здания").ClassListContains("lab-dirty"));
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Юниты"));view.Page.Q<TextField>("lab-field-tankSpeed").value=(baseline.TankSpeed+1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            StringAssert.Contains("1",view.Page.Q<Button>("lab-section-Изменения").text);
            nav.ActivateElement(view.Page.Q<Button>("lab-save"));nav.ActivateElement(view.Page.Q<Button>("lab-section-Изменения"));
            StringAssert.Contains("0",view.Page.Q<Button>("lab-section-Изменения").text);Assert.AreEqual(0,view.Page.Query<TextField>().ToList().Count(f=>f.name.StartsWith("lab-field-")));
            nav.ActivateElement(view.Page.Q<Button>("lab-section-Юниты"));view.Page.Q<TextField>("lab-field-tankSpeed").value=(baseline.TankSpeed+2).ToString(System.Globalization.CultureInfo.InvariantCulture);
            nav.ActivateElement(view.Page.Q<Button>("lab-reset"));Assert.NotNull(root.Q("lab-confirm"));nav.ActivateElement(root.Q<Button>("lab-confirm-cancel"));Assert.True(view.Page.Q<Button>("lab-save").enabledSelf);nav.ActivateElement(view.Page.Q<Button>("lab-reset"));nav.ActivateElement(root.Q<Button>("lab-confirm-accept"));StringAssert.Contains("0",view.Page.Q<Button>("lab-section-Изменения").text);Assert.False(view.Page.Q<Button>("lab-save").enabledSelf);
        }
        finally{nav.Dispose();panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);Directory.Delete(directory,true);}
    }
    private static void Capture(RenderTexture texture,string name)
    {
        var folder=Environment.GetEnvironmentVariable("SPACEWARS_LAB_EVIDENCE")??Path.GetTempPath();Directory.CreateDirectory(folder);
        var previous=RenderTexture.active;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),pixels.EncodeToPNG());}
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
    }
}
