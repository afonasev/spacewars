using System;
using System.Collections.Generic;
namespace Spacewars.Simulation
{
    public sealed class PlayableQueuedOrderSnapshot
    {
        public readonly long IssuanceId,GroupId,CommandSequence,ActivationRevision,AiActionId;
        public readonly PlayableCommandKind Kind; public readonly NavLocation? Anchor; public readonly int VisibleTargetId,PublicObjectiveId;
        public PlayableQueuedOrderSnapshot(long issuance,long group,long sequence,long revision,PlayableCommandKind kind,NavLocation? anchor,int target,int publicObjectiveId=0,long aiActionId=0)
        {IssuanceId=issuance;GroupId=group;CommandSequence=sequence;ActivationRevision=revision;Kind=kind;Anchor=anchor;VisibleTargetId=target;PublicObjectiveId=publicObjectiveId;AiActionId=aiActionId;}
    }
    public sealed class PlayableTacticalCompletion
    {
        public readonly PlayableSpatialCompletion SpatialReceipt;
        public readonly NavLocation? OriginalLocation,AssignedLocation;public readonly int TargetId;
        public readonly PlayableOrderStamp Order; public readonly long IssuanceId,GroupId,Incarnation,ActivationTick,CompletionTick;
        public PlayableTacticalCompletion(PlayableOrderStamp order,long issuance,long group,long incarnation,long activation,long completion,NavLocation? original=null,NavLocation? assigned=null,int targetId=0)
        {SpatialReceipt=order.Kind!=PlayableCommandKind.Attack&&original.HasValue&&assigned.HasValue?new PlayableSpatialCompletion(order,group,incarnation,original.Value,assigned.Value,activation,completion):null;OriginalLocation=original;AssignedLocation=assigned;TargetId=targetId;Order=order;IssuanceId=issuance;GroupId=group;Incarnation=incarnation;ActivationTick=activation;CompletionTick=completion;}
    }
    public sealed class PlayableUnitQueueSnapshot
    {
        public readonly PlayableQueuedOrderSnapshot Active,Pending;public readonly PlayableTacticalCompletion Completion;public readonly bool PendingAiRouteViable;
        private readonly PlayableQueuedOrderSnapshot[] deferred;
        public IReadOnlyList<PlayableQueuedOrderSnapshot> Deferred=>Array.AsReadOnly(deferred);
        public PlayableUnitQueueSnapshot(PlayableQueuedOrderSnapshot active,PlayableQueuedOrderSnapshot pending,PlayableQueuedOrderSnapshot[] deferred,PlayableTacticalCompletion completion,bool pendingAiRouteViable=false)
        {Active=active;Pending=pending;this.deferred=(PlayableQueuedOrderSnapshot[])deferred.Clone();Completion=completion;PendingAiRouteViable=pendingAiRouteViable;}
    }
}
