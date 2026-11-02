using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class CombatMicroPathDeliveryTests
    {
        const long Gen=906;
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile Ai=AiProfile.Initial;
        static PlayableDomain Domain(bool group=false)
        {
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,10),new NavPoint(-18,-12),pin:1),new OfflineStart("east",2,new NavPoint(24,21),new NavPoint(25,27),pin:2)};
            var site=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,P);var pad=site.DefenseSlots(P).Single(s=>s.Id==1003);
            var units=new List<OfflineScenarioUnit>{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-30,-4))};
            if(group)units.Add(new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-30,-6)));
            var config=new OfflineMatchConfiguration(P,"micro-path-delivery-control","flat-seed7","current-native-path",7,
                new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,
                new[]{site,new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())},Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},
                scenario:units.ToArray(),scenarioBuildings:new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.RepairTower,1,pad.Id,pad.Position,pad.Heading)});
            return new PlayableDomain(P,Gen,config);
        }
        static PlayableAiObservation View(PlayableDomain d)=>PlayableAiObservation.From(d.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player));
        static PlayableAiAction Move(PlayableAiObservation o,NavPoint goal,int parent=0)=>new PlayableAiAction(1,o.OwnerId,P.ProfileId,P.Revision,Gen,o.SnapshotSequence,PlayableCommandKind.Move,
            o.Entities.Where(u=>u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Tank).Select(u=>u.Id).ToArray(),goal,parentId:parent,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        static void Cleanup(PlayableDomain d)
        {
            d.StopObservedPaths();Assert.True(SpinWait.SpinUntil(()=>d.ObservedPathWorkerStopped,5000));
            d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);
        }
        [TestCase(false)][TestCase(true)]
        public void RepairConsumesOneOrdinaryValidatedPathAndPendingCannotAuthorize(bool asynchronous)
        {
            var d=Domain();using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
            try
            {
                var o=View(d);int patient=o.Entities.Single(u=>u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Tank).Id;
                typeof(PlayableDomain).GetMethod("Damage",F).Invoke(d,new object[]{patient,80});
                var tower=o.Buildings.Single(b=>b.Kind==PlayableBuildingKind.RepairTower);var action=Move(o,new NavPoint(-28.5,1.5),tower.Id);
                int solves=0;d.BeforeArmySafetyPathForTests=(a,id)=>{Assert.AreEqual(patient,id);Interlocked.Increment(ref solves);if(asynchronous){entered.Set();if(!release.Wait(5000))throw new TimeoutException("Control path not released");}};
                if(asynchronous)d.EnableAsyncObservedPaths();
                string Check()=>d.CombatMicroRejection(action,AiCombatMicroPhase.Repair,null,Ai,AiDifficulty.Veteran,new AiCombatMicroPolicy());
                string rejection=null;
                if(asynchronous)
                {
                    Assert.Throws<ObservedPathPending>(()=>Check());Assert.True(entered.Wait(5000));
                    Assert.Throws<ObservedPathPending>(()=>Check(),"An unfinished ordinary path is never a repair authorization");release.Set();
                    Assert.True(SpinWait.SpinUntil(()=>{try{rejection=Check();return true;}catch(ObservedPathPending){return false;}},5000));
                }
                else rejection=Check();
                Assert.IsNull(rejection);Assert.AreEqual(1,solves,"Micro must consume the existing actor path, without another solve");
                Assert.AreEqual(0,d.Tick,"Delivery validation does not advance simulation");
            }
            finally{release.Set();Cleanup(d);}
        }
        [TestCase(false)][TestCase(true)]
        public void KnownStaticFireStillRejectsTheValidatedOrdinaryPath(bool asynchronous)
        {
            var d=Domain();try
            {
                var o=View(d);var action=Move(o,new NavPoint(-23,-4));
                var knowledge=new AiKnowledgeState(o.OwnerId,Gen,o.Tick,new[]{new AiKnownContact(9999,PlayableOwner.Enemy,true,(int)PlayableBuildingKind.SentryTurret,new NavPoint(-26,2),100,o.Tick,1,false)},Array.Empty<AiVisitedArea>());
                int solves=0;d.BeforeArmySafetyPathForTests=(a,id)=>Interlocked.Increment(ref solves);if(asynchronous)d.EnableAsyncObservedPaths();
                string rejection=null;Assert.True(SpinWait.SpinUntil(()=>{try{rejection=d.CombatMicroRejection(action,AiCombatMicroPhase.Return,knowledge,Ai,AiDifficulty.Veteran,new AiCombatMicroPolicy());return true;}catch(ObservedPathPending){return false;}},5000));
                Assert.AreEqual("known static fire blocks micro path",rejection);Assert.AreEqual(1,solves);
            }
            finally{Cleanup(d);}
        }
        [Test]
        public void GroupedMoveBindsEachValidatedPathToItsActualFormationSlot()
        {
            var d=Domain(true);try
            {
                var o=View(d);var action=Move(o,new NavPoint(-27,-4));var paths=new Dictionary<int,NavPoint[]>();int solves=0;
                d.BeforeArmySafetyPathForTests=(a,id)=>solves++;
                var method=typeof(PlayableDomain).GetMethods(F).Single(m=>m.Name=="ArmyMoveRejection"&&m.GetParameters().Length==4);
                var args=new object[]{action,false,null,paths};Assert.IsNull(method.Invoke(d,args));var routeView=(PlayableAiObservation)args[2];
                CollectionAssert.AreEquivalent(action.EntityIds,paths.Keys);Assert.AreEqual(2,solves);
                Assert.AreEqual(2,paths.Values.Select(p=>p.Last()).Distinct().Count(),"Group recipients have distinct actual slots");
                foreach(int id in action.EntityIds)
                {var u=routeView.Entities.Single(e=>e.Id==id);Assert.Greater(paths[id].Length,0);Assert.True(d.Geometry.IsFree(paths[id].Last(),PlayableUnitRules.Radius(P,u.Kind)));}
                solves=0;var knowledge=new AiKnowledgeState(o.OwnerId,Gen,o.Tick,new[]{new AiKnownContact(9999,PlayableOwner.Enemy,true,(int)PlayableBuildingKind.SentryTurret,new NavPoint(40,-35),100,o.Tick,1,false)},Array.Empty<AiVisitedArea>());
                Assert.IsNull(d.CombatMicroRejection(action,AiCombatMicroPhase.Return,knowledge,Ai,AiDifficulty.Veteran,new AiCombatMicroPolicy()));Assert.AreEqual(2,solves,"One solve per grouped recipient, no per-unit reallocation/query");
            }
            finally{Cleanup(d);}
        }
    }
}
