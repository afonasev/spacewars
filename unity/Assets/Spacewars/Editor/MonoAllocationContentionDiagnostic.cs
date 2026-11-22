using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Spacewars.Editor
{
    // Standalone Editor diagnostic: no game, routes, AI, profiler or GC policy changes.
    public static class MonoAllocationContentionDiagnostic
    {
        struct Row
        {
            public int Mode, Size, Count, GcBefore, GcAfter;
            public long Start, End;
            public double CpuMs, MaxAllocationMs;
        }
        static readonly int[] Sizes = { 256, 1024, 8192, 32768 };
        static readonly Row[] WorkerRows = new Row[12000], MainRows = new Row[12000];
        static readonly Stopwatch Clock = new Stopwatch();
        static Thread worker;
        static volatile int mode = -1;
        static volatile bool stop;
        static object workerSink, mainSink;
        static int workerCount, mainCount, mainSequence;
        static long prior;
        static double maxFrame;
        static double nextMain;
        [DllImport("/usr/lib/libSystem.B.dylib")] static extern ulong clock_gettime_nsec_np(int clock);
        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_dyld_image_count")] static extern uint ImageCount();
        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_dyld_get_image_name")] static extern IntPtr ImageName(uint index);
        const double SecondsPerMode = 15;

        public static void DescribeRuntime()
        {
            try {
                var type = typeof(object).Assembly.GetType("Mono.Runtime");
                string display = type?.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null)?.ToString();
                var images = new List<string>();
                for (uint i = 0, n = ImageCount(); i < n; i++) {
                    string name = Marshal.PtrToStringAnsi(ImageName(i));
                    if (name != null && name.IndexOf("mono", StringComparison.OrdinalIgnoreCase) >= 0) images.Add(name);
                }
                string dir = Environment.GetEnvironmentVariable("SPACEWARS_MONO_OUTPUT");
                if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("SPACEWARS_MONO_OUTPUT required");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "runtime-identity.json"), JsonConvert.SerializeObject(new {
                    unity = Application.unityVersion, display, images,
                    mode = UnityEngine.Scripting.GarbageCollector.GCMode.ToString(),
                    incremental = UnityEngine.Scripting.GarbageCollector.isIncremental,
                    slice = UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds
                }, Formatting.Indented));
                string expected = Environment.GetEnvironmentVariable("SPACEWARS_EXPECT_MONO_TAG");
                if (!string.IsNullOrEmpty(expected) && (display == null || !display.Contains(expected)))
                    throw new InvalidOperationException("Expected owned Mono runtime was not loaded: " + display);
                EditorApplication.Exit(0);
            } catch (Exception e) { UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void Run()
        {
            try
            {
                string dir = Environment.GetEnvironmentVariable("SPACEWARS_MONO_OUTPUT");
                if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("SPACEWARS_MONO_OUTPUT required");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "ready.txt"), Process.GetCurrentProcess().Id.ToString());
                worker = new Thread(Work) { IsBackground = true, Name = "Mono.Allocation.Reproducer" };
                EditorApplication.update += Update;
            }
            catch (Exception e) { UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void Work()
        {
            try
            {
                int sequence = 0;
                while (!stop)
                {
                    int current = mode;
                    if (current == 0 || current == 1)
                    {
                        int at = workerCount;
                        if (at >= WorkerRows.Length) throw new InvalidOperationException("worker rows exhausted");
                        WorkerRows[at] = Burst(current, sequence++, ref workerSink);
                        Volatile.Write(ref workerCount, at + 1);
                    }
                    Thread.Sleep(33);
                }
            }
            catch (Exception e) { workerSink = e; stop = true; }
        }

        static Row Burst(int current, int sequence, ref object sink)
        {
            int size = Sizes[sequence % Sizes.Length];
            var row = new Row { Mode = current, Size = size, Count = 128, GcBefore = GC.CollectionCount(0) };
            row.Start = Stopwatch.GetTimestamp();
            ulong cpu = clock_gettime_nsec_np(16);
            for (int i = 0; i < row.Count; i++)
            {
                long before = Stopwatch.GetTimestamp();
                var bytes = new byte[size];
                row.MaxAllocationMs = Math.Max(row.MaxAllocationMs, (Stopwatch.GetTimestamp() - before) * 1000d / Stopwatch.Frequency);
                bytes[0] = (byte)i;
                Volatile.Write(ref sink, bytes);
            }
            row.End = Stopwatch.GetTimestamp();
            row.CpuMs = (clock_gettime_nsec_np(16) - cpu) / 1e6;
            row.GcAfter = GC.CollectionCount(0);
            return row;
        }

        static void Update()
        {
            try
            {
                long now = Stopwatch.GetTimestamp();
                if (!Clock.IsRunning) { prior = now; Clock.Start(); worker.Start(); }
                maxFrame = Math.Max(maxFrame, (now - prior) * 1000d / Stopwatch.Frequency); prior = now;
                double elapsed = Clock.Elapsed.TotalSeconds;
                int current = (int)(elapsed / SecondsPerMode);
                mode = current;
                if (stop || current >= 3) { Finish(); return; }
                if ((current == 1 || current == 2) && elapsed >= nextMain)
                {
                    nextMain = elapsed + 1d / 60;
                    if (mainCount >= MainRows.Length) throw new InvalidOperationException("main rows exhausted");
                    MainRows[mainCount++] = Burst(current, mainSequence++, ref mainSink);
                }
            }
            catch (Exception e) { UnityEngine.Debug.LogException(e); Finish(1); }
        }

        static void Finish(int exit = 0)
        {
            EditorApplication.update -= Update;
            stop = true;
            if (!worker.Join(10000)) exit = 1;
            if (workerSink is Exception e) { UnityEngine.Debug.LogException(e); exit = 1; }
            string dir = Environment.GetEnvironmentVariable("SPACEWARS_MONO_OUTPUT");
            File.WriteAllText(Path.Combine(dir, "allocation-contention.json"), JsonConvert.SerializeObject(new {
                unity = Application.unityVersion, runtime = Environment.Version.ToString(),
                frequency = Stopwatch.Frequency, secondsPerMode = SecondsPerMode,
                modes = new[] { "worker-only", "concurrent-main-worker", "main-only" },
                workerCount, mainCount, maxFrame, heap = GC.GetTotalMemory(false),
                worker = new ArraySegment<Row>(WorkerRows, 0, workerCount),
                main = new ArraySegment<Row>(MainRows, 0, mainCount), exit
            }, Formatting.Indented));
            EditorApplication.Exit(exit);
        }
    }
}
