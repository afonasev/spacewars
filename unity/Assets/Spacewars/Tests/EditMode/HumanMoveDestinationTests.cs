using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class HumanMoveDestinationTests
    {
        static PlayableEntitySnapshot Unit(int id,NavPoint point)=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,point,100,false,0,0,0);
        static PlayableSnapshot View(PlayableProfile profile,NavGeometry geometry,params PlayableEntitySnapshot[] units)=>new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,1,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,100,geometry,units,Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,activeProfile:profile);
        [Test] public void ReachableTargetKeepsExactCoordinatesEvenWhenItNeedsADetour()
        {
            var p=PlayableProfile.Default;var geometry=new NavGeometry(10,new[]{new NavObstacle(-1,-10,1,2)},1);var target=new NavPoint(5,.123);
            var result=HumanMoveDestination.Resolve(p,View(p,geometry,Unit(1,new NavPoint(-5,0))),new[]{1},target,geometry).Single();
            Assert.AreEqual(target,result.Target);CollectionAssert.AreEqual(new[]{1},result.Entities);
        }
        [Test] public void DisconnectedTargetProjectsToNearestClearBoundaryInTheOriginComponent()
        {
            var p=PlayableProfile.Default;double radius=p.TankCollisionRadius;
            var geometry=new NavGeometry(10,new[]{new NavObstacle(-1,-10,1,10)},1);var target=new NavPoint(5,0);
            var result=HumanMoveDestination.Resolve(p,View(p,geometry,Unit(1,new NavPoint(-5,0))),new[]{1},target,geometry).Single();
            Assert.Less(result.Target.X,-1-radius);Assert.That(-1-radius-result.Target.X,Is.LessThanOrEqualTo(p.Navigation.GridCell));
            Assert.That(Math.Abs(result.Target.Z),Is.LessThanOrEqualTo(p.Navigation.GridCell));Assert.True(geometry.IsFree(result.Target,radius));
        }
        [Test] public void DisconnectedRecipientsKeepAllIdsAndGetTheirOwnReachableDestination()
        {
            var p=PlayableProfile.Default;var geometry=new NavGeometry(10,new[]{new NavObstacle(-1,-10,1,10)},1);
            var result=HumanMoveDestination.Resolve(p,View(p,geometry,Unit(1,new NavPoint(-5,0)),Unit(2,new NavPoint(5,0))),new[]{1,2},default,geometry);
            Assert.AreEqual(2,result.Length);CollectionAssert.AreEquivalent(new[]{1,2},result.SelectMany(g=>g.Entities));
            Assert.Less(result.Single(g=>g.Entities.Contains(1)).Target.X,-1);Assert.Greater(result.Single(g=>g.Entities.Contains(2)).Target.X,1);
        }
        [Test] public void WaterTargetUsesSupportedClearanceWithoutChangingTheRequestedPoint()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var geometry=new NavGeometry(p.ArenaHalfExtent,PlayableMap.StaticObstacles(p),1);
            var start=new NavPoint(-20,22);var water=new NavPoint(0,22);Assert.True(p.AuthoredMap.SupportsFootprint(start,p.TankCollisionRadius));Assert.False(p.AuthoredMap.SupportsFootprint(water,p.TankCollisionRadius));
            var result=HumanMoveDestination.Resolve(p,View(p,geometry,Unit(1,start)),new[]{1},water,geometry).Single();
            Assert.AreNotEqual(water,result.Target);Assert.True(p.AuthoredMap.SupportsFootprint(result.Target,p.TankCollisionRadius));Assert.True(geometry.IsFree(result.Target,p.TankCollisionRadius));
            Assert.Less(result.Target.X,0);Assert.AreEqual(new NavPoint(0,22),water);
        }
        [Test] public void ForeignRecipientsAreLeftForNormalAuthorityRejection()
        {
            var p=PlayableProfile.Default;var geometry=new NavGeometry(10,Array.Empty<NavObstacle>(),1);var target=new NavPoint(20,0);
            var result=HumanMoveDestination.Resolve(p,View(p,geometry,Unit(1,new NavPoint(-5,0))),new[]{1,99},target,geometry).Single();
            CollectionAssert.AreEqual(new[]{1,99},result.Entities);Assert.AreEqual(target,result.Target);
        }
        [Test] public void ConnectedMixedUnitKindsKeepOneDestinationWithLargestFootprint()
        {
            var p=PlayableProfile.Default;var geometry=new NavGeometry(10,new[]{new NavObstacle(-1,-10,1,10)},1);
            var tank=Unit(1,new NavPoint(-5,-2));var explorer=new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(-5,2),100,false,0,0,0);
            var result=HumanMoveDestination.Resolve(p,View(p,geometry,tank,explorer),new[]{1,2},default,geometry);
            Assert.AreEqual(1,result.Length,"A connected mixed formation must not split into new commands solely by unit radius");CollectionAssert.AreEquivalent(new[]{1,2},result[0].Entities);
            Assert.True(geometry.IsFree(result[0].Target,Math.Max(PlayableUnitRules.Radius(p,tank.Kind),PlayableUnitRules.Radius(p,explorer.Kind))));
        }
        [TestCase(0d)][TestCase(-.4d)] public void CircularObstacleProjectsToTheClosestReachableRim(double x)
        {
            var p=PlayableProfile.Default;var geometry=new NavGeometry(10,new[]{new NavObstacle(default(NavPoint),1)},1);var requested=new NavPoint(x,0);
            var result=HumanMoveDestination.Resolve(p,View(p,geometry,Unit(1,new NavPoint(-5,0))),new[]{1},requested,geometry).Single();
            Assert.True(geometry.IsFree(result.Target,p.TankCollisionRadius));Assert.Less(result.Target.X,-1-p.TankCollisionRadius);
            Assert.That(Math.Abs(result.Target.Z),Is.LessThanOrEqualTo(p.Navigation.ArrivalTolerance));
            Assert.That(-result.Target.X-(1+p.TankCollisionRadius),Is.LessThanOrEqualTo(p.Navigation.ArrivalTolerance+.000001));
        }
        [Test] public void TemporaryWreckClosurePreservesTheOriginalDestinationForNormalWaiting()
        {
            var p=PlayableProfile.Default;var geometry=new NavGeometry(10,new[]{new NavObstacle(-10,1,10,10),new NavObstacle(-10,-10,10,-1),new NavObstacle(default(NavPoint),1)},1);
            var wreck=new PlayableWreckSnapshot(99,PlayableOwner.Player,PlayableEntityKind.Tank,default,0,1,0,0,1,0,0,1,20,30);
            var view=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,100,geometry,new[]{Unit(1,new NavPoint(-5,0))},Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,activeProfile:p,wrecks:new[]{wreck});
            var target=new NavPoint(5,0);var result=HumanMoveDestination.Resolve(p,view,new[]{1},target,geometry).Single();
            Assert.AreEqual(target,result.Target,"Temporary wreck closure must retain the waiting command, rather than become a new permanent destination");
        }
    }
}
