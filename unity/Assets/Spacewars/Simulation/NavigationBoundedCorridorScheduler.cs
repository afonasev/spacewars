using System;
namespace Spacewars.Simulation
{
    public sealed partial class LayeredNavigationScheduler
    {
        internal static Action<NavScheduledRequest,object> CertifiedQueryReadyObserver;
        private void SetCorridorDonor(NavDemandQueryWork donor)
        {
            if(ReferenceEquals(donor,corridorDonor))return;
            if(donor!=null){if(!donor.CanDonateCorridor)throw new ArgumentException("Unsolved corridor donor.");donor.Seal();if(IsRegistered)RequireEdge(donor);donor.Retain();}
            if(corridorDonor!=null)ReleaseProviderRef(corridorDonor.BoundedRecipe);if(donor!=null)AddProviderRef(donor.BoundedRecipe);corridorDonor?.Release();corridorDonor=donor;
        }
    }
    public sealed partial class NavigationArrivalWork
    {
        internal static Action<NavigationArrivalWork> CertifiedArrivalReadyObserver;
    }
}
