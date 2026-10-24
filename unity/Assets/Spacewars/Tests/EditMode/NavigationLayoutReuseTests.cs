using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationLayoutReuseTests
    {
        private enum Small : short { Zero }
#pragma warning disable 0649
        private struct Mixed { public byte A; public short B; public int C; public double D; }
        private class Base { private byte hidden; }
        private sealed class Derived : Base { private Small small; public object Reference; public Mixed Value; }
#pragma warning restore 0649
        [Test] public void ConcurrentFirstUseKeepsInheritedPrivateAndNestedValueLayout()
        {
            // Each caller sees the same metadata calculation, including private base fields.
            var workers=new Thread[8];var errors=new Exception[8];var answers=new int[8];
            using(var start=new ManualResetEvent(false)){
                for(int i=0;i<workers.Length;i++){int at=i;workers[i]=new Thread(()=>{try{start.WaitOne();for(int n=0;n<1000;n++)answers[at]=NavStorageLayout.ObjectBytesBound(typeof(Derived))+NavStorageLayout.ValueBytes(typeof(Mixed))+NavStorageLayout.ValueBytes(typeof(Small));}catch(Exception e){errors[at]=e;}});workers[i].Start();}
                start.Set();for(int i=0;i<workers.Length;i++)Assert.True(workers[i].Join(5000));
            }
            for(int i=0;i<workers.Length;i++){Assert.Null(errors[i]);Assert.AreEqual(74,answers[i]);}
            Assert.AreEqual(8,NavStorageLayout.ValueBytes(typeof(Derived)));
            Assert.Throws<ArgumentException>(()=>NavStorageLayout.ObjectBytesBound(typeof(Mixed)));
            Assert.AreEqual(56,NavStorageLayout.ObjectBytesBound(typeof(Derived)));
        }
        [Test] public void RepeatedPhysicalLayoutsPreserveChargesAndReportLookupCost()
        {
            const int count=10000;long total=0;
            // Fixed values are established physical-format contracts, not timing thresholds.
            Assert.AreEqual(376,NavStorageLayout.ObjectBytesBound(typeof(NavigationHostRoot)));
            Assert.AreEqual(272,NavStorageLayout.ValueBytes(typeof(NavExactCost)));
            Assert.AreEqual(280,NavStorageLayout.ValueBytes(typeof(NavExactQueueEntry)));
            var watch=Stopwatch.StartNew();
            for(int i=0;i<count;i++)total+=NavStorageLayout.ObjectBytesBound(typeof(NavigationHostRoot))+NavStorageLayout.ValueBytes(typeof(NavExactCost))+NavStorageLayout.ValueBytes(typeof(NavExactQueueEntry));
            watch.Stop();Assert.AreEqual((376L+272+280)*count,total);
            TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,"PHYSICAL_LAYOUT count={0} wall_ms={1:F6} profile={2}",count,watch.Elapsed.TotalMilliseconds,PerformanceProfile.Enabled));
        }
    }
}
