using Spacewars.Simulation;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private VisualElement scienceControls;
        private Button upgradeRefinery;
        private ProgressBar upgradeProgress;
        private Label upgradeReason;
        private readonly Button[] researchButtons=new Button[6];
        private readonly ResearchSlot[] researchSlots=new ResearchSlot[6];
        private sealed class ResearchSlot { public Button Button; public int Center; public long Order; }
        private int scienceRefinery;
        private bool scienceActionAllowed,scienceCancelling;
        private void CreateScienceHud(VisualElement parent)
        {
            scienceControls=new VisualElement{name="science-controls"};scienceControls.style.minWidth=0;parent.Add(scienceControls);
            upgradeRefinery=Button("Улучшить завод",()=>
            {
                if(scienceActionAllowed&&scienceRefinery!=0)Submit(scienceCancelling?PlayableCommandKind.CancelRefineryUpgrade:PlayableCommandKind.UpgradeRefinery,new[]{scienceRefinery});
            });upgradeRefinery.name="upgrade-refinery";scienceControls.Add(upgradeRefinery);
            upgradeProgress=new ProgressBar{lowValue=0,highValue=100,name="refinery-upgrade-progress"};scienceControls.Add(upgradeProgress);StyleProgress(upgradeProgress);
            upgradeReason=new Label{name="refinery-upgrade-reason"};upgradeReason.style.whiteSpace=WhiteSpace.Normal;scienceControls.Add(upgradeReason);
            var row=new VisualElement{name="research-queue"};row.style.flexDirection=FlexDirection.Row;row.style.flexWrap=Wrap.Wrap;scienceControls.Add(row);
            for(int i=0;i<researchButtons.Length;i++){
                var kind=ResearchButtonKind(i,System.Array.Empty<PlayableResearchOrderSnapshot>());Button button=null;
                button=Button(ResearchName(kind),()=>ResearchClick((PlayableResearchKind)button.userData));button.userData=kind;button.name="research-"+kind;
                button.style.flexGrow=1;button.style.flexBasis=Length.Percent(30);button.style.minWidth=0;button.style.flexShrink=1;button.style.paddingLeft=6;button.style.paddingRight=6;button.style.whiteSpace=WhiteSpace.Normal;button.style.height=62;button.style.fontSize=11;button.style.paddingTop=22;
                var glyph=new OrbitalGlyph(OrbitalPrecision.ResearchGlyph(kind));glyph.style.position=Position.Absolute;glyph.style.top=2;glyph.style.alignSelf=Align.Center;glyph.style.height=20;button.Add(glyph);
                row.Add(button);researchButtons[i]=button;
            }
            var slots=new VisualElement{name="research-slots"};slots.style.flexDirection=FlexDirection.Row;slots.style.flexWrap=Wrap.Wrap;slots.style.alignItems=Align.Center;scienceControls.Add(slots);
            for(int i=0;i<researchSlots.Length;i++){var slot=new ResearchSlot();slot.Button=Button("—",()=>CancelResearchSlot(slot));slot.Button.name="research-slot-"+i;slot.Button.style.flexGrow=1;slot.Button.style.flexBasis=0;slot.Button.style.minWidth=0;slot.Button.style.paddingLeft=4;slot.Button.style.paddingRight=4;slot.Button.style.height=52;slot.Button.style.fontSize=12;slot.Button.style.whiteSpace=WhiteSpace.Normal;slots.Add(slot.Button);researchSlots[i]=slot;}
        }
        private void UpdateScienceHud(PlayableBuildingSnapshot building)
        {
            var state=building?.PrivateState?.Upgrade;
            bool science=building?.Kind==PlayableBuildingKind.ScientificCenter;
            bool show=building?.PrivateState!=null&&(spectatorMode||building.Owner==LocalOwner)&&(state!=null||science);
            scienceControls.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
            scienceRefinery=show?building.Id:0;scienceCancelling=!science&&state?.Active==true;
            scienceActionAllowed=!spectatorMode&&show&&!paused&&!restarting&&!quitting&&view.Outcome==PlayableMatchOutcome.Playing&&building.PrivateState.Lifecycle?.Selling!=true&&(science||state.Active||state.BlockedReason==null);
            // Keep blocked actions focusable so keyboard users can discover the reason.
            upgradeRefinery.style.opacity=scienceActionAllowed?1:.45f;
            upgradeRefinery.text=scienceCancelling?"Отменить улучшение · +"+state.PaidCost.ToString("0"):state?.Complete==true?"Завод улучшен":"Улучшить завод · "+profile.RefineryUpgradeCost.ToString("0");
            string reason=paused?"Матч на паузе":state?.BlockedReason;
            upgradeRefinery.tooltip="Цена: "+profile.RefineryUpgradeCost.ToString("0")+" · Время: "+profile.RefineryUpgradeSeconds.ToString("0.#")+" с"+(scienceCancelling?" · Возврат оплаченной суммы":reason==null?"":" · "+reason);
            upgradeReason.text=state==null?"":state.Active?"Доход пока обычный · "+(state.Duration-state.Elapsed).ToString("0.0")+" с":state.Complete?"Доход: "+profile.RefineryUpgradedIncome.ToString("0")+" за "+profile.IncomePeriodSeconds+" с":reason??"Научный центр готов";
            upgradeProgress.style.display=state?.Active==true?DisplayStyle.Flex:DisplayStyle.None;
            upgradeProgress.value=(float)((state?.Progress??0)*100);upgradeProgress.title="Улучшение · "+upgradeProgress.value.ToString("0")+"%";
            upgradeRefinery.style.display=science?DisplayStyle.None:DisplayStyle.Flex;upgradeReason.style.display=science?DisplayStyle.None:DisplayStyle.Flex;upgradeProgress.style.display=science?DisplayStyle.None:upgradeProgress.style.display;
            var research=building?.PrivateState?.Research?.Where(order=>!order.Complete).ToArray();
            var all=building?.PrivateState?.Research??System.Array.Empty<PlayableResearchOrderSnapshot>();
            for(int i=0;i<researchButtons.Length;i++){
                var kind=ResearchButtonKind(i,all);var order=all.FirstOrDefault(o=>o.Kind==kind);var button=researchButtons[i];button.userData=kind;button.name="research-"+kind;
                var availability=view.ResearchAvailability.FirstOrDefault(a=>a.Kind==kind);
                bool prerequisite=PlayableResearchRules.Prerequisite(kind,all)&&(availability?.BlockedReason==null),canCancel=order?.Active==true&&order.CenterId==scienceRefinery;
                button.style.display=science?DisplayStyle.Flex:DisplayStyle.None;button.SetEnabled(science&&scienceActionAllowed&&(canCancel||order==null&&prerequisite));
                double cost=PlayableResearchRules.Cost(profile,kind),duration=PlayableResearchRules.Duration(profile,kind);
                button.text=ResearchName(kind)+(order==null?" · "+cost.ToString("0"):order.Complete?" · готово":order.Active?" · "+(canCancel?"отменить ":"")+(int)(order.Progress*100)+"%":" · ждёт");
                button.tooltip=ResearchName(kind)+" · "+cost.ToString("0")+" кредитов · "+duration.ToString("0.#")+" с\n"+PlayableResearchRules.Description(profile,kind)+(!prerequisite?"\n"+(availability?.BlockedReason??"Сначала завершите первый уровень"):"");
                button.Q<OrbitalGlyph>().Set(OrbitalPrecision.ResearchGlyph(kind),Ink,order?.Active==true?order.Progress:-1);
            }
            for(int i=0;i<researchSlots.Length;i++){var slot=researchSlots[i];var order=research!=null&&i<research.Length?research[i]:null;slot.Center=science?order?.CenterId??scienceRefinery:0;slot.Order=order?.Id??0;slot.Button.style.display=science?DisplayStyle.Flex:DisplayStyle.None;slot.Button.SetEnabled(science&&scienceActionAllowed&&order?.Active==true&&order.CenterId==scienceRefinery);slot.Button.text=order==null?"—":order.Active?(int)(order.Progress*100)+"%\n"+System.Math.Ceiling(order.Duration-order.Elapsed)+"с":order.Complete?"готово":"ждёт";OrbitalPrecision.QueueCard(slot.Button,order==null?"science":OrbitalPrecision.ResearchGlyph(order.Kind),order?.Active==true?order.Progress:-1,order!=null,title:order==null?null:ResearchName(order.Kind),status:order?.Active==true?System.Math.Ceiling(order.Duration-order.Elapsed)+" с · "+(int)(order.Progress*100)+"%":null);slot.Button.tooltip=order==null?"Свободный слот":ResearchName(order.Kind)+(order.Active?" · осталось "+System.Math.Ceiling(order.Duration-order.Elapsed)+" с"+(!spectatorMode&&order.CenterId==scienceRefinery?" · отменить: вернуть "+order.PaidCost:order.CenterId!=scienceRefinery?" · другой центр":""):" · ожидает");}
        }
        private void ResearchClick(PlayableResearchKind kind){if(spectatorMode)return;var b=view?.Buildings.FirstOrDefault(x=>x.Id==scienceRefinery);var order=b?.PrivateState?.Research==null?null:System.Linq.Enumerable.FirstOrDefault(b.PrivateState.Research,x=>x.Kind==kind);if(order?.Active==true)Submit(PlayableCommandKind.CancelResearch,new[]{scienceRefinery},productionOrderId:order.Id);else Submit(PlayableCommandKind.QueueResearch,new[]{scienceRefinery},researchKind:kind);}
        private void CancelResearchSlot(ResearchSlot slot){if(!spectatorMode&&slot.Center!=0&&slot.Order!=0)Submit(PlayableCommandKind.CancelResearch,new[]{slot.Center},productionOrderId:slot.Order);}
        private static PlayableResearchKind ResearchButtonKind(int index,System.Collections.Generic.IEnumerable<PlayableResearchOrderSnapshot> orders)
        {
            if(index==5)return PlayableResearchKind.HawkSalvoSystems;
            if(index<3)return (PlayableResearchKind)index;
            var first=index==3?PlayableResearchKind.ImprovedWeaponSystems:PlayableResearchKind.ImprovedDefensiveSystems;
            return orders.Any(o=>o.Kind==first&&o.Complete)?index==3?PlayableResearchKind.AdvancedWeaponSystems:PlayableResearchKind.AdvancedDefensiveSystems:first;
        }
        private static string ResearchName(PlayableResearchKind kind)=>PlayableResearchRules.Name(kind);
    }
}
