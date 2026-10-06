using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableAiOwnerLoopTests
    {
        private static PlayableAiOwnerCheckpoint Enemy(PlayableRuntime runtime)=>(PlayableAiOwnerCheckpoint)typeof(PlayableRuntime)
            .GetProperty("EnemyAiCheckpoint",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);

        [Test]
        public void OrdinaryRuntimeAppliesIndependentEnemyOwnerReceiptsWithoutPublishingPrivateState()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,707,19092026))
            {
                Assert.True(Until(()=>Enemy(runtime).Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied)));
                var enemy=Enemy(runtime);var player=runtime.AiCheckpoint;
                var applied=enemy.Records.First(r=>r.Status==PlayableAiDeliveryStatus.Applied);
                Assert.AreEqual("enemy-1",enemy.OwnerId);
                Assert.AreEqual("enemy-1",enemy.Opening.OwnerId);
                Assert.AreEqual(19092026,enemy.Seed);
                Assert.AreEqual(player.SourceIdentity,enemy.SourceIdentity);
                Assert.AreEqual(player.ProfileId,enemy.ProfileId);
                Assert.AreEqual(player.ProfileRevision,enemy.ProfileRevision);
                Assert.AreEqual(707,enemy.Generation);
                Assert.GreaterOrEqual(enemy.DecisionTick,45);
                var identityParts=applied.ObservationIdentity.Split(':');
                Assert.AreEqual(30,applied.DueTick-long.Parse(identityParts[identityParts.Length-2]));
                Assert.AreEqual(applied.DueTick,applied.ApplicationTick);
                Assert.GreaterOrEqual(applied.CommandSequence,1L<<61);
                Assert.True(applied.ObservationIdentity.Contains(":enemy-1:707:"));
                Assert.True(enemy.Records.All(r=>r.OwnerId=="enemy-1"));
                Assert.True(player.Records.All(r=>r.OwnerId=="player-1"));
                Assert.IsTrue(enemy.LastActionKind.HasValue);
                Assert.False(runtime.Latest.Buildings.Any(b=>b.Owner==PlayableOwner.Enemy&&b.PrivateState!=null));
                Assert.IsEmpty(runtime.DrainReceipts());
            }
        }

        [Test]
        public void EnemyPendingIsCancelledOnPauseAndStoppedOnShutdownThenRestartedFresh()
        {
            var runtime=new PlayableRuntime(PlayableProfile.Default,708,19092026);
            try
            {
                Assert.True(Until(()=>Enemy(runtime).PendingActionId!=0));
                var action=Enemy(runtime).PendingActionId;
                runtime.RequestPause(true);
                Assert.True(Until(()=>Enemy(runtime).Records.Any(r=>r.ActionId==action&&r.Status==PlayableAiDeliveryStatus.Cancelled)));
                runtime.RequestPause(false);
                Assert.True(Until(()=>Enemy(runtime).PendingActionId!=0));
                action=Enemy(runtime).PendingActionId;
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
                Assert.True(Enemy(runtime).Records.Any(r=>r.ActionId==action&&r.Status==PlayableAiDeliveryStatus.Stopped));
                using(var next=new PlayableRuntime(PlayableProfile.Default,709,19092026))
                {
                    Assert.AreEqual(709,Enemy(next).Generation);
                    Assert.Zero(Enemy(next).LastCommandSequence);
                    Assert.IsEmpty(Enemy(next).Records);
                    Assert.True(Until(()=>Enemy(next).Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied)));
                    Assert.True(Enemy(next).Records.All(r=>r.ObservationIdentity.Contains(":709:")));
                }
            }
            finally{runtime.Dispose();}
        }

        [Test]
        public void ScriptedPatrolExistsOnlyWithoutEnemyOwnerLoop()
        {
            var field=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true)
                .GetField("scriptedEnemyPatrol",BindingFlags.Instance|BindingFlags.NonPublic);
            var domain=typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic);
            using(var ordinary=new PlayableRuntime(PlayableProfile.Default,710,19092026))
            using(var diagnostic=new PlayableRuntime(PlayableProfile.Default,711,19092026,false))
            {
                Assert.False((bool)field.GetValue(domain.GetValue(ordinary)));
                Assert.True((bool)field.GetValue(domain.GetValue(diagnostic)));
                Assert.IsNull(Enemy(diagnostic));
            }
        }

        [Test]
        public void EnemyScoutDeliveryDoesNotBlockSubsequentPolicyActions()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,712,19092026))
            {
                Assert.True(Until(()=>Enemy(runtime).Records.Count(r=>r.Status==PlayableAiDeliveryStatus.Applied)>=3||
                    Enemy(runtime).Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Rejected),10));
                var rejected=Enemy(runtime).Records.FirstOrDefault(r=>r.Status==PlayableAiDeliveryStatus.Rejected);
                Assert.IsNull(rejected,rejected==null?null:rejected.RuntimeStatus+": "+rejected.Message);
                Assert.AreEqual(PlayableCommandKind.Move,Enemy(runtime).LastActionKind);
            }
        }

        [Test]
        public void HumanPlayerCommandCancelsOnlyThePlayerOwnerPendingAction()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,713,19092026))
            {
                Assert.True(Until(()=>runtime.AiCheckpoint.PendingActionId!=0&&Enemy(runtime).PendingActionId!=0));
                var playerAction=runtime.AiCheckpoint.PendingActionId;
                var enemyAction=Enemy(runtime).PendingActionId;
                Assert.True(runtime.TrySubmit(new PlayableCommand(713,1,"player-1",PlayableCommandKind.Stop,new[]{3})).Accepted);
                Assert.True(Until(()=>runtime.AiCheckpoint.Records.Any(r=>r.ActionId==playerAction&&r.Status==PlayableAiDeliveryStatus.Cancelled)&&
                    Enemy(runtime).Records.Any(r=>r.ActionId==enemyAction&&r.Status==PlayableAiDeliveryStatus.Applied)));
                Assert.False(Enemy(runtime).Records.Any(r=>r.ActionId==enemyAction&&r.Status==PlayableAiDeliveryStatus.Cancelled));
            }
        }
        [Test]
        public void ExternalHumanAdmissionStillRejectsEnemyOwner()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,706,19092026,false))
            {
                var result=runtime.TrySubmit(new PlayableCommand(706,1,"enemy-1",PlayableCommandKind.Stop,new[]{3}));
                Assert.AreEqual(PlayableCommandStatus.InvalidOwner,result.Status);
                Assert.IsEmpty(runtime.DrainReceipts());
            }
        }
        private static bool Until(Func<bool> condition,int seconds=7)
        {
            var end=DateTime.UtcNow.AddSeconds(seconds);
            while(DateTime.UtcNow<end){if(condition())return true;Thread.Sleep(20);}
            return condition();
        }

        [Test]
        public void NormalRuntimeAppliesOwnerAiEconomyWithAuthorityReceipt()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,701,19092026))
            {
                Assert.True(Until(()=>runtime.AiCheckpoint.Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied)));
                var checkpoint=runtime.AiCheckpoint;
                var applied=checkpoint.Records.First(r=>r.Status==PlayableAiDeliveryStatus.Applied);
                Assert.AreEqual("player-1",checkpoint.OwnerId);
                Assert.AreEqual(PlayableAiOpeningComposition.SourceIdentity,checkpoint.SourceIdentity);
                Assert.AreEqual(19092026,checkpoint.Seed);
                Assert.AreEqual(PlayableProfile.Default.ProfileId,checkpoint.ProfileId);
                Assert.AreEqual(PlayableProfile.Default.Revision,checkpoint.ProfileRevision);
                Assert.AreEqual(701,checkpoint.Generation);
                Assert.AreEqual("economy",checkpoint.LastPolicy);
                Assert.AreEqual(PlayableCommandKind.BuildAt,checkpoint.LastActionKind);
                Assert.AreEqual("player-1",checkpoint.Opening.OwnerId);
                Assert.Greater(applied.CommandSequence,0);
                Assert.Greater(applied.ApplicationTick,0);
                Assert.True(runtime.Latest.Buildings.Any(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory));
                Assert.False(runtime.Latest.Buildings.Any(b=>b.Owner==PlayableOwner.Enemy&&b.PrivateState!=null));
                Assert.False(runtime.DrainReceipts().Any(),"AI receipts must not corrupt external-input backlog accounting.");
            }
        }

        [Test]
        public void HumanAdmissionCancelsPendingAiActionBeforeDelivery()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,702,19092026))
            {
                Assert.True(Until(()=>runtime.AiCheckpoint.PendingActionId!=0));
                var pending=runtime.AiCheckpoint.PendingActionId;
                Assert.True(runtime.TrySubmit(new PlayableCommand(702,1,"player-1",PlayableCommandKind.Stop,new[]{3})).Accepted);
                Assert.True(Until(()=>runtime.AiCheckpoint.Records.Any(r=>r.ActionId==pending&&r.Status==PlayableAiDeliveryStatus.Cancelled)));
                Assert.False(runtime.AiCheckpoint.Records.Any(r=>r.ActionId==pending&&r.Status==PlayableAiDeliveryStatus.Applied));
                Assert.True(Until(()=>runtime.DrainReceipts().Any(r=>r.Sequence==1)));
            }
        }

        [Test]
        public void PauseAndStopTerminatePendingWithoutApplication()
        {
            var runtime=new PlayableRuntime(PlayableProfile.Default,703,19092026);
            try
            {
                Assert.True(Until(()=>runtime.AiCheckpoint.PendingActionId!=0));
                var action=runtime.AiCheckpoint.PendingActionId;
                runtime.RequestPause(true);
                Assert.True(Until(()=>runtime.AiCheckpoint.Records.Any(r=>r.ActionId==action&&r.Status==PlayableAiDeliveryStatus.Cancelled)));
                Assert.False(runtime.AiCheckpoint.Records.Any(r=>r.ActionId==action&&r.Status==PlayableAiDeliveryStatus.Applied));
                runtime.RequestPause(false);
                Assert.True(Until(()=>runtime.AiCheckpoint.PendingActionId!=0));
                action=runtime.AiCheckpoint.PendingActionId;
                runtime.RequestStop();
                Assert.True(Until(()=>runtime.IsStopped));
                Assert.True(runtime.AiCheckpoint.Records.Any(r=>r.ActionId==action&&r.Status==PlayableAiDeliveryStatus.Stopped));
            }
            finally{runtime.Dispose();}
        }

        [Test]
        public void NewGenerationStartsFreshOwnerCheckpoint()
        {
            var old=new PlayableRuntime(PlayableProfile.Default,704,19092026);
            try
            {
                Assert.True(Until(()=>old.AiCheckpoint.PendingActionId!=0));
                var oldAction=old.AiCheckpoint.PendingActionId;
                old.RequestStop();
                Assert.True(Until(()=>old.IsStopped));
                Assert.True(old.AiCheckpoint.Records.Any(r=>r.ActionId==oldAction&&r.Status==PlayableAiDeliveryStatus.Stopped));
                using(var next=new PlayableRuntime(PlayableProfile.Default,705,19092026))
                {
                    Assert.AreEqual(705,next.AiCheckpoint.Generation);
                    Assert.Zero(next.AiCheckpoint.LastCommandSequence);
                    Assert.Zero(next.AiCheckpoint.Records.Count);
                    Assert.AreEqual(PlayableAiOpeningComposition.SourceIdentity,next.AiCheckpoint.SourceIdentity);
                    Assert.True(Until(()=>next.AiCheckpoint.Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied)));
                    Assert.True(next.AiCheckpoint.Records.All(r=>r.ObservationIdentity.Contains(":705:")));
                }
            }
            finally{old.Dispose();}
        }
    }
}
