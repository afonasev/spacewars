using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class FoundryBallisticSamplingTests
    {
        // Frozen f079a0bd sampling reference. Keep original enumeration, sample
        // coordinates, order and contact arithmetic independent of optimized Cover.
        private static class Reference
        {
            private const double Eps=1e-5;
        private static bool Overlap(NavObstacle s,BallisticPoint p,double radius)
        {return p.X+radius>=s.MinX&&p.X-radius<=s.MaxX&&p.Z+radius>=s.MinZ&&p.Z-radius<=s.MaxZ&&s.DistanceSquared(p.Ground)<=radius*radius;}
        private static int Cover(BallisticPoint p,double radius,IReadOnlyList<NavObstacle> solids,double wallHeight,IReadOnlyList<BallisticBody> bodies,int ignored)
        {
            foreach(var s in solids)if(p.Y+radius>=s.Bottom&&p.Y-radius<=(double.IsNaN(s.Top)?wallHeight:s.Top)&&Overlap(s,p,radius))return -1;
            foreach(var b in bodies)if(b.Building&&b.Id!=ignored&&p.Y+radius>=b.BaseHeight&&p.Y-radius<=b.BaseHeight+b.Height)
            {double x=p.X-b.Position.X,z=p.Z-b.Position.Z,r=b.Radius+radius;if(x*x+z*z<=r*r)return b.Id;}
            return 0;
        }
        public static BallisticContact? FirstContact(BallisticFlight f,double radius,IReadOnlyList<NavObstacle> solids,double wallHeight,IReadOnlyList<BallisticBody> bodies,int source,double from=0,double to=1,Func<NavPoint,double> terrain=null)
        {
            var middle=new BallisticPoint((f.Start.X+f.End.X)/2,0,(f.Start.Z+f.End.Z)/2);
            double reach=Math.Sqrt((f.End.X-f.Start.X)*(f.End.X-f.Start.X)+(f.End.Z-f.Start.Z)*(f.End.Z-f.Start.Z))/2+radius;
            solids=solids.Where(s=>Overlap(s,middle,reach)).ToArray();
            bodies=bodies.Where(b=>b.Building&&b.Id!=source&&Math.Sqrt((b.Position.X-middle.X)*(b.Position.X-middle.X)+(b.Position.Z-middle.Z)*(b.Position.Z-middle.Z))<=reach+b.Radius).ToArray();
            if(solids.Count==0&&bodies.Count==0&&terrain==null)
            {
                BallisticContact? first=null;double a=-4*f.ArcHeight,b=f.End.Y-f.Start.Y+4*f.ArcHeight;
                foreach(double offset in new[]{-radius,radius})
                {
                    double c=f.Start.Y+offset,disc=b*b-4*a*c;
                    var roots=Math.Abs(a)<Eps?(Math.Abs(b)<Eps?Array.Empty<double>():new[]{-c/b}):disc<0?Array.Empty<double>():new[]{(-b+Math.Sqrt(disc))/(2*a),(-b-Math.Sqrt(disc))/(2*a)};
                    foreach(double u in roots){double derivative=b+2*a*u;if(u<=from+Eps||u>to+Eps||(offset<0&&derivative>=0)||(offset>0&&derivative<=0))continue;var point=f.Point(u);if(first==null||u<first.Value.Progress)first=new BallisticContact(u,new BallisticPoint(point.X,0,point.Z));}
                }
                return first??(to>=1-Eps?new BallisticContact(1,f.End):(BallisticContact?)null);
            }
            double dx=f.End.X-f.Start.X,dz=f.End.Z-f.Start.Z,dy=Math.Abs(f.End.Y-f.Start.Y)+4*f.ArcHeight;
            double bound=Math.Sqrt(dx*dx+dz*dz+dy*dy);int steps=Math.Max(1,(int)Math.Ceiling((to-from)*bound/Math.Min(.025,Math.Max(.005,radius/2))));
            var prev=f.Point(from);
            for(int i=1;i<=steps;i++)
            {
                double u=from+(to-from)*i/steps;var p=f.Point(u);int hit=Cover(p,radius,solids,wallHeight,bodies,source);
                if(hit!=0)return new BallisticContact(u,p,Math.Max(0,hit));
                double ground=terrain?.Invoke(p.Ground)??0,previousGround=terrain?.Invoke(prev.Ground)??0;
                if((prev.Y-radius>previousGround+Eps&&p.Y-radius<=ground+Eps)||(prev.Y+radius<previousGround-Eps&&p.Y+radius>=ground-Eps))return new BallisticContact(u,new BallisticPoint(p.X,ground,p.Z));
                prev=p;
            }
            return to>=1-Eps?new BallisticContact(1,f.End):(BallisticContact?)null;
        }

        }
        private static FoundryMap Map()=>new FoundryMap(new FoundryProfileData());
        private static double LegacyHeight(FoundryMap map,NavPoint point)=>map.Supports.FirstOrDefault(s=>s.Contains(point))?.HeightAt(point)??0;
        [Test] public void FoundrySupportPrecedenceIsExactAtAllBoundariesAndRandomPoints()
        {
            var map=Map();var random=new Random(1063723763);var points=new List<NavPoint>();
            foreach(var support in map.Supports){var b=support.Bounds;
                foreach(double x in new[]{b.MinX,b.MaxX,(b.MinX+b.MaxX)/2})foreach(double z in new[]{b.MinZ,b.MaxZ,(b.MinZ+b.MaxZ)/2})
                    foreach(double delta in new[]{-1e-10,0,1e-10})points.Add(new NavPoint(x+delta,z-delta));
                if(b.Polygon!=null)points.AddRange(b.Polygon.Vertices);
            }
            for(int i=0;i<10000;i++)points.Add(new NavPoint(random.NextDouble()*300-150,random.NextDouble()*300-150));
            foreach(var point in points){Assert.AreSame(map.Supports.FirstOrDefault(s=>s.Contains(point)),map.SupportAt(point));Assert.AreEqual(LegacyHeight(map,point),map.SurfaceHeight(point));}
        }
        [TestCase(.0)][TestCase(.09)][TestCase(.3)]
        public void ExactContactSamplesPreserveCoverOrderAndPartialFlight(double radius)
        {
            var map=Map();var random=new Random(1063723763);
            var bodies=new[]{new BallisticBody(10,PlayableOwner.Player,new NavPoint(0,65),default,2,8,100,true,3),
                new BallisticBody(2,PlayableOwner.Enemy,new NavPoint(0,65),default,2,8,100,true,3),
                new BallisticBody(3,PlayableOwner.Enemy,new NavPoint(0,70),default,2,12,100,false,3)};
            Func<NavPoint,double> oldTerrain=p=>LegacyHeight(map,p);
            for(int i=0;i<80;i++){
                var a=i<4?new NavPoint(0,60):new NavPoint(random.NextDouble()*180-90,random.NextDouble()*260-130);
                var b=i<4?new NavPoint(0,70):new NavPoint(a.X+random.NextDouble()*40-20,a.Z+random.NextDouble()*40-20);
                var flight=new BallisticFlight(new BallisticPoint(a.X,map.SurfaceHeight(a)+1.15,a.Z),new BallisticPoint(b.X,map.SurfaceHeight(b),b.Z),4,12);
                foreach(var interval in new[]{new[]{0d,1d},new[]{0d,.3},new[]{.3,.8},new[]{.8,1d}})
                    foreach(int ignored in new[]{0,10}){
                        var expected=Reference.FirstContact(flight,radius,map.Solids,2,bodies,ignored,interval[0],interval[1],oldTerrain);
                        var actual=PlayableBallistics.FirstContact(flight,radius,map.Solids,2,bodies,ignored,interval[0],interval[1],map.SurfaceHeight);
                        Assert.AreEqual(expected,actual,"ray="+i+" source="+ignored);
                    }
            }
        }
        [Test] public void GroundFireReuseIsExactBoundedAndProfileScopedWithoutWireState()
        {
            var map=Map();var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var config=map.Configuration(profile,1063723763);
            var domain=new PlayableDomain(profile,1,config);PlayableDomain restored=null;
            var method=typeof(PlayableDomain).GetMethod("GroundFireClear",BindingFlags.Instance|BindingFlags.NonPublic);
            var query=(Func<NavPoint,NavPoint,double,bool>)Delegate.CreateDelegate(typeof(Func<NavPoint,NavPoint,double,bool>),domain,method);
            var cache=(PlayableGroundFireCache)typeof(PlayableDomain).GetProperty("GroundFireCache",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(domain);
            try{
                var bytes=domain.CaptureWorldBytes(config.Seed,config.SourceIdentity);var random=new Random(22);int positive=0,negative=0;
                for(int i=0;i<1600;i++){
                    var a=new NavPoint(random.NextDouble()*160-80,random.NextDouble()*240-120);var b=new NavPoint(a.X+random.NextDouble()*30-15,a.Z+random.NextDouble()*30-15);double radius=i%2==0?0:profile.TankProjectileCollisionRadius;
                    bool expected=PlayableBallistics.TerrainLineClear(new BallisticPoint(a.X,map.SurfaceHeight(a)+map.DirectFireHeight,a.Z),new BallisticPoint(b.X,map.SurfaceHeight(b)+map.DirectFireHeight,b.Z),radius,map.Solids,profile.BallisticWallHeight,map.SurfaceHeight);
                    if(expected)positive++;else negative++;
                    Assert.AreEqual(expected,query(a,b,radius));Assert.True(cache.TryGet(profile,a,b,radius,out bool stored));Assert.AreEqual(expected,stored);Assert.AreEqual(expected,query(a,b,radius));
                }
                Assert.Greater(positive,0);Assert.Greater(negative,0);
                var origin=new NavPoint(0,0);var goal=new NavPoint(0,5);query(origin,goal,0);
                Assert.False(cache.TryGet(profile,new NavPoint(0,1e-12),goal,0,out _));Assert.False(cache.TryGet(profile,goal,origin,0,out _));Assert.False(cache.TryGet(profile,origin,goal,.01,out _));
                var changed=PlayableProfile.Create(profile.CopyData(),new FoundryMap(new FoundryProfileData{rearHeight=2}));
                Assert.False(cache.TryGet(changed,origin,goal,0,out _),"Same revision/different immutable profile cannot reuse old geometry");
                CollectionAssert.AreEqual(bytes,domain.CaptureWorldBytes(config.Seed,config.SourceIdentity));
                restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,config.Seed,config.SourceIdentity,config);
                var cold=(Func<NavPoint,NavPoint,double,bool>)Delegate.CreateDelegate(typeof(Func<NavPoint,NavPoint,double,bool>),restored,method);
                Assert.AreEqual(query(origin,goal,0),cold(origin,goal,0));CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(config.Seed,config.SourceIdentity));
            }finally{foreach(var d in new[]{domain,restored})if(d!=null){d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();}}
        }
        [Test] public void PublicObjectiveBatchPreservesIndividualCurrentRouteProofs()
        {
            var map=Map();var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var config=map.Configuration(profile,1063723763);
            var domain=new PlayableDomain(profile,1,config);
            try{
                var metrics=new PlayableRuntimeMetrics(0,0,0,0,0);
                var view=domain.PlayerSnapshot(1,RuntimeStatus.Running,false,metrics,null,config.Seed);
                var actor=view.Entities.Single(e=>e.Owner==PlayableOwner.Player);
                var requests=view.PublicScoutObjectives.Select(o=>new PlayableRouteRequest(actor.Id,PlayableRouteTargetKind.PublicObjective,o.SiteId,1,0,actor.Position,PlayableUnitRules.Radius(profile,actor.Kind),o.Approach)).ToArray();
                Assert.Greater(requests.Length,1);
                var expected=new List<PlayableRouteProof>();
                foreach(var request in requests)expected.AddRange(domain.PlayerSnapshotWithRoutes(1,RuntimeStatus.Running,false,metrics,null,config.Seed,PlayableOwner.Player,new[]{request}).RouteProofs);
                var actual=domain.PlayerSnapshotWithRoutes(1,RuntimeStatus.Running,false,metrics,null,config.Seed,PlayableOwner.Player,requests);
                Assert.Greater(expected.Count,0,"Must exercise actual visible public-objective proof generation");
                Assert.AreEqual(PlayableAiCanonical.Encode(expected.ToArray()),PlayableAiCanonical.Encode(actual.RouteProofs.ToArray()));
                var repeated=domain.PlayerSnapshotWithRoutes(1,RuntimeStatus.Running,false,metrics,null,config.Seed,PlayableOwner.Player,requests.Concat(requests).ToArray());
                Assert.AreEqual(PlayableAiCanonical.Encode(expected.Concat(expected).ToArray()),PlayableAiCanonical.Encode(repeated.RouteProofs.ToArray()));
            }finally{domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}
        }
        [Test] public void RecordSameInputSamplingCost()
        {
            var map=Map();Func<NavPoint,double> legacy=p=>LegacyHeight(map,p);
            // Local geometric workload, not a replay or whole-match performance gate.
            var flights=Enumerable.Range(0,24).Select(i=>new BallisticFlight(new BallisticPoint(-8+i*.7,4.15,92),new BallisticPoint(-8+i*.7,3,68),4,12)).ToArray();
            var bodies=new[]{new BallisticBody(1,PlayableOwner.Player,new NavPoint(25,80),default,2,4,100,true,3)};
            void Run(bool old){foreach(var flight in flights){var a=old?Reference.FirstContact(flight,.09,map.Solids,2,bodies,0,terrain:legacy):PlayableBallistics.FirstContact(flight,.09,map.Solids,2,bodies,0,terrain:map.SurfaceHeight);Assert.NotNull(a);}}
            Run(true);Run(false);var before=new List<double>();var after=new List<double>();
            for(int round=0;round<6;round++)foreach(bool old in round%2==0?new[]{true,false}:new[]{false,true}){var watch=Stopwatch.StartNew();Run(old);watch.Stop();(old?before:after).Add(watch.Elapsed.TotalMilliseconds);}
            before.Sort();after.Sort();TestContext.WriteLine("SAMPLING same24flights reference_median_ms="+before[3]+" current_median_ms="+after[3]+"; fixed predicates; not whole-match qualification");
        }
    }
}
