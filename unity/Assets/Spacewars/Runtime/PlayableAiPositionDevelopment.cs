using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableAiOwnerLoop
    {
        // Call from macro review after ordinary protections/quotes are established.
        // Uses the existing economy allocator, callback and serialized pending policy.
        // No additional live graph or policy state needs cloning/restoring.
        private void ProposePositionDevelopment(PlayableAiObservation o,List<AiIntent> proposals,int protectedFunds,bool urgentRepair,IReadOnlyList<AiPositionInvestment> choices,AiEconomicInvestment saving)
        {
            if(pendingItems.Any(p=>p.PolicyName=="economy")||urgentRepair||openingExecutor.MissingEssentialEconomy(o,nativeProfile))return;
            foreach(var choice in choices)
            {
                if(saving!=null&&AiEconomyAdmission.IntentId("economy",choice.Action)!=AiEconomyAdmission.IntentId("economy",saving.Candidate.Action))continue;
                var fork=economy.Fork();var action=fork.Admit(o,choice.Action);
                // An unmet useful position minimum is ordinary development, never survival.
                var quoted=Proposal("economy",action,o,action.Kind==PlayableCommandKind.BuildAt?1:0);if(quoted==null)continue;
                int cost=action.Kind==PlayableCommandKind.UpgradeRefinery?(int)Math.Ceiling(profile.RefineryUpgradeCost):quoted.Credits;
                var intent=new AiIntent(quoted.Id,quoted.Policy,quoted.Action,quoted.ExpiresTick,cost,quoted.Population,quoted.Claims,quoted.Priority,choice.Utility,quoted.Conditions,choice.Reason,quoted.StartupFund,quoted.ReservationId,quoted.Overdue);
                if(!AiExpansionFinancial.FullyFundable(budget,protectedFunds,intent))continue;
                intent.Commit=()=>economy=fork;intent.Feedback=fork.ObserveReceipt;proposals.Add(intent);
            }
        }
    }
}
