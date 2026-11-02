using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiArmyTests
    {
        private static Tuple<OfflineMatchConfiguration,AiProfile> PositionSavedSentryConfiguration()
        {
            const int seed=7;const long generation=107;
            var p=PlayableProfile.Default;var ai=NativeBalanceFields.InitialAiOpeningProfile();
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,-15),new NavPoint(-20,-27),pin:1),new OfflineStart("east",2,new NavPoint(20,15),new NavPoint(20,27),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);
            var enemy=new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p);
            var front=new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(-20,15),p);
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter};
            Assert.GreaterOrEqual(home.Slots.Count,kinds.Length);
            var buildings=kinds.Select((kind,i)=>new OfflineScenarioBuilding(1,kind,1,home.Slots[i].Id,home.Slots[i].Position,home.Slots[i].Heading)).Concat(new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Outpost,3,0,front.Position)}).ToArray();
            var units=Enumerable.Range(0,5).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-25+i*3,-1))).Concat(new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(20,-24))}).ToArray();
            var config=new OfflineMatchConfiguration(p,"ai-position-development-first-witness-v1","flat-public-position-v1","UnityHostRouteService",seed,new[]{new OfflineParticipant("position-owner",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},starts,new[]{home,enemy,front},PlayableMap.StaticObstacles(p),new double[,]{{0,100},{100,0}},scenario:units,scenarioBuildings:buildings,spectators:new[]{PlayableRuntime.LocalSpectatorId});

            return Tuple.Create(config,ai);
        }
        private static Tuple<OfflineMatchConfiguration,AiProfile> PositionSavedRepairConfiguration()
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
            var config=new OfflineMatchConfiguration(p,"ai-position-repair-runtime-v1",basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),sites,basis.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},
                spectators:new[]{PlayableRuntime.LocalSpectatorId},scenario:units,scenarioBuildings:buildings);

            return Tuple.Create(config,ai);
        }
        private static object PendingField(object value,string name)
        {
            if(value==null)return null;for(var t=value.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(f!=null)return f.GetValue(value);var p=t.GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(p!=null)return p.GetValue(value);}return null;
        }
        private static IEnumerable<object> PendingValues(object value)
        {if(value is IEnumerable rows)foreach(var row in rows)yield return PendingField(row,"Value")??row;}
        private static object PendingScalars(object value,params string[] names)
        {if(value==null)return null;var row=new Dictionary<string,object>();foreach(var n in names){var x=PendingField(value,n);row[n]=x is Enum?x.ToString():x;}return row;}
        private static object PendingRequestRow(NavigationRequest q)=>q==null?null:(object)new{q.Request,q.Entity,q.Order,q.Start,q.Goal,q.ProfileId,q.ProfileRevision,q.HeldIdentity};
        private static object PendingArrivalScheduler(object scheduler)
        {
            if(scheduler==null)return null;
            var rows=PendingValues(PendingField(scheduler,"jobs")).ToArray();Assert.LessOrEqual(rows.Length,LayeredNavigationScheduler.MaxQueue);
            return new{state=PendingScalars(scheduler,"PendingCount","ReadyCount","cachedGraphWork","peakQueue","epoch"),jobs=rows.Select(j=>new{
                state=PendingScalars(j,"Phase","GraphWork","FieldWork","GraphDebt","FieldDebt","OriginCursor","OriginComplete"),
                compiler=PendingScalars(PendingField(j,"Compiler"),"Phase","Consumed"),
                completedCompiler=PendingScalars(PendingField(j,"CompletedCompiler"),"Phase","Consumed"),
                singleton=PendingScalars(PendingField(j,"Singleton"),"Status","phase","consumed","edgeCursor","nodeCursor"),
                field=PendingScalars(PendingField(j,"Field"),"Status","phase","consumed"),
                request=PendingScalars(PendingField(PendingField(j,"Request"),"Subscription"),"EntityId","RequestSequence","GroupId","CommandSequence")}).ToArray()};
        }
        private static object PendingNavigationBoundary(NavigationSession nav,int[] actors)
        {
            var allocating=PendingValues(PendingField(nav,"allocating")).OfType<NavigationRequest>().Where(q=>actors.Contains(q.Entity)).ToArray();
            var pending=actors.Select(id=>nav.PendingRequestFor(id)).Where(q=>q!=null).ToArray();var requests=new HashSet<long>(pending.Concat(allocating).Select(q=>q.Request));
            var host=(NavigationHostRoot)PendingField(nav,"committedHostRoot");
            var attempts=PendingValues(PendingField(nav,"arrivalAttempts")).OfType<NavigationSession.ArrivalAttempt>().Where(a=>a.Intents.Any(q=>requests.Contains(q.Request))).Select(a=>new{
                a.Id,a.Group,a.Epoch,a.Published,a.Retiring,status=a.SnapshotStatus.ToString(),a.SnapshotPhase,a.SnapshotCandidate,a.SnapshotMember,a.Frontier,
                intents=a.Intents.Where(q=>requests.Contains(q.Request)).Select(PendingRequestRow).ToArray(),
                work=PendingScalars(a.OwnedWork,"Status","Phase","phase","candidate","member","occupancy","Consumed"),
                graph=PendingScalars(PendingField(a.OwnedWork,"graph"),"Phase","Consumed"),
                scheduler=PendingArrivalScheduler(PendingField(a.OwnedWork,"scheduler"))}).ToArray();
            var jobs=new List<object>();if(host!=null)foreach(var job in PendingValues(PendingField(host.Scheduler,"jobs")))
            {
                var request=PendingField(job,"Request") as NavScheduledRequest;var subscribers=PendingValues(PendingField(job,"Subscribers")).OfType<NavScheduledRequest>().ToArray();
                if((request==null||!requests.Contains(request.Subscription.RequestSequence))&&!subscribers.Any(q=>requests.Contains(q.Subscription.RequestSequence)))continue;
                jobs.Add(new{request=PendingScalars(request?.Subscription,"EntityId","RequestSequence","GroupId","CommandSequence"),subscribers=subscribers.Select(q=>PendingScalars(q.Subscription,"EntityId","RequestSequence","GroupId","CommandSequence")).ToArray(),
                    state=PendingScalars(job,"Phase","QueueNode","GraphDebt","FieldDebt","GraphWork","FieldWork","Failure","OriginCursor","OriginValidated","OriginComplete","HasIngress","ActiveConnectorKey"),
                    compiler=PendingScalars(PendingField(job,"Compiler"),"Phase","Consumed"),completedCompiler=PendingScalars(PendingField(job,"CompletedCompiler"),"Phase","Consumed"),
                    demand=PendingScalars(PendingField(job,"Demand"),"Status","phase"),graphPresent=PendingField(job,"Graph")!=null,fieldPresent=PendingField(job,"Field")!=null});
            }
            return new{nav.UsesRecastGroundRoutes,nav.TickQuantaEnabled,physical=PendingField(nav,"physicalArrivalEnabled"),actors,pending=pending.Select(PendingRequestRow).ToArray(),allocating=allocating.Select(PendingRequestRow).ToArray(),attempts,jobs,
                host=PendingScalars(host,"Epoch","Progress","AccruedCredits","TickQuantum","QuantumRunnable","QuantumTick","QuantumSerial","ConsumedQuantum","QuantumCompleted","QuantumBudget","AdmissionPrefix","LastServedArrival","ArrivalRoundEnd"),
                frontier=PendingScalars(host?.Frontier,"Stage","PhaseUsed"),operation=PendingScalars(host?.Operation,"Stage","Iteration","Entered","ProgressIncrementConsumed"),
                credits=PendingValues(PendingField(nav,"creditBank")).Take(24).ToArray(),
                boundary="Read committed restored scalar state only; no ticks/solver/Service/Pump/Ensure, no inference of original unsaved Working or CPU time."};
        }
        [Test,Timeout(60000)]
        public void PositionCurrentCapture_ReadsPendingNavigationBoundaryWithoutTicks()
        {
            const string scenario="sentry";
            var pair=scenario=="sentry"?PositionSavedSentryConfiguration():PositionSavedRepairConfiguration();var config=pair.Item1;var ai=pair.Item2;
            var prefix=scenario=="sentry"?"AI_SENTRY_CAPTURE":"AI_REPAIR_CAPTURE";var path=Environment.GetEnvironmentVariable(prefix);Assert.IsNotEmpty(path);
            var input=File.ReadAllBytes(path);var expected=Environment.GetEnvironmentVariable(prefix+"_SHA256");Assert.IsNotEmpty(expected);
            using(var sha=System.Security.Cryptography.SHA256.Create())Assert.AreEqual(expected,BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant());
            var ownerId=scenario=="sentry"?"position-owner":"player-1";const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            PlayableRuntime runtime=null;PlayableAuthorityTick authority=null;Exception error=null;
            try
            {
                runtime=PlayableRuntime.RestoreBytesForTests(input,config,ai,r=>
                {
                    try
                    {
                        authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(r);
                        var view=r.ParticipantView(ownerId);var cp=authority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId==ownerId);
                        var nav=authority.Domain.Navigation;var selected=view.Entities.Where(u=>u.Owner==view.Owner&&u.Queue?.Pending!=null).Select(u=>u.Id).ToArray();var facts=PendingNavigationBoundary(nav,selected);
                        var result=new{scenario,view.Tick,config.Seed,generation=r.Generation,ai.Id,ai.Hash,exact=input.SequenceEqual(r.CaptureBeforeStartForTests()),navigation=facts};
                        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),"pending-nav-readback.json"),Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));Assert.True(result.exact);
                    }
                    catch(Exception e){error=e;}finally{r.RequestStop();}
                });
                Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));if(error!=null)Assert.Fail(error.ToString());
            }
            finally{if(runtime!=null){runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));runtime.Dispose();}if(authority!=null){authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();}}
        }
    }
}
