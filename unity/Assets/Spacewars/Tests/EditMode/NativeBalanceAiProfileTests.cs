using System;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeBalanceAiProfileTests
    {
        private string directory;
        private NativeBalanceStore MakeStore(string path)=>new NativeBalanceStore(PlayableProfile.ThreeCrossingsDefault,path);
        [SetUp] public void Setup()=>directory=Path.Combine(Path.GetTempPath(),"spacewars-ai-balance-"+Guid.NewGuid().ToString("N"));
        [TearDown] public void Cleanup(){if(Directory.Exists(directory))Directory.Delete(directory,true);}

        [TestCase(true)][TestCase(false)]
        public void SavedLegacyBackendUsesNewEngineDefaultWithoutRewritingHistory(bool authoring)
        {
            string root=Environment.GetEnvironmentVariable("NATIVE_DEFAULT_LAB_OUTPUT")??Path.GetFullPath("../.local/recast-formation/native-default-legacy-lab-baseline-20261101");
            string owned=Path.Combine(root,authoring?"authoring":"installed");Assert.False(Directory.Exists(owned),"Preserve prior compatibility witness");Directory.CreateDirectory(owned);string path=Path.Combine(owned,"profiles.json");
            var current=PlayableProfile.ThreeCrossingsDefault;var oldData=current.CopyData();oldData.groundRouteBackend=0;var old=PlayableProfile.Create(oldData,current.AuthoredMap);
            var store=new NativeBalanceStore(old,path,authoring);Assert.True(store.HasActive,store.Problem);store.Copy("Legacy custom terms");
            var gameplay=store.Selected.CopyData();gameplay.tankCreditCost+=10;var audio=store.SelectedAudio;var ai=store.SelectedAiProfile.CopyData();var field=NativeBalanceFields.AiOpeningFields.Single(f=>f.Path=="openingsMidgame.opening.aggressive");NativeBalanceFields.Write(field,ai,field.Initial+field.Step);
            store.Save(gameplay,audio,ai);string id=store.SelectedId;int version=store.SelectedVersion;double cost=store.Active.TankCreditCost;string aiHash=store.ActiveAiProfile.Hash;string audioJson=Newtonsoft.Json.JsonConvert.SerializeObject(store.ActiveAudio);var raw=File.ReadAllBytes(path);var document=store.Document.DeepClone();
            Assert.False(store.Active.UsesRecastGroundRoutes);Assert.AreEqual(0,(int)store.SelectedRow["data"][nameof(PlayableProfileData.groundRouteBackend)]);
            File.WriteAllBytes(Path.Combine(owned,"before-store.bin"),raw);File.WriteAllText(Path.Combine(owned,"before-document.json"),document.ToString());
            var reloaded=new NativeBalanceStore(current,path,authoring);bool unchanged=raw.SequenceEqual(File.ReadAllBytes(path));
            File.WriteAllText(Path.Combine(owned,"startup-readback.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{authoring,reloaded.HasActive,reloaded.Problem,ExpectedId=id,reloaded.SelectedId,version,reloaded.SelectedVersion,ActiveBackend=reloaded.HasActive?(bool?)reloaded.Active.UsesRecastGroundRoutes:null,ExpectedCost=cost,ActualCost=reloaded.HasActive?(double?)reloaded.Active.TankCreditCost:null,StoreUnchanged=unchanged},Newtonsoft.Json.Formatting.Indented));
            Assert.True(unchanged,"startup rewrote old user history");Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(document,reloaded.Document));Assert.True(reloaded.HasActive,reloaded.Problem);
            Assert.AreEqual(id,reloaded.SelectedId,"startup silently replaced the selected custom profile");Assert.AreEqual(version,reloaded.SelectedVersion);Assert.True(reloaded.Active.UsesRecastGroundRoutes);Assert.AreSame(current.AuthoredMap,reloaded.Active.AuthoredMap);
            Assert.AreEqual(cost,reloaded.Active.TankCreditCost);Assert.AreEqual(aiHash,reloaded.ActiveAiProfile.Hash);Assert.AreEqual(audioJson,Newtonsoft.Json.JsonConvert.SerializeObject(reloaded.ActiveAudio));
            Assert.AreEqual(0,(int)reloaded.SelectedRow["data"][nameof(PlayableProfileData.groundRouteBackend)],"old serialized versions must remain exact");
            var invalid=(Newtonsoft.Json.Linq.JObject)reloaded.SelectedRow.DeepClone();invalid["data"][nameof(PlayableProfileData.groundRouteBackend)]=2;Assert.Throws<FormatException>(()=>reloaded.Compile(invalid));
            var badDraft=reloaded.Active.CopyData();badDraft.groundRouteBackend=0;badDraft.tankCreditCost+=10;Assert.Throws<ArgumentException>(()=>reloaded.Save(badDraft,reloaded.ActiveAudio));
            Assert.NotNull(NativeBalanceFields.ValidateLiveDifference(current,PlayableProfile.Create(badDraft,current.AuthoredMap)),"live backend changes remain forbidden");CollectionAssert.AreEqual(raw,File.ReadAllBytes(path));

        }

        [Test]
        public void NamedOpeningAndPerDifficultyControlMetadataIsEditable()
        {
            var fields=NativeBalanceFields.AiOpeningFields;
            Assert.IsNotEmpty(fields);
            Assert.IsTrue(fields.All(f=>f.Path.StartsWith("openingsMidgame.",StringComparison.Ordinal)||(f.Path.StartsWith("positionDevelopment.",StringComparison.Ordinal)||f.Path.StartsWith("activeStrategy.",StringComparison.Ordinal))||f.Path.StartsWith("safeReinforcements.",StringComparison.Ordinal)||f.Path.StartsWith("difficulty.",StringComparison.Ordinal)));
            foreach(var difficulty in new[]{AiDifficulty.Recruit,AiDifficulty.Fighter,AiDifficulty.Veteran})
            {
                string prefix="difficulty."+AiProfile.DifficultyId(difficulty)+".";
                foreach(string key in new[]{"reactionSeconds","control.ordersPerMinute","control.burst","control.reactionMaxSeconds","micro.intensity","micro.withdrawEnabled","micro.repairEnabled","micro.focusEnabled","micro.airFocusEnabled","micro.chaseEnabled","micro.focusGroupSize"})
                    Assert.IsTrue(fields.Any(f=>f.Path==prefix+key),prefix+key);
            }
            Assert.IsFalse(fields.Any(f=>f.Path.StartsWith("opening.weights.",StringComparison.Ordinal)));
            CollectionAssert.AreEquivalent(AiProfileMetadata.Fields.Where(f=>f.Path.StartsWith("openingsMidgame.",StringComparison.Ordinal)||f.Path.StartsWith("positionDevelopment.",StringComparison.Ordinal)||f.Path.StartsWith("activeStrategy.",StringComparison.Ordinal)||f.Path.StartsWith("safeReinforcements.",StringComparison.Ordinal)||f.Path.StartsWith("difficulty.",StringComparison.Ordinal)&&(f.Path.Contains(".control.")||f.Path.Contains(".micro.")||f.Path.EndsWith(".reactionSeconds",StringComparison.Ordinal))).Select(f=>f.Path).ToArray(),fields.Select(f=>f.Path).ToArray());
            Assert.AreEqual(fields.Count,fields.Select(f=>f.Path).Distinct().Count());
            var legacy=AiProfileMetadata.Fields.Single(f=>f.Path=="opening.weights.safe");var data=AiProfile.Initial.CopyData();
            Assert.DoesNotThrow(()=>NativeBalanceFields.Write(legacy,data,legacy.Initial+legacy.Step),"The existing generic AI field writer remains backward compatible.");
        }

        [TestCase("openingsMidgame.opening.aggressive")][TestCase("positionDevelopment.towerValue")][TestCase("activeStrategy.developmentSeconds")][TestCase("safeReinforcements.coreRadius")]
        public void SavedAiProfileSurvivesReloadAndReachesNewOrdinaryLobbyMatch(string fieldPath)
        {
            Directory.CreateDirectory(directory);var path=Path.Combine(directory,"profiles.json");var store=MakeStore(path);
            var gameplay=store.Selected.CopyData();var audio=store.SelectedAudio;var ai=store.SelectedAiProfile.CopyData();
            var opening=NativeBalanceFields.AiOpeningFields.Single(f=>f.Path==fieldPath);
            NativeBalanceFields.Write(opening,ai,opening.Initial+opening.Step);
            var controls=NativeBalanceFields.AiOpeningFields.Where(f=>f.Path.EndsWith(".control.ordersPerMinute",StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(3,controls.Length);
            foreach(var f in controls)NativeBalanceFields.Write(f,ai,f.Initial+f.Step);
            store.Save(gameplay,audio,ai);
            Assert.AreEqual(opening.Initial+opening.Step,store.ActiveAiProfile.Value(opening.Path));
            Assert.AreEqual(PlayableProfile.ThreeCrossingsDefault.TankCreditCost,store.Active.TankCreditCost);
            Assert.AreEqual(audio.near,store.ActiveAudio.near);

            var reloaded=MakeStore(path);
            Assert.AreEqual(opening.Initial+opening.Step,reloaded.ActiveAiProfile.Value(opening.Path));
            foreach(var f in controls)Assert.AreEqual(f.Initial+f.Step,reloaded.ActiveAiProfile.Value(f.Path));
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=71124};
            setup.InitializeParticipants();
            using(var runtime=PlayableRuntime.CreateLobbyMatch(reloaded.Active,21,setup,startPaused:true,aiProfile:reloaded.ActiveAiProfile))
            {
                Assert.AreEqual(NativeBalanceStore.AiProfileId,runtime.NativeAiProfile.Id);
                Assert.AreEqual(opening.Initial+opening.Step,runtime.NativeAiProfile.Value(opening.Path));
                foreach(var f in controls)Assert.AreEqual(f.Initial+f.Step,runtime.NativeAiProfile.Value(f.Path));
                runtime.RequestStop();Assert.IsTrue(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));
            }
        }

        [TestCase("openingsMidgame.opening.aggressive")][TestCase("positionDevelopment.towerValue")][TestCase("activeStrategy.developmentSeconds")][TestCase("safeReinforcements.coreRadius")]
        public void ResettingNamedAiProfilePreservesOtherDraftSettings(string fieldPath)
        {
            var p=PlayableProfile.Default.CopyData();p.tankCreditCost+=10;
            var ai=NativeBalanceFields.InitialAiOpeningProfile().CopyData();
            var field=NativeBalanceFields.AiOpeningFields.Single(f=>f.Path==fieldPath);
            NativeBalanceFields.Write(field,ai,field.Initial+field.Step);
            var reset=NativeBalanceFields.InitialAiOpeningProfile().CopyData();
            Assert.AreEqual(field.Initial,reset.fields.Single(f=>f.path==field.Path).value);
            Assert.AreEqual(PlayableProfile.Default.TankCreditCost+10,p.tankCreditCost);
        }

        [TestCase("openingsMidgame.opening.aggressive")][TestCase("positionDevelopment.repairHealth")]
        public void AiMetadataBoundsAndStepsAreEnforcedWithoutChangingDefaults(string fieldPath)
        {
            var profile=NativeBalanceFields.InitialAiOpeningProfile();var data=profile.CopyData();
            var field=NativeBalanceFields.AiOpeningFields.Single(f=>f.Path==fieldPath);var original=profile.Value(field.Path);
            Assert.Throws<ArgumentOutOfRangeException>(()=>NativeBalanceFields.Write(field,data,field.Maximum+field.Step));
            Assert.AreEqual(original,data.fields.Single(v=>v.path==field.Path).value);
            Assert.DoesNotThrow(()=>NativeBalanceStore.CheckStep(original+field.Step,original,field.Step,field.Path));
            Assert.Throws<ArgumentException>(()=>NativeBalanceStore.CheckStep(original+field.Step/2,original,field.Step,field.Path));
        }
        [Test]
        public void InvalidPositionRepairThresholdsCannotReplaceSavedProfile()
        {
            Directory.CreateDirectory(directory);var path=Path.Combine(directory,"profiles.json");var store=MakeStore(path);var before=store.ActiveAiProfile.Hash;
            var ai=store.SelectedAiProfile.CopyData();var health=NativeBalanceFields.AiOpeningFields.Single(f=>f.Path=="positionDevelopment.repairHealth");
            NativeBalanceFields.Write(health,ai,health.Maximum);
            Assert.Throws<ArgumentException>(()=>store.Save(store.Selected.CopyData(),store.SelectedAudio,ai));
            Assert.AreEqual(before,store.ActiveAiProfile.Hash);Assert.AreEqual(before,MakeStore(path).ActiveAiProfile.Hash);
        }
    }
}
