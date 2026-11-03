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
        public RadialActionPresentation Presentation;
        public string[] BlockedReasons=Array.Empty<string>();
        public NavPoint? Point;public bool Repeat;public double? Progress;public double? Price;
        public OfflinePadAction(string id,string label,bool enabled,string detail="",string hold=null){Sector=new OfflinePadSector(id,enabled,hold);Label=label;Detail=detail;}
    }
    public sealed class OfflinePadActionCommand
    {
        public PlayableCommandKind Kind;public int[] Entities=Array.Empty<int>();public NavPoint Point;
        public int Site,Slot,Parent,Target;public PlayableBuildingKind Building=PlayableBuildingKind.Factory;
        public long ProductionOrderId;
        public PlayableEntityKind Unit=PlayableEntityKind.Tank;public PlayableResearchKind Research;
        public PlayableCommandSubmitResult Submit(OfflineLocalSession session,string seat)=>session.Submit(seat,Kind,Entities,Point,Target,Site,Slot,Parent,Building,Unit,Research,productionOrderId:ProductionOrderId);
    }
    // SOURCE gamepadViewContext descriptors derived exclusively from the current
    // owner projection. Presentation never enumerates authority/private opponents.
    public static class OfflinePadWorldActions
    {
        // A captured center pad has one construction choice: keep it focused at rest.
        // Disabled choices remain focused for their reason, while gestures still reject them.
        public static int? RingSector(OfflinePadAction[] actions,double x,double y,double deadzone)
        {
            if(actions.Length==1&&actions[0].Sector.Id.StartsWith("build:",StringComparison.Ordinal))return 0;
            return OfflinePadGestures.RingSector(x,y,actions.Length,deadzone);
        }
        // Presentation order only; authority, prerequisites and research identifiers stay unchanged.
        public static int ResearchMenuOrder(PlayableResearchKind kind)=>kind switch {
            PlayableResearchKind.TankChassis=>0,PlayableResearchKind.ExplorerAssaultGuns=>1,
            PlayableResearchKind.ShkvalGuidance=>2,PlayableResearchKind.HawkSalvoSystems=>3,
            _=>PlayableResearchRules.Weapon(kind)?4:PlayableResearchRules.Defense(kind)?5:PlayableResearchRules.Concrete(kind)?6:7
        };
        public static bool ShkvalAvailable(PlayableSnapshot view)=>view.Buildings.Any(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.Phase==ConstructionPhase.Ready&&b.Health>0&&b.PrivateState?.Lifecycle?.Selling!=true);
        public static string Name(PlayableEntityKind kind)=>kind==PlayableEntityKind.Hawk?"Ястреб":kind==PlayableEntityKind.Explorer?"Исследователь":kind==PlayableEntityKind.Shkval?"Шквал":"Танк";
        public static string Name(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.GaussTower?"Гаусс-башня":kind==PlayableBuildingKind.RepairTower?"Ремонтная вышка":kind==PlayableBuildingKind.SentryTurret?"Пулемётная турель":kind==PlayableBuildingKind.Hangar?"Ангар":kind==PlayableBuildingKind.Headquarters?"Штаб":kind==PlayableBuildingKind.Outpost?"Форпост":kind==PlayableBuildingKind.RelayTower?"Вышка связи":kind==PlayableBuildingKind.Mine?"Шахта":kind==PlayableBuildingKind.Factory?"Фабрика":kind==PlayableBuildingKind.Refinery?"Завод переработки":"Научный центр";
        private static OfflinePadActionCommand Command(PlayableCommandKind kind,int building,PlayableEntityKind unit=PlayableEntityKind.Tank,PlayableResearchKind research=PlayableResearchKind.TankChassis)=>new OfflinePadActionCommand{Kind=kind,Entities=new[]{building},Unit=unit,Research=research};
        public static OfflinePadAction[] Building(PlayableSnapshot view,PlayableProfile profile,int id,bool includeRally=true,bool freeEconomy=false)
        {
            var b=view.Buildings.FirstOrDefault(x=>x.Id==id&&x.Owner==view.Owner);var actions=new List<OfflinePadAction>();
            if(b==null||b.Phase!=ConstructionPhase.Ready||b.PrivateState==null)return actions.ToArray();var state=b.PrivateState;var lifecycle=state.Lifecycle;
            // Only actions admitted by the current owner projection are offered.
            if(lifecycle?.Selling!=true){
                if(b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Hangar){
                    int MenuOrder(PlayableEntityKind unit)=>unit==PlayableEntityKind.Explorer?profile.ExplorerProductionMenuOrder:unit==PlayableEntityKind.Shkval?profile.ShkvalProductionMenuOrder:profile.TankProductionMenuOrder;
                    foreach(var unit in (b.Kind==PlayableBuildingKind.Hangar?new[]{PlayableEntityKind.Hawk}:new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval}).OrderBy(MenuOrder)){
                        double cost=PlayableUnitRules.Cost(profile,unit);bool prerequisite=unit!=PlayableEntityKind.Shkval||ShkvalAvailable(view);bool manual=prerequisite&&(freeEconomy||view.ExactCredits>=cost)&&state.Orders.Count<6;
                        var action=new OfflinePadAction(id+":"+(unit==PlayableEntityKind.Hawk?"hawk":unit==PlayableEntityKind.Explorer?"explorer":unit==PlayableEntityKind.Shkval?"shkval":"tank"),"Произвести: "+Name(unit),prerequisite||(state.Repeat&&state.RepeatKind==unit),PlayableMenuDescriptions.Unit(profile,unit,view.OwnerResearch,freeEconomy:freeEconomy)+(manual?"":(!prerequisite?" · Нужен готовый собственный Научный центр":!freeEconomy&&view.ExactCredits<cost?" · Недостаточно кредитов для разовой покупки":" · Очередь заполнена")),"repeat");
                        action.Price=freeEconomy?null:(double?)cost;action.Command=manual?Command(PlayableUnitRules.ProductionCommand(unit),id,unit):null;
                        action.HoldCommand=prerequisite||(state.Repeat&&state.RepeatKind==unit)?Command(PlayableCommandKind.ToggleRepeatProduction,id,unit):null;action.Repeat=state.Repeat&&state.RepeatKind==unit;if(action.Repeat)action.Label+=" ∞";
                        var active=state.Orders.FirstOrDefault(o=>!o.IsUpgrade&&o.Kind==unit&&o.Active);if(active!=null)action.Progress=active.Progress;
                        if(state.Orders.Count>0&&!action.Repeat)action.Detail+="\nПовтор сохраняет порядок общей очереди";
                        if(action.Repeat&&!action.Progress.HasValue)action.Detail+=freeEconomy?" · ∞: ожидание лимита или выпуска":" · ∞: ожидание ресурсов или лимита";actions.Add(action);
                    }
                }
                if(b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Hangar)
                {
                    var upgrade=state.Orders.FirstOrDefault(o=>o.IsUpgrade);string reason=state.Upgrade?.BlockedReason;
                    bool cancel=upgrade!=null,enabled=cancel||reason==null;
                    string upgradeDetail="Скорость производства ×"+profile.UpgradedProducerSpeedMultiplier.ToString("0.##")+" · "+(freeEconomy?profile.ProvingGroundActionSeconds:profile.ProducerUpgradeSeconds(b.Kind))+" с\n"+(freeEconomy?"Бесплатно":cancel?"Отмена: возврат оплаченной суммы "+upgrade.PaidCost:"Оплата при старте: "+profile.ProducerUpgradeCost(b.Kind)+" кредитов")+"\n"+(reason??"Научный центр готов");
                    actions.Add(new OfflinePadAction(id+":producer-upgrade",cancel?"Отменить улучшение":state.Upgrade?.Complete==true?"Здание улучшено":"Улучшить здание",enabled,upgradeDetail){Price=freeEconomy?null:(double?)profile.ProducerUpgradeCost(b.Kind),Progress=upgrade?.Active==true?(double?)upgrade.Progress:null,Command=enabled?new OfflinePadActionCommand{Kind=cancel?PlayableCommandKind.CancelProductionOrder:PlayableCommandKind.UpgradeProducer,Entities=new[]{id},ProductionOrderId=upgrade?.Id??0}:null});
                }
                if(b.Kind==PlayableBuildingKind.ScientificCenter){
                    foreach(var kind in System.Enum.GetValues(typeof(PlayableResearchKind)).Cast<PlayableResearchKind>().OrderBy(ResearchMenuOrder)){
                        // selectedWorldActions excludes a researched/queued action
                        // whose upgradeCommand is absent; do not retain a fake sector.
                        if(view.OwnerResearch.Any(o=>o.Kind==kind&&(!PlayableResearchRules.Local(kind)||o.CenterId==id))||!PlayableResearchRules.Prerequisite(kind,view.OwnerResearch))continue;
                        var availability=view.ResearchAvailability.FirstOrDefault(a=>a.Kind==kind);if(availability==null)continue;
                        string centerReason=PlayableResearchRules.CenterRequirement(kind,b.ScientificCenterUpgraded);
                        bool enabled=availability.Available&&centerReason==null&&(freeEconomy||view.ExactCredits>=availability.Cost);
                        string suffix=kind==PlayableResearchKind.TankChassis?"tank-chassis":kind==PlayableResearchKind.ExplorerAssaultGuns?"explorer-assault-guns":kind==PlayableResearchKind.ShkvalGuidance?"shkval-guidance":kind.ToString(),label=kind==PlayableResearchKind.TankChassis?"Улучшить танковое шасси":kind==PlayableResearchKind.ExplorerAssaultGuns?"Улучшить штурмовые орудия":kind==PlayableResearchKind.ShkvalGuidance?"Исследовать системы наведения":PlayableResearchRules.Name(kind);
                        actions.Add(new OfflinePadAction(id+":"+suffix,label,enabled,label+(freeEconomy?" · бесплатно":" · "+availability.Cost+" кредитов")+" · "+(freeEconomy?profile.ProvingGroundActionSeconds:PlayableResearchRules.Duration(profile,kind))+" с · "+PlayableResearchRules.Description(profile,kind)+" · "+(enabled?"Готово к запуску":centerReason??availability.BlockedReason??(freeEconomy?"Исследование недоступно":"Недостаточно кредитов"))){Price=freeEconomy?null:(double?)availability.Cost,Command=enabled?Command(PlayableCommandKind.QueueResearch,id,research:kind):null});
                    }
                }
                if(b.Kind==PlayableBuildingKind.Refinery&&state.Upgrade?.Active!=true&&state.Upgrade?.Complete!=true&&!b.RefineryUpgraded){
                    bool science=PlayableResearchRules.UpgradedCenterReady(view.Buildings,view.Owner),enabled=science&&(freeEconomy||view.ExactCredits>=profile.RefineryUpgradeCost);
                    actions.Add(new OfflinePadAction(id+":upgrade","Улучшить завод",enabled,(freeEconomy?"Улучшение завода · бесплатно · "+profile.ProvingGroundActionSeconds+" с\n":"Увеличивает доход завода переработки.\n"+PlayableResearchRules.RefineryDescription(profile)+"\nСтоимость улучшения: "+profile.RefineryUpgradeCost+" кредитов · "+profile.RefineryUpgradeSeconds+" с\n")+(!science?"Требуется готовый собственный улучшенный научный центр":enabled?"Готово к запуску":"Недостаточно кредитов")){Price=freeEconomy?null:(double?)profile.RefineryUpgradeCost,Command=enabled?Command(PlayableCommandKind.UpgradeRefinery,id):null});
                }
                bool repairing=lifecycle?.Repairing==true;bool repair=repairing||lifecycle!=null&&string.IsNullOrEmpty(lifecycle.RepairBlockedReason)&&b.Health<TerritoryRules.MaximumHealth(profile,b);
                actions.Add(new OfflinePadAction(id+":repair",repairing?"Отменить ремонт":"Ремонт",repair,repairing?(lifecycle.WaitingForCredits?"Ожидает кредитов":"Ремонт выполняется"):lifecycle?.RepairBlockedReason??"Здание полностью исправно"){Command=repair?Command(repairing?PlayableCommandKind.CancelBuildingRepair:PlayableCommandKind.StartBuildingRepair,id):null});
                bool sale=lifecycle!=null&&string.IsNullOrEmpty(lifecycle.SaleBlockedReason);
                string detail=sale?"Продать здание":"Продажа запрещена: здание находится в бою";if(TerritoryRules.Center(b.Kind))detail+=" · Продажа удалит все зависимые здания";if(lifecycle?.LastCenter==true)detail+=" · Последний центр: поражение игрока";
                actions.Add(new OfflinePadAction(id+(freeEconomy?":remove":":sale"),freeEconomy?"Убрать здание":"Продать здание",sale,freeEconomy?detail.Replace("Продать","Убрать").Replace("Продажа","Удаление"):detail,"sell"){Price=freeEconomy?null:lifecycle?.Refund,HoldCommand=sale?Command(PlayableCommandKind.SellBuilding,id):null});
            }
            if(includeRally&&lifecycle?.Selling!=true&&PlayableMenuDescriptions.RallyProducer(b.Kind))actions.Add(new OfflinePadAction("rally","Точка сбора",true,"A — флажок · Tab/Select — карта · A — поставить и вернуться · B — отменить"));
            return RadialActionPresentation.Decorate(actions.ToArray(),view,profile,b,freeEconomy);
        }
        public static OfflinePadAction[] Build(PlayableSnapshot view,PlayableProfile profile,int siteId,int slotId,bool freeEconomy=false)
        {
            var site=view.Sites.FirstOrDefault(s=>s.Site.Id==siteId);if(site==null||site.Site.Kind==PlayableBuildingKind.RelayTower)return Array.Empty<OfflinePadAction>();
            bool occupied=view.Buildings.Any(b=>b.SiteId==siteId&&b.SlotId==slotId);
            if(occupied)return Array.Empty<OfflinePadAction>();
            if(slotId!=0&&(site.Owner!=view.Owner||!site.Ready||!site.Site.Slots.Concat(site.Site.DefenseSlots(profile)).Any(s=>s.Id==slotId)||!view.Buildings.Any(b=>b.Id==site.CenterId&&b.Owner==view.Owner&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true)))return Array.Empty<OfflinePadAction>();
            if(slotId==0&&(site.Claimant!=view.Owner||site.Progress<1))return Array.Empty<OfflinePadAction>();
            var kinds=slotId==0?new[]{site.Site.Kind}:TerritoryRules.DefenseSlot(slotId)?new[]{PlayableBuildingKind.SentryTurret,PlayableBuildingKind.RepairTower,PlayableBuildingKind.GaussTower}:new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter,PlayableBuildingKind.Hangar};
            return RadialActionPresentation.Decorate(kinds.Select(kind=>
            {
                int cost=TerritoryRules.Cost(profile,kind);
                bool science=kind!=PlayableBuildingKind.GaussTower||view.Buildings.Any(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true);
                var reasons=new List<string>();
                if(!science)reasons.Add("Нужен научный центр");
                if(kind==PlayableBuildingKind.ScientificCenter&&!TerritoryRules.HasScienceProducer(view.Buildings,view.Owner))reasons.Add("Нужна фабрика или ангар");
                if(!freeEconomy&&view.ExactCredits<cost)
                {
                    // Whole displayed credits must cover the exact fractional shortfall.
                    double missing=Math.Ceiling(cost-view.ExactCredits);
                    double last=missing%10,lastTwo=missing%100;
                    string currency=last==1&&lastTwo!=11?"кредита":"кредитов";
                    reasons.Add("Не хватает "+missing.ToString("0")+" "+currency);
                }
                bool enabled=reasons.Count==0;
                string detail=PlayableMenuDescriptions.Building(profile,kind,freeEconomy:freeEconomy,maxHealth:TerritoryRules.Health(profile,kind)*(1+PlayableResearchRules.BuildingBonus(profile,view.OwnerResearch)/100));
                if(reasons.Count>0)detail+="\n"+string.Join("\n",reasons);
                return new OfflinePadAction("build:"+siteId+":"+slotId+":"+kind,"Построить: "+Name(kind),enabled,detail)
                {
                    BlockedReasons=reasons.ToArray(),Price=freeEconomy?null:(double?)cost,
                    Command=enabled?new OfflinePadActionCommand{Kind=PlayableCommandKind.BuildAt,Site=siteId,Slot=slotId,Parent=slotId==0?0:site.CenterId,Building=kind}:null
                };
            }).ToArray(),view,profile,free:freeEconomy);
        }
    }
}
