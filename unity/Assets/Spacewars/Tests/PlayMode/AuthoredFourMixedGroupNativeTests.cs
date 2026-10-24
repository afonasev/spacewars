using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class AuthoredFourMixedGroupNativeTests
    {
        private const long Generation=71;
        private const int Seed=19092026,Horizon=2700;
        private static readonly NavPoint Goal=new NavPoint(22,0);
        private static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private sealed class Row
        {
            public int Id;public PlayableEntityKind Kind;public NavPoint Position;public NavLocation Location;
            public double Height,Heading,Radius,Speed,Turn;public bool Moving,Held,CanYield,TypedValid;
            public NavigationOutcome Outcome;public long Group,Revision,Order,Incarnation,Mobility;
            public long RootId,RootCommand,RootRevision,RootGeneration,RootMembership;public string RootOwner,RootKind,RootOrigin,Phase,ContactPhase;
            public NavPoint RootGoal;public int[] Members;
        }
        private sealed class Frame {public long Tick;public Row[] Rows;}
        private static double DistanceSquared(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return x*x+z*z;}
        private static string Hash(byte[] bytes){using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        private static void Save(string folder,string name,object value)=>System.IO.File.WriteAllText(System.IO.Path.Combine(folder,name),Newtonsoft.Json.JsonConvert.SerializeObject(value,Newtonsoft.Json.Formatting.Indented));

        [UnityTest,Timeout(180000)]
        public IEnumerator AuthoredCentralBridgeFourMixedUnitsKeepOneMoveAndNaturallyArrive()
            =>RunAuthoredCentralBridgeFourMixed(PlayableProfile.ThreeCrossingsDefault);

        [UnityTest,Timeout(180000)]
        public IEnumerator CertifiedCorridorFourMixedUnitsKeepOneMoveAndNaturallyArrive()
        {
            var profile=PlayableProfile.CertifiedGroupCorridorDefault;
            Assert.AreEqual(15,profile.Navigation.GroupCorridorMaximumExcessPercent);
            return RunAuthoredCentralBridgeFourMixed(profile);
        }
        private IEnumerator RunAuthoredCentralBridgeFourMixed(PlayableProfile profile)
        {
            string managedOutput=Environment.GetEnvironmentVariable("B12_AUTHORED_FOUR_OUTPUT");
            string folder=System.IO.Path.GetFullPath(managedOutput??System.IO.Path.Combine(Application.dataPath,"..","..",".local","test-results","authored-four-"+Guid.NewGuid().ToString("N")));
            Assert.False(System.IO.Directory.Exists(folder)&&System.IO.Directory.EnumerateFileSystemEntries(folder).Any(),"owned evidence output must start empty");System.IO.Directory.CreateDirectory(folder);
            string producer=Environment.GetEnvironmentVariable("B12_AUTHORED_FOUR_PRODUCER");if(managedOutput!=null||producer!=null)Assert.True(producer!=null&&producer.Length==40&&producer.All(Uri.IsHexDigit));
            string producerBinding=producer==null?"Unavailable/unbound: producer environment not supplied; aggregate runner may bind source externally":"Supplied 40-hex producer";
            TestContext.WriteLine("AUTHORED_FOUR_EVIDENCE path="+folder+" producerBinding="+producerBinding);
            var map=(ThreeCrossingsMap)profile.AuthoredMap;
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=Seed};setup.InitializeParticipants();foreach(var participant in setup.Participants)participant.Human=true;
            var authored=NativeLobbyMatch.Create(profile,setup);Assert.AreEqual(2,authored.Roster.Count);Assert.True(authored.Roster.All(p=>p.Control==OfflineControl.Human));
            // Supported genesis: automatic owner1 Explorer supplies one of the four;
            // its authored start anchor is configured before construction, never teleported.
            var autoExplorer=new NavPoint(-20,-8);int assigned=authored.Assignments[0];
            var starts=authored.Starts.Select((s,i)=>new OfflineStart(s.Id,s.SiteId,s.Position,i==assigned?autoExplorer:s.ExplorerAnchor,s.Heading,s.PinnedLogicalPlayer)).ToArray();
            int player=authored.Roster[0].LogicalPlayer;
            var scenario=new[]{new OfflineScenarioUnit(player,PlayableEntityKind.Tank,new NavPoint(-24,-8)),new OfflineScenarioUnit(player,PlayableEntityKind.Tank,new NavPoint(-24,8)),new OfflineScenarioUnit(player,PlayableEntityKind.Explorer,new NavPoint(-20,8))};
            var costs=new double[starts.Length,starts.Length];for(int a=0;a<starts.Length;a++)for(int b=0;b<starts.Length;b++)costs[a,b]=authored.RouteCost(a,b);
            var config=new OfflineMatchConfiguration(profile,"authored-four-mixed-central-witness-v1",authored.MapIdentity,authored.RouteProvenance,Seed,authored.Roster.ToArray(),starts,authored.Sites.ToArray(),authored.Obstacles.ToArray(),costs,authored.Spectators.ToArray(),scenario,map.Id,authored.ScenarioBuildings.ToArray());
            var origins=new[]{autoExplorer}.Concat(scenario.Select(u=>u.Position)).ToArray();double largest=Math.Max(profile.TankCollisionRadius,profile.ExplorerCollisionRadius);
            var bridge=map.Supports.Single(s=>s.SurfaceId=="bridge/central");var west=map.Supports.Single(s=>s.SurfaceId=="bank/west");var east=map.Supports.Single(s=>s.SurfaceId=="bank/east");
            Assert.Less(bridge.Bounds.MaxZ-bridge.Bounds.MinZ,west.Bounds.MaxZ-west.Bounds.MinZ);Assert.Less(bridge.Bounds.MaxZ-bridge.Bounds.MinZ,east.Bounds.MaxZ-east.Bounds.MinZ);
            Assert.Greater(origins.Max(p=>p.Z)-origins.Min(p=>p.Z),map.CentralBridgeWidth,"initial group is wider than the actual bridge");
            var events=new ConcurrentQueue<Frame>();var observerErrors=new ConcurrentQueue<string>();var observed=new List<Frame>(Horizon+32);var checks=new List<string>();
            PlayableRuntime runtime=null;UnityHostRouteService host=null;PlayableDomain domain=null;object preflight=null;int[] ids=Array.Empty<int>();long commandTick=-1,firstMotion=-1,rootId=0;bool entered=false,arrived=false,submissionAttempted=false,submissionAccepted=false,commandApplied=false;object admission=null;int services=0,capturedCount=0;string captureStatus="NotRun",status="PRECOMMAND";
            double deadline=Time.realtimeSinceStartupAsDouble+180;
            IEnumerator Until(Func<bool> ready,string phase){while(!ready()&&Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);services++;Assert.IsNull(runtime.Latest?.Failure,phase);Assert.False(runtime.IsStopped,phase);yield return null;}Assert.True(ready(),phase);}
            void Drain(){while(events.TryDequeue(out var frame)){if(observed.Count>=Horizon+32){observerErrors.Enqueue("bounded frame buffer exceeded");break;}observed.Add(frame);}}
            try{
                runtime=new PlayableRuntime(config,Generation,startPaused:true);host=new UnityHostRouteService();yield return Until(()=>runtime.IsPrepared&&runtime.Latest!=null&&runtime.Latest.Sequence>0,"supported authored genesis startup");
                var initial=runtime.Latest.Entities;var selected=initial.Where(e=>e.Owner==PlayableOwner.Player).OrderBy(e=>e.Id).ToArray();ids=selected.Select(e=>e.Id).ToArray();
                Assert.AreEqual(4,ids.Length);Assert.AreEqual(2,selected.Count(e=>e.Kind==PlayableEntityKind.Tank));Assert.AreEqual(2,selected.Count(e=>e.Kind==PlayableEntityKind.Explorer));CollectionAssert.AreEquivalent(origins,selected.Select(e=>e.Position));
                domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",Fields).GetValue(runtime);var nav=domain.Navigation;var geometry=runtime.NavigationGeometry;Assert.AreEqual(5,nav.Crowd.Units.Count,"only four same-owner movers plus the other owner's automatic Explorer");
                Assert.LessOrEqual(profile.NavigationRequestsPerFrame,8);Assert.True(map.TryLocate(Goal,largest,out var target));Assert.AreEqual("bank/east",target.SurfaceId);
                var pathFacts=new List<object>();foreach(var origin in origins){Assert.True(geometry.IsFree(origin,largest));Assert.True(map.TryLocate(origin,largest,out var from));Assert.AreEqual("bank/west",from.SurfaceId);
                    var path=new[]{origin,new NavPoint(-14,0),new NavPoint(14,0),Goal};for(int leg=1;leg<path.Length;leg++){Assert.True(geometry.SegmentFree(path[leg-1],path[leg],largest),"preflight largest-footprint route corridor");Assert.True(map.SupportsSweep(path[leg-1],path[leg],largest));}pathFacts.Add(new{Origin=origin,From=from,Goal=target,GeometryOnlyReachabilityPolyline=path});}
                foreach(var actor in nav.Crowd.Units){Assert.True(map.IsValidLocation(actor.Location,actor.Radius));Assert.AreNotEqual("flat",actor.Location.SurfaceId);foreach(var peer in nav.Crowd.Units.Where(u=>u.Id<actor.Id))Assert.GreaterOrEqual(DistanceSquared(actor.Position,peer.Position),Math.Pow(actor.Radius+peer.Radius,2));}
                // Read complete authority-owned genesis metadata while paused, including fogged bodies.
                var genesisCapture=runtime.RequestCaptureBytes();yield return Until(()=>genesisCapture.IsCompleted,"paused genesis metadata capture");Assert.AreEqual(System.Threading.Tasks.TaskStatus.RanToCompletion,genesisCapture.Status,genesisCapture.Exception?.ToString());
                var genesisBytes=genesisCapture.Result;System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,"genesis-current.bin"),genesisBytes);
                using(var envelope=PlayableWorldState.Decode(genesisBytes)){Assert.AreEqual(11,envelope.Version);Assert.AreEqual(PlayableWorldState.CurrentVersion,envelope.Version);Assert.AreEqual(Seed,envelope.Seed);Assert.AreEqual(Generation,envelope.Generation);Assert.AreEqual(config.SourceIdentity,envelope.SourceIdentity);CollectionAssert.AreEqual(WorldWire.Binding(profile),envelope.Binding);CollectionAssert.AreEqual(OfflineConfigurationWire.Binding(config,profile),envelope.Participants);}
                var authorityUnits=(IDictionary)typeof(PlayableDomain).GetField("units",Fields).GetValue(domain);
                var kinds=new Dictionary<int,PlayableEntityKind>();foreach(DictionaryEntry entry in authorityUnits){var unit=entry.Value;int id=(int)unit.GetType().GetField("Id",Fields).GetValue(unit);Assert.AreEqual((int)entry.Key,id);kinds.Add(id,(PlayableEntityKind)unit.GetType().GetField("Kind",Fields).GetValue(unit));}
                CollectionAssert.AreEquivalent(nav.Crowd.Units.Select(a=>a.Id),kinds.Keys,"complete actual authority genesis unit-kind coverage");
                foreach(var actor in nav.Crowd.Units){Assert.Greater(actor.Incarnation,0);Assert.AreEqual(PlayableUnitRules.Radius(profile,kinds[actor.Id]),actor.Radius,"actual kind/profile radius binding");}
                foreach(var entity in selected)Assert.AreEqual(entity.Kind,kinds[entity.Id],"owner-visible selected kind matches authority");
                var metadata=new{Source="PlayableDomain.units IDictionary entries: actual private Unit.Id/Kind, read paused before observer/resume/Move; no visibility inference",CaptureSHA256=Hash(genesisBytes),Version=11,ProfileSHA=Hash(WorldWire.Binding(profile)),ConfigurationSHA=Hash(OfflineConfigurationWire.Binding(config,profile)),Units=nav.Crowd.Units.Select(a=>new{a.Id,Kind=kinds[a.Id],a.Incarnation,a.Radius}).ToArray()};
                preflight=new{SupportedConstructorAccepted=true,AuthorityMetadata=metadata,Producer=producer,ProducerBinding=producerBinding,Seed,Generation,CurrentVersion=PlayableWorldState.CurrentVersion,Profile=new{profile.ProfileId,profile.Revision,BindingSHA=Hash(WorldWire.Binding(profile))},Map=new{map.Id,map.Revision,map.SurfaceSemanticsVersion,map.CentralBridgeWidth,map.CentralBridgeHalfSpan,BridgeSurface=bridge.SurfaceId,BridgeBounds=bridge.Bounds,WestBounds=west.Bounds,EastBounds=east.Bounds},ConfigurationBindingSHA=Hash(OfflineConfigurationWire.Binding(config,profile)),LargestFootprint=largest,Scenario=scenario,AutomaticPlayerExplorerAnchor=autoExplorer,ActualUnits=initial.Select(e=>new{e.Id,e.Owner,e.Kind,e.Position,e.Location}),SelectedIds=ids,Reachability=pathFacts,Contract=new{Horizon,NUnitTimeoutMilliseconds=180000,NoSyncPump=true,OneHumanMove=true,NoReissue=true},Interpretation="Polyline certifies pre-command free geometry only; actual route is solved by the real threaded host. Support transition narrows/widens available map region; no invented internal formation state."};
                var incarnations=nav.Crowd.Units.ToDictionary(a=>a.Id,a=>a.Incarnation);var speeds=nav.Crowd.Units.ToDictionary(a=>a.Id,a=>a.Speed);var turns=nav.Crowd.Units.ToDictionary(a=>a.Id,a=>a.TurnSpeed);var roles=nav.Crowd.Units.ToDictionary(a=>a.Id,a=>a.CanYield);Row[] BuildRows()=>nav.Crowd.Units.Select(actor=>{var group=nav.GroupFor(actor.Id);var member=group?.Members.SingleOrDefault(m=>m.Entity==actor.Id);return new Row{Id=actor.Id,Kind=kinds[actor.Id],Position=actor.Position,Location=actor.Location,Height=map.SurfaceHeight(actor.Position),Heading=actor.Heading,Radius=actor.Radius,Speed=actor.Speed,Turn=actor.TurnSpeed,Moving=actor.Moving,Held=actor.Held,CanYield=actor.CanYield,TypedValid=map.IsValidLocation(actor.Location,actor.Radius),Outcome=actor.Outcome,Group=actor.InstalledGroupId,Revision=actor.InstalledRevision,Order=actor.InstalledOrder,Incarnation=actor.Incarnation,Mobility=actor.MobilityRevision,RootId=group?.GroupId??0,RootCommand=group?.CommandSequence??0,RootRevision=group?.OrderRevision??0,RootGeneration=group?.WorldGeneration??0,RootOwner=group?.OwnerId,RootKind=group?.Kind.ToString(),RootOrigin=group?.Origin.ToString(),RootMembership=group?.MembershipRevision??0,RootGoal=group?.OriginalGoal??default,Phase=member?.Phase.ToString(),ContactPhase=member?.ContactPhase,Members=group?.Members.Select(m=>m.Entity).ToArray()};}).ToArray();
                var dryRows=BuildRows();CollectionAssert.AreEquivalent(kinds.Keys,dryRows.Select(r=>r.Id),"paused observer dry build covers all real bodies; not an authority frame");
                domain.CombatStepForQa=()=>{try{
                    if(System.Threading.Interlocked.Increment(ref capturedCount)>Horizon+32){observerErrors.Enqueue("bounded authority buffer exhausted");runtime.RequestPause(true);return;}
                    events.Enqueue(new Frame{Tick=domain.Tick,Rows=BuildRows()});
                    long issued=System.Threading.Interlocked.Read(ref commandTick);if(issued>=0&&domain.Tick-issued>=Horizon)runtime.RequestPause(true);
                }catch(Exception error){observerErrors.Enqueue(error.ToString());runtime.RequestPause(true);}};
                runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"resume");
                submissionAttempted=true;status="SUBMISSION_ATTEMPTED";var submit=runtime.TrySubmit(new PlayableCommand(Generation,1,config.Roster[0].Id,PlayableCommandKind.Move,ids,Goal));admission=new{submit.Accepted,Status=submit.Status.ToString(),Detail="TrySubmit exposes Status only; no additional refusal message"};submissionAccepted=submit.Accepted;Save(folder,"admission.json",admission);Assert.True(submit.Accepted,Newtonsoft.Json.JsonConvert.SerializeObject(admission));status="SUBMISSION_ACCEPTED";
                yield return Until(()=>runtime.ReceiptsAfter(0).Any(receipt=>receipt.Sequence==1&&receipt.Status!=PlayableCommandStatus.Accepted),"one actual human Move receipt");var receipt=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==1&&r.Status!=PlayableCommandStatus.Accepted);Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);commandApplied=true;entered=true;status="COMMAND_APPLIED";System.Threading.Interlocked.Exchange(ref commandTick,receipt.AppliedTick);
                int checkedFrames=0;Frame previous=null;
                void Check(){Drain();while(checkedFrames<observed.Count){var frame=observed[checkedFrames++];if(!frame.Rows.Select(r=>r.Id).OrderBy(v=>v).SequenceEqual(kinds.Keys.OrderBy(v=>v)))checks.Add("missing/duplicate actual body at "+frame.Tick);if(previous!=null&&frame.Tick!=previous.Tick+1)checks.Add("authority tick gap at "+frame.Tick);foreach(var row in frame.Rows){
                    if(row.Incarnation!=incarnations[row.Id]||row.Speed!=speeds[row.Id]||row.Turn!=turns[row.Id]||row.CanYield!=roles[row.Id])checks.Add("immutable incarnation/speed/turn/role "+row.Id);
                    if(!row.TypedValid||row.Location.SurfaceId=="flat"||!geometry.IsFree(row.Position,row.Radius))checks.Add("typed/static footprint "+row.Id+"@"+frame.Tick);
                    if(previous!=null){var old=previous.Rows.Single(r=>r.Id==row.Id);double dx=row.Position.X-old.Position.X,dz=row.Position.Z-old.Position.Z;if(dx*dx+dz*dz>row.Speed*row.Speed/900d+1e-9)checks.Add("speed "+row.Id);double turn=Math.Abs(NavAngle(row.Heading-old.Heading));if(turn>row.Turn/30d+1e-9)checks.Add("turn "+row.Id);if(!geometry.SegmentFree(old.Position,row.Position,row.Radius)||!map.SupportsSweep(old.Position,row.Position,row.Radius)||!map.TryTraverse(old.Location,row.Position,row.Radius,out var typed)||!typed.Equals(row.Location))checks.Add("full typed/static sweep "+row.Id);
                        foreach(var peer in frame.Rows.Where(r=>r.Id<row.Id)){var oldPeer=previous.Rows.Single(r=>r.Id==peer.Id);double sx=old.Position.X-oldPeer.Position.X,sz=old.Position.Z-oldPeer.Position.Z,vx=dx-(peer.Position.X-oldPeer.Position.X),vz=dz-(peer.Position.Z-oldPeer.Position.Z),q=vx*vx+vz*vz,t=q==0?0:Math.Max(0,Math.Min(1,-(sx*vx+sz*vz)/q)),x=sx+vx*t,z=sz+vz*t;if(x*x+z*z<Math.Pow(row.Radius+peer.Radius,2)-1e-8)checks.Add("continuous body sweep "+row.Id+"/"+peer.Id);}
                        if(ids.Contains(row.Id)&&DistanceSquared(row.Position,old.Position)>1e-12&&firstMotion<0)firstMotion=frame.Tick;
                    }
                    if(ids.Contains(row.Id)&&row.RootCommand==1){if(rootId==0)rootId=row.RootId;if(row.RootGeneration!=Generation||row.RootRevision!=1||row.RootMembership!=1||row.RootKind!="Move"||row.RootOrigin!="Human"||row.RootOwner!=config.Roster[0].Id||!row.RootGoal.Equals(Goal)||row.Members==null||!row.Members.OrderBy(v=>v).SequenceEqual(ids.OrderBy(v=>v)))checks.Add("original root binding "+row.Id);}
                    if(ids.Contains(row.Id)&&row.Group>0&&(row.Group!=rootId||row.Revision!=1||row.Order!=1||row.Mobility!=0||row.Held))checks.Add("installed identity/mobility/role "+row.Id);
                    if(ids.Contains(row.Id)&&(row.Outcome==NavigationOutcome.Blocked||row.Outcome==NavigationOutcome.Unreachable||row.Outcome==NavigationOutcome.Rejected))checks.Add("terminal failure "+row.Id);
                }previous=frame;}}
                while(Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);services++;Check();Assert.IsNull(runtime.Latest.Failure);var latest=observed.LastOrDefault();arrived=latest!=null&&latest.Rows.Where(r=>ids.Contains(r.Id)).All(r=>r.Outcome==NavigationOutcome.Arrived);if(arrived||checks.Count>0||!observerErrors.IsEmpty||(latest!=null&&latest.Tick-commandTick>=Horizon))break;yield return new WaitForSecondsRealtime(.001f);}
                runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,"pause terminal evidence");Check();
                var capture=runtime.RequestCaptureBytes();yield return Until(()=>capture.IsCompleted,"one terminal current capture");Assert.AreEqual(System.Threading.Tasks.TaskStatus.RanToCompletion,capture.Status,capture.Exception?.ToString());var bytes=capture.Result;System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,"terminal-current.bin"),bytes);
                using(var envelope=PlayableWorldState.Decode(bytes)){Assert.AreEqual(11,envelope.Version);Assert.AreEqual(PlayableWorldState.CurrentVersion,envelope.Version);Assert.AreEqual(Seed,envelope.Seed);Assert.AreEqual(Generation,envelope.Generation);Assert.AreEqual(config.SourceIdentity,envelope.SourceIdentity);CollectionAssert.AreEqual(WorldWire.Binding(profile),envelope.Binding);CollectionAssert.AreEqual(OfflineConfigurationWire.Binding(config,profile),envelope.Participants);captureStatus="current11/typed-authored/profile-config-bound/sha="+Hash(bytes);}
                Save(folder,"capture.json",new{Producer=producer,Bytes=bytes.Length,SHA256=Hash(bytes),CurrentVersion=11,Tick=runtime.Latest.Tick,CaptureStatus=captureStatus,Profile=Hash(WorldWire.Binding(profile)),Configuration=Hash(OfflineConfigurationWire.Binding(config,profile))});
                Assert.IsEmpty(observerErrors);Assert.IsEmpty(checks);Assert.True(arrived,"all4 natural Arrived inside2700command-relative ticks");Assert.GreaterOrEqual(firstMotion,commandTick);Assert.Greater(rootId,0);
                foreach(int id in ids){var frames=observed.Where(f=>f.Tick>=commandTick).Select(f=>new{f.Tick,Row=f.Rows.Single(r=>r.Id==id)}).ToArray();long bridgeTick=frames.Where(f=>f.Row.Location.SurfaceId=="bridge/central").Select(f=>f.Tick).DefaultIfEmpty(-1).Min();Assert.Greater(bridgeTick,commandTick,"actual typed central bridge crossing "+id);Assert.True(frames.Any(f=>f.Tick>bridgeTick&&f.Row.Location.SurfaceId=="bank/east"&&f.Row.Position.X>bridge.Bounds.MaxX+f.Row.Radius),"actual exit into widening target bank "+id);}
                status="ORDINARY_FOUR_MIXED_PASS";
            }finally{
                Drain();if(domain!=null)domain.CombatStepForQa=null;
                Save(folder,"preflight.json",preflight??new{Qualified=false,Note="genesis/pre-command preflight failed"});Save(folder,"authority-frames.json",observed);
                Save(folder,"result.json",new{Status=status,Producer=producer,ProducerBinding=producerBinding,EnteredActualCommand=entered,SubmissionAttempted=submissionAttempted,SubmissionAccepted=submissionAccepted,CommandApplied=commandApplied,Admission=admission,CommandTick=commandTick,FirstMotion=firstMotion,All4Arrived=arrived,RootId=rootId,LastTick=observed.LastOrDefault()?.Tick,Frames=observed.Count,Services=services,Checks=checks,ObserverErrors=observerErrors.ToArray(),CaptureStatus=captureStatus,MemberPhases=observed.SelectMany(f=>f.Rows.Where(r=>ids.Contains(r.Id))).Select(r=>r.Phase).Distinct().ToArray(),ContactPhases=observed.SelectMany(f=>f.Rows.Where(r=>ids.Contains(r.Id))).Select(r=>r.ContactPhase).Distinct().ToArray(),FormationNarrowingWidening="NotProven: no explicit narrowing/widening authority state exposed; actual bank->centralBridge->bank transitions and per-frame group widths/positions are retained. Do not infer formation state from final arrival.",HeightCoverage="Actual support heights/typed identities saved per authority frame; this central bank/bridge path may remain height0, not a height-transition qualification.",Horizon,NoReissue=true});
                runtime?.RequestStop();host?.Dispose();runtime?.Dispose();
            }
        }
        private static double NavAngle(double value){while(value>Math.PI)value-=2*Math.PI;while(value< -Math.PI)value+=2*Math.PI;return value;}
    }
}
