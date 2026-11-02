using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private const int MaximumResearchOrders=6;
        private sealed class ResearchOrder { public long Id; public PlayableResearchKind Kind; public int CenterId; public double PaidCost,Elapsed,Duration; public bool Active,Complete; }
        private readonly Dictionary<PlayableOwner,List<ResearchOrder>> research=new Dictionary<PlayableOwner,List<ResearchOrder>>();
        private long nextResearchSequence=1;
        private List<ResearchOrder> Research(PlayableOwner owner)
        {
            if(!research.TryGetValue(owner,out var orders))research.Add(owner,orders=new List<ResearchOrder>());
            return orders;
        }
        private bool HasResearch(PlayableOwner owner,PlayableResearchKind kind)=>research.TryGetValue(owner,out var orders)&&orders.Any(o=>o.Kind==kind&&o.Complete);
        private bool CenterUpgraded(int id)=>buildings.TryGetValue(id,out var b)&&b.Kind==PlayableBuildingKind.ScientificCenter&&research.TryGetValue(b.Owner,out var orders)&&orders.Any(o=>PlayableResearchRules.Local(o.Kind)&&o.CenterId==id&&o.Complete);
        private bool OrderedAt(PlayableOwner owner,PlayableResearchKind kind,int centerId)=>Research(owner).Any(o=>o.Kind==kind&&(!PlayableResearchRules.Local(kind)||o.CenterId==centerId));
        private bool LegalResearchCenter(int id,PlayableOwner owner,PlayableResearchKind kind)=>ReadyResearchCenter(id,owner)&&(!PlayableResearchRules.NeedsUpgradedCenter(kind)||CenterUpgraded(id));
        private bool HasActiveResearch(PlayableOwner owner,PlayableResearchKind kind)=>research.TryGetValue(owner,out var orders)&&orders.Any(o=>o.Kind==kind&&!o.Complete);
        private bool ResearchProducerReady(PlayableOwner owner,PlayableResearchKind kind)
        {
            var producer=PlayableResearchRules.Producer(kind);
            return !producer.HasValue||buildings.Values.Any(b=>b.Owner==owner&&b.Kind==producer.Value&&b.Ready&&b.Health>0&&b.Sale==null);
        }
        private double ResearchCost(PlayableResearchKind kind)=>PlayableResearchRules.Cost(profile,kind);
        private double ResearchDuration(PlayableResearchKind kind)=>ActionDuration(PlayableResearchRules.Duration(profile,kind));
        private PlayableCommandStatus QueueResearch(int centerId,PlayableOwner owner,PlayableResearchKind kind,out string message)
        {
            message="Нужен готовый собственный Научный центр";
            if(!buildings.TryGetValue(centerId,out var center)||center.Owner!=owner||center.Kind!=PlayableBuildingKind.ScientificCenter||!center.Ready||center.Sale!=null)return PlayableCommandStatus.InvalidEntity;
            if(!Enum.IsDefined(typeof(PlayableResearchKind),kind)){message="Неизвестное исследование";return PlayableCommandStatus.Rejected;}
            if(!ResearchProducerReady(owner,kind)){message=PlayableResearchRules.ProducerRequirement(kind);return PlayableCommandStatus.Rejected;}
            if(PlayableResearchRules.CenterRequirement(kind,CenterUpgraded(centerId)) is string requirement){message=requirement;return PlayableCommandStatus.Rejected;}
            var previous=PlayableResearchRules.Previous(kind);if(!FreeProvingGround&&previous.HasValue&&!HasResearch(owner,previous.Value)){message="Сначала завершите предыдущий уровень";return PlayableCommandStatus.Rejected;}
            var orders=Research(owner);if(OrderedAt(owner,kind,centerId)){message="Исследование уже заказано";return PlayableCommandStatus.Rejected;}
            bool includePrevious=FreeProvingGround&&previous.HasValue&&!HasResearch(owner,previous.Value)&&!HasActiveResearch(owner,previous.Value);
            int chainCount=1;
            if(includePrevious)for(var prerequisite=previous;prerequisite.HasValue&&!HasResearch(owner,prerequisite.Value)&&!HasActiveResearch(owner,prerequisite.Value);prerequisite=PlayableResearchRules.Previous(prerequisite.Value))chainCount++;
            if(orders.Count(o=>!o.Complete&&o.CenterId==centerId)+chainCount>MaximumResearchOrders){message="Очередь исследований заполнена";return PlayableCommandStatus.Overflow;}
            if(!CanAfford(owner,ResearchCost(kind))){message="Недостаточно кредитов";return PlayableCommandStatus.InsufficientCredits;}
            // A sandbox level-II selection is one prerequisite chain. Capacity is
            // reserved for both entries before admitting either one.
            if(includePrevious){var status=QueueResearch(centerId,owner,previous.Value,out message);if(status!=PlayableCommandStatus.Applied)return status;}
            orders.Add(new ResearchOrder{Id=nextResearchSequence++,Kind=kind,CenterId=centerId});
            Fact(owner,MatchFactKind.ResearchOrdered,ResearchCost(kind),(int)kind,kind.ToString());
            Sound(PlayableSoundKind.ResearchQueued,center.Position,PlayableEntityKind.Tank,owner,true);
            StartResearchIfPossible(owner);
            message="Исследование добавлено в очередь";return PlayableCommandStatus.Applied;
        }
        private PlayableCommandStatus CancelResearch(int centerId,long id,PlayableOwner owner,out string message)
        {
            message="Нет активного исследования";var orders=Research(owner);var order=orders.FirstOrDefault(o=>o.Id==id&&o.Active&&!o.Complete);
            if(order==null||order.CenterId!=centerId||!ReadyResearchCenter(centerId,owner))return PlayableCommandStatus.InvalidTarget;
            AddCredits(owner,order.PaidCost);orders.Remove(order);Sound(PlayableSoundKind.ResearchCancelled,buildings[centerId].Position,PlayableEntityKind.Tank,owner,true);message="Исследование отменено: полный возврат";StartResearchIfPossible(owner);return PlayableCommandStatus.Applied;
        }
        private void StartResearchIfPossible(PlayableOwner owner)
        {
            var orders=Research(owner);
            if(FreeProvingGround)orders.RemoveAll(o=>!o.Complete&&PlayableResearchRules.Previous(o.Kind) is PlayableResearchKind prerequisite&&!orders.Any(p=>p.Kind==prerequisite));
            // Preserve unpaid orders after center loss, as before. Rehome in order
            // only where the destination has capacity; completed/paid jobs never move.
            foreach(var waiting in orders.Where(o=>!o.Active&&!o.Complete&&!ReadyResearchCenter(o.CenterId,owner)).OrderBy(o=>o.Id))
            {
                if(PlayableResearchRules.Local(waiting.Kind))continue;
                var destination=buildings.Values.Where(b=>LegalResearchCenter(b.Id,owner,waiting.Kind)&&orders.Count(o=>!o.Complete&&o.CenterId==b.Id)<MaximumResearchOrders).OrderBy(b=>b.Id).FirstOrDefault();
                if(destination!=null)waiting.CenterId=destination.Id;
            }
            // Global technology ownership, independent center queues. Oldest heads get
            // first access to scarce credits; one blocked center cannot stall another.
            var heads=orders.Where(o=>!o.Complete).GroupBy(o=>o.CenterId)
                .Where(g=>!g.Any(o=>o.Active)).Select(g=>g.OrderBy(o=>o.Id).First()).OrderBy(o=>o.Id).ToArray();
            foreach(var next in heads)
            {
                var previous=PlayableResearchRules.Previous(next.Kind);
                if(previous.HasValue&&!HasResearch(owner,previous.Value))continue;
                if(!LegalResearchCenter(next.CenterId,owner,next.Kind)||!ResearchProducerReady(owner,next.Kind))continue;
                double cost=ResearchCost(next.Kind);if(!CanAfford(owner,cost))continue;
                var center=buildings[next.CenterId];
                AddCredits(owner,-cost);next.PaidCost=cost;next.Duration=ResearchDuration(next.Kind);next.Elapsed=0;next.Active=true;
                Sound(PlayableSoundKind.ResearchStarted,center.Position,PlayableEntityKind.Tank,owner,true);
            }
        }
        private bool ReadyResearchCenter(int id,PlayableOwner owner)=>buildings.TryGetValue(id,out var center)&&center.Owner==owner&&center.Kind==PlayableBuildingKind.ScientificCenter&&center.Ready&&center.Health>0&&center.Sale==null;
        private void AdvanceResearch(double dt)
        {
            foreach(PlayableOwner owner in Owners)
            {
                if(eliminated.Contains(owner))continue;
                Research(owner).RemoveAll(o=>!o.Complete&&(o.Active||PlayableResearchRules.Local(o.Kind))&&!ReadyResearchCenter(o.CenterId,owner));
                // Snapshot this tick's active jobs; a newly started successor never
                // receives the elapsed time already credited to its predecessor.
                foreach(var active in Research(owner).Where(o=>o.Active&&!o.Complete).OrderBy(o=>o.Id).ToArray())
                {
                    active.Elapsed=Math.Min(active.Duration,active.Elapsed+dt);
                    if(active.Duration-active.Elapsed>2.220446049250313e-16*active.Duration*128)continue;
                    active.Elapsed=active.Duration;
                    var previousMaximum=units.Values.Where(u=>u.Owner==owner).ToDictionary(u=>u.Id,u=>UnitMaximumHealth(owner,u.Kind));
                    var previousBuildingMaximum=BuildingMaxima(owner);
                    active.Active=false;active.Complete=true;
                    Fact(owner,MatchFactKind.ResearchCompleted,ResearchCost(active.Kind),(int)active.Kind,PlayableResearchRules.Description(profile,active.Kind));
                    if(PlayableResearchRules.Concrete(active.Kind))ScaleBuildingHealth(owner,previousBuildingMaximum);
                    if(PlayableResearchRules.Defense(active.Kind)||active.Kind==PlayableResearchKind.TankChassis)
                        foreach(var unit in units.Values)if(unit.Owner==owner)unit.Health=RescaleCurrentHealth(unit.Health,previousMaximum[unit.Id],UnitMaximumHealth(owner,unit.Kind));
                    var center=buildings[active.CenterId];Sound(PlayableSoundKind.ResearchComplete,center.Position,PlayableEntityKind.Tank,owner,true);
                    if(active.Kind==PlayableResearchKind.TankChassis)
                        foreach(var u in units.Values)if(u.Owner==owner&&u.Kind==PlayableEntityKind.Tank&&!u.AuthoredUpgrade.HasValue)navigation.Crowd.SetSpeed(u.Id,profile.TankChassisSpeed);
                }
                StartResearchIfPossible(owner);
            }
        }
        private void InterruptResearchCenter(int centerId)
        {
            foreach(PlayableOwner owner in Owners)
            {
                foreach(var order in Research(owner).Where(o=>o.Active&&!o.Complete&&o.CenterId==centerId))Sound(PlayableSoundKind.ResearchCancelled,default(NavPoint),PlayableEntityKind.Tank,owner,true);
                Research(owner).RemoveAll(o=>o.CenterId==centerId&&(PlayableResearchRules.Local(o.Kind)||o.Active&&!o.Complete));
            }
        }
        private void InterruptUnreadyResearchCenters()
        {
            foreach(PlayableOwner owner in Owners)
            {
                foreach(var order in Research(owner).Where(o=>o.Active&&!o.Complete&&!ReadyResearchCenter(o.CenterId,owner)))Sound(PlayableSoundKind.ResearchCancelled,default(NavPoint),PlayableEntityKind.Tank,owner,true);
                Research(owner).RemoveAll(o=>!o.Complete&&(o.Active||PlayableResearchRules.Local(o.Kind))&&!ReadyResearchCenter(o.CenterId,owner));
            }
        }
        private PlayableResearchSaveState CaptureResearchState()
        {
            return new PlayableResearchSaveState
            {
                NextSequence=nextResearchSequence,
                Orders=research.SelectMany(pair=>pair.Value.Select(order=>new PlayableResearchSaveOrder
                {
                    Owner=(int)pair.Key,Id=order.Id,Kind=order.Kind,CenterId=order.CenterId,PaidCost=order.PaidCost,
                    Elapsed=order.Elapsed,Duration=order.Duration,Active=order.Active,Complete=order.Complete
                })).OrderBy(order=>order.Id).ToArray()
            };
        }
        private void RestoreResearchState(PlayableResearchSaveState state)
        {
            if(state==null)throw new ArgumentNullException(nameof(state));
            research.Clear();
            nextResearchSequence=Math.Max(1,state.NextSequence);
            foreach(var saved in state.Orders??Array.Empty<PlayableResearchSaveOrder>())
            {
                if(!HasOwner((PlayableOwner)saved.Owner)||!Enum.IsDefined(typeof(PlayableResearchKind),saved.Kind))throw new ArgumentException("Unknown research save value",nameof(state));
                var owner=(PlayableOwner)saved.Owner;
                Research(owner).Add(new ResearchOrder{Id=saved.Id,Kind=saved.Kind,CenterId=saved.CenterId,PaidCost=saved.PaidCost,Elapsed=saved.Elapsed,Duration=saved.Duration,Active=saved.Active,Complete=saved.Complete});
                nextResearchSequence=Math.Max(nextResearchSequence,saved.Id+1);
            }
            foreach(var owner in research.Keys.ToArray())if(Chassis(owner))foreach(var unit in units.Values)if(unit.Owner==owner&&unit.Kind==PlayableEntityKind.Tank&&!unit.AuthoredUpgrade.HasValue)navigation.Crowd.SetSpeed(unit.Id,profile.TankChassisSpeed);
        }
        private PlayableResearchOrderSnapshot[] ResearchSnapshot(PlayableOwner owner)=>research.TryGetValue(owner,out var orders)?orders.Select(o=>new PlayableResearchOrderSnapshot(o.Id,o.Kind,o.CenterId,o.PaidCost,o.Elapsed,o.Duration,o.Active,o.Complete)).ToArray():Array.Empty<PlayableResearchOrderSnapshot>();
        private string ResearchBlockedReason(PlayableOwner owner,PlayableResearchKind kind)
        {
            if(PlayableResearchRules.Local(kind))return buildings.Values.Any(b=>ReadyResearchCenter(b.Id,owner)&&!OrderedAt(owner,kind,b.Id))?null:"Все готовые центры уже улучшены или улучшаются";
            if(PlayableResearchRules.NeedsUpgradedCenter(kind)&&!buildings.Values.Any(b=>LegalResearchCenter(b.Id,owner,kind)))return "Нужен улучшенный научный центр";
            if(research.TryGetValue(owner,out var orders)&&orders.Any(o=>o.Kind==kind&&(o.Complete||o.Active)))return null;
            if(!ResearchProducerReady(owner,kind))return PlayableResearchRules.ProducerRequirement(kind);
            var previous=PlayableResearchRules.Previous(kind);
            return !FreeProvingGround&&previous.HasValue&&!HasResearch(owner,previous.Value)?"Сначала завершите предыдущий уровень":null;
        }
        private PlayableResearchAvailabilitySnapshot[] ResearchAvailability(PlayableOwner owner)=>Enum.GetValues(typeof(PlayableResearchKind)).Cast<PlayableResearchKind>().Select(kind=>
        {
            var blocked=ResearchBlockedReason(owner,kind);
            return new PlayableResearchAvailabilitySnapshot(kind,ResearchCost(kind),(PlayableResearchRules.Local(kind)||!HasResearch(owner,kind)&&!HasActiveResearch(owner,kind))&&blocked==null,blocked);
        }).ToArray();
        private double SystemsMultiplier(PlayableOwner owner,bool weapon)
        {
            var ultimate=weapon?PlayableResearchKind.UltimateWeaponSystems:PlayableResearchKind.UltimateDefensiveSystems;
            var advanced=weapon?PlayableResearchKind.AdvancedWeaponSystems:PlayableResearchKind.AdvancedDefensiveSystems;
            var improved=weapon?PlayableResearchKind.ImprovedWeaponSystems:PlayableResearchKind.ImprovedDefensiveSystems;
            return 1+(HasResearch(owner,ultimate)?PlayableResearchRules.Bonus(profile,ultimate):HasResearch(owner,advanced)?PlayableResearchRules.Bonus(profile,advanced):HasResearch(owner,improved)?PlayableResearchRules.Bonus(profile,improved):0)/100;
        }
        private double WeaponMultiplier(PlayableOwner owner)=>SystemsMultiplier(owner,true);
        private double WeaponDamage(PlayableOwner owner,double baseDamage)=>PlayableStatRounding.Damage(profile,baseDamage*WeaponMultiplier(owner));
        private double HealthMultiplier(PlayableOwner owner)=>SystemsMultiplier(owner,false);
        private int ArmySystemsLevel(PlayableOwner owner,bool weapon)
        {
            int level=0;
            if(research.TryGetValue(owner,out var orders))foreach(var order in orders)
                if(order.Complete&&(weapon?PlayableResearchRules.Weapon(order.Kind):PlayableResearchRules.Defense(order.Kind)))
                    level=Math.Max(level,PlayableResearchRules.Level(order.Kind));
            return level;
        }
        private double BuildingHealthMultiplier(PlayableOwner owner,PlayableProfile terms)=>1+(HasResearch(owner,PlayableResearchKind.AdvancedReinforcedConcrete)?terms.ReinforcedConcreteLevel2BonusPercent:HasResearch(owner,PlayableResearchKind.ReinforcedConcrete)?terms.ReinforcedConcreteBonusPercent:0)/100;
        private double BuildingMaximumHealth(PlayableOwner owner,PlayableBuildingKind kind,PlayableProfile terms=null)=>PlayableStatRounding.MaximumHealth(profile,TerritoryRules.Health(terms??profile,kind)*BuildingHealthMultiplier(owner,profile));
        // Clamp full HP to its maximum so binary noise cannot expose a phantom extra HP.
        private static double RescaleCurrentHealth(double health,double previous,double maximum)=>Math.Min(maximum,health*(maximum/previous));
        private Dictionary<int,double> BuildingMaxima(PlayableOwner owner)=>buildings.Values.Where(b=>b.Owner==owner).ToDictionary(b=>b.Id,b=>BuildingMaximumHealth(owner,b.Kind));
        private void ScaleBuildingHealth(PlayableOwner owner,Dictionary<int,double> previous)
        {
            foreach(var b in buildings.Values)if(b.Owner==owner)b.Health=RescaleCurrentHealth(b.Health,previous[b.Id],BuildingMaximumHealth(owner,b.Kind));
        }
        private double UnitMaximumHealth(PlayableOwner owner,PlayableEntityKind kind)=>PlayableStatRounding.MaximumHealth(profile,PlayableUnitRules.Health(profile,kind)*HealthMultiplier(owner)*(kind==PlayableEntityKind.Tank&&Chassis(owner)?1+profile.TankChassisHealthBonusPercent/100:1));
        private bool UnitUpgraded(Unit u)=>u.Kind==PlayableEntityKind.Hawk?false:u.AuthoredUpgrade??(u.Kind==PlayableEntityKind.Tank?Chassis(u.Owner):u.Kind==PlayableEntityKind.Explorer?Assault(u.Owner):Guidance(u.Owner));
        private bool HawkSalvo(PlayableOwner owner)=>HasResearch(owner,PlayableResearchKind.HawkSalvoSystems);
        private double HawkPreparation(Unit u)=>HawkSalvo(u.Owner)?profile.HawkUpgradedPreparationSeconds:profile.HawkPreparationSeconds;
        private double HawkReload(Unit u)=>HawkSalvo(u.Owner)?profile.HawkUpgradedReloadSeconds:profile.HawkReloadSeconds;
        private double HawkSalvoDelay(Unit u)=>HawkSalvo(u.Owner)?profile.HawkUpgradedSalvoDelay:profile.HawkSalvoDelay;
        private bool Chassis(PlayableOwner owner)=>HasResearch(owner,PlayableResearchKind.TankChassis);
        private bool Assault(PlayableOwner owner)=>HasResearch(owner,PlayableResearchKind.ExplorerAssaultGuns);
        private bool Guidance(PlayableOwner owner)=>HasResearch(owner,PlayableResearchKind.ShkvalGuidance);
    }
}
