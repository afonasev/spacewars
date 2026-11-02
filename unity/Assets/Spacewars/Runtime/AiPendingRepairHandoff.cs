using System;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal sealed partial class AiCombatMicroPolicy
    {
        internal const string PendingRepairHandoff="pending-local-repair-handoff:";
        internal bool CanYieldPendingRepair(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty difficulty,
            AiArmyRegistry armies,string policy,PlayableAiAction action)
        {
            if(o==null||action==null||!AiArmyPlanner.IsArmyPolicy(policy)||!AiRepairUsePolicy.IsAction(o,action)||
                action.PlayerId!=o.OwnerId||action.Generation!=o.Generation||action.Seed!=o.Seed||
                action.ProfileId!=o.ProfileId||action.ProfileRevision!=o.ProfileRevision||action.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity)return false;
            int id=action.EntityIds[0];
            if(!clients.TryGetValue(id,out var client)||client.Phase!=AiCombatMicroPhase.Repair||client.Tower!=action.ParentId||
                client.Sequence<=0||client.Started>o.Tick||o.Tick-client.Started>T(ai,difficulty,"maxPhaseSeconds"))return false;
            var unit=o.Entities.FirstOrDefault(u=>u.Id==id&&u.Owner==o.Owner&&Eligible(u));
            if(unit==null||PlayableUnitRules.HealthRatio(p,unit)>=ai.Value("positionDevelopment.repairReturnHealth")||
                Attackers(o,unit,p).Length>0||samples.TryGetValue(id,out var sample)&&(sample.Loss>0||Health(unit)<sample.Health))return false;
            var pending=unit.Queue?.Pending;
            if(pending==null||pending.Kind!=PlayableCommandKind.Move||pending.IssuanceId<=0||pending.GroupId<=0||
                pending.CommandSequence!=client.Sequence||pending.AiActionId!=client.ActionId||!pending.Anchor.HasValue||!pending.Anchor.Value.Position.Equals(client.Goal))return false;
            if(!certificates.TryGetValue(id,out var certificate)||certificate.Unit!=id||certificate.Owner!=o.OwnerId||certificate.Generation!=o.Generation||
                certificate.Source!=PlayableAiOpeningComposition.SourceIdentity||certificate.Kind!=PlayableCommandKind.Move||
                certificate.Sequence!=client.Sequence||certificate.ActionId!=client.ActionId||certificate.Army!=client.Army||
                certificate.Tick>o.Tick||certificate.Decision<=0||certificate.Ordinal<=0||!certificate.Goal.Equals(client.Goal))return false;
            var army=armies.Capture().Armies.FirstOrDefault(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded&&a.OwnerId==o.OwnerId&&a.TacticalOwner==policy&&a.Members.Contains(id));
            if(army==null||client.Army!=0&&client.Army!=army.Id)return false;
            var tower=o.Buildings.FirstOrDefault(b=>b.Id==client.Tower&&Tower(b,o.Owner));
            // This only makes the same-purpose pending custody replaceable. The
            // army's current proof/core admission and ordinary delivery still own
            // the new command; an installed local repair is never stolen.
            return tower!=null&&((tower.Healing?.TargetId??0)==0||tower.Healing.TargetId==id)&&
                Distance(action.Target,tower.Position)<=AiPositionDevelopmentPlanner.Range(p,PlayableBuildingKind.RepairTower);
        }
    }
}
