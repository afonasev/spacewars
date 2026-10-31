using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Spacewars.Runtime;
using Spacewars.Simulation;
// Compiled beside each arm's exact Runtime/Simulation sources, not into production.
public static class FieldKeyPacketFactory
{
    sealed class Packet : IRouteBenchmarkPacket {
        readonly Action<Action> run;readonly Func<string[]> facts;readonly Func<Dictionary<string,long>> counters;readonly Action stop;
        internal Packet(Action<Action> r,Func<string[]> f,Func<Dictionary<string,long>> c,Action s){run=r;facts=f;counters=c;stop=s;}
        public void Run(Action checkpoint)=>run(checkpoint);public string[] Facts()=>facts();public Dictionary<string,long> Counters()=>counters();public void Dispose()=>stop();
    }
    public static IRouteBenchmarkPacket Create(string kind,int seed,int count){
        var profile=PlayableProfile.Create(JsonConvert.DeserializeObject<PlayableProfileData>(File.ReadAllText("unity/Assets/Spacewars/Content/Resources/PlayableProfile.json")),new CrimsonGroveMap(JsonConvert.DeserializeObject<CrimsonGroveProfileData>(File.ReadAllText("unity/Assets/Spacewars/Content/Resources/CrimsonGroveProfile.json"))));
        if(kind=="warm")return Warm(profile,count);
        var setup=new NativeLobbyConfiguration{CrimsonGrove=true,HumanPresent=false,HasExplicitSeed=true,ExplicitSeed=seed};setup.InitializeParticipants();var config=NativeLobbyMatch.Create(profile,setup);var authority=new PlayableAuthorityTick(config,1);
        var inputs=config.Roster.Select(p=>PlayableAiObservation.From(authority.ParticipantView(p.Id))).Select(o=>new{Observation=o,Requests=AiScoutPlanner.RouteRequests(o.Entities,o.PublicScoutObjectives,o.Owner,o.Generation,o.Tick,profile)}).ToArray();
        if(inputs.Length!=8)throw new Exception("Expected8owners");var outputs=new List<PlayableSnapshot>();
        return new Packet(cp=>{foreach(var input in inputs){var o=input.Observation;outputs.Add(authority.Domain.PlayerSnapshotWithRoutes(17,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,seed,o.Owner,input.Requests));cp();}},
            ()=>outputs.Select(o=>JsonConvert.SerializeObject(o.RouteProofs)).ToArray(),()=>{
                var m=new Dictionary<string,long>{{"Requests",inputs.Sum(x=>x.Requests.Length)},{"Proofs",outputs.Sum(x=>x.RouteProofs.Count)},{"Routers",0},{"ExpandedCells",0},{"Rebuilds",0},{"CachedFields",0},{"PredicateBytes",0}};
                var table=authority.Domain.GetType().GetField("observedRouteCaches",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var lookup=new object[]{authority.Domain,null};if(!(bool)table.GetType().GetMethod("TryGetValue").Invoke(table,lookup))throw new Exception("Missing observer cache");
                foreach(IList pool in ((IDictionary)lookup[1]).Values)foreach(var e in pool){var router=(SharedFlowRouter)e.GetType().GetField("Router",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(e);if(router==null)continue;m["Routers"]++;m["ExpandedCells"]+=router.ExpandedCells;m["Rebuilds"]+=router.RebuildCount;m["CachedFields"]+=router.CachedFieldCount;m["PredicateBytes"]+=router.PreparedGeometryBytes;if(router.PreparedGeometryBytes>SharedFlowRouter.ObserverPreparationPayloadLimit)throw new Exception("Predicate bound");}return m;
            },()=>authority.Stop());
    }
    static IRouteBenchmarkPacket Warm(PlayableProfile profile,int count){
        var g=new NavGeometry(181,new[]{new NavObstacle(-.8,-7,.8,7)},1);var routers=Enumerable.Range(0,8).Select(_=>new SharedFlowRouter(g,profile.Navigation,null,true)).ToArray();var goal=new NavPoint(5.125,.375);var outputs=Enumerable.Range(0,8).Select(_=>new List<NavPoint[]>()).ToArray();
        foreach(var router in routers)if(router.FindPath(new NavPoint(-5.125+1e-5,.375),goal).Length<2)throw new Exception("Warm fixture bypasses blocked route");
        long expansions=routers.Sum(x=>x.ExpandedCells),rebuilds=routers.Sum(x=>(long)x.RebuildCount),hits=routers.Sum(x=>x.PathCacheHits),fieldHits=routers.Sum(x=>(long)x.FieldCacheHits);
        return new Packet(cp=>{for(int i=0;i<count;i++){var path=routers[i%8].FindPath(new NavPoint(-5.125-i*1e-8,.375),goal);if(path.Length<2)throw new Exception("Vacuous warm route");outputs[i%8].Add(path);if(i%2500==0)cp();}},
            ()=>outputs.Select(x=>JsonConvert.SerializeObject(x)).ToArray(),()=>new Dictionary<string,long>{{"Requests",count},{"Paths",outputs.Sum(x=>x.Count)},{"Routers",8},{"ExpandedDelta",routers.Sum(x=>x.ExpandedCells)-expansions},{"RebuildDelta",routers.Sum(x=>(long)x.RebuildCount)-rebuilds},{"PathHitDelta",routers.Sum(x=>x.PathCacheHits)-hits},{"FieldHitDelta",routers.Sum(x=>(long)x.FieldCacheHits)-fieldHits}},()=>{});
    }
}
