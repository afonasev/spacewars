# Native Balance Lab data prerequisite (U9)

This pure assembly reads lossless schema2 gameplay+AI revisions and stores device/development history. No runtime, match, projection, Player route or authoring controls consume it. The complete offline Lab still requires typed governed writers/coverage, applicable gameplay/AI/presentation consumers, desktop UI, tick transaction and Player acceptance. `EffectiveRevision.HasRuntimeProjection` is explicitly false.

## Adapter contract

Provide exact catalog/provenance bytes and expected SHA256 from trusted build/bootstrap configuration. Installed Player does not read Git/TS; an explicitly configured development adapter may additionally supply source bytes for provenance checks. The diagnostic Resource mismatch remains visible and does not invalidate the independent verified catalog or certify parity.

Inject `normalizer-rules.json` bytes and their trusted hash. This generated recipe binds the exported metadata fingerprint, web default snapshots and 87 source-derived validation conditions; it is not a separately maintained range catalog. Unknown data survives raw/canonical storage and is not assigned native runtime semantics. Original UTF8 bytes are separate from binary64 semantic fingerprints. The codec validates grammar/duplicates before Json.NET DOM conversion, preserves UTF16 strings (including lone escaped surrogates), disables date parsing, performs exact rational decimal/binary64 rounding, and implements the actual U8 UTF16 key order and JS shortest number formatting.

Select capabilities from trusted bootstrap configuration, never provenance, Player cwd, writable folders or `DEVELOPMENT_BUILD`. Installed history uses injected persistent data root `/BalanceLab/state.json`. Development uses an explicitly injected repository root with `.local/native-balance-lab/state.json` and Git-owned `balance/releases.json`. Publication persists snapshot and exact pointer in one atomic catalog document; a generation extension is compatible with the current web schema1 reader. No deployment/publication to a remote host occurs here.

Pass expected history/publication tokens to mutations. Save appends max-known+1, unifies full gameplay+AI and stamps the injected local calendar date. Renames are display overlays; original records stay immutable. Published membership protects rename/delete despite source flags. Whole-profile deletion requires exact name confirmation and clears dangling last-started memory. Import accepts explicit browser extraction bytes, retains original history/standalone-AI payloads, preserves dates and order, attaches valid legacy/default AI only when missing, dedupes exact immutable pairs and rejects conflicts. Device preferences require exact published membership; development ignores browser release preferences.

Success is returned only after flush, atomic replacement and readback. Locks serialize cooperative processes and repository lifecycle operations. Stale hashes/generations, busy lock, malformed committed state and IO failures are errors; input drafts are never mutated. `StoreCommitException.ReplacementOccurred` distinguishes an observed post-replace failure: reload to reconcile before retrying. Recovery never promotes pending temp files. Arbitrary external editors do not participate in CAS/locking; observed drift is rejected. Process-crash tests do not simulate hardware power loss. Windows IO and build gates must be executed on the relevant platform before integration.

## Reproduce

From repository root, with normal npm dependencies installed:

```sh
# TypeScript7 CLI has no compiler API; use isolated official TypeScript5.9.3 for this oracle.
TYPESCRIPT_ORACLE=/absolute/path/to/typescript-5.9.3 node unity/Assets/Spacewars/Tests/EditMode/BalanceLab/generate-fixtures.mjs --check
python3 unity/Assets/Spacewars/Tests/EditMode/BalanceLab/run-process-fixtures.py --output /absolute/evidence/path
python3 tools/check_qa.py --scope focused --platform EditMode --fixture Spacewars.Tests.EditMode.BalanceLab.ContractCodecTests --fixture Spacewars.Tests.EditMode.BalanceLab.NormalizerTests --fixture Spacewars.Tests.EditMode.BalanceLab.RepositoryTests --fixture Spacewars.Tests.EditMode.BalanceLab.AtomicStoreTests
npm run check:full
```

The oracle uses existing web normalizers/validators and metadata. 2056 binary64 vectors include subnormals/extremes/UTF16 escaping plus four published and four missing-field parity cases. Test assembly has 36 cases across four fixtures. `BuildCompatibilityProbe` builds the existing scene without running the asset preparation pipeline; its report verifies Player compilation and excludes unpublished/test data. macOS and Windows compatibility are distinct from physical device/Player acceptance.
