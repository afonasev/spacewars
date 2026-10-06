using System;

namespace Spacewars.Simulation
{
    public enum PlayableResearchKind { TankChassis, ExplorerAssaultGuns, ShkvalGuidance }

    // Save/replay code owns this authority-only payload. It deliberately is not
    // projected to opponents: research progress is private until its effect is visible.
    [Serializable]
    public sealed class PlayableResearchSaveState
    {
        public PlayableResearchSaveOrder[] Orders = Array.Empty<PlayableResearchSaveOrder>();
        public long NextSequence = 1;
    }

    [Serializable]
    public sealed class PlayableResearchSaveOrder
    {
        public int Owner;
        public long Id;
        public PlayableResearchKind Kind;
        public int CenterId;
        public double PaidCost;
        public double Elapsed;
        public double Duration;
        public bool Active;
        public bool Complete;
    }
    public sealed class PlayableResearchOrderSnapshot
    {
        public PlayableResearchOrderSnapshot(long id,PlayableResearchKind kind,int centerId,double paidCost,double elapsed,double duration,bool active,bool complete)
        {Id=id;Kind=kind;CenterId=centerId;PaidCost=paidCost;Elapsed=elapsed;Duration=duration;Active=active;Complete=complete;}
        public long Id{get;} public PlayableResearchKind Kind{get;} public int CenterId{get;} public double PaidCost{get;} public double Elapsed{get;} public double Duration{get;} public bool Active{get;} public bool Complete{get;}
        public double Progress=>Duration>0?Elapsed/Duration:0;
    }
    // Owner-safe legal research surface: cost and availability, never opponent progress.
    public sealed class PlayableResearchAvailabilitySnapshot
    {
        public PlayableResearchAvailabilitySnapshot(PlayableResearchKind kind,double cost,bool available)
        {Kind=kind;Cost=cost;Available=available;}
        public PlayableResearchKind Kind{get;} public double Cost{get;} public bool Available{get;}
    }
    public sealed class PlayableRefineryUpgradeSnapshot
    {
        public PlayableRefineryUpgradeSnapshot(bool active,bool complete,double paidCost,double elapsed,double duration,string blockedReason)
        {Active=active;Complete=complete;PaidCost=paidCost;Elapsed=elapsed;Duration=duration;BlockedReason=blockedReason;}
        public bool Active{get;} public bool Complete{get;} public double PaidCost{get;} public double Elapsed{get;} public double Duration{get;} public string BlockedReason{get;}
        public double Progress=>Duration>0?Elapsed/Duration:0;
    }
}
