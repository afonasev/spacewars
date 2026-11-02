using System;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // One bounded quote, not another reservation or a promise of money. The existing
    // shared ledger still owns every accepted action and real-money settlement.
    internal static class AiPositionDevelopmentFunding
    {
        internal static AiEconomicInvestment Quote(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDecisionArbiter arbiter,
            System.Collections.Generic.IReadOnlyList<AiPositionInvestment> choices,int productionProtection,bool fundingAllowed=true)
        {
            var choice=choices.OrderByDescending(c=>c.Utility).ThenBy(c=>AiEconomyAdmission.IntentId("economy",c.Action),StringComparer.Ordinal).FirstOrDefault();
            if(!arbiter.PositionSavingWindow(o,p,ai,choice?.Action)||!fundingAllowed)return null;
            int cost=choice.Action.Kind==PlayableCommandKind.UpgradeRefinery?(int)Math.Ceiling(p.RefineryUpgradeCost):TerritoryRules.Cost(p,choice.Action.BuildingKind);
            return new AiEconomicInvestment(new AiEconomyCandidate("economy",choice.Action,null,0),cost,choice.Reason,productionProtection);
        }
    }
    public sealed partial class AiDecisionArbiter
    {
        internal const string PositionGoalKey="position-investment-demand-v1";
        private static string PositionContext(string owner,long generation,PlayableProfile p,AiProfile ai)=>owner+"|"+generation+"|"+p.ProfileId+"@"+p.Revision+"|"+ai.Hash+"|";
        internal bool PositionSavingWindow(PlayableAiObservation o,PlayableProfile p,AiProfile ai,PlayableAiAction action)
        {
            if(action==null){history.Remove(PositionGoalKey);return false;}
            string target=PositionContext(o.OwnerId,o.Generation,p,ai)+(int)action.Kind+":"+action.SiteId+":"+action.SlotId+":"+action.ParentId+":"+(int)action.BuildingKind+":"+string.Join(",",action.EntityIds);
            if(!history.TryGetValue(PositionGoalKey,out var h)||h.Conditions!=target)
                history[PositionGoalKey]=h=new History{Conditions=target,FirstTick=o.Tick,NextRetry=checked(o.Tick+AiProfile.SecondsToTicks(ai.Value("economy.reservationExpirySeconds"),30))};
            // Keep an expired target as a tombstone until it becomes invalid/covered or
            // changes. Income and review ticks never renew its saving window.
            return o.Tick<=h.NextRetry;
        }
        internal void ClearPositionSaving()=>history.Remove(PositionGoalKey);
        private static void ValidatePositionSaving(string condition,long created,long expires,int failures,long tick,string owner,long generation,PlayableProfile p,AiProfile ai)
        {
            string prefix=PositionContext(owner,generation,p,ai);
            AiStateWire.Require(condition!=null&&condition.Length<=2048&&condition.StartsWith(prefix,StringComparison.Ordinal)&&failures==0&&created<=tick&&expires>=created&&expires-created<=AiProfile.SecondsToTicks(ai.Value("economy.reservationExpirySeconds"),30),"position saving context/clock");
            var parts=condition.Substring(prefix.Length).Split(':');
            AiStateWire.Require(parts.Length==6&&int.TryParse(parts[0],out int kind)&&Enum.IsDefined(typeof(PlayableCommandKind),kind)&&
                (kind==(int)PlayableCommandKind.BuildAt||kind==(int)PlayableCommandKind.UpgradeRefinery)&&int.TryParse(parts[1],out int site)&&int.TryParse(parts[2],out int slot)&&int.TryParse(parts[3],out int parent)&&int.TryParse(parts[4],out int building)&&Enum.IsDefined(typeof(PlayableBuildingKind),building)&&
                (kind==(int)PlayableCommandKind.BuildAt?site>0&&slot>0&&parent>0&&TerritoryRules.Defensive((PlayableBuildingKind)building)&&parts[5]=="":site==0&&slot==0&&parent==0&&int.TryParse(parts[5],out int entity)&&entity>0),"position saving target");
        }
    }
}
