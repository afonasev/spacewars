using System;
using System.Linq;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        [Serializable] private sealed class SaleState { public int TermsRevision; public double Elapsed,Duration; }
        [Serializable] private sealed class RepairState
        { public int TermsRevision;
            public double MissingHealth,TotalCost,Duration,PaidSeconds,SettlementElapsed;
            public bool Waiting;
        }
        private bool CombatLocked(Building b,double seconds)=>b.LastDamageTime.HasValue&&elapsed-b.LastDamageTime.Value+1e-9<seconds;
        private Building[] SaleGroup(Building b)=>new[]{b}.Concat(TerritoryRules.Center(b.Kind)?buildings.Values.Where(c=>c.ParentId==b.Id&&c.Sale==null).OrderBy(c=>c.Id):Enumerable.Empty<Building>()).ToArray();
        private string SaleBlocked(Building b)
        {
            if(!b.Ready||b.Health<=0)return "Здание не готово";
            if(b.Sale!=null)return "Демонтаж необратим";
            if(CombatLocked(b,profile.BuildingSaleCombatLockoutSec))return "Продажа запрещена: здание находится в бою";
            if(TerritoryRules.Center(b.Kind))foreach(var c in buildings.Values)
                if(c.ParentId==b.Id&&c.Sale==null&&CombatLocked(c,profile.BuildingSaleCombatLockoutSec))return "Продажа запрещена: здание находится в бою";
            return null;
        }
        private string RepairBlocked(Building b)
        {
            if(!b.Ready||b.Health<=0||b.Sale!=null)return "Здание недоступно";
            if(b.Repair!=null)return "Ремонт уже активен";
            if(b.Health>=TerritoryRules.Health(profile,b.Kind))return "Здание не повреждено";
            return CombatLocked(b,profile.BuildingRepairCombatLockoutSec)?"Ремонт запрещён: здание находится в бою":null;
        }
        private PlayableCommandStatus SellBuilding(int id,PlayableOwner owner,out string message)
        {
            message="Выберите собственное готовое здание";
            if(!buildings.TryGetValue(id,out var b)||b.Owner!=owner)return PlayableCommandStatus.InvalidEntity;
            message=SaleBlocked(b);if(message!=null)return PlayableCommandStatus.Rejected;
            foreach(var item in SaleGroup(b))
            {
                AddCredits(owner,SaleRefund(item));
                item.Sale=new SaleState{TermsRevision=profile.Revision,Duration=profile.BuildingSaleDemolitionSec};
                item.Repair=null;if(item.Upgrade?.Complete!=true)item.Upgrade=null;item.Orders.Clear();item.RepeatTank=false;
            }
            message="Продажа принята. Демонтаж необратим.";return PlayableCommandStatus.Applied;
        }
        private PlayableCommandStatus StartRepair(int id,PlayableOwner owner,out string message)
        {
            message="Выберите собственное повреждённое здание";
            if(!buildings.TryGetValue(id,out var b)||b.Owner!=owner)return PlayableCommandStatus.InvalidEntity;
            message=RepairBlocked(b);if(message!=null)return PlayableCommandStatus.Rejected;
            double missing=TerritoryRules.Health(profile,b.Kind)-b.Health,ratio=missing/TerritoryRules.Health(profile,b.Kind);
            b.Repair=new RepairState{TermsRevision=profile.Revision,MissingHealth=missing,TotalCost=TerritoryRules.Cost(profile,b.Kind)*profile.BuildingRepairCostRatio*ratio,Duration=profile.BuildingRepairDurationSec*ratio};
            message="Ремонт начат";return PlayableCommandStatus.Applied;
        }
        private PlayableCommandStatus CancelRepair(int id,PlayableOwner owner,out string message)
        {
            message="Нет активного ремонта";
            if(!buildings.TryGetValue(id,out var b)||b.Owner!=owner||b.Repair==null)return PlayableCommandStatus.InvalidEntity;
            b.Repair=null;message="Ремонт отменён";return PlayableCommandStatus.Applied;
        }
        private readonly List<Building> lifecycleWork=new List<Building>();
        private void AdvanceBuildingLifecycle(double dt)
        {
            bool removed=false;lifecycleWork.Clear();
            foreach(var item in buildings.Values)if(item.Sale!=null||item.Repair!=null)lifecycleWork.Add(item);
            lifecycleWork.Sort((a,b)=>a.Id.CompareTo(b.Id));
            foreach(var b in lifecycleWork)
            {
                if(!buildings.ContainsKey(b.Id))continue;
                if(b.Sale!=null)
                {
                    b.Sale.Elapsed+=dt;
                    if(b.Sale.Elapsed+1e-9>=b.Sale.Duration){RemoveBuilding(b,false);removed=true;}
                    continue;
                }
                var r=b.Repair;if(r==null)continue;
                r.SettlementElapsed+=dt;
                // One second is the canonical settlement cadence, not a balance parameter.
                while(r.SettlementElapsed+1e-9>=1&&b.Repair!=null)
                {
                    r.SettlementElapsed=Math.Max(0,r.SettlementElapsed-1);
                    double remaining=r.Duration-r.PaidSeconds,slice=Math.Min(1,remaining);
                    bool terminal=slice>=remaining-1e-9;
                    double cost=terminal?Math.Max(0,r.TotalCost-r.TotalCost*Math.Min(1,r.PaidSeconds/r.Duration)):r.TotalCost*slice/r.Duration;
                    if(Balance(b.Owner)+1e-9<cost){r.Waiting=true;continue;}
                    AddCredits(b.Owner,-cost);
                    double maximum=TerritoryRules.Health(profile,b.Kind);
                    b.Health=Math.Min(maximum,b.Health+r.MissingHealth*slice/r.Duration);
                    // Settle sub-nanohitpoint accumulation before the integer HUD ceiling.
                    if(terminal)b.Health=Math.Round(b.Health,9);
                    r.PaidSeconds+=slice;r.Waiting=false;
                    if(terminal||b.Health>=maximum-1e-9){b.Repair=null;}
                }
            }
            if(removed)RebuildGeometry();
        }
        private int SaleRefund(Building b)=>(int)Math.Floor((b.PaidCost+(b.Upgrade?.PaidCost??0))*profile.BuildingSaleRefundRatio*Math.Max(0,b.Health)/TerritoryRules.Health(profile,b.Kind));
        private PlayableBuildingLifecycleSnapshot LifecycleSnapshot(Building b)
        {
            bool center=TerritoryRules.Center(b.Kind),lastCenter=center;int count=1;
            int refund=SaleRefund(b);
            if(center)foreach(var c in buildings.Values)
            {
                if(c.Owner==b.Owner&&TerritoryRules.Center(c.Kind)&&c.Id!=b.Id&&c.Sale==null)lastCenter=false;
                if(c.ParentId==b.Id&&c.Sale==null){count++;refund+=SaleRefund(c);}
            }
            return new PlayableBuildingLifecycleSnapshot(b.Sale!=null,b.Sale==null?0:b.Sale.Elapsed/b.Sale.Duration,b.Repair!=null,b.Repair?.Waiting??false,
                b.Repair?.PaidSeconds??0,b.Repair?.Duration??0,SaleBlocked(b),RepairBlocked(b),count,refund,lastCenter);
        }
    }
}
