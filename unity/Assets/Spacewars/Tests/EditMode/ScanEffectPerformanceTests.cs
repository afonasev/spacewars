using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using System.Globalization;

namespace Spacewars.Tests.EditMode
{
    internal sealed class ScanPerformanceScene : IDisposable
    {
        internal readonly PlayableDomain Domain;
        internal readonly PlayableProfile Profile=PlayableProfile.Default;
        internal readonly TeamVisionSnapshot[] Views;
        internal static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        readonly MethodInfo lines,faces,advance;
        readonly object[][] args;
        readonly object[] step={1d/30};
        internal ScanPerformanceScene()
        {
            Domain=new PlayableDomain(Profile,1);
            Call("CreditOrbital",(PlayableOwner?)PlayableOwner.Player,PlayableOwner.Enemy,250d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("ChooseOrbital",PlayableOwner.Player,1));
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("UseOrbital",PlayableOwner.Player,1,default(NavPoint)));
            Call("AdvanceOrbital",.75d);
            TeamVisionSnapshot Vision(int team,VisionSource[] sources){var v=new PlayableVision(team,Profile.ArenaHalfExtent,Profile.ArenaHalfExtent,Profile.VisionCellSize,Profile.FogEdgeFeather);v.Refresh(sources,Array.Empty<KnownBuilding>());return v.Snapshot();}
            Views=new[]{Vision(0,new[]{new VisionSource(default,10)}),Vision(1,Enumerable.Range(0,100).Select(i=>new VisionSource(new NavPoint(20+i%10*.6,20+i/10*.6),1)).ToArray()),Vision(2,new[]{new VisionSource(new NavPoint(10,0),6)})};
            lines=typeof(PlayableDomain).GetMethod("ScanLines",F);faces=typeof(PlayableDomain).GetMethod("ScanTriangles",F);advance=typeof(PlayableDomain).GetMethod("AdvanceOrbital",F);args=Views.Select(v=>new object[]{v}).ToArray();
        }
        internal object Call(string name,params object[] values)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(Domain,values);
        internal OrbitalScanLine[] Lines(int view)=>(OrbitalScanLine[])lines.Invoke(Domain,args[view]);
        internal OrbitalScanTriangle[] Faces(int view)=>(OrbitalScanTriangle[])faces.Invoke(Domain,args[view]);
        internal void Advance()=>advance.Invoke(Domain,step);
        internal string Digest()
        {
            using var stream=new MemoryStream();using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
            for(int i=0;i<Views.Length;i++){
                var ls=Lines(i);var fs=Faces(i);w.Write(ls.Length);foreach(var l in ls){w.Write(l.From.X);w.Write(l.From.Z);w.Write(l.To.X);w.Write(l.To.Z);w.Write(l.Opacity);}
                w.Write(fs.Length);foreach(var f in fs){w.Write(f.A.X);w.Write(f.A.Z);w.Write(f.B.X);w.Write(f.B.Z);w.Write(f.C.X);w.Write(f.C.Z);w.Write(f.Opacity);w.Write(f.Highlight);w.Write(f.OpacityB);w.Write(f.HighlightB);w.Write(f.OpacityC);w.Write(f.HighlightC);}
            }
            using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();
        }
        public void Dispose(){Domain.Navigation.ReleaseCommittedHostRoot();Domain.DestroyOwnedNavigation();}
    }
    public sealed class ScanEffectPerformanceTests
    {
        [Serializable] sealed class Report {public string digest;public double fixedMs,advancingMs;public long fixedBytes,advancingBytes,faces;public int iterations=25,views=3;public bool allocationCounterValid;}
        [Test,Timeout(60000)] public void ReportDerivedScanWork()
        {
            using var scene=new ScanPerformanceScene();Assert.Greater(scene.Faces(0).Length,0);Assert.Zero(scene.Faces(1).Length);Assert.Greater(scene.Faces(2).Length,0);
            var report=new Report{digest=scene.Digest()};string output=Environment.GetEnvironmentVariable("SCAN_PERF_OUTPUT");
            // Ordinary suite runs keep the functional assertions; timing requires the explicit exclusive harness.
            if(string.IsNullOrEmpty(output))return;
            long calibration=GC.GetAllocatedBytesForCurrentThread();var probe=new byte[4096];GC.KeepAlive(probe);
            report.allocationCounterValid=GC.GetAllocatedBytesForCurrentThread()>calibration;
            var watch=new Stopwatch();
            for(int phase=0;phase<2;phase++){
                GC.Collect();GC.WaitForPendingFinalizers();long bytes=GC.GetAllocatedBytesForCurrentThread();watch.Restart();
                for(int i=0;i<report.iterations;i++){
                    if(phase==1)scene.Advance();
                    for(int v=0;v<scene.Views.Length;v++){report.faces+=scene.Lines(v).Length;report.faces+=scene.Faces(v).Length;}
                }
                watch.Stop();long allocated=report.allocationCounterValid?GC.GetAllocatedBytesForCurrentThread()-bytes:-1;
                if(phase==0){report.fixedMs=watch.Elapsed.TotalMilliseconds;report.fixedBytes=allocated;}else{report.advancingMs=watch.Elapsed.TotalMilliseconds;report.advancingBytes=allocated;}
            }
            string text=string.Format(CultureInfo.InvariantCulture,"{{\"digest\":\"{0}\",\"fixedMs\":{1:R},\"advancingMs\":{2:R},\"fixedBytes\":{3},\"advancingBytes\":{4},\"faces\":{5},\"iterations\":{6},\"views\":{7},\"allocationCounterValid\":{8}}}",report.digest,report.fixedMs,report.advancingMs,report.fixedBytes,report.advancingBytes,report.faces,report.iterations,report.views,report.allocationCounterValid?"true":"false");
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,text);TestContext.WriteLine(text);
        }
    }
}
