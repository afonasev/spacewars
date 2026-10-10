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
        private sealed class SpatialExecution
        {
            internal PlayableOrderStamp Order; internal NavPoint OriginalTarget,AssignedGoal; internal NavLocation? OriginalLocation,AssignedLocation;
            internal long GroupId,Incarnation,ActivationTick=-1,RouteRequest,RouteOrder;
        }
        private readonly AuthoritySlotMap<int,SpatialExecution> spatialExecutions=new AuthoritySlotMap<int,SpatialExecution>();
        private readonly AuthoritySlotMap<int,PlayableSpatialCompletion> spatialCompletions=new AuthoritySlotMap<int,PlayableSpatialCompletion>();
        // Callers must project through the issuing owner. No ally/enemy completion data.
        internal PlayableSpatialCompletion SpatialCompletion(int id,PlayableOwner observer)
            {if(!units.TryGetValue(id,out var unit)||unit.Owner!=observer)return null;
            if(tacticalQueues.TryGetValue(id,out var q)&&q.Completion!=null&&q.Completion.Order.Kind!=PlayableCommandKind.Attack&&q.Completion.OriginalLocation.HasValue&&q.Completion.AssignedLocation.HasValue){var c=q.Completion;return c.SpatialReceipt;}
            return spatialCompletions.TryGetValue(id,out var receipt)?receipt:null;}
        private void InvalidateSpatialCompletion(int id)
        {spatialExecutions.Remove(id);spatialCompletions.Remove(id);}
        private void CancelSpatialExecution(int id){spatialExecutions.Remove(id);QueueMovementCancelled(id);}
        private void AdmitSpatialExecution(Unit unit)
        {
            if(unit.CurrentOrder?.Kind!=PlayableTacticalOrderKind.Move||unit.LastOrder?.Kind!=PlayableCommandKind.Move)return;
            var group=navigation.GroupFor(unit.Id);var member=group?.Members.SingleOrDefault(x=>x.Entity==unit.Id);
            if(member==null||!member.HasGoal||!group.OriginalLocation.HasValue||!member.GoalLocation.HasValue)return;
            spatialExecutions[unit.Id]=new SpatialExecution{Order=unit.LastOrder,OriginalTarget=unit.CurrentOrder.Destination,
                AssignedGoal=member.Goal,OriginalLocation=group.OriginalLocation,AssignedLocation=member.GoalLocation,GroupId=group.GroupId,Incarnation=member.Incarnation};
        }
        private static bool MatchesSpatialRequest(SpatialExecution execution,NavigationRequest request)
            =>request.MemberIdentity!=null&&request.MemberIdentity.GroupId==execution.GroupId&&
                request.MemberIdentity.OrderRevision==execution.Order.Revision&&request.MemberIdentity.Incarnation==execution.Incarnation&&
                request.Goal.Equals(execution.AssignedGoal)&&request.GoalLocation.HasValue&&request.GoalLocation.Value.Equals(execution.AssignedLocation.Value);
        private void ObserveSpatialRequest(NavigationRequest request)
        {
            ObserveTacticalRequest(request);
            // Foundation evacuation/internal movement replacement can change the goal
            // without a tactical command. It revokes the interrupted execution only.
            if(spatialExecutions.TryGetValue(request.Entity,out var execution)&&!MatchesSpatialRequest(execution,request))
                spatialExecutions.Remove(request.Entity);
        }
        private void ActivateSpatialExecution(NavigationRequest request)
        {
            ActivateTacticalRoute(request);
            // Only a previously admitted spatial intent can be armed by a successful route.
            // Legacy hydration and internal chase/Follow paths cannot invent evidence.
            if(!spatialExecutions.TryGetValue(request.Entity,out var execution)||!units.TryGetValue(request.Entity,out var unit))return;
            if(!MatchesSpatialRequest(execution,request)||!SameSpatialOrder(unit.LastOrder,execution.Order)){
                spatialExecutions.Remove(request.Entity);return;
            }
            if(execution.ActivationTick<0)execution.ActivationTick=Tick;
            execution.RouteRequest=request.Request;execution.RouteOrder=request.Order;
        }
        private void RefreshSpatialCompletions()
        {
            foreach(var row in spatialExecutions.ToArray()){
                var execution=row.Value;
                if(!units.TryGetValue(row.Key,out var unit)||unit.LastOrder?.Revision!=execution.Order.Revision||
                    !navigation.Crowd.TryGet(row.Key,out var actor)||actor.Incarnation!=execution.Incarnation||actor.Held&&!navigation.IsPending(row.Key)){spatialExecutions.Remove(row.Key);continue;}
                if(navigation.IsPending(row.Key)||actor.Moving)continue;
                if(execution.ActivationTick>=0&&actor.Goal.Equals(execution.AssignedGoal)&&actor.Outcome==NavigationOutcome.Arrived&&execution.AssignedLocation.HasValue&&navigation.Crowd.CompatibleArrival(actor.Location,execution.AssignedLocation.Value,actor.Radius))
                    spatialCompletions[row.Key]=new PlayableSpatialCompletion(execution.Order,execution.GroupId,execution.Incarnation,
                        execution.OriginalLocation.Value,execution.AssignedLocation.Value,execution.ActivationTick,Tick);
                // Blocked/unreachable/rejected/stopped/idle are not completion evidence.
                spatialExecutions.Remove(row.Key);
            }
        }
        private const int SpatialCompletionTag=0x53505231;
        private void WriteSpatialCompletionExtension(BinaryWriter writer)
        {
            writer.Write(SpatialCompletionTag);writer.Write(2);
            Array(writer,spatialExecutions.OrderBy(x=>x.Key).Select(x=>x.Value).ToArray(),x=>{
                WriteOrderStamp(writer,x.Order);writer.Write(x.GroupId);writer.Write(x.Incarnation);WorldWire.Write(writer,x.OriginalTarget);WorldWire.Write(writer,x.AssignedGoal);writer.Write(x.ActivationTick);writer.Write(x.RouteRequest);writer.Write(x.RouteOrder);
            });
            Array(writer,spatialCompletions.OrderBy(x=>x.Key).Select(x=>x.Value).ToArray(),x=>{
                WriteOrderStamp(writer,x.Order);writer.Write(x.GroupId);writer.Write(x.Incarnation);WorldWire.Write(writer,x.OriginalTarget);
                String(writer,x.SurfaceId);writer.Write(x.ActivationTick);writer.Write(x.CompletionTick);
            });
            Array(writer,spatialExecutions.OrderBy(x=>x.Key).ToArray(),row=>{writer.Write(row.Key);GroupOrderWire.WriteLocation(writer,row.Value.OriginalLocation);GroupOrderWire.WriteLocation(writer,row.Value.AssignedLocation);});
            Array(writer,spatialCompletions.OrderBy(x=>x.Key).ToArray(),row=>{writer.Write(row.Key);GroupOrderWire.WriteLocation(writer,row.Value.OriginalLocation);GroupOrderWire.WriteLocation(writer,row.Value.AssignedLocation);});
        }
        private void ReadSpatialCompletionExtension(BinaryReader reader)
        {
            if(reader.BaseStream.Position==reader.BaseStream.Length)return; // Exact pre-A1/A1 v9, no invented evidence.
            if(reader.ReadInt32()!=SpatialCompletionTag)throw new ArgumentException("Unsupported spatial completion extension.");
            int version=reader.ReadInt32();if(version!=1&&version!=2)throw new ArgumentException("Unsupported spatial surface extension.");
            foreach(var x in Array(reader,()=>new SpatialExecution{Order=ReadOrderStamp(reader),GroupId=reader.ReadInt64(),Incarnation=reader.ReadInt64(),OriginalTarget=ReadPoint(reader),AssignedGoal=ReadPoint(reader),ActivationTick=reader.ReadInt64(),RouteRequest=reader.ReadInt64(),RouteOrder=reader.ReadInt64()})){
                if(x.Order==null||spatialExecutions.ContainsKey(x.Order.UnitId))throw new ArgumentException("Invalid spatial execution identity.");spatialExecutions.Add(x.Order.UnitId,x);
            }
            foreach(var x in Array(reader,()=>new PlayableSpatialCompletion(ReadOrderStamp(reader),reader.ReadInt64(),reader.ReadInt64(),ReadPoint(reader),String(reader),reader.ReadInt64(),reader.ReadInt64()))){
                if(x.Order==null||spatialCompletions.ContainsKey(x.Order.UnitId)||spatialExecutions.ContainsKey(x.Order.UnitId))throw new ArgumentException("Invalid spatial completion identity.");spatialCompletions.Add(x.Order.UnitId,x);
            }
            if(version==1){
                if(profile.AuthoredMap!=null&&(spatialExecutions.Count>0||spatialCompletions.Count>0))throw new ArgumentException("Legacy authored receipt is invalid.");
                foreach(var x in spatialExecutions.Values){x.OriginalLocation=new NavLocation(x.OriginalTarget,NavLocation.FlatSurface);x.AssignedLocation=new NavLocation(x.AssignedGoal,NavLocation.FlatSurface);}
                foreach(var id in spatialCompletions.Keys.ToArray()){var x=spatialCompletions[id];var m=navigation.GroupFor(id)?.Members.SingleOrDefault(member=>member.Entity==id);if(m==null)throw new ArgumentException("Missing legacy receipt member.");spatialCompletions[id]=new PlayableSpatialCompletion(x.Order,x.GroupId,x.Incarnation,x.OriginalLocation,new NavLocation(m.Goal,NavLocation.FlatSurface),x.ActivationTick,x.CompletionTick);}
                return;
            }
            int executions=reader.ReadInt32();if(executions!=spatialExecutions.Count)throw new ArgumentException("Surface execution count mismatch.");var seen=new HashSet<int>();
            for(int i=0;i<executions;i++){int id=reader.ReadInt32();if(!seen.Add(id)||!spatialExecutions.TryGetValue(id,out var x))throw new ArgumentException("Surface execution identity mismatch.");x.OriginalLocation=GroupOrderWire.ReadLocation(reader);x.AssignedLocation=GroupOrderWire.ReadLocation(reader);}
            int receipts=reader.ReadInt32();if(receipts!=spatialCompletions.Count)throw new ArgumentException("Surface receipt count mismatch.");seen.Clear();
            for(int i=0;i<receipts;i++){int id=reader.ReadInt32();if(!seen.Add(id)||!spatialCompletions.TryGetValue(id,out var x))throw new ArgumentException("Surface receipt identity mismatch.");var original=GroupOrderWire.ReadLocation(reader);var assigned=GroupOrderWire.ReadLocation(reader);if(!original.HasValue||!assigned.HasValue||!original.Value.Equals(x.OriginalLocation))throw new ArgumentException("Surface receipt anchor mismatch.");spatialCompletions[id]=new PlayableSpatialCompletion(x.Order,x.GroupId,x.Incarnation,original.Value,assigned.Value,x.ActivationTick,x.CompletionTick);}
        }
        private void ValidateSpatialCompletions()
        {
            var checkpoint=navigation.CaptureState();
            var pendingRequests=checkpoint.PendingRequestIndices.Select(i=>checkpoint.Requests[i]).ToDictionary(r=>r.Entity);
            var orderWatermarks=checkpoint.Orders.ToDictionary(o=>o.Entity,o=>o.Order);
            void Validate(PlayableOrderStamp order,long groupId,long incarnation,NavPoint target,long activationTick,bool pending=false){
                if(order==null||order.Kind!=PlayableCommandKind.Move||!units.TryGetValue(order.UnitId,out var unit)||
                    !SameSpatialOrder(unit.LastOrder,order)||!navigation.Crowd.TryGet(unit.Id,out var actor)||actor.Incarnation!=incarnation||actor.Held&&!pending||
                    groupId<1||navigation.GroupFor(unit.Id)?.GroupId!=groupId||navigation.GroupFor(unit.Id)?.Members.Single(m=>m.Entity==unit.Id).OrderRevision!=order.Revision||
                    !navigation.GroupFor(unit.Id).OriginalGoal.Equals(target)||(pending?activationTick!=-1:activationTick<order.Tick||activationTick>Tick)||
                    double.IsNaN(target.X)||double.IsInfinity(target.X)||double.IsNaN(target.Z)||double.IsInfinity(target.Z))throw new ArgumentException("Invalid spatial order binding.");
            }
            foreach(var execution in spatialExecutions.Values){
                Validate(execution.Order,execution.GroupId,execution.Incarnation,execution.OriginalTarget,execution.ActivationTick,execution.ActivationTick<0);
                var unit=units[execution.Order.UnitId];
                if(!execution.OriginalLocation.HasValue||!execution.AssignedLocation.HasValue||!execution.OriginalLocation.Value.Position.Equals(execution.OriginalTarget)||!execution.AssignedLocation.Value.Position.Equals(execution.AssignedGoal)||
                    !navigation.Crowd.ValidLocation(execution.OriginalLocation.Value,0)||!navigation.Crowd.ValidLocation(execution.AssignedLocation.Value,PlayableUnitRules.Radius(profile,unit.Kind))||
                    !navigation.GroupFor(unit.Id).OriginalLocation.Equals(execution.OriginalLocation)||!navigation.GroupFor(unit.Id).Members.Single(m=>m.Entity==unit.Id).GoalLocation.Equals(execution.AssignedLocation))throw new ArgumentException("Invalid execution surfaces.");
                if(!tacticalQueues.ContainsKey(unit.Id)&&(unit.CurrentOrder?.Kind!=PlayableTacticalOrderKind.Move||unit.CurrentOrder.CommandSequence!=execution.Order.Sequence||
                    !unit.CurrentOrder.Destination.Equals(execution.OriginalTarget)))throw new ArgumentException("Invalid active spatial intent.");
                if(execution.ActivationTick<0){
                    if(execution.RouteRequest!=0||execution.RouteOrder!=0||!pendingRequests.TryGetValue(unit.Id,out var request)||
                        !request.Goal.Equals(execution.AssignedGoal)||!request.GoalLocation.Equals(execution.AssignedLocation)||request.MemberIdentity==null||request.MemberIdentity.GroupId!=execution.GroupId||
                        request.MemberIdentity.OrderRevision!=execution.Order.Revision||request.MemberIdentity.Incarnation!=execution.Incarnation)
                        throw new ArgumentException("Invalid pending spatial intent.");
                }else if(execution.RouteRequest<1||execution.RouteRequest>checkpoint.RequestSequence||execution.RouteOrder<1||
                    !orderWatermarks.TryGetValue(unit.Id,out var watermark)||execution.RouteOrder>watermark||
                    (!pendingRequests.ContainsKey(unit.Id)&&execution.RouteOrder!=watermark)||
                    !navigation.Crowd.TryGet(unit.Id,out var actor)||!actor.Goal.Equals(execution.AssignedGoal)||!actor.GoalLocation.Equals(execution.AssignedLocation))
                    throw new ArgumentException("Invalid installed spatial route.");
            }
            foreach(var receipt in spatialCompletions.Values){
                Validate(receipt.Order,receipt.GroupId,receipt.Incarnation,receipt.OriginalTarget,receipt.ActivationTick);
                var member=navigation.GroupFor(receipt.Order.UnitId).Members.Single(m=>m.Entity==receipt.Order.UnitId);
                if(!navigation.Crowd.ValidLocation(receipt.OriginalLocation,0)||!navigation.Crowd.ValidLocation(receipt.AssignedLocation,0)||!navigation.GroupFor(receipt.Order.UnitId).OriginalLocation.Equals(receipt.OriginalLocation)||!member.GoalLocation.Equals(receipt.AssignedLocation)||receipt.CompletionTick<receipt.ActivationTick||receipt.CompletionTick>Tick)
                    throw new ArgumentException("Invalid spatial completion receipt.");
            }
        }
        private static bool SameSpatialOrder(PlayableOrderStamp a,PlayableOrderStamp b)
            =>a!=null&&b!=null&&a.UnitId==b.UnitId&&a.Owner==b.Owner&&a.Generation==b.Generation&&a.Revision==b.Revision&&a.Sequence==b.Sequence&&
                a.Tick==b.Tick&&a.Origin==b.Origin&&a.Kind==b.Kind&&a.Source==b.Source&&a.JobId==b.JobId&&a.ActionId==b.ActionId;
    }
}
