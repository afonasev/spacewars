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
        private Button finishMatch,resultReopen,surrenderMatch;
        private string surrenderOwner;
        private bool ResultsVisible=>postMatch!=null&&postMatch.Root.style.display.value==DisplayStyle.Flex;
        private void CreatePostMatchActions()
        {
            finishMatch=OrbitalTheme.Action("Завершить матч",()=>{if(!finishConfirmation){finishConfirmation=true;finishMatch.text="Подтвердить завершение";return;}runtime?.RequestFinish();},"pause-finish");modalCard.Add(finishMatch);
            surrenderMatch=OrbitalTheme.Action("Сдаться",()=>{
                var seat=localPauseOwner??this;
                if(seat.view==null)return;
                if(surrenderOwner!=seat.view.OwnerId){surrenderOwner=seat.view.OwnerId;surrenderMatch.text="Подтвердить сдачу";return;}
                if(runtime?.RequestSurrender(surrenderOwner)==true){surrenderOwner=null;Pause(false);}
            },"pause-surrender");
            modalCard.Add(surrenderMatch);
            surrenderMatch.style.display=DisplayStyle.None;
            resultReopen=OrbitalTheme.Action("Статистика",()=>{resultOverview=false;ShowPostMatch();},"result-reopen");resultReopen.style.display=DisplayStyle.None;top.Add(resultReopen);
        }
        private void ResetPostMatch(){postMatch?.Root.RemoveFromHierarchy();postMatch=null;resultOverview=finishConfirmation=false;if(finishMatch!=null)finishMatch.text="Завершить матч";}
        private void UpdatePostMatch()
        {
            if(localCoordinator!=null)return;
            if(campaignConfiguration!=null){finishMatch.style.display=resultReopen.style.display=surrenderMatch.style.display=DisplayStyle.None;return;}
            var surrenderSeat=(localPauseOwner??this).view;
            bool canSurrender=paused&&!restarting&&provingGround==null&&matchSetup?.Spectator==false&&
                surrenderSeat?.Outcome==PlayableMatchOutcome.Playing&&!surrenderSeat.OwnerEliminated&&surrenderSeat.Participants.Count>0;
            surrenderMatch.style.display=canSurrender?DisplayStyle.Flex:DisplayStyle.None;
            if(!canSurrender||surrenderOwner!=surrenderSeat.OwnerId){surrenderOwner=null;surrenderMatch.text="Сдаться";}
            finishMatch.style.display=view.Outcome==PlayableMatchOutcome.Playing&&paused&&!restarting?DisplayStyle.Flex:DisplayStyle.None;
            resultReopen.style.display=resultOverview&&runtime?.Result!=null?DisplayStyle.Flex:DisplayStyle.None;
            if(runtime?.Result==null||restarting||returningToLobby)return;
            if(postMatch==null)
            {
                if(WaitingForAiDefeatLine(runtime.Result))return;
                postMatch=new PostMatchResultsView(runtime.Result,()=>{ClosePostMatchOverview();},ReturnToMainMenu,Restart,
                    p=>matchSetup?.Participants!=null||matchSetup?.Foundry==true?matchSetup.ParticipantName(p.Slot):matchSetup!=null?(p.IsAi?matchSetup.MatchAiName:matchSetup.MatchHumanName):p.IsAi?"ИИ":"Игрок",
                    p=>matchSetup!=null?LobbyPaint(p.Owner):PostMatchResultsView.Palette[p.Slot],
                    f=>f.Kind==MatchFactKind.AiDecision||f.Kind==MatchFactKind.ResearchOrdered||f.Kind==MatchFactKind.RefineryUpgradeOrdered?PostMatchResultsView.FactLabel(f):f.Kind==MatchFactKind.UnitOrdered?UnitName((PlayableEntityKind)f.EntityKind):BuildingName((PlayableBuildingKind)f.EntityKind),
                    f=>f.Kind==MatchFactKind.AiDecision||f.Kind==MatchFactKind.ResearchOrdered||f.Kind==MatchFactKind.RefineryUpgradeOrdered?"◆":f.Kind==MatchFactKind.UnitOrdered?UnitSymbol((PlayableEntityKind)f.EntityKind):"⬡");
                root.Add(postMatch.Root);ShowPostMatch();
            }
        }
        private void ClosePostMatchOverview(){resultOverview=true;paused=false;postMatch.Root.style.display=DisplayStyle.None;foreach(var seat in localPresentations){seat.root.style.display=DisplayStyle.Flex;seat.ExpandLocalMenu(false);}menuNavigation.SetScope(null);root.Focus();}
        private void ShowPostMatch(){if(postMatch==null)return;foreach(var seat in localPresentations)seat.root.style.display=seat==this?DisplayStyle.Flex:DisplayStyle.None;ExpandLocalMenu(true);postMatch.Root.style.display=DisplayStyle.Flex;menuNavigation.SetScope(postMatch.Root,()=>{ClosePostMatchOverview();},postMatch.FirstTab);}
    }
}
