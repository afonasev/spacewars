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
        IEnumerator DefenseCards(int humans)
        {
            yield return StartOrdinary(humans);
            void Context(PlayableBuildingKind kind)
            {
                foreach(var seat in Seats)
                {
                    var view=Get<PlayableSnapshot>(seat,"view");var p=Get<PlayableProfile>(seat,"profile");
                    var site=view.Sites.First(s=>s.Owner==view.Owner&&s.Ready);var pad=site.Site.DefenseSlots(p).First();
                    Selection(seat).Clear();Put(seat,"selectedSite",site.Site.Id);Put(seat,"selectedSlot",pad.Id);
                    Put(seat,"battleController",true);Put(seat,"battleSector","build:"+site.Site.Id+":"+pad.Id+":"+kind);Call(seat,"UpdateHud");
                }
            }
            void Bounds(PlayableBuildingKind kind)
            {
                if(humans>1)AssertSplitDividers();
                foreach(var seat in Seats)
                {
                    var root=Get<VisualElement>(seat,"root");var ring=Get<OrbitalBattleRing>(seat,"battleRing");var card=ring.Q<RadialActionCard>();
                    Assert.AreEqual(DisplayStyle.Flex,card.style.display.value);
                    Inside(root.worldBound,ring.worldBound,"defense ring");Inside(root.worldBound,card.worldBound,"defense card");
                    Inside(card.worldBound,card.Q("radial-footer").worldBound,"defense footer");
                    Assert.False(ring.worldBound.Overlaps(card.worldBound),"card clears ring");
                    var actions=Get<OfflinePadAction[]>(seat,"battleActions");Assert.AreEqual(3,actions.Length);
                    var action=actions.Single(a=>a.Sector.Id.EndsWith(":"+kind));
                    var stats=card.Query<VisualElement>("radial-stat").ToList();Assert.AreEqual(kind==PlayableBuildingKind.SentryTurret?7:kind==PlayableBuildingKind.GaussTower?6:3,stats.Count);
                    if(kind==PlayableBuildingKind.GaussTower){Assert.False(action.Sector.Enabled);Assert.IsNull(action.Command);StringAssert.Contains("научный центр",card.Q<Label>("radial-note").text);Assert.AreEqual(DisplayStyle.None,card.Q("radial-actions").style.display.value);}
                    for(int i=0;i<stats.Count;i++)
                    {
                        var cell=stats[i];var label=cell.Q<Label>("radial-stat-label");var value=cell.Q<Label>("radial-stat-value");
                        Inside(card.worldBound,label.worldBound,"stat label");Inside(card.worldBound,value.worldBound,"stat value");Assert.False(label.worldBound.Overlaps(value.worldBound));
                        if(stats.Count>3&&i%2==1)Assert.GreaterOrEqual(label.worldBound.xMin-cell.worldBound.xMin,RadialCardProfile.Load().statColumnPadding,"text clears vertical divider");
                        if(stats.Count>3&&i%2==0)Assert.GreaterOrEqual(cell.worldBound.xMax-value.worldBound.xMax,RadialCardProfile.Load().statColumnPadding-.1f,"value clears vertical divider");
                    }
                }
            }
            foreach(var kind in new[]{PlayableBuildingKind.SentryTurret,PlayableBuildingKind.GaussTower,PlayableBuildingKind.RepairTower})
                yield return Capture("defense-cards-"+humans+"-"+kind,()=>Context(kind),humans==1?1280:1920,humans==1?800:1080,captureChecks:()=>Bounds(kind));
        }
        IEnumerator ConstructionWheel(int humans,bool science=false)
        {
            yield return StartOrdinary(humans);
            void Context(bool defense)
            {
                foreach(var seat in Seats)
                {
                    var v=Get<PlayableSnapshot>(seat,"view");var p=Get<PlayableProfile>(seat,"profile");
                    var site=v.Sites.First(t=>t.Owner==v.Owner&&t.Ready);int slot=defense?site.Site.DefenseSlots(p).First().Id:site.Site.Slots.First().Id;
                    // Deterministic owner UI projection: both gauss prerequisites are missing.
                    Put(seat,"view",new PlayableSnapshot(v.ProfileId,v.ProfileRevision,v.Generation,v.Seed,v.Sequence,v.Tick,v.Status,false,v.Outcome,250,v.Geometry,v.Entities.ToArray(),v.Buildings.ToArray(),v.Projectiles.ToArray(),v.Metrics,null,sites:v.Sites.ToArray(),incomePerSecond:v.IncomePerSecond,vision:v.Vision,population:v.Population,ownerResearch:v.OwnerResearch.ToArray(),owner:v.Owner,ownerId:v.OwnerId,activeProfile:p));
                    Selection(seat).Clear();Put(seat,"selectedSite",site.Site.Id);Put(seat,"selectedSlot",slot);Put(seat,"battleController",true);
                    Put(seat,"battleSector","build:"+site.Site.Id+":"+slot+":"+(defense?PlayableBuildingKind.GaussTower:science?PlayableBuildingKind.ScientificCenter:PlayableBuildingKind.Factory));Call(seat,"UpdateHud");
                }
            }
            void Bounds(bool defense)
            {
                if(humans>1)AssertSplitDividers();
                foreach(var seat in Seats)
                {
                    var root=Get<VisualElement>(seat,"root");var ring=Get<OrbitalBattleRing>(seat,"battleRing");var card=ring.Q<RadialActionCard>();
                    Inside(root.worldBound,ring.worldBound,"construction wheel");Inside(root.worldBound,card.worldBound,"construction card");Inside(card.worldBound,card.Q("radial-footer").worldBound,"footer");
                    Assert.False(card.worldBound.Overlaps(ring.worldBound));
                    Assert.AreEqual(defense?"Нужен научный центр\nНе хватает 150 кредитов":science?"Нужна фабрика или ангар\nНе хватает 50 кредитов":"Не хватает 50 кредитов",card.Q<Label>("radial-note").text);
                    var buttons=ring.Children().OfType<Button>().ToArray();Assert.AreEqual(defense?3:4,buttons.Length);
                    foreach(var button in buttons)
                    {
                        var title=button.Q<Label>("radial-sector-title");var price=button.Q<Label>("radial-sector-count");var art=button.Q<RadialArtwork>();var padlock=button.Q("radial-sector-lock");
                        Assert.AreEqual(DisplayStyle.Flex,title.style.display.value);Assert.IsNotEmpty(title.text);
                        Inside(ring.worldBound,title.worldBound,"sector title");Inside(ring.worldBound,price.worldBound,"sector price");Inside(ring.worldBound,art.worldBound,"sector portrait");
                        Assert.GreaterOrEqual(title.contentRect.width+.5f,title.MeasureTextSize(title.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x,"full short name fits");
                        var textRect=title.worldBound;
                        foreach(var corner in new[]{new Vector2(textRect.xMin,textRect.yMin),new Vector2(textRect.xMax,textRect.yMin),new Vector2(textRect.xMin,textRect.yMax),new Vector2(textRect.xMax,textRect.yMax)})
                            Assert.True(button.ContainsPoint(button.WorldToLocal(corner)),"title clears the painted petal: "+title.text);
                        Assert.LessOrEqual(art.worldBound.yMax,title.worldBound.yMin+.5f);Assert.LessOrEqual(title.worldBound.yMax,price.worldBound.yMin+.5f);
                        if(padlock.style.display.value==DisplayStyle.Flex)
                        {
                            Inside(ring.worldBound,padlock.worldBound,"sector lock");
                            float textWidth=price.MeasureTextSize(price.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x;
                            Assert.GreaterOrEqual(padlock.worldBound.xMin,price.worldBound.center.x+textWidth/2,"lock clears price text");
                        }
                    }
                }
            }
            foreach(bool defense in science?new[]{false}:new[]{false,true})yield return Capture((science?"science-prerequisite-":"construction-b-")+humans+"-"+(defense?"defense":"buildings"),()=>Context(defense),humans==1?1280:1920,humans==1?800:1080,captureChecks:()=>Bounds(defense));
        }
        [UnityTest] public IEnumerator SciencePrerequisiteSingleMatch()=>ConstructionWheel(1,true);
        [UnityTest] public IEnumerator SciencePrerequisiteSplitMatch()=>ConstructionWheel(2,true);
        [UnityTest] public IEnumerator ConstructionWheelSingleMatch()=>ConstructionWheel(1);
        [UnityTest] public IEnumerator ConstructionWheelSplitMatch()=>ConstructionWheel(2);
        [UnityTest] public IEnumerator DefenseCardsSingleMatch()=>DefenseCards(1);
        [UnityTest] public IEnumerator DefenseCardsSplitMatch()=>DefenseCards(2);
    }
}
