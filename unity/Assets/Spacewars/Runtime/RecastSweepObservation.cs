using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        internal object MovementDelayRecastReceipt;
        private void CaptureRecastMovementReceipt(NavPreparedCrowdStep frame)
        {
            MovementDelayRecastReceipt=null;if(MovementDelayActor==0)return;int row=frame.Find(MovementDelayActor);if(row<0||frame.RecastRoutes[row]==null)return;
            var route=frame.RecastRoutes[row];var receipt=frame.RecastReceipts[row];var causal=NavCrowdMotion.Causal;
            object Proposal(NavMotionProposal p)=>new{p.Position,p.Velocity,p.Heading,p.Blocked,p.StaticNoWinner,p.JointRefused,p.Overflow,p.Refuse};
            var committedCausal=NavCrowdMotion.CausalActor==MovementDelayActor?new{causal.ComputeSerial,causal.SelectedSeen,causal.FinalSeen,causal.JointPasses,causal.ConflictCount,causal.HintFlags,causal.GuideTarget,causal.ForwardLimit,causal.PolicyRefused,causal.TypedRefused,Selected=Proposal(causal.Selected),PreFilter=Proposal(causal.PreFilter),Final=Proposal(causal.Final),causal.First,causal.Last}:null;
            if(!receipt.Verified){MovementDelayRecastReceipt=new{Version=2,Committed=true,Causal=committedCausal,route.Subscription.WorldGeneration,route.Subscription.RequestSequence,Verified=false,receipt.ProductionUnmapped,ProductionException=frame.Motion[row].YieldLabel<0,receipt.From,receipt.To,receipt.FromSurface,receipt.ToSurface};return;}
            MovementDelayRecastReceipt=new{Version=2,Committed=true,Causal=committedCausal,route.Subscription.WorldGeneration,route.Subscription.RequestSequence,Verified=receipt.Verified,ProductionException=frame.Motion[row].YieldLabel<0,receipt.From,receipt.To,receipt.FromSurface,receipt.ToSurface,BeforePhase=receipt.Before.Phase,BeforeCursor=receipt.Before.Cursor,BeforeStation=receipt.Before.Station,BeforeHighWater=receipt.Before.HighWater,AfterPhase=receipt.After.Phase,AfterCursor=receipt.After.Cursor,AfterStation=receipt.After.Station,AfterHighWater=receipt.After.HighWater,receipt.PortalCount,receipt.FirstPortalLeg,receipt.LastPortalLeg,receipt.FirstSeam,receipt.LastSeam,receipt.FirstCrossing,receipt.LastCrossing};
        }
    }
}
