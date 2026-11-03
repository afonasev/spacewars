using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed partial class RecastFormationOrdinaryTests
    {
        [UnityTest,Timeout(180000)] public IEnumerator TwelveMemberCounterflowKeepsFrozenLanesAndNaturallyArrives()
        {
            const int members=6,horizon=1800;const double wall=120;
            string output=Environment.GetEnvironmentVariable("RECAST_FORMATION_OUTPUT")??throw new InvalidOperationException("Unique output required");Assert.False(Directory.Exists(output));Directory.CreateDirectory(output);Assert.True(NativeSilentStartup.IsSilent);
            var p=PlayableProfile.RecastGroupCorridorDefault;var map=(ThreeCrossingsMap)p.AuthoredMap;var config=RecastOpposingFormationInputs.Create(p,out var rejected,out var roster,scenarioCount:members-1);
            var goals=new[]{new NavPoint(-26.8,15),new NavPoint(30.4,15)};var preGoals=new[]{new NavPoint(34,58.2),new NavPoint(-34,58.2)};
            PlayableRuntime runtime=null;UnityHostRouteService host=null;PlayableDomain domain=null;CleanupReceipt census=null;var errors=new ConcurrentQueue<string>();var queue=new ConcurrentQueue<Frame[]>();var frames=new List<Frame[]>();var formations=new[]{new FormationObservation(horizon),new FormationObservation(horizon)};
            int[][] ids=null;HashSet<int>[] selected=null;long repairCount=0,repairFailed=0,localSamples=0;double repairCpuMs=0;long mainIssued=-1;long[] observedIssued={-1,-1};int phase=0,activeRequested=0;bool activeSaved=false,complete=false,pauseAck=false;ulong native=NativeCount();long faults=ReleaseFaults();double deadline=Time.realtimeSinceStartupAsDouble+wall;
            var wait=new GuardAwait(name=>{host.Service(runtime,8);Assert.IsNull(runtime.Latest?.Failure,name);},()=>Time.realtimeSinceStartupAsDouble);IEnumerator Until(Func<bool> done,string name)=>wait.Until(done,name,deadline,"small counterflow");
            PlayableCommandReceipt Receipt(string owner,int sequence)=>runtime.ReceiptsAfter(0).SingleOrDefault(v=>v.OwnerId==owner&&v.Sequence==sequence&&v.Status!=PlayableCommandStatus.Accepted);
            void Submit(int i,int sequence,int[] actors,NavPoint goal){Assert.True(map.TryLocate(goal,p.ExplorerCollisionRadius,out var target));Assert.True(runtime.TrySubmit(new PlayableCommand(RecastFormationInputs.Generation,sequence,"player-"+(i+1),PlayableCommandKind.Move,actors,goal).WithResolvedLocation(target)).Accepted);}
            void Drain(){while(queue.TryDequeue(out var f))frames.Add(f);}
            void Capture(string name){Assert.True(runtime.Latest.Paused);var c=new PipelineObservation(horizon+64,ids[0].Last(),ids[0][0]);c.Bind(host);c.CaptureTerminal(domain.Navigation,true,()=>runtime.IsStopRequested);Assert.IsNull(c.SnapshotError);Assert.True(c.SnapshotStatus.StartsWith("CAPTURED:"));var folder=Path.Combine(output,name);Directory.CreateDirectory(folder);c.ExportCapture(folder);File.WriteAllText(Path.Combine(folder,"producer.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Candidate=Environment.GetEnvironmentVariable("RECAST_FORMATION_CANDIDATE"),CaptureSource=Environment.GetEnvironmentVariable("RECAST_FORMATION_CAPTURE_SOURCE"),Tick=domain.Tick,Map=map.Id,map.Revision,Profile=p.ProfileId,ProfileRevision=p.Revision,ProfileBinding=Binding(p),PausedACK=true,Members=ids}));}
            var localRouteField=typeof(NavUnit).GetField("localRoute",Hidden);var localIndexField=typeof(NavUnit).GetField("localIndex",Hidden);Assert.IsNotNull(localRouteField);Assert.IsNotNull(localIndexField);
            void Observe(){try{
                if(Volatile.Read(ref phase)==1){if(ids.All(g=>domain.Navigation.Crowd.Units.Single(a=>a.Id==g[0]).Outcome==NavigationOutcome.Arrived))runtime.RequestPause(true);return;}
                if(Volatile.Read(ref phase)!=2)return;repairCount=domain.Navigation.Crowd.RepairCount;repairFailed=domain.Navigation.Crowd.RepairFailed;repairCpuMs=domain.Navigation.Crowd.RepairCpuMs;localSamples+=domain.Navigation.Crowd.Units.Count(a=>((NavPoint[])localRouteField.GetValue(a)).Length>(int)localIndexField.GetValue(a));var groups=ids.Select(g=>domain.Navigation.GroupFor(g[0])).ToArray();if(groups.Where((g,i)=>g==null||!g.OriginalGoal.Equals(goals[i])).Any())return;
                var sample=new Frame[2];for(int i=0;i<2;i++){var g=groups[i];observedIssued[i]=g.IssuedTick;formations[i].Read(g,domain.Tick);sample[i]=new Frame{Tick=domain.Tick,Group=g.GroupId,Revision=g.OrderRevision,OriginalGoal=g.OriginalGoal,FormationPhase=g.FormationPlan?.Phase.ToString(),Bodies=domain.Navigation.Crowd.Units.Select(a=>{var m=g.Members.SingleOrDefault(v=>v.Entity==a.Id);bool has=m!=null&&m.HasGoal&&m.GoalLocation.HasValue;return new Body{Id=a.Id,Position=a.Position,Location=a.Location,Goal=a.Goal,Assigned=has?m.GoalLocation.Value.Position:a.Goal,Radius=a.Radius,Speed=a.Speed,Selected=selected[i].Contains(a.Id),Moving=a.Moving,Outcome=a.Outcome,Installed=m!=null&&a.InstalledGroupId==g.GroupId&&a.InstalledRevision==m.OrderRevision?a.InstalledRequest:0,Slot=m?.Slot??-1,HasSlot=has,AllSlots=g.Members.Length==members&&g.Members.All(v=>v.HasGoal),Progress=m?.Recast??default,Spine=Spine(a)};}).OrderBy(v=>v.Id).ToArray()};}
                queue.Enqueue(sample);if(sample.All(g=>g.Bodies.Count(b=>b.Selected&&b.Installed>0)==members)&&sample.Any(g=>g.Bodies.Any(b=>b.Selected&&b.Moving))&&Interlocked.CompareExchange(ref activeRequested,1,0)==0)runtime.RequestPause(true);
                if(sample.All(g=>g.Bodies.Where(b=>b.Selected).All(b=>b.Outcome==NavigationOutcome.Arrived))||Interlocked.Read(ref mainIssued)>=0&&domain.Tick-Interlocked.Read(ref mainIssued)>=horizon)runtime.RequestPause(true);
            }catch(Exception e){errors.Enqueue(e.ToString());runtime.RequestPause(true);}}
            try{
                TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));Assert.IsNotNull(TestMatchLogSession.CurrentDirectory);runtime=new PlayableRuntime(config,RecastFormationInputs.Generation,startPaused:true);host=new UnityHostRouteService();yield return Until(()=>runtime.IsPrepared&&runtime.Latest.Paused&&host.CertifiedPublication?.RecastFoundationBuilds==1,"counterflow startup");wait.Rethrow();domain=Domain(runtime);Assert.Zero(domain.Tick);
                ids=new[]{PlayableOwner.Player,PlayableOwner.Enemy}.Select(o=>runtime.Latest.Entities.Where(v=>v.Owner==o&&v.Kind==PlayableEntityKind.Explorer).Select(v=>v.Id).OrderBy(v=>v).ToArray()).ToArray();Assert.True(ids.All(g=>g.Length==members));Assert.AreEqual(2*members+1,domain.Navigation.Crowd.Units.Count);Assert.True(config.Roster.All(v=>v.Control==OfflineControl.Human));selected=ids.Select(g=>new HashSet<int>(g)).ToArray();Capture("ready-current");Assert.IsNull(domain.CombatStepForQa);domain.CombatStepForQa=Observe;
                Volatile.Write(ref phase,1);runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"counterflow pre resume");wait.Rethrow();for(int i=0;i<2;i++)Submit(i,1,new[]{ids[i][0]},preGoals[i]);yield return Until(()=>Receipt("player-1",1)!=null&&Receipt("player-2",1)!=null,"counterflow pre receipts");wait.Rethrow();for(int i=0;i<2;i++)Assert.AreEqual(PlayableCommandStatus.Applied,Receipt("player-"+(i+1),1).Status);
                yield return Until(()=>runtime.Latest.Paused,"counterflow pre arrival");wait.Rethrow();for(int i=0;i<2;i++)Assert.AreEqual(NavigationOutcome.Arrived,domain.Navigation.Crowd.Units.Single(a=>a.Id==ids[i][0]).Outcome);Capture("prephase-current");
                Volatile.Write(ref phase,2);runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"counterflow main resume");wait.Rethrow();for(int i=0;i<2;i++)Submit(i,2,ids[i],goals[i]);yield return Until(()=>Receipt("player-1",2)!=null&&Receipt("player-2",2)!=null,"counterflow main receipts");wait.Rethrow();for(int i=0;i<2;i++)Assert.AreEqual(PlayableCommandStatus.Applied,Receipt("player-"+(i+1),2).Status);Interlocked.Exchange(ref mainIssued,Math.Min(Receipt("player-1",2).AppliedTick,Receipt("player-2",2).AppliedTick));
                while(Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);Assert.IsNull(runtime.Latest.Failure);Drain();Assert.IsEmpty(errors);if(runtime.Latest.Paused){var last=frames.Last();if(!activeSaved&&last.All(g=>g.Bodies.Count(b=>b.Selected&&b.Installed>0)==members)&&last.Any(g=>g.Bodies.Any(b=>b.Selected&&b.Moving))){Capture("active-current");activeSaved=true;runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"counterflow active resume");wait.Rethrow();}else break;}yield return null;}
                runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,"counterflow terminal pause");wait.Rethrow();pauseAck=true;Drain();Capture("terminal-current");Assert.IsEmpty(errors);Assert.True(activeSaved);Assert.LessOrEqual(frames.Last()[0].Tick-mainIssued,horizon);
                for(int i=0;i<2;i++){Assert.AreEqual(Receipt("player-"+(i+1),2).AppliedTick,observedIssued[i]);Assert.AreEqual(members,formations[i].MappingRows);Assert.AreEqual(members,formations[i].UniqueRanks);Assert.GreaterOrEqual(formations[i].Lanes,2);Assert.Zero(formations[i].DroppedFrames);foreach(int id in ids[i]){var own=frames.Select(f=>f[i].Bodies.Single(b=>b.Id==id)).Where(b=>b.Installed>0).ToArray();Assert.AreEqual(NavigationOutcome.Arrived,own.Last().Outcome);Assert.True(own.Any(b=>!b.Position.Equals(own[0].Position)));Assert.True(own.Any(b=>b.Location.SurfaceId=="bridge/north"));Assert.True(own.All(b=>b.Outcome!=NavigationOutcome.Blocked&&b.Outcome!=NavigationOutcome.Unreachable&&b.Outcome!=NavigationOutcome.Rejected));}}
                // Actual trajectories, including every local-prefix and redirected
                // translation, must stay on the certified side in this witness.
                // Geometric bounds come from the actual physical support.
                var bridge=map.Supports.Single(v=>v.SurfaceId=="bridge/north").Bounds;double middle=(bridge.MinZ+bridge.MaxZ)/2;
                for(int side=0;side<2;side++)foreach(var f in frames)foreach(var b in f[side].Bodies.Where(v=>v.Selected&&v.Installed>0)){
                    if(b.Position.X>=bridge.MinX-b.Radius&&b.Position.X<=bridge.MaxX+b.Radius){
                        if(side==0)Assert.GreaterOrEqual(b.Position.Z,middle+b.Radius-1e-8,"Westbound actual full-body domain actor "+b.Id+" tick "+f[side].Tick);
                        else Assert.LessOrEqual(b.Position.Z,middle-b.Radius+1e-8,"Eastbound actual full-body domain actor "+b.Id+" tick "+f[side].Tick);
                    }
                }
                // Independent physical oracle: clip actual center sweeps to the
                // map's bank+bridge rectangle, without calling production Allows.
                // Initial displaced bodies may recover before their first alignment.
                var bankRows=new List<object>();var bankFailures=new List<string>();long bankFailureCount=0;
                for(int side=0;side<2;side++)foreach(int id in ids[side]){
                    Body prior=null;bool armed=false,initialDisplaced=false,passedExit=false,turnedAfterExit=false,outsideGoalProgress=false,crossHalfRequired=false;int entrySamples=0,exitSamples=0,guardedSweeps=0;double minimumMargin=double.PositiveInfinity,firstOutsideGoalDistance=double.NaN;
                    void FailBank(long tick,string reason){bankFailureCount++;if(bankFailures.Count<16)bankFailures.Add(id+"@"+tick+":"+reason);}
                    foreach(var f in frames){var body=f[side].Bodies.Single(v=>v.Id==id);if(body.Installed<=0)continue;
                        double reach=body.Speed*Math.Max(1d/30,p.Navigation.CrowdLookaheadSeconds),lo=bridge.MinX-body.Radius-reach,hi=bridge.MaxX+body.Radius+reach;
                        double Margin(double z)=>side==0?z-middle-body.Radius:middle-body.Radius-z;
                        bool inside=body.Position.X>=lo&&body.Position.X<=hi;
                        if(prior==null){initialDisplaced=inside&&Margin(body.Position.Z)<-1e-8;armed=inside&&!initialDisplaced;prior=body;continue;}
                        double dx=body.Position.X-prior.Position.X,a=0,b=1;
                        bool intersects=true;if(dx==0)intersects=prior.Position.X>=lo&&prior.Position.X<=hi;else{double t0=(lo-prior.Position.X)/dx,t1=(hi-prior.Position.X)/dx;a=Math.Max(0,Math.Min(t0,t1));b=Math.Min(1,Math.Max(t0,t1));intersects=a<=b;}
                        if(intersects){double za=prior.Position.Z+(body.Position.Z-prior.Position.Z)*a,zb=prior.Position.Z+(body.Position.Z-prior.Position.Z)*b;
                            if(initialDisplaced&&!armed){if(Math.Max(0,-Margin(zb))>Math.Max(0,-Margin(za))+1e-8)FailBank(f[side].Tick,"initial displaced recovery worsened");if(Margin(zb)>=-1e-8)armed=true;}
                            else{armed=true;guardedSweeps++;double margin=Math.Min(Margin(za),Margin(zb));minimumMargin=Math.Min(minimumMargin,margin);if(margin < -1e-8)FailBank(f[side].Tick,"actual full bank sweep mixed opposite half");}
                        }
                        if(inside&&armed){bool west=body.Position.X<bridge.MinX-body.Radius,east=body.Position.X>bridge.MaxX+body.Radius;if(side==0?east:west)entrySamples++;if(side==0?west:east)exitSamples++;}
                        passedExit|=side==0?body.Position.X<lo:body.Position.X>hi;
                        crossHalfRequired=Margin(body.Assigned.Z)<-1e-8;
                        if(passedExit&&!inside){double gx=body.Assigned.X-body.Position.X,gz=body.Assigned.Z-body.Position.Z,distance=gx*gx+gz*gz;if(double.IsNaN(firstOutsideGoalDistance))firstOutsideGoalDistance=distance;else outsideGoalProgress|=distance<firstOutsideGoalDistance;}
                        turnedAfterExit|=passedExit&&!inside&&Margin(body.Position.Z)<-1e-8;prior=body;
                    }
                    bankRows.Add(new{Side=side,Actor=id,initialDisplaced,entrySamples,exitSamples,guardedSweeps,minimumMargin,passedExit,turnedAfterExit,crossHalfRequired,outsideGoalProgress});
                    if(!armed||entrySamples==0||exitSamples==0||!passedExit||!outsideGoalProgress||crossHalfRequired&&!turnedAfterExit)FailBank(frames.Last()[side].Tick,"missing actual entry/exit/motor-turn witness");
                }
                File.WriteAllText(Path.Combine(output,"bank-trajectories.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Oracle="Actual map bounds +full footprint +existing nominal pace/lookahead, independent of Allows/guide hints; initial displaced recovery separately classified",Rows=bankRows,FailureCount=bankFailureCount,Failures=bankFailures,CollectiveContactJam="Not claimed: same normal6+6 geometry/recipe; original100 still required"},Newtonsoft.Json.Formatting.Indented));
                Assert.Zero(bankFailureCount,string.Join("\n",bankFailures));
                Frame previous=null;foreach(var f in frames){var current=f[0];foreach(var b in current.Bodies){Assert.True(map.IsValidLocation(b.Location,b.Radius));Assert.True(domain.Geometry.IsFree(b.Position,b.Radius));if(previous==null)continue;var before=previous.Bodies.Single(v=>v.Id==b.Id);double dx=b.Position.X-before.Position.X,dz=b.Position.Z-before.Position.Z;Assert.LessOrEqual(dx*dx+dz*dz,b.Speed*b.Speed/900+1e-9);Assert.True(domain.Geometry.SegmentFree(before.Position,b.Position,b.Radius));Assert.True(map.TryTraverse(before.Location,b.Position,b.Radius,out var reached)&&reached.Equals(b.Location));foreach(var other in current.Bodies.Where(v=>v.Id<b.Id)){var old=previous.Bodies.Single(v=>v.Id==other.Id);double sx=before.Position.X-old.Position.X,sz=before.Position.Z-old.Position.Z,vx=dx-(other.Position.X-old.Position.X),vz=dz-(other.Position.Z-old.Position.Z),q=vx*vx+vz*vz,t=q==0?0:Math.Max(0,Math.Min(1,-(sx*vx+sz*vz)/q));Assert.GreaterOrEqual(Math.Pow(sx+vx*t,2)+Math.Pow(sz+vz*t,2),Math.Pow(b.Radius+other.Radius,2)-1e-8);}}previous=current;}
                complete=true;
            }finally{census=Cleanup(runtime,host,domain,errors);TestMatchLogSession.Shutdown();Drain();File.WriteAllText(Path.Combine(output,"frames.json"),Newtonsoft.Json.JsonConvert.SerializeObject(frames));File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Result=complete&&errors.IsEmpty?"PASS":"FAIL",Candidate=Environment.GetEnvironmentVariable("RECAST_FORMATION_CANDIDATE"),Members=members,Seed=RecastFormationInputs.Seed,Generation=RecastFormationInputs.Generation,Horizon=horizon,Wall=wall,MainIssued=mainIssued,ObservedIssued=observedIssued,activeSaved,pauseAck,Frames=frames.Count,RepairCount=repairCount,RepairFailed=repairFailed,RepairCpuMs=repairCpuMs,LocalSamples=localSamples,Errors=errors.ToArray(),Census=census,NativeBefore=native,NativeAfter=census?.Ack==true?(ulong?)NativeCount():null,FaultsBefore=faults,FaultsAfter=census?.Ack==true?(long?)ReleaseFaults():null,Rejected=rejected,Roster=roster},Newtonsoft.Json.Formatting.Indented));}
            Assert.IsEmpty(errors);Assert.True(census?.Ack==true);Assert.AreEqual(native,NativeCount());Assert.AreEqual(faults,ReleaseFaults());
        }
    }
}
