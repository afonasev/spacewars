using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiFourPlayerEconomyTests
    {
        [Test,Timeout(600000)] public void MossHighlandTwoTeamsDevelopThroughOrdinaryAuthority()=>Run(postOpening:true);
        [Test,Timeout(600000)] public void MossHighlandColdStartGrowsIncomeWithoutGifts()=>Run(postOpening:false);
        private static void Run(bool postOpening)
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new MossHighlandMap());
            var roster=Enumerable.Range(0,4).Select(i=>new OfflineParticipant("foundry-"+(i+1),i+1,i/2+1,OfflineControl.Ai)).ToArray();
            var config=((MossHighlandMap)profile.AuthoredMap).Configuration(profile,7108,roster);
            if(postOpening)config=WithReadyHome(config);
            var authority=new PlayableAuthorityTick(config,71);
            // Match PlayableRuntime's ordinary per-participant initialization; no world,
            // money, queues, observations or decisions are altered after initialization.
            var loops=roster.Select((p,i)=>new PlayableAiOwnerLoop(profile,PlayableAiOpeningComposition.Initialize(config.Seed,p.Id,aiProfile:AiProfile.Initial),71,null,AiProfile.Initial,i==3?AiDifficulty.Veteran:AiDifficulty.Fighter)).ToArray();
            typeof(PlayableAuthorityTick).GetField("scheduler",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(authority,new AiAuthorityScheduler(loops));
            var folder=Path.GetFullPath(Path.Combine(ApplicationRoot(),".local/four-player/"+(postOpening?"post-opening":"cold-start")));Directory.CreateDirectory(folder);
            var timeline=new List<object>();bool grownIncome=false,usedAdditionalFactory=false;
            using(var host=new UnityHostRouteService())
            try
            {
                for(int tick=0;tick<7200;tick++)
                {
                    bool advanced=false;for(int attempt=0;attempt<512;attempt++){if(authority.TryAdvance()){advanced=true;break;}host.Service(authority,64);}
                    Assert.True(advanced,"ordinary route barrier at "+authority.Tick);Assert.IsNull(authority.Latest.Failure);
                    if(authority.Tick%300==0)
                    {
                        foreach(var loop in loops)
                        {
                            var view=authority.ParticipantView(loop.OwnerId);
                            var ownBuildings=view.Buildings.Where(b=>b.Owner==view.Owner).ToArray();
                            int initialIncome=new AiOpeningExecutor(loop.Checkpoint.Opening,71).RequiredHomeRefineryCount(AiProfile.Initial);
                            grownIncome|=ownBuildings.Count(b=>b.Kind==PlayableBuildingKind.Refinery&&b.Phase==ConstructionPhase.Ready)>initialIncome;
                            var factories=ownBuildings.Where(b=>b.Kind==PlayableBuildingKind.Factory).OrderBy(b=>b.Id).ToArray();
                            usedAdditionalFactory|=factories.Skip(1).Any(b=>b.PrivateState.Orders.Count>0);
                            var checkpoint=authority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId==loop.OwnerId);
                            var row=new{authority.Tick,loop.OwnerId,Difficulty=checkpoint.Difficulty,view.Credits,view.IncomePerSecond,view.SettledIncome,view.OwnerEliminated,
                                Buildings=view.Buildings.Where(b=>b.Owner==view.Owner).Select(b=>new{b.Id,b.Kind,b.SiteId,b.Phase,Orders=b.PrivateState?.Orders}).ToArray(),
                                Army=view.Entities.Where(e=>e.Owner==view.Owner).GroupBy(e=>e.Kind).Select(g=>new{Kind=g.Key,Count=g.Count()}).ToArray(),
                                checkpoint.OpeningExecution,checkpoint.EconomyDecision,Technology=loop.TechnologyPlan,Ledger=loop.Budget.Capture(),Records=checkpoint.Records};
                            timeline.Add(row);
                        }
                        File.WriteAllText(Path.Combine(folder,"timeline.json"),JsonConvert.SerializeObject(timeline,Formatting.Indented));
                        TestContext.WriteLine("2x2 actual tick="+authority.Tick);
                    }
                    if(authority.Tick==3000||authority.Tick==6000)
                    {
                        var bytes=authority.CaptureBytes();File.WriteAllBytes(Path.Combine(folder,"checkpoint-"+authority.Tick+".world"),bytes);var restored=PlayableAuthorityTick.RestoreBytes(bytes,config);
                        CollectionAssert.AreEqual(bytes,restored.CaptureBytes(),"investment decisions survive exact ordinary save/restore");
                        authority.Stop();authority=restored;
                        loops=((AiAuthorityScheduler)typeof(PlayableAuthorityTick).GetField("scheduler",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(authority)).Owners.ToArray();
                    }
                    if(authority.Latest.Outcome!=PlayableMatchOutcome.Playing)break;
                }
                File.WriteAllBytes(Path.Combine(folder,"checkpoint-final.world"),authority.CaptureBytes());
                Assert.True(grownIncome,"ordinary AI must invest in income beyond its opening minimum");if(postOpening)Assert.True(usedAdditionalFactory,"income growth must fund and use additional production in the actual match");Assert.GreaterOrEqual(authority.Tick,300);Assert.AreEqual(4,config.Roster.Count);Assert.AreEqual(2,config.Roster.Select(p=>p.Team).Distinct().Count());
            }
            finally{authority.Stop();}
        }
        // A production-scaling scenario starts AFTER the opening's required home
        // structures are READY. Cold-start growth remains a separate ordinary match.
        // Keep gameplay prices, seed, map, banks and authority unchanged; use validated
        // authored initial buildings rather than granting money during the run.
        private static OfflineMatchConfiguration WithReadyHome(OfflineMatchConfiguration c)
        {
            var participant=c.Roster[0];var home=c.Sites.Single(s=>s.Id==c.Starts[c.Assignments[0]].SiteId);
            var opening=PlayableAiOpeningComposition.Initialize(c.Seed,participant.Id,aiProfile:AiProfile.Initial);
            int required=new AiOpeningExecutor(opening,71).RequiredHomeRefineryCount(AiProfile.Initial);
            var costs=new double[c.Starts.Count,c.Starts.Count];for(int a=0;a<c.Starts.Count;a++)for(int b=0;b<c.Starts.Count;b++)costs[a,b]=c.RouteCost(a,b);
            System.ArgumentException last=null;
            foreach(var factory in home.Slots)
            foreach(var income in Choose(home.Slots.Where(s=>s.Id!=factory.Id).ToArray(),required))
            {
                var buildings=income.Select(s=>new OfflineScenarioBuilding(participant.LogicalPlayer,PlayableBuildingKind.Refinery,home.Id,s.Id,s.Position,s.Heading))
                    .Concat(new[]{new OfflineScenarioBuilding(participant.LogicalPlayer,PlayableBuildingKind.Factory,home.Id,factory.Id,factory.Position,factory.Heading)}).ToArray();
                try{return new OfflineMatchConfiguration(c.Profile,c.SourceIdentity+"-ready-home",c.MapIdentity+"-ready-home",c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),costs,c.Spectators.ToArray(),c.ScenarioUnits.ToArray(),c.Profile.AuthoredMap.Id,c.ScenarioBuildings.Concat(buildings).ToArray());}
                catch(System.ArgumentException error){last=error;}
            }
            throw new System.ArgumentException("No validated ready-home placement for the scaling fixture",last);
        }
        private static System.Collections.Generic.IEnumerable<TerritorySlot[]> Choose(TerritorySlot[] slots,int count)
        {
            if(count==0){yield return Array.Empty<TerritorySlot>();yield break;}
            for(int i=0;i<=slots.Length-count;i++)foreach(var tail in Choose(slots.Skip(i+1).ToArray(),count-1))yield return new[]{slots[i]}.Concat(tail).ToArray();
        }
        private static string ApplicationRoot(){var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;return d.FullName;}
    }
}
