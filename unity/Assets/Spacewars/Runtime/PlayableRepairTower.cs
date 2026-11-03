using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private bool RepairTowerAvailable(Building b)=>!SystemsDisabled(b.Id)&&b.Kind==PlayableBuildingKind.RepairTower&&b.Ready&&b.Health>0&&b.Sale==null&&!eliminated.Contains(b.Owner);
        private bool RepairTowerLegal(Building b,int id)=>units.TryGetValue(id,out var u)&&u.Owner==b.Owner&&u.Health>0&&u.Health<UnitMaximumHealth(u.Owner,u.Kind)&&!wrecks.ContainsKey(id)&&TryActor(id,out var actor)&&Distance(b.Position,actor.Position)<=TurretRange;
        private PlayableRepairTowerSnapshot RepairTowerSnapshot(Building b,PlayableOwner? observer=null)
        {
            if(SystemsDisabled(b.Id)||b.Kind!=PlayableBuildingKind.RepairTower)return null;
            var state=b.Healing;int target=state!=null&&RepairTowerAvailable(b)&&RepairTowerLegal(b,state.Target)?state.Target:0;
            if(target!=0&&observer.HasValue&&TryActor(target,out var at)&&!Vision(observer.Value).IsVisible(at.Position))target=0;
            return new PlayableRepairTowerSnapshot(target,state?.Heading??Math.PI/2,state?.Pitch??0);
        }
        private void AdvanceRepairTowers(double dt)
        {
            // Retained locks are reserved BEFORE new acquisitions, regardless of dictionary/build order.
            // A sorted owner-independent ID tie break makes simultaneous first acquisitions reproducible.
            var towers=buildings.Values.Where(b=>b.Kind==PlayableBuildingKind.RepairTower).OrderBy(b=>b.Id).ToArray();
            var reserved=new HashSet<int>();var retained=new HashSet<int>();
            foreach(var b in towers)
            {
                if(!RepairTowerAvailable(b)){if(b.Healing!=null)b.Healing.Target=0;continue;}
                var state=b.Healing=b.Healing??new RepairTowerState();
                if(RepairTowerLegal(b,state.Target)&&reserved.Add(state.Target))retained.Add(b.Id);
            }
            foreach(var b in towers)
            {
                if(!RepairTowerAvailable(b))continue;
                var state=b.Healing;
                if(!retained.Contains(b.Id))
                {
                    bool priority=!state.Initialized||state.Target!=0;state.Target=0;
                    int chosen=0;double worst=double.PositiveInfinity;
                    foreach(var unit in units.Values)
                    {
                        if(reserved.Contains(unit.Id)||!RepairTowerLegal(b,unit.Id))continue;
                        double health=unit.Health/UnitMaximumHealth(unit.Owner,unit.Kind);
                        if(chosen==0||(priority&&(health<worst||health==worst&&unit.Id<chosen))||(!priority&&unit.Id<chosen)){chosen=unit.Id;worst=health;}
                    }
                    state.Target=chosen;if(chosen!=0)reserved.Add(chosen);
                }
                state.Initialized=true;if(state.Target==0)continue;
                var target=units[state.Target];TryActor(target.Id,out var actor);
                double distance=Distance(b.Position,actor.Position);
                double heading=distance<1e-9?state.Heading:Math.Atan2(actor.Position.Z-b.Position.Z,actor.Position.X-b.Position.X);
                double pitch=Math.Max(-profile.TurretDepression*Math.PI/180,Math.Min(Math.PI/2,Math.Atan2(TargetHeight(target.Id,actor.Position)-GroundHeight(b.Position)-BuildingCombatGeometry.ApronLift-RepairTowerGeometry.PitchHeight*profile.TurretModelScale,distance)));
                state.Heading=Turn(state.Heading,heading,profile.TurretYawSpeed*Math.PI/180*dt);
                state.Pitch=Turn(state.Pitch,pitch,profile.TurretPitchSpeed*Math.PI/180*dt);
                target.Health=Math.Min(UnitMaximumHealth(target.Owner,target.Kind),target.Health+profile.RepairTowerHealthPerSecond*dt);
            }
        }
    }
}
