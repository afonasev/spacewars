using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class SelectedAbilityAudioImportTests
    {
        [UnityTest] public IEnumerator SelectedResourcesLoadAndRouteAcrossFramesWithoutAudioOutput()
        {
            var clips = new[] {
                ("ability-reinforcement",3f), ("ability-reinforcement-ready",1.15f),
                ("ability-stasis",2.8f), ("ability-stasis-release",.95f),
                ("ability-nanobots",3.6f), ("ability-production-boost",3.1f),
                ("ability-hack",.72f), ("ability-system-failure",1.1f), ("ability-nuclear-impact",3.05f),
                ("ability-nuclear-descent",.5f), ("ability-scan",.9f), ("ability-mine",.45f)
            };
            foreach (var item in clips)
            {
                var clip = Resources.Load<AudioClip>("Sfx/Hybrid/"+item.Item1+"-1");
                Assert.IsNotNull(clip, item.Item1);
                Assert.AreEqual(item.Item2, clip.length, .002, item.Item1);
                Assert.Greater(clip.samples, 0);
                Assert.IsTrue(clip.LoadAudioData(), item.Item1);
            }
            yield return null;
            var plan = new GameplayAudioPlan(new GameplayAudioProfile());
            GameplayAudioScreen Screen(long tick, PlayableSoundEvent[] sounds = null) => new GameplayAudioScreen(
                new PlayableSnapshot("test",1,1,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,
                    Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),
                    new PlayableRuntimeMetrics(0,0,0,0,0),null,ownerId:"player-1",sounds:sounds), default, new NavPoint(1,0));
            Assert.IsEmpty(plan.Observe(new[]{Screen(0)},0,true));
            var sound = new PlayableSoundEvent(1,1,PlayableSoundKind.OrbitalReinforcementReady,default,PlayableEntityKind.Tank,owner:PlayableOwner.Player);
            var screen = Screen(1,new[]{sound});
            Assert.AreEqual("ability-reinforcement-ready", plan.Observe(new[]{screen,screen},.1,true).Single().Clip);
            yield return null;
            Assert.IsEmpty(plan.Observe(new[]{screen},.2,true));
            Assert.IsEmpty(plan.Observe(new[]{screen},.3,false));
            Assert.IsEmpty(plan.Observe(new[]{screen},.4,true), "resume baselines existing events");
        }
    }
}
