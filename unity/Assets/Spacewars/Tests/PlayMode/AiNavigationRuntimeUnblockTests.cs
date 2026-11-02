using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class AiNavigationRuntimeUnblockTests
    {
        private const int Seed=7,Horizon=600,SaveAfter=120;
        private const long Generation=902;
        private sealed class Body
        {public int Id;public PlayableOwner Owner;public PlayableEntityKind Kind;public NavPoint Position;public double Health,Heading,Height;public NavigationOutcome Outcome;public long Installed;}
        private sealed class Frame {public long Tick;public Body[] Bodies;}
        private static readonly Type NativeType=typeof(RecastNavigationMap).Assembly.GetType("Spacewars.Simulation.RecastNative",true);
        private static ulong NativeAllocations()=>(ulong)NativeType.GetMethod("sw_recast_allocation_count",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        private static long ReleaseFaults()=>(long)NativeType.GetProperty("ReleaseFaults",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        private static void ExactCurrentGameState(byte[] saved,byte[] recaptured)
        {
            using var before=PlayableWorldState.Decode(saved);using var after=PlayableWorldState.Decode(recaptured);
            Assert.AreEqual(before.Version,after.Version);Assert.AreEqual(before.Tick,after.Tick);Assert.AreEqual(before.Seed,after.Seed);Assert.AreEqual(before.Generation,after.Generation);Assert.AreEqual(before.SourceIdentity,after.SourceIdentity);
            CollectionAssert.AreEqual(before.Binding,after.Binding);CollectionAssert.AreEqual(before.Participants,after.Participants);CollectionAssert.AreEqual(before.Domain,after.Domain);CollectionAssert.AreEqual(before.Navigation,after.Navigation);CollectionAssert.AreEqual(before.RuntimeTransport,after.RuntimeTransport);
            Assert.AreEqual(before.Vision.Length,after.Vision.Length);for(int i=0;i<before.Vision.Length;i++)CollectionAssert.AreEqual(before.Vision[i],after.Vision[i]);
            // RequestCapture publishes another view even while paused. Its
            // monotonic publication sequence is an actual new operation, not
            // changed game time/state. Every other authority byte must match.
            int Header(byte[] bytes,out long publication){using var reader=new BinaryReader(new MemoryStream(bytes));reader.ReadInt32();reader.ReadInt32();WorldWire.String(reader);int offset=checked((int)reader.BaseStream.Position);publication=reader.ReadInt64();return offset;}
            int a=Header(before.AiAuthority,out var oldPublication),b=Header(after.AiAuthority,out var newPublication);Assert.AreEqual(a,b);Assert.GreaterOrEqual(newPublication,oldPublication);
            CollectionAssert.AreEqual(before.AiAuthority.Take(a),after.AiAuthority.Take(b));CollectionAssert.AreEqual(before.AiAuthority.Skip(a+8),after.AiAuthority.Skip(b+8));
        }
        private static PlayableDomain Domain(PlayableRuntime runtime)=>(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
        private static OfflineMatchConfiguration Configuration(PlayableProfile p,out OfflineScenarioUnit[] scenario)
        {
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,new NavPoint(-22,-22),p),new TerritorySite(2,PlayableBuildingKind.Headquarters,new NavPoint(22,22),p)};
            var starts=new[]{new OfflineStart("west",1,sites[0].Position,new NavPoint(-22,-12),pin:1),new OfflineStart("east",2,sites[1].Position,new NavPoint(22,12),pin:2)};
            var units=new List<OfflineScenarioUnit>();
            foreach(int owner in new[]{1,2}){
                double sign=owner==1?-1:1;
                foreach(double x in new[]{6d,9d,12d})foreach(double z in new[]{-6d,0d,6d})units.Add(new OfflineScenarioUnit(owner,PlayableEntityKind.Tank,new NavPoint(sign*x,z),owner==1?0:Math.PI));
                units.Add(new OfflineScenarioUnit(owner,PlayableEntityKind.Hawk,new NavPoint(sign*15,10),owner==1?0:Math.PI));
            }
            scenario=units.ToArray();
            return new OfflineMatchConfiguration(p,"native-flat-small-nav-unblock-v1","flat-small-two-sides-v1","real-flat-nav-runtime",Seed,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)},starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,62},{62,0}},scenario:scenario);
        }
        private static object Cleanup(PlayableRuntime runtime,UnityHostRouteService host,PlayableDomain domain)
        {
            if(runtime==null)return null;domain=domain??Domain(runtime);domain.CombatStepForQa=null;
            var worker=host==null?null:(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);
            runtime.RequestStop();host?.Dispose();runtime.Dispose();
            Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000),"actual runtime and route worker stop ACK");
            domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();var budget=domain.Navigation.PhysicalHostBudget;
            Assert.Zero(budget.PhysicalBytes,"physical owner census after shutdown");Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            return new{RuntimeStopped=runtime.IsStopped,WorkerStopped=worker==null||worker.IsStopped,budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes};
        }
        [UnityTest,Timeout(90000)]
        public IEnumerator SmallFlatCommandCombatAndCurrentRestore()=>RunSmallFlat();
        [UnityTest,Timeout(90000)]
        public IEnumerator SmallDefaultFlatCommandCombatAndCurrentRestore()=>RunSmallFlat(normalStartup:true);
        [UnityTest,Timeout(120000)]
        public IEnumerator NormalAuthoredStartupMovementAndCurrentRestore()=>RunNormalAuthored();
        [UnityTest,Timeout(120000)]
        public IEnumerator NormalAuthoredLegacyLabStartupMovementAndCurrentRestore()=>RunNormalAuthored(legacyLab:true);
        private IEnumerator RunNormalAuthored(bool legacyLab=false)
        {
            var parent=Environment.GetEnvironmentVariable("AI_NAV_UNBLOCK_OUTPUT")??Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/ai-navigation-runtime-unblock"));var output=Path.Combine(parent,legacyLab?"normal-authored-legacy-lab":"normal-authored");Assert.False(Directory.Exists(output),"Preserve prior witness");Directory.CreateDirectory(output);Assert.True(NativeSilentStartup.IsSilent);
            // SAME resource/map loader and lobby constructor as ordinary startup.
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=Seed};
            var baseline=(PlayableProfile)typeof(PlayableBootstrap).GetMethod("LoadNormalProfile",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);var balancePath=Path.Combine(output,"native-balance.json");byte[] legacyHistory=null;
            if(legacyLab){var oldData=baseline.CopyData();oldData.groundRouteBackend=0;var old=PlayableProfile.Create(oldData,baseline.AuthoredMap);var previous=new NativeBalanceStore(old,balancePath);Assert.True(previous.HasActive,previous.Problem);previous.Copy("Legacy startup custom terms");var custom=previous.Selected.CopyData();custom.tankCreditCost+=10;previous.Save(custom,previous.SelectedAudio);Assert.False(previous.Active.UsesRecastGroundRoutes);legacyHistory=File.ReadAllBytes(balancePath);File.WriteAllBytes(Path.Combine(output,"legacy-history-before.bin"),legacyHistory);}
            var store=NativeBalanceStore.ForApplication(baseline,balancePath);Assert.True(store.HasActive,store.Problem);if(legacyLab){CollectionAssert.AreEqual(legacyHistory,File.ReadAllBytes(balancePath),"ordinary startup rewrote historical Lab versions");Assert.True(store.Active.UsesRecastGroundRoutes);}
            var profile=(PlayableProfile)typeof(PlayableBootstrap).GetMethod("ComposeLobbyProfile",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{store.Active,setup});Assert.True(profile.UsesRecastGroundRoutes);Assert.IsInstanceOf<ThreeCrossingsMap>(profile.AuthoredMap);var map=(ThreeCrossingsMap)profile.AuthoredMap;
            TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));Assert.IsNotNull(TestMatchLogSession.CurrentDirectory);
            PlayableRuntime runtime=null;UnityHostRouteService host=null;PlayableDomain domain=null;OfflineMatchConfiguration configuration=null;object firstCensus=null,lastCensus=null;bool complete=false;string phase="startup";
            var frames=new ConcurrentQueue<Frame>();var observed=new List<Frame>();var errors=new ConcurrentQueue<string>();var origins=new Dictionary<int,NavPoint>();var aiOwners=new HashSet<PlayableOwner>();PlayableOwner humanOwner=default;
            long issued=-1,savedTick=-1,firstHuman=-1,firstAi=-1,firstDamage=-1;int pauseForSave=1;byte[] bytes=null;string firstRouteFailure=null;double deadline=Time.realtimeSinceStartupAsDouble+90;ulong nativeBefore=NativeAllocations();
            Body[] Read()=>domain.Snapshot(0,RuntimeStatus.Running,false,null,null,Seed).Entities.Select(e=>new Body{Id=e.Id,Owner=e.Owner,Kind=e.Kind,Position=e.Position,Health=e.ExactHealth??e.Health,Heading=e.HullHeading,Height=e.Altitude,Outcome=e.NavigationOutcome,Installed=domain.Navigation.Crowd.TryGet(e.Id,out var actor)?actor.InstalledRequest:0}).OrderBy(e=>e.Id).ToArray();
            IEnumerator Until(Func<bool> ready,string name){while(!ready()&&Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);Assert.IsNull(runtime.Latest?.Failure,name);Assert.IsNull(runtime.JournalFailure);yield return null;}Assert.True(ready(),name);}
            void Drain(){while(frames.TryDequeue(out var f))observed.Add(f);}
            void Observe(){try{
                if(frames.Count+observed.Count>Horizon+64)throw new InvalidOperationException("Bounded normal observer capacity exceeded");
                var bodies=Read();foreach(var b in bodies){if(!origins.TryGetValue(b.Id,out var birth)){origins.Add(b.Id,b.Position);birth=b.Position;}if(b.Installed>0&&!b.Position.Equals(birth)){if(b.Owner==humanOwner&&firstHuman<0)firstHuman=domain.Tick;if(aiOwners.Contains(b.Owner)&&firstAi<0)firstAi=domain.Tick;}}
                frames.Enqueue(new Frame{Tick=domain.Tick,Bodies=bodies});long start=Interlocked.Read(ref issued);if(start>=0&&(domain.Tick-start>=Horizon||Volatile.Read(ref pauseForSave)!=0&&domain.Tick-start>=SaveAfter))runtime.RequestPause(true);
            }catch(Exception e){errors.Enqueue(e.ToString());runtime.RequestPause(true);}}
            try{
                runtime=PlayableRuntime.CreateLobbyMatch(profile,Generation,setup,startPaused:true,aiProfile:store.ActiveAiProfile);host=new UnityHostRouteService();yield return Until(()=>runtime.IsPrepared&&runtime.Latest?.Paused==true,"ordinary authored preclock startup");Assert.Zero(runtime.Latest.Tick);
                configuration=(OfflineMatchConfiguration)typeof(PlayableRuntime).GetField("offlineConfiguration",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);Assert.IsNotNull(configuration);domain=Domain(runtime);
                // One detached value at the existing owner callback, no query,
                // source mutation, extra pause or per-quantum I/O.
                domain.Navigation.RouteFailed+=(q,outcome)=>{if(firstRouteFailure!=null)return;firstRouteFailure=Newtonsoft.Json.JsonConvert.SerializeObject(new{Tick=domain.Tick,Outcome=outcome.ToString(),q.Session,q.Request,q.Entity,q.Order,q.StartLocation,q.GoalLocation,q.HeldIdentity,q.MemberIdentity,Profile=new{q.Profile.ProfileId,q.Profile.Revision,Values=(double[])typeof(NavigationProfile).GetMethod("CaptureWireValues",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(q.Profile,null)},Geometry=new{q.Geometry.Revision,q.Geometry.HalfExtent,Obstacles=q.Geometry.Obstacles.ToArray()},BaseGeometry=new{q.BaseGeometry.Revision,q.BaseGeometry.HalfExtent,Obstacles=q.BaseGeometry.Obstacles.ToArray()}},Newtonsoft.Json.Formatting.Indented);};
                Assert.AreSame(map,configuration.Profile.AuthoredMap);Assert.AreSame(map,domain.RoutingProfile.AuthoredMap);Assert.True(domain.Navigation.UsesRecastGroundRoutes);CollectionAssert.AreEqual(WorldWire.Binding(profile),WorldWire.Binding(configuration.Profile));CollectionAssert.AreEqual(WorldWire.Binding(profile),WorldWire.Binding(domain.RoutingProfile));
                var human=configuration.Roster.Single(p=>p.Control==OfflineControl.Human);humanOwner=domain.OwnerFor(human.Id);foreach(var participant in configuration.Roster.Where(p=>p.Control==OfflineControl.Ai))aiOwners.Add(domain.OwnerFor(participant.Id));Assert.IsNotEmpty(aiOwners);
                var initial=Read();foreach(var b in initial)origins.Add(b.Id,b.Position);var own=initial.First(b=>b.Owner==humanOwner&&b.Kind==PlayableEntityKind.Explorer);var opponent=configuration.Roster.First(p=>p.Control==OfflineControl.Ai);var target=configuration.Starts.Single(s=>s.PinnedLogicalPlayer==opponent.LogicalPlayer).ExplorerAnchor;
                domain.CombatStepForQa=Observe;runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"ordinary authored resume");Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,1,human.Id,PlayableCommandKind.Move,new[]{own.Id},target)).Accepted);
                yield return Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.OwnerId==human.Id&&r.Sequence==1&&r.Status!=PlayableCommandStatus.Accepted),"ordinary human Move receipt");var receipt=runtime.ReceiptsAfter(0).Last(r=>r.OwnerId==human.Id&&r.Sequence==1);Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);Interlocked.Exchange(ref issued,receipt.AppliedTick);
                phase="active current";yield return Until(()=>runtime.Latest.Paused&&domain.Tick-issued>=SaveAfter||!errors.IsEmpty,"ordinary active current prefix");Drain();Assert.IsEmpty(errors);
                var capture=runtime.RequestCaptureBytes();yield return Until(()=>capture.IsCompleted,"ordinary current capture");Assert.False(capture.IsFaulted,capture.Exception?.ToString());bytes=capture.Result;using(var state=PlayableWorldState.Decode(bytes)){savedTick=state.Tick;Assert.AreEqual(configuration.SourceIdentity,state.SourceIdentity);Assert.AreEqual(Seed,state.Seed);}File.WriteAllBytes(Path.Combine(output,"normal-current.world"),bytes);
                firstCensus=Cleanup(runtime,host,domain);runtime=null;host=null;domain=null;
                runtime=PlayableRuntime.RestoreBytes(bytes,configuration,aiProfile:store.ActiveAiProfile);host=new UnityHostRouteService();yield return Until(()=>runtime.IsPrepared&&runtime.Latest?.Paused==true,"ordinary current restore preclock");domain=Domain(runtime);Assert.AreEqual(savedTick,runtime.Latest.Tick);Assert.AreEqual(map.Id,domain.RoutingProfile.AuthoredMap.Id);
                var recapture=runtime.RequestCaptureBytes();yield return Until(()=>recapture.IsCompleted,"ordinary current recapture");Assert.False(recapture.IsFaulted,recapture.Exception?.ToString());ExactCurrentGameState(bytes,recapture.Result);
                phase="restored ordinary match";Volatile.Write(ref pauseForSave,0);domain.CombatStepForQa=Observe;runtime.RequestPause(false);yield return Until(()=>runtime.Latest.Paused&&domain.Tick-issued>=Horizon||!errors.IsEmpty,"ordinary fixed small horizon");Drain();Assert.IsEmpty(errors);
                Assert.GreaterOrEqual(firstHuman,issued,"real normal human command movement");Assert.GreaterOrEqual(firstAi,0,"real normal AI owning-route movement");Assert.Greater(host.CertifiedPublication.RecastLongQueries,0);Assert.True(observed.All(f=>f.Bodies.Where(b=>b.Kind!=PlayableEntityKind.Hawk).All(b=>b.Outcome!=NavigationOutcome.Blocked&&b.Outcome!=NavigationOutcome.Rejected&&b.Outcome!=NavigationOutcome.Unreachable)),"ordinary functional navigation failure");
                if(legacyLab)CollectionAssert.AreEqual(legacyHistory,File.ReadAllBytes(balancePath),"match rewrote historical Lab versions");complete=true;phase="complete";
            }finally{Drain();try{if(runtime!=null)lastCensus=Cleanup(runtime,host,domain);}finally{TestMatchLogSession.Shutdown();if(firstRouteFailure!=null)File.WriteAllText(Path.Combine(output,"first-route-failure.json"),firstRouteFailure);File.WriteAllText(Path.Combine(output,"frames.json"),Newtonsoft.Json.JsonConvert.SerializeObject(observed));File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Result=complete?"PASS":"FAIL",phase,LegacyLabStartup=legacyLab,Map=map.Id,map.Revision,profile.ProfileId,ProfileRevision=profile.Revision,Backend=profile.UsesRecastGroundRoutes,Seed,Generation,Horizon,SaveAfter,Issued=issued,SavedTick=savedTick,FirstHumanMotion=firstHuman,FirstAiMotion=firstAi,FirstDamage=firstDamage,Combat="Not a combat acceptance gate; selected flat20 combat and Micro consumer qualification remain separate",Frames=observed.Count,ObserverErrors=errors.ToArray(),FirstCensus=firstCensus,LastCensus=lastCensus,NativeBefore=nativeBefore,NativeAfter=NativeAllocations(),ReleaseFaults=ReleaseFaults()},Newtonsoft.Json.Formatting.Indented));}}
            Assert.AreEqual(nativeBefore,NativeAllocations());Assert.Zero(ReleaseFaults());
        }
        private IEnumerator RunSmallFlat(bool normalStartup=false)
        {
            string output=Environment.GetEnvironmentVariable("AI_NAV_UNBLOCK_OUTPUT")??Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/ai-navigation-runtime-unblock/small-flat"));if(normalStartup)output=Path.Combine(output,"normal-startup");Directory.CreateDirectory(output);
            Assert.True(NativeSilentStartup.IsSilent);
            PlayableProfile profile;
            if(normalStartup){Assert.True(PlayableProfile.Default.UsesRecastGroundRoutes,"Default API must use the selected primary backend");var asset=Resources.Load<TextAsset>("PlayableProfile");Assert.IsNotNull(asset);profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(asset.text));Assert.True(profile.UsesRecastGroundRoutes,"Normal resource startup must choose Recast before Domain creation");}
            else{var data=PlayableProfile.Default.CopyData();data.groundRouteBackend=1;profile=PlayableProfile.Create(data);}Assert.IsNull(profile.AuthoredMap);
            var configuration=Configuration(profile,out var scenario);var frames=new ConcurrentQueue<Frame>();var observed=new List<Frame>();var errors=new ConcurrentQueue<string>();var failures=new List<string>();
            TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));Assert.IsNotNull(TestMatchLogSession.CurrentDirectory);
            PlayableRuntime runtime=null;UnityHostRouteService host=null;PlayableDomain domain=null;int[] ids=Array.Empty<int>();long issued=-1,savedTick=-1;int pauseForSave=1;bool complete=false;string phase="startup";object firstCensus=null,lastCensus=null;
            byte[] saved=null;double deadline=Time.realtimeSinceStartupAsDouble+60;long firstGroundMotion=-1,firstAirMotion=-1,firstDamage=-1;
            ulong nativeBefore=NativeAllocations();
            IEnumerator Until(Func<bool> done,string name){while(!done()&&Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);Assert.IsNull(runtime.Latest?.Failure,name);Assert.IsNull(runtime.JournalFailure);yield return null;}Assert.True(done(),name);}
            void Drain(){while(frames.TryDequeue(out var frame))observed.Add(frame);}
            Body[] Read(){var snapshot=domain.Snapshot(0,RuntimeStatus.Running,false,null,null,Seed);return snapshot.Entities.Where(e=>ids.Contains(e.Id)).Select(e=>new Body{Id=e.Id,Owner=e.Owner,Kind=e.Kind,Position=e.Position,Health=e.ExactHealth??e.Health,Heading=e.HullHeading,Height=e.Altitude,Outcome=e.NavigationOutcome,Installed=domain.Navigation.Crowd.TryGet(e.Id,out var actor)?actor.InstalledRequest:0}).OrderBy(e=>e.Id).ToArray();}
            void Observe(){try{
                if(frames.Count+observed.Count>Horizon+64){errors.Enqueue("bounded observer capacity exceeded");runtime.RequestPause(true);return;}
                frames.Enqueue(new Frame{Tick=domain.Tick,Bodies=Read()});long start=Interlocked.Read(ref issued);
                if(start>=0&&(domain.Tick-start>=Horizon||Volatile.Read(ref pauseForSave)!=0&&domain.Tick-start>=SaveAfter))runtime.RequestPause(true);
            }catch(Exception error){errors.Enqueue(error.ToString());runtime.RequestPause(true);}}
            try{
                runtime=new PlayableRuntime(configuration,Generation,startPaused:true);host=new UnityHostRouteService();yield return Until(()=>runtime.IsPrepared&&runtime.Latest?.Paused==true&&host.CertifiedPublication?.RecastFoundationBuilds==1,"flat startup/preclock foundation and paused ACK");
                Assert.Zero(runtime.Latest.Tick);Assert.True(runtime.Latest.Paused);domain=Domain(runtime);Assert.IsNull(domain.Navigation.Admission.AuthoredTerrain);Assert.True(domain.Navigation.UsesRecastGroundRoutes);CollectionAssert.AreEqual(WorldWire.Binding(profile),WorldWire.Binding(configuration.Profile));
                var all=runtime.ParticipantView("player-1").Entities.Concat(runtime.ParticipantView("player-2").Entities).GroupBy(e=>e.Id).Select(g=>g.First()).ToArray();
                ids=all.Where(e=>scenario.Any(s=>s.Kind==e.Kind&&s.Position.Equals(e.Position))).Select(e=>e.Id).OrderBy(id=>id).ToArray();Assert.AreEqual(20,ids.Length);
                foreach(var owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy})Assert.AreEqual(10,all.Count(e=>ids.Contains(e.Id)&&e.Owner==owner));
                var initial=Read();domain.CombatStepForQa=Observe;runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"resume command ingress");
                foreach(var owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy}){
                    int sequence=owner==PlayableOwner.Player?1:2;string ownerId=sequence==1?"player-1":"player-2";var group=initial.Where(e=>e.Owner==owner).Select(e=>e.Id).ToArray();
                    Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,sequence,ownerId,PlayableCommandKind.AttackMove,group,new NavPoint(sequence==1?7:-7,0))).Accepted);
                }
                yield return Until(()=>runtime.ReceiptsAfter(0).Count(r=>r.Sequence>=1&&r.Sequence<=2&&r.Status!=PlayableCommandStatus.Accepted)==2,"both ordinary command receipts");
                var receipts=runtime.ReceiptsAfter(0).Where(r=>r.Sequence>=1&&r.Sequence<=2&&r.Status!=PlayableCommandStatus.Accepted).ToArray();foreach(var receipt in receipts)Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);Interlocked.Exchange(ref issued,receipts.Max(r=>r.AppliedTick));
                phase="current world save";yield return Until(()=>runtime.Latest.Paused&&domain.Tick-issued>=SaveAfter,"fixed active save prefix");Drain();Assert.IsEmpty(errors);
                var prefix=Read();Assert.True(prefix.Any(e=>e.Kind==PlayableEntityKind.Tank&&e.Installed>0));
                var capture=runtime.RequestCaptureBytes();yield return Until(()=>capture.IsCompleted,"current world capture");Assert.IsFalse(capture.IsFaulted,capture.Exception?.ToString());saved=capture.Result;
                using(var envelope=PlayableWorldState.Decode(saved)){Assert.AreEqual(PlayableWorldState.CurrentVersion,envelope.Version);Assert.AreEqual(Seed,envelope.Seed);Assert.AreEqual(Generation,envelope.Generation);Assert.AreEqual(configuration.SourceIdentity,envelope.SourceIdentity);savedTick=envelope.Tick;}
                File.WriteAllBytes(Path.Combine(output,"small-current.world"),saved);firstCensus=Cleanup(runtime,host,domain);runtime=null;host=null;domain=null;
                phase="restore preclock";runtime=PlayableRuntime.RestoreBytes(saved,configuration);host=new UnityHostRouteService();yield return Until(()=>runtime.IsPrepared&&runtime.Latest?.Paused==true&&host.CertifiedPublication?.RecastFoundationBuilds==1,"flat restored native preparation and paused ACK");domain=Domain(runtime);
                Assert.AreEqual(savedTick,runtime.Latest.Tick);Assert.True(runtime.Latest.Paused);var restored=Read();Assert.AreEqual(prefix.Length,restored.Length);
                foreach(var expected in prefix){var actual=restored.Single(e=>e.Id==expected.Id);Assert.AreEqual(expected.Position,actual.Position);Assert.AreEqual(expected.Health,actual.Health);Assert.AreEqual(expected.Heading,actual.Heading);Assert.AreEqual(expected.Height,actual.Height);Assert.AreEqual(expected.Installed,actual.Installed);}
                var recapture=runtime.RequestCaptureBytes();yield return Until(()=>recapture.IsCompleted,"immediate current recapture");Assert.IsFalse(recapture.IsFaulted,recapture.Exception?.ToString());ExactCurrentGameState(saved,recapture.Result);
                Volatile.Write(ref pauseForSave,0);domain.CombatStepForQa=Observe;runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"resume restored battle");phase="restored battle";
                yield return Until(()=>runtime.Latest.Paused&&domain.Tick-issued>=Horizon||!errors.IsEmpty,"fixed small battle horizon");Drain();Assert.IsEmpty(errors);
                foreach(var frame in observed)foreach(var body in frame.Bodies){var origin=initial.Single(e=>e.Id==body.Id);if(!body.Position.Equals(origin.Position)){if(body.Kind==PlayableEntityKind.Hawk&&firstAirMotion<0)firstAirMotion=frame.Tick;else if(body.Kind==PlayableEntityKind.Tank&&firstGroundMotion<0)firstGroundMotion=frame.Tick;}if(body.Health<origin.Health&&firstDamage<0)firstDamage=frame.Tick;if(body.Kind==PlayableEntityKind.Tank&&(body.Outcome==NavigationOutcome.Blocked||body.Outcome==NavigationOutcome.Rejected||body.Outcome==NavigationOutcome.Unreachable))failures.Add("functional movement failure "+body.Id+"@"+frame.Tick+":"+body.Outcome);}
                Assert.IsEmpty(failures);Assert.GreaterOrEqual(firstGroundMotion,issued);Assert.GreaterOrEqual(firstAirMotion,issued);Assert.GreaterOrEqual(firstDamage,issued,"actual combat health effect");
                Assert.True(observed.Any(f=>f.Bodies.Any(e=>e.Owner==PlayableOwner.Player&&!e.Position.Equals(initial.Single(a=>a.Id==e.Id).Position))));Assert.True(observed.Any(f=>f.Bodies.Any(e=>e.Owner==PlayableOwner.Enemy&&!e.Position.Equals(initial.Single(a=>a.Id==e.Id).Position))));
                complete=true;phase="complete";
            }finally{
                Drain();try{if(runtime!=null)lastCensus=Cleanup(runtime,host,domain);}finally{TestMatchLogSession.Shutdown();File.WriteAllText(Path.Combine(output,"frames.json"),Newtonsoft.Json.JsonConvert.SerializeObject(observed));File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Result=complete?"PASS":"FAIL",phase,Seed,Generation,Horizon,SaveAfter,Ids=ids,Issued=issued,SavedTick=savedTick,FirstGroundMotion=firstGroundMotion,FirstAirMotion=firstAirMotion,FirstDamage=firstDamage,Failures=failures,ObserverErrors=errors.ToArray(),FirstCensus=firstCensus,LastCensus=lastCensus,NativeBefore=nativeBefore,NativeAfter=NativeAllocations(),ReleaseFaults=ReleaseFaults()},Newtonsoft.Json.Formatting.Indented));}
            }
            Assert.AreEqual(nativeBefore,NativeAllocations());Assert.Zero(ReleaseFaults());
        }
    }
}
