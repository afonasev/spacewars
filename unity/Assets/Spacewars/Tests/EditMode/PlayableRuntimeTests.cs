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

        [Test] public void AuthorityPublishesRealPreBarrierTankPaymentWithoutAdvancingIncomeOrTick()
        {
            var basic=OfflineParticipantAuthorityTests.Config(2,false);var costs=new double[basic.Starts.Count,basic.Starts.Count];
            for(int i=0;i<basic.Starts.Count;i++)for(int j=0;j<basic.Starts.Count;j++)costs[i,j]=basic.RouteCost(i,j);
            var config=new OfflineMatchConfiguration(basic.Profile,basic.SourceIdentity,basic.MapIdentity,basic.RouteProvenance,basic.Seed,basic.Roster.Select(p=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,OfflineControl.Human)).ToArray(),basic.Starts.ToArray(),basic.Sites.ToArray(),basic.Obstacles.ToArray(),costs);
            var a=new PlayableAuthorityTick(config,71);var home=a.Latest.Sites.Single(x=>x.Site.Id==a.Latest.HomeSiteId);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,config.Roster[0].Id,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:home.Site.Id,slotId:1,parentId:home.CenterId,buildingKind:PlayableBuildingKind.Factory)).Status);
            for(int i=0;i<(config.Profile.FactoryBuildSeconds+1)*30;i++)Assert.True(a.TryAdvance());
            int factory=a.Latest.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory).Id;
            Assert.AreEqual(ConstructionPhase.Ready,a.Latest.Buildings.Single(b=>b.Id==factory).Phase);
            var stale=a.Latest;
            var paid=a.Apply(new PlayableCommand(71,2,config.Roster[0].Id,PlayableCommandKind.QueueTank,new[]{factory}));
            Assert.AreEqual(PlayableCommandStatus.Applied,paid.Status);Assert.AreEqual(stale.Credits,a.Latest.Credits,"A receipt can precede its next snapshot publication.");
            int explorer=stale.Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,3,config.Roster[0].Id,PlayableCommandKind.Move,new[]{explorer},new NavPoint(-8,8))).Status);
            NavigationRequest request=null;Assert.True(a.Requests.TryDequeue(out request));Assert.AreEqual(explorer,request.Entity);
            Assert.False(a.TryAdvance());var held=a.Latest;Assert.True(a.AwaitingRoutes);
            Assert.AreEqual(stale.Tick,held.Tick);Assert.AreEqual(stale.SettledIncome,held.SettledIncome);
            Assert.AreEqual(stale.Credits-config.Profile.TankCreditCost,held.Credits,"Only the actual pre-barrier Tank debit is published; income has not advanced.");
            var order=held.Buildings.Single(b=>b.Id==factory).PrivateState.Orders.Single();Assert.AreEqual(PlayableEntityKind.Tank,order.Kind);Assert.AreEqual(config.Profile.TankCreditCost,order.PaidCost);Assert.AreEqual((double)config.Profile.TankProductionSeconds,order.Duration);
            for(int i=0;i<3;i++){Assert.False(a.TryAdvance());Assert.AreEqual(held.Tick,a.Latest.Tick);Assert.AreEqual(held.Credits,a.Latest.Credits);Assert.AreEqual(held.SettledIncome,a.Latest.SettledIncome);Assert.AreEqual(held.Entities.Single(e=>e.Id==explorer).Position,a.Latest.Entities.Single(e=>e.Id==explorer).Position);}
            TestContext.WriteLine("E3_ACTUAL_BARRIER_PAYMENT "+Newtonsoft.Json.JsonConvert.SerializeObject(new{Receipt=paid,Before=new{stale.Tick,stale.Credits,stale.SettledIncome},Held=new{held.Tick,held.Credits,held.SettledIncome},Order=order,RequestEntity=request.Entity}));
            a.Stop();
        }

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

        [Test] public void RuntimePublishesWhileFixedRouteBarrierFreezesTickAndStopKeepsReceipts()
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
                PlayableCommandReceipt paid=null;
                Assert.IsTrue(Until(()=>{paid=runtime.DrainReceipts().FirstOrDefault(r=>r.Sequence==2&&r.Status==PlayableCommandStatus.Applied);return paid!=null;}),"The ordinary human Tank purchase must actually be applied before the freeze baseline.");
                Assert.IsTrue(Until(()=>runtime.Latest.Entities.Any(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank)));
                int tank=runtime.Latest.Entities.First(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank).Id;
                Assert.IsTrue(runtime.TrySubmit(Command(9,3,PlayableCommandKind.Move,new[]{tank},new NavPoint(-8,8))).Accepted);
                long movePublication=runtime.Latest.Sequence;
                NavigationRequest request=null; Assert.IsTrue(Until(()=>runtime.Requests.TryDequeue(out request)));
                Assert.AreEqual(tank,request.Entity,"Hold the actual moved Tank route, not an earlier unrelated request.");
                Assert.IsTrue(Until(()=>runtime.Latest.Sequence>movePublication&&runtime.Latest.Metrics.NavigationPending>0),"Observe the held barrier publication after pre-barrier command admission.");
                var before=runtime.Latest;
                // Repeated identical polls reuse payloads. Meaningful same-tick actions
                // still publish while the route answer is held.
                for(int i=0;i<3;i++){long prior=runtime.Latest.Sequence;runtime.RecordHumanAction("player-1");Assert.IsTrue(Until(()=>runtime.Latest.Sequence>prior));}
                Assert.IsTrue(Until(()=>runtime.Latest.Sequence>=before.Sequence+3),"Authority must publish same-tick action changes while the route answer is held.");
                Assert.AreEqual(before.Tick,runtime.Latest.Tick,"The fixed route barrier freezes gameplay tick.");
                Assert.AreEqual(before.Credits,runtime.Latest.Credits,"Income clocks cannot advance at a route barrier.");
                Assert.AreEqual(before.Entities.First(e=>e.Id==tank).Position,runtime.Latest.Entities.First(e=>e.Id==tank).Position,"Movement clocks cannot advance without the answer.");
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
