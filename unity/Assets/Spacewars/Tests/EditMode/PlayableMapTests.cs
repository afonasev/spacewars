using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableMapTests
    {
        [Test] public void RectangularMapRoundtripsAndKeepsNorthAtTop()
        {
            var map=new PlayableMapTransform(40,15);
            foreach(var point in new[]{new NavPoint(-40,15),new NavPoint(40,-15),new NavPoint(12,7)}){var actual=map.Ground(map.Project(point));Assert.AreEqual(point.X,actual.X,.00001);Assert.AreEqual(point.Z,actual.Z,.00001);}
            Assert.AreEqual(0,map.Project(new NavPoint(-40,15)).X);Assert.AreEqual(0,map.Project(new NavPoint(-40,15)).Z);
            Assert.AreEqual(40,map.Ground(new NavPoint(2,2)).X);Assert.AreEqual(-15,map.Ground(new NavPoint(2,2)).Z);
        }
        [Test] public void ProjectionPreservesAnchorsTypedConstructionAndMinimalMemoryWithoutSlots()
        {
            var p=PlayableProfile.Default;var vision=new PlayableVision(0,32,32,4,1.5);
            var known=new KnownBuilding(44,1,PlayableOwner.Enemy,PlayableBuildingKind.Factory,new NavPoint(20,0),0);
            vision.Refresh(new[]{new VisionSource(new NavPoint(20,0),8)},new[]{known});vision.Refresh(new[]{new VisionSource(new NavPoint(-21,0),22)},new[]{known});
            var sites=TerritoryRules.Sites(p);var buildings=new[]{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,sites[0].Position,400,1,0,0,default(NavPoint),siteId:1),new PlayableBuildingSnapshot(5,PlayableOwner.Player,PlayableBuildingKind.Outpost,sites[2].Position,300,.5,0,0,default(NavPoint),siteId:3,phase:ConstructionPhase.Constructing)};
            var entities=new[]{new PlayableEntitySnapshot(10,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-10,0),100,false,0,0,0),new PlayableEntitySnapshot(11,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-11,0),100,false,0,0,0)};
            var view=new PlayableSnapshot(p.ProfileId,1,1,7,1,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,500,new NavGeometry(32,Array.Empty<NavObstacle>(),1),entities,buildings,Array.Empty<PlayableProjectileSnapshot>(),null,null,vision:vision.Snapshot(),discoveredSites:new[]{sites[0],sites[2]});
            var marks=PlayableMapView.Marks(view);Assert.AreEqual(5,marks.Count);Assert.False(marks.Any(m=>m.Start==2));
            Assert.AreEqual(sites[2].Position,marks.Single(m=>m.Id==5).Position);Assert.AreEqual(MapMarkState.Construction,marks.Single(m=>m.Id==5).State);Assert.AreEqual(MapMarkState.Memory,marks.Single(m=>m.Id==44).State);
            CollectionAssert.AreEqual(new[]{10},PlayableMapView.SelectOwnUnits(view,new NavPoint(-15,-5),new NavPoint(-5,5)));
        }
    }
}
