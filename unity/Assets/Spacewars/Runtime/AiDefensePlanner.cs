using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if UNITY_INCLUDE_TESTS
using Newtonsoft.Json;
#endif
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Authority-owned defense transaction. Inputs are detached owner observations;
    // reachability and distance come from the ordinary native route proofs.
    public sealed class AiDefensePlanner
    {
#if UNITY_INCLUDE_TESTS
        private sealed class CoverageGuardReadback{public string Guard;public bool Evaluated;public bool? Pass;public object Data;}
        internal static Action<string> TechnologyCoverageReadbackForTests;
        internal static string TechnologyCoverageOwnerFilterForTests;
#endif
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
            Bind(o);var threats=Threats(o,p);bool entered=threats.Length>0&&!emergencyObserved;
            if(!Active){emergencyObserved=threats.Length>0;return entered;}
            var a=registry.Capture().Armies.FirstOrDefault(a=>a.Id==armyId&&a.Phase!=AiArmyPhase.Disbanded&&a.TacticalOwner==Policy);
            if(a==null){armyId=0;targetId=0;emergencyObserved=threats.Length>0;return entered;}
            if(threats.Length==0)
            {
                if(a.Reinforcements.Length>0)
                {
                    // Keep the Defense lifecycle long enough to reassemble members
                    // whose safe routes were unavailable during the visible threat.
                    emergencyObserved=true;
                    registry.UpdateMission(armyId,Policy,AiArmyPhase.Staging,o.Tick,"defend:"+targetId,rally:a.Rally);
                    return entered;
                }
                registry.Disband(armyId);armyId=0;targetId=0;emergencyObserved=false;return entered;
            }
            emergencyObserved=true;
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
#if UNITY_INCLUDE_TESTS
            if(TechnologyCoverageReadbackForTests!=null&&TechnologyCoverageOwnerFilterForTests==o.OwnerId)
                return CoversTechnologyThreatsReadback(o,p,ai,registry,records,threats);
#endif
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
#if UNITY_INCLUDE_TESTS
        private bool CoversTechnologyThreatsReadback(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiArmyRegistry registry,IEnumerable<PlayableAiTraceRecord> records,PlayableEntitySnapshot[] threats)
        {
            var guards=new List<CoverageGuardReadback>();bool Guard(string name,bool pass,object data){guards.Add(new CoverageGuardReadback{Guard=name,Evaluated=true,Pass=pass,Data=data});return pass;}
            if(!Guard("Active",Active,new{Active,ArmyId=armyId}))return CoverageResult(false,guards,o);
            if(!Guard("Pending",pendingId==0,new{PendingId=pendingId,PendingGeneration=pendingGeneration}))return CoverageResult(false,guards,o);
            if(!Guard("Owner",registry.OwnerId==owner,new{Expected=owner,Actual=registry.OwnerId}))return CoverageResult(false,guards,o);
            if(!Guard("Generation",registry.Generation==generation,new{Expected=generation,Actual=registry.Generation}))return CoverageResult(false,guards,o);
            var captured=registry.Capture();var army=captured.Armies.SingleOrDefault(a=>a.Id==armyId&&a.OwnerId==owner&&a.TacticalOwner==Policy&&a.Role==AiArmyRole.MobileDefense&&a.Phase!=AiArmyPhase.Disbanded&&a.Objective=="defend:"+targetId);
            if(!Guard("ArmyBinding",army!=null,new{ArmyId=armyId,TargetId=targetId,RegistryOwner=captured.OwnerId,RegistryGeneration=captured.Generation,Army=army==null?null:new{army.Id,army.OwnerId,army.TacticalOwner,army.Role,army.Phase,army.Objective,Members=army.Members.ToArray(),Reinforcements=army.Reinforcements.ToArray(),army.MinimumViableForce}}))return CoverageResult(false,guards,o);
            var target=threats.FirstOrDefault(t=>t.Id==targetId);
            if(!Guard("VisibleTarget",target!=null,new{TargetId=targetId,ThreatIds=threats.Select(t=>t.Id).ToArray(),Target=target==null?null:new{target.Id,target.Owner,target.Health,target.Position}}))return CoverageResult(false,guards,o);
            var members=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)).OrderBy(u=>u.Id).ToArray();
            if(!Guard("MinimumViableMembers",members.Length>=army.MinimumViableForce,new{Actual=members.Length,Required=army.MinimumViableForce,MemberIds=members.Select(u=>u.Id).ToArray()}))return CoverageResult(false,guards,o);
            foreach(var member in members)
            {
                bool applied=AppliedDefenseOrderReadback(o,member,targetId,records,guards);
                if(!applied)return CoverageResult(false,guards,o);
                bool route=CertifiedDefenseRouteReadback(o,member,target,p,guards);
                if(!route)return CoverageResult(false,guards,o);
            }
            foreach(var center in o.Buildings.Where(b=>b.Owner==o.Owner&&b.Health>0&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost)))
            {
                double region=center.Kind==PlayableBuildingKind.Headquarters?p.HeadquartersVisionRange:p.OutpostVisionRange;
                int firstLocalThreat=-1;foreach(var threat in threats)if(Distance(threat.Position,center.Position)<=region){firstLocalThreat=threat.Id;break;}
                if(firstLocalThreat<0)continue;
                double targetDistance=Distance(target.Position,center.Position);bool inRegion=targetDistance<=region;
                if(!Guard("CenterFrontRegion",inRegion,new{CenterId=center.Id,center.Kind,center.Position,Region=region,TargetId=target.Id,TargetDistance=targetDistance,ThreatId=firstLocalThreat}))return CoverageResult(false,guards,o);
            }
            double memberForce=Force(members,p,ai),threatForce=Force(threats,p,ai);bool sufficient=memberForce>=threatForce;
            Guard("SufficientForce",sufficient,new{MemberForce=memberForce,ThreatForce=threatForce,MemberIds=members.Select(u=>u.Id).ToArray(),ThreatIds=threats.Select(t=>t.Id).ToArray()});
            return CoverageResult(sufficient,guards,o);
        }
        private bool CoverageResult(bool covered,List<CoverageGuardReadback> guards,PlayableAiObservation o)
        {
            TechnologyCoverageReadbackForTests(JsonConvert.SerializeObject(new{OwnerId=o.OwnerId,Tick=o.Tick,ArmyId=armyId,TargetId=targetId,Covered=covered,FirstFailedGuard=guards.FirstOrDefault(g=>g.Pass==false)?.Guard,Unreached="NotEvaluated",Guards=guards}));
            return covered;
        }
        private static bool AppliedDefenseOrderReadback(PlayableAiObservation o,PlayableEntitySnapshot u,int target,IEnumerable<PlayableAiTraceRecord> records,List<CoverageGuardReadback> guards)
        {
            var order=u.CurrentOrder;var stamp=u.OrderStamp;bool held=!u.Held;
            if(!CoverageGuard(guards,"AppliedDefenseOrder.CurrentOrder",held,new{u.Id,u.Held}))return false;
            if(!CoverageGuard(guards,"AppliedDefenseOrder.CurrentOrderPresent",order!=null,new{UnitId=u.Id,Present=order!=null}))return false;
            if(!CoverageGuard(guards,"AppliedDefenseOrder.Kind",order.Kind==PlayableTacticalOrderKind.Attack,new{UnitId=u.Id,order.Kind}))return false;
            if(!CoverageGuard(guards,"AppliedDefenseOrder.Target",order.TargetId==target,new{UnitId=u.Id,Expected=target,Actual=order.TargetId}))return false;
            if(!CoverageGuard(guards,"AppliedDefenseOrder.Owner",order.Owner==o.Owner,new{UnitId=u.Id,Expected=o.Owner,Actual=order.Owner}))return false;
            if(!CoverageGuard(guards,"AppliedDefenseOrder.Generation",order.Generation==o.Generation,new{UnitId=u.Id,Expected=o.Generation,Actual=order.Generation}))return false;
            if(!CoverageGuard(guards,"AppliedDefenseOrder.Unit",order.UnitId==u.Id,new{Expected=u.Id,Actual=order.UnitId}))return false;
            bool stampMatches=stamp!=null&&stamp.Owner==o.Owner&&stamp.Generation==o.Generation&&stamp.UnitId==u.Id&&stamp.Origin==PlayableOrderOrigin.Ai&&stamp.Kind==PlayableCommandKind.Attack&&stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&stamp.ActionId>0&&stamp.JobId==stamp.ActionId&&stamp.Sequence==order.CommandSequence&&stamp.Tick==order.IssuedTick&&stamp.Tick<=o.Tick;
            if(!CoverageGuard(guards,"AppliedDefenseOrder.Stamp",stampMatches,new{Stamp=stamp==null?null:new{stamp.Owner,stamp.Generation,stamp.UnitId,stamp.Origin,stamp.Kind,stamp.Source,stamp.ActionId,stamp.JobId,stamp.Sequence,stamp.Tick}}))return false;
            var applied=(records??Array.Empty<PlayableAiTraceRecord>()).FirstOrDefault(r=>r.Policy==Policy&&r.Kind==PlayableCommandKind.Attack&&r.OwnerId==o.OwnerId&&r.SourceIdentity==stamp.Source&&r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence&&r.ApplicationTick==stamp.Tick&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ReceiptIdentity?.Generation==o.Generation&&r.ReceiptIdentity.OwnerId==o.OwnerId);
            return CoverageGuard(guards,"AppliedDefenseOrder.AppliedReceipt",applied!=null,new{Receipt=applied==null?null:new{applied.Policy,applied.Kind,applied.OwnerId,applied.SourceIdentity,applied.ActionId,applied.CommandSequence,applied.ApplicationTick,applied.Status,applied.RuntimeStatus,ReceiptIdentity=applied.ReceiptIdentity}});
        }
        private static bool CertifiedDefenseRouteReadback(PlayableAiObservation o,PlayableEntitySnapshot u,PlayableEntitySnapshot target,PlayableProfile p,List<CoverageGuardReadback> guards)
        {
            var r=Route(o,u,target);bool certified=r!=null&&r.GeometryRevision>0&&r.Radius==PlayableUnitRules.Radius(p,u.Kind)&&r.Target.Equals(target.Position)&&r.Path.Count>0&&r.Path.Last().Equals(r.Goal);
            return CoverageGuard(guards,"CertifiedDefenseRoute",certified,new{UnitId=u.Id,Proof=r==null?null:new{r.UnitId,r.Owner,r.Generation,r.Tick,r.GeometryRevision,r.Radius,r.Target,Path=r.Path.ToArray(),r.Goal}});
        }
        private static bool CoverageGuard(List<CoverageGuardReadback> guards,string name,bool pass,object data){guards.Add(new CoverageGuardReadback{Guard=name,Evaluated=true,Pass=pass,Data=data});return pass;}
#endif
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
            var groups=registry.Capture().Armies.Where(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded&&(Active?a.Id==armyId:true)).Select(a=>new
                {Id=a.Id,Units=own.Where(u=>a.Members.Contains(u.Id)&&(!Active||!a.Reinforcements.Contains(u.Id))).ToArray()}).ToList();
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
        internal PlayableAiAction ProposeAssembly(PlayableAiObservation o,PlayableProfile p,AiArmyRegistry registry,IEnumerable<PlayableAiTraceRecord> records)
        {
            Bind(o);if(!Active||pendingId!=0||registry.OwnerId!=owner||registry.Generation!=generation)return null;
            var army=registry.Capture().Armies.FirstOrDefault(a=>a.Id==armyId&&a.TacticalOwner==Policy&&a.Phase!=AiArmyPhase.Disbanded);
            return army==null?null:AiTacticalExecutor.Propose(o,army,p,nextId,assemblyOnly:true,records:records);
        }
        internal void CommitAssembly(PlayableAiObservation o,PlayableAiAction action,AiArmyRegistry registry,PlayableProfile p,IEnumerable<PlayableAiTraceRecord> records)
        {
            Bind(o);if(action==null||action.ActionId!=nextId||pendingId!=0||!Active||action.PlayerId!=owner||action.Generation!=generation||
                action.SnapshotSequence!=o.SnapshotSequence||registry.OwnerId!=owner||registry.Generation!=generation||
                action.Kind!=PlayableCommandKind.Move&&action.Kind!=PlayableCommandKind.Follow&&action.Kind!=PlayableCommandKind.Stop)
                throw new InvalidOperationException("Defense assembly commit binding");
            var army=registry.Capture().Armies.FirstOrDefault(a=>a.Id==armyId&&a.TacticalOwner==Policy&&a.Phase!=AiArmyPhase.Disbanded);
            if(army==null||action.EntityIds.Count==0||action.EntityIds.Any(id=>!army.Members.Contains(id)||!army.Reinforcements.Contains(id)))
                throw new InvalidOperationException("Defense assembly membership");
            bool route=action.Kind==PlayableCommandKind.Stop?
                action.EntityIds.Count==1&&AiTacticalExecutor.IsCurrentAppliedDefenseAssemblyOrder(o,o.Entities.SingleOrDefault(u=>u.Id==action.EntityIds[0]),records):
                action.Kind==PlayableCommandKind.Follow?
                action.EntityIds.Count==1&&o.RouteProofs.Any(r=>r.UnitId==action.EntityIds[0]&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.TargetId==action.TargetId&&
                    o.Entities.Any(e=>e.Id==action.TargetId&&army.Members.Contains(e.Id)&&!army.Reinforcements.Contains(e.Id))&&
                    r.Target.Equals(PlayableUnitRules.FollowGoal(p,o.Entities.Single(e=>e.Id==action.TargetId).Position,o.Entities.Single(e=>e.Id==action.TargetId).HullHeading))&&AiTacticalExecutor.SafeRoute(o,r,p)):
                action.EntityIds.All(id=>o.RouteProofs.Any(r=>r.UnitId==id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.Goal.Equals(action.Target)&&
                    (army.Members.Contains(r.TargetId)&&!army.Reinforcements.Contains(r.TargetId)||o.Buildings.Any(b=>b.Id==r.TargetId&&b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready))&&
                    AiTacticalExecutor.SafeRoute(o,r,p)));
            if(!route)throw new InvalidOperationException("Defense assembly route is not currently safe");
            pendingId=nextId++;pendingGeneration=generation;
        }
        public void Commit(PlayableAiObservation o,PlayableAiAction action,AiArmyRegistry registry)
        {
            Bind(o);if(action==null||action.ActionId!=nextId||pendingId!=0||action.PlayerId!=owner||action.Generation!=generation||action.SnapshotSequence!=o.SnapshotSequence||registry.OwnerId!=owner||registry.Generation!=generation||action.Kind!=PlayableCommandKind.Attack||!o.Entities.Any(e=>e.Id==action.TargetId&&o.IsHostile(e.Owner)&&e.Health>0))throw new InvalidOperationException("Defense commit binding");
            var id=registry.ArmyFor(action.EntityIds[0]);
            if(id==0){if(!registry.TryCreate(o,AiArmyRole.MobileDefense,Policy,action.EntityIds,out id))throw new InvalidOperationException("Defense slot admission");}
            else if(!(Active&&id==armyId&&registry.Capture().Armies.Any(a=>a.Id==id&&a.TacticalOwner==Policy))&&!registry.PreemptDefense(id,action.EntityIds))throw new InvalidOperationException("Defense atomic ownership admission");
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
