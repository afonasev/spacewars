using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        void AssertSplitDividers()
        {
            var divider=Get<SplitScreenDividers>(app,"splitDividers");var map=Get<VisualElement>(app,"sharedMapFrame");
            Assert.NotNull(divider);Assert.AreEqual(DisplayStyle.Flex,divider.style.display.value);Assert.AreEqual(PickingMode.Ignore,divider.pickingMode);
            var profile=SplitScreenHudProfile.Load();var bounds=Get<VisualElement>(app,"panelRoot").worldBound;
            var excluded=map.worldBound;excluded.xMin-=profile.minimapGap;excluded.xMax+=profile.minimapGap;excluded.yMin-=profile.minimapGap;excluded.yMax+=profile.minimapGap;
            Assert.AreEqual(Seats.Length==2?1:Seats.Length==3?2:4,divider.PaintedSegments.Count,"Visible seam pieces around the shared minimap");
            foreach(var segment in divider.PaintedSegments)
            {
                var world=segment;world.position=divider.LocalToWorld(segment.position);Inside(bounds,world,"divider in panel");
                Assert.False(world.Overlaps(excluded),"divider must leave a gap outside the minimap frame");
                bool vertical=world.width<world.height;
                Assert.That(vertical?world.center.x:world.center.y,Is.EqualTo(vertical?bounds.center.x:bounds.center.y).Within(.5),"divider follows viewport boundary");
                Assert.That(vertical?world.width:world.height,Is.EqualTo(profile.thickness).Within(.1));
            }
        }
        IEnumerator SplitDividerLayout(int humans)
        {
            yield return StartOrdinary(humans);Set(Mouse.current.position,Get<Camera>(app,"cameraView").pixelRect.center);InputSystem.Update();
            foreach(var seat in Seats)Call(seat,"FocusPadPoint",Get<PlayableSnapshot>(seat,"view").Entities.First(e=>e.Owner==Get<PlayableSnapshot>(seat,"view").Owner).Position);
            void Context(){foreach(var seat in Seats){Put(seat,"battleDismissed",true);Call(seat,"UpdateHud");}}
            yield return Capture("viewport-seams-"+humans,Context,1920,1080,captureChecks:AssertSplitDividers);
            Call(app,"Pause",true);Assert.AreEqual(DisplayStyle.None,Get<SplitScreenDividers>(app,"splitDividers").style.display.value,"global pause hides seams");
            Call(app,"Pause",false);
            yield return Capture("viewport-seams-"+humans+"-resized",Context,1280,720,captureChecks:AssertSplitDividers);
            Call(app,"DisposeLocalChildren");Call(app,"ResetLocalViewport");
            Assert.IsNull(Get<VisualElement>(app,"panelRoot").Q<SplitScreenDividers>(),"returning to a single viewport removes the overlay");
        }
        IEnumerator CenteredFps(int humans)
        {
            bool previous=NativeDevicePreferences.ShowFps;NativeDevicePreferences.ShowFps=true;
            try
            {
                yield return StartOrdinary(humans);
                foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)})
                {
                    void Check()
                    {
                        AssertSplitDividers();
                        var panel=Get<VisualElement>(app,"panelRoot");var fps=panel.Q<Label>("device-fps");
                        Assert.AreEqual(DisplayStyle.Flex,fps.resolvedStyle.display);
                        Assert.AreEqual(panel.worldBound.center.x,fps.worldBound.center.x,.5f);
                        Assert.AreEqual(panel.worldBound.yMin+8,fps.worldBound.yMin,.5f);
                        Assert.AreEqual(1,fps.resolvedStyle.backgroundColor.a,.001f,"Opaque plate conceals the seam behind the reading");
                        Assert.Greater(panel.IndexOf(fps),panel.IndexOf(Get<SplitScreenDividers>(app,"splitDividers")),"FPS above seam every frame");
                        foreach(var seat in Seats)Assert.False(fps.worldBound.Overlaps(Get<VisualElement>(seat,"top").worldBound),"FPS clear of resources");
                    }
                    yield return Capture("fps-"+humans+"-"+size.x+"x"+size.y,captureWidth:size.x,captureHeight:size.y,captureChecks:Check);
                }
                Call(app,"DisposeLocalChildren");Call(app,"ResetLocalViewport");
                OrbitalTheme.LayoutDeviceDiagnostics(Get<VisualElement>(app,"panelRoot"));yield return null;yield return null;
                var root=Get<VisualElement>(app,"panelRoot");Assert.AreEqual(root.worldBound.xMin+8,root.Q("device-fps").worldBound.xMin,.5f);
            }
            finally{NativeDevicePreferences.ShowFps=previous;}
        }
        [UnityTest] public IEnumerator TwoPlayerFpsStaysAboveSeam()=>CenteredFps(2);
        [UnityTest] public IEnumerator ThreePlayerFpsStaysAboveSeam()=>CenteredFps(3);
        [UnityTest] public IEnumerator FourPlayerFpsStaysAboveSeam()=>CenteredFps(4);
        [UnityTest] public IEnumerator ThreePlayerSeamsExcludeMinimapAfterResize()=>SplitDividerLayout(3);
        [UnityTest] public IEnumerator FourPlayerSeamsExcludeMinimapAfterResize()=>SplitDividerLayout(4);
    }
}
