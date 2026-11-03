using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.PlayMode
{
    // Phase B only. Phase A inputs and its frozen recipe remain independent.
    internal static class RecastOpposingFormationInputs
    {
        // Retained data1 guard, now named explicitly: two Explorer weapon ranges
        // plus one body diameter. This is a conservative fixture exclusion,
        // independent of visibility/fire-clear; it is not the gameplay fire law.
        internal const string CombatGuard="fixture-source-range-plus-one-range-and-body-diameter-v1: distance > 2*ExplorerWeaponRange+2*ExplorerRadius; actual DefaultDefenseTarget additionally requires visible/fireclear and distance<=weaponRange";
        internal static bool EnemySeparated(PlayableProfile p,NavPoint point,NavPoint enemy)
            =>RecastFormationInputs.Distance(point,enemy)>2*PlayableUnitRules.Range(p,PlayableEntityKind.Explorer,false)+2*p.ExplorerCollisionRadius;
        internal static OfflineMatchConfiguration Create(PlayableProfile p,out string[] rejected,out object[] rosterTable,int scenarioCount=49)
        {
            var map=(ThreeCrossingsMap)p.AuthoredMap;
            // Frozen source-qualified geography from data2; no further roster search.
            var genesis=map.Configuration(p,RecastFormationInputs.Seed,new[]{
                new OfflineParticipant("player-1",1,1,OfflineControl.Human),
                new OfflineParticipant("player-2",3,1,OfflineControl.Human),
                new OfflineParticipant("player-3",4,2,OfflineControl.Human)});
            rosterTable=new object[]{new{LogicalSeats=new[]{1,3,4},Assignments=genesis.Assignments,Chosen=true,PriorEnumeration="opposing-data2/roster-table.json all24 normal choices, first geographic match; frozen for joint packing"}};
            var enemy=genesis.Starts[genesis.Assignments[2]].ExplorerAnchor;
            var geometry=new NavGeometry(map.HalfExtent,genesis.Obstacles.ToArray(),map.Revision);
            var selected=new List<OfflineScenarioUnit>();var failures=new List<string>();
            double pitch=p.Navigation.ArrivalSlotSpacing;
            NavPoint[] Bank(bool east,int count)
            {
                var bank=new List<NavPoint>();
                var candidates=(from c in Enumerable.Range(0,12) from r in Enumerable.Range(0,13)
                    select new NavPoint((east?1:-1)*(16+c*pitch),15+r*pitch))
                    .OrderBy(x=>(x.X-(east?28:-28))*(x.X-(east?28:-28))+(x.Z-38)*(x.Z-38)).ThenBy(x=>x.X).ThenBy(x=>x.Z);
                foreach(var point in candidates){
                    if(!map.TryLocate(point,p.ExplorerCollisionRadius,out var at)||at.SurfaceId!=(east?"bank/east":"bank/west")||!geometry.IsFree(point,p.ExplorerCollisionRadius))continue;
                    double home=p.ExplorerRange+p.HeadquartersFootprintRadius+p.ExplorerCollisionRadius+pitch;
                    if(genesis.Starts.Any(s=>RecastFormationInputs.Distance(point,s.Position)<home||RecastFormationInputs.Distance(point,s.ExplorerAnchor)<pitch)||!EnemySeparated(p,point,enemy))continue;
                    var next=selected.Concat(bank.Select(x=>new OfflineScenarioUnit(genesis.Roster[east?0:1].LogicalPlayer,PlayableEntityKind.Explorer,x))).Concat(new[]{new OfflineScenarioUnit(genesis.Roster[east?0:1].LogicalPlayer,PlayableEntityKind.Explorer,point)}).ToArray();
                    try{RecastFormationInputs.WithScenario(p,genesis,next);}catch(ArgumentException e){failures.Add(point.X+","+point.Z+":"+e.Message);continue;}
                    bank.Add(point);if(bank.Count==count)break;
                }
                if(bank.Count!=count)throw new InvalidOperationException("Only "+bank.Count+" valid "+(east?"east":"west")+" source lattice points.");
                return bank.ToArray();
            }
            selected.AddRange(Bank(true,scenarioCount).Select(x=>new OfflineScenarioUnit(genesis.Roster[0].LogicalPlayer,PlayableEntityKind.Explorer,x)));
            var west=Bank(false,scenarioCount+1);selected.AddRange(west.Take(scenarioCount).Select(x=>new OfflineScenarioUnit(genesis.Roster[1].LogicalPlayer,PlayableEntityKind.Explorer,x)));
            rejected=failures.ToArray();
            return RecastFormationInputs.WithScenario(p,genesis,selected.ToArray());
        }
        internal sealed class Packing
        {
            internal bool Found;internal NavPoint Preposition,EastPreposition,WestGoal,EastGoal;
            internal NavPoint[] WestSlots,EastSlots;internal double Spacing,PrepositionSeparation;
            internal readonly List<object> Trials=new List<object>();
        }
        internal static Packing Pack(PlayableProfile p,OfflineMatchConfiguration config,NavGeometry geometry,NavigationSession navigation,int[][] ids,int prepositionActor,int eastPrepositionActor,NavPoint enemy)
        {
            var map=(ThreeCrossingsMap)p.AuthoredMap;var result=new Packing();
            // Exactly the normal allocator's spacing expression for this Explorer.
            double radius=p.ExplorerCollisionRadius;
            result.Spacing=Math.Max(p.Navigation.ArrivalSlotSpacing,4*radius+2*p.Navigation.ArrivalTolerance+2*p.Navigation.LocalGridCell);
            result.PrepositionSeparation=result.Spacing+p.Navigation.ArrivalTolerance;
            var population=navigation.Crowd.Units.ToArray();
            string PointFailure(NavPoint point,string bank){
                if(!map.TryLocate(point,radius,out var at)||at.SurfaceId!=bank||!geometry.IsFree(point,radius))return "typed footprint";
                if(config.Starts.Any(s=>RecastFormationInputs.Distance(point,s.Position)<=p.ExplorerRange+p.HeadquartersFootprintRadius+radius))return "home combat/contact";
                if(!EnemySeparated(p,point,enemy))return "parked opposing auto combat/contact";
                return null;
            }
            IEnumerable<NavPoint> Centers(bool east)=>(new[]{new NavPoint(east?23.2:-23.2,29.4)}).Concat(from c in Enumerable.Range(0,12) from r in Enumerable.Range(0,13) select new NavPoint((east?1:-1)*(16+c*p.Navigation.ArrivalSlotSpacing),15+r*p.Navigation.ArrivalSlotSpacing));
            var valid=new List<KeyValuePair<NavPoint,NavPoint[]>>[2];
            for(int group=0;group<2;group++){
                valid[group]=new List<KeyValuePair<NavPoint,NavPoint[]>>();string bank=group==0?"bank/west":"bank/east";int trial=0;
                foreach(var center in Centers(group==1)){
                    var slots=navigation.AllocateArrivalSlots(center,ids[group]);string reason=PointFailure(center,bank);int bad=-1;
                    if(reason==null&&slots.Length!=50)reason="not50";
                    for(int i=0;i<slots.Length&&reason==null;i++){reason=PointFailure(slots[i],bank);if(reason!=null)bad=i;}
                    result.Trials.Add(new{Kind="normal-goal-candidate",Group=group,Trial=trial++,Center=center,Slots=slots,Accepted=reason==null,Reason=reason,FailedSlot=bad,FailedPoint=bad>=0?(NavPoint?)slots[bad]:null});
                    if(reason==null)valid[group].Add(new KeyValuePair<NavPoint,NavPoint[]>(center,slots));
                }
            }
            var candidates=(from c in Enumerable.Range(0,12) from r in Enumerable.Range(0,13) select new NavPoint(-(16+c*p.Navigation.ArrivalSlotSpacing),15+r*p.Navigation.ArrivalSlotSpacing))
                .OrderBy(x=>(x.X+28)*(x.X+28)+(x.Z-38)*(x.Z-38)).ThenBy(x=>x.X).ThenBy(x=>x.Z).ToArray();
            var eastCandidates=(from c in Enumerable.Range(0,12) from r in Enumerable.Range(0,13) select new NavPoint(16+c*p.Navigation.ArrivalSlotSpacing,15+r*p.Navigation.ArrivalSlotSpacing))
                .OrderBy(x=>(x.X-28)*(x.X-28)+(x.Z-38)*(x.Z-38)).ThenBy(x=>x.X).ThenBy(x=>x.Z).ToArray();
            int pair=0;
            foreach(var west in valid[0])foreach(var east in valid[1]){
                int pairId=pair++;bool conflict=west.Value.Any(w=>east.Value.Any(e=>RecastFormationInputs.Distance(w,e)<result.Spacing));
                if(conflict){result.Trials.Add(new{Kind="goal-pair",Pair=pairId,West=west.Key,East=east.Key,Reason="cross-group goal spacing"});continue;}
                foreach(var point in candidates){
                    string reason=PointFailure(point,"bank/west");int? conflictActor=null;NavPoint? conflictSlot=null;
                    if(reason==null)foreach(var actor in population.Where(a=>a.Id!=prepositionActor))if(RecastFormationInputs.Distance(point,actor.Position)<result.PrepositionSeparation){reason="initial traffic plus arrival error";conflictActor=actor.Id;break;}
                    if(reason==null)foreach(var slot in west.Value.Concat(east.Value))if(RecastFormationInputs.Distance(point,slot)<result.PrepositionSeparation){reason="goal slot plus arrival error";conflictSlot=slot;break;}
                    NavPoint[] preSlots=null;if(reason==null){preSlots=navigation.AllocateArrivalSlots(point,new[]{prepositionActor});if(preSlots.Length!=1||!preSlots[0].Equals(point))reason="normal single allocator changed target";}
                    result.Trials.Add(new{Kind="joint-preposition",Pair=pairId,West=west.Key,East=east.Key,Point=point,Accepted=reason==null,Reason=reason,ConflictActor=conflictActor,ConflictSlot=conflictSlot,SingleSlots=preSlots});
                    if(reason!=null)continue;
                    NavPoint? eastPoint=null;
                    foreach(var next in eastCandidates){
                        string eastReason=PointFailure(next,"bank/east");int? eastActor=null;NavPoint? eastSlot=null;
                        if(eastReason==null&&RecastFormationInputs.Distance(next,point)<result.PrepositionSeparation)eastReason="other preposition plus arrival error";
                        if(eastReason==null)foreach(var actor in population.Where(a=>a.Id!=eastPrepositionActor))if(RecastFormationInputs.Distance(next,actor.Position)<result.PrepositionSeparation){eastReason="initial traffic plus arrival error";eastActor=actor.Id;break;}
                        if(eastReason==null)foreach(var slot in west.Value.Concat(east.Value))if(RecastFormationInputs.Distance(next,slot)<result.PrepositionSeparation){eastReason="goal slot plus arrival error";eastSlot=slot;break;}
                        NavPoint[] eastSingle=null;if(eastReason==null){eastSingle=navigation.AllocateArrivalSlots(next,new[]{eastPrepositionActor});if(eastSingle.Length!=1||!eastSingle[0].Equals(next))eastReason="normal single allocator changed target";}
                        result.Trials.Add(new{Kind="joint-east-preposition",Pair=pairId,WestPreposition=point,Point=next,Accepted=eastReason==null,Reason=eastReason,ConflictActor=eastActor,ConflictSlot=eastSlot,SingleSlots=eastSingle});
                        if(eastReason==null){eastPoint=next;break;}
                    }
                    if(!eastPoint.HasValue)continue;
                    result.Found=true;result.EastPreposition=eastPoint.Value;result.Preposition=point;result.WestGoal=west.Key;result.EastGoal=east.Key;result.WestSlots=west.Value;result.EastSlots=east.Value;return result;
                }
            }
            return result;
        }
    }
}
