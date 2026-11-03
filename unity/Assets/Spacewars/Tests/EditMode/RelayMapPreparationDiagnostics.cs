using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class RelayMapPreparationDiagnostics
    {
        [TestCase("City")][TestCase("Grove")][TestCase("Basalt")]
        public void InspectExactMapPreparationInputs(string key)
        {
            IPlayableTerrain map=key=="City"?(IPlayableTerrain)new AntennaCityMap():key=="Grove"?new CrimsonGroveMap():(IPlayableTerrain)new BasaltCrownMap();
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var source=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var input=RecastAuthorityGeometry.Prepare(map,source,p.Navigation);var f=typeof(RecastAuthorityGeometry).GetField("Vertices",BindingFlags.Instance|BindingFlags.NonPublic);var vertices=(float[])f.GetValue(input);
            var x=Enumerable.Range(0,vertices.Length/3).Select(i=>vertices[i*3]).ToArray();var z=Enumerable.Range(0,vertices.Length/3).Select(i=>vertices[i*3+2]).ToArray();
            TestContext.WriteLine("MAP_INPUT key="+key+" supports="+map.Supports.Count+" obstacles="+source.Obstacles.Count+" triangles="+input.TriangleCount+" vertices="+vertices.Length/3+" cell="+p.Navigation.LocalGridCell+" scratch="+typeof(RecastAuthorityGeometry).GetMethod("ScratchBound",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{map,source,true})+" bounds="+x.Min()+","+z.Min()+".."+x.Max()+","+z.Max());
            Assert.LessOrEqual(x.Max(),map.HalfExtent);Assert.GreaterOrEqual(x.Min(),-map.HalfExtent);Assert.LessOrEqual(z.Max(),map.HalfExtent);Assert.GreaterOrEqual(z.Min(),-map.HalfExtent);
            Assert.Less((long)typeof(RecastAuthorityGeometry).GetMethod("ScratchBound",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{map,source,true}),LayeredNavigationScheduler.MaxRetainedBytes);
            if(key=="Basalt"){
                double radius=p.ExplorerCollisionRadius;var nav=p.Navigation.ForUnit(radius,p.ExplorerSpeed,p.Navigation.TankTurnSpeed);
                var routeInput=RecastAuthorityGeometry.Prepare(map,source,nav);using var native=new RecastNavigationMap(routeInput);
                Assert.True(map.TryLocate(new NavPoint(-40+10/Math.Sqrt(2),-32+10/Math.Sqrt(2)),radius,out var start));
                Assert.True(map.TryLocate(new NavPoint(p.RelayCaptureRadius-radius,0),radius,out var goal));
                using var query=native.Query(start,goal);for(int i=0;i<4096&&query.Status==RecastQueryStatus.Pending;i++)query.Step(128);
                TestContext.WriteLine("BASALT_NATIVE_ROUTE start="+start.SurfaceId+" goal="+goal.SurfaceId+" status="+query.Status+" work="+query.Iterations);
                foreach(var at in new[]{new NavPoint(-26,0),new NavPoint(-23,0),new NavPoint(-22,0),new NavPoint(-21,0),new NavPoint(-20,0),new NavPoint(-10,0),new NavPoint(0,0)}){
                    if(!map.TryLocate(at,radius,out var target)){TestContext.WriteLine("PROBE unsupported "+at.X);continue;}
                    using var q=native.Query(start,target);for(int j=0;j<4096&&q.Status==RecastQueryStatus.Pending;j++)q.Step(128);
                    TestContext.WriteLine("PROBE x="+at.X+" y="+map.SurfaceHeight(at)+" surface="+target.SurfaceId+" result="+q.Status+" work="+q.Iterations);
                }
                foreach(var variant in new[]{"solids","empty"}){
                    var walls=variant=="solids"?map.Solids.ToArray():Array.Empty<NavObstacle>();
                    using var variantMap=new RecastNavigationMap(RecastAuthorityGeometry.Prepare(map,new NavGeometry(map.HalfExtent,walls,map.Revision),nav));
                    using var q=variantMap.Query(start,goal);for(int j=0;j<4096&&q.Status==RecastQueryStatus.Pending;j++)q.Step(128);
                    TestContext.WriteLine("VARIANT "+variant+" result="+q.Status+" work="+q.Iterations);
                }
                var areaField=typeof(RecastAuthorityGeometry).GetField("Areas",BindingFlags.Instance|BindingFlags.NonPublic);var a=(byte[])areaField.GetValue(routeInput);var vv=(float[])f.GetValue(routeInput);
                for(int j=0;j<a.Length;j++)if(a[j]==1)TestContext.WriteLine("RAMP_TRI "+string.Join(",",Enumerable.Range(j*9,9).Select(k=>vv[k])));
                Assert.AreEqual(RecastQueryStatus.Full,query.Status,"Exact relay approach must connect to the start");
            }
            // Diagnostic only: the owning ordinary failures remain FAIL.
        }
    }
}
