using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiArmyTests
    {
        private static object PendingFields(object value,int depth=1)
        {
            if(value==null)return null;var t=value.GetType();if(t.IsPrimitive||t.IsEnum||value is string||value is decimal)return value;
            if(value is byte[] bytes)return new{type=t.FullName,length=bytes.Length};
            if(depth==0)return new{type=t.FullName};
            var result=new Dictionary<string,object>{{"type",t.FullName}};
            foreach(var f in t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Take(40))
            {var v=f.GetValue(value);if(v is System.Collections.ICollection array)result[f.Name]=new{type=v.GetType().FullName,count=array.Count};else result[f.Name]=PendingFields(v,depth-1);}
            return result;
        }
        [Test,Timeout(60000)] public void PositionRepair_ColdPendingCaptureOperandsWithoutStepsOrQueries()
        {
            var path=Environment.GetEnvironmentVariable("AI_REPAIR_CAPTURE");var input=File.ReadAllBytes(path);string Hash(byte[] b){using var h=System.Security.Cryptography.SHA256.Create();return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
            Assert.AreEqual(Environment.GetEnvironmentVariable("AI_REPAIR_CAPTURE_SHA256"),Hash(input));var config=PendingReadbackConfiguration();var ai=AiProfile.Initial;
            PlayableRuntime runtime=null;PlayableAuthorityTick authority=null;Exception failure=null;const BindingFlags f=BindingFlags.NonPublic|BindingFlags.Instance;
            try{
                runtime=PlayableRuntime.RestoreBytesForTests(input,config,ai,r=>{try{
                    authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",f).GetValue(r);var nav=authority.Domain.Navigation;
                    var root=(NavigationHostRoot)typeof(NavigationSession).GetField("committedHostRoot",f).GetValue(nav);
                    using var world=PlayableWorldState.Decode(input);using var state=WorldWire.Unpack(world.Navigation,WorldWire.ReadNavigationSessionState);
                    var attempts=(System.Collections.IEnumerable)typeof(NavigationSession).GetField("arrivalAttempts",f).GetValue(nav);var rows=new List<object>();
                    foreach(var pair in attempts){
                        var a=pair.GetType().GetProperty("Value").GetValue(pair);object Field(string name)=>a.GetType().GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(a);
                        var work=(NavigationArrivalWork)Field("Work");var arrival=(NavArrivalInput)Field("Input");var binding=arrival.WorkingFor(arrival.Members[0]);
                        object WorkField(string name)=>work.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(work);
                        rows.Add(new{header=PendingFields(a,1),intents=((NavigationRequest[])Field("Intents")).Select(q=>new{q.Entity,q.Request,q.Order,q.MemberIdentity,q.Start,q.Goal}).ToArray(),
                            arrival.IndependentMembers,members=arrival.Members.Count,candidates=arrival.Source.Candidates.Count,primaryProviderType=arrival.Provider.GetType().FullName,workingProviderType=binding.Provider.GetType().FullName,
                            inputProfile=PendingFields(arrival.Profile,2),workingProfile=PendingFields(binding.Profile,2),work.LogicalEpoch,work.MemberCursor,work.TerminalPageCount,work.CandidateCursor,work.OccupancyCursor,work.WorkPhase,work.AllocationQuotaRemaining,work.AllocationWorkConsumed,work.CandidateWorkConsumed,work.OccupancyWorkConsumed,work.DirectWorkConsumed,work.RouteCounters,
                            pageStarts=WorkField("pageStarts"),pageCounts=WorkField("pageCounts"),preparationCursor=WorkField("preparationCursor"),preparationPageIndex=WorkField("preparationPageIndex")});
                    }
                    bool exact=input.SequenceEqual(r.CaptureBeforeStartForTests());Assert.True(exact);
                    var result=new{producerCaptureSHA=Hash(input),world.Tick,world.Generation,exact,steps=0,queries=0,state.SurfaceProviderId,state.SurfaceSemanticsVersion,state.TransportVersion,state.RequestSequence,
                        state.PendingRequestIndices,state.AllocationRequestIndices,state.RegisteredRequestIndices,state.DeliveredRequestIndices,state.RequestMailboxIndices,state.Requests,state.TechnicalFailures,
                        state.Groups,state.InstalledExecutions,state.AppliedResults,state.RejectedResults,
                        host=root==null?null:new{root.Epoch,root.Progress,operation=PendingFields(root.Operation,2),frontier=PendingFields(root.Frontier,2),root.QuantumTick,root.QuantumSerial,root.ConsumedQuantum,root.QuantumBudget,root.QuantumCompleted,root.AdmissionPrefix,
                            requests=root.Requests.Select(x=>new{key=x.Key.Value,request=((NavigationHostRequest)x.Value).Request}).ToArray(),
                            registered=root.Rows.Select(x=>{var row=(NavigationHostRow)x.Value;return new{key=x.Key.Value,row.Request,row.ProviderIdentity,row.ArrivalToken,provider=PendingFields(row.Provider,2),scheduled=PendingFields(row.Scheduled,2),solved=PendingFields(row.Solved,2),projection=PendingFields(row.ProjectionOwner,2),answer=PendingFields(row.Answer,1)};}).ToArray(),
                            scheduler=PendingFields(root.Scheduler,3)},arrivals=rows};
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),"pending-readback-detail.json"),Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
                }catch(Exception e){failure=e;}finally{r.RequestStop();}});
                Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));if(failure!=null)Assert.Fail(failure.ToString());
            }finally{if(runtime!=null){runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));runtime.Dispose();}if(authority!=null){authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();}}
        }
        private static OfflineMatchConfiguration PendingReadbackConfiguration()
        {
            var basis=A4NativeConfig();var p=basis.Profile;var ai=AiProfile.Initial;
            var added=new[]{new TerritorySlot(3,new NavPoint(-24,9),0),new TerritorySlot(4,new NavPoint(-14,9),0),new TerritorySlot(5,new NavPoint(-14,-9),0)};
            var sites=basis.Sites.Select(s=>s.Id==1?new TerritorySite(s.Id,s.Kind,s.Position,s.Slots.Concat(added).ToArray()):s).ToArray();
            var victimAt=new NavPoint(1,-8);double repairRange=AiPositionDevelopmentPlanner.Range(p,PlayableBuildingKind.RepairTower);
            var pad=sites.Single(s=>s.Id==1).DefenseSlots(p).Where(s=>AiPositionDevelopmentPlanner.Distance(s.Position,victimAt)>repairRange+PlayableUnitRules.Radius(p,PlayableEntityKind.Tank))
                .OrderBy(s=>AiPositionDevelopmentPlanner.Distance(s.Position,victimAt)).ThenBy(s=>s.Id).First();
            var buildings=basis.ScenarioBuildings.Concat(new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.ScientificCenter,1,3,added[0].Position),
                new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,1,4,added[1].Position),new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,1,5,added[2].Position),
                new OfflineScenarioBuilding(1,PlayableBuildingKind.RepairTower,1,pad.Id,pad.Position,pad.Heading)}).ToArray();
            var units=basis.ScenarioUnits.Concat(new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-1,-8)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,victimAt),
                new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(1,-2))}).ToArray();
            return new OfflineMatchConfiguration(p,"ai-position-repair-runtime-v1",basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),sites,basis.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},
                spectators:new[]{PlayableRuntime.LocalSpectatorId},scenario:units,scenarioBuildings:buildings);
        }

    }
}
