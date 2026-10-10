#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private IEnumerator CaptureBattleHudEvidence()
        {
            AudioListener.volume=0;
            InvalidOperationException Fail(string message){Debug.LogError("BATTLE_HUD_PLAYER_SMOKE_FAIL "+message);Quit();return new InvalidOperationException(message);}
            void Choose(string id){var button=battleRing.Q<Button>("radial-"+id);if(button==null)throw Fail("Missing radial button: "+id);button.Focus();using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}}
            IEnumerator Frame(string name){yield return new WaitForSecondsRealtime(.35f);ScreenCapture.CaptureScreenshot(Path.Combine(evidence,name+".png"));Record("battle-hud frame="+name);yield return new WaitForSecondsRealtime(.25f);}
            IEnumerator Ready(PlayableBuildingKind kind){float until=Time.realtimeSinceStartup+25;while(!view.Buildings.Any(b=>b.Owner==view.Owner&&b.Kind==kind&&b.Phase==ConstructionPhase.Ready)&&Time.realtimeSinceStartup<until)yield return null;if(!view.Buildings.Any(b=>b.Owner==view.Owner&&b.Kind==kind&&b.Phase==ConstructionPhase.Ready))throw Fail("Missing ready "+kind);}
            float entryDeadline=Time.realtimeSinceStartup+45;
            while((view==null||inLobby||preparing)&&Time.realtimeSinceStartup<entryDeadline)yield return null;
            if(view==null||inLobby||preparing){Debug.LogError("BATTLE_HUD_PLAYER_SMOKE_FAIL entry timeout");Quit();yield break;}
            yield return new WaitForSecondsRealtime(2);
            yield return Frame("01-empty");
            selection.Clear();foreach(var e in view.Entities.Where(e=>e.Owner==view.Owner))selection.Add(e.Id);
            yield return Frame("02-selection");hudFocusButton.Focus();yield return Frame("03-army-composition");root.Focus();
            var home=view.Sites.First(s=>s.Owner==view.Owner&&s.Ready&&s.Site.Slots.Count>2);
            selectedSite=home.Site.Id;selectedSlot=home.Site.Slots[0].Id;selection.Clear();
            yield return Frame("04-build-ring");Choose("build:"+selectedSite+":"+selectedSlot+":"+PlayableBuildingKind.Factory);
            if(selectedSite!=0)throw Fail("Build ring did not close.");
            yield return Ready(PlayableBuildingKind.Factory);
            var factory=view.Buildings.First(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Factory);selection.Add(factory.Id);battleDismissed=false;
            yield return Frame("05-factory-ring");
            foreach(var unit in new[]{"tank","explorer","shkval"})Choose(factory.Id+":"+unit);
            yield return new WaitForSecondsRealtime(.7f);
            if(view.Buildings.First(b=>b.Id==factory.Id).PrivateState.Orders.Count<3)throw Fail("Production ring did not enqueue three orders.");
            yield return Frame("06-production-queue");
            selection.Clear();selectedSite=home.Site.Id;selectedSlot=home.Site.Slots[1].Id;yield return new WaitForSecondsRealtime(.1f);
            Choose("build:"+selectedSite+":"+selectedSlot+":"+PlayableBuildingKind.ScientificCenter);yield return Ready(PlayableBuildingKind.ScientificCenter);
            var science=view.Buildings.First(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.ScientificCenter);selection.Clear();selection.Add(science.Id);battleDismissed=false;
            yield return Frame("07-research-ring");
            foreach(var key in new[]{"tank-chassis","explorer-assault-guns","shkval-guidance"})Choose(science.Id+":"+key);
            yield return new WaitForSecondsRealtime(.7f);
            if(view.OwnerResearch.Count<3)throw Fail("Research ring did not enqueue three orders.");
            yield return Frame("08-research-queue");
            selection.Clear();selectedSite=home.Site.Id;selectedSlot=home.Site.Slots[2].Id;yield return new WaitForSecondsRealtime(.1f);
            Choose("build:"+selectedSite+":"+selectedSlot+":"+PlayableBuildingKind.Refinery);yield return Ready(PlayableBuildingKind.Refinery);
            var refinery=view.Buildings.First(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Refinery);selection.Clear();selection.Add(refinery.Id);battleDismissed=false;
            yield return Frame("09-upgrade-ring");Choose(refinery.Id+":upgrade");yield return new WaitForSecondsRealtime(.6f);
            if(view.Buildings.First(b=>b.Id==refinery.Id).PrivateState.Upgrade?.Active!=true)throw Fail("Upgrade action failed.");
            yield return Frame("10-upgrade-progress");
            CloseBattleContext();yield return null;if(paused)throw Fail("Closing context paused battle.");
            Pause(true);yield return Frame("11-pause");Pause(false);yield return new WaitForSecondsRealtime(.2f);
            selection.Clear();var headquarters=view.Buildings.First(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Headquarters);selection.Add(headquarters.Id);
            yield return Frame("11b-last-centre-ring");Choose(headquarters.Id+":sale");Choose(headquarters.Id+":sale");
            float endDeadline=Time.realtimeSinceStartup+20;while(view.Outcome==PlayableMatchOutcome.Playing&&Time.realtimeSinceStartup<endDeadline)yield return null;
            if(view.Outcome==PlayableMatchOutcome.Playing)throw Fail("Confirmed sale did not end match.");
            yield return Frame("11c-result");
            long old=generation;modalRestartButton.Focus();menuNavigation.Activate();float deadline=Time.realtimeSinceStartup+15;while(generation==old&&Time.realtimeSinceStartup<deadline)yield return null;if(generation==old)throw Fail("Restart failed.");
            yield return Frame("12-restarted");
            File.WriteAllText(Path.Combine(evidence,"battle-hud-smoke.json"),"{\"result\":\"passed\",\"scope\":\"UI/queues/radial commands/pause/result/restart\",\"fixture\":\"UI review economy\",\"physical_gamepad\":false}");
            Debug.Log("BATTLE_HUD_PLAYER_SMOKE_PASS");Quit();
        }
    }
}
#endif
