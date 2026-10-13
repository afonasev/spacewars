using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    public sealed class NavigationArrivalWork
    {
        private enum Phase { PrepareRegions, ScanCandidates, SolvePage, Finished }
        private sealed class PreparedClearance
        { internal int[][] CandidateIndices; internal NavTerminalRegion[] Regions; }
        internal sealed class PreparedCheckpoint
        { internal string ClearanceKey; internal int[][] CandidateIndices; internal NavTerminalRegion[] Regions; }
        private sealed class RouteRow
        {
            internal int Candidate; internal NavScheduledRequest Request; internal NavSolveStatus Status; internal NavTypedRoute Route;
        }
        public sealed class Checkpoint
        {
            public int Version { get; internal set; }
            internal string InputKey;
            internal long LastEpoch;
            internal int AllocationRemaining, MemberIndex, PageIndex, CandidateCursor, OccupantCursor;
            internal int PreparationCursor, PreparationPageIndex;
            internal string PreparingClearanceKey;
            internal int[] PreparationValidCandidateIndices;
            internal int[][] PreparationCandidateIndices;
            internal NavTerminalRegion[] PreparationRegions;
            internal PreparedCheckpoint[] PreparedClearances;
            internal long AllocationWorkConsumed, CandidateWorkConsumed, OccupancyWorkConsumed;
            internal bool CandidateValidated, CandidateBlocked, MemberSawBlocked, MemberAllUnreachable;
            internal NavArrivalStatus MemberFailure;
            internal int MemberRouteCount;
            internal NavArrivalStatus Status; internal int Phase;
            internal int[] PageStarts, PageCounts;
            internal NavScheduledRequest[] Requests; internal int[] RequestCandidates;
            internal RouteCheckpoint[] Routes;
            internal NavArrivalProposal[] Assignments;
            internal LayeredNavigationScheduler.Checkpoint Scheduler;
        }
        internal sealed class RouteCheckpoint
        { internal int Candidate; internal NavSolveStatus Status; internal NavTypedRoute Route; }

        private readonly NavArrivalInput input;
        private readonly NavArrivalMember[] members;
        private readonly int[] pageStarts, pageCounts;
        private readonly Dictionary<string, PreparedClearance> preparedClearances = new Dictionary<string, PreparedClearance>(StringComparer.Ordinal);
        private readonly List<int> preparationValidCandidateIndices = new List<int>();
        private int preparationCursor, preparationPageIndex;
        private string preparingClearanceKey;
        private int[][] preparationCandidateIndices;
        private NavTerminalRegion[] preparationRegions;
        private readonly List<RouteRow> pageRequests = new List<RouteRow>();
        private readonly Dictionary<int, RouteRow> pageResults = new Dictionary<int, RouteRow>();
        private readonly List<NavArrivalProposal> assignments = new List<NavArrivalProposal>();
        private LayeredNavigationScheduler scheduler = new LayeredNavigationScheduler();
        private long lastEpoch = -1;
        private int allocationRemaining, memberIndex, pageIndex, candidateCursor, occupantCursor;
        private bool candidateValidated, candidateBlocked, memberSawBlocked, memberAllUnreachable = true;
        private int memberRouteCount;
        private NavArrivalStatus memberFailure = NavArrivalStatus.Pending;
        private Phase phase;

        public NavigationArrivalWork(NavArrivalInput input)
        {
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            members = input.CopyMembers();
            BuildPages(input.Source.Candidates, out pageStarts, out pageCounts);
            candidateCursor = 0;
            Status = members.Length > LayeredNavigationScheduler.MaxQueue ? NavArrivalStatus.CapacityExceeded : NavArrivalStatus.Pending;
            phase = Status == NavArrivalStatus.Pending ? Phase.PrepareRegions : Phase.Finished;
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
            OccupancyWorkConsumed = saved.OccupancyWorkConsumed;
            memberIndex = saved.MemberIndex; pageIndex = saved.PageIndex; candidateCursor = saved.CandidateCursor;
            occupantCursor = saved.OccupantCursor; candidateValidated = saved.CandidateValidated;
            candidateBlocked = saved.CandidateBlocked; memberSawBlocked = saved.MemberSawBlocked;
            memberAllUnreachable = saved.MemberAllUnreachable; memberRouteCount = saved.MemberRouteCount;
            memberFailure = saved.MemberFailure;
            preparationCursor = saved.PreparationCursor; preparationPageIndex = saved.PreparationPageIndex;
            preparingClearanceKey = saved.PreparingClearanceKey;
            preparationValidCandidateIndices.AddRange(saved.PreparationValidCandidateIndices ?? Array.Empty<int>());
            preparationCandidateIndices = CloneJagged(saved.PreparationCandidateIndices);
            preparationRegions = saved.PreparationRegions == null ? null : (NavTerminalRegion[])saved.PreparationRegions.Clone();
            foreach (var prepared in saved.PreparedClearances ?? Array.Empty<PreparedCheckpoint>()) {
                if (prepared == null || prepared.ClearanceKey == null || prepared.CandidateIndices == null ||
                    prepared.Regions == null || prepared.CandidateIndices.Length != pageStarts.Length ||
                    prepared.Regions.Length != pageStarts.Length || preparedClearances.ContainsKey(prepared.ClearanceKey))
                    throw new ArgumentException("Invalid prepared terminal region checkpoint.");
                preparedClearances.Add(prepared.ClearanceKey, new PreparedClearance {
                    CandidateIndices = CloneJagged(prepared.CandidateIndices), Regions = (NavTerminalRegion[])prepared.Regions.Clone() });
            }
            Status = saved.Status; phase = (Phase)saved.Phase; assignments.AddRange(saved.Assignments ?? Array.Empty<NavArrivalProposal>());
            if (saved.Requests == null || saved.RequestCandidates == null || saved.Requests.Length != saved.RequestCandidates.Length || saved.Routes == null)
                throw new ArgumentException("Invalid arrival checkpoint rows.");
            for (int i = 0; i < saved.Requests.Length; i++) pageRequests.Add(new RouteRow { Request = saved.Requests[i], Candidate = saved.RequestCandidates[i] });
            foreach (var result in saved.Routes) {
                if (result == null || pageResults.ContainsKey(result.Candidate)) throw new ArgumentException("Duplicate arrival route checkpoint.");
                var row = pageRequests.SingleOrDefault(x => x.Candidate == result.Candidate);
                if (row == null) throw new ArgumentException("Unknown arrival route checkpoint.");
                row.Status = result.Status; row.Route = result.Route; pageResults.Add(result.Candidate, row);
            }
            scheduler = LayeredNavigationScheduler.Restore(saved.Scheduler, pageRequests.Select(x => x.Request));
        }

        public NavArrivalStatus Status { get; private set; }
        public string InputBinding => input.StableKey;
        public IReadOnlyList<NavArrivalProposal> Proposals => Status == NavArrivalStatus.Ready
            ? Array.AsReadOnly(assignments.ToArray()) : Array.AsReadOnly(Array.Empty<NavArrivalProposal>());
        public int PendingRouteCount => scheduler.PendingCount;
        public int ReadyRouteCount => scheduler.ReadyCount;
        public long LogicalEpoch => lastEpoch;
        public long AllocationWorkConsumed { get; private set; }
        public long CandidateWorkConsumed { get; private set; }
        public long OccupancyWorkConsumed { get; private set; }
        public int MemberCursor => memberIndex;
        public int TerminalPageCount => pageStarts.Length;
        public int CandidateCursor => candidateCursor;
        public int OccupancyCursor => occupantCursor;
        public string WorkPhase => phase.ToString();
        public NavSchedulerCounters RouteCounters => scheduler.Counters;
        public int AllocationQuotaRemaining => allocationRemaining;
        public long LastAdvanceConsumed { get; private set; }

        public bool ValidateAgainst(NavArrivalInput currentSnapshot)
        {
            bool valid = currentSnapshot != null && input.StableKey == currentSnapshot.StableKey &&
                input.Provider.Binding == currentSnapshot.Provider.Binding &&
                input.TopologyIdentity == currentSnapshot.TopologyIdentity &&
                input.ProfileIdentity == currentSnapshot.ProfileIdentity &&
                input.Source.StableKey == currentSnapshot.Source.StableKey &&
                input.Occupancy.StableKey == currentSnapshot.Occupancy.StableKey &&
                input.Members.Count == currentSnapshot.Members.Count &&
                input.Members.Select(x => x.StableKey).SequenceEqual(currentSnapshot.Members.Select(x => x.StableKey));
            if (!valid) { Status = NavArrivalStatus.Stale; phase = Phase.Finished; assignments.Clear(); }
            return valid;
        }

        public NavArrivalStatus Advance(long logicalEpoch, int allocationQuota, NavEpochQuota navQuota)
        {
            LastAdvanceConsumed = 0;
            if (Status != NavArrivalStatus.Pending && Status != NavArrivalStatus.BudgetExhausted) return Status;
            if (logicalEpoch < 0 || allocationQuota < 0 || navQuota == null || logicalEpoch < lastEpoch) {
                Status = NavArrivalStatus.InvalidInput; phase = Phase.Finished; return Status;
            }
            if (logicalEpoch != lastEpoch) { lastEpoch = logicalEpoch; allocationRemaining = allocationQuota; }
            Status = NavArrivalStatus.Pending;
            long advanceWorkStart = AllocationWorkConsumed;
            while (phase != Phase.Finished) {
                if (memberIndex >= members.Length) { Status = NavArrivalStatus.Ready; phase = Phase.Finished; break; }
                if (phase == Phase.PrepareRegions) {
                    var clearance = members[memberIndex].Clearance;
                    string clearanceKey = ClearanceKey(clearance);
                    if (preparedClearances.ContainsKey(clearanceKey)) { BeginCandidateScan(); continue; }
                    if (preparingClearanceKey != clearanceKey) BeginPreparation(clearanceKey);
                    if (preparationPageIndex >= pageStarts.Length) {
                        preparedClearances.Add(clearanceKey, new PreparedClearance {
                            CandidateIndices = preparationCandidateIndices, Regions = preparationRegions });
                        preparingClearanceKey = null; preparationCandidateIndices = null; preparationRegions = null;
                        BeginCandidateScan(); continue;
                    }
                    int preparationEnd = pageStarts[preparationPageIndex] + pageCounts[preparationPageIndex];
                    if (preparationCursor >= preparationEnd) {
                        int[] valid = preparationValidCandidateIndices.ToArray();
                        preparationCandidateIndices[preparationPageIndex] = valid;
                        if (valid.Length > 0) {
                            var locations = valid.Select(i => input.Source.Candidates[i]).ToArray();
                            preparationRegions[preparationPageIndex] = new NavTerminalRegion(
                                "arrival-" + input.Source.StableKey.Substring(0, 20) + "-" + clearanceKey.Substring(0, 20) + "-" + preparationPageIndex,
                                input.Source.Revision, locations[0], locations);
                        }
                        preparationPageIndex++; preparationValidCandidateIndices.Clear();
                        preparationCursor = preparationPageIndex < pageStarts.Length ? pageStarts[preparationPageIndex] : 0;
                        continue;
                    }
                    if (allocationRemaining == 0) { Status = NavArrivalStatus.BudgetExhausted; break; }
                    SpendCandidate();
                    int rawIndex = preparationCursor++;
                    if (input.Provider.IsValid(input.Source.Candidates[rawIndex], clearance))
                        preparationValidCandidateIndices.Add(rawIndex);
                    continue;
                }
                if (phase == Phase.ScanCandidates) {
                    if (pageIndex >= pageStarts.Length) { FinishMember(); continue; }
                    var prepared = preparedClearances[ClearanceKey(members[memberIndex].Clearance)];
                    var validCandidates = prepared.CandidateIndices[pageIndex];
                    if (validCandidates.Length == 0) { NextPage(); continue; }
                    if (candidateCursor >= validCandidates.Length) { phase = Phase.SolvePage; continue; }
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
                    if (!scheduler.Submit(request)) { Status = NavArrivalStatus.CapacityExceeded; phase = Phase.Finished; break; }
                    pageRequests.Add(new RouteRow { Candidate = candidateIndex, Request = request });
                    memberRouteCount++;
                    AdvanceCandidate();
                    continue;
                }
                if (phase == Phase.SolvePage) {
                    if (pageRequests.Count == 0) { NextPage(); continue; }
                    scheduler.Advance(logicalEpoch, navQuota, _ => true);
                    while (allocationRemaining > 0 && scheduler.TryTake(_ => true, out var result)) {
                        SpendCandidate();
                        var row = pageRequests.Single(x => x.Request.Subscription.StableKey == result.Subscription.StableKey);
                        row.Status = result.Status; row.Route = result.Route; pageResults.Add(row.Candidate, row);
                        if (result.Status != NavSolveStatus.UnreachableInGraph) memberAllUnreachable = false;
                        ObserveTerminalFailure(result.Status);
                    }
                    if (pageResults.Count == pageRequests.Count) {
                        if (memberFailure != NavArrivalStatus.Pending) {
                            Status = memberFailure; phase = Phase.Finished; assignments.Clear(); break;
                        }
                        var ready = pageResults.Values.Where(x => x.Status == NavSolveStatus.Ready)
                            .OrderBy(x => x.Candidate).FirstOrDefault();
                        if (ready != null) {
                            var member = members[memberIndex]; var endpoint = input.Source.Candidates[ready.Candidate];
                            var proof = new NavArrivalReachabilityProof(input, member, endpoint, ready.Request, ready.Route);
                            assignments.Add(new NavArrivalProposal(member, endpoint, ready.Route, proof));
                            NextMember(); continue;
                        }
                        pageRequests.Clear(); pageResults.Clear(); NextPage(); continue;
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
        }

        private void FinishMember()
        {
            if (memberFailure != NavArrivalStatus.Pending) Status = memberFailure;
            else if (memberRouteCount > 0 && memberAllUnreachable && !memberSawBlocked) Status = NavArrivalStatus.UnreachableInGraph;
            else Status = NavArrivalStatus.NoAvailableSlot;
            phase = Phase.Finished; assignments.Clear();
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
            candidateValidated = false; candidateBlocked = false; pageRequests.Clear(); pageResults.Clear(); phase = Phase.PrepareRegions;
        }
        private void BeginCandidateScan()
        { pageIndex = 0; candidateCursor = 0; occupantCursor = 0; candidateValidated = false; candidateBlocked = false; phase = Phase.ScanCandidates; }
        private void BeginPreparation(string clearanceKey)
        {
            preparingClearanceKey = clearanceKey; preparationPageIndex = 0;
            preparationCursor = pageStarts.Length == 0 ? 0 : pageStarts[0];
            preparationValidCandidateIndices.Clear();
            preparationCandidateIndices = new int[pageStarts.Length][];
            preparationRegions = new NavTerminalRegion[pageStarts.Length];
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
            return new NavScheduledRequest(input.Provider, member.Clearance, input.Profile, input.Limits,
                preparedClearances[ClearanceKey(member.Clearance)].Regions[pageIndex], subscription, input.MaxLabels,
                singleton: input.Provider is PartitionNavigationProvider && members.Length == 1);
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
            return new Checkpoint { Version = 2, InputKey = input.StableKey, LastEpoch = lastEpoch,
                AllocationWorkConsumed = AllocationWorkConsumed, CandidateWorkConsumed = CandidateWorkConsumed,
                OccupancyWorkConsumed = OccupancyWorkConsumed,
                AllocationRemaining = allocationRemaining, MemberIndex = memberIndex, PageIndex = pageIndex,
                CandidateCursor = candidateCursor, OccupantCursor = occupantCursor, CandidateValidated = candidateValidated,
                PreparationCursor = preparationCursor, PreparationPageIndex = preparationPageIndex,
                PreparingClearanceKey = preparingClearanceKey,
                PreparationValidCandidateIndices = preparationValidCandidateIndices.ToArray(),
                PreparationCandidateIndices = CloneJagged(preparationCandidateIndices),
                PreparationRegions = preparationRegions == null ? null : (NavTerminalRegion[])preparationRegions.Clone(),
                PreparedClearances = preparedClearances.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new PreparedCheckpoint {
                    ClearanceKey = x.Key, CandidateIndices = CloneJagged(x.Value.CandidateIndices),
                    Regions = (NavTerminalRegion[])x.Value.Regions.Clone() }).ToArray(),
                CandidateBlocked = candidateBlocked, MemberSawBlocked = memberSawBlocked,
                MemberAllUnreachable = memberAllUnreachable, MemberRouteCount = memberRouteCount, MemberFailure = memberFailure,
                Status = Status, Phase = (int)phase, PageStarts = (int[])pageStarts.Clone(), PageCounts = (int[])pageCounts.Clone(),
                Requests = pageRequests.Select(x => x.Request).ToArray(), RequestCandidates = pageRequests.Select(x => x.Candidate).ToArray(),
                Routes = pageResults.Values.OrderBy(x => x.Candidate).Select(x => new RouteCheckpoint { Candidate = x.Candidate, Status = x.Status, Route = x.Route }).ToArray(),
                Assignments = assignments.ToArray(), Scheduler = scheduler.Save() };
        }
        public static NavigationArrivalWork Restore(NavArrivalInput exactInputs, Checkpoint checkpoint)
            => new NavigationArrivalWork(exactInputs, checkpoint);

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
