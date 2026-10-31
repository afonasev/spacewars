using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed class DefaultCombatThreadedTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        [Test,Timeout(60000)]
        public void IdleFireRetreatPursuitAndReturnUseTheOrdinaryThreadedHost()
        {
            var p=PlayableProfile.Default;var d=new PlayableDomain(p,814,false);CertifiedRouteWorker worker=null;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
            var units=(IDictionary)typeof(PlayableDomain).GetField("units",F).GetValue(d);
            object State(int id)=>units[id].GetType().GetField("DefaultCombat").GetValue(units[id]);
            string Phase(int id){var s=State(id);return s==null?"None":s.GetType().GetField("Phase",F).GetValue(s).ToString();}
            PlayableSnapshot View()=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            try
            {
                int shooter=(int)Call("SpawnUnit",new NavPoint(-20,15),PlayableOwner.Player,PlayableEntityKind.Tank);
                int enemy=(int)Call("SpawnUnit",new NavPoint(-12,15),PlayableOwner.Enemy,PlayableEntityKind.Tank);
                Call("SpawnUnit",new NavPoint(-8,23),PlayableOwner.Player,PlayableEntityKind.Explorer);
                var start=View().Entities.Single(e=>e.Id==shooter).Position;
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(814,1,"enemy-1",PlayableCommandKind.Hold,new[]{enemy})).Status);
                bool fired=false,ordered=false,pursued=false,moved=false,returned=false;long begin=-1,end=-1;
                var clock=Stopwatch.StartNew();
                while(d.Tick<900&&clock.Elapsed.TotalSeconds<45)
                {
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}
                    Assert.IsNull(worker.Publication.Failure);var view=View();var actor=view.Entities.Single(e=>e.Id==shooter);
                    fired|=view.Projectiles.Any(x=>x.OwnerId==shooter);
                    if(fired&&!ordered)
                    {
                        Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(814,2,"enemy-1",PlayableCommandKind.Move,new[]{enemy},new NavPoint(-12,29))).Status);ordered=true;
                    }
                    string phase=Phase(shooter);
                    if(phase=="Pursuit"){pursued=true;if(begin<0)begin=d.Tick;moved|=Math.Abs(actor.Position.X-start.X)>.1;}
                    if(pursued&&phase=="Return"&&end<0)end=d.Tick;
                    if(pursued&&end>=0&&phase=="None"&&Math.Abs(actor.Position.X-start.X)<.5&&Math.Abs(actor.Position.Z-start.Z)<.5){returned=true;break;}
                    Thread.Sleep(1);
                }
                TestContext.WriteLine($"DEFAULT_COMBAT seed=7 tick={d.Tick} fire={fired} pursue={begin} end={end} moved={moved} returned={returned} pumps={worker.Publication.Pumps}");
                Assert.True(fired,"Idle tank must fire without Attack");Assert.True(pursued&&moved,"Retreat must cause physical pursuit");
                Assert.That(end-begin,Is.InRange(1L,60L),"Pursuit is bounded by two simulation seconds");Assert.True(returned,"Unit physically returns to its pursuit anchor");
                Assert.True(d.Navigation.HasCertifiedHost);Assert.Greater(worker.Publication.Pumps,0);
            }
            finally
            {
                worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));
                d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();
                Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);
            }
        }
        [TestCase(OfflineControl.Human)] [TestCase(OfflineControl.Ai)] [Timeout(60000)]
        public void AutoFireBelongsToTheUnitWithoutAnAiTacticalLoop(OfflineControl control)
        {
            var p=PlayableProfile.Default;
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-21,0),new NavPoint(-13,-5),pin:1),new OfflineStart("east",2,new NavPoint(21,0),new NavPoint(13,5),pin:2)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,Array.Empty<TerritorySlot>()),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())};
            var roster=new[]{new OfflineParticipant("west-owner",1,1,control),new OfflineParticipant("east-owner",2,2,OfflineControl.Human)};
            var scenario=new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-20,26)),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-12,26))};
            var config=new OfflineMatchConfiguration(p,"default-combat","local-default","certified-threaded-host",7,roster,starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:scenario);
            var d=new PlayableDomain(p,815,config);CertifiedRouteWorker worker=null;
            try
            {
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7,config);
                int id=authority.ParticipantView("west-owner").Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank).Id;
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                // An idle host correctly sleeps without route work. Give the other owner's scout a real route;
                // the observed shooter still receives no command and has no tactical AI loop.
                int scout=authority.ParticipantView("east-owner").Entities.Single(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Explorer).Id;
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(815,1,"east-owner",PlayableCommandKind.Move,new[]{scout},new NavPoint(13,8))).Status);
                // Tick quanta are granted by the ordinary authority barrier, not merely by route admission.
                // Warmup remains within the original 180-tick observation horizon.
                Assert.True(SpinWait.SpinUntil(()=>{if(d.Tick<180)authority.TryAdvance();return worker.Publication.Pumps>0;},5000),"Threaded host warmup precedes the idle-fire observation");bool fired=false;var clock=Stopwatch.StartNew();
                while(d.Tick<180&&clock.Elapsed.TotalSeconds<45)
                {
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}
                    Assert.IsNull(worker.Publication.Failure);var view=authority.ParticipantView("west-owner");
                    Assert.IsNull(view.Entities.Single(e=>e.Id==id).CurrentOrder);Assert.IsNull(view.Entities.Single(e=>e.Id==id).OrderStamp);
                    fired=view.Projectiles.Any(x=>x.OwnerId==id);if(fired)break;Thread.Sleep(1);
                }
                Assert.True(fired,"Unit auto-fire must not depend on an AI controller issuing Attack");Assert.Greater(worker.Publication.Pumps,0);
                TestContext.WriteLine($"DEFAULT_OWNER control={control} seed=7 tick={d.Tick} fired={fired}");
            }
            finally
            {
                worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));
                d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);
            }
        }
        [Test,Timeout(60000)]
        public void CompletedExplicitQueueResumesDefaultFireThroughTheOrdinaryHost()
        {
            var p=PlayableProfile.Default;var d=new PlayableDomain(p,816,false);CertifiedRouteWorker worker=null;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
            try
            {
                int shooter=(int)Call("SpawnUnit",new NavPoint(-20,26),PlayableOwner.Player,PlayableEntityKind.Tank);
                int enemy=(int)Call("SpawnUnit",new NavPoint(-12,26),PlayableOwner.Enemy,PlayableEntityKind.Tank);
                var units=(IDictionary)typeof(PlayableDomain).GetField("units",F).GetValue(d);units[enemy].GetType().GetField("Reload").SetValue(units[enemy],1000d);
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(816,1,"player-1",PlayableCommandKind.Move,new[]{shooter},new NavPoint(-24,26))).Status);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(816,2,"player-1",PlayableCommandKind.Move,new[]{shooter},new NavPoint(-20,26),mode:PlayableOrderMode.Append)).Status);
                bool first=false,second=false,fired=false;var clock=Stopwatch.StartNew();
                while(d.Tick<900&&clock.Elapsed.TotalSeconds<45)
                {
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}
                    Assert.IsNull(worker.Publication.Failure);var view=authority.Latest;var u=view.Entities.Single(e=>e.Id==shooter);
                    first|=u.CurrentOrder?.CommandSequence==1;second|=u.CurrentOrder?.CommandSequence==2;
                    if(second&&u.CurrentOrder==null&&Math.Abs(u.Position.X+20)<.5&&view.Projectiles.Any(x=>x.OwnerId==shooter)){fired=true;break;}
                    Thread.Sleep(1);
                }
                Assert.True(first&&second,"Both explicit queue entries activate");Assert.True(fired,"Default auto-fire resumes after the explicit queue completes");Assert.Greater(worker.Publication.Pumps,0);
                TestContext.WriteLine($"DEFAULT_QUEUE seed=7 tick={d.Tick} first={first} second={second} fired={fired}");
            }
            finally
            {
                worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);
            }
        }
    }
}
