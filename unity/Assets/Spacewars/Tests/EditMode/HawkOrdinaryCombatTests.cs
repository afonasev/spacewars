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
    public sealed class HawkOrdinaryCombatTests
    {
        // Two ordinary human seats; no bot policy, spawned fixtures or live hidden targets.
        [Test,Timeout(180000)] public void ProducedAircraftMeetAndFightThroughOrdinaryThreadedHost()
        {
            const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            string output=Environment.GetEnvironmentVariable("SPACEWARS_HAWK_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            var p=PlayableProfile.ThreeCrossingsDefault;var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=1024700465};setup.InitializeParticipants();setup.Participants[1].Human=true;setup.Participants[1].DeviceId=1;setup.Participants[1].Name="Игрок 2";
            var config=NativeLobbyMatch.Create(p,setup);var runtime=new PlayableRuntime(config,82,startPaused:true);var host=new UnityHostRouteService();
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",hidden).GetValue(runtime);var nav=domain.Navigation;
            using var log=new StreamWriter(Path.Combine(output,"combat.csv")){AutoFlush=true};log.WriteLine("seconds,tick,owner,stage,unit,x,z,height,hp,target,projectiles");
            try
            {
                var start=Stopwatch.StartNew();void Service(){host.Service(runtime,p.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(host.CertifiedPublication?.Failure);}
                while(!(runtime.IsPrepared&&runtime.Latest.Paused)&&start.Elapsed.TotalSeconds<60){Service();Thread.Sleep(1);}Assert.True(runtime.IsPrepared&&runtime.Latest.Paused,"Ordinary startup");
                string[] owners=config.Roster.Select(x=>x.Id).ToArray();var starts=owners.Select(o=>runtime.ParticipantView(o).Buildings.Single(b=>b.Owner==runtime.ParticipantView(o).Owner&&b.Kind==PlayableBuildingKind.Headquarters)).ToArray();
                var middle=new NavPoint((starts[0].Position.X+starts[1].Position.X)/2,(starts[0].Position.Z+starts[1].Position.Z)/2);
                long[] sequence={1,1};bool[] ordered={false,false},issued={false,false},motion={false,false};int[] aircraft={0,0};NavPoint[] birth=new NavPoint[2];int applied=0;bool shot=false,damaged=false,dead=false;long lastTick=-1;double firstMotion=-1;
                runtime.RequestPause(false);var clock=Stopwatch.StartNew();
                for(int i=0;i<2;i++){var view=runtime.ParticipantView(owners[i]);int slot=view.Sites.Single(s=>s.Site.Id==starts[i].SiteId).Site.Slots[0].Id;Assert.True(runtime.TrySubmit(new PlayableCommand(82,1,owners[i],PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:starts[i].SiteId,slotId:slot,parentId:starts[i].Id,buildingKind:PlayableBuildingKind.Hangar)).Accepted);}
                while(clock.Elapsed.TotalSeconds<90)
                {
                    Service();foreach(var receipt in runtime.DrainReceipts()){Assert.That(receipt.Status,Is.EqualTo(PlayableCommandStatus.Applied).Or.EqualTo(PlayableCommandStatus.Accepted),receipt.Message);if(receipt.Status==PlayableCommandStatus.Applied)applied++;}
                    long tick=runtime.Latest.Tick;
                    for(int i=0;i<2;i++)
                    {
                        var view=runtime.ParticipantView(owners[i]);var hangar=view.Buildings.FirstOrDefault(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Hangar);
                        if(!ordered[i]&&hangar?.Phase==ConstructionPhase.Ready){ordered[i]=true;Assert.True(runtime.TrySubmit(new PlayableCommand(82,++sequence[i],owners[i],PlayableCommandKind.QueueHawk,new[]{hangar.Id})).Accepted);}
                        var hawk=view.Entities.FirstOrDefault(u=>u.Owner==view.Owner&&u.Kind==PlayableEntityKind.Hawk);
                        if(hawk!=null&&!issued[i]){issued[i]=true;aircraft[i]=hawk.Id;birth[i]=hawk.Position;var goal=new NavPoint(middle.X+(i==0?-3.5:3.5),middle.Z);Assert.True(runtime.TrySubmit(new PlayableCommand(82,++sequence[i],owners[i],PlayableCommandKind.AttackMove,new[]{hawk.Id},goal)).Accepted);}
                        if(hawk!=null){if(!hawk.Position.Equals(birth[i])){motion[i]=true;if(firstMotion<0)firstMotion=clock.Elapsed.TotalSeconds;}damaged|=hawk.Health<p.HawkHealth;}
                        shot|=view.Projectiles.Any(x=>x.Kind==PlayableEntityKind.Hawk)||view.Sounds.Any(x=>x.Kind==PlayableSoundKind.Shot&&x.Weapon==PlayableEntityKind.Hawk);
                        dead|=view.Sounds.Any(x=>x.Kind==PlayableSoundKind.Destroyed&&x.Weapon==PlayableEntityKind.Hawk&&x.Altitude>0);
                        if(tick!=lastTick)log.WriteLine($"{clock.Elapsed.TotalSeconds:R},{tick},{i},{(issued[i]?2:ordered[i]?1:0)},{hawk?.Id??0},{hawk?.Position.X??0:R},{hawk?.Position.Z??0:R},{hawk?.Altitude??0:R},{hawk?.Health??0},{hawk?.TargetId??0},{view.Projectiles.Count}");
                    }
                    lastTick=tick;if(dead)break;Thread.Sleep(1);
                }
                string result=$"applied={applied} produced={issued.All(x=>x)} motion={motion.All(x=>x)} shot={shot} damaged={damaged} airborneDeath={dead} firstMotionSeconds={firstMotion:R} seconds={clock.Elapsed.TotalSeconds:R}";File.WriteAllText(Path.Combine(output,"result.txt"),result);TestContext.WriteLine(result);
                Assert.AreEqual(6,applied,"Build, production and attack-move receipts from both seats");Assert.True(issued.All(x=>x)&&motion.All(x=>x),"Actual production, independent liftoff and first movement");Assert.True(shot&&damaged&&dead,"Physical rockets must damage and kill an airborne peer in the ordinary match");Assert.True(nav.HasCertifiedHost);
            }
            finally
            {
                var worker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",hidden).GetValue(host);runtime.Dispose();host.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000));nav.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);
            }
        }
    }
}
