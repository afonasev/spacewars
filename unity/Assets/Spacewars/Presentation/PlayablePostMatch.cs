using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private PostMatchResultsView postMatch;
        private bool resultOverview,finishConfirmation;
        private Button finishMatch,resultReopen;
        private bool ResultsVisible=>postMatch!=null&&postMatch.Root.style.display.value==DisplayStyle.Flex;
        private void CreatePostMatchActions()
        {
            finishMatch=OrbitalTheme.Action("Завершить матч",()=>{if(!finishConfirmation){finishConfirmation=true;finishMatch.text="Подтвердить завершение";return;}runtime?.RequestFinish();},"pause-finish");modalCard.Add(finishMatch);
            resultReopen=OrbitalTheme.Action("Статистика",()=>{resultOverview=false;ShowPostMatch();},"result-reopen");resultReopen.style.display=DisplayStyle.None;top.Add(resultReopen);
        }
        private void ResetPostMatch(){postMatch?.Root.RemoveFromHierarchy();postMatch=null;resultOverview=finishConfirmation=false;if(finishMatch!=null)finishMatch.text="Завершить матч";}
        private void UpdatePostMatch()
        {
            if(localCoordinator!=null)return;
            finishMatch.style.display=view.Outcome==PlayableMatchOutcome.Playing&&paused&&!restarting?DisplayStyle.Flex:DisplayStyle.None;
            resultReopen.style.display=resultOverview&&runtime?.Result!=null?DisplayStyle.Flex:DisplayStyle.None;
            if(runtime?.Result==null||restarting||returningToLobby)return;
            if(postMatch==null)
            {
                postMatch=new PostMatchResultsView(runtime.Result,()=>{ClosePostMatchOverview();},ReturnToMainMenu,Restart,
                    p=>matchSetup?.Participants!=null||matchSetup?.Foundry==true?matchSetup.ParticipantName(p.Slot):matchSetup!=null?(p.IsAi?matchSetup.MatchAiName:matchSetup.MatchHumanName):p.IsAi?"ИИ":"Игрок",
                    p=>matchSetup!=null?LobbyPaint(p.Owner):PostMatchResultsView.Palette[p.Slot],
                    f=>f.Kind==MatchFactKind.AiDecision?PostMatchResultsView.FactLabel(f):f.Kind==MatchFactKind.UnitCompleted?UnitName((PlayableEntityKind)f.EntityKind):BuildingName((PlayableBuildingKind)f.EntityKind),
                    f=>f.Kind==MatchFactKind.AiDecision?"◆":f.Kind==MatchFactKind.UnitCompleted?UnitSymbol((PlayableEntityKind)f.EntityKind):"⬡");
                root.Add(postMatch.Root);ShowPostMatch();
            }
        }
        private void ClosePostMatchOverview(){resultOverview=true;paused=false;postMatch.Root.style.display=DisplayStyle.None;foreach(var seat in localPresentations){seat.root.style.display=DisplayStyle.Flex;seat.ExpandLocalMenu(false);}menuNavigation.SetScope(null);root.Focus();}
        private void ShowPostMatch(){if(postMatch==null)return;foreach(var seat in localPresentations)seat.root.style.display=seat==this?DisplayStyle.Flex:DisplayStyle.None;ExpandLocalMenu(true);postMatch.Root.style.display=DisplayStyle.Flex;menuNavigation.SetScope(postMatch.Root,()=>{ClosePostMatchOverview();},postMatch.FirstTab);}
    }
}
