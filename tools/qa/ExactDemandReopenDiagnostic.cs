// QA-only: install with ExactDemandDiagnostic.cs and the temporary Simulation probe.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    internal sealed class ExactDemandReopenObserverQa : IDisposable
    {
        private sealed class Label {internal long Rev;internal double G,H,F;}
        private sealed class Entry {internal double G,H,F;}
        private sealed class Pop {internal long Rev;internal double G;internal int Count;}
        private readonly Dictionary<int,Label> labels=new Dictionary<int,Label>();
        private readonly Dictionary<int,Entry> entries=new Dictionary<int,Entry>();
        private readonly Dictionary<int,Pop> pops=new Dictionary<int,Pop>();
        private readonly StreamWriter writer;private readonly string path;private readonly long memoryBefore;
        private long ordinal,revision,rows,entryCalls,seeds,updates,accepted,repeated,graphExpanded,relaxExpanded,removeFalse,addFalse,queueMismatches;
        private readonly HashSet<string> queueKeys=new HashSet<string>();
        private static string QueueKey(int node,double f)=>node+":"+Bits(f);
        private readonly List<string> errors=new List<string>();
        internal ExactDemandReopenObserverQa(string path){this.path=path;memoryBefore=GC.GetTotalMemory(false);writer=new StreamWriter(path,false,new UTF8Encoding(false),65536);}
        private static string Bits(double x)=>BitConverter.DoubleToInt64Bits(x).ToString("x16");
        private void Error(string message){if(errors.Count<32)errors.Add(message);}
        private Label Current(int node,double g){if(!labels.TryGetValue(node,out var label)){Error("missing label node="+node);return new Label();}if(Bits(label.G)!=Bits(g))Error("label g binding node="+node);return label;}
        private Entry Cached(int node){if(entries.TryGetValue(node,out var entry))return entry;Error("missing Entry node="+node);return new Entry();}
        private void Row(object value){writer.WriteLine(JsonConvert.SerializeObject(value,Formatting.None));rows++;}
        internal void Accept(NavDemandReopenEventQa e)
        {
            ordinal++;
            if(e.Kind=="E"){
                entryCalls++;if(entries.TryGetValue(e.Node,out var old)&&Bits(old.H)!=Bits(e.H))Error("V3 h changed node="+e.Node);
                entries[e.Node]=new Entry{G=e.G,H=e.H,F=e.F};return;
            }
            if(e.Kind=="Q"||e.Kind=="D"){
                string key=QueueKey(e.Node,e.F);bool expected=queueKeys.Contains(key);if(expected!=e.Removed)queueMismatches++;
                if(e.Removed)queueKeys.Remove(key);
                Row(new{k=e.Kind,eventId=ordinal,node=e.Node,g=e.Kind=="D"?Bits(e.G):null,f=Bits(e.F),removed=e.Removed,expectedPresent=expected,currentRev=labels.TryGetValue(e.Node,out var current)?current.Rev:0});return;
            }
            if(e.Kind=="S"){
                seeds++;var at=Cached(e.Node);if(labels.ContainsKey(e.Node))Error("duplicate seed node="+e.Node);if(!e.Added)addFalse++;
                var label=new Label{Rev=++revision,G=e.G,H=at.H,F=e.F};labels[e.Node]=label;bool expectedAdd=!queueKeys.Contains(QueueKey(e.Node,e.F));if(expectedAdd!=e.Added)queueMismatches++;if(e.Added)queueKeys.Add(QueueKey(e.Node,e.F));
                Row(new{k="S",eventId=ordinal,rev=label.Rev,node=e.Node,parent=0,seed=Bits(e.G),g=Bits(e.G),h=Bits(at.H),f=Bits(e.F),added=e.Added});return;
            }
            if(e.Kind=="P"){
                accepted++;var label=Current(e.Node,e.G);var at=Cached(e.Node);if(Bits(at.G)!=Bits(e.G)||Bits(at.F)!=Bits(e.F))Error("accepted Pop Entry binding node="+e.Node);if(!e.Removed)removeFalse++;
                pops.TryGetValue(e.Node,out var previous);int count=(previous?.Count??0)+1;if(count>1)repeated++;
                Row(new{k="P",eventId=ordinal,node=e.Node,rev=label.Rev,g=Bits(e.G),h=Bits(at.H),queuedF=Bits(e.F),computedF=Bits(at.F),removed=e.Removed,popOrdinal=count,previousPopRev=previous?.Rev??0,previousPopG=previous==null?null:Bits(previous.G)});
                pops[e.Node]=new Pop{Rev=label.Rev,G=e.G,Count=count};return;
            }
            if(e.Kind=="G"||e.Kind=="R"){
                if(!pops.ContainsKey(e.Node))return;
                if(e.Kind=="G")graphExpanded++;else relaxExpanded++;
                var parent=Current(e.From,e.FromG);var old=Current(e.Node,e.OldG);var hu=Cached(e.From);var hv=Cached(e.Node);
                Row(new{k=e.Kind,eventId=ordinal,node=e.Node,from=e.From,parent=parent.Rev,oldRev=old.Rev,g=Bits(e.G),oldG=Bits(e.OldG),fromG=Bits(e.FromG),arc=Bits(e.Cost),hFrom=Bits(hu.H),hTo=Bits(hv.H),parentF=Bits(parent.F),oldF=Bits(old.F),had=e.Had,decision=e.Decision,previousPopRev=pops[e.Node].Rev});return;
            }
            if(e.Kind=="U"){
                updates++;var parent=Current(e.From,e.FromG);Label old=null;if(e.Had)old=Current(e.Node,e.OldG);else if(labels.ContainsKey(e.Node))Error("unexpected existing target "+e.Node);
                var at=Cached(e.Node);if(Bits(at.G)!=Bits(e.G))Error("update Entry binding node="+e.Node);if(e.Had&&!e.Removed)removeFalse++;if(!e.Added)addFalse++;
                var label=new Label{Rev=++revision,G=e.G,H=at.H,F=at.F};labels[e.Node]=label;bool expectedAdd=!queueKeys.Contains(QueueKey(e.Node,at.F));if(expectedAdd!=e.Added)queueMismatches++;if(e.Added)queueKeys.Add(QueueKey(e.Node,at.F));
                Row(new{k="U",eventId=ordinal,rev=label.Rev,node=e.Node,from=e.From,parent=parent.Rev,g=Bits(e.G),fromG=Bits(e.FromG),arc=Bits(e.Cost),oldRev=old?.Rev??0,oldG=e.Had?Bits(e.OldG):null,h=Bits(at.H),f=Bits(at.F),had=e.Had,removed=e.Removed,added=e.Added,alreadyExpanded=pops.ContainsKey(e.Node),previousPopRev=pops.TryGetValue(e.Node,out var pop)?pop.Rev:0});return;
            }
            Error("unknown event "+e.Kind);
        }
        internal object Summary(){writer.Flush();return new{schema="exact-demand-reopen-events-v1",path=Path.GetFileName(path),rows,eventCallbacks=ordinal,entryCalls,seeds,updates,acceptedPops=accepted,uniquePoppedNodes=pops.Count,repeatedPops=repeated,graphAttemptsToExpanded=graphExpanded,connectorAttemptsToExpanded=relaxExpanded,removeFalse,addFalse,queueKeyReturnMismatches=queueMismatches,remainingObservedQueueKeys=queueKeys.Count,labelRevisions=revision,labelNodes=labels.Count,entryNodes=entries.Count,bindingErrors=errors.ToArray(),diagnosticSidecar=new{outsideSolverBudget=true,notRuntimeMemoryMeasurement=true,managedMemoryBefore=memoryBefore,managedMemoryNow=GC.GetTotalMemory(false),eventFileBytes=new FileInfo(path).Length},notes="E cached only actual production Entry operands; S/U every label revision and parent revision. P only accepted current g/f. G is cost-computed Graph rejection decision; R is Connector strict-improvement decision. Attempts to unexpanded receivers omitted; S/U retained for every path. Q/D records every actual Pop/relax Remove result, including rejected stale Pops; external queue-key model only observes operations and never steers solver. RemoveFalse on a previously popped absent key is normal. No extra frontier operation/probe."};}
        public void Dispose()=>writer.Dispose();
    }
    public sealed partial class NavigationPhysicalBodyTests
    {
        [Test] public void ExactDemandReopenV3SameBody()
        {
            string path=Environment.GetEnvironmentVariable("SPACEWARS_EXACT_DEMAND_BODY"),output=Environment.GetEnvironmentVariable("SPACEWARS_EXACT_DEMAND_OUT"),expected=Environment.GetEnvironmentVariable("SPACEWARS_EXACT_DEMAND_SHA256"),events=Environment.GetEnvironmentVariable("SPACEWARS_EXACT_DEMAND_EVENTS");
            Assert.That(path,Is.Not.Null.And.Not.Empty);Assert.That(output,Is.Not.Null.And.Not.Empty);Assert.That(events,Is.Not.Null.And.Not.Empty);Assert.AreEqual("2919bcd4c50bc60d0ae3b9de17a0d3f92a8373b2ff8b1fff30d11fc2e386c7d0",expected);
            var info=new FileInfo(path);Assert.That(info.Length,Is.InRange(1,Spacewars.Runtime.WorldWire.MaxBytes));byte[] body=File.ReadAllBytes(path);Assert.AreEqual(expected,ExactDemandCompletionQa.Sha(body));
            object raw,observed;using(var observer=new ExactDemandReopenObserverQa(events)){
                Assert.IsNull(NavDemandReopenProbeQa.Sink);NavDemandReopenProbeQa.Sink=observer.Accept;
                try{raw=ExactDemandControlledQa.Run(body,false);}finally{NavDemandReopenProbeQa.Sink=null;}
                observed=observer.Summary();
            }
            var run=JObject.FromObject(raw);var observation=JObject.FromObject(observed);
            bool control=(string)run["outcome"]=="Terminal"&&(string)run["status"]=="Ready"&&(double?)run["cost"]==169.02135658930808&&(int)run["epochs"]==303&&(long)run["work"]["Graph"]==77113&&(long)run["work"]["Connectors"]==31553&&(long)run["work"]["Bookkeeping"]==14000;
            var result=new JObject{{"schema","exact-demand-reopen-v3-v1"},{"bodySha256",expected},{"run",run},{"observer",observation},{"logicalControlMatches",control},{"qualification",control&&observation["bindingErrors"].Count()==0?"Established":"NotEstablished"},{"boundary","ONE fresh V3 bb7 Entity10/geometry8 query, QA observer outside solver budget, not Army600/performance acceptance"}};
            File.WriteAllText(output,result.ToString(Formatting.Indented));
            Assert.AreEqual(0,(long)run["remainingPhysical"]);Assert.AreEqual(0,(long)run["remainingHeld"]);Assert.AreEqual(0,(long)run["remainingScopes"]);Assert.False((bool)run["captureActive"]);Assert.True(control,"instrumented logical control mismatch: NotEstablished");Assert.IsEmpty(observation["bindingErrors"]);
            TestContext.WriteLine("REOPEN_CONTROL output="+output+" events="+events+" accepted="+observation["acceptedPops"]+" repeated="+observation["repeatedPops"]);
        }
    }
}
