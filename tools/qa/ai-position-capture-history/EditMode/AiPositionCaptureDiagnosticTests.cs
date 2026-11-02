using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Security.Cryptography;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiPositionCaptureDiagnosticTests
    {
        const string Producer="3e7e2a89b516e987b59636cf5c3b5124e4875f4c";
        const string CaptureSha="8b03cb9013fb50abf9f2a6cbc08e7612fe6b576938b7dff25e925482774acfe4";
        static string Hash(byte[] bytes){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        [Test,Timeout(180000)] public void ExistingCaptureSeparatesRestoreCaptureAndExactComparison()
        {
            string input=Environment.GetEnvironmentVariable("AI_POSITION_CAPTURE");Assert.IsNotEmpty(input);
            var output=Path.Combine(Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(input))).FullName,"existing-capture-"+Producer+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
            var clock=Stopwatch.StartNew();
            void Mark(string phase,object details=null)=>File.AppendAllText(Path.Combine(output,"phases.jsonl"),Newtonsoft.Json.JsonConvert.SerializeObject(new{phase,elapsedSeconds=clock.Elapsed.TotalSeconds,utc=DateTime.UtcNow.ToString("O"),producer=Producer,captureSha=CaptureSha,details})+"\n");
            Mark("read-before",new{input});var bytes=File.ReadAllBytes(input);Assert.AreEqual(CaptureSha,Hash(bytes));Mark("read-after",new{bytes=bytes.Length});
            // Exact configuration statements from the fixed ordinary witness.
            // setup-sha256 3484ffe7e2381491d763ffe0793738c92a0e3b412b0d71e72e7f7b5ee7494043
            const int seed=7;const long generation=107;
            var p=PlayableProfile.Default;var ai=NativeBalanceFields.InitialAiOpeningProfile();
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,-15),new NavPoint(-20,-27),pin:1),new OfflineStart("east",2,new NavPoint(20,15),new NavPoint(20,27),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);
            var enemy=new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p);
            var front=new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(-20,15),p);
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter};
            Assert.GreaterOrEqual(home.Slots.Count,kinds.Length);
            var buildings=kinds.Select((kind,i)=>new OfflineScenarioBuilding(1,kind,1,home.Slots[i].Id,home.Slots[i].Position,home.Slots[i].Heading)).Concat(new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Outpost,3,0,front.Position)}).ToArray();
            var units=Enumerable.Range(0,5).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-25+i*3,-1))).Concat(new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(20,-24))}).ToArray();
            var config=new OfflineMatchConfiguration(p,"ai-position-development-first-witness-v1","flat-public-position-v1","UnityHostRouteService",seed,new[]{new OfflineParticipant("position-owner",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},starts,new[]{home,enemy,front},PlayableMap.StaticObstacles(p),new double[,]{{0,100},{100,0}},scenario:units,scenarioBuildings:buildings,spectators:new[]{PlayableRuntime.LocalSpectatorId});

            using(var state=PlayableWorldState.Decode(bytes)){Assert.AreEqual(22,state.Version);Assert.AreEqual(seed,state.Seed);Assert.AreEqual(generation,state.Generation);Assert.AreEqual(config.SourceIdentity,state.SourceIdentity);Mark("identity",new{state.Version,state.Seed,state.Generation,state.Tick,state.SourceIdentity,p.ProfileId,p.Revision,ai.Id,ai.Hash,config.MapIdentity,config.RouteProvenance});}
            PlayableAuthorityTick authority=null;byte[] actual=null;
            try
            {
                Mark("restore-before");authority=PlayableAuthorityTick.RestoreBytes(bytes,config,ai);Mark("restore-after");
                Mark("capture-before");actual=authority.CaptureBytes();Mark("capture-after",new{bytes=actual.Length});
            }
            finally{Mark("cleanup-before");if(authority!=null){authority.Stop();authority.Domain.Navigation.DestroyNavigationAuthority();}Mark("cleanup-after");}
            Mark("sequence-original-before");bool exact=bytes.SequenceEqual(actual);Mark("sequence-original-after",new{exact,expectedLength=bytes.Length,actualLength=actual.Length});
            using(var before=PlayableWorldState.Decode(bytes))using(var after=PlayableWorldState.Decode(actual))
                Mark("section-equality",new{binding=before.Binding.SequenceEqual(after.Binding),domain=before.Domain.SequenceEqual(after.Domain),navigation=before.Navigation.SequenceEqual(after.Navigation),participants=before.Participants.SequenceEqual(after.Participants),ai=before.AiAuthority.SequenceEqual(after.AiAuthority),transport=before.RuntimeTransport.SequenceEqual(after.RuntimeTransport),beforeTransport=before.RuntimeTransport.Length,afterTransport=after.RuntimeTransport.Length});
            var same=actual.ToArray();var corrupt=actual.ToArray();corrupt[corrupt.Length-1]^=1;var shorter=actual.Take(actual.Length-1).ToArray();
            Mark("sequence-controls-before");Assert.True(actual.SequenceEqual(same));Assert.False(actual.SequenceEqual(corrupt));Assert.False(actual.SequenceEqual(shorter));Mark("sequence-controls-after");
            Mark("nunit-constraint-same-before");Assert.True(new EqualConstraint(actual).ApplyTo(same).IsSuccess);Mark("nunit-constraint-same-after");
            Mark("nunit-constraint-corrupt-before");Assert.False(new EqualConstraint(actual).ApplyTo(corrupt).IsSuccess);Mark("nunit-constraint-corrupt-after");
            Mark("nunit-constraint-length-before");Assert.False(new EqualConstraint(actual).ApplyTo(shorter).IsSuccess);Mark("nunit-constraint-length-after");
            Mark("collection-same-before");CollectionAssert.AreEqual(actual,same);Mark("collection-same-after");
            Mark("collection-original-before",new{exact});
            bool rejected=false;
            try{CollectionAssert.AreEqual(bytes,actual);}
            catch(AssertionException error){rejected=true;Mark("collection-original-rejection",new{type=error.GetType().FullName,messageLength=error.Message.Length});}
            Mark("collection-original-after",new{rejected});Assert.AreEqual(!exact,rejected,"same exact-byte acceptance for original and restored captures");
        }
    }
}
