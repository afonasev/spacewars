using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private NavPoint? GroundProductionEscape(NavUnit actor)
        {
            foreach(var b in buildings.Values)
            {
                if(b.Kind!=PlayableBuildingKind.Factory||!b.Ready||b.Sale!=null||b.Orders.Count==0||b.LaunchPhase==FactoryReleaseRules.Closing)continue;
                var order=b.Orders[0];if(!order.Active||order.Remaining>0)continue;
                var exit=FactoryReleaseRules.Exit(profile,b.Position,b.Heading,order.Kind);
                double radius=actor.Radius+PlayableUnitRules.Radius(profile,order.Kind)+profile.FactoryAssemblyGap;
                bool inCorridor=SegmentDistance(actor.Position,b.Position,exit)<radius;
                if(!inCorridor)
                {
                    // Idle apron occupants finish parking while the paid release
                    // is active. Moving orders only yield inside the actual exit.
                    double x=actor.Position.X-exit.X,z=actor.Position.Z-exit.Z;
                    double along=x*Math.Cos(b.Heading)+z*Math.Sin(b.Heading);
                    if(actor.Moving||along< -actor.Radius||x*x+z*z>profile.DefaultRallyDistance*profile.DefaultRallyDistance)continue;
                }
                var parking=FactoryClearanceTarget(b,actor,exit);
                if(parking.HasValue)
                {
                    var delta=new NavPoint(parking.Value.X-actor.Position.X,parking.Value.Z-actor.Position.Z);
                    if(!inCorridor&&delta.X*delta.X+delta.Z*delta.Z<=profile.Navigation.ArrivalTolerance*profile.Navigation.ArrivalTolerance)continue;
                    return delta;
                }
                if(!inCorridor)continue;
                var away=new NavPoint(actor.Position.X-b.Position.X,actor.Position.Z-b.Position.Z);
                return away.Equals(default(NavPoint))?new NavPoint(Math.Cos(b.Heading),Math.Sin(b.Heading)):away;
            }
            return null;
        }
        private NavPoint? FactoryClearanceTarget(Building b,NavUnit actor,NavPoint exit)
        {
            // A square lattice is only a deterministic free-space preference,
            // not an assigned formation or a replacement for the unit's order.
            // Recompute from current occupancy: no transient parking intent to save.
            double largest=Math.Max(profile.TankCollisionRadius,Math.Max(profile.ExplorerCollisionRadius,profile.ShkvalCollisionRadius));
            // Both settled actors may stop within arrival tolerance of their
            // cells; include that envelope so the visible gap still survives.
            double spacing=2*Math.Max(largest,actor.Radius)+profile.FactoryAssemblyGap+2*profile.Navigation.ArrivalTolerance;
            double forwardX=Math.Cos(b.Heading),forwardZ=Math.Sin(b.Heading);
            int extent=(int)Math.Ceiling(Math.Sqrt(navigation.Crowd.Units.Count+1));
            NavPoint? best=null;double bestCost=double.PositiveInfinity;
            int preferredSide=(actor.Id&1)==0?1:-1;
            for(int row=0;row<=extent;row++)for(int column=0;column<=extent;column++)for(int side=0;side<(column==0?1:2);side++)
            {
                if(row==0&&column==0)continue; // Keep the release throat open.
                int signedColumn=column*(side==0?preferredSide:-preferredSide);
                var target=new NavPoint(exit.X+forwardX*row*spacing-forwardZ*signedColumn*spacing,
                    exit.Z+forwardZ*row*spacing+forwardX*signedColumn*spacing);
                double x=target.X-actor.Position.X,z=target.Z-actor.Position.Z;
                double cost=x*x+z*z;
                if(cost>=bestCost||!Geometry.SegmentFree(actor.Position,target,actor.Radius))continue;
                bool clear=true;
                foreach(var peer in navigation.Crowd.Units)
                {
                    if(peer.Id==actor.Id)continue;
                    double gap=actor.Radius+peer.Radius+profile.FactoryAssemblyGap+profile.Navigation.ArrivalTolerance;
                    double px=peer.Position.X-target.X,pz=peer.Position.Z-target.Z;
                    if(px*px+pz*pz<gap*gap){clear=false;break;}
                }
                if(clear){best=target;bestCost=cost;}
            }
            // Only static geometry and endpoint space select a cell. Any peers
            // encountered en route use the original safe physical chain policy.
            return best;
        }
        private bool FactoryCorridorFree(Building b,PlayableEntityKind kind,out NavPoint exit)
        {
            exit=FactoryReleaseRules.Exit(profile,b.Position,b.Heading,kind);
            if(!navigation.Crowd.CanPlace(exit,PlayableUnitRules.Radius(profile,kind)))return false;
            double length=FactoryReleaseRules.ExitDistance(profile,kind),dx=Math.Cos(b.Heading),dz=Math.Sin(b.Heading);
            // The factory footprint owns the inside of the bay. Observe all current
            // ground occupants along the outward corridor. Normal movement clears
            // production pressure before this readiness check; never spawn overlaps.
            foreach(var actor in navigation.Crowd.Units)
            {
                double x=actor.Position.X-b.Position.X,z=actor.Position.Z-b.Position.Z;
                double along=Math.Max(0,Math.Min(length,x*dx+z*dz));
                double sx=x-along*dx,sz=z-along*dz,r=actor.Radius+PlayableUnitRules.Radius(profile,kind)+profile.FactoryAssemblyGap;
                if(sx*sx+sz*sz<r*r)return false;
            }
            return true;
        }
        private void AdvanceFactoryRelease(Building b,double dt)
        {
            if(b.LaunchPhase==FactoryReleaseRules.Closing)
            {
                b.LaunchTime+=dt;
                if(b.LaunchTime>=profile.FactoryGateSeconds){b.LaunchPhase=0;b.LaunchTime=0;b.Heading=TerritoryRules.BuildingHeading;}
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
            Sound(PlayableSoundKind.ProductionComplete,b.Position,order.Kind,b.Owner,true,notificationPoint:exit);
            b.Orders.RemoveAt(0);b.LaunchedId=id;b.LaunchPhase=FactoryReleaseRules.Closing;b.LaunchTime=0;
        }
        private PlayableEntityKind? FactoryStagedKind(Building b)=>b.Kind==PlayableBuildingKind.Factory&&b.LaunchPhase>0&&b.LaunchPhase<FactoryReleaseRules.Closing&&b.Orders.Count>0?(PlayableEntityKind?)b.Orders[0].Kind:null;
        private bool FactoryStagedUpgraded(Building b)=>FactoryStagedKind(b) is PlayableEntityKind kind&&UnitUpgraded(new Unit{Kind=kind,Owner=b.Owner});
    }
}
