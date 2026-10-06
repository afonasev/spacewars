using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Globalization;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    // Controlled authority fixtures. Crowd-only blockers are test objects, never Player evidence.
    public sealed class FactoryAlternativeExitTests
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private object domain;private PlayableProfile p;private long seq;private int blocker=10000;
        private object Call(string name,params object[] args)=>Domain.GetMethod(name,Flags).Invoke(domain,args);
        private NavigationSession Nav=>(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(domain);
        private IDictionary Buildings=>(IDictionary)Domain.GetField("buildings",Flags).GetValue(domain);
        private PlayableSnapshot View()=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,PlayableOwner.Player);
        private PlayableBuildingPrivateState State(int id)=>View().Buildings.Single(b=>b.Id==id).PrivateState;
        [SetUp] public void Create(){p=PlayableProfile.Default;domain=Activator.CreateInstance(Domain,Flags,null,new object[]{p,1L,false},null);seq=0;blocker=10000;}
        private int Factory(int slot=2)
        {
            Call("AddCredits",PlayableOwner.Player,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,slot,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            return View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory&&b.SlotId==slot).Id;
        }
        private NavPoint[] Candidates(int f,PlayableEntityKind kind)=>(NavPoint[])Call("FactoryExitCandidates",Buildings[f],kind);
        private PlayableCommandStatus Send(PlayableCommandKind kind,int id,long order=0)=>
            (PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++seq,"player-1",kind,new[]{id},productionOrderId:order),null);
        private void Queue(int f,PlayableEntityKind kind)=>Assert.AreEqual(PlayableCommandStatus.Applied,Send(kind==PlayableEntityKind.Explorer?PlayableCommandKind.QueueExplorer:kind==PlayableEntityKind.Shkval?PlayableCommandKind.QueueShkval:PlayableCommandKind.QueueTank,f));
        private int Block(NavPoint point,double radius)
        {int id=blocker++;Nav.Crowd.Add(id,point,radius,p.TankSpeed,p.TankTurnSpeed);Nav.Crowd.Stop(id,true);return id;}
        private void BlockAll(int f,PlayableEntityKind kind)
        {
            double r=PlayableUnitRules.Radius(p,kind);
            foreach(var point in Candidates(f,kind))if(Nav.Crowd.CanPlace(point,r))Block(point,r);
            Assert.False(Candidates(f,kind).Any(q=>Nav.Crowd.CanPlace(q,r)));
        }
        private void Advance(double seconds=100)=>Call("AdvanceProduction",seconds);
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));



        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void OccupiedAuthoredFirstAndFreeSecondReleaseExactlyOnceWithTypedPendingPlainMove(PlayableEntityKind kind)
        {
            int f=Factory();var points=Candidates(f,kind);double r=PlayableUnitRules.Radius(p,kind);
            Assert.True(Nav.Crowd.CanPlace(points[0],r));Block(points[0],r);
            Assert.True(Nav.Crowd.CanPlace(points[1],r),"Exact first occupied / second valid fixture");
            var rallyTarget=new NavPoint(points[1].X+5,points[1].Z);
            Assert.AreEqual(PlayableCommandStatus.Accepted,Call("Apply",new PlayableCommand(1,++seq,"player-1",PlayableCommandKind.SetRally,new[]{f},rallyTarget),null));
            for(int step=0;step<100&&State(f).PendingRally.HasValue;step++){
                Call("AdvanceRally");while(Nav.Requests.TryDequeue(out var proof)){Nav.Answers.TryEnqueue(new NavigationAnswer(proof,new SharedFlowRouter(proof.Geometry,proof.Profile).FindPath(proof.Start,proof.Goal)));Nav.ApplyResults();Call("AdvanceRally");}
            }
            Assert.True(State(f).HasRally,"Navigation regression requires an explicitly accepted rally.");
            int living=View().Population.Living,count=View().Entities.Count;Queue(f,kind);int paid=View().Credits;
            Advance();Assert.IsEmpty(State(f).Orders);Assert.Zero(View().Population.Reserved);
            Assert.AreEqual(living+PlayableUnitRules.Population(p,kind),View().Population.Living);Assert.AreEqual(paid,View().Credits);
            var unit=View().Entities.OrderByDescending(u=>u.Id).First();Assert.AreEqual(kind,unit.Kind);Assert.AreEqual(points[1],unit.Position);
            Assert.True(Nav.IsPending(unit.Id));Assert.False(unit.Moving);Assert.Zero(unit.TargetId);
            Assert.True(Nav.Requests.TryDequeue(out var request));Assert.AreEqual(unit.Id,request.Entity);Assert.AreEqual(r,request.Profile.Radius);
            Advance();Assert.AreEqual(count+1,View().Entities.Count);Assert.AreEqual(paid,View().Credits);
            var blockerState=Nav.Crowd.Units.Single(u=>u.Id==10000);Assert.True(blockerState.Held);Assert.AreEqual(points[0],blockerState.Position);
            Nav.Answers.TryEnqueue(new NavigationAnswer(request,new SharedFlowRouter(request.Geometry,request.Profile).FindPath(request.Start,request.Goal)));Nav.ApplyResults();
            Assert.True(Nav.Crowd.TryGet(unit.Id,out var navUnit));Assert.True(navUnit.Moving);Assert.False(navUnit.Held);
        }

        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void AllBlockedPreservesPaidHeadThenOneFreedTransfersReserveOnce(PlayableEntityKind kind)
        {
            int f=Factory();BlockAll(f,kind);Queue(f,kind);int paid=View().Credits,living=View().Population.Living;
            Advance();var head=State(f).Orders.Single();Assert.True(head.Active);Assert.Zero(head.Remaining);
            for(int tick=0;tick<10;tick++)Advance(.1);
            Assert.AreEqual(head.Id,State(f).Orders.Single().Id);Assert.AreEqual(living,View().Population.Living);
            Assert.AreEqual(PlayableUnitRules.Population(p,kind),View().Population.Reserved);Assert.AreEqual(paid,View().Credits);
            // Free one isolated outward anchor, retaining all other held blockers.
            var points=Candidates(f,kind);var first=points[0];double r=PlayableUnitRules.Radius(p,kind);
            foreach(var u in Nav.Crowd.Units.Where(u=>u.Id>=10000&&Distance(u.Position,first)<u.Radius+r).ToArray())Nav.Crowd.Remove(u.Id);
            Assert.True(Nav.Crowd.CanPlace(first,r));Advance(.1);Assert.IsEmpty(State(f).Orders);
            Assert.AreEqual(first,View().Entities.OrderByDescending(u=>u.Id).First().Position);
            Assert.Zero(View().Population.Reserved);Assert.AreEqual(living+PlayableUnitRules.Population(p,kind),View().Population.Living);
            Advance();Assert.AreEqual(paid,View().Credits);Assert.AreEqual(living+PlayableUnitRules.Population(p,kind),View().Population.Living);
        }

        [Test] public void OrderIsGlobalRadialAndTypedDistanceAfterAuthoredAnchor()
        {
            int f=Factory();var b=View().Buildings.Single(x=>x.Id==f);
            foreach(var kind in new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval})
            {
                var points=Candidates(f,kind);CollectionAssert.AreEqual(points,Candidates(f,kind));Assert.AreEqual(17,points.Length);
                Assert.AreEqual(p.FactoryExitDistance,Distance(b.Position,points[0]),1e-9);
                double distance=p.FactoryFootprintRadius+2*PlayableUnitRules.Radius(p,kind);
                for(int i=0;i<16;i++){Assert.AreEqual(b.Position.X+Math.Cos(i*Math.PI/8)*distance,points[i+1].X,1e-9);Assert.AreEqual(b.Position.Z+Math.Sin(i*Math.PI/8)*distance,points[i+1].Z,1e-9);}
            }
        }

        [Test] public void CurrentGeometryRevalidatedAtCompletedHeadWithoutCachedFreeAnchor()
        {
            int f=Factory();Queue(f,PlayableEntityKind.Tank);Advance(.1);
            var old=(NavGeometry)Domain.GetProperty("Geometry",Flags).GetValue(domain);
            var points=Candidates(f,PlayableEntityKind.Tank);
            var geometry=new NavGeometry(p.ArenaHalfExtent,old.Obstacles.Concat(points.Select(q=>new NavObstacle(q.X-.1,q.Z-.1,q.X+.1,q.Z+.1))).ToArray(),old.Revision+1);
            Nav.ChangeGeometry(geometry);Advance();Assert.Zero(State(f).Orders.Single().Remaining);Assert.AreEqual(p.TankPopulationCost,View().Population.Reserved);
            Call("RebuildGeometry");Advance(.1);Assert.IsEmpty(State(f).Orders);Assert.Zero(View().Population.Reserved);
        }

        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void FullFootprintRejectsBoundaryAndBuildingEvenWhenCenterIsClear(PlayableEntityKind kind)
        {
            double r=PlayableUnitRules.Radius(p,kind);
            var g=new NavGeometry(32,new[]{new NavObstacle(-2,-2,2,2)},1);var crowd=new NavCrowd(g,p.Navigation);
            Assert.True(g.IsFree(new NavPoint(32-r-.001,10),r));Assert.False(crowd.CanPlace(new NavPoint(32-r+.001,10),r));
            Assert.True(g.IsFree(new NavPoint(2+r-.001,0),0));Assert.False(crowd.CanPlace(new NavPoint(2+r-.001,0),r));
            Assert.True(crowd.CanPlace(new NavPoint(2+r+.001,0),r));
        }

        [TestCase("cancel")][TestCase("destroy")][TestCase("sale")][TestCase("cascade")]
        public void BlockedCompletedProducerLifecycleCannotReleaseStaleHead(string action)
        {
            int f=Factory();BlockAll(f,PlayableEntityKind.Tank);Queue(f,PlayableEntityKind.Tank);Advance();int before=View().Credits,count=View().Entities.Count;
            if(action=="cancel")Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.CancelProductionOrder,f,State(f).Orders[0].Id));
            else if(action=="destroy")Call("Damage",f,(int)p.FactoryHealth);
            else Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.SellBuilding,action=="cascade"?1:f));
            Assert.Zero(View().Population.Reserved);Advance();Assert.AreEqual(count,View().Entities.Count);
            Assert.AreEqual(before+(action=="cancel"?p.TankCreditCost:action=="sale"?Math.Floor(p.FactoryCreditCost*p.BuildingSaleRefundRatio):action=="cascade"?Math.Floor((p.FactoryCreditCost+p.HeadquartersCreditCost)*p.BuildingSaleRefundRatio):0),View().Credits);
        }

        [Test] public void MixedSixPaidHeadsAndReconstructedFactoryRetainUniqueLivingIdentities()
        {
            int f=Factory();var kinds=new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval};
            for(int i=0;i<6;i++)Queue(f,kinds[i%3]);
            Assert.AreEqual(6,State(f).Orders.Count);long[] orders=State(f).Orders.Select(x=>x.Id).ToArray();
            int before=View().Entities.Count;
            for(int i=0;i<6;i++)
            {
                foreach(var u in Nav.Crowd.Units.Where(u=>u.Id>=10000).ToArray())Nav.Crowd.Remove(u.Id);
                Advance();Assert.AreEqual(6-i-1,State(f).Orders.Count);
                var spawned=View().Entities.OrderByDescending(u=>u.Id).First();Assert.AreEqual(kinds[i%3],spawned.Kind);
                // Relocate via actual crowd remove/add only in this deterministic fixture;
                // living authority entities/IDs remain, freeing the factory without movement timing.
                Nav.Crowd.Remove(spawned.Id);Nav.Crowd.Add(spawned.Id,new NavPoint(-8,-20+i*3),PlayableUnitRules.Radius(p,spawned.Kind),p.TankSpeed,p.TankTurnSpeed);
            }
            Assert.AreEqual(before+6,View().Entities.Count);var survivors=View().Entities.Select(x=>x.Id).ToArray();
            Call("Damage",f,(int)p.FactoryHealth);int rebuilt=Factory();Assert.AreNotEqual(f,rebuilt);Queue(rebuilt,PlayableEntityKind.Tank);
            Assert.Greater(State(rebuilt).Orders[0].Id,orders.Last());Advance();var ids=View().Entities.Select(x=>x.Id).ToArray();Assert.AreEqual(ids.Length,ids.Distinct().Count());Assert.True(survivors.All(ids.Contains));
        }
    }
}
