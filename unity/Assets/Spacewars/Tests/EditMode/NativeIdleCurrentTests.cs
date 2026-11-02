using System;
using NUnit.Framework;
namespace Spacewars.Tests.EditMode
{
    public sealed class NativeIdleCurrentTests
    {
        [Test] public void FreshBoundIdleCurrentKeepsExactNextNativeOperation()
            =>NativeIdleCurrentFixture.Run(Environment.GetEnvironmentVariable("NATIVE_IDLE_CURRENT_OUTPUT")??throw new InvalidOperationException("Explicit unique evidence output required"));
    }
}
