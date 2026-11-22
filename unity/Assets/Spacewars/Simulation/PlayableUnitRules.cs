using System;
using System.Collections.Generic;
using System.Linq;

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
        public static NavPoint[] AttackApproachCandidates(PlayableProfile profile,NavPoint from,NavPoint target,double range,double minimumRadius=0)
        {
            double radius=Math.Max(range*profile.AttackApproachRangeRatio,minimumRadius);
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
        // Expected ground/building damage on the same horizon as the ordinary AI force
        // estimate. Dispatch belongs to the mechanic adapter, never composition policies.
        public static double ExplorerRocketDamagePerSecond(PlayableProfile p,bool air)=>
            2*p.ExplorerRocketDamage/(air?p.ExplorerRocketAirInterval+p.ExplorerRocketAirReload:p.ExplorerRocketGroundInterval+p.ExplorerRocketGroundReload);

        public static double DamagePerSecond(PlayableProfile p,PlayableEntityKind k,bool assault=false)
        {
            switch(k)
            {
                case PlayableEntityKind.Igla:return p.IglaDamage/p.IglaReload;
                case PlayableEntityKind.Cerberus:return 2*p.CerberusDamage*p.CerberusFireTime/(p.CerberusFireTime+p.CerberusCooldown);
                case PlayableEntityKind.Skat:return 0;
                case PlayableEntityKind.Hawk:return 2*p.HawkDamage/HawkSalvoCycle(p,assault);
                case PlayableEntityKind.Tank:return p.TankWeaponDamage/p.TankWeaponReloadSeconds;
                case PlayableEntityKind.Explorer:
                    int burst=assault?p.ExplorerAssaultBurstSize:p.ExplorerBurstSize;
                    return p.ExplorerDamage*burst*1000d/(p.ExplorerBurstPauseMs+(burst-1)*p.ExplorerBurstShotIntervalMs);
                case PlayableEntityKind.Shkval:return p.ShkvalDamage/ShkvalFireInterval(p,assault);
                default:throw new ArgumentException("Unknown damage mechanic");
            }
        }
        // Single registration of the existing typed ordinary production adapters.
        // Strategy/admission/ledger paths decode this registry, never enumerate commands.
        private static readonly IReadOnlyDictionary<PlayableEntityKind,PlayableCommandKind> productionCommands=
            new Dictionary<PlayableEntityKind,PlayableCommandKind>{
                {PlayableEntityKind.Igla,PlayableCommandKind.QueueIgla},
                {PlayableEntityKind.Cerberus,PlayableCommandKind.QueueCerberus},
                {PlayableEntityKind.Skat,PlayableCommandKind.QueueSkat},
                {PlayableEntityKind.Hawk,PlayableCommandKind.QueueHawk},
                {PlayableEntityKind.Tank,PlayableCommandKind.QueueTank},
                {PlayableEntityKind.Explorer,PlayableCommandKind.QueueExplorer},
                {PlayableEntityKind.Shkval,PlayableCommandKind.QueueShkval}};
        public static PlayableCommandKind ProductionCommand(PlayableEntityKind kind)=>productionCommands.TryGetValue(kind,out var command)?command:throw new ArgumentException("Unknown production adapter.");
        public static PlayableEntityKind? ProductionKind(PlayableCommandKind command)
        {foreach(var row in productionCommands)if(row.Value==command)return row.Key;return null;}
        public static bool Supported(PlayableEntityKind k)=>productionCommands.ContainsKey(k);
        public static bool Aircraft(PlayableEntityKind kind) => kind == PlayableEntityKind.Hawk || kind == PlayableEntityKind.Skat;
        // C3.2 source is 3.02 m wide and 4.43 m long before the common actor scale.
        public const float IglaBeamVisibleSeconds = .24f;
        public const double IglaModelScale = .56;
        public const double CerberusPlumeWidthScale = .46;
        public static double CerberusModelScale(PlayableProfile p)=>1.18*p.TankModelRadius*p.TankModelScale/1.585;
        // Enclose the C3.2 hull and tracks in the shared circular ground footprint.
        // The swivelling weapon barrels do not contribute to movement clearance.
        public static double CerberusCollisionRadius(PlayableProfile p) => 2.63 * CerberusModelScale(p);
        public static double Radius(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Igla?.65:k==PlayableEntityKind.Cerberus?CerberusCollisionRadius(p):Aircraft(k)?p.HawkRadius:k==PlayableEntityKind.Shkval?p.ShkvalCollisionRadius:k==PlayableEntityKind.Explorer?p.ExplorerCollisionRadius:p.TankCollisionRadius;
        public static double AirBuildingHeight(PlayableProfile profile,PlayableBuildingKind k)
        {
            if(k==PlayableBuildingKind.Hangar)return profile.HangarModelHeightMeters*profile.HangarModelScale;
            double visual=k==PlayableBuildingKind.Headquarters?profile.HeadquartersModelHeightMeters*profile.HeadquartersModelScale:k==PlayableBuildingKind.Factory?profile.FactoryModelHeightMeters*profile.FactoryModelScale:k==PlayableBuildingKind.Refinery?profile.RefineryModelHeightMeters*profile.RefineryModelScale:k==PlayableBuildingKind.Outpost?profile.OutpostModelHeightMeters*profile.OutpostModelScale:k==PlayableBuildingKind.Mine?profile.MineModelHeightMeters*profile.MineModelScale:profile.ScienceModelHeightMeters*profile.ScienceModelScale;
            return Math.Max(profile.ShkvalBuildingCollisionHeight,visual);
        }
        // Authored Hawk body bounds; center offset is geometry, not a balance setting.
        // Starting a fresh salvo always requires a full second since the last actual launch.
        public static double HawkSalvoPause(PlayableProfile p)=>Math.Max(1,p.HawkReloadSeconds);
        public static double HawkSalvoCycle(PlayableProfile p,bool upgraded)=>HawkSalvoPause(p)+(upgraded?p.HawkUpgradedSalvoDelay:p.HawkSalvoDelay);
        public static double HawkBodyHeight(PlayableProfile profile)=>1.8145018368959427*profile.HawkModelScale;
        // Skat coordinates are authored around the platform datum.
        public const double SkatModelScale = .54;
        public static double AircraftBodyHeight(PlayableProfile profile,PlayableEntityKind kind) =>
            kind == PlayableEntityKind.Skat ? .387 * SkatModelScale : HawkBodyHeight(profile);
        public static double Speed(PlayableProfile p,PlayableEntityKind k,bool chassis=false)=>k==PlayableEntityKind.Igla?p.IglaSpeed:k==PlayableEntityKind.Cerberus?p.CerberusSpeed:k==PlayableEntityKind.Skat?p.SkatSpeed:k==PlayableEntityKind.Hawk?p.HawkSpeed:k==PlayableEntityKind.Shkval?p.ShkvalSpeed:k==PlayableEntityKind.Explorer?p.ExplorerSpeed:chassis?p.TankChassisSpeed:p.TankSpeed;
        public static double Turn(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Igla?p.IglaHullTurn:k==PlayableEntityKind.Cerberus?p.CerberusTurn*Math.PI/180:k==PlayableEntityKind.Skat?p.SkatYawSpeed:k==PlayableEntityKind.Hawk?p.HawkYawSpeed:k==PlayableEntityKind.Shkval?p.ShkvalTurnSpeed:k==PlayableEntityKind.Explorer?p.ExplorerTurnSpeed:p.TankTurnSpeed;
        public static double Range(PlayableProfile p,PlayableEntityKind k,bool guidance=false)=>k==PlayableEntityKind.Igla?p.IglaRange:k==PlayableEntityKind.Cerberus?p.CerberusRange:k==PlayableEntityKind.Skat?p.SkatRepairRange:k==PlayableEntityKind.Hawk?p.HawkRange:k==PlayableEntityKind.Shkval?(guidance?p.ShkvalGuidanceRange:p.ShkvalBaseRange):k==PlayableEntityKind.Explorer?p.ExplorerRange:p.TankRange;
        public static double ShkvalFireInterval(PlayableProfile p,bool guidance)=>p.ShkvalFireIntervalMs/1000d*(guidance?p.ShkvalGuidanceFireIntervalMultiplier:1);
        public static double ShkvalBlastRadius(PlayableProfile p,bool guidance)=>guidance?p.ShkvalBlastRadius:p.ShkvalBaseBlastRadius;
        public static double Vision(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Igla?p.IglaVision:k==PlayableEntityKind.Cerberus?p.CerberusVision:k==PlayableEntityKind.Skat?p.SkatVision:k==PlayableEntityKind.Hawk?p.HawkVision:k==PlayableEntityKind.Shkval?p.ShkvalVision:k==PlayableEntityKind.Explorer?p.ExplorerVision:p.TankVisionRange;
        public static double MaximumHealth(PlayableProfile p,PlayableEntitySnapshot u)=>u.MaxHealth>0?u.MaxHealth:PlayableStatRounding.MaximumHealth(p,Health(p,u.Kind));
        public static double HealthRatio(PlayableProfile p,PlayableEntitySnapshot u)=>(u.ExactHealth??u.Health)/MaximumHealth(p,u);
        public static int Health(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Igla?p.IglaHealth:k==PlayableEntityKind.Cerberus?p.CerberusHealth:k==PlayableEntityKind.Skat?p.SkatHealth:k==PlayableEntityKind.Hawk?(int)p.HawkHealth:k==PlayableEntityKind.Shkval?p.ShkvalHealth:k==PlayableEntityKind.Explorer?p.ExplorerHealth:p.TankHealth;
        public static int Cost(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Igla?p.IglaCreditCost:k==PlayableEntityKind.Cerberus?p.CerberusCreditCost:k==PlayableEntityKind.Skat?p.SkatCreditCost:k==PlayableEntityKind.Hawk?(int)p.HawkCreditCost:k==PlayableEntityKind.Shkval?p.ShkvalCreditCost:k==PlayableEntityKind.Explorer?p.ExplorerCreditCost:p.TankCreditCost;
        public static int Population(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Igla?p.IglaPopulation:k==PlayableEntityKind.Cerberus?p.CerberusPopulation:k==PlayableEntityKind.Skat?p.SkatPopulationCost:k==PlayableEntityKind.Hawk?(int)p.HawkPopulationCost:k==PlayableEntityKind.Shkval?p.ShkvalPopulationCost:k==PlayableEntityKind.Explorer?p.ExplorerPopulationCost:p.TankPopulationCost;
        public static double Duration(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Igla?p.IglaDuration:k==PlayableEntityKind.Cerberus?p.CerberusDuration:k==PlayableEntityKind.Skat?p.SkatProductionSeconds:k==PlayableEntityKind.Hawk?p.HawkProductionSeconds:k==PlayableEntityKind.Shkval?p.ShkvalProductionDurationSec:k==PlayableEntityKind.Explorer?p.ExplorerProductionDurationSec:p.TankProductionSeconds;
    }
}
