#if UNITY_EDITOR || DEVELOPMENT_BUILD
namespace Spacewars.Simulation
{
    // Observation only: no wire/authority ownership and no derived strings.
    internal struct NavArrivalLifecycleState
    {
        internal long Epoch,Total,CandidateWork,OccupancyWork,DirectWork;
        internal int Member,Page,Raw,CandidateCursor,OccupancyCursor,PreparationPage,PreparationMember,HistoricalGate,HistoricalCursor,Phase,Status;
    }
    public sealed partial class NavigationArrivalWork
    {
        internal NavArrivalLifecycleState LifecycleState=>new NavArrivalLifecycleState{Epoch=lastEpoch,Total=AllocationWorkConsumed,CandidateWork=CandidateWorkConsumed,OccupancyWork=OccupancyWorkConsumed,DirectWork=DirectWorkConsumed,Member=memberIndex,Page=pageIndex,Raw=preparationCursor,CandidateCursor=candidateCursor,OccupancyCursor=occupantCursor,PreparationPage=preparationPageIndex,PreparationMember=preparationMember,HistoricalGate=historicalGate,HistoricalCursor=historicalCursor,Phase=(int)phase,Status=(int)Status};
    }
}
#endif
