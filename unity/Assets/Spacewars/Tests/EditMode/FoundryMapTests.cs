using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class FoundryMapTests
    {
        private static PlayableProfile Profile(FoundryProfileData data=null)=>PlayableProfile.Create(PlayableProfile.Default.CopyData(),new FoundryMap(data??new FoundryProfileData()));
        [Test]
        public void ActualFifthRouteToMine12UsesCapturedGameplayProfile()
        {
            var root=new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
            while(root!=null&&!System.IO.File.Exists(System.IO.Path.Combine(root.FullName,"workflow/project.json")))root=root.Parent;
            Assert.NotNull(root);
            var json=System.IO.File.ReadAllText(System.IO.Path.Combine(root.FullName,"unity/Tests/Fixtures/Foundry/captured-seed7-gameplay-profile.json"));
            var data=Newtonsoft.Json.JsonConvert.DeserializeObject<PlayableProfileData>(json);
            var map=new FoundryMap(new FoundryProfileData());var p=PlayableProfile.Create(data,map);
            Assert.AreEqual(5,map.Revision);Assert.AreEqual(15,p.Navigation.GroupCorridorMaximumExcessPercent);
            Assert.AreEqual(0,data.navigationMaxAccruedCredits);
            var start=new NavPoint(44,96);double radius=PlayableUnitRules.Radius(p,PlayableEntityKind.Explorer);
            Assert.AreEqual(.58,radius);Assert.AreEqual(start,map.Configuration(p,7).Starts.Single(s=>s.SiteId==3).ExplorerAnchor);
            var mine=map.Sites(p).Single(s=>s.Id==12);
            var target=new NavPoint(mine.Position.X+TerritoryRules.CaptureRadius(p,mine.Kind)-radius,mine.Position.Z);
            Assert.AreEqual(2.42,target.X,1e-9);Assert.AreEqual(71,target.Z);
            var geometry=new NavGeometry(map.HalfExtent,map.Solids.ToArray(),map.Revision);
            Assert.True(map.TryLocate(start,radius,out var location));
            using(var router=new UnityNavigationRouter(geometry,p.Navigation.ForUnit(radius,PlayableUnitRules.Speed(p,PlayableEntityKind.Explorer),p.Navigation.TankTurnSpeed))) {
                var path=router.FindPath(start,target);Assert.IsNotEmpty(path);var previous=start;
                foreach(var point in path) {
                    Assert.True(geometry.SegmentFree(previous,point,radius));
                    Assert.True(map.TryTraverse(location,point,radius,out var next),"Actual site12 route must preserve support adjacency");
                    TestContext.WriteLine("FIFTH_SITE12 leg="+previous.X+","+previous.Z+"->"+point.X+","+point.Z+" surface="+location.SurfaceId+"->"+next.SurfaceId);
                    previous=point;location=next;
                }
                Assert.AreEqual(target,previous);
                Assert.LessOrEqual(LayeredNavigationProvider.Distance(previous,mine.Position),TerritoryRules.CaptureRadius(p,mine.Kind));
            }
        }
        // Causal geometry diagnostic only: the two observed non-threshold owners'
        // assigned anchors, without an AI host, ordinary match, or replay.
        [TestCase(3,13)] [TestCase(4,14)]
        public void MissingScoutStartsReachTheirActualPublicMineApproachOnSupportedNativeRoute(int homeSite,int mineSite)
        {
            var p=Profile();var map=(FoundryMap)p.AuthoredMap;var config=map.Configuration(p,7);
            var start=config.Starts.Single(x=>x.SiteId==homeSite).ExplorerAnchor;var mine=config.Sites.Single(x=>x.Id==mineSite);
            double radius=PlayableUnitRules.Radius(p,PlayableEntityKind.Explorer);
            var target=new NavPoint(mine.Position.X+TerritoryRules.CaptureRadius(p,mine.Kind)-radius,mine.Position.Z);
            var geometry=new NavGeometry(map.HalfExtent,map.Solids.ToArray(),map.Revision);
            Assert.True(map.SupportsFootprint(start,radius));Assert.True(map.TryLocate(start,radius,out var location));
            Assert.AreEqual(map.RearHeight,map.SurfaceHeight(start),1e-9);
            Assert.True(map.SupportsFootprint(target,radius));Assert.True(map.TryLocate(target,radius,out _));
            using(var router=new UnityNavigationRouter(geometry,p.Navigation.ForUnit(radius,PlayableUnitRules.Speed(p,PlayableEntityKind.Explorer),p.Navigation.TankTurnSpeed)))
            {
                var path=router.FindPath(start,target);Assert.IsNotEmpty(path);
                var previous=start;double length=0;
                foreach(var point in path)
                {
                    Assert.True(geometry.SegmentFree(previous,point,radius));
                    Assert.True(map.TryTraverse(location,point,radius,out var next),"Every native route leg must retain declared support adjacency");
                    length+=Math.Sqrt(Math.Pow(point.X-previous.X,2)+Math.Pow(point.Z-previous.Z,2));location=next;previous=point;
                }
                Assert.LessOrEqual(Math.Sqrt(Math.Pow(previous.X-mine.Position.X,2)+Math.Pow(previous.Z-mine.Position.Z,2)),TerritoryRules.CaptureRadius(p,mine.Kind));
                TestContext.WriteLine("FOUNDRY_CAUSAL seed=7 home="+homeSite+" anchor="+start.X+","+start.Z+" mine="+mineSite+" approach="+target.X+","+target.Z+" radius="+radius+" support="+map.SupportAt(start).SurfaceId+" height="+map.SurfaceHeight(start)+" nativePoints="+string.Join(";",path.Select(q=>q.X+","+q.Z))+" length="+length+" profile="+p.ProfileId+"@"+p.Revision+" map="+map.Id+"@"+map.Revision+" only geometry, not an AI/gameplay acceptance");
            }
        }
        [Test] public void SavedSeedSevenAirOwnerSelectionIsAnExplicitNonMatchDiagnostic()
        {
            var p=Profile();var map=(FoundryMap)p.AuthoredMap;var ai=NativeBalanceFields.InitialAiOpeningProfile();
            string[] ids={"player-1","foundry-2","foundry-3","foundry-4","foundry-5","foundry-6"};int[] homes={4,5,6,1,3,2};
            var roster=ids.Select((id,i)=>new OfflineParticipant(id,i+1,i<3?1:2,OfflineControl.Ai)).ToArray();
            var binding=PlayableAiOpeningComposition.ProfileBinding(ai);var winners=new string[6];
            for(int i=0;i<6;i++)
            {
                var owner=(PlayableOwner)i;var home=map.Sites(p).Single(x=>x.Id==homes[i]);var anchor=map.Configuration(p,7).Starts.Single(x=>x.SiteId==homes[i]).ExplorerAnchor;
                int scout=100+i;var unit=new PlayableEntitySnapshot(scout,owner,PlayableEntityKind.Explorer,anchor,p.ExplorerHealth,false,0,0,0);
                var headquarters=new PlayableBuildingSnapshot(homes[i],owner,PlayableBuildingKind.Headquarters,home.Position,p.HeadquartersHealth,1,0,0,default,siteId:home.Id);
                var snapshot=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,new[]{unit},new[]{headquarters},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:new[]{new TerritorySiteSnapshot(home,owner,owner,1,false,home.Id,true)},owner:owner,ownerId:ids[i],homeSiteId:home.Id,team:roster[i].Team,participants:roster);
                var observation=PlayableAiObservation.From(snapshot);winners[i]=AiOpeningExecutor.TeamAirOwner(observation,ai,binding);
                Assert.True(winners[i]==null||roster.Any(r=>r.Id==winners[i]&&r.Team==roster[i].Team));
                var opening=PlayableAiOpeningComposition.Initialize(7,ids[i],aiProfile:ai);var executor=new AiOpeningExecutor(opening,1){PlayableProfileForObservation=p,SpendableCredits=0};executor.Observe(observation,null,null,ai);
                TestContext.WriteLine("FOUNDRY_OPENING_SOURCE seed=7 owner="+ids[i]+" logical="+(i+1)+" site="+homes[i]+" team="+roster[i].Team+" airWinner="+winners[i]+" opening="+opening.Opening+" protectedCapturer="+executor.Capture().ProtectedCapturerId+" profileBinding="+binding+" source-only initialized state, not captured tick1800");
            }
            Assert.AreEqual(winners[0],winners[1]);Assert.AreEqual(winners[1],winners[2]);Assert.AreEqual(winners[3],winners[4]);Assert.AreEqual(winners[4],winners[5]);
        }
        [Test] public void ExposedCentralShoulderTraversesTheNewSamePlaneRampSeam()
        {
            var p=Profile();var m=(FoundryMap)p.AuthoredMap;
            foreach(int side in new[]{-1,1})foreach(int direction in new[]{-1,1})
            {
                var a=m.Point(side*59.8,direction*2);var b=m.Point(side*59.8,-direction*2);
                Assert.True(m.SupportsSweep(a,b,p.TankCollisionRadius));Assert.True(m.TryLocate(a,p.TankCollisionRadius,out var from));
                Assert.True(m.TryTraverse(from,b,p.TankCollisionRadius,out var to),"Explicit adjacent authored ramp surfaces");
                Assert.AreNotEqual(from.SurfaceId,to.SurfaceId);Assert.AreEqual(m.SurfaceHeight(a),m.SurfaceHeight(b),1e-8);
            }
        }
        [Test] public void FracturedBanksWidenMineFlanksAndExposedShouldersStaySupported()
        {
            var p=Profile();var m=(FoundryMap)p.AuthoredMap;
            foreach(int side in new[]{-1,1})
            {
                Assert.True(m.SupportsFootprint(m.Point(side*92,60),p.TankCollisionRadius),"New broad mine-side apron");
                Assert.AreEqual(m.UpperHeight,m.SurfaceHeight(m.Point(side*92,60)));
                Assert.False(m.SupportsFootprint(m.Point(side*100,60),p.TankCollisionRadius),"Visible bank remains solid");
            }
            for(double x=-105;x<=105;x+=1)for(double z=-129;z<=129;z+=1)
            {
                var q=m.Point(x,z);if(m.Solids.Any(s=>s.Contains(q)))continue;
                Assert.NotNull(m.SupportAt(q),"No unsupported free hole after trimming the rock: "+x+","+z);
            }
        }
        [Test] public void CountsAssignmentsAndExistingOwnershipMaterializeSixSeparateStarts()
        {
            var p=Profile();var m=(FoundryMap)p.AuthoredMap;var c=m.Configuration(p,19092026);var a=new OfflineParticipantAuthority(c,1);
            Assert.AreEqual(6,c.Starts.Count);Assert.AreEqual(12,c.Sites.Count(s=>s.Kind==PlayableBuildingKind.Mine));Assert.AreEqual(2,c.Sites.Count(s=>s.Kind==PlayableBuildingKind.RelayTower));Assert.AreEqual(4,c.Sites.Count(s=>s.Kind==PlayableBuildingKind.Outpost));
            for(int i=0;i<6;i++){Assert.AreEqual(i,c.Assignments[i]);Assert.AreEqual(i<3?1:2,c.Roster[i].Team);var v=a.View(c.Roster[i].Id);Assert.AreEqual(c.Roster[i].Team,v.Team);Assert.AreEqual(6,v.Participants.Count);Assert.AreEqual(c.Starts[i].Position,v.Buildings.Single(b=>b.Owner==(PlayableOwner)i&&b.Kind==PlayableBuildingKind.Headquarters).Position);Assert.AreEqual(1,v.Entities.Count(e=>e.Owner==(PlayableOwner)i));}
            var relays=c.Sites.Where(s=>s.Kind==PlayableBuildingKind.RelayTower).ToArray();Assert.Less(relays[0].Position.X,0);Assert.Greater(relays[0].Position.Z,0);Assert.Greater(relays[1].Position.X,0);Assert.Less(relays[1].Position.Z,0);
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
