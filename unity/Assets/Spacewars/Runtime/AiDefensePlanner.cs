using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Authority-owned defense transaction. Inputs are detached owner observations;
    // reachability and distance come from the ordinary native route proofs.
    public sealed class AiDefensePlanner
    {
        public const string Policy="defense";
        private readonly string owner;private readonly long generation;
        private long armyId,nextId=1,pendingId,pendingGeneration;
        private int targetId;
        private bool emergencyObserved;
        private double previousDistance=double.MaxValue,previousTargetHealth=-1;
        internal bool EmergencyObserved=>emergencyObserved;
        public long ArmyId=>armyId;
        public long PendingId=>pendingId;
        internal long PendingGeneration=>pendingGeneration;
        public bool Active=>armyId!=0;
        public AiDefensePlanner(string owner,long generation)
        {if(string.IsNullOrWhiteSpace(owner)||generation<=0)throw new ArgumentException("Defense identity");this.owner=owner;this.generation=generation;}
        private void Bind(PlayableAiObservation o)
        {if(o==null||o.OwnerId!=owner||o.Generation!=generation)throw new ArgumentException("Defense observation binding");}
        public static PlayableEntitySnapshot[] Threats(PlayableAiObservation o,PlayableProfile p)
        {
            var centers=o.Buildings.Where(b=>b.Owner==o.Owner&&b.Health>0&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost)).ToArray();
            return o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Health>0&&centers.Any(c=>Distance(e.Position,c.Position)<=(c.Kind==PlayableBuildingKind.Headquarters?p.HeadquartersVisionRange:p.OutpostVisionRange)))
                .OrderBy(e=>e.Id).ToArray();
        }
        private static PlayableEntitySnapshot[] LocalThreats(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot target)
        {
            var centers=o.Buildings.Where(b=>b.Owner==o.Owner&&b.Health>0&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost)&&Distance(target.Position,b.Position)<=(b.Kind==PlayableBuildingKind.Headquarters?p.HeadquartersVisionRange:p.OutpostVisionRange)).ToArray();
            return Threats(o,p).Where(e=>centers.Any(c=>Distance(e.Position,c.Position)<=(c.Kind==PlayableBuildingKind.Headquarters?p.HeadquartersVisionRange:p.OutpostVisionRange))).ToArray();
        }
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        private static double RouteDistance(PlayableRouteProof r)
        {var at=r.Origin;double result=0;foreach(var point in r.Path){result+=Distance(at,point);at=point;}return result;}
        // Compatible ground damage uses one configured horizon for both sides;
        // reload is DPS, with no fabricated health/stat multiplier.
        public static double Force(PlayableEntitySnapshot[] units,PlayableProfile p,AiProfile ai,AiRosterCatalog catalog=null)
        {
            var c=catalog??AiRosterCatalog.Initial;
            return units.Where(u=>c.For(u.Kind).targetTags.Contains("ground")).Sum(u=>
                (u.Kind==PlayableEntityKind.Tank?p.TankWeaponDamage*1000d/p.TankWeaponReloadMilliseconds:
                 u.Kind==PlayableEntityKind.Explorer?p.ExplorerDamage*p.ExplorerBurstSize*1000d/(p.ExplorerBurstPauseMs+p.ExplorerBurstSize*p.ExplorerBurstShotIntervalMs):
                 p.ShkvalDamage*1000d/p.ShkvalFireIntervalMs)*ai.Value("utility.forceHorizonSeconds")*Math.Min(1,u.Health/(double)PlayableUnitRules.Health(p,u.Kind)));
        }
        public bool Observe(PlayableAiObservation o,AiArmyRegistry registry,PlayableProfile p)
        {
            Bind(o);var threats=Threats(o,p);bool entered=threats.Length>0&&!emergencyObserved;emergencyObserved=threats.Length>0;
            if(!Active)return entered;
            var a=registry.Capture().Armies.FirstOrDefault(a=>a.Id==armyId&&a.Phase!=AiArmyPhase.Disbanded&&a.TacticalOwner==Policy);
            if(a==null){armyId=0;targetId=0;return entered;}
            if(threats.Length==0){registry.Disband(armyId);armyId=0;targetId=0;return entered;}
            var target=threats.FirstOrDefault(e=>e.Id==targetId);
            if(target==null){target=threats[0];targetId=target.Id;previousDistance=double.MaxValue;previousTargetHealth=-1;}
            var members=o.Entities.Where(u=>u.Owner==o.Owner&&a.Members.Contains(u.Id)).ToArray();
            double distance=members.Average(u=>Distance(u.Position,target.Position));
            bool progressed=distance<previousDistance&&previousDistance!=double.MaxValue||previousTargetHealth>=0&&target.Health<previousTargetHealth;
            previousDistance=Math.Min(previousDistance,distance);previousTargetHealth=target.Health;
            registry.UpdateMission(armyId,Policy,a.Members.Any(id=>o.Entities.Any(u=>u.Id==id&&u.TargetId==targetId))?AiArmyPhase.Engaging:AiArmyPhase.Advancing,o.Tick,"defend:"+targetId,rally:target.Position,progressed:progressed);
            return entered;
        }
        private sealed class Choice {public long ArmyId;public PlayableEntitySnapshot[] Units;public PlayableEntitySnapshot Target;public double Distance;}
        private static PlayableRouteProof Route(PlayableAiObservation o,PlayableEntitySnapshot u,PlayableEntitySnapshot target)=>
            o.RouteProofs.FirstOrDefault(r=>r.UnitId==u.Id&&r.Owner==o.Owner&&r.Generation==o.Generation&&r.Tick==o.Tick&&r.Origin.Equals(u.Position)&&r.Kind==PlayableRouteTargetKind.VisibleEnemy&&r.TargetId==target.Id);
        // O5 read-only coverage. Broad A3 threats/preemption remain unchanged.
        // A registry Active bit or Scheduled intent is not applied physical defense.
        public bool CoversTechnologyThreats(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiArmyRegistry registry,IEnumerable<PlayableAiTraceRecord> records)
        {
            Bind(o);var threats=Threats(o,p);if(threats.Length==0)return true;
            if(!Active||pendingId!=0||registry.OwnerId!=owner||registry.Generation!=generation)return false;
            var army=registry.Capture().Armies.SingleOrDefault(a=>a.Id==armyId&&a.OwnerId==owner&&a.TacticalOwner==Policy&&a.Role==AiArmyRole.MobileDefense&&a.Phase!=AiArmyPhase.Disbanded&&a.Objective=="defend:"+targetId);
            var target=threats.FirstOrDefault(t=>t.Id==targetId);if(army==null||target==null)return false;
            var members=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)).OrderBy(u=>u.Id).ToArray();
            if(members.Length<army.MinimumViableForce||members.Any(u=>!AppliedDefenseOrder(o,u,targetId,records)||!CertifiedDefenseRoute(o,u,target,p)))return false;
            // The defended visible target must lie in EACH threatened center's existing
            // defense region. A distant committed army cannot waive another front.
            foreach(var center in o.Buildings.Where(b=>b.Owner==o.Owner&&b.Health>0&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost)))
            {
                double region=center.Kind==PlayableBuildingKind.Headquarters?p.HeadquartersVisionRange:p.OutpostVisionRange;
                if(threats.Any(t=>Distance(t.Position,center.Position)<=region)&&Distance(target.Position,center.Position)>region)return false;
            }
            // Same sufficient-force comparison as ordinary A3 admission, for the union
            // of all threatened fronts (never spend one army's force twice).
            return Force(members,p,ai)>=Force(threats,p,ai);
        }
        private static bool AppliedDefenseOrder(PlayableAiObservation o,PlayableEntitySnapshot u,int target,IEnumerable<PlayableAiTraceRecord> records)
        {
            var order=u.CurrentOrder;var stamp=u.OrderStamp;
            return !u.Held&&order!=null&&order.Kind==PlayableTacticalOrderKind.Attack&&order.TargetId==target&&order.Owner==o.Owner&&order.Generation==o.Generation&&order.UnitId==u.Id&&
                stamp!=null&&stamp.Owner==o.Owner&&stamp.Generation==o.Generation&&stamp.UnitId==u.Id&&stamp.Origin==PlayableOrderOrigin.Ai&&stamp.Kind==PlayableCommandKind.Attack&&stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&
                stamp.ActionId>0&&stamp.JobId==stamp.ActionId&&stamp.Sequence==order.CommandSequence&&stamp.Tick==order.IssuedTick&&stamp.Tick<=o.Tick&&
                (records??Array.Empty<PlayableAiTraceRecord>()).Any(r=>r.Policy==Policy&&r.Kind==PlayableCommandKind.Attack&&r.OwnerId==o.OwnerId&&r.SourceIdentity==stamp.Source&&r.ActionId==stamp.ActionId&&
                    r.CommandSequence==stamp.Sequence&&r.ApplicationTick==stamp.Tick&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ReceiptIdentity?.Generation==o.Generation&&r.ReceiptIdentity.OwnerId==o.OwnerId);
        }
        private static bool CertifiedDefenseRoute(PlayableAiObservation o,PlayableEntitySnapshot u,PlayableEntitySnapshot target,PlayableProfile p)
        {
            var r=Route(o,u,target);return r!=null&&r.GeometryRevision>0&&r.Radius==PlayableUnitRules.Radius(p,u.Kind)&&r.Target.Equals(target.Position)&&r.Path.Count>0&&r.Path.Last().Equals(r.Goal);
        }
        // Current proof requests use the existing detached native observer/router. No
        // cached proof, hidden query delegate, attention/army claim or new state schema.
        public IReadOnlyList<PlayableRouteRequest> TechnologyCoverageRoutes(PlayableAiObservation o,PlayableProfile p,AiArmyRegistry registry,IEnumerable<PlayableAiTraceRecord> records)
        {
            Bind(o);if(!Active||pendingId!=0)return Array.Empty<PlayableRouteRequest>();
            var target=Threats(o,p).FirstOrDefault(t=>t.Id==targetId);if(target==null)return Array.Empty<PlayableRouteRequest>();
            var army=registry.Capture().Armies.FirstOrDefault(a=>a.Id==armyId&&a.TacticalOwner==Policy&&a.Phase!=AiArmyPhase.Disbanded);if(army==null)return Array.Empty<PlayableRouteRequest>();
            return o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&AppliedDefenseOrder(o,u,targetId,records)).OrderBy(u=>u.Id)
                .Select(u=>new PlayableRouteRequest(u.Id,PlayableRouteTargetKind.VisibleEnemy,target.Id,o.Generation,o.Tick,u.Position,PlayableUnitRules.Radius(p,u.Kind),target.Position)).ToArray();
        }
        public PlayableAiAction Propose(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiArmyRegistry registry)
        {
            Bind(o);if(registry==null||registry.OwnerId!=owner||registry.Generation!=generation)throw new ArgumentException("Defense registry binding");if(pendingId!=0)return null;
            var threats=Threats(o,p);if(threats.Length==0)return null;
            var own=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0).ToArray();
            var groups=registry.Capture().Armies.Where(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded&&(Active?a.Id==armyId:true)).Select(a=>new {Id=a.Id,Units=own.Where(u=>a.Members.Contains(u.Id)).ToArray()}).ToList();
            if(!Active&&groups.Count<registry.MajorCap)
            {
                // Scout/transport members are never recruited through a combat loophole.
                var free=own.Where(u=>registry.ArmyFor(u.Id)==0&&AiRosterCatalog.Initial.For(u.Kind).lineWeight>0).ToArray();
                if(free.Length>=ai.Value("armies.minimumUnits"))groups.Add(new{Id=0L,Units=free});
            }
            var choices=groups.SelectMany(g=>threats.Select(t=>
            {
                var reachable=g.Units.Where(u=>Route(o,u,t)!=null).OrderBy(u=>RouteDistance(Route(o,u,t))).ThenBy(u=>u.Id).ToArray();
                if(g.Id!=0&&reachable.Length!=g.Units.Length)return null; // Whole-group atomic ownership.
                if(g.Id==0)
                {
                    int count=(int)ai.Value("armies.minimumUnits");if(reachable.Length<count)return null;
                    while(count<reachable.Length&&Force(reachable.Take(count).ToArray(),p,ai)<Force(LocalThreats(o,p,t),p,ai))count++;
                    reachable=reachable.Take(count).ToArray();
                }
                if(reachable.Length==0||Force(reachable,p,ai)<Force(LocalThreats(o,p,t),p,ai))return null;
                return new Choice{ArmyId=g.Id,Units=reachable,Target=t,Distance=reachable.Average(u=>RouteDistance(Route(o,u,t)))};
            })).Where(c=>c!=null).OrderBy(c=>c.Distance).ThenBy(c=>c.ArmyId).ThenBy(c=>c.Target.Id).ToArray();
            var choice=choices.FirstOrDefault();if(choice==null)return null;
            var units=choice.Units;
            if(Active&&units.All(u=>u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&u.CurrentOrder.TargetId==choice.Target.Id))return null;
            return new PlayableAiAction(nextId,owner,o.ProfileId,o.ProfileRevision,generation,o.SnapshotSequence,PlayableCommandKind.Attack,units.Select(u=>u.Id).ToArray(),choice.Target.Position,targetId:choice.Target.Id,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
        public void Commit(PlayableAiObservation o,PlayableAiAction action,AiArmyRegistry registry)
        {
            Bind(o);if(action==null||action.ActionId!=nextId||pendingId!=0||action.PlayerId!=owner||action.Generation!=generation||action.SnapshotSequence!=o.SnapshotSequence||registry.OwnerId!=owner||registry.Generation!=generation||action.Kind!=PlayableCommandKind.Attack||!o.Entities.Any(e=>e.Id==action.TargetId&&o.IsHostile(e.Owner)&&e.Health>0))throw new InvalidOperationException("Defense commit binding");
            var id=registry.ArmyFor(action.EntityIds[0]);
            if(id==0){if(!registry.TryCreate(o,AiArmyRole.MobileDefense,Policy,action.EntityIds,out id))throw new InvalidOperationException("Defense slot admission");}
            else if(!registry.PreemptDefense(id,action.EntityIds))throw new InvalidOperationException("Defense atomic ownership admission");
            armyId=id;targetId=action.TargetId;emergencyObserved=true;
            previousDistance=o.Entities.Where(u=>u.Owner==o.Owner&&action.EntityIds.Contains(u.Id)).Average(u=>Distance(u.Position,action.Target));previousTargetHealth=o.Entities.Single(e=>e.Id==targetId&&o.IsHostile(e.Owner)).Health;
            registry.UpdateMission(id,Policy,AiArmyPhase.Staging,o.Tick,"defend:"+targetId,rally:action.Target);
            pendingId=nextId++;pendingGeneration=generation;
        }
        public void ObserveReceipt(PlayableAiTraceRecord r)
        {
            if(r==null||r.ActionId!=pendingId||r.OwnerId!=owner||!AiStateWire.CallbackGenerationMatches(r,pendingGeneration)||r.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity||r.Status==PlayableAiDeliveryStatus.Scheduled||r.Status==PlayableAiDeliveryStatus.Accepted)return;
            pendingId=pendingGeneration=0;
        }
        internal void ReconcileRegistry(AiArmyRegistry registry)
        {if(Active&&!registry.Capture().Armies.Any(a=>a.Id==armyId&&a.Phase!=AiArmyPhase.Disbanded&&a.TacticalOwner==Policy)){armyId=0;targetId=0;}}
        public void Stop(){emergencyObserved=false;armyId=0;targetId=0;pendingId=pendingGeneration=0;}
        internal void WriteState(BinaryWriter w){w.Write(armyId);w.Write(targetId);w.Write(nextId);w.Write(pendingId);w.Write(pendingGeneration);w.Write(emergencyObserved);WorldWire.Number(w,previousDistance);WorldWire.Number(w,previousTargetHealth);}
        internal void ReadState(BinaryReader r,long tick,AiArmyRegistry registry)
        {
            armyId=r.ReadInt64();targetId=r.ReadInt32();nextId=r.ReadInt64();pendingId=r.ReadInt64();pendingGeneration=r.ReadInt64();emergencyObserved=WorldWire.Boolean(r);previousDistance=WorldWire.Number(r);previousTargetHealth=WorldWire.Number(r);
            AiStateWire.Require(previousDistance>=0&&previousTargetHealth>=-1&&registry.Capture().Armies.Count(a=>a.TacticalOwner==Policy&&a.Phase!=AiArmyPhase.Disbanded)==(Active?1:0)&&armyId>=0&&nextId>pendingId&&pendingId>=0&&(pendingId==0?pendingGeneration==0:pendingGeneration==generation&&Active)&&(Active?emergencyObserved&&targetId>0&&registry.Capture().Armies.Any(a=>a.Id==armyId&&a.TacticalOwner==Policy&&a.Role==AiArmyRole.MobileDefense&&a.Phase!=AiArmyPhase.Disbanded&&a.Objective=="defend:"+targetId):targetId==0),"defense state/army/callback binding");
        }
    }
}
