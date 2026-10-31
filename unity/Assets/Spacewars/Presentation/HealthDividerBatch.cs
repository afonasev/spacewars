using System;
using UnityEngine;

namespace Spacewars.Presentation
{
    /// <summary>Owns the single render submission for the divider cubes of one health bar.</summary>
    internal sealed class HealthDividerBatch : IDisposable
    {
        private readonly Transform[] sources;
        private readonly MeshRenderer[] sourceRenderers;
        private readonly bool[] sourceEnabled;
        private readonly Vector3[] positions;
        private readonly Vector3[] scales;
        private readonly Quaternion[] rotations;
        private readonly CombineInstance[] instances;
        private readonly GameObject host;
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private bool disposed;
        private int rebuildCount;

        internal int RebuildCount => rebuildCount;

        internal HealthDividerBatch(Transform healthRoot, Transform[] dividerTransforms)
        {
            if (healthRoot == null) throw new ArgumentNullException(nameof(healthRoot));
            if (dividerTransforms == null) throw new ArgumentNullException(nameof(dividerTransforms));

            sources = dividerTransforms;
            int count = sources.Length;
            sourceRenderers = new MeshRenderer[count];
            sourceEnabled = new bool[count];
            positions = new Vector3[count];
            scales = new Vector3[count];
            rotations = new Quaternion[count];
            instances = new CombineInstance[count];

            for (int i = 0; i < count; i++)
            {
                var source = sources[i];
                sourceRenderers[i] = source.GetComponent<MeshRenderer>();
                sourceEnabled[i] = sourceRenderers[i].enabled;
                var filter = source.GetComponent<MeshFilter>();
                instances[i].mesh = filter.sharedMesh;
                instances[i].subMeshIndex = 0;
            }

            var first = sourceRenderers[0];
            host = new GameObject("Batched health dividers") { layer = first.gameObject.layer };
            host.transform.SetParent(healthRoot, false);
            mesh = new Mesh { name = "Health divider batch" };
            mesh.MarkDynamic();
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = first.sharedMaterials;

            var properties = new MaterialPropertyBlock();
            first.GetPropertyBlock(properties);
            renderer.SetPropertyBlock(properties);
            for (int slot = 0; slot < first.sharedMaterials.Length; slot++)
            {
                properties.Clear();
                first.GetPropertyBlock(properties, slot);
                // An absent indexed block must remain null: an allocated empty block
                // masks the renderer-wide block in Unity's material property lookup.
                renderer.SetPropertyBlock(properties.isEmpty ? null : properties, slot);
            }

            renderer.shadowCastingMode = first.shadowCastingMode;
            renderer.receiveShadows = first.receiveShadows;
            renderer.lightProbeUsage = first.lightProbeUsage;
            renderer.reflectionProbeUsage = first.reflectionProbeUsage;
            renderer.probeAnchor = first.probeAnchor;
            renderer.motionVectorGenerationMode = first.motionVectorGenerationMode;
            renderer.allowOcclusionWhenDynamic = first.allowOcclusionWhenDynamic;
            renderer.renderingLayerMask = first.renderingLayerMask;
            renderer.sortingLayerID = first.sortingLayerID;
            renderer.sortingOrder = first.sortingOrder;

            Rebuild();
            SetBatched(true);
        }

        /// <summary>Rebuild only when layout changes local divider geometry.</summary>
        internal void RefreshIfChanged()
        {
            if (disposed) return;
            bool changed = false;
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                if (source == null) return;
                Vector3 position = source.localPosition;
                Vector3 scale = source.localScale;
                Quaternion rotation = source.localRotation;
                if (!position.Equals(positions[i]) || !scale.Equals(scales[i]) || !rotation.Equals(rotations[i])) changed = true;
            }
            if (changed) Rebuild();
        }

        private void Rebuild()
        {
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                positions[i] = source.localPosition;
                scales[i] = source.localScale;
                rotations[i] = source.localRotation;
                instances[i].transform = Matrix4x4.TRS(positions[i], rotations[i], scales[i]);
            }
            mesh.Clear();
            if (instances.Length != 0) mesh.CombineMeshes(instances, true, true, false);
            rebuildCount++;
        }

        private void SetBatched(bool batched)
        {
            for (int i = 0; i < sourceRenderers.Length; i++)
                if (sourceRenderers[i] != null) sourceRenderers[i].enabled = batched ? false : sourceEnabled[i];
            if (renderer != null) renderer.enabled = batched;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SetBatched(false);
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(host);
                UnityEngine.Object.Destroy(mesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }
}
