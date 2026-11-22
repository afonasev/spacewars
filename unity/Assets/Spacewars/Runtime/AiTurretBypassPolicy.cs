using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // One owner-wide ledger shared by the main and flank planners. Queries never
    // spend a battle or renew a departure health budget.
    internal sealed class AiTurretBypassPolicy
    {
        internal sealed class Passage
        {
            internal long Army,Ordinal,Started,HealthTick,Sequence;
            internal double Damage;
            internal int Target;
            internal NavPoint Goal;
            internal bool Dangerous,Finished;
            internal Dictionary<int,double> Health=new Dictionary<int,double>();
            internal Dictionary<int,double> LastHealth=new Dictionary<int,double>();
        }
        internal void Reconcile(AiArmyRegistry registry)
        {
            var live=registry.Capture().Armies.Where(a=>a.Phase!=AiArmyPhase.Disbanded).Select(a=>a.Id).ToHashSet();
            foreach(var id in battles.Keys.Where(id=>!live.Contains(id)).ToArray()){battles.Remove(id);passages.Remove(id);}
        }
        long ordinal;
        readonly Dictionary<long,long> battles=new Dictionary<long,long>();
        readonly Dictionary<long,Passage> passages=new Dictionary<long,Passage>();
        internal void Battle(long army)
        {if(army>0&&!battles.ContainsKey(army))battles.Add(army,++ordinal);}
        internal bool Allowed(AiDifficulty difficulty,long army,AiProfile ai)=>
            AiProfile.UsesExpertPolicy(difficulty)||difficulty==AiDifficulty.Veteran&&battles.TryGetValue(army,out var n)&&n%(long)ai.Value("turretBypass.veteranBattleInterval")==0;
        internal Passage For(long army)=>passages.TryGetValue(army,out var p)?p:null;
        internal void Begin(long army,PlayableAiAction action,PlayableAiObservation o,bool dangerous)
        {
            Battle(army);
            passages[army]=new Passage{Army=army,Ordinal=battles[army],Started=o.Tick,Target=action.TargetId,Goal=action.Target,Dangerous=dangerous,
                HealthTick=o.Tick,Health=o.Entities.Where(u=>action.EntityIds.Contains(u.Id)).ToDictionary(u=>u.Id,Health)};
            passages[army].LastHealth=new Dictionary<int,double>(passages[army].Health);
        }
        internal void CancelUnstarted(long army)
        {if(For(army)?.Sequence==0)passages.Remove(army);}
        internal void ObserveHealth(long army,IEnumerable<PlayableEntitySnapshot> units,long tick)
        {
            var passage=For(army);if(passage==null||passage.Finished||tick<=passage.HealthTick)return;
            var current=units.ToDictionary(u=>u.Id,Health);
            foreach(var start in passage.Health)
            {
                double previous=passage.LastHealth.TryGetValue(start.Key,out var seen)?seen:start.Value;
                double health=current.TryGetValue(start.Key,out var alive)?alive:0;
                passage.Damage+=Math.Max(0,previous-health);passage.LastHealth[start.Key]=health;
            }
            passage.HealthTick=tick;
        }
        internal static bool RemainingGoal(PlayableEntitySnapshot unit,long installedGroup,long installedRevision,NavPoint installedGoal,NavPoint anchor,out NavPoint goal)
        {
            goal=default;
            var active=unit.Queue?.Active;
            if(unit.Queue?.ActiveAiOrderViable==true&&active?.Kind==PlayableCommandKind.Move&&active.Anchor?.Position.Equals(anchor)==true&&active.GroupId==installedGroup&&active.ActivationRevision==installedRevision&&active.CommandSequence==unit.OrderStamp?.Sequence)
            {goal=installedGoal;return true;}
            var done=unit.Completion;
            if(done?.Order.Kind==PlayableCommandKind.Move&&done.OriginalTarget.Equals(anchor)&&done.Order.Sequence==unit.OrderStamp?.Sequence)
            {goal=done.AssignedLocation.Position;return true;}
            return false;
        }
        internal static double Health(PlayableEntitySnapshot u)=>u.ExactHealth??u.Health;
        internal static bool Enabled(AiDifficulty d)=>d==AiDifficulty.Veteran||AiProfile.UsesExpertPolicy(d);
        internal static NavPoint[] Positions(PlayableProfile p,PlayableBuildingSnapshot target,IEnumerable<PlayableEntitySnapshot> actors)
        {
            var combat=actors.Where(u=>u.Health>0&&AiRosterCatalog.Initial.Targets(u.Kind,"ground")).ToArray();
            if(combat.Length==0)return Array.Empty<NavPoint>();
            // Stable common ring, independent of individual unit origins and IDs.
            double range=combat.Min(u=>PlayableUnitRules.Range(p,u.Kind,u.Upgraded));
            // Reserve the existing square formation's approximate half-width.
            // Actual allocated slots are still certified again before delivery.
            double clearance=TerritoryRules.Radius(p,target.Kind)+combat.Max(u=>PlayableUnitRules.Radius(p,u.Kind))+p.Navigation.ArrivalTolerance;
            double ring=Math.Max(clearance,range*p.AttackApproachRangeRatio-p.Navigation.ArrivalSlotSpacing*Math.Ceiling(Math.Sqrt(combat.Length))/2);
            if(ring>range)return Array.Empty<NavPoint>();
            double outer=range*p.AttackApproachRangeRatio;
            return new[]{outer,(outer+ring)/2,ring}.Where(r=>r>=clearance&&r<=range).Distinct()
                .SelectMany(r=>PlayableUnitRules.AttackApproachCandidates(p,new NavPoint(target.Position.X+range,target.Position.Z),target.Position,r/p.AttackApproachRangeRatio)).ToArray();
        }
        internal static IEnumerable<PlayableEntitySnapshot> Enemies(PlayableAiObservation o,AiKnowledgeState knowledge)
        {
            var enemies=o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0).ToList();
            foreach(var c in knowledge?.Contacts??Array.Empty<AiKnownContact>())
                if(!c.Building&&c.HealthAtLastSeen>0&&c.Confidence>0&&o.IsHostile(c.Owner)&&Enum.IsDefined(typeof(PlayableEntityKind),c.Kind)&&!enemies.Any(u=>u.Id==c.Id))
                    enemies.Add(new PlayableEntitySnapshot(c.Id,c.Owner,(PlayableEntityKind)c.Kind,c.Position,c.HealthAtLastSeen,false,0,0,0,upgraded:true,weaponSystemsLevel:3,defensiveSystemsLevel:3,rocketLauncher:(PlayableEntityKind)c.Kind==PlayableEntityKind.Explorer));
            return enemies;
        }
        static double Modifier(PlayableProfile p,int level)=>level<=0?1:1+PlayableResearchRules.Bonus(p,level>=3?PlayableResearchKind.UltimateWeaponSystems:level==2?PlayableResearchKind.AdvancedWeaponSystems:PlayableResearchKind.ImprovedWeaponSystems)/100;
        static (double Dps,double Volley) Weapon(PlayableProfile p,PlayableEntitySnapshot u,bool hostile=false)
        {
            double modifier=Modifier(p,u.WeaponSystemsLevel);
            int burst=u.Kind==PlayableEntityKind.Explorer?(u.Upgraded?p.ExplorerAssaultBurstSize:p.ExplorerBurstSize):u.Kind==PlayableEntityKind.Hawk?2:1;
            double raw=u.Kind==PlayableEntityKind.Tank?p.TankWeaponDamage:u.Kind==PlayableEntityKind.Explorer?p.ExplorerDamage:u.Kind==PlayableEntityKind.Hawk?p.HawkDamage:u.Kind==PlayableEntityKind.Shkval?(hostile?Math.Max(p.ShkvalDirectHitDamage,p.ShkvalDamage):p.ShkvalDirectHitDamage):0;
            double cycle=u.Kind==PlayableEntityKind.Tank?p.TankWeaponReloadSeconds:u.Kind==PlayableEntityKind.Explorer?(p.ExplorerBurstPauseMs+(burst-1)*p.ExplorerBurstShotIntervalMs)/1000d:u.Kind==PlayableEntityKind.Hawk?PlayableUnitRules.HawkSalvoCycle(p,u.Upgraded):u.Kind==PlayableEntityKind.Shkval?PlayableUnitRules.ShkvalFireInterval(p,u.Upgraded):1;
            double volley=burst*PlayableStatRounding.Damage(p,raw*modifier),dps=volley/cycle;
            if(u.RocketLauncher&&p.ExplorerRocketDamage>0)
            {
                double shot=PlayableStatRounding.Damage(p,p.ExplorerRocketDamage*modifier);
                double ground=PlayableUnitRules.ExplorerRocketDamagePerSecond(p,false),air=PlayableUnitRules.ExplorerRocketDamagePerSecond(p,true);
                dps+=(hostile?Math.Max(ground,air):Math.Min(ground,air))*shot/p.ExplorerRocketDamage;volley+=2*shot;
            }
            return (dps,volley);
        }
        static double Power(PlayableProfile p,PlayableEntitySnapshot u,double health,bool hostile=false)=>Weapon(p,u,hostile).Dps*Math.Min(1,health/Math.Max(1,PlayableUnitRules.MaximumHealth(p,u)));
        internal static bool UsefulShot(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot u,PlayableBuildingSnapshot b,NavPoint at)
        {
            if(!AiRosterCatalog.Initial.Targets(u.Kind,"ground")||AiOpponentSelector.Distance(at,b.Position)>PlayableUnitRules.Range(p,u.Kind,u.Upgraded))return false;
            var target=new PlayableEntitySnapshot(b.Id,b.Owner,PlayableEntityKind.Tank,b.Position,b.Health,false,0,0,0);
            return AiCombatMicroSafety.FireClear(o,p,u,target,at);
        }
        // A conservative time envelope includes turns and a ready volley. Each
        // source spends its fire capacity once across the group; per-unit worst
        // cases separately ensure that concentrated fire cannot kill a member.
#if UNITY_INCLUDE_TESTS
        internal static Action<long,string> RejectedForTests;
#endif
        static bool Reject(long tick,string reason)
        {
#if UNITY_INCLUDE_TESTS
            RejectedForTests?.Invoke(tick,reason);
#endif
            return false;
        }
        internal static bool Assess(PlayableAiObservation o,PlayableProfile p,AiProfile ai,PlayableEntitySnapshot[] units,int targetId,
            IReadOnlyDictionary<int,NavPoint[]> paths,AiKnowledgeState knowledge,IReadOnlyDictionary<string,long> readyMemo,
            out double damage,out bool dangerous,Passage departure=null)
        {
            damage=0;dangerous=false;
            var target=o.Buildings.FirstOrDefault(b=>b.Id==targetId&&b.Health>0&&o.IsHostile(b.Owner));
            if(target==null||units.Length==0||units.Any(u=>u.Owner!=o.Owner||u.Held||u.Departing||u.OrderStamp?.Origin==PlayableOrderOrigin.Human)||units.Any(u=>!paths.TryGetValue(u.Id,out var path)||path.Length==0))return false;
            var towers=AiActiveStrategyPolicy.KnownTowers(o,knowledge,readyMemo);
            var enemies=Enemies(o,knowledge).ToArray();
            var worst=units.ToDictionary(u=>u.Id,_=>0d);double totalSeconds=0,forecast=0;
            var samples=new Dictionary<int,List<(NavPoint Point,double Time,double Duration)>>();
            foreach(var u in units)
            {
                var path=paths[u.Id];var at=u.Position;double time=0,heading=u.HullHeading;
                var points=new List<(NavPoint Point,double Time,double Duration)>();
                foreach(var end in path)
                {
                    double length=AiOpponentSelector.Distance(at,end);if(length==0)continue;
                    double bearing=Math.Atan2(end.Z-at.Z,end.X-at.X);
                    double turn=Math.Abs(Math.IEEERemainder(bearing-heading,2*Math.PI))/Math.Max(.000001,PlayableUnitRules.Turn(p,u.Kind));
                    points.Add((at,time,turn));time+=turn;
                    int steps=Math.Max(1,(int)Math.Ceiling(length/PlayableUnitRules.Radius(p,u.Kind)));
                    double duration=length/Math.Max(.000001,PlayableUnitRules.Speed(p,u.Kind,u.Upgraded))/steps;
                    for(int i=0;i<steps;i++)points.Add((new NavPoint(at.X+(end.X-at.X)*(i+.5)/steps,at.Z+(end.Z-at.Z)*(i+.5)/steps),time+i*duration,duration));
                    time+=duration*steps;heading=bearing;at=end;
                }
                if(towers.Any(t=>AiPositionDevelopmentPlanner.Covers(o,p,t.Position,t.Kind,at,PlayableUnitRules.Aircraft(u.Kind),t.Id,u.Id)))return Reject(o.Tick,$"covered endpoint {u.Id} {at.X},{at.Z}");
                samples[u.Id]=points;totalSeconds=Math.Max(totalSeconds,time);
            }
            var shooters=units.Where(u=>UsefulShot(o,p,u,target,paths[u.Id].Last())).ToArray();
            if(shooters.Length<Math.Min(units.Length,(int)ai.Value("armies.minimumUnits")))return Reject(o.Tick,$"insufficient shooters {shooters.Length}/{units.Length}; target={target.Position.X},{target.Position.Z}; goals="+string.Join(";",units.Select(u=>$"{u.Id}:{paths[u.Id].Last().X},{paths[u.Id].Last().Z}")));
            void Incoming(double dps,double volley,Func<PlayableEntitySnapshot,NavPoint,double,bool> exposed,bool penetrates=false)
            {
                double group=0,first=double.PositiveInfinity,last=0;
                foreach(var u in units)
                {
                    var hit=samples[u.Id].Where(s=>exposed(u,s.Point,s.Time+s.Duration)).ToArray();
                    if(hit.Length==0)continue;
                    // Retain gaps inside the envelope: a ready weapon may reacquire.
                    double seconds=hit.Last().Time+hit.Last().Duration-hit.First().Time;
                    double loss=volley+dps*seconds;worst[u.Id]+=loss;
                    group+=loss;first=Math.Min(first,hit.First().Time);last=Math.Max(last,hit.Last().Time+hit.Last().Duration);
                }
                forecast+=penetrates?group:double.IsPositiveInfinity(first)?0:volley+dps*(last-first);
            }
            foreach(var tower in towers)
            {
                bool gauss=tower.Kind==PlayableBuildingKind.GaussTower;
                double cycle=gauss?p.GaussReloadSeconds:(p.TurretBurstPauseMs+(p.TurretBurstSize-1)*p.TurretBurstShotIntervalMs)/1000d;
                double volley=PlayableStatRounding.Damage(p,(gauss?p.GaussDamage:p.TurretDamage)*Modifier(p,3))*(gauss?1:p.TurretBurstSize);
                Incoming(volley/cycle,volley,(u,at,time)=>AiPositionDevelopmentPlanner.Covers(o,p,tower.Position,tower.Kind,at,PlayableUnitRules.Aircraft(u.Kind),tower.Id,u.Id),gauss);
            }
            foreach(var enemy in enemies.Where(e=>units.Any(u=>AiRosterCatalog.Initial.Targets(e.Kind,PlayableUnitRules.Aircraft(u.Kind)?"air":"ground"))))
            {
                var weapon=Weapon(p,enemy,true);
                Incoming(weapon.Dps,weapon.Volley,(u,at,time)=>AiRosterCatalog.Initial.Targets(enemy.Kind,PlayableUnitRules.Aircraft(u.Kind)?"air":"ground")&&AiOpponentSelector.Distance(enemy.Position,at)<=PlayableUnitRules.Range(p,enemy.Kind,enemy.Upgraded)+PlayableUnitRules.Speed(p,enemy.Kind,enemy.Upgraded)*time+PlayableUnitRules.Radius(p,u.Kind),enemy.Kind==PlayableEntityKind.Shkval);
            }
            damage=forecast;dangerous=damage>0;
            double original=departure==null?units.Sum(Health):departure.Health.Values.Sum();
            if(departure!=null&&(departure.Started>o.Tick||departure.HealthTick>o.Tick||units.Any(u=>!departure.Health.ContainsKey(u.Id))))return false;
            double spent=departure==null?0:departure.Damage+departure.Health.Sum(x=>Math.Max(0,(departure.LastHealth.TryGetValue(x.Key,out var last)?last:x.Value)-units.Where(u=>u.Id==x.Key).Select(Health).DefaultIfEmpty(0).Single()));
            if(spent+damage>original*ai.Value("turretBypass.maximumHealthLossShare")||units.Any(u=>worst[u.Id]>=Health(u)))return Reject(o.Tick,$"health spent={spent} forecast={damage} original={original}; worst="+string.Join(",",units.Select(u=>$"{u.Id}:{worst[u.Id]}/{Health(u)}")));
            var defenders=enemies.Where(e=>units.Any(u=>AiOpponentSelector.Distance(e.Position,paths[u.Id].Last())<=p.TankVisionRange+PlayableUnitRules.Speed(p,e.Kind,e.Upgraded)*totalSeconds)).ToArray();
            if(defenders.Any(e=>units.Any(u=>AiRosterCatalog.Initial.Targets(e.Kind,PlayableUnitRules.Aircraft(u.Kind)?"air":"ground"))&&!shooters.Any(u=>AiRosterCatalog.Initial.Targets(u.Kind,PlayableUnitRules.Aircraft(e.Kind)?"air":"ground"))))return false;
            double ours=shooters.Where(u=>defenders.Length==0||defenders.Any(e=>AiRosterCatalog.Initial.Targets(u.Kind,PlayableUnitRules.Aircraft(e.Kind)?"air":"ground"))).Sum(u=>Power(p,u,Math.Max(0,Health(u)-worst[u.Id])));
            double theirs=defenders.Where(e=>units.Any(u=>AiRosterCatalog.Initial.Targets(e.Kind,PlayableUnitRules.Aircraft(u.Kind)?"air":"ground"))).Sum(e=>Power(p,e,Health(e),true));
            return ours>=theirs*ai.Value("turretBypass.minimumForceRatio")||Reject(o.Tick,$"force ours={ours} defenders={theirs}");
        }
        internal PlayableAiAction Propose(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiArmyState army,long actionId,AiKnowledgeState knowledge,IReadOnlyDictionary<string,long> memo)
        {
            if(!Enabled(o.Difficulty)||For(army.Id)!=null)return null;
            var units=o.Entities.Where(u=>army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&u.Owner==o.Owner&&u.Health>0).OrderBy(u=>u.Id).ToArray();
            if(units.Length<army.MinimumViableForce)return null;
            var options=new List<(NavPoint Goal,int Target,double Damage,bool Dangerous)>();
            foreach(var group in o.RouteProofs.Where(r=>r.Kind==PlayableRouteTargetKind.EnemyApproach&&units.Any(u=>u.Id==r.UnitId)).GroupBy(r=>(r.TargetId,Anchor:r.Target)))
            {
                var paths=group.GroupBy(r=>r.UnitId).ToDictionary(g=>g.Key,g=>g.First().Path.ToArray());
                if(paths.Count!=units.Length||group.Any(r=>r.Owner!=o.Owner||r.Generation!=o.Generation||r.Tick!=o.Tick||r.GeometryRevision<=0||!r.Path.Last().Equals(r.Goal)))continue;
                if(!Assess(o,p,ai,units,group.Key.TargetId,paths,knowledge,memo,out var damage,out var dangerous)||dangerous&&!Allowed(o.Difficulty,army.Id,ai))continue;
                // An approach is useful only against a locally fortified target.
                var target=o.Buildings.FirstOrDefault(b=>b.Id==group.Key.TargetId);
                if(target==null||!AiActiveStrategyPolicy.KnownTowers(o,knowledge,memo).Any(t=>AiOpponentSelector.Distance(t.Position,target.Position)<=AiPositionDevelopmentPlanner.Range(p,t.Kind)+PlayableUnitRules.Range(p,units[0].Kind,units[0].Upgraded)))continue;
                if(units.All(u=>UsefulShot(o,p,u,target,u.Position)&&AiActiveStrategyPolicy.KnownTowers(o,knowledge,memo).All(t=>!AiPositionDevelopmentPlanner.Covers(o,p,t.Position,t.Kind,u.Position,PlayableUnitRules.Aircraft(u.Kind),t.Id,u.Id))))continue;
                options.Add((group.Key.Anchor,group.Key.TargetId,damage,dangerous));
            }
            var choice=options.OrderBy(x=>x.Dangerous).ThenBy(x=>x.Damage).ThenBy(x=>units.Max(u=>AiOpponentSelector.Distance(u.Position,x.Goal))).ThenBy(x=>x.Target).ThenBy(x=>x.Goal.X).ThenBy(x=>x.Goal.Z).FirstOrDefault();
            return options.Count==0?null:new PlayableAiAction(actionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Move,units.Select(u=>u.Id).ToArray(),choice.Goal,targetId:choice.Target,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
        internal void Write(BinaryWriter w)
        {
            w.Write(ordinal);WorldWire.Array(w,battles.OrderBy(x=>x.Key).ToArray(),x=>{w.Write(x.Key);w.Write(x.Value);});
            WorldWire.Array(w,passages.Values.OrderBy(x=>x.Army).ToArray(),x=>{w.Write(x.Army);w.Write(x.Ordinal);w.Write(x.Started);w.Write(x.Target);WorldWire.Number(w,x.Goal.X);WorldWire.Number(w,x.Goal.Z);w.Write(x.Dangerous);w.Write(x.Finished);w.Write(x.HealthTick);w.Write(x.Sequence);WorldWire.Number(w,x.Damage);WorldWire.Array(w,x.Health.OrderBy(h=>h.Key).ToArray(),h=>{w.Write(h.Key);WorldWire.Number(w,h.Value);WorldWire.Number(w,x.LastHealth.TryGetValue(h.Key,out var last)?last:h.Value);});});
        }
        internal void Read(BinaryReader r,long tick)
        {
            ordinal=r.ReadInt64();battles.Clear();passages.Clear();
            foreach(var x in WorldWire.Array(r,()=>new KeyValuePair<long,long>(r.ReadInt64(),r.ReadInt64())))battles.Add(x.Key,x.Value);
            foreach(var x in WorldWire.Array(r,()=>{var p=new Passage{Army=r.ReadInt64(),Ordinal=r.ReadInt64(),Started=r.ReadInt64(),Target=r.ReadInt32(),Goal=new NavPoint(WorldWire.Number(r),WorldWire.Number(r)),Dangerous=WorldWire.Boolean(r),Finished=WorldWire.Boolean(r),HealthTick=r.ReadInt64(),Sequence=r.ReadInt64(),Damage=WorldWire.Number(r)};foreach(var h in WorldWire.Array(r,()=> (Id:r.ReadInt32(),Health:WorldWire.Number(r),Last:WorldWire.Number(r)))){p.Health.Add(h.Id,h.Health);p.LastHealth.Add(h.Id,h.Last);}return p;}))passages.Add(x.Army,x);
            AiStateWire.Require(ordinal>=0&&battles.All(x=>x.Key>0&&x.Value>0&&x.Value<=ordinal)&&battles.Values.Distinct().Count()==battles.Count&&passages.Values.All(x=>battles.TryGetValue(x.Army,out var n)&&n==x.Ordinal&&x.Started>=0&&x.Started<=tick&&x.HealthTick>=x.Started&&x.HealthTick<=tick&&x.Sequence>=0&&x.Damage>=0&&x.LastHealth.Values.All(h=>h>=0)&&x.Target>0&&x.Health.Count>0&&x.Health.All(h=>h.Key>0&&h.Value>0)),"turret bypass ledger");
        }
    }
}
