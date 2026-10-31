using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeModelDetailLabTests
    {
        string directory;
        [SetUp] public void Setup(){directory=Path.Combine(Path.GetTempPath(),"spacewars-lod-lab-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);}
        [TearDown] public void Cleanup(){ModelDetailProfile.UseSession(null);if(Directory.Exists(directory))Directory.Delete(directory,true);}
        NativeBalanceStore Store(string path)=>new NativeBalanceStore(PlayableProfile.Default,path);
        [Test] public void LegacyFallbackCopySaveAndReloadKeepIndependentImmutableVersions()
        {
            string path=Path.Combine(directory,"profiles.json");var store=Store(path);var shipped=ModelDetailProfile.Load();
            Assert.AreEqual(shipped.farEnter,store.SelectedModelDetail.farEnter);store.Copy("LOD experiment");
            var first=store.SelectedRow.ToString();var data=store.Selected.CopyData();var detail=store.SelectedModelDetail;var audio=store.SelectedAudio;
            detail.farEnter=.07f;detail.farExit=.08f;store.Save(data,audio,modelDetail:detail);
            Assert.AreEqual(.07f,store.ActiveModelDetail.farEnter);Assert.AreEqual(.08f,store.ActiveModelDetail.farExit);
            Assert.AreEqual(first,store.Versions.Single(v=>(int)v["revision"]==1).ToString(),"Old row is immutable");
            foreach(var field in PlayableProfileMetadata.Fields.Where(NativeBalanceFields.Editable))Assert.AreEqual(field.Read(data),field.Read(store.Active.CopyData()),field.Path);
            Assert.AreEqual(audio.near,store.ActiveAudio.near);Assert.AreEqual(store.SelectedAiProfile.Id,store.ActiveAiProfile.Id);
            var reloaded=Store(path);Assert.AreEqual(.07f,reloaded.SelectedModelDetail.farEnter);Assert.AreEqual(.08f,reloaded.ActiveModelDetail.farExit);
            reloaded.Select(1);Assert.AreEqual(shipped.farEnter,reloaded.ActiveModelDetail.farEnter);
            reloaded.Select(2);reloaded.Copy("Separate LOD");Assert.AreEqual(.07f,reloaded.ActiveModelDetail.farEnter);
        }
        [Test] public void InvalidOrderAndFractionalStepNeverWriteNewHistory()
        {
            string path=Path.Combine(directory,"profiles.json");var store=Store(path);store.Copy("LOD invalid");var before=File.ReadAllBytes(path);
            var p=store.SelectedModelDetail;p.farEnter=.09f;p.farExit=.08f;
            Assert.Throws<ArgumentException>(()=>store.Save(store.Selected.CopyData(),store.SelectedAudio,modelDetail:p));CollectionAssert.AreEqual(before,File.ReadAllBytes(path));
            p=store.SelectedModelDetail;p.farEnter=.056f;
            Assert.Throws<ArgumentException>(()=>store.Save(store.Selected.CopyData(),store.SelectedAudio,modelDetail:p));CollectionAssert.AreEqual(before,File.ReadAllBytes(path));
        }
        [Test] public void HealthDistanceThresholdHasMetadataAndPersistsInImmutableLabVersion()
        {
            string path=Path.Combine(directory,"health-distance.json");var store=Store(path);store.Copy("Health distance");
            var detail=store.SelectedModelDetail;Assert.AreEqual(ModelDetailProfile.Load().healthHideZoom,detail.healthHideZoom,"Legacy stored row falls back to authored threshold");var field=detail.Parameters().Single(f=>f.Path=="visual.modelDetail.healthHideZoom");
            Assert.AreEqual("Полоски здоровья",field.Group);Assert.IsNotEmpty(field.Description);Assert.AreEqual("м",field.Unit);Assert.AreEqual(5,field.Minimum);Assert.AreEqual(100,field.Maximum);Assert.AreEqual(1,field.Step);
            detail.healthHideZoom=24;store.Save(store.Selected.CopyData(),store.SelectedAudio,modelDetail:detail);Assert.AreEqual(24,Store(path).ActiveModelDetail.healthHideZoom);
            var bytes=File.ReadAllBytes(path);foreach(float value in new[]{float.NaN,float.PositiveInfinity,4,101,20.5f}){
                detail.healthHideZoom=value;Assert.Throws<ArgumentException>(()=>store.Save(store.Selected.CopyData(),store.SelectedAudio,modelDetail:detail));CollectionAssert.AreEqual(bytes,File.ReadAllBytes(path));
            }
        }

        [Test] public void WholeVehicleTrailControlsPersistWithoutChangingGameplay()
        {
            string path=Path.Combine(directory,"vehicle-trails.json");var store=Store(path);store.Copy("Vehicle trail cohort");var data=store.Selected.CopyData();var detail=store.SelectedModelDetail;
            detail.trailHideZoom=22;detail.trailCrowdThreshold=24;detail.trailLargeCrowdThreshold=48;detail.trailCrowdDivisor=3;detail.trailLargeCrowdDivisor=6;
            Assert.AreEqual(5,detail.Parameters().Count(f=>f.Group=="Следы техники"));store.Save(data,store.SelectedAudio,modelDetail:detail);
            var restored=Store(path).SelectedModelDetail;Assert.AreEqual(22,restored.trailHideZoom);Assert.AreEqual(24,restored.trailCrowdThreshold);Assert.AreEqual(48,restored.trailLargeCrowdThreshold);Assert.AreEqual(3,restored.trailCrowdDivisor);Assert.AreEqual(6,restored.trailLargeCrowdDivisor);
            foreach(var field in PlayableProfileMetadata.Fields.Where(NativeBalanceFields.Editable))Assert.AreEqual(field.Read(data),field.Read(store.Active.CopyData()),field.Path);
            var bytes=File.ReadAllBytes(path);detail=store.SelectedModelDetail;detail.trailCrowdDivisor=2.5f;Assert.Throws<ArgumentException>(()=>store.Save(data,store.SelectedAudio,modelDetail:detail));CollectionAssert.AreEqual(bytes,File.ReadAllBytes(path));
        }

    }
}
