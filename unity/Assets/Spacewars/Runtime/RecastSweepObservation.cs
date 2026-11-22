using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
#if UNITY_INCLUDE_TESTS
        internal object ObserveRotationMotorForTests(int actor,int peer)
        {
            var causal=NavCrowdMotion.Causal;
            NavCrowdMotion.Causal=default;
            NavCrowdMotion.CausalActor=actor;
            NavCrowdMotion.CausalPeerActor=peer;
            if(!causal.SelectedSeen)return null;
            var spine=InstalledExecutionFor(actor)?.Corridor?.RecastPlan?.Spine;
            return new
            {
                causal.SelectedSeen,causal.FinalSeen,causal.PolicyRefused,causal.TypedRefused,causal.HintFlags,causal.GuideTarget,
                Body=new{causal.Body.Id,causal.Body.Position,causal.Body.Target,causal.Body.Heading,causal.Body.Active,causal.Body.Speed,causal.Body.OwnPaceCap,causal.Body.OwnedShortReverseDistance,causal.Body.LocalLookaheadSeconds},
                Selected=new{causal.Selected.Position,causal.Selected.Heading,causal.Selected.Blocked,causal.Selected.StaticNoWinner,causal.Selected.IndependentTurn},
                Final=new{causal.Final.Position,causal.Final.Heading},
                Typed=new{causal.FocusContext.TypedState.Phase,causal.FocusContext.TypedState.Cursor,causal.FocusContext.LocalLength,causal.FocusContext.GlobalLength},
                Fan=spine==null?null:new{spine.FanCenter,spine.FanRadius,Boundary=spine.Node(spine.FanNode).Position},
                causal.ConflictCount,causal.First,causal.Last
            };
        }
#endif
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
