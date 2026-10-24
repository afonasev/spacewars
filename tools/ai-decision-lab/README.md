# Fast AI decision lab

Runs the **same current production C# Runtime and Simulation sources** under .NET 8, without Unity Editor, graphics, workers or an alternate game implementation. Both source assemblies already declare `noEngineReferences`. No NuGet packages or network sources are used. `UNITY_INCLUDE_TESTS` is not defined: the ordinary production path is compiled, without diagnostic hooks.

From the repository root:

```sh
python3 tools/ai-decision-lab/run.py --output .local/ai-decision-lab/run-01
```

The launcher finds `dotnet` in PATH or `~/.dotnet/dotnet`; alternatively pass `--dotnet /absolute/path/to/dotnet`. Install a .NET 8 SDK if absent. The output directory must not already exist. Every invocation builds incrementally, then runs selected contracts and one inspectable scenario. It records source hashes, Git revision/dirty paths, SDK, copied input, output hashes, failures and separate build/check/inspection wall times. A changed source or failed/empty check is a failure, never a cached PASS. The executable supports direct warm use:

```sh
dotnet tools/ai-decision-lab/bin/Release/net8.0/ai-decision-lab.dll check
dotnet tools/ai-decision-lab/bin/Release/net8.0/ai-decision-lab.dll inspect tools/ai-decision-lab/cases/science-production.json
python3 tools/ai-decision-lab/test_runner.py
python3 tools/ai-decision-lab/audit.py --main main --candidate codex/ai-methodology-cycle-20261022
```

Use the launcher for evidence: direct DLL calls can be stale after edits and do not create source manifests. The Python tests use the already built Release executable, never Unity, and reject missing builds rather than silently skipping.

## What the first case answers

The authored case models competition for money around 603 credits, inspired by retained economic captures. It is **not an exact replay** of the old failing match. It contains one owned ready HQ/factory/refinery, ten Tanks and no prior decision history, income window, scout or enemy knowledge. Profile prices/rules come from production code. Change the explicit JSON inputs for focused exploration; unknown/missing fields, negative resources and invalid schema are rejected.

Output distinguishes two questions:

- `isolatedTechnology` / `isolatedFunding`: what the real technology planner/funding state would propose when allowed to fund, with the explicit `ProtectedFunds` input.
- `ownerReview`: what the **actual `PlayableAiOwnerLoop.Review`** chooses on the same observation, considering the opening, scout, economy, armies, technology and common arbiter. `ProtectedFunds` is not forced into this path: production calculates its own protections.

At the initial current-main baseline the isolated planner reserves 600 of 603 for TankChassis science. The ordinary owner review chooses GreedyMine income development and a missing Explorer, with reason `fund income for existing productive capacity`. This distinction is the point: an isolated policy PASS does not prove it wins real arbitration. It is not proof that the historical late-match defect is fixed or reproduced.

Seven bounded checks cover the approved 10-unit new-center boundary; the obsolete three-unit funding-fixture assumption; continued research at an existing center after unit loss; protected partial savings and real-income renewal; expiry without invented income; inability of discretionary production to spend a science reservation; and deterministic, funded actual-owner arbitration. Funding trajectories retain one real ledger/arbiter across observations. Owner inspection starts fresh; it does not fabricate paid receipts or model movement.

## Boundaries and next use

This tool compiles all Runtime/Simulation dependencies but executes only those selected decision contracts. It is not a full game test run. It neither imports nor changes existing NUnit tests and makes no assertion of equivalent NUnit discovery/coverage. Existing assertions, fixtures, FAIL and NotRun records remain intact. .NET execution does not certify Unity/Mono behavior, threaded scheduling, current-world restore, map legality, movement, rendering or performance of a match.

Use the fast layer to distinguish policy/funding/arbiter causes and build a regression before modifying runtime. Then choose the affected Unity contracts and one justified ordinary threaded witness for a gameplay patch. The existing `tools/ai-sim` is the separate actual Unity headless-world layer. No additional ordinary run is authorized by this tooling result.

Historical S1-S4 acceptance is a review of those versions, not proof of defect-free code. Reopen a concrete defect on its evidence; do not declare whole stages broken or repeat every old test automatically. New known issues and candidate dispositions are in `docs/evidence/ai-decision-lab-20261023/README.md`.
