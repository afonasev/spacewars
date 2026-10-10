using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        // Style A presentation colors are shared with the established native HUD;
        // they do not alter gameplay, camera behavior or player profile values.
        private static readonly Color PanelColor=OrbitalTheme.Panel;
        private static readonly Color Ink=OrbitalTheme.Ink;
        private static readonly Color Muted=OrbitalTheme.Muted;
        private static readonly Color Cyan=OrbitalTheme.Cyan;
        private static readonly Color Danger=new Color(.95f,.47f,.43f);
        private static readonly Color Line=OrbitalTheme.Line;
        private static readonly Color MinimapOutline=new Color(.78f,.61f,.40f);
        private static readonly Color ActionColor=new Color(.045f,.105f,.135f);

        private static void StyleRegion(VisualElement element)
        {
            element.style.backgroundColor=PanelColor;
            element.style.borderLeftWidth=1;element.style.borderRightWidth=1;
            element.style.borderTopWidth=1;element.style.borderBottomWidth=1;
            element.style.borderLeftColor=Line;element.style.borderRightColor=Line;
            element.style.borderTopColor=Cyan;element.style.borderBottomColor=Line;
            element.style.paddingLeft=14;element.style.paddingRight=14;
            element.style.paddingTop=12;element.style.paddingBottom=12;
        }

        private static Label Section(string title)
        {
            var label=new Label(title);
            label.style.color=Cyan;label.style.fontSize=13;
            label.style.unityFontStyleAndWeight=FontStyle.Bold;
            label.style.letterSpacing=1;
            label.style.marginBottom=8;
            return label;
        }

        private static void StyleAction(Button button)
        {
            OrbitalTheme.StyleButton(button);button.style.height=38;button.style.minHeight=38;button.style.fontSize=15;button.style.marginRight=6;button.style.marginBottom=5;
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
            if(childMenu!=null)return;
            bool visible=ModalVisible();
            if(visible&&!modalWasVisible)menuNavigation?.SetScope(modalCard,()=>{if(paused)Pause(false);},resumeButton.style.display.value==DisplayStyle.Flex?resumeButton:modalRestartButton);
            else if(!visible&&(modalWasVisible||menuNavigation?.Scope==modalCard))menuNavigation?.SetScope(null);
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
            var factory=view.Buildings.FirstOrDefault(building=>building.Kind==Spacewars.Simulation.PlayableBuildingKind.Factory&&building.Owner==LocalOwner);
            switch(responsiveEvidenceStage)
            {
                case 0:
                    var home=view.Sites.FirstOrDefault(site=>site.Owner==LocalOwner&&site.Ready&&site.Site.Slots.Count>0);
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
            compactHud=panelWidth<1000;
            bool local=localHumanCount>1,left=local&&localViewportIndex%2==0;
            top.style.left=left?new StyleLength(6):new StyleLength(StyleKeyword.Auto);top.style.right=left?new StyleLength(StyleKeyword.Auto):new StyleLength(6);top.style.top=6;top.style.minHeight=26;top.style.paddingTop=top.style.paddingBottom=2;top.style.paddingLeft=top.style.paddingRight=6;
            armyComposition.style.left=left?new StyleLength(6):new StyleLength(StyleKeyword.Auto);armyComposition.style.right=left?new StyleLength(StyleKeyword.Auto):new StyleLength(6);
            creditsLabel.style.fontSize=hudFocusButton.style.fontSize=12;hudFocusButton.style.height=hudFocusButton.style.minHeight=24;
            bottom.style.left=bottom.style.right=0;bottom.style.bottom=0;
            hudRow.style.flexWrap=Wrap.NoWrap;hudRow.style.justifyContent=local?(left?Justify.FlexStart:Justify.FlexEnd):Justify.SpaceBetween;
            armyRegion.style.flexGrow=0;armyRegion.style.flexShrink=1;armyRegion.style.flexBasis=StyleKeyword.Auto;
            armyRegion.style.minWidth=0;armyRegion.style.width=Mathf.Max(160,Mathf.Min(local?270:340,panelWidth-(local?20:(float)profile.MinimapCompactSize+40)));
            armyRegion.style.marginLeft=local?0:8;armyRegion.style.marginRight=0;armyRegion.style.marginTop=0;
            armyRegion.style.paddingLeft=armyRegion.style.paddingRight=4;armyRegion.style.paddingTop=armyRegion.style.paddingBottom=3;
            foreach(var label in armyRegion.Query<Label>().ToList())label.style.fontSize=12;
            selectionLabel.style.fontSize=14;selectionLabel.style.marginBottom=0;noticeLabel.style.fontSize=12;noticeLabel.style.marginRight=0;
            noticeLabel.style.alignSelf=left?Align.FlexStart:Align.FlexEnd;
            buildRegion.style.display=commandRegion.style.display=DisplayStyle.None;
            buildRegion.RemoveFromHierarchy();commandRegion.RemoveFromHierarchy();
            UpdateResponsiveVisibility();
        }
        private void UpdateResponsiveVisibility()
        {
            if(view==null||hudRow==null)return;
            buildRegion.style.display=commandRegion.style.display=DisplayStyle.None;
            armyRegion.style.display=selection.Count>0||selectedSite!=0||spectatorMode&&inspectionEntityId!=0?DisplayStyle.Flex:DisplayStyle.None;
            armyRegion.style.flexGrow=0;
            if(armyRegion.parent!=hudRow)hudRow.Add(armyRegion);
        }
    }
}
