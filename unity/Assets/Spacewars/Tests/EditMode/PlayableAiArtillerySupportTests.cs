using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableAiArtillerySupportTests
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private readonly PlayableProfile profile=PlayableProfile.Default;
        private object domain;
        private NavigationSession Nav=>(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(domain);
        private IDictionary Units=>(IDictionary)Domain.GetField("units",Flags).GetValue(domain);
        private object Call(string name,params object[] args)=>Domain.GetMethod(name,Flags).Invoke(domain,args);
        private int Spawn(PlayableOwner owner,PlayableEntityKind kind,double x,double z)=>(int)Call("SpawnUnit",new NavPoint(x,z),owner,kind);
        private PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        [SetUp]public void Setup()
        {
            domain=Activator.CreateInstance(Domain,Flags,null,new object[]{profile,1L},null);
            foreach(int id in Units.Keys.Cast<int>().ToArray()){Nav.Remove(id);Units.Remove(id);}
        }
        [Test]public void UsefulShotUsesVisibleBodiesAndFriendlyFireScore()
        {
            var gun=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0);
            Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,-9,0);
            Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);
            var safe=View();var fact=safe.ArtillerySupport.Single(x=>x.UnitId==gun);
            Assert.True(fact.Supported);Assert.True(fact.UsefulShot);Assert.False(fact.Threatened);
            Spawn(PlayableOwner.Enemy,PlayableEntityKind.Shkval,25,25);
            Assert.True(View().ArtillerySupport.Single(x=>x.UnitId==gun).UsefulShot);
            Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,-4,1.5);
            Assert.False(View().ArtillerySupport.Single(x=>x.UnitId==gun).UsefulShot);
            Assert.True(View(PlayableOwner.Enemy).ArtillerySupport.All(x=>x.UnitId!=gun),"Enemy support facts cannot reference a player gun.");
        }
        [Test]public void ReachablePositionAndReceiptBoundMovement()
        {
            var gun=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0);
            Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,-5,0);
            Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,3,0);
            var observation=PlayableAiObservation.From(View());var fact=observation.ArtillerySupport.Single(x=>x.UnitId==gun);
            Assert.False(fact.UsefulShot);Assert.True(fact.Position.HasValue);Assert.True(fact.PositionAllowsShot);
            var opening=PlayableAiOpeningComposition.Initialize(7,"player-1");
            var policy=new PlayableAiArtillerySupportPolicy(profile);
            var move=policy.TryPlan(observation,opening);
            Assert.NotNull(move);Assert.AreEqual(PlayableCommandKind.Move,move.Kind);Assert.AreEqual(gun,move.EntityIds.Single());
            Assert.Null(policy.TryPlan(observation,opening));
            policy.ObserveReceipt(new PlayableAiTraceRecord(observation.Identity,move.ActionId,0,1,0,PlayableAiDeliveryStatus.Rejected,PlayableCommandStatus.InvalidTarget,"rejected",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity));
            var retry=policy.TryPlan(observation,opening);Assert.NotNull(retry);Assert.AreNotEqual(move.ActionId,retry.ActionId);
            var command=new PlayableCommand(1,2,"player-1",retry.Kind,retry.CopyEntityIds(),retry.Target);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",command,null));
            policy.ObserveReceipt(new PlayableAiTraceRecord(observation.Identity,retry.ActionId,0,2,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity));
            var current=PlayableAiObservation.From(View());Assert.Null(policy.TryPlan(current,opening));
            var restored=new PlayableAiArtillerySupportPolicy(profile);restored.Restore(policy.Capture());
            Assert.AreEqual(policy.Capture().HeldPosition.X,restored.Capture().HeldPosition.X);
        }
        [Test]public void SeedStableDoctrineSurvivesRestoreWithoutHiddenEnemy()
        {
            Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,-18,0);
            var first=PlayableAiObservation.From(View());var opening=PlayableAiOpeningComposition.Initialize(7,"player-1");
            var policy=new PlayableAiArtillerySupportPolicy(profile);
            policy.TryPlan(first,opening);var saved=policy.Capture();Assert.NotNull(saved);
            var restored=new PlayableAiArtillerySupportPolicy(profile);restored.Restore(saved);
            Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,25,25);
            restored.TryPlan(PlayableAiObservation.From(View()),opening);
            Assert.AreEqual(saved.Adopted,restored.Capture().Adopted);Assert.AreEqual(saved.Share,restored.Capture().Share);
            Assert.AreEqual(saved.EarlyOrdinal,restored.Capture().EarlyOrdinal);
            Assert.Throws<ArgumentException>(()=>restored.Restore(new PlayableAiArtilleryState(7,1,false,5,1,false,false,.2,"opening:safe:False:False",0,0,0,default(NavPoint),"wrong")));
        }
        [Test]public void OpeningDoctrineQueuesOnlyOwnedFactoryAndCountsPaidQueue()
        {
            var units=Enumerable.Range(1,5).Select(i=>new PlayableEntitySnapshot(i,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-12+i*2,8),profile.TankHealth,false,0,0,0)).ToArray();
            var own=new PlayableBuildingSnapshot(50,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(-15,8),profile.FactoryHealth,1,0,0,default(NavPoint));
            var foreign=new PlayableBuildingSnapshot(51,PlayableOwner.Enemy,PlayableBuildingKind.Factory,new NavPoint(15,8),profile.FactoryHealth,1,0,0,default(NavPoint),includePrivateState:false);
            PlayableAiObservation observe(int seed,PlayableBuildingSnapshot factory)=>PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,1,seed,1,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,units,new[]{factory,foreign},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,population:new PlayablePopulationSnapshot(5,0,40)));
            PlayableAiAction action=null;PlayableAiArtillerySupportPolicy policy=null;PlayableAiOpeningCompositionState opening=null;int selected=0;
            for(int candidate=1;candidate<10000&&action==null;candidate++)
            {
                opening=PlayableAiOpeningComposition.Initialize(candidate,"player-1");policy=new PlayableAiArtillerySupportPolicy(profile);
                action=policy.TryPlan(observe(candidate,own),opening);if(action!=null)selected=candidate;
            }
            Assert.Greater(selected,0);Assert.AreEqual(PlayableCommandKind.QueueShkval,action.Kind);Assert.AreEqual(50,action.EntityIds.Single());
            Assert.Null(policy.TryPlan(observe(selected,own),opening));
            policy.ObserveReceipt(new PlayableAiTraceRecord("fixture",action.ActionId,0,1,1,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",ownerId:"player-1",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity));
            var queued=new PlayableBuildingSnapshot(50,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(-15,8),profile.FactoryHealth,1,1,0,default(NavPoint),orders:new[]{new PlayableProductionOrderSnapshot(1,PlayableEntityKind.Shkval,profile.ShkvalCreditCost,profile.ShkvalPopulationCost,profile.ShkvalProductionDurationSec,profile.ShkvalProductionDurationSec,true)});
            Assert.Null(policy.TryPlan(observe(selected,queued),opening));
        }
    }
}
