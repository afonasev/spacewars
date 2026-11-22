using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    // Actual Domain/current policy setup; the Native input fixture below tests
    // the owning contract only. It is NOT evidence of a production Nav consumer.
    public sealed class AiCombatMicroExecutionOwnerTests
    {
        const long Gen=907;const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static object Field(object o,string key)=>o.GetType().GetField(key,F).GetValue(o);
        sealed class World:IDisposable
        {
            internal readonly PlayableProfile P;
            internal readonly PlayableDomain D;internal readonly PlayableAiOwnerLoop Owner;internal readonly OfflineMatchConfiguration Config;
            internal readonly int Patient;internal readonly int[] Members;
            internal AiCombatMicroExecutionOwner Registry=>Owner.ExecutionOwner;
            internal World(int units=4,bool returning=false,bool recast=false,bool repair=false,bool useTowerApproach=false)
            {
                var data=PlayableProfile.Default.CopyData();
                if(recast)data.groundRouteBackend=1;
                P=PlayableProfile.Create(data,new AiTestMap());
                var map=(AiTestMap)P.AuthoredMap;var authored=map.Configuration(P,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)});var starts=authored.Starts.ToArray();
                var points=new System.Collections.Generic.List<NavPoint>{new NavPoint(-96,-36),new NavPoint(-92,-36),new NavPoint(-96,-32),new NavPoint(-92,-32)};
                double radius=PlayableUnitRules.Radius(P,PlayableEntityKind.Tank),slot=Math.Max(P.ScienceFootprintRadius,Math.Max(P.FactoryFootprintRadius,P.RefineryFootprintRadius));
                for(int x=-120;x<=120&&points.Count<units;x+=8)for(int z=-120;z<=120&&points.Count<units;z+=8){var at=new NavPoint(x,z);if(points.Any(q=>AiCombatMicroSafety.Distance(q,at)<4)||authored.Sites.Any(site=>AiCombatMicroSafety.Distance(site.Position,at)<TerritoryRules.Radius(P,site.Kind)+radius+1||site.Slots.Any(q=>AiCombatMicroSafety.Distance(q.Position,at)<slot+radius+1))||starts.Any(start=>AiCombatMicroSafety.Distance(start.ExplorerAnchor,at)<P.ExplorerCollisionRadius+radius+1))continue;points.Add(at);}
                Assert.AreEqual(units,points.Count,"legal authored scenario capacity");var costs=new double[starts.Length,starts.Length];for(int i=0;i<starts.Length;i++)for(int j=0;j<starts.Length;j++)costs[i,j]=AiCombatMicroSafety.Distance(starts[i].Position,starts[j].Position);
                var home=authored.Sites.Single(site=>site.Id==starts[authored.Assignments[0]].SiteId);
                OfflineScenarioBuilding[] towers=null;
                if(repair){
                    var pad=home.DefenseSlots(P).OrderBy(slot=>AiCombatMicroSafety.Distance(slot.Position,points[0])).First();
                    towers=new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.RepairTower,home.Id,pad.Id,pad.Position,pad.Heading)};
                }
                if(useTowerApproach)
                {
                    var center=towers[0].Position;
                    var direction=points[0];
                    double f=(P.RepairTowerRange+8)/AiCombatMicroSafety.Distance(direction,center);
                    points[0]=new NavPoint(center.X+(direction.X-center.X)*f,center.Z+(direction.Z-center.Z)*f);
                }
                var scenario=points.Select(at=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,at)).ToArray();
                Config=new OfflineMatchConfiguration(P,"execution-owner-controls",map.Id,authored.RouteProvenance,7,authored.Roster.ToArray(),starts,authored.Sites.ToArray(),authored.Obstacles.ToArray(),costs,scenario:scenario,terrain:map.Id,scenarioBuildings:towers);
                D=new PlayableDomain(P,Gen,Config);Owner=new PlayableAiOwnerLoop(P,PlayableAiOpeningComposition.Initialize(7,"player-1",aiProfile:AiProfile.Initial),Gen,null,AiProfile.Initial,AiDifficulty.Veteran);
                var before=View();Members=before.Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank).OrderBy(u=>u.Id).Take(4).Select(u=>u.Id).ToArray();Patient=Members[0];var armies=(AiArmyRegistry)Field(Owner,"armies");Assert.True(armies.TryCreate(before,AiArmyRole.Attack,AiArmyPlanner.Policy,Members,out var army));
                var kind=returning?PlayableCommandKind.Follow:PlayableCommandKind.Move;var goal=new NavPoint(-88,-34);int target=returning?Members[1]:0;
                var repairTower=repair?before.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.RepairTower):null;
                int tower=repairTower?.Id??0;
                if(useTowerApproach)
                {
                    var center=repairTower.Position;
                    var start=before.Entities.Single(u=>u.Id==Patient).Position;
                    double fraction=0.8*P.RepairTowerRange/AiCombatMicroSafety.Distance(start,center);
                    goal=new NavPoint(center.X+(start.X-center.X)*fraction,center.Z+(start.Z-center.Z)*fraction);
                }
                var phase=repair?AiCombatMicroPhase.Repair:returning?AiCombatMicroPhase.Return:AiCombatMicroPhase.Withdraw;
                Assert.AreEqual(PlayableCommandStatus.Applied,D.Apply(new PlayableCommand(Gen,12,"player-1",kind,new[]{Patient},goal,targetId:target,origin:PlayableOrderOrigin.Ai,source:PlayableAiOpeningComposition.SourceIdentity,jobId:88,actionId:88),out var message),message);
                var o=View();var micro=(AiCombatMicroPolicy)Field(Owner,"combatMicro");var local=new PlayableAiAction(1,"player-1",P.ProfileId,P.Revision,Gen,o.SnapshotSequence,kind,new[]{Patient},goal,targetId:target,parentId:tower,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                micro.Commit(new AiCombatMicroPlan{Action=local,Army=army,Phase=phase},Gen,0);var actual=new PlayableAiAction(88,"player-1",P.ProfileId,P.Revision,Gen,o.SnapshotSequence,kind,new[]{Patient},goal,targetId:target,parentId:tower,seed:7,sourceIdentity:local.SourceIdentity);var receipt=new AiReceiptIdentity(Gen,"player-1",1,1);micro.Applied(actual,12,0,receipt,repair?before.Entities.Single(u=>u.Id==Patient).Location:null);micro.Receipt(new PlayableAiTraceRecord(o.Identity,1,0,12,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"fixture applied command",o.OwnerId,local.SourceIdentity,receipt,AiCombatMicroPolicy.Policy,kind));
                Owner.ReconcileBudget(D);Owner.BindExecutionDomain(D);
            }
            internal PlayableAiObservation View()=>PlayableAiObservation.From(D.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player));
            internal MotionExecutionProposal Proposal(bool changedRemaining=false)
            {
                long grant=1000000;Assert.True(Registry.TryDescribe(Patient,ref grant,out var descriptor));using(descriptor){var request=D.Navigation.PendingRequestFor(Patient);Assert.NotNull(request,"control uses real accepted pre-activation Native request");var m=request.MemberIdentity;var actor=D.CombatExecutionActor(Patient);var from=actor.Location.Value;var to=from;var endpoint=request.GoalLocation.Value;
                    var binding=new MotionExecutionBinding(Gen,m.GroupId,m.MembershipRevision,D.Navigation.GroupFor(Patient).OrderRevision,descriptor.Sequence,Patient,m.Incarnation,m.MobilityRevision,request.Request,request.Order,m.OrderRevision,"player-1","fixture-request-"+request.Request,request.ProfileId,"fixture-geometry-"+request.Geometry.Revision,string.IsNullOrEmpty(request.HeldIdentity)?null:request.HeldIdentity,descriptor.Identity);
                    var middle=new NavLocation(new NavPoint(endpoint.Position.X,endpoint.Position.Z+(changedRemaining?2:0)),endpoint.SurfaceId);
                    var legs=new[]{new NavTypedLeg(from,to,0,NavTypedLegKind.Surface,null),new NavTypedLeg(to,middle,AiCombatMicroSafety.Distance(to.Position,middle.Position),NavTypedLegKind.Surface,null),new NavTypedLeg(middle,endpoint,AiCombatMicroSafety.Distance(middle.Position,endpoint.Position),NavTypedLegKind.Surface,null)};
                    Assert.True(MotionExecutionRemainingBranch.TryCreate(binding,from,endpoint,0,0,legs,ref grant,_=>true,out var remaining));var trace=new MotionExecutionTracePiece(MotionExecutionTraceKind.Surface,MotionExecutionLaw.AffineSupportChord,from,to,"open-ground",0,0,0,0,1);
                    Assert.True(MotionExecutionPrimitive.TryCreate(binding,1,MotionExecutionKind.PureTurnWait,0,1d/30,from,to,endpoint,PlayableUnitRules.Radius(P,PlayableEntityKind.Tank),"tank",actor.HullHeading,actor.HullHeading,Patient,m.GroupId,request.Request,new[]{trace},ref grant,_=>true,out var primitive,remaining));Assert.True(MotionExecutionProposal.TryCreate(Gen,m.GroupId,m.MembershipRevision,1,0,new[]{primitive},ref grant,_=>true,out var proposal));return proposal;}
            }
            public void Dispose(){((AiCombatMicroExecutionOwner)Field(Owner,"executionOwner"))?.Dispose();D.StopObservedPaths();Assert.True(SpinWait.SpinUntil(()=>D.ObservedPathWorkerStopped,5000));D.Navigation.ReleaseCommittedHostRoot();D.DestroyOwnedNavigation();Assert.Zero(D.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(D.Navigation.PhysicalHostBudget.HeldBytes);}
        }
        sealed class NativeSource:IAiCombatMicroNativeExecutionSource
        {
            sealed class Record:IAiCombatMicroNativeCommittedRecord{internal readonly NativeInput Input;public IAiCombatMicroNativeExecutionSource NativeOwner{get;}public long OperationId=>Input.OperationId;public MotionExecutionProposal Actual=>Input.Proposal;internal Record(NativeSource owner,NativeInput input){NativeOwner=owner;Input=input;}}
            Record committed;
            internal void CommitFixtureOperation(NativeInput input){Assert.AreSame(this,input.NativeOwner);committed=new Record(this,input);}
            public bool TryRetainCommitted(long operation,out IAiCombatMicroNativeCommittedRecord record,out IDisposable retention){record=null;retention=null;if(committed==null||committed.OperationId!=operation)return false;record=committed;return committed.Input.TryRetain(out retention);}
        }
        sealed class NativeInput:IAiCombatMicroNativeInput
        {
            internal int Pins;readonly NavigationHostRequest request;public IAiCombatMicroNativeExecutionSource NativeOwner{get;}public long OperationId{get;}public string SourceIdentity=>"fixture-native-retention";public MotionExecutionProposal Proposal{get;}
            internal NativeInput(World w,MotionExecutionProposal p,NativeSource issuer,long operation=1){OperationId=operation;Proposal=p;NativeOwner=issuer;request=w.D.Navigation.PendingRequestFor(w.Patient).StorageOwner;Assert.NotNull(request);}
            public bool TryRetain(out IDisposable retention){request.Retain();Pins++;retention=new Pin(this);return true;}
            sealed class Pin:IDisposable{NativeInput owner;internal Pin(NativeInput owner){this.owner=owner;}public void Dispose(){if(owner==null)return;owner.request.Release();owner.Pins--;owner=null;}}
        }
        static AiCombatMicroExecutionOwner.Input Retain(AiCombatMicroExecutionOwner registry,AiCombatMicroExecutionOwner.Source source,NativeInput native){long grant=1000000;Assert.True(registry.TryRetainInput(source,native,ref grant,out var input));return input;}
        static void Finish(AiCombatMicroExecutionOwner registry,AiCombatMicroExecutionOwner.Submission s){int cap=10000;while(registry.Status(s)==AiCombatMicroExecutionStatus.Pending&&cap-->0){long current=1000000;registry.Advance(s,1000,ref current);}Assert.Greater(cap,0);Assert.AreEqual(AiCombatMicroExecutionStatus.Allowed,registry.Status(s));}
        static (NativeSource issuer,AiCombatMicroExecutionOwner.Source source,NativeInput native,AiCombatMicroExecutionPathWork.Cursor cursor) CommitHistory(World w)
        {
            long grant=1000000;var issuer=new NativeSource();var source=w.Registry.RegisterSource(issuer,"fixture-native-retention",ref grant);var native=new NativeInput(w,w.Proposal(),issuer);Assert.True(w.Registry.TrySubmit(Retain(w.Registry,source,native),ref grant,out var s));Finish(w.Registry,s);Assert.True(w.Registry.TryIssue(s,ref grant,out var issued));Assert.True(w.Registry.TryPrepareActual(issued,Retain(w.Registry,source,native),ref grant));while(w.Registry.Status(s)==AiCombatMicroExecutionStatus.Matching)w.Registry.Advance(s,1,ref grant);Assert.True(w.Registry.TryBeginExecution(issued,ref grant,out var ticket));issuer.CommitFixtureOperation(native);Assert.True(w.Registry.TryConsumeCommitted(source,ticket,ref grant,ref grant));Assert.Zero(native.Pins);var cursor=(AiCombatMicroExecutionPathWork.Cursor)((IDictionary)Field(w.Registry,"committed"))[w.Patient];Assert.NotNull(cursor);return(issuer,source,native,cursor);
        }
        [Test]public void AppliedOwnPendingMintsRealDescriptorWhileForeignPendingAndUnpaidBirthCannot()
        {
            using(var w=new World()){Assert.NotNull(w.View().Entities.Single(u=>u.Id==w.Patient).Queue?.Pending);long baseline=w.Registry.Retained,grant=0;Assert.False(w.Registry.TryDescribe(w.Patient,ref grant,out _));Assert.Zero(grant);Assert.AreEqual(baseline,w.Registry.Retained);
                grant=1000000;Assert.True(w.Registry.TryDescribe(w.Patient,ref grant,out var descriptor));using(descriptor)Assert.True(descriptor.Withdrawal);
                Assert.AreEqual(PlayableCommandStatus.Applied,w.D.Apply(new PlayableCommand(Gen,13,"player-1",PlayableCommandKind.Move,new[]{w.Patient},new NavPoint(-87,-28),origin:PlayableOrderOrigin.Ai,source:PlayableAiOpeningComposition.SourceIdentity,jobId:89,actionId:89),out var message),message);grant=1000000;Assert.False(w.Registry.TryDescribe(w.Patient,ref grant,out _));Assert.AreEqual(baseline,w.Registry.Retained);}
        }
        [Test,Timeout(30000)]
        public void DefaultGroundResponseMovesAfterAppliedMicroWithdrawalArrives()
        {
            using var w=new World(recast:true);
            var d=w.D;
            var nav=d.Navigation;
            nav.TickQuantaEnabled=true;
            nav.SuspendPendingMovement=true;
            d.NonblockingNavigationPreparation=true;
            d.ConfigureCombatExecutionOwners(()=>new[]{w.Owner});
            nav.RoutePort.PrepareRecastStartup(w.P,null);
            d.BindCombatMotionAuthority(()=>new[]{w.Owner});
            var binding=new PlayableRouteBinding(d.Geometry,w.P,nav.Admission,nav.RoutePort);
            using var host=new CertifiedRouteWorker(binding,8);
            bool staleReceiver=false;
            void Step()
            {
                d.BindCombatMotionAuthority(()=>new[]{w.Owner});
                nav.PrepareDeliveryBarrier();
                nav.PrepareTickQuantum(d.Tick+1,w.P.NavigationRequestsPerFrame,true);
                d.Step(1d/30);
                staleReceiver|=d.CombatMotionAuthority.LastRefusal?.Contains("Stale applied micro receiver.")==true;
                Assert.IsNull(host.Publication.Failure);
                Thread.Sleep(1);
            }
            try
            {
                var start=d.CombatExecutionActor(w.Patient).Position;
                long firstRequest=nav.PendingRequestFor(w.Patient).Request;
                Assert.True(SpinWait.SpinUntil(()=>nav.HasCertifiedHost||host.Publication.Failure!=null,5000));
                Assert.IsNull(host.Publication.Failure);
                void Until(Func<bool> predicate,int horizon,string failure)
                {
                    for(int i=0;i<horizon&&!predicate();i++)Step();
                    var actor=d.CombatExecutionActor(w.Patient);
                    Assert.True(predicate(),$"{failure} tick={d.Tick} pending={nav.PendingCount} pumps={host.Publication.Pumps} applied={nav.AppliedResults} outcome={actor.NavigationOutcome} runnable={nav.QuantumRunnable} host_attached={nav.HasCertifiedHost} world_refusal={d.CombatMotionAuthority.LastRefusal} route_refusal={host.Publication.ProjectionFailure}");
                }
                Until(()=>d.SpatialCompletion(w.Patient,PlayableOwner.Player)?.Order.Sequence==12,420,"The applied Micro Move must physically arrive through the certified worker.");
                var arrived=d.CombatExecutionActor(w.Patient);
                Assert.Greater(AiCombatMicroSafety.Distance(start,arrived.Position),2);
                Assert.True(w.Owner.ReservesExecutionSubject(w.Patient));
                Assert.IsNull(arrived.CurrentOrder);
                Assert.AreEqual(12,arrived.OrderStamp.Sequence);

                var hit=typeof(PlayableDomain).GetMethod("ObserveCombatHit",F);
                hit.Invoke(d,new object[]{w.Patient,PlayableOwner.Player,arrived.Position,0,PlayableOwner.Enemy,new NavPoint(0,-1)});
                bool issued=false,translated=false;
                for(int tick=0;tick<90&&!translated;tick++){
                    Step();
                    var actor=d.CombatExecutionActor(w.Patient);
                    long request=nav.InstalledExecutionFor(w.Patient)?.Corridor.Request??0;
                    issued|=(nav.PendingRequestFor(w.Patient)?.Request??0)>firstRequest||request>firstRequest;
                    if(request>firstRequest&&actor.Position.Z<arrived.Position.Z-.4)translated=true;
                }
                TestContext.WriteLine($"MICRO_DEFAULT_RESPONSE issued={issued} translated={translated} stale_receiver={staleReceiver} pumps={host.Publication.Pumps}");
                Assert.True(issued,"A normal hidden-hit response must issue its own Native move.");
                Assert.False(staleReceiver,"The completed withdrawal must not claim the ordinary response's movement.");
                Assert.True(translated,"The hit response must physically move away from the incoming direction.");
            }
            finally
            {
                host.Dispose();
                Assert.True(SpinWait.SpinUntil(()=>host.IsStopped,5000));
                d.StopCombatMotionAuthority();
            }
        }
        [TestCase(false,TestName="GroundRepairWithSafeActualRouteArrives")]
        [TestCase(true,TestName="GroundRepairWithDeniedActualRouteReleasesCustody")]
        [Timeout(30000)]
        public void GroundRepairRecoversFromPaidPathVerdict(bool denied)
        {
            using var w=new World(recast:true,repair:true,useTowerApproach:true);
            var d=w.D;
            var n=d.Navigation;
            n.TickQuantaEnabled=true;
            n.SuspendPendingMovement=true;
            d.ConfigureCombatExecutionOwners(()=>new[]{w.Owner});
            n.RoutePort.PrepareRecastStartup(w.P,null);
            d.BindCombatMotionAuthority(()=>new[]{w.Owner});
            var start=d.CombatExecutionActor(w.Patient);
            if(denied)
            {
                var goal=n.PendingRequestFor(w.Patient).GoalLocation.Value.Position;
                var enemyView=d.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Enemy);
                int id=enemyView.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Headquarters&&b.Owner==PlayableOwner.Enemy).Id;
                var kind=PlayableBuildingKind.SentryTurret;
                double range=TerritoryRules.AiDefenseRange(w.P,kind);
                double length=AiCombatMicroSafety.Distance(start.Position,goal);
                double dx=(goal.X-start.Position.X)/length,dz=(goal.Z-start.Position.Z)/length;
                var at=new NavPoint(goal.X+range*(0.45*dx+0.53*dz),goal.Z+range*(0.45*dz-0.53*dx));
                var tower=new PlayableBuildingSnapshot(id,PlayableOwner.Enemy,kind,at,100,1,0,0,default);
                var vision=new PlayableVision(0,144,144,1,1);
                vision.Refresh(new[]{new VisionSource(default,300)},Array.Empty<KnownBuilding>());
                var observation=PlayableAiObservation.From(new PlayableSnapshot(w.P.ProfileId,w.P.Revision,Gen,7,0,0,RuntimeStatus.Running,false,
                    PlayableMatchOutcome.Playing,0,null,new[]{start},new[]{tower},Array.Empty<PlayableProjectileSnapshot>(),
                    new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),ownerId:"player-1",participants:w.Config.Roster.ToArray()));
                Assert.True(AiCombatMicroSafety.Covers(observation,w.P,tower,start,goal));
                ((AiKnowledgeTracker)Field(w.Owner,"knowledge")).Observe(observation,AiProfile.Initial);
            }
            var binding=new PlayableRouteBinding(d.Geometry,w.P,n.Admission,n.RoutePort);
            using var host=new CertifiedRouteWorker(binding,8);
            bool? rejectedRemainingBranch=null;
            w.Registry.PaidPathDenialForQa=(id,rejectedBranch)=>{if(id==w.Patient)rejectedRemainingBranch=rejectedBranch;};
            try
            {
                Assert.True(SpinWait.SpinUntil(()=>n.HasCertifiedHost||host.Publication.Failure!=null,5000));
                for(int i=0;i<630&&!rejectedRemainingBranch.HasValue;i++)
                {
                    d.BindCombatMotionAuthority(()=>new[]{w.Owner});
                    n.PrepareDeliveryBarrier();
                    n.PrepareTickQuantum(d.Tick+1,w.P.NavigationRequestsPerFrame,true);
                    d.Step(1d/30);
                    Assert.IsNull(host.Publication.Failure);
                    if(d.SpatialCompletion(w.Patient,PlayableOwner.Player)!=null)break;
                    Thread.Sleep(2);
                }
                var result=d.CombatExecutionActor(w.Patient);
                TestContext.WriteLine($"REPAIR_PATH denied={denied} tick={d.Tick} rejected_after_preparation={rejectedRemainingBranch} pending={n.IsPending(w.Patient)} reserved={w.Owner.ReservesExecutionSubject(w.Patient)} outcome={result.NavigationOutcome}");
                if(!denied)
                {
                    Assert.IsNull(rejectedRemainingBranch);
                    Assert.AreEqual(12,d.SpatialCompletion(w.Patient,PlayableOwner.Player)?.Order.Sequence);
                    return;
                }
                Assert.AreEqual(true,rejectedRemainingBranch,"The real Native route's remaining branch must fail the paid threat check.");
                Assert.AreEqual(start.Position,result.Position);
                Assert.IsNull(d.SpatialCompletion(w.Patient,PlayableOwner.Player));
                Assert.IsFalse(w.Owner.ReservesExecutionSubject(w.Patient),"A denied repair path must release the exact applied job after publication rolls back.");
                Assert.IsFalse(n.IsPending(w.Patient));
                Assert.IsNull(result.CurrentOrder);
                Assert.AreEqual(NavigationOutcome.Stopped,result.NavigationOutcome);
            }
            finally
            {
                host.Dispose();
                Assert.True(SpinWait.SpinUntil(()=>host.IsStopped,5000));
                d.StopCombatMotionAuthority();
            }
        }

        [TestCase("busy")][TestCase("cold")][TestCase("capacity")][TestCase("replacement")][TestCase("available")]
        public void BusyRepairTowerCancelsTheExactGroundMoveBeforePublication(string condition)
        {
            using var w=new World(recast:true,repair:true);
            var authority=new PlayableAuthorityTick(w.D,new AiAuthorityScheduler(new[]{w.Owner}),7,w.Config);
            PlayableAuthorityTick restored=null;
            try
            {
                if(condition=="cold"){
                    var bytes=authority.CaptureBytes();
                    restored=PlayableAuthorityTick.RestoreBytes(bytes,w.Config);
                    CollectionAssert.AreEqual(bytes,restored.CaptureBytes());
                    authority=restored;
                }
                ExerciseRepairCancellation(authority,w.Patient,w.Members[1],condition);
            }
            finally
            {
                if(restored!=null){
                    restored.Stop();
                    restored.Domain.Navigation.ReleaseCommittedHostRoot();
                    restored.Domain.DestroyOwnedNavigation();
                }
            }
        }
        private static void ExerciseRepairCancellation(PlayableAuthorityTick authority,int patient,int recipient,string condition)
        {
            var domain=authority.Domain;
            var owner=authority.Scheduler.Owners.Single();
            var nav=domain.Navigation;
            var view=authority.ParticipantView("player-1");
            var tower=view.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.RepairTower);
            Assert.True(domain.CombatExecutionRepairReady(tower.Id,PlayableOwner.Player,patient));
            var old=domain.CombatExecutionActor(patient);
            Assert.AreEqual(12,old.Queue.Pending.CommandSequence);
            Assert.True(owner.ReservesExecutionSubject(patient));
            if(condition=="replacement")
                Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(Gen,13,"player-1",PlayableCommandKind.Move,new[]{patient},new NavPoint(-87,-28)).AsHuman(),out _));

            var buildings=(IDictionary)Field(domain,"buildings");
            var building=buildings[tower.Id];
            var healing=building.GetType().GetField("Healing",F);
            var beam=Activator.CreateInstance(healing.FieldType,true);
            void OccupyTower()
            {
                beam.GetType().GetField("Target",F).SetValue(beam,recipient);
                healing.SetValue(building,beam);
                Assert.False(domain.CombatExecutionRepairReady(tower.Id,PlayableOwner.Player,patient));
            }
            if(condition!="available")OccupyTower();
            if(condition=="capacity"){
                nav.PhysicalHostBudget.AllocationAdmission=_=>false;
                try{domain.Step(1d/30);}
                finally{nav.PhysicalHostBudget.AllocationAdmission=null;}
                Assert.True(nav.IsPending(patient));
                Assert.True(owner.ReservesExecutionSubject(patient));
                OccupyTower();
            }

            domain.Step(1d/30);
            var next=domain.CombatExecutionActor(patient);
            TestContext.WriteLine($"BUSY_REPAIR condition={condition} cancelled={next.Queue?.Pending==null} pending={nav.IsPending(patient)} outcome={next.NavigationOutcome} reserved={owner.ReservesExecutionSubject(patient)}");
            if(condition is ("available" or "replacement")){
                Assert.True(nav.IsPending(patient));
                Assert.AreEqual(condition=="available"?12:13,next.Queue.Pending.CommandSequence);
                return;
            }
            Assert.False(nav.IsPending(patient),"The old Move cannot keep receiving the same repair-tower refusal.");
            Assert.IsNull(next.CurrentOrder);
            Assert.AreEqual(NavigationOutcome.Stopped,next.NavigationOutcome);
            Assert.False(owner.ReservesExecutionSubject(patient));
            Assert.AreEqual(old.Position,next.Position);
            Assert.IsNull(domain.SpatialCompletion(patient,PlayableOwner.Player));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void GroundRepairMoveThatCannotFinishWithinItsPhaseStopsBeforePublication(bool canFinish)
        {
            using var w=new World(recast:true,repair:true);
            var authority=new PlayableAuthorityTick(w.D,new AiAuthorityScheduler(new[]{w.Owner}),7,w.Config);
            var units=(IDictionary)Field(w.D,"units");
            var unit=units[w.Patient];
            unit.GetType().GetField("Health",F).SetValue(unit,40d);
            var old=w.D.CombatExecutionActor(w.Patient);
            var target=w.D.Navigation.PendingRequestFor(w.Patient).Goal;
            var profile=AiProfile.Initial;
            long limit=AiProfile.SecondsToTicks(profile.DifficultyValue(AiDifficulty.Veteran,"micro.maxPhaseSeconds"),30);
            double length=AiCombatMicroSafety.Distance(old.Position,target);
            double estimate=AiCombatMicroPolicy.RepairSeconds(w.P,profile,AiDifficulty.Veteran,old,length);
            long travel=(long)Math.Ceiling(estimate*30);
            long tick=limit-travel+(canFinish?-2:2);
            Assert.Greater(tick,0);
            Assert.Less(tick+1,limit,"The phase is live, but repair can exceed the remaining time.");
            typeof(PlayableDomain).GetProperty("Tick",F).SetValue(w.D,tick);
            w.D.EnableRepairCancellationJournal();

            w.D.Step(1d/30);

            var next=w.D.CombatExecutionActor(w.Patient);
            Assert.AreEqual(canFinish,w.D.Navigation.IsPending(w.Patient),"Only a repair that can still finish keeps the route.");
            Assert.AreEqual(canFinish,w.Owner.ReservesExecutionSubject(w.Patient));
            Assert.AreEqual(old.Position,next.Position);
            Assert.IsNull(w.D.SpatialCompletion(w.Patient,PlayableOwner.Player));
            if(canFinish)
            {
                Assert.IsEmpty(w.D.DrainRepairCancellations());
                return;
            }
            Assert.AreEqual(NavigationOutcome.Stopped,next.NavigationOutcome);
            var cancelled=w.D.DrainRepairCancellations().Single();
            Assert.AreEqual("travel-and-healing-exceed-phase",cancelled.Reason);
            Assert.AreEqual(88,cancelled.ActionId);
            Assert.AreEqual(0d,cancelled.Displacement);
            Assert.True(cancelled.RetryBlocked);
            Assert.IsEmpty(w.D.DrainRepairCancellations());

            var micro=(AiCombatMicroPolicy)Field(w.Owner,"combatMicro");
            var observation=w.View();
            var state=WorldWire.Pack(micro.Write);
            var restored=WorldWire.Unpack(state,reader=>AiCombatMicroPolicy.Read(reader,w.D.Tick,Gen));
            CollectionAssert.AreEqual(state,WorldWire.Pack(restored.Write));
            Assert.True(restored.BlocksRepairRetry(observation,w.P,next,cancelled.TowerId));
            Assert.False(restored.BlocksRepairRetry(observation,w.P,next,cancelled.TowerId+1));
            var moved=new PlayableEntitySnapshot(next.Id,next.Owner,next.Kind,
                new NavPoint(next.Position.X+2*w.P.Navigation.ArrivalTolerance,next.Position.Z),next.Health,false,0,0,0,
                location:next.Location);
            Assert.False(restored.BlocksRepairRetry(observation,w.P,moved,cancelled.TowerId));
        }
        [Test, Explicit("Reconstructs the original Null Dock seed 19 AB repair order."), Timeout(30000)]
        public void NullDock19ABStationaryVeprDoesNotReissueRepair()
        {
            const string ownerId="foundry-2";
            const long generation=3,sequence=12,actionId=88;
            var manifestPath=Environment.GetEnvironmentVariable("SPACEWARS_REPAIR_REPLAY_MANIFEST");
            var mapPath=Environment.GetEnvironmentVariable("SPACEWARS_REPAIR_REPLAY_MAP");
            Assert.That(manifestPath,Is.Not.Empty);
            Assert.That(mapPath,Is.Not.Empty);
            var manifest=JObject.Parse(File.ReadAllText(manifestPath));
            Assert.AreEqual(19,(int)manifest["Seed"]);
            Assert.AreEqual(generation,(long)manifest["Generation"]);
            var map=new NullDockMap(JsonConvert.DeserializeObject<NullDockProfileData>(File.ReadAllText(mapPath)));
            Assert.AreEqual((string)manifest["Map"],map.Id);
            var profile=PlayableProfile.Create(manifest["Profile"].ToObject<PlayableProfileData>(),map);
            var ai=new AiProfile(manifest["Ai"]["Data"].ToObject<AiProfileData>());
            Assert.AreEqual((string)manifest["Ai"]["Hash"],ai.Hash);
            var lobby=new NativeLobbyConfiguration{NullDock=true,HasExplicitSeed=true,ExplicitSeed=19,
                Participants=new System.Collections.Generic.List<NativeLobbyParticipant>{
                    new NativeLobbyParticipant{Name="ИИ",Team=1,Color=0,Difficulty=AiDifficulty.Veteran},
                    new NativeLobbyParticipant{Name="ИИ",Team=2,Color=1,Difficulty=AiDifficulty.Expert}}};
            var match=NativeLobbyMatch.Create(profile,lobby);
            Assert.AreEqual(1,match.Starts[match.Assignments[0]].SiteId);
            Assert.AreEqual(2,match.Starts[match.Assignments[1]].SiteId);
            var pad=match.Sites.Single(site=>site.Id==2).DefenseSlots(profile).Single(slot=>slot.Id==1004);
            Assert.AreEqual(new NavPoint(68.62664445218705,8.083980388508804),pad.Position);
            var origin=new NavPoint(12.92182828863582,-4.412416403814545);
            var goal=new NavPoint(63.03843791011044,10.127126894046807);
            var scenario=new OfflineMatchConfiguration(profile,match.SourceIdentity,match.MapIdentity,match.RouteProvenance,
                match.Seed,match.Roster.ToArray(),match.Starts.ToArray(),match.Sites.ToArray(),match.Obstacles.ToArray(),
                new double[2,2],scenario:new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Tank,origin,0.3625380222261859)},
                terrain:map.Id,scenarioBuildings:new[]{new OfflineScenarioBuilding(2,PlayableBuildingKind.RepairTower,2,pad.Id,pad.Position,pad.Heading)});
            var domain=new PlayableDomain(profile,generation,scenario);
            var loop=new PlayableAiOwnerLoop(profile,PlayableAiOpeningComposition.Initialize(19,ownerId,
                aiProfile:ai,forcedOpening:PlayableAiOpening.Safe,difficulty:AiDifficulty.Expert),generation,null,ai,AiDifficulty.Expert);
            var authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(new[]{loop}),19,scenario);
            try
            {
                var faction=domain.OwnerFor(ownerId);
                var initial=authority.ParticipantView(ownerId);
                var tank=initial.Entities.Single(e=>e.Owner==faction&&e.Kind==PlayableEntityKind.Tank);
                var tower=initial.Buildings.Single(b=>b.Owner==faction&&b.Kind==PlayableBuildingKind.RepairTower);
                var unit=((IDictionary)Field(domain,"units"))[tank.Id];
                unit.GetType().GetField("Health",F).SetValue(unit,28d);
                var micro=(AiCombatMicroPolicy)Field(loop,"combatMicro");
                var armies=(AiArmyRegistry)Field(loop,"armies");
                var action=new PlayableAiAction(1,ownerId,profile.ProfileId,profile.Revision,generation,initial.Sequence,
                    PlayableCommandKind.Move,new[]{tank.Id},goal,parentId:tower.Id,seed:19,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(generation,sequence,ownerId,
                    action.Kind,new[]{tank.Id},goal,parentId:tower.Id,origin:PlayableOrderOrigin.Ai,source:action.SourceIdentity,
                    jobId:actionId,actionId:actionId),out var message),message);
                micro.Commit(new AiCombatMicroPlan{Action=action,Phase=AiCombatMicroPhase.Repair},generation,0);
                var applied=new PlayableAiAction(actionId,ownerId,profile.ProfileId,profile.Revision,generation,initial.Sequence,
                    action.Kind,new[]{tank.Id},goal,parentId:tower.Id,seed:19,sourceIdentity:action.SourceIdentity);
                var receipt=new AiReceiptIdentity(generation,ownerId,1,1);
                micro.Applied(applied,sequence,0,receipt,tank.Location);
                micro.Receipt(new PlayableAiTraceRecord(PlayableAiObservation.From(initial).Identity,1,0,sequence,0,
                    PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"Order accepted.",ownerId,action.SourceIdentity,
                    receipt,AiCombatMicroPolicy.Policy,action.Kind));

                // Isolate retirement before route publication. Advance only the already
                // accepted order's clock until its straight-line repair budget is exhausted.
                typeof(PlayableDomain).GetProperty("Tick",F).SetValue(domain,60L);
                domain.EnableRepairCancellationJournal();
                domain.Step(1d/30);
                var cancelled=domain.DrainRepairCancellations().Single();
                Assert.AreEqual(actionId,cancelled.ActionId);
                Assert.AreEqual(tank.Id,cancelled.UnitId);
                Assert.AreEqual(tower.Id,cancelled.TowerId);
                Assert.AreEqual("travel-and-healing-exceed-phase",cancelled.Reason);
                Assert.AreEqual(0d,cancelled.Displacement);
                Assert.True(cancelled.RetryBlocked);
                Assert.IsNull(domain.CombatExecutionActor(tank.Id).CurrentOrder);
                Assert.IsFalse(domain.Navigation.IsPending(tank.Id));

                // An unchanged decision should pass the cooldown without planning another Move.
                for(int i=0;i<150;i++)domain.Step(1d/30);
                var observed=PlayableAiObservation.From(authority.ParticipantView(ownerId),AiDifficulty.Expert);
                var patient=observed.Entities.Single(u=>u.Id==tank.Id&&u.Owner==faction);
                micro.Observe(observed,profile,ai,AiDifficulty.Expert,armies);
                Assert.True(micro.BlocksRepairRetry(observed,profile,patient,tower.Id));
                Assert.IsNull(micro.Propose(observed,profile,ai,AiDifficulty.Expert,armies));
                Assert.IsEmpty(domain.DrainRepairCancellations());

                var building=((IDictionary)Field(domain,"buildings"))[tower.Id];
                var phase=building.GetType().GetField("Phase",F);
                phase.SetValue(building,ConstructionPhase.Constructing);
                var unavailable=PlayableAiObservation.From(authority.ParticipantView(ownerId),AiDifficulty.Expert);
                Assert.AreEqual(ConstructionPhase.Constructing,unavailable.Buildings.Single(b=>b.Id==tower.Id).Phase);
                micro.Observe(unavailable,profile,ai,AiDifficulty.Expert,armies);
                phase.SetValue(building,ConstructionPhase.Ready);
                var reopened=PlayableAiObservation.From(authority.ParticipantView(ownerId),AiDifficulty.Expert);
                Assert.False(micro.BlocksRepairRetry(reopened,profile,patient,tower.Id));
                TestContext.WriteLine($"Null Dock 19 AB: {cancelled.Reason}, displacement {cancelled.Displacement}, unchanged retries 0.");
            }
            finally
            {
                authority.Stop();
                domain.Navigation.ReleaseCommittedHostRoot();
                domain.DestroyOwnedNavigation();
            }
        }

        [Test]public void PaidHighUnitSmallCoreGuardRefusesUnderfundedActualNestedScans()
        {
            using(var w=new World(100,true)){long baseline=w.Registry.Retained;long grant=100*8+4*(8+4)+128+64;Assert.False(w.Registry.TryDescribe(w.Patient,ref grant,out _),"old U*8 plus M^2 charge cannot fund actual U*(M+R) scans");Assert.AreEqual(baseline,w.Registry.Retained);grant=1000000;Assert.True(w.Registry.TryDescribe(w.Patient,ref grant,out var descriptor));descriptor.Dispose();Assert.AreEqual(baseline,w.Registry.Retained);}
        }
        [Test]public void PreparedPublicationReservesBeforeWriteAndInstallsWithoutAllocationOrExternalRelease()
        {
            using(var w=new World()){
                var registry=w.Registry;long grant=1000000;var issuer=new NativeSource();var source=registry.RegisterSource(issuer,"fixture-native-retention",ref grant);var native=new NativeInput(w,w.Proposal(),issuer);
                Assert.True(registry.TrySubmit(Retain(registry,source,native),ref grant,out var submission));Finish(registry,submission);Assert.True(registry.TryIssue(submission,ref grant,out var issued));Assert.True(registry.TryPrepareActual(issued,Retain(registry,source,native),ref grant));while(registry.Status(submission)==AiCombatMicroExecutionStatus.Matching)registry.Advance(submission,1,ref grant);
                byte[] before=WorldWire.Pack(registry.Write);long liability=registry.Retained,zero=0;Assert.IsNull(registry.PrepareInstall(issued,ref zero));CollectionAssert.AreEqual(before,WorldWire.Pack(registry.Write));Assert.AreEqual(liability,registry.Retained);
                var install=registry.PrepareInstall(issued,ref grant);Assert.NotNull(install);install.Validate(ref grant);int pins=native.Pins;
                long allocation=GC.GetAllocatedBytesForCurrentThread();install.EnterNoThrow();install.ApplyNoThrow();long used=GC.GetAllocatedBytesForCurrentThread()-allocation;
                Assert.Zero(used,"Factual cursor installation must not allocate after the Native write.");Assert.AreEqual(pins,native.Pins,"External Native pins remain owned until the postpublication release phase.");Assert.AreEqual(AiCombatMicroExecutionStatus.Committed,registry.Status(submission));Assert.NotNull(((IDictionary)Field(registry,"committed"))[w.Patient]);
                install.Dispose();Assert.Zero(native.Pins);Assert.False(registry.TryIssue(submission,ref grant,out _));registry.Unregister(source);
            }
        }
        [Test]public void PreparedPublicationRechecksHumanReplacementBeforeAnyFactualCursorInstall()
        {
            using(var w=new World()){
                var registry=w.Registry;long grant=1000000;var issuer=new NativeSource();var source=registry.RegisterSource(issuer,"fixture-native-retention",ref grant);var native=new NativeInput(w,w.Proposal(),issuer);
                Assert.True(registry.TrySubmit(Retain(registry,source,native),ref grant,out var submission));Finish(registry,submission);Assert.True(registry.TryIssue(submission,ref grant,out var issued));Assert.True(registry.TryPrepareActual(issued,Retain(registry,source,native),ref grant));while(registry.Status(submission)==AiCombatMicroExecutionStatus.Matching)registry.Advance(submission,1,ref grant);
                var install=registry.PrepareInstall(issued,ref grant);Assert.NotNull(install);
                Assert.AreEqual(PlayableCommandStatus.Applied,w.D.Apply(new PlayableCommand(Gen,500,"player-1",PlayableCommandKind.Hold,new[]{w.Patient},origin:PlayableOrderOrigin.Human),out _));
                Assert.Throws<InvalidOperationException>(()=>install.Validate(ref grant));Assert.Zero(((IDictionary)Field(registry,"committed")).Count);Assert.AreEqual(AiCombatMicroExecutionStatus.Ready,registry.Status(submission));
                install.Dispose();registry.Cancel(submission);Assert.Zero(native.Pins);registry.Unregister(source);
            }
        }
        [Test]public void RegistryIssuesOnceMatchesWholeActualInputConsumesOnlyItsOwnTicketAndReleasesNativePins()
        {
            using(var w=new World()){var registry=w.Registry;long grant=1000000;var issuer=new NativeSource();var source=registry.RegisterSource(issuer,"fixture-native-retention",ref grant);var native=new NativeInput(w,w.Proposal(),issuer);var input=Retain(registry,source,native);Assert.AreEqual(1,native.Pins);Assert.True(registry.TrySubmit(input,ref grant,out var s));Assert.False(registry.TryIssue(s,ref grant,out _));Finish(registry,s);Assert.True(registry.TryIssue(s,ref grant,out var issued));Assert.True(registry.TryIssue(s,ref grant,out var repeated));Assert.AreSame(issued,repeated);
                var actual=Retain(registry,source,native);Assert.True(registry.TryPrepareActual(issued,actual,ref grant));while(registry.Status(s)==AiCombatMicroExecutionStatus.Matching)registry.Advance(s,1,ref grant);Assert.True(registry.TryBeginExecution(issued,ref grant,out var ticket));Assert.False(registry.TryBeginExecution(issued,ref grant,out _));var forged=new AiCombatMicroExecutionOwner.Ticket(registry,s);Assert.False(registry.TryConsumeCommitted(source,forged,ref grant,ref grant));Assert.False(registry.TryConsumeCommitted(source,ticket,ref grant,ref grant),"endpoint and ticket without a Native committed record are insufficient");issuer.CommitFixtureOperation(native);long refused=0;Assert.False(registry.TryConsumeCommitted(source,ticket,ref refused,ref refused));Assert.AreEqual(AiCombatMicroExecutionStatus.Executing,registry.Status(s));Assert.True(registry.TryConsumeCommitted(source,ticket,ref grant,ref grant),"registered Native fixture record authenticates this exact zero-motion operation; not production Nav acceptance");Assert.False(registry.TryConsumeCommitted(source,ticket,ref grant,ref grant));Assert.AreEqual(AiCombatMicroExecutionStatus.Committed,registry.Status(s));Assert.Zero(native.Pins);Assert.False(registry.TryRetainInput(source,native,ref grant,out _),"same Native operation cannot obtain a second permit after commit");registry.Unregister(source);}
        }
        [Test]public void ChangedRemainingRouteCannotConsumeAndCancellationRetainsNoNativeInput()
        {
            using(var w=new World()){var r=w.Registry;long grant=1000000;var issuer=new NativeSource();var source=r.RegisterSource(issuer,"fixture-native-retention",ref grant);var native=new NativeInput(w,w.Proposal(),issuer);var input=Retain(r,source,native);Assert.True(r.TrySubmit(input,ref grant,out var s));Finish(r,s);Assert.True(r.TryIssue(s,ref grant,out var issued));var changed=new NativeInput(w,w.Proposal(true),issuer);var actual=Retain(r,source,changed);Assert.True(r.TryPrepareActual(issued,actual,ref grant));while(r.Status(s)==AiCombatMicroExecutionStatus.Matching)r.Advance(s,1,ref grant);Assert.AreEqual(AiCombatMicroExecutionStatus.Denied,r.Status(s));Assert.False(r.TryBeginExecution(issued,ref grant,out _));Assert.Zero(native.Pins);Assert.Zero(changed.Pins);r.Unregister(source);}
        }
        [TestCase(false)][TestCase(true)]public void PendingAndAllowedOwnerColdBytesAreExactUntilFreshNativeSourceRebind(bool allowed)
        {
            using(var w=new World()){var r=w.Registry;long grant=1000000;var issuer=new NativeSource();var source=r.RegisterSource(issuer,"fixture-native-retention",ref grant);var native=new NativeInput(w,w.Proposal(),issuer);var input=Retain(r,source,native);Assert.True(r.TrySubmit(input,ref grant,out var s));r.Advance(s,3,ref grant);if(allowed){Finish(r,s);Assert.True(r.TryIssue(s,ref grant,out _));}
                byte[] bytes=WorldWire.Pack(w.Owner.WriteState);var clone=WorldWire.Unpack(bytes,reader=>PlayableAiOwnerLoop.ReadState(reader,w.P,AiProfile.Initial,Gen,7,0));CollectionAssert.AreEqual(bytes,WorldWire.Pack(clone.WriteState));clone.BindExecutionDomain(w.D);var cold=clone.ExecutionOwner;try{Assert.True(cold.AwaitingNativeSources);long refusedGrant=1000000;Assert.False(cold.TryDescribe(w.Patient,ref refusedGrant,out _));Assert.AreEqual(1000000,refusedGrant);var old=cold.AwaitingSource(source.Identity,s.Id);Assert.NotNull(old);Assert.False(cold.TryIssue(old,ref grant,out _));var coldSource=cold.RegisterSource(issuer,source.Identity,ref grant);var coldInput=Retain(cold,coldSource,native);Assert.False(cold.TrySubmit(coldInput,ref grant,out _),"dormant image cannot supply new operational admission credit");Assert.True(cold.TryRebind(old,coldInput,null,ref grant));Assert.False(cold.AwaitingNativeSources);Assert.AreEqual(r.Retained,cold.Retained,"accepted hot/cold state rebuilds the same live liability before new admission");CollectionAssert.AreEqual(bytes,WorldWire.Pack(clone.WriteState));if(!allowed)Finish(cold,old);Assert.True(cold.TryIssue(old,ref grant,out _));cold.Unregister(coldSource);}finally{cold.Dispose();}r.Cancel(s);Assert.Zero(native.Pins);r.Unregister(source);}
        }
        [Test]public void AcceptedPendingRebindUsesSavedDescriptorAfterHumanReplacementThenDeniesFutureMotion()
        {
            using(var w=new World()){
                long grant=1000000;var issuer=new NativeSource();var source=w.Registry.RegisterSource(issuer,"fixture-native-retention",ref grant);var native=new NativeInput(w,w.Proposal(),issuer);Assert.True(w.Registry.TrySubmit(Retain(w.Registry,source,native),ref grant,out var pending));w.Registry.Advance(pending,3,ref grant);
                Assert.AreEqual(PlayableCommandStatus.Applied,w.D.Apply(new PlayableCommand(Gen,501,"player-1",PlayableCommandKind.Hold,new[]{w.Patient},origin:PlayableOrderOrigin.Human),out _));
                byte[] saved=WorldWire.Pack(w.Owner.WriteState);var clone=WorldWire.Unpack(saved,r=>PlayableAiOwnerLoop.ReadState(r,w.P,AiProfile.Initial,Gen,7,0));clone.BindExecutionDomain(w.D);
                try{
                    var cold=clone.ExecutionOwner;Assert.True(cold.AwaitingNativeSources);CollectionAssert.AreEqual(saved,WorldWire.Pack(clone.WriteState));var accepted=cold.AwaitingSource(source.Identity,pending.Id);var registered=cold.RegisterSource(issuer,source.Identity,ref grant);var input=Retain(cold,registered,native);
                    Assert.True(cold.TryRebind(accepted,input,null,ref grant),"Accepted work restores even though current human custody denies a new descriptor.");Assert.False(cold.AwaitingNativeSources);CollectionAssert.AreEqual(saved,WorldWire.Pack(clone.WriteState),"Rebinding restores the exact immutable accepted work.");
                    Assert.Zero(cold.Advance(accepted,1,ref grant));Assert.AreEqual(AiCombatMicroExecutionStatus.Cancelled,cold.Status(accepted));Assert.Zero(((IDictionary)Field(cold,"committed")).Count);cold.Unregister(registered);
                }finally{clone.ExecutionOwner.Dispose();}
                w.Registry.Cancel(pending);w.Registry.Unregister(source);Assert.Zero(native.Pins);
            }
        }
        [Test]public void AsyncAdoptionTransfersLiveRegistryAndRevokesOriginalWithoutACloneIssuer()
        {
            using(var w=new World()){long grant=1000000;var issuer=new NativeSource();var source=w.Registry.RegisterSource(issuer,"fixture-native-retention",ref grant);var native=new NativeInput(w,w.Proposal(),issuer);Assert.True(w.Registry.TrySubmit(Retain(w.Registry,source,native),ref grant,out var s));var live=w.Registry;var candidate=w.Owner.CreateReviewClone(0);Assert.Throws<InvalidOperationException>(()=>{var ignored=candidate.ExecutionOwner;});candidate.AdoptExecutionAuthority(w.Owner,w.D);Assert.AreSame(live,candidate.ExecutionOwner);Assert.Throws<InvalidOperationException>(()=>w.Owner.BindExecutionDomain(w.D));Finish(candidate.ExecutionOwner,s);Assert.True(candidate.ExecutionOwner.TryIssue(s,ref grant,out _));candidate.ExecutionOwner.Cancel(s);candidate.ExecutionOwner.Unregister(source);Assert.Zero(native.Pins);candidate.ExecutionOwner.Dispose();}
        }
        [TestCase(false)][TestCase(true)]public void CurrentAndDistinctMaintenanceFullWorldPreservePendingExecutionBytes(bool maintenance)
        {
            using(var w=new World()){long grant=1000000;var issuer=new NativeSource();var source=w.Registry.RegisterSource(issuer,"fixture-native-retention",ref grant);var native=new NativeInput(w,w.Proposal(),issuer);Assert.True(w.Registry.TrySubmit(Retain(w.Registry,source,native),ref grant,out var submission));w.Registry.Advance(submission,3,ref grant);
                var scheduler=new AiAuthorityScheduler(new[]{w.Owner});var authority=new PlayableAuthorityTick(w.D,scheduler,7,w.Config);
                // Exercise the two current wire grammars at the phase boundary;
                // this is not a claim of Native maintenance/movement activation.
                if(maintenance)typeof(PlayableAuthorityTick).GetField("barrierPhase",F).SetValue(authority,PlayableAuthorityTick.RouteBarrierPhase.MaintenancePending);
                byte[] bytes=authority.CaptureBytes();Assert.AreEqual(maintenance?15:19,BitConverter.ToInt32(PlayableWorldState.Decode(bytes).AiAuthority,4));
                var cold=PlayableAuthorityTick.RestoreBytes(bytes,w.Config);try{CollectionAssert.AreEqual(bytes,cold.CaptureBytes());Assert.NotNull(cold.Scheduler.Owners.Single().ExecutionOwner.AwaitingSource(source.Identity,submission.Id));Assert.AreEqual(maintenance,cold.MaintenancePendingFor(cold.Domain.Navigation));}finally{cold.Scheduler.Owners.Single().ExecutionOwner.Dispose();cold.Domain.StopObservedPaths();Assert.True(SpinWait.SpinUntil(()=>cold.Domain.ObservedPathWorkerStopped,5000));cold.Domain.Navigation.ReleaseCommittedHostRoot();cold.Domain.DestroyOwnedNavigation();Assert.Zero(cold.Domain.Navigation.PhysicalHostBudget.PhysicalBytes);}
                w.Registry.Cancel(submission);w.Registry.Unregister(source);Assert.Zero(native.Pins);}
        }
        [TestCase(false)][TestCase(true)]public void OwnerGuardRefusalPreservesPendingOrAllowedBytesAndPins(bool allowed)
        {
            using(var w=new World()){long grant=1000000;var issuer=new NativeSource();var source=w.Registry.RegisterSource(issuer,"fixture-native-retention",ref grant);var native=new NativeInput(w,w.Proposal(),issuer);Assert.True(w.Registry.TrySubmit(Retain(w.Registry,source,native),ref grant,out var s));if(allowed)Finish(w.Registry,s);byte[] before=WorldWire.Pack(w.Owner.WriteState);long retained=w.Registry.Retained,zero=0;Assert.Zero(w.Registry.Advance(s,100,ref zero));Assert.False(w.Registry.TryIssue(s,ref zero,out _));CollectionAssert.AreEqual(before,WorldWire.Pack(w.Owner.WriteState));Assert.AreEqual(retained,w.Registry.Retained);Assert.AreEqual(1,native.Pins);w.Registry.Cancel(s);w.Registry.Unregister(source);Assert.Zero(native.Pins);}
        }
        [Test]public void MatchingAndCommittedRecordRefusalColdContinuationNeverReissuesPhysicalExecution()
        {
            using(var w=new World()){long grant=1000000;var issuer=new NativeSource();var source=w.Registry.RegisterSource(issuer,"fixture-native-retention",ref grant);var native=new NativeInput(w,w.Proposal(),issuer);Assert.True(w.Registry.TrySubmit(Retain(w.Registry,source,native),ref grant,out var s));Finish(w.Registry,s);Assert.True(w.Registry.TryIssue(s,ref grant,out var issued));var actual=Retain(w.Registry,source,native);Assert.True(w.Registry.TryPrepareActual(issued,actual,ref grant));w.Registry.Advance(s,2,ref grant);
                byte[] matching=WorldWire.Pack(w.Owner.WriteState);var coldOwner=WorldWire.Unpack(matching,r=>PlayableAiOwnerLoop.ReadState(r,w.P,AiProfile.Initial,Gen,7,0));coldOwner.BindExecutionDomain(w.D);var cold=coldOwner.ExecutionOwner;try{var coldSource=cold.RegisterSource(issuer,source.Identity,ref grant);var restored=cold.AwaitingSource(source.Identity,s.Id);Assert.True(cold.TryRebind(restored,Retain(cold,coldSource,native),Retain(cold,coldSource,native),ref grant));CollectionAssert.AreEqual(matching,WorldWire.Pack(coldOwner.WriteState));while(cold.Status(restored)==AiCombatMicroExecutionStatus.Matching)cold.Advance(restored,1,ref grant);Assert.True(cold.TryIssue(restored,ref grant,out var coldIssued));Assert.True(cold.TryBeginExecution(coldIssued,ref grant,out var ticket));Assert.False(cold.TryConsumeCommitted(coldSource,ticket,ref grant,ref grant));issuer.CommitFixtureOperation(native);long shortGrant=1;Assert.False(cold.TryConsumeCommitted(coldSource,ticket,ref shortGrant,ref grant));Assert.AreEqual(AiCombatMicroExecutionStatus.Executing,cold.Status(restored));byte[] executing=WorldWire.Pack(coldOwner.WriteState);
                    var second=WorldWire.Unpack(executing,r=>PlayableAiOwnerLoop.ReadState(r,w.P,AiProfile.Initial,Gen,7,0));second.BindExecutionDomain(w.D);var secondRegistry=second.ExecutionOwner;try{var secondSource=secondRegistry.RegisterSource(issuer,source.Identity,ref grant);var secondSubmission=secondRegistry.AwaitingSource(source.Identity,s.Id);Assert.True(secondRegistry.TryRebind(secondSubmission,Retain(secondRegistry,secondSource,native),Retain(secondRegistry,secondSource,native),ref grant));CollectionAssert.AreEqual(executing,WorldWire.Pack(second.WriteState));Assert.False(secondRegistry.TryIssue(secondSubmission,ref grant,out _));Assert.True(secondRegistry.TryResumeCommitted(secondSource,secondSubmission,out var resumed));Assert.True(secondRegistry.TryConsumeCommitted(secondSource,resumed,ref grant,ref grant));Assert.False(secondRegistry.TryConsumeCommitted(secondSource,resumed,ref grant,ref grant));byte[] committed=WorldWire.Pack(second.WriteState);var third=WorldWire.Unpack(committed,r=>PlayableAiOwnerLoop.ReadState(r,w.P,AiProfile.Initial,Gen,7,0));try{CollectionAssert.AreEqual(committed,WorldWire.Pack(third.WriteState));}finally{third.BindExecutionDomain(w.D);third.ExecutionOwner.Dispose();}secondRegistry.Unregister(secondSource);}finally{secondRegistry.Dispose();}
                    // The original Executing branch is a discarded cold fork,
                    // disposed as a whole below; it cannot masquerade as a cancelled live operation.
                }finally{cold.Dispose();}w.Registry.Cancel(s);w.Registry.Unregister(source);Assert.Zero(native.Pins);}
        }
        [TestCase(false)][TestCase(true)]public void ActualAsyncSchedulerBindsReviewingOwnerAndTransfersOrDiscardsWithoutSecondIssuer(bool invalidate)
        {
            using(var w=new World())using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
                var original=WorldWire.Unpack(WorldWire.Pack(w.Owner.WriteState),r=>PlayableAiOwnerLoop.ReadState(r,w.P,AiProfile.Initial,Gen,7,0));var scheduler=new AiAuthorityScheduler(new[]{original});scheduler.EnableBackgroundReviews();w.D.BeforeOwnerReviewForTests=()=>{entered.Set();if(!release.Wait(10000))throw new TimeoutException("Async adoption control not released.");};AiCombatMicroExecutionOwner live=null;
                try{scheduler.Review(w.D,0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),7,_=>0);Assert.True(entered.Wait(5000));Assert.False(scheduler.ReviewIdle);scheduler.Deliver(w.D,_=>0,false);live=original.ExecutionOwner;long grant=1000000;var issuer=new NativeSource();var source=live.RegisterSource(issuer,"fixture-native-retention",ref grant);var native=new NativeInput(w,w.Proposal(),issuer);Assert.True(live.TrySubmit(Retain(live,source,native),ref grant,out var submission));if(invalidate)scheduler.Rebind(w.P);release.Set();Assert.True(SpinWait.SpinUntil(()=>scheduler.ReviewIdle,5000));scheduler.Deliver(w.D,_=>0,false);var current=scheduler.Owners.Single();if(invalidate)Assert.AreSame(original,current);else{Assert.AreNotSame(original,current);Assert.Throws<InvalidOperationException>(()=>original.BindExecutionDomain(w.D));}Assert.AreSame(live,current.ExecutionOwner);Finish(live,submission);Assert.True(live.TryIssue(submission,ref grant,out _));live.Cancel(submission);live.Unregister(source);Assert.Zero(native.Pins);
                }finally{w.D.BeforeOwnerReviewForTests=null;release.Set();Assert.True(SpinWait.SpinUntil(()=>scheduler.ReviewIdle,5000));scheduler.Stop(w.D.Tick);live?.Dispose();}
            }
        }
        [Test]public void ConnectedNonemptyCoverageCursorColdRestoreResumesWithoutGeometryReplay()
        {
            using(var w=new World()){
                var view=w.View();var patient=view.Entities.Single(u=>u.Id==w.Patient);var enemy=w.Config.Roster.Single(p=>p.Id=="enemy-1");int enemyId=w.D.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Enemy).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Headquarters&&b.Owner==PlayableOwner.Enemy).Id;
                var tower=new PlayableBuildingSnapshot(enemyId,PlayableOwner.Enemy,PlayableBuildingKind.SentryTurret,new NavPoint(patient.Position.X-(w.P.ExplorerRange*w.P.TurretRangeMultiplier-4),patient.Position.Z),100,1,0,0,default);var vision=new PlayableVision(0,144,144,1,1);vision.Refresh(new[]{new VisionSource(default,300)},Array.Empty<KnownBuilding>());
                var past=PlayableAiObservation.From(new PlayableSnapshot(w.P.ProfileId,w.P.Revision,Gen,7,0,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,new[]{patient},new[]{tower},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),ownerId:"player-1",participants:w.Config.Roster.ToArray()));((AiKnowledgeTracker)Field(w.Owner,"knowledge")).Observe(past,AiProfile.Initial);
                long grant=10000000;var issuer=new NativeSource();var source=w.Registry.RegisterSource(issuer,"fixture-native-retention",ref grant);var native=new NativeInput(w,w.Proposal(),issuer);Assert.True(w.Registry.TrySubmit(Retain(w.Registry,source,native),ref grant,out var submission));int cap=2000;AiCombatMicroCoverWork cover=null;while(cap-->0){w.Registry.Advance(submission,1,ref grant);cover=(AiCombatMicroCoverWork)Field(submission.Paths[0],"cover");if(cover!=null&&!cover.Complete&&cover.Consumed>=5)break;}Assert.Greater(cap,0);long consumed=cover.Consumed;byte[] bytes=WorldWire.Pack(w.Owner.WriteState);var clone=WorldWire.Unpack(bytes,r=>PlayableAiOwnerLoop.ReadState(r,w.P,AiProfile.Initial,Gen,7,0));clone.BindExecutionDomain(w.D);var cold=clone.ExecutionOwner;try{var coldSource=cold.RegisterSource(issuer,source.Identity,ref grant);var restored=cold.AwaitingSource(source.Identity,submission.Id);Assert.True(cold.TryRebind(restored,Retain(cold,coldSource,native),null,ref grant));CollectionAssert.AreEqual(bytes,WorldWire.Pack(clone.WriteState));Assert.AreEqual(consumed,((AiCombatMicroCoverWork)Field(restored.Paths[0],"cover")).Consumed);Finish(w.Registry,submission);Finish(cold,restored);CollectionAssert.AreEqual(WorldWire.Pack(w.Owner.WriteState),WorldWire.Pack(clone.WriteState));cold.Cancel(restored);cold.Unregister(coldSource);}finally{cold.Dispose();}w.Registry.Cancel(submission);w.Registry.Unregister(source);Assert.Zero(native.Pins);
            }
        }
        [TestCase("retired")][TestCase("replaced")][TestCase("dead")]
        public void ExactJobRetirementReleasesCommittedCursorOnceAndPreservesColdReplayFloor(string reason)
        {
            using(var w=new World()){var history=CommitHistory(w);var r=w.Registry;long before=r.Retained;byte[] raw=WorldWire.Pack(r.Write);var micro=(AiCombatMicroPolicy)Field(w.Owner,"combatMicro");
                if(reason=="retired")micro.Preempt(new[]{w.Patient});
                else if(reason=="dead")typeof(PlayableDomain).GetMethod("Damage",F).Invoke(w.D,new object[]{w.Patient,1000000});
                else{var goal=new NavPoint(-87,-28);var local=new PlayableAiAction(2,"player-1",w.P.ProfileId,w.P.Revision,Gen,0,PlayableCommandKind.Move,new[]{w.Patient},goal,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);micro.Commit(new AiCombatMicroPlan{Action=local,Phase=AiCombatMicroPhase.Withdraw,Army=((AiArmyRegistry)Field(w.Owner,"armies")).ArmyFor(w.Patient)},Gen,0);Assert.AreEqual(PlayableCommandStatus.Applied,w.D.Apply(new PlayableCommand(Gen,13,"player-1",PlayableCommandKind.Move,new[]{w.Patient},goal,origin:PlayableOrderOrigin.Ai,source:PlayableAiOpeningComposition.SourceIdentity,jobId:89,actionId:89),out var message),message);var actual=new PlayableAiAction(89,"player-1",w.P.ProfileId,w.P.Revision,Gen,0,local.Kind,new[]{w.Patient},goal,seed:7,sourceIdentity:local.SourceIdentity);var receipt=new AiReceiptIdentity(Gen,"player-1",2,1);micro.Applied(actual,13,0,receipt);micro.Receipt(new PlayableAiTraceRecord(w.View().Identity,2,0,13,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"replacement",actual.PlayerId,actual.SourceIdentity,receipt,AiCombatMicroPolicy.Policy,actual.Kind));}
                long refused=0;Assert.False(r.TryReconcileCommitted(ref refused));Assert.AreEqual(before,r.Retained);CollectionAssert.AreEqual(raw,WorldWire.Pack(r.Write));Assert.False(history.cursor.Disposed);
                if(reason=="retired")WorldWire.Pack(w.Owner.WriteState);else if(reason=="replaced"){long admission=1000000;var next=new NativeInput(w,w.Proposal(),history.issuer,2);Assert.True(r.TrySubmit(Retain(r,history.source,next),ref admission,out var pending));r.Cancel(pending);Assert.Zero(next.Pins);}else w.Owner.BindExecutionDomain(w.D);Assert.True(history.cursor.Disposed);Assert.Zero(((IDictionary)Field(r,"committed")).Count);Assert.Less(r.Retained,before);long after=r.Retained,grant=1000000;Assert.True(r.TryReconcileCommitted(ref grant));Assert.AreEqual(after,r.Retained);Assert.False(r.TryRetainInput(history.source,history.native,ref grant,out _));
                byte[] bytes=WorldWire.Pack(w.Owner.WriteState);var clone=WorldWire.Unpack(bytes,reader=>PlayableAiOwnerLoop.ReadState(reader,w.P,AiProfile.Initial,Gen,7,0));clone.BindExecutionDomain(w.D);try{CollectionAssert.AreEqual(bytes,WorldWire.Pack(clone.WriteState));Assert.Zero(((IDictionary)Field(clone.ExecutionOwner,"committed")).Count);var source=clone.ExecutionOwner.RegisterSource(history.issuer,history.source.Identity,ref grant);Assert.False(clone.ExecutionOwner.TryRetainInput(source,history.native,ref grant,out _));clone.ExecutionOwner.Unregister(source);}finally{clone.ExecutionOwner.Dispose();}r.Unregister(history.source);}
        }
        [Test]public void SameAppliedJobRetainsHistoryWhenHealthAndHealthyCoreTemporarilyFailAuthorization()
        {
            using(var w=new World(returning:true)){
                // Follow already inside its normal radial distance has no Native
                // route. Seed its historical DATA through the admitted production
                // codec, not a fabricated request/receipt or an execution claim.
                long grant=1000000;Assert.True(w.Registry.TryDescribe(w.Patient,ref grant,out var descriptor));byte[] fixture;using(descriptor){var at=w.D.CombatExecutionActor(w.Patient).Location.Value;fixture=WorldWire.Pack(writer=>{writer.Write(2);writer.Write(0L);writer.Write(0L);writer.Write(1);WorldWire.String(writer,"fixture-native-retention");writer.Write(1L);writer.Write(1);writer.Write(w.Patient);writer.Write(2);writer.Write(Gen);writer.Write((long)w.Patient);WorldWire.String(writer,descriptor.Identity);WorldWire.Write(writer,at.Position);WorldWire.String(writer,at.SurfaceId);writer.Write(false);writer.Write(0);writer.Write(0);});}
                w.Registry.Dispose();var restored=WorldWire.Unpack(fixture,reader=>AiCombatMicroExecutionOwner.Read(reader,w.Owner));typeof(PlayableAiOwnerLoop).GetField("executionOwner",F).SetValue(w.Owner,restored);w.Owner.BindExecutionDomain(w.D);var cursor=(AiCombatMicroExecutionPathWork.Cursor)((IDictionary)Field(restored,"committed"))[w.Patient];byte[] raw=WorldWire.Pack(restored.Write);
                typeof(PlayableDomain).GetMethod("Damage",F).Invoke(w.D,new object[]{w.Patient,90});typeof(PlayableDomain).GetMethod("Damage",F).Invoke(w.D,new object[]{w.Members[1],90});Assert.False(restored.TryDescribe(w.Patient,ref grant,out _),"current strict core really is no longer eligible");Assert.True(restored.TryReconcileCommitted(ref grant));w.Owner.BindExecutionDomain(w.D);Assert.False(cursor.Disposed);CollectionAssert.AreEqual(raw,WorldWire.Pack(restored.Write));byte[] bytes=WorldWire.Pack(w.Owner.WriteState);var clone=WorldWire.Unpack(bytes,reader=>PlayableAiOwnerLoop.ReadState(reader,w.P,AiProfile.Initial,Gen,7,0));clone.BindExecutionDomain(w.D);try{CollectionAssert.AreEqual(bytes,WorldWire.Pack(clone.WriteState));Assert.AreEqual(1,((IDictionary)Field(clone.ExecutionOwner,"committed")).Count);}finally{clone.ExecutionOwner.Dispose();}
            }
        }
        [Test]public void RetirementProtectsUnresolvedExecutingHistoryAcrossLiveAndColdBoundaries()
        {
            NativeInput second=null;using(var w=new World()){var history=CommitHistory(w);long grant=1000000;second=new NativeInput(w,w.Proposal(),history.issuer,2);Assert.True(w.Registry.TrySubmit(Retain(w.Registry,history.source,second),ref grant,out var s));Finish(w.Registry,s);Assert.True(w.Registry.TryIssue(s,ref grant,out var issued));Assert.True(w.Registry.TryPrepareActual(issued,Retain(w.Registry,history.source,second),ref grant));while(w.Registry.Status(s)==AiCombatMicroExecutionStatus.Matching)w.Registry.Advance(s,1,ref grant);Assert.True(w.Registry.TryBeginExecution(issued,ref grant,out var ticket));((AiCombatMicroPolicy)Field(w.Owner,"combatMicro")).Preempt(new[]{w.Patient});history.issuer.CommitFixtureOperation(second);Assert.False(w.Registry.TryConsumeCommitted(history.source,ticket,ref grant,ref grant));Assert.False(ticket.Used,"live-policy refusal must not burn the only resumable ticket");byte[] raw=WorldWire.Pack(w.Registry.Write);Assert.Throws<InvalidOperationException>(()=>w.Registry.Cancel(s));Assert.Throws<InvalidOperationException>(()=>w.Registry.CancelInput(s.Input));Assert.Throws<InvalidOperationException>(()=>w.Registry.Unregister(history.source));Assert.True(w.Registry.TryReconcileCommitted(ref grant));w.Owner.BindExecutionDomain(w.D);Assert.False(history.cursor.Disposed);Assert.AreEqual(AiCombatMicroExecutionStatus.Executing,w.Registry.Status(s));CollectionAssert.AreEqual(raw,WorldWire.Pack(w.Registry.Write));Assert.AreEqual(3,second.Pins,"expected/actual inputs and the real retained fixture record remain held");
                byte[] bytes=WorldWire.Pack(w.Owner.WriteState);var clone=WorldWire.Unpack(bytes,reader=>PlayableAiOwnerLoop.ReadState(reader,w.P,AiProfile.Initial,Gen,7,0));clone.BindExecutionDomain(w.D);try{Assert.True(clone.AwaitingExecutionSources);Assert.Throws<InvalidOperationException>(()=>clone.ExecutionOwner.Cancel(clone.ExecutionOwner.AwaitingSource(history.source.Identity,s.Id)));CollectionAssert.AreEqual(bytes,WorldWire.Pack(clone.WriteState));Assert.AreEqual(1,((IDictionary)Field(clone.ExecutionOwner,"committed")).Count);}finally{clone.ExecutionOwner.Dispose();}}
            Assert.Zero(second.Pins);
        }
        [Test]public void FailedUnpublishedExecutingRebindPreservesOriginalErrorImageAndLiability()
        {
            using(var w=new World()){long grant=1000000;var issuer=new NativeSource();var source=w.Registry.RegisterSource(issuer,"fixture-native-retention",ref grant);var native=new NativeInput(w,w.Proposal(),issuer);Assert.True(w.Registry.TrySubmit(Retain(w.Registry,source,native),ref grant,out var s));Finish(w.Registry,s);Assert.True(w.Registry.TryIssue(s,ref grant,out var issued));Assert.True(w.Registry.TryPrepareActual(issued,Retain(w.Registry,source,native),ref grant));while(w.Registry.Status(s)==AiCombatMicroExecutionStatus.Matching)w.Registry.Advance(s,1,ref grant);Assert.True(w.Registry.TryBeginExecution(issued,ref grant,out var ticket));issuer.CommitFixtureOperation(native);long partial=1;Assert.False(w.Registry.TryConsumeCommitted(source,ticket,ref partial,ref grant));byte[] raw=WorldWire.Pack(w.Registry.Write);
                // Controlled invalid cold binding: source watermark differs from
                // its saved Executing operation. This must fail before adoption.
                using(var stream=new MemoryStream(raw,true))using(var reader=new BinaryReader(stream,System.Text.Encoding.UTF8,true))using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){Assert.AreEqual(2,reader.ReadInt32());reader.ReadInt64();reader.ReadInt64();Assert.AreEqual(1,reader.ReadInt32());AiCombatMicroExecutionBindingWire.ReadName(reader);writer.Write(2L);}
                var cold=WorldWire.Unpack(raw,reader=>AiCombatMicroExecutionOwner.Read(reader,w.Owner));cold.Bind(w.Owner,w.D);try{var coldSource=cold.RegisterSource(issuer,source.Identity,ref grant);var original=cold.AwaitingSource(source.Identity,s.Id);byte[] before=WorldWire.Pack(cold.Write);long retained=cold.Retained;int pins=native.Pins;var expected=Retain(cold,coldSource,native);var actual=Retain(cold,coldSource,native);var error=Assert.Throws<ArgumentException>(()=>cold.TryRebind(original,expected,actual,ref grant));StringAssert.Contains("Duplicated cold Native execution",error.Message);Assert.AreSame(original,cold.AwaitingSource(source.Identity,s.Id));CollectionAssert.AreEqual(before,WorldWire.Pack(cold.Write));Assert.AreEqual(retained,cold.Retained);Assert.AreEqual(pins,native.Pins);Assert.AreEqual(1,((IList)Field(cold,"submissions")).Count);}finally{cold.Dispose();}
            }
        }
    }
}
