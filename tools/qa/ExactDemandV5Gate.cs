// QA-only temporary install into Tests/EditMode.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        [Test] public void ExactDemandV5SameBodyGate()
        {
            string input=Environment.GetEnvironmentVariable("SPACEWARS_EXACT_DEMAND_BODY"),output=Environment.GetEnvironmentVariable("SPACEWARS_EXACT_DEMAND_OUT");
            byte[] body=File.ReadAllBytes(input);Assert.AreEqual("2919bcd4c50bc60d0ae3b9de17a0d3f92a8373b2ff8b1fff30d11fc2e386c7d0",ExactDemandCompletionQa.Sha(body));
            var popped=new HashSet<int>();int pops=0,repeated=0,inserts=0,equalInserts=0,nonImproving=0,nonMonotone=0;bool first=true,ready=false;NavExactCost last=default,best=default,prefix=default;double fold=0;
            NavExactDemandProbeQa.Pop=(node,cost)=>{pops++;if(!popped.Add(node))repeated++;if(!first&&cost<last)nonMonotone++;first=false;last=cost;};
            NavExactDemandProbeQa.Insert=(node,cost,had,old)=>{inserts++;if(had&&cost==old)equalInserts++;if(had&&cost>=old)nonImproving++;};
            NavExactDemandProbeQa.Ready=(a,b,c)=>{ready=true;best=a;prefix=b;fold=c;};
            JObject run;try{run=JObject.FromObject(ExactDemandControlledQa.Run(body,false,true));}finally{NavExactDemandProbeQa.Pop=null;NavExactDemandProbeQa.Insert=null;NavExactDemandProbeQa.Ready=null;}
            string Hex(NavExactCost cost)=>string.Concat(Enumerable.Range(0,34).Reverse().Select(n=>cost.Limb(n).ToString("x16")));
            long graph=(long?)run["work"]?["Graph"]??-1,connectors=(long?)run["work"]?["Connectors"]??-1,bk=(long?)run["work"]?["Bookkeeping"]??-1,field=(long?)run["work"]?["Field"]??-1,maintenance=(long?)run["work"]?["Maintenance"]??-1;
            long search=graph-25921,total=graph+connectors+bk+field+maintenance;
            var result=new{schema="exact-demand-v5-gate-v1",run,observer=new{pops,unique=popped.Count,repeated,inserts,equalInserts,nonImproving,nonMonotone,ready,exactObjectiveHex=Hex(best),exactPrefixHex=Hex(prefix),legacyPublicFlattenedFold=fold,note="Temporary observer HashSet/delegates are QA memory outside solver ledger; no runtime-memory/wall-performance qualification."},gate=new{searchGraph=search,baselineSearchGraph=51192,totalPaid=total,baselineTotalPaid=122666,searchImproves=search>=0&&search<51192,totalImproves=total>=0&&total<122666}};
            File.WriteAllText(output,JsonConvert.SerializeObject(result,Formatting.Indented));
            Assert.AreEqual("Terminal",(string)run["outcome"],(string)run["reason"]);Assert.AreEqual("Ready",(string)run["status"]);Assert.True(ready);Assert.AreEqual(best,prefix);Assert.AreEqual(fold,(double)run["cost"]);Assert.Zero(repeated);Assert.Zero(equalInserts);Assert.Zero(nonImproving);Assert.Zero(nonMonotone);Assert.AreEqual(4L*pops,search);Assert.Less(search,51192);Assert.Less(total,122666);Assert.Zero((long)run["remainingPhysical"]);Assert.Zero((long)run["remainingHeld"]);Assert.Zero((int)run["remainingScopes"]);Assert.False((bool)run["captureActive"]);
        }
    }
}
