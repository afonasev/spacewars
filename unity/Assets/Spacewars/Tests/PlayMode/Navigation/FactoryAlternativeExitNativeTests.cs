using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Tests.PlayMode
{
    public sealed class FactoryAlternativeExitNativeTests
    {
        [Test] public void ShippedAcceptedMapValidatesTypedExitsAgainstFullBuiltAuthorityAndBridgeWaterRockFootprints()
        {
            var data=JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text);
            var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(data));
            Assert.AreEqual("three-crossings-greybox-v1",p.AuthoredMap.Id);Assert.AreEqual(3,p.AuthoredMap.Revision);
            var obstacles=PlayableMap.StaticObstacles(p).ToList();
            foreach(var site in TerritoryRules.Sites(p)){Add(site.Position,TerritoryRules.Radius(p,site.Kind));foreach(var slot in site.Slots)Add(slot.Position,Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius)));}
            var g=new NavGeometry(p.ArenaHalfExtent,obstacles.ToArray(),1);
            foreach(var kind in new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval})
            {
                double r=PlayableUnitRules.Radius(p,kind);
                foreach(var site in TerritoryRules.Sites(p))foreach(var slot in site.Slots)
                {
                    var anchor=new NavPoint(slot.Position.X+Math.Cos(slot.Heading)*p.FactoryExitDistance,slot.Position.Z+Math.Sin(slot.Heading)*p.FactoryExitDistance);
                    var candidates=FactoryProductionAnchors.Candidates(slot.Position,p.FactoryFootprintRadius,r,new[]{anchor});
                    Assert.True(candidates.Any(q=>g.IsFree(q,r)),site.Id+":"+slot.Id+":"+kind);
                    foreach(var q in candidates.Where(q=>g.IsFree(q,r)))Assert.True(p.AuthoredMap.SupportsSweep(q,q,r));
                }
                var terrain=new NavGeometry(p.ArenaHalfExtent,PlayableMap.StaticObstacles(p),1);var crowd=new NavCrowd(terrain,p.Navigation);
                Assert.False(crowd.CanPlace(new NavPoint(0,20),r),"Water");Assert.False(crowd.CanPlace(new NavPoint(-30,0),r),"Rock");
                var edge=new NavPoint(0,data.crossingZ+data.sideBridgeWidth/2-r+.001);
                Assert.True(terrain.IsFree(edge,0),"Bridge center is supported");Assert.False(crowd.CanPlace(edge,r),"Typed full footprint crosses bridge water edge");
                Assert.True(crowd.CanPlace(new NavPoint(0,data.crossingZ),r),"Bridge center full footprint");
            }
            void Add(NavPoint q,double r)=>obstacles.Add(new NavObstacle(q.X-r,q.Z-r,q.X+r,q.Z+r));
        }
    }
}
