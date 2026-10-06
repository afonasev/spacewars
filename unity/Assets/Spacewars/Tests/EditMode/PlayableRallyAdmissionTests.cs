using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableRallyAdmissionTests
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private object domain;private PlayableProfile p;private long sequence;private int factory;
        private object Call(string method,params object[] args)=>Domain.GetMethod(method,F).Invoke(domain,args);
        private NavigationSession Nav=>(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);
        private IDictionary Buildings=>(IDictionary)Domain.GetField("buildings",F).GetValue(domain);
        private PlayableSnapshot View()=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,PlayableOwner.Player);
        private PlayableBuildingPrivateState State=>View().Buildings.Single(b=>b.Id==factory).PrivateState;
        private void Field(string name,object value)=>Buildings[factory].GetType().GetField(name).SetValue(Buildings[factory],value);
        private void Geometry(params NavObstacle[] obstacles){var g=new NavGeometry(p.ArenaHalfExtent,obstacles,((NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain)).Revision+1);Domain.GetProperty("Geometry",F).SetValue(domain,g);Nav.ChangeGeometry(g);}
        [SetUp] public void Create()
        {
            p=PlayableProfile.Default;sequence=0;domain=Activator.CreateInstance(Domain,F,null,new object[]{p,1L,false},null);
            Call("AddCredits",PlayableOwner.Player,10000d);Call("BuildAt",1,2,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null);Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            factory=View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;Field("Position",new NavPoint(-10,0));Field("Heading",0d);Geometry();
        }
        private PlayableCommandStatus Rally(NavPoint target,string owner="player-1",long generation=1)=>
            (PlayableCommandStatus)Call("Apply",new PlayableCommand(generation,++sequence,owner,PlayableCommandKind.SetRally,new[]{factory},target),null);
        private NavigationRequest Next(){Call("AdvanceRally");Assert.True(Nav.Requests.TryDequeue(out var r));return r;}
        private void Answer(NavigationRequest request,NavPoint[] route=null){Nav.Answers.TryEnqueue(new NavigationAnswer(request,route??new SharedFlowRouter(request.Geometry,request.Profile).FindPath(request.Start,request.Goal)));Nav.ApplyResults();Call("AdvanceRally");}
        private PlayableCommandReceipt[] Resolve()
        {
            for(int i=0;i<100&&State.PendingRally.HasValue;i++){
                Call("AdvanceRally");while(Nav.Requests.TryDequeue(out var r))Answer(r);
            }
            Assert.False(State.PendingRally.HasValue);return (PlayableCommandReceipt[])Call("DrainRallyReceipts");
        }
        private void Accept(NavPoint target){Assert.AreEqual(PlayableCommandStatus.Accepted,Rally(target));Assert.AreEqual(PlayableCommandStatus.Applied,Resolve().Last().Status);Assert.True(State.HasRally);Assert.AreEqual(target,State.Rally);}
        [Test] public void BackgroundAdmissionNeedsAllThreeTypedFullFootprints()
        {
            var target=new NavPoint(10,10);Assert.AreEqual(PlayableCommandStatus.Accepted,Rally(target));Assert.False(State.HasRally);
            var radii=new[]{p.TankCollisionRadius,p.ExplorerCollisionRadius,p.ShkvalCollisionRadius};
            foreach(double radius in radii){var r=Next();Assert.AreEqual(-factory,r.Entity);Assert.AreEqual(radius,r.Profile.Radius);Assert.False(State.HasRally);Answer(r);}
            Assert.True(State.HasRally);Assert.AreEqual(target,State.Rally);Assert.AreEqual(PlayableCommandStatus.Applied,((PlayableCommandReceipt[])Call("DrainRallyReceipts")).Single().Status);
        }
        [Test] public void FreeEndpointAcrossDisconnectedTopologyPreservesOldPoint()
        {
            var old=new NavPoint(-8,10);Accept(old);Geometry(new NavObstacle(-1,-p.ArenaHalfExtent,1,p.ArenaHalfExtent));
            var target=new NavPoint(10,10);Assert.True(((NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain)).IsFree(target,p.ShkvalCollisionRadius));
            Assert.AreEqual(PlayableCommandStatus.Accepted,Rally(target));Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Resolve().Last().Status);Assert.AreEqual(old,State.Rally);
        }
        [Test] public void SuccessfulTankProofDoesNotSkipOtherCompatibleFootprints()
        {
            var old=new NavPoint(-8,10);Accept(old);Rally(new NavPoint(10,10));var tank=Next();Assert.AreEqual(p.TankCollisionRadius,tank.Profile.Radius);Answer(tank);
            // A worker's missing Explorer proof must fail closed even though Tank
            // succeeded. Separate real geometry/radius fixtures exercise clearance.
            for(int i=0;i<100&&State.PendingRally.HasValue;i++){Call("AdvanceRally");while(Nav.Requests.TryDequeue(out var r))Answer(r,Array.Empty<NavPoint>());}
            Assert.False(State.PendingRally.HasValue);Assert.AreEqual(old,State.Rally);Assert.AreEqual(PlayableCommandStatus.InvalidTarget,((PlayableCommandReceipt[])Call("DrainRallyReceipts")).Last().Status);
        }
        [Test] public void EveryExitTemporarilyOccupiedByHeldBodiesStillAcceptsTopology()
        {
            var points=(NavPoint[])Call("FactoryExitCandidates",Buildings[factory],PlayableEntityKind.Shkval);int id=10000;
            foreach(var q in points)if(Nav.Crowd.CanPlace(q,p.ShkvalCollisionRadius)){Nav.Crowd.Add(id,q,p.ShkvalCollisionRadius,p.TankSpeed,p.TankTurnSpeed);Nav.Crowd.Stop(id++,true);}
            Assert.False(points.Any(q=>Nav.Crowd.CanPlace(q,p.ShkvalCollisionRadius)));Accept(new NavPoint(10,10));
            Assert.True(Nav.Crowd.Units.Where(u=>u.Id>=10000).All(u=>u.Held));
        }
        [Test] public void ForgedEndpointOnlyRouteCannotCrossWall()
        {
            Accept(new NavPoint(-8,10));Geometry(new NavObstacle(-1,-p.ArenaHalfExtent,1,p.ArenaHalfExtent));Rally(new NavPoint(10,10));
            var r=Next();Answer(r,new[]{r.Goal});Assert.False(State.Rally.X>0);Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Resolve().Last().Status);
        }
        [Test] public void LatestRequestRejectsOldAnswerAndInstallsOnlyNewTarget()
        {
            Rally(new NavPoint(10,10));var old=Next();var target=new NavPoint(12,12);Rally(target);Answer(old,new[]{old.Goal});
            Assert.False(State.HasRally);Assert.AreEqual(target,State.PendingRally.Value);var receipts=Resolve();Assert.AreEqual(1,receipts.Count(r=>r.Status==PlayableCommandStatus.Cancelled));Assert.AreEqual(1,receipts.Count(r=>r.Status==PlayableCommandStatus.Applied));Assert.AreEqual(target,State.Rally);
        }
        [Test] public void SameRevisionDifferentGeometryRestartsAllTypedProofs()
        {
            Rally(new NavPoint(10,10));var old=Next();var g=new NavGeometry(p.ArenaHalfExtent,new[]{new NavObstacle(-1,-p.ArenaHalfExtent,1,p.ArenaHalfExtent)},old.Geometry.Revision);
            Domain.GetProperty("Geometry",F).SetValue(domain,g);Nav.ChangeGeometry(g);Answer(old,new[]{old.Goal});Assert.False(State.HasRally);Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Resolve().Last().Status);
        }
        [Test] public void PauseRetainsSerializableIntentAndDiscardsOldJob()
        {
            var target=new NavPoint(10,10);Rally(target);var old=Next();Call("SetRallyPaused",true);Answer(old,new[]{old.Goal});Assert.False(State.HasRally);
            var saved=(PlayableRallyIntentState[])Call("CaptureRallyIntents");Assert.AreEqual(target.X,saved.Single().TargetX);Assert.AreEqual(sequence,saved.Single().Sequence);
            Call("SetRallyPaused",false);Assert.AreEqual(PlayableCommandStatus.Applied,Resolve().Last().Status);Assert.AreEqual(target,State.Rally);
        }
        [TestCase("Ready")][TestCase("Owner")][TestCase("Health")][TestCase("removed")]
        public void InvalidatedProducerCannotInstallLateResult(string reason)
        {
            Rally(new NavPoint(10,10));var old=Next();if(reason=="removed")Buildings.Remove(factory);else Field(reason,reason=="Ready"?(object)false:reason=="Health"?(object)0d:PlayableOwner.Enemy);
            Answer(old,new[]{old.Goal});Assert.AreEqual(PlayableCommandStatus.Cancelled,((PlayableCommandReceipt[])Call("DrainRallyReceipts")).Single().Status);
        }
        [Test] public void SaleAndReconstructionDoNotRevivePriorFactoryIntent()
        {
            Rally(new NavPoint(10,10));var old=Next();Assert.AreEqual(PlayableCommandStatus.Applied,Call("SellBuilding",factory,PlayableOwner.Player,null));Answer(old,new[]{old.Goal});
            Assert.AreEqual(PlayableCommandStatus.Cancelled,((PlayableCommandReceipt[])Call("DrainRallyReceipts")).Single().Status);
            int prior=factory;Call("RemoveBuilding",Buildings[factory],false);Call("BuildAt",1,2,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null);Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            factory=View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;Assert.AreNotEqual(prior,factory);Answer(old,new[]{old.Goal});Assert.False(State.HasRally);Assert.False(State.PendingRally.HasValue);
        }
        [Test] public void PausedIntentRestoresFreshValidationWithoutRestoringJobIdentity()
        {
            var target=new NavPoint(10,10);Rally(target);var old=Next();Call("SetRallyPaused",true);var saved=(PlayableRallyIntentState[])Call("CaptureRallyIntents");
            ((IDictionary)Domain.GetField("rallyWork",F).GetValue(domain)).Clear();Call("RestoreRallyIntents",(object)saved);saved[0].TargetX=999;
            Answer(old,new[]{old.Goal});Assert.False(State.HasRally);Assert.AreEqual(target,State.PendingRally.Value);
            Call("SetRallyPaused",false);Assert.AreEqual(PlayableCommandStatus.Applied,Resolve().Last().Status);Assert.AreEqual(target,State.Rally);
        }
        [Test] public void ReadyOwnerGenerationAndProducerTypeAreRequired()
        {
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Rally(new NavPoint(10,10),"enemy-1"));Assert.AreEqual(PlayableCommandStatus.StaleGeneration,Rally(new NavPoint(10,10),generation:2));
            Field("Ready",false);Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Rally(new NavPoint(10,10)));Field("Ready",true);Field("Kind",PlayableBuildingKind.Refinery);Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Rally(new NavPoint(10,10)));
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void AbsentPointProducesIdleAndAcceptedPointProducesPlainMove(PlayableEntityKind kind)
        {
            void Produce()=>Call("Apply",new PlayableCommand(1,++sequence,"player-1",kind==PlayableEntityKind.Tank?PlayableCommandKind.QueueTank:kind==PlayableEntityKind.Explorer?PlayableCommandKind.QueueExplorer:PlayableCommandKind.QueueShkval,new[]{factory}),null);
            Produce();Call("AdvanceProduction",100d);var idle=View().Entities.OrderByDescending(u=>u.Id).First();Assert.False(State.HasRally);Assert.False(idle.Moving);Assert.False(Nav.IsPending(idle.Id));Assert.IsNull(idle.CurrentOrder);
            Accept(new NavPoint(10,10));Produce();Call("AdvanceProduction",100d);var moved=View().Entities.OrderByDescending(u=>u.Id).First();Assert.AreEqual(kind,moved.Kind);Assert.True(Nav.IsPending(moved.Id));Assert.AreEqual(PlayableTacticalOrderKind.Move,moved.CurrentOrder.Kind);Assert.Zero(moved.TargetId);
        }
        [Test] public void PendingProbeDoesNotHoldProductionOrCombatTicks()
        {
            Rally(new NavPoint(10,10));Next();Call("Apply",new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.QueueTank,new[]{factory}),null);Call("AdvanceProduction",100d);Assert.IsEmpty(State.Orders);Assert.True(State.PendingRally.HasValue);
            long tick=View().Tick;Call("Step",1d/30);Assert.Greater(View().Tick,tick);Assert.True(State.PendingRally.HasValue);
        }
    }
}
