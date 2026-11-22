using System;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal sealed partial class AiCombatMicroPolicy
    {
        static bool CanHit(PlayableEntitySnapshot attacker,PlayableEntitySnapshot victim)=>
            AiRosterCatalog.Initial.Targets(attacker.Kind,victim.Kind==PlayableEntityKind.Hawk?"air":"ground");
        static bool GroundScreen(PlayableEntitySnapshot unit)=>unit.Kind is PlayableEntityKind.Tank or PlayableEntityKind.Explorer;
        static PlayableEntitySnapshot[] LocalScreens(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot unit)=>
            o.Entities.Where(a=>a.Id!=unit.Id&&a.Owner==unit.Owner&&a.Health>0&&!a.Departing&&
                Distance(a.Position,unit.Position)<=V(ai,d,"supportRadius")).ToArray();
        static double Blast(PlayableProfile p,PlayableEntitySnapshot threat)=>threat.Kind==PlayableEntityKind.Shkval?
            PlayableUnitRules.ShkvalBlastRadius(p,threat.Upgraded):0;
        internal static double RotationLimit(PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot unit)=>V(ai,d,"maxWithdrawal");
        static double WithdrawalSeconds(PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot unit,NavPoint goal)
        {
            double length=Distance(unit.Position,goal);
            double speed=PlayableUnitRules.Speed(p,unit.Kind,unit.Upgraded);
            if(unit.Kind==PlayableEntityKind.Tank&&length<=Math.Max(p.TankReverseDistance,
                WithdrawalStepLength(p,ai,d,unit)+p.Navigation.ArrivalTolerance))
            {
                double aim=Math.Atan2(goal.Z-unit.Position.Z,goal.X-unit.Position.X);
                if(Math.Abs(NavCrowdMotion.Angle(aim-unit.HullHeading-Math.PI))<=p.TankReverseConeRadians)
                    speed*=p.TankReverseSpeedRatio;
            }
            // Allow for an ordinary paid command's reaction window before travel.
            return ai.DifficultyValue(d,"control.reactionMaxSeconds")+length/speed;
        }
        static bool ScreensThreat(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot unit,PlayableEntitySnapshot cover,PlayableEntitySnapshot enemy,NavPoint point)
        {
            if(!CanHit(enemy,cover)||enemy.Kind!=PlayableEntityKind.Shkval&&!AiCombatMicroSafety.FireClear(o,p,enemy,cover))return false;
            double nearer=Distance(enemy.Position,cover.Position);
            return nearer<=PlayableUnitRules.Range(p,enemy.Kind,enemy.Upgraded)&&
                nearer+2*p.Navigation.ArrivalTolerance<Distance(enemy.Position,point)&&
                (Blast(p,enemy)<=0||Distance(point,cover.Position)>Blast(p,enemy)+PlayableUnitRules.Radius(p,unit.Kind));
        }
        bool Covered(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot unit,
            PlayableEntitySnapshot[] threats,NavPoint point)
        {
            var cover=LocalScreens(o,p,ai,d,unit);
            if(unit.Kind==PlayableEntityKind.Shkval&&cover.Any(GroundScreen))cover=cover.Where(GroundScreen).ToArray();
            return threats.All(enemy=>Distance(enemy.Position,point)>PlayableUnitRules.Range(p,enemy.Kind,enemy.Upgraded)+Blast(p,enemy)+PlayableUnitRules.Radius(p,unit.Kind)||
                cover.Any(a=>ScreensThreat(o,p,unit,a,enemy,point)&&
                    // An ally already withdrawing from this shooter cannot
                    // take over its fire for a second retreating unit.
                    !WithdrawsFrom(a.Id,enemy.Id)));
        }
        bool WithdrawsFrom(int actor,int attacker)=>
            clients.TryGetValue(actor,out var client)&&client.Sequence>0&&client.Phase==AiCombatMicroPhase.Withdraw&&
            fire.Any(f=>f.Source==attacker&&f.Victim==actor&&!SwitchedFire(f,actor));

        static PlayableEntitySnapshot[] ArtilleryThreats(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot unit)=>
            o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Health>0&&CanHit(e,unit)&&o.Vision?.IsVisible(e.Position)==true&&
                Distance(e.Position,unit.Position)<=PlayableUnitRules.Range(p,e.Kind,e.Upgraded)+RotationLimit(p,ai,d,unit)).ToArray();
        internal bool NeedsArtilleryCover(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot unit)
        {
            if(unit.Kind!=PlayableEntityKind.Shkval||!LocalScreens(o,p,ai,d,unit).Any(GroundScreen))return false;
            var threats=ArtilleryThreats(o,p,ai,d,unit);
            return threats.Length>0&&!Covered(o,p,ai,d,unit,threats,unit.Position);
        }
        AiCombatMicroPlan ArtilleryCover(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyRegistry armies,PlayableEntitySnapshot unit)
        {
            if(V(ai,d,"withdrawEnabled")==0||!NeedsArtilleryCover(o,p,ai,d,unit))return null;
            var army=Army(armies,unit.Id);if(army==null)return null;
            var goal=Withdrawal(o,p,ai,d,unit,ArtilleryThreats(o,p,ai,d,unit));
            return goal.HasValue?new AiCombatMicroPlan{Action=Action(o,PlayableCommandKind.Move,new[]{unit.Id},goal.Value),
                Phase=AiCombatMicroPhase.Withdraw,Army=army.Id,Survival=true}:null;
        }
        static PlayableEntitySnapshot[] RotationAnchors(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,
            AiArmyRegistry registry,PlayableEntitySnapshot unit,long army)
        {
            if(!registry.TryReadGuard(army,out var group)||!ReturnGroup(o.OwnerId,unit.Id,group))return Array.Empty<PlayableEntitySnapshot>();
            // A single compatible ally is sufficient for a local rotation. Repair
            // Follow still uses the existing healthy-core contract.
            var local=LocalScreens(o,p,ai,d,unit).Where(a=>group.ContainsMember(a.Id)&&!group.ContainsReinforcement(a.Id)&&Eligible(a)).ToArray();
            return local.Length>0?local:new[]{unit};
        }
        static bool AirRotationSpaceFree(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot unit,NavPoint point)=>
            Math.Abs(point.X)<=p.ArenaHalfExtent-p.HawkRadius&&Math.Abs(point.Z)<=p.ArenaHalfExtent-p.HawkRadius&&
            o.Entities.Where(a=>a.Id!=unit.Id&&a.Kind==PlayableEntityKind.Hawk&&a.Health>0).All(a=>
                Distance(a.Position,point)>=2*p.HawkRadius+p.HawkSeparation);
        static bool SplashSafe(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot unit,NavPoint point)
        {
            if(unit.Kind==PlayableEntityKind.Hawk)return true;
            double body=PlayableUnitRules.Radius(p,unit.Kind);
            // Markers are already owner-visible facts. Never infer hidden source identity.
            return o.ObservedHostileImpacts.All(marker=>Distance(point,marker.Position)>
                Math.Max(marker.Radius,Math.Max(p.ShkvalBlastRadius,p.ShkvalBaseBlastRadius))+body);
        }
        internal static bool SplashPathSafe(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot unit,NavPoint from,NavPoint to)
        {
            double length=Distance(from,to);int steps=Math.Max(1,(int)Math.Ceiling(length/PlayableUnitRules.Radius(p,unit.Kind)));
            for(int i=0;i<=steps;i++)
                if(!SplashSafe(o,p,unit,new NavPoint(from.X+(to.X-from.X)*i/steps,from.Z+(to.Z-from.Z)*i/steps)))return false;
            return true;
        }
        internal bool RotationWithdrawalSafe(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot unit,NavPoint point)
        {
            var threats=NeedsArtilleryCover(o,p,ai,d,unit)?ArtilleryThreats(o,p,ai,d,unit):Attackers(o,unit,p);
            return threats.Length>0&&Covered(o,p,ai,d,unit,threats,point)&&SplashSafe(o,p,unit,point);
        }
        internal bool RotationReturnSafe(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot unit,NavPoint point)
        {
            if(!SplashPathSafe(o,p,unit,unit.Position,point))return false;
            if(unit.Kind==PlayableEntityKind.Shkval&&LocalScreens(o,p,ai,d,unit).Any(GroundScreen))
                return Covered(o,p,ai,d,unit,ArtilleryThreats(o,p,ai,d,unit),point);
            // Do not return beside the new recipient of an artillery salvo.
            foreach(var enemy in o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Kind==PlayableEntityKind.Shkval&&e.Health>0))
                foreach(var ally in o.Entities.Where(a=>a.Owner==unit.Owner&&a.Id!=unit.Id&&a.Health>0&&CanHit(enemy,a)&&
                    Distance(enemy.Position,a.Position)<=PlayableUnitRules.Range(p,enemy.Kind,enemy.Upgraded)&&
                    fire.Any(f=>f.Source==enemy.Id&&f.Victim==a.Id)))
                    if(unit.Kind!=PlayableEntityKind.Hawk&&Distance(point,ally.Position)<=Blast(p,enemy)+PlayableUnitRules.Radius(p,unit.Kind)&&
                        Distance(point,enemy.Position)<=PlayableUnitRules.Range(p,enemy.Kind,enemy.Upgraded)+Blast(p,enemy))return false;
            return true;
        }
    }
}
