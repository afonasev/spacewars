using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class ProductionClearanceProgressTests
    {
        [Test] public void ObservedMossStoppedLineClearsPaidExit()
        {
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new MossHighlandMap());var factory=new NavPoint(-35.99138953656284,-31.8181326440815);var map=p.AuthoredMap;
            var buildings=new[]{new NavObstacle(factory,p.FactoryFootprintRadius),new NavObstacle(new NavPoint(-38,-38),p.HeadquartersFootprintRadius),new NavObstacle(new NavPoint(-31.5,-38),p.RefineryFootprintRadius),new NavObstacle(new NavPoint(-43.25861046343716,-34.17939586009892),p.HangarFootprintRadius)};
            var geometry=new NavGeometry(p.ArenaHalfExtent,PlayableMap.StaticObstacles(p).Concat(buildings).ToArray(),1);var crowd=new NavCrowd(geometry,p.Navigation,map);
            var points=new[]{new NavPoint(-35.42362812315474,-35.66989551984456),new NavPoint(-34.777459694965415,-36.74142368967731),new NavPoint(-34.13129126677609,-37.812951859510065),new NavPoint(-33.48512283858676,-38.88448002934282)};
            for(int i=0;i<points.Length;i++){var u=crowd.Add(i+1,points[i],p.ExplorerCollisionRadius,p.ExplorerSpeed,p.ExplorerTurnSpeed);u.Heading=-.8687004623923437;crowd.Stop(i+1,false);}
            crowd.ProductionEscape=u=>{
                double z=Math.Max(factory.Z-p.FactoryExitDistance,Math.Min(factory.Z,u.Position.Z));double x=u.Position.X-factory.X,d=u.Position.Z-z,r=u.Radius+p.ExplorerCollisionRadius;
                return x*x+d*d<r*r?(NavPoint?)new NavPoint(u.Position.X-factory.X,u.Position.Z-factory.Z):null;
            };
            for(int tick=0;tick<150;tick++)crowd.Step(1d/30);
            TestContext.WriteLine("MOSS_PRODUCER_POS "+string.Join(";",crowd.Units.Select(u=>u.Id+":"+u.Position.X+","+u.Position.Z+","+u.Heading)));
            Assert.True(crowd.CanPlace(new NavPoint(factory.X,factory.Z-p.FactoryExitDistance),p.ExplorerCollisionRadius),"Exact observed stopped line must release the paid exit through normal swept motion");
        }
        [TestCase(false)][TestCase(true)] public void PaidExitClearsStoppedLineWithoutReplacingOrders(bool marchCapped)
        {
            var p=PlayableProfile.Default;var geometry=new NavGeometry(20,new[]{new NavObstacle(-1.45,-1.45,1.45,1.45)},1);var crowd=new NavCrowd(geometry,p.Navigation);
            var points=new[]{new NavPoint(0,-3.46666666666667),new NavPoint(.646168428189325,-4.53819483649942),new NavPoint(1.29233685637865,-5.60972300633218)};
            for(int i=0;i<points.Length;i++){var u=crowd.Add(i+1,points[i],p.ExplorerCollisionRadius,p.ExplorerSpeed,p.ExplorerTurnSpeed);u.Heading=-.8207963267948966;crowd.Stop(i+1,true);}
            var exit=new NavPoint(0,-p.FactoryExitDistance);
            crowd.ProductionEscape=u=>{
                double z=Math.Max(-p.FactoryExitDistance,Math.Min(0,u.Position.Z));double x=u.Position.X,d=u.Position.Z-z,r=u.Radius+p.ExplorerCollisionRadius;
                return x*x+d*d<r*r?(NavPoint?)u.Position:null;
            };
            for(int tick=0;tick<150;tick++){
                if(marchCapped)foreach(var u in crowd.Units)crowd.PreferMaxSpeed(u.Id,0);
                crowd.Step(1d/30);
                foreach(var a in crowd.Units)foreach(var b in crowd.Units.Where(b=>b.Id>a.Id))Assert.GreaterOrEqual(Math.Sqrt(Math.Pow(a.Position.X-b.Position.X,2)+Math.Pow(a.Position.Z-b.Position.Z,2)),a.Radius+b.Radius-1e-7);
            }
            TestContext.WriteLine("PRODUCER_POS "+string.Join(";",crowd.Units.Select(u=>u.Id+":"+u.Position.X+","+u.Position.Z+","+u.Heading)));
            Assert.True(crowd.CanPlace(exit,p.ExplorerCollisionRadius),"A paid exit must clear physically despite stopped/held line and march preferences");
            Assert.True(crowd.Units.All(u=>u.Held));
        }
    }
}
