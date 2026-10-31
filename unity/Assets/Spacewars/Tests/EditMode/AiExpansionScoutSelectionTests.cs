using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

public sealed class AiExpansionScoutSelectionTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly PlayableProfile P=PlayableProfile.ThreeCrossingsDefault;
    static readonly AiProfile Ai=NativeBalanceFields.InitialAiOpeningProfile();
    static T Get<T>(object x,string name)=>(T)x.GetType().GetField(name,F).GetValue(x);
    static AiIntent Proposal(PlayableAiOwnerLoop loop,string policy,PlayableAiAction action,PlayableAiObservation o,int priority)=>(AiIntent)typeof(PlayableAiOwnerLoop).GetMethod("Proposal",F).Invoke(loop,new object[]{policy,action,o,priority});
    static object VerifyBuildAndScoutClaims(PlayableAiOwnerLoop loop)
    {
        int site=TerritoryRules.Sites(P).First(s=>s.Kind==PlayableBuildingKind.Mine).Id;
        var o=PlayableAiObservation.From(AiExpansionScoutAdmissionTests.View(5900,1000,claimedSite:site,captureProgress:1));
        PlayableAiAction Action(PlayableCommandKind kind)=>new PlayableAiAction(1,o.OwnerId,P.ProfileId,P.Revision,1,o.SnapshotSequence,kind,new[]{13},o.Sites.Single(s=>s.Site.Id==site).Site.Position,siteId:site,buildingKind:PlayableBuildingKind.Mine,seed:881549,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        var build=Proposal(loop,"expansion",Action(PlayableCommandKind.BuildAt),o,1);var scout=Proposal(loop,"scout",Action(PlayableCommandKind.Move),o,0);
        Assert.NotNull(build);Assert.NotNull(scout);CollectionAssert.Contains(build.Claims,"recipient:13");CollectionAssert.Contains(scout.Claims,"recipient:13");
        var arbiter=new AiDecisionArbiter(Ai);var selected=arbiter.Select(new[]{scout,build},5900,1000,100,(int)Ai.DifficultyValue(AiDifficulty.Veteran,"actionsPerDecision"));
        CollectionAssert.AreEqual(new[]{build.Id},selected.Select(x=>x.Id));Assert.AreEqual("selected action claim conflict",arbiter.Rejections[scout.Id]);
        return new{BuildClaims=build.Claims,ScoutClaims=scout.Claims,Selected=selected.Select(x=>x.Policy),Rejections=arbiter.Rejections};
    }
    [TestCase(false)][TestCase(true)] public void ActualRejectedSelectionLeavesScoutEligibleWhileAcceptedExpansionKeepsActor(bool rejectPrior)
    {
        var loop=AiExpansionScoutAdmissionTests.Owner(1000,out var financeOrdinal,remoteHistory:false);var rows=new List<object>();var readbacks=new List<CurrentExpansionDecisionReadback>();
        var previous=PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests;var previousOwner=PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests;
        try{
            Assert.IsNull(previous);var sharedClaims=VerifyBuildAndScoutClaims(loop);
            PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests="air-owner";PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=c=>readbacks.Add(c);
            loop.Review(AiExpansionScoutAdmissionTests.View(5985,1000),0);
            var pending=(IList)Get<object>(loop,"pendingItems");var expansionItem=pending.Cast<object>().Single(x=>Get<string>(x,"PolicyName")=="expansion");var priorIntent=Get<AiIntent>(expansionItem,"Intent");
            int site=priorIntent.Action.SiteId;Assert.AreEqual(13,priorIntent.Action.EntityIds.Single());Assert.AreEqual(13,Get<AiExpansionPlanner>(loop,"expansion").Capture().ActorId);
            var old=PlayableAiObservation.From(AiExpansionScoutAdmissionTests.View(5985,1000));
            object feedback=null;
            if(rejectPrior){
                pending.Remove(expansionItem);typeof(PlayableAiOwnerLoop).GetField("authorityTick",F).SetValue(loop,5999L);
                typeof(PlayableAiOwnerLoop).GetMethod("Finish",F).Invoke(loop,new object[]{expansionItem,1L,5999L,PlayableAiDeliveryStatus.Rejected,PlayableCommandStatus.Rejected,"controlled ordinary expansion rejection",false});
                var planner=Get<AiExpansionPlanner>(loop,"expansion");var changed=PlayableAiObservation.From(AiExpansionScoutAdmissionTests.View(6000,1000,claimedSite:site));
                bool oldSuppressed=planner.Suppressed(old.Sites.Single(s=>s.Site.Id==site),old.Entities.Single(e=>e.Id==13),old,P,Ai);
                bool changedSuppressed=planner.Suppressed(changed.Sites.Single(s=>s.Site.Id==site),changed.Entities.Single(e=>e.Id==13),changed,P,Ai);
                Assert.True(oldSuppressed);Assert.False(changedSuppressed,"material claimant change reopens the actual planner without clearing arbiter history");
                feedback=new{DeliveryTick=5999,Status="Rejected",OldPlannerSuppressed=oldSuppressed,ChangedPlannerSuppressed=changedSuppressed,PriorId=priorIntent.Id,PriorConditions=priorIntent.Conditions};
            }
            foreach(long tick in rejectPrior?new[]{6000L,6015L}:new[]{6000L}){
                var view=AiExpansionScoutAdmissionTests.View(tick,1000,claimedSite:rejectPrior?site:0);var o=PlayableAiObservation.From(view);var registry=Get<AiArmyRegistry>(loop,"armies");
                var probe=new AiKnowledgeTracker("air-owner",1);probe.Observe(o,Ai);
                var potential=Get<AiScoutPlanner>(loop,"scoutPlanner").Plan(o,probe.Capture(),registry,P,Ai,(int)Ai.DifficultyValue(AiDifficulty.Veteran,"scoutAssignments"));
                int available=Get<int>(loop,"actionLimit")-pending.Count;
                loop.Review(view,0);var c=readbacks.Last();var expansion=Get<AiExpansionPlanner>(loop,"expansion").Capture();
                var scoutIds=registry.Capture().Armies.Where(a=>a.Role==AiArmyRole.Scout&&a.Phase!=AiArmyPhase.Disbanded).SelectMany(a=>a.Members).ToArray();
                PlayableAiAction military=null;
                if(expansion.ActorId==0&&scoutIds.Length==0){var planner=new AiArmyPlanner("air-owner",1);planner.Rebind(P);military=planner.ProposeWithRecords(o,loop.Checkpoint.Opening.WithPhase(PlayableAiOpeningPhase.Complete),P,Ai,AiArmyRegistry.Restore(registry.Capture(),Ai,AiDifficulty.Veteran,tick),Array.Empty<PlayableAiTraceRecord>());}
                rows.Add(new{tick,ForwardTicks=0,AvailableActions=available,PotentialScout=potential,c.ExpansionAction,c.ExpansionIntent,Proposals=c.Proposals.Select(p=>new{p.Id,p.Policy,p.Action,p.Priority,p.Claims,p.Conditions}),c.Selected,c.Rejections,c.ProtectedFunds,c.Ledger,Utility=Get<HashSet<int>>(loop,"utilityReserved").ToArray(),ScoutIds=scoutIds,Expansion=expansion,Armies=registry.Capture(),MilitaryWithoutGhost=military});
            }
            TestContext.WriteLine("Q5_SELECTION "+JsonConvert.SerializeObject(new{BaselineSource="04f7581869b085d860e423213609f60e24b38e56",rejectPrior,financeOrdinal,ForwardTicks=0,SharedBuildAndScoutClaims=sharedClaims,Feedback=feedback,PriorProposal=readbacks[0].ExpansionIntent,PriorProposals=readbacks[0].Proposals.Select(p=>new{p.Policy,p.Action,p.Claims}),PriorSelected=readbacks[0].Selected,Reviews=rows}));
            var finalRegistry=Get<AiArmyRegistry>(loop,"armies");var finalExpansion=Get<AiExpansionPlanner>(loop,"expansion").Capture();
            if(rejectPrior){
                var first=readbacks[1];Assert.NotNull(first.ExpansionIntent);Assert.AreEqual(priorIntent.Id,first.ExpansionIntent.Id);Assert.AreEqual(priorIntent.Conditions,first.ExpansionIntent.Conditions);
                Assert.AreEqual("rejection cooldown",first.Rejections[first.ExpansionIntent.Id]);Assert.Zero(finalExpansion.SiteId);
                Assert.True(finalRegistry.Capture().Armies.Any(a=>a.Role==AiArmyRole.Scout&&a.Phase!=AiArmyPhase.Disbanded&&a.Members.Contains(13)),"an arbiter-rejected expansion cannot hide the same Explorer from the actual scout candidate");
            }else{Assert.AreEqual(13,finalExpansion.ActorId);Assert.Zero(finalRegistry.ArmyFor(13));}
            finalRegistry.AssertInvariants();
        }finally{PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=previous;PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=previousOwner;loop.Stop();}
    }
}
