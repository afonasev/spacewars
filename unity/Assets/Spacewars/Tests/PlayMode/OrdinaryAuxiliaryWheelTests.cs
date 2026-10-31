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
        [Test] public void CompactGroupSlotsKeepSparseIdsPruneLossesAndCapAtNine()
        {
            var entities=new[]{new KeyboardSelectable(1,0,true,false,0,0),new KeyboardSelectable(2,0,true,false,1,0),new KeyboardSelectable(3,0,false,false,2,0)};
            var groups=new KeyboardControlGroups();
            CollectionAssert.AreEqual(new[]{1,2,3},AuxiliaryWheelPresentation.GroupSlots(groups,entities));
            groups.Apply(9,true,0,new[]{1,3},entities,300,out _);
            CollectionAssert.AreEqual(new[]{1,2,9},AuxiliaryWheelPresentation.GroupSlots(groups,entities),"a high occupied ID must not force nine sectors");
            groups.Apply(4,true,0,new[]{2},entities,300,out _);
            var frozen=AuxiliaryWheelPresentation.GroupSlots(groups,entities);CollectionAssert.AreEqual(new[]{1,2,4,9},frozen);
            groups.Add(1,new[]{1},entities);
            CollectionAssert.AreEqual(new[]{"1","2","4","9"},groups.PadActions(new[]{1},entities,true,frozen).Select(a=>a.Id),"held direction keeps its ID after assignment");
            CollectionAssert.AreEqual(new[]{1,2,3,4,9},AuxiliaryWheelPresentation.GroupSlots(groups,entities),"next opening includes the new spare slot");
            CollectionAssert.AreEqual(new[]{1,2,4},AuxiliaryWheelPresentation.GroupSlots(groups,new[]{entities[1],entities[2]}),"dead/foreign members do not keep slots occupied");
            groups.Reset();
            for(int count=1;count<=9;count++){groups.Apply(count,true,0,new[]{1},entities,300,out _);Assert.AreEqual(Math.Min(9,Math.Max(3,count+2)),AuxiliaryWheelPresentation.GroupSlots(groups,entities).Length);}
        }

        IEnumerator AuxiliaryWheels(int humans)
        {
            yield return StartOrdinary(humans,false,true);
            foreach(var seat in Seats)
            {
                var v=Get<PlayableSnapshot>(seat,"view");var own=v.Entities.Where(e=>e.Owner==v.Owner).Select(e=>e.Id).ToArray();
                var groups=Get<KeyboardControlGroups>(seat,"keyboardGroups");var entities=(KeyboardSelectable[])Call(seat,"KeyboardEntities");
                groups.Apply(1,true,0,own,entities,300,out _);groups.Apply(9,true,0,new[]{v.Buildings.First(b=>b.Owner==v.Owner).Id},entities,300,out _);
                foreach(int id in own.Take(1))Selection(seat).Add(id);
                var selected=Selection(seat).ToArray();int? active=groups.Active;
                var actions=AuxiliaryWheelPresentation.Groups(groups,selected,entities,v,true,AuxiliaryWheelPresentation.GroupSlots(groups,entities));
                Assert.AreEqual(own.Length,actions[0].Presentation.ResultCount,"union must not double-count selected members");
                Assert.AreEqual(0,actions[0].Presentation.AddCount);Assert.AreEqual(1,actions.Single(a=>a.Sector.Id=="9").Presentation.ResultCount,"unit addition replaces building-only group");
                CollectionAssert.AreEquivalent(selected,Selection(seat));Assert.AreEqual(active,groups.Active,"preview must not recall a group");
                groups.Apply(9,true,0,Array.Empty<int>(),entities,300,out _);
                Call(seat,"EnsureOrdinaryPadUi");Put(seat,"padOriginalHq",v.Buildings.First(b=>b.Owner==v.Owner).Id);
            }
            string displayedMode=null;
            void Context(string mode,string selected)
            {
                displayedMode=mode;
                foreach(var seat in Seats)
                {
                    var v=Get<PlayableSnapshot>(seat,"view");var groups=Get<KeyboardControlGroups>(seat,"keyboardGroups");var entities=(KeyboardSelectable[])Call(seat,"KeyboardEntities");
                    if(mode=="baseWheel"&&selected=="many")
                    {
                        var original=v.Buildings.First(b=>b.Owner==v.Owner);var buildings=new[]{original,new PlayableBuildingSnapshot(90001,v.Owner,PlayableBuildingKind.Outpost,original.Position,100,1,0,0,original.Position),new PlayableBuildingSnapshot(90002,v.Owner,PlayableBuildingKind.Headquarters,original.Position,100,1,0,0,original.Position)};
                        v=new PlayableSnapshot(v.ProfileId,v.ProfileRevision,v.Generation,v.Seed,v.Sequence,v.Tick,v.Status,false,v.Outcome,v.Credits,v.Geometry,v.Entities.ToArray(),buildings,v.Projectiles.ToArray(),v.Metrics,null,owner:v.Owner,ownerId:v.OwnerId,activeProfile:Get<PlayableProfile>(seat,"profile"));
                    }
                    var own=v.Entities.Where(e=>e.Owner==v.Owner).Select(e=>e.Id).Take(1).ToArray();
                    foreach(int slot in new[]{2,3,9})groups.Apply(slot,true,0,(selected=="five"&&(slot==2||slot==3)||selected=="sparse"&&slot==9)?own:Array.Empty<int>(),entities,300,out _);
                    var actions=mode=="baseWheel"?AuxiliaryWheelPresentation.Bases(v,Get<int>(seat,"padOriginalHq")):AuxiliaryWheelPresentation.Groups(groups,Selection(seat),entities,v,mode=="groupAssign",AuxiliaryWheelPresentation.GroupSlots(groups,entities),selected=="complete"?"1":null);
                    var root=Get<VisualElement>(seat,"root");var ring=Get<OrbitalBattleRing>(seat,"padAuxiliaryRing");ring.ConfigureInput(true,pads[Array.IndexOf(Seats,seat)],mode);ring.Resize(Mathf.Min(280,root.contentRect.height-100));
                    ring.Set(actions,mode=="baseWheel"?"Базы":mode=="groupAssign"?"Запись":"Группы",mode=="baseWheel"?actions[0].Sector.Id:selected=="complete"||selected=="three"||selected=="five"||selected=="sparse"?"1":selected,selected=="complete"?1:0,mode=="baseWheel"?"headquarters":"army");
                    if(mode=="baseWheel"&&selected=="many")Assert.AreEqual(3,actions.Select(a=>a.Label).Distinct().Count(),"base labels must be distinct");
                    ring.FitAuxiliary(root.contentRect.width,root.contentRect.height,(Rect?)Call(seat,"AuxiliaryMapObstacle"));ring.style.display=DisplayStyle.Flex;ring.BringToFront();Get<OrbitalBattleRing>(seat,"battleRing").style.display=DisplayStyle.None;
                }
            }
            void Bounds()
            {
                foreach(var seat in Seats)
                {
                    var root=Get<VisualElement>(seat,"root");var ring=Get<OrbitalBattleRing>(seat,"padAuxiliaryRing");var card=ring.Q<RadialActionCard>();
                    Inside(root.worldBound,ring.worldBound,"auxiliary wheel viewport");
                    if(displayedMode!="baseWheel")
                    {
                        var groups=Get<KeyboardControlGroups>(seat,"keyboardGroups");var entities=(KeyboardSelectable[])Call(seat,"KeyboardEntities");
                        var slots=AuxiliaryWheelPresentation.GroupSlots(groups,entities);
                        CollectionAssert.AreEqual(slots.Select(i=>"radial-"+i).ToArray(),ring.Query<Button>().ToList().Select(b=>b.name).ToArray(),"render uses compact stable slot identities");
                        foreach(int slot in slots){int count=groups.Members(slot,entities).Length;Assert.AreEqual(count>0?count+" ед.":"",ring.Q("radial-"+slot).Q<Label>("radial-sector-count").text,"empty sectors have no zero label");}
                        var art=ring.Q("radial-centre-artwork");var label=ring.Q<Label>("radial-centre-label");var glyph=art.Q<Image>("glyph-artwork");
                        Assert.AreEqual(ring.worldBound.center.x,art.worldBound.center.x,.5f,"centre icon horizontal alignment");Assert.AreEqual(art.worldBound.center.x,glyph.worldBound.center.x,.5f,"fallback glyph fills centre container");
                        Assert.AreEqual(art.worldBound.width,glyph.worldBound.width,.5f,"fallback glyph cannot keep its old fixed width");
                        Assert.AreEqual(ring.worldBound.center.y,(art.worldBound.yMin+label.worldBound.yMax)/2,.5f,"icon/title stack vertical alignment");
                    }
                    var first=ring.Q("radial-"+Get<int>(seat,"padOriginalHq"));if(displayedMode=="baseWheel"&&first!=null&&ring.Query<Button>().ToList().Count>1){var title=first.Q<Label>("radial-sector-title");Assert.LessOrEqual(title.worldBound.yMax,ring.worldBound.center.y-ring.worldBound.height*OrbitalBattleRing.InnerRatio,"top base label must not enter neutral centre");}Inside(root.worldBound,card.worldBound,"auxiliary card viewport");Assert.False(ring.worldBound.Overlaps(card.worldBound),"wheel and card cannot overlap");
                    Inside(card.worldBound,card.Q("radial-footer").worldBound,"auxiliary footer");
                    var map=Get<VisualElement>(app,"sharedMapFrame");if(map!=null){Assert.False(map.worldBound.Overlaps(ring.worldBound),"wheel must not cover shared minimap");Assert.False(map.worldBound.Overlaps(card.worldBound),"card must not cover shared minimap");}
                    foreach(var label in card.Query<Label>().ToList())if(label.resolvedStyle.display!=DisplayStyle.None&&label.worldBound.height>0)Inside(card.worldBound,label.worldBound,"auxiliary text: "+label.text);
                }
            }
            foreach(var state in new[]{("groupWheel","three"),("groupWheel","five"),("groupWheel","sparse"),("groupWheel","1"),("groupWheel","2"),("groupAssign","1"),("groupAssign","complete"),("baseWheel","1"),("baseWheel","many")})
                yield return Capture("auxiliary-"+humans+"-"+state.Item1+"-"+state.Item2,()=>Context(state.Item1,state.Item2),humans==1?1280:1920,humans==1?800:1080,captureChecks:Bounds);
            if(humans==4)yield return Capture("auxiliary-4-narrow",()=>Context("groupAssign","1"),1600,1000,captureChecks:Bounds);
            foreach(var seat in Seats){Get<OrbitalBattleRing>(seat,"padAuxiliaryRing").style.display=DisplayStyle.None;Get<OfflinePadGestures>(seat,"battleGestures").Cancel();}
            // Real input on every seat exercises the preparation-to-command eligibility transition.
            for(int i=0;i<Seats.Length;i++)
            {
                var seat=Seats[i];Assert.True(Get<PlayableInput>(seat,"input").CommandInputEnabled,"ready seat command eligibility");
                Put(seat,"padCursorInitialized",false);Set(pads[i].leftStick,Vector2.zero);InputSystem.Update();Call(app,"UpdateFrame");
                Tap(pads[i],pads[i].rightStickButton);Assert.True(Get<bool>(seat,"signalWheelOpen"),"R3 opens signal wheel for seat "+i);
            }
            void SignalContext()
            {
                foreach(var seat in Seats){var screen=Get<Camera>(seat,"cameraView").pixelRect.center;Call(seat,"OpenTeamSignal",screen,true);Get<TeamSignalWheel>(seat,"signalWheel").Choose(Vector2.up*-1,.2f);}
            }
            yield return Capture("auxiliary-"+humans+"-signals",SignalContext,humans==1?1280:1920,humans==1?800:1080,captureChecks:()=>
            {
                foreach(var seat in Seats){var root=Get<VisualElement>(seat,"root");var wheel=Get<TeamSignalWheel>(seat,"signalWheel");var hints=root.Q("team-signal-hints");Inside(root.worldBound,wheel.worldBound,"signal wheel viewport");Inside(root.worldBound,hints.worldBound,"signal hints viewport");Assert.False(wheel.worldBound.Overlaps(hints.worldBound));var map=Get<VisualElement>(app,"sharedMapFrame");if(map!=null){Assert.False(map.worldBound.Overlaps(wheel.worldBound),"signal wheel must not cover shared minimap");Assert.False(map.worldBound.Overlaps(hints.worldBound),"signal hints must not cover shared minimap");}foreach(var label in hints.Query<Label>().ToList())Inside(hints.worldBound,label.worldBound,"signal hint text");}
            });
            foreach(var seat in Seats)Call(seat,"CancelTeamSignal");
            var last=Seats.Last();var pad=pads.Last();var before=Selection(last).ToArray();long sequence=Get<long>(last,"sequence");
            Set(pad.leftStick,Vector2.zero);InputSystem.Update();Call(app,"UpdateFrame");Tap(pad,pad.rightStickButton);Assert.True(Get<bool>(last,"signalWheelOpen"));
            Set(pad.leftStick,Vector2.up);InputSystem.Update();Call(app,"UpdateFrame");Tap(pad,pad.buttonSouth);Assert.False(Get<bool>(last,"signalWheelOpen"));Assert.AreEqual(sequence+1,Get<long>(last,"sequence"));CollectionAssert.AreEquivalent(before,Selection(last));
            yield return Wait(()=>{Call(app,"UpdateFrame");return Get<PlayableSnapshot>(last,"view").TeamSignals.Count>0;},"team signal receipt");
            Assert.AreEqual(TeamSignalKind.Attack,Get<PlayableSnapshot>(last,"view").TeamSignals.Last().Kind);
        }
        [UnityTest]public IEnumerator AuxiliaryWheelsSingleMatch()=>AuxiliaryWheels(1);
        [UnityTest]public IEnumerator AuxiliaryWheelsSplitMatch()=>AuxiliaryWheels(2);
        [UnityTest]public IEnumerator AuxiliaryWheelsFourSeats()=>AuxiliaryWheels(4);
    }
}
