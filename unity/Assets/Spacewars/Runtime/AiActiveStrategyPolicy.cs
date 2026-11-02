using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Decisions consume only detached owner observations and previously acquired
    // contacts. The ordinary army planner/authority still owns every command.
    internal static class AiActiveStrategyPolicy
    {
        internal const string DevelopmentWindow="active-development-window";
        private const string ReadyTower="active-ready-tower:";
        internal static void ObserveFortifications(PlayableAiObservation o,AiKnowledgeState knowledge,IDictionary<string,long> memo)
        {
            foreach(var key in memo.Keys.Where(k=>k.StartsWith(ReadyTower,StringComparison.Ordinal)).ToArray())
                if(!o.Buildings.Any(b=>key==ReadyTower+b.Id)&&!(knowledge?.Contacts.Any(c=>c.Building&&key==ReadyTower+c.Id)??false))memo.Remove(key);
            foreach(var b in o.Buildings.Where(b=>o.IsHostile(b.Owner)&&TerritoryRules.ArmedDefense(b.Kind)))
                if(b.Health>0&&b.Phase==ConstructionPhase.Ready)memo[ReadyTower+b.Id]=o.Tick;else memo.Remove(ReadyTower+b.Id);
        }
        internal static bool MaterialDevelopment(PlayableAiObservation o,PlayableProfile p,AiProfile ai)
        {
            double minimum=ai.Value("activeStrategy.materialCredits");
            bool Useful(PlayableBuildingSnapshot b)=>TerritoryRules.Income(p,b.Kind)>0||
                b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Hangar||b.Kind==PlayableBuildingKind.ScientificCenter;
            if(o.Buildings.Any(b=>b.Owner==o.Owner&&b.Health>0&&Useful(b)&&b.PrivateState?.Lifecycle?.Selling!=true&&
                (b.Phase==ConstructionPhase.Constructing&&TerritoryRules.Cost(p,b.Kind)>=minimum||
                 b.PrivateState?.Upgrade?.Active==true&&b.PrivateState.Upgrade.PaidCost>=minimum&&!b.PrivateState.Upgrade.Complete)))return true;
            return o.OwnerResearch.Any(r=>r.Active&&!r.Complete&&r.PaidCost>=minimum&&r.Duration>r.Elapsed&&
                AiResearchCatalog.All.Any(d=>d.Kind==r.Kind&&d.RelativeGain(p)>0&&
                    o.Entities.Any(u=>u.Owner==o.Owner&&u.Health>0&&d.Affects(u.Kind)&&(!u.Upgraded||d.Global))));
        }
        internal static void ObserveDevelopment(PlayableAiObservation o,PlayableProfile p,AiProfile ai,IDictionary<string,long> memo)
        {
            // Never renew on another purchase or research forecast. An actual
            // offensive Applied receipt releases this window for the next cycle.
            if(!memo.ContainsKey(DevelopmentWindow)&&MaterialDevelopment(o,p,ai))memo[DevelopmentWindow]=o.Tick;
        }
        internal static bool HoldForDevelopment(PlayableAiObservation o,PlayableProfile p,AiProfile ai,IReadOnlyDictionary<string,long> memo)=>
            memo.TryGetValue(DevelopmentWindow,out var started)&&o.Tick-started<AiProfile.SecondsToTicks(ai.Value("activeStrategy.developmentSeconds"),30)&&MaterialDevelopment(o,p,ai);

        internal static double Force(PlayableEntitySnapshot[] units,PlayableProfile p,AiProfile ai)=>AiDefensePlanner.Force(units,p,ai)+
            units.Where(u=>u.Kind==PlayableEntityKind.Hawk).Sum(u=>PlayableUnitRules.DamagePerSecond(p,u.Kind,u.Upgraded)*ai.Value("utility.forceHorizonSeconds")*Math.Min(1,PlayableUnitRules.HealthRatio(p,u)));

        internal static PlayableBuildingSnapshot[] KnownTowers(PlayableAiObservation o,AiKnowledgeState knowledge,IReadOnlyDictionary<string,long> memo=null)
        {
            if(knowledge!=null&&(knowledge.OwnerId!=o.OwnerId||knowledge.Generation!=o.Generation||knowledge.ObservationTick>o.Tick))throw new ArgumentException("Foreign fortification knowledge");
            var towers=o.Buildings.Where(b=>o.IsHostile(b.Owner)&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&TerritoryRules.ArmedDefense(b.Kind)).ToList();
            foreach(var c in knowledge?.Contacts??Array.Empty<AiKnownContact>())
                if(c.Building&&o.IsHostile(c.Owner)&&c.HealthAtLastSeen>0&&c.Confidence>0&&memo?.ContainsKey(ReadyTower+c.Id)==true&&Enum.IsDefined(typeof(PlayableBuildingKind),c.Kind)&&TerritoryRules.ArmedDefense((PlayableBuildingKind)c.Kind)&&!o.Buildings.Any(b=>b.Id==c.Id))
                    towers.Add(new PlayableBuildingSnapshot(c.Id,c.Owner,(PlayableBuildingKind)c.Kind,c.Position,c.HealthAtLastSeen,1,0,0,default,includePrivateState:false));
            return towers.OrderBy(b=>b.Id).ToArray();
        }
        internal static double FortificationPower(PlayableAiObservation o,PlayableProfile p,AiProfile ai,PlayableEntitySnapshot[] units,int target,NavPoint point,AiKnowledgeState knowledge=null,IReadOnlyDictionary<string,long> memo=null)
        {
            if(units.Length==0)return 0;
            double power=0;
            foreach(var tower in KnownTowers(o,knowledge,memo))
            {
                // Project each recipient's firing position from its certified
                // approach, rather than crediting one tower against every front.
                double exposed=units.Count(u=>{
                    if(u.Kind==PlayableEntityKind.Hawk&&tower.Kind==PlayableBuildingKind.GaussTower)return false;
                    var r=o.RouteProofs.FirstOrDefault(r=>r.UnitId==u.Id&&r.TargetId==target);
                    double range=PlayableUnitRules.Range(p,u.Kind,u.Upgraded);
                    bool Exposed(NavPoint q)=>AiPositionDevelopmentPlanner.Covers(o,p,tower.Position,tower.Kind,q,u.Kind==PlayableEntityKind.Hawk,tower.Id,u.Id);
                    var at=u.Position;
                    foreach(var waypoint in r?.Path??new[]{point})
                    {
                        if(AiOpponentSelector.Distance(at,point)<=range)return Exposed(at);
                        double dx=waypoint.X-at.X,dz=waypoint.Z-at.Z,length=dx*dx+dz*dz;
                        if(length==0)continue;
                        double bx=at.X-point.X,bz=at.Z-point.Z,b=bx*dx+bz*dz;
                        double discriminant=b*b-length*(bx*bx+bz*bz-range*range),t=1;
                        bool firing=false;
                        if(discriminant>=0){double entry=(-b-Math.Sqrt(discriminant))/length;if(entry>=0&&entry<=1){t=entry;firing=true;}}
                        var end=new NavPoint(at.X+dx*t,at.Z+dz*t);
                        double segment=AiOpponentSelector.Distance(at,end);
                        double nearest=segment==0?0:Math.Max(0,Math.Min(1,((tower.Position.X-at.X)*(end.X-at.X)+(tower.Position.Z-at.Z)*(end.Z-at.Z))/(segment*segment)));
                        if(Exposed(at)||Exposed(end)||Exposed(new NavPoint(at.X+(end.X-at.X)*nearest,at.Z+(end.Z-at.Z)*nearest)))return true;
                        if(firing)return false;
                        at=end;
                    }
                    return Exposed(at);
                })/(double)units.Length;
                double dps=tower.Kind==PlayableBuildingKind.GaussTower?p.TankHealth*p.GaussTankDamageFraction/p.GaussReloadSeconds:
                    p.ExplorerDamage*p.ExplorerAssaultBurstSize*1000d/(p.ExplorerBurstPauseMs+(p.ExplorerAssaultBurstSize-1)*p.ExplorerBurstShotIntervalMs);
                power+=dps*ai.Value("utility.forceHorizonSeconds")*exposed*Math.Min(1,tower.Health/Math.Max(1,TerritoryRules.MaximumHealth(p,tower)));
            }
            return power;
        }
        internal static bool ViableApproach(PlayableAiObservation o,PlayableProfile p,AiProfile ai,PlayableEntitySnapshot[] units,int target,NavPoint point,PlayableRouteTargetKind kind,AiKnowledgeState knowledge=null,IReadOnlyDictionary<string,long> memo=null)
        {
            double fortification=FortificationPower(o,p,ai,units,target,point,knowledge,memo);
            if(fortification==0)return true; // No global veto from remote/incompatible towers.
            int team=AiOpponentSelector.TargetTeam(o,target);
            var local=o.Entities.Where(e=>e.Health>0&&o.IsHostile(e.Owner)&&(team<0||AiOpponentSelector.Team(o,e.Owner)==team)&&AiOpponentSelector.Distance(e.Position,point)<=p.TankVisionRange).ToArray();
            return Force(units,p,ai)*ai.Value("activeStrategy.maximumEncounterRatio")>=fortification+Force(local,p,ai);
        }
    }
}
