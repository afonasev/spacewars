using System;
internal static class NativeIdleCurrentChecks
{
    internal static void Run()=>NativeIdleCurrentFixture.Run(Environment.GetEnvironmentVariable("NATIVE_IDLE_CURRENT_OUTPUT")??throw new ArgumentException("Unique output required"));
}
