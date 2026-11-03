using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        // Six slots is the canonical structural queue contract (one head plus five waiting).
        internal const int MaximumProductionOrders=6;
        private long nextProductionSequence=1;
        private sealed class ProductionOrder
        { public int TermsRevision;
            public long Id;
            public PlayableEntityKind Kind;
            public int PaidCost,PopulationCost;
            public double Duration,Remaining;
            public bool Active,IsUpgrade;
            public double SpeedMultiplier=1;
            public int SpeedRevision;
        }
        private bool UpgradeCenterReady(PlayableOwner owner)=>buildings.Values.Any(b=>b.Owner==owner&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.Ready&&b.Health>0&&b.Sale==null);
        private bool UnitPrerequisite(PlayableOwner owner,PlayableEntityKind kind)=>kind!=PlayableEntityKind.Shkval||UpgradeCenterReady(owner);
        private string ProducerUpgradeBlocked(Building b)
        {
            if(!b.Ready||b.Health<=0||b.Sale!=null)return "Нужно готовое собственное здание";
            if(b.ProducerUpgrade!=null)return "Здание уже улучшено";
            if(b.Orders.Any(o=>o.IsUpgrade))return "Улучшение уже в очереди";
            if(!UpgradeCenterReady(b.Owner))return "Нужен готовый собственный Научный центр";
            return b.Orders.Count>=MaximumProductionOrders?"Очередь заполнена":null;
        }
        private PlayableCommandStatus QueueProducerUpgrade(int id,PlayableOwner owner,out string message)
        {
            var b=Producer(id,owner);message="Выберите собственную фабрику или ангар";
            if(b==null)return PlayableCommandStatus.InvalidEntity;
            // Capacity and prerequisite are admission gates; payment is deferred until head activation.
            if(b.Orders.Count>=MaximumProductionOrders){message="Очередь заполнена";return PlayableCommandStatus.Overflow;}
            message=ProducerUpgradeBlocked(b);if(message!=null)return PlayableCommandStatus.Rejected;
            b.Orders.Add(new ProductionOrder{Id=nextProductionSequence++,IsUpgrade=true,Kind=b.Kind==PlayableBuildingKind.Hangar?PlayableEntityKind.Hawk:PlayableEntityKind.Tank});
            Sound(PlayableSoundKind.ResearchQueued,b.Position,PlayableEntityKind.Tank,owner,true);
            message="Улучшение добавлено в очередь";return PlayableCommandStatus.Applied;
        }
        private string ProductionWaitReason(Building b,ProductionOrder o)
        {
            if(o.Active){var p=Population(b.Owner);return !o.IsUpgrade&&p.Living+p.Reserved>p.Capacity?"Превышен лимит армии":null;}
            if(b.LaunchPhase!=0)return "Ожидает завершения выпуска";
            if(!o.IsUpgrade)return "Ожидает места в армии или своей очереди";
            if(b.Orders[0]!=o)return "Ожидает своей очереди";
            if(!UpgradeCenterReady(b.Owner))return "Нужен готовый собственный Научный центр";
            return !CanAfford(b.Owner,profile.ProducerUpgradeCost(b.Kind))?"Недостаточно кредитов":"Ожидает старта";
        }
        private void StartProducerUpgrade(Building b,ProductionOrder o)
        {
            int cost=profile.ProducerUpgradeCost(b.Kind);
            if(!UpgradeCenterReady(b.Owner)||!CanAfford(b.Owner,cost))return;
            o.TermsRevision=profile.Revision;o.PaidCost=cost;o.Duration=ActionDuration(profile.ProducerUpgradeSeconds(b.Kind));o.Remaining=o.Duration;o.Active=true;
            AddCredits(b.Owner,-cost);Fact(b.Owner,MatchFactKind.RefineryUpgradeOrdered,cost,(int)b.Kind);Sound(PlayableSoundKind.UpgradeStarted,b.Position,o.Kind,b.Owner,true);
        }
        private void ActivateUnitOrder(Building b,ProductionOrder o)
        {
            o.SpeedRevision=profile.Revision;o.SpeedMultiplier=b.ProducerUpgrade?.Complete==true?profile.UpgradedProducerSpeedMultiplier:1;
            o.Duration=ActionDuration(PlayableUnitRules.Duration(Terms(o.TermsRevision),o.Kind))/o.SpeedMultiplier;o.Remaining=o.Duration;o.Active=true;
        }
        private PlayableRefineryUpgradeSnapshot ProducerUpgradeSnapshot(Building b)
        {
            if(!b.Ready)return null;
            var order=b.Orders.FirstOrDefault(o=>o.IsUpgrade);var done=b.ProducerUpgrade;
            return new PlayableRefineryUpgradeSnapshot(order?.Active==true,done?.Complete==true,order?.PaidCost??done?.PaidCost??0,
                order?.Active==true?order.Duration-order.Remaining:done?.Elapsed??0,
                order?.Active==true?order.Duration:done?.Duration??ActionDuration(profile.ProducerUpgradeSeconds(b.Kind)),ProducerUpgradeBlocked(b));
        }
        private Building Producer(int id,PlayableOwner owner)
            =>buildings.TryGetValue(id,out var b)&&b.Owner==owner&&(b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Hangar)&&b.Ready&&b.Sale==null?b:null;
        private ProductionOrder NewOrder(PlayableEntityKind kind,bool active)
            =>new ProductionOrder{TermsRevision=profile.Revision,Id=nextProductionSequence++,Kind=kind,PaidCost=PlayableUnitRules.Cost(profile,kind),
                PopulationCost=PlayableUnitRules.Population(profile,kind),Duration=ActionDuration(PlayableUnitRules.Duration(profile,kind)),Remaining=ActionDuration(PlayableUnitRules.Duration(profile,kind)),Active=active};
        private PlayableCommandStatus QueueTank(int id,PlayableOwner owner,out string message)=>QueueUnit(id,owner,PlayableEntityKind.Tank,out message);
        private PlayableCommandStatus QueueUnit(int id,PlayableOwner owner,PlayableEntityKind kind,out string message)
        {
            var b=Producer(id,owner);message="Select a ready factory.";
            if(b==null||!PlayableUnitRules.Supported(kind)||(b.Kind==PlayableBuildingKind.Hangar)!=(kind==PlayableEntityKind.Hawk))return PlayableCommandStatus.InvalidEntity;
            if(!UnitPrerequisite(owner,kind)){message="Нужен готовый собственный Научный центр";return PlayableCommandStatus.Rejected;}
            if(b.Orders.Count>=MaximumProductionOrders){message="Production queue is full.";return PlayableCommandStatus.Overflow;}
            if(!CanAfford(owner,PlayableUnitRules.Cost(profile,kind))){message="Insufficient credits.";return PlayableCommandStatus.InsufficientCredits;}
            var order=NewOrder(kind,false);AddCredits(owner,-order.PaidCost);b.Orders.Add(order);Fact(owner,MatchFactKind.UnitOrdered,order.PaidCost,(int)order.Kind,order.Kind.ToString());
            Sound(PlayableSoundKind.ProductionQueued,b.Position,kind,owner,true);message=kind+" ordered.";return PlayableCommandStatus.Applied;
        }
        private PlayableCommandStatus CancelProduction(int id,long orderId,PlayableOwner owner,out string message)
        {
            var b=Producer(id,owner);message="Order no longer exists.";
            if(b==null)return PlayableCommandStatus.InvalidEntity;
            int index=b.Orders.FindIndex(o=>o.Id==orderId);
            if(index<0)return PlayableCommandStatus.InvalidTarget;
            var order=b.Orders[index];b.Orders.RemoveAt(index);
            if(index==0&&b.Kind==PlayableBuildingKind.Factory&&b.LaunchPhase>0){b.LaunchPhase=FactoryReleaseRules.Closing;b.LaunchTime=0;}
            if(index==0&&b.Kind==PlayableBuildingKind.Hangar&&b.LaunchPhase>0&&b.LaunchPhase<4){b.LaunchPhase=5;b.LaunchTime=0;}
            AddCredits(owner,Math.Floor(order.PaidCost*(order.IsUpgrade?1:profile.UnitCancellationRefundRatio)));
            Sound(PlayableSoundKind.ProductionCancelled,b.Position,order.Kind,owner,true);message="Order cancelled.";return PlayableCommandStatus.Applied;
        }
        private PlayableCommandStatus ToggleRepeat(int id,PlayableOwner owner,out string message,PlayableEntityKind kind=PlayableEntityKind.Tank)
        {
            var b=Producer(id,owner);message="Select a ready factory.";
            if(b==null||!PlayableUnitRules.Supported(kind)||(b.Kind==PlayableBuildingKind.Hangar)!=(kind==PlayableEntityKind.Hawk))return PlayableCommandStatus.InvalidEntity;
            if(b.RepeatTank&&b.RepeatKind==kind)b.RepeatTank=false;
            else
            {
                if(!UnitPrerequisite(owner,kind)){message="Нужен готовый собственный Научный центр";return PlayableCommandStatus.Rejected;}
                // Repeat is an unpaid tail intent; existing paid work keeps its order and progress.
                b.RepeatTank=true;b.RepeatKind=kind;
            }
            message="Repeat updated.";return PlayableCommandStatus.Applied;
        }
        private PlayablePopulationSnapshot Population(PlayableOwner owner)
        {
            int living=0,reserved=0;
            foreach(var u in units.Values)if(u.Owner==owner&&u.Health>0)living+=PlayableUnitRules.Population(profile,u.Kind);
            foreach(var b in buildings.Values)if(b.Owner==owner)foreach(var o in b.Orders)if(o.Active&&!o.IsUpgrade)reserved+=PlayableUnitRules.Population(profile,o.Kind);
            return new PlayablePopulationSnapshot(living,reserved,profile.ArmyCapacity);
        }
        private PlayableProductionOrderSnapshot[] ProductionSnapshot(Building b)
            =>b.Orders.Select(o=>new PlayableProductionOrderSnapshot(o.Id,o.Kind,o.PaidCost,o.PopulationCost,o.Duration,o.Remaining,o.Active,o.IsUpgrade,ProductionWaitReason(b,o))).ToArray();
        private void AdvanceProduction(double dt)
        {
            var factories=buildings.Values.Where(b=>(b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Hangar)&&b.Ready&&b.Sale==null&&!eliminated.Contains(b.Owner)).OrderBy(b=>b.Id).ToArray();
            var overflow=new HashSet<PlayableOwner>();
            foreach(PlayableOwner owner in Owners)
            {
                var population=Population(owner);int usage=population.Living+population.Reserved;if(usage>profile.ArmyCapacity)overflow.Add(owner);
                foreach(var b in factories.Where(b=>b.Owner==owner&&b.Orders.Count>0&&!b.Orders[0].Active&&b.LaunchPhase==0).OrderBy(b=>b.Orders[0].Id).ThenBy(b=>b.Id))
                {
                    var order=b.Orders[0];
                    if(order.IsUpgrade){StartProducerUpgrade(b,order);continue;}
                    if(usage+PlayableUnitRules.Population(profile,order.Kind)>profile.ArmyCapacity)continue;
                    ActivateUnitOrder(b,order);Sound(PlayableSoundKind.ProductionStarted,b.Position,order.Kind,owner,true);usage+=PlayableUnitRules.Population(profile,order.Kind);
                }
                foreach(var b in factories)
                {
                    if(b.Owner!=owner||!b.RepeatTank||b.Orders.Count!=0||b.LaunchPhase!=0||usage+PlayableUnitRules.Population(profile,b.RepeatKind)>profile.ArmyCapacity||!CanAfford(owner,PlayableUnitRules.Cost(profile,b.RepeatKind))||!UnitPrerequisite(owner,b.RepeatKind))continue;
                    var order=NewOrder(b.RepeatKind,false);ActivateUnitOrder(b,order);AddCredits(owner,-order.PaidCost);b.Orders.Add(order);Fact(owner,MatchFactKind.UnitOrdered,order.PaidCost,(int)order.Kind,order.Kind.ToString());usage+=PlayableUnitRules.Population(profile,order.Kind);
                    Sound(PlayableSoundKind.ProductionQueued,b.Position,order.Kind,owner,true);
                    Sound(PlayableSoundKind.ProductionStarted,b.Position,order.Kind,owner,true);
                }
            }
            foreach(var b in factories)
            {
                ObserveProductionLimit(b);
                // Capture can exceed the cap after an order has already paid/reserved space.
                // Preserve that work, but do not produce another unit until capacity returns.
                if(overflow.Contains(b.Owner)&&b.Orders.Count>0&&b.Orders[0].Active&&!b.Orders[0].IsUpgrade)continue;
                if(b.Kind==PlayableBuildingKind.Factory&&b.LaunchPhase!=0){AdvanceFactoryRelease(b,dt);continue;}
                if(b.Kind==PlayableBuildingKind.Hangar&&b.LaunchPhase!=0){AdvanceHangar(b,dt);continue;}
                if(b.Orders.Count==0||!b.Orders[0].Active)continue;
                var order=b.Orders[0];order.Remaining=Math.Max(0,order.Remaining-dt);
                if(order.Remaining>1e-9)continue;
                order.Remaining=0;
                if(order.IsUpgrade)
                {
                    b.ProducerUpgrade=new RefineryUpgrade{TermsRevision=order.TermsRevision,PaidCost=order.PaidCost,Duration=order.Duration,Elapsed=order.Duration,Complete=true};
                    b.Orders.RemoveAt(0);Fact(b.Owner,MatchFactKind.RefineryUpgradeCompleted,order.PaidCost,(int)b.Kind,"Ускорение производства");Sound(PlayableSoundKind.UpgradeComplete,b.Position,order.Kind,b.Owner,true);continue;
                }
                if(b.Kind==PlayableBuildingKind.Hangar){AdvanceHangar(b,dt);continue;}
                if(b.Kind==PlayableBuildingKind.Factory){AdvanceFactoryRelease(b,dt);continue;}
                if(!TryFactoryExit(b,order.Kind,out var spawn))continue;
                // Owner-thread transaction: there is no published gap between reserve and living population.
                if(!TrySpawnProduced(spawn,b.HasRally?(NavPoint?)b.Rally:null,b.Owner,order.Kind))continue;
                Sound(PlayableSoundKind.ProductionComplete,b.Position,order.Kind,b.Owner,true,notificationPoint:spawn);b.Orders.RemoveAt(0);
            }
        }
        private readonly System.Collections.Generic.Dictionary<int,string> soundProductionLimits=new System.Collections.Generic.Dictionary<int,string>();
        private void ObserveProductionLimit(Building b)
        {
            string reason="";
            if(b.Orders.Count>0&&b.Orders[0].IsUpgrade)return;
            if(b.Orders.Count>0&&!b.Orders[0].Active&&b.LaunchPhase==0)reason="capacity";
            else if(b.RepeatTank&&b.Orders.Count==0)reason=Population(b.Owner).Living+Population(b.Owner).Reserved+PlayableUnitRules.Population(profile,b.RepeatKind)>profile.ArmyCapacity?"capacity":!CanAfford(b.Owner,PlayableUnitRules.Cost(profile,b.RepeatKind))?"credits":"";
            else if(b.Orders.Count>0&&b.Orders[0].Remaining<=1e-9&&!TryFactoryExit(b,b.Orders[0].Kind,out _))reason="exit";
            if(reason.Length>0&&(!soundProductionLimits.TryGetValue(b.Id,out var previous)||previous!=reason))
            {
                Sound(PlayableSoundKind.ProductionLimited,b.Position,b.RepeatKind,b.Owner,true);
                MatchEvent(reason=="capacity"?MatchEventKind.ProductionCapacity:reason=="credits"?MatchEventKind.ProductionCredits:MatchEventKind.ProductionExit,b.Owner,b.Position);
            }
            soundProductionLimits[b.Id]=reason;
            foreach(var id in soundProductionLimits.Keys.Where(id=>!buildings.ContainsKey(id)).ToArray())soundProductionLimits.Remove(id);
        }
        private NavPoint[] FactoryExitCandidates(Building b,PlayableEntityKind kind)
            // The accepted native TerritorySlot projects its authored outward anchor
            // through the existing profile distance; map/slot geometry is unchanged.
            =>FactoryProductionAnchors.Candidates(b.Position,BuildingRadius(b.Kind),PlayableUnitRules.Radius(profile,kind),new[]{FactoryExit(b)});
        private bool TryFactoryExit(Building b,PlayableEntityKind kind,out NavPoint spawn)
        {
            if(b.Kind==PlayableBuildingKind.Factory)return FactoryCorridorFree(b,kind,out spawn);
            double radius=PlayableUnitRules.Radius(profile,kind);
            foreach(var candidate in FactoryExitCandidates(b,kind))
            {
                // Always current owner-thread geometry (including solid buildings) and
                // crowd footprints. No stale availability cache or blocking-unit mutation.
                // Native wreck authority is absent and remains a separate parity gap.
                if(!navigation.Crowd.CanPlace(candidate,radius))continue;
                spawn=candidate;return true;
            }
            spawn=default;return false;
        }
    }
}
