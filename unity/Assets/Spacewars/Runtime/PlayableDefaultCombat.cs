using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private enum DefaultCombatPhase { Idle, Pursuit, Return, Response, Withdrawal, ResponseReturn }
        private sealed class DefaultCombatState
        {
            internal DefaultCombatPhase Phase;
            internal NavPoint Anchor;
            internal double Deadline;
            internal int Engaged;
            internal bool MovementRequested;
            internal NavPoint WithdrawalGoal;
        }
        private void ResetDefaultCombat(Unit u) { u.DefaultCombat=null; }
        private bool HasExplicitCombatWork(Unit u)=>u.CurrentOrder!=null||u.ExplicitTarget||u.HasAttackMove||u.WreckWaiting||
            tacticalQueues.TryGetValue(u.Id,out var q)&&(q.Active!=null||q.Pending!=null||q.Deferred.Count>0)||
            u.Air!=null&&(u.Air.Departing||u.Air.Yielding||u.Air.Deferred.Count>0);
        // Ordinary combat steering can keep the last tactical stamp and Native
        // group after arrival. The active default state owns that movement.
        internal bool CombatExecutionHasDefaultGroundIntent(int id)=>
            units.TryGetValue(id,out var unit)&&unit.Air==null&&unit.DefaultCombat is {Phase: not DefaultCombatPhase.Idle}&&
            !HasExplicitCombatWork(unit);
        private bool DefaultEnemyAlive(int id)=>units.TryGetValue(id,out var unit)&&unit.Health>0||buildings.TryGetValue(id,out var building)&&building.Health>0;
        private double DefaultWeaponRange(Unit u,int target)=>
            ExplorerRocketEquipped(u)?ExplorerRocketRange(AirTarget(target)):
            PlayableUnitRules.Range(profile,u.Kind,u.Kind==PlayableEntityKind.Shkval&&UnitUpgraded(u));
        private double DefaultWeaponRange(Unit u)=>ExplorerRocketEquipped(u)?
            Math.Max(profile.ExplorerRocketGroundRange,profile.ExplorerRocketAirRange):DefaultWeaponRange(u,0);
        private bool DefaultFireClear(Unit u,NavUnit self,int target)
        {
            if(!VisibleTarget(u.Owner,target)||!Target(target,out var point,out _))return false;
            if(AirTarget(target)&&u.Kind!=PlayableEntityKind.Explorer&&u.Kind!=PlayableEntityKind.Hawk)return false;
            if(u.Kind==PlayableEntityKind.Igla)return IglaClear(u,self,target,point);
            if(u.Kind==PlayableEntityKind.Shkval)return true; // The ballistic arc can pass above direct-fire cover.
            if(u.Kind==PlayableEntityKind.Hawk)return HawkLineClear(u,self.Position,u.Air.Height+HawkBodyHeight/2,target);
            if(ExplorerRocketEquipped(u))return SpatialFireClear(u,self.Position,GroundHeight(self.Position)+DirectFireHeight,target,profile.HawkMissileRadius);
            if(AirTarget(target))return SpatialFireClear(u,self.Position,GroundHeight(self.Position)+DirectFireHeight,target,0);
            return GroundTargetFireClear(u,self.Position,point,target);
        }
        private readonly Dictionary<int,int> activeAttackerTargets=new Dictionary<int,int>();
        private readonly HashSet<int> activeAttackVictims=new HashSet<int>();

        private void CollectActiveAttackers()
        {
            // Read the previous firing phase before any unit changes its target this tick.
            activeAttackerTargets.Clear();
            activeAttackVictims.Clear();
            foreach(var unit in units.Values)
            {
                int victim=unit.LastCombatShotTarget;
                if(victim==0||Frozen(unit))continue;
                if(victim!=unit.Target||!DefaultEnemyAlive(victim)){unit.LastCombatShotTarget=0;continue;}
                if(units.ContainsKey(victim)&&!eliminated.Contains(unit.Owner)&&TryActor(unit.Id,out var self)&&
                    Target(victim,out var point,out _)&&Distance(self.Position,point)<=DefaultWeaponRange(unit,victim)&&DefaultFireClear(unit,self,victim))
                    AddAttacker(unit.Id,victim);
            }
            foreach(var building in buildings.Values)
            {
                int victim=building.LastCombatShotTarget;
                if(victim==0)continue;
                if(victim!=building.Weapon?.Target||!DefaultEnemyAlive(victim)){building.LastCombatShotTarget=0;continue;}
                if(units.ContainsKey(victim)&&building.Ready&&building.Sale==null&&!eliminated.Contains(building.Owner)&&
                    !SystemsDisabled(building.Id)&&TurretLegal(building,victim))
                    AddAttacker(building.Id,victim);
            }
        }

        private void AddAttacker(int source,int victim)
        {
            activeAttackerTargets.Add(source,victim);
            activeAttackVictims.Add(victim);
        }

        private int PreferredAttacker(Unit unit,NavUnit self)
        {
            // A direct Attack is an order, not a suggestion to retaliate.
            if(unit.ExplicitTarget)return 0;
            if(ExplorerRocketEquipped(unit))return RocketAutomaticTarget(unit,self);
            int cover=RetreatCoverTarget(unit,self);if(cover!=0)return cover;
            int retained=KeepAutomaticTarget(unit,self)?unit.Target:0;
            if(retained!=0&&units.TryGetValue(retained,out var current)&&CanThreaten(current,unit))return retained;
            if(!activeAttackVictims.Contains(unit.Id))return retained;
            double range=DefaultWeaponRange(unit),nearest=double.PositiveInfinity;
            int best=0;
            targetSpatialIndex.Query(self.Position,range,targetSpatialIndex.Candidates);
            foreach(var candidate in targetSpatialIndex.Candidates)
            {
                int id=candidate.Id;
                if(!activeAttackerTargets.TryGetValue(id,out int victim)||victim!=unit.Id||
                    !Target(id,out var point,out _)||!DefaultFireClear(unit,self,id))continue;
                double distance=Distance(self.Position,point);
                if(distance>range)continue;
                if(unit.BurstRemaining>0&&id==unit.BurstTarget)return id;
                if(distance<nearest||distance==nearest&&id<best){best=id;nearest=distance;}
            }
            return best!=0?best:retained;
        }

        private bool CanThreaten(Unit source,Unit victim)=>
            Spacewars.Simulation.Ai.AiRosterCatalog.Initial.Targets(source.Kind,victim.Kind==PlayableEntityKind.Hawk?"air":"ground");

        private bool KeepAutomaticTarget(Unit unit,NavUnit self)
        {
            if(unit.Target==0||!DefaultEnemyAlive(unit.Target)||!Target(unit.Target,out var point,out _)||
                Distance(self.Position,point)>DefaultWeaponRange(unit,unit.Target)||!DefaultFireClear(unit,self,unit.Target))return false;
            return true;
        }

        private int RetreatCoverTarget(Unit unit,NavUnit self)
        {
            if(!units.TryGetValue(unit.Target,out var target)||!TryActor(target.Id,out var actor)||!actor.Moving||
                !VisibleTarget(unit.Owner,target.Id)||
                (actor.Position.X-self.Position.X)*target.Velocity.X+(actor.Position.Z-self.Position.Z)*target.Velocity.Z<=0)return 0;
            double distance=Distance(self.Position,actor.Position);int best=0;
            foreach(var other in units.Values)
            {
                if(other.Id==target.Id||other.Health<=0||!Hostile(unit.Owner,other.Owner)||
                    !TryActor(other.Id,out var cover)||Distance(self.Position,cover.Position)+profile.Navigation.ArrivalTolerance>=distance||
                    Distance(self.Position,cover.Position)>DefaultWeaponRange(unit)||!DefaultFireClear(unit,self,other.Id))continue;
                // Ground weapons never select an airborne cover recipient.
                if(!CanThreaten(unit,other))continue;
                best=other.Id;distance=Distance(self.Position,cover.Position);
            }
            return best;
        }

        // Attack-move looks beyond weapon range for an approach. The weapon still
        // checks its own range, visibility and line of fire before each shot.
        private const double AttackMoveSearchMultiplier=1.3;
        private void ContinueAttackMove(Unit unit,NavUnit self)
        {
            if(!unit.HasAttackMove||unit.CurrentOrder?.Kind!=PlayableTacticalOrderKind.AttackMove||
                self.Held||unit.Air?.Departing==true||unit.Air?.Yielding==true)return;
            if(unit.Air==null&&!CanResumeCombat(unit.Id))return;

            if(unit.Air==null&&navigation.IsPending(unit.Id))
            {
                if(!self.Moving&&self.Goal.Equals(unit.AttackMove))CombatMove(unit.Id,unit.AttackMove);
                return;
            }
            if(self.Outcome==NavigationOutcome.Arrived&&self.Goal.Equals(unit.AttackMove)&&
                Distance(self.Position,unit.AttackMove)<=profile.Navigation.ArrivalTolerance)return;

            if(unit.Repath>0)
            {
                if(self.Moving&&!self.Goal.Equals(unit.AttackMove))return;
            }
            else
            {
                unit.Repath=profile.AttackRepathSeconds;
                if(TryAttackMoveApproach(unit,self,out var approach))
                {
                    MoveAttackMoveAlong(unit,self,approach);
                    return;
                }
            }
            if(!self.Moving&&unit.Air==null&&ResumeSuspendedAttackMove(unit,self))return;
            bool away=Distance(self.Position,unit.AttackMove)>profile.Navigation.ArrivalTolerance;
            if(away&&(!self.Goal.Equals(unit.AttackMove)||!self.Moving)||!away&&self.Moving&&!self.Goal.Equals(unit.AttackMove))
                MoveAttackMoveAlong(unit,self,unit.AttackMove);
        }

        private bool TryAttackMoveApproach(Unit unit,NavUnit self,out NavPoint result)
        {
            result=default;
            // Measure Cerberus's search buffer from its hull. The exact firing
            // stance still has to pass the per-nozzle engagement checks.
            double hullClearance = unit.Kind == PlayableEntityKind.Cerberus ? self.Radius : 0;
            double searchRadius=DefaultWeaponRange(unit)*AttackMoveSearchMultiplier+hullClearance;
            double nearest=double.PositiveInfinity;
            int bestPriority=int.MaxValue,bestId=0;
            targetSpatialIndex.Query(self.Position,searchRadius,targetSpatialIndex.Candidates);
            foreach(var candidate in targetSpatialIndex.Candidates)
            {
                int id=candidate.Id;
                if(!PersonallyVisible(unit,self,id,out var point)||!ResponseWeaponCompatible(unit,id))continue;
                double distance=Distance(self.Position,point);
                if(distance>DefaultWeaponRange(unit,id)*AttackMoveSearchMultiplier+hullClearance)continue;
                int priority=ExplorerRocketEquipped(unit)&&units.TryGetValue(id,out var foe)&&foe.Kind==PlayableEntityKind.Hawk?0:
                    activeAttackerTargets.TryGetValue(id,out var victim)&&victim==unit.Id?1:2;
                if(priority>bestPriority||priority==bestPriority&&(distance>nearest||distance==nearest&&id>=bestId))continue;
                bool found=unit.Air!=null?HawkApproach(unit,id,out var goal):
                    TryTargetAttackApproach(unit.Id,self.Position,point,id,out goal,
                        candidateGoal=>CanAdmitAttackMoveApproach(unit.Id,candidateGoal));
                if(!found)continue;
                bestPriority=priority;nearest=distance;bestId=id;result=goal;
            }
            return bestId!=0;
        }

        private void MoveAttackMoveAlong(Unit unit,NavUnit self,NavPoint goal)
        {
            if(self.Moving&&Distance(self.Goal,goal)<=profile.Navigation.ArrivalTolerance)return;
            if(unit.Air!=null)
            {
                self.Goal=goal;self.Moving=true;self.Outcome=NavigationOutcome.Moving;unit.Air.Short=false;
            }
            else if (!tacticalQueues.TryGetValue(unit.Id, out var queue) ||
                queue.Active?.TerminalFailure?.Location?.Position.Equals(goal) != true ||
                !TerminalProofMatches(queue.Active, self))
                CombatMove(unit.Id,goal);
        }

        private int RocketAutomaticTarget(Unit unit,NavUnit self)
        {
            if(unit.BurstRemaining>0&&unit.BurstTarget==unit.Target&&KeepAutomaticTarget(unit,self))
                return unit.Target;
            if(!targetSpatialIndex.CombatPhase)RebuildTargetSpatialIndex();
            targetSpatialIndex.Query(self.Position,DefaultWeaponRange(unit),targetSpatialIndex.Candidates,TeamOf(unit.Owner));
            int best=0,bestPriority=int.MaxValue;
            double nearest=double.PositiveInfinity;
            foreach(var candidate in targetSpatialIndex.Candidates)
            {
                int id=candidate.Id;
                if(!Target(id,out var point,out _)||!DefaultFireClear(unit,self,id))continue;
                double distance=Distance(self.Position,point);
                if(distance>DefaultWeaponRange(unit,id))continue;
                int priority=units.TryGetValue(id,out var foe)&&foe.Kind==PlayableEntityKind.Hawk?0:
                    activeAttackerTargets.TryGetValue(id,out var victim)&&victim==unit.Id?1:2;
                if(priority>bestPriority||priority==bestPriority&&(distance>nearest||distance==nearest&&id>=best))continue;
                bestPriority=priority;nearest=distance;best=id;
            }
            return best;
        }

        private int DefaultDefenseTarget(Unit u,NavUnit self)
        {
            if(u.Kind==PlayableEntityKind.Shkval)return ArtilleryTarget(u,self.Position);
            if(ExplorerRocketEquipped(u))return RocketAutomaticTarget(u,self);
            int best=0,bestClass=int.MaxValue;double distance=double.PositiveInfinity,range=DefaultWeaponRange(u);
            // Filter every candidate before ranking: a blocked nearest enemy must not mask a clear shot.
            if(!targetSpatialIndex.CombatPhase)RebuildTargetSpatialIndex();
            targetSpatialIndex.Query(self.Position,range,targetSpatialIndex.Candidates,TeamOf(u.Owner));
            foreach(var candidate in targetSpatialIndex.Candidates)
            {
                int id=candidate.Id;
                if(!Target(id,out var point,out _)||Distance(self.Position,point)>range||!DefaultFireClear(u,self,id))continue;
                int targetClass=candidate.Building?1:0;double d=Distance(self.Position,point);
                if(best==0||targetClass<bestClass||targetClass==bestClass&&(d<distance||d==distance&&id<best))
                {best=id;bestClass=targetClass;distance=d;}
            }
            return best;
        }
        private void RememberDefaultShot(Unit u)
        {
            u.LastCombatShotTarget=u.Target;
            bool completedWithdrawal=u.LastOrder?.Kind==PlayableCommandKind.Move&&u.CurrentOrder==null&&
                TryActor(u.Id,out var actor)&&!actor.Moving&&!HasQueueExecution(u.Id)&&!navigation.IsPending(u.Id);
            if(u.LastOrder?.Origin==PlayableOrderOrigin.Ai&&
                (u.LastOrder.Kind is PlayableCommandKind.Follow or PlayableCommandKind.AttackMove||completedWithdrawal)&&
                units.ContainsKey(u.Target)&&combatExecutionOwners!=null)
                foreach(var owner in combatExecutionOwners())owner.CombatReturnShot(u.LastOrder,Tick,completedWithdrawal);
            if(u.DefaultCombat!=null&&u.DefaultCombat.Phase<DefaultCombatPhase.Return&&!HasExplicitCombatWork(u))u.DefaultCombat.Engaged=u.Target;
        }
        // Runs before the weapon-specific code. It owns only default intent; weapons still own fire/reload/aim.
        private bool PrepareDefaultCombat(Unit u,NavUnit self)
        {
            if(HasExplicitCombatWork(u)) { ResetDefaultCombat(u);return false; }
            if(u.DefaultCombat==null)
            {
                if(!self.Held&&(self.Moving||navigation.IsPending(u.Id)))return false;
                u.DefaultCombat=new DefaultCombatState();
            }
            var state=u.DefaultCombat;
            if(self.Held&&state.Phase!=DefaultCombatPhase.Idle) { ResetDefaultCombat(u);u.DefaultCombat=state=new DefaultCombatState(); }
            if(state.Phase>=DefaultCombatPhase.Response)return PrepareCombatResponse(u,self,state);
            if(state.Phase==DefaultCombatPhase.Pursuit&&
                (elapsed>=state.Deadline-1e-9||!VisibleTarget(u.Owner,u.Target)||!DefaultEnemyAlive(u.Target)))
            {state.Phase=DefaultCombatPhase.Return;state.MovementRequested=false;InterruptBurst(u);u.Target=0;}
            if(state.Phase==DefaultCombatPhase.Return)
            {
                u.Target=0;
                if(!state.MovementRequested)
                {
                    DefaultStop(u,self);
                    state.MovementRequested=true;
                    if(!DefaultMove(u,self,state.Anchor))ResetDefaultCombat(u);
                }
                else if(!self.Moving&&!navigation.IsPending(u.Id))ResetDefaultCombat(u);
                return true; // Complete return before considering another pursuit.
            }
            int attacker=PreferredAttacker(u,self);
            if(attacker!=0)
            {
                if(attacker!=u.Target){InterruptBurst(u);state.Engaged=0;}
                u.Target=attacker;
                if(state.Phase==DefaultCombatPhase.Pursuit)DefaultStop(u,self);
                return true;
            }
            if(state.Phase==DefaultCombatPhase.Idle&&state.Engaged!=0&&u.Target==state.Engaged&&
                !(u.Kind==PlayableEntityKind.Igla&&u.LastOrder?.Origin==PlayableOrderOrigin.Ai)&&
                !self.Held&&profile.DefaultCombatPursuitSeconds>0&&VisibleTarget(u.Owner,u.Target)&&Target(u.Target,out var retreat,out _)&&Distance(self.Position,retreat)>DefaultWeaponRange(u,u.Target))
            {state.Phase=DefaultCombatPhase.Pursuit;state.Anchor=self.Position;state.Deadline=elapsed+profile.DefaultCombatPursuitSeconds;u.Repath=0;}
            if(state.Phase==DefaultCombatPhase.Pursuit)
            {
                if(Target(u.Target,out var target,out _)&&Distance(self.Position,target)>DefaultWeaponRange(u,u.Target))
                {
                    if(u.Repath<=0&&!navigation.IsPending(u.Id))
                    {
                        bool found=u.Kind==PlayableEntityKind.Hawk?HawkApproach(u,u.Target,out var goal):TryAttackApproach(u.Id,self.Position,target,out goal);
                        if(found)DefaultMove(u,self,goal);
                        u.Repath=profile.AttackRepathSeconds;
                    }
                }
                else DefaultStop(u,self);
                return true;
            }
            // Preserve the fixed victim of an already started burst while it remains eligible.
            if(u.BurstRemaining>0&&Target(u.Target,out var burstTarget,out _)&&Distance(self.Position,burstTarget)<=DefaultWeaponRange(u,u.Target)&&DefaultFireClear(u,self,u.Target))return true;
            int next=u.Kind==PlayableEntityKind.Igla&&u.LastOrder?.Origin==PlayableOrderOrigin.Ai&&KeepAutomaticTarget(u,self)
                ?u.Target:DefaultDefenseTarget(u,self);
            if(next!=u.Target){InterruptBurst(u);state.Engaged=0;}
            u.Target=next;
            return true;
        }
        private void DefaultStop(Unit u,NavUnit self)
        {
            if(self.Held||!self.Moving&&!navigation.IsPending(u.Id))return;
            if(u.Air!=null){self.Moving=false;self.Goal=self.Position;self.Outcome=NavigationOutcome.Stopped;u.Air.Short=false;}
            else CombatStop(u.Id);
        }
        private bool DefaultMove(Unit u,NavUnit self,NavPoint goal)
        {
            if(u.Air==null)return CombatMove(u.Id,goal);
            if(!AirInside(goal))return false;
            self.Goal=goal;self.Moving=true;self.Outcome=NavigationOutcome.Moving;u.Air.Short=false;return true;
        }
        private static void WriteDefaultCombatState(BinaryWriter w,DefaultCombatState state)
        {
            w.Write((int)state.Phase);
            WorldWire.Write(w,state.Anchor);
            WorldWire.Number(w,state.Deadline);
            w.Write(state.Engaged);
            w.Write(state.MovementRequested);
            if(state.Phase==DefaultCombatPhase.Withdrawal)WorldWire.Write(w,state.WithdrawalGoal);
        }

        private static DefaultCombatState ReadDefaultCombatState(BinaryReader r,bool responses)
        {
            var phase=(DefaultCombatPhase)r.ReadInt32();
            var anchor=WorldWire.ReadPoint(r);
            double deadline=WorldWire.Number(r);
            int engaged=r.ReadInt32();
            bool requested=WorldWire.Boolean(r);
            if(!Enum.IsDefined(typeof(DefaultCombatPhase),phase)||!responses&&phase>=DefaultCombatPhase.Response||
                deadline<0||engaged<0||requested&&phase!=DefaultCombatPhase.Return&&phase<DefaultCombatPhase.Response)
                throw new ArgumentException("Invalid default combat state.");
            var withdrawalGoal=phase==DefaultCombatPhase.Withdrawal?WorldWire.ReadPoint(r):default;
            return WorldWire.Record(r,()=>new DefaultCombatState
            {
                Phase=phase, Anchor=anchor, Deadline=deadline, Engaged=engaged,
                MovementRequested=requested, WithdrawalGoal=withdrawalGoal
            });
        }

        private void WriteDefaultCombat(BinaryWriter w)
        {
            var rows=units.Values.Where(u=>u.DefaultCombat!=null).ToArray();
            bool response=rows.Any(u=>u.DefaultCombat.Phase>=DefaultCombatPhase.Response);
            int shotCount=units.Values.Count(u=>u.LastCombatShotTarget!=0)+buildings.Values.Count(b=>b.LastCombatShotTarget!=0);
            w.Write(shotCount>0?0x44434633:response?0x44434632:0x44434631);
            w.Write(rows.Length);
            foreach(var u in rows)
            {
                w.Write(u.Id);
                WriteDefaultCombatState(w,u.DefaultCombat);
            }
            if(shotCount==0)return;
            w.Write(shotCount);
            foreach(var u in units.Values)
                if(u.LastCombatShotTarget!=0){w.Write(u.Id);w.Write(u.LastCombatShotTarget);}
            foreach(var b in buildings.Values)
                if(b.LastCombatShotTarget!=0){w.Write(b.Id);w.Write(b.LastCombatShotTarget);}
        }

        private void ReadDefaultCombat(BinaryReader r)
        {
            if(r.BaseStream.Position==r.BaseStream.Length)return;
            int version=r.ReadInt32();
            if(version!=0x44434631&&version!=0x44434632&&version!=0x44434633)throw new ArgumentException("Invalid default combat extension.");
            int count=r.ReadInt32();
            if(count<0||count>units.Count)throw new ArgumentException("Invalid default combat count.");
            for(int i=0;i<count;i++)
            {
                int id=r.ReadInt32();
                if(!units.TryGetValue(id,out var u)||u.DefaultCombat!=null)throw new ArgumentException("Invalid default combat unit.");
                u.DefaultCombat=ReadDefaultCombatState(r,version!=0x44434631);
            }
            if(version!=0x44434633)return;
            int shotCount=r.ReadInt32();
            if(shotCount<=0||shotCount>units.Count+buildings.Count)throw new ArgumentException("Invalid combat shot count.");
            for(int i=0;i<shotCount;i++)
            {
                int shooter=r.ReadInt32(),victim=r.ReadInt32();
                if(victim<=0||victim>=nextId||victim==shooter)throw new ArgumentException("Invalid combat shot victim.");
                if(units.TryGetValue(shooter,out var unit)&&unit.LastCombatShotTarget==0)
                    unit.LastCombatShotTarget=victim;
                else if(buildings.TryGetValue(shooter,out var building)&&TerritoryRules.ArmedDefense(building.Kind)&&building.LastCombatShotTarget==0)
                    building.LastCombatShotTarget=victim;
                else throw new ArgumentException("Invalid combat shot source.");
            }
        }
    }
}
