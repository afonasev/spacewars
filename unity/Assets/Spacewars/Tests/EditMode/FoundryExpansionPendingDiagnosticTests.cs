using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Newtonsoft.Json;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class FoundryExpansionPendingDiagnosticTests
    {
        private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static string Fixture(string name)
        {
            var root = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (root != null && !File.Exists(Path.Combine(root.FullName,"workflow/project.json"))) root=root.Parent;
            Assert.NotNull(root);
            return File.ReadAllText(Path.Combine(root.FullName,"unity/Tests/Fixtures/Foundry/"+name));
        }

        [Test]
        public void ActualExpansionProposalAndReceiptMustRetainPendingProgressAcrossCooldowns()
        {
            var p=PlayableProfile.Create(JsonConvert.DeserializeObject<PlayableProfileData>(Fixture("captured-seed7-gameplay-profile.json")),new FoundryMap(new FoundryProfileData()));
            var config=NativeLobbyMatch.Create(p,JsonConvert.DeserializeObject<NativeLobbyConfiguration>(Fixture("captured-seed7-lobby.json")));
            var domain=new PlayableDomain(p,1,config);var nav=domain.Navigation;var kernel=new CertifiedRouteKernel();
            var ai=NativeBalanceFields.InitialAiOpeningProfile();
            var loop=new PlayableAiOwnerLoop(p,PlayableAiOpeningComposition.Initialize(7,"foundry-5",aiProfile:ai),1,null,ai,AiDifficulty.Fighter);
            var planner=(AiExpansionPlanner)typeof(PlayableAiOwnerLoop).GetField("expansion",F).GetValue(loop);
            var proposal=typeof(PlayableAiOwnerLoop).GetMethod("Proposal",F);
            var owner=(PlayableOwner)4;var target=new NavPoint(2.42,71);long sequence=1L<<61;
            try {
                var mine=config.Sites.Single(s=>s.Id==12);var home=config.Sites.Single(s=>s.Id==3);
                var initial=domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
                var scout=initial.Entities.Single(u=>u.Id==11);Assert.AreEqual(new NavPoint(44,96),scout.Position);
                NavPoint[] path;
                using(var router=new UnityNavigationRouter(domain.Geometry,p.Navigation.ForUnit(.58,PlayableUnitRules.Speed(p,scout.Kind),p.Navigation.TankTurnSpeed)))
                    path=router.FindPath(scout.Position,target).ToArray();
                Assert.IsNotEmpty(path);
                void AdvanceTo(long tick) { while(domain.Tick<tick)domain.Step(1d/30);Assert.AreEqual(tick,domain.Tick); }
                // Controlled detached policy observations, with real map/profile,
                // real native proof and real live queue; not a historical replay.
                PlayableAiObservation Observation(long tick) {
                    Assert.AreEqual(tick,domain.Tick,"Policy observation uses actual authority tick, not a rewritten clock.");
                    var actual=domain.PlayerSnapshot(tick,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
                    var actor=actual.Entities.Single(u=>u.Id==11);
                    return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,1,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,initial.Credits,domain.Geometry,
                        new[]{actor},actual.Buildings.Where(b=>b.Owner==owner).ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                        sites:new[]{new TerritorySiteSnapshot(home,owner,owner,1,false,home.Id,true),new TerritorySiteSnapshot(mine,null,null,0,false,0,false)},
                        publicScoutObjectives:new[]{new PlayablePublicScoutObjective(12,target,PlayablePublicScoutObjectiveRole.ExpansionIncome,true)},
                        routeProofs:new[]{new PlayableRouteProof(11,owner,1,tick,1,actor.Position,.58,PlayableRouteTargetKind.PublicObjective,12,target,target,path)},
                        owner:owner,ownerId:"foundry-5",homeSiteId:3,team:2,participants:config.Roster.ToArray(),activeProfile:p));
                }
                AiIntent Admit(PlayableAiAction action,PlayableAiObservation observation)=>(AiIntent)proposal.Invoke(loop,new object[]{"expansion",action,observation,1});
                void Apply(PlayableAiAction action,long tick) {
                    var command=new PlayableCommand(1,++sequence,"foundry-5",action.Kind,action.CopyEntityIds(),action.Target,siteId:action.SiteId,
                        origin:PlayableOrderOrigin.Ai,source:action.SourceIdentity,jobId:1,actionId:action.ActionId);
                    var status=domain.Apply(command,out var message);Assert.AreEqual(PlayableCommandStatus.Applied,status,message);
                    planner.ObserveReceipt(new PlayableAiTraceRecord("fixture",action.ActionId,tick,sequence,tick,PlayableAiDeliveryStatus.Applied,status,message,
                        "foundry-5",action.SourceIdentity,new AiReceiptIdentity(1,"foundry-5",1,1),"expansion",action.Kind));
                }
                AdvanceTo(630);var firstObservation=Observation(630);var first=planner.TryPlan(firstObservation,p,ai);
                Assert.NotNull(first,"Reach actual expansion planner selection.");Assert.AreEqual(12,first.SiteId);Assert.AreEqual(target,first.Target);
                Assert.NotNull(Admit(first,firstObservation),"Reach actual owner admission before the initial command.");
                AdvanceTo(660);Apply(first,660);Assert.Zero(planner.PendingId,"Applied receipt releases action callback, while spatial queue remains pending.");
                long original=nav.PendingRequestFor(11).Request;int admittedRepeats=0;long paidProgress=0;
                foreach(long tick in new[]{750L,840L,930L}) {
                    AdvanceTo(tick);
                    var binding=new PlayableRouteBinding(domain.Geometry,p,nav.Admission,nav.RoutePort);
                    kernel.Pump(binding,1);nav.ApplyResults();
                    var root=nav.RoutePort.PinCommittedHostRoot();
                    try { if(root!=null)foreach(var entry in root.ArrivalWork) {
                        var work=entry.Value as NavigationArrivalWork;
                        if(work!=null)paidProgress+=work.AllocationWorkConsumed+work.DirectWorkConsumed+work.RouteCounters.ConnectorWork;
                    }} finally {root?.Release();}
                    Assert.NotNull(nav.PendingRequestFor(11),"Fixed bounded grants must leave the actual request pending for this causal check.");
                    var observation=Observation(tick);planner.Observe(observation,p,ai);
                    var speculative=planner.Fork();
                    var repeated=speculative.TryPlan(observation,p,ai);var admitted=repeated==null?null:Admit(repeated,observation);
                    TestContext.WriteLine("EXPANSION_COOLDOWN tick="+tick+" pendingBefore="+nav.PendingRequestFor(11).Request+" plannerRepeated="+(repeated!=null)+" ownerAdmitted="+(admitted!=null)+" cumulativePaidProgress="+paidProgress);
                    if(admitted!=null){admittedRepeats++;planner=speculative;typeof(PlayableAiOwnerLoop).GetField("expansion",F).SetValue(loop,planner);Apply(admitted.Action,tick);}
                }
                TestContext.WriteLine("EXPANSION_REPLACEMENT original="+original+" final="+nav.PendingRequestFor(11)?.Request+" admittedRepeats="+admittedRepeats+" paidProgress="+paidProgress);
                Assert.Greater(paidProgress,0,"Reach real paid pending arrival work before evaluating preservation.");
                Assert.Zero(admittedRepeats,"Actual expansion planner/receipt/owner admission must not reissue the same live pending Move after cooldown.");
                Assert.AreEqual(original,nav.PendingRequestFor(11).Request,"Same pending request and its advancing work must survive.");
            } finally {
                loop.Stop();kernel.Clear();nav.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();
                Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);
            }
        }

        [Test]
        public void ActualFifthExpansionPendingMoveMustCoalesceBeforeReplacingItsRequest()
        {
            var profile=PlayableProfile.Create(JsonConvert.DeserializeObject<PlayableProfileData>(Fixture("captured-seed7-gameplay-profile.json")),new FoundryMap(new FoundryProfileData()));
            var lobby=JsonConvert.DeserializeObject<NativeLobbyConfiguration>(Fixture("captured-seed7-lobby.json"));
            var config=NativeLobbyMatch.Create(profile,lobby);
            var domain=new PlayableDomain(profile,1,config); var navigation=domain.Navigation;
            try {
                var owner=(PlayableOwner)4; var origin=new NavPoint(44,96); var target=new NavPoint(2.42,71);
                var before=domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
                var actor=before.Entities.Single(u=>u.Owner==owner&&u.Kind==PlayableEntityKind.Explorer);
                Assert.AreEqual(11,actor.Id); Assert.AreEqual(origin,actor.Position);Assert.AreEqual(.58,PlayableUnitRules.Radius(profile,actor.Kind));
                // Exact observed Fifth recipient/target/profile, a newly created
                // real domain queue. Historical tick660 internals were not saved.
                var command=new PlayableCommand(1,1L<<61,"foundry-5",PlayableCommandKind.Move,new[]{11},target,siteId:12,
                    origin:PlayableOrderOrigin.Ai,source:PlayableAiOpeningComposition.SourceIdentity,jobId:1,actionId:1);
                Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(command,out var message),message);
                var view=domain.PlayerSnapshot(2,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
                var pendingActor=view.Entities.Single(u=>u.Id==11); var pending=pendingActor.Queue?.Pending;
                Assert.NotNull(pending);Assert.AreEqual(PlayableCommandKind.Move,pending.Kind);Assert.AreEqual(target,pending.Anchor.Value.Position);
                Assert.Null(pendingActor.CurrentOrder);Assert.False(pendingActor.Moving);
                var request=navigation.PendingRequestFor(11);Assert.NotNull(request);
                var sameOrder=typeof(PlayableAiOwnerLoop).GetMethod("SameOrder",BindingFlags.Static|BindingFlags.NonPublic);
                bool Same(PlayableAiAction action,PlayableEntitySnapshot unit)=>(bool)sameOrder.Invoke(null,new object[]{action,unit,"expansion",false,"foundry-5",1L});
                PlayableAiAction Proposal(NavPoint point,PlayableCommandKind kind=PlayableCommandKind.Move,int site=12,string player="foundry-5",long generation=1,int unit=11,string source=null)=>new PlayableAiAction(2,player,
                    profile.ProfileId,profile.Revision,generation,2,kind,new[]{unit},point,siteId:site,seed:7,sourceIdentity:source??PlayableAiOpeningComposition.SourceIdentity);
                var terminal=new PlayableEntitySnapshot(11,owner,actor.Kind,origin,actor.Health,false,0,0,0,
                    queue:new PlayableUnitQueueSnapshot(null,pending,Array.Empty<PlayableQueuedOrderSnapshot>(),null,false));
                Assert.False(Same(Proposal(new NavPoint(3.42,71)),pendingActor),"Changed target remains eligible.");
                Assert.False(Same(Proposal(target,PlayableCommandKind.AttackMove),pendingActor),"Changed kind remains eligible.");
                Assert.False(Same(Proposal(target,site:13),pendingActor),"Changed public site remains eligible even at the same target point.");
                Assert.False(Same(Proposal(target,player:"player-1"),pendingActor),"Foreign owner cannot match.");
                Assert.False(Same(Proposal(target,generation:2),pendingActor),"Foreign generation cannot match.");
                Assert.False(Same(Proposal(target,unit:12),pendingActor),"Another recipient cannot match.");
                Assert.False(Same(Proposal(target,source:"foreign"),pendingActor),"Foreign source cannot match.");
                Assert.False(Same(Proposal(target),terminal),"Terminal/unbound pending remains eligible for a fresh decision.");
                TestContext.WriteLine("FIFTH_PENDING source=fresh-owned-queue/captured-profile-not-historical-state owner=foundry-5 unit=11 site=12 request="+
                    request.Request+" order="+request.Order+" pendingViable="+pendingActor.Queue.PendingAiRouteViable+" sameExpansion="+Same(Proposal(target),pendingActor));
                Assert.AreSame(request,navigation.PendingRequestFor(11),"Observation/admission check must not restart pending work.");
                Assert.True(pendingActor.Queue.PendingAiRouteViable&&Same(Proposal(target),pendingActor),
                    "Exact live pending expansion must be viable and coalesced; viable="+pendingActor.Queue.PendingAiRouteViable+" same="+Same(Proposal(target),pendingActor));
            } finally {
                domain.DestroyOwnedNavigation();
                Assert.Zero(navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(navigation.PhysicalHostBudget.HeldBytes);Assert.Zero(navigation.PhysicalHostBudget.ActiveScopes);
            }
        }
    }
}
