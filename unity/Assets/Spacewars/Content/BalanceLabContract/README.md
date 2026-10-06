# Published Balance Lab contract (U8)

This is a disconnected content pack for the complete offline Unity migration. No C# reader, Player Lab, persistence/editor/apply route or runtime consumption is supplied here. Pack presence does not make its parameters effective in Unity or establish full migration/Player acceptance.

Regenerate from the repository root:

```sh
node scripts/export-native-balance-contract.mjs
node scripts/export-native-balance-contract.mjs --check
```

`catalog.json` transport schema 1 preserves exact UTF-8 `balance/releases.json` bytes and SHA256, its full parsed raw catalog (including unknown fields, original source labels, dates, immutable gameplay/AI refs and releaseRef), and all authoritative gameplay numeric/select/text and AI descriptors in source order. `catalogs` retains projectile types and AI roster/opening/difficulty descriptors/options. `revisions` carries canonical raw content fingerprints and independent effective snapshots produced by existing gameplay/AI normalizers and validators. Normalization never overwrites raw snapshots. Older omitted values remain omitted in raw data; defaults are added only to effective views.

Canonical fingerprints sort object keys by code point, retain array order, use JSON numbers and UTF-8 without a trailing newline. Generated files add one LF; `packSha256` hashes those exact catalog file bytes. `source.bytesUtf8` keeps the original manifest whitespace/newline as well. `provenance.json` fingerprints the actual transitive SSR metadata/normalization/validation sources, runner and dependency lock. Source bytes, not the current Git HEAD, determine reproducibility.

Existing Resource provenance is recorded with its exact source commit/profile/ref and file hash. A manifest byte-hash mismatch is printed and retained with both hashes, never repaired. It does not prove snapshot inequality or runtime parity. Neither command writes `Resources/PlayableProfile.json`, balance source files, frozen AI sources or unpublished `.local` history. Invalid JSON/duplicate keys, corrupt or duplicate refs, unsupported schemas and invalid metadata/values fail visibly. `--check` checks exact bytes and wholecatalog readback without repairing stale files.

Future reader/repository/editor/apply work must explicitly map runtime support, choose native persistence and tick transaction semantics, coordinate restored worlds and AI policy bindings, and provide its own native/Player gates. Those decisions remain outside U8.
