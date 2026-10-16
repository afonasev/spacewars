using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private sealed partial class PreparedRebind
        {
            internal readonly PlayableDomain.BalanceRallySource[] BalanceRallies;internal bool BalanceRallyPaused;
            internal PlayableDomain BalanceDomain;internal PlayableProfile BalanceProfile,BalanceSource;
        }
        private static void BindPreparedBalance(PreparedRebind c,PlayableDomain domain,PlayableProfile profile)
        {c.BalanceDomain=domain;c.BalanceProfile=profile;c.BalanceSource=domain?.PreparedBalanceSource;if(domain!=null){c.BalanceRallyPaused=domain.PreparedBalanceRallyPaused;domain.ObservePreparedBalanceRallies(c.BalanceRallies);}if(domain!=null&&profile!=null)ValidateMarchConfiguration(profile.GroupMarchMaximumStretch,profile.Revision);if(domain!=null&&(profile==null||!domain.CanCommitPreparedBalance(c.BalanceSource)))throw new InvalidOperationException("Balance caller changed before navigation preparation.");}
        private static bool RebindBalanceSourceCurrent(PreparedRebind c)
        {if(c.BalanceDomain==null)return true;if(c.Operation>long.MaxValue-2||!c.BalanceDomain.CanCommitPreparedBalance(c.BalanceSource)||!c.BalanceDomain.PreparedBalanceRalliesCurrent(c.BalanceRallies,c.BalanceRallyPaused))return false;for(int i=0;i<c.Actors.Length;i++)if(!c.BalanceDomain.BalanceMobilityCurrent(c.Actors[i].Id,c.Actors[i].BalanceMobility))return false;return true;}
        private void ApplyRebindMobilityNoThrow(PreparedRebind c)
        {if(c.BalanceDomain!=null){c.BalanceDomain.ApplyPreparedBalanceRallyPauseNoThrow(c.BalanceRallies,c.BalanceRallyPaused);for(int i=0;i<c.Actors.Length;i++){c.Actors[i].Actor.Speed=c.Actors[i].TargetSpeed;c.Actors[i].Actor.TurnSpeed=c.Actors[i].TargetTurn;}marchStretch=c.BalanceProfile.GroupMarchMaximumStretch;marchProfileRevision=c.BalanceProfile.Revision;marchConfigured=true;}}
        private static void FinishPreparedBalance(PreparedRebind c)
        {c.BalanceDomain?.FinishPreparedBalance(c.BalanceProfile);}
        // Concrete caller bridge: rally progress is observed with navigation
        // and reset without calling CancelProbe after the joint publication.
        internal bool? TryRebindBalance(PlayableDomain domain,PlayableProfile candidate)
        {
            if(domain==null||candidate==null||!domain.CanCommitPreparedBalance(domain.PreparedBalanceSource))return null;
            return TryOwnedRebind(candidate.Navigation,domain,candidate);
        }
    }
}
