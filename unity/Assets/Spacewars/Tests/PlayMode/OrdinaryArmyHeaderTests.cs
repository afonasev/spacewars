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
                        var root=Get<VisualElement>(seat,"root");var row=Get<VisualElement>(seat,"armyComposition");var resources=Get<VisualElement>(seat,"top");
                        Assert.AreEqual(DisplayStyle.Flex,row.resolvedStyle.display);Assert.AreEqual(FlexDirection.Row,row.resolvedStyle.flexDirection);
                        Inside(root.worldBound,row.worldBound,"army header in player viewport");Assert.False(row.worldBound.Overlaps(resources.worldBound),"army row clear of original resource indicators");
                        Assert.That(row.worldBound.center.y,Is.EqualTo(resources.worldBound.center.y).Within(.5),"same header line");
                        bool resourcesLeft=humans>1&&Get<int>(seat,"localViewportIndex")%2==0;
                        if(resourcesLeft){Assert.Less(resources.worldBound.center.x,root.worldBound.center.x);Assert.Greater(row.worldBound.center.x,root.worldBound.center.x);}
                        else{Assert.Greater(resources.worldBound.center.x,root.worldBound.center.x);Assert.Less(row.worldBound.center.x,root.worldBound.center.x);}
                        if(NativeDevicePreferences.ShowFps){Assert.AreEqual(DisplayStyle.Flex,fps.resolvedStyle.display);Assert.False(row.worldBound.Overlaps(fps.worldBound),"army row clear of optional FPS");}
                        var map=Get<PlayableMapSurface>(seat,"tacticalMap");Assert.False(row.worldBound.Overlaps(map.worldBound),"army row clear of centered map");
                        var frame=Get<VisualElement>(seat,"tacticalOverlay").Q("tactical-map-background");
                        Assert.That(frame.worldBound.center.x,Is.EqualTo(root.worldBound.center.x).Within(.5));Assert.That(frame.worldBound.center.y,Is.EqualTo(root.worldBound.center.y).Within(.5));
                        Assert.AreEqual(0,row.resolvedStyle.backgroundColor.a);Assert.AreEqual(0,row.resolvedStyle.borderTopWidth);
                        var counts=Get<Label[]>(seat,"armyCounts");var view=Get<PlayableSnapshot>(seat,"view");
                        var kinds=new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk};
                        Assert.AreEqual(4,counts.Length);Label previous=null;float occupiedWidth=0;
                        for(int i=0;i<counts.Length;i++)
                        {
                            var count=counts[i];int amount=view.Entities.Count(e=>e.Owner==view.Owner&&e.Kind==kinds[i]);Assert.AreEqual(amount.ToString(),count.text);
                            Assert.AreEqual(amount>0?DisplayStyle.Flex:DisplayStyle.None,count.resolvedStyle.display);
                            if(amount==0)continue;
                            Inside(row.worldBound,count.worldBound,"army count in row");if(previous!=null)Assert.That(count.worldBound.xMin,Is.EqualTo(previous.worldBound.xMax).Within(.5f),"hidden types leave no gaps");previous=count;occupiedWidth+=count.worldBound.width;
                            var icon=count.Q<OrbitalGlyph>();Inside(count.worldBound,icon.worldBound,"portrait in count cell");Assert.AreSame(DockArtwork.TextureFor(icon.Kind),icon.Q<Image>("glyph-portrait").image);
                            Assert.AreEqual(OrbitalTheme.Ink,icon.Q<Image>("portrait-outline").tintColor,"light contour");Assert.AreEqual(Color.white,icon.Q<Image>("glyph-portrait").tintColor,"model colors retained");
                        }
                        Assert.That(row.worldBound.width,Is.EqualTo(occupiedWidth).Within(.5f),"row width excludes absent types");
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
