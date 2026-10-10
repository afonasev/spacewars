using System;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Diagnostic U6 policy: one owner, one idle Factory, and no domain access.
    public sealed partial class PlayableAiProductionLivenessPolicy
    {
        internal PlayableAiProductionLivenessPolicy Fork() => (PlayableAiProductionLivenessPolicy)MemberwiseClone();


        private readonly AiRosterCatalog catalog;
        private PlayableProfile profile;
        internal void Rebind(PlayableProfile next){profile=next;}
        private readonly string ownerId;
        private long nextActionId;
        private long pendingActionId;
        private long pendingGeneration;

        public PlayableAiProductionLivenessPolicy(long firstActionId=1,PlayableProfile profile=null,string ownerId="player-1",AiRosterCatalog catalog=null)
        {
            if(firstActionId<1)throw new ArgumentOutOfRangeException(nameof(firstActionId));
            this.catalog=catalog??AiRosterCatalog.Initial;nextActionId=firstActionId;this.profile=profile??PlayableProfile.Default;this.ownerId=ownerId;
        }

        public bool HasPendingObligation=>pendingActionId!=0;

        public PlayableAiAction TryPlan(PlayableAiObservation observation,PlayableEntityKind productionKind=PlayableEntityKind.Tank)
        {
            if(observation==null)throw new ArgumentNullException(nameof(observation));
            if(observation.OwnerId!=ownerId)throw new ArgumentException("Policy owner differs from observation.",nameof(observation));
            if(HasPendingObligation&&pendingGeneration!=observation.Generation)ClearPending();
            if(HasPendingObligation)return null;
            if(observation.ProfileId!=profile.ProfileId||observation.ProfileRevision!=profile.Revision)return null;
            var descriptor=catalog.For(productionKind);
            foreach(var producer in AiEconomyAdmission.Producers(observation,descriptor))
            {
                var candidate=new PlayableAiAction(1,observation.OwnerId,observation.ProfileId,observation.ProfileRevision,observation.Generation,observation.SnapshotSequence,descriptor.productionCommand,new[]{producer.Id},unitKind:descriptor.kind,seed:observation.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                if(AiEconomyAdmission.Reject(observation,profile,candidate,true,catalog)==null)return Admit(observation,candidate);
            }
            return null;
        }

        internal PlayableAiAction Admit(PlayableAiObservation observation,PlayableAiAction candidate)
        {
            // Admission is independent of the caller's composition choice and shares the
            // same descriptor/unlock/producer/gameplay validation as macro candidates.
            if(candidate==null||HasPendingObligation||AiEconomyAdmission.Reject(observation,profile,candidate,true,catalog)!=null||catalog.Production(candidate.Kind)==null)
                throw new InvalidOperationException("Invalid production commit.");
            var descriptor=catalog.Production(candidate.Kind);
            pendingGeneration=observation.Generation;pendingActionId=nextActionId++;
            return new PlayableAiAction(pendingActionId,candidate.PlayerId,candidate.ProfileId,candidate.ProfileRevision,candidate.Generation,candidate.SnapshotSequence,candidate.Kind,candidate.CopyEntityIds(),unitKind:descriptor.kind,seed:candidate.Seed,sourceIdentity:candidate.SourceIdentity);
        }

        public void ObserveReceipt(PlayableAiTraceRecord record)
        {
            if(record==null||!AiStateWire.CallbackGenerationMatches(record,pendingGeneration)||record.OwnerId!=ownerId||record.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity||!HasPendingObligation||record.ActionId!=pendingActionId)return;
            if(record.Status==PlayableAiDeliveryStatus.Applied||record.Status==PlayableAiDeliveryStatus.Rejected||record.Status==PlayableAiDeliveryStatus.Stale||record.Status==PlayableAiDeliveryStatus.Cancelled||record.Status==PlayableAiDeliveryStatus.Stopped||record.Status==PlayableAiDeliveryStatus.InvalidAction||record.Status==PlayableAiDeliveryStatus.InvalidOwner)ClearPending();
        }

        private void ClearPending(){pendingActionId=0;pendingGeneration=0;}
    }
}
