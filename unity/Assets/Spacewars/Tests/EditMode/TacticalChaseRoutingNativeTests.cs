using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class TacticalChaseRoutingNativeTests
    {
        private const int Seed=19261019;
        private const long Generation=81;
        private const string Source="native-c2-3-explicit-attack-current-v1";
        private const string Map="native-flat-eight-starts-v1";
        private const string GenesisSha256="ed4d338c3c1d256f82b2da268bf6c0c01c81cf929f5889a1cdf5d2c1a7a940ed";
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;

        private sealed class ChaseProof
        {
            internal int Target;
            internal bool Explicit,HasAttackMove;
            internal NavPoint AttackMove;
            internal double Repath;
            internal PlayableTacticalOrderKind OrderKind;
            internal long Group,Revision,ActivationTick,RouteRequest,RouteOrder,AttemptRequest,AttemptOrder;
            internal long InstalledGroup,InstalledRevision,InstalledRequest,InstalledOrder;
        }

        private static OfflineMatchConfiguration Configuration(NavPoint origin,NavPoint target)
        {
            var basis=OfflineParticipantAuthorityTests.Config(2,false,Seed);
            var costs=new double[basis.Starts.Count,basis.Starts.Count];
            for(int i=0;i<basis.Starts.Count;i++)for(int j=0;j<basis.Starts.Count;j++)costs[i,j]=basis.RouteCost(i,j);
            return new OfflineMatchConfiguration(basis.Profile,Source,Map,"UnityHostRouteService",Seed,
                basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,
                scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,origin),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,target)});
        }

        private static string GenesisPath()
        {
            var dir=new DirectoryInfo(Directory.GetCurrentDirectory());
            while(dir!=null&&!File.Exists(Path.Combine(dir.FullName,"unity/Tests/Fixtures/native-offline-participant-authority/source-fixture.tsv")))dir=dir.Parent;
            Assert.NotNull(dir,"Native flat genesis fixture must be present in this checkout.");
            return Path.Combine(dir.FullName,"unity/Tests/Fixtures/native-offline-participant-authority/source-fixture.tsv");
        }

        private static double Distance(NavPoint a,NavPoint b)
        {double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}

        private static bool Until(UnityHostRouteService host,PlayableRuntime runtime,Func<bool> condition,double seconds)
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();
            while(timer.Elapsed.TotalSeconds<seconds)
            {
                host.Service(runtime,runtime.NavigationBinding.Profile.NavigationRequestsPerFrame);
                Assert.IsNull(runtime.Latest.Failure,"Ordinary runtime failure");
                Assert.IsNull(host.CertifiedPublication?.Failure,"Certified host failure");
                if(condition())return true;
                Thread.Sleep(1);
            }
            return condition();
        }

        private static bool UntilWithoutHost(PlayableRuntime runtime,Func<bool> condition,double seconds)
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();
            while(timer.Elapsed.TotalSeconds<seconds)
            {
                Assert.IsNull(runtime.Latest.Failure,"Ordinary runtime failure");
                if(condition())return true;
                Thread.Sleep(1);
            }
            return condition();
        }

        private static byte[] Capture(UnityHostRouteService host,PlayableRuntime runtime)
        {
            var task=runtime.RequestCaptureBytes();
            Assert.True(Until(host,runtime,()=>task.IsCompleted,10),"Current-authority capture should complete at its boundary.");
            Assert.AreEqual(TaskStatus.RanToCompletion,task.Status,task.Exception?.ToString());
            return task.GetAwaiter().GetResult();
        }

        private static byte[] CaptureWithoutHost(PlayableRuntime runtime)
        {
            var task=runtime.RequestCaptureBytes();
            Assert.True(UntilWithoutHost(runtime,()=>task.IsCompleted,10),"Current-authority capture should complete without servicing the host.");
            Assert.AreEqual(TaskStatus.RanToCompletion,task.Status,task.Exception?.ToString());
            return task.GetAwaiter().GetResult();
        }

        private static CertifiedRouteWorker StopHostWorker(UnityHostRouteService host,PlayableRuntime runtime)
        {
            var worker=(CertifiedRouteWorker)Field(host,"worker");
            Assert.NotNull(worker,"The ordinary host must own a route worker before quiescence.");
            host.Dispose();
            Assert.True(UntilWithoutHost(runtime,()=>worker.IsStopped,10),"Disposed ordinary route worker must finish cleanup before the next phase.");
            Assert.IsNull(worker.Publication.Failure,"Stopped ordinary route worker must publish no failure.");
            return worker;
        }

        private static PlayableEntitySnapshot RequireActor(PlayableRuntime runtime,OfflinePresentationFrame frame,string owner,int actor,
            PlayableEntitySnapshot lastSeen,UnityHostRouteService host,CertifiedRouteWorker stoppedWorker=null)
        {
            var entity=frame.Views[owner].Entities.SingleOrDefault(e=>e.Id==actor);
            if(entity!=null)return entity;
            var latest=runtime.Latest;var publication=host?.CertifiedPublication??stoppedWorker?.Publication;
            string Requests()=>string.Join(";",runtime.Requests.CopyItems().Select(r=>$"request={r.Request},entity={r.Entity},order={r.Order}"));
            string Answers()=>string.Join(";",runtime.Answers.CopyItems().Select(a=>$"request={a.Request.Request},entity={a.Request.Entity},status={a.Status},routeLength={a.RouteLength}"));
            string seen=lastSeen==null?"none":$"issuedTick={lastSeen.CurrentOrder?.IssuedTick},health={lastSeen.Health},exactHealth={lastSeen.ExactHealth},position={lastSeen.Position},moving={lastSeen.Moving},outcome={lastSeen.NavigationOutcome},target={lastSeen.TargetId}";
            Assert.Fail($"Actor {actor} absent from immutable participant frame owner={owner},sequence={frame.Sequence},tick={frame.Tick}; lastSeen=[{seen}]; latest=sequence:{latest.Sequence},tick:{latest.Tick},paused:{latest.Paused},status:{latest.Status},failure:{latest.Failure}; publishedRequestSequence={runtime.NavigationBinding.Admission.PublishedThroughRequestSequence},queuedRequests={runtime.NavigationBinding.RoutePort.QueuedRequestCount}; requests=[{Requests()}]; answers=[{Answers()}]; workerPublication=[pumps:{publication?.Pumps},failure:{publication?.Failure},projectionFailure:{publication?.ProjectionFailure}].");
            return null;
        }

        private static object Field(object value,string name)
        {
            Assert.NotNull(value,"Expected authority object before reading "+name);
            var field=value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            Assert.NotNull(field,value.GetType().FullName+"."+name);
            return field.GetValue(value);
        }

        private static PlayableAuthorityTick Authority(PlayableRuntime runtime)
            =>(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",Hidden).GetValue(runtime);

        private static byte[] CanonicalAuthority(PlayableAuthorityTick authority,long publication,IReadOnlyDictionary<string,long> watermarks)
        {
            using(var state=authority.CaptureWorldState(publication,id=>watermarks.TryGetValue(id,out var value)?value:0))
                return state.Encode();
        }

        private static NavigationRequest PendingRequest(PlayableRuntime runtime,int actor)
            =>(NavigationRequest)typeof(NavigationSession).GetMethod("PendingRequestFor",Hidden).Invoke(Authority(runtime).Domain.Navigation,new object[]{actor});

        private static double RuntimeTickSeconds()
        {
            var field=typeof(PlayableRuntime).GetField("TickSeconds",BindingFlags.Static|BindingFlags.NonPublic);
            Assert.NotNull(field,"Use the ordinary runtime's source constant for tick-based guards.");
            return (double)field.GetRawConstantValue();
        }

        private static void AssertSamePending(NavigationRequest expected,NavigationRequest actual)
        {
            Assert.NotNull(expected);Assert.NotNull(actual);
            Assert.AreEqual(expected.Session,actual.Session);Assert.AreEqual(expected.Request,actual.Request);
            Assert.AreEqual(expected.Entity,actual.Entity);Assert.AreEqual(expected.Order,actual.Order);
            Assert.AreEqual(expected.GoalLocation,actual.GoalLocation);
            Assert.NotNull(expected.MemberIdentity);Assert.NotNull(actual.MemberIdentity);
            Assert.AreEqual(expected.MemberIdentity.Incarnation,actual.MemberIdentity.Incarnation);
            Assert.AreEqual(expected.MemberIdentity.MobilityRevision,actual.MemberIdentity.MobilityRevision);
            Assert.AreEqual(expected.MemberIdentity.GroupId,actual.MemberIdentity.GroupId);
            Assert.AreEqual(expected.MemberIdentity.OrderRevision,actual.MemberIdentity.OrderRevision);
            Assert.AreEqual(expected.MemberIdentity.MembershipRevision,actual.MemberIdentity.MembershipRevision);
        }

        private static void AssertAllAuthorityBodies(PlayableRuntime runtime,PlayableProfile profile)
        {
            var domain=Authority(runtime).Domain;
            var units=(IDictionary)typeof(PlayableDomain).GetField("units",Hidden).GetValue(domain);
            Assert.AreEqual(4,units.Count,"Two offline genesis Explorers and two authored Tanks are present in authority.");
            var bodies=new System.Collections.Generic.List<NavUnit>();
            foreach(DictionaryEntry entry in units)
            {
                var unit=entry.Value;var kind=(PlayableEntityKind)Field(unit,"Kind");
                Assert.True(domain.Navigation.Crowd.TryGet((int)entry.Key,out var body),"Every authority unit must have one navigation body.");
                Assert.AreEqual(PlayableUnitRules.Radius(profile,kind),body.Radius);
                Assert.True(domain.Geometry.IsFree(body.Position,body.Radius),"Every authority body must fit authored geometry.");
                bodies.Add(body);
            }
            for(int i=0;i<bodies.Count;i++)for(int j=0;j<i;j++)
                Assert.Greater(Distance(bodies[i].Position,bodies[j].Position),bodies[i].Radius+bodies[j].Radius,"Full authority bodies must not overlap.");
        }

        private static ChaseProof ReadChase(PlayableAuthorityTick authority,int actor)
        {
            var domain=authority.Domain;
            var units=(IDictionary)typeof(PlayableDomain).GetField("units",Hidden).GetValue(domain);
            var unit=units[actor];
            var order=Field(unit,"CurrentOrder");
            var queues=(IDictionary)typeof(PlayableDomain).GetField("tacticalQueues",Hidden).GetValue(domain);
            var queue=queues[actor];var active=Field(queue,"Active");
            Assert.NotNull(active,"An explicit Attack must retain its active tactical execution.");
            var issuance=Field(active,"Issuance");var command=Field(issuance,"Command");
            Assert.AreEqual(PlayableCommandKind.Attack,((PlayableCommand)command).Kind);
            Assert.True(domain.Navigation.Crowd.TryGet(actor,out var body));
            return new ChaseProof
            {
                Target=(int)Field(unit,"Target"),Explicit=(bool)Field(unit,"ExplicitTarget"),
                HasAttackMove=(bool)Field(unit,"HasAttackMove"),AttackMove=(NavPoint)Field(unit,"AttackMove"),
                Repath=(double)Field(unit,"Repath"),OrderKind=((PlayableTacticalOrderSnapshot)order).Kind,
                Group=(long)Field(issuance,"GroupId"),Revision=(long)Field(Field(active,"Stamp"),"Revision"),
                ActivationTick=(long)Field(active,"ActivationTick"),RouteRequest=(long)Field(active,"RouteRequest"),
                RouteOrder=(long)Field(active,"RouteOrder"),AttemptRequest=(long)Field(active,"AttemptRequest"),
                AttemptOrder=(long)Field(active,"AttemptOrder"),InstalledGroup=body.InstalledGroupId,
                InstalledRevision=body.InstalledRevision,InstalledRequest=body.InstalledRequest,InstalledOrder=body.InstalledOrder
            };
        }

        private static void AssertSameChase(ChaseProof expected,ChaseProof actual)
        {
            Assert.AreEqual(expected.Target,actual.Target);Assert.AreEqual(expected.Explicit,actual.Explicit);
            Assert.AreEqual(expected.HasAttackMove,actual.HasAttackMove);Assert.AreEqual(expected.AttackMove,actual.AttackMove);
            Assert.AreEqual(expected.Repath,actual.Repath);Assert.AreEqual(expected.OrderKind,actual.OrderKind);
            Assert.AreEqual(expected.Group,actual.Group);Assert.AreEqual(expected.Revision,actual.Revision);
            Assert.AreEqual(expected.ActivationTick,actual.ActivationTick);Assert.AreEqual(expected.RouteRequest,actual.RouteRequest);
            Assert.AreEqual(expected.RouteOrder,actual.RouteOrder);Assert.AreEqual(expected.AttemptRequest,actual.AttemptRequest);
            Assert.AreEqual(expected.AttemptOrder,actual.AttemptOrder);Assert.AreEqual(expected.InstalledGroup,actual.InstalledGroup);
            Assert.AreEqual(expected.InstalledRevision,actual.InstalledRevision);Assert.AreEqual(expected.InstalledRequest,actual.InstalledRequest);
            Assert.AreEqual(expected.InstalledOrder,actual.InstalledOrder);
        }

        private static PlayableAuthorityTick Probe(byte[] bytes,OfflineMatchConfiguration config,int actor,out ChaseProof proof)
        {
            var authority=PlayableAuthorityTick.RestoreBytes(bytes,config);
            proof=ReadChase(authority,actor);
            return authority;
        }

        [Test,Timeout(180000)]
        public void OrdinaryAttackMoveRestoresCombatInterruptionAndResumesOriginalIntentAfterTargetDeparture()
        {
            var origin=new NavPoint(-18,8);var targetStart=new NavPoint(3,8);var goal=new NavPoint(18,8);var departure=new NavPoint(3,27);
            var config=Configuration(origin,targetStart);var profile=config.Profile;
            string player=config.Roster[0].Id,enemyOwner=config.Roster[1].Id;
            string output=Path.GetFullPath(Environment.GetEnvironmentVariable("SPACEWARS_C23_ATTACKMOVE_OUTPUT")??
                Path.Combine(TestContext.CurrentContext.WorkDirectory,".local/qa/c23-attackmove-combat-restore"));
            if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new InvalidOperationException("AttackMove evidence output must be empty.");
            Directory.CreateDirectory(output);
            void Save(string name,object value)=>File.WriteAllText(Path.Combine(output,name),Newtonsoft.Json.JsonConvert.SerializeObject(value,Newtonsoft.Json.Formatting.Indented));
            object Point(NavPoint p)=>new {p.X,p.Z};
            object[] IntentFacts(object[] values)=>values.Select(x=>x is NavLocation location?(object)new {position=Point(location.Position),surface=location.SurfaceId}:x).ToArray();
            using(var sha=SHA256.Create())
                Assert.AreEqual(GenesisSha256,BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(GenesisPath()))).Replace("-","").ToLowerInvariant());
            Assert.AreEqual(Seed,config.Seed);Assert.AreEqual(Source,config.SourceIdentity);Assert.AreEqual(Map,config.MapIdentity);
            CollectionAssert.AreEqual(WorldWire.Binding(PlayableProfile.Default),WorldWire.Binding(profile));
            Assert.AreEqual(10d,profile.TankRange);Assert.AreEqual(13d,profile.TankVisionRange);
            Assert.AreEqual(2,config.Roster.Count);Assert.True(config.Roster.All(x=>x.Control==OfflineControl.Human));
            Assert.AreEqual(2,config.ScenarioUnits.Count);Assert.AreEqual(0,config.ScenarioBuildings.Count);
            var geometry=new NavGeometry(profile.ArenaHalfExtent,config.Obstacles.ToArray(),1);
            foreach(var point in new[]{origin,targetStart,goal,departure})Assert.True(geometry.IsFree(point,PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank)));
            Assert.Greater(Distance(origin,targetStart),profile.TankVisionRange);
            Save("preflight.json",new {Seed,Generation,Source,Map,profile.ProfileId,profile.Revision,profile.SourceCommit,profile.SourceManifestSha256,
                origin=Point(origin),target=Point(targetStart),goal=Point(goal),departure=Point(departure),format=PlayableWorldState.CurrentVersion,horizon=2700,timeoutMs=180000});
            int actor=-1,enemy=-1;long commandTick=-1,motionTick=-1,shotTick=-1,resumeTick=-1,publication=0;
            object[] originalIntent=null;byte[] checkpoint=null,canonical=null;Dictionary<string,long> ingress=null;
            NavPoint interruptedPosition=origin;long captureTick=-1;string phase="prepared";
            bool Wait(UnityHostRouteService host,PlayableRuntime runtime,Func<bool> condition,double seconds)
            {
                var timer=System.Diagnostics.Stopwatch.StartNew();
                while(timer.Elapsed.TotalSeconds<seconds){
                    if(host!=null)host.Service(runtime,profile.NavigationRequestsPerFrame);
                    Assert.IsNull(runtime.Latest.Failure,"Ordinary runtime failure in "+phase);Assert.IsNull(host?.CertifiedPublication?.Failure,"Host failure in "+phase);
                    if(commandTick>=0)Assert.LessOrEqual(runtime.Latest.Tick-commandTick,2700,"Frozen command-relative authority horizon in "+phase);
                    if(condition())return true;Thread.Sleep(1);
                }
                return false;
            }
            void Pause(UnityHostRouteService host,PlayableRuntime runtime){runtime.RequestPause(true);Assert.True(Wait(host,runtime,()=>runtime.Latest.Paused,5),"Pause at authority boundary in "+phase);}
            object Execution(PlayableRuntime runtime)
            {
                Assert.True(runtime.Latest.Paused,"Read mutable authority only at paused boundary.");
                var queues=(IDictionary)Field(Authority(runtime).Domain,"tacticalQueues");var queue=queues[actor];
                Assert.IsNull(Field(queue,"Pending"));Assert.IsNull(Field(queue,"Completion"));Assert.Zero(((ICollection)Field(queue,"Deferred")).Count);
                return Field(queue,"Active");
            }
            object[] Intent(PlayableRuntime runtime)
            {
                var execution=Execution(runtime);var issuance=Field(execution,"Issuance");var command=(PlayableCommand)Field(issuance,"Command");
                var order=runtime.ParticipantView(player).Entities.Single(e=>e.Id==actor).CurrentOrder;
                Assert.NotNull(order);Assert.AreEqual(PlayableTacticalOrderKind.AttackMove,order.Kind);Assert.AreEqual(goal,order.Destination);
                Assert.AreEqual(PlayableCommandKind.AttackMove,command.Kind);Assert.AreEqual(1,command.Sequence);Assert.AreEqual(player,command.PlayerId);
                Assert.True(Authority(runtime).Domain.Navigation.Crowd.TryGet(actor,out var body));
                Assert.Greater(body.InstalledRequest,0);Assert.Greater(body.InstalledOrder,0);Assert.Greater(body.InstalledGroupId,0);
                var assigned=(NavLocation?)Field(execution,"Assigned");Assert.True(assigned.HasValue);Assert.AreEqual(assigned,body.GoalLocation);
                var units=(IDictionary)Field(Authority(runtime).Domain,"units");var unit=units[actor];
                Assert.True((bool)Field(unit,"HasAttackMove"));Assert.AreEqual(assigned.Value.Position,(NavPoint)Field(unit,"AttackMove"));
                var group=Authority(runtime).Domain.Navigation.GroupFor(actor);Assert.NotNull(group);
                var member=group.Members.Single(m=>m.Entity==actor);Assert.AreEqual(group.GroupId,body.InstalledGroupId);
                Assert.AreEqual(member.Incarnation,body.Incarnation);Assert.AreEqual((long)Field(execution,"Incarnation"),body.Incarnation);
                return new object[]{Field(issuance,"Id"),Field(issuance,"Revision"),Field(issuance,"Tick"),Field(issuance,"GroupId"),
                    order.UnitId,order.Owner,order.Generation,order.CommandSequence,order.IssuedTick,order.DestinationLocation,assigned,
                    Field(Field(execution,"Stamp"),"Revision"),Field(execution,"ActivationTick"),Field(execution,"RouteRequest"),Field(execution,"RouteOrder"),
                    Field(execution,"AttemptRequest"),Field(execution,"AttemptOrder"),body.Id,body.Incarnation,body.MobilityRevision,
                    body.InstalledGroupId,body.InstalledRevision,body.InstalledRequest,body.InstalledOrder,member.GoalLocation,group.WorldGeneration,group.OrderRevision,group.CommandSequence,group.IssuedTick,group.MembershipRevision,group.OriginalLocation,member.OrderRevision,member.MobilityRevision};
            }
            Dictionary<string,long> ActualIngress(PlayableRuntime runtime)
            {
                lock(Field(runtime,"gate")){
                    Assert.True(runtime.Latest.Paused);Assert.AreNotEqual(0,(int)Field(runtime,"paused"));
                    Assert.Zero(((ICollection)Field(runtime,"inbox")).Count);Assert.Zero(((IDictionary)Field(runtime,"humanActions")).Count);
                    Assert.Zero(((ICollection)Field(runtime,"campaignTutorialControls")).Count);Assert.Zero((int)Field(runtime,"finishRequested"));
                    Assert.IsNull(Field(runtime,"requestedBalance"));Assert.IsNull(Field(runtime,"requestedAiProfile"));Assert.IsNull(Field(runtime,"captureRequest"));
                    return new Dictionary<string,long>((IDictionary<string,long>)Field(runtime,"acceptedByOwner"),StringComparer.Ordinal);
                }
            }
            string[] Watermarks(IEnumerable<KeyValuePair<string,long>> values)=>values.OrderBy(x=>x.Key).Select(x=>x.Key+":"+x.Value).ToArray();
            void ValidateCapture(byte[] bytes)
            {
                using(var state=PlayableWorldState.Decode(bytes)){
                    Assert.AreEqual(PlayableWorldState.CurrentVersion,state.Version);Assert.AreEqual(11,state.Version);Assert.AreEqual(Seed,state.Seed);
                    Assert.AreEqual(Generation,state.Generation);Assert.AreEqual(Source,state.SourceIdentity);Assert.AreEqual(captureTick,state.Tick);
                    CollectionAssert.AreEqual(WorldWire.Binding(profile),state.Binding,"Current profile/producer revision binding.");
                    CollectionAssert.AreEqual(OfflineConfigurationWire.Binding(config,profile),state.Participants,"Current authored map/roster binding.");
                    Assert.Greater(state.RuntimeTransport.Length,0);
                }
            }
            try
            {
                using(var host=new UnityHostRouteService())
                using(var runtime=new PlayableRuntime(config,Generation,startPaused:true))
                {
                    Assert.True(Wait(null,runtime,()=>runtime.IsPrepared&&runtime.Latest.Paused,30));AssertAllAuthorityBodies(runtime,profile);
                    Assert.AreEqual(PlayableAuthorityTick.AccruedRouteDeliveryPolicy,Authority(runtime).EffectiveRouteDeliveryPolicy);
                    actor=runtime.ParticipantView(player).Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank).Id;
                    enemy=runtime.ParticipantView(enemyOwner).Entities.Single(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Tank).Id;
                    Assert.AreEqual(origin,runtime.ParticipantView(player).Entities.Single(e=>e.Id==actor).Position);
                    commandTick=runtime.Latest.Tick;phase="ordinary-command-to-motion";Save("ordinary-entered.json",new {actor,enemy,commandTick});
                    runtime.RequestPause(false);Assert.True(Wait(null,runtime,()=>!runtime.Latest.Paused,5));
                    Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,1,enemyOwner,PlayableCommandKind.Hold,new[]{enemy}).AsHuman()).Accepted);
                    Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,1,player,PlayableCommandKind.AttackMove,new[]{actor},goal).AsHuman()).Accepted);
                    Assert.True(Wait(host,runtime,()=>{
                        var view=runtime.ParticipantView(player);var tank=view.Entities.Single(e=>e.Id==actor);
                        if(Distance(tank.Position,origin)>.05&&tank.Moving){motionTick=view.Tick;return true;}return false;
                    },60),"Applied AttackMove must receive real threaded host service and move.");
                    Pause(host,runtime);originalIntent=Intent(runtime);
                    Assert.False((bool)Field(Execution(runtime),"Suspended"));
                    Assert.True(runtime.ReceiptsAfter(0).Any(r=>r.OwnerId==player&&r.Sequence==1&&r.Status==PlayableCommandStatus.Applied));
                    Assert.True(runtime.ReceiptsAfter(0).Any(r=>r.OwnerId==enemyOwner&&r.Sequence==1&&r.Status==PlayableCommandStatus.Applied));
                    Save("first-motion.json",new {motionTick,intent=IntentFacts(originalIntent),position=Point(runtime.ParticipantView(player).Entities.Single(e=>e.Id==actor).Position)});
                    phase="natural-combat-interruption";runtime.RequestPause(false);
                    Assert.True(Wait(host,runtime,()=>{
                        var view=runtime.ParticipantView(player);var tank=view.Entities.Single(e=>e.Id==actor);
                        if(tank.TargetId==enemy&&!tank.Moving&&view.Projectiles.Any(p=>p.OwnerId==actor&&p.TargetId==enemy)){shotTick=view.Tick;return true;}return false;
                    },60),"Original AttackMove must naturally suspend its installed route and fire.");
                    Pause(host,runtime);CollectionAssert.AreEqual(originalIntent,Intent(runtime),"Combat must preserve original issuance, assigned goal and member/root route identity.");
                    Assert.True((bool)Field(Execution(runtime),"Suspended"));Assert.Greater(shotTick,motionTick);
                    Assert.AreEqual(enemy,runtime.ParticipantView(player).Entities.Single(e=>e.Id==actor).TargetId);
                    interruptedPosition=runtime.ParticipantView(player).Entities.Single(e=>e.Id==actor).Position;
                    StopHostWorker(host,runtime);ingress=ActualIngress(runtime);
                    CollectionAssert.AreEqual(new[]{player+":1",enemyOwner+":1"}.OrderBy(x=>x).ToArray(),Watermarks(ingress));
                    captureTick=runtime.Latest.Tick;checkpoint=CaptureWithoutHost(runtime);
                    File.WriteAllBytes(Path.Combine(output,"combat-current-v10.bin"),checkpoint);ValidateCapture(checkpoint);
                    Save("combat-facts.json",new {captureTick,shotTick,position=Point(interruptedPosition),intent=IntentFacts(Intent(runtime)),ingress,actor,enemy});
                    var probe=PlayableAuthorityTick.RestoreBytes(checkpoint,config);
                    try{
                        publication=probe.PublicationSequence;CollectionAssert.AreEqual(Watermarks(ingress),Watermarks(probe.HumanWatermarks));
                        canonical=CanonicalAuthority(probe,publication,ingress);
                    }finally{probe.Stop();probe.Domain.Navigation.ReleaseCommittedHostRoot();probe.Domain.DestroyOwnedNavigation();}
                }
                phase="cold-current-restore";
                using(var host=new UnityHostRouteService())
                using(var runtime=PlayableRuntime.RestoreBytes(checkpoint,config))
                {
                    Assert.True(Wait(null,runtime,()=>runtime.IsPrepared&&runtime.Latest.Paused,30));AssertAllAuthorityBodies(runtime,profile);
                    CollectionAssert.AreEqual(originalIntent,Intent(runtime));Assert.True((bool)Field(Execution(runtime),"Suspended"));
                    var tank=runtime.ParticipantView(player).Entities.Single(e=>e.Id==actor);
                    Assert.AreEqual(enemy,tank.TargetId);Assert.False(tank.Moving);Assert.AreEqual(interruptedPosition,tank.Position);
                    CollectionAssert.AreEqual(Watermarks(ingress),Watermarks(ActualIngress(runtime)));
                    CollectionAssert.AreEqual(Watermarks(ingress),Watermarks(Authority(runtime).HumanWatermarks));
                    var roundTrip=CaptureWithoutHost(runtime);File.WriteAllBytes(Path.Combine(output,"restored-current-v10.bin"),roundTrip);ValidateCapture(roundTrip);
                    using(var source=PlayableWorldState.Decode(checkpoint))using(var restored=PlayableWorldState.Decode(roundTrip))
                        CollectionAssert.AreEqual(source.RuntimeTransport,restored.RuntimeTransport,"Literal runtime transport must survive without normalization.");
                    CollectionAssert.AreEqual(canonical,CanonicalAuthority(Authority(runtime),publication,ingress),"Full typed current authority must survive cold restore without exclusions.");
                    Save("restored-facts.json",new {captureTick,intent=IntentFacts(Intent(runtime)),ingress,typedAuthorityExact=true,literalTransportExact=true});
                    phase="ordinary-target-departure-and-resume";runtime.RequestPause(false);Assert.True(Wait(null,runtime,()=>!runtime.Latest.Paused,5));
                    Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,2,enemyOwner,PlayableCommandKind.Move,new[]{enemy},departure).AsHuman()).Accepted);
                    bool departureApplied=false,enemyMoved=false;NavPoint resumedPosition=interruptedPosition;
                    Assert.True(Wait(host,runtime,()=>{
                        departureApplied|=runtime.ReceiptsAfter(0).Any(r=>r.OwnerId==enemyOwner&&r.Sequence==2&&r.Status==PlayableCommandStatus.Applied);
                        var frame=runtime.OfflineFrame;var unit=frame.Views[player].Entities.Single(e=>e.Id==actor);
                        var foe=frame.Views[enemyOwner].Entities.SingleOrDefault(e=>e.Id==enemy);
                        Assert.NotNull(foe,"This frozen loss event is ordinary departure, not target death.");enemyMoved|=Distance(foe.Position,targetStart)>.2;
                        Assert.NotNull(unit.CurrentOrder);Assert.AreEqual(1,unit.CurrentOrder.CommandSequence);Assert.AreEqual(PlayableTacticalOrderKind.AttackMove,unit.CurrentOrder.Kind);
                        if(departureApplied&&enemyMoved&&unit.TargetId==0&&unit.Moving&&Distance(unit.Position,interruptedPosition)>.2&&Distance(unit.Position,goal)<Distance(interruptedPosition,goal)-.2){resumeTick=frame.Tick;resumedPosition=unit.Position;return true;}return false;
                    },60),"Natural target departure must resume actual movement toward the original assigned destination.");
                    Pause(host,runtime);CollectionAssert.AreEqual(originalIntent,Intent(runtime),"Resume must retain original root issuance and installed member route.");
                    Assert.False((bool)Field(Execution(runtime),"Suspended"));Assert.Greater(resumeTick,captureTick);
                    CollectionAssert.AreEqual(new[]{player+":1",enemyOwner+":2"}.OrderBy(x=>x).ToArray(),Watermarks(ActualIngress(runtime)));
                    Assert.Zero(runtime.BarrierPolls,"The ordinary asynchronous path must not enter a global route barrier.");
                    File.WriteAllBytes(Path.Combine(output,"resumed-current-v10.bin"),CaptureWithoutHost(runtime));
                    Save("result.json",new {result="PASS",motionTick,shotTick,captureTick,resumeTick,actor,enemy,originalIntent=IntentFacts(originalIntent),resumedPosition=Point(resumedPosition),ingress=ActualIngress(runtime),phase});
                    TestContext.WriteLine($"C2.3 AttackMove motion={motionTick} shot={shotTick} capture={captureTick} resume={resumeTick}; original issuance/member/assigned goal preserved; literal transport and full typed current authority exact.");
                }
            }
            catch(Exception error){try{Save("failure.json",new {phase,commandTick,motionTick,shotTick,captureTick,resumeTick,actor,enemy,error=error.ToString()});}catch(Exception preservationError){TestContext.WriteLine("Failure evidence write: "+preservationError);}throw;}
        }

        [Test,Timeout(120000)]
        public void OrdinaryExplicitAttackChaseRestoresInstalledRouteAndContinuesAfterVisibleTargetMove()
        {
            var origin=new NavPoint(-18,8);var targetStart=new NavPoint(-6,8);var targetMoved=new NavPoint(-9,8);
            var config=Configuration(origin,targetStart);var expectedProfile=PlayableProfile.Default;var profile=config.Profile;
            using(var sha=SHA256.Create())
                Assert.AreEqual(GenesisSha256,BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(GenesisPath()))).Replace("-","").ToLowerInvariant(),"Flat genesis source fixture changed.");
            Assert.AreEqual(Seed,config.Seed);Assert.AreEqual(Source,config.SourceIdentity);Assert.AreEqual(Map,config.MapIdentity);
            Assert.AreEqual(expectedProfile.ProfileId,profile.ProfileId);Assert.AreEqual(expectedProfile.Revision,profile.Revision);
            Assert.AreEqual(expectedProfile.SourceCommit,profile.SourceCommit);Assert.AreEqual(expectedProfile.SourceProfileId,profile.SourceProfileId);
            Assert.AreEqual(expectedProfile.SourceProfileRevision,profile.SourceProfileRevision);Assert.AreEqual(expectedProfile.SourceManifestSha256,profile.SourceManifestSha256);
            Assert.AreEqual(10d,profile.TankRange);Assert.AreEqual(13d,profile.TankVisionRange);Assert.AreEqual(.5d,profile.AttackRepathSeconds);
            Assert.AreEqual(2,config.Roster.Count);Assert.True(config.Roster.All(x=>x.Control==OfflineControl.Human),"No AI owners or production in this witness.");
            Assert.AreEqual(2,config.ScenarioUnits.Count);Assert.AreEqual(0,config.ScenarioBuildings.Count);
            Assert.AreEqual(origin,config.ScenarioUnits.Single(x=>x.LogicalPlayer==1).Position);Assert.AreEqual(targetStart,config.ScenarioUnits.Single(x=>x.LogicalPlayer==2).Position);
            var geometry=new NavGeometry(profile.ArenaHalfExtent,config.Obstacles.ToArray(),1);
            double radius=PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank);
            Assert.True(geometry.IsFree(origin,radius));Assert.True(geometry.IsFree(targetStart,radius));
            Assert.Greater(Distance(origin,targetStart),2*radius,"Initial bodies must not overlap.");
            Assert.AreEqual(12d,Distance(origin,targetStart));Assert.LessOrEqual(Distance(origin,targetStart),PlayableUnitRules.Vision(profile,PlayableEntityKind.Tank));
            Assert.AreEqual(3d,Distance(targetStart,targetMoved));Assert.LessOrEqual(Distance(origin,targetMoved),PlayableUnitRules.Vision(profile,PlayableEntityKind.Tank));
            Assert.LessOrEqual(Distance(origin,targetMoved),PlayableUnitRules.Range(profile,PlayableEntityKind.Tank));

            string player=config.Roster[0].Id,enemyOwner=config.Roster[1].Id;int actor=-1,enemy=-1;
            long firstRouteRequest=0,sourcePublication=0;byte[] checkpoint=null,canonicalAuthority=null;
            Dictionary<string,long> sourceWatermarks=null;
            PlayableSnapshot beforeRestore=null;
            using(var host=new UnityHostRouteService())
            {
                using(var runtime=new PlayableRuntime(config,Generation,startPaused:true))
                {
                    Assert.True(UntilWithoutHost(runtime,()=>runtime.IsPrepared&&runtime.Latest.Paused,30),"Ordinary runtime prepared and paused without starting the route host.");
                    Assert.AreEqual(PlayableAuthorityTick.AccruedRouteDeliveryPolicy,Authority(runtime).EffectiveRouteDeliveryPolicy,"Offline runtime uses its ordinary accrued per-unit route policy.");
                    var initial=runtime.ParticipantView(player);
                    Assert.AreEqual(2,initial.Entities.Count(e=>e.Kind==PlayableEntityKind.Tank),"The two authored Tank bodies should exist.");
                    Assert.AreEqual(2,initial.Entities.Count(e=>e.Id>0&&e.Owner==PlayableOwner.Player),"Offline genesis creates a Player Explorer in addition to its authored Tank.");
                    AssertAllAuthorityBodies(runtime,profile);
                    actor=initial.Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank).Id;
                    enemy=runtime.ParticipantView(enemyOwner).Entities.Single(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Tank).Id;
                    Assert.AreEqual(origin,initial.Entities.Single(e=>e.Id==actor).Position);
                    Assert.AreEqual(targetStart,initial.Entities.Single(e=>e.Id==enemy).Position);
                    Assert.True(initial.Entities.Any(e=>e.Id==enemy),"The 12m enemy must be visible before Attack expectations.");
                    long requestSequenceBefore=runtime.NavigationBinding.Admission.PublishedThroughRequestSequence;
                    runtime.RequestPause(false);
                    Assert.True(UntilWithoutHost(runtime,()=>!runtime.Latest.Paused,5),"Human ingress requires resumed authority.");
                    Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,1,player,PlayableCommandKind.Attack,new[]{actor},targetId:enemy).AsHuman()).Accepted);
                    Assert.True(UntilWithoutHost(runtime,()=>runtime.ReceiptsAfter(0).Any(r=>r.OwnerId==player&&r.Sequence==1&&r.Status==PlayableCommandStatus.Applied),10),"Explicit Attack must apply through human ingress.");
                    Assert.True(UntilWithoutHost(runtime,()=>runtime.NavigationBinding.Admission.PublishedThroughRequestSequence>requestSequenceBefore,10),"Attack chase must admit an ordinary host route request.");
                    firstRouteRequest=runtime.NavigationBinding.Admission.PublishedThroughRequestSequence;
                    Assert.AreEqual(requestSequenceBefore+1,firstRouteRequest,"One ordinary Attack chase request should be admitted.");
                    Assert.AreEqual(1,runtime.NavigationBinding.RoutePort.QueuedRequestCount,"One route request should be awaiting the host.");
                    long pendingTick=runtime.Latest.Tick;var pending=PendingRequest(runtime,actor);
                    Assert.NotNull(pending,"Actor-specific chase request must remain pending before host service.");
                    long requiredPendingTicks=checked((long)Math.Ceiling(profile.AttackRepathSeconds/RuntimeTickSeconds())+1);
                    Assert.Greater(requiredPendingTicks,5,"This wait must cross the authored Attack repath interval.");
                    var pendingWait=System.Diagnostics.Stopwatch.StartNew();
                    while(runtime.Latest.Tick<pendingTick+requiredPendingTicks&&pendingWait.Elapsed.TotalSeconds<5)Thread.Sleep(1);
                    Assert.GreaterOrEqual(runtime.Latest.Tick,pendingTick+requiredPendingTicks,"The same route must remain pending beyond the complete Attack repath interval.");
                    Assert.AreEqual(firstRouteRequest,runtime.NavigationBinding.Admission.PublishedThroughRequestSequence,"No repeated request while the chase route is pending.");
                    Assert.AreEqual(1,runtime.NavigationBinding.RoutePort.QueuedRequestCount,"The same single request must remain queued for the host.");
                    var pendingAfter=PendingRequest(runtime,actor);AssertSamePending(pending,pendingAfter);
                    Assert.False(runtime.ParticipantView(player).Entities.Single(e=>e.Id==actor).Moving,"No route can move before its host answer.");
                    Assert.Zero(runtime.BarrierPolls,"Accepted asynchronous per-unit delivery never enters the global route barrier.");
                    runtime.RequestPause(true);
                    Assert.True(UntilWithoutHost(runtime,()=>runtime.Latest.Paused,5),"Pause at a stable authority boundary after the repath timer expires.");
                    Assert.GreaterOrEqual(runtime.Latest.Tick,pendingTick+requiredPendingTicks);
                    AssertSamePending(pending,PendingRequest(runtime,actor));
                    Assert.AreEqual(firstRouteRequest,runtime.NavigationBinding.Admission.PublishedThroughRequestSequence,"No new request may be admitted after repath expiry while the original remains pending.");
                    Assert.Zero(ReadChase(Authority(runtime),actor).Repath,"A coherent paused authority read must show that the Attack repath timer expired.");
                    Assert.False(runtime.ParticipantView(player).Entities.Single(e=>e.Id==actor).Moving,"The actor remains stationary until its pending route is serviced.");
                    runtime.RequestPause(false);
                    Assert.True(UntilWithoutHost(runtime,()=>!runtime.Latest.Paused,5),"Resume without servicing the host so the original request remains pending.");
                    AssertSamePending(pending,PendingRequest(runtime,actor));

                    Assert.True(Until(host,runtime,()=>Distance(runtime.ParticipantView(player).Entities.Single(e=>e.Id==actor).Position,origin)>.5,60),"Attack route must activate and produce first ordinary movement.");
                    Assert.IsFalse(runtime.ParticipantView(player).Projectiles.Any(p=>p.OwnerId==actor&&p.TargetId==enemy),"Initial target is still outside TankRange before its small move.");
                    Assert.AreEqual(firstRouteRequest,runtime.NavigationBinding.Admission.PublishedThroughRequestSequence,"The active chase should retain the first admitted route identity.");
                    runtime.RequestPause(true);
                    Assert.True(Until(host,runtime,()=>runtime.Latest.Paused,5),"Freeze at a current installed-route capture boundary.");
                    beforeRestore=runtime.ParticipantView(player);
                    checkpoint=Capture(host,runtime);
                    using(var state=PlayableWorldState.Decode(checkpoint))
                    {
                        Assert.AreEqual(PlayableWorldState.CurrentVersion,state.Version);Assert.AreEqual(Seed,state.Seed);
                        Assert.AreEqual(Generation,state.Generation);Assert.AreEqual(beforeRestore.Tick,state.Tick);Assert.AreEqual(Source,state.SourceIdentity);
                    }
                    runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,5000),"Original runtime must stop before restoring its frozen current capture.");
                }

                ChaseProof originalProof;
                var probe=Probe(checkpoint,config,actor,out originalProof);
                try
                {
                    sourcePublication=probe.PublicationSequence;
                    sourceWatermarks=probe.HumanWatermarks.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
                    canonicalAuthority=CanonicalAuthority(probe,sourcePublication,sourceWatermarks);
                    Assert.AreEqual(enemy,originalProof.Target);Assert.True(originalProof.Explicit);Assert.False(originalProof.HasAttackMove);
                    Assert.AreEqual(PlayableTacticalOrderKind.Attack,originalProof.OrderKind);
                    Assert.Greater(originalProof.RouteRequest,0);Assert.Greater(originalProof.RouteOrder,0);
                    Assert.AreEqual(originalProof.RouteRequest,originalProof.AttemptRequest);Assert.AreEqual(originalProof.RouteOrder,originalProof.AttemptOrder);
                    Assert.AreEqual(originalProof.RouteRequest,originalProof.InstalledRequest);Assert.AreEqual(originalProof.RouteOrder,originalProof.InstalledOrder);
                    Assert.AreEqual(originalProof.Group,originalProof.InstalledGroup);Assert.AreEqual(originalProof.Revision,originalProof.InstalledRevision);
                    Assert.AreEqual(firstRouteRequest,originalProof.RouteRequest,"Tactical route identity must be the request admitted during this witness.");
                    Assert.GreaterOrEqual(originalProof.Repath,0);Assert.LessOrEqual(originalProof.Repath,profile.AttackRepathSeconds);
                }
                finally
                {
                    probe.Stop();probe.Domain.Navigation.ReleaseCommittedHostRoot();probe.Domain.DestroyOwnedNavigation();
                }

                using(var restoredHost=new UnityHostRouteService())
                using(var restored=PlayableRuntime.RestoreBytes(checkpoint,config))
                {
                    Assert.True(UntilWithoutHost(restored,()=>restored.IsPrepared&&restored.Latest.Paused,30),"Restored runtime must bind the saved current authority while remaining paused.");
                    var restoredView=restored.ParticipantView(player);
                    Assert.AreEqual(beforeRestore.Tick,restoredView.Tick);Assert.AreEqual(beforeRestore.Entities.Single(e=>e.Id==actor).Position,restoredView.Entities.Single(e=>e.Id==actor).Position);
                    Assert.AreEqual(beforeRestore.Entities.Single(e=>e.Id==actor).TargetId,restoredView.Entities.Single(e=>e.Id==actor).TargetId);
                    Assert.AreEqual(PlayableTacticalOrderKind.Attack,restoredView.Entities.Single(e=>e.Id==actor).CurrentOrder.Kind);
                    var roundTrip=Capture(restoredHost,restored);
                    using(var sourceState=PlayableWorldState.Decode(checkpoint))
                    using(var restoredState=PlayableWorldState.Decode(roundTrip))
                    {
                        Assert.AreEqual(sourceState.Version,restoredState.Version);Assert.AreEqual(sourceState.Seed,restoredState.Seed);
                        Assert.AreEqual(sourceState.Generation,restoredState.Generation);Assert.AreEqual(sourceState.Tick,restoredState.Tick);
                        Assert.AreEqual(sourceState.SourceIdentity,restoredState.SourceIdentity);
                        Assert.AreEqual(sourceState.RuntimeTransport.Length,restoredState.RuntimeTransport.Length);
                        CollectionAssert.AreEqual(sourceState.RuntimeTransport,restoredState.RuntimeTransport,"No commands occurred between captures; runtime transport must round trip exactly.");
                        var restoredAuthority=Authority(restored);
                        var restoredWatermarks=restoredAuthority.HumanWatermarks.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
                        CollectionAssert.AreEqual(sourceWatermarks.OrderBy(x=>x.Key).Select(x=>x.Key+":"+x.Value).ToArray(),restoredWatermarks.OrderBy(x=>x.Key).Select(x=>x.Key+":"+x.Value).ToArray(),"Human watermarks must round trip exactly.");
                        CollectionAssert.AreEqual(canonicalAuthority,CanonicalAuthority(restoredAuthority,sourcePublication,restoredWatermarks),"Complete typed authority state, including AI, navigation, participants, vision, and binding, must round trip byte-for-byte at the same publication and restored watermarks.");
                    }
                    ChaseProof restoredProof;
                    var restoredProbe=Probe(roundTrip,config,actor,out restoredProof);
                    try{AssertSameChase(originalProof,restoredProof);}finally{restoredProbe.Stop();restoredProbe.Domain.Navigation.ReleaseCommittedHostRoot();restoredProbe.Domain.DestroyOwnedNavigation();}

                    restored.RequestPause(false);
                    Assert.True(Until(restoredHost,restored,()=>!restored.Latest.Paused,5),"Restored authority must resume on its ordinary runtime thread.");
                    Assert.True(restored.TrySubmit(new PlayableCommand(Generation,2,enemyOwner,PlayableCommandKind.Move,new[]{enemy},targetMoved).AsHuman()).Accepted);
                    bool targetMoveApplied=false,targetMovedObserved=false,shot=false;long moveTick=-1,shotTick=-1;
                    Assert.True(Until(restoredHost,restored,() =>
                    {
                        targetMoveApplied|=restored.DrainReceipts().Any(r=>r.OwnerId==enemyOwner&&r.Sequence==2&&r.Status==PlayableCommandStatus.Applied);
                        var view=restored.ParticipantView(player);var tank=view.Entities.Single(e=>e.Id==actor);var foe=view.Entities.SingleOrDefault(e=>e.Id==enemy);
                        if(foe!=null&&Distance(foe.Position,targetMoved)<.2)targetMovedObserved=true;
                        if(targetMoveApplied&&shotTick<0&&view.Projectiles.Any(p=>p.OwnerId==actor&&p.TargetId==enemy)){shot=true;shotTick=view.Tick;moveTick=foe==null?-1:view.Tick;}
                        if(shot){Assert.AreEqual(enemy,tank.TargetId,"Explicit Attack should reacquire/retain the still-visible moved target.");Assert.True(Distance(tank.Position,foe.Position)<=profile.TankVisionRange);}
                        return targetMoveApplied&&targetMovedObserved&&shot;
                    },60),"Restored Attack must continue after the visible target's ordinary 3m move and fire.");
                    Assert.Greater(shotTick,beforeRestore.Tick);Assert.GreaterOrEqual(moveTick,beforeRestore.Tick);
                    var final=restored.ParticipantView(player).Entities.Single(e=>e.Id==actor);
                    Assert.AreEqual(PlayableTacticalOrderKind.Attack,final.CurrentOrder.Kind);
                    Assert.AreEqual(enemy,final.CurrentOrder.TargetId);Assert.AreEqual(enemy,final.TargetId);
                    TestContext.WriteLine("C2.3 explicit chase: request="+firstRouteRequest+" order="+originalProof.RouteOrder+" group="+originalProof.Group+" revision="+originalProof.Revision+" activation="+originalProof.ActivationTick+" captureTick="+beforeRestore.Tick+" postRestoreShotTick="+shotTick+" targetMove="+targetStart+"->"+targetMoved);
                }
            }
        }

        [Test,Timeout(120000)]
        public void OrdinaryExplicitAttackRepathsAfterOutwardTargetMoveAndRestoresPendingAttempt()
        {
            var origin=new NavPoint(-18,8);var targetStart=new NavPoint(-6,8);var targetMoved=new NavPoint(-4,8);
            var config=Configuration(origin,targetStart);var expectedProfile=PlayableProfile.Default;var profile=config.Profile;
            using(var sha=SHA256.Create())
                Assert.AreEqual(GenesisSha256,BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(GenesisPath()))).Replace("-","").ToLowerInvariant(),"Flat genesis source fixture changed.");
            Assert.AreEqual(Seed,config.Seed);Assert.AreEqual(Source,config.SourceIdentity);Assert.AreEqual(Map,config.MapIdentity);
            Assert.AreEqual(expectedProfile.ProfileId,profile.ProfileId);Assert.AreEqual(expectedProfile.Revision,profile.Revision);
            Assert.AreEqual(expectedProfile.SourceCommit,profile.SourceCommit);Assert.AreEqual(expectedProfile.SourceProfileId,profile.SourceProfileId);
            Assert.AreEqual(expectedProfile.SourceProfileRevision,profile.SourceProfileRevision);Assert.AreEqual(expectedProfile.SourceManifestSha256,profile.SourceManifestSha256);
            Assert.AreEqual(10d,PlayableUnitRules.Range(profile,PlayableEntityKind.Tank));
            Assert.AreEqual(13d,PlayableUnitRules.Vision(profile,PlayableEntityKind.Tank));Assert.AreEqual(.5d,profile.AttackRepathSeconds);
            Assert.AreEqual(2,config.Roster.Count);Assert.True(config.Roster.All(x=>x.Control==OfflineControl.Human),"No AI owners or production in this witness.");
            Assert.AreEqual(2,config.ScenarioUnits.Count);Assert.AreEqual(0,config.ScenarioBuildings.Count);
            Assert.AreEqual(origin,config.ScenarioUnits.Single(x=>x.LogicalPlayer==1).Position);Assert.AreEqual(targetStart,config.ScenarioUnits.Single(x=>x.LogicalPlayer==2).Position);
            var geometry=new NavGeometry(profile.ArenaHalfExtent,config.Obstacles.ToArray(),1);
            double radius=PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank),range=PlayableUnitRules.Range(profile,PlayableEntityKind.Tank);
            double vision=PlayableUnitRules.Vision(profile,PlayableEntityKind.Tank);
            Assert.True(geometry.IsFree(origin,radius));Assert.True(geometry.IsFree(targetStart,radius));Assert.True(geometry.IsFree(targetMoved,radius));
            Assert.Greater(Distance(origin,targetStart),2*radius);Assert.AreEqual(12d,Distance(origin,targetStart));
            Assert.AreEqual(2d,Distance(targetStart,targetMoved));Assert.LessOrEqual(Distance(origin,targetStart),vision);
            double observedMove=Distance(targetStart,targetMoved);
            var outwardDirection=new NavPoint((targetMoved.X-targetStart.X)/observedMove,(targetMoved.Z-targetStart.Z)/observedMove);
            // Move arrival covers the command point with the authored footprint. Keep the observed 2m point separate.
            var commandTarget=new NavPoint(targetMoved.X+radius*outwardDirection.X,targetMoved.Z+radius*outwardDirection.Z);
            Assert.True(geometry.IsFree(commandTarget,radius),"The footprint-derived command endpoint must fit unchanged fixture geometry.");
            double OutwardDisplacement(NavPoint position)=>(position.X-targetStart.X)*outwardDirection.X+(position.Z-targetStart.Z)*outwardDirection.Z;
            string evidenceOutput=Path.GetFullPath(Environment.GetEnvironmentVariable("SPACEWARS_C23_OUTWARD_OUTPUT")??
                Path.Combine(TestContext.CurrentContext.WorkDirectory,".local/qa/c23-outward-watermark-oracle"));
            if(Directory.Exists(evidenceOutput)&&Directory.EnumerateFileSystemEntries(evidenceOutput).Any())throw new InvalidOperationException("Outward evidence output must be empty: "+evidenceOutput);
            Directory.CreateDirectory(evidenceOutput);
            void SaveEvidence(string name,object value)=>File.WriteAllText(Path.Combine(evidenceOutput,name),Newtonsoft.Json.JsonConvert.SerializeObject(value,Newtonsoft.Json.Formatting.Indented));
            object NumericPoint(NavPoint p)=>new {x=p.X,z=p.Z};
            object NumericLocation(NavLocation? p)=>p.HasValue?new {x=p.Value.Position.X,z=p.Value.Position.Z,surface=p.Value.SurfaceId}:null;
            object ProofFacts(ChaseProof p)=>new {p.Target,p.Explicit,p.HasAttackMove,attackMove=NumericPoint(p.AttackMove),p.Repath,p.OrderKind,p.Group,p.Revision,p.ActivationTick,p.RouteRequest,p.RouteOrder,p.AttemptRequest,p.AttemptOrder,p.InstalledGroup,p.InstalledRevision,p.InstalledRequest,p.InstalledOrder};

            string player=config.Roster[0].Id,enemyOwner=config.Roster[1].Id;
            int actor=-1,enemy=-1;long initialRequest=0;byte[] pendingCapture=null;
            NavPoint pausedActorPosition=default(NavPoint);long pendingPublication=0;Dictionary<string,long> pendingWatermarks=null;
            ChaseProof pendingProof=null;NavigationRequest pendingRequest=null,enemyMoveRequest=null;long pendingCaptureTick=-1;
            long sequenceBeforeEnemyMove=0;NavLocation? originalActorGoal=null;
            PlayableEntitySnapshot lastActor=null;CertifiedRouteWorker initialWorker=null,enemyWorker=null;
            PlayableCommandReceipt enemyReceipt=null;
            using(var host=new UnityHostRouteService())
            using(var runtime=new PlayableRuntime(config,Generation,startPaused:true))
            {
                string OutwardDiagnostics(string phase,UnityHostRouteService activeHost=null)
                {
                    var frame=runtime.OfflineFrame;var admission=runtime.NavigationBinding.Admission;
                    var publication=activeHost?.CertifiedPublication??enemyWorker?.Publication??initialWorker?.Publication;
                    string Entities()=>frame==null?"unpublished":string.Join(";",frame.Views[player].Entities.Where(e=>e.Id==actor||e.Id==enemy)
                        .Select(e=>$"entity={e.Id},position={e.Position},moving={e.Moving},outcome={e.NavigationOutcome}"));
                    string Identity(NavigationRequest r)=>$"session={r.Session},request={r.Request},entity={r.Entity},order={r.Order},incarnation={r.MemberIdentity?.Incarnation},mobility={r.MemberIdentity?.MobilityRevision},group={r.MemberIdentity?.GroupId},revision={r.MemberIdentity?.OrderRevision},membership={r.MemberIdentity?.MembershipRevision}";
                    var receipts=runtime.ReceiptsAfter(0).Where(r=>r.OwnerId==enemyOwner&&r.Sequence==2);
                    return $" phase={phase}; enemyTerminal=[{string.Join(";",receipts.Select(r=>$"status={r.Status},tick={r.AppliedTick},message={r.Message}"))}];"+
                        $" frameTick={frame?.Tick},paused={runtime.Latest.Paused},failure={runtime.Latest.Failure}; admissionPublication={admission.PublicationIdentity},publishedRequest={admission.PublishedThroughRequestSequence},expectedEnemyRequest={sequenceBeforeEnemyMove+1};"+
                        $" admitted=[{string.Join(";",admission.Requests.Select(Identity))}]; mailbox=[{string.Join(";",runtime.Requests.CopyItems().Select(Identity))}];"+
                        $" answers=[{string.Join(";",runtime.Answers.CopyItems().Select(a=>Identity(a.Request)+",status="+a.Status))}]; bodies=[{Entities()}];"+
                        $" worker=[pumps={publication?.Pumps},failure={publication?.Failure},projectionFailure={publication?.ProjectionFailure}].";
                }
                Assert.True(UntilWithoutHost(runtime,()=>runtime.IsPrepared&&runtime.Latest.Paused,30),"Ordinary runtime prepared and paused without starting the route host.");
                Assert.AreEqual(PlayableAuthorityTick.AccruedRouteDeliveryPolicy,Authority(runtime).EffectiveRouteDeliveryPolicy,"Offline runtime uses its ordinary accrued per-unit route policy.");
                var initial=runtime.ParticipantView(player);
                Assert.AreEqual(2,initial.Entities.Count(e=>e.Kind==PlayableEntityKind.Tank));
                Assert.AreEqual(2,initial.Entities.Count(e=>e.Id>0&&e.Owner==PlayableOwner.Player));
                AssertAllAuthorityBodies(runtime,profile);
                actor=initial.Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank).Id;
                enemy=runtime.ParticipantView(enemyOwner).Entities.Single(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Tank).Id;
                Assert.AreEqual(origin,initial.Entities.Single(e=>e.Id==actor).Position);
                Assert.AreEqual(targetStart,initial.Entities.Single(e=>e.Id==enemy).Position);
                Assert.True(initial.Entities.Any(e=>e.Id==enemy),"The target is visible before Attack expectations.");

                long requestSequenceBefore=runtime.NavigationBinding.Admission.PublishedThroughRequestSequence;
                runtime.RequestPause(false);
                Assert.True(UntilWithoutHost(runtime,()=>!runtime.Latest.Paused,5),"Human ingress requires resumed authority.");
                Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,1,player,PlayableCommandKind.Attack,new[]{actor},targetId:enemy).AsHuman()).Accepted);
                Assert.True(UntilWithoutHost(runtime,()=>runtime.ReceiptsAfter(0).Any(r=>r.OwnerId==player&&r.Sequence==1&&r.Status==PlayableCommandStatus.Applied),10),"Explicit Attack must apply through human ingress.");
                Assert.True(UntilWithoutHost(runtime,()=>runtime.NavigationBinding.Admission.PublishedThroughRequestSequence>requestSequenceBefore,10),"Attack chase must admit its original ordinary host route request.");
                initialRequest=runtime.NavigationBinding.Admission.PublishedThroughRequestSequence;
                Assert.AreEqual(requestSequenceBefore+1,initialRequest,"One original Attack chase request should be admitted.");
                Assert.AreEqual(1,runtime.NavigationBinding.RoutePort.QueuedRequestCount,"The original route request is awaiting the host.");
                Assert.True(Until(host,runtime,() =>
                {
                    var frame=runtime.OfflineFrame;var view=frame.Views[player];var tank=RequireActor(runtime,frame,player,actor,lastActor,host);lastActor=tank;var foe=view.Entities.Single(e=>e.Id==enemy);
                    double distance=Distance(tank.Position,foe.Position);
                    return distance>range&&distance<=vision-2;
                },60),"The ordinary Attack route must reach a stable in-range-of-vision band before the outward move.");
                runtime.RequestPause(true);
                Assert.True(Until(host,runtime,()=>runtime.Latest.Paused,5),"Pause at a stable distance before moving the visible target outward.");
                var stableFrame=runtime.OfflineFrame;var stable=stableFrame.Views[player];var stableTank=RequireActor(runtime,stableFrame,player,actor,lastActor,host);lastActor=stableTank;var stableFoe=stable.Entities.Single(e=>e.Id==enemy);
                Assert.Greater(Distance(stableTank.Position,stableFoe.Position),range);
                Assert.LessOrEqual(Distance(stableTank.Position,stableFoe.Position),vision-2);
                Assert.IsFalse(stable.Projectiles.Any(p=>p.OwnerId==actor&&p.TargetId==enemy),"The target is still outside TankRange at the stable pause.");
                lastActor=stableTank;
                initialWorker=StopHostWorker(host,runtime);
                Assert.True(stableTank.Moving,"The initial installed Attack route must still be moving at the stable pause.");
                Assert.IsNull(PendingRequest(runtime,actor),"The initial Attack route must be installed, not still pending.");
                Assert.True(Authority(runtime).Domain.Navigation.Crowd.TryGet(actor,out var initialBody));
                Assert.AreEqual(initialRequest,initialBody.InstalledRequest,"The original Attack route must already be installed.");
                Assert.True(initialBody.GoalLocation.HasValue,"The installed Attack route must retain its typed goal.");
                originalActorGoal=initialBody.GoalLocation;
                Assert.True(geometry.IsFree(initialBody.Goal,radius),"The original installed goal must remain valid fixture geometry.");
                sequenceBeforeEnemyMove=runtime.NavigationBinding.Admission.PublishedThroughRequestSequence;
                Assert.AreEqual(initialRequest,sequenceBeforeEnemyMove,"No request may be admitted between the initial Attack route and the outward Move.");

                runtime.RequestPause(false);
                Assert.True(UntilWithoutHost(runtime,()=>!runtime.Latest.Paused,5),"Resume to admit the ordinary opposing-owner target Move.");
                var outwardBudget=System.Diagnostics.Stopwatch.StartNew();
                var enemySubmit=runtime.TrySubmit(new PlayableCommand(Generation,2,enemyOwner,PlayableCommandKind.Move,new[]{enemy},commandTarget).AsHuman());
                Assert.True(enemySubmit.Accepted,"Enemy Move ingress status="+enemySubmit.Status+OutwardDiagnostics("enemy-ingress"));
                Assert.True(UntilWithoutHost(runtime,()=>
                {
                    enemyReceipt=runtime.ReceiptsAfter(0).LastOrDefault(r=>r.OwnerId==enemyOwner&&r.Sequence==2&&r.Status!=PlayableCommandStatus.Accepted);
                    return enemyReceipt!=null;
                },Math.Max(0,60-outwardBudget.Elapsed.TotalSeconds)),"The ordinary enemy Move must produce a terminal receipt without host service."+OutwardDiagnostics("enemy-terminal"));
                Assert.AreEqual(PlayableCommandStatus.Applied,enemyReceipt.Status,"Enemy Move terminal message="+enemyReceipt.Message+OutwardDiagnostics("enemy-applied"));
                runtime.RequestPause(true);
                Assert.True(UntilWithoutHost(runtime,()=>runtime.Latest.Paused,5),"Pause after enemy Move admission before host service."+OutwardDiagnostics("enemy-admission-pause"));
                // Ordinary Move first admits an allocating arrival intent. It does not yet publish a route mailbox entry.
                enemyMoveRequest=PendingRequest(runtime,enemy);
                Assert.NotNull(enemyMoveRequest,OutwardDiagnostics("enemy-allocating-intent"));Assert.AreEqual(enemy,enemyMoveRequest.Entity);
                Assert.AreEqual(Generation,enemyMoveRequest.Session);Assert.NotNull(enemyMoveRequest.MemberIdentity);
                Assert.AreEqual(commandTarget,enemyMoveRequest.Goal,"The allocating intent targets the footprint-derived command point.");
                Assert.True(enemyMoveRequest.GoalLocation.HasValue);Assert.AreEqual(commandTarget,enemyMoveRequest.GoalLocation.Value.Position);
                Assert.AreEqual(sequenceBeforeEnemyMove+1,enemyMoveRequest.Request);
                Assert.AreEqual(sequenceBeforeEnemyMove+1,runtime.NavigationBinding.Admission.PublishedThroughRequestSequence,"Exactly the enemy Move intent may be admitted before host service."+OutwardDiagnostics("enemy-only-admission"));
                Assert.IsNull(PendingRequest(runtime,actor),"The actor must not have a fresh request before the host solves the enemy Move.");
                Assert.AreSame(enemyMoveRequest,PendingRequest(runtime,enemy),"The quiescent allocating intent is stable before arrival service.");
                var allocating=(IDictionary<int,NavigationRequest>)Field(Authority(runtime).Domain.Navigation,"allocating");
                Assert.True(allocating.TryGetValue(enemy,out var enemyAllocating),"Enemy Move must be in the allocating arrival phase before host service.");
                Assert.AreSame(enemyMoveRequest,enemyAllocating);

                using(var enemyHost=new UnityHostRouteService())
                {
                    // Arrival work needs host service, and Ready arrival publication needs resumed authority maintenance.
                    runtime.RequestPause(false);
                    Assert.True(UntilWithoutHost(runtime,()=>!runtime.Latest.Paused,5),"Resume authority for ordinary enemy arrival publication."+OutwardDiagnostics("enemy-arrival-resume"));
                    Assert.True(Until(enemyHost,runtime,()=>
                    {
                        var frame=runtime.OfflineFrame;var view=frame.Views[player];
                        lastActor=RequireActor(runtime,frame,player,actor,lastActor,enemyHost);
                        var foe=view.Entities.SingleOrDefault(e=>e.Id==enemy);
                        return foe!=null&&foe.Moving&&Distance(foe.Position,targetStart)>0;
                    },Math.Max(0,60-outwardBudget.Elapsed.TotalSeconds)),"The ordinary enemy Move must begin actual motion after arrival service."+OutwardDiagnostics("enemy-first-motion",enemyHost));
                    runtime.RequestPause(true);
                    Assert.True(UntilWithoutHost(runtime,()=>runtime.Latest.Paused,5),"Pause immediately after first observed enemy movement."+OutwardDiagnostics("enemy-first-motion-pause",enemyHost));
                    // This monotonic sequence guard rejects even a fresh actor request that a worker already consumed or rolled back.
                    long beforeEnemyHostStop=runtime.NavigationBinding.Admission.PublishedThroughRequestSequence;
                    string beforeEnemyHostStopDiagnostic=OutwardDiagnostics("before-enemy-host-stop",enemyHost);
                    enemyWorker=StopHostWorker(enemyHost,runtime);
                    Assert.AreEqual(sequenceBeforeEnemyMove+1,beforeEnemyHostStop,
                        "No fresh actor request may have been admitted before host quiescence."+beforeEnemyHostStopDiagnostic);
                    Assert.AreEqual(sequenceBeforeEnemyMove+1,runtime.NavigationBinding.Admission.PublishedThroughRequestSequence,
                        "Quiescence must preserve the sole enemy admission."+OutwardDiagnostics("after-enemy-host-stop"));
                    Assert.IsNull(PendingRequest(runtime,actor),"The fresh actor attempt must not exist before all host service stops.");
                    var navigation=Authority(runtime).Domain.Navigation;
                    Assert.True(navigation.Crowd.TryGet(actor,out var stillOriginal));
                    Assert.AreEqual(initialRequest,stillOriginal.InstalledRequest,"The actor must retain its original installed route after enemy host service.");
                    Assert.True(navigation.Crowd.TryGet(enemy,out var enemyBody));
                    Assert.IsNull(PendingRequest(runtime,enemy),"The enemy Move must be installed after its observed actual motion.");
                    // Arrival stages clone requests and assign endpoints. Compare stable intent identity with the actual installed execution.
                    Assert.AreEqual(enemyMoveRequest.Request,enemyBody.InstalledRequest);
                    Assert.AreEqual(enemyMoveRequest.Order,enemyBody.InstalledOrder);
                    Assert.AreEqual(enemyMoveRequest.MemberIdentity.Incarnation,enemyBody.Incarnation);
                    Assert.AreEqual(enemyMoveRequest.MemberIdentity.MobilityRevision,enemyBody.MobilityRevision);
                    Assert.AreEqual(enemyMoveRequest.MemberIdentity.GroupId,enemyBody.InstalledGroupId);
                    Assert.AreEqual(enemyMoveRequest.MemberIdentity.OrderRevision,enemyBody.InstalledRevision);
                    Assert.AreEqual(commandTarget,enemyBody.Goal);Assert.True(enemyBody.GoalLocation.HasValue);
                    Assert.AreEqual(commandTarget,enemyBody.GoalLocation.Value.Position);
                    var enemyExecution=((IDictionary<int,NavigationInstalledExecutionState>)Field(navigation,"installedExecutions"))[enemy];
                    Assert.NotNull(enemyExecution.Corridor,"Enemy actual motion must have an installed typed Ready route.");
                    Assert.Greater(enemyExecution.InstalledRoute.Length,0,"The installed enemy route must contain points.");
                    Assert.AreEqual(enemyMoveRequest.Session,enemyExecution.Corridor.World);
                    Assert.AreEqual(enemyMoveRequest.Request,enemyExecution.Corridor.Request);
                    Assert.AreEqual(enemyMoveRequest.Entity,enemyExecution.Corridor.Entity);
                    Assert.AreEqual(enemyMoveRequest.Order,enemyExecution.Corridor.Order);
                    Assert.AreEqual(commandTarget,enemyExecution.Corridor.Endpoint.Position);
                    var enemyGroup=((IDictionary<long,GroupOrderState>)Field(navigation,"groups"))[enemyBody.InstalledGroupId];
                    Assert.AreEqual(enemyMoveRequest.Session,enemyGroup.WorldGeneration);
                    Assert.AreEqual(enemyMoveRequest.MemberIdentity.MembershipRevision,enemyGroup.MembershipRevision);
                    Assert.AreEqual(2,enemyGroup.CommandSequence);Assert.AreEqual(enemyOwner,enemyGroup.OwnerId);
                    Assert.AreEqual(PlayableCommandKind.Move,enemyGroup.Kind);
                    Assert.AreEqual(commandTarget,enemyGroup.OriginalGoal);Assert.True(enemyGroup.OriginalLocation.HasValue);
                    Assert.AreEqual(commandTarget,enemyGroup.OriginalLocation.Value.Position);
                    var enemyMember=enemyGroup.Members.Single(m=>m.Entity==enemy);
                    Assert.AreEqual(commandTarget,enemyMember.Goal);Assert.True(enemyMember.GoalLocation.HasValue);
                    Assert.AreEqual(commandTarget,enemyMember.GoalLocation.Value.Position);
                    Assert.AreEqual(enemyMoveRequest.MemberIdentity.Incarnation,enemyMember.Incarnation);
                    Assert.AreEqual(enemyMoveRequest.MemberIdentity.MobilityRevision,enemyMember.MobilityRevision);
                    Assert.AreEqual(enemyMoveRequest.MemberIdentity.OrderRevision,enemyMember.OrderRevision);
                }

                runtime.RequestPause(false);
                Assert.True(UntilWithoutHost(runtime,()=>!runtime.Latest.Paused,5),"Resume with both ordinary routes installed and the host fully quiesced."+OutwardDiagnostics("full-move-resume"));
                Assert.True(UntilWithoutHost(runtime,()=>
                {
                    var frame=runtime.OfflineFrame;var view=frame.Views[player];
                    lastActor=RequireActor(runtime,frame,player,actor,lastActor,host,enemyWorker);
                    var foe=view.Entities.SingleOrDefault(e=>e.Id==enemy);
                    return foe!=null&&OutwardDisplacement(foe.Position)>=2d&&Distance(foe.Position,targetMoved)<.2&&!lastActor.Moving;
                },Math.Max(0,60-outwardBudget.Elapsed.TotalSeconds)),"The enemy must complete its ordinary 2m Move and the actor's original installed route must stop without host service."+OutwardDiagnostics("enemy-full-move-and-old-actor-stop"));
                runtime.RequestPause(true);
                Assert.True(UntilWithoutHost(runtime,()=>runtime.Latest.Paused,5),"Pause after the full outward move and original actor route stop.");

                var movedFrame=runtime.OfflineFrame;var moved=movedFrame.Views[player];var movedTank=RequireActor(runtime,movedFrame,player,actor,lastActor,host,enemyWorker);lastActor=movedTank;var movedFoe=moved.Entities.Single(e=>e.Id==enemy);
                Assert.True(moved.Entities.Any(e=>e.Id==enemy),"The outward-moved target remains visible to the actor owner.");
                Assert.Less(Distance(movedFoe.Position,targetMoved),.2);
                Assert.GreaterOrEqual(OutwardDisplacement(movedFoe.Position),2d,"The target must actually move at least the full 2m outward before capture.");
                Assert.False(movedTank.Moving,"The previously accepted route must finish and stop before the repath attempt.");
                Assert.Greater(Distance(movedTank.Position,movedFoe.Position),range,"The old route destination must now be outside TankRange.");
                Assert.LessOrEqual(Distance(movedTank.Position,movedFoe.Position),vision,"The moved target remains visible.");
                Assert.IsFalse(moved.Projectiles.Any(p=>p.OwnerId==actor&&p.TargetId==enemy));
                Assert.True(Authority(runtime).Domain.Navigation.Crowd.TryGet(actor,out var oldBody));
                Assert.True(oldBody.GoalLocation.HasValue,"The accepted old route retains a typed destination.");
                Assert.AreEqual(originalActorGoal,oldBody.GoalLocation,"The old installed typed goal remains unchanged through the target move.");
                Assert.True(geometry.IsFree(oldBody.Goal,radius),"The accepted old route destination remains valid geometry.");
                Assert.Greater(Distance(oldBody.Goal,movedFoe.Position),range,"The old accepted destination is no longer within TankRange of the outward target.");
                Assert.AreEqual(initialRequest,oldBody.InstalledRequest,"The old route remains the installed route until the fresh attempt is serviced.");
                double remainingRepath=ReadChase(Authority(runtime),actor).Repath;
                Assert.GreaterOrEqual(remainingRepath,0,"The stopped old route retains its authored Attack repath timer.");

                pendingRequest=PendingRequest(runtime,actor);
                if(pendingRequest==null)
                {
                    runtime.RequestPause(false);
                    Assert.True(UntilWithoutHost(runtime,()=>!runtime.Latest.Paused,5),"Resume without host service for the authored Attack repath cooldown.");
                    Assert.True(UntilWithoutHost(runtime,()=>runtime.Requests.CopyItems().Any(r=>r.Entity==actor),10),"The authored Attack repath cooldown must naturally admit one fresh actor request to the thread-safe route mailbox without host service."+OutwardDiagnostics("fresh-actor-mailbox"));
                    runtime.RequestPause(true);
                    Assert.True(UntilWithoutHost(runtime,()=>runtime.Latest.Paused,5),"Pause with the fresh actor request pending and unserviced.");
                    pendingRequest=PendingRequest(runtime,actor);
                }
                Assert.NotNull(pendingRequest);Assert.AreEqual(actor,pendingRequest.Entity);
                Assert.AreEqual(sequenceBeforeEnemyMove+2,runtime.NavigationBinding.Admission.PublishedThroughRequestSequence,"Exactly one enemy Move and one fresh actor request are admitted after the original route.");
                Assert.AreEqual(sequenceBeforeEnemyMove+1,enemyMoveRequest.Request,"The enemy Move is the only request admitted before the actor's fresh attempt.");
                Assert.AreEqual(sequenceBeforeEnemyMove+2,pendingRequest.Request,"The actor's fresh request is the sole request after the enemy Move.");
                Assert.AreNotEqual(initialRequest,pendingRequest.Request);
                Assert.True(pendingRequest.GoalLocation.HasValue,"The new request carries a typed target anchor.");
                Assert.True(geometry.IsFree(pendingRequest.Goal,radius),"The new request's authored goal remains within the fixture geometry.");
                CollectionAssert.Contains(runtime.Requests.CopyItems(),pendingRequest,"The exact fresh actor request remains queued and unserviced.");
                AssertSamePending(pendingRequest,PendingRequest(runtime,actor));
                Assert.AreEqual(sequenceBeforeEnemyMove+2,runtime.NavigationBinding.Admission.PublishedThroughRequestSequence,"No duplicate outward-target request may be admitted while the first remains pending.");
                Dictionary<string,long> ingressWatermarks;object ingressBarrier;
                // This is the same actual runtime gate and ingress barrier used by CompleteCapture, not the authority-local humans dictionary.
                lock(Field(runtime,"gate")){
                    Assert.True(runtime.Latest.Paused);Assert.AreNotEqual(0,(int)Field(runtime,"paused"));
                    int inbox=((ICollection)Field(runtime,"inbox")).Count,humanActions=((IDictionary)Field(runtime,"humanActions")).Count;
                    int tutorial=((ICollection)Field(runtime,"campaignTutorialControls")).Count,finish=(int)Field(runtime,"finishRequested");
                    Assert.Zero(inbox);Assert.Zero(humanActions);Assert.Zero(tutorial);Assert.Zero(finish);
                    Assert.IsNull(Field(runtime,"requestedBalance"));Assert.IsNull(Field(runtime,"requestedAiProfile"));Assert.IsNull(Field(runtime,"captureRequest"));
                    ingressWatermarks=new Dictionary<string,long>((IDictionary<string,long>)Field(runtime,"acceptedByOwner"),StringComparer.Ordinal);
                    ingressBarrier=new {paused=true,inbox,humanActions,tutorial,finish};
                }
                SaveEvidence("independent-ingress-oracle.json",new {ingressBarrier,watermarks=ingressWatermarks});
                Assert.AreEqual(2,ingressWatermarks.Count);
                CollectionAssert.AreEqual(new[]{"owner-11:1","owner-28:2"},ingressWatermarks.OrderBy(x=>x.Key).Select(x=>x.Key+":"+x.Value).ToArray(),"Independent actual human ingress must contain exactly both submitted commands.");
                pendingProof=ReadChase(Authority(runtime),actor);
                Assert.AreEqual(pendingRequest.Request,pendingProof.AttemptRequest);
                Assert.AreEqual(pendingRequest.Order,pendingProof.AttemptOrder);
                Assert.AreEqual(enemy,pendingProof.Target);Assert.True(pendingProof.Explicit);Assert.False(pendingProof.HasAttackMove);Assert.AreEqual(PlayableTacticalOrderKind.Attack,pendingProof.OrderKind);
                // The source Attack branch issues this matching attempt only after its authored Repath precondition has expired.
                Assert.LessOrEqual(remainingRepath,profile.AttackRepathSeconds,"The old explicit Attack retained only its authored cooldown before the fresh attempt.");
                Assert.LessOrEqual(pendingProof.Repath,profile.AttackRepathSeconds,"The naturally emitted explicit Attack attempt retains only its authored repath cooldown.");
                pausedActorPosition=RequireActor(runtime,runtime.OfflineFrame,player,actor,lastActor,host,enemyWorker).Position;
                pendingCapture=CaptureWithoutHost(runtime);
                // Persist the original buffer already created by this witness before any probe/cold assertions. No additional capture is requested here.
                File.WriteAllBytes(Path.Combine(evidenceOutput,"pending-current-v11.bin"),pendingCapture);
                var sourceFrame=runtime.OfflineFrame;var sourceNavigation=Authority(runtime).Domain.Navigation;
                Assert.True(sourceFrame.Views[player].Paused);Assert.True(initialWorker.IsStopped&&enemyWorker.IsStopped);
                using(var sourceState=PlayableWorldState.Decode(pendingCapture))using(var sha=SHA256.Create()){
                    object BodyFacts(int id){if(!sourceNavigation.Crowd.TryGet(id,out var body))return null;return new {body.Id,position=NumericPoint(body.Position),goal=NumericLocation(body.GoalLocation),body.Moving,body.Outcome,body.InstalledRequest,body.InstalledOrder};}
                    SaveEvidence("pending-source-facts.json",new {sourceState.Version,sourceState.Seed,sourceState.Generation,sourceState.Tick,sourceState.SourceIdentity,bytes=pendingCapture.Length,sha256=BitConverter.ToString(sha.ComputeHash(pendingCapture)).Replace("-","").ToLowerInvariant(),
                        profileBindingExact=sourceState.Binding.SequenceEqual(WorldWire.Binding(profile)),participantBindingExact=sourceState.Participants.SequenceEqual(OfflineConfigurationWire.Binding(config,profile)),config.MapIdentity,profile.ProfileId,profile.Revision,profile.SourceCommit,profile.SourceProfileId,profile.SourceProfileRevision,profile.SourceManifestSha256,
                        observedTarget=NumericPoint(targetMoved),commandTarget=NumericPoint(commandTarget),ingressWatermarks,actorBody=BodyFacts(actor),enemyBody=BodyFacts(enemy),pendingRequest=new {pendingRequest.Session,pendingRequest.Request,pendingRequest.Entity,pendingRequest.Order,start=NumericLocation(pendingRequest.StartLocation),goal=NumericLocation(pendingRequest.GoalLocation)},proof=ProofFacts(pendingProof),
                        frame=new {sourceFrame.Sequence,sourceFrame.Tick,views=sourceFrame.Views.Select(v=>new {owner=v.Key,entities=v.Value.Entities.Select(e=>new {e.Id,position=NumericPoint(e.Position),e.Health,e.ExactHealth,e.Moving,e.NavigationOutcome,e.TargetId}).ToArray()}).ToArray()}});
                }
                Assert.NotNull(pendingCapture);
                Assert.AreSame(pendingRequest,PendingRequest(runtime,actor),"Capture without host service must leave the new live request pending.");
                var literalProbe=Probe(pendingCapture,config,actor,out var literalProof);
                try{
                    pendingPublication=literalProbe.PublicationSequence;
                    pendingWatermarks=literalProbe.HumanWatermarks.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
                    SaveEvidence("literal-capture-probe.json",new {pendingPublication,watermarks=pendingWatermarks,independentIngress=ingressWatermarks,proof=ProofFacts(literalProof)});
                    CollectionAssert.AreEqual(ingressWatermarks.OrderBy(x=>x.Key).Select(x=>x.Key+":"+x.Value).ToArray(),pendingWatermarks.OrderBy(x=>x.Key).Select(x=>x.Key+":"+x.Value).ToArray(),"Literal capture watermarks must equal the independent actual ingress oracle.");
                    AssertSameChase(pendingProof,literalProof);
                }finally{literalProbe.Stop();literalProbe.Domain.Navigation.ReleaseCommittedHostRoot();literalProbe.Domain.DestroyOwnedNavigation();}
            }

            ChaseProof restoredProof;
            using(var restoredHost=new UnityHostRouteService())
            using(var restored=PlayableRuntime.RestoreBytes(pendingCapture,config))
            {
                Assert.True(UntilWithoutHost(restored,()=>restored.IsPrepared&&restored.Latest.Paused,30),"Current v11 capture restores paused before host service.");
                AssertAllAuthorityBodies(restored,profile);
                var restoredPending=PendingRequest(restored,actor);
                AssertSamePending(pendingRequest,restoredPending);
                Assert.AreEqual(pendingRequest.StartLocation,restoredPending.StartLocation);
                Assert.AreEqual(pendingRequest.GoalLocation,restoredPending.GoalLocation);
                Assert.AreEqual(pendingRequest.Request,restoredPending.Request);Assert.AreEqual(pendingRequest.Order,restoredPending.Order);
                restoredProof=ReadChase(Authority(restored),actor);AssertSameChase(pendingProof,restoredProof);
                Assert.AreEqual(enemy,restoredProof.Target);Assert.AreEqual(pendingRequest.Request,restoredProof.AttemptRequest);
                Assert.AreEqual(pendingRequest.Order,restoredProof.AttemptOrder);
                var restoredWatermarks=Authority(restored).HumanWatermarks.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
                SaveEvidence("restored-watermarks.json",new {pendingPublication,source=pendingWatermarks,restored=restoredWatermarks,proof=ProofFacts(restoredProof)});
                CollectionAssert.AreEqual(pendingWatermarks.OrderBy(x=>x.Key).Select(x=>x.Key+":"+x.Value).ToArray(),restoredWatermarks.OrderBy(x=>x.Key).Select(x=>x.Key+":"+x.Value).ToArray(),"All human owner watermarks survive the current pending capture.");
                var restoredFrame=restored.OfflineFrame;var restoredLastActor=RequireActor(restored,restoredFrame,player,actor,null,restoredHost);
                var roundTrip=CaptureWithoutHost(restored);
                File.WriteAllBytes(Path.Combine(evidenceOutput,"restored-roundtrip-current-v11.bin"),roundTrip);
                using(var sourceState=PlayableWorldState.Decode(pendingCapture))
                using(var restoredState=PlayableWorldState.Decode(roundTrip))
                {
                    Assert.AreEqual(PlayableWorldState.CurrentVersion,sourceState.Version);Assert.AreEqual(PlayableWorldState.CurrentVersion,restoredState.Version);
                    Assert.AreEqual(Seed,restoredState.Seed);Assert.AreEqual(Generation,restoredState.Generation);
                    Assert.AreEqual(sourceState.Tick,restoredState.Tick);Assert.AreEqual(Source,restoredState.SourceIdentity);
                    pendingCaptureTick=sourceState.Tick;
                    CollectionAssert.AreEqual(sourceState.RuntimeTransport,restoredState.RuntimeTransport,"Literal current runtime transport survives the pending capture round trip.");
                    var sourceAuthority=PlayableAuthorityTick.RestoreBytes(pendingCapture,config);
                    try
                    {
                        CollectionAssert.AreEqual(CanonicalAuthority(sourceAuthority,pendingPublication,pendingWatermarks),
                            CanonicalAuthority(Authority(restored),pendingPublication,restoredWatermarks),"Typed pending authority and order state survive cold restore before host service.");
                    }
                    finally
                    {
                        sourceAuthority.Stop();sourceAuthority.Domain.Navigation.ReleaseCommittedHostRoot();sourceAuthority.Domain.DestroyOwnedNavigation();
                    }
                }

                restored.RequestPause(false);
                bool moved=false,shot=false;
                Assert.True(Until(restoredHost,restored,() =>
                {
                    var frame=restored.OfflineFrame;var view=frame.Views[player];var tank=RequireActor(restored,frame,player,actor,restoredLastActor,restoredHost);restoredLastActor=tank;var foe=view.Entities.SingleOrDefault(e=>e.Id==enemy);
                    moved|=Distance(tank.Position,pausedActorPosition)>.1;
                    shot|=view.Projectiles.Any(p=>p.OwnerId==actor&&p.TargetId==enemy);
                    if(foe!=null&&shot){Assert.AreEqual(enemy,tank.TargetId,"Explicit Attack continues to track the still-visible outward-moving target.");Assert.LessOrEqual(Distance(tank.Position,foe.Position),vision);}
                    return moved&&shot;
                },60),"The restored pending route must receive ordinary threaded-host service, move the actor, and produce fire.");
                var final=RequireActor(restored,restored.OfflineFrame,player,actor,restoredLastActor,restoredHost);
                Assert.AreEqual(PlayableTacticalOrderKind.Attack,final.CurrentOrder.Kind);Assert.AreEqual(enemy,final.CurrentOrder.TargetId);Assert.AreEqual(enemy,final.TargetId);
                TestContext.WriteLine("C2.3 outward target: oldRequest="+initialRequest+" pendingRequest="+pendingRequest.Request+" order="+restoredProof.AttemptOrder+" group="+restoredProof.Group+" revision="+restoredProof.Revision+" captureTick="+pendingCaptureTick+" restoredMoveAndFire=true");
            }
        }

        [Test,Timeout(180000)]
        public void OrdinaryExplicitAttackSurvivesNaturalHeldCorridorFailureRestoreAndResumesWithoutReissue()
        {
            // Test genesis only. No route answer, pose, timer, profile or authority writes.
            const double gap=1.6;
            var origin=new NavPoint(-2,0);var heldAt=new NavPoint(0,0);var enemyAt=new NavPoint(10.5,0);var pocket=new NavPoint(4,4);
            var profile=PlayableProfile.Default;double radius=PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank);
            var walls=new[]{new NavObstacle(-1,gap,1,32),new NavObstacle(-1,-32,1,-gap)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,new NavPoint(-20,-20),Array.Empty<TerritorySlot>()),
                new TerritorySite(2,PlayableBuildingKind.Headquarters,new NavPoint(20,-20),Array.Empty<TerritorySlot>())};
            var starts=new[]{new OfflineStart("c23-left",1,sites[0].Position,new NavPoint(-20,-14),pin:1),
                new OfflineStart("c23-right",2,sites[1].Position,new NavPoint(20,-14),pin:2)};
            var roster=new[]{new OfflineParticipant("c23-p",1,1,OfflineControl.Human),new OfflineParticipant("c23-e",2,2,OfflineControl.Human)};
            var config=new OfflineMatchConfiguration(profile,"native-c23-natural-held-corridor-v1","test-flat-held-corridor-v1","UnityHostRouteService",Seed,
                roster,starts,sites,walls,new double[,]{{0,40},{40,0}},scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,origin),
                    new OfflineScenarioUnit(1,PlayableEntityKind.Tank,heldAt),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,enemyAt)});
            string player=roster[0].Id,foe=roster[1].Id;
            string output=Path.GetFullPath(Environment.GetEnvironmentVariable("SPACEWARS_C23_NATURAL_OUTPUT")??Path.Combine(TestContext.CurrentContext.WorkDirectory,".local/qa/c23-natural-explicit"));
            if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new InvalidOperationException("Natural failure output must be empty.");
            Directory.CreateDirectory(output);
            void Save(string name,object value)=>File.WriteAllText(Path.Combine(output,name),Newtonsoft.Json.JsonConvert.SerializeObject(value,Newtonsoft.Json.Formatting.Indented));
            object Point(NavPoint p)=>new {p.X,p.Z};
            object[] Facts(object[] values)=>values.Select(v=>v is NavLocation l?(object)new {position=Point(l.Position),surface=l.SurfaceId}:v).ToArray();
            var events=new List<object>();var cleanups=new List<object>();var cleanupErrors=new List<Exception>();
            int actor=-1,blocker=-1,enemy=-1;long commandTick=-1,motionTick=-1,failureTick=-1,failedRequest=0,firstEmission=0;
            int actorEmissions=0,actorFailures=0;long lastCombatEmissionTick=-1;string phase="preflight";object[] root=null;Exception primary=null;
            PlayableRuntime current=null;UnityHostRouteService host=null;
            object Unit(PlayableRuntime rt)=>((IDictionary)Field(Authority(rt).Domain,"units"))[actor];
            object Active(PlayableRuntime rt)=>Field(((IDictionary)Field(Authority(rt).Domain,"tacticalQueues"))[actor],"Active");
            double Timer(PlayableRuntime rt)=>(double)Field(Unit(rt),"Repath");
            object[] Root(PlayableRuntime rt)
            {
                Assert.True(rt.Latest.Paused);var nav=Authority(rt).Domain.Navigation;var active=Active(rt);var issuance=Field(active,"Issuance");
                var command=(PlayableCommand)Field(issuance,"Command");Assert.AreEqual(PlayableCommandKind.Attack,command.Kind);
                Assert.AreEqual(2,command.Sequence);Assert.AreEqual(player,command.PlayerId);Assert.AreEqual(enemy,command.TargetId);
                Assert.True((bool)Field(Unit(rt),"ExplicitTarget"));Assert.False((bool)Field(Unit(rt),"HasAttackMove"));
                Assert.AreEqual(enemy,(int)Field(Unit(rt),"Target"));Assert.False((bool)Field(active,"Interrupted"));
                var group=nav.GroupFor(actor);var member=group.Members.Single(m=>m.Entity==actor);Assert.True(nav.Crowd.TryGet(actor,out var body));
                Assert.AreEqual(group.GroupId,Field(issuance,"GroupId"));Assert.AreEqual(member.Incarnation,body.Incarnation);
                return new object[]{Field(issuance,"Id"),Field(issuance,"Revision"),Field(issuance,"Tick"),Field(issuance,"GroupId"),
                    Field(active,"Incarnation"),Field(active,"ActivationTick"),Field(active,"Assigned"),Field(Field(active,"Stamp"),"Revision"),
                    group.WorldGeneration,group.CommandSequence,group.IssuedTick,group.OriginalLocation,group.MembershipRevision,
                    member.Entity,member.Incarnation,member.MobilityRevision,member.OrderRevision,body.Incarnation,body.MobilityRevision,command.TargetId};
            }
            object[] Scheduling(PlayableRuntime rt)
            {
                var active=Active(rt);var terminal=Field(active,"TerminalFailure");var nav=Authority(rt).Domain.Navigation;var pending=PendingRequest(rt,actor);
                return new object[]{BitConverter.DoubleToInt64Bits(Timer(rt)),Field(active,"RouteRequest"),Field(active,"RouteOrder"),Field(active,"AttemptRequest"),Field(active,"AttemptOrder"),Field(active,"AttemptLocation"),
                    terminal==null?0L:Field(terminal,"Request"),terminal==null?0L:Field(terminal,"Order"),terminal==null?0L:Field(terminal,"Tick"),terminal==null?null:Field(terminal,"Outcome"),
                    pending?.Request??0,pending?.Order??0,nav.CaptureState().RequestSequence};
            }
            Dictionary<string,long> Watermarks(PlayableRuntime rt)
            {
                lock(Field(rt,"gate")){
                    Assert.True(rt.Latest.Paused);Assert.Zero(((ICollection)Field(rt,"inbox")).Count);
                    return new Dictionary<string,long>((IDictionary<string,long>)Field(rt,"acceptedByOwner"),StringComparer.Ordinal);
                }
            }
            string[] WatermarkFacts(IEnumerable<KeyValuePair<string,long>> marks)=>marks.OrderBy(x=>x.Key).Select(x=>x.Key+":"+x.Value).ToArray();
            bool Wait(Func<bool> condition,double seconds,bool service=true)
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();
                while(watch.Elapsed.TotalSeconds<seconds){
                    if(service&&host!=null)host.Service(current,profile.NavigationRequestsPerFrame);
                    Assert.IsNull(current.Latest.Failure,"Runtime failure in "+phase);Assert.IsNull(host?.CertifiedPublication?.Failure,"Shared host failure in "+phase);
                    if(commandTick>=0)Assert.LessOrEqual(current.Latest.Tick-commandTick,2700,"Frozen command-relative horizon");
                    if(condition())return true;Thread.Sleep(1);
                }
                return false;
            }
            void Pause(){current.RequestPause(true);Assert.True(Wait(()=>current.Latest.Paused,5,false));}
            void Submit(long sequence,string owner,PlayableCommandKind kind,int id,NavPoint destination=default(NavPoint),int target=0)
            {
                var result=current.TrySubmit(new PlayableCommand(Generation,sequence,owner,kind,new[]{id},destination,targetId:target).AsHuman());
                lock(events)events.Add(new {kind="command-attempt",phase,sequence,owner,command=kind.ToString(),id,accepted=result.Accepted});Assert.True(result.Accepted);
            }
            bool Applied(string owner,long sequence)=>current.ReceiptsAfter(0).Any(r=>r.OwnerId==owner&&r.Sequence==sequence&&r.Status==PlayableCommandStatus.Applied);
            void Observe(PlayableRuntime rt,bool pauseFirstEmission,bool pauseFirstFailure)
            {
                var nav=Authority(rt).Domain.Navigation;var domain=Authority(rt).Domain;
                nav.RouteRequested+=request=>{
                    if(request.Entity!=actor)return;
                    if(lastCombatEmissionTick>=0&&phase!="public-blocker-clear-and-resume")Assert.GreaterOrEqual((domain.Tick-lastCombatEmissionTick)*RuntimeTickSeconds(),profile.AttackRepathSeconds,"Combat emissions before geometry change respect authored cadence.");
                    lastCombatEmissionTick=domain.Tick;int count=Interlocked.Increment(ref actorEmissions);if(firstEmission==0)firstEmission=request.Request;
                    lock(events)events.Add(new {kind="request",phase,tick=domain.Tick,request.Request,request.Order,request.Entity,request.HeldIdentity,request.MemberIdentity.GroupId,request.MemberIdentity.OrderRevision,
                        request.MemberIdentity.Incarnation,request.MemberIdentity.MobilityRevision,request.MemberIdentity.MembershipRevision,goal=Point(request.Goal),timerBeforeEmissionReset=Timer(rt)});
                    if(pauseFirstEmission&&count==1)rt.RequestPause(true);
                };
                nav.RouteFailed+=(request,outcome)=>{
                    if(request.Entity!=actor)return;int count=Interlocked.Increment(ref actorFailures);failureTick=domain.Tick;failedRequest=request.Request;
                    var terminal=Field(Active(rt),"TerminalFailure");Assert.NotNull(terminal,"Actual failure handler must publish its terminal proof before observer.");
                    lock(events)events.Add(new {kind="real-route-failure",phase,tick=domain.Tick,request.Request,request.Order,outcome=outcome.ToString(),timer=Timer(rt),timerBits=BitConverter.DoubleToInt64Bits(Timer(rt)),terminalRequest=Field(terminal,"Request"),terminalOutcome=Field(terminal,"Outcome").ToString(),pending=nav.IsPending(actor)});
                    Assert.AreEqual(NavigationOutcome.Unreachable,outcome,"The real shared worker must produce physical NoPath, not rejection.");
                    if(pauseFirstFailure&&count==1)rt.RequestPause(true);
                };
                nav.RouteActivated+=request=>{lock(events)events.Add(new {kind="route-activated",phase,tick=domain.Tick,request.Entity,request.Request,request.Order,group=request.MemberIdentity.GroupId});};
                nav.MovementCancelled+=id=>{lock(events)events.Add(new {kind="movement-cancelled",phase,tick=domain.Tick,id});};
            }
            void DestroyProbe(PlayableAuthorityTick probe,string label)
            {
                var nav=probe.Domain.Navigation;var budget=nav.PhysicalHostBudget;
                probe.Stop();nav.ReleaseCommittedHostRoot();probe.Domain.DestroyOwnedNavigation();
                cleanups.Add(new {label,budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes});
                Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }
            void CloseBranch(string label)
            {
                if(current==null)return;var rt=current;var service=host;var nav=Authority(rt).Domain.Navigation;var budget=nav.PhysicalHostBudget;
                var worker=service==null?null:(CertifiedRouteWorker)Field(service,"worker");
                try{
                    rt.Dispose();service?.Dispose();
                    Assert.True(UntilWithoutHost(rt,()=>rt.IsStopped&&(worker==null||worker.IsStopped),10),"Both owned runtime and route worker must stop: "+label);
                    Assert.IsNull(worker?.Publication.Failure,"Independent worker cleanup failure");
                    nav.ReleaseCommittedHostRoot();Authority(rt).Domain.DestroyOwnedNavigation();
                    cleanups.Add(new {label,runtimeStopped=rt.IsStopped,workerStopped=worker==null||worker.IsStopped,budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes});
                    Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                }catch(Exception error){cleanupErrors.Add(error);}
                finally{current=null;host=null;}
            }
            byte[] Snapshot(string label,out object[] schedule,out byte[] typed,out long publication,out Dictionary<string,long> marks)
            {
                Assert.True(current.Latest.Paused);CollectionAssert.AreEqual(root,Root(current));schedule=Scheduling(current);marks=Watermarks(current);
                CollectionAssert.AreEqual(new[]{player+":2",foe+":1"}.OrderBy(x=>x).ToArray(),WatermarkFacts(marks));
                var bytes=CaptureWithoutHost(current);File.WriteAllBytes(Path.Combine(output,label+"-current-v11.bin"),bytes);
                using(var state=PlayableWorldState.Decode(bytes)){
                    Assert.AreEqual(11,state.Version);Assert.AreEqual(PlayableWorldState.CurrentVersion,state.Version);Assert.AreEqual(Seed,state.Seed);Assert.AreEqual(Generation,state.Generation);
                    Assert.AreEqual(config.SourceIdentity,state.SourceIdentity);CollectionAssert.AreEqual(WorldWire.Binding(profile),state.Binding);
                    CollectionAssert.AreEqual(OfflineConfigurationWire.Binding(config,profile),state.Participants);Assert.Greater(state.RuntimeTransport.Length,0);
                }
                var probe=PlayableAuthorityTick.RestoreBytes(bytes,config);
                try{publication=probe.PublicationSequence;typed=CanonicalAuthority(probe,publication,marks);CollectionAssert.AreEqual(WatermarkFacts(marks),WatermarkFacts(probe.HumanWatermarks));}
                finally{DestroyProbe(probe,label+"-probe");}
                Save(label+"-facts.json",new {tick=current.Latest.Tick,timer=Timer(current),timerBits=BitConverter.DoubleToInt64Bits(Timer(current)),root=Facts(root),schedule=Facts(schedule),marks});
                return bytes;
            }
            void Restore(byte[] bytes,object[] schedule,byte[] typed,long publication,Dictionary<string,long> marks,string label)
            {
                current=PlayableRuntime.RestoreBytes(bytes,config);Assert.True(Wait(()=>current.IsPrepared&&current.Latest.Paused,30,false));
                CollectionAssert.AreEqual(root,Root(current));CollectionAssert.AreEqual(schedule,Scheduling(current));
                CollectionAssert.AreEqual(WatermarkFacts(marks),WatermarkFacts(Watermarks(current)));
                CollectionAssert.AreEqual(WatermarkFacts(marks),WatermarkFacts(Authority(current).HumanWatermarks));
                var roundtrip=CaptureWithoutHost(current);File.WriteAllBytes(Path.Combine(output,label+"-restored-current-v11.bin"),roundtrip);
                using(var a=PlayableWorldState.Decode(bytes))using(var b=PlayableWorldState.Decode(roundtrip))CollectionAssert.AreEqual(a.RuntimeTransport,b.RuntimeTransport);
                CollectionAssert.AreEqual(typed,CanonicalAuthority(Authority(current),publication,marks),"Complete current typed authority without exclusions.");
                Save(label+"-restore.json",new {tick=current.Latest.Tick,exactTypedAuthority=true,exactRuntimeTransport=true,timerBits=BitConverter.DoubleToInt64Bits(Timer(current)),schedule=Facts(Scheduling(current))});
            }
            try
            {
                Assert.AreEqual(.72,radius);Assert.AreEqual(.5,profile.AttackRepathSeconds);Assert.AreEqual(32,profile.ArenaHalfExtent);
                Assert.Greater(Distance(origin,enemyAt),profile.TankRange);Assert.LessOrEqual(Distance(origin,enemyAt),profile.TankVisionRange);
                Assert.Greater(Distance(heldAt,enemyAt),profile.TankRange);Assert.Less(gap-radius,2*radius,"At x=0 every legal passage center lies within the expanded HOLD disk; no bypass inside arena.");
                var geometry=new NavGeometry(profile.ArenaHalfExtent,walls,1);
                foreach(var position in new[]{origin,heldAt,enemyAt,pocket,new NavPoint(2,0)})Assert.True(geometry.IsFree(position,radius));
                Assert.True(geometry.SegmentFree(heldAt,new NavPoint(2,0),radius));Assert.True(geometry.SegmentFree(new NavPoint(2,0),pocket,radius));
                Assert.True(geometry.SegmentFree(origin,enemyAt,profile.TankProjectileCollisionRadius),"Passage does not occlude initial visible target.");
                Save("preflight.json",new {Seed,Generation,config.SourceIdentity,config.MapIdentity,profile.ProfileId,profile.Revision,profile.SourceCommit,profile.SourceManifestSha256,
                    format=11,horizon=2700,timeoutMs=180000,gap,radius,origin=Point(origin),blocker=Point(heldAt),enemy=Point(enemyAt),pocket=Point(pocket),noPathProof="At x=0 |z|<=gap-radius=.88, expanded friendly HOLD disk radius=1.44; slabs reach arena boundary",clearPath="(0,0)->(2,0)->(4,4) segments fit full footprint"});
                current=new PlayableRuntime(config,Generation,startPaused:true);Assert.True(Wait(()=>current.IsPrepared&&current.Latest.Paused,30,false));
                var initial=current.ParticipantView(player);actor=initial.Entities.Single(e=>e.Kind==PlayableEntityKind.Tank&&e.Position.Equals(origin)).Id;
                blocker=initial.Entities.Single(e=>e.Kind==PlayableEntityKind.Tank&&e.Position.Equals(heldAt)).Id;
                enemy=current.ParticipantView(foe).Entities.Single(e=>e.Kind==PlayableEntityKind.Tank&&e.Position.Equals(enemyAt)).Id;
                Assert.True(initial.Entities.Any(e=>e.Id==enemy),"Real participant fog view must expose the enemy before Attack.");
                var nav=Authority(current).Domain.Navigation;
                foreach(var body in nav.Crowd.Units)Assert.True(Authority(current).Domain.Geometry.IsFree(body.Position,body.Radius),"All automatic and scenario bodies fit genesis.");
                Observe(current,true,false);current.RequestPause(false);Assert.True(Wait(()=>!current.Latest.Paused,5,false));
                Submit(1,player,PlayableCommandKind.Hold,blocker);Submit(1,foe,PlayableCommandKind.Hold,enemy);
                Assert.True(Wait(()=>Applied(player,1)&&Applied(foe,1),10,false));Pause();
                Assert.True(nav.Crowd.TryGet(blocker,out var held));Assert.True(held.Held);Assert.AreEqual(heldAt,held.Position);
                phase="original-explicit-attack-emission";commandTick=current.Latest.Tick;
                Save("ordinary-target-entered.json",new {actor,blocker,enemy,commandTick});
                current.RequestPause(false);Assert.True(Wait(()=>!current.Latest.Paused,5,false));Submit(2,player,PlayableCommandKind.Attack,actor,target:enemy);
                Assert.True(Wait(()=>Applied(player,2)&&current.Latest.Paused&&Volatile.Read(ref actorEmissions)==1,10,false));
                root=Root(current);var pending=PendingRequest(current,actor);Assert.NotNull(pending);Assert.AreEqual(firstEmission,pending.Request);
                Assert.Greater(pending.Goal.X,1+radius,"The natural approach lies in the far room.");Assert.True(pending.Geometry.Obstacles.Count>pending.BaseGeometry.Obstacles.Count,"Actual request includes full friendly HOLD footprint.");
                Assert.Greater(Timer(current),0);Assert.LessOrEqual(Timer(current),profile.AttackRepathSeconds);
                var checkpoint=Snapshot("pending-positive",out var pendingSchedule,out var pendingTyped,out var pendingPublication,out var pendingMarks);
                CloseBranch("source-pending");Assert.Zero(cleanupErrors.Count,"Independent cleanup must pass before next branch.");
                phase="restored-pending-duplicate-suppression";Restore(checkpoint,pendingSchedule,pendingTyped,pendingPublication,pendingMarks,"pending-positive");
                Observe(current,false,true);long waitFrom=current.Latest.Tick;int countBefore=actorEmissions;
                current.RequestPause(false);Assert.True(Wait(()=>!current.Latest.Paused,5,false));
                long required=checked((long)Math.Ceiling(profile.AttackRepathSeconds/RuntimeTickSeconds())+2);
                Assert.True(Wait(()=>current.Latest.Tick-waitFrom>=required,10,false));Pause();
                AssertSamePending(pending,PendingRequest(current,actor));Assert.AreEqual(countBefore,actorEmissions);Assert.Zero(Timer(current));
                Save("pending-suppression.json",new {waitFrom,to=current.Latest.Tick,requiredTicks=required,actorEmissions,pendingRequest=PendingRequest(current,actor).Request,timerBits=BitConverter.DoubleToInt64Bits(Timer(current))});
                phase="real-shared-worker-nopath";host=new UnityHostRouteService();
                Save("natural-failure-target-entered.json",new {tick=current.Latest.Tick,firstEmission});current.RequestPause(false);
                Assert.True(Wait(()=>Volatile.Read(ref actorFailures)>0&&current.Latest.Paused,60),"Real threaded host must deliver physical Unreachable and pause at the next ordinary boundary.");
                Assert.Greater(failedRequest,0);Assert.GreaterOrEqual(Timer(current),0);CollectionAssert.AreEqual(root,Root(current));
                Assert.AreEqual(origin,current.ParticipantView(player).Entities.Single(e=>e.Id==actor).Position,"Held corridor must not leak pursuer motion before clearance.");
                StopHostWorker(host,current);host=null;
                var failedCheckpoint=Snapshot("post-real-failure",out var failedSchedule,out var failedTyped,out var failedPublication,out var failedMarks);
                Save("failure-scheduling.json",new {failureTick,failedRequest,actorFailures,actorEmissions,tick=current.Latest.Tick,schedule=Facts(failedSchedule),timer=Timer(current),timerBits=BitConverter.DoubleToInt64Bits(Timer(current)),note="Failure event records handler state; ordinary same-tick retry may already replace terminal proof before paused capture."});
                CloseBranch("post-real-failure");Assert.Zero(cleanupErrors.Count);
                phase="restored-failure-scheduling";Restore(failedCheckpoint,failedSchedule,failedTyped,failedPublication,failedMarks,"post-real-failure");
                Observe(current,false,false);host=new UnityHostRouteService();
                phase="public-blocker-clear-and-resume";current.RequestPause(false);Assert.True(Wait(()=>!current.Latest.Paused,5,false));
                Submit(3,player,PlayableCommandKind.Move,blocker,pocket);NavPoint resumed=origin;
                Assert.True(Wait(()=>{
                    var frame=current.OfflineFrame;var pursuer=frame.Views[player].Entities.Single(e=>e.Id==actor);var block=frame.Views[player].Entities.Single(e=>e.Id==blocker);
                    Assert.NotNull(pursuer.CurrentOrder);Assert.AreEqual(PlayableTacticalOrderKind.Attack,pursuer.CurrentOrder.Kind);Assert.AreEqual(2,pursuer.CurrentOrder.CommandSequence);Assert.AreEqual(enemy,pursuer.CurrentOrder.TargetId);
                    if(Applied(player,3)&&Distance(block.Position,heldAt)>.2&&pursuer.Moving&&Distance(pursuer.Position,origin)>.2&&pursuer.Position.X>origin.X+.2){motionTick=frame.Tick;resumed=pursuer.Position;return true;}return false;
                },60),"Same original Attack must receive real service and move after public blocker clearance, with no pursuer reissue.");
                Pause();
                var expectedResumedRoot=(object[])root.Clone();
                expectedResumedRoot[15]=checked((long)root[15]+1);
                expectedResumedRoot[18]=checked((long)root[18]+1);
                CollectionAssert.AreEqual(expectedResumedRoot,Root(current));
                var finalNav=Authority(current).Domain.Navigation;
                Assert.True(finalNav.Crowd.TryGet(actor,out var moving));Assert.Greater(moving.InstalledRequest,failedRequest);
                Assert.AreEqual((long)root[3],moving.InstalledGroupId);Assert.AreEqual((long)root[7],moving.InstalledRevision);Assert.Zero(current.BarrierPolls);
                CollectionAssert.AreEqual(new[]{player+":3",foe+":1"}.OrderBy(x=>x).ToArray(),WatermarkFacts(Watermarks(current)));
                Save("resumed.json",new {motionTick,failureTick,failedRequest,actorEmissions,actorFailures,resumed=Point(resumed),moving.InstalledRequest,moving.InstalledOrder,moving.InstalledGroupId,moving.InstalledRevision,root=Facts(root),watermarks=Watermarks(current),sameExplicitAttack=true});
            }
            catch(Exception error){primary=error;}
            finally{
                CloseBranch("final");Save("events.json",events);Save("cleanup.json",new {cleanups,failures=cleanupErrors.Select(e=>e.ToString()).ToArray()});
                Save("result.json",new {result=primary==null&&cleanupErrors.Count==0?"PASS":"FAIL",phase,actor,blocker,enemy,commandTick,motionTick,failureTick,failedRequest,actorEmissions,actorFailures,primary=primary?.ToString(),cleanupFailures=cleanupErrors.Select(e=>e.ToString()).ToArray()});
            }
            if(primary!=null||cleanupErrors.Count>0)throw new AggregateException(new[]{primary}.Where(e=>e!=null).Concat(cleanupErrors));
        }

        private static bool c23DiagnosticStopped;
        private const string C23DiagnosticInputSha="22551ab25ed81703e0e5733d19d4afe0fd5d370ba7e185359a7ec14f955bb69b";

        private static OfflineMatchConfiguration C23DiagnosticConfiguration()
        {
            var p=PlayableProfile.Default;
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,new NavPoint(-20,-20),Array.Empty<TerritorySlot>()),new TerritorySite(2,PlayableBuildingKind.Headquarters,new NavPoint(20,-20),Array.Empty<TerritorySlot>())};
            return new OfflineMatchConfiguration(p,"native-c23-natural-held-corridor-v1","test-flat-held-corridor-v1","UnityHostRouteService",Seed,
                new[]{new OfflineParticipant("c23-p",1,1,OfflineControl.Human),new OfflineParticipant("c23-e",2,2,OfflineControl.Human)},
                new[]{new OfflineStart("c23-left",1,sites[0].Position,new NavPoint(-20,-14),pin:1),new OfflineStart("c23-right",2,sites[1].Position,new NavPoint(20,-14),pin:2)},
                sites,new[]{new NavObstacle(-1,1.6,1,32),new NavObstacle(-1,-32,1,-1.6)},new double[,]{{0,40},{40,0}},
                scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-2,0)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(0,0)),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(10.5,0))});
        }

        [Test,Explicit("Manual current-input component diagnostic; no ordinary game qualification."),Timeout(120000)]
        public void DiagnosticA_Current11BlockerReceiptAndRealSchedulerWrapInputs()=>C23CurrentInputDiagnostic(false);

        [Test,Explicit("Controlled paid-pressure component; not original game failure proof."),Timeout(120000)]
        public void DiagnosticB_Current11WrapPaidPressureRefusalInputs()=>C23CurrentInputDiagnostic(true);

        private static void C23CurrentInputDiagnostic(bool paidPressure)
        {
            if(c23DiagnosticStopped)Assert.Ignore("Previous unexplained diagnostic failure: STOP; no second component entry.");
            string name=paidPressure?"B-paid-pressure":"A-current-scheduler";
            string input=Environment.GetEnvironmentVariable("SPACEWARS_C23_COMPONENT_INPUT");
            string output=Path.Combine(Path.GetFullPath(Environment.GetEnvironmentVariable("SPACEWARS_C23_COMPONENT_OUTPUT")??Path.Combine(TestContext.CurrentContext.WorkDirectory,".local/qa/c23-components")),name);
            if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new InvalidOperationException("Component output must be empty.");
            Directory.CreateDirectory(output);
            void Save(string file,object value)=>File.WriteAllText(Path.Combine(output,file),Newtonsoft.Json.JsonConvert.SerializeObject(value,Newtonsoft.Json.Formatting.Indented));
            var samples=new object[128];var milestones=new List<object>();var cleanup=new List<object>();var cleanupErrors=new List<Exception>();
            object refusal=null;long calls=0,requestedTotal=0;string phase="input-preflight",outcome="NotRun";Exception primary=null;
            PlayableAuthorityTick authority=null;LayeredNavigationScheduler scheduler=null,candidate=null;
            LayeredNavigationCompileWork pressureCompiler=null;NavigationRetentionBudget.Scope activeScope=null,pressureScope=null,pressureSolveScope=null;
            var budget=new NavigationRetentionBudget();var config=C23DiagnosticConfiguration();int operations=0,consumedTotal=0;
            var queueField=typeof(LayeredNavigationScheduler).GetField("jobs",Hidden);
            var preparingField=typeof(LayeredNavigationScheduler).GetField("preparingJob",Hidden);
            var jobType=typeof(LayeredNavigationScheduler).GetNestedType("Job",BindingFlags.NonPublic);
            var compilerField=jobType.GetField("compiler",Hidden);
            var committedField=typeof(NavigationRetentionBudget).GetField("committed",Hidden);
            var heldField=typeof(NavigationRetentionBudget).GetField("held",Hidden);
            var physicalField=typeof(NavigationRetentionBudget).GetField("physical",Hidden);
            var flags=new[]{"cancelled","publishing","published","resolving","resolved"}.Select(f=>typeof(NavigationRetentionBudget.Scope).GetField(f,Hidden)).ToArray();
            LayeredNavigationCompileWork Compiler(LayeredNavigationScheduler value)
            {
                if(value==null)return null;var queue=queueField.GetValue(value);
                var job=queue.GetType().GetProperty("First",Hidden|BindingFlags.Public).GetValue(queue);
                return job==null?null:(LayeredNavigationCompileWork)compilerField.GetValue(job);
            }
            object Ledger(NavigationRetentionBudget value)=>new {value.PhysicalBytes,value.HeldBytes,value.ActiveScopes,value.CommittedBytes,value.PeakRetainedBytes,value.RetainedBytes};
            object Body(PlayableAuthorityTick value,int id)
            {
                var nav=value.Domain.Navigation;Assert.True(nav.Crowd.TryGet(id,out var body));var pending=(NavigationRequest)typeof(NavigationSession).GetMethod("PendingRequestFor",Hidden).Invoke(nav,new object[]{id});
                return new {id,tick=value.Tick,body.Held,body.Moving,position=new {body.Position.X,body.Position.Z},body.Incarnation,body.MobilityRevision,
                    body.InstalledRequest,body.InstalledOrder,pending=pending?.Request,pendingOrder=pending?.Order,pendingBinding=pending?.HeldIdentity,
                    publication=nav.Admission.PublicationIdentity,requestSequence=nav.CaptureState().RequestSequence};
            }
            // This observer never injects refusal. Scope.TryReserve makes the actual decision.
            budget.AllocationAdmission=charge=>{
                long committed=(long)committedField.GetValue(budget),held=(long)heldField.GetValue(budget),physical=(long)physicalField.GetValue(budget);
                long remaining=LayeredNavigationScheduler.MaxRetainedBytes-committed-held-physical;
                bool[] state=activeScope==null?null:flags.Select(f=>(bool)f.GetValue(activeScope)).ToArray();
                var selectedScheduler=candidate??scheduler;var job=selectedScheduler==null?null:preparingField.GetValue(selectedScheduler);var compiler=pressureCompiler??(job==null?null:(LayeredNavigationCompileWork)compilerField.GetValue(job))??Compiler(candidate??scheduler);
                bool deniedByInputs=charge>remaining||state!=null&&state.Take(4).Any(x=>x);
                string caller=deniedByInputs?new System.Diagnostics.StackTrace().ToString():null;
                var sample=new {call=++calls,phase,requestedCharge=charge,remaining,committed,held,physical,scopeToken=activeScope?.Token,
                    successfulLiveAndConvertedCredits=activeScope==null?(long?)null:activeScope.HeldBytes-NavigationRetentionBudget.ScopeBytes,
                    scopeFlags=state,flagOrder="cancelled,publishing,published,resolving,resolved",compilerPhase=compiler?.Phase.ToString(),observerReturns=true,deniedByInputs,caller};
                requestedTotal+=charge;samples[(calls-1)%samples.Length]=sample;if(deniedByInputs)refusal=sample;return true;
            };
            void ResolveCandidate()
            {
                candidate?.ReleaseStorage();candidate=null;activeScope?.Discard();activeScope=null;
            }
            try
            {
                Assert.False(string.IsNullOrWhiteSpace(input));var bytes=File.ReadAllBytes(input);
                using(var sha=SHA256.Create())Assert.AreEqual(C23DiagnosticInputSha,BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());
                using(var state=PlayableWorldState.Decode(bytes)){
                    Assert.AreEqual(11,state.Version);Assert.AreEqual(63,state.Tick);Assert.AreEqual(Generation,state.Generation);Assert.AreEqual(Seed,state.Seed);
                    Assert.AreEqual(config.SourceIdentity,state.SourceIdentity);CollectionAssert.AreEqual(WorldWire.Binding(config.Profile),state.Binding);CollectionAssert.AreEqual(OfflineConfigurationWire.Binding(config,config.Profile),state.Participants);
                }
                authority=PlayableAuthorityTick.RestoreBytes(bytes,config);Assert.AreEqual(63,authority.Tick);
                var nav=authority.Domain.Navigation;Assert.AreEqual(PlayableAuthorityTick.AccruedRouteDeliveryPolicy,authority.EffectiveRouteDeliveryPolicy);
                milestones.Add(new {kind="input-before-command",blocker=Body(authority,6),actor=Body(authority,5),ledger=Ledger(nav.PhysicalHostBudget)});
                Save("component-entered.json",new {name,inputSha=C23DiagnosticInputSha,tick=authority.Tick,manualComponent=true,ordinaryReplay=false,paidPressure});
                phase="actual-authority-command";
                var receipt=authority.Apply(new PlayableCommand(Generation,3,"c23-p",PlayableCommandKind.Move,new[]{6},new NavPoint(4,4)).AsHuman());
                milestones.Add(new {kind="actual-Apply-receipt",receipt.OwnerId,receipt.Sequence,receipt.AppliedTick,status=receipt.Status.ToString(),receipt.Message,blocker=Body(authority,6),actor=Body(authority,5),ledger=Ledger(nav.PhysicalHostBudget)});
                Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,"This component establishes actual command status, not an inference from old Accepted.");
                var actual=(NavigationRequest)typeof(NavigationSession).GetMethod("PendingRequestFor",Hidden).Invoke(nav,new object[]{6});Assert.NotNull(actual);
                // Recreate only immutable value input in a separate diagnostic ledger; bindings must remain exact.
                var obstacles=actual.Geometry.Obstacles.Select(o=>{Assert.IsNull(o.Polygon);return o.CircleRadius>0?new NavObstacle(o.CircleCenter,o.CircleRadius):new NavObstacle(o.MinX,o.MinZ,o.MaxX,o.MaxZ);}).ToArray();
                var geometry=new NavGeometry(actual.Geometry.HalfExtent,obstacles,actual.Geometry.Revision);
                var plain=new NavigationRequest(actual.Session,actual.Request,actual.Entity,actual.Order,actual.Profile,geometry,actual.Start,actual.Goal,geometry,actual.HeldIdentity,actual.MemberIdentity,
                    actual.StartLocation.HasValue?new NavLocation(actual.StartLocation.Value.Position,actual.StartLocation.Value.SurfaceId):(NavLocation?)null,
                    actual.GoalLocation.HasValue?new NavLocation(actual.GoalLocation.Value.Position,actual.GoalLocation.Value.SurfaceId):(NavLocation?)null);
                var rawProvider=NavigationAdmission.CertifiedFlatProvider(geometry);Assert.AreEqual(NavigationAdmission.CertifiedFlatProvider(actual.Geometry).Binding,rawProvider.Binding);
                var raw=(NavScheduledRequest)typeof(CertifiedRouteKernel).GetMethod("BuildScheduled",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{nav.Admission,plain,rawProvider});
                milestones.Add(new {kind="exact-derived-scheduler-input",actual.Request,actual.Order,actual.Entity,actual.HeldIdentity,providerBinding=raw.Provider.Binding,profileBinding=raw.Profile.ExactProfileBinding,raw.Profile.GridCell,raw.Clearance.Radius,raw.Limits.MaxSamples,raw.Limits.MaxNodes,raw.Limits.MaxArcs,
                    note="manual separate-ledger component; original input host graph is not counted in this new ledger; not original game refusal inputs"});
                phase="real-scheduler-birth";Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,Generation,1,out activeScope));
                var provider=LayeredNavigationProvider.CopyOwned((LayeredNavigationProvider)rawProvider,activeScope.OwnedReservation());
                try{
                    var request=NavScheduledRequest.CopyOwned(raw,provider,activeScope.OwnedReservation());
                    try{scheduler=LayeredNavigationScheduler.CreateOwned(activeScope.OwnedReservation());Assert.True(scheduler.Submit(request));}
                    finally{request.StorageRoot.ReleaseOwner();}
                }finally{((INavStorageFacade)provider).StorageRoot.ReleaseOwner();}
                scheduler.SealPrepared();activeScope.Attach(scheduler);activeScope.Commit(scheduler,_=>{});activeScope=null;
                var quota=(NavEpochQuota)typeof(CertifiedRouteKernel).GetField("Quota",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                phase="real-scheduler-compile-prefix";
                while(Compiler(scheduler)?.Phase!=LayeredNavigationCompileWork.Stage.WrapAdjacency){
                    Assert.Less(++operations,8192,"Frozen paid component operation bound; no ordinary ticks.");Assert.Less(consumedTotal,2000000,"Frozen compiler/scheduler work bound.");
                    var before=Compiler(scheduler);Assert.True(scheduler.PendingCount>0,"Scheduler completed before WrapAdjacency target.");
                    int allotment=before?.Phase==LayeredNavigationCompileWork.Stage.Adjacency?16:1024;
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,Generation,operations+1,out activeScope));candidate=scheduler.ForkOwned(activeScope.OwnedReservation());
                    int used=candidate.Advance(operations,quota,_=>true,allotment);consumedTotal+=used;
                    candidate.SealPrepared();activeScope.Attach(candidate);activeScope.Commit(candidate,_=>{});activeScope=null;
                    scheduler.ReleaseStorage();scheduler=candidate;candidate=null;
                }
                milestones.Add(new {kind="real-scheduler-WrapAdjacency-reached",operations,consumedTotal,ledger=Ledger(budget),blocker=Body(authority,6),actor=Body(authority,5)});
                if(!paidPressure){
                    phase="real-scheduler-one-Wrap-step";Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,Generation,operations+2,out activeScope));
                    candidate=scheduler.ForkOwned(activeScope.OwnedReservation());long creditsBefore=activeScope.HeldBytes-NavigationRetentionBudget.ScopeBytes;
                    int used=candidate.Advance(operations+1,quota,_=>true,1);Assert.AreEqual(1,used);
                    milestones.Add(new {kind="successful-paid-Wrap-step",creditsBefore,creditsAfter=activeScope.HeldBytes-NavigationRetentionBudget.ScopeBytes,ledger=Ledger(budget),compilerPhase=Compiler(candidate)?.Phase.ToString()});
                    candidate.SealPrepared();activeScope.Attach(candidate);activeScope.Commit(candidate,_=>{});activeScope=null;scheduler.ReleaseStorage();scheduler=candidate;candidate=null;
                    outcome="ObservedSuccessfulRealSchedulerWrapStep; original game refusal NOT reproduced";
                }else{
                    phase="controlled-compiler-Wrap-fork";Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,Generation,operations+2,out activeScope));
                    pressureCompiler=Compiler(scheduler).ForkOwned(activeScope.OwnedReservation());pressureSolveScope=activeScope;activeScope=null;
                    phase="controlled-paid-Held-pressure";Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,Generation,operations+3,out pressureScope));activeScope=pressureScope;
                    long paid=LayeredNavigationScheduler.MaxRetainedBytes-budget.RetainedBytes;Assert.Greater(paid,0);Assert.True(pressureScope.TryReserve(paid));
                    milestones.Add(new {kind="controlled-paid-pressure",paid,ledger=Ledger(budget),scopeToken=pressureScope.Token,note="held credits legally paid at existing cap; separate controlled component, never original game evidence"});
                    activeScope=pressureSolveScope;phase="controlled-paid-Wrap-Step";
                    for(int step=0;step<64;step++)pressureCompiler.Advance(1);
                    Assert.Fail("Controlled fully paid ledger did not reach a reserving Wrap step within frozen64 bound.");
                }
            }
            catch(Exception error){
                bool known=error is InvalidOperationException&&error.Message=="Physical storage exceeds prepared reservation."&&refusal!=null;
                if(known){outcome=paidPressure?"ExpectedControlledPaidCapacityDiagnostic; NOT original game proof":"ObservedComponentReservationRefusal; inspect exact inputs; NOT original game proof";milestones.Add(new {kind="expected-reservation-diagnostic",error=error.ToString(),inputs=refusal});}
                else{primary=error;c23DiagnosticStopped=true;outcome="UnexpectedFAIL_STOP";}
            }
            finally{
                // Manual authority and synchronous scheduler: no runtime/worker thread exists to ACK.
                // Cleanup deliberately never consults Latest.Failure and aggregates every owned teardown fault.
                try{pressureCompiler?.ReleaseStorage();pressureCompiler=null;}catch(Exception error){cleanupErrors.Add(error);}
                try{ResolveCandidate();}catch(Exception error){cleanupErrors.Add(error);}
                try{pressureScope?.Discard();pressureScope=null;}catch(Exception error){cleanupErrors.Add(error);}
                try{pressureSolveScope?.Discard();pressureSolveScope=null;}catch(Exception error){cleanupErrors.Add(error);}
                try{scheduler?.ReleaseStorage();scheduler=null;}catch(Exception error){cleanupErrors.Add(error);}
                budget.AllocationAdmission=null;
                try{cleanup.Add(new {kind="component-ledger",state=Ledger(budget)});Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}catch(Exception error){cleanupErrors.Add(error);}
                if(authority!=null){var nav=authority.Domain.Navigation;var inputBudget=nav.PhysicalHostBudget;
                    try{authority.Stop();nav.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();}catch(Exception error){cleanupErrors.Add(error);}
                    try{cleanup.Add(new {kind="manual-input-authority",worker="not-created",stop="synchronous Stop completed or separately recorded error",state=Ledger(inputBudget)});Assert.Zero(inputBudget.PhysicalBytes);Assert.Zero(inputBudget.HeldBytes);Assert.Zero(inputBudget.ActiveScopes);}catch(Exception error){cleanupErrors.Add(error);}
                }
                if(cleanupErrors.Count>0)c23DiagnosticStopped=true;
                Save("observations.json",new {name,phase,outcome,paidPressure,observerAlwaysTrue=true,calls,requestedTotal,milestones,refusal,lastSamples=samples,primary=primary?.ToString(),cleanup,cleanupErrors=cleanupErrors.Select(e=>e.ToString()).ToArray(),ordinaryGamePass=false});
            }
            if(primary!=null||cleanupErrors.Count>0)throw new AggregateException(new[]{primary}.Where(e=>e!=null).Concat(cleanupErrors));
        }

        private struct C23HostAllocationSample
        {
            public long Call,Charge,Remaining,CommittedIncludingRoot,Held,Physical,BudgetSequence,WorldTick,RootEpoch,RootProgress,Publication;
            public int ActiveScopes,Operation,Rows,Requests,Pending,Ready;
            public string Phase,CommittedCompilerPhases;
            public bool CapacityPredicate;
        }

        [Test,Explicit("Manual actual-host same-ledger component; no ordinary match/tick-pacing proof."),Timeout(120000)]
        public void DiagnosticC_Current11ActualHostSolveWrapReservationInputs()
        {
            string input=Environment.GetEnvironmentVariable("SPACEWARS_C23_HOST_INPUT");
            string output=Path.GetFullPath(Environment.GetEnvironmentVariable("SPACEWARS_C23_HOST_OUTPUT")??Path.Combine(TestContext.CurrentContext.WorkDirectory,".local/qa/c23-actual-host"));
            if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new InvalidOperationException("Actual host output must be empty.");
            Directory.CreateDirectory(output);
            void Save(string name,object value)=>File.WriteAllText(Path.Combine(output,name),Newtonsoft.Json.JsonConvert.SerializeObject(value,Newtonsoft.Json.Formatting.Indented));
            PlayableAuthorityTick authority=null;NavigationSession nav=null;NavigationRetentionBudget budget=null;CertifiedRouteKernel kernel=null;
            var ring=new C23HostAllocationSample[128];var milestones=new List<object>();var cleanupErrors=new List<Exception>();
            Exception primary=null;string phase="input-preflight",outcome="NotRun",denialCaller=null;C23HostAllocationSample? refusal=null;
            long calls=0,requestedTotal=0,workUpper=0,successfulReturnedWork=0;int operations=0;
            long contextEpoch=0,contextProgress=0,contextPublication=0;int contextRows=0,contextRequests=0,contextPending=0,contextReady=0;
            string contextPhases="unknown";bool componentTarget=false,prestepAccounted=false;int hostBudget=PlayableProfile.Default.NavigationRequestsPerFrame;
            var jobsField=typeof(LayeredNavigationScheduler).GetField("jobs",Hidden);
            var compilerField=typeof(LayeredNavigationScheduler).GetNestedType("Job",BindingFlags.NonPublic).GetField("compiler",Hidden);
            object Ledger()=>new {budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes,budget.CommittedBytes,budget.RetainedBytes,budget.PeakRetainedBytes};
            object Body(int id)
            {
                Assert.True(nav.Crowd.TryGet(id,out var body));var pending=(NavigationRequest)typeof(NavigationSession).GetMethod("PendingRequestFor",Hidden).Invoke(nav,new object[]{id});
                return new {id,body.Held,body.Moving,position=new {body.Position.X,body.Position.Z},body.Incarnation,body.MobilityRevision,body.InstalledRequest,body.InstalledOrder,
                    pending=pending?.Request,pendingOrder=pending?.Order,pendingBinding=pending?.HeldIdentity};
            }
            object Bank()
            {
                var bank=(NavPagedList<long>)Field(nav,"creditBank");
                return new {watermark=bank[NavigationAccruedCredits.Watermark],count=bank[NavigationAccruedCredits.Count],cap=bank[NavigationAccruedCredits.Cap],promoted=bank[NavigationAccruedCredits.Promoted],
                    firstTick=bank[NavigationAccruedCredits.Count]>0?NavigationAccruedCredits.First(bank).Tick:-1,
                    firstPrefix=bank[NavigationAccruedCredits.Count]>0?NavigationAccruedCredits.First(bank).Prefix:-1};
            }
            bool ObserveRoot(out bool hasBlocker,out bool hasWrap,out bool adjacency)
            {
                hasBlocker=false;hasWrap=false;adjacency=false;var root=nav.PinCommittedHostRoot();
                if(root==null)return false;
                try{
                    contextEpoch=root.Epoch;contextProgress=root.Progress;contextPublication=nav.Admission.PublicationIdentity;
                    contextRows=root.Rows.Count;contextRequests=root.Requests.Count;contextPending=root.Scheduler.PendingCount;contextReady=root.Scheduler.ReadyCount;
                    var phases=new List<string>();
                    foreach(var item in (IEnumerable)jobsField.GetValue(root.Scheduler)){
                        var compiler=(LayeredNavigationCompileWork)compilerField.GetValue(item);
                        if(compiler!=null){phases.Add(compiler.Phase.ToString());hasWrap|=compiler.Phase==LayeredNavigationCompileWork.Stage.WrapAdjacency;adjacency|=compiler.Phase==LayeredNavigationCompileWork.Stage.Adjacency;}
                    }
                    contextPhases=string.Join(",",phases);
                    foreach(var row in root.Rows.Values.OfType<NavigationHostRow>())if(row.Request.Entity==6)hasBlocker=true;
                    return root.QuantumRunnable||root.Frontier.Stage!=CertifiedRouteKernel.PumpStage.Idle;
                }finally{root.Release();}
            }
            void CheckBound(int allowedWork)
            {
                Assert.Less(operations,8192,"Frozen kernel operation bound");Assert.LessOrEqual(workUpper+allowedWork,2000000,"Frozen paid-work upper bound");
                operations++;workUpper+=allowedWork;Assert.AreEqual(63,authority.Tick,"No simulated game tick may advance.");
            }
            void PumpSavedQuantum()
            {
                var root=nav.PinCommittedHostRoot();int quantumBudget;
                try{
                    var bank=(NavPagedList<long>)Field(nav,"creditBank");
                    quantumBudget=root.QuantumRunnable?root.QuantumBudget:bank[NavigationAccruedCredits.Count]>0?NavigationAccruedCredits.First(bank).Budget:0;
                }finally{root.Release();}
                CheckBound(checked(quantumBudget*1024));ObserveRoot(out _,out _,out _);
                kernel.Pump(authority.NavigationBinding,hostBudget);nav.ApplyResults();Assert.AreEqual(63,authority.Tick);
            }
            try
            {
                Assert.False(string.IsNullOrWhiteSpace(input));var bytes=File.ReadAllBytes(input);var config=C23DiagnosticConfiguration();
                using(var sha=SHA256.Create())Assert.AreEqual(C23DiagnosticInputSha,BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());
                using(var state=PlayableWorldState.Decode(bytes)){Assert.AreEqual(11,state.Version);Assert.AreEqual(63,state.Tick);CollectionAssert.AreEqual(WorldWire.Binding(config.Profile),state.Binding);CollectionAssert.AreEqual(OfflineConfigurationWire.Binding(config,config.Profile),state.Participants);}
                authority=PlayableAuthorityTick.RestoreBytes(bytes,config);nav=authority.Domain.Navigation;budget=nav.PhysicalHostBudget;kernel=new CertifiedRouteKernel();
                budget.AllocationAdmission=charge=>{
                    // No reflection, stack walk, object sample or I/O on successful per-allocation observations.
                    long physical=budget.PhysicalBytes,held=budget.HeldBytes,retained=budget.RetainedBytes;
                    var sample=new C23HostAllocationSample{Call=++calls,Charge=charge,Remaining=LayeredNavigationScheduler.MaxRetainedBytes-retained,
                        CommittedIncludingRoot=retained-held-physical,Held=held,Physical=physical,ActiveScopes=budget.ActiveScopes,BudgetSequence=budget.Sequence,
                        WorldTick=authority.Tick,Operation=operations,Phase=phase,RootEpoch=contextEpoch,RootProgress=contextProgress,Publication=contextPublication,
                        Rows=contextRows,Requests=contextRequests,Pending=contextPending,Ready=contextReady,CommittedCompilerPhases=contextPhases,CapacityPredicate=charge>LayeredNavigationScheduler.MaxRetainedBytes-retained};
                    requestedTotal+=charge;ring[(calls-1)%ring.Length]=sample;
                    if(sample.CapacityPredicate&&refusal==null){refusal=sample;denialCaller=new System.Diagnostics.StackTrace().ToString();}
                    return true;
                };
                ObserveRoot(out _,out _,out _);
                milestones.Add(new {kind="same-input-ledger-before-Move",worldTick=authority.Tick,ledger=Ledger(),bank=Bank(),blocker=Body(6),actor=Body(5)});
                Save("component-entered.json",new {inputSha=C23DiagnosticInputSha,worldTick=63,sameRestoredPhysicalBudget=true,ordinaryGame=false});
                phase="actual-authority-Move";var receipt=authority.Apply(new PlayableCommand(Generation,3,"c23-p",PlayableCommandKind.Move,new[]{6},new NavPoint(4,4)).AsHuman());
                milestones.Add(new {kind="actual-Move-receipt",receipt.OwnerId,receipt.Sequence,receipt.AppliedTick,status=receipt.Status.ToString(),receipt.Message,blocker=Body(6),actor=Body(5),ledger=Ledger(),bank=Bank()});
                Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status);
                phase="normal-saved-credit-drain";
                while(true){
                    bool runnable=ObserveRoot(out bool registered,out _,out _);var bank=(NavPagedList<long>)Field(nav,"creditBank");
                    if(registered)break;
                    if(!runnable&&bank[NavigationAccruedCredits.Count]==0)break;
                    PumpSavedQuantum();
                }
                ObserveRoot(out bool blockerRegistered,out _,out _);
                if(!blockerRegistered){
                    var bank=(NavPagedList<long>)Field(nav,"creditBank");
                    if(bank[NavigationAccruedCredits.Watermark]>=authority.Tick+1){outcome="SourceBlocked: no eligible saved credit for blocker and next-boundary watermark already spent";}
                    else{
                        phase="normal-prestep-credit-admission";
                        CheckBound(0);Assert.True(nav.AccountAccruedCredit(authority.Tick+1,config.Profile.NavigationRequestsPerFrame,config.Profile.Revision,config.Profile.MaxAccruedCredits,authority.Domain));prestepAccounted=true;
                        milestones.Add(new {kind="one-normal-next-boundary-credit",worldTick=authority.Tick,upcoming=authority.Tick+1,bank=Bank(),ledger=Ledger(),note="normal authority-bound pre-step operation; no Domain.Step/world tick/timer write"});
                        while(!blockerRegistered){
                            bool runnable=ObserveRoot(out blockerRegistered,out _,out _);if(blockerRegistered)break;
                            bank=(NavPagedList<long>)Field(nav,"creditBank");if(!runnable&&bank[NavigationAccruedCredits.Count]==0)break;
                            PumpSavedQuantum();
                        }
                    }
                }
                ObserveRoot(out blockerRegistered,out bool wrap,out bool adjacency);
                if(!blockerRegistered){if(outcome=="NotRun")outcome="SourceBlocked: normal admission did not register blocker; no prefix/clock bypass";}
                else{
                    componentTarget=true;milestones.Add(new {kind="actual-blocker-host-row-registered",worldTick=authority.Tick,operations,workUpper,ledger=Ledger(),bank=Bank(),contextRows,contextRequests,contextPending,contextReady,contextPhases,blocker=Body(6),actor=Body(5)});
                    phase="actual-host-Solve-prefix";
                    while(!wrap){
                        ObserveRoot(out _,out wrap,out adjacency);if(wrap)break;
                        if(contextPending==0){outcome="ActualHostCompletedWithoutObservedWrap";break;}
                        int allowance=adjacency?16:1024;CheckBound(allowance);
                        successfulReturnedWork+=kernel.AdvancePhysicalOrdinary(authority.NavigationBinding,allowance);
                    }
                    if(wrap){
                        milestones.Add(new {kind="same-ledger-actual-host-Wrap-reached",worldTick=authority.Tick,operations,workUpper,successfulReturnedWork,ledger=Ledger(),contextRows,contextRequests,contextPending,contextReady,contextPhases,blocker=Body(6),actor=Body(5)});
                        phase="actual-host-one-Wrap-operation";CheckBound(1);ObserveRoot(out _,out _,out _);
                        successfulReturnedWork+=kernel.AdvancePhysicalOrdinary(authority.NavigationBinding,1);
                        milestones.Add(new {kind="actual-host-Wrap-operation-success",worldTick=authority.Tick,ledger=Ledger(),blocker=Body(6),actor=Body(5)});
                        outcome="ActualHostSameLedgerWrapOperationSucceeded; original game refusal not reproduced";
                    }
                }
                Assert.True(nav.Crowd.TryGet(5,out var actorBody));Assert.AreEqual(new NavPoint(-2,0),actorBody.Position);
                Assert.True(nav.Crowd.TryGet(6,out var blockerBody));Assert.AreEqual(new NavPoint(0,0),blockerBody.Position);Assert.AreEqual(63,authority.Tick);
            }
            catch(Exception error){
                if(error is InvalidOperationException&&error.Message=="Physical storage exceeds prepared reservation."){
                    outcome="ObservedActualHostStorageRefusalDiagnostic; original match cause remains separately qualified";
                    milestones.Add(new {kind="actual-host-storage-refusal",error=error.ToString(),capacityInputs=refusal,lastSample=calls==0?(C23HostAllocationSample?)null:ring[(calls-1)%ring.Length],scopeTokenAndFlags="not safely reachable from production local reservation; BudgetSequence is ledger sequence, not claimed as actual Scope.Token",denialCaller});
                }else{primary=error;outcome="UnexpectedFAIL_STOP";}
            }
            finally{
                if(budget!=null)budget.AllocationAdmission=null;
                if(authority!=null){
                    try{authority.Stop();}catch(Exception error){cleanupErrors.Add(error);}
                    try{kernel?.Clear();}catch(Exception error){cleanupErrors.Add(error);}
                    try{
                        if(nav.HasCertifiedHost)throw new InvalidOperationException("Kernel detach ACK absent; live-owner root destruction withheld.");
                        nav.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();
                    }catch(Exception error){cleanupErrors.Add(error);}
                }
                object census=budget==null?null:Ledger();
                if(budget!=null)try{Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}catch(Exception error){cleanupErrors.Add(error);}
                Save("observations.json",new {outcome,phase,componentTarget,prestepAccounted,operations,workUpper,successfulReturnedWork,calls,requestedTotal,observerAlwaysTrue=true,
                    milestones,refusal,denialCaller,lastSamples=ring,actualScopeTokenFlags="unknown; no runtime hook/API added",primary=primary?.ToString(),cleanupErrors=cleanupErrors.Select(e=>e.ToString()).ToArray(),census,
                    ownerModel="same restored navigation ledger and actual committed host root/RoutePort/kernel; no standalone compiler/provider/scheduler, no paid pressure, no thread worker; synchronous Stop/kernel detach/root destroy",ordinaryGamePass=false});
            }
            if(primary!=null||cleanupErrors.Count>0)throw new AggregateException(new[]{primary}.Where(e=>e!=null).Concat(cleanupErrors));
        }

        private struct C23ThreadedAllocationSample
        {
            public long Call,Charge,Remaining,CommittedIncludingRoot,Held,Physical,BudgetSequence,LastMainObservedTick;
            public int Thread,Phase,ActiveScopes;
            public bool CapacityPredicate;
        }
        private struct C23ThreadedNavigationSample
        {
            public long Tick,Request,Order,Group,Revision,Incarnation,Mobility;
            public int Kind,Entity,Outcome;public bool Present,Held,Moving,Pending;public double X,Z;
        }

        [Test,Explicit("One current-input real-threaded diagnostic continuation; never original natural witness PASS."),Timeout(180000)]
        public void DiagnosticD_Current11ThreadedContinuationActualLedgerRefusalAndBlockerReceipt()
        {
            string input=Environment.GetEnvironmentVariable("SPACEWARS_C23_THREADED_INPUT");
            string output=Path.GetFullPath(Environment.GetEnvironmentVariable("SPACEWARS_C23_THREADED_OUTPUT")??Path.Combine(TestContext.CurrentContext.WorkDirectory,".local/qa/c23-threaded-ledger"));
            if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new InvalidOperationException("Threaded diagnostic output must be empty.");
            Directory.CreateDirectory(output);
            void Save(string name,object value)=>File.WriteAllText(Path.Combine(output,name),Newtonsoft.Json.JsonConvert.SerializeObject(value,Newtonsoft.Json.Formatting.Indented));
            var allocationRing=new C23ThreadedAllocationSample[128];var navigationRing=new C23ThreadedNavigationSample[128];
            var receipts=new List<object>();var receiptKeys=new HashSet<string>();var milestones=new List<object>();var cleanupErrors=new List<Exception>();var captureErrors=new List<Exception>();
            PlayableRuntime runtime=null;UnityHostRouteService host=null;CertifiedRouteWorker worker=null;NavigationSession nav=null;NavigationRetentionBudget budget=null;
            long allocationCalls=0,totalRequested=0,lastMainTick=63,navEvents=0,lastRecordedTick=-1;int phase=0,workerThread=-1;
            C23ThreadedAllocationSample? firstCapacity=null,lastCapacity=null;string firstCapacityCaller=null;
            string runtimeFailure=null,workerFailure=null,outcome="NotRun";Exception primary=null;bool entered=false,moveAccepted=false,applied=false,rejected=false,resumed=false,acks=false;
            C23ThreadedNavigationSample? firstBlockerRequest=null,firstBlockerActivation=null;long stopTick=-1;
            var config=C23DiagnosticConfiguration();var initialActor=new NavPoint(-2,0);var initialBlocker=new NavPoint(0,0);
            object Ledger()=>budget==null?null:new {budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes,budget.CommittedBytes,budget.RetainedBytes,budget.PeakRetainedBytes,budget.Sequence};
            object RootFacts()
            {
                var root=nav.PinCommittedHostRoot();if(root==null)return new {missing=true};
                try{return new {root.Epoch,root.Progress,root.AdmissionPrefix,root.QuantumTick,root.QuantumBudget,root.QuantumRunnable,frontier=root.Frontier.Stage.ToString(),rows=root.Rows.Count,requests=root.Requests.Count,pending=root.Scheduler.PendingCount,ready=root.Scheduler.ReadyCount,
                    entries=root.Rows.Values.OfType<NavigationHostRow>().Select(row=>new {row.Request.Entity,row.Request.Request,row.Request.Order,row.Request.HeldIdentity,goal=new {row.Request.Goal.X,row.Request.Goal.Z},group=row.Request.MemberIdentity?.GroupId,revision=row.Request.MemberIdentity?.OrderRevision,hasAnswer=row.Answer!=null,hasSolved=row.Solved!=null}).ToArray()};}
                finally{root.Release();}
            }
            void NavEvent(int kind,int id,NavigationRequest request=null,NavigationOutcome outcomeValue=NavigationOutcome.Idle)
            {
                // Existing navigation callbacks execute on the authority owner outside prepared allocation scopes.
                bool present=nav.Crowd.TryGet(id,out var body);
                var sample=new C23ThreadedNavigationSample{Kind=kind,Tick=Authority(runtime).Tick,Entity=id,Request=request?.Request??0,Order=request?.Order??0,
                    Group=request?.MemberIdentity?.GroupId??0,Revision=request?.MemberIdentity?.OrderRevision??0,Incarnation=present?body.Incarnation:0,Mobility=present?body.MobilityRevision:0,
                    Present=present,Held=present&&body.Held,Moving=present&&body.Moving,Pending=nav.IsPending(id),X=present?body.Position.X:0,Z=present?body.Position.Z:0,Outcome=(int)outcomeValue};
                navigationRing[navEvents++%navigationRing.Length]=sample;
                if(id==6&&kind==1&&firstBlockerRequest==null)firstBlockerRequest=sample;
                if(id==6&&kind==2&&firstBlockerActivation==null)firstBlockerActivation=sample;
            }
            void ObserveImmutableFrame()
            {
                var latest=runtime.Latest;Volatile.Write(ref lastMainTick,latest.Tick);if(latest.Tick==lastRecordedTick)return;lastRecordedTick=latest.Tick;
                foreach(var receipt in runtime.ReceiptsAfter(0)){
                    string key=receipt.OwnerId+":"+receipt.Sequence+":"+receipt.Status;if(!receiptKeys.Add(key))continue;
                    receipts.Add(new {receipt.OwnerId,receipt.Sequence,receipt.AppliedTick,status=receipt.Status.ToString(),receipt.Message,observedTick=latest.Tick});
                    if(receipt.OwnerId=="c23-p"&&receipt.Sequence==3){applied|=receipt.Status==PlayableCommandStatus.Applied;rejected|=receipt.Status!=PlayableCommandStatus.Applied&&receipt.Status!=PlayableCommandStatus.Accepted;}
                }
            }
            bool Wait(Func<bool> condition,double seconds,bool service)
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();
                while(watch.Elapsed.TotalSeconds<seconds){
                    if(service){host.Service(runtime,config.Profile.NavigationRequestsPerFrame);if(worker==null){worker=(CertifiedRouteWorker)Field(host,"worker");if(worker!=null)workerThread=((Thread)Field(worker,"thread")).ManagedThreadId;}}
                    ObserveImmutableFrame();if(condition())return true;
                    if(runtime.Latest.Failure!=null||host?.CertifiedPublication?.Failure!=null)return false;Thread.Sleep(1);
                }
                return condition();
            }
            try
            {
                Assert.False(string.IsNullOrWhiteSpace(input));var bytes=File.ReadAllBytes(input);
                using(var sha=SHA256.Create())Assert.AreEqual(C23DiagnosticInputSha,BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());
                using(var state=PlayableWorldState.Decode(bytes)){Assert.AreEqual(11,state.Version);Assert.AreEqual(63,state.Tick);Assert.AreEqual(Generation,state.Generation);Assert.AreEqual(Seed,state.Seed);CollectionAssert.AreEqual(WorldWire.Binding(config.Profile),state.Binding);CollectionAssert.AreEqual(OfflineConfigurationWire.Binding(config,config.Profile),state.Participants);}
                runtime=PlayableRuntime.RestoreBytes(bytes,config);Assert.True(Wait(()=>runtime.IsPrepared&&runtime.Latest.Paused,30,false));
                Assert.AreEqual(63,runtime.Latest.Tick);nav=Authority(runtime).Domain.Navigation;budget=nav.PhysicalHostBudget;
                Assert.AreEqual(PlayableAuthorityTick.AccruedRouteDeliveryPolicy,Authority(runtime).EffectiveRouteDeliveryPolicy);
                phase=1;
                budget.AllocationAdmission=charge=>{
                    // Only the already-held ledger gate is touched. No reflection, transport/nav/runtime API,
                    // authority gate, capture, anonymous object or per-success stack/string allocation here.
                    long physical=budget.PhysicalBytes,held=budget.HeldBytes,retained=budget.RetainedBytes;
                    var sample=new C23ThreadedAllocationSample{Call=++allocationCalls,Charge=charge,Remaining=LayeredNavigationScheduler.MaxRetainedBytes-retained,
                        CommittedIncludingRoot=retained-held-physical,Held=held,Physical=physical,ActiveScopes=budget.ActiveScopes,BudgetSequence=budget.Sequence,
                        Thread=Thread.CurrentThread.ManagedThreadId,Phase=Volatile.Read(ref phase),LastMainObservedTick=Volatile.Read(ref lastMainTick),CapacityPredicate=charge>LayeredNavigationScheduler.MaxRetainedBytes-retained};
                    totalRequested+=charge;allocationRing[(allocationCalls-1)%allocationRing.Length]=sample;
                    if(sample.CapacityPredicate){lastCapacity=sample;if(firstCapacity==null){firstCapacity=sample;firstCapacityCaller=new System.Diagnostics.StackTrace().ToString();}}
                    return true;
                };
                nav.RouteRequested+=request=>NavEvent(1,request.Entity,request);
                nav.RouteActivated+=request=>NavEvent(2,request.Entity,request);
                nav.RouteFailed+=(request,reason)=>NavEvent(3,request.Entity,request,reason);
                nav.MovementCancelled+=id=>NavEvent(4,id);
                var pre=CaptureWithoutHost(runtime);File.WriteAllBytes(Path.Combine(output,"restored-pre-service-current-v11.bin"),pre);
                using(var original=PlayableWorldState.Decode(bytes))using(var restored=PlayableWorldState.Decode(pre))CollectionAssert.AreEqual(original.RuntimeTransport,restored.RuntimeTransport);
                milestones.Add(new {kind="paused-before-worker-and-ingress",tick=runtime.Latest.Tick,ledger=Ledger(),root=RootFacts()});
                phase=2;host=new UnityHostRouteService();entered=true;Save("continuation-entered.json",new {inputSha=C23DiagnosticInputSha,inputTick=63,maxFinalTick=2763,maxTicks=2700,pauseGuardTick=2760,seed=Seed,generation=Generation,noPursuerReissue=true});
                runtime.RequestPause(false);Assert.True(Wait(()=>!runtime.Latest.Paused,5,false));
                var submit=runtime.TrySubmit(new PlayableCommand(Generation,3,"c23-p",PlayableCommandKind.Move,new[]{6},new NavPoint(4,4)).AsHuman());moveAccepted=submit.Accepted;
                milestones.Add(new {kind="real-ingress-blocker-Move",sequence=3,owner="c23-p",entity=6,goal=new {X=4,Z=4},submit.Accepted,observedTick=runtime.Latest.Tick});Assert.True(submit.Accepted);
                phase=3;var diagnosticClock=System.Diagnostics.Stopwatch.StartNew();
                while(true){
                    ObserveImmutableFrame();runtimeFailure=runtime.Latest.Failure;workerFailure=host.CertifiedPublication?.Failure;
                    if(runtimeFailure!=null||workerFailure!=null){outcome="ObservedMeaningfulRuntimeOrWorkerFailure_STOP";break;}
                    long tick=runtime.Latest.Tick;Assert.LessOrEqual(tick,2763,"Frozen actual authority horizon must not be exceeded.");
                    if(tick>=2760){outcome="NOTREPRODUCED_atGuardedActualTickDiagnosticLimit";break;}
                    var view=runtime.ParticipantView("c23-p");var actor=view.Entities.SingleOrDefault(e=>e.Id==5);var blocker=view.Entities.SingleOrDefault(e=>e.Id==6);
                    if(actor==null){outcome="DiagnosticActorAbsent_STOP";break;}
                    if(applied&&actor.Moving&&actor.Position.X>initialActor.X+.2&&Distance(actor.Position,initialActor)>.2&&actor.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&actor.CurrentOrder.CommandSequence==2&&actor.CurrentOrder.TargetId==7){resumed=true;outcome="SameOriginalAttackResumedMotion_CauseNotReproduced";break;}
                    if(blocker==null){outcome="DiagnosticBlockerAbsent_STOP_CauseNotReproduced";break;}
                    Assert.Less(diagnosticClock.Elapsed.TotalSeconds,125,"Wall safety guard; an unchanged continuation is never retried.");
                    host.Service(runtime,config.Profile.NavigationRequestsPerFrame);
                    if(worker==null){worker=(CertifiedRouteWorker)Field(host,"worker");if(worker!=null)workerThread=((Thread)Field(worker,"thread")).ManagedThreadId;}
                    Thread.Sleep(1);
                }
                phase=4;stopTick=runtime.Latest.Tick;runtime.RequestPause(true);
                if(runtimeFailure==null&&workerFailure==null)Assert.True(Wait(()=>runtime.Latest.Paused,5,false),"Normal diagnostic outcome pauses at an authority boundary.");
                ObserveImmutableFrame();Assert.LessOrEqual(runtime.Latest.Tick,2763,"Final actual authority horizon including pause boundary.");runtimeFailure=runtime.Latest.Failure??runtimeFailure;workerFailure=host.CertifiedPublication?.Failure??workerFailure;
                milestones.Add(new {kind="observed-stop-boundary",stopTick,outcome,applied,rejected,resumed,ledger=Ledger(),root=RootFacts(),runtimeFailure,workerFailure,
                    actor=runtime.ParticipantView("c23-p").Entities.SingleOrDefault(e=>e.Id==5),blocker=runtime.ParticipantView("c23-p").Entities.SingleOrDefault(e=>e.Id==6)});
                if(runtimeFailure!=null||workerFailure!=null){
                    bool known=(runtimeFailure??"").Contains("Physical storage exceeds prepared reservation.")||(workerFailure??"").Contains("Physical storage exceeds prepared reservation.");
                    if(known)outcome="ObservedActualThreadedStorageRefusalDiagnostic; original earlier FAIL remains independent";
                    else throw new InvalidOperationException("Unexpected meaningful continuation failure. Runtime="+runtimeFailure+" Worker="+workerFailure);
                }else{
                    var post=CaptureWithoutHost(runtime);File.WriteAllBytes(Path.Combine(output,"paused-post-continuation-current-v11.bin"),post);
                }
            }
            catch(Exception error){primary=error;outcome="UnexpectedFAIL_STOP";}
            finally{
                phase=5;
                if(budget!=null)budget.AllocationAdmission=null;
                if(runtime!=null){
                    try{if(worker==null&&host!=null)worker=(CertifiedRouteWorker)Field(host,"worker");runtime.Dispose();host?.Dispose();}catch(Exception error){cleanupErrors.Add(error);}
                    var wait=System.Diagnostics.Stopwatch.StartNew();
                    while(wait.Elapsed.TotalSeconds<10&&(!runtime.IsStopped||worker!=null&&!worker.IsStopped))Thread.Sleep(1);
                    acks=runtime.IsStopped&&(worker==null||worker.IsStopped);
                    if(!acks)cleanupErrors.Add(new InvalidOperationException("Runtime/worker stop ACK missing; live root destruction withheld."));
                    workerFailure=worker?.Publication.Failure??workerFailure;runtimeFailure=runtime.Latest.Failure??runtimeFailure;
                    if(acks&&nav!=null){
                        // Terminal evidence only: both real owners ACKed; copy existing value, never candidate.
                        try{
                            var guard=nav.LastGroupGuardObservationForQa;
                            Save("terminal-guard.json",new {afterRuntimeAndWorkerACK=true,beforeDomainDestroy=true,tick=Authority(runtime).Tick,
                                observedGuardMismatch=guard.Mismatch!=0,mismatchNumeric=(ulong)guard.Mismatch,mismatchNames=guard.Mismatch.ToString(),
                                observation=guard,commandKindName=guard.CommandKind.ToString(),
                                qualification=guard.Mismatch==0?"DefaultZero_NoObservedGuardTrigger":"ExistingGuardValueCopiedAfterACK",
                                disposedCandidateAccessed=false});
                        }catch(Exception error){captureErrors.Add(error);}
                        try{
                            var terminalReceipts=runtime.ReceiptsAfter(0);
                            Save("terminal-receipts.json",new {afterRuntimeAndWorkerACK=true,beforeDomainDestroy=true,independentOfTickDeduplication=true,
                                tick=Authority(runtime).Tick,rows=terminalReceipts.Select(receipt=>new {receipt.OwnerId,receipt.Sequence,receipt.AppliedTick,
                                    Status=(int)receipt.Status,StatusName=receipt.Status.ToString(),receipt.Message}).ToArray()});
                        }catch(Exception error){captureErrors.Add(error);}
                        try{
                            milestones.Add(new {kind="after-stop-ACK-before-destroy",runtimeStopped=runtime.IsStopped,workerStopped=worker==null||worker.IsStopped,ledger=Ledger(),root=RootFacts()});
                            // No runtime capture is requested from a stopped thread. This optional typed capture
                            // has no literal runtime transport and is labelled separately from runtime envelopes.
                            if(runtimeFailure!=null||workerFailure!=null){
                                try{var authority=Authority(runtime);var marks=new Dictionary<string,long>((IDictionary<string,long>)Field(runtime,"acceptedByOwner"),StringComparer.Ordinal);
                                    using(var state=authority.CaptureWorldState(authority.PublicationSequence,id=>marks.TryGetValue(id,out var sequence)?sequence:0)){
                                        File.WriteAllBytes(Path.Combine(output,"post-stop-typed-authority-current-v11.bin"),state.Encode());
                                        milestones.Add(new {kind="post-stop-typed-capture",state.Version,state.Tick,runtimeTransportLength=state.RuntimeTransport.Length,literalRuntimeEnvelope=false});
                                    }
                                }catch(Exception error){captureErrors.Add(error);}
                            }
                        }catch(Exception error){cleanupErrors.Add(error);}
                        try{if(nav.HasCertifiedHost)throw new InvalidOperationException("Certified host detach missing after stop ACK; root destruction withheld.");nav.ReleaseCommittedHostRoot();Authority(runtime).Domain.DestroyOwnedNavigation();}catch(Exception error){cleanupErrors.Add(error);}
                    }
                }
                object census=Ledger();
                if(budget!=null&&acks)try{Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}catch(Exception error){cleanupErrors.Add(error);}
                Save("observations.json",new {outcome,entered,inputTick=63,stopTick,moveAccepted,applied,rejected,resumed,acks,workerThread,phaseMeaning="1pre-service capture,2resume/ingress,3ordinary service,4stop boundary,5cleanup",allocationCalls,totalRequested,
                    observerAlwaysTrue=true,scopeTokenFlags="NotSafelyDiscoverable without runtime hook; BudgetSequence is ledger counter, not asserted Scope.Token",firstCapacity,lastCapacity,firstCapacityCaller,
                    allocationRing,navigationEvents=navEvents,navigationKind="1requested,2activated,3failed,4cancelled",firstBlockerRequest,firstBlockerActivation,navigationRing,receipts,milestones,
                    runtimeFailure,workerFailure,primary=primary?.ToString(),captureErrors=captureErrors.Select(e=>e.ToString()).ToArray(),cleanupErrors=cleanupErrors.Select(e=>e.ToString()).ToArray(),census,
                    originalNaturalGamePass=false,nativeCompactions=0});
            }
            if(primary!=null||cleanupErrors.Count>0)throw new AggregateException(new[]{primary}.Where(e=>e!=null).Concat(cleanupErrors));
        }
    }
}
