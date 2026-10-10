using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private string resultUiEvidence;
        private string resultActionEvidence;
        private int resultEvidenceStage,resultCaptureFrame;
        private long resultTerminalGeneration,resultObservedTick;
        private float resultEvidenceStarted;
        private readonly HashSet<int> resultOrderedUnits=new HashSet<int>();
        private readonly Dictionary<int,int> resultFocusedCenters=new Dictionary<int,int>();

        private void DriveResultUiEvidence()
        {
            if(evidence==null||(resultUiEvidence!="victory"&&resultUiEvidence!="defeat")||(view==null&&resultEvidenceStage<91))return;
            if(resultEvidenceStarted==0)resultEvidenceStarted=Time.realtimeSinceStartup;
            // This opt-in Player driver submits only ordinary player commands. A capture
            // requires the runtime's published terminal snapshot, never a UI-only result.
            if(resultEvidenceStage>=90)
            {
                if(resultActionEvidence==null)
                {
                    if(Time.frameCount>resultCaptureFrame+2)Quit();
                    return;
                }
                if(resultActionEvidence=="manual")return;
                if(resultEvidenceStage==90&&Time.frameCount>resultCaptureFrame+2)
                {
                    var action=postMatch?.Root.Q<Button>(resultActionEvidence=="restart"?"result-repeat":"result-menu")??(resultActionEvidence=="restart"?modalRestartButton:modalExitButton);
                    action.Focus();
                    Record("result UI action submit="+resultActionEvidence+" focused="+(root.focusController.focusedElement==action));
                    menuNavigation.Activate();
                    bool requested=resultActionEvidence=="restart"?restarting:returningToMain;
                    Record("result UI action callback="+(requested?"PASS":"FAIL")+" action="+resultActionEvidence);
                    resultEvidenceStarted=Time.realtimeSinceStartup;resultEvidenceStage=91;
                    return;
                }
                if(resultEvidenceStage==91&&resultActionEvidence!="restart"&&inLobby&&runtime==null)
                {
                    Record("result UI menu PASS main_menu=true");ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"result-menu.png"));resultCaptureFrame=Time.frameCount;resultEvidenceStage=92;return;
                }
                if(resultEvidenceStage==91&&resultActionEvidence=="restart"&&view.Generation>resultTerminalGeneration)
                {
                    if(view.Outcome!=PlayableMatchOutcome.Playing||view.Seed!=19092026||view.ProfileId!=profile.ProfileId||view.ProfileRevision!=profile.Revision||ModalVisible()||root.focusController.focusedElement!=root)
                    {
                        Record("result UI restart FAIL generation="+view.Generation+" outcome="+view.Outcome+" seed="+view.Seed+" profile="+view.ProfileId+"@"+view.ProfileRevision+" modal="+ModalVisible());
                        Quit();return;
                    }
                    Record("result UI restart PASS old_generation="+resultTerminalGeneration+" new_generation="+view.Generation+" outcome="+view.Outcome+" seed="+view.Seed+" profile="+view.ProfileId+"@"+view.ProfileRevision+" field_focus=true");
                    ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"result-restart-new-session.png"));
                    resultCaptureFrame=Time.frameCount;resultEvidenceStage=92;
                    return;
                }
                if(resultEvidenceStage==92&&Time.frameCount>resultCaptureFrame+2)Quit();
                if(resultEvidenceStage==91&&Time.realtimeSinceStartup-resultEvidenceStarted>30)
                {
                    Record("result UI action timeout action="+resultActionEvidence+" generation="+view.Generation);
                    Quit();
                }
                return;
            }
            if(view.Outcome!=PlayableMatchOutcome.Playing)
            {
                string actual=view.Outcome==PlayableMatchOutcome.PlayerWon?"victory":"defeat";
                Record("authoritative result="+view.Outcome+" seed="+view.Seed+" profile="+view.ProfileId+"@"+view.ProfileRevision);
                resultTerminalGeneration=view.Generation;
                (postMatch?.Root.Q<Button>("result-repeat")??modalRestartButton).Focus();
                ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"result-"+resultUiEvidence+"-attempt-actual-"+actual+".png"));
                resultCaptureFrame=Time.frameCount;resultEvidenceStage=90;return;
            }
            if(Time.realtimeSinceStartup-resultEvidenceStarted>600)
            {
                CaptureProductionSnapshot(++captures);
                Record("result evidence timeout expected="+resultUiEvidence+" actual="+view.Outcome);
                Quit();resultEvidenceStage=90;resultCaptureFrame=Time.frameCount;return;
            }
            if(paused){Pause(false);return;}
            if(resultUiEvidence=="defeat")
            {
                if(resultEvidenceStage!=0)return;
                var headquarters=view.Buildings.FirstOrDefault(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
                if(headquarters==null)return;
                Record("defeat evidence: player sells own last headquarters through existing command");
                Submit(PlayableCommandKind.SellBuilding,new[]{headquarters.Id});
                resultEvidenceStage=1;return;
            }
            var factory=view.Buildings.FirstOrDefault(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory);
            switch(resultEvidenceStage)
            {
                case 0:
                    var home=view.Sites.FirstOrDefault(site=>site.Owner==PlayableOwner.Player&&site.Ready&&site.Site.Slots.Count>0);
                    if(home==null)return;
                    selectedSite=home.Site.Id;selectedSlot=home.Site.Slots[0].Id;
                    resultEvidenceStage=1;return;
                case 1:
                    Record("victory evidence: build factory at selected home slot");
                    BuildSelected(PlayableBuildingKind.Factory);resultEvidenceStage=2;return;
                case 2:
                    if(factory==null||factory.Phase!=ConstructionPhase.Ready)return;
                    Record("victory evidence: repeat ordinary tank production");
                    Submit(PlayableCommandKind.ToggleRepeatProduction,new[]{factory.Id},unitKind:PlayableEntityKind.Tank);
                    resultEvidenceStage=3;return;
                case 3:
                    if(view.Entities.Count(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank)<3)return;
                    Record("victory evidence: three tanks ready, attack-move toward visible map enemy base");
                    resultEvidenceStage=4;break;
            }
            if(resultEvidenceStage==4)
            {
                if(view.Tick-resultObservedTick>=1800){CaptureProductionSnapshot(++captures);resultObservedTick=view.Tick;}
                var center=view.Buildings.FirstOrDefault(b=>b.Owner==PlayableOwner.Enemy&&TerritoryRules.Center(b.Kind));
                foreach(var tank in view.Entities.Where(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank))
                {
                    if(center!=null)
                    {
                        if(resultFocusedCenters.TryGetValue(tank.Id,out int previous)&&previous==center.Id)continue;
                        Submit(PlayableCommandKind.Attack,new[]{tank.Id},targetId:center.Id);resultFocusedCenters[tank.Id]=center.Id;
                        Record("victory evidence: focus visible hostile center="+center.Id+" unit="+tank.Id);
                    }
                    else if(resultOrderedUnits.Add(tank.Id))Submit(PlayableCommandKind.AttackMove,new[]{tank.Id},new NavPoint(profile.Headquarters(PlayableOwner.Enemy).X-profile.DefenderOffsetX,profile.Headquarters(PlayableOwner.Enemy).Z-profile.DefenderOffsetZ));
                }
            }
        }
#endif
    }
}
