# Spacewars

Native Unity RTS prototype. [Download Windows / macOS](https://github.com/afonasev/spacewars/releases) · [Website](https://spacewars.afonasev.tech/)

Open `unity/` with Unity **6000.3.23f1**. Let Unity resolve its pinned packages, then choose **Spacewars → Build Playable macOS** to create a Development Player. The project contains native gameplay, local lobby, AI, multilevel maps and tests. See [current capabilities](docs/NATIVE_PROTOTYPE.md).

Left click/drag selects; right click moves/attacks or sets rally; A then click attack-moves, S stops, arrows pan, wheel zooms, Tab opens the map, Escape pauses. Production and research are available from HQ/factory/science panels.

Installed releases start in the main menu. **Update** downloads only after your explicit click, reuses verified chunks, and preserves the previous signed release. Full installers are GitHub Release assets; signed update metadata/chunks remain on the website. Test builds are unsigned by an OS publisher (SmartScreen/Gatekeeper may appear).

`tools/native-release/` contains installer/update source. Python 3.11+, Go 1.23+, NSIS and platform packaging tools are required. The maintainer build wrapper `tools/unity.sh` uses an external host-concurrency guard (`UNITY_RUNNER`); ordinary Editor builds do not need this wrapper. `build.py` creates source/byte identity before `package.py`; production signing requires the maintainer's private Ed25519 key, which is intentionally not in this repository. The committed trust contains only the public key.

This repository starts with an audited current-source snapshot. It does not import the retired browser implementation or private development history. `source-snapshot.json` records the original build-source revision and hashes of exported files; the GitHub snapshot commit is a separate identity. See [asset/dependency notices](ASSET_NOTICES.md).
