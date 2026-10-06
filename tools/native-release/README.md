# Native distribution

`python3 tools/native-release/build.py --platform macos-universal|windows-x64 --version VERSION` builds the Player through tools/unity.sh and records its source/version/byte identity. Production builds require clean committed source; --allow-dirty is explicit nonpublishable smoke mode.

Package each platform with `python3 tools/native-release/package.py --platform windows-x64|macos-universal --version VERSION --sequence SEQUENCE`. The package generates a private Ed25519 key once at `~/.local/share/spacewars-release/native-ed25519.key`; only the public key is recorded in `trust.json`. Keep that key outside Git and retain it for future releases. Existing trust cannot be silently replaced.

On this macOS host NSIS 3.13 aborts with bad_alloc even for a minimal script. `SPACEWARS_REMOTE_NSIS=1` uses the existing VPS NSIS compiler in a private temporary directory, copies back the installer, then removes that directory; it does not publish anything.

Stable root bootstrap delegates to an updateable signed release host. The host exposes only a token-protected loopback API to the game. Metadata check can run at startup; only main-menu Update initiates content requests. 1 MiB SHA-256 chunks are reused after verification. Download cache is per-user, installation payload stays inside Program Files or macOS Application Support (the installed launcher bundle remains immutable). Signed staged files are independently checked when copied into protected install storage. Windows requests UAC only for apply/recovery and restarts via the original unelevated launcher. macOS updates require writable user Application Support storage; the installed launcher bundle stays immutable. Permissions failures preserve the old release.

Release activation updates only active.json after complete candidate validation. Old release remains for rollback. Startup must acknowledge ready; crash before readiness returns to the prior signed release. A normal voluntary exit does not trigger rollback. Stale pointer/recovery and platform/Windows UAC behavior require actual platform QA.

Tests: `cd tools/native-release/updater && go test -race ./...`; Windows cross-compile with `GOOS=windows GOARCH=amd64 go build`. This does not prove Windows runtime acceptance.

The canonical planet icon is `unity/Assets/Spacewars/Content/Brand/Icon.png` (recovered from the game's original production icon). ICO/ICNS variants preserve that artwork. Windows bootstrap and UpdateHost embed the ICO with pinned rsrc v0.10.2. Both final-screen checkboxes default checked; the desktop link is machine-wide (Public Desktop). Silent installs create the desktop link but do not launch the game. Reinstallation validates signed payload/monotonic sequence before switching active.json, preserving a previous release.

Full installers live in versioned GitHub Releases; `publish.py` transfers only OTA chunks/manifests/catalog and landing to VPS, verifies GitHub bytes, and removes dedicated full native installers after successful public catalog readback. Keep the private signing key: a public clone alone cannot sign trusted production updates.
