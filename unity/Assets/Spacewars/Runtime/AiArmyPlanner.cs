using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    [Serializable] public sealed class AiArmyMissionState
    {
        public long ArmyId,StartedTick,PhaseTick,ProgressTick,LastInitiativeTick,LastSeenTick;
        public int TargetId; public PlayableRouteTargetKind TargetKind;
        public NavPoint Target; public AiArmyPhase Phase;
        public string Reason; public bool TargetVisible;
        // Detached diagnostic projection of the existing saved memo; not new wire state.
        public IReadOnlyDictionary<string,long> RepairClients{get;internal set;}
        public AiArmyMissionState Copy()=>(AiArmyMissionState)MemberwiseClone();
    }
    // A live owner observation, constructed only from the planner's terminal receipt
    // plus currently effective registry-owned orders. This is not a legacy mission.
    public sealed class AiArmyDeployment
    {
        internal AiArmyDeployment(PlayableAiObservation o,long army,long started)
        {OwnerId=o.OwnerId;Generation=o.Generation;Seed=o.Seed;ProfileId=o.ProfileId;ProfileRevision=o.ProfileRevision;ObservedTick=o.Tick;ArmyId=army;StartedTick=started;}
        public string OwnerId{get;} public long Generation{get;} public int Seed{get;} public string ProfileId{get;} public int ProfileRevision{get;}
        public long ObservedTick{get;} public long ArmyId{get;} public long StartedTick{get;}
    }
    // Owner-safe mission lifecycle. Propose is a pure query; Commit is invoked only
    // by the shared arbiter. Movement/combat continue through ordinary game commands.
    public sealed class AiArmyPlanner
    {
        public const string Policy="army-mission";
        public const string FlankPolicy="army-flank";
        private readonly string policyName;
        internal static bool IsArmyPolicy(string value)=>value==Policy||value==FlankPolicy;
        internal bool HarassAllowed{get;set;}
        internal AiPersonality? Personality{get;set;}
        internal bool SplitLargeArmies{get;set;}
        internal int OtherTargetId{get;set;}
        internal AiKnowledgeState Knowledge{get;set;}
        private readonly string owner;
        private readonly long generation;
        private AiArmyMissionState state=new AiArmyMissionState{Phase=AiArmyPhase.Disbanded,Reason="no mission"};
        private long nextId=1,pendingId,pendingGeneration;
        internal AiTeamAssignment TeamAssignment{get;set;}
        internal int OpeningMinimum{get;set;}
        internal int ProtectedUnit{get;set;}
        internal int[] OpeningMembers{get;set;}
        internal System.Collections.Generic.ISet<int> UtilityMembers{get;set;}
        private AiProfile AiProfileForReason;
        private double distance=double.MaxValue,targetHealth=-1;
        private bool applied;
        private Dictionary<string,long> completed=new Dictionary<string,long>(StringComparer.Ordinal);
        public AiArmyPlanner(string ownerId,long generation,string tacticalPolicy=Policy)
        {if(string.IsNullOrWhiteSpace(ownerId)||generation<=0)throw new ArgumentException("Mission binding");owner=ownerId;this.generation=generation;policyName=tacticalPolicy;if(!IsArmyPolicy(policyName))throw new ArgumentException("Army tactical policy");}
        public AiArmyMissionState Capture()
        {var copy=state.Copy();copy.RepairClients=new System.Collections.ObjectModel.ReadOnlyDictionary<string,long>(completed.Where(x=>AiRepairUsePolicy.Parse(x.Key,out _,out _,out _)).ToDictionary(x=>x.Key,x=>x.Value));return copy;}
        public bool Active=>state.ArmyId!=0;
        internal long PendingId=>pendingId;
        internal long PendingGeneration=>pendingGeneration;
        internal bool HasAppliedOrder=>applied;
        internal bool CanDeliverReinforcement(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiArmyRegistry registry,PlayableAiAction action)
        {
            var army=registry.CaptureArmy(state.ArmyId).SingleOrDefault(a=>a.Id==state.ArmyId&&a.TacticalOwner==policyName);
            if(army==null)return false;
            if(state.Reason==AiReinforcementPolicy.RegroupReason)return action.Kind==PlayableCommandKind.Move||action.Kind==PlayableCommandKind.Stop;
            if(!action.EntityIds.Any(army.Reinforcements.Contains))return true;
            if(action.Kind==PlayableCommandKind.Move||action.Kind==PlayableCommandKind.Stop)return true;
            var core=AiReinforcementPolicy.ReceivingCore(o,army,p,ai,Knowledge);
            return action.Kind==PlayableCommandKind.Follow?core.Any(c=>c.Id==action.TargetId):
                core.Length>0&&action.EntityIds.Where(army.Reinforcements.Contains).All(id=>AiTacticalExecutor.Ready(o,army,p,Knowledge).Any(u=>u.Id==id));
        }
        internal int ReinforcementPriority(PlayableAiObservation o,AiArmyRegistry registry,PlayableAiAction action)
        {
            if(action==null||!Active||action.Kind!=PlayableCommandKind.Move&&action.Kind!=PlayableCommandKind.Follow&&action.Kind!=PlayableCommandKind.Stop)return 0;
            var army=registry.CaptureArmy(state.ArmyId).SingleOrDefault(a=>a.TacticalOwner==policyName);
            // Finish waiting client obligations before refreshing another front.
            // Existing arbiter aging, group cooldown and micro custody still apply.
            return army!=null&&o.Entities.Any(u=>u.Owner==o.Owner&&AiReinforcementPolicy.Available(u)&&army.Reinforcements.Contains(u.Id)&&action.EntityIds.Contains(u.Id)&&
                (u.CurrentOrder==null||action.Kind==PlayableCommandKind.Stop))?1:0;
        }
        // OwnerLoop actions are globally reidentified; pendingId and the saved
        // repair memo retain the local policy action supplied by that owner item.
        internal bool OwnsPendingRepairReturn(PlayableAiAction action,long localAction)
            =>Active&&pendingId==localAction&&pendingGeneration==generation&&action!=null&&action.PlayerId==owner&&action.Generation==generation&&
                action.SourceIdentity==PlayableAiOpeningComposition.SourceIdentity&&AiRepairUsePolicy.Pending(completed,action,localAction,out _);
        internal bool CanDeliverRepairReturn(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiArmyRegistry registry,PlayableAiAction action,long localAction)
        {
            if(!OwnsPendingRepairReturn(action,localAction)||o.OwnerId!=owner||o.Generation!=generation||registry.OwnerId!=owner||registry.Generation!=generation)return false;
            var army=registry.CaptureArmy(state.ArmyId).SingleOrDefault(a=>a.Id==state.ArmyId&&a.TacticalOwner==policyName);
            if(!AiRepairUsePolicy.CanDeliver(o,p,ai,completed,action,localAction,army))return false;
            var clients=AiRepairUsePolicy.Clients(completed);
            var core=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&!u.Held&&!u.Departing&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&
                !clients.Contains(u.Id)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0).ToArray();
            return core.Length>=army.MinimumViableForce&&Viable(core,p,ai,AiRepairUsePolicy.GuardMission(army)?army.MinimumViableForce:0);
        }
        private void Bind(PlayableAiObservation o)
        {if(o==null||o.OwnerId!=owner||o.Generation!=generation)throw new ArgumentException("Mission observation binding");}
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        private static string Key(PlayableRouteTargetKind kind,int id)=>((int)kind)+":"+id;
        private PlayableEntitySnapshot[] Fighters(PlayableAiObservation o)=>o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&u.Id!=ProtectedUnit&&(UtilityMembers==null||!UtilityMembers.Contains(u.Id))&&(OpeningMembers==null||OpeningMembers.Contains(u.Id))&&!u.Held&&!(u.CurrentOrder!=null&&u.OrderStamp?.Origin==PlayableOrderOrigin.Human)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0).OrderBy(u=>u.Id).ToArray();
        private bool Viable(IEnumerable<PlayableEntitySnapshot> units,PlayableProfile p,AiProfile ai,int minimum=0)
        {
            // Initial line catalog is Tank. Use gameplay DPS on the common horizon and
            // current health; three critically damaged bodies are not three viable units.
            double horizon=ai.Value("utility.forceHorizonSeconds");
            double reference=p.TankWeaponDamage*1000d/p.TankWeaponReloadMilliseconds*horizon;
            return units.Sum(u=>p.TankWeaponDamage*1000d/p.TankWeaponReloadMilliseconds*horizon*Math.Min(1,PlayableUnitRules.HealthRatio(p,u)))>=reference*(minimum>0?minimum:OpeningMinimum>0?OpeningMinimum:ai.Value("armies.minimumUnits"));
        }
        private static bool Same(PlayableEntitySnapshot u,AiArmyMissionState s)=>u.CurrentOrder!=null&&
            (s.TargetKind==PlayableRouteTargetKind.VisibleEnemy?u.CurrentOrder.Kind==PlayableTacticalOrderKind.Attack&&u.CurrentOrder.TargetId==s.TargetId:
            u.CurrentOrder.Kind==(s.TargetKind==PlayableRouteTargetKind.FriendlyAnchor?PlayableTacticalOrderKind.Move:PlayableTacticalOrderKind.AttackMove)&&u.CurrentOrder.Destination.Equals(s.Target));
        private bool SameLivePending(PlayableEntitySnapshot u,AiArmyMissionState s)
        {
            var pending=u.Queue?.Pending;
            if(!applied||s.TargetKind!=PlayableRouteTargetKind.PublicObjective||u.Queue?.PendingAiRouteViable!=true||pending==null||
                pending.AiActionId<=0||pending.Kind!=PlayableCommandKind.AttackMove||pending.PublicObjectiveId!=s.TargetId||!pending.Anchor.HasValue||!pending.Anchor.Value.Position.Equals(s.Target))return false;
            // `applied` is mission-owned checkpoint state. The live, owner-only
            // queue projection proves this exact source/request is still viable;
            // debug trace retention is deliberately not part of that proof.
            return true;
        }
        // A public site ID is a different namespace from world entity IDs.
        // Seeing a colliding world object must not replace its accepted approach.
        private static double Health(PlayableAiObservation o,AiArmyMissionState s)=>s.TargetKind!=PlayableRouteTargetKind.VisibleEnemy?-1:
            o.Entities.Where(u=>u.Id==s.TargetId&&o.IsHostile(u.Owner)&&u.Health>0).Select(u=>(double)u.Health)
            .Concat(o.Buildings.Where(b=>b.Id==s.TargetId&&o.IsHostile(b.Owner)&&b.Health>0).Select(b=>(double)b.Health)).DefaultIfEmpty(-1).First();
        private static NavPoint Point(PlayableAiObservation o,AiArmyMissionState s)=>s.TargetKind!=PlayableRouteTargetKind.VisibleEnemy?s.Target:o.Entities.Where(u=>u.Id==s.TargetId&&o.IsHostile(u.Owner)&&u.Health>0).Select(u=>u.Position)
            .Concat(o.Buildings.Where(b=>b.Id==s.TargetId&&o.IsHostile(b.Owner)&&b.Health>0).Select(b=>b.Position)).DefaultIfEmpty(s.Target).First();
        // Effective assembly is proven by authority provenance, not by a receipt alone.
        internal static bool AssemblyFollow(PlayableAiObservation o,AiArmyState army,PlayableEntitySnapshot unit,IEnumerable<PlayableAiTraceRecord> records)
        {
            var order=unit.CurrentOrder;var stamp=unit.OrderStamp;
            if(order?.Kind!=PlayableTacticalOrderKind.Follow||stamp==null||unit.Owner!=o.Owner||unit.Health<=0||
                stamp.UnitId!=unit.Id||stamp.Owner!=o.Owner||stamp.Generation!=o.Generation||stamp.Origin!=PlayableOrderOrigin.Ai||
                stamp.Kind!=PlayableCommandKind.Follow||stamp.Source!=PlayableAiOpeningComposition.SourceIdentity||stamp.ActionId<=0||stamp.JobId!=stamp.ActionId||
                order.Generation!=o.Generation||order.Owner!=o.Owner||order.UnitId!=unit.Id||order.CommandSequence!=stamp.Sequence||order.IssuedTick!=stamp.Tick)return false;
            if(!o.Entities.Any(c=>c.Id==order.TargetId&&c.Id!=unit.Id&&c.Owner==o.Owner&&c.Health>0&&army.Members.Contains(c.Id)&&!army.Reinforcements.Contains(c.Id)&&AiRosterCatalog.Initial.LineWeight(c.Kind)>0))return false;
            return (records??Array.Empty<PlayableAiTraceRecord>()).Any(r=>r.Policy==army.TacticalOwner&&r.Kind==PlayableCommandKind.Follow&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&
                r.OwnerId==o.OwnerId&&r.SourceIdentity==stamp.Source&&r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence&&r.ApplicationTick==stamp.Tick&&r.ApplicationTick<=o.Tick&&
                r.ReceiptIdentity?.Generation==o.Generation&&r.ReceiptIdentity.OwnerId==o.OwnerId);
        }
        private static PlayableBuildingSnapshot[] GuardAssets(PlayableAiObservation o)=>o.Buildings.Where(b=>b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&
            (b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost||b.Kind==PlayableBuildingKind.Mine)).ToArray();
        private AiArmyState[] QuietGuards(AiArmyRegistry registry)=>registry.Capture().Armies.Where(a=>a.Phase!=AiArmyPhase.Disbanded&&a.Role==AiArmyRole.MobileDefense&&
            a.TacticalOwner==policyName&&a.Objective!=null&&a.Objective.StartsWith(((int)PlayableRouteTargetKind.FriendlyAnchor)+":",StringComparison.Ordinal)).ToArray();
        private bool EconomicDefense(PlayableAiObservation o,PlayableAiOpeningCompositionState opening)=>
            policyName==Policy&&OpeningMinimum==0&&TeamAssignment?.Helping!=true&&
            AiActiveStrategyPolicy.HoldForDevelopment(o,oProfile,AiProfileForReason??AiProfile.Initial,completed);
        private void ReconcileQuietGuards(PlayableAiObservation o,AiArmyRegistry registry,AiProfile profile,PlayableAiOpeningCompositionState opening,bool allowsPressure)
        {
            var assets=GuardAssets(o);
            // Keep this real recovery obligation through quiet-guard redistribution,
            // but never beyond the existing recovery budget or after the client dies.
            if(Active&&state.TargetKind==PlayableRouteTargetKind.FriendlyAnchor&&AiRepairUsePolicy.HasLiveRecoveryBudget(o,profile,completed))return;
            foreach(var guard in QuietGuards(registry))
            {
                if(guard.Id==state.ArmyId&&pendingId!=0)continue;
                if(!assets.Any(b=>guard.Objective==Key(PlayableRouteTargetKind.FriendlyAnchor,b.Id))||
                    o.Entities.Any(u=>guard.Members.Contains(u.Id)&&(u.Held||u.CurrentOrder!=null&&u.OrderStamp?.Origin==PlayableOrderOrigin.Human)))registry.Disband(guard.Id);
            }
            ReconcileRegistry(registry,o.Tick,"quiet guard assignment released");
            if(pendingId!=0||Active&&state.TargetKind!=PlayableRouteTargetKind.FriendlyAnchor)return;
            var guards=QuietGuards(registry);var guardIds=new HashSet<long>(guards.Select(a=>a.Id));
            var available=Fighters(o).Where(u=>registry.ArmyFor(u.Id)==0||guardIds.Contains(registry.ArmyFor(u.Id))).ToArray();
            if(allowsPressure&&!EconomicDefense(o,opening))
            {
                var target=OffensiveRoutes(o,available).GroupBy(r=>Key(r.Kind,r.TargetId))
                    .Where(g=>Viable(available.Where(u=>g.Any(r=>r.UnitId==u.Id)),oProfile,profile))
                    .Where(g=>AiActiveStrategyPolicy.ViableApproach(o,oProfile,profile,available.Where(u=>g.Any(r=>r.UnitId==u.Id)).ToArray(),g.First().TargetId,g.First().Target,g.First().Kind,Knowledge,completed))
                    .OrderBy(g=>g.First().Kind).ThenBy(g=>g.First().TargetId).FirstOrDefault();
                if(target!=null)
                {
                    var eligible=new HashSet<int>(target.Select(r=>r.UnitId));
                    var free=available.Where(u=>registry.ArmyFor(u.Id)==0&&eligible.Contains(u.Id)).ToList();
                    var released=new List<AiArmyState>();int majors=registry.Capture().Armies.Count(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded);
                    foreach(var guard in guards.OrderBy(a=>assets.Any(b=>b.Kind==PlayableBuildingKind.Headquarters&&a.Objective==Key(PlayableRouteTargetKind.FriendlyAnchor,b.Id))?0:1)
                        .ThenByDescending(a=>available.Where(u=>a.Members.Contains(u.Id)&&eligible.Contains(u.Id)).Select(u=>Distance(u.Position,target.First().Target)).DefaultIfEmpty(0).Min()).ThenBy(a=>a.Id))
                    {
                        if(Viable(free,oProfile,profile)&&majors-released.Count<registry.MajorCap)break;
                        released.Add(guard);free.AddRange(available.Where(u=>guard.Members.Contains(u.Id)&&eligible.Contains(u.Id)));
                    }
                    if(Viable(free,oProfile,profile)&&majors-released.Count<registry.MajorCap)
                    {
                        foreach(var guard in released)registry.Disband(guard.Id);
                        // Other completed guard groups keep their registry-owned position;
                        // only the planner cursor is released for the new offensive mission.
                        if(Active)Release(registry,o.Tick,"territory force ready for pressure",disband:false);
                        return;
                    }
                }
            }
            // Split an old concentrated assignment when additional reachable assets
            // need coverage. Group count and membership admission stay authoritative.
            int otherMajors=registry.Capture().Armies.Count(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded&&!guardIds.Contains(a.Id));
            int goals=Math.Min(registry.MajorCap-otherMajors,assets.Count(b=>available.Any(u=>o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.TargetId==b.Id&&AiTacticalExecutor.SafeRoute(o,r,oProfile)))));
            if(goals>guards.Length&&available.Length>0)
            {
                int share=(available.Length+goals-1)/goals;
                foreach(var guard in guards.Where(a=>a.Members.Length>share))registry.Disband(guard.Id);
                ReconcileRegistry(registry,o.Tick,"spread quiet defense across owned assets");
            }
        }
        public void Observe(PlayableAiObservation o,AiArmyRegistry registry,AiProfile profile,IEnumerable<PlayableAiTraceRecord> records=null,PlayableAiOpeningCompositionState opening=null,bool allowsPressure=true,bool replanQuietGuards=true)
        {
            Bind(o);AiProfileForReason=profile;AiActiveStrategyPolicy.ObserveFortifications(o,Knowledge,completed);
            // Observe alone advances the saved bounded development window. Queries
            // and unselected proposals never create/renew strategic commitments.
            if(OpeningMinimum==0)AiActiveStrategyPolicy.ObserveDevelopment(o,oProfile,profile,completed);
            if(replanQuietGuards)ReconcileQuietGuards(o,registry,profile,opening,allowsPressure);if(!Active)return;
            var army=registry.CaptureArmy(state.ArmyId).FirstOrDefault(a=>a.Id==state.ArmyId&&a.Phase!=AiArmyPhase.Disbanded);
            if(army==null){Release(registry,o.Tick,"force lost");return;}
            if(army.TacticalOwner!=policyName){Release(registry,o.Tick,"tactical owner changed",disband:false);return;}
            var existingRepairClients=AiRepairUsePolicy.Clients(completed);
            if(existingRepairClients.Count==0&&(state.Reason==AiReinforcementPolicy.RegroupReason||
                army.Reinforcements.Length>0&&AiReinforcementPolicy.ReceivingCore(o,army,oProfile,profile,Knowledge).Length==0))
            {
                if(state.Reason!=AiReinforcementPolicy.RegroupReason){state.Reason=AiReinforcementPolicy.RegroupReason;SetPhase(registry,AiArmyPhase.Regrouping,o.Tick);}
                registry.UpdateMission(army.Id,policyName,AiArmyPhase.Regrouping,o.Tick,Key(state.TargetKind,state.TargetId),rally:state.Target);
                var survivors=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)).ToArray();
                if(survivors.Any(u=>!army.Reinforcements.Contains(u.Id)&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&!AssemblyFollow(o,army,u,records)))
                {Release(registry,o.Tick,"regroup assembly order replaced");return;}
                var unavailable=survivors.Where(u=>!AiReinforcementPolicy.Available(u)).Select(u=>u.Id).ToArray();
                if(unavailable.Length>0){registry.ReleaseMembers(army.Id,policyName,unavailable);army=registry.CaptureArmy(army.Id).Single();survivors=survivors.Where(AiReinforcementPolicy.Available).ToArray();}
                if(survivors.Length==0){Release(registry,o.Tick,"regroup members unavailable");return;}
                bool assembled=survivors.All(u=>AiReinforcementPolicy.AtHome(o,u,oProfile,Knowledge)&&AiReinforcementPolicy.AppliedAssembly(o,army,u,records));
                if(assembled&&survivors.Any(c=>survivors.All(u=>Distance(u.Position,c.Position)<=profile.Value("safeReinforcements.coreRadius"))&&
                    Viable(survivors.Where(u=>AiRosterCatalog.Initial.LineWeight(u.Kind)>0),oProfile,profile,army.MinimumViableForce)))
                {Release(registry,o.Tick,"safe replacement group assembled");return;}
                return;
            }
            // Keep the retreat's original arrival obligations stable. Ordinary
            // waves can join once that core has actually entered recovery.
            if((!Withdrawing||state.Phase==AiArmyPhase.Recovering)&&AiReinforcementPolicy.ReceivingCore(o,army,oProfile,profile,Knowledge).Length>0)
                registry.JoinReinforcements(army.Id,policyName,AiTacticalExecutor.Ready(o,army,oProfile).Where(u=>!existingRepairClients.Contains(u.Id)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0&&(Same(u,state)||AssemblyFollow(o,army,u,records))).Select(u=>u.Id));
            army=registry.CaptureArmy(army.Id).Single(a=>a.Id==army.Id);
            AiRepairUsePolicy.Prune(o,profile,completed);
            var recovering=AiRepairUsePolicy.Recovering(o,oProfile,profile,completed,policyName,records);
            #if UNITY_INCLUDE_TESTS
            int returnsBefore=completed.Keys.Count(k=>k.StartsWith("repair-return:",StringComparison.Ordinal));
#endif
            AiRepairUsePolicy.StartHealedReturns(o,oProfile,profile,completed,policyName,records);
#if UNITY_INCLUDE_TESTS
            if(completed.Keys.Count(k=>k.StartsWith("repair-return:",StringComparison.Ordinal))>returnsBefore)RepairStateForQa?.Invoke("healed-return-ready",o,army,state,completed,records);
#endif
            var repairClients=AiRepairUsePolicy.Clients(completed);
            foreach(var lost in repairClients.Where(id=>!army.Members.Contains(id)).ToArray())AiRepairUsePolicy.Returned(completed,lost);
            repairClients=AiRepairUsePolicy.Clients(completed);
            var repairView=army.Copy();repairView.Reinforcements=repairView.Reinforcements.Concat(repairClients).Distinct().OrderBy(id=>id).ToArray();
            foreach(var returned in AiTacticalExecutor.Ready(o,repairView,oProfile,Knowledge).Where(u=>repairClients.Contains(u.Id)&&(!Withdrawing||state.Phase==AiArmyPhase.Recovering)&&!recovering.Contains(u.Id)&&AiRepairUsePolicy.CanFinishReturn(completed,u)&&AiRepairUsePolicy.ReadyForOriginalLeader(o,oProfile,army,u,Knowledge)&&AssemblyFollow(o,army,u,records)))
            {
#if UNITY_INCLUDE_TESTS
                RepairStateForQa?.Invoke("physical-return-promoted-"+returned.Id,o,army,state,completed,records);
#endif
                AiRepairUsePolicy.Returned(completed,returned.Id);registry.JoinReinforcements(army.Id,policyName,new[]{returned.Id});
            }
            repairClients=AiRepairUsePolicy.Clients(completed);
            // A promoted return is part of the current core for this same
            // observation's viability and phase, rather than the old snapshot.
            army=registry.CaptureArmy(army.Id).Single();
            var units=o.Entities.Where(u=>army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&!repairClients.Contains(u.Id)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0&&u.Owner==o.Owner&&u.Health>0).ToArray();
            // A client already physically back with its original leader is available
            // during the core's retreat. Keep its saved repair binding until actual
            // home arrival/recovery; this neither renews its budget nor permits an
            // understrength original core to resume offense or absorb the client early.
            var returnedAvailability=Withdrawing&&units.Length>=army.MinimumViableForce&&
                !units.Any(u=>u.Held||u.Departing||u.OrderStamp?.Origin==PlayableOrderOrigin.Human)
                ?o.Entities.Where(u=>repairClients.Contains(u.Id)&&army.Reinforcements.Contains(u.Id)&&u.Owner==o.Owner&&u.Health>0&&!u.Held&&!u.Departing&&
                    PlayableUnitRules.HealthRatio(oProfile,u)>=profile.Value("positionDevelopment.repairReturnHealth")&&
                    AiRepairUsePolicy.CanFinishReturn(completed,u)&&AiRepairUsePolicy.ReadyForOriginalLeader(o,oProfile,army,u,Knowledge)&&AssemblyFollow(o,army,u,records)).ToArray()
                :Array.Empty<PlayableEntitySnapshot>();
            if(repairClients.Count>0&&pendingId==0&&!Viable(units.Concat(returnedAvailability),oProfile,profile,army.MinimumViableForce))
            {
                // Loss of offensive viability requires a real safe regroup, not
                // immediate destruction of the live original core/client binding.
                // The existing withdrawal/recovery path still proves movement,
                // arrival and strict viability before any offensive resumption.
                if(units.Length==0||units.Any(u=>u.Held||u.Departing||u.OrderStamp?.Origin==PlayableOrderOrigin.Human)||
                    !AiRepairUsePolicy.HasLiveRecoveryBudget(o,profile,completed))
                {Release(registry,o.Tick,"repair return: no viable core or recovery deadline");return;}
                if(state.Phase!=AiArmyPhase.Regrouping&&state.Phase!=AiArmyPhase.Retreating&&state.Phase!=AiArmyPhase.Recovering)
                {state.Reason="withdrawal";SetPhase(registry,AiArmyPhase.Regrouping,o.Tick);}
            }
            if(units.Length==0){Release(registry,o.Tick,"force lost");return;}
            if(state.TargetKind==PlayableRouteTargetKind.FriendlyAnchor&&!Withdrawing)
            {
                if(units.Any(u=>u.Held||u.CurrentOrder!=null&&u.OrderStamp?.Origin==PlayableOrderOrigin.Human))
                {Release(registry,o.Tick,"human took guard member");return;}
                if(!o.Buildings.Any(b=>b.Id==state.TargetId&&b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready))
                {Release(registry,o.Tick,"guarded asset lost");return;}
                double remaining=units.Average(u=>Distance(u.Position,state.Target));bool progressed=remaining<distance;
                distance=Math.Min(distance,remaining);if(progressed)state.ProgressTick=o.Tick;
                SetPhase(registry,units.Any(u=>u.Moving)?AiArmyPhase.Advancing:AiArmyPhase.Staging,o.Tick,progressed);
                var guards=QuietGuards(registry);
                if(applied&&pendingId==0&&repairClients.Count==0&&units.All(u=>Distance(u.Position,state.Target)<=PlayableUnitRules.Radius(oProfile,u.Kind))&&
                    Fighters(o).Any(u=>registry.ArmyFor(u.Id)==0)&&GuardAssets(o).Any(b=>!guards.Any(g=>g.Objective==Key(PlayableRouteTargetKind.FriendlyAnchor,b.Id))))
                    Release(registry,o.Tick,"guard in position; assign another asset",disband:false);
                return;
            }
            bool withdrawal=state.Phase==AiArmyPhase.Retreating||state.Phase==AiArmyPhase.Recovering||state.Phase==AiArmyPhase.Regrouping&&state.Reason=="withdrawal";
            if(withdrawal)
            {
                var members=o.Entities.Where(u=>army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&u.Owner==o.Owner&&u.Health>0).ToArray();
                if(members.Any(u=>u.CurrentOrder!=null&&!Same(u,state)&&!AssemblyFollow(o,army,u,records)&&!AiTacticalExecutor.EffectiveWithdrawal(o,army,u,records,oProfile,certified:false)&&
                    !AiRepairUsePolicy.OwnedPendingWithdrawal(o,u,policyName,records)))
                {
#if UNITY_INCLUDE_TESTS
                    if(repairClients.Count>0)RepairStateForQa?.Invoke("withdrawal-order-replaced",o,army,state,completed,records);
#endif
                    Release(registry,o.Tick,"withdrawal order replaced");return;
                }
                if(state.Phase==AiArmyPhase.Recovering&&AiTacticalExecutor.Withdraw(o,army,oProfile,profile))
                {SetPhase(registry,AiArmyPhase.Regrouping,o.Tick);return;}
                var moving=members.Where(u=>u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&AiTacticalExecutor.EffectiveWithdrawal(o,army,u,records,oProfile)).ToArray();
                double remaining=moving.Length==0||!army.Anchor.HasValue?distance:members.Average(u=>Distance(u.Position,army.Anchor.Value));
                bool progressed=moving.Length>0&&remaining<distance;distance=Math.Min(distance,remaining);
                if(progressed)state.ProgressTick=o.Tick;
                bool arrived=members.All(u=>u.NavigationOutcome==NavigationOutcome.Arrived&&AiTacticalExecutor.EffectiveWithdrawal(o,army,u,records,oProfile))&&AiTacticalExecutor.LocalThreats(o,army,oProfile).Length==0;
                if(arrived&&state.Phase==AiArmyPhase.Retreating)SetPhase(registry,AiArmyPhase.Recovering,o.Tick,true);
                if(state.Phase==AiArmyPhase.Recovering&&o.Tick-state.PhaseTick>=AiProfile.SecondsToTicks(profile.Value("decision.retrySeconds"),30)&&!AiTacticalExecutor.Withdraw(o,army,oProfile,profile)&&Viable(units,oProfile,profile,army.MinimumViableForce))
                {SetPhase(registry,AiArmyPhase.Staging,o.Tick);state.Reason="recovered";distance=units.Average(u=>Distance(u.Position,state.Target));applied=false;}
                else
                {
                    if(o.Tick-Math.Max(state.ProgressTick,state.PhaseTick)>=AiProfile.SecondsToTicks(profile.Value("armies.stallSeconds"),30))
                    {Release(registry,o.Tick,"withdrawal no meaningful progress deadline");return;}
                    registry.UpdateMission(state.ArmyId,policyName,state.Phase,o.Tick,Key(state.TargetKind,state.TargetId),anchor:army.Anchor,rally:state.Target,progressed:progressed);
                    return;
                }
            }
            if(AiTacticalExecutor.Withdraw(o,army,oProfile,profile))
            {state.Reason="withdrawal";SetPhase(registry,AiArmyPhase.Regrouping,o.Tick);return;}
            if(pendingId==0&&state.TargetKind!=PlayableRouteTargetKind.FriendlyAnchor&&o.Tick-state.LastInitiativeTick>=AiProfile.SecondsToTicks(profile.Value("strategy.commitmentSeconds"),30)&&
                !AiActiveStrategyPolicy.ViableApproach(o,oProfile,profile,units,state.TargetId,state.Target,state.TargetKind,Knowledge,completed))
            {Release(registry,o.Tick,"known compatible fortification requires another approach or support");return;}
            if(pendingId==0&&o.Participants.Count>2&&state.TargetKind==PlayableRouteTargetKind.VisibleEnemy&&
                o.Tick-state.LastInitiativeTick>=AiProfile.SecondsToTicks(profile.Value("strategy.commitmentSeconds"),30))
            {
                var assessments=AiOpponentSelector.Assess(o,oProfile,profile,state.TargetId,Knowledge,completed);
                var current=assessments.FirstOrDefault(a=>a.TargetId==state.TargetId);
                var best=assessments.FirstOrDefault();
                if(current!=null&&best!=null&&best.TargetId!=state.TargetId&&best.Score-current.Score>=profile.Value("strategy.switchMargin"))
                {Release(registry,o.Tick,"observed opponent advantage after commitment");return;}
            }
            if(state.TargetKind==PlayableRouteTargetKind.VisibleEnemy&&Health(o,state)<0)
            {
                state.TargetVisible=false;
                // The owner can confirm only absence at the remembered point, never
                // destruction elsewhere. Occlusion retains prior observed knowledge.
                if(o.Vision!=null&&o.Vision.IsVisible(state.Target))
                {Release(registry,o.Tick,"target not at remembered visible position; whereabouts unconfirmed");return;}
                SetPhase(registry,AiArmyPhase.Regrouping,o.Tick);
                if(o.Tick-state.ProgressTick>=AiProfile.SecondsToTicks(profile.Value("armies.stallSeconds"),30))
                    Release(registry,o.Tick,"unconfirmed contact recheck deadline");
                return;
            }
            if(state.TargetKind==PlayableRouteTargetKind.VisibleEnemy)
            {state.TargetVisible=true;state.LastSeenTick=o.Tick;if(state.Phase==AiArmyPhase.Regrouping)SetPhase(registry,AiArmyPhase.Staging,o.Tick);}
            if(state.TargetKind==PlayableRouteTargetKind.PublicObjective&&!(AiRelayObjectives.IsRelay(o,state.TargetId)?AiRelayObjectives.Needed(o,oProfile,state.TargetId):AiPublicEnemyStarts.CanAttack(o,Knowledge,state.TargetId)))
            {Release(registry,o.Tick,"public target empty or whereabouts unconfirmed");return;}
            state.Target=Point(o,state);
            double now=units.Average(u=>Distance(u.Position,state.Target)),health=Health(o,state);
            bool relayHolding=state.TargetKind==PlayableRouteTargetKind.PublicObjective&&AiRelayObjectives.IsRelay(o,state.TargetId)&&AiRelayObjectives.Holding(o,oProfile,state.TargetId,units);
            bool progress=relayHolding&&!o.Sites.Any(s=>s.Site.Id==state.TargetId&&s.Contested)||now<distance||health>=0&&targetHealth>=0&&health<targetHealth;
            distance=Math.Min(distance,now);targetHealth=health;
            if(progress)state.ProgressTick=o.Tick;
            // A receipt alone is not an effective order and does not advance progress.
            // A newly accepted offensive route retains the preceding effective order until installation.
            // Its exact live pending binding is not a foreign replacement, nor installed progress.
            if(applied&&units.Any(u=>u.CurrentOrder!=null&&!Same(u,state)&&!(state.TargetKind==PlayableRouteTargetKind.PublicObjective&&AiRelayObjectives.IsRelay(o,state.TargetId)&&AiRelayObjectives.CombatOrder(o,oProfile,state.TargetId,u))&&!SameLivePending(u,state)&&!AssemblyFollow(o,army,u,records)&&CombatMicroOrder?.Invoke(o,army,u)!=true&&
                !(u.CurrentOrder.Kind!=PlayableTacticalOrderKind.Follow&&AiRepairUsePolicy.OwnedCoreOrder(o,u,policyName,state.StartedTick,records,
                    allowPriorDefense:repairClients.Count>0&&AiRepairUsePolicy.HasLiveRecoveryBudget(o,profile,completed)))))
            {Release(registry,o.Tick,"order replaced");return;}
            if(applied&&(state.Phase==AiArmyPhase.Forming||state.Phase==AiArmyPhase.Staging||state.Phase==AiArmyPhase.Advancing||state.Phase==AiArmyPhase.Engaging)&&units.Any(u=>Same(u,state)))
            {
                bool engaging=state.TargetKind==PlayableRouteTargetKind.VisibleEnemy&&units.Any(u=>u.TargetId==state.TargetId);
                SetPhase(registry,engaging?AiArmyPhase.Engaging:AiArmyPhase.Advancing,o.Tick,progress);
            }
            else if(state.Phase==AiArmyPhase.Forming)SetPhase(registry,AiArmyPhase.Staging,o.Tick,progress);
            if(applied&&state.TargetKind==PlayableRouteTargetKind.PublicObjective&&!AiRelayObjectives.IsRelay(o,state.TargetId)&&units.All(u=>CompletedPublicOrder(o,u)))
            {Release(registry,o.Tick,"public objective reached");return;}
            long deadline=AiProfile.SecondsToTicks(profile.Value(state.Phase==AiArmyPhase.Forming||state.Phase==AiArmyPhase.Staging?"armies.assemblyDeadlineSeconds":"armies.stallSeconds"),30);
            if(o.Tick-state.ProgressTick>=deadline)
            {
                if(AiTacticalExecutor.Withdrawal(o,army,oProfile,profile,nextId,stalled:true,threatRatio:HarassAllowed?profile.Value("openingsMidgame.personality.harassingRetreatRatio"):(double?)null)!=null)
                {state.Reason="withdrawal";SetPhase(registry,AiArmyPhase.Regrouping,o.Tick);}
                else Release(registry,o.Tick,"no meaningful progress deadline");
            }
            else registry.UpdateMission(state.ArmyId,policyName,state.Phase,o.Tick,Key(state.TargetKind,state.TargetId),rally:state.Target,progressed:progress);
        }
        // Gameplay capture/arrival radii are read from the immutable gameplay profile,
        // never copied into tunable AI stats. Rebind retains valid mission identity.
        private PlayableProfile oProfile=PlayableProfile.Default;
        private bool CompletedPublicOrder(PlayableAiObservation observation,PlayableEntitySnapshot unit)
        {
            if(unit.Queue?.Pending!=null||unit.Queue?.Active!=null)return false;
            var completion=unit.Completion??unit.Queue?.Completion?.SpatialReceipt;var stamp=unit.OrderStamp;
            if(completion==null||stamp==null||completion.Order==null||completion.GroupId<=0||completion.Incarnation<=0)return false;
            var order=completion.Order;
            return stamp.UnitId==unit.Id&&stamp.Owner==observation.Owner&&stamp.Generation==observation.Generation&&stamp.Origin==PlayableOrderOrigin.Ai&&
                stamp.Kind==PlayableCommandKind.AttackMove&&stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&stamp.ActionId>0&&stamp.JobId==stamp.ActionId&&
                order.UnitId==stamp.UnitId&&order.Owner==stamp.Owner&&order.Generation==stamp.Generation&&order.Revision==stamp.Revision&&order.Sequence==stamp.Sequence&&order.ActionId==stamp.ActionId&&order.JobId==stamp.JobId&&order.Origin==stamp.Origin&&order.Kind==stamp.Kind&&order.Source==stamp.Source&&order.Tick==stamp.Tick&&
                stamp.Tick>=state.StartedTick&&completion.ActivationTick>=stamp.Tick&&completion.CompletionTick>=completion.ActivationTick&&completion.CompletionTick<=observation.Tick&&completion.OriginalTarget.Equals(state.Target);
        }
        internal void Rebind(PlayableProfile p){oProfile=p;}
        public bool SetPhase(AiArmyRegistry registry,AiArmyPhase phase,long tick,bool progressed=false)
        {
            if(!Active||tick<state.PhaseTick||!Enum.IsDefined(typeof(AiArmyPhase),phase)||phase==AiArmyPhase.Disbanded)return false;
            if(!registry.UpdateMission(state.ArmyId,policyName,phase,tick,Key(state.TargetKind,state.TargetId),anchor:phase==AiArmyPhase.Retreating||phase==AiArmyPhase.Recovering?registry.Capture().Armies.Single(a=>a.Id==state.ArmyId).Anchor:null,rally:state.Target,progressed:progressed))return false;
            if(state.Phase!=phase){state.Phase=phase;state.PhaseTick=tick;}if(progressed)state.ProgressTick=tick;return true;
        }
#if UNITY_INCLUDE_TESTS
        internal static Action<long,long,string,int[]> RepairReleaseForQa;
        internal static Action<string,PlayableAiObservation,AiArmyState,AiArmyMissionState,IReadOnlyDictionary<string,long>,IEnumerable<PlayableAiTraceRecord>> RepairStateForQa;
#endif
        private bool Withdrawing=>state.Phase==AiArmyPhase.Retreating||state.Phase==AiArmyPhase.Recovering||state.Phase==AiArmyPhase.Regrouping&&state.Reason=="withdrawal";
        private void Release(AiArmyRegistry registry,long tick,string reason,bool disband=true)
        {
#if UNITY_INCLUDE_TESTS
            if(Active&&(AiRepairUsePolicy.Clients(completed).Count>0||state.Reason=="recovered"))RepairReleaseForQa?.Invoke(state.ArmyId,tick,reason,AiRepairUsePolicy.Clients(completed).ToArray());
#endif
            if(Active){completed[Key(state.TargetKind,state.TargetId)]=tick;if(disband)registry.Disband(state.ArmyId);}
            state.ArmyId=0;state.Phase=AiArmyPhase.Disbanded;state.PhaseTick=tick;state.Reason=reason;applied=false;
        }
        public PlayableAiAction Propose(PlayableAiObservation o,PlayableAiOpeningCompositionState opening,PlayableProfile gameplay,AiProfile profile,AiArmyRegistry registry)
            =>ProposeWithRecords(o,opening,gameplay,profile,registry,null);
        internal PlayableAiAction ProposeWithRecords(PlayableAiObservation o,PlayableAiOpeningCompositionState opening,PlayableProfile gameplay,AiProfile profile,AiArmyRegistry registry,IEnumerable<PlayableAiTraceRecord> records)
        {
            Bind(o);AiProfileForReason=profile;if(opening==null||opening.OwnerId!=owner||opening.MatchSeed!=o.Seed)throw new ArgumentException("Opening binding");
            if(pendingId!=0)return null;
            AiArmyMissionState plan=state;
            int[] ids;
            if(Active)
            {
                var a=registry.Capture().Armies.FirstOrDefault(a=>a.Id==state.ArmyId&&a.Phase!=AiArmyPhase.Disbanded&&a.TacticalOwner==policyName);
                if(a==null)return null;
                var completeArmy=a;
                if(state.Reason==AiReinforcementPolicy.RegroupReason)return AiReinforcementPolicy.Gather(o,a,gameplay,nextId,records,Knowledge,state.PhaseTick);
                var repairClients=AiRepairUsePolicy.Clients(completed);
                if(repairClients.Count>0){a=a.Copy();a.Members=a.Members.Where(id=>!repairClients.Contains(id)||!AiRepairUsePolicy.HasLiveRecoveryBudget(o,profile,completed,id)).ToArray();a.Reinforcements=a.Reinforcements.Concat(repairClients).Where(id=>a.Members.Contains(id)).Distinct().OrderBy(id=>id).ToArray();}
#if UNITY_INCLUDE_TESTS
                if(completed.Keys.Any(k=>k.StartsWith("repair-return:",StringComparison.Ordinal))||state.Reason=="recovered")RepairStateForQa?.Invoke("return-decision",o,completeArmy,state,completed,records);
#endif
                // Rendezvous at a completed safe withdrawal leg, before continuing
                // home. Waiting for every home leg consumes the client's return
                // budget; following a still travelling leader races its route.
                // Full core home arrival remains the independent recovery gate.
                bool returnAnchorReady=!Withdrawing||o.Entities.Any(u=>u.Id==completeArmy.LeaderId&&!u.Moving&&
                    u.CurrentOrder==null&&u.NavigationOutcome==NavigationOutcome.Arrived&&u.Queue?.Pending==null&&u.OrderStamp?.Tick>=completeArmy.CreatedTick&&
                    AiTacticalExecutor.EffectiveWithdrawal(o,completeArmy,u,records,gameplay,certified:false)&&
                    AiTacticalExecutor.SafePathForUnit(o,u.Position,new[]{u.Position},PlayableUnitRules.Radius(gameplay,u.Kind),gameplay,u.Kind==PlayableEntityKind.Hawk)&&
                    AiStaticDefenseSafety.Withdrawal(o,gameplay,u,new[]{u.Position},Knowledge)&&
                    o.RouteProofs.Any(r=>r.UnitId==u.Id&&o.Buildings.Any(b=>b.Id==r.TargetId)&&AiTacticalExecutor.WithdrawalRoute(o,r,gameplay)));
                var returning=returnAnchorReady?AiRepairUsePolicy.ReturnAssembly(o,gameplay,profile,completeArmy,nextId,completed,policyName,records,Knowledge):null;
                if(returning!=null)return returning;
                if(state.TargetKind==PlayableRouteTargetKind.FriendlyAnchor&&!Withdrawing)
                {
                    var guardRepair=AiRepairUsePolicy.Propose(o,gameplay,profile,completeArmy,nextId,completed,policyName,records,
                        remaining=>Viable(remaining,gameplay,profile,completeArmy.MinimumViableForce),Knowledge);
                    if(guardRepair!=null)return guardRepair;
                    // A guard order must not steal a recovering or assembling client.
                    a=a.Copy();a.Members=a.Members.Where(id=>!repairClients.Contains(id)).ToArray();
                    var guardAssembly=AiTacticalExecutor.Propose(o,a,gameplay,nextId,records:records,knowledge:Knowledge,ai:profile);
                    if(guardAssembly!=null)return guardAssembly;
                    return Guard(o,gameplay,profile,registry,a);
                }
                long repeat=AiProfile.SecondsToTicks(profile.Value("decision.repeatOrderSeconds"),30);
                var assembly=AiTacticalExecutor.RecoveryAssembly(o,a,gameplay,nextId,repeat,records);
                if(assembly!=null)return assembly;
                var ready=new HashSet<int>(AiTacticalExecutor.Ready(o,a,gameplay,Knowledge).Where(u=>!repairClients.Contains(u.Id)&&(u.CurrentOrder==null||o.Tick-u.CurrentOrder.IssuedTick>=repeat)&&!SameLivePending(u,state)).Select(u=>u.Id));
                bool joining=o.Entities.Any(u=>ready.Contains(u.Id)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0&&o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==state.TargetKind&&r.TargetId==state.TargetId));
                // Withdrawal excludes reinforcements itself. Keep their identities
                // here so corridor clearing can see an applied return Follow wave.
                var fallback=AiTacticalExecutor.Withdrawal(o,completeArmy,gameplay,profile,nextId,stalled:state.Reason=="withdrawal",threatRatio:HarassAllowed?profile.Value("openingsMidgame.personality.harassingRetreatRatio"):(double?)null,records:records,knowledge:Knowledge,
                    prioritizeLeader:Withdrawing&&AiRepairUsePolicy.HasLiveRecoveryBudget(o,profile,completed));
                if(fallback!=null)return fallback;
                var repair=AiRepairUsePolicy.Propose(o,gameplay,profile,completeArmy,nextId,completed,policyName,records,remaining=>Viable(remaining,gameplay,profile),Knowledge);
                if(repair!=null)return repair;
                // A physically admitted client must graduate from assembly to the
                // live line mission. Continuous fresh output cannot leave joined
                // members parked in Follow forever behind the group cooldown.
                bool assembledLine=(state.Phase==AiArmyPhase.Advancing||state.Phase==AiArmyPhase.Engaging)&&o.Entities.Any(u=>a.Members.Contains(u.Id)&&!a.Reinforcements.Contains(u.Id)&&
                    u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&o.Tick-u.CurrentOrder.IssuedTick>=repeat&&
                    AssemblyFollow(o,a,u,records)&&o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==state.TargetKind&&r.TargetId==state.TargetId));
                bool SafetyCorrection(PlayableAiAction action)=>action.Kind==PlayableCommandKind.Stop||o.Entities.Any(u=>action.EntityIds.Contains(u.Id)&&u.CurrentOrder!=null&&
                    !o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&
                        (u.CurrentOrder.Kind==PlayableTacticalOrderKind.Follow&&r.TargetId==u.CurrentOrder.TargetId&&o.Entities.Any(c=>c.Id==r.TargetId&&r.Target.Equals(PlayableUnitRules.FollowGoal(gameplay,c.Position,c.HullHeading)))||
                         u.CurrentOrder.Kind==PlayableTacticalOrderKind.Move&&r.TargetId==u.Id&&r.Target.Equals(u.CurrentOrder.Destination))&&AiTacticalExecutor.SafeRoute(o,r,gameplay,Knowledge)));
                var correction=AiTacticalExecutor.ArrivalAssembly(o,a,gameplay,nextId,records,Knowledge,profile);
                if(correction!=null&&(!assembledLine||SafetyCorrection(correction)))return correction;
                var tactical=AiTacticalExecutor.Propose(o,a,gameplay,nextId,records:records,knowledge:Knowledge,ai:profile);
                // A ready peer may repeatedly lose its delayed line admission as
                // the core moves. That cannot suppress a fresh client's first leg.
                bool waitingClient=tactical!=null&&o.Entities.Any(u=>tactical.EntityIds.Contains(u.Id)&&u.CurrentOrder==null&&!ready.Contains(u.Id));
                if(tactical!=null&&(!assembledLine||SafetyCorrection(tactical))&&(!joining||waitingClient||tactical.Kind==PlayableCommandKind.Stop||
                    state.Phase==AiArmyPhase.Regrouping||state.Phase==AiArmyPhase.Retreating||state.Phase==AiArmyPhase.Recovering))return tactical;
                ids=o.Entities.Where(u=>a.Members.Contains(u.Id)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0&&(!a.Reinforcements.Contains(u.Id)||ready.Contains(u.Id)&&o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==state.TargetKind&&r.TargetId==state.TargetId))&&!Same(u,state)&&!SameLivePending(u,state)&&(u.CurrentOrder==null||o.Tick-u.CurrentOrder.IssuedTick>=repeat)).Select(u=>u.Id).OrderBy(x=>x).ToArray();
                if(state.Phase==AiArmyPhase.Regrouping||state.Phase==AiArmyPhase.Retreating||state.Phase==AiArmyPhase.Recovering)return null; // Withdrawal/recovery owns these phases.
            }
            else
            {
                // Safe has actual minimum economic commitments; after completion the name
                // of the opening imposes no permanent ban on pressure.
                if(opening.Phase==PlayableAiOpeningPhase.Active&&opening.Opening==PlayableAiOpening.Safe&&
                    ((!Ready(o,PlayableBuildingKind.Factory,1)&&!Ready(o,PlayableBuildingKind.Hangar,1))||!Ready(o,PlayableBuildingKind.Refinery,1)))return null;
                if(registry.Capture().Armies.Count(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded)>=registry.MajorCap)return null;
                if(EconomicDefense(o,opening))return Guard(o,gameplay,profile,registry,null);
                var free=Fighters(o).Where(u=>registry.ArmyFor(u.Id)==0).ToArray();
                if(TeamAssignment?.Helping==true)
                {
                    // Reserve real owner-local bodies; defense retains first arbitration priority.
                    int reserve=(int)Math.Ceiling(Fighters(o).Length*profile.Value("team.minimumHomeReserveShare"));
                    free=free.Take(Math.Max(0,free.Length-reserve)).ToArray();
                }
                int minimum=OpeningMinimum>0?OpeningMinimum:(int)profile.Value("armies.minimumUnits");
                if(SplitLargeArmies&&OpeningMembers==null&&free.Length/minimum>1)
                {int fronts=Math.Min(registry.MajorCap,free.Length/minimum);free=free.Take((free.Length+fronts-1)/fronts).ToArray();}
                if(free.Length<(OpeningMinimum>0?OpeningMinimum:profile.Value("armies.minimumUnits"))||!Viable(free,gameplay,profile))return Guard(o,gameplay,profile,registry,null);
                var candidates=OffensiveRoutes(o,free)
                    .Where(r=>!completed.TryGetValue(Key(r.Kind,r.TargetId),out var tick)||o.Tick-tick>=AiProfile.SecondsToTicks(profile.Value("decision.retrySeconds"),30))
                    .GroupBy(r=>Key(r.Kind,r.TargetId)).Select(g=>new{Route=g.OrderBy(r=>r.UnitId).First(),Units=free.Where(u=>g.Any(r=>r.UnitId==u.Id)).Select(u=>u.Id).ToArray()})
                    .Where(g=>g.Units.Length>=(OpeningMinimum>0?OpeningMinimum:profile.Value("armies.minimumUnits"))&&Viable(free.Where(u=>g.Units.Contains(u.Id)),gameplay,profile))
                    .OrderBy(g=>g.Route.Kind).ThenBy(g=>g.Route.TargetId).ToArray();
                if(OtherTargetId>0&&candidates.Any(g=>g.Route.TargetId!=OtherTargetId))candidates=candidates.Where(g=>g.Route.TargetId!=OtherTargetId).ToArray();
                var assessments=AiOpponentSelector.Assess(o,gameplay,profile,knowledge:Knowledge,memory:completed);
                candidates=candidates.Where(g=>AiActiveStrategyPolicy.ViableApproach(o,gameplay,profile,
                    free.Where(u=>g.Units.Contains(u.Id)).ToArray(),g.Route.TargetId,g.Route.Target,g.Route.Kind,Knowledge,completed)).ToArray();
                var choice=candidates.Where(g=>!AiRelayObjectives.IsRelay(o,g.Route.TargetId)||g.Route.Kind!=PlayableRouteTargetKind.PublicObjective||AiRelayObjectives.Viable(o,gameplay,profile,Knowledge,free.Where(u=>g.Units.Contains(u.Id)),g.Route.TargetId,g.Route.Target))
                    .OrderByDescending(g=>g.Route.Kind==PlayableRouteTargetKind.PublicObjective&&AiRelayObjectives.IsRelay(o,g.Route.TargetId))
                    .ThenByDescending(g=>TeamAssignment!=null&&TeamAssignment.TargetId==g.Route.TargetId&&g.Route.Kind==PlayableRouteTargetKind.VisibleEnemy)
                    .ThenByDescending(g=>HarassAllowed&&o.Buildings.Any(b=>b.Id==g.Route.TargetId&&o.IsHostile(b.Owner)&&TerritoryRules.Income(gameplay,b.Kind)>0))
                    .ThenByDescending(g=>assessments.FirstOrDefault(a=>a.TargetId==g.Route.TargetId&&g.Route.Kind==a.Kind)?.Score??double.MinValue)
                    .ThenBy(g=>g.Route.Kind).ThenBy(g=>g.Route.TargetId).FirstOrDefault();if(choice==null)return Guard(o,gameplay,profile,registry,null);
                if(TeamAssignment!=null&&TeamAssignment.TargetId==choice.Route.TargetId&&TeamAssignment.WaitUntilTick>o.Tick)return null;
                ids=choice.Units;plan=new AiArmyMissionState{TargetKind=choice.Route.Kind,TargetId=choice.Route.TargetId,Target=choice.Route.Target};
            }
            var recipients=o.Entities.Where(u=>ids.Contains(u.Id)).ToArray();
            if(Active&&plan.TargetKind==PlayableRouteTargetKind.PublicObjective&&AiRelayObjectives.IsRelay(o,plan.TargetId))
            {
                var contest=AiRelayObjectives.Combat(o,gameplay,recipients,plan.TargetId,nextId);
                if(contest!=null)return contest;
                if(AiRelayObjectives.Holding(o,gameplay,plan.TargetId,recipients))return null;
            }
            if(ids.Length==0||recipients.Length!=ids.Length||recipients.All(u=>Same(u,plan)||SameLivePending(u,plan)))return null;
            if(!o.RouteProofs.Any(r=>ids.Contains(r.UnitId)&&r.Kind==plan.TargetKind&&r.TargetId==plan.TargetId))return null;
            return new PlayableAiAction(nextId,owner,o.ProfileId,o.ProfileRevision,generation,o.SnapshotSequence,
                plan.TargetKind==PlayableRouteTargetKind.VisibleEnemy?PlayableCommandKind.Attack:PlayableCommandKind.AttackMove,ids,plan.Target,targetId:plan.TargetKind==PlayableRouteTargetKind.VisibleEnemy?plan.TargetId:0,
                siteId:plan.TargetKind==PlayableRouteTargetKind.PublicObjective?plan.TargetId:0,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
        private IEnumerable<PlayableRouteProof> OffensiveRoutes(PlayableAiObservation o,PlayableEntitySnapshot[] units)
        {
            // Resolve owner knowledge once per public site, not once per unit route.
            var starts=new HashSet<int>(o.PublicScoutObjectives.Where(g=>g.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart&&AiPublicEnemyStarts.CanAttack(o,Knowledge,g.SiteId)).Select(g=>g.SiteId));
            return o.RouteProofs.Where(r=>units.Any(u=>u.Id==r.UnitId)&&(r.Kind==PlayableRouteTargetKind.VisibleEnemy||r.Kind==PlayableRouteTargetKind.PublicObjective&&(starts.Contains(r.TargetId)||AiRelayObjectives.IsRelay(o,r.TargetId)&&AiRelayObjectives.Needed(o,oProfile,r.TargetId))));
        }
        internal PlayableAiAction ProposePositioning(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiArmyRegistry registry)
        {
            Bind(o);if(registry==null||registry.OwnerId!=owner||registry.Generation!=generation)throw new ArgumentException("Positioning registry binding");
            return pendingId==0?Guard(o,p,ai,registry,null):null;
        }
        private PlayableAiAction Guard(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiArmyRegistry registry,AiArmyState army)
        {
            if(army==null&&registry.Capture().Armies.Count(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded)>=registry.MajorCap)return null;
            var units=Fighters(o).Where(u=>(army==null?registry.ArmyFor(u.Id)==0:army.Members.Contains(u.Id))&&
                !u.Held&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human).ToArray();
            if(units.Length==0)return null;
            var enemies=o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Health>0).Select(e=>e.Position)
                .Concat(o.Buildings.Where(b=>o.IsHostile(b.Owner)&&b.Health>0).Select(b=>b.Position)).ToArray();
            var guards=QuietGuards(registry);
            var choices=GuardAssets(o).Where(b=>!guards.Any(a=>a.Id!=army?.Id&&a.Objective==Key(PlayableRouteTargetKind.FriendlyAnchor,b.Id)))
                .Select(b=>new{Building=b,Routes=units.Select(u=>o.RouteProofs.FirstOrDefault(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.TargetId==b.Id&&AiTacticalExecutor.SafeRoute(o,r,p))).ToArray()})
                .Where(c=>c.Routes.Any(r=>r!=null)).OrderBy(c=>enemies.Length>0?enemies.Min(e=>Distance(e,c.Building.Position)):
                    -Distance(c.Building.Position,o.Buildings.FirstOrDefault(b=>b.SiteId==o.HomeSiteId&&b.Kind==PlayableBuildingKind.Headquarters)?.Position??c.Building.Position))
                .ThenBy(c=>c.Building.Id).ToArray();
            var choice=choices.FirstOrDefault();if(choice==null)return null;
            // Existing geometry-certified approach is a legal defensive position;
            // no guessed offset, teleport or direct Nav query is introduced here.
            var route=choice.Routes.First(r=>r!=null);var point=route.Goal;
            long repeat=AiProfile.SecondsToTicks(ai.Value("decision.repeatOrderSeconds"),30);
            var ids=units.Where(u=>choice.Routes.Any(r=>r!=null&&r.UnitId==u.Id)&&
                Distance(u.Position,point)>PlayableUnitRules.Radius(p,u.Kind)&&
                !(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&u.CurrentOrder.Destination.Equals(point))&&
                !AiTacticalExecutor.HasLivePendingMove(u,point)&&
                (u.CurrentOrder==null||o.Tick-u.CurrentOrder.IssuedTick>=repeat)).OrderBy(u=>Distance(u.Position,point)).ThenBy(u=>u.Id).Select(u=>u.Id).ToArray();
            if(army==null)
            {
                int slots=registry.MajorCap-registry.Capture().Armies.Count(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded);
                int goals=Math.Max(1,Math.Min(slots,choices.Length));
                ids=ids.Take((units.Length+goals-1)/goals).ToArray();
            }
            if(ids.Length==0)return null;
            return new PlayableAiAction(nextId,owner,o.ProfileId,o.ProfileRevision,generation,o.SnapshotSequence,PlayableCommandKind.Move,ids,point,parentId:choice.Building.Id,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
        private static bool Ready(PlayableAiObservation o,PlayableBuildingKind kind,int count)=>o.Buildings.Count(b=>b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.Kind==kind)>=count;
        // A current Applied local order can temporarily differ from strategic
        // intent without transferring membership or suppressing human overrides.
        internal Func<PlayableAiObservation,AiArmyState,PlayableEntitySnapshot,bool> CombatMicroOrder;
        public void Commit(PlayableAiObservation o,PlayableAiAction action,AiArmyRegistry registry)
        {
            Bind(o);if(action==null||pendingId!=0||action.ActionId!=nextId||action.PlayerId!=owner||action.Generation!=generation||action.SnapshotSequence!=o.SnapshotSequence)throw new InvalidOperationException("Selected mission binding");
#if UNITY_INCLUDE_TESTS
            if(Active&&(completed.Keys.Any(k=>k.StartsWith("repair-return:",StringComparison.Ordinal))||state.Reason=="recovered"))
                RepairStateForQa?.Invoke("selected-"+action.Kind+"-"+string.Join(",",action.EntityIds)+"-target="+action.TargetId+"-point="+action.Target.X+","+action.Target.Z,o,registry.CaptureArmy(state.ArmyId).Single(),state,completed,null);
#endif
            if(!Active)
            {
                if(!registry.TryCreate(o,action.Kind==PlayableCommandKind.Move?AiArmyRole.MobileDefense:TeamAssignment?.Helping==true?AiArmyRole.AlliedSupport:HarassAllowed&&o.Buildings.Any(b=>b.Id==action.TargetId&&o.IsHostile(b.Owner)&&TerritoryRules.Income(oProfile,b.Kind)>0)?AiArmyRole.Raid:AiArmyRole.Attack,policyName,action.EntityIds,out var id,requiredMinimum:OpeningMinimum>0?OpeningMinimum:(int?)null))throw new InvalidOperationException("Selected mission lost atomic membership admission");
                state=new AiArmyMissionState{ArmyId=id,TargetKind=action.Kind==PlayableCommandKind.Move?PlayableRouteTargetKind.FriendlyAnchor:action.TargetId>0?PlayableRouteTargetKind.VisibleEnemy:PlayableRouteTargetKind.PublicObjective,TargetId=action.Kind==PlayableCommandKind.Move?action.ParentId:action.TargetId>0?action.TargetId:action.SiteId,Target=action.Target,StartedTick=o.Tick,ProgressTick=o.Tick,PhaseTick=o.Tick,LastInitiativeTick=o.Tick,LastSeenTick=o.Tick,TargetVisible=action.TargetId>0,Phase=AiArmyPhase.Forming,Reason=action.Kind==PlayableCommandKind.Move?"owned asset defensive position":"selected useful pressure"};
                var assessment=AiOpponentSelector.Assess(o,oProfile,AiProfileForReason??Spacewars.Simulation.Ai.AiProfile.Initial,knowledge:Knowledge,memory:completed).FirstOrDefault(a=>a.TargetId==state.TargetId&&state.TargetKind==PlayableRouteTargetKind.VisibleEnemy);
                if(assessment!=null)state.Reason=string.Format(System.Globalization.CultureInfo.InvariantCulture,"observed team={0} tick={1} T={2:F4} E={3:F4} R={4:F4} O={5:F4} F={6:F4} score={7:F4}; {8}",assessment.Team,assessment.SourceTick,assessment.Threat,assessment.EconomicValue,assessment.Reachability,assessment.Opportunity,assessment.NewFrontCost,assessment.Score,TeamAssignment?.Reason??"independent pressure");
                distance=o.Entities.Where(u=>action.EntityIds.Contains(u.Id)).Average(u=>Distance(u.Position,state.Target));targetHealth=Health(o,state);applied=false;
                registry.UpdateMission(id,policyName,state.Phase,o.Tick,Key(state.TargetKind,state.TargetId),rally:state.Target);
            }
            else if(AiRepairUsePolicy.IsAction(o,action))
            {
                if(!registry.BeginRepairReturn(state.ArmyId,policyName,action.EntityIds.Single()))throw new InvalidOperationException("Repair return membership lost.");
                AiRepairUsePolicy.Commit(completed,action,o.Tick);
            }
            else if(state.Reason!=AiReinforcementPolicy.RegroupReason&&state.TargetKind==PlayableRouteTargetKind.FriendlyAnchor&&action.Kind==PlayableCommandKind.Move&&action.ParentId>0)
            {
                state.TargetId=action.ParentId;state.Target=action.Target;state.Reason="owned asset defensive position";
                registry.UpdateMission(state.ArmyId,policyName,state.Phase,o.Tick,Key(state.TargetKind,state.TargetId),rally:state.Target);
            }
            else if(action.Kind==PlayableCommandKind.Move&&state.Reason=="withdrawal"&&(state.Phase==AiArmyPhase.Regrouping||state.Phase==AiArmyPhase.Retreating)&&
                action.EntityIds.All(id=>!registry.Capture().Armies.Single(a=>a.Id==state.ArmyId).Reinforcements.Contains(id)&&o.RouteProofs.Any(r=>r.UnitId==id&&AiTacticalExecutor.WithdrawalLeg(o,r,oProfile,action.Target))))
            {
                state.Reason="withdrawal";bool starting=state.Phase!=AiArmyPhase.Retreating;
                SetPhase(registry,AiArmyPhase.Retreating,o.Tick);
                if(starting)
                {
                    var a=registry.Capture().Armies.Single(x=>x.Id==state.ArmyId);
                    distance=o.Entities.Where(u=>a.Members.Contains(u.Id)&&!a.Reinforcements.Contains(u.Id)).Average(u=>Distance(u.Position,action.Target));
                    registry.UpdateMission(state.ArmyId,policyName,state.Phase,o.Tick,Key(state.TargetKind,state.TargetId),anchor:action.Target,rally:state.Target);
                }
            }
            pendingId=nextId++;pendingGeneration=generation;
        }
        public void ObserveReceipt(PlayableAiTraceRecord r)
        {
            if(r==null||r.ActionId!=pendingId||r.OwnerId!=owner||r.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity||!AiStateWire.CallbackGenerationMatches(r,pendingGeneration)||r.Status==PlayableAiDeliveryStatus.Scheduled||r.Status==PlayableAiDeliveryStatus.Accepted)return;
            AiRepairUsePolicy.Receipt(completed,r);
            if(r.Status==PlayableAiDeliveryStatus.Applied&&Active)
            {applied=true;if(state.TargetKind!=PlayableRouteTargetKind.FriendlyAnchor)completed.Remove(AiActiveStrategyPolicy.DevelopmentWindow);}
            pendingId=pendingGeneration=0;
        }
        public AiArmyDeployment Deployment(PlayableAiObservation o,AiArmyRegistry registry)
        {
            Bind(o);
            if(registry==null||registry.OwnerId!=owner||registry.Generation!=generation)return null;
            if(!Active||state.TargetKind==PlayableRouteTargetKind.FriendlyAnchor||!applied||pendingId!=0||state.Phase!=AiArmyPhase.Advancing&&state.Phase!=AiArmyPhase.Engaging||
                state.TargetKind==PlayableRouteTargetKind.VisibleEnemy&&!state.TargetVisible)return null;
            var army=registry.Capture().Armies.FirstOrDefault(a=>a.Id==state.ArmyId&&a.Phase==state.Phase&&a.TacticalOwner==policyName&&a.Major&&
                a.OwnerId==owner&&a.Objective==Key(state.TargetKind,state.TargetId)&&a.Rally.HasValue&&a.Rally.Value.Equals(state.Target));
            if(army==null||!o.Entities.Any(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&Same(u,state)))return null;
            return new AiArmyDeployment(o,state.ArmyId,state.StartedTick);
        }
        internal void ReconcileRegistry(AiArmyRegistry registry,long tick,string reason="registry released mission at profile barrier")
        {
            if(Active&&!registry.Capture().Armies.Any(a=>a.Id==state.ArmyId&&a.Phase!=AiArmyPhase.Disbanded&&a.TacticalOwner==policyName))
                Release(registry,tick,reason,disband:false);
        }
        public void Stop(){state.ArmyId=0;state.Phase=AiArmyPhase.Disbanded;state.Reason="runtime stopped";pendingId=pendingGeneration=0;applied=false;}
        // These requests contain only detached owner entities and public targets. The
        // existing observer/router verifies each proof; commands use the native route service.
        internal static PlayableRouteRequest[] RouteRequests(IEnumerable<PlayableEntitySnapshot> entities,IEnumerable<PlayableBuildingSnapshot> buildings,IEnumerable<PlayablePublicScoutObjective> objectives,PlayableOwner owner,long generation,long tick,PlayableProfile profile,Func<PlayableOwner,bool> hostile)
        {
            var es=entities.ToArray();var targets=es.Where(e=>hostile(e.Owner)&&e.Health>0).Select(e=>new{Id=e.Id,Point=e.Position,Kind=PlayableRouteTargetKind.VisibleEnemy})
                .Concat(buildings.Where(b=>hostile(b.Owner)&&b.Health>0).Select(b=>new{Id=b.Id,Point=b.Position,Kind=PlayableRouteTargetKind.VisibleEnemy}))
                .Concat(buildings.Where(b=>b.Owner==owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost||b.Kind==PlayableBuildingKind.Mine||b.Kind==PlayableBuildingKind.RepairTower)).Select(b=>new{Id=b.Id,Point=b.Position,Kind=PlayableRouteTargetKind.FriendlyAnchor}))
                .Concat(objectives.Where(g=>(g.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart||g.Role==PlayablePublicScoutObjectiveRole.Relay)&&g.Reachable).Select(g=>new{Id=g.SiteId,Point=g.Approach,Kind=PlayableRouteTargetKind.PublicObjective})).OrderBy(t=>t.Kind).ThenBy(t=>t.Id).ToArray();
            return es.Where(e=>e.Owner==owner&&e.Health>0&&AiRosterCatalog.Initial.LineWeight(e.Kind)+AiRosterCatalog.Initial.SupportWeight(e.Kind)>0).OrderBy(e=>e.Id)
                .SelectMany(e=>targets.Select(t=>new PlayableRouteRequest(e.Id,t.Kind,t.Id,generation,tick,e.Position,PlayableUnitRules.Radius(profile,e.Kind),t.Point))).ToArray();
        }
        internal void WriteState(BinaryWriter w)
        {
            w.Write(state.ArmyId);w.Write(state.StartedTick);w.Write(state.PhaseTick);w.Write(state.ProgressTick);w.Write(state.LastInitiativeTick);w.Write(state.LastSeenTick);w.Write(state.TargetVisible);w.Write(state.TargetId);w.Write((int)state.TargetKind);WorldWire.Number(w,state.Target.X);WorldWire.Number(w,state.Target.Z);w.Write((int)state.Phase);WorldWire.String(w,state.Reason);
            w.Write(nextId);w.Write(pendingId);w.Write(pendingGeneration);w.Write(distance);w.Write(targetHealth);w.Write(applied);
            WorldWire.Array(w,completed.OrderBy(x=>x.Key,StringComparer.Ordinal).ToArray(),x=>{WorldWire.String(w,x.Key);w.Write(x.Value);});
        }
        internal void ReadState(BinaryReader r,long tick,AiArmyRegistry registry)
        {
            state=new AiArmyMissionState{ArmyId=r.ReadInt64(),StartedTick=r.ReadInt64(),PhaseTick=r.ReadInt64(),ProgressTick=r.ReadInt64(),LastInitiativeTick=r.ReadInt64(),LastSeenTick=r.ReadInt64(),TargetVisible=WorldWire.Boolean(r),TargetId=r.ReadInt32(),TargetKind=WorldWire.EnumValue<PlayableRouteTargetKind>(r),Target=new NavPoint(WorldWire.Number(r),WorldWire.Number(r)),Phase=WorldWire.EnumValue<AiArmyPhase>(r),Reason=WorldWire.String(r)};
            nextId=r.ReadInt64();pendingId=r.ReadInt64();pendingGeneration=r.ReadInt64();distance=WorldWire.Number(r);targetHealth=WorldWire.Number(r);applied=WorldWire.Boolean(r);
            completed=WorldWire.Array(r,()=>new KeyValuePair<string,long>(WorldWire.String(r),r.ReadInt64())).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
            AiStateWire.Require((state.TargetKind==PlayableRouteTargetKind.VisibleEnemy||state.TargetKind==PlayableRouteTargetKind.PublicObjective||state.TargetKind==PlayableRouteTargetKind.FriendlyAnchor)&&state.ArmyId>=0&&state.StartedTick>=0&&state.StartedTick<=tick&&state.ProgressTick>=state.StartedTick&&state.ProgressTick<=tick&&state.PhaseTick>=state.StartedTick&&state.PhaseTick<=tick&&state.LastInitiativeTick>=0&&state.LastInitiativeTick<=tick&&state.LastSeenTick>=0&&state.LastSeenTick<=tick&&state.Reason!=null&&
                (Active?state.TargetId>0&&state.Phase!=AiArmyPhase.Disbanded&&registry.Capture().Armies.Any(a=>a.Id==state.ArmyId&&a.Phase==state.Phase&&a.ProgressTick==state.ProgressTick&&a.TacticalOwner==policyName&&a.Objective==Key(state.TargetKind,state.TargetId)&&a.Rally.HasValue&&a.Rally.Value.Equals(state.Target)):state.Phase==AiArmyPhase.Disbanded)&&nextId>pendingId&&pendingId>=0&&(pendingId==0?pendingGeneration==0:pendingGeneration==generation&&Active)&&distance>=0&&targetHealth>=-1&&completed.All(x=>x.Key!=null&&x.Value>=0&&x.Value<=tick&&(!x.Key.StartsWith("repair-use:",StringComparison.Ordinal)||AiRepairUsePolicy.Parse(x.Key,out _,out _,out var repairAction)&&repairAction<nextId)),"army mission state/clocks/membership/callback");
        }
    }
}
