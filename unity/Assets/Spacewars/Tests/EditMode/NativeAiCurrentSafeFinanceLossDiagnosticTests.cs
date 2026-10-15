using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiCurrentSafeFinanceLossDiagnosticTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private const string Archive="docs/native-ai/q4/original-natural-20261013-source-compiled-raw.zip";
        private const string ArchiveHash="bed495a3a55ae9d2b0507cc6f9d91c23f37bfee6e79dd9e099c51263eb81e154";
        private const string Entry=".local/o6-natural/7109-checkpoint-300.world";
        private const string InputHash="66ee0f7388da10721416a634a397c34f034baf2a0dce2e70c12723e7da87d264";
        private const string Owner="ai-0";private const int StartTick=300,EndTick=600,Site=9;
        private static string Root(){var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);return d.FullName;}
        private static object Get(object x,string name){var f=x.GetType().GetField(name,F);Assert.NotNull(f,name);return f.GetValue(x);}
        private static void Advance(PlayableAuthorityTick a,UnityHostRouteService host){for(int i=0;i<512;i++){if(a.TryAdvance())return;host.Service(a,64);}Assert.Fail("ordinary typed-route barrier timed out at "+a.Tick);}
        private static void AssertBytesEqual(byte[] e,byte[] a,string m){if(e.Length!=a.Length)Assert.Fail(m+" length "+e.Length+" != "+a.Length);for(int i=0;i<e.Length;i++)if(e[i]!=a[i])Assert.Fail(m+" first difference "+i);}
        private static string AuthorityFacts(object domain,FieldInfo projection,FieldInfo requests,FieldInfo ownerFilter,FieldInfo capture)
        {
            var p=projection.GetValue(domain);var r=requests.GetValue(domain);var o=ownerFilter.GetValue(domain);var c=capture.GetValue(domain);
            try{projection.SetValue(domain,null);requests.SetValue(domain,null);ownerFilter.SetValue(domain,null);capture.SetValue(domain,false);return PlayableWorldRestoreTests.Facts(domain,true);}
            finally{projection.SetValue(domain,p);requests.SetValue(domain,r);ownerFilter.SetValue(domain,o);capture.SetValue(domain,c);}
        }
        private static byte[] ReadEntry(string path,string entry){using(var z=ZipFile.OpenRead(path)){var e=z.GetEntry(entry);Assert.NotNull(e,entry);using(var s=e.Open())using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}}}
        private sealed class Tick{internal int Number,Finance;internal byte[] World;internal string Facts,Views;}
        private sealed class Run{internal Tick[] Ticks;internal List<string> Trace=new List<string>();internal List<string> Timeline=new List<string>();internal object FirstLoss,ObserverBindingProof;}

        [Test]
        public void FirstNetPersistedSite9FinanceDecrease_300To600_HooksByteAndFactNeutral()
        {
            var root=Root();var archive=Path.Combine(root,Archive);Assert.AreEqual(ArchiveHash,Wire.Hash(File.ReadAllBytes(archive)),"immutable original packet");
            var input=ReadEntry(archive,Entry);Assert.AreEqual(InputHash,Wire.Hash(input),"exact checkpoint member");
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var participants=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,7109,participants);Assert.AreEqual(8,config.Roster.Count);Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
            Run Execute(bool enabled,Run expected=null)
            {
                var a=PlayableAuthorityTick.RestoreBytes(input,config);var run=new Run{Ticks=new Tick[EndTick-StartTick]};object domain=Get(a,"domain");var dt=domain.GetType();var routeProjection=dt.GetField("RouteProjectionReadbackForTests",F);var routeRequests=dt.GetField("RouteRequestsReadbackForTests",F);var routeOwner=dt.GetField("RouteProjectionOwnerFilterForTests",F);var routeCapture=dt.GetField("CaptureCurrentExpansionRoutesForTests",F);
                try
                {
                    Assert.AreEqual(StartTick,a.Tick);AssertBytesEqual(input,a.CaptureBytes(),"immediate restore");var initial=a.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==Owner);Assert.AreEqual(7109,initial.Seed);Assert.AreEqual(71,initial.Generation);Assert.AreEqual("Safe",initial.Opening.Opening.ToString());var initialFinance=a.CaptureDiagnosticBudgets().Single(x=>x.OwnerId==Owner).Reserved.Concat(a.CaptureDiagnosticBudgets().Single(x=>x.OwnerId==Owner).Unpaid).Where(e=>e.Purpose=="expansion-financial"&&e.Action.SiteId==Site).Sum(e=>e.Amount);
                    void Trace(object row){run.Trace.Add(JsonConvert.SerializeObject(row));}
                    AiExpansionFinancial.ActualLifecycleReadbackOwnerFilterForTests=enabled?Owner:null;
                    AiExpansionFinancial.ActualLifecycleReadbackForTests=enabled?(Action<string,long,string,int,AiBudgetEntry,AiBudgetEntry,AiBudgetState>)((who,tick,stage,site,before,after,budget)=>{if(site==Site||before?.Action.SiteId==Site||after?.Action.SiteId==Site)Trace(new{Owner=who,Tick=tick,Stage="Lifecycle."+stage,Site=site,Before=before,After=after,Budget=budget});}):null;
                    AiExpansionFinancial.ActualDecisionReadbackOwnerFilterForTests=enabled?Owner:null;AiExpansionFinancial.ActualDecisionReadbackForTests=enabled?(Action<object>)Trace:null;
                    AiExpansionFinancial.ActualWitnessStageOwnerFilterForTests=enabled?Owner:null;AiExpansionFinancial.ActualWitnessStageReadbackForTests=enabled?(Action<object>)Trace:null;
                    Assert.NotNull(routeProjection);Assert.NotNull(routeRequests);routeOwner.SetValue(domain,enabled?Owner:null);routeCapture.SetValue(domain,false);
                    routeProjection.SetValue(domain,enabled?(object)new Action<PlayableRouteProjectionReadback>(r=>Trace(new{Stage="RouteProjection",Readback=r})):null);
                    routeRequests.SetValue(domain,enabled?(object)new Action<string,long,PlayableRouteRequest[]>((who,tick,requests)=>{if(who==Owner)Trace(new{Stage="RouteRequests",Owner=who,Tick=tick,Requests=requests});}):null);
                    PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=enabled?Owner:null;
                    PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=enabled?new Action<string>(s=>Trace(new{Stage="CurrentSafeFunding",Raw=s})):null;
                    using(var host=new UnityHostRouteService())for(int i=0;i<EndTick-StartTick;i++)
                    {
                        var traceOffset=run.Trace.Count;Advance(a,host);Assert.AreEqual(StartTick+i+1,a.Tick);var world=a.CaptureBytes();
                        var authorityFacts=AuthorityFacts(domain,routeProjection,routeRequests,routeOwner,routeCapture);
                        if(enabled&&i==0)
                        {
                            var clone=PlayableAuthorityTick.RestoreBytes(world,config);try
                            {
                                var cloneDomain=Get(clone,"domain");var hookedFacts=PlayableWorldRestoreTests.Facts(domain,true);var cloneFacts=PlayableWorldRestoreTests.Facts(cloneDomain,true);
                                var authorityDiff=typeof(OfflineParticipantRestoreTests).GetMethod("AuthorityDiff",BindingFlags.Static|BindingFlags.NonPublic);
                                Assert.NotNull(authorityDiff,"existing full-field authority diff helper");var firstDifference=authorityDiff.Invoke(null,new object[]{domain,cloneDomain,"domain",0});
                                Assert.AreEqual(authorityFacts,cloneFacts,"detaching only test observer bindings yields exact restored authority facts");
                                run.ObserverBindingProof=new{Tick=a.Tick,Compared="live hooked domain vs exact byte-restored twin",FirstDifferingField=firstDifference,
                                    DetachedFactsEqualToTwin=true,Bindings=new object[]{new{Field="RouteProjectionReadbackForTests",Before=routeProjection.GetValue(domain)?.GetType().FullName,DuringNeutralization="null"},
                                    new{Field="RouteRequestsReadbackForTests",Before=routeRequests.GetValue(domain)?.GetType().FullName,DuringNeutralization="null"},
                                    new{Field="RouteProjectionOwnerFilterForTests",Before=routeOwner.GetValue(domain),DuringNeutralization="null"},
                                    new{Field="CaptureCurrentExpansionRoutesForTests",Before=routeCapture.GetValue(domain),DuringNeutralization=false}}};
                                Assert.AreNotEqual(hookedFacts,authorityFacts,"observer bindings, and no gameplay authority fields, explain the live-vs-twin hash delta");
                            }finally{clone.Stop();}
                        }
                        var checkpoints=a.CaptureDiagnosticCheckpoints();var budgets=a.CaptureDiagnosticBudgets();var views=Enumerable.Range(0,8).Select(n=>a.ParticipantView("ai-"+n)).ToArray();
                        var facts=JsonConvert.SerializeObject(new{AuthorityFacts=authorityFacts,Checkpoints=checkpoints,Budgets=budgets});var allViews=JsonConvert.SerializeObject(views);
                        var ownerBudget=budgets.Single(x=>x.OwnerId==Owner);var site9=ownerBudget.Reserved.Concat(ownerBudget.Unpaid).Where(e=>e.Purpose=="expansion-financial"&&e.Action.SiteId==Site).Sum(e=>e.Amount);
                        var postRead=a.CaptureBytes();var postFacts=AuthorityFacts(domain,routeProjection,routeRequests,routeOwner,routeCapture);
                        var postCheckpoints=JsonConvert.SerializeObject(a.CaptureDiagnosticCheckpoints());var postBudgets=JsonConvert.SerializeObject(a.CaptureDiagnosticBudgets());var postViews=JsonConvert.SerializeObject(Enumerable.Range(0,8).Select(n=>a.ParticipantView("ai-"+n)).ToArray());
                        AssertBytesEqual(world,postRead,"complete diagnostic reads preserve bytes at "+a.Tick);Assert.AreEqual(authorityFacts,postFacts,"independent full-authority facts before/after diagnostic reads at "+a.Tick);
                        Assert.AreEqual(JsonConvert.SerializeObject(checkpoints),postCheckpoints,"all-owner checkpoints and full receipts before/after reads at "+a.Tick);Assert.AreEqual(JsonConvert.SerializeObject(budgets),postBudgets,"all-owner ledgers before/after reads at "+a.Tick);Assert.AreEqual(allViews,postViews,"all eight complete views before/after reads at "+a.Tick);
                        var current=new Tick{Number=(int)a.Tick,Finance=site9,World=world,Facts=facts,Views=allViews};
                        if(expected!=null){var prior=expected.Ticks[i];AssertBytesEqual(prior.World,world,"hook parity full authority at "+a.Tick);Assert.AreEqual(prior.Facts,facts,"all-owner checkpoints/budgets/receipts at "+a.Tick);Assert.AreEqual(prior.Views,allViews,"all participant views at "+a.Tick);}
                        else run.Ticks[i]=current;
                        var before=i==0?initialFinance:(expected==null?run.Ticks[i-1].Finance:expected.Ticks[i-1].Finance);
                        if(run.FirstLoss==null&&before>0&&site9<before)run.FirstLoss=new{Tick=a.Tick,PreviousPositiveAmount=before,CurrentPersistedAmount=site9,Delta=site9-before,Checkpoint=checkpoints.Single(x=>x.OwnerId==Owner),Budget=ownerBudget,View=views[0]};
                        if(enabled)run.Timeline.Add(JsonConvert.SerializeObject(new{Tick=a.Tick,FinanceBeforeTick=before,FinanceAfterCompleteTick=site9,AuthorityFacts=authorityFacts,
                            OwnerCheckpoint=checkpoints.Single(x=>x.OwnerId==Owner),AllOwnerReceipts=checkpoints.Select(c=>new{c.OwnerId,Records=c.Records.ToArray()}).ToArray(),Budget=ownerBudget,
                            ViewFacts=views.Select((v,n)=>new{Owner="ai-"+n,Facts=PlayableWorldRestoreTests.Facts(v)}).ToArray(),TargetView=views[0],
                            Site9=views[0].Sites.FirstOrDefault(x=>x.Site.Id==Site),Actors=views[0].Entities.Where(x=>x.Owner==views[0].Owner&&x.Kind==PlayableEntityKind.Explorer).ToArray(),ActualEvents=run.Trace.Skip(traceOffset).ToArray()},Formatting.None));
                    }
                    var reread=a.CaptureBytes();var finalWorld=expected==null?run.Ticks[run.Ticks.Length-1].World:expected.Ticks[expected.Ticks.Length-1].World;AssertBytesEqual(finalWorld,reread,"end read preserves final bytes");
                    return run;
                }
                finally
                {
                    AiExpansionFinancial.ActualLifecycleReadbackForTests=null;AiExpansionFinancial.ActualLifecycleReadbackOwnerFilterForTests=null;AiExpansionFinancial.ActualDecisionReadbackForTests=null;AiExpansionFinancial.ActualDecisionReadbackOwnerFilterForTests=null;AiExpansionFinancial.ActualWitnessStageReadbackForTests=null;AiExpansionFinancial.ActualWitnessStageOwnerFilterForTests=null;
                    PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=null;PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=null;
                    routeProjection.SetValue(domain,null);routeRequests.SetValue(domain,null);routeOwner.SetValue(domain,null);routeCapture.SetValue(domain,false);a.Stop();
                }
            }
            var off=Execute(false);var on=Execute(true,off);Assert.AreEqual(EndTick-StartTick,off.Ticks.Length);Assert.IsEmpty(off.Trace);Assert.IsNotEmpty(on.Trace,"in-place financial/witness/route diagnostics observed");
            Assert.AreEqual(JsonConvert.SerializeObject(off.FirstLoss),JsonConvert.SerializeObject(on.FirstLoss),"first stable persisted finance decrease agrees hook-off/on");
            var evidence=Path.Combine(root,".local/qa/q4-safe-finance-loss-20261013");Directory.CreateDirectory(evidence);File.WriteAllLines(Path.Combine(evidence,"timeline-on.jsonl"),on.Timeline);File.WriteAllLines(Path.Combine(evidence,"trace-on.jsonl"),on.Trace);
            var first=on.FirstLoss;var outcome=first??(object)new{Result="NotQualifiedCause",Reason="No positive net persisted site-9 finance decrease observed at a complete tick boundary from 301 through 600"};
            TestContext.WriteLine("Q4_SAFE_FINANCE_LOSS "+JsonConvert.SerializeObject(new{Seed=7109,Owner,StartTick,EndTick,Site,ArchiveHash,Entry,InputHash,FirstNetPersistedDecrease=outcome,HookOffOnByteAndIndependentFullFactEqual=true,Timeline=Path.Combine(evidence,"timeline-on.jsonl"),Trace=Path.Combine(evidence,"trace-on.jsonl"),TraceRows=on.Trace.Count,ObserverBindingProof=on.ObserverBindingProof}));
        }
    }
}
