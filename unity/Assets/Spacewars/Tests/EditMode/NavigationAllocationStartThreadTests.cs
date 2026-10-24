using NUnit.Framework;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationAllocationStartThreadTests
    {
        [TestCase(true)][TestCase(false)]
        public void RealWorkerStartPreservesMove(bool overlap)
        {
            TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(NavigationCommandInterleavingScenario.Run(overlap)));
        }
    }
}
