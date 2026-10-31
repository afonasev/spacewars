using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        IEnumerator CurrentArtworkUi(int humans)
        {
            yield return StartOrdinary(humans);
            foreach(bool defense in new[]{false,true})
                yield return Capture("empty-slot-"+humans+"-"+(defense?"defense":"ordinary"),()=>
                {
                    foreach(var seat in Seats)
                    {
                        var view=Get<PlayableSnapshot>(seat,"view");var p=Get<PlayableProfile>(seat,"profile");var site=view.Sites.First(s=>s.Owner==view.Owner&&s.Ready);
                        int slot=defense?site.Site.DefenseSlots(p).First().Id:site.Site.Slots.First().Id;
                        Selection(seat).Clear();Put(seat,"selectedSite",site.Site.Id);Put(seat,"selectedSlot",slot);Put(seat,"battleController",true);
                        Put(seat,"battleSector","build:"+site.Site.Id+":"+slot+":"+(defense?PlayableBuildingKind.GaussTower:PlayableBuildingKind.ScientificCenter));Call(seat,"UpdateHud");
                    }
                },humans==1?1280:1920,humans==1?800:1080,captureChecks:()=>
                {
                    foreach(var seat in Seats){Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(seat,"armyRegion").style.display.value);Assert.AreEqual(DisplayStyle.Flex,Get<OrbitalBattleRing>(seat,"battleRing").style.display.value);Assert.NotNull(Get<OrbitalBattleRing>(seat,"battleRing").Q<RadialActionCard>());}
                });
            yield return Capture("tab-army-"+humans,()=>
            {
                foreach(var seat in Seats){Selection(seat).Clear();Put(seat,"selectedSite",0);Put(seat,"selectedSlot",0);if(!Get<bool>(seat,"mapOpen"))Call(seat,"ToggleMap");Call(seat,"UpdateHud");}
            },humans==1?1280:1920,humans==1?800:1080,captureChecks:()=>
            {
                foreach(var seat in Seats)
                {
                    var list=Get<VisualElement>(seat,"armyComposition");Assert.AreEqual(DisplayStyle.Flex,list.style.display.value);
                    var icons=list.Query<OrbitalGlyph>().ToList();Assert.AreEqual(4,icons.Count);
                    foreach(var icon in icons){Assert.AreSame(DockArtwork.TextureFor(icon.Kind),icon.Q<Image>("glyph-portrait").image);if(icon.parent.resolvedStyle.display==DisplayStyle.Flex)Inside(list.worldBound,icon.worldBound,"TAB unit portrait");Assert.AreEqual(OrbitalTheme.Ink,icon.Q<Image>("portrait-outline").tintColor);}
                    Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(seat,"armyRegion").style.display.value);
                }
            });
        }
        [UnityTest] public IEnumerator CurrentArtworkSingleMatch()=>CurrentArtworkUi(1);
        [UnityTest] public IEnumerator CurrentArtworkSplitMatch()=>CurrentArtworkUi(2);
    }
}
