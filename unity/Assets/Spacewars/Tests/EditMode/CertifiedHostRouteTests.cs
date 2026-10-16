using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CertifiedHostRouteTests
    {
        [Test] public void ZeroEntityAndZeroOrderProbeKeepCertifiedProvenanceAcrossCheckpoint()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(0,new NavPoint(-5,0));
            Assert.True(source.Move(0,new NavPoint(5,0)));
            Assert.NotNull(source.Probe(7,0,PlayableProfile.Default.Navigation,
                new NavPoint(-4,2),new NavPoint(4,2)));
            var bytes=WorldWire.Pack(w=>WorldWire.Write(w,source.CaptureState()));
            var state=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
            var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),
                PlayableProfile.Default.Navigation);
            restored.RestoreState(state);
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,restored.Admission,restored.RoutePort);
                for(int i=0;i<40;i++){host.Service(binding,4);restored.ApplyResults();}
            }
            Assert.AreEqual(1,restored.AppliedResults);
            Assert.AreEqual(0,restored.InstalledExecutionFor(0).Corridor.Entity);
            Assert.True(restored.TryProbeAnswer(out var probe));
            Assert.AreEqual(NavRouteProvenance.ProducerProbe,probe.Corridor.Provenance);
            Assert.AreEqual(0,probe.Corridor.Order);
        }

        [Test] public void OldNullMemberIdentityUsesExplicitLegacyCorridorFallback()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(0,new NavPoint(-5,0));
            Assert.True(source.Move(0,new NavPoint(5,0)));
            var state=source.CaptureState();
            Assert.AreEqual(1,state.Requests.Length);
            state.Requests[0].MemberIdentity=null; // supported pre-A1 checkpoint mode
            var bytes=WorldWire.Pack(w=>WorldWire.Write(w,state));
            var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),
                PlayableProfile.Default.Navigation);
            restored.RestoreState(WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,restored.Admission,restored.RoutePort);
                for(int i=0;i<40&&restored.PendingCount>0;i++){host.Service(binding,4);restored.ApplyResults();}
            }
            Assert.AreEqual(1,restored.AppliedResults);
            Assert.Null(restored.InstalledExecutionFor(0));
            Assert.AreEqual(0,restored.CaptureState().InstalledExecutions.Single().Corridor.Group);
            var replay=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),
                PlayableProfile.Default.Navigation);
            replay.RestoreState(WorldWire.Unpack(WorldWire.Pack(w=>WorldWire.Write(w,restored.CaptureState())),
                WorldWire.ReadNavigationSessionState));
            Assert.AreEqual(NavigationOutcome.Moving,replay.Crowd.Units.Single().Outcome);
        }

        [Test] public void MalformedCertifiedBindingCannotActivateReplacement()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(session.Move(1,new NavPoint(5,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                for(int i=0;i<40&&session.PendingCount>0;i++){host.Service(binding,4);session.ApplyResults();}
            }
            Assert.AreEqual(1,session.AppliedResults);
            var prior=session.InstalledExecutionFor(1);
            Assert.NotNull(prior);
            int activated=0;session.RouteActivated+=_=>activated++;
            Assert.True(session.Move(1,new NavPoint(7,1)));
            Assert.True(session.Requests.TryDequeue(out var request));
            var group=session.GroupFor(1);
            var origin=request.StartLocation.Value;var endpoint=request.GoalLocation.Value;
            var leg=new NavCorridorLeg(origin,endpoint,NavTypedLegKind.Surface,null,
                Math.Sqrt(Math.Pow(origin.Position.X-endpoint.Position.X,2)+
                    Math.Pow(origin.Position.Z-endpoint.Position.Z,2)),
                NavCorridorSection.IndividualConnector,0,0);
            var forged=new NavCorridorDescriptor(1,new string('0',64),new string('0',64),
                NavigationAdmission.ExactProfileBinding(request.Profile),NavLocation.FlatSurface,
                request.HeldIdentity,request.BaseGeometry.Revision,request.Profile.Radius,request.Profile.GridCell,
                NavRouteProvenance.CommandMember,request.Session,group.GroupId,group.OrderRevision,
                group.CommandSequence,group.IssuedTick,request.MemberIdentity.OrderRevision,
                request.Entity,request.MemberIdentity.Incarnation,request.MemberIdentity.MobilityRevision,
                request.Request,request.Order,origin,endpoint,new[]{leg});
            Assert.True(session.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal},
                NavSolveStatus.Ready,forged)));
            session.ApplyResults();
            Assert.AreEqual(0,activated);
            Assert.AreEqual(1,session.AppliedResults);
            Assert.True(session.Crowd.Units.Single().Moving);
            Assert.AreEqual(prior.Corridor.Request,session.InstalledExecutionFor(1).Corridor.Request);
        }

        [Test] public void TamperedReadyMailboxCorridorIsRejectedBeforeRestorePublication()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(source.Move(1,new NavPoint(5,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,source.Admission,source.RoutePort);
                for(int i=0;i<40&&source.Answers.Count==0;i++)host.Service(binding,4);
            }
            var state=source.CaptureState();
            Assert.AreEqual(1,state.AnswerMailbox.Length);
            Assert.NotNull(state.AnswerMailbox[0].Corridor);
            state.AnswerMailbox[0].Route=new[]{new NavPoint(1,1)};
            var target=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),
                PlayableProfile.Default.Navigation);
            Assert.Throws<ArgumentException>(()=>target.RestoreState(state));
        }

        [Test] public void StrippedCertifiedSidecarCannotActivateThroughPublicAnswerMailbox()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(session.Move(1,new NavPoint(5,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                for(int i=0;i<40&&session.Answers.Count==0;i++)host.Service(binding,4);
            }
            Assert.True(session.Answers.TryDequeue(out var certified));
            Assert.NotNull(certified.Corridor);
            Assert.True(session.Answers.TryEnqueue(new NavigationAnswer(certified.Request,
                certified.CopyRoute(),NavSolveStatus.Ready)));
            session.ApplyResults();
            Assert.AreEqual(0,session.AppliedResults);
            Assert.False(session.Crowd.Units.Single().Moving);
            Assert.Null(session.InstalledExecutionFor(1));
        }

        [Test] public void HostWorkQuantumWaitsForAuthorityRetentionGate()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(session.Move(1,new NavPoint(5,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                var held=new ManualResetEventSlim(false);
                var hostCalling=new ManualResetEventSlim(false);
                var worker=Task.Run(()=>session.RoutePort.RunHostStep(()=>{
                    held.Set();
                    Assert.True(hostCalling.Wait(TimeSpan.FromSeconds(5)));
                    Thread.Sleep(100); // Give the main-thread host a chance to contend.
                    Assert.AreEqual(1,session.Requests.Count);
                }));
                Assert.True(held.Wait(TimeSpan.FromSeconds(5)));
                hostCalling.Set();
                host.Service(binding,4);
                Assert.True(worker.Wait(TimeSpan.FromSeconds(10)));
                for(int i=0;i<40&&session.PendingCount>0;i++){host.Service(binding,4);session.ApplyResults();}
                Assert.AreEqual(1,session.AppliedResults);
            }
        }

        [Test] public void ProbeDequeueKeepsCorridorRetentionUnderAuthorityGate()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            Assert.NotNull(session.Probe(7,0,PlayableProfile.Default.Navigation,
                new NavPoint(-4,2),new NavPoint(4,2)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                for(int i=0;i<40&&session.CorridorRetainedBytes==0;i++){host.Service(binding,4);session.ApplyResults();}
                Assert.Greater(session.CorridorRetainedBytes,0);
                long before=session.CorridorRetainedBytes;
                var started=new ManualResetEventSlim(false);
                var finished=new ManualResetEventSlim(false);
                NavigationAnswer dequeued=null;bool received=false;
                Task worker=null;
                session.RoutePort.RunHostStep(()=>{
                    worker=Task.Run(()=>{started.Set();received=session.TryProbeAnswer(out dequeued);finished.Set();});
                    Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
                    Assert.False(finished.Wait(TimeSpan.FromMilliseconds(100)));
                    Assert.AreEqual(before,session.CorridorRetainedBytes);
                });
                Assert.True(worker.Wait(TimeSpan.FromSeconds(10)));
                Assert.True(received);Assert.NotNull(dequeued.Corridor);
                Assert.Less(session.CorridorRetainedBytes,before);
            }
        }

        internal static NavigationSession LegacyCheckpointSession(NavGeometry geometry,PlayableProfile profile,IPlayableTerrain terrain=null)
            =>LegacyCheckpointSession(geometry,profile.Navigation,terrain);
        internal static NavigationSession LegacyCheckpointSession(NavGeometry geometry,NavigationProfile profile,IPlayableTerrain terrain=null)
        {
            // Explicit historical A5-v8 / ARR3 empty input, never reflection-switch
            // a current physical Session into destructive replay mode.
            var source=new NavigationSession(1,geometry,profile,terrain:terrain);
            try{
                using(var state=source.CaptureState()){
                    state.HostAuthority=null;state.RegisteredRequestIndices=state.DeliveredRequestIndices=Array.Empty<int>();state.AllocationRequestIndices=Array.Empty<int>();
                    state.ArrivalAuthority=WorldWire.Pack(w=>{w.Write(0x41525231);w.Write(3);for(int i=0;i<7;i++)w.Write(0L);for(int i=0;i<6;i++)w.Write(0);});
                    var bytes=WorldWire.Pack(w=>WorldWire.Write(w,state));using(var saved=WorldWire.Unpack(bytes,r=>WorldWire.ReadNavigationSessionState(r,NavigationV8Format.A5Arrival))){var target=new NavigationSession(1,geometry,profile,terrain:terrain);try{target.RestoreState(saved);return target;}catch{target.DestroyNavigationAuthority();throw;}}
                }
            }finally{source.DestroyNavigationAuthority();}
        }
        [Test] public void PhysicalCheckpointReleaseRejectsBeforeCallbackAndPreservesExactColdNextPump()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var profile=PlayableProfile.Default;var source=new NavigationSession(1,geometry,profile.Navigation);NavigationSession cold=null;var host=new UnityHostRouteService();var coldHost=new UnityHostRouteService();
            try{
                source.Crowd.Add(1,new NavPoint(-5,0));Assert.True(source.Move(1,new NavPoint(5,0)));var binding=new PlayableRouteBinding(geometry,profile,source.Admission,source.RoutePort);var kernel=(CertifiedRouteKernel)typeof(UnityHostRouteService).GetField("certified",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(host);kernel.PauseAtPhysicalBoundary=()=>source.RegisteredRouteCount>0;host.Service(binding,1);Assert.Greater(source.RegisteredRouteCount,0);kernel.PauseAtPhysicalBoundary=null;
                byte[] Bytes(NavigationSession s)=>WorldWire.Pack(w=>WorldWire.Write(w,s.CaptureState()));var before=Bytes(source);var root=source.PinCommittedHostRoot();Assert.NotNull(root);int callbacks=0;
                try{Assert.Throws<InvalidOperationException>(()=>source.RoutePort.ReleaseHostForWorldCheckpoint(()=>callbacks++));Assert.Zero(callbacks);Assert.Throws<InvalidOperationException>(()=>host.ReleaseForWorldCheckpoint(binding));CollectionAssert.AreEqual(before,Bytes(source));var current=source.PinCommittedHostRoot();try{Assert.AreSame(root,current);}finally{current.Release();}}finally{root.Release();}
                host.Service(binding,1);var after=Bytes(source);host.Dispose();cold=new NavigationSession(1,geometry,profile.Navigation);using(var saved=WorldWire.Unpack(before,WorldWire.ReadNavigationSessionState))cold.RestoreState(saved);CollectionAssert.AreEqual(before,Bytes(cold));coldHost.Service(new PlayableRouteBinding(geometry,profile,cold.Admission,cold.RoutePort),1);CollectionAssert.AreEqual(after,Bytes(cold));Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(cold),cold.PhysicalHostBudget.PhysicalBytes);
            }finally{host.Dispose();coldHost.Dispose();cold?.DestroyNavigationAuthority();source.DestroyNavigationAuthority();if(cold!=null)Assert.Zero(cold.PhysicalHostBudget.PhysicalBytes);Assert.Zero(source.PhysicalHostBudget.PhysicalBytes);}
        }

        private static PlayableRouteBinding CheckpointFixture(out NavigationSession session)
        {
            var map=ThreeCrossingsMap.Default;var profile=PlayableProfile.ThreeCrossingsDefault;
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            session=LegacyCheckpointSession(geometry,profile,map);
            session.Crowd.Add(1,new NavPoint(-20,18),.58,4,4.5);
            session.Crowd.Add(2,new NavPoint(-18,18),.58,4,4.5);
            Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(18,18),new NavPoint(20,18)}));
            return new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
        }
        private static object[] CheckpointLedger(UnityHostRouteService host)
        {
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var kernel=typeof(UnityHostRouteService).GetField("certified",flags).GetValue(host);
            var scheduler=typeof(CertifiedRouteKernel).GetField("scheduler",flags).GetValue(kernel);
            return new[]{typeof(CertifiedRouteKernel).GetField("epoch",flags).GetValue(kernel)}.Concat(
                new[]{"lastEpoch","graphQuota","fieldQuota","connectorQuota","bookkeepingQuota","maintenanceQuota"}
                .Select(name=>typeof(LayeredNavigationScheduler).GetField(name,flags).GetValue(scheduler))).ToArray();
        }
        private static Action<NavigationSessionState> LegacyReplayValueAssertion(NavigationSession source)
        {
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            object Field(object value,string name)=>value.GetType().GetField(name,flags).GetValue(value);
            object[] Obstacles(System.Collections.Generic.IEnumerable<NavObstacle> obstacles)=>obstacles.Select(value=>new object[]{value.MinX,value.MinZ,value.MaxX,value.MaxZ,value.CircleRadius,value.Bottom,value.Top,value.Polygon?.Vertices.ToArray()}).Cast<object>().ToArray();
            object Geometry(NavGeometry geometry)=>new object[]{geometry.HalfExtent,geometry.Revision,Obstacles(geometry.Obstacles)};
            object SavedGeometry(NavigationGeometryState geometry)=>new object[]{geometry.HalfExtent,geometry.Revision,Obstacles(geometry.Obstacles)};
            string Key(NavigationRequest request)=>request.Session+":"+request.Request+":"+request.Entity;
            string SavedKey(NavigationRequestState row)=>row.Session+":"+row.Request+":"+row.Entity;
            var expected=new System.Collections.Generic.SortedDictionary<string,byte[]>();
            void Put(string key,object value)=>expected.Add(key,PlayableWorldRestoreTests.FactBytes(value));
            var pending=(System.Collections.Generic.IDictionary<int,NavigationRequest>)Field(source,"pending");var queued=source.Requests.CopyItems();var requests=pending.Values.Concat(queued).Distinct().OrderBy(Key).ToArray();Assert.AreEqual(requests.Length,requests.Select(Key).Distinct().Count());
            Put("session",new object[]{source.Generation,Field(source,"requestSequence"),Field(source,"groupSequence"),source.AppliedResults,source.RejectedResults,source.UnreachableResults});
            Put("requests",requests.Select(request=>new object[]{request.Session,request.Request,request.Entity,request.Order,request.Start,request.Goal,request.StartLocation,request.GoalLocation,request.ProfileId,request.ProfileRevision,NavigationProfile.Metadata.Select(field=>field.Read(request.Profile)).ToArray(),request.HeldIdentity,request.CertifiedProviderBinding,request.MemberIdentity,Geometry(request.Geometry),Geometry(request.BaseGeometry)}).ToArray());
            var baseGeometry=(NavGeometry)Field(source,"geometry");var navigationGeometry=(NavGeometry)Field(source,"navigationGeometry");Put("geometry",Geometry(baseGeometry));Put("navigationGeometry",Geometry(navigationGeometry));
            var roots=new[]{baseGeometry,navigationGeometry}.Concat(requests.SelectMany(row=>new[]{row.Geometry,row.BaseGeometry})).ToArray();Put("geometryAliases",roots.SelectMany(first=>roots.Select(second=>ReferenceEquals(first,second))).ToArray());Put("geometryCount",roots.Distinct().Count());
            Put("FIFO",queued.Select(Key).ToArray());Put("pending",pending.Select(pair=>new object[]{pair.Key,Key(pair.Value)}).ToArray());foreach(var input in queued)Assert.AreSame(input,pending[input.Entity]);
            var orders=(System.Collections.Generic.IDictionary<int,long>)Field(source,"orders");Put("orders",orders.Select(pair=>new object[]{pair.Key,pair.Value}).ToArray());
            foreach(var name in new[]{"reservations","retainedGoals"}){var values=(System.Collections.Generic.IDictionary<int,NavPoint>)Field(source,name);Put(name,values.Select(pair=>new object[]{pair.Key,pair.Value}).ToArray());}
            var identities=(System.Collections.Generic.IDictionary<int,string>)Field(source,"movementIdentities");Put("identities",identities.Select(pair=>new object[]{pair.Key,pair.Value}).ToArray());
            var groups=(System.Collections.Generic.IDictionary<long,GroupOrderState>)Field(source,"groups");Put("groups",groups.Values.ToArray());
            Assert.Zero(source.RegisteredRouteCount);Assert.Zero(((System.Collections.Generic.IDictionary<int,NavigationRequest>)Field(source,"allocating")).Count);Assert.Zero(((System.Collections.Generic.IDictionary<int,NavigationRequest>)Field(source,"probes")).Count);Assert.Zero((int)typeof(NavigationSession).GetProperty("ProbeAnswerCount",flags).GetValue(source));Assert.Zero(source.Answers.Count);Assert.Zero((int)Field(source,"barrierAnswers").GetType().GetProperty("Count",flags|System.Reflection.BindingFlags.Public).GetValue(Field(source,"barrierAnswers")));Assert.Zero(((System.Collections.Generic.IDictionary<int,NavSolveStatus>)Field(source,"technicalFailures")).Count);Assert.Zero(((NavigationInstalledExecutions)Field(source,"installedExecutions")).Count);
            var crowd=source.Crowd;var crowdProfile=(NavigationProfile)Field(crowd,"profile");var crowdGeometry=(NavGeometry)Field(crowd,"geometry");
            Put("crowdHeaders",new object[]{Field(crowd,"nextIncarnation"),crowdGeometry.HalfExtent,Field(crowd,"maximumRadius"),crowdGeometry.Revision,crowdProfile.ProfileId,crowdProfile.Revision,NavigationProfile.Metadata.Select(field=>field.Read(crowdProfile)).ToArray(),crowd.RepairCount,crowd.RepairFailed,crowd.MaxNoProgressTicks,Obstacles(crowdGeometry.Obstacles)});
            foreach(var actor in crowd.Units){foreach(var field in typeof(NavUnitSaveState).GetFields()){var property=typeof(NavUnit).GetProperty(field.Name);object value=field.Name=="Route"?actor.route:property!=null?property.GetValue(actor):Field(actor,char.ToLowerInvariant(field.Name[0])+field.Name.Substring(1));Put("actor."+actor.Id+"."+field.Name,value);}}
            return saved=>{
                var actual=new System.Collections.Generic.SortedDictionary<string,byte[]>();void Save(string key,object value)=>actual.Add(key,PlayableWorldRestoreTests.FactBytes(value));
                Save("session",new object[]{saved.Generation,saved.RequestSequence,saved.GroupSequence,saved.AppliedResults,saved.RejectedResults,saved.UnreachableResults});
                var rows=saved.Requests.OrderBy(SavedKey).ToArray();Assert.AreEqual(rows.Length,rows.Select(SavedKey).Distinct().Count());
                Save("requests",rows.Select(row=>new object[]{row.Session,row.Request,row.Entity,row.Order,row.Start,row.Goal,row.StartLocation,row.GoalLocation,row.ProfileId,row.ProfileRevision,row.ProfileValues,row.HeldIdentity,row.CertifiedProviderBinding,row.MemberIdentity,SavedGeometry(row.Geometry),SavedGeometry(row.BaseGeometry)}).ToArray());
                Save("geometry",SavedGeometry(saved.Geometry));Save("navigationGeometry",SavedGeometry(saved.NavigationGeometry));var indices=new[]{0,saved.NavigationGeometryIndex}.Concat(rows.SelectMany(row=>new[]{row.GeometryIndex,row.BaseGeometryIndex})).ToArray();Save("geometryAliases",indices.SelectMany(first=>indices.Select(second=>first==second)).ToArray());Save("geometryCount",saved.GeometryTable.Length);
                Save("FIFO",saved.RequestMailboxIndices.Select(index=>SavedKey(saved.Requests[index])).ToArray());Save("pending",saved.PendingRequestIndices.Select(index=>new object[]{saved.Requests[index].Entity,SavedKey(saved.Requests[index])}).ToArray());
                Save("orders",saved.Orders.Select(row=>new object[]{row.Entity,row.Order}).ToArray());Save("reservations",saved.Reservations.Select(row=>new object[]{row.Entity,row.Point}).ToArray());Save("retainedGoals",saved.RetainedGoals.Select(row=>new object[]{row.Entity,row.Point}).ToArray());Save("identities",saved.MovementIdentities.Select(row=>new object[]{row.Entity,row.Identity}).ToArray());Save("groups",saved.Groups);
                Assert.Null(saved.RegisteredRequestIndices);Assert.Null(saved.DeliveredRequestIndices);Assert.IsEmpty(saved.AllocationRequestIndices);Assert.IsEmpty(saved.ProbeRequestIndices);Assert.IsEmpty(saved.ProbeAnswers);Assert.IsEmpty(saved.AnswerMailbox);Assert.IsEmpty(saved.BarrierAnswers);Assert.IsEmpty(saved.TechnicalFailures);Assert.IsEmpty(saved.InstalledExecutions);
                var state=saved.Crowd;Save("crowdHeaders",new object[]{state.NextIncarnation,state.HalfExtent,state.MaximumRadius,state.GeometryRevision,state.ProfileId,state.ProfileRevision,state.ProfileValues,state.RepairCount,state.RepairFailed,state.MaxNoProgressTicks,Obstacles(state.Obstacles)});
                foreach(var actor in state.Units)foreach(var field in typeof(NavUnitSaveState).GetFields())Save("actor."+actor.Id+"."+field.Name,field.GetValue(actor));
                CollectionAssert.AreEqual(expected.Keys,actual.Keys);foreach(var pair in expected)CollectionAssert.AreEqual(pair.Value,actual[pair.Key],"Independent pre-capture legacy semantic value: "+pair.Key);
            };
        }
        [Test] public void WorldCheckpointReleaseReplaysRegisteredAndQueuedInputsAndRebindsWithoutQuotaRefill()
        {
            var binding=CheckpointFixture(out var source);var port=source.RoutePort;NavigationSession restored=null,independent=null;
            try{
            using(var host=new UnityHostRouteService()){
                host.Service(binding,1);
                Assert.AreEqual(1,source.RegisteredRouteCount);Assert.AreEqual(1,source.Requests.Count);
                Assert.AreEqual(2,source.PendingCount);Assert.Greater(source.HostRetainedBytes,0);
                const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var held=((List<NavigationRequest>)typeof(NavigationSession).GetField("registeredRoutes",flags).GetValue(source)).Single();
                var queued=source.Requests.CopyItems().Single();var ledger=CheckpointLedger(host);
                var counters=host.CertifiedCounters;
                host.ReleaseForWorldCheckpoint(binding);
                Assert.Zero(source.HostRetainedBytes);Assert.Zero(host.CertifiedRetainedBytes);
                Assert.Zero(source.RegisteredRouteCount);Assert.AreEqual(2,source.PendingCount);
                var replay=source.Requests.CopyItems();Assert.AreEqual(2,replay.Length);
                Assert.AreSame(held,replay[0]);Assert.AreSame(queued,replay[1]);Assert.AreSame(port,source.RoutePort);
                Assert.False(source.TryCompleteRegisteredRoute(new NavigationAnswer(held,Array.Empty<NavPoint>(),NavSolveStatus.Stale)),"Released old registration cannot complete twice");
                CollectionAssert.AreEqual(ledger,CheckpointLedger(host),"Real release must retain all remaining epoch quotas");
                Assert.AreEqual(counters.LogicalGraphWork,host.CertifiedCounters.LogicalGraphWork);
                Assert.AreEqual(counters.LogicalFieldWork,host.CertifiedCounters.LogicalFieldWork);
                var assertLegacy=LegacyReplayValueAssertion(source);byte[] bytes;using(var captured=source.CaptureState())bytes=WorldWire.Pack(w=>WorldWire.Write(w,captured));
                using(var state=WorldWire.Unpack(bytes,r=>WorldWire.ReadNavigationSessionState(r,NavigationV8Format.A5Arrival)))assertLegacy(state);
                NavigationSession Import(){
                    var geometry=new NavGeometry(binding.Geometry.HalfExtent,binding.Geometry.Obstacles.ToArray(),binding.Geometry.Revision);
                    var candidate=new NavigationSession(1,geometry,binding.Profile.Navigation,terrain:ThreeCrossingsMap.Default);try{using(var state=WorldWire.Unpack(bytes,r=>WorldWire.ReadNavigationSessionState(r,NavigationV8Format.A5Arrival)))candidate.RestoreState(state);return candidate;}catch{candidate.DestroyNavigationAuthority();throw;}
                }
                restored=Import();independent=Import();Assert.AreNotSame(port,restored.RoutePort);Assert.AreNotSame(restored.RoutePort,independent.RoutePort);
                AssertInstalledFacts(restored,independent,"Every primary owner/alias/layout field agrees between independent v8 migration endpoints");
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(restored),restored.PhysicalHostBudget.PhysicalBytes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(independent),independent.PhysicalHostBudget.PhysicalBytes);
                var next=new PlayableRouteBinding(restored.Admission.Geometry,binding.Profile,restored.Admission,restored.RoutePort);
                CollectionAssert.AreEqual(ledger,CheckpointLedger(host),"Facade rebind performs no solver work");
                for(int i=0;i<4096&&restored.PendingCount>0;i++){host.Service(next,8);restored.ApplyResults();}
                Assert.Zero(restored.PendingCount);Assert.AreEqual(2,restored.AppliedResults);Assert.Zero(restored.RejectedResults);
                Assert.Zero(source.Answers.Count);Assert.AreEqual(2,source.PendingCount,"Rebound host cannot answer its old session");
                Assert.GreaterOrEqual(host.SolverEpoch,(long)ledger[0]);
            }
            }finally{independent?.DestroyNavigationAuthority();restored?.DestroyNavigationAuthority();source.DestroyNavigationAuthority();if(independent!=null)Assert.Zero(independent.PhysicalHostBudget.PhysicalBytes);if(restored!=null)Assert.Zero(restored.PhysicalHostBudget.PhysicalBytes);Assert.Zero(source.PhysicalHostBudget.PhysicalBytes);}
        }
        [Test] public void WorldCheckpointReleaseRejectsWrongOwnerAndLostInputBeforeDroppingHostWork()
        {
            var binding=CheckpointFixture(out var source);NavigationSession otherSession=null;
            try{
            using(var host=new UnityHostRouteService()){
                host.Service(binding,1);Assert.AreEqual(1,source.RegisteredRouteCount);
                var other=CheckpointFixture(out otherSession);var before=PlayableWorldRestoreTests.Facts(source,true);var ledger=CheckpointLedger(host);long memory=host.CertifiedRetainedBytes;
                Assert.Throws<InvalidOperationException>(()=>host.ReleaseForWorldCheckpoint(other));
                Assert.AreEqual(before,PlayableWorldRestoreTests.Facts(source,true));Assert.AreEqual(memory,host.CertifiedRetainedBytes);
                Assert.True(source.Requests.TryDequeue(out var lost));before=PlayableWorldRestoreTests.Facts(source,true);
                Assert.Throws<InvalidOperationException>(()=>host.ReleaseForWorldCheckpoint(binding));
                Assert.AreEqual(before,PlayableWorldRestoreTests.Facts(source,true));Assert.AreEqual(memory,host.CertifiedRetainedBytes);
                CollectionAssert.AreEqual(ledger,CheckpointLedger(host));Assert.AreEqual(1,source.RegisteredRouteCount);
                Assert.True(source.Requests.TryEnqueue(lost));
            }
            }finally{otherSession?.DestroyNavigationAuthority();source.DestroyNavigationAuthority();if(otherSession!=null)Assert.Zero(otherSession.PhysicalHostBudget.PhysicalBytes);Assert.Zero(source.PhysicalHostBudget.PhysicalBytes);}
        }
        [TestCase(false)][TestCase(true)] public void WorldCheckpointNearCapReservationUsesRealReleasedMemory(bool releaseBeforeApply)
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var profile=PlayableProfile.Default;
            var session=LegacyCheckpointSession(geometry,profile);session.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(session.Move(1,new NavPoint(5,0)));
            var host=new UnityHostRouteService();var port=session.RoutePort;var memory=IntPtr.Zero;
            try{
                var binding=new PlayableRouteBinding(geometry,profile,session.Admission,port);
                for(int i=0;i<40&&session.Answers.Count==0;i++)host.Service(binding,4);
                Assert.AreEqual(1,session.Answers.Count);Assert.NotNull(session.Answers.CopyItems().Single().Corridor);
                // This fixture owns a real unmanaged host buffer. Reservation counts
                // both that buffer and the real certified kernel, never phantom bytes.
                long bytes=LayeredNavigationScheduler.MaxRetainedBytes-1-session.HostRetainedBytes;
                Assert.Greater(bytes,0);memory=System.Runtime.InteropServices.Marshal.AllocHGlobal(new IntPtr(bytes));
                port.SetHostRetainedBytes(session.HostRetainedBytes+bytes);
                void Release(){port.ReleaseHostForWorldCheckpoint(()=>{host.Dispose();System.Runtime.InteropServices.Marshal.FreeHGlobal(memory);memory=IntPtr.Zero;port.SetHostRetainedBytes(0);});}
                if(releaseBeforeApply){Release();Assert.Zero(session.HostRetainedBytes);}
                session.ApplyResults();Assert.Zero(session.PendingCount);
                Assert.AreEqual(releaseBeforeApply?1:0,session.AppliedResults);
                Assert.Zero(session.RejectedResults,"Capacity outcome is not a rejected transport binding");
                var state=session.CaptureState();Assert.AreEqual(releaseBeforeApply?0:1,state.TechnicalFailures.Length);
                Assert.AreEqual(releaseBeforeApply?1:0,state.InstalledExecutions.Length);
                if(!releaseBeforeApply){Assert.AreEqual(1,state.TechnicalFailures.Single().Entity);Assert.AreEqual(NavSolveStatus.CapacityExceeded,state.TechnicalFailures.Single().Status);Assert.AreEqual(NavigationOutcome.Rejected,session.Crowd.Units.Single().Outcome);Release();}
                Assert.AreEqual(IntPtr.Zero,memory);Assert.Zero(session.HostRetainedBytes);
            }finally{host.Dispose();if(memory!=IntPtr.Zero)System.Runtime.InteropServices.Marshal.FreeHGlobal(memory);session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);}
        }

        private static NavigationSession InstalledLayoutFixture()
        {
            var session=new NavigationSession(1,new NavGeometry(30,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            int[] ids={30,10,20,40,50};for(int i=0;i<ids.Length;i++)session.Crowd.Add(ids[i],new NavPoint(-10,-8+i*3));
            foreach(int id in ids.Take(3))InstallLayoutRoute(session,id);
            return session;
        }
        private static void InstallLayoutRoute(NavigationSession session,int id)
        {
            var unit=session.Crowd.Units.Single(u=>u.Id==id);
            Assert.True(session.MoveGroup(new[]{id},new[]{new NavPoint(10,unit.Position.Z)}));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(session.Admission.Geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                for(int i=0;i<80&&session.PendingCount>0;i++){host.Service(binding,8);session.ApplyResults();}
                Assert.Zero(session.PendingCount);Assert.NotNull(session.InstalledExecutionFor(id));
            }
        }
        private static void AssertInstalledFacts(NavigationSession source,NavigationSession restored,string label)
        {
            var lp=new System.Collections.Generic.SortedDictionary<long,string>();var rp=new System.Collections.Generic.SortedDictionary<long,string>();var left=PlayableWorldRestoreTests.FactBytes(source,true,positions:lp);var right=PlayableWorldRestoreTests.FactBytes(restored,true,positions:rp);if(!left.SequenceEqual(right)){int first=0;while(first<Math.Min(left.Length,right.Length)&&left[first]==right[first])first++;string Around(byte[] value){int start=Math.Max(0,first-180);return start>=value.Length?"<end>":System.Text.Encoding.UTF8.GetString(value,start,Math.Min(360,value.Length-start));}TestContext.WriteLine("INSTALLED_PATH_LEFT="+lp.Last(e=>e.Key<=first).Value+" RIGHT="+rp.Last(e=>e.Key<=first).Value);TestContext.WriteLine("INSTALLED_ROOT_DIFF="+first+" left="+Around(left)+" right="+Around(right));}CollectionAssert.AreEqual(left,right,label);
        }
        private static NavigationSession RestoreInstalledLayout(NavigationSession source)
        {
            var bytes=WorldWire.Pack(w=>WorldWire.Write(w,source.CaptureState()));
            var restored=new NavigationSession(1,new NavGeometry(30,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            restored.RestoreState(WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState));var ownPort=restored.RoutePort;Assert.AreNotSame(source.RoutePort,ownPort);
            AssertInstalledFacts(source,restored,"exact independent actor-owned authority after cold restore");
            CollectionAssert.AreEqual(bytes,WorldWire.Pack(w=>WorldWire.Write(w,restored.CaptureState())));
            return restored;
        }
        private static NavigationSessionState MainLayoutState(NavigationSession source,AuthoritySlotMap<int,byte> layout=null)
        {
            var state=source.CaptureState();if(layout==null){layout=new AuthoritySlotMap<int,byte>();foreach(var row in state.InstalledExecutions)layout.Add(row.Entity,0);}
            state.InstalledExecutionLayoutVersion=1;state.InstalledExecutionKeys=layout.Keys.ToArray();state.InstalledExecutionLayout=layout.CaptureLayout();return state;
        }
        private static byte[] LegacyMainV8Bytes(NavigationSessionState state)
        {
            var bytes=WorldWire.Pack(w=>WorldWire.Write(w,state));int tag=QueueLegacyLayout.Tag(bytes,0x47525031);int version=BitConverter.ToInt32(bytes,tag+4);Assert.AreEqual(9,version);
            int tail=checked(4+4*(state.AllocationRequestIndices?.Length??0)+1+4+state.ArrivalAuthority.Length+4+4*(state.RegisteredRequestIndices?.Length??0)+4+4*(state.DeliveredRequestIndices?.Length??0)+4+state.HostAuthority.Length);
            using(var stream=new System.IO.MemoryStream())using(var writer=new System.IO.BinaryWriter(stream)){
                writer.Write(bytes,0,bytes.Length-tail);writer.Write(state.InstalledExecutionLayoutVersion);WorldWire.Array(writer,state.InstalledExecutionKeys??Array.Empty<int>(),writer.Write);WorldWire.Array(writer,state.InstalledExecutionLayout??Array.Empty<int>(),writer.Write);
                var result=stream.ToArray();BitConverter.GetBytes(8).CopyTo(result,tag+4);return result;
            }
        }
        private static NavigationSession RestoreMainV8(byte[] bytes)
        {
            Assert.Throws<ArgumentException>(()=>WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState),"ambiguous v8 requires caller format; no decoder fallback");
            using(var state=WorldWire.Unpack(bytes,r=>WorldWire.ReadNavigationSessionState(r,NavigationV8Format.MainInstalledLayout))){var restored=new NavigationSession(1,new NavGeometry(30,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);try{restored.RestoreState(state);return restored;}catch{restored.DestroyNavigationAuthority();throw;}}
        }
        [Test] public void InstalledAllocationLayoutKeepsChurnEmptyHolesAndFutureInstallExactly()
        {
            var source=InstalledLayoutFixture();var layout=new AuthoritySlotMap<int,byte>();foreach(int id in new[]{30,10,20})layout.Add(id,0);
            source.Stop(30,false);source.Stop(20,false);layout.Remove(30);layout.Remove(20);CollectionAssert.AreEqual(new[]{3,2,0},layout.CaptureLayout());
            var legacy=MainLayoutState(source,layout);var bytes=LegacyMainV8Bytes(legacy);var restored=RestoreMainV8(bytes);NavigationSession cold=null;
            try{
                Assert.Zero(restored.CaptureState().InstalledExecutionLayoutVersion,"accepted migration drops independent slot history");Assert.IsNull(restored.CaptureState().InstalledExecutionLayout);Assert.NotNull(restored.InstalledExecutionFor(10));
                cold=RestoreInstalledLayout(restored);
                foreach(int id in new[]{40,50}){InstallLayoutRoute(restored,id);InstallLayoutRoute(cold,id);AssertInstalledFacts(restored,cold,"future installed authority");}
                foreach(int id in new[]{10,40,50}){restored.Stop(id,false);cold.Stop(id,false);}Assert.Zero(restored.CaptureState().InstalledExecutions.Length);
                foreach(int id in new[]{30,20,10}){InstallLayoutRoute(restored,id);InstallLayoutRoute(cold,id);CollectionAssert.AreEqual(WorldWire.Pack(w=>WorldWire.Write(w,restored.CaptureState())),WorldWire.Pack(w=>WorldWire.Write(w,cold.CaptureState())));AssertInstalledFacts(restored,cold,"future installed authority");}
            }finally{cold?.DestroyNavigationAuthority();restored.DestroyNavigationAuthority();source.DestroyNavigationAuthority();}
        }
        [Test] public void InstalledAllocationLayoutRejectsCorruptionBeforeCandidateMutation()
        {
            var source=InstalledLayoutFixture();string before=PlayableWorldRestoreTests.Facts(source,true);
            try{foreach(Action<NavigationSessionState> corrupt in new Action<NavigationSessionState>[] {
                state=>state.InstalledExecutionLayoutVersion=2,state=>state.InstalledExecutionKeys=null,state=>state.InstalledExecutionLayout=null,
                state=>state.InstalledExecutionKeys[1]=state.InstalledExecutionKeys[0],state=>state.InstalledExecutionKeys[0]=999,
                state=>state.InstalledExecutionKeys=new[]{30},state=>state.InstalledExecutions[1].Entity=state.InstalledExecutions[0].Entity,
                state=>state.InstalledExecutionLayout=new[]{-1},state=>state.InstalledExecutionLayout=new[]{AuthoritySlotMap<int,int>.MaximumSlots+1},
                state=>state.InstalledExecutionLayout=new[]{5,4,4},state=>state.InstalledExecutionLayout=new[]{4,4},state=>state.InstalledExecutionLayout=new[]{2},
                state=>state.InstalledExecutionLayoutVersion=0,state=>state.InstalledExecutions=state.InstalledExecutions.Reverse().ToArray(),
                state=>state.Crowd.Units=state.Crowd.Units.Where(u=>u.Id!=state.InstalledExecutions[0].Entity).ToArray()}){
                var state=MainLayoutState(source);corrupt(state);var bytes=LegacyMainV8Bytes(state);
                Assert.Throws<ArgumentException>(()=>WorldWire.Unpack(bytes,r=>WorldWire.ReadNavigationSessionState(r,NavigationV8Format.MainInstalledLayout)));
                var target=new NavigationSession(1,new NavGeometry(30,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
                try{Assert.Throws<ArgumentException>(()=>target.RestoreState(state));Assert.Zero(target.Crowd.Units.Count);Assert.Zero(target.Requests.Count);}finally{target.DestroyNavigationAuthority();}
                Assert.AreEqual(before,PlayableWorldRestoreTests.Facts(source,true),"Rejected candidate cannot mutate original world");
            }}finally{source.DestroyNavigationAuthority();}
        }
        [Test] public void VersionEightCannotDowngradeInstalledAllocationToLegacyDense()
        {
            var source=InstalledLayoutFixture();try{string before=PlayableWorldRestoreTests.Facts(source,true);var state=MainLayoutState(source);state.InstalledExecutionLayoutVersion=0;state.InstalledExecutionKeys=Array.Empty<int>();state.InstalledExecutionLayout=Array.Empty<int>();
                Assert.Throws<ArgumentException>(()=>WorldWire.Unpack(LegacyMainV8Bytes(state),r=>WorldWire.ReadNavigationSessionState(r,NavigationV8Format.MainInstalledLayout)));Assert.AreEqual(before,PlayableWorldRestoreTests.Facts(source,true));
            }finally{source.DestroyNavigationAuthority();}
        }
        [Test] public void VersionEightInstalledAllocationPreservesSupportedZeroEntity()
        {
            var source=new NavigationSession(1,new NavGeometry(30,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);NavigationSession restored=null;
            try{source.Crowd.Add(0,new NavPoint(-10,0));InstallLayoutRoute(source,0);var state=MainLayoutState(source);CollectionAssert.AreEqual(new[]{0},state.InstalledExecutionKeys);restored=RestoreMainV8(LegacyMainV8Bytes(state));Assert.NotNull(restored.InstalledExecutionFor(0));Assert.Zero(restored.CaptureState().InstalledExecutionLayoutVersion);}
            finally{restored?.DestroyNavigationAuthority();source.DestroyNavigationAuthority();}
        }
        [Test] public void VersionSevenInstalledRowsRestoreWithoutInventingMissingAllocationHistory()
        {
            var source=InstalledLayoutFixture();NavigationSession restored=null;try{var checkpoint=MainLayoutState(source);var bytes=LegacyMainV8Bytes(checkpoint);int tag=QueueLegacyLayout.Tag(bytes,0x47525031);Assert.AreEqual(8,BitConverter.ToInt32(bytes,tag+4));
                var legacy=bytes.Take(bytes.Length-QueueLegacyLayout.InstalledLayoutTailBytes(checkpoint)).ToArray();BitConverter.GetBytes(7).CopyTo(legacy,tag+4);
                var decoded=WorldWire.Unpack(legacy,WorldWire.ReadNavigationSessionState);Assert.Zero(decoded.InstalledExecutionLayoutVersion);Assert.IsNull(decoded.InstalledExecutionKeys);
                restored=new NavigationSession(1,new NavGeometry(30,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);restored.RestoreState(decoded);Assert.Zero(restored.CaptureState().InstalledExecutionLayoutVersion);Assert.IsNull(restored.CaptureState().InstalledExecutionKeys);
                foreach(int id in new[]{10,20,30})Assert.AreEqual(PlayableWorldRestoreTests.Facts(source.InstalledExecutionFor(id).Corridor),PlayableWorldRestoreTests.Facts(restored.InstalledExecutionFor(id).Corridor));Assert.AreEqual(source.AppliedResults,restored.AppliedResults);Assert.Zero(restored.PendingCount);
            }finally{restored?.DestroyNavigationAuthority();source.DestroyNavigationAuthority();}
        }
        [Test] public void A5VersionEightRequiresExplicitFormatAndVersionNineKeepsItsExactBytes()
        {
            var source=InstalledLayoutFixture();try{var state=source.CaptureState();var current=WorldWire.Pack(w=>WorldWire.Write(w,state));int tag=QueueLegacyLayout.Tag(current,0x47525031);Assert.AreEqual(9,BitConverter.ToInt32(current,tag+4));int hostTail=4+4*state.RegisteredRequestIndices.Length+4+4*state.DeliveredRequestIndices.Length+4+state.HostAuthority.Length;
                var missingBody=(byte[])current.Clone();BitConverter.GetBytes(0).CopyTo(missingBody,current.Length-state.HostAuthority.Length-4);
                Assert.Throws<ArgumentException>(()=>WorldWire.Unpack(missingBody,WorldWire.ReadNavigationSessionState),"Current format requires a nonempty host BODY before publication");
                Assert.Throws<ArgumentException>(()=>WorldWire.Unpack(current.Take(current.Length-1).ToArray(),WorldWire.ReadNavigationSessionState),"Truncated host BODY cannot be read as a legacy tail");
                var legacy=current.Take(current.Length-hostTail).ToArray();BitConverter.GetBytes(8).CopyTo(legacy,tag+4);Assert.Throws<ArgumentException>(()=>WorldWire.Unpack(legacy,WorldWire.ReadNavigationSessionState));Assert.Throws<ArgumentException>(()=>WorldWire.Unpack(legacy,r=>WorldWire.ReadNavigationSessionState(r,NavigationV8Format.MainInstalledLayout)));
                using(var decoded=WorldWire.Unpack(legacy,r=>WorldWire.ReadNavigationSessionState(r,NavigationV8Format.A5Arrival))){CollectionAssert.AreEqual(state.ArrivalAuthority,decoded.ArrivalAuthority);CollectionAssert.AreEqual(state.AllocationRequestIndices,decoded.AllocationRequestIndices);Assert.IsNull(decoded.HostAuthority);}
                using(var decoded=WorldWire.Unpack(current,WorldWire.ReadNavigationSessionState))CollectionAssert.AreEqual(current,WorldWire.Pack(w=>WorldWire.Write(w,decoded)));
            }finally{source.DestroyNavigationAuthority();}
        }

        [Test] public void HostClearReleasesNearCapReservationAfterOldWorldIsCleared()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(session.Move(1,new NavPoint(5,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                host.Service(binding,1);
                long reserved=LayeredNavigationScheduler.MaxRetainedBytes-1024;
                session.RoutePort.SetHostRetainedBytes(reserved);
                var held=new ManualResetEventSlim(false);
                var clearing=new ManualResetEventSlim(false);
                var worker=Task.Run(()=>session.RoutePort.RunHostStep(()=>{
                    held.Set();Assert.True(clearing.Wait(TimeSpan.FromSeconds(5)));
                    Thread.Sleep(100);
                    Assert.AreEqual(reserved,session.HostRetainedBytes);
                }));
                Assert.True(held.Wait(TimeSpan.FromSeconds(5)));
                clearing.Set();
                host.Dispose(); // The main-thread disposal waits for the old authority gate.
                Assert.True(worker.Wait(TimeSpan.FromSeconds(10)));
                Assert.Zero(session.HostRetainedBytes);
            }
        }

        [Test] public void ReadyCheckpointRejectsChangedActiveRoot()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(source.Move(1,new NavPoint(5,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,source.Admission,source.RoutePort);
                for(int i=0;i<40&&source.Answers.Count==0;i++)host.Service(binding,4);
            }
            var state=source.CaptureState();
            Assert.NotNull(state.AnswerMailbox.Single().Corridor);
            state.Groups.Single().CommandSequence++;
            Assert.Throws<ArgumentException>(()=>new NavigationSession(1,
                new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation)
                .RestoreState(state));
        }

        [Test] public void ReboundPortalIdCannotExposeInstalledAuthoredCorridor()
        {
            var map=ThreeCrossingsMap.Default;
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=PlayableProfile.ThreeCrossingsDefault;
            var source=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
            source.Crowd.Add(1,new NavPoint(-18,0),.58,4,4.5);
            Assert.True(source.Move(1,new NavPoint(18,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,profile,source.Admission,source.RoutePort);
                for(int i=0;i<40&&source.PendingCount>0;i++){host.Service(binding,8);source.ApplyResults();}
            }
            var state=source.CaptureState();
            var original=state.InstalledExecutions.Single().Corridor;
            Assert.True(original.Legs.Any(l=>l.Kind==NavTypedLegKind.Portal));
            bool changed=false;
            var legs=original.Legs.Select(l=>{
                if(l.Kind!=NavTypedLegKind.Portal||changed)return l;
                changed=true;return new NavCorridorLeg(l.From,l.To,l.Kind,new string('0',64),l.Cost,
                    l.Section,l.Branch,l.DownstreamMergeLeg);
            }).ToArray();
            state.InstalledExecutions[0].Corridor=new NavCorridorDescriptor(original.Version,
                original.GraphKey,original.ProviderBinding,original.ProfileBinding,original.SurfaceProviderId,
                original.HoldBinding,original.TopologyRevision,original.Radius,original.GridCell,
                original.Provenance,original.World,original.Group,original.RootRevision,
                original.CommandSequence,original.IssuedTick,original.ActivationRevision,original.Entity,
                original.Incarnation,original.Mobility,original.Request,original.Order,
                original.Origin,original.Endpoint,legs);
            Assert.Throws<ArgumentException>(()=>new NavigationSession(1,
                new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),profile.Navigation,
                terrain:map).RestoreState(state));
        }

        [Test] public void ProfileRebindDetachesOldCorridorWhilePreservingMotionCheckpoint()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(source.Move(1,new NavPoint(5,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,source.Admission,source.RoutePort);
                for(int i=0;i<40&&source.PendingCount>0;i++){host.Service(binding,4);source.ApplyResults();}
            }
            Assert.NotNull(source.InstalledExecutionFor(1));
            var changed=new NavigationProfile("profile-after-install",2,.72,4,4.5,.8,.15,3.6,.08);
            source.Rebind(changed);
            Assert.Null(source.InstalledExecutionFor(1));
            Assert.True(source.Crowd.Units.Single().Moving);
            var state=WorldWire.Unpack(WorldWire.Pack(w=>WorldWire.Write(w,source.CaptureState())),
                WorldWire.ReadNavigationSessionState);
            var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),changed);
            restored.RestoreState(state);
            Assert.True(restored.Crowd.Units.Single().Moving);
            Assert.True(restored.CaptureState().InstalledExecutions.Single().Detached);
            source.Step(1d/30);restored.Step(1d/30);
            Assert.AreEqual(source.Crowd.Units.Single().Position,restored.Crowd.Units.Single().Position);
        }

        [Test] public void ArrivedCorridorRestoresWithoutReactivatingMarch()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(1,new NavPoint(-2,0));
            Assert.True(source.Move(1,new NavPoint(2,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,source.Admission,source.RoutePort);
                for(int i=0;i<40&&source.PendingCount>0;i++){host.Service(binding,4);source.ApplyResults();}
            }
            for(int i=0;i<240&&source.Crowd.Units.Single().Moving;i++)source.Step(1d/30);
            Assert.AreEqual(NavigationOutcome.Arrived,source.Crowd.Units.Single().Outcome);
            var state=WorldWire.Unpack(WorldWire.Pack(w=>WorldWire.Write(w,source.CaptureState())),
                WorldWire.ReadNavigationSessionState);
            var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),
                PlayableProfile.Default.Navigation);
            restored.RestoreState(state);
            Assert.AreEqual(NavigationOutcome.Arrived,restored.Crowd.Units.Single().Outcome);
            Assert.Null(restored.InstalledExecutionFor(1));
            Assert.AreEqual(source.Crowd.Units.Single().Position,restored.Crowd.Units.Single().Position);
        }

        [Test] public void HistoricalArrivedCorridorSurvivesLaterAlliedHold()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(1,new NavPoint(-2,0));
            source.Crowd.Add(2,new NavPoint(-2,4));
            Assert.True(source.Move(1,new NavPoint(2,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,source.Admission,source.RoutePort);
                for(int i=0;i<40&&source.PendingCount>0;i++){host.Service(binding,4);source.ApplyResults();}
            }
            for(int i=0;i<240&&source.Crowd.Units.Single(u=>u.Id==1).Moving;i++)source.Step(1d/30);
            Assert.AreEqual(NavigationOutcome.Arrived,source.Crowd.Units.Single(u=>u.Id==1).Outcome);
            source.Stop(2,true);
            var state=WorldWire.Unpack(WorldWire.Pack(w=>WorldWire.Write(w,source.CaptureState())),
                WorldWire.ReadNavigationSessionState);
            var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),
                PlayableProfile.Default.Navigation);
            restored.RestoreState(state);
            Assert.AreEqual(NavigationOutcome.Arrived,restored.Crowd.Units.Single(u=>u.Id==1).Outcome);
            Assert.Null(restored.InstalledExecutionFor(1));
            Assert.AreEqual(source.Crowd.Units.Single(u=>u.Id==1).Position,
                restored.Crowd.Units.Single(u=>u.Id==1).Position);
        }

        [Test] public void VersionFiveWireRestoresExplicitLegacyMotion()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(source.Move(1,new NavPoint(5,0)));
            Assert.True(source.Requests.TryDequeue(out var request));
            Assert.True(source.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal})));
            source.ApplyResults();
            var checkpoint=source.CaptureState();
            var bytes=WorldWire.Pack(w=>WorldWire.Write(w,checkpoint));
            int tag=-1;
            for(int i=0;i<bytes.Length-20;i++)if(bytes[i]==0x31&&bytes[i+1]==0x50&&
                bytes[i+2]==0x52&&bytes[i+3]==0x47){tag=i;break;}
            Assert.GreaterOrEqual(tag,0);
            Assert.AreEqual(8,BitConverter.ToInt32(bytes,tag+4));
            bytes[tag+4]=5; // v6 adds five empty sidecar arrays to this legacy state
            int marchTail=4+checkpoint.Groups.Sum(g=>37+12*g.Members.Length);
            var legacy=bytes.Take(bytes.Length-20-marchTail-QueueLegacyLayout.InstalledLayoutTailBytes(checkpoint)).ToArray();
            var state=WorldWire.Unpack(legacy,WorldWire.ReadNavigationSessionState);
            Assert.AreEqual(0,state.CorridorWireVersion);
            var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),
                PlayableProfile.Default.Navigation);
            restored.RestoreState(state);
            Assert.True(restored.Crowd.Units.Single().Moving);
            Assert.Null(restored.InstalledExecutionFor(1));
        }

        [Test] public void InstalledCorridorSurvivesPendingSubsetAndV6CheckpointWithoutSteeringOldMember()
        {
            var geometry=new NavGeometry(30,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-10,-2));session.Crowd.Add(2,new NavPoint(-10,2));
            Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(10,-2),new NavPoint(10,2)}));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                for(int i=0;i<80&&session.PendingCount>0;i++){host.Service(binding,8);session.ApplyResults();}
            }
            Assert.AreEqual(2,session.AppliedResults);
            var first=session.InstalledExecutionFor(1);
            Assert.NotNull(first);Assert.Greater(first.Corridor.Legs.Count,0);
            Assert.AreEqual(NavCorridorSection.IndividualConnector,first.Corridor.Legs[0].Section);
            Assert.True(session.MoveGroup(new[]{1},new[]{new NavPoint(12,-4)}));
            Assert.Null(session.InstalledExecutionFor(1));
            Assert.NotNull(session.InstalledExecutionFor(2));
            Assert.True(session.Crowd.Units.Single(u=>u.Id==1).Moving);
            var bytes=WorldWire.Pack(w=>WorldWire.Write(w,session.CaptureState()));
            var state=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
            Assert.AreEqual(1,state.CorridorWireVersion);
            Assert.AreEqual(2,state.InstalledExecutions.Length);
            Assert.True(state.InstalledExecutions.Single(x=>x.Entity==1).Detached);
            var restored=new NavigationSession(1,new NavGeometry(30,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            restored.RestoreState(state);
            Assert.Null(restored.InstalledExecutionFor(1));
            Assert.NotNull(restored.InstalledExecutionFor(2));
            Assert.AreEqual(session.Crowd.Units.Single(u=>u.Id==1).Goal,
                restored.Crowd.Units.Single(u=>u.Id==1).Goal);
            CollectionAssert.AreEqual(session.Crowd.Units.Single(u=>u.Id==1).Route,
                restored.Crowd.Units.Single(u=>u.Id==1).Route);
            session.Step(1d/30);restored.Step(1d/30);
            foreach(var id in new[]{1,2}){
                Assert.AreEqual(session.Crowd.Units.Single(u=>u.Id==id).Position,
                    restored.Crowd.Units.Single(u=>u.Id==id).Position);
                Assert.AreEqual(session.Crowd.Units.Single(u=>u.Id==id).Outcome,
                    restored.Crowd.Units.Single(u=>u.Id==id).Outcome);
            }
            Assert.True(restored.Requests.TryDequeue(out var replacement));
            Assert.True(restored.Answers.TryEnqueue(new NavigationAnswer(replacement,
                Array.Empty<NavPoint>(),NavSolveStatus.BlockedConnector)));
            restored.ApplyResults();
            Assert.True(restored.Crowd.Units.Single(u=>u.Id==1).Moving);
            Assert.Null(restored.InstalledExecutionFor(1));
            Assert.NotNull(restored.InstalledExecutionFor(2));
            Assert.AreEqual(NavSolveStatus.BlockedConnector,restored.TechnicalFailureFor(1));
        }

        [Test] public void AuthoredSingletonDirectSeamInstallsWithoutGraphOrNavMesh()
        {
            var map=ThreeCrossingsMap.Default;
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=PlayableProfile.ThreeCrossingsDefault;
            foreach(double radius in new[]{.58,.72}){
                var session=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
                var actor=session.Crowd.Add(1,new NavPoint(-18,0),radius,4,4.5);
                var goal=new NavPoint(18,0);
                Assert.True(session.Move(1,goal));
                Assert.AreEqual(1,session.GroupOrders.Single().Members.Length);
                using(var host=new UnityHostRouteService()){
                    var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
                    for(int i=0;i<16&&session.AppliedResults==0;i++){
                        host.Service(binding,64);session.ApplyResults();
                    }
                    Assert.AreEqual(1,session.AppliedResults,"radius "+radius+" pending="+session.PendingCount+
                        " queued="+session.Requests.Count+" registered="+session.RegisteredRouteCount+
                        " rejected="+session.RejectedResults+" technical="+session.TechnicalFailureFor(1)+
                        " ready="+host.CertifiedCounters.ReadyResults+" graph="+host.CertifiedCounters.GraphBuilds+
                        " projection="+host.LastProjectionFailure+" corridor="+session.LastCorridorFailure);
                    Assert.AreEqual(0,host.CertifiedCounters.GraphBuilds);
                    Assert.AreEqual(0,host.CertifiedCounters.PhysicalGraphWork);
                    Assert.AreEqual(0,host.NavMeshBuilds);
                    Assert.AreEqual(goal,actor.Goal);
                    Assert.AreEqual(NavigationOutcome.Moving,actor.Outcome);
                }
            }
        }

        [Test] public void FoundryCertifiedSingletonInstallsAtBothExistingClearances()
        {
            var map=new FoundryMap(new FoundryProfileData());
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            foreach(double radius in new[]{.58,.72}){
                var session=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
                var start=new NavPoint(0,0);var goal=new NavPoint(1,0);
                session.Crowd.Add(1,start,radius,4,4.5);
                Assert.True(session.Move(1,goal));
                using(var host=new UnityHostRouteService()){
                    var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
                    for(int i=0;i<40&&session.PendingCount>0;i++){host.Service(binding,4);session.ApplyResults();}
                    Assert.AreEqual(1,session.AppliedResults,"radius="+radius+" technical="+
                        session.TechnicalFailureFor(1)+" projection="+host.LastProjectionFailure);
                    Assert.AreEqual(0,host.CertifiedCounters.GraphBuilds);
                    Assert.AreEqual(0,host.NavMeshBuilds);
                }
            }
        }

        [Test] public void EightFiftyMemberGroupsAdmitAndReplayFourHundredOriginalInputs()
        {
            var geometry=new NavGeometry(40,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,new NavigationProfile());
            for(int group=0;group<8;group++){
                var ids=new int[50];var goals=new NavPoint[50];
                for(int member=0;member<50;member++){
                    int index=group*50+member,id=index+1;
                    ids[member]=id;
                    source.Crowd.Add(id,new NavPoint(-35+1.6*(index%20),-35+1.6*(index/20)));
                    goals[member]=new NavPoint(4+1.6*(index%20),4+1.6*(index/20));
                }
                Assert.True(source.MoveGroup(ids,goals),"group "+group);
            }
            Assert.AreEqual(400,source.Requests.Count);
            Assert.AreEqual(8,source.GroupOrders.Count);
            var saved=source.CaptureState();
            Assert.AreEqual(1,saved.TransportVersion);
            Assert.AreEqual(400,saved.RequestMailboxIndices.Length);
            var restored=new NavigationSession(1,new NavGeometry(40,Array.Empty<NavObstacle>(),1),new NavigationProfile());
            restored.RestoreState(saved);
            Assert.AreEqual(400,restored.Requests.Count);
            Assert.AreEqual(400,restored.PendingCount);
            saved.TransportVersion=0;
            Assert.Throws<ArgumentException>(()=>new NavigationSession(1,
                new NavGeometry(40,Array.Empty<NavObstacle>(),1),new NavigationProfile()).RestoreState(saved));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,source.Admission,source.RoutePort);
                for(int attempt=0;attempt<4096&&source.PendingCount>0;attempt++){
                    host.Service(binding,64);source.ApplyResults();
                }
                Assert.AreEqual(400,source.AppliedResults,"pending="+source.PendingCount+
                    " rejected="+source.RejectedResults+" unreachable="+source.UnreachableResults+
                    " projection="+host.LastProjectionFailure);
                Assert.AreEqual(0,source.PendingCount);
                Assert.LessOrEqual(host.CertifiedCounters.FieldBuilds,8,
                    "Eight groups in one clearance class cannot compile one field per exact slot.");
                Assert.LessOrEqual(host.CertifiedCounters.PeakQueue,4096);
                Assert.LessOrEqual(host.CertifiedCounters.CacheBytes,LayeredNavigationScheduler.MaxCacheBytes);
                Assert.LessOrEqual(host.CertifiedPeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                Assert.AreEqual(0,host.NavMeshBuilds);
            }
        }

        [Test] public void DistinctAssignedSlotsReuseOneActualTerminalField()
        {
            var map=ThreeCrossingsMap.Default;
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=PlayableProfile.ThreeCrossingsDefault;
            var session=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
            var ids=new[]{1,2};var goals=new[]{new NavPoint(18,-1.2),new NavPoint(18,1.2)};
            for(int i=0;i<2;i++)session.Crowd.Add(ids[i],new NavPoint(-18,i==0?-1.2:1.2),.58,4,4.5);
            Assert.True(session.MoveGroup(ids,goals));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
                for(int i=0;i<300&&session.PendingCount>0;i++){host.Service(binding,64);session.ApplyResults();}
                Assert.AreEqual(2,session.AppliedResults,"technical="+session.TechnicalFailureFor(1)+"/"+
                    session.TechnicalFailureFor(2)+" projection="+host.LastProjectionFailure);
                Assert.AreEqual(1,host.CertifiedCounters.FieldBuilds);
                Assert.AreEqual(0,host.NavMeshBuilds);
                Assert.AreEqual(0,session.PendingCount);
                Assert.AreEqual(goals[0],session.Crowd.Units.Single(u=>u.Id==1).Goal);
                Assert.AreEqual(goals[1],session.Crowd.Units.Single(u=>u.Id==2).Goal);
            }
        }

        [Test] public void FoundryMaximumScaleFiftyMemberDetourSharesOneFieldAndInstallsExactSlots()
        {
            var map=new FoundryMap(new FoundryProfileData{scale=1.5});
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var provider=new PartitionNavigationProvider(map,geometry,null);
            var clearance=new NavClearanceProfile("ground",1,"ground",0,.58);
            NavPoint[] Sites(NavPoint center){
                var found=new List<NavPoint>{center};
                for(int radius=1;radius<=20&&found.Count<50;radius++)
                    for(int z=-radius;z<=radius&&found.Count<50;z++)
                        for(int x=-radius;x<=radius&&found.Count<50;x++){
                            if(Math.Max(Math.Abs(x),Math.Abs(z))!=radius)continue;
                            var point=new NavPoint(center.X+x*1.6,center.Z+z*1.6);
                            if(provider.TryLocate(point,clearance,out var location)&&
                                provider.IsValid(location,clearance)&&geometry.IsFree(point,.58))found.Add(point);
                        }
                Assert.AreEqual(50,found.Count,"Not enough certified Foundry locations around "+center);
                return found.ToArray();
            }
            var starts=Sites(new NavPoint(0,0));
            var goals=Sites(map.Point(0,100));
            var ids=Enumerable.Range(1,50).ToArray();
            var session=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
            for(int i=0;i<50;i++)session.Crowd.Add(ids[i],starts[i],.58,4,4.5);
            Assert.True(session.MoveGroup(ids,goals));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
                for(int i=0;i<4096&&session.PendingCount>0;i++){host.Service(binding,32);session.ApplyResults();}
                Assert.AreEqual(50,session.AppliedResults,"pending="+session.PendingCount+
                    " field="+host.CertifiedCounters.FieldBuilds+" graph="+host.CertifiedCounters.GraphBuilds+
                    " projection="+host.LastProjectionFailure+" technical="+session.TechnicalFailureFor(1));
                Assert.AreEqual(1,host.CertifiedCounters.FieldBuilds);
                Assert.AreEqual(1,host.CertifiedCounters.GraphBuilds);
                Assert.AreEqual(0,host.NavMeshBuilds);
                Assert.LessOrEqual(host.CertifiedPeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                for(int i=0;i<50;i++)Assert.AreEqual(goals[i],session.Crowd.Units.Single(u=>u.Id==ids[i]).Goal);
            }
        }

        [Test] public void RestoredRoutePortKeepsMonotonicSolverEpochAndDiscardsStaleOldInput()
        {
            var map=new FoundryMap(new FoundryProfileData{scale=1.5});
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var provider=new PartitionNavigationProvider(map,geometry,null);
            var clearance=new NavClearanceProfile("ground",1,"ground",0,.58);
            NavPoint[] Sites(NavPoint center){
                var found=new List<NavPoint>{center};
                for(int radius=1;radius<=20&&found.Count<50;radius++)
                    for(int z=-radius;z<=radius&&found.Count<50;z++)
                        for(int x=-radius;x<=radius&&found.Count<50;x++){
                            if(Math.Max(Math.Abs(x),Math.Abs(z))!=radius)continue;
                            var point=new NavPoint(center.X+x*1.6,center.Z+z*1.6);
                            if(provider.TryLocate(point,clearance,out var location)&&
                                provider.IsValid(location,clearance)&&geometry.IsFree(point,.58))found.Add(point);
                        }
                Assert.AreEqual(50,found.Count,"Not enough certified Foundry locations around "+center);
                return found.ToArray();
            }
            var starts=Sites(new NavPoint(0,0));var goals=Sites(map.Point(0,100));
            var ids=Enumerable.Range(1,50).ToArray();
            var source=new NavigationSession(71,geometry,profile.Navigation,terrain:map);
            for(int i=0;i<ids.Length;i++)source.Crowd.Add(ids[i],starts[i],.58,4,4.5);
            Assert.True(source.MoveGroup(ids,goals));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,profile,source.Admission,source.RoutePort);
                for(int i=0;i<4096&&host.SolverEpoch==0;i++)host.Service(binding,4);
                Assert.Greater(host.SolverEpoch,0,"Real detour work must exhaust an epoch before switching authority.");
                Assert.Greater(source.RegisteredRouteCount,0);
                Assert.Greater(source.PendingCount,0,"Capture genuinely registered unfinished work.");
                long before=host.SolverEpoch;int oldAnswers=source.Answers.Count;
                var bytes=WorldWire.Pack(w=>WorldWire.Write(w,source.CaptureState()));
                var state=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
                var nextGeometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
                var restored=new NavigationSession(71,nextGeometry,profile.Navigation,terrain:map);
                restored.RestoreState(state);
                Assert.AreNotSame(source.RoutePort,restored.RoutePort);
                var nextGoals=starts.Reverse().ToArray();
                Assert.True(restored.MoveGroup(ids,nextGoals),"Replace restored inputs through ordinary authority.");
                var next=new PlayableRouteBinding(nextGeometry,profile,restored.Admission,restored.RoutePort);
                bool staleObserved=false;
                for(int i=0;i<4096&&restored.PendingCount>0;i++){
                    host.Service(next,8);
                    staleObserved|=restored.CaptureState().AnswerMailbox.Any(answer=>answer.Status==NavSolveStatus.Stale);
                    restored.ApplyResults();
                }
                Assert.GreaterOrEqual(host.SolverEpoch,before,"Port changes cannot rewind solver quota history.");
                Assert.AreEqual(0,restored.PendingCount);
                Assert.AreEqual(50,restored.AppliedResults,"Only current replacement orders are installed.");
                Assert.True(staleObserved,"Captured old requests are actually answered as typed stale.");
                Assert.AreEqual(50,restored.RejectedResults,"Every stale old input is rejected exactly once.");
                for(int i=0;i<ids.Length;i++){
                    var actor=restored.Crowd.Units.Single(u=>u.Id==ids[i]);
                    Assert.AreEqual(nextGoals[i],actor.Goal);
                    Assert.AreEqual(NavigationOutcome.Moving,actor.Outcome);
                }
                Assert.AreEqual(oldAnswers,source.Answers.Count,"No completion leaks back into the old authority.");
                Assert.LessOrEqual(host.CertifiedPeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                Assert.AreEqual(0,host.NavMeshBuilds);
            }
        }

        [Test] public void HeadlessAuthorityUsesSameCertifiedServiceAndFrozenBarrier()
        {
            var configuration=OfflineParticipantAuthorityTests.Config(2);
            var authority=new PlayableAuthorityTick(configuration,71);
            var actor=authority.Latest.Entities.First(e=>e.Owner==PlayableOwner.Player);
            var goal=new NavPoint(actor.Position.X+2,actor.Position.Z);
            Assert.True(authority.NavigationBinding.Geometry.IsFree(goal,configuration.Profile.Navigation.Radius));
            var receipt=authority.Apply(new PlayableCommand(71,1,configuration.Roster[0].Id,
                PlayableCommandKind.Move,new[]{actor.Id},goal).AsHuman());
            Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status);
            using(var host=new UnityHostRouteService()){
                bool advanced=false;
                for(int attempt=0;attempt<4096;attempt++){
                    if(authority.TryAdvance()){advanced=true;break;}
                    host.Service(authority,4);
                }
                Assert.True(advanced);
                Assert.Greater(host.CertifiedCounters.ConnectorWork,0);
                Assert.AreEqual(0,host.NavMeshBuilds);
                Assert.Greater(authority.Tick,0);
            }
            authority.Stop();
        }

        [Test] public void ReadyRouteWaitsBehindFullAnswerMailboxAndResumesWithoutReplanning()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,new NavigationProfile());
            session.Crowd.Add(1,new NavPoint(0,0));
            Assert.True(session.Move(1,new NavPoint(5,0)));
            var filler=new NavigationRequest(1,999,42,1,new NavigationProfile(),geometry,
                new NavPoint(0,0),new NavPoint(5,0));
            for(int i=0;i<NavMailbox<NavigationAnswer>.Capacity;i++)
                Assert.True(session.Answers.TryEnqueue(new NavigationAnswer(filler,new[]{filler.Goal})));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                for(int i=0;i<128&&session.RegisteredRouteCount==0;i++)host.Service(binding,1);
                Assert.AreEqual(1,session.RegisteredRouteCount);
                for(int i=0;i<128;i++)host.Service(binding,1);
                Assert.AreEqual(NavMailbox<NavigationAnswer>.Capacity,session.Answers.Count);
                Assert.AreEqual(1,session.RegisteredRouteCount);
                Assert.LessOrEqual(host.CertifiedPeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                Assert.True(session.Answers.TryDequeue(out _));
                host.Service(binding,1);
                Assert.AreEqual(0,session.RegisteredRouteCount);
                session.ApplyResults();
                Assert.AreEqual(1,session.AppliedResults);
                Assert.AreEqual(0,host.NavMeshBuilds);
            }
        }

        [Test] public void StaleSubsetHeadDoesNotBlockUnchangedSibling()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,new NavigationProfile());
            session.Crowd.Add(1,new NavPoint(-5,-1));session.Crowd.Add(2,new NavPoint(-5,1));
            Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(5,-1),new NavPoint(5,1)}));
            session.Stop(1,false);
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                for(int i=0;i<100&&session.PendingCount>0;i++){host.Service(binding,1);session.ApplyResults();}
                Assert.AreEqual(0,session.PendingCount);
                Assert.AreEqual(1,session.AppliedResults);
                Assert.False(session.Crowd.Units.Single(u=>u.Id==1).Moving);
                Assert.True(session.Crowd.Units.Single(u=>u.Id==2).Moving);
                Assert.AreEqual(0,session.RegisteredRouteCount);
            }
        }

        [Test] public void RegisteredSubsetCancellationAndSmallServiceChunksPreserveSiblingInstall()
        {
            var map=ThreeCrossingsMap.Default;
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=PlayableProfile.ThreeCrossingsDefault;
            NavigationSession Session(){
                var value=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
                value.Crowd.Add(1,new NavPoint(-20,18),.58,4,4.5);
                value.Crowd.Add(2,new NavPoint(-18,18),.58,4,4.5);
                Assert.True(value.MoveGroup(new[]{1,2},new[]{new NavPoint(18,18),new NavPoint(20,18)}));
                return value;
            }
            var narrow=Session();var wide=Session();
            using(var small=new UnityHostRouteService())using(var large=new UnityHostRouteService()){
                var smallBinding=new PlayableRouteBinding(geometry,profile,narrow.Admission,narrow.RoutePort);
                var largeBinding=new PlayableRouteBinding(geometry,profile,wide.Admission,wide.RoutePort);
                small.Service(smallBinding,1);
                large.Service(largeBinding,1);
                Assert.AreEqual(1,narrow.RegisteredRouteCount);
                Assert.AreEqual(1,wide.RegisteredRouteCount);
                narrow.Stop(1,false);wide.Stop(1,false);
                for(int i=0;i<4096&&(narrow.PendingCount>0||wide.PendingCount>0);i++){
                    if(narrow.PendingCount>0){small.Service(smallBinding,1);narrow.ApplyResults();}
                    if(wide.PendingCount>0){large.Service(largeBinding,64);wide.ApplyResults();}
                }
                Assert.AreEqual(0,narrow.PendingCount);
                Assert.AreEqual(0,wide.PendingCount);
                Assert.AreEqual(0,narrow.RegisteredRouteCount);
                Assert.AreEqual(0,wide.RegisteredRouteCount);
                Assert.False(narrow.Crowd.Units.Single(u=>u.Id==1).Moving);
                Assert.False(wide.Crowd.Units.Single(u=>u.Id==1).Moving);
                Assert.AreEqual(1,narrow.AppliedResults);
                Assert.AreEqual(1,wide.AppliedResults);
                var n=narrow.Crowd.Units.Single(u=>u.Id==2);
                var w=wide.Crowd.Units.Single(u=>u.Id==2);
                Assert.True(n.Moving);Assert.True(w.Moving);
                Assert.AreEqual(n.Goal,w.Goal);
                Assert.AreEqual(small.SolverEpoch,large.SolverEpoch);
                Assert.LessOrEqual(small.CertifiedPeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                Assert.LessOrEqual(large.CertifiedPeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
            }
        }

        [TestCase(NavSolveStatus.InvalidEndpoint)]
        [TestCase(NavSolveStatus.BlockedConnector)]
        [TestCase(NavSolveStatus.CapacityExceeded)]
        public void TechnicalFailureSurvivesV5AnswerAndInstalledFailureWire(NavSolveStatus status)
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,new NavigationProfile());
            source.Crowd.Add(1,new NavPoint(0,0));
            Assert.True(source.Move(1,new NavPoint(5,0)));
            Assert.True(source.Requests.TryDequeue(out var request));
            Assert.True(source.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>(),status)));
            var bytes=WorldWire.Pack(w=>WorldWire.Write(w,source.CaptureState()));
            var state=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
            Assert.AreEqual(1,state.TransportVersion);
            Assert.AreEqual(status,state.AnswerMailbox.Single().Status);
            var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),new NavigationProfile());
            restored.RestoreState(state);restored.ApplyResults();
            Assert.AreEqual(0,restored.UnreachableResults);
            Assert.AreEqual(NavigationOutcome.Rejected,restored.Crowd.Units.Single().Outcome);
            Assert.AreEqual(status,restored.TechnicalFailureFor(1));
            bytes=WorldWire.Pack(w=>WorldWire.Write(w,restored.CaptureState()));
            state=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
            Assert.AreEqual(status,state.TechnicalFailures.Single().Status);
        }
    }
}
