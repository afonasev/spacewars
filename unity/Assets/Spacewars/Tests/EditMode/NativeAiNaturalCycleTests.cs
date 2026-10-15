using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    // Trusted evaluator: no world/selection injection. C9 CaptureBytes uses ordinary receipt reconciliation.
    public sealed class NativeAiNaturalCycleTests
    {
        private static readonly int[] Seeds={7108,7109};
        private const int Horizon=18000;
        private static string Json(object value)=>JsonConvert.SerializeObject(value,Formatting.None);
        internal static string Root()
        {var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);return d.FullName;}
        internal static bool HasReadyExpansion(PlayableSnapshot v)=>v.Buildings.Any(b=>b.Owner==v.Owner&&b.SiteId!=v.HomeSiteId&&b.Phase==ConstructionPhase.Ready&&(b.Kind==PlayableBuildingKind.Mine||b.Kind==PlayableBuildingKind.Outpost||b.Kind==PlayableBuildingKind.Headquarters));
        internal sealed class Result
        {
            public string Owner,Opening,OpeningPhase,Reason;public long Tick,Paid;public bool Mid,Expansion,Research,SurvivalDeferral;
            public int PeakLiving,PeakPaidQueues;public string[] Milestones;
        }
        internal sealed class SurvivalEvidence
        {
            private readonly Dictionary<string,long> uncovered=new Dictionary<string,long>();
            internal readonly HashSet<string> DeferredOwners=new HashSet<string>();
            internal void Observe(string line)
            {
                var row=JObject.Parse(line);string owner=(string)row["Owner"];long tick=(long)row["Tick"];var data=row["Data"];
                if((string)row["Stage"]=="TechnologyEmergency"&& (bool)data["Emergency"]&&
                    (string)data["Coverage"]=="uncovered"&&data["ThreatIds"].Any())uncovered[owner]=tick;
                if((string)row["Stage"]=="CanFundTechnology"&&(bool)data["Emergency"]&&!(bool)data["Allowed"]&&
                    !(bool)data["UrgentRepair"]&&!(bool)data["EssentialScale"]&&
                    uncovered.TryGetValue(owner,out var observed)&&observed==tick)DeferredOwners.Add(owner);
            }
        }
        internal static bool IsSurvivalStage(string stage)=>stage=="TechnologyEmergency"||stage=="CanFundTechnology";
        internal static void CaptureSurvivalOnly(IEnumerable<string> safeOwners,Action<string> observe)
        {
            var owners=new HashSet<string>(safeOwners);
            PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=null;
            PlayableAiOwnerLoop.CurrentSafeFundingStageFilterForTests=(owner,stage)=>owners.Contains(owner)&&IsSurvivalStage(stage);
            PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=owners.Count==0?null:observe;
        }
        internal static bool Qualified(Result r)=>r.Mid&&r.Paid>0&&r.PeakLiving>1&&r.PeakPaidQueues>0&&r.Expansion&&
            (r.Research||r.Opening=="Safe"&&r.SurvivalDeferral);
        internal static void AssertQualified(IEnumerable<Result> results)
        {
            CollectionAssert.AreEquivalent(AiOpeningCatalog.All.Select(x=>x.Opening.ToString()),results.Select(x=>x.Opening).Distinct(),"fixed natural seed bank coverage");
            foreach(var opening in results.GroupBy(x=>x.Opening))
                Assert.True(opening.Any(Qualified),opening.Key+" lacks a paid natural early-middle/expansion/research or evidenced Safe survival cycle: "+Json(opening));
        }
        [TestCase("Safe",true,false,true)]
        [TestCase("Safe",false,false,false)]
        [TestCase("GreedyMine",true,false,false)]
        [TestCase("Safe",false,true,true)]
        public void ResearchExceptionRequiresSafeAndActualSurvivalDeferral(string opening,bool deferred,bool research,bool expected)
        {
            var r=new Result{Opening=opening,Mid=true,Paid=1,PeakLiving=2,PeakPaidQueues=1,Expansion=true,Research=research,SurvivalDeferral=deferred};
            Assert.AreEqual(expected,Qualified(r));
            r.Expansion=false;Assert.False(Qualified(r),"survival exception never bypasses actual expansion");
        }
        [Test] public void SurvivalEvidenceRequiresSameOwnerTickUncoveredThreatAndActualFundingDenial()
        {
            string Threat(string owner,long tick,bool emergency,string coverage,int[] ids)=>Json(new{Owner=owner,Tick=tick,Stage="TechnologyEmergency",Data=new{Emergency=emergency,Coverage=coverage,ThreatIds=ids}});
            string Funding(string owner,long tick,bool emergency,bool allowed,bool repair=false,bool scale=false)=>Json(new{Owner=owner,Tick=tick,Stage="CanFundTechnology",Data=new{Emergency=emergency,Allowed=allowed,UrgentRepair=repair,EssentialScale=scale}});
            var evidence=new SurvivalEvidence();
            evidence.Observe(Funding("a",1,true,false));Assert.IsEmpty(evidence.DeferredOwners);
            evidence.Observe(Threat("a",1,true,"uncovered",new[]{89}));
            foreach(string row in new[]{Funding("b",1,true,false),Funding("a",2,true,false),Funding("a",1,true,true),Funding("a",1,true,false,true),Funding("a",1,true,false,false,true)}){evidence.Observe(row);Assert.IsEmpty(evidence.DeferredOwners);}
            foreach(var threat in new[]{Threat("b",1,false,"covered",new[]{89}),Threat("c",1,true,"uncovered",Array.Empty<int>())})evidence.Observe(threat);
            evidence.Observe(Funding("b",1,true,false));evidence.Observe(Funding("c",1,true,false));Assert.IsEmpty(evidence.DeferredOwners);
            evidence.Observe(Funding("a",1,true,false));CollectionAssert.AreEquivalent(new[]{"a"},evidence.DeferredOwners);
        }
        [Test] public void FixedNormalSeeds_AllOpeningsReachNaturalEarlyMiddleCycle()
        {
            var folder=Path.Combine(Root(),".local/o6-natural");Directory.CreateDirectory(folder);
            var results=new List<Result>();
            foreach(int seed in Seeds)
            {
                var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
                var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
                var c=((AiTestMap)p.AuthoredMap).Configuration(p,seed,roster);
                Assert.AreEqual(PlayableProfile.Default.StartingCredits,p.StartingCredits);Assert.IsEmpty(c.ScenarioUnits);Assert.IsEmpty(c.ScenarioBuildings);
                var authority=new PlayableAuthorityTick(c,71,difficulty:AiDifficulty.Fighter);
                var initial=authority.CaptureDiagnosticCheckpoints();
                var current=initial.ToDictionary(x=>x.OwnerId,x=>new Result{Owner=x.OwnerId,Opening=x.Opening.Opening.ToString()});
                File.WriteAllText(Path.Combine(folder,seed+"-input.json"),Json(new{Schema="o6-editor-natural-v1",seed,Horizon,TickRate=30,Generation=71,StartingCredits=p.StartingCredits,Difficulty="Fighter",ForcedOpenings=false,Commands=Array.Empty<object>(),ScenarioUnits=c.ScenarioUnits,ScenarioBuildings=c.ScenarioBuildings,MapHash=HeadlessFixtures.MapHash(c),GeometryHash=HeadlessFixtures.GeometryHash(c),GameplayHash=AiMatchIdentity.GameplayDigest(p),AiHash=AiProfile.Initial.Hash,Roster=roster,Selections=initial.Select(x=>new{x.OwnerId,Opening=x.Opening.Opening.ToString()}),RoutePolicy=PlayableAuthorityTick.RouteDeliveryPolicy}));
                var survival=new SurvivalEvidence();
                var guardLog=new StreamWriter(Path.Combine(folder,seed+"-survival-guards.jsonl"));
                CaptureSurvivalOnly(current.Values.Where(x=>x.Opening=="Safe").Select(x=>x.Owner),line=>{survival.Observe(line);guardLog.WriteLine(line);});
                try
                {
                using(var sink=new StreamWriter(Path.Combine(folder,seed+"-facts.jsonl")))
                using(var host=new UnityHostRouteService())
                {
                    Action sample=()=>{
                        var budgets=authority.CaptureDiagnosticBudgets().ToDictionary(x=>x.OwnerId);
                        foreach(var checkpoint in authority.CaptureDiagnosticCheckpoints())
                        {
                            var v=authority.ParticipantView(checkpoint.OwnerId);var b=budgets[v.OwnerId];var r=current[v.OwnerId];
                            var own=v.Buildings.Where(x=>x.Owner==v.Owner).ToArray();var units=v.Entities.Where(x=>x.Owner==v.Owner).ToArray();
                            var queues=own.Where(x=>x.PrivateState!=null).SelectMany(x=>x.PrivateState.Orders).ToArray();
                            r.Tick=v.Tick;r.Paid=b.PaidTotal;r.Mid|=checkpoint.GamePhase!=null&&checkpoint.GamePhase.Phase>=AiGamePhase.Mid;
                            r.Expansion|=HasReadyExpansion(v);
                            r.Research|=v.OwnerResearch.Any(x=>x.Complete);r.PeakLiving=Math.Max(r.PeakLiving,units.Length);r.PeakPaidQueues=Math.Max(r.PeakPaidQueues,queues.Count(x=>x.PaidCost>0));
                            r.SurvivalDeferral=survival.DeferredOwners.Contains(r.Owner);
                            r.OpeningPhase=checkpoint.OpeningExecution.Phase.ToString();r.Reason=checkpoint.OpeningExecution.Reason;r.Milestones=checkpoint.OpeningExecution.Completed.ToArray();
                            sink.WriteLine(Json(new{Tick=v.Tick,v.OwnerId,SelectedOpening=r.Opening,Opening=checkpoint.OpeningExecution,Phase=checkpoint.GamePhase,Bank=v.Credits,b.PaidTotal,b.RepairPaidTotal,Unpaid=b.Unpaid,Reserved=b.Reserved,Income=v.IncomePerSecond,v.SettledIncome,v.OwnerEliminated,Living=units.Select(x=>new{x.Id,Kind=x.Kind.ToString(),x.Health,x.Position}),OwnedBuildings=own.Select(x=>new{x.Id,Kind=x.Kind.ToString(),x.SiteId,Phase=x.Phase.ToString(),x.Health,Queues=x.PrivateState?.Orders,Research=x.PrivateState?.Research}),Research=v.OwnerResearch,Knowledge=checkpoint.Knowledge,Expansion=checkpoint.Expansion}));
                        }
                        sink.Flush();File.WriteAllBytes(Path.Combine(folder,seed+"-checkpoint-"+authority.Tick+".world"),authority.CaptureBytes());
                        TestContext.WriteLine("O6 seed="+seed+" tick="+authority.Tick);
                    };
                    sample();
                    for(int tick=0;tick<Horizon&&!authority.WinnerTeam.HasValue;tick++)
                    {
                        bool advanced=false;for(int attempt=0;attempt<512;attempt++){if(authority.TryAdvance()){advanced=true;break;}host.Service(authority,64);}
                        Assert.True(advanced,"native route barrier at "+authority.Tick);Assert.IsNull(authority.Latest.Failure);
                        if(authority.Tick%300==0)sample();
                    }
                    sample();
                    File.WriteAllText(Path.Combine(folder,seed+"-summary.json"),Json(new{seed,Tick=authority.Tick,Termination=authority.WinnerTeam.HasValue?"win":"timeout",authority.WinnerTeam,Results=current.Values}));
                }
                foreach(var r in current.Values)r.SurvivalDeferral=survival.DeferredOwners.Contains(r.Owner);
                results.AddRange(current.Values);
                }
                finally
                {
                    PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=null;
                    PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=null;
                    PlayableAiOwnerLoop.CurrentSafeFundingStageFilterForTests=null;
                    guardLog.Dispose();authority.Stop();
                }
            }
            File.WriteAllText(Path.Combine(folder,"qualification.json"),Json(results));
            AssertQualified(results);
        }
    }
}
