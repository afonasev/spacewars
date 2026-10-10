using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Diagnostic U6 policy: one owner, one idle Factory, and no domain access.
    public sealed partial class PlayableAiProductionLivenessPolicy
    {
        internal PlayableAiProductionLivenessPolicy Fork() => (PlayableAiProductionLivenessPolicy)MemberwiseClone();


        private PlayableProfile profile;
        internal void Rebind(PlayableProfile next){profile=next;}
        private readonly string ownerId;
        private long nextActionId;
        private long pendingActionId;
        private long pendingGeneration;

        public PlayableAiProductionLivenessPolicy(long firstActionId=1,PlayableProfile profile=null,string ownerId="player-1")
        {
            if(firstActionId<1)throw new ArgumentOutOfRangeException(nameof(firstActionId));
            nextActionId=firstActionId;this.profile=profile??PlayableProfile.Default;this.ownerId=ownerId;
        }

        public bool HasPendingObligation=>pendingActionId!=0;

        public PlayableAiAction TryPlan(PlayableAiObservation observation)
        {
            if(observation==null)throw new ArgumentNullException(nameof(observation));
            if(observation.OwnerId!=ownerId)throw new ArgumentException("Policy owner differs from observation.",nameof(observation));
            if(HasPendingObligation&&pendingGeneration!=observation.Generation)ClearPending();
            if(HasPendingObligation)return null;
            // The paid queue rejects insufficient credits; planning from the owner balance avoids a known-illegal action.
            if(observation.ProfileId!=profile.ProfileId||observation.ProfileRevision!=profile.Revision||observation.Credits<profile.TankCreditCost)return null;
            var factory=observation.Buildings
                .Where(x=>x.Owner==observation.Owner&&x.Kind==PlayableBuildingKind.Factory&&x.Phase==ConstructionPhase.Ready&&x.PrivateState!=null&&x.PrivateState.QueueCount==0)
                .OrderBy(x=>x.Id)
                .FirstOrDefault();
            if(factory==null)return null;

            pendingActionId=nextActionId++;pendingGeneration=observation.Generation;
            return new PlayableAiAction(pendingActionId,observation.OwnerId,observation.ProfileId,observation.ProfileRevision,observation.Generation,observation.SnapshotSequence,PlayableCommandKind.QueueTank,new[]{factory.Id},unitKind:PlayableEntityKind.Tank,seed:observation.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }

        internal PlayableAiAction Admit(PlayableAiObservation observation,PlayableAiAction candidate)
        {
            if(HasPendingObligation||(candidate.Kind!=PlayableCommandKind.QueueTank&&candidate.Kind!=PlayableCommandKind.QueueExplorer))throw new InvalidOperationException("Invalid production commit.");
            pendingGeneration=observation.Generation;pendingActionId=nextActionId++;
            return new PlayableAiAction(pendingActionId,candidate.PlayerId,candidate.ProfileId,candidate.ProfileRevision,candidate.Generation,candidate.SnapshotSequence,candidate.Kind,candidate.CopyEntityIds(),unitKind:candidate.UnitKind,seed:candidate.Seed,sourceIdentity:candidate.SourceIdentity);
        }

        public void ObserveReceipt(PlayableAiTraceRecord record)
        {
            if(record==null||!AiStateWire.CallbackGenerationMatches(record,pendingGeneration)||record.OwnerId!=ownerId||record.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity||!HasPendingObligation||record.ActionId!=pendingActionId)return;
            if(record.Status==PlayableAiDeliveryStatus.Applied||record.Status==PlayableAiDeliveryStatus.Rejected||record.Status==PlayableAiDeliveryStatus.Stale||record.Status==PlayableAiDeliveryStatus.Cancelled||record.Status==PlayableAiDeliveryStatus.Stopped||record.Status==PlayableAiDeliveryStatus.InvalidAction||record.Status==PlayableAiDeliveryStatus.InvalidOwner)ClearPending();
        }

        private void ClearPending(){pendingActionId=0;pendingGeneration=0;}
    }
}
