using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
internal static class FieldKeyControl
{
    sealed class Row {public bool Optimized,Warmup;public string Kind;public double WallSeconds;public RouteCpu.Interval Cpu;public long AllocatedBytes;public Dictionary<string,long> Counters;}
    public static int Main(string[] args){string output=args[0];int seed=int.Parse(args[1]),count=20000;Directory.CreateDirectory(output);
        var factories=new[]{Assembly.LoadFrom(args[2]),Assembly.LoadFrom(args[3])}.Select(a=>a.GetType("FieldKeyPacketFactory").GetMethod("Create")).ToArray();var rows=new List<Row>();
        foreach(string kind in new[]{"cold","warm"}){string[] reference=null;string referenceCounters=null;int pass=0;
            foreach(bool optimized in new[]{false,true,false,true,true,false,false,true,true,false}){
                GC.Collect();GC.WaitForPendingFinalizers();using(var packet=(IRouteBenchmarkPacket)factories[optimized?1:0].Invoke(null,new object[]{kind,seed,count})){
                    var stamps=new List<RouteCpu.Stamp>();Action checkpoint=()=>stamps.Add(RouteCpu.Read());var start=RouteCpu.Read();long bytes=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.StartNew();packet.Run(checkpoint);clock.Stop();bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;var end=RouteCpu.Read();
                    var row=new Row{Optimized=optimized,Warmup=pass++<2,Kind=kind,WallSeconds=clock.Elapsed.TotalSeconds,Cpu=RouteCpu.Delta(start,end),AllocatedBytes=bytes,Counters=packet.Counters()};
                    var facts=packet.Facts();string prefix=kind+"-"+(pass-1);File.WriteAllText(Path.Combine(output,prefix+"-facts.json"),JsonConvert.SerializeObject(facts,Formatting.Indented));File.WriteAllText(Path.Combine(output,prefix+"-cpu.json"),JsonConvert.SerializeObject(new{start,stamps,end},Formatting.Indented));
                    if(reference==null)reference=facts;else if(!reference.SequenceEqual(facts))throw new Exception("Exact paths/proofs differ: "+prefix);
                    string counters=JsonConvert.SerializeObject(row.Counters.OrderBy(x=>x.Key));if(referenceCounters==null)referenceCounters=counters;else if(referenceCounters!=counters)throw new Exception("Logical work differs: "+prefix);
                    if(kind=="cold"&&(row.Counters["Requests"]!=248||row.Counters["Proofs"]!=248||row.Counters["Routers"]!=8))throw new Exception("Cold workload binding changed");
                    if(kind=="warm"&&(row.Counters["ExpandedDelta"]!=0||row.Counters["RebuildDelta"]!=0||row.Counters["PathHitDelta"]!=0||row.Counters["FieldHitDelta"]!=count))throw new Exception("Warm workload bypassed field lookup");
                    rows.Add(row);Console.WriteLine("CONTROL_PASS "+JsonConvert.SerializeObject(row));
                }
            }
        }
        File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new{seed,rows,exactFacts=true,logicalWorkIdentical=true,warmupExcluded=true,warmCase="Separate synthetic8router blocked-route field reuse; not gameplay throughput",warmRequestsPerPass=count},Formatting.Indented));return 0;
    }
}
