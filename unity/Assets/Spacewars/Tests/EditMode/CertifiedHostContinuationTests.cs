using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class CertifiedHostContinuationTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private sealed class ResolvingBirth : NavOwnedStorage
        {
            private readonly ManualResetEventSlim entered,release;
            internal ResolvingBirth(ManualResetEventSlim entered,ManualResetEventSlim release):base(128){this.entered=entered;this.release=release;}
            protected override void ReleaseEdges(){entered.Set();if(!release.Wait(5000))throw new TimeoutException("Resolver release was not signaled.");}
        }
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private object domain;
        private NavigationSession session;
        private CertifiedRouteKernel kernel;
        private long command;
        private PlayableProfile activeProfile=PlayableProfile.Default;

        [SetUp] public void SetUp()
        {
            activeProfile=PlayableProfile.Default;
            domain=Activator.CreateInstance(Domain,F,null,new object[]{PlayableProfile.Default,1L,false},null);
            session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);
            kernel=new CertifiedRouteKernel();
        }

        [TearDown] public void TearDown(){kernel?.Clear();if(session!=null){session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);}}

        private object Call(string name,params object[] args)=>Domain.GetMethod(name,F).Invoke(domain,args);
        private int Spawn(double x,double z)=>(int)Call("SpawnUnit",new NavPoint(x,z),PlayableOwner.Player,PlayableEntityKind.Tank);
        private static object MovementReadback(NavUnit actor)=>new{actor.Id,actor.Incarnation,actor.Moving,Outcome=actor.Outcome.ToString(),Position=new{actor.Position.X,actor.Position.Z},actor.Heading,Goal=new{actor.Goal.X,actor.Goal.Z},actor.routeIndex,GlobalRoute=actor.route.Select(p=>new{x=p.X,z=p.Z}).ToArray(),actor.localIndex,LocalRoute=actor.localRoute.Select(p=>new{x=p.X,z=p.Z}).ToArray(),actor.blockedSeconds,actor.noProgressTicks};
        private void AssertClearFromPath(NavUnit actor,params NavPoint[] points)
        {
            var clearFrom=typeof(NavCrowd).GetMethod("ClearFrom",F);Assert.NotNull(clearFrom);var from=actor.Location;
            foreach(var point in points){var args=new object[]{actor,from,point,null};Assert.True((bool)clearFrom.Invoke(session.Crowd,args),$"actual ClearFrom rejected QA repair segment from {from.Position} to {point}");from=(NavLocation)args[3];}
        }
        private void Arrival(int id,NavPoint goal)
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",
                PlayableCommandKind.Move,new[]{id},goal,origin:PlayableOrderOrigin.Human),null));
        }
        private PlayableRouteBinding Binding()=>new PlayableRouteBinding(
            (NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain),activeProfile,
            session.Admission,session.RoutePort);
        private void Pump(int budget)=>kernel.Pump(Binding(),budget);
        private int ArrivalLeaseCount=>((System.Collections.Generic.IDictionary<long,ArrivalProviderLease>)typeof(NavigationSession)
            .GetField("arrivalProjectionLeases",F).GetValue(session)).Count;
        private NavigationHostRow[] HostRows
        {
            get{var root=session.PinCommittedHostRoot();try{return root?.Rows.Select(e=>(NavigationHostRow)e.Value).ToArray()??Array.Empty<NavigationHostRow>();}finally{root?.Release();}}
        }
        private NavigationHostRow HostRow(int entity)=>HostRows.FirstOrDefault(row=>row.Request.Entity==entity);
        private string HostFacts()
        {
            var c=kernel.Counters;var root=session.PinCommittedHostRoot();
            try{return string.Join(":",kernel.SolverEpoch,kernel.ProgressSequence,c.LogicalGraphWork,c.PhysicalGraphWork,
                c.LogicalFieldWork,c.PhysicalFieldWork,c.ConnectorWork,c.PendingSubscriptions,c.ReadyResults,
                c.CacheFields,c.CacheBytes,c.RetainedBytes,kernel.RetainedBytes,
                root.Rows.Count,root.Scan.Count,root.Projection.Count,root.Completion.Count);}
            finally{root?.Release();}
        }
        private object ColdRetentionReadback(string stage)
        {
            var budget=session.PhysicalHostBudget;
            var before=new{budget.RetainedBytes,budget.PhysicalBytes,budget.HeldBytes,budget.CommittedBytes,budget.ActiveScopes};
            var backing=(NavigationCommandAuthority)typeof(NavigationSession).GetField("groupCommandBacking",F).GetValue(session);
            var names=backing?.CrowdNames;var keys=names?.Keys.ToArray()??Array.Empty<int>();
            var entries=names==null?Array.Empty<object>():Enumerable.Range(0,names.Names.Length).Select(i=>{
                var name=names.Names[i];return (object)new{Entity=keys[i/3],Slot=i%3,Role=name.Role.ToString(),OwnerType=name.Owner?.GetType().FullName,OwnerBytes=name.Owner?.StorageBytes,OwnerReferences=name.Owner?.References};
            }).ToArray();
            long unique=NavigationStorageOwnershipTests.UniqueWithSession(session);
            string ownerShape=NavigationStorageOwnershipTests.OwnerShapeWithSession(session);
            var after=new{budget.RetainedBytes,budget.PhysicalBytes,budget.HeldBytes,budget.CommittedBytes,budget.ActiveScopes};
            return new{Stage=stage,BudgetBefore=before,BackingBytes=backing?.StorageBytes,BackingReferences=backing?.References,CrowdNamesBytes=names?.StorageBytes,CrowdNamesReferences=names?.References,KeysLength=names?.Keys.Length,NameReferencesLength=names?.Names.Length,Keys=keys,Entries=entries,CrowdIds=session.Crowd.Units.Select(u=>u.Id).ToArray(),UniqueWithSession=unique,OwnerShapeWithSession=ownerShape,BudgetAfter=after};
        }
        private byte[] Bytes(){using(var state=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation")){CertifiedPhysicalPumpTests.AssertCaptureDtoLayout(state.CaptureNavigation);return state.Encode();}}
        private void Restore(byte[] bytes)
        {
            kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");kernel=null;session=null;domain=null;
            domain=Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{bytes,activeProfile,7,"a5a-host-continuation"});
            session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);
            kernel=new CertifiedRouteKernel();
        }

        private byte[] ProbeBytes(){using(var state=session.CaptureOwnedState()){CertifiedPhysicalPumpTests.AssertCaptureDtoLayout(state);return WorldWire.Pack(w=>WorldWire.Write(w,state));}}
        private void PumpProbe(int budget)=>kernel.Pump(new PlayableRouteBinding(session.Admission.Geometry,activeProfile,session.Admission,session.RoutePort),budget);
        private void RestoreProbeBytes(byte[] bytes,IPlayableTerrain terrainOverride=null){
            var g=session.Admission.Geometry;var polygons=new System.Collections.Generic.Dictionary<NavPolygon,NavPolygon>();NavObstacle Fresh(NavObstacle o){if(o.Polygon==null)return o;if(!polygons.TryGetValue(o.Polygon,out var polygon)){polygon=new NavPolygon(o.Polygon.Vertices.ToArray());polygons.Add(o.Polygon,polygon);}return new NavObstacle(polygon,o.Bottom,o.Top);}var freshGeometry=new NavGeometry(g.HalfExtent,g.Obstacles.Select(Fresh).ToArray(),g.Revision);
            kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");session=new NavigationSession(1,freshGeometry,activeProfile.Navigation,null,terrainOverride);Domain.GetField("navigation",F).SetValue(domain,session);session.ConfigureMarch(activeProfile.GroupMarchMaximumStretch,activeProfile.Revision);
            using(var state=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState))session.RestoreState(state);kernel=new CertifiedRouteKernel();
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)] public void InstalledMovementSurvivesWorkerQuantumChangeButRefusesActorChange(bool stopActor,bool ready)
        {
            int moving=Spawn(-18,-14);Arrival(moving,new NavPoint(-10,-14));
            for(int i=0;i<20000&&(session.PendingCount>0||session.AllocationCount>0);i++){Pump(4);session.ApplyResults();}
            Assert.Zero(session.PendingCount);Assert.Zero(session.AllocationCount);Assert.Zero(session.Answers.Count);
            Assert.True(session.Crowd.TryGet(moving,out var actor));Assert.True(actor.Moving);Assert.NotNull(session.InstalledExecutionFor(moving));
            for(int i=0;i<20000;i++){var root=session.PinCommittedHostRoot();bool idle;try{idle=root.Frontier.Stage==CertifiedRouteKernel.PumpStage.Idle;}finally{root.Release();}if(idle)break;Pump(4);}
            void PublishQuantum()
            {
                var source=session.PinCommittedHostRoot();NavigationHostRoot candidate=null;NavigationRetentionBudget.Scope scope=null;
                try{
                    Assert.AreEqual(CertifiedRouteKernel.PumpStage.Idle,source.Frontier.Stage);
                    Assert.True(session.TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,session.Generation,session.Admission.PublicationIdentity,out scope));
                    candidate=source.ForkOwned(scope.OwnedReservation());candidate.IssueQuantum(session.Generation,source.QuantumTick+1,4,session.Admission.PublishedThroughRequestSequence);
                    candidate.CompleteQuantum();candidate.ConsumeQuantum();candidate.SealPrepared();scope.Attach(candidate);session.PublishPhysicalHostRoot(scope,source,candidate,()=>{});
                }finally{scope?.Dispose();candidate?.ReleaseStorage();source?.Release();}
            }
            if(ready){int second=Spawn(-18,-6);Arrival(second,new NavPoint(-10,-6));for(int i=0;i<20000&&session.Answers.Count==0;i++){Pump(4);if(session.Answers.Count==0)session.PrepareDeliveryBarrier();}Assert.AreEqual(1,session.Answers.Count);Assert.AreEqual(NavSolveStatus.Ready,session.Answers.CopyItems()[0].Status);
                for(int i=0;i<20000;i++){var root=session.PinCommittedHostRoot();bool idle;try{idle=root.Frontier.Stage==CertifiedRouteKernel.PumpStage.Idle;}finally{root.Release();}if(idle)break;Pump(4);}}
            PublishQuantum();
            int applied=session.AppliedResults;
            var before=actor.Position;int publications=session.OwnedMovementPublications;bool changed=false;var budget=session.PhysicalHostBudget;
            var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            try{
                budget.AllocationAdmission=_=>{
                    if(changed||!new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(GroupMarch)&&f.GetMethod().Name=="PrepareOwned"))return true;
                    Assert.False(Monitor.IsEntered(gate));changed=true;PublishQuantum();
                    if(stopActor)Assert.True(session.TryStopGroup(new[]{moving},true));
                    return true;
                };
                session.Step(1d/30);
            }finally{budget.AllocationAdmission=null;}
            Assert.True(changed,"Inject a legitimate worker quantum publication after movement source capture.");
            var afterHost=session.PinCommittedHostRoot();try{Assert.AreEqual(2,afterHost.QuantumSerial);Assert.AreEqual(2,afterHost.ConsumedQuantum);}finally{afterHost.Release();}
            if(stopActor){Assert.AreEqual(publications,session.OwnedMovementPublications);Assert.AreEqual(before,actor.Position);Assert.True(actor.Held);Assert.AreEqual(applied,session.AppliedResults);}
            else{
                Assert.AreEqual(publications+1,session.OwnedMovementPublications,"Worker-only quantum progress must not discard installed movement.");Assert.Greater(actor.Position.X,before.X);
                Assert.Zero(session.LastOwnedMovementPublicationAllocatedBytes);Assert.AreEqual(applied+(ready?1:0),session.AppliedResults);Assert.Zero(session.Answers.Count);
                byte[] captured=Bytes();session.Step(1d/30);byte[] next=Bytes();Restore(captured);CollectionAssert.AreEqual(captured,Bytes());session.Step(1d/30);CollectionAssert.AreEqual(next,Bytes(),"Current-format continuation after commuted worker progress.");
            }
            Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [TestCase(false,false,false)][TestCase(true,false,false)][TestCase(false,true,false)][TestCase(false,false,true)][TestCase(true,false,true)][TestCase(false,true,true)] public void InstalledMovementSurvivesUnrelatedArrivalWorkButRefusesRetirement(bool stopForeign,bool completeForeign,bool ready)
        {
            int moving=Spawn(-18,-14);Arrival(moving,new NavPoint(-10,-14));
            for(int i=0;i<20000&&(session.PendingCount>0||session.AllocationCount>0);i++){Pump(4);session.ApplyResults();}
            Assert.Zero(session.PendingCount);Assert.Zero(session.AllocationCount);Assert.Zero(session.Answers.Count);
            Assert.True(session.Crowd.TryGet(moving,out var actor));Assert.True(actor.Moving);
            if(ready){int second=Spawn(-18,-10);Arrival(second,new NavPoint(-10,-10));for(int i=0;i<20000&&session.Answers.Count==0;i++){Pump(4);if(session.Answers.Count==0)session.PrepareDeliveryBarrier();}Assert.AreEqual(1,session.Answers.Count);Assert.AreEqual(NavSolveStatus.Ready,session.Answers.CopyItems()[0].Status);}
            int applied=session.AppliedResults;
            int foreign=Spawn(-18,-6);Arrival(foreign,new NavPoint(-10,-6));Assert.True(session.PreparePhysicalArrivalAttempt());Assert.False(session.HasReadyArrival);
            var before=actor.Position;int publications=session.OwnedMovementPublications;bool changed=false;var budget=session.PhysicalHostBudget;
            var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            try{
                budget.AllocationAdmission=_=>{
                    if(changed||!new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(GroupMarch)&&f.GetMethod().Name=="PrepareOwned"))return true;
                    Assert.False(Monitor.IsEntered(gate));changed=true;
                    Assert.True(session.AdvancePhysicalArrivalAuthority(1),"Execute the real worker work/history publication and Working transitions.");
                    Assert.False(session.HasReadyArrival,"The injected service step is still derived work, not a new Ready result.");
                    if(completeForeign){for(int i=0;i<20000&&!session.HasReadyArrival;i++)Assert.True(session.AdvancePhysicalArrivalAuthority(4));Assert.True(session.HasReadyArrival);}
                    if(stopForeign)Assert.True(session.TryStopGroup(new[]{foreign},true));
                    return true;
                };
                session.Step(1d/30);
            }finally{budget.AllocationAdmission=null;}
            Assert.True(changed);
            if(stopForeign||completeForeign){Assert.AreEqual(publications,session.OwnedMovementPublications);Assert.AreEqual(before,actor.Position);
                if(stopForeign)Assert.True(session.Crowd.Units.Single(u=>u.Id==foreign).Held);
                else if(!ready){session.Step(1d/30);Assert.AreEqual(21,session.LastOwnedMovementNullReason,"The next step selects the ready authority prefix instead of endlessly refusing its captured source.");}
            }
            else{
                Assert.AreEqual(publications+1,session.OwnedMovementPublications,"Unrelated worker-only arrival progress must not discard installed movement.");Assert.Greater(actor.Position.X,before.X);Assert.Zero(session.LastOwnedMovementPublicationAllocatedBytes);Assert.AreEqual(applied+(ready?1:0),session.AppliedResults);Assert.Zero(session.Answers.Count);
                byte[] captured=Bytes();session.Step(1d/30);byte[] next=Bytes();Restore(captured);CollectionAssert.AreEqual(captured,Bytes());session.Step(1d/30);CollectionAssert.AreEqual(next,Bytes());
            }
            Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [Test] public void CompatibilityMotionAdmissionRefusalPrecedesActualReadyAnswerAndMarchMutation()
        {
            int id=Spawn(-18,-14);Arrival(id,new NavPoint(-10,-14));for(int i=0;i<20000&&session.Answers.Count==0;i++){Pump(4);if(session.Answers.Count==0)session.PrepareDeliveryBarrier();}Assert.AreEqual(1,session.Answers.Count);Assert.AreEqual(NavSolveStatus.Ready,session.Answers.CopyItems()[0].Status);
            byte[] prefix=Bytes();var answers=session.Answers.CopyItems();var actor=session.Crowd.Units.Single(u=>u.Id==id);var point=actor.Position;var budget=session.PhysicalHostBudget;long physical=budget.PhysicalBytes;int applied=session.AppliedResults,batches=session.OwnedResultBatchPublications,movement=session.OwnedMovementPublications,denied=0,callbacks=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            Action<NavigationRequest> activated=_=>callbacks++;session.RouteActivated+=activated;
            try{budget.AllocationAdmission=_=>{var frames=new System.Diagnostics.StackTrace().GetFrames();if(frames.Any(f=>f.GetMethod().DeclaringType==typeof(NavStorageScratch)&&f.GetMethod().Name=="Reserve")&&frames.Any(f=>f.GetMethod().Name=="CreateCompatibilityMotionWorkspace")){Assert.False(Monitor.IsEntered(gate));denied++;return false;}return true;};session.Step(1d/30);}finally{budget.AllocationAdmission=null;session.RouteActivated-=activated;}
            Assert.AreEqual(1,denied);Assert.Zero(callbacks);Assert.AreEqual(point,actor.Position);Assert.AreEqual(applied,session.AppliedResults);Assert.AreEqual(batches,session.OwnedResultBatchPublications);Assert.AreEqual(movement,session.OwnedMovementPublications);CollectionAssert.AreEqual(answers,session.Answers.CopyItems());Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(prefix,Bytes());
            session.Step(1d/30);Assert.AreEqual(applied+1,session.AppliedResults);Assert.Zero(session.Answers.Count);Assert.Greater(actor.Position.X,point.X);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [TestCase(false)][TestCase(true)] public void SharedMotionFrameRefusesAndPublishesMovementOrProjectedReadyAtomically(bool ready)
        {
            int moving=Spawn(-18,-14);Arrival(moving,new NavPoint(-10,-14));
            for(int i=0;i<20000&&session.PendingCount>0;i++){Pump(4);session.ApplyResults();}
            Assert.Zero(session.PendingCount);var actor=session.Crowd.Units.Single(u=>u.Id==moving);Assert.NotNull(session.InstalledExecutionFor(moving));
            int second=0;if(ready){second=Spawn(-18,-6);Arrival(second,new NavPoint(-10,-6));for(int i=0;i<20000&&session.Answers.Count==0;i++){Pump(4);if(session.Answers.Count==0)session.PrepareDeliveryBarrier();}Assert.AreEqual(1,session.Answers.Count);Assert.AreEqual(NavSolveStatus.Ready,session.Answers.CopyItems()[0].Status);}
            var prefix=Bytes();var point=actor.Position;var budget=session.PhysicalHostBudget;long physical=budget.PhysicalBytes;int publications=session.OwnedMovementPublications,batches=session.OwnedResultBatchPublications,denied=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            try{budget.AllocationAdmission=_=>{Assert.False(Monitor.IsEntered(gate));if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavPreparedCrowdStep)&&f.GetMethod().Name=="Create")){denied++;return false;}return true;};session.Step(1d/30);}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.AreEqual(point,actor.Position);Assert.AreEqual(publications,session.OwnedMovementPublications);Assert.AreEqual(batches,session.OwnedResultBatchPublications);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(prefix,Bytes());
            session.Step(1d/30);Assert.AreEqual(publications+1,session.OwnedMovementPublications);Assert.Zero(session.LastOwnedMovementPublicationAllocatedBytes);Assert.Greater(actor.Position.X,point.X);
            if(ready){Assert.AreEqual(batches+1,session.OwnedResultBatchPublications);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);Assert.Zero(session.Answers.Count);Assert.NotNull(session.InstalledExecutionFor(second));Assert.Greater(session.Crowd.Units.Single(u=>u.Id==second).Position.X,-18);}
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);byte[] published=Bytes();session.Step(1d/30);byte[] suffix=Bytes();Restore(published);CollectionAssert.AreEqual(published,Bytes());session.Step(1d/30);CollectionAssert.AreEqual(suffix,Bytes());
        }
        [TestCase(false)][TestCase(true)] public void ReadyAndActualLocalRepairRefuseAndPublishOneWholeStepBeforeCallbacks(bool mixed)
        {
            int factory=0;if(mixed){Call("AddCredits",PlayableOwner.Player,10000d);Call("BuildAt",1,2,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null);Call("AdvanceFoundations");Call("AdvanceBuildings",100d);var buildings=(IDictionary)Domain.GetField("buildings",F).GetValue(domain);foreach(DictionaryEntry pair in buildings)if((PlayableBuildingKind)pair.Value.GetType().GetField("Kind").GetValue(pair.Value)==PlayableBuildingKind.Factory)factory=(int)pair.Key;Assert.Greater(factory,0);}
            int moving=Spawn(-18,-14);Spawn(-14,-14);Arrival(moving,new NavPoint(-10,-14));
            for(int i=0;i<20000&&(session.PendingCount>0||session.AllocationCount>0);i++){Pump(4);session.ApplyResults();}
            Assert.Zero(session.PendingCount);Assert.True(session.Crowd.TryGet(moving,out var actor));const double dt=1d/30;
            for(int tick=0;tick<300&&actor.blockedSeconds+dt<activeProfile.Navigation.RepairRetrySeconds;tick++)Call("Step",dt);
            Assert.GreaterOrEqual(actor.blockedSeconds+dt,activeProfile.Navigation.RepairRetrySeconds);Assert.Zero(session.Crowd.RepairCount);var global=actor.StorageOwner.Global;
            int ready=Spawn(-18,-6);Arrival(ready,new NavPoint(-10,-6));
            if(mixed){Assert.AreEqual(PlayableCommandStatus.Accepted,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.SetRally,new[]{factory},new NavPoint(-8,10)),null));Call("AdvanceRally");Assert.AreEqual(PlayableCommandStatus.Accepted,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.SetRally,new[]{factory},new NavPoint(-6,10)),null));Call("AdvanceRally");}
            int expectedAnswers=mixed?3:1;for(int i=0;i<20000&&session.Answers.Count<expectedAnswers;i++){Pump(4);if(session.Answers.Count<expectedAnswers)session.PrepareDeliveryBarrier();}
            Assert.AreEqual(expectedAnswers,session.Answers.Count);Assert.Zero(session.AllocationCount);Assert.True(session.Crowd.TryGet(ready,out var next));var nextPosition=next.Position;var answers=session.Answers.CopyItems();var answer=answers.Single(a=>a.Request.Entity==ready);Assert.AreEqual(ready,answer.Request.Entity);Assert.AreEqual(NavSolveStatus.Ready,answer.Status);
            var budget=session.PhysicalHostBudget;byte[] prefix=Bytes();long physical=budget.PhysicalBytes;int applied=session.AppliedResults,batch=session.OwnedResultBatchPublications,movements=session.OwnedMovementPublications,denied=0,underGate=0;bool destination=false;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;var frames=new System.Diagnostics.StackTrace().GetFrames();if(frames.Any(f=>f.GetMethod().DeclaringType==typeof(NavigationInstalledExecutionOwner)))destination=true;if(frames.Any(f=>f.GetMethod().Name=="ReplaceLocal"&&f.GetMethod().DeclaringType==typeof(NavPreparedCrowdStep))){Assert.True(destination,"Ready destination must exist before movement local-output refusal");denied++;return false;}return true;};session.Step(dt);}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreEqual(batch,session.OwnedResultBatchPublications);Assert.AreEqual(movements,session.OwnedMovementPublications);Assert.AreEqual(applied,session.AppliedResults);CollectionAssert.AreEqual(answers,session.Answers.CopyItems());Assert.AreEqual(nextPosition,next.Position);Assert.AreSame(global,actor.StorageOwner.Global);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(prefix,Bytes());
            if(!mixed){
                bool changed=false;long operation=(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session),attachment=(long)typeof(NavigationSession).GetField("certifiedHostAttachmentIdentity",F).GetValue(session);var host=session.PinCommittedHostRoot();
                try{
                    budget.AllocationAdmission=_=>{if(!changed&&new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(GroupMarch)&&f.GetMethod().Name=="PrepareOwned")){changed=true;session.DetachCertifiedHost(kernel);session.AttachCertifiedHost(kernel);Assert.AreEqual(operation,typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session));}return true;};session.Step(dt);
                }finally{budget.AllocationAdmission=null;var current=session.PinCommittedHostRoot();try{Assert.AreSame(host,current);}finally{current?.Release();host?.Release();}}
                Assert.True(changed);Assert.AreEqual(operation,typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session));Assert.AreEqual(attachment+1,typeof(NavigationSession).GetField("certifiedHostAttachmentIdentity",F).GetValue(session));Assert.AreEqual(batch,session.OwnedResultBatchPublications);Assert.AreEqual(movements,session.OwnedMovementPublications);CollectionAssert.AreEqual(answers,session.Answers.CopyItems());Assert.AreEqual(nextPosition,next.Position);Assert.AreSame(global,actor.StorageOwner.Global);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(prefix,Bytes());
            }
            int callbacks=0;void Activated(NavigationRequest request){if(request.Entity!=ready)return;callbacks++;Assert.False(Monitor.IsEntered(gate));Assert.Zero(session.Answers.Count);Assert.Greater(session.Crowd.RepairCount,0);Assert.Greater(actor.StorageOwner.Local.Count,0);Assert.Greater(next.Position.X,nextPosition.X);Assert.NotNull(session.InstalledExecutionFor(ready));}
            session.RouteActivated+=Activated;try{session.Step(dt);}finally{session.RouteActivated-=Activated;}
            Assert.AreEqual(1,callbacks);Assert.AreEqual(batch+1,session.OwnedResultBatchPublications);Assert.AreEqual(movements+1,session.OwnedMovementPublications);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);Assert.Zero(session.LastOwnedMovementPublicationAllocatedBytes);Assert.AreSame(global,actor.StorageOwner.Global);Assert.AreEqual(mixed?1:0,typeof(NavigationSession).GetProperty("ProbeAnswerCount",F).GetValue(session));Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
            byte[] published=Bytes();var suffix=new byte[20][];for(int i=0;i<suffix.Length;i++){Call("Step",dt);suffix[i]=Bytes();}Pump(4);byte[] afterPump=Bytes();string facts=HostFacts();
            Restore(prefix);CollectionAssert.AreEqual(prefix,Bytes());kernel.Attach(session.RoutePort);typeof(CertifiedRouteKernel).GetMethod("RefreshPhysicalFrontier",F).Invoke(kernel,new object[]{session.RoutePort});session.Step(dt);CollectionAssert.AreEqual(published,Bytes());for(int i=0;i<suffix.Length;i++){Call("Step",dt);CollectionAssert.AreEqual(suffix[i],Bytes(),"composed Ready/repair cold suffix "+i);}Pump(4);CollectionAssert.AreEqual(afterPump,Bytes());Assert.AreEqual(facts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }
        [Test] public void ActualMovingGroupRepairsInOwnedStepAndRefusesLocalOutputAtomically()
        {
            int moving=Spawn(-18,-14);int neighbor=Spawn(-14,-14);Arrival(moving,new NavPoint(-10,-14));
            for(int i=0;i<20000&&(session.PendingCount>0||session.AllocationCount>0);i++){Pump(4);session.ApplyResults();}
            Assert.Zero(session.PendingCount);Assert.Zero(session.AllocationCount);Assert.Zero(session.Answers.Count);Assert.True(session.Crowd.TryGet(moving,out var actor));Assert.True(actor.Moving);Assert.NotNull(session.InstalledExecutionFor(moving));
            var global=actor.StorageOwner.Global;var budget=session.PhysicalHostBudget;int start=session.OwnedMovementPublications;
            Assert.AreEqual(.72,activeProfile.TankCollisionRadius,1e-12,"Tank radius changed; review this QA-only blockage construction.");Assert.AreEqual(.2,activeProfile.Navigation.LocalGridCell,1e-12,"Local grid changed; review this QA-only blockage construction.");Assert.AreEqual(3.6,activeProfile.Navigation.LocalDetourDistance,1e-12,"Detour reach changed; review this QA-only path.");
            double radius=activeProfile.TankCollisionRadius,d=2*radius+activeProfile.Navigation.LocalGridCell;var blockers=new[]{new NavPoint(actor.Position.X+d,actor.Position.Z),new NavPoint(actor.Position.X+d/2,actor.Position.Z+Math.Sqrt(3)*d/2),new NavPoint(actor.Position.X+d/2,actor.Position.Z-Math.Sqrt(3)*d/2)};
            foreach(var blockerPoint in blockers){Assert.True(session.Crowd.CanPlace(blockerPoint,radius),$"QA U blocker placement must pass geometry, terrain, and non-overlap checks: {blockerPoint}");Spawn(blockerPoint.X,blockerPoint.Z);}
            var rearExit=new[]{new NavPoint(actor.Position.X-1.2,actor.Position.Z),new NavPoint(actor.Position.X-1.2,actor.Position.Z+2.8),new NavPoint(actor.Position.X+1.6,actor.Position.Z+3.0)};
            foreach(var exitPoint in rearExit){double dx=exitPoint.X-actor.Position.X,dz=exitPoint.Z-actor.Position.Z;Assert.LessOrEqual(Math.Sqrt(dx*dx+dz*dz),activeProfile.Navigation.LocalDetourDistance+1e-12,"QA rear-exit intermediate must remain inside the existing detour reach.");}
            AssertClearFromPath(actor,rearExit[0],rearExit[1],rearExit[2],actor.Goal);
            const double dt=1d/30;
            for(int tick=0;tick<300&&actor.blockedSeconds+dt<activeProfile.Navigation.RepairRetrySeconds;tick++)Call("Step",dt);
            bool hasCurrentActor=session.Crowd.TryGet(moving,out var currentActor),hasCurrentNeighbor=session.Crowd.TryGet(neighbor,out var currentNeighbor);
            TestContext.WriteLine("OWNED_RETRY_PRECONDITION "+Newtonsoft.Json.JsonConvert.SerializeObject(new{ActorReferenceIsCurrent=hasCurrentActor&&ReferenceEquals(actor,currentActor),CapturedActor=MovementReadback(actor),CurrentActor=hasCurrentActor?MovementReadback(currentActor):null,CapturedNeighbor=MovementReadback(session.Crowd.Units.Single(u=>u.Id==neighbor)),CurrentNeighbor=hasCurrentNeighbor?MovementReadback(currentNeighbor):null,RepairCount=session.Crowd.RepairCount,RepairFailed=session.Crowd.RepairFailed,session.Crowd.MaxNoProgressTicks,MovementPublicationDelta=session.OwnedMovementPublications-start,session.OwnedMovementPublications,session.LastOwnedMovementPublicationAllocatedBytes,RepairRetrySeconds=activeProfile.Navigation.RepairRetrySeconds,BlockedTimeoutSeconds=activeProfile.Navigation.BlockedTimeoutSeconds}));
            Assert.Greater(session.OwnedMovementPublications,start);Assert.Zero(session.LastOwnedMovementPublicationAllocatedBytes);Assert.GreaterOrEqual(actor.blockedSeconds+dt,activeProfile.Navigation.RepairRetrySeconds);Assert.Zero(session.Crowd.RepairCount);
            var point=actor.Position;double heading=actor.Heading,blocked=actor.blockedSeconds;int cursor=actor.routeIndex,noProgress=actor.noProgressTicks,publications=session.OwnedMovementPublications;var spatial=session.Crowd.SpatialStorage;var group=session.GroupFor(moving);byte[] prefix=Bytes();long physical=budget.PhysicalBytes;int denied=0,underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().Name=="ReplaceLocal"&&f.GetMethod().DeclaringType==typeof(NavPreparedCrowdStep))){denied++;return false;}return true;};session.Step(dt);}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreEqual(publications,session.OwnedMovementPublications);Assert.AreEqual(point,actor.Position);Assert.AreEqual(heading,actor.Heading);Assert.AreEqual(blocked,actor.blockedSeconds);Assert.AreEqual(cursor,actor.routeIndex);Assert.AreEqual(noProgress,actor.noProgressTicks);Assert.AreSame(spatial,session.Crowd.SpatialStorage);Assert.AreSame(global,actor.StorageOwner.Global);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(prefix,Bytes());
            session.Step(dt);Assert.AreEqual(publications+1,session.OwnedMovementPublications);Assert.Zero(session.LastOwnedMovementPublicationAllocatedBytes);Assert.Greater(session.Crowd.RepairCount,0);Assert.Greater(actor.StorageOwner.Local.Count,0);Assert.AreSame(global,actor.StorageOwner.Global);Assert.True(actor.Moving);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
            byte[] atRepair=Bytes();var suffix=new byte[20][];for(int i=0;i<suffix.Length;i++){Call("Step",dt);suffix[i]=Bytes();}Pump(4);byte[] afterPump=Bytes();string facts=HostFacts();
            TestContext.WriteLine("COLD_RETENTION_READBACK "+Newtonsoft.Json.JsonConvert.SerializeObject(ColdRetentionReadback("warm")));
            Restore(atRepair);CollectionAssert.AreEqual(atRepair,Bytes());for(int i=0;i<suffix.Length;i++){Call("Step",dt);CollectionAssert.AreEqual(suffix[i],Bytes(),"actual repaired movement cold suffix "+i);}Pump(4);CollectionAssert.AreEqual(afterPump,Bytes());
            TestContext.WriteLine("COLD_RETENTION_READBACK "+Newtonsoft.Json.JsonConvert.SerializeObject(ColdRetentionReadback("cold")));
            Assert.AreEqual(facts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }
        [TestCase(false)][TestCase(true)]
        public void PhysicalCompletionTracksBeforePublicationAndRestoresPubliclyDequeuedOwnership(bool publicTake)
        {
            Assert.NotNull(session.Probe(7,1,activeProfile.Navigation,new NavPoint(-4,2),new NavPoint(4,2)));
            var binding=new PlayableRouteBinding(session.Admission.Geometry,activeProfile,session.Admission,session.RoutePort);
            Assert.True(kernel.TryRegisterPhysicalOrdinary(binding));
            bool ready=false;for(int i=0;i<32&&!ready;i++){kernel.AdvancePhysicalOrdinary(binding,16384);var root=session.PinCommittedHostRoot();try{ready=root.Scheduler.ReadyCount>0;}finally{root.Release();}}
            Assert.True(ready);Assert.AreEqual(1,kernel.TransferPhysicalReady(binding,1));
            bool projected=false;for(int i=0;i<128&&!projected;i++){Assert.AreEqual(1,kernel.ProjectPhysicalOrdinary(binding,64));var root=session.PinCommittedHostRoot();try{projected=root.Completion.Count>0;}finally{root.Release();}}
            Assert.True(projected);var budget=session.PhysicalHostBudget;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            var prefix=ProbeBytes();long charge=budget.PhysicalBytes;var source=session.PinCommittedHostRoot();int denied=0,underGate=0;
            try{
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationCommandSlots<NavigationAnswer,NavigationHostAnswer>))){denied++;return false;}return true;};Assert.False(kernel.FlushPhysicalOrdinary(binding));}
                finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.Zero(session.Answers.Count);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                var unchanged=session.PinCommittedHostRoot();try{Assert.AreSame(source,unchanged);}finally{unchanged.Release();}CollectionAssert.AreEqual(prefix,ProbeBytes());
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};Assert.True(kernel.FlushPhysicalOrdinary(binding));}finally{budget.AllocationAdmission=null;}
                Assert.Zero(underGate);Assert.Zero(session.LastOwnedPhysicalCompletionAllocatedBytes);
            }finally{source.Release();}
            var trackingField=typeof(NavigationSession).GetField("ownedAnswerTracking",F);
            var tracked=(NavigationCommandSlots<NavigationAnswer,NavigationHostAnswer>)trackingField.GetValue(session);Assert.AreEqual(1,tracked.Count);Assert.AreEqual(4,tracked.Capacity);
            if(publicTake){
                Assert.True(session.Answers.TryDequeue(out var answer));Assert.True(session.Answers.TryEnqueue(answer));Assert.True(session.Answers.TryEnqueue(answer));Assert.True(session.Answers.TryDequeue(out _));Assert.True(session.Answers.TryDequeue(out _));
                session.CancelProbe(7);Assert.True(kernel.RetirePhysicalDelivered(binding));Assert.AreEqual(3,answer.Request.StorageOwner.TransportAnswers);
            }
            var completed=ProbeBytes();var oldBudget=budget;PumpProbe(1);var next=ProbeBytes();var facts=HostFacts();
            RestoreProbeBytes(completed);Assert.Zero(oldBudget.PhysicalBytes);CollectionAssert.AreEqual(completed,ProbeBytes());
            tracked=(NavigationCommandSlots<NavigationAnswer,NavigationHostAnswer>)trackingField.GetValue(session);Assert.AreEqual(1,tracked.Count);Assert.AreEqual(4,tracked.Capacity);Assert.AreEqual(publicTake?3:1,tracked.Values.Single().RequestOwner.TransportAnswers);
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            PumpProbe(1);CollectionAssert.AreEqual(next,ProbeBytes());Assert.AreEqual(facts,HostFacts());
        }

        [TestCase(1)][TestCase(2)] public void CanceledProjectedCompletionPrunesLastAttemptAndProviderBeforeColdNextPump(int members)
        {
            var entities=new int[members];for(int i=0;i<members;i++)entities[i]=Spawn(-18,-14+4*i);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Move,entities,new NavPoint(-10,-14),origin:PlayableOrderOrigin.Human),null));kernel.Continue=()=>ArrivalLeaseCount<members;
            try{for(int i=0;i<20000&&ArrivalLeaseCount<members;i++){Pump(4);session.ApplyResults();}}finally{kernel.Continue=null;}
            Assert.AreEqual(members,ArrivalLeaseCount);var binding=Binding();bool projected=false;
            for(int i=0;i<128&&!projected;i++){Assert.AreEqual(1,kernel.ProjectPhysicalOrdinary(binding,64));var root=session.PinCommittedHostRoot();try{projected=root.Completion.Count==members;}finally{root.Release();}}
            Assert.True(projected);Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Stop,entities,origin:PlayableOrderOrigin.Human),null));Assert.True(session.NextRetiringArrivalProjection().Retiring);Assert.AreEqual(members,ArrivalLeaseCount);
            if(members==2){Assert.True(kernel.FlushPhysicalOrdinary(binding));Assert.Zero(session.LastOwnedPhysicalCompletionAllocatedBytes);Assert.AreEqual(1,ArrivalLeaseCount);var remaining=(NavigationSession.NavigationLiveArrivalTables)typeof(NavigationSession).GetField("liveArrivalTables",F).GetValue(session);Assert.AreEqual(1,remaining.Count);session.ApplyResults();Assert.Zero(session.LastOwnedDiscardPublicationAllocatedBytes);Assert.AreEqual(1,session.RejectedResults);}
            var budget=session.PhysicalHostBudget;var prefix=Bytes();long charge=budget.PhysicalBytes;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);int denied=0,underGate=0;
            try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationSession.NavigationLiveArrivalTables))){denied++;return false;}return true;};Assert.False(kernel.FlushPhysicalOrdinary(binding));}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.AreEqual(1,ArrivalLeaseCount);Assert.AreEqual(1,session.RegisteredRouteCount);Assert.Zero(session.Answers.Count);CollectionAssert.AreEqual(prefix,Bytes());Assert.Zero(budget.ActiveScopes);Assert.Zero(budget.HeldBytes);
            void Complete(){Assert.True(kernel.FlushPhysicalOrdinary(Binding()));Assert.Zero(session.LastOwnedPhysicalCompletionAllocatedBytes);Assert.Zero(ArrivalLeaseCount);Assert.Zero(session.RegisteredRouteCount);var tables=(NavigationSession.NavigationLiveArrivalTables)typeof(NavigationSession).GetField("liveArrivalTables",F).GetValue(session);Assert.Zero(tables.Count);Assert.Zero(tables.Ids.Capacity);Assert.Zero(tables.References.Capacity);Assert.AreEqual(1,session.Answers.Count);session.ApplyResults();Assert.Zero(session.LastOwnedDiscardPublicationAllocatedBytes);Assert.Zero(session.Answers.Count);Assert.AreEqual(members,session.RejectedResults);}
            try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};Complete();}finally{budget.AllocationAdmission=null;}Assert.Zero(underGate);
            var suffix=Bytes();var facts=HostFacts();Pump(1);var next=Bytes();var nextFacts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());kernel.Attach(session.RoutePort);typeof(CertifiedRouteKernel).GetMethod("RefreshPhysicalFrontier",F).Invoke(kernel,new object[]{session.RoutePort});Complete();CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [Test] public void TrackedCatalogRejectsUnderstatedDistinctAnswersAndForeignOwnerBeforeAllocation()
        {
            Assert.NotNull(session.Probe(7,1,activeProfile.Navigation,new NavPoint(-4,2),new NavPoint(4,2)));
            for(int i=0;i<20000&&session.Answers.Count==0;i++)PumpProbe(64);Assert.True(session.Answers.TryDequeue(out var answer));session.CancelProbe(7);
            var binding=new PlayableRouteBinding(session.Admission.Geometry,activeProfile,session.Admission,session.RoutePort);Assert.True(kernel.RetirePhysicalDelivered(binding));
            using(var state=session.CaptureOwnedState()){
                var source=state.CaptureAuthority;Assert.True(source.HasAnswerTracking);var owner=(NavigationHostAnswer)source.AnswerTrackingOwners[0];
                var layouts=new int[13][];for(int i=0;i<13;i++)layouts[i]=((NavRetainedArray<int>)source.CollectionCatalog[i]).ReadOnlyBacking;
                NavigationAuthorityOwners Catalog(INavStorageReservation r,int[] tracking,NavOwnedStorage[] owners)=>NavigationAuthorityOwners.CreateCatalogOwned(source,source.RequestCatalog.ReadOnlyBacking,source.GeometryCatalog.ReadOnlyBacking,source.GroupCatalog.ReadOnlyBacking,source.IdentityCatalog.ReadOnlyBacking,source.CrowdNames.ReadOnlyBacking,source.CrowdKeys.ReadOnlyBacking,source.CrowdPoints.ReadOnlyBacking,source.CrowdFlags.ReadOnlyBacking,source.RequestRoles.ReadOnlyBacking,layouts,source.TerrainNames,source.ActorGlobalCatalog.ReadOnlyBacking,source.ActorLocalCatalog.ReadOnlyBacking,source.InstalledCatalog.ReadOnlyBacking,r,source.ArrivalCatalog,source.LeaseCatalog,source.ProbeAnswerCatalog?.ReadOnlyBacking,source.MailboxAnswerCatalog?.ReadOnlyBacking,source.BarrierAnswerCatalog?.ReadOnlyBacking,tracking,owners);
                var budget=session.PhysicalHostBudget;Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var scope));
                using(scope){var duplicate=NavigationHostAnswer.CreateReady(owner.RequestOwner,owner.CorridorOwner,owner.Answer.ReadOnlyRouteBacking,scope.OwnedReservation());try{
                    Assert.AreNotSame(owner,duplicate);Assert.AreSame(owner.RequestOwner,duplicate.RequestOwner);
                    Assert.Throws<ArgumentException>(()=>Catalog(scope.OwnedReservation(),new[]{4,1,2,1,0,1,0},new NavOwnedStorage[]{owner,duplicate}));
                    var valid=Catalog(scope.OwnedReservation(),new[]{4,1,2,2,0,2,0},new NavOwnedStorage[]{owner,duplicate});try{Assert.AreEqual(2,valid.AnswerTrackingOwners.Count);Assert.AreNotSame(valid.AnswerTrackingOwners[0],valid.AnswerTrackingOwners[1]);}finally{valid.ReleaseStorage();}
                }finally{duplicate.ReleaseStorage();}}
                var foreignBudget=new NavigationRetentionBudget();NavigationHostRoot foreignHost=null;NavigationAuthorityOwners foreign=null;
                try{NavigationSession.DecodeDetachedHostAuthority(state.HostAuthority,foreignBudget,out foreignHost,out foreign);Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,2,out scope));using(scope){int callbacks=0;long charge=budget.PhysicalBytes;try{budget.AllocationAdmission=_=>{callbacks++;return true;};Assert.Throws<ArgumentException>(()=>Catalog(scope.OwnedReservation(),source.AnswerTrackingLayout.ReadOnlyBacking,new[]{foreign.AnswerTrackingOwners[0]}));}finally{budget.AllocationAdmission=null;}Assert.Zero(callbacks);Assert.AreEqual(charge,budget.PhysicalBytes);}}
                finally{foreignHost?.Release();foreign?.Release();Assert.Zero(foreignBudget.PhysicalBytes);}
            }
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [Test] public void ProducerProbeOwnedFifoPreservesColdSuffixAndNextPump()
        {
            // A Session producer has no PlayableDomain rally record. Exercise
            // the complete owned Session BODY here; actual rally validation is
            // covered by the separate actual-domain producer scenario.
            byte[] Bytes()=>ProbeBytes();void Pump(int budget)=>PumpProbe(budget);void Restore(byte[] bytes)=>RestoreProbeBytes(bytes);
            var p=activeProfile.Navigation;var sourceBudget=session.PhysicalHostBudget;
            Assert.NotNull(session.Probe(7,1,p,new NavPoint(-4,2),new NavPoint(4,2)));
            Assert.NotNull(session.Probe(8,2,p,new NavPoint(-4,4),new NavPoint(4,4)));
            session.CancelProbe(7);
            Assert.NotNull(session.Probe(9,3,p,new NavPoint(-4,6),new NavPoint(4,6)));
            Assert.AreEqual(4,session.OwnedProbePublications);Assert.Zero(session.LastOwnedProbePublicationAllocatedBytes);
            for(int i=0;i<20000&&session.OwnedIngressPublications==0;i++)Pump(1);Assert.Greater(session.OwnedIngressPublications,0);Assert.Zero(session.LastOwnedIngressPublicationAllocatedBytes);
            var prefix=Bytes();
            void Complete(){for(int i=0;i<20000&&session.PendingCount>0;i++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);Assert.Zero(session.LastOwnedProbeAnswerPublicationAllocatedBytes);}
            Complete();Assert.AreEqual(3,session.OwnedProbeAnswerPublications);Assert.AreEqual(1,session.RejectedResults);var completed=Bytes();
            Assert.True(session.TryProbeAnswer(out var first));Assert.Zero(session.LastOwnedProbeTakeAllocatedBytes);Assert.AreEqual(-8,first.Request.Entity);
            Assert.True(session.TryProbeAnswer(out var second));Assert.AreEqual(-9,second.Request.Entity);
            Assert.False(session.TryProbeAnswer(out _));var suffix=Bytes();var facts=HostFacts();Pump(1);var next=Bytes();var nextFacts=HostFacts();
            Restore(prefix);Assert.Zero(sourceBudget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Complete();CollectionAssert.AreEqual(completed,Bytes());
            Assert.True(session.TryProbeAnswer(out first));Assert.AreEqual(-8,first.Request.Entity);Assert.True(session.TryProbeAnswer(out second));Assert.AreEqual(-9,second.Request.Entity);Assert.False(session.TryProbeAnswer(out _));
            CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());
            Restore(completed);CollectionAssert.AreEqual(completed,Bytes());Assert.True(session.TryProbeAnswer(out first));Assert.AreEqual(-8,first.Request.Entity);Assert.True(session.TryProbeAnswer(out second));Assert.AreEqual(-9,second.Request.Entity);CollectionAssert.AreEqual(suffix,Bytes());
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)]
        public void LateOrdinaryAnswerDiscardsExactOwnerAndColdStepKeepsReplacement(bool reissue,bool applyOnly)
        {
            int entity=Spawn(-18,-14),walker=Spawn(-18,-10);session.CancelProbe(7);Assert.AreEqual(1,session.OwnedProbePublications);
            Assert.True(session.Move(walker,new NavPoint(-10,-10)));for(int i=0;i<20000&&session.Answers.Count==0;i++)Pump(64);Assert.AreEqual(1,session.Answers.Count);session.ApplyResults();Assert.True(session.Crowd.TryGet(walker,out var walking));Assert.True(walking.Moving);
            Assert.True(session.Move(entity,new NavPoint(-10,-14)));
            for(int i=0;i<20000&&session.Answers.Count==0;i++)Pump(64);
            Assert.AreEqual(1,session.Answers.Count);var obsolete=session.Answers.CopyItems()[0];Assert.NotNull(obsolete.StorageOwner);
            session.Stop(entity,false);if(reissue)Assert.True(session.Move(entity,new NavPoint(-8,-14)));
            var replacement=session.PendingRequestFor(entity);Assert.AreEqual(reissue,replacement!=null);Assert.AreNotSame(obsolete.Request,replacement);
            var budget=session.PhysicalHostBudget;var prefix=Bytes();long charge=budget.PhysicalBytes;int denied=0,underGate=0;
            var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);var type=typeof(NavigationSession).GetNestedType("PreparedDiscard",BindingFlags.NonPublic);
            void Consume(){if(applyOnly)session.ApplyResults();else session.Step(1d/30);}
            try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().DeclaringType==type)){denied++;return false;}return true;};Consume();}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.Zero(session.RejectedResults);Assert.Zero(session.OwnedDiscardPublications);Assert.AreSame(obsolete,session.Answers.CopyItems()[0]);Assert.AreSame(replacement,session.PendingRequestFor(entity));Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(prefix,Bytes());
            Assert.True(session.Crowd.TryGet(entity,out var actor));var position=actor.Position;var walkerPosition=walking.Position;var walkerRoute=walking.StorageOwner.Global;int notifications=0;session.RouteActivated+=_=>notifications++;
            Consume();Assert.AreEqual(1,session.OwnedDiscardPublications);Assert.Zero(session.LastOwnedDiscardPublicationAllocatedBytes);Assert.AreEqual(1,session.RejectedResults);Assert.Zero(notifications);Assert.Zero(session.Answers.Count);Assert.AreSame(replacement,session.PendingRequestFor(entity));Assert.AreEqual(position,actor.Position);Assert.False(actor.Moving);Assert.AreSame(walkerRoute,walking.StorageOwner.Global);if(applyOnly)Assert.AreEqual(walkerPosition,walking.Position);else Assert.AreNotEqual(walkerPosition,walking.Position);
            var suffix=Bytes();string facts=HostFacts();Pump(1);var next=Bytes();string nextFacts=HostFacts();
            Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());kernel.Attach(session.RoutePort);typeof(CertifiedRouteKernel).GetMethod("RefreshPhysicalFrontier",F).Invoke(kernel,new object[]{session.RoutePort});Consume();Assert.AreEqual(1,session.OwnedDiscardPublications);Assert.Zero(session.LastOwnedDiscardPublicationAllocatedBytes);CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [TestCase(false)][TestCase(true)] public void LateOrdinaryDiscardPreparationCannotOverwriteNewerProducerState(bool reentrant)
        {
            int entity=Spawn(-18,-14);session.CancelProbe(7);Assert.True(session.Move(entity,new NavPoint(-10,-14)));for(int i=0;i<20000&&session.Answers.Count==0;i++)Pump(64);Assert.AreEqual(1,session.Answers.Count);session.Stop(entity,false);
            var budget=session.PhysicalHostBudget;var before=Bytes();long charge=budget.PhysicalBytes;byte[] accepted=null;int observed=0,underGate=0,nestedUnderGate=0;bool nested=false;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            try{budget.AllocationAdmission=_=>{
                if(Monitor.IsEntered(gate)){if(nested)nestedUnderGate++;else underGate++;}
                if(observed==0){var frames=new System.Diagnostics.StackTrace().GetFrames();if(frames.Any(frame=>frame.GetMethod().DeclaringType==typeof(NavRetainedArray<NavOwnedStorage>))&&frames.Any(frame=>frame.GetMethod().Name=="TryPublishOwnedDiscard")){
                    observed++;if(!reentrant)return false;nested=true;try{Assert.True(session.Move(entity,new NavPoint(-8,-14)));accepted=Bytes();}finally{nested=false;}
                }}return true;
            };session.Step(1d/30);}finally{budget.AllocationAdmission=null;}
            TestContext.WriteLine("Discard preparation callbacks under gate="+underGate+"; injected command/capture callbacks under gate="+nestedUnderGate+" (capture staging remains open)");Assert.AreEqual(1,observed);Assert.Zero(underGate);Assert.Zero(session.OwnedDiscardPublications);Assert.Zero(session.RejectedResults);Assert.AreEqual(1,session.Answers.Count);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            if(reentrant){Assert.NotNull(accepted);CollectionAssert.AreEqual(accepted,Bytes());Assert.AreEqual(new NavPoint(-8,-14),session.PendingRequestFor(entity).Goal);}
            else{CollectionAssert.AreEqual(before,Bytes());Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Null(session.PendingRequestFor(entity));}
            session.Step(1d/30);Assert.AreEqual(1,session.OwnedDiscardPublications);Assert.Zero(session.LastOwnedDiscardPublicationAllocatedBytes);Assert.AreEqual(1,session.RejectedResults);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
        }

        [Test] public void MixedStepPublishesOrdinaryFailureReadyAndActualProducerWithHistoricalOwnerCold()
        {
            Call("AddCredits",PlayableOwner.Player,10000d);Call("BuildAt",1,2,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null);Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            var buildings=(IDictionary)Domain.GetField("buildings",F).GetValue(domain);int factory=0;foreach(DictionaryEntry pair in buildings)if((PlayableBuildingKind)pair.Value.GetType().GetField("Kind").GetValue(pair.Value)==PlayableBuildingKind.Factory)factory=(int)pair.Key;Assert.Greater(factory,0);
            int blocked=Spawn(-20,-14);Assert.True(session.Move(blocked,new NavPoint(-18,-14)));
            for(int tick=0;tick<300;tick++){Pump(64);Call("Step",1d/30);if(session.Crowd.TryGet(blocked,out var unit)&&unit.Outcome==NavigationOutcome.Arrived)break;}
            Assert.True(session.Crowd.TryGet(blocked,out var failed));Assert.AreEqual(NavigationOutcome.Arrived,failed.Outcome);var installed=(NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);var prior=installed.OwnerFor(blocked);Assert.NotNull(prior);
            Assert.AreEqual(PlayableCommandStatus.Accepted,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.SetRally,new[]{factory},new NavPoint(-8,10)),null));Call("AdvanceRally");for(int i=0;i<20000&&session.Answers.Count==0;i++)Pump(64);Assert.AreEqual(1,session.Answers.Count);Assert.Less(session.Answers.CopyItems()[0].Request.Entity,0);
            var origin=failed.Position;var holders=new[]{Spawn(origin.X+1.5,origin.Z),Spawn(origin.X-1.5,origin.Z),Spawn(origin.X,origin.Z+1.5),Spawn(origin.X,origin.Z-1.5)};int ready=Spawn(-18,-6);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Hold,holders,origin:PlayableOrderOrigin.Human),null));Assert.True(session.Move(blocked,new NavPoint(-10,-14)));Assert.True(session.Move(ready,new NavPoint(-10,-14)));
            for(int i=0;i<20000&&session.Answers.Count<3;i++)Pump(64);Assert.AreEqual(3,session.Answers.Count);Assert.AreSame(prior,installed.OwnerFor(blocked),"Historical owner must still be live before result publication.");var answers=session.Answers.CopyItems();var terminal=answers.Single(a=>a.Request.Entity==blocked);var good=answers.Single(a=>a.Request.Entity==ready);Assert.AreNotEqual(NavSolveStatus.Ready,terminal.Status);Assert.Null(terminal.Corridor);Assert.AreEqual(NavSolveStatus.Ready,good.Status);Assert.NotNull(good.Corridor);Assert.AreSame(terminal.Request,session.PendingRequestFor(blocked));Assert.AreSame(good.Request,session.PendingRequestFor(ready));
            var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);var events=new System.Collections.Generic.List<string>();Action<NavigationRequest> Activated=r=>{Assert.False(Monitor.IsEntered(gate));events.Add("ready:"+r.Entity);};Action<NavigationRequest,NavigationOutcome> Failed=(r,o)=>{Assert.False(Monitor.IsEntered(gate));events.Add("failure:"+r.Entity);};Action<int> Cancelled=id=>{Assert.False(Monitor.IsEntered(gate));events.Add("cancel:"+id);};session.RouteActivated+=Activated;session.RouteFailed+=Failed;session.MovementCancelled+=Cancelled;
            var expected=answers.Where(a=>a.Request.Entity>=0).SelectMany(a=>a.Status==NavSolveStatus.Ready?new[]{"ready:"+a.Request.Entity}:new[]{"failure:"+a.Request.Entity,"cancel:"+a.Request.Entity}).ToArray();var budget=session.PhysicalHostBudget;var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var before=frozen.Encode();long charge=budget.PhysicalBytes,mobility=failed.MobilityRevision;int denied=0,priorBatches=session.OwnedResultBatchPublications;var queueType=typeof(NavigationLiveProbeAnswers);
            try{
                try{budget.AllocationAdmission=_=>{if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==queueType)){denied++;return false;}return true;};session.Step(1d/30);}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied);Assert.IsEmpty(events);Assert.AreEqual(priorBatches,session.OwnedResultBatchPublications);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());
                session.Step(1d/30);Assert.AreEqual(priorBatches+1,session.OwnedResultBatchPublications);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);Assert.AreEqual(1,session.OwnedOrdinaryFailurePublications);CollectionAssert.AreEqual(expected,events);Assert.Null(session.PendingRequestFor(blocked));Assert.Null(session.PendingRequestFor(ready));Assert.Zero(session.Answers.Count);Assert.AreEqual(mobility+1,failed.MobilityRevision);Assert.False(failed.Moving);Assert.AreEqual(terminal.Request.Request,failed.FailedRequest);Assert.AreEqual(terminal.Request.GoalLocation,failed.FailedGoal);Assert.AreSame(prior,installed.OwnerFor(blocked));Assert.True(installed.IsDetached(blocked));Assert.True(session.Crowd.TryGet(ready,out var walking));Assert.True(walking.Moving);Assert.AreEqual(1,typeof(NavigationSession).GetProperty("ProbeAnswerCount",F).GetValue(session));CollectionAssert.AreEqual(before,frozen.Encode());PhysicalShape(frozen);frozen.Dispose();frozen=null;session.RouteActivated-=Activated;session.RouteFailed-=Failed;session.MovementCancelled-=Cancelled;
                TestContext.WriteLine("mixed failure physical="+budget.PhysicalBytes+" peak="+budget.PeakRetainedBytes);var prefix=Bytes();Pump(1);var suffix=Bytes();var facts=HostFacts();Pump(1);var next=Bytes();var nextFacts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Pump(1);CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;if(session!=null){session.RouteActivated-=Activated;session.RouteFailed-=Failed;session.MovementCancelled-=Cancelled;}frozen?.Dispose();}
        }
        [Test] public void MixedStepPublishesBothGroupMembersLateDiscardAndActualProducerTogether()
        {
            Call("AddCredits",PlayableOwner.Player,10000d);Call("BuildAt",1,2,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null);Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            var buildings=(IDictionary)Domain.GetField("buildings",F).GetValue(domain);int factory=0;foreach(DictionaryEntry pair in buildings)if((PlayableBuildingKind)pair.Value.GetType().GetField("Kind").GetValue(pair.Value)==PlayableBuildingKind.Factory)factory=(int)pair.Key;Assert.Greater(factory,0);
            int a=Spawn(-18,-14),b=Spawn(-18,-10),obsolete=Spawn(-18,-6);session.CancelProbe(7);Assert.True(session.Move(obsolete,new NavPoint(-10,-6)));for(int i=0;i<20000&&session.Answers.Count==0;i++)Pump(64);Assert.AreEqual(1,session.Answers.Count);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Move,new[]{a,b},new NavPoint(-10,-12),origin:PlayableOrderOrigin.Human),null));
            for(int i=0;i<20000&&session.Answers.Count<3;i++){Pump(64);session.PrepareDeliveryBarrier();}Assert.AreEqual(3,session.Answers.Count);Assert.True(session.DeliveryBarrierReady);
            var barrier=(NavigationSession.NavigationBarrierAnswers)typeof(NavigationSession).GetField("barrierAnswers",F).GetValue(session);Assert.AreEqual(3,barrier.Count);barrier.Reverse();var expected=barrier.Where(answer=>answer.Request.Entity!=obsolete).Select(answer=>answer.Request.Request).OrderBy(value=>value).ToArray();
            Assert.AreEqual(PlayableCommandStatus.Accepted,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.SetRally,new[]{factory},new NavPoint(-8,10)),null));Call("AdvanceRally");for(int i=0;i<20000&&session.Answers.Count==0;i++)Pump(64);Assert.AreEqual(1,session.Answers.Count);Assert.Less(session.Answers.CopyItems()[0].Request.Entity,0);
            session.Stop(obsolete,false);Assert.True(session.Move(obsolete,new NavPoint(-8,-6)));var replacement=session.PendingRequestFor(obsolete);Assert.NotNull(replacement);Assert.True(session.Crowd.TryGet(a,out var first));Assert.True(session.Crowd.TryGet(b,out var second));var firstPosition=first.Position;var secondPosition=second.Position;
            foreach(var actor in new[]{first,second}){var request=barrier.Single(answer=>answer.Request.Entity==actor.Id).Request;Assert.True(request.Geometry.SegmentFree(actor.Position,request.Goal,actor.Radius));actor.Heading=Math.Atan2(request.Goal.Z-actor.Position.Z,request.Goal.X-actor.Position.X);}
            var budget=session.PhysicalHostBudget;var prefix=Bytes();long charge=budget.PhysicalBytes;var fifo=typeof(NavigationLiveProbeAnswers);int denied=0,underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().DeclaringType==fifo)){denied++;return false;}return true;};session.Step(1d/30);}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.Zero(session.OwnedResultBatchPublications);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(prefix,Bytes());Assert.AreSame(replacement,session.PendingRequestFor(obsolete));Assert.False(first.Moving);Assert.False(second.Moving);
            session.MovementDiagnosticObserver=_=>TestContext.WriteLine(session.LastOwnedMovementComputation);
            var order=new System.Collections.Generic.List<long>();long operation=(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session);long publication=session.Admission.PublicationIdentity;
            session.RouteActivated+=request=>{Assert.False(Monitor.IsEntered(gate));Assert.AreEqual(operation+4,(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session));Assert.AreEqual(publication+1,session.Admission.PublicationIdentity);order.Add(request.Request);};
            session.Step(1d/30);Assert.AreEqual(1,session.OwnedResultBatchPublications);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);CollectionAssert.AreEqual(expected,order);Assert.AreEqual(2,session.AppliedResults);Assert.AreEqual(1,session.RejectedResults);Assert.AreSame(replacement,session.PendingRequestFor(obsolete));Assert.AreNotEqual(firstPosition,first.Position);Assert.AreNotEqual(secondPosition,second.Position);Assert.True(first.Moving);Assert.True(second.Moving);
            var group=session.GroupFor(a);Assert.AreEqual(group.GroupId,session.GroupFor(b).GroupId);foreach(var member in group.Members)Assert.AreEqual(GroupMemberPhase.March,member.Phase);Assert.AreEqual(0,barrier.Count);Assert.Zero(session.Answers.Count);
            var published=Bytes();Call("AdvanceRally");var suffix=Bytes();var facts=HostFacts();Pump(1);var next=Bytes();var nextFacts=HostFacts();
            Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());kernel.Attach(session.RoutePort);typeof(CertifiedRouteKernel).GetMethod("RefreshPhysicalFrontier",F).Invoke(kernel,new object[]{session.RoutePort});session.Step(1d/30);Assert.AreEqual(1,session.OwnedResultBatchPublications);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);CollectionAssert.AreEqual(published,Bytes());Call("AdvanceRally");CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [TestCase("request")][TestCase("admission")][TestCase("remove")][TestCase("fifo")][TestCase("mixed")][TestCase("capacity")][TestCase("speed")] public void MovedSessionReadyReissuesFromCurrentPositionWithoutDiscardingOldMotionCold(string site)
        {
            // Public Session contract. AuthorityTick deliberately freezes at its
            // route barrier, so this does not claim a native issuing-tick producer.
            kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");session=new NavigationSession(1,new NavGeometry(30,new[]{new NavObstacle(-3,-3,3,3)},1),activeProfile.Navigation);Domain.GetField("navigation",F).SetValue(domain,session);kernel=new CertifiedRouteKernel();var actor=session.Crowd.Add(1,new NavPoint(-10,-6));session.CancelProbe(7);Assert.True(session.Move(1,new NavPoint(10,-6)));
            for(int i=0;i<20000&&session.Answers.Count==0;i++)PumpProbe(64);Assert.AreEqual(1,session.Answers.Count);session.ApplyResults();Assert.True(actor.Moving);var global=actor.StorageOwner.Global;var installed=(NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);var old=installed.OwnerFor(1);
            Assert.True(session.Move(1,new NavPoint(-10,6)));for(int i=0;i<20000&&session.Answers.Count==0;i++)PumpProbe(64);Assert.AreEqual(1,session.Answers.Count);Assert.True(session.Answers.TryDequeue(out var answer));((NavigationTransportOwner)typeof(NavigationSession).GetField("transportOwner",F).GetValue(session)).Consume(answer.Request);Assert.AreEqual(NavSolveStatus.Ready,answer.Status);
            for(int i=0;i<1000&&actor.Moving&&answer.Request.Geometry.SegmentFree(actor.Position,answer.ReadOnlyRouteBacking[0],actor.Radius);i++)session.Step(1d/30);
            Assert.True(actor.Moving);Assert.False(answer.Request.Geometry.SegmentFree(actor.Position,answer.ReadOnlyRouteBacking[0],actor.Radius));Assert.AreSame(global,actor.StorageOwner.Global);Assert.AreSame(old,installed.OwnerFor(1));
            bool mixed=site=="mixed";if(mixed||site=="fifo"){session.Crowd.Add(2,new NavPoint(-12,12));Assert.True(session.Move(2,new NavPoint(10,12)));if(mixed){for(int i=0;i<20000&&session.Answers.Count==0;i++)PumpProbe(64);Assert.AreEqual(1,session.Answers.Count);Assert.AreEqual(2,session.Answers.CopyItems()[0].Request.Entity);}}
            Assert.True(session.Answers.TryEnqueue(answer));var tracking=(NavigationCommandSlots<NavigationAnswer,NavigationHostAnswer>)typeof(NavigationSession).GetField("ownedAnswerTracking",F).GetValue(session);Assert.AreSame(answer.StorageOwner,tracking.OwnerFor(answer));Assert.AreEqual(1,answer.Request.StorageOwner.TransportAnswers);
            if(site=="capacity")for(int i=0;i<NavigationSession.RouteRequestCapacity;i++)Assert.True(session.Requests.TryEnqueue(answer.Request));
            var budget=session.PhysicalHostBudget;var queuedBefore=session.Requests.CopyItems();long sequence=(long)typeof(NavigationSession).GetField("requestSequence",F).GetValue(session);var prefix=ProbeBytes();var position=actor.Position;var previous=answer.Request;bool detached=installed.IsDetached(1);int ready=session.OwnedReadyPublications,batch=session.OwnedResultBatchPublications,denied=0,underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            using(var frozen=session.CaptureOwnedState()){
                var frozenBytes=WorldWire.Pack(w=>WorldWire.Write(w,frozen));long physical=budget.PhysicalBytes;
                if(site=="speed"){
                    bool changed=false;double speed=actor.Speed*.75;long sourceOperation=(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session);var admission=session.Admission;
                    try{budget.AllocationAdmission=_=>{var frames=new System.Diagnostics.StackTrace().GetFrames();if(!changed&&frames.Any(f=>f.GetMethod().Name=="CreateForUnitOwned")&&frames.Any(f=>f.GetMethod().Name=="PrepareMovedRejectedReady")){changed=true;Assert.False(Monitor.IsEntered(gate));session.Crowd.SetSpeed(1,speed);Assert.AreEqual(sourceOperation,typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session));}return true;};session.Step(1d/30);}finally{budget.AllocationAdmission=null;}
                    Assert.True(changed);Assert.AreEqual(sourceOperation,typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session));Assert.AreEqual(speed,actor.Speed);Assert.AreEqual(position,actor.Position);Assert.AreSame(admission,session.Admission);Assert.AreSame(previous,session.PendingRequestFor(1));Assert.AreSame(answer,session.Answers.CopyItems().Single());Assert.AreSame(global,actor.StorageOwner.Global);Assert.AreSame(old,installed.OwnerFor(1));Assert.AreEqual(batch,session.OwnedResultBatchPublications);Assert.AreEqual(physical,budget.PhysicalBytes);CollectionAssert.AreEqual(frozenBytes,WorldWire.Pack(w=>WorldWire.Write(w,frozen)));Assert.Zero(budget.ActiveScopes);Assert.Zero(budget.HeldBytes);return;
                }
                if(site=="remove"){
                    byte[] removed=null;bool changed=false;
                    try{budget.AllocationAdmission=_=>{var frames=new System.Diagnostics.StackTrace().GetFrames();if(!changed&&frames.Any(f=>f.GetMethod().Name=="CreateForUnitOwned")&&frames.Any(f=>f.GetMethod().Name=="PrepareMovedRejectedReady")){changed=true;session.Remove(1);removed=ProbeBytes();}return true;};session.Step(1d/30);}finally{budget.AllocationAdmission=null;}
                    Assert.True(changed);Assert.NotNull(removed);Assert.False(session.Crowd.TryGet(1,out _));Assert.AreEqual(batch,session.OwnedResultBatchPublications);CollectionAssert.AreEqual(removed,ProbeBytes());CollectionAssert.AreEqual(frozenBytes,WorldWire.Pack(w=>WorldWire.Write(w,frozen)));Assert.Zero(budget.ActiveScopes);Assert.Zero(budget.HeldBytes);return;
                }
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;var frames=new System.Diagnostics.StackTrace().GetFrames();if(site=="request"?frames.Any(f=>f.GetMethod().DeclaringType==typeof(NavigationHostRequest)&&f.GetMethod().Name=="CreateOwned"):frames.Any(f=>f.GetMethod().Name=="PrepareAdmissionProjection")){denied++;return false;}return true;};session.Step(1d/30);}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreEqual(physical,budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,ProbeBytes());Assert.AreSame(previous,session.PendingRequestFor(1));Assert.AreSame(answer,session.Answers.CopyItems().Last());Assert.AreEqual(position,actor.Position);Assert.AreEqual(batch,session.OwnedResultBatchPublications);
                int requested=0,failed=0,cancelled=0,activated=0;var callbacks=new System.Collections.Generic.List<string>();long operation=(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session);session.RouteActivated+=_=>{activated++;callbacks.Add("ready");};session.RouteFailed+=(_,__)=>{failed++;callbacks.Add("failure");};session.MovementCancelled+=_=>{cancelled++;callbacks.Add("cancel");};session.RouteRequested+=request=>{requested++;callbacks.Add("request");Assert.False(Monitor.IsEntered(gate));Assert.Zero(budget.ActiveScopes);Assert.AreEqual(operation+(site=="capacity"?5:6),typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session));Assert.AreSame(request,session.PendingRequestFor(1));Assert.AreEqual(position,request.Start);};
                session.Step(1d/30);Assert.AreEqual(site=="capacity"?0:1,requested);Assert.AreEqual(site=="capacity"?1:0,failed);Assert.AreEqual(site=="capacity"?1:0,cancelled);Assert.AreEqual(operation+(site=="capacity"?5:6),typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session));Assert.AreEqual(batch+1,session.OwnedResultBatchPublications);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);Assert.Zero(session.LastOwnedMovementPublicationAllocatedBytes);Assert.AreEqual(ready+(mixed?1:0),session.OwnedReadyPublications);Assert.AreEqual(mixed?1:0,activated);CollectionAssert.AreEqual(site=="capacity"?new[]{"failure","cancel"}:mixed?new[]{"ready","request"}:new[]{"request"},callbacks);Assert.AreEqual(1,session.RejectedResults);Assert.Null(session.LastCorridorFailure);Assert.AreSame(global,actor.StorageOwner.Global);Assert.AreSame(old,installed.OwnerFor(1));Assert.AreEqual(detached,installed.IsDetached(1));Assert.True(actor.Moving);Assert.AreNotEqual(position,actor.Position);
                var replacement=session.PendingRequestFor(1);if(site=="capacity"){Assert.Null(replacement);Assert.AreEqual(NavigationOutcome.Rejected,actor.LastFailure);CollectionAssert.AreEqual(queuedBefore,session.Requests.CopyItems());}else{Assert.AreEqual(sequence+1,replacement.Request);Assert.AreEqual(previous.Order+1,replacement.Order);Assert.AreEqual(previous.MemberIdentity.GroupId,replacement.MemberIdentity.GroupId);Assert.AreEqual(previous.MemberIdentity.OrderRevision,replacement.MemberIdentity.OrderRevision);Assert.AreEqual(previous.Goal,replacement.Goal);CollectionAssert.AreEqual(queuedBefore.Concat(new[]{replacement}).ToArray(),session.Requests.CopyItems());}CollectionAssert.AreEqual(frozenBytes,WorldWire.Pack(w=>WorldWire.Write(w,frozen)));
            }
            var suffix=ProbeBytes();var facts=HostFacts();PumpProbe(4);var next=ProbeBytes();var nextFacts=HostFacts();RestoreProbeBytes(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,ProbeBytes());kernel.Attach(session.RoutePort);typeof(CertifiedRouteKernel).GetMethod("RefreshPhysicalFrontier",F).Invoke(kernel,new object[]{session.RoutePort});session.Step(1d/30);CollectionAssert.AreEqual(suffix,ProbeBytes());Assert.AreEqual(facts,HostFacts());PumpProbe(4);CollectionAssert.AreEqual(next,ProbeBytes());Assert.AreEqual(nextFacts,HostFacts());RestoreProbeBytes(suffix);CollectionAssert.AreEqual(suffix,ProbeBytes());PumpProbe(4);CollectionAssert.AreEqual(next,ProbeBytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [TestCase(false)][TestCase(true)] public void BarrierReceiptContainerPublishesFirstAndGrowthWithoutBackingAllocationCold(bool refuseGrowth)
        {
            int first=Spawn(-18,-14),second=Spawn(-18,-8);var budget=session.PhysicalHostBudget;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            for(int stage=0;stage<2;stage++){
                int entity=stage==0?first:second;Assert.True(session.Move(entity,new NavPoint(-10,stage==0?-14:-8)));for(int i=0;i<20000&&session.Answers.Count==0;i++)Pump(4);Assert.AreEqual(1,session.Answers.Count);
                if((stage==1)==refuseGrowth){
                    var prefix=Bytes();long physical=budget.PhysicalBytes;int denied=0,underGate=0;try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationBarrierReceipt))){denied++;return false;}return true;};Assert.False(session.DeliveryBarrierReady);}finally{budget.AllocationAdmission=null;}
                    Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreEqual(physical,budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Assert.AreEqual(1,session.Answers.Count);Assert.AreEqual(stage,((IList)typeof(NavigationSession).GetField("barrierAnswers",F).GetValue(session)).Count);Assert.Zero(budget.ActiveScopes);Assert.Zero(budget.HeldBytes);
                }
                int callbacks=0;try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))callbacks++;return true;};Assert.True(session.DeliveryBarrierReady);}finally{budget.AllocationAdmission=null;}
                Assert.Zero(callbacks);Assert.Zero(session.LastOwnedBarrierHandoffAllocatedBytes);Assert.AreEqual(stage+1,((IList)typeof(NavigationSession).GetField("barrierAnswers",F).GetValue(session)).Count);Assert.Zero(session.Answers.Count);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
            }
            var full=Bytes();byte[] Consume(){session.Step(1d/30);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);Assert.Zero(session.PendingCount);Assert.AreEqual(2,session.AppliedResults);Assert.Zero(((IList)typeof(NavigationSession).GetField("barrierAnswers",F).GetValue(session)).Count);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);return Bytes();}
            var suffix=Consume();Pump(4);var next=Bytes();var facts=HostFacts();Restore(full);CollectionAssert.AreEqual(full,Bytes());CollectionAssert.AreEqual(suffix,Consume());Pump(4);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(facts,HostFacts());
        }

        [TestCase("request",false)][TestCase("admission",false)][TestCase("mixed",false)][TestCase("request",true)][TestCase("admission",true)][TestCase("mixed",true)] public void ActualStepHeldPrefixReissuesBeforeConsumingHistoricalReadyCold(string site,bool applyOnly)
        {
            int factory=0;if(site=="mixed"){
                Call("AddCredits",PlayableOwner.Player,10000d);Call("BuildAt",1,2,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null);Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
                foreach(DictionaryEntry pair in (IDictionary)Domain.GetField("buildings",F).GetValue(domain))if((PlayableBuildingKind)pair.Value.GetType().GetField("Kind").GetValue(pair.Value)==PlayableBuildingKind.Factory)factory=(int)pair.Key;Assert.Greater(factory,0);
            }
            int moving=Spawn(-18,-14),holder=Spawn(-8,10);Arrival(moving,new NavPoint(-10,-14));
            for(int i=0;i<20000&&(session.PendingCount>0||session.AllocationCount>0);i++){Pump(4);session.ApplyResults();}
            Assert.True(session.Crowd.TryGet(moving,out var actor));Assert.True(actor.Moving);
            if(site=="mixed"){Assert.AreEqual(PlayableCommandStatus.Accepted,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.SetRally,new[]{factory},new NavPoint(-8,10)),null));Call("AdvanceRally");}
            Assert.True(session.Move(moving,actor.Goal));
            int answers=site=="mixed"?2:1;for(int i=0;i<20000&&session.Answers.Count<answers;i++)Pump(4);Assert.AreEqual(answers,session.Answers.Count);Assert.True(session.DeliveryBarrierReady);Assert.Zero(session.Answers.Count);
            var old=session.PendingRequestFor(moving);Assert.NotNull(old);Assert.AreEqual(NavSolveStatus.Ready,((NavigationSession.NavigationBarrierAnswers)typeof(NavigationSession).GetField("barrierAnswers",F).GetValue(session)).Single(a=>a.Request.Entity==moving).Status);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Hold,new[]{holder},origin:PlayableOrderOrigin.Human),null));
            void ConsumeResults(){if(applyOnly)session.ApplyResults();else session.Step(1d/30);}
            var prefix=Bytes();var budget=session.PhysicalHostBudget;long physical=budget.PhysicalBytes;int denied=0,underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;var frames=new System.Diagnostics.StackTrace().GetFrames();bool refuse=site=="request"?frames.Any(f=>f.GetMethod().DeclaringType==typeof(NavigationHostRequest)&&f.GetMethod().Name=="CreateOwned"):frames.Any(f=>f.GetMethod().Name=="PrepareAdmissionProjection");if(refuse){denied++;return false;}return true;};ConsumeResults();}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreEqual(physical,budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Assert.AreSame(old,session.PendingRequestFor(moving));Assert.Zero(budget.ActiveScopes);Assert.Zero(budget.HeldBytes);
            byte[] Complete(){
                Assert.True(session.Crowd.TryGet(moving,out var a));var previous=session.PendingRequestFor(moving);var position=a.Position;long mobility=a.MobilityRevision,op=(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session),publication=session.Admission.PublicationIdentity;int batch=session.OwnedResultBatchPublications,rejected=session.RejectedResults,requested=0,activated=0;var currentBudget=session.PhysicalHostBudget;
                void Requested(NavigationRequest r){Assert.AreEqual(moving,r.Entity);requested++;Assert.False(Monitor.IsEntered(typeof(NavigationSession).GetField("transportGate",F).GetValue(session)));Assert.Zero(currentBudget.ActiveScopes);Assert.AreEqual(op+(applyOnly?5:6),typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session));Assert.AreSame(r,session.PendingRequestFor(moving));Assert.AreEqual(position,r.Start);}
                void Activated(NavigationRequest r){if(r.Entity==moving)activated++;}session.RouteRequested+=Requested;session.RouteActivated+=Activated;try{ConsumeResults();}finally{session.RouteRequested-=Requested;session.RouteActivated-=Activated;}
                Assert.AreEqual(1,requested);Assert.Zero(activated);Assert.AreEqual(batch+1,session.OwnedResultBatchPublications);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);if(!applyOnly)Assert.Zero(session.LastOwnedMovementPublicationAllocatedBytes);Assert.AreEqual(rejected+1,session.RejectedResults);Assert.AreEqual(position,a.Position);Assert.False(a.Moving);Assert.AreEqual(mobility+1,a.MobilityRevision);Assert.AreEqual(NavigationOutcome.Stopped,a.Outcome);Assert.Null(a.StorageOwner.Global);Assert.AreEqual(publication+1,session.Admission.PublicationIdentity);
                var replacement=session.PendingRequestFor(moving);Assert.AreEqual(previous.Request+1,replacement.Request);Assert.AreEqual(previous.Order+1,replacement.Order);Assert.AreEqual(mobility+1,replacement.MemberIdentity.MobilityRevision);Assert.AreEqual(previous.MemberIdentity.OrderRevision,replacement.MemberIdentity.OrderRevision);Assert.AreEqual(1,session.Requests.Count);Assert.Zero(((System.Collections.IList)typeof(NavigationSession).GetField("barrierAnswers",F).GetValue(session)).Count);
                if(site=="mixed"){Assert.AreEqual(1,typeof(NavigationSession).GetProperty("ProbeAnswerCount",F).GetValue(session));Call("AdvanceRally");Assert.AreEqual(0,typeof(NavigationSession).GetProperty("ProbeAnswerCount",F).GetValue(session));}
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),currentBudget.PhysicalBytes);return Bytes();
            }
            var suffix=Complete();Pump(4);var next=Bytes();var facts=HostFacts();Restore(prefix);CollectionAssert.AreEqual(prefix,Bytes());CollectionAssert.AreEqual(suffix,Complete(),"held prefix/answer/movement cold suffix");Pump(4);CollectionAssert.AreEqual(next,Bytes(),"next full Pump");Assert.AreEqual(facts,HostFacts());
        }

        [TestCase(false)][TestCase(true)] public void ActualResultHeldCapacityRetainsIntentButDiscardsOldMobilityCold(bool applyOnly)
        {
            int moving=Spawn(-18,-14),holder=Spawn(-8,10);Arrival(moving,new NavPoint(-10,-14));for(int i=0;i<20000&&(session.PendingCount>0||session.AllocationCount>0);i++){Pump(4);session.ApplyResults();}
            Assert.True(session.Crowd.TryGet(moving,out var actor));Assert.True(actor.Moving);Assert.True(session.Move(moving,actor.Goal));for(int i=0;i<20000&&session.Answers.Count==0;i++)Pump(4);Assert.AreEqual(1,session.Answers.Count);Assert.AreEqual(NavSolveStatus.Ready,session.Answers.CopyItems()[0].Status);Assert.True(session.DeliveryBarrierReady);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Hold,new[]{holder},origin:PlayableOrderOrigin.Human),null));var request=session.PendingRequestFor(moving);
            for(int i=0;i<4096;i++){
                Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,session.Generation,session.Admission.PublicationIdentity,out var input));using(input){NavigationHostRequest stale=null;try{stale=NavigationHostRequest.CreateOwned(session.Generation+1,i+1,request.Entity,i+1,request.Profile,request.Geometry,request.Start,request.Goal,request.BaseGeometry,request.HeldIdentity,request.MemberIdentity,request.StartLocation,request.GoalLocation,null,input.OwnedReservation());input.Attach(stale);input.Commit(stale,_=>Assert.True(session.Requests.TryEnqueue(stale.Request)));}finally{stale?.ReleaseStorage();}}}
            void Consume(){if(applyOnly)session.ApplyResults();else session.Step(1d/30);}
            var prefix=Bytes();var budget=session.PhysicalHostBudget;long physical=budget.PhysicalBytes;int denied=0,underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().Name=="PrepareAdmissionProjection")){denied++;return false;}return true;};Consume();}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreEqual(physical,budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());
            byte[] Complete(){
                Assert.True(session.Crowd.TryGet(moving,out var a));var previous=session.PendingRequestFor(moving);var goals=typeof(NavigationSession).GetField("retainedGoals",F).GetValue(session);var reservations=typeof(NavigationSession).GetField("reservations",F).GetValue(session);var identities=typeof(NavigationSession).GetField("movementIdentities",F).GetValue(session);var position=a.Position;long mobility=a.MobilityRevision,op=(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session),publication=session.Admission.PublicationIdentity;int rejected=session.RejectedResults,batch=session.OwnedResultBatchPublications,cancelled=0,requested=0,activated=0;
                void Cancelled(int id){Assert.AreEqual(moving,id);cancelled++;Assert.False(Monitor.IsEntered(typeof(NavigationSession).GetField("transportGate",F).GetValue(session)));Assert.Zero(session.PhysicalHostBudget.ActiveScopes);Assert.AreEqual(op+(applyOnly?4:5),typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session));}void Requested(NavigationRequest _){requested++;}void Activated(NavigationRequest _){activated++;}
                session.MovementCancelled+=Cancelled;session.RouteRequested+=Requested;session.RouteActivated+=Activated;try{Consume();}finally{session.MovementCancelled-=Cancelled;session.RouteRequested-=Requested;session.RouteActivated-=Activated;}
                Assert.AreEqual(1,cancelled);Assert.Zero(requested);Assert.Zero(activated);Assert.AreEqual(batch+1,session.OwnedResultBatchPublications);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);if(!applyOnly)Assert.Zero(session.LastOwnedMovementPublicationAllocatedBytes);Assert.AreEqual(rejected+1,session.RejectedResults);Assert.AreEqual(position,a.Position);Assert.AreEqual(mobility+1,a.MobilityRevision);Assert.False(a.Moving);Assert.AreEqual(NavigationOutcome.Stopped,a.Outcome);Assert.Null(a.StorageOwner.Global);Assert.AreSame(previous,session.PendingRequestFor(moving));Assert.AreSame(goals,typeof(NavigationSession).GetField("retainedGoals",F).GetValue(session));Assert.AreSame(reservations,typeof(NavigationSession).GetField("reservations",F).GetValue(session));Assert.AreSame(identities,typeof(NavigationSession).GetField("movementIdentities",F).GetValue(session));Assert.AreEqual(publication+(applyOnly?0:1),session.Admission.PublicationIdentity);Assert.AreEqual(4096,session.Requests.Count);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);return Bytes();
            }
            var suffix=Complete();Pump(4);var next=Bytes();var facts=HostFacts();Restore(prefix);CollectionAssert.AreEqual(prefix,Bytes());CollectionAssert.AreEqual(suffix,Complete());Pump(4);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(facts,HostFacts());Restore(suffix);CollectionAssert.AreEqual(suffix,Bytes());Pump(4);CollectionAssert.AreEqual(next,Bytes());
        }

        [TestCase("request",true)][TestCase("admission",true)][TestCase("two",true)]
        [TestCase("request",false)][TestCase("admission",false)][TestCase("two",false)]
        public void ActualAuthorityHeldBarrierRefusesThenReissuesAndRestoresPendingPhaseCold(string site,bool legacyV1)
        {
            int moving=Spawn(-18,-14),holder=Spawn(-8,10);int second=site=="two"?Spawn(-14,-6):0;int count=second==0?1:2;if(second==0)Arrival(moving,new NavPoint(-10,-14));else Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Move,new[]{moving,second},new NavPoint(-10,-10),origin:PlayableOrderOrigin.Human),null));
            for(int i=0;i<20000&&(session.PendingCount>0||session.AllocationCount>0);i++){Pump(4);session.ApplyResults();}
            Assert.Zero(session.PendingCount);Assert.Zero(session.AllocationCount);Assert.True(session.Crowd.TryGet(moving,out var actor));Assert.True(actor.Moving);
            var initialPosition=actor.Position;for(int t=0;t<120&&actor.Position.Equals(initialPosition);t++)Call("Step",1d/30);Assert.AreNotEqual(initialPosition,actor.Position,"preflight requires actual motion, including normal steering/formation startup");var authority=new PlayableAuthorityTick((PlayableDomain)domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
            if(legacyV1){typeof(PlayableAuthorityTick).GetField("tickQuanta",F).SetValue(authority,false);typeof(PlayableAuthorityTick).GetMethod("SetRouteDeliveryPolicy",F).Invoke(authority,new object[]{false});}
            var idle=authority.CaptureBytes();Assert.AreEqual(15,BitConverter.ToInt32(PlayableWorldState.Decode(idle).AiAuthority,4),"ordinary authority uses current inner and outer schema v15");
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Hold,new[]{holder},origin:PlayableOrderOrigin.Human)).Status);
            var global=actor.StorageOwner.Global;var installed=(NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);var old=installed.OwnerFor(moving);var position=actor.Position;long mobility=actor.MobilityRevision,tick=authority.Tick;var admission=session.Admission;var budget=session.PhysicalHostBudget;long physical=budget.PhysicalBytes;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);int denied=0,underGate=0;
            try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;var frames=new System.Diagnostics.StackTrace().GetFrames();bool reject=frames.Any(f=>f.GetMethod().DeclaringType==(site=="request"?typeof(NavigationHostRequest):typeof(NavigationAdmissionStorage)));if(reject){denied++;return false;}return true;};Assert.False(authority.TryAdvance());}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.True(authority.AwaitingRoutes);Assert.True(authority.MaintenancePendingFor(session));Assert.AreEqual(tick,authority.Tick);Assert.AreEqual(position,actor.Position);Assert.True(actor.Moving);Assert.AreEqual(mobility,actor.MobilityRevision);Assert.AreSame(global,actor.StorageOwner.Global);Assert.AreSame(old,installed.OwnerFor(moving));Assert.False(installed.IsDetached(moving));Assert.AreSame(admission,session.Admission);Assert.Zero(session.PendingCount);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            byte[] pending=authority.CaptureBytes();Assert.AreEqual(14,BitConverter.ToInt32(PlayableWorldState.Decode(pending).AiAuthority,4));
            void RestoreAuthority(byte[] bytes){kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");authority=PlayableAuthorityTick.RestoreBytes(bytes,activeProfile,7,PlayableAiOpeningComposition.SourceIdentity,null,AiProfile.Initial,Array.Empty<string>());domain=authority.Domain;session=authority.Domain.Navigation;kernel=new CertifiedRouteKernel();}
            byte[] Complete(){
                Assert.True(session.Crowd.TryGet(moving,out var a));var start=a.Position;long m=a.MobilityRevision,op=(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session),publication=session.Admission.PublicationIdentity;var snapshot=session.Admission.Groups.Single(g=>g.Members.Any(member=>member.Entity==moving));long revision=snapshot.Members.Single(member=>member.Entity==moving).ActivationRevision;int requested=0,cancelled=0;byte[] callback=null;
                void Requested(NavigationRequest r){if(r.Entity!=moving&&r.Entity!=second)return;requested++;if(r.Entity!=moving)return;Assert.False(Monitor.IsEntered(typeof(NavigationSession).GetField("transportGate",F).GetValue(session)));Assert.False(authority.MaintenancePendingFor(session));Assert.Zero(session.PhysicalHostBudget.ActiveScopes);Assert.AreEqual(start,r.Start);Assert.AreEqual(m+1,r.MemberIdentity.MobilityRevision);Assert.AreEqual(revision,r.MemberIdentity.OrderRevision);Assert.AreEqual(op+count+1,typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session));Assert.AreEqual(publication+count,session.Admission.PublicationIdentity);Assert.Greater(r.Geometry.Obstacles.Count,session.Admission.Geometry.Obstacles.Count);callback=authority.CaptureBytes();}
                void Cancelled(int _){cancelled++;}session.RouteRequested+=Requested;session.MovementCancelled+=Cancelled;try{Assert.False(authority.TryAdvance());}finally{session.RouteRequested-=Requested;session.MovementCancelled-=Cancelled;}
                Assert.AreEqual(count,requested);Assert.Zero(cancelled);Assert.NotNull(callback);Assert.AreEqual(tick,authority.Tick);Assert.True(authority.AwaitingRoutes);Assert.False(authority.MaintenancePendingFor(session));Assert.AreEqual(start,a.Position);Assert.False(a.Moving);Assert.AreEqual(m+1,a.MobilityRevision);Assert.Null(a.StorageOwner.Global);Assert.Zero(session.LastOwnedHeldMaintenancePublicationAllocatedBytes);Assert.AreEqual(count,session.PendingCount);Assert.AreEqual(count,session.Requests.Count);Assert.AreEqual(15,BitConverter.ToInt32(PlayableWorldState.Decode(callback).AiAuthority,4),"completed waiting uses current ordinary true encoding");
                long completedOp=(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session);Assert.False(authority.TryAdvance());Assert.AreEqual(completedOp,typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session));return authority.CaptureBytes();
            }
            byte[] waiting=Complete();RestoreAuthority(pending);Assert.True(authority.MaintenancePendingFor(session));CollectionAssert.AreEqual(pending,authority.CaptureBytes(),"pending prefix");byte[] coldWaiting=Complete();CollectionAssert.AreEqual(waiting,coldWaiting,"waiting prefix");
            byte[] Finish(){
                if(legacyV1){for(int i=0;i<20000&&!session.DeliveryBarrierReady;i++)Pump(4);Assert.True(session.DeliveryBarrierReady);Assert.True(authority.TryAdvance());Assert.AreEqual(tick+1,authority.Tick);Assert.False(authority.AwaitingRoutes);Assert.True(session.Crowd.TryGet(moving,out var a));Assert.True(a.Moving);Assert.AreNotEqual(position,a.Position);}
                else{int advanced=0;long progressedTick=tick;bool moved=false;for(int i=0;i<20000&&(!session.DeliveryBarrierReady||advanced==0||authority.AwaitingRoutes);i++){if(authority.TryAdvance()){advanced++;Assert.AreEqual(progressedTick+1,authority.Tick,"each current V2 grant advances one fixed world tick");progressedTick=authority.Tick;Assert.True(session.Crowd.TryGet(moving,out var stepped));moved|=!position.Equals(stepped.Position);}Pump(4);}Assert.True(session.DeliveryBarrierReady,"current V2 held continuation reaches its delivery barrier");Assert.Greater(advanced,0,"current V2 must progress through ordinary authority grants");Assert.True(moved,"current V2 continuation includes actual bounded movement");}
                Assert.False(authority.AwaitingRoutes,"finish must leave the route-waiting phase before capturing the suffix");var suffix=authority.CaptureBytes();Pump(4);var next=authority.CaptureBytes();return WorldWire.Pack(w=>{w.Write(suffix.Length);w.Write(suffix);w.Write(next.Length);w.Write(next);});
            }
            var hot=Finish();RestoreAuthority(waiting);Assert.False(authority.MaintenancePendingFor(session));Assert.True(authority.AwaitingRoutes);Assert.False(authority.TryAdvance());var cold=Finish();if(!hot.SequenceEqual(cold)){var folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(CertifiedHostContinuationTests).Assembly.Location),"../../../.local/qa/held-diag-01a13b4c"));System.IO.Directory.CreateDirectory(folder);System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,"hot-"+site+"-"+legacyV1+".bin"),hot);System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,"cold-"+site+"-"+legacyV1+".bin"),cold);}CollectionAssert.AreEqual(hot,cold,legacyV1?"legacy V1 suffix and next Pump":"current V2 final suffix and next Pump");Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);RestoreAuthority(idle);Assert.False(authority.AwaitingRoutes);CollectionAssert.AreEqual(idle,authority.CaptureBytes(),"old false phase decode");
        }
        [TestCase(false,false)][TestCase(false,true)][TestCase(true,false)][TestCase(true,true)] public void ProducerBarrierHandoffKeepsTypedPayloadThroughReadBoundary(bool legacy,bool throwRead)
        {
            int producer=Spawn(-18,-14);var request=session.Probe(producer,1,activeProfile.Navigation,new NavPoint(-4,2),new NavPoint(4,2));Assert.NotNull(request);request.StorageOwner.Retain();
            try{
                // Controlled legacy destination: use an actual paid producer input,
                // preserve its external pin while replacing only the old raw stores.
                if(legacy){var map=(NavigationCommandSlots<int,NavigationRequest>)typeof(NavigationSession).GetField("ownedProbes",F).GetValue(session);var fifo=(NavigationLiveProbeAnswers)typeof(NavigationSession).GetField("liveProbeAnswers",F).GetValue(session);
                typeof(NavigationSession).GetField("ownedProbes",F).SetValue(session,null);typeof(NavigationSession).GetField("liveProbeAnswers",F).SetValue(session,null);typeof(NavigationSession).GetField("probes",F).SetValue(session,new AuthoritySlotMap<int,NavigationRequest>{{-producer,request}});map.Release();fifo.Release();}
                Assert.True(session.Requests.TryDequeue(out var queued));Assert.AreSame(request,queued);Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,session.Generation,session.Admission.PublicationIdentity,out var input));NavigationHostAnswer payload=null;
                using(input){try{payload=NavigationHostAnswer.CreateTerminal(request.StorageOwner,NavSolveStatus.Stale,input.OwnedReservation());input.Attach(payload);input.Commit(payload,_=>Assert.True(session.Answers.TryEnqueue(payload.Answer)));}finally{payload?.ReleaseStorage();}}
                Assert.True(session.DeliveryBarrierReady);Assert.Zero(session.Answers.Count);Assert.AreEqual(1,payload.References);session.ApplyResults();Assert.AreEqual(1,payload.References,"raw producer destination owns payload after barrier and names retire");Assert.Zero(((IList)typeof(NavigationSession).GetField("barrierAnswers",F).GetValue(session)).Count);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,request.StorageOwner),session.PhysicalHostBudget.PhysicalBytes);void Read(){session.RunHostStep(()=>{Assert.True(session.TryProbeAnswer(out var answer));Assert.AreSame(payload.Answer,answer);Assert.AreEqual(1,payload.References,"returned facade remains owned inside the production read boundary");Assert.AreEqual(request.Session,answer.Request.Session);Assert.AreEqual(request.Request,answer.Request.Request);Assert.AreEqual(request.Profile.Radius,answer.Request.Profile.Radius);Assert.True(answer.Request.Geometry.SegmentFree(answer.Request.Start,answer.Request.Goal,answer.Request.Profile.Radius));Assert.AreEqual(NavSolveStatus.Stale,answer.Status);Assert.Zero(answer.CopyRoute().Length);if(throwRead)throw new InvalidOperationException("controlled consumer exception");});}if(throwRead)Assert.Throws<InvalidOperationException>(Read);else Read();Assert.Zero(payload.References);Assert.False(session.TryProbeAnswer(out _));
            }finally{request.StorageOwner.Release();}
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [TestCase("receipt")][TestCase("probe-map")][TestCase("probe-fifo")] public void HistoricalPendingStalePayloadSurvivesEmptyBarrierRefusalAndColdConsumption(string refusal)
        {
            int moving=Spawn(-18,-14),holder=Spawn(-8,10);Arrival(moving,new NavPoint(-10,-14));for(int i=0;i<20000&&(session.PendingCount>0||session.AllocationCount>0);i++){Pump(4);session.ApplyResults();}Assert.True(session.Crowd.TryGet(moving,out var actor));Assert.True(actor.Moving);
            Assert.True(session.Move(moving,actor.Goal));var request=session.PendingRequestFor(moving);Assert.AreEqual(1,session.Requests.Count);Assert.Zero(session.Answers.Count);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Hold,new[]{holder},origin:PlayableOrderOrigin.Human),null));
            for(int i=1;i<session.Requests.InstanceCapacity;i++){
                Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,session.Generation,session.Admission.PublicationIdentity,out var input));using(input){NavigationHostRequest stale=null;try{stale=NavigationHostRequest.CreateOwned(session.Generation+1,i+1,request.Entity,i+1,request.Profile,request.Geometry,request.Start,request.Goal,request.BaseGeometry,request.HeldIdentity,request.MemberIdentity,request.StartLocation,request.GoalLocation,null,input.OwnedReservation());input.Attach(stale);input.Commit(stale,_=>Assert.True(session.Requests.TryEnqueue(stale.Request)));}finally{stale?.ReleaseStorage();}}
            }
            Assert.True(session.PrepareDeliveryBarrier());Assert.AreSame(request,session.PendingRequestFor(moving));Assert.False(actor.Moving);Assert.False((bool)typeof(NavigationSession).GetMethod("CurrentMember",F).Invoke(session,new object[]{request}));
            // Controlled terminal ingress: the worker keeps this historical
            // pending request in admission and may legitimately solve it Ready.
            Assert.True(session.Requests.TryDequeue(out var handed));Assert.AreSame(request,handed);Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,session.Generation,session.Admission.PublicationIdentity,out var terminalScope));NavigationHostAnswer terminalOwner=null;
            using(terminalScope){try{terminalOwner=NavigationHostAnswer.CreateTerminal(request.StorageOwner,NavSolveStatus.Stale,terminalScope.OwnedReservation());terminalScope.Attach(terminalOwner);terminalScope.Commit(terminalOwner,_=>Assert.True(session.Answers.TryEnqueue(terminalOwner.Answer)));}finally{terminalOwner?.ReleaseStorage();}}
            kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Transfer;try{Pump(1);}finally{kernel.PauseAtPhysicalBoundary=null;}
            Assert.AreEqual(1,session.Answers.Count);var terminal=session.Answers.CopyItems()[0];Assert.AreSame(request,terminal.Request);Assert.AreEqual(NavSolveStatus.Stale,terminal.Status);Assert.NotNull(terminal.StorageOwner);Assert.Null(typeof(NavigationSession).GetField("ownedProbes",F).GetValue(session));Assert.Null(typeof(NavigationSession).GetField("liveProbeAnswers",F).GetValue(session));var before=Bytes();long physical=session.PhysicalHostBudget.PhysicalBytes;int denied=0,underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            try{session.PhysicalHostBudget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==(refusal=="receipt"?typeof(NavigationBarrierReceipt):refusal=="probe-map"?typeof(NavigationCommandSlots<int,NavigationRequest>):typeof(NavigationLiveProbeAnswers)))){denied++;return false;}return true;};Assert.False(session.DeliveryBarrierReady);}finally{session.PhysicalHostBudget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreEqual(physical,session.PhysicalHostBudget.PhysicalBytes);CollectionAssert.AreEqual(before,Bytes());Assert.AreSame(terminal,session.Answers.CopyItems()[0]);
            Assert.False(session.DeliveryBarrierReady);Assert.Zero(session.Answers.Count);Assert.Zero(((NavigationCommandSlots<int,NavigationRequest>)typeof(NavigationSession).GetField("ownedProbes",F).GetValue(session)).Capacity);Assert.Zero(((NavigationLiveProbeAnswers)typeof(NavigationSession).GetField("liveProbeAnswers",F).GetValue(session)).Capacity);var barrier=(NavigationSession.NavigationBarrierAnswers)typeof(NavigationSession).GetField("barrierAnswers",F).GetValue(session);Assert.AreEqual(1,barrier.Count);Assert.AreSame(terminal,barrier[0]);Assert.AreEqual(1,terminal.StorageOwner.References,"receipt is the sole terminal payload owner after mailbox removal");var prefix=Bytes();long stopSequence=++command;
            byte[] Consume(){Pump(1);Assert.True(session.DeliveryBarrierReady);var rows=(NavigationSession.NavigationBarrierAnswers)typeof(NavigationSession).GetField("barrierAnswers",F).GetValue(session);Assert.AreEqual(1,rows.Count);var owner=rows[0].StorageOwner;Assert.NotNull(owner);Assert.AreEqual(1,owner.References);Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,stopSequence,"player-1",PlayableCommandKind.Stop,new[]{moving},origin:PlayableOrderOrigin.Human),null));var stopped=Bytes();long charge=session.PhysicalHostBudget.PhysicalBytes;int failures=0;
                try{session.PhysicalHostBudget.AllocationAdmission=_=>{if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationPreparedAnswerNames))){failures++;return false;}return true;};session.ApplyResults();}finally{session.PhysicalHostBudget.AllocationAdmission=null;}
                Assert.AreEqual(1,failures);Assert.AreEqual(charge,session.PhysicalHostBudget.PhysicalBytes);CollectionAssert.AreEqual(stopped,Bytes());Assert.AreEqual(1,owner.References);int rejected=session.RejectedResults,applied=session.AppliedResults;session.ApplyResults();Assert.AreEqual(rejected+1,session.RejectedResults);Assert.AreEqual(applied,session.AppliedResults);Assert.Zero(rows.Count);Assert.Zero(owner.References);Assert.Zero(session.LastOwnedDiscardPublicationAllocatedBytes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);return Bytes();}
            var suffix=Consume();Pump(4);var next=Bytes();var facts=HostFacts();Restore(prefix);CollectionAssert.AreEqual(prefix,Bytes());CollectionAssert.AreEqual(suffix,Consume());Pump(4);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(facts,HostFacts());
        }

        [Test] public void SingleTerminalFailureOwnsBarrierPayloadThroughRefusalAndColdConsumption()
        {
            int entity=Spawn(-18,-14);Assert.True(session.Move(entity,new NavPoint(-10,-14)));var request=session.PendingRequestFor(entity);Assert.True(session.Requests.TryDequeue(out var handed));Assert.AreSame(request,handed);Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,session.Generation,session.Admission.PublicationIdentity,out var input));NavigationHostAnswer payload=null;
            using(input){try{payload=NavigationHostAnswer.CreateTerminal(request.StorageOwner,NavSolveStatus.InvalidEndpoint,input.OwnedReservation());input.Attach(payload);input.Commit(payload,_=>Assert.True(session.Answers.TryEnqueue(payload.Answer)));}finally{payload?.ReleaseStorage();}}
            Assert.True(session.DeliveryBarrierReady);Assert.Zero(session.Answers.Count);Assert.AreEqual(1,payload.References);var prefix=Bytes();var budget=session.PhysicalHostBudget;long charge=budget.PhysicalBytes;int denied=0,underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){denied++;return false;}return true;};session.ApplyResults();}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreEqual(charge,budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Assert.AreEqual(1,payload.References);
            byte[] Consume(){var rows=(IList)typeof(NavigationSession).GetField("barrierAnswers",F).GetValue(session);Assert.AreEqual(1,rows.Count);var owner=((NavigationAnswer)rows[0]).StorageOwner;Assert.NotNull(owner);session.ApplyResults();Assert.Zero(rows.Count);Assert.Zero(owner.References);Assert.Null(session.PendingRequestFor(entity));Assert.True(session.Crowd.TryGet(entity,out var actor));Assert.AreEqual(NavigationOutcome.Rejected,actor.Outcome);Assert.AreEqual(NavigationOutcome.Rejected,actor.LastFailure);Assert.AreEqual(1,session.OwnedOrdinaryFailurePublications);Assert.Zero(session.LastOwnedOrdinaryFailurePublicationAllocatedBytes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);return Bytes();}
            var suffix=Consume();Pump(1);var next=Bytes();var facts=HostFacts();Restore(prefix);CollectionAssert.AreEqual(prefix,Bytes());CollectionAssert.AreEqual(suffix,Consume());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(facts,HostFacts());
        }

        [Test] public void HeldBarrierFullRequestQueueStopsButKeepsPendingGoalAndFailureCold()
        {
            int moving=Spawn(-18,-14),holder=Spawn(-8,10);Arrival(moving,new NavPoint(-10,-14));for(int i=0;i<20000&&(session.PendingCount>0||session.AllocationCount>0);i++){Pump(4);session.ApplyResults();}Assert.True(session.Crowd.TryGet(moving,out var actor));Assert.True(actor.Moving);
            Assert.True(session.Move(moving,actor.Goal));var request=session.PendingRequestFor(moving);for(int i=0;i<20000&&session.Answers.Count==0;i++)Pump(4);Assert.AreEqual(1,session.Answers.Count);request=session.PendingRequestFor(moving);Assert.AreSame(request,session.Answers.CopyItems()[0].Request);Assert.AreEqual(NavSolveStatus.Ready,session.Answers.CopyItems()[0].Status);Assert.True(session.DeliveryBarrierReady);Assert.Zero(session.Answers.Count);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Hold,new[]{holder},origin:PlayableOrderOrigin.Human),null));for(int i=0;i<4096;i++){
                Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,session.Generation,session.Admission.PublicationIdentity,out var inputScope));using(inputScope){NavigationHostRequest stale=null;try{stale=NavigationHostRequest.CreateOwned(session.Generation+1,i+1,request.Entity,i+1,request.Profile,request.Geometry,request.Start,request.Goal,request.BaseGeometry,request.HeldIdentity,request.MemberIdentity,request.StartLocation,request.GoalLocation,null,inputScope.OwnedReservation());inputScope.Attach(stale);inputScope.Commit(stale,_=>Assert.True(session.Requests.TryEnqueue(stale.Request)));}finally{stale?.ReleaseStorage();}}}
            Assert.AreEqual(4096,session.Requests.Count);
            // A prior technical diagnostic is independent of the retained intent.
            var sourceBacking=(NavigationCommandAuthority)typeof(NavigationSession).GetField("groupCommandBacking",F).GetValue(session);Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,session.Generation,session.Admission.PublicationIdentity,out var faultScope));using(faultScope){var names=NavigationPreparedCrowdNameWrite.CreateOwned(sourceBacking.CrowdNames,sourceBacking.CrowdNames.Find(moving),request,faultScope.OwnedReservation());try{faultScope.Attach(names);faultScope.Publish(names,_=>{names.Validate(sourceBacking.CrowdNames);names.ApplyNoThrow(false);actor.FailedRequest=request.Request;actor.FailedOrder=request.Order;actor.FailedGroupId=request.MemberIdentity.GroupId;actor.FailedRevision=request.MemberIdentity.OrderRevision;actor.FailedGoal=request.GoalLocation;actor.LastFailure=NavigationOutcome.Rejected;});faultScope.ResolveCommitted();}finally{names.Release();}}
            Assert.True((bool)typeof(NavigationSession).GetMethod("CurrentMember",F).Invoke(session,new object[]{request}),"preflight pending must be current before held maintenance");var prefix=Bytes();var budget=session.PhysicalHostBudget;var global=actor.StorageOwner.Global;var queue=session.Requests.CopyItems();var admission=session.Admission;long physical=budget.PhysicalBytes,mobility=actor.MobilityRevision;int denied=0,underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().Name=="PrepareHeldAdmission")){denied++;return false;}return true;};Assert.False(session.PrepareDeliveryBarrier());}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreSame(global,actor.StorageOwner.Global);Assert.True(actor.Moving);Assert.AreEqual(mobility,actor.MobilityRevision);Assert.AreSame(admission,session.Admission);Assert.AreSame(request,session.PendingRequestFor(moving));CollectionAssert.AreEqual(queue,session.Requests.CopyItems());Assert.AreEqual(physical,budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());
            byte[] Complete(){Assert.True(session.Crowd.TryGet(moving,out var a));var old=session.PendingRequestFor(moving);var pending=(NavigationCommandSlots<int,NavigationRequest>)typeof(NavigationSession).GetField("pending",F).GetValue(session);var goals=(NavigationCommandSlots<int,NavPoint>)typeof(NavigationSession).GetField("retainedGoals",F).GetValue(session);var identities=(NavigationCommandSlots<int,string>)typeof(NavigationSession).GetField("movementIdentities",F).GetValue(session);var reservations=(NavigationCommandSlots<int,NavPoint>)typeof(NavigationSession).GetField("reservations",F).GetValue(session);long op=(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session),m=a.MobilityRevision,publication=session.Admission.PublicationIdentity;int cancelled=0,requested=0;void Cancelled(int id){Assert.AreEqual(moving,id);Assert.False(Monitor.IsEntered(typeof(NavigationSession).GetField("transportGate",F).GetValue(session)));Assert.False(a.Moving);Assert.AreEqual(op+1,typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session));cancelled++;}void Requested(NavigationRequest _){requested++;}session.MovementCancelled+=Cancelled;session.RouteRequested+=Requested;try{Assert.True(session.PrepareDeliveryBarrier());}finally{session.MovementCancelled-=Cancelled;session.RouteRequested-=Requested;}
                Assert.AreEqual(1,cancelled);Assert.Zero(requested);Assert.AreSame(pending,typeof(NavigationSession).GetField("pending",F).GetValue(session));Assert.AreSame(goals,typeof(NavigationSession).GetField("retainedGoals",F).GetValue(session));Assert.AreSame(identities,typeof(NavigationSession).GetField("movementIdentities",F).GetValue(session));Assert.AreSame(reservations,typeof(NavigationSession).GetField("reservations",F).GetValue(session));Assert.AreSame(old,session.PendingRequestFor(moving));Assert.AreEqual(m+1,a.MobilityRevision);Assert.False(a.Moving);Assert.Null(a.StorageOwner.Global);Assert.AreEqual(old.Request,a.FailedRequest);Assert.AreEqual(old.Order,a.FailedOrder);Assert.AreEqual(old.GoalLocation,a.FailedGoal);Assert.AreEqual(NavigationOutcome.Rejected,a.LastFailure);Assert.AreEqual(publication,session.Admission.PublicationIdentity);Assert.True(((NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session)).IsDetached(moving));Assert.AreEqual(4096,session.Requests.Count);Assert.Zero(session.LastOwnedHeldMaintenancePublicationAllocatedBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);return Bytes();}
            var suffix=Complete();Pump(4);var next=Bytes();
            Assert.DoesNotThrow(()=>Restore(prefix),"restore pre-maintenance prefix");
            CollectionAssert.AreEqual(suffix,Complete(),"cold logical failure suffix");Pump(4);CollectionAssert.AreEqual(next,Bytes(),"cold logical failure next Pump");
            Assert.DoesNotThrow(()=>Restore(suffix),"restore published stopped historical-pending suffix");
            var restoredPending=session.PendingRequestFor(moving);Assert.False((bool)typeof(NavigationSession).GetMethod("CurrentMember",F).Invoke(session,new object[]{restoredPending}),"historical request must never become live-current");
            using(var saved=session.CaptureOwnedState()){
                var check=typeof(NavigationSession).GetMethod("RetainedHeldStopPendingForRestore",F);bool Valid(NavigationRequest r)=>(bool)check.Invoke(session,new object[]{r,saved});Assert.True(Valid(restoredPending));var id=restoredPending.MemberIdentity;
                NavigationRequest Changed(long incarnation,long mobilityRevision,long group,long revision,string held=null)=>new NavigationRequest(restoredPending.Session,restoredPending.Request,restoredPending.Entity,restoredPending.Order,restoredPending.Profile,restoredPending.Geometry,restoredPending.Start,restoredPending.Goal,restoredPending.BaseGeometry,held??restoredPending.HeldIdentity,new NavigationMemberIdentity(incarnation,mobilityRevision,group,revision,id.MembershipRevision),restoredPending.StartLocation,restoredPending.GoalLocation);
                Assert.False(Valid(Changed(id.Incarnation+1,id.MobilityRevision,id.GroupId,id.OrderRevision)));Assert.False(Valid(Changed(id.Incarnation,id.MobilityRevision,id.GroupId+1,id.OrderRevision)));Assert.False(Valid(Changed(id.Incarnation,id.MobilityRevision,id.GroupId,id.OrderRevision+1)));Assert.True(session.Crowd.TryGet(moving,out var stopped));Assert.False(Valid(Changed(id.Incarnation,stopped.MobilityRevision+1,id.GroupId,id.OrderRevision)));bool wasMoving=stopped.Moving;try{stopped.Moving=true;Assert.False(Valid(restoredPending));}finally{stopped.Moving=wasMoving;}
                string currentHeld=(string)typeof(NavigationSession).GetMethod("HeldIdentity",F).Invoke(session,new object[]{moving});Assert.False(Valid(Changed(id.Incarnation,id.MobilityRevision,id.GroupId,id.OrderRevision,currentHeld)));
            }

            CollectionAssert.AreEqual(suffix,Bytes());Pump(4);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            var late=(NavigationSession.NavigationBarrierAnswers)typeof(NavigationSession).GetField("barrierAnswers",F).GetValue(session);Assert.AreEqual(1,late.Count,"actual Ready was retained unread across held-stop and cold restore");Assert.AreSame(restoredPending,late[0].Request);Assert.AreEqual(NavSolveStatus.Ready,late[0].Status);var allocation=typeof(NavigationSession).GetField("hostAllocationThread",F);TestContext.WriteLine("late host before="+allocation.GetValue(session));for(int i=0;i<20000&&(int)allocation.GetValue(session)!=0;i++)Pump(4);Assert.Zero((int)allocation.GetValue(session),"ordinary authority result processing requires the host allocation barrier to finish naturally");
            int rejected=session.RejectedResults,activatedOld=0;void Activated(NavigationRequest r){if(r.Entity==moving)activatedOld++;}session.RouteActivated+=Activated;try{Assert.True((bool)typeof(NavigationSession).GetMethod("TryApplyResultsPrepared",F).Invoke(session,null),"late result preparation must finish");}finally{session.RouteActivated-=Activated;}Assert.Zero(activatedOld,"late old result cannot activate its actor; independent ingress must not alter actor activation");Assert.Greater(session.RejectedResults,rejected);Assert.True(session.Crowd.TryGet(moving,out var after));Assert.False(after.Moving,"old mobility response must not reactivate stopped actor");
        }
        [Test] public void UnsupportedZeroAnswerHeldPrefixPreservesLegacyStepThenBarrierRefresh()
        {
            kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");session=new NavigationSession(1,new NavGeometry(30,Array.Empty<NavObstacle>(),1),activeProfile.Navigation);Domain.GetField("navigation",F).SetValue(domain,session);kernel=new CertifiedRouteKernel();var actor=session.Crowd.Add(1,new NavPoint(-10,-6));session.CancelProbe(7);Assert.True(session.Move(1,new NavPoint(10,-6)));for(int i=0;i<20000&&session.Answers.Count==0;i++)PumpProbe(64);Assert.AreEqual(1,session.Answers.Count);session.ApplyResults();session.Crowd.Add(2,new NavPoint(-12,12));session.Stop(2,true);Assert.True(actor.Moving);var position=actor.Position;int movements=session.OwnedMovementPublications;
            session.Step(1d/30);Assert.AreNotEqual(position,actor.Position);Assert.AreEqual(movements,session.OwnedMovementPublications,"unsupported held prefix selects compatibility before writes");Assert.Zero(session.PendingCount);var current=actor.Position;session.PrepareDeliveryBarrier();Assert.True(session.IsPending(1));Assert.AreEqual(current,session.PendingRequestFor(1).Start);Assert.False(actor.Moving);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);
        }

        [Test] public void InvalidReadyCostRejectsAtomicallyWithPreparedFailureAndExactColdPump()
        {
            int entity=Spawn(-18,-14);Arrival(entity,new NavPoint(-10,-14));for(int i=0;i<20000&&session.Answers.Count==0;i++){Pump(64);if(session.Answers.Count==0)session.PrepareDeliveryBarrier();}Assert.AreEqual(1,session.Answers.Count);
            var budget=session.PhysicalHostBudget;
            var validPrefix=Bytes();NavigationAnswer injected=null;
            void Inject(){
            var original=session.Answers.CopyItems()[0];var owner=original.StorageOwner;var host=session.PinCommittedHostRoot();NavScheduledRequest recipe;try{var row=HostRow(entity)??host.Delivered.Select(e=>(NavigationHostRow)e.Value).Single(r=>r.Request.Entity==entity);Assert.NotNull(row);recipe=(NavScheduledRequest)row.RecipeOwner.Facade;}finally{host.Release();}
            Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,session.Generation,session.Admission.PublicationIdentity,out var scope));NavigationHostAnswer bad=null;
            using(scope){
                var r=scope.OwnedReservation();using(NavStorageScratch.Reserve(r,32L+8L*owner.CorridorOwner.TypedRoute.Legs.Count)){
                    var legs=owner.CorridorOwner.TypedRoute.Legs.ToArray();var old=legs.First(l=>l.Kind==NavTypedLegKind.Surface);int index=Array.IndexOf(legs,old);var changed=NavTypedLeg.CreateOwned(old.From,old.To,old.Cost+1,old.Kind,old.PortalId,r,owner.CorridorOwner.ProviderOwner.Provider);NavTypedRoute route=null;NavigationHostCorridor corridor=null;
                    try{legs[index]=changed;route=NavTypedRoute.CopySubscriptionAndCreateOwned(owner.CorridorOwner.TypedRoute.Subscription,legs,owner.CorridorOwner.TypedRoute.Cost+1,r);corridor=NavigationHostCorridor.CreateOwned(owner.RequestOwner,owner.CorridorOwner.ProviderOwner,recipe,route,r);bad=NavigationHostAnswer.CreateReady(owner.RequestOwner,corridor,original.ReadOnlyRouteBacking,r);}finally{changed.ReleaseStorage();route?.ReleaseStorage();corridor?.ReleaseStorage();}
                }
                scope.Attach(bad);scope.Commit(bad,_=>{});
            }
            try{Assert.True(session.Answers.TryDequeue(out var removed));Assert.AreSame(original,removed);typeof(NavigationSession).GetMethod("ReleaseCorridorAnswer",F).Invoke(session,new object[]{original});Assert.True(kernel.RetirePhysicalDelivered(Binding()),"retire the original delivery before injecting a distinct incoming fault");Assert.True(session.Answers.TryEnqueue(bad.Answer));injected=bad.Answer;}finally{bad.ReleaseStorage();}
            }
            Inject();
            var prefix=Bytes();long physical=budget.PhysicalBytes;int denied=0,underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);var diagnostic=typeof(NavigationSession).GetProperty("LastCorridorFailure",F);diagnostic.SetValue(session,"old diagnostic");
            try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().Name=="PrepareAdmissionProjection")){denied++;return false;}return true;};session.Step(1d/30);}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual("old diagnostic",session.LastCorridorFailure);CollectionAssert.AreEqual(prefix,Bytes());Assert.AreSame(injected,session.Answers.CopyItems()[0]);
            var events=new System.Collections.Generic.List<string>();session.RouteFailed+=(request,outcome)=>{Assert.False(Monitor.IsEntered(gate));Assert.AreEqual(NavigationOutcome.Rejected,outcome);events.Add("failure");};session.MovementCancelled+=_=>events.Add("cancel");
            session.Step(1d/30);Assert.AreEqual(1,session.OwnedResultBatchPublications);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);Assert.Zero(session.LastOwnedMovementPublicationAllocatedBytes);Assert.AreEqual(1,session.RejectedResults);Assert.Zero(session.AppliedResults);Assert.Zero(session.UnreachableResults);Assert.Zero(session.OwnedOrdinaryFailurePublications);Assert.AreEqual("surface clearance/cost",session.LastCorridorFailure);CollectionAssert.AreEqual(new[]{"failure","cancel"},events);Assert.Null(session.PendingRequestFor(entity));Assert.Zero(session.Answers.Count);Assert.True(session.Crowd.TryGet(entity,out var actor));Assert.AreEqual(NavigationOutcome.Rejected,actor.Outcome);Assert.AreEqual(GroupMemberPhase.Blocked,session.GroupFor(entity).Members.Single(m=>m.Entity==entity).Phase);Assert.Zero(session.ArrivalReservations.Count);
            var suffix=Bytes();var facts=HostFacts();Pump(4);var next=Bytes();var nextFacts=HostFacts();Restore(validPrefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(validPrefix,Bytes());Inject();CollectionAssert.AreEqual(prefix,Bytes());kernel.Attach(session.RoutePort);typeof(CertifiedRouteKernel).GetMethod("RefreshPhysicalFrontier",F).Invoke(kernel,new object[]{session.RoutePort});session.Step(1d/30);CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(4);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [Test] public void CurrentReadyProducerClearsDiagnosticAfterStaleOrdinaryCold()
        {
            Call("AddCredits",PlayableOwner.Player,10000d);Call("BuildAt",1,2,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null);Call("AdvanceFoundations");Call("AdvanceBuildings",100d);int factory=0;foreach(DictionaryEntry pair in (IDictionary)Domain.GetField("buildings",F).GetValue(domain))if((PlayableBuildingKind)pair.Value.GetType().GetField("Kind").GetValue(pair.Value)==PlayableBuildingKind.Factory)factory=(int)pair.Key;Assert.Greater(factory,0);
            int entity=Spawn(-18,-14);session.CancelProbe(7);Assert.True(session.Move(entity,new NavPoint(-10,-14)));for(int i=0;i<20000&&session.Answers.Count==0;i++){Pump(64);if(session.Answers.Count==0)session.PrepareDeliveryBarrier();}Assert.AreEqual(1,session.Answers.Count);
            Assert.AreEqual(PlayableCommandStatus.Accepted,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.SetRally,new[]{factory},new NavPoint(-8,10)),null));Call("AdvanceRally");for(int i=0;i<20000&&session.Answers.Count<2;i++)Pump(64);Assert.AreEqual(2,session.Answers.Count);Assert.Less(session.Answers.CopyItems()[1].Request.Entity,0);session.Stop(entity,false);
            var budget=session.PhysicalHostBudget;var prefix=Bytes();void Consume(){typeof(NavigationSession).GetProperty("LastCorridorFailure",F).SetValue(session,"surface clearance/cost");session.Step(1d/30);Assert.Null(session.LastCorridorFailure);Assert.AreEqual(1,session.RejectedResults);Assert.AreEqual(1,session.OwnedResultBatchPublications);Assert.AreEqual(1,session.OwnedProbeAnswerPublications);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);Assert.Zero(session.Answers.Count);}
            Consume();var suffix=Bytes();var facts=HostFacts();Pump(4);var next=Bytes();var nextFacts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());kernel.Attach(session.RoutePort);typeof(CertifiedRouteKernel).GetMethod("RefreshPhysicalFrontier",F).Invoke(kernel,new object[]{session.RoutePort});Consume();CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(4);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [Test] public void EmptyAnswerOwnedStepReusesAdmissionWithoutWakingHostOrCountingResults()
        {
            int entity=Spawn(-18,-14);session.CancelProbe(7);Assert.True(session.Move(entity,new NavPoint(-10,-14)));Assert.AreEqual(1,session.PendingCount);Assert.Zero(session.Answers.Count);
            var admission=session.Admission;long operation=(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session);int batches=session.OwnedResultBatchPublications,movements=session.OwnedMovementPublications,wakes=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            session.HostChanged+=()=>{Assert.False(Monitor.IsEntered(gate));wakes++;};session.Step(1d/30);
            Assert.AreEqual(movements+1,session.OwnedMovementPublications);Assert.Zero(session.LastOwnedMovementPublicationAllocatedBytes);Assert.AreEqual(batches,session.OwnedResultBatchPublications);Assert.AreEqual(operation+3,typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session));Assert.AreSame(admission,session.Admission);Assert.Zero(wakes);Assert.AreEqual(1,session.PendingCount);Assert.Zero(session.AppliedResults);Assert.Zero(session.RejectedResults);
        }

        [TestCase(false)][TestCase(true)] public void ActualGroupSettlementRefusesLateMemberCopyAndRetiresOnlyAfterLastArrivalCold(bool admissionDenial)
        {
            int first=Spawn(-12,-14),last=Spawn(-18,-10);const double dt=1d/30;
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Move,new[]{first,last},new NavPoint(-10,-14),origin:PlayableOrderOrigin.Human),null));
            for(int i=0;i<20000&&(session.PendingCount>0||session.AllocationCount>0);i++){Pump(4);session.ApplyResults();}
            Assert.Zero(session.PendingCount);Assert.Zero(session.AllocationCount);Assert.True(session.Crowd.TryGet(first,out var a));Assert.True(session.Crowd.TryGet(last,out var b));
            for(int i=0;i<1000&&!(a.routeIndex==a.route.Length-1&&Math.Sqrt((a.Position.X-a.Goal.X)*(a.Position.X-a.Goal.X)+(a.Position.Z-a.Goal.Z)*(a.Position.Z-a.Goal.Z))<=activeProfile.Navigation.ArrivalTolerance);i++)session.Step(dt);
            Assert.True(a.Moving);Assert.True(b.Moving);Assert.LessOrEqual(Math.Sqrt((a.Position.X-a.Goal.X)*(a.Position.X-a.Goal.X)+(a.Position.Z-a.Goal.Z)*(a.Position.Z-a.Goal.Z)),activeProfile.Navigation.ArrivalTolerance);
            var budget=session.PhysicalHostBudget;var prefix=Bytes();long physical=budget.PhysicalBytes;var global=a.StorageOwner.Global;var installed=((NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session)).OwnerFor(first);
            NavigationSession.NavigationLiveArrivalTables Tables()=>(NavigationSession.NavigationLiveArrivalTables)typeof(NavigationSession).GetField("liveArrivalTables",F).GetValue(session);
            Assert.AreEqual(1,Tables().Count);Assert.AreEqual(2,session.ArrivalReservations.Count);long retirement=(long)typeof(NavigationSession).GetField("arrivalRetirementSequence",F).GetValue(session);
            int batch=session.OwnedResultBatchPublications,movement=session.OwnedMovementPublications,denied=0,underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            using(var old=session.CaptureOwnedState()){
                var oldBytes=WorldWire.Pack(w=>WorldWire.Write(w,old));physical=budget.PhysicalBytes;
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;var frames=new System.Diagnostics.StackTrace().GetFrames();if(admissionDenial?frames.Any(f=>f.GetMethod().Name=="PrepareAdmissionProjection"):frames.Any(f=>f.GetMethod().Name=="CopyWithValuesOwned"&&f.GetMethod().DeclaringType==typeof(NavigationAuthorityMember))&&!frames.Any(f=>f.GetMethod().DeclaringType==typeof(GroupMarch))){denied++;return false;}return true;};session.Step(dt);}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.True(a.Moving);Assert.True(b.Moving);Assert.AreEqual(batch,session.OwnedResultBatchPublications);Assert.AreEqual(movement,session.OwnedMovementPublications);CollectionAssert.AreEqual(prefix,Bytes());CollectionAssert.AreEqual(oldBytes,WorldWire.Pack(w=>WorldWire.Write(w,old)));
                session.Step(dt);Assert.AreEqual(movement+1,session.OwnedMovementPublications);Assert.Zero(session.LastOwnedMovementPublicationAllocatedBytes);Assert.AreEqual(batch,session.OwnedResultBatchPublications);Assert.AreEqual(NavigationOutcome.Arrived,a.Outcome);Assert.True(b.Moving);Assert.AreEqual(GroupMemberPhase.Arrived,session.GroupFor(first).Members.Single(m=>m.Entity==first).Phase);Assert.AreSame(global,a.StorageOwner.Global);Assert.AreSame(installed,((NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session)).OwnerFor(first));Assert.AreEqual(1,Tables().Count);Assert.AreEqual(1,session.ArrivalReservations.Count);Assert.AreEqual(last,session.ArrivalReservations.Single().Entity);Assert.AreEqual(retirement,typeof(NavigationSession).GetField("arrivalRetirementSequence",F).GetValue(session));CollectionAssert.AreEqual(oldBytes,WorldWire.Pack(w=>WorldWire.Write(w,old)));
            }
            void Finish(){for(int i=0;i<1000&&session.Crowd.Units.Any(u=>u.Id==last&&u.Moving);i++)session.Step(dt);Assert.True(session.Crowd.TryGet(last,out var actor));Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome);Assert.AreEqual(GroupMemberPhase.Arrived,session.GroupFor(last).Members.Single(m=>m.Entity==last).Phase);}
            Finish();Assert.Zero(Tables().Count);Assert.Zero(Tables().References.Count);Assert.Zero(Tables().Ids.Count);Assert.Zero(session.ArrivalReservations.Count);Assert.AreEqual(retirement+1,typeof(NavigationSession).GetField("arrivalRetirementSequence",F).GetValue(session));var suffix=Bytes();var facts=HostFacts();Pump(4);var next=Bytes();var nextFacts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());kernel.Attach(session.RoutePort);typeof(CertifiedRouteKernel).GetMethod("RefreshPhysicalFrontier",F).Invoke(kernel,new object[]{session.RoutePort});session.Step(dt);Finish();CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(4);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
        }

        [Test] public void MixedStepDefersAdmissionWhenMovementSettlesAnAcceptedRoute()
        {
            Call("AddCredits",PlayableOwner.Player,10000d);Call("BuildAt",1,2,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null);Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            var buildings=(IDictionary)Domain.GetField("buildings",F).GetValue(domain);int factory=0;foreach(DictionaryEntry pair in buildings)if((PlayableBuildingKind)pair.Value.GetType().GetField("Kind").GetValue(pair.Value)==PlayableBuildingKind.Factory)factory=(int)pair.Key;Assert.Greater(factory,0);
            int entity=Spawn(-18,-14);session.CancelProbe(7);Assert.True(session.Move(entity,new NavPoint(-17.95,-14)));for(int i=0;i<20000&&session.Answers.Count==0;i++)Pump(64);Assert.AreEqual(1,session.Answers.Count);Assert.AreEqual(NavSolveStatus.Ready,session.Answers.CopyItems()[0].Status);
            Assert.AreEqual(PlayableCommandStatus.Accepted,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.SetRally,new[]{factory},new NavPoint(-8,10)),null));Call("AdvanceRally");for(int i=0;i<20000&&session.Answers.Count<2;i++)Pump(64);Assert.AreEqual(2,session.Answers.Count);
            var budget=session.PhysicalHostBudget;var prefix=Bytes();var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);long operation=(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session),publication=session.Admission.PublicationIdentity;int callbacks=0;
            session.RouteActivated+=request=>{callbacks++;Assert.False(Monitor.IsEntered(gate));Assert.AreEqual(operation+4,(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session));Assert.AreEqual(publication+1,session.Admission.PublicationIdentity);Assert.True(session.Crowd.TryGet(entity,out var actor));Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome);};
            session.Step(1d/30);Assert.AreEqual(1,callbacks);Assert.AreEqual(1,session.OwnedResultBatchPublications);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);Assert.AreEqual(GroupMemberPhase.Arrived,session.GroupFor(entity).Members[0].Phase);Assert.False(session.IsPending(entity));
            var suffix=Bytes();string facts=HostFacts();Pump(1);var next=Bytes();string nextFacts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes(),"settled-route cold prefix");kernel.Attach(session.RoutePort);typeof(CertifiedRouteKernel).GetMethod("RefreshPhysicalFrontier",F).Invoke(kernel,new object[]{session.RoutePort});session.Step(1d/30);Assert.AreEqual(1,session.OwnedResultBatchPublications);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);Assert.AreEqual(GroupMemberPhase.Arrived,session.GroupFor(entity).Members[0].Phase);var actual=Bytes();if(!suffix.SequenceEqual(actual)){var diagnostic=System.IO.Path.GetFullPath(System.IO.Path.GetDirectoryName(typeof(CertifiedHostContinuationTests).Assembly.Location)+"/../../../.local/qa/result-batch-settled-01a13b4c");System.IO.Directory.CreateDirectory(diagnostic);System.IO.File.WriteAllBytes(diagnostic+"/hot.bin",suffix);System.IO.File.WriteAllBytes(diagnostic+"/cold.bin",actual);using(var hot=PlayableWorldState.Decode(suffix))using(var cold=PlayableWorldState.Decode(actual))foreach(var field in new[]{"Domain","Navigation","Binding","Participants","AiAuthority","RuntimeTransport"})TestContext.WriteLine(field+" equal="+((byte[])typeof(PlayableWorldState).GetField(field).GetValue(hot)).SequenceEqual((byte[])typeof(PlayableWorldState).GetField(field).GetValue(cold)));}CollectionAssert.AreEqual(suffix,actual,"settled-route Step suffix");Assert.AreEqual(facts,HostFacts());Pump(1);var actualNext=Bytes();if(!next.SequenceEqual(actualNext)){var diagnostic=System.IO.Path.GetFullPath(System.IO.Path.GetDirectoryName(typeof(CertifiedHostContinuationTests).Assembly.Location)+"/../../../.local/qa/result-batch-settled-01a13b4c");System.IO.Directory.CreateDirectory(diagnostic);System.IO.File.WriteAllBytes(diagnostic+"/hot-next.bin",next);System.IO.File.WriteAllBytes(diagnostic+"/cold-next.bin",actualNext);TestContext.WriteLine("hotnextHostFacts="+nextFacts+" coldnextHostFacts="+HostFacts());using(var hot=PlayableWorldState.Decode(next))using(var cold=PlayableWorldState.Decode(actualNext)){System.IO.File.WriteAllBytes(diagnostic+"/hot-next-navigation.bin",hot.Navigation);System.IO.File.WriteAllBytes(diagnostic+"/cold-next-navigation.bin",cold.Navigation);using(var hotNav=WorldWire.Unpack(hot.Navigation,WorldWire.ReadNavigationSessionState))using(var coldNav=WorldWire.Unpack(cold.Navigation,WorldWire.ReadNavigationSessionState)){System.IO.File.WriteAllBytes(diagnostic+"/hot-next-host.bin",hotNav.HostAuthority);System.IO.File.WriteAllBytes(diagnostic+"/cold-next-host.bin",coldNav.HostAuthority);}}}CollectionAssert.AreEqual(next,actualNext,"settled-route next Pump");Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)] public void ActualRallyProducerCompletesAcrossOwnedIngressAndFullWorldColdRestore(int capturePoint)
        {
            Call("AddCredits",PlayableOwner.Player,10000d);Call("BuildAt",1,2,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null);Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            var buildings=(IDictionary)Domain.GetField("buildings",F).GetValue(domain);int factory=0;
            foreach(DictionaryEntry pair in buildings)if((PlayableBuildingKind)pair.Value.GetType().GetField("Kind").GetValue(pair.Value)==PlayableBuildingKind.Factory)factory=(int)pair.Key;
            Assert.Greater(factory,0);var goal=new NavPoint(-8,10);
            Assert.AreEqual(PlayableCommandStatus.Accepted,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.SetRally,new[]{factory},goal),null));Call("AdvanceRally");
            if(capturePoint>0&&capturePoint<3){for(int i=0;i<20000&&session.OwnedIngressPublications==0;i++)Pump(1);Assert.AreEqual(1,session.OwnedIngressPublications);}
            if(capturePoint==2){for(int i=0;i<20000&&session.OwnedProbeAnswerPublications==0;i++){Pump(4);session.ApplyResults();}Assert.AreEqual(1,session.OwnedProbeAnswerPublications);}
            if(capturePoint==3){Assert.True(session.Requests.TryDequeue(out var legacy));Assert.True(session.Answers.TryEnqueue(new NavigationAnswer(legacy,Array.Empty<NavPoint>())));session.ApplyResults();Assert.AreEqual(1,session.OwnedProbeAnswerPublications);}
            var prefix=Bytes();var budget=session.PhysicalHostBudget;
            void Complete(){for(int i=0;i<20000&&((IDictionary)Domain.GetField("rallyWork",F).GetValue(domain)).Count>0;i++){Pump(4);session.Step(1d/30);Call("AdvanceRally");}Assert.Zero(((IDictionary)Domain.GetField("rallyWork",F).GetValue(domain)).Count);}
            Complete();Assert.AreEqual(3,session.OwnedIngressPublications);var suffix=Bytes();var facts=HostFacts();Pump(1);var next=Bytes();var nextFacts=HostFacts();
            Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Complete();CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());
            var restoredBuildings=(IDictionary)Domain.GetField("buildings",F).GetValue(domain);var building=restoredBuildings[factory];Assert.True((bool)building.GetType().GetField("HasRally").GetValue(building));Assert.AreEqual(goal,building.GetType().GetField("Rally").GetValue(building));
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [TestCase("carrier")][TestCase("map")][TestCase("admission")]
        public void FirstProducerProbeRefusesWithoutPublishingBootstrapOrMailbox(string site)
        {
            var before=Bytes();var budget=session.PhysicalHostBudget;long charge=budget.PhysicalBytes;var original=session.Admission;int denied=0,underGate=0;
            var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            var target=site=="carrier"?typeof(NavigationSession).GetNestedType("PreparedProbe",BindingFlags.NonPublic):site=="map"?typeof(NavigationCommandSlots<int,NavigationRequest>):typeof(NavigationAdmissionStorage);
            try{
                budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().DeclaringType==target)){denied++;return false;}return true;};
                Assert.Null(session.Probe(7,1,activeProfile.Navigation,new NavPoint(-4,2),new NavPoint(4,2)));
            }finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreSame(original,session.Admission);Assert.Zero(session.OwnedProbePublications);Assert.Zero(session.Requests.Count);Assert.Zero(session.PendingCount);Assert.Null(typeof(NavigationSession).GetField("groupCommandBacking",F).GetValue(session));Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());
            Assert.NotNull(session.Probe(7,1,activeProfile.Navigation,new NavPoint(-4,2),new NavPoint(4,2)));Assert.AreEqual(1,session.OwnedProbePublications);Assert.Zero(session.LastOwnedProbePublicationAllocatedBytes);
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
        }

        [Test] public void ArrivalPointQualificationMatchesAuthoredMapsWithoutTouchingFootprintMemo()
        {
            IPlayableTerrain[] maps={new FoundryMap(new FoundryProfileData()),new ThreeCrossingsMap(new ThreeCrossingsProfileData()),new MossHighlandMap(),new BasaltCrownMap(),new AiTestMap(),new NereidaMap(new NereidaProfileData()),new StackedNavigationFixture()};
            foreach(var map in maps){
                var locations=(map as INavArrivalPointTerrain)?.ArrivalLocations;
                var layered=(map as ILayeredPlayableTerrain)?.LayeredNavigation as LayeredNavigationProvider;
                var memo=locations==null?null:(IDictionary)typeof(TerrainLocations).GetField("footprints",F).GetValue(locations);
                var points=map.Supports.SelectMany(s=>new[]{new NavPoint(s.Bounds.MinX,s.Bounds.MinZ),new NavPoint(s.Bounds.MaxX,s.Bounds.MaxZ),new NavPoint((s.Bounds.MinX+s.Bounds.MaxX)/2,(s.Bounds.MinZ+s.Bounds.MaxZ)/2)})
                    .Concat(Enumerable.Range(-12,25).SelectMany(x=>Enumerable.Range(-12,25).Select(z=>new NavPoint(map.HalfExtent*x/12,map.HalfExtent*z/12))))
                    .Concat(new[]{new NavPoint(double.NaN,0),new NavPoint(double.PositiveInfinity,0)});
                foreach(var point in points){
                    int count=memo?.Count??0;NavLocation actual;bool result=locations!=null?locations.TryLocatePoint(point,out actual):layered.TryLocatePoint(point,out actual);
                    Assert.AreEqual(count,memo?.Count??0,"Pure point query must not mutate the movement memo");
                    bool expected=map.TryLocate(point,0,out var original);Assert.AreEqual(expected,result,map.Id+" "+point);
                    if(expected){Assert.AreEqual(original.Position,actual.Position);Assert.AreEqual(original.SurfaceId,actual.SurfaceId);Assert.AreSame(original.NameReference.Owner,actual.NameReference.Owner);}
                }
            }
            var blocked=new TerrainLocations(new NavGeometry(10,new[]{new NavObstacle(-1,-1,1,1)},1),new[]{new MapSupport("floor",new NavObstacle(-10,-10,10,10),false,0,surfaceId:"floor")},Array.Empty<NavSurfaceTransition>());
            Assert.False(blocked.TryLocatePoint(new NavPoint(0,0),out _),"Keep the terrain's own blocker even if Session geometry is clear");
        }

        [TestCase(false)][TestCase(true)] public void ArrivalObservationPinsExactInputsThroughConcurrentReplacementOrStop(bool replace)
        {
            int id=Spawn(-18,-14);using(var idle=session.CaptureState()){}Arrival(id,new NavPoint(-10,-14));
            var original=session.PendingRequestFor(id);Assert.NotNull(original);var owner=original.StorageOwner;var profile=original.Profile.StorageRoot;Assert.NotNull(owner);Assert.NotNull(profile);
            var budget=session.PhysicalHostBudget;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);bool shell=false,cancelled=false,pinned=false;int callbacksUnderGate=0;
            try{
                budget.AllocationAdmission=_=>{
                    if(Monitor.IsEntered(gate))callbacksUnderGate++;
                    if(!shell){shell=new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().DeclaringType==typeof(NavigationPreparedArrivalObservation));return true;}
                    if(!cancelled){cancelled=true;if(replace)Arrival(id,new NavPoint(-8,-14));else session.Stop(id,true);
                        Assert.True(profile.BelongsTo(budget));Assert.Greater(owner.References,0,"Observation retains the exact superseded input");Assert.Greater(profile.References,0,"Its profile remains live through off-gate preparation");pinned=true;}
                    return true;
                };
                Assert.False(session.PreparePhysicalArrivalAttempt(),"Stale preparation refuses without publishing or faulting the worker");
            }finally{budget.AllocationAdmission=null;}
            Assert.True(shell);Assert.True(cancelled);Assert.True(pinned);Assert.Zero(callbacksUnderGate);Assert.Zero(session.RegisteredRouteCount);
            if(replace){Assert.NotNull(session.PendingRequestFor(id));Assert.AreNotSame(original,session.PendingRequestFor(id));}else{Assert.Null(session.PendingRequestFor(id));Assert.True(session.Crowd.Units.Single(actor=>actor.Id==id).Held);}
            Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
        }
        [Test] public void ArrivalObservationNearPhysicalCapRefusesBeforeBirthAndResumesExactColdPump()
        {
            int id=Spawn(-18,-14);using(var idle=session.CaptureState()){}Arrival(id,new NavPoint(-10,-14));var before=Bytes();var original=session.PendingRequestFor(id);var root=session.PinCommittedHostRoot();var budget=session.PhysicalHostBudget;NavRetainedArray<NavPoint> pressure=null;
            int Count(string name){var value=typeof(NavigationSession).GetField(name,F).GetValue(session);return (int)value.GetType().GetProperty("Count",F|BindingFlags.Public).GetValue(value);}
            int rows=Count("allocating"),members=session.GroupFor(id).Members.Length,actors=session.Crowd.Units.Count,occupants=3*actors+Count("pending")+Count("arrivalLedger");
            long scratch=8192L+2048L*rows+2048L*actors+256L*members;
            long required=NavigationRetentionBudget.ScopeBytes+NavStorageScratch.LeaseBytes+scratch+NavStorageLayout.Charge(NavigationPreparedArrivalObservation.Layout(rows,members,occupants));
            try{
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,session.Generation,1,out var scope));using(scope){
                    int count=checked((int)((LayeredNavigationScheduler.MaxRetainedBytes-budget.RetainedBytes+NavigationRetentionBudget.ScopeBytes-(required-32)-NavStorageLayout.Charge(96))/16));Assert.Greater(count,1000000);
                    pressure=NavRetainedArray<NavPoint>.Copy(new RepeatedPressurePoints(count),scope.TryReserve);scope.Attach(pressure);scope.Commit(pressure,_=>{});
                }
                Assert.Less(LayeredNavigationScheduler.MaxRetainedBytes-budget.RetainedBytes,required);long physical=budget.PhysicalBytes;int observedBirth=0;
                try{budget.AllocationAdmission=_=>{if(new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().DeclaringType==typeof(NavigationPreparedArrivalObservation)))observedBirth++;return true;};Assert.False(session.PreparePhysicalArrivalAttempt());}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,observedBirth,"The paid observation is the real cap boundary");Assert.AreSame(original,session.PendingRequestFor(id));var current=session.PinCommittedHostRoot();try{Assert.AreSame(root,current);}finally{current.Release();}
                Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,pressure),physical);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.LessOrEqual(budget.PeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                pressure.Release();pressure=null;root.Release();root=null;CollectionAssert.AreEqual(before,Bytes(),"Refused observation preserves the complete authority");
                Assert.True(session.PreparePhysicalArrivalAttempt());for(int n=0;n<20000&&session.PendingCount>0;n++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);Assert.True(session.Crowd.Units.Single(actor=>actor.Id==id).Moving);
                var prefix=Bytes();Pump(1);var suffix=Bytes();var facts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Pump(1);CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());
            }finally{budget.AllocationAdmission=null;pressure?.Release();root?.Release();}
        }
        [Test,Timeout(180000)] public void InstalledTrioWithForeignArrivalRecordsFirstStepAndFoundationReissue()
        {
            var folder=Environment.GetEnvironmentVariable("SPACEWARS_ARMY_MOTION_EVIDENCE");if(String.IsNullOrWhiteSpace(folder))folder=null;else System.IO.Directory.CreateDirectory(folder);
            kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");activeProfile=PlayableProfile.ThreeCrossingsDefault;
            domain=Activator.CreateInstance(Domain,F,null,new object[]{activeProfile,1L,false},null);session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);kernel=new CertifiedRouteKernel();
            int EnemyTank(double x,double z)=>(int)Call("SpawnUnit",new NavPoint(x,z),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            int[] trio={EnemyTank(42.39,46),EnemyTank(41.5,48.89),EnemyTank(39.5,48.89)};int foreign=Spawn(-42,-40);var goal=new NavPoint(-36,-46);
            var authority=new PlayableAuthorityTick((PlayableDomain)domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),-515260688,asynchronousRoutes:true);authority.EnableAccruedCredits();Assert.True(session.AccruedCreditsEnabled);
            var stages=new System.Collections.Generic.List<object>();var callbacks=new System.Collections.Generic.List<object>();CertifiedRouteWorker worker=null;string result="PREPARATION_NOT_RUN",stage="prepare-certified-trio";
            object Hidden(object value,string name)=>value?.GetType().GetField(name,F|BindingFlags.Public)?.GetValue(value);
            int Allocating()=>((System.Collections.Generic.IDictionary<int,NavigationRequest>)typeof(NavigationSession).GetField("allocating",F).GetValue(session)).Count;
            var originalGoals=new System.Collections.Generic.Dictionary<int,NavPoint>();
            object Quantum()
            {
                var root=session.PinCommittedHostRoot();var bank=session.PinCreditBank();try{return new{pinnedRoot=root==null?0:System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(root),serial=root?.QuantumSerial,tick=root?.QuantumTick,budget=root?.QuantumBudget,prefix=root?.AdmissionPrefix,consumed=root?.ConsumedQuantum,complete=root?.QuantumCompleted,queued=bank==null?(long?)null:bank[NavigationAccruedCredits.Count],promoted=bank==null?(long?)null:bank[NavigationAccruedCredits.Promoted],earned=bank==null?(long?)null:bank[NavigationAccruedCredits.Accepted]};}finally{root?.Release();bank?.Release();}
            }
            object Snapshot(string label)
            {
                using((IDisposable)typeof(NavigationSession).GetMethod("EnterTransport",F).Invoke(session,null)){
                    var goals=(System.Collections.Generic.IDictionary<int,NavPoint>)typeof(NavigationSession).GetField("retainedGoals",F).GetValue(session);
                    var reservations=(System.Collections.Generic.IDictionary<int,NavPoint>)typeof(NavigationSession).GetField("reservations",F).GetValue(session);
                    var pending=(System.Collections.Generic.IDictionary<int,NavigationRequest>)typeof(NavigationSession).GetField("pending",F).GetValue(session);
                    var allocations=(System.Collections.Generic.IDictionary<int,NavigationRequest>)typeof(NavigationSession).GetField("allocating",F).GetValue(session);
                    var queues=(IDictionary)Domain.GetField("tacticalQueues",F).GetValue(domain);
                    var installed=(NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);
                    var row=new{stage=label,hostQuantum=Quantum(),tick=Domain.GetProperty("Tick",F|BindingFlags.Public).GetValue(domain),geometry=session.AuthorityGeometry.Revision,globalAllocating=allocations.Keys.OrderBy(x=>x).ToArray(),globalPending=pending.Keys.OrderBy(x=>x).ToArray(),session.SuspendPendingMovement,session.LastNavigationStepPath,session.LastOwnedMovementNullReason,session.LastOwnedMovementNullDetail,session.LastOwnedMovementRefusal,session.LastOwnedMovementRefusalMessage,session.LastOwnedMovementComputation,session.LastTrafficDispatchStage,session.LastAnswerNamesFailure,session.LastResultBatchFailure,session.OwnedMovementPublications,session.OwnedRebindPublications,workerFailure=worker?.Publication.Failure,
                        actors=trio.Select(id=>{var a=session.Crowd.Units.Single(u=>u.Id==id);var execution=session.InstalledExecutionFor(id);var q=queues.Contains(id)?queues[id]:null;var active=Hidden(q,"Active");var waiting=Hidden(q,"Pending");return new{id,a.Position,a.Location,a.Goal,a.GoalLocation,a.Heading,a.Radius,a.Speed,a.TurnSpeed,a.Moving,a.Held,a.Outcome,a.MobilityRevision,a.routeIndex,a.localIndex,routePoints=a.Route.Count,globalRoute=a.Route.ToArray(),localRoute=a.localRoute.ToArray(),installedRoute=execution?.InstalledRoute,corridor=execution?.Corridor,installed=execution!=null,installedOwner=installed.OwnerFor(id)==null?0:System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(installed.OwnerFor(id)),a.InstalledGroupId,a.InstalledRevision,a.InstalledRequest,a.InstalledOrder,retained=goals.ContainsKey(id),retainedGoal=goals.TryGetValue(id,out var g)?(NavPoint?)g:null,reserved=reservations.ContainsKey(id),reservation=reservations.TryGetValue(id,out var r)?(NavPoint?)r:null,pending=pending.ContainsKey(id),allocating=allocations.ContainsKey(id),active=active!=null,activeSequence=active==null?(long?)null:((PlayableCommand)Hidden(Hidden(active,"Issuance"),"Command")).Sequence,activeAssigned=Hidden(active,"Assigned"),activeSuspended=Hidden(active,"Suspended"),activeFailed=Hidden(active,"Failed"),activeInterrupted=Hidden(active,"Interrupted"),activeAttemptRequest=Hidden(active,"AttemptRequest"),activeAttemptOrder=Hidden(active,"AttemptOrder"),pendingTactical=waiting!=null};}).ToArray()};stages.Add(row);return row;
                }
            }
            Action<NavigationRequest> requested=r=>callbacks.Add(new{kind="requested",stage,entity=r.Entity,r.Request,r.Order,r.Goal,r.MemberIdentity.GroupId});
            Action<int> cancelled=id=>callbacks.Add(new{kind="cancelled",stage,entity=id});
            try{
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"enemy-1",PlayableCommandKind.AttackMove,trio,goal,origin:PlayableOrderOrigin.Human),null));
                long trioSequence=command;worker=new CertifiedRouteWorker(Binding(),4,kernel);
                Assert.True(SpinWait.SpinUntil(()=>{Assert.True(authority.TryAdvance());return trio.All(id=>session.InstalledExecutionFor(id)!=null);},60000),worker.Publication.Failure??"Preparation: actual certified trio did not install within the fixture bound.");
                Assert.Null(worker.Publication.Failure);foreach(int id in trio){var a=session.Crowd.Units.Single(u=>u.Id==id);Assert.True(a.Moving,"Preparation: installed actor must be moving.");Assert.Greater(a.Route.Count,0);Assert.NotNull(session.InstalledExecutionFor(id));}
                var retained=(System.Collections.Generic.IDictionary<int,NavPoint>)typeof(NavigationSession).GetField("retainedGoals",F).GetValue(session);foreach(int id in trio){Assert.True(retained.ContainsKey(id));originalGoals[id]=retained[id];}
                int observedSteps=0;session.MovementDiagnosticObserver=_=>observedSteps++;session.RouteRequested+=requested;session.MovementCancelled+=cancelled;
                stage="foreign-arrival-before-first-step";Arrival(foreign,new NavPoint(36,40));Assert.Greater(Allocating(),0,"Preparation: a real foreign arrival must remain unfinished.");Snapshot(stage);
                var before=trio.Select(id=>session.Crowd.Units.Single(u=>u.Id==id)).Select(a=>(a.Position,a.Heading,a.routeIndex,a.localIndex)).ToArray();int publications=session.OwnedMovementPublications;
                stage="first-session-step";Assert.True(authority.TryAdvance());Snapshot(stage);Assert.AreEqual(1,observedSteps,"One real authority boundary must execute its normal Navigation.Step.");callbacks.Add(new{kind="first-step-result",path=session.LastNavigationStepPath,ownedPublications=session.OwnedMovementPublications-publications,poseChanged=trio.Select(id=>session.Crowd.Units.Single(u=>u.Id==id)).Select((u,i)=>!u.Position.Equals(before[i].Position)||u.Heading!=before[i].Heading).ToArray()});Assert.Null(worker.Publication.Failure);Assert.Greater(Allocating(),0,"The diagnostic must cover unfinished foreign allocation after Step too.");
                foreach(int id in trio){Assert.NotNull(session.InstalledExecutionFor(id),"Unrelated foreign work must not discard installed trio execution.");Assert.True(((System.Collections.Generic.IDictionary<int,NavPoint>)typeof(NavigationSession).GetField("retainedGoals",F).GetValue(session)).ContainsKey(id));}
                Assert.Greater(session.LastNavigationStepPath,0,"A real Step path must be observed.");
                if(session.LastNavigationStepPath==3){
                    Assert.AreEqual(publications,session.OwnedMovementPublications,"A refused prepared frame must not publish a partial movement.");
                    for(int i=0;i<trio.Length;i++){var actor=session.Crowd.Units.Single(u=>u.Id==trio[i]);Assert.AreEqual(before[i].Position,actor.Position,"Refusal keeps each actual position.");Assert.AreEqual(before[i].Heading,actor.Heading,"Refusal keeps each actual heading.");Assert.AreEqual(before[i].routeIndex,actor.routeIndex,"Refusal keeps each installed route cursor.");Assert.AreEqual(before[i].localIndex,actor.localIndex,"Refusal keeps each local cursor.");}
                    callbacks.Add(new{kind="atomic-first-step-refusal",stage,exactGuard=session.LastOwnedMovementNullDetail,refusal=session.LastOwnedMovementRefusal,allTrioPoseAndCursorUnchanged=true});
                }

                stage="foundation-pending";Call("AddCredits",PlayableOwner.Enemy,10000d);var buildings=(IDictionary)Domain.GetField("buildings",F).GetValue(domain);var parent=buildings.Values.Cast<object>().Single(v=>(PlayableBuildingKind)v.GetType().GetField("Kind").GetValue(v)==PlayableBuildingKind.Headquarters&&(PlayableOwner)v.GetType().GetField("Owner").GetValue(v)==PlayableOwner.Enemy);int parentId=(int)parent.GetType().GetField("Id").GetValue(parent);Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",2,2,PlayableBuildingKind.Factory,parentId,PlayableOwner.Enemy,null));
                var building=buildings.Values.Cast<object>().Single(v=>(PlayableBuildingKind)v.GetType().GetField("Kind").GetValue(v)==PlayableBuildingKind.Factory);var type=building.GetType();Assert.AreEqual(ConstructionPhase.Pending,type.GetField("Phase").GetValue(building));Snapshot(stage);int revision=session.AuthorityGeometry.Revision;
                stage="foundation-geometry-publication";int foundationCalls=0;Assert.True(SpinWait.SpinUntil(()=>{Call("AdvanceFoundations");foundationCalls++;return (ConstructionPhase)type.GetField("Phase").GetValue(building)==ConstructionPhase.Constructing;},5000),"Normal foundation source refusals may retry while the worker advances; the actual publication must complete.");callbacks.Add(new{kind="foundation-publication",calls=foundationCalls});Snapshot(stage);Assert.Greater(Allocating(),0,"Foreign arrival must remain unfinished through foundation publication.");Assert.AreEqual(ConstructionPhase.Constructing,type.GetField("Phase").GetValue(building));Assert.AreEqual(revision+1,session.AuthorityGeometry.Revision);Assert.AreSame(session.AuthorityGeometry,Domain.GetProperty("Geometry",F).GetValue(domain));Assert.AreEqual(1,session.OwnedRebindPublications);
                var reissued=(System.Collections.Generic.IDictionary<int,NavPoint>)typeof(NavigationSession).GetField("retainedGoals",F).GetValue(session);
                foreach(int id in trio){Assert.IsNull(session.InstalledExecutionFor(id),"Geometry publication deliberately retires the old installed execution.");Assert.True(reissued.TryGetValue(id,out var saved)&&saved.Equals(originalGoals[id]),"Foundation rebind must retain each original assigned goal.");Assert.True(session.IsPending(id),"Each retired installed trio actor must have an actual retry intent.");Assert.AreEqual(1,callbacks.Count(x=>(string)x.GetType().GetProperty("kind").GetValue(x)=="requested"&&(int)x.GetType().GetProperty("entity").GetValue(x)==id),"Exactly one production retry callback per installed trio actor.");}
                var queuesAfter=(IDictionary)Domain.GetField("tacticalQueues",F).GetValue(domain);var reservationsAfter=(System.Collections.Generic.IDictionary<int,NavPoint>)typeof(NavigationSession).GetField("reservations",F).GetValue(session);
                foreach(int id in trio){Assert.True(reservationsAfter.TryGetValue(id,out var reservation)&&reservation.Equals(originalGoals[id]),"Foundation retains each assigned reservation.");var active=Hidden(queuesAfter[id],"Active");Assert.NotNull(active,"Foundation retry retains the active tactical execution.");Assert.AreEqual(trioSequence,((PlayableCommand)Hidden(Hidden(active,"Issuance"),"Command")).Sequence,"Foundation does not replace the original AttackMove intent.");}
                Assert.Zero(callbacks.Count(x=>x.GetType().GetProperty("kind").GetValue(x).Equals("cancelled")),"This unblocked foundation must not cancel trio or foreign intent.");
                result="DIAGNOSTIC_COMPONENT_PASS_ORDINARY_ARMY_FAIL_REMAINS";
            }
            catch(Exception e){result=stage.StartsWith("prepare")||stage.StartsWith("foreign-arrival")?"PREPARATION_FAILURE":"DIAGNOSTIC_FAILURE";callbacks.Add(new{kind="exception",stage,detail=e.ToString()});throw;}
            finally{
                session.MovementDiagnosticObserver=null;session.RouteRequested-=requested;session.MovementCancelled-=cancelled;worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000),"Worker stop ACK");
                var json=Newtonsoft.Json.JsonConvert.SerializeObject(new{result,stage,profile=activeProfile.ProfileId,activeProfile.Revision,map=activeProfile.AuthoredMap.Id,mode="actual Domain constructor, scriptedEnemyPatrol=false; fresh async accrued-v3 authority, empty strategic owner scheduler, original seed; threaded certified host; Human Enemy AttackMove; Factory foundation site2/slot2; not an Army mission witness",trio,foreign,stages,callbacks,ledger=new{session.PhysicalHostBudget.PhysicalBytes,session.PhysicalHostBudget.HeldBytes,session.PhysicalHostBudget.ActiveScopes}},Newtonsoft.Json.Formatting.Indented);if(folder==null)TestContext.Out.WriteLine(json);else System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"diagnostic.json"),json);
            }
        }

        [Test,Timeout(180000)] public void InstalledTrioMovesWhileGroupedForeignMemberIsAllocating()
        {
            kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");activeProfile=PlayableProfile.ThreeCrossingsDefault;
            domain=Activator.CreateInstance(Domain,F,null,new object[]{activeProfile,1L,false},null);session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);kernel=new CertifiedRouteKernel();
            int EnemyTank(double x,double z)=>(int)Call("SpawnUnit",new NavPoint(x,z),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            int[] trio={EnemyTank(42.39,46),EnemyTank(41.5,48.89),EnemyTank(39.5,48.89)};int foreign=Spawn(-42,-40);
            var authority=new PlayableAuthorityTick((PlayableDomain)domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),-515260688,asynchronousRoutes:true);authority.EnableAccruedCredits();
            CertifiedRouteWorker worker=null;
            var allocating=(System.Collections.Generic.IDictionary<int,NavigationRequest>)typeof(NavigationSession).GetField("allocating",F).GetValue(session);
            try{
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"enemy-1",PlayableCommandKind.AttackMove,trio,new NavPoint(-36,-46),origin:PlayableOrderOrigin.Human),null));
                worker=new CertifiedRouteWorker(Binding(),4,kernel);
                Assert.True(SpinWait.SpinUntil(()=>{Assert.True(authority.TryAdvance());return trio.All(id=>session.InstalledExecutionFor(id)!=null);},60000),worker.Publication.Failure??"Preparation: actual certified trio did not install within the fixture bound.");
                Assert.Null(worker.Publication.Failure);
                foreach(int id in trio){var actor=session.Crowd.Units.Single(u=>u.Id==id);Assert.True(actor.Moving);Assert.Greater(actor.Route.Count,0);Assert.NotNull(session.InstalledExecutionFor(id));}
                worker.Dispose();Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000),"Worker stop ACK before the stable mixed-allocation step.");worker=null;

                Arrival(foreign,new NavPoint(36,40));
                allocating=(System.Collections.Generic.IDictionary<int,NavigationRequest>)typeof(NavigationSession).GetField("allocating",F).GetValue(session);
                var foreignGroup=session.GroupFor(foreign);Assert.NotNull(foreignGroup,"The actual Move must bind the allocating actor into its authoritative command group.");
                Assert.AreEqual(GroupMemberPhase.Allocating,foreignGroup.Members.Single(m=>m.Entity==foreign).Phase);
                Assert.True(allocating.ContainsKey(foreign),"The same grouped actor must still have an actual unfinished route allocation.");
                var before=trio.Select(id=>session.Crowd.Units.Single(u=>u.Id==id)).Select(a=>(a.Position,a.Heading,a.routeIndex,a.localIndex)).ToArray();
                int publications=session.OwnedMovementPublications;
                session.MovementDiagnosticObserver=_=>{};
                long serial,tick,consumed,prefix;int budget;bool completed;
                var hostBefore=session.PinCommittedHostRoot();try{serial=hostBefore?.QuantumSerial??0;tick=hostBefore?.QuantumTick??0;consumed=hostBefore?.ConsumedQuantum??0;prefix=hostBefore?.AdmissionPrefix??0;budget=hostBefore?.QuantumBudget??0;completed=hostBefore?.QuantumCompleted??false;}finally{hostBefore?.Release();}
                Action AssertHostQuantumStable=()=>{var root=session.PinCommittedHostRoot();try{Assert.AreEqual(serial,root?.QuantumSerial??0,"Direct Navigation.Step must not advance the host quantum.");Assert.AreEqual(tick,root?.QuantumTick??0);Assert.AreEqual(consumed,root?.ConsumedQuantum??0);Assert.AreEqual(prefix,root?.AdmissionPrefix??0);Assert.AreEqual(budget,root?.QuantumBudget??0);Assert.AreEqual(completed,root?.QuantumCompleted??false);}finally{root?.Release();}};

                session.Step(1d/30);

                Assert.Greater(session.LastNavigationStepPath,0,"A real stable-host Navigation.Step must be observed.");
                Assert.AreEqual(publications+1,session.OwnedMovementPublications,$"The certified movers must publish through the owned path. Phase diagnostic: path={session.LastNavigationStepPath}, reason={session.LastOwnedMovementNullReason}, detail={session.LastOwnedMovementNullDetail}, actor={session.LastOwnedMovementNullActor}, group={session.LastOwnedMovementNullGroup}; computation={session.LastOwnedMovementComputation}; before={string.Join(";",before.Select(x=>$"{x.Position}:{x.Heading}:{x.routeIndex}:{x.localIndex}"))}; after={string.Join(";",trio.Select(id=>{var a=session.Crowd.Units.Single(u=>u.Id==id);return $"{a.Position}:{a.Heading}:{a.routeIndex}:{a.localIndex}";}))}");
                Assert.AreEqual(0,session.LastOwnedMovementNullReason,"A successful owned movement publication must not retain a no-op diagnostic.");
                AssertHostQuantumStable();
                // At the 4.5 rad/s turn cap, pi radians takes at most 21 exact
                // 1/30-second steps; 30 normal steps allow a complete turn and
                // prove subsequent physical progress without changing dt.
                for(int step=0;step<30;step++){
                    allocating=(System.Collections.Generic.IDictionary<int,NavigationRequest>)typeof(NavigationSession).GetField("allocating",F).GetValue(session);
                    Assert.True(allocating.ContainsKey(foreign),"Foreign allocation must remain live during the fixed continuation.");
                    int stepPublications=session.OwnedMovementPublications;session.Step(1d/30);
                    Assert.AreEqual(stepPublications+1,session.OwnedMovementPublications,"Every continuation must use the owned movement path.");
                    Assert.AreEqual(0,session.LastOwnedMovementNullReason);AssertHostQuantumStable();
                }
                allocating=(System.Collections.Generic.IDictionary<int,NavigationRequest>)typeof(NavigationSession).GetField("allocating",F).GetValue(session);
                int moved=0;for(int i=0;i<trio.Length;i++){var actor=session.Crowd.Units.Single(u=>u.Id==trio[i]);if(!before[i].Position.Equals(actor.Position)||before[i].routeIndex!=actor.routeIndex||before[i].localIndex!=actor.localIndex)moved++;Assert.NotNull(session.InstalledExecutionFor(trio[i]));}
                Assert.Greater(moved,0,"The same-dt continuation must advance at least one installed actor position or route cursor.");
                Assert.True(allocating.ContainsKey(foreign),"The diagnostic must leave the actual foreign allocation intact.");
            }
            finally{
                session.MovementDiagnosticObserver=null;
                worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000),"Worker stop ACK");
            }
        }

        private void ReadyForeignAllocationBoundary(bool keepHostToken)
        {
            kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");activeProfile=PlayableProfile.ThreeCrossingsDefault;
            domain=Activator.CreateInstance(Domain,F,null,new object[]{activeProfile,1L,false},null);session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);kernel=new CertifiedRouteKernel();
            int EnemyTank(double x,double z)=>(int)Call("SpawnUnit",new NavPoint(x,z),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            int[] trio={EnemyTank(42.39,46),EnemyTank(41.5,48.89),EnemyTank(39.5,48.89)};int foreign=Spawn(-42,-40);
            var authority=new PlayableAuthorityTick((PlayableDomain)domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),-515260688,asynchronousRoutes:true);authority.EnableAccruedCredits();
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"enemy-1",PlayableCommandKind.AttackMove,trio,new NavPoint(-36,-46),origin:PlayableOrderOrigin.Human),null));
            // Saved credits belong to the original admission prefix. The normal later
            // foreign request cannot consume this FIFO while the original routes finish.
            for(int credit=0;credit<600;credit++)Assert.True(authority.TryAdvance());
            long originalPrefix=session.Admission.PublishedThroughRequestSequence;
            Arrival(foreign,new NavPoint(36,40));
            var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);var allocatingField=typeof(NavigationSession).GetField("allocating",F);var barrierAnswersField=typeof(NavigationSession).GetField("barrierAnswers",F);
            NavigationRequest foreignRequest;lock(gate){foreignRequest=session.PendingRequestFor(foreign);}
            NavigationAnswer[] AnswerSnapshot(){lock(gate){var barrier=(System.Collections.IEnumerable)barrierAnswersField.GetValue(session);return session.Answers.CopyItems().Concat(barrier.Cast<NavigationAnswer>()).ToArray();}}
            bool ForeignStillAllocating(){lock(gate){var current=(System.Collections.Generic.IDictionary<int,NavigationRequest>)allocatingField.GetValue(session);return current.TryGetValue(foreign,out var request)&&ReferenceEquals(request,foreignRequest);}}
            bool HasUnfinishedReadyArrivalFor(System.Collections.Generic.IEnumerable<int> entityIds){var ids=new System.Collections.Generic.HashSet<int>(entityIds);lock(gate){var attempts=(System.Collections.IEnumerable)typeof(NavigationSession).GetField("arrivalAttempts",F).GetValue(session);if(attempts==null)return false;foreach(var pair in attempts){var attempt=(NavigationSession.ArrivalAttempt)pair.GetType().GetProperty("Value").GetValue(pair);if(attempt.Intents!=null&&attempt.Intents.Any(intent=>ids.Contains(intent.Entity))&&!attempt.Retiring&&!attempt.Working&&!attempt.Reconstructing&&!attempt.Published&&attempt.SnapshotStatus!=NavArrivalStatus.Pending&&attempt.SnapshotStatus!=NavArrivalStatus.BudgetExhausted)return true;}return false;}}
            lock(gate){var foreignGroup=session.GroupFor(foreign);Assert.NotNull(foreignRequest);Assert.NotNull(foreignGroup);Assert.AreEqual(GroupMemberPhase.Allocating,foreignGroup.Members.Single(m=>m.Entity==foreign).Phase);Assert.True(ForeignStillAllocating());}
            var diagnosticRows=new System.Collections.Generic.List<string>();string diagnosticResult="PREPARATION_FAILURE";
            int deniedInstallAdmission=0;var budget=session.PhysicalHostBudget;
            // Barrier-only preparation cannot consume answers or install execution.
            // No admission callback or per-allocation stack walking is needed.
            CertifiedRouteWorker worker=null;var heldBoundary=new ManualResetEventSlim(false);var releaseBoundary=new ManualResetEventSlim(false);var freeBoundary=new ManualResetEventSlim(false);var allocation=typeof(NavigationSession).GetField("hostAllocationThread",F);
            try{
                worker=new CertifiedRouteWorker(Binding(),4,kernel);
                kernel.PauseAtPhysicalBoundary=()=>{
                    var stage=kernel.CapturePumpFrontier().Stage;
                    bool readyAndAllocating;lock(gate){var ready=AnswerSnapshot().Where(answer=>answer.Status==NavSolveStatus.Ready&&trio.Contains(answer.Request.Entity)).Select(answer=>answer.Request.Entity).Distinct().Count();readyAndAllocating=ready==trio.Length&&ForeignStillAllocating()&&trio.All(id=>session.InstalledExecutionFor(id)==null)&&ArrivalLeaseCount==0;}
                    if(!releaseBoundary.IsSet&&readyAndAllocating&&(int)allocation.GetValue(session)!=0&&(stage==CertifiedRouteKernel.PumpStage.Projection||stage==CertifiedRouteKernel.PumpStage.EndAllocation)){heldBoundary.Set();releaseBoundary.Wait();return false;}
                    if(!keepHostToken&&releaseBoundary.IsSet&&(int)allocation.GetValue(session)==0){freeBoundary.Set();return true;}
                    return false;
                };
                var bound=System.Diagnostics.Stopwatch.StartNew();while(!heldBoundary.IsSet&&bound.ElapsedMilliseconds<60000){session.PrepareDeliveryBarrier();heldBoundary.Wait(1);}
                object BatchValue(string name)=>typeof(NavigationSession).GetProperty(name,F)?.GetValue(session)??typeof(NavigationSession).GetField(name,F)?.GetValue(session);
                string BatchFacts()=>"stage="+BatchValue("LastResultBatchStage")+",failure="+BatchValue("LastResultBatchFailure")+",sourceReason="+BatchValue("LastResultBatchSourceReason")+",detail="+BatchValue("LastResultBatchDetail");
                string Snapshot(string phase){var root=session.PinCommittedHostRoot();string host;try{host=$"root={root?.GetHashCode()},hostStage={root?.Frontier.Stage},quantum={root?.QuantumSerial}/{root?.QuantumTick}/{root?.ConsumedQuantum}/{root?.AdmissionPrefix}/{root?.QuantumBudget}/{root?.QuantumCompleted},allocation={(int)allocation.GetValue(session)},held={budget.HeldBytes},scopes={budget.ActiveScopes}";}finally{root?.Release();}
                    var publication=worker?.Publication;lock(gate){var answers=AnswerSnapshot();var current=(System.Collections.Generic.IDictionary<int,NavigationRequest>)allocatingField.GetValue(session);return $"{phase}:authorityTick={authority.Tick},workerPumps={publication?.Pumps},workerEpoch={publication?.Epoch},workerFailure={publication?.Failure},projectionFailure={publication?.ProjectionFailure},answers=[{string.Join(";",answers.Select(answer=>$"{answer.Request.Entity}/{answer.Status}/req{answer.Request.Request}/ord{answer.Request.Order}/member{answer.Request.MemberIdentity?.GroupId}:{answer.Request.MemberIdentity?.Incarnation}:{answer.Request.MemberIdentity?.OrderRevision}/corridor{answer.Corridor?.GetHashCode()}"))}],requests={session.Requests.Count},pending={string.Join(",",trio.Select(id=>$"{id}:{session.PendingRequestFor(id)?.Request}"))},allocating={string.Join(",",current.Keys)},foreignPhase={session.GroupFor(foreign)?.Members.Single(m=>m.Entity==foreign).Phase},installed={string.Join(",",trio.Select(id=>$"{id}:{session.InstalledExecutionFor(id)?.GetHashCode()}"))},admissionRefusals={deniedInstallAdmission},batch={BatchFacts()},{host}";}
                }
                if(!heldBoundary.IsSet){diagnosticRows.Add(Snapshot("preparation-failure"));Assert.Fail("Preparation failure: expected all 3 actual original-trio Ready answers at a Projection or EndAllocation boundary with the foreign allocation token held within 60000ms.");}
                var heldAnswers=AnswerSnapshot();var readyAnswers=heldAnswers.Where(answer=>answer.Status==NavSolveStatus.Ready&&trio.Contains(answer.Request.Entity)).ToArray();var readyIds=readyAnswers.Select(answer=>answer.Request.Entity).Distinct().ToArray();Assert.AreEqual(trio.Length,readyIds.Length);
                Assert.Zero(ArrivalLeaseCount,"No active arrival projection lease at the park.");Assert.False(HasUnfinishedReadyArrivalFor(readyIds),"The actually Ready original members must not have an unfinished READY-arrival prefix awaiting projection; unrelated pending arrival work remains admissible.");
                foreach(var answer in readyAnswers){lock(gate)Assert.AreSame(session.PendingRequestFor(answer.Request.Entity),answer.Request);Assert.NotNull(answer.Request.MemberIdentity);Assert.NotNull(answer.Corridor);Assert.LessOrEqual(answer.Request.Request,originalPrefix,"Real original request binding stays eligible for saved-prefix credits.");}
                diagnosticRows.Add(Snapshot("parked-held"));
                if(!keepHostToken){
                    releaseBoundary.Set();Assert.True(freeBoundary.Wait(10000),"Preparation failure: worker did not release the allocation token at a stable pump boundary.");
                    Assert.Zero((int)allocation.GetValue(session));
                }
                diagnosticResult="MEASUREMENT_FAILURE";budget.AllocationAdmission=null;var answersBefore=AnswerSnapshot();Assert.AreEqual(trio.Length,answersBefore.Length);int applied=session.AppliedResults,batches=session.OwnedResultBatchPublications,movements=session.OwnedMovementPublications;var actorBefore=trio.ToDictionary(id=>id,id=>session.Crowd.Units.Single(actor=>actor.Id==id).Position);
                session.Step(1d/30);diagnosticRows.Add(Snapshot("after-one-step"));
                Assert.AreEqual(answersBefore.Length,session.AppliedResults-applied,"The real Ready batch should drain atomically; "+Snapshot("drain-check"));
                if(keepHostToken){Assert.AreEqual(batches+1,session.OwnedResultBatchPublications,"Held-token Ready batch must publish atomically through the owned result carrier.");Assert.AreEqual(movements+1,session.OwnedMovementPublications);}
                Assert.Zero(AnswerSnapshot().Length);foreach(int id in readyIds)Assert.Null(session.PendingRequestFor(id));Assert.AreSame(foreignRequest,session.PendingRequestFor(foreign));Assert.AreEqual(GroupMemberPhase.Allocating,session.GroupFor(foreign).Members.Single(m=>m.Entity==foreign).Phase);Assert.True(ForeignStillAllocating());
                foreach(int id in readyIds){Assert.NotNull(session.InstalledExecutionFor(id));Assert.NotNull(session.Crowd.Units.Single(actor=>actor.Id==id).StorageOwner.Global);}
                for(int step=0;step<30;step++){session.Step(1d/30);Assert.True(ForeignStillAllocating());Assert.AreEqual(GroupMemberPhase.Allocating,session.GroupFor(foreign).Members.Single(m=>m.Entity==foreign).Phase);}
                Assert.True(trio.Any(id=>!actorBefore[id].Equals(session.Crowd.Units.Single(actor=>actor.Id==id).Position)),"Fixed 1/30-second continuation must move an installed member while the foreign intent stays live.");
                diagnosticResult="PASS";
            }
            finally{
                budget.AllocationAdmission=null;releaseBoundary.Set();kernel.PauseAtPhysicalBoundary=null;
                worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000),"Worker stop ACK after releasing its physical-boundary park.");
                freeBoundary.Dispose();releaseBoundary.Dispose();heldBoundary.Dispose();
                var evidence=Environment.GetEnvironmentVariable("SPACEWARS_READY_ALLOCATION_EVIDENCE");if(!String.IsNullOrWhiteSpace(evidence)){System.IO.Directory.CreateDirectory(evidence);System.IO.File.WriteAllText(System.IO.Path.Combine(evidence,keepHostToken?"held-boundary.json":"free-boundary.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{diagnosticResult,diagnosticRows,ledger=new{budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes}},Newtonsoft.Json.Formatting.Indented));}
            }
            Assert.Zero(budget.HeldBytes,"All physical token reservations must be released after worker stop and teardown.");Assert.Zero(budget.ActiveScopes,"No allocation scopes remain after worker stop and teardown.");var creditBank=session.PinCreditBank();try{Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,creditBank),budget.PhysicalBytes,"Session plus its accrued credit bank must reconcile after worker stop and teardown.");}finally{creditBank?.Release();}
        }

        [Test,Timeout(180000)] public void ReadyOriginalTrioWithForeignAllocationInstallsAtStableFreeToken(){ReadyForeignAllocationBoundary(false);}
        [Test,Timeout(180000)] public void ReadyOriginalTrioWithForeignAllocationInstallsAtHeldToken(){ReadyForeignAllocationBoundary(true);}

        [Test] public void GeometryReissuePublishesSuccessfulThenRejectedRetainedGoalsAndRefusesLateWithoutLeaking()
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-10);Assert.True(session.Move(a,new NavPoint(-10,-14)));Assert.True(session.Move(b,new NavPoint(-10,-10)));
            for(int i=0;i<20000&&session.PendingCount>0;i++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);Assert.True(session.Crowd.TryGet(a,out var first));Assert.True(session.Crowd.TryGet(b,out var second));Assert.True(first.Moving);Assert.True(second.Moving);
            long am=first.MobilityRevision,bm=second.MobilityRevision;var geometry=(NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain);var next=new NavGeometry(geometry.HalfExtent,geometry.Obstacles.Concat(new[]{new NavObstacle(-11,-11,-9,-9)}).ToArray(),geometry.Revision+1);
            var budget=session.PhysicalHostBudget;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var before=frozen.Encode();long charge=budget.PhysicalBytes;int denied=0,underGate=0,canceled=0,requested=0;
            Action<int> cancel=id=>{Assert.False(Monitor.IsEntered(gate));Assert.AreEqual(b,id);canceled++;};Action<NavigationRequest> request=r=>{Assert.False(Monitor.IsEntered(gate));Assert.AreEqual(a,r.Entity);Assert.NotNull(r.StorageOwner);requested++;};session.MovementCancelled+=cancel;session.RouteRequested+=request;
            try{
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){denied++;return false;}return true;};Assert.False(session.TryChangeGeometry(next));}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.Zero(canceled);Assert.Zero(requested);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());CollectionAssert.AreEqual(before,frozen.Encode());
                Assert.True(session.TryChangeGeometry(next));Domain.GetProperty("Geometry",F).SetValue(domain,session.AuthorityGeometry);Assert.AreEqual(1,canceled);Assert.AreEqual(1,requested);Assert.AreEqual(1,session.OwnedRebindPublications);Assert.Zero(session.LastOwnedRebindPublicationAllocatedBytes);Assert.AreEqual(am,first.MobilityRevision);Assert.AreEqual(bm,second.MobilityRevision);Assert.False(first.Moving);Assert.False(second.Moving);Assert.IsNull(first.StorageOwner.Global);Assert.IsNull(second.StorageOwner.Global);Assert.IsNull(session.InstalledExecutionFor(a));Assert.IsNull(session.InstalledExecutionFor(b));Assert.AreEqual(1,session.Requests.Count);Assert.AreEqual(a,session.Requests.CopyItems().Single().Entity);CollectionAssert.AreEqual(before,frozen.Encode());PhysicalShape(frozen);frozen.Dispose();frozen=null;Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
                var prefix=ProbeBytes();PumpProbe(1);var suffix=ProbeBytes();string facts=HostFacts();RestoreProbeBytes(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,ProbeBytes(),"Session-only custom obstacle cold prefix");PumpProbe(1);CollectionAssert.AreEqual(suffix,ProbeBytes(),"Session-only custom obstacle next Pump");Assert.AreEqual(facts,HostFacts());
            }finally{budget.AllocationAdmission=null;if(session!=null){session.MovementCancelled-=cancel;session.RouteRequested-=request;}frozen?.Dispose();}
        }

        [Test,Timeout(180000)] public void TransientPhysicalArrivalRefusalPreservesPaidHistoricalContinuation()
        {
            kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");activeProfile=PlayableProfile.ThreeCrossingsDefault;
            domain=Activator.CreateInstance(Domain,F,null,new object[]{activeProfile,1L,false},null);session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);kernel=new CertifiedRouteKernel();
            int entity=Spawn(-42,-40);using(var idle=session.CaptureState()){}Arrival(entity,new NavPoint(-36,-46));Assert.True(session.PreparePhysicalArrivalAttempt(),"the normal physical-arrival preparation publishes owned Work");
            var attemptsField=typeof(NavigationSession).GetField("arrivalAttempts",F);
            NavigationSession.ArrivalAttempt AttemptFor(int id)
            {
                var attempts=(IEnumerable)attemptsField.GetValue(session);
                foreach(var pair in attempts){var a=(NavigationSession.ArrivalAttempt)pair.GetType().GetProperty("Value").GetValue(pair);if(a.Intents.Any(i=>i.Entity==id))return a;}
                return null;
            }
            NavigationHostRoot Root()=>session.PinCommittedHostRoot();
            NavigationArrivalWork PaidWork()
            {
                var root=Root();try{if(root!=null)foreach(var pair in root.ArrivalWork){var work=(NavigationArrivalWork)pair.Value;if(work.IsHistoricalCandidate&&work.AllocationWorkConsumed>0)return work;}return null;}finally{root?.Release();}
            }
            for(int i=0;i<20000&&PaidWork()==null;i++)Pump(1);
            var original=PaidWork();Assert.NotNull(original,"the normal terrain-backed arrival must create paid historical-capable Work");
            Assert.IsInstanceOf<PartitionNavigationProvider>(original.PhysicalInput.Provider);
            var prior=AttemptFor(entity);Assert.NotNull(prior);Assert.AreSame(original,prior.Work);
            var geometry=(NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain);
            var next=new NavGeometry(geometry.HalfExtent,geometry.Obstacles.ToArray(),geometry.Revision+1);
            Domain.GetProperty("Geometry",F).SetValue(domain,next);session.ChangeGeometry(next);
            Assert.True(prior.Retiring||AttemptFor(entity)==null,"geometry replacement must retire or remove the old attempt");
            var retained=PaidWork();Assert.AreSame(original,retained,"the paid historical source must remain in the committed host root");
            Assert.True(session.PendingRequestFor(entity)!=null||session.AllocationCount>0,"same-command replacement must be allocating");
            Assert.True((bool)typeof(NavigationSession).GetMethod("HistoricalBirthPending",F).Invoke(session,new object[]{original}),"the current replacement must still protect the paid historical source");
            Assert.IsNull(session.NextRetiringArrivalProjection(),"no projection lease may precede the wrapper retirement");
            var replacementRoot=Root();try{Assert.True(replacementRoot.ArrivalWork.Any(pair=>ReferenceEquals(pair.Value,original)));}finally{replacementRoot?.Release();}
            var replacementRequest=session.PendingRequestFor(entity);Assert.NotNull(replacementRequest);long replacementIdentity=replacementRequest.Request,quantumIdentity,commandIdentity=session.GroupFor(entity).CommandSequence;
            var beforeQuantum=Root();try{quantumIdentity=beforeQuantum.QuantumSerial;}finally{beforeQuantum?.Release();}

            var budget=session.PhysicalHostBudget;var ledgerGate=typeof(NavigationRetentionBudget).GetField("gate",F).GetValue(budget);
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            Thread resolver=null;Exception resolverError=null;bool resolutionObserved=false;int scopeAdmissions=0;bool firstRefusedWhileResolving=false,secondWasForcedRetirement=false;
            Action resolved=()=>{lock(ledgerGate){resolutionObserved=true;Monitor.PulseAll(ledgerGate);}};session.HostChanged+=resolved;
            try{
                resolver=new Thread(()=>{
                    NavigationRetentionBudget.Scope scope=null;
                    try{if(!budget.TryOpenScope(NavigationPreparationKind.Capture,session.Generation,0,out scope))throw new InvalidOperationException("Resolver fixture scope admission refused.");var reservation=scope.OwnedReservation();using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(128);allocation.Register(new ResolvingBirth(entered,release));}scope.Discard();}
                    catch(Exception error){resolverError=error;entered.Set();}finally{scope?.Discard();}
                }){IsBackground=true};resolver.Start();Assert.True(entered.Wait(5000),"the resolver must be paused while releasing its real scope");Assert.IsNull(resolverError);
                budget.AllocationAdmission=bytes=>{
                    if(bytes!=NavigationRetentionBudget.ScopeBytes)return true;
                    var frames=new System.Diagnostics.StackTrace().GetFrames();if(!frames.Any(frame=>frame.GetMethod().DeclaringType==typeof(NavigationRetentionBudget)&&frame.GetMethod().Name=="TryOpenScope"))return true;
                    scopeAdmissions++;
                    if(scopeAdmissions==1){firstRefusedWhileResolving=(int)typeof(NavigationRetentionBudget).GetField("resolvingScopes",F).GetValue(budget)>0;return true;}
                    if(scopeAdmissions==2){secondWasForcedRetirement=frames.Any(frame=>frame.GetMethod().Name=="RetirePhysicalArrivalOwner");release.Set();while(!resolutionObserved)Assert.True(Monitor.Wait(ledgerGate,5000),"HostChanged must observe completed resolution");return true;}
                    return true;
                };
                Assert.False(session.PreparePhysicalArrivalAttempt(),"the first transient admission refusal leaves the normal replacement attempt pending");
            }finally{
                budget.AllocationAdmission=null;release.Set();if(resolver!=null)Assert.True(resolver.Join(5000),"resolver must finish before fixture cleanup");session.HostChanged-=resolved;
            }
            Assert.IsNull(resolverError);Assert.True(firstRefusedWhileResolving,"the wrapper's first admitted callback must observe resolvingScopes");Assert.AreEqual(1,scopeAdmissions,"transient resolution refusal must return before a retirement admission");Assert.False(secondWasForcedRetirement,"transient resolution refusal must not force historical retirement");
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            Assert.AreEqual(replacementIdentity,session.PendingRequestFor(entity)?.Request,"the live replacement request must remain the same");var afterQuantum=Root();try{Assert.AreEqual(quantumIdentity,afterQuantum.QuantumSerial,"transient refusal cannot mint or consume a quantum");}finally{afterQuantum?.Release();}
            var after=Root();bool oldStillRetained;try{oldStillRetained=after.ArrivalWork.Any(pair=>ReferenceEquals(pair.Value,original));}finally{after?.Release();}
            Assert.True(oldStillRetained,"a transient resolution refusal must preserve paid historical Work until its replacement is born");
            Assert.True(session.PreparePhysicalArrivalAttempt(),"the ordinary replacement preparation must resume after resolution");
            var current=AttemptFor(entity);Assert.NotNull(current);Assert.AreSame(original,current.Work.HistoricalSource,"replacement Work must continue from the exact paid historical source");
            Assert.AreEqual(prior.Group,session.GroupFor(entity).GroupId);Assert.AreEqual(commandIdentity,session.GroupFor(entity).CommandSequence);
            TestContext.WriteLine("TRANSIENT_HISTORICAL_PRECONDITION status="+original.Status+" paid="+original.AllocationWorkConsumed+" resolvingRefusal="+firstRefusedWhileResolving+" retirementAdmission="+scopeAdmissions+" retained="+oldStillRetained+" replacementHistoricalSource="+ReferenceEquals(original,current.Work.HistoricalSource));

            int fallbackAdmissions=0;bool capacityFallbackRetirement=false;
            budget.AllocationAdmission=bytes=>{
                if(bytes!=NavigationRetentionBudget.ScopeBytes)return true;
                var frames=new System.Diagnostics.StackTrace().GetFrames();if(!frames.Any(frame=>frame.GetMethod().DeclaringType==typeof(NavigationRetentionBudget)&&frame.GetMethod().Name=="TryOpenScope"))return true;
                fallbackAdmissions++;if(fallbackAdmissions==1)return false;
                if(fallbackAdmissions==2)capacityFallbackRetirement=frames.Any(frame=>frame.GetMethod().Name=="RetirePhysicalArrivalOwner");
                return true;
            };
            bool capacityPrepared;try{capacityPrepared=session.PreparePhysicalArrivalAttempt();}finally{budget.AllocationAdmission=null;}
            Assert.False(capacityPrepared,"the injected admission refusal must remain a refusal");Assert.AreEqual(2,fallbackAdmissions,"one injected admission refusal followed by the existing retirement fallback");Assert.True(capacityFallbackRetirement,"the second admitted scope is the wrapper's ordinary retirement fallback");
            var fallbackRoot=Root();bool oldRetired;try{oldRetired=!fallbackRoot.ArrivalWork.Any(pair=>ReferenceEquals(pair.Value,original));Assert.True(fallbackRoot.ArrivalWork.Any(pair=>ReferenceEquals(pair.Value,current.Work)));}finally{fallbackRoot?.Release();}
            Assert.True(oldRetired,"ordinary injected admission refusal keeps the existing historical retirement fallback");Assert.AreSame(original,current.Work.HistoricalSource,"the born replacement retains its paid source across owner retirement");
            Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
            TestContext.WriteLine("INJECTED_ADMISSION_FALLBACK admissions="+fallbackAdmissions+" retiredHistoricalOwner="+oldRetired+" replacementKeepsPaidSource="+ReferenceEquals(original,current.Work.HistoricalSource));
        }
        [Test,Timeout(180000)] public void PhysicalArrivalCapacityRefusalRetiresPaidWorkBeforeReplacementBirth()
        {
            kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");activeProfile=PlayableProfile.ThreeCrossingsDefault;
            domain=Activator.CreateInstance(Domain,F,null,new object[]{activeProfile,1L,false},null);session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);kernel=new CertifiedRouteKernel();
            int entity=Spawn(-42,-40);using(var idle=session.CaptureState()){}Arrival(entity,new NavPoint(-36,-46));Assert.True(session.PreparePhysicalArrivalAttempt(),"the normal physical-arrival preparation publishes owned Work");
            var attemptsField=typeof(NavigationSession).GetField("arrivalAttempts",F);
            NavigationSession.ArrivalAttempt AttemptFor(int id)
            {
                var attempts=(IEnumerable)attemptsField.GetValue(session);
                foreach(var pair in attempts){var attempt=(NavigationSession.ArrivalAttempt)pair.GetType().GetProperty("Value").GetValue(pair);if(attempt.Intents.Any(i=>i.Entity==id))return attempt;}
                return null;
            }
            NavigationHostRoot Root()=>session.PinCommittedHostRoot();
            NavigationArrivalWork PaidWork()
            {
                var root=Root();try{if(root!=null)foreach(var pair in root.ArrivalWork){var work=(NavigationArrivalWork)pair.Value;if(work.IsHistoricalCandidate&&work.AllocationWorkConsumed>0)return work;}return null;}finally{root?.Release();}
            }
            for(int i=0;i<20000&&PaidWork()==null;i++)Pump(1);
            var original=PaidWork();Assert.NotNull(original,"the normal terrain-backed arrival must create paid historical-capable Work");
            Assert.IsInstanceOf<PartitionNavigationProvider>(original.PhysicalInput.Provider);
            var prior=AttemptFor(entity);Assert.NotNull(prior);Assert.AreSame(original,prior.Work);
            var geometry=(NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain);
            var next=new NavGeometry(geometry.HalfExtent,geometry.Obstacles.ToArray(),geometry.Revision+1);
            Domain.GetProperty("Geometry",F).SetValue(domain,next);session.ChangeGeometry(next);
            Assert.True(prior.Retiring||AttemptFor(entity)==null,"geometry replacement must retire or remove the old attempt");
            Assert.AreSame(original,PaidWork(),"paid historical Work remains before the injected refusal");
            Assert.True((bool)typeof(NavigationSession).GetMethod("HistoricalBirthPending",F).Invoke(session,new object[]{original}),"the allocating replacement protects the historical source before refusal");
            Assert.IsNull(session.NextRetiringArrivalProjection(),"no projection lease may precede the wrapper retirement");
            var request=session.PendingRequestFor(entity);Assert.NotNull(request);long requestIdentity=request.Request;
            long commandIdentity=session.GroupFor(entity).CommandSequence,quantumIdentity,allocationCount=session.AllocationCount,groupIdentity=session.GroupFor(entity).GroupId;
            var before=Root();try{quantumIdentity=before.QuantumSerial;}finally{before?.Release();}
            var budget=session.PhysicalHostBudget;int admissions=0;bool forcedRetirement=false;
            budget.AllocationAdmission=bytes=>{
                if(bytes!=NavigationRetentionBudget.ScopeBytes)return true;
                var frames=new System.Diagnostics.StackTrace().GetFrames();if(!frames.Any(frame=>frame.GetMethod().DeclaringType==typeof(NavigationRetentionBudget)&&frame.GetMethod().Name=="TryOpenScope"))return true;
                admissions++;if(admissions==1)return false;
                if(admissions==2)forcedRetirement=frames.Any(frame=>frame.GetMethod().Name=="RetirePhysicalArrivalOwner");
                return true;
            };
            bool prepared;try{prepared=session.PreparePhysicalArrivalAttempt();}finally{budget.AllocationAdmission=null;}
            Assert.False(prepared,"the first capacity refusal still refuses the physical arrival");Assert.AreEqual(2,admissions,"the wrapper retries only for its historical-owner retirement fallback");Assert.True(forcedRetirement,"the fallback admission must be RetirePhysicalArrivalOwner");
            var afterRetirement=Root();try{Assert.False(afterRetirement.ArrivalWork.Any(pair=>ReferenceEquals(pair.Value,original)),"capacity fallback must remove the historical source before replacement birth");}finally{afterRetirement?.Release();}
            Assert.AreEqual(requestIdentity,session.PendingRequestFor(entity)?.Request,"fallback preserves the same replacement request");Assert.AreEqual(allocationCount,session.AllocationCount,"fallback does not change allocation ownership");
            Assert.AreEqual(groupIdentity,session.GroupFor(entity).GroupId);Assert.AreEqual(commandIdentity,session.GroupFor(entity).CommandSequence);
            var afterQuantum=Root();try{Assert.AreEqual(quantumIdentity,afterQuantum.QuantumSerial,"fallback does not mint or consume a quantum");}finally{afterQuantum?.Release();}
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            Assert.True(session.PreparePhysicalArrivalAttempt(),"after the ordinary capacity fallback, replacement birth proceeds cold");
            var replacement=AttemptFor(entity);Assert.NotNull(replacement);Assert.AreNotSame(original,replacement.Work.HistoricalSource,"the replacement must not attach the retired paid historical source");
            var born=Root();try{Assert.True(born.ArrivalWork.Any(pair=>ReferenceEquals(pair.Value,replacement.Work)));Assert.False(born.ArrivalWork.Any(pair=>ReferenceEquals(pair.Value,original)));}finally{born?.Release();}
            Assert.AreEqual(requestIdentity,session.PendingRequestFor(entity)?.Request);Assert.AreEqual(groupIdentity,session.GroupFor(entity).GroupId);Assert.AreEqual(commandIdentity,session.GroupFor(entity).CommandSequence);
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            TestContext.WriteLine("CAPACITY_FALLBACK_BEFORE_BIRTH paid="+original.AllocationWorkConsumed+" admissions="+admissions+" oldRemoved="+(!ReferenceEquals(original,replacement.Work.HistoricalSource))+" requestSame="+(requestIdentity==session.PendingRequestFor(entity)?.Request)+" allocationCount="+allocationCount+" quantum="+quantumIdentity+" replacementHistoricalSource="+(replacement.Work.HistoricalSource==null));
        }
        [Test,Timeout(180000)] public void DoubleGeometryRebindRetainsOriginalPaidThreeMemberArrivalOracle()
        {
            kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");activeProfile=PlayableProfile.ThreeCrossingsDefault;
            domain=Activator.CreateInstance(Domain,F,null,new object[]{activeProfile,1L,false},null);session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);kernel=new CertifiedRouteKernel();using(var idle=session.CaptureState()){}
            int a=Spawn(42.39,46),b=Spawn(41.54353859762912,48.04353859762912),c=Spawn(39.5,48.89);int[] ids={a,b,c};
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Move,ids,new NavPoint(-36,-46),origin:PlayableOrderOrigin.Human),null));
            object Field(Type type,string name,object value)=>type.GetField(name,F).GetValue(value);
            NavigationArrivalWork WorkForGroup(long group)
            {var root=session.PinCommittedHostRoot();try{return root?.ArrivalWork.OrderBy(pair=>pair.Key).Select(pair=>(NavigationArrivalWork)pair.Value).LastOrDefault(work=>work.PhysicalInput.Members.Count==3&&work.PhysicalInput.Members[0].GroupId==group);}finally{root?.Release();}}
            int AssignmentCount(NavigationArrivalWork work)=> (int)typeof(NavigationArrivalWork).GetField("assignments",F).GetValue(work).GetType().GetProperty("Count").GetValue(typeof(NavigationArrivalWork).GetField("assignments",F).GetValue(work));
            object HistoricalSnapshot(NavigationArrivalWork work)=>Field(typeof(NavigationArrivalWork.Checkpoint),"Historical",work.Save());
            string HistoricalFacts(NavigationArrivalWork work)
            {
                var saved=HistoricalSnapshot(work);if(saved==null)return "none";var type=saved.GetType();var removal=Field(type,"Removal",saved) as int[];var occupancy=Field(type,"Page0Occupancy",saved) as int[];
                return "gate="+Field(type,"Gate",saved)+" cursor="+Field(type,"Cursor",saved)+" member="+Field(type,"Member",saved)+" leg="+Field(type,"Leg",saved)+" removal="+(removal==null?"null":string.Join(",",removal))+" occupancy="+(occupancy==null?"null":string.Join(",",occupancy));
            }
            string FrontierFacts(NavigationArrivalWork oracle)
            {
                var cp=oracle.Save();var scheduler=Field(typeof(NavigationArrivalWork.Checkpoint),"Scheduler",cp);var jobs=(Array)Field(scheduler.GetType(),"Jobs",scheduler);int demandJobs=0,exact=0,frontier=0;string scalarSha="none";
                foreach(var job in jobs){if(job==null)continue;var demand=Field(job.GetType(),"Demand",job);if(demand==null)continue;demandJobs++;var dt=demand.GetType();exact+=(Field(dt,"ExactFrontier",demand) as Array)?.Length??0;frontier+=(Field(dt,"Frontier",demand) as Array)?.Length??0;var scalar=Field(dt,"Scalars",demand) as byte[];if(scalar!=null)using(var sha=System.Security.Cryptography.SHA256.Create())scalarSha=BitConverter.ToString(sha.ComputeHash(scalar)).Replace("-","");}
                return "oracleObject="+System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(oracle)+" binding="+oracle.LogicalContinuationBinding+" status="+oracle.Status+" paid="+oracle.AllocationWorkConsumed+" member="+oracle.MemberCursor+" demandJobs="+demandJobs+" exactFrontier="+exact+" frontier="+frontier+" scalarSha256="+scalarSha;
            }
            string Inputs(NavigationArrivalWork current,NavigationArrivalWork oracle)
            {
                var x=current.PhysicalInput;var y=oracle.PhysicalInput;
                bool[] memberSame=Enumerable.Range(0,Math.Min(x.Members.Count,y.Members.Count)).Select(index=>(bool)typeof(NavigationArrivalWork).GetMethod("HistoricalMemberSame",F).Invoke(current,new object[]{index})).ToArray();
                bool header=(bool)typeof(NavigationArrivalWork).GetMethod("HistoricalHeader",F).Invoke(current,null);
                bool provider=((PartitionNavigationProvider)y.Provider).MonotoneHeaderTo((PartitionNavigationProvider)x.Provider);
                bool candidates=x.Source.Candidates.SequenceEqual(y.Source.Candidates);
                bool occupancyPrefix=x.Occupancy.Occupants.Count>=y.Occupancy.Occupants.Count&&y.Occupancy.Occupants.Select((row,index)=>row.StableKey==x.Occupancy.Occupants[index].StableKey).All(equal=>equal);
                return "header="+header+" memberSame="+string.Join(",",memberSame)+" providerMonotone="+provider+" sourceEqual="+candidates+" source="+x.Source.Id+":"+x.Source.Revision+":"+x.Source.Candidates.Count+":"+x.Source.StableKey+"/"+y.Source.Id+":"+y.Source.Revision+":"+y.Source.Candidates.Count+":"+y.Source.StableKey+" occupancyPrefix="+occupancyPrefix+" occupancy="+x.Occupancy.Generation+":"+x.Occupancy.Occupants.Count+":"+x.Occupancy.StableKey+"/"+y.Occupancy.Generation+":"+y.Occupancy.Occupants.Count+":"+y.Occupancy.StableKey+" members="+string.Join(";",x.Members.Select(member=>member.StableKey))+"/"+string.Join(";",y.Members.Select(member=>member.StableKey));
            }
            NavGeometry Addition(NavGeometry prior,int revision,NavPoint[] away)
            {
                for(double z=-prior.HalfExtent+2;z<=prior.HalfExtent-2;z+=2)for(double x=-prior.HalfExtent+2;x<=prior.HalfExtent-2;x+=2){var center=new NavPoint(x,z);if(away.Any(point=>Math.Sqrt(Math.Pow(point.X-x,2)+Math.Pow(point.Z-z,2))<24)||!prior.IsFree(center,0))continue;var obstacle=new NavObstacle(x-.25,z-.25,x+.25,z+.25);var next=new NavGeometry(prior.HalfExtent,prior.Obstacles.Concat(new[]{obstacle}).ToArray(),revision);if(!next.IsFree(center,0))return next;}return null;
            }
            Assert.True(session.PreparePhysicalArrivalAttempt(),"normal group Move must publish physical arrival Work");long group=session.GroupFor(a).GroupId;var original=WorkForGroup(group);
            if(original==null)Assert.Inconclusive("NotQualified: actual three-member arrival Work was not published by normal Move/PreparePhysicalArrivalAttempt.");
            for(int tick=0;tick<30000&&(AssignmentCount(original)<2||original.Status==NavArrivalStatus.Ready);tick++){Pump(1);session.ApplyResults();original=WorkForGroup(group)??original;if(original.Status==NavArrivalStatus.Ready&&AssignmentCount(original)==3)break;}
            int ready=AssignmentCount(original);TestContext.WriteLine("INITIAL_WORK status="+original.Status+" members="+original.PhysicalInput.Members.Count+" assignments="+ready+" memberCursor="+original.MemberCursor+" paid="+original.AllocationWorkConsumed+" source="+original.PhysicalInput.Source.StableKey+" provider="+original.PhysicalInput.Provider.Binding);
            if(ready!=2||original.Status==NavArrivalStatus.Ready||original.MemberCursor!=2)Assert.Inconclusive("NotQualified: the real paid three-member oracle did not reach exactly two Ready assignments with its third pending; no geometry reissue was attempted.");
            Assert.True(original.IsHistoricalCandidate);Assert.Greater(original.AllocationWorkConsumed,0);var geometry=(NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain);
            var points=original.PhysicalInput.Members.Select(member=>member.Origin.Position).Concat(new[]{new NavPoint(-36,-46)}).ToArray();var firstGeometry=Addition(geometry,geometry.Revision+1,points);if(firstGeometry==null)Assert.Inconclusive("NotQualified: no unoccupied position was available for an additive geometry change.");
            Domain.GetProperty("Geometry",F).SetValue(domain,firstGeometry);session.ChangeGeometry(firstGeometry);Assert.True(session.PreparePhysicalArrivalAttempt());var first=WorkForGroup(group);if(first==null)Assert.Inconclusive("NotQualified: first geometry reissue produced no replacement work.");
            Assert.AreSame(original,first.HistoricalSource,"first replacement must unwrap to the original paid Work");Assert.AreSame(original,typeof(NavigationArrivalWork).GetProperty("historicalOracle",F).GetValue(first),"first replacement must own the original direct oracle");
            string beforeFirstGrant=FrontierFacts(original);long paidBefore=first.AllocationWorkConsumed;
            for(int tick=0;tick<30000;tick++){var saved=HistoricalSnapshot(first);if(saved!=null&&(int)Field(saved.GetType(),"Gate",saved)>0)break;Pump(1);session.ApplyResults();first=WorkForGroup(group)??first;if(!ReferenceEquals(original,first.HistoricalSource))break;}
            if(HistoricalSnapshot(first)==null||!ReferenceEquals(original,first.HistoricalSource)){TestContext.WriteLine("FIRST_REISSUE oracleRetained="+ReferenceEquals(original,first.HistoricalSource)+" historical="+HistoricalFacts(first)+" input="+Inputs(first,original)+" oracleBefore="+beforeFirstGrant+" paidBefore="+paidBefore+" paidAfter="+first.AllocationWorkConsumed);Assert.Inconclusive("NotQualified: first reissue failed to reach a paid incomplete certificate retaining the original oracle.");}
            TestContext.WriteLine("FIRST_REISSUE historical="+HistoricalFacts(first)+" input="+Inputs(first,original)+" oracle="+FrontierFacts(original)+" paidBefore="+paidBefore+" paidAfter="+first.AllocationWorkConsumed);
            var secondGeometry=Addition(firstGeometry,firstGeometry.Revision+1,points);if(secondGeometry==null)Assert.Inconclusive("NotQualified: no free position for the second additive geometry change.");var firstSource=first.PhysicalInput;string oracleBeforeSecond=FrontierFacts(original);
            Domain.GetProperty("Geometry",F).SetValue(domain,secondGeometry);session.ChangeGeometry(secondGeometry);Assert.True(session.PreparePhysicalArrivalAttempt());var second=WorkForGroup(group);if(second==null)Assert.Inconclusive("NotQualified: second geometry reissue produced no replacement work.");
            Assert.AreSame(original,second.HistoricalSource,"second replacement must still unwrap to the same original oracle");Assert.AreSame(original,typeof(NavigationArrivalWork).GetProperty("historicalOracle",F).GetValue(second),"second replacement's direct oracle identity is unchanged");
            TestContext.WriteLine("SECOND_REISSUE historical="+HistoricalFacts(second)+" input="+Inputs(second,original)+" oracleBefore="+oracleBeforeSecond+" oracleAfter="+FrontierFacts(original)+" priorInput="+firstSource.Source.StableKey);
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
            int grants=0;for(;grants<30000;grants++){var saved=HistoricalSnapshot(second);if(saved!=null&&(int)Field(saved.GetType(),"Gate",saved)>0)break;Pump(1);session.ApplyResults();second=WorkForGroup(group)??second;if(!ReferenceEquals(original,second.HistoricalSource))break;}
            if(!ReferenceEquals(original,second.HistoricalSource)){TestContext.WriteLine("SECOND_REISSUE_ORACLE_DETACHED grants="+grants+" cert="+HistoricalFacts(second)+" physicalMembers="+string.Join(";",second.PhysicalInput.Members.Select(member=>member.StableKey))+" originalMembers="+string.Join(";",original.PhysicalInput.Members.Select(member=>member.StableKey))+" source="+second.PhysicalInput.Source.StableKey+"/"+original.PhysicalInput.Source.StableKey+" occupancy="+second.PhysicalInput.Occupancy.StableKey+"/"+original.PhysicalInput.Occupancy.StableKey+" originalFrontier="+FrontierFacts(original));}
            var partial=HistoricalSnapshot(second);if(partial==null||(int)Field(partial.GetType(),"Gate",partial)==0)Assert.Inconclusive("NotQualified: second certificate did not consume a measurable allocation grant.");
            var prefix=ProbeBytes();var terrain=(IPlayableTerrain)typeof(NavigationSession).GetField("terrain",F).GetValue(session);var budget=session.PhysicalHostBudget;Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            PumpProbe(1);session.ApplyResults();second=WorkForGroup(group)??second;var suffix=ProbeBytes();string hotFacts=HistoricalFacts(second)+" "+FrontierFacts(original);
            RestoreProbeBytes(prefix,terrain);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,ProbeBytes());kernel.Attach(session.RoutePort);typeof(CertifiedRouteKernel).GetMethod("RefreshPhysicalFrontier",F).Invoke(kernel,new object[]{session.RoutePort});PumpProbe(1);session.ApplyResults();var cold=ProbeBytes();var coldWork=WorkForGroup(group);TestContext.WriteLine("COLD_NEXT_GRANT hot="+hotFacts+" cold="+(coldWork==null?"missing":HistoricalFacts(coldWork)+" "+FrontierFacts(coldWork.HistoricalSource)));
            CollectionAssert.AreEqual(suffix,cold,"partial second certificate plus next grant must cold-continue byte-exactly");Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
            string OracleCheckpointFacts(NavigationArrivalWork oracle)
            {var cp=oracle.Save();var starts=Field(typeof(NavigationArrivalWork.Checkpoint),"PageStarts",cp) as int[];var counts=Field(typeof(NavigationArrivalWork.Checkpoint),"PageCounts",cp) as int[];var prepared=Field(typeof(NavigationArrivalWork.Checkpoint),"PreparedClearances",cp) as Array;var valid=prepared==null?"none":string.Join(";",prepared.Cast<object>().Select(row=>{var indices=(int[][])Field(row.GetType(),"CandidateIndices",row);return string.Join(",",indices.Select(values=>values.Length));}));return "status="+oracle.Status+" phase="+Field(typeof(NavigationArrivalWork.Checkpoint),"Phase",cp)+" page="+Field(typeof(NavigationArrivalWork.Checkpoint),"PageIndex",cp)+" member="+Field(typeof(NavigationArrivalWork.Checkpoint),"MemberIndex",cp)+" candidateCursor="+Field(typeof(NavigationArrivalWork.Checkpoint),"CandidateCursor",cp)+" prepPage="+Field(typeof(NavigationArrivalWork.Checkpoint),"PreparationPageIndex",cp)+" prepCursor="+Field(typeof(NavigationArrivalWork.Checkpoint),"PreparationCursor",cp)+" pageStarts="+string.Join(",",starts??Array.Empty<int>())+" pageCounts="+string.Join(",",counts??Array.Empty<int>())+" validCounts="+valid+" requests="+((Array)Field(typeof(NavigationArrivalWork.Checkpoint),"Requests",cp)).Length+" routes="+((Array)Field(typeof(NavigationArrivalWork.Checkpoint),"Routes",cp)).Length+" outcomes="+((Array)Field(typeof(NavigationArrivalWork.Checkpoint),"MemberOutcomes",cp)).Length+" assignments="+((Array)Field(typeof(NavigationArrivalWork.Checkpoint),"Assignments",cp)).Length+" paid="+oracle.AllocationWorkConsumed;}
            string DropPredicates(NavigationArrivalWork work,NavigationArrivalWork oracle,NavigationArrivalWork.Checkpoint before)
            {
                var hist=HistoricalSnapshot(work);var ht=hist.GetType();int gate=(int)Field(ht,"Gate",hist),cursor=(int)Field(ht,"Cursor",hist);var removal=Field(ht,"Removal",hist) as int[];var occupancy=Field(ht,"Page0Occupancy",hist) as int[];var bits=typeof(NavigationArrivalWork).GetProperties(F).Where(property=>property.Name.EndsWith("Mode")||property.Name=="HistoricalPartCount").OrderBy(property=>property.Name).Select(property=>property.Name+"="+property.GetValue(work));var failures=new System.Collections.Generic.List<string>();
                bool header=(bool)typeof(NavigationArrivalWork).GetMethod("HistoricalHeader",F).Invoke(work,null);if(!header)failures.Add("HistoricalHeader=false");
                for(int i=0;i<work.PhysicalInput.Members.Count;i++){bool same=(bool)typeof(NavigationArrivalWork).GetMethod("HistoricalMemberSame",F).Invoke(work,new object[]{i});if(!same){failures.Add("HistoricalMemberSame["+i+"]=false");break;}}
                var oldProvider=(PartitionNavigationProvider)oracle.PhysicalInput.Provider;var newProvider=(PartitionNavigationProvider)work.PhysicalInput.Provider;for(int i=0;i<oldProvider.MonotonePartCount;i++)if(!oldProvider.MonotonePartTo(newProvider,i)){failures.Add("MonotonePartTo["+i+"]=false old="+oldProvider.MonotonePartCount+" current="+newProvider.MonotonePartCount);break;}
                if(gate==2&&removal!=null&&removal.Length>0&&removal[0]==oracle.PhysicalInput.Source.Candidates.Count&&cursor!=work.PhysicalInput.Source.Candidates.Count)failures.Add("RemovalSubsequenceFinalCursor=false oldSource="+oracle.PhysicalInput.Source.Candidates.Count+" currentSource="+work.PhysicalInput.Source.Candidates.Count+" cursor="+cursor);
                bool page0=(bool)typeof(NavigationArrivalWork).GetMethod("RemovalOraclePageZero",F).Invoke(work,null);if(gate==4&&!page0)failures.Add("RemovalOraclePageZero=false oldPage="+Field(typeof(NavigationArrivalWork.Checkpoint),"PageIndex",oracle.Save())+" oldOutcomes="+oracle.Save().MemberOutcomes.Length);
                if(gate==4){var regionMethod=typeof(NavigationArrivalWork).GetMethod("RemovalRegion",F);var recipeMethod=typeof(NavigationArrivalWork).GetMethod("RemovalOldRecipe",F);for(int m=0;m<work.PhysicalInput.Members.Count;m++){var old=(NavTerminalRegion)regionMethod.Invoke(work,new object[]{oracle,m});var current=(NavTerminalRegion)regionMethod.Invoke(work,new object[]{work,m});if(old==null||current==null){failures.Add("RemovalRegion["+m+"] old="+(old!=null)+" current="+(current!=null));break;}bool shape=old.Candidates.Count==current.Candidates.Count&&old.Requested.Equals(current.Requested);bool recipe=(bool)recipeMethod.Invoke(work,new object[]{m,old});if(!shape||!recipe){failures.Add("RemovalRegionRecipe["+m+"] oldCount="+old.Candidates.Count+" currentCount="+current.Candidates.Count+" requestedEqual="+old.Requested.Equals(current.Requested)+" oldRecipe="+recipe+" oldKey="+old.StableKey+" currentKey="+current.StableKey);break;}int changed=-1;for(int n=0;n<old.Candidates.Count;n++)if(!old.Candidates[n].Equals(current.Candidates[n])){changed=n;break;}if(changed>=0){failures.Add("RemovalRegionCandidate["+m+","+changed+"]=false old="+old.Candidates[changed]+" current="+current.Candidates[changed]);break;}
                    var blocker=typeof(NavigationArrivalWork).GetMethod("HistoricalOccupantBlocks",F);var oldRows=oracle.PhysicalInput.Occupancy.Occupants;var currentRows=work.PhysicalInput.Occupancy.Occupants;for(int n=0;n<current.Candidates.Count;n++){bool oldBlocked=oldRows.Any(row=>(bool)blocker.Invoke(work,new object[]{work.PhysicalInput.Members[m],current.Candidates[n],row}));bool currentBlocked=currentRows.Any(row=>(bool)blocker.Invoke(work,new object[]{work.PhysicalInput.Members[m],current.Candidates[n],row}));if(oldBlocked!=currentBlocked){failures.Add("Page0Placement["+m+","+n+"]=false oldBlocked="+oldBlocked+" currentBlocked="+currentBlocked+" oldRows="+oldRows.Count+" currentRows="+currentRows.Count);break;}}
                    if(failures.Count>0)break;}}
                bool page0Mode=(bool)typeof(NavigationArrivalWork).GetProperty("page0OccupancyMode",F).GetValue(work);return "firstFalse="+(failures.Count>0?failures[0]:page0Mode&&!page0?"RemovalOraclePageZero=false(oldPage="+Field(typeof(NavigationArrivalWork.Checkpoint),"PageIndex",oracle.Save())+",oldOutcomes="+oracle.Save().MemberOutcomes.Length+")":"none-of-tested-predicates")+" gate="+gate+" cursor="+cursor+" removal="+string.Join(",",removal??Array.Empty<int>())+" occupancy="+string.Join(",",occupancy??Array.Empty<int>())+" policy="+string.Join(",",bits)+" header="+header+" page0="+page0+" old="+OracleCheckpointFacts(oracle);
            }
            second=coldWork;int completion=0;bool detachmentLogged=false;for(;completion<30000&&second.Status!=NavArrivalStatus.Ready;completion++){var pinned=session.PinCommittedHostRoot();try{var preWork=WorkForGroup(group)??second;var preOracle=typeof(NavigationArrivalWork).GetProperty("historicalOracle",F).GetValue(preWork) as NavigationArrivalWork;var beforeWork=preWork.Save();var prePaid=preWork.AllocationWorkConsumed;string certificateBefore=preOracle==null?"none":DropPredicates(preWork,preOracle,beforeWork);PumpProbe(1);session.ApplyResults();var postWork=WorkForGroup(group)??preWork;var postOracle=typeof(NavigationArrivalWork).GetProperty("historicalOracle",F).GetValue(postWork);if(preOracle!=null&&postOracle==null&&!detachmentLogged){detachmentLogged=true;TestContext.WriteLine("CERTIFICATE_ORACLE_DETACHED iteration="+(completion+1)+" prePaid="+prePaid+" postPaid="+postWork.AllocationWorkConsumed+" actualGrantDelta="+(postWork.AllocationWorkConsumed-prePaid)+" postLastAdvance="+postWork.LastAdvanceConsumed+" preCert="+certificateBefore+" postStatus="+postWork.Status+" postAssignments="+AssignmentCount(postWork)+" postCursor="+postWork.MemberCursor+" postHistorical="+HistoricalFacts(postWork)+" postFrontier="+FrontierFacts(postWork)+" source="+postWork.PhysicalInput.Source.StableKey+" members="+string.Join(";",postWork.PhysicalInput.Members.Select(member=>member.StableKey)));}second=postWork;}finally{pinned?.Release();}}
            object finalOracle=typeof(NavigationArrivalWork).GetProperty("historicalOracle",F).GetValue(second);TestContext.WriteLine("SECOND_CERTIFICATE_END iterations="+completion+" detachmentLogged="+detachmentLogged+" oracleAttached="+(finalOracle!=null)+" status="+second.Status+" assignments="+AssignmentCount(second)+" cursor="+second.MemberCursor+" paid="+second.AllocationWorkConsumed+" facts="+HistoricalFacts(second)+" frontier="+FrontierFacts(finalOracle as NavigationArrivalWork??second));
            Assert.AreEqual(NavArrivalStatus.Ready,second.Status,"the pending third assignment must reach Ready after certificate/solver continuation");Assert.AreEqual(3,AssignmentCount(second));Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
        }
        [Test] public void FoundationGeometryRefusalKeepsPendingPhaseHealthAndWorldThenPublishesTogetherCold()
        {
            int actor=Spawn(-18,14);Assert.True(session.Move(actor,new NavPoint(-10,14)));for(int i=0;i<20000&&session.PendingCount>0;i++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);
            Call("AddCredits",PlayableOwner.Player,10000d);Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,2,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));var buildings=(IDictionary)Domain.GetField("buildings",F).GetValue(domain);var building=buildings.Values.Cast<object>().Single(v=>(PlayableBuildingKind)v.GetType().GetField("Kind").GetValue(v)==PlayableBuildingKind.Factory);var type=building.GetType();Assert.AreEqual(ConstructionPhase.Pending,type.GetField("Phase").GetValue(building));
            var budget=session.PhysicalHostBudget;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);var geometry=Domain.GetProperty("Geometry",F).GetValue(domain);var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var before=frozen.Encode();long charge=budget.PhysicalBytes;int denied=0,underGate=0;
            try{
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){denied++;return false;}return true;};Call("AdvanceFoundations");}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreSame(geometry,Domain.GetProperty("Geometry",F).GetValue(domain));Assert.AreEqual(ConstructionPhase.Pending,type.GetField("Phase").GetValue(building));Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());CollectionAssert.AreEqual(before,frozen.Encode());
                Call("AdvanceFoundations");Assert.AreEqual(ConstructionPhase.Constructing,type.GetField("Phase").GetValue(building));Assert.AreEqual(0d,type.GetField("Health").GetValue(building));Assert.AreSame(session.AuthorityGeometry,Domain.GetProperty("Geometry",F).GetValue(domain));Assert.AreEqual(1,session.OwnedRebindPublications);Assert.Zero(session.LastOwnedRebindPublicationAllocatedBytes);CollectionAssert.AreEqual(before,frozen.Encode());PhysicalShape(frozen);frozen.Dispose();frozen=null;Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
                var prefix=Bytes();Pump(1);var suffix=Bytes();string facts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);var coldPrefix=Bytes();if(!prefix.SequenceEqual(coldPrefix)){var x=PlayableWorldState.Decode(prefix);var y=PlayableWorldState.Decode(coldPrefix);void Diff(string label,byte[] left,byte[] right){int first=0;while(first<Math.Min(left.Length,right.Length)&&left[first]==right[first])first++;if(first<Math.Max(left.Length,right.Length))TestContext.WriteLine("FOUNDATION_"+label+" first="+first+" len="+left.Length+"/"+right.Length+" left="+BitConverter.ToString(left.Skip(Math.Max(0,first-32)).Take(96).ToArray())+" right="+BitConverter.ToString(right.Skip(Math.Max(0,first-32)).Take(96).ToArray()));}Diff("DOMAIN",x.Domain,y.Domain);Diff("NAV",x.Navigation,y.Navigation);Diff("BINDING",x.Binding,y.Binding);}CollectionAssert.AreEqual(prefix,coldPrefix,"Foundation immediate cold world prefix");Pump(1);CollectionAssert.AreEqual(suffix,Bytes(),"Foundation next Pump cold world suffix");Assert.AreEqual(facts,HostFacts());
            }finally{budget.AllocationAdmission=null;frozen?.Dispose();}
        }

        [Test] public void RebindSmallRosterAdmitsActualLargeGeometryStagingBeforeAllocation()
        {
            var obstacles=new NavObstacle[2047];for(int i=0;i<obstacles.Length;i++)obstacles[i]=new NavObstacle(25,25,26,26);
            var geometry=new NavGeometry(activeProfile.ArenaHalfExtent,obstacles,2);Domain.GetProperty("Geometry",F).SetValue(domain,geometry);session.ChangeGeometry(geometry);
            int actor=Spawn(-18,-14),held=Spawn(-18,-10);session.Stop(held,true);Arrival(actor,new NavPoint(-10,-14));kernel.PauseAtPhysicalBoundary=()=>session.HasReadyArrival;
            try{for(int i=0;i<20000&&!session.HasReadyArrival;i++)Pump(64);}finally{kernel.PauseAtPhysicalBoundary=null;}Assert.True(session.HasReadyArrival);Assert.AreEqual(1,session.AllocationCount);
            var budget=session.PhysicalHostBudget;var before=Bytes();long charge=budget.PhysicalBytes;long bound=NavigationSession.RebindStagingScratchBound(2,1,obstacles.Length);Assert.Greater(bound,8L*(8192+4096*2+4096));int denied=0;
            var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);int underGate=0;
            try{budget.AllocationAdmission=bytes=>{if(Monitor.IsEntered(gate))underGate++;if(bytes>=bound){denied++;return false;}return true;};Assert.Throws<InvalidOperationException>(()=>session.Rebind(activeProfile.Navigation));}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.Zero(session.OwnedRebindPublications);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());
            session.Rebind(activeProfile.Navigation);Assert.AreEqual(1,session.OwnedRebindPublications);Assert.Zero(session.LastOwnedRebindPublicationAllocatedBytes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
        }

        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)] public void CanceledProbeStalePublicationRefusesAtomicallyAndColdPumpKeepsExactHead(bool fullAnswerMailbox,bool duplicateIngress)
        {
            var canceled=session.Probe(7,1,activeProfile.Navigation,new NavPoint(-4,2),new NavPoint(4,2));Assert.NotNull(canceled);session.CancelProbe(7);if(duplicateIngress)Assert.True(session.Requests.TryEnqueue(canceled));Assert.NotNull(session.Probe(8,2,activeProfile.Navigation,new NavPoint(-4,4),new NavPoint(4,4)));
            kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Transfer;try{PumpProbe(duplicateIngress?2:1);}finally{kernel.PauseAtPhysicalBoundary=null;}
            var prefix=ProbeBytes();var budget=session.PhysicalHostBudget;long charge=budget.PhysicalBytes;int denied=0;var type=typeof(NavigationSession).GetNestedType("PreparedStaleIngress",BindingFlags.NonPublic);
            try{budget.AllocationAdmission=_=>{if(new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().DeclaringType==type)){denied++;return false;}return true;};PumpProbe(1);}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.Zero(session.Answers.Count);Assert.AreEqual(duplicateIngress?3:2,session.Requests.Count);Assert.Zero(session.RejectedResults);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(prefix,ProbeBytes());
            if(fullAnswerMailbox){
                var waiting=session.Requests.CopyItems().Last().StorageOwner;Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));NavigationHostAnswer filler=null;
                try{filler=NavigationHostAnswer.CreateTerminal(waiting,NavSolveStatus.Stale,scope.OwnedReservation());scope.Attach(filler);scope.Commit(filler,_=>{});for(int i=0;i<session.Answers.InstanceCapacity;i++)Assert.True(session.Answers.TryEnqueue(filler.Answer));var saturated=ProbeBytes();long fullCharge=budget.PhysicalBytes;
                    PumpProbe(1);CollectionAssert.AreEqual(saturated,ProbeBytes());Assert.AreEqual(duplicateIngress?3:2,session.Requests.Count);Assert.AreEqual(session.Answers.InstanceCapacity,session.Answers.Count);Assert.Zero(session.RejectedResults);Assert.AreEqual(fullCharge,budget.PhysicalBytes);
                    while(session.Answers.TryDequeue(out var incoming))incoming.Request.StorageOwner.TransportOwner.Consume(incoming.Request);
                }finally{filler?.ReleaseStorage();scope.Dispose();}
                CollectionAssert.AreEqual(prefix,ProbeBytes());Assert.AreEqual(charge,budget.PhysicalBytes);
            }
            kernel.PauseAtPhysicalBoundary=()=>session.Answers.Count==1;try{PumpProbe(1);}finally{kernel.PauseAtPhysicalBoundary=null;}Assert.Zero(session.LastOwnedStaleIngressAllocatedBytes);Assert.AreEqual(duplicateIngress?2:1,session.Requests.Count);Assert.True(session.Answers.TryPeek(out var stale));Assert.AreSame(canceled,stale.Request);Assert.NotNull(stale.StorageOwner);Assert.AreEqual(NavSolveStatus.Stale,stale.Status);
            if(duplicateIngress){
                Assert.AreEqual(1,canceled.StorageOwner.TransportAnswers);var one=ProbeBytes();long oneCharge=budget.PhysicalBytes;denied=0;
                try{budget.AllocationAdmission=_=>{if(new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().DeclaringType==typeof(NavigationHostAnswer))){denied++;return false;}return true;};PumpProbe(1);}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied);CollectionAssert.AreEqual(one,ProbeBytes());Assert.AreEqual(oneCharge,budget.PhysicalBytes);Assert.AreEqual(1,canceled.StorageOwner.TransportAnswers);
                kernel.PauseAtPhysicalBoundary=()=>session.Answers.Count==2;try{PumpProbe(1);}finally{kernel.PauseAtPhysicalBoundary=null;}
                Assert.Zero(session.LastOwnedStaleIngressAllocatedBytes);Assert.AreEqual(1,session.Requests.Count);Assert.AreEqual(2,canceled.StorageOwner.TransportAnswers);var aliases=session.Answers.CopyItems();Assert.AreSame(aliases[0].Request,aliases[1].Request);Assert.AreNotSame(aliases[0].StorageOwner,aliases[1].StorageOwner);Assert.AreEqual(NavSolveStatus.Stale,aliases[1].Status);
            }
            var published=ProbeBytes();
            PumpProbe(1);session.ApplyResults();Assert.AreEqual(duplicateIngress?2:1,session.RejectedResults);Assert.Zero(session.Answers.Count);var suffix=ProbeBytes();PumpProbe(1);var next=ProbeBytes();var facts=HostFacts();
            RestoreProbeBytes(published);CollectionAssert.AreEqual(published,ProbeBytes());PumpProbe(1);session.ApplyResults();Assert.AreEqual(duplicateIngress?2:1,session.RejectedResults);CollectionAssert.AreEqual(suffix,ProbeBytes());PumpProbe(1);CollectionAssert.AreEqual(next,ProbeBytes());Assert.AreEqual(facts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [Test] public void ObsoleteOrdinaryDuplicateAnswersConsumeCountedTransportAndRestoreCold()
        {
            int entity=Spawn(-18,-14);Assert.True(session.Move(entity,new NavPoint(-10,-14)));var old=session.PendingRequestFor(entity);Assert.True(session.Requests.TryEnqueue(old));session.Stop(entity,false);Assert.True(session.Move(entity,new NavPoint(-8,-14)));
            kernel.PauseAtPhysicalBoundary=()=>session.Answers.Count==2;try{Pump(2);}finally{kernel.PauseAtPhysicalBoundary=null;}
            Assert.AreEqual(2,session.Answers.Count);Assert.AreEqual(2,old.StorageOwner.TransportAnswers);Assert.Zero(session.LastOwnedStaleIngressAllocatedBytes);var prefix=Bytes();
            byte[] Finish(){Pump(2);var current=session.PendingRequestFor(entity);Assert.NotNull(current);session.ApplyResults();Assert.AreEqual(2,session.RejectedResults);Assert.Zero(session.Answers.Count);Assert.AreSame(current,session.PendingRequestFor(entity));Assert.AreEqual(2,session.OwnedDiscardPublications);Assert.Zero(session.LastOwnedDiscardPublicationAllocatedBytes);return Bytes();}
            var suffix=Finish();Pump(2);var next=Bytes();var facts=HostFacts();Restore(prefix);CollectionAssert.AreEqual(prefix,Bytes());CollectionAssert.AreEqual(suffix,Finish());Pump(2);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(facts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [Test] public void FullAnswerBarrierDrainsDuringSavedAllocationWithoutAdvancingActorCold()
        {
            int entity=Spawn(-18,-14);var canceled=session.Probe(entity,1,activeProfile.Navigation,new NavPoint(-4,2),new NavPoint(4,2));session.CancelProbe(entity);
            Assert.True(session.Move(entity,new NavPoint(-10,-14)));Assert.True(session.Crowd.TryGet(entity,out var actor));var position=actor.Position;
            var budget=session.PhysicalHostBudget;Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));NavigationHostAnswer filler=null;
            try{filler=NavigationHostAnswer.CreateTerminal(canceled.StorageOwner,NavSolveStatus.Stale,scope.OwnedReservation());scope.Attach(filler);scope.Commit(filler,_=>{});for(int i=0;i<session.Answers.InstanceCapacity;i++)Assert.True(session.Answers.TryEnqueue(filler.Answer));}
            finally{filler?.ReleaseStorage();scope.Dispose();}
            kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Transfer;try{Pump(1);}finally{kernel.PauseAtPhysicalBoundary=null;}
            var allocation=typeof(NavigationSession).GetField("hostAllocationThread",F);Assert.AreNotEqual(0,allocation.GetValue(session));var prefix=Bytes();int capacity=session.Answers.InstanceCapacity;
            void Drain(){Assert.False(session.DeliveryBarrierReady);Assert.Zero(session.Answers.Count);Assert.AreEqual(capacity,session.RejectedResults);Assert.AreNotEqual(0,allocation.GetValue(session));Assert.True(session.Crowd.TryGet(entity,out var current));Assert.AreEqual(position,current.Position);Assert.False(current.Moving);Assert.AreEqual(1,session.PendingCount);}
            byte[] Finish(){for(int i=0;i<20000&&!session.DeliveryBarrierReady;i++)Pump(4);Assert.True(session.DeliveryBarrierReady);Assert.Zero((int)allocation.GetValue(session));Assert.AreEqual(capacity+1,session.RejectedResults);session.ApplyResults();Assert.AreEqual(1,session.AppliedResults);Assert.True(session.Crowd.TryGet(entity,out var current));Assert.True(current.Moving);Assert.AreEqual(position,current.Position);return Bytes();}
            Drain();var drained=Bytes();var suffix=Finish();Pump(4);var next=Bytes();var facts=HostFacts();
            Restore(prefix);CollectionAssert.AreEqual(prefix,Bytes());kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Transfer;try{Pump(0);}finally{kernel.PauseAtPhysicalBoundary=null;}CollectionAssert.AreEqual(prefix,Bytes());Drain();CollectionAssert.AreEqual(drained,Bytes());CollectionAssert.AreEqual(suffix,Finish());Pump(4);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(facts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [Test] public void TwoOrdinaryReadyAnswersDrainAndMoveAtomicallyDuringSavedPhysicalAllocationCold()
        {
            int first=Spawn(-18,-14),second=Spawn(-18,-10);Assert.True(session.Move(second,new NavPoint(-10,-10)));Assert.True(session.Move(first,new NavPoint(-10,-14)));
            var allocation=typeof(NavigationSession).GetField("hostAllocationThread",F);bool paused=false;
            kernel.PauseAtPhysicalBoundary=()=>{
                var stage=kernel.CapturePumpFrontier().Stage;
                if(session.Answers.Count==2&&(int)allocation.GetValue(session)!=0&&(stage==CertifiedRouteKernel.PumpStage.Projection||stage==CertifiedRouteKernel.PumpStage.EndAllocation)){paused=true;return true;}
                return false;
            };
            try{for(int tick=0;tick<20000&&!paused;tick++)Pump(1);}finally{kernel.PauseAtPhysicalBoundary=null;}
            Assert.True(paused,"Pause only after two genuine answers exist while the physical host allocation token is held");Assert.AreEqual(2,session.Answers.Count);Assert.AreNotEqual(0,allocation.GetValue(session));
            Assert.True(session.Crowd.TryGet(first,out var a));Assert.True(session.Crowd.TryGet(second,out var b));var a0=a.Position;var b0=b.Position;var prefix=Bytes();var budget=session.PhysicalHostBudget;long physical=budget.PhysicalBytes;int applied=session.AppliedResults,batches=session.OwnedResultBatchPublications,movements=session.OwnedMovementPublications,denied=0;
            try{budget.AllocationAdmission=_=>{if(new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().DeclaringType==typeof(NavPreparedCrowdStep)&&frame.GetMethod().Name=="Create")){denied++;return false;}return true;};session.Step(1d/30);}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied,"The refusal must reach real composed movement preparation");Assert.AreEqual(2,session.Answers.Count);Assert.AreEqual(applied,session.AppliedResults);Assert.AreEqual(batches,session.OwnedResultBatchPublications);Assert.AreEqual(movements,session.OwnedMovementPublications);Assert.AreEqual(a0,a.Position);Assert.AreEqual(b0,b.Position);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(prefix,Bytes());Assert.Zero(ArrivalLeaseCount);
            session.Step(1d/30);Assert.AreEqual(applied+2,session.AppliedResults);Assert.AreEqual(batches+1,session.OwnedResultBatchPublications);Assert.AreEqual(movements+1,session.OwnedMovementPublications);Assert.Zero(session.Answers.Count);Assert.Zero(session.PendingCount);Assert.Zero(ArrivalLeaseCount);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.NotNull(session.InstalledExecutionFor(first));Assert.NotNull(session.InstalledExecutionFor(second));Assert.Greater(a.Position.X,a0.X);Assert.Greater(b.Position.X,b0.X);
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);var published=Bytes();Pump(4);var next=Bytes();var facts=HostFacts();
            Restore(prefix);CollectionAssert.AreEqual(prefix,Bytes());kernel.Attach(session.RoutePort);typeof(CertifiedRouteKernel).GetMethod("RefreshPhysicalFrontier",F).Invoke(kernel,new object[]{session.RoutePort});session.Step(1d/30);CollectionAssert.AreEqual(published,Bytes());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);Pump(4);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(facts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [Test] public void TwoStaleReadyDiscardsStillMoveUnrelatedInstalledActorDuringSavedAllocationCold()
        {
            int moving=Spawn(-18,-6);Assert.True(session.Move(moving,new NavPoint(-10,-6)));for(int i=0;i<20000&&session.PendingCount>0;i++){Pump(4);session.ApplyResults();}var initialAllocation=typeof(NavigationSession).GetField("hostAllocationThread",F);for(int i=0;i<20000&&(int)initialAllocation.GetValue(session)!=0;i++)Pump(4);Assert.Zero((int)initialAllocation.GetValue(session));session.Step(1d/30);Assert.NotNull(session.InstalledExecutionFor(moving));Assert.True(session.Crowd.TryGet(moving,out var mover));Assert.True(mover.Moving);session.Step(1d/30);var movingStart=mover.Position;
            int first=Spawn(-18,-14),second=Spawn(-18,-10);Assert.True(session.Move(second,new NavPoint(-10,-10)));Assert.True(session.Move(first,new NavPoint(-10,-14)));var allocation=typeof(NavigationSession).GetField("hostAllocationThread",F);bool paused=false;
            kernel.PauseAtPhysicalBoundary=()=>{var stage=kernel.CapturePumpFrontier().Stage;if(session.Answers.Count==2&&(int)allocation.GetValue(session)!=0&&(stage==CertifiedRouteKernel.PumpStage.Projection||stage==CertifiedRouteKernel.PumpStage.EndAllocation)){paused=true;return true;}return false;};
            try{for(int i=0;i<20000&&!paused;i++)Pump(1);}finally{kernel.PauseAtPhysicalBoundary=null;}Assert.True(paused);Assert.AreEqual(2,session.Answers.Count);Assert.AreNotEqual(0,allocation.GetValue(session));
            session.Stop(first,false);session.Stop(second,false);Assert.AreEqual(2,session.Answers.Count,"Both late Ready answers remain transport-owned after Stop");var prefix=Bytes();var budget=session.PhysicalHostBudget;long physical=budget.PhysicalBytes;int rejected=session.RejectedResults,batches=session.OwnedResultBatchPublications,movements=session.OwnedMovementPublications,denied=0;var installedSet=(NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);var installed=installedSet.OwnerFor(moving);
            try{budget.AllocationAdmission=_=>{if(new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().DeclaringType==typeof(NavPreparedCrowdStep)&&frame.GetMethod().Name=="Create")){denied++;return false;}return true;};session.Step(1d/30);}finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.AreEqual(2,session.Answers.Count);Assert.AreEqual(rejected,session.RejectedResults);Assert.AreEqual(batches,session.OwnedResultBatchPublications);Assert.AreEqual(movements,session.OwnedMovementPublications);Assert.AreSame(installed,installedSet.OwnerFor(moving));Assert.AreEqual(movingStart,mover.Position);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(prefix,Bytes());Assert.Zero(ArrivalLeaseCount);
            session.Step(1d/30);Assert.AreEqual(rejected+2,session.RejectedResults);Assert.AreEqual(batches+1,session.OwnedResultBatchPublications);Assert.AreEqual(movements+1,session.OwnedMovementPublications);Assert.AreEqual(2,session.OwnedDiscardPublications);Assert.Zero(session.Answers.Count);Assert.Zero(ArrivalLeaseCount);Assert.Greater(mover.Position.X,movingStart.X);Assert.AreSame(installed,installedSet.OwnerFor(moving));Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            var drained=Bytes();Pump(4);var next=Bytes();var facts=HostFacts();Restore(prefix);CollectionAssert.AreEqual(prefix,Bytes());kernel.Attach(session.RoutePort);typeof(CertifiedRouteKernel).GetMethod("RefreshPhysicalFrontier",F).Invoke(kernel,new object[]{session.RoutePort});session.Step(1d/30);CollectionAssert.AreEqual(drained,Bytes());Pump(4);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(facts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [TestCase(false)][TestCase(true)] public void SavedAllocationCommutationAcceptsSameSchedulerHostForkAndRefusesMailboxChange(bool mutateMailbox)
        {
            int first=Spawn(-18,-14),second=Spawn(-18,-10),extra=mutateMailbox?Spawn(-18,-6):0;Assert.True(session.Move(second,new NavPoint(-10,-10)));Assert.True(session.Move(first,new NavPoint(-10,-14)));var allocation=typeof(NavigationSession).GetField("hostAllocationThread",F);bool paused=false;
            kernel.PauseAtPhysicalBoundary=()=>{var stage=kernel.CapturePumpFrontier().Stage;if(session.Answers.Count==2&&(int)allocation.GetValue(session)!=0&&(stage==CertifiedRouteKernel.PumpStage.Projection||stage==CertifiedRouteKernel.PumpStage.EndAllocation)){paused=true;return true;}return false;};
            try{for(int i=0;i<20000&&!paused;i++)Pump(1);}finally{kernel.PauseAtPhysicalBoundary=null;}Assert.True(paused);var prefix=Bytes();var budget=session.PhysicalHostBudget;int applied=session.AppliedResults,batches=session.OwnedResultBatchPublications,movements=session.OwnedMovementPublications;bool changed=false;byte[] mutationPrefix=null;NavigationHostRoot originalRoot=null;
            if(!mutateMailbox)originalRoot=session.PinCommittedHostRoot();
            try{budget.AllocationAdmission=_=>{
                if(changed||!new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().DeclaringType==typeof(NavPreparedCrowdStep)&&frame.GetMethod().Name=="Create"))return true;
                changed=true;
                if(mutateMailbox){Assert.True(session.Move(extra,new NavPoint(-10,-6)));mutationPrefix=Bytes();}
                else{var source=session.PinCommittedHostRoot();NavigationHostRoot candidate=null;NavigationRetentionBudget.Scope scope=null;try{Assert.True(session.TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,session.Generation,session.Admission.PublicationIdentity,out scope));candidate=source.ForkOwned(scope.OwnedReservation());candidate.SealPrepared();scope.Attach(candidate);session.PublishPhysicalHostRoot(scope,source,candidate,()=>{});}finally{scope?.Dispose();candidate?.ReleaseStorage();source?.Release();}}
                return true;
            };session.Step(1d/30);}finally{budget.AllocationAdmission=null;originalRoot?.Release();}
            Assert.True(changed);
            if(mutateMailbox){Assert.AreNotEqual(0,extra);Assert.AreEqual(2,session.Answers.Count);Assert.AreEqual(applied,session.AppliedResults);Assert.AreEqual(batches,session.OwnedResultBatchPublications);Assert.AreEqual(movements,session.OwnedMovementPublications);CollectionAssert.AreEqual(mutationPrefix,Bytes());}
            else{Assert.AreEqual(applied+2,session.AppliedResults);Assert.AreEqual(batches+1,session.OwnedResultBatchPublications);Assert.AreEqual(movements+1,session.OwnedMovementPublications);Assert.Zero(session.Answers.Count);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);Assert.Zero(session.LastOwnedMovementPublicationAllocatedBytes);var current=session.PinCommittedHostRoot();try{Assert.AreNotSame(originalRoot,current);}finally{current?.Release();}}
            Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
        }

        [Test] public void FirstGroupPublicationInvalidatesPreparedProbeBootstrapWithoutLosingPendingOrder()
        {
            int actor=Spawn(-18,-14);bool changed=false;byte[] accepted=null;var budget=session.PhysicalHostBudget;
            try{budget.AllocationAdmission=_=>{if(!changed&&new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().DeclaringType==typeof(NavigationGroupCommand))){changed=true;Arrival(actor,new NavPoint(-10,-14));accepted=Bytes();}return true;};Assert.Null(session.Probe(7,1,activeProfile.Navigation,new NavPoint(-4,2),new NavPoint(4,2)));}finally{budget.AllocationAdmission=null;}
            Assert.True(changed);Assert.NotNull(accepted);CollectionAssert.AreEqual(accepted,Bytes());Assert.Zero(session.OwnedProbePublications);Assert.Zero(session.Requests.Count);Assert.AreEqual(1,session.AllocationCount);Assert.NotNull(session.PendingRequestFor(actor));Assert.NotNull(session.GroupFor(actor));var allocating=session.PendingRequestFor(actor);
            Assert.NotNull(session.Probe(7,1,activeProfile.Navigation,new NavPoint(-4,2),new NavPoint(4,2)));Assert.AreEqual(1,session.Requests.Count);Assert.AreEqual(-7,session.Requests.CopyItems()[0].Entity);Assert.AreSame(allocating,session.PendingRequestFor(actor));Assert.AreEqual(1,session.AllocationCount);Assert.Zero(session.LastOwnedProbePublicationAllocatedBytes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
        }

        [Test] public void MemberAdmissionRefusalIsAtomicAndOldWorkerPinSurvivesPublication()
        {
            int entity=Spawn(-18,-14);Assert.True(session.Move(entity,new NavPoint(-10,-14)));
            var before=Bytes();var original=session.Admission;var pending=session.PendingRequestFor(entity);
            using(var reader=session.PinAdmission()){
                Assert.NotNull(reader.StorageOwner);long physical=session.PhysicalHostBudget.PhysicalBytes;int denied=0;
                long layout=NavStorageLayout.Charge((long)typeof(NavigationAdmissionStorage).GetField("Layout",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null));
                try{
                    session.PhysicalHostBudget.AllocationAdmission=bytes=>{if(bytes==layout){denied++;return false;}return true;};session.SetFormationContact(entity,true,19);
                    Assert.AreEqual(1,denied);Assert.AreSame(original,session.Admission);Assert.AreSame(pending,session.PendingRequestFor(entity));Assert.False(session.GroupFor(entity).Members[0].FormationReleased);Assert.AreEqual(physical,session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
                }finally{session.PhysicalHostBudget.AllocationAdmission=null;}
                CollectionAssert.AreEqual(before,Bytes());session.SetFormationContact(entity,true,19);
                Assert.AreNotSame(original,session.Admission);Assert.True(session.GroupFor(entity).Members[0].FormationReleased);Assert.False(reader.Value.Groups.Single(g=>g.Members.Any(m=>m.Entity==entity)).Members.Single(m=>m.Entity==entity).FormationReleased);Assert.True(reader.Value.Contains(pending));
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,reader.StorageOwner),session.PhysicalHostBudget.PhysicalBytes);
            }
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }
        [TestCase(false)][TestCase(true)] public void ReadyRouteAndInstalledAdmissionRefusalKeepActualMailboxAndActor(bool installed)
        {
            int entity=Spawn(-18,-14);Assert.True(session.Move(entity,new NavPoint(-10,-14)));
            for(int tick=0;tick<20000&&session.Answers.Count==0;tick++)Pump(64);
            Assert.AreEqual(1,session.Answers.Count);var answer=session.Answers.CopyItems()[0];Assert.NotNull(answer.StorageOwner);Assert.True(session.Crowd.TryGet(entity,out var actor));Assert.Null(actor.StorageOwner.Global);
            long deniedBytes=installed?NavStorageLayout.Charge((long)typeof(NavigationInstalledExecutionOwner).GetField("Layout",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)):NavStorageLayout.Charge(96L+16L*(answer.RouteLength+1));
            var before=Bytes();long physical=session.PhysicalHostBudget.PhysicalBytes;var pending=session.PendingRequestFor(entity);int denied=0;
            try{
                session.PhysicalHostBudget.AllocationAdmission=bytes=>{if(bytes==deniedBytes){denied++;return false;}return true;};session.ApplyResults();
                Assert.AreEqual(1,denied);Assert.AreEqual(1,session.Answers.Count);Assert.AreSame(answer,session.Answers.CopyItems()[0]);Assert.AreSame(pending,session.PendingRequestFor(entity));Assert.Null(actor.StorageOwner.Global);Assert.False(actor.Moving);Assert.AreEqual(physical,session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
            }finally{session.PhysicalHostBudget.AllocationAdmission=null;}
            CollectionAssert.AreEqual(before,Bytes());session.ApplyResults();Assert.Zero(session.Answers.Count);Assert.True(actor.Moving);Assert.NotNull(actor.StorageOwner.Global);
            var executions=(System.Collections.Generic.IDictionary<int,NavigationInstalledExecutionState>)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);Assert.NotNull(executions[entity].StorageOwner);Assert.AreSame(actor.StorageOwner.Global,executions[entity].StorageOwner.Route);
        }
        [TestCase(false)][TestCase(true)] public void SessionLargeGeometryReadyDowngradesCapacityBeforeDestinationsAndRestoresCold(bool batch)
        {
            var points=Enumerable.Range(0,128).Select(i=>new NavPoint(25.5+.25*Math.Cos(2*Math.PI*i/128),25.5+.25*Math.Sin(2*Math.PI*i/128))).ToArray();var polygon=new NavPolygon(points);var obstacles=Enumerable.Range(0,2047).Select(_=>new NavObstacle(polygon)).ToArray();var geometry=new NavGeometry(16,obstacles,2);TestContext.WriteLine("old default actors="+string.Join(";",session.Crowd.Units.Select(u=>u.Id+":"+u.Position)));var previousBudget=session.PhysicalHostBudget;kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");session=new NavigationSession(1,geometry,activeProfile.Navigation);Domain.GetField("navigation",F).SetValue(domain,session);Domain.GetProperty("Geometry",F).SetValue(domain,geometry);kernel=new CertifiedRouteKernel();Assert.Zero(previousBudget.PhysicalBytes);Assert.Zero(session.Crowd.Units.Count);
            int held=Spawn(-10,10);session.Stop(held,true);int first=Spawn(-6,-6);int[] ids=new[]{first};Assert.True(session.Move(first,new NavPoint(2,-6)));if(batch){Assert.NotNull(session.Probe(7,1,activeProfile.Navigation,new NavPoint(-4,2),new NavPoint(4,2)));session.CancelProbe(7);}
            Assert.AreEqual(2,session.Crowd.Units.Count);foreach(var u in session.Crowd.Units)Assert.True(session.Crowd.ValidLocation(u.Location,u.Radius));int expected=batch?2:1;for(int tick=0;tick<20000&&session.Answers.Count<expected;tick++)Pump(64);Assert.AreEqual(expected,session.Answers.Count);var answers=session.Answers.CopyItems();if(batch)Assert.AreEqual(NavSolveStatus.Stale,answers.Single(a=>a.Request.Entity<0).Status);foreach(var answer in answers.Where(a=>a.Request.Entity>=0)){Assert.AreEqual(NavSolveStatus.Ready,answer.Status,"Witness must be a genuine certified Ready, not solver capacity refusal.");Assert.NotNull(answer.Corridor);Assert.NotNull(answer.StorageOwner);Assert.AreSame(answer.Request,session.PendingRequestFor(answer.Request.Entity));}
            byte[] PaidBytes(){using(var state=session.CaptureOwnedState()){CertifiedPhysicalPumpTests.AssertCaptureDtoLayout(state);using(var bytes=WorldWire.PackOwned(w=>WorldWire.Write(w,state),session.PhysicalHostBudget))return bytes.TransferPublic();}}
            var budget=session.PhysicalHostBudget;TestContext.WriteLine("capacity Ready source physical="+budget.PhysicalBytes+" peak="+budget.PeakRetainedBytes);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var measured));using(measured)using(NavStorageScratch.Reserve(measured.OwnedReservation(),NavigationSession.RebindStagingScratchBound(session.Crowd.Units.Count,ids.Length,obstacles.Length+1))){var answer=answers.Single(a=>a.Request.Entity>=0);long host=(long)typeof(NavigationSession).GetField("hostRetainedBytes",F).GetValue(session),tracked=(long)typeof(NavigationSession).GetField("answerCorridorBytes",F).GetValue(session),installed=(long)typeof(NavigationSession).GetField("installedCorridorBytes",F).GetValue(session),geometryBytes=(long)typeof(NavigationSession).GetMethod("ProspectiveGeometryBytes",F).Invoke(session,new object[]{answer.Request.Entity,answer.Request});long required=host+tracked-answer.Corridor.EstimatedRetainedBytes+installed+geometryBytes+answer.Corridor.EstimatedRetainedBytes+16L*(answer.RouteLength+1);TestContext.WriteLine("actual legacy install bytes="+required+" host="+host+" geometry="+geometryBytes);Assert.Greater(required,LayeredNavigationScheduler.MaxRetainedBytes,"Positive capacity witness prerequisite from real retained payloads.");}
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);long physical=budget.PhysicalBytes;int destinations=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);int underGate=0;var events=new System.Collections.Generic.List<int>();Action<NavigationRequest,NavigationOutcome> Failed=(r,o)=>{Assert.False(Monitor.IsEntered(gate));Assert.AreEqual(NavigationOutcome.Rejected,o);events.Add(r.Entity);};session.RouteFailed+=Failed;
            try{
                int denied=0;var pendingSource=typeof(NavigationSession).GetField("pending",F).GetValue(session);var failureSource=typeof(NavigationSession).GetField("technicalFailures",F).GetValue(session);Assert.True(session.Crowd.TryGet(first,out var sourceActor));long mobility=sourceActor.MobilityRevision;
                try{budget.AllocationAdmission=_=>{if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationCommandSlots<int,NavSolveStatus>))){denied++;return false;}return true;};if(batch)session.Step(1d/30);else session.ApplyResults();}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied);Assert.IsEmpty(events);Assert.Zero(session.OwnedOrdinaryFailurePublications);Assert.Zero(session.OwnedResultBatchPublications);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.AreSame(pendingSource,typeof(NavigationSession).GetField("pending",F).GetValue(session));Assert.AreSame(failureSource,typeof(NavigationSession).GetField("technicalFailures",F).GetValue(session));CollectionAssert.AreEqual(answers,session.Answers.CopyItems());Assert.AreSame(answers.Single(a=>a.Request.Entity>=0).Request,session.PendingRequestFor(first));Assert.AreEqual(mobility,sourceActor.MobilityRevision);Assert.AreEqual(NavigationOutcome.Idle,sourceActor.LastFailure);Assert.Null(sourceActor.FailedGoal);Assert.Null(sourceActor.StorageOwner.Global);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
                budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavPreparedUnitRoute)||f.GetMethod().DeclaringType==typeof(NavigationInstalledExecutionOwner)))destinations++;return true;};try{if(batch)session.Step(1d/30);else session.ApplyResults();}finally{budget.AllocationAdmission=null;}
                Assert.Zero(destinations,"Capacity rejection must not allocate unused installation destinations.");if(!batch)Assert.Zero(underGate);Assert.AreEqual(ids.Length,session.OwnedOrdinaryFailurePublications);Assert.Zero(session.LastOwnedOrdinaryFailurePublicationAllocatedBytes);if(batch){Assert.AreEqual(1,session.OwnedResultBatchPublications);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);}Assert.Zero(session.AppliedResults);Assert.AreEqual(batch?1:0,session.RejectedResults);Assert.Zero(session.UnreachableResults);Assert.Zero(session.Answers.Count);CollectionAssert.AreEqual(answers.Where(a=>a.Request.Entity>=0).Select(a=>a.Request.Entity).ToArray(),events);
                var failures=(NavigationCommandSlots<int,NavSolveStatus>)typeof(NavigationSession).GetField("technicalFailures",F).GetValue(session);foreach(int id in ids){Assert.Null(session.PendingRequestFor(id));Assert.True(session.Crowd.TryGet(id,out var actor));Assert.False(actor.Moving);Assert.Null(actor.StorageOwner.Global);Assert.AreEqual(NavigationOutcome.Rejected,actor.LastFailure);Assert.AreEqual(NavSolveStatus.CapacityExceeded,failures[id]);}
                Assert.LessOrEqual(budget.PeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);TestContext.WriteLine("capacity actual physical="+physical+"->"+budget.PhysicalBytes+" peak="+budget.PeakRetainedBytes+" blockers="+obstacles.Length+" vertices="+points.Length);session.RouteFailed-=Failed;
                PumpProbe(1); // Capture the first real delivered-owner retirement boundary.
                var prefix=PaidBytes();PumpProbe(1);var suffix=PaidBytes();var facts=HostFacts();PumpProbe(1);var next=PaidBytes();var nextFacts=HostFacts();RestoreProbeBytes(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,PaidBytes());PumpProbe(1);CollectionAssert.AreEqual(suffix,PaidBytes());Assert.AreEqual(facts,HostFacts());PumpProbe(1);CollectionAssert.AreEqual(next,PaidBytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;if(session!=null)session.RouteFailed-=Failed;}
        }
        private sealed class RepeatedPressurePoints:System.Collections.Generic.IReadOnlyList<NavPoint>
        {
            internal RepeatedPressurePoints(int count){Count=count;}public int Count{get;}public NavPoint this[int index]=>default;
            public System.Collections.Generic.IEnumerator<NavPoint> GetEnumerator(){for(int i=0;i<Count;i++)yield return default;}IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        [TestCase(false)][TestCase(true)] public void ActualReadyPublicationRespectsNearCapPhysicalSiblingPressure(bool refuse)
        {
            int entity=Spawn(-18,-14);Assert.True(session.Move(entity,new NavPoint(-10,-14)));for(int tick=0;tick<20000&&session.Answers.Count==0;tick++)Pump(64);Assert.AreEqual(1,session.Answers.Count);var answer=session.Answers.CopyItems()[0];Assert.AreEqual(NavSolveStatus.Ready,answer.Status);Assert.True(session.Crowd.TryGet(entity,out var actor));var budget=session.PhysicalHostBudget;var before=Bytes();long original=budget.PhysicalBytes;NavRetainedArray<NavPoint> pressure=null;
            try{
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,session.Generation,1,out var scope));
                using(scope){long headroom=refuse?256:1024*1024;int count=checked((int)((LayeredNavigationScheduler.MaxRetainedBytes-budget.RetainedBytes-headroom-NavStorageLayout.Charge(96))/16));Assert.Greater(count,1000000);pressure=NavRetainedArray<NavPoint>.Copy(new RepeatedPressurePoints(count),scope.TryReserve);scope.Attach(pressure);scope.Commit(pressure,_=>{});}
                long physical=budget.PhysicalBytes;Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,pressure),physical);Assert.Greater(budget.RetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes-2*1024*1024);session.ApplyResults();Assert.Zero(budget.ActiveScopes);Assert.Zero(budget.HeldBytes);Assert.LessOrEqual(budget.PeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                if(refuse){Assert.Zero(session.OwnedReadyPublications);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreSame(answer,session.Answers.CopyItems()[0]);Assert.AreSame(answer.Request,session.PendingRequestFor(entity));Assert.False(actor.Moving);Assert.Null(actor.StorageOwner.Global);}
                else{Assert.AreEqual(1,session.OwnedReadyPublications);Assert.Zero(session.LastOwnedReadyPublicationAllocatedBytes);Assert.Zero(session.Answers.Count);Assert.Null(session.PendingRequestFor(entity));Assert.True(actor.Moving);Assert.NotNull(actor.StorageOwner.Global);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,pressure),budget.PhysicalBytes);}
                TestContext.WriteLine("nearcap physical="+budget.PhysicalBytes+" peak="+budget.PeakRetainedBytes+" limit="+LayeredNavigationScheduler.MaxRetainedBytes);pressure.Release();pressure=null;
                if(refuse){Assert.AreEqual(original,budget.PhysicalBytes);CollectionAssert.AreEqual(before,Bytes());session.ApplyResults();Assert.AreEqual(1,session.OwnedReadyPublications);Assert.Zero(session.LastOwnedReadyPublicationAllocatedBytes);Assert.True(actor.Moving);}
                var prefix=Bytes();Pump(1);var suffix=Bytes();var facts=HostFacts();Pump(1);var next=Bytes();var nextFacts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Pump(1);CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{pressure?.Release();}
        }
        [TestCase(false)][TestCase(true)] public void PreparedReadyQualificationKeepsFullVerdictAndRefusalOffGate(bool partition)
        {
            if(partition){kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");activeProfile=PlayableProfile.ThreeCrossingsDefault;domain=Activator.CreateInstance(Domain,F,null,new object[]{activeProfile,1L,false},null);session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);kernel=new CertifiedRouteKernel();}
            int entity=partition?Spawn(-18,0):Spawn(-18,-14);Assert.True(session.Move(entity,partition?new NavPoint(18,0):new NavPoint(-10,-14)));
            for(int tick=0;tick<20000&&session.Answers.Count==0;tick++)Pump(64);
            Assert.AreEqual(1,session.Answers.Count);var answer=session.Answers.CopyItems()[0];var request=answer.Request;var corridor=answer.Corridor;var provider=answer.StorageOwner.CorridorOwner.ProviderOwner.Provider;
            if(partition)Assert.True(corridor.Legs.Any(leg=>leg.Kind==NavTypedLegKind.Portal));
            var qualifier=typeof(NavigationSession).GetMethod("QualifyCorridorPrepared",BindingFlags.Static|BindingFlags.NonPublic);
            var group=typeof(NavigationSession).GetMethod("ObserveCorridorGroup",F).Invoke(session,new object[]{request});
            bool Qualify(NavCorridorDescriptor value,NavPoint[] route,out string reason){object[] args={request,route,value,provider,true,session.Generation,answer.StorageOwner.CorridorOwner.ProviderOwner.SurfaceProviderId,group,null};bool valid=(bool)qualifier.Invoke(null,args);reason=(string)args[8];return valid;}
            NavCorridorDescriptor WithLegs(NavCorridorLeg[] legs)=>new NavCorridorDescriptor(corridor.Version,corridor.GraphKey,corridor.ProviderBinding,corridor.ProfileBinding,corridor.SurfaceProviderId,corridor.HoldBinding,corridor.TopologyRevision,corridor.Radius,corridor.GridCell,corridor.Provenance,corridor.World,corridor.Group,corridor.RootRevision,corridor.CommandSequence,corridor.IssuedTick,corridor.ActivationRevision,corridor.Entity,corridor.Incarnation,corridor.Mobility,corridor.Request,corridor.Order,corridor.Origin,corridor.Endpoint,legs);
            Assert.True(Qualify(corridor,answer.ReadOnlyRouteBacking,out var good),good);
            var legs=corridor.Legs.ToArray();int surface=Array.FindIndex(legs,leg=>leg.Kind==NavTypedLegKind.Surface);var old=legs[surface];legs[surface]=new NavCorridorLeg(old.From,old.To,old.Kind,old.PortalId,old.Cost+1,old.Section,old.Branch,old.DownstreamMergeLeg);
            Assert.False(Qualify(WithLegs(legs),answer.ReadOnlyRouteBacking,out var badCost));Assert.AreEqual("surface clearance/cost",badCost);
            var wrongRoute=(NavPoint[])answer.ReadOnlyRouteBacking.Clone();wrongRoute[0]=new NavPoint(wrongRoute[0].X+.01,wrongRoute[0].Z);Assert.False(Qualify(corridor,wrongRoute,out var badProjection));Assert.AreEqual("XZ projection",badProjection);
            if(partition){legs=corridor.Legs.ToArray();int portal=Array.FindIndex(legs,leg=>leg.Kind==NavTypedLegKind.Portal);old=legs[portal];legs[portal]=new NavCorridorLeg(old.From,old.To,old.Kind,new string('f',64),old.Cost,old.Section,old.Branch,old.DownstreamMergeLeg);Assert.False(Qualify(WithLegs(legs),answer.ReadOnlyRouteBacking,out var badPortal));Assert.AreEqual("portal seam",badPortal);}
            var diagnostic=typeof(NavigationSession).GetProperty("LastCorridorFailure",F);diagnostic.SetValue(session,"unmodified diagnostic");
            long scratch=(long)typeof(NavigationSession).GetMethod("CorridorQualificationScratch",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{request,corridor,provider,answer.RouteLength});long deniedBytes=NavStorageScratch.LeaseBytes+scratch;
            var before=Bytes();long physical=session.PhysicalHostBudget.PhysicalBytes;int denied=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);var prepare=typeof(NavigationSession).GetMethod("TryPrepareAnswerNames",F);
            try{session.PhysicalHostBudget.AllocationAdmission=bytes=>{if(bytes!=deniedBytes)return true;Assert.False(Monitor.IsEntered(gate),"qualification reservation must precede publication");denied++;return false;};session.ApplyResults();Assert.AreEqual(1,denied);Assert.AreEqual("unmodified diagnostic",session.LastCorridorFailure);Assert.AreEqual(1,session.Answers.Count);Assert.AreSame(request,session.PendingRequestFor(entity));Assert.AreEqual(physical,session.PhysicalHostBudget.PhysicalBytes);}
            finally{session.PhysicalHostBudget.AllocationAdmission=null;}
            CollectionAssert.AreEqual(before,Bytes());object[] preparedArgs={null};Assert.True((bool)prepare.Invoke(session,preparedArgs));using(var prepared=(NavigationPreparedAnswerNames)preparedArgs[0]){Assert.True(prepared.HasQualification[0]);Assert.True(prepared.Qualified[0],prepared.QualificationFailures[0]);Assert.AreEqual("unmodified diagnostic",session.LastCorridorFailure);}
            bool changed=false;try{session.PhysicalHostBudget.AllocationAdmission=bytes=>{if(bytes==deniedBytes&&!changed){changed=true;session.SetFormationContact(entity,true,31);}return true;};preparedArgs[0]=null;Assert.False((bool)prepare.Invoke(session,preparedArgs));Assert.Null(preparedArgs[0]);Assert.True(changed);Assert.AreSame(request,session.PendingRequestFor(entity));Assert.AreSame(answer,session.Answers.CopyItems()[0]);Assert.AreEqual("unmodified diagnostic",session.LastCorridorFailure);Assert.True(session.GroupFor(entity).Members.Single(m=>m.Entity==entity).FormationReleased);}
            finally{session.PhysicalHostBudget.AllocationAdmission=null;}
            Assert.True(session.Crowd.TryGet(entity,out var actor));Assert.Null(actor.StorageOwner.Global);Assert.False(actor.Moving);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            session.ApplyResults();Assert.True(actor.Moving);Assert.Zero(session.Answers.Count);Assert.Null(session.LastCorridorFailure);
        }
        [TestCase(false)][TestCase(true)] public void ActualArrivalReadyCoPublishesLedgerAdmissionAndActorWithoutAllocation(bool denyAdmission)
        {
            int entity=Spawn(-18,-14);Arrival(entity,new NavPoint(-10,-14));
            for(int tick=0;tick<20000&&session.Answers.Count==0;tick++){Pump(64);if(session.Answers.Count==0)session.ApplyResults();}
            Assert.AreEqual(1,session.Answers.Count);var answer=session.Answers.CopyItems()[0];var request=answer.Request;Assert.True(session.Crowd.TryGet(entity,out var actor));Assert.Null(actor.StorageOwner.Global);
            var ledger=(NavigationCommandSlots<long,NavigationArrivalReservation>)typeof(NavigationSession).GetField("arrivalLedger",F).GetValue(session);var row=(NavigationLiveArrivalReservation)ledger.OwnerFor(request.Request);Assert.NotNull(row);Assert.AreEqual(NavArrivalReservationKind.PendingRequest,row.Entry.State);
            var before=Bytes();var admission=session.Admission;long physical=session.PhysicalHostBudget.PhysicalBytes;int denied=0,underGate=0;bool measuringReady=true;
            long deny=NavStorageLayout.Charge((long)(denyAdmission?typeof(NavigationAdmissionStorage):typeof(NavigationLiveArrivalReservation)).GetField("Layout",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null));var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            try{session.PhysicalHostBudget.AllocationAdmission=bytes=>{if(measuringReady&&Monitor.IsEntered(gate))underGate++;if(bytes==deny){denied++;return false;}return true;};session.ApplyResults();Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.Zero(session.OwnedReadyPublications);Assert.AreSame(admission,session.Admission);Assert.AreSame(request,session.PendingRequestFor(entity));Assert.AreSame(answer,session.Answers.CopyItems()[0]);Assert.AreSame(ledger,typeof(NavigationSession).GetField("arrivalLedger",F).GetValue(session));Assert.AreEqual(NavArrivalReservationKind.PendingRequest,row.Entry.State);Assert.Null(actor.StorageOwner.Global);Assert.AreEqual(physical,session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);}
            finally{session.PhysicalHostBudget.AllocationAdmission=null;}
            CollectionAssert.AreEqual(before,Bytes());byte[] during=null;bool observed=false;
            session.ObserveOwnedReadyBeforeRetirement=carrier=>{
                observed=true;Assert.False(Monitor.IsEntered(gate));Assert.Zero(session.LastOwnedReadyPublicationAllocatedBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);Assert.True(actor.Moving);Assert.Null(session.PendingRequestFor(entity));Assert.Zero(session.Answers.Count);Assert.False(session.Admission.Contains(request));Assert.AreEqual(GroupMemberPhase.March,session.GroupFor(entity).Members.Single(m=>m.Entity==entity).Phase);
                var next=(NavigationCommandSlots<long,NavigationArrivalReservation>)typeof(NavigationSession).GetField("arrivalLedger",F).GetValue(session);var installed=(NavigationLiveArrivalReservation)next.OwnerFor(request.Request);Assert.AreNotSame(row,installed);Assert.AreEqual(NavArrivalReservationKind.InstalledExecution,installed.Entry.State);Assert.AreEqual(NavArrivalReservationKind.PendingRequest,row.Entry.State);
                foreach(string field in new[]{"proof","provider","profile","hold"})Assert.AreSame(typeof(NavigationLiveArrivalReservation).GetField(field,F).GetValue(row),typeof(NavigationLiveArrivalReservation).GetField(field,F).GetValue(installed),field+" exact metadata owner");
                // The capture probe has its own still-open preparation gate;
                // measure Ready reservations separately from that extra action.
                measuringReady=false;try{using(var capture=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation")){
                    var nav=capture.CaptureNavigation;var roots=new NavOwnedStorage[]{carrier,nav.CaptureRoot,nav.CaptureAuthority,nav.CaptureOutput,nav.CaptureArrivalOutput,nav.CaptureDto,capture.CaptureDomain,capture.CaptureNavigationBytes,capture.CaptureBinding,capture.CaptureParticipants,capture.CaptureVision};Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,roots),session.PhysicalHostBudget.PhysicalBytes);during=capture.Encode();
                }}finally{measuringReady=true;}
            };
            try{session.PhysicalHostBudget.AllocationAdmission=bytes=>{if(measuringReady&&Monitor.IsEntered(gate))underGate++;return true;};session.ApplyResults();}
            finally{session.PhysicalHostBudget.AllocationAdmission=null;session.ObserveOwnedReadyBeforeRetirement=null;}
            Assert.True(observed,"actual arrival Ready must use the prepared publication");Assert.Zero(underGate);Assert.AreEqual(1,session.OwnedReadyPublications);Assert.Zero(session.LastOwnedReadyPublicationAllocatedBytes);CollectionAssert.AreEqual(during,Bytes());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            Pump(1);var expected=Bytes();string facts=HostFacts();Restore(during);CollectionAssert.AreEqual(during,Bytes());Pump(1);CollectionAssert.AreEqual(expected,Bytes());Assert.AreEqual(facts,HostFacts());
            // Replacing the last member retires its old attempt/provider maps
            // in the same prepared publication as the new installed actor.
            int publications=session.OwnedReadyPublications;Arrival(entity,new NavPoint(-6,-14));for(int tick=0;tick<20000&&session.Answers.Count==0;tick++){Pump(64);if(session.Answers.Count==0)session.ApplyResults();}Assert.AreEqual(1,session.Answers.Count);session.ApplyResults();Assert.AreEqual(publications+1,session.OwnedReadyPublications);Assert.Zero(session.LastOwnedReadyPublicationAllocatedBytes);Assert.True(session.Crowd.Units.Single(u=>u.Id==entity).Moving);
        }
        [Test] public void LastMemberReadyRetiresExactOwnedTablesAtomicallyAndRestoresNextPump()
        {
            int entity=Spawn(-18,-14);
            void Answer(NavPoint goal){Arrival(entity,goal);for(int tick=0;tick<20000&&session.Answers.Count==0;tick++){Pump(64);if(session.Answers.Count==0)session.ApplyResults();}Assert.AreEqual(1,session.Answers.Count);}
            Answer(new NavPoint(-10,-14));session.ApplyResults();Assert.AreEqual(1,session.OwnedReadyPublications);
            var field=typeof(NavigationSession).GetField("liveArrivalTables",F);var old=(NavigationSession.NavigationLiveArrivalTables)field.GetValue(session);var original=old.Attempts[old.SortedId(0)];var originalProvider=old.References[original.OwnedProviders[0]].StorageOwner;long oldId=original.Id;int oldReferences=originalProvider.Record.References;
            var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var frozenBytes=frozen.Encode();old.Retain();
            try{
                Answer(new NavPoint(-6,-14));var before=Bytes();var source=(NavigationSession.NavigationLiveArrivalTables)field.GetValue(session);var request=session.Answers.CopyItems()[0].Request;int publications=session.OwnedReadyPublications;long physical=session.PhysicalHostBudget.PhysicalBytes;var budget=session.PhysicalHostBudget;int denied=0;
                long target=NavStorageLayout.Charge((long)typeof(NavigationSession.NavigationLiveArrivalTables).GetMethod("Layout",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{source.Attempts.Capacity}));
                try{budget.AllocationAdmission=bytes=>{if(bytes==target){denied++;return false;}return true;};session.ApplyResults();}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied);Assert.AreEqual(publications,session.OwnedReadyPublications);Assert.AreSame(source,field.GetValue(session));Assert.AreSame(request,session.PendingRequestFor(entity));Assert.AreEqual(1,session.Answers.Count);Assert.AreEqual(physical,budget.PhysicalBytes);CollectionAssert.AreEqual(before,Bytes());
                var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);int underGate=0;bool observed=false;byte[] during=null;
                session.ObserveOwnedReadyBeforeRetirement=carrier=>{observed=true;Assert.False(Monitor.IsEntered(gate));Assert.Zero(session.LastOwnedReadyPublicationAllocatedBytes);Assert.True(source.Attempts.ContainsKey(oldId));Assert.AreEqual(oldReferences,originalProvider.Record.References);var current=(NavigationSession.NavigationLiveArrivalTables)field.GetValue(session);Assert.AreNotSame(source,current);Assert.False(current.Attempts.ContainsKey(oldId));Assert.AreEqual(1,current.Count);CollectionAssert.AreEqual(frozenBytes,frozen.Encode());budget.AllocationAdmission=null;during=Bytes();};
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};session.ApplyResults();}finally{budget.AllocationAdmission=null;session.ObserveOwnedReadyBeforeRetirement=null;}
                Assert.True(observed);Assert.Zero(underGate);Assert.AreEqual(publications+1,session.OwnedReadyPublications);CollectionAssert.AreEqual(during,Bytes());CollectionAssert.AreEqual(frozenBytes,frozen.Encode());PhysicalShape(frozen,old);
                frozen.Dispose();frozen=null;old.Release();old=null;Assert.Zero(((NavOwnedStorage)source).References);Assert.Zero(originalProvider.References);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
                var suffix=Bytes();string facts=HostFacts();Pump(1);var next=Bytes();string nextFacts=HostFacts();Restore(before);CollectionAssert.AreEqual(before,Bytes());kernel.Attach(session.RoutePort);typeof(CertifiedRouteKernel).GetMethod("RefreshPhysicalFrontier",F).Invoke(kernel,new object[]{session.RoutePort});session.ApplyResults();Assert.AreEqual(1,session.OwnedReadyPublications);CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }finally{frozen?.Dispose();old?.Release();}
        }
        [Test] public void PreparedReadyBatchKeepsSortedBarrierThenMailboxFifoAndColdNextPump()
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-10),c=Spawn(-18,-6);Assert.True(session.Move(b,new NavPoint(-10,-10)));Assert.True(session.Move(a,new NavPoint(-10,-14)));
            for(int tick=0;tick<20000&&session.Answers.Count<2;tick++)Pump(64);Assert.AreEqual(2,session.Answers.Count);Assert.True(session.DeliveryBarrierReady);
            var barrier=(NavigationSession.NavigationBarrierAnswers)typeof(NavigationSession).GetField("barrierAnswers",F).GetValue(session);Assert.AreEqual(2,barrier.Count);barrier.Reverse();var expected=barrier.Select(answer=>answer.Request.Request).OrderBy(value=>value).ToList();
            Assert.True(session.Move(c,new NavPoint(-10,-6)));for(int tick=0;tick<20000&&session.Answers.Count==0;tick++)Pump(64);Assert.AreEqual(1,session.Answers.Count);expected.Add(session.Answers.CopyItems()[0].Request.Request);
            var actual=new System.Collections.Generic.List<long>();var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);session.ObserveOwnedReadyBeforeRetirement=carrier=>{Assert.False(Monitor.IsEntered(gate));Assert.Zero(session.LastOwnedReadyPublicationAllocatedBytes);actual.Add(((NavigationPreparedAnswerNames)carrier.GetType().GetField("Names",F).GetValue(carrier)).Answers[0].Request.Request);};
            try{session.ApplyResults();}finally{session.ObserveOwnedReadyBeforeRetirement=null;}
            CollectionAssert.AreEqual(expected,actual);Assert.AreEqual(3,session.OwnedReadyPublications);Assert.Zero(barrier.Count);Assert.Zero(session.Answers.Count);Assert.Zero(session.PendingCount);foreach(int entity in new[]{a,b,c}){Assert.True(session.Crowd.TryGet(entity,out var actor));Assert.True(actor.Moving);Assert.AreEqual(GroupMemberPhase.March,session.GroupFor(entity).Members.Single(m=>m.Entity==entity).Phase);}
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);var prefix=Bytes();Pump(1);var next=Bytes();string facts=HostFacts();Restore(prefix);CollectionAssert.AreEqual(prefix,Bytes());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(facts,HostFacts());
        }
        [TestCase(false)][TestCase(true)] public void ActualAttemptCaptureLeafHasExactBodyRolesAndOutlivesMutableSession(bool partition)
        {
            if(partition){kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");activeProfile=PlayableProfile.ThreeCrossingsDefault;domain=Activator.CreateInstance(Domain,F,null,new object[]{activeProfile,1L,false},null);session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);kernel=new CertifiedRouteKernel();}
            int a=Spawn(-18,partition?0:-14),b=Spawn(-18,partition?4:-10);Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Move,new[]{a,b},partition?new NavPoint(18,0):new NavPoint(-10,-12),origin:PlayableOrderOrigin.Human),null));
            kernel.PauseAtPhysicalBoundary=()=>session.ArrivalPhases.Length!=0;try{Pump(1);}finally{kernel.PauseAtPhysicalBoundary=null;}
            var attempts=(IEnumerable)typeof(NavigationSession).GetField("arrivalAttempts",F).GetValue(session);long id=0;foreach(var pair in attempts){id=(long)pair.GetType().GetProperty("Key").GetValue(pair);break;}Assert.Greater(id,0);
            var budget=session.PhysicalHostBudget;var before=Bytes();long charge=budget.PhysicalBytes;Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var denied));
            try{budget.AllocationAdmission=_=>false;Assert.Throws<InvalidOperationException>(()=>session.CaptureArrivalAttemptSnapshotOwned(id,denied.OwnedReservation()));Assert.AreEqual(charge,budget.PhysicalBytes);}
            finally{budget.AllocationAdmission=null;denied.Discard();}CollectionAssert.AreEqual(before,Bytes());
            byte[] Body(NavigationRetentionBudget owner,NavOwnedStorage root){Assert.True(owner.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var capture));try{using(NavStorageScratch.Reserve(capture.OwnedReservation(),2*1048576))using(var stream=new System.IO.MemoryStream(1048576)){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))NavPhysicalBodyWire.WriteTo(writer,root,capture.OwnedReservation());Assert.Less(stream.Length,1048576);return stream.ToArray();}}finally{capture.Discard();}}
            NavRetainedArray<NavOwnedStorage> bundle=null,coldBundle=null;var cold=new NavigationRetentionBudget();
            try{
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var scope));NavigationArrivalAttemptSnapshot snapshot=null;NavigationArrivalProviderSnapshot provider=null;int underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};snapshot=session.CaptureArrivalAttemptSnapshotOwned(id,scope.OwnedReservation());Assert.AreEqual(77,(int)snapshot.PhysicalTag.Storage);Assert.AreEqual(partition,snapshot.Work.OwnedArrivalAnchorPolicy);Assert.AreEqual(partition,snapshot.UsesAnchor);Assert.AreEqual(partition?NavPhysicalValueKind.OwnedArrivalAnchorV1:NavPhysicalValueKind.None,snapshot.PhysicalTag.Value);if(partition){Assert.NotNull(snapshot.FullFrontier);Assert.AreEqual(NavigationSession.ArrivalFrontier(snapshot.Work),snapshot.FullFrontier.Value);Assert.AreNotEqual(snapshot.Frontier.Value,snapshot.FullFrontier.Value);}else Assert.Null(snapshot.FullFrontier);Assert.AreEqual(NavigationSession.LiveArrivalFrontier(snapshot.Work),snapshot.Frontier.Value);Assert.AreEqual(232,NavStorageLayout.ObjectBytesBound(typeof(NavigationArrivalAttemptSnapshot)),"Historical leaf layout remains exact");Assert.LessOrEqual(NavStorageLayout.ObjectBytesBound(snapshot.GetType()),snapshot.StorageBytes);Assert.AreEqual(78,(int)NavPhysicalStorageKind.ArrivalProviderSnapshot);Assert.AreEqual(76,(int)NavPhysicalStorageKind.AuthorityCatalogRoutes);Assert.AreEqual(2,snapshot.WorkingCount);Assert.AreEqual(2,snapshot.ProviderCount);Assert.AreNotSame(snapshot.ProviderRoot(0),snapshot.ProviderRoot(1));Assert.AreEqual(((ILayeredNavigationProvider)snapshot.ProviderRoot(0).Facade).Binding,((ILayeredNavigationProvider)snapshot.ProviderRoot(1).Facade).Binding);Assert.AreEqual(snapshot.Data.InputProviderId,snapshot.WorkingId(0));Assert.AreNotEqual(snapshot.WorkingId(0),snapshot.WorkingId(1));provider=NavigationArrivalProviderSnapshot.AllocatePrepared(scope.OwnedReservation());provider.ObservePrepared(snapshot.Data.InputProviderId,1,snapshot.ProviderRoot(0));bundle=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{snapshot,provider,provider},scope.TryReserve);scope.Attach(bundle);scope.Commit(bundle,_=>{});Assert.Zero(underGate);}
                finally{budget.AllocationAdmission=null;snapshot?.ReleaseStorage();provider?.ReleaseStorage();scope.Discard();}
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,bundle),budget.PhysicalBytes);var bytes=Body(budget,bundle);var oldWork=((NavigationArrivalAttemptSnapshot)bundle[0]).Work;Assert.True(session.AdvancePhysicalArrivalAuthority(1));CollectionAssert.AreEqual(bytes,Body(budget,bundle));
                var witness=(((NavigationArrivalAttemptSnapshot)bundle[0]).FullFrontier??((NavigationArrivalAttemptSnapshot)bundle[0]).Frontier).Value;var needle=System.Text.Encoding.Unicode.GetBytes(witness);int offset=-1;for(int at=0;at<=bytes.Length-needle.Length;at++){bool match=true;for(int n=0;n<needle.Length;n++)if(bytes[at+n]!=needle[n]){match=false;break;}if(match){offset=at;break;}}Assert.GreaterOrEqual(offset,0);var corrupt=(byte[])bytes.Clone();corrupt[offset]=(byte)(corrupt[offset]=='0'?'1':'0');Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var bad));try{Assert.Throws<ArgumentException>(()=>{using(var stream=new System.IO.MemoryStream(corrupt,false))using(var reader=new System.IO.BinaryReader(stream))NavPhysicalBodyWire.ReadFrom(reader,bad.OwnedReservation(),NavigationHostBodyExtension.Instance);});}finally{bad.Discard();}Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
                if(partition){
                    void Reject(byte[] malformed,string message=null){Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var rejected));try{var error=Assert.Throws<ArgumentException>(()=>{using(var stream=new System.IO.MemoryStream(malformed,false))using(var reader=new System.IO.BinaryReader(stream))NavPhysicalBodyWire.ReadFrom(reader,rejected.OwnedReservation(),NavigationHostBodyExtension.Instance);});if(message!=null)Assert.AreEqual(message,error.Message);}finally{rejected.Discard();}Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);}
                    Assert.AreEqual(2,bytes[offset-21]);Assert.AreEqual(new NavPhysicalTag(NavPhysicalStorageKind.Metadata,value:NavPhysicalValueKind.Text).Encoded,BitConverter.ToInt32(bytes,offset-16));Assert.AreEqual(64,BitConverter.ToInt32(bytes,offset-4));
                    var missing=(byte[])bytes.Clone();missing[offset-21]=0;Reject(missing,"Missing physical macro edge.");
                    int tag=((NavigationArrivalAttemptSnapshot)bundle[0]).PhysicalTag.Encoded,tagOffset=-1,definitions=0;for(int at=5;at<=bytes.Length-4;at++)if(bytes[at-5]==2&&BitConverter.ToInt32(bytes,at)==tag){tagOffset=at;definitions++;}Assert.AreEqual(1,definitions);var mixed=(byte[])bytes.Clone();Array.Copy(BitConverter.GetBytes(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalAttemptSnapshot).Encoded),0,mixed,tagOffset,4);Reject(mixed);
                    using(var captured=session.CaptureOwnedState()){
                        var isolatedDomain=Activator.CreateInstance(Domain,F,null,new object[]{activeProfile,1L,false},null);var isolated=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(isolatedDomain);
                        try{typeof(NavigationSession).GetMethod("UseLegacyArrivalTablesForDecode",F).Invoke(isolated,null);var focused=new NavigationSessionState{ArrivalAuthority=captured.ArrivalAuthority,AllocationRequestIndices=Array.Empty<int>()};var error=Assert.Throws<TargetInvocationException>(()=>typeof(NavigationSession).GetMethod("RestoreArrivalAuthority",F).Invoke(isolated,new object[]{focused,Array.Empty<NavigationRequest>(),captured.CaptureRoot,null,null}));Assert.IsInstanceOf<ArgumentException>(error.InnerException,error.InnerException.ToString());Assert.AreEqual("Owned arrival anchor requires its full-witness catalog leaf.",error.InnerException.Message);}
                        finally{isolated.ReleaseCommittedHostRoot();Domain.GetMethod("DestroyOwnedNavigation",F).Invoke(isolatedDomain,null);Assert.Zero(isolated.PhysicalHostBudget.PhysicalBytes);Assert.Zero(isolated.PhysicalHostBudget.HeldBytes);Assert.Zero(isolated.PhysicalHostBudget.ActiveScopes);}
                    }
                }
                Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));try{using(var stream=new System.IO.MemoryStream(bytes,false))using(var reader=new System.IO.BinaryReader(stream)){coldBundle=(NavRetainedArray<NavOwnedStorage>)NavPhysicalBodyWire.ReadFrom(reader,decode.OwnedReservation(),NavigationHostBodyExtension.Instance);Assert.AreEqual(stream.Length,stream.Position);}decode.Attach(coldBundle);decode.Commit(coldBundle,_=>{});}finally{decode.Discard();}
                Assert.AreEqual(NavigationStorageOwnershipTests.Unique(coldBundle),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Body(cold,coldBundle));var restored=(NavigationArrivalAttemptSnapshot)coldBundle[0];var record=(NavigationArrivalProviderSnapshot)coldBundle[1];Assert.AreSame(coldBundle[1],coldBundle[2]);Assert.AreSame(restored.ProviderRoot(0),record.ProviderRoot);Assert.AreSame((restored.Work.PhysicalInput.Provider as INavStorageFacade).StorageRoot,record.ProviderRoot);Assert.AreEqual(1,record.LogicalReferences);Assert.AreEqual(0,restored.Data.ReasonSelector);Assert.Null(restored.InitialReason);Assert.AreEqual("gameplay-owner-removed",restored.RemovedReason.Value);
                kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");session=null;kernel=null;domain=null;Assert.AreEqual(NavigationStorageOwnershipTests.Unique(bundle),budget.PhysicalBytes);Assert.Greater(oldWork.References,0);CollectionAssert.AreEqual(bytes,Body(budget,bundle));
            }finally{coldBundle?.Release();bundle?.Release();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
        }
        [Test] public void ActualComputeAckRetiresWithoutAdmissionAndColdAckKeepsExactEmptyLayouts()
        {
            int entity=Spawn(-18,-14);Arrival(entity,new NavPoint(-10,-14));kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.ArrivalAdvance;try{Pump(1);}finally{kernel.PauseAtPhysicalBoundary=null;}Assert.True(session.PreparePhysicalArrivalAttempt());
            var field=typeof(NavigationSession).GetField("liveArrivalTables",F);var tables=(NavigationSession.NavigationLiveArrivalTables)field.GetValue(session);var attempt=tables.Attempts[tables.SortedId(0)];var owner=attempt.StorageOwner;var provider=tables.References[attempt.OwnedProviders[0]].StorageOwner;owner.Retain();provider.Retain();
            var budget=session.PhysicalHostBudget;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);PlayableWorldState frozen=null;byte[] retiring=null;int denied=0,underGate=0;bool computed=false;
            try{
                session.BeforeArrivalAdvanceCommit=_=>{
                    computed=true;Assert.True(attempt.Working);Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Stop,new[]{entity},origin:PlayableOrderOrigin.Human),null));Assert.True(attempt.Retiring);Assert.AreSame(owner.RemovedReason.Value,attempt.RetirementReason);Assert.Null(owner.Reason);
                    frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");retiring=frozen.Encode();var leaf=frozen.CaptureNavigation.CaptureAuthority.ArrivalCatalog.Attempt(0);Assert.AreEqual(2,leaf.Data.ReasonSelector);Assert.True(leaf.Data.RetiringCompute);Assert.AreSame(owner.RemovedReason,leaf.RemovedReason);Assert.Null(leaf.InitialReason);
                    budget.AllocationAdmission=_=>{denied++;if(Monitor.IsEntered(gate))underGate++;return false;};
                };
                try{Assert.Throws<InvalidOperationException>(()=>session.AdvancePhysicalArrivalAuthority(1));}finally{budget.AllocationAdmission=null;session.BeforeArrivalAdvanceCommit=null;}
                Assert.True(computed);Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.Zero(session.LastOwnedArrivalPruneAllocatedBytes);Assert.False(attempt.Working);Assert.AreEqual(1,owner.References);Assert.AreEqual(1,provider.References);Assert.Zero(provider.Record.References);Assert.AreSame(attempt.Input.Provider,provider.Record.Provider);
                var empty=(NavigationSession.NavigationLiveArrivalTables)field.GetValue(session);Assert.Zero(empty.Count);Assert.Zero(empty.References.Capacity);Assert.Zero(empty.Ids.Capacity);CollectionAssert.AreEqual(new[]{1,0},empty.Attempts.CaptureLayout());Assert.AreSame(empty.EmptyReferences,empty.References);Assert.AreSame(empty.EmptyIds,empty.Ids);CollectionAssert.AreEqual(retiring,frozen.Encode());PhysicalShape(frozen,owner,provider);
                frozen.Dispose();frozen=null;owner.Release();owner=null;provider.Release();provider=null;var cleaned=Bytes();string facts=HostFacts();Pump(1);var next=Bytes();string nextFacts=HostFacts();Restore(retiring);Assert.Zero(budget.PhysicalBytes);kernel.Attach(session.RoutePort);typeof(CertifiedRouteKernel).GetMethod("RefreshPhysicalFrontier",F).Invoke(kernel,new object[]{session.RoutePort});
                var restored=(NavigationSession.NavigationLiveArrivalTables)field.GetValue(session);var restoredAttempt=restored.Attempts[restored.SortedId(0)];Assert.AreSame(restoredAttempt.StorageOwner.RemovedReason.Value,restoredAttempt.RetirementReason);Assert.Null(restoredAttempt.StorageOwner.Reason);Assert.True(restoredAttempt.RestoredRetiringCompute);
                var cold=session.PhysicalHostBudget;int allocations=0;bool ack;long managed;try{cold.AllocationAdmission=_=>{allocations++;return false;};long before=GC.GetAllocatedBytesForCurrentThread();ack=session.RetireRestoredArrivalProjection();managed=GC.GetAllocatedBytesForCurrentThread()-before;}finally{cold.AllocationAdmission=null;}
                Assert.True(ack);Assert.Zero(allocations);Assert.Zero(managed);Assert.Zero(session.LastOwnedArrivalPruneAllocatedBytes);Assert.Zero(restored.Count);Assert.Zero(restored.References.Capacity);Assert.Zero(restored.Ids.Capacity);CollectionAssert.AreEqual(new[]{1,0},restored.Attempts.CaptureLayout());CollectionAssert.AreEqual(cleaned,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
            }finally{if(session!=null)session.BeforeArrivalAdvanceCommit=null;budget.AllocationAdmission=null;frozen?.Dispose();owner?.Release();provider?.Release();}
        }
        [TestCase(false,false)][TestCase(true,false)]
        [TestCase(false,true)][TestCase(true,true)]
        public void ActualIngressAuthorityChangeAtAdmissionReadGateRefusesAtomicallyAndRetries(bool secondGate,bool cancel)
        {
            ExerciseAdmissionReadChange(secondGate,cancel,false);
        }
        [TestCase(false)][TestCase(true)]
        public void ActualIngressNeighborMoveAtAdmissionReadGateRefusesWithoutPublication(bool secondGate)
        {
            ExerciseAdmissionReadChange(secondGate,false,true);
        }
        [Test] public void AdmissionCountChangeWithoutOperationChangeRemainsInvariantFailure()
        {
            int entity=Spawn(-18,-14);Arrival(entity,new NavPoint(-10,-14));
            var budget=session.PhysicalHostBudget;
            var groups=typeof(NavigationSession).GetField("groups",F).GetValue(session);
            var owner=(NavigationAdmissionStorage)typeof(NavigationSession).GetField("admissionOwner",F).GetValue(session);
            long operation=(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session);
            int mailboxCount=session.Requests.Count;
            var requests=new System.Collections.Generic.Dictionary<int,NavigationRequest>();
            var probes=new System.Collections.Generic.Dictionary<int,NavigationRequest>();
            Assert.True(session.TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,1,session.Admission.PublicationIdentity,out var scope));
            bool changed=false;
            try{
                budget.AllocationAdmission=_=>{if(!changed){changed=true;requests.Add(999,null);}return true;};
                var failure=Assert.Throws<TargetInvocationException>(()=>typeof(NavigationSession).GetMethod("PrepareAdmissionProjection",F).Invoke(session,new object[]{groups,requests,0L,session.Admission.PublicationIdentity+1,operation,scope,owner,null,null,probes,null,0}));
                Assert.IsTrue(changed);Assert.AreEqual(typeof(InvalidOperationException),failure.InnerException.GetType());
                Assert.AreEqual("Admission copy lost read-set.",failure.InnerException.Message);
                Assert.AreEqual(operation,(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(session));
            }finally{budget.AllocationAdmission=null;scope.Discard();}
            Assert.AreSame(owner.Value,session.Admission);Assert.AreEqual(mailboxCount,session.Requests.Count);
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
            Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        private void ExerciseAdmissionReadChange(bool secondGate,bool cancel,bool moveNeighbor)
        {
            int entity=Spawn(-18,-14),other=Spawn(-18,-6);Arrival(entity,new NavPoint(-10,-14));
            bool AtIngressBoundary()=>session.Requests.Count>0&&kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Transfer;
            kernel.PauseAtPhysicalBoundary=AtIngressBoundary;
            try{for(int n=0;n<20000&&!AtIngressBoundary();n++){Pump(4);session.ApplyResults();}}
            finally{kernel.PauseAtPhysicalBoundary=null;}
            Assert.AreEqual(1,session.Requests.Count);Assert.True(AtIngressBoundary());
            Assert.AreSame(kernel,typeof(NavigationSession).GetField("certifiedHost",F).GetValue(session));
            long attachment=(long)typeof(NavigationSession).GetField("certifiedHostAttachmentIdentity",F).GetValue(session);
            Assert.True(session.TryObservePhysicalArrivalIngress(out var ingress,out var pin,out var input,out var proposal,out var token,out var attempt));
            var budget=session.PhysicalHostBudget;bool changed=false;byte[] afterCommand=null;
            NavigationRequest[] mailbox=null;NavigationAdmission afterAdmission=null;
            var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            var operationField=typeof(NavigationSession).GetField("authorityOperationIdentity",F);
            using(pin){
                var source=session.PinCommittedHostRoot();NavigationHostRoot candidate=null;
                Assert.AreEqual(source.Frontier,kernel.CapturePumpFrontier());Assert.AreEqual(source.Epoch,kernel.SolverEpoch);
                Assert.True(session.TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,1,pin.Value.PublicationIdentity,out var scope));
                try{
                    candidate=source.ForkOwned(scope.OwnedReservation());
                    var key=candidate.RegisterArrival(ingress,proposal.Proof.PhysicalRecipe,proposal.Route,token,pin.Value.SurfaceProviderId,scope.OwnedReservation());candidate.SealPrepared();
                    budget.AllocationAdmission=_=>{
                        if(changed)return true;
                        var frames=new System.Diagnostics.StackTrace().GetFrames();
                        bool target=secondGate?
                            frames.Any(f=>f.GetMethod().Name=="PrepareAdmissionProjection"):
                            frames.Any(f=>f.GetMethod().DeclaringType?.Name=="NavigationLiveArrivalLease"&&f.GetMethod().Name=="CreateOwned");
                        if(!target)return true;
                        Assert.False(Monitor.IsEntered(gate),"mutation runs outside transport gate");
                        changed=true;budget.AllocationAdmission=null;
                        long operation=(long)operationField.GetValue(session);
                        if(cancel)Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Stop,new[]{entity},origin:PlayableOrderOrigin.Human),null));
                        else if(moveNeighbor)Arrival(other,new NavPoint(-10,-6));
                        // The normal owned capture publication itself advances authority
                        // operation identity without changing the pending route or HOLD set.
                        afterCommand=Bytes();afterAdmission=session.Admission;mailbox=session.Requests.CopyItems();
                        Assert.AreNotEqual(operation,(long)operationField.GetValue(session));
                        if(!cancel&&!moveNeighbor){
                            Assert.AreSame(ingress,session.PendingRequestFor(entity));Assert.True(session.Admission.Contains(ingress));
                            Assert.True((bool)typeof(NavigationSession).GetMethod("CurrentHeldBinding",F).Invoke(session,new object[]{ingress.HeldIdentity,entity}));
                            Assert.True((bool)typeof(NavigationSession).GetMethod("IngressObservationCompatible",F).Invoke(session,new object[]{pin.Value,ingress}));
                            Assert.AreSame(kernel,typeof(NavigationSession).GetField("certifiedHost",F).GetValue(session));
                            Assert.AreEqual(attachment,(long)typeof(NavigationSession).GetField("certifiedHostAttachmentIdentity",F).GetValue(session));
                            Assert.True(session.TryObservePhysicalArrivalIngress(out var sameRequest,out var freshPin,out var sameInput,out var sameProposal,out var sameToken,out var sameAttempt));
                            using(freshPin){Assert.AreSame(input,sameInput);Assert.AreSame(ingress,sameRequest);Assert.AreSame(proposal.Route,sameProposal.Route);Assert.AreSame(proposal.Proof.PhysicalRecipe,sameProposal.Proof.PhysicalRecipe);Assert.AreEqual(token,sameToken);Assert.AreEqual(attempt,sameAttempt);}
                        }
                        return true;
                    };
                    Assert.Throws<NavigationSession.PhysicalIngressRefused>(()=>session.PublishRegisteredPhysicalArrival(scope,source,candidate,pin.Value,ingress,key,attempt));
                    Assert.True(changed,"must reach the selected admission read gate");
                    var current=session.PinCommittedHostRoot();try{Assert.AreSame(source,current);}finally{current.Release();}
                    Assert.AreSame(afterAdmission,session.Admission);CollectionAssert.AreEqual(mailbox,session.Requests.CopyItems());
                    Assert.Zero(session.OwnedIngressPublications);Assert.Zero(session.RegisteredRouteCount);Assert.Zero(ArrivalLeaseCount);
                    CollectionAssert.AreEqual(afterCommand,Bytes());
                }finally{budget.AllocationAdmission=null;candidate?.ReleaseStorage();scope.Discard();source.Release();}
            }
            Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes,"refused private ingress leaves no retained owners");
            if(moveNeighbor)return;
            if(cancel){
                Assert.Null(session.PendingRequestFor(entity));
                for(int n=0;n<3;n++){Pump(4);session.ApplyResults();}
                Assert.False(HostRows.Any(row=>row.Request.Request==ingress.Request));Assert.Null(session.InstalledExecutionFor(entity));
            }else{
                Assert.True(AtIngressBoundary());
                var current=session.PinCommittedHostRoot();try{Assert.AreEqual(current.Frontier,kernel.CapturePumpFrontier());Assert.AreEqual(current.Epoch,kernel.SolverEpoch);}finally{current.Release();}
                Assert.True((bool)typeof(CertifiedRouteKernel).GetMethod("TryRegisterPhysicalArrival",F).Invoke(kernel,new object[]{Binding()}),"one fresh kernel observation retries the refused ingress");
                Assert.True(HostRows.Any(row=>row.Request.Entity==entity));
                Assert.AreEqual(1,session.OwnedIngressPublications);Assert.AreEqual(1,ArrivalLeaseCount);
                Assert.AreEqual(1,session.RegisteredRouteCount);Assert.Zero(session.LastOwnedIngressPublicationAllocatedBytes);
            }
        }
        [Test] public void ActualJointIngressCarrierRefusalPreservesExactMailboxAndAuthority()
        {
            int entity=Spawn(-18,-14);Arrival(entity,new NavPoint(-10,-14));kernel.PauseAtPhysicalBoundary=()=>session.Requests.Count>0;
            try{for(int n=0;n<20000&&session.Requests.Count==0;n++){Pump(4);session.ApplyResults();}}finally{kernel.PauseAtPhysicalBoundary=null;}
            Assert.AreEqual(1,session.Requests.Count);Assert.True(session.TryObservePhysicalArrivalIngress(out var ingress,out var pin,out _,out var proposal,out var token,out var attempt));using(pin){
                var source=session.PinCommittedHostRoot();NavigationHostRoot candidate=null;var budget=session.PhysicalHostBudget;Assert.True(session.TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,1,pin.Value.PublicationIdentity,out var scope));
                try{
                    candidate=source.ForkOwned(scope.OwnedReservation());var key=candidate.RegisterArrival(ingress,proposal.Proof.PhysicalRecipe,proposal.Route,token,pin.Value.SurfaceProviderId,scope.OwnedReservation());candidate.SealPrepared();
                    var before=Bytes();long charge=budget.PhysicalBytes,held=budget.HeldBytes;int publications=session.OwnedIngressPublications;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);int denied=0,underGate=0;
                    var type=typeof(NavigationSession).GetNestedType("PreparedIngress",BindingFlags.NonPublic);long target=NavStorageLayout.Charge((long)type.GetField("Layout",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null));
                    try{budget.AllocationAdmission=bytes=>{if(Monitor.IsEntered(gate))underGate++;if(bytes==target){denied++;return false;}return true;};Assert.Throws<InvalidOperationException>(()=>session.PublishRegisteredPhysicalArrival(scope,source,candidate,pin.Value,ingress,key,attempt));}finally{budget.AllocationAdmission=null;}
                    Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreEqual(publications,session.OwnedIngressPublications);Assert.Zero(session.RegisteredRouteCount);Assert.Zero(ArrivalLeaseCount);Assert.AreSame(ingress,session.Requests.CopyItems()[0]);Assert.AreSame(ingress,session.PendingRequestFor(entity));Assert.AreEqual(charge,budget.PhysicalBytes);Assert.AreEqual(held,budget.HeldBytes);CollectionAssert.AreEqual(before,Bytes());
                }finally{budget.AllocationAdmission=null;candidate?.ReleaseStorage();scope.Discard();source.Release();}
            }
        }
        [Test] public void ActualJointIngressCancellationRestoresExactRetirementSuffixAndNextPump()
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-10);
            void Human(PlayableCommandKind kind,int[] ids,NavPoint goal=default)=>Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",kind,ids,goal,origin:PlayableOrderOrigin.Human),null));
            Human(PlayableCommandKind.Move,new[]{a,b},new NavPoint(-10,-12));kernel.Continue=()=>ArrivalLeaseCount<2;
            try{for(int n=0;n<20000&&ArrivalLeaseCount<2;n++){Pump(4);session.ApplyResults();}}finally{kernel.Continue=null;}
            Assert.AreEqual(2,ArrivalLeaseCount);Assert.AreEqual(2,session.OwnedIngressPublications);Assert.Zero(session.LastOwnedIngressPublicationAllocatedBytes);Assert.AreEqual(2,session.RegisteredRouteCount);
            var rows=HostRows;var sourceBudget=session.PhysicalHostBudget;var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var frozenBytes=frozen.Encode();
            try{
                Human(PlayableCommandKind.Stop,new[]{a});Human(PlayableCommandKind.Stop,new[]{b});Assert.Zero(session.LastOwnedArrivalPruneAllocatedBytes);Assert.AreEqual(2,ArrivalLeaseCount);Assert.True(session.NextRetiringArrivalProjection().Retiring);CollectionAssert.AreEqual(frozenBytes,frozen.Encode());var prefix=Bytes();
                var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);int underGate=0;
                sourceBudget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};
                try{Assert.True(session.RetirePhysicalArrivalOwner());Assert.Zero(session.LastOwnedLeaseReleaseAllocatedBytes);Assert.AreEqual(1,ArrivalLeaseCount);Assert.True(session.RetirePhysicalArrivalOwner());Assert.Zero(session.LastOwnedLeaseReleaseAllocatedBytes);}finally{sourceBudget.AllocationAdmission=null;}
                Assert.Zero(underGate);Assert.Zero(ArrivalLeaseCount);Assert.Zero(session.RegisteredRouteCount);var table=(NavigationSession.NavigationLiveArrivalLeaseTable)typeof(NavigationSession).GetField("liveArrivalLeaseTable",F).GetValue(session);Assert.Zero(table.Tokens.Capacity);Assert.Zero(table.Count);Assert.Null(table.RetirementHead);CollectionAssert.AreEqual(frozenBytes,frozen.Encode());PhysicalShape(frozen);
                frozen.Dispose();frozen=null;var suffix=Bytes();string facts=HostFacts();Pump(1);var next=Bytes();string nextFacts=HostFacts();Restore(prefix);Assert.Zero(sourceBudget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());kernel.Attach(session.RoutePort);typeof(CertifiedRouteKernel).GetMethod("RefreshPhysicalFrontier",F).Invoke(kernel,new object[]{session.RoutePort});Assert.True(session.RetirePhysicalArrivalOwner());Assert.Zero(session.LastOwnedLeaseReleaseAllocatedBytes);Assert.True(session.RetirePhysicalArrivalOwner());Assert.Zero(session.LastOwnedLeaseReleaseAllocatedBytes);CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{sourceBudget.AllocationAdmission=null;frozen?.Dispose();}
        }
        [Test] public void CompactRegistrationOwnsExactRowsThroughHostAndSessionDestruction()
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-10);Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Move,new[]{a,b},new NavPoint(-10,-12),origin:PlayableOrderOrigin.Human),null));kernel.Continue=()=>ArrivalLeaseCount<2;try{for(int n=0;n<20000&&ArrivalLeaseCount<2;n++){Pump(4);session.ApplyResults();}}finally{kernel.Continue=null;}Assert.AreEqual(2,ArrivalLeaseCount);var rows=HostRows;Assert.AreEqual(2,rows.Length);
            var first=rows[0];var second=rows[1];var request=first.Request;string key=first.Scheduled.GraphKey;var budget=session.PhysicalHostBudget;Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));NavigationLiveRegistrations root=null;
            try{
                root=NavigationLiveRegistrations.CreateOwned(scope.OwnedReservation());root.AppendColdOwned(first);root.AppendColdOwned(second);scope.Attach(root);scope.Commit(root,_=>{});Assert.AreEqual(4096,NavigationLiveRegistrations.Capacity);Assert.AreSame(first.RequestOwner,root.RequestOwnerAt(0));Assert.AreSame(first.RecipeOwner,root.RecipeOwnerAt(0));Assert.True(root.TryGraphKey(request,out var value));Assert.AreSame(key,value);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,root),budget.PhysicalBytes);
                var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);int admissions=0;long charge=budget.PhysicalBytes;budget.AllocationAdmission=_=>{admissions++;return false;};long allocated;
                lock(gate){long before=GC.GetAllocatedBytesForCurrentThread();int revision=root.Revision;if(!root.CanExtract(0,request,revision))throw new InvalidOperationException("Missing compact registration.");root.ExtractNoThrow(0,out var requestOwner,out var recipeOwner);if(!root.CanAppend(requestOwner,recipeOwner,root.Revision,root.Count))throw new InvalidOperationException("Invalid compact append.");root.AppendNoThrow(requestOwner,recipeOwner);allocated=GC.GetAllocatedBytesForCurrentThread()-before;}
                budget.AllocationAdmission=null;Assert.Zero(admissions);Assert.Zero(allocated);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.AreSame(second.Request,root.RequestAt(0));Assert.AreSame(request,root.RequestAt(1));Assert.AreEqual(1,root.IndexOf(request));Assert.True(root.TryGraphKey(request,out value));Assert.AreSame(key,value);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,root),budget.PhysicalBytes);
                kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");session=null;kernel=null;domain=null;Assert.AreSame(request,root.RequestAt(1));Assert.True(root.TryGraphKey(request,out value));Assert.AreSame(key,value);Assert.Greater(first.RequestOwner.References,0);Assert.Greater(first.RecipeOwner.References,0);Assert.AreEqual(NavigationStorageOwnershipTests.Unique(root),budget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;root?.ReleaseStorage();scope.Discard();}Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void PhysicalLeaseOwnersKeepRoundedGrowthIntrusiveOrderAndFrozenBodyAfterReset()
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-10),c=Spawn(-18,-6);Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Move,new[]{a,b,c},new NavPoint(-10,-10),origin:PlayableOrderOrigin.Human),null));kernel.PauseAtPhysicalBoundary=()=>session.ArrivalPhases.Length!=0;try{Pump(1);}finally{kernel.PauseAtPhysicalBoundary=null;}
            var source=(NavigationSession.NavigationLiveArrivalTables)typeof(NavigationSession).GetField("liveArrivalTables",F).GetValue(session);var attempt=source.Attempts[source.SortedId(0)];var budget=session.PhysicalHostBudget;Assert.True(budget.TryOpenScope(NavigationPreparationKind.Arrival,1,1,out var scope));
            NavigationSession.NavigationLiveArrivalLeaseTable table=null;NavigationSession.NavigationLiveArrivalLease one=null,two=null,three=null,retired=null;NavigationArrivalLeaseSnapshot snapshot=null,coldSnapshot=null;NavigationArrivalLeaseCatalog catalog=null,coldCatalog=null;var cold=new NavigationRetentionBudget();
            byte[] Body(NavigationRetentionBudget ledger,NavOwnedStorage root){Assert.True(ledger.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var capture));try{using(NavStorageScratch.Reserve(capture.OwnedReservation(),2*1048576))using(var stream=new System.IO.MemoryStream(1048576)){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))NavPhysicalBodyWire.WriteTo(writer,root,capture.OwnedReservation());return stream.ToArray();}}finally{capture.Discard();}}
            try{
                var r=scope.OwnedReservation();Assert.AreEqual(3,NavigationSession.NavigationLiveArrivalLeaseTable.GrowthCapacity(0,0,r));Assert.AreEqual(3,NavigationSession.NavigationLiveArrivalLeaseTable.GrowthCapacity(3,2,r));Assert.AreEqual(7,NavigationSession.NavigationLiveArrivalLeaseTable.GrowthCapacity(3,3,r));Assert.AreEqual(17,NavigationSession.NavigationLiveArrivalLeaseTable.GrowthCapacity(7,7,r));
                NavigationSession.NavigationLiveArrivalLease Create(int index){var request=attempt.Intents[index];var provider=attempt.Input.WorkingBindings[index].Provider;return NavigationSession.NavigationLiveArrivalLease.CreateOwned(new NavigationArrivalLeaseValues(index+1,index+1,0,session.Generation,attempt.Id,request.Request,request.Entity,request.MemberIdentity.Incarnation,request.Order,source.References[provider].Id,false),session,provider,r);}
                one=Create(0);two=Create(1);three=Create(2);table=NavigationSession.NavigationLiveArrivalLeaseTable.AllocatePrepared(3,r);table.AddPreparing(one);table.AddPreparing(two);table.AddPreparing(three);table.SealPrepared();Assert.True(table.CanMarkRetiring(two.Record));table.MarkRetiringNoThrow(two.Record,10);Assert.True(table.CanMarkRetiring(one.Record));table.MarkRetiringNoThrow(one.Record,11);Assert.AreSame(two,table.RetirementHead);Assert.AreSame(one,table.RetirementTail);Assert.AreSame(one,two.RetirementNext);Assert.AreSame(two,one.RetirementPrevious);
                snapshot=NavigationArrivalLeaseSnapshot.AllocatePrepared(r);snapshot.ObservePrepared(NavigationArrivalLeaseValues.Read(one.Record),one.ProviderRoot,one.Binding,one.InitialReason,one.RemovedReason);snapshot.SealPrepared(r);catalog=NavigationArrivalLeaseCatalog.AllocatePrepared(3,3,1,r);catalog.ObserveValues(new NavigationArrivalLeaseCatalog.Values(session.Generation,3,3,11,0));catalog.ObserveLease(0,snapshot);
                for(int i=1;i<3;i++){var owner=i==1?two:three;var leaf=NavigationArrivalLeaseSnapshot.AllocatePrepared(r);try{leaf.ObservePrepared(NavigationArrivalLeaseValues.Read(owner.Record),owner.ProviderRoot,owner.Binding,owner.InitialReason,owner.RemovedReason);leaf.SealPrepared(r);catalog.ObserveLease(i,leaf);}finally{leaf.ReleaseStorage();}}catalog.ObserveLayout(table.Tokens);for(int i=0;i<3;i++)catalog.ObserveAcquisition(i,table.AcquisitionToken(i));catalog.SealPrepared(r);scope.Attach(table);scope.Commit(table,_=>{});var bytes=Body(budget,snapshot);Assert.AreEqual(81,(int)snapshot.PhysicalTag.Storage);
                Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));try{using(var stream=new System.IO.MemoryStream(bytes,false))using(var reader=new System.IO.BinaryReader(stream)){coldSnapshot=(NavigationArrivalLeaseSnapshot)NavPhysicalBodyWire.ReadFrom(reader,decode.OwnedReservation(),NavigationHostBodyExtension.Instance);Assert.AreEqual(stream.Length,stream.Position);}decode.Attach(coldSnapshot);decode.Commit(coldSnapshot,_=>{});}finally{decode.Discard();}CollectionAssert.AreEqual(bytes,Body(cold,coldSnapshot));Assert.True(snapshot.Data.Same(coldSnapshot.Data));Assert.AreEqual(one.RemovedReason.Value,coldSnapshot.CurrentReason);Assert.AreEqual(NavigationStorageOwnershipTests.Unique(coldSnapshot),cold.PhysicalBytes);
                var catalogBytes=Body(budget,catalog);Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var catalogDecode));try{using(var stream=new System.IO.MemoryStream(catalogBytes,false))using(var reader=new System.IO.BinaryReader(stream)){coldCatalog=(NavigationArrivalLeaseCatalog)NavPhysicalBodyWire.ReadFrom(reader,catalogDecode.OwnedReservation(),NavigationHostBodyExtension.Instance);Assert.AreEqual(stream.Length,stream.Position);}catalogDecode.Attach(coldCatalog);catalogDecode.Commit(coldCatalog,_=>{});}finally{catalogDecode.Discard();}CollectionAssert.AreEqual(catalogBytes,Body(cold,coldCatalog));Assert.AreEqual(82,(int)coldCatalog.PhysicalTag.Storage);Assert.AreEqual(3,coldCatalog.Capacity);Assert.AreEqual(3,coldCatalog.LayoutValue(0));Assert.AreEqual(1,coldCatalog.LayoutLength);Assert.AreEqual(1,coldCatalog.AcquisitionToken(0));Assert.AreEqual(10,coldCatalog.Lease(1).Data.Retirement);Assert.AreEqual(11,coldCatalog.Lease(0).Data.Retirement);Assert.AreNotSame(coldCatalog.Lease(0).ProviderRoot,coldCatalog.Lease(1).ProviderRoot);Assert.AreEqual(((ILayeredNavigationProvider)coldCatalog.Lease(0).ProviderRoot.Facade).Binding,((ILayeredNavigationProvider)coldCatalog.Lease(1).ProviderRoot.Facade).Binding);Assert.AreEqual(NavigationStorageOwnershipTests.Unique(coldSnapshot,coldCatalog),cold.PhysicalBytes);
                Assert.AreEqual(2,one.References);Assert.AreEqual(2,two.References);int admissions=0;long charge=budget.PhysicalBytes;budget.AllocationAdmission=_=>{admissions++;return false;};long allocated;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
                lock(gate){long before=GC.GetAllocatedBytesForCurrentThread();if(!table.CanExtract(two.Record,out int slot,out int acquisition))throw new InvalidOperationException("Missing lease2.");retired=table.ExtractNoThrow(slot,acquisition);if(!table.CanExtract(one.Record,out slot,out acquisition))throw new InvalidOperationException("Missing lease1.");var node=table.ExtractNoThrow(slot,acquisition);node.RetiredNext=retired;retired=node;if(!table.CanExtract(three.Record,out slot,out acquisition))throw new InvalidOperationException("Missing lease3.");node=table.ExtractNoThrow(slot,acquisition);node.RetiredNext=retired;retired=node;allocated=GC.GetAllocatedBytesForCurrentThread()-before;}
                Assert.Zero(allocated);Assert.Zero(admissions);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.AreEqual(2,one.References);Assert.Zero(table.Count);Assert.Zero(table.Tokens.Capacity);Assert.AreSame(table.EmptyTokens,table.Tokens);Assert.Null(table.ResetTokens);Assert.Null(table.RetirementHead);Assert.Null(table.RetirementTail);Assert.AreSame(attempt.Input.Provider,one.Record.Provider);Assert.False(one.Record.Attached);budget.AllocationAdmission=null;
                NavigationSession.ReleaseRetiredArrivalLeases(retired);retired=null;Assert.AreEqual(1,one.References);Assert.AreEqual(1,two.References);Assert.AreEqual(1,three.References);CollectionAssert.AreEqual(bytes,Body(budget,snapshot));Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,table,one,two,three,snapshot,catalog),budget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;NavigationSession.ReleaseRetiredArrivalLeases(retired);catalog?.ReleaseStorage();coldCatalog?.ReleaseStorage();snapshot?.ReleaseStorage();coldSnapshot?.ReleaseStorage();table?.ReleaseStorage();one?.ReleaseStorage();two?.ReleaseStorage();three?.ReleaseStorage();scope.Discard();}
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
        }
        [Test] public void LiveProviderTablesRetireRepeatedResetsWithoutAdmissionOrGatedRelease()
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-10);Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Move,new[]{a,b},new NavPoint(-10,-12),origin:PlayableOrderOrigin.Human),null));
            kernel.PauseAtPhysicalBoundary=()=>session.ArrivalPhases.Length!=0;try{Pump(1);}finally{kernel.PauseAtPhysicalBoundary=null;}
            var rows=(IEnumerable)typeof(NavigationSession).GetField("arrivalAttempts",F).GetValue(session);NavigationSession.ArrivalAttempt attempt=null;foreach(var pair in rows){attempt=(NavigationSession.ArrivalAttempt)pair.GetType().GetProperty("Value").GetValue(pair);break;}Assert.NotNull(attempt);
            var budget=session.PhysicalHostBudget;Assert.True(budget.TryOpenScope(NavigationPreparationKind.Arrival,1,1,out var scope));var r=scope.OwnedReservation();
            NavigationSession.NavigationLiveArrivalTables first=null,second=null;NavigationSession.NavigationLiveArrivalProvider one=null,two=null,three=null,four=null,retired=null;
            try{
                first=NavigationSession.NavigationLiveArrivalTables.AllocatePrepared(3,3,r);second=NavigationSession.NavigationLiveArrivalTables.AllocatePrepared(0,2,r,first);
                one=NavigationSession.NavigationLiveArrivalProvider.CreateOwned(1,1,attempt.OwnedProviders[0],r);two=NavigationSession.NavigationLiveArrivalProvider.CreateOwned(2,1,attempt.OwnedProviders[1],r);
                three=NavigationSession.NavigationLiveArrivalProvider.CreateOwned(3,1,attempt.OwnedProviders[0],r);four=NavigationSession.NavigationLiveArrivalProvider.CreateOwned(4,1,attempt.OwnedProviders[1],r);
                first.AddProviderPreparing(one);first.AddProviderPreparing(two);first.AddAttemptPreparing(attempt.StorageOwner);first.SealPrepared();second.AddProviderPreparing(three);second.AddProviderPreparing(four);second.SealPrepared();scope.Attach(first);scope.Commit(first,_=>{});
                Assert.AreSame(first.EmptyReferences,second.EmptyReferences);Assert.AreSame(first.EmptyIds,second.EmptyIds);Assert.AreNotSame(one,two);Assert.AreNotSame(one.ProviderRoot,two.ProviderRoot);Assert.AreEqual(one.Record.Provider.Binding,two.Record.Provider.Binding);
                // Factory references are the observer pins. Each index adds one
                // separate owning edge to the same record, not another record.
                Assert.AreEqual(3,one.References);Assert.AreEqual(3,two.References);long bytes=budget.PhysicalBytes;int admissions=0;budget.AllocationAdmission=_=>{admissions++;return false;};
                Assert.True(first.CanExtractProvider(one.Record,out int ar,out int ai));Assert.True(first.CanExtractProvider(two.Record,out int br,out int bi));Assert.True(second.CanExtractProvider(three.Record,out int cr,out int ci));Assert.True(second.CanExtractProvider(four.Record,out int dr,out int di));
                var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);long allocated;
                lock(gate){long before=GC.GetAllocatedBytesForCurrentThread();
                    retired=first.ExtractProviderNoThrow(one.Record,ar,ai);var node=first.ExtractProviderNoThrow(two.Record,br,bi);node.RetiredNext=retired;retired=node;
                    node=second.ExtractProviderNoThrow(three.Record,cr,ci);node.RetiredNext=retired;retired=node;node=second.ExtractProviderNoThrow(four.Record,dr,di);node.RetiredNext=retired;retired=node;
                    allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                }
                Assert.Zero(allocated);Assert.Zero(admissions);Assert.AreEqual(bytes,budget.PhysicalBytes);Assert.AreEqual(3,one.References);Assert.AreEqual(3,two.References);Assert.AreSame(attempt.OwnedProviders[0],one.Record.Provider);
                Assert.Zero(first.References.Capacity);Assert.Zero(first.Ids.Capacity);Assert.Zero(second.References.Capacity);Assert.Zero(second.Ids.Capacity);Assert.AreSame(first.EmptyReferences,first.References);Assert.AreSame(first.EmptyIds,first.Ids);Assert.AreSame(first.References,second.References);Assert.Null(first.ResetReferences);Assert.Null(first.ResetIds);
                NavigationSession.ReleaseRetiredArrivalProviders(retired);retired=null;Assert.AreEqual(1,one.References);Assert.AreEqual(1,two.References);Assert.AreEqual(1,three.References);Assert.AreEqual(1,four.References);Assert.AreSame(attempt.OwnedProviders[0],one.Record.Provider);Assert.False(first.CanExtractProvider(one.Record,out _,out _));
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,first,second,one,two,three,four),budget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;NavigationSession.ReleaseRetiredArrivalProviders(retired);first?.ReleaseStorage();second?.ReleaseStorage();one?.ReleaseStorage();two?.ReleaseStorage();three?.ReleaseStorage();four?.ReleaseStorage();scope.Discard();}
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [TestCase(false)] [TestCase(true)] public void LiveAttemptHeaderRejectsCompletedBatchAndHostAttachmentAba(bool aba)
        {
            int entity=Spawn(-18,-14);Arrival(entity,new NavPoint(-10,-14));kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.ArrivalAdvance;
            try{Pump(1);}finally{kernel.PauseAtPhysicalBoundary=null;}
            var type=typeof(NavigationSession).GetNestedType("NavigationLiveArrivalAttempt",BindingFlags.NonPublic);long target=NavStorageLayout.Charge((long)type.GetMethod("Layout",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{1}));bool changed=false;byte[] expected=null;
            try{session.PhysicalHostBudget.AllocationAdmission=bytes=>{if(bytes==target&&!changed){changed=true;if(aba){kernel.Clear();kernel.Attach(session.RoutePort);}else typeof(NavigationSession).GetMethod("AdmissionBatch",F,null,new[]{typeof(Action)},null).Invoke(session,new object[]{(Action)(()=>{})});expected=Bytes();}return true;};Assert.False(session.PreparePhysicalArrivalAttempt());}
            finally{session.PhysicalHostBudget.AllocationAdmission=null;}
            Assert.True(changed);Assert.AreEqual(1,session.AllocationCount);Assert.Zero(session.Requests.Count);Assert.That(session.ArrivalPhases,Is.Empty);CollectionAssert.AreEqual(expected,Bytes());Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);Assert.True(session.PreparePhysicalArrivalAttempt());
        }
        [Test] public void LiveAttemptHeaderRefusalIsAtomicAndPinnedHeaderOutlivesSource()
        {
            int entity=Spawn(-18,-14);Arrival(entity,new NavPoint(-10,-14));kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.ArrivalAdvance;
            try{Pump(1);}finally{kernel.PauseAtPhysicalBoundary=null;}
            var before=Bytes();string facts=HostFacts();var budget=session.PhysicalHostBudget;long physical=budget.PhysicalBytes;int denied=0;
            var type=typeof(NavigationSession).GetNestedType("NavigationLiveArrivalAttempt",BindingFlags.NonPublic);Assert.NotNull(type);long target=NavStorageLayout.Charge((long)type.GetMethod("Layout",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{1}));
            try{budget.AllocationAdmission=bytes=>{if(bytes==target){denied++;return false;}return true;};Assert.False(session.PreparePhysicalArrivalAttempt());}
            finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());Assert.AreEqual(facts,HostFacts());Assert.AreEqual(1,session.AllocationCount);Assert.Zero(session.Requests.Count);
            Assert.True(session.PreparePhysicalArrivalAttempt());
            var attempts=typeof(NavigationSession).GetField("arrivalAttempts",F).GetValue(session);var values=(IEnumerable)attempts.GetType().GetProperty("Values").GetValue(attempts);object attempt=null;foreach(var value in values){attempt=value;break;}Assert.NotNull(attempt);
            var owner=((INavStorageFacade)attempt).StorageRoot;Assert.NotNull(owner);Assert.False(owner.IsImmutableCaptureRoot);Assert.Null(attempt.GetType().GetField("LegacyJournal",F).GetValue(attempt));Assert.Null(attempt.GetType().GetField("Projections",F).GetValue(attempt));
            Assert.AreSame(type.GetField("Work",F).GetValue(owner),attempt.GetType().GetField("Work",F).GetValue(attempt));Assert.AreSame(((NavOwnedText)type.GetField("Frontier",F).GetValue(owner)).Value,attempt.GetType().GetField("Frontier",F).GetValue(attempt));Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
            var prefix=Bytes();Pump(1);var suffix=Bytes();string nextFacts=HostFacts();owner.Retain();
            try{Restore(prefix);Assert.AreEqual(NavigationStorageOwnershipTests.Unique(owner),budget.PhysicalBytes);Assert.Greater(owner.References,0);CollectionAssert.AreEqual(prefix,Bytes());Pump(1);CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(nextFacts,HostFacts());}
            finally{owner.Release();}
            Assert.Zero(owner.References);Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [TestCase(false)][TestCase(true)]
        public void ActualBalanceRequeuesAssignedMailboxAndRemovesUnretainedAttemptBeforeColdCompletion(bool refuse)
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-8);Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Move,new[]{a,b},new NavPoint(-10,-10),origin:PlayableOrderOrigin.Human),null));
            kernel.PauseAtPhysicalBoundary=()=>session.HasReadyArrival;try{for(int i=0;i<20000&&!session.HasReadyArrival;i++)Pump(1);}finally{kernel.PauseAtPhysicalBoundary=null;}Assert.True(session.HasReadyArrival);session.PrepareDeliveryBarrier();Assert.AreEqual(2,session.Requests.Count);Assert.Zero(session.AllocationCount);Assert.Zero(ArrivalLeaseCount);Assert.Zero(session.RegisteredRouteCount);var old=new[]{session.PendingRequestFor(a),session.PendingRequestFor(b)};Assert.True(session.IsArrivalRequest(old[0]));Assert.True(session.IsArrivalRequest(old[1]));
            var tables=(NavigationSession.NavigationLiveArrivalTables)typeof(NavigationSession).GetField("liveArrivalTables",F).GetValue(session);Assert.AreEqual(1,tables.Count);Assert.Greater(tables.References.Count,0);long retirement=(long)typeof(NavigationSession).GetField("arrivalRetirementSequence",F).GetValue(session);var data=activeProfile.CopyData();data.revision++;data.tankSpeed*=1.1;var target=PlayableProfile.Create(data);var before=Bytes();var budget=session.PhysicalHostBudget;long charge=budget.PhysicalBytes;int denied=0,underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            if(refuse){try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){denied++;return false;}return true;};var failure=Assert.Throws<TargetInvocationException>(()=>Call("ApplyBalance",target));Assert.IsInstanceOf<InvalidOperationException>(failure.InnerException);}finally{budget.AllocationAdmission=null;}Assert.AreEqual(1,denied);Assert.Zero(session.OwnedRebindPublications);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());Restore(before);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(before,Bytes());budget=session.PhysicalHostBudget;gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);old=new[]{session.PendingRequestFor(a),session.PendingRequestFor(b)};}
            var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var frozenBytes=frozen.Encode();int assigned=0;Action<NavigationRequest> assignment=_=>{assigned++;Assert.False(Monitor.IsEntered(gate));};session.EndpointAssigned+=assignment;
            try{
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};Call("ApplyBalance",target);}finally{budget.AllocationAdmission=null;session.EndpointAssigned-=assignment;}activeProfile=target;
                Assert.AreEqual(1,session.OwnedRebindPublications);Assert.Zero(session.LastOwnedRebindPublicationAllocatedBytes);Assert.Zero(underGate);Assert.AreEqual(2,assigned);Assert.Zero(session.Requests.Count);Assert.Zero(session.Answers.Count);Assert.AreEqual(2,session.AllocationCount);Assert.Zero(ArrivalLeaseCount);Assert.Zero(((NavigationCommandSlots<int,NavigationRequest>)typeof(NavigationSession).GetField("pending",F).GetValue(session)).Count);Assert.Zero(((NavigationCommandSlots<long,NavigationArrivalReservation>)typeof(NavigationSession).GetField("arrivalLedger",F).GetValue(session)).Count);
                tables=(NavigationSession.NavigationLiveArrivalTables)typeof(NavigationSession).GetField("liveArrivalTables",F).GetValue(session);Assert.Zero(tables.Count);Assert.Zero(tables.References.Count);Assert.Zero(tables.Ids.Count);Assert.Zero(tables.References.Capacity);Assert.Zero(tables.Ids.Capacity);Assert.AreEqual(retirement+1,typeof(NavigationSession).GetField("arrivalRetirementSequence",F).GetValue(session));var allocating=(NavigationCommandSlots<int,NavigationRequest>)typeof(NavigationSession).GetField("allocating",F).GetValue(session);for(int i=0;i<old.Length;i++){var replacement=allocating[old[i].Entity];Assert.AreSame(replacement,session.PendingRequestFor(old[i].Entity));Assert.AreEqual(old[i].Order,replacement.Order);Assert.Greater(replacement.Request,old[i].Request);Assert.AreEqual(old[i].Goal,replacement.Goal);Assert.AreEqual(old[i].MemberIdentity.OrderRevision,replacement.MemberIdentity.OrderRevision);Assert.AreEqual(target.Revision,replacement.ProfileRevision);}CollectionAssert.AreEqual(frozenBytes,frozen.Encode());PhysicalShape(frozen);frozen.Dispose();frozen=null;
                var prefix=Bytes();void Finish(){for(int i=0;i<20000&&(session.PendingCount>0||ArrivalLeaseCount>0);i++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);Assert.Zero(ArrivalLeaseCount);Assert.Zero(session.RejectedResults);foreach(int id in new[]{a,b}){Assert.True(session.Crowd.TryGet(id,out var actor));Assert.True(actor.Moving);Assert.AreEqual(target.Revision,((NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session)).OwnerFor(id).Request.Request.ProfileRevision);}}
                Finish();var suffix=Bytes();var facts=HostFacts();Pump(1);var next=Bytes();var nextFacts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Finish();CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;if(session!=null)session.EndpointAssigned-=assignment;frozen?.Dispose();}
        }

        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)]
        public void ActualBalanceRetriesAssignedPartialProjectionWhileKeepingInstalledCohortAndColdCleanup(bool refuse,bool stopInstalled)
        {
            int neighbor=0;if(stopInstalled){neighbor=Spawn(-18,-20);Arrival(neighbor,new NavPoint(-10,-20));for(int i=0;i<20000&&session.PendingCount>0;i++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);}
            int baselineApplied=session.AppliedResults;int a=Spawn(-18,-14),b=Spawn(-18,-8);Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Move,new[]{a,b},new NavPoint(-10,-10),origin:PlayableOrderOrigin.Human),null));
            var data=activeProfile.CopyData();data.revision++;data.tankSpeed*=1.1;var target=PlayableProfile.Create(data);
            bool Partial(){var row=HostRow(b);return session.AppliedResults==baselineApplied+1&&row?.ProjectionOwner!=null&&row.ProjectionOwner.LegCursor>0&&row.Answer==null&&ArrivalLeaseCount>0;}
            bool callbackRefused=false;kernel.Continue=()=>session.Answers.Count==0;kernel.PauseAtPhysicalBoundary=()=>{if(!Partial())return false;Assert.True(kernel.PumpExecuting);var source=Bytes();var failure=Assert.Throws<TargetInvocationException>(()=>Call("ApplyBalance",target));Assert.IsInstanceOf<InvalidOperationException>(failure.InnerException);CollectionAssert.AreEqual(source,Bytes());callbackRefused=true;return true;};try{for(int i=0;i<20000&&!Partial();i++){Pump(1);session.ApplyResults();}}finally{kernel.Continue=null;kernel.PauseAtPhysicalBoundary=null;}Assert.True(callbackRefused);Assert.False(kernel.PumpExecuting);Assert.True(Partial(),"A installed and B retains a partially advanced projection");Assert.AreEqual(1,session.PendingCount);Assert.Zero(session.AllocationCount);var old=session.PendingRequestFor(b);Assert.NotNull(old);Assert.True(session.IsArrivalRequest(old));
            if(stopInstalled){Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Stop,new[]{a},origin:PlayableOrderOrigin.Human),null));Assert.True(session.Crowd.TryGet(a,out var stopped));Assert.False(stopped.Moving);}
            var before=Bytes();var budget=session.PhysicalHostBudget;long charge=budget.PhysicalBytes;int denied=0,underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            if(refuse){try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){denied++;return false;}return true;};var failure=Assert.Throws<TargetInvocationException>(()=>Call("ApplyBalance",target));Assert.IsInstanceOf<InvalidOperationException>(failure.InnerException);}finally{budget.AllocationAdmission=null;}Assert.AreEqual(1,denied);Assert.Zero(session.OwnedRebindPublications);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());Restore(before);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(before,Bytes());budget=session.PhysicalHostBudget;gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);old=session.PendingRequestFor(b);}
            var installed=(NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);var prior=installed.OwnerFor(stopInstalled?neighbor:a);Assert.NotNull(prior);Assert.True(session.Crowd.TryGet(stopInstalled?neighbor:a,out var actor));var route=actor.StorageOwner.Global;var position=actor.Position;var leases=(System.Collections.Generic.IDictionary<long,ArrivalProviderLease>)typeof(NavigationSession).GetField("arrivalProjectionLeases",F).GetValue(session);var lease=leases.Values.Single(v=>v.Entity==b);var leaseOwner=lease.StorageOwner;var arrivalTables=(NavigationSession.NavigationLiveArrivalTables)typeof(NavigationSession).GetField("liveArrivalTables",F).GetValue(session);var attempt=arrivalTables.Attempts[lease.Attempt];Assert.False(attempt.Retiring);long token=lease.Token;Assert.False(lease.Retiring);var ledger=(NavigationCommandSlots<long,NavigationArrivalReservation>)typeof(NavigationSession).GetField("arrivalLedger",F).GetValue(session);Assert.AreEqual(NavArrivalReservationKind.PendingRequest,ledger[old.Request].State);var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var frozenBytes=frozen.Encode();int assigned=0,requested=0;Action<NavigationRequest> assignment=_=>{assigned++;Assert.False(Monitor.IsEntered(gate));};Action<NavigationRequest> request=_=>requested++;session.EndpointAssigned+=assignment;session.RouteRequested+=request;
            try{
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};Call("ApplyBalance",target);}finally{budget.AllocationAdmission=null;session.EndpointAssigned-=assignment;session.RouteRequested-=request;}activeProfile=target;
                Assert.AreEqual(1,session.OwnedRebindPublications);Assert.Zero(session.LastOwnedRebindPublicationAllocatedBytes);Assert.Zero(underGate);Assert.AreEqual(1,assigned);Assert.Zero(requested);Assert.Zero(session.Requests.Count);Assert.Zero(session.Answers.Count);Assert.AreEqual(1,session.AllocationCount);Assert.False(((NavigationCommandSlots<int,NavigationRequest>)typeof(NavigationSession).GetField("pending",F).GetValue(session)).ContainsKey(b));var allocating=(NavigationCommandSlots<int,NavigationRequest>)typeof(NavigationSession).GetField("allocating",F).GetValue(session);var replacement=allocating[b];Assert.AreSame(replacement,session.PendingRequestFor(b));Assert.AreEqual(old.Order,replacement.Order);Assert.Greater(replacement.Request,old.Request);Assert.AreEqual(old.Goal,replacement.Goal);Assert.AreEqual(old.MemberIdentity.OrderRevision,replacement.MemberIdentity.OrderRevision);Assert.AreEqual(target.Revision,replacement.ProfileRevision);Assert.True(lease.Retiring);Assert.AreEqual(stopInstalled,attempt.Retiring);if(stopInstalled){Assert.Greater(attempt.Retirement,lease.Retirement);Assert.AreSame(attempt.StorageOwner.RemovedReason.Value,attempt.RetirementReason);}Assert.AreSame(leaseOwner,leases[token].StorageOwner);Assert.True(lease.Attached);Assert.AreSame(prior,installed.OwnerFor(stopInstalled?neighbor:a));Assert.True(installed.IsDetached(stopInstalled?neighbor:a));Assert.AreSame(route,actor.StorageOwner.Global);Assert.AreEqual(position,actor.Position);Assert.True(actor.Moving);var nextLedger=(NavigationCommandSlots<long,NavigationArrivalReservation>)typeof(NavigationSession).GetField("arrivalLedger",F).GetValue(session);Assert.False(nextLedger.ContainsKey(old.Request));Assert.AreEqual(NavArrivalReservationKind.InstalledExecution,nextLedger[prior.Request.Request.Request].State);CollectionAssert.AreEqual(frozenBytes,frozen.Encode());PhysicalShape(frozen);frozen.Dispose();frozen=null;
                var prefix=Bytes();void Finish(){for(int i=0;i<20000&&(session.PendingCount>0||ArrivalLeaseCount>0);i++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);Assert.Zero(ArrivalLeaseCount);Assert.True(session.Crowd.TryGet(b,out var unit));Assert.True(unit.Moving);Assert.AreEqual(target.Revision,((NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session)).OwnerFor(b).Request.Request.ProfileRevision);Assert.True(session.Crowd.TryGet(stopInstalled?neighbor:a,out var preserved));Assert.True(preserved.Moving);Assert.AreEqual(position,preserved.Position);}
                Finish();var suffix=Bytes();var facts=HostFacts();Pump(1);var next=Bytes();var nextFacts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Finish();CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;if(session!=null){session.EndpointAssigned-=assignment;session.RouteRequested-=request;}frozen?.Dispose();}
        }

        [TestCase(false,false,false)][TestCase(true,false,false)][TestCase(false,true,false)][TestCase(true,true,false)][TestCase(false,true,true)]
        public void ActualBalanceRebindDrainsProducerAndMixedOrdinaryTogetherThroughColdRallyRestart(bool barrier,bool refuse,bool duplicate)
        {
            Call("AddCredits",PlayableOwner.Player,10000d);Call("BuildAt",1,2,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null);Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            var buildings=(IDictionary)Domain.GetField("buildings",F).GetValue(domain);int factory=0;foreach(DictionaryEntry pair in buildings)if((PlayableBuildingKind)pair.Value.GetType().GetField("Kind").GetValue(pair.Value)==PlayableBuildingKind.Factory)factory=(int)pair.Key;Assert.Greater(factory,0);
            int moving=Spawn(-18,-14),a=Spawn(-18,-10),b=Spawn(-18,-6),queued=Spawn(-18,-2);Arrival(moving,new NavPoint(-10,-14));for(int i=0;i<20000&&(session.PendingCount>0||session.AllocationCount>0);i++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);Assert.Zero(session.AllocationCount);
            var goal=new NavPoint(-8,10);Assert.AreEqual(PlayableCommandStatus.Accepted,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.SetRally,new[]{factory},goal),null));Call("AdvanceRally");for(int i=0;i<20000&&session.OwnedProbeAnswerPublications==0;i++){Pump(4);session.ApplyResults();}Assert.AreEqual(1,session.OwnedProbeAnswerPublications);Assert.AreEqual(1,((NavigationLiveProbeAnswers)typeof(NavigationSession).GetField("liveProbeAnswers",F).GetValue(session)).Count);Assert.AreEqual(1,((IDictionary)Domain.GetField("rallyWork",F).GetValue(domain)).Count);
            Assert.True(session.MoveGroup(new[]{a,b},new[]{new NavPoint(-10,-10),new NavPoint(-10,-6)}));kernel.Continue=()=>session.Answers.Count==0;try{for(int i=0;i<20000&&session.Answers.Count==0;i++)Pump(4);}finally{kernel.Continue=null;}Assert.AreEqual(1,session.Answers.Count);Assert.AreEqual(1,session.RegisteredRouteCount);Assert.AreEqual(2,session.PendingCount);
            if(barrier){Assert.False(session.DeliveryBarrierReady);Assert.Zero(session.Answers.Count);Assert.AreEqual(1,((IList)typeof(NavigationSession).GetField("barrierAnswers",F).GetValue(session)).Count);}
            Assert.True(session.Move(queued,new NavPoint(-10,-2)));Assert.AreEqual(1,session.Requests.Count);var old=new[]{session.PendingRequestFor(a),session.PendingRequestFor(b),session.PendingRequestFor(queued)};Assert.AreEqual(old[0].MemberIdentity.GroupId,old[1].MemberIdentity.GroupId);Assert.AreNotEqual(old[0].MemberIdentity.GroupId,old[2].MemberIdentity.GroupId);
            if(duplicate){var producer=((NavigationLiveProbeAnswers)typeof(NavigationSession).GetField("liveProbeAnswers",F).GetValue(session)).AnswerAt(0);Assert.True(session.Answers.TryEnqueue(producer));Assert.AreEqual(2,producer.Request.StorageOwner.TransportAnswers);Assert.AreSame(producer,session.Answers.CopyItems().Last());}
            var data=activeProfile.CopyData();data.revision++;data.tankSpeed*=1.1;var target=PlayableProfile.Create(data);var before=Bytes();var budget=session.PhysicalHostBudget;long charge=budget.PhysicalBytes;int underGate=0,denied=0,notifications=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            if(refuse){try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){denied++;return false;}return true;};var failure=Assert.Throws<TargetInvocationException>(()=>Call("ApplyBalance",target));Assert.IsInstanceOf<InvalidOperationException>(failure.InnerException);}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied);Assert.Zero(session.OwnedRebindPublications);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());Restore(before);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(before,Bytes());budget=session.PhysicalHostBudget;gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            }
            var installed=(NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);var prior=installed.OwnerFor(moving);Assert.True(session.Crowd.TryGet(moving,out var actor));var route=actor.StorageOwner.Global;var position=actor.Position;var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var frozenBytes=frozen.Encode();Action<NavigationRequest> notified=_=>{notifications++;Assert.False(Monitor.IsEntered(gate));};session.RouteRequested+=notified;
            try{
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};Call("ApplyBalance",target);}finally{budget.AllocationAdmission=null;session.RouteRequested-=notified;}activeProfile=target;
                Assert.AreEqual(1,session.OwnedRebindPublications);Assert.Zero(session.LastOwnedRebindPublicationAllocatedBytes);Assert.Zero(underGate);Assert.AreEqual(3,notifications);Assert.AreEqual(3,session.Requests.Count);Assert.Zero(session.Answers.Count);Assert.AreEqual(1,session.RegisteredRouteCount);Assert.AreEqual(3,session.PendingCount);Assert.AreEqual(0,((IList)typeof(NavigationSession).GetField("barrierAnswers",F).GetValue(session)).Count);Assert.AreEqual(0,((NavigationLiveProbeAnswers)typeof(NavigationSession).GetField("liveProbeAnswers",F).GetValue(session)).Count);Assert.True((bool)Domain.GetField("rallyPaused",F).GetValue(domain));
                var replacements=session.Requests.CopyItems();for(int i=0;i<old.Length;i++){Assert.AreEqual(old[i].Entity,replacements[i].Entity);Assert.AreEqual(old[i].Order+1,replacements[i].Order);Assert.AreEqual(old[i].MemberIdentity.OrderRevision,replacements[i].MemberIdentity.OrderRevision);Assert.AreEqual(target.Revision,replacements[i].ProfileRevision);if(i>0)Assert.AreEqual(replacements[i-1].Request+1,replacements[i].Request);}
                Assert.AreSame(prior,installed.OwnerFor(moving));Assert.True(installed.IsDetached(moving));Assert.AreSame(route,actor.StorageOwner.Global);Assert.AreEqual(position,actor.Position);Assert.True(actor.Moving);CollectionAssert.AreEqual(frozenBytes,frozen.Encode());PhysicalShape(frozen);frozen.Dispose();frozen=null;
                var prefix=Bytes();void Finish(){Call("SetRallyPaused",false);for(int i=0;i<20000&&(session.PendingCount>0||((IDictionary)Domain.GetField("rallyWork",F).GetValue(domain)).Count>0);i++){Pump(4);session.ApplyResults();Call("AdvanceRally");}Assert.Zero(session.PendingCount);Assert.Zero(((IDictionary)Domain.GetField("rallyWork",F).GetValue(domain)).Count);Assert.AreEqual(1,session.RejectedResults);foreach(int id in new[]{a,b,queued}){Assert.True(session.Crowd.TryGet(id,out var unit));Assert.True(unit.Moving);}var currentBuildings=(IDictionary)Domain.GetField("buildings",F).GetValue(domain);var building=currentBuildings[factory];Assert.True((bool)building.GetType().GetField("HasRally").GetValue(building));Assert.AreEqual(goal,building.GetType().GetField("Rally").GetValue(building));}
                Finish();var suffix=Bytes();var facts=HostFacts();Pump(1);var next=Bytes();var nextFacts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Finish();CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;if(session!=null)session.RouteRequested-=notified;frozen?.Dispose();}
        }

        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)]
        public void ActualBalanceWithoutPendingKeepsInstalledMovementAndWholeDomainRefusalThroughColdPump(bool moving,bool refuse)
        {
            int entity=Spawn(-18,-14);
            if(moving){Arrival(entity,new NavPoint(-10,-14));for(int i=0;i<20000&&(session.PendingCount>0||session.AllocationCount>0);i++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);Assert.Zero(session.AllocationCount);}
            Assert.True(session.Crowd.TryGet(entity,out var actor));Assert.AreEqual(moving,actor.Moving);var route=actor.StorageOwner.Global;var position=actor.Position;var installed=(NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);var owner=installed.OwnerFor(entity);Assert.AreEqual(moving,owner!=null);
            var data=activeProfile.CopyData();data.revision++;data.tankSpeed*=1.1;var target=PlayableProfile.Create(data);var budget=session.PhysicalHostBudget;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);int underGate=0,denied=0;var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var before=frozen.Encode();long charge=budget.PhysicalBytes;
            try{
                if(refuse){try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){denied++;return false;}return true;};var failure=Assert.Throws<TargetInvocationException>(()=>Call("ApplyBalance",target));Assert.IsInstanceOf<InvalidOperationException>(failure.InnerException);}finally{budget.AllocationAdmission=null;}
                    Assert.AreEqual(1,denied);Assert.Zero(session.OwnedRebindPublications);Assert.AreEqual(activeProfile.Revision,Domain.GetProperty("CurrentBalanceRevision",F).GetValue(domain));Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());CollectionAssert.AreEqual(before,frozen.Encode());
                }
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};Call("ApplyBalance",target);}finally{budget.AllocationAdmission=null;}activeProfile=target;
                Assert.AreEqual(1,session.OwnedRebindPublications);Assert.Zero(session.LastOwnedRebindPublicationAllocatedBytes);Assert.Zero(underGate);Assert.AreEqual(target.Revision,session.Admission.Profile.Revision);Assert.AreEqual(target.Revision,Domain.GetProperty("CurrentBalanceRevision",F).GetValue(domain));Assert.AreEqual(target.Navigation.TankSpeed,actor.Speed);Assert.AreEqual(position,actor.Position);Assert.AreEqual(moving,actor.Moving);Assert.AreSame(route,actor.StorageOwner.Global);Assert.AreSame(owner,installed.OwnerFor(entity));if(moving)Assert.True(installed.IsDetached(entity));Assert.Zero(session.PendingCount);Assert.Zero(session.AllocationCount);Assert.Zero(session.Requests.Count);Assert.Zero(session.Answers.Count);CollectionAssert.AreEqual(before,frozen.Encode());PhysicalShape(frozen);frozen.Dispose();frozen=null;
                var prefix=Bytes();Pump(1);var next=Bytes();var facts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Assert.True(session.Crowd.TryGet(entity,out var cold));Assert.AreEqual(moving,cold.Moving);Assert.AreEqual(position,cold.Position);Assert.AreEqual(target.Navigation.TankSpeed,cold.Speed);Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(facts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;frozen?.Dispose();}
        }

        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)] public void ActualBalanceRefusalKeepsWholeDomainBytesAndColdRetryConsistent(bool overflow,bool unread)
        {
            int entity=Spawn(-18,-14);Assert.True(session.Move(entity,new NavPoint(-10,-14)));kernel.Continue=()=>session.RegisteredRouteCount<1;try{for(int i=0;i<20000&&session.RegisteredRouteCount<1;i++)Pump(4);}finally{kernel.Continue=null;}Assert.AreEqual(1,session.RegisteredRouteCount);Assert.Zero(session.Answers.Count);Assert.Zero(session.Requests.Count);if(unread){for(int i=0;i<20000&&session.Answers.Count==0;i++)Pump(4);Assert.AreEqual(1,session.Answers.Count);Assert.Zero(session.RegisteredRouteCount);}
            var research=(IDictionary)Domain.GetField("research",F).GetValue(domain);int researchCount=research.Count;
            var oldProfile=activeProfile;var data=oldProfile.CopyData();data.revision++;data.tankSpeed*=1.1;var target=PlayableProfile.Create(data);var before=Bytes();var budget=session.PhysicalHostBudget;long charge=budget.PhysicalBytes;int denied=0,underGate=0,notifications=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);Action<NavigationRequest> requested=_=>notifications++;session.RouteRequested+=requested;
            var operationField=typeof(NavigationSession).GetField("authorityOperationIdentity",F);long operation=(long)operationField.GetValue(session);if(overflow)operationField.SetValue(session,long.MaxValue-1);
            try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){denied++;return false;}return true;};var failure=Assert.Throws<TargetInvocationException>(()=>Call("ApplyBalance",target));Assert.IsInstanceOf<InvalidOperationException>(failure.InnerException);if(overflow)Assert.AreEqual(long.MaxValue-1,operationField.GetValue(session));}finally{budget.AllocationAdmission=null;session.RouteRequested-=requested;if(overflow)operationField.SetValue(session,operation);}
            Assert.AreEqual(researchCount,research.Count,"preparation preserves research ownership");Assert.AreEqual(overflow?0:1,denied);Assert.Zero(underGate);Assert.Zero(notifications);Assert.Zero(session.OwnedRebindPublications);Assert.AreEqual(oldProfile.Revision,Domain.GetProperty("CurrentBalanceRevision",F).GetValue(domain));Assert.AreEqual(oldProfile.Revision,session.Admission.Profile.Revision);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());
            Restore(before);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(before,Bytes());Call("ApplyBalance",target);activeProfile=target;Assert.AreEqual(1,session.OwnedRebindPublications);Assert.Zero(session.LastOwnedRebindPublicationAllocatedBytes);Assert.AreEqual(target.Revision,Domain.GetProperty("CurrentBalanceRevision",F).GetValue(domain));Assert.AreEqual(target.Revision,session.Admission.Profile.Revision);
            var prefix=Bytes();void Finish(){for(int i=0;i<20000&&session.PendingCount>0;i++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);Assert.AreEqual(unread?0:1,session.RejectedResults);Assert.True(session.Crowd.TryGet(entity,out var unit));Assert.True(unit.Moving);}
            Finish();var suffix=Bytes();var facts=HostFacts();Pump(1);var next=Bytes();var nextFacts=HostFacts();Restore(prefix);CollectionAssert.AreEqual(prefix,Bytes());Finish();CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [Test] public void BalancePreparationRejectsChangedDomainUpgradeSourceBeforePublication()
        {
            int entity=Spawn(-18,-14);Assert.True(session.Move(entity,new NavPoint(-10,-14)));kernel.Continue=()=>session.RegisteredRouteCount<1;try{for(int i=0;i<20000&&session.RegisteredRouteCount<1;i++)Pump(4);}finally{kernel.Continue=null;}Assert.AreEqual(1,session.RegisteredRouteCount);
            // Exercise the read-only helper with an absent optional research
            // entry, then restore the actual game's canonical source directory
            // before the live publication/save scenario.
            var research=(IDictionary)Domain.GetField("research",F).GetValue(domain);var playerRows=research[PlayableOwner.Player];Assert.IsEmpty((IEnumerable)playerRows);research.Remove(PlayableOwner.Player);
            try{var actual=(PlayableDomain)domain;Assert.True(actual.TryBalanceMobility(entity,activeProfile,out var source,out _,out _));Assert.False(research.Contains(PlayableOwner.Player));Assert.True(actual.BalanceMobilityCurrent(entity,source));Assert.False(research.Contains(PlayableOwner.Player));}finally{research[PlayableOwner.Player]=playerRows;}
            var before=Bytes();var pending=session.PendingRequestFor(entity);var budget=session.PhysicalHostBudget;long charge=budget.PhysicalBytes;var units=(IDictionary)Domain.GetField("units",F).GetValue(domain);var unit=units[entity];var authored=unit.GetType().GetField("AuthoredUpgrade");Assert.Null(authored.GetValue(unit));var data=activeProfile.CopyData();data.revision++;data.tankSpeed*=1.1;var target=PlayableProfile.Create(data);int changed=0;
            try{budget.AllocationAdmission=_=>{if(changed==0&&new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){changed++;authored.SetValue(unit,(bool?)true);}return true;};var failure=Assert.Throws<TargetInvocationException>(()=>Call("ApplyBalance",target));Assert.IsInstanceOf<InvalidOperationException>(failure.InnerException);Assert.AreEqual(true,authored.GetValue(unit),"newer injected source is preserved");Assert.AreEqual(activeProfile.Revision,Domain.GetProperty("CurrentBalanceRevision",F).GetValue(domain));Assert.AreSame(pending,session.PendingRequestFor(entity));Assert.Zero(session.OwnedRebindPublications);}
            finally{budget.AllocationAdmission=null;authored.SetValue(unit,null);}
            Assert.AreEqual(1,changed);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
        }

        [TestCase(false)][TestCase(true)] public void ActualBalanceRebindReissuesRegisteredOrdinaryAndKeepsInstalledNeighborThroughColdCompletion(bool unread)
        {
            int moving=Spawn(-18,-14),waiting=Spawn(-18,-10);Arrival(moving,new NavPoint(-10,-14));for(int i=0;i<20000&&(session.PendingCount>0||session.AllocationCount>0);i++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);Assert.Zero(session.AllocationCount);
            var installed=(NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);var prior=installed.OwnerFor(moving);Assert.NotNull(prior);Assert.True(session.Crowd.TryGet(moving,out var actor));var position=actor.Position;var route=actor.StorageOwner.Global;Assert.True(actor.Moving);
            Assert.True(session.Move(waiting,new NavPoint(-10,-10)));var binding=Binding();Assert.True(kernel.TryRegisterPhysicalOrdinary(binding));var old=session.PendingRequestFor(waiting);Assert.NotNull(old);Assert.Zero(session.Answers.Count);Assert.Zero(session.Requests.Count);Assert.AreEqual(1,session.RegisteredRouteCount);if(unread){for(int i=0;i<20000&&session.Answers.Count==0;i++)Pump(4);Assert.AreEqual(1,session.Answers.Count);Assert.Zero(session.RegisteredRouteCount);}
            var budget=session.PhysicalHostBudget;var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var before=frozen.Encode();var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);int underGate=0,requested=0;Action<NavigationRequest> notified=request=>{requested++;Assert.False(Monitor.IsEntered(gate));};session.RouteRequested+=notified;
            try{
                var data=activeProfile.CopyData();data.revision++;data.tankSpeed*=1.1;activeProfile=PlayableProfile.Create(data);
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};Call("ApplyBalance",activeProfile);}finally{budget.AllocationAdmission=null;session.RouteRequested-=notified;}
                Assert.AreEqual(1,session.OwnedRebindPublications);Assert.Zero(session.LastOwnedRebindPublicationAllocatedBytes);Assert.Zero(underGate);Assert.AreEqual(1,requested);var replacement=session.PendingRequestFor(waiting);Assert.AreNotSame(old,replacement);Assert.Greater(replacement.Request,old.Request);Assert.AreEqual(old.Order+1,replacement.Order);Assert.AreEqual(activeProfile.Revision,replacement.ProfileRevision);Assert.AreEqual(old.MemberIdentity.GroupId,replacement.MemberIdentity.GroupId);Assert.AreEqual(old.MemberIdentity.OrderRevision,replacement.MemberIdentity.OrderRevision);Assert.AreEqual(1,session.Requests.Count);Assert.AreEqual(unread?0:1,session.RegisteredRouteCount);Assert.Zero(session.Answers.Count);
                Assert.AreSame(prior,installed.OwnerFor(moving));Assert.True(installed.IsDetached(moving));Assert.AreSame(route,actor.StorageOwner.Global);Assert.AreEqual(position,actor.Position);Assert.True(actor.Moving);CollectionAssert.AreEqual(before,frozen.Encode());PhysicalShape(frozen);frozen.Dispose();frozen=null;
                var prefix=Bytes();void Finish(){for(int i=0;i<20000&&session.PendingCount>0;i++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);Assert.AreEqual(unread?0:1,session.RejectedResults);Assert.True(session.Crowd.TryGet(waiting,out var unit));Assert.True(unit.Moving);Assert.AreEqual(activeProfile.Revision,((NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session)).OwnerFor(waiting).Request.Request.ProfileRevision);}
                Finish();var suffix=Bytes();var facts=HostFacts();Pump(1);var next=Bytes();var nextFacts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Finish();CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;if(session!=null)session.RouteRequested-=notified;frozen?.Dispose();}
        }

        [Test] public void ActualBalanceRebindOwnsAllocatingRetryAndPreservesDetachedInstalledMotionThroughColdPump()
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-10);Arrival(a,new NavPoint(-10,-14));for(int n=0;n<20000&&session.PendingCount>0;n++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);
            var installed=(NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);var old=installed.OwnerFor(a);Assert.NotNull(old);Assert.True(session.Crowd.TryGet(a,out var actor));var route=actor.StorageOwner.Global;var position=actor.Position;Assert.True(actor.Moving);
            Arrival(b,new NavPoint(-10,-10));kernel.PauseAtPhysicalBoundary=()=>session.HasReadyArrival;try{for(int n=0;n<20000&&!session.HasReadyArrival;n++)Pump(64);}finally{kernel.PauseAtPhysicalBoundary=null;}Assert.True(session.HasReadyArrival);var budget=session.PhysicalHostBudget;var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var before=frozen.Encode();
            try{
                var data=activeProfile.CopyData();data.revision++;data.tankSpeed*=1.1;activeProfile=PlayableProfile.Create(data);Call("ApplyBalance",activeProfile);
                Assert.AreEqual(1,session.OwnedRebindPublications);Assert.Zero(session.LastOwnedRebindPublicationAllocatedBytes);Assert.AreEqual(activeProfile.Navigation.Revision,session.Admission.Profile.Revision);Assert.AreEqual(activeProfile.Navigation.TankSpeed,session.Admission.Profile.TankSpeed);Assert.AreSame(old,installed.OwnerFor(a));Assert.True(installed.IsDetached(a));Assert.AreSame(route,actor.StorageOwner.Global);Assert.AreEqual(position,actor.Position);Assert.True(actor.Moving);Assert.AreEqual(1,session.AllocationCount);Assert.Zero(session.Requests.Count);var allocating=(NavigationCommandSlots<int,NavigationRequest>)typeof(NavigationSession).GetField("allocating",F).GetValue(session);Assert.AreEqual(activeProfile.Navigation.Revision,allocating[b].ProfileRevision);Assert.NotNull(allocating[b].StorageOwner);CollectionAssert.AreEqual(before,frozen.Encode());PhysicalShape(frozen);frozen.Dispose();frozen=null;
                var prefix=Bytes();Pump(1);var next=Bytes();string facts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(facts,HostFacts());Assert.True(session.Crowd.TryGet(a,out var cold));Assert.True(cold.Moving);Assert.AreEqual(position,cold.Position);Assert.True(((NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session)).IsDetached(a));Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{frozen?.Dispose();}
        }
        [TestCase("core")] [TestCase("request")] [TestCase("admission")]
        public void OwnedAllocatingRebindRefusesAtomicallyAndRestoresExactNextPump(string site)
        {
            int entity=Spawn(-18,-14);Arrival(entity,new NavPoint(-10,-14));kernel.PauseAtPhysicalBoundary=()=>session.HasReadyArrival;
            try{for(int n=0;n<20000&&!session.HasReadyArrival;n++)Pump(64);}finally{kernel.PauseAtPhysicalBoundary=null;}Assert.True(session.HasReadyArrival);Assert.AreEqual(1,session.AllocationCount);Assert.AreEqual(1,session.PendingCount);Assert.Zero(session.Requests.Count);
            var budget=session.PhysicalHostBudget;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var before=frozen.Encode();long charge=budget.PhysicalBytes;int notifications=0,underGate=0,denied=0;
            Action<NavigationRequest> assigned=request=>{notifications++;Assert.False(Monitor.IsEntered(gate));};session.EndpointAssigned+=assigned;
            try{
                Type owner=site=="core"?typeof(NavigationAuthorityOwners):site=="request"?typeof(NavigationHostRequest):typeof(NavigationAdmissionStorage);
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().DeclaringType==owner)){denied++;return false;}return true;};Assert.Throws<InvalidOperationException>(()=>session.Rebind(activeProfile.Navigation));}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.Zero(notifications);Assert.Zero(session.OwnedRebindPublications);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());CollectionAssert.AreEqual(before,frozen.Encode());
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};session.Rebind(activeProfile.Navigation);}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,session.OwnedRebindPublications);Assert.Zero(session.LastOwnedRebindPublicationAllocatedBytes);Assert.Zero(underGate);Assert.AreEqual(1,notifications);Assert.Zero(session.Requests.Count);Assert.AreEqual(1,session.AllocationCount);var allocating=(NavigationCommandSlots<int,NavigationRequest>)typeof(NavigationSession).GetField("allocating",F).GetValue(session);Assert.NotNull(allocating[entity].StorageOwner);Assert.AreSame(allocating.OwnerFor(entity),allocating[entity].StorageOwner);CollectionAssert.AreEqual(before,frozen.Encode());PhysicalShape(frozen);frozen.Dispose();frozen=null;
                var prefix=Bytes();Pump(1);var next=Bytes();string facts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(facts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;if(session!=null)session.EndpointAssigned-=assigned;frozen?.Dispose();}
        }
        [TestCase(false,false,false)][TestCase(false,true,true)][TestCase(true,false,false)] public void ActualOrdinaryTerminalAnswerRejectsIdleActorAndRetainsHistoricalOwnerCold(bool step,bool refuse,bool historical)
        {
            int entity=Spawn(historical?-20:-18,-14);var installed=(NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);
            if(historical){Assert.True(session.Move(entity,new NavPoint(-18,-14)));for(int tick=0;tick<300;tick++){Pump(64);Call("Step",1d/30);if(session.Crowd.TryGet(entity,out var arrived)&&arrived.Outcome==NavigationOutcome.Arrived&&!((PlayableDomain)domain).ObserveNavigationRetention(entity).Keep)break;}Assert.True(session.Crowd.TryGet(entity,out var completed));Assert.AreEqual(NavigationOutcome.Arrived,completed.Outcome);Assert.False(((PlayableDomain)domain).ObserveNavigationRetention(entity).Keep);}
            Assert.True(session.Crowd.TryGet(entity,out var actor));Assert.False(actor.Moving);var prior=installed.OwnerFor(entity);Assert.AreEqual(historical,prior!=null);var origin=actor.Position;var holders=new[]{Spawn(origin.X+1.5,origin.Z),Spawn(origin.X-1.5,origin.Z),Spawn(origin.X,origin.Z+1.5),Spawn(origin.X,origin.Z-1.5)};
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Hold,holders,origin:PlayableOrderOrigin.Human),null));Assert.True(session.Move(entity,new NavPoint(-10,-14)));var request=session.PendingRequestFor(entity);Assert.True(request.Geometry.IsFree(request.Start,request.Profile.Radius));Assert.Zero(session.AllocationCount);
            for(int tick=0;tick<20000&&session.Answers.Count==0;tick++)Pump(64);Assert.AreEqual(1,session.Answers.Count);var answer=session.Answers.CopyItems().Single();Assert.AreEqual(request.Request,answer.Request.Request);Assert.AreEqual(request.Order,answer.Request.Order);Assert.AreEqual(request.GoalLocation,answer.Request.GoalLocation);request=answer.Request;Assert.AreSame(request,session.PendingRequestFor(entity));TestContext.WriteLine("ordinary terminal physical="+session.PhysicalHostBudget.PhysicalBytes+" peak="+session.PhysicalHostBudget.PeakRetainedBytes);Assert.AreNotEqual(NavSolveStatus.Ready,answer.Status);Assert.Null(answer.Corridor);Assert.NotNull(answer.StorageOwner);var outcome=answer.Status==NavSolveStatus.UnreachableInGraph?NavigationOutcome.Unreachable:NavigationOutcome.Rejected;long mobility=actor.MobilityRevision;int applied=session.AppliedResults,rejected=session.RejectedResults,unreachable=session.UnreachableResults;
            var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);var events=new System.Collections.Generic.List<string>();Action<NavigationRequest,NavigationOutcome> Failed=(r,o)=>{Assert.False(Monitor.IsEntered(gate));Assert.AreSame(request,r);Assert.AreEqual(outcome,o);events.Add("failure");};Action<int> Cancelled=id=>{Assert.False(Monitor.IsEntered(gate));Assert.AreEqual(entity,id);events.Add("cancel");};session.RouteFailed+=Failed;session.MovementCancelled+=Cancelled;
            var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var before=frozen.Encode();var budget=session.PhysicalHostBudget;long charge=budget.PhysicalBytes;int denied=0,underGate=0;
            try{
                if(refuse){try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){denied++;return false;}return true;};session.ApplyResults();}finally{budget.AllocationAdmission=null;}Assert.AreEqual(1,denied);Assert.IsEmpty(events);Assert.Zero(session.OwnedOrdinaryFailurePublications);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());}
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};if(step)session.Step(1d/30);else session.ApplyResults();}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,session.OwnedOrdinaryFailurePublications);Assert.Zero(session.LastOwnedOrdinaryFailurePublicationAllocatedBytes);if(!step)Assert.Zero(underGate);CollectionAssert.AreEqual(new[]{"failure","cancel"},events);Assert.Null(session.PendingRequestFor(entity));Assert.Zero(session.Answers.Count);Assert.AreEqual(applied,session.AppliedResults);Assert.AreEqual(rejected,session.RejectedResults);Assert.AreEqual(unreachable+(outcome==NavigationOutcome.Unreachable?1:0),session.UnreachableResults);Assert.AreEqual(mobility+1,actor.MobilityRevision);Assert.False(actor.Moving);Assert.False(actor.Held);Assert.AreEqual(outcome,actor.Outcome);Assert.AreEqual(outcome,actor.LastFailure);Assert.AreEqual(request.Request,actor.FailedRequest);Assert.AreEqual(request.Order,actor.FailedOrder);Assert.AreEqual(request.GoalLocation,actor.FailedGoal);Assert.AreSame(prior,installed.OwnerFor(entity));if(historical){Assert.True(installed.IsDetached(entity));Assert.AreNotSame(prior.Route,actor.StorageOwner.Global);}
                var failures=(NavigationCommandSlots<int,NavSolveStatus>)typeof(NavigationSession).GetField("technicalFailures",F).GetValue(session);bool technical=answer.Status==NavSolveStatus.InvalidEndpoint||answer.Status==NavSolveStatus.BlockedConnector||answer.Status==NavSolveStatus.CapacityExceeded;Assert.AreEqual(technical,failures.ContainsKey(entity));if(technical)Assert.AreEqual(answer.Status,failures[entity]);CollectionAssert.AreEqual(before,frozen.Encode());PhysicalShape(frozen);frozen.Dispose();frozen=null;session.RouteFailed-=Failed;session.MovementCancelled-=Cancelled;
                var prefix=Bytes();Pump(1);var suffix=Bytes();var facts=HostFacts();Pump(1);var next=Bytes();var nextFacts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Pump(1);CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.True(session.Crowd.TryGet(entity,out var cold));Assert.AreEqual(outcome,cold.LastFailure);Assert.AreEqual(historical,((NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session)).OwnerFor(entity)!=null);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;if(session!=null){session.RouteFailed-=Failed;session.MovementCancelled-=Cancelled;}frozen?.Dispose();}
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(true,true)] public void ActualEnclosedOriginPublishesTerminalMemberWithOptionalReadySiblingCold(bool mixed,bool refuse)
        {
            int blocked=Spawn(-18,-14);var holders=new[]{Spawn(-16.5,-14),Spawn(-19.5,-14),Spawn(-18,-12.5),Spawn(-18,-15.5)};int free=mixed?Spawn(-18,-6):0;int[] movers=mixed?new[]{blocked,free}:new[]{blocked};
            void Human(PlayableCommandKind kind,int[] ids,NavPoint goal=default)=>Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",kind,ids,goal,origin:PlayableOrderOrigin.Human),null));
            Human(PlayableCommandKind.Hold,holders);Human(PlayableCommandKind.Move,movers,new NavPoint(-10,-10));var old=session.PendingRequestFor(blocked);Assert.True(old.Geometry.IsFree(old.Start,old.Profile.Radius),"origin free inside valid held ring");
            kernel.PauseAtPhysicalBoundary=()=>session.HasReadyArrival;try{for(int i=0;i<20000&&!session.HasReadyArrival;i++)Pump(64);}finally{kernel.PauseAtPhysicalBoundary=null;}
            Assert.True(session.HasReadyArrival);var tables=(NavigationSession.NavigationLiveArrivalTables)typeof(NavigationSession).GetField("liveArrivalTables",F).GetValue(session);var attempt=tables.Attempts[tables.SortedId(0)];Assert.True(attempt.Work.MemberOutcomes.TryGetValue(blocked,out var outcome),"actual solver produced terminal blocked member");Assert.AreEqual(mixed?1:0,attempt.Work.Proposals.Count);Assert.False(attempt.Work.Proposals.Any(p=>p.Member.EntityId==blocked));Assert.Zero(session.Requests.Count);
            var sourceAllocating=(NavigationCommandSlots<int,NavigationRequest>)typeof(NavigationSession).GetField("allocating",F).GetValue(session);var sourceLayout=sourceAllocating.CaptureLayout();int blockedSlot=-1,freeSlot=-1;for(int slot=0;slot<sourceAllocating.Capacity;slot++)if(sourceAllocating.TryReadSlot(slot,out int entity,out _)){if(entity==blocked)blockedSlot=slot;else if(entity==free)freeSlot=slot;}Assert.GreaterOrEqual(blockedSlot,0);if(mixed)Assert.GreaterOrEqual(freeSlot,0);
            var expected=outcome==NavArrivalStatus.UnreachableInGraph?NavigationOutcome.Unreachable:NavigationOutcome.Rejected;int unreachable=session.UnreachableResults;var events=new System.Collections.Generic.List<string>();var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            Action<NavigationRequest,NavigationOutcome> Failed=(r,o)=>{Assert.False(Monitor.IsEntered(gate));Assert.AreEqual(blocked,r.Entity);Assert.AreEqual(expected,o);events.Add("failure");};Action<int> Cancelled=id=>{Assert.False(Monitor.IsEntered(gate));Assert.AreEqual(blocked,id);events.Add("cancel");};session.RouteFailed+=Failed;session.MovementCancelled+=Cancelled;
            var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var before=frozen.Encode();var budget=session.PhysicalHostBudget;long charge=budget.PhysicalBytes;int denied=0,underGate=0;
            try{
                if(refuse){try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){denied++;return false;}return true;};Assert.AreEqual(false,typeof(NavigationSession).GetMethod("TryRetryOwnedArrival",F).Invoke(session,new object[]{attempt}));}finally{budget.AllocationAdmission=null;}Assert.AreEqual(1,denied);Assert.IsEmpty(events);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());}
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};session.PrepareDeliveryBarrier();}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,session.OwnedArrivalRetryPublications);Assert.Zero(session.LastOwnedArrivalRetryPublicationAllocatedBytes);Assert.Zero(underGate);CollectionAssert.AreEqual(new[]{"failure","cancel"},events);Assert.Null(session.PendingRequestFor(blocked));Assert.AreEqual(mixed?1:0,session.PendingCount);Assert.Zero(session.AllocationCount);Assert.AreEqual(mixed?1:0,session.Requests.Count);Assert.AreEqual(unreachable+(outcome==NavArrivalStatus.UnreachableInGraph?1:0),session.UnreachableResults);Assert.True(session.Crowd.TryGet(blocked,out var actor));Assert.AreEqual(old.Request,actor.FailedRequest);Assert.AreEqual(old.Order,actor.FailedOrder);Assert.AreEqual(old.MemberIdentity.GroupId,actor.FailedGroupId);Assert.AreEqual(old.MemberIdentity.OrderRevision,actor.FailedRevision);Assert.AreEqual(old.GoalLocation,actor.FailedGoal);Assert.AreEqual(expected,actor.LastFailure);Assert.False(actor.Moving);Assert.True(attempt.Published);Assert.AreEqual(!mixed,attempt.Retiring);var nextAllocating=(NavigationCommandSlots<int,NavigationRequest>)typeof(NavigationSession).GetField("allocating",F).GetValue(session);CollectionAssert.AreEqual(new[]{sourceLayout[0]}.Concat(mixed?new[]{blockedSlot,freeSlot}:new[]{blockedSlot}).Concat(sourceLayout.Skip(1)).ToArray(),nextAllocating.CaptureLayout(),"legacy proposal removals precede terminal removals in free LIFO");
                CollectionAssert.AreEqual(before,frozen.Encode());PhysicalShape(frozen);frozen.Dispose();frozen=null;session.RouteFailed-=Failed;session.MovementCancelled-=Cancelled;
                var prefix=Bytes();void Finish(){if(!mixed)Pump(1);for(int tick=0;tick<20000&&session.PendingCount>0;tick++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);Assert.True(session.Crowd.TryGet(blocked,out var failed));Assert.AreEqual(expected,failed.LastFailure);if(mixed){Assert.True(session.Crowd.TryGet(free,out var moving));Assert.True(moving.Moving);}}
                Finish();var suffix=Bytes();var facts=HostFacts();Pump(1);var next=Bytes();var nextFacts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Finish();CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;session.RouteFailed-=Failed;session.MovementCancelled-=Cancelled;frozen?.Dispose();}
        }
        [TestCase(false)][TestCase(true)] public void ActualLateOccupantPublishesFreeEndpointAndRetriesBlockedMemberTogetherCold(bool refuse)
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-8);int[] movers={a,b};
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",PlayableCommandKind.Move,movers,new NavPoint(-10,-10),origin:PlayableOrderOrigin.Human),null));
            kernel.PauseAtPhysicalBoundary=()=>session.HasReadyArrival;try{for(int i=0;i<20000&&!session.HasReadyArrival;i++)Pump(1);}finally{kernel.PauseAtPhysicalBoundary=null;}
            Assert.True(session.HasReadyArrival);Assert.Zero(session.Requests.Count);
            var tables=(NavigationSession.NavigationLiveArrivalTables)typeof(NavigationSession).GetField("liveArrivalTables",F).GetValue(session);var attempt=tables.Attempts[tables.SortedId(0)];Assert.AreEqual(2,attempt.Work.Proposals.Count);
            var proposals=attempt.Work.Proposals.OrderBy(p=>p.Member.RequestSequence).ToArray();var endpoint=proposals[1].Endpoint;int blocked=(int)proposals[1].Member.EntityId,free=(int)proposals[0].Member.EntityId;var oldBlocked=session.PendingRequestFor(blocked);var oldFree=session.PendingRequestFor(free);int occupant=Spawn(endpoint.Position.X,endpoint.Position.Z);
            Assert.True(session.Crowd.TryGet(occupant,out var blocker));var overlap=typeof(NavigationSession).GetMethod("ArrivalOverlap",F);Assert.True((bool)overlap.Invoke(session,new object[]{proposals[1].Endpoint,proposals[1].Member.Radius,blocker.Location,blocker.Radius}),"chosen blocked endpoint");Assert.False((bool)overlap.Invoke(session,new object[]{proposals[0].Endpoint,proposals[0].Member.Radius,blocker.Location,blocker.Radius}),"other endpoint remains free");
            var occupancy=session.CaptureArrivalOccupancySnapshot(attempt.Input.Members);Assert.True(occupancy.Occupants.Any(o=>o.ActorId==occupant&&o.BlocksPlacement));
            var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var before=frozen.Encode();var budget=session.PhysicalHostBudget;long charge=budget.PhysicalBytes;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);int denied=0,underGate=0,assigned=0,requested=0;
            Action<NavigationRequest> Assigned=r=>{assigned++;Assert.False(Monitor.IsEntered(gate));};Action<NavigationRequest> Requested=r=>{requested++;Assert.False(Monitor.IsEntered(gate));};session.EndpointAssigned+=Assigned;session.RouteRequested+=Requested;
            try{
                if(refuse){try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){denied++;return false;}return true;};Assert.AreEqual(false,typeof(NavigationSession).GetMethod("TryRetryOwnedArrival",F).Invoke(session,new object[]{attempt}));}finally{budget.AllocationAdmission=null;}
                    Assert.AreEqual(1,denied);Assert.Zero(assigned);Assert.Zero(requested);Assert.Zero(session.OwnedArrivalRetryPublications);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());
                }
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};session.PrepareDeliveryBarrier();}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,session.OwnedArrivalRetryPublications);Assert.Zero(session.LastOwnedArrivalRetryPublicationAllocatedBytes);Assert.Zero(underGate);Assert.AreEqual(2,assigned);Assert.AreEqual(1,requested);Assert.AreEqual(1,session.AllocationCount);Assert.AreEqual(2,session.PendingCount);Assert.AreEqual(1,session.Requests.Count);Assert.AreEqual(free,session.Requests.CopyItems().Single().Entity);
                var nextFree=session.PendingRequestFor(free);var nextBlocked=session.PendingRequestFor(blocked);Assert.AreEqual(oldFree.Request,nextFree.Request);Assert.AreEqual(proposals[0].Endpoint,nextFree.GoalLocation);Assert.Greater(nextBlocked.Request,oldBlocked.Request);Assert.AreEqual(oldBlocked.Order,nextBlocked.Order);Assert.AreEqual(oldBlocked.Goal,nextBlocked.Goal);Assert.AreEqual(oldBlocked.GoalLocation,nextBlocked.GoalLocation);Assert.True(attempt.Published);Assert.False(attempt.Retiring);CollectionAssert.AreEqual(before,frozen.Encode());PhysicalShape(frozen);frozen.Dispose();frozen=null;
                session.EndpointAssigned-=Assigned;session.RouteRequested-=Requested;
                var prefix=Bytes();void Finish(){for(int tick=0;tick<20000&&session.PendingCount>0;tick++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);foreach(int id in movers){Assert.True(session.Crowd.TryGet(id,out var actor));Assert.True(actor.Moving);}}
                Finish();var suffix=Bytes();var facts=HostFacts();Pump(1);var next=Bytes();var nextFacts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Finish();CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;session.EndpointAssigned-=Assigned;session.RouteRequested-=Requested;frozen?.Dispose();}
        }
        [TestCase(false)][TestCase(true)] public void ActualHeldChangeRetriesReadyCohortAtomicallyAndCompletesCold(bool refuse)
        {
            int held=Spawn(-18,4),a=Spawn(-18,-14),b=Spawn(-18,-8);int[] movers={a,b};
            void Human(PlayableCommandKind kind,int[] ids,NavPoint goal=default)=>Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",kind,ids,goal,origin:PlayableOrderOrigin.Human),null));
            Human(PlayableCommandKind.Move,movers,new NavPoint(-10,-10));kernel.PauseAtPhysicalBoundary=()=>session.HasReadyArrival;
            try{for(int i=0;i<20000&&!session.HasReadyArrival;i++)Pump(1);}finally{kernel.PauseAtPhysicalBoundary=null;}
            Assert.True(session.HasReadyArrival);Assert.Zero(session.Requests.Count);Assert.AreEqual(2,session.AllocationCount);
            var old=movers.Select(id=>session.PendingRequestFor(id)).ToArray();Assert.True(old.All(r=>r.HeldIdentity==""));Human(PlayableCommandKind.Hold,new[]{held});
            var tables=(NavigationSession.NavigationLiveArrivalTables)typeof(NavigationSession).GetField("liveArrivalTables",F).GetValue(session);var attempt=tables.Attempts[tables.SortedId(0)];
            var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var before=frozen.Encode();var budget=session.PhysicalHostBudget;long charge=budget.PhysicalBytes;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);int denied=0,underGate=0,assigned=0,requested=0;
            Action<NavigationRequest> OnAssigned=r=>{assigned++;Assert.False(Monitor.IsEntered(gate));};Action<NavigationRequest> OnRequested=r=>requested++;
            session.EndpointAssigned+=OnAssigned;session.RouteRequested+=OnRequested;
            try{
                if(refuse){try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){denied++;return false;}return true;};Assert.AreEqual(false,typeof(NavigationSession).GetMethod("TryRetryOwnedArrival",F).Invoke(session,new object[]{attempt}));}finally{budget.AllocationAdmission=null;}
                    Assert.AreEqual(1,denied);Assert.Zero(session.OwnedArrivalRetryPublications);Assert.Zero(assigned);Assert.Zero(requested);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());CollectionAssert.AreEqual(before,frozen.Encode());
                }
                try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};session.PrepareDeliveryBarrier();}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,session.OwnedArrivalRetryPublications);Assert.Zero(session.LastOwnedArrivalRetryPublicationAllocatedBytes,"retry publication allocation");Assert.Zero(underGate,"admission callbacks under gate");Assert.AreEqual(2,assigned);Assert.Zero(requested);Assert.AreEqual(2,session.AllocationCount);Assert.AreEqual(2,session.PendingCount,"PendingCount includes allocating");Assert.Zero(session.Requests.Count);
                for(int i=0;i<movers.Length;i++){var next=session.PendingRequestFor(movers[i]);Assert.AreNotSame(old[i],next);Assert.Greater(next.Request,old[i].Request);Assert.AreEqual(old[i].Order,next.Order);Assert.AreEqual(old[i].Goal,next.Goal);Assert.AreEqual(old[i].GoalLocation,next.GoalLocation);Assert.AreEqual(old[i].MemberIdentity.OrderRevision,next.MemberIdentity.OrderRevision);Assert.IsNotEmpty(next.HeldIdentity);Assert.NotNull(next.StorageOwner);}
                Assert.True(attempt.Published);Assert.True(attempt.Retiring);CollectionAssert.AreEqual(before,frozen.Encode());PhysicalShape(frozen);frozen.Dispose();frozen=null;
                session.EndpointAssigned-=OnAssigned;session.RouteRequested-=OnRequested;
                var prefix=Bytes();void Finish(){for(int tick=0;tick<20000&&(session.PendingCount>0||session.AllocationCount>0);tick++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);Assert.Zero(session.AllocationCount);foreach(int id in movers){Assert.True(session.Crowd.TryGet(id,out var actor));Assert.True(actor.Moving);}}
                Finish();var suffix=Bytes();var facts=HostFacts();Pump(1);var nextBytes=Bytes();var nextFacts=HostFacts();Restore(prefix);Assert.Zero(budget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Finish();CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(facts,HostFacts());Pump(1);CollectionAssert.AreEqual(nextBytes,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;session.EndpointAssigned-=OnAssigned;session.RouteRequested-=OnRequested;frozen?.Dispose();}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] public void ReadyArrivalValidatesExactAttemptAcrossHostFrameOrReentrantStop(int change)
        {
            bool stop=change==1;
            int entity=Spawn(-18,-14);Arrival(entity,new NavPoint(-10,-14));kernel.PauseAtPhysicalBoundary=()=>session.HasReadyArrival;
            try{for(int i=0;i<20000&&!session.HasReadyArrival;i++)Pump(64);}finally{kernel.PauseAtPhysicalBoundary=null;}
            Assert.True(session.HasReadyArrival);Assert.Zero(session.Requests.Count);
            var attempts=typeof(NavigationSession).GetField("arrivalAttempts",F).GetValue(session);var values=(IEnumerable)attempts.GetType().GetProperty("Values").GetValue(attempts);object attempt=null;foreach(var value in values){attempt=value;break;}Assert.NotNull(attempt);
            var source=session.PinCommittedHostRoot();bool changed=false;byte[] stopped=null;
            try{
                session.PhysicalHostBudget.AllocationAdmission=_=>{if(!changed){changed=true;if(stop){session.Stop(entity,false);stopped=Bytes();}else if(change==2){kernel.Clear();kernel.Attach(session.RoutePort);}else if(change==3)typeof(NavigationSession).GetMethod("AdmissionBatch",F,null,new[]{typeof(Action)},null).Invoke(session,new object[]{(Action)(()=>{})});else if(change==4){session.Rebind(activeProfile.Navigation);stopped=Bytes();}else Pump(1);}return true;};
                bool published=(bool)typeof(NavigationSession).GetMethod("TryPublishOwnedArrival",F).Invoke(session,new[]{attempt});
                Assert.True(changed);Assert.AreEqual(change==0,published);
                if(stop){Assert.Zero(session.Requests.Count);Assert.Zero(session.AllocationCount);Assert.Zero(session.PendingCount);CollectionAssert.AreEqual(stopped,Bytes(),"stale attempt cannot overwrite reentrant Stop");}
                else if(change==0){var current=session.PinCommittedHostRoot();try{Assert.AreNotSame(source,current);Assert.AreSame(source.ArrivalWork[(long)attempt.GetType().GetField("Id",F).GetValue(attempt)],current.ArrivalWork[(long)attempt.GetType().GetField("Id",F).GetValue(attempt)],"same exact work role across frontier frame");}finally{current.Release();}Assert.AreEqual(1,session.Requests.Count);Assert.Zero(session.AllocationCount);Assert.AreEqual(1,session.PendingCount);}
                else{Assert.Zero(session.Requests.Count);Assert.AreEqual(1,session.AllocationCount);if(change==4)CollectionAssert.AreEqual(stopped,Bytes(),"stale attempt cannot overwrite rebind");else Assert.True(session.HasReadyArrival);}
            }finally{session.PhysicalHostBudget.AllocationAdmission=null;source.Release();}
            Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }
        [Test] public void PhysicalAdvancePinsExactSourcesAndUnwindsAfterHistoryRefusal()
        {
            int entity=Spawn(-18,-14);Arrival(entity,new NavPoint(-10,-14));kernel.PauseAtPhysicalBoundary=()=>session.ArrivalPhases.Length!=0;Pump(1);kernel.PauseAtPhysicalBoundary=null;
            var attempts=typeof(NavigationSession).GetField("arrivalAttempts",F).GetValue(session);var values=(IEnumerable)attempts.GetType().GetProperty("Values").GetValue(attempts);object attempt=null;foreach(var value in values){attempt=value;break;}Assert.NotNull(attempt);
            var work=(NavigationArrivalWork)attempt.GetType().GetField("Work",F).GetValue(attempt);var history=(NavigationHostArrivalHistory)attempt.GetType().GetField("History",F).GetValue(attempt);int workRefs=work.References,historyRefs=history.References;var before=Bytes();long bytes=session.PhysicalHostBudget.PhysicalBytes;bool observed=false;int denied=0;
            session.BeforeArrivalAdvanceCommit=_=>{observed=true;Assert.That(work.References,Is.GreaterThan(workRefs),"worker must own its exact immutable work source");Assert.That(history.References,Is.GreaterThan(historyRefs),"worker must own its exact immutable history source");session.PhysicalHostBudget.AllocationAdmission=size=>{denied++;return false;};};
            try{Assert.Throws<InvalidOperationException>(()=>session.AdvancePhysicalArrivalAuthority(1));}
            finally{session.BeforeArrivalAdvanceCommit=null;session.PhysicalHostBudget.AllocationAdmission=null;}
            Assert.True(observed);Assert.AreEqual(1,denied);Assert.AreSame(work,attempt.GetType().GetField("Work",F).GetValue(attempt));Assert.AreSame(history,attempt.GetType().GetField("History",F).GetValue(attempt));Assert.False((bool)attempt.GetType().GetField("Working",F).GetValue(attempt));Assert.AreEqual(workRefs,work.References);Assert.AreEqual(historyRefs,history.References);Assert.AreEqual(bytes,session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes());
            Assert.True(session.AdvancePhysicalArrivalAuthority(1));Assert.AreNotSame(work,attempt.GetType().GetField("Work",F).GetValue(attempt));Assert.AreNotSame(history,attempt.GetType().GetField("History",F).GetValue(attempt));
            var prefix=Bytes();Pump(1);var expected=Bytes();string facts=HostFacts();Restore(prefix);CollectionAssert.AreEqual(prefix,Bytes());Pump(1);CollectionAssert.AreEqual(expected,Bytes());Assert.AreEqual(facts,HostFacts());
        }
        [Test] public void ReadyWithRetiringWorkerSelectsCompatibilityBeforePreparedWrites()
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-10);Arrival(a,new NavPoint(-10,-14));Pump(1);session.ApplyResults();
            var attempts=typeof(NavigationSession).GetField("arrivalAttempts",F).GetValue(session);var values=(IEnumerable)attempts.GetType().GetProperty("Values").GetValue(attempts);object attempt=null;foreach(var value in values){attempt=value;break;}Assert.NotNull(attempt);
            // A legitimate worker-owned retiring attempt has no gameplay row,
            // but remains represented until its worker releases the compute pin.
            var working=attempt.GetType().GetField("Working",F);working.SetValue(attempt,true);
            try{
                session.Stop(a,false);Assert.True((bool)attempt.GetType().GetField("Retiring",F).GetValue(attempt));Assert.True(session.Move(b,new NavPoint(-10,-10)));
                for(int tick=0;tick<20000&&session.Answers.Count==0;tick++)Pump(64);Assert.AreEqual(1,session.Answers.Count);Assert.AreEqual(b,session.Answers.CopyItems()[0].Request.Entity);
                session.ApplyResults();Assert.Zero(session.OwnedReadyPublications,"nonempty prune must select compatibility before prepared writes");Assert.True(session.Crowd.TryGet(b,out var actor));Assert.True(actor.Moving);Assert.Null(session.PendingRequestFor(b));Assert.Zero(session.Answers.Count);
            }finally{working.SetValue(attempt,false);session.Stop(b,false);}
            Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }
        [Test] public void GroupContactSubsetStopAndPartialReissuePreserveOwnedCohortThroughColdNextPump()
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-10),c=Spawn(-18,-6);int[] entities={a,b,c};
            void Human(PlayableCommandKind kind,int[] ids,NavPoint goal=default)=>Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",kind,ids,goal,origin:PlayableOrderOrigin.Human),null));
            NavigationInstalledExecutions Installed()=>(NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);
            void Finish(){for(int tick=0;tick<20000&&(session.PendingCount>0||session.AllocationCount>0);tick++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);Assert.Zero(session.AllocationCount);}
            Human(PlayableCommandKind.Move,entities,new NavPoint(-10,-10));Finish();Assert.AreEqual(3,session.OwnedReadyPublications);Assert.Zero(ArrivalLeaseCount);
            var oldOwners=entities.Select(entity=>Installed().OwnerFor(entity)).ToArray();Assert.True(oldOwners.All(owner=>owner!=null));long oldA=oldOwners[0].Request.Request.Request,oldB=oldOwners[1].Request.Request.Request;var oldBudget=session.PhysicalHostBudget;
            var oldCapture=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation");var oldBytes=oldCapture.Encode();bool oldCaptureReleased=false;
            try{
                session.SetFormationContact(a,true,Convert.ToInt64(Domain.GetProperty("Tick",F|BindingFlags.Public).GetValue(domain)));Human(PlayableCommandKind.Stop,new[]{c});Human(PlayableCommandKind.Move,new[]{b},new NavPoint(-6,-14));Assert.True(Installed().IsDetached(b));Assert.AreSame(oldOwners[1],Installed().OwnerFor(b));Assert.Null(Installed().OwnerFor(c));
                kernel.Continue=()=>ArrivalLeaseCount<1;for(int tick=0;tick<20000&&ArrivalLeaseCount<1;tick++){Pump(4);session.ApplyResults();}Assert.AreEqual(1,ArrivalLeaseCount);Assert.True(session.IsPending(b));Assert.Zero(session.Answers.Count);Assert.True(session.GroupFor(a).Members.Single(m=>m.Entity==a).FormationReleased);CollectionAssert.AreEqual(oldBytes,oldCapture.Encode());var prefix=Bytes();int publications=session.OwnedReadyPublications;
                Assert.Greater(session.OwnedCapturePublications,0,"actual partial arrival must use prepared capture");Assert.Zero(session.LastOwnedCaptureObservationAllocatedBytes,"coherent capture observation allocation");Assert.Zero(session.LastOwnedCapturePublicationAllocatedBytes,"capture final publication allocation");
                kernel.Continue=null;Finish();Assert.AreEqual(publications+1,session.OwnedReadyPublications,"other installed cohort member makes historical-row removal a no-op prune");Assert.AreSame(oldOwners[0],Installed().OwnerFor(a));Assert.AreNotSame(oldOwners[1],Installed().OwnerFor(b));Assert.False(Installed().IsDetached(b));Assert.True(session.ArrivalReservations.Any(row=>row.Request==oldA&&row.State==NavArrivalReservationKind.InstalledExecution));Assert.False(session.ArrivalReservations.Any(row=>row.Request==oldB));CollectionAssert.AreEqual(oldBytes,oldCapture.Encode());PhysicalShape(oldCapture);
                // Compare source/cold work under the same reader context. The
                // old pre-command capture has proved COW; cold prefix never
                // included that independent historical reader.
                oldCapture.Dispose();oldCaptureReleased=true;
                var suffix=Bytes();string suffixFacts=HostFacts();Pump(1);var next=Bytes();string nextFacts=HostFacts();Restore(prefix);Assert.Zero(oldBudget.PhysicalBytes);CollectionAssert.AreEqual(prefix,Bytes());Finish();Assert.AreEqual(1,session.OwnedReadyPublications);CollectionAssert.AreEqual(suffix,Bytes());Assert.AreEqual(suffixFacts,HostFacts());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());Assert.True(session.Crowd.TryGet(a,out var cold));Assert.AreSame(cold.StorageOwner.Global,Installed().OwnerFor(a).Route);Assert.True(session.GroupFor(a).Members.Single(m=>m.Entity==a).FormationReleased);Assert.Null(Installed().OwnerFor(c));
            }finally{if(!oldCaptureReleased)oldCapture.Dispose();}
            Assert.Zero(oldBudget.PhysicalBytes);Assert.Zero(oldBudget.HeldBytes);Assert.Zero(oldBudget.ActiveScopes);foreach(var owner in oldOwners)Assert.Zero(owner.References);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }
        [TestCase(0)][TestCase(1)][TestCase(2)] public void PreparedCaptureRefusesDtoArrivalAndBodyWithoutChangingActualPartialAuthority(int refusal)
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-10),c=Spawn(-18,-6),unread=Spawn(-18,6);int[] entities={a,b,c};
            void Human(PlayableCommandKind kind,int[] ids,NavPoint goal=default)=>Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++command,"player-1",kind,ids,goal,origin:PlayableOrderOrigin.Human),null));
            Human(PlayableCommandKind.Move,entities,new NavPoint(-10,-10));for(int tick=0;tick<20000&&(session.PendingCount>0||session.AllocationCount>0);tick++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);
            var installed=(NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);var historical=installed.OwnerFor(b);Assert.NotNull(historical);
            Assert.True(session.Move(unread,new NavPoint(-10,6)));for(int tick=0;tick<20000&&session.Answers.Count==0;tick++)Pump(4);Assert.AreEqual(1,session.Answers.Count);var receipt=session.Answers.CopyItems()[0];Assert.AreEqual(unread,receipt.Request.Entity);Assert.True(session.DeliveryBarrierReady,"park unread receipt in Session barrier before next allocation");
            session.SetFormationContact(a,true,0);Human(PlayableCommandKind.Stop,new[]{c});Human(PlayableCommandKind.Move,new[]{b},new NavPoint(-6,-14));
            kernel.Continue=()=>ArrivalLeaseCount<1;for(int tick=0;tick<20000&&ArrivalLeaseCount<1;tick++){Pump(4);session.PrepareDeliveryBarrier();}Assert.AreEqual(1,ArrivalLeaseCount);var barrier=(NavigationSession.NavigationBarrierAnswers)typeof(NavigationSession).GetField("barrierAnswers",F).GetValue(session);Assert.True(barrier.Contains(receipt));Assert.True(installed.IsDetached(b));
            var budget=session.PhysicalHostBudget;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);var operation=typeof(NavigationSession).GetField("authorityOperationIdentity",F);var publication=typeof(NavigationSession).GetField("admissionPublicationIdentity",F);
            using(var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation")){
                var before=frozen.Encode();long physical=budget.PhysicalBytes,op=(long)operation.GetValue(session),pub=(long)publication.GetValue(session);var admission=session.Admission;string facts=HostFacts();int denied=0,underGate=0,publications=session.OwnedCapturePublications;
                try{budget.AllocationAdmission=_=>{
                    if(Monitor.IsEntered(gate))underGate++;var frames=new System.Diagnostics.StackTrace().GetFrames();
                    bool target=refusal==0?frames.Any(f=>f.GetMethod().DeclaringType==typeof(NavigationCaptureDtoStorage)):refusal==1?frames.Any(f=>f.GetMethod().DeclaringType==typeof(NavigationCaptureBytes)):frames.Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAuthorityOwners)&&f.GetMethod().Name=="CreateCatalogOwned");
                    if(target){denied++;return false;}return true;
                };var failure=Assert.Throws<TargetInvocationException>(()=>Call("CaptureWorldState",7,"a5a-host-continuation"));Assert.IsInstanceOf<InvalidOperationException>(failure.InnerException);}finally{budget.AllocationAdmission=null;}
                Assert.Greater(denied,0);Assert.Zero(underGate);Assert.AreEqual(op,operation.GetValue(session));Assert.AreEqual(pub,publication.GetValue(session));Assert.AreSame(admission,session.Admission);Assert.AreEqual(publications,session.OwnedCapturePublications);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.AreEqual(facts,HostFacts());Assert.True(barrier.Contains(receipt));Assert.AreSame(historical,installed.OwnerFor(b));CollectionAssert.AreEqual(before,frozen.Encode());CollectionAssert.AreEqual(before,Bytes());
                Assert.Zero(session.LastOwnedCaptureObservationAllocatedBytes);Assert.Zero(session.LastOwnedCapturePublicationAllocatedBytes);PhysicalShape(frozen);
            }
            kernel.Continue=null;
        }
        [TestCase(false)][TestCase(true)] public void PreparedCaptureStagesStaleMemberAdmissionAndEmptyHostBeforeAnyPublication(bool missingHost)
        {
            int entity=Spawn(-18,-14);Arrival(entity,new NavPoint(-10,-14));for(int tick=0;tick<20000&&(session.PendingCount>0||session.AllocationCount>0);tick++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);
            var budget=session.PhysicalHostBudget;using(var frozen=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation")){
                var old=frozen.Encode();session.Crowd.Stop(entity,false);var installed=(NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);installed.SetDetachedNoThrow(entity,true);
                if(missingHost){kernel.Clear();session.ReleaseCommittedHostRoot();}
                var groups=(NavigationCommandSlots<long,GroupOrderState>)typeof(NavigationSession).GetField("groups",F).GetValue(session);var record=session.GroupFor(entity);var live=(NavigationLiveGroup)groups.OwnerFor(record.GroupId);var version=live.ImmutableVersion;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);var operation=typeof(NavigationSession).GetField("authorityOperationIdentity",F);var publication=typeof(NavigationSession).GetField("admissionPublicationIdentity",F);long op=(long)operation.GetValue(session),pub=(long)publication.GetValue(session),physical=budget.PhysicalBytes;var admission=session.Admission;var root=session.PinCommittedHostRoot();int denied=0,underGate=0;
                try{
                    try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){denied++;return false;}return true;};Assert.Throws<InvalidOperationException>(()=>{using(var unexpected=session.CaptureOwnedState())Assert.Fail("capture admission must refuse");});}finally{budget.AllocationAdmission=null;}
                    Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreEqual(op,operation.GetValue(session));Assert.AreEqual(pub,publication.GetValue(session));Assert.AreSame(version,live.ImmutableVersion);Assert.AreEqual(record.Members[0].Phase,session.GroupFor(entity).Members[0].Phase);Assert.AreEqual(record.Members[0].MobilityRevision,session.GroupFor(entity).Members[0].MobilityRevision);Assert.AreSame(admission,session.Admission);var after=session.PinCommittedHostRoot();try{Assert.AreSame(root,after);}finally{after?.Release();}Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(old,frozen.Encode());
                    try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};using(var capture=session.CaptureOwnedState()){CertifiedPhysicalPumpTests.AssertCaptureDtoLayout(capture);Assert.AreEqual(GroupMemberPhase.Stopped,capture.Groups.Single().Members.Single().Phase);}}finally{budget.AllocationAdmission=null;}
                    Assert.Zero(underGate);Assert.AreEqual(op+2,operation.GetValue(session));Assert.AreEqual(pub+1,publication.GetValue(session));Assert.AreEqual(GroupMemberPhase.Stopped,session.GroupFor(entity).Members.Single().Phase);Assert.Zero(session.LastOwnedCaptureObservationAllocatedBytes);Assert.Zero(session.LastOwnedCapturePublicationAllocatedBytes);Assert.True(session.HasPhysicalHostRoot);CollectionAssert.AreEqual(old,frozen.Encode());
                }finally{root?.Release();}
            }
            var prefix=Bytes();Pump(1);var next=Bytes();var facts=HostFacts();Restore(prefix);CollectionAssert.AreEqual(prefix,Bytes());Pump(1);CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(facts,HostFacts());
        }
        [TestCase(false)][TestCase(true)] public void PreparedCaptureRejectsFactoryReentryThroughStopOrWorkerAttachment(bool attachment)
        {
            int entity=Spawn(-18,-14);Arrival(entity,new NavPoint(-10,-14));for(int tick=0;tick<20000&&(session.PendingCount>0||session.AllocationCount>0);tick++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);var before=Bytes();var budget=session.PhysicalHostBudget;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);var operation=typeof(NavigationSession).GetField("authorityOperationIdentity",F);long afterOperation=(long)operation.GetValue(session);byte[] changedBytes=null;bool changed=false;int underGate=0;long physical=budget.PhysicalBytes;
            try{budget.AllocationAdmission=_=>{
                if(Monitor.IsEntered(gate))underGate++;if(!changed&&new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationCaptureDtoStorage))){changed=true;if(attachment){session.DetachCertifiedHost(kernel);session.AttachCertifiedHost(kernel);}else{session.Stop(entity,false);changedBytes=Bytes();}afterOperation=(long)operation.GetValue(session);}return true;
            };Assert.Throws<InvalidOperationException>(()=>{using(var unexpected=session.CaptureOwnedState())Assert.Fail("capture must reject a changed observed source");});}finally{budget.AllocationAdmission=null;}
            Assert.True(changed);Assert.Zero(underGate);Assert.AreEqual(afterOperation,operation.GetValue(session));Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);if(attachment){Assert.AreEqual(physical,budget.PhysicalBytes);CollectionAssert.AreEqual(before,Bytes());}else{CollectionAssert.AreEqual(changedBytes,Bytes());Assert.True(session.Crowd.TryGet(entity,out var actor));Assert.False(actor.Moving);Assert.Null(actor.StorageOwner.Global);}Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
        }
        private string PhysicalShape(PlayableWorldState state,params NavOwnedStorage[] additional)
        {
            var nav=state.CaptureNavigation;var roots=new NavOwnedStorage[]{nav.CaptureRoot,nav.CaptureAuthority,nav.CaptureOutput,nav.CaptureArrivalOutput,nav.CaptureDto,state.CaptureDomain,state.CaptureNavigationBytes,state.CaptureBinding,state.CaptureParticipants,state.CaptureVision};
            roots=roots.Concat(additional).ToArray();Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,roots),session.PhysicalHostBudget.PhysicalBytes);return NavigationStorageOwnershipTests.OwnerShapeWithSession(session,roots);
        }
        [Test] public void ActualInstalledRouteSurvivesHostRetirementAndCaptureRestoresExactAlias()
        {
            int entity=Spawn(-18,-14);Arrival(entity,new NavPoint(-10,-14));
            NavigationInstalledExecutionState Installed()=>((System.Collections.Generic.IDictionary<int,NavigationInstalledExecutionState>)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session)).TryGetValue(entity,out var value)?value:null;
            for(int tick=0;tick<20000&&Installed()?.StorageOwner==null;tick++){Pump(64);session.ApplyResults();}
            Assert.NotNull(Installed()?.StorageOwner,"actual Ready must install an owned historical execution");Assert.True(session.Crowd.TryGet(entity,out var actor));
            var owner=Installed().StorageOwner;var route=owner.Route;var request=owner.Request;
            Assert.AreSame(actor.StorageOwner.Global,route);Assert.AreSame(actor.route,route.ReadOnlyBacking);Assert.Zero(session.Answers.Count);
            kernel.Clear();session.ReleaseCommittedHostRoot();
            Assert.Greater(owner.References,0);Assert.Greater(route.References,0);Assert.Greater(request.References,0,"installed ownership must outlive the delivered host row");
            var oldBudget=session.PhysicalHostBudget;
            using(var capture=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation")){
                var prefix=capture.Encode();CertifiedPhysicalPumpTests.AssertCaptureDtoLayout(capture.CaptureNavigation);
                var catalog=capture.CaptureNavigation.CaptureAuthority;int actorIndex=Array.IndexOf(capture.CaptureNavigation.Crowd.Units.Select(u=>u.Id).ToArray(),entity);Assert.GreaterOrEqual(actorIndex,0);Assert.AreEqual(entity,catalog.CrowdKeys[actorIndex]);Assert.AreSame(route,catalog.ActorGlobalCatalog[actorIndex]);Assert.AreSame(owner,catalog.InstalledCatalog[0]);Assert.AreSame(((NavigationInstalledExecutionOwner)catalog.InstalledCatalog[0]).Route,catalog.ActorGlobalCatalog[actorIndex]);
                Pump(1);session.ApplyResults();var next=Bytes();var facts=HostFacts();var hotShape=PhysicalShape(capture);
                session.Stop(entity,false);Assert.Null(Installed());Assert.Null(actor.StorageOwner.Global);Assert.Greater(owner.References,0);Assert.Greater(route.References,0);
                kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");kernel=null;session=null;domain=null;
                Assert.Greater(owner.References,0);Assert.Greater(request.References,0);CollectionAssert.AreEqual(prefix,capture.Encode(),"retired source must not mutate the private checkpoint");
                domain=Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{prefix,PlayableProfile.Default,7,"a5a-host-continuation"});session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);kernel=new CertifiedRouteKernel();
                Assert.True(session.Crowd.TryGet(entity,out var cold));Assert.NotNull(Installed()?.StorageOwner);Assert.AreSame(cold.StorageOwner.Global,Installed().StorageOwner.Route);Assert.AreSame(cold.route,Installed().InstalledRoute);
                using(var coldCapture=(PlayableWorldState)Call("CaptureWorldState",7,"a5a-host-continuation")){CollectionAssert.AreEqual(prefix,coldCapture.Encode());CollectionAssert.AreEqual(prefix,Bytes());Pump(1);session.ApplyResults();CollectionAssert.AreEqual(next,Bytes());TestContext.WriteLine("HOT_OWNERS\n"+hotShape+"\nCOLD_OWNERS\n"+PhysicalShape(coldCapture));Assert.AreEqual(facts,HostFacts());}
            }
            Assert.Zero(owner.References);Assert.Zero(route.References);Assert.Zero(request.References);Assert.Zero(oldBudget.PhysicalBytes);Assert.Zero(oldBudget.HeldBytes);Assert.Zero(oldBudget.ActiveScopes);
        }

        [Test] public void PreparedAnswerMovementSuspendsOnlyProjectedPendingActorsWithoutDroppingOldRoute()
        {
            int pendingActor=Spawn(-18,-14),readyA=Spawn(-18,-6),readyB=Spawn(-18,6);
            Assert.True(session.Move(pendingActor,new NavPoint(-10,-14)));
            for(int i=0;i<4096&&session.IsPending(pendingActor);i++){Pump(64);session.ApplyResults();}
            Assert.False(session.IsPending(pendingActor));Assert.True(session.Crowd.TryGet(pendingActor,out var actor));Assert.True(actor.Moving);
            var oldRoute=actor.StorageOwner.Global;long mobility=actor.MobilityRevision;var position=actor.Position;
            Assert.True(session.Move(pendingActor,new NavPoint(-8,-14)));Assert.True(session.Requests.TryDequeue(out var withheld));Assert.AreEqual(pendingActor,withheld.Entity);
            Assert.True(session.Move(readyA,new NavPoint(-10,-6)));Assert.True(session.Move(readyB,new NavPoint(-10,6)));
            for(int i=0;i<4096&&session.Answers.Count<2;i++)Pump(64);
            Assert.AreEqual(2,session.Answers.Count);Assert.AreEqual(3,session.PendingCount);session.SuspendPendingMovement=true;
            int movement=session.OwnedMovementPublications,batch=session.OwnedResultBatchPublications;
            session.Step(1d/30);
            Assert.AreEqual(batch+1,session.OwnedResultBatchPublications);Assert.AreEqual(movement+1,session.OwnedMovementPublications);Assert.Zero(session.LastOwnedMovementPublicationAllocatedBytes);Assert.Zero(session.LastOwnedResultBatchPublicationAllocatedBytes);
            Assert.True(session.IsPending(pendingActor));Assert.False(actor.Moving);Assert.AreEqual(position,actor.Position);Assert.AreEqual(mobility,actor.MobilityRevision);Assert.AreSame(oldRoute,actor.StorageOwner.Global);
            Assert.False(session.IsPending(readyA));Assert.False(session.IsPending(readyB));Assert.True(session.Crowd.TryGet(readyA,out var activatedA)&&activatedA.Moving);Assert.True(session.Crowd.TryGet(readyB,out var activatedB)&&activatedB.Moving);
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
        }

        [Test] public void ReverseInstallOrderPreservesDistinctHistoricalGeometryThroughColdPump()
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-10),held=Spawn(18,18);
            NavigationInstalledExecutions Installed()=>(NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions",F).GetValue(session);
            session.Stop(held,true);Assert.True(session.Move(b,new NavPoint(-10,-10)));
            for(int tick=0;tick<20000&&Installed().OwnerFor(b)==null;tick++){Pump(64);session.ApplyResults();}
            var historical=Installed().OwnerFor(b);Assert.NotNull(historical);Assert.False(Installed().IsDetached(b));
            session.Stop(held,false);Assert.True(session.Move(a,new NavPoint(-10,-14)));
            for(int tick=0;tick<20000&&session.Answers.Count==0;tick++)Pump(64);
            Assert.AreEqual(1,session.Answers.Count);Assert.AreEqual(a,session.Answers.CopyItems()[0].Request.Entity);
            // Reissue B before consuming A, so its held binding is current while
            // the old installed history remains attached to B's live header.
            Assert.True(session.Move(b,new NavPoint(-10,-10)));session.ApplyResults();
            Assert.NotNull(Installed().OwnerFor(a));Assert.AreSame(historical,Installed().OwnerFor(b));Assert.True(Installed().IsDetached(b));Assert.False(historical.State.Detached,"mutable detachment must not enter the shared immutable historical facade");
            Assert.AreNotSame(Installed().OwnerFor(a).Request.Request.Geometry,historical.Request.Request.Geometry);
            Assert.AreNotEqual(Installed().OwnerFor(a).Request.Request.Geometry.Obstacles.Count,historical.Request.Request.Geometry.Obstacles.Count);
            var ordered=Installed().Values.Select(value=>value.Entity).ToArray();CollectionAssert.AreEqual(new[]{a,b},ordered);
            kernel.Clear();session.ReleaseCommittedHostRoot();var prefix=Bytes();
            Pump(1);var expected=Bytes();string facts=HostFacts();Restore(prefix);
            CollectionAssert.AreEqual(prefix,Bytes());Assert.True(Installed().IsDetached(b));Assert.False(Installed().OwnerFor(b).State.Detached);Assert.False(Installed().IsDetached(a));
            Assert.True(session.Crowd.TryGet(a,out var actor));Assert.AreSame(actor.StorageOwner.Global,Installed().OwnerFor(a).Route);
            Pump(1);CollectionAssert.AreEqual(expected,Bytes());Assert.AreEqual(facts,HostFacts());Assert.Zero(historical.References);
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }
        [TestCase(false)][TestCase(true)] public void MixedHostBytesOnlyRestoreDoesNotReplayOrdinaryWork(bool partition)=>ExerciseMixedHostContinuation(partition,false);
        [Test] public void MixedHostNearCapCaptureRecapturesResumedSuffixAndMatchesCancellationRetirement()=>ExerciseMixedHostContinuation(false,true);
        private void ExerciseMixedHostContinuation(bool partition,bool nearCap)
        {
            if(partition){kernel.Clear();session.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");activeProfile=PlayableProfile.ThreeCrossingsDefault;domain=Activator.CreateInstance(Domain,F,null,new object[]{activeProfile,1L,false},null);session=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);kernel=new CertifiedRouteKernel();}
            int a=Spawn(-18,partition?0:-14),b=Spawn(-22,partition?-2:-14),c=Spawn(-24,partition?4:-8),d=Spawn(-26,partition?-20:4);
            Arrival(a,new NavPoint(-10,partition?0:-14));
            session.PrepareDeliveryBarrier();
            kernel.Continue=()=>ArrivalLeaseCount<1;
            for(int n=0;n<20000&&ArrivalLeaseCount<1;n++){Pump(4);session.ApplyResults();}
            Assert.AreEqual(1,ArrivalLeaseCount,"A must be transferred with its projection still pending");
            Assert.True(session.Move(d,partition?new NavPoint(18,-20):new NavPoint(-18,4)),"D supplies a real earlier host completion");
            kernel.Continue=()=>HostRow(d)==null;
            for(int n=0;n<20000&&HostRow(d)==null;n++){Pump(1);session.ApplyResults();}
            Assert.NotNull(HostRow(d),"D must be registered before the later mixed ingress");
            kernel.Continue=()=>false;
            for(int n=0;n<20000&&HostRow(d).Solved==null;n++){
                Pump(4);session.ApplyResults();
            }
            Assert.NotNull(HostRow(d).Solved,
                "D must have a completed route waiting in the host projection queue");
            Assert.True(session.Move(b,partition?new NavPoint(18,0):new NavPoint(8,-14)),"B is a real ordinary request");
            Arrival(c,new NavPoint(-10,partition?4:-8));
            session.PrepareDeliveryBarrier();
            kernel.Continue=()=>false;
            for(int n=0;n<20000&&session.Requests.Count<2;n++){Pump(4);session.ApplyResults();}
            Assert.AreEqual(2,session.Requests.Count,"B and assigned C must wait in their actual mailbox order");
            session.Stop(d,false);
            kernel.Continue=null;
            string Charges()
            {
                var names=new[]{"ArrivalProviderOwnedBytes","ArrivalRetainedBytes"};
                var values=names.Select(name=>name+"="+typeof(NavigationSession).GetProperty(name,F).GetValue(session));
                var fields=new[]{"installedCorridorBytes","installedGeometryBytes","answerCorridorBytes"}.Select(name=>name+"="+typeof(NavigationSession).GetField(name,F).GetValue(session));
                var providers=(IEnumerable)typeof(NavigationSession).GetField("arrivalProviderIds",F).GetValue(session);
                var rows=providers.Cast<object>().Select(pair=>pair.GetType().GetProperty("Value").GetValue(pair)).Select(row=>{var provider=(ILayeredNavigationProvider)row.GetType().GetField("Provider",F|BindingFlags.Public).GetValue(row);return provider.Binding+":"+provider.EstimatedRetainedBytes;});
                return string.Join(";",values.Concat(fields).Concat(rows));
            }
            byte[] prefix=null,hotRecapture=null,coldRecapture=null;
            if(nearCap)kernel.PauseAtPhysicalBoundary=()=>{if(prefix!=null&&hotRecapture==null&&kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.EndAllocation)hotRecapture=Bytes();return false;};
            kernel.BeforePhysicalProjectionStep=()=>{
                if(prefix!=null||HostRows.Count(r=>r.ArrivalToken!=0)<2)return;
                var frontier=kernel.CapturePumpFrontier();
                Assert.AreEqual(CertifiedRouteKernel.PumpStage.Projection,frontier.Stage);
                Assert.AreEqual(4,frontier.OriginalBudget);
                Assert.AreEqual(64,frontier.ProjectionAllowance);
                Assert.Greater(frontier.AllocationFloor,0);
                Assert.Greater(frontier.SolveWorkLimit,0);
                Assert.Greater(frontier.SolveConsumed,0);
                var rows=HostRows;
                Assert.True(rows.Any(row=>row.ArrivalToken==0),"ordinary B must coexist with arrival projection A");
                Assert.GreaterOrEqual(session.OwnedIngressPublications,4,"A/D/B/C must use the shared owned ingress");Assert.Zero(session.LastOwnedIngressPublicationAllocatedBytes,"whole final ingress gate including guards");
                Assert.GreaterOrEqual(rows.Count(row=>row.ArrivalToken!=0),2,"arrival C must coexist with A and B in the host frontier");
                Assert.Greater(kernel.Counters.PhysicalGraphWork,0,"ordinary B has already consumed graph work");
                if(partition){Assert.IsInstanceOf<PartitionNavigationProvider>(HostRow(b).Provider);Assert.AreEqual("bank/west",HostRow(b).Request.StartLocation.Value.SurfaceId);Assert.AreEqual("bank/east",HostRow(b).Request.GoalLocation.Value.SurfaceId);}
                Assert.NotNull(HostRow(d).Answer,"stale D has a prior host-owned completion before A projects");
                var root=session.PinCommittedHostRoot();
                try{Assert.True(root.Projection.Any(key=>root.Rows.TryGetValue(key,out var entry)&&ReferenceEquals(entry,HostRow(d))),"D's stale projection queue key remains meaningful at capture");}
                finally{root.Release();}
                if(!nearCap){prefix=Bytes();return;}
                var budget=session.PhysicalHostBudget;long before=budget.RetainedBytes,peak=before;int underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
                byte[] unpressured;try{budget.AllocationAdmission=bytes=>{if(Monitor.IsEntered(gate))underGate++;peak=Math.Max(peak,budget.RetainedBytes+bytes);return true;};unpressured=Bytes();}finally{budget.AllocationAdmission=null;}
                Assert.Zero(underGate);long captureOverlap=Math.Max(peak-before,peak-budget.RetainedBytes);Assert.Greater(captureOverlap,0);long headroom=checked(captureOverlap+65536);
                NavRetainedArray<NavPoint> neighbor=null;try{
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,session.Generation,1,out var pressure));using(pressure){int count=checked((int)((LayeredNavigationScheduler.MaxRetainedBytes-budget.RetainedBytes-headroom-NavStorageLayout.Charge(96))/16));Assert.Greater(count,1000000);neighbor=NavRetainedArray<NavPoint>.Copy(new RepeatedPressurePoints(count),pressure.TryReserve);pressure.Attach(neighbor);pressure.Commit(neighbor,_=>{});}
                    Assert.Greater(budget.RetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes-8L*1024*1024,"Measured preparation must be genuinely near the physical cap");
                    Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,neighbor),budget.PhysicalBytes);
                    prefix=Bytes();CollectionAssert.AreEqual(unpressured,prefix,"A real independently owned sibling must not change captured authority");Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,neighbor),budget.PhysicalBytes);Assert.LessOrEqual(budget.PeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                    TestContext.WriteLine("mixed nearcap retained="+budget.RetainedBytes+" incrementalCaptureOverlap="+captureOverlap+" peak="+budget.PeakRetainedBytes);
                }finally{neighbor?.Release();}
            };
            for(int n=0;n<20000&&prefix==null;n++){Pump(4);session.ApplyResults();}
            Assert.NotNull(prefix,"real mixed projection checkpoint was not reached: pending="+session.PendingCount+
                " arrivals="+session.ArrivalRetentionDiagnostic+" phases="+string.Join(",",session.ArrivalPhases)+
                " hostWork="+kernel.HasWork+" projectionFailure="+kernel.LastProjectionFailure+
                " registered="+HostRows.Length);
            kernel.BeforePhysicalProjectionStep=null;kernel.PauseAtPhysicalBoundary=null;if(nearCap)Assert.NotNull(hotRecapture,"Recapture must occur inside the original resumed suffix");
            string hotCharges=Charges();TestContext.WriteLine("Hot suffix charges: "+hotCharges);
            byte[] suffix=Bytes();
            string suffixFacts=HostFacts();
            Pump(1);session.ApplyResults();byte[] next=Bytes();string nextFacts=HostFacts();
            Restore(prefix);
            CollectionAssert.AreEqual(prefix,Bytes(),"bytes-only restore must preserve the captured host frontier");
            int replayedPreparations=0;kernel.BeforePrepare=()=>replayedPreparations++;
            if(nearCap)kernel.PauseAtPhysicalBoundary=()=>{if(coldRecapture==null&&kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.EndAllocation)coldRecapture=Bytes();return false;};
            Pump(4);session.ApplyResults();kernel.PauseAtPhysicalBoundary=null;if(nearCap)CollectionAssert.AreEqual(hotRecapture,coldRecapture,"Matched recapture inside the resumed suffix");
            CollectionAssert.AreEqual(suffix,Bytes(),"resume the captured pump suffix without replaying consumed phases");
            Assert.Zero(replayedPreparations,"ordinary B was already admitted and must not be submitted again");
            Assert.AreEqual(suffixFacts,HostFacts(),"the solver, host rows and FIFO queues must resume from bytes");
            TestContext.WriteLine("Cold suffix charges: "+Charges());
            Assert.AreEqual(hotCharges,Charges(),"all retained provider and allocation-floor components agree before the next Pump");
            Pump(1);session.ApplyResults();
            CollectionAssert.AreEqual(next,Bytes(),"the changed budget applies to the next full pump");
            Assert.AreEqual(nextFacts,HostFacts(),"the next pump must keep exact grants and charges");
            if(nearCap){
                Restore(coldRecapture);Pump(4);session.ApplyResults();CollectionAssert.AreEqual(suffix,Bytes(),"Bytes recaptured inside the suffix resume its remaining phases exactly");Assert.AreEqual(suffixFacts,HostFacts());Pump(1);session.ApplyResults();CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(nextFacts,HostFacts());
                TestContext.WriteLine("Cancel identities a="+a+" b="+b+" c="+c+" d="+d);
                session.Stop(c,true);TestContext.WriteLine("After Stop queued="+string.Join(";",session.Requests.CopyItems().Select(r=>r.Entity+":"+r.Request+":ownerRefs="+(r.StorageOwner?.References??-1))));
                var canceled=Bytes();
                Pump(1);session.ApplyResults();TestContext.WriteLine("After cancellation Pump queued="+string.Join(";",session.Requests.CopyItems().Select(r=>r.Entity+":"+r.Request+":ownerRefs="+(r.StorageOwner?.References??-1))));
                var retired=Bytes();string retiredFacts=HostFacts();
                Restore(next);session.Stop(c,true);CollectionAssert.AreEqual(canceled,Bytes(),"Cancel the same member at the same semantic frontier");Pump(1);session.ApplyResults();CollectionAssert.AreEqual(retired,Bytes(),"Cancellation/discard/retirement retain the exact next Pump");Assert.AreEqual(retiredFacts,HostFacts());Assert.Null(session.PendingRequestFor(c));Assert.True(session.Crowd.Units.Single(actor=>actor.Id==c).Held);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }
        }

        [Test] public void PausedComputeCaptureRestoresIntoFreshRealWorkerAndStopsAfterOwnedCleanup()
        {
            int id=Spawn(-18,-14);Arrival(id,new NavPoint(-10,-14));
            kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.ArrivalAdvance;
            try{Pump(1);}finally{kernel.PauseAtPhysicalBoundary=null;}
            Assert.True(session.PreparePhysicalArrivalAttempt());kernel.Clear();var prior=Bytes();var originalBudget=session.PhysicalHostBudget;
            using(var computed=new ManualResetEventSlim())using(var resume=new ManualResetEventSlim()){
                CertifiedRouteWorker old=null,fresh=null;System.Threading.Tasks.Task<byte[]> capture=null;
                try{
                    session.BeforeArrivalAdvanceCommit=_=>{computed.Set();Assert.True(resume.Wait(TimeSpan.FromSeconds(10)));};
                    old=new CertifiedRouteWorker(Binding(),1);
                    Assert.True(computed.Wait(5000),old.Publication.Failure??"actual worker did not reach the uncommitted compute boundary");
                    capture=System.Threading.Tasks.Task.Run(()=>Bytes());Assert.True(capture.Wait(5000),"Capture must complete while real compute is held");
                    var prefix=capture.Result;CollectionAssert.AreEqual(prior,prefix,"Only the committed frontier is captured");
                    old.Dispose();Assert.False(old.IsStopped,"Stop ACK must wait for the held operation's cleanup");resume.Set();
                    Assert.True(SpinWait.SpinUntil(()=>old.IsStopped,5000));Assert.Null(old.Publication.Failure);session.BeforeArrivalAdvanceCommit=null;
                    Restore(prefix);Assert.Zero(originalBudget.PhysicalBytes);Assert.Zero(originalBudget.HeldBytes);Assert.Zero(originalBudget.ActiveScopes);CollectionAssert.AreEqual(prefix,Bytes());
                    int replayed=0;var restoredKernel=new CertifiedRouteKernel();restoredKernel.BeforePrepare=()=>Interlocked.Increment(ref replayed);
                    fresh=new CertifiedRouteWorker(Binding(),1,restoredKernel);
                    // Observe worker publication before applying it. Empty ApplyResults in a
                    // tight spin is not the authority tick schedule and continually invalidates admission.
                    Assert.True(SpinWait.SpinUntil(()=>{if(session.HasReadyArrival||session.Answers.Count>0)session.ApplyResults();return session.PendingCount==0;},5000),fresh.Publication.Failure??"fresh worker must wake and resume without a new command or manual Pump");
                    Assert.Greater(fresh.Publication.Pumps,0);Assert.AreEqual(1,session.AppliedResults);Assert.Zero(replayed);Assert.True(session.Crowd.Units.Single(actor=>actor.Id==id).Moving);Assert.Null(fresh.Publication.Failure);
                    fresh.Dispose();Assert.True(SpinWait.SpinUntil(()=>fresh.IsStopped,5000));Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
                    Call("DestroyOwnedNavigation");Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);session=null;domain=null;kernel=null;
                }finally{old?.Dispose();fresh?.Dispose();resume.Set();if(old!=null)Assert.True(SpinWait.SpinUntil(()=>old.IsStopped,5000));if(fresh!=null)Assert.True(SpinWait.SpinUntil(()=>fresh.IsStopped,5000));if(session!=null)session.BeforeArrivalAdvanceCommit=null;if(capture!=null)Assert.True(capture.Wait(5000));}
            }
        }

        [Test] public void OneAuthorityCannotBeDrivenByTwoCertifiedHosts()
        {
            int a=Spawn(-18,-14);
            Assert.True(session.Move(a,new NavPoint(-12,-14)));
            session.PrepareDeliveryBarrier();
            kernel.Continue=()=>false;
            Pump(1);
            var second=new CertifiedRouteKernel();
            Assert.Throws<InvalidOperationException>(()=>second.Pump(Binding(),1));
            Assert.AreEqual(1,session.Requests.Count,"the rejected host must not dequeue the original request");
            kernel.Clear();
            second.Continue=()=>false;
            Assert.DoesNotThrow(()=>second.Pump(Binding(),1),"released authority permits the next host");
            second.Clear();
        }

        [Test] public void IdleWorkerClaimsAuthorityBeforeItsFirstPumpAndReleasesOnStop()
        {
            var binding=Binding();
            var worker=new CertifiedRouteWorker(binding,1);
            try{
                Assert.True(SpinWait.SpinUntil(()=>session.HasCertifiedHost&&worker.IsWaiting&&worker.Publication.Pumps==0,5000));
                var second=new CertifiedRouteKernel();
                Assert.Throws<InvalidOperationException>(()=>second.Pump(binding,1));
                Assert.Zero(worker.Publication.Pumps);
                second.Clear();
            }finally{worker.Dispose();Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));}
            Assert.DoesNotThrow(()=>kernel.Pump(binding,1));
        }
        [Test,Timeout(180000)] public void V2FarFoundationKeepsWholeInstalledGroupAndExactColdNextWithAtomicRefusal()
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-10);Assert.True(session.MoveGroup(new[]{a,b},new[]{new NavPoint(-10,-14),new NavPoint(-10,-10)}));
            for(int i=0;i<20000&&session.PendingCount>0;i++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);
            for(int i=0;i<30;i++)session.Step(1d/30);
            Assert.AreEqual(2,session.GroupFor(a).MarchVersion);var group=session.GroupFor(a);var positions=new[]{a,b}.Select(id=>session.Crowd.Units.Single(u=>u.Id==id).Position).ToArray();
            var requests=new[]{a,b}.Select(id=>session.InstalledExecutionFor(id).Corridor.Request).ToArray();var budget=session.PhysicalHostBudget;
            Call("AddCredits",PlayableOwner.Enemy,10000d);Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",2,1,PlayableBuildingKind.Refinery,2,PlayableOwner.Enemy,null));
            var before=Bytes();long charge=budget.PhysicalBytes;int denied=0,underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(session);
            try{budget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(InstalledGeometryCertificateV2))){denied++;return false;}return true;};Call("AdvanceFoundations");}finally{budget.AllocationAdmission=null;}
            Assert.Greater(denied,0,"The actual foundation path must reach certificate preparation.");Assert.Zero(underGate);Assert.AreEqual(charge,budget.PhysicalBytes);CollectionAssert.AreEqual(before,Bytes());
            Call("AdvanceFoundations");Assert.Zero(session.LastOwnedRebindPublicationAllocatedBytes);Assert.Zero(session.PendingCount);
            for(int i=0;i<2;i++){int id=i==0?a:b;Assert.NotNull(session.InstalledExecutionFor(id));Assert.AreEqual(requests[i],session.InstalledExecutionFor(id).Corridor.Request);Assert.True(session.Crowd.Units.Single(u=>u.Id==id).Moving);Assert.AreEqual(positions[i],session.Crowd.Units.Single(u=>u.Id==id).Position);}
            foreach(var m in group.Members){var now=session.GroupFor(a).Members.Single(n=>n.Entity==m.Entity);Assert.AreEqual(m.Progress,now.Progress);Assert.AreEqual(m.ProgressOffset,now.ProgressOffset);Assert.AreEqual(m.ProgressRequest,now.ProgressRequest);}
            var prefix=Bytes();session.Step(1d/30);var next=Bytes();Restore(prefix);CollectionAssert.AreEqual(prefix,Bytes());session.Step(1d/30);CollectionAssert.AreEqual(next,Bytes());
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [Test,Timeout(180000)] public void V2CertificateRejectsIntersectingRemoveReplaceAndWrongGroupInputs()
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-10);Assert.True(session.MoveGroup(new[]{a,b},new[]{new NavPoint(-10,-14),new NavPoint(-10,-10)}));
            for(int i=0;i<20000&&session.PendingCount>0;i++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);
            Call("AddCredits",PlayableOwner.Enemy,10000d);Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",2,1,PlayableBuildingKind.Refinery,2,PlayableOwner.Enemy,null));Call("AdvanceFoundations");
            var installed=(NavigationInstalledExecutionOwner)session.Crowd.Units.Single(u=>u.Id==a).StorageOwner.InstalledExecution;Assert.NotNull(installed.Certificate);var proof=installed.Certificate;var old=proof.Before;var far=proof.Current;
            Assert.True(InstalledGeometryCertificateV2.Safe(proof.Group,old,far,proof.Members));
            NavGeometry Next(params NavObstacle[] rows)=>new NavGeometry(far.HalfExtent,rows,far.Revision+1);
            foreach(var p in new[]{new NavPoint(-14,-14),installed.Corridor.Corridor.Origin.Position,installed.Corridor.Corridor.Endpoint.Position})Assert.False(InstalledGeometryCertificateV2.Safe(proof.Group,far,Next(far.Obstacles.Concat(new[]{new NavObstacle(p.X-.1,p.Z-.1,p.X+.1,p.Z+.1)}).ToArray()),proof.Members),"Every group route, origin and endpoint is covered.");
            Assert.False(InstalledGeometryCertificateV2.AddOnly(far,Next(far.Obstacles.Skip(1).ToArray())));
            Assert.False(InstalledGeometryCertificateV2.AddOnly(far,Next(far.Obstacles.Skip(1).Concat(new[]{new NavObstacle(20,10,21,11),new NavObstacle(22,10,23,11)}).ToArray())));
            Assert.False(InstalledGeometryCertificateV2.AddOnly(far,Next(far.Obstacles.Concat(new[]{new NavObstacle(new NavPoint(20,10),1)}).ToArray())));
            Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,session.Generation,0,out var scope));using(scope){
                var r=scope.OwnedReservation();var changed=session.GroupFor(a);changed.MembershipRevision++;var wrong=NavigationAuthorityGroup.CopyWithValuesOwned(proof.Group,new NavigationAuthorityGroup.Values(changed),r);try{Assert.False(InstalledGeometryCertificateV2.Safe(wrong,old,far,proof.Members));}finally{wrong.ReleaseStorage();}
                var original=(NavigationAuthorityMember)proof.Group.Members[0];var v=original.Data;var endpoint=new NavigationAuthorityMember.Values(v.Entity,v.Slot,v.Incarnation,v.OrderRevision,v.MobilityRevision,v.Phase,new NavPoint(v.Goal.X+1,v.Goal.Z),v.Location,v.HasLocation,v.HasGoal,v.FormationReleased,v.Branch,v.Reservation,v.ContactTick,v.ProgressRequest,v.Progress,v.ProgressVersion,v.ProgressOffset);var member=NavigationAuthorityMember.CopyWithValuesOwned(original,endpoint,r);try{wrong=NavigationAuthorityGroup.CopyWithMemberOwned(proof.Group,0,member,r);try{Assert.False(InstalledGeometryCertificateV2.Safe(wrong,old,far,proof.Members));}finally{wrong.ReleaseStorage();}}finally{member.ReleaseStorage();}
            }
            var before=Bytes();session.Stop(a,true);Assert.Null(session.InstalledExecutionFor(a));Assert.True(session.Crowd.Units.Single(u=>u.Id==a).Held);Assert.False(before.SequenceEqual(Bytes()));
            // A later HOLD is an existing reissue boundary, not a new geometry
            // certificate. Its pending transition must still save exactly.
            var held=Bytes();session.Step(1d/30);var heldNext=Bytes();Restore(held);CollectionAssert.AreEqual(held,Bytes());session.Step(1d/30);CollectionAssert.AreEqual(heldNext,Bytes());
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
        }

        [Test,Timeout(180000)] public void V2ChangedProfileRevisionRejectsFoundationCarryAndSavesFallbackExactly()
        {
            int a=Spawn(-18,-14),b=Spawn(-18,-10);Assert.True(session.MoveGroup(new[]{a,b},new[]{new NavPoint(-10,-14),new NavPoint(-10,-10)}));
            for(int i=0;i<20000&&session.PendingCount>0;i++){Pump(4);session.ApplyResults();}Assert.Zero(session.PendingCount);
            var data=activeProfile.CopyData();data.revision++;activeProfile=PlayableProfile.Create(data,activeProfile.AuthoredMap);Call("ApplyBalance",activeProfile);
            Assert.NotNull(session.InstalledExecutionFor(a),"Existing compatible profile apply keeps its established route policy.");
            var old=(NavigationInstalledExecutionOwner)session.Crowd.Units.Single(u=>u.Id==a).StorageOwner.InstalledExecution;Assert.AreNotEqual(activeProfile.Navigation.Revision,old.Request.Request.ProfileRevision);
            Call("AddCredits",PlayableOwner.Enemy,10000d);Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",2,1,PlayableBuildingKind.Refinery,2,PlayableOwner.Enemy,null));Call("AdvanceFoundations");
            Assert.Null(session.InstalledExecutionFor(a),"A new geometry certificate cannot cross a profile revision identity change.");Assert.Null(session.InstalledExecutionFor(b));Assert.Greater(session.PendingCount,0);
            var prefix=Bytes();Pump(1);var next=Bytes();Restore(prefix);CollectionAssert.AreEqual(prefix,Bytes());Pump(1);CollectionAssert.AreEqual(next,Bytes());
        }

    }
}
