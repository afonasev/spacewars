using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class CampaignHorizontalMapTests
    {
        [Test] public void ApprovedBSpawnsBranchesAndClosedGateUseActualPolygonCollision()
        {
            var map=new CampaignOpeningMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var c=CampaignReconOpening.Configuration(p);
            Assert.AreEqual(18,c.ScenarioUnits.Count);Assert.AreEqual(7,c.Sites.Count);Assert.AreEqual(9,c.ScenarioBuildings.Count);
            Assert.True(map.Solids.All(s=>s.Polygon!=null),"visible and physical canyon use authored polygons");
            foreach(var u in c.ScenarioUnits)Assert.True(map.SupportsFootprint(u.Position,PlayableUnitRules.Radius(p,u.Kind)),"spawn "+u.Position.X+","+u.Position.Z);
            foreach(var site in c.Sites)Assert.True(map.SupportsFootprint(site.Position,0),"site "+site.Id);
            var closed=new NavGeometry(map.HalfExtent,map.Solids.Concat(new[]{CampaignReconOpening.Gate}).ToArray(),map.Revision);
            var open=new NavGeometry(map.HalfExtent,map.Solids.ToArray(),map.Revision);
            // A small clearance-aware flood is only a topology smoke, not a gameplay
            // arrival/performance claim. Sweeps forbid diagonal corner cutting.
            HashSet<int> Reach(NavGeometry geometry)
            {
                const int size=121;const double step=4;var seen=new HashSet<int>();var q=new Queue<int>();
                int X(double x)=>(int)Math.Round((x+240)/step);NavPoint Point(int id)=>new NavPoint(id%size*step-240,id/size*step-240);
                int start=X(CampaignReconOpening.Landing.X)+size*X(CampaignReconOpening.Landing.Z);seen.Add(start);q.Enqueue(start);
                while(q.Count>0){int a=q.Dequeue();for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++){
                    int x=a%size+dx,z=a/size+dz;if(x<0||x>=size||z<0||z>=size||dx==0&&dz==0)continue;int b=x+size*z;
                    if(!seen.Contains(b)&&geometry.SegmentFree(Point(a),Point(b),p.ExplorerCollisionRadius)){seen.Add(b);q.Enqueue(b);}
                }}return seen;
            }
            bool Near(HashSet<int> reached,NavPoint target)=>reached.Any(id=>Math.Abs(id%121*4-240-target.X)<=4&&Math.Abs(id/121*4-240-target.Z)<=4);
            var before=Reach(closed);var after=Reach(open);
            foreach(var point in new[]{CampaignReconOpening.Rendezvous,CampaignReconOpening.Explore,CampaignReconOpening.Fork,CampaignReconOpening.West,CampaignReconOpening.East,CampaignReconOpening.Convergence})Assert.True(Near(before,point),"pre-gate connected "+point.X+","+point.Z);
            Assert.False(Near(before,CampaignReconOpening.Final),"closed gate has no bypass");Assert.True(Near(after,CampaignReconOpening.Final),"final connected after opening");
            Assert.Greater(CampaignReconOpening.Final.X-CampaignReconOpening.Landing.X,400);
        }
    }
}
