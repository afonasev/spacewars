using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class AiPositionQuantumContinuationControlTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static object Read(object value,string name)
        {if(value==null)return null;for(var t=value.GetType();t!=null;t=t.BaseType){var field=t.GetField(name,F|BindingFlags.DeclaredOnly);if(field!=null)return field.GetValue(value);var property=t.GetProperty(name,F|BindingFlags.DeclaredOnly);if(property!=null)return property.GetValue(value);}throw new MissingMemberException(value.GetType().Name,name);}
        static object Call(object value,string name,params object[] args)
        {for(var t=value.GetType();t!=null;t=t.BaseType){var method=t.GetMethods(F|BindingFlags.DeclaredOnly).FirstOrDefault(m=>m.Name==name&&m.GetParameters().Length==args.Length);if(method!=null)return method.Invoke(value,args);}throw new MissingMethodException(value.GetType().Name,name);}
        static IEnumerable<object> Values(object map){if(map is IEnumerable entries)foreach(var entry in entries){var property=entry.GetType().GetProperty("Value",F);yield return property==null?entry:property.GetValue(entry);}}
        static long BankValue(object bank,int index)=>(long)Call(bank,"get_Item",index);
        static void Unpin(object value){if(value!=null)Call(value,"Release");}
        sealed class RequestRow { public long Request,Order,Group;public int Entity,Roles; }
        sealed class Sample
        {
            public string Phase,Stage,Operation,FirstCompletion,WorkerFailure,RuntimeFailure,RuntimeOutcome;
            public long PublishedTick,QuantumTick,Serial,Consumed,Prefix,Epoch,Progress,BankCount,HeadTick,HeadPrefix,WorkerPumps;
            public int PhaseUsed,Budget,CompletionCount,HostRows,HostReady,HostPending,ArrivalWork;
            public bool Runnable,Completed;public bool? WorkerWaiting,WorkerStopped;
            public RequestRow[] Requests;public object FirstCompletionRequest;public object[] TargetAttempts;
        }
        static RequestRow Row(NavigationRequest request,int role)=>new RequestRow{Request=request.Request,Order=request.Order,Entity=request.Entity,Group=request.MemberIdentity?.GroupId??0,Roles=role};
        static Sample Observe(PlayableRuntime runtime,NavigationSession nav,CertifiedRouteWorker worker,string phase)
        {
            var published=runtime.Latest;var sample=new Sample{Phase=phase,PublishedTick=published.Tick,RuntimeFailure=published.Failure,RuntimeOutcome=published.Outcome.ToString()};
            NavigationHostRoot root=null;object bank=null;
            try
            {
                // Existing authority transport gate + existing atomic pin APIs. No
                // read-root-then-retain race, no mutable pointer retained past this copy.
                using((IDisposable)typeof(NavigationSession).GetMethod("EnterTransport",F).Invoke(nav,null))
                {
                    root=nav.PinCommittedHostRoot();bank=Call(nav,"PinCreditBank");Assert.NotNull(root);
                    var requests=new Dictionary<long,RequestRow>();
                    void Add(NavigationRequest request,int role){if(request==null||request.Request<1||request.Request>111)return;if(requests.TryGetValue(request.Request,out var prior))prior.Roles|=role;else requests.Add(request.Request,Row(request,role));}
                    foreach(var request in Values(Read(nav,"pending")).OfType<NavigationRequest>())Add(request,1);
                    var allocating=Values(Read(nav,"allocating")).OfType<NavigationRequest>().ToArray();foreach(var request in allocating)Add(request,2);
                    var mailboxState=new object[]{0L,0};Call(nav.Requests,"ObserveState",mailboxState);long revision=(long)mailboxState[0];int count=(int)mailboxState[1];for(int n=0;n<count;n++)Add((NavigationRequest)Call(nav.Requests,"ReadPreparedAt",revision,n),4);
                    var attempts=Values(Read(nav,"arrivalAttempts")).OfType<NavigationSession.ArrivalAttempt>().ToArray();
                    foreach(var attempt in attempts.Where(a=>!a.Retiring&&!a.Published))foreach(var request in attempt.Intents.Where(allocating.Contains))Add(request,64);
                    sample.TargetAttempts=attempts.Where(a=>a.Intents.Any(r=>r.Request>=105&&r.Request<=111)).Select(a=>(object)new{a.Id,a.Group,a.Epoch,status=a.SnapshotStatus.ToString(),a.Published,a.Retiring,requests=a.Intents.Where(r=>r.Request>=105&&r.Request<=111).Select(r=>r.Request).ToArray()}).ToArray();
                    // All dictionaries under this gate are current; immutable root fields
                    // below belong to the safely retained SAME committed publication.
                    foreach(var entry in Values(Read(root,"Requests")))if(entry is NavigationHostRequest request)Add(request.Request,8);
                    foreach(var entry in Values(Read(root,"Rows")))if(entry is NavigationHostRow row)Add(row.Request,16);
                    foreach(var entry in Values(Read(root,"Delivered")))if(entry is NavigationHostRow row)Add(row.Request,32);
                    sample.Requests=requests.Values.OrderBy(r=>r.Request).ToArray();
                    sample.Serial=root.QuantumSerial;sample.Consumed=root.ConsumedQuantum;sample.QuantumTick=root.QuantumTick;sample.Prefix=root.AdmissionPrefix;sample.Runnable=root.QuantumRunnable;sample.Completed=root.QuantumCompleted;sample.Budget=root.QuantumBudget;
                    sample.Epoch=root.Epoch;sample.Progress=root.Progress;sample.Stage=root.Frontier.Stage.ToString();sample.PhaseUsed=root.Frontier.PhaseUsed;sample.Operation=root.Operation.Stage+":"+root.Operation.Iteration+":"+root.Operation.Entered;
                    sample.CompletionCount=(int)Read(Read(root,"Completion"),"Count");sample.HostRows=(int)Read(Read(root,"Rows"),"Count");sample.HostPending=root.Scheduler.PendingCount;sample.HostReady=root.Scheduler.ReadyCount;sample.ArrivalWork=(int)Read(Read(root,"ArrivalWork"),"Count");
                    if(sample.CompletionCount>0){var key=Read(Read(root,"Completion"),"First");sample.FirstCompletion=(string)Read(key,"Value");var found=new object[]{key,null};if((bool)Call(Read(root,"Rows"),"TryGetValue",found)&&found[1] is NavigationHostRow row)sample.FirstCompletionRequest=new{request=Row(row.Request,0),answer=row.Answer?.Status.ToString(),solved=row.Solved?.Status.ToString()};}
                    if(bank!=null){sample.BankCount=BankValue(bank,NavigationAccruedCredits.Count);if(sample.BankCount>0){int at=NavigationAccruedCredits.Header+4*(int)BankValue(bank,NavigationAccruedCredits.Head);sample.HeadTick=BankValue(bank,at);sample.HeadPrefix=BankValue(bank,at+1);}}
                }
                var publication=worker?.Publication;sample.WorkerPumps=publication?.Pumps??0;sample.WorkerFailure=publication?.Failure;sample.WorkerWaiting=worker?.IsWaiting;sample.WorkerStopped=worker?.IsStopped;
                return sample;
            }
            finally{Unpin(bank);Unpin(root);}
        }
        [UnityTest,Timeout(180000)] public IEnumerator Exact4b9NormalWorkerQuantumContinuation()
        {
            var input=Environment.GetEnvironmentVariable("AI_POSITION_CAPTURE");var source=Environment.GetEnvironmentVariable("AI_POSITION_SOURCE_SHA");Assert.IsNotEmpty(input);Assert.IsNotEmpty(source);
            var bytes=File.ReadAllBytes(input);Assert.AreEqual(4620541,bytes.Length);using(var hash=SHA256.Create())Assert.AreEqual("a07e7e94710a3dcb95e5c6495230bfce33d157e4c1268b3f03c0de7ac02c6706",BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());
            long startTick;int transportLength;using(var state=PlayableWorldState.Decode(bytes)){Assert.AreEqual(22,state.Version);Assert.AreEqual(7,state.Seed);Assert.AreEqual(107,state.Generation);Assert.AreEqual("ai-position-development-first-witness-v1",state.SourceIdentity);startTick=state.Tick;transportLength=state.RuntimeTransport.Length;}
            var output=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(input))),"continuation-4b9a-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);var phases=Stopwatch.StartNew();
            void Mark(string phase,object details=null)=>File.AppendAllText(Path.Combine(output,"phases.jsonl"),Newtonsoft.Json.JsonConvert.SerializeObject(new{phase,elapsedSeconds=phases.Elapsed.TotalSeconds,details})+"\n");
            const int seed=7;const long generation=107;
            var p=PlayableProfile.Default;var ai=NativeBalanceFields.InitialAiOpeningProfile();
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,-15),new NavPoint(-20,-27),pin:1),new OfflineStart("east",2,new NavPoint(20,15),new NavPoint(20,27),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);
            var enemy=new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p);
            var front=new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(-20,15),p);
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter};
            Assert.GreaterOrEqual(home.Slots.Count,kinds.Length);
            var buildings=kinds.Select((kind,i)=>new OfflineScenarioBuilding(1,kind,1,home.Slots[i].Id,home.Slots[i].Position,home.Slots[i].Heading)).Concat(new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Outpost,3,0,front.Position)}).ToArray();
            var units=Enumerable.Range(0,5).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-25+i*3,-1))).Concat(new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(20,-24))}).ToArray();
            var config=new OfflineMatchConfiguration(p,"ai-position-development-first-witness-v1","flat-public-position-v1","UnityHostRouteService",seed,new[]{new OfflineParticipant("position-owner",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},starts,new[]{home,enemy,front},PlayableMap.StaticObstacles(p),new double[,]{{0,100},{100,0}},scenario:units,scenarioBuildings:buildings,spectators:new[]{PlayableRuntime.LocalSpectatorId});

            Assert.AreEqual("8d2ab24c1989b3fa52b21f175a4fbfe9b29e423eae5328ae55e606b9566c64ff",ai.Hash);Assert.AreEqual("unity-owner-research-queue-u6-v1",p.ProfileId);Assert.AreEqual(1,p.Revision);Assert.AreEqual(4,p.NavigationRequestsPerFrame);
            const int capacity=128;var buffer=new Sample[capacity];int written=0;bool overflow=false,exact=false;long lastSampleTick=-1;string observationError=null;Exception prestartFailure=null;Stopwatch window=null;
            PlayableRuntime runtime=null;PlayableAuthorityTick authority=null;NavigationSession nav=null;UnityHostRouteService routes=null;CertifiedRouteWorker routeWorker=null;Sample baseline=null;
            void Add(Sample value){if(written==capacity){overflow=true;return;}buffer[written++]=value;lastSampleTick=value.PublishedTick;}
            void ObserveNow(string phase,bool force=false){if(!force&&runtime.Latest.Tick==lastSampleTick)return;try{Add(Observe(runtime,nav,routeWorker,phase));}catch(Exception error){observationError=error.ToString();}}
            try
            {
                Mark("restore-before");runtime=PlayableRuntime.RestoreBytesForTests(bytes,config,ai,r=>
                {
                    try
                    {
                        authority=(PlayableAuthorityTick)Read(r,"authorityTick");nav=authority.Domain.Navigation;Assert.AreEqual(startTick,r.Latest.Tick);
                        var roundtrip=r.CaptureBeforeStartForTests();using(var state=PlayableWorldState.Decode(roundtrip)){Assert.AreEqual(startTick,state.Tick);Assert.AreEqual(transportLength,state.RuntimeTransport.Length);}
                        exact=bytes.SequenceEqual(roundtrip);Assert.True(exact,"all Runtime bytes before normal worker continuation");
                        baseline=Observe(r,nav,null,"prestart");Assert.AreEqual(4,baseline.Budget);Add(baseline);Mark("prestart-exact",new{exact,startTick,transportLength,baseline.Stage,baseline.CompletionCount,baseline.FirstCompletion});
                        // Exact snapshot boundary above precedes the original witness's
                        // presentation pacing input. The public setter runs before Start.
                        r.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);Assert.AreEqual(4d,(double)Read(r,"spectatorSpeed"));
                        Mark("prestart-clock",new{nominalSpectatorSpeed=4,actualSpectatorSpeed=(double)Read(r,"spectatorSpeed"),exactBoundary="before-public-speed-setter",presentationChange="public setter signals presentation revision before worker.Start"});window=Stopwatch.StartNew();
                    }
                    catch(Exception error){prestartFailure=error;r.RequestStop();Mark("prestart-failure",error.ToString());}
                });
                if(prestartFailure!=null)Assert.Fail(prestartFailure.ToString());Mark("normal-start-returned");routes=new UnityHostRouteService();
                while(runtime.Latest.Tick-startTick<120&&window.Elapsed.TotalSeconds<30&&!runtime.IsStopped&&runtime.Latest.Outcome==PlayableMatchOutcome.Playing)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);routeWorker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",F).GetValue(routes);ObserveNow("normal");
                    if(overflow||observationError!=null||runtime.Latest.Failure!=null||routeWorker?.Publication.Failure!=null)break;yield return null;
                }
                ObserveNow("first-observed-bound",true);var endTick=runtime.Latest.Tick;var elapsed=window.Elapsed.TotalSeconds;
                runtime.RequestStop();routeWorker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",F).GetValue(routes);routes.Dispose();runtime.Dispose();
                bool stopped=SpinWait.SpinUntil(()=>runtime.IsStopped&&(routeWorker==null||routeWorker.IsStopped),5000);Mark("both-stopped",new{stopped,endTick,elapsed});
                var samples=buffer.Take(written).ToArray();var last=samples.LastOrDefault();
                bool progress=samples.Any(s=>s.Serial>baseline.Serial||s.Consumed>baseline.Consumed||s.Progress>baseline.Progress||s.Epoch>baseline.Epoch||s.Stage!=baseline.Stage||s.PhaseUsed!=baseline.PhaseUsed||s.HeadTick>baseline.HeadTick);
                bool targetBirth=samples.Any(s=>s.TargetAttempts.Length>0||s.Requests.Any(q=>q.Request>=105&&(q.Roles&~2)!=0));
                bool failure=samples.Any(s=>s.RuntimeFailure!=null||s.WorkerFailure!=null)||last?.RuntimeOutcome!=PlayableMatchOutcome.Playing.ToString();
                string outcome=!stopped||overflow||observationError!=null||failure?"inconclusive-interruption-or-incomplete":targetBirth?"target-birth-after-native-admission":progress?"committed-frontier-or-fifo-advancing":last?.WorkerWaiting==true&&endTick>startTick?"bounded-unchanged-waiting-frontier":"inconclusive-active-or-unobserved-work";
                File.WriteAllText(Path.Combine(output,"continuation.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,producer="4b9ab9e15dae939beea6d3330d767d1dcc9c3af2",startTick,endTick,nominalSpectatorSpeed=4,exactBoundary="before-public-speed-setter",windowSeconds=elapsed,limitTicks=120,limitWallSeconds=30,capacity,written,overflow,observationError,exact,stopped,outcome,progress,targetBirth,samples,
                    evidenceBoundary="Post-original-horizon diagnostic only; original3 ordinaryFAIL unchanged. New worker observations are contemporary continuation evidence, not historical original worker state. No submitted commands, policy/profile/cap/quota/money edits, syncPump or forced promotion/drain. Native autonomous AI continues as captured. No-progress bounded window is not deadlock proof; no full history beyond sampled plain scalars."},Newtonsoft.Json.Formatting.Indented));
                Assert.True(stopped,"both native worker ACK before destruction");Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routeWorker?.Publication.Failure);
            }
            finally
            {
                if(runtime!=null){runtime.RequestStop();if(routes!=null){routeWorker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",F).GetValue(routes)??routeWorker;routes.Dispose();}runtime.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(routeWorker==null||routeWorker.IsStopped),5000));}
                if(authority!=null){authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();}Mark("cleanup-after");
            }
        }
    }
}
