using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableTargetSpatialIndexTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private PlayableDomain domain;
        private object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,Hidden).Invoke(domain,args);
        private IDictionary Rows(string name)=>(IDictionary)typeof(PlayableDomain).GetField(name,Hidden).GetValue(domain);
        private object Unit(int id)=>Rows("units")[id];
        private static void Set(object row,string field,object value)=>row.GetType().GetField(field).SetValue(row,value);
        private int Spawn(PlayableOwner owner,NavPoint p,PlayableEntityKind kind=PlayableEntityKind.Tank)=>(int)Call("SpawnUnit",p,owner,kind);
        private int Building(NavPoint p){Call("AddBuilding",PlayableOwner.Enemy,PlayableBuildingKind.Refinery,p,true);return Rows("buildings").Keys.Cast<int>().Max();}
        [SetUp] public void Create(){domain=new PlayableDomain(PlayableProfile.Default,71,false);}
        [TearDown] public void Destroy(){domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}
        private static double Distance(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        // Current Unity rules, deliberately independent of the spatial query. No legacy source oracle.
        private int Reference(NavPoint point,double range,bool classes)
        {
            int best=0,bestClass=2;double distance=range;
            foreach(var group in new[]{Rows("units"),Rows("buildings")})foreach(int id in group.Keys.Cast<int>().OrderBy(x=>x))
            {
                if(!(bool)Call("VisibleTarget",PlayableOwner.Player,id))continue;
                object[] args={id,default(NavPoint),0};if(!(bool)Call("Target",args))continue;
                double d=Distance(point,(NavPoint)args[1]);int c=classes&&group==Rows("buildings")?1:0;
                if(d<=range&&(best==0||c<bestClass||c==bestClass&&(d<distance||d==distance&&id<best)))
                {best=id;bestClass=c;distance=d;}
            }
            return best;
        }
        private void AssertSelectors(int actor,NavPoint point,double range)
        {
            Assert.AreEqual(Reference(point,range,true),(int)Call("NearestDefenseTarget",point,PlayableOwner.Player,range));
            Assert.AreEqual(Reference(point,range,false),(int)Call("FollowDefenseTarget",Unit(actor),point,range));
        }
        [Test] public void CurrentOracleKeepsDistinctClassAndNumericTieRules()
        {
            var p=new NavPoint(-10,-20);int actor=Spawn(PlayableOwner.Player,p);
            int nearBuilding=Building(new NavPoint(-9,-20));int first=Spawn(PlayableOwner.Enemy,new NavPoint(-6,-20));
            Spawn(PlayableOwner.Enemy,new NavPoint(-14,-20));Call("RefreshVision");
            Assert.AreEqual(first,(int)Call("NearestDefenseTarget",p,PlayableOwner.Player,4d));
            Assert.AreEqual(nearBuilding,(int)Call("FollowDefenseTarget",Unit(actor),p,4d));
            AssertSelectors(actor,p,4);AssertSelectors(actor,p,0);AssertSelectors(actor,p,3.999999);
        }
        [Test] public void HiddenPendingAlliedAndRemovedTargetsStayIneligible()
        {
            var p=new NavPoint(-10,-20);int actor=Spawn(PlayableOwner.Player,p);
            int visible=Building(new NavPoint(-5,-20)),pending=Building(new NavPoint(-9,-20));
            Set(Rows("buildings")[pending],"Phase",ConstructionPhase.Pending);
            int hidden=Spawn(PlayableOwner.Enemy,new NavPoint(28,28));Spawn(PlayableOwner.Player,new NavPoint(-10,-17));
            int victim=Spawn(PlayableOwner.Enemy,new NavPoint(-8,-20));Call("RefreshVision");
            Assert.False((bool)Call("VisibleTarget",PlayableOwner.Player,hidden));AssertSelectors(actor,p,100);
            Call("Damage",victim,int.MaxValue);Call("RefreshVision");AssertSelectors(actor,p,100);
            Assert.AreEqual(visible,(int)Call("NearestDefenseTarget",p,PlayableOwner.Player,100d));
        }
        [Test] public void OutOfRangeHostileCandidateMatchesLegacyLazyVisionAndCapture()
        {
            const int seed=19261020;const string source="native-spatial-target-lazy-vision-v1";
            var p=new NavPoint(-10,-20);int fusedId=Spawn(PlayableOwner.Enemy,new NavPoint(-5,-20));
            var fusedDomain=domain;var legacyDomain=new PlayableDomain(PlayableProfile.Default,71,false);
            byte[] Capture(PlayableDomain value)=>value.CaptureWorldBytes(seed,source);
            try{
                domain=legacyDomain;int legacyId=Spawn(PlayableOwner.Enemy,new NavPoint(-5,-20));
                Assert.AreEqual(fusedId,legacyId,"Paired worlds must begin with the same target identity");
                byte[] legacyBefore=Capture(legacyDomain);
                int expected=Reference(p,.5d,true);
                byte[] legacyAfter=Capture(legacyDomain);
                var legacyVisions=(IDictionary)typeof(PlayableDomain).GetField("visions",Hidden).GetValue(legacyDomain);
                Assert.AreEqual(1,legacyVisions.Count,"The legacy VisibleTarget call creates the observer's lazy vision cache");

                domain=fusedDomain;
                var visions=(IDictionary)typeof(PlayableDomain).GetField("visions",Hidden).GetValue(fusedDomain);
                Assert.AreEqual(0,visions.Count,"Use a new observer/team so this exercises lazy cache creation");
                byte[] fusedBefore=Capture(fusedDomain);
                int actual=(int)Call("NearestDefenseTarget",p,PlayableOwner.Player,.5d);
                byte[] fusedAfter=Capture(fusedDomain);

                Assert.AreEqual(expected,actual,"Fused selector must match the independent legacy VisibleTarget→Target→Distance→ranking scan");
                Assert.AreEqual(0,actual,"The hostile candidate is outside the exact range");
                Assert.AreEqual(1,visions.Count,"The out-of-range hostile candidate still creates the observer's lazy vision cache");
                CollectionAssert.AreEqual(legacyBefore,fusedBefore,"Paired domains begin with identical exact current authority bytes");
                CollectionAssert.AreEqual(legacyAfter,fusedAfter,"Paired same-genesis worlds retain identical exact current authority bytes");
            }finally{
                domain=fusedDomain;legacyDomain.Navigation.ReleaseCommittedHostRoot();legacyDomain.DestroyOwnedNavigation();
            }
        }
        [Test] public void FollowBurstVictimInvalidationDoesNotReacquireInSameCall()
        {
            var p=new NavPoint(-10,-20);int actor=Spawn(PlayableOwner.Player,p,PlayableEntityKind.Explorer);
            int victim=Spawn(PlayableOwner.Enemy,new NavPoint(-6,-20)),near=Spawn(PlayableOwner.Enemy,new NavPoint(-8,-20));Call("RefreshVision");
            Set(Unit(actor),"BurstTarget",victim);Set(Unit(actor),"BurstRemaining",2);
            Assert.AreEqual(victim,(int)Call("FollowDefenseTarget",Unit(actor),p,5d));
            Call("Damage",victim,int.MaxValue);
            Assert.AreEqual(0,(int)Call("FollowDefenseTarget",Unit(actor),p,5d));
            Assert.AreEqual(near,(int)Call("FollowDefenseTarget",Unit(actor),p,5d));
        }
        [Test] public void RandomizedSmallWorldMatchesCurrentScanAcrossRangeAndVisibility()
        {
            var rng=new Random(19261019);int actor=Spawn(PlayableOwner.Player,new NavPoint(-10,-20));
            for(int i=0;i<48;i++){
                var kind=i%3==0?PlayableEntityKind.Explorer:PlayableEntityKind.Tank;double radius=PlayableUnitRules.Radius(PlayableProfile.Default,kind);
                NavPoint point;int attempts=0;
                do{Assert.Less(++attempts,10000);point=new NavPoint(rng.NextDouble()*60-30,rng.NextDouble()*50-25);}
                while(!domain.Geometry.IsFree(point,radius)||domain.Navigation.Crowd.Units.Any(u=>Distance(u.Position,point)<u.Radius+radius));
                Spawn(i%4==0?PlayableOwner.Player:PlayableOwner.Enemy,point,kind);
            }
            for(int i=0;i<8;i++)Building(new NavPoint(rng.NextDouble()*60-30,rng.NextDouble()*40-20));
            Call("RefreshVision");
            for(int i=0;i<80;i++)AssertSelectors(actor,new NavPoint(rng.NextDouble()*60-30,rng.NextDouble()*40-20),rng.NextDouble()*50);
        }
        [Test] public void SpatialBucketsAreConservativeWithoutCandidateTruncation()
        {
            var index=new PlayableTargetSpatialIndex();var output=new System.Collections.Generic.List<PlayableTargetSpatialIndex.Entry>();
            for(int i=0;i<1500;i++)index.Add(i+1,new NavPoint(1000+i*32,1000),false);
            for(int i=0;i<300;i++)index.Add(2000+i,new NavPoint((i%20)*.1,(i/20)*.1),i%2==0);
            index.Query(default(NavPoint),2,output);
            Assert.AreEqual(300,output.Count);Assert.AreEqual(300,index.VisitedEntries);
            Assert.AreEqual(300,output.Select(x=>x.Id).Distinct().Count());
            index.Clear();index.Add(77,new NavPoint(-16,-16),true);index.Query(new NavPoint(-20,-19),5,output);Assert.AreEqual(77,output.Single().Id);
            index.Add(88,new NavPoint(1e200,0),false);index.Query(default(NavPoint),double.PositiveInfinity,output);Assert.AreEqual(2,output.Count);
        }
        [Test] public void BroadPhaseNeverDropsExactRangeWinnerAtCellAndFloatingBoundaries()
        {
            var rng=new Random(19261020);var index=new PlayableTargetSpatialIndex();var output=new System.Collections.Generic.List<PlayableTargetSpatialIndex.Entry>();
            for(int n=0;n<180;n++){
                double offset=n%3==0?0:n%3==1?1e12:-1e12;
                var point=new NavPoint(offset+rng.Next(-100,100)*16,offset+rng.Next(-100,100)*16);
                double range=n%5==0?0:rng.NextDouble()*100;
                index.Clear();int id=1;
                foreach(double x in new[]{point.X-range,point.X,point.X+range})foreach(double z in new[]{point.Z-range,point.Z,point.Z+range})index.Add(id++,new NavPoint(x,z),false);
                index.Query(point,range,output);var ids=output.Select(x=>x.Id).ToArray();id=1;
                foreach(double x in new[]{point.X-range,point.X,point.X+range})foreach(double z in new[]{point.Z-range,point.Z,point.Z+range}){
                    if(Distance(point,new NavPoint(x,z))<=range)CollectionAssert.Contains(ids,id);id++;
                }
            }
        }
        [Test] public void ExplicitAttackKeepsItsVisibleVictimDespiteNearerAcquisitionCandidate()
        {
            var p=new NavPoint(-10,-20);int actor=Spawn(PlayableOwner.Player,p),victim=Spawn(PlayableOwner.Enemy,new NavPoint(-5,-20));
            Call("RefreshVision");Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(71,1,"player-1",PlayableCommandKind.Attack,new[]{actor},targetId:victim),out _));
            Spawn(PlayableOwner.Enemy,new NavPoint(-8,-20));Call("RefreshVision");Call("AdvanceIndexedCombat",1d/30);
            Assert.AreEqual(victim,(int)Unit(actor).GetType().GetField("Target").GetValue(Unit(actor)));
        }
        [Test,Timeout(120000)] public void OrdinaryThreadedAttackMoveAcquiresFiresAndResumesAfterTargetDeparture()
        {
            var c=OfflineParticipantAuthorityTests.Config(2,false,19261019);
            var costs=new double[c.Starts.Count,c.Starts.Count];
            for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)costs[i,j]=c.RouteCost(i,j);
            var origin=new NavPoint(-18,8);var enemyStart=new NavPoint(3,8);var goal=new NavPoint(18,8);
            var config=new OfflineMatchConfiguration(c.Profile,"native-spatial-target-small-world-v1","native-flat-eight-starts-v1","UnityHostRouteService",c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),costs,
                scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,origin),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,enemyStart)});
            using(var host=new Spacewars.Presentation.UnityHostRouteService())
            using(var runtime=new PlayableRuntime(config,81,startPaused:true))
            {
                void Service(){host.Service(runtime,c.Profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(host.CertifiedPublication?.Failure);}
                bool Until(Func<bool> condition,double seconds){var timer=System.Diagnostics.Stopwatch.StartNew();while(timer.Elapsed.TotalSeconds<seconds){Service();if(condition())return true;System.Threading.Thread.Sleep(1);}return false;}
                Assert.True(Until(()=>runtime.IsPrepared&&runtime.Latest.Paused,30),"Ordinary runtime prepared and paused");
                var initial=runtime.ParticipantView(c.Roster[0].Id);
                int actor=initial.Entities.Single(e=>e.Kind==PlayableEntityKind.Tank&&e.Owner==PlayableOwner.Player).Id;
                int enemy=runtime.ParticipantView(c.Roster[1].Id).Entities.Single(e=>e.Kind==PlayableEntityKind.Tank&&e.Owner==PlayableOwner.Enemy).Id;
                runtime.RequestPause(false);
                Assert.True(Until(()=>!runtime.Latest.Paused,5),"Human ingress requires the ordinary runtime to be resumed");
                Assert.True(runtime.TrySubmit(new PlayableCommand(81,1,c.Roster[1].Id,PlayableCommandKind.Hold,new[]{enemy}).AsHuman()).Accepted);
                Assert.True(runtime.TrySubmit(new PlayableCommand(81,1,c.Roster[0].Id,PlayableCommandKind.AttackMove,new[]{actor},goal).AsHuman()).Accepted);
                bool applied=false,moved=false,acquired=false,shot=false,retreat=false,resumed=false;
                long motionTick=-1,acquireTick=-1,shotTick=-1,resumeTick=-1;
                NavPoint stopped=origin;
                Assert.True(Until(()=>{
                    applied|=runtime.DrainReceipts().Any(r=>r.OwnerId==c.Roster[0].Id&&r.Sequence==1&&r.Status==PlayableCommandStatus.Applied);
                    var view=runtime.ParticipantView(c.Roster[0].Id);var unit=view.Entities.SingleOrDefault(e=>e.Id==actor);
                    Assert.NotNull(unit,"Actor must survive this small acquisition witness");
                    if(!moved&&Distance(unit.Position,origin)>.05){moved=true;motionTick=view.Tick;}
                    if(unit.TargetId==enemy){if(!acquired)acquireTick=view.Tick;acquired=true;}
                    if(acquired&&view.Projectiles.Any(p=>p.OwnerId==actor&&p.TargetId==enemy)){
                        if(!shot){shotTick=view.Tick;stopped=unit.Position;}shot=true;
                        if(!retreat){Assert.True(runtime.TrySubmit(new PlayableCommand(81,2,c.Roster[1].Id,PlayableCommandKind.Move,new[]{enemy},new NavPoint(3,27)).AsHuman()).Accepted);retreat=true;}
                    }
                    if(shot&&unit.TargetId==0&&unit.Moving&&Distance(unit.Position,stopped)>.2){resumed=true;resumeTick=view.Tick;}
                    return applied&&moved&&acquired&&shot&&resumed;
                },60),"AttackMove must move, acquire/fire and resume its original route after enemy departure");
                Assert.Greater(acquireTick,motionTick);Assert.GreaterOrEqual(shotTick,acquireTick);Assert.Greater(resumeTick,shotTick);
                var result=runtime.ParticipantView(c.Roster[0].Id).Entities.Single(e=>e.Id==actor);
                Assert.AreEqual(PlayableTacticalOrderKind.AttackMove,result.CurrentOrder.Kind);Assert.AreEqual(goal,result.CurrentOrder.Destination);
                TestContext.WriteLine("ordinary target witness: motion="+motionTick+" acquire="+acquireTick+" shot="+shotTick+" resume="+resumeTick);
            }
        }
        [Test] public void CurrentCaptureAndContinuationIgnoreHotOrColdTargetBuckets()
        {
            const int seed=19261019;const string source="native-spatial-target-cache-v1";
            var p=new NavPoint(-10,-20);int actor=Spawn(PlayableOwner.Player,p),enemy=Spawn(PlayableOwner.Enemy,new NavPoint(-4,-20));
            foreach(var row in Rows("units").Values.Cast<object>())Set(row,"Reload",999d);
            Call("RefreshVision");
            Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(71,1,"player-1",PlayableCommandKind.Hold,new[]{actor}),out _));
            Assert.NotNull(typeof(NavigationSession).GetField("groupCommandBacking",Hidden).GetValue(domain.Navigation),"Capture a current owned command authority, not pre-host managed bootstrap tables");
            byte[] cold=domain.CaptureWorldBytes(seed,source);
            AssertSelectors(actor,p,10);byte[] hot=domain.CaptureWorldBytes(seed,source);
            CollectionAssert.AreEqual(cold,hot,"Warming a derived query must not change current capture bytes");
            var restored=PlayableDomain.RestoreWorldBytes(hot,PlayableProfile.Default,seed,source);
            var indexField=typeof(PlayableDomain).GetField("targetSpatialIndex",Hidden);
            var restoredIndex=(PlayableTargetSpatialIndex)indexField.GetValue(restored);
            Assert.AreEqual(0,restoredIndex.Candidates.Count,"Cold restore must not serialize spatial buckets");
            var kernels=new[]{new CertifiedRouteKernel(),new CertifiedRouteKernel()};
            var worlds=new[]{domain,restored};
            try{
                Assert.AreEqual(PlayableWorldRestoreTests.Facts(domain,true),PlayableWorldRestoreTests.Facts(restored,true));
                foreach(var world in worlds)Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(71,2,"player-1",PlayableCommandKind.Move,new[]{actor},new NavPoint(-10,-5)),out _));
                bool moved=false,lostVisibility=false;int born=0;
                for(int tick=0;tick<150;tick++){
                    // One side retains its predecessor-phase buckets; the other starts cold.
                    restoredIndex.Clear();restoredIndex.Candidates.Clear();
                    for(int i=0;i<worlds.Length;i++){
                        var world=worlds[i];
                        if(tick==20){
                            int id=(int)typeof(PlayableDomain).GetMethod("SpawnUnit",Hidden).Invoke(world,new object[]{new NavPoint(-9,-20),PlayableOwner.Enemy,PlayableEntityKind.Explorer});
                            if(i==0)born=id;else Assert.AreEqual(born,id);
                            var rows=(IDictionary)typeof(PlayableDomain).GetField("units",Hidden).GetValue(world);Set(rows[id],"Reload",999d);
                        }
                        if(tick==40)typeof(PlayableDomain).GetMethod("Damage",Hidden).Invoke(world,new object[]{born,int.MaxValue});
                        var nav=world.Navigation;Assert.True(nav.PrepareDeliveryBarrier());
                        var binding=new PlayableRouteBinding(world.Geometry,PlayableProfile.Default,nav.Admission,nav.RoutePort);
                        for(int work=0;work<20000&&!nav.DeliveryBarrierReady;work++)kernels[i].Pump(binding,64);
                        Assert.True(nav.DeliveryBarrierReady);nav.ApplyResults();world.Step(1d/30);
                    }
                    domain.Navigation.Crowd.TryGet(actor,out var body);moved|=Distance(body.Position,p)>.1;
                    lostVisibility|=!(bool)Call("VisibleTarget",PlayableOwner.Player,enemy);
                    Assert.AreEqual(PlayableWorldRestoreTests.Facts(domain,true),PlayableWorldRestoreTests.Facts(restored,true),"exact current authority tick "+tick);
                    var point=body.Position;
                    int a=(int)typeof(PlayableDomain).GetMethod("NearestDefenseTarget",Hidden).Invoke(domain,new object[]{point,PlayableOwner.Player,10d});
                    int b=(int)typeof(PlayableDomain).GetMethod("NearestDefenseTarget",Hidden).Invoke(restored,new object[]{point,PlayableOwner.Player,10d});
                    Assert.AreEqual(Reference(point,10,true),a);Assert.AreEqual(a,b);
                }
                Assert.True(moved);Assert.True(lostVisibility,"Exercise live movement and fog transition, not just accepted intent");
                CollectionAssert.AreEqual(domain.CaptureWorldBytes(seed,source),restored.CaptureWorldBytes(seed,source));
            }finally{foreach(var kernel in kernels)kernel.Clear();restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }
        [Test] public void LiveArrivalRecordOracleKeepsExactOwnersAliasesAndMutablePayload()
        {
            int actor=Spawn(PlayableOwner.Player,new NavPoint(-10,-20));
            Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(71,1,"player-1",PlayableCommandKind.Move,new[]{actor},new NavPoint(-10,-5)),out _));
            var nav=domain.Navigation;var kernel=new CertifiedRouteKernel();
            try{
                kernel.PauseAtPhysicalBoundary=()=>nav.ArrivalPhases.Length!=0;
                try{kernel.Pump(new PlayableRouteBinding(domain.Geometry,PlayableProfile.Default,nav.Admission,nav.RoutePort),1);}finally{kernel.PauseAtPhysicalBoundary=null;}
                var attempts=(System.Collections.Generic.IDictionary<long,NavigationSession.ArrivalAttempt>)typeof(NavigationSession).GetField("arrivalAttempts",Hidden).GetValue(nav);
                var attempt=attempts.Values.Single();Assert.NotNull(attempt.StorageOwner);
                var budget=nav.PhysicalHostBudget;Assert.True(budget.TryOpenScope(NavigationPreparationKind.Arrival,71,1,out var scope));
                NavigationSession.NavigationLiveArrivalProvider one=null,two=null;NavigationSession.NavigationLiveArrivalLease lease=null;
                try{
                    var r=scope.OwnedReservation();var provider=attempt.OwnedProviders[0];
                    one=NavigationSession.NavigationLiveArrivalProvider.CreateOwned(1,1,provider,r);two=NavigationSession.NavigationLiveArrivalProvider.CreateOwned(1,1,provider,r);
                    var request=attempt.Intents[0];var data=new NavigationArrivalLeaseValues(1,1,0,nav.Generation,attempt.Id,request.Request,request.Entity,request.MemberIdentity.Incarnation,request.Order,1,false);
                    lease=NavigationSession.NavigationLiveArrivalLease.CreateOwned(data,nav,provider,r);
                    byte[] Facts(object value){var cached=PlayableWorldRestoreTests.FactBytes(value,true,true);CollectionAssert.AreEqual(cached,PlayableWorldRestoreTests.FactBytes(value,true,false));return cached;}
                    foreach(var record in new object[]{attempt,one.Record,lease.Record}){
                        Facts(record);var clone=typeof(object).GetMethod("MemberwiseClone",Hidden).Invoke(record,null);
                        Assert.Throws<AssertionException>(()=>Facts(clone),"Same-valued fake record must not borrow the canonical owner");
                        var owner=((INavStorageFacade)record).StorageRoot;var refs=typeof(NavOwnedStorage).GetField("references",Hidden);int prior=owner.References;
                        try{refs.SetValue(owner,0);Assert.Throws<AssertionException>(()=>Facts(record));}finally{refs.SetValue(owner,prior);}
                    }
                    CollectionAssert.AreNotEqual(Facts(new object[]{one.Record,one.Record}),Facts(new object[]{one.Record,two.Record}),"Equal records with distinct owners retain distinct aliases");
                    var before=Facts(attempt);long epoch=attempt.Epoch;try{attempt.Epoch++;CollectionAssert.AreNotEqual(before,Facts(attempt));}finally{attempt.Epoch=epoch;}
                    before=Facts(one.Record);int count=one.Record.References;try{one.Record.References++;CollectionAssert.AreNotEqual(before,Facts(one.Record));}finally{one.Record.References=count;}
                    before=Facts(lease.Record);long retirement=lease.Record.Retirement;try{lease.Record.Retirement++;CollectionAssert.AreNotEqual(before,Facts(lease.Record));}finally{lease.Record.Retirement=retirement;}
                    before=Facts(one);int revision=one.Revision;try{one.Revision++;CollectionAssert.AreNotEqual(before,Facts(one));}finally{one.Revision=revision;}
                }finally{lease?.ReleaseStorage();one?.ReleaseStorage();two?.ReleaseStorage();scope.Discard();}
                Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }finally{kernel.Clear();}
        }
        [Test] public void OrdinarySelectorDoesNotSkipItsBlockedWinnerForAnotherVisibleTarget()
        {
            var p=new NavPoint(-10,-20);int actor=Spawn(PlayableOwner.Player,p);
            Spawn(PlayableOwner.Player,new NavPoint(10,-17));Spawn(PlayableOwner.Player,new NavPoint(-10,-8));
            int blocked=Spawn(PlayableOwner.Enemy,new NavPoint(5,-20));Spawn(PlayableOwner.Enemy,new NavPoint(-10,-4));Call("RefreshVision");
            AssertSelectors(actor,p,20);
            Assert.AreEqual(blocked,(int)Call("NearestDefenseTarget",p,PlayableOwner.Player,20d));
            Assert.False((bool)Call("GroundFireClear",p,new NavPoint(5,-20),PlayableProfile.Default.TankProjectileCollisionRadius));
        }
    }
}
