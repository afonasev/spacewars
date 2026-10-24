// Temporary QA source: copy into Tests/EditMode only for the named diagnostic.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Security.Cryptography;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    // BEGIN ACTOR COPY. Needs System, IO, Linq, Reflection, Runtime, Simulation usings.
    // This class also compiles in Spacewars.Input.PlayModeTests: Simulation
    // internals are accessed only through these exact cached reflection APIs.
    internal static class ExactDemandCaptureQa
    {
        private const BindingFlags Instance=BindingFlags.Instance|BindingFlags.NonPublic;
        private static readonly PropertyInfo RecipeRoot=typeof(NavScheduledRequest).GetProperty("StorageRoot",Instance);
        private static readonly MethodInfo Reservation=typeof(NavigationRetentionBudget.Scope).GetMethod("OwnedReservation",Instance);
        private static readonly MethodInfo Write=typeof(NavigationCaptureBytes).GetMethod("Write",BindingFlags.Static|BindingFlags.NonPublic);
        private static readonly MethodInfo BodyWrite=typeof(NavScheduledRequest).Assembly.GetType("Spacewars.Simulation.NavPhysicalBodyWire",true).GetMethod("WriteTo",BindingFlags.Static|BindingFlags.NonPublic);
        private static readonly PropertyInfo WorkMap=typeof(NavigationHostRoot).GetProperty("ArrivalWork",Instance);
        private static readonly FieldInfo Input=typeof(NavigationArrivalWork).GetField("input",Instance),Scheduler=typeof(NavigationArrivalWork).GetField("scheduler",Instance),Jobs=typeof(LayeredNavigationScheduler).GetField("jobs",Instance);
        private static readonly Type JobType=typeof(LayeredNavigationScheduler).GetNestedType("Job",BindingFlags.NonPublic);
        private static readonly PropertyInfo Request=JobType.GetProperty("Request",Instance),Demand=JobType.GetProperty("Demand",Instance);
        private static readonly PropertyInfo DemandStatus=typeof(NavScheduledRequest).Assembly.GetType("Spacewars.Simulation.NavDemandQueryWork",true).GetProperty("Status",Instance);
        private static readonly MethodInfo Release=typeof(NavOwnedStorage).GetMethod("Release",Instance,null,Type.EmptyTypes,null);

        // Call until matched=true; then never call again during this prefix.
        // A matched refusal is ONE capture attempt, not a reason to retry.
        internal static bool TryCurrent(NavigationRoutePort port,NavigationSession session,long group,long command,long[] originalCore,string providerBinding,
            out byte[] body,out object metadata,out string reason)
        {
            body=null;metadata=null;reason="NotCaptured: exact current query not observed";
            NavigationHostRoot pin=null;NavScheduledRequest selected=null;object observed=null;string rejection=null;int pendingCount=0;
            try{
                port.RunHostStep(()=>{
                    pin=port.PinCommittedHostRoot();if(pin==null)return;
                    var map=(System.Collections.Generic.IReadOnlyDictionary<long,NavOwnedStorage>)WorkMap.GetValue(pin);
                    foreach(var pair in map){
                        if(!(pair.Value is NavigationArrivalWork work)||work.MemberCursor!=0)continue;
                        var input=(NavArrivalInput)Input.GetValue(work);var member=SelectMember(input,work.MemberCursor,group,command,originalCore);
                        // Old pre-rebind work is not this capture target.
                        if(member==null||input.Provider.Binding!=providerBinding)continue;
                        var scheduler=(LayeredNavigationScheduler)Scheduler.GetValue(work);
                        foreach(var job in (System.Collections.IEnumerable)Jobs.GetValue(scheduler)){
                            var demand=Demand.GetValue(job);if(demand==null||DemandStatus.GetValue(demand).ToString()!="Pending")continue;
                            var r=(NavScheduledRequest)Request.GetValue(job);var s=r.Subscription;pendingCount++;
                            var binding=input.WorkingFor(member);
                            if(!r.Query||!r.DemandQueryV3||!r.GroupQuery||r.Provider.Binding!=providerBinding||!ReferenceEquals(r.Provider,binding.Provider)||r.Region==null||r.Region.Candidates.Count!=3||!MatchesCurrentMember(s,member)||
                                r.Clearance.Id!=member.Clearance.Id||r.Clearance.Revision!=member.Clearance.Revision||r.Clearance.MobilityClass!=member.Clearance.MobilityClass||r.Clearance.MobilityRevision!=member.Clearance.MobilityRevision||r.Clearance.Radius!=member.Radius||
                                r.Profile.ExactProfileBinding!=binding.Profile.ExactProfileBinding||r.Profile.CostId!=binding.Profile.CostId||r.Profile.CostRevision!=binding.Profile.CostRevision||r.Profile.GridCell!=binding.Profile.GridCell||
                                r.MaxLabels!=input.MaxLabels||r.Limits.MaxSamples!=input.Limits.MaxSamples||r.Limits.MaxNodes!=input.Limits.MaxNodes||r.Limits.MaxArcs!=input.Limits.MaxArcs){rejection="NotCaptured: current pending query tuple/provider/policy mismatch";continue;}
                            selected=r;observed=new{attempt=pair.Key,work.LogicalEpoch,provider=r.Provider.Binding,r.GraphKey,r.CompileKey,r.ArtifactKey,
                                source=input.Source.StableKey,sourceCandidateCount=input.Source.Candidates.Count,region=r.Region.StableKey,regionCandidateCount=r.Region.Candidates.Count,
                                selectionPolicy="current-ordered-core-member-v2",memberCursor=work.MemberCursor,originalCore=originalCore.ToArray(),orderedCore=input.Members.Select(m=>m.EntityId).ToArray(),memberOrder=member.Order,memberKey=member.StableKey,
                                s.WorldGeneration,s.EntityId,s.GroupId,s.RootOrderRevision,s.CommandSequence,s.IssuedTick,s.ActivationRevision,s.MemberOrderRevision,s.RequestSequence,s.Incarnation,s.MobilityRevision,s.HoldBinding,s.Provenance,s.StableKey,
                                origin=new{surface=s.Origin.SurfaceId,s.Origin.Position.X,s.Origin.Position.Z},endpoint=new{surface=s.AssignedEndpoint.SurfaceId,s.AssignedEndpoint.Position.X,s.AssignedEndpoint.Position.Z},
                                radius=r.Clearance.Radius,profile=r.Profile.ExactProfileBinding};
                        }
                    }
                }); // RunHostStep releases EnterTransport BEFORE BODY traversal.
                metadata=observed;if(pendingCount==0)return false;
                if(pendingCount!=1){reason="NotCaptured: ambiguous current pending queries";return true;}
                if(rejection!=null||selected==null){reason=rejection??"NotCaptured: current query selection failed";return true;}
                TryCapture(session,selected,out body,out reason);return true;
            }finally{if(pin!=null)Release.Invoke(pin,null);}
        }

        internal static NavArrivalMember SelectMember(NavArrivalInput input,int cursor,long group,long command,long[] core)
        {
            if(input==null||cursor!=0||core==null||core.Length!=3||core.Distinct().Count()!=3||input.Members.Count!=3||
                input.Members.Any(m=>!core.Contains(m.EntityId)||m.GroupId!=group||m.CommandSequence!=command))return null;
            return input.Members[cursor];
        }
        // Exact constructor mapping from NavigationArrivalWork.CreateRequest.
        internal static bool MatchesCurrentMember(NavRouteSubscription s,NavArrivalMember m)
            =>s.WorldGeneration==m.WorldGeneration&&s.EntityId==m.EntityId&&s.GroupId==m.GroupId&&s.RootOrderRevision==m.RootOrderRevision&&s.CommandSequence==m.CommandSequence&&s.IssuedTick==m.IssuedTick&&
                s.ActivationRevision==m.ActivationRevision&&s.MemberOrderRevision==m.MemberOrderRevision&&s.RequestSequence==m.RequestSequence&&s.Incarnation==m.Incarnation&&s.MobilityRevision==m.MobilityRevision&&
                s.Origin.Equals(m.Origin)&&s.HoldBinding==m.HoldBinding&&s.Provenance==NavRouteProvenance.CommandMember;

        // Select the exact CURRENT recipe and acquire its host pin under normal
        // RunHostStep; then EXIT RunHostStep before calling this method. Keep
        // that immutable pin alive throughout serialization. No file IO/yield.
        internal static bool TryCapture(NavigationSession session,NavScheduledRequest recipe,out byte[] body,out string reason)
            => TryCaptureBudget(session.PhysicalHostBudget,recipe,out body,out reason);

        internal static bool TryCaptureBudget(NavigationRetentionBudget budget,NavScheduledRequest recipe,out byte[] body,out string reason)
        {
            body=null;reason=null;
            if(budget==null||recipe==null||!recipe.DemandQueryV3){reason="NotCaptured: missing ledger or exact Demand V3 recipe";return false;}
            if(!budget.TryOpenScope(NavigationPreparationKind.Capture,recipe.Subscription.EntityId,recipe.Subscription.RequestSequence,out var scope)){
                reason="NotCaptured: Capture scope admission refused";return false;
            }
            NavigationCaptureBytes output=null;
            try{
                using(scope){
                    object reservation=Reservation.Invoke(scope,new object[]{null});
                    var root=(NavOwnedStorage)RecipeRoot.GetValue(recipe);
                    Action<BinaryWriter> write=w=>BodyWrite.Invoke(null,new object[]{w,root,reservation});
                    output=(NavigationCaptureBytes)Write.Invoke(null,new object[]{reservation,write});
                    scope.Attach(output);scope.Commit(output,_=>{});
                }
                body=output.TransferPublic();return true;
            }catch(Exception error){
                while(error is TargetInvocationException wrapped&&wrapped.InnerException!=null)error=wrapped.InnerException;
                reason="NotCaptured: "+error.GetType().FullName+": "+error.Message;return false;
            }finally{output?.Dispose();}
        }
    }
    // END ACTOR COPY. After caller releases the pin and exits RunHostStep,
    // File.WriteAllBytes(path, body). Do not clone body or retry on refusal.

    internal static class ExactDemandCompletionQa
    {
        internal static string Sha(byte[] bytes)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}

        // Bounds are diagnostic stop conditions, never solver/game watchdogs.
        // A fresh scheduler means fresh query execution, not frontier continuation.
        internal static object Run(byte[] body,int maxEpochs=4096,int maxSeconds=180)
        {
            if(maxEpochs<1||maxSeconds<1)throw new ArgumentException("Positive diagnostic limits required.");
            var clock=Stopwatch.StartNew();var budget=new NavigationRetentionBudget();
            NavOwnedStorage recipeRoot=null;LayeredNavigationScheduler scheduler=null;
            int epochs=0;long bookkeeping=0;string outcome="NotRun",reason="diagnostic epoch limit",status="Pending";
            double? cost=null;object[] legs=null;object identity=null;NavSchedulerCounters counters=null;
            double decodeSeconds=0,setupSeconds=0,solveStart=0;
            try{
                if(!budget.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var decode))throw new InvalidOperationException("Decode scope refused");
                using(decode){
                    // body is caller-owned transport; pay its exact length again
                    // while this isolated decoder reads it. No second array copy.
                    using(NavStorageScratch.Reserve(decode.OwnedReservation(),body.Length+512L))
                    using(var stream=new MemoryStream(body,false))using(var reader=new BinaryReader(stream)){
                        recipeRoot=NavPhysicalBodyWire.ReadFrom(reader,decode.OwnedReservation());
                        if(stream.Position!=stream.Length)throw new ArgumentException("Trailing exact recipe BODY bytes");
                    }
                    decode.Attach(recipeRoot);decode.Commit(recipeRoot,_=>{});
                }
                decodeSeconds=clock.Elapsed.TotalSeconds;
                var request=(recipeRoot as NavDeclarationStorage)?.Facade as NavScheduledRequest;
                if(request==null||!request.DemandQueryV3||!request.Query||request.Singleton)throw new ArgumentException("Expected exact Demand V3 query recipe BODY");
                identity=new{provider=request.Provider.Binding,request.GraphKey,request.CompileKey,request.ArtifactKey,
                    request.Query,request.GroupQuery,request.DemandQuery,request.DemandQueryV2,request.DemandQueryV3,
                    clearance=new{request.Clearance.Id,request.Clearance.Revision,request.Clearance.MobilityClass,request.Clearance.MobilityRevision,request.Clearance.Radius},
                    profile=new{request.Profile.ExactProfileBinding,request.Profile.CostId,request.Profile.CostRevision,request.Profile.GridCell},
                    limits=new{request.Limits.MaxSamples,request.Limits.MaxNodes,request.Limits.MaxArcs,request.MaxLabels},
                    subscription=request.Subscription.StableKey,origin=Location(request.Subscription.Origin),endpoint=Location(request.Subscription.AssignedEndpoint),
                    region=new{request.Region.StableKey,requested=Location(request.Region.Requested),candidates=request.Region.Candidates.Select(Location).ToArray()}};
                if(!budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var admission))throw new InvalidOperationException("Scheduler admission scope refused");
                using(admission){
                    scheduler=LayeredNavigationScheduler.CreateOwned(admission.OwnedReservation());scheduler.SessionOwnsProviders=true;
                    if(!scheduler.Submit(request))throw new InvalidOperationException("Exact recipe Submit refused");
                    scheduler.SealPrepared();admission.Attach(scheduler);admission.Commit(scheduler,_=>{});
                }
                solveStart=clock.Elapsed.TotalSeconds;setupSeconds=solveStart-decodeSeconds;
                for(int epoch=0;epoch<maxEpochs;epoch++){
                    if(clock.Elapsed.TotalSeconds>=maxSeconds){reason="diagnostic wall limit";break;}
                    if(!budget.TryOpenScope(NavigationPreparationKind.Solve,1,epoch,out var scope)){reason="Solve scope admission refused";break;}
                    LayeredNavigationScheduler next=null;
                    try{
                        using(scope){
                            next=scheduler.ForkOwned(scope.OwnedReservation());
                            next.Advance(epoch,new NavEpochQuota(256,256,256,256,256),_=>true);
                            bookkeeping+=256-next.RemainingQuota.Bookkeeping;epochs++;
                            if(next.TryTake(_=>true,out var completion)){
                                try{status=completion.Status.ToString();outcome="Terminal";reason=null;
                                    if(completion.Route!=null){cost=completion.Route.Cost;legs=completion.Route.Legs.Select(l=>(object)new{from=Location(l.From),to=Location(l.To),l.Cost,kind=l.Kind.ToString(),l.PortalId}).ToArray();}
                                }finally{completion.ReleaseOwner();}
                            }
                            next.SealPrepared();scope.Attach(next);scope.Commit(next,_=>{});
                        }
                        scheduler.ReleaseOwner();scheduler=next;next=null;
                    }finally{next?.ReleaseOwner();}
                    counters=scheduler.Counters;
                    if(outcome=="Terminal")break;
                    if(scheduler.RetentionBlocked){reason="scheduler RetentionBlocked";break;}
                }
            }catch(Exception error){outcome="NotRun";reason=error.GetType().FullName+": "+error.Message;}
            finally{scheduler?.ReleaseOwner();recipeRoot?.ReleaseOwner();}
            string reachability=outcome!="Terminal"?"NotEstablished":status=="Ready"?"Reachable":status=="UnreachableInGraph"?"UnreachableInGraph":"NotEstablished";
            return new{schema="exact-demand-completion-qa-v1",snapshotSha256=Sha(body),identity,outcome,reason,status,reachability,cost,legs,epochs,
                diagnosticLimit=new{maxEpochs,maxSeconds},quota=new{Graph=256,Field=256,Connectors=256,Bookkeeping=256,Maintenance=256},
                work=new{Graph=counters?.PhysicalGraphWork,Field=counters?.PhysicalFieldWork,Connectors=counters?.ConnectorWork,Bookkeeping=bookkeeping,Maintenance=counters?.MaintenanceWork},
                timing=new{decodeSeconds,setupSeconds,solveSeconds=solveStart==0?0:clock.Elapsed.TotalSeconds-solveStart,totalSeconds=clock.Elapsed.TotalSeconds},budget.PeakRetainedBytes,remainingPhysical=budget.PhysicalBytes,remainingHeld=budget.HeldBytes,remainingScopes=budget.ActiveScopes};
        }
        private static object Location(NavLocation at)=>new{at.SurfaceId,at.Position.X,at.Position.Z};
    }

    internal static class ExactDemandControlledQa
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static readonly FieldInfo Jobs=typeof(LayeredNavigationScheduler).GetField("jobs",Hidden);
        private static readonly Type WorkType=typeof(NavScheduledRequest).Assembly.GetType("Spacewars.Simulation.NavDemandQueryWork",true);
        private static object Field(object value,string name)=>value?.GetType().GetField(name,Hidden)?.GetValue(value);
        private static int Count(object value)=>value==null?0:(int)value.GetType().GetProperty("Count",Hidden).GetValue(value);
        private sealed class State
        {
            public string phase;public int sample,indexed,validNodes,origin,terminal,seed,current,direction,candidate,reconstruct;
            public int labels,predecessors,frontier,starts,ends,hcache,seamCursor,seamBoxes,boundNode,boundCursor;
        }
        private static State Observe(LayeredNavigationScheduler scheduler)
        {
            foreach(var job in (System.Collections.IEnumerable)Jobs.GetValue(scheduler)){
                var w=job.GetType().GetProperty("Demand",Hidden).GetValue(job);if(w==null)continue;
                int I(string name)=>(int)Field(w,name);
                return new State{phase=Field(w,"phase").ToString(),sample=I("sample"),indexed=I("indexedCount"),validNodes=(int)WorkType.GetProperty("SampleCount",Hidden).GetValue(w),origin=I("origin"),terminal=I("terminal"),seed=I("seed"),current=I("current"),direction=I("direction"),candidate=I("candidate"),reconstruct=I("reconstruct"),labels=Count(Field(w,"exactDistance")??Field(w,"distance")),predecessors=Count(Field(w,"previous")),frontier=(int)WorkType.GetProperty("FrontierCount",Hidden).GetValue(w),starts=Count(Field(w,"starts")),ends=Count(Field(w,"ends")),hcache=Count(Field(w,"nodeBounds")),seamCursor=I("seamCursor"),seamBoxes=Count(Field(w,"seamBoxes")),boundNode=I("boundNode"),boundCursor=I("boundCursor")};
            }
            return null;
        }
        private static object Location(NavLocation at)=>new{at.SurfaceId,at.Position.X,at.Position.Z};
        private static object Identity(NavScheduledRequest r)=>new{
            provider=r.Provider.Binding,r.GraphKey,r.Singleton,r.Query,r.GroupQuery,r.DemandQuery,r.DemandQueryV2,r.DemandQueryV3,
            clearance=new{r.Clearance.Id,r.Clearance.Revision,r.Clearance.MobilityClass,r.Clearance.MobilityRevision,r.Clearance.Radius},
            profile=new{r.Profile.ExactProfileBinding,r.Profile.CostId,r.Profile.CostRevision,r.Profile.GridCell},
            limits=new{r.Limits.MaxSamples,r.Limits.MaxNodes,r.Limits.MaxArcs,r.MaxLabels},
            subscription=new{r.Subscription.StableKey,r.Subscription.WorldGeneration,r.Subscription.EntityId,r.Subscription.GroupId,r.Subscription.RootOrderRevision,r.Subscription.CommandSequence,r.Subscription.IssuedTick,r.Subscription.ActivationRevision,r.Subscription.MemberOrderRevision,r.Subscription.RequestSequence,r.Subscription.Incarnation,r.Subscription.MobilityRevision,r.Subscription.HoldBinding,r.Subscription.Provenance,origin=Location(r.Subscription.Origin),endpoint=Location(r.Subscription.AssignedEndpoint)},
            region=new{r.Region.StableKey,r.Region.Id,r.Region.Revision,requested=Location(r.Region.Requested),candidates=r.Region.Candidates.Select(Location).ToArray()},
            surfaceWeights=r.Provider.Surfaces.Select(s=>new{s.Id,s.DistanceCost}).ToArray()};
        internal static object Run(byte[] body,bool v4,bool v5=false,bool v6=false)
        {
            const int maxEpochs=4096,maxSeconds=180;
            var clock=Stopwatch.StartNew();var budget=new NavigationRetentionBudget();NavOwnedStorage decodedRoot=null;NavScheduledRequest fresh=null;LayeredNavigationScheduler scheduler=null;
            var trace=new System.Collections.Generic.List<object>();int epochs=0,maxLabelsObserved=0,maxHcacheObserved=0,maxSample=0,maxIndexed=0;
            long bk=0,maintenance=0;double decodeSeconds=0,setupSeconds=0,solveStart=0,bodySeconds=0,cleanupSeconds=0;
            string outcome="NotRun",reason="diagnostic epoch limit",status="Pending";double? cost=null;object[] legs=null;object identity=null,copy=null;NavSchedulerCounters counters=null;State last=null;
            try{
                if(body.Length>WorldWire.MaxBytes||LayeredNavigationScheduler.MaxRetainedBytes!=64L*1024*1024)throw new ArgumentException("Wire/session cap contradiction");
                if(!budget.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var decode))throw new InvalidOperationException("Decode scope refused");
                using(decode){using(NavStorageScratch.Reserve(decode.OwnedReservation(),body.Length+512L))using(var stream=new MemoryStream(body,false))using(var reader=new BinaryReader(stream)){
                    decodedRoot=NavPhysicalBodyWire.ReadFrom(reader,decode.OwnedReservation());if(stream.Position!=stream.Length)throw new ArgumentException("Trailing BODY bytes");}
                    decode.Attach(decodedRoot);decode.Commit(decodedRoot,_=>{});}
                decodeSeconds=clock.Elapsed.TotalSeconds;
                var source=(decodedRoot as NavDeclarationStorage)?.Facade as NavScheduledRequest;
                if(source==null||!source.DemandQueryV3||source.DemandQueryV4||!source.GroupQuery||source.Singleton||source.Region.Candidates.Count!=3||source.Subscription.EntityId!=10||!source.Provider.Binding.StartsWith("031f",StringComparison.Ordinal))throw new ArgumentException("Wrong exact bb7 V3 Entity10/provider031f/fullC3 BODY");
                identity=Identity(source);
                if(!budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var admission))throw new InvalidOperationException("Fresh owned policy setup scope refused");
                using(admission){var r=admission.OwnedReservation();
                    fresh=NavScheduledRequest.CreateOwned(source.Provider,source.Clearance,source.Profile,source.Limits,source.Region,source.Subscription,source.MaxLabels,r,singleton:source.Singleton,query:source.Query,groupQuery:source.GroupQuery,demandQuery:source.DemandQuery,demandQueryV2:source.DemandQueryV2,demandQueryV3:source.DemandQueryV3,demandQueryV4:v4,demandQueryV5:v5,demandQueryV6:v6);
                    bool sameReferences=ReferenceEquals(source.Provider,fresh.Provider)&&ReferenceEquals(source.Clearance,fresh.Clearance)&&ReferenceEquals(source.Profile,fresh.Profile)&&ReferenceEquals(source.Limits,fresh.Limits)&&ReferenceEquals(source.Region,fresh.Region);
                    bool sameIdentity=Newtonsoft.Json.Linq.JToken.DeepEquals(Newtonsoft.Json.Linq.JToken.FromObject(identity),Newtonsoft.Json.Linq.JToken.FromObject(Identity(fresh)));
                    if(!sameReferences||!sameIdentity||fresh.Subscription.StableKey!=source.Subscription.StableKey||fresh.GraphKey!=source.GraphKey||fresh.DemandQueryV4!=v4||fresh.DemandQueryV5!=v5||fresh.DemandQueryV6!=v6||(!v4&&!v5&&(fresh.CompileKey!=source.CompileKey||fresh.ArtifactKey!=source.ArtifactKey))||((v4||v5)&&(fresh.CompileKey==source.CompileKey||fresh.ArtifactKey==source.ArtifactKey)))throw new ArgumentException("New policy copy changed an exact input beyond policy/compile/artifact keys");
                    copy=new{sameReferences,sameIdentity,sourceCompile=source.CompileKey,sourceArtifact=source.ArtifactKey,fresh.CompileKey,fresh.ArtifactKey,fresh.DemandQueryV4,fresh.DemandQueryV5,fresh.DemandQueryV6,metricContract=fresh.MetricContract.ToString(),ownedMetadataFromDecodedClosure=true,freshSchedulerNoFrontierResume=true};
                    scheduler=LayeredNavigationScheduler.CreateOwned(r);scheduler.SessionOwnsProviders=true;if(!scheduler.Submit(fresh))throw new InvalidOperationException("Fresh request Submit refused");scheduler.SealPrepared();admission.Attach(scheduler);admission.Commit(scheduler,_=>{});
                }
                solveStart=clock.Elapsed.TotalSeconds;setupSeconds=solveStart-decodeSeconds;
                for(int epoch=0;epoch<maxEpochs;epoch++){
                    if(clock.Elapsed.TotalSeconds>=maxSeconds){reason="diagnostic wall limit";break;}
                    State before=Observe(scheduler);var previous=scheduler.Counters;
                    if(!budget.TryOpenScope(NavigationPreparationKind.Solve,1,epoch,out var scope)){reason="Solve scope admission refused";break;}
                    LayeredNavigationScheduler next=null;
                    try{using(scope){next=scheduler.ForkOwned(scope.OwnedReservation());
                        // One full fresh epoch, no maxSteps override or refill.
                        next.Advance(epoch,new NavEpochQuota(256,256,256,256,256),_=>true);
                        var remaining=next.RemainingQuota;long paidBk=256-remaining.Bookkeeping;bk+=paidBk;maintenance+=256-remaining.Maintenance;epochs++;
                        State after=Observe(next);var count=next.Counters;
                        foreach(var at in new[]{before,after})if(at!=null){last=at;maxLabelsObserved=Math.Max(maxLabelsObserved,at.labels);maxHcacheObserved=Math.Max(maxHcacheObserved,at.hcache);maxSample=Math.Max(maxSample,at.sample);maxIndexed=Math.Max(maxIndexed,at.indexed);}
                        trace.Add(new{epoch,before,after,paid=new{Graph=count.PhysicalGraphWork-previous.PhysicalGraphWork,Connectors=count.ConnectorWork-previous.ConnectorWork,Field=count.PhysicalFieldWork-previous.PhysicalFieldWork,Bookkeeping=paidBk,Maintenance=256-remaining.Maintenance},remaining=new{remaining.Graph,remaining.Field,remaining.Connectors,remaining.Bookkeeping,remaining.Maintenance}});
                        if(next.TryTake(_=>true,out var done)){try{status=done.Status.ToString();outcome="Terminal";reason=null;if(done.Route!=null){cost=done.Route.Cost;legs=done.Route.Legs.Select(l=>(object)new{from=Location(l.From),to=Location(l.To),l.Cost,kind=l.Kind.ToString(),l.PortalId}).ToArray();}}finally{done.ReleaseOwner();}}
                        next.SealPrepared();scope.Attach(next);scope.Commit(next,_=>{});}
                        scheduler.ReleaseOwner();scheduler=next;next=null;
                    }finally{next?.ReleaseOwner();}
                    counters=scheduler.Counters;if(outcome=="Terminal")break;if(scheduler.RetentionBlocked){reason="scheduler RetentionBlocked";break;}
                }
                bodySeconds=solveStart==0?0:clock.Elapsed.TotalSeconds-solveStart;
            }catch(Exception error){outcome="NotRun";reason=error.GetType().FullName+": "+error.Message;bodySeconds=solveStart==0?0:clock.Elapsed.TotalSeconds-solveStart;}
            finally{double start=clock.Elapsed.TotalSeconds;scheduler?.ReleaseOwner();fresh?.StorageRoot.ReleaseOwner();decodedRoot?.ReleaseOwner();cleanupSeconds=clock.Elapsed.TotalSeconds-start;}
            return new{mode=v6?"DemandV6":v5?"DemandV5":v4?"DemandV4":"DemandV3",snapshotSha256=ExactDemandCompletionQa.Sha(body),identity,copy,outcome,reason,status,reachability=outcome!="Terminal"?"NotEstablished":status=="Ready"?"Reachable":status=="UnreachableInGraph"?"UnreachableInGraph":"NotEstablished",cost,legs,epochs,
                limits=new{maxEpochs,maxSeconds,sessionBytes=LayeredNavigationScheduler.MaxRetainedBytes,wireBytes=WorldWire.MaxBytes},quota=new{Graph=256,Field=256,Connectors=256,Bookkeeping=256,Maintenance=256},
                work=new{Graph=counters?.PhysicalGraphWork,Connectors=counters?.ConnectorWork,Field=counters?.PhysicalFieldWork,Bookkeeping=bk,Maintenance=maintenance,PeakFrontier=counters?.PeakFrontier,nodeExpansions="Unknown: no runtime total",reopens="Unknown: no runtime total",maxLabelsObservedLowerBound=maxLabelsObserved,maxHcacheObservedLowerBound=maxHcacheObserved,lastPending=last},
                phases=new{samplingGraphExactWhenComplete=maxSample,indexedNodesObserved=maxIndexed,note="Per-epoch raw scalar before/after and paid vectors below. Catalog pipelines sampling/index/origins/terminals. Seam and h-reduction may share epochs with search. Exact sublane totals beyond exposed cursors are Unknown; do not attribute a mixed epoch to one phase."},trace,
                timing=new{decodeSeconds,setupSeconds,bodySeconds,cleanupSeconds,totalSeconds=clock.Elapsed.TotalSeconds},budget.PeakRetainedBytes,remainingPhysical=budget.PhysicalBytes,remainingHeld=budget.HeldBytes,remainingScopes=budget.ActiveScopes,captureActive=budget.CaptureActive};
        }
    }

    public sealed partial class NavigationPhysicalBodyTests
    {
        [TestCase(10)][TestCase(14)] public void ExactDemandSelectorUsesCurrentOrderedMemberAndFullTuple(int firstEntity)
        {
            var raw=V3Request();var s=raw.Subscription;var core=new long[]{10,12,14};
            var members=core.Select(id=>new NavArrivalMember(id,2,id==firstEntity?0:1,3,s.Origin,raw.Clearance,s.WorldGeneration,s.GroupId,s.RootOrderRevision,s.CommandSequence,s.IssuedTick,s.ActivationRevision,s.MemberOrderRevision,100+id,s.HoldBinding)).ToArray();
            var input=new NavArrivalInput("selector",raw.Profile.ExactProfileBinding,raw.Provider,raw.Profile,new NavigationProfile(),raw.Limits,
                new NavArrivalCandidateSource("selector",1,raw.Region.Requested,raw.Region.Candidates),members,new NavArrivalOccupancySnapshot(s.WorldGeneration,Array.Empty<NavArrivalOccupant>()),raw.MaxLabels,independentMembers:true);
            var m=ExactDemandCaptureQa.SelectMember(input,0,s.GroupId,s.CommandSequence,core);Assert.NotNull(m);Assert.AreEqual(firstEntity,m.EntityId);
            Assert.Null(ExactDemandCaptureQa.SelectMember(input,1,s.GroupId,s.CommandSequence,core));Assert.Null(ExactDemandCaptureQa.SelectMember(input,0,s.GroupId,s.CommandSequence+1,core));Assert.Null(ExactDemandCaptureQa.SelectMember(input,0,s.GroupId,s.CommandSequence,new long[]{10,12,99}));
            NavRouteSubscription Q(long request,long root,long incarnation,long mobility)=>new NavRouteSubscription(m.WorldGeneration,m.GroupId,root,m.CommandSequence,m.IssuedTick,m.ActivationRevision,m.EntityId,incarnation,m.MemberOrderRevision,mobility,request,m.Origin,raw.Subscription.AssignedEndpoint,m.HoldBinding,NavRouteProvenance.CommandMember);
            Assert.True(ExactDemandCaptureQa.MatchesCurrentMember(Q(m.RequestSequence,m.RootOrderRevision,m.Incarnation,m.MobilityRevision),m));
            Assert.False(ExactDemandCaptureQa.MatchesCurrentMember(Q(m.RequestSequence+1,m.RootOrderRevision,m.Incarnation,m.MobilityRevision),m));
            Assert.False(ExactDemandCaptureQa.MatchesCurrentMember(Q(m.RequestSequence,m.RootOrderRevision+1,m.Incarnation,m.MobilityRevision),m));
            Assert.False(ExactDemandCaptureQa.MatchesCurrentMember(Q(m.RequestSequence,m.RootOrderRevision,m.Incarnation+1,m.MobilityRevision),m));
            Assert.False(ExactDemandCaptureQa.MatchesCurrentMember(Q(m.RequestSequence,m.RootOrderRevision,m.Incarnation,m.MobilityRevision+1),m));
        }

        [TestCase(false)][TestCase(true)] public void ExactDemandHelperExternalRecipeClosurePreflight(bool stopEarly)
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));
            NavScheduledRequest recipe;
            using(scope){
                var r=scope.OwnedReservation();var raw=V3Request();
                if(stopEarly)raw=new NavScheduledRequest(raw.Provider,raw.Clearance,new NavGraphProfile(raw.Profile.ExactProfileBinding,raw.Profile.CostId,raw.Profile.CostRevision,4),raw.Limits,raw.Region,raw.Subscription,raw.MaxLabels,query:true,demandQuery:true,demandQueryV2:true,demandQueryV3:true);
                var provider=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,r);
                var metadata=NavGraphMetadata.CopyUnowned(raw.Clearance,raw.Profile,raw.Limits,r);var region=NavTerminalRegion.CopyOwned(raw.Region,r);
                recipe=NavScheduledRequest.CreateOwned(provider,metadata.Clearance,metadata.Profile,metadata.Limits,region,raw.Subscription,raw.MaxLabels,r,query:true,demandQuery:true,demandQueryV2:true,demandQueryV3:true);
                provider.StorageRoot.ReleaseOwner();metadata.ReleaseOwner();region.StorageRoot.ReleaseOwner();scope.Attach(recipe.StorageRoot);scope.Commit(recipe.StorageRoot,_=>{});
            }
            try{
                Assert.NotNull(((NavDeclarationStorage)recipe.StorageRoot).Third,"exercise external metadata owner path");
                Assert.True(ExactDemandCaptureQa.TryCaptureBudget(budget,recipe,out var body,out var reason),reason);
                var json=Newtonsoft.Json.Linq.JObject.FromObject(ExactDemandCompletionQa.Run(body,stopEarly?1:256,30));
                Assert.AreEqual(stopEarly?"NotRun":"Terminal",(string)json["outcome"],json.ToString());Assert.AreEqual(stopEarly?"Pending":"Ready",(string)json["status"]);
                Assert.AreEqual(stopEarly?"NotEstablished":"Reachable",(string)json["reachability"]);
                if(stopEarly)Assert.AreEqual("diagnostic epoch limit",(string)json["reason"]);
                Assert.AreEqual(recipe.Provider.Binding,(string)json["identity"]["provider"]);Assert.AreEqual(recipe.Region.StableKey,(string)json["identity"]["region"]["StableKey"]);
                Assert.Greater((long)json["work"]["Graph"],0,"must exercise Demand, not direct shortcut");
                Assert.AreEqual(0,(long)json["remainingPhysical"]);Assert.AreEqual(0,(long)json["remainingHeld"]);Assert.AreEqual(0,(long)json["remainingScopes"]);
                var foreign=new NavigationRetentionBudget();Assert.False(ExactDemandCaptureQa.TryCaptureBudget(foreign,recipe,out var rejected,out reason));Assert.Null(rejected);Assert.That(reason,Does.StartWith("NotCaptured:"));
                Assert.Zero(foreign.PhysicalBytes);Assert.Zero(foreign.HeldBytes);Assert.Zero(foreign.ActiveScopes);
                TestContext.WriteLine(json.ToString(Formatting.None));
            }finally{recipe.StorageRoot.ReleaseOwner();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.False(budget.CaptureActive);
        }

        [Test] public void ExactDemandControlledV3V4SameBody()
        {
            string path=Environment.GetEnvironmentVariable("SPACEWARS_EXACT_DEMAND_BODY"),output=Environment.GetEnvironmentVariable("SPACEWARS_EXACT_DEMAND_OUT"),expected=Environment.GetEnvironmentVariable("SPACEWARS_EXACT_DEMAND_SHA256");
            Assert.That(path,Is.Not.Null.And.Not.Empty);Assert.That(output,Is.Not.Null.And.Not.Empty);Assert.AreEqual("2919bcd4c50bc60d0ae3b9de17a0d3f92a8373b2ff8b1fff30d11fc2e386c7d0",expected);
            var info=new FileInfo(path);Assert.That(info.Length,Is.InRange(1,WorldWire.MaxBytes));byte[] body=File.ReadAllBytes(path);Assert.AreEqual(expected,ExactDemandCompletionQa.Sha(body));
            var v3=Newtonsoft.Json.Linq.JObject.FromObject(ExactDemandControlledQa.Run(body,false));
            var v4=Newtonsoft.Json.Linq.JObject.FromObject(ExactDemandControlledQa.Run(body,true));
            var result=new Newtonsoft.Json.Linq.JObject{{"schema","exact-demand-controlled-v3-v4-v1"},{"bodySha256",expected},{"inputBoundary","Different bb7 Entity10/geometry8; fresh exact recipes, not last Army14/600 continuation, not gameplay acceptance"},{"v3",v3},{"v4",v4}};
            result["sameSemanticIdentity"]=Newtonsoft.Json.Linq.JToken.DeepEquals(v3["identity"],v4["identity"]);
            result["historicalV3Crosscheck"]=new Newtonsoft.Json.Linq.JObject{{"source","12a historical independent run only"},{"cost",169.02135658930808},{"epochs",303},{"Graph",77113},{"Connectors",31553},{"Bookkeeping",14000},{"matchesCost",(double?)v3["cost"]==169.02135658930808}};
            File.WriteAllText(output,result.ToString(Formatting.Indented));
            foreach(var mode in new[]{v3,v4}){Assert.AreEqual(0,(long)mode["remainingPhysical"]);Assert.AreEqual(0,(long)mode["remainingHeld"]);Assert.AreEqual(0,(long)mode["remainingScopes"]);Assert.False((bool)mode["captureActive"]);Assert.AreEqual("Terminal",(string)mode["outcome"],mode.ToString());}
            Assert.True((bool)result["sameSemanticIdentity"]);TestContext.WriteLine("CONTROLLED_DEMAND output="+output+" V3="+v3["status"]+"/"+v3["epochs"]+" V4="+v4["status"]+"/"+v4["epochs"]);
        }

        [Test] public void ExactDemandBodyToTerminal()
        {
            string path=Environment.GetEnvironmentVariable("SPACEWARS_EXACT_DEMAND_BODY"),output=Environment.GetEnvironmentVariable("SPACEWARS_EXACT_DEMAND_OUT"),expected=Environment.GetEnvironmentVariable("SPACEWARS_EXACT_DEMAND_SHA256");
            Assert.That(path,Is.Not.Null.And.Not.Empty);Assert.That(output,Is.Not.Null.And.Not.Empty);Assert.That(expected,Is.Not.Null.And.Not.Empty);
            var info=new FileInfo(path);Assert.That(info.Length,Is.InRange(1,WorldWire.MaxBytes));var body=File.ReadAllBytes(path);Assert.AreEqual(expected,ExactDemandCompletionQa.Sha(body));
            var result=Newtonsoft.Json.Linq.JObject.FromObject(ExactDemandCompletionQa.Run(body));File.WriteAllText(output,result.ToString(Formatting.Indented));
            Assert.AreEqual("Terminal",(string)result["outcome"],result.ToString());
            Assert.AreEqual(0,(long)result["remainingPhysical"]);Assert.AreEqual(0,(long)result["remainingHeld"]);Assert.AreEqual(0,(long)result["remainingScopes"]);
            TestContext.WriteLine(result.ToString(Formatting.None));
        }
    }
}
