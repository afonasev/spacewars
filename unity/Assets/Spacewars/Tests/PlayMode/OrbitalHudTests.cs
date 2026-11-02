using System;
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
        [UnityTest] public IEnumerator OrbitalHudOneScreen()=>OrbitalLayout(1);
        [UnityTest] public IEnumerator OrbitalHudTwoScreens()=>OrbitalLayout(2);
        [UnityTest] public IEnumerator OrbitalHudThreeScreens()=>OrbitalLayout(3);
        [UnityTest] public IEnumerator OrbitalHudFourScreens()=>OrbitalLayout(4);
        private IEnumerator OrbitalLayout(int count)
        {
            yield return StartOrdinary(count);
            foreach(var seat in Seats){var view=Get<PlayableSnapshot>(seat,"view");Selection(seat).Add(view.Entities.First(e=>e.Owner==view.Owner).Id);}Call(app,"UpdateFrame");yield return null;
            var data=Get<PlayableProfile>(app,"profile").CopyData();var model=new OrbitalEscalationState();model.Damage(5750,data);model.Choose(1);model.Choose(2);model.Choose(3);model.Choose(4);model.Use(3,data);model.Advance(30,data);
            void Seed(){foreach(var seat in Seats){Put(seat,"view",Get<PlayableSnapshot>(seat,"view").WithOrbital(model.Snapshot(data)));Call(seat,"UpdateOrbitalHud");}}
            Seed();yield return null;
            yield return Capture("orbital-"+count+"-screens",Seed,captureWidth:1600,captureHeight:1000,refreshWorld:false,captureChecks:()=>{
                foreach(var seat in Seats){var root=Get<VisualElement>(seat,"root");var panel=Get<VisualElement>(seat,"orbitalPanel");Inside(root.worldBound,panel.worldBound,"orbital panel");bool right=count>1&&Get<Camera>(seat,"cameraView").rect.center.x>.5f;if(right)Assert.Less(root.worldBound.xMax-panel.worldBound.xMax,10,"right sector support stays at the physical right edge");else Assert.Less(panel.worldBound.xMin-root.worldBound.xMin,10,"left sector support stays at the physical left edge");var hint=Get<Label>(seat,"orbitalLtHint");var badge=Get<Button>(seat,"orbitalLevelButton");if(hint.resolvedStyle.display!=DisplayStyle.None){if(right)Assert.LessOrEqual(hint.worldBound.xMax,badge.worldBound.xMin,"right LT faces inward");else Assert.GreaterOrEqual(hint.worldBound.xMin,badge.worldBound.xMax,"left LT faces inward");}var shared=Get<VisualElement>(Seats[0],"sharedMapFrame");if(shared!=null)Assert.False(panel.worldBound.Overlaps(shared.worldBound),"left support must clear the shared minimap");Assert.AreEqual("4",Get<Label>(seat,"orbitalLevel").text);Assert.AreEqual(.5f,Get<OrbitalProgressRing>(seat,"orbitalProgress").Fraction,.001f);CollectionAssert.AreEqual(new[]{"orbital-slot-4","orbital-slot-3","orbital-slot-2","orbital-slot-1"},Get<VisualElement>(seat,"orbitalSlots").Children().Select(e=>e.name));Assert.AreEqual(4,Get<VisualElement>(seat,"orbitalSlots").childCount);
                    var anchor=count==1?Get<VisualElement>(seat,"root").Q("tactical-minimap-frame"):Get<VisualElement>(seat,"armyRegion");if(anchor.resolvedStyle.display!=DisplayStyle.None)Assert.LessOrEqual(panel.worldBound.yMax,anchor.worldBound.yMin+.5f);
                }
            });
            model.Damage(10000,data);Assert.True(model.Choose(6));
            yield return Capture("orbital-"+count+"-max",Seed,captureWidth:1600,captureHeight:1000,refreshWorld:false,captureChecks:()=>{
                foreach(var seat in Seats){Assert.AreEqual("MAX",Get<Label>(seat,"orbitalLevel").text);Assert.AreEqual(5,Get<VisualElement>(seat,"orbitalSlots").childCount);Assert.AreEqual(1,Get<OrbitalProgressRing>(seat,"orbitalProgress").Fraction);Inside(Get<VisualElement>(seat,"root").worldBound,Get<VisualElement>(seat,"orbitalPanel").worldBound,"MAX panel");var label=Get<Label>(seat,"orbitalLevel");Assert.LessOrEqual(label.MeasureTextSize(label.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x,Get<Button>(seat,"orbitalLevelButton").worldBound.width,"MAX fits medallion");}
            });
            var first=Seats[0];var pending=new OrbitalEscalationState();pending.Damage(250,data);
            Put(first,"view",Get<PlayableSnapshot>(first,"view").WithOrbital(pending.Snapshot(data)));Call(first,"UpdateOrbitalHud");
            yield return Capture("orbital-"+count+"-pending",()=>{Put(first,"view",Get<PlayableSnapshot>(first,"view").WithOrbital(pending.Snapshot(data)));Call(first,"UpdateOrbitalHud");},1600,1000,refreshWorld:false,captureChecks:()=>Assert.AreEqual("1",Get<Label>(first,"orbitalLevel").text));
            Set(pads[0].leftTrigger,1f);UnityEngine.InputSystem.InputSystem.Update();Call(first,"PollOrbitalPad",pads[0]);Assert.True(Get<bool>(first,"orbitalOpen"));Assert.False(Get<bool>(first,"selectionRosterExpanded"),"LT opens support without expanding unit details");Assert.AreEqual("LT",Get<Label>(first,"orbitalLtHint").text);Assert.AreEqual(3,Get<VisualElement>(first,"orbitalCards").childCount);
            foreach(var other in Seats.Skip(1))Assert.False(Get<bool>(other,"orbitalOpen"));Set(pads[0].leftTrigger,0f);UnityEngine.InputSystem.InputSystem.Update();Call(first,"PollOrbitalPad",pads[0]);
            void Pending(){Put(first,"view",Get<PlayableSnapshot>(first,"view").WithOrbital(pending.Snapshot(data)));Call(first,"UpdateOrbitalHud");}
            // Input helper refreshes the live snapshot; render the declared synthetic card state explicitly.
            Call(first,"CloseOrbital");Pending();Call(first,"OpenOrbital",true);
            yield return Capture("orbital-"+count+"-cards",Pending,captureWidth:1600,captureHeight:1000,refreshWorld:false,captureChecks:()=>{Inside(Get<VisualElement>(first,"root").worldBound,Get<VisualElement>(first,"orbitalCards").worldBound,"cards");var controls=Get<VisualElement>(first,"orbitalControls");Inside(Get<VisualElement>(first,"root").worldBound,controls.worldBound,"control hints");Assert.AreEqual(3,controls.Query<RadialControlHint>().ToList().Count);Assert.NotNull(controls.Q("radial-stick-glyph"));Assert.False(controls.worldBound.Overlaps(Get<VisualElement>(first,"orbitalCards").worldBound),"footer clears cards");});
            Call(first,"CloseOrbital");Seed();Call(first,"OpenOrbital",true);
            yield return Capture("orbital-"+count+"-petals",Seed,captureWidth:1600,captureHeight:1000,refreshWorld:false,captureChecks:()=>{Assert.AreEqual(DisplayStyle.Flex,Get<OrbitalBattleRing>(first,"orbitalRing").style.display.value);});
            ButtonFrame(pads[0],pads[0].buttonEast,true);Assert.False(Get<bool>(first,"orbitalOpen"));ButtonFrame(pads[0],pads[0].buttonEast,false);
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
    }
}
