using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableHoldStopTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private object domain;private PlayableProfile profile;private long sequence;
        private object Call(string name,params object[] args)=>Domain.GetMethod(name,Flags).Invoke(domain,args);
        private NavigationSession Nav=>(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(domain);
        private PlayableSnapshot View()=>(PlayableSnapshot)Call("Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026);
        [SetUp] public void Setup(){profile=PlayableProfile.Default;domain=Activator.CreateInstance(Domain,Flags,null,new object[]{profile,1L,false},null);sequence=0;}
        private int Spawn(NavPoint p,PlayableOwner owner,PlayableEntityKind kind)=>(int)Call("SpawnUnit",p,owner,kind);
        private PlayableCommandStatus Send(PlayableCommandKind kind,int id,NavPoint target=default(NavPoint),int targetId=0)=>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,"player-1",kind,new[]{id},target,targetId:targetId),null);
        private void Seconds(double seconds){for(int i=0;i<seconds*30;i++)Call("Step",1d/30d);}
        private NavigationRequest Request(int id){NavigationRequest r;while(Nav.Requests.TryDequeue(out r))if(r.Entity==id)return r;Assert.Fail("Missing request");return null;}
        [Test] public void OrdinaryOwnerAiCannotReplaceHumanHoldButHumanStopCan()
        {
            using(var runtime=new PlayableRuntime(profile,91,19092026))
            {
                var initial=runtime.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer);
                Assert.True(runtime.TrySubmit(new PlayableCommand(91,1,"player-1",PlayableCommandKind.Hold,new[]{initial.Id})).Accepted);
                var end=DateTime.UtcNow.AddSeconds(20);
                while(DateTime.UtcNow<end&&!runtime.AiCheckpoint.Records.Any(r=>r.Message=="Human/unknown HOLD has priority."))Thread.Sleep(20);
                Assert.True(runtime.AiCheckpoint.Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Rejected&&r.Message=="Human/unknown HOLD has priority."),"Ordinary owner policy must attempt delivery against the retained human HOLD.");
                var held=runtime.Latest.Entities.Single(e=>e.Id==initial.Id);Assert.True(held.Held);Assert.False(held.Moving);Assert.AreEqual(initial.Position,held.Position);
                Assert.True(runtime.TrySubmit(new PlayableCommand(91,2,"player-1",PlayableCommandKind.Stop,new[]{initial.Id})).Accepted);
                end=DateTime.UtcNow.AddSeconds(2);while(DateTime.UtcNow<end&&runtime.Latest.Entities.Single(e=>e.Id==initial.Id).Held)Thread.Sleep(20);
                Assert.False(runtime.Latest.Entities.Single(e=>e.Id==initial.Id).Held);
            }
        }
        [TestCase(PlayableEntityKind.Tank)] [TestCase(PlayableEntityKind.Explorer)] [TestCase(PlayableEntityKind.Shkval)]
        public void HoldClearsOldPendingAndStopReturnsFree(PlayableEntityKind kind)
        {
            var start=new NavPoint(-12,-12);int id=Spawn(start,PlayableOwner.Player,kind);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.Move,id,new NavPoint(-10,-12)));var old=Request(id);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.Hold,id));
            Nav.Answers.TryEnqueue(new NavigationAnswer(old,new[]{old.Goal}));Seconds(1);
            Assert.True(Nav.Crowd.TryGet(id,out var unit));Assert.True(unit.Held);Assert.False(unit.Moving);Assert.AreEqual(start,unit.Position);Assert.AreEqual(0,Nav.PendingCount);Assert.Null(View().Entities.Single(e=>e.Id==id).CurrentOrder);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.Stop,id));Assert.False(unit.Held);Assert.False(unit.Moving);
        }
        [Test] public void AsyncReplacementOnlyReleasesHoldOnValidRouteAndIllegalAttackKeepsIt()
        {
            int id=Spawn(new NavPoint(-12,-12),PlayableOwner.Player,PlayableEntityKind.Tank);Send(PlayableCommandKind.Hold,id);
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.Attack,id,targetId:99999));Assert.True(Nav.Crowd.Units.Single(u=>u.Id==id).Held);
            Send(PlayableCommandKind.Move,id,new NavPoint(-10,-12));var request=Request(id);Assert.True(Nav.Crowd.Units.Single(u=>u.Id==id).Held);
            Nav.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>()));Nav.ApplyResults();Assert.True(Nav.Crowd.Units.Single(u=>u.Id==id).Held,"Failed path preserves prior HOLD");
            Send(PlayableCommandKind.Move,id,new NavPoint(-10,-12));request=Request(id);Nav.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal}));Nav.ApplyResults();Assert.False(Nav.Crowd.Units.Single(u=>u.Id==id).Held);
        }
        [TestCase(PlayableEntityKind.Tank)] [TestCase(PlayableEntityKind.Explorer)] [TestCase(PlayableEntityKind.Shkval)]
        public void StationaryDefenseCannotCancelPendingHeldAttackMove(PlayableEntityKind kind)
        {
            int id=Spawn(new NavPoint(-12,-12),PlayableOwner.Player,kind);Spawn(new NavPoint(-6,-12),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            Send(PlayableCommandKind.Hold,id);Send(PlayableCommandKind.AttackMove,id,new NavPoint(-12,-10));var request=Request(id);Seconds(.2);
            Assert.True(Nav.Crowd.Units.Single(u=>u.Id==id).Held);Assert.True(Nav.IsPending(id));Assert.AreEqual(new NavPoint(-12,-12),Nav.Crowd.Units.Single(u=>u.Id==id).Position);
            Nav.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>()));Seconds(.1);Assert.True(Nav.Crowd.Units.Single(u=>u.Id==id).Held);Assert.Null(View().Entities.Single(e=>e.Id==id).CurrentOrder);
        }
        [Test] public void HeldDefenseFiresWithoutChasingAndExplicitMoveHasPriority()
        {
            var start=new NavPoint(-12,-12);int id=Spawn(start,PlayableOwner.Player,PlayableEntityKind.Tank);int enemy=Spawn(new NavPoint(-6,-12),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            Send(PlayableCommandKind.Hold,id);Seconds(1);
            var held=View().Entities.Single(e=>e.Id==id);Assert.True(held.Held);Assert.AreEqual(start,held.Position);Assert.AreEqual(enemy,held.TargetId);Assert.True(View().Projectiles.Any(p=>p.OwnerId==id));
            Send(PlayableCommandKind.Move,id,new NavPoint(-12,-10));var request=Request(id);Seconds(.1);Assert.True(Nav.Crowd.Units.Single(u=>u.Id==id).Held,"Prior idle HOLD survives pending replacement");
            Nav.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal}));Seconds(.1);Assert.AreEqual(0,View().Entities.Single(e=>e.Id==id).TargetId,"Activated explicit move suppresses idle defense");Assert.False(Nav.Crowd.Units.Single(u=>u.Id==id).Held);
            Send(PlayableCommandKind.Hold,id);Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.Attack,id,targetId:enemy));Assert.False(Nav.Crowd.Units.Single(u=>u.Id==id).Held);
        }
        private static NavigationSession Open()
        {var s=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),NavigationProfile.Default);s.Crowd.Add(1,new NavPoint(-4,0));s.Crowd.Add(2,new NavPoint(0,0),1.1,4,4.5);s.Stop(2,true);return s;}
        private static NavigationRequest Take(NavigationSession s){Assert.True(s.Requests.TryDequeue(out var r));return r;}
        [Test] public void ExactHeldCircleBypassNeverMovesHeldAndRejectsUncheckedFallback()
        {
            var s=Open();s.Move(1,new NavPoint(4,0));var r=Take(s);Assert.False(r.Geometry.SegmentFree(r.Start,r.Goal,r.Profile.Radius));
            var path=new SharedFlowRouter(r.Geometry,r.Profile).FindPath(r.Start,r.Goal);Assert.Greater(path.Length,1);var previous=r.Start;foreach(var p in path){Assert.True(r.Geometry.SegmentFree(previous,p,r.Profile.Radius));previous=p;}
            s.Answers.TryEnqueue(new NavigationAnswer(r,new[]{r.Goal}));s.ApplyResults();Assert.AreEqual(1,s.RejectedResults);
            s.Move(1,new NavPoint(4,0));r=Take(s);s.Answers.TryEnqueue(new NavigationAnswer(r,path));s.ApplyResults();for(int i=0;i<600;i++)s.Step(1d/30d);
            Assert.True(s.Crowd.TryGet(2,out var held));Assert.True(held.Held);Assert.AreEqual(new NavPoint(0,0),held.Position);Assert.False(s.Crowd.Units[0].Moving);
            Assert.Contains(s.Crowd.Units[0].Outcome,new[]{NavigationOutcome.Arrived,NavigationOutcome.Blocked});
            Assert.AreEqual(0,s.CaptureState().Geometry.Obstacles.Length,"Production/static base geometry has no held bodies");
        }
        [TestCase(PlayableEntityKind.Tank)] [TestCase(PlayableEntityKind.Explorer)] [TestCase(PlayableEntityKind.Shkval)]
        public void MovementGeometryUsesEachNativeHeldTypeRadius(PlayableEntityKind kind)
        {
            int mover=Spawn(new NavPoint(-16,-12),PlayableOwner.Player,PlayableEntityKind.Explorer);int held=Spawn(new NavPoint(-12,-12),PlayableOwner.Player,kind);Send(PlayableCommandKind.Hold,held);
            Send(PlayableCommandKind.Move,mover,new NavPoint(-8,-12));var r=Request(mover);
            var circle=r.Geometry.Obstacles.Single(o=>o.CircleRadius>0&&o.CircleCenter.X==-12&&o.CircleCenter.Z==-12);
            Assert.AreEqual(PlayableUnitRules.Radius(profile,kind),circle.CircleRadius);Assert.AreEqual(profile.ExplorerCollisionRadius,r.Profile.Radius);
            Assert.False(r.Geometry.SegmentFree(r.Start,r.Goal,r.Profile.Radius));Assert.True(r.BaseGeometry.SegmentFree(r.Start,r.Goal,r.Profile.Radius));
        }
        [Test] public void HeldRemovalSameIdReuseAndToggleRejectOldGeometryIdentity()
        {
            var s=Open();s.Move(1,new NavPoint(4,0));var old=Take(s);s.Remove(2);s.Crowd.Add(2,new NavPoint(0,0),1.1,4,4.5);s.Stop(2,true);
            s.Answers.TryEnqueue(new NavigationAnswer(old,new SharedFlowRouter(old.Geometry,old.Profile).FindPath(old.Start,old.Goal)));s.ApplyResults();Assert.AreEqual(1,s.RejectedResults);Assert.True(s.IsPending(1));
            var current=Take(s);s.Stop(2,false);s.Stop(2,true);s.Answers.TryEnqueue(new NavigationAnswer(current,new[]{current.Goal}));s.ApplyResults();Assert.AreEqual(2,s.RejectedResults);
        }
        [Test] public void EnemyHoldExcludedAndEndpointInsideAllyHoldIsUnreachable()
        {
            var s=Open();s.Crowd.Units.Single(u=>u.Id==2).Team=1;s.Move(1,new NavPoint(4,0));var r=Take(s);Assert.True(r.Geometry.SegmentFree(r.Start,r.Goal,r.Profile.Radius));s.Stop(1,false);
            s.Crowd.Units.Single(u=>u.Id==2).Team=0;s.Move(1,new NavPoint(0,0));r=Take(s);Assert.IsEmpty(new SharedFlowRouter(r.Geometry,r.Profile).FindPath(r.Start,r.Goal));
        }
        [Test] public void HeldPendingStateRoundTripPreservesBinding()
        {
            var s=Open();s.Move(1,new NavPoint(4,0));var state=s.CaptureState();var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),NavigationProfile.Default);restored.RestoreState(state);
            var r=Take(restored);restored.Answers.TryEnqueue(new NavigationAnswer(r,new SharedFlowRouter(r.Geometry,r.Profile).FindPath(r.Start,r.Goal)));restored.ApplyResults();Assert.AreEqual(1,restored.AppliedResults);Assert.True(restored.Crowd.Units.Single(u=>u.Id==2).Held);
        }
        [Test] public void HeldGeometryChangePreservesHoldAndGenerationAnswerCannotRevive()
        {
            var s=Open();s.Move(1,new NavPoint(4,0));var old=Take(s);s.ChangeGeometry(new NavGeometry(20,Array.Empty<NavObstacle>(),2));Assert.True(s.Crowd.Units.Single(u=>u.Id==2).Held);Assert.AreEqual(NavigationOutcome.Held,s.Crowd.Units.Single(u=>u.Id==2).Outcome);
            s.Answers.TryEnqueue(new NavigationAnswer(old,new[]{old.Goal}));s.ApplyResults();Assert.AreEqual(1,s.RejectedResults);
            var next=new NavigationSession(2,new NavGeometry(20,Array.Empty<NavObstacle>(),2),NavigationProfile.Default);next.Crowd.Add(1,new NavPoint(-4,0));next.Stop(1,true);next.Answers.TryEnqueue(new NavigationAnswer(old,new[]{old.Goal}));next.ApplyResults();Assert.True(next.Crowd.Units[0].Held);Assert.AreEqual(1,next.RejectedResults);
        }
        private static bool Until(Func<bool> predicate){var until=DateTime.UtcNow.AddSeconds(5);while(DateTime.UtcNow<until){if(predicate())return true;Thread.Sleep(10);}return predicate();}
        [Test] public void HoldPauseQueuedReplacementAndRestartKeepGenerationSafety()
        {
            var runtime=new PlayableRuntime(profile,41,19092026,false,false);
            try{
                Assert.True(Until(()=>runtime.Latest?.Entities.Any(e=>e.Owner==PlayableOwner.Player)==true));int id=runtime.Latest.Entities.First(e=>e.Owner==PlayableOwner.Player).Id;
                Assert.True(runtime.TrySubmit(new PlayableCommand(41,1,"player-1",PlayableCommandKind.Hold,new[]{id})).Accepted);
                Assert.True(Until(()=>runtime.Latest.Entities.Single(e=>e.Id==id).Held));var position=runtime.Latest.Entities.Single(e=>e.Id==id).Position;
                runtime.RequestPause(true);Assert.True(Until(()=>runtime.Latest.Paused));long tick=runtime.Latest.Tick;
                Assert.AreEqual(PlayableCommandStatus.Rejected,runtime.TrySubmit(new PlayableCommand(41,2,"player-1",PlayableCommandKind.Stop,new[]{id})).Status);
                Assert.True(Until(()=>runtime.Latest.Sequence>tick+3));Assert.True(runtime.Latest.Entities.Single(e=>e.Id==id).Held);Assert.AreEqual(position,runtime.Latest.Entities.Single(e=>e.Id==id).Position);Assert.AreEqual(tick,runtime.Latest.Tick);
                runtime.RequestPause(false);Assert.True(Until(()=>!runtime.Latest.Paused));
                Assert.True(runtime.TrySubmit(new PlayableCommand(41,3,"player-1",PlayableCommandKind.Stop,new[]{id})).Accepted);Assert.True(runtime.TrySubmit(new PlayableCommand(41,4,"player-1",PlayableCommandKind.Hold,new[]{id})).Accepted);
                Assert.True(Until(()=>runtime.DrainReceipts().Any(r=>r.Sequence==4&&r.Status==PlayableCommandStatus.Applied)));Assert.True(runtime.Latest.Entities.Single(e=>e.Id==id).Held);
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
                var next=new PlayableRuntime(profile,42,19092026,false,false);try{Assert.AreEqual(PlayableCommandStatus.StaleGeneration,next.TrySubmit(new PlayableCommand(41,5,"player-1",PlayableCommandKind.Hold,new[]{id})).Status);Assert.True(Until(()=>next.Latest!=null));Assert.False(next.Latest.Entities.Any(e=>e.Held));}finally{next.Dispose();Assert.True(Until(()=>next.IsStopped));}
            }finally{runtime.Dispose();Assert.True(Until(()=>runtime.IsStopped));}
        }
        [Test] public void DestroyedHeldUnitInvalidatesFootprintAndLatePendingAnswer()
        {
            int id=Spawn(new NavPoint(-12,-12),PlayableOwner.Player,PlayableEntityKind.Explorer);Send(PlayableCommandKind.Hold,id);Send(PlayableCommandKind.Move,id,new NavPoint(-10,-12));var request=Request(id);
            Call("Damage",id,10000);Assert.False(Nav.Crowd.TryGet(id,out _));Nav.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal}));Nav.ApplyResults();Assert.AreEqual(1,Nav.RejectedResults);Assert.False(View().Entities.Any(e=>e.Id==id));
        }
        [Test] public void SourceIdleDefenseMetadataDoesNotChangeFrozenIdentity()
        {
            Assert.AreEqual("unity-owner-research-queue-u6-v1",profile.ProfileId);Assert.AreEqual(1,profile.Revision);Assert.AreEqual(1.1,profile.IdleAutoDefenseMultiplier);
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="system.idleAutoDefenseMultiplier");Assert.Greater(field.Step,0);Assert.LessOrEqual(field.Minimum,1.1);Assert.GreaterOrEqual(field.Maximum,1.1);
        }
    }
}
