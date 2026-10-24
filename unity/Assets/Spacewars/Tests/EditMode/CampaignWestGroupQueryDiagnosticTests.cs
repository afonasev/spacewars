using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        static string BoxText(NavHeuristicBox b)=>b.MinX.ToString("R")+","+b.MinZ.ToString("R")+".."+b.MaxX.ToString("R")+","+b.MaxZ.ToString("R");

        [Test,Timeout(180000)] public void CampaignWestGroupPendingQueryBodyAndFixedGrantDiagnosis()
        {
            string first=Environment.GetEnvironmentVariable("CAMPAIGN_GROUP_BODY_ONE"),second=Environment.GetEnvironmentVariable("CAMPAIGN_GROUP_BODY_TWO"),folder=Environment.GetEnvironmentVariable("CAMPAIGN_GROUP_QUERY_OUTPUT");
            Assert.IsNotEmpty(first);Assert.IsNotEmpty(second);Assert.IsNotEmpty(folder);Directory.CreateDirectory(folder);
            var sources=new[]{File.ReadAllBytes(first),File.ReadAllBytes(second)};
            var expected=new[]{"bb3fda91c9a195a45ab4a4bea77e2b37e6692c6cea177540a2a288398e39d802","d4a6915eafd16004647b0f570d9c7ab8dc724e05cb9acef06c4886c792ab1519"};
            string Sha(byte[] b){using(var h=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
            var reports=new StringBuilder("Real ordinary west-group candidate-phase BODY diagnosis. Exact bytes are decoded and re-encoded before analysis.\n");
            var budget=new NavigationRetentionBudget();NavDemandQueryWork query=null;NavigationArrivalWork outer=null;NavigationCaptureBytes transport=null;var overallClock=Stopwatch.StartNew();double preparationSeconds=0;
            try
            {
                for(int sample=0;sample<2;sample++)
                {
                    byte[] body=sources[sample];Assert.AreEqual(expected[sample],Sha(body),"candidate body hash #"+(sample+1));
                    transport=NavigationCaptureBytes.CreateOwned(body.Length,budget,_=>body);
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,sample+1,0,out var decode));
                    using(decode)
                    {
                        using(var stream=new MemoryStream(transport.Bytes,false))using(var reader=new BinaryReader(stream))
                        {outer=(NavigationArrivalWork)NavPhysicalBodyWire.ReadFrom(reader,decode.OwnedReservation());Assert.AreEqual(stream.Length,stream.Position,"body EOF #"+(sample+1));}
                        decode.Attach(outer);decode.Commit(outer,_=>{});
                    }
                    transport.Dispose();transport=null;
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,sample+1,1,out var capture));NavigationCaptureBytes encoded=null;
                    try{using(capture){var reservation=capture.OwnedReservation();encoded=NavigationCaptureBytes.Write(reservation,w=>NavPhysicalBodyWire.WriteTo(w,outer,reservation));capture.Attach(encoded);capture.Commit(encoded,_=>{});CollectionAssert.AreEqual(body,encoded.TransferPublic(),"exact owned BODY re-encode #"+(sample+1));encoded=null;}}finally{encoded?.Dispose();}
                    var candidates=new List<NavDemandQueryWork>();var seen=new HashSet<NavigationArrivalWork>();
                    void Visit(NavigationArrivalWork work)
                    {
                        if(work==null||!seen.Add(work))return;
                        var scheduler=(LayeredNavigationScheduler)LazyField(work,"scheduler");var jobs=(NavPersistentQueue<LayeredNavigationScheduler.Job>)LazyField(scheduler,"jobs");
                        foreach(var job in jobs)if(job?.Demand!=null)candidates.Add(job.Demand);
                        var oracle=(NavigationArrivalWork)typeof(NavigationArrivalWork).GetProperty("historicalOracle",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(work);Visit(oracle);
                    }
                    Visit(outer);
                    var selected=candidates.FirstOrDefault(q=>((NavScheduledRequest)LazyField(q,"request")).TerminalRectangleProof&&LazyField(q,"phase").ToString()=="Candidates")
                        ??candidates.FirstOrDefault(q=>((NavScheduledRequest)LazyField(q,"request")).TerminalRectangleProof);
                    Assert.NotNull(selected,"terminal-rectangle query found in current/historical arrival scheduler #"+(sample+1));
                    query=selected;query.Retain();
                    var request=(NavScheduledRequest)LazyField(query,"request");Assert.True(request.TerminalRectangleProof);Assert.AreEqual(64,request.Region.Candidates.Count);
                    int current=(int)LazyField(query,"current"),candidate=(int)LazyField(query,"candidate");var store=(NavLazyPartitionStore)LazyField(query,"lazyStore");var from=store.Location(current);var provider=(PartitionNavigationProvider)request.Provider;
                    var firstPoint=request.Region.Candidates[0].Position;var terminalBox=new NavHeuristicBox{MinX=firstPoint.X,MaxX=firstPoint.X,MinZ=firstPoint.Z,MaxZ=firstPoint.Z};for(int i=1;i<request.Region.Candidates.Count;i++)terminalBox.Include(request.Region.Candidates[i].Position);Assert.AreEqual(terminalBox,(NavHeuristicBox)LazyField(query,"terminalBox"),"Probe must use the exact paid terminal hull");
                    reports.AppendLine("sample"+(sample+1)+" SHA256="+Sha(body)+" byteRoundTrip=exact EOF=checked input="+((NavigationArrivalWork)outer).InputBinding+" phase="+LazyField(query,"phase")+" node="+current+" nodePosition="+from.Position.X.ToString("R")+","+from.Position.Z.ToString("R")+" candidate="+candidate+" candidatePosition="+request.Region.Candidates[candidate].Position.X.ToString("R")+","+request.Region.Candidates[candidate].Position.Z.ToString("R"));
                    object Count(object value)=>value.GetType().GetProperty("Count",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(value);
                    reports.AppendLine("terminal64 hull="+terminalBox.MinX.ToString("R")+","+terminalBox.MinZ.ToString("R")+".."+terminalBox.MaxX.ToString("R")+","+terminalBox.MaxZ.ToString("R")+" actualPaidHull="+BoxText((NavHeuristicBox)LazyField(query,"terminalBox"))+" hEligible="+LazyField(query,"hEligible")+" hCandidateCursor="+LazyField(query,"hCandidateCursor")+" starts="+Count(LazyField(query,"starts"))+" ends="+Count(LazyField(query,"ends"))+" labels="+Count(LazyField(query,"exactDistance"))+" frontier="+query.FrontierCount+" chosen="+LazyField(query,"chosen")+" chosenCandidate="+LazyField(query,"chosenCandidate")+" queryFlags="+request.DemandQuery+"/"+request.DemandQueryV6+"/"+request.CertifiedLazyPartition+"/"+request.RectangularOrigins+"/"+request.TerminalRectangleProof);
                    reports.AppendLine("arrivalCounters graph="+outer.RouteCounters.PhysicalGraphWork+" connector="+outer.RouteCounters.ConnectorWork);
                    var egress=LazyField(query,"ends");var egressTry=egress.GetType().GetMethod("TryGetValue",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                    var blockers=new SortedDictionary<int,int>();var egressBlockers=new SortedDictionary<int,int>();int egressEligible=0,productionEgressVisible=0,originVisible=0;
                    for(int endpointIndex=0;endpointIndex<request.Region.Candidates.Count;endpointIndex++)
                    {
                        var endpoint=request.Region.Candidates[endpointIndex];
                        bool has=(bool)egressTry.Invoke(egress,new object[]{endpointIndex,null});if(has)egressEligible++;
                        bool endSweep=provider.CanSweep(endpoint,request.Subscription.AssignedEndpoint,request.Clearance,out int endBlocker);if(endSweep)productionEgressVisible++;else{egressBlockers.TryGetValue(endBlocker,out int endCount);egressBlockers[endBlocker]=endCount+1;}
                        bool sweep=provider.CanSweep(from,endpoint,request.Clearance,out int blocker);if(sweep)originVisible++;else{blockers.TryGetValue(blocker,out int originCount);blockers[blocker]=originCount+1;}
                    }
                    reports.AppendLine("endpoints=64 queryEgressEligible="+egressEligible+" productionEgressSweepEligible="+productionEgressVisible+" productionEgressBlockerHistogram="+string.Join(",",egressBlockers.Select(p=>p.Key+":"+p.Value))+" originSweepEligible="+originVisible+" originBlockerHistogram="+string.Join(",",blockers.Select(p=>p.Key+":"+p.Value)));
                    foreach(var pair in blockers.Concat(egressBlockers).GroupBy(p=>p.Key).Select(g=>new KeyValuePair<int,int>(g.Key,g.Sum(p=>p.Value))).OrderBy(p=>p.Key))
                    {
                        bool reject=provider.RejectTerminalRectangle(from.Position,terminalBox,request.Clearance.Radius,pair.Key);
                        string obstacle=pair.Key>=0&&pair.Key<provider.ExclusionObstacles.Count?provider.ExclusionObstacles[pair.Key].MinX.ToString("R")+","+provider.ExclusionObstacles[pair.Key].MinZ.ToString("R")+".."+provider.ExclusionObstacles[pair.Key].MaxX.ToString("R")+","+provider.ExclusionObstacles[pair.Key].MaxZ.ToString("R"):"out-of-range";
                        reports.AppendLine("blocker="+pair.Key+" count="+pair.Value+" rectangleRejectCurrent="+reject+" obstacle="+obstacle);
                    }
                    var actualBlocker=(int)LazyField(query,"terminalBlocker");reports.AppendLine("actualTerminalBlocker="+actualBlocker+" actualBlockerRejectsCurrentHull="+(actualBlocker>=0&&provider.RejectTerminalRectangle(from.Position,terminalBox,request.Clearance.Radius,actualBlocker)));
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,sample+1,2,out var afterProbe));NavigationCaptureBytes checkedBytes=null;
                    try{using(afterProbe){checkedBytes=NavigationCaptureBytes.Write(afterProbe.OwnedReservation(),w=>NavPhysicalBodyWire.WriteTo(w,outer,afterProbe.OwnedReservation()));afterProbe.Attach(checkedBytes);afterProbe.Commit(checkedBytes,_=>{});CollectionAssert.AreEqual(body,checkedBytes.TransferPublic(),"Read-only probe preserves actual BODY");checkedBytes=null;}}finally{checkedBytes?.Dispose();}
                    outer.ReleaseOwner();outer=null;if(sample==0){query.ReleaseOwner();query=null;PrefixNoLeaks(budget);}preparationSeconds=overallClock.Elapsed.TotalSeconds;
                }
                // The second body is the only one advanced. Preserve the real production fixed grant sizes.
                var fixedReport=new StringBuilder("sample2 only; fixed query grants Graph256/Connector256/Bookkeeping256; diagnostic is not a route result.\n");
                var routeRequest=(NavScheduledRequest)LazyField(query,"request");var routeProvider=(PartitionNavigationProvider)routeRequest.Provider;var routeStore=(NavLazyPartitionStore)LazyField(query,"lazyStore");
                var initialChosen=(int)LazyField(query,"chosen");long pops=0,normalAttempts=0,normalSuccess=0,rectangleAttempts=0,rectangleSuccess=0,graphActions=0;int lastNode=-1,firstChosen=-1;bool firstChosenChanged=false;var solveClock=Stopwatch.StartNew();
                for(int grant=0;grant<64&&query.Status==NavSolveStatus.Pending;grant++)
                {
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,100+grant,out var scope));NavDemandQueryWork next=null;
                    try
                    {
                        using(scope){next=query.Fork(scope.TryReserve);int graph=256,connector=256,bookkeeping=256;
                            while(next.Status==NavSolveStatus.Pending)
                            {
                                int node=(int)LazyField(next,"current");if(node>=0&&node!=lastNode){pops++;lastNode=node;}
                                if(next.CanBookkeeping&&bookkeeping>0){next.StepBookkeeping();bookkeeping--;}
                                else if(next.CanGraph&&graph>0&&!(next.CanConnector&&connector>0)){next.StepGraph();graph--;graphActions++;}
                                else if(next.CanConnector&&connector>0)
                                {
                                    bool isCandidate=LazyField(next,"phase").ToString()=="Candidates";int blocker=(int)LazyField(next,"terminalBlocker"),beforeCandidate=(int)LazyField(next,"candidate"),beforeChosen=(int)LazyField(next,"chosen"),beforeChosenCandidate=(int)LazyField(next,"chosenCandidate");
                                    bool proofAction=isCandidate&&blocker>=0,normalAction=isCandidate&&blocker<0&&beforeCandidate<routeRequest.Region.Candidates.Count;
                                    if(proofAction)rectangleAttempts++;else if(normalAction)normalAttempts++;
                                    next.StepConnector();connector--;
                                    if(proofAction&&(int)LazyField(next,"candidate")==routeRequest.Region.Candidates.Count)rectangleSuccess++;
                                    if(normalAction&&((int)LazyField(next,"chosen")!=beforeChosen||(int)LazyField(next,"chosenCandidate")!=beforeChosenCandidate))normalSuccess++;
                                    int chosen=(int)LazyField(next,"chosen");if(!firstChosenChanged&&chosen>=0&&chosen!=initialChosen){firstChosen=chosen;firstChosenChanged=true;}
                                }
                                else break;
                            }
                            next.Seal();scope.Attach(next);scope.Commit(next,_=>{});
                        }
                        query.ReleaseOwner();query=next;next=null;
                    }
                    finally{next?.ReleaseOwner();}
                }
                fixedReport.AppendLine("grants=64 quota=Graph256/Connector256/Bookkeeping256 graphActions="+graphActions+" status="+query.Status+" phase="+LazyField(query,"phase")+" popsDistinctNode="+pops+" initialChosen="+initialChosen+" firstChosenChange="+(firstChosenChanged?firstChosen.ToString():"none")+" chosen="+LazyField(query,"chosen")+" frontier="+query.FrontierCount);
                fixedReport.AppendLine("normalCandidateAttempts="+normalAttempts+" candidateBestImprovements="+normalSuccess+" rectangleProofAttempts="+rectangleAttempts+" rectangleProofSuccess="+rectangleSuccess+" preparationSeconds="+preparationSeconds.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+" solveSeconds="+solveClock.Elapsed.TotalSeconds.ToString("F6",System.Globalization.CultureInfo.InvariantCulture));
                File.WriteAllText(Path.Combine(folder,"west-group-query-diagnosis.txt"),reports.Append(fixedReport).ToString());TestContext.WriteLine(reports.ToString());
            }
            finally{transport?.Dispose();outer?.ReleaseOwner();query?.ReleaseOwner();}
            PrefixNoLeaks(budget);
        }
    }
}
