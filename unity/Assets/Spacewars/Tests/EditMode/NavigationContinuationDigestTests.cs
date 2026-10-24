using System;
using System.IO;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationContinuationDigestTests
    {
        [TestCase(0)] [TestCase(4095)] [TestCase(4096)] [TestCase(1048576)]
        public void StreamedContinuationPreservesExactBinaryWriterDigest(int size)
        {
            var text=new string('я',size);
            void Transcript(BinaryWriter writer)
            {
                writer.Write("continuation");writer.Write(-17L);writer.Write(-0.0);writer.Write(true);
                writer.Write(text);for(int n=0;n<8192;n++){writer.Write(n);writer.Write(n/7.0);}
                writer.Write(NavContract.StreamingDigest(inner=>{inner.Write(text);inner.Write(long.MaxValue);}));
            }
            string expected=NavContract.Digest(Transcript);
            var budget=new NavigationRetentionBudget(LayeredNavigationScheduler.MaxRetainedBytes-NavigationRetentionBudget.RootBytes-2*NavContract.StreamingDigestScratchBytes-1024);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,0,0,out var scope));
            using(scope){
                long before=budget.RetainedBytes;
                using(NavStorageScratch.Reserve(scope.OwnedReservation(),2*NavContract.StreamingDigestScratchBytes))
                    Assert.AreEqual(expected,NavContract.StreamingDigest(Transcript));
                Assert.AreEqual(before,budget.RetainedBytes,"digest scratch is returned before the next decode allocation");
                budget.AllocationAdmission=_=>false;
                Assert.Throws<InvalidOperationException>(()=>NavStorageScratch.Reserve(scope.OwnedReservation(),2*NavContract.StreamingDigestScratchBytes));
                Assert.AreEqual(before,budget.RetainedBytes,"refusal leaves the current cap and ledger intact");
            }
        }
    }
}
