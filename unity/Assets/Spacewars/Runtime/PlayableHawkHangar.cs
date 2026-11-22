using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private PlayableEntityKind? StagedUnitKind(Building building) =>
            building.Kind == PlayableBuildingKind.Hangar && building.LaunchPhase >= 1 &&
            building.LaunchPhase <= 3 && building.Orders.Count > 0
            ? building.Orders[0].Kind : FactoryStagedKind(building);

        private bool AdvanceHangar(Building b,double dt)
        {
            var f=profile;
            if(b.LaunchPhase==0)
            {
                if(b.Orders.Count==0||!b.Orders[0].Active||b.Orders[0].Remaining>0)return false;
                if(!ReserveAirExit(b))return false;
                b.LaunchPhase=1;b.LaunchTime=0;
            }
            b.LaunchTime+=dt;
            double duration=b.LaunchPhase==1||b.LaunchPhase==6?f.HawkRoofSeconds:b.LaunchPhase==2||b.LaunchPhase==5?f.HawkPlatformSeconds:f.HawkSpinSeconds;
            if(b.LaunchPhase==4)
            {
                // Keep hatch open until the independent aircraft has cleared the opening.
                if(units.TryGetValue(b.LaunchedId,out var flying)&&flying.Air!=null&&flying.Air.Departing)return false;
                b.LaunchPhase=5;b.LaunchTime=0;return false;
            }
            if(b.LaunchTime<duration)return false;
            b.LaunchTime=0;
            if(b.LaunchPhase==3)
            {
                if(!ReserveAirExit(b))return false;
                var destination=AirRallyDestination(b);
                if(!destination.HasValue)return false;
                var kind=b.Orders[0].Kind;
                int id=SpawnUnit(b.Position,b.Owner,kind);var u=units[id];u.Air.Actor.Heading=b.Heading;u.Turret=b.Heading;u.Air.Height=AirSurface(b.Position)+(kind==PlayableEntityKind.Skat?HawkHangarGeometry.PlatformHeight(profile):HawkHangarGeometry.LaunchHeight(profile));u.Air.PreviousHeight=u.Air.Height;u.Air.Departing=true;
                b.LaunchedId=id;b.LaunchPhase=4;
                CompleteProduced(id,destination,b.Owner,kind);
                Sound(PlayableSoundKind.ProductionComplete,b.Position,kind,b.Owner,true);b.Orders.RemoveAt(0);return true;
            }
            if(b.LaunchPhase==6){b.LaunchPhase=0;b.LaunchedId=0;return false;}
            b.LaunchPhase++;return false;
        }
        private NavPoint? AirRallyDestination(Building b)
        {
            // Repeated production shares a rally marker. Each Hawk still needs
            // its own arrival footprint, including room for normal braking.
            var stationary=new List<NavPoint>();
            foreach(var unit in units.Values)
            {
                if(unit.Air!=null&&!unit.Air.Actor.Moving)stationary.Add(unit.Air.Actor.Position);
            }
            var rally=b.HasRally?b.Rally:DefaultAirRally(b);
            var arrivals=new[]{nextId};var footprints=AirArrivalFootprints(Array.Empty<int>());
            // The offset between launch destinations needs room for a full
            // lateral correction as well as both aircraft's arrival error.
            double clearance=profile.HawkRadius;
            double spacing=2*profile.HawkRadius+profile.HawkSeparation;
            // A braking aircraft can stop anywhere inside its arrival radius.
            // Keep the approach away from idle Hawks by that same margin.
            double approachSpacing=spacing+profile.Navigation.ArrivalTolerance;
            var destinations=AirDestinations(rally,arrivals,footprints,clearance,
                point=>stationary.All(other=>SegmentDistance(other,b.Position,point)>=approachSpacing))
                ??AirDestinations(rally,arrivals,footprints);
            return destinations==null?(NavPoint?)null:destinations[0];
        }
        private NavPoint DefaultAirRally(Building b)
        {
            double spacing=2*profile.HawkRadius+profile.HawkSeparation;
            var goal=new NavPoint(b.Position.X+spacing*2,b.Position.Z);
            return AirInside(goal)?goal:new NavPoint(b.Position.X-spacing*2,b.Position.Z);
        }
        private bool ReserveAirExit(Building b)
        {
            double spacing=2*profile.HawkRadius+profile.HawkSeparation;
            var all=units.Values.Where(u=>u.Air!=null&&u.Id!=b.LaunchedId).OrderBy(u=>u.Id).ToArray();
            var blockers=all.Where(u=>Distance(u.Air.Actor.Position,b.Position)<spacing).ToArray();
            if(blockers.Length==0)return true;
            // Finish existing physical yielding before selecting another plan;
            // continually extending the destination can otherwise chase free space.
            if(blockers.Any(u=>u.Air.Yielding))return false;
            var bodies=all.Select(u=>new NavMotionBody{Id=u.Id,Position=u.Air.Actor.Position,Radius=spacing/2,YieldLabel=u.Air.Yielding?-1:0}).ToArray();
            var queue=new int[all.Length];var marks=new int[all.Length];
            for(int root=0;root<all.Length;root++)
            {
                if(Distance(bodies[root].Position,b.Position)>=spacing)continue;
                double away=Math.Atan2(bodies[root].Position.Z-b.Position.Z,bodies[root].Position.X-b.Position.X);
                for(int sample=0;sample<profile.Navigation.CrowdHeadingIntervals;sample++)
                {
                    double offset=(sample%2==0?1:-1)*((sample+1)/2)*Math.PI/profile.Navigation.CrowdHeadingIntervals;
                    double travel=spacing+profile.Navigation.ArrivalTolerance;
                    var delta=new NavPoint(travel*Math.Cos(away+offset),travel*Math.Sin(away+offset));
                    bool Clear(int row,NavPoint to)=>AirInside(to)&&Distance(to,b.Position)>=spacing&&AirSweepClear(bodies[row].Position,to,Math.Max(all[row].Air.Height,ClearHeight(to)));
                    if(!ProducerClearanceRules.TryChain(bodies,root,delta,queue,marks,Clear,out int count))continue;
                    for(int i=0;i<count;i++)
                    {
                        int row=queue[i];var air=all[row].Air;
                        air.YieldGoal=new NavPoint(bodies[row].Position.X+delta.X,bodies[row].Position.Z+delta.Z);
                        air.Yielding=true;air.Short=false;air.Escape=false;
                    }
                    return false;
                }
            }
            return false;
        }
    }
}
