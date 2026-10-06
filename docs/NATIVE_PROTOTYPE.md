# Spacewars Unity prototype

Current consolidation scope: `consolidate-unity-playable-prototype`, canonical planning store `spacewars-planning`. Strategy approved 2026-10-04. This inventory describes available implementation; verification receipts and acceptance remain separate.

## Local lobby slice

`native-local-lobby` adds the ordinary main-menu → local-lobby → real Three Crossings match route in the original graphite/cyan style. The supported roster is one mouse/keyboard human and the current Fighter AI on the map's two starts. Names, opposing team labels and unique owner colors bind the match; missing assigned devices prevent launch and pause an active match. Return and restart preserve setup. Authored split-screen/gamepad, spectators and other AI levels are explicitly unavailable; Q1 in canonical planning controls any expansion. Human visual/device acceptance and publication remain separate.

## Implemented base

- Ordinary keyboard/mouse match on Three Crossings: two armies, authored banks/bridges, raised platforms and continuous ramps, route clearance, terrain-aware unit pose and shots.
- Shared background simulation, immutable owner views, command receipts, fog/exploration/building memory, minimap and expanded map.
- Headquarters, outposts and mines; capture, construction, cancellation, factory/refinery/science, owner-private production, rally, repeat orders, repair and sale.
- Tank, Explorer and Shkval; direct/projectile damage, target selection, hold/stop, attack and attack-move; authoritative victory/defeat and restart.
- Refinery upgrade and sequential owner research (chassis, assault guns, guidance); paid active work and waiting orders; domain world capture/restore regressions.
- Native AI policy for ordinary match: economic development, production, opening/composition, mission defense, scouting, research and artillery support. Old separate SOURCE crisis/technology qualification is not the ordinary runtime.
- Opt-in two-local-human flat arena (`-twoLocalHumans`): keyboard/mouse plus gamepad, readiness/disconnect pause, split views, personal/shared map, selection/groups/menus and persistent allied Follow. Device acceptance remains pending; this mode has no autonomous opponent and is not the authored multilayer mode.
- Native Balance Lab repository primitives: validation, effective revision, immutable records and atomic disk storage. A complete end-user editor is not claimed.

## Prototype limits / historical original capabilities not delivered

- No online rooms/server/reconnect/chat/spectator delivery, Electron client/updater or web runtime in the maintained Unity base.
- No complete original map catalogue/generator/editor, authored multilayer local split-screen, 3–4-local-player shell, complete settings/menu flow or replay/save UI.
- No audiovisual polish, sound acceptance, physical controller approval, signed installers or measured target-device/performance acceptance.
- Primitive native restore integrity is retained; compatibility with original saves is not a requirement.

The old migration plan is in `historical/unity-migration/`. It is an inventory and historical evidence, not a readiness checklist. Original-version equivalence, SOURCE holdout and browser full suites do not qualify this prototype.
