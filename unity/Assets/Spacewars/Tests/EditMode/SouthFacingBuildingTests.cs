using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class SouthFacingBuildingTests
    {
        [Test] public void SouthFacingFactoryCorridorsFitExistingMapPads()
        {
            var maps=new IPlayableTerrain[]{null,ThreeCrossingsMap.Default,new FoundryMap(new FoundryProfileData()),new MossHighlandMap(),new CrimsonGroveMap(),new BasaltCrownMap(),new WhiteMeridianMap(),new NereidaMap(new NereidaProfileData())};
            var failures=new List<string>();int checkedCorridors=0;
            foreach(var map in maps)
            {
                var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var sites=TerritoryRules.Sites(p);
                foreach(var site in sites)foreach(var slot in site.Slots)
                {
                    var blockers=PlayableMap.StaticObstacles(p).ToList();
                    foreach(var other in sites)
                    {
                        double r=TerritoryRules.Radius(p,other.Kind);blockers.Add(new NavObstacle(other.Position.X-r,other.Position.Z-r,other.Position.X+r,other.Position.Z+r));
                        foreach(var pad in other.Slots)
                        {
                            if(site.Id==other.Id&&slot.Id==pad.Id)continue;
                            r=Math.Max(p.HangarFootprintRadius,Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius)));
                            blockers.Add(new NavObstacle(pad.Position.X-r,pad.Position.Z-r,pad.Position.X+r,pad.Position.Z+r));
                        }
                    }
                    var g=new NavGeometry(p.ArenaHalfExtent,blockers.ToArray(),1);
                    foreach(var kind in new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval})
                    {
                        var exit=FactoryReleaseRules.Exit(p,slot.Position,-Math.PI/2,kind);double radius=PlayableUnitRules.Radius(p,kind);checkedCorridors++;
                        if(!g.SegmentFree(slot.Position,exit,radius)||(map!=null&&!map.SupportsSweep(slot.Position,exit,radius)))
                            failures.Add($"{map?.GetType().Name??"Diagnostic"} site={site.Id} pad={slot.Id} {kind}: ({slot.Position.X:R},{slot.Position.Z:R}) -> ({exit.X:R},{exit.Z:R})");
                    }
                }
            }
            TestContext.WriteLine($"Checked {checkedCorridors} south-facing corridors; conflicts={failures.Count}\n"+string.Join("\n",failures));
            Assert.IsEmpty(failures,"South-facing release must fit every existing pad before runtime orientation changes");
        }
    }
}
