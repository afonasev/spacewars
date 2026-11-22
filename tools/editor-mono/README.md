# macOS ARM64 Editor Mono allocator

Unity 6000.3.23f1 embeds Mono 6.13.0 at `9d7c9afd266e523029fb0cab48e944e528e61e68`, with BDWGC `1113fefc4ba0e895767ac780d84e9e456fd624db`. Unity disables thread-local allocation in this build. The default global spin-lock can repeatedly back off in nanosleep under competing managed allocations. The patch selects BDWGC's existing pthread mutex implementation on Darwin. Object tracing, automatic incremental GC, its 3 ms slice, gameplay and Player code remain unchanged.

`runtime.json` pins the installed Editor/Mono bytes, sources and candidate DLL. The native ABI comparison retains every Mono export; only private allocator symbols change (`GC_allocate_lock`/`GC_pause` → `GC_allocate_ml`). A rebuilt spin control using the same compiler/SDK reproduced the long allocation wait. Raw comparisons and original game controls are in the configured evidence store under `stabilize-early-scout-navigation/f3-mono-repair`.

## Local setup and launch

The verified native DLL is preserved in that evidence package. Prepare its process-local cache:

```sh
python3 tools/editor_mono_runtime.py prepare
python3 tools/editor_mono_runtime.py verify
```

`prepare --library /absolute/path/libmonobdwgc-2.0.dylib` accepts only the exact pinned bytes. Existing mismatched caches are preserved and rejected. The cache is under `~/.local/share/spacewars/editor-mono/<id>`; `SPACEWARS_EDITOR_MONO_CACHE_ROOT` changes its parent directory.

On macOS ARM64, `tools/unity.sh shared|exclusive` uses the cache for the default Editor. The launcher checks both installed binaries and the cached DLL, then supplies `DYLD_LIBRARY_PATH` only to the child Editor process. It never patches Unity.app, changes system loader settings, downloads dependencies or builds anything. Missing cache fails before Editor startup. An explicit `UNITY_EDITOR` remains authoritative. `SPACEWARS_EDITOR_MONO_STOCK=1` selects the original runtime for labelled diagnosis. The `player` route is unchanged.

## Source reproduction

Prerequisites: macOS ARM64, Command Line Tools, Python 3.12+, autoconf, automake and GNU libtool (`glibtoolize`). Run:

```sh
tools/editor-mono/rebuild.sh .local/NEW-owned-mono-build
```

The recipe downloads the four immutable source archives and checks all SHA-256 values. It applies the allocator patch and one SDK field-name correction (`objc_super.super_class`, same field/layout). Current Clang treats historical `Buffer*`/`void*` callback assignments as errors; the recipe keeps those diagnostics as warnings, matching the source's existing ABI, without changing callbacks or tracing logic. It builds only the native embedding library under shared host admission. It does not build managed class libraries, Unity Players, installers or run game/full tests. All output and failure logs remain in the supplied new directory.

A fresh rebuild can differ in debug/build metadata. Treat it as a new binary: retain its hash, compare exports, run the allocator reproducer and the unchanged affected/original 40s gates before updating the pinned DLL identity. Do not silently substitute a new engine or collector.

## Minimal allocator reproducer

The no-game Play Mode source is kept outside Assets so the stress experiment is opt-in. In an owned code worktree:

```sh
tools/editor-mono/reproduce.sh .local/NEW-mono-reproduction
```

This temporarily installs only that fixture, measures the same two-thread allocation workload for 30s, records the actual Mono build ID and every authority allocation burst, then removes its own copied source/meta. It never runs ordinary matches or the full suite. `UNITY_EDITOR` and `SPACEWARS_EXPECT_MONO_TAG` can bind a separately recorded rebuilt-spin control. `FrameIdleBudgetAllocationContention` in the source is a separate diagnostic, not a required game contract.
