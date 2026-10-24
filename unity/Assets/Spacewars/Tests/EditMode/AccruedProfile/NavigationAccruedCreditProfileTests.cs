using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationAccruedCreditProfileTests
    {
        [Test] public void NamedAccruedRevisionPreservesShippedDefaultAndLegacyAbsence()
        {
            var raw=Resources.Load<TextAsset>("PlayableProfile").text;var data=JsonUtility.FromJson<PlayableProfileData>(raw);
            Assert.AreEqual(1,data.revision);Assert.Zero(data.navigationMaxAccruedCredits);Assert.False(raw.Contains("navigationMaxAccruedCredits"));
            string directory=Path.Combine(Path.GetTempPath(),"spacewars-accrued-profile-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try{
                var store=new NativeBalanceStore(PlayableProfile.Create(data),Path.Combine(directory,"profiles.json"),false);
                Assert.IsNull(store.Problem);Assert.AreEqual("spacewars-default",store.SelectedId);Assert.AreEqual(1,store.SelectedVersion);StringAssert.Contains("spacewars-default @1",store.Selected.DisplayName);Assert.Zero(store.Selected.CopyData().navigationMaxAccruedCredits);
                store.Select("spacewars-accrued-navigation",1);Assert.AreEqual(600,store.Selected.MaxAccruedCredits);Assert.AreEqual(600,store.Selected.CopyData().navigationMaxAccruedCredits);
                var round=JsonUtility.FromJson<PlayableProfileData>(JsonUtility.ToJson(store.Selected.CopyData()));Assert.AreEqual(600,round.navigationMaxAccruedCredits);Assert.AreEqual(600,PlayableProfile.Create(round).MaxAccruedCredits);
                var package=(Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.Linq.JObject.Parse(Resources.Load<TextAsset>("NativeLabCatalog").text);package["profiles"].Single(p=>(string)p["id"]=="spacewars-accrued-navigation")["versions"][0]["data"]["navigationMaxAccruedCredits"]=0;
                var badStore=new NativeBalanceStore(PlayableProfile.Create(data),Path.Combine(directory,"invalid.json"),false,package);Assert.Throws<FormatException>(()=>badStore.Select("spacewars-accrued-navigation",1),"explicit zero must not be normalized into legacy absence");
                store.Select("spacewars-default",1);Assert.Zero(store.Selected.CopyData().navigationMaxAccruedCredits,"old revision must not inherit optional release/profile defaults");
            }finally{Directory.Delete(directory,true);}
        }
        [Test] public void CreditCapMetadataControlsIntegerInputAndValidation()
        {
            var f=PlayableProfileMetadata.AccruedCreditField;Assert.AreEqual("navigation.maxAccruedCredits",f.Path);Assert.AreEqual("Navigation",f.Group);Assert.AreEqual("Лимит накопленного бюджета навигации",f.Label);Assert.AreEqual("квант",f.Unit);Assert.AreEqual(1,f.Minimum);Assert.AreEqual(600,f.Maximum);Assert.AreEqual(1,f.Step);Assert.True(NativeBalanceFields.Editable(f));
            foreach(int cap in new[]{1,600}){var data=PlayableProfile.Default.CopyData();f.Write(data,cap);Assert.AreEqual(cap,PlayableProfile.Create(data).MaxAccruedCredits);}
            foreach(double cap in new[]{0d,601d})Assert.Throws<ArgumentOutOfRangeException>(()=>f.Write(PlayableProfile.Default.CopyData(),cap));Assert.Throws<ArgumentException>(()=>f.Write(PlayableProfile.Default.CopyData(),1.5));
        }
    }
}
