using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class HawkOrdinaryFlightTests
    {
        // One early ordinary witness. Fixed seed, 90s gameplay horizon; do not silently extend on failure.
        [Test,Timeout(180000)] public void HangarOrderToTakeoffAndArrivalThroughOrdinaryThreadedHost()
        {
            const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            string output=Environment.GetEnvironmentVariable("SPACEWARS_HAWK_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            var p=PlayableProfile.ThreeCrossingsDefault;var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=1024700465};setup.InitializeParticipants();
            var config=NativeLobbyMatch.Create(p,setup);var runtime=new PlayableRuntime(config,81,startPaused:true);var host=new UnityHostRouteService();
            object domain=typeof(PlayableRuntime).GetField("domain",hidden).GetValue(runtime);var nav=(NavigationSession)domain.GetType().GetProperty("Navigation",hidden).GetValue(domain);
            using var log=new StreamWriter(Path.Combine(output,"flight.csv")){AutoFlush=true};log.WriteLine("seconds,tick,stage,phase,unit,x,z,altitude,moving,outcome");
            try
            {
                var start=Stopwatch.StartNew();
                void Service(){host.Service(runtime,p.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(host.CertifiedPublication?.Failure);}
                while(!(runtime.IsPrepared&&runtime.Latest.Paused)&&start.Elapsed.TotalSeconds<60){Service();Thread.Sleep(1);}Assert.True(runtime.IsPrepared&&runtime.Latest.Paused,"Ordinary startup");
                string owner=config.Roster[0].Id;var initial=runtime.ParticipantView(owner);var hq=initial.Buildings.Single(b=>b.Owner==initial.Owner&&b.Kind==PlayableBuildingKind.Headquarters);var site=initial.Sites.Single(s=>s.Site.Id==hq.SiteId);int slot=site.Site.Slots[0].Id;
                long sequence=1;runtime.RequestPause(false);var clock=Stopwatch.StartNew();
                Assert.True(runtime.TrySubmit(new PlayableCommand(81,sequence,owner,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:hq.SiteId,slotId:slot,parentId:hq.Id,buildingKind:PlayableBuildingKind.Hangar)).Accepted);
                bool built=false,ordered=false,spawned=false,moved=false,arrived=false,moveApplied=false,buildApplied=false,orderApplied=false;int aircraft=0;long lastTick=-1;NavPoint first=default,goal=default;double firstMotion=-1;int stage=0;long moveSequence=0;
                while(clock.Elapsed.TotalSeconds<90)
                {
                    Service();var receipts=runtime.DrainReceipts();foreach(var receipt in receipts){Assert.That(receipt.Status,Is.EqualTo(PlayableCommandStatus.Applied).Or.EqualTo(PlayableCommandStatus.Accepted),receipt.Message);if(receipt.Sequence==1)buildApplied=true;if(receipt.Sequence==2)orderApplied=true;if(receipt.Sequence==moveSequence&&moveSequence>0)moveApplied=true;}
                    var view=runtime.ParticipantView(owner);var hangar=view.Buildings.FirstOrDefault(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Hangar);
                    if(!ordered&&hangar?.Phase==ConstructionPhase.Ready){built=true;ordered=true;stage=1;Assert.True(runtime.TrySubmit(new PlayableCommand(81,++sequence,owner,PlayableCommandKind.QueueHawk,new[]{hangar.Id})).Accepted);}
                    var hawk=view.Entities.FirstOrDefault(u=>u.Owner==view.Owner&&u.Kind==PlayableEntityKind.Hawk);
                    if(hawk!=null&&!spawned){spawned=true;aircraft=hawk.Id;first=hawk.Position;goal=new NavPoint(first.X+8,first.Z+2);moveSequence=++sequence;stage=2;Assert.True(runtime.TrySubmit(new PlayableCommand(81,moveSequence,owner,PlayableCommandKind.Move,new[]{aircraft},goal)).Accepted);}
                    if(hawk!=null){if(!moved&&!hawk.Position.Equals(first)){moved=true;firstMotion=clock.Elapsed.TotalSeconds;}if(hawk.NavigationOutcome==NavigationOutcome.Arrived&&Math.Sqrt(Math.Pow(hawk.Position.X-goal.X,2)+Math.Pow(hawk.Position.Z-goal.Z,2))<=p.Navigation.ArrivalTolerance+.01){arrived=true;stage=3;}}
                    if(view.Tick!=lastTick){lastTick=view.Tick;log.WriteLine($"{clock.Elapsed.TotalSeconds:R},{view.Tick},{stage},{hangar?.LaunchPhase??0},{hawk?.Id??0},{hawk?.Position.X??0:R},{hawk?.Position.Z??0:R},{hawk?.Altitude??0:R},{hawk?.Moving??false},{hawk?.NavigationOutcome}");}
                    if(arrived&&hangar?.LaunchPhase==0)break;
                    Thread.Sleep(1);
                }
                string result=$"buildApplied={buildApplied} orderApplied={orderApplied} moveApplied={moveApplied} built={built} spawned={spawned} moved={moved} arrived={arrived} aircraft={aircraft} firstMotionSeconds={firstMotion:R} seconds={clock.Elapsed.TotalSeconds:R}";File.WriteAllText(Path.Combine(output,"result.txt"),result);TestContext.WriteLine(result);
                Assert.True(buildApplied&&orderApplied&&moveApplied,"Actual command receipts");Assert.True(built&&spawned,"Actual hangar production and independent takeoff");Assert.True(moved&&arrived,"Aircraft must physically move and arrive through ordinary threaded match");Assert.True(nav.HasCertifiedHost,"Ordinary threaded host remains bound");
            }
            finally
            {
                var worker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",hidden).GetValue(host);runtime.Dispose();host.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000));nav.ReleaseCommittedHostRoot();domain.GetType().GetMethod("DestroyOwnedNavigation",hidden).Invoke(domain,null);Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);
            }
        }
    }
}
