using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationArrivalFairServiceTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        [Test,Timeout(60000)] public void IndependentShortMoveMakesFirstMotionWhileEarlierScoutStillComputesThroughThreadedHost()
        {
            Assert.AreEqual(376,NavStorageLayout.ObjectBytesBound(typeof(NavigationHostRoot)),"preserve the pre-policy HostRoot layout charge");
            var profile=PlayableProfile.ThreeCrossingsDefault;
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=19092026};setup.InitializeParticipants();foreach(var p in setup.Participants)p.Human=true;
            var seed=NativeLobbyMatch.Create(profile,setup);var costs=new double[seed.Starts.Count,seed.Starts.Count];
            for(int i=0;i<seed.Starts.Count;i++)for(int j=0;j<seed.Starts.Count;j++)costs[i,j]=seed.RouteCost(i,j);
            int logical=seed.Roster[0].LogicalPlayer;
            var config=new OfflineMatchConfiguration(profile,seed.SourceIdentity,seed.MapIdentity,seed.RouteProvenance,seed.Seed,seed.Roster.ToArray(),seed.Starts.ToArray(),seed.Sites.ToArray(),seed.Obstacles.ToArray(),costs,seed.Spectators.ToArray(),new[]{
                new OfflineScenarioUnit(logical,PlayableEntityKind.Tank,new NavPoint(-18,-1.8),0),
                new OfflineScenarioUnit(logical,PlayableEntityKind.Tank,new NavPoint(-24,0),0)
            },profile.AuthoredMap.Id,seed.ScenarioBuildings.ToArray());
            var runtime=new PlayableRuntime(config,71,startPaused:true);var host=new UnityHostRouteService();
            var domain=typeof(PlayableRuntime).GetField("domain",Hidden).GetValue(runtime);
            var nav=(NavigationSession)domain.GetType().GetProperty("Navigation",Hidden).GetValue(domain);var clock=Stopwatch.StartNew();
            bool Until(Func<bool> condition,int ms){long start=clock.ElapsedMilliseconds;while(clock.ElapsedMilliseconds-start<ms){host.Service(runtime,profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);if(condition())return true;Thread.Sleep(1);}return false;}
            try{
                Assert.True(Until(()=>runtime.IsPrepared&&runtime.Latest.Paused,5000));
                var scout=runtime.Latest.Entities.Single(e=>e.Kind==PlayableEntityKind.Tank&&e.Position.Equals(new NavPoint(-18,-1.8)));
                var shortActor=runtime.Latest.Entities.Single(e=>e.Kind==PlayableEntityKind.Tank&&e.Position.Equals(new NavPoint(-24,0)));
                string owner=config.Roster[0].Id;runtime.RequestPause(false);Assert.True(Until(()=>!runtime.Latest.Paused,5000));
                Assert.True(runtime.TrySubmit(new PlayableCommand(71,1,owner,PlayableCommandKind.Move,new[]{scout.Id},new NavPoint(36,46))).Accepted);
                bool firstApplied=false;Assert.True(Until(()=>{firstApplied|=runtime.DrainReceipts().Any(r=>r.Sequence==1&&r.Status==PlayableCommandStatus.Applied);var root=nav.PinCommittedHostRoot();try{return firstApplied&&root.ArrivalWork.Count==1&&root.ArrivalWork.Any(p=>((NavigationArrivalWork)p.Value).RouteCounters.PhysicalGraphWork>0);}finally{root.Release();}},5000));
                var firstRequest=nav.PendingRequestFor(scout.Id);Assert.NotNull(firstRequest);long submitted=runtime.Latest.Tick;
                var geometry=(NavGeometry)domain.GetType().GetProperty("Geometry",Hidden).GetValue(domain);double radius=PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank);
                var shortGoal=new[]{new NavPoint(-22,0),new NavPoint(-24,4),new NavPoint(-24,-4),new NavPoint(-26,0)}.First(p=>geometry.IsFree(p,radius)&&profile.AuthoredMap.TryLocate(p,radius,out _));
                Assert.True(geometry.IsFree(shortGoal,radius));Assert.True(profile.AuthoredMap.TryLocate(shortGoal,radius,out _));
                TestContext.WriteLine("SHORT_GOAL "+shortGoal.X+","+shortGoal.Z);
                Assert.True(runtime.TrySubmit(new PlayableCommand(71,2,owner,PlayableCommandKind.Move,new[]{shortActor.Id},shortGoal)).Accepted);
                PlayableCommandReceipt shortReceipt=null;Assert.True(Until(()=>{shortReceipt=runtime.DrainReceipts().LastOrDefault(r=>r.Sequence==2&&r.Status!=PlayableCommandStatus.Accepted)??shortReceipt;return shortReceipt!=null;},5000));
                Assert.AreEqual(PlayableCommandStatus.Applied,shortReceipt.Status,shortReceipt.Message);
                bool secondApplied=true,overlap=false;long services=0;long maxCursor=0;
                Assert.True(Until(()=>{
                    secondApplied|=runtime.DrainReceipts().Any(r=>r.Sequence==2&&r.Status==PlayableCommandStatus.Applied);
                    var root=nav.PinCommittedHostRoot();try{Assert.True(root.FairArrivals);overlap|=root.ArrivalWork.Count>=2;services=Math.Max(services,root.Progress);maxCursor=Math.Max(maxCursor,root.LastServedArrival);}finally{root.Release();}
                    Assert.LessOrEqual(runtime.Latest.Tick-submitted,600,"independent first motion must fit the unchanged20s logical deadline");
                    return secondApplied&&!runtime.Latest.Entities.Single(e=>e.Id==shortActor.Id).Position.Equals(shortActor.Position);
                },30000),"short original command must activate and move through the actual worker before the earlier heavy query finishes");
                Assert.True(overlap,"both independent admitted attempts must coexist");Assert.Greater(maxCursor,1);Assert.True(nav.IsPending(scout.Id));Assert.AreEqual(firstRequest.Request,nav.PendingRequestFor(scout.Id).Request);
                Assert.IsNull(host.CertifiedPublication.Failure);Assert.Greater(host.CertifiedPublication.Pumps,0);
                TestContext.WriteLine("ARRIVAL_FAIR_THREADED firstMotion="+runtime.Latest.Tick+" elapsedTicks="+(runtime.Latest.Tick-submitted)+" firstRequest="+firstRequest.Request+" lastServed="+maxCursor+" progress="+services);
            }catch(Exception){
                var root=nav.PinCommittedHostRoot();try{
                    TestContext.WriteLine("FAIR_FAILURE tick="+runtime.Latest.Tick+" cursor="+root.LastServedArrival+" stage="+root.Frontier.Stage+" serial="+root.QuantumSerial+" workCount="+root.ArrivalWork.Count+" allocating="+nav.AllocationCount);
                    foreach(var pair in root.ArrivalWork){var w=(NavigationArrivalWork)pair.Value;var c=w.RouteCounters;TestContext.WriteLine("ATTEMPT id="+pair.Key+" members="+string.Join(",",w.PhysicalInput.Members.Select(m=>m.EntityId))+" status="+w.Status+" phase="+w.WorkPhase+" epoch="+w.LogicalEpoch+" allocation="+w.AllocationWorkConsumed+" member="+w.MemberCursor+" graph="+c.PhysicalGraphWork+" field="+c.PhysicalFieldWork+" connector="+c.ConnectorWork+" ready="+w.ReadyRouteCount+" pending="+w.PendingRouteCount+" blocked="+w.LastAdvanceRetentionBlocked);}
                    TestContext.WriteLine("ENTITIES "+string.Join(";",runtime.Latest.Entities.Where(e=>e.Kind==PlayableEntityKind.Tank).Select(e=>e.Id+":"+e.Position+":"+e.NavigationOutcome)));
                }finally{root.Release();}throw;
            }finally{
                var worker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",Hidden).GetValue(host);runtime.Dispose();host.Dispose();
                Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000));nav.ReleaseCommittedHostRoot();domain.GetType().GetMethod("DestroyOwnedNavigation",Hidden).Invoke(domain,null);
                Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);
            }
        }
    }
}
