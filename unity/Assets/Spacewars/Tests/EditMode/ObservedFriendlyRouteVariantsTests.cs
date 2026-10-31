using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class ObservedFriendlyRouteVariantsTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        [Test] public void ActualAnchorAndFollowLegSettleTogetherAndRestoreTheirDistinctAnswers()
        {
            var source=JObject.Parse(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures/Oct29AiMatches.json")));
            var entry=source["Scenarios"].Single(s=>(int)s["Number"]==2);
            var p=PlayableProfile.Create(source["Profile"].ToObject<PlayableProfileData>(),new NullDockMap());
            var lobby=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=(int)entry["Seed"],NullDock=true,
                Participants=entry["Participants"].Select(x=>new NativeLobbyParticipant{Name=(string)x["Id"],Team=(int)x["Team"],Color=(int)x["Color"],Difficulty=(AiDifficulty)Enum.Parse(typeof(AiDifficulty),(string)x["Difficulty"])}).ToList()};
            var basis=NativeLobbyMatch.Create(p,lobby);var home=basis.Sites.Single(s=>s.Id==basis.Starts[basis.Assignments[1]].SiteId);
            // Actual pair from the current Null Dock world: the anchor approach
            // goes around the factory, while the Follow leg ends below it.
            var leaderPoint=new NavPoint(-69.01159616821786,-4.448862130946506);
            var followPoint=new NavPoint(-69.12014118483089,-6.445914453588173);
            var followerPoint=new NavPoint(-71.25861046343715,-6.820604139901075);
            int logical=basis.Roster[1].LogicalPlayer;
            var units=new[]{new OfflineScenarioUnit(logical,PlayableEntityKind.Tank,leaderPoint,Math.Atan2(leaderPoint.Z-followPoint.Z,leaderPoint.X-followPoint.X)),
                new OfflineScenarioUnit(logical,PlayableEntityKind.Tank,followerPoint),new OfflineScenarioUnit(logical,PlayableEntityKind.Tank,new NavPoint(-69.25861046343705,-6.820604139901075))};
            var buildings=home.Slots.Take(3).Select(s=>new OfflineScenarioBuilding(logical,PlayableBuildingKind.Refinery,home.Id,s.Id,s.Position,s.Heading))
                .Concat(new[]{new OfflineScenarioBuilding(logical,PlayableBuildingKind.Factory,home.Id,home.Slots[3].Id,home.Slots[3].Position,home.Slots[3].Heading)}).ToArray();
            var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            var config=new OfflineMatchConfiguration(p,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,scenario:units,scenarioBuildings:buildings,terrain:p.AuthoredMap.Id);
            var domain=new PlayableDomain(p,2,config);
            try{
                var view=domain.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed,PlayableOwner.Enemy);
                var leader=view.Entities.Single(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank&&u.Position.Equals(leaderPoint));
                var follower=view.Entities.Single(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank&&u.Position.Equals(followerPoint));
                var targets=new[]{leader.Position,PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading)};
                var requests=targets.Select(t=>new PlayableRouteRequest(follower.Id,PlayableRouteTargetKind.FriendlyAnchor,leader.Id,2,domain.Tick,follower.Position,p.TankCollisionRadius,t)).ToArray();
                var sync=domain.PlayerSnapshotWithRoutes(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed,PlayableOwner.Enemy,requests);
                Assert.AreEqual(2,sync.RouteProofs.Count,"Both actual semantic requests must be individually feasible before testing asynchronous convergence.");
                domain.EnableAsyncObservedPaths();var vision=typeof(PlayableDomain).GetMethod("Vision",F).Invoke(domain,new object[]{PlayableOwner.Enemy});
                PlayableRouteProof[] Project(){try{return (PlayableRouteProof[])typeof(PlayableDomain).GetMethod("ProjectRoutes",F).Invoke(domain,new object[]{PlayableOwner.Enemy,requests,vision,view.Entities,view.Buildings,null,true});}catch(TargetInvocationException e)when(e.InnerException is ObservedPathPending){return null;}}
                var laneField=typeof(PlayableDomain).GetField("observedPathLane",F);var coldLane=(ObservedPathLane)laneField.GetValue(domain);
                Assert.Throws<ObservedPathPending>(()=>domain.PlayerSnapshotForDelivery(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed,PlayableOwner.Enemy,requests));
                Assert.Zero(coldLane.RetainedCount,"Delivery reads cannot create a job in a cold lane.");Assert.Zero(coldLane.Count);
                PlayableRouteProof[] ready=null;
                for(int i=0;i<64&&ready==null;i++){ready=Project();domain.DrainObservedPathsForCapture(()=>false);}
                Assert.NotNull(ready,"Completed anchor and Follow jobs cannot evict each other under one cache key forever.");
                Assert.AreEqual(2,ready.Length);foreach(var target in targets)Assert.True(ready.Any(r=>r.Target.Equals(target)));
                var field=typeof(PlayableDomain).GetField("observedPathLane",F);var lane=(ObservedPathLane)field.GetValue(domain);
                byte[] Capture(ObservedPathLane value){using var s=new MemoryStream();using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))value.WriteState(w);return s.ToArray();}
                var bytes=Capture(lane);
                var delivery=domain.PlayerSnapshotForDelivery(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed,PlayableOwner.Enemy,requests);
                Assert.AreEqual(2,delivery.RouteProofs.Count);CollectionAssert.AreEqual(bytes,Capture(lane),"Ready-only delivery preserves jobs, exact answers and use clock.");
                var epochs=typeof(ObservedPathLane).GetField("epochs",F);var beforeEpochs=Newtonsoft.Json.JsonConvert.SerializeObject(epochs.GetValue(lane));
                domain.PlayerSnapshotForDelivery(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed,PlayableOwner.Enemy,requests);
                Assert.AreEqual(beforeEpochs,Newtonsoft.Json.JsonConvert.SerializeObject(epochs.GetValue(lane)),"Delivery does not start/prune a projection epoch.");
                ObservedPathLane restored;using(var r=new BinaryReader(new MemoryStream(bytes)))restored=ObservedPathLane.ReadState(r);
                CollectionAssert.AreEqual(bytes,Capture(restored),"Current route variants preserve exact completed transport state.");
                lane.Stop();Assert.True(SpinWait.SpinUntil(()=>lane.IsStopped,5000));field.SetValue(domain,restored);
                Assert.AreEqual(2,Project().Length,"Both answers remain independently available after restore.");
            }finally{domain.StopObservedPaths();Assert.True(SpinWait.SpinUntil(()=>domain.ObservedPathWorkerStopped,5000));domain.DestroyOwnedNavigation();}
        }
    }
}
