using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

public sealed class AiExpansionScoutAdmissionTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly PlayableProfile P=PlayableProfile.ThreeCrossingsDefault;
    static readonly AiProfile Ai=NativeBalanceFields.InitialAiOpeningProfile();
    static T Get<T>(object x,string name)=>(T)x.GetType().GetField(name,F).GetValue(x);
    internal static PlayableSnapshot View(long tick,int credits,int hawks=2,int claimedSite=0,double captureProgress=.25)
    {
        var home=TerritoryRules.Sites(P).Single(s=>s.Id==1);
        var mines=TerritoryRules.Sites(P).Where(s=>s.Kind==PlayableBuildingKind.Mine).Take(2).ToArray();Assert.AreEqual(2,mines.Length);
        var bs=new List<PlayableBuildingSnapshot>{new PlayableBuildingSnapshot(1000,PlayableOwner.Player,PlayableBuildingKind.Headquarters,home.Position,P.HeadquartersHealth,1,0,0,default,siteId:1)};
        for(int i=0;i<3;i++)bs.Add(new PlayableBuildingSnapshot(1001+i,PlayableOwner.Player,PlayableBuildingKind.Refinery,home.Slots[i].Position,P.RefineryHealth,1,0,0,default,siteId:1,slotId:home.Slots[i].Id,parentId:1000));
        bs.Add(new PlayableBuildingSnapshot(4,PlayableOwner.Player,PlayableBuildingKind.Factory,home.Slots[3].Position,P.FactoryHealth,1,0,0,default,siteId:1,slotId:home.Slots[3].Id,parentId:1000));
        bs.Add(new PlayableBuildingSnapshot(5,PlayableOwner.Player,PlayableBuildingKind.Hangar,home.Slots[4].Position,(int)P.HangarHealth,1,0,0,default,siteId:1,slotId:home.Slots[4].Id,parentId:1000));
        var units=new[]{new PlayableEntitySnapshot(13,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(mines[0].Position.X+6,mines[0].Position.Z),P.ExplorerHealth,false,0,0,0)}
            .Concat(new[]{14,16,18,26}.Take(hawks).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Hawk,default,(int)P.HawkHealth,false,0,0,0))).ToArray();
        var objectives=mines.Select(s=>new PlayablePublicScoutObjective(s.Id,s.Position,PlayablePublicScoutObjectiveRole.ExpansionIncome,true))
            .Concat(new[]{new PlayablePublicScoutObjective(991,new NavPoint(20,0),PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true)}).ToArray();
        var proofs=(from u in units from t in objectives where u.Kind==PlayableEntityKind.Explorer||t.SiteId==991
            select new PlayableRouteProof(u.Id,u.Owner,1,tick,1,u.Position,PlayableUnitRules.Radius(P,u.Kind),PlayableRouteTargetKind.PublicObjective,t.SiteId,t.Approach,t.Approach,new[]{t.Approach})).ToArray();
        return new PlayableSnapshot(P.ProfileId,P.Revision,1,881549,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,new NavGeometry(P.ArenaHalfExtent,Array.Empty<NavObstacle>(),1),units,bs.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
            sites:new[]{new TerritorySiteSnapshot(home,PlayableOwner.Player,PlayableOwner.Player,1,false,1000,true)}.Concat(mines.Select(s=>new TerritorySiteSnapshot(s,claimant:s.Id==claimedSite?(PlayableOwner?)PlayableOwner.Player:null,owner:null,progress:s.Id==claimedSite?captureProgress:0,contested:false,centerId:0,ready:false))).ToArray(),population:new PlayablePopulationSnapshot(units.Length,0,100),publicScoutObjectives:objectives,routeProofs:proofs,owner:PlayableOwner.Player,ownerId:"air-owner",homeSiteId:1,activeProfile:P);
    }
    internal static PlayableAiOwnerLoop Owner(int credits,out long historyOrdinal,bool remoteHistory=true)
    {
        var loop=new PlayableAiOwnerLoop(P,PlayableAiOpeningComposition.Initialize(881549,"air-owner",aiProfile:Ai),1,null,Ai,AiDifficulty.Veteran);
        var opening=Get<AiOpeningExecutor>(loop,"openingExecutor");opening.SpendableCredits=credits;
        opening.Observe(PlayableAiObservation.From(View(5900,credits,4)),null,null,Ai);
        Assert.True(opening.Capture().InitialForceFulfilled);Assert.Zero(opening.ProtectedUnit);
        // A legal post-opening fixture: the original four-Hawk wave was deployed,
        // and only two Hawks now survive. The historical milestone stays fulfilled.
        Get<HashSet<string>>(opening,"completed").Add("pressure");typeof(AiOpeningExecutor).GetField("phase",F).SetValue(opening,PlayableAiOpeningPhase.Complete);
        var o=PlayableAiObservation.From(View(5900,credits));var budget=Get<AiBudgetLedger>(loop,"budget");budget.ObserveBank(credits);
        var build=new PlayableAiAction(1,o.OwnerId,P.ProfileId,P.Revision,1,o.SnapshotSequence,PlayableCommandKind.BuildAt,siteId:1,slotId:TerritoryRules.Sites(P).Single(s=>s.Id==1).Slots[3].Id,parentId:1000,buildingKind:PlayableBuildingKind.Factory,unitKind:PlayableEntityKind.Explorer,seed:881549,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        int held=(int)Ai.Value("economy.factoryLaunchCycles")*PlayableUnitRules.Cost(P,PlayableEntityKind.Explorer);
        var intent=new AiIntent("prior-factory","economy",build,7250,0,0,Array.Empty<string>(),startupFund:held);var receipt=new AiReceiptIdentity(1,"air-owner",1,1);var launches=Get<AiFactoryLaunchCommitments>(loop,"launches");
        launches.Begin(intent,receipt,5900,budget,P,Ai);launches.Terminal(intent,receipt,PlayableAiDeliveryStatus.Applied,true,budget,P);launches.Observe(o,budget,P);
        Assert.AreEqual(held,budget.Reserved);typeof(PlayableAiOwnerLoop).GetField("ordinal",F).SetValue(loop,1L);
        // Remote-history fixtures retain a prior abandoned choice; first-choice
        // fixtures use the genuine genesis ordinal. Seed/profile/weights stay
        // unchanged in both cases.
        historyOrdinal=remoteHistory?Enumerable.Range(1,16).Select(i=>(long)i).First(i=>AiIncomeSiteSelection.DrawRemote(o,Ai,i)):0;
        var arbiter=Get<AiDecisionArbiter>(loop,"arbiter");var finance=Get<object>(loop,"expansionFinancial");var type=finance.GetType();
        type.GetMethod("Reconcile",F).Invoke(finance,new object[]{o,P,Ai,opening,budget,arbiter,Get<AiExpansionPlanner>(loop,"expansion")});
        var memo=type.GetMethod("Load",F).Invoke(null,new object[]{arbiter});
        memo.GetType().GetField("HasSelection",F).SetValue(memo,remoteHistory);memo.GetType().GetField("NextSelectionOrdinal",F).SetValue(memo,historyOrdinal);
        type.GetMethod("Save",F).Invoke(null,new object[]{arbiter,memo});
        type.GetMethod("Validate",F).Invoke(finance,new object[]{P,Ai,budget,arbiter,5900L,881549,TerritoryRules.Sites(P),2});
        type.GetMethod("ValidateHome",F).Invoke(finance,new object[]{arbiter,1});return loop;
    }
    [TestCase(200,false)][TestCase(1000,true)] public void ActualExpansionAdmissionKeepsTheSharedActorAvailableToItsRealOwner(int credits,bool acceptedExpansion)
    {
        var loop=Owner(credits,out var historyOrdinal);var rows=new List<object>();var admissions=new List<object>();var readbacks=new List<CurrentExpansionDecisionReadback>();
        var previous=PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests;var previousOwner=PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests;
        var oldAdmission=PlayableAiOwnerLoop.ProposalAdmissionReadbackForTests;var oldAdmissionOwner=PlayableAiOwnerLoop.ProposalAdmissionOwnerFilterForTests;
        try{
            Assert.IsNull(previous);Assert.IsNull(oldAdmission);
            PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests="air-owner";PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=c=>readbacks.Add(c);
            PlayableAiOwnerLoop.ProposalAdmissionOwnerFilterForTests="air-owner";PlayableAiOwnerLoop.ProposalAdmissionReadbackForTests=r=>admissions.Add(new{r.Tick,r.Stage,r.Reason,r.Action});
            foreach(long tick in acceptedExpansion?new[]{6000L}:new[]{6000L,6015L}){
                var view=View(tick,credits);loop.Review(view,0);var c=readbacks.Last();var registry=Get<AiArmyRegistry>(loop,"armies");
                var scoutIds=registry.Capture().Armies.Where(a=>a.Role==AiArmyRole.Scout&&a.Phase!=AiArmyPhase.Disbanded).SelectMany(a=>a.Members).ToArray();
                var control=new AiArmyPlanner("air-owner",1);control.Rebind(P);
                var controlAction=control.ProposeWithRecords(PlayableAiObservation.From(view),loop.Checkpoint.Opening.WithPhase(PlayableAiOpeningPhase.Complete),P,Ai,AiArmyRegistry.Restore(registry.Capture(),Ai,AiDifficulty.Veteran,tick),Array.Empty<PlayableAiTraceRecord>());
                rows.Add(new{tick,ForwardTicks=0,c.ProtectedFunds,c.FinanceSite,c.FinanceReady,c.ExpansionAction,c.ExpansionIntent,Proposals=c.Proposals.Select(p=>new{p.Policy,p.Action,p.Credits,p.StartupFund}),c.Selected,c.Rejections,c.Ledger,
                    Utility=Get<HashSet<int>>(loop,"utilityReserved").ToArray(),ScoutIds=scoutIds,Expansion=Get<AiExpansionPlanner>(loop,"expansion").Capture(),Armies=registry.Capture(),ControlArmyActionWithoutGhost=controlAction});
            }
            TestContext.WriteLine("Q5_ADMISSION "+JsonConvert.SerializeObject(new{credits,acceptedExpansion,historyOrdinal,NaturalRemoteDraw=true,BaselineSource="03db526152425b6a304c6aae9fc8b108ee85cfd9",ForwardTicks=0,Admissions=admissions,Reviews=rows}));
            var last=readbacks.Last();var armies=Get<AiArmyRegistry>(loop,"armies");var expansion=Get<AiExpansionPlanner>(loop,"expansion").Capture();
            if(acceptedExpansion){Assert.NotNull(last.ExpansionIntent);Assert.AreEqual(13,expansion.ActorId);Assert.Zero(armies.ArmyFor(13));Assert.False(armies.Capture().Armies.Any(a=>a.Role==AiArmyRole.Scout&&a.Members.Contains(13)));}
            else{Assert.NotNull(readbacks[0].ExpansionAction);Assert.AreEqual(13,readbacks[0].ExpansionAction.EntityIds.Single());Assert.IsNull(last.ExpansionIntent);Assert.Zero(expansion.SiteId);Assert.True(armies.Capture().Armies.Any(a=>a.Role==AiArmyRole.Scout&&a.Phase!=AiArmyPhase.Disbanded&&a.Members.Contains(13)),"a rejected fallback must not hide its unclaimed Explorer from the actual scout proposal");}
            armies.AssertInvariants();
        }finally{PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=previous;PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=previousOwner;PlayableAiOwnerLoop.ProposalAdmissionReadbackForTests=oldAdmission;PlayableAiOwnerLoop.ProposalAdmissionOwnerFilterForTests=oldAdmissionOwner;loop.Stop();}
    }
}
