using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Spacewars.Editor
{
    // Active Spearhead import. Existing tank/tankUpgraded remain legacy comparison assets.
    public static class TankHybridAssets
    {
        const string Output = "Assets/Spacewars/Content/Resources/StyleA/";
        static string Evidence => Path.GetFullPath("../.local/tank-model-maneuvers");

        [MenuItem("Spacewars/Assets/Prepare Hybrid Tank Candidate")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Evidence);
            foreach (var key in new[] { "tankHybrid", "tankHybridUpgraded" }) Import(key);
            UnitModelResources.ReuseExactVariantMeshes();
            AssetDatabase.SaveAssets();
            Render();
            Debug.Log("TANK_HYBRID_IMPORT_PASS");
        }

        static void Import(string key)
        {
            string sourcePath = "Assets/Spacewars/Content/StyleA/" + key + ".glb";
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (!source) throw new InvalidOperationException("Missing GLB " + sourcePath);
            var model = UnityEngine.Object.Instantiate(source);
            model.name = key;
            try
            {
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = Material(materials[i].name);
                    renderer.sharedMaterials = materials;
                }
                var nodes = model.GetComponentsInChildren<Transform>(true);
                foreach (var name in new[] { "modelRoot", "turretYaw", "barrelRecoil", "muzzle", "trackL", "trackR" })
                    if (nodes.Count(t => t.name == name) != 1) throw new InvalidOperationException("Articulation node " + name);
                if (model.GetComponentsInChildren<Collider>(true).Length != 0) throw new InvalidOperationException("Visual candidate acquired colliders");
                foreach (var side in new[] { "L", "R" })
                {
                    if (nodes.Count(t => t && t.parent && t.parent.name == "track" + side && t.name.StartsWith("tread" + side, StringComparison.Ordinal)) != 36 ||
                        nodes.Count(t => t && t.parent && t.parent.name == "track" + side && t.name.StartsWith("wheel" + side, StringComparison.Ordinal)) != 6)
                        throw new InvalidOperationException("Track articulation count " + side);
                    PackTrack(nodes.Single(t => t && t.name == "track" + side), key + "-track" + side);
                }
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                var envelope = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) envelope.Encapsulate(renderer.bounds);
                if (envelope.size.x < 2 || envelope.size.x > 2.3f || envelope.size.y < 1 || envelope.size.y > 1.4f || envelope.size.z > 3.5f)
                    throw new InvalidOperationException("Unexpected candidate envelope " + envelope);
                // Bounds and nodes are art checks, not runtime/gameplay acceptance.
                PrefabUtility.SaveAsPrefabAsset(model, Output + key + ".prefab");
                File.WriteAllText(Path.Combine(Evidence, key + "-import.json"), JsonUtility.ToJson(new Report
                {
                    asset = key, unity = Application.unityVersion, bounds = envelope.size,
                    renderers = renderers.Length, trackBones = 84, result = "PASS"
                }, true));
            }
            finally { UnityEngine.Object.DestroyImmediate(model); }
        }

        static Material Material(string role)
        {
            string path = Output + "tankHybrid-" + role + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.name = "tankHybrid-" + role;
            var color = role switch
            {
                "team-primary" => new Color(.12f, .30f, .46f),
                "structure" => new Color(.085f, .105f, .12f),
                "neutral-metal" => new Color(.27f, .31f, .35f),
                "system-emissive" or "chassis-emissive" => new Color(.08f, .72f, .9f),
                _ => throw new InvalidOperationException("Unexpected tank role " + role)
            };
            m.SetColor("_BaseColor", color);
            // Match the established fleet coating. Owner paint is applied later by
            // ActorMaterialProfile, so darkening _BaseColor alone would be lost.
            if (role == "team-primary" || role == "neutral-metal")
            {
                var template = AssetDatabase.LoadAssetAtPath<Material>(Output + "explorer-" + role + ".mat");
                if (!template || !template.GetTexture("_BaseMap"))
                    throw new InvalidOperationException("Missing fleet coating for " + role);
                m.SetTexture("_BaseMap", template.GetTexture("_BaseMap"));
                if (role == "neutral-metal") m.SetColor("_BaseColor", template.GetColor("_BaseColor"));
            }
            m.SetFloat("_Metallic", role == "neutral-metal" ? .72f : role == "structure" ? .15f : .35f);
            m.SetFloat("_Smoothness", role == "structure" ? .32f : .48f);
            if (role.EndsWith("emissive", StringComparison.Ordinal))
            { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * 1.2f); }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // Material-only repair; leaves prefab meshes, track packing and articulation intact.
        public static void RefreshFinish()
        {
            foreach (var role in new[] { "team-primary", "neutral-metal" }) Material(role);
            AssetDatabase.SaveAssets();
            Render();
            Debug.Log("TANK_FLEET_FINISH_PASS");
        }

        // One rigid-skin draw per material/side replaces dozens of separate tread renderers.
        // Bones preserve the authored links and wheels for differential track animation.
        internal static void PackTrack(Transform root, string key, bool persist=true)
        {
            var bones = root.Cast<Transform>().ToArray();
            var filters = root.GetComponentsInChildren<MeshFilter>();
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var weights = new List<BoneWeight>();
            var materials = new List<Material>(); var triangles = new List<List<int>>();
            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh; var rs = filter.GetComponent<Renderer>().sharedMaterials;
                var matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var normalMatrix = matrix.inverse.transpose;
                int offset = vertices.Count, bone = Array.IndexOf(bones, filter.transform.parent);
                if (bone < 0) throw new InvalidOperationException("Unowned track mesh " + filter.name);
                var vs = mesh.vertices; var ns = mesh.normals;
                for (int i = 0; i < vs.Length; i++)
                { vertices.Add(matrix.MultiplyPoint3x4(vs[i])); normals.Add(normalMatrix.MultiplyVector(ns[i]).normalized); weights.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1 }); }
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int material = materials.IndexOf(rs[sub]);
                    if (material < 0) { material = materials.Count; materials.Add(rs[sub]); triangles.Add(new List<int>()); }
                    triangles[material].AddRange(mesh.GetTriangles(sub).Select(t => t + offset));
                }
            }
            string path = Output + key + ".asset";
            var combined = persist?AssetDatabase.LoadAssetAtPath<Mesh>(path):null;
            bool created = !combined;
            if (created) combined = new Mesh { name = key };
            // Rebuild the native vertex/index buffers together. CopySerialized can retain
            // an existing Mesh vertex stream while replacing its submesh index ranges.
            combined.Clear(false);
            combined.indexFormat = IndexFormat.UInt32;
            combined.SetVertices(vertices); combined.SetNormals(normals); combined.boneWeights = weights.ToArray();
            combined.bindposes = bones.Select(b => b.worldToLocalMatrix * root.localToWorldMatrix).ToArray();
            combined.subMeshCount = triangles.Count;
            for (int i = 0; i < triangles.Count; i++) combined.SetTriangles(triangles[i], i);
            combined.RecalculateBounds();
            if (combined.vertexCount != vertices.Count || combined.boneWeights.Length != vertices.Count ||
                combined.triangles.Any(index => index < 0 || index >= combined.vertexCount))
                throw new InvalidOperationException("Invalid packed track buffers " + key);
            if (persist){if (created) AssetDatabase.CreateAsset(combined, path);
            else EditorUtility.SetDirty(combined);}
            foreach (var filter in filters) UnityEngine.Object.DestroyImmediate(filter.gameObject);
            var renderer = root.gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = combined; renderer.sharedMaterials = materials.ToArray(); renderer.bones = bones;
            renderer.rootBone = root; renderer.localBounds = combined.bounds; renderer.quality = SkinQuality.Bone1;
            renderer.updateWhenOffscreen = false;
        }

        public static void RenderEngineDetail()
        {
            Directory.CreateDirectory(Evidence);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.gray;
            var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Output+"tankHybridUpgraded.prefab"));
            foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))renderer.enabled=renderer.name.StartsWith("engine",StringComparison.Ordinal);
            var sun=new GameObject("Key").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.5f;sun.transform.rotation=Quaternion.Euler(30,120,0);
            var camera=new GameObject("Rear engine detail").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=.6f;camera.transform.position=new Vector3(0,.9f,-4);camera.transform.LookAt(new Vector3(0,.785f,-1));camera.backgroundColor=Color.gray;camera.clearFlags=CameraClearFlags.SolidColor;
            var target=new RenderTexture(1200,700,24);camera.targetTexture=target;camera.Render();RenderTexture.active=target;var pixels=new Texture2D(1200,700,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1200,700),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(Evidence,"engine-detail.png"),pixels.EncodeToPNG());
            RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(target);Debug.Log("TANK_ENGINE_DETAIL_RENDERED");
        }

        public static void Render()
        {
            Directory.CreateDirectory(Evidence);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.57f, .60f, .65f);
            var key = new GameObject("Studio key").AddComponent<Light>();
            key.type = LightType.Directional; key.intensity = 1.65f; key.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.Euler(45, -35, 0);
            var fill = new GameObject("Studio fill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = .45f; fill.transform.rotation = Quaternion.Euler(35, 140, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = Vector3.one * 3; floor.transform.position = Vector3.down * .025f;
            var fm = new Material(Shader.Find("Universal Render Pipeline/Lit")); fm.SetColor("_BaseColor", new Color(.27f,.29f,.31f)); fm.SetFloat("_Smoothness",.1f);
            floor.GetComponent<Renderer>().sharedMaterial = fm;
            var camera = new GameObject("Candidate comparison camera").AddComponent<Camera>();
            string requestedSide=Environment.GetEnvironmentVariable("SPACEWARS_TANK_PREVIEW_SIDE");
            camera.orthographic = true; camera.orthographicSize = string.IsNullOrEmpty(requestedSide)?2.3f:1.7f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.27f,.29f,.31f);
            camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = rt;
            try
            {
                foreach (bool upgraded in new[] { false, true })
                {
                    var requestedVariant=Environment.GetEnvironmentVariable("SPACEWARS_TANK_PREVIEW_VARIANT");
                    if(!string.IsNullOrEmpty(requestedVariant)&&requestedVariant!=(upgraded?"upgraded":"basic"))continue;
                    var models = new List<GameObject>();
                    var names = new[] { upgraded ? "tankUpgraded" : "tank", upgraded ? "tankHybridUpgraded" : "tankHybrid" };
                    for (int i = 0; i < 2; i++)
                    {
                        if(!string.IsNullOrEmpty(requestedSide)&&requestedSide!=(i==0?"baseline":"candidate"))continue;
                        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Output + names[i] + ".prefab"));
                        if(i==1) model.transform.localScale *= Spacewars.Presentation.TankMechanismProfile.Load().modelScale;
                        model.transform.position = string.IsNullOrEmpty(requestedSide)?new Vector3((i == 0 ? 1 : -1) * 1.65f, 0, 0):Vector3.zero;
                        foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
                            for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)
                                Spacewars.Presentation.ActorMaterialProfile.Load().Apply(renderer,slot,new Color(.12f,.30f,.46f));
                        models.Add(model);
                    }
                    string[] views = { "front", "rear", "top", "distance", "head-on", "side" };
                    var positions = new[] { new Vector3(4, 4.5f, 7), new Vector3(-4, 4.5f, -7), new Vector3(0, 10, .001f) };
                    for (int i = 0; i < views.Length; i++)
                    {
                        var requestedView=Environment.GetEnvironmentVariable("SPACEWARS_TANK_PREVIEW_VIEW");
                        if(!string.IsNullOrEmpty(requestedView)&&requestedView!=views[i])continue;
                        camera.orthographicSize = i == 3 ? 7f : string.IsNullOrEmpty(requestedSide) ? 2.3f : 1.7f;
                        // Align both models rather than using different camera perspective per side.
                        foreach (var model in models) model.transform.rotation = i == 5 ? Quaternion.Euler(0,90,0) : i == 2 || i == 4 ? Quaternion.identity : Quaternion.Euler(0, i == 0 || i == 3 ? -25 : 155, 0);
                        camera.transform.position = i == 2 ? positions[i] : new Vector3(0, i == 5 ? .8f : i == 1 || i == 4 ? 1.8f : 4.5f, 8);
                        camera.transform.LookAt(new Vector3(0,.5f,0));
                        camera.Render();
                        var previous = RenderTexture.active; RenderTexture.active = rt;
                        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                        image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply();
                        File.WriteAllBytes(Path.Combine(Evidence, (upgraded ? "upgraded-" : "basic-") + views[i] + (string.IsNullOrEmpty(requestedSide)?"":"-"+requestedSide) + ".png"), image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = previous;
                    }
                    foreach (var model in models) UnityEngine.Object.DestroyImmediate(model);
                }
            }
            finally
            {
                camera.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(fm);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            Debug.Log("TANK_HYBRID_EDITOR_RENDERS_PASS");
        }

        [Serializable] sealed class Report
        { public string asset, unity, result; public Vector3 bounds; public int renderers, trackBones; }
    }
}
