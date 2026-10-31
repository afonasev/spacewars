using System;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Tests.PlayMode
{
    // This exact C# factory is used by both the PlayMode witness and the
    // independent preflight. No runtime is created by source validation.
    internal static class RecastStartupMixedInputs
    {
        internal const int Seed=19092026,Horizon=2700,SaveAfter=120;
        internal static readonly NavPoint Goal=new NavPoint(-46,46);
        internal static OfflineMatchConfiguration Create(PlayableProfile profile,out OfflineScenarioUnit[] scenario)
        {
            var map=(ThreeCrossingsMap)profile.AuthoredMap;
            var genesis=map.Configuration(profile,Seed,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            scenario=new[]{
                new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-27.25,42)),
                new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-27.25,44)),
                new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-27.25,46)),
                new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-27.25,48)),
                new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-24.25,42)),
                new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-24.25,44)),
                new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-24.25,46)),
                new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-24.25,48))};
            var costs=new double[4,4];for(int i=0;i<4;i++)for(int j=0;j<4;j++)costs[i,j]=genesis.RouteCost(i,j);
            return new OfflineMatchConfiguration(profile,genesis.SourceIdentity,genesis.MapIdentity,genesis.RouteProvenance,Seed,genesis.Roster.ToArray(),genesis.Starts.ToArray(),genesis.Sites.ToArray(),genesis.Obstacles.ToArray(),costs,genesis.Spectators.ToArray(),scenario,map.Id,genesis.ScenarioBuildings.ToArray());
        }
        internal static void Validate()
        {
            var profile=PlayableProfile.RecastGroupCorridorDefault;var map=(ThreeCrossingsMap)profile.AuthoredMap;
            var config=Create(profile,out var scenario);var geometry=new NavGeometry(profile.ArenaHalfExtent,config.Obstacles.ToArray(),1);bool blocked=false;
            for(int i=0;i<scenario.Length;i++){
                var input=scenario[i];double radius=PlayableUnitRules.Radius(profile,input.Kind);
                if(!map.TryLocate(input.Position,radius,out var start)||start.SurfaceId!="bank/west"||!geometry.IsFree(input.Position,radius))throw new Exception("Invalid typed source start "+i);
                if(!map.TryLocate(Goal,radius,out var goal)||goal.SurfaceId!="deck/-1/1")throw new Exception("Invalid source goal "+i);
                blocked|=!geometry.SegmentFree(input.Position,Goal,radius);
                for(int j=0;j<i;j++){double x=input.Position.X-scenario[j].Position.X,z=input.Position.Z-scenario[j].Position.Z,r=radius+PlayableUnitRules.Radius(profile,scenario[j].Kind);if(x*x+z*z<r*r)throw new Exception("Source pair clearance "+i+"/"+j);}
            }
            if(!blocked)throw new Exception("Source has no authored direct-path obstruction.");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Result="PASS",Check="same-CSharp-genesis/typedstarts/pairclearance/blocker/goal",Seed,Horizon,SaveAfter,Goal,Scenario=scenario,Map=map.Id,map.Revision,Profile=profile.ProfileId,ProfileRevision=profile.Revision,config.SourceIdentity,config.MapIdentity},Newtonsoft.Json.Formatting.Indented));
        }
    }
}
