// QA-only temporary install into Simulation; no solver-owned fields.
using System;
namespace Spacewars.Simulation
{
    internal static class NavExactDemandProbeQa
    {
        internal static Action<int,NavExactCost,bool,NavExactCost> Insert;
        internal static Action<int,NavExactCost> Pop;
        internal static Action<int,NavExactCost,NavExactCost> AstarPop;
        internal static Action<bool,NavDistanceMetricContract> Qualification;
        internal static Action<NavExactCost,NavExactCost,double> Ready;
    }
}
