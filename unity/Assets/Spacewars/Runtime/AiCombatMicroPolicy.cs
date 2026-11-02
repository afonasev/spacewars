using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal enum AiCombatMicroPhase { Withdraw, Return, Repair, Regroup, Focus }
    internal sealed class AiCombatMicroPlan
    {
        internal PlayableAiAction Action;
        internal AiCombatMicroPhase Phase;
        internal long Army;
        internal bool Survival;
    }
    // Temporary per-unit control never transfers the army's strategic owner.
    internal sealed partial class AiCombatMicroPolicy
    {
        internal const string Policy="combat-micro";
        sealed class Fire {internal int Source,Victim;internal long Tick;}
        readonly List<Fire> fire=new List<Fire>();
        sealed class Sample {internal long Tick,DamageTick=-1;internal double Health,Loss;}
        sealed class Certificate
        {
            internal int Unit,Target;
            internal long Army,Generation,Sequence,ActionId,Tick,Decision,Ordinal;
            internal string Owner,Source;
            internal PlayableCommandKind Kind;
            internal NavPoint Goal;
        }
        readonly Dictionary<int,Certificate> certificates=new Dictionary<int,Certificate>();
        sealed class Client
        {
            internal AiCombatMicroPhase Phase;
            internal long Started,SafeSince=-1,Sequence,ActionId,Army;
            internal int Tower;
            internal NavPoint Goal;
        }
        readonly Dictionary<int,Sample> samples=new Dictionary<int,Sample>();
        readonly Dictionary<int,Client> clients=new Dictionary<int,Client>();
        readonly Dictionary<int,long> cooldowns=new Dictionary<int,long>();
        long nextId=1,pendingId,pendingGeneration;
        AiCombatMicroPlan proposed;
        AiCombatMicroPlan pending;
        internal long PendingId=>pendingId;
        internal long PendingGeneration=>pendingGeneration;
        internal IEnumerable<int> ReservedActors=>clients.Keys.Where(Reserves);
        static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        static double Health(PlayableEntitySnapshot u)=>u.ExactHealth??u.Health;
        static bool Eligible(PlayableEntitySnapshot u)=>u.Health>0&&!u.Held&&!u.Departing&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human;
        static bool Tower(PlayableBuildingSnapshot b,PlayableOwner owner)=>b.Owner==owner&&b.Health>0&&b.Kind==PlayableBuildingKind.RepairTower&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true;
        PlayableEntitySnapshot[] Attackers(PlayableAiObservation o,PlayableEntitySnapshot u,PlayableProfile p)=>o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Health>0&&fire.Any(f=>f.Source==e.Id&&f.Victim==u.Id)&&o.Vision?.IsVisible(e.Position)==true&&AiRosterCatalog.Initial.Targets(e.Kind,u.Kind==PlayableEntityKind.Hawk?"air":"ground")&&Distance(e.Position,u.Position)<=PlayableUnitRules.Range(p,e.Kind,e.Upgraded)+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,e.Kind)).ToArray();
        static AiArmyState Army(AiArmyRegistry registry,int unit)=>registry.Capture().Armies.FirstOrDefault(a=>a.Members.Contains(unit)&&a.Major&&a.Phase!=AiArmyPhase.Disbanded&&!a.Reinforcements.Contains(unit));
        static long T(AiProfile ai,AiDifficulty d,string key)=>AiProfile.SecondsToTicks(ai.DifficultyValue(d,"micro."+key),30);
        static double V(AiProfile ai,AiDifficulty d,string key)=>ai.DifficultyValue(d,"micro."+key);
        internal bool Reserves(int unit)=>clients.TryGetValue(unit,out var c)&&(c.Phase==AiCombatMicroPhase.Withdraw||c.Phase==AiCombatMicroPhase.Repair||c.Phase==AiCombatMicroPhase.Return);
        internal bool CurrentOverride(PlayableAiObservation o,AiArmyState army,PlayableEntitySnapshot u)
        {
            if(o==null||army==null||u==null||!certificates.TryGetValue(u.Id,out var c)||c.Army!=army.Id||c.Army<=0||army.OwnerId!=o.OwnerId||!army.Major||army.Phase==AiArmyPhase.Disbanded||!army.Members.Contains(u.Id)||army.Reinforcements.Contains(u.Id)||!Eligible(u)||u.Owner!=o.Owner)return false;
            return Matches(c,o,u);
        }
        static bool Matches(Certificate c,PlayableAiObservation o,PlayableEntitySnapshot u)=>Matches(c,o.OwnerId,o.Owner,o.Generation,o.Tick,u);
        static bool Matches(Certificate c,string ownerId,PlayableOwner owner,long generation,long tick,PlayableEntitySnapshot u)
        {
            var s=u.OrderStamp;
            if(c.Unit!=u.Id||c.Owner!=ownerId||c.Generation!=generation||c.Tick>tick||c.Source!=PlayableAiOpeningComposition.SourceIdentity||c.Sequence<=0||c.ActionId<=0||c.Decision<=0||c.Ordinal<=0||s==null||s.UnitId!=u.Id||s.Owner!=owner||s.Generation!=c.Generation||s.Origin!=PlayableOrderOrigin.Ai||s.Sequence!=c.Sequence||s.ActionId!=c.ActionId||s.JobId!=c.ActionId||s.Source!=c.Source||s.Tick!=c.Tick||s.Kind!=c.Kind)return false;
            var pending=u.Queue?.Pending;
            if(pending!=null&&pending.CommandSequence==c.Sequence&&pending.AiActionId==c.ActionId&&pending.Kind==c.Kind)return true;
            var order=u.CurrentOrder;
            if(order!=null)return order.Owner==owner&&order.UnitId==u.Id&&order.Generation==c.Generation&&order.CommandSequence==c.Sequence&&order.IssuedTick==c.Tick&&
                (c.Kind==PlayableCommandKind.Move?order.Kind==PlayableTacticalOrderKind.Move&&order.Destination.Equals(c.Goal):c.Kind==PlayableCommandKind.Follow?order.Kind==PlayableTacticalOrderKind.Follow&&order.TargetId==c.Target:c.Kind==PlayableCommandKind.Attack&&order.Kind==PlayableTacticalOrderKind.Attack&&order.TargetId==c.Target);
            // Stop and completed Move are still the current accepted order. They
            // confer no reservation and cannot authorize a future replacement.
            return c.Kind==PlayableCommandKind.Stop||c.Kind==PlayableCommandKind.Move&&u.NavigationOutcome==NavigationOutcome.Arrived;
        }
        bool AppliedRepair(PlayableAiObservation o,PlayableEntitySnapshot u)=>
            Eligible(u)&&clients.TryGetValue(u.Id,out var client)&&client.Phase==AiCombatMicroPhase.Repair&&client.Sequence>0&&
            certificates.TryGetValue(u.Id,out var certificate)&&certificate.Kind==PlayableCommandKind.Move&&
            certificate.Army==client.Army&&certificate.Sequence==client.Sequence&&certificate.ActionId==client.ActionId&&
            certificate.Goal.Equals(client.Goal)&&Matches(certificate,o,u);
        internal static bool ReturnJoined(PlayableProfile p,PlayableEntitySnapshot unit,PlayableEntitySnapshot leader)=>
            // Ordinary ground Follow stops radially (PlayableFollow.AdvanceFollow).
            // Its behind-leader point is a routing destination, not the join test.
            // Air Follow instead uses its exact point tolerance (PlayableHawkFlight).
            unit.Kind==PlayableEntityKind.Hawk?Distance(unit.Position,PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading))<=p.FollowArrivalTolerance:
                Distance(unit.Position,leader.Position)<=p.FollowDistance+p.FollowArrivalTolerance;
        void Release(int id,long tick,AiProfile ai,AiDifficulty d)
        {clients.Remove(id);cooldowns[id]=tick+T(ai,d,"cooldownSeconds");}
        internal void Observe(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyRegistry armies)
        {
            foreach(var id in certificates.Keys.ToArray())
            {
                var u=o.Entities.FirstOrDefault(e=>e.Id==id&&e.Owner==o.Owner&&e.Health>0);
                var c=certificates[id];
                if(u==null||!Eligible(u)||!Matches(c,o,u)||c.Army!=0&&!armies.Capture().Armies.Any(a=>a.Id==c.Army&&a.Major&&a.Phase!=AiArmyPhase.Disbanded&&a.Members.Contains(id)&&!a.Reinforcements.Contains(id))&&!AppliedRepair(o,u)&&!CancelFollow(o,p,ai,d,armies,u))certificates.Remove(id);
            }
            long fireWindow=T(ai,d,"observedFireSeconds");
            fire.RemoveAll(f=>o.Tick-f.Tick>fireWindow||!o.Entities.Any(e=>e.Id==f.Victim&&e.Owner==o.Owner&&e.Health>0));
            foreach(var shot in o.ObservedHostileShots)
            {
                var fact=fire.FirstOrDefault(f=>f.Source==shot.OwnerId&&f.Victim==shot.TargetId);
                if(fact==null){fact=new Fire{Source=shot.OwnerId,Victim=shot.TargetId};fire.Add(fact);}fact.Tick=o.Tick;
            }
            var live=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0).ToDictionary(u=>u.Id);
            foreach(var id in samples.Keys.Where(id=>!live.ContainsKey(id)).ToArray())samples.Remove(id);
            foreach(var id in cooldowns.Keys.Where(id=>!live.ContainsKey(id)||cooldowns[id]<=o.Tick).ToArray())cooldowns.Remove(id);
            foreach(var u in live.Values)
            {
                if(!samples.TryGetValue(u.Id,out var s))samples[u.Id]=s=new Sample{Tick=o.Tick,Health=Health(u)};
                if(o.Tick>s.Tick)
                {
                    double loss=Math.Max(0,s.Health-Health(u));
                    if(loss>0){s.Loss=loss*30/(o.Tick-s.Tick);s.DamageTick=o.Tick;}
                    else if(s.DamageTick>=0&&o.Tick-s.DamageTick>T(ai,d,"safeSeconds"))s.Loss=0;
                    s.Tick=o.Tick;s.Health=Health(u);
                }
            }
            foreach(var pair in clients.ToArray())
            {
                var c=pair.Value;
                // A lost core cannot leave an old micro Follow running. Preserve
                // only its exact certificate until a normal budgeted Stop cancels it.
                if(live.TryGetValue(pair.Key,out var returning)&&c.Phase==AiCombatMicroPhase.Return&&CancelFollow(o,p,ai,d,armies,returning))continue;
                if(!live.TryGetValue(pair.Key,out var u)||!Eligible(u)||o.Tick-c.Started>T(ai,d,"maxPhaseSeconds")||
                    c.Army!=0&&!armies.Capture().Armies.Any(a=>a.Id==c.Army&&a.Major&&a.Phase!=AiArmyPhase.Disbanded&&a.Members.Contains(u.Id))&&!AppliedRepair(o,u)||
                    c.Phase==AiCombatMicroPhase.Repair&&!o.Buildings.Any(b=>b.Id==c.Tower&&Tower(b,o.Owner)))
                {Release(pair.Key,o.Tick,ai,d);continue;}
                if(c.Sequence>0)
                {
                    // Pending route is also an ordinary applied command; never mistake it
                    // for arrival, and never reserve over a later foreign/human order.
                    var q=u.Queue?.Pending;var stamp=u.OrderStamp;
                    bool pendingRoute=q!=null&&q.CommandSequence==c.Sequence&&q.AiActionId==c.ActionId;
                    bool installed=stamp!=null&&stamp.Owner==o.Owner&&stamp.UnitId==u.Id&&stamp.Generation==o.Generation&&stamp.Origin==PlayableOrderOrigin.Ai&&stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&stamp.Sequence==c.Sequence&&stamp.ActionId==c.ActionId&&stamp.JobId==c.ActionId;
                    if(!pendingRoute&&!installed||(u.NavigationOutcome==NavigationOutcome.Unreachable||u.NavigationOutcome==NavigationOutcome.Rejected))
                    {Release(pair.Key,o.Tick,ai,d);continue;}
                    if(c.Phase==AiCombatMicroPhase.Focus&&u.CurrentOrder==null&&u.Queue?.Pending==null){Release(pair.Key,o.Tick,ai,d);continue;}
                    if(c.Phase==AiCombatMicroPhase.Return)
                    {
                        var leader=o.Entities.FirstOrDefault(e=>e.Id==u.CurrentOrder?.TargetId&&e.Owner==o.Owner&&e.Health>0);
                        if(leader!=null&&ReturnJoined(p,u,leader))Release(pair.Key,o.Tick,ai,d);
                        continue;
                    }
                    if(c.Phase==AiCombatMicroPhase.Regroup){Release(pair.Key,o.Tick,ai,d);continue;}
                }
                bool danger=Attackers(o,u,p).Length>0||samples[u.Id].Loss>0;
                c.SafeSince=danger?-1:c.SafeSince<0?o.Tick:c.SafeSince;
            }
        }
        internal int ObservedAttackCount(PlayableAiObservation o,PlayableEntitySnapshot unit,PlayableProfile p)=>Attackers(o,unit,p).Length;
        bool Attention(PlayableAiObservation o,int id,AiProfile ai,AiDifficulty d)
        {
            if(cooldowns.TryGetValue(id,out var until)&&o.Tick<until)return false;
            // Reproducible opportunity sampling once per cooldown window; no global RNG changes.
            long window=o.Tick/Math.Max(1,T(ai,d,"cooldownSeconds"));
            uint hash=unchecked((uint)(o.Seed*397+id*7919+window*104729));hash^=hash>>16;hash*=2246822519u;hash^=hash>>13;
            return (hash%10000)/10000d<V(ai,d,"intensity");
        }
        PlayableAiAction Action(PlayableAiObservation o,PlayableCommandKind kind,IEnumerable<int> ids,NavPoint goal,int target=0,int tower=0)=>new PlayableAiAction(nextId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,kind,ids.ToArray(),goal,targetId:target,parentId:tower,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        internal AiCombatMicroPlan Propose(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyRegistry armies)
        {
            if(pendingId!=0)return null;
            var own=o.Entities.Where(u=>u.Owner==o.Owner&&Eligible(u)).OrderBy(u=>u.Id).ToArray();
            foreach(var u in own.Where(u=>CancelFollow(o,p,ai,d,armies,u)))
                return new AiCombatMicroPlan{Action=Action(o,PlayableCommandKind.Stop,new[]{u.Id},u.Position),Phase=AiCombatMicroPhase.Regroup,Army=certificates[u.Id].Army};
            foreach(var pair in clients.OrderBy(x=>x.Key).ToArray())
            {
                var c=pair.Value;var u=own.FirstOrDefault(u=>u.Id==pair.Key);if(u==null||c.Sequence==0)continue;
                if(c.Phase==AiCombatMicroPhase.Focus)continue;
                if(c.Phase==AiCombatMicroPhase.Withdraw&&V(ai,d,"repairEnabled")>0&&PlayableUnitRules.HealthRatio(p,u)<V(ai,d,"combatRepairHealth")&&
                    Army(armies,u.Id)?.Id==c.Army&&u.Queue?.Pending==null&&(u.Queue==null||u.Queue.Deferred.Count==0)&&
                    certificates.TryGetValue(u.Id,out var applied)&&Matches(applied,o,u))
                {
                    var repair=Repair(o,p,ai,d,u,Army(armies,u.Id));if(repair!=null)return repair;
                }
                bool ready=c.Phase==AiCombatMicroPhase.Repair?PlayableUnitRules.HealthRatio(p,u)>=V(ai,d,"repairReturnHealth"):
                    c.Phase==AiCombatMicroPhase.Withdraw&&c.SafeSince>=0&&o.Tick-c.SafeSince>=T(ai,d,"safeSeconds");
                if(!ready)continue;
                if(c.Phase==AiCombatMicroPhase.Repair&&c.Army==0){Release(u.Id,o.Tick,ai,d);continue;}
                // Repair custody records the accepted job's original army. A new
                // return command requires independently current membership.
                long returnArmy=c.Phase==AiCombatMicroPhase.Repair?Army(armies,u.Id)?.Id??0:c.Army;
                var anchor=ReturnAnchor(o,p,ai,d,armies,u,returnArmy);
                if(anchor!=null)
                    return new AiCombatMicroPlan{Action=Action(o,PlayableCommandKind.Follow,new[]{u.Id},PlayableUnitRules.FollowGoal(p,anchor.Position,anchor.HullHeading),anchor.Id),Phase=AiCombatMicroPhase.Return,Army=returnArmy};
                // No viable current core: stay at the safe recovery position. Never
                // restore an old enemy destination or chase the sole survivor.
                Release(u.Id,o.Tick,ai,d);
            }
            foreach(var u in own.Where(u=>(!clients.TryGetValue(u.Id,out var active)||active.Phase==AiCombatMicroPhase.Focus)&&u.Queue?.Pending==null&&(u.Queue==null||u.Queue.Deferred.Count==0)&&u.CurrentOrder?.Kind!=PlayableTacticalOrderKind.Follow&&Attention(o,u.Id,ai,d)).OrderBy(u=>PlayableUnitRules.HealthRatio(p,u)))
            {
                var army=Army(armies,u.Id);var threats=Attackers(o,u,p);
                samples.TryGetValue(u.Id,out var s);double loss=s?.Loss??0;
                bool damaged=s?.DamageTick>=0&&o.Tick-s.DamageTick<=T(ai,d,"safeSeconds");
                bool combat=threats.Length>0||u.TargetId>0||u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack||u.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove;
                if(RepairEligible(o,p,ai,d,u,army,combat))
                {
                    var repair=Repair(o,p,ai,d,u,army);if(repair!=null)return repair;
                }
                if(army==null||V(ai,d,"withdrawEnabled")==0||!damaged||threats.Length<V(ai,d,"focusAttackers")||
                    PlayableUnitRules.HealthRatio(p,u)>V(ai,d,"withdrawHealth")&&loss/Math.Max(1,u.MaxHealth)<V(ai,d,"lossPerSecond"))continue;
                var goal=Withdrawal(o,p,ai,d,u,threats);if(!goal.HasValue)continue;
                double seconds=Distance(u.Position,goal.Value)/Math.Max(double.Epsilon,PlayableUnitRules.Speed(p,u.Kind));
                // Only factual recent loss is used. When escape takes longer than
                // the remaining observed life, leave ordinary fire untouched.
                if(loss>0&&Health(u)/loss<=seconds)continue;
                return new AiCombatMicroPlan{Action=Action(o,PlayableCommandKind.Move,new[]{u.Id},goal.Value),Phase=AiCombatMicroPhase.Withdraw,Army=army.Id,Survival=true};
            }
            var chase=Chase(o,p,ai,d,armies,own);return chase??Focus(o,p,ai,d,armies,own);
        }
        internal AiCombatMicroPlan Plan(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyRegistry armies)
        {proposed=Propose(o,p,ai,d,armies);return proposed;}
        internal AiCombatMicroPhase PendingPhase=>pending?.Phase??proposed?.Phase??AiCombatMicroPhase.Focus;
        internal bool Authorizes(AiIntent intent,PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty difficulty,AiArmyRegistry registry)
        {
            var plan=pending??proposed;var action=intent.Action;
            if(o==null||plan==null||action.PlayerId!=o.OwnerId||action.Generation!=o.Generation||action.Seed!=o.Seed||
                plan.Action.Kind!=action.Kind||plan.Action.TargetId!=action.TargetId||!plan.Action.Target.Equals(action.Target)||!plan.Action.EntityIds.SequenceEqual(action.EntityIds))return false;
            if(plan.Phase==AiCombatMicroPhase.Focus&&!FocusCurrent(o,p,ai,difficulty,registry,plan,action))return false;
            if(plan.Phase==AiCombatMicroPhase.Repair&&!action.EntityIds.All(id=>o.Entities.Any(u=>u.Id==id&&RepairEligible(o,p,ai,difficulty,u,Army(registry,id))&&RepairAvailable(o,p,ai,difficulty,u,action.ParentId))))return false;
            if(plan.Phase==AiCombatMicroPhase.Return&&(!action.EntityIds.All(id=>o.Entities.Any(u=>u.Id==id&&ValidReturnTarget(o,p,ai,difficulty,registry,u,plan.Army,action.TargetId)))))return false;
            if(plan.Phase==AiCombatMicroPhase.Regroup&&action.Kind==PlayableCommandKind.Stop&&action.EntityIds.All(id=>o.Entities.Any(u=>u.Id==id&&certificates.TryGetValue(id,out var previous)&&previous.Army==plan.Army&&CancelFollow(o,p,ai,difficulty,registry,u))))return true;
            return action.EntityIds.All(id=>o.Entities.Any(u=>u.Id==id&&u.Owner==o.Owner&&Eligible(u)&&
                (plan.Army==0?registry.ArmyFor(id)==0&&plan.Phase==AiCombatMicroPhase.Repair&&u.CurrentOrder==null&&(u.Queue==null||u.Queue.Pending==null&&u.Queue.Deferred.Count==0):
                 registry.Capture().Armies.Any(a=>a.Id==plan.Army&&a.Major&&a.Phase!=AiArmyPhase.Disbanded&&a.OwnerId==o.OwnerId&&a.Members.Contains(id)&&!a.Reinforcements.Contains(id)))));
        }
        internal void Preempt(System.Collections.Generic.IEnumerable<int> ids)
        {foreach(var id in ids){clients.Remove(id);certificates.Remove(id);}}
        static PlayableEntitySnapshot[] ReturnCore(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyRegistry registry,PlayableEntitySnapshot u,long army)
        {
            if(!registry.TryReadGuard(army,out var group)||!ReturnGroup(o.OwnerId,u.Id,group))return Array.Empty<PlayableEntitySnapshot>();
            var core=o.Entities.Where(x=>ReturnCoreCandidate(o.Owner,u.Id,p,ai,d,group,x)).ToArray();
            return core.Where(a=>core.Count(b=>ReturnCoreNeighbor(ai,d,a.Position,b.Position))>=V(ai,d,"returnCoreUnits")).ToArray();
        }
        // These are the same strict ReturnCore predicates used by planning and
        // delivery. GuardView is synchronously borrowed; detached execution work
        // must copy paid operands and later recheck selected live witnesses.
        static bool ReturnGroup(string owner,int patient,AiArmyRegistry.GuardView group)=>
            group.OwnerId==owner&&group.Major&&group.Phase!=AiArmyPhase.Disbanded&&group.Phase!=AiArmyPhase.Retreating&&group.Phase!=AiArmyPhase.Recovering&&group.Phase!=AiArmyPhase.Regrouping&&group.ContainsMember(patient)&&!group.ContainsReinforcement(patient);
        static bool ReturnCoreCandidate(PlayableOwner owner,int patient,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyRegistry.GuardView group,PlayableEntitySnapshot candidate)=>
            candidate.Id!=patient&&candidate.Owner==owner&&Eligible(candidate)&&group.ContainsMember(candidate.Id)&&!group.ContainsReinforcement(candidate.Id)&&AiRosterCatalog.Initial.LineWeight(candidate.Kind)>0&&PlayableUnitRules.HealthRatio(p,candidate)>=V(ai,d,"returnCoreHealth");
        static bool ReturnCoreNeighbor(AiProfile ai,AiDifficulty d,NavPoint anchor,NavPoint peer)=>Distance(anchor,peer)<=V(ai,d,"supportRadius");
        static bool ValidReturnTarget(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyRegistry registry,PlayableEntitySnapshot u,long army,int target)=>ReturnCore(o,p,ai,d,registry,u,army).Any(a=>a.Id==target);
        static PlayableEntitySnapshot ReturnAnchor(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyRegistry registry,PlayableEntitySnapshot u,long army)=>ReturnCore(o,p,ai,d,registry,u,army).OrderBy(a=>Distance(u.Position,a.Position)).ThenBy(a=>a.Id).FirstOrDefault();
        bool CancelFollow(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyRegistry registry,PlayableEntitySnapshot u)=>
            u.Owner==o.Owner&&Eligible(u)&&u.Queue?.Pending==null&&(u.Queue==null||u.Queue.Deferred.Count==0)&&
            certificates.TryGetValue(u.Id,out var c)&&c.Kind==PlayableCommandKind.Follow&&Matches(c,o,u)&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&
            !ValidReturnTarget(o,p,ai,d,registry,u,c.Army,c.Target);
        static NavPoint? Withdrawal(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot u,PlayableEntitySnapshot[] attackers)
        {
            double x=u.Position.X-attackers.Average(e=>e.Position.X),z=u.Position.Z-attackers.Average(e=>e.Position.Z),length=Math.Sqrt(x*x+z*z);
            if(length<double.Epsilon)return null;x/=length;z/=length;
            double first=V(ai,d,"shortStep"),maximum=V(ai,d,"maxWithdrawal");
            // Search short first. Opposing directions, blocked geography and unsafe
            // paths are refused by authoritative observed routing before delivery.
            for(double step=first;step<=maximum;step+=first)
            {
                var goal=new NavPoint(u.Position.X+x*step,u.Position.Z+z*step);
                if(o.Vision?.IsVisible(goal)!=true)continue;
                if(attackers.All(e=>Distance(goal,e.Position)>PlayableUnitRules.Range(p,e.Kind,e.Upgraded)+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,e.Kind)+V(ai,d,"safetyMargin"))&&
                    AiTacticalExecutor.WithdrawalPathForUnit(o,u.Position,new[]{goal},PlayableUnitRules.Radius(p,u.Kind),p,u.Kind==PlayableEntityKind.Hawk))return goal;
            }
            return null;
        }
        internal void Commit(AiCombatMicroPlan plan,long generation,long tick)
        {
            if(plan?.Action==null||pendingId!=0||plan.Action.ActionId!=nextId)throw new InvalidOperationException("Micro pending binding.");
            pending=plan;pendingId=nextId++;pendingGeneration=generation;
            foreach(var id in plan.Action.EntityIds)clients[id]=new Client{Phase=plan.Phase,Started=tick,Army=plan.Army,Tower=plan.Action.ParentId,Goal=plan.Action.Target};
        }
        internal void Applied(PlayableAiAction action,long sequence,long tick,AiReceiptIdentity receipt)
        {
            if(pending==null)return;
            if(receipt==null||receipt.OwnerId!=action.PlayerId||receipt.Generation!=action.Generation||receipt.DecisionOrdinal<=0||receipt.ActionOrdinal<=0)throw new InvalidOperationException("Micro applied receipt binding.");
            foreach(var id in action.EntityIds)if(clients.TryGetValue(id,out var c))
            {
                c.Sequence=sequence;c.ActionId=action.ActionId;
                certificates[id]=new Certificate{Unit=id,Army=c.Army,Owner=action.PlayerId,Generation=action.Generation,Sequence=sequence,ActionId=action.ActionId,Tick=tick,Decision=receipt.DecisionOrdinal,Ordinal=receipt.ActionOrdinal,Source=action.SourceIdentity,Kind=action.Kind,Target=action.TargetId,Goal=action.Target};
            }
        }
        internal void Receipt(PlayableAiTraceRecord r)
        {
            if(r==null||r.ActionId!=pendingId||r.OwnerId!=pending?.Action.PlayerId||r.SourceIdentity!=pending?.Action.SourceIdentity||!AiStateWire.CallbackGenerationMatches(r,pendingGeneration)||r.Status==PlayableAiDeliveryStatus.Scheduled||r.Status==PlayableAiDeliveryStatus.Accepted)return;
            if(r.Status!=PlayableAiDeliveryStatus.Applied)foreach(var id in pending.Action.EntityIds)clients.Remove(id);
            pendingId=pendingGeneration=0;pending=null;
        }
        static bool PhaseKind(AiCombatMicroPhase phase,PlayableCommandKind kind)=>
            phase==AiCombatMicroPhase.Return?kind==PlayableCommandKind.Follow:phase==AiCombatMicroPhase.Focus?kind==PlayableCommandKind.Attack:phase==AiCombatMicroPhase.Regroup?kind==PlayableCommandKind.Stop:kind==PlayableCommandKind.Move;
        internal void ValidateOwner(string owner)
        {
            AiStateWire.Require(certificates.Values.All(c=>c.Owner==owner)&&
                (pending==null||pending.Action.PlayerId==owner),"micro owner binding");
        }
        internal void ValidatePending(PlayableAiAction global,long local)
        {
            // Owner scheduling replaces only the local action id with its global
            // id. Every other serialized field must remain the original proposal.
            AiStateWire.Require(pending!=null&&pendingId==local&&
                WorldWire.Pack(w=>AiStateWire.Write(w,pending.Action)).Skip(9).SequenceEqual(WorldWire.Pack(w=>AiStateWire.Write(w,global)).Skip(9)),"micro saved pending signature");
        }
        internal void Stop(){clients.Clear();certificates.Clear();pending=null;pendingId=pendingGeneration=0;}
        internal void Write(BinaryWriter w)
        {
            WorldWire.Array(w,certificates.OrderBy(x=>x.Key).Select(x=>x.Value).ToArray(),c=>{w.Write(c.Unit);w.Write(c.Army);WorldWire.String(w,c.Owner);w.Write(c.Generation);w.Write(c.Sequence);w.Write(c.ActionId);w.Write(c.Tick);w.Write(c.Decision);w.Write(c.Ordinal);WorldWire.String(w,c.Source);w.Write((int)c.Kind);w.Write(c.Target);WorldWire.Write(w,c.Goal);});
            WorldWire.Array(w,fire.OrderBy(f=>f.Victim).ThenBy(f=>f.Source).ToArray(),f=>{w.Write(f.Source);w.Write(f.Victim);w.Write(f.Tick);});
            w.Write(nextId);w.Write(pendingId);w.Write(pendingGeneration);
            w.Write(pending!=null);if(pending!=null){AiStateWire.Write(w,pending.Action);w.Write((int)pending.Phase);w.Write(pending.Army);w.Write(pending.Survival);}
            WorldWire.Array(w,samples.OrderBy(x=>x.Key).ToArray(),x=>{w.Write(x.Key);w.Write(x.Value.Tick);w.Write(x.Value.DamageTick);WorldWire.Number(w,x.Value.Health);WorldWire.Number(w,x.Value.Loss);});
            WorldWire.Array(w,clients.OrderBy(x=>x.Key).ToArray(),x=>{w.Write(x.Key);var c=x.Value;w.Write((int)c.Phase);w.Write(c.Started);w.Write(c.SafeSince);w.Write(c.Sequence);w.Write(c.ActionId);w.Write(c.Army);w.Write(c.Tower);WorldWire.Write(w,c.Goal);});
            WorldWire.Array(w,cooldowns.OrderBy(x=>x.Key).ToArray(),x=>{w.Write(x.Key);w.Write(x.Value);});
        }
        internal static AiCombatMicroPolicy Read(BinaryReader r,long tick,long generation)
        {
            var savedCertificates=WorldWire.Array(r,()=>new Certificate{Unit=r.ReadInt32(),Army=r.ReadInt64(),Owner=WorldWire.String(r),Generation=r.ReadInt64(),Sequence=r.ReadInt64(),ActionId=r.ReadInt64(),Tick=r.ReadInt64(),Decision=r.ReadInt64(),Ordinal=r.ReadInt64(),Source=WorldWire.String(r),Kind=WorldWire.EnumValue<PlayableCommandKind>(r),Target=r.ReadInt32(),Goal=WorldWire.ReadPoint(r)});
            AiStateWire.Require(savedCertificates.All(c=>c.Unit>0&&c.Army>=0&&!string.IsNullOrWhiteSpace(c.Owner)&&c.Generation==generation&&c.Sequence>0&&c.ActionId>0&&c.Tick>=0&&c.Tick<=tick&&c.Decision>0&&c.Ordinal>0&&c.Source==PlayableAiOpeningComposition.SourceIdentity&&(c.Kind==PlayableCommandKind.Move||c.Kind==PlayableCommandKind.Follow||c.Kind==PlayableCommandKind.Attack||c.Kind==PlayableCommandKind.Stop))&&savedCertificates.Select(c=>c.Unit).Distinct().Count()==savedCertificates.Length,"micro current certificates");
            var facts=WorldWire.Array(r,()=>new Fire{Source=r.ReadInt32(),Victim=r.ReadInt32(),Tick=r.ReadInt64()});
            AiStateWire.Require(facts.All(f=>f.Source>0&&f.Victim>0&&f.Source!=f.Victim&&f.Tick>=0&&f.Tick<=tick)&&facts.Select(f=>f.Source+":"+f.Victim).Distinct().Count()==facts.Length,"micro observed fire");
            var m=new AiCombatMicroPolicy{nextId=r.ReadInt64(),pendingId=r.ReadInt64(),pendingGeneration=r.ReadInt64()};
            foreach(var c in savedCertificates)m.certificates.Add(c.Unit,c);
            m.fire.AddRange(facts);
            if(WorldWire.Boolean(r))m.pending=new AiCombatMicroPlan{Action=AiStateWire.ReadPlayableAiAction(r),Phase=WorldWire.EnumValue<AiCombatMicroPhase>(r),Army=r.ReadInt64(),Survival=WorldWire.Boolean(r)};
            AiStateWire.Require(m.nextId>m.pendingId&&m.pendingId>=0&&(m.pendingId==0?m.pendingGeneration==0&&m.pending==null:m.pendingGeneration==generation&&m.pending?.Action.ActionId==m.pendingId),"micro pending state");
            foreach(var pair in WorldWire.Array(r,()=>new KeyValuePair<int,Sample>(r.ReadInt32(),new Sample{Tick=r.ReadInt64(),DamageTick=r.ReadInt64(),Health=WorldWire.Number(r),Loss=WorldWire.Number(r)})))
            {AiStateWire.Require(pair.Key>0&&pair.Value.Tick>=0&&pair.Value.Tick<=tick&&pair.Value.DamageTick>=-1&&pair.Value.DamageTick<=tick&&pair.Value.Health>=0&&pair.Value.Loss>=0&&!m.samples.ContainsKey(pair.Key),"micro damage sample");m.samples.Add(pair.Key,pair.Value);}
            foreach(var pair in WorldWire.Array(r,()=>new KeyValuePair<int,Client>(r.ReadInt32(),new Client{Phase=WorldWire.EnumValue<AiCombatMicroPhase>(r),Started=r.ReadInt64(),SafeSince=r.ReadInt64(),Sequence=r.ReadInt64(),ActionId=r.ReadInt64(),Army=r.ReadInt64(),Tower=r.ReadInt32(),Goal=WorldWire.ReadPoint(r)})))
            {var c=pair.Value;AiStateWire.Require(pair.Key>0&&c.Started>=0&&c.Started<=tick&&c.SafeSince>=-1&&c.SafeSince<=tick&&c.Sequence>=0&&c.ActionId>=0&&c.Army>=0&&c.Tower>=0&&!m.clients.ContainsKey(pair.Key),"micro client");m.clients.Add(pair.Key,c);}
            foreach(var pair in WorldWire.Array(r,()=>new KeyValuePair<int,long>(r.ReadInt32(),r.ReadInt64())))
            {AiStateWire.Require(pair.Key>0&&pair.Value>=0&&!m.cooldowns.ContainsKey(pair.Key),"micro cooldown");m.cooldowns.Add(pair.Key,pair.Value);}
            foreach(var pair in m.clients)
            {
                var c=pair.Value;
                AiStateWire.Require((c.Sequence==0)==(c.ActionId==0)&&(c.Phase==AiCombatMicroPhase.Repair?c.Tower>0:c.Tower==0),"micro client phase binding");
                if(c.Sequence==0)AiStateWire.Require(m.pending!=null&&m.pending.Action.EntityIds.Contains(pair.Key)&&c.Phase==m.pending.Phase&&c.Army==m.pending.Army&&c.Tower==m.pending.Action.ParentId&&c.Goal.Equals(m.pending.Action.Target),"micro pending client binding");
                else AiStateWire.Require(m.certificates.TryGetValue(pair.Key,out var cert)&&cert.Sequence==c.Sequence&&cert.ActionId==c.ActionId&&cert.Army==c.Army&&cert.Goal.Equals(c.Goal)&&PhaseKind(c.Phase,cert.Kind),"micro applied client binding");
            }
            AiStateWire.Require(m.pending==null||m.pending.Army>=0&&m.pending.Action.Generation==generation&&m.pending.Action.SourceIdentity==PlayableAiOpeningComposition.SourceIdentity&&m.pending.Action.EntityIds.Count>0&&m.pending.Action.EntityIds.Distinct().Count()==m.pending.Action.EntityIds.Count&&PhaseKind(m.pending.Phase,m.pending.Action.Kind)&&m.pending.Action.EntityIds.All(id=>m.clients.TryGetValue(id,out var c)&&c.Sequence==0&&c.Phase==m.pending.Phase),"micro pending recipients");
            return m;
        }
    }
}
