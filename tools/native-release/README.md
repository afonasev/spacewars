# GitHub desktop delivery

Both platforms, signed metadata, packages, installers and current public Unity source are published with one command from a clean committed task checkout:

```sh
SPACEWARS_REMOTE_NSIS=1 python3 tools/native-release/publish.py --version 0.6.6 --sequence 7 --channel test --publish
```

Use a new unpublished version and strictly increasing sequence on every subsequent release. Test means GitHub prerelease, never latest. Production requires the separately authorized `--channel production --production-approved`. Never promote the same already published tag or overwrite assets; rebuild a new version with production-pinned helpers. Without `--publish`, the command builds/packages/verifies locally only. `--reuse-builds` still verifies exact source/version/byte inventories.

Dependencies: Unity6000.3.23f1 with Windows and Mac standalone modules, host unity-run guard, Go, GitHub CLI authentication with repository write permission, Git authentication able to push the exported source (including `.github/workflows`), macOS codesign/hdiutil/lipo, NSIS. This host uses the existing remote NSIS compiler because local NSIS aborts: SPACEWARS_REMOTE_NSIS=1 creates/removes an isolated compiler directory; releases/updates never use that server. The public source clone preserves only public history and exports current Unity/tooling/landing, never private Git history.

The publisher uses the existing `gh config get git_protocol --host github.com` setting for both tag lookup and source clone/push. On this host it is SSH; no global Git rewrite or token-scope change is needed. Use `--git-protocol ssh` or `--git-protocol https` to override for one invocation. HTTPS OAuth pushes that change workflow files require the workflow scope; an already authorized SSH identity avoids that OAuth limitation. API operations still use GitHub CLI authentication.

Output defaults to `.local/github-release/<channel>/<version>` and must be empty, including with `--reuse-builds` (that option reuses bound Players, not publication output). Failed output is evidence: preserve it and choose a fresh `--output` directory for a retry. The script never silently deletes it or mixes old packages/catalogs into the signed inventory.

The private Ed25519 key lives only in `~/.local/share/spacewars-release/native-ed25519.key` (0600). Back it up securely; only the pinned public key ships. No key rotation by update-server data. No OS publisher signature is claimed: Windows SmartScreen/macOS Gatekeeper can warn on these test installers.

Draft publication uploads the complete pair, platform signed manifests, raw differential packages, signed release-wide inventory, downloads catalog, source snapshot and prepared legacy bridge. It checks complete draft assets, downloads every draft asset again, verifies exact sizes/SHA256 and platform signatures/package contents, then publishes the selected channel. Existing tags/releases are refused. Draft discovery uses the authenticated releases list and its release ID: the published `releases/tags/<tag>` endpoint can return 404 while the draft exists. A 404 is not permission to bypass readback or create a duplicate draft.

A failure after draft creation leaves an unpublished draft for inspection. Keep receipts, hashes and source identity. Before any retry, verify that the draft and tag belong to this attempt and have never been published. Remove only that owned unpublished draft and its owned unpublished tag if a clean retry is required, or choose a new version. The publisher does not delete them automatically. Never delete/retarget a published tag, replace its assets, reuse its version or promote a test tag into production.

Clients use GitHub's public releases API and exact versioned assets only. HTTPS redirects are restricted to GitHub release CDN. Test clients use an isolated Spacewars Test installation/user store. Production cannot discover prerelease candidates; signed channel/tag/platform and monotonic sequence/version are independently checked. A single background startup discovery retrieves metadata only; there is no manual or repeated discovery in the session. Update explicitly requests missing1MiB chunks via exact206 ranges, reuses verified local chunks and verifies full package SHA256 before preparation. File and package integrity are checked again by the privileged apply helper. Partial downloads remain resumable; the active installed pointer changes only after complete validation.

The host owns downloading and explicitly acknowledges quiescence before match entry. Downloads pause before a match and resume the same selected package on return to the menu without rediscovery. Progress, cancellation and prepared status are visible only in the main menu. Release history is requested only from the menu, independently of discovery, and includes the installed version. Restart exits the Player before applying and reopens it. Ordinary clean exit applies prepared updates without relaunching; if the process stops first, a durable pending receipt retries before the next Player launch. Denied UAC or failed apply preserves the old pointer and files. Windows installation/application uses Program Files/UAC; ordinary play is asInvoker. Shortcut and launch are checked on the same last screen. NSIS uninstall retains original INSTDIR and touches only application paths. Unity profiles/settings are persistent user data, never inside replaced release/cache files or removed by uninstall.

## One-time website and legacy migration

`python3 tools/native-release/deploy_landing.py` updates the landing once to GitHub API discovery. Future releases need no VPS changes. Stable downloads and separately labelled test downloads keep versioned names. Binary download is ordinary navigation to GitHub, not cross-origin fetch. Check public API CORS and CDN redirect/ranges on each initial delivery.

`Spacewars-VERSION-Legacy-Bridge.zip` contains an unadvertised schema1/production-host transition feed. The installed v0.6.4 root still accepts that protocol/public key; old hosts need actual legacy VPS chunk bytes, not a redirect. The test publication never changes `/native/v1` or `/desktop`. Before production cutover, test the prepared bridge on old installations, publish a new production version and use the explicit legacy publisher with a verified production catalog; retain old objects until migration. `publish_legacy.py` refuses a test catalog.

Affected publication checks: `python3 -m unittest tools.test_github_publication tools.test_game_release_notes`. Select named updater tests from the changed contracts; see docs/game-template/QA.md. A full application run requires separate explicit approval. Windows runner exercises installer defaults, actual Common Desktop link/icon, Program Files, reinstall/legacy metadata and real uninstall. Runner/compilation/signature verification do not establish physical Windows UAC, normal unelevated launch, Intel Mac, OS-signing or human acceptance.

## Publication checklist and evidence

1. Start from a clean committed task branch. Choose a never-published version and a sequence greater than the latest signed sequence in the selected channel; the example above follows test 0.6.5/6. Inspect current releases before using it. Production requires its own new version, production-pinned helpers and explicit approval.
2. Run the single command above. Both Players must bind to the exact source/version; packaging signs platform metadata and the complete release inventory with the external pinned key.
3. All assets go to a draft. Exact names, channel, downloaded sizes/SHA256 and downloaded platform signatures/packages must pass before publication. Preserve `draft-readback.json` and `publication.json` with the signed inventory and source snapshot.
4. Independently download the published versioned installers and packages from their public GitHub URLs. Check sizes/SHA256 against signed metadata and run the pinned updater's `--verify-package` for both platforms. Verify metadata-only Check separately from explicit Update. A draft readback alone does not prove public CDN downloads.
5. Retain exact installers/evidence needed for device acceptance, then clean only owned intermediate builds and caches. Record remaining physical Windows/macOS checks separately. Compilation, CI installation and metadata signatures do not establish OS publisher signing or physical acceptance.

Routine release publication never uploads installers or update bytes to the VPS. The landing discovers releases on GitHub without per-version deployment. Legacy VPS objects remain until an explicitly authorized, verified migration; do not delete them merely because a new GitHub prerelease exists.

### Footer release date

`build.py --release-date YYYY-MM-DD` embeds the designated release date in the Player footer and records `release_date` in `native-build.json`. The default is the UTC release preparation day; pass an explicit date when publication is planned for a different day. Version comes from the Player's bundle version. Editor/dev Players without release metadata show only the version. Release generation restores the temporary `Resources/NativeReleaseInfo.json` and its meta file after success or failure. This metadata change does not authorize building or publishing.

## Quiet installed launch and pinned UX

Windows (PowerShell): `& "$env:ProgramFiles\Spacewars Test\Spacewars.exe" -noaudio -muted`. For production replace `Spacewars Test` with `Spacewars`.

macOS: `"/Applications/Spacewars Test.app/Contents/MacOS/Spacewars" -noaudio -muted`. For production use `Spacewars.app`. The launcher forwards the exact application arguments through the host and every update restart; the engine and application receive quiet flags before startup. Device preferences and system volume are unchanged by quiet startup.

The Windows installer stages its payload in its own temporary directory and invokes `--install-payload` for signed inventory validation, host/apply locking, free-space checks, transactional repair/activation and downgrade rejection before changing the active version. Windows 10 x64 or newer is required. macOS packaging declares macOS 11+ and Universal architecture; Intel/Apple Silicon hardware acceptance is separate. No installer is built by focused source checks.

Release descriptions are generated on publication from the previous channel release's source identity and the exact Git range, in matching Russian and English sections. `release-notes-source.json` records the source provenance for review. Missing source history stops generation rather than inventing changes. Historical release descriptions lacking a Russian section are shown as unavailable; this source task does not edit already published releases.
