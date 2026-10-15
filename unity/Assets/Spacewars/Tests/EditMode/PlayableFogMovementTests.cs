using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableFogMovementTests
    {
        [Test] public void OwnRoutesAndTacticalPathsCrossFogButHiddenHostileTargetsRemainInvalid()
        {
            var profile=PlayableProfile.Default;var origin=default(NavPoint);var hidden=new NavPoint(-10,0);var anchor=new NavPoint(-25,0);
            var vision=new PlayableVision(0,50,50,1,1);
            vision.Refresh(new[]{new VisionSource(origin,5),new VisionSource(anchor,5)},Array.Empty<KnownBuilding>());
            Assert.True(vision.IsVisible(origin));Assert.False(vision.IsVisible(hidden));Assert.True(vision.IsVisible(anchor));

            var units=new[]{
                new PlayableEntitySnapshot(10,PlayableOwner.Player,PlayableEntityKind.Shkval,origin,profile.ShkvalHealth,false,0,0,0),
                new PlayableEntitySnapshot(11,PlayableOwner.Player,PlayableEntityKind.Tank,anchor,profile.TankHealth,false,0,0,0)
            };
            var friendly=new PlayableRouteProof(10,PlayableOwner.Player,1,1,1,origin,PlayableUnitRules.Radius(profile,PlayableEntityKind.Shkval),
                PlayableRouteTargetKind.FriendlyAnchor,11,anchor,anchor,new[]{hidden,anchor});
            var support=new PlayableArtillerySupportSnapshot(10,1,1,true,false,true,true,hidden);
            var snapshot=new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,
                units,Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                vision:vision.Snapshot(),routeProofs:new[]{friendly},artillerySupport:new[]{support},owner:PlayableOwner.Player,activeProfile:profile);
            var observation=PlayableAiObservation.From(snapshot);

            Assert.AreEqual(friendly.UnitId,observation.RouteProofs.Single().UnitId,"A currently bound own route remains valid when its road crosses fog");
            Assert.AreEqual(hidden,observation.ArtillerySupport.Single().Position.Value,"An owned artillery reposition goal may be beyond current vision");
            Assert.True(AiTacticalExecutor.SafePath(observation,origin,new[]{hidden,anchor},friendly.Radius,profile));
            Assert.True(AiTacticalExecutor.WithdrawalPath(observation,origin,new[]{hidden,anchor},friendly.Radius,profile));
        }
    }
}
