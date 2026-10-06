# GitHub desktop delivery

Both platforms, signed metadata, packages, installers and current public Unity source are published with one command from a clean committed task checkout:

```sh
SPACEWARS_REMOTE_NSIS=1 python3 tools/native-release/publish.py --version 0.6.5 --sequence 6 --channel test --publish
```

Use a new unpublished version and strictly increasing sequence on every subsequent release. Test means GitHub prerelease, never latest. Production requires the separately authorized `--channel production --production-approved`. Never promote the same already published tag or overwrite assets; rebuild a new version with production-pinned helpers. Without `--publish`, the command builds/packages/verifies locally only. `--reuse-builds` still verifies exact source/version/byte inventories.

Dependencies: Unity6000.3.23f1 with Windows and Mac standalone modules, host unity-run guard, Go, GitHub CLI authentication with repository write/workflow permission, macOS codesign/hdiutil/lipo, NSIS. This host uses the existing remote NSIS compiler because local NSIS aborts: SPACEWARS_REMOTE_NSIS=1 creates/removes an isolated compiler directory; releases/updates never use that server. The public source clone preserves only public history and exports current Unity/tooling/landing, never private Git history.

The private Ed25519 key lives only in `~/.local/share/spacewars-release/native-ed25519.key` (0600). Back it up securely; only the pinned public key ships. No key rotation by update-server data. No OS publisher signature is claimed: Windows SmartScreen/macOS Gatekeeper can warn on these test installers.

Draft publication uploads the complete pair, platform signed manifests, raw differential packages, signed release-wide inventory, downloads catalog, source snapshot and prepared legacy bridge. It checks complete draft assets, downloads every draft asset again, verifies exact sizes/SHA256 and platform signatures/package contents, then publishes the selected channel. Existing tags/releases are refused. A failure leaves an unpublished draft for inspection; remove only that owned failed draft before retrying the same version, never overwrite a published version.

Clients use GitHub's public releases API and exact versioned assets only. HTTPS redirects are restricted to GitHub release CDN. Test clients use an isolated Spacewars Test installation/user store. Production cannot discover prerelease candidates; signed channel/tag/platform and monotonic sequence/version are independently checked. Startup/Check retrieve metadata only. Update explicitly requests missing1MiB chunks via exact206 ranges, reuses verified local chunks and verifies full package SHA256 before preparation. File and package integrity are checked again by the privileged apply helper. Partial downloads remain resumable; the active installed pointer changes only after complete validation.

The host owns downloading independently of menu/gameplay. Progress remains visible during play. Restart exits the Player before applying and reopens it. Ordinary clean exit applies prepared updates without relaunching; if the process stops first, a durable pending receipt retries before the next Player launch. Denied UAC or failed apply preserves the old pointer and files. Windows installation/application uses Program Files/UAC; ordinary play is asInvoker. Shortcut and launch are checked on the same last screen. NSIS uninstall retains original INSTDIR and touches only application paths. Unity profiles/settings are persistent user data, never inside replaced release/cache files or removed by uninstall.

## One-time website and legacy migration

`python3 tools/native-release/deploy_landing.py` updates the landing once to GitHub API discovery. Future releases need no VPS changes. Stable downloads and separately labelled test downloads keep versioned names. Binary download is ordinary navigation to GitHub, not cross-origin fetch. Check public API CORS and CDN redirect/ranges on each initial delivery.

`Spacewars-VERSION-Legacy-Bridge.zip` contains an unadvertised schema1/production-host transition feed. The installed v0.6.4 root still accepts that protocol/public key; old hosts need actual legacy VPS chunk bytes, not a redirect. The test publication never changes `/native/v1` or `/desktop`. Before production cutover, test the prepared bridge on old installations, publish a new production version and use the explicit legacy publisher with a verified production catalog; retain old objects until migration. `publish_legacy.py` refuses a test catalog.

Tests: `python3 -m unittest discover -s tools -p 'test_*.py'`; `cd tools/native-release/updater && go test -race ./...`. Windows runner exercises installer defaults, actual Common Desktop link/icon, Program Files, reinstall/legacy metadata and real uninstall. Runner/compilation/signature verification do not establish physical Windows UAC, normal unelevated launch, Intel Mac, OS-signing or human acceptance.
