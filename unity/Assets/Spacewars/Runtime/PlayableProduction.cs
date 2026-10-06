using System;
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
        {
            public long Id;
            public PlayableEntityKind Kind;
            public int PaidCost,PopulationCost;
            public double Duration,Remaining;
            public bool Active;
        }
        private Building Producer(int id,PlayableOwner owner)
            =>buildings.TryGetValue(id,out var b)&&b.Owner==owner&&b.Kind==PlayableBuildingKind.Factory&&b.Ready&&b.Sale==null?b:null;
        private ProductionOrder NewOrder(PlayableEntityKind kind,bool active)
            =>new ProductionOrder{Id=nextProductionSequence++,Kind=kind,PaidCost=PlayableUnitRules.Cost(profile,kind),
                PopulationCost=PlayableUnitRules.Population(profile,kind),Duration=PlayableUnitRules.Duration(profile,kind),Remaining=PlayableUnitRules.Duration(profile,kind),Active=active};
        private PlayableCommandStatus QueueTank(int id,PlayableOwner owner,out string message)=>QueueUnit(id,owner,PlayableEntityKind.Tank,out message);
        private PlayableCommandStatus QueueUnit(int id,PlayableOwner owner,PlayableEntityKind kind,out string message)
        {
            var b=Producer(id,owner);message="Select a ready factory.";
            if(b==null||!PlayableUnitRules.Supported(kind))return PlayableCommandStatus.InvalidEntity;
            b.RepeatTank=false;
            if(b.Orders.Count>=MaximumProductionOrders){message="Production queue is full.";return PlayableCommandStatus.Overflow;}
            if(Balance(owner)<PlayableUnitRules.Cost(profile,kind)){message="Insufficient credits.";return PlayableCommandStatus.InsufficientCredits;}
            var order=NewOrder(kind,false);AddCredits(owner,-order.PaidCost);b.Orders.Add(order);
            message=kind+" ordered.";return PlayableCommandStatus.Applied;
        }
        private PlayableCommandStatus CancelProduction(int id,long orderId,PlayableOwner owner,out string message)
        {
            var b=Producer(id,owner);message="Order no longer exists.";
            if(b==null)return PlayableCommandStatus.InvalidEntity;
            int index=b.Orders.FindIndex(o=>o.Id==orderId);
            if(index<0)return PlayableCommandStatus.InvalidTarget;
            var order=b.Orders[index];b.Orders.RemoveAt(index);b.RepeatTank=false;
            AddCredits(owner,Math.Floor(order.PaidCost*profile.UnitCancellationRefundRatio));
            message="Order cancelled.";return PlayableCommandStatus.Applied;
        }
        private PlayableCommandStatus ToggleRepeat(int id,PlayableOwner owner,out string message,PlayableEntityKind kind=PlayableEntityKind.Tank)
        {
            var b=Producer(id,owner);message="Select a ready factory.";
            if(b==null||!PlayableUnitRules.Supported(kind))return PlayableCommandStatus.InvalidEntity;
            if(b.RepeatTank&&b.RepeatKind==kind)b.RepeatTank=false;
            else
            {
                foreach(var order in b.Orders)AddCredits(owner,Math.Floor(order.PaidCost*profile.UnitCancellationRefundRatio));
                b.Orders.Clear();b.RepeatTank=true;b.RepeatKind=kind;
            }
            message="Repeat updated.";return PlayableCommandStatus.Applied;
        }
        private PlayablePopulationSnapshot Population(PlayableOwner owner)
        {
            int living=0,reserved=0;
            foreach(var u in units.Values)if(u.Owner==owner&&u.Health>0)living+=PlayableUnitRules.Population(profile,u.Kind);
            foreach(var b in buildings.Values)if(b.Owner==owner)foreach(var o in b.Orders)if(o.Active)reserved+=o.PopulationCost;
            return new PlayablePopulationSnapshot(living,reserved,profile.ArmyCapacity);
        }
        private PlayableProductionOrderSnapshot[] ProductionSnapshot(Building b)
            =>b.Orders.Select(o=>new PlayableProductionOrderSnapshot(o.Id,o.Kind,o.PaidCost,o.PopulationCost,o.Duration,o.Remaining,o.Active)).ToArray();
        private void AdvanceProduction(double dt)
        {
            var factories=buildings.Values.Where(b=>b.Kind==PlayableBuildingKind.Factory&&b.Ready&&b.Sale==null&&!eliminated.Contains(b.Owner)).OrderBy(b=>b.Id).ToArray();
            foreach(PlayableOwner owner in Owners)
            {
                var population=Population(owner);int usage=population.Living+population.Reserved;
                foreach(var b in factories.Where(b=>b.Owner==owner&&b.Orders.Count>0&&!b.Orders[0].Active).OrderBy(b=>b.Orders[0].Id).ThenBy(b=>b.Id))
                {
                    var order=b.Orders[0];if(usage+order.PopulationCost>profile.ArmyCapacity)continue;
                    order.Active=true;usage+=order.PopulationCost;
                }
                foreach(var b in factories)
                {
                    if(b.Owner!=owner||!b.RepeatTank||b.Orders.Count!=0||usage+PlayableUnitRules.Population(profile,b.RepeatKind)>profile.ArmyCapacity||Balance(owner)<PlayableUnitRules.Cost(profile,b.RepeatKind))continue;
                    var order=NewOrder(b.RepeatKind,true);AddCredits(owner,-order.PaidCost);b.Orders.Add(order);usage+=order.PopulationCost;
                }
            }
            foreach(var b in factories)
            {
                if(b.Orders.Count==0||!b.Orders[0].Active)continue;
                var order=b.Orders[0];order.Remaining=Math.Max(0,order.Remaining-dt);
                if(order.Remaining>1e-9)continue;
                order.Remaining=0;
                if(!TryFactoryExit(b,order.Kind,out var spawn))continue;
                // Owner-thread transaction: there is no published gap between reserve and living population.
                SpawnProduced(spawn,b.HasRally?(NavPoint?)b.Rally:null,b.Owner,order.Kind);
                b.Orders.RemoveAt(0);
            }
        }
        private NavPoint[] FactoryExitCandidates(Building b,PlayableEntityKind kind)
            // The accepted native TerritorySlot projects its authored outward anchor
            // through the existing profile distance; map/slot geometry is unchanged.
            =>FactoryProductionAnchors.Candidates(b.Position,BuildingRadius(b.Kind),PlayableUnitRules.Radius(profile,kind),new[]{FactoryExit(b)});
        private bool TryFactoryExit(Building b,PlayableEntityKind kind,out NavPoint spawn)
        {
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
