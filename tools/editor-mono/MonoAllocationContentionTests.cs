using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    // Diagnostic only: original gameplay performance fixtures remain untouched.
    public sealed class MonoAllocationContentionTests
    {
        struct Row { public int Mode, Size, Gc0, Gc1; public long Start, End; public double Cpu, Single; }
        readonly Row[] workerRows = new Row[8000];
        static readonly int[] Sizes = { 256, 1024, 8192, 32768 };
        volatile int mode;
        volatile bool stopping;
        int count;
        object workerSink, mainSink;
        Exception failure;
        readonly object allocationGate = new object();
        volatile bool serialize;
        long helperBursts;
        static string RuntimeDisplay() => typeof(object).Assembly.GetType("Mono.Runtime")?
            .GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null)?.ToString();
        [Test] public void RuntimeIdentityMatchesRequestedEngine()
        {
            string expected = Environment.GetEnvironmentVariable("SPACEWARS_EXPECT_MONO_TAG");
            Assert.IsNotEmpty(expected);
            string display = RuntimeDisplay(); StringAssert.Contains(expected, display);
            string dir = Environment.GetEnvironmentVariable("SPACEWARS_MONO_OUTPUT"); Assert.IsNotEmpty(dir); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "test-runtime-identity.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
                unity = Application.unityVersion, display, gcMode = UnityEngine.Scripting.GarbageCollector.GCMode.ToString(),
                incremental = UnityEngine.Scripting.GarbageCollector.isIncremental,
                slice = UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds
            }, Newtonsoft.Json.Formatting.Indented));
        }
        [DllImport("/usr/lib/libSystem.B.dylib")] static extern ulong clock_gettime_nsec_np(int clock);
        Row Allocate(int current, int sequence, ref object sink)
        {
            int size = Sizes[sequence % 4];
            var r = new Row { Mode = current, Size = size, Gc0 = GC.CollectionCount(0), Start = Stopwatch.GetTimestamp() };
            ulong cpu = clock_gettime_nsec_np(16);
            bool entered = false;
            if (serialize) { Monitor.Enter(allocationGate); entered = true; }
            try {
            for (int i = 0; i < 128; i++) {
                long at = Stopwatch.GetTimestamp(); var bytes = new byte[size];
                r.Single = Math.Max(r.Single, (Stopwatch.GetTimestamp() - at) * 1000d / Stopwatch.Frequency);
                bytes[0] = (byte)i; Volatile.Write(ref sink, bytes);
            }
            } finally { if (entered) Monitor.Exit(allocationGate); }
            r.End = Stopwatch.GetTimestamp(); r.Cpu = (clock_gettime_nsec_np(16) - cpu) / 1e6; r.Gc1 = GC.CollectionCount(0);
            return r;
        }
        [UnityTest, Timeout(120000)] public IEnumerator BackgroundAllocatorSerialization()
        {
            count = 0; stopping = false; failure = null;
            var thread = new Thread(Work) { IsBackground = true, Name = "Mono.Allocation.Authority" };
            object helperSink = null;
            var helper = new Thread(() => {
                try { int i = 0; while (!stopping) { Allocate(mode, i++, ref helperSink); Interlocked.Increment(ref helperBursts); Thread.Yield(); } }
                catch (Exception e) { failure = e; stopping = true; }
            }) { IsBackground = true, Name = "Mono.Allocation.Review" };
            var clock = Stopwatch.StartNew(); var periods = new List<object>();
            thread.Start(); helper.Start();
            try {
                for (int m = 0; m < 2; m++) {
                    mode = m; serialize = m == 1; double start = clock.Elapsed.TotalSeconds, previous = start;
                    long bursts = Interlocked.Read(ref helperBursts); double maxFrame = 0;
                    while (clock.Elapsed.TotalSeconds - start < 15) {
                        double now = clock.Elapsed.TotalSeconds; maxFrame = Math.Max(maxFrame, (now - previous) * 1000); previous = now;
                        Assert.IsNull(failure); yield return null;
                    }
                    periods.Add(new { mode = m, helperBursts = Interlocked.Read(ref helperBursts) - bursts, maxFrame });
                }
            } finally {
                stopping = true; Assert.True(thread.Join(10000)); Assert.True(helper.Join(10000));
                string output = Environment.GetEnvironmentVariable("SPACEWARS_MONO_OUTPUT"); Assert.IsNotEmpty(output); Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "background-contention.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
                    unity = Application.unityVersion, runtime = RuntimeDisplay(), frequency = Stopwatch.Frequency, target = Application.targetFrameRate, vsync = QualitySettings.vSyncCount,
                    gcMode = UnityEngine.Scripting.GarbageCollector.GCMode.ToString(), periods,
                    worker = new ArraySegment<Row>(workerRows, 0, count), failure = failure?.ToString()
                }, Newtonsoft.Json.Formatting.Indented));
            }
            Assert.IsNull(failure);
        }
        void Work()
        {
            try { int i = 0; while (!stopping) { int at = count;
                if (at >= workerRows.Length) throw new InvalidOperationException("Diagnostic row bound");
                workerRows[at] = Allocate(mode, i++, ref workerSink); Volatile.Write(ref count, at + 1); Thread.Sleep(33);
            } } catch (Exception e) { failure = e; stopping = true; }
        }
        [UnityTest, Timeout(180000)] public IEnumerator FrameIdleBudgetAllocationContention()
        {
            int priorTarget = Application.targetFrameRate, priorVsync = QualitySettings.vSyncCount;
            var rows = new List<Row>(4000); var frames = new List<object>(4000);
            var thread = new Thread(Work) { IsBackground = true, Name = "Mono.Allocation.PlayMode" };
            var clock = Stopwatch.StartNew(); thread.Start();
            try {
                // Same allocations. Isolate Unity's idle-time GC budget from an explicit wait.
                for (int current = 0; current < 3; current++) {
                    mode = current; QualitySettings.vSyncCount = 0; Application.targetFrameRate = current == 0 ? 60 : -1;
                    double start = clock.Elapsed.TotalSeconds, previous = start, nextAllocation = start; int i = 0;
                    while (clock.Elapsed.TotalSeconds - start < 15) {
                        if (current == 2) { double wait = 1d / 60 - (clock.Elapsed.TotalSeconds - previous); if (wait > .001) Thread.Sleep((int)(wait * 1000)); }
                        double now = clock.Elapsed.TotalSeconds;
                        frames.Add(new { mode = current, ms = (now - previous) * 1000 }); previous = now;
                        if (now >= nextAllocation) { nextAllocation = now + 1d / 60; rows.Add(Allocate(current, i++, ref mainSink)); }
                        Assert.IsNull(failure); yield return null;
                    }
                }
            } finally {
                stopping = true; Assert.True(thread.Join(10000)); Application.targetFrameRate = priorTarget; QualitySettings.vSyncCount = priorVsync;
                string output = Environment.GetEnvironmentVariable("SPACEWARS_MONO_OUTPUT"); Assert.IsNotEmpty(output); Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "playmode-contention.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
                    unity = Application.unityVersion, frequency = Stopwatch.Frequency, priorTarget, priorVsync,
                    gcMode = UnityEngine.Scripting.GarbageCollector.GCMode.ToString(), incremental = UnityEngine.Scripting.GarbageCollector.isIncremental,
                    slice = UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds,
                    modes = new[] { "unity-cap-60", "uncapped", "explicit-wait-60" }, count,
                    worker = new ArraySegment<Row>(workerRows, 0, count), main = rows, frames, failure = failure?.ToString()
                }, Newtonsoft.Json.Formatting.Indented));
            }
            Assert.IsNull(failure);
        }
    }
}
