using System;
using System.Collections;
using System.Collections.Generic;
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
        private readonly Dictionary<PlayableBootstrap,PlayableSnapshot> compactBases=new();
        private void CompactContext(int count,PlayableEntityKind singleKind=PlayableEntityKind.Tank,bool reset=false,bool variants=false)
        {
            foreach(var seat in Seats)
            {
                if(!compactBases.TryGetValue(seat,out var c))compactBases[seat]=c=Get<PlayableSnapshot>(seat,"view");
                var p=Get<PlayableProfile>(seat,"profile");var point=c.Entities.First(e=>e.Owner==c.Owner).Position;
                var units=Enumerable.Range(0,count).Select(i=>{
                    var kind=count==1?singleKind:i<20?PlayableEntityKind.Tank:i<40?PlayableEntityKind.Explorer:PlayableEntityKind.Shkval;
                    int hp=(int)PlayableUnitRules.Health(p,kind);
                    return new PlayableEntitySnapshot(20000+i,c.Owner,kind,point,Math.Max(1,hp-i%5*5),false,0,0,0,upgraded:variants&&i==19,maxHealth:hp);
                }).ToArray();
                Put(seat,"view",new PlayableSnapshot(c.ProfileId,c.ProfileRevision,c.Generation,c.Seed,c.Sequence,c.Tick,c.Status,false,c.Outcome,1000,c.Geometry,units,Array.Empty<PlayableBuildingSnapshot>(),c.Projectiles.ToArray(),c.Metrics,null,incomePerSecond:c.IncomePerSecond,vision:c.Vision,population:c.Population,owner:c.Owner,ownerId:c.OwnerId,activeProfile:p));
                if(reset){Selection(seat).Clear();foreach(var u in units)Selection(seat).Add(u.Id);Call(seat,"SelectionMarkerEvent");}
                Put(seat,"selectedSite",0);Put(seat,"selectedSlot",0);Put(seat,"notice","");Call(seat,"UpdateHud");
            }
        }
        private void CompactCaptureContext(int count,PlayableEntityKind kind=PlayableEntityKind.Tank,bool expanded=false)
        {
            // Keep the explicit HUD projection stable while UI Toolkit resolves and renders its geometry.
            CompactContext(count,kind,true);
            foreach(var seat in Seats){Put(seat,"selectionRosterExpanded",expanded);Call(seat,"UpdateHud");}
        }
        private void AssertCompactSelection(bool expanded,int count)
        {
            AssertCompactBounds();
            foreach(var seat in Seats)
            {
                var panel=Get<VisualElement>(seat,"armyRegion");var root=Get<VisualElement>(seat,"root");
                Assert.Less(panel.worldBound.height,root.worldBound.height*(expanded?.55f:.30f),"selection stays compact");
                Assert.AreEqual(count,Selection(seat).Count);
                if(count>1)
                {
                    Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(seat,"dockIdentity").style.display.value,"no primary unit card for group");
                    Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(seat,"selectionStats").style.display.value);
                    Assert.AreEqual("ВЫБРАНО "+count,Get<Label>(seat,"selectionGroupCount").text);
                    Assert.AreEqual(expanded?DisplayStyle.Flex:DisplayStyle.None,Get<VisualElement>(seat,"selectionRoster").style.display.value);
                    var tiles=Get<VisualElement>(seat,"selectionRoster").Query<Button>().ToList();Assert.AreEqual(count,tiles.Count);
                    if(expanded)foreach(var tile in tiles){Inside(panel.worldBound,tile.worldBound,"individual portrait");Assert.NotNull(tile.Q("unit-health"));Assert.Greater(tile.worldBound.width,20);}
                    Assert.AreEqual(count,Get<VisualElement>(seat,"selectionSummary").Query<Label>().ToList().Sum(l=>int.Parse(l.text)));
                }
                else
                {
                    var cells=Get<VisualElement>(seat,"selectionStats").Children().Where(e=>e.style.display.value!=DisplayStyle.None).ToArray();Assert.GreaterOrEqual(cells.Length,8);
                    foreach(var cell in cells){var key=cell.Q<Label>("selection-stat-label");var value=cell.Q<Label>("selection-stat-value");Inside(panel.worldBound,key.worldBound,"stat key");Inside(panel.worldBound,value.worldBound,"stat value");Assert.False(key.worldBound.Overlaps(value.worldBound),"stat text cannot overlap");}
                    var keys=cells.Select(c=>c.Q<Label>("selection-stat-label").text).ToArray();foreach(var key in new[]{"Здоровье","Урон","Дальность","Скорость","Обзор","Производство","Место в армии"})Assert.Contains(key,keys);
                }
            }
        }
        private IEnumerator CompactSelectionLayout(int humans)
        {
            compactBases.Clear();yield return StartOrdinary(humans);
            int w=humans==1?1280:1920,h=humans==1?800:1080;
            foreach(var kind in new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk})
            {
                CompactContext(1,kind,true);
                yield return Capture("compact-single-"+humans+"-"+kind,()=>CompactCaptureContext(1,kind),w,h,captureChecks:()=>AssertCompactSelection(false,1),refreshWorld:false);
            }
            CompactContext(50,reset:true);
            yield return Capture("compact-group50-"+humans,()=>CompactCaptureContext(50),w,h,captureChecks:()=>AssertCompactSelection(false,50),refreshWorld:false);
            foreach(var seat in Seats)Call(seat,"ToggleSelectionRoster",false);
            yield return Capture("expanded-group50-"+humans,()=>CompactCaptureContext(50,expanded:true),w,h,captureChecks:()=>AssertCompactSelection(true,50),refreshWorld:false);
            if(humans==2)yield return Capture("expanded-group50-narrow",()=>CompactCaptureContext(50,expanded:true),1280,720,captureChecks:()=>AssertCompactSelection(true,50),refreshWorld:false);
            foreach(var seat in Seats)Call(seat,"ToggleSelectionRoster",false);
            CompactContext(50,variants:true);foreach(var seat in Seats)Assert.AreEqual(4,Get<VisualElement>(seat,"selectionSummary").childCount,"upgraded and base types stay distinct");
            CompactContext(49,variants:true);foreach(var seat in Seats)Assert.AreEqual("ВЫБРАНО 49",Get<Label>(seat,"selectionGroupCount").text,"dead entries removed");
        }
        [UnityTest] public IEnumerator CompactSelectionSingleMatch()=>CompactSelectionLayout(1);
        [UnityTest] public IEnumerator CompactSelectionSplitMatch()=>CompactSelectionLayout(2);
        [UnityTest] public IEnumerator CompactSelectionControllerIsolation()
        {
            compactBases.Clear();yield return StartOrdinary(2);CompactContext(50,reset:true);
            var first=Seats[0];var second=Seats[1];var camera=Get<Camera>(first,"cameraView");float zoom=camera.orthographicSize;
            int receipts=Runtime.OfflineFrame.Receipts.Count;
            Press(pads[0].leftTrigger);InputSystem.Update();Call(first,"PollOrdinaryPad");
            Assert.True(Get<bool>(first,"selectionRosterExpanded"));Assert.True(Get<bool>(first,"selectionRosterPadFocus"));Assert.False(Get<bool>(second,"selectionRosterExpanded"));
            Release(pads[0].leftTrigger);InputSystem.Update();Call(first,"PollOrdinaryPad");
            Press(pads[0].dpad.down);InputSystem.Update();Call(first,"PollOrdinaryPad");Assert.AreEqual(zoom,camera.orthographicSize,"roster down cannot zoom");
            int focus=Get<int>(first,"selectionRosterFocusId");Assert.Greater(focus,20000);
            Release(pads[0].dpad.down);InputSystem.Update();Call(first,"PollOrdinaryPad");
            Press(pads[0].buttonEast);InputSystem.Update();Call(first,"PollOrdinaryPad");Assert.False(Get<bool>(first,"selectionRosterPadFocus"));Assert.AreEqual(50,Selection(first).Count);
            Release(pads[0].buttonEast);InputSystem.Update();Call(first,"PollOrdinaryPad");Assert.AreEqual(receipts,Runtime.OfflineFrame.Receipts.Count,"back cannot issue world order");
            // LT closes the still-expanded list; a second LT reopens with focus.
            Press(pads[0].leftTrigger);InputSystem.Update();Call(first,"PollOrdinaryPad");Release(pads[0].leftTrigger);InputSystem.Update();Call(first,"PollOrdinaryPad");
            Press(pads[0].leftTrigger);InputSystem.Update();Call(first,"PollOrdinaryPad");Release(pads[0].leftTrigger);InputSystem.Update();Call(first,"PollOrdinaryPad");
            Press(pads[0].buttonSouth);InputSystem.Update();Call(first,"PollOrdinaryPad");CollectionAssert.AreEqual(new[]{focus},Selection(first));Assert.AreEqual(50,Selection(second).Count);
            Release(pads[0].buttonSouth);InputSystem.Update();Call(first,"PollOrdinaryPad");Assert.AreEqual(receipts,Runtime.OfflineFrame.Receipts.Count);
            CompactContext(50,reset:true);Call(first,"ToggleSelectionRoster",false);Call(first,"SelectRosterUnit",20002,true);Assert.AreEqual(49,Selection(first).Count);Assert.False(Selection(first).Contains(20002));
            CompactContext(50,reset:true);Press(pads[0].leftTrigger);InputSystem.Update();Call(first,"PollOrdinaryPad");Put(first,"mapOpen",true);Call(first,"PollOrdinaryPad");Assert.False(Get<bool>(first,"selectionRosterPadFocus"));Put(first,"mapOpen",false);
            Release(pads[0].leftTrigger);InputSystem.Update();
        }
    }
}
