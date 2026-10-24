using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationRectangleRangeProofTests
    {
        [Test] public void ActualCampaignRangesLeaveEveryFeasibleOriginAndReduceClassificationDomain()
        {
            var map=new CampaignOpeningMap();var p=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.Solids.ToArray(),map.Revision),null);
            var input=new NavExclusionInput(p.ExclusionObstacles,p.Binding,p.Binding,.8,-300,300,-300,300,361201,1048576,300000,1200000,1048576);
            var excluded=new bool[361201];long operations=0;
            for(int n=0;n<input.Obstacles.Count;n++){
                var cursor=NavigationRectangleRangeProof.StartRectangle(input,n);
                while(NavigationRectangleRangeProof.Progress(cursor)==NavRectangleProgress.Pending){
                    var before=cursor;Assert.AreEqual(NavRectangleProgress.Pending,NavigationRectangleRangeProof.StepRectangle(input,ref cursor,0,out int zero,out _));Assert.Zero(zero);Assert.AreEqual(before,cursor);
                    Assert.True(NavigationRectangleRangeProof.ValidateRectangle(input,cursor));
                    NavigationRectangleRangeProof.StepRectangle(input,ref cursor,1,out int used,out _);Assert.AreEqual(1,used);operations+=used;Assert.Less(operations,10000);
                }
                Assert.True(NavigationRectangleRangeProof.ValidateRectangle(input,cursor));
                if(NavigationRectangleRangeProof.Progress(cursor)!=NavRectangleProgress.Ready)continue;
                var range=cursor.Work.TerminalCertificate;
                for(long x=range.MinXIndex;x<=range.MaxXIndex;x++)for(long z=range.MinZIndex;z<=range.MaxZIndex;z++)excluded[(x+300)*601+z+300]=true;
            }
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);int remaining=0,feasible=0;
            for(int id=0;id<excluded.Length;id++){
                if(!excluded[id])remaining++;
                if(p.TryLocate(new NavPoint((-300+id/601)*.8,(-300+id%601)*.8),clearance,out _)){feasible++;Assert.False(excluded[id],"No feasible origin may be skipped: "+id);}
            }
            Assert.Less(remaining,50000,"Diagnostic scale check; caps/quotas/map unchanged");Assert.Greater(feasible,0);
            TestContext.WriteLine("Campaign descriptors="+input.Obstacles.Count+" paid bound operations="+operations+" remaining classification domain="+remaining+" feasible centers="+feasible);
        }
        [Test] public void SingleDescriptorNeverConsumesFollowingRectangleOrUsesCapacityUnion()
        {
            var polygon=new NavObstacle(new NavPolygon(new[]{new NavPoint(-5,-5),new NavPoint(5,-5),new NavPoint(5,5),new NavPoint(-5,5)}));
            var obstacles=new[]{polygon,new NavObstacle(new NavPoint(0,0),8),new NavObstacle(-1,-1,1,1),new NavObstacle(.1,0,.2,2)};
            var input=new NavExclusionInput(obstacles,"provider","geometry",1,-2,2,-2,2,25,25,0,0,0);
            for(int n=0;n<obstacles.Length;n++){
                var cursor=NavigationRectangleRangeProof.StartRectangle(input,n);int total=0;
                while(NavigationRectangleRangeProof.Progress(cursor)==NavRectangleProgress.Pending){NavigationRectangleRangeProof.StepRectangle(input,ref cursor,100,out int used,out _);Assert.AreEqual(1,used,"One paid operation despite larger allowance");total+=used;Assert.Less(total,100);}
                Assert.True(NavigationRectangleRangeProof.ValidateRectangle(input,cursor));
                Assert.AreEqual(n<2?NavRectangleProgress.Skipped:NavRectangleProgress.Ready,NavigationRectangleRangeProof.Progress(cursor));
                if(n>=2){Assert.AreEqual(n,cursor.Work.TerminalCertificate.ObstacleIndex);Assert.AreEqual(n==2?9:0,cursor.Work.TerminalCertificate.ExcludedCount);}
            }
        }
    }
}
