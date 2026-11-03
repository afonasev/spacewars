using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Tests.PlayMode;
internal static class FormationOpposingData
{
    internal static void Run()
    {
        string output=Environment.GetEnvironmentVariable("RECAST_FORMATION_OUTPUT")??throw new InvalidOperationException("Explicit unique data output required.");
        if(Directory.Exists(output))throw new InvalidOperationException("Output exists; preserve prior observation.");Directory.CreateDirectory(output);
        var p=PlayableProfile.RecastGroupCorridorDefault;var map=(ThreeCrossingsMap)p.AuthoredMap;
        var config=RecastOpposingFormationInputs.Create(p,out var rejected,out var rosterTable);
        File.WriteAllText(Path.Combine(output,"roster-table.json"),Newtonsoft.Json.JsonConvert.SerializeObject(rosterTable,Newtonsoft.Json.Formatting.Indented));
        ulong native=RecastNative.sw_recast_allocation_count();long faults=RecastNative.ReleaseFaults;PlayableAuthorityTick world=null;var failures=new List<string>();
        void Check(bool ok,string why){if(!ok)failures.Add(why);}
        try{
            world=new PlayableAuthorityTick(config,RecastFormationInputs.Generation);var d=world.Domain;var n=d.Navigation;
            var full=d.Snapshot(0,RuntimeStatus.Starting,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed);
            var ids=new[]{PlayableOwner.Player,PlayableOwner.Enemy}.Select(owner=>full.Entities.Where(a=>a.Owner==owner&&a.Kind==PlayableEntityKind.Explorer).Select(a=>a.Id).OrderBy(x=>x).ToArray()).ToArray();
            var population=typeof(PlayableDomain).GetMethod("Population",BindingFlags.Instance|BindingFlags.NonPublic);
            var populations=new[]{PlayableOwner.Player,PlayableOwner.Enemy}.Select(owner=>(PlayablePopulationSnapshot)population.Invoke(d,new object[]{owner})).ToArray();
            var autos=config.Roster.Select((r,i)=>full.Entities.Single(a=>(int)a.Owner==i&&a.Position.Equals(config.Starts[config.Assignments[i]].ExplorerAnchor))).ToArray();
            Check(config.Assignments.Take(2).All(i=>config.Starts[i].Position.X>0),"Candidate allied owner1/owner2 homes are not both East.");
            Check(config.Starts[config.Assignments[2]].Position.X<0&&config.Starts[config.Assignments[2]].Position.Z<0,"Parked opponent home must be SouthWest.");
            Check(ids.All(x=>x.Length==50)&&full.Entities.Count==101&&n.Crowd.Units.Count==101,"Factory50+50+parked1 identity/count mismatch.");
            Check(populations.All(x=>x.Living==100&&x.Reserved==0&&x.Capacity==100),"Each moving owner must have normal population100/cap100.");
            Check(!world.Scheduler.Owners.Any()&&config.Roster.All(x=>x.Control==OfflineControl.Human)&&d.Outcome==PlayableMatchOutcome.Playing,"Human-only continuing normal roster mismatch.");
            var parked=autos[2];var geometry=d.Geometry;
            var enemyContacts=full.Entities.Where(a=>a.Id!=parked.Id&&!RecastOpposingFormationInputs.EnemySeparated(p,a.Position,parked.Position)).Select(a=>new{a.Id,a.Owner,a.Position,Distance=RecastFormationInputs.Distance(a.Position,parked.Position)}).ToArray();
            Check(enemyContacts.Length==0,"Initial automatic/scenario actors violate conservative declared enemy separation.");
            Check(full.Entities.All(a=>a.TargetId==0),"Initial automatic/scenario actor already has combat target.");
            var packing=RecastOpposingFormationInputs.Pack(p,config,geometry,n,ids,autos[1].Id,autos[0].Id,parked.Position);
            // All finite candidate witnesses survive any later failed assertion.
            File.WriteAllText(Path.Combine(output,"packing-trials.json"),Newtonsoft.Json.JsonConvert.SerializeObject(packing.Trials,Newtonsoft.Json.Formatting.Indented));
            Check(packing.Found,"No joint normal goal/preposition packing in frozen candidate domain.");
            var preposition=packing.Preposition;
            var preSlots=packing.Found?n.AllocateArrivalSlots(preposition,new[]{autos[1].Id}):Array.Empty<NavPoint>();
            Check(packing.Found&&preSlots.Length==1&&preSlots[0].Equals(preposition),"Normal preposition single-slot allocator changed target.");
            var eastPreposition=packing.EastPreposition;
            var eastPreSlots=packing.Found?n.AllocateArrivalSlots(eastPreposition,new[]{autos[0].Id}):Array.Empty<NavPoint>();
            Check(packing.Found&&eastPreSlots.Length==1&&eastPreSlots[0].Equals(eastPreposition),"Normal East preposition single-slot allocator changed target.");
            var goalRows=new[]{new{Group=0,Ids=ids[0],Goal=packing.WestGoal,Slots=packing.WestSlots},new{Group=1,Ids=ids[1],Goal=packing.EastGoal,Slots=packing.EastSlots}};
            var analyticalPostPrephase=new List<object>();
            if(packing.Found)for(int group=0;group<2;group++){
                var members=new HashSet<int>(ids[group]);
                var occupied=n.Crowd.Units.Where(a=>!members.Contains(a.Id)).Select(a=>a.Id==autos[1].Id?preposition:a.Id==autos[0].Id?eastPreposition:a.Position).ToArray();
                var navProfile=p.Navigation.ForUnit(p.ExplorerCollisionRadius,p.Navigation.TankSpeed,p.Navigation.TankTurnSpeed);
                var expected=group==0?packing.WestSlots:packing.EastSlots;var center=group==0?packing.WestGoal:packing.EastGoal;
                var predicted=NavArrivalAllocator.Allocate(geometry,navProfile,center,50,occupied);
                bool equal=predicted.SequenceEqual(expected);Check(equal,"Analytical proposed post-prephase normal allocation differs for group "+group);
                analyticalPostPrephase.Add(new{Group=group,WestPointAssumption=preposition,WestActor=autos[1].Id,EastPointAssumption=eastPreposition,EastActor=autos[0].Id,ActualPoseChanged=false,ExactSlots=equal,Slots=predicted,Meaning="Pure normal allocator with explicitly proposed future occupancy only; actual ordinary must recheck after natural prephase"});
            }
            Check(n.Crowd.Units.All(a=>map.IsValidLocation(a.Location,a.Radius)&&geometry.IsFree(a.Position,a.Radius)),"Initial typed body footprint invalid.");
            var bodies=n.Crowd.Units.ToArray();for(int i=0;i<bodies.Length;i++)for(int j=0;j<i;j++)Check(RecastFormationInputs.Distance(bodies[i].Position,bodies[j].Position)>=bodies[i].Radius+bodies[j].Radius-1e-9,"Initial pair overlap "+bodies[i].Id+"/"+bodies[j].Id);
            File.WriteAllText(Path.Combine(output,"data.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Result=failures.Count==0?"PASS":"FAIL",Candidate=Environment.GetEnvironmentVariable("RECAST_FORMATION_CANDIDATE"),config.Seed,RecastFormationInputs.Generation,Tick=world.Tick,Steps=0,MovementCommands=0,WorkerCreated=false,NativeLongQueries=0,config.Roster,config.Assignments,config.Starts,config.SourceIdentity,config.MapIdentity,config.RouteProvenance,Scenario=config.ScenarioUnits,RejectedCandidates=rejected,RosterTable=rosterTable,CombatGuard=RecastOpposingFormationInputs.CombatGuard,ExplorerWeaponRange=PlayableUnitRules.Range(p,PlayableEntityKind.Explorer,false),EnemyContacts=enemyContacts,PrepositionSlots=preSlots,EastPrepositionSlots=eastPreSlots,AllocatorSpacing=packing.Spacing,PrepositionSeparation=packing.PrepositionSeparation,PackingFound=packing.Found,AnalyticalPostPrephase=analyticalPostPrephase,PrephaseStability="Analytical envelope only: actual future arrival within profile.ArrivalTolerance preserves spacing to all initial other actors/accepted goal slots. No pose mutation. Ordinary must recheck actual prephase arrival/current allocations.",FullDomain=full.Entities.Count,FullNav=bodies.Length,Viewer=world.Latest.Entities.Count,Populations=populations,Automatic=autos,InitialBodies=bodies.Select(a=>new{a.Id,a.Position,a.Location,a.Radius,a.Speed}),Preposition=new{Actor=autos[1].Id,Target=preposition,Meaning="Proposed future normal Move; no movement performed"},EastPreposition=new{Actor=autos[0].Id,Target=eastPreposition,Meaning="Proposed second genuine normal Move; no movement performed"},Goals=goalRows,NorthBridge=map.Supports.Single(s=>s.SurfaceId=="bridge/north"),Failures=failures},Newtonsoft.Json.Formatting.Indented));
        }finally{
            if(world!=null){world.Stop();world.Domain.Navigation.ReleaseCommittedHostRoot();world.Domain.DestroyOwnedNavigation();var b=world.Domain.Navigation.PhysicalHostBudget;File.WriteAllText(Path.Combine(output,"cleanup.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{b.PhysicalBytes,b.HeldBytes,b.ActiveScopes,NativeBefore=native,NativeAfter=RecastNative.sw_recast_allocation_count(),FaultsBefore=faults,FaultsAfter=RecastNative.ReleaseFaults}));if(b.PhysicalBytes!=0||b.HeldBytes!=0||b.ActiveScopes!=0)throw new InvalidOperationException("Data preflight retained owned navigation.");}
            if(native!=RecastNative.sw_recast_allocation_count()||faults!=RecastNative.ReleaseFaults)throw new InvalidOperationException("Data preflight native census changed.");
        }
        if(failures.Count>0)throw new InvalidOperationException(string.Join("\n",failures));
        Console.WriteLine("PASS opposing data only: "+output);
    }
}
