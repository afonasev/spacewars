using System;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Spacewars.Presentation
{
    internal static class PlayableMapOverheadCapture
    {
        static readonly int FogMask = Shader.PropertyToID("_FogMask");

        // Runs on the loading or authoring path with a newly prepared static world.
        public static byte[] Capture(PlayableWorld world, PlayableProfile profile, int size)
        {
            if (world.Actors.Count != 0)
                throw new InvalidOperationException("The map background must be captured before spawning actors.");

            var root = world.GraphicsRoot;
            var hidden = new List<Renderer>();
            var lights = new List<Light>();
            var masks = new Dictionary<Material, Texture>();
            var ambient = RenderSettings.ambientMode;
            var ambientLight = RenderSettings.ambientLight;
            var previousSun = RenderSettings.sun;
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float previousShadowDistance = pipeline ? pipeline.shadowDistance : 0;
            // URP can update these legacy settings during a camera render in EditMode.
            bool previousLinearLights = GraphicsSettings.lightsUseLinearIntensity;
            bool previousColorTemperature = GraphicsSettings.lightsUseColorTemperature;
            int previousAntialiasing = QualitySettings.antiAliasing;
            var previousTarget = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            var rig = new GameObject("Static overhead map capture") { hideFlags = HideFlags.HideAndDontSave };
            var target = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D image = null;
            try
            {
                foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (!renderer.transform.IsChildOf(root))
                    {
                        if (!renderer.forceRenderingOff)
                        {
                            renderer.forceRenderingOff = true;
                            hidden.Add(renderer);
                        }
                        continue;
                    }
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (!material || !material.HasProperty(FogMask) || masks.ContainsKey(material)) continue;
                        var original = material.GetTexture(FogMask);
                        if (original != world.Fog.Texture) continue;
                        masks.Add(material, original);
                        material.SetTexture(FogMask, Texture2D.blackTexture);
                    }
                }
                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (!light.enabled) continue;
                    lights.Add(light);
                    light.enabled = false;
                }
                var sun = rig.AddComponent<Light>();
                var lighting = IndustrialLightingProfile.Load(profile.AuthoredMap?.Id);
                lighting.ApplyScene(sun);
                RenderSettings.sun = sun;

                var cameraObject = new GameObject("Orthographic camera");
                cameraObject.transform.SetParent(rig.transform, false);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = (float)profile.ArenaHalfExtent;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 1000;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.025f, .04f, .057f);
                camera.transform.SetPositionAndRotation(Vector3.up * 300, Quaternion.LookRotation(Vector3.down, Vector3.forward));
                cameraObject.AddComponent<VisibleShadowCoverage>().Configure(root, lighting);
                cameraObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());
                cameraObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());
                cameraObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());
                camera.targetTexture = target;
                camera.Render();
                camera.targetTexture = null;

                RenderTexture.active = target;
                image = new Texture2D(size, size, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                return image.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previousTarget;
                RenderTexture.ReleaseTemporary(target);
                foreach (var entry in masks) entry.Key.SetTexture(FogMask, entry.Value);
                foreach (var renderer in hidden) renderer.forceRenderingOff = false;
                foreach (var light in lights) light.enabled = true;
                RenderSettings.sun = previousSun;
                RenderSettings.ambientMode = ambient;
                RenderSettings.ambientLight = ambientLight;
                if (pipeline) pipeline.shadowDistance = previousShadowDistance;
                Release(image);
                rig.SetActive(false);
                Release(rig);
                GraphicsSettings.lightsUseLinearIntensity = previousLinearLights;
                GraphicsSettings.lightsUseColorTemperature = previousColorTemperature;
                QualitySettings.antiAliasing = previousAntialiasing;
                GL.sRGBWrite = previousSrgb;
            }
        }

        private static void Release(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
