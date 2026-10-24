using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        [Test,Timeout(180000)] public void CampaignFreshTerminalRectangleFixedGrantsAndOldBodyCompatibility()
        {
            var source=Environment.GetEnvironmentVariable("CAMPAIGN_SEARCH_BODY");var folder=Environment.GetEnvironmentVariable("CAMPAIGN_SEARCH_OUTPUT");Assert.IsNotEmpty(source);Assert.IsNotEmpty(folder);Directory.CreateDirectory(folder);
            var body=File.ReadAllBytes(source);using(var h=System.Security.Cryptography.SHA256.Create())Assert.AreEqual("0772376bcb9d85125065eeb5fb145471631c301e55a4044748f6273abd855f8e",BitConverter.ToString(h.ComputeHash(body)).Replace("-","").ToLowerInvariant());
            var sourceBudget=new NavigationRetentionBudget();NavigationArrivalWork outer=null;
            var oldBudget=new NavigationRetentionBudget();var freshBudget=new NavigationRetentionBudget();NavDemandQueryWork old=null,fresh=null;
            try{
                Assert.True(sourceBudget.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var decode));using(decode){outer=(NavigationArrivalWork)Decode(body,decode.OwnedReservation());decode.Attach(outer);decode.Commit(outer,_=>{});}
                Assert.True(sourceBudget.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var capture));NavigationCaptureBytes encoded=null;
                try{using(capture){var reservation=capture.OwnedReservation();encoded=NavigationCaptureBytes.Write(reservation,w=>NavPhysicalBodyWire.WriteTo(w,outer,reservation));capture.Attach(encoded);capture.Commit(encoded,_=>{});CollectionAssert.AreEqual(body,encoded.TransferPublic(),"Historical V9 BODY byte-identical reencode, EOF checked by Decode");encoded=null;}}finally{encoded?.Dispose();}
                var job=((NavPersistentQueue<LayeredNavigationScheduler.Job>)LazyField(PrefixScheduler(outer),"jobs")).First;var r=job.Request;Assert.True(r.RectangularOrigins);Assert.False(r.TerminalRectangleProof);Assert.AreEqual(64,r.Region.Candidates.Count);
                // Fresh comparisons use detached declarations; owned roots cannot cross ledgers.
                r=NavDemandQueryWork.DetachSharedRecipe(r,((PartitionNavigationProvider)r.Provider).DetachedSnapshot());
                NavDemandQueryWork Prepare(NavigationRetentionBudget b,bool terminal)
                {
                    Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var birth));using(birth){var provider=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)r.Provider,birth.OwnedReservation());
                    var raw=new NavScheduledRequest(provider,r.Clearance,r.Profile,r.Limits,r.Region,r.Subscription,r.MaxLabels,r.Singleton,r.Query,r.GroupQuery,r.DemandQuery,r.DemandQueryV2,r.DemandQueryV3,r.DemandQueryV4,r.DemandQueryV5,r.DemandQueryV6,r.LazyPartition,r.CertifiedLazyPartition,r.RectangularOrigins,terminal);
                    var q=NavScheduledRequest.CopyOwned(raw,provider,birth.OwnedReservation());var n=new NavDemandQueryWork(q,birth.TryReserve);q.StorageRoot.ReleaseOwner();provider.StorageRoot.ReleaseOwner();
                    try{for(int i=0;LazyField(n,"phase").ToString()!="Seed";i++){Assert.Less(i,500000);Assert.AreEqual(NavSolveStatus.Pending,n.Status);V3Step(n);}n.Seal();birth.Attach(n);birth.Commit(n,_=>{});return n;}catch{n.ReleaseOwner();throw;}}
                }
                old=Prepare(oldBudget,false);fresh=Prepare(freshBudget,true);CollectionAssert.AreEqual(old.Save().Starts,fresh.Save().Starts);Assert.AreEqual(old.SampleCount,fresh.SampleCount);outer.ReleaseOwner();outer=null;PrefixNoLeaks(sourceBudget);
                long Run(NavigationRetentionBudget b,ref NavDemandQueryWork q,string name)
                {
                    long pops=0,proofs=0,candidates=0;int lastNode=-1;var clock=System.Diagnostics.Stopwatch.StartNew();
                    for(int grant=0;grant<64&&q.Status==NavSolveStatus.Pending;grant++){
                        Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,grant+1,out var scope));NavDemandQueryWork n=null;try{using(scope){n=q.Fork(scope.TryReserve);int graph=256,connector=256,bookkeeping=256;
                        while(n.Status==NavSolveStatus.Pending){int active=(int)LazyField(n,"current");if(active>=0&&active!=lastNode){pops++;lastNode=active;}if(n.CanBookkeeping&&bookkeeping>0){n.StepBookkeeping();bookkeeping--;}
                        else if(n.CanGraph&&graph>0&&!(n.CanConnector&&connector>0)){n.StepGraph();graph--;}
                        else if(n.CanConnector&&connector>0){var phase=LazyField(n,"phase").ToString();if(phase=="Candidates"){candidates++;if((int)LazyField(n,"terminalBlocker")>=0)proofs++;}n.StepConnector();connector--;}
                        else break;}
                        n.Seal();scope.Attach(n);scope.Commit(n,_=>{});}q.ReleaseOwner();q=n;n=null;}finally{n?.ReleaseOwner();}}
                    string result=name+" grants=64 pops="+pops+" proofs="+proofs+" candidateConnector="+candidates+" status="+q.Status+" phase="+LazyField(q,"phase")+" frontier="+q.FrontierCount+" seconds="+clock.Elapsed.TotalSeconds;File.WriteAllText(Path.Combine(folder,name+"-fixed-grants.txt"),result);TestContext.WriteLine(result);return pops;
                }
                long previous=Run(oldBudget,ref old,"old-v9"),current=Run(freshBudget,ref fresh,"fresh-v10");Assert.Greater(current,previous,"Paid proof must advance more actual search nodes under identical grants");
            }finally{outer?.ReleaseOwner();old?.ReleaseOwner();fresh?.ReleaseOwner();}
            PrefixNoLeaks(sourceBudget);PrefixNoLeaks(oldBudget);PrefixNoLeaks(freshBudget);
        }
        [Test,Timeout(180000)] public void CampaignFreshTerminalCornerIdenticalGrantsCompleteAndOldBodyCompatibility()
        {
            var source=Environment.GetEnvironmentVariable("CAMPAIGN_GROUP_BODY_TWO");var folder=Environment.GetEnvironmentVariable("CAMPAIGN_GROUP_QUERY_OUTPUT");Assert.IsNotEmpty(source);Assert.IsNotEmpty(folder);Directory.CreateDirectory(folder);
            var body=File.ReadAllBytes(source);using(var h=System.Security.Cryptography.SHA256.Create())Assert.AreEqual("d4a6915eafd16004647b0f570d9c7ab8dc724e05cb9acef06c4886c792ab1519",BitConverter.ToString(h.ComputeHash(body)).Replace("-","").ToLowerInvariant());
            var sourceBudget=new NavigationRetentionBudget();NavigationArrivalWork outer=null;
            var oldBudget=new NavigationRetentionBudget();var freshBudget=new NavigationRetentionBudget();NavDemandQueryWork old=null,fresh=null;
            try{
                Assert.True(sourceBudget.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var decode));using(decode){outer=(NavigationArrivalWork)Decode(body,decode.OwnedReservation());decode.Attach(outer);decode.Commit(outer,_=>{});}
                Assert.True(sourceBudget.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var capture));NavigationCaptureBytes encoded=null;
                try{using(capture){var reservation=capture.OwnedReservation();encoded=NavigationCaptureBytes.Write(reservation,w=>NavPhysicalBodyWire.WriteTo(w,outer,reservation));capture.Attach(encoded);capture.Commit(encoded,_=>{});CollectionAssert.AreEqual(body,encoded.TransferPublic(),"Historical V10 BODY byte-identical reencode, EOF checked by Decode");encoded=null;}}finally{encoded?.Dispose();}
                LayeredNavigationScheduler.Job Find(NavigationArrivalWork w){var job=((NavPersistentQueue<LayeredNavigationScheduler.Job>)LazyField(PrefixScheduler(w),"jobs")).FirstOrDefault(j=>j.Demand!=null);if(job!=null)return job;var h=PrefixState(w)?.Oracle;Assert.NotNull(h);return Find(h);}
                var r=Find(outer).Request;Assert.True(r.TerminalRectangleProof);Assert.False(r.TerminalCornerProof);Assert.AreEqual(64,r.Region.Candidates.Count);
                // Fresh comparisons use detached declarations; owned roots cannot cross ledgers.
                r=NavDemandQueryWork.DetachSharedRecipe(r,((PartitionNavigationProvider)r.Provider).DetachedSnapshot());
                NavDemandQueryWork Prepare(NavigationRetentionBudget b,bool corners)
                {
                    Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var birth));using(birth){var provider=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)r.Provider,birth.OwnedReservation());
                    var raw=new NavScheduledRequest(provider,r.Clearance,r.Profile,r.Limits,r.Region,r.Subscription,r.MaxLabels,r.Singleton,r.Query,r.GroupQuery,r.DemandQuery,r.DemandQueryV2,r.DemandQueryV3,r.DemandQueryV4,r.DemandQueryV5,r.DemandQueryV6,r.LazyPartition,r.CertifiedLazyPartition,r.RectangularOrigins,true,corners);
                    var q=NavScheduledRequest.CopyOwned(raw,provider,birth.OwnedReservation());var n=new NavDemandQueryWork(q,birth.TryReserve);q.StorageRoot.ReleaseOwner();provider.StorageRoot.ReleaseOwner();
                    try{for(int i=0;LazyField(n,"phase").ToString()!="Seed";i++){Assert.Less(i,500000);Assert.AreEqual(NavSolveStatus.Pending,n.Status);V3Step(n);}n.Seal();birth.Attach(n);birth.Commit(n,_=>{});return n;}catch{n.ReleaseOwner();throw;}}
                }
                old=Prepare(oldBudget,false);fresh=Prepare(freshBudget,true);CollectionAssert.AreEqual(old.Save().Starts,fresh.Save().Starts);Assert.AreEqual(old.SampleCount,fresh.SampleCount);outer.ReleaseOwner();outer=null;PrefixNoLeaks(sourceBudget);
                long Run(NavigationRetentionBudget b,ref NavDemandQueryWork q,string name)
                {
                    long pops=0,proofs=0,candidates=0;int lastNode=-1,firstChosenGrant=-1,totalGrants=0;var clock=System.Diagnostics.Stopwatch.StartNew();
                    for(int grant=0;grant<50000&&q.Status==NavSolveStatus.Pending;grant++){
                        Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,grant+1,out var scope));NavDemandQueryWork n=null;try{using(scope){n=q.Fork(scope.TryReserve);int graph=256,connector=256,bookkeeping=256;
                        while(n.Status==NavSolveStatus.Pending){int active=(int)LazyField(n,"current");if(active>=0&&active!=lastNode){pops++;lastNode=active;}if(n.CanBookkeeping&&bookkeeping>0){n.StepBookkeeping();bookkeeping--;}
                        else if(n.CanGraph&&graph>0&&!(n.CanConnector&&connector>0)){n.StepGraph();graph--;}
                        else if(n.CanConnector&&connector>0){var phase=LazyField(n,"phase").ToString();if(phase=="Candidates"){candidates++;if((int)LazyField(n,"terminalBlocker")>=0)proofs++;}n.StepConnector();connector--;}
                        else break;}
                        if(firstChosenGrant<0&&(int)LazyField(n,"chosen")>=0)firstChosenGrant=grant+1;
                        totalGrants=grant+1;
                        if(totalGrants==64){string interim=name+" identical64 grants pops="+pops+" proofs="+proofs+" candidateActions="+candidates+" chosen="+LazyField(n,"chosen")+" status="+n.Status;File.WriteAllText(Path.Combine(folder,name+"-64.txt"),interim);TestContext.WriteLine(interim);}
                        n.Seal();scope.Attach(n);scope.Commit(n,_=>{});}q.ReleaseOwner();q=n;n=null;}finally{n?.ReleaseOwner();}}
                    string result=name+" grants="+totalGrants+" firstChosenGrant="+firstChosenGrant+" pops="+pops+" proofs="+proofs+" candidateConnector="+candidates+" status="+q.Status+" phase="+LazyField(q,"phase")+" frontier="+q.FrontierCount+" seconds="+clock.Elapsed.TotalSeconds;File.WriteAllText(Path.Combine(folder,name+"-fixed-grants.txt"),result);TestContext.WriteLine(result);Assert.AreEqual(NavSolveStatus.Ready,q.Status,"Fresh identical grants must reach completed objective");return candidates;
                }
                long previous=Run(oldBudget,ref old,"old-v10"),current=Run(freshBudget,ref fresh,"fresh-v11");Assert.Less(current,previous,"Corner proof must save candidate Connector actions through the completed objective");
                Assert.AreEqual(old.ExactObjective,fresh.ExactObjective);Assert.AreEqual(LazyField(old,"chosen"),LazyField(fresh,"chosen"));Assert.AreEqual(LazyField(old,"chosenCandidate"),LazyField(fresh,"chosenCandidate"));
            }finally{outer?.ReleaseOwner();old?.ReleaseOwner();fresh?.ReleaseOwner();}
            PrefixNoLeaks(sourceBudget);PrefixNoLeaks(oldBudget);PrefixNoLeaks(freshBudget);
        }
        [Test,Timeout(180000)] public void CampaignFreshOriginShadowIdenticalGrantsCompleteAndOldBodyCompatibility()
        {
            var source=Environment.GetEnvironmentVariable("CAMPAIGN_ORIGIN_BODY");var folder=Environment.GetEnvironmentVariable("CAMPAIGN_GROUP_QUERY_OUTPUT");Assert.IsNotEmpty(source);Assert.IsNotEmpty(folder);Directory.CreateDirectory(folder);
            var body=File.ReadAllBytes(source);using(var h=System.Security.Cryptography.SHA256.Create())Assert.AreEqual("69ea09a9cd41ce4b4d39089a9f7d8c8ba223b4f4bf7051e393a9ac97fcc499a7",BitConverter.ToString(h.ComputeHash(body)).Replace("-","").ToLowerInvariant());
            var sourceBudget=new NavigationRetentionBudget();NavigationArrivalWork outer=null;
            var oldBudget=new NavigationRetentionBudget();var freshBudget=new NavigationRetentionBudget();NavDemandQueryWork old=null,fresh=null;
            try{
                Assert.True(sourceBudget.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var decode));using(decode){outer=(NavigationArrivalWork)Decode(body,decode.OwnedReservation());decode.Attach(outer);decode.Commit(outer,_=>{});}
                Assert.True(sourceBudget.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var capture));NavigationCaptureBytes encoded=null;
                try{using(capture){var reservation=capture.OwnedReservation();encoded=NavigationCaptureBytes.Write(reservation,w=>NavPhysicalBodyWire.WriteTo(w,outer,reservation));capture.Attach(encoded);capture.Commit(encoded,_=>{});CollectionAssert.AreEqual(body,encoded.TransferPublic(),"Historical V11 BODY byte-identical reencode, EOF checked by Decode");encoded=null;}}finally{encoded?.Dispose();}
                LayeredNavigationScheduler.Job Find(NavigationArrivalWork w){var job=((NavPersistentQueue<LayeredNavigationScheduler.Job>)LazyField(PrefixScheduler(w),"jobs")).FirstOrDefault(j=>j.Demand!=null);if(job!=null)return job;var h=PrefixState(w)?.Oracle;Assert.NotNull(h);return Find(h);}
                var r=Find(outer).Request;Assert.True(r.TerminalRectangleProof);Assert.True(r.TerminalCornerProof);Assert.False(r.OriginShadowProof);Assert.AreEqual(64,r.Region.Candidates.Count);
                // Fresh comparisons use detached declarations; owned roots cannot cross ledgers.
                r=NavDemandQueryWork.DetachSharedRecipe(r,((PartitionNavigationProvider)r.Provider).DetachedSnapshot());
                NavDemandQueryWork Prepare(NavigationRetentionBudget b,bool shadow)
                {
                    Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var birth));using(birth){var provider=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)r.Provider,birth.OwnedReservation());
                    var raw=new NavScheduledRequest(provider,r.Clearance,r.Profile,r.Limits,r.Region,r.Subscription,r.MaxLabels,r.Singleton,r.Query,r.GroupQuery,r.DemandQuery,r.DemandQueryV2,r.DemandQueryV3,r.DemandQueryV4,r.DemandQueryV5,r.DemandQueryV6,r.LazyPartition,r.CertifiedLazyPartition,r.RectangularOrigins,true,true,shadow);
                    var q=NavScheduledRequest.CopyOwned(raw,provider,birth.OwnedReservation());var n=new NavDemandQueryWork(q,birth.TryReserve);q.StorageRoot.ReleaseOwner();provider.StorageRoot.ReleaseOwner();
                    try{n.Seal();birth.Attach(n);birth.Commit(n,_=>{});return n;}catch{n.ReleaseOwner();throw;}}
                }
                old=Prepare(oldBudget,false);fresh=Prepare(freshBudget,true);outer.ReleaseOwner();outer=null;PrefixNoLeaks(sourceBudget);
                long Run(NavigationRetentionBudget b,ref NavDemandQueryWork q,string name)
                {
                    long pops=0,proofs=0,candidates=0,originProbes=0,shadowProofs=0,skipped=0,seeds=0,graphs=0,bookkeepingActions=0;long dominated=0,boxAttempts=0,boxRejects=0,predictedSaved=0,directRejects=0,linfRejects=0;bool probe=Environment.GetEnvironmentVariable("CAMPAIGN_COST_PROBE")=="1";int lastNode=-1,firstChosenGrant=-1,totalGrants=0;var clock=System.Diagnostics.Stopwatch.StartNew();
                    for(int grant=0;grant<50000&&q.Status==NavSolveStatus.Pending;grant++){
                        Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,grant+1,out var scope));NavDemandQueryWork n=null;try{using(scope){n=q.Fork(scope.TryReserve);int graph=256,connector=256,bookkeeping=256;
                        while(n.Status==NavSolveStatus.Pending){int active=(int)LazyField(n,"current");if(active>=0&&active!=lastNode){pops++;lastNode=active;}if(n.CanBookkeeping&&bookkeeping>0){n.StepBookkeeping();bookkeeping--;bookkeepingActions++;}
                        else if(n.CanGraph&&graph>0&&!(n.CanConnector&&connector>0)){n.StepGraph();graph--;graphs++;}
                        else if(n.CanConnector&&connector>0){var phase=LazyField(n,"phase").ToString();bool shadowAction=(int)LazyField(n,"shadowBlocker")>=0;int beforeZ=(int)LazyField(n,"lazyZ");if(shadowAction)shadowProofs++;else if(phase=="Catalog"&&(int)LazyField(n,"terminal")>=2*r.Region.Candidates.Count&&(int)LazyField(n,"lazyStage")==4)originProbes++;if(phase=="Seed")seeds++;if(phase=="Candidates"){candidates++;if((int)LazyField(n,"terminalBlocker")>=0)proofs++;if(probe&&(int)LazyField(n,"chosen")>=0&&(int)LazyField(n,"candidate")<r.Region.Candidates.Count){
                            int cursor=(int)LazyField(n,"candidate"),nodeId=(int)LazyField(n,"current");var node=(NavLocation)typeof(NavDemandQueryWork).GetMethod("NodeLocation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(n,new object[]{nodeId});var distances=(NavCowDictionary<int,NavExactCost>)LazyField(n,"exactDistance");var best=(NavExactCost)LazyField(n,"exactBest");var ends=(NavCowDictionary<int,double>)LazyField(n,"ends");
                            if(ends.TryGetValue(cursor,out var egress)&&distances[nodeId].Add(LayeredNavigationProvider.Distance(node.Position,r.Region.Candidates[cursor].Position)).Add(egress)>best)dominated++;
                            if(cursor==0){boxAttempts++;var endpoint=r.Subscription.AssignedEndpoint.Position;var direct=NavExactCost.ConservativeHeuristicScale(NavExactCost.FromDouble(LayeredNavigationProvider.Distance(node.Position,endpoint)));var dx=NavExactCost.Difference(node.Position.X,endpoint.X);var dz=NavExactCost.Difference(node.Position.Z,endpoint.Z);var linf=NavExactCost.ConservativeHeuristicScale(dx>=dz?dx:dz);if(distances[nodeId].Add(direct)>best)directRejects++;if(distances[nodeId].Add(linf)>best)linfRejects++;var box=(NavHeuristicBox)LazyField(n,"terminalBox");var closest=new NavPoint(Math.Max(box.MinX,Math.Min(box.MaxX,node.Position.X)),Math.Max(box.MinZ,Math.Min(box.MaxZ,node.Position.Z)));if(distances[nodeId].Add(LayeredNavigationProvider.Distance(node.Position,closest))>best){boxRejects++;predictedSaved+=r.Region.Candidates.Count;}}
                        }}n.StepConnector();connector--;if(shadowAction&&(int)LazyField(n,"shadowBlocker")<0)skipped+=Math.Max(0,(int)LazyField(n,"lazyZ")-beforeZ);}
                        else break;}
                        if(firstChosenGrant<0&&(int)LazyField(n,"chosen")>=0)firstChosenGrant=grant+1;
                        totalGrants=grant+1;
                        if(totalGrants==64){string interim=name+" identical64 grants pops="+pops+" proofs="+proofs+" candidateActions="+candidates+" chosen="+LazyField(n,"chosen")+" status="+n.Status;File.WriteAllText(Path.Combine(folder,name+"-64.txt"),interim);TestContext.WriteLine(interim);}
                        n.Seal();scope.Attach(n);scope.Commit(n,_=>{});}q.ReleaseOwner();q=n;n=null;}finally{n?.ReleaseOwner();}}
                    string result=name+" grants="+totalGrants+" firstChosenGrant="+firstChosenGrant+" pops="+pops+" proofs="+proofs+" candidateConnector="+candidates+" origins="+originProbes+" shadowProofs="+shadowProofs+" skipped="+skipped+" seeds="+seeds+" graph="+graphs+" bookkeeping="+bookkeepingActions+" dominated="+dominated+" boxAttempts="+boxAttempts+" boxRejects="+boxRejects+" predictedNetSaved="+(predictedSaved-boxAttempts)+" directRejects="+directRejects+" directNetSaved="+(64*directRejects-boxAttempts)+" linfRejects="+linfRejects+" status="+q.Status+" phase="+LazyField(q,"phase")+" frontier="+q.FrontierCount+" seconds="+clock.Elapsed.TotalSeconds;File.WriteAllText(Path.Combine(folder,name+"-fixed-grants.txt"),result);TestContext.WriteLine(result);Assert.AreEqual(NavSolveStatus.Ready,q.Status,"Fresh identical grants must reach completed objective");return totalGrants;
                }
                long previous=Run(oldBudget,ref old,"old-v11"),current=Run(freshBudget,ref fresh,"fresh-v12");Assert.Less(current,previous,"Origin shadow must reduce full preparation+solve grants with identical quotas");
                CollectionAssert.AreEqual(old.Save().Starts,fresh.Save().Starts,"Complete full-ID feasible Origins/costs must match");Assert.AreEqual(old.ExactObjective,fresh.ExactObjective);Assert.AreEqual(LazyField(old,"chosen"),LazyField(fresh,"chosen"));Assert.AreEqual(LazyField(old,"chosenCandidate"),LazyField(fresh,"chosenCandidate"));
            }finally{outer?.ReleaseOwner();old?.ReleaseOwner();fresh?.ReleaseOwner();}
            PrefixNoLeaks(sourceBudget);PrefixNoLeaks(oldBudget);PrefixNoLeaks(freshBudget);
        }        [Test,Timeout(180000)] public void CampaignFreshTerminalCostIdenticalGrantsCompleteAndOldBodyCompatibility()
        {
            var source=Environment.GetEnvironmentVariable("CAMPAIGN_ORIGIN_BODY");var folder=Environment.GetEnvironmentVariable("CAMPAIGN_GROUP_QUERY_OUTPUT");Assert.IsNotEmpty(source);Assert.IsNotEmpty(folder);Directory.CreateDirectory(folder);
            var body=File.ReadAllBytes(source);using(var h=System.Security.Cryptography.SHA256.Create())Assert.AreEqual("69ea09a9cd41ce4b4d39089a9f7d8c8ba223b4f4bf7051e393a9ac97fcc499a7",BitConverter.ToString(h.ComputeHash(body)).Replace("-","").ToLowerInvariant());
            var sourceBudget=new NavigationRetentionBudget();NavigationArrivalWork outer=null;
            var oldBudget=new NavigationRetentionBudget();var freshBudget=new NavigationRetentionBudget();NavDemandQueryWork old=null,fresh=null;
            try{
                Assert.True(sourceBudget.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var decode));using(decode){outer=(NavigationArrivalWork)Decode(body,decode.OwnedReservation());decode.Attach(outer);decode.Commit(outer,_=>{});}
                Assert.True(sourceBudget.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var capture));NavigationCaptureBytes encoded=null;
                try{using(capture){var reservation=capture.OwnedReservation();encoded=NavigationCaptureBytes.Write(reservation,w=>NavPhysicalBodyWire.WriteTo(w,outer,reservation));capture.Attach(encoded);capture.Commit(encoded,_=>{});CollectionAssert.AreEqual(body,encoded.TransferPublic(),"Historical V11 BODY byte-identical reencode, EOF checked by Decode");encoded=null;}}finally{encoded?.Dispose();}
                LayeredNavigationScheduler.Job Find(NavigationArrivalWork w){var job=((NavPersistentQueue<LayeredNavigationScheduler.Job>)LazyField(PrefixScheduler(w),"jobs")).FirstOrDefault(j=>j.Demand!=null);if(job!=null)return job;var h=PrefixState(w)?.Oracle;Assert.NotNull(h);return Find(h);}
                var r=Find(outer).Request;Assert.True(r.TerminalRectangleProof);Assert.True(r.TerminalCornerProof);Assert.False(r.OriginShadowProof);Assert.AreEqual(64,r.Region.Candidates.Count);
                // Fresh comparisons use detached declarations; owned roots cannot cross ledgers.
                r=NavDemandQueryWork.DetachSharedRecipe(r,((PartitionNavigationProvider)r.Provider).DetachedSnapshot());
                NavDemandQueryWork Prepare(NavigationRetentionBudget b,bool shadow)
                {
                    Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var birth));using(birth){var provider=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)r.Provider,birth.OwnedReservation());
                    var raw=new NavScheduledRequest(provider,r.Clearance,r.Profile,r.Limits,r.Region,r.Subscription,r.MaxLabels,r.Singleton,r.Query,r.GroupQuery,r.DemandQuery,r.DemandQueryV2,r.DemandQueryV3,r.DemandQueryV4,r.DemandQueryV5,r.DemandQueryV6,r.LazyPartition,r.CertifiedLazyPartition,r.RectangularOrigins,true,true,true,shadow);
                    var q=NavScheduledRequest.CopyOwned(raw,provider,birth.OwnedReservation());var n=new NavDemandQueryWork(q,birth.TryReserve);q.StorageRoot.ReleaseOwner();provider.StorageRoot.ReleaseOwner();
                    try{n.Seal();birth.Attach(n);birth.Commit(n,_=>{});return n;}catch{n.ReleaseOwner();throw;}}
                }
                old=Prepare(oldBudget,false);fresh=Prepare(freshBudget,true);outer.ReleaseOwner();outer=null;PrefixNoLeaks(sourceBudget);
                long Run(NavigationRetentionBudget b,ref NavDemandQueryWork q,string name)
                {
                    long pops=0,proofs=0,candidates=0,originProbes=0,shadowProofs=0,skipped=0,seeds=0,graphs=0,bookkeepingActions=0;long dominated=0,boxAttempts=0,boxRejects=0,predictedSaved=0,directRejects=0,linfRejects=0;bool probe=Environment.GetEnvironmentVariable("CAMPAIGN_COST_PROBE")=="1";int lastNode=-1,firstChosenGrant=-1,totalGrants=0;var clock=System.Diagnostics.Stopwatch.StartNew();
                    for(int grant=0;grant<50000&&q.Status==NavSolveStatus.Pending;grant++){
                        Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,grant+1,out var scope));NavDemandQueryWork n=null;try{using(scope){n=q.Fork(scope.TryReserve);int graph=256,connector=256,bookkeeping=256;
                        while(n.Status==NavSolveStatus.Pending){int active=(int)LazyField(n,"current");if(active>=0&&active!=lastNode){pops++;lastNode=active;}if(n.CanBookkeeping&&bookkeeping>0){n.StepBookkeeping();bookkeeping--;bookkeepingActions++;}
                        else if(n.CanGraph&&graph>0&&!(n.CanConnector&&connector>0)){n.StepGraph();graph--;graphs++;}
                        else if(n.CanConnector&&connector>0){var phase=LazyField(n,"phase").ToString();bool shadowAction=(int)LazyField(n,"shadowBlocker")>=0;int beforeZ=(int)LazyField(n,"lazyZ");if(shadowAction)shadowProofs++;else if(phase=="Catalog"&&(int)LazyField(n,"terminal")>=2*r.Region.Candidates.Count&&(int)LazyField(n,"lazyStage")==4)originProbes++;if(phase=="Seed")seeds++;if(phase=="Candidates"){candidates++;if((int)LazyField(n,"terminalBlocker")>=0)proofs++;if(probe&&(int)LazyField(n,"chosen")>=0&&(int)LazyField(n,"candidate")<r.Region.Candidates.Count){
                            int cursor=(int)LazyField(n,"candidate"),nodeId=(int)LazyField(n,"current");var node=(NavLocation)typeof(NavDemandQueryWork).GetMethod("NodeLocation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(n,new object[]{nodeId});var distances=(NavCowDictionary<int,NavExactCost>)LazyField(n,"exactDistance");var best=(NavExactCost)LazyField(n,"exactBest");var ends=(NavCowDictionary<int,double>)LazyField(n,"ends");
                            if(ends.TryGetValue(cursor,out var egress)&&distances[nodeId].Add(LayeredNavigationProvider.Distance(node.Position,r.Region.Candidates[cursor].Position)).Add(egress)>best)dominated++;
                            if(cursor==0){boxAttempts++;var endpoint=r.Subscription.AssignedEndpoint.Position;var direct=NavExactCost.ConservativeHeuristicScale(NavExactCost.FromDouble(LayeredNavigationProvider.Distance(node.Position,endpoint)));var dx=NavExactCost.Difference(node.Position.X,endpoint.X);var dz=NavExactCost.Difference(node.Position.Z,endpoint.Z);var linf=NavExactCost.ConservativeHeuristicScale(dx>=dz?dx:dz);if(distances[nodeId].Add(direct)>best)directRejects++;if(distances[nodeId].Add(linf)>best)linfRejects++;var box=(NavHeuristicBox)LazyField(n,"terminalBox");var closest=new NavPoint(Math.Max(box.MinX,Math.Min(box.MaxX,node.Position.X)),Math.Max(box.MinZ,Math.Min(box.MaxZ,node.Position.Z)));if(distances[nodeId].Add(LayeredNavigationProvider.Distance(node.Position,closest))>best){boxRejects++;predictedSaved+=r.Region.Candidates.Count;}}
                        }}n.StepConnector();connector--;if(shadowAction&&(int)LazyField(n,"shadowBlocker")<0)skipped+=Math.Max(0,(int)LazyField(n,"lazyZ")-beforeZ);}
                        else break;}
                        if(firstChosenGrant<0&&(int)LazyField(n,"chosen")>=0)firstChosenGrant=grant+1;
                        totalGrants=grant+1;
                        if(totalGrants==64){string interim=name+" identical64 grants pops="+pops+" proofs="+proofs+" candidateActions="+candidates+" chosen="+LazyField(n,"chosen")+" status="+n.Status;File.WriteAllText(Path.Combine(folder,name+"-64.txt"),interim);TestContext.WriteLine(interim);}
                        n.Seal();scope.Attach(n);scope.Commit(n,_=>{});}q.ReleaseOwner();q=n;n=null;}finally{n?.ReleaseOwner();}}
                    string result=name+" grants="+totalGrants+" firstChosenGrant="+firstChosenGrant+" pops="+pops+" proofs="+proofs+" candidateConnector="+candidates+" origins="+originProbes+" shadowProofs="+shadowProofs+" skipped="+skipped+" seeds="+seeds+" graph="+graphs+" bookkeeping="+bookkeepingActions+" dominated="+dominated+" boxAttempts="+boxAttempts+" boxRejects="+boxRejects+" predictedNetSaved="+(predictedSaved-boxAttempts)+" directRejects="+directRejects+" directNetSaved="+(64*directRejects-boxAttempts)+" linfRejects="+linfRejects+" status="+q.Status+" phase="+LazyField(q,"phase")+" frontier="+q.FrontierCount+" seconds="+clock.Elapsed.TotalSeconds;File.WriteAllText(Path.Combine(folder,name+"-fixed-grants.txt"),result);TestContext.WriteLine(result);Assert.AreEqual(NavSolveStatus.Ready,q.Status,"Fresh identical grants must reach completed objective");return totalGrants;
                }
                long previous=Run(oldBudget,ref old,"old-v12"),current=Run(freshBudget,ref fresh,"fresh-v13");Assert.Less(current,previous,"Terminal cost proof must reduce full preparation+solve grants with identical quotas");
                CollectionAssert.AreEqual(old.Save().Starts,fresh.Save().Starts,"Complete full-ID feasible Origins/costs must match");Assert.AreEqual(old.ExactObjective,fresh.ExactObjective);Assert.AreEqual(LazyField(old,"chosen"),LazyField(fresh,"chosen"));Assert.AreEqual(LazyField(old,"chosenCandidate"),LazyField(fresh,"chosenCandidate"));
            }finally{outer?.ReleaseOwner();old?.ReleaseOwner();fresh?.ReleaseOwner();}
            PrefixNoLeaks(sourceBudget);PrefixNoLeaks(oldBudget);PrefixNoLeaks(freshBudget);
        }
    }
}
