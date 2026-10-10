using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Diagnostic U6 policy: one owner, one public authored start approach, and no domain access.
    public sealed partial class PlayableAiScoutLivenessPolicy
    {
        internal PlayableAiScoutLivenessPolicy Fork() => (PlayableAiScoutLivenessPolicy)MemberwiseClone();


        private long nextActionId;
        private long pendingActionId;
        private long pendingGeneration;
        private readonly string ownerId;

        public PlayableAiScoutLivenessPolicy(long firstActionId=1,string ownerId="player-1")
        {
            if(firstActionId<1)throw new ArgumentOutOfRangeException(nameof(firstActionId));
            nextActionId=firstActionId;this.ownerId=ownerId;
        }

        public bool HasPendingObligation=>pendingActionId!=0;

        public PlayableAiAction TryPlan(PlayableAiObservation observation)
        {
            if(observation==null)throw new ArgumentNullException(nameof(observation));
            if(observation.OwnerId!=ownerId)throw new ArgumentException("Policy owner differs from observation.",nameof(observation));
            if(HasPendingObligation&&pendingGeneration!=observation.Generation)ClearPending();
            if(HasPendingObligation)return null;
            var explorer=observation.Entities.Where(x=>x.Owner==observation.Owner&&x.Kind==PlayableEntityKind.Explorer&&x.Health>0&&!x.Moving).OrderBy(x=>x.Id).FirstOrDefault();
            var target=observation.PublicScoutObjectives.Where(x=>x.Reachable&&x.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart).OrderBy(x=>x.SiteId).FirstOrDefault();
            if(explorer==null||target==null)return null;
            pendingActionId=nextActionId++;pendingGeneration=observation.Generation;
            return new PlayableAiAction(pendingActionId,observation.OwnerId,observation.ProfileId,observation.ProfileRevision,observation.Generation,observation.SnapshotSequence,PlayableCommandKind.Move,new[]{explorer.Id},target.Approach,seed:observation.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }

        // Preserve the existing allocator/receipt wire while native selection lives in O2.
        internal PlayableAiAction Admit(PlayableAiObservation o,PlayableAiAction choice)
        {
            if(choice==null||HasPendingObligation)return null;
            if(o.OwnerId!=ownerId)throw new ArgumentException("Scout owner");
            pendingActionId=nextActionId++;pendingGeneration=o.Generation;
            return new PlayableAiAction(pendingActionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,choice.Kind,choice.EntityIds.ToArray(),choice.Target,siteId:choice.SiteId,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }

        public void ObserveReceipt(PlayableAiTraceRecord record)
        {
            if(record==null||!AiStateWire.CallbackGenerationMatches(record,pendingGeneration)||record.OwnerId!=ownerId||record.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity||!HasPendingObligation||record.ActionId!=pendingActionId)return;
            if(record.Status==PlayableAiDeliveryStatus.Applied||record.Status==PlayableAiDeliveryStatus.Rejected||record.Status==PlayableAiDeliveryStatus.Stale||record.Status==PlayableAiDeliveryStatus.Cancelled||record.Status==PlayableAiDeliveryStatus.Stopped||record.Status==PlayableAiDeliveryStatus.InvalidAction||record.Status==PlayableAiDeliveryStatus.InvalidOwner)ClearPending();
        }

        private void ClearPending(){pendingActionId=0;pendingGeneration=0;}
    }
}
