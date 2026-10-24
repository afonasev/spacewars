using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
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
                int id=SpawnUnit(b.Position,b.Owner,PlayableEntityKind.Hawk);var u=units[id];u.Air.Actor.Heading=b.Heading;u.Turret=b.Heading;u.Air.Height=AirSurface(b.Position)+HawkHangarGeometry.LaunchHeight(profile);u.Air.PreviousHeight=u.Air.Height;u.Air.Departing=true;
                b.LaunchedId=id;b.LaunchPhase=4;
                CompleteProduced(id,b.HasRally?(NavPoint?)b.Rally:DefaultAirRally(b),b.Owner,PlayableEntityKind.Hawk);
                Sound(PlayableSoundKind.ProductionComplete,b.Position,PlayableEntityKind.Hawk,b.Owner,true);b.Orders.RemoveAt(0);return true;
            }
            if(b.LaunchPhase==6){b.LaunchPhase=0;b.LaunchedId=0;return false;}
            b.LaunchPhase++;return false;
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
            var planned=new Dictionary<int,NavPoint>();var visiting=new HashSet<int>();int attempts=checked(all.Length*16);
            bool Displace(Unit u)
            {
                if(planned.ContainsKey(u.Id))return true;if(!visiting.Add(u.Id))return false;
                var from=u.Air.Actor.Position;
                // Search candidate neighbouring positions outward from the hatch; each blocked candidate recurses through the chain.
                double away=Math.Atan2(from.Z-b.Position.Z,from.X-b.Position.X);
                for(int i=0;i<16&&attempts>0;i++)
                {
                    attempts--;double angle=away+(i%2==0?1:-1)*(i+1)/2*Math.PI/8;
                    var to=new NavPoint(from.X+spacing*Math.Cos(angle),from.Z+spacing*Math.Sin(angle));
                    if(!AirInside(to)||Distance(to,b.Position)<spacing||planned.Values.Any(p=>Distance(p,to)<spacing))continue;
                    var occupants=all.Where(x=>x.Id!=u.Id&&!visiting.Contains(x.Id)&&Distance(planned.TryGetValue(x.Id,out var p)?p:x.Air.Actor.Position,to)<spacing).ToArray();
                    var prior=planned.Keys.ToArray();
                    bool failed=occupants.Any(x=>!Displace(x))||all.Any(x=>x.Id!=u.Id&&Distance(planned.TryGetValue(x.Id,out var destination)?destination:x.Air.Actor.Position,to)<spacing);
                    if(failed){foreach(int id in planned.Keys.Where(id=>!prior.Contains(id)).ToArray())planned.Remove(id);continue;}
                    planned[u.Id]=to;visiting.Remove(u.Id);return true;
                }
                visiting.Remove(u.Id);return false;
            }
            foreach(var u in blockers)if(!Displace(u))return false;
            foreach(var pair in planned)
            {
                var air=units[pair.Key].Air;
                // Yield is a temporary local flight intent. Existing order/hold survives.
                air.YieldGoal=pair.Value;air.Yielding=true;air.Short=false;air.Escape=false;
            }
            return false;
        }
    }
}
