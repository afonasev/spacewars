using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using Spacewars.Runtime;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class ThreeCrossingsContourTests
    {
        [Test] public void ConcaveFootprintKeepsNotchFreeAndSweepsEdgesExactly()
        {
            var poly=new NavPolygon(new[]{new NavPoint(0,0),new NavPoint(6,0),new NavPoint(6,2),new NavPoint(2,2),new NavPoint(2,6),new NavPoint(0,6)});
            var g=new NavGeometry(20,new[]{new NavObstacle(poly)},1);
            Assert.IsTrue(g.IsFree(new NavPoint(4,4),.7));Assert.IsFalse(g.IsFree(new NavPoint(1,4),.7));
            Assert.IsTrue(g.SegmentFree(new NavPoint(3,5),new NavPoint(5,3),.5));Assert.IsFalse(g.SegmentFree(new NavPoint(-1,4),new NavPoint(4,4),.5));
            double area=0;for(int i=0;i<poly.Triangles.Count;i+=3){var a=poly.Vertices[poly.Triangles[i]];var b=poly.Vertices[poly.Triangles[i+1]];var c=poly.Vertices[poly.Triangles[i+2]];area+=Math.Abs((b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X))/2;}Assert.AreEqual(20,area,1e-9);
            Assert.Throws<ArgumentException>(()=>new NavPolygon(new[]{new NavPoint(0,0),new NavPoint(4,4),new NavPoint(0,4),new NavPoint(4,0)}));
        }
        [Test] public void PolygonRectangleMatchesLegacyPointAndSweepIncludingTouch()
        {
            var r=new NavObstacle(-2,-3,2,3);var a=new NavGeometry(20,new[]{r},1);var b=new NavGeometry(20,new[]{new NavObstacle(r.Footprint)},1);
            for(double x=-5;x<=5;x+=.5)for(double z=-6;z<=6;z+=.5){var p=new NavPoint(x,z);Assert.AreEqual(a.IsFree(p,.5),b.IsFree(p,.5));Assert.AreEqual(a.SegmentFree(p,new NavPoint(5,-5),.5),b.SegmentFree(p,new NavPoint(5,-5),.5));}
        }
        [Test] public void PlatformRampPortalsHaveContinuousAuthoritativeHeightAndBlockedSides()
        {
            var d=new ThreeCrossingsProfileData();var m=new ThreeCrossingsMap(d);var ramps=m.Supports.Where(s=>s.Id.Contains("ramp")).ToArray();Assert.AreEqual(8,ramps.Length);
            foreach(var ramp in ramps){var gradient=ramp.Gradient;double slope=Math.Sqrt(gradient.X*gradient.X+gradient.Z*gradient.Z);var outer=ramp.Origin;var inner=new NavPoint(outer.X+gradient.X/slope*d.rampLength,outer.Z+gradient.Z/slope*d.rampLength);
                Assert.AreEqual(0,m.SurfaceHeight(outer),1e-9);Assert.AreEqual(d.platformHeight,m.SurfaceHeight(inner),1e-9);
                Assert.IsTrue(m.SupportsSweep(outer,inner,.72));
                var middle=new NavPoint((outer.X+inner.X)/2,(outer.Z+inner.Z)/2);Assert.AreEqual(d.platformHeight/2,m.SurfaceHeight(middle),1e-9);
                var side=new NavPoint(middle.X-gradient.Z/slope*d.rampWidth,middle.Z+gradient.X/slope*d.rampWidth);Assert.IsFalse(m.SupportsSweep(middle,side,.72));
            }
            foreach(var s in m.Supports.Where(s=>s.Id.StartsWith("platform-")&&!s.Id.Contains("ramp")))Assert.AreEqual(8,s.Bounds.Polygon.Vertices.Count);
        }
        [Test] public void BallisticsUsePolygonNotBoundsAndSolidTierHeight()
        {
            var tri=new NavObstacle(new NavPolygon(new[]{new NavPoint(0,0),new NavPoint(4,0),new NavPoint(0,4)}),0,5);
            Assert.IsTrue(PlayableBallistics.TerrainLineClear(new BallisticPoint(3,2,3),new BallisticPoint(5,2,3),0,new[]{tri},1,p=>0));
            Assert.IsFalse(PlayableBallistics.TerrainLineClear(new BallisticPoint(-1,3,1),new BallisticPoint(4,3,1),0,new[]{tri},1,p=>0));
            Assert.IsTrue(PlayableBallistics.TerrainLineClear(new BallisticPoint(-1,6,1),new BallisticPoint(4,6,1),0,new[]{tri},1,p=>0));
        }
        [Test] public void RaisedTerrainStopsFlightAndBlastAndPredictionFollowsRamp()
        {
            Func<NavPoint,double> ground=p=>p.X<0?0:2;
            var flight=new BallisticFlight(new BallisticPoint(-2,1,0),new BallisticPoint(2,1,0),0,10);
            var hit=PlayableBallistics.FirstContact(flight,.1,Array.Empty<NavObstacle>(),1,Array.Empty<BallisticBody>(),0,terrain:ground);Assert.Less(hit.Value.Progress,1);
            var body=new BallisticBody(1,PlayableOwner.Player,new NavPoint(2,0),new NavPoint(1,0),.5,1,100,false,2);
            Assert.IsFalse(PlayableBallistics.BlastHits(new BallisticPoint(-2,1,0),10,body,Array.Empty<NavObstacle>(),1,new[]{body},ground));
            var moving=new BallisticBody(2,PlayableOwner.Player,new NavPoint(-1,0),new NavPoint(1,0),.5,1,100,false);Assert.AreEqual(2,moving.Predict(2,ground).BaseHeight);
        }
        [Test] public void NativeExplorerShotsHitAcrossBothPlatformRampDirections()
        {
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            foreach(bool uphill in new[]{false,true})
            {
                var profile=PlayableProfile.ThreeCrossingsDefault;var domain=Activator.CreateInstance(type,flags,null,new object[]{profile,1L},null);
                object Call(string method,params object[] args)=>type.GetMethod(method,flags).Invoke(domain,args);
                var units=(IDictionary)type.GetField("units",flags).GetValue(domain);var navigation=(NavigationSession)type.GetProperty("Navigation",flags).GetValue(domain);
                foreach(int id in units.Keys.Cast<int>().ToArray()){navigation.Remove(id);units.Remove(id);}
                var from=new NavPoint(uphill?-29:-37,-46);var to=new NavPoint(uphill?-37:-29,-46);
                var owner=uphill?PlayableOwner.Enemy:PlayableOwner.Player;int shooter=(int)Call("SpawnUnit",from,owner,PlayableEntityKind.Explorer),target=(int)Call("SpawnUnit",to,uphill?PlayableOwner.Player:PlayableOwner.Enemy,PlayableEntityKind.Tank);
                var unit=units[shooter];unit.GetType().GetField("Target").SetValue(unit,target);
                for(int remaining=profile.ExplorerBurstSize;remaining>0;remaining--){unit.GetType().GetField("BurstRemaining").SetValue(unit,remaining);Call("FireTracer",unit,from,to);}
                var shots=(IList)type.GetField("projectiles",flags).GetValue(domain);Assert.AreEqual(profile.ExplorerBurstSize,shots.Count);var shot=shots[0];
                double height=(double)shot.GetType().GetField("Height").GetValue(shot);Assert.Greater(height,profile.AuthoredMap.SurfaceHeight(from));
                var snapshot=(PlayableSnapshot)Call("Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);Assert.AreEqual(height,snapshot.Projectiles[0].Height);Assert.AreEqual(owner,snapshot.Projectiles[0].Faction);
                for(int i=0;i<100&&shots.Count>0;i++)Call("AdvanceProjectiles",.01d);
                Assert.Less(Convert.ToDouble(units[target].GetType().GetField("Health").GetValue(units[target])),profile.TankHealth,"Explorer must hit across the ramp in both directions");
            }
        }
        [Test] public void InclinedDirectShotCanEnterVerticalBodyAfterHorizontalEntry()
        {
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var domain=Activator.CreateInstance(type,flags,null,new object[]{PlayableProfile.ThreeCrossingsDefault,1L},null);
            int target=(int)type.GetMethod("SpawnUnit",flags).Invoke(domain,new object[]{new NavPoint(-20,0),PlayableOwner.Enemy,PlayableEntityKind.Tank});
            int hit=(int)type.GetMethod("FirstHitAtHeight",flags).Invoke(domain,new object[]{PlayableOwner.Player,new NavPoint(-23,0),new NavPoint(-17,0),0d,3.5d,-1d});Assert.AreEqual(target,hit);
        }
        [Test] public void EditorPolygonRouterReleasesItsNativeDataAndMeshesWithoutErrors()
        {
            var type=Type.GetType("Spacewars.Presentation.UnityNavigationRouter, Spacewars.Presentation",true);
            var polygon=new NavPolygon(new[]{new NavPoint(-2,-2),new NavPoint(2,-2),new NavPoint(0,2)});
            var geometry=new NavGeometry(16,new[]{new NavObstacle(polygon)},1);
            // The test runner treats unexpected Unity Error logs as failures, including deferred Destroy in EditMode.
            using(var router=(IDisposable)Activator.CreateInstance(type,new object[]{geometry,NavigationProfile.Default})){}
        }
        [Test] public void AuthoredContourMetadataHasStablePathsAndRejectsInvalidCoordinates()
        {
            var d=new ThreeCrossingsProfileData();var fields=ThreeCrossingsMap.ContourFields(d).ToArray();Assert.AreEqual(fields.Length,fields.Select(f=>f.Path).Distinct().Count());
            foreach(var f in fields){Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Greater(f.Step,0);}
            fields[0].Write(double.NaN);Assert.Throws<ArgumentException>(()=>new ThreeCrossingsMap(d));
        }
    }
}
