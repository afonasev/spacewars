using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed class AiRelayOpeningThreadedTests
    {
        // Current ordinary opening, original first unit, real threaded route host.
        // No prepared economy, forced milestones or policy-injected commands.
        [Test,Timeout(65000)] public void FirstUnitLeavesHomeAndCompletesRelayCapture()
        {
            var p=PlayableProfile.Default;
            var starts=new[]{new OfflineStart("home",1,new NavPoint(-24,-12),new NavPoint(-19,-12),pin:1),new OfflineStart("enemy",2,new NavPoint(24,18),new NavPoint(19,18),pin:2)};
            var sites=starts.Select((s,i)=>new TerritorySite(i+1,PlayableBuildingKind.Headquarters,s.Position,Array.Empty<TerritorySlot>()))
                .Concat(new[]{new TerritorySite(3,PlayableBuildingKind.RelayTower,new NavPoint(0,-8),Array.Empty<TerritorySlot>())}).ToArray();
            var config=new OfflineMatchConfiguration(p,"ai-relay-opening-v1","relay-flat","ordinary-threaded",7,
                new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,55},{55,0}});
            var a=new PlayableAuthorityTick(config,81);CertifiedRouteWorker worker=null;
            try
            {
                var first=a.ParticipantView("player-1").Entities.Single(u=>u.Owner==PlayableOwner.Player);
                worker=new CertifiedRouteWorker(a.NavigationBinding,p.NavigationRequestsPerFrame);
                bool moved=false,applied=false,captured=false;long departure=-1;var clock=Stopwatch.StartNew();
                while(a.Tick<1800&&clock.Elapsed.TotalSeconds<55)
                {
                    if(!a.TryAdvance()){Thread.Sleep(1);continue;}
                    Assert.IsNull(worker.Publication.Failure);var v=a.ParticipantView("player-1");var unit=v.Entities.FirstOrDefault(u=>u.Id==first.Id);
                    if(unit!=null&&AiOpponentSelector.Distance(unit.Position,first.Position)>PlayableUnitRules.Radius(p,first.Kind)){moved=true;if(departure<0)departure=a.Tick;}
                    var cp=a.CaptureDiagnosticCheckpoints().Single();
                    applied|=cp.Records.Any(r=>r.Policy=="scout"&&r.Kind==PlayableCommandKind.AttackMove&&r.RuntimeStatus==PlayableCommandStatus.Applied);
                    captured|=v.Sites.Any(s=>s.Site.Id==3&&s.Owner==PlayableOwner.Player);
                    if(moved&&applied&&captured)break;
                    Thread.Sleep(1);
                }
                var end=a.ParticipantView("player-1");var checkpoint=a.CaptureDiagnosticCheckpoints().Single();
                TestContext.WriteLine("RELAY_OPENING tick="+a.Tick+" departure="+departure+" applied="+applied+" captured="+captured+" pumps="+worker.Publication.Pumps+" records="+string.Join(";",checkpoint.Records.Select(r=>r.Policy+":"+r.Kind+":"+r.RuntimeStatus+":"+r.Message)));
                Assert.True(moved&&applied&&captured,"First unit must leave and finish actual relay capture without economic readiness");
                Assert.Less(departure,300,"No passive opening wait");Assert.Greater(worker.Publication.Pumps,0);
                Assert.False(end.Buildings.Any(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory),"Fixture has no prepared factory");
                worker.Dispose();Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));worker=null;
                var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,config);
                var hot=new CertifiedRouteKernel();var cold=new CertifiedRouteKernel();
                void Advance(PlayableAuthorityTick authority,CertifiedRouteKernel kernel)
                {for(int pump=0;pump<4096;pump++){if(authority.TryAdvance())return;kernel.Pump(authority.NavigationBinding,4);}Assert.Fail("Current fixed quantum must complete");}
                try
                {
                    CollectionAssert.AreEqual(bytes,restored.CaptureBytes(),"Current relay mission exact roundtrip");
                    for(int i=0;i<30;i++){Advance(a,hot);Advance(restored,cold);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes(),"Exact relay mission cold continuation");}
                }
                finally{hot.Clear();cold.Clear();restored.Stop();}
            }
            finally{worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));a.Stop();}
        }
    }
}
