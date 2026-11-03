using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode
{
    public sealed class AbilityFeedbackAudioTests
    {
        private PlayableSnapshot Frame(long tick,PlayableSoundEvent[] sounds=null)=>new PlayableSnapshot("test",1,1,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,ownerId:"player-1",sounds:sounds);
        private GameplayAudioScreen Screen(PlayableSnapshot f)=>new GameplayAudioScreen(f,new NavPoint(0,0),new NavPoint(1,0));
        [TestCase(PlayableSoundKind.OrbitalSystemsDisabled,"ability-system-failure")]
        [TestCase(PlayableSoundKind.OrbitalScanApplied,"ability-scan")]
        [TestCase(PlayableSoundKind.OrbitalMineInstalled,"ability-mine")]
        [TestCase(PlayableSoundKind.OrbitalControlHacked,"ability-hack")]
        public void DistinctAbilityClipDeduplicatesAndKeepsOwnOffscreenConfirmation(PlayableSoundKind kind,string clip)
        {
            var profile=new GameplayAudioProfile();var plan=new GameplayAudioPlan(profile);plan.Observe(new[]{Screen(Frame(0))},0,true);
            var fact=new PlayableSoundEvent(1,1,kind,new NavPoint(200,0),PlayableEntityKind.Tank,owner:PlayableOwner.Player);
            var view=Frame(1,new[]{fact});var cues=plan.Observe(new[]{Screen(view),Screen(view)},.1,true);
            Assert.AreEqual(1,cues.Length);Assert.AreEqual(clip,cues[0].Clip);Assert.AreEqual(profile.abilityGain,cues[0].Gain);Assert.False(cues[0].Notification);
            Assert.IsEmpty(plan.Observe(new[]{Screen(view)},.2,true));
        }
        [Test] public void AbilityAudioMetadataHasStableValidatedRange()
        {var field=GameplayAudioMetadata.Fields.Single(f=>f.Path=="audio.abilityGain");Assert.Throws<ArgumentOutOfRangeException>(()=>field.Validate(2));field.Validate(new GameplayAudioProfile().abilityGain);}
    }
}
