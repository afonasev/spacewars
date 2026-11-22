using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class RecastMovedReadyTests
    {
        [Test] public void ReadyFromPreviousSupportReissuesCurrentPhysicalBindingAndArrives()
            =>RecastMovedReadyFixture.Run(PlayableProfile.RecastGroupCorridorDefault);
    }
}
