# Natural Frontier — reusable Unity environment kit

Current F2 direction: turquoise river water, continuous black-basalt/orange mineral geology, weathered industrial platforms and steel. Original moss/shale source textures are retained for reuse. See [alien geology](ALIEN-GEOLOGY.md) for modules and geometry constraints. Authored for Spacewars on 2026-10-07. This kit contains presentation only: no colliders, navigation, gameplay values or Three Crossings coordinates.

## Assets

- `Materials/Earth.mat`, `Rock.mat`, `Concrete.mat`, `Steel.mat`, `Water.mat`: ready-to-assign material presets using the shared URP `Spacewars/TerritoryFog` shader.
- `../../Resources/Environment/NaturalFrontier/`: original generated PNGs, committed Unity importer metadata, `Profile.json`, and `provenance.json` with source-generation identities and SHA256 hashes.
- `../../../Presentation/EnvironmentArt.cs`: map-independent validated profile and binder; `EnvironmentArt.Apply(material, profile)` binds the kit.
- `../../Resources/TerritoryFog.shader`: world-space surface sampling and water animation. No mesh UV requirement. Natural materials use continuous weighted world projections independent of face normals; constructed surfaces retain dominant-plane panel alignment.

The PNGs are source albedo assets, not screenshots. Import as sRGB, Repeat, mipmapped, trilinear, anisotropy 8, compressed at max 2048. Source images are retained at generator resolution; Unity performs its configured runtime resampling. No third-party texture-pack dependencies. Keep each PNG with its `.meta` to preserve GUID references.

## Reuse on another map

1. Assign the appropriate material preset to the map's mesh renderer. World-space meters define texture scale, so the same material can span many meshes without separate UV unwraps. Mesh transforms must use the intended world-meter scale.
2. To keep a shared material across surface types, set `_SurfaceRole` using `MaterialPropertyBlock`: 0 unstyled, 1 earth, 2 concrete, 3 steel, 4 rock, 5 water. Preserve the existing block when writing.
3. Supply that map's `_RoadMask` (R8, road coverage) and `_ShoreMask` (R8, distance to the base land-mass boundaries, excluding bridges and inland platforms, encoded as distance/8 meters). Also supply `_RockMask`: R8 distance outside the union of ground-level natural rock footprints (zero inside, distance/8 outside; white means no rock apron). All masks are linear, bilinear, clamp. Set `_FogBounds.xy` to the map's half extents; masks map world XZ / (2 * bounds) + 0.5. Black road mask means no roads; white shore mask means deep water/no wet edge. Shore distance under bridges must come from riverbanks, not deck edges.
4. Bind the viewport/team's `_FogMask` and `_FogColor`. For a revealed preview use black fog; white fog fully conceals every role, including water and specular highlights. Each independently fogged viewport/world needs its own material instance. Dispose runtime masks/materials when the map closes; never destroy shared imported textures.
5. Use **Spacewars → Balance Lab → Environment Art** for validated art settings. Tile sizes default to earth 7 m, shale 6 m, concrete 5 m and steel 4 m. Palette, water speed/ripple/shore/foam, moss and relief controls are profile-owned. Save increments the art revision. **Spacewars → Environment → Export Natural Frontier Materials** refreshes reusable `.mat` presets from that profile.

`_NaturalBlend` stores transition width, organic width variation, patch scale and ground elevation (default 0 m). The profile exposes the first three values. Set its W component for maps with a different ground elevation. Rock/soil contact uses the same world samples on both surfaces; rock crowns do not restart the soil band. Banks blend through their upper edge into cliff material. Keep meaningful metal/concrete construction joints crisp.

Three Crossings supplies its own roads and derived shore/rock masks through `ThreeCrossingsMaterials`. Its existing ground/road/slab/metal palette remains in **Balance Lab → Three Crossings Materials**. Do not copy those map coordinates to new maps. The standalone presets use shader defaults for this second palette; adapt them locally when styling a different map.

## Rendering and limits

Water animation uses shader time and world coordinates, with no per-frame texture upload or screen-color/depth readback. Ripples, pale intermittent shoreline foam and soft sky-color reflection are stylized; this is not physical water simulation or planar scene reflection. Water geometry remains the actual map's recessed surface. Generated albedos include authored material microstructure; layered rock and ground are reused on the existing bank walls. Bridge rails use steel and end posts use concrete.  shader motifs and optical normalization constants are fixed parts of the material, while designer controls are exposed by profile metadata.

All roles retain the existing fog path. The kit does not add camera, input, actor, obstacle or pathfinding behavior. Validate the appearance in the target map's real Player lighting, camera and fog before accepting a new map.

## F3: functioning dusty outpost

Profile `natural-frontier-v1@4` adds metadata-backed Detail and Weathering controls to the existing environment Balance Lab panel. `EnvironmentDetails` builds three local-space fractured stone variants and VentilationBlock, CableCabinet and PumpNode. `EnvironmentArtAssets.Export` saves their meshes/prefabs and Debris/Service materials. Modules have vertex tint, ground-centred pivots and no colliders or interactive behavior. Service modules use a non-emissive status lens so decoration does not resemble an owned gameplay building.

`ThreeCrossingsDetails` is the map adapter: deterministic clustered placement near cliff feet; conservative clearances against all support, solids, road masks, platforms and build sites. It batches stones and service equipment into two meshes. Module silhouettes are preserved in the reusable mesh source; world scale and density belong to the versioned art profile. No simulation RNG, per-frame generation, navigation edits or added colliders.

The map-owned `_WeatheringMask` stores R = edge deposits, G = contact shade, B = track wear. Another map supplies its own mask (black disables it) and binds its live fog mask on the same material. Dust is evaluated in world space with the profile wind direction; seams remain partially visible. Vertex colors are read only when `_Dressing` is enabled, keeping existing terrain/actor meshes compatible. Physical dimensions are in Unity meters; imported/generated meshes need no textures beyond the existing four source albedos.

### F4 localized deposits and scree

Profile revision 5 adds `dustPatchLength`, `dustCoverage` and `screeClusterSpacing`, with the same metadata-driven Balance Lab validation. Dust has true clean breaks at constructed edges, using a wind-aligned warped field; reach and opacity preserve exposed panel seams. Debris uses separated cliff-foot groups with large fragments placed first and a smaller fading fan. Clearance and the three service anchors are unchanged. The map adapter owns placement; the meshes, materials and controls remain reusable.
