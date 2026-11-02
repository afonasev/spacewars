using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private PlayableTurretSnapshot GaussSnapshot(Building b,PlayableOwner? observer)
        {
            var state=b.Weapon;var shot=b.Gauss;
            bool ready=b.Ready&&b.Health>0&&b.Sale==null;
            int target=ready&&state!=null&&TurretLegal(b,state.Target)?state.Target:0;
            if(target!=0&&observer.HasValue&&Target(target,out var p,out _)&&!Vision(observer.Value).IsVisible(p))target=0;
            // Conservative projection: no endpoints for a shot crossing unseen space.
            bool visible=ready&&shot!=null&&shot.VisibleSeconds>0;
            if(visible&&observer.HasValue)
                for(int i=0;i<=64;i++)if(!Vision(observer.Value).IsVisible(LerpAir(shot.Start,shot.End,i/64d).Ground)){visible=false;break;}
            return new PlayableTurretSnapshot(target,state?.Heading??Math.PI/2,state?.Pitch??0,state?.Turning??false,state?.ShotSequence??0,ArmySystemsLevel(b.Owner,true),visible?shot.Start:default,visible?shot.End:default,visible?shot.VisibleSeconds:0,ready&&target!=0&&state.Reload>0?Math.Max(0,1-state.Reload/profile.GaussChargeSeconds):0,ready?GaussTowerGeometry.Recoil(profile,shot?.VisibleSeconds??0):0);
        }
        private bool AdvanceGaussPending(Building b,double dt)
        {
            var shot=b.Gauss;if(shot==null)return true;
            shot.VisibleSeconds=Math.Max(0,shot.VisibleSeconds-dt);
            while(shot.Next<shot.Targets.Length)
            {
                int id=shot.Targets[shot.Next];
                // A disappeared unit must not turn into a hit on its newly created wreck.
                if((units.ContainsKey(id)||buildings.ContainsKey(id))&&!TryDamageWithOwner(id,shot.Damage[shot.Next],b.Id,b.Owner))return false;
                shot.Next++;
            }
            return true;
        }
        private void FireGauss(Building b,SentryTurretState state)
        {
            var from=GaussTowerGeometry.Muzzle(b.Position,GroundHeight(b.Position)+BuildingCombatGeometry.ApronLift,profile.TurretModelScale,state.Heading,state.Pitch);
            var forward=new BallisticPoint(Math.Cos(state.Heading)*Math.Cos(state.Pitch),Math.Sin(state.Pitch),Math.Sin(state.Heading)*Math.Cos(state.Pitch));
            // The targeting radius is measured from the building, as for the sentry.
            double length=Math.Max(0,profile.GaussRange-GaussTowerGeometry.BarrelOffset*profile.TurretModelScale);
            var to=HawkRocketFlight.Add(from,HawkRocketFlight.Scale(forward,length));
            double stop=1;
            bool Clear(double t)=>PlayableBallistics.TerrainLineClear(from,LerpAir(from,to,t),profile.GaussBeamRadius,SolidObstacles(),profile.BallisticWallHeight,AirSurface);
            if(!Clear(1)){
                double low=0,high=1;
                for(int i=0;i<40;i++){double mid=(low+high)/2;if(Clear(mid))low=mid;else high=mid;}
                stop=low;
            }
            var hits=new List<(int id,double t)>();
            foreach(var u in units.Values)
            {
                if(u.Air!=null||u.Kind==PlayableEntityKind.Hawk||!TryActor(u.Id,out var n))continue;
                // Reuse the current native ground-body contact volume, independent of renderer/LOD.
                double radius=PlayableUnitRules.Radius(profile,u.Kind),r=radius+profile.GaussBeamRadius;
                var center=new BallisticPoint(n.Position.X,GroundHeight(n.Position)+radius,n.Position.Z);
                if(!HawkRocketFlight.Contact(from,to,center,center,r,out double t)||t>stop)continue;
                if(Hostile(u.Owner,b.Owner))hits.Add((u.Id,t));else stop=Math.Min(stop,t);
            }
            foreach(var other in buildings.Values)
            {
                if(other.Id==b.Id||other.Phase==ConstructionPhase.Pending)continue;
                if(!BuildingCombat(other).Segment(from,to,profile.GaussBeamRadius,out double t,out _)||t>stop)continue;
                if(Hostile(other.Owner,b.Owner))hits.Add((other.Id,t));else stop=Math.Min(stop,t);
            }
            var ordered=hits.Where(h=>h.t<stop).OrderBy(h=>h.t).ThenBy(h=>h.id).ToArray();
            double damage=WeaponDamage(b.Owner,profile.GaussDamage);
            var amounts=new double[ordered.Length];for(int i=0;i<amounts.Length;i++){amounts[i]=damage;damage*=profile.GaussPierceRetention;}
            b.Gauss=new GaussShotState{Start=from,End=LerpAir(from,to,stop),VisibleSeconds=profile.GaussBeamSeconds,Targets=ordered.Select(h=>h.id).ToArray(),Damage=amounts};
            state.ShotSequence++;state.Reload=profile.GaussReloadSeconds;
            Sound(PlayableSoundKind.Shot,b.Position,PlayableEntityKind.Tank,b.Owner,unitId:b.Id,altitude:from.Y);
            AdvanceGaussPending(b,0);
        }
    }
}
