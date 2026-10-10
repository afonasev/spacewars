using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class SpatialCompletionTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private object domain; private NavigationSession nav; private long sequence;
        private object Call(string name,params object[] args)=>D.GetMethod(name,F).Invoke(domain,args);
        [SetUp] public void Setup(){domain=Activator.CreateInstance(D,F,null,new object[]{PlayableProfile.Default,1L,false},null);nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);sequence=0;}
        private int Spawn(double x,double z,PlayableOwner owner=PlayableOwner.Player)=>(int)Call("SpawnUnit",new NavPoint(x,z),owner,PlayableEntityKind.Tank);
        private PlayableCommandStatus Send(int id,NavPoint goal,PlayableCommandKind kind=PlayableCommandKind.Move,int target=0,PlayableOrderOrigin origin=PlayableOrderOrigin.Human)
            =>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,"player-1",kind,new[]{id},goal,targetId:target,origin:origin,source:origin==PlayableOrderOrigin.Ai?"receipt-ai":null,jobId:origin==PlayableOrderOrigin.Ai?11:0,actionId:origin==PlayableOrderOrigin.Ai?12:0),null);
        private NavigationRequest[] Drain(){var rows=new List<NavigationRequest>();while(nav.Requests.TryDequeue(out var r))rows.Add(r);return rows.ToArray();}
        private void Deliver(){foreach(var r in Drain())nav.Answers.TryEnqueue(new NavigationAnswer(r,new SharedFlowRouter(r.Geometry,r.Profile).FindPath(r.Start,r.Goal)));nav.ApplyResults();}
        private void Step(int ticks=1){for(int i=0;i<ticks;i++)Call("Step",1d/30);}
        private PlayableSpatialCompletion Receipt(int id,PlayableOwner owner=PlayableOwner.Player)=>(PlayableSpatialCompletion)Call("SpatialCompletion",id,owner);
        private void Arrive(int id){for(int i=0;i<600&&Receipt(id)==null;i++){Deliver();Step();}Assert.NotNull(Receipt(id));}
        private byte[] Save()=>(byte[])Call("CaptureWorldBytes",19092026,"a1q-world");
        private void Restore(byte[] bytes){domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,19092026,"a1q-world"});nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);}
        private static string Identity(PlayableSpatialCompletion r)=>r==null?"none":$"{r.Order.Generation}/{r.Order.Sequence}/{r.Order.Revision}/{r.GroupId}/{r.Incarnation}/{r.OriginalTarget.X:R}/{r.OriginalTarget.Z:R}/{r.SurfaceId}/{r.ActivationTick}/{r.CompletionTick}/{r.Order.Source}/{r.Order.JobId}/{r.Order.ActionId}";
        private static int Tag(byte[] bytes,int tag){var needle=BitConverter.GetBytes(tag);int found=-1;for(int i=0;i<=bytes.Length-4;i++){
            if(bytes[i]!=needle[0]||bytes[i+1]!=needle[1]||bytes[i+2]!=needle[2]||bytes[i+3]!=needle[3])continue;
            Assert.AreEqual(-1,found,"Tag must be unique.");found=i;
        }Assert.GreaterOrEqual(found,0);return found;}

        [Test] public void ReceiptRequiresActivatedArrivalAndSurvivesAutomaticIdle()
        {
            int id=Spawn(-12,-12);var goal=new NavPoint(-10,-12);Assert.AreEqual(PlayableCommandStatus.Applied,Send(id,goal,origin:PlayableOrderOrigin.Ai));
            Assert.Null(Receipt(id));Step(3);Assert.Null(Receipt(id));Deliver();Assert.Null(Receipt(id));Arrive(id);
            var r=Receipt(id);Assert.AreEqual(goal,r.OriginalTarget);Assert.AreEqual(PlayableSpatialCompletion.FlatSurface,r.SurfaceId);Assert.AreEqual(11,r.Order.JobId);Assert.AreEqual(12,r.Order.ActionId);
            Assert.LessOrEqual(r.Order.Tick,r.ActivationTick);Assert.LessOrEqual(r.ActivationTick,r.CompletionTick);
            Assert.Null(((PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,PlayableOwner.Player)).Entities.Single(e=>e.Id==id).CurrentOrder);
            var identity=Identity(r);Step(45);Assert.AreEqual(identity,Identity(Receipt(id)));Assert.Null(Receipt(id,PlayableOwner.Enemy));
        }
        [Test] public void PendingReplacementCannotReceiveOldArrivalOrStaleAnswer()
        {
            int id=Spawn(-12,-12);Send(id,new NavPoint(-10,-12));var old=Drain();nav.Answers.TryEnqueue(new NavigationAnswer(old[0],new[]{old[0].Goal}));nav.ApplyResults();
            Send(id,new NavPoint(-12,-10));var replacement=Drain();Step(200);Assert.Null(Receipt(id));Assert.True(nav.IsPending(id));
            nav.Answers.TryEnqueue(new NavigationAnswer(old[0],new[]{old[0].Goal}));nav.ApplyResults();Assert.Null(Receipt(id));
            // Valid late answer is attached from the actor's actual position by the navigation contract.
            nav.Answers.TryEnqueue(new NavigationAnswer(replacement[0],new[]{replacement[0].Goal}));Arrive(id);Assert.AreEqual(2,Receipt(id).Order.Sequence);
        }
        [TestCase(PlayableCommandKind.Stop)][TestCase(PlayableCommandKind.Hold)][TestCase(PlayableCommandKind.Move)][TestCase(PlayableCommandKind.AttackMove)][TestCase(PlayableCommandKind.Attack)][TestCase(PlayableCommandKind.Follow)]
        public void AcceptedReplacementClearsOnlyRecipient(PlayableCommandKind kind)
        {
            int a=Spawn(-12,-12),b=Spawn(-14,-12),leader=Spawn(-12,-8),enemy=Spawn(-6,-12,PlayableOwner.Enemy);
            Send(a,new NavPoint(-10,-12));Send(b,new NavPoint(-14,-10));Arrive(a);Arrive(b);var other=Identity(Receipt(b));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,new NavPoint(-10,-10),kind,kind==PlayableCommandKind.Follow?leader:enemy));
            Assert.Null(Receipt(a));Assert.AreEqual(other,Identity(Receipt(b)));Step(10);Assert.Null(Receipt(a));
        }
        [Test] public void RejectedCommandDoesNotEraseReceipt()
        {
            int id=Spawn(-12,-12);Send(id,new NavPoint(-10,-12));Arrive(id);var before=Identity(Receipt(id));
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(id,new NavPoint(999,999)));Assert.AreEqual(before,Identity(Receipt(id)));
        }
        [TestCase(false)][TestCase(true)] public void FailedOrCancelledRouteNeverCreatesReceipt(bool cancel)
        {
            int id=Spawn(-12,-12);Send(id,new NavPoint(-10,-12));var request=Drain().Single();
            if(cancel)Send(id,default(NavPoint),PlayableCommandKind.Stop);else nav.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>()));
            Step(200);Assert.Null(Receipt(id));nav.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal}));Step();Assert.Null(Receipt(id));
        }
        [TestCase(0)][TestCase(8)][TestCase(200)] public void PendingActiveAndCompletedSaveRestorePreserveContinuation(int ticks)
        {
            int id=Spawn(-12,-12);Send(id,new NavPoint(-10,-12));if(ticks>0){Deliver();Step(ticks);}var bytes=Save();
            Arrive(id);var expected=Identity(Receipt(id));Restore(bytes);Arrive(id);Assert.AreEqual(expected,Identity(Receipt(id)));
            CollectionAssert.AreEqual(Save(),Save());
        }
        [Test] public void HeldReplacementCanBeSavedBeforeActivation()
        {
            int id=Spawn(-12,-12);Send(id,default(NavPoint),PlayableCommandKind.Hold);Send(id,new NavPoint(-10,-12));Restore(Save());Arrive(id);
        }
        [TestCase(false)][TestCase(true)] public void ExactLegacyV9LayoutsDoNotInventReceipts(bool preA1)
        {
            int id=Spawn(-12,-12);Send(id,new NavPoint(-10,-12));Deliver();Step(4);var state=PlayableWorldState.Decode(Save());var binding=state.Binding.ToArray();
            // The positional domain and history bytes are unchanged; omit only the new tagged tail.
            state=PlayableWorldState.Decode(QueueLegacyLayout.Convert(state.Encode(),PlayableProfile.Default,preA1,nav.Crowd.Units.Count));
            Assert.AreEqual(9,state.Version);Restore(state.Encode());Step(200);Assert.Null(Receipt(id));
            CollectionAssert.AreEqual(binding,PlayableWorldState.Decode(Save()).Binding);
            Send(id,new NavPoint(-12,-10));Arrive(id);Assert.AreEqual(2,Receipt(id).Order.Sequence);
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)] public void UnknownTruncatedDuplicateAndInvalidReceiptExtensionRejects(int corruption)
        {
            int id=Spawn(-12,-12);Send(id,new NavPoint(-10,-12));Arrive(id);var state=PlayableWorldState.Decode(Save());int start=Tag(state.Domain,0x53505231);
            if(corruption==0)state.Domain[start]^=1;
            if(corruption==1)state.Domain=state.Domain.Take(state.Domain.Length-1).ToArray();
            if(corruption==2){int count=start+12;var record=state.Domain.Skip(count+4).ToArray();state.Domain=state.Domain.Concat(record).ToArray();BitConverter.GetBytes(2).CopyTo(state.Domain,count);}
            if(corruption==3)BitConverter.GetBytes(long.MaxValue).CopyTo(state.Domain,state.Domain.Length-8);
            Assert.Throws<TargetInvocationException>(()=>Restore(state.Encode()));
        }
        [Test] public void DeathAndRestartCannotReuseReceipt()
        {
            int id=Spawn(-12,-12);Send(id,new NavPoint(-10,-12));Arrive(id);Call("Damage",id,10000);Assert.Null(Receipt(id));Restore(Save());Assert.Null(Receipt(id));
            domain=Activator.CreateInstance(D,F,null,new object[]{PlayableProfile.Default,2L,false},null);nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);Assert.Null(Receipt(id));
        }
        [Test] public void FoundationEvacuationPendingAndInstalledReplacementRoundTripWithoutOldReceipt()
        {
            var view=(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,PlayableOwner.Player);
            var pos=view.Sites.Single(s=>s.Site.Id==3).Site.Position;
            int id=Spawn(pos.X,pos.Z);Step(152);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(id,new NavPoint(pos.X-4,pos.Z)));Deliver();
            Assert.AreEqual(PlayableCommandStatus.Applied,(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:3,buildingKind:PlayableBuildingKind.Outpost),null));
            Step();Assert.True(nav.IsPending(id));Restore(Save());Assert.Null(Receipt(id));
            Deliver();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Restore(Save());
            for(int i=0;i<200;i++){Deliver();Step();}Assert.Null(Receipt(id));
        }
        [TestCase(false)][TestCase(true)] public void ConstructionBlockingPendingOrReplannedGoalRevokesExecutionAndRestores(bool active)
        {
            var view=(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,PlayableOwner.Player);
            var home=view.Sites.Single(s=>s.Site.Id==1);var goal=home.Site.Slots.Single(s=>s.Id==1).Position;
            int id=Spawn(-12,-12);Assert.AreEqual(PlayableCommandStatus.Applied,Send(id,goal));
            if(active){Deliver();Assert.True(nav.Move(id,goal));}
            Assert.AreEqual(PlayableCommandStatus.Applied,(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:1,slotId:1,buildingKind:PlayableBuildingKind.Factory,parentId:home.CenterId),null));
            Step();Assert.False(nav.IsPending(id));Assert.False(((NavGeometry)D.GetProperty("Geometry",F).GetValue(domain)).IsFree(goal,PlayableProfile.Default.TankCollisionRadius));
            Restore(Save());Assert.Null(Receipt(id));Step(20);Assert.Null(Receipt(id));
        }
        [TestCase(false)][TestCase(true)] public void InconsistentPendingGoalOrInstalledOrderWatermarkRejects(bool installed)
        {
            int id=Spawn(-12,-12);Send(id,new NavPoint(-10,-12));if(installed)Deliver();
            var entries=(IDictionary)D.GetField("spatialExecutions",F).GetValue(domain);var record=entries[id];
            record.GetType().GetField(installed?"RouteOrder":"AssignedGoal",F).SetValue(record,installed?(object)long.MaxValue:new NavPoint(20,20));
            Assert.Throws<TargetInvocationException>(()=>Restore(Save()));
        }
        [Test] public void AttackMoveArrivalWaitsForVisibleAnchorClearance()
        {
            int id=Spawn(-12,-26),enemy=Spawn(2,-26,PlayableOwner.Enemy);Send(id,new NavPoint(-10,-26),PlayableCommandKind.AttackMove);Deliver();Step(200);Assert.Null(Receipt(id));Call("Damage",enemy,10000);Step(20);Assert.NotNull(Receipt(id));Assert.AreEqual(PlayableCommandKind.AttackMove,Receipt(id).Order.Kind);
        }
        [Test] public void AuthoredTerrainReceiptBindsProviderSurfaceAfterRealArrival()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;
            domain=Activator.CreateInstance(D,F,null,new object[]{profile,1L,false},null);nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);
            var id=nav.Crowd.Units.First(u=>u.Team==0).Id; // Use the authored existing player actor.
            var from=nav.Crowd.Units.Single(u=>u.Id==id).Position;
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(id,from));Deliver();Step(200);Assert.NotNull(Receipt(id));Assert.AreNotEqual(PlayableSpatialCompletion.FlatSurface,Receipt(id).SurfaceId);Assert.True(profile.AuthoredMap.IsValidLocation(Receipt(id).OriginalLocation,0));
        }
        [Test] public void OwnerProjectionPublishesOnlyOwnReceipt()
        {
            int id=Spawn(-12,-12);Send(id,new NavPoint(-10,-12));Arrive(id);
            var own=(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,PlayableOwner.Player);
            Assert.AreEqual(Identity(Receipt(id)),Identity(own.Entities.Single(e=>e.Id==id).Completion));
            var enemy=(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,PlayableOwner.Enemy);
            Assert.True(enemy.Entities.All(e=>e.Completion==null));
        }
    }
}
