#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Observation only. No native root/facade is a field or an output operand.
    // Copy operations are capped; last-pin Release may still destroy an old DAG.
    internal sealed class StableGuardPendingReadback
    {
        const int AttemptLimit=32,MemberLimit=4,MatchLimit=4,GrantLimit=8,BuildingLimit=16,SampleLimit=900;
        readonly int thread=Thread.CurrentThread.ManagedThreadId;
        long generation,sequence,action,group,issuance,revision;
        int unit;
        internal bool Bound=>sequence>0;
        internal int Errors,Missing,Truncated,Samples,PinsAcquired,PinsReleased;
        internal long MaxCopyTicks,MaxReleaseTicks;
        internal string FirstError;
        void Need(bool valid,string reason,bool missing=false,bool truncated=false)
        {
            if(valid)return;if(missing)Missing++;if(truncated)Truncated++;throw new InvalidOperationException(reason);
        }
        internal void Bind(long gen,int patient,int expectedTower,int actualTower,PlayableOwner owner,PlayableEntitySnapshot actor,PlayableAiTraceRecord receipt,long clientSequence,long clientAction)
        {
            Need(Thread.CurrentThread.ManagedThreadId==thread,"Native probe binding on foreign thread");
            var stamp=actor?.OrderStamp;
            Need(actor!=null&&actor.Id==patient&&actor.Owner==owner&&stamp!=null,"Missing accepted Repair actor/stamp",missing:true);
            Need(receipt!=null&&receipt.Policy==AiCombatMicroPolicy.Policy&&receipt.Kind==PlayableCommandKind.Move&&receipt.Status==PlayableAiDeliveryStatus.Applied&&receipt.RuntimeStatus==PlayableCommandStatus.Applied&&
                receipt.ReceiptIdentity?.Generation==gen&&receipt.ReceiptIdentity.OwnerId=="player-1"&&receipt.SourceIdentity==PlayableAiOpeningComposition.SourceIdentity&&actualTower==expectedTower,
                "Missing exact Applied Repair receipt/tower binding",missing:true);
            Need(stamp.Generation==gen&&stamp.UnitId==patient&&stamp.Owner==owner&&stamp.Origin==PlayableOrderOrigin.Ai&&stamp.Kind==PlayableCommandKind.Move&&stamp.Source==receipt.SourceIdentity&&
                stamp.Sequence==receipt.CommandSequence&&stamp.ActionId==receipt.ActionId&&stamp.JobId==receipt.ActionId&&clientSequence==stamp.Sequence&&clientAction==stamp.ActionId,
                "Accepted Repair stamp/client/receipt mismatch");
            var pending=actor.Queue?.Pending;var active=actor.Queue?.Active;
            var native=pending?.CommandSequence==stamp.Sequence&&pending.AiActionId==stamp.ActionId?pending:active?.CommandSequence==stamp.Sequence&&active.AiActionId==stamp.ActionId?active:null;
            Need(native!=null&&native.Kind==PlayableCommandKind.Move&&native.GroupId>0&&native.IssuanceId>0,"Missing native accepted Repair group/issuance",missing:true);
            if(Bound){Need(generation==gen&&unit==patient&&sequence==stamp.Sequence&&action==stamp.ActionId&&group==native.GroupId&&issuance==native.IssuanceId,"Repair watch identity changed");return;}
            generation=gen;unit=patient;sequence=stamp.Sequence;action=stamp.ActionId;group=native.GroupId;issuance=native.IssuanceId;revision=native.ActivationRevision;
        }
        internal object Capture(PlayableDomain domain,CertifiedRouteWorker worker,PlayableAiObservation view)
        {
            try{return Read(domain,worker,view);}
            catch(Exception error){Errors++;if(FirstError==null)FirstError=error.Message.Length>256?error.Message.Substring(0,256):error.Message;return new{Valid=false,Errors,Missing,Truncated,Error=FirstError};}
        }
        object Read(PlayableDomain domain,CertifiedRouteWorker worker,PlayableAiObservation view)
        {
            Need(Thread.CurrentThread.ManagedThreadId==thread,"Native probe must run on owned post-frame thread");
            if(!Bound)return new{Valid=true,TargetBound=false,ObservationTick=view.Tick};
            Need(Samples<SampleLimit,"Native probe sample bound exceeded",truncated:true);Samples++;
            Need(view.Generation==generation&&view.OwnerId=="player-1","Native probe observation owner/generation changed");
            Need(view.Buildings.Count<=BuildingLimit,"Observed building bound exceeded",truncated:true);
            var buildings=new object[view.Buildings.Count];
            for(int i=0;i<buildings.Length;i++){var b=view.Buildings[i];buildings[i]=new{b.Id,Owner=(int)b.Owner,Kind=(int)b.Kind,Phase=(int)b.Phase,b.Health,X=b.Position.X,Z=b.Position.Z};}
            var publication=worker?.Publication;Need(publication!=null,"Missing immutable worker publication",missing:true);
            var counters=publication.Counters;Need(counters!=null,"Missing immutable worker counters",missing:true);
            int domainGeometry=domain.Geometry.Revision;long worldTick=domain.Tick;
            // ONLY called after successful Authority.TryAdvance, never in Review/Apply.
            var root=domain.Navigation.RoutePort.PinCommittedHostRoot();if(root!=null)PinsAcquired++;
            object body=null;long copyTicks=0,releaseTicks=0;
            try{
                long start=Stopwatch.GetTimestamp();Need(root!=null,"Missing committed host root for bound Repair",missing:true);
                Need(root.IsImmutableCaptureRoot,"Unsealed committed host root");Need(root.AuthorityGeometry!=null,"Missing committed authority geometry",missing:true);
                Need(root.ArrivalWork.Count<=AttemptLimit,"Arrival catalog exceeds direct lookup bound",truncated:true);
                var matches=new List<object>(MatchLimit);int found=0;
                for(long id=1;id<=AttemptLimit;id++){
                    if(!root.ArrivalWork.TryGetValue(id,out var value))continue;found++;
                    var work=value as NavigationArrivalWork;Need(work!=null&&work.IsImmutableCaptureRoot,"Invalid committed arrival work",missing:true);
                    var input=work.PhysicalInput;Need(input!=null,"Missing committed arrival input",missing:true);
                    Need(input.Members.Count<=MemberLimit,"Arrival member bound exceeded",truncated:true);
                    for(int n=0;n<input.Members.Count;n++){
                        var member=input.Members[n];
                        if(member.WorldGeneration!=generation||member.EntityId!=unit||member.CommandSequence!=sequence||member.GroupId!=group)continue;
                        Need(matches.Count<MatchLimit,"Matching arrival bound exceeded",truncated:true);
                        Need(member.ActivationRevision==revision,"Repair activation binding changed");
                        Need(root.ArrivalIntents.TryGetValue(member.RequestSequence,out var intent)&&intent is NavigationHostRequest,"Missing committed request owner",missing:true);
                        var request=((NavigationHostRequest)intent).Request;
                        Need(request.Session==generation&&request.Entity==unit&&request.Request==member.RequestSequence&&request.MemberIdentity!=null&&request.MemberIdentity.GroupId==group,"Native arrival/request identity mismatch");
                        Need(root.ArrivalHistory.TryGetValue(id,out var historyValue)&&historyValue is NavigationHostArrivalHistory,"Missing committed arrival history",missing:true);
                        var history=(NavigationHostArrivalHistory)historyValue;int copied=Math.Min(GrantLimit,history.Count);var grants=new object[copied];
                        for(int h=0;h<copied;h++){var grant=history[history.Count-copied+h];grants[h]=new{grant.Epoch,grant.External,grant.Allocation,grant.Graph,grant.Field,grant.Connectors,grant.Bookkeeping,grant.Maintenance};}
                        var state=work.LifecycleState;
                        Need(input.WorkingBindings.Count<=MemberLimit,"Working provider binding bound exceeded",truncated:true);
                        NavArrivalWorkingBinding working=null;
                        for(int b=0;b<input.WorkingBindings.Count;b++){
                            var candidate=input.WorkingBindings[b];if(candidate.Entity!=member.EntityId)continue;
                            Need(working==null,"Duplicate working provider binding");working=candidate;
                        }
                        Need(working?.Provider!=null,"Missing working provider binding",missing:true);
                        var flat=working.Provider as LayeredNavigationProvider;bool partition=working.Provider is PartitionNavigationProvider;
                        bool certifiedFlat=flat!=null&&flat.Id=="flat"&&flat.SemanticsVersion==1&&flat.Surfaces.Count==1&&flat.Surfaces[0].Id==NavLocation.FlatSurface&&flat.Portals.Count==0;
                        bool derivedSingleton=input.Members.Count==1&&(partition?!input.IndependentMembers:input.IndependentMembers&&certifiedFlat);
                        // This facade may forward historical/restored counters. Its
                        // getter also evaluates readonly retained-size estimates.
                        var route=work.RouteCounters;Need(route!=null,"Missing arrival facade route counters",missing:true);
                        matches.Add(new{Attempt=id,MemberIndex=n,member.EntityId,member.WorldGeneration,member.GroupId,member.CommandSequence,member.RequestSequence,member.Incarnation,member.RootOrderRevision,member.ActivationRevision,member.MemberOrderRevision,
                            RequestOrder=request.Order,RequestRevision=request.MemberIdentity.OrderRevision,MembershipRevision=request.MemberIdentity.MembershipRevision,Geometry=request.Geometry.Revision,BaseGeometry=request.BaseGeometry.Revision,request.ProfileRevision,
                            RequestStartX=request.Start.X,RequestStartZ=request.Start.Z,RequestGoalX=request.Goal.X,RequestGoalZ=request.Goal.Z,RequestedX=input.Source.Requested.Position.X,RequestedZ=input.Source.Requested.Position.Z,CandidateCount=input.Source.Candidates.Count,OccupantCount=input.Occupancy.Occupants.Count,
                            State=new{state.Epoch,state.Total,state.CandidateWork,state.OccupancyWork,state.DirectWork,state.Member,state.Page,state.Raw,state.CandidateCursor,state.OccupancyCursor,state.PreparationPage,state.PreparationMember,state.HistoricalGate,state.HistoricalCursor,state.Phase,state.Status},
                            work.AllocationQuotaRemaining,work.LastAdvanceConsumed,work.LastAdvanceRetentionBlocked,
                            input.IndependentMembers,InputMemberCount=input.Members.Count,WorkingBindingCount=input.WorkingBindings.Count,
                            WorkingProvider=new{Kind=flat!=null?1:partition?2:0,IdIsFlat=flat?.Id=="flat",Semantics=flat?.SemanticsVersion,Revision=flat?.Revision,Topology=flat?.TopologyRevision,Surfaces=working.Provider.Surfaces.Count,Portals=working.Provider.Portals.Count,SoleSurfaceIsFlat=working.Provider.Surfaces.Count==1&&working.Provider.Surfaces[0].Id==NavLocation.FlatSurface},
                            DerivedSingletonEligibility=derivedSingleton,
                            FacadeRouteCounters=new{route.LogicalGraphWork,route.PhysicalGraphWork,route.LogicalFieldWork,route.PhysicalFieldWork,route.ConnectorWork,route.MaintenanceWork,route.CacheHits,route.GraphBuilds,route.FieldBuilds,route.Cancelled,route.Evictions,route.PendingSubscriptions,route.ReadyResults,route.CacheFields,route.PeakFieldLabels,route.PeakFrontier,route.PeakQueueAge},
                            HistoryCount=history.Count,Grants=grants});
                    }
                }
                // Count detects sparse ids outside 1..32, even when few rows remain.
                Need(found==root.ArrivalWork.Count,"Arrival ids outside direct lookup bound",truncated:true);
                var frontier=root.Frontier;var operation=root.Operation;
                body=new{AuthorityGeometry=root.AuthorityGeometry.Facade.Revision,root.Epoch,root.Progress,
                    root.QuantumGeneration,root.QuantumTick,root.QuantumSerial,root.ConsumedQuantum,root.AdmissionPrefix,root.QuantumBudget,root.QuantumCompleted,root.LastServedArrival,root.ArrivalRoundEnd,
                    Frontier=new{Stage=(int)frontier.Stage,frontier.OriginalBudget,frontier.ScansUsed,frontier.TransfersUsed,frontier.ReadyUsed,frontier.ProjectionUsed,frontier.ProjectionAllowance,frontier.SolveWorkLimit,frontier.SolveConsumed,frontier.AllocationFloor,frontier.PhaseUsed,frontier.ScanAllowance},
                    Operation=new{Stage=(int)operation.Stage,operation.Iteration,operation.Entered,operation.ProgressIncrementConsumed,SelectorRole=(int)operation.Selector.Role,SelectorIndex=operation.Selector.Index},
                    ArrivalCatalogCount=root.ArrivalWork.Count,LookupsFound=found,CatalogComplete=true,TargetBodyAbsent=matches.Count==0,Matches=matches.ToArray(),
                    RequestRows=root.Requests.Count,SchedulerRows=root.Rows.Count,DeliveredRows=root.Delivered.Count,ScanCount=root.Scan.Count,ProjectionCount=root.Projection.Count,CompletionCount=root.Completion.Count};
                copyTicks=Stopwatch.GetTimestamp()-start;MaxCopyTicks=Math.Max(MaxCopyTicks,copyTicks);
            }finally{
                if(root!=null){long start=Stopwatch.GetTimestamp();root.Release();PinsReleased++;releaseTicks=Stopwatch.GetTimestamp()-start;MaxReleaseTicks=Math.Max(MaxReleaseTicks,releaseTicks);}
            }
            // All operands below are detached scalars/arrays. No root reference escapes.
            return new{Valid=true,TargetBound=true,Generation=generation,Unit=unit,Sequence=sequence,Action=action,Group=group,Issuance=issuance,ActivationRevision=revision,
                WorldTick=worldTick,ObservationTick=view.Tick,DomainGeometry=domainGeometry,Buildings=buildings,
                Publication=new{publication.Pumps,publication.Epoch,publication.PumpElapsedMilliseconds,publication.PreparationElapsedMilliseconds,publication.ProviderElapsedMilliseconds,publication.SolveElapsedMilliseconds,publication.ProjectionElapsedMilliseconds,publication.TransportWaitMilliseconds,publication.TransportHoldMilliseconds,
                    counters.LogicalGraphWork,counters.PhysicalGraphWork,counters.LogicalFieldWork,counters.PhysicalFieldWork,counters.ConnectorWork,counters.CacheHits,counters.GraphBuilds,counters.FieldBuilds,counters.Cancelled,counters.MaintenanceWork,counters.PendingSubscriptions,counters.ReadyResults,counters.PeakQueueAge},
                Committed=body,CopyTicks=copyTicks,ReleaseTicks=releaseTicks,
                PhaseContract="Post-frame only. View, domain geometry, immutable publication and committed root are separate observations; no atomic epoch equality or per-request ownership of global host stage is assumed. Missing target body in a complete catalog is native absence, not missing coverage."};
        }
    }
}
#endif
