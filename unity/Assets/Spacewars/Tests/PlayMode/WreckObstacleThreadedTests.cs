using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.PlayMode
{
    public sealed class WreckObstacleThreadedTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        [Test,Timeout(120000)] public void NaturalExpiryDuringActiveMovePublishesGeometryAndStillArrives()
        {
            var data=PlayableProfile.Default.CopyData();data.unitWreckLifetimeSeconds=1;data.wreckSinkSeconds=.2;
            var p=PlayableProfile.Create(data);var d=new PlayableDomain(p,81,false);CertifiedRouteWorker worker=null;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
            PlayableSnapshot View()=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            try{
                int dead=(int)Call("SpawnUnit",default(NavPoint),PlayableOwner.Player,PlayableEntityKind.Tank);
                var start=new NavPoint(-10,0);int mover=(int)Call("SpawnUnit",start,PlayableOwner.Player,PlayableEntityKind.Tank);
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                Assert.True(SpinWait.SpinUntil(()=>worker.IsWaiting||worker.Publication.Failure!=null,5000),"Threaded host startup must reach its idle admission point.");Assert.IsNull(worker.Publication.Failure);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(81,1,"player-1",PlayableCommandKind.Move,new[]{mover},new NavPoint(10,0))).Status);
                bool motion=false,created=false,expired=false,arrived=false;long expiry=-1;var clock=Stopwatch.StartNew();
                while(d.Tick<1200&&clock.Elapsed.TotalSeconds<110){
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}Assert.IsNull(worker.Publication.Failure);
                    var actor=View().Entities.Single(e=>e.Id==mover);motion|=!actor.Position.Equals(start);
                    if(motion&&!created){created=(bool)Call("TryDamageWithOwner",dead,(double)int.MaxValue,0,(PlayableOwner?)PlayableOwner.Enemy,null,default(NavPoint),null);if(created){expiry=View().Wrecks.Single(w=>w.Id==dead).ExpireTick;Assert.False(d.Geometry.IsFree(default,0));}}
                    if(created){expired=!View().Wrecks.Any(w=>w.Id==dead);if(expired){Assert.GreaterOrEqual(d.Tick,expiry);Assert.True(d.Geometry.IsFree(default,0));}else if(d.Tick<expiry)Assert.Greater(Math.Sqrt(actor.Position.X*actor.Position.X+actor.Position.Z*actor.Position.Z),p.TankCollisionRadius+View().Wrecks.Single(w=>w.Id==dead).Radius);}
                    arrived=actor.NavigationOutcome==NavigationOutcome.Arrived;if(expired&&arrived)break;Thread.Sleep(1);
                }
                TestContext.WriteLine($"EXPIRY_WRECK seed=7 tick={d.Tick} motion={motion} created={created} expiry={expiry} cleared={expired} arrived={arrived} pumps={worker.Publication.Pumps}");
                Assert.True(motion&&created&&expired&&arrived,"The same ordinary threaded command must move, survive natural wreck expiry, and arrive without reissue");Assert.Greater(worker.Publication.Pumps,0);Assert.IsNull(worker.Publication.Failure);
            }finally{worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);}
        }
        [Test,Timeout(65000)] public void OrdinaryCombatDeathPublishesWreckWhileCommandedUnitMovesAndArrives()
        {
            const int seed=7;const long generation=81;var p=PlayableProfile.Default;
            var d=new PlayableDomain(p,generation,false);CertifiedRouteWorker worker=null;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
            PlayableSnapshot View()=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,seed);
            try{
                int victim=(int)Call("SpawnUnit",default(NavPoint),PlayableOwner.Enemy,PlayableEntityKind.Tank);
                Call("Damage",victim,(int)View().Entities.Single(e=>e.Id==victim).Health-1);
                int gun=(int)Call("SpawnUnit",new NavPoint(-6,-4),PlayableOwner.Player,PlayableEntityKind.Tank);
                var start=new NavPoint(-10,3);int mover=(int)Call("SpawnUnit",start,PlayableOwner.Player,PlayableEntityKind.Tank);
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),seed);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,1,"player-1",PlayableCommandKind.Move,new[]{mover},new NavPoint(10,3))).Status);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,2,"player-1",PlayableCommandKind.Attack,new[]{gun},targetId:victim)).Status);
                bool motion=false,shot=false,dead=false,arrived=false;var clock=Stopwatch.StartNew();
                while(d.Tick<900&&clock.Elapsed.TotalSeconds<55){
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}Assert.IsNull(worker.Publication.Failure);
                    var v=View();var actor=v.Entities.Single(e=>e.Id==mover);motion|=!actor.Position.Equals(start);
                    shot|=v.Projectiles.Any(x=>x.OwnerId==gun&&x.TargetId==victim);
                    dead|=v.Wrecks.Any(x=>x.Id==victim&&x.Blocks(d.Tick));
                    arrived=actor.NavigationOutcome==NavigationOutcome.Arrived;
                    if(motion&&shot&&dead&&arrived)break;Thread.Sleep(1);
                }
                TestContext.WriteLine($"COMBAT_WRECK seed={seed} tick={d.Tick} motion={motion} shot={shot} death={dead} arrived={arrived} pumps={worker.Publication.Pumps}");
                Assert.True(motion&&shot&&dead&&arrived,"Real command, threaded route service, projectile death and physical arrival must all occur");
                Assert.Greater(worker.Publication.Pumps,0);Assert.IsNull(worker.Publication.Failure);
            }finally{worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);}
        }
        [Test,Timeout(120000)] public void ActiveMoveDetoursAroundNewWreckAndArrivesThroughOrdinaryHost()=>RunWreckMove(false);
        [Test,Timeout(120000)] public void DestroyingWreckDuringActiveMovePublishesGeometryAndStillArrives()=>RunWreckMove(true);
        private void RunWreckMove(bool destroyWreck)
        {
            var p=PlayableProfile.Default;var d=new PlayableDomain(p,81,false);CertifiedRouteWorker worker=null;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
            PlayableSnapshot View()=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            try{
                int dead=(int)Call("SpawnUnit",new NavPoint(0,0),PlayableOwner.Player,PlayableEntityKind.Tank);
                var start=new NavPoint(-10,0);int mover=(int)Call("SpawnUnit",start,PlayableOwner.Player,PlayableEntityKind.Tank);
                int gun=destroyWreck?(int)Call("SpawnUnit",new NavPoint(-6,0),PlayableOwner.Player,PlayableEntityKind.Tank):0;
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
                worker=new CertifiedRouteWorker(new PlayableRouteBinding(d.Geometry,p,d.Navigation.Admission,d.Navigation.RoutePort),p.NavigationRequestsPerFrame);
                Assert.True(SpinWait.SpinUntil(()=>worker.IsWaiting||worker.Publication.Failure!=null,5000),"Threaded host startup must reach its idle admission point.");Assert.IsNull(worker.Publication.Failure);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(81,1,"player-1",PlayableCommandKind.Move,new[]{mover},new NavPoint(10,0))).Status);
                var clock=Stopwatch.StartNew();bool motion=false,created=false,arrived=false,detoured=false,destroyed=false,attack=false;
                while(d.Tick<1200&&clock.Elapsed.TotalSeconds<110){
                    authority.TryAdvance();Assert.IsNull(worker.Publication.Failure);
                    var view=View();var actor=view.Entities.Single(e=>e.Id==mover);motion|=!actor.Position.Equals(start);
                    if(motion&&!created){Call("Damage",dead,int.MaxValue);created=true;var wreck=View().Wrecks.Single(w=>w.Id==dead);Assert.True(wreck.Blocks(d.Tick));Assert.False(d.Geometry.IsFree(wreck.Position,0));}
                    if(created){var wreck=View().Wrecks.FirstOrDefault(w=>w.Id==dead);if(wreck!=null&&wreck.Blocks(d.Tick))Assert.Greater(Math.Sqrt(actor.Position.X*actor.Position.X+actor.Position.Z*actor.Position.Z),p.TankCollisionRadius+wreck.Radius);detoured|=Math.Abs(actor.Position.Z)>p.TankCollisionRadius;}
                    if(destroyWreck&&detoured&&!attack){Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(81,2,"player-1",PlayableCommandKind.Attack,new[]{gun},targetId:dead)).Status);attack=true;}
                    if(attack&&!destroyed){var wreck=View().Wrecks.FirstOrDefault(w=>w.Id==dead);destroyed=wreck==null||wreck.DestroyedTick>=0;if(destroyed)Assert.True(d.Geometry.IsFree(default,0));}
                    arrived=actor.NavigationOutcome==NavigationOutcome.Arrived;if(arrived&&(!destroyWreck||destroyed))break;Thread.Sleep(1);
                }
                TestContext.WriteLine($"tick={d.Tick} seconds={clock.Elapsed.TotalSeconds:R} firstMotion={motion} wreck={created} detour={detoured} arrived={arrived}");
                Assert.True(motion&&created&&detoured&&arrived,"The ordinary threaded command must detour around a death obstacle and arrive.");
                Assert.AreEqual(destroyWreck,destroyed);
                Assert.True(d.Navigation.HasCertifiedHost);Assert.Greater(worker.Publication.Pumps,0);
            }finally{worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);}
        }
    }
}
