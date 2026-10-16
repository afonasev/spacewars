using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.BalanceLab;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class NativeDesignLabTests
{
    private string directory,path;
    private PlayableProfile baseline;
    [SetUp] public void Setup(){directory=Path.Combine(Path.GetTempPath(),"spacewars-design-lab-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);path=Path.Combine(directory,"profiles.json");baseline=PlayableProfile.Default;}
    [TearDown] public void Cleanup(){Directory.Delete(directory,true);NativeGameplayAudioProfile.Reload();}
    private NativeBalanceStore Dev()=>new NativeBalanceStore(baseline,path);
    private static PlayableProfileData Changed(NativeBalanceStore store){var d=store.Selected.CopyData();var f=PlayableProfileMetadata.Fields.Single(v=>v.FieldName=="tankHealth");f.Write(d,f.Read(d)+f.Step);return d;}
    [Test] public void VersionsCopiesMarksAndStartupHaveIndependentIdentity()
    {
        var s=Dev();var first=s.SelectedRow.DeepClone();s.Save(Changed(s),s.SelectedAudio);Assert.AreEqual(2,s.SelectedVersion);Assert.False(s.IsMarked);Assert.AreEqual(2,s.Active.Revision);
        s.Select(1);s.Save(Changed(s),s.SelectedAudio);Assert.AreEqual(3,s.SelectedVersion);Assert.True(JToken.DeepEquals(first,s.Versions[0]),"Every saved field and historical date must remain unchanged regardless of JSON property order");s.Mark(true);
        s.Select(1);s.Copy("Эксперимент");string id=s.SelectedId;Assert.AreEqual(1,s.Versions.Count);Assert.AreEqual(1,s.SelectedVersion);Assert.False(s.IsMarked);Assert.AreNotEqual(NativeBalanceStore.DefaultId,id);
        s.Rename("Переименован");Assert.AreEqual(id,s.SelectedId);StringAssert.Contains("Переименован",s.Active.DisplayName);s.Mark(true);
        var catalog=s.ExportPackage();Assert.AreEqual(2,catalog["profiles"].Count());CollectionAssert.AreEquivalent(new[]{1,3},catalog["profiles"][0]["versions"].Select(v=>(int)v["revision"]));
        var reopened=Dev();Assert.AreEqual(NativeBalanceStore.DefaultId,reopened.SelectedId);Assert.AreEqual(3,reopened.SelectedVersion);reopened.SelectProfile(id);Assert.AreEqual(1,reopened.SelectedVersion);reopened.Delete();Assert.AreEqual(NativeBalanceStore.DefaultId,reopened.SelectedId);Assert.AreEqual(3,reopened.SelectedVersion);
    }
    [Test] public void ProductionProtectsPackageAtDataLayerButCopiesRemainIndependent()
    {
        var dev=Dev();dev.Save(Changed(dev),dev.SelectedAudio);dev.Mark(true);var package=dev.ExportPackage();string original=package.ToString();
        var player=new NativeBalanceStore(baseline,Path.Combine(directory,"player.json"),false,package);Assert.False(player.CanEdit);
        Assert.Throws<InvalidOperationException>(()=>player.Save(Changed(player),player.SelectedAudio));Assert.Throws<InvalidOperationException>(()=>player.Rename("hacked"));Assert.Throws<InvalidOperationException>(()=>player.Delete());Assert.Throws<InvalidOperationException>(()=>player.Mark(false));
        player.Select(1);player.Copy("Default");var id=player.SelectedId;Assert.AreEqual(1,player.Versions.Count);Assert.True(player.CanEdit);Assert.False(player.IsMarked);player.Save(Changed(player),player.SelectedAudio);Assert.AreEqual(2,player.SelectedVersion);Assert.AreEqual(original,package.ToString());
        var reopened=new NativeBalanceStore(baseline,Path.Combine(directory,"player.json"),false,package);Assert.AreEqual(NativeBalanceStore.DefaultId,reopened.SelectedId);Assert.AreEqual(2,reopened.SelectedVersion);reopened.SelectProfile(id);Assert.AreEqual(2,reopened.SelectedVersion);
        reopened.Delete();Assert.AreEqual(NativeBalanceStore.DefaultId,reopened.SelectedId);Assert.AreEqual(2,reopened.SelectedVersion);
    }
    [Test] public void ReleaseChangesDoNotSwitchActiveAndMissingDefaultRemainsRepairable()
    {
        var s=Dev();s.Save(Changed(s),s.SelectedAudio);s.Mark(true);s.Select(1);Assert.AreEqual(1,s.Active.Revision);s.Mark(false);Assert.AreEqual(1,s.Active.Revision);s.ResetRelease();Assert.AreEqual(2,s.Active.Revision);s.Mark(false);
        Assert.NotNull(s.ReleaseProblem);Assert.Throws<InvalidOperationException>(()=>s.ExportPackage());var reopened=Dev();Assert.False(reopened.HasActive);Assert.NotNull(reopened.ReleaseProblem);reopened.Mark(true);reopened.ResetRelease();Assert.True(reopened.HasActive);
    }
    [Test] public void UpgradeUsesCurrentReleasedDefaultsWithoutRewritingHistoryAndKeepsZero()
    {
        var dev=Dev();var package=dev.ExportPackage();var data=(JObject)package["profiles"][0]["versions"][0]["data"];data["tankHealth"]=baseline.TankHealth+100;
        var s=new NativeBalanceStore(baseline,Path.Combine(directory,"upgrade.json"),false,package);s.Copy("Старый эксперимент");var id=s.SelectedId;var disk=new AtomicStore(s.StoragePath,()=>new JObject());var read=disk.Read();var doc=read.Payload;var row=doc["profiles"][0]["versions"][0];((JObject)row["data"]).Remove("tankHealth");row["audio"]["near"]=0;((JObject)row["audio"]).Remove("far");disk.Commit(read.Token,doc);var bytes=File.ReadAllBytes(s.StoragePath);
        s.Reload();s.SelectProfile(id);Assert.AreEqual(baseline.TankHealth+100,s.Selected.TankHealth);Assert.Zero(s.SelectedAudio.near);Assert.AreEqual(70,s.SelectedAudio.far);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(s.StoragePath));s.Save(Changed(s),s.SelectedAudio);Assert.NotNull(s.SelectedRow["data"]["tankHealth"]);Assert.AreEqual(2,s.SelectedVersion);
    }
    [Test] public void IncompatibleSnapshotNeverActivatesAndRemainsIntact()
    {
        var s=Dev();s.Copy("Сломанная");var id=s.SelectedId;s.ResetRelease();s.Copy("Активная");string activeId=s.SelectedId;var disk=new AtomicStore(path,()=>new JObject());var read=disk.Read();var doc=read.Payload;doc["profiles"][1]["versions"][0]["data"]["tankHealth"]="wrong type";disk.Commit(read.Token,doc);s.Reload();s.SelectProfile(activeId);string before=File.ReadAllText(path);Assert.Throws<FormatException>(()=>s.SelectProfile(id));Assert.AreEqual(id,s.SelectedId,"An unavailable base remains inspectable so older versions can be selected");StringAssert.Contains("Активная",s.ActiveIdentity);Assert.AreEqual(before,File.ReadAllText(path));s.Delete();Assert.AreEqual(activeId,s.SelectedId);StringAssert.Contains("Активная",s.ActiveIdentity);
    }
    [Test] public void CasCorruptionAndLegacyImportPreserveOriginals()
    {
        var a=Dev();var b=Dev();a.Save(Changed(a),a.SelectedAudio);Assert.Throws<StoreConflictException>(()=>b.Save(Changed(b),b.SelectedAudio));
        string legacy=Path.Combine(directory,"legacy.json");var old=new AtomicStore(legacy,()=>new JObject{["schema"]=1,["selected"]=7,["revisions"]=new JArray(new JObject{["name"]="Старая версия",["revision"]=7,["data"]=JObject.Parse(JsonUtility.ToJson(baseline.CopyData()))})});old.Commit(old.Read().Token,old.Read().Payload);var bytes=File.ReadAllBytes(legacy);
        var migrated=new NativeBalanceStore(baseline,Path.Combine(directory,"migrated.json"),false,a.ExportPackage(),legacy);Assert.AreEqual(2,migrated.Profiles.Count);var id=(string)migrated.Profiles.Single(p=>(string)p["name"]=="Старая версия")["id"];migrated.SelectProfile(id);Assert.AreEqual(7,migrated.SelectedVersion);Assert.IsTrue(migrated.SelectedRow["date"]==null||migrated.SelectedRow["date"].Type==JTokenType.Null);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(legacy));
        File.WriteAllText(path,"broken");var damaged=Dev();Assert.NotNull(damaged.Problem);Assert.AreEqual(NativeBalanceStore.DefaultId,damaged.SelectedId);Assert.Throws<InvalidOperationException>(()=>damaged.Save(Changed(damaged),damaged.SelectedAudio));Assert.AreEqual("broken",File.ReadAllText(path));
    }
    [Test] public void FractionalVersionIsReportedAsCorruptionWithoutReplacingTheFile()
    {
        var s=Dev();s.Save(Changed(s),s.SelectedAudio);var disk=new AtomicStore(path,()=>new JObject());var read=disk.Read();var doc=read.Payload;doc["profiles"][0]["versions"][1]["revision"]=1.5;disk.Commit(read.Token,doc);var bytes=File.ReadAllBytes(path);var damaged=Dev();Assert.NotNull(damaged.Problem);Assert.False(damaged.CanEdit);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(path));
    }
    [Test] public void PreviousAudioExperimentIsImportedOnceWithoutChangingActiveVersion()
    {
        var s=Dev();var audio=s.SelectedAudio;audio.near=0;string text=JsonUtility.ToJson(audio);s.ImportLegacyAudio(text);Assert.AreEqual(2,s.Profiles.Count);Assert.AreEqual(NativeBalanceStore.DefaultId,s.SelectedId);var id=(string)s.Profiles.Single(p=>(string)p["id"]!=NativeBalanceStore.DefaultId)["id"];s.SelectProfile(id);Assert.Zero(s.SelectedAudio.near);Assert.AreEqual(JTokenType.Null,s.SelectedRow["date"].Type);s.ImportLegacyAudio(text);Assert.AreEqual(2,s.Profiles.Count);
    }
    [UnityTest] public IEnumerator DraftDialogsInvalidInputAudioSearchAndReturnRoute()
    {
        var host=new GameObject("design lab lifecycle UI");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;root.style.width=800;root.style.height=620;var nav=new NativeMenuNavigation(root);var store=Dev();bool closed=false;var view=new NativeBalanceView(root,nav,store,()=>closed=true);
        try{
            yield return null;var search=view.Page.Q<TextField>("lab-search");search.value="tank.health";var field=view.Page.Q<TextField>("lab-field-tankHealth");field.value="не число";Assert.AreEqual("не число",field.value);nav.ActivateElement(view.Page.Q<Button>("lab-save"));Assert.AreEqual(1,store.Versions.Count);StringAssert.Contains("Исправьте",view.Page.Q<Label>("lab-status").text);
            nav.Back();Assert.NotNull(root.Q("lab-confirm"));StringAssert.Contains("не число",root.Q("lab-confirm-diff").Q<Label>().text);nav.ActivateElement(root.Q<Button>("lab-confirm-cancel"));Assert.AreEqual("не число",field.value);
            field.value=(baseline.TankHealth+100).ToString();nav.ActivateElement(view.Page.Q<Button>("lab-copy"));Assert.NotNull(root.Q("lab-confirm"));nav.ActivateElement(root.Q<Button>("lab-confirm-cancel"));Assert.AreEqual(1,store.Profiles.Count);
            bool marked=store.IsMarked;nav.ActivateElement(view.Page.Q<Button>("lab-release"));Assert.NotNull(root.Q("lab-confirm"));nav.ActivateElement(root.Q<Button>("lab-confirm-cancel"));Assert.AreEqual(marked,store.IsMarked);
            search.value="audio.near";var audio=view.Page.Q<TextField>("audio.near");Assert.NotNull(audio);audio.value="9";Assert.AreEqual(8,store.SelectedAudio.near);nav.ActivateElement(view.Page.Q<Button>("lab-section-Изменения"));search.value="";Assert.NotNull(view.Page.Q("audio.near"));Assert.NotNull(view.Page.Q("lab-before-tankHealth"));
            nav.ActivateElement(view.Page.Q<Button>("lab-back"));Assert.False(closed);Assert.NotNull(root.Q("lab-confirm"));nav.ActivateElement(root.Q<Button>("lab-confirm-cancel"));nav.ActivateElement(view.Page.Q<Button>("lab-save"));Assert.AreEqual(2,store.SelectedVersion);Assert.AreEqual(9,store.SelectedAudio.near);Assert.AreEqual(baseline.TankHealth+100,store.Active.TankHealth);
            nav.ActivateElement(view.Page.Q<Button>("lab-back"));Assert.True(closed);Assert.AreEqual(2,store.Active.Revision);Assert.IsNull(root.Q("laboratory-screen"));
        }finally{nav.Dispose();UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);}
    }
    [UnityTest] public IEnumerator ProductionCopyAndDiscardDialogFitBothViewports()
    {
        var host=new GameObject("lab production/dialog layout");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");var target=new RenderTexture(800,620,0);target.Create();panel.targetTexture=target;
        var document=host.AddComponent<UIDocument>();document.panelSettings=panel;var root=document.rootVisualElement;var nav=new NativeMenuNavigation(root);
        try{foreach(var size in new[]{new Vector2Int(800,620),new Vector2Int(1280,800)}){
            panel.targetTexture=null;target.Release();target.width=size.x;target.height=size.y;target.Create();panel.targetTexture=target;root.style.width=size.x;root.style.height=size.y;OrbitalTheme.ConfigurePanel(panel,root);
            var store=new NativeBalanceStore(baseline,Path.Combine(directory,"production-"+size.x+".json"),false,Dev().ExportPackage());var view=new NativeBalanceView(root,nav,store,()=>{});
            for(int i=0;i<5;i++)yield return null;
            foreach(string key in new[]{"lab-profiles","lab-revisions","lab-copy","lab-back"}){var e=view.Page.Q(key);Assert.Greater(e.worldBound.width,0,key);Assert.LessOrEqual(e.worldBound.xMax,size.x+.5f,key);Assert.LessOrEqual(e.worldBound.yMax,size.y+.5f,key);}
            Assert.True(view.Page.Q<TextField>("lab-field-tankHealth").isReadOnly);Assert.False(view.Page.Q<Button>("lab-rename").enabledSelf);Assert.AreEqual(DisplayStyle.None,view.Page.Q("lab-release").resolvedStyle.display);Capture(target,"production-"+size.x+"x"+size.y);
            nav.ActivateElement(view.Page.Q<Button>("lab-copy"));root.Q<TextField>("lab-name").value="Эксперимент";nav.ActivateElement(root.Q<Button>("lab-name-accept"));Assert.True(store.CanEdit);Assert.False(view.Page.Q<TextField>("lab-field-tankHealth").isReadOnly);
            view.Page.Q<TextField>("lab-search").value="tank.health";view.Page.Q<TextField>("lab-field-tankHealth").value=(baseline.TankHealth+100).ToString();nav.ActivateElement(view.Page.Q<Button>("lab-reset"));Assert.NotNull(root.Q("lab-confirm"));StringAssert.Contains("100 → 200",root.Q("lab-confirm-diff").Q<Label>().text);
            for(int i=0;i<5;i++)yield return null;
            var footer=root.Q("lab-confirm").Q<Label>("menu-copyright");Assert.NotNull(footer);StringAssert.Contains(OrbitalTheme.Copyright,footer.text);Assert.AreEqual(DisplayStyle.Flex,footer.resolvedStyle.display);Assert.Greater(footer.worldBound.height,0);Assert.LessOrEqual(footer.worldBound.yMax,size.y);
            Capture(target,"discard-dialog-"+size.x+"x"+size.y);
            var cancel=root.Q<Button>("lab-confirm-cancel");Assert.LessOrEqual(cancel.worldBound.yMax,size.y);nav.ActivateElement(cancel);Assert.AreEqual(1,store.SelectedVersion);Assert.True(view.Page.Q<Button>("lab-save").enabledSelf);nav.ActivateElement(view.Page.Q<Button>("lab-reset"));nav.ActivateElement(root.Q<Button>("lab-confirm-accept"));Assert.AreNotEqual(NativeBalanceStore.DefaultId,store.SelectedId);Assert.AreEqual(baseline.TankHealth,store.Active.TankHealth);Assert.AreEqual(2,store.Profiles.Count,"Discard preserves the experiment");nav.Back();Assert.IsNull(root.Q("laboratory-screen"));
        }}finally{nav.Dispose();panel.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);}
    }
    private static void Capture(RenderTexture target,string name)
    {
        string directory=Environment.GetEnvironmentVariable("SPACEWARS_LAB_EVIDENCE");if(string.IsNullOrEmpty(directory))return;Directory.CreateDirectory(directory);
        var previous=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(directory,name+".png"),image.EncodeToPNG());}
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
    }
    [UnityTest] public IEnumerator SavedSelectionReachesActualRuntimeAndCannotChangeAnExistingMatch()
    {
        var store=Dev();store.Save(Changed(store),store.SelectedAudio);using(var runtime=new PlayableRuntime(store.Active,1,7,autonomousOwnerAi:false,autonomousEnemyAi:false,startPaused:true))
        {
            float deadline=Time.realtimeSinceStartup+10;while(runtime.Latest==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.NotNull(runtime.Latest);Assert.IsNull(runtime.Latest.Failure);Assert.AreEqual(store.Active.TankHealth,runtime.Latest.ActiveProfile.TankHealth);StringAssert.Contains("spacewars-default @2",runtime.Latest.ActiveProfile.DisplayName);
            var fixedProfile=runtime.Latest.ActiveProfile;store.Save(Changed(store),store.SelectedAudio);Assert.AreEqual(3,store.SelectedVersion);Assert.AreEqual(2,fixedProfile.Revision);Assert.AreNotEqual(store.Active.TankHealth,fixedProfile.TankHealth);
            runtime.RequestStop();while(!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)yield return null;Assert.True(runtime.IsStopped);
        }
    }
}
