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
        private static OfflineMatchConfiguration PositionRefineryCaptureConfig()
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
            return config;

        }
        // Reads the authenticated failed capture before starting any authority tick.
        [Test,Timeout(60000)]
        public void PositionRefinery_ExistingCaptureBindsPaymentAndProgressExactly()
        {
            var config=PositionRefineryCaptureConfig();var p=config.Profile;var ai=AiProfile.Initial;
            var path=Environment.GetEnvironmentVariable("AI_REFINERY_CAPTURE");Assert.IsNotEmpty(path);
            var input=File.ReadAllBytes(path);
            using(var sha=System.Security.Cryptography.SHA256.Create())
                Assert.AreEqual("1adc4a8ed7c1061277b508baf43040fedf16062222e4adbdca1e30e2895689e8",BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant());
            PlayableRuntime runtime=null;PlayableAuthorityTick authority=null;Exception failure=null;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            try
            {
                runtime=PlayableRuntime.RestoreBytesForTests(input,config,ai,r=>
                {
                    try
                    {
                        authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(r);
                        var view=r.ParticipantView("player-1");
                        var building=view.Buildings.Single(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Refinery&&b.PrivateState?.Upgrade?.Active==true);
                        var upgrade=building.PrivateState.Upgrade;
                        var checkpoint=authority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="player-1");
                        var ledger=((AiBudgetLedger)Field(Owners(authority).Single(),"budget")).Capture();
                        var paid=ledger.Paid.Single(e=>e.Action.Kind==PlayableCommandKind.UpgradeRefinery&&e.Action.EntityIds.Contains(building.Id));
                        var receipt=checkpoint.Records.Single(t=>t.Policy=="economy"&&t.Kind==PlayableCommandKind.UpgradeRefinery&&t.Status==PlayableAiDeliveryStatus.Applied&&t.RuntimeStatus==PlayableCommandStatus.Applied&&t.ReceiptIdentity?.Id==paid.Receipt?.Id);
                        bool exact=input.SequenceEqual(r.CaptureBeforeStartForTests());
                        var result=new{producer="867f791a05399950e00d054a342e0f7ee086d0f9",config.Seed,generation=r.Generation,tick=r.Latest.Tick,ai.Id,ai.Hash,buildingId=building.Id,upgrade,paid,receipt,exact};
                        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),"readback.json"),Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
                        Assert.AreEqual(107,r.Generation);Assert.AreEqual(2,config.Seed);
                        Assert.AreEqual((int)Math.Ceiling(p.RefineryUpgradeCost),paid.Amount);Assert.AreEqual(p.RefineryUpgradeCost,upgrade.PaidCost);
                        Assert.AreEqual(p.RefineryUpgradeSeconds,upgrade.Duration);Assert.Greater(upgrade.Elapsed,0);Assert.Less(upgrade.Elapsed,upgrade.Duration);
                        Assert.AreEqual("player-1",paid.Receipt.OwnerId);Assert.AreEqual(107,paid.Receipt.Generation);Assert.True(exact);
                    }
                    catch(Exception error){failure=error;}
                    finally{r.RequestStop();}
                });
                Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));if(failure!=null)Assert.Fail(failure.ToString());
            }
            finally
            {
                if(runtime!=null){runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));runtime.Dispose();}
                if(authority!=null){authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();}
            }
        }
        // A distinct saved-work completion witness. The original 1800-tick FAIL is retained.
        [Test,Timeout(90000)]
        public void PositionRefinery_SavedPaidWorkCompletesThroughRealThreadedHost()
        {
            var config=PositionRefineryCaptureConfig();var p=config.Profile;var ai=AiProfile.Initial;
            var path=Environment.GetEnvironmentVariable("AI_REFINERY_CAPTURE");Assert.IsNotEmpty(path);var input=File.ReadAllBytes(path);
            using(var sha=System.Security.Cryptography.SHA256.Create())
                Assert.AreEqual("1adc4a8ed7c1061277b508baf43040fedf16062222e4adbdca1e30e2895689e8",BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant());
            var output=Path.Combine(Path.GetDirectoryName(path),"continuation");Directory.CreateDirectory(output);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            PlayableRuntime runtime=null;PlayableAuthorityTick authority=null;var routes=new UnityHostRouteService();bool released=false;
            long start=0;int id=0;double elapsed=0;AiBudgetEntry paid=null;Exception failure=null;bool initialExact=false;
            void Release()
            {
                if(released)return;var worker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",flags).GetValue(routes);
                runtime?.RequestStop();routes.Dispose();runtime?.Dispose();
                Assert.True(SpinWait.SpinUntil(()=>(runtime==null||runtime.IsStopped)&&(worker==null||worker.IsStopped),5000));
                if(authority!=null){authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();}released=true;
            }
            try
            {
                runtime=PlayableRuntime.RestoreBytesForTests(input,config,ai,r=>
                {
                    try
                    {
                        authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(r);
                        var v=r.ParticipantView("player-1");var b=v.Buildings.Single(b=>b.Owner==v.Owner&&b.Kind==PlayableBuildingKind.Refinery&&b.PrivateState?.Upgrade?.Active==true);
                        id=b.Id;elapsed=b.PrivateState.Upgrade.Elapsed;start=v.Tick;
                        paid=((AiBudgetLedger)Field(Owners(authority).Single(),"budget")).Capture().Paid.Single(e=>e.Action.Kind==PlayableCommandKind.UpgradeRefinery&&e.Action.EntityIds.Contains(id));
                        initialExact=input.SequenceEqual(r.CaptureBeforeStartForTests());
                        Assert.True(initialExact);Assert.AreEqual(1801,start);Assert.AreEqual(250,paid.Amount);Assert.AreEqual(p.RefineryUpgradeCost,b.PrivateState.Upgrade.PaidCost);
                        Assert.AreEqual(p.RefineryUpgradeSeconds,b.PrivateState.Upgrade.Duration);Assert.AreEqual(9.5,elapsed,1e-9);
                        // Spectator clock is external to the saved transport. Match the producer's explicit 4x before worker start.
                        r.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                    }
                    catch(Exception error){failure=error;r.RequestStop();}
                });
                if(failure!=null)Assert.Fail(failure.ToString());
                var clock=Stopwatch.StartNew();bool completed=false;long end=start;double lastElapsed=elapsed;var samples=new System.Collections.Generic.List<object>();
                while(runtime.Latest.Tick-start<360&&clock.Elapsed.TotalSeconds<45&&!completed)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);var v=runtime.ParticipantView("player-1");Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    var b=v.Buildings.Single(b=>b.Id==id);var u=b.PrivateState.Upgrade;
                    Assert.GreaterOrEqual(u.Elapsed,lastElapsed);Assert.AreEqual(p.RefineryUpgradeCost,u.PaidCost);Assert.AreEqual(p.RefineryUpgradeSeconds,u.Duration);
                    if(v.Tick!=end){end=v.Tick;lastElapsed=u.Elapsed;if(samples.Count==0||end-start>=samples.Count*30)samples.Add(new{tick=end,u.Elapsed,u.Complete,b.RefineryUpgraded});}
                    completed=b.RefineryUpgraded&&u.Complete&&!u.Active;Thread.Sleep(1);
                }
                var request=runtime.RequestCaptureBytes();var captureClock=Stopwatch.StartNew();
                while(!request.IsCompleted&&captureClock.Elapsed.TotalSeconds<10){routes.Service(runtime,p.NavigationRequestsPerFrame);Thread.Sleep(1);}
                Assert.True(request.IsCompleted);Assert.False(request.IsFaulted,request.Exception?.ToString());var final=request.Result;File.WriteAllBytes(Path.Combine(output,"current.bin"),final);Release();
                PlayableRuntime restored=null;PlayableAuthorityTick finalAuthority=null;Exception finalFailure=null;bool finalExact=false;
                try
                {
                    restored=PlayableRuntime.RestoreBytesForTests(final,config,ai,r=>
                    {
                        try
                        {
                            finalAuthority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(r);
                            var finalPaid=((AiBudgetLedger)Field(Owners(finalAuthority).Single(),"budget")).Capture().Paid.Single(e=>e.Receipt.Id==paid.Receipt.Id);
                            Assert.AreEqual(paid.Amount,finalPaid.Amount);finalExact=final.SequenceEqual(r.CaptureBeforeStartForTests());
                        }
                        catch(Exception error){finalFailure=error;}finally{r.RequestStop();}
                    });
                    Assert.True(SpinWait.SpinUntil(()=>restored.IsStopped,3000));if(finalFailure!=null)Assert.Fail(finalFailure.ToString());
                }
                finally
                {
                    if(restored!=null){restored.RequestStop();Assert.True(SpinWait.SpinUntil(()=>restored.IsStopped,3000));restored.Dispose();}
                    if(finalAuthority!=null){finalAuthority.Domain.Navigation.ReleaseCommittedHostRoot();finalAuthority.Domain.DestroyOwnedNavigation();}
                }
                File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{producer="867f791a05399950e00d054a342e0f7ee086d0f9",start,end,id,elapsed,lastElapsed,completed,initialExact,finalExact,paid,samples,wallSeconds=clock.Elapsed.TotalSeconds,limitTicks=360,wallLimitSeconds=45},Newtonsoft.Json.Formatting.Indented));
                Assert.True(completed,"saved fully paid work must finish through ordinary threaded authority at stock duration");Assert.Greater(end,start);Assert.True(finalExact);
            }
            finally{Release();}
        }
    }
}
