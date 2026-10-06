using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class ThreeCrossingsNativeTests
    {
        [Test] public void RiverBanksStayFlatTraversableAndHaveNoRaisedBallisticCover()
        {
            // Check both the code fallback and the actual shipped resource profile.
            var shipped=UnityEngine.JsonUtility.FromJson<ThreeCrossingsProfileData>(UnityEngine.Resources.Load<UnityEngine.TextAsset>("ThreeCrossingsProfile").text);
            foreach(var data in new[]{new ThreeCrossingsProfileData(),shipped})
            {
                var map=new ThreeCrossingsMap(data);
                foreach(int rotation in new[]{-1,1})foreach(int side in new[]{-1,1})
                {
                    var bank=new NavPoint(rotation*-12,side*20);
                    var approach=new NavPoint(rotation*-18,side*20);
                    var water=new NavPoint(0,side*20);
                    Assert.AreEqual(0,map.SurfaceHeight(bank));
                    Assert.AreEqual(-map.WaterDepth,map.SurfaceHeight(water));
                    Assert.IsTrue(map.SupportsSweep(approach,bank,.72),"Former rock strip must be reachable land");
                    Assert.IsFalse(map.SupportsSweep(bank,water,.72),"Cliff must still prevent entering water");
                    Assert.IsFalse(map.Solids.Any(s=>s.Footprint.Contains(bank)),"Shared render/minimap/ballistic solids must not retain the bank strip");
                    Assert.IsTrue(PlayableBallistics.TerrainLineClear(new BallisticPoint(approach.X,1,approach.Z),new BallisticPoint(0,1,bank.Z),0,map.Solids,1,map.SurfaceHeight));
                }
            }
        }
        [UnityTest] public IEnumerator NativeRoutesAndMixedGroupMotionRespectFullyBuiltMap()
        {
            var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
            var m=p.AuthoredMap;var obstacles=PlayableMap.StaticObstacles(p).ToList();
            foreach(var site in TerritoryRules.Sites(p)){Add(site.Position,TerritoryRules.Radius(p,site.Kind));foreach(var slot in site.Slots)Add(slot.Position,Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius)));}
            var g=new NavGeometry(p.ArenaHalfExtent,obstacles.ToArray(),1);
            using(var router=new UnityNavigationRouter(g,p.Navigation))
            {
                foreach(var site in TerritoryRules.Sites(p))foreach(var slot in site.Slots){var exit=new NavPoint(slot.Position.X+Math.Cos(slot.Heading)*p.FactoryExitDistance,slot.Position.Z+Math.Sin(slot.Heading)*p.FactoryExitDistance);Route(router,g,exit,new NavPoint(-16,0),p.TankCollisionRadius);}
                foreach(double z in new[]{-m.CrossingZ,0,m.CrossingZ})
                {
                    var crowd=new NavCrowd(g,p.Navigation);var origins=new NavPoint[3];
                    for(int i=0;i<3;i++){origins[i]=new NavPoint(-18,z+(i-1)*p.Navigation.ArrivalSlotSpacing);var kind=i==0?PlayableEntityKind.Explorer:i==1?PlayableEntityKind.Tank:PlayableEntityKind.Shkval;crowd.Add(i,origins[i],PlayableUnitRules.Radius(p,kind),PlayableUnitRules.Speed(p,kind),PlayableUnitRules.Turn(p,kind));}
                    for(int leg=0;leg<2;leg++)
                    {
                        foreach(var unit in crowd.Units){var goal=leg==0?new NavPoint(18,origins[unit.Id].Z):origins[unit.Id];Assert.IsTrue(crowd.SetRoute(unit.Id,goal,router.FindPath(unit.Position,goal)));}
                        for(int tick=0;tick<2400&&crowd.Units.Any(u=>u.Outcome!=NavigationOutcome.Arrived);tick++){var previous=crowd.Units.Select(u=>u.Position).ToArray();crowd.Step(1d/30);foreach(var u in crowd.Units)Assert.IsTrue(g.SegmentFree(previous[u.Id],u.Position,u.Radius));}
                        Assert.IsTrue(crowd.Units.All(u=>u.Outcome==NavigationOutcome.Arrived));
                    }
                    yield return null;
                }
                foreach(var ramp in m.Supports.Where(s=>s.Id.Contains("ramp")))
                {
                    var gradient=ramp.Gradient;double length=Math.Sqrt(gradient.X*gradient.X+gradient.Z*gradient.Z);var d=JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text);
                    var outside=new NavPoint(ramp.Origin.X-gradient.X/length*p.Navigation.ArrivalSlotSpacing,ramp.Origin.Z-gradient.Z/length*p.Navigation.ArrivalSlotSpacing);
                    var inside=new NavPoint(ramp.Origin.X+gradient.X/length*(d.rampLength+p.Navigation.ArrivalTolerance),ramp.Origin.Z+gradient.Z/length*(d.rampLength+p.Navigation.ArrivalTolerance));
                    Route(router,g,outside,inside,p.TankCollisionRadius);Route(router,g,inside,outside,p.TankCollisionRadius);
                    var crowd=new NavCrowd(g,p.Navigation);crowd.Add(0,outside,p.TankCollisionRadius,p.TankSpeed,p.TankTurnSpeed);Assert.IsTrue(crowd.SetRoute(0,inside,router.FindPath(outside,inside)));
                    for(int tick=0;tick<1200&&crowd.Units[0].Outcome!=NavigationOutcome.Arrived;tick++){var before=crowd.Units[0].Position;crowd.Step(1d/30);var after=crowd.Units[0].Position;Assert.IsTrue(g.SegmentFree(before,after,p.TankCollisionRadius));Assert.LessOrEqual(Math.Abs(m.SurfaceHeight(after)-m.SurfaceHeight(before)),length*Math.Sqrt(Math.Pow(after.X-before.X,2)+Math.Pow(after.Z-before.Z,2))+1e-8);}
                    Assert.AreEqual(NavigationOutcome.Arrived,crowd.Units[0].Outcome);
                }
                foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1})Route(router,g,new NavPoint(side*m.PocketX,end*24),new NavPoint(side*m.PocketX,end*6),p.TankCollisionRadius);
            }
            void Add(NavPoint q,double r)=>obstacles.Add(new NavObstacle(q.X-r,q.Z-r,q.X+r,q.Z+r));
        }
        [Test] public void TerrainMeshHasOutwardVisibleSideFaces()
        {
            var type=typeof(UnityNavigationRouter).Assembly.GetType("Spacewars.Presentation.TerrainMesh",true);
            var shape=new NavObstacle(new NavPolygon(new[]{new NavPoint(-2,-3),new NavPoint(2,-3),new NavPoint(2,3),new NavPoint(-2,3)}));
            var mesh=(Mesh)type.GetMethod("Prism").Invoke(null,new object[]{shape,0d,5d,null});
            try{for(int i=0;i<mesh.vertexCount;i++){var normal=mesh.normals[i];if(Math.Abs(normal.y)>.5)continue;var point=mesh.vertices[i];Assert.Greater(Vector3.Dot(normal,new Vector3(point.x,0,point.z)),0,"Outside side faces must survive back-face culling");}}
            finally{UnityEngine.Object.DestroyImmediate(mesh);}
        }
        private static void Route(UnityNavigationRouter router,NavGeometry g,NavPoint a,NavPoint b,double radius){var path=router.FindPath(a,b);Assert.IsNotEmpty(path);foreach(var q in path){Assert.IsTrue(g.SegmentFree(a,q,radius));a=q;}}
    }
}
