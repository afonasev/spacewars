using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class CombatMicroAttentionTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static object Get(object o,string key)=>o.GetType().GetField(key,F).GetValue(o);
        static void Set(object o,string key,object value)=>o.GetType().GetField(key,F).SetValue(o,value);
        [TestCase(AiDifficulty.Recruit)][TestCase(AiDifficulty.Fighter)][TestCase(AiDifficulty.Veteran)]
        public void TemporalLimitBurstIdlePauseFairnessAndExactStateAreOwnerWide(AiDifficulty difficulty)
        {
            var ai=AiProfile.Initial;var budget=new AiCombatMicroAttention();long delivered=0;
            int burst=(int)ai.DifficultyValue(difficulty,"control.burst");long rate=(long)ai.DifficultyValue(difficulty,"control.ordersPerMinute");
            for(int tick=0;tick<=1800;tick++)
            {
                // Several independent map fronts and macro clients ask in the same
                // tick. The one shared limiter never multiplies its capacity.
                for(int client=0;client<12;client++)if(budget.Allows(tick,ai,difficulty,client%2==0,false))
                {budget.Charge(tick,ai,difficulty,client%2==0);delivered++;}
                Assert.LessOrEqual(delivered,burst+(tick*rate)/1800);
            }
            Assert.AreEqual(burst+rate,delivered);
            long now=180000;budget.Advance(now,ai,difficulty);long before=budget.Credit;
            for(int wall=0;wall<1000;wall++)budget.Advance(now,ai,difficulty);
            Assert.AreEqual(before,budget.Credit,"Paused simulation time does not refill attention");
            int count=0;while(budget.Allows(now,ai,difficulty,false,false)){budget.Charge(now,ai,difficulty,false);count++;}Assert.AreEqual(burst,count,"Long idle never banks an unbounded series");
            byte[] Encode(AiCombatMicroAttention value){using(var s=new MemoryStream()){using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))value.Write(w);return s.ToArray();}}
            AiCombatMicroAttention restored;using(var r=new BinaryReader(new MemoryStream(Encode(budget))))restored=AiCombatMicroAttention.Read(r,now,ai,difficulty);
            CollectionAssert.AreEqual(Encode(budget),Encode(restored));
            for(long tick=now;tick<now+300;tick++)Assert.AreEqual(budget.Allows(tick,ai,difficulty,true,false),restored.Allows(tick,ai,difficulty,true,false));
        }
        [Test]
        public void AQueuedMicroAtTheFrontCannotHideAReadyProductionOrderBehindIt()
        {
            const long gen=901;var p=PlayableProfile.Default;var ai=AiProfile.Initial;var d=new PlayableDomain(p,gen,false);
            object Call(string method,params object[] args)=>typeof(PlayableDomain).GetMethod(method,F).Invoke(d,args);
            try
            {
                int actor=(int)Call("SpawnUnit",new NavPoint(-16,15),PlayableOwner.Player,PlayableEntityKind.Tank);
                int enemy=(int)Call("SpawnUnit",new NavPoint(-7,15),PlayableOwner.Enemy,PlayableEntityKind.Tank);
                Call("AddBuilding",PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(-24,20),true);
                var loop=new PlayableAiOwnerLoop(p,PlayableAiOpeningComposition.Initialize(7,"player-1",aiProfile:ai),gen,null,ai,AiDifficulty.Veteran);
                string message;Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(gen,1,"player-1",PlayableCommandKind.Attack,new[]{actor},new NavPoint(-7,15),targetId:enemy,origin:PlayableOrderOrigin.Ai,source:PlayableAiOpeningComposition.SourceIdentity,jobId:1,actionId:1),out message),message);
                var view=d.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player);var o=PlayableAiObservation.From(view);
                var registry=(AiArmyRegistry)Get(loop,"armies");Assert.True(registry.TryCreate(o,AiArmyRole.MobileDefense,AiArmyPlanner.Policy,new[]{actor},out var army,requiredMinimum:1));
                var micro=(AiCombatMicroPolicy)Get(loop,"combatMicro");
                var stop=new PlayableAiAction(11,o.OwnerId,p.ProfileId,p.Revision,gen,o.SnapshotSequence,PlayableCommandKind.Stop,new[]{actor},new NavPoint(-16,15),seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                var local=new PlayableAiAction(1,o.OwnerId,p.ProfileId,p.Revision,gen,o.SnapshotSequence,stop.Kind,stop.CopyEntityIds(),stop.Target,seed:7,sourceIdentity:stop.SourceIdentity);
                micro.Commit(new AiCombatMicroPlan{Action=local,Army=army,Phase=AiCombatMicroPhase.Regroup},gen,0);
                var factory=view.Buildings.Last(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory);
                var production=new PlayableAiAction(12,o.OwnerId,p.ProfileId,p.Revision,gen,o.SnapshotSequence,PlayableCommandKind.QueueTank,new[]{factory.Id},default,seed:7,sourceIdentity:stop.SourceIdentity);
                loop.ReconcileBudget(d);Set(loop,"ordinal",1L);
                void Stage(PlayableAiAction action,string policy,int actionOrdinal,int cost,Action<PlayableAiTraceRecord> receipt,long localId)
                {
                    var identity=new AiReceiptIdentity(gen,o.OwnerId,1,actionOrdinal);var claims=action.EntityIds.Select(id=>"recipient:"+id).Concat(new[]{"policy:"+policy}).ToArray();
                    var intent=new AiIntent("attention-control:"+policy,policy,action,100,cost,0,claims);
                    Assert.True(loop.Budget.Accept(new AiBudgetEntry(intent.Id,policy,action,cost,0,0,100,"control",claims,p.ProfileId+"@"+p.Revision,identity)));
                    var item=Activator.CreateInstance(typeof(PlayableAiOwnerLoop).GetNestedType("Pending",BindingFlags.NonPublic),true);
                    foreach(var pair in new[]{new {Key="Observation",Value=(object)o},new{Key="ObservationIdentity",Value=(object)o.Identity},new{Key="ObservationTick",Value=(object)0L},new{Key="SnapshotSequence",Value=(object)o.SnapshotSequence},new{Key="Action",Value=(object)action},new{Key="DueTick",Value=(object)0L},new{Key="HumanSequence",Value=(object)0L},new{Key="Ordinal",Value=(object)1L},new{Key="LocalActionId",Value=(object)localId},new{Key="PolicyName",Value=(object)policy},new{Key="Intent",Value=(object)intent},new{Key="Identity",Value=(object)identity},new{Key="Receipt",Value=(object)receipt}})Set(item,pair.Key,pair.Value);
                    ((IList)Get(loop,"pendingItems")).Add(item);
                }
                Stage(stop,AiCombatMicroPolicy.Policy,1,0,micro.Receipt,1);
                var saved=WorldWire.Pack(loop.WriteState);var clone=loop.CreateReviewClone(0);CollectionAssert.AreEqual(saved,WorldWire.Pack(clone.WriteState),"Whole owner clone preserves the pending micro ledger and clients exactly");
                Stage(production,"production",2,p.TankCreditCost,_=>{},1);
                var attention=loop.CombatAttention;attention.Charge(0,ai,AiDifficulty.Veteran,true);attention.Charge(0,ai,AiDifficulty.Veteran,true);
                loop.Deliver(d,0,false);
                Assert.True(loop.Checkpoint.Records.Any(r=>r.Policy=="production"&&r.Status==PlayableAiDeliveryStatus.Applied),"Due macro work behind a fairness-blocked micro is delivered in the bounded scan");
                Assert.False(loop.Checkpoint.Records.Any(r=>r.Policy==AiCombatMicroPolicy.Policy&&r.Status==PlayableAiDeliveryStatus.Applied));
                loop.Deliver(d,0,false);
                Assert.True(loop.Checkpoint.Records.Any(r=>r.Policy==AiCombatMicroPolicy.Policy&&r.Status==PlayableAiDeliveryStatus.Applied));
                Assert.AreEqual(4,attention.Applied);Assert.IsEmpty(((IList)Get(loop,"pendingItems")));
                Assert.AreEqual(1,d.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player).Buildings.Single(b=>b.Id==factory.Id).QueueCount);
            }
            finally{d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();}
        }
        [Test]
        public void ImpossibleReactionWindowDoesNotMutateCurrentOwnerState()
        {
            var ai=AiProfile.Initial;var p=PlayableProfile.Default;var loop=new PlayableAiOwnerLoop(p,PlayableAiOpeningComposition.Initialize(7,"player-1",aiProfile:ai),901,null,ai,AiDifficulty.Veteran);
            var action=new PlayableAiAction(1,"player-1",p.ProfileId,p.Revision,901,0,PlayableCommandKind.Stop,new[]{1},default,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            var before=WorldWire.Pack(loop.WriteState);var method=typeof(PlayableAiOwnerLoop).GetMethod("CombatReaction",F);
            var error=Assert.Throws<TargetInvocationException>(()=>method.Invoke(loop,new object[]{action,100L,100L}));Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
            CollectionAssert.AreEqual(before,WorldWire.Pack(loop.WriteState));var clone=loop.CreateReviewClone(0);CollectionAssert.AreEqual(before,WorldWire.Pack(clone.WriteState));
        }
    }
}
