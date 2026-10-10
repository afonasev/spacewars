# Native headless run/replay (F7)

The .NET 8 CLI only orchestrates an actual Unity executable. There is no .NET game solver, synthetic winner, LLM or network dependency. A restored .NET SDK is sufficient for offline execution; the project has no NuGet dependencies.

Explicit minimal QA build, from a clean committed task worktree:

```sh
python3 tools/ai-sim/build-worker.py --output .local/ai-worker
```

The build uses `tools/unity.sh shared`, Unity 6000.3.23f1 and an empty scene with only the headless authority bootstrap. Current v1 build target is macOS arm64. The script produces a worker bundle, identity, build logs and seven fully bound manifests. It does not package or publish a release. Generated scene/build-identity assets are removed by the Editor entrypoint.

```sh
/Users/eaafonasev/.dotnet/dotnet run --project tools/ai-sim -- run --manifest .local/ai-worker/duel-coverage-v1.json --output .local/duel-run
/Users/eaafonasev/.dotnet/dotnet run --project tools/ai-sim -- replay --manifest .local/duel-run/replay-inputs.json --output .local/duel-replay
```

Use the obstacle manifest the same way. Each output directory must be new or empty. Preserve the worker bundle byte-for-byte; the manifest binds both the executable and the complete bundle. Paths can be relocated together by editing only `WorkerRoot`, `Executable`, `Launcher` (relative paths resolve from the input manifest directory). Runtime identity includes exact code, gameplay, AI/catalog, map/geometry, roster/teams/control/difficulty, seed, generation, Unity/platform/architecture, NavMesh bake settings and fixed route barrier. Cross-platform equality is not promised.

Schema `native-ai-run-v1` requires every declared property, including explicit `Expected: null` for fresh runs. Unknown fields, duplicate fields, unsupported fixtures/configurations, omitted seeds, malformed hashes and incompatible worker identities are rejected. v1 exposes seven authored diagnostic fixtures and an ordered list of ordinary human command inputs. All AI owners currently share one selected difficulty, matching the authority API. These fixtures carry authored extra tanks and forced commands for coverage; they are excluded from strength scores and are not ordinary equal-genesis matches.

Outputs: bound manifest, technical summary, metrics, bounded decision/observed-fact/commitment trace, state hashes and world+AI checkpoints, failures, replay-input manifest, Unity and host logs. Logical checkpoint hashes include world and private AI state, not wall timing. Trace facts are the owner's fog-safe view at `ObservedTick`; the record separately retains the policy observation identity. Replay compares states, trace, termination and final tick. Headless batches call the same `PlayableAuthorityTick` and `UnityHostRouteService` as Player, with NavMesh on the main thread. There is no realtime simulation sleep.

Exit 0 means technically valid (including a timeout); 2 invalid/unavailable input; 3 invariant/runtime or replay mismatch; 4 interruption. Timeout never invents a winner. Invalid/interrupted runs preserve their input, failures and technical summary plus any partial worker outputs; unavailable simulation measurements remain absent. Ctrl-C interrupts the CLI process group; direct SIGTERM to the compiled CLI also records exit 4. Infrastructure failures remain visible in failures/technical summary. `metrics.json` timings are diagnostic wall/CPU observations on the admitted worker, not an exclusive performance benchmark or strategic qualification.

E6 economy diagnostics adds five fully bound manifests: `economy-rich-v1`,
`economy-low-v1`, `economy-full-slots-v1`, `economy-lost-hq-v1`, and
`economy-blocked-exit-v1`. These have west AI/east human, unchanged default
profiles, 5400 ticks and a 4096-record trace bound. Authored ready buildings
supply rich income; ordinary purchases drain the low-bank fixture; the full
slots fixture exercises conversion and capture; ordinary enemy AttackMove
combat destroys the HQ while an authored outpost survives; static authored
obstacles enclose the blocked factory. None injects money or changes rules.

`metrics.json.Economy` aggregates each AI owner on every actual authority tick:

* Spendable bank is current integer world bank minus actual accepted unpaid
  obligations, bounded held reservations and safety reserve, clamped to zero.
  Paid obligations are excluded. Latest/peak obligations are reported.
* Excess-bank ticks have a known useful legal affordable construction or a
  legal affordable idle usable production line. Consecutive run and ticks
  beyond the frozen `economy.excessBankDeadlineSeconds` are reported.
* Idle-affordable line ticks require an empty ready live nonselling factory,
  a compatible supported legal unit, available population and spendable funds,
  and a currently usable real exit for that unit. They are counted per line;
  consecutive durations and ticks beyond `economy.idleLineDeadlineSeconds`
  are reported. A paid completed head blocked at exit is a separate counter.
* Low-bank, full local slots, population block and absent HQ are independent
  observed conditions, not fabricated spend or a single inferred cause.
* Realized expansion income counts only observed positive world settlements
  from actual ready income sources on sites lacking an owned income center at
  genesis. Actual per-settlement total must match those ready sources. Up to
  32 example settlements are retained. Authored rich income is excluded from
  expansion; estimated future income, refunds and balance injection are excluded.

These are trusted local evaluator diagnostics and never policy inputs or new
save state. Real crowd availability may reveal hidden blockers to the evaluator;
this read stays outside AI observations and network projections. Deadline
metrics describe behavior and do not replace the independent frozen evaluation
policy or human/device acceptance. Timeout is a valid diagnostic run, never a
resource-based victory. Compare aggregate economy reports separately on
repeat/replay as well as the required state/decision/termination hashes.

A real lost-HQ checkpoint exposed a missing affordable scout at bank116:
Tank150 could not be bought, while the existing Explorer100 role was absent.
The bounded recovery alternative uses ordinary production admission only when
that existing role is missing, no live/queued Explorer exists, a public scout goal
is reachable, an owned idle ready producer has no known nearby threat, and
uncommitted funds afford Explorer but not Tank. Normal Tank composition and
prices remain unchanged; queued/live scouts suppress duplicate recovery.
The original a98 checkpoint and manifest are retained in
`unity/Tests/Fixtures/e6-lost-hq` for ordinary payment/restore/scout regressions.

Restoration of a missing role is independent of the previous scout Move clock.
The existing Move cadence remains unchanged; a dead actor cannot postpone
replacement production. The actual 787 checkpoint2400 is retained in
`unity/Tests/Fixtures/e6-lost-hq-recent-order` to cover this distinction.

`ProducedUnits` is the peak number of currently living owned entities absent at
 genesis, not a cumulative birth count. `AdditionalFactories` is the peak count
 of currently present non-genesis factories, including construction, not total
 factories built. Ordinary paid orders, queue/head and birth milestones provide
 the corresponding execution proof. The final economy QA worker has exact code
 identity52f25a8b; the later source edits only derive the legacy diagnostic human
 research input tick (3000, ready and funded before combat) and clarify docs/tests.
 Bootstrap executes manifest.Commands and never calls Template. Reproduction uses
 the bound52 worker plus the corrected explicit manifest; source binding maps
 those template-only changes without an unnecessary Player rebuild. The rejected
 old6000 input and earlier macro deadline reports remain raw evidence.
