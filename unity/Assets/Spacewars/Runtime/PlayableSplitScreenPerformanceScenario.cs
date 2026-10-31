#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Runtime
{
    public sealed partial class PlayableRuntime
    {
        // Opt-in QA genesis only. All scheduling, fog, controls and commands use the ordinary host.
        public static PlayableRuntime CreateSplitScreenPerformanceMatch(PlayableProfile profile,long generation,NativeLobbyConfiguration setup,bool startPaused=false,AiProfile aiProfile=null)
        {
            if(!(profile.AuthoredMap is ThreeCrossingsMap)||setup.Participants.Count!=4||setup.Participants.Any(p=>!p.Human))
                throw new ArgumentException("Split-screen benchmark requires four ordinary human seats on Crossings.");
            setup=setup.Copy();setup.PrepareMatchNames();var basis=NativeLobbyMatch.Create(profile,setup);
            var placed=new List<OfflineScenarioUnit>();
            double radius=PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank),spacing=2*radius+.25;
            double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
            foreach(var seat in Enumerable.Range(0,4)){
                var home=basis.Starts[basis.Assignments[seat]].ExplorerAnchor;int count=0;
                for(int ring=1;ring<=60&&count<49;ring++)for(int z=-ring;z<=ring&&count<49;z++)for(int x=-ring;x<=ring&&count<49;x++){
                    if(Math.Max(Math.Abs(x),Math.Abs(z))!=ring)continue;
                    var point=new NavPoint(home.X+x*spacing,home.Z+z*spacing);
                    if(!profile.AuthoredMap.SupportsFootprint(point,radius))continue;
                    if(basis.Starts.Any(s=>Distance(point,s.ExplorerAnchor)<radius+profile.ExplorerCollisionRadius))continue;
                    if(placed.Any(u=>Distance(point,u.Position)<2*radius))continue;
                    if(basis.Sites.Any(s=>Distance(point,s.Position)<TerritoryRules.Radius(profile,s.Kind)+radius||s.Slots.Any(slot=>Distance(point,slot.Position)<Math.Max(profile.ScienceFootprintRadius,Math.Max(profile.FactoryFootprintRadius,profile.RefineryFootprintRadius))+radius)))continue;
                    placed.Add(new OfflineScenarioUnit(basis.Roster[seat].LogicalPlayer,PlayableEntityKind.Tank,point,0));count++;
                }
                if(count!=49)throw new InvalidOperationException("Cannot place benchmark army for owner "+seat);
            }
            var costs=new double[basis.Starts.Count,basis.Starts.Count];
            for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            var configuration=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,scenario:placed.ToArray(),terrain:profile.AuthoredMap.Id);
            var runtime=new PlayableRuntime(configuration,generation,startPaused,setup.Difficulty,aiProfile:aiProfile);
            runtime.lobbyConfiguration=setup;return runtime;
        }
    }
}
#endif
