using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private NativeMenuNavigation menuNavigation;
        private VisualElement childMenu;
        private Button pauseSettings,pauseMain;
        private bool returningToMain;
        private bool MenuOwnsInput=>inLobby||preparing||paused||childMenu!=null||ModalVisible()||ResultsVisible;
        private void OpenPauseHelp()
        {
            if(!paused)return;modal.style.display=DisplayStyle.None;
            childMenu=NativeControlsHelp.Open(root,menuNavigation,()=>{childMenu=null;modal.style.display=DisplayStyle.Flex;menuNavigation.SetScope(modalCard,()=>Pause(false),modalCard.Q<Button>("pause-controls"));});
        }
        private void OpenPauseSettings()
        {
            if(!paused)return;modal.style.display=DisplayStyle.None;
            childMenu=NativeSettingsView.Open(root,menuNavigation,()=>{
                childMenu=null;modal.style.display=DisplayStyle.Flex;
                menuNavigation.SetScope(modalCard,()=>Pause(false),pauseSettings);
            },LocalControlSettings);
        }
        private void ReturnToMainMenu()
        {
            if(localCoordinator!=null){localCoordinator.ReturnToMainMenu();return;}
            returningToMain=true;ReturnToLobby();
        }
        private void TickMenuInput()
        {
            bool owned=MenuOwnsInput;var owner=localPauseOwner??this;owner.menuNavigation?.Tick();
            if(!owned&&!MenuOwnsInput){var pressed=localPresentations.FirstOrDefault(seat=>seat.SeatPad?.startButton.wasPressedThisFrame==true);if(pressed!=null)PauseFrom(pressed,true);else if(localPresentations.Count==0&&Gamepad.current?.startButton.wasPressedThisFrame==true)Pause(true);}
        }
        private NativeBalanceStore balanceStore;
        private void EnsureBalanceStore(){if(balanceStore!=null)return;string path=null;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(orbitalEvidence!=null)path=System.IO.Path.Combine(orbitalEvidence,"native-balance.json");
#endif
            balanceStore=new NativeBalanceStore(profile,path);
        }
        private void OpenLaboratory()
        {
            try{
                EnsureBalanceStore();bool fromMain=inLobby;
                if(fromMain)nativeMainMenu.ScreenRoot.style.display=DisplayStyle.None;else {Pause(true);modal.style.display=DisplayStyle.None;}
                var page=new NativeBalanceView(root,menuNavigation,balanceStore,()=>{
                    childMenu=null;
                    if(fromMain)nativeMainMenu.RestoreFocus(nativeMainMenu.ScreenRoot.Q<Button>("main-laboratory"));
                    else {modal.style.display=DisplayStyle.Flex;menuNavigation.SetScope(modalCard,()=>Pause(false),resumeButton);}
                },fromMain?null:next=>runtime.RequestBalance(ComposeLobbyProfile(next),generation,view.ProfileRevision),fromMain?null:runtime.Latest.ActiveProfile);
                childMenu=page.Page;
            }catch(System.Exception e){Debug.LogException(e);notice="Не удалось открыть лабораторию: "+e.Message;}
        }
        private void RebindPresentation()
        {
            if(view?.ActiveProfile==null||ReferenceEquals(profile,view.ActiveProfile))return;
            profile=view.ActiveProfile;world.Rebind(profile); // Shared route service rebinds its solver cache from authority publication.
        }
        private void CloseChildMenu()
        {childMenu?.RemoveFromHierarchy();childMenu=null;}
    }
}
