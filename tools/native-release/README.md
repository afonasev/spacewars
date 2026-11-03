# GitHub desktop delivery

Both platforms, signed metadata, packages, installers and current public Unity source are published with one command from a clean committed task checkout:

```sh
SPACEWARS_REMOTE_NSIS=1 python3 tools/native-release/publish.py --version 0.6.6 --sequence 7 --channel test --publish
```

Use a new unpublished version and strictly increasing sequence on every subsequent release. Test means GitHub prerelease, never latest. Production requires the separately authorized `--channel production --production-approved`. Never promote the same already published tag or overwrite assets; rebuild a new version with production-pinned helpers. Without `--publish`, the command builds/packages/verifies locally only. `--reuse-builds` still verifies exact source/version/byte inventories. `--resume` also retains completed packaging, source export and signing for the identical recorded release, verifying checkpoint file hashes before reuse.

Dependencies: Unity6000.3.23f1 with Windows and Mac standalone modules, host unity-run guard, Go, GitHub CLI authentication with repository write permission, Git authentication able to push the exported source (including `.github/workflows`), macOS codesign/hdiutil/lipo, NSIS. This host uses the existing remote NSIS compiler because local NSIS aborts: SPACEWARS_REMOTE_NSIS=1 creates/removes an isolated compiler directory under disk-backed /var/tmp and pins NSIS TMPDIR there, so payloads and compressor intermediates do not exhaust the small /tmp tmpfs; releases/updates never use that server. The public source export fetches the current public tree and snapshot with a blobless/no-checkout clone over HTTPS, stages exact local blobs and pushes through the configured Git protocol. It preserves public history and never exports private Git history or fetches historical asset blobs.

The publisher uses the existing `gh config get git_protocol --host github.com` setting for both tag lookup and source clone/push. On this host it is SSH; no global Git rewrite or token-scope change is needed. Use `--git-protocol ssh` or `--git-protocol https` to override for one invocation. HTTPS OAuth pushes that change workflow files require the workflow scope; an already authorized SSH identity avoids that OAuth limitation. API operations still use GitHub CLI authentication.

Output defaults to `.local/github-release/<channel>/<version>`. A fresh run requires an empty output, including with `--reuse-builds`. After failure retain that output and repeat the same command with `--resume`; source SHA, version, sequence, channel, notes and legacy mode must match. A different identity requires a fresh empty directory. The checkpoint rehashes completed outputs and records phase times/failures. Only the recorded owned draft can resume; already uploaded assets are reused, own zero-byte starter assets can be retried, and published releases are never mutated. A lost create response is recovered using the recorded draft nonce. No global authentication settings are changed.

The private Ed25519 key lives only in `~/.local/share/spacewars-release/native-ed25519.key` (0600). Back it up securely; only the pinned public key ships. No key rotation by update-server data. No OS publisher signature is claimed: Windows SmartScreen/macOS Gatekeeper can warn on these test installers.

Draft publication uploads the complete pair, platform signed manifests, raw differential packages, signed release-wide inventory, downloads catalog and source snapshot. The legacy bridge is opt-in. It verifies package signatures and byte inventories locally before upload, checks the complete GitHub uploaded-asset metadata (names, uploaded state, sizes and SHA256 digests when returned), then publishes the selected channel. It never downloads client payloads after upload. Unowned existing tags/releases are refused; a matching recorded owned draft can resume. Draft discovery uses the authenticated releases list and its release ID: the published `releases/tags/<tag>` endpoint can return 404 while the draft exists. A 404 is not permission to bypass ownership/completeness checks or create a duplicate draft.

A failure after draft creation leaves an unpublished draft for inspection. Keep receipts, hashes and source identity. Before any retry, verify that the draft and tag belong to this attempt and have never been published. Remove only that owned unpublished draft and its owned unpublished tag if a clean retry is required, or choose a new version. The publisher does not delete them automatically. Never delete/retarget a published tag, replace its assets, reuse its version or promote a test tag into production.

Clients use GitHub's public releases API and exact versioned assets only. HTTPS redirects are restricted to GitHub release CDN. Test clients use an isolated Spacewars Test installation/user store. Production cannot discover prerelease candidates; signed channel/tag/platform and monotonic sequence/version are independently checked. A single background startup discovery retrieves metadata only; there is no manual or repeated discovery in the session. Update explicitly requests missing1MiB chunks via exact206 ranges, reuses verified local chunks and verifies full package SHA256 before preparation. File and package integrity are checked again by the privileged apply helper. Partial downloads remain resumable; the active installed pointer changes only after complete validation.

The host owns downloading and explicitly acknowledges quiescence before match entry. Downloads pause before a match and resume the same selected package on return to the menu without rediscovery. Progress, cancellation and prepared status are visible only in the main menu. Release history is requested only from the menu, independently of discovery, and includes the installed version. Restart exits the Player before applying and reopens it. Ordinary clean exit applies prepared updates without relaunching; if the process stops first, a durable pending receipt retries before the next Player launch. Denied UAC or failed apply preserves the old pointer and files. Windows installation/application uses Program Files/UAC; ordinary play is asInvoker. Shortcut and launch are checked on the same last screen. NSIS uninstall retains original INSTDIR and touches only application paths. Unity profiles/settings are persistent user data, never inside replaced release/cache files or removed by uninstall.

## One-time website and legacy migration

`python3 tools/native-release/deploy_landing.py` updates the landing once to GitHub API discovery. Future releases need no VPS changes. Stable downloads and separately labelled test downloads keep versioned names. Binary download is ordinary navigation to GitHub, not cross-origin fetch. CDN/range/installed-client acceptance is an explicitly requested scenario, not a routine publication step.

`Spacewars-VERSION-Legacy-Bridge.zip` contains an unadvertised schema1/production-host transition feed. The installed v0.6.4 root still accepts that protocol/public key; old hosts need actual legacy VPS chunk bytes, not a redirect. The test publication never changes `/native/v1` or `/desktop`. Before production cutover, test the prepared bridge on old installations, publish a new production version and use the explicit legacy publisher with a verified production catalog; retain old objects until migration. `publish_legacy.py` refuses a test catalog.

Affected publication checks: `PYTHONPATH=tools python3 -m unittest tools.test_github_publication tools.test_release_pipeline tools.test_game_release_notes tools.test_test_match_logging_build tools.test_client_resources tools.test_select_tooling_tests`. Select named updater tests from the changed contracts; see docs/game-template/QA.md. A full application run requires separate explicit approval. Windows runner exercises installer defaults, actual Common Desktop link/icon, Program Files, reinstall/legacy metadata and real uninstall. Runner/compilation/signature verification do not establish physical Windows UAC, normal unelevated launch, Intel Mac, OS-signing or human acceptance.

## Publication checklist and evidence

1. Start from a clean committed task branch. Choose a never-published version and a sequence greater than the latest signed sequence in the selected channel; the example above follows test 0.6.5/6. Inspect current releases before using it. Production requires its own new version, production-pinned helpers and explicit approval.
2. Run the single command above. Both Players must bind to the exact source/version; packaging signs platform metadata and the complete release inventory with the external pinned key.
3. Verify exact local source/byte inventories and both signed packages before upload. All assets go to an owned draft; exact asset names, uploaded state, sizes, optional GitHub SHA256 digests and channel must pass before publication. Preserve `upload.json`, `release-state.json` and `publication.json` with signed manifests and source identity.
4. User policy 2026-11-02: do not download installers/update packages after upload, from either draft or public GitHub URLs. Do not run routine CDN/range or installed-client download checks. They are NotRun by policy; upload metadata is not a claim of end-to-end delivery. Actual install/update/device acceptance is a separate explicitly requested scenario. This does not change the game client's download/hash/signature checks during a real update.
5. Retain exact installers/evidence needed for device acceptance, then clean only owned intermediate builds and caches. Record remaining physical Windows/macOS checks separately. Compilation, CI installation and metadata signatures do not establish OS publisher signing or physical acceptance.

Routine release publication never uploads installers or update bytes to the VPS. The landing discovers releases on GitHub without per-version deployment. Legacy VPS objects remain until an explicitly authorized, verified migration; do not delete them merely because a new GitHub prerelease exists.

### Footer release date

`build.py --release-date YYYY-MM-DD` embeds the designated release date in the Player footer and records `release_date` in `native-build.json`. The default is the UTC release preparation day; pass an explicit date when publication is planned for a different day. Version comes from the Player's bundle version. Editor/dev Players without release metadata show only the version. Release generation restores the temporary `Resources/NativeReleaseInfo.json` and its meta file after success or failure. This metadata change does not authorize building or publishing.

## Quiet installed launch and pinned UX

Windows (PowerShell): `& "$env:ProgramFiles\Spacewars Test\Spacewars.exe" -noaudio -muted`. For production replace `Spacewars Test` with `Spacewars`.

macOS: `"/Applications/Spacewars Test.app/Contents/MacOS/Spacewars" -noaudio -muted`. For production use `Spacewars.app`. The launcher forwards the exact application arguments through the host and every update restart; the engine and application receive quiet flags before startup. Device preferences and system volume are unchanged by quiet startup.

The Windows installer stages its payload in its own temporary directory and invokes `--install-payload` for signed inventory validation, host/apply locking, free-space checks, transactional repair/activation and downgrade rejection before changing the active version. Windows 10 x64 or newer is required. macOS packaging declares macOS 11+ and Universal architecture; Intel/Apple Silicon hardware acceptance is separate. No installer is built by focused source checks.

Release descriptions are generated on publication from the previous channel release's source identity and the exact Git range, in matching Russian and English sections. `release-notes-source.json` records the source provenance for review. Missing source history stops generation rather than inventing changes. Historical release descriptions lacking a Russian section are shown as unavailable; this source task does not edit already published releases.

### Release from committed playable assets

Set `SPACEWARS_RELEASE_COMMITTED_ASSETS=1` when publishing the saved Playable scene and native Resources already committed in the selected clean revision. Both release builds retain the normal version/date/icon configuration, source/byte binding and signed production packaging; only asset/scene regeneration is skipped. The default build still regenerates assets. This is appropriate when shipping the existing playable content, and does not repair the separate material-catalog regeneration failure. The build log records `RELEASE_ASSETS committed`.

### Illustrated release notes

Pass `--notes tools/native-release/notes/<name>.json` to `publish.py` when preparing an illustrated release. The manifest and screenshot files must be committed in the exact source revision being released. Preparation reads Git blobs, so unrelated local files cannot silently enter a release. Without `--notes`, the existing conservative Git-range summary remains available. Author concrete player-facing changes, one bullet per item, with matching RU/EN categories and meaning:

```json
{
  "schema": 1,
  "entries": [{
    "category_ru": "Интерфейс", "category_en": "Interface",
    "ru": "История версий показывает иллюстрации рядом с изменениями.",
    "en": "Release history shows illustrations beside their changes.",
    "images": [{
      "path": "tools/native-release/notes/media/history.png",
      "source_commit": "<exact 40-character game revision captured>",
      "ru": "История версий с иллюстрациями", "en": "Illustrated release history"
    }]
  }]
}
```

Use real screenshots of the identified revision. Images are optional for text-only fixes; add two ordered images captioned before/after when useful. PNG inputs must be noninterlaced 8-bit grayscale/RGB/RGBA, at most 4096 pixels per side and 8 MiB. Paths must be relative to the repository; the source revision must be an ancestor of the release. Missing, corrupt, oversized or uncommitted assets reject preparation. Caption/text fields are plain single lines.

Preparation copies images to content-addressed `notes-<sha256>.png` assets **before signing the release inventory**, and writes matching Markdown in the release body. Local validation binds every image's size/hash into the signed inventory; upload metadata is checked before publication. Keep published attachments with their release. The updater accepts only images explicitly attached to the same release, serves them via the authenticated menu-only `/history-image` endpoint and stores verified images/history under the installed root's `history/<channel>/` directory. No automatic cache deletion is performed. A successfully viewed release can be reopened offline; images are requested only for the selected release. Failed media leaves the caption and text usable. In-game preview activation enlarges the image; Back restores the same history focus and scroll.

## Local test clients and lossless compression

For a local Development/test Player with the existing native QA hooks and required match logging:

```sh
SPACEWARS_RELEASE_COMMITTED_ASSETS=1 python3 tools/native-release/build.py --platform macos-universal --version VERSION --channel test --release-date YYYY-MM-DD --development --compression none
```

Use the corresponding Windows target `windows-x64`. `--development` rejects the production channel. A bound build records the development flag and data compression. The default remains `none`: uncompressed Unity data compresses better in the installer. `--compression lz4hc` uses lossless chunk compression to reduce installed Player size; it can produce a larger outer LZMA installer. Neither route changes texture/mesh quality. Select the profile by the measured installed-size/download tradeoff; do not infer gameplay or device acceptance from byte counts.

Mac packaging defaults to LZMA DMG (`ULMO`, supported on macOS 10.15+, below the project's macOS 11 minimum); `--dmg-format UDZO` retains the former zlib format. Windows uses solid LZMA with a 64 MiB dictionary; `--lzma-dict-mb 8|32|64` is a bounded override passed through the local or configured remote compiler. Dictionary size is approximately the required decompression memory, so 64 MiB adds about 56 MiB over the old 8 MiB setting. Local packaging never publishes a release. Keep source/byte identities, verify lossless extraction and retain the exact test installer before removing staging.

## Resume, workspace and disk budget

```sh
SPACEWARS_REMOTE_NSIS=1 SPACEWARS_RELEASE_COMMITTED_ASSETS=1 python3 tools/native-release/publish.py --version VERSION --sequence SEQUENCE --channel production --production-approved --notes tools/native-release/notes/VERSION.json --publish
# Same source and exact arguments after an interruption:
SPACEWARS_REMOTE_NSIS=1 SPACEWARS_RELEASE_COMMITTED_ASSETS=1 python3 tools/native-release/publish.py --version VERSION --sequence SEQUENCE --channel production --production-approved --notes tools/native-release/notes/VERSION.json --publish --resume
```

Use `--prepare-legacy-bridge` only for a planned legacy migration. Routine releases omit the archive and its additional helper/payload copies; existing deployed legacy data is unchanged.

The publisher checks estimated per-stage disk needs plus `--min-free-gib` headroom (default 2 GiB) before build/package/source export. This is not a space reservation: prefer an owned release workspace on storage with adequate headroom and avoid competing large captures on that storage during release. The publisher removes its redundant packaging staging after each successful platform checkpoint. It does not remove source, bound Players needed for retries, another task's cache, or browser data.

For an already scheduled next release/retry, retain the same owned Unity workspace/Library under the existing named-continuation cleanup policy; import caches stay private to that Unity project. Never symlink/share Library across worktrees. A new source SHA requires a new release identity/output; an old checkpoint is not reusable across commits. A persistent build runner is an infrastructure choice, not something the publisher silently provisions.

`build.py` records the build-generated diff and restores exact pre-build bytes only after checking all changes. Prepared map caches and the build-time lab catalog are expected outputs; pipeline shader-prefilter/runtime-list changes are restricted to generated fields. Unexpected code, added/deleted tracked assets or quality changes fail and remain for inspection. Evidence is `.local/generated-PLATFORM/`.

## Minimal persistent launcher

Opening the installed application shows a compact native launcher before Unity starts. It has only the product name, installed version and Play button. Available updates add one contextual action; downloads add progress and cancellation. Offline discovery does not block Play. A normal game exit returns to this window. While Player is alive, launch/update actions are disabled; closing the window never terminates Player. Existing match-entry download quiescence, signed manifests, isolated channels, transactional activation and profile preservation remain in force.

The macOS view is a sealed Universal AppKit helper bundle; Windows uses the OS-supplied PowerShell/WinForms view, with no additional runtime download. Both view sources/binaries are added inside the signed payload inventory before packing. The view receives presentation JSON through private pipes and can emit only fixed action names; it receives no updater credentials or paths. First installation remains the existing Windows wizard/macOS DMG workflow.

Focused launcher checks: run `go test -race -run 'TestLauncher|TestHostExplicit|TestApplicationArguments|TestReadiness' .` in `tools/native-release/updater`, and `PYTHONPATH=tools python3 -m unittest tools.test_desktop_launcher tools.test_release_pipeline` from the repository root. Source compilation and simulated lifecycle fixtures do not establish physical Windows installer/UAC or actual installed-game acceptance.
