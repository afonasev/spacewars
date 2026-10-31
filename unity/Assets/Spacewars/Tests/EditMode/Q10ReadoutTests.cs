using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        [Test] public void Q10NavigationStepRecorderIsBoundedAndPreservesCompletedPrefix()
        {
            var recorder=new QaPerformanceNavigationSteps();
            for(int i=0;i<QaPerformanceNavigationSteps.Capacity+1;i++)recorder.Add(new QaPerformanceNavigationSteps.Row{Tick=i,Path=5,BatchStage=108,BatchSource=19});
            var rows=recorder.Copy(out int dropped);Assert.AreEqual(8192,rows.Length);Assert.AreEqual(1,dropped);
            Assert.Zero(rows[0].Tick);Assert.AreEqual(8191,rows[8191].Tick);Assert.AreEqual(108,rows[8191].BatchStage);Assert.AreEqual(19,rows[8191].BatchSource);
            string path=Path.Combine(Path.GetTempPath(),"spacewars-q10-navigation-"+Guid.NewGuid()+".csv");
            try{recorder.WriteCsv(path);var lines=File.ReadAllLines(path);Assert.AreEqual(8194,lines.Length);int columns=lines[0].Split(',').Length;Assert.True(lines.Skip(1).Take(8192).All(line=>line.Split(',').Length==columns));StringAssert.StartsWith("# rows=8192,dropped=1,capacity=8192,frequency=",lines.Last());}
            finally{if(File.Exists(path))File.Delete(path);}
        }
        [Test] public void Q10NavigationStepReadoutMatchesActualStepWithoutChangingCurrentContinuation()
        {
            var profile=PlayableProfile.Default;var d=new PlayableDomain(profile,1,false);PlayableDomain cold=null;
            byte[] Bytes(PlayableDomain domain){using(var capture=domain.CaptureWorldState(7,"q10-navigation-step"))return capture.Encode();}
            try{
                Assert.IsNull(d.NavigationStepsForQa);var initial=Bytes(d);
                cold=PlayableDomain.RestoreWorldBytes(initial,profile,7,"q10-navigation-step");
                d.EnableNavigationStepsForQa();var recorder=d.NavigationStepsForQa;d.EnableNavigationStepsForQa();Assert.AreSame(recorder,d.NavigationStepsForQa);
                d.Step(1d/30);cold.Step(1d/30);
                var rows=recorder.Copy(out int dropped);Assert.AreEqual(1,rows.Length);Assert.Zero(dropped);
                Assert.AreEqual(d.Navigation.LastNavigationStepPath,rows[0].Path);Assert.AreEqual(d.Navigation.LastResultBatchStage,rows[0].BatchStage);
                Assert.AreEqual(d.Navigation.LastResultBatchSourceReason,rows[0].BatchSource);Assert.AreEqual(d.Navigation.AppliedResults,rows[0].Applied);
                Assert.AreEqual(d.Navigation.LastOwnedMovementRefusal,rows[0].MovementRefusal);Assert.AreEqual(d.Navigation.OwnedMovementRefusals,rows[0].MovementRefusals);
                Assert.AreEqual(d.Navigation.Answers.Count,rows[0].Answers);Assert.Greater(rows[0].Stamp,0);Assert.GreaterOrEqual(rows[0].Milliseconds,0);
                Assert.IsNull(d.Navigation.MovementDiagnosticObserver);Assert.IsNull(cold.NavigationStepsForQa);
                CollectionAssert.AreEqual(Bytes(cold),Bytes(d),"scalar readout leaves current-format world and same next step exact");
            }finally{cold?.DestroyOwnedNavigation();d.DestroyOwnedNavigation();}
        }
        [Test] public void Q10PendingIntentIsDistinctFromCurrentAndSurvivesValidColdRestore()
        {
            Contact30Source(true,(config,views,profile)=>{
                using(var s=new ContactCostSource(config,views,profile)){
                    var commands=views.Select(v=>PlayableRuntime.CreateEightPlayerMovingPerformanceCommand(v,PlayableRuntime.EightPlayerMovingPerformanceSequence(v.Owner,0))).ToArray();
                    var saved=s.Save();var hot=((PlayableDomain)s.Domain).CapturePerformanceQueues(commands);var cold=PlayableDomain.RestoreConfiguredWorldBytes(saved,profile,7,"contact-publish-cost",config);var budget=cold.Navigation.PhysicalHostBudget;
                    try{
                        var restored=cold.CapturePerformanceQueues(commands);Assert.AreEqual(400,hot.Count);Assert.Zero(hot.Dropped);int pending=0;
                        foreach(var command in commands)foreach(int id in command.EntityIds){
                            Assert.True(hot.TryActor(id,out var a));Assert.True(restored.TryActor(id,out var b));
                            Assert.AreEqual((long)CostOperation.GetValue(s.Nav),a.AuthorityOperation);Assert.AreEqual((long)CostOperation.GetValue(cold.Navigation),b.AuthorityOperation);
                            // This diagnostic token is runtime-local and not encoded in current BODY.
                            a.AuthorityOperation=b.AuthorityOperation=0;Assert.AreEqual(JsonConvert.SerializeObject(a),JsonConvert.SerializeObject(b));
                            Assert.True(a.ActorPresent);Assert.AreEqual(command.Sequence,a.ExpectedSequence);Assert.AreEqual(s.Nav.Admission.Requests.Count,a.AdmissionRequests);
                            if(a.PendingSequence==command.Sequence&&a.CurrentSequence!=command.Sequence&&a.ActiveSequence!=command.Sequence)pending++;
                        }
                        string output=Environment.GetEnvironmentVariable("SPACEWARS_Q10_READOUT_OUTPUT");
                        if(output!=null){Directory.CreateDirectory(output);string path=Path.Combine(output,"pending-groups.csv");hot.WriteCsv(path);var lines=File.ReadAllLines(path);Assert.AreEqual(402,lines.Length);Assert.AreEqual("# rows=400,dropped=0",lines[lines.Length-1]);int columns=lines[0].Split(',').Length;Assert.True(lines.Skip(1).Take(400).All(l=>l.Split(',').Length==columns));}
                        Assert.Greater(pending,0,"Actual uninstalled intents must be visible separately; no CurrentOrder sample is not rejection.");
                        Assert.False(hot.TryActor(int.MaxValue,out var unknown));Assert.AreEqual(-1,unknown.ExpectedSequence);Assert.AreEqual(-1,unknown.AdmissionRequests);
                    }finally{cold.DestroyOwnedNavigation();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
                }
            });
        }
        [Test] public void Q10ActiveReadoutFollowsActualRouteInstallation()
        {
            var p=PlayableProfile.Default;var d=new PlayableDomain(p,1,false);var n=d.Navigation;var budget=n.PhysicalHostBudget;var kernel=new CertifiedRouteKernel();
            try{
                int id=(int)typeof(PlayableDomain).GetMethod("SpawnUnit",CostFlags).Invoke(d,new object[]{new NavPoint(-18,-14),PlayableOwner.Player,PlayableEntityKind.Tank});
                var command=new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{id},new NavPoint(-10,-14));
                Assert.AreEqual(PlayableCommandStatus.Applied,typeof(PlayableDomain).GetMethod("Apply",CostFlags).Invoke(d,new object[]{command,null}));
                QaPerformanceQueueRow row=default;bool installed=false;
                for(int i=0;i<240;i++){
                    d.CapturePerformanceQueues(new[]{command}).TryActor(id,out row);if(row.ActiveSequence==1&&row.CurrentSequence==1){installed=true;break;}
                    kernel.Pump(new PlayableRouteBinding(d.Geometry,p,n.Admission,n.RoutePort),64);n.ApplyResults();
                    typeof(PlayableDomain).GetMethod("Step",CostFlags).Invoke(d,new object[]{1d/30});
                }
                Assert.True(installed,"Minimal actual route must reach installed/current before qualifying this readout.");Assert.AreEqual(0,row.PendingSequence);Assert.Greater(row.ActiveGroup,0);Assert.AreEqual(1,row.ExpectedSequence);
            }finally{kernel.Clear();n.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        }
        [Test] public void Q10AiObserverOnOffKeepsExactConfiguredAiStateAndCurrentRestore()
        {
            byte[] expected=null;
            try{Contact30Source(true,(config,views,profile)=>{
                foreach(bool enabled in new[]{false,true}){
                    QaAiRouteAudit.ResetForTests(enabled);var a=new PlayableAuthorityTick(config,1);var d=a.Domain;var budget=d.Navigation.PhysicalHostBudget;PlayableAuthorityTick cold=null;
                    try{
                        typeof(PlayableDomain).GetProperty("Tick",CostFlags).SetValue(d,60L);typeof(PlayableAuthorityTick).GetField("sequence",CostFlags).SetValue(a,60L);
                        var snapshots=a.Scheduler.Owners.Select(owner=>d.PlayerSnapshotForArmyAi(60,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,d.OwnerFor(owner.OwnerId),true,owner.ArmyState)).ToArray();
                        for(int i=0;i<8;i++)a.Scheduler.Owners[i].Review(snapshots[i],0);
                        var saved=a.CaptureBytes();if(expected==null)expected=saved;else CollectionAssert.AreEqual(expected,saved,"AI actions/records/receipts/private state and world/proof bindings must remain exact ON/OFF.");
                        cold=PlayableAuthorityTick.RestoreBytes(saved,config);CollectionAssert.AreEqual(a.CaptureBytes(),cold.CaptureBytes(),"Valid current configured format, no QA owner state in wire.");
                        var totals=QaAiRouteAudit.Snapshot(out int dropped);Assert.Zero(dropped);Assert.AreEqual(enabled?8:0,totals.Length);
                        if(enabled){Assert.AreEqual(8,totals.Sum(x=>x.SafeCalls));Assert.Zero(totals.Sum(x=>x.RepeatedSafe));TestContext.WriteLine("Q10_OBSERVER native8Review safe="+totals.Sum(x=>x.SafeCalls)+" observerWallMs="+totals.Sum(x=>x.ObserverMilliseconds)+" reviewWallMs="+totals.Sum(x=>x.ReviewMilliseconds));}
                    }finally{if(cold!=null){var cb=cold.Domain.Navigation.PhysicalHostBudget;cold.Stop();cold.Domain.DestroyOwnedNavigation();Assert.Zero(cb.PhysicalBytes);Assert.Zero(cb.HeldBytes);Assert.Zero(cb.ActiveScopes);}a.Stop();d.DestroyOwnedNavigation();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
                }
            });}finally{QaAiRouteAudit.ResetForTests(false);QaAiRouteAudit.EnabledForTests=null;}
        }
        [Test] public void Q10ReadoutsAreAbsentWithoutExplicitFlag()
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            using(var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(profile,1,setup,startPaused:true)){
                Assert.False(runtime.PerformanceReadoutsEnabled);Assert.IsNull(runtime.PerformanceQueueFrame);
                Assert.IsEmpty(runtime.CopyPerformanceReceipts(out int expected,out int dropped));Assert.Zero(expected);Assert.Zero(dropped);
                Assert.IsEmpty(QaAiRouteAudit.Snapshot(out int auditDropped));Assert.Zero(auditDropped);
            }
        }
        [Test] public void Q10AiObserverResetsPerReviewAndReportsBoundsAndBindings()
        {
            try{Contact30Source(true,(config,views,profile)=>{
                QaAiRouteAudit.ResetForTests(true);var o=PlayableAiObservation.From(views[0]);var other=PlayableAiObservation.From(views[0]);var p2=PlayableProfile.Create(profile.CopyData(),new AiTestMap());
                using(var empty=QaAiRouteAudit.Begin(o,profile)){Assert.IsNull(empty.GetType().GetField("seen",CostFlags).GetValue(empty),"No dictionary before a route-safety call.");}
                var emptyTotals=QaAiRouteAudit.Snapshot(out _);Assert.Zero(emptyTotals[0].MaxPairs);QaAiRouteAudit.ResetForTests(true);
                using(QaAiRouteAudit.Begin(o,profile)){
                    QaAiRouteAudit.Observe(o,null,profile,false);QaAiRouteAudit.Observe(o,null,profile,false);QaAiRouteAudit.Observe(o,null,profile,true);
                    QaAiRouteAudit.Observe(o,null,p2,false);QaAiRouteAudit.Observe(other,null,profile,false);
                }
                using(QaAiRouteAudit.Begin(o,profile))QaAiRouteAudit.Observe(o,null,profile,false);
                QaAiRouteAudit.Observe(o,null,profile,false); // outside lifetime: no effect
                var rows=QaAiRouteAudit.Snapshot(out int dropped);Assert.Zero(dropped);Assert.AreEqual(1,rows.Length);var r=rows[0];
                Assert.AreEqual(2,r.Reviews);Assert.AreEqual(5,r.SafeCalls);Assert.AreEqual(1,r.WithdrawalCalls);Assert.AreEqual(1,r.RepeatedSafe);Assert.AreEqual(2,r.UniqueSafe);Assert.AreEqual(1,r.UniqueWithdrawal);Assert.AreEqual(1,r.ForeignProfileCalls);Assert.AreEqual(1,r.ForeignObservationCalls);Assert.AreEqual(1,r.UntrackedCalls);Assert.AreEqual(1,r.MaxPairs);Assert.AreEqual(1,r.MaxBound);
            });}finally{QaAiRouteAudit.ResetForTests(false);QaAiRouteAudit.EnabledForTests=null;}
        }
    }
}
