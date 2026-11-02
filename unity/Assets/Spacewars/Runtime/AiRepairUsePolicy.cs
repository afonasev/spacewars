using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // One bounded recovery client per tower. Its memo is stored in the army's
    // existing saved decision dictionary; no unsaved callback or separate clock.
    internal static class AiRepairUsePolicy
    {
        const string Prefix="repair-use:";
        internal static bool Parse(string key,out int unit,out int tower,out long action)
        {
            unit=tower=0;action=0;var parts=key.Split(':');
            return (parts.Length==4||parts.Length==5&&long.TryParse(parts[4],out var sequence)&&sequence>0)&&parts[0]=="repair-use"&&int.TryParse(parts[1],out unit)&&unit>0&&int.TryParse(parts[2],out tower)&&tower>0&&long.TryParse(parts[3],out action)&&action>0;
        }
        internal static HashSet<int> Clients(IReadOnlyDictionary<string,long> memory)=>new HashSet<int>(memory.Keys.Where(k=>Parse(k,out _,out _,out _)).Select(k=>{Parse(k,out var unit,out _,out _);return unit;}));
        internal static void Returned(IDictionary<string,long> memory,int unit)
        {foreach(var key in memory.Keys.Where(k=>Parse(k,out var id,out _,out _)&&id==unit||k=="repair-return:"+unit).ToArray())memory.Remove(key);}
        internal static bool CanFinishReturn(IReadOnlyDictionary<string,long> memory,PlayableEntitySnapshot unit)
            =>unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&memory.Any(x=>Parse(x.Key,out var id,out _,out _)&&id==unit.Id&&x.Key.Split(':').Length==5&&unit.CurrentOrder.IssuedTick>=x.Value);
        internal static void Commit(IDictionary<string,long> memory,PlayableAiAction a,long tick)
        {
            foreach(var key in memory.Keys.Where(k=>Parse(k,out var u,out _,out _)&&a.EntityIds.Contains(u)).ToArray())memory.Remove(key);
            memory[Prefix+a.EntityIds.Single()+":"+a.ParentId+":"+a.ActionId]=tick;
        }
        internal static void Receipt(IDictionary<string,long> memory,PlayableAiTraceRecord receipt)
        {
            foreach(var key in memory.Keys.Where(k=>Parse(k,out _,out _,out var local)&&local==receipt.ActionId).ToArray())
            {
                long started=memory[key];Parse(key,out var unit,out var tower,out var action);memory.Remove(key);
                if(receipt.Status==PlayableAiDeliveryStatus.Applied&&receipt.CommandSequence>0)memory[Prefix+unit+":"+tower+":"+action+":"+receipt.CommandSequence]=started;
            }
        }
        private static bool Tower(PlayableBuildingSnapshot b,PlayableOwner owner)=>b.Owner==owner&&b.Kind==PlayableBuildingKind.RepairTower&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true;
        internal static bool IsAction(PlayableAiObservation o,PlayableAiAction a)=>a.Kind==PlayableCommandKind.Move&&a.EntityIds.Count==1&&o.Buildings.Any(b=>b.Id==a.ParentId&&Tower(b,o.Owner));
        internal static bool Pending(IReadOnlyDictionary<string,long> memory,PlayableAiAction action,long localAction,out long started)
        {
            started=0;
            return action!=null&&action.Kind==PlayableCommandKind.Move&&action.EntityIds.Count==1&&action.ParentId>0&&localAction>0&&
                memory.TryGetValue(Prefix+action.EntityIds[0]+":"+action.ParentId+":"+localAction,out started);
        }
        internal static bool GuardMission(AiArmyState army)=>army!=null&&army.Role==AiArmyRole.MobileDefense&&AiArmyPlanner.IsArmyPolicy(army.TacticalOwner)&&
            army.Objective!=null&&army.Objective.StartsWith(((int)PlayableRouteTargetKind.FriendlyAnchor)+":",StringComparison.Ordinal);
        internal static bool QuietGuard(AiArmyState army)=>GuardMission(army)&&(army.Phase==AiArmyPhase.Staging||army.Phase==AiArmyPhase.Advancing);
        private static bool Eligible(AiArmyState army)=>army.Phase==AiArmyPhase.Advancing||army.Phase==AiArmyPhase.Engaging||QuietGuard(army)||
            army.Phase==AiArmyPhase.Staging&&AiArmyPlanner.IsArmyPolicy(army.TacticalOwner)&&
            (army.Role==AiArmyRole.Attack||army.Role==AiArmyRole.Raid||army.Role==AiArmyRole.AlliedSupport);
        internal static bool CanDeliver(PlayableAiObservation o,PlayableProfile p,AiProfile ai,IReadOnlyDictionary<string,long> memory,PlayableAiAction action,long localAction,AiArmyState army)
        {
            if(o==null||action==null||army==null||action.PlayerId!=o.OwnerId||action.Generation!=o.Generation||
                action.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity||!Pending(memory,action,localAction,out long started)||
                started>o.Tick||o.Tick-started>=AiProfile.SecondsToTicks(ai.Value("positionDevelopment.repairWaitSeconds"),30)||
                army.OwnerId!=o.OwnerId||!AiArmyPlanner.IsArmyPolicy(army.TacticalOwner)||!Eligible(army))return false;
            int id=action.EntityIds[0];
            if(id==army.LeaderId||!army.Members.Contains(id)||!army.Reinforcements.Contains(id))return false;
            var unit=o.Entities.FirstOrDefault(u=>u.Id==id&&u.Owner==o.Owner&&u.Health>0&&!u.Held&&!u.Departing&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human);
            var tower=o.Buildings.FirstOrDefault(b=>b.Id==action.ParentId&&Tower(b,o.Owner));
            return unit!=null&&tower!=null&&PlayableUnitRules.HealthRatio(p,unit)<ai.Value("positionDevelopment.repairReturnHealth")&&
                DistanceToTower(action.Target,tower,p)&&((tower.Healing?.TargetId??0)==0||tower.Healing?.TargetId==id);
        }
        private static bool DistanceToTower(NavPoint target,PlayableBuildingSnapshot tower,PlayableProfile p)
            =>AiPositionDevelopmentPlanner.Distance(target,tower.Position)<=AiPositionDevelopmentPlanner.Range(p,PlayableBuildingKind.RepairTower);
        internal static HashSet<int> Recovering(PlayableAiObservation o,PlayableProfile p,AiProfile ai,IReadOnlyDictionary<string,long> memory,string policy,IEnumerable<PlayableAiTraceRecord> records,bool includeRecovered=false,bool includeExpired=false)
        {
            var result=new HashSet<int>();long horizon=AiProfile.SecondsToTicks(ai.Value("positionDevelopment.repairWaitSeconds"),30);
            foreach(var entry in memory)
            {
                if(!Parse(entry.Key,out var unit,out var tower,out var action)||entry.Value>o.Tick||!includeExpired&&o.Tick-entry.Value>=horizon)continue;
                var u=o.Entities.FirstOrDefault(e=>e.Id==unit&&e.Owner==o.Owner&&e.Health>0);
                var b=o.Buildings.FirstOrDefault(b=>b.Id==tower&&Tower(b,o.Owner));if(u==null||b==null||u.Held||u.OrderStamp?.Origin==PlayableOrderOrigin.Human||!includeRecovered&&PlayableUnitRules.HealthRatio(p,u)>=ai.Value("positionDevelopment.repairReturnHealth"))continue;
                var parts=entry.Key.Split(':');
                if(parts.Length!=5||!long.TryParse(parts[4],out var command))continue;
                // The saved action is policy-local. Authority records/stamps use a
                // global action ID; the terminal callback binds the local action
                // to its unique command sequence before this observation.
                var receipt=(records??Array.Empty<PlayableAiTraceRecord>()).LastOrDefault(r=>r.Policy==policy&&r.OwnerId==o.OwnerId&&r.ReceiptIdentity?.OwnerId==o.OwnerId&&r.ReceiptIdentity.Generation==o.Generation&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.Kind==PlayableCommandKind.Move&&r.SourceIdentity==PlayableAiOpeningComposition.SourceIdentity&&r.CommandSequence==command&&r.ApplicationTick>=entry.Value&&r.ApplicationTick<=o.Tick);
                if(receipt==null)continue;
                var queued=u.Queue?.Pending;
                bool pending=u.Queue?.PendingAiRouteViable==true&&queued!=null&&queued.AiActionId==receipt.ActionId&&queued.CommandSequence==command&&queued.Kind==PlayableCommandKind.Move&&queued.Anchor.HasValue&&AiPositionDevelopmentPlanner.Distance(queued.Anchor.Value.Position,b.Position)<=AiPositionDevelopmentPlanner.Range(p,b.Kind);
                var stamp=u.OrderStamp;var order=u.CurrentOrder;var completion=u.Completion??u.Queue?.Completion?.SpatialReceipt;
                bool installed=stamp!=null&&stamp.ActionId==receipt.ActionId&&stamp.UnitId==u.Id&&stamp.Owner==o.Owner&&stamp.Generation==o.Generation&&stamp.Origin==PlayableOrderOrigin.Ai&&stamp.Kind==PlayableCommandKind.Move&&stamp.Source==receipt.SourceIdentity&&stamp.JobId==receipt.ActionId&&stamp.Sequence==command&&stamp.Tick==receipt.ApplicationTick&&
                    (order!=null?order.Owner==o.Owner&&order.Generation==o.Generation&&order.UnitId==u.Id&&order.Kind==PlayableTacticalOrderKind.Move&&order.CommandSequence==stamp.Sequence&&order.IssuedTick==stamp.Tick&&AiPositionDevelopmentPlanner.Distance(order.Destination,b.Position)<=AiPositionDevelopmentPlanner.Range(p,b.Kind):
                    completion!=null&&completion.Order.UnitId==u.Id&&completion.Order.Owner==o.Owner&&completion.Order.Generation==o.Generation&&completion.Order.Sequence==command&&completion.Order.ActionId==receipt.ActionId&&AiPositionDevelopmentPlanner.Distance(completion.OriginalLocation.Position,b.Position)<=AiPositionDevelopmentPlanner.Range(p,b.Kind));
                if(pending||installed&& (u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move||AiPositionDevelopmentPlanner.Distance(u.Position,b.Position)<=AiPositionDevelopmentPlanner.Range(p,b.Kind)))result.Add(unit);
            }
            return result;
        }
        internal static bool OwnedCoreOrder(PlayableAiObservation o,PlayableEntitySnapshot u,string policy,long missionStarted,IEnumerable<PlayableAiTraceRecord> records,bool allowPriorDefense=false)
        {
            var stamp=u.OrderStamp;var order=u.CurrentOrder;
            if(u.Owner!=o.Owner||u.Health<=0||u.Held||u.Departing||stamp==null||order==null||stamp.Origin!=PlayableOrderOrigin.Ai||
                stamp.UnitId!=u.Id||stamp.Owner!=o.Owner||stamp.Generation!=o.Generation||stamp.Source!=PlayableAiOpeningComposition.SourceIdentity||stamp.ActionId<=0||stamp.JobId!=stamp.ActionId||
                order.UnitId!=u.Id||order.Owner!=o.Owner||order.Generation!=o.Generation||order.CommandSequence!=stamp.Sequence||order.IssuedTick!=stamp.Tick)return false;
            var kind=order.Kind==PlayableTacticalOrderKind.Move?PlayableCommandKind.Move:order.Kind==PlayableTacticalOrderKind.Attack?PlayableCommandKind.Attack:order.Kind==PlayableTacticalOrderKind.AttackMove?PlayableCommandKind.AttackMove:order.Kind==PlayableTacticalOrderKind.Follow?PlayableCommandKind.Follow:(PlayableCommandKind?)null;
            return kind.HasValue&&stamp.Kind==kind&&(records??Array.Empty<PlayableAiTraceRecord>()).Any(r=>
                (r.Policy==policy&&stamp.Tick>=missionStarted||allowPriorDefense&&stamp.Tick<missionStarted&&r.Policy==AiDefensePlanner.Policy)&&r.OwnerId==o.OwnerId&&r.SourceIdentity==stamp.Source&&
                r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.Kind==kind&&r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence&&r.ApplicationTick==stamp.Tick&&r.ApplicationTick<=o.Tick&&r.ReceiptIdentity?.OwnerId==o.OwnerId&&r.ReceiptIdentity.Generation==o.Generation);
        }
        internal static bool OwnedPendingWithdrawal(PlayableAiObservation o,PlayableEntitySnapshot unit,string policy,IEnumerable<PlayableAiTraceRecord> records)
        {
            var pending=unit.Queue?.Pending;var stamp=unit.OrderStamp;
            if(unit.Queue?.PendingAiRouteViable!=true||pending?.Kind!=PlayableCommandKind.Move||!pending.Anchor.HasValue||stamp==null||unit.Held||unit.Departing||unit.Owner!=o.Owner||unit.Health<=0||
                stamp.UnitId!=unit.Id||stamp.Owner!=o.Owner||stamp.Generation!=o.Generation||stamp.Origin!=PlayableOrderOrigin.Ai||stamp.Kind!=PlayableCommandKind.Move||stamp.Source!=PlayableAiOpeningComposition.SourceIdentity||
                stamp.ActionId<=0||stamp.JobId!=stamp.ActionId||pending.AiActionId!=stamp.ActionId||pending.CommandSequence!=stamp.Sequence||pending.ActivationRevision!=stamp.Revision)return false;
            return (records??Array.Empty<PlayableAiTraceRecord>()).Any(r=>r.Policy==policy&&r.OwnerId==o.OwnerId&&r.SourceIdentity==stamp.Source&&r.Kind==PlayableCommandKind.Move&&
                r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence&&r.ApplicationTick==stamp.Tick&&r.ApplicationTick<=o.Tick&&r.ReceiptIdentity?.OwnerId==o.OwnerId&&r.ReceiptIdentity.Generation==o.Generation);
        }
        internal static void StartHealedReturns(PlayableAiObservation o,PlayableProfile p,AiProfile ai,IDictionary<string,long> memory,string policy,IEnumerable<PlayableAiTraceRecord> records)
        {
            var snapshot=memory as IReadOnlyDictionary<string,long>??new Dictionary<string,long>(memory);
            foreach(int id in Recovering(o,p,ai,snapshot,policy,records,includeRecovered:true))
                if(o.Entities.Any(u=>u.Id==id&&PlayableUnitRules.HealthRatio(p,u)>=ai.Value("positionDevelopment.repairReturnHealth"))&&!memory.ContainsKey("repair-return:"+id))memory.Add("repair-return:"+id,o.Tick);
        }
        internal static bool HasLiveRecoveryBudget(PlayableAiObservation o,AiProfile ai,IReadOnlyDictionary<string,long> memory,int? client=null)
            =>memory.Any(x=>Parse(x.Key,out var unit,out _,out _)&&(!client.HasValue||unit==client.Value)&&
                (memory.TryGetValue("repair-return:"+unit,out var returned)?returned:x.Value)<=o.Tick&&
                o.Tick-(memory.TryGetValue("repair-return:"+unit,out returned)?returned:x.Value)<AiProfile.SecondsToTicks(ai.Value("positionDevelopment.repairWaitSeconds"),30)&&
                o.Entities.Any(u=>u.Id==unit&&u.Owner==o.Owner&&u.Health>0&&!u.Held&&!u.Departing&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human));
        internal static void Prune(PlayableAiObservation o,AiProfile ai,IDictionary<string,long> memory)
        {
            foreach(var key in memory.Keys.Where(k=>k.StartsWith(Prefix,StringComparison.Ordinal)).ToArray())
                if(!Parse(key,out var unit,out var tower,out _)||!o.Entities.Any(e=>e.Id==unit&&e.Owner==o.Owner&&e.Health>0&&e.OrderStamp?.Origin!=PlayableOrderOrigin.Human))memory.Remove(key);
        }
        internal static PlayableAiAction Propose(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiArmyState army,long actionId,IReadOnlyDictionary<string,long> memory,string policy,IEnumerable<PlayableAiTraceRecord> records,Func<IEnumerable<PlayableEntitySnapshot>,bool> viable,AiKnowledgeState knowledge=null)
        {
            if(!Eligible(army))return null;
            var own=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&!u.Held&&!u.Departing&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human).ToArray();
            var recovering=Recovering(o,p,ai,memory,policy,records);
            // A wounded assigned reinforcement need not reach the front before
            // it can repair. It does not consume a core body when detached.
            var core=own.Where(u=>!army.Reinforcements.Contains(u.Id)&&!recovering.Contains(u.Id)).ToArray();
            if(core.Count(u=>AiRosterCatalog.Initial.LineWeight(u.Kind)>0)<army.MinimumViableForce)return null;
            foreach(var u in own.Where(u=>u.Id!=army.LeaderId&&core.Count(other=>other.Id!=u.Id&&AiRosterCatalog.Initial.LineWeight(other.Kind)>0)>=army.MinimumViableForce&&
                viable(core.Where(other=>other.Id!=u.Id))&&PlayableUnitRules.HealthRatio(p,u)<ai.Value("positionDevelopment.repairHealth")&&!memory.Keys.Any(k=>Parse(k,out var id,out _,out _)&&id==u.Id))
                .OrderByDescending(u=>PlayableUnitRules.Cost(p,u.Kind)*(1-PlayableUnitRules.HealthRatio(p,u))).ThenBy(u=>u.Id))
            {
                var routes=o.RouteProofs.Where(r=>r.UnitId==u.Id&&o.Buildings.Any(b=>b.Id==r.TargetId&&Tower(b,o.Owner))&&AiTacticalExecutor.WithdrawalRoute(o,r,p)&&AiStaticDefenseSafety.Withdrawal(o,p,u,r.Path,knowledge)).Select(r=>new{Route=r,Tower=o.Buildings.FirstOrDefault(b=>b.Id==r.TargetId&&Tower(b,o.Owner))})
                    .Where(x=>x.Tower!=null&&AiPositionDevelopmentPlanner.Distance(x.Route.Goal,x.Tower.Position)<=AiPositionDevelopmentPlanner.Range(p,PlayableBuildingKind.RepairTower)&&
                        (x.Tower.Healing?.TargetId??0)==0&&!o.Entities.Any(other=>other.Id!=u.Id&&other.Owner==o.Owner&&other.Health>0&&PlayableUnitRules.HealthRatio(p,other)<ai.Value("positionDevelopment.repairReturnHealth")&&
                            (AiPositionDevelopmentPlanner.Distance(other.Position,x.Tower.Position)<=AiPositionDevelopmentPlanner.Range(p,PlayableBuildingKind.RepairTower)||other.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&AiPositionDevelopmentPlanner.Distance(other.CurrentOrder.Destination,x.Tower.Position)<=AiPositionDevelopmentPlanner.Range(p,PlayableBuildingKind.RepairTower))))
                    .Select(x=>new{x.Route,x.Tower,Seconds=Length(x.Route)/Math.Max(double.Epsilon,PlayableUnitRules.Speed(p,u.Kind))+(p.RepairTowerHealthPerSecond>0?Math.Max(0,(ai.Value("positionDevelopment.repairReturnHealth")-PlayableUnitRules.HealthRatio(p,u))*u.MaxHealth)/p.RepairTowerHealthPerSecond:double.PositiveInfinity)})
                    .Where(x=>x.Seconds<=ai.Value("positionDevelopment.repairWaitSeconds")).OrderBy(x=>x.Seconds).ThenBy(x=>x.Tower.Id).FirstOrDefault();
                if(routes==null)continue;
                return new PlayableAiAction(actionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Move,new[]{u.Id},routes.Route.Goal,parentId:routes.Tower.Id,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            }
            return null;
        }
        // Return only a saved, effectively delivered repair client to the actual live
        // original core. A receipt or nearby guard destination is never this identity.
        internal static PlayableAiAction ReturnAssembly(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiArmyState army,long actionId,IReadOnlyDictionary<string,long> memory,string policy,IEnumerable<PlayableAiTraceRecord> records,AiKnowledgeState knowledge=null)
        {
            bool quiet=QuietGuard(army);
            bool organized=army!=null&&army.OwnerId==o.OwnerId&&AiArmyPlanner.IsArmyPolicy(army.TacticalOwner)&&army.Phase!=AiArmyPhase.Disbanded&&
                (army.Role==AiArmyRole.Attack||army.Role==AiArmyRole.Raid||army.Role==AiArmyRole.AlliedSupport);
            if(!quiet&&(!organized||!HasLiveRecoveryBudget(o,ai,memory)))return null;
            var clients=Clients(memory);
            var core=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&!u.Held&&!u.Departing&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human&&
                army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&!clients.Contains(u.Id)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0).ToArray();
            if(core.Length<army.MinimumViableForce)return null;
            double coreHealth=core.Sum(u=>Math.Min(1,PlayableUnitRules.HealthRatio(p,u)));
            if(quiet&&coreHealth<army.MinimumViableForce)return null;
            var delivered=Recovering(o,p,ai,memory,policy,records,includeRecovered:true,includeExpired:true);
            foreach(var unit in o.Entities.Where(u=>clients.Contains(u.Id)&&delivered.Contains(u.Id)&&army.Reinforcements.Contains(u.Id)&&!u.Held&&!u.Departing&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human).OrderBy(u=>u.Id))
            {
                bool expired=memory.Any(x=>Parse(x.Key,out var id,out _,out _)&&id==unit.Id&&o.Tick-x.Value>=AiProfile.SecondsToTicks(ai.Value("positionDevelopment.repairWaitSeconds"),30));
                if(!expired&&PlayableUnitRules.HealthRatio(p,unit)<ai.Value("positionDevelopment.repairReturnHealth"))continue;
                // The repaired client restores the same stored viability threshold;
                // core body count and offensive resumption remain independent gates.
                if(!quiet&&coreHealth+Math.Min(1,PlayableUnitRules.HealthRatio(p,unit))<army.MinimumViableForce)continue;
                var route=o.RouteProofs.Where(r=>r.UnitId==unit.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.Owner==o.Owner&&r.Generation==o.Generation&&r.Tick==o.Tick&&
                    core.Any(c=>c.Id==army.LeaderId&&c.Id==r.TargetId&&r.Target.Equals(PlayableUnitRules.FollowGoal(p,c.Position,c.HullHeading)))&&r.Goal.Equals(r.Target)&&AiTacticalExecutor.SafeRoute(o,r,p,knowledge))
                    .OrderBy(Length).ThenBy(r=>r.TargetId).FirstOrDefault();
                if(route!=null)return new PlayableAiAction(actionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Follow,new[]{unit.Id},targetId:route.TargetId,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            }
            return null;
        }
        internal static bool ReadyForOriginalLeader(PlayableAiObservation o,PlayableProfile p,AiArmyState army,PlayableEntitySnapshot unit,AiKnowledgeState knowledge=null)
        {
            var leader=o.Entities.FirstOrDefault(c=>c.Id==army.LeaderId&&c.Id!=unit.Id&&c.Owner==o.Owner&&c.Health>0&&army.Members.Contains(c.Id)&&!army.Reinforcements.Contains(c.Id)&&AiRosterCatalog.Initial.LineWeight(c.Kind)>0);
            if(leader==null||unit.CurrentOrder?.Kind!=PlayableTacticalOrderKind.Follow||unit.CurrentOrder.TargetId!=leader.Id)return false;
            double spacing=p.FollowDistance+p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,unit.Kind)+PlayableUnitRules.Radius(p,leader.Kind);
            return AiPositionDevelopmentPlanner.Distance(unit.Position,leader.Position)<=spacing&&o.RouteProofs.Any(r=>r.UnitId==unit.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.TargetId==leader.Id&&
                // Ground Follow assembles radially. Its current safe leader-center
                // proof is sufficient even if the separate behind-point answer
                // retains a longer bend; air keeps its exact behind-point binding.
                (r.Target.Equals(PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading))&&r.Goal.Equals(r.Target)||
                    unit.Kind!=PlayableEntityKind.Hawk&&r.Target.Equals(leader.Position))&&
                Length(r)<=spacing&&AiTacticalExecutor.SafeRoute(o,r,p,knowledge));
        }
        private static double Length(PlayableRouteProof r){double result=0;var at=r.Origin;foreach(var q in r.Path){result+=AiPositionDevelopmentPlanner.Distance(at,q);at=q;}return result;}
    }
}
