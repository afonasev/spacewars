using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private bool FactoryCorridorFree(Building b,PlayableEntityKind kind,out NavPoint exit)
        {
            exit=FactoryReleaseRules.Exit(profile,b.Position,b.Heading,kind);
            if(!navigation.Crowd.CanPlace(exit,PlayableUnitRules.Radius(profile,kind)))return false;
            double length=FactoryReleaseRules.ExitDistance(profile,kind),dx=Math.Cos(b.Heading),dz=Math.Sin(b.Heading);
            // The factory footprint owns the inside of the bay. Observe all current
            // ground occupants along the outward corridor without moving them.
            foreach(var actor in navigation.Crowd.Units)
            {
                double x=actor.Position.X-b.Position.X,z=actor.Position.Z-b.Position.Z;
                double along=Math.Max(0,Math.Min(length,x*dx+z*dz));
                double sx=x-along*dx,sz=z-along*dz,r=actor.Radius+PlayableUnitRules.Radius(profile,kind);
                if(sx*sx+sz*sz<r*r)return false;
            }
            return true;
        }
        private void AdvanceFactoryRelease(Building b,double dt)
        {
            if(b.LaunchPhase==FactoryReleaseRules.Closing)
            {
                b.LaunchTime+=dt;
                if(b.LaunchTime>=profile.FactoryGateSeconds){b.LaunchPhase=0;b.LaunchTime=0;}
                return;
            }
            if(b.Orders.Count==0){b.LaunchPhase=FactoryReleaseRules.Closing;b.LaunchTime=0;return;}
            var order=b.Orders[0];
            if(b.LaunchPhase==0)
            {
                if(!FactoryCorridorFree(b,order.Kind,out _))return;
                b.LaunchPhase=FactoryReleaseRules.Opening;b.LaunchTime=0;
            }
            if(b.LaunchPhase==FactoryReleaseRules.Opening)
            {
                b.LaunchTime+=dt;
                if(b.LaunchTime>=profile.FactoryGateSeconds){b.LaunchPhase=FactoryReleaseRules.Driving;b.LaunchTime=0;}
                return;
            }
            if(!FactoryCorridorFree(b,order.Kind,out var exit))return;
            double duration=FactoryReleaseRules.Duration(profile,order.Kind);
            b.LaunchTime=Math.Min(duration,b.LaunchTime+dt);
            if(b.LaunchTime<duration)return;
            int id;
            try{id=SpawnUnit(exit,b.Owner,order.Kind);}catch(NavigationRetentionBudget.CapacityRefused){return;}
            navigation.Crowd.TryGet(id,out var actor);actor.Heading=b.Heading;units[id].Turret=b.Heading;
            // Single owner-thread boundary: the same new Unit now participates in
            // commands, target selection, direct/AoE damage and weapons.
            CompleteProduced(id,b.HasRally?(NavPoint?)b.Rally:null,b.Owner,order.Kind);
            Sound(PlayableSoundKind.ProductionComplete,b.Position,order.Kind,b.Owner,true);
            b.Orders.RemoveAt(0);b.LaunchedId=id;b.LaunchPhase=FactoryReleaseRules.Closing;b.LaunchTime=0;
        }
        private PlayableEntityKind? FactoryStagedKind(Building b)=>b.Kind==PlayableBuildingKind.Factory&&b.LaunchPhase>0&&b.LaunchPhase<FactoryReleaseRules.Closing&&b.Orders.Count>0?(PlayableEntityKind?)b.Orders[0].Kind:null;
        private bool FactoryStagedUpgraded(Building b)=>FactoryStagedKind(b) is PlayableEntityKind kind&&UnitUpgraded(new Unit{Kind=kind,Owner=b.Owner});
    }
}
