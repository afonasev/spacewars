using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Spacewars.StyleA.Preview
{
    public sealed class StyleALodAudit : MonoBehaviour
    {
        // Fixed experimental fixture, never consumed by gameplay or Balance Lab.
        public const int Copies = 4, WarmupFrames = 120, SampleFrames = 300;
        public GameObject[] prefabs;
        public Camera auditCamera;
        private string output;
        private readonly List<ProfilerRecorder> recorders = new List<ProfilerRecorder>();
        private static readonly string[] CounterNames = { "Draw Calls Count", "Batches Count", "SetPass Calls Count", "Triangles Count", "Vertices Count", "Main Thread" };
        private readonly FrameTiming[] timing = new FrameTiming[1];

        [Serializable] private sealed class Host
        {
            public string classification = "DIAGNOSTIC_NOT_ACCEPTANCE", unity, os, cpu, gpu, graphicsApi, utc, pipeline;
            public string seed = "none", profile = "style-a-lod-audit-fixture-v1; authored scale; no gameplay profile";
            public int ramMb, vramMb, width, height, copies, assetKinds, warmupFrames, sampleFrames, quality, antiAliasing;
            public int vSync, targetFrameRate;
            public bool frameTimingEnabled, focused;
            public Vector3 cameraPosition, cameraEuler;
            public float orthographicSize, nearClip, farClip;
            public string counterMissingValue = "-1 means unavailable; GPU 0/duplicate timestamps are excluded";
        }
        [Serializable] private sealed class Case
        {
            public string name;
            public int instanceCount, rendererCount, staticBatchRenderers;
            public long allocatedBytes, meshRuntimeBytes;
            public bool focusedAtStart, focusedAtEnd;
            public int unfocusedSamples;
        }

        private IEnumerator Start()
        {
            AudioListener.volume = 0;
            Application.runInBackground = true;
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++) if (args[i] == "-styleALodEvidence") output = args[i + 1];
            if (string.IsNullOrEmpty(output)) { Debug.LogError("-styleALodEvidence is required"); Application.Quit(2); yield break; }
            Directory.CreateDirectory(output);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            File.WriteAllText(Path.Combine(output, "inventory.json"), JsonUtility.ToJson(StyleALodInventory.Inspect(prefabs), true));
            File.WriteAllText(Path.Combine(output, "host.json"), JsonUtility.ToJson(new Host
            {
                unity = Application.unityVersion, os = SystemInfo.operatingSystem, cpu = SystemInfo.processorType,
                gpu = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                utc = DateTime.UtcNow.ToString("O"), ramMb = SystemInfo.systemMemorySize, vramMb = SystemInfo.graphicsMemorySize,
                width = Screen.width, height = Screen.height, copies = Copies, assetKinds = prefabs.Length,
                warmupFrames = WarmupFrames, sampleFrames = SampleFrames, quality = QualitySettings.GetQualityLevel(),
                antiAliasing = QualitySettings.antiAliasing, pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name,
                vSync = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate,
                frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(), focused = Application.isFocused,
                cameraPosition = auditCamera.transform.position, cameraEuler = auditCamera.transform.eulerAngles,
                orthographicSize = auditCamera.orthographicSize, nearClip = auditCamera.nearClipPlane, farClip = auditCamera.farClipPlane
            }, true));
            for (var i = 0; i < CounterNames.Length; i++)
                recorders.Add(ProfilerRecorder.StartNew(i == 5 ? ProfilerCategory.Internal : ProfilerCategory.Render, CounterNames[i], 1));
            foreach (var mode in new[] { "raw-before", "static-batched", "raw-after" })
            {
                var root = CreateGrid();
                if (mode == "static-batched") StaticBatchingUtility.Combine(root);
                for (var frame = 0; frame < WarmupFrames; frame++) { FrameTimingManager.CaptureFrameTimings(); yield return null; }
                var state = new Case { name = mode, instanceCount = Copies * prefabs.Length,
                    rendererCount = root.GetComponentsInChildren<MeshRenderer>().Length,
                    staticBatchRenderers = root.GetComponentsInChildren<MeshRenderer>().Count(r => r.isPartOfStaticBatch),
                    allocatedBytes = Profiler.GetTotalAllocatedMemoryLong(), focusedAtStart = Application.isFocused,
                    meshRuntimeBytes = root.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh).Distinct().Sum(m => Profiler.GetRuntimeMemorySizeLong(m)) };
                // Preallocate sample storage; no per-frame CSV formatting or file writes in measurement.
                var wall = new double[SampleFrames];
                var cpu = new double[SampleFrames]; var gpu = new double[SampleFrames];
                var stamps = new ulong[SampleFrames];
                var counters = new long[SampleFrames, CounterNames.Length];
                for (var frame = 0; frame < SampleFrames; frame++)
                {
                    FrameTimingManager.CaptureFrameTimings();
                    yield return null;
                    wall[frame] = Time.unscaledDeltaTime * 1000.0;
                    if (!Application.isFocused) state.unfocusedSamples++;
                    for (var c = 0; c < CounterNames.Length; c++)
                        counters[frame, c] = recorders[c].Valid && recorders[c].Count > 0 ? recorders[c].LastValue : -1;
                    var available = FrameTimingManager.GetLatestTimings(1, timing) > 0;
                    stamps[frame] = available ? timing[0].frameStartTimestamp : 0;
                    cpu[frame] = available && timing[0].cpuFrameTime > 0 ? timing[0].cpuFrameTime : -1;
                    gpu[frame] = available && timing[0].gpuFrameTime > 0 ? timing[0].gpuFrameTime : -1;
                }
                state.focusedAtEnd = Application.isFocused;
                var csv = new StringBuilder("sample,wallMs,cpuFrameMs,gpuFrameMs,timingTimestamp,drawCalls,batches,setPass,triangles,vertices,mainThreadNs\n");
                for (var f = 0; f < SampleFrames; f++)
                {
                    csv.Append(f).Append(',').Append(wall[f].ToString("R", CultureInfo.InvariantCulture)).Append(',')
                        .Append(cpu[f].ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(gpu[f].ToString("R", CultureInfo.InvariantCulture))
                        .Append(',').Append(stamps[f]);
                    for (var c = 0; c < CounterNames.Length; c++) csv.Append(',').Append(counters[f, c]);
                    csv.AppendLine();
                }
                File.WriteAllText(Path.Combine(output, mode + ".csv"), csv.ToString());
                File.WriteAllText(Path.Combine(output, mode + ".json"), JsonUtility.ToJson(state, true));
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(output, "style-a-lod-" + mode + ".png"));
                yield return new WaitForSeconds(1);
                Destroy(root);
                yield return null;
                yield return Resources.UnloadUnusedAssets();
                GC.Collect();
                Debug.Log("STYLE_A_LOD_CASE_COMPLETE " + mode);
            }
            Debug.Log("STYLE_A_LOD_AUDIT_COMPLETE");
            Application.Quit();
        }

        private GameObject CreateGrid()
        {
            var root = new GameObject("Frozen diagnostic assets");
            for (var row = 0; row < Copies; row++)
                for (var col = 0; col < prefabs.Length; col++)
                {
                    var instance = Instantiate(prefabs[col], root.transform);
                    instance.transform.position = new Vector3((col - 4) * 4f, 0, (row - 1.5f) * 5f);
                    instance.transform.rotation = Quaternion.Euler(0, 18, 0);
                }
            return root;
        }
        private void OnDestroy() { foreach (var recorder in recorders) recorder.Dispose(); }
    }
}
