using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
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
        [TestCase(false)] [TestCase(true)] [Timeout(60000)]
        public void AttackMoveKillsFirstTargetAndContinuesToOriginalDestination(bool recast)
        {
            var data=PlayableProfile.Default.CopyData();data.groundRouteBackend=recast?1:0;var map=new AiTestMap();
            var p=PlayableProfile.Create(data,map);var basis=map.Configuration(p,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            var scenario=new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-38,-38)),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-30,-35))};
            var config=new OfflineMatchConfiguration(p,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,7,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,scenario:scenario,terrain:map.Id);
            var runtime=new PlayableRuntime(config,819,startPaused:true);var host=new Spacewars.Presentation.UnityHostRouteService();
            var d=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",F).GetValue(runtime);var clock=Stopwatch.StartNew();bool hold=false,ordered=false;
            void Service(){host.Service(runtime,p.NavigationRequestsPerFrame);foreach(var receipt in runtime.DrainReceipts()){Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status);if(receipt.OwnerId=="player-2")hold=true;else ordered=true;}Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(host.CertifiedPublication?.Failure);}
            try{
                while(!(runtime.IsPrepared&&runtime.Latest.Paused)&&clock.Elapsed.TotalSeconds<45){Service();Thread.Sleep(1);}Assert.True(runtime.IsPrepared&&runtime.Latest.Paused,"current native startup must be prepared before commands");
                var view=runtime.Latest;int shooter=view.Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank).Id;
                int enemy=view.Entities.Single(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Tank).Id;
                var units=(IDictionary)typeof(PlayableDomain).GetField("units",F).GetValue(d);units[enemy].GetType().GetField("Health",F).SetValue(units[enemy],1d);units[enemy].GetType().GetField("Reload",F).SetValue(units[enemy],1000d);
                var goal=new NavPoint(-20,-38);runtime.RequestPause(false);Assert.True(runtime.TrySubmit(new PlayableCommand(819,1,"player-2",PlayableCommandKind.Hold,new[]{enemy})).Accepted);
                Assert.True(runtime.TrySubmit(new PlayableCommand(819,1,"player-1",PlayableCommandKind.AttackMove,new[]{shooter},goal)).Accepted);runtime.RequestPause(false);
                bool fired=false,killed=false,resumed=false,arrived=false;NavPoint deathPosition=default;
                while(runtime.Latest.Tick<900&&clock.Elapsed.TotalSeconds<45){
                    Service();view=runtime.Latest;var actor=view.Entities.Single(e=>e.Id==shooter);fired|=view.Projectiles.Any(x=>x.OwnerId==shooter);
                    if(!killed&&!view.Entities.Any(e=>e.Id==enemy)){killed=true;deathPosition=actor.Position;}
                    if(killed){resumed|=Math.Pow(actor.Position.X-deathPosition.X,2)+Math.Pow(actor.Position.Z-deathPosition.Z,2)>1;arrived=Math.Pow(actor.Position.X-goal.X,2)+Math.Pow(actor.Position.Z-goal.Z,2)<1;if(arrived)break;}Thread.Sleep(1);
                }
                TestContext.WriteLine($"ATTACK_MOVE_RESUME recast={recast} seed=7 tick={runtime.Latest.Tick} fired={fired} killed={killed} resumed={resumed} arrived={arrived} pumps={host.CertifiedPublication.Pumps}");
                Assert.True(hold&&ordered,"Both commands must apply through the ordinary command queue");Assert.True(fired&&killed,"Attack-move must engage and kill the first live target through actual projectiles");
                Assert.True(resumed,"After target death, the original attack-move must resume without a new command");Assert.True(arrived,"Original destination must still be reached");Assert.Greater(host.CertifiedPublication.Pumps,0);
            }finally{var worker=(CertifiedRouteWorker)typeof(Spacewars.Presentation.UnityHostRouteService).GetField("worker",F).GetValue(host);runtime.RequestStop();host.Dispose();runtime.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),10000));d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);}
        }
        [Test,Timeout(60000)]
        public void NearbyHitMakesFreeTankFightAndReturnThroughTheOrdinaryHost()
        {
            var profile=PlayableProfile.Default;
            var domain=new PlayableDomain(profile,821,false);
            CertifiedRouteWorker worker=null;
            var spawn=typeof(PlayableDomain).GetMethod("SpawnUnit",F);
            int Spawn(NavPoint point,PlayableOwner owner)=>(int)spawn.Invoke(domain,new object[]{point,owner,PlayableEntityKind.Tank});
            try
            {
                var anchor=new NavPoint(-22,26);
                int helper=Spawn(anchor,PlayableOwner.Player);
                int victim=Spawn(new NavPoint(-15,24),PlayableOwner.Player);
                int attacker=Spawn(new NavPoint(-10,26),PlayableOwner.Enemy);
                var units=(IDictionary)typeof(PlayableDomain).GetField("units",F).GetValue(domain);
                void Set(int id,string field,double value)=>units[id].GetType().GetField(field,F).SetValue(units[id],value);
                Set(victim,"Reload",1000);
                Set(attacker,"Health",profile.TankWeaponDamage);
                var authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
                var hold=authority.Apply(new PlayableCommand(821,1,"player-1",PlayableCommandKind.Hold,new[]{victim}));
                Assert.AreEqual(PlayableCommandStatus.Applied,hold.Status,hold.Message);
                var attack=authority.Apply(new PlayableCommand(821,1,"enemy-1",PlayableCommandKind.Attack,new[]{attacker},targetId:victim));
                Assert.AreEqual(PlayableCommandStatus.Applied,attack.Status,attack.Message);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,profile.NavigationRequestsPerFrame);
                bool hit=false,advanced=false,fired=false,returned=false;
                var clock=Stopwatch.StartNew();
                while(domain.Tick<900&&clock.Elapsed.TotalSeconds<45)
                {
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}
                    Assert.IsNull(worker.Publication.Failure);
                    var view=authority.Latest;
                    var unit=view.Entities.Single(e=>e.Id==helper);
                    var protectedUnit=view.Entities.Single(e=>e.Id==victim);
                    hit|=protectedUnit.Health<profile.TankHealth;
                    double displacement=Math.Sqrt(Math.Pow(unit.Position.X-anchor.X,2)+Math.Pow(unit.Position.Z-anchor.Z,2));
                    Assert.That(displacement,Is.LessThanOrEqualTo(profile.CombatResponsePursuitRadius));
                    Assert.IsNull(unit.CurrentOrder,"The response must not create an explicit order.");
                    if(hit)advanced|=displacement>0.5;
                    else Assert.AreEqual(anchor,unit.Position,"A visible target alone must not start the approach.");
                    fired|=view.Projectiles.Any(projectile=>projectile.OwnerId==helper);
                    if(advanced&&fired&&!view.Entities.Any(e=>e.Id==attacker)&&displacement<0.3){returned=true;break;}
                }
                TestContext.WriteLine($"HIT_RESPONSE tick={domain.Tick} hit={hit} moved={advanced} fired={fired} returned={returned}");
                Assert.True(hit&&advanced&&fired&&returned);
                Assert.Greater(worker.Publication.Pumps,0);
            }
            finally
            {
                worker?.Dispose();
                if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));
                domain.Navigation.ReleaseCommittedHostRoot();
                domain.DestroyOwnedNavigation();
                Assert.Zero(domain.Navigation.PhysicalHostBudget.PhysicalBytes);
                Assert.Zero(domain.Navigation.PhysicalHostBudget.HeldBytes);
            }
        }
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
        [TestCase(OfflineControl.Human)] [TestCase(OfflineControl.Ai)] [Timeout(60000)]
        public void MovingExplorerFiresAndKeepsItsRouteWithTheSameOrder(OfflineControl control)
        {
            var path=Path.Combine(Path.GetTempPath(),"spacewars-combat-"+Guid.NewGuid()+".json");
            var store=new Spacewars.Presentation.NativeBalanceStore(PlayableProfile.Default,path,authoring:false);
            store.Select(Spacewars.Presentation.NativeBalanceStore.CorridorExperimentId,Spacewars.Presentation.NativeBalanceStore.CorridorExperimentVersion);
            var profile=store.Active;
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-21,0),new NavPoint(-13,-5),pin:1),new OfflineStart("east",2,new NavPoint(21,0),new NavPoint(13,5),pin:2)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,Array.Empty<TerritorySlot>()),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())};
            var roster=new[]{new OfflineParticipant("west-owner",1,1,control),new OfflineParticipant("east-owner",2,2,OfflineControl.Human)};
            var start=new NavPoint(-22,26);
            var goal=new NavPoint(-7,26);
            var scenario=new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,start),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-10,30))};
            var config=new OfflineMatchConfiguration(profile,"moving-scout","local-default","certified-threaded-host",7,roster,starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:scenario);
            var domain=new PlayableDomain(profile,822,config);
            var authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7,config);
            CertifiedRouteWorker worker=null;
            PlayableAiOwnerLoop scoutOwner=null;
            try
            {
                var west=authority.ParticipantView("west-owner");
                int scout=west.Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer&&e.Position.Equals(start)).Id;
                var move=new PlayableCommand(822,1,"west-owner",PlayableCommandKind.Move,new[]{scout},goal,origin:PlayableOrderOrigin.Human);
                if(control==OfflineControl.Ai)
                {
                    scoutOwner=new PlayableAiOwnerLoop(profile,PlayableAiOpeningComposition.Initialize(7,"west-owner"),822);
                    var armies=(AiArmyRegistry)typeof(PlayableAiOwnerLoop).GetField("armies",F).GetValue(scoutOwner);
                    Assert.True(armies.TryCreate(PlayableAiObservation.From(west),AiArmyRole.Scout,AiScoutPlanner.Policy,new[]{scout},out _));
                    domain.ConfigureCombatExecutionOwners(()=>new[]{scoutOwner});
                    move=new PlayableCommand(822,1,"west-owner",PlayableCommandKind.Move,new[]{scout},goal,
                        origin:PlayableOrderOrigin.Ai,source:AiScoutPlanner.Policy,jobId:1,actionId:1);
                }
                var receipt=authority.Apply(move);
                Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,profile.NavigationRequestsPerFrame);
                bool advanced=false,fired=false,arrived=false;
                var clock=Stopwatch.StartNew();
                while(domain.Tick<300&&clock.Elapsed.TotalSeconds<45)
                {
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}
                    Assert.IsNull(worker.Publication.Failure);
                    var view=authority.ParticipantView("west-owner");
                    var actor=view.Entities.Single(e=>e.Id==scout);
                    advanced|=actor.Position.X>start.X+.5;
                    if(actor.Moving&&view.Projectiles.Any(p=>p.OwnerId==scout&&p.DirectLaunchTick==domain.Tick))
                    {
                        Assert.AreEqual(PlayableTacticalOrderKind.Move,actor.CurrentOrder?.Kind);
                        Assert.AreEqual(move.Origin,actor.OrderStamp?.Origin);
                        fired=true;
                    }
                    if(actor.Position.X>goal.X-.5&&!actor.Moving){arrived=true;break;}
                }
                TestContext.WriteLine($"SCOUT_MOVING_FIRE control={control} profile={profile.ProfileId}@{profile.Revision} range={profile.ExplorerRange} speed={profile.ExplorerSpeed} tick={domain.Tick} moved={advanced} fired={fired} arrived={arrived}");
                Assert.True(advanced&&fired&&arrived,"The Explorer must move, fire during its Move, and reach the original destination.");
                Assert.Greater(worker.Publication.Pumps,0);
            }
            finally
            {
                worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));
                scoutOwner?.Stop();
                domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();
                Assert.Zero(domain.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(domain.Navigation.PhysicalHostBudget.HeldBytes);
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
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
                // Direct test ingress has no runtime waiter for host startup admission.
                var firstMove=authority.Apply(new PlayableCommand(816,1,"player-1",PlayableCommandKind.Move,new[]{shooter},new NavPoint(-24,26)));
                Assert.AreEqual(PlayableCommandStatus.Applied,firstMove.Status,firstMove.Message);
                var nextMove=authority.Apply(new PlayableCommand(816,2,"player-1",PlayableCommandKind.Move,new[]{shooter},new NavPoint(-20,26),mode:PlayableOrderMode.Append));
                Assert.AreEqual(PlayableCommandStatus.Applied,nextMove.Status,nextMove.Message);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
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
