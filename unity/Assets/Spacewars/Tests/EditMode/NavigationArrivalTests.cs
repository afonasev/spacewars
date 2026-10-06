using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationArrivalTests
    {
        [Test] public void LateTankReachesInnerSlotAfterOthersPark()
        {
            var profile=new NavigationProfile();var geometry=new NavGeometry(50,new NavObstacle[0],1);var crowd=new NavCrowd(geometry,profile);
            var slots=NavArrivalAllocator.Allocate(geometry,profile,new NavPoint(24,0),12);Assert.AreEqual(12,slots.Length);
            crowd.Add(0,new NavPoint(-24,0));for(int i=1;i<12;i++)crowd.Add(i,slots[i]);
            Assert.IsTrue(crowd.SetRoute(0,slots[0],new SharedFlowRouter(geometry,profile).FindPath(crowd.Units[0].Position,slots[0])));
            for(int t=0;t<2700&&crowd.Units[0].Moving;t++)crowd.Step(1d/30);
            Assert.AreEqual(NavigationOutcome.Arrived,crowd.Units[0].Outcome);
            double dx=crowd.Units[0].Position.X-slots[0].X,dz=crowd.Units[0].Position.Z-slots[0].Z;
            Assert.LessOrEqual(dx*dx+dz*dz,profile.ArrivalTolerance*profile.ArrivalTolerance);
            for(int i=1;i<12;i++){Assert.AreEqual(slots[i].X,crowd.Units[i].Position.X);Assert.AreEqual(slots[i].Z,crowd.Units[i].Position.Z);}
        }
        [Test] public void EnclosedDenseSlotEndsBlockedWithoutFalseArrival()
        {
            var profile=new NavigationProfile();var geometry=new NavGeometry(50,new NavObstacle[0],1);var crowd=new NavCrowd(geometry,profile);
            crowd.Add(0,new NavPoint(-24,0));int id=1;
            for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)if(x!=0||z!=0)crowd.Add(id++,new NavPoint(24+x*1.8,z*1.8));
            crowd.SetRoute(0,new NavPoint(24,0),new[]{new NavPoint(24,0)});
            for(int tick=0;tick<2700&&crowd.Units[0].Moving;tick++)crowd.Step(1d/30);
            Assert.AreEqual(NavigationOutcome.Blocked,crowd.Units[0].Outcome);Assert.IsFalse(crowd.Units[0].Moving);
            Assert.Greater(24-crowd.Units[0].Position.X,profile.Radius);
        }
    }
}
