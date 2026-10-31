using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using NUnit.Framework;
using Unity.Profiling;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Input.Tests
{
    public sealed class AiScoutQueryAllocationTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static AiArmyRegistry Registry(int history)
        {
            var armies=new List<AiArmyState>();
            for(int i=0;i<history+2;i++){
                bool active=i>=history;int first=(i-history)*3+1;
                armies.Add(new AiArmyState{Id=i+1,OwnerId="player-1",TacticalOwner="army",Role=AiArmyRole.Attack,
                    Phase=active?AiArmyPhase.Advancing:AiArmyPhase.Disbanded,Members=active?Enumerable.Range(first,3).ToArray():Array.Empty<int>(),
                    Reinforcements=active?new[]{first+2}:Array.Empty<int>(),LeaderId=active?first:0,MinimumViableForce=3,CreatedTick=1,ProgressTick=10,
                    Anchor=new NavPoint(0,0),Rally=new NavPoint(8,8)});
            }
            return AiArmyRegistry.Restore(new AiArmyRegistryState{OwnerId="player-1",Generation=71,NextId=history+3,Armies=armies.ToArray()},AiProfile.Initial,AiDifficulty.Fighter,10);
        }
        static PlayableSnapshot ScoutSnapshot(bool loseScout=false)
        {
            var p=PlayableProfile.Default;
            var entities=Enumerable.Range(1,9).Where(id=>!loseScout||id!=7).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,id<=6?PlayableEntityKind.Tank:PlayableEntityKind.Explorer,new NavPoint(id,0),100,false,0,0,0)).ToArray();
            return new PlayableSnapshot(p.ProfileId,p.Revision,71,41,1,10,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,100,new NavGeometry(20,Array.Empty<NavObstacle>(),1),entities,Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,ownerId:"player-1");
        }
        static byte[] RegistryBytes(AiArmyRegistry r)
        {using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))r.WriteState(writer);return stream.ToArray();}
        static bool DetachedScout(AiArmyRegistry r,int id)=>r.Capture().Armies.Any(a=>a.Role==AiArmyRole.Scout&&a.Members.Contains(id));
        static long AllocationEventsOnCurrentThread(Func<int> body,out int result,out long rawValue)
        {
            // Count records GC.Alloc occurrences; Value is marker duration, not
            // allocation metadata bytes. No frame sum/wrap; require complete samples.
            const int capacity=1<<20;
            using var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC.Alloc",capacity,ProfilerRecorderOptions.StartImmediately|ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            if(!recorder.Valid)throw new InvalidOperationException("GC.Alloc recorder is unavailable.");
            result=body();recorder.Stop();long events=0;rawValue=0;int count=recorder.Count;
            for(int i=0;i<count;i++){var sample=recorder.GetSample(i);rawValue+=sample.Value;events+=sample.Count;}
            Assert.Less(count,capacity,"Allocation samples must not be truncated.");return events;
        }
        static void CalibrateAllocationRecorder()
        {
            long events=AllocationEventsOnCurrentThread(()=>{var allocated=new byte[1024];GC.KeepAlive(allocated);return allocated.Length;},out int length,out long rawValue);
            TestContext.WriteLine($"SCOUT_ALLOCATION_CONTROL array_length={length} events={events} raw_marker_value={rawValue} byte_amount=unavailable");
            Assert.AreEqual(1024,length);Assert.AreEqual(1,events,"One actual array allocation must produce exactly one GC.Alloc occurrence.");
        }
        [Test] public void ActualReviewAndActiveArmyCallersReportHistoryCopyCost()
        {
            CalibrateAllocationRecorder();
            foreach(int history in new[]{0,64}){
                var state=Registry(history).Capture();
                foreach(var army in state.Armies){army.TacticalOwner=AiArmyPlanner.Policy;if(army.Phase!=AiArmyPhase.Disbanded)army.Phase=AiArmyPhase.Retreating;}
                var r=AiArmyRegistry.Restore(state,AiProfile.Initial,AiDifficulty.Fighter,10);
                var o=PlayableAiObservation.From(ScoutSnapshot());var p=PlayableProfile.Default;
                var planner=new AiArmyPlanner("player-1",71);
                typeof(AiArmyPlanner).GetField("state",F).SetValue(planner,new AiArmyMissionState{ArmyId=history+1,StartedTick=1,ProgressTick=10,PhaseTick=10,TargetId=1,TargetKind=PlayableRouteTargetKind.PublicObjective,Target=new NavPoint(8,8),Phase=AiArmyPhase.Retreating,Reason="withdrawal"});
                void Planner()=>planner.Observe(o,r,AiProfile.Initial,replanQuietGuards:false);
                void Tactical()=>AiTacticalExecutor.Observe(o,r,history+1,p);
                Planner();Tactical();Assert.True(planner.Active);r.AssertInvariants();
                var before=RegistryBytes(r);const int calls=128;
                long plannerEvents=AllocationEventsOnCurrentThread(()=>{for(int i=0;i<calls;i++)Planner();return calls;},out int plannerCalls,out _);
                long tacticalEvents=AllocationEventsOnCurrentThread(()=>{for(int i=0;i<calls;i++)Tactical();return calls;},out int tacticalCalls,out _);
                Assert.AreEqual(calls,plannerCalls);Assert.AreEqual(calls,tacticalCalls);Assert.True(planner.Active);
                CollectionAssert.AreEqual(before,RegistryBytes(r),"Actual active callers must preserve this settled retreat fixture.");
                var loop=new PlayableAiOwnerLoop(p,PlayableAiOpeningComposition.Initialize(41,"player-1"),71);
                long reviewEvents;
                try{
                    loop.Review(ScoutSnapshot(),0);
                    var owned=(AiArmyRegistry)typeof(PlayableAiOwnerLoop).GetField("armies",F).GetValue(loop);
                    var ownedState=owned.Capture();long first=ownedState.NextId;
                    ownedState.Armies=ownedState.Armies.Concat(Enumerable.Range(0,history).Select(i=>new AiArmyState{Id=first+i,OwnerId="player-1",TacticalOwner=AiArmyPlanner.Policy,Role=AiArmyRole.Attack,Phase=AiArmyPhase.Disbanded,MinimumViableForce=3,CreatedTick=1,ProgressTick=10})).ToArray();
                    ownedState.NextId=first+history;
                    owned=AiArmyRegistry.Restore(ownedState,AiProfile.Initial,AiDifficulty.Fighter,10);
                    typeof(PlayableAiOwnerLoop).GetField("armies",F).SetValue(loop,owned);
                    var snapshot=ScoutSnapshot();loop.Review(snapshot,0);owned.AssertInvariants();
                    reviewEvents=AllocationEventsOnCurrentThread(()=>{for(int i=0;i<16;i++)loop.Review(snapshot,0);return 16;},out int reviewCalls,out _);
                    Assert.AreEqual(16,reviewCalls);owned.AssertInvariants();
                }finally{loop.Stop();}
                TestContext.WriteLine($"ACTUAL_ARMY_CALLER_COST history={history} active=2 calls={calls} planner_events={plannerEvents} tactical_events={tacticalEvents} owner_review_calls=16 owner_review_events={reviewEvents} byte_amount=unavailable timing=not_measured");
                Assert.Greater(plannerEvents,0);Assert.Greater(tacticalEvents,0);Assert.Greater(reviewEvents,0);
            }
        }
        [Test] public void ActualOwnerReviewKeepsSelectedActiveArmyAndDetachedRegistryState()
        {
            var loop=new PlayableAiOwnerLoop(PlayableProfile.Default,PlayableAiOpeningComposition.Initialize(41,"player-1"),71);
            try{
                // This settled-retreat control has no free reinforcement actors.
                // The separate mutation/provenance controls exercise fresh admissions.
                var p=PlayableProfile.Default;
                var snapshot=new PlayableSnapshot(p.ProfileId,p.Revision,71,41,1,10,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,100,new NavGeometry(20,Array.Empty<NavObstacle>(),1),ScoutSnapshot().Entities.Where(u=>u.Id<=6).ToArray(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,ownerId:"player-1");
                loop.Review(snapshot,0);
                const int history=64;long id=history+1;var state=Registry(history).Capture();
                foreach(var army in state.Armies){
                    army.TacticalOwner=AiArmyPlanner.Policy;
                    if(army.Phase!=AiArmyPhase.Disbanded){army.Phase=AiArmyPhase.Retreating;army.Objective=((int)PlayableRouteTargetKind.PublicObjective)+":1";army.Rally=new NavPoint(8,8);}
                }
                var registry=AiArmyRegistry.Restore(state,AiProfile.Initial,AiDifficulty.Fighter,10);
                typeof(PlayableAiOwnerLoop).GetField("armies",F).SetValue(loop,registry);
                var planner=(AiArmyPlanner)typeof(PlayableAiOwnerLoop).GetField("armyPlanner",F).GetValue(loop);
                typeof(AiArmyPlanner).GetField("state",F).SetValue(planner,new AiArmyMissionState{ArmyId=id,StartedTick=1,ProgressTick=10,PhaseTick=10,TargetId=1,TargetKind=PlayableRouteTargetKind.PublicObjective,Target=new NavPoint(8,8),Phase=AiArmyPhase.Retreating,Reason="withdrawal"});
                for(int i=0;i<3;i++){
                    loop.Review(snapshot,0);Assert.True(planner.Active);Assert.AreEqual(id,planner.Capture().ArmyId);registry.AssertInvariants();
                    var selected=registry.CaptureArmy(id).Single();Assert.AreEqual(AiArmyPhase.Retreating,selected.Phase);
                    CollectionAssert.AreEqual(new[]{1,2,3},selected.Members);CollectionAssert.AreEqual(new[]{3},selected.Reinforcements);
                    var bytes=RegistryBytes(registry);selected.Members[0]=-1;selected.Reinforcements[0]=-1;selected.TacticalOwner="foreign";
                    CollectionAssert.AreEqual(bytes,RegistryBytes(registry));
                    using var stream=new MemoryStream(bytes);using var reader=new BinaryReader(stream);
                    var cold=AiArmyRegistry.ReadState(reader,AiProfile.Initial,AiDifficulty.Fighter,10,"player-1",71);CollectionAssert.AreEqual(bytes,RegistryBytes(cold));
                }
                TestContext.WriteLine("ACTUAL_OWNER_ACTIVE_REVIEW calls=3 history=64 army=65 selected_members=1,2,3 reinforcements=3 CURRENT_registry_cold_exact=true no_FPS_claim");
            }finally{loop.Stop();}
        }
        [Test] public void ScoutMembershipRemovesDeepCaptureAllocationsWithoutTimingThreshold()
        {
            var state=Registry(64).Capture();state.Armies[64].Role=AiArmyRole.Scout;
            var r=AiArmyRegistry.Restore(state,AiProfile.Initial,AiDifficulty.Fighter,10);const int calls=4096;
            for(int id=-1;id<10;id++){r.IsScoutMember(id);DetachedScout(r,id);}
            CalibrateAllocationRecorder();
            long freshEvents=AllocationEventsOnCurrentThread(()=>{int hits=0;for(int i=0;i<calls;i++)if(r.IsScoutMember(i%9))hits++;return hits;},out int fresh,out long freshRaw);
            long detachedEvents=AllocationEventsOnCurrentThread(()=>{int hits=0;for(int i=0;i<calls;i++)if(DetachedScout(r,i%9))hits++;return hits;},out int detached,out long detachedRaw);
            TestContext.WriteLine($"SCOUT_QUERY_ALLOC calls={calls} historical=64 actual_hits={fresh} detached_events={detachedEvents} fresh_events={freshEvents} detached_raw_marker={detachedRaw} fresh_raw_marker={freshRaw} byte_amount=unavailable");
            Assert.AreEqual(detached,fresh);Assert.Greater(fresh,0,"Both branches must exercise a real scout hit.");Assert.Zero(freshEvents);Assert.Greater(detachedEvents,0,"The allocating control must establish a working allocation event counter.");
        }
        [Test] public void ActualOwnerScoutCallbacksStayFreshAndAllocateNoPerUnitCaptures()
        {
            var loop=new PlayableAiOwnerLoop(PlayableProfile.Default,PlayableAiOpeningComposition.Initialize(41,"player-1"),71);
            T Get<T>(string name)=>(T)typeof(PlayableAiOwnerLoop).GetField(name,F).GetValue(loop);
            try{
                loop.Review(ScoutSnapshot(),0);
                var r=Get<AiArmyRegistry>("armies");var doctrine=Get<AiDoctrinePolicy>("doctrine");var opening=Get<AiOpeningExecutor>("openingExecutor");var utility=Get<HashSet<int>>("utilityReserved");var expansion=Get<AiExpansionPlanner>("expansion");var o=PlayableAiObservation.From(ScoutSnapshot());
                Assert.NotNull(doctrine.CombatCountable);Assert.NotNull(doctrine.UtilityAvailable);Assert.NotNull(opening.CombatAvailable);
                void Check()
                {
                    var bytes=RegistryBytes(r);
                    for(int id=-1;id<=12;id++){
                        bool scout=DetachedScout(r,id),combat=!utility.Contains(id)&&!expansion.ClaimsActor(id)&&!scout;
                        Assert.AreEqual(combat,doctrine.CombatCountable(id));Assert.AreEqual(r.ArmyFor(id)==0||scout,doctrine.UtilityAvailable(id));Assert.AreEqual(combat,opening.CombatAvailable(id));
                    }
                    CollectionAssert.AreEqual(bytes,RegistryBytes(r));
                }
                r.DisbandAll();Check();Assert.True(r.TryCreate(o,AiArmyRole.Scout,AiScoutPlanner.Policy,new[]{7,8},out var scout));Check();
                Assert.True(r.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var attack));Assert.True(r.TryTransfer(o,scout,attack,new[]{8}));Check();
                const int calls=4096;CalibrateAllocationRecorder();
                long events=AllocationEventsOnCurrentThread(()=>{int hits=0;for(int i=0;i<calls;i++){int id=i%9;if(doctrine.CombatCountable(id))hits++;if(doctrine.UtilityAvailable(id))hits++;if(opening.CombatAvailable(id))hits++;}return hits;},out int yes,out long rawValue);
                TestContext.WriteLine($"ACTUAL_SCOUT_CALLBACK_ALLOC calls={calls*3} events={events} raw_marker_value={rawValue} byte_amount=unavailable");Assert.Greater(yes,0);Assert.Zero(events);
                r.Disband(scout);Check();r.Disband(attack);Check();
            }finally{loop.Stop();}
        }
    }
}
