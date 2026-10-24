# Exact Demand query diagnostic

QA-only source, outside Unity Assets. Runtime baseline: `3ca208890555a4d104a4d00efb73fffcb50eefc2`; actor composite: `6cb4f7892346236199f5258a9f9d6f296e79082c` (contains that exact Nav candidate). This helper does not change either runtime. No shipping API, reconstructed provider, world dump, game watchdog extension, full suite, build or publication.

## ONE actor capture, after root dispatch

Copy only the `BEGIN ACTOR COPY` / `END ACTOR COPY` class from `ExactDemandDiagnostic.cs` into the existing actor diagnostic source/assembly. Required usings: `System`, `System.IO`, `System.Linq`, `System.Reflection`, `Spacewars.Runtime`, `Spacewars.Simulation`. The exact class also compiles in `Spacewars.Input.PlayModeTests`; it does not reference Simulation-internal types at compile time.

Call from the prefix's OUTER observer loop, outside existing `RunHostStep` / `CaptureRootUnderGate`. The helper itself obtains a normal committed-host pin and selects from that immutable root under `RunHostStep`. It then exits the transport gate, writes the exact recipe BODY while keeping the pin alive, closes Capture scope, transfers public bytes, and releases the pin in `finally`. It never performs IO or yields. Do not wrap this call in another host/transport gate.

```csharp
// Keep these across observer samples; do not reset on rebind.
bool exactCaptureAttempted = false;
long[] exactOriginalCore = { 10, 12, 14 }; // Frozen ORIGINAL command cohort,
                                        // not a member selector or live roster.
byte[] exactBody = null;
object exactMetadata = null;
string exactReason = null;

// In the existing OUTER prefix observer loop, once firstGroup/firstSequence
// identify the original first-army command (do not hardcode IssuedTick).
if (!exactCaptureAttempted) {
    bool matched = ExactDemandCaptureQa.TryCurrent(
        port, session, firstGroup, firstSequence, exactOriginalCore,
        "031f2ea8fd28c0efce2fc254b1840914d257c5ed76bbbd7facc9cd720edceb78",
        out exactBody, out exactMetadata, out exactReason);
    if (matched) {
        exactCaptureAttempted = true; // Includes refusal: NEVER retry it.
        // All pins/scopes already released. Existing durable IO sink is safe here.
        if (exactBody != null) File.WriteAllBytes(exactBodyPath, exactBody);
        // Save exactMetadata + exactReason with existing JSON sink, and SHA-256
        // of exactBody. Record source6cb, helper hash, test DLL, prefix test SHA,
        // observation tick, capture wall time and ledger headroom separately.
    }
}
// At the unchanged prefix end: if !exactCaptureAttempted, report NotCaptured:
// exact current query not observed. Do not extend the original 600 ticks.
```

Selection policy `current-ordered-core-member-v2` chooses `input.Members[work.MemberCursor]` from the pinned CURRENT work, with MemberCursor==0. There is no literal member selector: asynchronous ordering selected Entity14 in one prior run and Entity10 in the first capture run. The first capture was NotCaptured, matched=false, with no Capture scope invoked. Neither observation determines the next run's member. The immutable input must contain exactly the original three distinct core IDs, all with the original group/command. `NavArrivalInput` sorts by member.Order, EntityId, Incarnation; `NavigationArrivalWork` copies that same ordered list.

Only work whose current input provider matches the fixed post-rebind binding above is eligible; earlier pre-rebind work is not a capture target. Within eligible work there must be exactly ONE pending Demand query. Its recipe must be Query+GroupQuery+DemandV3 with C3, reference that member's current working provider, and match the fixed provider binding, current clearance/profile/limits and complete current subscription tuple: world/entity/group/root order/command/issued tick/activation/member order/request/incarnation/mobility/origin/HoldBinding/provenance. This mapping is taken directly from `NavigationArrivalWork.CreateRequest`. The metadata records the selection policy, original and actual ordered core, member.Order/member key, full actual subscription tuple, endpoint and recipe/provider identities. It reads the CURRENT scheduler, never the historical oracle scheduler.

No eligible pending query means matched=false and eventual NotCaptured if the unchanged prefix ends. Ambiguity, wrong tuple/policy/provider within eligible work or Capture refusal means matched=true with NotCaptured and no retry; it never chooses a different member as fallback. Scope admission, same-ledger capture validation, external metadata/provider closure and output growth use unchanged existing code. Do not substitute another binding, copy a public recipe into another ledger, or clone transport bytes to work around admission. Completion conclusions refer only to the actual captured query and endpoint, not a previous Entity14 query or all796 source candidates.

The provider hash above is source-bound to the latest header probe. If reproduction has a different current provider, preserve that observation as NotCaptured for root review; do not silently relax selection. Transport is capped by existing `WorldWire.MaxBytes` (32MiB); physical retention remains 64MiB. Existing prefix/GameFAIL result remains independent.

## Source preflight

Copy the whole source into `unity/Assets/Spacewars/Tests/EditMode/ExactDemandDiagnostic.cs` temporarily. For compile verification in the actor assembly, a second temporary file contains only the actor class, with the required usings and a distinct namespace, under `Tests/PlayMode`. Run only:

```sh
tools/unity.sh shared -batchmode -nographics -noaudio -muted -runTests -testPlatform EditMode -testFilter Spacewars.Tests.EditMode.NavigationPhysicalBodyTests.ExactDemandHelperExternalRecipeClosurePreflight -testResults /ABS/owned/results.xml -logFile /ABS/owned/editor.log
```

Two cases exercise the real external recipe path (`CreateOwned` with separately owned graph metadata), complete provider/region closure, paid capture/TransferPublic, fresh decode, owned scheduler COW, original lane quotas, terminal Ready, foreign-ledger refusal and zero leaked physical/held/scope owners. The second case leaves Pending at one diagnostic epoch and requires NotRun/NotEstablished. These fixtures do not establish reachability of the actor query.

For the v2 selector change, reuse those unchanged codec/cleanup2PASS. Run only `Spacewars.Tests.EditMode.NavigationPhysicalBodyTests.ExactDemandSelectorUsesCurrentOrderedMemberAndFullTuple` with the same muted wrapper and isolated output paths. Its two cases select different first entities from the immutable ordered input, reject another cursor/cohort/command, and reject stale request/root order/incarnation/mobility. Also compile the actor-only class in `Spacewars.Input.PlayModeTests` as above. This is not a new gameplay prefix/capture run.

## Exact BODY completion, only after actor capture

In the existing Nav worktree, temporarily install the whole source as above. Verify actor package CRC, snapshot SHA, source6cb/helper/test binding and selected metadata first. Set three environment variables to absolute OWNED paths and the verified lowercase SHA:

```sh
SPACEWARS_EXACT_DEMAND_BODY=/ABS/exact-recipe.body SPACEWARS_EXACT_DEMAND_OUT=/ABS/exact-result.json SPACEWARS_EXACT_DEMAND_SHA256=VERIFIED_SHA tools/unity.sh shared -batchmode -nographics -noaudio -muted -runTests -testPlatform EditMode -testFilter Spacewars.Tests.EditMode.NavigationPhysicalBodyTests.ExactDemandBodyToTerminal -testResults /ABS/owned/results.xml -logFile /ABS/owned/editor.log
```

This starts a FRESH query from the exact decoded recipe closure, not a continuation of the captured frontier. The existing owned scheduler executes the same Demand V3 implementation, with `SessionOwnsProviders=true`, unchanged compile/label limits, 64MiB physical/logical caps, and Graph/Field/Connector/Bookkeeping/Maintenance = 256 each epoch. It preserves source recipe/subscription/provider/region identity and reports its exact values, route cost/typed legs, work categories, peak retention, decode/setup/solve time, and cleanup.

Diagnostic bounds: at most 4096 epochs or 180 seconds checked between epochs; these are stop conditions, not a completion guarantee and not a performance benchmark. Pending at either bound or admission/retention refusal is NotRun/NotEstablished. Ready means Reachable for this exact query. UnreachableInGraph means only this C3 query in the current discrete model. Other terminal statuses (including CapacityExceeded) report NotEstablished. A passing completion harness means it observed a terminal status and released resources; read `status` and `reachability`, never equate NUnit PASS with GamePASS. No conclusion about all796 ordered source candidates, the whole group or 600-tick latency follows from this isolated diagnostic.

Save source/patch, DLL bindings, BODY, JSON/XML and runner/editor logs in one scoped evidence package. Remove only the temporary installed QA sources/metas after evidence preservation; retain the warm worktree/Library for the authorized continuation.
