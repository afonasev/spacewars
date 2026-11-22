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
        [Test]
        public void BalanceOnlyAndAiOnlySavesShareIdentityAndPreserveBothParts()
        {
            Directory.CreateDirectory(directory);var path=Path.Combine(directory,"unified.json");var store=MakeStore(path);
            var before=store.SelectedAiProfile.CopyData();var oldRows=store.Versions.DeepClone();
            var balance=store.Selected.CopyData();balance.tankCreditCost+=10;
            store.Save(balance,store.SelectedAudio);
            Assert.AreEqual(store.SelectedId,store.ActiveAiProfile.Id);
            Assert.AreEqual(store.Active.Revision,store.ActiveAiProfile.Revision);
            foreach(var field in before.fields)Assert.AreEqual(field.value,store.ActiveAiProfile.Value(field.path));
            var ai=store.SelectedAiProfile.CopyData();var fieldToChange=NativeBalanceFields.AiOpeningFields.Single(f=>f.Path=="openingsMidgame.opening.aggressive");
            NativeBalanceFields.Write(fieldToChange,ai,store.ActiveAiProfile.Value(fieldToChange.Path)+fieldToChange.Step);
            store.Save(store.Selected.CopyData(),store.SelectedAudio,ai);
            var reloaded=MakeStore(path);
            Assert.AreEqual(balance.tankCreditCost,reloaded.Active.TankCreditCost);
            Assert.AreEqual(reloaded.SelectedId,reloaded.ActiveAiProfile.Id);
            Assert.AreEqual(reloaded.Active.Revision,reloaded.ActiveAiProfile.Revision);
            Assert.AreEqual(ai.fields.Single(f=>f.path==fieldToChange.Path).value,reloaded.ActiveAiProfile.Value(fieldToChange.Path));
            for(int i=0;i<oldRows.Count();i++)Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(oldRows[i],store.Versions[i]));
            reloaded.Copy("Общий эксперимент");
            Assert.AreEqual(1,reloaded.SelectedVersion);Assert.AreEqual(1,reloaded.ActiveAiProfile.Revision);
            Assert.AreEqual(reloaded.SelectedId,reloaded.ActiveAiProfile.Id);
            Assert.AreEqual(1,(int)reloaded.SelectedRow["data"]["revision"]);
            Assert.AreEqual(reloaded.SelectedId,(string)reloaded.SelectedRow["aiProfile"]["id"]);
        }

        [Test]
        public void LegacyAiIdentityIsBoundOnReadWithoutRewritingAuthoringHistory()
        {
            Directory.CreateDirectory(directory);var path=Path.Combine(directory,"legacy-ai.json");var store=MakeStore(path);store.Copy("Проверка чтения");
            var original=File.ReadAllBytes(path);
            foreach(var profile in store.Profiles)
            foreach(var row in profile["versions"])
            {
                store.Select((string)profile["id"],(int)row["revision"]);
                Assert.AreEqual(store.SelectedId,store.SelectedAiProfile.Id);
                Assert.AreEqual(store.SelectedVersion,store.SelectedAiProfile.Revision);
            }
            CollectionAssert.AreEqual(original,File.ReadAllBytes(path));
        }

        [Test]
        public void ReleaseExportContainsOneCompleteSnapshotAndLeavesDraftHistoryIntact()
        {
            Directory.CreateDirectory(directory);var path=Path.Combine(directory,"release.json");var store=MakeStore(path);
            var balance=store.Selected.CopyData();balance.tankCreditCost+=10;store.Save(balance,store.SelectedAudio);store.Mark(true);
            int released=store.SelectedVersion;var expectedBalance=store.Selected.CopyData();var expectedAi=store.SelectedAiProfile;
            var expectedAudio=Newtonsoft.Json.JsonConvert.SerializeObject(store.SelectedAudio);var expectedDetail=Newtonsoft.Json.JsonConvert.SerializeObject(store.SelectedModelDetail);
            balance=store.Selected.CopyData();balance.tankCreditCost+=10;store.Save(balance,store.SelectedAudio);
            store.Copy("Не включать в поставку");store.Mark(true);
            var before=File.ReadAllBytes(path);var selectedId=store.SelectedId;var selectedVersion=store.SelectedVersion;
            var package=store.ExportReleasePackage("0.6.20");
            Assert.AreEqual("0.6.20",(string)package["releaseVersion"]);
            Assert.AreEqual(1,package["profiles"].Count());Assert.AreEqual(1,package["profiles"][0]["versions"].Count());
            CollectionAssert.AreEqual(new[]{released},package["profiles"][0]["release"].Select(v=>(int)v));
            Assert.AreEqual(released,(int)package["profileDefaultsVersion"]);
            CollectionAssert.AreEqual(before,File.ReadAllBytes(path));Assert.AreEqual(selectedId,store.SelectedId);Assert.AreEqual(selectedVersion,store.SelectedVersion);
            var player=new NativeBalanceStore(PlayableProfile.ThreeCrossingsDefault,Path.Combine(directory,"player.json"),false,package);
            Assert.True(player.HasActive,player.Problem);Assert.False(player.CanEdit);Assert.AreEqual(released,player.Active.Revision);
            Assert.AreEqual(NativeBalanceStore.DefaultId,player.ActiveAiProfile.Id);Assert.AreEqual(released,player.ActiveAiProfile.Revision);
            foreach(var field in PlayableProfileMetadata.Fields)Assert.AreEqual(field.Read(expectedBalance),field.Read(player.Active.CopyData()),field.Path);
            Assert.AreEqual(expectedAi.Hash,player.ActiveAiProfile.Hash);
            Assert.AreEqual(expectedAudio,Newtonsoft.Json.JsonConvert.SerializeObject(player.ActiveAudio));Assert.AreEqual(expectedDetail,Newtonsoft.Json.JsonConvert.SerializeObject(player.ActiveModelDetail));
            player.Copy("Личный эксперимент");
            Assert.Null(player.SelectedRow["releaseVersion"]);Assert.AreEqual(player.SelectedId,player.ActiveAiProfile.Id);
            Assert.AreEqual(player.Active.Revision,player.ActiveAiProfile.Revision);
            Assert.Throws<ArgumentException>(()=>store.ExportReleasePackage(""));
            Assert.Throws<InvalidOperationException>(()=>player.ExportReleasePackage("0.6.20"));
        }

        [Test]
        public void UnifiedAiVersionMismatchIsRejectedWithoutChangingActiveProfile()
        {
            Directory.CreateDirectory(directory);var path=Path.Combine(directory,"mismatch.json");var store=MakeStore(path);store.Copy("Связанный профиль");
            var package=store.ExportReleasePackage("0.6.20");var active=store.ActiveAiProfile.Hash;
            package["profiles"][0]["versions"][0]["aiProfile"]["revision"]=2;
            var player=new NativeBalanceStore(PlayableProfile.ThreeCrossingsDefault,Path.Combine(directory,"bad-player.json"),false,package);
            Assert.False(player.HasActive);Assert.NotNull(player.ReleaseProblem);
            Assert.AreEqual(active,store.ActiveAiProfile.Hash);
        }

        [Test]
        public void BuildRoutingCompactsReleasesAndPreservesLocalDiagnosticCatalog()
        {
            Directory.CreateDirectory(directory);var store=MakeStore(Path.Combine(directory,"build-routing.json"));
            var editorType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Spacewars.Editor.NativeLabBuildCatalog")).Single(t=>t!=null);
            var method=editorType.GetMethod("CatalogForBuild");
            var diagnostic=(Newtonsoft.Json.Linq.JObject)method.Invoke(null,new object[]{store,null});
            Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(store.ExportPackage(),diagnostic));
            var release=(Newtonsoft.Json.Linq.JObject)method.Invoke(null,new object[]{store,"0.6.20"});
            Assert.AreEqual(1,release["profiles"].Count());Assert.AreEqual(1,release["profiles"][0]["versions"].Count());
            Assert.AreEqual("0.6.20",(string)release["releaseVersion"]);
        }

        [Test]
        public void ObsoleteFocusWithdrawalThresholdsStayStoredButLeaveLab()
        {
            foreach(var suffix in new[]{".micro.focusAttackers",".micro.lossPerSecond"})
            {
                Assert.False(NativeBalanceFields.AiLabFields.Any(f=>f.Path.EndsWith(suffix)));
                Assert.True(AiProfile.Initial.CopyData().fields.Any(f=>f.path.EndsWith(suffix)));
            }
        }
        [Test]
        public void IndependentLabProjectionPreservesLegacyAndSeparatesSharedValues()
        {
            var data=PlayableProfile.Default.CopyData();
            data.turretHealth=205;
            data.gaussHealthMultiplier=1.5;
            data.turretCreditCost=225;
            data.gaussCostMultiplier=1.3;
            data.shkvalBlastRadius=1.5;
            data.shkvalBaseBlastRadiusMultiplier=.8;
            var legacy=PlayableProfile.Create(data);
            PlayableProfileMetadata.ProjectIndependentLabStats(data);
            var migrated=PlayableProfile.Create(data);
            Assert.AreEqual(307,migrated.GaussHealth);
            Assert.AreEqual(292,migrated.GaussCreditCost);
            Assert.AreEqual(legacy.GaussVision,migrated.GaussVision);
            Assert.AreEqual(legacy.RepairTowerRange,migrated.RepairTowerRange);
            Assert.AreEqual(legacy.MineEnemyClearanceRadius,migrated.MineEnemyClearanceRadius);
            Assert.AreEqual(PlayableUnitRules.ShkvalBlastRadius(legacy,false),PlayableUnitRules.ShkvalBlastRadius(migrated,false));
            data.turretHealth+=50;
            data.turretCreditCost+=25;
            data.turretBuildSeconds+=5;
            data.turretVision+=2;
            data.turretRange+=2;
            data.gaussRange+=2;
            data.explorerVision+=2;
            data.shkvalBlastRadius+=.2;
            data.orbitalMineEnemyClearanceTankBodies+=2;
            var changed=PlayableProfile.Create(data);
            Assert.AreEqual(migrated.GaussHealth,changed.GaussHealth);
            Assert.AreEqual(migrated.GaussCreditCost,changed.GaussCreditCost);
            Assert.AreEqual(migrated.GaussBuildSeconds,changed.GaussBuildSeconds);
            Assert.AreEqual(migrated.GaussVision,changed.GaussVision);
            Assert.AreEqual(migrated.RepairTowerHealth,changed.RepairTowerHealth);
            Assert.AreEqual(migrated.RepairTowerCreditCost,changed.RepairTowerCreditCost);
            Assert.AreEqual(migrated.RepairTowerVision,changed.RepairTowerVision);
            Assert.AreEqual(migrated.RepairTowerRange,changed.RepairTowerRange);
            Assert.AreEqual(migrated.HawkVision,changed.HawkVision);
            Assert.AreEqual(migrated.ShkvalBaseBlastRadius,changed.ShkvalBaseBlastRadius);
            Assert.AreEqual(migrated.MineEnemyClearanceRadius,changed.MineEnemyClearanceRadius);
            Assert.AreEqual(changed.RepairTowerRange,TerritoryRules.AiDefenseRange(changed,PlayableBuildingKind.RepairTower));
        }


        private string directory;
        private NativeBalanceStore MakeStore(string path)=>new NativeBalanceStore(PlayableProfile.ThreeCrossingsDefault,path);
        [SetUp] public void Setup()=>directory=Path.Combine(Path.GetTempPath(),"spacewars-ai-balance-"+Guid.NewGuid().ToString("N"));
        [TearDown] public void Cleanup(){if(Directory.Exists(directory))Directory.Delete(directory,true);}

        [TestCase(true)][TestCase(false)]
        public void SavedLegacyBackendUsesNewEngineDefaultWithoutRewritingHistory(bool authoring)
        {
            string root=Environment.GetEnvironmentVariable("NATIVE_DEFAULT_LAB_OUTPUT")??directory;
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
            var extraPaths=new[]{"economy.continuityEnabled","scouting.maximumCombatHealthLossShare","difficulty.expert.incomeBonusPercent","economy.earlyMinePaybackSeconds","economy.minimumIncomeToSpendRatio","economy.surplusIncomeToSpendRatio",
                "technology.minimumIncomeBuildings","technology.mixedBaseIncomeBuildings",
                "technology.rocketExplorers","technology.massAirPercent","technology.massAirMinimum","technology.massAirPerOpponent"};
            Assert.IsNotEmpty(fields);
            Assert.IsTrue(fields.All(f=>extraPaths.Contains(f.Path)||f.Path.StartsWith("openingsMidgame.",StringComparison.Ordinal)||(f.Path.StartsWith("positionDevelopment.",StringComparison.Ordinal)||f.Path.StartsWith("activeStrategy.",StringComparison.Ordinal))||f.Path.StartsWith("safeReinforcements.",StringComparison.Ordinal)||f.Path.StartsWith("victoryConversion.",StringComparison.Ordinal)||f.Path.StartsWith("turretBypass.",StringComparison.Ordinal)||f.Path.StartsWith("difficulty.",StringComparison.Ordinal)));
            foreach(var difficulty in new[]{AiDifficulty.Recruit,AiDifficulty.Fighter,AiDifficulty.Veteran})
            {
                string prefix="difficulty."+AiProfile.DifficultyId(difficulty)+".";
                foreach(string key in new[]{"reactionSeconds","control.ordersPerMinute","control.burst","control.reactionMaxSeconds","micro.intensity","micro.withdrawEnabled","micro.repairEnabled","micro.focusEnabled","micro.airFocusEnabled","micro.chaseEnabled","micro.focusGroupSize"})
                    Assert.IsTrue(fields.Any(f=>f.Path==prefix+key),prefix+key);
            }
            Assert.IsFalse(fields.Any(f=>f.Path.StartsWith("opening.weights.",StringComparison.Ordinal)));
            CollectionAssert.AreEquivalent(AiProfileMetadata.Fields.Where(f=>extraPaths.Contains(f.Path)||f.Path.StartsWith("openingsMidgame.",StringComparison.Ordinal)||f.Path.StartsWith("positionDevelopment.",StringComparison.Ordinal)||f.Path.StartsWith("activeStrategy.",StringComparison.Ordinal)||f.Path.StartsWith("safeReinforcements.",StringComparison.Ordinal)||f.Path.StartsWith("victoryConversion.",StringComparison.Ordinal)||f.Path.StartsWith("turretBypass.",StringComparison.Ordinal)||f.Path.StartsWith("difficulty.expert.",StringComparison.Ordinal)||f.Path=="difficulty.cheater.incomeBonusPercent"||f.Path.StartsWith("difficulty.",StringComparison.Ordinal)&&(f.Path.Contains(".control.")||f.Path.Contains(".micro.")||f.Path.EndsWith(".reactionSeconds",StringComparison.Ordinal))).Select(f=>f.Path).ToArray(),fields.Select(f=>f.Path).ToArray());
            Assert.AreEqual(fields.Count,fields.Select(f=>f.Path).Distinct().Count());
            var legacy=AiProfileMetadata.Fields.Single(f=>f.Path=="opening.weights.safe");var data=AiProfile.Initial.CopyData();
            Assert.DoesNotThrow(()=>NativeBalanceFields.Write(legacy,data,legacy.Initial+legacy.Step),"The existing generic AI field writer remains backward compatible.");
        }

        [TestCase("openingsMidgame.opening.aggressive")][TestCase("positionDevelopment.towerValue")][TestCase("activeStrategy.developmentSeconds")][TestCase("safeReinforcements.coreRadius")]
        [TestCase("economy.minimumIncomeToSpendRatio")]
        [TestCase("victoryConversion.accumulationSeconds")]
        public void SavedAiProfileSurvivesReloadAndReachesNewOrdinaryLobbyMatch(string fieldPath)
        {
            Directory.CreateDirectory(directory);var path=Path.Combine(directory,"profiles.json");var store=MakeStore(path);
            var gameplay=store.Selected.CopyData();var audio=store.SelectedAudio;var ai=store.SelectedAiProfile.CopyData();
            var opening=NativeBalanceFields.AiOpeningFields.Single(f=>f.Path==fieldPath);
            NativeBalanceFields.Write(opening,ai,opening.Initial+opening.Step);
            var controls=NativeBalanceFields.AiOpeningFields.Where(f=>f.Path.EndsWith(".control.ordersPerMinute",StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(4,controls.Length);
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
                Assert.AreEqual(reloaded.SelectedId,runtime.NativeAiProfile.Id);
                Assert.AreEqual(reloaded.Active.Revision,runtime.NativeAiProfile.Revision);
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
        [TestCase("victoryConversion.earlyDefensive")]
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
