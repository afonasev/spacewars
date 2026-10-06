using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class NavigationUnityTests
    {
        [UnityTest] public IEnumerator OrdinaryShkvalExactEndpointContinuationIsAdmissible()
        {
            var p=PlayableProfile.Create(
                UnityEngine.JsonUtility.FromJson<PlayableProfileData>(UnityEngine.Resources.Load<UnityEngine.TextAsset>("PlayableProfile").text),
                new ThreeCrossingsMap(UnityEngine.JsonUtility.FromJson<ThreeCrossingsProfileData>(UnityEngine.Resources.Load<UnityEngine.TextAsset>("ThreeCrossingsProfile").text)));
            var geometry=new NavGeometry(p.ArenaHalfExtent,PlayableMap.StaticObstacles(p),1);
            var start=new NavPoint(-15.138596267917878,-20.555440454253549);
            var goal=new NavPoint(-15.181308411214951,-20.416635160680535);
            var crowd=new NavCrowd(geometry,p.Navigation);
            crowd.Add(20,start,p.ShkvalCollisionRadius,p.ShkvalSpeed,p.ShkvalTurnSpeed);
            using(var router=new UnityNavigationRouter(geometry,p.Navigation))
            {
                var route=router.FindPath(start,goal);
                Assert.IsNotEmpty(route);
                Assert.AreEqual(goal,route[route.Length-1],"Provider returns the original double-precision endpoint");
                TestContext.WriteLine("ordinary Shkval requested goal="+goal.X+","+goal.Z+" native last="+route[route.Length-1].X+","+route[route.Length-1].Z);
                Assert.True(crowd.SetRoute(20,goal,route),"Exact typed request must retain its final endpoint contract");
            }
            yield return null;
        }

        [UnityTest] public IEnumerator OrdinaryProducedShkvalExactStartAndAllocatedGoalAreAdmissible()
        {
            var p=PlayableProfile.Create(
                UnityEngine.JsonUtility.FromJson<PlayableProfileData>(UnityEngine.Resources.Load<UnityEngine.TextAsset>("PlayableProfile").text),
                new ThreeCrossingsMap(UnityEngine.JsonUtility.FromJson<ThreeCrossingsProfileData>(UnityEngine.Resources.Load<UnityEngine.TextAsset>("ThreeCrossingsProfile").text)));
            var type=typeof(Spacewars.Runtime.PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var domain=Activator.CreateInstance(type,flags,null,new object[]{p,1L,false},null);
            object Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(domain,args);
            Call("AddCredits",PlayableOwner.Player,10000d);
            foreach(int slot in new[]{2,3}) Call("BuildAt",1,slot,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null);
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            var geometry=(NavGeometry)type.GetProperty("Geometry",flags).GetValue(domain);
            var start=new NavPoint(-49.215071865808035,-40.1358572624698);
            var target=new NavPoint(21.891588785046736,-27.947069943289222);
            var goals=NavArrivalAllocator.Allocate(geometry,p.Navigation,target,4);
            Assert.AreEqual(4,goals.Length);
            var goal=goals[3];
            var crowd=new NavCrowd(geometry,p.Navigation);
            crowd.Add(20,start,p.ShkvalCollisionRadius,p.ShkvalSpeed,p.ShkvalTurnSpeed);
            using(var router=new UnityNavigationRouter(geometry,p.Navigation))
            {
                var route=router.FindPath(start,goal);
                Assert.IsNotEmpty(route);
                Assert.AreEqual(goal,route[route.Length-1],"Provider returns the original double-precision endpoint");
                var previous=start;
                foreach(var corner in route){Assert.True(geometry.SegmentFree(previous,corner,p.ShkvalCollisionRadius));previous=corner;}
                TestContext.WriteLine("ordinary produced Shkval allocated goal="+goal.X+","+goal.Z+" native last="+previous.X+","+previous.Z);
                Assert.True(geometry.IsFree(goal,p.ShkvalCollisionRadius));
                Assert.True(geometry.SegmentFree(previous,goal,p.ShkvalCollisionRadius),"Native proposal can attach to the original exact goal with the full typed footprint");
                Assert.True(crowd.SetRoute(20,goal,route),"Native provider must preserve typed exact endpoints");
            }
            yield return null;
        }

        [UnityTest] public IEnumerator FullyBuiltTerritoryKeepsEveryFactoryExitConnectedToPassage()
        {
            var p=PlayableProfile.Default;var obstacles=new List<NavObstacle>(PlayableMap.StaticObstacles(p));
            foreach(var site in TerritoryRules.Sites(p))
            {
                double r=TerritoryRules.Radius(p,site.Kind);obstacles.Add(new NavObstacle(site.Position.X-r,site.Position.Z-r,site.Position.X+r,site.Position.Z+r));
                foreach(var slot in site.Slots){r=Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius);obstacles.Add(new NavObstacle(slot.Position.X-r,slot.Position.Z-r,slot.Position.X+r,slot.Position.Z+r));}
            }
            var geometry=new NavGeometry(p.ArenaHalfExtent,obstacles.ToArray(),1);
            using(var router=new UnityNavigationRouter(geometry,p.Navigation))
                foreach(var site in TerritoryRules.Sites(p))foreach(var slot in site.Slots)
                {
                    var exit=new NavPoint(slot.Position.X+Math.Cos(slot.Heading)*p.FactoryExitDistance,slot.Position.Z+Math.Sin(slot.Heading)*p.FactoryExitDistance);
                    Assert.Greater(router.FindPath(exit,new NavPoint(0,0)).Length,0,"Factory exit "+site.Id+":"+slot.Id);
                }
            yield return null;
        }
        [UnityTest] public IEnumerator StockNavMeshReturnsOnlyCompleteClearRoutesAndRebuilds()
        {
            var profile=new NavigationProfile();
            var geometry=new NavGeometry(50,new[]{new NavObstacle(-5,-20,5,8)},1);
            using(var router=new UnityNavigationRouter(geometry,profile)){
                var start=new NavPoint(-24,-3.6);var goal=new NavPoint(24,-3.6);var route=router.FindPath(start,goal);
                Assert.Greater(route.Length,0);var previous=start;
                foreach(var point in route){Assert.IsTrue(geometry.SegmentFree(previous,point,profile.Radius),"NavMesh supplied an unsafe segment");previous=point;}
                Assert.AreEqual(0,router.FindPath(start,new NavPoint(0,0)).Length);
            }
            yield return null;
            var blocked=new NavGeometry(50,new[]{new NavObstacle(-2,-50,2,50)},2);
            using(var router=new UnityNavigationRouter(blocked,profile))Assert.AreEqual(0,router.FindPath(new NavPoint(-20,0),new NavPoint(20,0)).Length,"Partial path must not be accepted");
            yield return null;
            using(var router=new UnityNavigationRouter(new NavGeometry(50,Array.Empty<NavObstacle>(),3),profile))Assert.Greater(router.FindPath(new NavPoint(-20,0),new NavPoint(20,0)).Length,0);
        }
    }
}
