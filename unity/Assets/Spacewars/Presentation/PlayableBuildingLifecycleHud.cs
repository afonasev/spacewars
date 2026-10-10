using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private Button sellBuilding,repairBuilding;
        private Label lifecycleLabel;
        private int lifecycleBuilding,confirmSaleBuilding;
        private PlayableBuildingLifecycleSnapshot lifecycleState;
        private void CreateBuildingLifecycleHud(VisualElement parent)
        {
            sellBuilding=Button("$ Продать",ConfirmBuildingSale);sellBuilding.name="sell-building";parent.Add(sellBuilding);
            repairBuilding=Button("Ремонт",()=>
            {
                if(lifecycleBuilding!=0)Submit(lifecycleState?.Repairing==true?PlayableCommandKind.CancelBuildingRepair:PlayableCommandKind.StartBuildingRepair,new[]{lifecycleBuilding});
            });repairBuilding.name="repair-building";parent.Add(repairBuilding);
            lifecycleLabel=new Label();lifecycleLabel.name="building-lifecycle-status";lifecycleLabel.style.whiteSpace=WhiteSpace.Normal;parent.Add(lifecycleLabel);
        }
        private void ConfirmBuildingSale()
        {
            if(lifecycleBuilding==0||lifecycleState==null||lifecycleState.SaleBlockedReason!=null)return;
            if(confirmSaleBuilding!=lifecycleBuilding){confirmSaleBuilding=lifecycleBuilding;sellBuilding.text="✓ Подтвердить продажу";return;}
            Submit(PlayableCommandKind.SellBuilding,new[]{lifecycleBuilding});confirmSaleBuilding=0;
        }
        private void UpdateBuildingLifecycleHud(PlayableBuildingSnapshot building)
        {
            int id=building?.PrivateState!=null?building.Id:0;
            if(id!=lifecycleBuilding)confirmSaleBuilding=0;
            lifecycleBuilding=id;lifecycleState=building?.PrivateState?.Lifecycle;
            bool available=id!=0&&lifecycleState!=null&&!paused&&!restarting&&view.Outcome==PlayableMatchOutcome.Playing;
            var state=lifecycleState;
            if(!available||state.SaleBlockedReason!=null)confirmSaleBuilding=0;
            sellBuilding.text=confirmSaleBuilding==id&&id!=0?"✓ Подтвердить продажу":"$ Продать";
            sellBuilding.SetEnabled(available&&state.SaleBlockedReason==null);
            sellBuilding.tooltip=state?.SaleBlockedReason??"Возврат: "+(state?.Refund??0)+" · зданий: "+(state?.CascadeCount??0);
            repairBuilding.text=state?.Repairing==true?"Отменить ремонт":"Ремонт";
            repairBuilding.SetEnabled(available&&(state.Repairing||state.RepairBlockedReason==null));
            repairBuilding.style.backgroundColor=state?.Repairing==true?new Color(.55f,.15f,.12f):new Color(.12f,.25f,.28f);
            repairBuilding.tooltip=state?.RepairBlockedReason??"Восстановить повреждённое здание";
            lifecycleLabel.text=state==null?"":state.Selling?"Демонтаж · "+(int)(state.SaleProgress*100)+"%":state.Repairing?(state.WaitingForCredits?"Ремонт: ожидает кредитов":"Ремонт · "+state.PaidSeconds.ToString("0.#")+" / "+state.RepairDuration.ToString("0.#")+" с"):
                "$ "+state.Refund+" · зданий: "+state.CascadeCount+(state.LastCenter?"\nПоследний центр: поражение после демонтажа":"");
        }
        private void OnApplicationFocus(bool focused){localInputFocused=focused;if(localCoordinator==null)foreach(var seat in localPresentations){seat.localInputFocused=focused;seat.input?.SetFocus(focused);}if(!focused){SetSystemCursorHidden(false);confirmSaleBuilding=0;if(localCoordinator==null&&runtime!=null)Pause(true);}}
    }
}
