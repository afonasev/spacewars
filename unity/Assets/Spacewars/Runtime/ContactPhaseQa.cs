#if UNITY_INCLUDE_TESTS
using System;
namespace Spacewars.Runtime
{
    // Test-only causal controls and bounded observers. No Player/release switches.
    internal static class ContactPhaseQa
    {
        internal static bool FullScanDetection,LegacyAdmission;
        internal static Action<int> RangeCandidate,TerrainCandidate,QueryVisited;
        internal static Action<int,bool> AfterPublish;
        internal static bool Range(int id){RangeCandidate?.Invoke(id);return true;}
        internal static bool Terrain(int id){TerrainCandidate?.Invoke(id);return true;}
        internal static void Reset(){FullScanDetection=LegacyAdmission=false;RangeCandidate=TerrainCandidate=QueryVisited=null;AfterPublish=null;}
    }
}
#endif
