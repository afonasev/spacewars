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
        [UnityTest,Timeout(150000)] public IEnumerator FourMemberAsyncCommandIsAttributedThroughArrivalAndCurrentCapture()
        {
            const int horizon=1800;const double wall=90;
            string output=Environment.GetEnvironmentVariable("RECAST_FORMATION_OUTPUT")??throw new InvalidOperationException("Unique output required");Assert.False(Directory.Exists(output));Directory.CreateDirectory(output);Assert.True(NativeSilentStartup.IsSilent);
            var p=PlayableProfile.RecastGroupCorridorDefault;var map=(ThreeCrossingsMap)p.AuthoredMap;var config=RecastFormationInputs.Create(p,out var scenario,out var rejected,scenarioCount:3);var goal=new NavPoint(-23.2,29.4);
            PlayableRuntime runtime=null;UnityHostRouteService host=null;PlayableDomain domain=null;PipelineObservation capture=null;CleanupReceipt census=null;
            var repairsObserved=new ConcurrentQueue<object>();int priorRepairs=0,priorFailures=0;
            var errors=new ConcurrentQueue<string>();var frames=new List<Frame>();var queue=new ConcurrentQueue<Frame>();int[] ids=Array.Empty<int>();HashSet<int> selected=null;long issued=-1,groupIssued=-1,count=0;int activeRequested=0;bool activeSaved=false,pauseAck=false,complete=false;string phase="startup";
            ulong native=NativeCount();long faults=ReleaseFaults();double deadline=Time.realtimeSinceStartupAsDouble+wall;
            var wait=new GuardAwait(name=>{host.Service(runtime,8);Assert.IsNull(runtime.Latest?.Failure,name);},()=>Time.realtimeSinceStartupAsDouble);
            IEnumerator Until(Func<bool> done,string name)=>wait.Until(done,name,deadline,phase);
            void Drain(){while(queue.TryDequeue(out var f))frames.Add(f);}
            void Observe(){try{
                if(Interlocked.Increment(ref count)>horizon+64){errors.Enqueue("small observer capacity exceeded");runtime.RequestPause(true);return;}
                var n=domain.Navigation;var g=n.GroupFor(ids[0]);if(g==null||!g.OriginalGoal.Equals(goal))return;Interlocked.Exchange(ref groupIssued,g.IssuedTick);
                var f=new Frame{Tick=domain.Tick,Group=g.GroupId,Revision=g.OrderRevision,OriginalGoal=g.OriginalGoal,FormationPhase=g.FormationPlan?.Phase.ToString(),Bodies=n.Crowd.Units.Select(a=>{var m=g.Members.SingleOrDefault(v=>v.Entity==a.Id);bool has=m!=null&&m.HasGoal&&m.GoalLocation.HasValue;return new Body{Id=a.Id,Position=a.Position,Location=a.Location,Goal=a.Goal,Assigned=has?m.GoalLocation.Value.Position:a.Goal,Radius=a.Radius,Speed=a.Speed,Selected=selected.Contains(a.Id),Moving=a.Moving,Outcome=a.Outcome,Installed=m!=null&&a.InstalledGroupId==g.GroupId&&a.InstalledRevision==m.OrderRevision?a.InstalledRequest:0,Slot=m?.Slot??-1,HasSlot=has,AllSlots=g.Members.Length==4&&g.Members.All(v=>v.HasGoal),Progress=m?.Recast??default,Spine=Spine(a)};}).OrderBy(v=>v.Id).ToArray()};queue.Enqueue(f);
                if(n.Crowd.RepairCount!=priorRepairs||n.Crowd.RepairFailed!=priorFailures){priorRepairs=n.Crowd.RepairCount;priorFailures=n.Crowd.RepairFailed;repairsObserved.Enqueue(new{Tick=domain.Tick,GlobalRepairs=priorRepairs,GlobalFailures=priorFailures,Group=g.GroupId,GroupIssued=g.IssuedTick,NonRecipientMoving=f.Bodies.Any(v=>!v.Selected&&v.Moving),Members=f.Bodies.Where(v=>v.Selected).Select(v=>new{v.Id,v.Installed,v.Position,v.Moving}).ToArray()});}
                var own=f.Bodies.Where(v=>v.Selected).ToArray();bool installed=own.Length==4&&own.All(v=>v.Installed>0&&v.HasSlot);
                if(installed&&own.Any(v=>v.Moving)&&Interlocked.CompareExchange(ref activeRequested,1,0)==0)runtime.RequestPause(true);
                if(own.Length==4&&own.All(v=>v.Outcome==NavigationOutcome.Arrived)||Interlocked.Read(ref issued)>=0&&domain.Tick-Interlocked.Read(ref issued)>=horizon)runtime.RequestPause(true);
            }catch(Exception e){errors.Enqueue(e.ToString());runtime.RequestPause(true);}}
            void Capture(string name){Assert.True(runtime.Latest.Paused);capture=new PipelineObservation(horizon+64,ids.Last(),ids[0]);capture.Bind(host);capture.CaptureTerminal(domain.Navigation,true,()=>runtime.IsStopRequested);Assert.IsNull(capture.SnapshotError);Assert.True(capture.SnapshotStatus.StartsWith("CAPTURED:"));string folder=Path.Combine(output,name);Directory.CreateDirectory(folder);capture.ExportCapture(folder);File.WriteAllText(Path.Combine(folder,"producer.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Candidate=Environment.GetEnvironmentVariable("RECAST_FORMATION_CANDIDATE"),CaptureSource=Environment.GetEnvironmentVariable("RECAST_FORMATION_CAPTURE_SOURCE"),Tick=domain.Tick,Map=map.Id,map.Revision,Profile=p.ProfileId,ProfileRevision=p.Revision,ProfileBinding=Binding(p),PausedACK=true,Members=ids,CommandOwner="player-1",CommandSequence=1}));}
            try{
                TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));Assert.IsNotNull(TestMatchLogSession.CurrentDirectory);
                runtime=new PlayableRuntime(config,RecastFormationInputs.Generation,startPaused:true);host=new UnityHostRouteService();yield return Until(()=>runtime.IsPrepared&&runtime.Latest.Paused&&host.CertifiedPublication?.RecastFoundationBuilds==1,"small actual threaded startup");wait.Rethrow();domain=Domain(runtime);Assert.Zero(domain.Tick);
                ids=runtime.Latest.Entities.Where(v=>v.Owner==PlayableOwner.Player&&v.Kind==PlayableEntityKind.Explorer).Select(v=>v.Id).OrderBy(v=>v).ToArray();Assert.AreEqual(4,ids.Length);selected=new HashSet<int>(ids);Assert.True(config.Roster.All(v=>v.Control==OfflineControl.Human));Capture("ready-current");Assert.IsNull(domain.CombatStepForQa);domain.CombatStepForQa=Observe;
                runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"small resume");wait.Rethrow();Assert.True(map.TryLocate(goal,p.ExplorerCollisionRadius,out var target));Assert.True(runtime.TrySubmit(new PlayableCommand(RecastFormationInputs.Generation,1,"player-1",PlayableCommandKind.Move,ids,goal).WithResolvedLocation(target)).Accepted);
                yield return Until(()=>runtime.ReceiptsAfter(0).Any(v=>v.OwnerId=="player-1"&&v.Sequence==1&&v.Status!=PlayableCommandStatus.Accepted),"small attributed command receipt");wait.Rethrow();var receipt=runtime.ReceiptsAfter(0).Single(v=>v.OwnerId=="player-1"&&v.Sequence==1&&v.Status!=PlayableCommandStatus.Accepted);Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);Interlocked.Exchange(ref issued,receipt.AppliedTick);phase="movement";
                while(Time.realtimeSinceStartupAsDouble<deadline){
                    host.Service(runtime,8);Assert.IsNull(runtime.Latest.Failure);Drain();Assert.IsEmpty(errors);
                    if(runtime.Latest.Paused){
                        var last=frames.LastOrDefault();Assert.IsNotNull(last);var own=last.Bodies.Where(v=>v.Selected).ToArray();
                        if(!activeSaved&&own.All(v=>v.Installed>0)&&own.Any(v=>v.Moving)){Capture("active-current");activeSaved=true;runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"small active continuation");wait.Rethrow();}
                        else break;
                    }
                    yield return null;
                }
                runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,"small terminal pause");pauseAck=runtime.Latest.Paused;wait.Rethrow();Drain();Assert.IsEmpty(errors);Assert.True(activeSaved);Assert.AreEqual(receipt.AppliedTick,Interlocked.Read(ref groupIssued),"Observer must refer to this actual command");
                var final=frames.Last();Assert.AreEqual(4,final.Bodies.Count(v=>v.Selected&&v.Installed>0&&v.Outcome==NavigationOutcome.Arrived));Assert.LessOrEqual(final.Tick-issued,horizon);Assert.AreEqual(1,frames.Select(v=>v.Group).Distinct().Count());
                foreach(int id in ids){var own=frames.Select(v=>v.Bodies.Single(b=>b.Id==id)).Where(v=>v.Selected&&v.Installed>0).ToArray();Assert.True(own.Any(v=>!v.Position.Equals(own[0].Position)),"Actual installed member must translate");Assert.True(own.All(v=>v.Outcome!=NavigationOutcome.Blocked&&v.Outcome!=NavigationOutcome.Unreachable&&v.Outcome!=NavigationOutcome.Rejected));}
                foreach(var f in frames){for(int i=0;i<f.Bodies.Length;i++){var b=f.Bodies[i];Assert.True(map.IsValidLocation(b.Location,b.Radius));Assert.True(domain.Geometry.IsFree(b.Position,b.Radius));for(int j=i+1;j<f.Bodies.Length;j++){var other=f.Bodies[j];Assert.GreaterOrEqual(Math.Pow(b.Position.X-other.Position.X,2)+Math.Pow(b.Position.Z-other.Position.Z,2),Math.Pow(b.Radius+other.Radius,2)-1e-8);}}}
                Assert.True(domain.Navigation.HasCertifiedHost);Assert.Greater(host.CertifiedPublication.Pumps,0);Capture("terminal-current");complete=true;
            }finally{
                var lastHost=host?.CertifiedPublication;census=Cleanup(runtime,host,domain,errors);TestMatchLogSession.Shutdown();Drain();
                File.WriteAllText(Path.Combine(output,"frames.json"),Newtonsoft.Json.JsonConvert.SerializeObject(frames));File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Result=complete&&errors.IsEmpty?"PASS":"FAIL",Candidate=Environment.GetEnvironmentVariable("RECAST_FORMATION_CANDIDATE"),Seed=RecastFormationInputs.Seed,Generation=RecastFormationInputs.Generation,Horizon=horizon,DeadlineSeconds=wall,Ids=ids,Goal=goal,Issued=issued,GroupIssued=groupIssued,activeSaved,pauseAck,Frames=frames.Count,ObserverCalls=count,Errors=errors.ToArray(),Census=census,NativeBefore=native,NativeAfter=census?.Ack==true?(ulong?)NativeCount():null,FaultsBefore=faults,FaultsAfter=census?.Ack==true?(long?)ReleaseFaults():null,LastHost=lastHost,RepairEvents=repairsObserved.ToArray(),RepairCoverage="Global repair observations, not per-actor or complete/partial classification; this is a threaded passage smoke, not an actor25 replay",Scenario=scenario,Rejected=rejected},Newtonsoft.Json.Formatting.Indented));
            }
            Assert.IsEmpty(errors);Assert.True(census?.Ack==true);Assert.AreEqual(native,NativeCount());Assert.AreEqual(faults,ReleaseFaults());
        }
    }
}
