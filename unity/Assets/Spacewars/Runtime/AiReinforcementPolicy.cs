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
        internal const string AssemblyPrefix="regroup-assembly:";

        internal static bool Settled(PlayableEntitySnapshot unit) =>
            unit.CurrentOrder==null && unit.Queue?.Pending==null && unit.Queue?.Active==null &&
            (unit.Queue==null || unit.Queue.Deferred.Count==0) && !unit.Moving;

        // A stationary unit can still own a pending or obsolete joining command.
        // Only the ordinary Stop path may clear it before the mission is released.
        internal static bool NeedsRegroupStop(PlayableAiObservation o,AiArmyState army,PlayableEntitySnapshot unit,
            IEnumerable<PlayableAiTraceRecord> records,long regroupTick) =>
            Available(unit) && (unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow ||
                unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove ||
                unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack && unit.CurrentOrder.IssuedTick<regroupTick ||
                unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move && unit.CurrentOrder.IssuedTick<regroupTick &&
                    army.Reinforcements.Contains(unit.Id) && OwnedJoiningOrder(o,army,unit,records) ||
                OldQueuedJoin(unit.Queue?.Pending) || OldQueuedJoin(unit.Queue?.Active) ||
                unit.Queue?.Deferred.Any(OldQueuedJoin)==true);
        private static bool OldQueuedJoin(PlayableQueuedOrderSnapshot order) =>
            order?.AiActionId>0 && (order.Kind==PlayableCommandKind.AttackMove ||
                order.Kind==PlayableCommandKind.Attack || order.Kind==PlayableCommandKind.Follow);
        private static string AssemblyKey(AiArmyState army,PlayableEntitySnapshot unit)=>AssemblyPrefix+army.Id+":"+unit.Id+":"+unit.OrderStamp?.ActionId+":"+unit.OrderStamp?.Sequence;
        internal static bool RememberedAssembly(AiArmyState army,PlayableEntitySnapshot unit,IReadOnlyDictionary<string,long> memory)=>
            unit.OrderStamp!=null&&memory.TryGetValue(AssemblyKey(army,unit),out long tick)&&tick==unit.OrderStamp.Tick;
        internal static void RememberAssembly(PlayableAiObservation o,AiArmyState army,System.Collections.Generic.IDictionary<string,long> memory,System.Collections.Generic.IEnumerable<PlayableAiTraceRecord> records)
        {
            var units=o.Entities.Where(u=>u.Owner==o.Owner&&Available(u)&&army.Members.Contains(u.Id)).ToArray();
            var live=new System.Collections.Generic.HashSet<string>(units.Select(u=>AssemblyKey(army,u)));
            foreach(var key in memory.Keys.Where(k=>k.StartsWith(AssemblyPrefix,StringComparison.Ordinal)&&!live.Contains(k)).ToArray())memory.Remove(key);
            foreach(var u in units)if(AppliedAssembly(o,army,u,records,AiArmyPlanner.IsArmyPolicy(army.TacticalOwner)&&army.Role==AiArmyRole.MobileDefense))memory[AssemblyKey(army,u)]=u.OrderStamp.Tick;
        }
        internal static bool AssembledAtHome(PlayableAiObservation o,AiArmyState army,PlayableEntitySnapshot unit,PlayableProfile p,AiProfile ai,System.Collections.Generic.IEnumerable<PlayableAiTraceRecord> records,System.Collections.Generic.IDictionary<string,long> memory=null,AiKnowledgeState knowledge=null)
        {
            if(unit.Owner!=o.Owner||army.OwnerId!=o.OwnerId||!army.Members.Contains(unit.Id)||!Available(unit))return false;
            var stamp=unit.OrderStamp;
            bool effective=stamp!=null&&stamp.UnitId==unit.Id&&stamp.Owner==o.Owner&&stamp.Generation==o.Generation&&stamp.Tick>=army.CreatedTick&&stamp.Tick<=o.Tick&&
                stamp.Origin==PlayableOrderOrigin.Ai&&stamp.Kind==PlayableCommandKind.Move&&stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&stamp.ActionId>0&&stamp.JobId==stamp.ActionId&&
                (unit.CurrentOrder==null&&unit.NavigationOutcome==NavigationOutcome.Arrived||unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&unit.CurrentOrder.Owner==o.Owner&&unit.CurrentOrder.Generation==o.Generation&&unit.CurrentOrder.UnitId==unit.Id&&unit.CurrentOrder.CommandSequence==stamp.Sequence&&unit.CurrentOrder.IssuedTick==stamp.Tick);
            if(!effective||!(AppliedAssembly(o,army,unit,records)||memory!=null&&memory.TryGetValue(AssemblyKey(army,unit),out var tick)&&tick==stamp.Tick))return false;
            // Physical compact assembly uses the existing core radius. Several
            // aircraft cannot all finish at one exact home point. Preserve the
            // owned Applied Move and current safe home proof, without requiring
            // another member's occupied arrival slot or an unbounded trace.
            double radius=ai.Value("safeReinforcements.coreRadius");
            return o.RouteProofs.Any(r=>r.UnitId==unit.Id&&Home(o,r)&&AiTacticalExecutor.SafeRoute(o,r,p,knowledge)&&
                Distance(unit.Position,r.Goal)<=radius&&(unit.CurrentOrder==null||Distance(unit.CurrentOrder.Destination,r.Goal)<=radius));
        }
        private static double Distance(NavPoint a,NavPoint b)=>AiOpponentSelector.Distance(a,b);
        internal static bool Available(PlayableEntitySnapshot u)=>u.Health>0&&u.MaterializationProgress>=1&&!u.Held&&!u.Departing&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human;
        internal static PlayableEntitySnapshot[] Core(PlayableAiObservation o,AiArmyState army)=>o.Entities.Where(u=>u.Owner==o.Owner&&Available(u)&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0).OrderBy(u=>u.Id).ToArray();
        internal static bool Viable(PlayableEntitySnapshot[] units,int minimum,PlayableProfile p,AiProfile ai)=>units.Length>=minimum&&units.Sum(u=>PlayableUnitRules.HealthRatio(p,u))>=minimum*ai.Value("safeReinforcements.coreHealthShare");
        internal static long PreferredReceiver(PlayableAiObservation o,AiArmyRegistry registry,AiArmyState current,PlayableEntitySnapshot unit,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge)
        {
            var active = registry.Capture().Armies;
            if (unit.Kind == PlayableEntityKind.Cerberus)
            {
                var ownCerberus = o.Entities.Where(e => e.Owner == o.Owner &&
                    e.Kind == PlayableEntityKind.Cerberus && e.Health > 0).Select(e => e.Id).ToHashSet();
                var homes = o.Buildings.Where(b => b.Owner == o.Owner && b.Kind == PlayableBuildingKind.Headquarters)
                    .Select(b => ((int)PlayableRouteTargetKind.FriendlyAnchor) + ":" + b.Id).ToHashSet();
                bool Strike(AiArmyState army) =>
                    AiArmyPlanner.IsArmyPolicy(army.TacticalOwner) &&
                    army.Role is AiArmyRole.Attack or AiArmyRole.AlliedSupport;
                bool ForwardGuard(AiArmyState army) => AiRepairUsePolicy.GuardMission(army) && !homes.Contains(army.Objective);
                // An existing forward guard can receive the tank if there
                // is no reachable strike group. Quiet headquarters stay out.
                return active.Where(army => Strike(army) || ForwardGuard(army))
                    .Select(army => new { Army = army, Core = ReceivingCore(o, army, p, ai, knowledge) })
                    .Where(entry => entry.Core.Length > 0 &&
                        o.RouteProofs.Any(route => route.UnitId == unit.Id && route.Kind == PlayableRouteTargetKind.FriendlyAnchor &&
                            entry.Core.Any(member => member.Id == route.TargetId) && AiTacticalExecutor.SafeRoute(o, route, p, knowledge)))
                    .OrderBy(entry => !Strike(entry.Army))
                    .ThenBy(entry => entry.Army.Members.Count(ownCerberus.Contains))
                    .ThenBy(entry => entry.Army.Id).Select(entry => entry.Army.Id).FirstOrDefault();
            }
            bool sniper=unit.Kind==PlayableEntityKind.Igla;
            int SnipersIn(AiArmyState army)=>o.Entities.Count(member=>member.Owner==o.Owner&&member.Health>0&&
                member.Kind==PlayableEntityKind.Igla&&army.Members.Contains(member.Id));
            var choices=active.Where(a=>a.Id==current.Id||a.TacticalOwner!=current.TacticalOwner&&AiArmyPlanner.IsArmyPolicy(a.TacticalOwner)&&(a.Phase==AiArmyPhase.Advancing||a.Phase==AiArmyPhase.Engaging))
                .Select(a=>new{Army=a,Core=ReceivingCore(o,a,p,ai,knowledge)})
                .Where(x=>x.Core.Length>0&&(!sniper||x.Core.Any(c=>AiEconomyAdmission.IglaCover(c.Kind)>0)))
                .ToArray();
            if(sniper)
            {
                var preferred=choices.Where(x=>AiEconomyAdmission.IglaCoverAllowsOrder(
                    x.Core.Sum(c=>AiEconomyAdmission.IglaCover(c.Kind)),SnipersIn(x.Army))).ToArray();
                if(preferred.Any(x=>o.RouteProofs.Any(r=>r.UnitId==unit.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&
                    x.Core.Any(c=>c.Id==r.TargetId&&(!sniper||AiEconomyAdmission.IglaCover(c.Kind)>0))&&AiTacticalExecutor.SafeRoute(o,r,p,knowledge))))choices=preferred;
            }
            var reachable=choices.SelectMany(x=>o.RouteProofs.Where(r=>r.UnitId==unit.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&x.Core.Any(c=>c.Id==r.TargetId&&(!sniper||AiEconomyAdmission.IglaCover(c.Kind)>0))&&AiTacticalExecutor.SafeRoute(o,r,p,knowledge))
                .Select(r=>new{Id=x.Army.Id,Length=r.Path.Zip(r.Path.Skip(1),(a,b)=>Distance(a,b)).Sum()+Distance(r.Origin,r.Path.First())})).OrderBy(x=>x.Length).ThenBy(x=>x.Id).FirstOrDefault();
            if(sniper)return reachable?.Id??0;
            return reachable?.Id??choices.OrderBy(x=>x.Army.Id).Select(x=>x.Army.Id).FirstOrDefault();
        }
        internal static PlayableEntitySnapshot[] ReceivingCore(PlayableAiObservation o,AiArmyState army,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge=null,bool allowRecovering=false,int requiredAnchor=0)
        {
            if(army.Phase==AiArmyPhase.Disbanded||army.Phase==AiArmyPhase.Regrouping||army.Phase==AiArmyPhase.Retreating||
                army.Phase==AiArmyPhase.Recovering&&!allowRecovering)return Array.Empty<PlayableEntitySnapshot>();
            var core=Core(o,army);
            foreach(var cluster in CompactCores(core,army.MinimumViableForce,p,ai))
            {
                var threats=o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Health>0&&o.Vision!=null&&o.Vision.IsVisible(e.Position)&&cluster.Any(u=>AiRosterCatalog.Initial.Targets(e.Kind,PlayableUnitRules.Aircraft(u.Kind)?"air":"ground")&&Distance(e.Position,u.Position)<=PlayableUnitRules.Range(p,e.Kind,e.Upgraded)+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,e.Kind))).ToArray();
                if(AiDefensePlanner.Force(threats,p,ai)>AiDefensePlanner.Force(cluster,p,ai)*ai.Value("safeReinforcements.maximumThreatRatio"))continue;
                // An anchor under known compatible turret fire is not a safe join side.
                var safe=cluster.Where(u=>AiStaticDefenseSafety.Withdrawal(o,p,u,new[]{u.Position},knowledge)).ToArray();
                if(Viable(safe,army.MinimumViableForce,p,ai)&&(requiredAnchor==0||safe.Any(u=>u.Id==requiredAnchor)))return safe;
            }
            return Array.Empty<PlayableEntitySnapshot>();
        }
        private static IEnumerable<PlayableEntitySnapshot[]> CompactCores(PlayableEntitySnapshot[] core,int minimum,
            PlayableProfile p,AiProfile ai)
        {
            double radius=ai.Value("safeReinforcements.coreRadius");
            return core.Select(center=>core.Where(u=>Distance(u.Position,center.Position)<=radius).ToArray())
                .Where(cluster=>Viable(cluster,minimum,p,ai));
        }

        internal static bool LostReceivingCore(PlayableAiObservation o,AiArmyState army,PlayableProfile p,
            AiProfile ai,AiKnowledgeState knowledge,bool checkSafety)
        {
            var core=Core(o,army);
            if(!Viable(core,army.MinimumViableForce,p,ai))return true;
            if(!checkSafety||ReceivingCore(o,army,p,ai,knowledge).Length>0)return false;
            // Spacing alone pauses joining orders while the core keeps its objective.
            // A compact core with no safe anchor, or an exposed dispersed core,
            // must send its incoming units to regroup.
            return CompactCores(core,army.MinimumViableForce,p,ai).Any()||
                AiTacticalExecutor.Withdraw(o,army,p,ai,ai.Value("safeReinforcements.maximumThreatRatio"))||
                !Viable(core.Where(u=>AiStaticDefenseSafety.Withdrawal(o,p,u,new[]{u.Position},knowledge)).ToArray(),
                    army.MinimumViableForce,p,ai);
        }

        internal static bool AppliedAssembly(PlayableAiObservation o,AiArmyState army,PlayableEntitySnapshot unit,IEnumerable<PlayableAiTraceRecord> records,bool allowAttackMove=false)
        {
            var stamp=unit.OrderStamp;
            return stamp!=null&&stamp.UnitId==unit.Id&&stamp.Owner==o.Owner&&stamp.Generation==o.Generation&&stamp.Tick>=army.CreatedTick&&stamp.Tick<=o.Tick&&
                stamp.Origin==PlayableOrderOrigin.Ai&&(stamp.Kind==PlayableCommandKind.Move||allowAttackMove&&stamp.Kind==PlayableCommandKind.AttackMove)&&stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&stamp.ActionId>0&&stamp.JobId==stamp.ActionId&&
                (unit.CurrentOrder==null&&unit.NavigationOutcome==NavigationOutcome.Arrived||unit.CurrentOrder?.Kind==(stamp.Kind==PlayableCommandKind.AttackMove?PlayableTacticalOrderKind.AttackMove:PlayableTacticalOrderKind.Move)&&unit.CurrentOrder.CommandSequence==stamp.Sequence&&unit.CurrentOrder.IssuedTick==stamp.Tick)&&
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
        internal static bool Home(PlayableAiObservation o,PlayableRouteProof route)=>route.Kind==PlayableRouteTargetKind.FriendlyAnchor&&o.Buildings.Any(b=>b.Id==route.TargetId&&Home(o,b));
        internal static bool Home(PlayableAiObservation o,PlayableBuildingSnapshot building)=>Home(o.Owner,building);
        internal static bool Home(PlayableOwner owner,PlayableBuildingSnapshot building)=>building.Owner==owner&&building.Health>0&&building.Phase==ConstructionPhase.Ready&&
            (building.Kind==PlayableBuildingKind.Headquarters||building.Kind==PlayableBuildingKind.Outpost||building.Kind==PlayableBuildingKind.Factory||building.Kind==PlayableBuildingKind.RepairTower);
        internal static PlayableAiAction RedirectWithdrawingClients(PlayableAiObservation o,AiArmyState army,
            PlayableProfile p,long id,long withdrawalTick,IEnumerable<PlayableAiTraceRecord> records,AiKnowledgeState knowledge)
        {
            var clients=o.Entities.Where(u=>u.Owner==o.Owner&&army.Reinforcements.Contains(u.Id)&&
                !(u.Queue?.PendingAiRouteViable==true&&u.Queue.Pending?.Kind==PlayableCommandKind.Move)&&
                NeedsRegroupStop(o,army,u,records,withdrawalTick)).OrderBy(u=>u.Id).ToArray();
            if(clients.Length==0)return null;

            var home=o.RouteProofs.Where(r=>clients.Any(u=>u.Id==r.UnitId)&&Home(o,r)&&
                AiTacticalExecutor.SafeRoute(o,r,p,knowledge)).OrderBy(AiTacticalExecutor.RouteLength)
                .ThenBy(r=>r.UnitId).ThenBy(r=>r.TargetId).FirstOrDefault();
            if(home!=null)
                return new PlayableAiAction(id,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,
                    PlayableCommandKind.Move,new[]{home.UnitId},home.Goal,parentId:home.TargetId,seed:o.Seed,
                    sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);

            return new PlayableAiAction(id,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,
                PlayableCommandKind.Stop,clients.Select(u=>u.Id).ToArray(),seed:o.Seed,
                sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }

        internal static PlayableAiAction Gather(PlayableAiObservation o,AiArmyState army,PlayableProfile p,long id,IEnumerable<PlayableAiTraceRecord> records,AiKnowledgeState knowledge=null,long regroupTick=0,AiProfile ai=null,System.Collections.Generic.IDictionary<string,long> memory=null)
        {
            var members=o.Entities.Where(u=>u.Owner==o.Owner&&Available(u)&&army.Members.Contains(u.Id)).OrderBy(u=>u.Id).ToArray();
            // Stop the old stream as one ordinary command before serial escape
            // admissions. A low-ID core must not leave followers chasing its old
            // battle while their safe home leg waits for shared attention.
            var stale=members.Where(u=>NeedsRegroupStop(o,army,u,records,regroupTick)&&!AssembledAtHome(o,army,u,p,ai??AiProfile.Initial,records,memory,knowledge)).Select(u=>u.Id).ToArray();
            if(stale.Length>0)return new PlayableAiAction(id,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Stop,stale,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            // Prefer one mutually reachable home, then the widest safe rendezvous.
            // Deterministic IDs avoid separate nearest-home streams and oscillation.
            bool SafeHomeLeg(PlayableRouteProof route, PlayableEntitySnapshot unit) =>
                Home(o,route) && (AiTacticalExecutor.SafeRoute(o,route,p,knowledge) ||
                    AiTacticalExecutor.WithdrawalRoute(o,route,p) && AiStaticDefenseSafety.Withdrawal(o,p,unit,route.Path,knowledge));
            var safeHomeLegs=o.RouteProofs.Where(r=>members.Any(u=>u.Id==r.UnitId&&SafeHomeLeg(r,u))).ToLookup(r=>r.UnitId);
            var home=safeHomeLegs.SelectMany(g=>g)
                .GroupBy(r=>r.TargetId).OrderByDescending(g=>g.Select(r=>r.UnitId).Distinct().Count()).ThenBy(g=>g.Key).FirstOrDefault();
            bool Exposed(PlayableEntitySnapshot u)=>!AiTacticalExecutor.SafePathForUnit(o,u.Position,new[]{u.Position},PlayableUnitRules.Radius(p,u.Kind),p,PlayableUnitRules.Aircraft(u.Kind))||!AiStaticDefenseSafety.Withdrawal(o,p,u,new[]{u.Position},knowledge);
            foreach(var unit in members.OrderByDescending(Exposed).ThenBy(u=>u.CurrentOrder!=null).ThenByDescending(u=>army.Reinforcements.Contains(u.Id)).ThenBy(u=>u.Id))
            {
                if(!Exposed(unit)&&AssembledAtHome(o,army,unit,p,ai??AiProfile.Initial,records,memory,knowledge))continue;
                var pending=unit.Queue?.Pending;
                if(unit.Queue?.PendingAiRouteViable==true&&pending?.Kind==PlayableCommandKind.Move&&
                    pending.AiActionId>0&&pending.Anchor.HasValue)continue;
                var order=unit.CurrentOrder;
                if(order?.Kind==PlayableTacticalOrderKind.Move&&order.IssuedTick>=regroupTick&&
                    (unit.Moving||unit.Queue?.ActiveAiOrderViable==true)&&
                    (OwnedJoiningOrder(o,army,unit,records)||memory is IReadOnlyDictionary<string,long> retained&&RememberedAssembly(army,unit,retained)))
                {
                    // A home proof can disappear while the path worker refreshes it.
                    // Only a current adverse proof may replace a live regroup leg.
                    var current=o.RouteProofs.Where(r=>r.UnitId==unit.Id&&Home(o,r)&&r.Goal.Equals(order.Destination)).ToArray();
                    if(current.Length==0||current.Any(r=>SafeHomeLeg(r,unit)))continue;
                }
                var routes=safeHomeLegs[unit.Id];
                var route=routes.OrderBy(r=>r.TargetId!=home?.Key)
                    .ThenBy(r=>r.Path.Zip(r.Path.Skip(1),(a,b)=>Distance(a,b)).Sum()+Distance(r.Origin,r.Path.First()))
                    .ThenBy(r=>r.TargetId).FirstOrDefault();
                if(route==null||order?.Kind==PlayableTacticalOrderKind.Move&&order.Destination.Equals(route.Goal)&&unit.Moving||
                    AtHome(o,unit,p,knowledge)&&AppliedAssembly(o,army,unit,records))continue;
                return new PlayableAiAction(id,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Move,new[]{unit.Id},route.Goal,parentId:route.TargetId,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            }
            return null;
        }
    }
}
