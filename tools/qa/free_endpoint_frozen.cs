using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Spacewars.Runtime;
using Spacewars.Simulation;

// Diagnostic compiled-copy selector for certified endpoint reuse only. Each pass owns fresh observer caches; JIT warmup passes are excluded.
internal static class FreeEndpointFrozen
{
    public static bool Optimized;
    private sealed class Row
    {
        public bool Optimized;public bool Warmup;public double WallSeconds;public RouteCpu.Interval Cpu;public int Requests,Proofs,Routers;public long ExpandedCells,Rebuilds,PayloadBytes;
    }
    public static int Run(string output,int seed,bool calibrate)
    {
        Directory.CreateDirectory(output);
        var profile=PlayableProfile.Create(JsonConvert.DeserializeObject<PlayableProfileData>(File.ReadAllText("unity/Assets/Spacewars/Content/Resources/PlayableProfile.json")),new CrimsonGroveMap(JsonConvert.DeserializeObject<CrimsonGroveProfileData>(File.ReadAllText("unity/Assets/Spacewars/Content/Resources/CrimsonGroveProfile.json"))));
        var setup=new NativeLobbyConfiguration{CrimsonGrove=true,HumanPresent=false,HasExplicitSeed=true,ExplicitSeed=seed};setup.InitializeParticipants();
        var config=NativeLobbyMatch.Create(profile,setup);var rows=new List<Row>();string[] reference=null;long? referenceExpanded=null,referenceRebuilds=null;
        var sequence=calibrate?new[]{false,true,false,true}:new[]{false,true,false,true,true,false,false,true,true,false};int pass=0;
        foreach(bool memoized in sequence){
            Optimized=memoized;GC.Collect();GC.WaitForPendingFinalizers();
            var authority=new PlayableAuthorityTick(config,1);var outputs=new List<PlayableSnapshot>();var row=new Row{Optimized=memoized,Warmup=pass++<2};
            try{
                var inputs=config.Roster.Select(p=>PlayableAiObservation.From(authority.ParticipantView(p.Id))).Select(o=>new{Observation=o,Requests=AiScoutPlanner.RouteRequests(o.Entities,o.PublicScoutObjectives,o.Owner,o.Generation,o.Tick,profile)}).ToArray();
                if(inputs.Length!=8)throw new Exception("Expected eight unchanged owner observations");
                var cpu0=RouteCpu.Read();var cpuSamples=new List<RouteCpu.Stamp>{cpu0};var clock=Stopwatch.StartNew();
                foreach(var input in inputs){var o=input.Observation;row.Requests+=input.Requests.Length;outputs.Add(authority.Domain.PlayerSnapshotWithRoutes(17,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,seed,o.Owner,input.Requests));cpuSamples.Add(RouteCpu.Read());}
                clock.Stop();row.WallSeconds=clock.Elapsed.TotalSeconds;row.Cpu=RouteCpu.Delta(cpu0,RouteCpu.Read());File.WriteAllText(Path.Combine(output,"cpu-"+rows.Count+".json"),JsonConvert.SerializeObject(cpuSamples,Formatting.Indented));
                var facts=outputs.Select(o=>JsonConvert.SerializeObject(o.RouteProofs)).ToArray();
                File.WriteAllText(Path.Combine(output,"proofs-"+rows.Count+".json"),JsonConvert.SerializeObject(facts,Formatting.Indented));row.Proofs=outputs.Sum(o=>o.RouteProofs.Count);
                if(row.Requests==0||row.Proofs==0)throw new Exception("Vacuous proof workload");
                if(reference==null)reference=facts;else if(!reference.SequenceEqual(facts))throw new Exception("Proof bindings/path results changed on identical observations");
                var table=authority.Domain.GetType().GetField("observedRouteCaches",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var lookup=new object[]{authority.Domain,null};
                if(!(bool)table.GetType().GetMethod("TryGetValue").Invoke(table,lookup))throw new Exception("Missing observer routes");
                foreach(IList pool in ((IDictionary)lookup[1]).Values)foreach(var entry in pool){
                    var router=(SharedFlowRouter)entry.GetType().GetField("Router",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(entry);
                    if(router==null)continue;row.Routers++;row.ExpandedCells+=router.ExpandedCells;row.Rebuilds+=router.RebuildCount;row.PayloadBytes+=router.PreparedGeometryBytes;
                    if(router.PreparedGeometryBytes>SharedFlowRouter.ObserverPreparationPayloadLimit)throw new Exception("Observer predicate payload bound exceeded");
                }
                if(referenceExpanded==null){referenceExpanded=row.ExpandedCells;referenceRebuilds=row.Rebuilds;}
                else if(referenceExpanded!=row.ExpandedCells||referenceRebuilds!=row.Rebuilds)throw new Exception("Logical BFS work changed");
                rows.Add(row);Console.WriteLine("FROZEN_PASS "+JsonConvert.SerializeObject(row));
            }finally{authority.Stop();}
        }
        File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new{seed,owners=8,logicalTicksAdvanced=0,proofsIdentical=true,warmupExcluded=true,calibrate,rows},Formatting.Indented));
        return 0;
    }
}
