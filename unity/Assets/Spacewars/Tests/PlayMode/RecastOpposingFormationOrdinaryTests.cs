using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed partial class RecastFormationOrdinaryTests
    {
        // Separate Phase B recipe. These bounds do not change the original A50.
        const int OpposingPreHorizon=2700,OpposingMainHorizon=5400,OpposingCapacity=8100+128;
        const double OpposingStartupWall=180,OpposingPreWall=180,OpposingMainWall=360;
        sealed class OpposingFrame {public int Phase;public Frame[] Groups;public object[] Queues;}
        // A separate PARTIAL observation; the ordinary acceptance entry retains null.
        sealed class PairedPeerDiagnostic
        {
            static readonly Type Motion=typeof(NavUnit).Assembly.GetType("Spacewars.Simulation.NavCrowdMotion",true);
            static readonly System.Reflection.BindingFlags StaticHidden=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
            static readonly System.Reflection.FieldInfo PeerActor=Motion.GetField("CausalPeerActor",StaticHidden),Causal=Motion.GetField("Causal",StaticHidden);
            readonly Func<bool> matches;internal readonly int Actor,Peer;
            internal PairedPeerDiagnostic(int actor=14,int peer=10,bool requireLocal=true){Actor=actor;Peer=peer;matches=BuildMatch(actor,peer,requireLocal);}
            int previousPublications;bool bound;internal bool OwnerThreadFlagReleased;
            internal bool Latched;internal object Pair,Receipt;internal long Tick;internal int PublicationBefore,PublicationAfter;
            static System.Linq.Expressions.Expression Field(System.Linq.Expressions.Expression value,string path){foreach(string name in path.Split('.'))value=System.Linq.Expressions.Expression.Field(value,name);return value;}
            static Func<bool> BuildMatch(int focusId=14,int peer=10,bool requireLocal=true)
            {
                var c=System.Linq.Expressions.Expression.Field(null,Causal);var checks=new List<System.Linq.Expressions.Expression>();
                void Yes(string path)=>checks.Add(Field(c,path));
                void No(string path)=>checks.Add(System.Linq.Expressions.Expression.Not(Field(c,path)));
                void Equal(string path,int n)=>checks.Add(System.Linq.Expressions.Expression.Equal(Field(c,path),System.Linq.Expressions.Expression.Constant(n)));
                void Point(string left,string right,bool equal){var l=Field(c,left);var r=Field(c,right);var both=System.Linq.Expressions.Expression.AndAlso(System.Linq.Expressions.Expression.Equal(Field(l,"X"),Field(r,"X")),System.Linq.Expressions.Expression.Equal(Field(l,"Z"),Field(r,"Z")));checks.Add(equal?both:System.Linq.Expressions.Expression.Not(both));}
                void Zero(string path){checks.Add(System.Linq.Expressions.Expression.Equal(Field(c,path+".X"),System.Linq.Expressions.Expression.Constant(0d)));checks.Add(System.Linq.Expressions.Expression.Equal(Field(c,path+".Z"),System.Linq.Expressions.Expression.Constant(0d)));}
                foreach(string actor in new[]{"FocusContext","Peer"}){Yes(actor+".SelectedSeen");Yes(actor+".PreFilterSeen");Yes(actor+".FinalSeen");Yes(actor+".PreparedSeen");Yes(actor+".TypedSeen");}
                Yes("SelectedSeen");Yes("FinalSeen");Yes("Body.Active");Yes("Body.OwnMoving");No("Body.Yielding");Equal("Body.Id",focusId);Equal("Body.YieldLabel",0);
                if(requireLocal)checks.Add(System.Linq.Expressions.Expression.LessThan(Field(c,"Body.OwnLocalIndex"),Field(c,"FocusContext.LocalLength")));
                Point("Selected.Position","Body.Position",false);checks.Add(System.Linq.Expressions.Expression.OrElse(System.Linq.Expressions.Expression.NotEqual(Field(c,"Selected.Velocity.X"),System.Linq.Expressions.Expression.Constant(0d)),System.Linq.Expressions.Expression.NotEqual(Field(c,"Selected.Velocity.Z"),System.Linq.Expressions.Expression.Constant(0d))));Zero("Final.Velocity");Point("Final.Position","Body.Position",true);Yes("Final.JointRefused");No("Final.Overflow");
                checks.Add(System.Linq.Expressions.Expression.GreaterThan(Field(c,"ConflictCount"),System.Linq.Expressions.Expression.Constant(0)));Equal("Last.Actor",focusId);Equal("Last.Peer",peer);
                Equal("Peer.Body.Id",peer);Zero("Peer.Final.Velocity");Point("Peer.Final.Position","Peer.Body.Position",true);
                return System.Linq.Expressions.Expression.Lambda<Func<bool>>(checks.Aggregate((a,b)=>System.Linq.Expressions.Expression.AndAlso(a,b))).Compile();
            }
            static object Value(object o,string name)=>o.GetType().GetProperty(name)?.GetValue(o)??o.GetType().GetField(name)?.GetValue(o)??throw new MissingMemberException(name);
            static object Detach(object o){var t=o.GetType();if(t.IsPrimitive||t.IsEnum||t==typeof(string))return o;return t.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).ToDictionary(f=>f.Name,f=>Detach(f.GetValue(o)));}
            internal void Read(PlayableDomain domain,bool main)
            {
                var n=domain.Navigation;int now=n.OwnedMovementPublications;
                if(!bound){if((int)PeerActor.GetValue(null)!=0)throw new InvalidOperationException("Foreign owner-thread paired tracking is active");PeerActor.SetValue(null,Peer);bound=true;previousPublications=now;return;}
                if(!Latched&&main&&matches()){
                    var frame=Causal.GetValue(null);var context=Value(frame,"FocusContext");var receipt=typeof(NavigationSession).GetField("MovementDelayRecastReceipt",Hidden).GetValue(n);
                    Pair=Detach(frame);Receipt=receipt;Tick=domain.Tick;PublicationBefore=previousPublications;PublicationAfter=now;
                    if(n.LastNavigationStepPath!=2||now!=previousPublications+1||receipt==null||(int)Value(receipt,"Version")!=2||!(bool)Value(receipt,"Committed")||
                        (long)Value(receipt,"WorldGeneration")!=(long)Value(context,"World")||(long)Value(receipt,"RequestSequence")!=(long)Value(context,"Request")||
                        (long)Value(Value(receipt,"Causal"),"ComputeSerial")!=(long)Value(frame,"ComputeSerial"))throw new InvalidOperationException("Paired-peer committed publication mismatch");
                    Latched=true;ReleaseOnOwnerThread();
                }
                previousPublications=now;
            }
            // Called only from the runtime callback, never main-thread cleanup.
            internal void ReleaseOnOwnerThread(){if(bound&&(int)PeerActor.GetValue(null)==Peer){PeerActor.SetValue(null,0);bound=false;OwnerThreadFlagReleased=true;}}
            internal static void Validate()
            {
                Assert.IsNotNull(BuildMatch());Assert.IsNotNull(BuildMatch(24,17,false));Assert.AreEqual(typeof(int),PeerActor.FieldType);
                void ValuesOnly(Type t){if(t.IsPrimitive||t.IsEnum)return;Assert.True(t.IsValueType,"No owner/array/reference in paired values: "+t);foreach(var f in t.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))ValuesOnly(f.FieldType);}
                ValuesOnly(Causal.FieldType);Assert.AreEqual(0,(int)PeerActor.GetValue(null),"paired capture defaults off");
                Assert.That(ValidatePipelineMetadata(),Is.Not.Empty);Assert.That(ValidateApproachMetadata(),Is.Not.Empty);
            }
        }
        [Test] public void PairedPeerDiagnosticMetadataKeepsValueOnlyDisabledSeam()=>PairedPeerDiagnostic.Validate();
        [UnityTest,Timeout(780000)]
        public IEnumerator OpposingFiftyPlusFiftyPairedPeerPartialDiagnostic()=>RunOpposing(new PairedPeerDiagnostic());
        [UnityTest,Timeout(780000)]
        public IEnumerator OpposingExitPocketActualPairAndCurrent()=>RunOpposing(new PairedPeerDiagnostic(24,17,false));
        [UnityTest,Timeout(480000)]
        public IEnumerator OpposingActualPhysicalRefusalCurrentBeforeTimeout()=>RunOpposing(null,1800,false,true);
        [UnityTest,Timeout(780000)]
        public IEnumerator OpposingFiftyPlusFiftyNaturallyPassNorth()=>RunOpposing(null);
        // Cost observation only: same normal ingress, a bounded main window, no acceptance gates.
        [UnityTest,Timeout(480000)]
        public IEnumerator OpposingFiftyPlusFiftyDenseCostWindow()=>RunOpposing(null,1200);
        [UnityTest,Timeout(480000)]
        public IEnumerator OpposingDenseDisplacedBankAndTerminalHandoffControl()=>RunOpposing(null,1800,true);
        private IEnumerator RunOpposing(PairedPeerDiagnostic paired,int diagnosticMainTicks=0,bool bankControl=false,bool beforeFailure=false)
        {
            string output=Environment.GetEnvironmentVariable("RECAST_FORMATION_OUTPUT")??throw new InvalidOperationException("Explicit unique output required");
            if(Directory.Exists(output))throw new InvalidOperationException("Preserve prior run; output exists");Directory.CreateDirectory(output);
            Assert.True(NativeSilentStartup.IsSilent);
            var p=PlayableProfile.RecastGroupCorridorDefault;var map=(ThreeCrossingsMap)p.AuthoredMap;
            var config=RecastOpposingFormationInputs.Create(p,out var rejected,out var roster);
            PlayableRuntime runtime=null;UnityHostRouteService host=null;PlayableDomain domain=null;LiveVideo video=null;
            var cleanupErrors=new ConcurrentQueue<string>();var queue=new ConcurrentQueue<OpposingFrame>();var frames=new List<OpposingFrame>();
            int[][] ids=null;HashSet<int>[] selected=null;int[] automatic=null;NavPoint[] preTargets=null,goals=null;NavPoint[][] expected=null;RecastOpposingFormationInputs.Packing packing=null;
            PipelineObservation[] pipelines=null;FormationObservation[] formations=null;ApproachObservation approach=null;PipelineObservation activeCapture=null;
            long count=0,lastTick=-1,preIssued=-1,mainIssued=-1;int observationPhase=0,activeRequested=0,activeSaved=0;bool pauseAck=false,complete=false;string phase="startup",testLogs=null;double deadline=Time.realtimeSinceStartupAsDouble+OpposingStartupWall;
            ulong native=NativeCount();long faults=ReleaseFaults();CleanupReceipt census=null;CertifiedRoutePublication lastHost=null;long preQueries=-1;PlayableSnapshot birth=null,terminal=null;string[] metadata=null;
            var receipts=new List<PlayableCommandReceipt>();var observerErrors=new ConcurrentQueue<string>();
            var firstInstalled=new Dictionary<int,long>();var firstMotion=new Dictionary<int,long>();var arrivals=new Dictionary<int,long>();var visited=new HashSet<int>();var assigned=new Dictionary<int,Body>();
            var initialSpan=new double[2];var bridgeSpan=new double[2];var bridgeCount=new int[2];
            var safetyCounts=new Dictionary<string,int>();var safetySamples=new List<string>();long safety=0;string safetyAnalysis="NotRun";
            // Opt-in same-run attribution only; no per-tick I/O or authoritative state.
            bool denseCost=diagnosticMainTicks>0;bool costBegan=false,costEnded=false;
            bool observerCost=Environment.GetEnvironmentVariable("RECAST_FORMATION_OBSERVER_COST")=="1";
            long observerCostCount=0,observerCostTicks=0,observerCostMax=0;
            var wait=new GuardAwait(name=>{host.Service(runtime,8);Assert.IsNull(runtime.Latest?.Failure,name);},()=>Time.realtimeSinceStartupAsDouble);
            IEnumerator Until(Func<bool> done,string name)=>wait.Until(done,name,deadline,phase);
            void Drain(){while(queue.TryDequeue(out var frame))frames.Add(frame);}
            Frame ReadGroup(int index){var n=domain.Navigation;var g=n.GroupFor(ids[index][0]);bool all=g!=null&&g.Members.Length==50&&g.Members.All(m=>m.HasGoal&&m.GoalLocation.HasValue);
                return new Frame{Tick=domain.Tick,Group=g?.GroupId??0,Revision=g?.OrderRevision??0,OriginalGoal=g?.OriginalGoal??default,FormationPhase=g?.FormationPlan?.Phase.ToString(),Bodies=n.Crowd.Units.Select(a=>{var member=g?.Members.SingleOrDefault(m=>m.Entity==a.Id);bool has=member!=null&&member.HasGoal&&member.GoalLocation.HasValue;return new Body{Id=a.Id,Position=a.Position,Location=a.Location,Goal=a.Goal,Assigned=has?member.GoalLocation.Value.Position:a.Goal,Radius=a.Radius,Speed=a.Speed,Selected=selected[index].Contains(a.Id),Moving=a.Moving,Outcome=a.Outcome,Installed=member!=null&&a.InstalledGroupId==g.GroupId&&a.InstalledRevision==member.OrderRevision?a.InstalledRequest:0,Slot=member?.Slot??-1,HasSlot=has,AllSlots=all,Progress=member?.Recast??default,Spine=Spine(a)};}).OrderBy(a=>a.Id).ToArray()};
            }
            void Observe(){long observerBegan=observerCost?System.Diagnostics.Stopwatch.GetTimestamp():0;try{
                if(Interlocked.Increment(ref count)>OpposingCapacity){observerErrors.Enqueue("bounded frame capacity exceeded");paired?.ReleaseOnOwnerThread();runtime.RequestPause(true);return;}
                int current=Volatile.Read(ref observationPhase);var groups=new[]{ReadGroup(0),ReadGroup(1)};Interlocked.Exchange(ref lastTick,domain.Tick);
                var queues=new object[2];
                for(int i=0;i<2;i++){var group=domain.Navigation.GroupFor(ids[i][0]);
                    if(current==2&&group?.Members.Length==50){formations[i].Read(group,domain.Tick);pipelines[i].Read(domain,groups[i]);}
                    queues[i]=new{Tick=domain.Tick,Group=i,GroupIdentity=group?.GroupId,IssuedTick=group?.IssuedTick,Age=group==null?(long?)null:domain.Tick-group.IssuedTick,Phase=group?.FormationPlan?.Phase.ToString(),Installed=groups[i].Bodies.Count(b=>b.Selected&&b.Installed>0),Moving=groups[i].Bodies.Count(b=>b.Selected&&b.Moving),Arrived=groups[i].Bodies.Count(b=>b.Selected&&b.Outcome==NavigationOutcome.Arrived),Entries=group?.FormationPlan?.Entries.Select(e=>new{e.Entity,e.EntryOrdinal,e.Lane,Stage=e.Stage.ToString()}).ToArray(),WaitMeaning="Stage/current group age plus physical stationary intervals from frames; no inferred transient policy reason"};
                }
                paired?.Read(domain,current==2);approach?.Read(domain,groups[0],beforeFailure&&current==2);queue.Enqueue(new OpposingFrame{Phase=current,Groups=groups,Queues=queues});
                if(current==1&&Interlocked.Read(ref preIssued)>=0&&(domain.Tick-preIssued>=OpposingPreHorizon||automatic.All(id=>domain.Navigation.Crowd.Units.Single(a=>a.Id==id).Outcome==NavigationOutcome.Arrived)))runtime.RequestPause(true);
                if(current==2){
                    if(denseCost&&Interlocked.Read(ref mainIssued)>=0){
                        if(!costBegan){PerformanceProfile.Flush();Console.WriteLine("DENSE_COST_BEGIN tick="+domain.Tick+" mainIssued="+mainIssued);costBegan=true;}
                        if((domain.Tick-mainIssued>=diagnosticMainTicks||beforeFailure&&approach.NearFailureActor!=0)&&!costEnded){PerformanceProfile.Flush();Console.WriteLine("DENSE_COST_END tick="+domain.Tick+" mainIssued="+mainIssued);costEnded=true;runtime.RequestPause(true);}
                    }
                    if(paired!=null){
                        if(groups.Any(g=>g.Bodies.Any(b=>b.Selected&&(b.Outcome==NavigationOutcome.Blocked||b.Outcome==NavigationOutcome.Rejected||b.Outcome==NavigationOutcome.Unreachable)))){observerErrors.Enqueue("partial diagnostic functional terminal failure");paired.ReleaseOnOwnerThread();runtime.RequestPause(true);}
                        if(paired.Latched)runtime.RequestPause(true);
                    }
                    if(Interlocked.Read(ref mainIssued)>=0&&(domain.Tick-mainIssued>=OpposingMainHorizon||groups.All(g=>g.Bodies.Where(b=>b.Selected).All(b=>b.Outcome==NavigationOutcome.Arrived))))runtime.RequestPause(true);
                    bool bothAccepted=groups.All(g=>g.Bodies.Count(b=>b.Selected&&b.Installed>0)==50)&&formations.All(f=>f.MappingRows==50);
                    bool anyActive=groups.Any(g=>(g.FormationPhase=="Feeding"||g.FormationPhase=="Recovering")&&g.Bodies.Any(b=>b.Selected&&b.Installed>0&&b.Moving));
                    if(paired==null&&bothAccepted&&anyActive&&Interlocked.CompareExchange(ref activeRequested,1,0)==0)runtime.RequestPause(true);
                }
                if(domain.Outcome!=PlayableMatchOutcome.Playing){observerErrors.Enqueue("normal match ended");paired?.ReleaseOnOwnerThread();runtime.RequestPause(true);}
            }catch(Exception e){observerErrors.Enqueue(e.ToString());paired?.ReleaseOnOwnerThread();runtime.RequestPause(true);}finally{if(observerCost){long elapsed=System.Diagnostics.Stopwatch.GetTimestamp()-observerBegan;Interlocked.Increment(ref observerCostCount);Interlocked.Add(ref observerCostTicks,elapsed);observerCostMax=Math.Max(observerCostMax,elapsed);}}}
            PlayableCommandReceipt Receipt(string owner,long sequence)=>runtime.ReceiptsAfter(0).SingleOrDefault(r=>r.OwnerId==owner&&r.Sequence==sequence&&r.Status!=PlayableCommandStatus.Accepted);
            void Submit(string owner,long sequence,int[] members,NavPoint goal){Assert.True(map.TryLocate(goal,p.ExplorerCollisionRadius,out var location));Assert.True(runtime.TrySubmit(new PlayableCommand(RecastFormationInputs.Generation,sequence,owner,PlayableCommandKind.Move,members,goal).WithResolvedLocation(location)).Accepted);}
            void Capture(PipelineObservation capture,string folder){bool capturePaused=runtime.Latest.Paused;Assert.True(capturePaused,"capture requires an acknowledged runtime pause");Directory.CreateDirectory(folder);capture.CaptureTerminal(domain.Navigation,capturePaused,()=>runtime.IsStopRequested);Assert.IsNull(capture.SnapshotError);Assert.True(capture.SnapshotStatus.StartsWith("CAPTURED:"),capture.SnapshotStatus);capture.ExportCapture(folder);File.WriteAllText(Path.Combine(folder,"producer.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Candidate=Environment.GetEnvironmentVariable("RECAST_FORMATION_CANDIDATE"),Tick=domain.Tick,Map=map.Id,map.Revision,Profile=p.ProfileId,ProfileRevision=p.Revision,ProfileBinding=Binding(p),PausedACK=capturePaused,Status=capture.SnapshotStatus,Groups=ids.Select(group=>{var g=domain.Navigation.GroupFor(group[0]);return new{GroupId=g?.GroupId,IssuedTick=g?.IssuedTick,OrderRevision=g?.OrderRevision,Seal=g?.FormationPlan?.Seal,Phase=g?.FormationPlan?.Phase.ToString(),Moving=domain.Navigation.Crowd.Units.Count(a=>group.Contains(a.Id)&&a.Moving)};}).ToArray()}));}
            void Fail(string kind,string detail){safety++;safetyCounts.TryGetValue(kind,out int old);safetyCounts[kind]=old+1;if(safetySamples.Count<128)safetySamples.Add(kind+":"+detail);}
            try{
                metadata=ValidatePipelineMetadata().Concat(ValidateApproachMetadata()).ToArray();TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));testLogs=TestMatchLogSession.CurrentDirectory;Assert.IsNotNull(testLogs);
                runtime=new PlayableRuntime(config,RecastFormationInputs.Generation,startPaused:true);host=new UnityHostRouteService();yield return Until(()=>runtime.IsPrepared&&runtime.Latest.Paused&&host.CertifiedPublication?.RecastFoundationBuilds==1,"normal startup");wait.Rethrow();
                domain=Domain(runtime);Assert.Zero(domain.Tick);birth=domain.Snapshot(0,RuntimeStatus.Starting,true,runtime.Latest.Metrics,null,config.Seed);
                ids=new[]{PlayableOwner.Player,PlayableOwner.Enemy}.Select(owner=>birth.Entities.Where(a=>a.Owner==owner&&a.Kind==PlayableEntityKind.Explorer).Select(a=>a.Id).OrderBy(id=>id).ToArray()).ToArray();selected=ids.Select(x=>new HashSet<int>(x)).ToArray();automatic=new[]{ids[0][0],ids[1][0]};
                Assert.True(ids.All(x=>x.Length==50));Assert.AreEqual(101,birth.Entities.Count);Assert.AreEqual(101,domain.Navigation.Crowd.Units.Count);Assert.True(config.Roster.All(r=>r.Control==OfflineControl.Human));
                foreach(string owner in new[]{"player-1","player-2"}){var pop=runtime.ParticipantView(owner).Population;Assert.AreEqual(100,pop.Living);Assert.Zero(pop.Reserved);Assert.AreEqual(100,pop.Capacity);}
                var parked=birth.Entities.Single(a=>!ids.SelectMany(x=>x).Contains(a.Id));packing=RecastOpposingFormationInputs.Pack(p,config,domain.Geometry,domain.Navigation,ids,automatic[1],automatic[0],parked.Position);Assert.True(packing.Found);
                goals=new[]{packing.WestGoal,packing.EastGoal};expected=new[]{packing.WestSlots,packing.EastSlots};preTargets=new[]{packing.EastPreposition,packing.Preposition};
                Assert.AreEqual(new NavPoint(-26.8,15),goals[0]);Assert.AreEqual(new NavPoint(30.4,15),goals[1]);Assert.AreEqual(new NavPoint(34,58.2),preTargets[0]);Assert.AreEqual(new NavPoint(-34,58.2),preTargets[1]);
                formations=new[]{new FormationObservation(OpposingMainHorizon),new FormationObservation(OpposingMainHorizon)};
                pipelines=new[]{new PipelineObservation(OpposingCapacity,ids[0].Last(),ids[0][0]),new PipelineObservation(OpposingCapacity,ids[1].Last(),ids[1][0])};foreach(var pipeline in pipelines)pipeline.Bind(host);
                approach=paired!=null?new ApproachObservation(p.Navigation.BlockedTimeoutSeconds,paired.Actor,false,OpposingCapacity,100):beforeFailure||Environment.GetEnvironmentVariable("RECAST_FORMATION_CAUSAL")=="1"?new ApproachObservation(p.Navigation.BlockedTimeoutSeconds,automatic[0],true,OpposingCapacity,100,beforeFailure?p.Navigation.RepairRetrySeconds:0):null;approach?.Bind(domain.Navigation);
                if(Environment.GetEnvironmentVariable("RECAST_FORMATION_VIDEO")=="1"){video=new LiveVideo(p,Path.Combine(output,"live-video"),ids.SelectMany(x=>x).ToArray());video.Read(runtime,true);}
                // Frame0 precedes all normal commands. No producer pose or execution edits.
                queue.Enqueue(new OpposingFrame{Phase=0,Groups=new[]{ReadGroup(0),ReadGroup(1)},Queues=Array.Empty<object>()});Assert.IsNull(domain.CombatStepForQa);domain.CombatStepForQa=Observe;
                phase="prephase";deadline=Time.realtimeSinceStartupAsDouble+OpposingPreWall;Volatile.Write(ref observationPhase,1);runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"prephase resume");wait.Rethrow();
                Submit("player-1",1,new[]{automatic[0]},preTargets[0]);Submit("player-2",1,new[]{automatic[1]},preTargets[1]);
                yield return Until(()=>Receipt("player-1",1)!=null&&Receipt("player-2",1)!=null,"two prephase receipts");wait.Rethrow();
                var preReceipts=new[]{Receipt("player-1",1),Receipt("player-2",1)};foreach(var receipt in preReceipts)Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);receipts.AddRange(preReceipts);Interlocked.Exchange(ref preIssued,preReceipts.Min(r=>r.AppliedTick));
                while(Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);Assert.IsNull(runtime.Latest.Failure);video?.Read(runtime);Drain();if(runtime.Latest.Paused||!observerErrors.IsEmpty)break;yield return null;}
                runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,"prephase terminal pause");wait.Rethrow();Drain();Assert.IsEmpty(observerErrors);pauseAck=true;
                for(int i=0;i<2;i++){var actor=domain.Navigation.Crowd.Units.Single(a=>a.Id==automatic[i]);Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome);Assert.AreEqual(i==0?"bank/east":"bank/west",actor.Location.SurfaceId);Assert.LessOrEqual(RecastFormationInputs.Distance(actor.Position,preTargets[i]),p.Navigation.ArrivalTolerance);}
                foreach(var actor in domain.Navigation.Crowd.Units.Where(a=>!automatic.Contains(a.Id)))Assert.AreEqual(birth.Entities.Single(a=>a.Id==actor.Id).Position,actor.Position,"prephase moved nonrecipient");
                for(int i=0;i<2;i++)CollectionAssert.AreEqual(expected[i],domain.Navigation.AllocateArrivalSlots(goals[i],ids[i]),"actual post-prephase normal allocation");
                preQueries=host.CertifiedPublication.RecastLongQueries;Assert.AreEqual(2,preQueries,"one native context per real single Move");
                var preCapture=new PipelineObservation();Capture(preCapture,Path.Combine(output,"prephase-current"));
                File.WriteAllText(Path.Combine(output,"prephase.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Receipts=preReceipts,Issued=preIssued,End=domain.Tick,Queries=preQueries,Actual=automatic.Select(id=>{var a=domain.Navigation.Crowd.Units.Single(x=>x.Id==id);return new{a.Id,a.Position,a.Location,a.Outcome};}).ToArray(),CurrentSlots=expected}));
                phase="main";deadline=Time.realtimeSinceStartupAsDouble+OpposingMainWall;Volatile.Write(ref observationPhase,2);runtime.RequestPause(false);pauseAck=false;yield return Until(()=>!runtime.Latest.Paused,"main resume");wait.Rethrow();
                Submit("player-1",2,ids[0],goals[0]);Submit("player-2",2,ids[1],goals[1]);
                yield return Until(()=>Receipt("player-1",2)!=null&&Receipt("player-2",2)!=null,"two main receipts");wait.Rethrow();var mainReceipts=new[]{Receipt("player-1",2),Receipt("player-2",2)};foreach(var receipt in mainReceipts)Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);receipts.AddRange(mainReceipts);Interlocked.Exchange(ref mainIssued,mainReceipts.Min(r=>r.AppliedTick));
                while(Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);Assert.IsNull(runtime.Latest.Failure);video?.Read(runtime);Drain();
                    if((paired!=null||denseCost&&Volatile.Read(ref costEnded))&&runtime.Latest.Paused)break;
                    if(runtime.Latest.Paused&&Volatile.Read(ref activeRequested)==1&&Volatile.Read(ref activeSaved)==0){activeCapture=new PipelineObservation();Capture(activeCapture,Path.Combine(output,"active-current"));Volatile.Write(ref activeSaved,1);var last=frames.LastOrDefault();if(last!=null&&last.Groups.All(g=>g.Bodies.Where(b=>b.Selected).All(b=>b.Outcome==NavigationOutcome.Arrived)))break;runtime.RequestPause(false);}
                    var frame=frames.LastOrDefault();if(frame!=null&&(frame.Groups.All(g=>g.Bodies.Where(b=>b.Selected).All(b=>b.Outcome==NavigationOutcome.Arrived))||frame.Groups[0].Tick-mainIssued>=OpposingMainHorizon)||!observerErrors.IsEmpty)break;yield return null;
                }
                runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,"main terminal pause");wait.Rethrow();pauseAck=runtime.Latest.Paused;Drain();Assert.IsEmpty(observerErrors);
                if(Environment.GetEnvironmentVariable("RECAST_FORMATION_PREPARATION_CAPTURE")=="1")foreach(var pipeline in pipelines){Assert.IsNull(pipeline.PreparationError);Assert.Zero(pipeline.PreparationDropped);Assert.True(pipeline.HasPreparationSource,"Actual fifty-member paid helper source not captured");}
                if(denseCost){Assert.True(costBegan&&costEnded,"bounded dense window not reached");Assert.True(pauseAck,"REAL pause ACK required");Assert.AreEqual(1,Volatile.Read(ref activeSaved),"actual both decisions/active capture required");Assert.True(frames.Last().Groups.All(g=>g.Bodies.Count(b=>b.Selected&&b.Installed>0)==50));if(bankControl)VerifyDenseBankControl(frames,ids,map,domain.Geometry,p,output);
                    if(beforeFailure){Assert.Greater(approach.NearFailureActor,0,"No actual physical refusal close to timeout captured");var refusalActor=domain.Navigation.Crowd.Units.Single(a=>a.Id==approach.NearFailureActor);Assert.True(refusalActor.Moving,"Actual capture must precede rejection/route clearing");File.WriteAllText(Path.Combine(output,"before-failure.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{approach.NearFailureActor,approach.NearFailureTick,ActualPausedTick=domain.Tick,refusalActor.Position,refusalActor.Heading,refusalActor.Outcome,refusalActor.InstalledRequest,Lead=p.Navigation.RepairRetrySeconds,ThresholdSeconds=p.Navigation.BlockedTimeoutSeconds-p.Navigation.RepairRetrySeconds,Meaning="Real callback physical refusal, pause ACK and full actual current before route clearing; partial diagnostic, not H1800 or arrival acceptance"}));}
                    phase=beforeFailure?"PARTIAL actual physical refusal current before timeout; fullH/allarrival NotRun":bankControl?"PARTIAL actual dense bank/core/handoff control; fullH/allarrival NotRun":"PARTIAL dense-cost observation; Safety/formation/fullH/arrival NotRun";complete=true;}
                else if(paired!=null){Assert.True(paired.Latched,"No qualifying paired-peer observation; no retry");Assert.True(pauseAck,"REAL pause ACK required");if(paired.Actor==24)Assert.True(frames.Last().Groups.All(g=>g.Bodies.Count(b=>b.Selected&&b.Installed>0)==50),"Actual both accepted group decisions required for exit-pocket current");phase="PARTIAL paired-peer committed observation";complete=true;}else{
                phase="physical and formation analysis";
                terminal=domain.Snapshot(0,RuntimeStatus.Running,true,runtime.Latest.Metrics,null,config.Seed);Assert.AreEqual(PlayableMatchOutcome.Playing,terminal.Outcome);Assert.AreEqual(101,terminal.Entities.Count);
                foreach(var actor in terminal.Entities){var initial=birth.Entities.Single(a=>a.Id==actor.Id);Assert.AreEqual(initial.ExactHealth,actor.ExactHealth,"combat damage");Assert.Zero(actor.TargetId,"unexpected combat target");}
                OpposingFrame previous=null;var stationary=new Dictionary<int,long>();var stable=new Dictionary<int,(long,long)>();
                safetyAnalysis="Incomplete";
                foreach(var frame in frames){var all=frame.Groups[0].Bodies;var previousBodies=previous?.Groups[0].Bodies.ToDictionary(b=>b.Id);
                    foreach(var body in all){if(!map.IsValidLocation(body.Location,body.Radius)||!runtime.NavigationGeometry.IsFree(body.Position,body.Radius))Fail("footprint",body.Id+"@"+frame.Groups[0].Tick);if(body.Id!=parked.Id&&!RecastOpposingFormationInputs.EnemySeparated(p,body.Position,parked.Position))Fail("enemy fixture contact",body.Id+"@"+frame.Groups[0].Tick);if(body.Id==parked.Id&&!body.Position.Equals(parked.Position))Fail("parked moved",body.Id.ToString());
                        if(previous==null)continue;var before=previousBodies[body.Id];double dx=body.Position.X-before.Position.X,dz=body.Position.Z-before.Position.Z;
                        if(dx*dx+dz*dz>body.Speed*body.Speed/900+1e-9)Fail("speed",body.Id+"@"+frame.Groups[0].Tick);
                        if(!runtime.NavigationGeometry.SegmentFree(before.Position,body.Position,body.Radius)||!map.TryTraverse(before.Location,body.Position,body.Radius,out var reached)||!reached.Equals(body.Location))Fail("typed sweep",body.Id+"@"+frame.Groups[0].Tick);
                        foreach(var peer in all.Where(b=>b.Id<body.Id)){var oldPeer=previousBodies[peer.Id];double sx=before.Position.X-oldPeer.Position.X,sz=before.Position.Z-oldPeer.Position.Z,vx=dx-(peer.Position.X-oldPeer.Position.X),vz=dz-(peer.Position.Z-oldPeer.Position.Z),q=vx*vx+vz*vz,t=q==0?0:Math.Max(0,Math.Min(1,-(sx*vx+sz*vz)/q));if(Math.Pow(sx+vx*t,2)+Math.Pow(sz+vz*t,2)<Math.Pow(body.Radius+peer.Radius,2)-1e-8)Fail("continuous pair",body.Id+"/"+peer.Id+"@"+frame.Groups[0].Tick);}
                    }
                    if(previous!=null&&frame.Groups[0].Tick!=previous.Groups[0].Tick+1)Fail("missing or duplicate tick",frame.Groups[0].Tick.ToString());
                    if(frame.Phase==2)for(int i=0;i<2;i++){var group=frame.Groups[i];if(!group.OriginalGoal.Equals(goals[i]))continue;
                        if(initialSpan[i]==0){var source=group.Bodies.Where(b=>b.Selected).ToArray();initialSpan[i]=source.Max(b=>b.Position.Z)-source.Min(b=>b.Position.Z);}
                        var north=group.Bodies.Where(b=>b.Selected&&b.Location.SurfaceId=="bridge/north").ToArray();bridgeCount[i]=Math.Max(bridgeCount[i],north.Length);if(north.Length>=2)bridgeSpan[i]=Math.Max(bridgeSpan[i],north.Max(b=>b.Position.Z)-north.Min(b=>b.Position.Z));
                        if(stable.TryGetValue(i,out var identity)){if(identity.Item1!=group.Group||identity.Item2!=group.Revision)Fail("group identity",i+"@"+group.Tick);}else stable.Add(i,(group.Group,group.Revision));
                        foreach(var body in group.Bodies.Where(b=>b.Selected)){
                            if(body.Moving&&!body.AllSlots)Fail("motion before slots",body.Id.ToString());if(body.Outcome==NavigationOutcome.Blocked||body.Outcome==NavigationOutcome.Rejected||body.Outcome==NavigationOutcome.Unreachable)Fail("terminal failure",body.Id+"="+body.Outcome);
                            if(body.HasSlot){if(assigned.TryGetValue(body.Id,out var old)){if(old.Slot!=body.Slot||!old.Assigned.Equals(body.Assigned))Fail("slot identity",body.Id.ToString());}else assigned.Add(body.Id,body);}
                            if(body.Installed>0&&!firstInstalled.ContainsKey(body.Id))firstInstalled.Add(body.Id,group.Tick);
                            var oldBody=previous?.Groups[i].Bodies.Single(b=>b.Id==body.Id);bool translated=oldBody!=null&&!body.Position.Equals(oldBody.Position);
                            if(translated&&!firstMotion.ContainsKey(body.Id))firstMotion.Add(body.Id,group.Tick);if(!translated){stationary.TryGetValue(body.Id,out long age);stationary[body.Id]=age+1;}
                            if(body.Location.SurfaceId=="bridge/north")visited.Add(body.Id);if(body.Installed>0&&body.Outcome==NavigationOutcome.Arrived&&!arrivals.ContainsKey(body.Id))arrivals.Add(body.Id,group.Tick);
                        }
                    }
                    previous=frame;
                }
                safetyAnalysis="Complete";
                File.WriteAllText(Path.Combine(output,"progress.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{FirstInstalled=firstInstalled,FirstMotion=firstMotion,Arrivals=arrivals,StationaryTicks=stationary,Meaning="Exact eachgroup queue entries/issuedage/phase/count/throughput are inframes.json; stationary is observed displacement, not inferred policy wait"}));
                Assert.Zero(safety,string.Join("\n",safetySamples));Assert.AreEqual(100,firstMotion.Count);Assert.AreEqual(100,arrivals.Count);Assert.AreEqual(100,assigned.Count);Assert.AreEqual(100,visited.Count,"ALL100 actual north bridge");Assert.AreEqual(1,Volatile.Read(ref activeSaved),"both accepted decisions/ages retained with at least one active formation");
                foreach(int i in new[]{0,1}){var final=frames.Last().Groups[i].Bodies.Where(b=>b.Selected).ToArray();foreach(var body in final){Assert.AreEqual(NavigationOutcome.Arrived,body.Outcome);Assert.AreEqual(i==0?"bank/west":"bank/east",body.Location.SurfaceId);Assert.LessOrEqual(RecastFormationInputs.Distance(body.Position,assigned[body.Id].Assigned),p.Navigation.ArrivalTolerance);Assert.IsNotNull(body.Spine);}
                    var bridge=map.Supports.Single(s=>s.SurfaceId=="bridge/north");double fit=bridge.Bounds.MaxZ-bridge.Bounds.MinZ-2*p.ExplorerCollisionRadius;
                    double finalSpan=final.Max(b=>b.Position.Z)-final.Min(b=>b.Position.Z),goalSpan=assigned.Values.Where(b=>selected[i].Contains(b.Id)).Max(b=>b.Assigned.Z)-assigned.Values.Where(b=>selected[i].Contains(b.Id)).Min(b=>b.Assigned.Z);
                    Assert.Greater(initialSpan[i],fit);Assert.GreaterOrEqual(bridgeCount[i],2);Assert.LessOrEqual(bridgeSpan[i],fit+1e-8);Assert.Greater(finalSpan,fit);Assert.GreaterOrEqual(finalSpan,goalSpan-2*p.Navigation.ArrivalTolerance-1e-8);
                    var f=formations[i];Assert.Zero(f.DroppedFrames);Assert.AreEqual(50,f.MappingRows);Assert.AreEqual(50,f.UniqueRanks);Assert.GreaterOrEqual(f.Lanes,2);Assert.True(f.SawCollecting&&f.SawFeeding&&f.SawRecovering);Assert.Greater(f.MaxAdmitted,0);Assert.Less(f.MaxAdmitted,50);Assert.Greater(f.MaxStaging,0);Assert.Greater(f.MaxRecovering,0);Assert.Zero(pipelines[i].Dropped);
                }
                Assert.Zero(approach?.DroppedRows??0);Assert.Zero(approach?.DroppedSnapshots??0);lastHost=host.CertifiedPublication;Assert.AreEqual(4,lastHost.RecastLongQueries,"two prephase single contexts + two homogeneousbank main contexts");complete=true;phase="complete";
                }
            }finally{
                var exit=runtime?.Latest;if(video!=null){Attempt(cleanupErrors,"last-video",()=>video.Read(runtime,true));Attempt(cleanupErrors,"video-dispose",video.Dispose);}
                var exports=new List<KeyValuePair<string,Action>>{
                    new KeyValuePair<string,Action>("frames",()=>{Drain();File.WriteAllText(Path.Combine(output,"frames.json"),Newtonsoft.Json.JsonConvert.SerializeObject(frames));}),
                    new KeyValuePair<string,Action>("approach",()=>approach?.Save(output,census?.Ack==true)),
                    new KeyValuePair<string,Action>("terminal",()=>pipelines?[0].ExportCapture(output)),
                    new KeyValuePair<string,Action>("result",()=>File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Result=complete&&cleanupErrors.IsEmpty?(denseCost?"DIAGNOSTIC":"PASS"):"FAIL",PartialDiagnostic=paired!=null||denseCost,DiagnosticMainTicks=diagnosticMainTicks,BeforeFailureCurrent=beforeFailure,BankCausalControl=bankControl,CostWindowBegan=costBegan,CostWindowEnded=costEnded,Acceptance=denseCost?"NotRun:Safety/formation/fullH/allarrival":"Original gates",phase,Candidate=Environment.GetEnvironmentVariable("RECAST_FORMATION_CANDIDATE"),RecastFormationInputs.Seed,RecastFormationInputs.Generation,PreHorizon=OpposingPreHorizon,MainHorizon=OpposingMainHorizon,Dt=1d/30,StartupWall=OpposingStartupWall,PreWall=OpposingPreWall,MainWall=OpposingMainWall,TimeoutMs=denseCost?480000:780000,Ids=ids,Automatic=automatic,PreTargets=preTargets,Goals=goals,ExpectedSlots=expected,PreQueries=preQueries,LastHost=lastHost,Receipts=receipts,FrameCount=frames.Count,ObserverFrameCount=Interlocked.Read(ref count),InitialSpan=initialSpan,BridgeSpan=bridgeSpan,SimultaneousNorth=bridgeCount,MainIssued=mainIssued,PreIssued=preIssued,ActiveSaved=activeSaved,FirstInstalled=firstInstalled,FirstMotion=firstMotion,Arrivals=arrivals,BridgeVisited=visited.ToArray(),Assigned=assigned.ToDictionary(x=>x.Key,x=>new{x.Value.Slot,x.Value.Assigned}),SafetyAnalysisStatus=safetyAnalysis,SafetyResult=safetyAnalysis=="Complete"?(safety==0?"PASS":"FAIL"):safetyAnalysis,Safety=safety,SafetyCounts=safetyCounts,SafetySamples=safetySamples,ObserverErrors=observerErrors.ToArray(),CleanupErrors=cleanupErrors.ToArray(),Census=census,NativeBefore=native,NativeAfter=census?.Ack==true?(ulong?)NativeCount():null,ReleaseFaultsBefore=faults,ReleaseFaultsAfter=census?.Ack==true?(long?)ReleaseFaults():null,TestLogs=testLogs,Metadata=metadata,Rejected=rejected,Roster=roster},Newtonsoft.Json.Formatting.Indented))),
                    new KeyValuePair<string,Action>("cleanup-faults",()=>File.WriteAllText(Path.Combine(output,"export-faults.txt"),string.Join("\n",cleanupErrors.ToArray())))
                };
                if(paired!=null)exports.Insert(0,new KeyValuePair<string,Action>("paired-peer",()=>File.WriteAllText(Path.Combine(output,"paired-peer.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{PartialDiagnostic=true,paired.Actor,paired.Peer,paired.Latched,paired.Tick,paired.PublicationBefore,paired.PublicationAfter,paired.OwnerThreadFlagReleased,ThreadEndCleanup="Unreleased ThreadStatic state ends with the owned runtime thread; main-thread reflection is never claimed to clear owner-thread tracking",paired.Pair,paired.Receipt,PauseACK=pauseAck,Safety="NotRun",FullH="NotRun",Scheduling="Shared diagnostic using declared actual pair, may alter scheduling; no ordinary parity/performance/acceptance claim",UnknownReason="Stages/flags are actual values; zero velocity alone does not prove why selection stopped",Census=census},Newtonsoft.Json.Formatting.Indented))));
                if(observerCost)exports.Insert(0,new KeyValuePair<string,Action>("observer-cost",()=>File.WriteAllText(Path.Combine(output,"observer-cost.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Enabled=true,Samples=Interlocked.Read(ref observerCostCount),ElapsedTicks=Interlocked.Read(ref observerCostTicks),MaximumElapsedTicks=Interlocked.Read(ref observerCostMax),Frequency=System.Diagnostics.Stopwatch.Frequency,TotalElapsedMilliseconds=Interlocked.Read(ref observerCostTicks)*1000d/System.Diagnostics.Stopwatch.Frequency,MaximumElapsedMilliseconds=Interlocked.Read(ref observerCostMax)*1000d/System.Diagnostics.Stopwatch.Frequency,Boundary="Existing CombatStepForQa Observe callback, inclusive snapshots/reflection/queues, elapsed wall clock. No per-tick I/O. Inside runtime lastCpu timer; no product state change."},Newtonsoft.Json.Formatting.Indented))));
                if(pipelines!=null)for(int i=0;i<2;i++){int group=i;exports.Add(new KeyValuePair<string,Action>("group"+i,()=>{string folder=Path.Combine(output,"group-"+group);Directory.CreateDirectory(folder);pipelines[group].Save(folder,census?.Ack==true);formations[group].Save(Path.Combine(folder,"formation.json"),"after ACK",census?.Ack==true);}));}
                FinalizeObserved(pauseAck,()=>{if(domain!=null&&pipelines!=null){Capture(pipelines[0],output);}},()=>{Attempt(cleanupErrors,"last-host",()=>lastHost=host?.CertifiedPublication);census=Cleanup(runtime,host,domain,cleanupErrors);},TestMatchLogSession.Shutdown,()=>File.WriteAllText(Path.Combine(output,"exit-receipt.txt"),FormatExitReceipt(Environment.GetEnvironmentVariable("RECAST_FORMATION_CANDIDATE"),phase,deadline,wait,exit?.Tick,exit?.Metrics,Interlocked.Read(ref lastTick),Interlocked.Read(ref count),pauseAck,census,pipelines?[0].SnapshotStatus,cleanupErrors.ToArray())),exports,cleanupErrors);
            }
            Assert.IsEmpty(cleanupErrors);Assert.True(census?.Ack==true);Assert.AreEqual(native,NativeCount());Assert.AreEqual(faults,ReleaseFaults());Assert.True(pipelines[0].SnapshotStatus.StartsWith("CAPTURED:"));
        }
        private static void VerifyDenseBankControl(List<OpposingFrame> frames,int[][] ids,ThreeCrossingsMap map,NavGeometry geometry,PlayableProfile profile,string output)
        {
            var bridge=map.Supports.Single(s=>s.SurfaceId=="bridge/north").Bounds;double middle=(bridge.MinZ+bridge.MaxZ)/2;
            var initial=frames.First(f=>f.Phase==2);var displaced=new HashSet<int>();var entered=new HashSet<int>();var crossed=new HashSet<int>();var nearTargets=new HashSet<int>();var handoffs=new HashSet<int>();
            var exits=new Dictionary<int,Body>();var violations=new List<string>();long failureCount=0,coreSweeps=0,simultaneousBankFrames=0;
            void Fail(string reason,int id,long tick){failureCount++;if(violations.Count<24)violations.Add(reason+" actor="+id+" tick="+tick);}
            double Minimum(int side,double radius)=>side==0?middle+radius:bridge.MinZ+radius;
            double Maximum(int side,double radius)=>side==0?bridge.MaxZ-radius:middle-radius;
            for(int side=0;side<2;side++)foreach(var body in initial.Groups[side].Bodies.Where(b=>b.Selected)){
                double reach=body.Speed*Math.Max(1d/30,profile.Navigation.CrowdLookaheadSeconds),x=body.Position.X;
                bool bank=side==0?x>bridge.MaxX+body.Radius&&x<=bridge.MaxX+body.Radius+reach:x<bridge.MinX-body.Radius&&x>=bridge.MinX-body.Radius-reach;
                if(bank&&(body.Position.Z<Minimum(side,body.Radius)||body.Position.Z>Maximum(side,body.Radius)))displaced.Add(body.Id);
            }
            Frame previous=null;
            foreach(var sample in frames){var current=sample.Groups[0];var old=previous?.Bodies.ToDictionary(b=>b.Id);
                if(previous!=null&&current.Tick!=previous.Tick+1)Fail("missing or duplicate tick",0,current.Tick);
                if(sample.Phase==2&&sample.Groups.All(g=>g.Bodies.Any(b=>b.Selected&&b.Installed>0&&b.Moving&&b.Location.SurfaceId!="bridge/north"&&b.Position.X>=bridge.MinX-b.Radius-b.Speed*profile.Navigation.CrowdLookaheadSeconds&&b.Position.X<=bridge.MaxX+b.Radius+b.Speed*profile.Navigation.CrowdLookaheadSeconds)))simultaneousBankFrames++;
                foreach(var body in current.Bodies){
                    if(!map.IsValidLocation(body.Location,body.Radius)||!geometry.IsFree(body.Position,body.Radius))Fail("actual footprint",body.Id,current.Tick);
                    if(old==null)continue;var before=old[body.Id];double dx=body.Position.X-before.Position.X,dz=body.Position.Z-before.Position.Z;
                    if(dx*dx+dz*dz>body.Speed*body.Speed/900+1e-9)Fail("actual speed",body.Id,current.Tick);
                    if(!geometry.SegmentFree(before.Position,body.Position,body.Radius)||!map.TryTraverse(before.Location,body.Position,body.Radius,out var reached)||!reached.Equals(body.Location))Fail("actual typed sweep",body.Id,current.Tick);
                    foreach(var peer in current.Bodies.Where(p=>p.Id<body.Id)){var prior=old[peer.Id];double sx=before.Position.X-prior.Position.X,sz=before.Position.Z-prior.Position.Z,vx=dx-(peer.Position.X-prior.Position.X),vz=dz-(peer.Position.Z-prior.Position.Z),q=vx*vx+vz*vz,t=q==0?0:Math.Max(0,Math.Min(1,-(sx*vx+sz*vz)/q));if(Math.Pow(sx+vx*t,2)+Math.Pow(sz+vz*t,2)<Math.Pow(body.Radius+peer.Radius,2)-1e-8)Fail("actual continuous pair",body.Id,current.Tick);}
                }
                if(sample.Phase==2)for(int side=0;side<2;side++){
                    var group=sample.Groups[side];var expected=new NavPoint(side==0?-26.8:30.4,15);if(!group.OriginalGoal.Equals(expected))continue;
                    foreach(var body in group.Bodies.Where(b=>b.Selected&&b.Installed>0)){
                        if(body.Outcome==NavigationOutcome.Blocked||body.Outcome==NavigationOutcome.Rejected||body.Outcome==NavigationOutcome.Unreachable)Fail("functional terminal failure",body.Id,group.Tick);
                        double lo=bridge.MinX-body.Radius,hi=bridge.MaxX+body.Radius;
                        if(old!=null){var before=old[body.Id];double dx=body.Position.X-before.Position.X,a=0,b=1;bool intersects=before.Position.X>=lo&&before.Position.X<=hi;
                            if(dx!=0){double t0=(lo-before.Position.X)/dx,t1=(hi-before.Position.X)/dx;a=Math.Max(0,Math.Min(t0,t1));b=Math.Min(1,Math.Max(t0,t1));intersects=a<=b;}
                            if(intersects){coreSweeps++;double za=before.Position.Z+(body.Position.Z-before.Position.Z)*a,zb=before.Position.Z+(body.Position.Z-before.Position.Z)*b;if(Math.Min(za,zb)<Minimum(side,body.Radius)-1e-8||Math.Max(za,zb)>Maximum(side,body.Radius)+1e-8)Fail("original core entry/domain",body.Id,group.Tick);}
                        }
                        if(body.Location.SurfaceId=="bridge/north")entered.Add(body.Id);
                        bool past=side==0?body.Position.X<lo:body.Position.X>hi;
                        if(entered.Contains(body.Id)&&past){crossed.Add(body.Id);if(!exits.ContainsKey(body.Id))exits.Add(body.Id,body);}
                        double reach=body.Speed*profile.Navigation.CrowdLookaheadSeconds,gx=body.Assigned.X;
                        bool goalInOldBank=side==0?gx<lo&&gx>=lo-reach:gx>hi&&gx<=hi+reach;
                        bool goalOutside=body.Assigned.Z<Minimum(side,body.Radius)||body.Assigned.Z>Maximum(side,body.Radius);
                        if(goalInOldBank&&goalOutside){nearTargets.Add(body.Id);if(exits.TryGetValue(body.Id,out var exit)){
                            double moved=Math.Pow(body.Position.X-exit.Position.X,2)+Math.Pow(body.Position.Z-exit.Position.Z,2),distance=Math.Pow(body.Assigned.X-body.Position.X,2)+Math.Pow(body.Assigned.Z-body.Position.Z,2),oldDistance=Math.Pow(body.Assigned.X-exit.Position.X,2)+Math.Pow(body.Assigned.Z-exit.Position.Z,2);
                            bool restoredSide=body.Assigned.Z<Minimum(side,body.Radius)?body.Position.Z<Minimum(side,body.Radius)-body.Radius:body.Position.Z>Maximum(side,body.Radius)+body.Radius;
                            if(past&&restoredSide&&moved>=body.Radius*body.Radius&&distance<oldDistance)handoffs.Add(body.Id);
                        }}
                    }
                }
                previous=current;
            }
            int displacedTransit=displaced.Count(crossed.Contains);
            File.WriteAllText(Path.Combine(output,"dense-bank-causal-control.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Meaning="Same original100 normalcommand/threaded execution. Independent actual map/core/pair sweeps; initial displacement relative to OLD lookahead bank footprint, no pose writes. Near assigned fan restores beyond old narrow band with actual goalward translation. Partial window, not full100 arrival acceptance.",Actors=ids.Sum(g=>g.Length),InitialDisplaced=displaced.OrderBy(x=>x).ToArray(),DisplacedActuallyCrossed=displacedTransit,Entered=entered.Count,Crossed=crossed.Count,NearGoalActors=nearTargets.OrderBy(x=>x).ToArray(),ActualHandoffActors=handoffs.OrderBy(x=>x).ToArray(),CoreSweeps=coreSweeps,SimultaneousOpposingBankFrames=simultaneousBankFrames,FailureCount=failureCount,Failures=violations},Newtonsoft.Json.Formatting.Indented));
            Assert.AreEqual(100,ids.Sum(g=>g.Length));Assert.Greater(displaced.Count,0);Assert.Greater(displacedTransit,0,"Initially displaced dense actor must naturally cross aligned");Assert.Greater(nearTargets.Count,0);Assert.Greater(handoffs.Count,0,"Actual near-bank terminal fan must escape the old parked-guide condition");Assert.Greater(coreSweeps,0);Assert.Greater(simultaneousBankFrames,0,"Both opposing cohorts must actually occupy the bank approach concurrently");Assert.Zero(failureCount,string.Join("\n",violations));
        }

    }
}
