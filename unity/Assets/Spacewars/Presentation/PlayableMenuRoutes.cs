using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private NativeMenuNavigation menuNavigation;
        private VisualElement childMenu;
        private Button pauseSettings,pauseLab,pauseMain;
        private bool returningToMain;
        private bool MenuOwnsInput=>inLobby||preparing||paused||childMenu!=null||ModalVisible();
        private void OpenPauseSettings()
        {
            if(!paused)return;modal.style.display=DisplayStyle.None;
            childMenu=NativeSettingsView.Open(root,menuNavigation,()=>{
                childMenu=null;modal.style.display=DisplayStyle.Flex;
                menuNavigation.SetScope(modalCard,()=>Pause(false),pauseSettings);
            });
        }
        private void ReturnToMainMenu()
        {
            returningToMain=true;ReturnToLobby();
        }
        private void TickMenuInput()
        {
            bool owned=MenuOwnsInput;menuNavigation?.Tick();
            if(!owned&&!MenuOwnsInput&&Gamepad.current?.startButton.wasPressedThisFrame==true)Pause(true);
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
                    else {modal.style.display=DisplayStyle.Flex;menuNavigation.SetScope(modalCard,()=>Pause(false),pauseLab);}
                },fromMain?null:next=>runtime.RequestBalance(next,generation,view.ProfileRevision),fromMain?null:runtime.Latest.ActiveProfile);
                childMenu=page.Page;
            }catch(System.Exception e){Debug.LogException(e);notice="Не удалось открыть лабораторию: "+e.Message;}
        }
        private void RebindPresentation()
        {
            if(view?.ActiveProfile==null||ReferenceEquals(profile,view.ActiveProfile))return;
            profile=view.ActiveProfile;world.Rebind(profile);routedGeometry=null;explorerRouter=null;router?.Dispose();router=null;
        }
        private void CloseChildMenu()
        {childMenu?.RemoveFromHierarchy();childMenu=null;}
    }
}
