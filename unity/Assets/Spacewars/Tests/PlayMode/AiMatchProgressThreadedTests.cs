using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Tests.PlayMode
{
    public sealed class AiMatchProgressThreadedTests
    {
        [Test,Timeout(110000)] public void NullDockIncidentAttackMoveMakesPhysicalProgressAndArrives()
        {
            const int seed=-715691070;
            var map=new NullDockMap(JsonUtility.FromJson<NullDockProfileData>(Resources.Load<TextAsset>("NullDockProfile").text));
            var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            var basis=map.Configuration(p,seed,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("foundry-2",2,2,OfflineControl.Human)});
            // The recorded lobby assigned foundry-2 to dock-A. The map helper's
            // fixed default pins differ; preserve the incident's actual start.
            var starts=basis.Starts.Select(s=>new OfflineStart(s.Id,s.SiteId,s.Position,s.ExplorerAnchor,s.Heading,s.PinnedLogicalPlayer==1?2:1)).ToArray();
            var config=new OfflineMatchConfiguration(p,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,seed,basis.Roster.ToArray(),starts,basis.Sites.ToArray(),basis.Obstacles.ToArray(),new double[2,2],terrain:map.Id);
            var d=new PlayableDomain(p,2,config);CertifiedRouteWorker worker=null;
            PlayableSnapshot View()=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,seed);
            try{
                var actor=View().Entities.Single(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Explorer);
                Assert.That(actor.Position.X,Is.EqualTo(-66).Within(.001));Assert.That(actor.Position.Z,Is.EqualTo(14).Within(.001));
                int id=actor.Id;var origin=actor.Position;var target=new NavPoint(-9.58,-30);
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),seed);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                var receipt=authority.Apply(new PlayableCommand(2,1,"foundry-2",PlayableCommandKind.AttackMove,new[]{id},target));
                Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
                var clock=Stopwatch.StartNew();var anchor=origin;long lastProgress=0,maxStationary=0;bool moved=false,arrived=false;
                while(d.Tick<1800&&clock.Elapsed.TotalSeconds<90){
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}
                    Assert.IsNull(worker.Publication.Failure);actor=View().Entities.Single(e=>e.Id==id);
                    double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
                    if(Distance(anchor,actor.Position)>.01){moved=true;anchor=actor.Position;lastProgress=d.Tick;}
                    if(actor.Moving)maxStationary=Math.Max(maxStationary,d.Tick-lastProgress);
                    if(d.Tick%150==0)TestContext.WriteLine($"NULL_DOCK tick={d.Tick} position={actor.Position.X:R},{actor.Position.Z:R} outcome={actor.NavigationOutcome} stationary={d.Tick-lastProgress}");
                    arrived=actor.NavigationOutcome==NavigationOutcome.Arrived&&Distance(actor.Position,target)<=p.Navigation.ArrivalTolerance;
                    if(arrived)break;Thread.Sleep(1);
                }
                TestContext.WriteLine($"NULL_DOCK_RESULT seed={seed} tick={d.Tick} moved={moved} arrived={arrived} maxStationary={maxStationary} pumps={worker.Publication.Pumps}");
                if(!arrived){
                    var motor=d.Navigation.Crowd.Units.Single(e=>e.Id==id);
                    TestContext.WriteLine("NULL_DOCK_MOTOR "+Newtonsoft.Json.JsonConvert.SerializeObject(d.Navigation.Crowd.CaptureState().Units.Single(e=>e.Id==id)));
                    const System.Reflection.BindingFlags f=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
                    var storage=typeof(NavUnit).GetProperty("StorageOwner",f).GetValue(motor);
                    var execution=(NavigationInstalledExecutionOwner)storage.GetType().GetField("InstalledExecution",f).GetValue(storage);
                    TestContext.WriteLine("NULL_DOCK_TYPED "+Newtonsoft.Json.JsonConvert.SerializeObject(execution.Corridor.TypedRoute.Legs.Select(l=>new{l.Kind,l.From,l.To,l.PortalId})));
                }
                Assert.True(moved,"Accepted command must produce physical coordinate displacement through the real host");
                Assert.LessOrEqual(maxStationary,90,"Moving flag must not conceal prolonged stationary motion");
                Assert.True(arrived,"Original incident destination must be reached within the fixed horizon");Assert.Greater(worker.Publication.Pumps,0);
            }finally{worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);}
        }
    }
}
