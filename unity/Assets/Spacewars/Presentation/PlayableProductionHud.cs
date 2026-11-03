using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        // Canonical six-slot queue is a structural UI contract, matching domain admission.
        private readonly ProductionSlot[] productionSlots=new ProductionSlot[6];
        private int productionFactory;
        private sealed class ProductionSlot
        {
            public Button Button;
            public int Factory;
            public long Order;
        }
        private void CreateProductionHud(VisualElement parent)
        {
            hawkButton.tooltip="ЛКМ — оплатить Ястреба. ПКМ — повтор.";
            hawkButton.RegisterCallback<PointerDownEvent>(evt=>{if(evt.button!=1)return;if(productionFactory!=0)Submit(PlayableCommandKind.ToggleRepeatProduction,new[]{productionFactory},unitKind:PlayableEntityKind.Hawk);root.Focus();evt.StopImmediatePropagation();},TrickleDown.TrickleDown);
            shkvalButton.tooltip="ЛКМ — оплатить Шквал. ПКМ — повтор.";
            shkvalButton.RegisterCallback<PointerDownEvent>(evt=>{if(evt.button!=1)return;if(productionFactory!=0)Submit(PlayableCommandKind.ToggleRepeatProduction,new[]{productionFactory},unitKind:PlayableEntityKind.Shkval);root.Focus();evt.StopImmediatePropagation();},TrickleDown.TrickleDown);
            explorerButton.tooltip="ЛКМ — оплатить Исследователя. ПКМ — повтор.";
            explorerButton.RegisterCallback<PointerDownEvent>(evt=>{if(evt.button!=1)return;if(productionFactory!=0)Submit(PlayableCommandKind.ToggleRepeatProduction,new[]{productionFactory},unitKind:PlayableEntityKind.Explorer);root.Focus();evt.StopImmediatePropagation();},TrickleDown.TrickleDown);
            tankButton.tooltip="ЛКМ — оплатить танк. ПКМ — включить или выключить повтор.";
            tankButton.RegisterCallback<PointerDownEvent>(evt=>
            {
                if(evt.button!=1)return;
                if(productionFactory!=0)Submit(PlayableCommandKind.ToggleRepeatProduction,new[]{productionFactory});
                root.Focus();evt.StopImmediatePropagation();
            },TrickleDown.TrickleDown);
            var slots=new VisualElement{name="production-slots"};slots.style.flexDirection=FlexDirection.Row;slots.style.flexWrap=Wrap.Wrap;slots.style.alignItems=Align.Center;parent.Add(slots);
            for(int i=0;i<productionSlots.Length;i++)
            {
                var slot=new ProductionSlot();
                slot.Button=Button("—",()=>CancelProductionSlot(slot));
                slot.Button.name="production-slot-"+i;
                slot.Button.style.flexGrow=1;slot.Button.style.flexBasis=0;slot.Button.style.minWidth=0;slot.Button.style.paddingLeft=4;slot.Button.style.paddingRight=4;
                slot.Button.style.height=52;slot.Button.style.fontSize=13;
                slot.Button.style.whiteSpace=WhiteSpace.Normal;
                // Resolve the order at press time: an intervening spawn must not retarget cancellation.
                slot.Button.RegisterCallback<PointerDownEvent>(evt=>
                {
                    if(evt.button!=0&&evt.button!=1)return;
                    CancelProductionSlot(slot);root.Focus();evt.StopImmediatePropagation();
                },TrickleDown.TrickleDown);
                slots.Add(slot.Button);productionSlots[i]=slot;
            }
        }
        private static string ProductionName(PlayableProductionOrderSnapshot o)=>o.IsUpgrade?"Улучшение здания":UnitName(o.Kind);
        private static string UnitName(PlayableEntityKind kind)=>kind==PlayableEntityKind.Hawk?"Ястреб":kind==PlayableEntityKind.Shkval?"Шквал":kind==PlayableEntityKind.Explorer?"Исследователь":"Танк";
        private void CancelProductionSlot(ProductionSlot slot)
        {
            if(!spectatorMode&&slot.Factory!=0&&slot.Order!=0)
                Submit(PlayableCommandKind.CancelProductionOrder,new[]{slot.Factory},productionOrderId:slot.Order);
        }
        private void UpdateProductionHud(PlayableBuildingSnapshot factory)
        {
            productionFactory=factory?.Id??0;
            productionSlots[0].Button.parent.style.display=factory==null?DisplayStyle.None:DisplayStyle.Flex;
            tankButton.style.display=factory==null||factory.Kind==PlayableBuildingKind.Hangar?DisplayStyle.None:DisplayStyle.Flex;explorerButton.style.display=tankButton.style.display;shkvalButton.style.display=tankButton.style.display;
            hawkButton.style.display=factory?.Kind==PlayableBuildingKind.Hangar?DisplayStyle.Flex:DisplayStyle.None;

            queueLabel.style.display=factory==null?DisplayStyle.None:DisplayStyle.Flex;
            var state=factory?.PrivateState;
            foreach(var item in new[]{(hawkButton,PlayableEntityKind.Hawk),(tankButton,PlayableEntityKind.Tank),(explorerButton,PlayableEntityKind.Explorer),(shkvalButton,PlayableEntityKind.Shkval)})item.Item1.tooltip=PlayableMenuDescriptions.Unit(profile,item.Item2,view.OwnerResearch,freeEconomy:provingGround!=null)+"\nЛКМ — разовый заказ. ПКМ — повтор.";
            bool enabled=!spectatorMode&&factory!=null&&!factory.SystemsDisabled&&factory.Phase==ConstructionPhase.Ready&&state?.Lifecycle?.Selling!=true&&view.Outcome==PlayableMatchOutcome.Playing&&!paused&&!restarting;
            hawkButton.SetEnabled(enabled);hawkButton.text="Ястреб · "+(provingGround!=null?"бесплатно":profile.HawkCreditCost.ToString())+(state?.Repeat==true&&state.RepeatKind==PlayableEntityKind.Hawk?" · Повтор ВКЛ":"");
            bool shkvalReady=OfflinePadWorldActions.ShkvalAvailable(view);
            shkvalButton.SetEnabled(enabled&&(shkvalReady||state?.Repeat==true&&state.RepeatKind==PlayableEntityKind.Shkval));
            if(!shkvalReady)shkvalButton.tooltip+="\nНужен готовый собственный Научный центр";shkvalButton.text="Шквал · "+(provingGround!=null?"бесплатно":profile.ShkvalCreditCost.ToString())+(state?.Repeat==true&&state.RepeatKind==PlayableEntityKind.Shkval?" · Повтор ВКЛ":"");
            explorerButton.SetEnabled(enabled);explorerButton.text="Исследователь · "+(provingGround!=null?"бесплатно":profile.ExplorerCreditCost.ToString())+(state?.Repeat==true&&state.RepeatKind==PlayableEntityKind.Explorer?" · Повтор ВКЛ":"");
            tankButton.SetEnabled(enabled);tankButton.text="Танк · "+(provingGround!=null?"бесплатно":profile.TankCreditCost.ToString())+(state?.Repeat==true&&state.RepeatKind==PlayableEntityKind.Tank?" · Повтор ВКЛ":"");
            var population=view.Population;
            if(population!=null&&hudFocusButton==null)selectionLabel.text+=" · Армия: "+population.Living+" (+"+population.Reserved+") / "+population.Capacity;
            var orders=state?.Orders;
            var head=orders!=null&&orders.Count>0?orders[0]:null;
            queueLabel.text=state==null?"Выберите фабрику":head!=null&&!head.Active?(head.IsUpgrade?head.BlockedReason:"Ожидает места в армии"):head!=null&&!head.IsUpgrade&&head.Remaining==0?"Выход занят — юнит готов":state.Repeat&&head==null?(population.Living+population.Reserved+PlayableUnitRules.Population(profile,state.RepeatKind)>population.Capacity?"Повтор: ожидает места в армии":(provingGround!=null?"Повтор: ожидает выпуска":"Повтор: ожидает оплаты")):"";
            queueLabel.style.display=factory!=null&&!string.IsNullOrEmpty(queueLabel.text)?DisplayStyle.Flex:DisplayStyle.None;
            if(head!=null&&!head.Active)progress.title=head.IsUpgrade?head.BlockedReason:"Ожидает места в армии";
            else if(head!=null&&!head.IsUpgrade&&head.Remaining==0)progress.title=ProductionName(head)+" готов · выход занят";
            else if(head!=null)progress.title=ProductionName(head)+" · "+(int)(head.Progress*100)+"% · "+System.Math.Ceiling(head.Remaining)+" с";
            if(factory?.Kind==PlayableBuildingKind.Hangar&&factory.LaunchPhase>0)
            {
                string[] phases={"","Открытие люка","Подъём платформы","Раскрутка винта","Взлёт","Опускание платформы","Закрытие люка"};
                int phase=factory.LaunchPhase;progress.title=phases[phase];
                double duration=phase==1||phase==6?profile.HawkRoofSeconds:phase==2||phase==5?profile.HawkPlatformSeconds:phase==3?profile.HawkSpinSeconds:profile.HawkClearance/profile.HawkClimbSpeed;
                progress.value=(float)(100*System.Math.Min(1,factory.LaunchTime/duration));
                queueLabel.text=head!=null&&!head.Active?"Следующий Ястреб ожидает завершения выпуска":"";
                queueLabel.style.display=string.IsNullOrEmpty(queueLabel.text)?DisplayStyle.None:DisplayStyle.Flex;
            }
            for(int i=0;i<productionSlots.Length;i++)
            {
                var slot=productionSlots[i];var order=orders!=null&&i<orders.Count?orders[i]:null;
                slot.Factory=productionFactory;slot.Order=order?.Id??0;
                slot.Button.SetEnabled(enabled&&factory?.SystemsDisabled!=true&&order!=null);
                slot.Button.text=order==null?"—":order.Active?(int)(order.Progress*100)+"%":"Ждёт";
                slot.Button.tooltip=spectatorMode||factory?.SystemsDisabled==true?(order==null?"Свободный слот":ProductionName(order)+" · осталось "+System.Math.Ceiling(order.Remaining)+" с"):order==null?"Свободный слот":ProductionName(order)+" · отменить заказ №"+order.Id+" · осталось "+System.Math.Ceiling(order.Remaining)+" с"+(provingGround!=null?"":" · возврат "+(int)(order.PaidCost*(order.IsUpgrade?1:profile.UnitCancellationRefundRatio)));
                if(order!=null&&!order.IsUpgrade)slot.Button.tooltip=PlayableMenuDescriptions.Unit(profile,order.Kind,view.OwnerResearch,freeEconomy:provingGround!=null)+"\n\n"+slot.Button.tooltip;
                OrbitalPrecision.QueueCard(slot.Button,order==null?(factory?.Kind==PlayableBuildingKind.Hangar?"hawk":"tank"):order.IsUpgrade?OrbitalPrecision.BuildingArtwork(factory.Kind,true):RadialActionPresentation.UnitArtwork(order.Kind,RadialActionPresentation.Upgraded(order.Kind,view.OwnerResearch)),order?.Active==true?order.Progress:-1,order!=null,order?.IsUpgrade!=true&&state?.Repeat==true&&state.RepeatKind==order?.Kind,order==null?null:ProductionName(order),order?.Active==true?System.Math.Ceiling(order.Remaining)+" с · "+(int)(order.Progress*100)+"%":null);
                if(order?.IsUpgrade==true)slot.Button.tooltip+="\n"+(order.BlockedReason??"Ускоряет следующее производство");
                if(order?.Active==true)slot.Button.text+="\n"+System.Math.Ceiling(order.Remaining)+"с";
            }
        }
    }
}
