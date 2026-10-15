using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiSiteFinancialGoalTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static object Get(object value,string field)=>value.GetType().GetField(field,F).GetValue(value);
        private static object Property(object value,string name)=>value.GetType().GetProperty(name,F).GetValue(value);
        private static object Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,F).Invoke(value,args);
        private static string Root(){var p=new DirectoryInfo(Directory.GetCurrentDirectory());while(p!=null&&!File.Exists(Path.Combine(p.FullName,"workflow/project.json")))p=p.Parent;Assert.NotNull(p);return p.FullName;}
        private static bool Safe(PlayableAiObservation o,PlayableProfile p,TerritorySiteSnapshot site,PlayableEntitySnapshot actor,PlayableRouteProof route)=>(bool)typeof(AiExpansionPlanner).GetMethod("Safe",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{o,p,AiProfile.Initial,site,actor,route});
        [Test]
        public void ActualScheduled990KeepsSiteFinancialGoalWhileScoutLegallyClaimsWitness()
        {
            var root=Root();var folder=Path.Combine(root,".local/o6-site-financial-red");Directory.CreateDirectory(folder);
            var input=File.ReadAllBytes(Path.Combine(root,"unity/Tests/Fixtures/o6-expansion-funding/checkpoint-7108-900.world"));
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)p.AuthoredMap).Configuration(p,7108,roster);var a=PlayableAuthorityTick.RestoreBytes(input,config);
            try
            {
                CollectionAssert.AreEqual(input,a.CaptureBytes(),"Audited original900 whole C9");Assert.AreEqual(900,a.Tick);
                var domain=Get(a,"domain");var loop=((IEnumerable)Get(Get(a,"scheduler"),"owners")).Cast<object>().Single(x=>(string)Property(x,"OwnerId")=="ai-4");
                PlayableAiObservation Observation()=>PlayableAiObservation.From((PlayableSnapshot)Call(domain,"PlayerSnapshotForArmyAi",Get(a,"sequence"),RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7108,(PlayableOwner)Call(domain,"OwnerFor","ai-4"),true,Property(loop,"ArmyState")));
                using(var host=new UnityHostRouteService())
                {
                    int ticks=0;
                    Action advance=()=>{bool done=false;for(int attempt=0;attempt<512;attempt++){if(a.TryAdvance()){done=true;break;}host.Service(a,64);}Assert.True(done,"Ordinary authority route barrier");ticks++;Assert.LessOrEqual(ticks,90);};
                    while(a.Tick<989)advance();
                    Assert.AreEqual(989,a.Tick);Assert.AreEqual(945,(long)Get(loop,"lastDecisionTick"));Assert.AreEqual(45,(long)Get(loop,"DecisionIntervalTicks"));
                    var before=Observation();var site=before.Sites.Single(x=>x.Site.Id==21);var actor=before.Entities.Single(x=>x.Id==13);
                    var route=before.RouteProofs.Single(x=>x.UnitId==13&&x.TargetId==21&&x.Kind==PlayableRouteTargetKind.PublicObjective);
                    var army=(AiArmyRegistryState)Property(loop,"ArmyState");
                    File.WriteAllBytes(Path.Combine(folder,"before-989.world"),a.CaptureBytes());
                    File.WriteAllText(Path.Combine(folder,"before-989.json"),JsonConvert.SerializeObject(new{Observation=before,Army=army,Budget=((AiBudgetLedger)Get(loop,"budget")).Capture(),Safe=Safe(before,p,site,actor,route),Pending=((IEnumerable)Get(loop,"pendingItems")).Cast<object>().Select(x=>new{Policy=Get(x,"PolicyName"),Action=Get(x,"Action")})}));
                    Assert.AreEqual(16,before.Credits);Assert.AreEqual(200,TerritoryRules.Cost(p,site.Site.Kind));Assert.AreEqual(PlayableBuildingKind.Mine,site.Site.Kind);
                    Assert.Zero(site.CenterId);Assert.False(site.Contested);Assert.True(!site.Owner.HasValue||site.Owner==before.Owner);Assert.True(!site.Claimant.HasValue||site.Claimant==before.Owner);
                    Assert.True(before.PublicScoutObjectives.Any(x=>x.SiteId==21&&x.Reachable&&x.Role!=PlayablePublicScoutObjectiveRole.PossibleEnemyStart));
                    Assert.True(Safe(before,p,site,actor,route));Assert.IsEmpty(AiDefensePlanner.Threats(before,p));Assert.False(actor.Moving);Assert.IsNull(actor.CurrentOrder);
                    Assert.False(army.Armies.Any(x=>x.Members.Contains(13)||x.Reinforcements.Contains(13)));
                    Assert.False(((IEnumerable)Get(loop,"pendingItems")).Cast<object>().Any(x=>((PlayableAiAction)Get(x,"Action")).EntityIds.Contains(13)));
                    advance();Assert.AreEqual(990,a.Tick);Assert.AreEqual(90,ticks);Assert.AreEqual(990,(long)Get(loop,"lastDecisionTick"),"Ordinary production scheduler decision, no manual Review");
                    var after=Observation();var postSite=after.Sites.Single(x=>x.Site.Id==21);var postActor=after.Entities.Single(x=>x.Id==13);
                    var postRoute=after.RouteProofs.Single(x=>x.UnitId==13&&x.TargetId==21&&x.Kind==PlayableRouteTargetKind.PublicObjective);
                    var budget=((AiBudgetLedger)Get(loop,"budget")).Capture();var postArmy=(AiArmyRegistryState)Property(loop,"ArmyState");
                    var pending=((IEnumerable)Get(loop,"pendingItems")).Cast<object>().Select(x=>new{Policy=(string)Get(x,"PolicyName"),Action=(PlayableAiAction)Get(x,"Action")}).ToArray();
                    var economy=(AiEconomyPlan)Property(loop,"EconomyPlan");var composition=(AiCompositionAssessment)Get(loop,"compositionAssessment");
                    var scaling=((AiProductionDemand)Get(loop,"demand")).Assess(after,p,AiProfile.Initial,((AiBudgetLedger)Get(loop,"budget")).Available+budget.Reserved.Where(AiTechnologyFunding.IsFunding).Sum(x=>(long)x.Amount),(AiRosterCatalog)Get(loop,"roster"),composition.Candidates.LastOrDefault()?.Action.UnitKind??PlayableEntityKind.Tank);
                    var history=(IDictionary)Get(Get(loop,"arbiter"),"history");
                    File.WriteAllBytes(Path.Combine(folder,"after-990.world"),a.CaptureBytes());
                    File.WriteAllText(Path.Combine(folder,"after-990.json"),JsonConvert.SerializeObject(new{Observation=after,Army=postArmy,Budget=budget,Pending=pending,Safe=Safe(after,p,postSite,postActor,postRoute),Economy=economy,Technology=Property(loop,"TechnologyPlan"),Composition=composition,PostDecisionScalingPreview=scaling,History=history,ProtectionEvidence="Actual safety/unpaid/startup/technology holds plus actual plans; scaling preview uses production Assess. Do not assume preview is the local protectedFunds value; no positive hold assertion when higher bands consume funds."}));
                    var bytes=a.CaptureBytes();var restore=PlayableAuthorityTick.RestoreBytes(bytes,config);try{CollectionAssert.AreEqual(bytes,restore.CaptureBytes(),"Exact actual post990 C9 before behavior RED");}finally{restore.Stop();}
                    Assert.AreEqual(22,after.Credits);Assert.Less(after.Credits,TerritoryRules.Cost(p,postSite.Site.Kind));
                    Assert.Zero(postSite.CenterId);Assert.False(postSite.Contested);Assert.True(!postSite.Owner.HasValue||postSite.Owner==after.Owner);Assert.True(!postSite.Claimant.HasValue||postSite.Claimant==after.Owner);
                    Assert.True(after.PublicScoutObjectives.Any(x=>x.SiteId==21&&x.Reachable&&x.Role!=PlayablePublicScoutObjectiveRole.PossibleEnemyStart));
                    Assert.True(Safe(after,p,postSite,postActor,postRoute),"CURRENT actual site/proof still safe after ordinary Review");Assert.IsEmpty(AiDefensePlanner.Threats(after,p));
                    Assert.True(postArmy.Armies.Any(x=>x.TacticalOwner=="scout"&&x.Members.Contains(13)),"Preserve actual legal Scout ownership; financial witness is not an actor lease");
                    Assert.True(pending.Any(x=>x.Policy=="scout"&&x.Action.EntityIds.Contains(13)&&x.Action.SiteId==17));
                    Assert.Zero(((AiExpansionPlanner)Get(loop,"expansion")).Capture().SiteId,"No unfunded gameplay expansion claim");
                    Assert.IsEmpty(budget.Reserved.Where(x=>x.Purpose=="expansion-financial").SelectMany(x=>x.Claims));
                    Assert.True(history.Contains("expansion-financial-lifecycle-v1"),"BEHAVIOR_RED: CURRENT safe Mine21 at actual ordinary990 needs a serialized site-only financial lifecycle even when Scout legally owns its proof witness; positive held money is separately bounded by higher protected bands");
                    Assert.AreEqual(21,(int)Call(Get(loop,"expansionFinancial"),"ActiveSite",Get(loop,"arbiter")),"Typed lifecycle owns current Mine21, not its Scout witness");
                    var financial=budget.Reserved.Single(x=>x.Purpose=="expansion-financial");Assert.AreEqual(21,financial.Action.SiteId);Assert.AreEqual(PlayableCommandKind.BuildAt,financial.Action.Kind);Assert.IsEmpty(financial.Action.EntityIds);Assert.IsEmpty(financial.Claims);Assert.GreaterOrEqual(financial.Amount,0);Assert.LessOrEqual(financial.Amount,after.Credits);
                }
            }
            finally{a.Stop();}
        }
    }
}
