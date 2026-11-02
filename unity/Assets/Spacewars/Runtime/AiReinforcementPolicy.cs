using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Detached, stateless decisions. Membership, receipts and regroup phase use
    // the existing registry/mission wire; navigation remains the native authority.
    internal static class AiReinforcementPolicy
    {
        internal const string RegroupReason="reinforcement core lost: safe regroup";
        private static double Distance(NavPoint a,NavPoint b)=>AiOpponentSelector.Distance(a,b);
        internal static bool Available(PlayableEntitySnapshot u)=>u.Health>0&&!u.Held&&!u.Departing&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human;
        internal static PlayableEntitySnapshot[] Core(PlayableAiObservation o,AiArmyState army)=>o.Entities.Where(u=>u.Owner==o.Owner&&Available(u)&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0).OrderBy(u=>u.Id).ToArray();
        internal static bool Viable(PlayableEntitySnapshot[] units,int minimum,PlayableProfile p,AiProfile ai)=>units.Length>=minimum&&units.Sum(u=>PlayableUnitRules.HealthRatio(p,u))>=minimum*ai.Value("safeReinforcements.coreHealthShare");
        internal static long PreferredReceiver(PlayableAiObservation o,AiArmyRegistry registry,AiArmyState current,PlayableEntitySnapshot unit,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge)
        {
            var choices=registry.Capture().Armies.Where(a=>a.Id==current.Id||a.TacticalOwner!=current.TacticalOwner&&AiArmyPlanner.IsArmyPolicy(a.TacticalOwner)&&(a.Phase==AiArmyPhase.Advancing||a.Phase==AiArmyPhase.Engaging))
                .Select(a=>new{Army=a,Core=ReceivingCore(o,a,p,ai,knowledge)}).Where(x=>x.Core.Length>0).ToArray();
            var reachable=choices.SelectMany(x=>o.RouteProofs.Where(r=>r.UnitId==unit.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&x.Core.Any(c=>c.Id==r.TargetId)&&AiTacticalExecutor.SafeRoute(o,r,p,knowledge))
                .Select(r=>new{Id=x.Army.Id,Length=r.Path.Zip(r.Path.Skip(1),(a,b)=>Distance(a,b)).Sum()+Distance(r.Origin,r.Path.First())})).OrderBy(x=>x.Length).ThenBy(x=>x.Id).FirstOrDefault();
            return reachable?.Id??choices.OrderBy(x=>x.Army.Id).Select(x=>x.Army.Id).FirstOrDefault();
        }
        internal static PlayableEntitySnapshot[] ReceivingCore(PlayableAiObservation o,AiArmyState army,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge=null)
        {
            if(army.Phase==AiArmyPhase.Disbanded||army.Phase==AiArmyPhase.Regrouping||army.Phase==AiArmyPhase.Retreating||army.Phase==AiArmyPhase.Recovering)return Array.Empty<PlayableEntitySnapshot>();
            var core=Core(o,army);
            foreach(var center in core)
            {
                var cluster=core.Where(u=>Distance(u.Position,center.Position)<=ai.Value("safeReinforcements.coreRadius")).ToArray();
                if(!Viable(cluster,army.MinimumViableForce,p,ai))continue;
                var threats=o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Health>0&&o.Vision!=null&&o.Vision.IsVisible(e.Position)&&cluster.Any(u=>AiRosterCatalog.Initial.Targets(e.Kind,u.Kind==PlayableEntityKind.Hawk?"air":"ground")&&Distance(e.Position,u.Position)<=PlayableUnitRules.Range(p,e.Kind,e.Upgraded)+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,e.Kind))).ToArray();
                if(AiDefensePlanner.Force(threats,p,ai)>AiDefensePlanner.Force(cluster,p,ai)*ai.Value("safeReinforcements.maximumThreatRatio"))continue;
                // An anchor under known compatible turret fire is not a safe join side.
                var safe=cluster.Where(u=>AiStaticDefenseSafety.Withdrawal(o,p,u,new[]{u.Position},knowledge)).ToArray();
                if(Viable(safe,army.MinimumViableForce,p,ai))return safe;
            }
            return Array.Empty<PlayableEntitySnapshot>();
        }
        internal static bool AppliedAssembly(PlayableAiObservation o,AiArmyState army,PlayableEntitySnapshot unit,IEnumerable<PlayableAiTraceRecord> records)
        {
            var stamp=unit.OrderStamp;
            return stamp!=null&&stamp.UnitId==unit.Id&&stamp.Owner==o.Owner&&stamp.Generation==o.Generation&&stamp.Tick>=army.CreatedTick&&stamp.Tick<=o.Tick&&
                stamp.Origin==PlayableOrderOrigin.Ai&&stamp.Kind==PlayableCommandKind.Move&&stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&stamp.ActionId>0&&stamp.JobId==stamp.ActionId&&
                (unit.CurrentOrder==null&&unit.NavigationOutcome==NavigationOutcome.Arrived||unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&unit.CurrentOrder.CommandSequence==stamp.Sequence&&unit.CurrentOrder.IssuedTick==stamp.Tick)&&
                (records??Array.Empty<PlayableAiTraceRecord>()).Any(r=>r.Policy==army.TacticalOwner&&r.Kind==stamp.Kind&&r.OwnerId==o.OwnerId&&r.SourceIdentity==stamp.Source&&r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence&&r.ApplicationTick==stamp.Tick&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ReceiptIdentity?.Generation==o.Generation&&r.ReceiptIdentity.OwnerId==o.OwnerId);
        }
        internal static bool OwnedJoiningOrder(PlayableAiObservation o,AiArmyState army,PlayableEntitySnapshot unit,IEnumerable<PlayableAiTraceRecord> records)
        {
            var order=unit.CurrentOrder;var stamp=unit.OrderStamp;
            var kind=order?.Kind==PlayableTacticalOrderKind.Follow?PlayableCommandKind.Follow:order?.Kind==PlayableTacticalOrderKind.Move?PlayableCommandKind.Move:(PlayableCommandKind?)null;
            return kind.HasValue&&stamp!=null&&stamp.Kind==kind.Value&&stamp.UnitId==unit.Id&&stamp.Owner==o.Owner&&stamp.Generation==o.Generation&&stamp.Tick>=army.CreatedTick&&stamp.Tick<=o.Tick&&
                stamp.Origin==PlayableOrderOrigin.Ai&&stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&stamp.ActionId>0&&stamp.JobId==stamp.ActionId&&
                order.Owner==o.Owner&&order.Generation==o.Generation&&order.UnitId==unit.Id&&order.CommandSequence==stamp.Sequence&&order.IssuedTick==stamp.Tick&&
                (records??Array.Empty<PlayableAiTraceRecord>()).Any(r=>r.Policy==army.TacticalOwner&&r.Kind==kind.Value&&r.OwnerId==o.OwnerId&&r.SourceIdentity==stamp.Source&&r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence&&r.ApplicationTick==stamp.Tick&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ReceiptIdentity?.Generation==o.Generation&&r.ReceiptIdentity.OwnerId==o.OwnerId);
        }
        internal static bool AtHome(PlayableAiObservation o,PlayableEntitySnapshot unit,PlayableProfile p,AiKnowledgeState knowledge=null)=>o.RouteProofs.Any(r=>r.UnitId==unit.Id&&Home(o,r)&&AiTacticalExecutor.SafeRoute(o,r,p,knowledge)&&Distance(unit.Position,r.Goal)<=p.Navigation.ArrivalTolerance+PlayableUnitRules.Radius(p,unit.Kind));
        internal static bool Home(PlayableAiObservation o,PlayableRouteProof route)=>route.Kind==PlayableRouteTargetKind.FriendlyAnchor&&o.Buildings.Any(b=>b.Id==route.TargetId&&b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost||b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.RepairTower));
        internal static PlayableAiAction Gather(PlayableAiObservation o,AiArmyState army,PlayableProfile p,long id,IEnumerable<PlayableAiTraceRecord> records,AiKnowledgeState knowledge=null,long regroupTick=0)
        {
            var members=o.Entities.Where(u=>u.Owner==o.Owner&&Available(u)&&army.Members.Contains(u.Id)).OrderBy(u=>u.Id).ToArray();
            // Stop the old stream as one ordinary command before serial escape
            // admissions. A low-ID core must not leave followers chasing its old
            // battle while their safe home leg waits for shared attention.
            var stale=members.Where(u=>army.Reinforcements.Contains(u.Id)&&OwnedJoiningOrder(o,army,u,records)&&
                (u.CurrentOrder.Kind==PlayableTacticalOrderKind.Follow||u.CurrentOrder.IssuedTick<regroupTick)).Select(u=>u.Id).ToArray();
            if(stale.Length>0)return new PlayableAiAction(id,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Stop,stale,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            // Prefer one mutually reachable home, then the widest safe rendezvous.
            // Deterministic IDs avoid separate nearest-home streams and oscillation.
            var home=o.RouteProofs.Where(r=>members.Any(u=>u.Id==r.UnitId)&&Home(o,r)&&(AiTacticalExecutor.SafeRoute(o,r,p,knowledge)||AiTacticalExecutor.WithdrawalRoute(o,r,p)))
                .GroupBy(r=>r.TargetId).OrderByDescending(g=>g.Select(r=>r.UnitId).Distinct().Count()).ThenBy(g=>g.Key).FirstOrDefault();
            bool Exposed(PlayableEntitySnapshot u)=>!AiTacticalExecutor.SafePathForUnit(o,u.Position,new[]{u.Position},PlayableUnitRules.Radius(p,u.Kind),p,u.Kind==PlayableEntityKind.Hawk)||!AiStaticDefenseSafety.Withdrawal(o,p,u,new[]{u.Position},knowledge);
            foreach(var unit in members.OrderByDescending(Exposed).ThenBy(u=>u.CurrentOrder!=null).ThenByDescending(u=>army.Reinforcements.Contains(u.Id)).ThenBy(u=>u.Id))
            {
                var route=o.RouteProofs.Where(r=>r.UnitId==unit.Id&&Home(o,r)&&(home==null||r.TargetId==home.Key)&&(AiTacticalExecutor.SafeRoute(o,r,p,knowledge)||AiTacticalExecutor.WithdrawalRoute(o,r,p)&&AiStaticDefenseSafety.Withdrawal(o,p,unit,r.Path,knowledge)))
                    .OrderBy(r=>r.Path.Zip(r.Path.Skip(1),(a,b)=>Distance(a,b)).Sum()+Distance(r.Origin,r.Path.First())).ThenBy(r=>r.TargetId).FirstOrDefault();
                if(route==null)
                {
                    if(unit.CurrentOrder!=null)return new PlayableAiAction(id,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Stop,new[]{unit.Id},seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                    continue;
                }
                if(unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&unit.CurrentOrder.Destination.Equals(route.Goal)||AtHome(o,unit,p,knowledge)&&AppliedAssembly(o,army,unit,records))continue;
                if(AiTacticalExecutor.HasLivePendingMove(unit,route.Goal))continue;
                return new PlayableAiAction(id,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Move,new[]{unit.Id},route.Goal,parentId:route.TargetId,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            }
            return null;
        }
    }
}
