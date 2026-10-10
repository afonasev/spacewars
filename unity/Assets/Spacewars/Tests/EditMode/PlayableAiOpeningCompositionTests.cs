using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableAiOpeningCompositionTests
    {
        private static bool Until(Func<bool> predicate)=>SpinWait.SpinUntil(predicate,8000);

        [Test] public void NativeDescriptorRetainsTheSupportedOpenings()
        {
            CollectionAssert.AreEqual(new[]{PlayableAiOpening.Safe,PlayableAiOpening.GreedySafe,PlayableAiOpening.GreedyMine,PlayableAiOpening.BlindRush,PlayableAiOpening.ExplorerAllIn,PlayableAiOpening.DoubleMineExplorerRush},PlayableAiOpeningComposition.Supported);
            Assert.AreEqual(PlayableAiOpeningComposition.ProfileBinding(AiProfile.Initial),PlayableAiOpeningComposition.SourceProfileIdentity);
            Assert.AreEqual("native-strategic-ai:opening-v1",PlayableAiOpeningComposition.SourceIdentity);
            Assert.Throws<ArgumentException>(()=>PlayableAiOpeningComposition.Initialize(1,"player-1","foreign@1"));
        }

        [Test] public void StableSeedOwnerAndProfileChooseTheSameOpeningWithoutLiveObservation()
        {
            var left=PlayableAiOpeningComposition.Initialize(41,"player-1");
            var right=PlayableAiOpeningComposition.Initialize(41,"player-1");
            Assert.True(left.SemanticallyEquals(right));
            Assert.AreEqual(PlayableAiOpeningPhase.Active,left.Phase);Assert.AreEqual(1,left.Intent.Explorer);Assert.AreEqual(1,left.Intent.Tank);Assert.AreEqual(.5,left.Intent.ExplorerShare);
            Assert.False(typeof(PlayableAiOpeningCompositionState).GetProperties().Any(x=>x.Name.Contains("Enemy")||x.Name.Contains("Vision")||x.Name.Contains("Geometry")||x.Name.Contains("Action")));
        }

        [Test] public void DeterministicSelectorCanRepresentEveryAuditedSourceOpening()
        {
            var selected=new HashSet<PlayableAiOpening>();
            for(var seed=0;seed<10000&&selected.Count<PlayableAiOpeningComposition.Supported.Count;seed++)selected.Add(PlayableAiOpeningComposition.Initialize(seed,"player-1").Opening);
            CollectionAssert.AreEquivalent(PlayableAiOpeningComposition.Supported,selected);
        }

        [Test] public void AuthorityCaptureAndRestoreRetainOwnerIntentWithoutRerolling()
        {
            var authority=new PlayableAiOpeningCompositionAuthority();var player=authority.Initialize(57,"player-1");var other=authority.Initialize(57,"player-2");
            Assert.AreNotEqual(player.OwnerId,other.OwnerId);Assert.IsNull(authority.ForOwner("enemy-private"));
            var snapshot=authority.Capture();var restored=new PlayableAiOpeningCompositionAuthority();restored.Restore(snapshot);
            Assert.True(player.SemanticallyEquals(restored.ForOwner("player-1")));Assert.True(other.SemanticallyEquals(restored.ForOwner("player-2")));
            Assert.Throws<ArgumentException>(()=>restored.Restore(new[]{player,player}));
        }

        [Test] public void DiagnosticStartupOwnsIntentButEmitsNoCommand()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,101,61,false))
            {
                Assert.True(Until(()=>runtime.Latest.Tick>1));var run=new PlayableAiDiagnosticRun(runtime,"native-flat-sandbox-u6","source:opening-composition-u6","opening-composition-u6-fixtures-v1");
                Assert.NotNull(run.OpeningCompositionAuthority);Assert.AreSame(runtime.OpeningCompositionAuthority,run.OpeningCompositionAuthority);Assert.AreSame(runtime.OpeningComposition,run.OpeningComposition);Assert.AreEqual(61,run.OpeningComposition.MatchSeed);Assert.AreEqual("player-1",run.OpeningComposition.OwnerId);Assert.Zero(run.Records.Count);
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
            }
        }
    }
}
