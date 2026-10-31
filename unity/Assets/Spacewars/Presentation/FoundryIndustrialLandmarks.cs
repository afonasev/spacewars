using System;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    /// <summary>Presentation-only factories fitted inside the four already-blocked corner solids.</summary>
    public static class FoundryIndustrialLandmarks
    {
        private const float WallTintR = .30f, WallTintG = .285f, WallTintB = .255f;
        private const float RoofTintR = .42f, RoofTintG = .245f, RoofTintB = .13f;
        private const float IronTintR = .19f, IronTintG = .205f, IronTintB = .20f;
        private const float ConcreteTintR = .36f, ConcreteTintG = .335f, ConcreteTintB = .29f;
        private const float InteriorTintR = .105f, InteriorTintG = .092f, InteriorTintB = .078f;

        /// <summary>
        /// Builds the four abandoned foundries from the final four authored solid bounds.
        /// The returned meshes are owned by the caller and must be destroyed with its world.
        /// </summary>
        public static void Build(Transform parent, Material material, FoundryMap map, FoundrySurfaceProfile settings, List<Mesh> ownedMeshes)
        {
            BuildInternal(parent, material, map, settings, ownedMeshes, true);
        }

#if UNITY_EDITOR
        /// <summary>Editor-only part-by-part equivalent used to measure lossless static consolidation.</summary>
        public static void BuildPerPartForMeasurement(Transform parent, Material material, FoundryMap map, FoundrySurfaceProfile settings, List<Mesh> ownedMeshes)
        {
            BuildInternal(parent, material, map, settings, ownedMeshes, false);
        }
#endif

        private static void BuildInternal(Transform parent, Material material, FoundryMap map, FoundrySurfaceProfile settings, List<Mesh> ownedMeshes, bool consolidate)
        {
            if (!parent) throw new ArgumentNullException(nameof(parent));
            if (!material) throw new ArgumentNullException(nameof(material));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (ownedMeshes == null) throw new ArgumentNullException(nameof(ownedMeshes));
            if (map.Solids.Count < 4) throw new ArgumentException("Foundry needs four blocked corner bounds.", nameof(map));
            settings.Validate();
            var art = FoundryIndustrialProfile.Load();

            // Preserve one culling and shadow bound per factory. Consolidation is only within
            // a single factory and a complete material/property-block key.
            for (int i = map.Solids.Count - 4; i < map.Solids.Count; i++)
            {
                int index = i - (map.Solids.Count - 4);
                var groups = CreateGroups(parent);
                BuildFactory(map.Solids[i], index, map, settings, art, groups);
                if (consolidate) AttachGroups(parent, material, ownedMeshes, index, groups);
                else AttachParts(parent, material, ownedMeshes, index, groups);
            }
        }

        private static MeshBuilder[] CreateGroups(Transform parent) => new[] {
            new MeshBuilder(parent), new MeshBuilder(parent), new MeshBuilder(parent), new MeshBuilder(parent), new MeshBuilder(parent)
        };

        private static readonly string[] GroupNames = { "Foundry industrial walls", "Foundry oxidized roof", "Foundry pipes and crane", "Foundry weathered concrete", "Foundry dark loading bay" };
        private static readonly Color[] GroupColors = {
            new Color(WallTintR, WallTintG, WallTintB), new Color(RoofTintR, RoofTintG, RoofTintB),
            new Color(IronTintR, IronTintG, IronTintB), new Color(ConcreteTintR, ConcreteTintG, ConcreteTintB),
            new Color(InteriorTintR, InteriorTintG, InteriorTintB)
        };

        private static void AttachGroups(Transform parent, Material material, List<Mesh> ownedMeshes, int factoryIndex, MeshBuilder[] groups)
        {
            for (int group = 0; group < groups.Length; group++)
                Attach(parent, material, ownedMeshes, GroupNames[group] + " factory " + (factoryIndex + 1), groups[group], GroupColors[group]);
        }

        private static void AttachParts(Transform parent, Material material, List<Mesh> ownedMeshes, int factoryIndex, MeshBuilder[] groups)
        {
            for (int group = 0; group < groups.Length; group++)
            {
                int part = 0;
                foreach (var mesh in groups[group].BuildParts(GroupNames[group] + " unoptimized factory-" + (factoryIndex + 1)))
                {
                    var name = GroupNames[group] + " unoptimized factory-" + (factoryIndex + 1) + " part-" + (++part).ToString("D3");
                    AttachMesh(parent, material, ownedMeshes, name, mesh, GroupColors[group]);
                }
            }
        }

        private static void BuildFactory(NavObstacle bound, int index, FoundryMap map, FoundrySurfaceProfile surface, FoundryIndustrialProfile art, MeshBuilder[] group)
        {
            float width = (float)(bound.MaxX - bound.MinX), depth = (float)(bound.MaxZ - bound.MinZ);
            float unit = Mathf.Min(width, depth) / 18f;
            float cx = (float)((bound.MinX + bound.MaxX) * .5), cz = (float)((bound.MinZ + bound.MaxZ) * .5);
            float baseY = (float)bound.Top - Mathf.Max(.25f, surface.crownRelief * .55f);
            float bodyHeight = Mathf.Max(2f, surface.industrialHeight * .82f);
            float halfWidth = width * .315f, halfDepth = depth * .315f;
            float yaw = index == 1 || index == 2 ? 90f : 0f;
            var basis = new FactoryBasis(cx, cz, yaw);
            var walls = group[0]; var roofs = group[1]; var iron = group[2]; var concrete = group[3]; var interior = group[4];
            concrete.Box(basis.Point(0, baseY + .10f * unit, 0), new Vector3(halfWidth * 2.05f, .20f * unit, halfDepth * 2.05f), basis.Flat);

            // A framed shed leaves a broad, genuinely open loading bay on the camera-facing side.
            float wall = .34f * unit, doorHalf = halfWidth * .24f, doorHeight = bodyHeight * .72f;
            walls.Box(basis.Point(-halfWidth + wall * .5f, baseY + bodyHeight * .5f, 0), new Vector3(wall, bodyHeight, halfDepth * 2), basis.Flat);
            walls.Box(basis.Point(halfWidth - wall * .5f, baseY + bodyHeight * .5f, 0), new Vector3(wall, bodyHeight, halfDepth * 2), basis.Flat);
            walls.Box(basis.Point(0, baseY + bodyHeight * .5f, halfDepth - wall * .5f), new Vector3(halfWidth * 2, bodyHeight, wall), basis.Flat);
            float sidePier = halfWidth - doorHalf;
            walls.Box(basis.Point(-(doorHalf + sidePier * .5f), baseY + bodyHeight * .5f, -halfDepth + wall * .5f),
                new Vector3(sidePier, bodyHeight, wall), basis.Flat);
            walls.Box(basis.Point(doorHalf + sidePier * .5f, baseY + bodyHeight * .5f, -halfDepth + wall * .5f),
                new Vector3(sidePier, bodyHeight, wall), basis.Flat);
            walls.Box(basis.Point(0, baseY + doorHeight + (bodyHeight - doorHeight) * .5f, -halfDepth + wall * .5f),
                new Vector3(doorHalf * 2, bodyHeight - doorHeight, wall), basis.Flat);
            interior.Box(basis.Point(0, baseY + doorHeight * .48f, -halfDepth + wall * 2.2f),
                new Vector3(doorHalf * 1.8f, doorHeight * .94f, .12f * unit), basis.Flat);

            // Taller furnace nave and a low loading annex create a stepped industrial silhouette.
            walls.Box(basis.Point(-halfWidth * .31f, baseY + bodyHeight * .59f, halfDepth * .02f),
                new Vector3(halfWidth * .66f, bodyHeight * .18f, halfDepth * 1.04f), basis.Flat);
            walls.Box(basis.Point(halfWidth * .68f, baseY + bodyHeight * .28f, halfDepth * .18f),
                new Vector3(halfWidth * .45f, bodyHeight * .55f, halfDepth * .70f), basis.Flat);

            float roofWidth = halfWidth * 1.98f;
            float roofHalfDepth = halfDepth * .97f;
            float gap = Mathf.Min(art.roofGap * unit, roofHalfDepth * .24f);
            float panelHalfDepth = roofHalfDepth - gap * .5f;
            float rise = panelHalfDepth * .46f;
            float ridgeY = baseY + bodyHeight + rise;
            int sections = 6;
            float sectionGap = .18f * unit;
            float sectionWidth = (roofWidth - sectionGap * (sections - 1)) / sections;
            float roofAngle = Mathf.Atan2(rise, panelHalfDepth) * Mathf.Rad2Deg;
            for (int side = -1; side <= 1; side += 2)
            {
                float centerZ = side * (gap * .5f + panelHalfDepth * .5f);
                float centerY = ridgeY - rise * .5f;
                var pitch = basis.Pitched(side < 0 ? -roofAngle : roofAngle);
                for (int section = 0; section < sections; section++)
                {
                    bool broken = side < 0 ? section == (index % 3 + 1) : section == (4 - index % 3);
                    if (broken) continue;
                    float x = -roofWidth * .5f + sectionWidth * .5f + section * (sectionWidth + sectionGap);
                    roofs.Box(basis.Point(x, centerY, centerZ), new Vector3(sectionWidth, .20f * unit, panelHalfDepth), pitch);
                }
            }

            // Ridge cap, exposed transverse trusses and longitudinal purlins remain visible through roof gaps.
            iron.Beam(basis.Point(0, ridgeY + .08f * unit, 0), basis.Point(0, ridgeY + .08f * unit, 0) + basis.Point(roofWidth * .5f, 0, 0) - basis.Point(0, 0, 0), .22f * unit, .24f * unit);
            float ribStep = Mathf.Max(1.2f, art.ribSpacing * unit * 2.4f);
            int ribCount = Mathf.Max(4, Mathf.FloorToInt(roofWidth / ribStep));
            for (int rib = 0; rib <= ribCount; rib++)
            {
                float x = -roofWidth * .5f + roofWidth * rib / ribCount;
                iron.Beam(basis.Point(x, ridgeY, 0), basis.Point(x, baseY + bodyHeight + .12f * unit, -roofHalfDepth), .18f * unit, .16f * unit);
                iron.Beam(basis.Point(x, ridgeY, 0), basis.Point(x, baseY + bodyHeight + .12f * unit, roofHalfDepth), .18f * unit, .16f * unit);
            }
            for (int row = -1; row <= 1; row++)
            {
                float z = row * panelHalfDepth * .55f;
                float y = ridgeY - Mathf.Abs(z) * rise / panelHalfDepth + .1f * unit;
                iron.Beam(basis.Point(-roofWidth * .48f, y, z), basis.Point(roofWidth * .48f, y, z), .13f * unit, .13f * unit);
            }

            // Heavy lintels and wall braces frame the loading opening and side bays.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int rib = 0; rib < 4; rib++)
                {
                    float z = -halfDepth * .72f + halfDepth * .48f * rib;
                    iron.Box(basis.Point(side * (halfWidth - wall), baseY + bodyHeight * .48f, z),
                        new Vector3(.09f * unit, bodyHeight * .9f, .12f * unit), basis.Flat);
                }
            }
            iron.Beam(basis.Point(-doorHalf, baseY + doorHeight, -halfDepth - .04f * unit), basis.Point(doorHalf, baseY + doorHeight, -halfDepth - .04f * unit), .20f * unit, .20f * unit);

            // Twin exhausts and a bent service pipe are fixed static geometry, with no particles or colliders.
            float stackHeight = art.chimneyHeight * unit;
            Vector3 stackA = basis.Point(-halfWidth * .58f, ridgeY + .15f * unit, -halfDepth * .35f);
            Vector3 stackB = basis.Point(-halfWidth * .58f, ridgeY + stackHeight, -halfDepth * .35f);
            iron.Cylinder(stackA, stackB, .18f * unit, 10);
            iron.Cylinder(basis.Point(-halfWidth * .56f, stackB.y, -halfDepth * .38f),
                basis.Point(-halfWidth * .56f, stackB.y + .16f * unit, -halfDepth * .38f), .24f * unit, 10);
            iron.Cylinder(basis.Point(halfWidth * .58f, ridgeY + .05f * unit, halfDepth * .35f),
                basis.Point(halfWidth * .58f, ridgeY + stackHeight * .68f, halfDepth * .35f), .15f * unit, 8);
            Vector3 pipeStart = basis.Point(-halfWidth * .82f, ridgeY + .16f * unit, halfDepth * .2f);
            Vector3 pipeElbow = basis.Point(-halfWidth * .82f, ridgeY + stackHeight * .24f, halfDepth * .2f);
            Vector3 pipeEnd = basis.Point(-halfWidth * .46f, ridgeY + stackHeight * .24f, halfDepth * .2f);
            iron.Cylinder(pipeStart, pipeElbow, .085f * unit, 8);
            iron.Cylinder(pipeElbow, pipeEnd, .085f * unit, 8);

            // Compact gantry crane and diagonal stay remain a full margin inside the corner solid.
            float craneX = halfWidth * 1.10f, craneZ = -halfDepth * .78f;
            float reach = Mathf.Min(art.craneReach * unit, halfWidth * .74f);
            Vector3 mastFoot = basis.Point(craneX, baseY + .08f * unit, craneZ);
            Vector3 mastTop = basis.Point(craneX, ridgeY + bodyHeight * .48f, craneZ);
            Vector3 boomEnd = basis.Point(craneX - reach, mastTop.y, craneZ);
            iron.Beam(mastFoot, mastTop, .18f * unit, .18f * unit);
            iron.Beam(mastTop, boomEnd, .16f * unit, .16f * unit);
            iron.Beam(basis.Point(craneX, mastTop.y - .12f * unit, craneZ),
                basis.Point(craneX - reach * .55f, mastTop.y + .18f * unit, craneZ), .10f * unit, .10f * unit);
            iron.Cylinder(basis.Point(craneX - reach * .74f, mastTop.y - .02f * unit, craneZ),
                basis.Point(craneX - reach * .74f, mastTop.y - bodyHeight * .26f, craneZ), .045f * unit, 6);
        }

        private static void Attach(Transform parent, Material material, List<Mesh> ownedMeshes, string name, MeshBuilder builder, Color tint)
        {
            if (builder.Triangles == 0) return;
            AttachMesh(parent, material, ownedMeshes, name, builder.Build(name), tint);
        }

        private static void AttachMesh(Transform parent, Material material, List<Mesh> ownedMeshes, string name, Mesh mesh, Color tint)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", tint);
            block.SetFloat("_SurfaceRole", 12);
            renderer.SetPropertyBlock(block);
            ownedMeshes.Add(mesh);
        }

        private readonly struct FactoryBasis
        {
            private readonly float x, z, c, s;
            public readonly float Yaw;
            public Quaternion Flat => Quaternion.Euler(0, Yaw, 0);
            public FactoryBasis(float x, float z, float yaw)
            { this.x = x; this.z = z; Yaw = yaw; c = Mathf.Cos(yaw * Mathf.Deg2Rad); s = Mathf.Sin(yaw * Mathf.Deg2Rad); }
            public Vector3 Point(float localX, float y, float localZ) => new Vector3(x + localX * c + localZ * s, y, z - localX * s + localZ * c);
            public Quaternion Pitched(float degrees) => Flat * Quaternion.AngleAxis(degrees, Vector3.right);
        }

        private sealed class MeshBuilder
        {
            private readonly Transform parent;
            private readonly List<Vector3> vertices = new List<Vector3>(2048);
            private readonly List<Vector2> uv = new List<Vector2>(2048);
            private readonly List<int> triangles = new List<int>(3072);
            private readonly List<PartRange> parts = new List<PartRange>(128);
            public int Triangles => triangles.Count / 3;
            public MeshBuilder(Transform parent) { this.parent = parent; }
            private Vector3 Local(Vector3 point) => parent.InverseTransformPoint(point);

            public void Box(Vector3 center, Vector3 size, Quaternion rotation)
            {
                int vertexStart = vertices.Count, triangleStart = triangles.Count;
                Vector3 h = size * .5f;
                Vector3[] p = {
                    new Vector3(-h.x,-h.y,-h.z), new Vector3(h.x,-h.y,-h.z), new Vector3(h.x,h.y,-h.z), new Vector3(-h.x,h.y,-h.z),
                    new Vector3(-h.x,-h.y,h.z), new Vector3(h.x,-h.y,h.z), new Vector3(h.x,h.y,h.z), new Vector3(-h.x,h.y,h.z)
                };
                for (int i = 0; i < p.Length; i++) p[i] = Local(center + rotation * p[i]);
                Quad(p[0], p[3], p[2], p[1]); Quad(p[4], p[5], p[6], p[7]);
                Quad(p[0], p[4], p[7], p[3]); Quad(p[1], p[2], p[6], p[5]);
                Quad(p[3], p[7], p[6], p[2]); Quad(p[0], p[1], p[5], p[4]);
                parts.Add(new PartRange(vertexStart, vertices.Count - vertexStart, triangleStart, triangles.Count - triangleStart));
            }

            public void Beam(Vector3 a, Vector3 b, float width, float depth)
            {
                Vector3 direction = b - a;
                var rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
                Box((a + b) * .5f, new Vector3(width, direction.magnitude, depth), rotation);
            }

            public void Cylinder(Vector3 a, Vector3 b, float radius, int sides)
            {
                int vertexStart = vertices.Count, triangleStart = triangles.Count;
                Vector3 axis = (b - a).normalized;
                Vector3 tangent = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right).normalized;
                Vector3 bitangent = Vector3.Cross(axis, tangent).normalized;
                int start = vertices.Count;
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * Mathf.PI * 2 / sides;
                    Vector3 offset = (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * radius;
                    vertices.Add(Local(a + offset)); uv.Add(new Vector2((float)i / sides, 0));
                    vertices.Add(Local(b + offset)); uv.Add(new Vector2((float)i / sides, 1));
                }
                for (int i = 0; i < sides; i++)
                {
                    int next = (i + 1) % sides;
                    int a0 = start + i * 2, b0 = a0 + 1, a1 = start + next * 2, b1 = a1 + 1;
                    triangles.Add(a0); triangles.Add(b0); triangles.Add(b1);
                    triangles.Add(a0); triangles.Add(b1); triangles.Add(a1);
                }
                int capA = vertices.Count; vertices.Add(Local(a)); uv.Add(Vector2.zero);
                int capB = vertices.Count; vertices.Add(Local(b)); uv.Add(Vector2.one);
                for (int i = 0; i < sides; i++)
                {
                    int next = (i + 1) % sides;
                    triangles.Add(capA); triangles.Add(start + next * 2); triangles.Add(start + i * 2);
                    triangles.Add(capB); triangles.Add(start + i * 2 + 1); triangles.Add(start + next * 2 + 1);
                }
                parts.Add(new PartRange(vertexStart, vertices.Count - vertexStart, triangleStart, triangles.Count - triangleStart));
            }

            private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0, true); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }

            public List<Mesh> BuildParts(string prefix)
            {
                var result = new List<Mesh>(parts.Count);
                for (int i = 0; i < parts.Count; i++)
                {
                    var range = parts[i];
                    var partVertices = vertices.GetRange(range.vertexStart, range.vertexCount);
                    var partUv = uv.GetRange(range.vertexStart, range.vertexCount);
                    var partIndices = new List<int>(range.indexCount);
                    for (int index = range.indexStart; index < range.indexStart + range.indexCount; index++)
                        partIndices.Add(triangles[index] - range.vertexStart);
                    var mesh = new Mesh { name = prefix + " part-" + (i + 1).ToString("D3"), indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.SetVertices(partVertices); mesh.SetUVs(0, partUv); mesh.SetTriangles(partIndices, 0, true); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    result.Add(mesh);
                }
                return result;
            }

            private readonly struct PartRange
            {
                public readonly int vertexStart, vertexCount, indexStart, indexCount;
                public PartRange(int vertexStart, int vertexCount, int indexStart, int indexCount)
                { this.vertexStart = vertexStart; this.vertexCount = vertexCount; this.indexStart = indexStart; this.indexCount = indexCount; }
            }
        }
    }
}
