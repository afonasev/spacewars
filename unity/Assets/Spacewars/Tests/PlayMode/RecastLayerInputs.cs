using System;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Tests.PlayMode
{
    // Same genesis factory for preflight and the single real threaded witness.
    internal static class RecastLayerInputs
    {
        internal const int Seed=19092026,Horizon=1800,Generation=803;
        internal const int GuardHoldTicks=20,GuardPreparationGateSeconds=10;
        internal static readonly NavLocation GuardGoal=new NavLocation(new NavPoint(-2,0),"ramp-support");
        internal static readonly NavLocation Goal=new NavLocation(new NavPoint(0,5),"upper");
        internal static OfflineMatchConfiguration Create(PlayableProfile profile,out OfflineScenarioUnit[] scenario){
            var map=(StackedRampQualificationMap)profile.AuthoredMap;
            var starts=new[]{new OfflineStart("lower-home-1",1,map.Headquarters(PlayableOwner.Player),new NavPoint(-32,-10),0,1),new OfflineStart("lower-home-2",2,map.Headquarters(PlayableOwner.Enemy),new NavPoint(32,10),Math.PI,2)};
            scenario=new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-12,0)),new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-15,0))};
            return new OfflineMatchConfiguration(profile,"native-stacked-ramp-qa-v1",map.Id,"authored-ramp-physical-v1",Seed,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)},starts,map.Sites(profile),map.MovementBlockers.ToArray(),new double[,]{{0,Math.Sqrt(8192)},{Math.Sqrt(8192),0}},scenario:scenario,terrain:map.Id);
        }
        internal static void Validate(){
            var profile=PlayableProfile.RecastLayerQualification;var map=(StackedRampQualificationMap)profile.AuthoredMap;var config=Create(profile,out var scenario);var geometry=new NavGeometry(map.HalfExtent,config.Obstacles.ToArray(),1);
            for(int i=0;i<scenario.Length;i++){double radius=PlayableUnitRules.Radius(profile,scenario[i].Kind);if(!map.TryLocate(scenario[i].Position,radius,out var start)||start.SurfaceId!="lower"||!geometry.IsFree(start.Position,radius)||!map.IsValidLocation(Goal,radius))throw new Exception("Invalid exact typed layer genesis.");
                for(int j=0;j<i;j++)if(LayerDistance(scenario[i].Position,scenario[j].Position)<radius+PlayableUnitRules.Radius(profile,scenario[j].Kind))throw new Exception("Initial pair overlap.");
                var from=new NavLocation(new NavPoint(-2,0),"lower");if(!map.TryTraverse(from,new NavLocation(new NavPoint(0,0),"ramp-support"),radius,out var active)||active.SurfaceId!="ramp-support"||map.Height(active)!=2)throw new Exception("No continuous affine ramp.");if(!map.TryTraverse(active,new NavLocation(new NavPoint(2,0),"upper"),radius,out var upper)||upper.SurfaceId!="upper"||map.Height(upper)!=4)throw new Exception("No physical upper crossing.");
            }
            Console.WriteLine($"INPUTS PASS map={map.Id}/{map.Revision} binding={map.Provider.Binding} profile={profile.ProfileId}/{profile.Revision} seed={Seed} lower starts=(-12,0),(-15,0), upper goal=(0,5), ramp y=x+2 Entry(-2,0,0) Exit(2,4,0) width1.5, radius={profile.ExplorerCollisionRadius}");
        }
        internal static void ValidateGuard(){
            Validate();var p=PlayableProfile.RecastLayerQualification;var map=(StackedRampQualificationMap)p.AuthoredMap;
            if(!map.IsValidLocation(GuardGoal,p.ExplorerCollisionRadius)||!map.TryTraverse(new NavLocation(GuardGoal.Position,"lower"),GuardGoal,p.ExplorerCollisionRadius,out var seam)||!seam.Equals(GuardGoal))throw new Exception("Invalid authored exact assigned guard seam.");
            Console.WriteLine($"GUARD INPUTS PASS subset goal={GuardGoal.SurfaceId}@({GuardGoal.Position.X},{GuardGoal.Position.Z}) holdticks={GuardHoldTicks} actualpending BeforePrepare gate<={GuardPreparationGateSeconds}s; original seed/gen/horizon unchanged");
        }
        private static double LayerDistance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
    }
}
