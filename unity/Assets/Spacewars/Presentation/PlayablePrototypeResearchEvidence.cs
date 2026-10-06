using System;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        // Explicit Development Player smoke: ordinary commands and observed state.
        // It does not change credits, elapsed time, outcome or physical device state.
        private int prototypeResearchStage,prototypeResearchCaptureFrame;
        private float prototypeResearchStarted;
        private bool prototypeResearchEnabled;
        private bool prototypeResearchChecked;
        private void DrivePrototypeResearchEvidence()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(!prototypeResearchChecked){prototypeResearchEnabled=Array.IndexOf(Environment.GetCommandLineArgs(),"-prototypeResearchEvidence")>=0;prototypeResearchChecked=true;}
            if(!prototypeResearchEnabled||evidence==null||view==null)return;
            if(prototypeResearchStarted==0)prototypeResearchStarted=Time.realtimeSinceStartup;
            if(Time.realtimeSinceStartup-prototypeResearchStarted>240){Record("prototype research FAIL timeout");Quit();return;}
            if(paused){Pause(false);return;}
            if(view.Outcome!=PlayableMatchOutcome.Playing){Record("prototype research FAIL premature result");Quit();return;}
            var center=view.Buildings.FirstOrDefault(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.ScientificCenter);
            if(prototypeResearchStage==0)
            {
                var home=view.Sites.FirstOrDefault(s=>s.Owner==PlayableOwner.Player&&s.Ready&&s.Site.Kind==PlayableBuildingKind.Headquarters);
                if(home==null)return;selectedSite=home.Site.Id;selectedSlot=home.Site.Slots[0].Id;
                BuildSelected(PlayableBuildingKind.ScientificCenter);Record("prototype research: build science through ordinary command");prototypeResearchStage=1;
            }
            else if(prototypeResearchStage==1)
            {
                if(center==null||center.Phase!=ConstructionPhase.Ready||view.Credits<profile.TankChassisCost+Math.Max(profile.ExplorerAssaultCost,profile.ShkvalGuidanceCost))return;
                selection.Clear();selection.Add(center.Id);
                foreach(PlayableResearchKind kind in Enum.GetValues(typeof(PlayableResearchKind)))Submit(PlayableCommandKind.QueueResearch,new[]{center.Id},researchKind:kind);
                prototypeResearchStage=2;
            }
            else if(prototypeResearchStage==2)
            {
                var orders=center?.PrivateState?.Research;
                if(orders==null||orders.Count<3||!orders[0].Active||orders[0].Progress<.1)return;
                if(orders.Skip(1).Any(o=>o.Active||o.Complete)){Record("prototype research FAIL queue ordering");Quit();return;}
                Record("prototype research PASS ready science, three queued orders, active paid progress and two waiting");
                ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"research-three-orders.png"));CaptureProductionSnapshot(1);
                prototypeResearchCaptureFrame=Time.frameCount;prototypeResearchStage=3;
            }
            else if(prototypeResearchStage==3&&Time.frameCount>prototypeResearchCaptureFrame+3)Quit();
#endif
        }
    }
}
