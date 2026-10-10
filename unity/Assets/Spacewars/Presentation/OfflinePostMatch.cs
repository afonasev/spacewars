using System.Collections;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    public sealed partial class OfflineTwoLocalBootstrap
    {
        private PostMatchResultsView offlineResult;
        private NativeMenuNavigation resultNavigation;
        private bool offlineResultOverview,offlineFinishConfirmed,offlineChangingScene;
        private Button offlineFinish,offlineReopen;
        private bool OfflineResultVisible=>offlineResult!=null&&offlineResult.Root.style.display.value==DisplayStyle.Flex;
        private void CreateOfflineResultActions()
        {
            resultNavigation=new NativeMenuNavigation(root,()=>assignedPad);
            offlineFinish=OrbitalTheme.Action("Завершить матч",ConfirmOfflineFinish,"offline-finish");lobby.Add(offlineFinish);resultNavigation.SetScope(lobby,()=>Resume(),resumeButton);
            offlineReopen=OrbitalTheme.Action("Статистика",ShowOfflineResult,"offline-result-reopen");offlineReopen.style.position=Position.Absolute;offlineReopen.style.left=Length.Percent(50);offlineReopen.style.top=50;offlineReopen.style.display=DisplayStyle.None;root.Add(offlineReopen);
        }
        private void ConfirmOfflineFinish(){if(session==null||runtime.Result!=null)return;if(!offlineFinishConfirmed){offlineFinishConfirmed=true;offlineFinish.text="Подтвердить завершение";}else session.FinishManually();}
        private void ShowOfflineResult(){if(offlineResult==null)return;offlineResultOverview=false;offlineResult.Root.style.display=DisplayStyle.Flex;resultNavigation.SetScope(offlineResult.Root,OverviewOfflineResult,offlineResult.FirstTab);}
        private void OverviewOfflineResult(){offlineResultOverview=true;offlineResult.Root.style.display=DisplayStyle.None;resultNavigation.SetScope(null);root.Focus();}
        private void UpdateOfflineResult()
        {
            offlineFinish.style.display=session!=null&&session.Paused&&runtime.Result==null?DisplayStyle.Flex:DisplayStyle.None;
            if(runtime.Result==null){if(session==null||session.Paused){if(resultNavigation.Scope!=lobby)resultNavigation.SetScope(lobby,()=>Resume(),resumeButton);}else if(resultNavigation.Scope==lobby)resultNavigation.SetScope(null);return;}
            lobby.style.display=DisplayStyle.None;offlineReopen.style.display=offlineResultOverview?DisplayStyle.Flex:DisplayStyle.None;
            if(offlineResult==null){offlineResult=new PostMatchResultsView(runtime.Result,OverviewOfflineResult,()=>ChangeOfflineScene(true),()=>ChangeOfflineScene(false),p=>(p.IsAi?"ИИ ":"Игрок ")+(p.Slot+1),p=>Paint(p.Owner),f=>f.Kind==MatchFactKind.AiDecision?PostMatchResultsView.FactLabel(f):f.Kind==MatchFactKind.UnitCompleted?OfflinePadWorldActions.Name((PlayableEntityKind)f.EntityKind):OfflinePadWorldActions.Name((PlayableBuildingKind)f.EntityKind),f=>f.Kind==MatchFactKind.AiDecision?"◆":f.Kind==MatchFactKind.BuildingCompleted?"⬡":f.EntityKind==(int)PlayableEntityKind.Explorer?"◇":f.EntityKind==(int)PlayableEntityKind.Shkval?"△":"▣");root.Add(offlineResult.Root);ShowOfflineResult();}
            if(OfflineResultVisible){keyboard.WorldInputEnabled=false;gestures.Cancel();padRing.style.display=DisplayStyle.None;resultNavigation.Tick();}
        }
        private void ChangeOfflineScene(bool mainMenu){if(offlineChangingScene)return;offlineChangingScene=true;StartCoroutine(ChangeOfflineSceneWhenStopped(mainMenu));}
        private IEnumerator ChangeOfflineSceneWhenStopped(bool mainMenu){runtime.RequestStop();while(!runtime.IsStopped)yield return null;PlayableBootstrap.BypassTwoLocalOnce=mainMenu;SceneManager.LoadScene(SceneManager.GetActiveScene().name);}
    }
}
