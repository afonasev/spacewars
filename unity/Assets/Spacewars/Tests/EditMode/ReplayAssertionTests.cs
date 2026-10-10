using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class ReplayAssertionTests
    {
        private static bool Passes(TestDelegate assertion)
        {
            try { assertion(); return true; }
            catch (AssertionException) { return false; }
        }

        [Test]
        public void DirectByteAssertionMatchesNUnitForEqualNullLengthAndEveryMismatchPosition()
        {
            void Equivalent(byte[] a, byte[] b)
            {
                Assert.AreEqual(Passes(() => CollectionAssert.AreEqual(a, b)),
                    Passes(() => ExactByteAssert.AreEqual(a, b)));
            }
            Equivalent(null, null); Equivalent(null, new byte[0]); Equivalent(new byte[0], null);
            Equivalent(new byte[0], new byte[0]);
            var random = new Random(19092026);
            foreach (int length in new[] { 1, 7, 64, 257 })
            {
                var a = new byte[length]; random.NextBytes(a);
                Equivalent(a, a); Equivalent(a, (byte[])a.Clone());
                Equivalent(a, new byte[length + 1]); Equivalent(new byte[length + 1], a);
                for (int i = 0; i < length; i++)
                {
                    var b = (byte[])a.Clone(); b[i] ^= 255;
                    Equivalent(a, b); Equivalent(b, a);
                    var failure = Assert.Throws<AssertionException>(() => ExactByteAssert.AreEqual(a, b, "checkpoint"));
                    StringAssert.Contains("checkpoint byte mismatch index=" + i + " ", failure.Message);
                }
            }
            var large = new byte[262145]; random.NextBytes(large);
            Equivalent(large, (byte[])large.Clone());
            foreach (int index in new[] { 0, 65535, 65536, 262144 })
            {
                var b = (byte[])large.Clone(); b[index] ^= 1; Equivalent(large, b);
            }
        }

        [Test]
        public void CachedNamesPreserveEveryIndependentOracleByteAndReadMutatedState()
        {
            void Equivalent(object value, bool authority)
            {
                ExactByteAssert.AreEqual(PlayableWorldRestoreTests.FactBytes(value, authority, false),
                    PlayableWorldRestoreTests.FactBytes(value, authority, true));
            }
            var values = new object[] { null, "unicode \u041c\u0438\u0440", double.NaN, -0d, long.MaxValue,
                new byte[] { 0, 128, 255 }, new double[] { -0d, double.NaN, double.PositiveInfinity, 3 }, new int[] { int.MinValue, 0, int.MaxValue }, new HashSet<int> { 3, 1, 2 },
                new Dictionary<int,string> { { 2, "b" }, { 1, "a" } }, new double[,] { { 1, 2 }, { 3, 4 } } };
            foreach (var value in values) { Equivalent(value, true); Equivalent(value, false); }
            var domainType = typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain", true);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (var profile in new[] { PlayableProfile.Default, PlayableProfile.ThreeCrossingsDefault })
            {
                var domain = Activator.CreateInstance(domainType, flags, null, new object[] { profile, 71L, false }, null);
                Equivalent(domain, true);
                var before = PlayableWorldRestoreTests.FactBytes(domain, true);
                domainType.GetMethod("Step", flags).Invoke(domain, new object[] { 1d / 30 });
                Equivalent(domain, true);
                Assert.False(Passes(() => ExactByteAssert.AreEqual(before, PlayableWorldRestoreTests.FactBytes(domain, true))),
                    "Caching names must never cache mutable authority values");
            }
        }
    }
}
