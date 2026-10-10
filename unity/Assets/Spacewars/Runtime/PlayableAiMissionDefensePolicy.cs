using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Diagnostic release@8 combat path. These are frozen source-profile values, not native balance settings.
    [Serializable]
    public sealed class PlayableAiCombatMission
    {
        private readonly int[] assigned;
        public PlayableAiCombatMission(long generation,long startedTick,long progressTick,long reinforcementTick,
            int targetId,int publicSiteId,NavPoint target,bool visibleTarget,bool deployed,bool completed,string reason,
            IEnumerable<int> assignedIds,string ownerId="player-1",string sourceIdentity=null)
        {
            OwnerId=ownerId;SourceIdentity=sourceIdentity??PlayableAiOpeningComposition.SourceIdentity;
            Generation=generation;StartedTick=startedTick;ProgressTick=progressTick;ReinforcementTick=reinforcementTick;
            TargetId=targetId;PublicSiteId=publicSiteId;Target=target;VisibleTarget=visibleTarget;Deployed=deployed;
            Completed=completed;Reason=reason??"";assigned=(assignedIds??Enumerable.Empty<int>()).Distinct().OrderBy(x=>x).ToArray();
        }
        public string OwnerId{get;} public string SourceIdentity{get;}
        public long Generation{get;} public long StartedTick{get;} public long ProgressTick{get;} public long ReinforcementTick{get;}
        public int TargetId{get;} public int PublicSiteId{get;} public NavPoint Target{get;} public bool VisibleTarget{get;}
        public bool Deployed{get;} public bool Completed{get;} public string Reason{get;}
        public IReadOnlyList<int> AssignedIds=>Array.AsReadOnly(assigned);
        public PlayableAiCombatMission Copy()=>new PlayableAiCombatMission(Generation,StartedTick,ProgressTick,ReinforcementTick,TargetId,PublicSiteId,Target,VisibleTarget,Deployed,Completed,Reason,assigned,OwnerId,SourceIdentity);
        internal PlayableAiCombatMission With(long? progress=null,long? reinforcement=null,bool? deployed=null,bool? completed=null,string reason=null,IEnumerable<int> ids=null)
            =>new PlayableAiCombatMission(Generation,StartedTick,progress??ProgressTick,reinforcement??ReinforcementTick,TargetId,PublicSiteId,Target,VisibleTarget,deployed??Deployed,completed??Completed,reason??Reason,ids??assigned,OwnerId,SourceIdentity);
    }

    // Called by an owner loop or a diagnostic controller with owner-safe observations and terminal receipts.
    // The policy never inspects PlayableDomain.
    public sealed partial class PlayableAiMissionDefensePolicy
    {
        internal PlayableAiMissionDefensePolicy Fork() { var copy=(PlayableAiMissionDefensePolicy)MemberwiseClone();copy.mission=mission?.Copy();copy.pendingAdded=pendingAdded.ToArray();return copy; }

        private readonly string OwnerId;
        private const double EmergencyRadius=30; // ai/release.json tactics.emergencyRadius
        private const double EmergencyThreatRatio=.75; // ai/release.json midgame.emergencyThreatRatio
        private const long ReinforcementTicks=120; // ai/release.json tactics.reinforcementIntervalSec * fixed 30 Hz
        private const long StallTicks=1800; // ai/release.json liveness.missionStallSec * fixed 30 Hz
        private PlayableProfile nativeProfile;
        internal void Rebind(PlayableProfile next){nativeProfile=next;}
        private long nextActionId,pendingActionId,pendingGeneration;
        private int[] pendingAdded=Array.Empty<int>();
        private bool pendingDefense;
        private long preemptedActionId;
        private double previousDistance=Double.PositiveInfinity;
        private PlayableAiCombatMission mission;

        public PlayableAiMissionDefensePolicy(PlayableProfile nativeProfile,long firstActionId=1,string ownerId="player-1")
        {
            this.nativeProfile=nativeProfile??throw new ArgumentNullException(nameof(nativeProfile));OwnerId=ownerId;
            if(firstActionId<1)throw new ArgumentOutOfRangeException(nameof(firstActionId));nextActionId=firstActionId;
        }
        public PlayableAiCombatMission Mission=>mission?.Copy();
        internal bool IsDefenseCandidate=>pendingDefense;
        public bool HasPendingAction=>pendingActionId!=0;
        public long PendingActionId=>pendingActionId;
        public long PreemptedActionId=>preemptedActionId;
        public void Restore(PlayableAiCombatMission snapshot)
        {
            if(snapshot!=null&&(snapshot.OwnerId!=OwnerId||snapshot.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity))
                throw new ArgumentException("Mission owner/source identity does not match the native diagnostic policy.",nameof(snapshot));
            mission=snapshot?.Copy();pendingActionId=0;pendingGeneration=0;pendingAdded=Array.Empty<int>();pendingDefense=false;preemptedActionId=0;
            previousDistance=Double.PositiveInfinity;
        }
        public PlayableAiAction TryPlan(PlayableAiObservation observation,PlayableAiOpeningCompositionState opening,bool defenseOnly=false)
        {
            if(observation==null||opening==null)throw new ArgumentNullException(observation==null?nameof(observation):nameof(opening));
            if(observation.OwnerId!=OwnerId||opening.OwnerId!=OwnerId||opening.MatchSeed!=observation.Seed||opening.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity)
                throw new ArgumentException("Frozen owner/opening provenance does not match this observation.",nameof(opening));
            preemptedActionId=0;
            if(mission!=null&&mission.Generation!=observation.Generation){mission=null;ClearPending();previousDistance=Double.PositiveInfinity;}
            if(pendingActionId!=0&&pendingGeneration!=observation.Generation)ClearPending();
            Reconcile(observation);
            var centers=observation.Buildings.Where(x=>x.Owner==observation.Owner&&(x.Kind==PlayableBuildingKind.Headquarters||x.Kind==PlayableBuildingKind.Outpost)&&x.Health>0).OrderBy(x=>x.Id).ToArray();
            var enemy=VisibleEnemies(observation).Where(x=>centers.Any(c=>Distance(x.Position,c.Position)<=EmergencyRadius)).OrderBy(x=>x.Id).FirstOrDefault();
            var emergency=enemy!=null&&Force(VisibleEnemies(observation))/Math.Max(.1,Force(OwnFighters(observation)))>=EmergencyThreatRatio;
            if(emergency)
            {
                if(mission!=null&&!mission.Completed)mission=mission.With(completed:true,reason:"emergency-preempted");
                // The caller cancels any older scheduled offensive action before scheduling this defense action.
                var defenders=OwnFighters(observation).Select(x=>x.Id).ToArray();
                if(defenders.Length==0||pendingDefense)return null;
                if(pendingActionId!=0){preemptedActionId=pendingActionId;ClearPending();}
                pendingDefense=true;return Action(observation,PlayableCommandKind.Attack,defenders,targetId:enemy.Id);
            }
            if(defenseOnly||pendingActionId!=0||mission?.Completed==true)return null;
            if(mission==null)
            {
                var wave=Wave(opening.Opening);
                if(wave==0||opening.Phase!=PlayableAiOpeningPhase.Active)return null;
                var fighters=OwnFighters(observation).OrderBy(x=>x.Id).ToArray();
                if(fighters.Length<wave)return null;
                if(opening.Opening==PlayableAiOpening.BlindRush&&!fighters.Any(x=>x.Kind==PlayableEntityKind.Tank))return null;
                if((opening.Opening==PlayableAiOpening.ExplorerAllIn||opening.Opening==PlayableAiOpening.DoubleMineExplorerRush)&&fighters.Count(x=>x.Kind==PlayableEntityKind.Explorer)<wave)return null;
                if(opening.Opening==PlayableAiOpening.DoubleMineExplorerRush&&observation.Buildings.Count(x=>x.Owner==observation.Owner&&x.Kind==PlayableBuildingKind.Mine)<2)return null;
                var target=SelectTarget(observation);
                if(target==null)return null;
                var assigned=fighters.Take(wave).Select(x=>x.Id).ToArray();
                mission=new PlayableAiCombatMission(observation.Generation,observation.Tick,observation.Tick,observation.Tick,
                    target.TargetId,target.PublicSiteId,target.Point,target.Visible,false,false,"",assigned,OwnerId);
                previousDistance=DistanceToTarget(fighters.Take(wave),target.Point);
            }
            if(!mission.Deployed)return MissionAction(observation,mission.AssignedIds.ToArray());
            if(observation.Tick-mission.ReinforcementTick<ReinforcementTicks)return null;
            var assignedIds=new HashSet<int>(mission.AssignedIds);
            var additions=OwnFighters(observation).Where(x=>!assignedIds.Contains(x.Id)).OrderBy(x=>x.Id).ToArray();
            if(additions.Length==0)return null;
            pendingAdded=additions.Select(x=>x.Id).ToArray();
            return MissionAction(observation,pendingAdded);
        }
        public void ObserveReceipt(PlayableAiTraceRecord record)
        {
            if(record==null||!AiStateWire.CallbackGenerationMatches(record,pendingGeneration)||record.OwnerId!=OwnerId||record.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity||pendingActionId==0||record.ActionId!=pendingActionId||record.Status==PlayableAiDeliveryStatus.Scheduled||record.Status==PlayableAiDeliveryStatus.Accepted)return;
            if(record.Status==PlayableAiDeliveryStatus.Applied&&mission!=null&&!mission.Completed&&mission.Generation==pendingGeneration&&!pendingDefense)
            {
                mission=mission.With(reinforcement:pendingAdded.Length>0?record.ApplicationTick:null,deployed:true,ids:mission.AssignedIds.Concat(pendingAdded));
            }
            ClearPending();
        }
        private void Reconcile(PlayableAiObservation observation)
        {
            if(mission==null||mission.Completed)return;
            var own=OwnFighters(observation).ToDictionary(x=>x.Id);
            var living=mission.AssignedIds.Where(own.ContainsKey).ToArray();
            // A published replacement order is positive evidence that exclusive mission ownership ended.
            // Missing order data is not proof of replacement in older diagnostic fixtures.
            var remaining=living.Where(id=>!mission.Deployed||own[id].CurrentOrder==null||MatchesMissionOrder(own[id].CurrentOrder)).ToArray();
            if(remaining.Length==0){mission=mission.With(completed:true,reason:living.Length==0?"force-lost":"order-replaced",ids:remaining);return;}
            var distance=DistanceToTarget(remaining.Select(id=>own[id]),mission.Target);
            if(distance+.5<previousDistance){previousDistance=distance;mission=mission.With(progress:observation.Tick,ids:remaining);}
            else if(remaining.Length!=mission.AssignedIds.Count)mission=mission.With(ids:remaining);
            if(mission.Deployed&&distance<=2){mission=mission.With(completed:true,reason:"arrived");return;}
            if(observation.Tick-mission.ProgressTick>StallTicks)mission=mission.With(completed:true,reason:"deadline");
        }
        private PlayableAiAction MissionAction(PlayableAiObservation observation,int[] ids)
        {
            var stillVisible=mission.VisibleTarget&&(observation.Entities.Any(x=>observation.IsHostile(x.Owner)&&x.Id==mission.TargetId)||observation.Buildings.Any(x=>observation.IsHostile(x.Owner)&&x.Id==mission.TargetId));
            return stillVisible?Action(observation,PlayableCommandKind.Attack,ids,targetId:mission.TargetId)
                :Action(observation,PlayableCommandKind.AttackMove,ids,target:mission.Target);
        }
        private PlayableAiAction Action(PlayableAiObservation observation,PlayableCommandKind kind,int[] ids,NavPoint target=default(NavPoint),int targetId=0)
        {
            pendingActionId=nextActionId++;pendingGeneration=observation.Generation;
            return new PlayableAiAction(pendingActionId,OwnerId,observation.ProfileId,observation.ProfileRevision,observation.Generation,observation.SnapshotSequence,kind,ids,target,targetId:targetId,seed:observation.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
        private void ClearPending(){pendingActionId=0;pendingGeneration=0;pendingAdded=Array.Empty<int>();pendingDefense=false;}
        private sealed class TargetChoice{public int TargetId,PublicSiteId;public NavPoint Point;public bool Visible;}
        private static TargetChoice SelectTarget(PlayableAiObservation observation)
        {
            var unit=VisibleEnemies(observation).OrderBy(x=>x.Health).ThenBy(x=>x.Id).FirstOrDefault();
            if(unit!=null)return new TargetChoice{TargetId=unit.Id,Point=unit.Position,Visible=true};
            var building=observation.Buildings.Where(x=>observation.IsHostile(x.Owner)&&x.Health>0).OrderBy(x=>x.Id).FirstOrDefault();
            if(building!=null)return new TargetChoice{TargetId=building.Id,Point=building.Position,Visible=true};
            var publicStart=observation.PublicScoutObjectives.Where(x=>x.Reachable&&x.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart).OrderBy(x=>x.SiteId).FirstOrDefault();
            return publicStart==null?null:new TargetChoice{PublicSiteId=publicStart.SiteId,Point=publicStart.Approach,Visible=false};
        }
        private static int Wave(PlayableAiOpening opening)=>opening==PlayableAiOpening.BlindRush?2:opening==PlayableAiOpening.ExplorerAllIn?4:opening==PlayableAiOpening.DoubleMineExplorerRush?5:0;
        private static IEnumerable<PlayableEntitySnapshot> VisibleEnemies(PlayableAiObservation observation)=>observation.Entities.Where(x=>observation.IsHostile(x.Owner)&&x.Health>0);
        private static IEnumerable<PlayableEntitySnapshot> OwnFighters(PlayableAiObservation observation)=>observation.Entities.Where(x=>x.Owner==observation.Owner&&x.Kind!=PlayableEntityKind.Shkval&&x.Health>0);
        private double Force(IEnumerable<PlayableEntitySnapshot> units)=>units.Sum(x=>(x.Kind==PlayableEntityKind.Explorer?.55:1)*Math.Min(1,x.Health/(double)(x.Kind==PlayableEntityKind.Explorer?nativeProfile.ExplorerHealth:x.Kind==PlayableEntityKind.Shkval?nativeProfile.ShkvalHealth:nativeProfile.TankHealth)));
        private static double Distance(NavPoint a,NavPoint b){var dx=a.X-b.X;var dz=a.Z-b.Z;return Math.Sqrt(dx*dx+dz*dz);}
        private static double DistanceToTarget(IEnumerable<PlayableEntitySnapshot> units,NavPoint target)=>units.Min(x=>Distance(x.Position,target));
        private bool MatchesMissionOrder(PlayableTacticalOrderSnapshot order)=>order.Kind==PlayableTacticalOrderKind.Attack&&mission.TargetId!=0&&order.TargetId==mission.TargetId
            ||order.Kind==PlayableTacticalOrderKind.AttackMove&&Distance(order.Destination,mission.Target)<.001;
    }
}
