using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
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
    public sealed class NativeAiO6DefenseAdmissionDiagnosticTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private const string Owner="ai-5";
        private const int Seed=7108,StartTick=8400,EndTick=8445;
        private const string Input=".local/o6-natural/7108-checkpoint-8400.world";
        private const string InputHash="807aea40a50e6a95076c3a91b527acc4b74a6dd12ddf33c5cbd311fa265a124e";
        private const string BaselineArchive="docs/native-ai/o6/defense-admission-source-compiled-raw-20261012.zip";
        private const string BaselineArchiveHash="6f689e39155c2b931d80db8698f45d7d0e7427525daa8842736be41c1fad98d8";
        private static string Root()
        {var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);return d.FullName;}
        private static object Get(object x,string name)
        {var f=x.GetType().GetField(name,F);if(f!=null)return f.GetValue(x);var p=x.GetType().GetProperty(name,F);Assert.NotNull(p,"member "+name);return p.GetValue(x);}
        private static object Call(object x,string name,params object[] args)=>x.GetType().GetMethod(name,F).Invoke(x,args);
        private static byte[] StateBytes(AiDefensePlanner planner)
        {using(var ms=new MemoryStream())using(var w=new BinaryWriter(ms)){Call(planner,"WriteState",w);w.Flush();return ms.ToArray();}}
        private static void Advance(PlayableAuthorityTick a,UnityHostRouteService host)
        {for(int i=0;i<512;i++){if(a.TryAdvance())return;host.Service(a,64);}Assert.Fail("ordinary typed-route barrier timed out at "+a.Tick);}
        private static object ActionRow(PlayableAiAction a)=>a==null?null:new{a.ActionId,a.PlayerId,a.Generation,a.SnapshotSequence,a.Kind,Actors=a.CopyEntityIds(),a.TargetId,a.Target};
        private static object RouteRow(PlayableRouteProof r)=>new{r.UnitId,Owner=r.Owner.ToString(),r.Generation,r.Tick,r.GeometryRevision,r.Origin,r.Radius,Kind=r.Kind.ToString(),r.TargetId,r.Target,r.Goal,Path=r.Path.ToArray()};
        private static JObject ProjectionRow(PlayableRouteProjectionReadback r)=>JObject.FromObject(new
        {
            r.OwnerId,Owner=r.Owner.ToString(),r.UnitId,Kind=r.Kind.ToString(),r.TargetId,r.Generation,r.Tick,r.Origin,r.Radius,
            RequestedTarget=r.RequestedTarget,Outcome=r.Outcome.ToString(),r.BindingRejection,ResolvedTarget=r.ResolvedTarget,
            r.OriginVisible,r.TargetVisible,r.ObservedGeometryRevision,r.ObservedObstacleCount,r.CandidateCount,
            Candidates=r.Candidates.Select(c=>new{c.Index,c.Candidate,c.Visible,Outcome=c.Outcome.ToString(),Path=c.Path.ToArray(),
                c.FailedSegmentIndex,c.FailedFrom,c.FailedTo,c.AuthoritativeSegmentFree,c.VisibleSegment}).ToArray()
        });
        private static JObject ProposalReadback(object owner,PlayableAiObservation o,PlayableProfile profile,AiProfile ai,PlayableAiOwnerCheckpoint checkpoint)
        {
            var liveRegistry=(AiArmyRegistry)Get(owner,"armies");var registryState=liveRegistry.Capture();
            var detached=AiArmyRegistry.Restore(registryState,ai,checkpoint.Difficulty,o.Tick);
            var live=(AiDefensePlanner)Get(owner,"defense");byte[] state=StateBytes(live);
            var clone=new AiDefensePlanner(o.OwnerId,o.Generation);
            using(var ms=new MemoryStream(state))using(var r=new BinaryReader(ms))Call(clone,"ReadState",r,(long)o.Tick,detached);
            var beforeRegistry=JsonConvert.SerializeObject(liveRegistry.Capture());var beforeState=Convert.ToBase64String(state);
            var detachedRegistryBefore=JsonConvert.SerializeObject(detached.Capture());
            var threats=AiDefensePlanner.Threats(o,profile);var own=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0).OrderBy(u=>u.Id).ToArray();
            var groups=registryState.Armies.Where(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded).OrderBy(a=>a.Id).ToArray();
            var routeMethod=typeof(AiDefensePlanner).GetMethod("Route",BindingFlags.Static|BindingFlags.NonPublic);
            var localThreatsMethod=typeof(AiDefensePlanner).GetMethod("LocalThreats",BindingFlags.Static|BindingFlags.NonPublic);
            var routeMatrix=own.SelectMany(u=>threats.Select(t=>
            {
                var accepted=(PlayableRouteProof)routeMethod.Invoke(null,new object[]{o,u,t});
                var observed=o.RouteProofs.Where(r=>r.UnitId==u.Id&&r.TargetId==t.Id).ToArray();
                var army=registryState.Armies.FirstOrDefault(a=>a.Phase!=AiArmyPhase.Disbanded&&a.Members.Contains(u.Id));
                return new{ArmyId=army?.Id??0,ArmyRole=army?.Role.ToString(),UnitId=u.Id,UnitKind=u.Kind.ToString(),ThreatId=t.Id,ActualRouteAccepted=accepted!=null,
                    ExpectedOwner=o.Owner.ToString(),ExpectedGeneration=o.Generation,ExpectedTick=o.Tick,ExpectedOrigin=u.Position,ExpectedTarget=t.Position,
                    ObservedProofCount=observed.Length,Accepted=accepted==null?null:RouteRow(accepted),SameUnitTargetProofs=observed.Select(RouteRow).ToArray()};
            })).ToArray();
            var groupRows=groups.Select(g=>
            {
                var members=own.Where(u=>g.Members.Contains(u.Id)).ToArray();
                return new{g.Id,g.OwnerId,g.TacticalOwner,Role=g.Role.ToString(),Phase=g.Phase.ToString(),g.Objective,Members=g.Members,Reinforcements=g.Reinforcements,
                    g.MinimumViableForce,LivingMembers=members.Select(u=>new{u.Id,Kind=u.Kind.ToString(),u.Health,u.Position,u.Held,u.Moving,
                        Order=u.CurrentOrder==null?null:new{Kind=u.CurrentOrder.Kind.ToString(),u.CurrentOrder.TargetId,Owner=u.CurrentOrder.Owner.ToString(),u.CurrentOrder.Generation,u.CurrentOrder.IssuedTick}}).ToArray(),
                    WholeGroupTargetForce=threats.Select(t=>
                    {
                        var local=(PlayableEntitySnapshot[])localThreatsMethod.Invoke(null,new object[]{o,profile,t});
                        double force=AiDefensePlanner.Force(members,profile,ai),required=AiDefensePlanner.Force(local,profile,ai);
                        int routed=members.Count(u=>routeMethod.Invoke(null,new object[]{o,u,t})!=null);
                        return new{ThreatId=t.Id,TargetForce=force,RequiredLocalForce=required,ForceSufficient=force>=required,WholeGroupRouteCount=routed,WholeGroupRouted=routed==members.Length};
                    }).ToArray()};
            }).ToArray();
            var free=own.Where(u=>liveRegistry.ArmyFor(u.Id)==0&&AiRosterCatalog.Initial.For(u.Kind).lineWeight>0).ToArray();
            var technologyFunding=(AiTechnologyFunding)Get(owner,"technologyFunding");var hasTechnologyHold=(bool)Call(technologyFunding,"HasHold",Get(owner,"budget"));
            long lastDecisionTickAfterReview=(long)Get(owner,"lastDecisionTick"),decisionIntervalTicks=(long)Get(owner,"DecisionIntervalTicks");
            bool emergencyObserved=(bool)Get(Get(owner,"defense"),"emergencyObserved");
            var routeRequestCandidates=AiArmyPlanner.RouteRequests(o.Entities,o.Buildings,o.PublicScoutObjectives,o.Owner,o.Generation,o.Tick,profile,o.IsHostile);
            var proposal=clone.Propose(o,profile,ai,detached);
            return JObject.FromObject(new
            {
                Tick=o.Tick,Owner=o.OwnerId,Generation=o.Generation,Sequence=o.SnapshotSequence,Profile=new{profile.ProfileId,ProfileRevision=profile.Revision,NativeId=ai.Id,ai.Revision,ai.Hash,checkpoint.Difficulty},
                NativeValues=new{MinimumUnits=ai.Value("armies.minimumUnits"),ForceHorizon=ai.Value("utility.forceHorizonSeconds"),MajorCount=groups.Length,MajorCap=liveRegistry.MajorCap},
                ActualDefenseState=new{Active=live.Active,live.ArmyId,live.PendingId,EmergencyObserved=Get(live,"emergencyObserved"),TargetId=Get(live,"targetId"),StateBytes=beforeState},
                Registry=new{registryState.OwnerId,registryState.Generation,registryState.NextId,MajorCount=groups.Length,MajorCap=liveRegistry.MajorCap,FreeCombatUnits=free.Select(u=>u.Id).ToArray(),FreeCombatCount=free.Length,Groups=groupRows},
                Threats=threats.Select(t=>new{t.Id,Kind=t.Kind.ToString(),Owner=t.Owner.ToString(),t.Health,t.Position,LocalCenterIds=o.Buildings.Where(b=>b.Owner==o.Owner&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost)).Where(b=>Math.Sqrt(Math.Pow(t.Position.X-b.Position.X,2)+Math.Pow(t.Position.Z-b.Position.Z,2))<=(b.Kind==PlayableBuildingKind.Headquarters?profile.HeadquartersVisionRange:profile.OutpostVisionRange)).Select(b=>b.Id).ToArray()}).ToArray(),
                LivingOwnUnits=own.Select(u=>new{u.Id,Kind=u.Kind.ToString(),u.Health,u.Position,u.Held,u.Moving,ArmyId=liveRegistry.ArmyFor(u.Id),Order=u.CurrentOrder==null?null:new{Kind=u.CurrentOrder.Kind.ToString(),u.CurrentOrder.TargetId,u.CurrentOrder.IssuedTick}}).ToArray(),
                VisibleTarget83=o.Entities.Where(e=>e.Id==83).Select(e=>new{e.Id,e.Owner,e.Kind,e.Health,e.Position}).ToArray(),
                SchedulerRouteGates=new{lastDecisionTickAfterReview,decisionIntervalTicks,
                    EmergencyObserved=emergencyObserved,TechnologyFundingHasHold=hasTechnologyHold,EmergencyDefenseRoutesWouldOpen=(!emergencyObserved||hasTechnologyHold)&&threats.Length>0},
                RouteRequestCandidatesIfNormalCadenceRuns=routeRequestCandidates.Select(r=>new{r.UnitId,Kind=r.Kind.ToString(),r.TargetId,r.Generation,r.Tick,r.Origin,r.Radius,r.Target}).ToArray(),
                Routes=routeMatrix,ActualDetachedPlannerProposal=ActionRow(proposal),DetachedLiveRegistryUnchanged=JsonConvert.SerializeObject(liveRegistry.Capture())==beforeRegistry,
                DetachedProbeRegistryUnchanged=JsonConvert.SerializeObject(detached.Capture())==detachedRegistryBefore,
                LiveDefenseStateUnchanged=beforeState==Convert.ToBase64String(StateBytes(live)),DetachedProbeDefenseStateUnchanged=beforeState==Convert.ToBase64String(StateBytes(clone))
            });
        }
        private sealed class RunResult
        {public byte[][] Bytes;public string[] Decisions;public string[] TargetDefenseReceipts;public string[] ProjectionRows;public string[] PreselectionRows;public int DiagnosticRows;}
        private sealed class ProposalReceiptReadback
        {public JObject Row,Proposal;public long ActionId,DueTick;}
        [Test]
        public void Natural7108Ai5Tick8400_DefenseAdmissionReadbackAnd45TickHookNoninterference()
        {
            var root=Root();var archive=Path.Combine(root,"docs/native-ai/o6/o6-natural-topic-merge-source-compiled-raw.zip");
            Assert.AreEqual("5b7de68422093403ad1215bbd382c81ba5f771a3e643a2b0abd31159a29057d1",Wire.Hash(File.ReadAllBytes(archive)));
            var outDir=Path.Combine(root,".local/o6-route-query-20261012/output");Directory.CreateDirectory(outDir);
            byte[] input;using(var z=ZipFile.OpenRead(archive))using(var s=z.GetEntry(Input).Open())using(var m=new MemoryStream()){s.CopyTo(m);input=m.ToArray();}
            Assert.AreEqual(InputHash,Wire.Hash(input));File.WriteAllBytes(Path.Combine(outDir,"input-7108-ai5-8400.world"),input);
            File.WriteAllText(Path.Combine(outDir,"preselection.jsonl"),string.Empty);
            File.WriteAllText(Path.Combine(outDir,"hook-on-decisions.jsonl"),string.Empty);
            File.WriteAllText(Path.Combine(outDir,"hook-off-decisions.jsonl"),string.Empty);
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var participants=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,Seed,participants);
            Assert.AreEqual(500,profile.StartingCredits);Assert.AreEqual(8,config.Roster.Count);Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
            var runtimeAssembly=typeof(PlayableRuntime).Assembly;
            Assert.NotNull(runtimeAssembly.GetType("Spacewars.Runtime.PlayableRouteProjectionReadback",false),
                "Spacewars.Runtime must actually compile the UNITY_INCLUDE_TESTS route projection observer");
            var domainType=runtimeAssembly.GetType("Spacewars.Runtime.PlayableDomain",true);
            var routeObserverField=domainType.GetField("RouteProjectionReadbackForTests",F);
            Assert.NotNull(routeObserverField,"test symbol route observer field must be present in Spacewars.Runtime");
            var expectedRoutes=new HashSet<int>{38,52,60,89};
            RunResult Run(bool hook)
            {
                var authority=PlayableAuthorityTick.RestoreBytes(input,config);var bytes=new List<byte[]>();var decisions=new List<string>();var receipts=new List<string>();
                var projectionRows=new List<JObject>();
                var readbacks=new Dictionary<long,List<JObject>>();int diagnosticRows=0;
                long nextTickLastDecisionTick=0,nextTickDecisionIntervalTicks=0;bool nextTickArmyRouteGate=false;
                try
                {
                    Assert.AreEqual(StartTick,authority.Tick);CollectionAssert.AreEqual(input,authority.CaptureBytes(),"exact restore before ordinary C9 continuation");
                    Action<PlayableRouteProjectionReadback> routeObserver=r=>projectionRows.Add(ProjectionRow(r));
                    routeObserverField.SetValue(Get(authority,"domain"),hook?routeObserver:null);
                    PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=hook?Owner:null;
                    PlayableAiOwnerLoop.PreselectionReadbackForTests=hook?new Action<PlayableAiPreselectionReadback>(c=>
                    {
                        if(c.Observation.OwnerId!=Owner||c.Observation.Tick<=StartTick||c.Observation.Tick>EndTick)return;
                        var scheduler=Get(authority,"scheduler");var owner=((IEnumerable)Get(scheduler,"owners")).Cast<object>().Single(x=>(string)Get(x,"OwnerId")==Owner);
                        var checkpoint=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==Owner);
                        var before=authority.CaptureBytes();var row=ProposalReadback(owner,c.Observation,profile,(AiProfile)Get(owner,"nativeProfile"),checkpoint);
                        CollectionAssert.AreEqual(before,authority.CaptureBytes(),"detached actual Propose is read-only over live C9");
                        var pending=((IEnumerable)Get(owner,"pendingItems")).Cast<object>().Select(x=>new{Policy=Get(x,"PolicyName"),DueTick=Get(x,"DueTick"),Action=ActionRow((PlayableAiAction)Get(x,"Action"))}).ToArray();
                        row["PendingItems"]=JToken.FromObject(pending);row["ThreatenedByActualThreats"]=c.Threatened;
                        row["ActualPreReviewRouteGate"]=JToken.FromObject(new{Tick=c.Observation.Tick,NeedsArmyRoutes=nextTickArmyRouteGate,
                            lastDecisionTick=nextTickLastDecisionTick,decisionIntervalTicks=nextTickDecisionIntervalTicks});
                        row["SuppliedExpansionObservationState"]=JToken.FromObject(new{c.Observation.OwnerId,c.Observation.Generation,c.Observation.Tick,c.Observation.SnapshotSequence});
                        if(!readbacks.TryGetValue(c.Observation.Tick,out var atTick))readbacks[c.Observation.Tick]=atTick=new List<JObject>();atTick.Add(row);diagnosticRows++;
                    }):null;
                    using(var host=new UnityHostRouteService())for(int i=0;i<45;i++)
                    {
                        var scheduler=(AiAuthorityScheduler)Get(authority,"scheduler");var routeOwner=((IEnumerable)Get(scheduler,"owners")).Cast<object>().Single(x=>(string)Get(x,"OwnerId")==Owner);
                        long nextTick=authority.Tick+1;nextTickLastDecisionTick=(long)Get(routeOwner,"lastDecisionTick");nextTickDecisionIntervalTicks=(long)Get(routeOwner,"DecisionIntervalTicks");
                        nextTickArmyRouteGate=(bool)Call(routeOwner,"NeedsArmyRoutes",nextTick);
                        Advance(authority,host);Assert.AreEqual(StartTick+i+1,authority.Tick);var world=authority.CaptureBytes();bytes.Add(world);
                        File.WriteAllBytes(Path.Combine(outDir,(hook?"hook-on":"hook-off")+"-tick-"+authority.Tick+".world"),world);
                        var cp=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==Owner);
                        var economy=cp.EconomyDecision==null?null:new{cp.EconomyDecision.Tick,cp.EconomyDecision.Ordinal,cp.EconomyDecision.Identity,Selected=cp.EconomyDecision.Candidates.Where(x=>x.Selected).Select(x=>new{x.Policy,x.Kind,x.EntityIds,x.SiteId,x.SlotId,x.Reason}).ToArray(),Blockers=cp.EconomyDecision.Blockers.ToArray()};
                        var pendingAfterReview=((IEnumerable)Get(routeOwner,"pendingItems")).Cast<object>().Select(x=>new{Policy=Get(x,"PolicyName"),DueTick=Get(x,"DueTick"),Action=ActionRow((PlayableAiAction)Get(x,"Action"))}).ToArray();
                        var actual=new{authority.Tick,cp.DecisionTick,LastDecisionTick=(long)Get(routeOwner,"lastDecisionTick"),DecisionIntervalTicks=(long)Get(routeOwner,"DecisionIntervalTicks"),cp.LastPolicy,LastActionKind=cp.LastActionKind?.ToString(),cp.PendingActionId,Economy=economy,
                            Units=authority.ParticipantView(Owner).Entities.Select(e=>new{e.Id,e.Position,e.Moving,
                                Order=e.CurrentOrder==null?null:new{Kind=e.CurrentOrder.Kind.ToString(),e.CurrentOrder.TargetId,e.CurrentOrder.Generation,e.CurrentOrder.CommandSequence,e.CurrentOrder.IssuedTick},
                                OrderStamp=e.OrderStamp==null?null:new{e.OrderStamp.ActionId,e.OrderStamp.Generation,e.OrderStamp.Sequence,e.OrderStamp.Tick,Owner=e.OrderStamp.Owner.ToString(),Origin=e.OrderStamp.Origin.ToString(),Kind=e.OrderStamp.Kind.ToString(),e.OrderStamp.Source}}).ToArray(),
                            PendingItemsAfterReview=pendingAfterReview,
                            DefenseReceipts=cp.Records.Where(r=>r.Policy==AiDefensePlanner.Policy).Select(r=>new{r.Policy,Kind=r.Kind?.ToString(),r.ActionId,r.DueTick,r.CommandSequence,r.ApplicationTick,Status=r.Status.ToString(),RuntimeStatus=r.RuntimeStatus?.ToString(),r.Message}).ToArray()};
                        decisions.Add(JsonConvert.SerializeObject(actual));receipts.Add(JsonConvert.SerializeObject(actual.DefenseReceipts));
                        if(readbacks.TryGetValue(authority.Tick,out var rows))foreach(var row in rows)row["ActualPostselection"] = JToken.FromObject(actual);
                        File.AppendAllText(Path.Combine(outDir,(hook?"hook-on":"hook-off")+"-decisions.jsonl"),JsonConvert.SerializeObject(actual)+Environment.NewLine);
                    }
                    if(hook)File.WriteAllLines(Path.Combine(outDir,"preselection.jsonl"),readbacks.OrderBy(x=>x.Key).SelectMany(x=>x.Value).Select(x=>x.ToString(Formatting.None)));
                    if(hook)File.WriteAllLines(Path.Combine(outDir,"route-projection.jsonl"),projectionRows.Select(x=>x.ToString(Formatting.None)));
                    return new RunResult{Bytes=bytes.ToArray(),Decisions=decisions.ToArray(),TargetDefenseReceipts=receipts.ToArray(),ProjectionRows=projectionRows.Select(x=>x.ToString(Formatting.None)).ToArray(),
                        PreselectionRows=readbacks.OrderBy(x=>x.Key).SelectMany(x=>x.Value).Select(x=>x.ToString(Formatting.None)).ToArray(),DiagnosticRows=diagnosticRows};
                }
                finally
                {
                    routeObserverField.SetValue(Get(authority,"domain"),null);
                    PlayableAiOwnerLoop.PreselectionReadbackForTests=null;PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=null;
                    authority.Stop();
                }
            }
            File.WriteAllText(Path.Combine(outDir,"preselection.jsonl"),string.Empty);
            var off=Run(false);var on=Run(true);
            Assert.AreEqual(45,off.Bytes.Length);Assert.AreEqual(45,on.Bytes.Length);Assert.AreEqual(45,off.Decisions.Length);Assert.AreEqual(45,on.Decisions.Length);
            for(int i=0;i<45;i++)CollectionAssert.AreEqual(off.Bytes[i],on.Bytes[i],"complete C9 bytes hook noninterference at ordinary tick "+(StartTick+i+1));
            var baselinePath=Path.Combine(root,BaselineArchive);
            Assert.AreEqual(BaselineArchiveHash,Wire.Hash(File.ReadAllBytes(baselinePath)),"preserve the immutable original 45-tick C9 baseline archive");
            CollectionAssert.AreEqual(off.Decisions,on.Decisions,"ordinary postselection decision rows with hook disabled/enabled");
            CollectionAssert.AreEqual(off.TargetDefenseReceipts,on.TargetDefenseReceipts,"actual defense receipts with hook disabled/enabled");
            Assert.IsEmpty(off.ProjectionRows,"route observer disabled in hook-off run");
            Assert.Greater(on.DiagnosticRows,0,"actual target owner preselection occurred in the bounded 45-tick interval");
            Assert.AreEqual(4,on.ProjectionRows.Length,"one actual ProjectRoutes receipt for each of the four target-83 army members");
            var projected=on.ProjectionRows.Select(s=>JsonConvert.DeserializeObject<JObject>(s)).ToArray();
            CollectionAssert.AreEquivalent(expectedRoutes,projected.Select(x=>(int)x["UnitId"]),"actual ProjectRoutes unit ids");
            Assert.IsTrue(projected.All(x=>(string)x["OwnerId"]==Owner&&(long)x["Tick"]==8424&&(long)x["Generation"]==71&&
                (string)x["Kind"]==PlayableRouteTargetKind.VisibleEnemy.ToString()&&(int)x["TargetId"]==83),"exact live query binding");
            Assert.IsTrue(projected.All(x=>(string)x["Outcome"]=="ProofPublished"),"valid current hostile target bindings publish paths that may cross fog");
            Assert.IsTrue(projected.All(x=>(bool?)x["TargetVisible"]==true),"hostile semantic target identity still requires current visibility");
            Assert.IsTrue(projected.All(x=>((JArray)x["Candidates"]).Any(c=>(bool?)c["VisibleSegment"]==false)),"published full paths include a segment that traverses fog");
            var proposalReceipt=on.PreselectionRows.Select(s=>JsonConvert.DeserializeObject<JObject>(s)).SelectMany(row=>
            {
                var proposal=row["ActualDetachedPlannerProposal"] as JObject;var post=row["ActualPostselection"] as JObject;
                if(proposal==null||post==null)return Array.Empty<ProposalReceiptReadback>();
                var actors=(JArray)proposal["Actors"];
                var pending=(post["PendingItemsAfterReview"] as JArray)?.FirstOrDefault(x=>(string)x["Policy"]==AiDefensePlanner.Policy&&x["Action"] is JObject a&&
                    JToken.DeepEquals(a["Kind"],proposal["Kind"])&&JToken.DeepEquals(a["TargetId"],proposal["TargetId"])&&
                    JToken.DeepEquals(a["Actors"],actors)&&JToken.DeepEquals(a["Target"],proposal["Target"]));
                var actualAction=pending?["Action"] as JObject;if(actualAction==null)return Array.Empty<ProposalReceiptReadback>();
                long id=(long)actualAction["ActionId"];
                var records=post["DefenseReceipts"] as JArray;
                var match=records?.FirstOrDefault(r=>(long?)r["ActionId"]==id&&(string)r["Status"]=="Scheduled");
                return match==null?Array.Empty<ProposalReceiptReadback>():new[]{new ProposalReceiptReadback{Row=row,Proposal=actualAction,ActionId=id,DueTick=(long)match["DueTick"]}};
            }).FirstOrDefault();
            Assert.NotNull(proposalReceipt,"the ordinary owner loop must schedule the actual detached defense proposal");
            Assert.NotNull(proposalReceipt.Row,"the actual scheduled defense proposal must retain its source observation");
            long actionId=proposalReceipt.ActionId,dueTick=proposalReceipt.DueTick;
            long decisionIntervalTicks=(long)proposalReceipt.Row["ActualPreReviewRouteGate"]["decisionIntervalTicks"];
            long continuationLimitTick=dueTick+decisionIntervalTicks;
            int[] actors=((JArray)proposalReceipt.Proposal["Actors"]).Select(x=>(int)x).ToArray();
            Assert.IsNotEmpty(actors,"the scheduled defense action must bind actual bodies");
            var actualTickRows=on.Decisions.Select(s=>JsonConvert.DeserializeObject<JObject>(s)).ToList();
            bool HasAppliedMovement(IEnumerable<JObject> rows)
            {
                var ticks=rows.ToDictionary(t=>(long)t["Tick"]);
                foreach(var tick in ticks.Values)
                {
                    var applied=(tick["DefenseReceipts"] as JArray)?.FirstOrDefault(r=>(long?)r["ActionId"]==actionId&&
                        (string)r["Status"]=="Applied"&&(string)r["RuntimeStatus"]=="Applied");
                    if(applied==null)continue;
                    long applicationTick=(long)applied["ApplicationTick"];
                    if(!ticks.TryGetValue(applicationTick,out var baseline))continue;
                    foreach(long observedTick in ticks.Keys.Where(t=>t>applicationTick&&t<=continuationLimitTick).OrderBy(t=>t))
                    {
                        var after=ticks[observedTick];
                        if(after["Units"] is not JArray newUnits||baseline["Units"] is not JArray baselineUnits)continue;
                        if(actors.Any(id=>
                        {
                            var old=baselineUnits.FirstOrDefault(u=>(int)u["Id"]==id);var current=newUnits.FirstOrDefault(u=>(int)u["Id"]==id);
                            if(old==null||current==null)return false;
                            var stamp=current["OrderStamp"] as JObject;var order=current["Order"] as JObject;
                            if(stamp==null||order==null||(long)stamp["ActionId"]!=actionId||(long)stamp["Generation"]!=71||
                                (long)stamp["Sequence"]!=(long)applied["CommandSequence"]||(string)stamp["Owner"]!="Sixth"||
                                (long)stamp["Tick"]!=applicationTick||(string)stamp["Origin"]!="Ai"||(string)stamp["Kind"]!="Attack"||
                                (string)stamp["Source"]!=PlayableAiOpeningComposition.SourceIdentity||
                                (long)order["Generation"]!=71||(int)order["TargetId"]!=83||(long)order["IssuedTick"]!=applicationTick)return false;
                            var a=old["Position"].ToObject<NavPoint>();var b=current["Position"].ToObject<NavPoint>();return a.X!=b.X||a.Z!=b.Z;
                        }))return true;
                    }
                }
                return false;
            }
            JObject continuationResult=null;
            if(!HasAppliedMovement(actualTickRows))
            {
                var lastBytes=on.Bytes[on.Bytes.Length-1];var continuation=PlayableAuthorityTick.RestoreBytes(lastBytes,config);
                CollectionAssert.AreEqual(lastBytes,continuation.CaptureBytes(),"exact restore at the end of the protected 45-tick run");
                try
                {
                    using(var host=new UnityHostRouteService())
                    {
                        while(continuation.Tick<continuationLimitTick&&!HasAppliedMovement(actualTickRows))
                        {
                            Advance(continuation,host);
                            var cp=continuation.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==Owner);
                            var current=continuation.ParticipantView(Owner);
                            var row=new JObject
                            {
                                ["Tick"]=continuation.Tick,
                                ["Units"]=JArray.FromObject(current.Entities.Select(e=>new{e.Id,e.Position,e.Moving,
                                    Order=e.CurrentOrder==null?null:new{Kind=e.CurrentOrder.Kind.ToString(),e.CurrentOrder.TargetId,e.CurrentOrder.Generation,e.CurrentOrder.CommandSequence,e.CurrentOrder.IssuedTick},
                                    OrderStamp=e.OrderStamp==null?null:new{e.OrderStamp.ActionId,e.OrderStamp.Generation,e.OrderStamp.Sequence,e.OrderStamp.Tick,Owner=e.OrderStamp.Owner.ToString(),Origin=e.OrderStamp.Origin.ToString(),Kind=e.OrderStamp.Kind.ToString(),e.OrderStamp.Source}})),
                                ["DefenseReceipts"]=JArray.FromObject(cp.Records.Where(r=>r.Policy==AiDefensePlanner.Policy).Select(r=>new{r.Policy,Kind=r.Kind?.ToString(),r.ActionId,r.DueTick,r.CommandSequence,r.ApplicationTick,Status=r.Status.ToString(),RuntimeStatus=r.RuntimeStatus?.ToString(),r.Message}))
                            };
                            actualTickRows.Add(row);continuationResult=row;
                        }
                    }
                    Assert.True(HasAppliedMovement(actualTickRows),"the exact ordinary continuation through the existing decision interval must move an assigned body after the Applied receipt while that action's current generation/target order stamp remains active");
                    File.WriteAllLines(Path.Combine(outDir,"applied-movement-continuation.jsonl"),actualTickRows.Skip(on.Decisions.Length).Select(x=>x.ToString(Formatting.None)));
                    File.WriteAllBytes(Path.Combine(outDir,"applied-movement-final.world"),continuation.CaptureBytes());
                }
                finally{continuation.Stop();}
            }
            Assert.True(HasAppliedMovement(actualTickRows),"the real detached defense proposal must receive RuntimeStatus.Applied and move an assigned body afterward under the current target-83 order stamp");
            TestContext.WriteLine("O6_DEFENSE_ADMISSION "+JsonConvert.SerializeObject(new{Input,InputHash,Seed,Owner,StartTick,EndTick,Ticks=45,DiagnosticRows=on.DiagnosticRows,
                RouteProjectionRows=on.ProjectionRows.Length,RouteProjection=projected.Select(x=>new{UnitId=(int)x["UnitId"],Outcome=(string)x["Outcome"],BindingRejection=(string)x["BindingRejection"],
                    OriginVisible=x["OriginVisible"],TargetVisible=x["TargetVisible"],CandidateCount=x["CandidateCount"],Candidates=x["Candidates"]}).ToArray(),
                DefenseActionId=actionId,DefenseActionKind=(string)proposalReceipt.Proposal["Kind"],Actors=actors,DueTick=dueTick,
                AppliedMovementRows=actualTickRows.Where(t=>(t["DefenseReceipts"] as JArray)?.Any(r=>(long?)r["ActionId"]==actionId&&(string)r["Status"]=="Applied"&&(string)r["RuntimeStatus"]=="Applied")==true)
                    .Select(t=>new{Tick=(long)t["Tick"],Receipt=(t["DefenseReceipts"] as JArray).First(r=>(long?)r["ActionId"]==actionId),Units=t["Units"]}).ToArray(),
                ContinuationThroughTick=continuationResult?["Tick"],AllWorldBytesEqual=true,AllDecisionsEqual=true,AllDefenseReceiptsEqual=true,Output=outDir}));
        }
    }
}
