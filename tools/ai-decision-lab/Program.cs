using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

// Links the production sources; no Unity, alternate economic rules or world stepping.
internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    private static int Main(string[] args)
    {
        try
        {
            object result;
            if (args.Length == 1 && args[0] == "check") result = Checks.Run();
            else if (args.Length == 2 && args[0] == "inspect")
            {
                var input = JsonSerializer.Deserialize<Scenario>(File.ReadAllText(args[1]), Json)
                    ?? throw new ArgumentException("Empty scenario.");
                input.Validate();
                result = Lab.Inspect(input);
            }
            else throw new ArgumentException("Usage: ai-decision-lab check | inspect scenario.json");
            Console.WriteLine(JsonSerializer.Serialize(result, Json));
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "FAIL", error = e.ToString() }, Json));
            return 1;
        }
    }
}
internal sealed record Scenario
{
    public required string Schema { get; init; }
    public required string Name { get; init; }
    public required string Provenance { get; init; }
    public required int Seed { get; init; }
    public required long Tick { get; init; }
    public required int Cash { get; init; }
    public required int Tanks { get; init; }
    public required bool ScienceCenter { get; init; }
    public required double SettledIncome { get; init; }
    public required int ProtectedFunds { get; init; }
    public void Validate()
    {
        if (Schema != "ai-decision-case-v1" || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Provenance)
            || Tick < 0 || Cash < 0 || Tanks < 0 || Tanks > 100 || ProtectedFunds < 0 || ProtectedFunds > Cash
            || !double.IsFinite(SettledIncome) || SettledIncome < 0)
            throw new ArgumentException("Invalid bounded decision scenario.");
    }
}
internal static class Lab
{
    internal static readonly PlayableProfile Profile = PlayableProfile.Default;
    internal static readonly AiProfile Ai = AiProfile.Initial;
    internal static Scenario Case(int tanks = 10, int cash = 600, bool center = false) => new() {
        Schema = "ai-decision-case-v1", Name = "funding-boundary", Provenance = "Authored decision fixture, not a captured match",
        Seed = 41, Tick = 600, Cash = cash, Tanks = tanks, ScienceCenter = center, SettledIncome = 0, ProtectedFunds = 0
    };
    internal static PlayableSnapshot Snapshot(Scenario s)
    {
        s.Validate();
        var p = Profile;
        var site = TerritoryRules.Sites(p).First(x => x.Kind == PlayableBuildingKind.Headquarters);
        var buildings = new List<PlayableBuildingSnapshot> {
            new(1, PlayableOwner.Player, PlayableBuildingKind.Headquarters, site.Position, p.HeadquartersHealth, 1, 0, 0, default, siteId: site.Id),
            new(2, PlayableOwner.Player, PlayableBuildingKind.Factory, site.Slots[0].Position, p.FactoryHealth, 1, 0, 0, default, siteId: site.Id, slotId: site.Slots[0].Id, parentId: 1),
            new(3, PlayableOwner.Player, PlayableBuildingKind.Refinery, site.Slots[1].Position, p.RefineryHealth, 1, 0, 0, default, siteId: site.Id, slotId: site.Slots[1].Id, parentId: 1)
        };
        if (s.ScienceCenter) buildings.Add(new(4, PlayableOwner.Player, PlayableBuildingKind.ScientificCenter,
            site.Slots[2].Position, TerritoryRules.Health(p, PlayableBuildingKind.ScientificCenter), 1, 0, 0, default, siteId: site.Id, slotId: site.Slots[2].Id, parentId: 1));
        var units = Enumerable.Range(10, s.Tanks).Select(i => new PlayableEntitySnapshot(i, PlayableOwner.Player,
            PlayableEntityKind.Tank, new NavPoint(i, 0), p.TankHealth, false, 0, 0, 0)).ToArray();
        return new PlayableSnapshot(p.ProfileId, p.Revision, 71, s.Seed, s.Tick, s.Tick, RuntimeStatus.Running,
            false, PlayableMatchOutcome.Playing, s.Cash, null, units, buildings.ToArray(),
            Array.Empty<PlayableProjectileSnapshot>(), new PlayableRuntimeMetrics(0, 0, 0, 0, 0), null,
            sites: new[] { new TerritorySiteSnapshot(site, PlayableOwner.Player, PlayableOwner.Player, 1, false, 1, true) },
            population: new PlayablePopulationSnapshot(s.Tanks * PlayableUnitRules.Population(p, PlayableEntityKind.Tank), 0, p.ArmyCapacity),
            researchAvailability: AiResearchCatalog.All.Select(d => new PlayableResearchAvailabilitySnapshot(d.Kind, d.Cost(p), true)).ToArray(),
            activeProfile: p, homeSiteId: site.Id, settledIncome: s.SettledIncome);
    }
    internal static AiTechnologyPlan Technology(Scenario s)
    {
        var o = PlayableAiObservation.From(Snapshot(s)); var ledger = new AiBudgetLedger(o.OwnerId, o.Generation);
        ledger.ObserveBank(s.Cash);
        return new AiTechnologyPlanner().Plan(o, Profile, Ai, ledger.Capture());
    }
    internal static PlayableAiOwnerLoop Review(Scenario s)
    {
        var snapshot = Snapshot(s); var o = PlayableAiObservation.From(snapshot);
        var opening = PlayableAiOpeningComposition.Initialize(s.Seed, o.OwnerId, aiProfile: Ai);
        var loop = new PlayableAiOwnerLoop(Profile, opening, o.Generation);
        loop.Review(snapshot, 0);
        return loop;
    }
    internal static object Inspect(Scenario s)
    {
        var timer = Stopwatch.StartNew();
        var plan = Technology(s); var funding = new Funding(s); funding.Observe(s);
        var loop = Review(s); var checkpoint = loop.Checkpoint;
        return new {
            schema = "ai-decision-result-v1", scope = "decision-only; no movement, match or Unity acceptance",
            input = s, gameplay = new { Profile.ProfileId, Profile.Revision },
            ai = new { Ai.Id, Ai.Revision, Ai.Hash }, scienceFloor = Ai.Value("technology.minimumUnitsForScienceCenter"),
            isolatedTechnology = new { plan.Reason, goal = plan.FundingGoal?.Research, action = plan.Action?.Kind,
                branches = plan.Branches.Select(b => new { research = b.Descriptor.Kind, b.Progress, b.Utility, b.AffectedValue, b.MissingCost }) },
            isolatedFunding = new { funding.Ledger.Available, funding.Ledger.Reserved,
                entries = funding.Ledger.Capture().Reserved.Select(e => new { e.Id, e.Amount, e.CreatedTick, e.ExpiresTick }) },
            // This is the real owner Review, not our reconstruction of its arbitration.
            ownerReview = new { opening = checkpoint.Opening.Opening, technologyReason = loop.TechnologyPlan?.Reason,
                economy = checkpoint.EconomyDecision, checkpoint.LastPolicy, checkpoint.LastActionKind },
            decisionMilliseconds = timer.Elapsed.TotalMilliseconds
        };
    }
}
internal sealed class Funding
{
    internal readonly AiBudgetLedger Ledger;
    internal readonly AiDecisionArbiter Arbiter = new(Lab.Ai);
    private readonly AiTechnologyFunding funding;
    internal Funding(Scenario s)
    {
        var o = PlayableAiObservation.From(Lab.Snapshot(s));
        Ledger = new(o.OwnerId, o.Generation); funding = new(o.OwnerId, o.Generation);
    }
    internal void Observe(Scenario s)
    {
        var o = PlayableAiObservation.From(Lab.Snapshot(s));
        Ledger.ObserveBank(s.Cash);
        Ledger.Expire(s.Tick, e => !AiTechnologyFunding.IsFunding(e) || funding.Valid(e, o, Lab.Profile, Ledger, Arbiter));
        funding.Reconcile(o, Ledger, Arbiter);
        var plan = new AiTechnologyPlanner().Plan(o, Lab.Profile, Lab.Ai, Ledger.Capture(), preferredFunding: funding.Preferred(Arbiter));
        funding.Observe(o, Lab.Profile, Lab.Ai, Ledger, Arbiter, plan, true, s.ProtectedFunds);
    }
}
internal static class Checks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static object Run()
    {
        var results = new List<object>();
        void Check(string name, Action action) { var t = Stopwatch.StartNew(); try { action(); } catch (Exception e) { throw new Exception(name + ": " + e.Message, e); } results.Add(new { name, status = "PASS", milliseconds = t.Elapsed.TotalMilliseconds }); }
        int floor = (int)Lab.Ai.Value("technology.minimumUnitsForScienceCenter");
        Check("science-floor-current-profile", () => {
            Require(floor == 10, "Approved science floor changed; review the scenario contract.");
            Require(Lab.Technology(Lab.Case(floor - 1)).FundingGoal == null, "Below-floor new science must wait.");
            Require(Lab.Technology(Lab.Case(floor)).FundingGoal != null, "At-floor useful science must remain visible.");
        });
        Check("legacy-three-unit-funding-fixture-is-below-current-floor", () => {
            var s = Lab.Case(3, 176) with { Tick = 0, ProtectedFunds = 100 }; var f = new Funding(s); f.Observe(s);
            Require(f.Ledger.Reserved == 0, "Three living units must not create a new science hold under floor 10.");
        });
        Check("existing-center-survives-unit-loss", () => Require(Lab.Technology(Lab.Case(3, 600, true)).Action?.Kind == PlayableCommandKind.QueueResearch,
            "The new-center floor must not block useful existing-center research."));
        Check("partial-funding-protects-committed-money", () => {
            var s = Lab.Case(floor, 176) with { Tick = 0, ProtectedFunds = 100 }; var f = new Funding(s); f.Observe(s);
            Require(f.Ledger.Reserved == 76 && f.Ledger.Available == 100 && f.Ledger.PaidTotal == 0, "Savings must protect 100 without inventing spending.");
            var first = f.Ledger.Capture().Reserved.Single(); f.Observe(s with { Tick = 300 });
            Require(f.Ledger.Capture().Reserved.Single().ExpiresTick == first.ExpiresTick, "Unchanged cash cannot renew expiry.");
            f.Observe(s with { Tick = 600, Cash = 231, SettledIncome = 55 });
            Require(f.Ledger.Reserved == 131 && f.Ledger.Capture().Reserved.Single().CreatedTick == 600, "Actual income must grow the hold.");
        });
        Check("expired-savings-are-not-new-income", () => {
            var s = Lab.Case(floor, 176) with { Tick = 0 }; var f = new Funding(s); f.Observe(s);
            long expiry = f.Ledger.Capture().Reserved.Single().ExpiresTick;
            f.Observe(s with { Tick = expiry + 1 }); f.Observe(s with { Tick = expiry * 3 });
            Require(f.Ledger.Reserved == 0, "Released cash must not restart a funding loop.");
            f.Observe(s with { Tick = expiry * 3 + 1, Cash = 196, SettledIncome = 20 });
            Require(f.Ledger.Reserved == 196, "Actual income should allow recovery.");
        });
        Check("arbiter-cannot-spend-science-reservation-on-production", () => {
            var s = Lab.Case(floor, 176) with { Tick = 0 }; var f = new Funding(s); f.Observe(s);
            var o = PlayableAiObservation.From(Lab.Snapshot(s));
            var action = new PlayableAiAction(1, o.OwnerId, o.ProfileId, o.ProfileRevision, o.Generation, o.SnapshotSequence,
                PlayableCommandKind.QueueTank, new[] { 2 }, unitKind: PlayableEntityKind.Tank);
            var intent = new AiIntent("production-probe", "production", action, 300, Lab.Profile.TankCreditCost, 0, Array.Empty<string>());
            Require(f.Arbiter.Select(new[] { intent }, 0, s.Cash, Lab.Profile.ArmyCapacity, 1, f.Ledger).Count == 0,
                "Discretionary production must not consume reserved science money.");
        });
        Check("actual-owner-review-repeatable-and-funded", () => {
            var s = Lab.Case(floor); var a = Lab.Review(s).Checkpoint.EconomyDecision; var b = Lab.Review(s).Checkpoint.EconomyDecision;
            Require(a != null && a.Candidates.Count > 0, "Actual owner arbitration was not reached.");
            Require(JsonSerializer.Serialize(a, Program.Json) == JsonSerializer.Serialize(b, Program.Json), "Same input must produce the same decision.");
            Require(a.Available >= 0 && a.Reserved + a.Unpaid + a.SafetyReserve <= a.Liquid, "Decision overspent its real ledger.");
        });
        return new { schema = "ai-decision-checks-v1", status = "PASS", count = results.Count,
            scope = "Selected decision contracts only; not existing NUnit suite replacement or gameplay acceptance", cases = results };
    }
}
