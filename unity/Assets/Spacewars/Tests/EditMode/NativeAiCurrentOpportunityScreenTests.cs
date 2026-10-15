using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiCurrentOpportunityScreenTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private const string Archive="docs/native-ai/o6/current-natural-20261012-source-compiled-raw.zip";
        private const string ArchiveHash="8e4f56446e3eab63dbc935b15493fa6b20c160e9c50ed026dcaa4fe8c3dac081";
        private static string Root()
        {var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);return d.FullName;}
        private static byte[] Entry(ZipArchive zip,string name)
        {var e=zip.GetEntry(name);Assert.NotNull(e,name);using(var s=e.Open())using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}}
        private static void Exact(byte[] expected,byte[] actual,string where)
        {if(expected.Length!=actual.Length)Assert.Fail(where+" length changed "+expected.Length+" -> "+actual.Length);for(int i=0;i<expected.Length;i++)if(expected[i]!=actual[i])Assert.Fail(where+" first differing byte="+i+" expected="+expected[i]+" actual="+actual[i]);}
        private static object Get(object x,string name)
        {var f=x.GetType().GetField(name,Flags);if(f!=null)return f.GetValue(x);var p=x.GetType().GetProperty(name,Flags);Assert.NotNull(p,"missing actual state member "+name);return p.GetValue(x);}
        private static object FundingMemo(PlayableAuthorityTick authority,string owner,string typeName)
        {
            var scheduler=Get(authority,"scheduler");var loop=((System.Collections.IEnumerable)Get(scheduler,"owners")).Cast<object>().Single(x=>(string)Get(x,"OwnerId")==owner);
            var arbiter=Get(loop,"arbiter");var type=typeof(AiDecisionArbiter).Assembly.GetType("Spacewars.Runtime."+typeName,true);
            var load=type.GetMethod("Load",Flags|BindingFlags.Static);Assert.NotNull(load,"read-only actual C# funding memo loader");return load.Invoke(null,new[]{arbiter});
        }
        private sealed class PendingClaimRow
        {
            public string IntentId,Policy,Kind,ReservationId;public long ActionId,DueTick,ExpiresTick;public int SiteId,Credits;public int[] EntityIds;public string[] Claims;
        }
        private static object OwnerLoop(PlayableAuthorityTick authority,string owner)
        {var scheduler=Get(authority,"scheduler");return ((System.Collections.IEnumerable)Get(scheduler,"owners")).Cast<object>().Single(x=>(string)Get(x,"OwnerId")==owner);}
        private static PendingClaimRow[] PendingClaims(object ownerLoop)
        {return ((System.Collections.IEnumerable)Get(ownerLoop,"pendingItems")).Cast<object>().Select(x=>
            {var action=(PlayableAiAction)Get(x,"Action");var intent=(AiIntent)Get(x,"Intent");return new PendingClaimRow{IntentId=intent.Id,Policy=(string)Get(x,"PolicyName"),Kind=action.Kind.ToString(),ReservationId=intent.ReservationId,ActionId=action.ActionId,DueTick=(long)Get(x,"DueTick"),ExpiresTick=intent.ExpiresTick,SiteId=action.SiteId,Credits=intent.Credits,EntityIds=action.CopyEntityIds(),Claims=intent.Claims.ToArray()};}).ToArray();}
        private static bool ExpansionClaim(object ownerLoop,string method,int id)
        {var expansion=Get(ownerLoop,"expansion");var m=expansion.GetType().GetMethod(method,Flags);Assert.NotNull(m,"actual read-only expansion claim query "+method);return (bool)m.Invoke(expansion,new object[]{id});}
        private static object Fingerprint(object f)=>f==null?null:(object)new{Site=Get(f,"Site"),Kind=Get(f,"Kind"),Owner=Get(f,"Owner"),Claimant=Get(f,"Claimant"),Center=Get(f,"Center"),Contested=Get(f,"Contested"),Progress=Get(f,"Progress")};

        [TestCase(7108)] [TestCase(7109)]
        [Timeout(180000)]
        public void CurrentNaturalCheckpoints_RestoreAndScreenAllOwnersWithoutMutation(int seed)
        {
            var root=Root();var archivePath=Path.Combine(root,Archive);var packet=File.ReadAllBytes(archivePath);
            Assert.AreEqual(ArchiveHash,Wire.Hash(packet),"immutable natural source packet SHA256");
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)p.AuthoredMap).Configuration(p,seed,roster);
            Assert.AreEqual(8,config.Roster.Count);Assert.AreEqual(p.AuthoredMap.Id,config.MapIdentity);Assert.AreEqual(seed,config.Seed);Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
            var output=Path.Combine(root,".local/qa/o6-current-opportunity-screen-20261012");Directory.CreateDirectory(output);
            var rowsPath=Path.Combine(output,"seed-"+seed+".jsonl");int checkpoints=0,rows=0;
            using(var zip=ZipFile.OpenRead(archivePath))
            using(var sink=new StreamWriter(rowsPath,false))
            {
                var manifest=JsonConvert.DeserializeObject<JObject>(System.Text.Encoding.UTF8.GetString(Entry(zip,"packet-manifest.json")));
                var members=manifest["files"].OfType<JObject>().Where(x=>((string)x["path"]).StartsWith("natural/"+seed+"-checkpoint-",StringComparison.Ordinal)).ToDictionary(x=>(string)x["path"],x=>(string)x["sha256"]);
                Assert.AreEqual(61,members.Count,"every 300-tick checkpoint 0..18000 is represented for this seed");
                for(int tick=0;tick<=18000;tick+=300)
                {
                    string member="natural/"+seed+"-checkpoint-"+tick+".world";var input=Entry(zip,member);Assert.AreEqual(members[member],Wire.Hash(input),"original checkpoint member hash");
                    var envelope=PlayableWorldState.Decode(input);Assert.AreEqual(seed,envelope.Seed);Assert.AreEqual(71,envelope.Generation);Assert.AreEqual(tick,envelope.Tick);Assert.AreEqual(config.SourceIdentity,envelope.SourceIdentity,"original test-map/config identity");
                    var authority=PlayableAuthorityTick.RestoreBytes(input,config);
                    try
                    {
                        Assert.AreEqual(tick,authority.Tick);Exact(input,authority.CaptureBytes(),"restore before owner projections "+member);
                        var checkpointsByOwner=authority.CaptureDiagnosticCheckpoints().ToDictionary(x=>x.OwnerId);
                        var budgets=authority.CaptureDiagnosticBudgets().ToDictionary(x=>x.OwnerId);
                        Assert.AreEqual(8,checkpointsByOwner.Count);Assert.AreEqual(8,budgets.Count);
                        foreach(var owner in roster.Select(x=>x.Id))
                        {
                            var cp=checkpointsByOwner[owner];var budget=budgets[owner];var view=authority.ParticipantView(owner);var observation=PlayableAiObservation.From(view);
                            var ownerLoop=OwnerLoop(authority,owner);var pendingClaims=PendingClaims(ownerLoop);
                            Assert.AreEqual(seed,cp.Seed);Assert.AreEqual(71,cp.Generation);Assert.AreEqual(owner,cp.OwnerId);Assert.AreEqual(tick,view.Tick);
                            Assert.AreEqual(PlayableAiOpeningComposition.SourceIdentity,cp.SourceIdentity);Assert.AreEqual(AiProfile.Initial.Id,cp.AiProfileId);Assert.AreEqual(AiProfile.Initial.Hash,cp.AiHash);Assert.AreEqual(AiDifficulty.Fighter,cp.Difficulty);
                            Assert.AreEqual(p.ProfileId,view.ProfileId);Assert.AreEqual(p.Revision,view.ProfileRevision);Assert.AreEqual(8,view.Participants.Count);Assert.AreEqual(owner,view.Participants[(int)view.Owner].Id);
                            var scout=cp.Armies?.Armies?.Where(a=>a.Role==AiArmyRole.Scout&&a.Phase!=AiArmyPhase.Disbanded).ToArray()??Array.Empty<AiArmyState>();
                            var memberships=cp.Armies?.Armies?.Where(a=>a.Phase!=AiArmyPhase.Disbanded).ToArray()??Array.Empty<AiArmyState>();
                            var explorers=view.Entities.Where(e=>e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Explorer&&e.Health>0).Select(e=>new
                            {e.Id,e.Health,e.Moving,e.Position,Order=e.CurrentOrder==null?null:new{Kind=e.CurrentOrder.Kind,e.CurrentOrder.TargetId,e.CurrentOrder.IssuedTick},
                             Stamp=e.OrderStamp==null?null:new{e.OrderStamp.ActionId,e.OrderStamp.Generation,e.OrderStamp.Sequence,e.OrderStamp.Tick,e.OrderStamp.Source},
                             Navigation=e.NavigationOutcome,TacticalOwner=memberships.FirstOrDefault(a=>a.Members.Contains(e.Id))?.TacticalOwner,
                             ScoutObjective=scout.FirstOrDefault(a=>a.Members.Contains(e.Id))?.Objective,ScoutCreated=scout.FirstOrDefault(a=>a.Members.Contains(e.Id))?.CreatedTick,
                             ScoutProgress=scout.FirstOrDefault(a=>a.Members.Contains(e.Id))?.ProgressTick,
                             PendingClaims=pendingClaims.Where(x=>x.EntityIds.Contains(e.Id)||x.Claims.Contains("recipient:"+e.Id)).ToArray(),ExpansionClaimsActor=ExpansionClaim(ownerLoop,"ClaimsActor",e.Id)}).ToArray();
                            var threats=AiDefensePlanner.Threats(observation,p).Select(e=>new{e.Id,Owner=e.Owner,Kind=e.Kind.ToString(),e.Position,e.Health}).ToArray();
                            var sites=view.Sites.Select(s=>new{Id=s.Site.Id,Kind=s.Site.Kind.ToString(),Center=s.Site.Position,s.CenterId,Cost=TerritoryRules.Cost(p,s.Site.Kind),Owner=s.Owner?.ToString(),Claimant=s.Claimant?.ToString(),s.Contested,s.Ready,
                                PendingClaims=pendingClaims.Where(x=>x.SiteId==s.Site.Id&&x.Claims.Any(c=>c=="expansion-site:"+s.Site.Id||c.StartsWith("slot:"+s.Site.Id+":",StringComparison.Ordinal))).ToArray(),ExpansionClaimsSite=ExpansionClaim(ownerLoop,"ClaimsSite",s.Site.Id),
                                PublicObjective=view.PublicScoutObjectives.FirstOrDefault(o=>o.SiteId==s.Site.Id)==null?null:new{Role=view.PublicScoutObjectives.Single(o=>o.SiteId==s.Site.Id).Role,view.PublicScoutObjectives.Single(o=>o.SiteId==s.Site.Id).Reachable,view.PublicScoutObjectives.Single(o=>o.SiteId==s.Site.Id).Approach}}).ToArray();
                            var technology=new{Science=view.Buildings.Where(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.ScientificCenter).Select(b=>new{b.Id,b.SiteId,b.Phase,b.Health}).ToArray(),
                                Availability=view.ResearchAvailability,Orders=view.OwnerResearch,
                                ProductionQueues=view.Buildings.Where(b=>b.Owner==view.Owner&&b.PrivateState!=null).Select(b=>new{b.Id,Kind=b.Kind.ToString(),b.SiteId,b.Phase,b.QueueCount,b.ProductionProgress,b.PrivateState.Repeat,b.PrivateState.RepeatKind,
                                    Orders=b.PrivateState.Orders.Select(o=>new{o.Id,Kind=o.Kind.ToString(),o.PaidCost,o.PopulationCost,o.Duration,o.Remaining,o.Active,o.Progress}).ToArray()}).ToArray(),
                                EntityQueues=view.Entities.Where(e=>e.Owner==view.Owner&&e.Queue!=null).Select(e=>new{e.Id,e.Queue}).ToArray(),Opening=cp.Opening,cp.OpeningExecution};
                            var siteMemo=FundingMemo(authority,owner,"AiExpansionFinancial");
                            var siteFinance=siteMemo==null?null:new{Active=Fingerprint(Get(siteMemo,"Active")),Expired=((System.Collections.IEnumerable)Get(siteMemo,"Expired")).Cast<object>().Select(x=>new{Site=Get(x,"Key"),Fingerprint=Fingerprint(Get(x,"Value"))}).ToArray()};
                            var technologyMemo=FundingMemo(authority,owner,"AiTechnologyFunding");
                            var technologyFunding=technologyMemo==null?null:new{Owner=Get(technologyMemo,"Owner"),Generation=Get(technologyMemo,"Generation"),Branch=Get(technologyMemo,"Branch"),Phase=Get(technologyMemo,"Phase"),Site=Get(technologyMemo,"Site"),Slot=Get(technologyMemo,"Slot"),Parent=Get(technologyMemo,"Parent"),Center=Get(technologyMemo,"Center"),Amount=Get(technologyMemo,"Amount"),Cash=Get(technologyMemo,"Cash"),ScienceCost=Get(technologyMemo,"ScienceCost"),ResearchCost=Get(technologyMemo,"ResearchCost"),Income=Get(technologyMemo,"Income"),Progress=Get(technologyMemo,"Progress"),Expired=Get(technologyMemo,"Expired"),Created=Get(technologyMemo,"Created"),Expires=Get(technologyMemo,"Expires"),BuildIntent=Get(technologyMemo,"BuildIntent"),BuildTerms=Get(technologyMemo,"BuildTerms"),BuildAmount=Get(technologyMemo,"BuildAmount"),BuildReceipt=Get(technologyMemo,"BuildReceipt")};
                            var row=new{Seed=seed,CheckpointTick=tick,Owner=owner,KnowledgeObservationTick=cp.Knowledge?.ObservationTick,LastDecisionTick=cp.DecisionTick,OwnerPendingActionId=cp.PendingActionId,PendingItems=pendingClaims,
                                Opening=cp.Opening?.Opening.ToString(),Phase=cp.GamePhase,Eliminated=view.OwnerEliminated,Bank=new{budget.Liquid,budget.SafetyReserve,budget.PaidTotal,
                                    Reserved=budget.Reserved.Select(x=>new{x.Id,x.Purpose,x.Amount,x.CreatedTick,x.ExpiresTick,x.ReleaseCondition,x.Terms,x.Receipt,Action=x.Action.Kind.ToString(),x.Action.SiteId,Actors=x.Action.CopyEntityIds()}).ToArray(),
                                    Unpaid=budget.Unpaid.Select(x=>new{x.Id,x.Purpose,x.Amount,x.CreatedTick,x.ExpiresTick,x.ReleaseCondition,x.Terms,x.Receipt,Action=x.Action.Kind.ToString(),x.Action.SiteId,Actors=x.Action.CopyEntityIds()}).ToArray(),
                                    Paid=budget.Paid.Select(x=>new{x.Id,x.Purpose,x.Amount,x.CreatedTick,x.ExpiresTick,x.ReleaseCondition,x.Terms,x.Receipt,Action=x.Action.Kind.ToString(),x.Action.SiteId,Actors=x.Action.CopyEntityIds()}).ToArray()},
                                Explorers=explorers,ScoutRegistry=scout.Select(a=>new{a.Id,a.LeaderId,a.Members,a.Objective,a.CreatedTick,a.ProgressTick,a.TacticalOwner,a.Phase}).ToArray(),
                                Knowledge=cp.Knowledge,Threats=threats,VisibleIncomeSites=sites,Expansion=cp.Expansion,Infrastructure=cp.Infrastructure,SiteFinance=siteFinance,TechnologyFunding=technologyFunding,
                                FinanceHistory=cp.Records.Where(r=>r.Policy=="expansion"||r.Policy=="technology"||r.Policy=="infrastructure").Select(r=>new{r.Policy,r.ActionId,r.Kind,r.DueTick,r.ApplicationTick,Status=r.Status.ToString(),RuntimeStatus=r.RuntimeStatus?.ToString(),r.Message,r.ReceiptIdentity}).ToArray(),Technology=technology,
                                RouteProofStatus="NotEvaluated"};
                            sink.WriteLine(JsonConvert.SerializeObject(row));rows++;
                        }
                        Exact(input,authority.CaptureBytes(),"whole-world bytes after all owner reads "+member);
                    }
                    finally{authority.Stop();}
                    checkpoints++;
                }
            }
            Assert.AreEqual(61,checkpoints);Assert.AreEqual(488,rows);TestContext.WriteLine("CURRENT_OPPORTUNITY_SCREEN "+JsonConvert.SerializeObject(new{Seed=seed,Checkpoints=checkpoints,OwnerRows=rows,Output=rowsPath,WholeWorldBytesEqualBeforeAndAfterAllOwnerReads=true,RouteProof="NotEvaluated"}));
        }
    }
}
