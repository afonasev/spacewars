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
        [UnityTest] public IEnumerator SystemFailureUsesPowerModuleInChoiceAndHud()
        {
            yield return StartOrdinary(1,configureRoster:d=>d.Participants[0].DeviceId=0);
            var seat=Seats[0];
            var root=Get<VisualElement>(seat,"root");
            var data=Get<PlayableProfile>(seat,"profile").CopyData();
            var model=new OrbitalEscalationState(compact:true);
            model.Damage(Enumerable.Range(0,2).Sum(level=>OrbitalAbilities.Threshold(level,data)),data);
            Assert.True(model.Choose(1));
            Assert.AreEqual(2,model.PendingLevel);
            var texture=Resources.Load<Texture2D>("OrbitalIcons/system-failure");
            Assert.NotNull(texture);

            void Seed()
            {
                var view=Get<PlayableSnapshot>(seat,"view");
                Put(seat,"view",view.WithOrbital(model.Snapshot(data)));
                Call(seat,"UpdateOrbitalHud");
            }
            void CheckArt(VisualElement container,string name)
            {
                Assert.NotNull(container);
                var artwork=container.Q(name);
                Assert.NotNull(artwork);
                Assert.AreSame(texture,artwork.style.backgroundImage.value.texture);
            }

            int id=OrbitalAbilities.SystemFailure;
            Seed();
            Assert.True((bool)Call(seat,"OpenOrbital",false));
            CheckArt(root.Q("orbital-choice-"+id),"orbital-choice-artwork");
            yield return Capture("orbital-level-two",Seed,1280,720,refreshWorld:false,captureChecks:()=>
            {
                var cards=Get<VisualElement>(seat,"orbitalCards");
                Assert.AreEqual(5,cards.childCount);
                Inside(root.worldBound,cards.worldBound,"five advanced choices");
                Assert.False(cards.worldBound.Overlaps(Get<VisualElement>(seat,"orbitalControls").worldBound),"footer clears choices");
                var details=root.Q("orbital-choice-details");
                Assert.AreEqual(1,root.Query<Label>("orbital-choice-description").ToList().Count);
                Assert.IsNotEmpty(details.Q("orbital-choice-stats").Children());
                Assert.False(details.worldBound.Overlaps(Get<VisualElement>(seat,"orbitalControls").worldBound));
            });
            Call(seat,"CloseOrbital");
            Assert.True(model.Choose(id));
            Seed();
            CheckArt(root.Q("orbital-slot-"+id),"ability-artwork");
            yield return Capture("system-failure-hud",Seed,1600,1000,refreshWorld:false,
                captureChecks:()=>Inside(root.worldBound,root.Q("orbital-slot-"+id).worldBound,"System Failure slot"));

            Assert.True(model.Use(id,data));
            Seed();
            Assert.True((bool)Call(seat,"OpenOrbital",true));
            void FocusSystemFailure()
            {
                Seed();
                Put(seat,"orbitalFocus",1);
                Call(seat,"RefreshOrbitalMenu");
            }
            yield return Capture("orbital-wheel-cooldown",FocusSystemFailure,1280,720,refreshWorld:false,captureChecks:()=>
            {
                var card=root.Q("orbital-ring").Q<RadialActionCard>();
                Inside(root.worldBound,card.worldBound,"orbital detail");
                StringAssert.Contains("глушение",card.Q<Label>("radial-detail").text);
                var labels=card.Query<Label>().ToList();
                Assert.True(labels.Any(label=>label.text=="Откат"));
                Assert.True(labels.Any(label=>label.text=="Радиус"));
                StringAssert.Contains("До готовности",card.Q<Label>("radial-note").text);
            });

            Runtime.RequestStop();
            yield return Wait(()=>Runtime.IsStopped,"system failure artwork stop");
        }
        [UnityTest] public IEnumerator FourPlayerAdvancedChoicesKeepArtworkAndShowOnlyFocusedDetails()
        {
            yield return StartOrdinary(4);
            var seat=Seats[0];
            var root=Get<VisualElement>(seat,"root");
            var data=Get<PlayableProfile>(seat,"profile").CopyData();
            var model=new OrbitalEscalationState(compact:true);
            model.Damage(Enumerable.Range(0,3).Sum(level=>OrbitalAbilities.Threshold(level,data)),data);
            Assert.True(model.Choose(1));

            void Seed()
            {
                var view=Get<PlayableSnapshot>(seat,"view");
                Put(seat,"view",view.WithOrbital(model.Snapshot(data)));
                Call(seat,"UpdateOrbitalHud");
            }

            foreach(var stage in new[]{2,3})
            {
                Assert.AreEqual(stage,model.PendingLevel);
                Seed();
                Assert.True((bool)Call(seat,"OpenOrbital",true));
                Put(seat,"orbitalFocus",1);
                Call(seat,"RefreshOrbitalMenu");
                void Check()
                {
                    var cards=root.Q("orbital-cards");
                    var details=root.Q("orbital-choice-details");
                    Assert.AreEqual(7-stage,cards.childCount);
                    Assert.AreEqual(1,root.Query<Label>("orbital-choice-description").ToList().Count);
                    StringAssert.Contains(stage==2?"три Исследователя":"Задержите рейд",details.Q<Label>("orbital-choice-description").text);
                    foreach(var card in cards.Children())
                        Assert.GreaterOrEqual(card.Q("orbital-choice-artwork").worldBound.height,70);
                    Inside(root.worldBound,details.worldBound,"focused orbital details");
                    Assert.False(details.worldBound.Overlaps(Get<VisualElement>(seat,"orbitalControls").worldBound));
                    var map=Get<VisualElement>(seat,"sharedMapFrame").worldBound;
                    Assert.False(details.worldBound.Overlaps(map));
                    foreach(var control in Get<VisualElement>(seat,"orbitalControls").Children())
                        Assert.False(control.worldBound.Overlaps(map));
                }
                yield return Capture("focused-orbital-four-level-"+stage,Seed,1280,720,refreshWorld:false,captureChecks:Check);
                Call(seat,"CloseOrbital");
                if(stage==2)Assert.True(model.Choose(OrbitalAbilities.SystemFailure));
            }
            Runtime.RequestStop();
            yield return Wait(()=>Runtime.IsStopped,"four-player orbital choices stop");
        }

        private IEnumerator OrbitalLayout(int count)
        {
            yield return StartOrdinary(count);
            foreach(var seat in Seats){var view=Get<PlayableSnapshot>(seat,"view");Selection(seat).Add(view.Entities.First(e=>e.Owner==view.Owner).Id);}Call(app,"UpdateFrame");yield return null;
            var data=Get<PlayableProfile>(app,"profile").CopyData();var model=new OrbitalEscalationState(compact:true);model.Damage(Enumerable.Range(0,3).Sum(level=>OrbitalAbilities.Threshold(level,data))+.5*OrbitalAbilities.Threshold(3,data),data);Assert.True(model.Choose(1));Assert.True(model.Choose(3));Assert.True(model.Choose(4));model.Use(3,data);model.Advance(30,data);
            var profile=Get<PlayableProfile>(app,"profile");
            var explorers=OrbitalReinforcement.Quote(profile,1,false,true);
            var tanks=OrbitalReinforcement.Quote(profile,1,true,true);
            var explorerTexture=Resources.Load<Texture2D>("OrbitalIcons/reinforcement-explorers");
            var tankTexture=Resources.Load<Texture2D>("OrbitalIcons/reinforcement-tanks");
            Assert.NotNull(explorerTexture);
            Assert.NotNull(tankTexture);
            Assert.AreNotSame(explorerTexture,tankTexture);
            bool nextTanks=false;

            void Seed()
            {
                for(int i=0;i<Seats.Length;i++)
                {
                    var seat=Seats[i];
                    var quote=(nextTanks^(i%2!=0))?tanks:explorers;
                    Put(seat,"view",Get<PlayableSnapshot>(seat,"view").WithOrbital(model.Snapshot(data,quote)));
                    Call(seat,"UpdateOrbitalHud");
                }
            }
            VisualElement Artwork(PlayableBootstrap seat) => Get<VisualElement>(seat,"root")
                .Q("orbital-slot-"+OrbitalAbilities.Reinforcement).Q("ability-artwork");

            Seed();yield return null;
            Seed();
            var artwork=Artwork(Seats[0]);
            Assert.AreSame(explorerTexture,artwork.style.backgroundImage.value.texture);
            if(count>1)Assert.AreSame(tankTexture,Artwork(Seats[1]).style.backgroundImage.value.texture);
            yield return Capture("orbital-"+count+"-screens",Seed,captureWidth:1600,captureHeight:1000,refreshWorld:false,captureChecks:()=>{
                foreach(var seat in Seats){var root=Get<VisualElement>(seat,"root");var panel=Get<VisualElement>(seat,"orbitalPanel");Inside(root.worldBound,panel.worldBound,"orbital panel");bool right=count>1&&Get<Camera>(seat,"cameraView").rect.center.x>.5f;if(right)Assert.Less(root.worldBound.xMax-panel.worldBound.xMax,10,"right sector support stays at the physical right edge");else Assert.Less(panel.worldBound.xMin-root.worldBound.xMin,10,"left sector support stays at the physical left edge");var hint=Get<Label>(seat,"orbitalLtHint");var badge=Get<Button>(seat,"orbitalLevelButton");if(hint.resolvedStyle.display!=DisplayStyle.None){if(right)Assert.LessOrEqual(hint.worldBound.xMax,badge.worldBound.xMin,"right LT faces inward");else Assert.GreaterOrEqual(hint.worldBound.xMin,badge.worldBound.xMax,"left LT faces inward");}var shared=Get<VisualElement>(Seats[0],"sharedMapFrame");if(shared!=null)Assert.False(panel.worldBound.Overlaps(shared.worldBound),"left support must clear the shared minimap");Assert.AreEqual("3",Get<Label>(seat,"orbitalLevel").text);Assert.AreEqual(.5f,Get<OrbitalProgressRing>(seat,"orbitalProgress").Fraction,.001f);CollectionAssert.AreEqual(new[]{"orbital-slot-4","orbital-slot-3","orbital-slot-1"},Get<VisualElement>(seat,"orbitalSlots").Children().Select(e=>e.name));Assert.AreEqual(3,Get<VisualElement>(seat,"orbitalSlots").childCount);
                    var anchor=count==1?Get<VisualElement>(seat,"root").Q("tactical-minimap-frame"):Get<VisualElement>(seat,"armyRegion");if(anchor.resolvedStyle.display!=DisplayStyle.None)Assert.LessOrEqual(panel.worldBound.yMax,anchor.worldBound.yMin+.5f);
                }
            });
            nextTanks=true;
            Seed();
            Assert.AreSame(artwork,Artwork(Seats[0]),"A new wave refreshes the existing ability slot");
            Assert.AreSame(tankTexture,artwork.style.backgroundImage.value.texture);
            if(count>1)Assert.AreSame(explorerTexture,Artwork(Seats[1]).style.backgroundImage.value.texture);
            if(count==1)yield return Capture("reinforcement-tanks-hud",Seed,1600,1000,refreshWorld:false);

            model.Damage(10000,data);Assert.True(model.Choose(6));
            yield return Capture("orbital-"+count+"-max",Seed,captureWidth:1600,captureHeight:1000,refreshWorld:false,captureChecks:()=>{
                foreach(var seat in Seats){Assert.AreEqual("MAX",Get<Label>(seat,"orbitalLevel").text);Assert.AreEqual(4,Get<VisualElement>(seat,"orbitalSlots").childCount);Assert.AreEqual(1,Get<OrbitalProgressRing>(seat,"orbitalProgress").Fraction);Inside(Get<VisualElement>(seat,"root").worldBound,Get<VisualElement>(seat,"orbitalPanel").worldBound,"MAX panel");var label=Get<Label>(seat,"orbitalLevel");Assert.LessOrEqual(label.MeasureTextSize(label.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x,Get<Button>(seat,"orbitalLevelButton").worldBound.width,"MAX fits medallion");}
            });
            var first=Seats[0];var pending=new OrbitalEscalationState(compact:true);pending.Damage(250,data);
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
