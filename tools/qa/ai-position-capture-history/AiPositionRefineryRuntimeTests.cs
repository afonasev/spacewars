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
        // A focused authored midgame-input witness on the existing A4 geography.
        // It does not replace the frozen seed7/outpost ordinary FAIL or qualify Nav.
        [Test,Timeout(120000)]
        public void PositionRefinery_RealRuntimePaysCompletesAndRoundtrips()
        {
            var basis=A4NativeConfig();var p=basis.Profile;var ai=AiProfile.Initial;
            var added=new[]{new TerritorySlot(3,new NavPoint(-24,9),0),new TerritorySlot(4,new NavPoint(-14,9),0),new TerritorySlot(5,new NavPoint(-14,-9),0)};
            var sites=basis.Sites.Select(s=>s.Id==1?new TerritorySite(s.Id,s.Kind,s.Position,s.Slots.Concat(added).ToArray()):s).ToArray();
            var buildings=basis.ScenarioBuildings.Concat(new[]{
                new OfflineScenarioBuilding(1,PlayableBuildingKind.ScientificCenter,1,3,added[0].Position),
                new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,1,4,added[1].Position),
                new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,1,5,added[2].Position)}).ToArray();
            var config=new OfflineMatchConfiguration(p,"ai-position-refinery-runtime-v1",basis.MapIdentity,basis.RouteProvenance,basis.Seed,
                basis.Roster.ToArray(),basis.Starts.ToArray(),sites,basis.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},
                spectators:new[]{PlayableRuntime.LocalSpectatorId},scenario:basis.ScenarioUnits.ToArray(),scenarioBuildings:buildings);
            Assert.AreEqual(PlayableAiOpening.Safe,PlayableAiOpeningComposition.Initialize(config.Seed,"player-1",aiProfile:ai).Opening);
            string source=Environment.GetEnvironmentVariable("AI_POSITION_SOURCE_SHA");Assert.IsNotEmpty(source,"recorded candidate required");
            var output=Path.GetFullPath(Path.Combine(".local/ai-position-ready","refinery-"+source+"-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(output);
            var runtime=new PlayableRuntime(config,107,aiDifficulty:AiDifficulty.Veteran,spectator:true,aiProfile:ai);
            var routes=new UnityHostRouteService();bool released=false,active=false,completed=false;int refineryId=0;byte[] input=null,roundtrip=null;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void StopOriginal()
            {
                if(released)return;
                var worker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",flags).GetValue(routes);
                runtime.RequestStop();routes.Dispose();runtime.Dispose();
                Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000),"detach ACK before owned navigation destruction");
                var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(runtime);
                authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();released=true;
            }
            try
            {
                var initial=runtime.ParticipantView("player-1");var ids=initial.Buildings.Where(b=>b.Owner==initial.Owner&&b.Kind==PlayableBuildingKind.Refinery).Select(b=>b.Id).ToArray();
                Assert.AreEqual(3,ids.Length);Assert.True(initial.Buildings.Where(b=>ids.Contains(b.Id)).All(b=>!b.RefineryUpgraded));
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);var clock=Stopwatch.StartNew();
                while(runtime.Latest.Tick<1800&&clock.Elapsed.TotalSeconds<90&&!completed)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);var view=runtime.ParticipantView("player-1");
                    Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);Assert.GreaterOrEqual(view.Credits,0);
                    active|=view.Buildings.Any(b=>ids.Contains(b.Id)&&b.PrivateState?.Upgrade?.Active==true);
                    var upgraded=view.Buildings.FirstOrDefault(b=>ids.Contains(b.Id)&&b.RefineryUpgraded);
                    if(upgraded!=null){completed=true;refineryId=upgraded.Id;}Thread.Sleep(1);
                }
                var request=runtime.RequestCaptureBytes();var captureClock=Stopwatch.StartNew();
                while(!request.IsCompleted&&captureClock.Elapsed.TotalSeconds<10){routes.Service(runtime,p.NavigationRequestsPerFrame);Thread.Sleep(1);}
                Assert.True(request.IsCompleted,"bounded current runtime capture");Assert.False(request.IsFaulted,request.Exception?.ToString());input=request.Result;
                File.WriteAllBytes(Path.Combine(output,"current.bin"),input);StopOriginal();
                PlayableAiOwnerCheckpoint checkpoint=null;AiBudgetEntry paid=null;Exception restoreError=null;PlayableAuthorityTick restoredAuthority=null;PlayableRuntime restored=null;
                try
                {
                    restored=PlayableRuntime.RestoreBytesForTests(input,config,ai,r=>
                    {
                        try
                        {
                            restoredAuthority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(r);
                            checkpoint=restoredAuthority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="player-1");
                            var owner=Owners(restoredAuthority).Single();var ledger=(AiBudgetLedger)Field(owner,"budget");
                            paid=ledger.Capture().Paid.SingleOrDefault(e=>e.Action.Kind==PlayableCommandKind.UpgradeRefinery&&e.Action.EntityIds.Contains(refineryId));
                            roundtrip=r.CaptureBeforeStartForTests();
                        }
                        catch(Exception error){restoreError=error;}
                        finally{r.RequestStop();}
                    });
                    Assert.True(SpinWait.SpinUntil(()=>restored.IsStopped,3000));if(restoreError!=null)Assert.Fail(restoreError.ToString());
                    bool applied=paid!=null&&paid.Amount==(int)Math.Ceiling(p.RefineryUpgradeCost)&&checkpoint.Records.Any(r=>
                        r.Policy=="economy"&&r.Kind==PlayableCommandKind.UpgradeRefinery&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&
                        r.ReceiptIdentity?.Id==paid.Receipt?.Id&&r.ReceiptIdentity.OwnerId=="player-1"&&r.ReceiptIdentity.Generation==107);
                    bool exact=input.SequenceEqual(roundtrip);
                    var result=new{source,config.Seed,generation=107,ai.Id,ai.Hash,active,completed,refineryId,applied,exact,paid,checkpoint.OpeningExecution,checkpoint.ArmyMission,
                        limitTicks=1800,wallLimitSeconds=90,wallSeconds=clock.Elapsed.TotalSeconds,records=checkpoint.Records};
                    File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
                    TestContext.WriteLine("POSITION_REFINERY_RUNTIME "+Newtonsoft.Json.JsonConvert.SerializeObject(result));
                    Assert.True(active,"AI upgrade must enter actual active work");Assert.True(completed,"ordinary work must complete at unchanged duration");
                    Assert.True(applied,"exact AI economy receipt and full real upgrade payment");Assert.True(exact,"current full runtime including transport remains exact");
                }
                finally
                {
                    if(restored!=null){restored.RequestStop();Assert.True(SpinWait.SpinUntil(()=>restored.IsStopped,3000));restored.Dispose();}
                    if(restoredAuthority!=null){restoredAuthority.Domain.Navigation.ReleaseCommittedHostRoot();restoredAuthority.Domain.DestroyOwnedNavigation();}
                }
            }
            finally{StopOriginal();}
        }
    }
}
