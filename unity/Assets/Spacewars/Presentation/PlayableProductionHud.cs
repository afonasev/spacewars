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
            var slots=new VisualElement();slots.style.flexDirection=FlexDirection.Row;parent.Add(slots);
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
        private static string SlotName(PlayableEntityKind kind)=>kind==PlayableEntityKind.Shkval?"Шквал":kind==PlayableEntityKind.Explorer?"Иссл.":"Танк";
        private static string UnitName(PlayableEntityKind kind)=>kind==PlayableEntityKind.Shkval?"Шквал":kind==PlayableEntityKind.Explorer?"Исследователь":"Танк";
        private void CancelProductionSlot(ProductionSlot slot)
        {
            if(slot.Factory!=0&&slot.Order!=0)
                Submit(PlayableCommandKind.CancelProductionOrder,new[]{slot.Factory},productionOrderId:slot.Order);
        }
        private void UpdateProductionHud(PlayableBuildingSnapshot factory)
        {
            productionFactory=factory?.Id??0;
            productionSlots[0].Button.parent.style.display=factory==null?DisplayStyle.None:DisplayStyle.Flex;
            tankButton.style.display=factory==null?DisplayStyle.None:DisplayStyle.Flex;explorerButton.style.display=tankButton.style.display;shkvalButton.style.display=tankButton.style.display;
            queueLabel.style.display=factory==null?DisplayStyle.None:DisplayStyle.Flex;
            var state=factory?.PrivateState;
            bool enabled=factory!=null&&factory.Phase==ConstructionPhase.Ready&&state?.Lifecycle?.Selling!=true&&view.Outcome==PlayableMatchOutcome.Playing&&!paused&&!restarting;
            shkvalButton.SetEnabled(enabled);shkvalButton.text="Шквал · "+profile.ShkvalCreditCost+(state?.Repeat==true&&state.RepeatKind==PlayableEntityKind.Shkval?" · Повтор ВКЛ":"");
            explorerButton.SetEnabled(enabled);explorerButton.text="Исследователь · "+profile.ExplorerCreditCost+(state?.Repeat==true&&state.RepeatKind==PlayableEntityKind.Explorer?" · Повтор ВКЛ":"");
            tankButton.text="Танк · "+profile.TankCreditCost+(state?.Repeat==true&&state.RepeatKind==PlayableEntityKind.Tank?" · Повтор ВКЛ":"");
            var population=view.Population;
            if(population!=null)selectionLabel.text+=" · Армия: "+population.Living+" (+"+population.Reserved+") / "+population.Capacity;
            var orders=state?.Orders;
            var head=orders!=null&&orders.Count>0?orders[0]:null;
            queueLabel.text=state==null?"Выберите фабрику":head!=null&&!head.Active?"Ожидает места в армии":head!=null&&head.Remaining==0?"Выход занят — юнит готов":state.Repeat&&head==null?(population.Living+population.Reserved+PlayableUnitRules.Population(profile,state.RepeatKind)>population.Capacity?"Повтор: ожидает места в армии":"Повтор: ожидает оплаты"):"ЛКМ / ПКМ по заказу — отмена с возвратом";
            if(head!=null&&!head.Active)progress.title="Ожидает места в армии";
            else if(head!=null&&head.Remaining==0)progress.title=UnitName(head.Kind)+" готов · выход занят";
            else if(head!=null)progress.title=UnitName(head.Kind)+" · "+(int)(head.Progress*100)+"% · "+System.Math.Ceiling(head.Remaining)+" с";
            for(int i=0;i<productionSlots.Length;i++)
            {
                var slot=productionSlots[i];var order=orders!=null&&i<orders.Count?orders[i]:null;
                slot.Factory=productionFactory;slot.Order=order?.Id??0;
                slot.Button.SetEnabled(enabled&&order!=null);
                slot.Button.text=order==null?"—":order.Active?SlotName(order.Kind)+"\n"+(int)(order.Progress*100)+"%":SlotName(order.Kind)+"\n"+(compactHud?"Ждёт":"Ожидает");
                slot.Button.tooltip=order==null?"Свободный слот":UnitName(order.Kind)+" · отменить заказ №"+order.Id+" · осталось "+System.Math.Ceiling(order.Remaining)+" с · возврат "+(int)(order.PaidCost*profile.UnitCancellationRefundRatio);
                slot.Button.style.backgroundColor=order?.Active==true?new Color(.10f,.39f,.38f):new Color(.12f,.25f,.28f);
            }
        }
    }
}
