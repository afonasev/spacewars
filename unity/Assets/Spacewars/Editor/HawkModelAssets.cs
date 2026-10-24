using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Spacewars.Editor
{
    // Standalone artwork only. No entity/production/AI registration or gameplay colliders.
    public static class HawkModelAssets
    {
        const string Source = "Assets/Spacewars/Content/StyleA/hawk.glb";
        const string Output = "Assets/Spacewars/Content/Resources/StyleA/";
        static readonly string[] Nodes = { "modelRoot", "mainRotor", "tailRotor", "muzzleLeft", "muzzleRight" };

        [MenuItem("Spacewars/Assets/Prepare Hawk Model")]
        public static void Prepare()
        {
            AssetDatabase.ImportAsset(Source, ImportAssetOptions.ForceSynchronousImport);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            if (!source) throw new InvalidOperationException("Hawk GLB import failed");
            var model = UnityEngine.Object.Instantiate(source);
            model.name = "hawk";
            try
            {
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        string role = materials[i].name;
                        string path = Output + "hawk-" + role + ".mat";
                        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (!material)
                        {
                            var template = AssetDatabase.LoadAssetAtPath<Material>(Output + "tank-" + role + ".mat");
                            material = template ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                            material.name = role;
                            var color = role switch
                            {
                                "team-primary" => new Color(.32f, .70f, .80f),
                                "structure" => new Color(.10f, .13f, .15f),
                                "neutral-metal" => new Color(.42f, .46f, .49f),
                                "canopy" => new Color(.035f, .07f, .10f),
                                "warning" => new Color(.63f, .43f, .17f),
                                "system-emissive" => new Color(.1f, .75f, .9f),
                                _ => throw new InvalidOperationException("Unknown Hawk material role " + role)
                            };
                            material.SetColor("_BaseColor", color);
                            if (!template)
                            {
                                material.SetFloat("_Metallic", .55f);
                                material.SetFloat("_Smoothness", role == "canopy" ? .8f : .42f);
                            }
                            if (role == "system-emissive")
                            {
                                material.EnableKeyword("_EMISSION");
                                material.SetColor("_EmissionColor", color * 2);
                            }
                            AssetDatabase.CreateAsset(material, path);
                        }
                        materials[i] = material;
                    }
                    renderer.sharedMaterials = materials;
                }
                var transforms = model.GetComponentsInChildren<Transform>(true);
                foreach (var name in Nodes)
                    if (transforms.Count(t => t.name == name) != 1)
                        throw new InvalidOperationException("Missing/duplicate articulated node: " + name);
                if (model.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("Standalone visual asset must not introduce gameplay colliders");
                var meshes = model.GetComponentsInChildren<MeshFilter>(true);
                if (meshes.Length == 0) throw new InvalidOperationException("Empty Hawk mesh");
                foreach (var filter in meshes)
                {
                    var bounds = filter.sharedMesh.bounds;
                    if (float.IsNaN(bounds.size.sqrMagnitude) || float.IsInfinity(bounds.size.sqrMagnitude) || bounds.size.sqrMagnitude <= 0)
                        throw new InvalidOperationException("Invalid mesh bounds: " + filter.name);
                }
                var renderers = model.GetComponentsInChildren<Renderer>();
                var envelope = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) envelope.Encapsulate(renderer.bounds);
                if (envelope.size.x < 3 || envelope.size.x > 6 || envelope.size.y < 1 || envelope.size.y > 3)
                    throw new InvalidOperationException("Unexpected model dimensions: " + envelope.size);
                var left = transforms.Single(t => t.name == "muzzleLeft");
                var right = transforms.Single(t => t.name == "muzzleRight");
                if (Mathf.Abs(left.position.x - right.position.x) < 1 || left.position.z < .5f || right.position.z < .5f)
                    throw new InvalidOperationException("Rocket sockets must be separated and face +Z");
                PrefabUtility.SaveAsPrefabAsset(model, Output + "hawk.prefab");
                AssetDatabase.SaveAssets();
                var evidence = Path.GetFullPath("../design/hawk/previews");
                Directory.CreateDirectory(evidence);
                File.WriteAllText(Path.Combine(evidence, "unity-import.json"),
                    JsonUtility.ToJson(new ImportReport {
                        unity = Application.unityVersion, meshes = meshes.Length,
                        triangles = meshes.Sum(m => m.sharedMesh.triangles.Length / 3),
                        dimensions = envelope.size, nodes = Nodes, result = "PASS"
                    }, true));
                Debug.Log("HAWK_IMPORT_PASS " + envelope.size);
            }
            finally { UnityEngine.Object.DestroyImmediate(model); }
            RenderPreviews();
        }

        [Serializable] sealed class ImportReport
        {
            public string unity, result;
            public int meshes, triangles;
            public Vector3 dimensions;
            public string[] nodes;
        }

        static void RenderPreviews()
        {
            // Isolated Editor asset inspection, not an ordinary match or Player acceptance.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Output + "hawk.prefab"));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.position = new Vector3(0, -.03f, 0);
            floor.transform.localScale = Vector3.one * 10;
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.SetColor("_BaseColor", new Color(.22f, .25f, .27f));
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.55f, .58f, .62f);
            var light = new GameObject("Preview key").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 2.1f;
            light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(48, -35, 0);
            var fill = new GameObject("Preview fill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = .6f;
            fill.transform.rotation = Quaternion.Euler(30, 130, 0);
            var camera = new GameObject("Asset preview camera").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 2.8f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.16f, .18f, .20f);
            camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            string[] names = { "front", "rear", "top", "game" };
            var positions = new[] { new Vector3(6, 5, 8), new Vector3(-6, 4, -8), new Vector3(0, 10, .001f), new Vector3(5, 10, 7) };
            var texture = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = texture;
            try
            {
                for (int i = 0; i < names.Length; i++)
                {
                    camera.transform.position = positions[i];
                    camera.transform.LookAt(new Vector3(0, .75f, 0));
                    camera.Render();
                    var old = RenderTexture.active; RenderTexture.active = texture;
                    var image = new Texture2D(1280, 960, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0); image.Apply();
                    File.WriteAllBytes(Path.GetFullPath("../design/hawk/previews/unity-" + names[i] + ".png"), image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = old;
                }
            }
            finally
            {
                camera.targetTexture = null; texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(floorMaterial);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            Debug.Log("HAWK_EDITOR_PREVIEWS_PASS");
        }
    }
}
