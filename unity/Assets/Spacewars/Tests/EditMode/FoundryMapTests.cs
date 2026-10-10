using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class FoundryMapTests
    {
        private static PlayableProfile Profile(FoundryProfileData data=null)=>PlayableProfile.Create(PlayableProfile.Default.CopyData(),new FoundryMap(data??new FoundryProfileData()));
        [Test] public void CountsAssignmentsAndExistingOwnershipMaterializeSixSeparateStarts()
        {
            var p=Profile();var m=(FoundryMap)p.AuthoredMap;var c=m.Configuration(p,19092026);var a=new OfflineParticipantAuthority(c,1);
            Assert.AreEqual(6,c.Starts.Count);Assert.AreEqual(14,c.Sites.Count(s=>s.Kind==PlayableBuildingKind.Mine));Assert.AreEqual(4,c.Sites.Count(s=>s.Kind==PlayableBuildingKind.Outpost));
            for(int i=0;i<6;i++){Assert.AreEqual(i,c.Assignments[i]);Assert.AreEqual(i<3?1:2,c.Roster[i].Team);var v=a.View(c.Roster[i].Id);Assert.AreEqual(c.Roster[i].Team,v.Team);Assert.AreEqual(6,v.Participants.Count);Assert.AreEqual(c.Starts[i].Position,v.Buildings.Single(b=>b.Owner==(PlayableOwner)i&&b.Kind==PlayableBuildingKind.Headquarters).Position);Assert.AreEqual(1,v.Entities.Count(e=>e.Owner==(PlayableOwner)i));}
            var mines=c.Sites.Where(s=>s.Kind==PlayableBuildingKind.Mine).ToArray();Assert.Less(mines[12].Position.X,0);Assert.Greater(mines[12].Position.Z,0);Assert.Greater(mines[13].Position.X,0);Assert.Less(mines[13].Position.Z,0);
        }
        [Test] public void EveryRampSeamAndFreeCellHasContinuousSupportedHeight()
        {
            var p=Profile();var m=(FoundryMap)p.AuthoredMap;
            for(double z=-129;z<130;z+=1)for(double x=-89;x<90;x+=1)
            {
                var q=new NavPoint(x,z);if(!m.SupportsFootprint(q,p.TankCollisionRadius))continue;
                foreach(var d in new[]{new NavPoint(.01,0),new NavPoint(0,.01)}){var next=new NavPoint(x+d.X,z+d.Z);if(!m.SupportsFootprint(next,p.TankCollisionRadius))continue;Assert.Less(Math.Abs(m.SurfaceHeight(next)-m.SurfaceHeight(q)),.01,"step at "+x+","+z);}
            }
            foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1}){Assert.AreEqual(0,m.SurfaceHeight(m.Point(side*28,end*35)),1e-8);Assert.AreEqual(m.UpperHeight,m.SurfaceHeight(m.Point(side*60,end*35)),1e-8);}
            Assert.AreEqual(0,m.SurfaceHeight(new NavPoint(0,0)));Assert.AreEqual(6,m.SurfaceHeight(new NavPoint(77,0)));
            foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1})Assert.False(m.SupportsSweep(m.Point(side*77,end*90),m.Point(side*77,end*128),p.TankCollisionRadius),"No direct flank-to-rear shortcut");
        }
        [Test] public void FullyBuiltEnvelopesAndFactoryExitsKeepAllSitesConnected()
        {
            var p=Profile();TerritoryRules.ValidateArena(p);var m=(FoundryMap)p.AuthoredMap;var sites=m.Sites(p);
            var blockers=m.Solids.ToList();foreach(var site in sites){var r=TerritoryRules.Radius(p,site.Kind);blockers.Add(new NavObstacle(site.Position.X-r,site.Position.Z-r,site.Position.X+r,site.Position.Z+r));foreach(var slot in site.Slots){r=Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius));blockers.Add(new NavObstacle(slot.Position.X-r,slot.Position.Z-r,slot.Position.X+r,slot.Position.Z+r));}}
            var geometry=new NavGeometry(m.HalfExtent,blockers.ToArray(),1);var home=m.Configuration(p,7).Starts[0].ExplorerAnchor;
            foreach(var kind in new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval})
            {
                double radius=PlayableUnitRules.Radius(p,kind);var router=new SharedFlowRouter(geometry,p.Navigation.ForUnit(radius,PlayableUnitRules.Speed(p,kind),p.Navigation.TankTurnSpeed));
            // Static corridors are undirected: one shared home-goal field proves every typed connection without rebaking a field per point.
            foreach(var site in sites){var approach=new NavPoint(site.Position.X,site.Position.Z+(site.Position.Z>0?-1:1)*(site.Kind==PlayableBuildingKind.Mine?TerritoryRules.Radius(p,site.Kind)+3:12*m.Scale));if(!geometry.IsFree(approach,radius))approach=new NavPoint(site.Position.X+TerritoryRules.Radius(p,site.Kind)+3,site.Position.Z);Assert.IsNotEmpty(router.FindPath(approach,home),"site "+site.Id);}
            foreach(var site in sites)foreach(var slot in site.Slots){var exit=new NavPoint(slot.Position.X+Math.Cos(slot.Heading)*p.FactoryExitDistance,slot.Position.Z+Math.Sin(slot.Heading)*p.FactoryExitDistance);Assert.IsNotEmpty(router.FindPath(exit,home),"factory exit "+site.Id+":"+slot.Id);}
            foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1})Assert.True(geometry.SegmentFree(m.Point(0,end*35),m.Point(side*77,end*35),radius));
            }
        }
        [Test] public void TeamsHaveMirroredRouteCostsAndShortRearAssistance()
        {
            var p=Profile();var c=((FoundryMap)p.AuthoredMap).Configuration(p,7);
            for(int a=0;a<3;a++)for(int b=0;b<3;b++)Assert.AreEqual(c.RouteCost(a,b),c.RouteCost(a+3,b+3),1e-6);
            Assert.Less(c.RouteCost(0,2),c.RouteCost(0,3));
        }
        [Test] public void CurrentFactoryConstructionWorksAtEachHomeWithoutChangingRules()
        {
            var p=Profile();var c=((FoundryMap)p.AuthoredMap).Configuration(p,7);var a=new OfflineParticipantAuthority(c,1);
            for(int i=0;i<6;i++){var v=a.View(c.Roster[i].Id);var h=v.Buildings.Single(b=>b.Owner==(PlayableOwner)i&&b.Kind==PlayableBuildingKind.Headquarters);var receipt=a.Apply(new PlayableCommand(1,1,c.Roster[i].Id,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:h.SiteId,slotId:1,buildingKind:PlayableBuildingKind.Factory,parentId:h.Id));Assert.That(receipt.Status,Is.EqualTo(PlayableCommandStatus.Accepted).Or.EqualTo(PlayableCommandStatus.Applied),receipt.Message);}
            for(int i=0;i<(p.FactoryBuildSeconds+1)*30;i++)a.Step(1d/30);
            for(int i=0;i<6;i++)Assert.AreEqual(ConstructionPhase.Ready,a.View(c.Roster[i].Id).Buildings.Single(b=>b.Owner==(PlayableOwner)i&&b.Kind==PlayableBuildingKind.Factory).Phase);
        }
        [Test] public void SixOwnerAuthoredTerrainRestoresItsExactBinding()
        {
            var p=Profile();var c=((FoundryMap)p.AuthoredMap).Configuration(p,7);var a=new OfflineParticipantAuthority(c,1);var bytes=a.CaptureBytes();var restored=OfflineParticipantAuthority.Restore(bytes,c);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());
            var changed=Profile(new FoundryProfileData{flankHeight=7});var other=((FoundryMap)changed.AuthoredMap).Configuration(changed,7);Assert.Throws<ArgumentException>(()=>OfflineParticipantAuthority.Restore(bytes,other));
            var lowerRear=Profile(new FoundryProfileData{rearHeight=2});var rear=((FoundryMap)lowerRear.AuthoredMap).Configuration(lowerRear,7);Assert.Throws<ArgumentException>(()=>OfflineParticipantAuthority.Restore(bytes,rear));
        }
        [Test] public void InvalidProfileFailsAndSupportedScaleRangeRetainsEnvelopes()
        {
            Assert.Throws<ArgumentOutOfRangeException>(()=>Profile(new FoundryProfileData{scale=.5}));Assert.Throws<ArgumentOutOfRangeException>(()=>Profile(new FoundryProfileData{flankHeight=double.NaN}));
            Assert.DoesNotThrow(()=>Profile(new FoundryProfileData{scale=1.5,flankHeight=9}));
        }
    }
}
