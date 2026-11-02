using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
 internal sealed partial class PlayableDomain
 {
  private BuildingCombatPose BuildingCombat(Building b)
  {
   if(b.Kind==PlayableBuildingKind.RepairTower)return new BuildingCombatPose(new SentryCombatPose(b.Position,GroundHeight(b.Position)+BuildingCombatGeometry.ApronLift,profile.TurretModelScale,b.Healing?.Heading??Math.PI/2,b.Healing?.Pitch??0,b.Ready?1:b.Build/BuildingDuration(b),true));
   if(TerritoryRules.ArmedDefense(b.Kind))return new BuildingCombatPose(new SentryCombatPose(b.Position,GroundHeight(b.Position)+BuildingCombatGeometry.ApronLift,profile.TurretModelScale,b.Weapon?.Heading??Math.PI/2,b.Weapon?.Pitch??0,b.Ready?1:b.Build/BuildingDuration(b),gauss:b.Kind==PlayableBuildingKind.GaussTower,recoil:b.Kind==PlayableBuildingKind.GaussTower&&b.Ready&&b.Health>0&&b.Sale==null?GaussTowerGeometry.Recoil(profile,b.Gauss?.VisibleSeconds??0):0));
   double opening=0,platform=0;double heading=TerritoryRules.BuildingHeading;
   bool available=b.Ready&&b.Sale==null;
   if(b.Kind==PlayableBuildingKind.Factory&&available)
   {
    double u=Math.Min(1,b.LaunchTime/profile.FactoryGateSeconds);
    opening=b.LaunchPhase==FactoryReleaseRules.Opening?u:b.LaunchPhase==FactoryReleaseRules.Driving?1:b.LaunchPhase==FactoryReleaseRules.Closing?1-u:0;
    if(b.LaunchPhase>0)heading=b.Heading;
   }
   if(b.Kind==PlayableBuildingKind.Hangar)
   {
    double u=Math.Min(1,b.LaunchTime/profile.HawkRoofSeconds);
    opening=b.LaunchPhase==0?0:b.LaunchPhase==1?u:b.LaunchPhase==6?1-u:1;
    double v=Math.Min(1,b.LaunchTime/profile.HawkPlatformSeconds);
    platform=b.LaunchPhase==2?v:b.LaunchPhase==3||b.LaunchPhase==4?1:b.LaunchPhase==5?1-v:0;
   }
   return BuildingCombatGeometry.Pose(profile,b.Kind,b.Upgrade?.Complete==true||CenterUpgraded(b.Id),b.Position,GroundHeight(b.Position),heading,opening,platform,b.Ready?1:b.Build/BuildingDuration(b));
  }
 }
}
