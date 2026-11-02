using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        internal void StartProvingGroundBattle()
        {
            if(offline?.ProvingGround==null)return;
            foreach(var owner in Owners){var ids=units.Values.Where(u=>u.Owner==owner).Select(u=>u.Id).ToArray();if(ids.Length==0)continue;
                // The setup host issues one initial ordinary tactical order; it never runs an economic AI.
                var destination=((ProvingGroundMap)profile.AuthoredMap).AttackDestination(owner,profile);
                bool hadSequence=offlineSequences.TryGetValue(owner,out var previousSequence);
                var command=new PlayableCommand(navigation.Generation,previousSequence+1,OwnerName(owner),PlayableCommandKind.AttackMove,ids,destination);
                // An automatic opening is outside the human ingress sequence stream.
                PlayableCommandStatus result;string message;
                try{result=ApplyWithoutHumanStatistics(command,out message);}
                finally{if(hadSequence)offlineSequences[owner]=previousSequence;else offlineSequences.Remove(owner);}
                if(result!=PlayableCommandStatus.Applied)throw new System.InvalidOperationException("Proving ground attack: "+result+" "+message);
            }
        }
        private void InitializeProvingGroundResearch()
        {
            if(offline?.ProvingGround==null)return;
            foreach(var u in offline.ScenarioUnits){var owner=OwnerFor(offline.Roster.Single(p=>p.LogicalPlayer==u.LogicalPlayer).Id);Fact(owner,MatchFactKind.UnitCompleted,PlayableUnitRules.Cost(profile,u.Kind),(int)u.Kind);}
            foreach(var b in buildings.Values)
                if(offline.ProvingGround.BuildingUpgraded(b.SiteId,b.SlotId)&&b.Kind==PlayableBuildingKind.Refinery)
                    b.Upgrade=new RefineryUpgrade{TermsRevision=profile.Revision,PaidCost=0,Duration=profile.RefineryUpgradeSeconds,Elapsed=profile.RefineryUpgradeSeconds,Complete=true};
            foreach(var b in buildings.Values.Where(b=>(b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Hangar)&&offline.ProvingGround.BuildingUpgraded(b.SiteId,b.SlotId)))
            {
                double duration=ActionDuration(profile.ProducerUpgradeSeconds(b.Kind));
                b.ProducerUpgrade=new RefineryUpgrade{TermsRevision=profile.Revision,PaidCost=profile.ProducerUpgradeCost(b.Kind),Duration=duration,Elapsed=duration,Complete=true};
            }
            foreach(var b in buildings.Values.Where(b=>b.Kind==PlayableBuildingKind.ScientificCenter&&offline.ProvingGround.BuildingUpgraded(b.SiteId,b.SlotId)))
            {
                Research(b.Owner).Add(new ResearchOrder{Id=nextResearchSequence++,Kind=PlayableResearchKind.ScientificCenterUpgrade,CenterId=b.Id,Duration=profile.ScientificCenterUpgradeSeconds,Elapsed=profile.ScientificCenterUpgradeSeconds,Complete=true});
                Fact(b.Owner,MatchFactKind.ResearchCompleted,profile.ScientificCenterUpgradeCost,(int)PlayableResearchKind.ScientificCenterUpgrade);
            }
            foreach(var owner in Owners){ownerCredits[owner]=0;var previousBuildingMaximum=BuildingMaxima(owner);
                foreach(var kind in offline.ProvingGround.Research((int)owner)){Research(owner).Add(new ResearchOrder{Id=nextResearchSequence++,Kind=kind,Complete=true});Fact(owner,MatchFactKind.ResearchCompleted,ResearchCost(kind),(int)kind);}
                ScaleBuildingHealth(owner,previousBuildingMaximum);}
        }
        internal ProvingGroundSetup CaptureProvingSetup()
        {
            var setup=new ProvingGroundSetup(profile);
            foreach(var u in units.Values)if(TryActor(u.Id,out var actor))setup.Units.Add(new OfflineScenarioUnit((int)u.Owner+1,u.Kind,actor.Position,actor.Heading));
            foreach(var b in buildings.Values.Where(b=>b.SlotId!=0&&b.Ready)){setup.Buildings.Add(new OfflineScenarioBuilding((int)b.Owner+1,b.Kind,b.SiteId,b.SlotId,b.Position,sites[b.SiteId].Site.Slots.Concat(sites[b.SiteId].Site.DefenseSlots(profile)).Single(s=>s.Id==b.SlotId).Heading));if(b.Upgrade?.Complete==true||b.ProducerUpgrade?.Complete==true||CenterUpgraded(b.Id))setup.UpgradeBuilding(b.SiteId,b.SlotId);}
            foreach(var owner in Owners)foreach(var order in Research(owner).Where(o=>o.Complete&&!PlayableResearchRules.Local(o.Kind)))setup.SetResearch((int)owner,order.Kind,true);
            return setup;
        }
        internal double ProvingCost(int side,MatchFactKind kind)=>matchFacts.Where(f=>(int)f.Owner==side&&f.Kind==kind).Sum(f=>f.Value);
        internal string PlaceProvingUnit(int side,PlayableEntityKind kind,NavPoint point)
        {
            if(!FreeProvingGround||side<0||side>1)return "Недоступная расстановка";
            var recipe=new ProvingGroundSetup(profile);
            foreach(var u in units.Values)if(TryActor(u.Id,out var actor))recipe.Units.Add(new OfflineScenarioUnit((int)u.Owner+1,u.Kind,actor.Position,actor.Heading));
            foreach(var b in buildings.Values.Where(b=>b.SlotId!=0))recipe.Buildings.Add(new OfflineScenarioBuilding((int)b.Owner+1,b.Kind,b.SiteId,b.SlotId,b.Position));
            var error=recipe.PlaceUnit(side,kind,point);if(error!=null)return error;
            var owner=(PlayableOwner)side;var population=Population(owner);
            if(population.Living+population.Reserved+PlayableUnitRules.Population(profile,kind)>population.Capacity)return "Недостаточно места в армии";
            if(kind!=PlayableEntityKind.Hawk&&!navigation.Crowd.CanPlace(point,PlayableUnitRules.Radius(profile,kind)))return "Место занято";
            try{int id=SpawnUnit(point,owner,kind);if(TryActor(id,out var actor))actor.Heading=side==0?0:System.Math.PI;units[id].Turret=side==0?0:System.Math.PI;
                Fact(owner,MatchFactKind.UnitCompleted,PlayableUnitRules.Cost(profile,kind),(int)kind);}
            catch(NavigationRetentionBudget.CapacityRefused){return "Расстановка временно недоступна";}
            return null;
        }
        internal string SetProvingResearch(int side,PlayableResearchKind kind,bool enabled)
        {
            if(!FreeProvingGround||side<0||side>1||!Enum.IsDefined(typeof(PlayableResearchKind),kind)||PlayableResearchRules.Local(kind))return "Недоступное исследование";
            var owner=(PlayableOwner)side;var orders=Research(owner);var desired=new ProvingGroundSetup(profile);
            foreach(var order in orders.Where(o=>o.Complete&&!PlayableResearchRules.Local(o.Kind)))desired.SetResearch(side,order.Kind,true);
            desired.SetResearch(side,kind,enabled);var selected=desired.Research(side);
            bool Affected(PlayableResearchKind candidate){if(candidate==kind)return true;for(var prev=PlayableResearchRules.Previous(candidate);prev.HasValue;prev=PlayableResearchRules.Previous(prev.Value))if(prev.Value==kind)return true;return false;}
            var maxima=units.Values.Where(u=>u.Owner==owner).ToDictionary(u=>u.Id,u=>UnitMaximumHealth(owner,u.Kind));
            var previousBuildingMaximum=BuildingMaxima(owner);
            orders.RemoveAll(o=>!PlayableResearchRules.Local(o.Kind)&&(!o.Complete&&(selected.Contains(o.Kind)||!enabled&&Affected(o.Kind))||o.Complete&&!selected.Contains(o.Kind)));
            foreach(var chosen in selected)if(!HasResearch(owner,chosen))orders.Add(new ResearchOrder{Id=nextResearchSequence++,Kind=chosen,Complete=true});
            foreach(var unit in units.Values.Where(u=>u.Owner==owner)){
                unit.Health=RescaleCurrentHealth(unit.Health,maxima[unit.Id],UnitMaximumHealth(owner,unit.Kind));
                if(unit.Air==null)navigation.Crowd.SetSpeed(unit.Id,PlayableUnitRules.Speed(profile,unit.Kind,unit.Kind==PlayableEntityKind.Tank&&UnitUpgraded(unit)));
            }
            ScaleBuildingHealth(owner,previousBuildingMaximum);
            StartResearchIfPossible(owner);return null;
        }
        // Local building upgrades remain historical investments; global toggles track the current selection.
        internal double CurrentProvingResearchCost(int side)=>Research((PlayableOwner)side).Where(o=>o.Complete&&!PlayableResearchRules.Local(o.Kind)).Sum(o=>ResearchCost(o.Kind))+matchFacts.Where(f=>(int)f.Owner==side&&f.Kind==MatchFactKind.ResearchCompleted&&PlayableResearchRules.Local((PlayableResearchKind)f.EntityKind)).Sum(f=>f.Value);
        internal void RemoveProvingUnits(int side,bool all)
        {
            if(!FreeProvingGround)return;
            var ids=units.Values.Where(u=>(int)u.Owner==side).OrderByDescending(u=>u.Id).Select(u=>u.Id).ToArray();
            foreach(var id in all?ids:ids.Take(1))DamageFromProjectile(id,double.MaxValue,0);
        }
    }
    public sealed class ProvingGroundCost
    {
        public double Army{get;} public double Research{get;}
        internal ProvingGroundCost(double army,double research){Army=army;Research=research;}
    }
    public sealed partial class PlayableRuntime
    {
        private sealed class ProvingEdit
        {internal int Side;internal PlayableEntityKind Kind;internal NavPoint Point;internal int Remove;internal PlayableResearchKind? Research;internal bool Enabled;}
        private readonly Queue<ProvingEdit> provingEdits=new Queue<ProvingEdit>();
        private ProvingGroundSetup provingResetSetup;
        public ProvingGroundSetup ProvingGroundResetSetup=>Volatile.Read(ref provingResetSetup)?.Copy();
        private ProvingGroundCost[] provingCosts={new ProvingGroundCost(0,0),new ProvingGroundCost(0,0)};
        private string provingEditStatus;
        public string ProvingGroundEditStatus=>Volatile.Read(ref provingEditStatus);
        public ProvingGroundCost ProvingGroundCosts(int side)=>Volatile.Read(ref provingCosts)[side];
        public bool PlaceProvingGroundUnit(int side,PlayableEntityKind kind,NavPoint point)=>EnqueueProvingEdit(new ProvingEdit{Side=side,Kind=kind,Point=point});
        public bool SetProvingGroundResearch(int side,PlayableResearchKind kind,bool enabled)=>EnqueueProvingEdit(new ProvingEdit{Side=side,Research=kind,Enabled=enabled});
        public bool RemoveProvingGroundUnits(int side,bool all)=>EnqueueProvingEdit(new ProvingEdit{Side=side,Remove=all?2:1});
        // Trusted local sandbox editor; ordinary human ingress retains its ownership policy.
        public bool EditProvingGround(PlayableCommand command)
        {
            if(command==null||command.Generation!=Generation||command.Source!=null||command.JobId!=0||command.ActionId!=0||command.Origin==PlayableOrderOrigin.Ai)return false;
            int side=command.PlayerId=="player-1"?0:command.PlayerId=="enemy-1"?1:-1;
            switch(command.Kind){case PlayableCommandKind.BuildAt:case PlayableCommandKind.CancelBuilding:case PlayableCommandKind.SellBuilding:case PlayableCommandKind.UpgradeRefinery:case PlayableCommandKind.UpgradeProducer:case PlayableCommandKind.QueueTank:case PlayableCommandKind.QueueExplorer:case PlayableCommandKind.QueueShkval:case PlayableCommandKind.QueueHawk:case PlayableCommandKind.QueueResearch:case PlayableCommandKind.CancelResearch:case PlayableCommandKind.CancelProductionOrder:break;default:return false;}
            return side>=0&&Submit(command,provingEditor:true).Accepted;
        }
        private bool EnqueueProvingEdit(ProvingEdit edit)
        {
            lock(gate){if(offlineConfiguration?.ProvingGround==null||edit.Side<0||edit.Side>1||stopping!=0||stopped!=0||paused!=0||provingGroundStart!=0||provingEdits.Count>=MaxOutstandingCommands)return false;provingEdits.Enqueue(edit);}
            Signal();return true;
        }
        private void ApplyProvingGroundEdits(bool isPaused)
        {
            if(isPaused)return;
            ProvingEdit[] edits;lock(gate){edits=provingEdits.ToArray();provingEdits.Clear();}
            foreach(var edit in edits){string message=null;
                if(edit.Research.HasValue)message=domain.SetProvingResearch(edit.Side,edit.Research.Value,edit.Enabled);
                else if(edit.Remove!=0)domain.RemoveProvingUnits(edit.Side,edit.Remove==2);
                else message=domain.PlaceProvingUnit(edit.Side,edit.Kind,edit.Point);
                Volatile.Write(ref provingEditStatus,message);PresentationChanged();}
        }
        private void PublishProvingGroundCosts()
        {
            if(offlineConfiguration?.ProvingGround==null)return;
            if(provingGroundStart==0)Volatile.Write(ref provingResetSetup,domain.CaptureProvingSetup());
            Volatile.Write(ref provingCosts,Enumerable.Range(0,2).Select(side=>new ProvingGroundCost(domain.ProvingCost(side,MatchFactKind.UnitCompleted),domain.CurrentProvingResearchCost(side))).ToArray());
        }
    }
}
