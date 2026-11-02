using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private PlayableCommandStatus HackControl(PlayableOwner owner,int id,PlayableCommand command)
        {
            if(eliminated.Contains(owner)||!Orbital(owner).CanUse(OrbitalAbilities.ControlHack))return PlayableCommandStatus.Rejected;
            RefreshVision();
            if(!VisibleTarget(owner,id))return PlayableCommandStatus.InvalidTarget;
            double threshold=OrbitalData.orbitalHackHealthPercent/100;
            if(units.TryGetValue(id,out var unit))
            {
                double maximum=UnitMaximumHealth(unit.Owner,unit.Kind);
                if(unit.Health<=0||unit.Health>=maximum*threshold||!TryActor(id,out var actor))return PlayableCommandStatus.InvalidTarget;
                // Admit the route cancellation before spending cooldown or changing ownership.
                var stop=new PlayableCommand(navigation.Generation,command.Sequence,OwnerName(owner),PlayableCommandKind.Stop,new[]{id},origin:command.Origin,source:command.Source,jobId:command.JobId,actionId:command.ActionId);
                if(unit.Air==null){
                    // Replace the old navigation group with a new-owner Stop issuance.
                    // A bare stop retains the previous owner's group provenance in checkpoints.
                    bool stopped=false;navigation.PrepareCommandGroup(stop,nextOrderRevision,Tick,TeamOf(owner));
                    try{stopped=navigation.TryStopGroup(new[]{id},false);}finally{navigation.FinishCommandGroup(stopped,new[]{id});}
                    if(!stopped)return PlayableCommandStatus.Overflow;
                }
                var previous=unit.Owner;var point=actor.Position;
                tacticalQueues.Remove(id);ResetDefaultCombat(unit);InterruptBurst(unit);
                unit.Owner=owner;unit.Health=RescaleCurrentHealth(unit.Health,maximum,UnitMaximumHealth(owner,unit.Kind));
                // A captured scenario actor follows its new owner rather than a pinned authored variant.
                unit.ScenarioIndex=-1;unit.AuthoredUpgrade=null;unit.LastOrder=new PlayableOrderStamp(id,owner,navigation.Generation,nextOrderRevision++,command.Sequence,Tick,stop.Origin,stop.Kind,stop.Source,stop.JobId,stop.ActionId);unit.CurrentOrder=null;unit.Target=0;unit.ExplicitTarget=false;unit.HasAttackMove=false;unit.WreckWaiting=false;unit.PatrolStarted=false;unit.Repath=0;
                actor.Team=TeamOf(owner);
                if(unit.Air!=null){var air=unit.Air;air.Deferred.Clear();air.Short=false;air.Escape=false;air.Departing=false;air.FacingTarget=0;air.Yielding=false;air.ShortRemaining=0;actor.Goal=actor.Position;actor.Moving=false;actor.Held=false;actor.Outcome=NavigationOutcome.Stopped;}
                else navigation.Crowd.SetSpeed(id,PlayableUnitRules.Speed(profile,unit.Kind,unit.Kind==PlayableEntityKind.Tank&&Chassis(owner)));
                MatchEvent(MatchEventKind.UnitCaptured,previous,point);
            }
            else if(buildings.TryGetValue(id,out var mine)&&mine.Kind==PlayableBuildingKind.Mine)
            {
                double maximum=BuildingMaximumHealth(mine.Owner,mine.Kind);
                if(mine.Health<=0||mine.Health>=maximum*threshold)return PlayableCommandStatus.InvalidTarget;
                var previous=mine.Owner;mine.Owner=owner;
                mine.Health=RescaleCurrentHealth(mine.Health,maximum,BuildingMaximumHealth(owner,mine.Kind));
                // Repair/sale were paid and issued by the previous owner; they cannot carry over.
                mine.Repair=null;mine.Sale=null;
                foreach(var key in centerDamage.Where(p=>p.Value.CenterId==id).Select(p=>p.Key).ToArray())centerDamage.Remove(key);
                RecordBuildingCount(previous,mine.Kind);RecordBuildingCount(owner,mine.Kind);
                var site=sites[mine.SiteId];site.Locked=owner;site.Claimant=owner;site.Progress=1;site.Contested=false;
                MatchEvent(MatchEventKind.MineCaptured,previous,mine.Position);
            }
            else return PlayableCommandStatus.InvalidTarget;
            Orbital(owner).Use(OrbitalAbilities.ControlHack,OrbitalData);
            visionInputProfile=null;RefreshVision();
            return PlayableCommandStatus.Applied;
        }
    }
}
