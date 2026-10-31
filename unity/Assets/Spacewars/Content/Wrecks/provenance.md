# Current vehicle wreck provenance

Reproducible maintained generator: `design/vehicle-wrecks/build_wrecks.py`, using active `Content/StyleA` GLBs and exact hashes in `source-manifest.json`. There are 13 base and 13 upgrade variants. `models` retains the nine ground entries; `hawkModels` records the four base air entries; `upgradedModels` records all 13 upgrade entries. Stale unused LOD1 descriptions are removed; no LOD system is introduced.

All use the approved F3 `Textures/wreck-charred-fine.png` bytes. Runtime revision2 ActorMaterialProfile applies fine dark neutral soot response and local warm-light compensation. No model carries simulation collision or emits light. Authority and presentation scaling remain separate from these GLBs.
