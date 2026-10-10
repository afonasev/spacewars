using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using static Spacewars.Runtime.WorldWire;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private const int TacticalQueueTag=0x54514631;
        private void WriteTacticalQueueExtension(BinaryWriter w)
        {
            w.Write(TacticalQueueTag);w.Write(5);w.Write(queueSequence);
            var issuances=tacticalQueues.Values.SelectMany(q=>q.Deferred.Concat(q.Active==null?Enumerable.Empty<QueueIssuance>():new[]{q.Active.Issuance}).Concat(q.Pending==null?Enumerable.Empty<QueueIssuance>():new[]{q.Pending.Issuance})).GroupBy(i=>i.Id).Select(g=>g.First()).OrderBy(i=>i.Id).ToArray();
            Array(w,issuances,i=>{w.Write(i.Id);w.Write(i.Revision);w.Write(i.Tick);w.Write(i.GroupId);var c=i.Command;w.Write(c.Generation);w.Write(c.Sequence);String(w,c.PlayerId);w.Write((int)c.Kind);w.Write((int)c.Mode);WorldWire.Write(w,c.Target);w.Write(c.Pad);w.Write(c.SiteId);w.Write(c.SlotId);w.Write((int)c.BuildingKind);w.Write(c.ParentId);w.Write(c.TargetId);w.Write(c.ProductionOrderId);w.Write((int)c.UnitKind);w.Write((int)c.ResearchKind);w.Write((int)c.Origin);String(w,c.Source);w.Write(c.JobId);w.Write(c.ActionId);GroupOrderWire.WriteLocation(w,c.TargetLocation);Array(w,c.CopyEntityIds(),id=>w.Write(id));});
            void Execution(QueueExecution e){w.Write(e!=null);if(e==null)return;w.Write(e.Issuance.Id);WriteOrderStamp(w,e.Stamp);GroupOrderWire.WriteLocation(w,e.Assigned);w.Write(e.Incarnation);w.Write(e.ActivationTick);w.Write(e.RouteRequest);w.Write(e.RouteOrder);w.Write(e.Arrived);w.Write(e.Suspended);w.Write(e.Failed);w.Write(e.AttemptRequest);w.Write(e.AttemptOrder);w.Write(e.Interrupted);GroupOrderWire.WriteLocation(w,e.OverrideLocation);GroupOrderWire.WriteLocation(w,e.AttemptLocation);var f=e.TerminalFailure;w.Write(f!=null);if(f!=null){w.Write(f.Request);w.Write(f.Order);w.Write(f.GroupId);w.Write(f.Revision);w.Write(f.Incarnation);w.Write(f.Tick);GroupOrderWire.WriteLocation(w,f.Location);w.Write((int)f.Outcome);}}
            Array(w,tacticalQueues.OrderBy(x=>x.Key).ToArray(),row=>{w.Write(row.Key);var q=row.Value;w.Write(q.OldMotionSuspended);Execution(q.Active);Execution(q.Pending);Array(w,q.Deferred.ToArray(),i=>w.Write(i.Id));w.Write(q.Completion!=null);if(q.Completion!=null){var c=q.Completion;WriteOrderStamp(w,c.Order);w.Write(c.IssuanceId);w.Write(c.GroupId);w.Write(c.Incarnation);w.Write(c.ActivationTick);w.Write(c.CompletionTick);GroupOrderWire.WriteLocation(w,c.OriginalLocation);GroupOrderWire.WriteLocation(w,c.AssignedLocation);w.Write(c.TargetId);}});
            WriteQueueMapLayout(w,spatialExecutions);WriteQueueMapLayout(w,spatialCompletions);WriteQueueMapLayout(w,tacticalQueues);
        }
        private void ReadTacticalQueueExtension(BinaryReader r,bool legacy)
        {
            if(r.BaseStream.Position==r.BaseStream.Length){if(!legacy)throw new ArgumentException("Missing schema10 tactical queue extension.");return;} // Explicit absent extension never synthesizes execution/completion.
            if(legacy||r.ReadInt32()!=TacticalQueueTag)throw new ArgumentException("Unsupported tactical queue extension.");var version=r.ReadInt32();if(version!=3&&version!=4&&version!=5)throw new ArgumentException("Unsupported tactical queue extension.");
            queueSequence=r.ReadInt64();if(queueSequence<0)throw new ArgumentException("Invalid issuance allocator.");
            var issuances=new Dictionary<long,QueueIssuance>();
            foreach(var i in Array(r,()=>{
                long id=r.ReadInt64(),revision=r.ReadInt64(),tick=r.ReadInt64(),group=r.ReadInt64(),generation=r.ReadInt64(),sequence=r.ReadInt64();string owner=String(r);var kind=EnumValue<PlayableCommandKind>(r);var mode=EnumValue<PlayableOrderMode>(r);var target=ReadPoint(r);int pad=0,site=0,slot=0,parent=0,targetId=0;long productionOrder=0;var building=PlayableBuildingKind.Headquarters;var unit=PlayableEntityKind.Tank;var research=PlayableResearchKind.TankChassis;
                if(version>=4){pad=r.ReadInt32();site=r.ReadInt32();slot=r.ReadInt32();building=EnumValue<PlayableBuildingKind>(r);parent=r.ReadInt32();targetId=r.ReadInt32();productionOrder=r.ReadInt64();unit=EnumValue<PlayableEntityKind>(r);research=EnumValue<PlayableResearchKind>(r);}else targetId=r.ReadInt32();
                var origin=EnumValue<PlayableOrderOrigin>(r);string source=String(r);long job=r.ReadInt64(),action=r.ReadInt64();var location=GroupOrderWire.ReadLocation(r);var ids=Array(r,()=>r.ReadInt32());
                var command=new PlayableCommand(generation,sequence,owner,kind,ids,target,pad,site,slot,building,parent,targetId,productionOrder,unit,research,origin,source,job,action,mode);if(location.HasValue)command=command.WithResolvedLocation(location.Value);
                return new QueueIssuance{Id=id,Revision=revision,Tick=tick,GroupId=group,Command=command};
            })){
                if(i.Id<1||i.Id>queueSequence||i.Revision<1||i.Revision>=nextOrderRevision||i.Tick<0||i.Tick>Tick||i.GroupId<0||!QueueKind(i.Command.Kind)||i.Command.Generation!=navigation.Generation||i.Command.Sequence<1||!Authorizes(i.Command.PlayerId)||issuances.ContainsKey(i.Id)||
                    i.Command.EntityIds.Count==0||i.Command.EntityIds.Distinct().Count()!=i.Command.EntityIds.Count||i.Command.EntityIds.Any(id=>id<1||id>=nextId)||
                    (i.Command.Origin==PlayableOrderOrigin.Ai?(string.IsNullOrWhiteSpace(i.Command.Source)||i.Command.JobId<1||i.Command.ActionId<1):(i.Command.Source!=null||i.Command.JobId!=0||i.Command.ActionId!=0))||
                    (i.Command.Kind==PlayableCommandKind.Attack?i.Command.TargetId<1||i.Command.TargetId>=nextId||i.Command.TargetLocation.HasValue:!i.Command.TargetLocation.HasValue||!navigation.Crowd.ValidLocation(i.Command.TargetLocation.Value,0)))throw new ArgumentException("Invalid tactical issuance.");
                issuances.Add(i.Id,i);
            }
            QueueIssuance Issuance(){long id=r.ReadInt64();if(!issuances.TryGetValue(id,out var i))throw new ArgumentException("Unknown tactical issuance reference.");return i;}
            QueueExecution Execution(){if(!Boolean(r))return null;var e=new QueueExecution{Issuance=Issuance(),Stamp=ReadOrderStamp(r),Assigned=GroupOrderWire.ReadLocation(r),Incarnation=r.ReadInt64(),ActivationTick=r.ReadInt64(),RouteRequest=r.ReadInt64(),RouteOrder=r.ReadInt64(),Arrived=Boolean(r),Suspended=Boolean(r),Failed=Boolean(r),AttemptRequest=r.ReadInt64(),AttemptOrder=r.ReadInt64(),Interrupted=Boolean(r),OverrideLocation=GroupOrderWire.ReadLocation(r),AttemptLocation=GroupOrderWire.ReadLocation(r)};if(Boolean(r))e.TerminalFailure=new TerminalRouteFailure{Request=r.ReadInt64(),Order=r.ReadInt64(),GroupId=r.ReadInt64(),Revision=r.ReadInt64(),Incarnation=r.ReadInt64(),Tick=r.ReadInt64(),Location=GroupOrderWire.ReadLocation(r),Outcome=EnumValue<NavigationOutcome>(r)};return e;}
            foreach(var row in Array(r,()=>{
                int id=r.ReadInt32();var q=new TacticalQueue{OldMotionSuspended=Boolean(r),Active=Execution(),Pending=Execution()};q.Deferred.AddRange(Array(r,Issuance));
                if(Boolean(r))q.Completion=new PlayableTacticalCompletion(ReadOrderStamp(r),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),GroupOrderWire.ReadLocation(r),GroupOrderWire.ReadLocation(r),r.ReadInt32());return new KeyValuePair<int,TacticalQueue>(id,q);
            })){
                if(tacticalQueues.ContainsKey(row.Key)||!units.ContainsKey(row.Key))throw new ArgumentException("Invalid tactical recipient.");tacticalQueues.Add(row.Key,row.Value);if(row.Value.Active!=null){var unit=units[row.Key];var o=unit.CurrentOrder;var i=row.Value.Active.Issuance;if(o!=null&&o.CommandSequence==i.Command.Sequence){unit.CurrentOrder=new PlayableTacticalOrderSnapshot(o.UnitId,o.Owner,o.Generation,o.CommandSequence,o.IssuedTick,o.Kind,o.Destination,o.TargetId,i.Command.TargetLocation);}}
            }
            var used=new HashSet<long>(tacticalQueues.Values.SelectMany(q=>q.Deferred.Concat(q.Active==null?Enumerable.Empty<QueueIssuance>():new[]{q.Active.Issuance}).Concat(q.Pending==null?Enumerable.Empty<QueueIssuance>():new[]{q.Pending.Issuance})).Select(i=>i.Id));
            if(used.Count!=issuances.Count)throw new ArgumentException("Unreferenced tactical issuance.");
            if(version>=5){ReadQueueMapLayout(r,spatialExecutions);ReadQueueMapLayout(r,spatialCompletions);ReadQueueMapLayout(r,tacticalQueues);}
        }
        // Prior payloads sort keys. Preserve both occupied-row order and free-slot
        // stack in v5, so subsequent removals/additions survive detached restoration.
        private static void WriteQueueMapLayout<T>(BinaryWriter w,AuthoritySlotMap<int,T> map)
        {Array(w,map.Keys.ToArray(),id=>w.Write(id));Array(w,map.CaptureLayout(),slot=>w.Write(slot));}
        private static void ReadQueueMapLayout<T>(BinaryReader r,AuthoritySlotMap<int,T> map)
        {
            var keys=Array(r,()=>r.ReadInt32());var layout=Array(r,()=>r.ReadInt32());
            if(keys.Length!=map.Count||keys.Distinct().Count()!=keys.Length||keys.Any(id=>!map.ContainsKey(id)))throw new ArgumentException("Invalid queue map row order.");
            var rows=keys.Select(id=>new KeyValuePair<int,T>(id,map[id])).ToArray();map.Clear();foreach(var row in rows)map.Add(row.Key,row.Value);map.RestoreLayout(layout);
        }
        private static bool FailureMatches(NavUnit actor,QueueExecution e)=>actor.FailedRequest==e.AttemptRequest&&actor.FailedOrder==e.AttemptOrder&&actor.FailedGroupId==e.Issuance.GroupId&&actor.FailedRevision==e.Stamp.Revision&&actor.FailedGoal.Equals(e.Interrupted?e.OverrideLocation:e.Assigned);
        private bool TerminalProofMatches(QueueExecution e,NavUnit actor)=>e?.TerminalFailure is TerminalRouteFailure f&&f.Request==e.AttemptRequest&&f.Order==e.AttemptOrder&&f.GroupId==e.Issuance.GroupId&&f.Revision==e.Stamp.Revision&&f.Incarnation==e.Incarnation&&f.Incarnation==actor.Incarnation&&f.Tick>=e.Issuance.Tick&&f.Tick<=Tick&&f.Location.HasValue&&e.AttemptLocation.HasValue&&f.Location.Equals(e.AttemptLocation)&&
            (e.Issuance.Command.Kind==PlayableCommandKind.Attack||f.Location.Equals(e.Interrupted?e.OverrideLocation:e.Assigned))&&navigation.Crowd.ValidLocation(f.Location.Value,actor.Radius)&&(f.Outcome==NavigationOutcome.Rejected||f.Outcome==NavigationOutcome.Unreachable);
        private bool AttackAttemptBound(QueueExecution e,NavUnit actor,Dictionary<int,NavigationRequestState> pending,Dictionary<int,long> orders,long requestSequence)
        {
            if(pending.TryGetValue(actor.Id,out var latest)&&latest.MemberIdentity!=null&&latest.MemberIdentity.GroupId==e.Issuance.GroupId&&latest.MemberIdentity.OrderRevision==e.Stamp.Revision)
                return e.AttemptRequest==latest.Request&&e.AttemptOrder==latest.Order&&latest.MemberIdentity.Incarnation==e.Incarnation&&e.AttemptLocation.HasValue&&latest.GoalLocation.Equals(e.AttemptLocation);
            if(e.AttemptRequest==0)return e.AttemptOrder==0&&!e.AttemptLocation.HasValue&&e.RouteRequest==0&&e.RouteOrder==0&&e.TerminalFailure==null;
            if(e.AttemptRequest<1||e.AttemptRequest>requestSequence||e.AttemptOrder<1||!orders.TryGetValue(actor.Id,out var watermark)||e.AttemptOrder>watermark||!e.AttemptLocation.HasValue||!navigation.Crowd.ValidLocation(e.AttemptLocation.Value,actor.Radius))return false;
            if(pending.TryGetValue(actor.Id,out var r)&&r.Request==e.AttemptRequest)
                return r.Order==e.AttemptOrder&&r.MemberIdentity!=null&&r.MemberIdentity.GroupId==e.Issuance.GroupId&&r.MemberIdentity.OrderRevision==e.Stamp.Revision&&r.MemberIdentity.Incarnation==e.Incarnation&&r.GoalLocation.Equals(e.AttemptLocation);
            if(actor.InstalledRequest==e.AttemptRequest)
                return e.RouteRequest==e.AttemptRequest&&e.RouteOrder==e.AttemptOrder&&actor.InstalledGroupId==e.Issuance.GroupId&&actor.InstalledRevision==e.Stamp.Revision&&actor.InstalledOrder==e.AttemptOrder&&actor.GoalLocation.Equals(e.AttemptLocation);
            if(actor.InstalledGroupId==e.Issuance.GroupId&&actor.InstalledRevision==e.Stamp.Revision&&actor.InstalledRequest>e.AttemptRequest)return false;
            // Failure slots on NavUnit are a mutable last-result cache. A later accepted
            // replacement legitimately clears them, while this immutable attempt proof
            // remains the only evidence for the old active Attack chase.
            return TerminalProofMatches(e,actor);
        }
        private void ValidateTacticalQueues()
        {
            var nav=navigation.CaptureState();var pending=nav.PendingRequestIndices.Select(i=>nav.Requests[i]).ToDictionary(r=>r.Entity);var orders=nav.Orders.ToDictionary(o=>o.Entity,o=>o.Order);
            var issuanceRows=tacticalQueues.Values.SelectMany(q=>q.Deferred.Concat(q.Active==null?Enumerable.Empty<QueueIssuance>():new[]{q.Active.Issuance}).Concat(q.Pending==null?Enumerable.Empty<QueueIssuance>():new[]{q.Pending.Issuance})).GroupBy(i=>i.Id).Select(g=>g.First()).ToArray();
            foreach(var i in issuanceRows){var c=i.Command;var g=nav.Groups.SingleOrDefault(g=>g.GroupId==i.GroupId);if(i.GroupId>nav.GroupSequence||g!=null&&(g.OrderRevision!=i.Revision||g.CommandSequence!=c.Sequence||g.IssuedTick!=i.Tick||g.OwnerId!=c.PlayerId||g.Kind!=c.Kind||g.Origin!=c.Origin||g.Source!=c.Source||g.JobId!=c.JobId||g.ActionId!=c.ActionId||g.TargetId!=c.TargetId||!g.OriginalGoal.Equals(c.Target)||!g.OriginalLocation.Equals(c.TargetLocation)))throw new ArgumentException("Invalid issuance root/provenance.");}
            var rootRefs=issuanceRows.Where(i=>i.GroupId>0).Select(i=>new{Id=i.Id,Root=i.GroupId}).Concat(tacticalQueues.Values.Where(q=>q.Completion!=null).Select(q=>new{Id=q.Completion.IssuanceId,Root=q.Completion.GroupId}));
            if(rootRefs.GroupBy(x=>x.Root).Any(g=>g.Select(x=>x.Id).Distinct().Count()!=1))throw new ArgumentException("Distinct issuances alias a root.");
            foreach(var row in tacticalQueues){
                var u=units[row.Key];var q=row.Value;navigation.Crowd.TryGet(u.Id,out var actor);
                if(q.Deferred.Count>profile.UnitOrderQueueLimit||q.Deferred.Select(i=>i.Id).Distinct().Count()!=q.Deferred.Count||!q.Deferred.Select(i=>i.Id).SequenceEqual(q.Deferred.Select(i=>i.Id).OrderBy(id=>id))||q.Deferred.Any(i=>!i.Command.EntityIds.Contains(u.Id)||OwnerFor(i.Command.PlayerId)!=u.Owner))throw new ArgumentException("Invalid deferred FIFO.");
                void StampBinding(PlayableOrderStamp o){if(o==null||o.UnitId!=u.Id||o.Owner!=u.Owner||o.Generation!=navigation.Generation||o.Revision<1||o.Revision>=nextOrderRevision||o.Sequence<1||o.Tick<0||o.Tick>Tick||!QueueKind(o.Kind)||
                    (o.Origin==PlayableOrderOrigin.Ai?(string.IsNullOrWhiteSpace(o.Source)||o.JobId<1||o.ActionId<1):(o.Source!=null||o.JobId!=0||o.ActionId!=0)))throw new ArgumentException("Invalid queue order stamp.");}
                void Execution(QueueExecution e,bool waiting){
                    if(e==null)return;StampBinding(e.Stamp);var i=e.Issuance;var c=i.Command;
                    if(!c.EntityIds.Contains(u.Id)||OwnerFor(c.PlayerId)!=u.Owner||e.Stamp.Sequence!=c.Sequence||e.Stamp.Tick!=i.Tick||e.Stamp.Kind!=c.Kind||e.Stamp.Origin!=c.Origin||e.Stamp.Source!=c.Source||e.Stamp.JobId!=c.JobId||e.Stamp.ActionId!=c.ActionId||e.Incarnation!=actor.Incarnation||i.GroupId<1||i.GroupId>nav.GroupSequence||e.Stamp.Revision<i.Revision||
                        (c.Kind==PlayableCommandKind.Attack?e.Assigned.HasValue:!e.Assigned.HasValue||!navigation.Crowd.ValidLocation(e.Assigned.Value,actor.Radius)))throw new ArgumentException("Invalid execution identity/surface.");
                    if(e.TerminalFailure!=null&&!TerminalProofMatches(e,actor))throw new ArgumentException("Invalid terminal route failure proof.");
                    if(waiting){var group=navigation.GroupFor(u.Id);var member=group?.Members.Single(m=>m.Entity==u.Id);bool terminal=e.Failed&&TerminalProofMatches(e,actor);bool activeChase=q.Active?.Issuance.Command.Kind==PlayableCommandKind.Attack&&group?.GroupId==q.Active.Issuance.GroupId&&member?.OrderRevision==q.Active.Stamp.Revision;if(c.Kind==PlayableCommandKind.Attack||group==null||(!terminal&&(group.GroupId!=i.GroupId||group.OrderRevision!=i.Revision||group.CommandSequence!=c.Sequence||member.OrderRevision!=e.Stamp.Revision||!member.GoalLocation.Equals(e.Interrupted?e.OverrideLocation:e.Assigned)))||terminal&&!activeChase&&group.GroupId!=i.GroupId||e.AttemptRequest<1||e.AttemptRequest>nav.RequestSequence||e.AttemptOrder<1||!orders.TryGetValue(u.Id,out var watermark)||e.AttemptOrder>watermark)throw new ArgumentException("Invalid pending attempt binding.");bool invalidWait=e.ActivationTick!=-1||e.RouteRequest!=0||e.RouteOrder!=0||e.Arrived||e.Suspended;bool invalidLast=!SameSpatialOrder(u.LastOrder,e.Stamp);bool invalidFailure=e.Failed&&(pending.ContainsKey(u.Id)&&!(terminal&&activeChase)||!(e.Interrupted&&actor.InstalledRequest==e.AttemptRequest)&&!FailureMatches(actor,e)&&!terminal);bool invalidRequest=!e.Failed&&(!pending.TryGetValue(u.Id,out var r)||r.Request!=e.AttemptRequest||r.Order!=e.AttemptOrder||r.MemberIdentity==null||r.MemberIdentity.GroupId!=i.GroupId||r.MemberIdentity.OrderRevision!=e.Stamp.Revision||!r.GoalLocation.Equals(e.Interrupted?e.OverrideLocation:e.Assigned)||!r.GoalLocation.Equals(e.AttemptLocation));if(invalidWait||invalidLast||invalidFailure||invalidRequest)throw new ArgumentException("Invalid pending queue execution.");}
                    else if(!e.Interrupted&&(e.Failed||e.ActivationTick<i.Tick||e.ActivationTick>Tick||e.RouteRequest<0||e.RouteRequest>nav.RequestSequence||e.RouteOrder<0||e.Suspended&&actor.Moving||c.Kind!=PlayableCommandKind.Attack&&(e.RouteRequest<1||actor.InstalledGroupId!=i.GroupId||actor.InstalledRevision!=e.Stamp.Revision||actor.InstalledRequest!=e.RouteRequest||actor.InstalledOrder!=e.RouteOrder||!actor.GoalLocation.Equals(e.Assigned)||e.Arrived&&(actor.Outcome!=NavigationOutcome.Arrived||!navigation.Crowd.CompatibleArrival(actor.Location,e.Assigned.Value,actor.Radius)))||
                        c.Kind==PlayableCommandKind.Attack&&!AttackAttemptBound(e,actor,pending,orders,nav.RequestSequence)||
                        u.CurrentOrder==null||u.CurrentOrder.CommandSequence!=c.Sequence||u.CurrentOrder.Kind!=Order(u,i).Kind||!u.CurrentOrder.DestinationLocation.Equals(c.TargetLocation)))throw new ArgumentException("Invalid active queue execution.");
                    if(e.Interrupted&&(!waiting&&(e.ActivationTick<i.Tick||e.ActivationTick>Tick)||e.OverrideLocation.Equals(e.Assigned)||e.Arrived||e.Suspended||!e.OverrideLocation.HasValue||!navigation.Crowd.ValidLocation(e.OverrideLocation.Value,actor.Radius)||e.AttemptRequest<1||e.AttemptRequest>nav.RequestSequence||e.AttemptOrder<1||!orders.TryGetValue(u.Id,out var interruptedOrder)||e.AttemptOrder>interruptedOrder||
                        (pending.TryGetValue(u.Id,out var overrideRequest)?overrideRequest.Request!=e.AttemptRequest||!overrideRequest.GoalLocation.Equals(e.OverrideLocation):(e.Failed&&FailureMatches(actor,e)?false:(actor.InstalledRequest!=e.AttemptRequest||actor.InstalledOrder!=e.AttemptOrder||actor.InstalledGroupId!=i.GroupId||actor.InstalledRevision!=e.Stamp.Revision||!actor.GoalLocation.Equals(e.OverrideLocation))))))throw new ArgumentException("Invalid interrupted execution proof.");
                }
                Execution(q.Active,false);Execution(q.Pending,true);
                if(q.Active!=null&&q.Pending!=null&&q.Active.Issuance.Id==q.Pending.Issuance.Id)throw new ArgumentException("Active/pending alias.");
                if(q.Completion!=null){var c=q.Completion;StampBinding(c.Order);var group=navigation.GroupFor(u.Id);var member=group?.Members.Single(m=>m.Entity==u.Id);if(q.Active!=null||q.Pending!=null||!SameSpatialOrder(c.Order,u.LastOrder)||group==null||group.GroupId!=c.GroupId||member.OrderRevision!=c.Order.Revision||c.Incarnation!=actor.Incarnation||
                    (c.Order.Kind==PlayableCommandKind.Attack?c.TargetId<1||c.OriginalLocation.HasValue||c.AssignedLocation.HasValue:!c.OriginalLocation.HasValue||!c.AssignedLocation.HasValue||!group.OriginalLocation.Equals(c.OriginalLocation)||!member.GoalLocation.Equals(c.AssignedLocation))||c.IssuanceId<1||c.IssuanceId>queueSequence||c.GroupId<0||c.GroupId>nav.GroupSequence||c.Incarnation<1||c.TargetId<0||c.TargetId>=nextId||c.OriginalLocation.HasValue&&!navigation.Crowd.ValidLocation(c.OriginalLocation.Value,0)||c.AssignedLocation.HasValue&&!navigation.Crowd.ValidLocation(c.AssignedLocation.Value,0)||c.ActivationTick<c.Order.Tick||c.CompletionTick<c.ActivationTick||c.CompletionTick>Tick)throw new ArgumentException("Invalid tactical completion.");if(spatialCompletions.TryGetValue(u.Id,out var legacyReceipt)&&(!SameSpatialOrder(c.Order,legacyReceipt.Order)||c.GroupId!=legacyReceipt.GroupId||c.Incarnation!=legacyReceipt.Incarnation||!c.OriginalLocation.Equals(legacyReceipt.OriginalLocation)||!c.AssignedLocation.Equals(legacyReceipt.AssignedLocation)||c.ActivationTick!=legacyReceipt.ActivationTick||c.CompletionTick!=legacyReceipt.CompletionTick))throw new ArgumentException("Contradictory completion representations.");}
            }
        }
    }
}
