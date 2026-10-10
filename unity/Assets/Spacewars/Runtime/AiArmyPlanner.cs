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
        private readonly string owner;
        private readonly long generation;
        private AiArmyMissionState state=new AiArmyMissionState{Phase=AiArmyPhase.Disbanded,Reason="no mission"};
        private long nextId=1,pendingId,pendingGeneration;
        private double distance=double.MaxValue,targetHealth=-1;
        private bool applied;
        private Dictionary<string,long> completed=new Dictionary<string,long>(StringComparer.Ordinal);
        public AiArmyPlanner(string ownerId,long generation)
        {if(string.IsNullOrWhiteSpace(ownerId)||generation<=0)throw new ArgumentException("Mission binding");owner=ownerId;this.generation=generation;}
        public AiArmyMissionState Capture()=>state.Copy();
        public bool Active=>state.ArmyId!=0;
        internal long PendingId=>pendingId;
        internal long PendingGeneration=>pendingGeneration;
        private void Bind(PlayableAiObservation o)
        {if(o==null||o.OwnerId!=owner||o.Generation!=generation)throw new ArgumentException("Mission observation binding");}
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        private static string Key(PlayableRouteTargetKind kind,int id)=>((int)kind)+":"+id;
        private static PlayableEntitySnapshot[] Fighters(PlayableAiObservation o)=>o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&AiRosterCatalog.Initial.For(u.Kind).lineWeight>0).OrderBy(u=>u.Id).ToArray();
        private static bool Viable(IEnumerable<PlayableEntitySnapshot> units,PlayableProfile p,AiProfile ai)
        {
            // Initial line catalog is Tank. Use gameplay DPS on the common horizon and
            // current health; three critically damaged bodies are not three viable units.
            double horizon=ai.Value("utility.forceHorizonSeconds");
            double reference=p.TankWeaponDamage*1000d/p.TankWeaponReloadMilliseconds*horizon;
            return units.Sum(u=>p.TankWeaponDamage*1000d/p.TankWeaponReloadMilliseconds*horizon*Math.Min(1,u.Health/(double)PlayableUnitRules.Health(p,u.Kind)))>=reference*ai.Value("armies.minimumUnits");
        }
        private static bool Same(PlayableEntitySnapshot u,AiArmyMissionState s)=>u.CurrentOrder!=null&&
            (s.TargetKind==PlayableRouteTargetKind.VisibleEnemy?u.CurrentOrder.Kind==PlayableTacticalOrderKind.Attack&&u.CurrentOrder.TargetId==s.TargetId:
            u.CurrentOrder.Kind==PlayableTacticalOrderKind.AttackMove&&u.CurrentOrder.Destination.Equals(s.Target));
        private static double Health(PlayableAiObservation o,AiArmyMissionState s)=>
            o.Entities.Where(u=>u.Id==s.TargetId&&o.IsHostile(u.Owner)&&u.Health>0).Select(u=>(double)u.Health)
            .Concat(o.Buildings.Where(b=>b.Id==s.TargetId&&o.IsHostile(b.Owner)&&b.Health>0).Select(b=>(double)b.Health)).DefaultIfEmpty(-1).First();
        private static NavPoint Point(PlayableAiObservation o,AiArmyMissionState s)=>o.Entities.Where(u=>u.Id==s.TargetId&&o.IsHostile(u.Owner)&&u.Health>0).Select(u=>u.Position)
            .Concat(o.Buildings.Where(b=>b.Id==s.TargetId&&o.IsHostile(b.Owner)&&b.Health>0).Select(b=>b.Position)).DefaultIfEmpty(s.Target).First();
        // Effective assembly is proven by authority provenance, not by a receipt alone.
        internal static bool AssemblyFollow(PlayableAiObservation o,AiArmyState army,PlayableEntitySnapshot unit,IEnumerable<PlayableAiTraceRecord> records)
        {
            var order=unit.CurrentOrder;var stamp=unit.OrderStamp;
            if(order?.Kind!=PlayableTacticalOrderKind.Follow||stamp==null||unit.Owner!=o.Owner||unit.Health<=0||
                stamp.UnitId!=unit.Id||stamp.Owner!=o.Owner||stamp.Generation!=o.Generation||stamp.Origin!=PlayableOrderOrigin.Ai||
                stamp.Kind!=PlayableCommandKind.Follow||stamp.Source!=PlayableAiOpeningComposition.SourceIdentity||stamp.ActionId<=0||stamp.JobId!=stamp.ActionId||
                order.Generation!=o.Generation||order.Owner!=o.Owner||order.UnitId!=unit.Id||order.CommandSequence!=stamp.Sequence||order.IssuedTick!=stamp.Tick)return false;
            if(!o.Entities.Any(c=>c.Id==order.TargetId&&c.Id!=unit.Id&&c.Owner==o.Owner&&c.Health>0&&army.Members.Contains(c.Id)&&!army.Reinforcements.Contains(c.Id)&&AiRosterCatalog.Initial.For(c.Kind).lineWeight>0))return false;
            return (records??Array.Empty<PlayableAiTraceRecord>()).Any(r=>r.Policy==Policy&&r.Kind==PlayableCommandKind.Follow&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&
                r.OwnerId==o.OwnerId&&r.SourceIdentity==stamp.Source&&r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence&&r.ApplicationTick==stamp.Tick&&r.ApplicationTick<=o.Tick&&
                r.ReceiptIdentity?.Generation==o.Generation&&r.ReceiptIdentity.OwnerId==o.OwnerId);
        }
        public void Observe(PlayableAiObservation o,AiArmyRegistry registry,AiProfile profile,IEnumerable<PlayableAiTraceRecord> records=null)
        {
            Bind(o);if(!Active)return;
            var army=registry.Capture().Armies.FirstOrDefault(a=>a.Id==state.ArmyId&&a.Phase!=AiArmyPhase.Disbanded);
            if(army==null){Release(registry,o.Tick,"force lost");return;}
            if(army.TacticalOwner!=Policy){Release(registry,o.Tick,"tactical owner changed",disband:false);return;}
            registry.JoinReinforcements(army.Id,Policy,AiTacticalExecutor.Ready(o,army,oProfile).Where(u=>AiRosterCatalog.Initial.For(u.Kind).lineWeight>0&&(Same(u,state)||AssemblyFollow(o,army,u,records))).Select(u=>u.Id));
            army=registry.Capture().Armies.Single(a=>a.Id==army.Id);
            var units=o.Entities.Where(u=>army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&AiRosterCatalog.Initial.For(u.Kind).lineWeight>0&&u.Owner==o.Owner&&u.Health>0).ToArray();
            if(units.Length==0){Release(registry,o.Tick,"force lost");return;}
            bool withdrawal=state.Phase==AiArmyPhase.Retreating||state.Phase==AiArmyPhase.Recovering||state.Phase==AiArmyPhase.Regrouping&&state.Reason=="withdrawal";
            if(withdrawal)
            {
                var members=o.Entities.Where(u=>army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&u.Owner==o.Owner&&u.Health>0).ToArray();
                if(members.Any(u=>u.CurrentOrder!=null&&!Same(u,state)&&!AssemblyFollow(o,army,u,records)&&!AiTacticalExecutor.EffectiveWithdrawal(o,army,u,records,oProfile,certified:false)))
                {Release(registry,o.Tick,"withdrawal order replaced");return;}
                if(state.Phase==AiArmyPhase.Recovering&&AiTacticalExecutor.Withdraw(o,army,oProfile,profile))
                {SetPhase(registry,AiArmyPhase.Regrouping,o.Tick);return;}
                var moving=members.Where(u=>u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&AiTacticalExecutor.EffectiveWithdrawal(o,army,u,records,oProfile)).ToArray();
                double remaining=moving.Length==0||!army.Anchor.HasValue?distance:members.Average(u=>Distance(u.Position,army.Anchor.Value));
                bool progressed=moving.Length>0&&remaining<distance;distance=Math.Min(distance,remaining);
                if(progressed)state.ProgressTick=o.Tick;
                bool arrived=members.All(u=>u.NavigationOutcome==NavigationOutcome.Arrived&&AiTacticalExecutor.EffectiveWithdrawal(o,army,u,records,oProfile))&&AiTacticalExecutor.LocalThreats(o,army,oProfile).Length==0;
                if(arrived&&state.Phase==AiArmyPhase.Retreating)SetPhase(registry,AiArmyPhase.Recovering,o.Tick,true);
                if(state.Phase==AiArmyPhase.Recovering&&o.Tick-state.PhaseTick>=AiProfile.SecondsToTicks(profile.Value("decision.retrySeconds"),30)&&!AiTacticalExecutor.Withdraw(o,army,oProfile,profile)&&Viable(units,oProfile,profile))
                {SetPhase(registry,AiArmyPhase.Staging,o.Tick);state.Reason="recovered";distance=units.Average(u=>Distance(u.Position,state.Target));applied=false;}
                else
                {
                    if(o.Tick-Math.Max(state.ProgressTick,state.PhaseTick)>=AiProfile.SecondsToTicks(profile.Value("armies.stallSeconds"),30))
                    {Release(registry,o.Tick,"withdrawal no meaningful progress deadline");return;}
                    registry.UpdateMission(state.ArmyId,Policy,state.Phase,o.Tick,Key(state.TargetKind,state.TargetId),anchor:army.Anchor,rally:state.Target,progressed:progressed);
                    return;
                }
            }
            if(AiTacticalExecutor.Withdraw(o,army,oProfile,profile))
            {state.Reason="withdrawal";SetPhase(registry,AiArmyPhase.Regrouping,o.Tick);return;}
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
            if(state.TargetKind==PlayableRouteTargetKind.PublicObjective&&!o.PublicScoutObjectives.Any(g=>g.SiteId==state.TargetId&&g.Reachable))
            {Release(registry,o.Tick,"public target no longer permitted");return;}
            state.Target=Point(o,state);
            double now=units.Average(u=>Distance(u.Position,state.Target)),health=Health(o,state);
            bool progress=now<distance||health>=0&&targetHealth>=0&&health<targetHealth;
            distance=Math.Min(distance,now);targetHealth=health;
            if(progress)state.ProgressTick=o.Tick;
            // A receipt alone is not an effective order and does not advance progress.
            if(applied&&units.Any(u=>u.CurrentOrder!=null&&!Same(u,state)&&!AssemblyFollow(o,army,u,records)))
            {Release(registry,o.Tick,"order replaced");return;}
            if(applied&&(state.Phase==AiArmyPhase.Forming||state.Phase==AiArmyPhase.Staging||state.Phase==AiArmyPhase.Advancing||state.Phase==AiArmyPhase.Engaging)&&units.Any(u=>Same(u,state)))
            {
                bool engaging=state.TargetKind==PlayableRouteTargetKind.VisibleEnemy&&units.Any(u=>u.TargetId==state.TargetId);
                SetPhase(registry,engaging?AiArmyPhase.Engaging:AiArmyPhase.Advancing,o.Tick,progress);
            }
            else if(state.Phase==AiArmyPhase.Forming)SetPhase(registry,AiArmyPhase.Staging,o.Tick,progress);
            if(state.TargetKind==PlayableRouteTargetKind.PublicObjective&&units.All(u=>Distance(u.Position,state.Target)<=PlayableUnitRules.Radius(oProfile, u.Kind)))
            {Release(registry,o.Tick,"public objective reached");return;}
            long deadline=AiProfile.SecondsToTicks(profile.Value(state.Phase==AiArmyPhase.Forming||state.Phase==AiArmyPhase.Staging?"armies.assemblyDeadlineSeconds":"armies.stallSeconds"),30);
            if(o.Tick-state.ProgressTick>=deadline)
            {
                if(AiTacticalExecutor.Withdrawal(o,army,oProfile,profile,nextId,stalled:true)!=null)
                {state.Reason="withdrawal";SetPhase(registry,AiArmyPhase.Regrouping,o.Tick);}
                else Release(registry,o.Tick,"no meaningful progress deadline");
            }
            else registry.UpdateMission(state.ArmyId,Policy,state.Phase,o.Tick,Key(state.TargetKind,state.TargetId),rally:state.Target,progressed:progress);
        }
        // Gameplay capture/arrival radii are read from the immutable gameplay profile,
        // never copied into tunable AI stats. Rebind retains valid mission identity.
        private PlayableProfile oProfile=PlayableProfile.Default;
        internal void Rebind(PlayableProfile p){oProfile=p;}
        public bool SetPhase(AiArmyRegistry registry,AiArmyPhase phase,long tick,bool progressed=false)
        {
            if(!Active||tick<state.PhaseTick||!Enum.IsDefined(typeof(AiArmyPhase),phase)||phase==AiArmyPhase.Disbanded)return false;
            if(!registry.UpdateMission(state.ArmyId,Policy,phase,tick,Key(state.TargetKind,state.TargetId),anchor:phase==AiArmyPhase.Retreating||phase==AiArmyPhase.Recovering?registry.Capture().Armies.Single(a=>a.Id==state.ArmyId).Anchor:null,rally:state.Target,progressed:progressed))return false;
            if(state.Phase!=phase){state.Phase=phase;state.PhaseTick=tick;}if(progressed)state.ProgressTick=tick;return true;
        }
        private void Release(AiArmyRegistry registry,long tick,string reason,bool disband=true)
        {
            if(Active){completed[Key(state.TargetKind,state.TargetId)]=tick;if(disband)registry.Disband(state.ArmyId);}
            state.ArmyId=0;state.Phase=AiArmyPhase.Disbanded;state.PhaseTick=tick;state.Reason=reason;applied=false;
        }
        public PlayableAiAction Propose(PlayableAiObservation o,PlayableAiOpeningCompositionState opening,PlayableProfile gameplay,AiProfile profile,AiArmyRegistry registry)
            =>ProposeWithRecords(o,opening,gameplay,profile,registry,null);
        internal PlayableAiAction ProposeWithRecords(PlayableAiObservation o,PlayableAiOpeningCompositionState opening,PlayableProfile gameplay,AiProfile profile,AiArmyRegistry registry,IEnumerable<PlayableAiTraceRecord> records)
        {
            Bind(o);if(opening==null||opening.OwnerId!=owner||opening.MatchSeed!=o.Seed)throw new ArgumentException("Opening binding");
            if(pendingId!=0)return null;
            AiArmyMissionState plan=state;
            int[] ids;
            if(Active)
            {
                var a=registry.Capture().Armies.FirstOrDefault(a=>a.Id==state.ArmyId&&a.Phase!=AiArmyPhase.Disbanded&&a.TacticalOwner==Policy);
                if(a==null)return null;
                long repeat=AiProfile.SecondsToTicks(profile.Value("decision.repeatOrderSeconds"),30);
                var assembly=AiTacticalExecutor.RecoveryAssembly(o,a,gameplay,nextId,repeat,records);
                if(assembly!=null)return assembly;
                var ready=new HashSet<int>(AiTacticalExecutor.Ready(o,a,gameplay).Where(u=>u.CurrentOrder==null||o.Tick-u.CurrentOrder.IssuedTick>=repeat).Select(u=>u.Id));
                bool joining=o.Entities.Any(u=>ready.Contains(u.Id)&&AiRosterCatalog.Initial.For(u.Kind).lineWeight>0&&o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==state.TargetKind&&r.TargetId==state.TargetId));
                var fallback=AiTacticalExecutor.Withdrawal(o,a,gameplay,profile,nextId,stalled:state.Reason=="withdrawal");
                if(fallback!=null)return fallback;
                var correction=AiTacticalExecutor.ArrivalAssembly(o,a,gameplay,nextId,records);
                if(correction!=null)return correction;
                var tactical=joining&&state.Phase!=AiArmyPhase.Regrouping&&state.Phase!=AiArmyPhase.Retreating&&state.Phase!=AiArmyPhase.Recovering?null:AiTacticalExecutor.Propose(o,a,gameplay,nextId);
                if(tactical!=null)return tactical;
                ids=o.Entities.Where(u=>a.Members.Contains(u.Id)&&AiRosterCatalog.Initial.For(u.Kind).lineWeight>0&&(!a.Reinforcements.Contains(u.Id)||ready.Contains(u.Id)&&o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==state.TargetKind&&r.TargetId==state.TargetId))&&!Same(u,state)&&(u.CurrentOrder==null||o.Tick-u.CurrentOrder.IssuedTick>=repeat)).Select(u=>u.Id).OrderBy(x=>x).ToArray();
                if(state.Phase==AiArmyPhase.Regrouping||state.Phase==AiArmyPhase.Retreating||state.Phase==AiArmyPhase.Recovering)return null; // Withdrawal/recovery owns these phases.
            }
            else
            {
                // Safe has actual minimum economic commitments; after completion the name
                // of the opening imposes no permanent ban on pressure.
                if(opening.Opening==PlayableAiOpening.Safe&&!Ready(o,PlayableBuildingKind.Factory,1)||
                    opening.Opening==PlayableAiOpening.Safe&&!Ready(o,PlayableBuildingKind.Refinery,1))return null;
                if(registry.Capture().Armies.Count(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded)>=registry.MajorCap)return null;
                var free=Fighters(o).Where(u=>registry.ArmyFor(u.Id)==0).ToArray();
                if(free.Length<profile.Value("armies.minimumUnits")||!Viable(free,gameplay,profile))return null;
                var candidates=o.RouteProofs.Where(r=>free.Any(u=>u.Id==r.UnitId)&&
                    (r.Kind==PlayableRouteTargetKind.VisibleEnemy||r.Kind==PlayableRouteTargetKind.PublicObjective&&o.PublicScoutObjectives.Any(g=>g.SiteId==r.TargetId&&g.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart&&g.Reachable)))
                    .Where(r=>!completed.TryGetValue(Key(r.Kind,r.TargetId),out var tick)||o.Tick-tick>=AiProfile.SecondsToTicks(profile.Value("decision.retrySeconds"),30))
                    .GroupBy(r=>Key(r.Kind,r.TargetId)).Select(g=>new{Route=g.OrderBy(r=>r.UnitId).First(),Units=free.Where(u=>g.Any(r=>r.UnitId==u.Id)).Select(u=>u.Id).ToArray()})
                    .Where(g=>g.Units.Length>=profile.Value("armies.minimumUnits")&&Viable(free.Where(u=>g.Units.Contains(u.Id)),gameplay,profile))
                    .OrderBy(g=>g.Route.Kind).ThenBy(g=>g.Route.TargetId).ToArray();
                var choice=candidates.FirstOrDefault();if(choice==null)return null;
                ids=choice.Units;plan=new AiArmyMissionState{TargetKind=choice.Route.Kind,TargetId=choice.Route.TargetId,Target=choice.Route.Target};
            }
            var recipients=o.Entities.Where(u=>ids.Contains(u.Id)).ToArray();
            if(ids.Length==0||recipients.Length!=ids.Length||recipients.All(u=>Same(u,plan)))return null;
            if(!o.RouteProofs.Any(r=>ids.Contains(r.UnitId)&&r.Kind==plan.TargetKind&&r.TargetId==plan.TargetId))return null;
            return new PlayableAiAction(nextId,owner,o.ProfileId,o.ProfileRevision,generation,o.SnapshotSequence,
                plan.TargetKind==PlayableRouteTargetKind.VisibleEnemy?PlayableCommandKind.Attack:PlayableCommandKind.AttackMove,ids,plan.Target,targetId:plan.TargetKind==PlayableRouteTargetKind.VisibleEnemy?plan.TargetId:0,
                siteId:plan.TargetKind==PlayableRouteTargetKind.PublicObjective?plan.TargetId:0,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
        private static bool Ready(PlayableAiObservation o,PlayableBuildingKind kind,int count)=>o.Buildings.Count(b=>b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.Kind==kind)>=count;
        public void Commit(PlayableAiObservation o,PlayableAiAction action,AiArmyRegistry registry)
        {
            Bind(o);if(action==null||pendingId!=0||action.ActionId!=nextId||action.PlayerId!=owner||action.Generation!=generation||action.SnapshotSequence!=o.SnapshotSequence)throw new InvalidOperationException("Selected mission binding");
            if(!Active)
            {
                if(!registry.TryCreate(o,AiArmyRole.Attack,Policy,action.EntityIds,out var id))throw new InvalidOperationException("Selected mission lost atomic membership admission");
                state=new AiArmyMissionState{ArmyId=id,TargetKind=action.TargetId>0?PlayableRouteTargetKind.VisibleEnemy:PlayableRouteTargetKind.PublicObjective,TargetId=action.TargetId>0?action.TargetId:action.SiteId,Target=action.Target,StartedTick=o.Tick,ProgressTick=o.Tick,PhaseTick=o.Tick,LastInitiativeTick=o.Tick,LastSeenTick=o.Tick,TargetVisible=action.TargetId>0,Phase=AiArmyPhase.Forming,Reason="selected useful pressure"};
                distance=o.Entities.Where(u=>action.EntityIds.Contains(u.Id)).Average(u=>Distance(u.Position,state.Target));targetHealth=Health(o,state);applied=false;
                registry.UpdateMission(id,Policy,state.Phase,o.Tick,Key(state.TargetKind,state.TargetId),rally:state.Target);
            }
            else if(action.Kind==PlayableCommandKind.Move&&state.Reason=="withdrawal"&&(state.Phase==AiArmyPhase.Regrouping||state.Phase==AiArmyPhase.Retreating)&&
                action.EntityIds.All(id=>!registry.Capture().Armies.Single(a=>a.Id==state.ArmyId).Reinforcements.Contains(id)&&o.RouteProofs.Any(r=>r.UnitId==id&&r.Goal.Equals(action.Target)&&AiTacticalExecutor.WithdrawalRoute(o,r,oProfile))))
            {
                state.Reason="withdrawal";bool starting=state.Phase!=AiArmyPhase.Retreating;
                SetPhase(registry,AiArmyPhase.Retreating,o.Tick);
                if(starting)
                {
                    var a=registry.Capture().Armies.Single(x=>x.Id==state.ArmyId);
                    distance=o.Entities.Where(u=>a.Members.Contains(u.Id)&&!a.Reinforcements.Contains(u.Id)).Average(u=>Distance(u.Position,action.Target));
                    registry.UpdateMission(state.ArmyId,Policy,state.Phase,o.Tick,Key(state.TargetKind,state.TargetId),anchor:action.Target,rally:state.Target);
                }
            }
            pendingId=nextId++;pendingGeneration=generation;
        }
        public void ObserveReceipt(PlayableAiTraceRecord r)
        {
            if(r==null||r.ActionId!=pendingId||r.OwnerId!=owner||r.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity||!AiStateWire.CallbackGenerationMatches(r,pendingGeneration)||r.Status==PlayableAiDeliveryStatus.Scheduled||r.Status==PlayableAiDeliveryStatus.Accepted)return;
            if(r.Status==PlayableAiDeliveryStatus.Applied&&Active)applied=true;
            pendingId=pendingGeneration=0;
        }
        public AiArmyDeployment Deployment(PlayableAiObservation o,AiArmyRegistry registry)
        {
            Bind(o);
            if(registry==null||registry.OwnerId!=owner||registry.Generation!=generation)return null;
            if(!Active||!applied||pendingId!=0||state.Phase!=AiArmyPhase.Advancing&&state.Phase!=AiArmyPhase.Engaging||
                state.TargetKind==PlayableRouteTargetKind.VisibleEnemy&&!state.TargetVisible)return null;
            var army=registry.Capture().Armies.FirstOrDefault(a=>a.Id==state.ArmyId&&a.Phase==state.Phase&&a.TacticalOwner==Policy&&a.Major&&
                a.OwnerId==owner&&a.Objective==Key(state.TargetKind,state.TargetId)&&a.Rally.HasValue&&a.Rally.Value.Equals(state.Target));
            if(army==null||!o.Entities.Any(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&Same(u,state)))return null;
            return new AiArmyDeployment(o,state.ArmyId,state.StartedTick);
        }
        internal void ReconcileRegistry(AiArmyRegistry registry,long tick,string reason="registry released mission at profile barrier")
        {
            if(Active&&!registry.Capture().Armies.Any(a=>a.Id==state.ArmyId&&a.Phase!=AiArmyPhase.Disbanded&&a.TacticalOwner==Policy))
                Release(registry,tick,reason,disband:false);
        }
        public void Stop(){state.ArmyId=0;state.Phase=AiArmyPhase.Disbanded;state.Reason="runtime stopped";pendingId=pendingGeneration=0;applied=false;}
        // These requests contain only detached owner entities and public targets. The
        // existing observer/router verifies each proof; commands use the native route service.
        internal static PlayableRouteRequest[] RouteRequests(IEnumerable<PlayableEntitySnapshot> entities,IEnumerable<PlayableBuildingSnapshot> buildings,IEnumerable<PlayablePublicScoutObjective> objectives,PlayableOwner owner,long generation,long tick,PlayableProfile profile,Func<PlayableOwner,bool> hostile)
        {
            var es=entities.ToArray();var targets=es.Where(e=>hostile(e.Owner)&&e.Health>0).Select(e=>new{Id=e.Id,Point=e.Position,Kind=PlayableRouteTargetKind.VisibleEnemy})
                .Concat(buildings.Where(b=>hostile(b.Owner)&&b.Health>0).Select(b=>new{Id=b.Id,Point=b.Position,Kind=PlayableRouteTargetKind.VisibleEnemy}))
                .Concat(objectives.Where(g=>g.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart&&g.Reachable).Select(g=>new{Id=g.SiteId,Point=g.Approach,Kind=PlayableRouteTargetKind.PublicObjective})).OrderBy(t=>t.Kind).ThenBy(t=>t.Id).ToArray();
            return es.Where(e=>e.Owner==owner&&e.Health>0&&AiRosterCatalog.Initial.For(e.Kind).lineWeight+AiRosterCatalog.Initial.For(e.Kind).supportWeight>0).OrderBy(e=>e.Id)
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
            AiStateWire.Require((state.TargetKind==PlayableRouteTargetKind.VisibleEnemy||state.TargetKind==PlayableRouteTargetKind.PublicObjective)&&state.ArmyId>=0&&state.StartedTick>=0&&state.StartedTick<=tick&&state.ProgressTick>=state.StartedTick&&state.ProgressTick<=tick&&state.PhaseTick>=state.StartedTick&&state.PhaseTick<=tick&&state.LastInitiativeTick>=0&&state.LastInitiativeTick<=tick&&state.LastSeenTick>=0&&state.LastSeenTick<=tick&&state.Reason!=null&&
                (Active?state.TargetId>0&&state.Phase!=AiArmyPhase.Disbanded&&registry.Capture().Armies.Any(a=>a.Id==state.ArmyId&&a.Phase==state.Phase&&a.ProgressTick==state.ProgressTick&&a.TacticalOwner==Policy&&a.Objective==Key(state.TargetKind,state.TargetId)&&a.Rally.HasValue&&a.Rally.Value.Equals(state.Target)):state.Phase==AiArmyPhase.Disbanded)&&nextId>pendingId&&pendingId>=0&&(pendingId==0?pendingGeneration==0:pendingGeneration==generation&&Active)&&distance>=0&&targetHealth>=-1&&completed.All(x=>x.Key!=null&&x.Value>=0&&x.Value<=tick),"army mission state/clocks/membership/callback");
        }
    }
}
