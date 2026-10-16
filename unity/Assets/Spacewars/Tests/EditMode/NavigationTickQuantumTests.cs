using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationTickQuantumTests
    {
        private static PlayableAuthorityTick Authority(bool async=false)=>new PlayableAuthorityTick(OfflineParticipantAuthorityTests.Config(2),71,asynchronousRoutes:async);
        private static void Request(PlayableAuthorityTick a){var n=a.Domain.Navigation;var actor=n.Crowd.Units.First();Assert.True(n.Move(actor.Id,new NavPoint(actor.Position.X+3,actor.Position.Z)));}
        private static NavigationHostRoot Root(PlayableAuthorityTick a)=>a.Domain.Navigation.PinCommittedHostRoot();
        private static void Finish(PlayableAuthorityTick a,CertifiedRouteKernel kernel,int chunk){
            kernel.PhysicalOperationsPerPump=chunk;
            for(int i=0;i<10000;i++){
                using(var pin=new RootPin(Root(a))){if(pin.Value.QuantumCompleted)return;}
                kernel.Pump(a.NavigationBinding,99);
            }
            Assert.Fail("quantum never completed");
        }
        private sealed class RootPin:IDisposable{internal NavigationHostRoot Value;internal RootPin(NavigationHostRoot value){Value=value;}public void Dispose()=>Value?.Release();}
        private static void Cleanup(PlayableAuthorityTick a,CertifiedRouteKernel k){k.Clear();a.Stop();var n=a.Domain.Navigation;n.DestroyNavigationAuthority();Assert.Zero(n.PhysicalHostBudget.PhysicalBytes);}
        [Test] public void PhysicalQuantumEndUsesOneSessionLedgerAndSeparateOrdinaryHostAllowance(){
            var a=Authority();var k=new CertifiedRouteKernel();
            try{
                Request(a);Assert.False(a.TryAdvance());Finish(a,k,1);var n=a.Domain.Navigation;
                using(var p=new RootPin(Root(a)))Assert.AreEqual(p.Value.Scheduler.RetainedBytes,n.HostRetainedBytes);
                var budget=n.PhysicalHostBudget;Assert.Greater(budget.RetainedBytes,n.HostRetainedBytes);Assert.LessOrEqual(budget.RetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                long live=budget.PhysicalBytes;Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,71,n.Admission.PublicationIdentity,out var scope));using(scope)Assert.False(scope.TryReserve(LayeredNavigationScheduler.MaxRetainedBytes),"outer Session cap still refuses credits beyond the unique-owner union");Assert.AreEqual(live,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
            }finally{Cleanup(a,k);}
        }
        [Test] public void RefusedAllocationFramePublicationReusesTheAlreadyOwnedToken(){
            var a=Authority();var k=new CertifiedRouteKernel();
            try{
                Request(a);Assert.False(a.TryAdvance());k.PhysicalOperationsPerPump=1;
                for(int i=0;i<100;i++){using(var p=new RootPin(Root(a))){if(p.Value.Frontier.Stage==CertifiedRouteKernel.PumpStage.Allocation)break;}k.Pump(a.NavigationBinding,4);}
                using(var p=new RootPin(Root(a)))Assert.AreEqual(CertifiedRouteKernel.PumpStage.Allocation,p.Value.Frontier.Stage);
                var budget=a.Domain.Navigation.PhysicalHostBudget;budget.AllocationAdmission=bytes=>bytes!=NavigationRetentionBudget.ScopeBytes;
                try{k.Pump(a.NavigationBinding,4);}finally{budget.AllocationAdmission=null;}
                Finish(a,k,1);Assert.True(a.TryAdvance());
            }finally{Cleanup(a,k);}
        }
        [TestCase(false)][TestCase(true)] public void ProbeOnlyAuthorityIssuesAndCompletesItsOwnGrant(bool asynchronous){
            var a=Authority(asynchronous);var k=new CertifiedRouteKernel();
            try{
                var n=a.Domain.Navigation;var actor=n.Crowd.Units.First();var request=n.Probe(999,1,PlayableProfile.Default.Navigation,actor.Position,new NavPoint(actor.Position.X+3,actor.Position.Z));Assert.NotNull(request);
                bool ready=false;
                for(int i=0;i<4096&&!ready;i++){
                    bool advanced=a.TryAdvance();if(n.QuantumRunnable)Finish(a,k,3);
                    using(var p=new RootPin(Root(a)))foreach(var entry in p.Value.Delivered){var row=(NavigationHostRow)entry.Value;if(row.Request.Request==request.Request){Assert.AreEqual(NavSolveStatus.Ready,row.Answer.Status);Assert.AreEqual(request.Goal,row.Answer.ReadOnlyRouteBacking.Last());ready=true;}}
                    if(!advanced)Assert.True(a.TryAdvance());
                }
                Assert.True(ready,"probe-only work must not sleep without an authority grant");if(n.OwnedProbeAnswerPublications==0&&!a.TryAdvance()){Finish(a,k,3);Assert.True(a.TryAdvance());}Assert.AreEqual(1,n.OwnedProbeAnswerPublications);Assert.Zero(n.RejectedResults);Assert.Zero(n.PendingCount);
                using(var p=new RootPin(Root(a)))Assert.Greater(p.Value.QuantumSerial,0);
            }finally{Cleanup(a,k);}
        }
        [TestCase("tick")][TestCase("prefix")] public void SavedQuantumMustBindToWorldTickAndRequestPrefix(string defect){
            var config=OfflineParticipantAuthorityTests.Config(2);var a=Authority();var k=new CertifiedRouteKernel();
            try{
                Request(a);Assert.False(a.TryAdvance());
                using(var p=new RootPin(Root(a))){var field=typeof(NavigationHostRoot).GetField(defect=="tick"?"<QuantumTick>k__BackingField":"<AdmissionPrefix>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);field.SetValue(p.Value,999999L);}
                var bytes=a.CaptureBytes();Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(bytes,config));
            }finally{Cleanup(a,k);}
        }
        [Test] public void ActiveArrivalProjectionLeaseDoesNotSuspendIndependentOwnedMovement(){
            var config=OfflineParticipantAuthorityTests.Config(2);var a=Authority(true);var n=a.Domain.Navigation;var actor=n.Crowd.Units.First();var other=n.Crowd.Units.Last();var k=new CertifiedRouteKernel{PhysicalOperationsPerPump=1};
            try{
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,config.Roster[0].Id,PlayableCommandKind.Move,new[]{actor.Id},new NavPoint(actor.Position.X+3,actor.Position.Z)).AsHuman()).Status);Assert.True(a.TryAdvance());
                bool projection=false;
                for(int i=0;i<4096;i++){
                    bool complete;using(var p=new RootPin(Root(a))){projection=p.Value.Frontier.Stage==CertifiedRouteKernel.PumpStage.Projection&&p.Value.Rows.Any(row=>((NavigationHostRow)row.Value).ArrivalToken!=0);complete=p.Value.QuantumCompleted;}
                    if(projection)break;if(complete)Assert.True(a.TryAdvance());k.Pump(a.NavigationBinding,4);
                }
                Assert.True(projection);Assert.True(n.IsPending(actor.Id));var start=other.Position;
                var goal=new NavPoint(start.X+3,start.Z);Assert.True(n.Crowd.SetRoute(other.Id,goal,new[]{goal}));long publications=n.OwnedMovementPublications;
                for(int i=0;i<30;i++)Assert.True(a.TryAdvance());Assert.True(n.IsPending(actor.Id));Assert.AreNotEqual(start,other.Position);Assert.Greater(n.OwnedMovementPublications,publications);
            }finally{Cleanup(a,k);}
        }
        [Test] public void KnownV1FixedPolicyAndStandaloneManualPumpNeverRequireTickGrant(){
            var config=OfflineParticipantAuthorityTests.Config(2);var a=Authority();var k=new CertifiedRouteKernel();PlayableAuthorityTick restored=null;CertifiedRouteKernel fresh=null;
            try{
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                typeof(PlayableAuthorityTick).GetField("tickQuanta",flags).SetValue(a,false);
                typeof(PlayableAuthorityTick).GetMethod("SetRouteDeliveryPolicy",flags).Invoke(a,new object[]{false});
                Request(a);Assert.False(a.TryAdvance());Assert.AreEqual(PlayableAuthorityTick.LegacyRouteDeliveryPolicy,a.EffectiveRouteDeliveryPolicy);
                var bytes=a.CaptureBytes();restored=PlayableAuthorityTick.RestoreBytes(bytes,config);fresh=new CertifiedRouteKernel();
                CollectionAssert.AreEqual(bytes,restored.CaptureBytes());Assert.AreEqual(PlayableAuthorityTick.LegacyRouteDeliveryPolicy,restored.EffectiveRouteDeliveryPolicy);Assert.False(restored.Domain.Navigation.TickQuantaEnabled);
                for(int i=0;i<4096&&restored.Domain.Navigation.PendingCount>0;i++){fresh.Pump(restored.NavigationBinding,4);if(restored.TryAdvance())break;}
                Assert.Greater(restored.Tick,0);Assert.Zero(restored.Domain.Navigation.PendingCount);
                using(var root=new RootPin(Root(restored)))Assert.False(root.Value.TickQuantum,"known HostRoot BODY stays legacy");
            }finally{if(restored!=null)Cleanup(restored,fresh??new CertifiedRouteKernel());Cleanup(a,k);}
        }
        private static string Gameplay(PlayableAuthorityTick a){
            var s=a.Latest;return a.Tick+"|"+s.Credits+"|"+string.Join(";",a.Domain.Navigation.Crowd.Units.Select(u=>u.Id+":"+u.Position.X.ToString("R")+":"+u.Position.Z.ToString("R")+":"+u.Heading.ToString("R")+":"+u.Goal.X.ToString("R")+":"+u.Goal.Z.ToString("R")+":"+u.GoalLocation?.SurfaceId+":"+u.Location.SurfaceId+":"+u.Moving+":"+u.Outcome+":"+u.InstalledRequest+":"+u.InstalledOrder))+"|"+string.Join(";",s.Buildings.Select(b=>b.Id+":"+b.Health+":"+b.Kind));
        }
        private static string ArrivalGameplay(PlayableAuthorityTick a){
            using(var p=new RootPin(Root(a)))return string.Join(";",p.Value.ArrivalWork.Select(entry=>{var work=(NavigationArrivalWork)entry.Value;var c=work.RouteCounters;return entry.Key+":"+work.Status+":"+work.WorkPhase+":"+work.CandidateCursor+":"+work.MemberCursor+":"+c.LogicalGraphWork+":"+c.LogicalFieldWork+":"+c.ConnectorWork;}));
        }
        private static void EvictCache(PlayableAuthorityTick a,bool individual){
            var n=a.Domain.Navigation;using(var pin=new RootPin(Root(a))){
                Assert.True(n.TryOpenPhysicalHostScope(NavigationPreparationKind.Solve,71,n.Admission.PublicationIdentity,out var scope));
                using(scope){var next=pin.Value.ForkOwned(scope.OwnedReservation());try{
                    var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                    if(individual){var evict=typeof(LayeredNavigationScheduler).GetMethod("EvictOne",flags);while((bool)evict.Invoke(next.Scheduler,null)){} }
                    else typeof(LayeredNavigationScheduler).GetMethod("ClearCache",flags).Invoke(next.Scheduler,null);
                    next.SealPrepared();scope.Attach(next);n.PublishPhysicalHostRoot(scope,pin.Value,next,()=>{});
                }finally{next.ReleaseStorage();}}
            }
        }
        [TestCase(false)][TestCase(true)] public void FixedActualAuthorityColdWarmEvictedAndChunkCadenceHaveSameGameplayAndGrants(bool group)=>RunFixedCacheMatrix(group,group,k3:group);
        [Test,Explicit("Large flat field activation remains unqualified at the original 4096-tick bound; retain its assertions as a separate diagnostic, not an A5-b PASS.")]
        public void LargeFlatGroupFieldActivationDiagnostic()=>RunFixedCacheMatrix(true,false);
        private static OfflineMatchConfiguration AuthoredConfiguration(){
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=19092026};setup.InitializeParticipants();foreach(var p in setup.Participants)p.Human=true;
            return NativeLobbyMatch.Create(PlayableProfile.ThreeCrossingsDefault,setup);
        }
        [Test,Explicit("Original authored K8 remains unqualified at4096; preserved diagnostic, not K3 PASS.")]
        public void AuthoredK8FieldActivationDiagnostic()=>RunFixedCacheMatrix(true,true);
        [Test] public void AuthoredSharedCohortPreflight()=>RunFixedCacheMatrix(true,true,true,true);
        private static void RunFixedCacheMatrix(bool group,bool authored,bool preflightOnly=false,bool k3=false){
            var config=authored?AuthoredConfiguration():OfflineParticipantAuthorityTests.Config(2);var prime=new PlayableAuthorityTick(config,71);var kernel=new CertifiedRouteKernel();byte[] saved=null;string primeProvider=null;NavLocation primeOrigin=default;
            try{
                var actor=prime.Domain.Navigation.Crowd.Units.First();var home=prime.Latest.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
                var goal=new NavPoint(2*home.Position.X-actor.Position.X,2*home.Position.Z-actor.Position.Z);
                Assert.True(prime.Domain.Navigation.Move(actor.Id,goal));
                for(int i=0;i<4096&&prime.Domain.Navigation.PendingCount>0;i++){if(!prime.TryAdvance()){Finish(prime,kernel,int.MaxValue);Assert.True(prime.TryAdvance());}}
                Assert.Zero(prime.Domain.Navigation.PendingCount);Assert.Greater(kernel.Counters.LogicalGraphWork,0,"fixture must exercise graph cache, not a direct route");
                if(k3){
                    for(int i=0;i<4096&&actor.Moving;i++){if(!prime.TryAdvance()){Finish(prime,kernel,int.MaxValue);Assert.True(prime.TryAdvance());}}
                    Assert.False(actor.Moving,"K3 preparation requires actual stopped arrival");Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome);
                    Assert.AreEqual(new NavPoint(-56,-46),actor.Goal);double tolerance=prime.Domain.Navigation.Crowd.CaptureProfile.ArrivalTolerance;
                    double dx=actor.Position.X-actor.Goal.X,dz=actor.Position.Z-actor.Goal.Z;Assert.LessOrEqual(Math.Sqrt(dx*dx+dz*dz),tolerance);
                    primeOrigin=actor.Location;TestContext.WriteLine("K3 stopped actual="+actor.Position.X.ToString("R")+","+actor.Position.Z.ToString("R")+" goal="+actor.Goal.X+","+actor.Goal.Z+" outcome="+actor.Outcome+" tolerance="+tolerance);
                    prime.Domain.Navigation.Stop(actor.Id,false);Assert.AreEqual(primeOrigin,actor.Location,"ordinary Stop preserves actual pose");
                    using(var pin=new RootPin(Root(prime))){var graph=(LayeredNavigationGraph)typeof(LayeredNavigationScheduler).GetProperty("cachedGraph",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(pin.Value.Scheduler);
                        Assert.NotNull(graph,"reuse the actual priming graph for finite preflight");K3GraphPreflight(graph,primeOrigin);primeProvider=graph.PhysicalProvider.Binding;}
                }
                saved=prime.CaptureBytes();
            }finally{Cleanup(prime,kernel);}
            var worlds=new[]{PlayableAuthorityTick.RestoreBytes(saved,config),PlayableAuthorityTick.RestoreBytes(saved,config),PlayableAuthorityTick.RestoreBytes(saved,config)};
            var kernels=new[]{new CertifiedRouteKernel(),new CertifiedRouteKernel(),new CertifiedRouteKernel()};Exception original=null;var receipts=new string[3];
            try{
                EvictCache(worlds[0],false);EvictCache(worlds[2],true);
                for(int i=0;i<3;i++){
                    var actor=worlds[i].Domain.Navigation.Crowd.Units.First();if(k3)Assert.AreEqual(primeOrigin,actor.Location,"capture preserves actual priming pose");
                    if(!group)Assert.True(worlds[i].Domain.Navigation.Move(actor.Id,actor.Goal));
                    else{
                        var spawn=worlds[i].Domain.GetType().GetMethod("SpawnUnit",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                        int member=(int)spawn.Invoke(worlds[i].Domain,new object[]{k3?new NavPoint(-56,-42):new NavPoint(actor.Position.X-(authored?2:0),actor.Position.Z+(authored?0:2)),PlayableOwner.Player,PlayableEntityKind.Explorer});
                        var n=worlds[i].Domain.Navigation;var goal=actor.Goal;
                        if(authored){
                            // K3 uses the source-derived three-candidate first page.
                            // The old K8 diagnostic retains its original preparation.
                            var home=worlds[i].Latest.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
                            goal=k3?new NavPoint(-35.4,-46):new NavPoint((goal.X+home.Position.X)/2,(goal.Z+home.Position.Z)/2);
                            // Priming moved A's mobility revision. The new member
                            // must enter the same real clearance cohort before
                            // this shared-artifact witness; never rewrite keys.
                            n.Stop(actor.Id,false);Assert.True(n.Crowd.TryGet(member,out var peer));
                            while(peer.MobilityRevision<actor.MobilityRevision)n.Stop(member,false);
                            Assert.AreEqual(actor.MobilityRevision,peer.MobilityRevision);
                        }
                        var receipt=worlds[i].Apply(new PlayableCommand(71,1,config.Roster[0].Id,PlayableCommandKind.AttackMove,new[]{actor.Id,member},goal).AsHuman());
                        Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status);Assert.AreEqual(1,receipt.Sequence);Assert.AreEqual(worlds[0].Tick,receipt.AppliedTick);Assert.AreEqual(config.Roster[0].Id,receipt.OwnerId);receipts[i]=receipt.Sequence+":"+receipt.AppliedTick+":"+receipt.Status+":"+receipt.OwnerId+":"+receipt.Message;
                        if(k3){var actors=(System.Collections.IDictionary)worlds[i].Domain.GetType().GetField("units",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(worlds[i].Domain);Assert.True(actors.Contains(actor.Id));Assert.True(actors.Contains(member));}
                        if(authored){var left=n.PendingRequestFor(actor.Id);var right=n.PendingRequestFor(member);Assert.AreEqual(left.MemberIdentity.MobilityRevision,right.MemberIdentity.MobilityRevision);
                            TestContext.WriteLine("cohort left="+left.Profile.Radius+":"+left.Profile.TankSpeed+":"+left.Profile.TankTurnSpeed+":"+left.Profile.ProfileId+":"+left.Profile.Revision+" right="+right.Profile.Radius+":"+right.Profile.TankSpeed+":"+right.Profile.TankTurnSpeed+":"+right.Profile.ProfileId+":"+right.Profile.Revision+" hold="+left.HeldIdentity+"/"+right.HeldIdentity);
                            Assert.AreEqual(NavigationAdmission.ExactProfileBinding(left.Profile),NavigationAdmission.ExactProfileBinding(right.Profile),"shared fixture requires the same complete request profile");Assert.AreEqual(left.HeldIdentity,right.HeldIdentity);
                        }
                    }
                }
                if(k3){Assert.AreEqual(receipts[0],receipts[1]);Assert.AreEqual(receipts[0],receipts[2]);for(int i=0;i<3;i++){
                    Assert.False(worlds[i].TryAdvance());Finish(worlds[i],kernels[i],i==1?1:3);
                    using(var p=new RootPin(Root(worlds[i]))){var work=(NavigationArrivalWork)p.Value.ArrivalWork.Single().Value;K3InputPreflight(work,primeProvider,primeOrigin,worlds[i].Domain.Navigation.Crowd.Units.First().Id);}
                }if(preflightOnly)return;}
                bool pendingAcrossTicks=false;var activation=new[]{-1L,-1L,-1L};
                for(int tick=0;tick<4096;tick++){
                    for(int i=0;i<3;i++){
                        bool stepped=worlds[i].TryAdvance();
                        if(!stepped){for(int poll=0;poll<(i+1)*3;poll++)Assert.False(worlds[i].TryAdvance());Finish(worlds[i],kernels[i],i==1?1:3);Assert.True(worlds[i].TryAdvance());}
                        if(activation[i]<0&&worlds[i].Domain.Navigation.PendingCount==0)activation[i]=worlds[i].Tick;
                    }
                    Assert.AreEqual(Gameplay(worlds[0]),Gameplay(worlds[1]),"warm tick "+tick);Assert.AreEqual(Gameplay(worlds[0]),Gameplay(worlds[2]),"evicted tick "+tick);
                    if(group){Assert.AreEqual(ArrivalGameplay(worlds[0]),ArrivalGameplay(worlds[1]),"warm shared attempt at tick "+tick);Assert.AreEqual(ArrivalGameplay(worlds[0]),ArrivalGameplay(worlds[2]),"evicted shared attempt at tick "+tick);}
                    using(var x=new RootPin(Root(worlds[0])))for(int i=1;i<3;i++)using(var y=new RootPin(Root(worlds[i]))){Assert.AreEqual(x.Value.QuantumTick,y.Value.QuantumTick);Assert.AreEqual(x.Value.QuantumSerial,y.Value.QuantumSerial);Assert.AreEqual(x.Value.ConsumedQuantum,y.Value.ConsumedQuantum);Assert.AreEqual(x.Value.Scheduler.Counters.LogicalGraphWork,y.Value.Scheduler.Counters.LogicalGraphWork);Assert.AreEqual(x.Value.Scheduler.Counters.LogicalFieldWork,y.Value.Scheduler.Counters.LogicalFieldWork);Assert.AreEqual(x.Value.Scheduler.Counters.ConnectorWork,y.Value.Scheduler.Counters.ConnectorWork);}
                    pendingAcrossTicks|=worlds[0].Tick>1&&worlds[0].Domain.Navigation.PendingCount>0;
                    if(tick>=119&&(!group||activation.All(value=>value>=0)))break;
                }
                Assert.True(pendingAcrossTicks,"fixed must advance complete world ticks while route remains pending");
                TestContext.WriteLine("Fixed group="+group+" authored="+authored+" k3="+k3+" tick="+worlds[0].Tick+" pending="+worlds[0].Domain.Navigation.PendingCount+" arrival="+ArrivalGameplay(worlds[0]));
                if(!group){Assert.Greater(kernels[1].Counters.CacheHits,kernels[0].Counters.CacheHits,"warm measured path must actually reuse physical cache");Assert.Less(kernels[1].Counters.PhysicalGraphWork,kernels[0].Counters.PhysicalGraphWork);}
                else{Assert.Greater(activation[0],0,"both current group routes must activate");Assert.AreEqual(activation[0],activation[1]);Assert.AreEqual(activation[0],activation[2]);Assert.AreEqual(3,worlds[0].Domain.Navigation.AppliedResults);using(var p=new RootPin(Root(worlds[1]))){Assert.Greater(p.Value.ArrivalWork.Count,0,"actual multi-member arrival scheduler is retained");foreach(var entry in p.Value.ArrivalWork){var work=(NavigationArrivalWork)entry.Value;Assert.Greater(work.RouteCounters.LogicalGraphWork,0);Assert.Greater(work.RouteCounters.LogicalFieldWork,0);Assert.Greater(work.RouteCounters.CacheHits,0,"shared attempt reuse must be exercised; builds="+work.RouteCounters.GraphBuilds+"/"+work.RouteCounters.FieldBuilds);if(authored){Assert.AreEqual(1,work.RouteCounters.GraphBuilds);Assert.AreEqual(1,work.RouteCounters.FieldBuilds);}}}}
            }catch(Exception ex){original=ex;TestContext.WriteLine("primary failure: "+ex);for(int i=0;i<3;i++){var n=worlds[i].Domain.Navigation;var b=n.PhysicalHostBudget;using(var p=new RootPin(Root(worlds[i])))TestContext.WriteLine("retention world="+i+" tick="+worlds[i].Tick+" physical="+b.PhysicalBytes+" held="+b.HeldBytes+" peak="+b.PeakRetainedBytes+" ordinary="+p.Value.Scheduler.RetainedBytes+" cache="+p.Value.Scheduler.Counters.CacheBytes+" arrival="+n.ArrivalRetentionDiagnostic); }throw;}finally{for(int i=0;i<3;i++)try{Cleanup(worlds[i],kernels[i]);}catch(Exception cleanup){if(original==null)throw;TestContext.WriteLine("cleanup failure world "+i+": "+cleanup);}}
        }
        private static void K3GraphPreflight(LayeredNavigationGraph graph,NavLocation actualOrigin){
            Assert.LessOrEqual(graph.Nodes.Count,14883);Assert.AreEqual(.8,graph.Binding.Profile.GridCell);Assert.AreEqual(.58,graph.Binding.Clearance.Radius);
            int maxDegree=0,maxTrace=0;double minCost=double.PositiveInfinity;
            foreach(var node in graph.Nodes){var arcs=graph.Outgoing(node.Id);maxDegree=Math.Max(maxDegree,arcs.Count);foreach(var arc in arcs){minCost=Math.Min(minCost,arc.Cost);maxTrace=Math.Max(maxTrace,arc.Trace?.Count??1);}}
            Assert.LessOrEqual(maxDegree,4);Assert.GreaterOrEqual(minCost,.8-1e-8);Assert.LessOrEqual(maxTrace,3);
            foreach(var origin in new[]{actualOrigin,new NavLocation(new NavPoint(-56,-42),"deck/-1/-1")})foreach(int endpoint in new[]{0,1}){
                double nodeZ=origin.Equals(actualOrigin)?-46.4:-42.4;var target=new NavLocation(endpoint==0?new NavPoint(-35.4,-46):new NavPoint(-39,-49.6),"deck/-1/-1");
                Func<double,double,NavGraphNode> nodeAt=(x,z)=>graph.Nodes.Single(n=>n.Location.SurfaceId=="deck/-1/-1"&&Math.Abs(n.Location.Position.X-x)<1e-8&&Math.Abs(n.Location.Position.Z-z)<1e-8);
                var start=nodeAt(-56,nodeZ);double endX=endpoint==0?-36:-39.2,endZ=endpoint==0?-46.4:-49.6;var end=nodeAt(endX,endZ);
                Assert.True(graph.TryAttach(origin,start.Id,out var enter));Assert.True(graph.TryAttach(target,end.Id,out var exit));double cost=enter.Cost+exit.Cost;int count=0,legs=0;var current=start;
                foreach(var corner in new[]{new NavPoint(-56,-50.4),new NavPoint(endX,-50.4),new NavPoint(endX,endZ)}){
                    int steps=(int)Math.Round((Math.Abs(corner.X-current.Location.Position.X)+Math.Abs(corner.Z-current.Location.Position.Z))/.8);
                    double dx=Math.Sign(corner.X-current.Location.Position.X)*.8,dz=Math.Sign(corner.Z-current.Location.Position.Z)*.8;
                    // Each segment is axis aligned; roundoff on the unchanged axis is ignored.
                    if(Math.Abs(corner.X-current.Location.Position.X)<1e-8)dx=0;if(Math.Abs(corner.Z-current.Location.Position.Z)<1e-8)dz=0;
                    for(int j=0;j<steps;j++){var next=nodeAt(current.Location.Position.X+dx,current.Location.Position.Z+dz);var arc=graph.Outgoing(current.Id).Single(a=>a.To==next.Id);cost+=arc.Cost;count++;legs+=arc.Trace?.Count??1;current=next;}
                }
                Assert.AreEqual(end.Id,current.Id);Assert.LessOrEqual(cost,40);Assert.LessOrEqual(count,50);Assert.LessOrEqual(legs,150);
                TestContext.WriteLine("K3 graph witness origin="+origin.Position.X.ToString("R")+","+origin.Position.Z.ToString("R")+" endpoint="+endpoint+" cost="+cost+" arcs="+count+" legs="+legs);
            }
            TestContext.WriteLine("K3 graph N="+graph.Nodes.Count+" degree="+maxDegree+" minCost="+minCost+" trace="+maxTrace);
        }
        private static void K3InputPreflight(NavigationArrivalWork work,string primeProvider,NavLocation actualOrigin,int actorId){
            var input=work.PhysicalInput;var a=input.Members.Single(m=>m.EntityId==actorId);var b=input.Members.Single(m=>m.EntityId!=actorId);var left=input.WorkingFor(a);var right=input.WorkingFor(b);var cp=work.Save();
            Assert.AreEqual(primeProvider,left.Provider.Binding);Assert.AreEqual(left.Provider.Binding,right.Provider.Binding);
            Assert.AreEqual(new NavGraphBinding(left.Provider.Binding,a.Clearance,left.Profile).StableKey,new NavGraphBinding(right.Provider.Binding,b.Clearance,right.Profile).StableKey);Assert.AreEqual(a.HoldBinding,b.HoldBinding);
            Assert.AreEqual(actualOrigin,a.Origin,"working input preserves actual stopped pose");Assert.AreEqual(new NavPoint(-56,-42),b.Origin.Position);
            Assert.True(left.Provider.IsValid(a.Origin,a.Clearance));Assert.True(right.Provider.IsValid(b.Origin,b.Clearance));Assert.Greater(4,a.Radius+b.Radius);
            Assert.AreEqual(3,cp.PageCounts[0]);Assert.AreEqual(0,cp.PageStarts[0]);
            var expected=new[]{new NavPoint(-35.4,-46),new NavPoint(-39,-49.6),new NavPoint(-35.4,-49.6)};
            for(int i=0;i<3;i++){Assert.AreEqual("deck/-1/-1",input.Source.Candidates[i].SurfaceId);Assert.AreEqual(expected[i].X,input.Source.Candidates[i].Position.X,1e-8);Assert.AreEqual(expected[i].Z,input.Source.Candidates[i].Position.Z,1e-8);Assert.True(left.Provider.IsValid(input.Source.Candidates[i],a.Clearance));}
            Assert.AreEqual("ramp/-1/-1/x",input.Source.Candidates[3].SurfaceId);
            var prepared=cp.PreparationRegions?[0]??cp.PreparedClearances.Single().Regions[0];var indices=cp.PreparationCandidateIndices?[0]??cp.PreparedClearances.Single().CandidateIndices[0];
            Assert.NotNull(prepared);CollectionAssert.AreEqual(new[]{0,1,2},indices);
            Func<NavArrivalMember,NavScheduledRequest> request=m=>{var binding=input.WorkingFor(m);var subscription=new NavRouteSubscription(m.WorldGeneration,m.GroupId,m.RootOrderRevision,m.CommandSequence,m.IssuedTick,m.ActivationRevision,m.EntityId,m.Incarnation,m.MemberOrderRevision,m.MobilityRevision,m.RequestSequence,m.Origin,input.Source.Candidates[0],m.HoldBinding,NavRouteProvenance.CommandMember);return new NavScheduledRequest(binding.Provider,m.Clearance,binding.Profile,input.Limits,prepared,subscription,input.MaxLabels);};
            Assert.AreEqual(request(a).ArtifactKey,request(b).ArtifactKey,"same actual prepared page artifact");
            foreach(var member in input.Members)foreach(var endpoint in input.Source.Candidates.Take(2)){
                var binding=input.WorkingFor(member);Assert.False(binding.Provider.CanSweep(member.Origin,endpoint,member.Clearance),"K3 must require graph work");
                foreach(var occupant in input.Occupancy.Occupants.Where(o=>o.BlocksPlacement)){double dx=endpoint.Position.X-occupant.Location.Position.X,dz=endpoint.Position.Z-occupant.Location.Position.Z;double spacing=Math.Max(input.ArrivalProfile.ArrivalSlotSpacing,4*Math.Max(member.Radius,occupant.Radius)+2*input.ArrivalProfile.ArrivalTolerance+2*input.ArrivalProfile.LocalGridCell);Assert.GreaterOrEqual(dx*dx+dz*dz,spacing*spacing-1e-9,"endpoint occupied");}
            }
            double ex=expected[0].X-expected[1].X,ez=expected[0].Z-expected[1].Z;Assert.GreaterOrEqual(ex*ex+ez*ez,3.6*3.6-1e-8);
            TestContext.WriteLine("K3 actual input page0=3 prepared=3 compile/provider/artifact equal; origins full-radius valid; two free endpoints directblocked; candidates="+input.Source.Candidates.Count);
        }
        [TestCase(1)][TestCase(3)] public void FixedPollsSpendOneGrantAndWorldStepsAfterQuantum(int chunk){
            var a=Authority();var k=new CertifiedRouteKernel();
            try{
                Request(a);Assert.False(a.TryAdvance());
                using(var p=new RootPin(Root(a))){Assert.AreEqual(1,p.Value.QuantumSerial);Assert.AreEqual(1,p.Value.QuantumTick);Assert.Zero(p.Value.ConsumedQuantum);}
                for(int i=0;i<10;i++){Assert.False(a.TryAdvance());Assert.Zero(a.Tick);}
                Finish(a,k,chunk);
                using(var p=new RootPin(Root(a))){Assert.AreEqual(1,p.Value.QuantumSerial);Assert.True(p.Value.QuantumCompleted);Assert.Zero(p.Value.ConsumedQuantum);}
                var complete=a.CaptureBytes();for(int i=0;i<3;i++)k.Pump(a.NavigationBinding,1);CollectionAssert.AreEqual(complete,a.CaptureBytes(),"physical Pump cannot mint another grant");
                Assert.True(a.TryAdvance());Assert.AreEqual(1,a.Tick);
                using(var p=new RootPin(Root(a)))Assert.AreEqual(1,p.Value.ConsumedQuantum);
            }finally{Cleanup(a,k);}
        }
        [TestCase(false)][TestCase(true)] public void FreshWorkerBytesPreserveMidQuantumAndCompletedBeforeConsumption(bool completed){
            var config=OfflineParticipantAuthorityTests.Config(2);var a=Authority();var k=new CertifiedRouteKernel();PlayableAuthorityTick b=null;CertifiedRouteKernel fresh=null;CertifiedRouteWorker worker=null;
            try{
                Request(a);Assert.False(a.TryAdvance());k.PhysicalOperationsPerPump=2;k.Pump(a.NavigationBinding,1);
                if(completed)Finish(a,k,2);
                var saved=a.CaptureBytes();b=PlayableAuthorityTick.RestoreBytes(saved,config);fresh=new CertifiedRouteKernel();CollectionAssert.AreEqual(saved,b.CaptureBytes(),"immediate fresh bytes recapture");
                Finish(a,k,int.MaxValue);fresh.PhysicalOperationsPerPump=3;worker=new CertifiedRouteWorker(b.NavigationBinding,99,fresh);
                Assert.True(System.Threading.SpinWait.SpinUntil(()=>{using(var p=new RootPin(Root(b)))return p.Value.QuantumCompleted;},5000),"fresh real worker must finish the saved grant without another authority wake");
                CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes(),"same suffix despite different physical chunks");
                Assert.True(a.TryAdvance());Assert.True(b.TryAdvance());CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes(),"consume once and step exact world");
                bool aa=a.TryAdvance(),bb=b.TryAdvance();Assert.AreEqual(aa,bb);
                using(var x=new RootPin(Root(a)))using(var y=new RootPin(Root(b))){Assert.AreEqual(x.Value.QuantumSerial,y.Value.QuantumSerial);Assert.AreEqual(x.Value.QuantumTick,y.Value.QuantumTick);}
            }finally{if(worker!=null){worker.Dispose();Assert.True(System.Threading.SpinWait.SpinUntil(()=>worker.IsStopped,5000));}if(b!=null)Cleanup(b,fresh??new CertifiedRouteKernel());Cleanup(a,k);}
        }
        [Test] public void AsyncMissedTicksDoNotAccumulateGrantsAndPauseDoesNotIssue(){
            var a=Authority(true);var k=new CertifiedRouteKernel();
            try{
                Request(a);Assert.True(a.TryAdvance());
                for(int i=0;i<30;i++)Assert.True(a.TryAdvance());
                using(var p=new RootPin(Root(a))){Assert.AreEqual(1,p.Value.QuantumSerial);Assert.AreEqual(1,p.Value.QuantumTick);}
                Finish(a,k,2);Assert.True(a.TryAdvance(true));Assert.AreEqual(31,a.Tick);
                using(var p=new RootPin(Root(a)))Assert.AreEqual(1,p.Value.QuantumSerial);
                Assert.True(a.TryAdvance());
                using(var p=new RootPin(Root(a))){Assert.LessOrEqual(p.Value.QuantumSerial,2);if(p.Value.QuantumSerial==2)Assert.AreEqual(32,p.Value.QuantumTick);}
            }finally{Cleanup(a,k);}
        }
    }
}
