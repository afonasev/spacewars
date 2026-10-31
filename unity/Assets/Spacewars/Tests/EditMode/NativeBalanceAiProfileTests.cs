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

        [Test]
        public void OnlyNamedOpeningMidgameMetadataIsEditable()
        {
            var fields=NativeBalanceFields.AiOpeningFields;
            Assert.IsNotEmpty(fields);
            Assert.IsTrue(fields.All(f=>f.Path.StartsWith("openingsMidgame.",StringComparison.Ordinal)));
            Assert.IsFalse(fields.Any(f=>f.Path.StartsWith("opening.weights.",StringComparison.Ordinal)));
            CollectionAssert.AreEquivalent(AiProfileMetadata.Fields.Where(f=>f.Path.StartsWith("openingsMidgame.",StringComparison.Ordinal)).Select(f=>f.Path),fields.Select(f=>f.Path));
            var legacy=AiProfileMetadata.Fields.Single(f=>f.Path=="opening.weights.safe");var data=AiProfile.Initial.CopyData();
            Assert.DoesNotThrow(()=>NativeBalanceFields.Write(legacy,data,legacy.Initial+legacy.Step),"The existing generic AI field writer remains backward compatible.");
        }

        [Test]
        public void SavedAiProfileSurvivesReloadAndReachesNewOrdinaryLobbyMatch()
        {
            Directory.CreateDirectory(directory);var path=Path.Combine(directory,"profiles.json");var store=MakeStore(path);
            var gameplay=store.Selected.CopyData();var audio=store.SelectedAudio;var ai=store.SelectedAiProfile.CopyData();
            var opening=NativeBalanceFields.AiOpeningFields.Single(f=>f.Path=="openingsMidgame.opening.aggressive");
            NativeBalanceFields.Write(opening,ai,opening.Initial+opening.Step);
            store.Save(gameplay,audio,ai);
            Assert.AreEqual(opening.Initial+opening.Step,store.ActiveAiProfile.Value(opening.Path));
            Assert.AreEqual(PlayableProfile.ThreeCrossingsDefault.TankCreditCost,store.Active.TankCreditCost);
            Assert.AreEqual(audio.near,store.ActiveAudio.near);

            var reloaded=MakeStore(path);
            Assert.AreEqual(opening.Initial+opening.Step,reloaded.ActiveAiProfile.Value(opening.Path));
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=71124};
            setup.InitializeParticipants();
            using(var runtime=PlayableRuntime.CreateLobbyMatch(reloaded.Active,21,setup,startPaused:true,aiProfile:reloaded.ActiveAiProfile))
            {
                Assert.AreEqual(NativeBalanceStore.AiProfileId,runtime.NativeAiProfile.Id);
                Assert.AreEqual(opening.Initial+opening.Step,runtime.NativeAiProfile.Value(opening.Path));
                runtime.RequestStop();Assert.IsTrue(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));
            }
        }

        [Test]
        public void ResettingNamedAiProfilePreservesOtherDraftSettings()
        {
            var p=PlayableProfile.Default.CopyData();p.tankCreditCost+=10;
            var ai=NativeBalanceFields.InitialAiOpeningProfile().CopyData();
            var field=NativeBalanceFields.AiOpeningFields.Single(f=>f.Path=="openingsMidgame.opening.aggressive");
            NativeBalanceFields.Write(field,ai,field.Initial+field.Step);
            var reset=NativeBalanceFields.InitialAiOpeningProfile().CopyData();
            Assert.AreEqual(field.Initial,reset.fields.Single(f=>f.path==field.Path).value);
            Assert.AreEqual(PlayableProfile.Default.TankCreditCost+10,p.tankCreditCost);
        }

        [Test]
        public void AiMetadataBoundsAndStepsAreEnforcedWithoutChangingDefaults()
        {
            var profile=NativeBalanceFields.InitialAiOpeningProfile();var data=profile.CopyData();
            var field=NativeBalanceFields.AiOpeningFields.First();var original=profile.Value(field.Path);
            Assert.Throws<ArgumentOutOfRangeException>(()=>NativeBalanceFields.Write(field,data,field.Maximum+field.Step));
            Assert.AreEqual(original,data.fields.Single(v=>v.path==field.Path).value);
            Assert.DoesNotThrow(()=>NativeBalanceStore.CheckStep(original+field.Step,original,field.Step,field.Path));
            Assert.Throws<ArgumentException>(()=>NativeBalanceStore.CheckStep(original+field.Step/2,original,field.Step,field.Path));
        }
    }
}
