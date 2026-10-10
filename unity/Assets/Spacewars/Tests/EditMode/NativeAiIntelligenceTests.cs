using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiIntelligenceTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static PlayableSnapshot Snapshot(long tick,VisionSource[] sources,PlayableBuildingSnapshot[] buildings=null,PlayableEntitySnapshot[] entities=null,AiIntelEnvelope[] envelopes=null)
        {
            var vision=new PlayableVision(0,32,32,1,1);vision.Refresh(sources,Array.Empty<KnownBuilding>());
            return new PlayableSnapshot("p",1,71,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,new NavGeometry(32,Array.Empty<NavObstacle>(),1),entities??Array.Empty<PlayableEntitySnapshot>(),buildings??Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),intelEnvelopes:envelopes);
        }
        private static PlayableBuildingSnapshot Building(int health=100)=>new PlayableBuildingSnapshot(91,PlayableOwner.Enemy,PlayableBuildingKind.Factory,new NavPoint(15,0),health,1,0,0,default(NavPoint));
        private static readonly AiIntelEnvelope Area=new AiIntelEnvelope(12,new[]{new VisionSource(new NavPoint(15,0),4)});
        private static AiKnowledgeTracker Tracker()=>new AiKnowledgeTracker("player-1",71);
        private static void Observe(AiKnowledgeTracker k,PlayableSnapshot s)=>k.Observe(PlayableAiObservation.From(s),AiProfile.Initial);
        private static byte[] Bytes(AiKnowledgeTracker tracker)
        {using(var s=new MemoryStream()){using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))typeof(AiKnowledgeTracker).GetMethod("WriteState",F).Invoke(tracker,new object[]{w});return s.ToArray();}}
        private static AiKnowledgeTracker Restore(byte[] bytes,string owner="player-1",long generation=71,long tick=10000)
        {using(var r=new BinaryReader(new MemoryStream(bytes)))return (AiKnowledgeTracker)typeof(AiKnowledgeTracker).GetMethod("ReadState",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{r,owner,generation,tick,AiProfile.Initial});}
        [Test] public void Freshness_PartialEnvelopeDoesNotCertifyEmptyArea()
        {
            var k=Tracker();Observe(k,Snapshot(30,new[]{new VisionSource(new NavPoint(10,0),2)},envelopes:new[]{Area}));Assert.IsEmpty(k.Capture().Areas);
            Observe(k,Snapshot(31,new[]{new VisionSource(new NavPoint(15,0),1)},envelopes:new[]{Area}));Assert.IsEmpty(k.Capture().Areas,"center alone cannot certify footprint");
            Observe(k,Snapshot(32,new[]{new VisionSource(new NavPoint(15,0),5)},envelopes:new[]{Area}));var a=k.Capture().Areas.Single();Assert.AreEqual(32,a.ActuallyCoveredTick);Assert.True(a.EmptyAtLastCoverage);
        }
        [Test] public void Freshness_UnionCoverageRequiresNoUnseenHoles()
        {
            var v=new PlayableVision(0,32,32,1,1);v.Refresh(new[]{new VisionSource(new NavPoint(12,0),5.5),new VisionSource(new NavPoint(18,0),5.5)},Array.Empty<KnownBuilding>());Assert.True(Area.FullyCovered(v.Snapshot()));
            v.Refresh(new[]{new VisionSource(new NavPoint(12,0),3),new VisionSource(new NavPoint(18,0),3)},Array.Empty<KnownBuilding>());Assert.False(Area.FullyCovered(v.Snapshot()));
        }
        [Test] public void Freshness_CoverageDeltaReportsNewFullAndRevisitRatherThanPersistentVisibility()
        {
            var k=Tracker();var full=new[]{new VisionSource(new NavPoint(15,0),5)};
            CollectionAssert.AreEqual(new[]{12},k.Observe(PlayableAiObservation.From(Snapshot(30,full,envelopes:new[]{Area})),AiProfile.Initial));
            Assert.IsEmpty(k.Observe(PlayableAiObservation.From(Snapshot(75,full,envelopes:new[]{Area})),AiProfile.Initial));Assert.AreEqual(75,k.Capture().Areas.Single().ActuallyCoveredTick);
            Assert.IsEmpty(k.Observe(PlayableAiObservation.From(Snapshot(120,full,envelopes:new[]{Area})),AiProfile.Initial));Observe(k,Snapshot(165,Array.Empty<VisionSource>(),envelopes:new[]{Area}));CollectionAssert.AreEqual(new[]{12},k.Observe(PlayableAiObservation.From(Snapshot(210,full,envelopes:new[]{Area})),AiProfile.Initial));
            Assert.IsEmpty(k.Observe(PlayableAiObservation.From(Snapshot(210,full,envelopes:new[]{Area})),AiProfile.Initial));
        }
        [Test] public void Memory_HiddenDeathAndHealthChangesRetainLastSeenAndDecay()
        {
            var k=Tracker();Observe(k,Snapshot(30,new[]{new VisionSource(new NavPoint(15,0),5)},new[]{Building()},envelopes:new[]{Area}));var saved=k.Capture();Assert.AreEqual(100,saved.Contacts.Single().HealthAtLastSeen);
            // Even a deliberately polluted snapshot cannot refresh an invisible contact.
            Observe(k,Snapshot(1830,new[]{new VisionSource(default(NavPoint),2)},new[]{Building(1)},envelopes:new[]{Area}));var c=k.Capture().Contacts.Single();Assert.AreEqual(30,c.LastSeenTick);Assert.AreEqual(100,c.HealthAtLastSeen);Assert.AreEqual(.5,c.Confidence,1e-12);Assert.False(c.Visible);Assert.True(k.Capture().Areas.Single().RevisitDue);
            Observe(k,Snapshot(3630,new[]{new VisionSource(default(NavPoint),2)},envelopes:new[]{Area}));Assert.AreEqual(.25,k.Capture().Contacts.Single().Confidence,1e-12);Assert.AreEqual(1,saved.Contacts.Single().Confidence);
            Observe(k,Snapshot(3631,new[]{new VisionSource(new NavPoint(15,0),5)},envelopes:new[]{Area}));Assert.IsEmpty(k.Capture().Contacts);Assert.True(k.Capture().Areas.Single().EmptyAtLastCoverage);
        }
        [Test] public void Memory_VisibleConfirmationRefreshesHealthAndRevisit()
        {
            var k=Tracker();Observe(k,Snapshot(30,new[]{new VisionSource(new NavPoint(15,0),5)},new[]{Building()},envelopes:new[]{Area}));Observe(k,Snapshot(1800,new[]{new VisionSource(default(NavPoint),2)},envelopes:new[]{Area}));
            Observe(k,Snapshot(1801,new[]{new VisionSource(new NavPoint(15,0),5)},new[]{Building(17)},envelopes:new[]{Area}));Assert.AreEqual(17,k.Capture().Contacts.Single().HealthAtLastSeen);Assert.AreEqual(1801,k.Capture().Contacts.Single().LastSeenTick);Assert.False(k.Capture().Areas.Single().EmptyAtLastCoverage);Assert.False(k.Capture().Areas.Single().RevisitDue);
        }
        [Test] public void Memory_MobileContactAbsenceIsNotConfirmedDestruction()
        {
            var k=Tracker();var e=new PlayableEntitySnapshot(92,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(15,0),100,false,0,0,0);
            Observe(k,Snapshot(30,new[]{new VisionSource(new NavPoint(15,0),5)},entities:new[]{e}));Observe(k,Snapshot(31,new[]{new VisionSource(new NavPoint(15,0),5)}));Assert.False(k.Capture().Contacts.Single().Visible);Assert.IsNull(k.Capture().EnemyForceEstimate);Assert.IsNull(k.Capture().EnemyIncomeEstimate);
        }
        [Test] public void Memory_PollutedHiddenOccupancyCannotInvalidateTrueEmptyMemory()
        {
            var a=Tracker();var b=Tracker();var full=Snapshot(30,new[]{new VisionSource(new NavPoint(15,0),5)},envelopes:new[]{Area});Observe(a,full);Observe(b,full);
            Assert.True(a.Capture().Areas.Single().EmptyAtLastCoverage);
            Observe(a,Snapshot(31,new[]{new VisionSource(default(NavPoint),2)},envelopes:new[]{Area}));
            Observe(b,Snapshot(31,new[]{new VisionSource(default(NavPoint),2)},new[]{Building(1)},envelopes:new[]{Area}));
            CollectionAssert.AreEqual(Bytes(a),Bytes(b),"Hidden health/ownership/occupancy cannot invalidate confirmed historical emptiness");
        }
        [Test] public void DetachedImmutable_NoMutationOrAuthorityBackchannel()
        {
            var placement=new[]{new VisionSource(new NavPoint(15,0),4)};var area=new AiIntelEnvelope(12,placement);var s=Snapshot(1,new[]{new VisionSource(new NavPoint(15,0),5)},envelopes:new[]{area});var o=PlayableAiObservation.From(s);placement[0]=new VisionSource(default(NavPoint),20);
            Assert.AreEqual(15,o.Intel.Areas.Single().Envelope.Placement.Single().Position.X);
            Assert.Throws<NotSupportedException>(()=>((IList<AiAreaCoverage>)o.Intel.Areas).Clear());
            foreach(var type in new[]{typeof(AiKnowledgeState),typeof(AiIntelDelta),typeof(AiIntelEnvelope),typeof(AiKnownContact)})Assert.False(type.GetFields(F|BindingFlags.Public).Any(f=>typeof(Delegate).IsAssignableFrom(f.FieldType)||f.FieldType.Name=="PlayableDomain"));
        }
        [Test] public void Memory_WireRoundTripAndStrictIdentityClockCountGuards()
        {
            var k=Tracker();Observe(k,Snapshot(30,new[]{new VisionSource(new NavPoint(15,0),5)},new[]{Building()},envelopes:new[]{Area}));CollectionAssert.AreEqual(Bytes(k),Bytes(Restore(Bytes(k))));
            Assert.Throws<TargetInvocationException>(()=>Restore(Bytes(k),"foreign"));Assert.Throws<TargetInvocationException>(()=>Restore(Bytes(k),generation:72));Assert.Throws<TargetInvocationException>(()=>Restore(Bytes(k),tick:29));
            var bad=Bytes(k);using(var stream=new MemoryStream(bad,true))using(var r=new BinaryReader(stream)){int n=r.ReadInt32();stream.Position+=n+16;using(var w=new BinaryWriter(stream))w.Write(-1);}Assert.Throws<TargetInvocationException>(()=>Restore(bad));
        }
        [Test] public void Memory_CanonicalOrderingAndDerivedFreshnessRejectCorruption()
        {
            var k=Tracker();var buildings=new[]{Building(),new PlayableBuildingSnapshot(93,PlayableOwner.Enemy,PlayableBuildingKind.Outpost,new NavPoint(17,0),100,1,0,0,default(NavPoint))};
            Observe(k,Snapshot(30,new[]{new VisionSource(new NavPoint(15,0),8)},buildings,envelopes:new[]{Area,new AiIntelEnvelope(14,new[]{new VisionSource(new NavPoint(17,0),2)})}));
            var original=Bytes(k);int offset=4+System.Text.Encoding.UTF8.GetByteCount("player-1")+16+4;const int contactBytes=50;
            var bad=(byte[])original.Clone();System.Array.Copy(original,offset+contactBytes,bad,offset,contactBytes);System.Array.Copy(original,offset,bad,offset+contactBytes,contactBytes);Assert.Throws<TargetInvocationException>(()=>Restore(bad),"unordered contacts");
            int areasOffset=offset+contactBytes*2+4;bad=(byte[])original.Clone();System.Array.Copy(original,areasOffset+14,bad,areasOffset,14);System.Array.Copy(original,areasOffset,bad,areasOffset+14,14);Assert.Throws<TargetInvocationException>(()=>Restore(bad),"unordered areas");
            bad=(byte[])original.Clone();System.Array.Copy(BitConverter.GetBytes(.5),0,bad,offset+41,8);Assert.Throws<TargetInvocationException>(()=>Restore(bad),"corrupt confidence");
            bad=(byte[])original.Clone();bad[areasOffset+13]=1;Assert.Throws<TargetInvocationException>(()=>Restore(bad),"corrupt revisit flag");
        }
        private static double[,] Costs(int n){var costs=new double[n,n];for(int i=0;i<n;i++)for(int j=0;j<n;j++)costs[i,j]=i==j?0:1;return costs;}
        private static object OwnerLoop(PlayableAuthorityTick authority)=>((System.Collections.IEnumerable)authority.GetType().GetField("scheduler",F).GetValue(authority).GetType().GetField("owners",F).GetValue(authority.GetType().GetField("scheduler",F).GetValue(authority))).Cast<object>().First();
        private static void Step(PlayableAuthorityTick a)
        {while(a.Requests.TryDequeue(out var request))Assert.True(a.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>())));if(!a.TryAdvance()){while(a.Requests.TryDequeue(out var request))Assert.True(a.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>())));Assert.True(a.TryAdvance());}}
        [Test] public void Memory_AuthorityPauseRestartProfileBarrierAndByteExactRestore()
        {
            var basis=OfflineParticipantAuthorityTests.Config(2,false);var c=new OfflineMatchConfiguration(basis.Profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.Select(p=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,OfflineControl.Ai)).ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),Costs(basis.Starts.Count));
            var a=new PlayableAuthorityTick(c,71);for(int i=0;i<45;i++)Step(a);var checkpoints=a.CaptureDiagnosticCheckpoints();Assert.True(checkpoints.All(p=>p.Knowledge.ObservationTick==a.Tick&&p.Knowledge.Areas.Count>0));
            var original=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes(original,c);CollectionAssert.AreEqual(original,b.CaptureBytes());for(int i=0;i<45;i++){Step(a);Step(b);}CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
            var memory=a.CaptureDiagnosticCheckpoints().Select(p=>p.Knowledge).ToArray();for(int i=0;i<60;i++)Assert.True(a.TryAdvance(paused:true));Assert.AreEqual(90,a.Tick);for(int i=0;i<memory.Length;i++)Assert.AreSame(memory[i],a.CaptureDiagnosticCheckpoints()[i].Knowledge);
            var profile=AiProfile.Initial.CopyData();profile.revision++;profile.fields.Single(p=>p.path=="scouting.contactHalfLifeSeconds").value=120;profile.fields.Single(p=>p.path=="scouting.revisitSeconds").value=90;var next=new AiProfile(profile);Assert.IsNull(a.ApplyProfile(c.Profile,next));
            CollectionAssert.AreEqual(a.CaptureBytes(),PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,next).CaptureBytes());var fresh=new PlayableAuthorityTick(c,72);Assert.True(fresh.CaptureDiagnosticCheckpoints().All(p=>p.Knowledge.Generation==72&&p.Knowledge.ObservationTick==-1&&p.Knowledge.Contacts.Count==0));fresh.Stop();a.Stop();b.Stop();
        }
        [Test] public void Freshness_AppliedScoutCommandHasSeparateClockWithoutSurveySuccess()
        {
            var text=File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures/e6-lost-hq/manifest.json"));
            var manifest=Newtonsoft.Json.JsonConvert.DeserializeObject<Spacewars.Headless.MatchManifest>(text);
            var input=new Spacewars.Headless.ArmyRecoveryManifest{OriginalFixture="e6-lost-hq",OriginalManifest=manifest,OriginalManifestHash=Spacewars.Headless.Wire.Hash(System.Text.Encoding.UTF8.GetBytes(text)),MapIdentity="a3-derived-e6-lost-hq-hq-guard-v1"};
            var c=Spacewars.Headless.ArmyRecoveryFixture.Create(input);var a=new PlayableAuthorityTick(c,manifest.Generation);Spacewars.Headless.ArmyRecoveryFixture.BindCommands(input,a);
            foreach(var command in input.Commands)Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(command.Create(manifest.Generation)).Status);
            while(a.Tick<35*30&&a.CaptureDiagnosticCheckpoints().Single().LastScoutOrderTick<0)Step(a);var proof=a.CaptureDiagnosticCheckpoints().Single();
            Assert.True(proof.Records.Any(r=>r.Policy=="scout"&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied));
            Assert.Greater(proof.LastScoutOrderTick,0);Assert.Less(proof.LastScoutTick,proof.LastScoutOrderTick);
            Step(a);Step(a);var owner=OwnerLoop(a);var oldOrder=(long)owner.GetType().GetField("lastScoutOrderTick",F).GetValue(owner);var oldSurvey=(long)owner.GetType().GetField("lastScoutTick",F).GetValue(owner);
            owner.GetType().GetField("lastScoutOrderTick",F).SetValue(owner,oldOrder+1);Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c));owner.GetType().GetField("lastScoutOrderTick",F).SetValue(owner,oldOrder);
            var tracker=(AiKnowledgeTracker)owner.GetType().GetField("knowledge",F).GetValue(owner);var current=tracker.Capture();Assert.Less(proof.Knowledge.ObservationTick,current.ObservationTick);
            typeof(AiKnowledgeTracker).GetField("state",F).SetValue(tracker,proof.Knowledge);var aged=a.CaptureBytes();CollectionAssert.AreEqual(aged,PlayableAuthorityTick.RestoreBytes(aged,c).CaptureBytes());
            owner.GetType().GetField("lastScoutTick",F).SetValue(owner,proof.Knowledge.ObservationTick+1);Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c));owner.GetType().GetField("lastScoutTick",F).SetValue(owner,oldSurvey);typeof(AiKnowledgeTracker).GetField("state",F).SetValue(tracker,current);
            var bytes=a.CaptureBytes();var twin=PlayableAuthorityTick.RestoreBytes(bytes,c);CollectionAssert.AreEqual(bytes,twin.CaptureBytes());
            // The explicit no-route lifecycle fixture never arrives. A still visible
            // own economic envelope remains freshly observed without suppressing orders.
            long bound=proof.LastScoutOrderTick+35*30+AiProfile.SecondsToTicks(AiProfile.Initial.DifficultyValue(AiDifficulty.Fighter,"decisionSeconds"),30)+AiProfile.SecondsToTicks(AiProfile.Initial.DifficultyValue(AiDifficulty.Fighter,"reactionSeconds"),30);
            while(twin.Tick<=bound&&twin.CaptureDiagnosticCheckpoints().Single().LastScoutOrderTick==proof.LastScoutOrderTick)Step(twin);
            var next=twin.CaptureDiagnosticCheckpoints().Single();Assert.Greater(next.LastScoutOrderTick,proof.LastScoutOrderTick);Assert.GreaterOrEqual(next.LastScoutOrderTick-proof.LastScoutOrderTick,AiProfile.SecondsToTicks(AiProfile.Initial.Value("armies.stallSeconds"),30));Assert.False(next.Knowledge.Areas.Any(area=>area.AreaId==2));
            var domain=a.GetType().GetField("domain",F).GetValue(a);var unitRegistry=domain.GetType().GetField("units",F).GetValue(domain);var units=((System.Collections.IEnumerable)unitRegistry.GetType().GetProperty("Values").GetValue(unitRegistry)).Cast<object>();
            var scout=units.Single(u=>(PlayableOwner)u.GetType().GetField("Owner").GetValue(u)==PlayableOwner.Player&&(PlayableEntityKind)u.GetType().GetField("Kind").GetValue(u)==PlayableEntityKind.Explorer);scout.GetType().GetField("Health").SetValue(scout,0);Step(a);
            var afterDeath=a.CaptureDiagnosticCheckpoints().Single();Assert.AreEqual(proof.LastScoutOrderTick,afterDeath.LastScoutOrderTick);Assert.False(a.ParticipantView(proof.OwnerId).Entities.Any(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer&&e.Health>0));Assert.False(afterDeath.Knowledge.Areas.Any(area=>area.AreaId==2));a.Stop();twin.Stop();

        }
        [Test] public void Memory_AuthorityRejectsCorruptContactOwnershipWithoutReset()
        {
            var basis=OfflineParticipantAuthorityTests.Config(2,false);var c=new OfflineMatchConfiguration(basis.Profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.Select(p=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,OfflineControl.Ai)).ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),Costs(basis.Starts.Count));
            var a=new PlayableAuthorityTick(c,71);Step(a);var loop=OwnerLoop(a);var k=(AiKnowledgeTracker)loop.GetType().GetField("knowledge",F).GetValue(loop);var good=k.Capture();var view=a.ParticipantView(good.OwnerId);
            typeof(AiKnowledgeTracker).GetField("state",F).SetValue(k,new AiKnowledgeState(good.OwnerId,71,good.ObservationTick,new[]{new AiKnownContact(view.Entities.First().Id,view.Owner,false,0,view.Entities.First().Position,100,good.ObservationTick,1,true)},good.Areas));
            var bad=a.CaptureBytes();Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(bad,c));typeof(AiKnowledgeTracker).GetField("state",F).SetValue(k,good);CollectionAssert.AreEqual(a.CaptureBytes(),PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c).CaptureBytes());a.Stop();
        }
        [Test] public void Memory_RealHiddenWorldChangesPreservePopulatedMemoryAndPlans()
        {
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);var config=OfflineParticipantAuthorityTests.Config(3,false);var roster=config.Roster.ToArray();
            object Create()=>Activator.CreateInstance(type,F,null,new object[]{config.Profile,71L,config},null);var a=Create();var b=Create();
            PlayableSnapshot View(object d)=>(PlayableSnapshot)type.GetMethod("PlayerSnapshot",F).Invoke(d,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed,PlayableOwner.Player});
            var loopType=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop",true);object Loop()=>Activator.CreateInstance(loopType,F,null,new object[]{config.Profile,PlayableAiOpeningComposition.Initialize(config.Seed,roster[0].Id),71L,null,AiProfile.Initial,AiDifficulty.Fighter},null);var la=Loop();var lb=Loop();
            void Review(object loop,object d)=>loopType.GetMethod("Review",F).Invoke(loop,new object[]{View(d),0L});
            object[] Values(object d,string name)=>((System.Collections.IEnumerable)type.GetField(name,F).GetValue(d).GetType().GetProperty("Values").GetValue(type.GetField(name,F).GetValue(d))).Cast<object>().ToArray();
            foreach(var d in new[]{a,b})
            {
                var initial=View(d);var nav=(NavigationSession)type.GetProperty("Navigation",F).GetValue(d);nav.Crowd.TryGet(initial.Entities.Single(e=>e.Owner==PlayableOwner.Player).Id,out var unit);var home=unit.Position;
                var enemy=Values(d,"buildings").First(x=>(PlayableOwner)x.GetType().GetField("Owner").GetValue(x)==PlayableOwner.Enemy);typeof(NavUnit).GetProperty("Position").SetValue(unit,(NavPoint)enemy.GetType().GetField("Position").GetValue(enemy));Review(d==a?la:lb,d);typeof(NavUnit).GetProperty("Position").SetValue(unit,home);
                for(int i=0;i<45;i++)type.GetMethod("Step",F).Invoke(d,new object[]{1d/30});
            }
            Assert.Greater(((AiKnowledgeTracker)loopType.GetField("knowledge",F).GetValue(la)).Capture().Contacts.Count,0);
            PlayableCommandStatus Apply(object domain,PlayableCommand command){object[] args={command,null};return (PlayableCommandStatus)type.GetMethod("Apply",F).Invoke(domain,args);}
            var credits=(Dictionary<PlayableOwner,double>)type.GetField("ownerCredits",F).GetValue(b);
            // Both worlds get the same ordinary geometry refresh: SetGeometry changes
            // even an idle own agent to Stopped, which is a permitted own observation.
            // Counterfactual inputs must keep that observation equal, not erase it.
            foreach(var d in new[]{a,b})
            {((Dictionary<PlayableOwner,double>)type.GetField("ownerCredits",F).GetValue(d))[PlayableOwner.Enemy]=98765;Assert.AreEqual(PlayableCommandStatus.Applied,Apply(d,new PlayableCommand(71,1,roster[1].Id,PlayableCommandKind.BuildFactory,Array.Empty<int>())));}
            // Build the hidden producer through ordinary rules, with identical clocks in
            // both worlds. No production order or ownership is injected into AI memory.
            for(int i=0;i<AiProfile.SecondsToTicks(config.Profile.FactoryBuildSeconds,30)+1;i++)foreach(var d in new[]{a,b})type.GetMethod("Step",F).Invoke(d,new object[]{1d/30});
            var factory=Values(b,"buildings").Single(x=>(PlayableOwner)x.GetType().GetField("Owner").GetValue(x)==PlayableOwner.Enemy&&(PlayableBuildingKind)x.GetType().GetField("Kind").GetValue(x)==PlayableBuildingKind.Factory);
            Assert.AreEqual(ConstructionPhase.Ready,factory.GetType().GetField("Phase").GetValue(factory));
            var hiddenUnit=Values(b,"units").Single(x=>(PlayableOwner)x.GetType().GetField("Owner").GetValue(x)==PlayableOwner.Enemy);int unitId=(int)hiddenUnit.GetType().GetField("Id").GetValue(hiddenUnit);var navigation=(NavigationSession)type.GetProperty("Navigation",F).GetValue(b);navigation.Crowd.TryGet(unitId,out var mover);
            var goal=(from dx in Enumerable.Range(3,4) from dz in Enumerable.Range(-3,7) let point=new NavPoint(mover.Position.X+dx,mover.Position.Z+dz) where ((NavGeometry)type.GetProperty("Geometry",F).GetValue(b)).IsFree(point,config.Profile.ExplorerCollisionRadius)&&!View(b).Vision.IsVisible(point) select point).First();
            var status=Apply(b,new PlayableCommand(71,2,roster[1].Id,PlayableCommandKind.Move,new[]{unitId},target:goal));Assert.That(status,Is.EqualTo(PlayableCommandStatus.Accepted).Or.EqualTo(PlayableCommandStatus.Applied));
            foreach(var d in new[]{a,b})
            {
                var nav=(NavigationSession)type.GetProperty("Navigation",F).GetValue(d);
                // This request belongs to the ordinary Explorer SharedFlow route, not
                // the tank NavMesh backend. Deliver its actual native solver answer.
                while(nav.Requests.TryDequeue(out var request))
                {Assert.AreEqual(unitId,request.Entity);Assert.True(nav.Answers.TryEnqueue(new NavigationAnswer(request,new SharedFlowRouter(request.Geometry,request.Profile).FindPath(request.Start,request.Goal))));}
                typeof(NavigationSession).GetMethod("PrepareDeliveryBarrier",F).Invoke(nav,null);Assert.True((bool)typeof(NavigationSession).GetProperty("DeliveryBarrierReady",F).GetValue(nav));
                type.GetMethod("Step",F).Invoke(d,new object[]{1d/30});
            }
            var order=(PlayableTacticalOrderSnapshot)hiddenUnit.GetType().GetField("CurrentOrder").GetValue(hiddenUnit);Assert.NotNull(order);Assert.AreEqual(goal,order.Destination);
            Assert.False(View(b).Vision.IsVisible(mover.Position));Assert.False(View(b).Entities.Any(e=>e.Id==unitId));hiddenUnit.GetType().GetField("Health").SetValue(hiddenUnit,17);Assert.AreEqual(17,hiddenUnit.GetType().GetField("Health").GetValue(hiddenUnit));
            foreach(var building in Values(b,"buildings").Where(x=>(PlayableOwner)x.GetType().GetField("Owner").GetValue(x)==PlayableOwner.Enemy))
            {Assert.False(View(b).Vision.IsVisible((NavPoint)building.GetType().GetField("Position").GetValue(building)));Assert.False(View(b).Buildings.Any(e=>e.Id==(int)building.GetType().GetField("Id").GetValue(building)));building.GetType().GetField("Health").SetValue(building,123d);Assert.AreEqual(123d,building.GetType().GetField("Health").GetValue(building));}
            factory.GetType().GetField("Owner").SetValue(factory,PlayableOwner.Third);Assert.AreEqual(PlayableOwner.Third,factory.GetType().GetField("Owner").GetValue(factory));credits[PlayableOwner.Third]=98765;credits[PlayableOwner.Enemy]=98765;Assert.AreNotEqual(((Dictionary<PlayableOwner,double>)type.GetField("ownerCredits",F).GetValue(a))[PlayableOwner.Enemy],credits[PlayableOwner.Enemy]);
            int factoryId=(int)factory.GetType().GetField("Id").GetValue(factory);for(int i=1;i<=3;i++)Assert.AreEqual(PlayableCommandStatus.Applied,Apply(b,new PlayableCommand(71,i,roster[2].Id,PlayableCommandKind.QueueExplorer,new[]{factoryId})));
            Assert.AreEqual(3,factory.GetType().GetProperty("Queue").GetValue(factory));Assert.Greater(credits[PlayableOwner.Enemy],90000);Assert.Greater(credits[PlayableOwner.Third],90000);
            Assert.False(View(b).Vision.IsVisible((NavPoint)factory.GetType().GetField("Position").GetValue(factory)));Assert.False(View(b).Vision.IsVisible(mover.Position));
            foreach(var building in Values(b,"buildings").Where(x=>(double)x.GetType().GetField("Health").GetValue(x)==123d)){Assert.False(View(b).Vision.IsVisible((NavPoint)building.GetType().GetField("Position").GetValue(building)));Assert.False(View(b).Buildings.Any(e=>e.Id==(int)building.GetType().GetField("Id").GetValue(building)));}
            var encode=typeof(NativeAiFoundationTests).GetMethod("ProjectionBytes",BindingFlags.Static|BindingFlags.NonPublic);CollectionAssert.AreEqual((byte[])encode.Invoke(null,new object[]{PlayableAiObservation.From(View(a))}),(byte[])encode.Invoke(null,new object[]{PlayableAiObservation.From(View(b))}));
            Review(la,a);Review(lb,b);CollectionAssert.AreEqual(Bytes((AiKnowledgeTracker)loopType.GetField("knowledge",F).GetValue(la)),Bytes((AiKnowledgeTracker)loopType.GetField("knowledge",F).GetValue(lb)));
            byte[] LoopBytes(object loop){using(var stream=new MemoryStream()){using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))loopType.GetMethod("WriteState",F).Invoke(loop,new object[]{w});return stream.ToArray();}}
            CollectionAssert.AreEqual(LoopBytes(la),LoopBytes(lb));Assert.Greater(((PlayableAiOwnerCheckpoint)loopType.GetProperty("Checkpoint",F).GetValue(la)).PendingActionId,0);
        }
        private static PlayableAiObservation ScoutView(long tick=30,int scouts=3,bool seeBase=false,int[] covered=null,double detour=0,bool routes=true)
        {
            var targets=new[]{new PlayablePublicScoutObjective(12,new NavPoint(15,0),PlayablePublicScoutObjectiveRole.ExpansionIncome,true),new PlayablePublicScoutObjective(13,new NavPoint(0,15),PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true)};
            var units=Enumerable.Range(1,scouts).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(0,0),100,false,0,0,0)).ToArray();
            var vision=new PlayableVision(0,32,32,1,1);vision.Refresh(seeBase?new[]{new VisionSource(new NavPoint(15,0),8)}:Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());
            var proofs=routes?(from u in units from t in targets select new PlayableRouteProof(u.Id,u.Owner,71,tick,1,u.Position,PlayableProfile.Default.ExplorerCollisionRadius,PlayableRouteTargetKind.PublicObjective,t.SiteId,t.Approach,t.Approach,t.SiteId==12&&detour>0?new[]{new NavPoint(detour,0),t.Approach}:new[]{t.Approach})).ToArray():Array.Empty<PlayableRouteProof>();
            return PlayableAiObservation.From(new PlayableSnapshot("p",1,71,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,new NavGeometry(32,Array.Empty<NavObstacle>(),1),units,seeBase?new[]{Building()}:Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),publicScoutObjectives:targets,routeProofs:proofs,intelEnvelopes:new[]{Area,new AiIntelEnvelope(13,new[]{new VisionSource(new NavPoint(0,15),4)})}));
        }
        private static AiKnowledgeState ScoutKnowledge(PlayableAiObservation o,params AiVisitedArea[] areas)=>new AiKnowledgeState(o.OwnerId,o.Generation,o.Tick,Array.Empty<AiKnownContact>(),areas);
        [Test] public void HiddenBase_PublicEconomicQueueRevisitsAndRequiresActualContact()
        {
            var planner=new AiScoutPlanner();var r=new AiArmyRegistry("player-1",71,AiProfile.Initial,AiDifficulty.Fighter);var o=ScoutView();
            var k=ScoutKnowledge(o,new AiVisitedArea(13,30,true,false));var action=planner.Plan(o,k,r,PlayableProfile.Default,AiProfile.Initial,2);
            Assert.AreEqual(12,action.SiteId);Assert.False(planner.BaseSearchSuccess(k));planner.Commit(o,action,r);
            Assert.AreEqual(AiArmyRole.Scout,r.Capture().Armies.Single().Role);Assert.False(planner.BaseSearchSuccess(k));
            var tracker=Tracker();tracker.Observe(ScoutView(31,seeBase:true),AiProfile.Initial);Assert.True(planner.BaseSearchSuccess(tracker.Capture()));
            planner.Observe(ScoutView(31,seeBase:true),tracker.Capture(),r,AiProfile.Initial,2);Assert.Zero(r.ArmyFor(1));
            var later=ScoutView(31+AiProfile.SecondsToTicks(AiProfile.Initial.Value("scouting.revisitSeconds"),30));tracker.Observe(later,AiProfile.Initial);
            Assert.True(tracker.Capture().Areas.Single().RevisitDue);
            var known=tracker.Capture();known=new AiKnowledgeState(known.OwnerId,known.Generation,known.ObservationTick,known.Contacts,known.Areas.Concat(new[]{new AiVisitedArea(13,later.Tick,true,false)}));
            Assert.AreEqual(12,planner.Plan(later,known,r,PlayableProfile.Default,AiProfile.Initial,2).SiteId);
        }
        [Test] public void HiddenBase_TravelUsesNativePathAndRiskUsesOnlyKnownContacts()
        {
            var planner=new AiScoutPlanner();var r=new AiArmyRegistry("player-1",71,AiProfile.Initial,AiDifficulty.Fighter);var o=ScoutView();var k=ScoutKnowledge(o);
            Assert.AreEqual(12,planner.Plan(o,k,r,PlayableProfile.Default,AiProfile.Initial,2).SiteId);
            o=ScoutView(detour:1000);Assert.AreEqual(13,planner.Plan(o,ScoutKnowledge(o),r,PlayableProfile.Default,AiProfile.Initial,2).SiteId);
            o=ScoutView();k=new AiKnowledgeState(o.OwnerId,71,o.Tick,new[]{new AiKnownContact(91,PlayableOwner.Enemy,true,(int)PlayableBuildingKind.Factory,new NavPoint(15,0),100,0,1,false)},Array.Empty<AiVisitedArea>());
            Assert.AreEqual(13,planner.Plan(o,k,r,PlayableProfile.Default,AiProfile.Initial,2).SiteId);
            o=ScoutView(routes:false);Assert.Null(planner.Plan(o,ScoutKnowledge(o),r,PlayableProfile.Default,AiProfile.Initial,2));
        }
        [Test] public void ScoutBudget_DifficultyPersonalityAndBlindRushAreBounded()
        {
            var p=new AiScoutPlanner();foreach(AiDifficulty d in Enum.GetValues(typeof(AiDifficulty)))
            {
                int cap=(int)AiProfile.Initial.DifficultyValue(d,"scoutAssignments");
                Assert.AreEqual(cap,p.Budget(AiProfile.Initial,d,uint.MaxValue,AiScoutPlan.ScoutLed));
                Assert.AreEqual(1,p.Budget(AiProfile.Initial,d,0,AiScoutPlan.Standard));
                Assert.AreEqual(cap,p.Budget(AiProfile.Initial,d,uint.MaxValue,AiScoutPlan.Standard));
                Assert.Less(p.Budget(AiProfile.Initial,d,0,AiScoutPlan.BlindRush),cap);
            }
            var o=ScoutView();var r=new AiArmyRegistry("player-1",71,AiProfile.Initial,AiDifficulty.Fighter);var k=ScoutKnowledge(o);
            Assert.Null(p.Plan(o,k,r,PlayableProfile.Default,AiProfile.Initial,0));var first=p.Plan(o,k,r,PlayableProfile.Default,AiProfile.Initial,1);p.Commit(o,first,r);
            var second=p.Plan(o,k,r,PlayableProfile.Default,AiProfile.Initial,1);Assert.True(second==null||second.EntityIds.SequenceEqual(first.EntityIds));
            p.Observe(o,k,r,AiProfile.Initial,0);Assert.Zero(r.ArmyFor(first.EntityIds.Single()));
        }
        [Test] public void ScoutBudget_NoRaidBypassAndAssignmentsRoundtrip()
        {
            var p=new AiScoutPlanner();var o=ScoutView();var r=new AiArmyRegistry("player-1",71,AiProfile.Initial,AiDifficulty.Fighter);var k=ScoutKnowledge(o);var action=p.Plan(o,k,r,PlayableProfile.Default,AiProfile.Initial,2);p.Commit(o,action,r);
            var raid=new AiIntent("raid","scout",new PlayableAiAction(2,o.OwnerId,o.ProfileId,o.ProfileRevision,71,o.SnapshotSequence,PlayableCommandKind.Attack,action.EntityIds.ToArray(),targetId:91),100,0,0,Array.Empty<string>());
            StringAssert.Contains("requires major army",r.Reject(raid));var restored=AiArmyRegistry.Restore(r.Capture(),AiProfile.Initial,AiDifficulty.Fighter,o.Tick);
            Assert.AreEqual(r.ArmyFor(1),restored.ArmyFor(1));Assert.AreEqual(r.Capture().Armies.Single().Objective,restored.Capture().Armies.Single().Objective);
            var stalled=ScoutView(o.Tick+AiProfile.SecondsToTicks(AiProfile.Initial.Value("armies.stallSeconds"),30));p.Observe(stalled,ScoutKnowledge(stalled),restored,AiProfile.Initial,2);Assert.Zero(restored.ArmyFor(1));Assert.IsEmpty(k.Areas);
        }
        [Test] public void HiddenBase_ActualNativeTravelFindsHiddenEconomicExpansion()
        {
            var c=(OfflineMatchConfiguration)typeof(NativeAiExpansionTests).GetMethod("Config",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{PlayableBuildingKind.Mine,true});
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);var d=Activator.CreateInstance(type,F,null,new object[]{c.Profile,71L,c},null);
            PlayableSnapshot View(bool routes)=> (PlayableSnapshot)type.GetMethod(routes?"PlayerSnapshotForAi":"PlayerSnapshot",F).Invoke(d,routes?new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player,true}:new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player});
            var first=View(true);Assert.False(first.Vision.IsVisible(c.Sites.Single(s=>s.Id==4).Position));Assert.False(first.Buildings.Any(b=>b.SiteId==4));
            var o=PlayableAiObservation.From(first);var k=new AiKnowledgeTracker(o.OwnerId,71);k.Observe(o,AiProfile.Initial);
            var supplied=k.Capture();supplied=new AiKnowledgeState(o.OwnerId,71,o.Tick,supplied.Contacts,o.PublicScoutObjectives.Where(t=>t.SiteId!=4).Select(t=>new AiVisitedArea(t.SiteId,o.Tick,true,false)));
            var planner=new AiScoutPlanner();var registry=new AiArmyRegistry(o.OwnerId,71,AiProfile.Initial,AiDifficulty.Fighter);
            var action=planner.Plan(o,supplied,registry,c.Profile,AiProfile.Initial,2);Assert.NotNull(action,"Hidden public economic destination must have an observed-geometry route");Assert.AreEqual(4,action.SiteId);Assert.False(planner.BaseSearchSuccess(k.Capture()));planner.Commit(o,action,registry);
            object[] apply={new PlayableCommand(71,1,o.OwnerId,PlayableCommandKind.Move,action.EntityIds.ToArray(),target:action.Target),null};var status=(PlayableCommandStatus)type.GetMethod("Apply",F).Invoke(d,apply);Assert.That(status,Is.EqualTo(PlayableCommandStatus.Accepted).Or.EqualTo(PlayableCommandStatus.Applied));Assert.False(planner.BaseSearchSuccess(k.Capture()));
            var nav=(NavigationSession)type.GetProperty("Navigation",F).GetValue(d);
            int steps=0;while(steps++<1000&&!planner.BaseSearchSuccess(k.Capture()))
            {
                while(nav.Requests.TryDequeue(out var request))Assert.True(nav.Answers.TryEnqueue(new NavigationAnswer(request,new SharedFlowRouter(request.Geometry,request.Profile).FindPath(request.Start,request.Goal))));
                typeof(NavigationSession).GetMethod("PrepareDeliveryBarrier",F).Invoke(nav,null);Assert.True((bool)typeof(NavigationSession).GetProperty("DeliveryBarrierReady",F).GetValue(nav));
                type.GetMethod("Step",F).Invoke(d,new object[]{1d/30});k.Observe(PlayableAiObservation.From(View(false)),AiProfile.Initial);
            }
            Assert.True(planner.BaseSearchSuccess(k.Capture()),"Actual native scout failed to discover economic base");Assert.True(k.Capture().Contacts.Any(x=>x.Building&&x.Kind==(int)PlayableBuildingKind.Outpost&&x.Visible));
            Assert.Greater(steps,1);Assert.False(k.Capture().Contacts.Any(x=>x.Kind==(int)PlayableBuildingKind.Headquarters));
        }
        [Test] public void ForeignAndStaleObservationRejectedWithoutMutation()
        {
            var k=Tracker();Observe(k,Snapshot(30,Array.Empty<VisionSource>()));var before=Bytes(k);Assert.Throws<ArgumentException>(()=>Observe(k,Snapshot(29,Array.Empty<VisionSource>())));CollectionAssert.AreEqual(before,Bytes(k));
        }
        [Test] public void Freshness_ActualAcceptedMoveWithoutArrivalAndDeathDoesNotSurvey()
        {
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);var p=PlayableProfile.Default;var d=Activator.CreateInstance(type,F,null,new object[]{p,71L,false},null);
            PlayableSnapshot View()=>(PlayableSnapshot)type.GetMethod("PlayerSnapshot",F).Invoke(d,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player});
            var initial=View();var own=initial.Entities.Single(e=>e.Owner==initial.Owner);var goal=initial.PublicScoutObjectives.First().Approach;var k=Tracker();Observe(k,initial);
            var target=initial.IntelEnvelopes.Single(e=>e.AreaId==initial.PublicScoutObjectives.First().SiteId);Assert.False(target.FullyCovered(initial.Vision));
            var command=new PlayableCommand(71,1,initial.OwnerId,PlayableCommandKind.Move,new[]{own.Id},target:goal);object[] args={command,null};var status=(PlayableCommandStatus)type.GetMethod("Apply",F).Invoke(d,args);Assert.That(status,Is.EqualTo(PlayableCommandStatus.Accepted).Or.EqualTo(PlayableCommandStatus.Applied));Observe(k,View());Assert.False(k.Capture().Areas.Any(a=>a.AreaId==target.AreaId));
            var registry=type.GetField("units",F).GetValue(d);var unit=registry.GetType().GetProperty("Item").GetValue(registry,new object[]{own.Id});unit.GetType().GetField("Health").SetValue(unit,0);Observe(k,View());Assert.False(k.Capture().Areas.Any(a=>a.AreaId==target.AreaId));
        }
    }
}
