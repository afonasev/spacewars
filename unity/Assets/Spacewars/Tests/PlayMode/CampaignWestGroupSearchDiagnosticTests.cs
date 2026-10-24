using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class CampaignWestGroupSearchDiagnosticTests
{
    static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static readonly BindingFlags SF=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
    static readonly NavPoint Goal=new NavPoint(68,64);

    static OfflineMatchConfiguration Config(out PlayableProfile profile)
    {
        profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());
        var authored=CampaignReconOpening.Configuration(profile);
        var starts=new[]{
            new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-60.7675645086916,109.367966979121)),
            new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-61.2974757688757,108.128669510788)),
            new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-59.5675965315915,108.832241688214)),
            new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-63.4642813549011,105.685405596906))
        };
        return new OfflineMatchConfiguration(profile,authored.SourceIdentity+"-west-group-route-witness",authored.MapIdentity,authored.RouteProvenance,authored.Seed,
            authored.Roster.ToArray(),authored.Starts.ToArray(),authored.Sites.ToArray(),authored.Obstacles.ToArray(),new double[3,3],scenario:starts,
            terrain:profile.AuthoredMap.Id,scenarioBuildings:authored.ScenarioBuildings.Where(b=>b.SiteId>=4).ToArray(),missionId:CampaignReconOpening.Id);
    }

    static IEnumerator Until(Func<bool> condition,double seconds,string label,PlayableRuntime runtime,UnityHostRouteService routes,PlayableProfile profile,Action observe=null)
    {
        double end=Time.realtimeSinceStartupAsDouble+seconds;
        while(!condition()&&Time.realtimeSinceStartupAsDouble<end)
        {
            routes.Service(runtime,profile.NavigationRequestsPerFrame);
            Assert.False(runtime.IsStopped,label+" stopped: "+runtime.Latest?.Failure);
            observe?.Invoke();yield return null;
        }
        Assert.True(condition(),label+" timed out at tick="+runtime.Latest?.Tick+" state="+string.Join(";",runtime.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>e.Id+":"+e.Position.X+","+e.Position.Z+":"+e.NavigationOutcome)));
    }

    static NavigationArrivalWork ActiveWork(NavigationArrivalWork work)
    {
        var oracle=typeof(NavigationArrivalWork).GetProperty("historicalOracle",F);
        while(oracle.GetValue(work) is NavigationArrivalWork next)work=next;
        return work;
    }

    static string QueueSummary(object scheduler)
    {
        var jobs=typeof(LayeredNavigationScheduler).GetField("jobs",F).GetValue(scheduler);
        var result=new StringBuilder();
        foreach(var job in (System.Collections.IEnumerable)jobs)
        {
            if(result.Length>0)result.Append(" | ");
            var request=(NavScheduledRequest)job.GetType().GetProperty("Request",F).GetValue(job);
            var subscribers=job.GetType().GetProperty("Subscribers",F).GetValue(job);
            result.Append("phase=").Append(job.GetType().GetField("Phase",F).GetValue(job))
                .Append(" graph=").Append(request?.GraphKey).Append(" flags=").Append(request?.DemandQuery).Append('/').Append(request?.DemandQueryV6).Append('/').Append(request?.CertifiedLazyPartition).Append('/').Append(request?.RectangularOrigins)
                .Append(" candidates=").Append(request?.Region?.Candidates?.Count)
                .Append(" subscribers=").Append(subscribers?.GetType().GetProperty("Count",F).GetValue(subscribers));
        }
        return result.ToString();
    }

    [UnityTest] public IEnumerator CapturePendingOrdinaryWestGroupToEastSearch()=>CapturePending(false);
    [UnityTest] public IEnumerator CaptureLatePendingOrdinaryWestGroupToEastSearch()=>CapturePending(true);
    static IEnumerator CapturePending(bool late)
    {
        var config=Config(out var profile);var runtime=new PlayableRuntime(config,114);var routes=new UnityHostRouteService();
        try
        {
            yield return Until(()=>runtime.IsPrepared,60,"four-actor west-group preparation",runtime,routes,profile);
            Assert.AreEqual(4,runtime.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player));
            var actors=runtime.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>e.Id).OrderBy(id=>id).ToArray();
            Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,1,"player-1",PlayableCommandKind.AttackMove,actors,Goal)).Accepted);
            yield return Until(()=>runtime.ReceiptsAfter(0).Any(v=>v.Sequence==1&&v.Status!=PlayableCommandStatus.Accepted),30,"four-actor west-group command application",runtime,routes,profile);
            Assert.AreEqual(PlayableCommandStatus.Applied,runtime.ReceiptsAfter(0).Single(v=>v.Sequence==1&&v.Status!=PlayableCommandStatus.Accepted).Status);
            Assert.IsNotEmpty(Environment.GetEnvironmentVariable("CAMPAIGN_EVIDENCE"),"Diagnostic capture requires CAMPAIGN_EVIDENCE output directory");
            string evidence=Environment.GetEnvironmentVariable("CAMPAIGN_EVIDENCE");System.IO.Directory.CreateDirectory(evidence);bool captured=false;int captures=0;double firstCaptureAt=0;double captureNotBefore=late?Time.realtimeSinceStartupAsDouble+45:0;
            bool Capture()
            {
                if(captured)return true;if(Time.realtimeSinceStartupAsDouble<captureNotBefore)return false;if(captures>0&&runtime.Latest.Metrics.NavigationPending==0){captured=true;return true;}if(captures==1&&Time.realtimeSinceStartupAsDouble-firstCaptureAt<2)return false;
                var port=runtime.NavigationBinding.RoutePort;
                var session=(NavigationSession)typeof(NavigationRoutePort).GetField("session",F).GetValue(port);
                var root=session.PinCommittedHostRoot();if(root==null)return false;
                try
                {
                    foreach(var pair in root.ArrivalWork)
                    {
                        if(!(pair.Value is NavigationArrivalWork outer))continue;var work=ActiveWork(outer);
                        var scheduler=(LayeredNavigationScheduler)typeof(NavigationArrivalWork).GetField("scheduler",F).GetValue(work);
                        foreach(var job in (System.Collections.IEnumerable)typeof(LayeredNavigationScheduler).GetField("jobs",F).GetValue(scheduler))
                        {
                            var demand=job.GetType().GetProperty("Demand",F).GetValue(job);if(demand==null)continue;
                            object Field(string name)=>demand.GetType().GetField(name,F)?.GetValue(demand);
                            if(Field("phase")?.ToString()!="Candidates")continue;
                            Assert.True((bool)typeof(NavigationArrivalWork).GetProperty("IsImmutableCaptureRoot",F).GetValue(work));
                            var request=(NavScheduledRequest)job.GetType().GetProperty("Request",F).GetValue(job);
                            var report=new StringBuilder("Pinned actual immutable threaded ArrivalWork candidate search; diagnostic only.\n");
                            report.AppendLine("arrival_id="+pair.Key+" status="+work.Status+" epoch="+work.LogicalEpoch+" input="+work.InputBinding+" graph="+request.GraphKey+" flags="+request.DemandQuery+"/"+request.DemandQueryV6+"/"+request.CertifiedLazyPartition+"/"+request.RectangularOrigins+"/"+request.TerminalRectangleProof+"/corner="+request.TerminalCornerProof);
                            report.AppendLine("region_candidates="+request.Region.Candidates.Count+" job_phase="+job.GetType().GetField("Phase",F).GetValue(job)+" routeCounters="+work.RouteCounters.PhysicalGraphWork+"/"+work.RouteCounters.ConnectorWork);
                            report.AppendLine("work_phase="+Field("phase")+" lazyStage="+Field("lazyStage")+" node="+Field("node")+" current="+Field("current")+" candidate="+Field("candidate")+" chosen="+Field("chosen")+" frontier="+Field("exactFrontier")?.GetType().GetProperty("Count",F).GetValue(Field("exactFrontier")));
                            report.AppendLine("subscription="+request.Subscription.StableKey);var box=Field("terminalBox");if(box!=null)report.AppendLine("terminal_hull="+string.Join(";",box.GetType().GetFields(F).Select(v=>v.Name+"="+v.GetValue(box))));
                            foreach(string field in new[]{"phase","memberIndex","pageIndex","candidateCursor","preparationCursor","preparationPageIndex"})report.AppendLine("arrival_"+field+"="+typeof(NavigationArrivalWork).GetField(field,F)?.GetValue(work));
                            foreach(string field in new[]{"hStage","hCandidateCursor","lazyStage","lazyX","lazyZ","seed","current","candidate","chosen","terminalAttempted","terminalBlocker","terminalBox"})report.AppendLine("query_"+field+"="+Field(field));
                            report.AppendLine("scheduler_jobs="+QueueSummary(scheduler));
                            foreach(var row in root.ArrivalWork)
                            {
                                if(!(row.Value is NavigationArrivalWork other))continue;
                                var otherScheduler=(LayeredNavigationScheduler)typeof(NavigationArrivalWork).GetField("scheduler",F).GetValue(ActiveWork(other));
                                report.AppendLine("root_arrival="+row.Key+" status="+other.Status+" pending="+other.PendingRouteCount+" jobs={"+QueueSummary(otherScheduler)+"}");
                            }
                            report.AppendLine("counters="+string.Join(";",typeof(NavSchedulerCounters).GetProperties().Select(v=>v.Name+"="+v.GetValue(routes.CertifiedCounters))));
                            var open=typeof(NavigationSession).GetMethod("TryOpenPhysicalHostScope",F);var args=new object[]{Enum.Parse(open.GetParameters()[0].ParameterType,"Capture"),work.LogicalEpoch,0L,null};
                            Assert.True((bool)open.Invoke(session,args),"Pinned owned-navigation capture admission refused");var scope=args[3];object output=null;
                            try
                            {
                                var reservation=scope.GetType().GetMethod("OwnedReservation",F).Invoke(scope,new object[]{null});var wire=typeof(NavOwnedStorage).Assembly.GetType("Spacewars.Simulation.NavPhysicalBodyWire");var captureType=typeof(NavigationSession).Assembly.GetType("Spacewars.Runtime.NavigationCaptureBytes");
                                Action<System.IO.BinaryWriter> write=w=>wire.GetMethod("WriteTo",SF).Invoke(null,new object[]{w,outer,reservation});
                                output=captureType.GetMethod("Write",SF).Invoke(null,new object[]{reservation,write});scope.GetType().GetMethod("Attach",F).Invoke(scope,new[]{output});
                                scope.GetType().GetMethods(F).Single(m=>m.Name=="Commit"&&m.GetParameters().Length==2).Invoke(scope,new object[]{output,(Action<object>)(_=>{})});
                                var bytes=(byte[])captureType.GetMethod("TransferPublic",F).Invoke(output,null);output=null;
                                System.IO.File.WriteAllBytes(System.IO.Path.Combine(evidence,"west-group-pending-arrival-"+(captures+1)+".body"),bytes);
                                using(var sha=System.Security.Cryptography.SHA256.Create())report.AppendLine("BODY_SHA256="+BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());
                                report.AppendLine("BODY_BYTES="+bytes.Length);
                            }
                            finally{(output as IDisposable)?.Dispose();(scope as IDisposable)?.Dispose();}
                            report.AppendLine("tick="+runtime.Latest.Tick+" actors="+string.Join(";",runtime.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>e.Id+":"+e.Position.X+","+e.Position.Z+":"+e.NavigationOutcome)));
                            System.IO.File.WriteAllText(System.IO.Path.Combine(evidence,"west-group-pending-arrival-"+(captures+1)+".txt"),report.ToString());Debug.Log("CAMPAIGN_WEST_GROUP_PENDING_SEARCH "+report);captures++;if(captures==1)firstCaptureAt=Time.realtimeSinceStartupAsDouble;captured=captures>=2;return captured;
                        }
                    }
                    return false;
                }
                finally{typeof(NavOwnedStorage).GetMethod("Release",F).Invoke(root,null);}
            }
            yield return Until(Capture,60,"pinned west-group pending candidate search",runtime,routes,profile);Assert.True(captured);
        }
        finally{runtime.RequestStop();routes.Dispose();}
    }

    [UnityTest] public IEnumerator OrdinaryWestGroupToEastAppliedRouteWitness()=>RunOrdinaryWitness(false);
    [UnityTest] public IEnumerator ObserveOrdinaryWestGroupServiceCostPrefix()=>RunOrdinaryWitness(true);
    static IEnumerator RunOrdinaryWitness(bool diagnosis)
    {
        var config=Config(out var profile);var runtime=new PlayableRuntime(config,114);var routes=new UnityHostRouteService();ServiceTrace serviceTrace=null;
        try
        {
            yield return Until(()=>runtime.IsPrepared,60,"four-actor west-group preparation",runtime,routes,profile);
            Assert.AreEqual(4,runtime.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player));
            var initial=runtime.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player).ToDictionary(e=>e.Id,e=>e.Position);
            var actors=initial.Keys.OrderBy(id=>id).ToArray();serviceTrace=ServiceTrace.Create(runtime,actors);
            Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,1,"player-1",PlayableCommandKind.AttackMove,actors,Goal)).Accepted);
            yield return Until(()=>runtime.ReceiptsAfter(0).Any(v=>v.Sequence==1&&v.Status!=PlayableCommandStatus.Accepted),30,"four-actor west-group command application",runtime,routes,profile);
            Assert.AreEqual(PlayableCommandStatus.Applied,runtime.ReceiptsAfter(0).Single(v=>v.Sequence==1&&v.Status!=PlayableCommandStatus.Accepted).Status);
            bool moved=false,arrived=false,demandSeen=false,compilerSeen=false,certificateSeen=false;
            void Observe()
            {
                serviceTrace?.Sample(false);
                var binding=runtime.NavigationBinding;var port=binding?.RoutePort;if(port==null)return;
                var session=(NavigationSession)typeof(NavigationRoutePort).GetField("session",F).GetValue(port);var root=session.PinCommittedHostRoot();if(root==null)return;
                try
                {
                    foreach(var pair in root.ArrivalWork)
                    {
                        if(!(pair.Value is NavigationArrivalWork work))continue;
                        foreach(var job in (System.Collections.IEnumerable)typeof(LayeredNavigationScheduler).GetField("jobs",F).GetValue(typeof(NavigationArrivalWork).GetField("scheduler",F).GetValue(ActiveWork(work))))
                        {
                            var request=(NavScheduledRequest)job.GetType().GetProperty("Request",F).GetValue(job);if(request==null)continue;
                            demandSeen|=request.DemandQuery;
                            compilerSeen|=job.GetType().GetProperty("Compiler",F).GetValue(job)!=null;
                            if(request.CertifiedLazyPartition){var demand=job.GetType().GetProperty("Demand",F).GetValue(job);var store=demand?.GetType().GetField("lazyStore",F).GetValue(demand);var proof=store?.GetType().GetField("Exclusion",F).GetValue(store);certificateSeen|=proof?.GetType().GetProperty("Progress",F).GetValue(proof)?.ToString()=="Certified";}
                        }
                    }
                }
                finally{typeof(NavOwnedStorage).GetMethod("Release",F).Invoke(root,null);}
                foreach(var actor in runtime.Latest.Entities.Where(e=>initial.ContainsKey(e.Id)))
                {
                    var before=initial[actor.Id];moved|=Math.Abs(actor.Position.X-before.X)+Math.Abs(actor.Position.Z-before.Z)>.1;
                    arrived|=Math.Abs(actor.Position.X-Goal.X)<3&&Math.Abs(actor.Position.Z-Goal.Z)<3;
                }
            }
            if(diagnosis){Assert.NotNull(serviceTrace);double end=Time.realtimeSinceStartupAsDouble+20;yield return Until(()=>{Observe();return Time.realtimeSinceStartupAsDouble>=end;},30,"service-cost diagnostic prefix only",runtime,routes,profile,Observe);serviceTrace.Sample(true);yield break;}
            yield return Until(()=>{Observe();return moved&&arrived;},60,"actual west-group first motion and real arrival",runtime,routes,profile,Observe);
            Assert.True(moved,"at least one of the four actual actors must move from its authored start");Assert.True(arrived,"at least one actor must physically reach the attack-move destination");
            Assert.True(certificateSeen,"fresh route must retain a certified paid capacity request");Assert.True(demandSeen,"fresh route must use DemandQuery");Assert.False(compilerSeen,"fresh route must avoid eager compiler");
            Debug.Log("CAMPAIGN_WEST_GROUP_ROUTE_ARRIVAL tick="+runtime.Latest.Tick+" actors="+string.Join(";",runtime.Latest.Entities.Where(e=>initial.ContainsKey(e.Id)).Select(e=>e.Id+":"+e.Position.X+","+e.Position.Z)));
        }
        finally{serviceTrace?.Dispose();runtime.RequestStop();routes.Dispose();}
    }
    sealed class ServiceTrace : IDisposable
    {
        readonly PlayableRuntime runtime;readonly NavigationSession session;readonly int[] actors;readonly string folder;
        readonly StringBuilder trace=new StringBuilder();readonly long[] counts=new long[9];readonly long[] timingStart=new long[4],timingTicks=new long[4],timingCount=new long[4];readonly FieldInfo computedHook,serviceHook;
        readonly object priorComputed,priorService;readonly double started;double next;
        static readonly string[] Labels={"computed","committed","closed-without-advance","scope-refused","authority-bytes-refused","advance-retention-blocked","source-owner-refused","prepared-admission-refused","no-eligible"};
        static object Read(object o,string name)=>o==null?null:o.GetType().GetProperty(name,F)?.GetValue(o)??o.GetType().GetField(name,F)?.GetValue(o);
        static string Values(object o,params string[] names)=>string.Join(" ",names.Select(n=>n+"="+Read(o,n)));
        public static ServiceTrace Create(PlayableRuntime runtime,int[] actors){var folder=Environment.GetEnvironmentVariable("CAMPAIGN_SERVICE_TRACE");return string.IsNullOrEmpty(folder)?null:new ServiceTrace(runtime,actors,folder);}
        ServiceTrace(PlayableRuntime r,int[] ids,string output)
        {
            runtime=r;actors=ids;folder=output;System.IO.Directory.CreateDirectory(folder);started=Time.realtimeSinceStartupAsDouble;var leaf=typeof(NavigationSession).Assembly.GetType("Spacewars.Runtime.NavigationArrivalAttemptSnapshot");trace.AppendLine("snapshotHeaderPreChange="+leaf.GetField("Header",SF).GetValue(null));
            session=(NavigationSession)typeof(NavigationRoutePort).GetField("session",F).GetValue(r.NavigationBinding.RoutePort);
            computedHook=typeof(NavigationSession).GetField("BeforeArrivalAdvanceCommit",F);serviceHook=typeof(NavigationSession).GetField("ArrivalServiceDiagnosticForQa",F);
            priorComputed=computedHook.GetValue(session);priorService=serviceHook.GetValue(session);
            Action<NavArrivalStatus> computed=_=>{System.Threading.Interlocked.Increment(ref counts[0]);EndTiming(0);};
            Action<string> serviced=label=>{if(label=="advance-start")StartTiming(0);else if(label=="frontier-start")StartTiming(1);else if(label=="frontier-end")EndTiming(1);else if(label=="publish-start")StartTiming(2);else if(label=="committed")EndTiming(2);else if(label=="release-start")StartTiming(3);else if(label=="release-end")EndTiming(3);for(int n=1;n<Labels.Length;n++)if(Labels[n]==label){System.Threading.Interlocked.Increment(ref counts[n]);break;}};
            computedHook.SetValue(session,Delegate.Combine(priorComputed as Delegate,computed));serviceHook.SetValue(session,Delegate.Combine(priorService as Delegate,serviced));Sample(true);
        }
        void StartTiming(int n)=>timingStart[n]=System.Diagnostics.Stopwatch.GetTimestamp();
        void EndTiming(int n){long at=System.Diagnostics.Stopwatch.GetTimestamp();System.Threading.Interlocked.Add(ref timingTicks[n],at-timingStart[n]);System.Threading.Interlocked.Increment(ref timingCount[n]);}
        public void Sample(bool force)
        {
            double now=Time.realtimeSinceStartupAsDouble;if(!force&&now<next)return;next=now+1;
            var root=session.PinCommittedHostRoot();if(root==null)return;
            try
            {
                trace.AppendLine("sample elapsed="+(now-started).ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+" tick="+runtime.Latest.Tick+" counters="+string.Join(",",Labels.Select((label,n)=>label+":"+System.Threading.Interlocked.Read(ref counts[n]))));
                trace.AppendLine("timings="+string.Join(",",new[]{"fork+advance","frontier","publish","release"}.Select((label,n)=>label+":"+(System.Threading.Interlocked.Read(ref timingTicks[n])*1000d/System.Diagnostics.Stopwatch.Frequency).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"ms/"+System.Threading.Interlocked.Read(ref timingCount[n]))));
                trace.AppendLine("quantum "+Values(root,"QuantumSerial","QuantumTick","QuantumBudget","ConsumedQuantum","QuantumCompleted","QuantumRunnable","LastServedArrival","ArrivalRoundEnd","FairArrivals")+" frontier="+Read(Read(root,"Frontier"),"Stage"));
                foreach(var pair in root.ArrivalWork)
                {
                    var work=(NavigationArrivalWork)pair.Value;var active=ActiveWork(work);object history=null;foreach(var row in root.ArrivalHistory)if(row.Key==pair.Key)history=row.Value;
                    int count=history==null?0:(int)Read(history,"Count");object last=count>0?history.GetType().GetProperty("Item",F).GetValue(history,new object[]{count-1}):null;
                    trace.AppendLine("arrival="+pair.Key+" outer="+Values(work,"LogicalEpoch","Status","LastAdvanceRetentionBlocked","InputBinding")+" history="+count+" grant="+Values(last,"Epoch","Graph","Field","Connectors","Bookkeeping","Allocation")+" active="+Values(active,"LogicalEpoch","Status")+" phase="+Read(active,"phase")+" member="+Read(active,"memberIndex")+" page="+Read(active,"pageIndex")+" candidate="+Read(active,"candidateCursor")+" proposals="+Read(Read(active,"assignments"),"Count")+" historicalGate="+Read(work,"historicalGate")+" graph="+active.RouteCounters.PhysicalGraphWork+" connectors="+active.RouteCounters.ConnectorWork);
                    var scheduler=(LayeredNavigationScheduler)Read(active,"scheduler");trace.AppendLine("scheduler "+Values(scheduler,"lastEpoch","graphQuota","connectorQuota","bookkeepingQuota","maintenanceQuota","RetentionBlocked"));
                    foreach(var job in (System.Collections.IEnumerable)Read(scheduler,"jobs"))
                    {
                        var q=Read(job,"Demand");var request=(NavScheduledRequest)Read(job,"Request");
                        trace.AppendLine("query "+Values(q,"phase","lazyStage","lazyX","lazyZ","seed","shadowBlocker","shadowStart","shadowLow","shadowHigh","chosen","candidate")+" policy="+request?.OriginShadowProof+" origin="+request?.Subscription.Origin.Position.X+","+request?.Subscription.Origin.Position.Z);
                    }
                }
                using((IDisposable)typeof(NavigationSession).GetMethod("EnterTransport",F).Invoke(session,null))
                {
                    var installed=Read(session,"installedExecutions");var contains=installed.GetType().GetMethod("ContainsKey",F);
                    trace.AppendLine("installed="+string.Join(",",actors.Select(id=>id+":"+contains.Invoke(installed,new object[]{id})))+" "+Values(session,"OwnedMovementPublications","OwnedMovementRefusals","LastOwnedMovementRefusal","LastNavigationStepPath","OwnedResultBatchPublications","LastResultBatchStage","LastResultBatchSourceReason")+" answers="+Read(Read(session,"Answers"),"Count")+" allocating="+Read(Read(session,"allocating"),"Count")+" memory="+Values(Read(session,"PhysicalHostBudget"),"PhysicalBytes","HeldBytes","ActiveScopes"));
                    trace.AppendLine("authority="+Read(session,"ArrivalAuthoritySnapshot"));
                }
                trace.AppendLine("actors="+string.Join(";",runtime.Latest.Entities.Where(e=>actors.Contains(e.Id)).Select(e=>e.Id+":"+e.Position.X+","+e.Position.Z+":"+e.NavigationOutcome)));
            }
            finally{typeof(NavOwnedStorage).GetMethod("Release",F).Invoke(root,null);System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"whole-group-service-trace.txt"),trace.ToString());}
        }
        public void Dispose(){try{Sample(true);System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"whole-group-service-trace.txt"),trace.ToString());}finally{computedHook.SetValue(session,priorComputed);serviceHook.SetValue(session,priorService);}}
    }

}
