using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class RecastS3ContinuationTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private const int Seed=19092026;
        private const long Generation=811;
        private static readonly FieldInfo MotorIndex=typeof(NavUnit).GetField("routeIndex",Hidden),LocalIndex=typeof(NavUnit).GetField("localIndex",Hidden),LocalRoute=typeof(NavUnit).GetField("localRoute",Hidden),NoProgress=typeof(NavUnit).GetField("noProgressTicks",Hidden);
        private Witness active;
        private Witness Create(string name,bool passage=false)=>active=new Witness(name,passage);
        [TearDown] public void StopAndExportFailedNestedCoroutine(){active?.Dispose();active=null;}
        private sealed class Body
        {
            public int Id,Slot,Ordinal,Lane,Stage;public long Group,Revision,Installed;
            public NavPoint Position,Goal;public NavLocation Location;public NavLocation? Assigned;
            public double Radius,Speed,Heading;public bool Moving,Held,AllSlots;
            public NavigationOutcome Outcome;public RecastMemberProgress Progress;public PlayableUnitQueueSnapshot Queue;
            public int MotorIndex,LocalIndex,LocalCount,NoProgress;public NavPoint? MotorTarget,LocalTarget;
        }
        private sealed class Frame
        {
            public long Tick;public Body[] Bodies;public Dictionary<long,string> Plans;
        }
        private sealed class CompletionRow {public int Id;public long Sequence,Tick;public NavLocation Actual,Assigned;}
        private sealed class Witness:IDisposable
        {
            internal PlayableRuntime Runtime;internal UnityHostRouteService Host;internal PlayableDomain Domain;
            internal readonly PlayableProfile Profile=PlayableProfile.RecastGroupCorridorDefault;
            internal readonly OfflineMatchConfiguration Config;internal readonly IPlayableTerrain Map;
            internal readonly string Output;internal readonly List<Frame> Frames=new List<Frame>();
            internal readonly ConcurrentQueue<string> Errors=new ConcurrentQueue<string>();
            internal readonly List<PlayableCommand> Commands=new List<PlayableCommand>();
            internal readonly List<object> Cleanup=new List<object>();
            internal readonly ConcurrentQueue<CompletionRow> Completions=new ConcurrentQueue<CompletionRow>();
            internal readonly int Horizon;internal int[] Ids;internal NavPoint Goal,Center;
            internal Func<Frame,bool> PauseWhen;internal Action<Frame> Contract;internal Frame Last;
            internal bool Complete;internal string Phase="startup";internal object LastHost;internal long SavedTick=-1;
            private readonly double deadline;private long sequence;private Frame previous;private int disposed;
            internal Witness(string name,bool passage=false)
            {
                string root=Environment.GetEnvironmentVariable("RECAST_CONTINUATION_OUTPUT")??throw new InvalidOperationException("Unique output root required");
                Output=Path.Combine(root,name);Assert.False(Directory.Exists(Output));Directory.CreateDirectory(Output);
                Map=Profile.AuthoredMap;Horizon=passage?2700:1800;deadline=Time.realtimeSinceStartupAsDouble+180;
                var genesis=((ThreeCrossingsMap)Map).Configuration(Profile,Seed,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
                var site=genesis.Sites.Single(s=>s.Id==genesis.Starts.Single(s=>s.PinnedLogicalPlayer==1).SiteId);
                Center=site.Slots.Single(s=>s.Id==1).Position;Goal=new NavPoint(Center.X,Center.Z+7);
                var units=(from x in new[]{-1.8,0,1.8} from z in new[]{-7d,-9d} select new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(Center.X+x,Center.Z+z),Math.PI/2)).ToArray();
                if(passage){Goal=new NavPoint(-46,46);units=(from x in new[]{-27.25d,-24.25d} from z in new[]{42d,44d,46d,48d,50d} select new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(x,z))).ToArray();}
                Config=RecastFormationInputs.WithScenario(Profile,genesis,units);
                Assert.True(NativeSilentStartup.IsSilent);TestMatchLogSession.StartDefault(Path.Combine(Output,"TestLogs"));
                Runtime=new PlayableRuntime(Config,Generation,startPaused:true);Host=new UnityHostRouteService();
            }
            internal IEnumerator Until(Func<bool> done,string name)
            {
                Phase=name;
                while(!done()&&Errors.IsEmpty&&Time.realtimeSinceStartupAsDouble<deadline){
                    if(Last?.Tick>=Horizon){Errors.Enqueue("horizon "+Horizon+": "+name);break;}
                    try{Host.Service(Runtime,8);if(Runtime.Latest?.Failure!=null)Errors.Enqueue(Runtime.Latest.Failure);}catch(Exception e){Errors.Enqueue(e.ToString());}
                    yield return null;
                }
                if(!done()&&Errors.IsEmpty)Errors.Enqueue("deadline: "+name);
            }
            internal void Check()=>Assert.IsEmpty(Errors,string.Join("\n",Errors));
            internal IEnumerator Start()
            {
                yield return Until(()=>Runtime.IsPrepared&&Runtime.Latest!=null,"prepared ordinary runtime");Check();
                Domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",Hidden).GetValue(Runtime);
                Ids=Runtime.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank).Select(e=>e.Id).OrderBy(id=>id).ToArray();Assert.AreEqual(Horizon==2700?10:6,Ids.Length);
                Domain.CombatStepForQa=Observe;Runtime.RequestPause(false);yield return Until(()=>!Runtime.Latest.Paused,"resume ordinary ingress");Check();
            }
            internal PlayableCommandReceipt Receipt(long seq)=>Runtime.ReceiptsAfter(0).FirstOrDefault(r=>r.Sequence==seq&&r.OwnerId=="player-1"&&r.Status!=PlayableCommandStatus.Accepted);
            internal IEnumerator Send(PlayableCommandKind kind,int[] ids,NavPoint target=default,bool append=false)
            {
                Runtime.RequestPause(false);yield return Until(()=>!Runtime.Latest.Paused,"resume for "+kind);Check();
                var command=new PlayableCommand(Generation,++sequence,"player-1",kind,ids,target,mode:append?PlayableOrderMode.Append:PlayableOrderMode.Replace).AsHuman();
                Commands.Add(command);var accepted=Runtime.TrySubmit(command);Assert.True(accepted.Accepted,kind+" ingress: "+accepted.Status);
                yield return Until(()=>Receipt(command.Sequence)!=null,"receipt "+command.Sequence);Check();Assert.AreEqual(PlayableCommandStatus.Applied,Receipt(command.Sequence).Status,Receipt(command.Sequence).Message);
            }
            private static string Plan(RecastFormationPlan p)=>p==null?null:JsonConvert.SerializeObject(new{p.World,p.Group,p.RootOrder,p.Command,p.BirthMembership,p.LaneCount,p.Phase,p.FrontAnchor,p.AnchorInitialized,p.EntryPortal,p.ExitPortal,p.Seal,Entries=p.Entries.Select(e=>new{e.Entity,e.Slot,e.SpineIndex,e.EntryOrdinal,e.Lane,e.Stage,e.Incarnation,e.Order,e.Mobility,e.Request}).ToArray()});
            internal Frame Read()
            {
                var nav=Domain.Navigation;var plans=new Dictionary<long,string>();
                var bodies=nav.Crowd.Units.Select(a=>{
                    var g=nav.GroupFor(a.Id);var m=g?.Members.SingleOrDefault(v=>v.Entity==a.Id);
                    if(g!=null&&!plans.ContainsKey(g.GroupId))plans.Add(g.GroupId,Plan(g.FormationPlan));
                    var entry=g?.FormationPlan?.Entries.FirstOrDefault(e=>e.Entity==a.Id);
                    int motor=(int)MotorIndex.GetValue(a),local=(int)LocalIndex.GetValue(a);var localRoute=(NavPoint[])LocalRoute.GetValue(a);
                    return new Body{Id=a.Id,Position=a.Position,Location=a.Location,Goal=a.Goal,Heading=a.Heading,Radius=a.Radius,Speed=a.Speed,Held=a.Held,Moving=a.Moving,Outcome=a.Outcome,Group=g?.GroupId??0,Revision=g?.OrderRevision??0,Installed=a.InstalledRequest,Slot=m?.Slot??-1,Assigned=m?.GoalLocation,AllSlots=g!=null&&g.Members.Where(v=>v.Phase!=GroupMemberPhase.Held&&v.Phase!=GroupMemberPhase.Stopped).All(v=>v.HasGoal&&v.GoalLocation.HasValue),Progress=m?.Recast??default,Queue=Domain.TacticalQueueProjection(a.Id,PlayableOwner.Player),Stage=entry.HasValue?(int)entry.Value.Stage:-1,Ordinal=entry?.EntryOrdinal??-1,Lane=entry?.Lane??-1,MotorIndex=motor,LocalIndex=local,LocalCount=localRoute.Length,NoProgress=(int)NoProgress.GetValue(a),MotorTarget=motor<a.Route.Count?(NavPoint?)a.Route[motor]:null,LocalTarget=local<localRoute.Length?(NavPoint?)localRoute[local]:null};
                }).OrderBy(b=>b.Id).ToArray();
                return new Frame{Tick=Domain.Tick,Bodies=bodies,Plans=plans};
            }
            private void Observe()
            {
                try{
                    var f=Read();var geometry=Domain.Geometry;
                    foreach(var b in f.Bodies){
                        if(!geometry.IsFree(b.Position,b.Radius)||!Map.IsValidLocation(b.Location,b.Radius))Errors.Enqueue("footprint/layer "+b.Id+"@"+f.Tick);
                        if(Ids.Contains(b.Id)&&b.Moving&&!b.AllSlots)Errors.Enqueue("motion before all assigned slots "+b.Id);
                        if(Ids.Contains(b.Id)&&(b.Outcome==NavigationOutcome.Blocked||b.Outcome==NavigationOutcome.Unreachable||b.Outcome==NavigationOutcome.Rejected))Errors.Enqueue("terminal navigation "+b.Id+"@"+f.Tick);
                        if(previous==null)continue;var old=previous.Bodies.Single(v=>v.Id==b.Id);
                        if(Distance(old.Position,b.Position)>b.Speed/30+1e-8)Errors.Enqueue("speed/teleport "+b.Id+"@"+f.Tick);
                        if(!geometry.SegmentFree(old.Position,b.Position,b.Radius)||!Map.TryTraverse(old.Location,b.Position,b.Radius,out var at)||!at.Equals(b.Location))Errors.Enqueue("continuous geometry/layer sweep "+b.Id+"@"+f.Tick);
                        foreach(var peer in f.Bodies.Where(v=>v.Id<b.Id)){
                            var op=previous.Bodies.Single(v=>v.Id==peer.Id);double sx=old.Position.X-op.Position.X,sz=old.Position.Z-op.Position.Z,dx=b.Position.X-peer.Position.X-sx,dz=b.Position.Z-peer.Position.Z-sz,q=dx*dx+dz*dz,t=q==0?0:Math.Max(0,Math.Min(1,-(sx*dx+sz*dz)/q));
                            if(sx*sx+sz*sz+2*t*(sx*dx+sz*dz)+t*t*q<Math.Pow(b.Radius+peer.Radius,2)-1e-8)Errors.Enqueue("continuous peer sweep "+b.Id+"/"+peer.Id+"@"+f.Tick);
                        }
                    }
                    Contract?.Invoke(f);Frames.Add(f);Last=f;previous=f;
                    if(PauseWhen?.Invoke(f)==true||!Errors.IsEmpty||f.Tick>=Horizon)Runtime.RequestPause(true);
                }catch(Exception e){Errors.Enqueue(e.ToString());Runtime.RequestPause(true);}
            }
            internal IEnumerator PauseAt(Func<Frame,bool> condition,string name)
            {
                PauseWhen=condition;Runtime.RequestPause(false);
                yield return Until(()=>Runtime.Latest.Paused&&Last!=null&&condition(Last),name);Check();PauseWhen=null;
            }
            internal Body[] Movers(Frame frame)=>frame.Bodies.Where(b=>Ids.Contains(b.Id)).ToArray();
            internal IEnumerator Arrive(int[] ids,string name)
            {
                yield return PauseAt(f=>ids.All(id=>f.Bodies.Single(b=>b.Id==id).Outcome==NavigationOutcome.Arrived),name);Check();
                foreach(int id in ids){var b=Last.Bodies.Single(v=>v.Id==id);Assert.True(b.Assigned.HasValue);Assert.LessOrEqual(Distance(b.Position,b.Assigned.Value.Position),Profile.Navigation.ArrivalTolerance);}
            }
            internal IEnumerator SaveRestore()
            {
                Assert.True(Runtime.Latest.Paused);var prefix=Read();Assert.True(Movers(prefix).Any(b=>b.Moving),"save during actual unfinished movement");Assert.True(Movers(prefix).All(b=>b.Installed>0),"current ready decisions installed before capture");
                var capture=Runtime.RequestCaptureBytes();yield return Until(()=>capture.IsCompleted,"current moving save capture");Check();Assert.False(capture.IsFaulted,capture.Exception?.ToString());Assert.False(capture.IsCanceled);var bytes=capture.Result;
                using(var envelope=PlayableWorldState.Decode(bytes)){
                    Assert.AreEqual(PlayableWorldState.CurrentVersion,envelope.Version);Assert.AreEqual(Seed,envelope.Seed);Assert.AreEqual(Generation,envelope.Generation);Assert.AreEqual(Config.SourceIdentity,envelope.SourceIdentity);Assert.AreEqual(prefix.Tick,envelope.Tick);CollectionAssert.AreEqual(WorldWire.Binding(Profile),envelope.Binding);CollectionAssert.AreEqual(OfflineConfigurationWire.Binding(Config,Profile),envelope.Participants);SavedTick=envelope.Tick;
                }
                File.WriteAllBytes(Path.Combine(Output,"moving-current.world"),bytes);File.WriteAllText(Path.Combine(Output,"save-prefix.json"),JsonConvert.SerializeObject(prefix));
                Stop();Runtime=PlayableRuntime.RestoreBytes(bytes,Config);Host=new UnityHostRouteService();Domain=null;
                yield return Until(()=>Runtime.IsPrepared&&Runtime.Latest.Paused,"restored prepared paused runtime");Check();
                Domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",Hidden).GetValue(Runtime);var restored=Read();
                Assert.AreEqual(JsonConvert.SerializeObject(prefix),JsonConvert.SerializeObject(restored),"exact poses, goals, slots, group/order, held, ready requests/progress, queue identities and passage order at unchanged tick");
                Assert.Zero(Host.CertifiedPublication.RecastLongQueries,"loading ready decisions must not replace them with new long searches");
                File.WriteAllText(Path.Combine(Output,"restored-prefix.json"),JsonConvert.SerializeObject(restored));
                Assert.AreEqual(SavedTick,Runtime.Latest.Tick,"preparation must not advance match clock");Domain.CombatStepForQa=Observe;
            }
            private void Stop()
            {
                if(Runtime==null)return;if(Domain!=null){Domain.CombatStepForQa=null;Domain.TacticalCompletionForQa=null;}LastHost=Host?.CertifiedPublication;
                var worker=Host==null?null:(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",Hidden).GetValue(Host);
                Runtime.RequestStop();Host?.Dispose();Runtime.Dispose();Assert.True(SpinWait.SpinUntil(()=>Runtime.IsStopped&&(worker==null||worker.IsStopped),5000),"runtime/worker ACK");
                Domain=Domain??(PlayableDomain)typeof(PlayableRuntime).GetField("domain",Hidden).GetValue(Runtime);
                var nav=Domain.Navigation;nav.ReleaseCommittedHostRoot();Domain.DestroyOwnedNavigation();var b=nav.PhysicalHostBudget;
                Cleanup.Add(new{b.PhysicalBytes,b.HeldBytes,b.ActiveScopes});Assert.Zero(b.PhysicalBytes);Assert.Zero(b.HeldBytes);Assert.Zero(b.ActiveScopes);Runtime=null;Host=null;Domain=null;
            }
            public void Dispose()
            {
                if(Interlocked.Exchange(ref disposed,1)!=0)return;
                if(!Complete&&Domain!=null&&Runtime!=null){
                    try{Runtime.RequestPause(true);if(SpinWait.SpinUntil(()=>Runtime.Latest?.Paused==true,5000)){var capture=Runtime.RequestCaptureBytes();if(SpinWait.SpinUntil(()=>capture.IsCompleted,5000)&&!capture.IsFaulted&&!capture.IsCanceled)File.WriteAllBytes(Path.Combine(Output,"failure-current.world"),capture.Result);}}catch(Exception e){Errors.Enqueue("failure capture: "+e);}
                }
                try{Stop();}catch(Exception e){Errors.Enqueue("cleanup: "+e);}
                try{TestMatchLogSession.Shutdown();}catch(Exception e){Errors.Enqueue("journal: "+e);}
                File.WriteAllText(Path.Combine(Output,"frames.json"),JsonConvert.SerializeObject(Frames));
                File.WriteAllText(Path.Combine(Output,"result.json"),JsonConvert.SerializeObject(new{Result=Complete&&Errors.IsEmpty?"PASS":"FAIL",Phase,Seed,Generation,Horizon,Ids,Goal,Center,SavedTick,LastTick=Last?.Tick,Commands,Completions=Completions.ToArray(),Errors=Errors.ToArray(),Cleanup,Host=LastHost,Candidate=Environment.GetEnvironmentVariable("RECAST_CONTINUATION_CANDIDATE")},Formatting.Indented));
            }
        }
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
        private static Dictionary<int,Body> Assigned(Witness w)=>w.Movers(w.Last).ToDictionary(b=>b.Id);
        private static void Unchanged(Frame f,Dictionary<int,Body> expected,IEnumerable<int> ids)
        {
            foreach(int id in ids){var old=expected[id];var b=f.Bodies.Single(v=>v.Id==id);Assert.AreEqual(old.Group,b.Group,"nonrecipient group "+id);Assert.AreEqual(old.Revision,b.Revision,"nonrecipient revision "+id);Assert.AreEqual(old.Slot,b.Slot,"nonrecipient slot "+id);Assert.AreEqual(old.Assigned,b.Assigned,"nonrecipient assigned location "+id);}
        }
        [UnityTest,Timeout(240000)] public IEnumerator PartialMovePreservesOtherMembersWithoutReissue()
        {
            using(var w=Create("partial-move")){
                yield return w.Start();yield return w.Send(PlayableCommandKind.Move,w.Ids,w.Goal);
                yield return w.PauseAt(f=>w.Movers(f).All(b=>b.Assigned.HasValue&&b.Moving&&b.Installed>0),"all original members moving");
                var initial=Assigned(w);var subset=w.Ids.Take(2).ToArray();var remaining=w.Ids.Except(subset).ToArray();w.Contract=f=>Unchanged(f,initial,remaining);
                yield return w.Send(PlayableCommandKind.Move,subset,new NavPoint(w.Center.X-4,w.Center.Z+3));
                yield return w.Arrive(w.Ids,"subset and untouched members naturally arrive");w.Check();
                Assert.True(subset.All(id=>w.Last.Bodies.Single(b=>b.Id==id).Group!=initial[id].Group));Assert.AreEqual(2,w.Commands.Count);w.Complete=true;
            }
        }
        [UnityTest,Timeout(240000)] public IEnumerator PartialHoldAndStopLeaveRemainderMovingWithoutReissue()
        {
            using(var w=Create("partial-hold-stop")){
                yield return w.Start();yield return w.Send(PlayableCommandKind.Move,w.Ids,w.Goal);
                yield return w.PauseAt(f=>w.Movers(f).All(b=>b.Assigned.HasValue&&b.Moving&&b.Installed>0),"all original members moving");
                var initial=Assigned(w);int held=w.Ids[0],stopped=w.Ids[1];var rest=w.Ids.Skip(2).ToArray();w.Contract=f=>Unchanged(f,initial,rest);
                yield return w.Send(PlayableCommandKind.Hold,new[]{held});yield return w.Send(PlayableCommandKind.Stop,new[]{stopped});
                yield return w.PauseAt(f=>f.Bodies.Single(b=>b.Id==held).Held&&!f.Bodies.Single(b=>b.Id==stopped).Moving,"subset HOLD and STOP applied");
                var heldPosition=w.Last.Bodies.Single(b=>b.Id==held).Position;long holdTick=w.Last.Tick;
                w.Contract=f=>{Unchanged(f,initial,rest);var h=f.Bodies.Single(b=>b.Id==held);Assert.True(h.Held);Assert.AreEqual(heldPosition,h.Position);Assert.False(h.Moving);};
                yield return w.Arrive(rest,"untouched remainder naturally arrives");w.Check();Assert.Greater(w.Last.Tick-holdTick,20);Assert.AreEqual(1,w.Commands.Count(c=>c.Kind==PlayableCommandKind.Move));w.Complete=true;
            }
        }
        [UnityTest,Timeout(240000)] public IEnumerator QueuedMovesSurviveActiveSaveAndAdvanceWithoutNewCommands()
        {
            using(var w=Create("queued-save")){
                yield return w.Start();yield return w.Send(PlayableCommandKind.Move,w.Ids,w.Goal);
                yield return w.PauseAt(f=>w.Movers(f).All(b=>b.Assigned.HasValue&&b.Moving&&b.Installed>0),"all original members moving");
                var initial=Assigned(w);yield return w.Send(PlayableCommandKind.Move,w.Ids,new NavPoint(w.Center.X-4,w.Center.Z+3),append:true);
                yield return w.Send(PlayableCommandKind.Move,w.Ids,w.Goal,append:true);
                yield return w.PauseAt(f=>w.Movers(f).All(b=>b.Moving&&b.Queue?.Active?.CommandSequence==1&&b.Queue.Deferred.Count==2),"two queued waypoints during initial movement");
                Unchanged(w.Last,initial,w.Ids);yield return w.SaveRestore();
                // Completion is transient: activation can clear it before the
                // next combat tick. Observe the real event before that overwrite.
                w.Domain.TacticalCompletionForQa=(id,c)=>{
                    if(!w.Ids.Contains(id))return;var a=w.Domain.Navigation.Crowd.Units.Single(v=>v.Id==id);
                    if(!c.AssignedLocation.HasValue||Distance(a.Position,c.AssignedLocation.Value.Position)>w.Profile.Navigation.ArrivalTolerance||!w.Map.TryTraverse(a.Location,c.AssignedLocation.Value.Position,a.Radius,out var reached)||!reached.Equals(c.AssignedLocation.Value)){w.Errors.Enqueue("queued completion outside own slot "+id);return;}
                    w.Completions.Enqueue(new CompletionRow{Id=id,Sequence=c.Order.Sequence,Tick=c.CompletionTick,Actual=new NavLocation(a.Position,a.Location.SurfaceId),Assigned=new NavLocation(c.AssignedLocation.Value.Position,c.AssignedLocation.Value.SurfaceId)});
                };
                yield return w.PauseAt(f=>w.Movers(f).All(b=>b.Queue?.Completion?.Order.Sequence==3&&b.Outcome==NavigationOutcome.Arrived&&b.Queue.Deferred.Count==0),"saved queue completes all three waypoints");w.Check();
                foreach(int id in w.Ids)CollectionAssert.AreEquivalent(new long[]{1,2,3},w.Completions.Where(c=>c.Id==id).Select(c=>c.Sequence).ToArray(),"natural completion of each queued waypoint "+id);
                Assert.AreEqual(3,w.Commands.Count);w.Complete=true;
            }
        }
        [UnityTest,Timeout(240000)] public IEnumerator PassageQueueSurvivesMovingSaveAndAllMembersNaturallyArrive()
        {
            using(var w=Create("passage-save",passage:true)){
                yield return w.Start();yield return w.Send(PlayableCommandKind.Move,w.Ids,w.Goal);
                yield return w.PauseAt(f=>w.Movers(f).All(b=>b.Assigned.HasValue)&&w.Movers(f).Any(b=>b.Stage==(int)RecastPassageStage.Staging)&&w.Movers(f).Any(b=>b.Stage==(int)RecastPassageStage.Admitted&&b.Moving&&b.Location.SurfaceId=="ramp/-1/1/x"),"physically moving on the ramp with a waiting passage tail");
                var initial=Assigned(w);w.Contract=f=>Unchanged(f,initial,w.Ids);yield return w.SaveRestore();
                yield return w.Arrive(w.Ids,"all queued passage members naturally arrive after load");w.Check();
                foreach(int id in w.Ids)Assert.True(w.Frames.Any(f=>f.Bodies.Single(b=>b.Id==id).Location.SurfaceId=="ramp/-1/1/x"),"physical ramp passage "+id);
                Assert.AreEqual(1,w.Commands.Count);w.Complete=true;
            }
        }
    }
}
