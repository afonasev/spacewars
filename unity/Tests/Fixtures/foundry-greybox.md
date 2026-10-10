# Black Foundry v2 greybox

Open `Assets/Spacewars/Content/Scenes/FoundryGreybox.unity` and enter Editor Play Mode, or use **Spacewars → Maps → Open Black Foundry Greybox**. Existing Playable scene also recognizes `-foundryGreybox` for future explicitly requested builds. Normal Three Crossings lobby remains the default.

The map is deterministic authored v2 content; the sandbox seed is 19092026. Six homes A1–A3 / B1–B3 form teams 1 / 2. A1 is locally controlled and the other five seats use the existing native AI. This is an inspection scene with revealed terrain and a switchable perspective, not a new six-device lobby or shipping spectator interface.

- **Space**: pause / resume (starts paused); **R**: restart.
- **1–6**: inspect that owner's perspective (commands are available only to the locally controlled A1; AI owners retain normal ingress protection).
- **Left click**: select a visible own unit or building; **right click**: move, including existing neutral-point capture.
- **F** with cursor over an ordinary slot: request a factory using normal ownership/cost rules.
- **E / T** with factory selected: queue Explorer / Tank.
- **WASD / wheel**: pan / zoom; **S** also stops selected units.

Profile: `Content/Resources/FoundryProfile.json`, named `black-foundry-greybox-v2@1`; governed scale and height ranges are in `FoundryMap.Fields`. Geometry is intentionally excluded from the current in-match Balance Lab per GAME_SPEC decision 2026-10-07. Layout coordinates are authored content rather than global balance changes. The navigation authority remains projected X/Z; explicit rock edges block unsupported cliffs, and map support gradients place models and ballistic planes continuously on ramps.

14 mine points: M1–M6 nearest homes, M7–M10 first expansions, M11/M12 contested flanks, M13 northwest and M14 southeast of the lower center. F1–F4 are neutral outposts with current build slots. Major corner halls, basalt dividers and lava fissures are obstacles; local ash yard patches occur on both sides.

Review the whole layout, short rear links, upper flanks and four broad center passages. Greybox visuals, editor tests and synthetic commands do not establish human/device/Player acceptance. Fine art, VFX, installers and release/deploy remain outside this slice.
