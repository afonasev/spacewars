using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private sealed class GroundPainting
        {
            internal TerritorySite Site;
            internal Vector2 Right;
            internal Mesh Mesh;
            internal GameObject Node;
        }

        private readonly List<GroundPainting> relayGround = new List<GroundPainting>();
        private Material relayGroundMaterial;
        private Texture2D relayGroundTexture;

        private static string GroundPaintingName(IPlayableTerrain map)
        {
            switch(map)
            {
                case MossHighlandMap _: return "Highland";
                case WhiteMeridianMap _: return "Meridian";
                case FoundryMap _: return "Foundry";
                case AntennaCityMap _: return "City";
                case CrimsonGroveMap _: return "Basin";
                case DeafMirrorMap _: return "Observatory";
                case AiTestMap _: return "Test";
                case ThreeCrossingsMap _: return "Crossings";
                default: return null;
            }
        }

        private void PrepareRelayGround()
        {
            ClearRelayGround();
            var map = profile.AuthoredMap;
            if (map is ThreeCrossingsMap crossings && surfaceMaterials != null)
            {
                var road = surfaceMaterials.Profile;
                material.SetVector("_CrossingsRelayRoad", new Vector4((float)crossings.PocketX,
                    (float)profile.RelayCaptureRadius, road.roadFeather, road.roadWidth));
                material.SetVector("_CrossingsRelayRuts", new Vector4(road.truckWheelSpacing, road.truckTrackWidth, 0, 0));
            }
            string style = GroundPaintingName(map);
            if (style == null) return;
            relayGroundTexture = Resources.Load<Texture2D>("RelayPaving/" + style);
            relayGroundMaterial = new Material(Resources.Load<Shader>("RelayPavingSurface"));
            relayGroundMaterial.SetTexture("_MainTex", relayGroundTexture);
            relayGroundMaterial.SetTexture("_FogMask", material.GetTexture("_FogMask"));
            relayGroundMaterial.SetColor("_FogColor", material.GetColor("_FogColor"));
            relayGroundMaterial.SetVector("_FogBounds", material.GetVector("_FogBounds"));
            foreach (var site in map.Sites(profile))
            {
                if (site.Kind != PlayableBuildingKind.RelayTower) continue;
                double heading = map is AntennaCityMap city ? city.SiteHeading(site.Id) : System.Math.Atan2(site.Position.X, site.Position.Z);
                // Align the radius marks with the mirrored bank roads.
                if (map is ThreeCrossingsMap) heading = site.Position.X > 0 ? 0 : System.Math.PI;
                var right = new Vector2((float)System.Math.Cos(heading), (float)System.Math.Sin(heading));
                var node = new GameObject("Relay ground " + site.Id);
                node.transform.SetParent(root, false);
                node.layer = presentationLayer;
                var mesh = BuildRelayGroundMesh(site, right);
                node.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = node.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.sharedMaterial = relayGroundMaterial;
                relayGround.Add(new GroundPainting { Site = site, Right = right, Node = node, Mesh = mesh });
            }
        }

        private Mesh BuildRelayGroundMesh(TerritorySite site, Vector2 right)
        {
            // The square projects only a local patch. Sample the same terrain heights as the actors.
            const int cells = 24;
            const int stride = cells + 1;
            float width = 4 * (float)profile.RelayCaptureRadius;
            var forward = new Vector2(-right.y, right.x);
            var vertices = new Vector3[stride * stride];
            var uv = new Vector2[vertices.Length];
            var supported = new bool[vertices.Length];
            var indices = new List<int>(6*cells*cells);
            for (int row=0; row<stride; row++)
                for (int col=0; col<stride; col++)
                {
                    int i = row*stride + col;
                    uv[i] = new Vector2((float)col/cells, (float)row/cells);
                    var offset = width*((uv[i].x-.5f)*right + (uv[i].y-.5f)*forward);
                    var point = new NavPoint(site.Position.X+offset.x, site.Position.Z+offset.y);
                    vertices[i] = Point(point) + Vector3.up*.065f;
                    supported[i] = profile.AuthoredMap.SupportAt(point) != null;
                    if (col==0 || row==0) continue;
                    int a=i-stride-1, b=i-1, c=i-stride;
                    if (supported[a] && supported[b] && supported[c]) { indices.Add(a); indices.Add(b); indices.Add(c); }
                    if (supported[b] && supported[i] && supported[c]) { indices.Add(b); indices.Add(i); indices.Add(c); }
                }
            var mesh = new Mesh { name = "Relay paving on supported ground", vertices = vertices, uv = uv };
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        private bool OnRelayGround(NavPoint point)
        {
            float width = 4*(float)profile.RelayCaptureRadius;
            foreach (var painting in relayGround)
            {
                var offset = new Vector2((float)(point.X-painting.Site.Position.X), (float)(point.Z-painting.Site.Position.Z));
                // Painted marks on the Crossings dirt road take tracks. Only the tower foot is hard.
                if (profile.AuthoredMap is ThreeCrossingsMap)
                {
                    if (offset.sqrMagnitude <= profile.RelayFootprintRadius * profile.RelayFootprintRadius) return true;
                    continue;
                }
                float u = .5f + Vector2.Dot(offset, painting.Right)/width;
                float v = .5f + (painting.Right.x*offset.y - painting.Right.y*offset.x)/width;
                if (u>0 && u<1 && v>0 && v<1 && relayGroundTexture.GetPixelBilinear(u,v).a >= .5f) return true;
            }
            return false;
        }

        private void ClearRelayGround()
        {
            foreach (var painting in relayGround)
            {
                painting.Node.SetActive(false);
                ReleaseWorldObject(painting.Node);
                ReleaseWorldObject(painting.Mesh);
            }
            relayGround.Clear();
            if (relayGroundMaterial) ReleaseWorldObject(relayGroundMaterial);
            relayGroundMaterial = null;
            relayGroundTexture = null;
        }
    }
}
