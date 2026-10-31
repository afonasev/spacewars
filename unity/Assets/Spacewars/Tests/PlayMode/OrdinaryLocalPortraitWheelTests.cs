using System;
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
        IEnumerator PortraitWheel(int humans)
        {
            yield return StartOrdinary(humans);
            Set(Mouse.current.position,Get<Camera>(app,"cameraView").pixelRect.center);InputSystem.Update();
            foreach(var seat in Seats)Call(seat,"FocusPadPoint",Get<PlayableSnapshot>(seat,"view").Entities.First(e=>e.Owner==Get<PlayableSnapshot>(seat,"view").Owner).Position);
            void Context(string suffix,bool centered=false)
            {
                bool science=suffix=="tank-chassis";QueueContext(science);
                foreach(var seat in Seats)
                {
                    var current=Get<PlayableSnapshot>(seat,"view");var original=current.Buildings.Single();var p=Get<PlayableProfile>(seat,"profile");
                    var research=science?new[]{PlayableResearchKind.ExplorerAssaultGuns,PlayableResearchKind.ShkvalGuidance,PlayableResearchKind.HawkSalvoSystems}.Select((kind,i)=>new PlayableResearchOrderSnapshot(i+1,kind,original.Id,300,i==0?15:0,30,i==0,false)).ToArray():Array.Empty<PlayableResearchOrderSnapshot>();
                    var orders=original.PrivateState.Orders.Take(4).ToArray();
                    var position=centered?(NavPoint)Call(seat,"Ground",Get<Camera>(seat,"cameraView").pixelRect.center):original.Position;
                    var building=new PlayableBuildingSnapshot(original.Id,current.Owner,original.Kind,position,100,1,science?3:4,.5,original.Position,orders:orders,research:research,lifecycle:original.PrivateState.Lifecycle);
                    var availability=Enum.GetValues(typeof(PlayableResearchKind)).Cast<PlayableResearchKind>().Select(kind=>new PlayableResearchAvailabilitySnapshot(kind,PlayableResearchRules.Cost(p,kind),true)).ToArray();
                    Put(seat,"view",new PlayableSnapshot(current.ProfileId,current.ProfileRevision,current.Generation,current.Seed,current.Sequence,current.Tick,current.Status,false,current.Outcome,1000,current.Geometry,current.Entities.ToArray(),new[]{building},current.Projectiles.ToArray(),current.Metrics,null,incomePerSecond:current.IncomePerSecond,vision:current.Vision,population:current.Population,owner:current.Owner,ownerResearch:research,researchAvailability:availability,ownerId:current.OwnerId,activeProfile:p));
                }
                foreach(var seat in Seats)
                {
                    Put(seat,"battleController",true);var building=Get<PlayableSnapshot>(seat,"view").Buildings.Single();
                    Put(seat,"battleSector",suffix==null?null:suffix=="rally"?suffix:building.Id+":"+suffix);Call(seat,"UpdateHud");
                }
            }
            void Bounds()
            {
                if(humans>1)AssertSplitDividers();
                foreach(var seat in Seats)
                {
                    var root=Get<VisualElement>(seat,"root");var ring=Get<OrbitalBattleRing>(seat,"battleRing");var card=ring.Q<RadialActionCard>();
                    Inside(root.worldBound,ring.worldBound,"wheel viewport");
                    if(card.HasAction){var title=card.Q<Label>("radial-title");var price=card.Q<Label>("radial-price");Inside(card.worldBound,title.worldBound,"title in card");Inside(card.worldBound,price.worldBound,"price in card");Assert.False(title.worldBound.Overlaps(price.worldBound),"title and price must not overlap");}Inside(root.worldBound,card.worldBound,"card viewport");Assert.False(ring.worldBound.Overlaps(card.worldBound),"card must not cover wheel");
                    Assert.False(card.worldBound.Overlaps(Get<VisualElement>(seat,"armyRegion").worldBound),"card must not cover dock");
                    Inside(card.worldBound,card.Q("radial-footer").worldBound,"footer in card");
                    if(card.HasAction)
                    {
                        foreach(var cell in card.Query<VisualElement>("radial-stat").ToList())
                        {
                            var label=cell.Q<Label>("radial-stat-label");var value=cell.Q<Label>("radial-stat-value");
                            Inside(card.worldBound,label.worldBound,"stat label in card");Inside(card.worldBound,value.worldBound,"stat value in card");
                            Assert.False(label.worldBound.Overlaps(value.worldBound),"stat label and value must not overlap");
                        }
                        foreach(var hint in card.Query<Label>("radial-hint").ToList())Inside(card.worldBound,hint.worldBound,"hint in card");
                    }
                }
            }
            foreach(string sector in new[]{"tank",null,"repair","sale","rally","tank-chassis"})
                yield return Capture("portrait-wheel-"+humans+"-"+(sector??"neutral"),()=>Context(sector),humans==1?1280:1920,humans==1?800:1080,captureChecks:Bounds);
            yield return Capture("portrait-wheel-"+humans+"-center",()=>Context("tank",true),humans==1?1280:1920,humans==1?800:1080,captureChecks:()=>
            {
                Bounds();foreach(var seat in Seats)Assert.GreaterOrEqual(Get<OrbitalBattleRing>(seat,"battleRing").Q<RadialActionCard>().resolvedStyle.width,RadialCardProfile.Load().width-1,"centered selection reserves the preferred card width");
            });
        }
        [UnityTest] public IEnumerator PortraitRallyHintsFollowPlacementAndCancellation()
        {
            yield return StartOrdinary(1);Set(Mouse.current.position,Get<Camera>(app,"cameraView").pixelRect.center);InputSystem.Update();
            QueueContext(false);Put(app,"battleController",true);Call(app,"BeginRallyPlacement",true);Call(app,"UpdateRallyPreview");Call(app,"UpdateHud");
            var root=Get<VisualElement>(app,"root");var hints=root.Q("rally-placement-hints");
            Assert.AreEqual(DisplayStyle.Flex,hints.style.display.value);Assert.AreEqual(2,hints.Query<NativeControllerGlyph>().ToList().Count);
            Assert.AreEqual(DisplayStyle.None,Get<OrbitalBattleRing>(app,"battleRing").style.display.value);
            Call(app,"CancelRallyPlacement",true);Call(app,"UpdateRallyPreview");Assert.AreEqual(DisplayStyle.None,hints.style.display.value);Assert.False(Get<bool>(app,"battleRally"));
        }
        [UnityTest] public IEnumerator PortraitWheelSingleMatch()=>PortraitWheel(1);
        [UnityTest] public IEnumerator PortraitWheelSplitMatch()=>PortraitWheel(2);
    }
}
