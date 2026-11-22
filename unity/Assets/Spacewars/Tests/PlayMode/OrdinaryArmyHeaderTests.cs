using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        IEnumerator ArmyHeaderLayout(int humans)
        {
            bool savedFps=NativeDevicePreferences.ShowFps;float savedScale=NativeDevicePreferences.UiScale;
            try
            {
                NativeDevicePreferences.UiScale=1;yield return StartOrdinary(humans);
                foreach(var seat in Seats){Selection(seat).Clear();Put(seat,"selectedSite",0);Put(seat,"selectedSlot",0);Call(seat,"ToggleMap");}
                void Context()
                {
                    OrbitalTheme.LayoutDeviceDiagnostics(Get<VisualElement>(app,"panelRoot"));
                    foreach(var seat in Seats)Call(seat,"UpdateHud");
                }
                void Check()
                {
                    var fps=Get<VisualElement>(app,"panelRoot").Q<Label>("device-fps");
                    foreach(var seat in Seats)
                    {
                        AssertTacticalScreen(seat);
                        var table=Get<VisualElement>(seat,"tacticalTeam");
                        var rows=table.Q<ScrollView>("tactical-team-rows").contentContainer.Children().ToList();
                        var view=Get<PlayableSnapshot>(seat,"view");
                        var own=rows.First();
                        Assert.AreEqual("tactical-player-"+view.OwnerId,own.name);
                        AssertTacticalUnitCounts(own,view);
                        if(NativeDevicePreferences.ShowFps)Assert.AreEqual(DisplayStyle.Flex,fps.resolvedStyle.display);
                    }
                }
                int phase=0;
                foreach(bool fps in new[]{false,true,false})
                {
                    NativeDevicePreferences.ShowFps=fps;
                    yield return Capture("army-header-"+humans+"-"+(phase++)+"-fps-"+fps,Context,humans==1?1280:1920,humans==1?800:1080,captureChecks:Check);
                }
                if(humans==2)
                {
                    NativeDevicePreferences.UiScale=1.25f;NativeDevicePreferences.ShowFps=true;
                    yield return Capture("army-header-2-1280-max-ui-scale",Context,1280,720,captureChecks:Check);
                }
            }
            finally{NativeDevicePreferences.ShowFps=savedFps;NativeDevicePreferences.UiScale=savedScale;}
        }
        [UnityTest] public IEnumerator ArmyHeaderSinglePlayer()=>ArmyHeaderLayout(1);
        [UnityTest] public IEnumerator ArmyHeaderTwoPlayers()=>ArmyHeaderLayout(2);
        [UnityTest] public IEnumerator ArmyHeaderFourPlayers()=>ArmyHeaderLayout(4);
    }
}
