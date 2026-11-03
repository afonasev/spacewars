using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        [UnityTest] public IEnumerator AbilityTargetingSingleViewport()=>AbilityTargetingLayout(1);
        [UnityTest] public IEnumerator AbilityTargetingFourViewports()=>AbilityTargetingLayout(4);
        private IEnumerator AbilityTargetingLayout(int count)
        {
            yield return StartOrdinary(count);
            var data=Get<PlayableProfile>(app,"profile").CopyData();
            foreach(int ability in new[]{1,2,OrbitalAbilities.ControlHack}){
                foreach(var seat in Seats){
                    var model=new OrbitalEscalationState();model.Damage(250,data);Assert.True(model.Choose(ability));
                    Put(seat,"view",Get<PlayableSnapshot>(seat,"view").WithOrbital(model.Snapshot(data)));
                    Call(seat,"BeginOrbitalTarget",true,ability);
                    var point=Get<PlayableSnapshot>(seat,"view").Entities.First(e=>e.Owner==Get<PlayableSnapshot>(seat,"view").Owner).Position;PadCursor(seat,point);
                }
                void Seed(){foreach(var seat in Seats){var model=new OrbitalEscalationState();model.Damage(250,data);model.Choose(ability);Put(seat,"view",Get<PlayableSnapshot>(seat,"view").WithOrbital(model.Snapshot(data)));Call(seat,"UpdateOrbitalHud");Call(seat,"UpdateScanPresentation");}}
                Seed();
                yield return Capture("targeting-"+count+"-ability-"+ability,Seed,captureWidth:1280,captureHeight:720,refreshWorld:false,captureChecks:()=>{
                    foreach(var seat in Seats){
                        var root=Get<VisualElement>(seat,"root");var header=root.Q("ability-target-header");var controls=root.Q("ability-target-controls");
                        Assert.AreEqual(DisplayStyle.Flex,header.style.display.value);Inside(root.worldBound,header.worldBound,"target header");Inside(root.worldBound,controls.worldBound,"apply/cancel");
                        Assert.False(header.worldBound.Overlaps(controls.worldBound));Assert.AreEqual(DisplayStyle.None,Get<Label>(seat,"noticeLabel").style.display.value,"targeting instructions have one owner");
                        StringAssert.Contains(OrbitalAbilities.Name(ability),root.Q<Label>("ability-target-title").text);
                        Assert.AreEqual("Отменить",root.Q<Label>("ability-target-cancel").text);
                        foreach(var glyph in controls.Query<NativeControllerGlyph>().ToList()){glyph.Refresh(pads[Array.IndexOf(Seats,seat)]);Assert.AreEqual(DisplayStyle.Flex,glyph.style.display.value,"apply AND cancel badges stay visible after device refresh");}
                        var shared=Get<VisualElement>(app,"sharedMapFrame");if(shared!=null&&shared.style.display.value!=DisplayStyle.None)Assert.False(header.worldBound.Overlaps(shared.worldBound),"target header clears common minimap");
                        Assert.True(root.Q("orbital-slot-"+ability).ClassListContains("ability-targeting-active"));
                        if(ability==OrbitalAbilities.ControlHack)Assert.False(Get<bool>(seat,"abilityTargetValid"),"own unit cannot be hacked");
                        else StringAssert.Contains(ability==1?"Радиус обзора":"Радиус взрыва",root.Q<Label>("ability-target-status").text);
                    }
                });
                foreach(var seat in Seats){Call(seat,"CancelScanTarget");Call(seat,"UpdateScanPresentation");Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(seat,"root").Q("ability-target-header").style.display.value);Assert.Zero(Get<PlayableSnapshot>(seat,"view").Orbital.Abilities[0].Remaining);}
            }
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"targeting layout stop");
        }
    }
}
