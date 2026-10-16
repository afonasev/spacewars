using System;
using System.IO;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiIncomeSiteSelectionTests
    {
        private static readonly PlayableProfile P=PlayableProfile.Default;
        private static AiProfile Profile(double share=0.1,int protectedCount=2)
        {var data=AiProfile.Initial.CopyData();data.fields.Single(f=>f.path=="economy.remoteExpansionShare").value=share;data.fields.Single(f=>f.path=="economy.enemyMainProtectedIncomeSites").value=protectedCount;return new AiProfile(data);}
        private static TerritorySiteSnapshot Site(int id,double x,double z=0,int center=0)=>new TerritorySiteSnapshot(
            new TerritorySite(id,PlayableBuildingKind.Mine,new NavPoint(x,z),Array.Empty<TerritorySlot>()),null,center==0?(PlayableOwner?)null:PlayableOwner.Enemy,0,false,center,false);
        private static PlayableAiObservation Observation(TerritorySiteSnapshot[] sites,int seed=41,NavPoint? scout=null,long tick=0,PlayableBuildingSnapshot[] enemies=null)
        {
            var u=new PlayableEntitySnapshot(13,PlayableOwner.Player,PlayableEntityKind.Explorer,scout??new NavPoint(35,0),100,false,0,0,0);
            var home=new[]{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(0,0),100,1,0,0,default,siteId:1),
                new PlayableBuildingSnapshot(2,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(0,5),100,1,0,0,default,siteId:1,slotId:1),
                new PlayableBuildingSnapshot(3,PlayableOwner.Player,PlayableBuildingKind.Refinery,new NavPoint(0,-5),100,1,0,0,default,siteId:1,slotId:2)};
            var objectives=sites.Select(s=>new PlayablePublicScoutObjective(s.Site.Id,s.Site.Position,PlayablePublicScoutObjectiveRole.ExpansionIncome,true)).ToArray();
            var proofs=sites.Select(s=>new PlayableRouteProof(13,PlayableOwner.Player,71,tick,1,u.Position,P.ExplorerCollisionRadius,PlayableRouteTargetKind.PublicObjective,s.Site.Id,s.Site.Position,s.Site.Position,new[]{s.Site.Position})).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,seed,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,
                new[]{u},home.Concat(enemies??Array.Empty<PlayableBuildingSnapshot>()).ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                sites:sites,publicScoutObjectives:objectives,routeProofs:proofs,activeProfile:P,homeSiteId:1,ownerId:"owner-a"));
        }
        private static AiKnowledgeState Knowledge(PlayableAiObservation o,params AiKnownContact[] contacts)=>new AiKnowledgeState(o.OwnerId,o.Generation,o.Tick,contacts,Array.Empty<AiVisitedArea>());
        private static AiKnownContact Main(int id,double x,double z=0)=>new AiKnownContact(id,PlayableOwner.Enemy,true,(int)PlayableBuildingKind.Headquarters,new NavPoint(x,z),100,0,0.01,false);
        [Test] public void LocalTargetUsesHomeEvenWhenScoutIsBesideRemote()
        {
            var o=Observation(new[]{Site(4,12),Site(5,35)},scout:new NavPoint(35,0));
            var planner=new AiExpansionPlanner(o.OwnerId);var action=planner.TryPlan(o,P,Profile(0));
            Assert.NotNull(action);Assert.AreEqual(4,action.SiteId);
            Assert.AreEqual(4,AiIncomeSiteSelection.Select(o,P,Profile(),null,o.Sites.Reverse(),false).Site.Id);
        }
        [Test] public void RemoteExcludesTwoNearestIncludingOccupiedAndNextFreeBeyondThem()
        {
            var sites=new[]{Site(4,10,30),Site(5,90,0,55),Site(6,80,0,56),Site(7,70),Site(8,25,40)};
            var o=Observation(sites);var knowledge=Knowledge(o,Main(50,100));var ai=Profile(1);
            Assert.False(AiIncomeSiteSelection.RemoteAllowed(o,P,ai,knowledge,sites[1]));
            Assert.False(AiIncomeSiteSelection.RemoteAllowed(o,P,ai,knowledge,sites[2]));
            Assert.False(AiIncomeSiteSelection.RemoteAllowed(o,P,ai,knowledge,sites[3]),"nearest observed free is excluded even beyond occupied two");
            Assert.True(AiIncomeSiteSelection.RemoteAllowed(o,P,ai,knowledge,sites[4]));
            Assert.AreEqual(8,AiIncomeSiteSelection.Select(o,P,ai,knowledge,sites.Where(s=>s.CenterId==0),true).Site.Id);
        }
        [Test] public void NoEligibleRemoteFallsBackLocalAndNoSafeSiteDefers()
        {
            var o=Observation(new[]{Site(4,10),Site(5,80),Site(6,90)});var k=Knowledge(o,Main(50,100));
            Assert.AreEqual(4,AiIncomeSiteSelection.Select(o,P,Profile(1),k,o.Sites,true).Site.Id);
            Assert.Null(AiIncomeSiteSelection.Select(o,P,Profile(1),k,Array.Empty<TerritorySiteSnapshot>(),true));
        }
        [Test] public void KnownMainSurvivesFogButUnobservedMainAndAlliesDoNotProtectSites()
        {
            var o=Observation(new[]{Site(4,10),Site(5,80),Site(6,90)});var ai=Profile(1);
            Assert.AreEqual(5,AiIncomeSiteSelection.Select(o,P,ai,Knowledge(o),o.Sites,true).Site.Id,"no hidden world handle exists in the selector");
            Assert.AreEqual(4,AiIncomeSiteSelection.Select(o,P,ai,Knowledge(o,Main(50,100)),o.Sites,true).Site.Id);
            var ally=new AiKnownContact(50,PlayableOwner.Player,true,(int)PlayableBuildingKind.Headquarters,new NavPoint(100,0),100,0,1,true);
            Assert.AreEqual(5,AiIncomeSiteSelection.Select(o,P,ai,Knowledge(o,ally),o.Sites,true).Site.Id);
        }
        [Test] public void RememberedStaticDangerBlocksSiteAndRouteUntilObservedAbsent()
        {
            var o=Observation(new[]{Site(4,10),Site(5,35)},scout:new NavPoint(35,0));var s=o.Sites[1];var u=o.Entities.Single();
            var k=Knowledge(o,Main(50,36));
            Assert.False(AiIncomeSiteSelection.SiteSafe(o,P,k,s));
            Assert.False(AiExpansionPlanner.Safe(o,P,Profile(),s,u,o.RouteProofs[1],k));
            Assert.True(AiIncomeSiteSelection.SiteSafe(o,P,Knowledge(o),s));
        }
        [Test] public void RemoteShareIsSeededStableAndProfileControlled()
        {
            var sites=new[]{Site(4,10),Site(5,35)};var ai=Profile();int remote=0;
            for(int seed=0;seed<1000;seed++)
            {
                var o=Observation(sites,seed);bool draw=AiIncomeSiteSelection.DrawRemote(o,ai,0);if(draw)remote++;
                Assert.AreEqual(draw,AiIncomeSiteSelection.DrawRemote(Observation(sites,seed,tick:100),ai,0));
                Assert.False(AiIncomeSiteSelection.DrawRemote(o,Profile(0),0));Assert.True(AiIncomeSiteSelection.DrawRemote(o,Profile(1),0));
            }
            Assert.That(remote,Is.InRange(70,130),"fixed seed bank exercises the approved ten-percent policy, not repeated chance at each tick");
            Assert.AreEqual(0.1,ai.Value("economy.remoteExpansionShare"));Assert.AreEqual(2,ai.Value("economy.enemyMainProtectedIncomeSites"));
        }
        [Test] public void OrdinaryFallbackCannotRenameAnExcludedNearestSiteIntoALocalDecision()
        {
            var o=Observation(new[]{Site(4,10),Site(5,35)});var planner=new AiExpansionPlanner(o.OwnerId){LocalSiteId=4};
            Assert.Null(planner.TryPlan(o,P,Profile(0),0,null,id=>id!=4),"nearest financial goal cannot be bypassed by a 90-percent fallback");
            planner.PreferRemote=true;Assert.AreEqual(5,planner.TryPlan(o,P,Profile(1),0,null,id=>id!=4).SiteId);
        }
        [Test] public void NewlyKnownEnemyMainInvalidatesUnacceptedRemoteTerritory()
        {
            var o=Observation(new[]{Site(4,10),Site(5,80),Site(6,90)});
            Assert.True(AiIncomeSiteSelection.TerritoryAllowed(o,P,Profile(),Knowledge(o),o.Sites[1]));
            Assert.False(AiIncomeSiteSelection.TerritoryAllowed(o,P,Profile(),Knowledge(o,Main(50,100)),o.Sites[1]));
            Assert.True(AiIncomeSiteSelection.TerritoryAllowed(o,P,Profile(),Knowledge(o,Main(50,100)),o.Sites[0]),"nearest local fallback retains existing threat safety");
        }
        [Test] public void ActualOwnerProposalRejectsIncidentalRemoteMineAndAdmitsLocal()
        {
            // Invoke the production owner admission path, not a duplicate of its predicate.
            var config=(OfflineMatchConfiguration)typeof(NativeAiExpansionTests).GetMethod("PostOpeningConfig",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{PlayableBuildingKind.Mine});
            var authority=new PlayableAuthorityTick(config,71);var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            var scheduler=typeof(PlayableAuthorityTick).GetField("scheduler",flags).GetValue(authority);
            var owner=((System.Collections.IEnumerable)scheduler.GetType().GetField("owners",flags).GetValue(scheduler)).Cast<object>().Single();
            var proposal=owner.GetType().GetMethod("Proposal",flags);
            var local=Site(4,10);var remote=Site(5,35);
            var captured=new[]{local,remote}.Select(s=>new TerritorySiteSnapshot(s.Site,PlayableOwner.Player,null,1,false,0,false)).ToArray();
            var raw=Observation(captured);var o=PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,raw.Seed,0,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,
                raw.Entities.ToArray(),raw.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:captured,publicScoutObjectives:raw.PublicScoutObjectives.ToArray(),activeProfile:P,homeSiteId:1,ownerId:config.Roster.Single(x=>x.Control==OfflineControl.Ai).Id));
            // Bind deterministic local preference via the public profile, preserving all gameplay prices/guards.
            owner.GetType().GetField("nativeProfile",flags).SetValue(owner,Profile(0));
            PlayableAiAction Build(int id)=>new PlayableAiAction(1,o.OwnerId,P.ProfileId,P.Revision,71,0,PlayableCommandKind.BuildAt,siteId:id,buildingKind:PlayableBuildingKind.Mine);
            Assert.Null(proposal.Invoke(owner,new object[]{"economy",Build(5),o,0}));
            Assert.NotNull(proposal.Invoke(owner,new object[]{"economy",Build(4),o,0}),"ordinary captured local mine still has a real admitted intent");
        }
        [Test] public void FinanceOpportunityKeepsChoiceAndOrdinalAcrossFundingAndMemoRestore()
        {
            var ai=Profile(1);var o=Observation(new[]{Site(4,10),Site(5,35)});var planner=new AiExpansionPlanner(o.OwnerId);
            var finance=new AiExpansionFinancial(o.OwnerId,o.Generation);var arbiter=new AiDecisionArbiter(ai);var ledger=new AiBudgetLedger(o.OwnerId,o.Generation,1350);
            var opening=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(o.Seed,o.OwnerId,forcedOpening:PlayableAiOpening.Safe),o.Generation);
            finance.InitializeNewMatch(P,ai,arbiter);ledger.ObserveBank(o.Credits);
            finance.Observe(o,P,ai,opening,ledger,arbiter,planner,null,0,true);
            Assert.AreEqual(5,finance.ActiveSite(arbiter));Assert.True(finance.RemotePreference(o,ai,arbiter));
            string memo=arbiter.SiteFundingMemory();
            using(var stream=new MemoryStream(Convert.FromBase64String(memo)))using(var reader=new BinaryReader(stream))
            {stream.Position=stream.Length-9;Assert.AreEqual(1,reader.ReadInt64(),"only one new opportunity draw");Assert.True(reader.ReadBoolean());}
            var copy=new AiDecisionArbiter(ai);copy.SaveSiteFundingMemory(memo);var restored=new AiExpansionFinancial(o.OwnerId,o.Generation);
            Assert.AreEqual(5,restored.ActiveSite(copy));Assert.True(restored.RemotePreference(o,ai,copy));
            var old=finance.Detach(ledger);finance.Observe(Observation(o.Sites.ToArray(),tick:10),P,ai,opening,ledger,arbiter,planner,old,0,true);
            Assert.AreEqual(memo,arbiter.SiteFundingMemory(),"waiting/funding is not a new opportunity");
        }
    }
}
