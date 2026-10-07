#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using Spacewars.Simulation;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private string orbitalEvidence;
        private IEnumerator CaptureOrbitalUiEvidence()
        {
            Directory.CreateDirectory(orbitalEvidence);AudioListener.volume=0;
            IEnumerator Frame(string filename)
            {
                yield return new WaitForSecondsRealtime(.4f);
                ScreenCapture.CaptureScreenshot(Path.Combine(orbitalEvidence,filename));
                var panel=GetComponent<UIDocument>().panelSettings;
                File.AppendAllText(Path.Combine(orbitalEvidence,"layout.txt"),filename+" screen="+Screen.width+"x"+Screen.height+" scale="+panel.scale+" logical="+root.worldBound+" tick="+(view?.Tick??0)+"\n");
                yield return new WaitForSecondsRealtime(.3f);
            }
            void Activate(VisualElement container,string name){var action=container.Q<Button>(name);if(action==null||!action.enabledInHierarchy)throw new InvalidOperationException("Missing enabled action: "+name);action.Focus();menuNavigation.Activate();}
            yield return new WaitForSecondsRealtime(2);
            yield return Frame("01-main.png");
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-orbitalResizeEvidence")>=0)
            {
                int width=Screen.width,height=Screen.height;
                foreach(var size in new[]{new Vector2Int(800,620),new Vector2Int(1920,1080),new Vector2Int(width,height)})
                {Screen.SetResolution(size.x,size.y,false);yield return new WaitForSecondsRealtime(1);yield return Frame("resize-"+size.x+"x"+size.y+".png");}
            }
            Activate(nativeMainMenu.ScreenRoot,"main-settings");yield return Frame("02-main-settings.png");menuNavigation.Back();
            Activate(nativeMainMenu.ScreenRoot,"main-laboratory");yield return Frame("03-main-laboratory.png");
            childMenu.Q<TextField>("lab-name").Focus();menuNavigation.Activate();yield return Frame("03b-keyboard.png");menuNavigation.Back();menuNavigation.Back();
            Activate(nativeMainMenu.ScreenRoot,"main-play");yield return new WaitForSecondsRealtime(1);yield return Frame("04-lobby.png");
            launchButton.Focus();menuNavigation.Activate();
            float deadline=Time.realtimeSinceStartup+30;while((preparing||inLobby)&&Time.realtimeSinceStartup<deadline)yield return null;
            if(inLobby||runtime==null)throw new InvalidOperationException("Match did not start.");
            yield return new WaitForSecondsRealtime(2);
            yield return Frame("04b-match.png");
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-orbitalHudEvidence")>=0)
            {
                var home=view.Sites.First(site=>site.Owner==PlayableOwner.Player&&site.Ready&&site.Site.Slots.Count>1);
                selectedSite=home.Site.Id;selectedSlot=home.Site.Slots[0].Id;
                yield return Frame("04c-construction.png");BuildSelected(PlayableBuildingKind.Factory);
                deadline=Time.realtimeSinceStartup+20;
                while(!runtime.Latest.Buildings.Any(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready)&&Time.realtimeSinceStartup<deadline)yield return null;
                var factory=runtime.Latest.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory);
                selection.Clear();selection.Add(factory.Id);selectedSite=selectedSlot=0;
                foreach(var kind in new[]{PlayableCommandKind.QueueTank,PlayableCommandKind.QueueExplorer,PlayableCommandKind.QueueShkval})Submit(kind,new[]{factory.Id});
                yield return new WaitForSecondsRealtime(1);yield return Frame("04d-production.png");
                if(runtime.Latest.Buildings.Single(b=>b.Id==factory.Id).PrivateState.Orders.Count<3)throw new InvalidOperationException("Production evidence lacks occupied queue.");
                selectedSite=home.Site.Id;selectedSlot=home.Site.Slots[1].Id;BuildSelected(PlayableBuildingKind.ScientificCenter);
                deadline=Time.realtimeSinceStartup+20;
                while(!runtime.Latest.Buildings.Any(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.Phase==ConstructionPhase.Ready)&&Time.realtimeSinceStartup<deadline)yield return null;
                var science=runtime.Latest.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.ScientificCenter);
                selection.Clear();selection.Add(science.Id);selectedSite=selectedSlot=0;
                foreach(PlayableResearchKind kind in Enum.GetValues(typeof(PlayableResearchKind)))Submit(PlayableCommandKind.QueueResearch,new[]{science.Id},researchKind:kind);
                yield return new WaitForSecondsRealtime(1);yield return Frame("04e-research.png");
                if(runtime.Latest.Buildings.Single(b=>b.Id==science.Id).PrivateState.Research.Count<3)throw new InvalidOperationException("Research evidence lacks three orders.");
                ToggleMap();yield return Frame("04f-tactical.png");CloseMap();
            }
            Pause(true);yield return new WaitForSecondsRealtime(.3f);
            long tick=runtime.Latest.Tick;yield return Frame("05-pause.png");
            OpenPauseSettings();yield return Frame("06-pause-settings.png");menuNavigation.Back();
            if(!paused||runtime.Latest.Tick!=tick)throw new InvalidOperationException("Settings resumed the match.");
            OpenLaboratory();yield return new WaitForSecondsRealtime(.2f);
            var page=childMenu;page.Q<TextField>("lab-name").value="Проверка Orbital";
            var numeric=page.Query<TextField>().ToList().First(t=>t.name!=null&&t.name.StartsWith("lab-field-"));((Action<int>)numeric.userData)(1);
            Activate(page,"lab-save");int before=runtime.Latest.ProfileRevision;
            Activate(page,"lab-apply");yield return Frame("07-laboratory-pending.png");
            if(runtime.Latest.ProfileRevision!=before||runtime.Latest.Tick!=tick)throw new InvalidOperationException("Apply ran while paused.");
            menuNavigation.Back();if(!paused)throw new InvalidOperationException("Lab Back resumed the match.");
            Pause(false);deadline=Time.realtimeSinceStartup+10;while(runtime.Latest.ProfileRevision==before&&Time.realtimeSinceStartup<deadline)yield return null;
            if(runtime.Latest.ProfileRevision==before||runtime.Latest.Failure!=null)throw new InvalidOperationException("Saved revision not applied: "+runtime.BalanceApplyStatus);
            yield return new WaitForSecondsRealtime(2);Pause(true);yield return Frame("08-applied-pause.png");
            int applied=runtime.Latest.ProfileRevision;
            Pause(false);int headquarters=runtime.Latest.Buildings.Single(b=>b.Owner==Spacewars.Simulation.PlayableOwner.Player&&b.Kind==Spacewars.Simulation.PlayableBuildingKind.Headquarters).Id;
            Submit(Spacewars.Simulation.PlayableCommandKind.SellBuilding,new[]{headquarters});
            deadline=Time.realtimeSinceStartup+15;while(runtime.Latest.Outcome==Spacewars.Simulation.PlayableMatchOutcome.Playing&&Time.realtimeSinceStartup<deadline)yield return null;
            if(runtime.Latest.Outcome==Spacewars.Simulation.PlayableMatchOutcome.Playing)throw new InvalidOperationException("Selling the last HQ did not end the match.");
            yield return Frame("09-result.png");long oldGeneration=generation;modalRestartButton.Focus();menuNavigation.Activate();
            deadline=Time.realtimeSinceStartup+15;while(generation==oldGeneration&&Time.realtimeSinceStartup<deadline)yield return null;
            if(generation==oldGeneration||runtime.Latest.Outcome!=Spacewars.Simulation.PlayableMatchOutcome.Playing)throw new InvalidOperationException("Result restart did not create a new match.");
            yield return Frame("10-restarted-match.png");Pause(true);ReturnToMainMenu();yield return new WaitForSecondsRealtime(1);yield return Frame("11-return-main.png");
            if(runtime!=null||nativeMainMenu==null||!inLobby)throw new InvalidOperationException("Return to main failed.");
            File.WriteAllText(Path.Combine(orbitalEvidence,"smoke.json"),"{\"result\":\"passed\",\"applied_revision\":"+applied+",\"physical_gamepad\":false,\"scope\":\"main/settings/lab/lobby/match/pause/settings/live saved revision/pause preservation/result/restart/return main\"}");
            Debug.Log("ORBITAL_PLAYER_SMOKE_PASS");Application.Quit();
        }
    }
}
#endif
