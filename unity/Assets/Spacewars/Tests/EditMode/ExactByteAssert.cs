using NUnit.Framework;

namespace Spacewars.Tests.EditMode
{
    // Compare the complete byte sequence without NUnit's per-element boxing and
    // general collection constraints. No hash, sampling or state cache is used.
    internal static class ExactByteAssert
    {
        internal static void AreEqual(byte[] expected, byte[] actual, string message = null)
        {
            if (expected == null || actual == null)
            {
                if (expected != actual) Assert.Fail(message + " byte array null mismatch");
                return;
            }
            if (expected.Length != actual.Length)
                Assert.Fail(message + " byte length expected=" + expected.Length + " actual=" + actual.Length);
            for (int i = 0; i < expected.Length; i++)
                if (expected[i] != actual[i])
                    Assert.Fail(message + " byte mismatch index=" + i + " expected=" + expected[i] + " actual=" + actual[i]);
        }
    }
}
