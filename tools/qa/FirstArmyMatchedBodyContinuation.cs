using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    // Temporary EditMode installation only. The production solver is unmodified.
    public sealed partial class NavigationPhysicalBodyTests
    {
        static T MatchedField<T>(object value,string name) => (T)(value.GetType().GetField(name,
            BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public) ?? throw new MissingFieldException(name)).GetValue(value);
        static string MatchedSha(byte[] bytes) { using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
        static LayeredNavigationScheduler.Job MatchedJob(NavigationArrivalWork work)
        {
            var q=MatchedField<NavPersistentQueue<LayeredNavigationScheduler.Job>>(PrefixScheduler(work),"jobs");
            return q.Count==0?null:q.First;
        }
        static object MatchedLocation(NavLocation p)=>new{p.SurfaceId,x=p.Position.X,z=p.Position.Z};
        static object MatchedRequest(NavScheduledRequest r)=>new{r.GraphKey,r.CompileKey,r.ArtifactKey,
            provider=r.Provider.Binding,r.Profile.ExactProfileBinding,r.Profile.GridCell,r.MaxLabels,
            r.DemandQueryV3,r.DemandQueryV4,r.DemandQueryV5,r.DemandQueryV6,r.LazyPartition,
            r.Subscription.EntityId,r.Subscription.RequestSequence,r.Subscription.GroupId,r.Subscription.CommandSequence,
            r.Subscription.Incarnation,r.Subscription.MobilityRevision,r.Subscription.IssuedTick,
            origin=MatchedLocation(r.Subscription.Origin),assigned=MatchedLocation(r.Subscription.AssignedEndpoint),
            region=new{r.Region.Id,r.Region.Revision,r.Region.StableKey,requested=MatchedLocation(r.Region.Requested),
                candidates=r.Region.Candidates.Select(MatchedLocation).ToArray()}};
        static object MatchedDemand(LayeredNavigationScheduler.Job j)
        {
            if(j?.Demand==null)return null;var d=j.Demand;
            return new{phase=MatchedField<object>(d,"phase").ToString(),sample=MatchedField<int>(d,"sample"),
                d.SampleCount,d.IndexedCount,d.FrontierCount,lazyClassified=MatchedField<NavScheduledRequest>(d,"request").LazyPartition?MatchedField<NavLazyPartitionStore>(d,"lazyStore").Cells.Count:0,lazyMask=MatchedField<NavScheduledRequest>(d,"request").LazyPartition?MatchedField<int>(d,"lazyMask"):0,current=MatchedField<int>(d,"current"),
                direction=MatchedField<int>(d,"direction"),chosen=MatchedField<int>(d,"chosen"),
                heuristicActive=typeof(NavDemandQueryWork).GetProperty("HeuristicActive",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(d),
                d.Status};
        }
        static object MatchedCounters(NavigationArrivalWork w)=>new{w.RouteCounters.LogicalGraphWork,
            w.RouteCounters.PhysicalGraphWork,w.RouteCounters.ConnectorWork,w.RouteCounters.LogicalFieldWork,
            w.RouteCounters.PhysicalFieldWork,w.RouteCounters.MaintenanceWork,w.RouteCounters.CacheHits,
            w.RouteCounters.PendingSubscriptions,w.RouteCounters.ReadyResults,w.RouteCounters.PeakFrontier,
            w.AllocationWorkConsumed,w.CandidateWorkConsumed,w.OccupancyWorkConsumed,w.DirectWorkConsumed};
        static byte[] MatchedEncode(NavigationRetentionBudget budget,NavigationArrivalWork work,long token)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,token,out var scope));
            using(scope){NavigationCaptureBytes output=null;
                try{var r=scope.OwnedReservation();output=NavigationCaptureBytes.Write(r,w=>NavPhysicalBodyWire.WriteTo(w,work,r));
                    scope.Attach(output);scope.Commit(output,_=>{});return output.TransferPublic();}
                finally{output?.Dispose();}}
        }

        [Test,Timeout(180000)] public void FirstArmyLazyPartitionMatchedGroup()
        {
            string folder=Environment.GetEnvironmentVariable("SPACEWARS_MATCHED_OUTPUT") ?? throw new ArgumentException("Missing output");
            Directory.CreateDirectory(folder);var report=new Dictionary<string,object>();var trace=new List<object>();
            var completions=new List<object>();var recipes=new Dictionary<string,object>();var samples=new Dictionary<string,int>();
            var seenMembers=new HashSet<long>();var budget=new NavigationRetentionBudget();NavigationArrivalWork work=null;
            NavigationCaptureBytes inputOwner=null;Exception failure=null;bool matched=false;long bookkeeping=0,classificationGraph=0;
            int grants=0;var clock=Stopwatch.StartNew();double decodeSeconds=0,roundtripSeconds=0,solveSeconds=0;
            try{
                var body=File.ReadAllBytes(Environment.GetEnvironmentVariable("SPACEWARS_MATCHED_BODY"));
                var side=JObject.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("SPACEWARS_MATCHED_SIDECAR")));
                Assert.AreEqual("1004067cae0e8eb706da766ecd48a00154c3a4fb7f3eab5281a83437aea3752b",MatchedSha(body));
                Assert.AreEqual((string)side["producer"]["bodySHA256"],MatchedSha(body));
                inputOwner=NavigationCaptureBytes.CreateOwned(body.Length,budget,_=>body);
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,7,0,out var decode));
                using(decode){using(var stream=new MemoryStream(inputOwner.Bytes,false))using(var reader=new BinaryReader(stream)){
                    work=NavPhysicalBodyWire.ReadFrom(reader,decode.OwnedReservation()) as NavigationArrivalWork;
                    Assert.NotNull(work);Assert.AreEqual(stream.Length,stream.Position,"EOF");}
                    decode.Attach(work);decode.Commit(work,_=>{});}
                decodeSeconds=clock.Elapsed.TotalSeconds;
                Assert.AreEqual((string)side["snapshot"]["before"]["input"]["StableKey"],work.InputBinding);
                Assert.AreEqual(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.ExactAstarArrivalPolicyV8),((INavPhysicalBody)work).PhysicalTag);
                CollectionAssert.AreEqual(body,MatchedEncode(budget,work,1),"Exact reencode BEFORE any Advance");
                matched=true;roundtripSeconds=clock.Elapsed.TotalSeconds-decodeSeconds;
                report["oldPolicyTag"]=((INavPhysicalBody)work).PhysicalTag.Encoded;
                var originalInput=work.PhysicalInput;NavigationArrivalWork fresh=null;
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Arrival,7,2,out var birth));
                using(birth){fresh=NavigationArrivalWork.CreateOwned(originalInput,birth.OwnedReservation());
                    Assert.AreSame(originalInput,fresh.PhysicalInput);fresh.SealPrepared();birth.Attach(fresh);birth.Commit(fresh,_=>{});}
                CollectionAssert.AreEqual(body,MatchedEncode(budget,work,3),"Old BODY unchanged after independent fresh policy birth");
                work.ReleaseOwner();work=fresh;
                Assert.AreEqual(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.LazyArrivalPolicyV9),((INavPhysicalBody)work).PhysicalTag);
                report["newPolicyTag"]=((INavPhysicalBody)work).PhysicalTag.Encoded;report["sameOwnedPhysicalInput"]=true;

                Assert.AreEqual(-1,work.LogicalEpoch);Assert.Zero(work.AuthorityReservationBytes);Assert.Zero(work.MemberOutcomes.Count);
                report["bodySha256"]=MatchedSha(body);report["inputBinding"]=work.InputBinding;
                report["initial"]=new{work.WorkPhase,work.LogicalEpoch,work.AuthorityReservationBytes,counters=MatchedCounters(work)};
                long authorityBytes=work.AuthorityReservationBytes,epoch=work.LogicalEpoch+1;double start=clock.Elapsed.TotalSeconds;
                while(grants<4096){
                    Assert.Less(clock.Elapsed.TotalSeconds,170,"Existing diagnostic wall bound");
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Arrival,7,epoch+10,out var operation));
                    NavigationArrivalWork next=null;
                    try{using(operation){next=work.ForkOwned(operation.OwnedReservation());
                        Assert.AreEqual(authorityBytes,next.AuthorityReservationBytes);
                        next.Advance(epoch,256,new NavEpochQuota(256,256,256,256,256));
                        Assert.False(next.LastAdvanceRetentionBlocked,"Admission refusal is not completed work");
                        var scheduler=PrefixScheduler(next);if(scheduler.CaptureEpoch().Epoch==epoch)bookkeeping+=256-scheduler.RemainingQuota.Bookkeeping;
                        var job=MatchedJob(next);
                        if(job!=null){
                            string key=job.Request.Subscription.StableKey;var cached=MatchedField<NavLazyPartitionStore>(scheduler,"lazyCatalog");
                            var shared=job.Demand==null?null:MatchedField<NavLazyPartitionStore>(job.Demand,"lazyStore");
                            if(!recipes.ContainsKey(key))recipes[key]=new{firstObservedEpoch=epoch,request=MatchedRequest(job.Request),
                                catalogPresent=cached!=null,catalogCompatible=cached?.Compatible(job.Request),
                                demandAlreadyBorn=job.Demand!=null,sharedCatalog=cached!=null&&cached.Compatible(job.Request),classified=shared?.Cells.Count,positive=shared?.Points.Count,
                                sample=job.Demand==null?(int?)null:MatchedField<int>(job.Demand,"sample"),
                                indexed=job.Demand?.IndexedCount,cacheHits=scheduler.Counters.CacheHits};
                            if(job.Demand!=null){Assert.True(job.Request.LazyPartition);classificationGraph=Math.Max(classificationGraph,shared.Cells.Count);}
                        }
                        next.SealPrepared();operation.Attach(next);operation.Commit(next,_=>{});}
                        work.ReleaseOwner();work=next;next=null;
                    }finally{next?.ReleaseOwner();}
                    grants++;var active=MatchedJob(work);var assignments=MatchedField<NavPagedList<NavArrivalProposal>>(work,"assignments");
                    foreach(var a in assignments){if(!seenMembers.Add(a.Member.EntityId))continue;
                        Assert.True(a.Proof.ValidateAgainst(work.PhysicalInput,a.Member,a.Endpoint));
                        completions.Add(new{epoch,grants,status="Ready",a.Member.EntityId,a.Member.RequestSequence,a.Member.MobilityRevision,
                            a.Member.CommandSequence,a.Member.StableKey,origin=MatchedLocation(a.Member.Origin),endpoint=MatchedLocation(a.Endpoint),
                            a.Route.Cost,legs=a.Route.Legs.Count,proof=a.Proof.StableKey,proofValid=true,
                            counters=MatchedCounters(work),bookkeeping,classificationGraph,searchGraph=work.RouteCounters.PhysicalGraphWork-classificationGraph,
                            nextMember=active?.Request.Subscription.EntityId,granularity="end of unchanged256 grant; may include next member preparation"});
                    }
                    foreach(var outcome in work.MemberOutcomes){if(seenMembers.Add(outcome.Key))completions.Add(new{epoch,grants,entity=outcome.Key,status=outcome.Value.ToString(),counters=MatchedCounters(work),bookkeeping,classificationGraph});}
                    trace.Add(new{epoch,grants,work.WorkPhase,work.MemberCursor,work.Status,work.AuthorityReservationBytes,
                        counters=MatchedCounters(work),bookkeeping,classificationGraph,searchGraph=work.RouteCounters.PhysicalGraphWork-classificationGraph,
                        demand=MatchedDemand(active),pendingEntity=active?.Request.Subscription.EntityId,assignments=assignments.Count,
                        outcomes=work.MemberOutcomes.Select(p=>new{entity=p.Key,status=p.Value.ToString()}).ToArray()});
                    if(work.Status!=NavArrivalStatus.Pending&&work.Status!=NavArrivalStatus.BudgetExhausted)break;
                    epoch++;
                }
                solveSeconds=clock.Elapsed.TotalSeconds-start;
                report["final"]=new{work.WorkPhase,work.LogicalEpoch,grants,status=work.Status.ToString(),work.MemberCursor,
                    counters=MatchedCounters(work),bookkeeping,classificationGraph,searchGraph=work.RouteCounters.PhysicalGraphWork-classificationGraph,
                    outcomes=work.MemberOutcomes.Select(p=>new{entity=p.Key,status=p.Value.ToString()}).ToArray()};
                Assert.AreEqual(NavArrivalStatus.Ready,work.Status,"Actual full Work terminal");
                Assert.Zero(work.MemberOutcomes.Count);Assert.AreEqual(3,work.Proposals.Count);
                CollectionAssert.AreEquivalent(work.PhysicalInput.Members.Select(m=>m.EntityId),work.Proposals.Select(a=>a.Member.EntityId));
                foreach(var a in work.Proposals)Assert.True(a.Proof.ValidateAgainst(work.PhysicalInput,a.Member,a.Endpoint));
                var prior=JObject.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("SPACEWARS_MATCHED_REFERENCE")));
                foreach(var a in work.Proposals){var reference=prior["result"]["completions"].Single(v=>(long)v["EntityId"]==a.Member.EntityId);
                    Assert.AreEqual((long)reference["RequestSequence"],a.Member.RequestSequence);
                    Assert.AreEqual((double)reference["endpoint"]["x"],a.Endpoint.Position.X);Assert.AreEqual((double)reference["endpoint"]["z"],a.Endpoint.Position.Z);
                    Assert.AreEqual((double)reference["Cost"],a.Route.Cost,"Same-input exact-policy route scalar with preserved full-domain minimum proof");}
                var paid=work.RouteCounters.PhysicalGraphWork+work.RouteCounters.PhysicalFieldWork+work.RouteCounters.ConnectorWork+bookkeeping+work.RouteCounters.MaintenanceWork;
                report["comparison"]=new{sameEndpoints=true,sameRouteCosts=true,minimumQualification="Actual-source independent exact minimum prerequisites + identical finite graph/all Origins/consistent exact A*; previous atomic objectives were not recorded",priorGrants=373,grants,priorPaid=143858,paid,lessPaid=paid<143858,engineeringTarget=grants<=188};
                Assert.LessOrEqual(grants,188,"Engineering target, not gameplay SLA");Assert.Less(paid,143858,"Include all new-policy preparation charges");

            }catch(Exception e){failure=e;report["failure"]=e.ToString();throw;}
            finally{
                work?.ReleaseOwner();inputOwner?.Dispose();report["schema"]="first-army-lazy-matched-group-v1";
                report["classification"]=failure==null?"LAZY_GROUP_COMPONENT_PASS_GameplayNotRun":matched?"FAIL_see_Ready_and_engineering_fields":"CAPTURE_DECODE_NotRun";
                report["exactRoundtripBeforeAdvance"]=matched;report["completions"]=completions;report["recipes"]=recipes.Values.ToArray();
                report["quota"]=new{Allocation=256,Graph=256,Field=256,Connectors=256,Bookkeeping=256,Maintenance=256};
                report["bounds"]=new{grants=4096,wallSeconds=170,nunitMilliseconds=180000};
                report["timing"]=new{decodeSeconds,roundtripSeconds,solveSeconds,totalSeconds=clock.Elapsed.TotalSeconds};
                report["ledger"]=new{budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes,budget.CaptureActive};
                File.WriteAllText(Path.Combine(folder,"lazy-matched-result.json"),JsonConvert.SerializeObject(report,Formatting.Indented));
                File.WriteAllLines(Path.Combine(folder,"lazy-matched-trace.ndjson"),trace.Select(x=>JsonConvert.SerializeObject(x)));
                Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.False(budget.CaptureActive);
            }
        }

        [Test,Timeout(180000)] public void FirstArmyMatchedBodyGroupContinuation()
        {
            string folder=Environment.GetEnvironmentVariable("SPACEWARS_MATCHED_OUTPUT") ?? throw new ArgumentException("Missing output");
            Directory.CreateDirectory(folder);var report=new Dictionary<string,object>();var trace=new List<object>();
            var completions=new List<object>();var recipes=new Dictionary<string,object>();var samples=new Dictionary<string,int>();
            var seenMembers=new HashSet<long>();var budget=new NavigationRetentionBudget();NavigationArrivalWork work=null;
            NavigationCaptureBytes inputOwner=null;Exception failure=null;bool matched=false;long bookkeeping=0,catalogGraph=0;
            int grants=0;var clock=Stopwatch.StartNew();double decodeSeconds=0,roundtripSeconds=0,solveSeconds=0;
            try{
                var body=File.ReadAllBytes(Environment.GetEnvironmentVariable("SPACEWARS_MATCHED_BODY"));
                var side=JObject.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("SPACEWARS_MATCHED_SIDECAR")));
                Assert.AreEqual("1004067cae0e8eb706da766ecd48a00154c3a4fb7f3eab5281a83437aea3752b",MatchedSha(body));
                Assert.AreEqual((string)side["producer"]["bodySHA256"],MatchedSha(body));
                inputOwner=NavigationCaptureBytes.CreateOwned(body.Length,budget,_=>body);
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,7,0,out var decode));
                using(decode){using(var stream=new MemoryStream(inputOwner.Bytes,false))using(var reader=new BinaryReader(stream)){
                    work=NavPhysicalBodyWire.ReadFrom(reader,decode.OwnedReservation()) as NavigationArrivalWork;
                    Assert.NotNull(work);Assert.AreEqual(stream.Length,stream.Position,"EOF");}
                    decode.Attach(work);decode.Commit(work,_=>{});}
                decodeSeconds=clock.Elapsed.TotalSeconds;
                Assert.AreEqual((string)side["snapshot"]["before"]["input"]["StableKey"],work.InputBinding);
                Assert.AreEqual(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.ExactAstarArrivalPolicyV8),((INavPhysicalBody)work).PhysicalTag);
                CollectionAssert.AreEqual(body,MatchedEncode(budget,work,1),"Exact reencode BEFORE any Advance");
                matched=true;roundtripSeconds=clock.Elapsed.TotalSeconds-decodeSeconds;
                Assert.AreEqual(-1,work.LogicalEpoch);Assert.Zero(work.AuthorityReservationBytes);Assert.Zero(work.MemberOutcomes.Count);
                report["bodySha256"]=MatchedSha(body);report["inputBinding"]=work.InputBinding;
                report["initial"]=new{work.WorkPhase,work.LogicalEpoch,work.AuthorityReservationBytes,counters=MatchedCounters(work)};
                long authorityBytes=work.AuthorityReservationBytes,epoch=work.LogicalEpoch+1;double start=clock.Elapsed.TotalSeconds;
                while(grants<4096){
                    Assert.Less(clock.Elapsed.TotalSeconds,170,"Existing diagnostic wall bound");
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Arrival,7,epoch+10,out var operation));
                    NavigationArrivalWork next=null;
                    try{using(operation){next=work.ForkOwned(operation.OwnedReservation());
                        Assert.AreEqual(authorityBytes,next.AuthorityReservationBytes);
                        next.Advance(epoch,256,new NavEpochQuota(256,256,256,256,256));
                        Assert.False(next.LastAdvanceRetentionBlocked,"Admission refusal is not completed work");
                        var scheduler=PrefixScheduler(next);if(scheduler.CaptureEpoch().Epoch==epoch)bookkeeping+=256-scheduler.RemainingQuota.Bookkeeping;
                        var job=MatchedJob(next);
                        if(job!=null){
                            string key=job.Request.Subscription.StableKey;var cached=MatchedField<NavDemandQueryCatalog>(scheduler,"demandCatalog");
                            var shared=job.Demand==null?null:MatchedField<NavDemandQueryCatalog>(job.Demand,"catalog");
                            if(!recipes.ContainsKey(key))recipes[key]=new{firstObservedEpoch=epoch,request=MatchedRequest(job.Request),
                                catalogPresent=cached!=null,catalogCompatible=cached?.Compatible(job.Request),
                                demandAlreadyBorn=job.Demand!=null,sharedCatalog=shared!=null,
                                sample=job.Demand==null?(int?)null:MatchedField<int>(job.Demand,"sample"),
                                indexed=job.Demand?.IndexedCount,cacheHits=scheduler.Counters.CacheHits};
                            if(job.Demand!=null&&shared==null){int raw=MatchedField<int>(job.Demand,"sample");samples.TryGetValue(key,out int prior);
                                Assert.GreaterOrEqual(raw,prior);catalogGraph+=raw-prior;samples[key]=raw;}
                        }
                        next.SealPrepared();operation.Attach(next);operation.Commit(next,_=>{});}
                        work.ReleaseOwner();work=next;next=null;
                    }finally{next?.ReleaseOwner();}
                    grants++;var active=MatchedJob(work);var assignments=MatchedField<NavPagedList<NavArrivalProposal>>(work,"assignments");
                    foreach(var a in assignments){if(!seenMembers.Add(a.Member.EntityId))continue;
                        Assert.True(a.Proof.ValidateAgainst(work.PhysicalInput,a.Member,a.Endpoint));
                        completions.Add(new{epoch,grants,status="Ready",a.Member.EntityId,a.Member.RequestSequence,a.Member.MobilityRevision,
                            a.Member.CommandSequence,a.Member.StableKey,origin=MatchedLocation(a.Member.Origin),endpoint=MatchedLocation(a.Endpoint),
                            a.Route.Cost,legs=a.Route.Legs.Count,proof=a.Proof.StableKey,proofValid=true,
                            counters=MatchedCounters(work),bookkeeping,catalogGraph,searchGraph=work.RouteCounters.PhysicalGraphWork-catalogGraph,
                            nextMember=active?.Request.Subscription.EntityId,granularity="end of unchanged256 grant; may include next member preparation"});
                    }
                    foreach(var outcome in work.MemberOutcomes){if(seenMembers.Add(outcome.Key))completions.Add(new{epoch,grants,entity=outcome.Key,status=outcome.Value.ToString(),counters=MatchedCounters(work),bookkeeping,catalogGraph});}
                    trace.Add(new{epoch,grants,work.WorkPhase,work.MemberCursor,work.Status,work.AuthorityReservationBytes,
                        counters=MatchedCounters(work),bookkeeping,catalogGraph,searchGraph=work.RouteCounters.PhysicalGraphWork-catalogGraph,
                        demand=MatchedDemand(active),pendingEntity=active?.Request.Subscription.EntityId,assignments=assignments.Count,
                        outcomes=work.MemberOutcomes.Select(p=>new{entity=p.Key,status=p.Value.ToString()}).ToArray()});
                    if(grants==188){
                        Assert.AreEqual(46637,work.RouteCounters.PhysicalGraphWork,"Preserved first-assignment prefix");
                        Assert.AreEqual(12110,work.RouteCounters.ConnectorWork);Assert.AreEqual(14030,bookkeeping);
                        Assert.AreEqual(1,assignments.Count);Assert.AreEqual("d407017f653a13c7bc3ce0c2039a7379f446ac4cc1ed5647b77bdf54a4d9d0d6",assignments[0].Proof.StableKey);
                        report["priorFirstAssignmentPrefixExact"]=true;
                    }
                    if(work.Status!=NavArrivalStatus.Pending&&work.Status!=NavArrivalStatus.BudgetExhausted)break;
                    epoch++;
                }
                solveSeconds=clock.Elapsed.TotalSeconds-start;
                report["final"]=new{work.WorkPhase,work.LogicalEpoch,grants,status=work.Status.ToString(),work.MemberCursor,
                    counters=MatchedCounters(work),bookkeeping,catalogGraph,searchGraph=work.RouteCounters.PhysicalGraphWork-catalogGraph,
                    outcomes=work.MemberOutcomes.Select(p=>new{entity=p.Key,status=p.Value.ToString()}).ToArray()};
                Assert.AreEqual(NavArrivalStatus.Ready,work.Status,"Actual full Work terminal");
                Assert.Zero(work.MemberOutcomes.Count);Assert.AreEqual(3,work.Proposals.Count);
                CollectionAssert.AreEquivalent(work.PhysicalInput.Members.Select(m=>m.EntityId),work.Proposals.Select(a=>a.Member.EntityId));
                foreach(var a in work.Proposals)Assert.True(a.Proof.ValidateAgainst(work.PhysicalInput,a.Member,a.Endpoint));
            }catch(Exception e){failure=e;report["failure"]=e.ToString();throw;}
            finally{
                work?.ReleaseOwner();inputOwner?.Dispose();report["schema"]="first-army-matched-group-v1";
                report["classification"]=failure==null?"GROUP_COMPONENT_PASS_GameplayNotRun":matched?"CONTINUATION_FAIL":"CAPTURE_DECODE_NotRun";
                report["exactRoundtripBeforeAdvance"]=matched;report["completions"]=completions;report["recipes"]=recipes.Values.ToArray();
                report["quota"]=new{Allocation=256,Graph=256,Field=256,Connectors=256,Bookkeeping=256,Maintenance=256};
                report["bounds"]=new{grants=4096,wallSeconds=170,nunitMilliseconds=180000};
                report["timing"]=new{decodeSeconds,roundtripSeconds,solveSeconds,totalSeconds=clock.Elapsed.TotalSeconds};
                report["ledger"]=new{budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes,budget.CaptureActive};
                File.WriteAllText(Path.Combine(folder,"matched-group-result.json"),JsonConvert.SerializeObject(report,Formatting.Indented));
                File.WriteAllLines(Path.Combine(folder,"matched-group-trace.ndjson"),trace.Select(x=>JsonConvert.SerializeObject(x)));
                Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.False(budget.CaptureActive);
            }
        }

        [Test,Timeout(180000)] public void FirstArmyMatchedBodyContinuation()
        {
            string folder=Environment.GetEnvironmentVariable("SPACEWARS_MATCHED_OUTPUT") ?? throw new ArgumentException("Missing output");
            Directory.CreateDirectory(folder);var report=new Dictionary<string,object>();var trace=new List<object>();
            var budget=new NavigationRetentionBudget();NavigationArrivalWork work=null;NavigationCaptureBytes inputOwner=null;
            var clock=Stopwatch.StartNew();double decodeSeconds=0,roundtripSeconds=0,solveSeconds=0;long bookkeeping=0,catalogSamples=0;
            int grants=0;long firstEpoch=-1;bool matched=false;Exception failure=null;object firstRequest=null;
            try{
                var body=File.ReadAllBytes(Environment.GetEnvironmentVariable("SPACEWARS_MATCHED_BODY"));
                var side=JObject.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("SPACEWARS_MATCHED_SIDECAR")));
                Assert.AreEqual("1004067cae0e8eb706da766ecd48a00154c3a4fb7f3eab5281a83437aea3752b",MatchedSha(body));
                Assert.AreEqual((string)side["producer"]["bodySHA256"],MatchedSha(body));
                inputOwner=NavigationCaptureBytes.CreateOwned(body.Length,budget,_=>body);
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,7,0,out var decode));
                using(decode){using(var stream=new MemoryStream(inputOwner.Bytes,false))using(var reader=new BinaryReader(stream)){
                    work=NavPhysicalBodyWire.ReadFrom(reader,decode.OwnedReservation()) as NavigationArrivalWork;
                    Assert.NotNull(work,"Expected full physical Work BODY");Assert.AreEqual(stream.Length,stream.Position,"EOF");}
                    decode.Attach(work);decode.Commit(work,_=>{});}
                decodeSeconds=clock.Elapsed.TotalSeconds;
                Assert.AreEqual((string)side["snapshot"]["before"]["input"]["StableKey"],work.InputBinding);
                Assert.AreEqual((long)side["snapshot"]["before"]["LogicalEpoch"],work.LogicalEpoch);
                Assert.AreEqual((string)side["snapshot"]["before"]["input"]["provider"],work.PhysicalInput.Provider.Binding);
                Assert.AreEqual(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.ExactAstarArrivalPolicyV8),
                    ((INavPhysicalBody)work).PhysicalTag,"Preserve decoded exact A* arrival policy");
                var encoded=MatchedEncode(budget,work,1);CollectionAssert.AreEqual(body,encoded,"Exact reencode BEFORE any Advance");
                matched=true;roundtripSeconds=clock.Elapsed.TotalSeconds-decodeSeconds;
                report["bodySha256"]=MatchedSha(body);report["inputBinding"]=work.InputBinding;
                report["physicalTag"]=((INavPhysicalBody)work).PhysicalTag.Encoded;
                report["initial"]=new{work.WorkPhase,work.LogicalEpoch,work.AuthorityReservationBytes,counters=MatchedCounters(work)};
                var initialAssignments=MatchedField<NavPagedList<NavArrivalProposal>>(work,"assignments").Count;
                Assert.Zero(initialAssignments);Assert.Zero(work.MemberOutcomes.Count);Assert.AreEqual(-1,work.LogicalEpoch);
                long authorityBytes=work.AuthorityReservationBytes;long epoch=work.LogicalEpoch+1;double start=clock.Elapsed.TotalSeconds;
                while(grants<4096){
                    Assert.Less(clock.Elapsed.TotalSeconds,170,"Existing diagnostic wall bound");
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Arrival,7,epoch+10,out var operation));
                    NavigationArrivalWork next=null;
                    try{using(operation){next=work.ForkOwned(operation.OwnedReservation());
                        Assert.AreEqual(authorityBytes,next.AuthorityReservationBytes);
                        next.Advance(epoch,256,new NavEpochQuota(256,256,256,256,256));
                        Assert.False(next.LastAdvanceRetentionBlocked,"Admission refusal is not completed work");
                        var scheduler=PrefixScheduler(next);if(scheduler.CaptureEpoch().Epoch==epoch)bookkeeping+=256-scheduler.RemainingQuota.Bookkeeping;
                        var job=MatchedJob(next);
                        if(job!=null&&firstRequest==null){Assert.True(job.Request.DemandQueryV6);firstRequest=MatchedRequest(job.Request);}
                        if(job?.Demand!=null)catalogSamples=Math.Max(catalogSamples,MatchedField<int>(job.Demand,"sample"));
                        next.SealPrepared();operation.Attach(next);operation.Commit(next,_=>{});}
                        work.ReleaseOwner();work=next;next=null;
                    }finally{next?.ReleaseOwner();}
                    grants++;var active=MatchedJob(work);var assignments=MatchedField<NavPagedList<NavArrivalProposal>>(work,"assignments");
                    trace.Add(new{epoch,grants,work.WorkPhase,work.MemberCursor,work.CandidateCursor,
                        work.Status,work.AuthorityReservationBytes,bookkeeping,catalogSamples,counters=MatchedCounters(work),
                        demand=MatchedDemand(active),pendingEntity=active?.Request.Subscription.EntityId,assignments=assignments.Count,
                        outcomes=work.MemberOutcomes.Select(p=>new{entity=p.Key,status=p.Value.ToString()}).ToArray()});
                    if(assignments.Count>0||work.MemberOutcomes.Count>0||work.Status!=NavArrivalStatus.Pending&&work.Status!=NavArrivalStatus.BudgetExhausted){
                        firstEpoch=epoch;
                        if(assignments.Count>0){var a=assignments[0];
                            Assert.True(a.Proof.ValidateAgainst(work.PhysicalInput,a.Member,a.Endpoint));
                            report["firstAssignment"]=new{a.Member.EntityId,a.Member.RequestSequence,a.Member.CommandSequence,
                                a.Member.MobilityRevision,a.Member.StableKey,origin=MatchedLocation(a.Member.Origin),
                                endpoint=MatchedLocation(a.Endpoint),a.Route.Cost,legs=a.Route.Legs.Count,proof=a.Proof.StableKey,
                                proofValid=true,granularity="end of unchanged 256 grant; may include next member work",nextMember=active?.Request.Subscription.EntityId};
                        }
                        break;
                    }
                    epoch++;
                }
                solveSeconds=clock.Elapsed.TotalSeconds-start;
                report["firstRequest"]=firstRequest;report["firstObservedEpoch"]=firstEpoch;report["grants"]=grants;
                report["final"]=new{work.WorkPhase,work.LogicalEpoch,work.MemberCursor,work.Status,
                    counters=MatchedCounters(work),bookkeeping,catalogGraph=catalogSamples,
                    searchGraph=work.RouteCounters.PhysicalGraphWork-catalogSamples,
                    outcomes=work.MemberOutcomes.Select(p=>new{entity=p.Key,status=p.Value.ToString()}).ToArray()};
                Assert.GreaterOrEqual(firstEpoch,0,"No first member result within unchanged diagnostic bound");
                Assert.True(report.ContainsKey("firstAssignment"),"Actual terminal without qualified assignment");
            }catch(Exception e){failure=e;report["failure"]=e.ToString();throw;}
            finally{
                work?.ReleaseOwner();inputOwner?.Dispose();
                report["schema"]="first-army-matched-work-v1";report["classification"]=failure==null?"COMPONENT_PASS_GameplayNotRun":matched?"CONTINUATION_FAIL":"CAPTURE_DECODE_NotRun";
                report["exactRoundtripBeforeAdvance"]=matched;report["uniquePops"]="NotMeasured: no production observation hook";
                report["quota"]=new{Allocation=256,Graph=256,Field=256,Connectors=256,Bookkeeping=256,Maintenance=256};
                report["timing"]=new{decodeSeconds,roundtripSeconds,solveSeconds,totalSeconds=clock.Elapsed.TotalSeconds};
                report["ledger"]=new{budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes,budget.CaptureActive};
                File.WriteAllText(Path.Combine(folder,"matched-result.json"),JsonConvert.SerializeObject(report,Formatting.Indented));
                File.WriteAllLines(Path.Combine(folder,"matched-trace.ndjson"),trace.Select(x=>JsonConvert.SerializeObject(x)));
                Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.False(budget.CaptureActive);
            }
        }
    }
}
