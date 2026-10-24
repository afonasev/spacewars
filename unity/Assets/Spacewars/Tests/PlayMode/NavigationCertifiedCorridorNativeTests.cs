using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.TestTools;
using Spacewars.Simulation;
using Spacewars.Runtime;
using Spacewars.Presentation;
namespace Spacewars.Tests.PlayMode
{
    public sealed class NavigationCertifiedCorridorNativeTests
    {
        private sealed class QueryRecord
        {internal long Entity;internal NavLocation Origin,Endpoint;internal ulong[] Objective,Lower;internal double Cost;internal object[] TypedLegs;internal int Legs;internal bool Anchor,UsedMacro,CertifiedPolicy,IndependentLazyPolicy;internal long Expansions,Sweeps;}
        private sealed class WorkRecord
        {
            internal long Allocation,Graph,Field,Connectors,Bookkeeping,Maintenance;internal bool Proofs;internal long Expansions,Sweeps;
            internal long Total=>Allocation+Graph+Field+Connectors+Bookkeeping+Maintenance;
        }
        private static System.Numerics.BigInteger ToInteger(ulong[] limbs){var v=System.Numerics.BigInteger.Zero;for(int i=limbs.Length-1;i>=0;i--)v=(v<<64)+limbs[i];return v;}
        private static byte[] CaptureOwnedBody(NavigationRetentionBudget budget,object root)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,500,out var scope));
            try{var r=scope.GetType().GetMethod("OwnedReservation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(scope,new object[]{null});var a=typeof(NavScheduledRequest).Assembly;var scratch=a.GetType("Spacewars.Simulation.NavStorageScratch").GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="Reserve"&&m.GetParameters()[0].ParameterType.Name=="INavStorageReservation");
                using((IDisposable)scratch.Invoke(null,new object[]{r,16L*1048576}))using(var stream=new System.IO.MemoryStream(8*1048576)){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))a.GetType("Spacewars.Simulation.NavPhysicalBodyWire").GetMethod("WriteTo",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{writer,root,r});Assert.LessOrEqual(stream.Length,32L*1048576);return stream.ToArray();}
            }finally{scope.Discard();}
        }
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Value(object target,string name)=>target.GetType().GetProperty(name,Hidden|BindingFlags.Public).GetValue(target);
        private static long Counter(object target,string name)=>(long)Value(target,name);
        private static NavArrivalInput Input(NavigationArrivalWork w)=>(NavArrivalInput)Value(w,"PhysicalInput");
        private static NavLocation Bare(NavLocation p)=>new NavLocation(p.Position,p.SurfaceId);
        private static bool Proof(NavArrivalProposal p,NavArrivalInput input)=>(bool)p.Proof.GetType().GetMethod("ValidateAgainst",Hidden).Invoke(p.Proof,new object[]{input,p.Member,p.Endpoint});
        private static void ReleasePin(NavigationHostRoot p){if(p!=null)typeof(NavScheduledRequest).Assembly.GetType("Spacewars.Simulation.NavOwnedStorage").GetMethod("Release",Hidden).Invoke(p,null);}
        private static void ReleaseWork(NavigationArrivalWork w){if(w!=null)typeof(NavigationArrivalWork).GetMethod("ReleaseOwner",Hidden).Invoke(w,null);}
        private static OfflineMatchConfiguration Configuration()
        {
            var profile=PlayableProfile.CertifiedGroupCorridorDefault;var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=19092026};setup.InitializeParticipants();foreach(var p in setup.Participants)p.Human=true;
            var b=NativeLobbyMatch.Create(profile,setup);var costs=new double[b.Starts.Count,b.Starts.Count];for(int i=0;i<b.Starts.Count;i++)for(int j=0;j<b.Starts.Count;j++)costs[i,j]=b.RouteCost(i,j);int player=b.Roster[0].LogicalPlayer;
            return new OfflineMatchConfiguration(profile,b.SourceIdentity,b.MapIdentity,b.RouteProvenance,b.Seed,b.Roster.ToArray(),b.Starts.ToArray(),b.Sites.ToArray(),b.Obstacles.ToArray(),costs,b.Spectators.ToArray(),new[]{new OfflineScenarioUnit(player,PlayableEntityKind.Tank,new NavPoint(-18,-1.8),0),new OfflineScenarioUnit(player,PlayableEntityKind.Tank,new NavPoint(-18,1.8),0)},profile.AuthoredMap.Id,b.ScenarioBuildings.ToArray());
        }
        [UnityTest,Timeout(180000)] public IEnumerator TwoTankCertifiedCorridorArrivesAndReducesCompletePaidWork()
        {
            var c=Configuration();var runtime=new PlayableRuntime(c,71,startPaused:true);var host=new UnityHostRouteService();
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            var nav=(NavigationSession)domain.GetType().GetProperty("Navigation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(domain);
            NavigationHostRoot inputPin=null;NavArrivalInput actualInput=null;NavigationArrivalWork reference=null;
            var sync=new object();QueryRecord latestAnchor=null;bool collecting=true;WorkRecord actual=null,control=null;var actualQueries=new Dictionary<long,QueryRecord>();var referenceQueries=new Dictionary<long,QueryRecord>();
            var queryPins=new Dictionary<long,object>();
            long firstMotion=-1,commandTick=-1,pumpsAtCommand=0;bool arrived=false;int prep=0;double deadline=Time.realtimeSinceStartupAsDouble+180;int[] ids=Array.Empty<int>();
            string stage="created",captureFailure=null;int recordedReferenceGrants=0;byte[] authorityBefore=null,authorityAfter=null;object cleanupCensus=null;var artifactErrors=new List<string>();
            var output=System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).Parent.FullName,".local/qa/certified-corridor/selector-accounting-ordinary");
            string Facts()=>"firstMotion="+firstMotion+",command="+commandTick+",tick="+runtime.Latest?.Tick+",prep="+prep+",actors="+string.Join(";",runtime.Latest.Entities.Where(e=>ids.Contains(e.Id)).Select(e=>e.Id+":"+e.Position+":"+e.NavigationOutcome));
            string SafeFacts(){try{return Facts();}catch(Exception error){return "facts unavailable: "+error;}}
            object Work(WorkRecord w)=>w==null?null:new{w.Allocation,w.Graph,w.Field,w.Connectors,w.Bookkeeping,w.Maintenance,w.Expansions,w.Sweeps,w.Total,w.Proofs};
            object Query(QueryRecord q)=>new{q.Entity,q.Origin,q.Endpoint,q.Cost,q.Legs,q.TypedLegs,q.Anchor,q.UsedMacro,q.CertifiedPolicy,q.IndependentLazyPolicy,q.Expansions,q.Sweeps,Exact=q.Objective.Select(v=>v.ToString("x16")).ToArray(),Lower=q.Lower.Select(v=>v.ToString("x16")).ToArray(),LowerMeaning=q.UsedMacro?"certified full independent cut":"exact objective for donor/fallback, not a rejected-cut LB"};
            void Flush(string marker,bool protect=false)
            {
                stage=marker;
                try{
                    System.IO.Directory.CreateDirectory(output);object data;
                    lock(sync){if(actual!=null&&latestAnchor!=null){actual.Expansions=latestAnchor.Expansions+actualQueries.Values.Where(q=>!q.Anchor).Sum(q=>q.Expansions);actual.Sweeps=latestAnchor.Sweeps+actualQueries.Values.Where(q=>!q.Anchor).Sum(q=>q.Sweeps);}
                        data=new{Stage=stage,Facts=SafeFacts(),CaptureFailure=captureFailure,arrived,firstMotion,commandTick,prep,ReferenceCreated=reference!=null,ReferenceStatus=reference?.Status.ToString(),ReferenceGrants=recordedReferenceGrants,Actual=Work(actual),Control=Work(control),ActualQueryRecords=actualQueries.Values.Select(Query).ToArray(),ReferenceQueryRecords=referenceQueries.Values.Select(Query).ToArray(),Input=actualInput==null?null:new{actualInput.StableKey,actualInput.TopologyIdentity,actualInput.ProfileIdentity,Provider=actualInput.Provider.Binding,GraphProfile=actualInput.Profile.ExactProfileBinding},Receipts=runtime.ReceiptsAfter(0).Select(r=>new{r.Sequence,r.Status,r.AppliedTick}).ToArray(),CleanupCensus=cleanupCensus,ArtifactErrors=artifactErrors.ToArray()};}
                    System.IO.File.WriteAllText(System.IO.Path.Combine(output,"primitive-records.json"),JsonConvert.SerializeObject(data,Formatting.Indented));
                    if(authorityBefore!=null)System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"authority-before.world"),authorityBefore);if(authorityAfter!=null)System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"authority-after.world"),authorityAfter);
                }catch(Exception error){artifactErrors.Add(marker+": "+error);TestContext.WriteLine("A2_ARTIFACT_FLUSH_FAILURE "+marker+" "+error);if(!protect)throw;}
            }
            byte[] CaptureBody(object root){try{return CaptureOwnedBody(nav.PhysicalHostBudget,root);}catch(Exception error){captureFailure=error.ToString();throw;}}
            var arrivalObserver=typeof(NavigationArrivalWork).GetField("CertifiedArrivalReadyObserver",BindingFlags.Static|BindingFlags.NonPublic);var queryObserver=typeof(LayeredNavigationScheduler).GetField("CertifiedQueryReadyObserver",BindingFlags.Static|BindingFlags.NonPublic);
            Action<NavigationArrivalWork> onArrival=w=>{
                lock(sync){if(!collecting||actual!=null)return;var x=w.RouteCounters;actual=new WorkRecord{Allocation=w.AllocationWorkConsumed,Graph=x.PhysicalGraphWork,Field=x.PhysicalFieldWork,Connectors=x.ConnectorWork,Bookkeeping=Counter(w,"CertifiedBookkeeping"),Maintenance=x.MaintenanceWork,Proofs=w.Proposals.All(p=>Proof(p,Input(w)))};}
            };
            Action<NavScheduledRequest,object> onQuery=(r,w)=>{
                var exact=Value(w,"PublishedObjective");var lower=Value(w,"PublishedLower");var limb=exact.GetType().GetMethod("Limb",Hidden);var typed=(NavTypedRoute)Value(w,"Route");var q=new QueryRecord{Entity=r.Subscription.EntityId,Origin=Bare(r.Subscription.Origin),Endpoint=Bare(r.Subscription.AssignedEndpoint),Objective=Enumerable.Range(0,34).Select(i=>(ulong)limb.Invoke(exact,new object[]{i})).ToArray(),Lower=Enumerable.Range(0,34).Select(i=>(ulong)limb.Invoke(lower,new object[]{i})).ToArray(),Cost=typed.Cost,Legs=typed.Legs.Count,TypedLegs=typed.Legs.Select(l=>(object)new{l.From,l.To,l.Cost,l.Kind,l.PortalId}).ToArray(),Anchor=r.Subscription.EntityId==5,CertifiedPolicy=r.CertifiedGroupCorridor,IndependentLazyPolicy=r.LazyPartition&&!r.CertifiedLazyPartition,UsedMacro=(bool)Value(w,"UsedCertifiedCorridor"),Expansions=Counter(w,"ExactExpansions"),Sweeps=Counter(w,"ExactTerminalSweeps")};lock(sync){if(collecting){w.GetType().GetMethod("Seal",Hidden).Invoke(w,null);typeof(NavScheduledRequest).Assembly.GetType("Spacewars.Simulation.NavOwnedStorage").GetMethod("Retain",Hidden).Invoke(w,null);queryPins[q.Entity]=w;}if(collecting&&q.Anchor)latestAnchor=q;(collecting?actualQueries:referenceQueries)[q.Entity]=q;}
            };
            arrivalObserver.SetValue(null,onArrival);queryObserver.SetValue(null,Delegate.CreateDelegate(queryObserver.FieldType,onQuery.Target,onQuery.Method));
            IEnumerator Until(Func<bool> ready,string phase)
            {while(!ready()&&Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);Assert.IsNull(runtime.Latest.Failure,phase+" "+Facts());Assert.False(runtime.IsStopped);yield return null;}Assert.True(ready(),phase+" "+Facts());}
            try{
                stage="startup";yield return Until(()=>runtime.IsPrepared&&runtime.Latest.Sequence>0,"startup");
                ids=runtime.Latest.Entities.Where(e=>e.Kind==PlayableEntityKind.Tank).Select(e=>e.Id).OrderBy(x=>x).ToArray();Assert.AreEqual(2,ids.Length);
                var starts=runtime.Latest.Entities.Where(e=>ids.Contains(e.Id)).ToDictionary(e=>e.Id,e=>e.Position);Assert.AreEqual(new NavPoint(-18,-1.8),starts[ids[0]]);Assert.AreEqual(new NavPoint(-18,1.8),starts[ids[1]]);
                runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"resume");pumpsAtCommand=host.CertifiedPublication?.Pumps??0;
                Assert.True(runtime.TrySubmit(new PlayableCommand(71,1,c.Roster[0].Id,PlayableCommandKind.Move,ids,new NavPoint(36,46))).Accepted);
                yield return Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==1&&r.Status==PlayableCommandStatus.Applied),"ordinary command");commandTick=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==1&&r.Status==PlayableCommandStatus.Applied).AppliedTick;
                stage="ordinary-command-applied";while(Time.realtimeSinceStartupAsDouble<deadline){
                    host.Service(runtime,8);var snapshot=runtime.Latest;Assert.IsNull(snapshot.Failure,Facts());Assert.False(runtime.IsStopped,Facts());
                    if(inputPin==null){var root=nav.PinCommittedHostRoot();if(root!=null)try{foreach(var row in (IEnumerable)Value(root,"ArrivalWork")){var w=(NavigationArrivalWork)row.GetType().GetProperty("Value").GetValue(row);if(Input(w).Members.Select(m=>m.EntityId).OrderBy(x=>x).SequenceEqual(ids.Select(x=>(long)x))){inputPin=root;actualInput=Input(w);break;}}}finally{if(!ReferenceEquals(inputPin,root))ReleasePin(root);}}
                    prep=(int)((host.CertifiedPublication?.Pumps??0)-pumpsAtCommand);if(snapshot.Metrics.NavigationPending>0)Assert.LessOrEqual(prep,4096,"original preparation budget8 "+Facts());
                    lock(sync){if(actualQueries.Count>0)Assert.True(actualQueries.Values.All(q=>q.CertifiedPolicy),"actual opt-in request policy before movement observation; no lazy downgrade");}
                    var actors=snapshot.Entities.Where(e=>ids.Contains(e.Id)).ToArray();Assert.AreEqual(2,actors.Length);
                    foreach(var actor in actors){if(!actor.Position.Equals(starts[actor.Id])&&firstMotion<0)firstMotion=snapshot.Tick;if(actor.OrderStamp!=null)Assert.AreEqual(1,actor.OrderStamp.Sequence,"original order identity");}
                    arrived=actors.All(e=>e.NavigationOutcome==NavigationOutcome.Arrived);if(arrived||firstMotion>=0&&snapshot.Tick-firstMotion>=2700)break;yield return null;
                }
                stage="game-observation-ended";Assert.True(arrived,"original 2700 motion ticks / 180s "+Facts());Assert.GreaterOrEqual(firstMotion,commandTick);Assert.NotNull(actualInput,"pin actual immutable authority input before completion");
                lock(sync){Assert.NotNull(actual);Assert.True(actual.Proofs);Assert.AreEqual(2,actualQueries.Count);collecting=false;}
                // Same exact immutable authority input, normal current independent
                // A* plus its existing catalog sharing. Its new saved fallback mode
                // disables only the new frontier/suffix optimization. No new world.
                stage="stop-owners-before-reference";var stoppedWorker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",Hidden).GetValue(host);runtime.Dispose();host.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(stoppedWorker==null||stoppedWorker.IsStopped),5000));
                authorityBefore=domain.CaptureWorldBytes(c.Seed,c.SourceIdentity);
                Flush("primitives-before-input-body");
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"actual-input.body"),CaptureBody(Value(actualInput,"StorageRoot")));
                foreach(var pin in queryPins){Flush("before-query-body-"+pin.Key);System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"query-"+pin.Key+".body"),CaptureBody(pin.Value));}
                System.IO.File.WriteAllText(System.IO.Path.Combine(output,"typed-routes.json"),JsonConvert.SerializeObject(actualQueries.Values.Select(q=>new{q.Entity,q.Origin,q.Endpoint,q.Cost,q.TypedLegs,q.UsedMacro,Exact=q.Objective.Select(x=>x.ToString("x16")).ToArray(),Lower=q.Lower.Select(x=>x.ToString("x16")).ToArray()}),Formatting.Indented));
                Flush("all-body-written-before-reference");reference=(NavigationArrivalWork)typeof(NavigationArrivalWork).GetMethod("CertifiedIndependentReference",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{actualInput});int grants=0;
                while(reference.Status!=NavArrivalStatus.Ready){Assert.Less(grants,4096);Assert.Less(Time.realtimeSinceStartupAsDouble,deadline,"original total harness bound");reference.Advance(grants++,256,new NavEpochQuota(256,256,256,256,256));recordedReferenceGrants=grants;Assert.True(reference.Status==NavArrivalStatus.Pending||reference.Status==NavArrivalStatus.BudgetExhausted||reference.Status==NavArrivalStatus.Ready,reference.Status.ToString());}
                var rc=reference.RouteCounters;control=new WorkRecord{Allocation=reference.AllocationWorkConsumed,Graph=rc.PhysicalGraphWork,Field=rc.PhysicalFieldWork,Connectors=rc.ConnectorWork,Bookkeeping=Counter(reference,"CertifiedBookkeeping"),Maintenance=rc.MaintenanceWork,Proofs=reference.Proposals.All(p=>Proof(p,actualInput))};
                lock(sync){Assert.NotNull(latestAnchor);actual.Expansions=latestAnchor.Expansions+actualQueries.Values.Where(q=>!q.Anchor).Sum(q=>q.Expansions);actual.Sweeps=latestAnchor.Sweeps+actualQueries.Values.Where(q=>!q.Anchor).Sum(q=>q.Sweeps);control.Expansions=referenceQueries.Values.Sum(q=>q.Expansions);control.Sweeps=referenceQueries.Values.Sum(q=>q.Sweeps);}
                authorityAfter=domain.CaptureWorldBytes(c.Seed,c.SourceIdentity);Flush("reference-ready-before-authority-assertion");
                CollectionAssert.AreEqual(authorityBefore,authorityAfter,"private reference must preserve authority bytes");
                Assert.AreEqual(2,referenceQueries.Count);Assert.True(referenceQueries.Values.All(q=>q.IndependentLazyPolicy&&!q.CertifiedPolicy),"reference retains original independent V7 policy on the same input");Assert.True(control.Proofs);foreach(var id in ids){var a=actualQueries[id];var b=referenceQueries[id];Assert.AreEqual(b.Origin,a.Origin);Assert.AreEqual(b.Endpoint,a.Endpoint,"same allocator endpoint");if(a.UsedMacro){Assert.LessOrEqual(ToInteger(a.Lower),ToInteger(b.Objective),"independent full-objective LB");Assert.LessOrEqual(100*ToInteger(a.Objective),115*ToInteger(a.Lower),"sound certificate");Assert.LessOrEqual(100*ToInteger(a.Objective),115*ToInteger(b.Objective),"actual original Opt quality ceiling");}else CollectionAssert.AreEqual(b.Objective,a.Objective,"exact donor/fallback objective");}
                object Report(WorkRecord w)=>new{w.Allocation,w.Graph,w.Field,w.Connectors,w.Bookkeeping,w.Maintenance,w.Expansions,w.Sweeps,w.Total,w.Proofs};
                TestContext.WriteLine("A2_CERTIFIED_CORRIDOR_GAME "+JsonConvert.SerializeObject(new{Facts=Facts(),arrived,firstMotion,commandTick,prep,actual=Report(actual),reference=Report(control),referenceGrants=grants,queries=actualQueries.Values.Select(q=>new{q.Entity,q.Origin,q.Endpoint,q.Cost,q.Legs,q.Anchor,q.UsedMacro,q.CertifiedPolicy,q.IndependentLazyPolicy,q.Expansions,q.Sweeps,exact=q.Objective.Select(x=>x.ToString("x16")).ToArray()})}));
                Flush("quality-checked-before-shared-work-gates");Assert.True(actualQueries[ids[1]].UsedMacro,"the second member final route consumes its endpoint-specific suffix");Assert.Less(actual.Expansions,control.Expansions,"outgoing graph expansions reduced");Assert.Less(actual.Sweeps,control.Sweeps,"actual node-to-terminal sweep invocations reduced");
                Assert.Less(actual.Graph,control.Graph,"repeated graph work reduced");Assert.Less(actual.Connectors,control.Connectors,"terminal and connector work including macro lookup reduced");Assert.Less(actual.Total,control.Total,"all paid group work including anchor, retarget, lookup/extraction/fallback");
                Flush("all-original-assertions-passed");
            }finally{
                Flush("finally-before-pin-release",true);
                TestContext.WriteLine("A2_CERTIFIED_CORRIDOR_FINAL "+SafeFacts()+" failure="+runtime.Latest?.Failure);
                try{arrivalObserver.SetValue(null,null);queryObserver.SetValue(null,null);ReleaseWork(reference);foreach(var pin in queryPins.Values)typeof(NavScheduledRequest).Assembly.GetType("Spacewars.Simulation.NavOwnedStorage").GetMethod("Release",Hidden).Invoke(pin,null);queryPins.Clear();ReleasePin(inputPin);
                var worker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);runtime.Dispose();host.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000));nav.ReleaseCommittedHostRoot();domain.GetType().GetMethod("DestroyOwnedNavigation",Hidden).Invoke(domain,null);Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);}
                finally{cleanupCensus=new{nav.PhysicalHostBudget.PhysicalBytes,nav.PhysicalHostBudget.HeldBytes,nav.PhysicalHostBudget.ActiveScopes};Flush("finally-after-owned-cleanup",true);}
            }
        }
    }
}
