using System;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private static string BuildingName(PlayableBuildingKind kind)
        {
            switch(kind){case PlayableBuildingKind.GaussTower:return "ГАУСС-БАШНЯ";case PlayableBuildingKind.RepairTower:return "РЕМОНТНАЯ ВЫШКА";case PlayableBuildingKind.SentryTurret:return "ПУЛЕМЁТНАЯ ТУРЕЛЬ";case PlayableBuildingKind.Hangar:return "АНГАР";case PlayableBuildingKind.ScientificCenter:return "НАУЧНЫЙ ЦЕНТР";case PlayableBuildingKind.Headquarters:return "ШТАБ";case PlayableBuildingKind.Outpost:return "ФОРПОСТ";case PlayableBuildingKind.RelayTower:return "ВЫШКА СВЯЗИ";case PlayableBuildingKind.Mine:return "ШАХТА";case PlayableBuildingKind.Factory:return "ФАБРИКА";default:return "ПЕРЕРАБОТКА";}
        }
        private bool PickPad(NavPoint point)
        {
            foreach(var state in view.Sites)
            {
                var center=view.Buildings.FirstOrDefault(x=>x.Id==state.CenterId);
                if((center==null||center.Phase==ConstructionPhase.Pending)&&TerritoryRules.Contains(point,state.Site.Position,TerritoryRules.Radius(profile,state.Site.Kind),false,(state.Site.Kind==PlayableBuildingKind.Mine||state.Site.Kind==PlayableBuildingKind.RelayTower)))
                {
                    selection.Clear();selectedSite=state.Site.Id;selectedSlot=0;if(center!=null&&center.Owner==LocalOwner)selection.Add(center.Id);return true;
                }
                if(!state.Ready)continue;
                foreach(var slot in state.Site.Slots.Concat(state.Site.DefenseSlots(profile)))
                {
                    var building=view.Buildings.FirstOrDefault(b=>b.SiteId==state.Site.Id&&b.SlotId==slot.Id);
                    if(building!=null&&building.Phase!=ConstructionPhase.Pending)continue;
                    if(!TerritoryRules.Contains(point,slot.Position,TerritoryRules.DefenseSlot(slot.Id)?profile.TurretFootprintRadius:profile.OrdinaryPadRadius,true,false,slot.Heading))continue;
                    selection.Clear();selectedSite=state.Site.Id;selectedSlot=slot.Id;if(building!=null&&building.Owner==LocalOwner)selection.Add(building.Id);return true;
                }
            }
            foreach(var site in view.DiscoveredSites)
            {
                if(view.Sites.Any(s=>s.Site.Id==site.Id)||!TerritoryRules.Contains(point,site.Position,TerritoryRules.Radius(profile,site.Kind),false,site.Kind==PlayableBuildingKind.Mine))continue;
                selection.Clear();selectedSite=site.Id;selectedSlot=0;
                var pending=view.Buildings.FirstOrDefault(b=>b.SiteId==site.Id&&b.SlotId==0&&b.Owner==LocalOwner&&b.Phase==ConstructionPhase.Pending);
                if(pending!=null)selection.Add(pending.Id);
                return true;
            }
            return false;
        }
        private void BuildSelected(PlayableBuildingKind kind)
        {
            if(runtime==null||paused||restarting||quitting||matchSetup?.Spectator==true)return;
            var site=view.Sites.FirstOrDefault(s=>s.Site.Id==selectedSite);if(site==null)return;
            int parent=selectedSlot==0?0:site.CenterId;
            var intent=new PlayableCommand(generation,++sequence,LocalOwnerId,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:selectedSite,slotId:selectedSlot,buildingKind:kind,parentId:parent);
            var result=runtime.TrySubmit(intent);AudioCommand(intent,result.Accepted);
            notice=result.Accepted?"":Friendly(result.Status);
            Record("build site="+selectedSite+" slot="+selectedSlot+" kind="+kind+" admission="+result.Status);
        }
        private void UpdateSiteHud()
        {
            var state=view.Sites.FirstOrDefault(s=>s.Site.Id==selectedSite);
            var request=view.Buildings.FirstOrDefault(b=>b.SiteId==selectedSite&&b.SlotId==selectedSlot);
            if(request!=null&&request.Owner==LocalOwner&&selectedSite!=0){selection.Clear();selection.Add(request.Id);}
            bool active=!paused&&view.Outcome==PlayableMatchOutcome.Playing&&!restarting;
            bool defense=TerritoryRules.DefenseSlot(selectedSlot);
            bool ordinary=state!=null&&selectedSlot!=0;
            bool sellingCenter=state!=null&&view.Buildings.Any(b=>b.Id==state.CenterId&&b.PrivateState?.Lifecycle?.Selling==true);
            bool collapsing=view.BuildingDestructions.Any(d=>d.SiteId==selectedSite&&d.SlotId==selectedSlot);
            bool owned=!collapsing&&!sellingCenter&&state!=null&&(ordinary?state.Ready&&state.Owner==LocalOwner:state.Progress>=1&&state.Claimant==LocalOwner&&(!state.Owner.HasValue||state.Owner==LocalOwner));
            var discovered=view.DiscoveredSites.FirstOrDefault(s=>s.Id==selectedSite);
            siteLabel.text=state==null?(discovered==null?"Выберите площадку для стройки":BuildingName(discovered.Kind)+" · Разведана, вне обзора"):ordinary?(defense?"ОБОРОНИТЕЛЬНАЯ ПЛОЩАДКА · "+(selectedSlot-TerritoryRules.DefenseSlotOffset):"ПЛОЩАДКА · "+selectedSlot):BuildingName(state.Site.Kind)+" · "+(state.Contested?"СПОРНАЯ":state.Claimant.HasValue?(state.Claimant==view.Owner?"Ваша · ":IsOpponent(state.Claimant.Value)?"Вражеская · ":"Союзная · ")+(int)(state.Progress*100)+"%":"Нейтральная");
            if(collapsing)siteLabel.text+=" · РАЗРУШАЕТСЯ";
            bool scienceReady=view.Buildings.Any(b=>b.Owner==LocalOwner&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true);
            ConfigureBuild(buildGauss,PlayableBuildingKind.GaussTower,ordinary&&defense,active&&owned&&request==null&&scienceReady);
            if(!scienceReady)buildGauss.tooltip+=" · Нужен научный центр";
            ConfigureBuild(buildRepairTower,PlayableBuildingKind.RepairTower,ordinary&&defense,active&&owned&&request==null);
            ConfigureBuild(buildTurret,PlayableBuildingKind.SentryTurret,ordinary&&defense,active&&owned&&request==null);
            ConfigureBuild(buildHangar,PlayableBuildingKind.Hangar,ordinary&&!defense,active&&owned&&request==null);
            ConfigureBuild(buildFactory,PlayableBuildingKind.Factory,ordinary&&!defense,active&&owned&&request==null);
            ConfigureBuild(buildScience,PlayableBuildingKind.ScientificCenter,ordinary&&!defense,active&&owned&&request==null);
            ConfigureBuild(buildRefinery,PlayableBuildingKind.Refinery,ordinary&&!defense,active&&owned&&request==null);
            ConfigureBuild(buildCenter,state?.Site.Kind??PlayableBuildingKind.Headquarters,state!=null&&!ordinary&&state.Site.Kind!=PlayableBuildingKind.RelayTower,active&&owned&&request==null);
            var chosen=view.Buildings.FirstOrDefault(b=>selection.Contains(b.Id)&&b.Owner==LocalOwner);
            cancelBuilding.style.display=chosen!=null&&chosen.Phase!=ConstructionPhase.Ready&&chosen.PrivateState?.Lifecycle?.Selling!=true?DisplayStyle.Flex:DisplayStyle.None;
            cancelBuilding.SetEnabled(active);
            if(request!=null&&request.Phase==ConstructionPhase.Pending)notice=request.BlockedReason??"Ожидает свободной площадки";
        }
        private void ConfigureBuild(Button button,PlayableBuildingKind kind,bool show,bool valid)
        {
            int cost=TerritoryRules.Cost(profile,kind);double seconds=provingGround!=null?profile.ProvingGroundActionSeconds:TerritoryRules.Duration(profile,kind);
            button.text="⬡ "+BuildingName(kind)+(provingGround!=null?" · бесплатно":" · "+cost);
            button.tooltip=(provingGround!=null?BuildingName(kind)+" · бесплатно · "+seconds+" с":PlayableMenuDescriptions.Building(profile,kind,maxHealth:TerritoryRules.Health(profile,kind)*(view.OwnerResearch.Any(o=>o.Kind==PlayableResearchKind.ReinforcedConcrete&&o.Complete)?1+profile.ReinforcedConcreteBonusPercent/100:1)))+(provingGround==null&&view.Credits<cost?" · Недостаточно кредитов":!valid?" · Нужна свободная собственная площадка":"");
            button.style.display=show?DisplayStyle.Flex:DisplayStyle.None;button.SetEnabled(valid&&(provingGround!=null||view.Credits>=cost));
        }
    }
}
