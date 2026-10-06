using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Input;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    public sealed class OfflinePadAction
    {
        public OfflinePadSector Sector;public string Label,Detail;public OfflinePadActionCommand Command,HoldCommand;
        public NavPoint? Point;public bool Repeat;public double? Progress;
        public OfflinePadAction(string id,string label,bool enabled,string detail="",string hold=null){Sector=new OfflinePadSector(id,enabled,hold);Label=label;Detail=detail;}
    }
    public sealed class OfflinePadActionCommand
    {
        public PlayableCommandKind Kind;public int[] Entities=Array.Empty<int>();public NavPoint Point;
        public int Site,Slot,Parent,Target;public PlayableBuildingKind Building=PlayableBuildingKind.Factory;
        public PlayableEntityKind Unit=PlayableEntityKind.Tank;public PlayableResearchKind Research;
        public PlayableCommandSubmitResult Submit(OfflineLocalSession session,string seat)=>session.Submit(seat,Kind,Entities,Point,Target,Site,Slot,Parent,Building,Unit,Research);
    }
    // SOURCE gamepadViewContext descriptors derived exclusively from the current
    // owner projection. Presentation never enumerates authority/private opponents.
    public static class OfflinePadWorldActions
    {
        public static string Name(PlayableEntityKind kind)=>kind==PlayableEntityKind.Explorer?"Исследователь":kind==PlayableEntityKind.Shkval?"Шквал":"Танк";
        public static string Name(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.Headquarters?"Штаб":kind==PlayableBuildingKind.Outpost?"Форпост":kind==PlayableBuildingKind.Mine?"Шахта":kind==PlayableBuildingKind.Factory?"Фабрика":kind==PlayableBuildingKind.Refinery?"Завод переработки":"Научный центр";
        private static OfflinePadActionCommand Command(PlayableCommandKind kind,int building,PlayableEntityKind unit=PlayableEntityKind.Tank,PlayableResearchKind research=PlayableResearchKind.TankChassis)=>new OfflinePadActionCommand{Kind=kind,Entities=new[]{building},Unit=unit,Research=research};
        public static OfflinePadAction[] Building(PlayableSnapshot view,PlayableProfile profile,int id)
        {
            var b=view.Buildings.FirstOrDefault(x=>x.Id==id&&x.Owner==view.Owner);var actions=new List<OfflinePadAction>();
            if(b==null||b.Phase!=ConstructionPhase.Ready||b.PrivateState==null)return actions.ToArray();var state=b.PrivateState;var lifecycle=state.Lifecycle;
            // SOURCE owner actions omit selling buildings. The rally append only
            // tests buildingReady, so it remains visible even during demolition.
            if(lifecycle?.Selling!=true){
                if(b.Kind==PlayableBuildingKind.Factory){
                    int MenuOrder(PlayableEntityKind unit)=>unit==PlayableEntityKind.Explorer?profile.ExplorerProductionMenuOrder:unit==PlayableEntityKind.Shkval?profile.ShkvalProductionMenuOrder:profile.TankProductionMenuOrder;
                    foreach(var unit in new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval}.OrderBy(MenuOrder)){
                        double cost=PlayableUnitRules.Cost(profile,unit);bool manual=view.ExactCredits>=cost&&state.Orders.Count<6;
                        var action=new OfflinePadAction(id+":"+(unit==PlayableEntityKind.Explorer?"explorer":unit==PlayableEntityKind.Shkval?"shkval":"tank"),"Произвести: "+Name(unit),true,Name(unit)+" · "+cost+" кредитов · "+PlayableUnitRules.Population(profile,unit)+" лимита · "+PlayableUnitRules.Duration(profile,unit)+" с"+(manual?"":" · Сейчас недоступно для разовой покупки"),"repeat");
                        action.Command=manual?Command(unit==PlayableEntityKind.Explorer?PlayableCommandKind.QueueExplorer:unit==PlayableEntityKind.Shkval?PlayableCommandKind.QueueShkval:PlayableCommandKind.QueueTank,id,unit):null;
                        action.HoldCommand=Command(PlayableCommandKind.ToggleRepeatProduction,id,unit);action.Repeat=state.Repeat&&state.RepeatKind==unit;if(action.Repeat)action.Label+=" ∞";
                        var active=state.Orders.FirstOrDefault(o=>o.Kind==unit&&o.Active);if(active!=null)action.Progress=active.Progress;
                        if(state.Orders.Count>0&&!action.Repeat)action.Detail+=" · Включение ∞ отменит ручную очередь, включая начатый заказ, с возвратом по правилам";
                        if(action.Repeat&&!action.Progress.HasValue)action.Detail+=" · ∞: ожидание ресурсов или лимита";actions.Add(action);
                    }
                }
                if(b.Kind==PlayableBuildingKind.ScientificCenter){
                    foreach(var kind in new[]{PlayableResearchKind.TankChassis,PlayableResearchKind.ExplorerAssaultGuns,PlayableResearchKind.ShkvalGuidance}){
                        // selectedWorldActions excludes a researched/queued action
                        // whose upgradeCommand is absent; do not retain a fake sector.
                        if(view.OwnerResearch.Any(o=>o.Kind==kind))continue;
                        var availability=view.ResearchAvailability.FirstOrDefault(a=>a.Kind==kind);if(availability==null)continue;
                        bool enabled=availability.Available&&view.ExactCredits>=availability.Cost;
                        string suffix=kind==PlayableResearchKind.TankChassis?"tank-chassis":kind==PlayableResearchKind.ExplorerAssaultGuns?"explorer-assault-guns":"shkval-guidance",label=kind==PlayableResearchKind.TankChassis?"Улучшить танковое шасси":kind==PlayableResearchKind.ExplorerAssaultGuns?"Улучшить штурмовые орудия":"Исследовать системы наведения";
                        actions.Add(new OfflinePadAction(id+":"+suffix,label,enabled,enabled?"Готово к запуску":"Недостаточно кредитов"){Command=enabled?Command(PlayableCommandKind.QueueResearch,id,research:kind):null});
                    }
                }
                if(b.Kind==PlayableBuildingKind.Refinery&&state.Upgrade?.Active!=true&&state.Upgrade?.Complete!=true&&!b.RefineryUpgraded){
                    bool science=view.Buildings.Any(x=>x.Owner==view.Owner&&x.Kind==PlayableBuildingKind.ScientificCenter&&x.Phase==ConstructionPhase.Ready&&x.PrivateState?.Lifecycle?.Selling!=true),enabled=science&&view.ExactCredits>=profile.RefineryUpgradeCost;
                    actions.Add(new OfflinePadAction(id+":upgrade","Улучшить завод",enabled,!science?"Требуется готовый научный центр":enabled?"Готово к запуску":"Недостаточно кредитов"){Command=enabled?Command(PlayableCommandKind.UpgradeRefinery,id):null});
                }
                bool repairing=lifecycle?.Repairing==true;bool repair=repairing||lifecycle!=null&&string.IsNullOrEmpty(lifecycle.RepairBlockedReason)&&b.Health<TerritoryRules.Health(profile,b.Kind);
                actions.Add(new OfflinePadAction(id+":repair",repairing?"Отменить ремонт":"Ремонт",repair,repairing?(lifecycle.WaitingForCredits?"Ожидает кредитов":"Ремонт выполняется"):lifecycle?.RepairBlockedReason??"Здание полностью исправно"){Command=repair?Command(repairing?PlayableCommandKind.CancelBuildingRepair:PlayableCommandKind.StartBuildingRepair,id):null});
                bool sale=lifecycle!=null&&string.IsNullOrEmpty(lifecycle.SaleBlockedReason);
                string detail=sale?"Продать здание":"Продажа запрещена: здание находится в бою";if(TerritoryRules.Center(b.Kind))detail+=" · Продажа удалит все зависимые здания. Последний центр: поражение игрока";
                actions.Add(new OfflinePadAction(id+":sale","Продать здание",sale,detail,"sell"){HoldCommand=sale?Command(PlayableCommandKind.SellBuilding,id):null});
            }
            if(b.Kind==PlayableBuildingKind.Factory)actions.Add(new OfflinePadAction("rally","Точка сбора",true,"A — перейти к размещению; затем A — поставить, B — отменить"));
            return actions.ToArray();
        }
        public static OfflinePadAction[] Build(PlayableSnapshot view,PlayableProfile profile,int siteId,int slotId)
        {
            var site=view.Sites.FirstOrDefault(s=>s.Site.Id==siteId);if(site==null)return Array.Empty<OfflinePadAction>();
            bool occupied=view.Buildings.Any(b=>b.SiteId==siteId&&b.SlotId==slotId);
            if(occupied)return Array.Empty<OfflinePadAction>();
            if(slotId!=0&&(site.Owner!=view.Owner||!site.Ready||!site.Site.Slots.Any(s=>s.Id==slotId)||!view.Buildings.Any(b=>b.Id==site.CenterId&&b.Owner==view.Owner&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true)))return Array.Empty<OfflinePadAction>();
            if(slotId==0&&(site.Claimant!=view.Owner||site.Progress<1))return Array.Empty<OfflinePadAction>();
            var kinds=slotId==0?new[]{site.Site.Kind}:new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter};
            return kinds.Select(kind=>{int cost=TerritoryRules.Cost(profile,kind);bool enabled=view.ExactCredits>=cost;return new OfflinePadAction("build:"+siteId+":"+slotId+":"+kind,"Построить: "+Name(kind),enabled,cost+" кредитов"){Command=enabled?new OfflinePadActionCommand{Kind=PlayableCommandKind.BuildAt,Site=siteId,Slot=slotId,Parent=slotId==0?0:site.CenterId,Building=kind}:null};}).ToArray();
        }
    }
}
