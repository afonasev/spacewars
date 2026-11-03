using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Tests.PlayMode
{
    // Shared deterministic source factory for the data preflight and the ordinary witness.
    internal static class RecastFormationInputs
    {
        internal const int Seed=19092026,Horizon=2700,Members=50;
        internal const long Generation=805;
        internal static OfflineMatchConfiguration WithScenario(PlayableProfile p,OfflineMatchConfiguration genesis,OfflineScenarioUnit[] scenario)
        {
            var costs=new double[4,4];for(int i=0;i<4;i++)for(int j=0;j<4;j++)costs[i,j]=genesis.RouteCost(i,j);
            return new OfflineMatchConfiguration(p,genesis.SourceIdentity,genesis.MapIdentity,genesis.RouteProvenance,Seed,genesis.Roster.ToArray(),genesis.Starts.ToArray(),genesis.Sites.ToArray(),genesis.Obstacles.ToArray(),costs,genesis.Spectators.ToArray(),scenario,p.AuthoredMap.Id,genesis.ScenarioBuildings.ToArray());
        }
        internal static OfflineMatchConfiguration Create(PlayableProfile p,out OfflineScenarioUnit[] scenario,out string[] rejected,int scenarioCount=49,bool east=true)
        {
            var map=(ThreeCrossingsMap)p.AuthoredMap;var genesis=map.Configuration(p,Seed,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var geometry=new NavGeometry(map.HalfExtent,genesis.Obstacles.ToArray(),map.Revision);var selected=new List<OfflineScenarioUnit>();var failures=new List<string>();double pitch=p.Navigation.ArrivalSlotSpacing;
            var candidates=(from c in Enumerable.Range(0,12) from r in Enumerable.Range(0,13) select new NavPoint((east?1:-1)*(16+c*pitch),15+r*pitch)).OrderBy(x=>(x.X-(east?28:-28))*(x.X-(east?28:-28))+(x.Z-38)*(x.Z-38)).ThenBy(x=>x.X).ThenBy(x=>x.Z);
            foreach(var point in candidates){
                if(!map.TryLocate(point,p.ExplorerCollisionRadius,out var location)||location.SurfaceId!=(east?"bank/east":"bank/west")||!geometry.IsFree(point,p.ExplorerCollisionRadius))continue;
                // Keep both fixed home zones and automatic actors outside the moving population.
                double separation=p.ExplorerRange+p.HeadquartersFootprintRadius+p.ExplorerCollisionRadius+pitch;
                if(genesis.Starts.Any(s=>Distance(point,s.Position)<separation||Distance(point,s.ExplorerAnchor)<pitch))continue;
                // Unique integer lattice indices impose the pitch already. Rechecking
                // sqrt distance would reject 3.599999999999998 as smaller than3.6.
                // The normal configuration still validates every physical pair.
                var next=selected.Concat(new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,point)}).ToArray();
                try{WithScenario(p,genesis,next);}catch(ArgumentException e){failures.Add(point.X+","+point.Z+":"+e.Message);continue;}
                selected.Add(next.Last());if(selected.Count==scenarioCount)break;
            }
            if(selected.Count!=scenarioCount)throw new InvalidOperationException("Only "+selected.Count+" valid scenario members from frozen source lattice; no population bypass.");
            scenario=selected.ToArray();rejected=failures.ToArray();return WithScenario(p,genesis,scenario);
        }
        internal static double Distance(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
    }
}
