using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Spacewars.StyleA.Preview
{
    // Raw-prefab resource inventory, independent of scene rendering counters.
    public static class StyleALodInventory
    {
        public static readonly string[] Names = { "tank", "explorer", "shkval", "headquarters", "factory", "refinery", "outpost", "mine", "scientificCenter" };

        [Serializable] public sealed class TextureRow
        {
            public string name, format;
            public int width, height, mipCount;
            public long runtimeBytes;
        }
        [Serializable] public sealed class AssetRow
        {
            public string name;
            public int meshInstances, uniqueMeshes, renderers, materialSlots, uniqueMaterials, lodGroups, unreadableMeshes;
            public long triangleInstances, uniqueMeshTriangles, vertexInstances, uniqueMeshRuntimeBytes, textureRuntimeBytes;
            public TextureRow[] textures;
        }
        [Serializable] public sealed class Report
        {
            public string classification = "DIAGNOSTIC_NOT_ACCEPTANCE";
            public string memoryMeaning = "Profiler.GetRuntimeMemorySizeLong estimate; not exact VRAM; per-asset textures may overlap";
            public AssetRow[] assets;
            public int globalUniqueMeshes, globalUniqueTextures;
            public long globalMeshRuntimeBytes, globalTextureRuntimeBytes;
        }

        public static long Triangles(Mesh mesh)
        {
            long count = 0;
            for (var i = 0; i < mesh.subMeshCount; i++)
            {
                if (mesh.GetTopology(i) != MeshTopology.Triangles)
                    throw new InvalidOperationException("Non-triangle topology: " + mesh.name);
                count += mesh.GetIndexCount(i) / 3;
            }
            return count;
        }

        public static Report Inspect(GameObject[] prefabs)
        {
            var allMeshes = new HashSet<Mesh>();
            var allTextures = new HashSet<Texture>();
            var rows = new List<AssetRow>();
            foreach (var prefab in prefabs)
            {
                if (!prefab) throw new InvalidOperationException("Missing audit prefab");
                var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                if (filters.Any(f => !f.sharedMesh)) throw new InvalidOperationException("Missing mesh: " + prefab.name);
                var meshes = filters.Select(f => f.sharedMesh).Distinct().ToArray();
                var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
                var slots = renderers.SelectMany(r => r.sharedMaterials).ToArray();
                if (slots.Any(m => !m)) throw new InvalidOperationException("Missing material: " + prefab.name);
                var materials = slots.Distinct().ToArray();
                var textures = materials.SelectMany(m => m.GetTexturePropertyNames().Select(m.GetTexture))
                    .Where(t => t).Distinct().OrderBy(t => t.name).ToArray();
                allMeshes.UnionWith(meshes);
                allTextures.UnionWith(textures);
                rows.Add(new AssetRow
                {
                    name = prefab.name, meshInstances = filters.Length, uniqueMeshes = meshes.Length,
                    renderers = renderers.Length, materialSlots = slots.Length, uniqueMaterials = materials.Length,
                    lodGroups = prefab.GetComponentsInChildren<LODGroup>(true).Length,
                    unreadableMeshes = meshes.Count(m => !m.isReadable),
                    triangleInstances = filters.Sum(f => Triangles(f.sharedMesh)),
                    uniqueMeshTriangles = meshes.Sum(Triangles), vertexInstances = filters.Sum(f => (long)f.sharedMesh.vertexCount),
                    uniqueMeshRuntimeBytes = meshes.Sum(m => Profiler.GetRuntimeMemorySizeLong(m)),
                    textureRuntimeBytes = textures.Sum(t => Profiler.GetRuntimeMemorySizeLong(t)),
                    textures = textures.Select(t => new TextureRow { name = t.name, width = t.width, height = t.height,
                        mipCount = t.mipmapCount, format = t.graphicsFormat.ToString(), runtimeBytes = Profiler.GetRuntimeMemorySizeLong(t) }).ToArray()
                });
            }
            return new Report { assets = rows.ToArray(), globalUniqueMeshes = allMeshes.Count,
                globalUniqueTextures = allTextures.Count, globalMeshRuntimeBytes = allMeshes.Sum(m => Profiler.GetRuntimeMemorySizeLong(m)),
                globalTextureRuntimeBytes = allTextures.Sum(t => Profiler.GetRuntimeMemorySizeLong(t)) };
        }
    }
}
