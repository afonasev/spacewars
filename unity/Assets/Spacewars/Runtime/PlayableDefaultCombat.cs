using System;
using System.IO;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private enum DefaultCombatPhase { Idle, Pursuit, Return }
        private sealed class DefaultCombatState
        {
            internal DefaultCombatPhase Phase;
            internal NavPoint Anchor;
            internal double Deadline;
            internal int Engaged;
            internal bool ReturnRequested;
        }
        private void ResetDefaultCombat(Unit u) { u.DefaultCombat=null; }
        private bool HasExplicitCombatWork(Unit u)=>u.CurrentOrder!=null||u.ExplicitTarget||u.HasAttackMove||u.WreckWaiting||
            tacticalQueues.TryGetValue(u.Id,out var q)&&(q.Active!=null||q.Pending!=null||q.Deferred.Count>0)||
            u.Air!=null&&(u.Air.Departing||u.Air.Yielding||u.Air.Deferred.Count>0);
        private bool DefaultEnemyAlive(int id)=>units.TryGetValue(id,out var unit)&&unit.Health>0||buildings.TryGetValue(id,out var building)&&building.Health>0;
        private double DefaultWeaponRange(Unit u)=>PlayableUnitRules.Range(profile,u.Kind,u.Kind==PlayableEntityKind.Shkval&&UnitUpgraded(u));
        private bool DefaultFireClear(Unit u,NavUnit self,int target)
        {
            if(!VisibleTarget(u.Owner,target)||!Target(target,out var point,out _))return false;
            if(AirTarget(target)&&u.Kind!=PlayableEntityKind.Explorer&&u.Kind!=PlayableEntityKind.Hawk)return false;
            if(u.Kind==PlayableEntityKind.Shkval)return true; // The ballistic arc can pass above direct-fire cover.
            if(u.Kind==PlayableEntityKind.Hawk)return HawkLineClear(u,self.Position,u.Air.Height+HawkBodyHeight/2,target);
            if(AirTarget(target))return SpatialFireClear(u,self.Position,GroundHeight(self.Position)+DirectFireHeight,target,0);
            return GroundFireClear(self.Position,point,u.Kind==PlayableEntityKind.Explorer?0:profile.TankProjectileCollisionRadius);
        }
        private int DefaultDefenseTarget(Unit u,NavUnit self)
        {
            if(u.Kind==PlayableEntityKind.Shkval)return ArtilleryTarget(u,self.Position);
            int best=0,bestClass=int.MaxValue;double distance=double.PositiveInfinity,range=DefaultWeaponRange(u);
            // Filter every candidate before ranking: a blocked nearest enemy must not mask a clear shot.
            if(!targetSpatialIndex.CombatPhase)RebuildTargetSpatialIndex();
            targetSpatialIndex.Query(self.Position,range,targetSpatialIndex.Candidates);
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
            if(u.DefaultCombat!=null&&u.DefaultCombat.Phase!=DefaultCombatPhase.Return&&!HasExplicitCombatWork(u))u.DefaultCombat.Engaged=u.Target;
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
            if(state.Phase==DefaultCombatPhase.Pursuit&&
                (elapsed>=state.Deadline-1e-9||!VisibleTarget(u.Owner,u.Target)||!DefaultEnemyAlive(u.Target)))
            {state.Phase=DefaultCombatPhase.Return;state.ReturnRequested=false;InterruptBurst(u);u.Target=0;}
            if(state.Phase==DefaultCombatPhase.Return)
            {
                u.Target=0;
                if(!state.ReturnRequested)
                {
                    DefaultStop(u,self);
                    state.ReturnRequested=true;
                    if(!DefaultMove(u,self,state.Anchor))ResetDefaultCombat(u);
                }
                else if(!self.Moving&&!navigation.IsPending(u.Id))ResetDefaultCombat(u);
                return true; // Complete return before considering another pursuit.
            }
            if(state.Phase==DefaultCombatPhase.Idle&&state.Engaged!=0&&u.Target==state.Engaged&&
                !self.Held&&profile.DefaultCombatPursuitSeconds>0&&VisibleTarget(u.Owner,u.Target)&&Target(u.Target,out var retreat,out _)&&Distance(self.Position,retreat)>DefaultWeaponRange(u))
            {state.Phase=DefaultCombatPhase.Pursuit;state.Anchor=self.Position;state.Deadline=elapsed+profile.DefaultCombatPursuitSeconds;u.Repath=0;}
            if(state.Phase==DefaultCombatPhase.Pursuit)
            {
                if(Target(u.Target,out var target,out _)&&Distance(self.Position,target)>DefaultWeaponRange(u))
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
            if(u.BurstRemaining>0&&Target(u.Target,out var burstTarget,out _)&&Distance(self.Position,burstTarget)<=DefaultWeaponRange(u)&&DefaultFireClear(u,self,u.Target))return true;
            int next=DefaultDefenseTarget(u,self);
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
        private void WriteDefaultCombat(BinaryWriter w)
        {
            w.Write(0x44434631);var rows=units.Values.Where(u=>u.DefaultCombat!=null).ToArray();w.Write(rows.Length);
            foreach(var u in rows){var s=u.DefaultCombat;w.Write(u.Id);w.Write((int)s.Phase);WorldWire.Write(w,s.Anchor);WorldWire.Number(w,s.Deadline);w.Write(s.Engaged);w.Write(s.ReturnRequested);}
        }
        private void ReadDefaultCombat(BinaryReader r)
        {
            if(r.BaseStream.Position==r.BaseStream.Length)return;
            if(r.ReadInt32()!=0x44434631)throw new ArgumentException("Invalid default combat extension.");
            int count=r.ReadInt32();if(count<0||count>units.Count)throw new ArgumentException("Invalid default combat count.");
            for(int i=0;i<count;i++)
            {
                int id=r.ReadInt32();if(!units.TryGetValue(id,out var u)||u.DefaultCombat!=null)throw new ArgumentException("Invalid default combat unit.");
                var phase=(DefaultCombatPhase)r.ReadInt32();var anchor=WorldWire.ReadPoint(r);double deadline=WorldWire.Number(r);int engaged=r.ReadInt32();bool requested=WorldWire.Boolean(r);
                if(!Enum.IsDefined(typeof(DefaultCombatPhase),phase)||deadline<0||engaged<0||requested&&phase!=DefaultCombatPhase.Return)throw new ArgumentException("Invalid default combat state.");
                u.DefaultCombat=WorldWire.Record(r,()=>new DefaultCombatState{Phase=phase,Anchor=anchor,Deadline=deadline,Engaged=engaged,ReturnRequested=requested});
            }
        }
    }
}
