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
    public sealed class NavigationDemandQueryTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        [Test,Timeout(60000)] public void LongTankDetourMakesFirstMotionThroughOrdinaryThreadedHost()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=19092026};setup.InitializeParticipants();foreach(var p in setup.Participants)p.Human=true;
            var seed=NativeLobbyMatch.Create(profile,setup);var costs=new double[seed.Starts.Count,seed.Starts.Count];
            for(int i=0;i<seed.Starts.Count;i++)for(int j=0;j<seed.Starts.Count;j++)costs[i,j]=seed.RouteCost(i,j);
            int logical=seed.Roster[0].LogicalPlayer;
            var config=new OfflineMatchConfiguration(profile,seed.SourceIdentity,seed.MapIdentity,seed.RouteProvenance,seed.Seed,seed.Roster.ToArray(),seed.Starts.ToArray(),seed.Sites.ToArray(),seed.Obstacles.ToArray(),costs,seed.Spectators.ToArray(),new[]{
                new OfflineScenarioUnit(logical,PlayableEntityKind.Tank,new NavPoint(-18,-1.8),0),

            },profile.AuthoredMap.Id,seed.ScenarioBuildings.ToArray());
            var runtime=new PlayableRuntime(config,71,startPaused:true);var host=new UnityHostRouteService();
            var domain=typeof(PlayableRuntime).GetField("domain",Hidden).GetValue(runtime);
            var nav=(NavigationSession)domain.GetType().GetProperty("Navigation",Hidden).GetValue(domain);var clock=Stopwatch.StartNew();
            bool Until(Func<bool> condition,int ms){long start=clock.ElapsedMilliseconds;while(clock.ElapsedMilliseconds-start<ms){host.Service(runtime,profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);if(condition())return true;Thread.Sleep(1);}return false;}
            try{
                Assert.True(Until(()=>runtime.IsPrepared&&runtime.Latest.Paused,5000));
                var scout=runtime.Latest.Entities.Single(e=>e.Kind==PlayableEntityKind.Tank&&e.Position.Equals(new NavPoint(-18,-1.8)));
                string owner=config.Roster[0].Id;runtime.RequestPause(false);Assert.True(Until(()=>!runtime.Latest.Paused,5000));
                var geometry=(NavGeometry)domain.GetType().GetProperty("Geometry",Hidden).GetValue(domain);
                double radius=PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank);
                var provider=new PartitionNavigationProvider(profile.AuthoredMap,geometry,null);
                var clearance=new NavClearanceProfile("tank-witness",1,"ground",1,radius);
                Assert.True(provider.TryLocate(scout.Position,clearance,out var from));Assert.True(provider.TryLocate(new NavPoint(36,46),clearance,out var to));
                Assert.False(provider.CanSweep(from,to,clearance),"must require the long routed detour, not Direct");
                long submitted=runtime.Latest.Tick;bool applied=false;long graphWork=0,connectorWork=0;bool demand=false;
                Assert.True(runtime.TrySubmit(new PlayableCommand(71,1,owner,PlayableCommandKind.Move,new[]{scout.Id},new NavPoint(36,46))).Accepted);

                Assert.True(Until(()=>{
                    applied|=runtime.DrainReceipts().Any(r=>r.Sequence==1&&r.Status==PlayableCommandStatus.Applied);
                    var root=nav.PinCommittedHostRoot();try{foreach(var pair in root.ArrivalWork){var work=(NavigationArrivalWork)pair.Value;graphWork=Math.Max(graphWork,work.RouteCounters.PhysicalGraphWork);connectorWork=Math.Max(connectorWork,work.RouteCounters.ConnectorWork);demand|=((INavPhysicalBody)work).PhysicalTag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryArrivalWorkV1));}}finally{root.Release();}
                    Assert.LessOrEqual(runtime.Latest.Tick-submitted,600,"long Tank first motion must fit unchanged 600tick bound");
                    return applied&&!runtime.Latest.Entities.Single(e=>e.Id==scout.Id).Position.Equals(scout.Position);
                },30000),"ordinary threaded long Tank detour must activate and actually move");
                Assert.True(demand);Assert.Greater(graphWork,0);Assert.Greater(connectorWork,0);Assert.IsNull(host.CertifiedPublication.Failure);Assert.Greater(host.CertifiedPublication.Pumps,0);
                TestContext.WriteLine("DEMAND_THREADED firstMotionTick="+runtime.Latest.Tick+" elapsedTicks="+(runtime.Latest.Tick-submitted)+" graph="+graphWork+" connector="+connectorWork);
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
