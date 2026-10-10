using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Runtime;
namespace Spacewars.Tests.PlayMode
{
    public sealed class NativeHeadlessTests
    {
        private static MatchManifest Manifest(string fixture="obstacle-v1")
        {
            var identity=HeadlessFixtures.EngineIdentity(new string('a',40));identity.BundleHash=identity.ExecutableHash=new string('b',64);
            return HeadlessFixtures.Template(fixture,identity,"/worker","/worker/game","/repo/tools/unity.sh");
        }
        [TestCase("obstacle-v1")][TestCase("duel-coverage-v1")]
        public void AuthoredFixturesHaveBoundGeometryAndCommands(string fixture)
        {
            var m=Manifest(fixture);Wire.Validate(m,false);var c=HeadlessFixtures.Create(m);
            Assert.AreEqual(m.MapHash,HeadlessFixtures.MapHash(c));Assert.AreEqual(m.GeometryHash,HeadlessFixtures.GeometryHash(c));
            Assert.AreEqual(2,c.Roster.Count);Assert.AreEqual(2,c.Starts.Count);Assert.AreEqual(3,c.Sites.Count);
            Assert.AreEqual(fixture=="obstacle-v1"?1:6,c.ScenarioUnits.Count);
            var authority=new PlayableAuthorityTick(c,71);Assert.AreEqual(c.ScenarioUnits.Count+1,authority.Latest.Entities.Count);
        }
        [TestCase("obstacle-v1")][TestCase("duel-coverage-v1")]
        public void DiagnosticIngressUsesOrdinaryHumanProvenance(string fixture)
        {
            var m=Manifest(fixture);var authority=new PlayableAuthorityTick(HeadlessFixtures.Create(m),m.Generation);
            Assert.NotNull(authority.Latest.ActiveProfile);
            long sequence=0;
            foreach(var input in m.Commands.Where(c=>c.Tick==0))
            {
                var command=HeadlessFixtures.Command(authority,input,m.Generation,++sequence);
                Assert.AreEqual(Spacewars.Simulation.PlayableOrderOrigin.Human,command.Origin);Assert.Null(command.Source);
                Assert.AreEqual(Spacewars.Simulation.PlayableCommandStatus.Applied,authority.Apply(command).Status);
            }
        }
        [Test] public void BattleGoalsUseFreeApproachesInsteadOfOccupiedCenters()
        {
            var m=Manifest("duel-coverage-v1");var authority=new PlayableAuthorityTick(HeadlessFixtures.Create(m),m.Generation);long sequence=0;
            foreach(var input in m.Commands.Where(c=>c.Kind=="AttackMove"))
                Assert.AreEqual(Spacewars.Simulation.PlayableCommandStatus.Applied,authority.Apply(HeadlessFixtures.Command(authority,input,m.Generation,++sequence)).Status);
        }
        [Test] public void CoverageResearchInputIsReadyAndFundedBeforeTheBattleWindow()
        {
            var m=Manifest("duel-coverage-v1");var p=Spacewars.Simulation.PlayableProfile.Default;var research=m.Commands.Single(c=>c.Kind=="QueueResearch");
            Assert.AreEqual(3000,research.Tick);Assert.GreaterOrEqual(research.Tick,1800+Spacewars.Simulation.TerritoryRules.Duration(p,Spacewars.Simulation.PlayableBuildingKind.ScientificCenter)*30);
            Assert.GreaterOrEqual(p.StartingCredits+research.Tick/30d*p.HeadquartersIncomePerPeriod/p.IncomePeriodSeconds-p.FactoryCreditCost-p.ScienceCreditCost,p.TankChassisCost);
            Assert.True(m.Commands.Select(c=>c.Tick).SequenceEqual(m.Commands.Select(c=>c.Tick).OrderBy(t=>t)));Wire.Validate(m,false);
        }
        [Test] public void RosterMutationRequiresRebinding()
        {var m=Manifest();m.Roster[1].Team=7;Assert.Throws<ArgumentException>(()=>Wire.Validate(m,false));m.RosterHash=Wire.RosterDigest(m.Roster);Wire.Validate(m,false);}
        [Test] public void SeedAndGenerationDoNotChangeStaticMapBinding()
        {var m=Manifest();string map=m.MapHash;m.Seed++;m.Generation++;Assert.AreEqual(map,HeadlessFixtures.MapHash(HeadlessFixtures.Create(m)));}
        [TestCase("unknown")][TestCase("fighter")]
        public void UnsupportedFixtureAndMixedDifficultyReject(string input)
        {var m=Manifest();if(input=="unknown")m.Fixture=input;else m.Roster[0].Difficulty="veteran";m.RosterHash=Wire.RosterDigest(m.Roster);Assert.Throws<ArgumentException>(()=>Wire.Validate(m,false));}
        [Test] public void ReplayRequiresRecordedHashes()
        {var m=Manifest();Assert.Throws<ArgumentException>(()=>Wire.Validate(m,true));}
        [Test] public void NoSilentTickRateOrUnknownInputs()
        {var m=Manifest();m.TickRate=60;Assert.Throws<ArgumentException>(()=>Wire.Validate(m,false));m=Manifest();m.Commands[0].Kind="Teleport";Assert.Throws<ArgumentException>(()=>Wire.Validate(m,false));}
        [Test] public void BundleDigestBindsDataFilesAndRelativeNames()
        {
            string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"headless-hash-"+Guid.NewGuid());System.IO.Directory.CreateDirectory(root);
            try{System.IO.File.WriteAllText(root+"/binary","engine");string first=Wire.BundleHash(root);System.IO.File.WriteAllText(root+"/data","world");Assert.AreNotEqual(first,Wire.BundleHash(root));string second=Wire.BundleHash(root);System.IO.File.Move(root+"/data",root+"/other");Assert.AreNotEqual(second,Wire.BundleHash(root));}
            finally{System.IO.Directory.Delete(root,true);}
        }
    }
}
