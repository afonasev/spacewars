using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using System.IO;
using System.Xml.Serialization;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiProfileTests
    {
        private static AiProfileData Roundtrip(AiProfileData data)
        {
            var serializer=new XmlSerializer(typeof(AiProfileData));
            using(var stream=new MemoryStream()){serializer.Serialize(stream,data);stream.Position=0;return (AiProfileData)serializer.Deserialize(stream);}
        }
        private static void Set(AiProfileData data,string path,double value)=>NativeBalanceFields.Write(AiProfileMetadata.Field(path),data,value);
        [Test] public void ProfileRoundtrip()
        {
            var initial=AiProfile.Initial;var restored=new AiProfile(Roundtrip(initial.CopyData()));
            Assert.AreEqual(initial.Hash,restored.Hash);Assert.AreEqual(initial.Id,restored.Id);Assert.AreEqual(initial.Revision,restored.Revision);
            var copy=restored.CopyData();Set(copy,"difficulty.recruit.decisionSeconds",4);
            Assert.AreEqual(3,restored.DifficultyValue(AiDifficulty.Recruit,"decisionSeconds"));
            Assert.AreNotEqual(initial.Hash,new AiProfile(copy).Hash);
            Array.Reverse(copy.fields);Assert.AreEqual(new AiProfile(copy).Hash,new AiProfile(Roundtrip(copy)).Hash);
        }
        [Test] public void RejectsMissingUnknownDuplicateAndInvalidIdentity()
        {
            var data=AiProfile.Initial.CopyData();data.fields=data.fields.Skip(1).ToArray();Assert.Throws<ArgumentException>(()=>new AiProfile(data));
            data=AiProfile.Initial.CopyData();data.fields[0].path="unknown";Assert.Throws<ArgumentException>(()=>new AiProfile(data));
            data=AiProfile.Initial.CopyData();data.fields[0].path=data.fields[1].path;Assert.Throws<ArgumentException>(()=>new AiProfile(data));
            data=AiProfile.Initial.CopyData();data.revision=0;Assert.Throws<ArgumentException>(()=>new AiProfile(data));
        }
        [Test] public void MetadataControlsAllNumericRanges()
        {
            Assert.AreEqual(84,NativeBalanceFields.AiFields.Count);
            Assert.AreEqual(12,NativeBalanceFields.AiRosterFields.Count);
            foreach(var f in NativeBalanceFields.AiFields.Concat(NativeBalanceFields.AiRosterFields))
            {
                Assert.IsNotEmpty(f.Path);Assert.IsNotEmpty(f.Group);Assert.IsNotEmpty(f.Label);Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Greater(f.Step,0);
                f.Validate(f.Initial);f.Validate(f.Minimum);f.Validate(f.Maximum);
                Assert.Throws<ArgumentOutOfRangeException>(()=>f.Validate(f.Maximum+f.Step));
                Assert.Throws<ArgumentOutOfRangeException>(()=>f.Validate(double.NaN));
                Assert.Throws<ArgumentOutOfRangeException>(()=>f.Validate(double.PositiveInfinity));
            }
            Assert.Throws<ArgumentOutOfRangeException>(()=>Set(AiProfile.Initial.CopyData(),"difficulty.veteran.actionsPerDecision",2.5));
            Assert.Throws<ArgumentOutOfRangeException>(()=>Set(AiProfile.Initial.CopyData(),"difficulty.fighter.majorArmies",4));
        }
        [Test] public void RejectsGroupViolationsInsteadOfNormalizing()
        {
            var data=AiProfile.Initial.CopyData();Set(data,"difficulty.recruit.decisionSeconds",1);Assert.Throws<ArgumentException>(()=>new AiProfile(data));
            data=AiProfile.Initial.CopyData();Set(data,"difficulty.recruit.actionsPerDecision",5);Assert.Throws<ArgumentException>(()=>new AiProfile(data));
            data=AiProfile.Initial.CopyData();foreach(var role in new[]{"recon","line","support"})Set(data,"composition.early."+role+"BudgetShare",0);Assert.Throws<ArgumentException>(()=>new AiProfile(data));
            data=AiProfile.Initial.CopyData();Set(data,"utility.scout.informationGain",0);Assert.Throws<ArgumentException>(()=>new AiProfile(data));
            data=AiProfile.Initial.CopyData();foreach(var role in new[]{"threat","economicValue","reachability","opportunity"})Set(data,"utility.target."+role,0);Assert.Throws<ArgumentException>(()=>new AiProfile(data));
        }
        [Test] public void TimeConversionRoundsUpAndPreservesOrdering()
        {
            Assert.AreEqual(1,AiProfile.SecondsToTicks(.001,30));Assert.AreEqual(8,AiProfile.SecondsToTicks(.25,30));Assert.AreEqual(0,AiProfile.SecondsToTicks(0,30));
            Assert.Throws<ArgumentOutOfRangeException>(()=>AiProfile.SecondsToTicks(1,0));
            var p=AiProfile.Initial;
            CollectionAssert.AreEqual(new long[]{90,45,15},Enum.GetValues(typeof(AiDifficulty)).Cast<AiDifficulty>().Select(d=>AiProfile.SecondsToTicks(p.DifficultyValue(d,"decisionSeconds"),30)));
            CollectionAssert.AreEqual(new long[]{60,30,9},Enum.GetValues(typeof(AiDifficulty)).Cast<AiDifficulty>().Select(d=>AiProfile.SecondsToTicks(p.DifficultyValue(d,"reactionSeconds"),30)));
        }
        [Test] public void CatalogCoversEveryKindAndReadsGameplayPrices()
        {
            var catalog=AiRosterCatalog.Initial;var data=catalog.CopyData();
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(PlayableEntityKind)),data.Select(d=>d.kind));
            Assert.AreEqual(catalog.Hash,new AiRosterCatalog(data.Reverse()).Hash);
            foreach(var d in data){Assert.AreEqual(PlayableBuildingKind.Factory,d.producer);Assert.AreEqual(AiRosterCatalog.Command(d.kind),d.productionCommand);}
            var gameplay=PlayableProfile.Default;Assert.AreEqual(gameplay.TankCreditCost,catalog.CreditCost(PlayableEntityKind.Tank,gameplay));
            var f=NativeBalanceFields.AiRosterFields.Single(x=>x.Path=="roster.Tank.lineWeight");NativeBalanceFields.Write(f,data,.5);
            Assert.AreNotEqual(catalog.Hash,new AiRosterCatalog(data).Hash);Assert.AreEqual(1,catalog.For(PlayableEntityKind.Tank).lineWeight);
        }
        [Test] public void CatalogRejectsMissingRolesAndAdapters()
        {
            var data=AiRosterCatalog.Initial.CopyData();Assert.Throws<ArgumentException>(()=>new AiRosterCatalog(data.Skip(1)));
            data[0].productionCommand=PlayableCommandKind.QueueResearch;Assert.Throws<ArgumentException>(()=>new AiRosterCatalog(data));
            data=AiRosterCatalog.Initial.CopyData();data[0].reconWeight=data[0].lineWeight=data[0].supportWeight=0;Assert.Throws<ArgumentException>(()=>new AiRosterCatalog(data));
            data[0].exclusionReason="Diagnostic documented exclusion";Assert.DoesNotThrow(()=>new AiRosterCatalog(data));
            data[1].kind=(PlayableEntityKind)500;Assert.Throws<ArgumentException>(()=>new AiRosterCatalog(data));
        }
        [Test] public void SeedBinding()
        {
            var seeds=Enumerable.Range(0,100).Select(_=>AiMatchSeed.Resolve()).ToArray();Assert.AreEqual(100,seeds.Distinct().Count());
            Assert.AreEqual(-7,AiMatchSeed.Resolve(-7));Assert.AreEqual(-7,AiMatchSeed.Resolve(-7));
            var p=AiProfile.Initial;var a=new AiOwnerConfig("owner-a",AiDifficulty.Fighter,41,p);var b=new AiOwnerConfig("owner-a",AiDifficulty.Fighter,41,p);
            Assert.AreEqual(a.PersonalitySeed,b.PersonalitySeed);Assert.AreNotEqual(a.PersonalitySeed,new AiOwnerConfig("owner-z",AiDifficulty.Fighter,41,p).PersonalitySeed);
            var data=p.CopyData();data.revision++;Assert.AreNotEqual(a.PersonalitySeed,new AiOwnerConfig("owner-a",AiDifficulty.Fighter,41,new AiProfile(data)).PersonalitySeed);
            var key=AiRandom.Key(41,"owner-a",p.Hash,"scout",10);
            AiRandom.Key(41,"owner-a",p.Hash,"unselected",99);Assert.AreEqual(key,AiRandom.Key(41,"owner-a",p.Hash,"scout",10));
            Assert.AreNotEqual(key,AiRandom.Key(41,"owner-a",p.Hash,"scout",11));
        }
        [TestCase(false)][TestCase(true)] public void ActualOpeningAndRuntimeBindNativeRevisionAndHash(bool fieldChange)
        {
            var initial=AiProfile.Initial;var data=initial.CopyData();
            if(fieldChange)Set(data,"decision.intentAgingSeconds",initial.Value("decision.intentAgingSeconds")+1);else data.revision++;
            var revised=new AiProfile(data);int seed=4103;
            var old=PlayableAiOpeningComposition.Initialize(seed,"enemy-1",aiProfile:initial);
            var next=PlayableAiOpeningComposition.Initialize(seed,"enemy-1",aiProfile:revised);
            Assert.AreNotEqual(old.PersonalitySeed,next.PersonalitySeed);Assert.AreNotEqual(old.ProfileIdentity,next.ProfileIdentity);
            Assert.AreEqual(new AiOwnerConfig("enemy-1",AiDifficulty.Veteran,seed,revised).PersonalitySeed,next.PersonalitySeed);
            Assert.True(next.SemanticallyEquals(PlayableAiOpeningComposition.Initialize(seed,"enemy-1",aiProfile:revised)));
            Assert.True(PlayableAiOpeningComposition.HasNativeBinding(next));Assert.False(next.SourceIdentity.StartsWith("source:"));
            var runtime=new PlayableRuntime(PlayableProfile.Default,71,seed,startPaused:true,aiProfile:revised,aiDifficulty:AiDifficulty.Veteran);
            try
            {
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Paused,5000));
                Assert.AreEqual(PlayableAiOpeningComposition.ProfileBinding(revised),runtime.OpeningComposition.ProfileIdentity);
                var enemy=(PlayableAiOwnerCheckpoint)typeof(PlayableRuntime).GetProperty("EnemyAiCheckpoint",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
                Assert.True(next.SemanticallyEquals(enemy.Opening));Assert.AreEqual(revised.Hash,enemy.AiHash);
                var capture=runtime.RequestCaptureBytes();Assert.True(capture.Wait(5000));
                using(var restored=PlayableRuntime.RestoreBytes(capture.Result,PlayableProfile.Default,seed,aiProfile:revised))
                {Assert.True(SpinWait.SpinUntil(()=>restored.Latest.Paused,5000));Assert.AreEqual(runtime.OpeningComposition.PersonalitySeed,restored.OpeningComposition.PersonalitySeed);restored.RequestStop();Assert.True(SpinWait.SpinUntil(()=>restored.IsStopped,5000));}
                Assert.Throws<ArgumentException>(()=>PlayableRuntime.RestoreBytes(capture.Result,PlayableProfile.Default,seed,aiProfile:initial));
            }
            finally{runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,5000));runtime.Dispose();}
            var config=OfflineParticipantAuthorityTests.Config(3,false,seed:seed);
            var costs=new double[config.Starts.Count,config.Starts.Count];for(int i=0;i<config.Starts.Count;i++)for(int j=0;j<config.Starts.Count;j++)costs[i,j]=config.RouteCost(i,j);
            config=new OfflineMatchConfiguration(config.Profile,config.SourceIdentity,config.MapIdentity,config.RouteProvenance,seed,config.Roster.Select(p=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,OfflineControl.Ai)).ToArray(),config.Starts.ToArray(),config.Sites.ToArray(),config.Obstacles.ToArray(),costs);
            var tick=new PlayableAuthorityTick(config,71,revised,AiDifficulty.Veteran);
            foreach(var owner in tick.CaptureDiagnosticCheckpoints())
            {Assert.AreEqual(PlayableAiOpeningComposition.ProfileBinding(revised),owner.Opening.ProfileIdentity);Assert.AreEqual(new AiOwnerConfig(owner.OwnerId,AiDifficulty.Veteran,seed,revised).PersonalitySeed,owner.Opening.PersonalitySeed);}
            CollectionAssert.AreEqual(tick.CaptureBytes(),PlayableAuthorityTick.RestoreBytes(tick.CaptureBytes(),config,revised).CaptureBytes());
        }
        [Test] public void IdentityBindsSeparateRevisionsAndTeam()
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;var catalog=AiRosterCatalog.Initial;
            var identity=new AiMatchIdentity("code-sha","map-id","map-hash",p,ai,catalog,41,"owner-a","team-blue",AiDifficulty.Veteran,2);
            Assert.AreEqual(p.Revision,identity.GameplayRevision);Assert.AreEqual(ai.Revision,identity.AiRevision);Assert.AreEqual(catalog.Hash,identity.RosterHash);Assert.AreEqual("team-blue",identity.TeamId);
            var changed=ai.CopyData();changed.revision++;var next=new AiMatchIdentity("code-sha","map-id","map-hash",p,new AiProfile(changed),catalog,41,"owner-a","team-blue",AiDifficulty.Veteran,2);
            Assert.AreEqual(identity.GameplayHash,next.GameplayHash);Assert.AreNotEqual(identity.AiHash,next.AiHash);
            var gameplayData=p.CopyData();gameplayData.followDistance+=1e-15;
            var gameplayNext=PlayableProfile.Create(gameplayData);
            Assert.AreNotEqual(AiMatchIdentity.GameplayDigest(p),AiMatchIdentity.GameplayDigest(gameplayNext));
            Assert.Throws<ArgumentException>(()=>new AiOwnerConfig("owner-a",(AiDifficulty)99,41,ai));
        }
        [TestCase(AiDifficulty.Recruit,90,60)] [TestCase(AiDifficulty.Fighter,45,30)] [TestCase(AiDifficulty.Veteran,15,9)]
        public void LobbyDifficultyDrivesRuntimeCadence(AiDifficulty difficulty,int decision,int delay)
        {
            var setup=new NativeLobbyConfiguration{Difficulty=difficulty,HasExplicitSeed=true,ExplicitSeed=41};
            using(var runtime=PlayableRuntime.CreateLobbyMatch(PlayableProfile.ThreeCrossingsDefault,7,setup,startPaused:true))
            {
                setup.Difficulty=AiDifficulty.Recruit;setup.ExplicitSeed=99;
                Assert.AreEqual(41,runtime.Latest.Seed);Assert.AreEqual(difficulty,runtime.EnemyAiConfig.Difficulty);Assert.AreEqual(difficulty,runtime.LobbyConfiguration.Difficulty);
                Assert.AreEqual(AiProfile.Initial.Hash,runtime.NativeAiProfile.Hash);
                runtime.RequestPause(false);
                Func<PlayableAiOwnerCheckpoint> checkpoint=()=> (PlayableAiOwnerCheckpoint)typeof(PlayableRuntime).GetProperty("EnemyAiCheckpoint",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
                Assert.True(SpinWait.SpinUntil(()=>checkpoint().PendingActionId!=0,7000));
                var pending=checkpoint();Assert.AreEqual(decision,pending.DecisionTick);Assert.AreEqual(delay,pending.PendingDueTick-pending.DecisionTick);
                Assert.AreEqual(runtime.NativeAiProfile.Id,pending.AiProfileId);Assert.AreEqual(runtime.NativeAiProfile.Hash,pending.AiHash);Assert.AreEqual(runtime.EnemyAiConfig.PersonalitySeed,pending.PersonalitySeed);
            }
        }
        [Test] public void OrdinaryLobbyLaunchGeneratesSeedForEachMatch()
        {
            var setup=new NativeLobbyConfiguration();int first;
            using(var a=PlayableRuntime.CreateLobbyMatch(PlayableProfile.ThreeCrossingsDefault,8,setup,startPaused:true))first=a.Latest.Seed;
            using(var b=PlayableRuntime.CreateLobbyMatch(PlayableProfile.ThreeCrossingsDefault,9,setup,startPaused:true))Assert.AreNotEqual(first,b.Latest.Seed);
            Assert.IsFalse(setup.HasExplicitSeed);
        }
    }
}
