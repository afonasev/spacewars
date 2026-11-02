using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class NativeBalanceAiProfileUiTests
{
    private static string Unit(string value)=>value=="credits"?"кр.":value=="orders/min"?"пр./мин":value=="ratio/s"?"доля/с":value=="toggle"?"0/1":value=="m"?"м":value=="orders"?"приказов":value=="s"?"с":value=="ratio"?"×":value=="weight"?"баллов":value=="buildings"?"зданий":value=="units"?"юнитов":value=="changes"?"смен":value;
    private static string GroupButton(string group)=>"lab-object-"+(group.StartsWith("ИИ / ",StringComparison.Ordinal)?group.Substring(5):group).Replace('/','-').Replace(' ','-');
    [UnityTest] public IEnumerator NamedMetadataControlsRenderPersistReloadAndReset()
    {
        var host=new GameObject("AI Balance Lab QA");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var texture=new RenderTexture(1280,800,24);texture.Create();panel.targetTexture=texture;
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;root.style.width=1280;root.style.height=800;OrbitalTheme.ConfigurePanel(panel,root);
        var nav=new NativeMenuNavigation(root);var directory=Path.Combine(Path.GetTempPath(),"spacewars-ai-lab-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"profiles.json");var store=new NativeBalanceStore(PlayableProfile.Default,path);var view=new NativeBalanceView(root,nav,store,()=>{});
            for(int i=0;i<5;i++)yield return null;
            nav.ActivateElement(view.Page.Q<Button>("lab-section-ИИ"));
            Assert.IsTrue(view.Page.Query<Label>().ToList().Any(l=>l.text=="После сохранения настройки применятся к новым матчам; текущий матч не меняется."));
            var fields=NativeBalanceFields.AiOpeningFields;
            Assert.IsNull(view.Page.Q("lab-ai-opening-weights-safe"),"Legacy opening weights are not active controls.");
            var seen=new System.Collections.Generic.List<string>();
            foreach(var group in fields.Select(f=>f.Group).Distinct())
            {
                nav.ActivateElement(view.Page.Q<Button>(GroupButton(group)));
                var selected=fields.Where(f=>f.Group==group).ToArray();
                Assert.IsTrue(view.Page.Query<Label>().ToList().Any(l=>l.text==(group.StartsWith("ИИ / ",StringComparison.Ordinal)?group.Substring(5):group)),group+" navigation label");
                Assert.That(view.Page.Query<TextField>().ToList().Count(f=>f.name.StartsWith("lab-ai-")),Is.EqualTo(selected.Length));
                foreach(var field in selected)
                {
                    var editor=view.Page.Q<TextField>("lab-ai-"+field.Path.Replace('.','-'));
                    Assert.NotNull(editor,field.Path);Assert.AreEqual(field.Label,editor.label);
                    StringAssert.Contains(field.Description,editor.tooltip);StringAssert.Contains(Unit(field.Unit),editor.tooltip);
                    StringAssert.Contains("шаг "+field.Step.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture),editor.tooltip);seen.Add(field.Path);
                }
                for(int i=0;i<5;i++)yield return null;
                Capture(texture,group.StartsWith("ИИ / Контроль / ",StringComparison.Ordinal)?"ai-control-"+AiProfile.DifficultyId((AiDifficulty)Array.IndexOf(new[]{"Новичок","Боец","Ветеран"},group.Substring("ИИ / Контроль / ".Length)))+"-1280x800":"ai-"+GroupButton(group)+"-1280x800");
                if(fields.Any(f=>f.Group==group&&f.Path.StartsWith("positionDevelopment.",StringComparison.Ordinal)))Capture(texture,"ai-position-development-1280x800");
                if(fields.Any(f=>f.Group==group&&f.Path.StartsWith("activeStrategy.",StringComparison.Ordinal)))Capture(texture,"ai-active-strategy-1280x800");
                if(fields.Any(f=>f.Group==group&&f.Path.StartsWith("safeReinforcements.",StringComparison.Ordinal)))Capture(texture,"ai-safe-reinforcements-1280x800");
            }
            CollectionAssert.AreEquivalent(fields.Select(f=>f.Path).ToArray(),seen);
            var openingGroup=fields.First(f=>f.Path.StartsWith("openingsMidgame.",StringComparison.Ordinal)).Group;
            nav.ActivateElement(view.Page.Q<Button>(GroupButton(openingGroup)));
            var target=fields.Single(f=>f.Path=="openingsMidgame.opening.doubleIncome");var input=view.Page.Q<TextField>("lab-ai-"+target.Path.Replace('.','-'));
            input.value=(target.Initial+target.Step).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.AreEqual(target.Initial,store.ActiveAiProfile.Value(target.Path),"Editing a draft does not rebind the active profile before save.");
            var position=fields.Single(f=>f.Path=="positionDevelopment.towerValue");
            nav.ActivateElement(view.Page.Q<Button>(GroupButton(position.Group)));
            var positionInput=view.Page.Q<TextField>("lab-ai-"+position.Path.Replace('.','-'));
            positionInput.value=(position.Initial+position.Step).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.AreEqual(position.Initial,store.ActiveAiProfile.Value(position.Path),"Position draft stays isolated until save.");
            var active=fields.Single(f=>f.Path=="activeStrategy.developmentSeconds");
            nav.ActivateElement(view.Page.Q<Button>(GroupButton(active.Group)));
            view.Page.Q<TextField>("lab-ai-"+active.Path.Replace('.','-')).value=(active.Initial+active.Step).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.AreEqual(active.Initial,store.ActiveAiProfile.Value(active.Path));
            nav.ActivateElement(view.Page.Q<Button>("lab-save"));
            Assert.AreEqual(target.Initial+target.Step,store.ActiveAiProfile.Value(target.Path));
            Assert.AreEqual(position.Initial+position.Step,store.ActiveAiProfile.Value(position.Path));
            var reloaded=new NativeBalanceStore(PlayableProfile.Default,path);
            Assert.AreEqual(target.Initial+target.Step,reloaded.ActiveAiProfile.Value(target.Path),"Last saved profile selection survives store reload.");
            Assert.AreEqual(position.Initial+position.Step,reloaded.ActiveAiProfile.Value(position.Path));
            Assert.AreEqual(active.Initial+active.Step,reloaded.ActiveAiProfile.Value(active.Path));
            Assert.AreEqual(PlayableProfile.Default.TankCreditCost,reloaded.Active.TankCreditCost);
            nav.ActivateElement(view.Page.Q<Button>(GroupButton(position.Group)));
            positionInput=view.Page.Q<TextField>("lab-ai-"+position.Path.Replace('.','-'));positionInput.value="не число";
            nav.ActivateElement(view.Page.Q<Button>("lab-ai-reset"));
            Assert.AreEqual(position.Initial,double.Parse(view.Page.Q<TextField>("lab-ai-"+position.Path.Replace('.','-')).value,System.Globalization.CultureInfo.InvariantCulture));
            nav.ActivateElement(view.Page.Q<Button>("lab-save"));
            Assert.AreEqual(target.Initial,new NativeBalanceStore(PlayableProfile.Default,path).ActiveAiProfile.Value(target.Path),"Named reset persists independently of gameplay settings.");
            Assert.AreEqual(position.Initial,new NativeBalanceStore(PlayableProfile.Default,path).ActiveAiProfile.Value(position.Path),"Position reset clears invalid draft and persists defaults.");
            foreach(var difficulty in new[]{AiDifficulty.Recruit,AiDifficulty.Fighter,AiDifficulty.Veteran})
            {
                var control=fields.Single(f=>f.Path=="difficulty."+AiProfile.DifficultyId(difficulty)+".control.ordersPerMinute");
                nav.ActivateElement(view.Page.Q<Button>(GroupButton(control.Group)));
                view.Page.Q<TextField>("lab-ai-"+control.Path.Replace('.','-')).value=(control.Initial+control.Step).ToString(System.Globalization.CultureInfo.InvariantCulture);
                nav.ActivateElement(view.Page.Q<Button>("lab-save"));
                Assert.AreEqual(control.Initial+control.Step,new NativeBalanceStore(PlayableProfile.Default,path).ActiveAiProfile.Value(control.Path));
                nav.ActivateElement(view.Page.Q<Button>("lab-ai-reset"));
                Assert.AreEqual(control.Initial,double.Parse(view.Page.Q<TextField>("lab-ai-"+control.Path.Replace('.','-')).value,System.Globalization.CultureInfo.InvariantCulture));
                nav.ActivateElement(view.Page.Q<Button>("lab-save"));
                Assert.AreEqual(control.Initial,new NativeBalanceStore(PlayableProfile.Default,path).ActiveAiProfile.Value(control.Path));
            }

            Assert.AreEqual(active.Initial,new NativeBalanceStore(PlayableProfile.Default,path).ActiveAiProfile.Value(active.Path),"Active strategy reset persists defaults.");
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
