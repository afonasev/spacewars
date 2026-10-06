using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableRuntimeTests
    {
        private static PlayableProfileData Data()
        {
            return (PlayableProfileData)typeof(PlayableProfile).GetMethod("DefaultData",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        }
        private static bool Until(Func<bool> predicate){return SpinWait.SpinUntil(predicate,3000);}
        private static PlayableCommand Command(long generation,long sequence,PlayableCommandKind kind,int[] ids=null,NavPoint target=default(NavPoint))
        {return new PlayableCommand(generation,sequence,"player-1",kind,ids??new int[0],target);}

        [Test] public void HumanMatchRetainsEnemyAiWithoutSpendingOrOrderingForTheHuman()
        {
            var runtime=PlayableRuntime.CreateHumanMatch(PlayableProfile.ThreeCrossingsDefault,19,19092026);
            try
            {
                Assert.IsNull(runtime.AiCheckpoint,"A human seat must not receive an autonomous owner planner.");
                Assert.IsNotNull(typeof(PlayableRuntime).GetField("enemyAi",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime),"The opponent retains native AI.");
                Assert.IsTrue(Until(()=>runtime.Latest.Tick>=60));
                var own=runtime.Latest.Buildings.Where(b=>b.Owner==PlayableOwner.Player).ToArray();
                Assert.AreEqual(1,own.Length);Assert.AreEqual(PlayableBuildingKind.Headquarters,own[0].Kind);
                var view=runtime.Latest;var profile=PlayableProfile.ThreeCrossingsDefault;
                double expected=profile.StartingCredits+Math.Floor(view.Tick/30d+1e-9)*profile.HeadquartersIncomePerPeriod/profile.IncomePeriodSeconds;
                Assert.AreEqual(expected,view.Credits,1e-7,"Only ordinary headquarters income changes human credits.");
            }
            finally{runtime.RequestStop();Assert.IsTrue(Until(()=>runtime.IsStopped));}
        }

        [Test] public void RuntimePublishesTicksWhilePlannerHasNoAnswerAndStopKeepsReceipts()
        {
            var data=Data(); data.factoryBuildSeconds=1;data.tankProductionSeconds=1;data.startingCredits=1000;
            var runtime=new PlayableRuntime(PlayableProfile.Create(data),9,41,false);
            try
            {
                Assert.IsTrue(runtime.TrySubmit(Command(9,1,PlayableCommandKind.BuildFactory)).Accepted);
                Assert.IsTrue(Until(()=>runtime.DrainReceipts().Any(r=>r.Sequence==1&&r.Status==PlayableCommandStatus.Applied)));
                Assert.IsTrue(Until(()=>runtime.Latest.Buildings.Any(b=>b.Kind==PlayableBuildingKind.Factory&&b.Progress>=1d)));
                int factory=runtime.Latest.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;
                Assert.IsTrue(runtime.TrySubmit(Command(9,2,PlayableCommandKind.QueueTank,new[]{factory})).Accepted);
                Assert.IsTrue(Until(()=>runtime.Latest.Entities.Any(e=>e.Owner==PlayableOwner.Player)));
                int tank=runtime.Latest.Entities.First(e=>e.Owner==PlayableOwner.Player).Id;
                Assert.IsTrue(runtime.TrySubmit(Command(9,3,PlayableCommandKind.Move,new[]{tank},new NavPoint(-8,8))).Accepted);
                NavigationRequest request=null; Assert.IsTrue(Until(()=>runtime.Requests.TryDequeue(out request)));
                long before=runtime.Latest.Tick; Assert.IsTrue(Until(()=>runtime.Latest.Tick>=before+3));
                runtime.RequestStop(); Assert.IsTrue(Until(()=>runtime.IsStopped));
                var receipts=runtime.DrainReceipts(); Assert.IsTrue(receipts.Any(r=>r.Sequence==3));
                Assert.AreEqual(RuntimeStatus.Stopped,runtime.Latest.Status);
            }
            finally { runtime.Dispose(); Until(()=>runtime.IsStopped); }
        }

        [Test] public void RuntimeRejectsStaleSequenceAndGenerationBeforeAdmission()
        {
            var runtime=new PlayableRuntime(PlayableProfile.Default,12,3,false);
            try
            {
                Assert.AreEqual(PlayableCommandStatus.StaleGeneration,runtime.TrySubmit(Command(11,1,PlayableCommandKind.BuildFactory)).Status);
                Assert.IsTrue(runtime.TrySubmit(Command(12,2,PlayableCommandKind.BuildFactory)).Accepted);
                Assert.AreEqual(PlayableCommandStatus.InvalidSequence,runtime.TrySubmit(Command(12,2,PlayableCommandKind.BuildFactory)).Status);
                Assert.AreEqual(PlayableCommandStatus.InvalidSequence,runtime.TrySubmit(Command(12,1,PlayableCommandKind.BuildFactory)).Status);
            }
            finally { runtime.Dispose(); Until(()=>runtime.IsStopped); }
        }

        [Test] public void ProductionPauseFreezesLedgerAndRestartRejectsOldOrderCommands()
        {
            var data=Data();data.factoryBuildSeconds=1;var profile=PlayableProfile.Create(data);
            var runtime=new PlayableRuntime(profile,21,7,false);
            try
            {
                Assert.True(runtime.TrySubmit(Command(21,1,PlayableCommandKind.BuildFactory)).Accepted);
                Assert.True(Until(()=>runtime.Latest.Buildings.Any(b=>b.Kind==PlayableBuildingKind.Factory&&b.Progress>=1)));
                int id=runtime.Latest.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;
                Assert.True(runtime.TrySubmit(Command(21,2,PlayableCommandKind.QueueTank,new[]{id})).Accepted);
                Assert.True(Until(()=>runtime.Latest.Population.Reserved==profile.TankPopulationCost));
                runtime.RequestPause(true);Assert.True(Until(()=>runtime.Latest.Paused));
                var frozen=runtime.Latest;var order=frozen.Buildings.Single(b=>b.Id==id).PrivateState.Orders.Single();
                Assert.AreEqual(PlayableCommandStatus.Rejected,runtime.TrySubmit(new PlayableCommand(21,3,"player-1",PlayableCommandKind.CancelProductionOrder,new[]{id},productionOrderId:order.Id)).Status);
                Assert.True(Until(()=>runtime.Latest.Sequence>frozen.Sequence+3));
                Assert.AreEqual(frozen.Tick,runtime.Latest.Tick);Assert.AreEqual(frozen.Credits,runtime.Latest.Credits);
                Assert.AreEqual(order.Remaining,runtime.Latest.Buildings.Single(b=>b.Id==id).PrivateState.Orders.Single().Remaining);
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
                using(var next=new PlayableRuntime(profile,22,7,false))
                {
                    Assert.Zero(next.Latest.Population.Reserved);Assert.AreEqual(PlayableProfile.Default.ExplorerPopulationCost,next.Latest.Population.Living);
                    Assert.False(next.Latest.Buildings.Any(b=>b.Kind==PlayableBuildingKind.Factory));
                    Assert.AreEqual(PlayableCommandStatus.StaleGeneration,next.TrySubmit(new PlayableCommand(21,4,"player-1",PlayableCommandKind.CancelProductionOrder,new[]{id},productionOrderId:order.Id)).Status);
                    next.RequestStop();Assert.True(Until(()=>next.IsStopped));
                }
            }
            finally{runtime.Dispose();Until(()=>runtime.IsStopped);}
        }

        [Test] public void DemolitionPauseFreezesAndRestartClearsPrivateState()
        {
            var runtime=new PlayableRuntime(PlayableProfile.Default,31,7,false);
            try
            {
                Assert.True(runtime.TrySubmit(Command(31,1,PlayableCommandKind.SellBuilding,new[]{1})).Accepted);
                Assert.True(Until(()=>runtime.Latest.Buildings.Single(b=>b.Id==1).PrivateState.Lifecycle.Selling));
                runtime.RequestPause(true);Assert.True(Until(()=>runtime.Latest.Paused));
                var frozen=runtime.Latest;double progress=frozen.Buildings.Single(b=>b.Id==1).PrivateState.Lifecycle.SaleProgress;
                Assert.True(Until(()=>runtime.Latest.Sequence>frozen.Sequence+3));Assert.AreEqual(frozen.Tick,runtime.Latest.Tick);
                Assert.AreEqual(progress,runtime.Latest.Buildings.Single(b=>b.Id==1).PrivateState.Lifecycle.SaleProgress);
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
                using(var next=new PlayableRuntime(PlayableProfile.Default,32,7,false))
                {
                    Assert.False(next.Latest.Buildings.Single(b=>b.Id==1).PrivateState.Lifecycle.Selling);
                    Assert.AreEqual(PlayableCommandStatus.StaleGeneration,next.TrySubmit(Command(31,2,PlayableCommandKind.SellBuilding,new[]{1})).Status);
                    next.RequestStop();Assert.True(Until(()=>next.IsStopped));
                }
            }
            finally{runtime.Dispose();Until(()=>runtime.IsStopped);}
        }

        [Test] public void RefineryPauseFreezesProgressAndRestartRejectsOldUpgrade()
        {
            var data=Data();data.refineryBuildSeconds=1;data.scienceBuildSeconds=1;data.startingCredits=2000;
            var profile=PlayableProfile.Create(data);var runtime=new PlayableRuntime(profile,41,7,false);
            try
            {
                Assert.True(runtime.TrySubmit(new PlayableCommand(41,1,"player-1",PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:1,slotId:1,parentId:1,buildingKind:PlayableBuildingKind.Refinery)).Accepted);
                Assert.True(runtime.TrySubmit(new PlayableCommand(41,2,"player-1",PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:1,slotId:2,parentId:1,buildingKind:PlayableBuildingKind.ScientificCenter)).Accepted);
                Assert.True(Until(()=>runtime.Latest.Buildings.Count(b=>b.Owner==PlayableOwner.Player&&b.Phase==ConstructionPhase.Ready)==3));
                int id=runtime.Latest.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Refinery).Id;
                runtime.TrySubmit(Command(41,3,PlayableCommandKind.UpgradeRefinery,new[]{id}));
                Assert.True(Until(()=>runtime.Latest.Buildings.Single(b=>b.Id==id).PrivateState.Upgrade.Active));
                runtime.RequestPause(true);Assert.True(Until(()=>runtime.Latest.Paused));var frozen=runtime.Latest;
                double elapsed=frozen.Buildings.Single(b=>b.Id==id).PrivateState.Upgrade.Elapsed;
                Assert.AreEqual(PlayableCommandStatus.Rejected,runtime.TrySubmit(Command(41,4,PlayableCommandKind.CancelRefineryUpgrade,new[]{id})).Status);
                Assert.True(Until(()=>runtime.Latest.Sequence>frozen.Sequence+3));Assert.AreEqual(frozen.Tick,runtime.Latest.Tick);
                Assert.AreEqual(elapsed,runtime.Latest.Buildings.Single(b=>b.Id==id).PrivateState.Upgrade.Elapsed);
                runtime.RequestPause(false);Assert.True(Until(()=>runtime.Latest.Buildings.Single(b=>b.Id==id).PrivateState.Upgrade.Elapsed>elapsed));
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
                using(var next=new PlayableRuntime(profile,42,7,false))
                {
                    Assert.False(next.Latest.Buildings.Any(b=>b.RefineryUpgraded||b.PrivateState?.Upgrade?.Active==true));
                    Assert.AreEqual(PlayableCommandStatus.StaleGeneration,next.TrySubmit(Command(41,5,PlayableCommandKind.UpgradeRefinery,new[]{id})).Status);
                    next.RequestStop();Assert.True(Until(()=>next.IsStopped));
                }
            }
            finally{runtime.Dispose();Until(()=>runtime.IsStopped);}
        }

        [Test] public void ProfileValidationRejectsInvalidRangeAndMetadataCoversEveryValue()
        {
            var invalid=Data(); invalid.cameraMinZoom=invalid.cameraMaxZoom+1;
            Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(invalid));
            var nan=Data();nan.tankSpeed=Double.NaN;
            Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(nan));
            Assert.IsTrue(PlayableProfileMetadata.Fields.All(field=>!String.IsNullOrEmpty(field.Path)&&field.Step>0&&field.Maximum>=field.Minimum));
        }
    }
}
