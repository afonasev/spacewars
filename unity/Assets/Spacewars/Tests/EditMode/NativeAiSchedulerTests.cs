using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiSchedulerTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private static readonly Type Loop=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop",true);
        private static readonly Type Scheduler=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.AiAuthorityScheduler",true);
        private static object Owner(string id,PlayableProfile profile,int seed)=>Activator.CreateInstance(Loop,Flags,null,new object[]{profile,PlayableAiOpeningComposition.Initialize(seed,id),71L,null},null);
        private static PlayableAiOwnerCheckpoint State(object loop)=>(PlayableAiOwnerCheckpoint)Loop.GetProperty("Checkpoint",Flags).GetValue(loop);
        private static object Coordinator(IEnumerable<object> loops)
        {var values=loops.ToArray();var array=Array.CreateInstance(Loop,values.Length);for(int i=0;i<values.Length;i++)array.SetValue(values[i],i);return Activator.CreateInstance(Scheduler,Flags,null,new object[]{array},null);}
        private static void Review(object scheduler,OfflineParticipantAuthority authority)=>Scheduler.GetMethod("Review",Flags).Invoke(scheduler,new object[]{OfflineParticipantAuthorityTests.Domain(authority),1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),authority.Configuration.Seed,new Func<string,long>(_=>0)});
        private static void Deliver(object scheduler,OfflineParticipantAuthority authority)=>Scheduler.GetMethod("Deliver",Flags).Invoke(scheduler,new object[]{OfflineParticipantAuthorityTests.Domain(authority),new Func<string,long>(_=>0),false});
        private static AiIntent Intent(string id,int money=0,int pop=0,string claim=null,int priority=0,long expires=10000,string conditions="same")=>new AiIntent(id,id,new PlayableAiAction(1,"owner-z","p",1,71,1,PlayableCommandKind.BuildAt),expires,money,pop,claim==null?Array.Empty<string>():new[]{claim},priority,1,conditions);

        [Test] public void SharedTick()
        {
            var config=OfflineParticipantAuthorityTests.Config(2,false);var ordinary=new OfflineParticipantAuthority(config,71);
            var cycle=new PlayableAuthorityTick(config,71);
            for(int i=0;i<40;i++){ordinary.Step(1d/30d);Assert.True(cycle.TryAdvance());}
            Assert.AreEqual(ordinary.Tick,cycle.Tick);
            Assert.AreEqual(PlayableAiCanonical.Encode(ordinary.View(config.Roster[0].Id).Entities),PlayableAiCanonical.Encode(cycle.Latest.Entities));
            Assert.AreEqual(ordinary.View(config.Roster[0].Id).Credits,cycle.Latest.Credits);
            long tick=cycle.Tick;for(int i=0;i<5;i++)Assert.True(cycle.TryAdvance(true));Assert.AreEqual(tick,cycle.Tick);
        }

        [Test] public void RouteBarrier()
        {
            var config=OfflineParticipantAuthorityTests.Config(2,false);var cycle=new PlayableAuthorityTick(config,71);
            int scout=cycle.Latest.Entities.Single().Id;
            var start=cycle.Latest.Entities.Single().Position;
            var goal=new[]{new NavPoint(start.X+4,start.Z),new NavPoint(start.X-4,start.Z),new NavPoint(start.X,start.Z+4),new NavPoint(start.X,start.Z-4)}.First(g=>cycle.NavigationGeometry.IsFree(g,config.Profile.ExplorerCollisionRadius)&&cycle.NavigationGeometry.SegmentFree(start,g,config.Profile.ExplorerCollisionRadius));
            var command=new PlayableCommand(71,1,config.Roster[0].Id,PlayableCommandKind.Move,new[]{scout},goal);
            Assert.AreEqual(PlayableCommandStatus.Applied,cycle.Apply(command).Status);
            Assert.True(cycle.Requests.TryDequeue(out var request));
            for(int i=0;i<20;i++)Assert.False(cycle.TryAdvance());
            Assert.Zero(cycle.Tick);Assert.Throws<InvalidOperationException>(()=>cycle.Apply(command));
            // An explicit route failure is terminal too; silence is never a successful solve.
            Assert.True(cycle.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>())));
            Assert.True(cycle.TryAdvance());Assert.AreEqual(1,cycle.Tick);
            Assert.AreEqual(NavigationOutcome.Unreachable,cycle.Latest.Entities.Single().NavigationOutcome);
            Assert.False(cycle.AwaitingRoutes);
        }

        [Test] public void RouteBarrierDrainsBoundedTransportAndRestoresPartialAnswers()
        {
            var geometry=new NavGeometry(300,Array.Empty<NavObstacle>(),1);var nav=new NavigationSession(71,geometry,PlayableProfile.Default.Navigation);
            var ready=typeof(NavigationSession).GetProperty("DeliveryBarrierReady",Flags);
            bool Ready()=>(bool)ready.GetValue(nav);
            for(int i=0;i<300;i++)
            {
                nav.Crowd.Add(i,new NavPoint(-250+(i%100)*4,-20+(i/100)*8));
                Assert.True(nav.Move(i,new NavPoint(-250+(i%100)*4,30+(i/100)*8)));
                Assert.True(nav.Requests.TryDequeue(out var request));Assert.True(nav.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal})));
                if(i%50==0)Assert.True(Ready()); // Every currently active job answered.
            }
            Assert.True(Ready());Assert.Zero(nav.Answers.Count);
            nav.Crowd.Add(300,new NavPoint(200,-20));Assert.True(nav.Move(300,new NavPoint(200,30)));Assert.False(Ready());
            var wire=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.WorldWire",true);
            NavigationSessionState saved;
            using(var stream=new System.IO.MemoryStream())
            {
                var write=wire.GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="Write"&&m.GetParameters()[1].ParameterType==typeof(NavigationSessionState));
                using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))write.Invoke(null,new object[]{writer,nav.CaptureState()});
                stream.Position=0;
                using(var reader=new System.IO.BinaryReader(stream,System.Text.Encoding.UTF8,true))saved=(NavigationSessionState)wire.GetMethod("ReadNavigationSessionState",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{reader});
            }
            var restored=new NavigationSession(71,geometry,PlayableProfile.Default.Navigation);restored.RestoreState(saved);
            Assert.False((bool)ready.GetValue(restored));Assert.True(restored.Requests.TryDequeue(out var last));
            Assert.True(restored.Answers.TryEnqueue(new NavigationAnswer(last,new[]{last.Goal})));
            Assert.True((bool)ready.GetValue(restored));restored.Step(1d/30d);Assert.AreEqual(301,restored.AppliedResults);
            Assert.True(restored.Crowd.Units.All(u=>u.Moving));
        }

        [Test] public void ReceiptIsolation()
        {
            var config=OfflineParticipantAuthorityTests.Config(3,false);var authority=new OfflineParticipantAuthority(config,71);
            var owners=config.Roster.Select(p=>Owner(p.Id,config.Profile,config.Seed)).ToArray();var scheduler=Coordinator(owners.Reverse());
            OfflineParticipantAuthorityTests.Step(authority,45);Review(scheduler,authority);
            var scheduled=owners.SelectMany(o=>State(o).Records).Where(r=>r.Status==PlayableAiDeliveryStatus.Scheduled).ToArray();
            Assert.GreaterOrEqual(scheduled.Length,3);
            Assert.AreEqual(scheduled.Length,scheduled.Select(r=>r.ActionId).Distinct().Count());
            Assert.AreEqual(scheduled.Length,scheduled.Select(r=>r.ReceiptIdentity.Id).Distinct().Count());
            Assert.True(scheduled.All(r=>r.ReceiptIdentity.Generation==71&&r.ReceiptIdentity.OwnerId==r.OwnerId&&r.ReceiptIdentity.DecisionOrdinal==1));
            OfflineParticipantAuthorityTests.Step(authority,30);Deliver(scheduler,authority);Deliver(scheduler,authority);
            var terminals=owners.SelectMany(o=>State(o).Records).Where(r=>r.Status!=PlayableAiDeliveryStatus.Scheduled).ToArray();
            Assert.AreEqual(scheduled.Length,terminals.Length);Assert.True(terminals.All(r=>r.Status==PlayableAiDeliveryStatus.Applied));
            Assert.AreEqual(terminals.Length,terminals.Select(r=>r.CommandSequence).Distinct().Count());
            foreach(var owner in owners){var state=State(owner);Assert.True(state.Records.All(r=>r.OwnerId==state.OwnerId));Assert.Zero(state.PendingActionId);}
        }

        [Test] public void ProposalOrderIndependence()
        {
            var set=new[]{Intent("production",60,2,"producer:1"),Intent("research",60,0,"producer:2"),Intent("scout",0,0,"unit:7"),Intent("competing-scout",0,0,"unit:7")};
            var forward=new AiDecisionArbiter(AiProfile.Initial).Select(set,0,100,2,3);
            var reverse=new AiDecisionArbiter(AiProfile.Initial).Select(set.Reverse(),0,100,2,3);
            CollectionAssert.AreEqual(forward.Select(x=>x.Id),reverse.Select(x=>x.Id));
            Assert.LessOrEqual(forward.Sum(x=>x.Credits),100);Assert.LessOrEqual(forward.Sum(x=>x.Population),2);
            Assert.AreEqual(forward.SelectMany(x=>x.Claims).Count(),forward.SelectMany(x=>x.Claims).Distinct().Count());
            Assert.LessOrEqual(forward.Count,3);
        }

        [Test] public void Starvation()
        {
            var arbiter=new AiDecisionArbiter(AiProfile.Initial);var production=Intent("production",50,1,"producer:1");
            var illegal=Intent("unavailable-building",1000,0,"slot:1");
            Assert.AreEqual("production",arbiter.Select(new[]{illegal,production},0,50,1,1).Single().Id);
            arbiter.Terminal(production,0,PlayableAiDeliveryStatus.Applied);
            var rejected=Intent("rejected-placement",0,0,"slot:2",priority:1);
            var limit=(int)AiProfile.Initial.Value("decision.retryLimit");var backoff=AiProfile.SecondsToTicks(AiProfile.Initial.Value("decision.retrySeconds"),30);
            for(int i=0;i<limit;i++)
            {
                long tick=i*backoff;Assert.AreEqual(rejected.Id,arbiter.Select(new[]{rejected,production},tick,50,1,1).Single().Id);
                arbiter.Terminal(rejected,tick,PlayableAiDeliveryStatus.Rejected);
                Assert.AreEqual(production.Id,arbiter.Select(new[]{rejected,production},tick+1,50,1,1).Single().Id);
            }
            Assert.AreEqual(production.Id,arbiter.Select(new[]{rejected,production},limit*backoff,50,1,1).Single().Id);
            var changed=Intent(rejected.Id,conditions:"placement-changed",priority:2);
            Assert.AreEqual(changed.Id,arbiter.Select(new[]{changed,production},limit*backoff,50,1,1).Single().Id);
        }

        [Test] public void RetryExhaustionSurvivesMissingProposalAndSequenceAllocationFollowsAuthority()
        {
            var arbiter=new AiDecisionArbiter(AiProfile.Initial);var rejected=Intent("placement");
            int limit=(int)AiProfile.Initial.Value("decision.retryLimit");long retry=AiProfile.SecondsToTicks(AiProfile.Initial.Value("decision.retrySeconds"),30);
            for(int i=0;i<limit;i++){Assert.AreEqual(1,arbiter.Select(new[]{rejected},i*retry,0,0,1).Count);arbiter.Terminal(rejected,i*retry,PlayableAiDeliveryStatus.Rejected);}
            Assert.IsEmpty(arbiter.Select(Array.Empty<AiIntent>(),limit*retry,0,0,1));
            Assert.IsEmpty(arbiter.Select(new[]{rejected},(limit+1)*retry,0,0,1));
            var authority=new OfflineParticipantAuthority(OfflineParticipantAuthorityTests.Config(2,false),71);var v=authority.View(authority.Configuration.Roster[0].Id);
            long external=1L<<62;var unit=v.Entities.Single(e=>e.Owner==v.Owner);
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(71,external,v.OwnerId,PlayableCommandKind.Stop,new[]{unit.Id})).Status);
            var domain=OfflineParticipantAuthorityTests.Domain(authority);var allocate=domain.GetType().GetMethod("AllocateAiSequence",Flags);
            Assert.AreEqual(external+1,(long)allocate.Invoke(domain,null));Assert.AreEqual(external+2,(long)allocate.Invoke(domain,null));
        }

        [Test] public void OfflineRuntimeUsesRosterScheduler()
        {
            var config=OfflineParticipantAuthorityTests.Config(6,false);
            var runtime=new PlayableRuntime(config,71,startPaused:true);
            try
            {
                var scheduler=typeof(PlayableRuntime).GetField("aiScheduler",Flags).GetValue(runtime);
                var owners=((System.Collections.IEnumerable)Scheduler.GetProperty("Owners",Flags).GetValue(scheduler)).Cast<object>().ToArray();
                CollectionAssert.AreEqual(config.Roster.Where(p=>p.Control==OfflineControl.Ai).Select(p=>p.Id).OrderBy(x=>x,StringComparer.Ordinal),owners.Select(o=>State(o).OwnerId));
                Assert.IsEmpty(owners.SelectMany(o=>State(o).Records));
                runtime.RequestPause(false);var deadline=DateTime.UtcNow.AddSeconds(7);
                while(DateTime.UtcNow<deadline&&!owners.All(o=>State(o).Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied)))System.Threading.Thread.Sleep(20);
                Assert.True(owners.All(o=>State(o).Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied)),runtime.Latest.Failure);
                Assert.AreEqual(0,runtime.Latest.Metrics.Errors);
            }
            finally{runtime.RequestStop();var deadline=DateTime.UtcNow.AddSeconds(3);while(!runtime.IsStopped&&DateTime.UtcNow<deadline)System.Threading.Thread.Sleep(10);Assert.True(runtime.IsStopped);}
        }

        [Test] public void NoFreeSlot()
        {
            var authority=new OfflineParticipantAuthority(OfflineParticipantAuthorityTests.Config(2,false),71);
            var v=authority.View(authority.Configuration.Roster[0].Id);var home=v.Sites.Single(x=>x.Site.Id==v.HomeSiteId);
            // Fill every slot with a different useful building, leaving no Factory/Refinery.
            var extra=home.Site.Slots.Select((slot,i)=>new PlayableBuildingSnapshot(900+i,v.Owner,PlayableBuildingKind.ScientificCenter,slot.Position,100,1,0,0,default(NavPoint),home.Site.Id,slot.Id,home.CenterId,ConstructionPhase.Ready,null,0)).ToArray();
            var snapshot=new PlayableSnapshot(v.ProfileId,v.ProfileRevision,71,v.Seed,1,45,RuntimeStatus.Running,false,v.Outcome,v.Credits,v.Geometry,v.Entities.ToArray(),v.Buildings.Concat(extra).ToArray(),Array.Empty<PlayableProjectileSnapshot>(),v.Metrics,null,sites:v.Sites.ToArray(),population:v.Population,owner:v.Owner,ownerId:v.OwnerId,team:v.Team,participants:v.Participants.ToArray(),homeSiteId:v.HomeSiteId);
            var policy=new PlayableAiEconomicLivenessPolicy(ownerId:v.OwnerId);
            Assert.IsNull(policy.TryPlan(PlayableAiObservation.From(snapshot)));Assert.False(policy.HasPendingObligation);
            // Invalid provenance still fails visibly; the empty slot case is not a catch-all.
            Assert.Throws<ArgumentException>(()=>new PlayableAiEconomicLivenessPolicy(ownerId:"foreign").TryPlan(PlayableAiObservation.From(snapshot)));
        }

        [Test] public void UnselectedPolicyLeavesNoPendingAndAffordableProductionWins()
        {
            var authority=new OfflineParticipantAuthority(OfflineParticipantAuthorityTests.Config(2,false),71);
            var v=authority.View(authority.Configuration.Roster[0].Id);var profile=authority.Configuration.Profile;var home=v.Sites.Single(x=>x.Site.Id==v.HomeSiteId);
            var slot=home.Site.Slots.First();var factory=new PlayableBuildingSnapshot(900,v.Owner,PlayableBuildingKind.Factory,slot.Position,100,1,0,0,default(NavPoint),home.Site.Id,slot.Id,home.CenterId);
            PlayableSnapshot Snapshot(long tick)=>new PlayableSnapshot(v.ProfileId,v.ProfileRevision,71,v.Seed,tick,tick,RuntimeStatus.Running,false,v.Outcome,profile.TankCreditCost,v.Geometry,v.Entities.ToArray(),v.Buildings.Concat(new[]{factory}).ToArray(),Array.Empty<PlayableProjectileSnapshot>(),v.Metrics,null,sites:v.Sites.ToArray(),population:v.Population,owner:v.Owner,ownerId:v.OwnerId,team:v.Team,participants:v.Participants.ToArray(),homeSiteId:v.HomeSiteId);
            var owner=Owner(v.OwnerId,profile,v.Seed);
            Loop.GetMethod("Review",Flags).Invoke(owner,new object[]{Snapshot(45),0L});
            var scheduled=State(owner).Records.Where(r=>r.Status==PlayableAiDeliveryStatus.Scheduled).ToArray();
            Assert.AreEqual(1,scheduled.Length);Assert.AreEqual("production",scheduled[0].Policy);
            Assert.AreEqual(PlayableCommandKind.QueueTank,scheduled[0].Kind);
            var economy=(PlayableAiEconomicLivenessPolicy)Loop.GetField("economy",Flags).GetValue(owner);Assert.False(economy.HasPendingObligation);
            Loop.GetMethod("Deliver",Flags).Invoke(owner,new object[]{OfflineParticipantAuthorityTests.Domain(authority),1L,false});
            Assert.AreEqual(1,State(owner).Records.Count(r=>r.Status==PlayableAiDeliveryStatus.Cancelled));
            Loop.GetMethod("Review",Flags).Invoke(owner,new object[]{Snapshot(90),1L});
            Assert.AreEqual(2,State(owner).Records.Count(r=>r.Status==PlayableAiDeliveryStatus.Scheduled&&r.Policy=="production"));
            Assert.False(economy.HasPendingObligation);
        }

        [Test] public void AgingAndExpirationDoNotAdmitAnIllegalOrNoOpProposal()
        {
            var arbiter=new AiDecisionArbiter(AiProfile.Initial);var waiting=Intent("production",50,1,"producer:1");
            arbiter.Select(new[]{waiting},0,0,0,1);
            long age=AiProfile.SecondsToTicks(AiProfile.Initial.Value("decision.intentAgingSeconds"),30);
            Assert.AreEqual(waiting.Id,arbiter.Select(new[]{Intent("micro",priority:1),waiting},age,50,1,1).Single().Id);
            Assert.IsEmpty(arbiter.Select(new[]{Intent("expired",expires:age-1),Intent("too-expensive",money:1000)},age,50,1,2));
            Assert.Throws<ArgumentException>(()=>arbiter.Select(new[]{waiting,waiting},age,50,1,1));
        }
    }
}
