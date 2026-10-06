using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        // Style A presentation colors are shared with the established native HUD;
        // they do not alter gameplay, camera behavior or player profile values.
        private static readonly Color PanelColor=new Color(.025f,.055f,.07f,.96f);
        private static readonly Color Ink=new Color(.90f,.95f,.94f);
        private static readonly Color Muted=new Color(.64f,.74f,.76f);
        private static readonly Color Cyan=new Color(.48f,.88f,.81f);
        private static readonly Color Danger=new Color(.95f,.47f,.43f);
        private static readonly Color Line=new Color(.18f,.39f,.43f);
        private static readonly Color ActionColor=new Color(.09f,.22f,.25f);

        private static void StyleRegion(VisualElement element)
        {
            element.style.backgroundColor=PanelColor;
            element.style.borderLeftWidth=1;element.style.borderRightWidth=1;
            element.style.borderTopWidth=1;element.style.borderBottomWidth=1;
            element.style.borderLeftColor=Line;element.style.borderRightColor=Line;
            element.style.borderTopColor=Line;element.style.borderBottomColor=Line;
            element.style.paddingLeft=10;element.style.paddingRight=10;
            element.style.paddingTop=8;element.style.paddingBottom=8;
        }

        private static Label Section(string title)
        {
            var label=new Label(title);
            label.style.color=Cyan;label.style.fontSize=12;
            label.style.unityFontStyleAndWeight=FontStyle.Bold;
            label.style.letterSpacing=1;
            label.style.marginBottom=8;
            return label;
        }

        private static void StyleAction(Button button)
        {
            button.style.height=34;button.style.marginRight=6;button.style.marginBottom=5;
            button.style.backgroundColor=ActionColor;button.style.color=Ink;
            button.style.borderLeftWidth=1;button.style.borderRightWidth=1;
            button.style.borderTopWidth=1;button.style.borderBottomWidth=1;
            button.style.borderLeftColor=Line;button.style.borderRightColor=Line;
            button.style.borderTopColor=Line;button.style.borderBottomColor=Line;
            button.RegisterCallback<FocusInEvent>(_=>ActionFocus(button,true));
            button.RegisterCallback<FocusOutEvent>(_=>ActionFocus(button,false));
            button.RegisterCallback<PointerEnterEvent>(_=>ActionFocus(button,true));
            button.RegisterCallback<PointerLeaveEvent>(_=>ActionFocus(button,button.focusController?.focusedElement==button));
        }

        private static void ActionFocus(Button button,bool active)
        {
            var color=active?Cyan:Line;
            button.style.borderLeftColor=color;button.style.borderRightColor=color;
            button.style.borderTopColor=color;button.style.borderBottomColor=color;
        }

        private bool compactHud;
        private bool? compactBuildRelevant;
        private bool? compactArmyRelevant;
        private bool modalWasVisible;
        private bool ModalVisible()=>modal!=null&&modal.style.display.value==DisplayStyle.Flex;
        private void SyncModalFocus()
        {
            bool visible=ModalVisible();
            if(visible)
            {
                var focused=root.focusController?.focusedElement as Button;
                if(!modalWasVisible||focused==null||!modalCard.Contains(focused)||!HudActionVisible(focused))
                    (resumeButton.style.display.value==DisplayStyle.Flex?resumeButton:modalRestartButton).Focus();
            }
            else if(modalWasVisible)root.Focus();
            modalWasVisible=visible;
        }
        private void ToggleHudFocus()
        {
            if(ModalVisible()){(resumeButton.style.display.value==DisplayStyle.Flex?resumeButton:modalRestartButton).Focus();return;}
            if(root.focusController?.focusedElement is Button)root.Focus();
            else hudFocusButton.Focus();
        }
        private void MoveHudFocus(bool backwards)
        {
            var current=root.focusController?.focusedElement as Button;
            var actions=(ModalVisible()?modalCard:root).Query<Button>().ToList().Where(button=>button.focusable&&button.enabledInHierarchy&&HudActionVisible(button)).ToList();
            if(actions.Count==0)return;
            int index=actions.IndexOf(current);
            if(index<0){actions[0].Focus();return;}
            actions[(index+(backwards?actions.Count-1:1))%actions.Count].Focus();
        }
        private bool HudActionVisible(VisualElement element)
        {
            for(var ancestor=element;ancestor!=null&&ancestor!=root;ancestor=ancestor.parent)
                if(ancestor.resolvedStyle.display==DisplayStyle.None)return false;
            return true;
        }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private bool responsiveUiEvidence;
        private int responsiveEvidenceStage;
        private long responsiveCaptureTick;
        private void DriveResponsiveUiEvidence()
        {
            if(!responsiveUiEvidence||evidence==null||view.Outcome!=Spacewars.Simulation.PlayableMatchOutcome.Playing)return;
            // The opt-in unattended Development Player keeps its fixture running when macOS
            // moves focus to the test harness. Normal Player focus-loss pause is unchanged.
            if(paused&&responsiveEvidenceStage<5){Pause(false);return;}
            var factory=view.Buildings.FirstOrDefault(building=>building.Kind==Spacewars.Simulation.PlayableBuildingKind.Factory&&building.Owner==Spacewars.Simulation.PlayableOwner.Player);
            switch(responsiveEvidenceStage)
            {
                case 0:
                    var home=view.Sites.FirstOrDefault(site=>site.Owner==Spacewars.Simulation.PlayableOwner.Player&&site.Ready&&site.Site.Slots.Count>0);
                    if(home==null)return;
                    selectedSite=home.Site.Id;selectedSlot=home.Site.Slots[0].Id;
                    Record("responsive evidence selected site="+selectedSite+" slot="+selectedSlot);
                    responsiveEvidenceStage=1;return;
                case 1:
                    BuildSelected(Spacewars.Simulation.PlayableBuildingKind.Factory);
                    responsiveEvidenceStage=2;return;
                case 2:
                    if(factory==null||factory.Phase!=Spacewars.Simulation.ConstructionPhase.Ready)return;
                    selection.Clear();selection.Add(factory.Id);selectedSite=selectedSlot=0;
                    responsiveEvidenceStage=3;return;
                case 3:
                    if(view.Credits<profile.ShkvalCreditCost+profile.ExplorerCreditCost+profile.TankCreditCost)return;
                    Submit(Spacewars.Simulation.PlayableCommandKind.QueueShkval,new[]{factory.Id});
                    Submit(Spacewars.Simulation.PlayableCommandKind.QueueExplorer,new[]{factory.Id});
                    Submit(Spacewars.Simulation.PlayableCommandKind.QueueTank,new[]{factory.Id});
                    responsiveEvidenceStage=4;return;
                case 4:
                    var orders=factory?.PrivateState?.Orders;
                    // Evidence threshold only: wait until the active bar visibly advances.
                    if(orders==null||orders.Count<3||!orders[0].Active||orders[0].Progress<.05||!orders.Skip(1).Any(order=>!order.Active))return;
                    hudFocusButton.Focus();
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(evidence,"responsive-"+Screen.width+"x"+Screen.height+"-active-waiting.png"));
                    Record("responsive evidence capture active and waiting at "+Screen.width+"x"+Screen.height);
                    responsiveCaptureTick=view.Tick;responsiveEvidenceStage=5;return;
                case 5:
                    if(view.Tick<=responsiveCaptureTick)return;
                    Pause(true);
                    responsiveEvidenceStage=6;return;
                case 6:
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(evidence,"responsive-"+Screen.width+"x"+Screen.height+"-pause.png"));
                    Record("responsive evidence capture pause");responsiveEvidenceStage=7;return;
                case 7:
                    // The screenshot is completed at frame end before exiting this diagnostic Player.
                    Quit();responsiveEvidenceStage=8;
                    return;
            }
        }
#endif
        private void ApplyResponsiveHud()
        {
            if(root==null||root.resolvedStyle.width<=0)return;
            ApplyResponsiveHud(root.resolvedStyle.width);
        }
        private void ApplyResponsiveHud(float panelWidth)
        {
            // Structural minimum of the existing four regions, not a gameplay or designer-tunable size.
            // The minimap itself still uses the validated profile dimension.
            float wideMinimum=(float)profile.MinimapCompactSize+20+4+220+380+220+14+14+40+32;
            bool compact=panelWidth<wideMinimum;
            if(compact==compactHud)return;
            compactHud=compact;
            compactBuildRelevant=null;
            compactArmyRelevant=null;
            var focused=root.focusController?.focusedElement as Button;
            hudRow.style.flexWrap=compact?Wrap.Wrap:Wrap.NoWrap;
            commandRegion.RemoveFromHierarchy();
            hudRow.Insert(compact?2:3,commandRegion);
            if(compact)buildRegion.style.width=StyleKeyword.Auto;
            else buildRegion.style.width=220;
            buildRegion.style.flexGrow=compact?1:0;
            if(compact){buildRegion.style.flexBasis=0;buildRegion.style.minWidth=220;}
            else{buildRegion.style.flexBasis=StyleKeyword.Auto;buildRegion.style.minWidth=StyleKeyword.Auto;}
            buildActions.style.flexDirection=compact?FlexDirection.Row:FlexDirection.Column;
            buildActions.style.flexWrap=compact?Wrap.Wrap:Wrap.NoWrap;
            foreach(var button in buildActions.Children())
            {
                // Two equal action columns are a structural packing rule, not a tunable text size.
                if(compact){button.style.width=Length.Percent(50);button.style.marginRight=0;}
                else{button.style.width=StyleKeyword.Auto;button.style.marginRight=6;}
            }
            if(compact)armyRegion.style.width=Length.Percent(100);
            else armyRegion.style.width=StyleKeyword.Auto;
            armyRegion.style.flexGrow=compact?0:1;
            armyRegion.style.marginLeft=compact?0:14;
            armyRegion.style.marginTop=compact?8:0;
            commandRegion.style.marginLeft=compact?4:14;
            UpdateResponsiveVisibility();
            focused?.Focus();
        }
        private void UpdateResponsiveVisibility()
        {
            if(view==null||hudRow==null)return;
            bool buildRelevant=selectedSite!=0||cancelBuilding.style.display.value==DisplayStyle.Flex;
            bool armyRelevant=!buildRelevant||selection.Count>0;
            if(!compactHud)
            {
                if(buildRegion.style.display.value!=DisplayStyle.Flex)buildRegion.style.display=DisplayStyle.Flex;
                if(armyRegion.style.display.value!=DisplayStyle.Flex)armyRegion.style.display=DisplayStyle.Flex;
                compactBuildRelevant=null;
                compactArmyRelevant=null;
                return;
            }
            if(compactBuildRelevant==buildRelevant&&compactArmyRelevant==armyRelevant)return;
            compactBuildRelevant=buildRelevant;
            compactArmyRelevant=armyRelevant;
            buildRegion.style.display=buildRelevant?DisplayStyle.Flex:DisplayStyle.None;
            armyRegion.style.display=armyRelevant?DisplayStyle.Flex:DisplayStyle.None;
            var focused=root.focusController?.focusedElement as Button;
            bool moved=false;
            if(buildRelevant)
            {
                if(hudRow.IndexOf(buildRegion)!=1){hudRow.Insert(1,buildRegion);moved=true;}
                if(hudRow.IndexOf(commandRegion)!=2){hudRow.Insert(2,commandRegion);moved=true;}
                if(hudRow.IndexOf(armyRegion)!=3){hudRow.Insert(3,armyRegion);moved=true;}
                armyRegion.style.width=Length.Percent(100);
                armyRegion.style.flexGrow=0;
                armyRegion.style.marginLeft=0;
                armyRegion.style.marginTop=8;
            }
            else
            {
                if(hudRow.IndexOf(armyRegion)!=1){hudRow.Insert(1,armyRegion);moved=true;}
                if(hudRow.IndexOf(commandRegion)!=2){hudRow.Insert(2,commandRegion);moved=true;}
                armyRegion.style.width=StyleKeyword.Auto;
                armyRegion.style.flexGrow=1;
                armyRegion.style.marginLeft=14;
                armyRegion.style.marginTop=0;
            }
            if(focused!=null&&((buildRegion.style.display.value==DisplayStyle.None&&buildRegion.Contains(focused))||(armyRegion.style.display.value==DisplayStyle.None&&armyRegion.Contains(focused))))root.Focus();
            else if(moved)focused?.Focus();
        }
    }
}
