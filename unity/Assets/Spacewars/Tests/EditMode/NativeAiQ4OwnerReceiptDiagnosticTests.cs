using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Runtime;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiQ4OwnerReceiptDiagnosticTests
    {
        [Test] public void UnchangedNormalRuntimeContractWithActualInitialDecisionReadback()
        {
            var rows=new List<string>();var root=new DirectoryInfo(Directory.GetCurrentDirectory());while(root!=null&&!File.Exists(Path.Combine(root.FullName,"workflow/project.json")))root=root.Parent;Assert.NotNull(root);
            PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests="player-1";
            PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=r=>
            {
                var text=JsonConvert.SerializeObject(new{r.Observation.Tick,r.Observation.Credits,r.ProtectedFunds,r.FinanceSite,r.FinanceReady,r.Threatened,r.UrgentRepair,r.EssentialScale,r.ScalingStartup,r.ScalingReason,r.MissingScout,r.ExpansionAction,r.Proposals,r.Selected,r.Rejections,r.BeforeFinanceDetach,r.AfterTechnologyAllocation,r.AfterFinancialAllocation,r.Observation.Buildings});
                lock(rows)rows.Add(text);
            };
            try{new PlayableAiOwnerLoopTests().NormalRuntimeAppliesOwnerAiEconomyWithAuthorityReceipt();}
            finally
            {
                PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=null;PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=null;
                var folder=Path.Combine(root.FullName,".local/q4-owner-receipt-diagnostic");Directory.CreateDirectory(folder);lock(rows)File.WriteAllLines(Path.Combine(folder,"initial-decisions.jsonl"),rows);
            }
        }
    }
}
