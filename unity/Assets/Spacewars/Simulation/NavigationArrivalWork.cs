using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    public sealed partial class NavigationArrivalWork : NavOwnedStorage
    {
        private enum Phase { PrepareRegions, ScanCandidates, SolvePage, Finished }
        internal sealed class PreparedCheckpoint
        { internal string ClearanceKey; internal int[][] CandidateIndices; internal NavTerminalRegion[] Regions; }
        public sealed class Checkpoint
        {
            public int Version { get; internal set; }
            internal string InputKey;internal bool SessionProviders;internal long AuthorityBase;
            internal long LastEpoch;
            internal int AllocationRemaining, MemberIndex, PageIndex, CandidateCursor, OccupantCursor;
            internal int PreparationCursor, PreparationPageIndex;
            internal string PreparingClearanceKey;
            internal int[] PreparationValidCandidateIndices;
            internal int[][] PreparationCandidateIndices;
            internal NavTerminalRegion[] PreparationRegions;
            internal PreparedCheckpoint[] PreparedClearances;
            internal long AllocationWorkConsumed, CandidateWorkConsumed, OccupancyWorkConsumed, DirectWorkConsumed;
            internal bool CandidateValidated, CandidateBlocked, MemberSawBlocked, MemberAllUnreachable;
            internal NavArrivalStatus MemberFailure;
            internal int MemberRouteCount;
            internal NavArrivalStatus Status; internal int Phase;
            internal int[] PageStarts, PageCounts;
            internal NavScheduledRequest[] Requests; internal int[] RequestCandidates;
            internal RouteCheckpoint[] Routes;
            internal NavArrivalProposal[] Assignments; internal KeyValuePair<long, NavArrivalStatus>[] MemberOutcomes;
            internal LayeredNavigationScheduler.Checkpoint Scheduler;
        }
        internal sealed class ReadyCheckpoint
        {
            internal string InputKey, Continuation;
            internal long Epoch, Allocation, Candidate, Occupancy, Direct;
            internal int Remaining, Member, CandidateCursor, OccupancyCursor;
            internal NavSchedulerCounters Counters; internal NavEpochQuota Quota;
            internal NavArrivalProposal[] Proposals;
            internal KeyValuePair<long,NavArrivalStatus>[] Outcomes;
        }
        private ReadyCheckpoint restoredReady;
        internal long ReadyCaptureScratchBytes
        {
            get{
                if(restoredReady!=null)return 4096;
                long wire=checked(128L+NavContract.StringWireBytes(preparingClearanceKey??"")+4L*preparationValidCandidateIndices.Count+12L*pageResults.Count);
                for(int i=0;i<assignments.Count;i++)wire=checked(wire+NavContract.StringWireBytes(assignments[i].Proof.StableKey));for(int i=0;i<pageRequests.Count;i++)wire=checked(wire+8L+NavContract.StringWireBytes(pageRequests[i].Request.Subscription.StableKey));
                return checked(NavStorageLayout.ObjectBytesBound(typeof(ReadyCheckpoint))+NavStorageLayout.ObjectBytesBound(typeof(NavSchedulerCounters))+NavStorageLayout.ObjectBytesBound(typeof(NavEpochQuota))+8192L+256L*(assignments.Count+memberOutcomes.Count+pageRequests.Count+pageResults.Count+preparationValidCandidateIndices.Count)+scheduler.LogicalContinuationScratchBytes+NavContract.DigestScratchBytes(wire));
            }
        }
        internal ReadyCheckpoint SaveReady()
        {
            if(Status!=NavArrivalStatus.Ready)return null;
            return restoredReady??new ReadyCheckpoint{InputKey=input.StableKey,Continuation=LogicalContinuationBinding,Epoch=lastEpoch,
                Allocation=AllocationWorkConsumed,Candidate=CandidateWorkConsumed,Occupancy=OccupancyWorkConsumed,Direct=DirectWorkConsumed,
                Remaining=allocationRemaining,Member=memberIndex,CandidateCursor=candidateCursor,OccupancyCursor=occupantCursor,
                Counters=scheduler.Counters,Quota=scheduler.RemainingQuota,Proposals=assignments.ToArray(),Outcomes=memberOutcomes.OrderBy(p=>p.Key).ToArray()};
        }
        internal static NavigationArrivalWork RestoreReady(NavArrivalInput input,ReadyCheckpoint saved)
        {
            if(saved==null||saved.InputKey!=input.StableKey||saved.Continuation==null||saved.Continuation.Length!=64||saved.Epoch<0||
                saved.Allocation<0||saved.Candidate<0||saved.Occupancy<0||saved.Direct<0||saved.Allocation!=saved.Candidate+saved.Occupancy||
                saved.Remaining<0||saved.Member!=input.Members.Count||saved.CandidateCursor<0||saved.CandidateCursor>64||saved.OccupancyCursor<0||
                saved.Quota==null||saved.Counters==null||saved.Counters.PendingSubscriptions!=0||saved.Counters.ReadyResults!=0||saved.Proposals==null||saved.Outcomes==null||
                saved.Proposals.Length+saved.Outcomes.Length!=input.Members.Count)throw new ArgumentException("Invalid completed arrival state.");
            var keys=saved.Proposals.Select(p=>p.Member.EntityId).Concat(saved.Outcomes.Select(p=>p.Key)).ToArray();
            if(keys.Distinct().Count()!=keys.Length||!keys.OrderBy(k=>k).SequenceEqual(input.Members.Select(m=>m.EntityId).OrderBy(k=>k))||
                saved.Proposals.Any(p=>!p.Proof.ValidateAgainst(input,p.Member,p.Endpoint))||saved.Outcomes.Any(p=>p.Value!=NavArrivalStatus.NoAvailableSlot&&p.Value!=NavArrivalStatus.UnreachableInGraph&&p.Value!=NavArrivalStatus.CapacityExceeded&&p.Value!=NavArrivalStatus.InvalidInput))throw new ArgumentException("Invalid completed arrival owners/proofs.");
            var work=new NavigationArrivalWork(input){Status=NavArrivalStatus.Ready,phase=Phase.Finished,restoredReady=saved,
                lastEpoch=saved.Epoch,allocationRemaining=saved.Remaining,memberIndex=saved.Member,candidateCursor=saved.CandidateCursor,occupantCursor=saved.OccupancyCursor,
                AllocationWorkConsumed=saved.Allocation,CandidateWorkConsumed=saved.Candidate,OccupancyWorkConsumed=saved.Occupancy,DirectWorkConsumed=saved.Direct};
            foreach(var proposal in saved.Proposals)work.KeepAssignment(proposal);foreach(var row in saved.Outcomes)work.memberOutcomes.Add(row.Key,row.Value);return work;
        }
        internal sealed class RouteCheckpoint
        { internal int Candidate; internal NavSolveStatus Status; internal NavTypedRoute Route; }

        private NavArrivalInput input;
        private NavArrivalMember[] members;
        private NavRetainedArray<NavArrivalMember> membersStorage;
        private int[] pageStarts, pageCounts;
        private NavRetainedArray<int> pageStartsStorage, pageCountsStorage;
        private NavPersistentIndex<NavNameKey, PreparedClearance> preparedClearances;
        private NavPagedList<int> preparationValidCandidateIndices;
        private int preparationCursor, preparationPageIndex;
        private string preparingClearanceKey;
        private NavPagedList<NavRetainedArray<int>> preparationCandidateIndices;
        private NavPagedList<NavTerminalRegion> preparationRegions;
        private NavPagedList<RouteRow> pageRequests;
        private NavPersistentIndex<int, RouteRow> pageResults;
        private NavPersistentIndex<long, NavArrivalStatus> memberOutcomes;
        public IReadOnlyDictionary<long, NavArrivalStatus> MemberOutcomes => memberOutcomes;
        private NavPagedList<NavArrivalProposal> assignments;
        private LayeredNavigationScheduler scheduler;
        private long lastEpoch = -1;
        private long authorityReservationBase;

        private int allocationRemaining, memberIndex, pageIndex, candidateCursor, occupantCursor;
        private bool candidateValidated, candidateBlocked, memberSawBlocked, memberAllUnreachable = true;
        private int memberRouteCount;
        private NavArrivalStatus memberFailure = NavArrivalStatus.Pending;
        private Phase phase;

        public NavigationArrivalWork(NavArrivalInput input) : this(input,(Func<long,bool>)null) {}
        private NavigationArrivalWork(NavArrivalInput input, Func<long,bool> reserve, bool birthOnly=false) : base(LayoutBytes)
        {
            this.input = input ?? throw new ArgumentNullException(nameof(input));rootReservation=reserve;physicalLifetime=reserve!=null;
            using(var allocation=new NavStorageAllocation(reserve)){allocation.Reserve(LayoutBytes);allocation.Register(this);if(!birthOnly)Retain();}
            if(birthOnly)return;
            try{
                if(input.StorageRoot!=null){RequireEdge(input.StorageRoot);input.StorageRoot.Retain();ownedInput=input.StorageRoot;}
                membersStorage=NavRetainedArray<NavArrivalMember>.Copy(input.Members,reserve);members=membersStorage.ReadOnlyBacking;
                BuildPages(input.Source.Candidates,out var starts,out var counts);
                pageStartsStorage=NavRetainedArray<int>.Copy(starts,reserve);pageCountsStorage=NavRetainedArray<int>.Copy(counts,reserve);
                pageStarts=pageStartsStorage.ReadOnlyBacking;pageCounts=pageCountsStorage.ReadOnlyBacking;
                preparedClearances=NavPersistentIndex<NavNameKey,PreparedClearance>.Create(Comparer<NavNameKey>.Default,reserve);
                preparationValidCandidateIndices=NavPagedList<int>.Create(reserve);
                pageRequests=NavPagedList<RouteRow>.Create(reserve);pageResults=NavPersistentIndex<int,RouteRow>.Create(reserve:reserve);
                memberOutcomes=NavPersistentIndex<long,NavArrivalStatus>.Create(reserve:reserve);assignments=NavPagedList<NavArrivalProposal>.Create(reserve);
                scheduler=reserve==null?new LayeredNavigationScheduler():LayeredNavigationScheduler.CreateOwned(OwnedReservation);
                if(input.IndependentMembers)scheduler.BindAttemptArtifacts(input.StableKey);
                Status=members.Length>LayeredNavigationScheduler.MaxQueue?NavArrivalStatus.CapacityExceeded:NavArrivalStatus.Pending;
                phase=Status==NavArrivalStatus.Pending?Phase.PrepareRegions:Phase.Finished;
            }catch{ReleaseOwner();throw;}
        }
        private NavigationArrivalWork(NavArrivalInput input, Checkpoint saved) : this(input)
        {
            if (saved == null || saved.Version != 2 || saved.InputKey != input.StableKey ||
                saved.PageStarts == null || saved.PageCounts == null || !saved.PageStarts.SequenceEqual(pageStarts) ||
                !saved.PageCounts.SequenceEqual(pageCounts) || saved.Phase < 0 || saved.Phase > (int)Phase.Finished ||
                saved.MemberIndex < 0 || saved.MemberIndex > members.Length || saved.PageIndex < 0 || saved.PageIndex > pageStarts.Length)
                throw new ArgumentException("Invalid or stale arrival checkpoint.");
            lastEpoch = saved.LastEpoch; allocationRemaining = saved.AllocationRemaining;
            AllocationWorkConsumed = saved.AllocationWorkConsumed; CandidateWorkConsumed = saved.CandidateWorkConsumed;
            OccupancyWorkConsumed = saved.OccupancyWorkConsumed;DirectWorkConsumed=saved.DirectWorkConsumed;
            memberIndex = saved.MemberIndex; pageIndex = saved.PageIndex; candidateCursor = saved.CandidateCursor;
            occupantCursor = saved.OccupantCursor; candidateValidated = saved.CandidateValidated;
            candidateBlocked = saved.CandidateBlocked; memberSawBlocked = saved.MemberSawBlocked;
            memberAllUnreachable = saved.MemberAllUnreachable; memberRouteCount = saved.MemberRouteCount;
            memberFailure = saved.MemberFailure;
            preparationCursor = saved.PreparationCursor; preparationPageIndex = saved.PreparationPageIndex;
            preparingClearanceKey = saved.PreparingClearanceKey;
            foreach(var index in saved.PreparationValidCandidateIndices ?? Array.Empty<int>())preparationValidCandidateIndices.Add(index);
            preparationCandidateIndices = CopyCandidateSlots(saved.PreparationCandidateIndices);
            preparationRegions = CopyRegionSlots(saved.PreparationRegions);
            foreach (var prepared in saved.PreparedClearances ?? Array.Empty<PreparedCheckpoint>()) {
                if (prepared == null || prepared.ClearanceKey == null || prepared.CandidateIndices == null ||
                    prepared.Regions == null || prepared.CandidateIndices.Length != pageStarts.Length ||
                    prepared.Regions.Length != pageStarts.Length || preparedClearances.ContainsKey(prepared.ClearanceKey))
                    throw new ArgumentException("Invalid prepared terminal region checkpoint.");
                var indices=CopyCandidateSlots(prepared.CandidateIndices);var regions=CopyRegionSlots(prepared.Regions);
                try{AddPrepared(prepared.ClearanceKey,indices,regions);}finally{indices?.ReleaseOwner();regions?.ReleaseOwner();}
            }
            foreach (var row in saved.MemberOutcomes ?? Array.Empty<KeyValuePair<long, NavArrivalStatus>>()) memberOutcomes.Add(row.Key, row.Value);
            Status = saved.Status; phase = (Phase)saved.Phase; foreach(var proposal in saved.Assignments ?? Array.Empty<NavArrivalProposal>())KeepAssignment(proposal);
            if (saved.Requests == null || saved.RequestCandidates == null || saved.Requests.Length != saved.RequestCandidates.Length || saved.Routes == null)
                throw new ArgumentException("Invalid arrival checkpoint rows.");
            for(int i=0;i<saved.Requests.Length;i++)AddRequestRow(saved.RequestCandidates[i],saved.Requests[i]);
            foreach (var result in saved.Routes) {
                if (result == null || pageResults.ContainsKey(result.Candidate)) throw new ArgumentException("Duplicate arrival route checkpoint.");
                var row = pageRequests.SingleOrDefault(x => x.Candidate == result.Candidate);
                if (row == null) throw new ArgumentException("Unknown arrival route checkpoint.");
                AcceptCompletion(row,result.Status,result.Route);
            }
            scheduler.ReleaseOwner();scheduler = LayeredNavigationScheduler.Restore(saved.Scheduler, pageRequests.Select(x => x.Request));
            scheduler.SessionOwnsProviders=saved.SessionProviders;authorityReservationBase=saved.AuthorityBase;
            if(scheduler.AttemptBinding!=(input.IndependentMembers?input.StableKey:null))throw new ArgumentException("Stale allocation attempt artifact owner.");
        }

        public NavArrivalStatus Status { get; private set; }
        public string InputBinding => input.StableKey;
        public IReadOnlyList<NavArrivalProposal> Proposals => Status == NavArrivalStatus.Ready
            ? Array.AsReadOnly(assignments.ToArray()) : Array.AsReadOnly(Array.Empty<NavArrivalProposal>());
        public int PendingRouteCount => scheduler.PendingCount;
        public int ReadyRouteCount => scheduler.ReadyCount;
        internal string RetentionBreakdown=>scheduler.RetentionBreakdown;
        internal void UseSessionProviderOwnership(bool external){MutableRoot();scheduler.SessionOwnsProviders=external;}
        private long OutputRetainedBytes
        {get{using(var scratch=rootReservation==null?null:NavStorageScratch.Reserve(OwnedReservation,checked(8192+128L*(assignments.Count+pageResults.Count))))return assignments.Select(p=>p.Route).Concat(pageResults.Values.Select(p=>p.Route)).Where(r=>r!=null).Distinct().Sum(r=>1024L+256L*r.Legs.Count);}}
        internal long StructuralRetainedBytes=>scheduler.RetainedBytes-(scheduler.SessionOwnsProviders?0:scheduler.ProviderRetainedBytes)+OutputRetainedBytes;
        private void KeepAssignment(NavArrivalProposal proposal){if(rootReservation==null)proposal.Route?.Retain();assignments.Add(proposal);}
        private void ClearAssignments(){if(rootReservation==null)foreach(var proposal in assignments)proposal.Route?.Release();assignments.Clear();}
        private void ClearPageRows(){pageRequests.Clear();pageResults.Clear();}
        internal void DiscardCancelledDerivatives(){MutableRoot();try{scheduler.ClearWorld();ClearAssignments();ClearPageRows();preparedClearances.Clear();ClearPreparation();ownedInput?.Release();ownedInput=null;input=null;derivativesDiscarded=true;}catch{rootFaulted=true;throw;}}

        internal bool TryReserveAuthorityBytes(long bytes){MutableRoot();if(!scheduler.TryReserveExternalBytes(bytes+OutputRetainedBytes))return false;authorityReservationBase=bytes;return true;}
        internal long AuthorityReservationBytes=>authorityReservationBase;
        internal bool LastAdvanceRetentionBlocked {get;private set;}
        internal void ReserveAuthorityBytes(long bytes){MutableRoot();try{authorityReservationBase=bytes;scheduler.ReserveExternalBytes(bytes+OutputRetainedBytes);}catch{rootFaulted=true;throw;}}
        internal void ReleaseDerivatives() {MutableRoot(); if (Status != NavArrivalStatus.Ready) throw new InvalidOperationException("Arrival still pending."); scheduler.ClearWorld();
            if(restoredReady!=null){var c=restoredReady.Counters;restoredReady.Counters=new NavSchedulerCounters(c.LogicalGraphWork,c.PhysicalGraphWork,c.LogicalFieldWork,c.PhysicalFieldWork,c.ConnectorWork,c.CacheHits,c.GraphBuilds,c.FieldBuilds,c.Cancelled,c.Evictions,c.MaintenanceWork,0,0,0,0,c.PeakCacheBytes,c.PeakQueue,c.PeakFieldLabels,c.PeakFrontier,0,c.PeakRetainedBytes,c.PeakQueueAge);}
        }
        public long LogicalEpoch => lastEpoch;
        public long AllocationWorkConsumed { get; private set; }
        public long CandidateWorkConsumed { get; private set; }
        public long OccupancyWorkConsumed { get; private set; }
        public long DirectWorkConsumed {get;private set;}
        public int MemberCursor => memberIndex;
        public int TerminalPageCount => pageStarts.Length;
        public int CandidateCursor => candidateCursor;
        public int OccupancyCursor => occupantCursor;
        public string WorkPhase => phase.ToString();
        public NavSchedulerCounters RouteCounters => restoredReady?.Counters??scheduler.Counters;
        public int AllocationQuotaRemaining => allocationRemaining;
        public long LastAdvanceConsumed { get; private set; }

        public bool ValidateAgainst(NavArrivalInput currentSnapshot)
        {
            MutableRoot();
            bool valid = currentSnapshot != null && input.StableKey == currentSnapshot.StableKey &&
                input.Provider.Binding == currentSnapshot.Provider.Binding &&
                input.TopologyIdentity == currentSnapshot.TopologyIdentity &&
                input.ProfileIdentity == currentSnapshot.ProfileIdentity &&
                input.Source.StableKey == currentSnapshot.Source.StableKey &&
                input.Occupancy.StableKey == currentSnapshot.Occupancy.StableKey &&
                input.Members.Count == currentSnapshot.Members.Count &&
                input.Members.Select(x => x.StableKey).SequenceEqual(currentSnapshot.Members.Select(x => x.StableKey));
            if (!valid) { scheduler.ClearWorld();Status = NavArrivalStatus.Stale; phase = Phase.Finished; ClearAssignments(); }
            return valid;
        }

        public NavArrivalStatus Advance(long logicalEpoch, int allocationQuota, NavEpochQuota navQuota)
        {
            MutableRoot();if(derivativesDiscarded)throw new InvalidOperationException("Discarded arrival work cannot be advanced.");
            IDisposable advanceScratch=null;
            try{advanceScratch=rootReservation==null?null:NavStorageScratch.Reserve(OwnedReservation,131072);
            LastAdvanceConsumed = 0;LastAdvanceRetentionBlocked=false;
            long previousEpoch=lastEpoch;int previousRemaining=allocationRemaining;var previousStatus=Status;
            if (Status != NavArrivalStatus.Pending && Status != NavArrivalStatus.BudgetExhausted) return Status;
            if (logicalEpoch < 0 || allocationQuota < 0 || navQuota == null || logicalEpoch < lastEpoch) {
                Status = NavArrivalStatus.InvalidInput; phase = Phase.Finished; return Status;
            }
            if (logicalEpoch != lastEpoch) { lastEpoch = logicalEpoch; allocationRemaining = allocationQuota; }
            Status = NavArrivalStatus.Pending;
            long advanceWorkStart = AllocationWorkConsumed;
            while (phase != Phase.Finished) {
                if(scheduler.SessionOwnsProviders)scheduler.ReserveExternalBytes(authorityReservationBase+OutputRetainedBytes);
                if (memberIndex >= members.Length) {
                    if(input.IndependentMembers&&!scheduler.EndAttemptArtifacts(logicalEpoch,navQuota)){Status=NavArrivalStatus.BudgetExhausted;break;}
                    Status = NavArrivalStatus.Ready; phase = Phase.Finished; break;
                }
                if (phase == Phase.PrepareRegions) {
                    var clearance = members[memberIndex].Clearance;
                    string clearanceKey = WorkingKey(members[memberIndex]);
                    if (preparedClearances.ContainsKey(clearanceKey)) { BeginCandidateScan(); continue; }
                    if (preparingClearanceKey != clearanceKey) BeginPreparation(clearanceKey);
                    if (preparationPageIndex >= pageStarts.Length) {
                        AddPrepared(clearanceKey,preparationCandidateIndices,preparationRegions);
                        ClearPreparation();
                        BeginCandidateScan(); continue;
                    }
                    int preparationEnd = pageStarts[preparationPageIndex] + pageCounts[preparationPageIndex];
                    if (preparationCursor >= preparationEnd) {
                        FinishPreparationPage(clearanceKey);
                        preparationPageIndex++; preparationValidCandidateIndices.Clear();
                        preparationCursor = preparationPageIndex < pageStarts.Length ? pageStarts[preparationPageIndex] : 0;
                        continue;
                    }
                    if (allocationRemaining == 0) { Status = NavArrivalStatus.BudgetExhausted; break; }
                    SpendCandidate();
                    int rawIndex = preparationCursor++;
                    if (input.WorkingFor(members[memberIndex]).Provider.IsValid(input.Source.Candidates[rawIndex], clearance))
                        preparationValidCandidateIndices.Add(rawIndex);
                    continue;
                }
                if (phase == Phase.ScanCandidates) {
                    if (pageIndex >= pageStarts.Length) { FinishMember(); continue; }
                    var prepared = preparedClearances[WorkingKey(members[memberIndex])];
                    var validCandidates = prepared.CandidateIndices[pageIndex];
                    if (validCandidates.Count == 0) { NextPage(); continue; }
                    if (candidateCursor >= validCandidates.Count) { phase = Phase.SolvePage; continue; }
                    if (allocationRemaining == 0) { Status = NavArrivalStatus.BudgetExhausted; break; }
                    int candidateIndex = validCandidates[candidateCursor];
                    var candidate = input.Source.Candidates[candidateIndex];
                    if (!candidateValidated) {
                        SpendCandidate();
                        candidateValidated = true; candidateBlocked = false; occupantCursor = 0;
                    }
                    int totalOccupants = input.Occupancy.Occupants.Count + assignments.Count;
                    if (occupantCursor < totalOccupants) {
                        if (allocationRemaining == 0) { Status = NavArrivalStatus.BudgetExhausted; break; }
                        SpendOccupancy();
                        if (occupantCursor < input.Occupancy.Occupants.Count) {
                            var occupant = input.Occupancy.Occupants[occupantCursor];
                            if (occupant.BlocksPlacement && TooClose(candidate.Position, members[memberIndex].Radius,
                                occupant.Location.Position, occupant.Radius)) candidateBlocked = true;
                        } else {
                            var assigned = assignments[occupantCursor - input.Occupancy.Occupants.Count];
                            if (TooClose(candidate.Position, members[memberIndex].Radius, assigned.Endpoint.Position,
                                assigned.Member.Radius)) candidateBlocked = true;
                        }
                        occupantCursor++;
                        if (candidateBlocked) { memberSawBlocked = true; AdvanceCandidate(); }
                        continue;
                    }
                    if (allocationRemaining == 0) { Status = NavArrivalStatus.BudgetExhausted; break; }
                    SpendCandidate();
                    var request = CreateRequest(memberIndex, pageIndex, candidateIndex);
                    try{
                    if(input.IndependentMembers&&pageRequests.Count==0){
                        DirectWorkConsumed++;
                        var direct=scheduler.DirectPreparedRoute(request);
                        if(direct!=null){
                            var member=members[memberIndex];
                            try{KeepCertifiedAssignment(member,candidate,request,direct);}finally{direct.ReleaseOwner();}NextMember();continue;
                        }
                    }
                    if (!scheduler.Submit(request)) { Status = NavArrivalStatus.CapacityExceeded; phase = Phase.Finished; break; }
                    AddRequestRow(candidateIndex,request);
                    memberRouteCount++;
                    AdvanceCandidate();
                    continue;
                    }finally{request.StorageRoot?.Release();}
                }
                if (phase == Phase.SolvePage) {
                    if (pageRequests.Count == 0) { NextPage(); continue; }
                    var stamp=scheduler.CaptureEpoch();long maintenance=scheduler.Counters.MaintenanceWork;
                    int routeWork=scheduler.Advance(logicalEpoch, navQuota, _ => true);
                    if(routeWork==0&&scheduler.RetentionBlocked&&scheduler.Counters.MaintenanceWork==maintenance&&AllocationWorkConsumed==advanceWorkStart){
                        scheduler.RestoreUnspentEpoch(stamp);lastEpoch=previousEpoch;allocationRemaining=previousRemaining;Status=previousStatus;
                        LastAdvanceRetentionBlocked=true;return Status;
                    }
                    while (allocationRemaining > 0 && scheduler.TryTake(_ => true, out var result)) {
                        SpendCandidate();
                        var row = pageRequests.Single(x => x.Request.Subscription.StableKey == result.Subscription.StableKey);
                        try{AcceptCompletion(row,result.Status,result.Route);}finally{result.Release();}
                        if (result.Status != NavSolveStatus.UnreachableInGraph) memberAllUnreachable = false;
                        ObserveTerminalFailure(result.Status);
                    }
                    if(input.IndependentMembers&&scheduler.ReadyCount==0){
                        var first=pageResults.Values.Where(x=>x.Status==NavSolveStatus.Ready).OrderBy(x=>x.Candidate).FirstOrDefault();
                        if(first!=null&&pageRequests.Where(x=>x.Candidate<first.Candidate).All(x=>pageResults.ContainsKey(x.Candidate))&&
                            pageResults.Values.Where(x=>x.Candidate<first.Candidate).All(x=>x.Status==NavSolveStatus.UnreachableInGraph||x.Status==NavSolveStatus.BlockedConnector)){
                            // Later candidates cannot precede this certified endpoint.
                            // Release their subscriptions while keeping shared graph work.
                            foreach(var later in pageRequests.Where(x=>x.Candidate>first.Candidate))scheduler.Cancel(later.Request.Subscription.StableKey);
                            var member=members[memberIndex];var endpoint=input.Source.Candidates[first.Candidate];
                            KeepCertifiedAssignment(member,endpoint,first.Request,first.Route);
                            NextMember();continue;
                        }
                    }
                    if (pageResults.Count == pageRequests.Count) {
                        if (memberFailure != NavArrivalStatus.Pending) {
                            if (input.IndependentMembers && memberFailure != NavArrivalStatus.Stale) { FinishMember(); continue; }
                            Status = memberFailure; phase = Phase.Finished; ClearAssignments(); break;
                        }
                        var ready = pageResults.Values.Where(x => x.Status == NavSolveStatus.Ready)
                            .OrderBy(x => x.Candidate).FirstOrDefault();
                        if (ready != null) {
                            var member = members[memberIndex]; var endpoint = input.Source.Candidates[ready.Candidate];
                            KeepCertifiedAssignment(member,endpoint,ready.Request,ready.Route);
                            NextMember(); continue;
                        }
                        ClearPageRows(); NextPage(); continue;
                    }
                    if (allocationRemaining == 0) { Status = NavArrivalStatus.BudgetExhausted; break; }
                    if (scheduler.PendingCount == 0 && scheduler.ReadyCount == 0) {
                        Status = NavArrivalStatus.CapacityExceeded; phase = Phase.Finished; break;
                    }
                    // The scheduler has consumed this epoch's route quota. Keep its
                    // actual cursor and return control instead of spinning until ready.
                    if (navQuota.Graph == 0 && navQuota.Field == 0 && navQuota.Connectors == 0 && navQuota.Bookkeeping == 0) {
                        Status = NavArrivalStatus.BudgetExhausted; break;
                    }
                    Status = NavArrivalStatus.BudgetExhausted; break;
                }
            }
            if (phase == Phase.Finished && Status == NavArrivalStatus.Pending) Status = NavArrivalStatus.Ready;
            LastAdvanceConsumed = AllocationWorkConsumed - advanceWorkStart;
            return Status;
            }catch{rootFaulted=true;throw;}finally{advanceScratch?.Dispose();}
        }

        private void FinishMember()
        {
            if (memberFailure != NavArrivalStatus.Pending) Status = memberFailure;
            else if (memberRouteCount > 0 && memberAllUnreachable && !memberSawBlocked) Status = NavArrivalStatus.UnreachableInGraph;
            else Status = NavArrivalStatus.NoAvailableSlot;
            if (input.IndependentMembers && Status != NavArrivalStatus.Stale) { memberOutcomes.Add(members[memberIndex].EntityId, Status); Status = NavArrivalStatus.Pending; NextMember(); return; }
            phase = Phase.Finished; ClearAssignments();
        }
        private void ObserveTerminalFailure(NavSolveStatus status)
        {
            NavArrivalStatus failure;
            switch (status) {
                case NavSolveStatus.Stale: failure = NavArrivalStatus.Stale; break;
                case NavSolveStatus.InvalidEndpoint: failure = NavArrivalStatus.InvalidInput; break;
                case NavSolveStatus.CapacityExceeded: failure = NavArrivalStatus.CapacityExceeded; break;
                default: return;
            }
            int Rank(NavArrivalStatus value) => value == NavArrivalStatus.Stale ? 3 :
                value == NavArrivalStatus.InvalidInput ? 2 : value == NavArrivalStatus.CapacityExceeded ? 1 : 0;
            if (Rank(failure) > Rank(memberFailure)) memberFailure = failure;
        }
        private void NextPage()
        {
            pageIndex++;
            if (pageIndex >= pageStarts.Length) { FinishMember(); return; }
            candidateCursor = 0; occupantCursor = 0;
            candidateValidated = false; candidateBlocked = false; phase = Phase.ScanCandidates;
        }
        private void NextMember()
        {
            memberIndex++; pageIndex = 0; memberRouteCount = 0; memberSawBlocked = false; memberAllUnreachable = true;
            memberFailure = NavArrivalStatus.Pending;
            candidateCursor = 0; occupantCursor = 0;
            candidateValidated = false; candidateBlocked = false; ClearPageRows(); phase = Phase.PrepareRegions;
        }
        private void BeginCandidateScan()
        { pageIndex = 0; candidateCursor = 0; occupantCursor = 0; candidateValidated = false; candidateBlocked = false; phase = Phase.ScanCandidates; }
        private void BeginPreparation(string clearanceKey)
        {
            preparingClearanceKey = clearanceKey; preparationPageIndex = 0;
            preparationCursor = pageStarts.Length == 0 ? 0 : pageStarts[0];
            preparationValidCandidateIndices.Clear();
            ResetPreparationSlots();
        }
        private void AdvanceCandidate()
        { candidateCursor++; occupantCursor = 0; candidateValidated = false; candidateBlocked = false; }
        private void SpendAllocation() { allocationRemaining--; AllocationWorkConsumed++; }
        private void SpendCandidate() { SpendAllocation(); CandidateWorkConsumed++; }
        private void SpendOccupancy() { SpendAllocation(); OccupancyWorkConsumed++; }

        private NavScheduledRequest CreateRequest(int memberIndex, int pageIndex, int candidateIndex)
        {
            var member = members[memberIndex]; var endpoint = input.Source.Candidates[candidateIndex];
            var subscription = new NavRouteSubscription(member.WorldGeneration, member.GroupId,
                member.RootOrderRevision, member.CommandSequence, member.IssuedTick, member.ActivationRevision,
                member.EntityId, member.Incarnation, member.MemberOrderRevision, member.MobilityRevision,
                member.RequestSequence, member.Origin, endpoint, member.HoldBinding, NavRouteProvenance.CommandMember);
            var binding = input.WorkingFor(member);
            if(rootReservation!=null)return NavScheduledRequest.CreateOwned(binding.Provider,member.Clearance,binding.Profile,input.Limits,preparedClearances[WorkingKey(member)].Regions[pageIndex],subscription,input.MaxLabels,OwnedReservation,
                singleton:binding.Provider is PartitionNavigationProvider&&!input.IndependentMembers&&members.Length==1);
            return new NavScheduledRequest(binding.Provider, member.Clearance, binding.Profile, input.Limits,
                preparedClearances[WorkingKey(member)].Regions[pageIndex], subscription, input.MaxLabels,
                singleton: binding.Provider is PartitionNavigationProvider&&!input.IndependentMembers&&members.Length==1);
        }

        private bool TooClose(NavPoint a, double radiusA, NavPoint b, double radiusB)
        {
            double spacing = Math.Max(input.ArrivalProfile.ArrivalSlotSpacing,
                4 * Math.Max(radiusA, radiusB) + 2 * input.ArrivalProfile.ArrivalTolerance + 2 * input.ArrivalProfile.LocalGridCell);
            double dx = a.X - b.X, dz = a.Z - b.Z;
            return dx * dx + dz * dz < spacing * spacing - 1e-9;
        }

        public Checkpoint Save()
        {
            return new Checkpoint { Version = 2, InputKey = input.StableKey,SessionProviders=scheduler.SessionOwnsProviders,AuthorityBase=authorityReservationBase, LastEpoch = lastEpoch,
                AllocationWorkConsumed = AllocationWorkConsumed, CandidateWorkConsumed = CandidateWorkConsumed,
                OccupancyWorkConsumed = OccupancyWorkConsumed, DirectWorkConsumed=DirectWorkConsumed,
                AllocationRemaining = allocationRemaining, MemberIndex = memberIndex, PageIndex = pageIndex,
                CandidateCursor = candidateCursor, OccupantCursor = occupantCursor, CandidateValidated = candidateValidated,
                PreparationCursor = preparationCursor, PreparationPageIndex = preparationPageIndex,
                PreparingClearanceKey = preparingClearanceKey,
                PreparationValidCandidateIndices = preparationValidCandidateIndices.ToArray(),
                PreparationCandidateIndices = ExportCandidates(preparationCandidateIndices),
                PreparationRegions = preparationRegions?.ToArray(),
                PreparedClearances = preparedClearances.OrderBy(x => x.Key.Value, StringComparer.Ordinal).Select(x => new PreparedCheckpoint {
                    ClearanceKey = x.Key, CandidateIndices = ExportCandidates(x.Value.CandidateIndices),
                    Regions = x.Value.Regions.ToArray() }).ToArray(),
                CandidateBlocked = candidateBlocked, MemberSawBlocked = memberSawBlocked,
                MemberAllUnreachable = memberAllUnreachable, MemberRouteCount = memberRouteCount, MemberFailure = memberFailure,
                Status = Status, Phase = (int)phase, PageStarts = (int[])pageStarts.Clone(), PageCounts = (int[])pageCounts.Clone(),
                Requests = pageRequests.Select(x => x.Request).ToArray(), RequestCandidates = pageRequests.Select(x => x.Candidate).ToArray(),
                Routes = pageResults.Values.OrderBy(x => x.Candidate).Select(x => new RouteCheckpoint { Candidate = x.Candidate, Status = x.Status, Route = x.Route }).ToArray(),
                Assignments = assignments.ToArray(), MemberOutcomes = memberOutcomes.ToArray(), Scheduler = scheduler.Save() };
        }
        public static NavigationArrivalWork Restore(NavArrivalInput exactInputs, Checkpoint checkpoint)
            => new NavigationArrivalWork(exactInputs, checkpoint);

        internal string LogicalContinuationBinding => restoredReady?.Continuation??NavContract.Digest(w=>{
            w.Write(scheduler.LogicalContinuationBinding);w.Write(DirectWorkConsumed);
            w.Write(preparationCursor);w.Write(preparationPageIndex);w.Write(preparingClearanceKey??"");w.Write(pageIndex);
            w.Write(candidateValidated);w.Write(candidateBlocked);w.Write(memberSawBlocked);w.Write(memberAllUnreachable);
            w.Write(memberRouteCount);w.Write((int)memberFailure);
            foreach(var index in preparationValidCandidateIndices)w.Write(index);
            foreach(var p in assignments)w.Write(p.Proof.StableKey);
            foreach(var row in pageRequests){w.Write(row.Candidate);w.Write(row.Request.Subscription.StableKey);w.Write((int)row.Status);}
            foreach(var row in pageResults.OrderBy(p=>p.Key)){w.Write(row.Key);w.Write((int)row.Value.Status);}
        });
        private string WorkingKey(NavArrivalMember member){if(workingKeys==null)return RawWorkingKey(member);for(int n=0;n<members.Length;n++)if(ReferenceEquals(members[n],member))return workingKeys[n].Value;throw new ArgumentException("Unknown working member.");}
        private static string ClearanceKey(NavClearanceProfile clearance)
            => NavContract.Digest(w => clearance.Write(w));
        private static int[][] CloneJagged(int[][] source)
            => source == null ? null : source.Select(x => x == null ? null : (int[])x.Clone()).ToArray();

        private static void BuildPages(IReadOnlyList<NavLocation> candidates, out int[] starts, out int[] counts)
        {
            var s = new List<int>(); var c = new List<int>(); int i = 0;
            while (i < candidates.Count) { int start = i; string surface = candidates[i].SurfaceId;
                while (i < candidates.Count && i - start < 64 && candidates[i].SurfaceId == surface) i++;
                if (i == start) throw new ArgumentException("Invalid arrival candidate page.");
                s.Add(start); c.Add(i - start);
            }
            starts = s.ToArray(); counts = c.ToArray();
        }
    }
}
