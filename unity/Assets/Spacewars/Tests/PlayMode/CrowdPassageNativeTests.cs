using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class CrowdPassageNativeTests
    {
        [UnityTest] public IEnumerator OrdinaryTwoGroupsCompleteTheActualCentralBridgePassage()=>RunOrdinaryPassage(false);
        [UnityTest] public IEnumerator OrdinaryTwoGroupsCompleteTheActualSingleWidthHeldPassage()=>RunOrdinaryPassage(true);
        private static OfflineMatchConfiguration Configuration(bool narrow,out double entry,out double exit,out double middle)
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=19092026};setup.InitializeParticipants();foreach(var p in setup.Participants)p.Human=true;
            var seed=NativeLobbyMatch.Create(profile,setup);double lane=profile.Navigation.ArrivalSlotSpacing/2;
            var scenario=new[]{new OfflineScenarioUnit(seed.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(-18,-lane),0),new OfflineScenarioUnit(seed.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(-18,lane),0),new OfflineScenarioUnit(seed.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(18,-lane),Math.PI),new OfflineScenarioUnit(seed.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(18,lane),Math.PI)};
            entry=0;exit=0;middle=0;
            if(narrow){
                var terrain=profile.AuthoredMap;double radius=profile.TankCollisionRadius;var provider=new PartitionNavigationProvider(terrain,new NavGeometry(terrain.HalfExtent,seed.Obstacles.ToArray(),1),null);var clearance=new NavClearanceProfile("native-traffic",1,"tank",1,radius);
                Assert.True(provider.TryLocate(new NavPoint(-18,0),clearance,out var start));Assert.True(provider.TryTrace(start,new NavPoint(18,0),clearance,out _,out var trace));var seams=trace.Where(l=>l.Kind==NavTypedLegKind.Portal).Select(l=>provider.Seams.Single(s=>s.Id==l.PortalId)).ToArray();Assert.AreEqual(2,seams.Length);
                entry=seams.Min(s=>s.From.X);exit=seams.Max(s=>s.From.X);middle=(entry+exit)/2;double low=seams.Max(s=>Math.Min(s.From.Z,s.To.Z)),high=seams.Min(s=>Math.Max(s.From.Z,s.To.Z)),spare=high-low-18*radius,grid=profile.Navigation.GridCell,gridLane=Math.Ceiling((low+radius)/grid)*grid,gap=(spare-(gridLane-(low+radius)))/16,top=high-radius-gap;
                Assert.Greater(gap,0);double rowMiddle=middle;scenario=scenario.Concat(Enumerable.Range(0,8).Select(i=>new OfflineScenarioUnit(seed.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(rowMiddle,top-i*(2*radius+gap)),0))).ToArray();
            }
            var costs=new double[seed.Starts.Count,seed.Starts.Count];for(int i=0;i<seed.Starts.Count;i++)for(int j=0;j<seed.Starts.Count;j++)costs[i,j]=seed.RouteCost(i,j);
            return new OfflineMatchConfiguration(profile,seed.SourceIdentity,seed.MapIdentity,seed.RouteProvenance,seed.Seed,seed.Roster.ToArray(),seed.Starts.ToArray(),seed.Sites.ToArray(),seed.Obstacles.ToArray(),costs,seed.Spectators.ToArray(),scenario,profile.AuthoredMap.Id,seed.ScenarioBuildings.ToArray());
        }
        [UnityTest] public IEnumerator ThreadedTwoGroupsCompleteTheActualCentralBridgePassage()=>RunThreadedPassage(false);
        [UnityTest] public IEnumerator ThreadedTwoGroupsCompleteTheActualSingleWidthHeldPassage()=>RunThreadedPassage(true);
        [UnityTest] public IEnumerator ThreadedQueuedCurrentRestoreAndSubsetReplacementCompleteTheSingleWidthHeldPassage()=>RunThreadedPassage(true,true);
        // Observe on the existing authority-thread hook, copying only scalar rows.
        // Never borrow live traffic arrays from the presentation/test thread.
        private sealed class TrafficWitness
        {
            internal readonly object Gate=new object();
            internal readonly System.Collections.Generic.List<(long Tick,long[][] Rows)> Frames=new System.Collections.Generic.List<(long,long[][])>();
            internal int Boundary;
            internal static PlayableDomain Domain(PlayableRuntime runtime)=>(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            internal static long[][] Rows(PlayableDomain domain)
            {
                var traffic=(NavigationTrafficAuthority)typeof(NavigationSession).GetField("trafficAuthority",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(domain.Navigation);
                return traffic==null?Array.Empty<long[]>():Enumerable.Range(0,traffic.Count).Select(row=>Enumerable.Range(0,8).Select(column=>traffic.At(row,column)).ToArray()).ToArray();
            }
            internal void Attach(PlayableRuntime runtime,bool pauseAtQueue)
            {
                var domain=Domain(runtime);domain.CombatStepForQa=()=>{
                    var rows=Rows(domain);lock(Gate)Frames.Add((domain.Tick,rows));
                    if(pauseAtQueue&&rows.Length==4&&rows.Count(row=>row[6]==0)==2&&System.Threading.Interlocked.CompareExchange(ref Boundary,1,0)==0)runtime.RequestPause(true);
                };
            }
        }
        private IEnumerator RunThreadedPassage(bool narrow,bool lifecycle=false)
        {
            var c=Configuration(narrow,out double entry,out double exit,out double middle);var profile=c.Profile;
            Assert.LessOrEqual(profile.NavigationRequestsPerFrame,8,"ordinary profile quantum cannot exceed the frozen caller budget");
            var map=(ThreeCrossingsMap)profile.AuthoredMap;var runtime=new PlayableRuntime(c,71,startPaused:true);var host=new UnityHostRouteService();
            long firstMotion=-1,commandTick=-1,preparationEpoch=0,lastTick=-1;int observed=0,gaps=0;
            var crossed=new System.Collections.Generic.HashSet<int>();var followers=new System.Collections.Generic.HashSet<long>();
            var moved=new System.Collections.Generic.HashSet<int>();var directions=new System.Collections.Generic.Dictionary<long,int[]>();
            var previous=Array.Empty<PlayableEntitySnapshot>();int[] ids=Array.Empty<int>();
            double deadline=UnityEngine.Time.realtimeSinceStartupAsDouble+180;
            var trafficWitness=lifecycle?new TrafficWitness():null;int replaced=0,survivor=0;long survivorAge=0,replacedAge=0,replacementOrder=0,survivorGrant=-1,replacedGrant=-1;
            string Facts()=>"narrow="+narrow+",commandTick="+commandTick+",firstMotion="+firstMotion+",tick="+runtime.Latest?.Tick+",preparationPumps="+preparationEpoch+",epoch="+host.SolverEpoch+",workerPumps="+host.CertifiedPublication?.Pumps+",crossed="+string.Join(",",crossed)+",observed="+observed+",gaps="+gaps+",actors="+string.Join(";",runtime.Latest.Entities.Where(e=>ids.Contains(e.Id)).Select(e=>e.Id+":"+e.Position.X+","+e.Position.Z+":"+e.NavigationOutcome));
            IEnumerator Until(Func<bool> ready,string phase)
            {
                while(!ready()&&UnityEngine.Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);Assert.IsNull(runtime.Latest.Failure,phase);Assert.False(runtime.IsStopped,phase);yield return null;}
                Assert.True(ready(),phase+" "+Facts());
            }
            try{
                yield return Until(()=>runtime.IsPrepared&&runtime.Latest.Sequence>0,"startup");
                var tanks=runtime.Latest.Entities.Where(e=>e.Kind==PlayableEntityKind.Tank).ToArray();
                var blockers=narrow?tanks.Where(e=>e.Position.X==middle).Select(e=>e.Id).ToArray():Array.Empty<int>();
                ids=tanks.Select(e=>e.Id).Except(blockers).ToArray();Assert.AreEqual(4,ids.Length);Assert.AreEqual(narrow?8:0,blockers.Length);
                var initial=tanks.ToDictionary(e=>e.Id,e=>e);var left=ids.Where(id=>initial[id].Position.X<0).ToArray();var right=ids.Except(left).ToArray();Assert.AreEqual(2,left.Length);Assert.AreEqual(2,right.Length);
                foreach(var a in tanks){Assert.True(map.SupportsFootprint(a.Position,profile.TankCollisionRadius));Assert.True(runtime.NavigationBinding.Geometry.IsFree(a.Position,profile.TankCollisionRadius));foreach(var b in tanks.Where(b=>b.Id<a.Id)){double x=a.Position.X-b.Position.X,z=a.Position.Z-b.Position.Z;Assert.GreaterOrEqual(x*x+z*z,4*profile.TankCollisionRadius*profile.TankCollisionRadius-1e-8);}}
                runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"resume");long sequence=1;
                IEnumerator Apply(PlayableCommandKind kind,int[] actors,NavPoint target)
                {
                    long issued=sequence++;Assert.True(runtime.TrySubmit(new PlayableCommand(71,issued,c.Roster[0].Id,kind,actors,target)).Accepted);
                    yield return Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==issued&&r.Status!=PlayableCommandStatus.Accepted),"command "+issued);
                    var receipt=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==issued&&r.Status!=PlayableCommandStatus.Accepted);Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
                }
                if(narrow)yield return Apply(PlayableCommandKind.Hold,blockers,new NavPoint(middle,0));
                if(lifecycle)trafficWitness.Attach(runtime,true);
                // Queue both ordinary commands before yielding: route preparation
                // must not grant the first direction a fixture-created head start.
                long pumpStart=host.CertifiedPublication?.Pumps??0,leftOrder=sequence++,rightOrder=sequence++;
                Assert.True(runtime.TrySubmit(new PlayableCommand(71,leftOrder,c.Roster[0].Id,PlayableCommandKind.Move,left,new NavPoint(18,0))).Accepted);
                Assert.True(runtime.TrySubmit(new PlayableCommand(71,rightOrder,c.Roster[0].Id,PlayableCommandKind.Move,right,new NavPoint(-18,0))).Accepted);
                yield return Until(()=>new[]{leftOrder,rightOrder}.All(order=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==order&&r.Status!=PlayableCommandStatus.Accepted)),"both group commands");
                foreach(long order in new[]{leftOrder,rightOrder}){var receipt=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==order&&r.Status!=PlayableCommandStatus.Accepted);Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);if(commandTick<0||receipt.AppliedTick<commandTick)commandTick=receipt.AppliedTick;}
                // Receipts are published before Latest. Read only the snapshot
                // that actually contains both applied orders, even at the same tick.
                yield return Until(()=>ids.All(id=>runtime.Latest.Entities.Single(e=>e.Id==id).OrderStamp?.Sequence==(left.Contains(id)?leftOrder:rightOrder)),"both current orders published");
                directions.Add(leftOrder,left);directions.Add(rightOrder,right);
                if(lifecycle){
                    yield return Until(()=>System.Threading.Volatile.Read(ref trafficWitness.Boundary)==1&&runtime.Latest.Paused,"queued current checkpoint boundary");
                    preparationEpoch=(host.CertifiedPublication?.Pumps??0)-pumpStart;Assert.LessOrEqual(preparationEpoch,4096,"unchanged ordinary preparation calls");
                    var before=TrafficWitness.Rows(TrafficWitness.Domain(runtime));Assert.AreEqual(4,before.Length);CollectionAssert.AreEquivalent(new long[]{-1,1},before.Select(row=>row[7]).Distinct());
                    var queued=before.Where(row=>row[6]==0).ToArray();Assert.AreEqual(2,queued.Length);replaced=(int)queued[0][0];survivor=(int)queued[1][0];replacedAge=queued[0][5];survivorAge=queued[1][5];
                    var capture=runtime.RequestCaptureBytes();yield return Until(()=>capture.IsCompleted,"capture queued current format");Assert.AreEqual(System.Threading.Tasks.TaskStatus.RanToCompletion,capture.Status,capture.Exception?.ToString());var bytes=capture.Result;
                    using(var state=PlayableWorldState.Decode(bytes)){Assert.AreEqual(PlayableWorldState.CurrentVersion,state.Version);Assert.AreEqual(c.Seed,state.Seed);Assert.AreEqual(71,state.Generation);Assert.AreEqual(c.SourceIdentity,state.SourceIdentity);Assert.Greater(state.Binding.Length,0);TestContext.WriteLine("B_LIFECYCLE_PREFLIGHT envelope="+state.Version+" seed="+state.Seed+" generation="+state.Generation+" source="+state.SourceIdentity+" map="+c.MapIdentity+" profile="+profile.ProfileId+"/"+profile.Revision+" producer="+Environment.GetEnvironmentVariable("CROWD_PASSAGE_PRODUCER"));}
                    string folder=Environment.GetEnvironmentVariable("CROWD_PASSAGE_EVIDENCE");if(!string.IsNullOrEmpty(folder)){System.IO.Directory.CreateDirectory(folder);System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,"queued-current.bin"),bytes);}
                    runtime.RequestStop();while(!runtime.IsStopped&&UnityEngine.Time.realtimeSinceStartupAsDouble<deadline)yield return null;Assert.True(runtime.IsStopped,"stop captured ordinary host within unchanged deadline");host.Dispose();runtime.Dispose();
                    runtime=PlayableRuntime.RestoreBytes(bytes,c);host=new UnityHostRouteService();yield return Until(()=>runtime.IsPrepared&&runtime.Latest.Paused,"restored current ordinary host");
                    var restored=TrafficWitness.Rows(TrafficWitness.Domain(runtime));Assert.AreEqual(before.Length,restored.Length);for(int row=0;row<before.Length;row++)CollectionAssert.AreEqual(before[row],restored[row],"exact queued traffic row after supported current restore");
                    Assert.True(ids.All(id=>runtime.Latest.Entities.Single(e=>e.Id==id).OrderStamp?.Sequence==(left.Contains(id)?leftOrder:rightOrder)),"restored current human orders");
                    trafficWitness=new TrafficWitness();trafficWitness.Attach(runtime,false);runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"resume restored host");
                    replacementOrder=sequence;yield return Apply(PlayableCommandKind.Move,new[]{replaced},new NavPoint(left.Contains(replaced)?18:-18,0));
                    yield return Until(()=>runtime.Latest.Entities.Single(e=>e.Id==replaced).OrderStamp?.Sequence==replacementOrder,"replacement current order published");
                    pumpStart=host.CertifiedPublication?.Pumps??0;TestContext.WriteLine("B_LIFECYCLE_RESTORED bytes="+bytes.Length+" replaced="+replaced+" oldAge="+replacedAge+" survivor="+survivor+" age="+survivorAge+" replacement="+replacementOrder);
                }
                bool ready=false,arrived=false;
                while(UnityEngine.Time.realtimeSinceStartupAsDouble<deadline){
                    host.Service(runtime,8);var snapshot=runtime.Latest;Assert.IsNull(snapshot.Failure,Facts());Assert.False(runtime.IsStopped,Facts());
                    if(snapshot.Tick==lastTick){yield return null;continue;}
                    if(lastTick>=0&&snapshot.Tick!=lastTick+1)gaps++;observed++;
                    var actors=snapshot.Entities.Where(e=>initial.ContainsKey(e.Id)).ToArray();Assert.AreEqual(tanks.Length,actors.Length);
                    if(!ready){preparationEpoch=(host.CertifiedPublication?.Pumps??0)-pumpStart;ready=snapshot.Metrics.NavigationPending==0;Assert.LessOrEqual(preparationEpoch,4096,"frozen preparation budget8 "+Facts());}
                    foreach(var a in actors){
                        if(blockers.Contains(a.Id)){Assert.True(a.Held);Assert.AreEqual(initial[a.Id].Position,a.Position);Assert.AreEqual(BitConverter.DoubleToInt64Bits(initial[a.Id].HullHeading),BitConverter.DoubleToInt64Bits(a.HullHeading));}
                        if(ids.Contains(a.Id)){
                            long expected=lifecycle&&a.Id==replaced?replacementOrder:left.Contains(a.Id)?leftOrder:rightOrder;Assert.NotNull(a.OrderStamp);Assert.AreEqual(expected,a.OrderStamp.Sequence);Assert.AreEqual(PlayableOrderOrigin.Human,a.OrderStamp.Origin);
                            if(!a.Position.Equals(initial[a.Id].Position)){moved.Add(a.Id);if(firstMotion<0)firstMotion=snapshot.Tick;}
                            if(a.Location?.SurfaceId=="bridge/central")crossed.Add(a.Id);
                            if(Math.Abs(a.Position.X)<=map.RiverHalfWidth)Assert.LessOrEqual(Math.Abs(a.Position.Z)+profile.TankCollisionRadius,map.CentralBridgeWidth/2+1e-8);
                        }
                        Assert.True(map.SupportsFootprint(a.Position,profile.TankCollisionRadius));
                        foreach(var b in actors.Where(b=>b.Id<a.Id)){double x=a.Position.X-b.Position.X,z=a.Position.Z-b.Position.Z;Assert.GreaterOrEqual(x*x+z*z,4*profile.TankCollisionRadius*profile.TankCollisionRadius-1e-8);}
                        if(lastTick>=0&&snapshot.Tick==lastTick+1){var before=previous.Single(e=>e.Id==a.Id);double dx=a.Position.X-before.Position.X,dz=a.Position.Z-before.Position.Z;Assert.LessOrEqual(dx*dx+dz*dz,Math.Pow(PlayableUnitRules.Speed(profile,a.Kind)/30,2)+1e-9);Assert.LessOrEqual(Math.Abs(Math.Atan2(Math.Sin(a.HullHeading-before.HullHeading),Math.Cos(a.HullHeading-before.HullHeading))),PlayableUnitRules.Turn(profile,a.Kind)/30+1e-9);Assert.True(map.SupportsSweep(before.Position,a.Position,profile.TankCollisionRadius));}
                    }
                    if(narrow){var inside=directions.Where(g=>g.Value.Any(id=>{var a=actors.Single(e=>e.Id==id);return a.Position.X+profile.TankCollisionRadius>=entry&&a.Position.X-profile.TankCollisionRadius<=exit;})).ToArray();Assert.LessOrEqual(inside.Length,1);foreach(var g in directions)if(g.Value.All(id=>{var a=actors.Single(e=>e.Id==id);return a.Position.X-profile.TankCollisionRadius>=entry&&a.Position.X+profile.TankCollisionRadius<=exit;}))followers.Add(g.Key);}
                    previous=actors;lastTick=snapshot.Tick;arrived=ids.All(id=>actors.Single(e=>e.Id==id).NavigationOutcome==NavigationOutcome.Arrived);
                    if(arrived||firstMotion>=0&&snapshot.Tick-firstMotion>=2700)break;
                    yield return null;
                }
                string terminal=Facts();TestContext.WriteLine("B_THREADED "+terminal);
                TestContext.WriteLine("B_THREAD_WORK "+Newtonsoft.Json.JsonConvert.SerializeObject(new{CallerBudget=8,ProfileQuantumBudget=profile.NavigationRequestsPerFrame,PreparationPumpCalls=preparationEpoch,host.CertifiedCounters}));
                // Capture only a failed witness, through the ordinary atomic save
                // API. The frozen motion outcome above cannot turn into a PASS
                // while pause/capture is being serviced.
                string evidence=Environment.GetEnvironmentVariable("CROWD_PASSAGE_EVIDENCE");
                if(!arrived&&!string.IsNullOrEmpty(evidence)&&UnityEngine.Time.realtimeSinceStartupAsDouble<deadline){
                    runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,"pause terminal evidence");var capture=runtime.RequestCaptureBytes();
                    while(!capture.IsCompleted&&UnityEngine.Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);yield return null;}
                    if(capture.IsCompleted&&!capture.IsFaulted&&!capture.IsCanceled){var bytes=capture.Result;System.IO.Directory.CreateDirectory(evidence);string path=System.IO.Path.Combine(evidence,narrow?"narrow-terminal.bin":"wide-terminal.bin");System.IO.File.WriteAllBytes(path,bytes);using(var hash=System.Security.Cryptography.SHA256.Create())TestContext.WriteLine("B_CAPTURE "+path+" bytes="+bytes.Length+" sha256="+BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());}
                    else TestContext.WriteLine("B_CAPTURE_NOTRUN "+capture.Exception);
                }
                if(lifecycle){
                    bool sawReplacement=false;lock(trafficWitness.Gate)foreach(var frame in trafficWitness.Frames){foreach(var row in frame.Rows){if(row[0]==survivor){Assert.AreEqual(survivorAge,row[5],"surviving queued member keeps exact FIFO age");if(row[6]>0&&survivorGrant<0)survivorGrant=frame.Tick;}if(row[0]==replaced){if(sawReplacement)Assert.AreNotEqual(replacedAge,row[5],"replaced queued order must never revive");if(row[5]!=replacedAge){sawReplacement=true;Assert.Greater(row[5],survivorAge,"replacement receives a new queue age");if(row[6]>0&&replacedGrant<0)replacedGrant=frame.Tick;}}}}
                    Assert.GreaterOrEqual(survivorGrant,0);Assert.Greater(replacedGrant,survivorGrant,"older surviving queued member is granted before replacement");TestContext.WriteLine("B_LIFECYCLE_FIFO survivorGrant="+survivorGrant+" replacementGrant="+replacedGrant);
                }
                Assert.NotNull(host.CertifiedPublication,"ordinary background worker must exist");Assert.Greater(host.CertifiedPublication.Pumps,0);Assert.True(ready,Facts());Assert.True(arrived,terminal);CollectionAssert.AreEquivalent(ids,moved);CollectionAssert.AreEquivalent(ids,crossed);if(narrow&&!lifecycle)CollectionAssert.AreEquivalent(directions.Keys,followers,"both cohorts enter before their leader clears");
            }finally{TestContext.WriteLine("B_THREADED_FINAL "+Facts());runtime.RequestStop();host.Dispose();runtime.Dispose();}
        }
        private IEnumerator RunOrdinaryPassage(bool narrow)
        {
            var c=Configuration(narrow,out double entry,out double exit,out double middle);var profile=c.Profile;
            var world=new OfflineParticipantAuthority(c,71);var nav=world.Navigation;var host=new UnityHostRouteService();int preparation=0,ticks=0;var crossed=new System.Collections.Generic.HashSet<int>();
            var ids=world.View(c.Roster[0].Id).Entities.Where(e=>e.Kind==PlayableEntityKind.Tank).Select(e=>e.Id).ToArray();var blockers=narrow?ids.Where(id=>nav.Crowd.Units.Single(u=>u.Id==id).Position.X==middle).ToArray():Array.Empty<int>();ids=ids.Except(blockers).ToArray();var left=ids.Where(id=>nav.Crowd.Units.Single(u=>u.Id==id).Position.X<0).ToArray();var right=ids.Except(left).ToArray();var map=(ThreeCrossingsMap)profile.AuthoredMap;
            var followers=new System.Collections.Generic.HashSet<long>();var groupMembers=new System.Collections.Generic.Dictionary<long,int[]>();long command=1;NavUnitSaveState[] held=null;
            try{
                if(narrow){Assert.AreEqual(8,blockers.Length);Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(71,command++,c.Roster[0].Id,PlayableCommandKind.Hold,blockers,new NavPoint(middle,0)).AsHuman()).Status);held=nav.Crowd.CaptureState().Units.Where(a=>blockers.Contains(a.Id)).ToArray();}
                Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(71,command++,c.Roster[0].Id,PlayableCommandKind.Move,left,new NavPoint(18,0)).AsHuman()).Status);
                Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(71,command++,c.Roster[0].Id,PlayableCommandKind.Move,right,new NavPoint(-18,0)).AsHuman()).Status);
                for(;preparation<4096&&nav.PendingCount>0;preparation++){host.Service(new PlayableRouteBinding(nav.Admission.Geometry,profile,nav.Admission,nav.RoutePort),8);nav.ApplyResults();if(preparation%30==0)yield return null;}
                Assert.Zero(nav.PendingCount);foreach(var group in ids.GroupBy(id=>nav.GroupFor(id).GroupId))groupMembers.Add(group.Key,group.ToArray());Assert.AreNotEqual(nav.GroupFor(left[0]).GroupId,nav.GroupFor(right[0]).GroupId);
                for(;ticks<2700&&ids.Any(id=>nav.Crowd.Units.Single(u=>u.Id==id).Outcome!=NavigationOutcome.Arrived);ticks++){
                    var before=nav.Crowd.CaptureState().Units;host.Service(new PlayableRouteBinding(nav.Admission.Geometry,profile,nav.Admission,nav.RoutePort),8);world.Step(1d/30);
                    if(narrow){var occupying=new System.Collections.Generic.HashSet<long>();foreach(var group in groupMembers){int inside=0;foreach(int id in group.Value){var a=nav.Crowd.Units.Single(u=>u.Id==id);if(a.Position.X+a.Radius>=entry&&a.Position.X-a.Radius<=exit)occupying.Add(group.Key);if(a.Position.X-a.Radius>=entry&&a.Position.X+a.Radius<=exit)inside++;}if(inside==2)followers.Add(group.Key);}Assert.LessOrEqual(occupying.Count,1);foreach(var stop in held){var a=nav.Crowd.Units.Single(u=>u.Id==stop.Id);Assert.AreEqual(stop.Position,a.Position);Assert.AreEqual(BitConverter.DoubleToInt64Bits(stop.Heading),BitConverter.DoubleToInt64Bits(a.Heading));Assert.True(a.Held);}}
                    for(int i=0;i<nav.Crowd.Units.Count;i++){
                        var a=nav.Crowd.Units[i];var b=before.Single(u=>u.Id==a.Id);double dx=a.Position.X-b.Position.X,dz=a.Position.Z-b.Position.Z;
                        Assert.LessOrEqual(dx*dx+dz*dz,b.Speed*b.Speed/900+1e-9);Assert.True(map.SupportsSweep(b.Position,a.Position,a.Radius));Assert.True(nav.Admission.Geometry.SegmentFree(b.Position,a.Position,a.Radius));
                        for(int j=0;j<i;j++){var other=nav.Crowd.Units[j];var old=before.Single(u=>u.Id==other.Id);double x=b.Position.X-old.Position.X,z=b.Position.Z-old.Position.Z,vx=dx-other.Position.X+old.Position.X,vz=dz-other.Position.Z+old.Position.Z;double q=vx*vx+vz*vz,t=q>0?Math.Max(0,Math.Min(1,-(x*vx+z*vz)/q)):0,r=a.Radius+other.Radius;Assert.GreaterOrEqual((x+vx*t)*(x+vx*t)+(z+vz*t)*(z+vz*t),r*r-1e-8);}
                        if(ids.Contains(a.Id)&&Math.Abs(a.Position.X)<=map.RiverHalfWidth){Assert.LessOrEqual(Math.Abs(a.Position.Z)+a.Radius,map.CentralBridgeWidth/2+1e-8);if(a.Location.SurfaceId=="bridge/central")crossed.Add(a.Id);}
                    }
                    if(ticks%30==0)yield return null;
                }
                if(narrow)CollectionAssert.AreEquivalent(groupMembers.Keys,followers,"both cohorts enter as staggered pairs");CollectionAssert.AreEquivalent(ids,crossed);foreach(int id in ids)Assert.AreEqual(NavigationOutcome.Arrived,nav.Crowd.Units.Single(u=>u.Id==id).Outcome);TestContext.WriteLine($"B2_EDITOR_PLAYMODE narrow={narrow},preparation={preparation},ticks={ticks},arrived={ids.Length},centralCrossed={crossed.Count},repairs={nav.Crowd.RepairCount}");
            }finally{
                host.Dispose();nav.ReleaseCommittedHostRoot();var domain=typeof(OfflineParticipantAuthority).GetProperty("Domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);domain.GetType().GetMethod("DestroyOwnedNavigation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(domain,null);Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);
            }
        }
    }
}
