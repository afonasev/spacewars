using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal sealed partial class AiCombatMicroPolicy
    {
        internal static bool ResumesArmyAttack(AiDifficulty d)=>true;
        internal bool HasAppliedReturn(int unit)=>clients.TryGetValue(unit,out var client)&&client.Sequence>0&&
            (client.Phase==AiCombatMicroPhase.Return||client.Phase==AiCombatMicroPhase.Resume);

        internal bool ProtectsReturn(PlayableEntitySnapshot unit,string ownerId,long generation,long tick,
            AiProfile ai,AiDifficulty difficulty,AiArmyRegistry armies)
        {
            if(unit==null||!Eligible(unit)||!clients.TryGetValue(unit.Id,out var client)||client.Sequence==0||
                (client.Phase!=AiCombatMicroPhase.Return&&client.Phase!=AiCombatMicroPhase.Resume)||
                tick-client.Started>T(ai,difficulty,"maxPhaseSeconds")||
                !armies.TryReadGuard(client.Army,out var group)||!ReturnGroup(ownerId,unit.Id,group))return false;
            return certificates.TryGetValue(unit.Id,out var certificate)&&Matches(certificate,ownerId,unit.Owner,generation,tick,unit);
        }

        internal void ReturnShot(PlayableOrderStamp stamp,string ownerId,long tick,AiProfile ai,AiDifficulty difficulty,bool completedWithdrawal=false)
        {
            if(stamp?.Origin!=PlayableOrderOrigin.Ai||!clients.TryGetValue(stamp.UnitId,out var client)||client.Sequence==0||
                (client.Phase!=AiCombatMicroPhase.Return&&client.Phase!=AiCombatMicroPhase.Resume&&
                    !(completedWithdrawal&&client.Phase==AiCombatMicroPhase.Withdraw&&stamp.Kind==PlayableCommandKind.Move))||
                !certificates.TryGetValue(stamp.UnitId,out var certificate)||certificate.Owner!=ownerId||
                certificate.Generation!=stamp.Generation||certificate.Sequence!=stamp.Sequence||
                certificate.ActionId!=stamp.ActionId||certificate.ActionId!=stamp.JobId||
                certificate.Tick!=stamp.Tick||certificate.Kind!=stamp.Kind||certificate.Source!=stamp.Source)return;
            Release(stamp.UnitId,tick,ai,difficulty);
        }

        readonly Dictionary<(int Unit,int Tower),RepairRetry> repairRetries=new Dictionary<(int,int),RepairRetry>();

        sealed class RepairRetry
        {
            internal NavLocation At;
            internal NavPoint Goal;
            internal string RouteInputs;
            internal long CancelledAt;
            internal bool WasUnavailable;
        }

        // Use only the patient's pose and observable inputs near plausible approaches.
        // Do not key on an order stamp, snapshot tick, or global geometry revision.
        static string RepairRouteInputs(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot u,NavPoint goal)
        {
            double radius=PlayableUnitRules.Radius(p,u.Kind);
            double envelope=Distance(u.Position,goal)+2*p.RepairTowerRange;
            bool NearRoute(NavPoint point,double reach)=>Distance(u.Position,point)+Distance(point,goal)<=envelope+2*reach;
            string Building(PlayableBuildingSnapshot b)=>FormattableString.Invariant($"b{b.Id}/{b.Position.X:R}/{b.Position.Z:R}");
            string Wreck(PlayableWreckSnapshot w)=>FormattableString.Invariant($"w{w.Id}/{w.Position.X:R}/{w.Position.Z:R}/{w.PhysicalLayer}");
            var buildings=o.Buildings.Where(b=>b.Health>0&&b.Phase!=ConstructionPhase.Pending&&
                NearRoute(b.Position,radius+Math.Max(TerritoryRules.Radius(p,b.Kind),
                    o.IsHostile(b.Owner)&&TerritoryRules.ArmedDefense(b.Kind)?TerritoryRules.AiDefenseRange(p,b.Kind):0)))
                .OrderBy(b=>b.Id).Select(Building);
            var wrecks=o.VisibleWrecks.Where(w=>NearRoute(w.Position,w.Radius+radius)).OrderBy(w=>w.Id).Select(Wreck);
            bool OccupiesApproach(PlayableEntitySnapshot other,NavPoint point)=>
                Distance(other.Position,point)<=radius+PlayableUnitRules.Radius(p,other.Kind)+p.Navigation.ArrivalTolerance;
            var occupancy=o.Entities.Where(other=>other.Id!=u.Id&&other.Health>0&&!PlayableUnitRules.Aircraft(other.Kind)&&
                (OccupiesApproach(other,u.Position)||OccupiesApproach(other,goal))).OrderBy(other=>other.Id).Select(other=>
                    FormattableString.Invariant($"u{other.Id}/{OccupiesApproach(other,u.Position)}/{OccupiesApproach(other,goal)}"));
            return p.Revision.ToString(CultureInfo.InvariantCulture)+":"+string.Join(",",buildings.Concat(wrecks).Concat(occupancy));
        }

        internal bool BlocksRepairRetry(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot unit,int tower)
        {
            if(!repairRetries.TryGetValue((unit.Id,tower),out var retry)||retry.WasUnavailable)return false;
            return unit.Location?.SurfaceId==retry.At.SurfaceId&&
                Distance(unit.Position,retry.At.Position)<=p.Navigation.ArrivalTolerance&&
                retry.RouteInputs==RepairRouteInputs(o,p,unit,retry.Goal);
        }

        void ObserveRepairRetries(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty difficulty,
            IReadOnlyDictionary<int,PlayableEntitySnapshot> live)
        {
            foreach(var key in repairRetries.Keys.ToArray())
            {
                if(!live.TryGetValue(key.Unit,out var unit)||!o.Buildings.Any(b=>b.Id==key.Tower&&b.Owner==o.Owner&&b.Health>0))
                    repairRetries.Remove(key);
                else if(!repairRetries[key].WasUnavailable&&!RepairAvailable(o,p,ai,difficulty,unit,key.Tower))
                    repairRetries[key].WasUnavailable=true;
            }
        }

        void CompleteCancelledRepair(PlayableDomain domain,string ownerId,int entity,Client client,
            PlayableOrderStamp stamp,NavLocation location,string reason,AiProfile ai,AiDifficulty difficulty)
        {
            double? displacement=client.RepairOrigin is NavLocation start?Distance(start.Position,location.Position):(double?)null;
            bool blocked=displacement.HasValue&&displacement.Value<=1e-6&&client.RepairOrigin.Value.SurfaceId==location.SurfaceId;
            if(blocked)
            {
                var o=PlayableAiObservation.From(domain.PlayerSnapshot(0,RuntimeStatus.Running,false,
                    new PlayableRuntimeMetrics(0,0,0,0,0),null,0,domain.OwnerFor(ownerId)));
                var unit=o.Entities.FirstOrDefault(u=>u.Id==entity);
                if(unit!=null)
                    repairRetries[(entity,client.Tower)]=new RepairRetry{At=location.Detached(),Goal=client.Goal,
                        CancelledAt=domain.Tick,RouteInputs=RepairRouteInputs(o,domain.RoutingProfile,unit,client.Goal),
                        WasUnavailable=!RepairAvailable(o,domain.RoutingProfile,ai,difficulty,unit,client.Tower)};
            }
            domain.RecordRepairCancellation(ownerId,entity,client.Tower,stamp,location,displacement,reason,
                repairRetries.ContainsKey((entity,client.Tower)));
            Release(entity,domain.Tick,ai,difficulty);
        }

        void WriteRepairRetries(BinaryWriter w)
        {
            WorldWire.Array(w,repairRetries.OrderBy(x=>x.Key.Unit).ThenBy(x=>x.Key.Tower).ToArray(),x=>{
                w.Write(x.Key.Unit);w.Write(x.Key.Tower);GroupOrderWire.WriteLocation(w,x.Value.At);
                WorldWire.Write(w,x.Value.Goal);WorldWire.String(w,x.Value.RouteInputs);
                w.Write(x.Value.CancelledAt);w.Write(x.Value.WasUnavailable);
            });
        }

        void ReadRepairRetries(BinaryReader r,long tick)
        {
            foreach(var entry in WorldWire.Array(r,()=>{
                int unit=r.ReadInt32(),tower=r.ReadInt32();var at=GroupOrderWire.ReadLocation(r);
                var goal=WorldWire.ReadPoint(r);string inputs=WorldWire.String(r);long cancelledAt=r.ReadInt64();bool unavailable=WorldWire.Boolean(r);
                AiStateWire.Require(unit>0&&tower>0&&at.HasValue&&!string.IsNullOrEmpty(inputs)&&cancelledAt>=0&&cancelledAt<=tick,
                    "micro repair retry");
                return (Unit:unit,Tower:tower,Retry:new RepairRetry{At=at.Value,Goal=goal,RouteInputs=inputs,
                    CancelledAt=cancelledAt,WasUnavailable=unavailable});
            }))
                AiStateWire.Require(repairRetries.TryAdd((entry.Unit,entry.Tower),entry.Retry),"duplicate micro repair retry");
        }

        internal void RetireUnavailableGroundRepairs(PlayableDomain domain,string ownerId,AiProfile ai,AiDifficulty difficulty)
        {
            List<(int Entity,PlayableOrderStamp Stamp,NavLocation At,string Reason)> retired=null;
            foreach(var pair in clients)
            {
                var client=pair.Value;
                if(client.Phase!=AiCombatMicroPhase.Repair||client.Sequence<=0||
                    !certificates.TryGetValue(pair.Key,out var certificate)||certificate.Owner!=ownerId||
                    certificate.Generation!=domain.Navigation.Generation||certificate.Kind!=PlayableCommandKind.Move||
                    certificate.Sequence!=client.Sequence||certificate.ActionId!=client.ActionId)continue;
                if(domain.RetireGroundRepairMove(ownerId,pair.Key,client.Tower,client.Sequence,client.ActionId,
                    certificate.Tick,T(ai,difficulty,"maxPhaseSeconds")-(domain.Tick-client.Started),ai,difficulty,
                    out var stamp,out var location,out var reason))
                    (retired??=new List<(int,PlayableOrderStamp,NavLocation,string)>()).Add((pair.Key,stamp,location,reason));
            }
            if(retired!=null)
                foreach(var order in retired)
                    CompleteCancelledRepair(domain,ownerId,order.Entity,clients[order.Entity],order.Stamp,order.At,order.Reason,ai,difficulty);
        }

        internal void RetireDeniedGroundRepair(PlayableDomain domain,string ownerId,MotionExecutionBinding binding,
            string reason,AiProfile ai,AiDifficulty difficulty)
        {
            int entity=(int)binding.Entity;
            var own=domain.OwnerFor(ownerId);
            if(binding.Owner!=ownerId||binding.World!=domain.Navigation.Generation||
                !clients.TryGetValue(entity,out var client)||client.Phase!=AiCombatMicroPhase.Repair||
                !domain.TryReadCombatExecutionJobStamp(entity,own,out var stamp)||
                !RetainsExecutionJob(entity,binding.World,binding.OwnerPolicy,ownerId,own,stamp)||
                !domain.CancelDeniedGroundRepairMove(binding,client.ActionId,stamp.Tick,out var location))return;
            CompleteCancelledRepair(domain,ownerId,entity,client,stamp,location,reason,ai,difficulty);
        }

        // A detached review cannot erase a live cancellation or its retry block.
        internal bool MissedRelease(AiCombatMicroPolicy current,long observedTick,AiProfile ai,AiDifficulty difficulty)=>
            current.repairRetries.Any(x=>x.Value.CancelledAt>observedTick&&
                (!repairRetries.TryGetValue(x.Key,out var retry)||retry.CancelledAt<x.Value.CancelledAt))||
            clients.Keys.Any(id=>!current.clients.ContainsKey(id)&&current.cooldowns.TryGetValue(id,out var until)&&
                until-T(ai,difficulty,"cooldownSeconds")>observedTick);

        // Completed Move already enables ordinary auto fire. Let that weapon finish
        // aiming/preparing instead of spending another order and restarting it.
        static bool AutomaticallyResuming(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot unit)=>
            !unit.Moving&&unit.CurrentOrder==null&&unit.Queue?.Pending==null&&!(unit.Queue?.Deferred.Count>0)&&
            o.Entities.Any(e=>e.Id==unit.TargetId&&e.Health>0&&o.IsHostile(e.Owner)&&
                CanHit(unit,e)&&Distance(unit.Position,e.Position)<=PlayableUnitRules.Range(p,unit.Kind,unit.Upgraded)&&
                AiCombatMicroSafety.FireClear(o,p,unit,e));

        bool ReadyToResume(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot unit)
        {
            if(!certificates.TryGetValue(unit.Id,out var prior)||!Matches(prior,o,unit))return false;
            if(prior.Kind==PlayableCommandKind.Move)
                return clients.TryGetValue(unit.Id,out var client)&&WithdrawalCanReturn(o,p,ai,d,unit,client);
            if(prior.Kind!=PlayableCommandKind.Follow||unit.Queue?.Pending!=null||
                unit.Queue?.Deferred.Count>0||UnderWithdrawalPressure(o,unit,p))return false;
            var leader=o.Entities.FirstOrDefault(e=>e.Id==prior.Target&&e.Owner==o.Owner&&e.Health>0);
            return leader!=null&&ReturnJoined(p,unit,leader);
        }

        System.Collections.Generic.IEnumerable<(PlayableEntitySnapshot Anchor,PlayableEntitySnapshot Enemy)> LiveBattle(
            PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyRegistry armies,PlayableEntitySnapshot unit,long army)
        {
            if(!armies.TryReadGuard(army,out var group)||group.Phase!=AiArmyPhase.Engaging&&group.Phase!=AiArmyPhase.Advancing)yield break;
            foreach(var anchor in RotationAnchors(o,p,ai,d,armies,unit,army))
            {
                if(anchor.Id!=unit.Id&&Reserves(anchor.Id))continue;
                int target=anchor.TargetId;
                if(target==0&&anchor.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack)target=anchor.CurrentOrder.TargetId;
                var active=Attackers(o,anchor,p);
                foreach(var enemy in o.Entities.Where(e=>(e.Id==target||active.Any(a=>a.Id==e.Id)||anchor.Id==unit.Id)&&e.Health>0&&
                    o.IsHostile(e.Owner)&&o.Vision?.IsVisible(e.Position)==true&&
                    Distance(anchor.Position,e.Position)<=PlayableUnitRules.Range(p,anchor.Kind,anchor.Upgraded)&&
                    AiRosterCatalog.Initial.Targets(unit.Kind,e.Kind==PlayableEntityKind.Hawk?"air":"ground")))
                    yield return (anchor,enemy);
            }
        }

        // A nearby attacker switching to a teammate is contact evidence in its
        // own right. The healthy core still vouches for the firing position.
        System.Collections.Generic.IEnumerable<(PlayableEntitySnapshot Anchor,PlayableEntitySnapshot Enemy,bool ShortOnly)> ResumeContacts(
            PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyRegistry armies,PlayableEntitySnapshot unit,long army)
        {
            foreach(var pair in LiveBattle(o,p,ai,d,armies,unit,army))yield return (pair.Anchor,pair.Enemy,false);
            if(!certificates.TryGetValue(unit.Id,out var prior)||prior.Kind!=PlayableCommandKind.Move||
                !AttackersSwitched(o,unit,p))yield break;
            double reach=PlayableUnitRules.Range(p,unit.Kind,unit.Upgraded)+WithdrawalStepLength(p,ai,d,unit)+p.Navigation.ArrivalTolerance;
            var switched=o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Health>0&&o.Vision?.IsVisible(e.Position)==true&&
                Distance(e.Position,unit.Position)<=reach&&AiRosterCatalog.Initial.Targets(unit.Kind,e.Kind==PlayableEntityKind.Hawk?"air":"ground")&&
                fire.Any(f=>f.Source==e.Id&&f.Victim==unit.Id&&SwitchedFire(f,unit.Id))).ToArray();
            if(switched.Length==0)yield break;
            foreach(var anchor in RotationAnchors(o,p,ai,d,armies,unit,army))
                if(anchor.Id==unit.Id||!Reserves(anchor.Id))
                    foreach(var enemy in switched)yield return (anchor,enemy,true);
        }

        static bool ResumeEndpoint(PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot unit,
            PlayableEntitySnapshot anchor,PlayableEntitySnapshot enemy,NavPoint point)=>
            Distance(point,anchor.Position)<=V(ai,d,"supportRadius")&&
            Distance(point,enemy.Position)<=PlayableUnitRules.Range(p,unit.Kind,unit.Upgraded);

        AiCombatMicroPlan ResumeArmyAttack(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,
            AiArmyRegistry armies,PlayableEntitySnapshot unit,long army)
        {
            double body=PlayableUnitRules.Radius(p,unit.Kind);
            double weaponRange=PlayableUnitRules.Range(p,unit.Kind,unit.Upgraded);
            double range=weaponRange-2*p.Navigation.ArrivalTolerance;
            if(range<=0)return null;
            var contacts=ResumeContacts(o,p,ai,d,armies,unit,army).ToArray();
            if(contacts.Length==0)return null;
            var geometry=AiTacticalExecutor.ObservedGroundGeometry(o,p);
            var occupied=o.Entities.Where(other=>other.Id!=unit.Id&&other.Health>0&&(other.Kind==PlayableEntityKind.Hawk)==(unit.Kind==PlayableEntityKind.Hawk)).ToArray();
            var positions=contacts.SelectMany(pair=>ReturnPoints(p,unit.Position,pair.Enemy.Position,range,weaponRange,pair.ShortOnly)
                .Select(goal=>(pair.Anchor,pair.Enemy,pair.ShortOnly,Goal:goal)));
            foreach(var candidate in positions.OrderBy(x=>Distance(unit.Position,x.Goal)).ThenBy(x=>x.Anchor.Id).ThenBy(x=>x.Enemy.Id))
            {
                var goal=candidate.Goal;
                if(candidate.ShortOnly&&(!IsShortReturn(p,ai,d,unit,goal)||!WithdrawalSpaceFree(o,p,unit,new[]{goal})))continue;
                if(unit.Kind==PlayableEntityKind.Hawk&&!AirRotationSpaceFree(o,p,unit,goal)||!RotationReturnSafe(o,p,ai,d,unit,goal)||o.Vision?.IsVisible(goal)!=true||!ResumeEndpoint(p,ai,d,unit,candidate.Anchor,candidate.Enemy,goal)||
                    unit.Kind!=PlayableEntityKind.Hawk&&(!geometry.IsFree(goal,body)||!geometry.SegmentFree(unit.Position,goal,body)||
                    p.AuthoredMap?.SupportsSweep(unit.Position,goal,body)==false)||
                    occupied.Any(other=>Distance(other.Position,goal)<body+PlayableUnitRules.Radius(p,other.Kind)+p.Navigation.ArrivalTolerance)||
                    !AiCombatMicroSafety.FireClear(o,p,unit,candidate.Enemy,goal)||
                    !AiCombatMicroSafety.Path(o,p,unit,new[]{goal},false))continue;
                return new AiCombatMicroPlan
                {
                    Action=Action(o,PlayableCommandKind.AttackMove,new[]{unit.Id},goal,candidate.Anchor.Id),
                    Phase=AiCombatMicroPhase.Resume,Army=army
                };
            }
            return null;
        }

        static System.Collections.Generic.IEnumerable<NavPoint> ReturnPoints(PlayableProfile p,NavPoint position,NavPoint enemy,double range,double weaponRange,bool shortOnly)
        {
            double separation=Distance(position,enemy);
            if(separation<=weaponRange)yield return position;
            if(separation<=double.Epsilon)yield break;
            if(shortOnly)
            {
                double edge=Math.Min(weaponRange-p.Navigation.ArrivalTolerance,Math.Max(separation-p.Navigation.ArrivalTolerance,0));
                yield return new NavPoint(enemy.X+(position.X-enemy.X)*edge/separation,enemy.Z+(position.Z-enemy.Z)*edge/separation);
            }
            double approach=Math.Min(range,Math.Max(separation-p.Navigation.ArrivalTolerance,0));
            yield return new NavPoint(enemy.X+(position.X-enemy.X)*approach/separation,enemy.Z+(position.Z-enemy.Z)*approach/separation);
            foreach(var point in PlayableUnitRules.AttackApproachCandidates(p,position,enemy,range/p.AttackApproachRangeRatio))
                yield return point;
            foreach(var point in PlayableUnitRules.AttackApproachCandidates(p,position,enemy,weaponRange))
                yield return point;
        }

        bool RepairEligible(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot u,AiArmyState army,bool? combat=null)
        {
            if(V(ai,d,"repairEnabled")==0||!Eligible(u)||u.Queue?.Pending!=null||(u.Queue!=null&&u.Queue.Deferred.Count!=0)||u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow)return false;
            double health=PlayableUnitRules.HealthRatio(p,u);
            bool fighting=combat??(Attackers(o,u,p).Length>0||u.TargetId>0||u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack||u.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove);
            if(fighting)return health<V(ai,d,"combatRepairHealth");
            // An already accepted short withdrawal may convert directly to
            // repair under its original exact certificate and combat threshold.
            if(certificates.TryGetValue(u.Id,out var certificate)&&certificate.Kind==PlayableCommandKind.Move&&Matches(certificate,o,u))return health<V(ai,d,"combatRepairHealth");
            return health<V(ai,d,"idleRepairHealth")&&(u.CurrentOrder==null||QuietGuard(o,p,ai,d,u,army));
        }
        static bool QuietGuard(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot u,AiArmyState army)
        {
            var order=u.CurrentOrder;var stamp=u.OrderStamp;
            if(army==null||army.OwnerId!=o.OwnerId||army.TacticalOwner!=AiArmyPlanner.Policy||army.Role!=AiArmyRole.MobileDefense||
                (army.Phase!=AiArmyPhase.Staging&&army.Phase!=AiArmyPhase.Advancing)||!army.Rally.HasValue||order?.Kind!=PlayableTacticalOrderKind.Move||
                stamp==null||stamp.UnitId!=u.Id||stamp.Owner!=o.Owner||stamp.Generation!=o.Generation||stamp.Sequence<=0||stamp.Tick>o.Tick||stamp.Origin!=PlayableOrderOrigin.Ai||stamp.Source!=PlayableAiOpeningComposition.SourceIdentity||stamp.Kind!=PlayableCommandKind.Move||stamp.ActionId<=0||stamp.JobId!=stamp.ActionId||
                order.UnitId!=u.Id||order.Owner!=o.Owner||order.Generation!=o.Generation||order.CommandSequence!=stamp.Sequence||order.IssuedTick!=stamp.Tick)return false;
            // Rally is the actual Guard request point committed by its tactical
            // owner, not the HQ center or a guessed member endpoint. Ground's
            // live Native issuance preserves that original request even when its
            // physical assigned destination differs for this particular member.
            var active=u.Queue?.Active;
            if(u.Kind==PlayableEntityKind.Hawk){if(!order.Destination.Equals(army.Rally.Value))return false;}
            else if(active==null||active.IssuanceId<=0||active.GroupId<=0||active.Kind!=PlayableCommandKind.Move||active.CommandSequence!=stamp.Sequence||active.AiActionId!=stamp.ActionId||!active.Anchor.HasValue||!active.Anchor.Value.Position.Equals(army.Rally.Value))return false;
            var anchor=o.Buildings.FirstOrDefault(b=>b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&
                (b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost||b.Kind==PlayableBuildingKind.Mine)&&army.Objective==((int)PlayableRouteTargetKind.FriendlyAnchor)+":"+b.Id);
            if(anchor==null||o.OwnCenterDamage.Any(hit=>hit.CenterId==anchor.Id))return false;double margin=TerritoryRules.Radius(p,anchor.Kind)+V(ai,d,"safetyMargin");
            return !o.Entities.Any(e=>o.IsHostile(e.Owner)&&e.Health>0&&Distance(e.Position,anchor.Position)<=PlayableUnitRules.Range(p,e.Kind,e.Upgraded)+margin)&&
                !AiCombatMicroSafety.Towers(o).Any(tower=>Distance(tower.Position,anchor.Position)<=(TerritoryRules.AiDefenseRange(p,tower.Kind))+margin);
        }
        bool RepairAvailable(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot u,int towerId)
        {
            var tower=o.Buildings.FirstOrDefault(b=>b.Id==towerId&&Tower(b,o.Owner));if(tower==null)return false;
            double range=p.RepairTowerRange;
            return ((tower.Healing?.TargetId??0)==0||tower.Healing.TargetId==u.Id)&&
                !clients.Any(pair=>pair.Key!=u.Id&&pair.Value.Phase==AiCombatMicroPhase.Repair&&pair.Value.Tower==towerId)&&
                !o.Entities.Any(other=>other.Id!=u.Id&&other.Owner==o.Owner&&other.Health>0&&PlayableUnitRules.HealthRatio(p,other)<1&&Distance(other.Position,tower.Position)<=range);
        }
        internal static double RepairSeconds(PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot u,double length)=>
            RepairSeconds(p,ai,d,u.Kind,u.ExactHealth??u.Health,PlayableUnitRules.MaximumHealth(p,u),length);
        static double RepairSeconds(PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntityKind kind,double health,double maximumHealth,double length)=>
            length/Math.Max(double.Epsilon,PlayableUnitRules.Speed(p,kind))+
            (p.RepairTowerHealthPerSecond>0?Math.Max(0,(V(ai,d,"repairReturnHealth")-health/maximumHealth)*maximumHealth)/p.RepairTowerHealthPerSecond:double.PositiveInfinity);
        internal static bool RepairFitsPhase(PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntityKind kind,double health,double maximumHealth,double length,long remainingTicks)
        {
            double seconds=RepairSeconds(p,ai,d,kind,health,maximumHealth,length);
            return seconds<=Math.Min(V(ai,d,"repairTravelSeconds"),Math.Min(V(ai,d,"maxPhaseSeconds"),remainingTicks/30d));
        }
        internal static bool RepairTimeAllowed(PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot u,double length)=>
            RepairSeconds(p,ai,d,u,length)<=Math.Min(V(ai,d,"repairTravelSeconds"),V(ai,d,"maxPhaseSeconds"));
        internal bool RepairEscapeAllowed(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot u,NavPoint[] path)
        {
            if(!samples.TryGetValue(u.Id,out var sample)||sample.Loss<=0||sample.DamageTick<0||o.Tick-sample.DamageTick>T(ai,d,"safeSeconds"))return true;
            return RepairEscapeAllowed(p,u,sample.Loss,Attackers(o,u,p),path);
        }
        static bool RepairEscapeAllowed(PlayableProfile p,PlayableEntitySnapshot u,double loss,System.Collections.Generic.IEnumerable<PlayableEntitySnapshot> attackers,System.Collections.Generic.IEnumerable<NavPoint> path)
        {
            double danger=0;
            foreach(var enemy in attackers)
            {
                double range=PlayableUnitRules.Range(p,enemy.Kind,enemy.Upgraded)+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,enemy.Kind),length=0;var at=u.Position;
                foreach(var end in path)
                {
                    double segment=Distance(at,end);
                    if(Distance(at,enemy.Position)>range)break;
                    if(Distance(end,enemy.Position)>range&&segment>0)
                    {
                        double dx=end.X-at.X,dz=end.Z-at.Z,x=at.X-enemy.Position.X,z=at.Z-enemy.Position.Z;
                        double b=2*(x*dx+z*dz),a=segment*segment,c=x*x+z*z-range*range;
                        double exit=Math.Max(0,Math.Min(1,(-b+Math.Sqrt(Math.Max(0,b*b-4*a*c)))/(2*a)));
                        length+=segment*exit;break;
                    }
                    length+=segment;at=end;
                }
                danger=Math.Max(danger,length);
            }
            // Only time still inside current factual attackers' envelopes counts.
            // Safe travel/healing is not compared with the old incoming-loss rate.
            return Health(u)/loss>danger/Math.Max(double.Epsilon,PlayableUnitRules.Speed(p,u.Kind));
        }
        AiCombatMicroPlan Repair(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot u,AiArmyState army)
        {
            double range=p.RepairTowerRange,radius=PlayableUnitRules.Radius(p,u.Kind);
            var buildings=u.Kind==PlayableEntityKind.Hawk?Array.Empty<NavObstacle>():o.Buildings.Where(b=>b.Health>0&&b.Phase!=ConstructionPhase.Pending).Select(b=>{
                double half=TerritoryRules.Radius(p,b.Kind);return new NavObstacle(b.Position.X-half,b.Position.Z-half,b.Position.X+half,b.Position.Z+half);
            }).ToArray();
            // Towers are not army route-proof targets. Select an observed physical
            // approach, then use the existing authoritative asynchronous route lane
            // at delivery, including its actual travel length and danger checks.
            var candidate=o.Buildings.Where(b=>RepairAvailable(o,p,ai,d,u,b.Id)&&!BlocksRepairRetry(o,p,u,b.Id)).SelectMany(tower=>
                PlayableUnitRules.AttackApproachCandidates(p,u.Position,tower.Position,range).Select(goal=>new{Tower=tower,Goal=goal,Length=Distance(u.Position,goal)}))
                .Where(x=>o.Vision?.IsVisible(x.Goal)==true&&Math.Abs(x.Goal.X)+radius<p.ArenaHalfExtent&&Math.Abs(x.Goal.Z)+radius<p.ArenaHalfExtent&&
                    (u.Kind==PlayableEntityKind.Hawk||!PlayableMap.StaticObstacles(p).Any(obstacle=>obstacle.DistanceSquared(x.Goal)<=radius*radius)&&
                        !buildings.Any(obstacle=>obstacle.DistanceSquared(x.Goal)<=radius*radius))&&
                    !o.Entities.Any(other=>other.Id!=u.Id&&other.Health>0&&(other.Kind==PlayableEntityKind.Hawk)==(u.Kind==PlayableEntityKind.Hawk)&&Distance(other.Position,x.Goal)<radius+PlayableUnitRules.Radius(p,other.Kind))&&
                    RepairTimeAllowed(p,ai,d,u,x.Length)&&RepairEscapeAllowed(o,p,ai,d,u,new[]{x.Goal})&&AiTacticalExecutor.WithdrawalPathForUnit(o,u.Position,new[]{x.Goal},radius,p,u.Kind==PlayableEntityKind.Hawk)&&
                    AiCombatMicroSafety.Path(o,p,u,new[]{x.Goal},true))
                .OrderBy(x=>RepairSeconds(p,ai,d,u,x.Length)).ThenBy(x=>x.Tower.Id).ThenBy(x=>x.Goal.X).ThenBy(x=>x.Goal.Z).FirstOrDefault();
            return candidate==null?null:new AiCombatMicroPlan{Action=Action(o,PlayableCommandKind.Move,new[]{u.Id},candidate.Goal,tower:candidate.Tower.Id),Phase=AiCombatMicroPhase.Repair,Army=army?.Id??0,Survival=Attackers(o,u,p).Length>0};
        }
    }
}
