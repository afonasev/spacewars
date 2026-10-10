# Alien geology kit (F2)

The original four texture sources and provenance are retained. The material shader converts natural texture samples to luminance and uses a continuous world-space basalt/oxidation field; it does not assign biomes to corners. Soil, rock foot and riverbank use the same samples and graded map masks. No new AI bitmap assets were generated for F2.

`Environment/NaturalFrontier/Profile.json` revision 3 stores the reusable geological scale, exposure, values, strata and erosion depth with validated authoring metadata in EnvironmentArtProfile. The shader's palette defines basalt and ferric-orange hues. Four-metre steel, five-metre concrete, six-metre rock and seven-metre earth sampling remain independently reusable.

`EnvironmentArtAssets.Export` exports the five materials and three centred, metre-scaled rock mesh/prefab modules: BasaltSpur, LayeredOutcrop and WeatheredSlab. Their pivots are at ground centre. They have no colliders: a map adapter must fit them inside its own obstacle envelopes. The same TerrainMesh.ErodedRock generator dresses the authored Three Crossings solids without changing authoritative polygons or heights. Its upper layers overlap the lower rock volume to avoid exposed gaps.

Three Crossings alone supplies road, bank and rock-distance masks. Expanded eight-metre road paint and twelve-metre bridge flares are clipped against authoritative movement geometry; roads can remain narrower at genuine pinch points. Bridge approaches reach the deck width. This changes presentation, not traversal clearance.

Use on another map: apply EnvironmentArt to its shared fog material, provide that map's masks/bounds, choose surface role 1/2/3/4/5, and place modules only within registered solids. Preserve the map's viewport fog. All art fields have range/step metadata; map and surface profiles remain separate from simulation.
