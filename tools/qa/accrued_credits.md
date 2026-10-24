# Q3 B: bounded navigation credits

Fresh ordinary offline async runtime explicitly selects
`native-per-unit-accrued-credits-v3` before host birth. Existing Session and
Authority constructors, fixed mode and saved v1/v2 remain non-accruing. Restore
uses saved authority/host/bank mode, including Replay composition.

Authority accepts at most one `(tick, admission prefix, B, profile revision)` per
actual unpaused boundary with unfinished demand. Independent immutable bank
publication leaves the worker's expected HostRoot unchanged. At Idle, the worker
atomically consumes completed work and promotes the oldest waiting credit. Pump,
SetBudget, cold restore and paused wall time do not issue credits. Unfinished
credits keep their exact prefix and B vector; cancellation does not refund them.

`navigation.maxAccruedCredits` governs waiting credits, separately from the active
quantum. Range 1..600, integer step1, unit квант. The named Balance Lab revision
`spacewars-accrued-navigation @1` defaults to600 (existing 20s ×30Hz horizon).
Shipped `spacewars-default @1` is unchanged. Legacy profile absence stays absent;
only explicit optional extensions add profile/transaction binding bytes. Shrink
keeps oldest credits, saturation drops newest; no demand purges waiting credits.

The ring is existing owned `NavPagedList<long>`: Count2412, page256, capacity2560,
10 pages. Actual initial charge22256B includes directory/list/page metadata;
20480B is only raw long capacity. Header-only pop/purge/shrink and append admit
COW allocations in the same Session64MiB ledger; wire remains32MiB. Legacy
HostRoot layout and envelope are unchanged. Physical value90 under
HostRootFairArrivalV1/HostEnvelopePublication identifies the composed v3 root and
new concrete envelope; lifecycle32 coexists with Replay16. Full capture/decode
validates bank algebra, ranges, ordering, generation, request prefix and world
boundary. Ordinary append/promotion uses local source checks, no bank scan.

Actual C# source qualification (source and compiled DLL/PDB preserved in output):

```
python3 tools/qa/accrued_credits.py --out .local/accrued-credits-<id>/source
python3 tools/qa/accrued_credits.py --legacy --source-pin <baseline> --out .local/accrued-credits-<id>/legacy-baseline
python3 tools/qa/accrued_credits.py --legacy --out .local/accrued-credits-<id>/legacy-candidate
```

Compare legacy `*.bin` and `result.json` byte for byte. They cover v2 fixed/async
and Replay off/on. Source checks cover bank entitlement/COW/refusal/cold-next,
full WorldWire v3 states, actual held threaded ArrivalWork append/commit,
blocked promotion availability wake, oldB lane use despite SetBudget and
optional profile history. Unity affected selection and first ordinary4xArmy600
are separate evidence, with original seeds/bounds/assertions unchanged. The
ordinary witness may use `tools/qa/army_cow_ordinary.py --accrued-credits` to
record bounded credit scalar counts in the existing observer and save actual
compiled Unity DLL/PDB bytes. Use `tools/unity.sh shared` and `-noaudio -muted`
from startup; these routes do not build a Player. No CPU, Player, human, full,
release or deployment acceptance follows from component/Editor PASS.

## Current-main integration control

The integration baseline is `main@2b92e91c`. It has no Replay policy. For direct
legacy byte/charge comparisons against that baseline, pass
`--legacy --main-legacy-control` on both sides; the explicit control preserves
the old saved mode and labels its actual Replay setting in the report. It is
not Replay-ON qualification. The default `--legacy` mode retains the original
fixed/async × Replay OFF/ON coverage and rejects a baseline lacking Replay.

The integration packet also compares 24 worlds plus the report on an isolated
actual-source control: current main plus only the Replay delta and its required
writer-reservation seam, versus the candidate with B disabled, in both Replay
modes. Its private-index tree, exact patch, full source bindings and compiled
outputs are retained. No main/worktree mutation is needed for this control.
`replay_only_projection.py` uses neutral declarations from the exact producer
export; see `tools/testdata/replay-actual214-declarations.md`. The candidate does
not decode the historical Work fixture.
