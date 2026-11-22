using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private static readonly int ReinforcementBaseColor = Shader.PropertyToID("_BaseColor");
        private readonly Dictionary<Material, Material> reinforcementMaterials = new Dictionary<Material, Material>();
        private static readonly int WarpProgress = Shader.PropertyToID("_Progress");
        private static readonly int WarpOpacity = Shader.PropertyToID("_Opacity");
        private static readonly int WarpPulseCount = Shader.PropertyToID("_PulseCount");
        private Material reinforcementLight;
        private Mesh orbitalCylinderMesh;
        private AbilityFeedbackProfile reinforcementVisuals;
        private MaterialPropertyBlock reinforcementProperties;
        private readonly List<Transform> reinforcementPreviewModels = new List<Transform>();
        private string reinforcementPreviewKey;
        private bool reinforcementPreviewValid;
        private const float ReinforcementPreviewOpacity = .5f;

        internal void SetReinforcementPreviewModels(NavPoint[] points, PlayableEntityKind kind,
            bool upgraded, bool valid, Camera camera)
        {
            if (points == null || points.Length == 0)
            {
                HideReinforcementPreviewModels();
                return;
            }

            string key = UnitModelKey(kind, upgraded);
            if (key != reinforcementPreviewKey)
            {
                ClearReinforcementPreviewModels();
                reinforcementPreviewKey = key;
            }
            if (valid != reinforcementPreviewValid)
                foreach (var model in reinforcementPreviewModels) PaintReinforcementPreview(model, valid);
            reinforcementPreviewValid = valid;

            while (reinforcementPreviewModels.Count < points.Length)
            {
                var model = Model(key, root, true);
                model.name = "Reinforcement Placement Preview";
                model.localScale = Vector3.one * UnitModelScale(kind);
                foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    var materials = renderer.sharedMaterials;
                    for (int slot = 0; slot < materials.Length; slot++)
                        if (materials[slot]) materials[slot] = TransparentReinforcementMaterial(materials[slot]);
                    renderer.sharedMaterials = materials;
                }
                PaintReinforcementPreview(model, valid);
                reinforcementPreviewModels.Add(model);
            }

            for (int i = 0; i < points.Length; i++)
            {
                var model = reinforcementPreviewModels[i];
                var slope = profile.AuthoredMap?.SurfaceGradient(points[i]) ?? default(NavPoint);
                var normal = new Vector3((float)-slope.X, 1, (float)-slope.Z).normalized;
                model.SetPositionAndRotation(Point(points[i]), Quaternion.FromToRotation(Vector3.up, normal)
                    * Quaternion.Euler(0, 90, 0));
                if (!model.gameObject.activeSelf) model.gameObject.SetActive(true);
                if (model.TryGetComponent<ModelDetailLod>(out var detail)) detail.Select(camera);
                else if (model.TryGetComponent<TankDistantLod>(out var distant)) distant.Select(camera);
            }
            for (int i = points.Length; i < reinforcementPreviewModels.Count; i++)
                reinforcementPreviewModels[i].gameObject.SetActive(false);
        }

        private void PaintReinforcementPreview(Transform model, bool valid)
        {
            reinforcementProperties ??= new MaterialPropertyBlock();
            Color tint = valid ? OrbitalTheme.Cyan : PlayableBootstrap.InvalidOrbitalPreview;
            tint.a = ReinforcementPreviewOpacity;
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    if (!materials[slot] || !materials[slot].HasProperty(ReinforcementBaseColor)) continue;
                    renderer.GetPropertyBlock(reinforcementProperties, slot);
                    reinforcementProperties.SetColor(ReinforcementBaseColor, tint);
                    reinforcementProperties.SetColor("_EmissionColor", tint * .3f);
                    renderer.SetPropertyBlock(reinforcementProperties, slot);
                }
            }
        }

        internal void HideReinforcementPreviewModels()
        {
            foreach (var model in reinforcementPreviewModels)
                if (model.gameObject.activeSelf) model.gameObject.SetActive(false);
        }

        private void ClearReinforcementPreviewModels()
        {
            HideReinforcementPreviewModels();
            foreach (var model in reinforcementPreviewModels) ReleaseWorldObject(model.gameObject);
            reinforcementPreviewModels.Clear();
            reinforcementPreviewKey = null;
        }

        private Material TransparentReinforcementMaterial(Material source)
        {
            if (reinforcementMaterials.TryGetValue(source, out var result)) return result;
            result = new Material(source) { name = source.name, renderQueue = (int)RenderQueue.Transparent };
            result.SetOverrideTag("RenderType", "Transparent");
            result.SetFloat("_Surface", 1);
            result.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            result.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            result.SetFloat("_ZWrite", 0);
            result.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            result.SetShaderPassEnabled("ShadowCaster", false);
            reinforcementMaterials.Add(source, result);
            return result;
        }

        public void UpdateReinforcement(Actor actor, PlayableEntitySnapshot unit)
        {
            if (unit.MaterializationProgress >= 1 && actor.ReinforcementOriginalMaterials == null) return;
            reinforcementProperties ??= new MaterialPropertyBlock();
            if (unit.MaterializationProgress >= 1)
            {
                foreach (var entry in actor.ReinforcementOriginalMaterials)
                {
                    if (!entry.Key) continue;
                    entry.Key.sharedMaterials = entry.Value;
                    SetReinforcementOpacity(entry.Key, entry.Value, 1);
                }
                actor.ReinforcementOriginalMaterials = null;
                ReleaseWorldObject(actor.ReinforcementCylinder.gameObject);
                actor.ReinforcementCylinder = null;
                return;
            }

            reinforcementVisuals ??= AbilityFeedbackProfile.Load();
            actor.ReinforcementOriginalMaterials ??= new Dictionary<Renderer, Material[]>();
            float progress = Mathf.Clamp01((float)unit.MaterializationProgress);
            float opacity = Mathf.Lerp(reinforcementVisuals.reinforcementStartOpacity, 1, progress);
            foreach (var renderer in ActorModelRenderers(actor))
            {
                if (!(renderer is MeshRenderer)) continue;
                if (!actor.ReinforcementOriginalMaterials.TryGetValue(renderer, out var originals))
                {
                    originals = renderer.sharedMaterials;
                    actor.ReinforcementOriginalMaterials.Add(renderer, originals);
                    var transparent = new Material[originals.Length];
                    for (int slot = 0; slot < originals.Length; slot++)
                        transparent[slot] = originals[slot] ? TransparentReinforcementMaterial(originals[slot]) : null;
                    renderer.sharedMaterials = transparent;
                }
                SetReinforcementOpacity(renderer, originals, opacity);
            }

            if (!actor.ReinforcementCylinder)
                actor.ReinforcementCylinder = CreateReinforcementCylinder(actor.Root, (float)PlayableUnitRules.Radius(profile, unit.Kind));
            reinforcementProperties.Clear();
            reinforcementProperties.SetFloat(WarpProgress, progress);
            actor.ReinforcementCylinder.SetPropertyBlock(reinforcementProperties);
        }

        private void SetReinforcementOpacity(Renderer renderer, Material[] originals, float opacity)
        {
            for (int slot = 0; slot < originals.Length; slot++)
            {
                if (!originals[slot] || !originals[slot].HasProperty(ReinforcementBaseColor)) continue;
                renderer.GetPropertyBlock(reinforcementProperties, slot);
                Color color = reinforcementProperties.HasColor(ReinforcementBaseColor)
                    ? reinforcementProperties.GetColor(ReinforcementBaseColor)
                    : originals[slot].GetColor(ReinforcementBaseColor);
                color.a = opacity;
                reinforcementProperties.SetColor(ReinforcementBaseColor, color);
                renderer.SetPropertyBlock(reinforcementProperties, slot);
            }
        }

        private MeshRenderer CreateReinforcementCylinder(Transform parent, float radius)
        {
            if (!reinforcementLight)
            {
                reinforcementLight = new Material(Resources.Load<Shader>("ReinforcementWarp"));
                reinforcementLight.SetFloat(WarpOpacity, reinforcementVisuals.reinforcementCylinderOpacity);
                reinforcementLight.SetFloat(WarpPulseCount, reinforcementVisuals.reinforcementPulseCount);
                orbitalCylinderMesh ??= BuildReinforcementCylinder();
            }
            var effect = new GameObject("Reinforcement Warp Cylinder") { layer = presentationLayer };
            effect.transform.SetParent(parent, false);
            effect.transform.localPosition = Vector3.up * reinforcementVisuals.groundLift;
            effect.transform.localScale = new Vector3(radius, reinforcementVisuals.reinforcementCylinderHeight, radius);
            effect.AddComponent<MeshFilter>().sharedMesh = orbitalCylinderMesh;
            var renderer = effect.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = reinforcementLight;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private static Mesh BuildReinforcementCylinder()
        {
            const int sides = 48;
            const int rows = 4;
            var vertices = new Vector3[(sides + 1) * rows];
            var uv = new Vector2[vertices.Length];
            var indices = new int[sides * 12];
            for (int i = 0; i <= sides; i++)
            {
                float turn = i / (float)sides;
                float angle = 2 * Mathf.PI * turn;
                var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                int at = i * rows;
                vertices[at] = radial;
                vertices[at + 1] = radial + Vector3.up;
                vertices[at + 2] = radial * .82f;
                vertices[at + 3] = radial * 1.08f;
                uv[at] = new Vector2(turn, 0);
                uv[at + 1] = new Vector2(turn, 1);
                uv[at + 2] = new Vector2(turn, -2);
                uv[at + 3] = new Vector2(turn, -1);
                if (i == sides) continue;
                int tri = i * 12;
                for (int surface = 0; surface < 2; surface++)
                {
                    int a = at + surface * 2, b = a + rows;
                    indices[tri++] = a;
                    indices[tri++] = a + 1;
                    indices[tri++] = b;
                    indices[tri++] = b;
                    indices[tri++] = a + 1;
                    indices[tri++] = b + 1;
                }
            }
            var mesh = new Mesh { name = "Reinforcement Warp Cylinder", vertices = vertices, uv = uv, triangles = indices };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void DisposeReinforcementMaterials()
        {
            foreach (var value in reinforcementMaterials.Values) ReleaseWorldObject(value);
            reinforcementMaterials.Clear();
            if (reinforcementLight) ReleaseWorldObject(reinforcementLight);
            if (orbitalCylinderMesh) ReleaseWorldObject(orbitalCylinderMesh);
        }
    }
}
