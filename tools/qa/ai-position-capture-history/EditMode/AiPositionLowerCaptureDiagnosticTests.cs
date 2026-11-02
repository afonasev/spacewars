using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class AiPositionLowerCaptureDiagnosticTests
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly;
        internal static object Read(object value,string name)
        {
            if(value==null)return null;
            for(var type=value.GetType();type!=null;type=type.BaseType)
            {var field=type.GetField(name,Flags);if(field!=null)return field.GetValue(value);var property=type.GetProperty(name,Flags);if(property!=null)return property.GetValue(value);}
            return null;
        }
        // Explicit bounded scalar views; never walk arbitrary object graphs or BODY edges.
        internal static object Scalars(object value,params string[] names)
        {if(value==null)return null;var row=new Dictionary<string,object>();foreach(var name in names){var item=Read(value,name);row[name]=item is Enum?item.ToString():item;}return row;}
        internal static IEnumerable<object> Values(object value)
        {if(value is IEnumerable rows)foreach(var row in rows)yield return Read(row,"Value")??row;}
        static T[] Mailbox<T>(NavMailbox<T> box)
        {var items=(T[])Read(box,"items");int count=(int)Read(box,"count"),head=(int)Read(box,"head"),capacity=(int)Read(box,"capacity");return Enumerable.Range(0,count).Select(i=>items[(head+i)%capacity]).ToArray();}
        internal static object Request(NavigationRequest r)=>r==null?null:(object)new{r.Session,r.Request,r.Entity,r.Order,r.Start,r.Goal,r.StartLocation,r.GoalLocation,r.MemberIdentity,r.ProfileId,r.ProfileRevision,r.HeldIdentity,r.CertifiedProviderBinding,
            requestOwnerType=r.StorageOwner?.GetType().Name,transport=Scalars(r.StorageOwner,"TransportAnswers","TransportExternal")};
        static object Scheduled(NavScheduledRequest r)=>r==null?null:(object)new{r.Identity.StableKey,r.Identity.GraphKey,r.Identity.ArtifactKey,r.CompileKey,
            subscription=Scalars(r.Subscription,"Provenance","WorldGeneration","GroupId","RootOrderRevision","CommandSequence","IssuedTick","ActivationRevision","EntityId","Incarnation","MemberOrderRevision","MobilityRevision","RequestSequence","Origin","AssignedEndpoint","HoldBinding")};
        static object Answer(NavigationAnswer a)=>a==null?null:(object)new{request=Request(a.Request),status=a.Status.ToString(),points=a.ReadOnlyRouteBacking.Length,hasCorridor=a.Corridor!=null};
        static object HostRow(NavigationHostRow row)=>new{request=Request(row.Request),row.ProviderIdentity,row.ArrivalToken,row.ReplayOnly,scheduled=Scheduled(row.Scheduled),
            solved=row.Solved==null?null:(object)new{status=row.Solved.Status.ToString(),row.Solved.SubmittedEpoch,row.Solved.CompletedEpoch,hasTypedRoute=row.Solved.Route!=null},
            projection=Scalars(row.ProjectionOwner,"LegCursor","ReplayCursor","Expected","At","PointCount","TypedCount","ObservedCount","ReplayOnly"),answer=Answer(row.Answer)};
        static object Execution(object execution)=>execution==null?null:(object)new{
            state=Scalars(execution,"Assigned","Incarnation","ActivationTick","RouteRequest","RouteOrder","Arrived","Suspended","Failed","Interrupted","OverrideLocation","AttemptLocation","AttemptRequest","AttemptOrder"),
            stamp=Read(execution,"Stamp"),failure=Scalars(Read(execution,"TerminalFailure"),"Request","Order","GroupId","Revision","Incarnation","Tick","Location","Outcome"),
            issuance=Scalars(Read(execution,"Issuance"),"Id","Revision","Tick","GroupId"),
            command=Scalars(Read(Read(execution,"Issuance"),"Command"),"Generation","Sequence","Origin","Source","Kind","SiteId","TargetId","TargetLocation")};
        [Test,Timeout(180000)] public void Existing4b9Groups40And41PreparationBoundary()
        {
            const string expectedHash="a07e7e94710a3dcb95e5c6495230bfce33d157e4c1268b3f03c0de7ac02c6706";
            var input=Environment.GetEnvironmentVariable("AI_POSITION_CAPTURE");var source=Environment.GetEnvironmentVariable("AI_POSITION_SOURCE_SHA");Assert.IsNotEmpty(input);Assert.IsNotEmpty(source);
            var bytes=File.ReadAllBytes(input);Assert.AreEqual(4620541,bytes.Length);using(var hash=SHA256.Create())Assert.AreEqual(expectedHash,BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());
            long tick;int transportLength;using(var state=PlayableWorldState.Decode(bytes)){Assert.AreEqual(22,state.Version);Assert.AreEqual(7,state.Seed);Assert.AreEqual(107,state.Generation);Assert.AreEqual("ai-position-development-first-witness-v1",state.SourceIdentity);tick=state.Tick;transportLength=state.RuntimeTransport.Length;Assert.Greater(transportLength,0);}
            var output=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(input))),"lower-4b9a-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);var clock=Stopwatch.StartNew();
            void Mark(string phase,object details=null)=>File.AppendAllText(Path.Combine(output,"phases.jsonl"),Newtonsoft.Json.JsonConvert.SerializeObject(new{phase,elapsedSeconds=clock.Elapsed.TotalSeconds,details})+"\n");
            const int seed=7;const long generation=107;
            var p=PlayableProfile.Default;var ai=NativeBalanceFields.InitialAiOpeningProfile();
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,-15),new NavPoint(-20,-27),pin:1),new OfflineStart("east",2,new NavPoint(20,15),new NavPoint(20,27),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);
            var enemy=new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p);
            var front=new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(-20,15),p);
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter};
            Assert.GreaterOrEqual(home.Slots.Count,kinds.Length);
            var buildings=kinds.Select((kind,i)=>new OfflineScenarioBuilding(1,kind,1,home.Slots[i].Id,home.Slots[i].Position,home.Slots[i].Heading)).Concat(new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Outpost,3,0,front.Position)}).ToArray();
            var units=Enumerable.Range(0,5).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-25+i*3,-1))).Concat(new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(20,-24))}).ToArray();
            var config=new OfflineMatchConfiguration(p,"ai-position-development-first-witness-v1","flat-public-position-v1","UnityHostRouteService",seed,new[]{new OfflineParticipant("position-owner",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},starts,new[]{home,enemy,front},PlayableMap.StaticObstacles(p),new double[,]{{0,100},{100,0}},scenario:units,scenarioBuildings:buildings,spectators:new[]{PlayableRuntime.LocalSpectatorId});


            Assert.AreEqual("8d2ab24c1989b3fa52b21f175a4fbfe9b29e423eae5328ae55e606b9566c64ff",ai.Hash);Assert.AreEqual("unity-owner-research-queue-u6-v1",p.ProfileId);Assert.AreEqual(1,p.Revision);
            var actors=new HashSet<int>{9,11,12,13,17,23,26};var groups=new HashSet<long>{40,41};
            bool Selected(NavigationRequest r)=>r!=null&&actors.Contains(r.Entity)&&groups.Contains(r.MemberIdentity?.GroupId??0);
            bool SelectedSchedule(NavScheduledRequest r)=>r!=null&&actors.Contains((int)r.Subscription.EntityId)&&groups.Contains(r.Subscription.GroupId);
            PlayableRuntime clone=null;PlayableAuthorityTick authority=null;byte[] roundtrip=null;Exception failure=null;
            try
            {
                Mark("restore-before");clone=PlayableRuntime.RestoreBytesForTests(bytes,config,ai,r=>
                {
                    try
                    {
                        Mark("prestart-enter");authority=(PlayableAuthorityTick)Read(r,"authorityTick");var domain=authority.Domain;var nav=domain.Navigation;
                        Assert.AreEqual(tick,authority.Latest.Tick);var owner=Values(Read(Read(authority,"scheduler"),"owners")).Single();
                        var registry=(AiArmyRegistry)Read(owner,"armies");var planner=(AiArmyPlanner)Read(owner,"armyPlanner");var mission=planner.Capture();
                        var queues=(IDictionary)Read(domain,"tacticalQueues");var host=(NavigationHostRoot)Read(nav,"committedHostRoot");
                        var requestMailbox=Mailbox(nav.Requests).Where(Selected).Select(Request).ToArray();var answerMailbox=Mailbox(nav.Answers).Where(a=>Selected(a.Request)).Select(Answer).ToArray();
                        var units=actors.OrderBy(x=>x).Select(id=>
                        {
                            var body=nav.Crowd.Units.SingleOrDefault(u=>u.Id==id);var queue=queues.Contains(id)?queues[id]:null;var snapshot=authority.Latest.Entities.SingleOrDefault(u=>u.Id==id);
                            var installed=Values(Read(nav,"installedExecutions")).FirstOrDefault(value=>(int?)Read(value,"Entity")==id);
                            return new{id,army=registry.ArmyFor(id),snapshot,queueState=new{oldMotionSuspended=Read(queue,"OldMotionSuspended"),active=Execution(Read(queue,"Active")),pending=Execution(Read(queue,"Pending"))},
                                pendingRequest=Request(nav.PendingRequestFor(id)),group=nav.GroupFor(id),
                                body=body==null?null:(object)new{body.Id,body.Incarnation,body.MobilityRevision,body.Location,body.GoalLocation,body.Moving,body.Held,outcome=body.Outcome.ToString(),body.InstalledGroupId,body.InstalledRevision,body.InstalledRequest,body.InstalledOrder,body.FailedGroupId,body.FailedRevision,body.FailedRequest,body.FailedOrder,body.FailedGoal,lastFailure=body.LastFailure.ToString(),routePoints=body.Route.Count,progress=Scalars(body,"routeIndex","localIndex","noProgressTicks","blockedSeconds")},
                                installedExecution=installed==null?null:(object)new{state=Scalars(installed,"Entity","Detached","ProfileId","ProfileRevision","HeldIdentity","GeometryIndex","BaseGeometryIndex","ExecutionGeometryIndex"),routePoints=(Read(installed,"InstalledRoute") as Array)?.Length,hasCorridor=Read(installed,"Corridor")!=null}};
                        }).ToArray();
                        var rows=host==null?Array.Empty<object>():host.Rows.Select(x=>x.Value).OfType<NavigationHostRow>().Where(row=>Selected(row.Request)).Select(HostRow).ToArray();
                        var delivered=new List<object>();if(host!=null)foreach(var entry in host.Delivered){if(entry.Value is NavigationHostRow row&&Selected(row.Request))delivered.Add(HostRow(row));else if(entry.Value is NavigationHostAnswer answer&&Selected(answer.Answer.Request))delivered.Add(Answer(answer.Answer));}
                        var jobs=new List<object>();if(host!=null)foreach(var value in Values(Read(host.Scheduler,"jobs")))
                        {
                            var job=(LayeredNavigationScheduler.Job)value;var subscribers=job.Subscribers.Where(SelectedSchedule).ToArray();if(!SelectedSchedule(job.Request)&&subscribers.Length==0)continue;
                            jobs.Add(new{producer=Scheduled(job.Request),graphProducer=Scheduled(job.GraphProviderOwner),subscribers=subscribers.Select(Scheduled).ToArray(),
                                state=Scalars(job,"QueueNode","Phase","GraphDebt","FieldDebt","GraphWork","FieldWork","Failure","OriginCursor","OriginValidated","OriginComplete","HasIngress","ActiveConnectorKey","OriginBinding","DirectSubscription"),
                                compiler=Scalars(job.Compiler,"Phase","Consumed"),completedCompiler=Scalars(job.CompletedCompiler,"Phase","Consumed"),graphPresent=job.Graph!=null,fieldPresent=job.Field!=null,connectorPresent=job.Connector!=null,singletonPresent=job.Singleton!=null,directRoutePresent=job.DirectRoute!=null,
                                demand=Scalars(job.Demand,"Status","phase")});
                        }
                        var facts=new{source,producer="4b9ab9e15dae939beea6d3330d767d1dcc9c3af2",inputHash=expectedHash,tick,transportLength,
                            registry=registry.Capture().Armies.Where(a=>a.Phase!=AiArmyPhase.Disbanded&&(a.Id==mission.ArmyId||a.Members.Any(actors.Contains))).ToArray(),mission,
                            planner=new{planner.Active,planner.PendingId,planner.PendingGeneration,planner.HasAppliedOrder,state=Scalars(planner,"distance","targetHealth","nextId","policyName"),repairMemo=((Dictionary<string,long>)Read(planner,"completed")).Where(entry=>entry.Key.StartsWith("repair-use:",StringComparison.Ordinal)&&int.TryParse(entry.Key.Split(':')[1],out int actor)&&actors.Contains(actor)).ToArray()},
                            units,requestMailbox,answerMailbox,hostPresent=host!=null,
                            pendingRequests=Values(Read(nav,"pending")).OfType<NavigationRequest>().Where(Selected).Select(Request).ToArray(),
                            allocatingRequests=Values(Read(nav,"allocating")).OfType<NavigationRequest>().Where(Selected).Select(Request).ToArray(),
                            barrierEnumerable=Read(nav,"barrierAnswers") is IEnumerable,
                            barrierAnswers=Values(Read(nav,"barrierAnswers")).OfType<NavigationAnswer>().Where(a=>Selected(a.Request)).Select(Answer).ToArray(),
                            host=host==null?null:(object)new{state=Scalars(host,"Epoch","Progress","NextProviderIdentity"),frontier=Scalars(host.Frontier,"Stage","PhaseUsed"),operation=Scalars(host.Operation,"Stage","Iteration","Entered","ProgressIncrementConsumed"),requests=host.Requests.Select(x=>x.Value).OfType<NavigationHostRequest>().Where(q=>Selected(q.Request)).Select(q=>Request(q.Request)).ToArray(),rows,delivered,jobs},
                            evidenceBoundary="All host/job fields read from restored committed Host BODY before any worker starts. Logical work and producer bindings are snapshot state; no new solver or host. QueueExecution and crowd installation/failure bindings are persisted wire fields. Transient preparation stacks, historical invalidation reasons and unsaved worker progress remain UNKNOWN. No Ensure/Service/Pump/Quote/macro tick."};
                        File.WriteAllText(Path.Combine(output,"lower.json"),Newtonsoft.Json.JsonConvert.SerializeObject(facts,Newtonsoft.Json.Formatting.Indented));Mark("readback-written");
                        roundtrip=r.CaptureBeforeStartForTests();Mark("captured",new{bytes=roundtrip.Length});
                    }
                    catch(Exception error){failure=error;Mark("failure",error.ToString());}
                    finally{r.RequestStop();}
                });
                Assert.True(SpinWait.SpinUntil(()=>clone.IsStopped,5000));if(failure!=null)Assert.Fail(failure.ToString());
                using(var after=PlayableWorldState.Decode(roundtrip)){Assert.AreEqual(tick,after.Tick);Assert.AreEqual(transportLength,after.RuntimeTransport.Length);}
                ExactByteAssert.AreEqual(bytes,roundtrip,"lower-boundary read-only capture must retain every Runtime byte");Mark("exact-after");
            }
            finally{if(clone!=null){clone.RequestStop();Assert.True(SpinWait.SpinUntil(()=>clone.IsStopped,5000));clone.Dispose();}if(authority!=null){authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();}Mark("cleanup-after");}
        }
    }
}
