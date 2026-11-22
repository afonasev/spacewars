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
    private static string Unit(string value)=>value=="battles"?"боёв":value=="credits"?"кр.":value=="orders/min"?"пр./мин":value=="ratio/s"?"доля/с":value=="toggle"?"0/1":value=="m"?"м":value=="orders"?"приказов":value=="s"?"с":value=="ratio"?"×":value=="weight"?"баллов":value=="buildings"?"зданий":value=="units"?"юнитов":value=="changes"?"смен":value=="tank"?"Вепрей":value;
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
            var fields=NativeBalanceFields.AiLabFields;
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
                    StringAssert.Contains("Шаг: "+field.Step.ToString("0.######",System.Globalization.CultureInfo.InvariantCulture),editor.tooltip);seen.Add(field.Path);
                }
                for(int i=0;i<5;i++)yield return null;
                var difficultyControl=selected.FirstOrDefault(field=>field.Path.EndsWith(".control.ordersPerMinute",StringComparison.Ordinal));
                string imageName=difficultyControl==null?"ai-"+GroupButton(group):"ai-control-"+difficultyControl.Path.Split('.')[1];
                Capture(texture,imageName+"-1280x800");
                if(fields.Any(f=>f.Group==group&&f.Path.StartsWith("positionDevelopment.",StringComparison.Ordinal)))Capture(texture,"ai-position-development-1280x800");
                if(fields.Any(f=>f.Group==group&&f.Path.StartsWith("activeStrategy.",StringComparison.Ordinal)))Capture(texture,"ai-active-strategy-1280x800");
                if(fields.Any(f=>f.Group==group&&f.Path.StartsWith("safeReinforcements.",StringComparison.Ordinal)))Capture(texture,"ai-safe-reinforcements-1280x800");
            }
            CollectionAssert.AreEquivalent(fields.Select(f=>f.Path).ToArray(),seen);
            var openingGroup=fields.First(f=>f.Path.StartsWith("openingsMidgame.",StringComparison.Ordinal)).Group;
            nav.ActivateElement(view.Page.Q<Button>(GroupButton(openingGroup)));
            var target=fields.Single(f=>f.Path=="openingsMidgame.doubleIncome");var input=view.Page.Q<TextField>("lab-ai-"+target.Path.Replace('.','-'));
            input.value=(target.Initial+target.Step).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.AreEqual(target.Initial,store.ActiveAiProfile.Value(target.Path),"Editing a draft does not rebind the active profile before save.");
            var position=fields.Single(f=>f.Path=="positionDevelopment.localThreatRange");
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
    [UnityTest] public IEnumerator TurretBypassControlsRenderSearchAndPersistTogether()
    {
        var host=new GameObject("Turret bypass controls QA");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var texture=new RenderTexture(1280,800,24);texture.Create();panel.targetTexture=texture;
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;root.style.width=1280;root.style.height=800;OrbitalTheme.ConfigurePanel(panel,root);
        var nav=new NativeMenuNavigation(root);var directory=Path.Combine(Path.GetTempPath(),"spacewars-bypass-lab-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"profiles.json");var store=new NativeBalanceStore(PlayableProfile.Default,path);var view=new NativeBalanceView(root,nav,store,()=>{});
            for(int i=0;i<5;i++)yield return null;
            var fields=NativeBalanceFields.AiLabFields.Where(f=>f.Path.StartsWith("turretBypass.",StringComparison.Ordinal)).ToArray();Assert.AreEqual(3,fields.Length);
            nav.ActivateElement(view.Page.Q<Button>("lab-section-ИИ"));nav.ActivateElement(view.Page.Q<Button>(GroupButton(fields[0].Group)));
            var editors=view.Page.Query<TextField>().ToList().Where(f=>f.name.StartsWith("lab-ai-")).ToArray();CollectionAssert.AreEqual(fields.Select(f=>f.Label),editors.Select(f=>f.label));
            for(int i=0;i<5;i++)yield return null;Capture(texture,"ai-turret-bypass-controls-1280x800");
            var expected=new System.Collections.Generic.Dictionary<string,double>();
            foreach(var field in fields)
            {
                var editor=view.Page.Q<TextField>("lab-ai-"+field.Path.Replace('.','-'));StringAssert.Contains(field.Description,editor.tooltip);StringAssert.Contains(Unit(field.Unit),editor.tooltip);
                double next=field.Initial==field.Maximum?field.Initial-field.Step:field.Initial+field.Step;expected[field.Path]=next;editor.value=next.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Assert.AreEqual(field.Initial,store.ActiveAiProfile.Value(field.Path));
            }
            view.Page.Q<TextField>("lab-search").value="Обход турелей";
            Assert.AreEqual(3,view.Page.Query<TextField>().ToList().Count(f=>f.name.StartsWith("lab-ai-")));
            view.Page.Q<TextField>("lab-search").value="";nav.ActivateElement(view.Page.Q<Button>("lab-section-Изменения"));
            foreach(var field in fields)Assert.NotNull(view.Page.Q<TextField>("lab-ai-"+field.Path.Replace('.','-')),"Edited control appears in changes");
            nav.ActivateElement(view.Page.Q<Button>("lab-save"));var reloaded=new NativeBalanceStore(PlayableProfile.Default,path);
            foreach(var field in fields)Assert.AreEqual(expected[field.Path],reloaded.ActiveAiProfile.Value(field.Path),field.Path);
            nav.ActivateElement(view.Page.Q<Button>("lab-section-ИИ"));nav.ActivateElement(view.Page.Q<Button>(GroupButton(fields[0].Group)));
            nav.ActivateElement(view.Page.Q<Button>("lab-ai-reset"));nav.ActivateElement(view.Page.Q<Button>("lab-save"));reloaded=new NativeBalanceStore(PlayableProfile.Default,path);
            foreach(var field in fields)Assert.AreEqual(field.Initial,reloaded.ActiveAiProfile.Value(field.Path));
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
