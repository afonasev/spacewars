using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        [Test,Timeout(180000)] public void CampaignMatchedPendingSearchFixedGrants()
        {
            string source=Environment.GetEnvironmentVariable("CAMPAIGN_SEARCH_BODY"),folder=Environment.GetEnvironmentVariable("CAMPAIGN_SEARCH_OUTPUT");Assert.IsNotEmpty(source);Assert.IsNotEmpty(folder);Directory.CreateDirectory(folder);
            byte[] body=File.ReadAllBytes(source);string Sha(byte[] b){using(var h=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
            Assert.AreEqual("0772376bcb9d85125065eeb5fb145471631c301e55a4044748f6273abd855f8e",Sha(body));
            var budget=new NavigationRetentionBudget();NavigationArrivalWork work=null;NavDemandQueryWork query=null;NavigationCaptureBytes input=null;
            var clock=Stopwatch.StartNew();var counts=new Dictionary<string,long>();var cpu=new Dictionary<string,long>();var trace=new List<string>{"grant,phase,chosen,candidate,labels,frontier,seconds,physicalBytes"};int grants=0;
            try{
                input=NavigationCaptureBytes.CreateOwned(body.Length,budget,_=>body);Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var decode));
                using(decode){using(var stream=new MemoryStream(input.Bytes,false))using(var reader=new BinaryReader(stream)){work=(NavigationArrivalWork)NavPhysicalBodyWire.ReadFrom(reader,decode.OwnedReservation());Assert.AreEqual(stream.Length,stream.Position,"EOF");}decode.Attach(work);decode.Commit(work,_=>{});}input.Dispose();input=null;
                Assert.AreEqual("a2c7bd3112b1b0aad549ba2537e1f18c4d022a7feb42028f87525c7f9b4e4c2c",work.InputBinding);
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var capture));NavigationCaptureBytes output=null;
                try{using(capture){var reservation=capture.OwnedReservation();output=NavigationCaptureBytes.Write(reservation,w=>NavPhysicalBodyWire.WriteTo(w,work,reservation));capture.Attach(output);capture.Commit(output,_=>{});CollectionAssert.AreEqual(body,output.TransferPublic(),"Exact reencode before any work");output=null;}}finally{output?.Dispose();}
                var job=((NavPersistentQueue<LayeredNavigationScheduler.Job>)LazyField(PrefixScheduler(work),"jobs")).First;Assert.NotNull(job?.Demand);query=job.Demand;query.Retain();Assert.True(query.Matches(job.Request));Assert.True(job.Request.RectangularOrigins);Assert.AreEqual(64,job.Request.Region.Candidates.Count);work.ReleaseOwner();work=null;
                string Phase(NavDemandQueryWork q)=>LazyField(q,"phase").ToString();
                var report=new StringBuilder("Matched actual pending Demand; isolated fixed-grant diagnostic only, no ordinary game/arrival qualification.\n");report.AppendLine("BODY_SHA256="+Sha(body)+" input roundtrip=identical source release=true");
                var request=(NavScheduledRequest)LazyField(query,"request");int current=(int)LazyField(query,"current"),candidate=(int)LazyField(query,"candidate");var store=(NavLazyPartitionStore)LazyField(query,"lazyStore");var from=store.Location(current);var to=request.Region.Candidates[candidate];report.AppendLine("initial phase="+Phase(query)+" node="+current+" nodePosition="+from.Position.X+","+from.Position.Z+" candidate="+candidate+" target="+to.Position.X+","+to.Position.Z+" provider="+request.Provider.Binding);
                var geometry=((TerrainLocations)LazyField(request.Provider,"locations")).Geometry;
                Assert.True(request.Provider.IsValid(from,request.Clearance));
                Assert.False(request.Provider.CanSweep(from,to,request.Clearance));
                Assert.False(geometry.SegmentFree(from.Position,to.Position,request.Clearance.Radius));
                double Cross(NavPoint a,NavPoint b,NavPoint p)=>(b.X-a.X)*(p.Z-a.Z)-(b.Z-a.Z)*(p.X-a.X);
                bool Strict(NavPoint a,NavPoint b,NavPoint c,NavPoint d){double x=Cross(a,b,c),y=Cross(a,b,d),u=Cross(c,d,a),v=Cross(c,d,b);return ((x>0&&y<0)||(x<0&&y>0))&&((u>0&&v<0)||(u<0&&v>0));}
                foreach(var endpoint in request.Region.Candidates)Assert.True(request.Provider.IsValid(endpoint,request.Clearance));
                int blockers=0;
                for(int obstacle=0;obstacle<geometry.Obstacles.Count;obstacle++){
                    var r=geometry.Obstacles[obstacle];if(r.Polygon!=null||r.CircleRadius>0)continue;
                    var corners=new[]{new NavPoint(r.MinX,r.MinZ),new NavPoint(r.MaxX,r.MinZ),new NavPoint(r.MaxX,r.MaxZ),new NavPoint(r.MinX,r.MaxZ)};
                    for(int edge=0;edge<4;edge++)if(request.Region.Candidates.All(endpoint=>Strict(from.Position,endpoint.Position,corners[edge],corners[(edge+1)%4]))){
                        blockers++;report.AppendLine("actual rectangle blocker="+obstacle+" bounds="+r.MinX+","+r.MinZ+","+r.MaxX+","+r.MaxZ+" edge="+edge+" strictSameEdgeCandidates=64 endpointValid=64");break;
                    }
                }
                Assert.Greater(blockers,0,"Actual one-rectangle strict-sign applicability; no universal AABB proof claimed");
                double preparation=clock.Elapsed.TotalSeconds;
                for(;grants<64&&query.Status==NavSolveStatus.Pending;grants++){
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,grants+1000,out var scope));NavDemandQueryWork next=null;
                    try{using(scope){next=query.Fork(scope.TryReserve);int graph=256,connector=256,bookkeeping=256;
                        while(next.Status==NavSolveStatus.Pending){string lane;
                            if(next.CanBookkeeping&&bookkeeping>0)lane="Bookkeeping";
                            else if(next.CanGraph&&graph>0&&!(next.CanConnector&&connector>0))lane="Graph";
                            else if(next.CanConnector&&connector>0)lane="Connector";
                            else break;
                            string key=lane+":"+Phase(next)+(Phase(next)=="Candidates"?((int)LazyField(next,"chosen")<0?":noBest":":withBest"):"");long started=Stopwatch.GetTimestamp();
                            if(lane=="Bookkeeping"){next.StepBookkeeping();bookkeeping--;}else if(lane=="Graph"){next.StepGraph();graph--;}else{next.StepConnector();connector--;}
                            long elapsed=Stopwatch.GetTimestamp()-started;counts.TryGetValue(key,out long n);counts[key]=n+1;cpu.TryGetValue(key,out long ticks);cpu[key]=ticks+elapsed;
                        }
                        next.Seal();scope.Attach(next);scope.Commit(next,_=>{});}
                        query.ReleaseOwner();query=next;next=null;
                    }finally{next?.ReleaseOwner();}
                    trace.Add((grants+1)+","+Phase(query)+","+LazyField(query,"chosen")+","+LazyField(query,"candidate")+","+LazyField(query,"exactDistance").GetType().GetProperty("Count").GetValue(LazyField(query,"exactDistance"))+","+query.FrontierCount+","+(clock.Elapsed.TotalSeconds-preparation).ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+","+budget.PhysicalBytes);
                }
                report.AppendLine("grants="+grants+" quotaPerGrant=Graph256/Connector256/Bookkeeping256 status="+query.Status+" phase="+Phase(query)+" chosen="+LazyField(query,"chosen")+" frontier="+query.FrontierCount+" preparationSeconds="+preparation.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+" solveSeconds="+(clock.Elapsed.TotalSeconds-preparation).ToString("F6",System.Globalization.CultureInfo.InvariantCulture));
                foreach(var pair in counts.OrderBy(v=>v.Key))report.AppendLine(pair.Key+" operations="+pair.Value+" cpuSeconds="+((double)cpu[pair.Key]/Stopwatch.Frequency).ToString("F6",System.Globalization.CultureInfo.InvariantCulture));
                File.WriteAllText(Path.Combine(folder,"phase-costs.txt"),report.ToString());File.WriteAllLines(Path.Combine(folder,"fixed-grants.csv"),trace);TestContext.WriteLine(report);
            }finally{input?.Dispose();work?.ReleaseOwner();query?.ReleaseOwner();}
            PrefixNoLeaks(budget);
        }
    }
}
