using System;

namespace Spacewars.Simulation
{
    // Immutable kind dispatch shared by simulation and native presentation.
    public static class PlayableUnitRules
    {
        // Detached counterpart of the ordinary native Follow goal. The existing
        // Follow execution/arrival algorithm remains authoritative and unchanged.
        public static NavPoint FollowGoal(PlayableProfile p,NavPoint leader,double heading)=>new NavPoint(leader.X-Math.Cos(heading)*p.FollowDistance,leader.Z-Math.Sin(heading)*p.FollowDistance);
        // Shared enumeration from the ordinary combat authority: same math, order,
        // gameplay range ratio and slot spacing. Each caller retains its own legality checks.
        public static NavPoint[] AttackApproachCandidates(PlayableProfile profile,NavPoint from,NavPoint target,double range)
        {
            double radius=range*profile.AttackApproachRangeRatio;
            int count=Math.Max(1,(int)Math.Ceiling(2*Math.PI*radius/profile.Navigation.ArrivalSlotSpacing));
            double facing=Math.Atan2(from.Z-target.Z,from.X-target.X);
            var result=new NavPoint[count];
            for(int i=0;i<count;i++)
            {
                double angle=facing+i*2*Math.PI/count;
                result[i]=new NavPoint(target.X+Math.Cos(angle)*radius,target.Z+Math.Sin(angle)*radius);
            }
            return result;
        }
        public static bool Supported(PlayableEntityKind k)=>k==PlayableEntityKind.Tank||k==PlayableEntityKind.Explorer||k==PlayableEntityKind.Shkval;
        public static double Radius(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Shkval?p.ShkvalCollisionRadius:k==PlayableEntityKind.Explorer?p.ExplorerCollisionRadius:p.TankCollisionRadius;
        public static double Speed(PlayableProfile p,PlayableEntityKind k,bool chassis=false)=>k==PlayableEntityKind.Shkval?p.ShkvalSpeed:k==PlayableEntityKind.Explorer?p.ExplorerSpeed:chassis?p.TankChassisSpeed:p.TankSpeed;
        public static double Turn(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Shkval?p.ShkvalTurnSpeed:k==PlayableEntityKind.Explorer?p.ExplorerTurnSpeed:p.TankTurnSpeed;
        public static double Range(PlayableProfile p,PlayableEntityKind k,bool guidance=false)=>k==PlayableEntityKind.Shkval?(guidance?p.ShkvalGuidanceRange:p.ShkvalBaseRange):k==PlayableEntityKind.Explorer?p.ExplorerRange:p.TankRange;
        public static double Vision(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Shkval?p.ShkvalVision:k==PlayableEntityKind.Explorer?p.ExplorerVision:p.TankVisionRange;
        public static int Health(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Shkval?p.ShkvalHealth:k==PlayableEntityKind.Explorer?p.ExplorerHealth:p.TankHealth;
        public static int Cost(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Shkval?p.ShkvalCreditCost:k==PlayableEntityKind.Explorer?p.ExplorerCreditCost:p.TankCreditCost;
        public static int Population(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Shkval?p.ShkvalPopulationCost:k==PlayableEntityKind.Explorer?p.ExplorerPopulationCost:p.TankPopulationCost;
        public static double Duration(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Shkval?p.ShkvalProductionDurationSec:k==PlayableEntityKind.Explorer?p.ExplorerProductionDurationSec:p.TankProductionSeconds;
    }
}
