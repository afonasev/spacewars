using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiArmyTests
    {
        [Test,Timeout(60000)]
        public void PositionRepair_ExistingFailedCaptureReadback()
        {
            var config=PositionRepairReadbackConfiguration();var ai=AiProfile.Initial;

            var path=Environment.GetEnvironmentVariable("AI_REPAIR_CAPTURE");Assert.IsNotEmpty(path);var input=File.ReadAllBytes(path);
            var expected=Environment.GetEnvironmentVariable("AI_REPAIR_CAPTURE_SHA256");Assert.IsNotEmpty(expected);
            using(var sha=System.Security.Cryptography.SHA256.Create())Assert.AreEqual(expected,BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant());
            PlayableRuntime runtime=null;PlayableAuthorityTick authority=null;Exception failure=null;const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            try
            {
                runtime=PlayableRuntime.RestoreBytesForTests(input,config,ai,r=>
                {
                    try
                    {
                        authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(r);
                        var own=r.ParticipantView("player-1");var enemy=r.ParticipantView("enemy-1");var cp=authority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="player-1");
                        var result=new{own.Tick,generation=r.Generation,config.Seed,ai.Id,ai.Hash,exact=input.SequenceEqual(r.CaptureBeforeStartForTests()),own,enemy,checkpoint=cp};
                        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),"readback.json"),Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));Assert.True(result.exact);
                    }
                    catch(Exception e){failure=e;}finally{r.RequestStop();}
                });
                Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));if(failure!=null)Assert.Fail(failure.ToString());
            }
            finally{if(runtime!=null){runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));runtime.Dispose();}if(authority!=null){authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();}}
        }

        [TestCase("provider")][TestCase("profile")][TestCase("grid")]
        public void PositionRepair_CurrentCaptureTamperingIsRejected(string defect)
        {
            var config=PositionRepairReadbackConfiguration();var ai=AiProfile.Initial;
            byte[] input=File.ReadAllBytes(Environment.GetEnvironmentVariable("AI_REPAIR_CAPTURE"));
            using var state=PlayableWorldState.Decode(input);
            using var nav=WorldWire.Unpack(state.Navigation,WorldWire.ReadNavigationSessionState);
            string key=defect=="provider"?"8573cdd39db18b66fccc926c17a231f6ff171664ba7d69f218dc32779c167bda":"1b234b7ab95010fc8be4f9747dcf93f236e4b09a6f9c79e0aedc1477103723b5";
            byte[] marker=System.Text.Encoding.UTF8.GetBytes(key);var bytes=nav.ArrivalAuthority;
            int at=-1;for(int n=4;n+marker.Length<=bytes.Length;n++)if(BitConverter.ToInt32(bytes,n-4)==marker.Length&&marker.Select((v,i)=>bytes[n+i]==v).All(v=>v)){at=n;break;}
            Assert.GreaterOrEqual(at,4,"exact ARR primary binding field must be present");
            if(defect=="grid"){Assert.AreEqual(.8,BitConverter.ToDouble(bytes,at+marker.Length));Array.Copy(BitConverter.GetBytes(1.5),0,bytes,at+marker.Length,8);}
            else bytes[at]=(byte)(bytes[at]=='0'?'1':'0');
            state.Navigation=WorldWire.Pack(w=>WorldWire.Write(w,nav));var bad=state.Encode();
            var error=Assert.Throws<ArgumentException>(()=>PlayableRuntime.RestoreBytesForTests(bad,config,ai,r=>Assert.Fail("invalid binding cannot start runtime")));
            StringAssert.StartsWith("Invalid saved arrival binding.",error.Message,"recomputed section/envelope digests must reach the semantic binding guard");
            CollectionAssert.AreEqual(input,File.ReadAllBytes(Environment.GetEnvironmentVariable("AI_REPAIR_CAPTURE")));
        }
        private static OfflineMatchConfiguration PositionRepairReadbackConfiguration()
        {
            var basis=A4NativeConfig();var p=basis.Profile;var ai=AiProfile.Initial;
            var added=new[]{new TerritorySlot(3,new NavPoint(-24,9),0),new TerritorySlot(4,new NavPoint(-14,9),0),new TerritorySlot(5,new NavPoint(-14,-9),0)};
            var sites=basis.Sites.Select(s=>s.Id==1?new TerritorySite(s.Id,s.Kind,s.Position,s.Slots.Concat(added).ToArray()):s).ToArray();
            var victimAt=new NavPoint(1,-8);double repairRange=AiPositionDevelopmentPlanner.Range(p,PlayableBuildingKind.RepairTower);
            var pad=sites.Single(s=>s.Id==1).DefenseSlots(p).Where(s=>AiPositionDevelopmentPlanner.Distance(s.Position,victimAt)>repairRange+PlayableUnitRules.Radius(p,PlayableEntityKind.Tank))
                .OrderBy(s=>AiPositionDevelopmentPlanner.Distance(s.Position,victimAt)).ThenBy(s=>s.Id).First();
            var buildings=basis.ScenarioBuildings.Concat(new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.ScientificCenter,1,3,added[0].Position),
                new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,1,4,added[1].Position),new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,1,5,added[2].Position),
                new OfflineScenarioBuilding(1,PlayableBuildingKind.RepairTower,1,pad.Id,pad.Position,pad.Heading)}).ToArray();
            var units=basis.ScenarioUnits.Concat(new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-1,-8)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,victimAt),
                new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(1,-2))}).ToArray();
            return new OfflineMatchConfiguration(p,"ai-position-repair-runtime-v1",basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),sites,basis.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},
                spectators:new[]{PlayableRuntime.LocalSpectatorId},scenario:units,scenarioBuildings:buildings);
        }

    }
}
