using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Diagnostic U6 policy: one owner, existing durable commands, and no planner access to PlayableDomain.
    public sealed class PlayableAiEconomicLivenessPolicy
    {
        private enum Obligation { None, Factory, Refinery, Mine }

        private long nextActionId;
        private long pendingActionId;
        private long pendingGeneration;
        private Obligation pending;
        private readonly string ownerId;

        public PlayableAiEconomicLivenessPolicy(long firstActionId=1,string ownerId="player-1")
        {
            if(firstActionId<1)throw new ArgumentOutOfRangeException(nameof(firstActionId));
            nextActionId=firstActionId;this.ownerId=ownerId;
        }

        public bool HasPendingObligation=>pending!=Obligation.None;

        public PlayableAiAction TryPlan(PlayableAiObservation observation)
        {
            if(observation==null)throw new ArgumentNullException(nameof(observation));
            if(observation.OwnerId!=ownerId)throw new ArgumentException("Policy owner differs from observation.",nameof(observation));
            if(pending!=Obligation.None&&pendingGeneration!=observation.Generation)ClearPending();
            if(pending!=Obligation.None)return null;

            var choice=Choose(observation);
            if(choice==Obligation.None)return null;
            pending=choice;pendingGeneration=observation.Generation;pendingActionId=nextActionId++;
            return Action(observation,choice,pendingActionId);
        }

        public void ObserveReceipt(PlayableAiTraceRecord record)
        {
            if(record==null||record.OwnerId!=ownerId||record.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity||pending==Obligation.None||record.ActionId!=pendingActionId)return;
            if(record.Status==PlayableAiDeliveryStatus.Applied||record.Status==PlayableAiDeliveryStatus.Rejected||record.Status==PlayableAiDeliveryStatus.Stale||record.Status==PlayableAiDeliveryStatus.Cancelled||record.Status==PlayableAiDeliveryStatus.Stopped||record.Status==PlayableAiDeliveryStatus.InvalidAction||record.Status==PlayableAiDeliveryStatus.InvalidOwner)ClearPending();
        }

        private static Obligation Choose(PlayableAiObservation observation)
        {
            var home=observation.Sites.FirstOrDefault(x=>x.Site.Id==(observation.Owner==PlayableOwner.Player?1:2)&&x.Owner==observation.Owner&&x.Ready);
            if(home!=null)
            {
                if(!HasBuilding(observation,home.Site.Id,PlayableBuildingKind.Factory))return Obligation.Factory;
                if(!HasBuilding(observation,home.Site.Id,PlayableBuildingKind.Refinery))return Obligation.Refinery;
            }
            return observation.Sites.Any(x=>x.Site.Kind==PlayableBuildingKind.Mine&&x.Owner==observation.Owner&&x.CenterId==0&&!x.Contested)?Obligation.Mine:Obligation.None;
        }

        private static bool HasBuilding(PlayableAiObservation observation,int siteId,PlayableBuildingKind kind)
            =>observation.Buildings.Any(x=>x.Owner==observation.Owner&&x.SiteId==siteId&&x.Kind==kind);

        private static PlayableAiAction Action(PlayableAiObservation observation,Obligation obligation,long actionId)
        {
            if(obligation==Obligation.Factory||obligation==Obligation.Refinery)
            {
                var home=observation.Sites.Single(x=>x.Site.Id==(observation.Owner==PlayableOwner.Player?1:2));
                int slot=home.Site.Slots.First(x=>!observation.Buildings.Any(b=>b.SiteId==home.Site.Id&&b.SlotId==x.Id)).Id;
                return new PlayableAiAction(actionId,observation.OwnerId,observation.ProfileId,observation.ProfileRevision,observation.Generation,observation.SnapshotSequence,PlayableCommandKind.BuildAt,siteId:home.Site.Id,slotId:slot,parentId:home.CenterId,buildingKind:obligation==Obligation.Factory?PlayableBuildingKind.Factory:PlayableBuildingKind.Refinery,seed:observation.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            }
            var mine=observation.Sites.First(x=>x.Site.Kind==PlayableBuildingKind.Mine&&x.Owner==observation.Owner&&x.CenterId==0&&!x.Contested);
            return new PlayableAiAction(actionId,observation.OwnerId,observation.ProfileId,observation.ProfileRevision,observation.Generation,observation.SnapshotSequence,PlayableCommandKind.BuildAt,siteId:mine.Site.Id,slotId:0,parentId:0,buildingKind:PlayableBuildingKind.Mine,seed:observation.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }

        private void ClearPending(){pending=Obligation.None;pendingActionId=0;pendingGeneration=0;}
    }
}
