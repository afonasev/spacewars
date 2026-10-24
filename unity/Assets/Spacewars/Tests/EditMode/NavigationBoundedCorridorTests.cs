using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Newtonsoft.Json;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static NavScheduledRequest BoundedRequest(int entity=1,bool bounded=true,string hold=null)
        {
            var r=CertifiedTraceRequest();var s=r.Subscription;var from=new NavLocation(new NavPoint(s.Origin.Position.X,s.Origin.Position.Z+(entity-1)*.125),s.Origin.SurfaceId);
            var end=r.Region.Candidates[entity==1?0:1];var q=new NavRouteSubscription(s.WorldGeneration,s.GroupId,s.RootOrderRevision,s.CommandSequence,s.IssuedTick,s.ActivationRevision,entity,s.Incarnation,s.MemberOrderRevision,s.MobilityRevision,s.RequestSequence,from,end,hold);
            return new NavScheduledRequest(r.Provider,r.Clearance,r.Profile,r.Limits,r.Region,q,r.MaxLabels,query:true,groupQuery:true,demandQuery:true,lazyPartition:true,certifiedLazyPartition:true,rectangularOrigins:true,terminalRectangleProof:true,terminalCornerProof:true,originShadowProof:true,terminalCostProof:true,certifiedGroupCorridor:bounded);
        }
        // One fixed positive configuration authorized from the retained node10/P.
        // No coordinate search, snapping or alternate geometry is permitted.
        private static NavScheduledRequest PositiveBoundedRequest(int entity=1,bool bounded=true,string hold=null)
        {
            var r=BoundedRequest(entity,bounded,hold);var old=r.Subscription;var p=new NavLocation(new NavPoint(-48,-48),"deck/-1/-1");double radius=r.Clearance.Radius;
            Assert.AreEqual(.25,radius,"fixed source-bound radius");var from=new NavLocation(new NavPoint(p.Position.X-2*radius,p.Position.Z+(entity==1?0:radius/2)),p.SurfaceId);
            Assert.True(r.Provider.IsValid(p,r.Clearance),"fixed P must be occupiable");Assert.True(r.Provider.IsValid(from,r.Clearance),"fixed premise: origin must be occupiable; STOP, no coordinate repair");Assert.True(r.Provider.CanSweep(from,p,r.Clearance),"fixed premise: actual typed origin-to-P sweep; STOP, no coordinate repair");
            var q=new NavRouteSubscription(old.WorldGeneration,old.GroupId,old.RootOrderRevision,old.CommandSequence,old.IssuedTick,old.ActivationRevision,old.EntityId,old.Incarnation,old.MemberOrderRevision,old.MobilityRevision,old.RequestSequence,from,old.AssignedEndpoint,old.HoldBinding,old.Provenance);
            return new NavScheduledRequest(r.Provider,r.Clearance,r.Profile,r.Limits,r.Region,q,r.MaxLabels,query:r.Query,groupQuery:r.GroupQuery,demandQuery:r.DemandQuery,demandQueryV2:r.DemandQueryV2,demandQueryV3:r.DemandQueryV3,demandQueryV5:r.DemandQueryV5,demandQueryV6:r.DemandQueryV6,lazyPartition:r.LazyPartition,certifiedLazyPartition:r.CertifiedLazyPartition,rectangularOrigins:r.RectangularOrigins,terminalRectangleProof:r.TerminalRectangleProof,terminalCornerProof:r.TerminalCornerProof,originShadowProof:r.OriginShadowProof,terminalCostProof:r.TerminalCostProof,certifiedGroupCorridor:r.CertifiedGroupCorridor);
        }
        private static void PositiveDonorPremise(NavDemandQueryWork d)
        {
            var chain=(NavPagedList<NavGraphArc>)LazyField(d,"chain");var node=typeof(NavDemandQueryWork).GetMethod("NodeLocation",BindingFlags.Instance|BindingFlags.NonPublic);var p=new NavPoint(-48,-48);
            Assert.True(chain.Any(a=>((NavLocation)node.Invoke(d,new object[]{a.From})).Position.Equals(p)),"fixed P must be donor path boundary with nonempty suffix; STOP, no geometry search");Assert.AreNotEqual(p,d.Route.Subscription.Origin.Position,"fixed P cannot be the donor old assembly origin");
        }
        private static object BoundedLeanState(NavDemandQueryWork w)
        {
            string[] Cost(NavExactCost c)=>Enumerable.Range(0,34).Select(i=>c.Limb(i).ToString("x16")).ToArray();var open=(NavCowOrderedSet<NavExactQueueEntry>)LazyField(w,"exactFrontier");int chosen=(int)LazyField(w,"chosen");var q=(NavExactCost)LazyField(w,"exactBest");var lower=open.Count>0?open.Min.Cost:default;if(open.Count>0&&chosen>=0&&q<lower)lower=q;
            return new{Status=w.Status.ToString(),Phase=LazyField(w,"phase").ToString(),Current=LazyField(w,"current"),Pending=LazyField(w,"pending"),Scan=LazyField(w,"corridorScan"),ScanComplete=LazyField(w,"corridorScanned"),Boundary=LazyField(w,"corridorBoundary"),Node=LazyField(w,"corridorNode"),Checks=LazyField(w,"corridorChecks"),Selected=LazyField(w,"boundedSelected"),Upper=Cost((NavExactCost)LazyField(w,"corridorUpper")),OpenCount=open.Count,MinOpen=open.Count>0?Cost(open.Min.Cost):null,IndependentChosen=chosen,IndependentIncumbent=chosen>=0?Cost(q):null,SourceDerivedLower=open.Count>0?Cost(lower):null,LowerKind="SOURCE-DERIVED min from captured OPEN/Q; not method local"};
        }
        private static object BoundedFullState(NavDemandQueryWork w)
        {
            if(w==null)return new{NotRetained="query not created"};string[] Cost(NavExactCost c)=>Enumerable.Range(0,34).Select(i=>c.Limb(i).ToString("x16")).ToArray();object Point(NavLocation n)=>new{n.Position.X,n.Position.Z,n.SurfaceId};object Leg(NavTypedLeg l)=>new{From=Point(l.From),To=Point(l.To),l.Cost,l.Kind,l.PortalId};var r=(NavScheduledRequest)LazyField(w,"request");var donor=(NavDemandQueryWork)LazyField(w,"corridorSource");
            return new{Request=new{r.ArtifactKey,r.CompileKey,r.GraphKey,Provider=r.Provider.Binding,r.CertifiedGroupCorridor,Origin=Point(r.Subscription.Origin),Endpoint=Point(r.Subscription.AssignedEndpoint),r.Region.StableKey,Candidates=r.Region.Candidates.Select(Point).ToArray()},State=BoundedLeanState(w),Starts=((NavCowDictionary<int,double>)LazyField(w,"starts")).ToArray(),Ends=((NavCowDictionary<int,double>)LazyField(w,"ends")).ToArray(),Ga=((NavCowDictionary<int,NavExactCost>)LazyField(w,"exactDistance")).Select(x=>new{Node=x.Key,Cost=Cost(x.Value)}).ToArray(),Open=((NavCowOrderedSet<NavExactQueueEntry>)LazyField(w,"exactFrontier")).Select(x=>new{x.Node,Cost=Cost(x.Cost)}).ToArray(),ReverseChain=((NavPagedList<NavGraphArc>)LazyField(w,"chain")).Select(a=>new{a.From,a.To,a.Cost,a.Kind,a.PortalId,Trace=a.Trace?.Select(Leg).ToArray()}).ToArray(),Legs=w.Route?.Legs.Select(Leg).ToArray(),Donor=donor==null?null:BoundedFullState(donor)};
        }
        private static void BoundedTraceStep(NavDemandQueryWork w,System.Collections.Generic.List<object> trace)
        {
            var before=BoundedLeanState(w);var key=LazyField(w,"phase").ToString()+":"+LazyField(w,"corridorScan")+":"+LazyField(w,"corridorChecks");V3Step(w);var afterKey=LazyField(w,"phase").ToString()+":"+LazyField(w,"corridorScan")+":"+LazyField(w,"corridorChecks");if(key!=afterKey){Assert.Less(trace.Count,2048);trace.Add(new{Before=before,After=BoundedLeanState(w)});}
        }
        private static void BoundedWriteArtifacts(string name,object data,System.Collections.Generic.Dictionary<string,byte[]> bodies)
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_CORRIDOR_PROBE_OUTPUT");if(string.IsNullOrWhiteSpace(output))throw new InvalidOperationException("SPACEWARS_CORRIDOR_PROBE_OUTPUT is required.");var dir=System.IO.Path.GetFullPath(output);System.IO.Directory.CreateDirectory(dir);using(var file=System.IO.File.Create(System.IO.Path.Combine(dir,name+".zip")))using(var zip=new System.IO.Compression.ZipArchive(file,System.IO.Compression.ZipArchiveMode.Create)){using(var writer=new System.IO.StreamWriter(zip.CreateEntry("transcript.json").Open()))writer.Write(JsonConvert.SerializeObject(data,Formatting.Indented));foreach(var body in bodies)using(var stream=zip.CreateEntry(body.Key).Open())stream.Write(body.Value,0,body.Value.Length);}
        }
        private static void BoundedFinish(NavDemandQueryWork w){for(int n=0;w.Status==NavSolveStatus.Pending;n++){Assert.Less(n,10000);V3Step(w);}Assert.AreEqual(NavSolveStatus.Ready,w.Status);}
        [Test] public void BoundedRatioExactBoundaryZeroAndOverflow()
        {
            var one=NavExactCost.FromDouble(1);var u=NavExactCost.FromDouble(115);var l=NavExactCost.FromDouble(100);
            Assert.True(NavDemandQueryWork.CertifiedRatio(u,l));Assert.False(NavDemandQueryWork.CertifiedRatio(u.Add(double.Epsilon),l));Assert.False(NavDemandQueryWork.CertifiedRatio(default,default));Assert.True(NavDemandQueryWork.CertifiedRatio(default,one));
            var high=NavExactCost.FromDouble(double.MaxValue);for(int i=0;i<77;i++)high=high.Add(high);Assert.False(NavDemandQueryWork.CertifiedRatio(high,high));
            Assert.LessOrEqual(NavStorageLayout.ObjectBytesBound(typeof(NavDemandQueryWork)),2048,"old query layout contract remains unchanged");
        }
        [Test,Timeout(120000)] public void BoundedFullOriginalObjectiveAndForwardSuffix()=>BoundedAdmissionWithDiagnostics(true,false);
        [Test,Timeout(120000)] public void BoundedOriginalInputHasNoForwardJoinAndUsesExactFallback()=>BoundedAdmissionWithDiagnostics(false,true);
        [Test] public void BoundedIncompatibleHoldFallsBackToExact()
        {
            var d=new NavDemandQueryWork(PositiveBoundedRequest());var q=new NavDemandQueryWork(PositiveBoundedRequest(2,true,"different-hold"));var exact=new NavDemandQueryWork(PositiveBoundedRequest(2,false,"different-hold"));
            try{BoundedFinish(d);q.AttachCorridor(d);BoundedFinish(q);BoundedFinish(exact);Assert.False(q.UsedCertifiedCorridor);Assert.AreEqual(exact.ExactObjective,q.ExactObjective);}finally{q.ReleaseOwner();d.ReleaseOwner();exact.ReleaseOwner();}
        }
        [Test,Timeout(120000)] public void BoundedSchedulerCancellationColdResumeAndIndependentGroupsRetireTheirDonor()
        {
            var scheduler=new LayeredNavigationScheduler();scheduler.EnableCertifiedAccounting();scheduler.BindAttemptArtifacts(new string('c',64));
            var completed=new System.Collections.Generic.List<string>();var shared=new System.Collections.Generic.List<string>();long epoch=0;
            Exception primary=null;string stage="created";var previousObserver=LayeredNavigationScheduler.CertifiedQueryReadyObserver;
            LayeredNavigationScheduler.CertifiedQueryReadyObserver=(request,value)=>{
                previousObserver?.Invoke(request,value);var work=(NavDemandQueryWork)value;
                string key=request.Subscription.GroupId+":"+request.Subscription.EntityId;completed.Add(key);
                if(work.UsedCertifiedCorridor){Assert.True(NavDemandQueryWork.CertifiedRatio(work.PublishedObjective,work.PublishedLower));shared.Add(key);}
            };
            NavScheduledRequest Recipe(long group,int entity)
            {
                var r=PositiveBoundedRequest(entity);var q=r.Subscription;
                var sub=new NavRouteSubscription(q.WorldGeneration,group,group,group,q.IssuedTick,group,entity,q.Incarnation,group,q.MobilityRevision,q.RequestSequence,q.Origin,q.AssignedEndpoint,q.HoldBinding);
                return new NavScheduledRequest(r.Provider,r.Clearance,r.Profile,r.Limits,r.Region,sub,r.MaxLabels,query:true,groupQuery:true,demandQuery:true,lazyPartition:true,certifiedLazyPartition:true,rectangularOrigins:true,terminalRectangleProof:true,terminalCornerProof:true,originShadowProof:true,terminalCostProof:true,certifiedGroupCorridor:true);
            }
            NavScheduledCompletion Next()
            {
                for(int n=0;n<10000;n++){
                    if(scheduler.TryTake(_=>true,out var ready))return ready;
                    scheduler.Advance(epoch++,new NavEpochQuota(256,256,256,256,256),_=>true,1);
                    Assert.LessOrEqual(scheduler.RetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                }
                TestContext.WriteLine("A23_STALLED "+JsonConvert.SerializeObject(new{epoch,scheduler.PendingCount,scheduler.ReadyCount,scheduler.CertifiedBookkeeping,scheduler.RemainingQuota,Checkpoint=scheduler.Save()}));
                Assert.Fail("Fixed certified scheduler request must finish within its bounded grants.");return null;
            }
            try{
                for(long group=1;group<=2;group++){
                    Assert.Null(LazyField(scheduler,"corridorDonor"),"previous independent group must not pin its donor");
                    var requests=new[]{Recipe(group,1),Recipe(group,2),Recipe(group,3)};
                    foreach(var request in requests)Assert.True(scheduler.Submit(request));
                    stage="first-result";var first=Next();try{Assert.AreEqual(NavSolveStatus.Ready,first.Status);Assert.AreEqual(requests[0].Subscription.StableKey,first.Subscription.StableKey);}finally{first.ReleaseOwner();}
                    var donor=(NavDemandQueryWork)LazyField(scheduler,"corridorDonor");Assert.NotNull(donor);Assert.AreEqual(group,donor.BoundedRecipe.Subscription.GroupId);
                    stage="cancel";Assert.True(scheduler.Cancel(requests[2].Subscription.StableKey));Assert.False(scheduler.Cancel(requests[2].Subscription.StableKey));
                    long paid=scheduler.CertifiedBookkeeping;string prefix=scheduler.LogicalContinuationBinding;
                    stage="restore";var cold=LayeredNavigationScheduler.Restore(scheduler.Save(),requests);
                    try{Assert.AreEqual(prefix,cold.LogicalContinuationBinding);Assert.AreEqual(paid,cold.CertifiedBookkeeping);}catch{cold.ClearWorld();throw;}
                    scheduler.ClearWorld();scheduler=cold;
                    stage="second-result";var second=Next();try{Assert.AreEqual(NavSolveStatus.Ready,second.Status);Assert.AreEqual(requests[1].Subscription.StableKey,second.Subscription.StableKey);}finally{second.ReleaseOwner();}
                    Assert.Zero(scheduler.PendingCount);Assert.Zero(scheduler.ReadyCount);Assert.Contains(group+":2",shared,"surviving member must use its group's nonempty certified suffix");
                    stage="teardown";long before=scheduler.CertifiedBookkeeping;Assert.True(scheduler.EndAttemptArtifacts(epoch++,new NavEpochQuota(256,256,256,256,256)));
                    Assert.AreEqual(before+1,scheduler.CertifiedBookkeeping,"retire the real catalog/donor with one paid teardown");Assert.Null(LazyField(scheduler,"corridorDonor"));
                    Assert.Zero(scheduler.Counters.CacheFields);Assert.Zero(scheduler.ProviderRetainedBytes);
                }
                CollectionAssert.AreEqual(new[]{"1:1","1:2","2:1","2:2"},completed,"FIFO and cancelled subscribers stay exact across current restore");
                CollectionAssert.AreEqual(new[]{"1:2","2:2"},shared);Assert.AreEqual(2,scheduler.Counters.Cancelled);
                TestContext.WriteLine("A23_CERTIFIED_SCHEDULER "+JsonConvert.SerializeObject(new{completed,shared,Paid=scheduler.CertifiedBookkeeping,scheduler.Counters.PeakRetainedBytes}));
            }catch(Exception error){primary=error;TestContext.WriteLine("A23_PRIMARY "+stage+" "+error);throw;}
            finally{
                LayeredNavigationScheduler.CertifiedQueryReadyObserver=previousObserver;
                try{if(primary==null){scheduler.ClearWorld();Assert.Zero(scheduler.RetainedBytes);}else scheduler.ReleaseStorage();}
                catch(Exception cleanup){if(primary==null)throw;TestContext.WriteLine("A23_SECONDARY "+cleanup);}
            }
        }
        [Test] public void BoundedProfileFixedMetadataRoundtripAndExactDefault()
        {
            var p=PlayableProfile.CertifiedGroupCorridorDefault;Assert.AreEqual(15,p.Navigation.GroupCorridorMaximumExcessPercent);Assert.AreEqual(0,PlayableProfile.ThreeCrossingsDefault.Navigation.GroupCorridorMaximumExcessPercent);
            var meta=PlayableProfileMetadata.Fields.Single(f=>f.Path==NavigationProfile.GroupCorridorMetadata.Path);Assert.AreEqual(15,meta.Minimum);Assert.AreEqual(15,meta.Maximum);Assert.AreEqual(1,meta.Step);var copy=p.CopyData();Assert.Throws<ArgumentOutOfRangeException>(()=>meta.Write(copy,14));Assert.Throws<ArgumentOutOfRangeException>(()=>meta.Write(copy,double.NaN));meta.Write(copy,15);
            var json=JsonConvert.SerializeObject(copy);var loaded=PlayableProfile.Create(JsonConvert.DeserializeObject<PlayableProfileData>(json),ThreeCrossingsMap.Default);Assert.AreEqual(NavigationAdmission.ExactProfileBinding(p.Navigation),NavigationAdmission.ExactProfileBinding(loaded.Navigation));
            Assert.True(p.Navigation.ForUnit(.5,3,4).MatchesWireValues(NavigationProfile.FromWireValues(p.Navigation.ProfileId,p.Navigation.Revision,p.Navigation.CaptureWireValues()).ForUnit(.5,3,4).CaptureWireValues()));
        }
        [TestCase("donor")][TestCase("scan")][TestCase("cut")][TestCase("reconstruct")]
        public void BoundedCurrentColdNextCowAliasAndRefusal(string target)
        {
            var hot=new NavigationRetentionBudget();var cold=new NavigationRetentionBudget();NavDemandQueryWork root=null,donor=null,restored=null;NavScheduledRequest raw=null;var trace=new System.Collections.Generic.List<object>();var bodies=new System.Collections.Generic.Dictionary<string,byte[]>();Exception primary=null;object failedHot=null,failedCold=null;var errors=new System.Collections.Generic.List<string>();
            try{
                raw=PositiveBoundedRequest(target=="donor"?1:2);Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var birth));using(birth){var r=birth.OwnedReservation();var provider=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,r);var dq=NavScheduledRequest.CopyOwned(PositiveBoundedRequest(),provider,r);donor=new NavDemandQueryWork(dq,birth.TryReserve);dq.StorageRoot.ReleaseOwner();BoundedFinish(donor);PositiveDonorPremise(donor);Assert.Greater(donor.Save().Chain.Length,0,"fixture requires an actual shared graph suffix");
                    if(target=="donor"){root=donor;donor=null;}else{var rq=NavScheduledRequest.CopyOwned(raw,provider,r);root=new NavDemandQueryWork(rq,birth.TryReserve);rq.StorageRoot.ReleaseOwner();root.AttachCorridor(donor);donor.ReleaseOwner();donor=null;
                        for(int n=0;;n++){Assert.Less(n,10000);int scan=(int)LazyField(root,"corridorScan"),phase=(int)LazyField(root,"phase"),emit=(int)LazyField(root,"corridorEmit");bool at=target=="scan"?scan>0&&!(bool)LazyField(root,"corridorScanned"):target=="cut"?phase==8&&emit==0:phase==8&&emit>0;if(at)break;if(root.Status!=NavSolveStatus.Pending){failedHot=BoundedFullState(root);provider.StorageRoot.ReleaseOwner();root.Seal();birth.Attach(root);birth.Commit(root,_=>{});bodies["target-not-reached.body"]=CaptureGraph(hot,root);}Assert.AreEqual(NavSolveStatus.Pending,root.Status,"fixture must reach selected bounded phase");BoundedTraceStep(root,trace);}}
                    provider.StorageRoot.ReleaseOwner();root.Seal();birth.Attach(root);birth.Commit(root,_=>{});}
                var bytes=CaptureGraph(hot,root);bodies["hot.body"]=bytes;var saved=root.Save();
                byte[] Next(NavigationRetentionBudget b,NavDemandQueryWork source){if(source.Status!=NavSolveStatus.Pending)return CaptureGraph(b,source);Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,10,out var scope));NavDemandQueryWork n=null;try{using(scope){n=source.Fork(scope.TryReserve);V3Step(n);n.Seal();scope.Attach(n);scope.Commit(n,_=>{});}return CaptureGraph(b,n);}finally{n?.ReleaseOwner();}}
                var expected=Next(hot,root);bodies["expected-next.body"]=expected;CollectionAssert.AreEqual(bytes,CaptureGraph(hot,root));
                if(root.Status==NavSolveStatus.Pending){Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,20,out var scope));NavDemandQueryWork n=null;try{var refusal=new Refusal(scope,1);Assert.Throws<InvalidOperationException>(()=>{n=root.Fork(refusal.TryReserve);V3Step(n);});}finally{n?.ReleaseOwner();scope.Discard();}CollectionAssert.AreEqual(bytes,CaptureGraph(hot,root));Assert.Zero(hot.HeldBytes);}
                Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var decode));using(decode){restored=(NavDemandQueryWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}var coldBytes=CaptureGraph(cold,restored);bodies["decoded.body"]=coldBytes;CollectionAssert.AreEqual(bytes,coldBytes);var coldNext=Next(cold,restored);bodies["cold-next.body"]=coldNext;CollectionAssert.AreEqual(expected,coldNext);
                var logical=NavDemandQueryWork.Restore(raw,saved);try{if(logical.Status==NavSolveStatus.Pending)V3Step(logical);}finally{logical.ReleaseOwner();}
            }catch(Exception error){primary=error;throw;}finally{
                try{if(failedHot==null)failedHot=BoundedFullState(root);failedCold=BoundedFullState(restored);}catch(Exception error){errors.Add("snapshot: "+error);}
                void Clean(Action action){try{action();}catch(Exception error){errors.Add("cleanup: "+error);}}Clean(()=>restored?.ReleaseOwner());Clean(()=>donor?.ReleaseOwner());Clean(()=>root?.ReleaseOwner());
                BoundedWriteArtifacts("cold-"+target,new{Primary=primary?.ToString(),Errors=errors,HotState=failedHot,ColdState=failedCold,Trace=trace,Hot=new{hot.PhysicalBytes,hot.HeldBytes,hot.ActiveScopes},Control=new{cold.PhysicalBytes,cold.HeldBytes,cold.ActiveScopes}},bodies);
                if(primary==null)Assert.IsEmpty(errors);
            }PrefixNoLeaks(hot);PrefixNoLeaks(cold);
        }
        private void BoundedAdmissionWithDiagnostics(bool positive,bool negativeRegression)
        {
            var hot=new NavigationRetentionBudget();var control=new NavigationRetentionBudget();NavDemandQueryWork root=null,donor=null,exact=null;
            var document=new System.Collections.Generic.Dictionary<string,object>();var events=new System.Collections.Generic.List<object>();var bodies=new System.Collections.Generic.Dictionary<string,byte[]>();
            var cleanupErrors=new System.Collections.Generic.List<string>();Exception primary=null;int token=100,actions=0;var start=DateTime.UtcNow;
            NavScheduledRequest rawDonor=null,rawRecipient=null,rawExact=null;
            string[] Cost(NavExactCost value)=>Enumerable.Range(0,34).Select(i=>value.Limb(i).ToString("x16")).ToArray();
            object Weight(double value)=>new{Value=value,RoundTrip=value.ToString("R",System.Globalization.CultureInfo.InvariantCulture),Bits=unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString("x16")};
            object Point(NavLocation at)=>new{X=at.Position.X,Z=at.Position.Z,Surface=at.SurfaceId};
            NavLocation Node(NavDemandQueryWork w,int id)=>(NavLocation)typeof(NavDemandQueryWork).GetMethod("NodeLocation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(w,new object[]{id});
            NavScheduledRequest Recipe(NavDemandQueryWork w)=>(NavScheduledRequest)LazyField(w,"request");
            object Request(NavScheduledRequest r)=>new{r.CertifiedGroupCorridor,r.Query,r.GroupQuery,r.Singleton,r.DemandQueryV6,r.LazyPartition,r.CertifiedLazyPartition,r.RectangularOrigins,r.TerminalRectangleProof,r.TerminalCornerProof,r.OriginShadowProof,r.TerminalCostProof,r.GraphKey,r.CompileKey,r.ArtifactKey,Provider=r.Provider.Binding,GraphProfile=r.Profile.ExactProfileBinding,r.Profile.GridCell,r.Profile.CostId,r.Profile.CostRevision,r.MaxLabels,r.Limits.MaxSamples,r.Limits.MaxNodes,r.Limits.MaxArcs,Clearance=new{r.Clearance.Id,r.Clearance.Revision,r.Clearance.MobilityClass,r.Clearance.MobilityRevision,r.Clearance.Radius},Region=new{r.Region.Id,r.Region.Revision,r.Region.StableKey,Requested=Point(r.Region.Requested),Candidates=r.Region.Candidates.Select(Point).ToArray()},Subscription=new{r.Subscription.StableKey,r.Subscription.WorldGeneration,r.Subscription.GroupId,r.Subscription.RootOrderRevision,r.Subscription.CommandSequence,r.Subscription.IssuedTick,r.Subscription.ActivationRevision,r.Subscription.EntityId,r.Subscription.Incarnation,r.Subscription.MemberOrderRevision,r.Subscription.MobilityRevision,r.Subscription.RequestSequence,r.Subscription.HoldBinding,Origin=Point(r.Subscription.Origin),Endpoint=Point(r.Subscription.AssignedEndpoint)}};
            object Leg(NavTypedLeg l)=>new{From=Point(l.From),To=Point(l.To),Weight=Weight(l.Cost),l.Kind,l.PortalId};
            object State(NavDemandQueryWork w)
            {
                var open=(NavCowOrderedSet<NavExactQueueEntry>)LazyField(w,"exactFrontier");int chosen=(int)LazyField(w,"chosen");var incumbent=(NavExactCost)LazyField(w,"exactBest");
                bool has=open.Count>0;var min=has?open.Min.Cost:default;var lower=has&&chosen>=0&&incumbent<min?incumbent:min;
                return new{Status=w.Status.ToString(),HeuristicActive=w.HeuristicActive,MetricContract=w.EffectiveMetricContract.ToString(),Phase=LazyField(w,"phase").ToString(),Current=LazyField(w,"current"),Pending=LazyField(w,"pending"),SourcePresent=LazyField(w,"corridorSource")!=null,Scan=LazyField(w,"corridorScan"),ScanComplete=LazyField(w,"corridorScanned"),Boundary=LazyField(w,"corridorBoundary"),Node=LazyField(w,"corridorNode"),Checks=LazyField(w,"corridorChecks"),CutPaid=LazyField(w,"corridorCutPaid"),Selected=LazyField(w,"boundedSelected"),Upper=Cost((NavExactCost)LazyField(w,"corridorUpper")),OpenCount=open.Count,OpenMinimum=has?Cost(min):null,IndependentChosen=chosen,IndependentCandidate=LazyField(w,"chosenCandidate"),IndependentIncumbent=chosen>=0?Cost(incumbent):null,LowerKind="SOURCE-DERIVED min from captured OPEN minimum/independent incumbent; not a direct capture of a method local",SourceDerivedLower=has?Cost(lower):null};
            }
            void ObserveStep(NavDemandQueryWork w)
            {
                Assert.Less(actions++,10000,"same component step bound");Assert.Less((DateTime.UtcNow-start).TotalSeconds,115,"diagnostic leaves time for final flush");
                int scan=(int)LazyField(w,"corridorScan"),checks=(int)LazyField(w,"corridorChecks");var before=State(w);string phase=LazyField(w,"phase").ToString();
                V3Step(w);
                if(scan!=(int)LazyField(w,"corridorScan")||checks!=(int)LazyField(w,"corridorChecks")||phase!=LazyField(w,"phase").ToString()){
                    Assert.Less(events.Count,2048,"bounded transcript");events.Add(new{Action=actions,ScanAction=scan!=(int)LazyField(w,"corridorScan"),ActualCutAttempt=checks!=(int)LazyField(w,"corridorChecks"),Before=before,After=State(w)});
                }
            }
            void Capture(string name){Assert.Less(bodies.Count,8);bodies.Add(name+".body",CaptureGraph(hot,root,++token));Assert.Zero(hot.HeldBytes);Assert.Zero(hot.ActiveScopes,"capture owner disposed before advance");}
            void AdvanceUntil(Func<NavDemandQueryWork,bool> reached)
            {
                if(root.Status!=NavSolveStatus.Pending||reached(root))return;
                Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,++token,out var scope));NavDemandQueryWork next=null;
                try{using(scope){next=root.Fork(scope.TryReserve);while(next.Status==NavSolveStatus.Pending&&!reached(next))ObserveStep(next);next.Seal();scope.Attach(next);scope.Commit(next,_=>{});}root.ReleaseOwner();root=next;next=null;}finally{next?.ReleaseOwner();}
            }
            void Attachments()
            {
                var d=(NavDemandQueryWork)LazyField(root,"corridorSource");if(d==null){document["attachments"]=new{NotRetained="No attached donor at origins boundary"};return;}
                var r=Recipe(root);var chain=(NavPagedList<NavGraphArc>)LazyField(d,"chain");int chosen=(int)LazyField(d,"chosen"),candidate=(int)LazyField(d,"chosenCandidate");
                var starts=(NavCowDictionary<int,double>)LazyField(root,"starts");var ends=(NavCowDictionary<int,double>)LazyField(root,"ends");
                double final=(double)typeof(NavDemandQueryWork).GetMethod("Cost",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(root,new object[]{Node(d,chosen),r.Region.Candidates[candidate]});var suffix=NavExactCost.FromDouble(final);var boundaries=new System.Collections.Generic.List<object>();
                for(int i=0;i<=chain.Count;i++){
                    if(i>0)suffix=suffix.Add(chain[i-1].Cost);int node=i==0?chosen:chain[i-1].From;bool sh=starts.TryGetValue(node,out double ingress),eh=ends.TryGetValue(candidate,out double egress);bool? forward=null;
                    if(i>0&&sh&&eh)forward=(bool)typeof(NavDemandQueryWork).GetMethod("Forward",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(root,new object[]{node});
                    bool eligible=i>0&&sh&&eh&&forward==true;var u=eligible?NavExactCost.FromDouble(ingress).Add(suffix).Add(egress):default;
                    boundaries.Add(new{Index=i,Node=node,Location=Point(Node(d,node)),StartsHit=sh,EndsHit=eh,ForwardEvaluated=forward.HasValue,Forward=forward,ForwardMissingReason=forward.HasValue?null:"Not evaluated: same runtime short circuit",Eligible=eligible,Ingress=sh?Weight(ingress):null,Egress=eh?Weight(egress):null,Suffix=Cost(suffix),UpperKind="SOURCE-DERIVED using existing NavExactCost and original atomic arc.Cost/actual Cost()",SourceDerivedUpper=eligible?Cost(u):null});
                }
                document["attachments"]=new{Source=State(root),ActualDonorChosen=chosen,ActualDonorCandidate=candidate,FinalGraphToCandidate=Weight(final),Starts=starts.Select(x=>new{Node=x.Key,Location=Point(Node(root,x.Key)),Weight=Weight(x.Value)}).ToArray(),Ends=ends.Select(x=>new{Candidate=x.Key,Location=Point(r.Region.Candidates[x.Key]),Weight=Weight(x.Value)}).ToArray(),Boundaries=boundaries};
                document["donor"]=new{Request=Request(Recipe(d)),State=State(d),TypedLegs=d.Route.Legs.Select(Leg).ToArray(),ReverseChain=chain.Select(a=>new{a.From,a.To,FromLocation=Point(Node(d,a.From)),ToLocation=Point(Node(d,a.To)),AtomicWeight=Weight(a.Cost),a.Kind,a.PortalId,Trace=a.Trace?.Select(Leg).ToArray()}).ToArray()};
            }

            try{
                rawDonor=positive?PositiveBoundedRequest():BoundedRequest();rawRecipient=positive?PositiveBoundedRequest(2):BoundedRequest(2);rawExact=positive?PositiveBoundedRequest(2,false):BoundedRequest(2,false);
            document["scope"]=new{InputKind="component NavScheduledRequest inputs, not NavArrivalInput or a game world",Purpose=positive?"fixed-positive fixture qualification":negativeRegression?"old no-forward negative regression":"data about existing RequireShared failure, not feature qualification",NativeCompactions=0,OldSource="ee21b300a5e220857a38dc3ccb3a3325edca63c9",Runtime="96d6b7bed0b6f2ced0f76670dddd61e0477f184f",Fixture=positive?"fixed P=(-48,-48), donor=P+(-2r,0), recipient=P+(-2r,r/2), unchanged CertifiedTraceRequest provider/grid/candidates/limits": "original BoundedRequest/CertifiedTraceRequest input retained",RequestDonor=Request(rawDonor),RequestRecipient=Request(rawRecipient),RequestExact=Request(rawExact),Bounds=new{Steps=10000,Transcript=2048,NUnitMilliseconds=120000,EditorGuardianSeconds=300}};
                Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,++token,out var birth));using(birth){var r=birth.OwnedReservation();var provider=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)rawRecipient.Provider,r);var dq=NavScheduledRequest.CopyOwned(rawDonor,provider,r);donor=new NavDemandQueryWork(dq,birth.TryReserve);dq.StorageRoot.ReleaseOwner();BoundedFinish(donor);if(positive)PositiveDonorPremise(donor);Assert.Greater(donor.Save().Chain.Length,0,"fixture must have a nonempty solved graph tail");Assert.True(donor.Route.Legs.Any(l=>l.Kind==NavTypedLegKind.Portal),"fixture must cross typed transitions");
                    var rq=NavScheduledRequest.CopyOwned(rawRecipient,provider,r);root=new NavDemandQueryWork(rq,birth.TryReserve);rq.StorageRoot.ReleaseOwner();root.AttachCorridor(donor);Assert.AreEqual(0,(int)LazyField(root,"corridorChecks"),"no incomplete-origin cut certificate");donor.ReleaseOwner();donor=null;provider.StorageRoot.ReleaseOwner();root.Seal();birth.Attach(root);birth.Commit(root,_=>{});}
                Capture("donor-ready-recipient-catalog");AdvanceUntil(w=>LazyField(w,"phase").ToString()=="Seed");document["originsBoundary"]=State(root);Attachments();Capture("exhaustive-origins-before-scan");if(positive){var starts=(NavCowDictionary<int,double>)LazyField(root,"starts");var p=starts.Where(x=>Node(root,x.Key).Position.Equals(new NavPoint(-48,-48))).Select(x=>x.Key).ToArray();Assert.AreEqual(1,p.Length,"fixed P must be an actual validated recipient ingress");Assert.True((bool)typeof(NavDemandQueryWork).GetMethod("Forward",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(root,new object[]{p[0]}),"fixed P must pass actual Forward");}
                AdvanceUntil(w=>(bool)LazyField(w,"corridorScanned"));document["scanComplete"]=State(root);Capture("scan-complete");
                AdvanceUntil(w=>(int)LazyField(w,"corridorChecks")>0);document["firstComparisonOrReady"]=State(root);Capture("first-comparison-or-ready");
                AdvanceUntil(w=>false);Assert.AreEqual(NavSolveStatus.Ready,root.Status);document["finalRecipient"]=new{Request=Request(Recipe(root)),State=State(root),root.UsedCertifiedCorridor,TypedLegs=root.Route.Legs.Select(Leg).ToArray(),IndependentExactObjectiveWhenFallback=root.UsedCertifiedCorridor?null:Cost(root.ExactObjective)};Capture("recipient-ready");
                Assert.True(control.TryOpenScope(NavigationPreparationKind.Solve,1,++token,out var comparison));using(comparison){var r=comparison.OwnedReservation();var p=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)rawExact.Provider,r);var q=NavScheduledRequest.CopyOwned(rawExact,p,r);exact=new NavDemandQueryWork(q,comparison.TryReserve);q.StorageRoot.ReleaseOwner();p.StorageRoot.ReleaseOwner();BoundedFinish(exact);exact.Seal();comparison.Attach(exact);comparison.Commit(exact,_=>{});}
                document["exactReference"]=new{Request=Request(Recipe(exact)),State=State(exact),ExactObjective=Cost(exact.ExactObjective),TypedLegs=exact.Route.Legs.Select(Leg).ToArray()};bodies.Add("independent-exact-ready.body",CaptureGraph(control,exact,++token));Assert.Zero(control.HeldBytes);Assert.Zero(control.ActiveScopes);
                // Preserve the original same-geometry verification, including its
                // expected RequireShared FAIL; all diagnostics flush in finally.
                if(negativeRegression){Assert.False(root.UsedCertifiedCorridor,"old input must not force return to the donor old origin");Assert.AreEqual(exact.ExactObjective,root.ExactObjective,"old exact fallback remains exact");Assert.AreEqual(-1,(int)LazyField(root,"corridorBoundary"),"no admitted back-assembly join");Assert.AreEqual(0,(int)LazyField(root,"corridorChecks"));var negativeDonor=(NavDemandQueryWork)LazyField(root,"corridorSource");var p=((NavCowDictionary<int,double>)LazyField(root,"starts")).Single().Key;Assert.AreEqual(negativeDonor.Route.Subscription.Origin.Position,Node(root,p).Position);Assert.False((bool)typeof(NavDemandQueryWork).GetMethod("Forward",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(root,new object[]{p}));return;}
                Assert.True(root.UsedCertifiedCorridor,"distinct endpoints consume a real shared tail");Assert.True(NavDemandQueryWork.CertifiedRatio(root.PublishedObjective,root.PublishedLower));Assert.True(root.PublishedLower<=exact.ExactObjective,"full alternative objective lower bound");Assert.True(NavDemandQueryWork.CertifiedRatio(root.PublishedObjective,exact.ExactObjective));
                var source=(NavDemandQueryWork)LazyField(root,"corridorSource");Assert.AreNotEqual(source.Route.Subscription.AssignedEndpoint,root.Route.Subscription.AssignedEndpoint);Assert.Greater(root.Route.Legs.Count,2);var from=root.Route.Subscription.Origin.Position;var to=root.Route.Subscription.AssignedEndpoint.Position;var join=root.Route.Legs[0].To.Position;Assert.Greater((join.X-from.X)*(to.X-from.X)+(join.Z-from.Z)*(to.Z-from.Z),0,"forward direct ingress projection");Assert.Throws<InvalidOperationException>(()=>{var unused=root.ExactObjective;});
            }catch(Exception error){primary=error;throw;}
            finally{
                void Clean(Action action){try{action();}catch(Exception error){cleanupErrors.Add(error.ToString());}}
                Clean(()=>exact?.ReleaseOwner());Clean(()=>root?.ReleaseOwner());Clean(()=>donor?.ReleaseOwner());
                document["events"]=events;document["primary"]=primary?.ToString();document["cleanup"]=new{Errors=cleanupErrors,Hot=new{hot.PhysicalBytes,hot.HeldBytes,hot.ActiveScopes},Control=new{control.PhysicalBytes,control.HeldBytes,control.ActiveScopes},Actions=actions,Bodies=bodies.Keys.ToArray()};
                var outputPath=System.Environment.GetEnvironmentVariable("SPACEWARS_CORRIDOR_PROBE_OUTPUT");if(string.IsNullOrWhiteSpace(outputPath))throw new InvalidOperationException("SPACEWARS_CORRIDOR_PROBE_OUTPUT is required.");var dir=System.IO.Path.GetFullPath(outputPath);System.IO.Directory.CreateDirectory(dir);
                using(var file=System.IO.File.Create(System.IO.Path.Combine(dir,positive?"positive-admission.zip":negativeRegression?"negative-original.zip":"probe-state.zip")))using(var zip=new System.IO.Compression.ZipArchive(file,System.IO.Compression.ZipArchiveMode.Create)){
                    using(var writer=new System.IO.StreamWriter(zip.CreateEntry("transcript.json").Open()))writer.Write(JsonConvert.SerializeObject(document,Formatting.Indented));foreach(var body in bodies)using(var output=zip.CreateEntry(body.Key).Open())output.Write(body.Value,0,body.Value.Length);
                }
                TestContext.WriteLine("BOUNDED_PROBE_FLUSH actions="+actions+" events="+events.Count+" bodies="+bodies.Count+" hot="+hot.PhysicalBytes+" held="+hot.HeldBytes+" scopes="+hot.ActiveScopes+" control="+control.PhysicalBytes);
                if(primary==null){Assert.IsEmpty(cleanupErrors);PrefixNoLeaks(hot);PrefixNoLeaks(control);}
            }
        }

        [Test] public void BoundedProductionCapacityOptInAndExactPolicySelection()
        {
            var profile=PlayableProfile.CertifiedGroupCorridorDefault;var map=(ThreeCrossingsMap)profile.AuthoredMap;
            var provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.Solids.ToArray(),map.Revision),null);
            var clearance=new NavClearanceProfile("ground",1,"ground",1,profile.TankCollisionRadius);
            Assert.True(provider.TryLocate(new NavPoint(-18,-1.8),clearance,out var origin));Assert.True(provider.TryLocate(new NavPoint(36,42.4),clearance,out var endpoint));
            var region=new NavTerminalRegion("ordinary-selector",1,endpoint,new[]{endpoint,new NavLocation(new NavPoint(39.6,42.4),endpoint.SurfaceId)});
            var graph=new NavGraphProfile(NavigationAdmission.ExactProfileBinding(profile.Navigation),"distance",1,profile.Navigation.GridCell);
            var sub=new NavRouteSubscription(71,1,1,1,0,1,5,1,1,1,1,origin,endpoint,null);
            NavScheduledRequest Make(bool bounded)=>new NavScheduledRequest(provider,clearance,graph,new NavGraphCompileLimits(1048576,300000,1200000),region,sub,1048576,query:true,groupQuery:true,demandQuery:true,lazyPartition:true,certifiedLazyPartition:true,rectangularOrigins:true,terminalRectangleProof:true,terminalCornerProof:true,originShadowProof:true,terminalCostProof:true,certifiedGroupCorridor:bounded);
            var exact=Make(false);var optIn=Make(true);Assert.True(NavDemandQueryWork.LazyEligible(exact),"ordinary production capacity used to block opt-in");Assert.True(NavDemandQueryWork.CertifiedEligible(optIn));
            Assert.True(optIn.CertifiedGroupCorridor);Assert.True(optIn.CertifiedLazyPartition);Assert.True(optIn.TerminalCostProof);Assert.True(optIn.DemandQueryV6);
            Assert.False(exact.CertifiedGroupCorridor);Assert.True(exact.LazyPartition);Assert.False(exact.CertifiedLazyPartition);Assert.False(exact.TerminalCostProof);Assert.AreNotEqual(exact.CompileKey,optIn.CompileKey);
            var singleton=NavScheduledRequest.ForSingleton(provider,clearance,graph,exact.Limits,sub,1048576);Assert.True(singleton.Singleton);Assert.False(singleton.CertifiedGroupCorridor);
            TestContext.WriteLine("A2_PRODUCTION_SELECTOR "+JsonConvert.SerializeObject(new{profile.Navigation.GroupCorridorMaximumExcessPercent,ExactPolicy="LazyPartitionPolicyV7",OptInPolicy="CertifiedGroupCorridor115V1",exact.CompileKey,OptInCompileKey=optIn.CompileKey}));
        }
        [TestCase("query")][TestCase("cancel")][TestCase("invalid")]
        public void BoundedEveryPaidDebitAndColdCowCounter(string scenario)
        {
            var hot=new NavigationRetentionBudget();var cold=new NavigationRetentionBudget();LayeredNavigationScheduler root=null,restored=null;var trace=new System.Collections.Generic.List<object>();var bodies=new System.Collections.Generic.Dictionary<string,byte[]>();long spent=0;bool coldChecked=false;int token=100;Exception primary=null;
            try{
                var raw=LazyDetourRequest();if(scenario=="invalid"){var old=raw.Subscription;var invalid=new NavLocation(new NavPoint(10000,10000),old.AssignedEndpoint.SurfaceId);var sub=new NavRouteSubscription(old.WorldGeneration,old.GroupId,old.RootOrderRevision,old.CommandSequence,old.IssuedTick,old.ActivationRevision,old.EntityId,old.Incarnation,old.MemberOrderRevision,old.MobilityRevision,old.RequestSequence,old.Origin,invalid,old.HoldBinding);raw=new NavScheduledRequest(raw.Provider,raw.Clearance,raw.Profile,raw.Limits,new NavTerminalRegion("invalid",1,invalid,new[]{invalid}),sub,raw.MaxLabels,query:true,demandQuery:true,lazyPartition:true);}
                Assert.True(hot.TryOpenScope(NavigationPreparationKind.Admission,1,token++,out var birth));using(birth){var r=birth.OwnedReservation();var provider=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,r);var recipe=NavScheduledRequest.CopyOwned(raw,provider,r);root=LayeredNavigationScheduler.CreateOwned(r);root.EnableCertifiedAccounting();root.BindAttemptArtifacts(new string('e',64));Assert.True(root.Submit(recipe));recipe.StorageRoot.ReleaseOwner();provider.StorageRoot.ReleaseOwner();root.SealPrepared();birth.Attach(root);birth.Commit(root,_=>{});}
                for(int n=0;root.PendingCount>0;n++){
                    Assert.Less(n,10000);var previous=root;var before=CaptureGraph(hot,root,token++);long paid=root.CertifiedBookkeeping;Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,token++,out var step));
                    using(step){root=previous.ForkOwned(step.OwnedReservation());int used=root.Advance(n,new NavEpochQuota(13,17,19,23,29),_=>scenario!="cancel",1);int debited=23-root.RemainingQuota.Bookkeeping;Assert.GreaterOrEqual(debited,0);Assert.LessOrEqual(debited,1);spent+=debited;Assert.AreEqual(paid+debited,root.CertifiedBookkeeping,"count every actual quota debit, including non-query lanes");trace.Add(new{n,used,debited,Paid=root.CertifiedBookkeeping});root.SealPrepared();step.Attach(root);step.Commit(root,_=>{});}
                    Assert.AreEqual(paid,previous.CertifiedBookkeeping,"COW preserves prior counter");CollectionAssert.AreEqual(before,CaptureGraph(hot,previous,token++));previous.ReleaseStorage();
                    if(!coldChecked&&root.CertifiedBookkeeping>0){var bytes=CaptureGraph(hot,root,token++);bodies["paid-before-next.body"]=bytes;Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,token++,out var decode));using(decode){restored=(LayeredNavigationScheduler)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored,token++));Assert.AreEqual(root.CertifiedBookkeeping,restored.CertifiedBookkeeping);Assert.AreEqual(root.LogicalContinuationBinding,restored.LogicalContinuationBinding);
                        byte[] Next(NavigationRetentionBudget budget,LayeredNavigationScheduler source){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,token++,out var scope));LayeredNavigationScheduler next=null;try{using(scope){next=source.ForkOwned(scope.OwnedReservation());long old=next.CertifiedBookkeeping;next.Advance(n+1,new NavEpochQuota(13,17,19,23,29),_=>scenario!="cancel",1);Assert.AreEqual(old+23-next.RemainingQuota.Bookkeeping,next.CertifiedBookkeeping);next.SealPrepared();scope.Attach(next);scope.Commit(next,_=>{});}return CaptureGraph(budget,next,token++);}finally{next?.ReleaseStorage();}}
                        CollectionAssert.AreEqual(Next(hot,root),Next(cold,restored),"cold exact next action/counter");CollectionAssert.AreEqual(bytes,CaptureGraph(hot,root,token++));restored.ReleaseStorage();restored=null;coldChecked=true;
                    }
                }
                Assert.True(coldChecked,"paid path must exercise cold counter continuation");Assert.AreEqual(spent,root.CertifiedBookkeeping);Assert.Greater(spent,0);
                var pinned=root;var pinnedBytes=CaptureGraph(hot,pinned,token++);Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,token++,out var finish));using(finish){root=pinned.ForkOwned(finish.OwnedReservation());while(root.TryTake(_=>true,out var completion))completion.ReleaseOwner();long old=root.CertifiedBookkeeping;Assert.True(root.EndAttemptArtifacts(10001,new NavEpochQuota(13,17,19,23,29)));int debit=23-root.RemainingQuota.Bookkeeping;Assert.AreEqual(old+debit,root.CertifiedBookkeeping);if(scenario=="query")Assert.AreEqual(1,debit,"shared catalog teardown consumes its real paid unit");spent+=debit;root.SealPrepared();finish.Attach(root);finish.Commit(root,_=>{});}CollectionAssert.AreEqual(pinnedBytes,CaptureGraph(hot,pinned,token++));pinned.ReleaseStorage();Assert.AreEqual(spent,root.CertifiedBookkeeping);bodies["finished-counter.body"]=CaptureGraph(hot,root,token++);
                Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,token++,out var rejected));using(rejected){var trial=root.ForkOwned(rejected.OwnedReservation());long value=trial.CertifiedBookkeeping;Assert.True(trial.EndAttemptArtifacts(10002,new NavEpochQuota(1,1,1,1,1)));Assert.AreEqual(value,trial.CertifiedBookkeeping,"empty teardown never charges or refunds");trial.SealPrepared();rejected.Attach(trial);rejected.Discard();}CollectionAssert.AreEqual(bodies["finished-counter.body"],CaptureGraph(hot,root,token++));
            }catch(Exception error){primary=error;throw;}finally{restored?.ReleaseStorage();root?.ReleaseStorage();BoundedWriteArtifacts("paid-"+scenario,new{scenario,spent,coldChecked,primary=primary?.ToString(),trace,Census=new{hot.PhysicalBytes,hot.HeldBytes,hot.ActiveScopes,Cold=cold.PhysicalBytes}},bodies);if(primary==null){PrefixNoLeaks(hot);PrefixNoLeaks(cold);}}
        }

    }
}
