using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private const int ProvingOpeningStateMarker=0x50524731;
        private int[] provingOpeningGroundUnits;
        private void WriteProvingOpening(System.IO.BinaryWriter writer)
        {
            if(provingOpeningGroundUnits==null)return;
            writer.Write(ProvingOpeningStateMarker);
            WorldWire.Array(writer,provingOpeningGroundUnits,id=>writer.Write(id));
        }
        private void ReadProvingOpening(System.IO.BinaryReader reader)
        {
            long start=reader.BaseStream.Position;
            if(start==reader.BaseStream.Length)return;
            if(reader.ReadInt32()!=ProvingOpeningStateMarker){reader.BaseStream.Position=start;return;}
            var ids=WorldWire.Array(reader,reader.ReadInt32);
            if(offline?.ProvingGround==null||ids.Length==0||ids.Length!=ids.Distinct().Count()||
                ids.Any(id=>!units.TryGetValue(id,out var unit)||unit.Air!=null))
                throw new ArgumentException("Invalid Proving Ground opening.");
            provingOpeningGroundUnits=ids;
        }
        internal bool ProvingOpeningPending=>provingOpeningGroundUnits!=null;
#if UNITY_INCLUDE_TESTS
        internal Func<bool> RotationOpeningSubmittedForTests;
        internal void AwaitRotationOpeningForTests(int[] groundActors,Func<bool> submitted)
        {
            if(Tick!=0||provingOpeningGroundUnits!=null||offline?.ProvingGround==null)throw new InvalidOperationException("Rotation opening requires a new paused Proving Ground.");
            RotationOpeningSubmittedForTests=submitted;
            provingOpeningGroundUnits=groundActors;
        }
#endif
        internal bool AdvanceProvingOpening()
        {
            if(provingOpeningGroundUnits==null)return true;
#if UNITY_INCLUDE_TESTS
            if(RotationOpeningSubmittedForTests?.Invoke()==false)return false;
#endif
            var port=navigation.RoutePort;
            try
            {
                using var turn=port.TryAcquireResultPreparation(NavigationResultPreparationCancelled);
                if(turn!=null)navigation.ApplyResults();
            }
            finally{port.FinishResultPreparationTurn(false);}
            if(provingOpeningGroundUnits.Any(navigation.IsPending))
            {
                // Route credits belong to authority ticks. Motion, combat and AI
                // start together once the opening's last route has settled.
                Tick++;
                return false;
            }
            provingOpeningGroundUnits=null;
            return true;
        }
        internal void StartProvingGroundBattle()
        {
            if(offline?.ProvingGround==null)return;
            var map=(ProvingGroundMap)profile.AuthoredMap;
            var left=ProvingOpeningFormation(PlayableOwner.Player);
            var right=ProvingOpeningFormation(PlayableOwner.Enemy);
            // Keep the ordinary command's logical destination at the opposing HQ.
            // Shared navigation owns free approach slots and obstacle avoidance.
            if(left.Length>0)IssueProvingOpening(PlayableOwner.Player,left,map.AttackDestination(PlayableOwner.Player,profile));
            if(right.Length>0)IssueProvingOpening(PlayableOwner.Enemy,right,map.AttackDestination(PlayableOwner.Enemy,profile));
            var waiting=units.Values.Where(u=>u.Air==null&&navigation.IsPending(u.Id)).Select(u=>u.Id).OrderBy(id=>id).ToArray();
            provingOpeningGroundUnits=waiting.Length==0?null:waiting;
        }
        private int[] ProvingOpeningFormation(PlayableOwner owner)
        {
            double sign=owner==PlayableOwner.Player?1:-1;
            return units.Values.Where(u=>u.Owner==owner).Select(u=>(Unit:u,Position:TryActor(u.Id,out var actor)?actor.Position:default))
                .OrderBy(x=>x.Position.Z*sign).ThenBy(x=>x.Position.X*sign).ThenBy(x=>x.Unit.Kind).Select(x=>x.Unit.Id).ToArray();
        }
        private void IssueProvingOpening(PlayableOwner owner,int[] ids,NavPoint destination)
        {
            bool hadSequence=offlineSequences.TryGetValue(owner,out long previous);
            var command=new PlayableCommand(navigation.Generation,Math.Max(previous+1,nextAiSequence),OwnerName(owner),PlayableCommandKind.AttackMove,ids,destination);
            PlayableCommandStatus result;string message;
            try{result=ApplyWithoutHumanStatistics(command,out message);}
            finally{if(hadSequence)offlineSequences[owner]=previous;else offlineSequences.Remove(owner);}
            if(result!=PlayableCommandStatus.Applied)throw new InvalidOperationException("Proving ground attack: "+result+" "+message);
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
            if(!PlayableUnitRules.Aircraft(kind)&&!navigation.Crowd.CanPlace(point,PlayableUnitRules.Radius(profile,kind)))return "Место занято";
            try{int id=SpawnUnit(point,owner,kind);if(TryActor(id,out var actor))actor.Heading=side==0?0:System.Math.PI;units[id].Turret=side==0?0:System.Math.PI;
                Fact(owner,MatchFactKind.UnitCompleted,PlayableUnitRules.Cost(profile,kind),(int)kind);}
            catch(NavigationRetentionBudget.CapacityRefused){return "Расстановка временно недоступна";}
            return null;
        }
        internal string SetProvingResearch(int side,PlayableResearchKind kind,bool enabled)
        {
            if(!FreeProvingGround||side<0||side>1||!Enum.IsDefined(typeof(PlayableResearchKind),kind)||PlayableResearchRules.Local(kind))return "Недоступное исследование";
            var desired=CurrentProvingResearchSelection(side);
            desired.SetResearch(side,kind,enabled);
            bool Affected(PlayableResearchKind candidate){if(candidate==kind)return true;for(var prev=PlayableResearchRules.Previous(candidate);prev.HasValue;prev=PlayableResearchRules.Previous(prev.Value))if(prev.Value==kind)return true;return false;}
            return ApplyProvingResearchSelection(side,desired,candidate=>!enabled&&Affected(candidate));
        }
        private ProvingGroundSetup CurrentProvingResearchSelection(int side)
        {
            var desired=new ProvingGroundSetup(profile);
            foreach(var order in Research((PlayableOwner)side).Where(o=>o.Complete&&!PlayableResearchRules.Local(o.Kind)))desired.SetResearch(side,order.Kind,true);
            return desired;
        }
        internal string SelectProvingResearch(int side,PlayableResearchKind root,PlayableResearchKind? selectedKind)
        {
            if(!FreeProvingGround||side<0||side>1||!Enum.IsDefined(typeof(PlayableResearchKind),root)||PlayableResearchRules.Local(root)||PlayableResearchRules.Previous(root).HasValue)return "Недоступное исследование";
            bool InChain(PlayableResearchKind kind){for(PlayableResearchKind? current=kind;current.HasValue;current=PlayableResearchRules.Previous(current.Value))if(current.Value==root)return true;return false;}
            if(selectedKind.HasValue&&(!Enum.IsDefined(typeof(PlayableResearchKind),selectedKind.Value)||!InChain(selectedKind.Value)))return "Недоступная ступень";
            var desired=CurrentProvingResearchSelection(side);desired.SetResearch(side,root,false);
            if(selectedKind.HasValue)desired.SetResearch(side,selectedKind.Value,true);
            return ApplyProvingResearchSelection(side,desired,InChain);
        }
        private string ApplyProvingResearchSelection(int side,ProvingGroundSetup desired,Func<PlayableResearchKind,bool> cancelPending)
        {
            var owner=(PlayableOwner)side;bool hadGuidance=Guidance(owner);var orders=Research(owner);var selected=desired.Research(side);
            var maxima=units.Values.Where(u=>u.Owner==owner).ToDictionary(u=>u.Id,u=>UnitMaximumHealth(owner,u.Kind));
            var previousBuildingMaximum=BuildingMaxima(owner);
            orders.RemoveAll(o=>!PlayableResearchRules.Local(o.Kind)&&(!o.Complete&&(selected.Contains(o.Kind)||cancelPending(o.Kind))||o.Complete&&!selected.Contains(o.Kind)));
            foreach(var chosen in selected)if(!HasResearch(owner,chosen))orders.Add(new ResearchOrder{Id=nextResearchSequence++,Kind=chosen,Complete=true});
            bool hasGuidance=Guidance(owner);
            if(hadGuidance!=hasGuidance)RescaleShkvalGuidanceTimers(owner,hasGuidance);
            foreach(var unit in units.Values.Where(u=>u.Owner==owner)){
                unit.Health=RescaleCurrentHealth(unit.Health,maxima[unit.Id],UnitMaximumHealth(owner,unit.Kind));
                if(unit.Air==null)navigation.Crowd.SetSpeed(unit.Id,PlayableUnitRules.Speed(profile,unit.Kind,unit.Kind==PlayableEntityKind.Tank&&UnitUpgraded(unit)));
            }
            ScaleBuildingHealth(owner,previousBuildingMaximum);
            StartResearchIfPossible(owner);return null;
        }
        internal PlayableResearchKind[] SelectedProvingResearch(int side)=>Research((PlayableOwner)side).Where(o=>!PlayableResearchRules.Local(o.Kind)).Select(o=>o.Kind).ToArray();
        // Local building upgrades remain historical investments; global selectors track the current selection.
        internal double CurrentProvingResearchCost(int side)=>Research((PlayableOwner)side).Where(o=>o.Complete&&!PlayableResearchRules.Local(o.Kind)).Sum(o=>ResearchCost(o.Kind))+matchFacts.Where(f=>(int)f.Owner==side&&f.Kind==MatchFactKind.ResearchCompleted&&PlayableResearchRules.Local((PlayableResearchKind)f.EntityKind)).Sum(f=>f.Value);
        internal void RemoveProvingUnits(int side,bool all)
        {
            if(!FreeProvingGround)return;
            var ids=units.Values.Where(u=>(int)u.Owner==side).OrderByDescending(u=>u.Id).Select(u=>u.Id).ToArray();
            foreach(var id in all?ids:ids.Take(1))DamageFromProjectile(id,double.MaxValue,0);
        }
        internal void RemoveProvingSelection(int[] ids)
        {
            if(!FreeProvingGround||ids==null)return;
            foreach(var id in ids.Distinct().Where(units.ContainsKey).ToArray())DamageFromProjectile(id,double.MaxValue,0);
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
        {internal int Side;internal PlayableEntityKind Kind;internal NavPoint Point;internal int Remove;internal PlayableResearchKind? Research;internal bool Enabled;internal int[] SelectedUnits;internal bool ExclusiveResearchChoice;internal PlayableResearchKind? ResearchChoice;}
        private readonly Queue<ProvingEdit> provingEdits=new Queue<ProvingEdit>();
        private ProvingGroundSetup provingResetSetup;
        public ProvingGroundSetup ProvingGroundResetSetup=>Volatile.Read(ref provingResetSetup)?.Copy();
        private ProvingGroundCost[] provingCosts={new ProvingGroundCost(0,0),new ProvingGroundCost(0,0)};
        private string provingEditStatus;
        private int pendingProvingEdits,completedProvingEdits;
        private PlayableResearchKind[][] provingResearchSelection={Array.Empty<PlayableResearchKind>(),Array.Empty<PlayableResearchKind>()};
        public bool ProvingGroundEditsPending=>Volatile.Read(ref pendingProvingEdits)>0;
        public IReadOnlyList<PlayableResearchKind> ProvingGroundResearchSelection(int side)=>Array.AsReadOnly(Volatile.Read(ref provingResearchSelection)[side]);
        public bool SelectProvingGroundResearch(int side,PlayableResearchKind root,PlayableResearchKind? selectedKind)=>EnqueueProvingEdit(new ProvingEdit{Side=side,Research=root,ExclusiveResearchChoice=true,ResearchChoice=selectedKind});
        public string ProvingGroundEditStatus=>Volatile.Read(ref provingEditStatus);
        public ProvingGroundCost ProvingGroundCosts(int side)=>Volatile.Read(ref provingCosts)[side];
        public bool PlaceProvingGroundUnit(int side,PlayableEntityKind kind,NavPoint point)=>EnqueueProvingEdit(new ProvingEdit{Side=side,Kind=kind,Point=point});
        public bool SetProvingGroundResearch(int side,PlayableResearchKind kind,bool enabled)=>EnqueueProvingEdit(new ProvingEdit{Side=side,Research=kind,Enabled=enabled});
        public bool RemoveProvingGroundUnits(int side,bool all)=>EnqueueProvingEdit(new ProvingEdit{Side=side,Remove=all?2:1});
        public bool RemoveProvingGroundSelection(int[] ids)=>ids!=null&&ids.Length>0&&EnqueueProvingEdit(new ProvingEdit{SelectedUnits=ids.Distinct().ToArray()});
        // Trusted local sandbox editor; ordinary human ingress retains its ownership policy.
        public bool EditProvingGround(PlayableCommand command)
        {
            if(command==null||command.Generation!=Generation||command.Source!=null||command.JobId!=0||command.ActionId!=0||command.Origin==PlayableOrderOrigin.Ai)return false;
            int side=command.PlayerId=="player-1"?0:command.PlayerId=="enemy-1"?1:-1;
            switch(command.Kind){case PlayableCommandKind.BuildAt:case PlayableCommandKind.CancelBuilding:case PlayableCommandKind.SellBuilding:case PlayableCommandKind.UpgradeRefinery:case PlayableCommandKind.UpgradeProducer:case PlayableCommandKind.QueueTank:case PlayableCommandKind.QueueExplorer:case PlayableCommandKind.QueueIgla:case PlayableCommandKind.QueueCerberus:case PlayableCommandKind.QueueShkval:case PlayableCommandKind.QueueHawk:case PlayableCommandKind.QueueSkat:case PlayableCommandKind.QueueResearch:case PlayableCommandKind.CancelResearch:case PlayableCommandKind.CancelProductionOrder:break;default:return false;}
            return side>=0&&Submit(command,provingEditor:true).Accepted;
        }
        private bool EnqueueProvingEdit(ProvingEdit edit)
        {
            lock(gate){if(offlineConfiguration?.ProvingGround==null||edit.Side<0||edit.Side>1||stopping!=0||stopped!=0||paused!=0||provingGroundStart!=0||provingEdits.Count>=MaxOutstandingCommands)return false;provingEdits.Enqueue(edit);Interlocked.Increment(ref pendingProvingEdits);}
            Signal();return true;
        }
        private void ApplyProvingGroundEdits(bool isPaused)
        {
            if(isPaused)return;
            ProvingEdit[] edits;lock(gate){edits=provingEdits.ToArray();provingEdits.Clear();}
            foreach(var edit in edits){string message=null;
                if(edit.SelectedUnits!=null)domain.RemoveProvingSelection(edit.SelectedUnits);
                else if(edit.ExclusiveResearchChoice)message=domain.SelectProvingResearch(edit.Side,edit.Research.Value,edit.ResearchChoice);
                else if(edit.Research.HasValue)message=domain.SetProvingResearch(edit.Side,edit.Research.Value,edit.Enabled);
                else if(edit.Remove!=0)domain.RemoveProvingUnits(edit.Side,edit.Remove==2);
                else message=domain.PlaceProvingUnit(edit.Side,edit.Kind,edit.Point);
                Volatile.Write(ref provingEditStatus,message);PresentationChanged();}
            completedProvingEdits+=edits.Length;
        }
        private void PublishProvingGroundCosts()
        {
            if(offlineConfiguration?.ProvingGround==null)return;
            if(provingGroundStart==0)Volatile.Write(ref provingResetSetup,domain.CaptureProvingSetup());
            Volatile.Write(ref provingCosts,Enumerable.Range(0,2).Select(side=>new ProvingGroundCost(domain.ProvingCost(side,MatchFactKind.UnitCompleted),domain.CurrentProvingResearchCost(side))).ToArray());
            Volatile.Write(ref provingResearchSelection,Enumerable.Range(0,2).Select(domain.SelectedProvingResearch).ToArray());
            // UI may release optimistic levels only after the matching selection is published.
            int completed=completedProvingEdits;completedProvingEdits=0;
            if(completed>0)Interlocked.Add(ref pendingProvingEdits,-completed);
        }
    }
}
