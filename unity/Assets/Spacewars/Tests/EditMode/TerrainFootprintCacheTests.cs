using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class TerrainFootprintCacheTests
    {
        [Test] public void OwnedGeometryIndexPreservesExactBlockerOrderAndColdCharge()
        {
            var obstacles=new System.Collections.Generic.List<NavObstacle>();
            // Reverse location order makes a bucket-order first blocker incorrect.
            for(int x=3;x>=-3;x--)for(int z=3;z>=-3;z--){
                double a=x*3,b=z*3;
                if((x-z)%3==0)obstacles.Add(new NavObstacle(new NavPoint(a,b),.4,(x-z)&1));
                else if((x-z)%3==1)obstacles.Add(new NavObstacle(new NavPolygon(new[]{new NavPoint(a-.4,b-.4),new NavPoint(a+.4,b-.4),new NavPoint(a,b+.4)})));
                else obstacles.Add(new NavObstacle(a-.4,b-.4,a+.4,b+.4));
            }
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,982,1,out var scope));
            var hot=NavGeometry.CopyOwned(new NavGeometry(32,obstacles.ToArray(),1),scope.OwnedReservation());var root=hot.StorageRoot;scope.Attach(root);scope.Commit(root,_=>{});
            byte[] Capture(NavigationRetentionBudget ledger,NavOwnedStorage value){
                Assert.True(ledger.TryOpenScope(NavigationPreparationKind.Capture,982,2,out var save));
                using(save)using(var scratch=NavStorageScratch.Reserve(save.OwnedReservation(),2*1048576))using(var stream=new System.IO.MemoryStream(1048576)){
                    using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))NavPhysicalBodyWire.WriteTo(writer,value,save.OwnedReservation());return stream.ToArray();
                }
            }
            var bytes=Capture(budget,root);var coldBudget=new NavigationRetentionBudget();Assert.True(coldBudget.TryOpenScope(NavigationPreparationKind.Reconstruction,982,1,out var restore));NavOwnedStorage coldRoot=null;
            try{
                using(var stream=new System.IO.MemoryStream(bytes,false))using(var reader=new System.IO.BinaryReader(stream))coldRoot=NavPhysicalBodyWire.ReadFrom(reader,restore.OwnedReservation());
                restore.Attach(coldRoot);restore.Commit(coldRoot,_=>{});var cold=((NavGeometry.OwnedBuffers)coldRoot).Facade;
                Assert.AreEqual(budget.PhysicalBytes,coldBudget.PhysicalBytes);Assert.Greater(hot.SpatialIndexRetainedBytes,0);Assert.AreEqual(hot.SpatialIndexRetainedBytes,cold.SpatialIndexRetainedBytes);
                int accepted=0,blocked=0;long retained=coldBudget.PhysicalBytes;
                foreach(var geometry in new[]{hot,cold}){
                    var random=new Random(7111);
                    void Observe(NavPoint a,NavPoint b,double radius,int layer){
                        NavGeometry.DisableOwnedSpatialForTests=true;bool free=geometry.IsFree(a,radius,layer),sweep=geometry.SegmentFree(a,b,radius,out int blocker,layer);
                        NavGeometry.DisableOwnedSpatialForTests=false;Assert.AreEqual(free,geometry.IsFree(a,radius,layer));Assert.AreEqual(sweep,geometry.SegmentFree(a,b,radius,out int indexedBlocker,layer));Assert.AreEqual(blocker,indexedBlocker);
                        if(sweep)accepted++;else blocked++;
                    }
                    for(int n=0;n<4000;n++){
                        var a=new NavPoint(random.NextDouble()*32-16,random.NextDouble()*32-16);var b=n%2==0?new NavPoint(a.X+1.3,a.Z-.8):new NavPoint(random.NextDouble()*32-16,random.NextDouble()*32-16);
                        Observe(a,b,(n%4)*.2,(n%3)-1);
                    }
                    foreach(var obstacle in obstacles)foreach(double radius in new[]{0,.2,.58}){
                        Observe(new NavPoint(obstacle.MinX-radius,obstacle.MinZ),new NavPoint(obstacle.MinX-radius,obstacle.MaxZ),radius,-1);
                        Observe(new NavPoint(obstacle.MinX-1,obstacle.MinZ-1),new NavPoint(obstacle.MaxX+1,obstacle.MaxZ+1),radius,-1);
                    }
                }
                Assert.Greater(accepted,0);Assert.Greater(blocked,0);Assert.AreEqual(retained,coldBudget.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(coldBudget,coldRoot));
            }finally{NavGeometry.DisableOwnedSpatialForTests=false;root.ReleaseOwner();coldRoot?.ReleaseOwner();restore.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(coldBudget.PhysicalBytes);}
        }
        [TestCase(false)][TestCase(true)] public void OwnedQueryIndexesKeepTraceBitsAndColdLayoutWithoutRetainingQueryState(bool crimson)
        {
            IPlayableTerrain map=crimson?(IPlayableTerrain)new CrimsonGroveMap():ThreeCrossingsMap.Default;var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,981,1,out var scope));
            var provider=PartitionNavigationProvider.CopyOwned(new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,System.Linq.Enumerable.ToArray(map.MovementBlockers),1),null),scope.OwnedReservation());
            var root=provider.StorageRoot;scope.Attach(root);scope.Commit(root,_=>{});
            byte[] Capture(NavigationRetentionBudget ledger,NavOwnedStorage value){
                Assert.True(ledger.TryOpenScope(NavigationPreparationKind.Capture,981,2,out var save));
                using(save)using(var scratch=NavStorageScratch.Reserve(save.OwnedReservation(),2*1048576))using(var stream=new System.IO.MemoryStream(1048576)){
                    using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))NavPhysicalBodyWire.WriteTo(writer,value,save.OwnedReservation());return stream.ToArray();
                }
            }
            var bytes=Capture(budget,root);var coldBudget=new NavigationRetentionBudget();Assert.True(coldBudget.TryOpenScope(NavigationPreparationKind.Reconstruction,981,1,out var restore));NavOwnedStorage coldRoot=null;
            try{
                using(var stream=new System.IO.MemoryStream(bytes,false))using(var reader=new System.IO.BinaryReader(stream))coldRoot=NavPhysicalBodyWire.ReadFrom(reader,restore.OwnedReservation());
                restore.Attach(coldRoot);restore.Commit(coldRoot,_=>{});
                var cold=(PartitionNavigationProvider)((NavDeclarationStorage)coldRoot).Facade;
                Assert.AreEqual(provider.EstimatedRetainedBytes,cold.EstimatedRetainedBytes);Assert.AreEqual(budget.PhysicalBytes,coldBudget.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(coldBudget,coldRoot));
                foreach(var candidate in new[]{provider,cold}){
                    var locations=(TerrainLocations)typeof(PartitionNavigationProvider).GetField("locations",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(candidate);
                    Assert.True(locations.HasOwnedQueries);Assert.Greater(locations.OwnedQueryRetainedBytes,0);
                    Assert.LessOrEqual(candidate.Seams.Count,4096,"exercise indexed incident path");
                    var incident=typeof(TerrainLocations).GetMethod("Incident",BindingFlags.Instance|BindingFlags.NonPublic);
                    var points=new System.Collections.Generic.List<NavPoint>{new NavPoint(double.NaN,0),new NavPoint(double.PositiveInfinity,0),new NavPoint(0,double.NegativeInfinity)};
                    foreach(var seam in candidate.Seams)foreach(var point in new[]{seam.From,seam.To,new NavPoint((seam.From.X+seam.To.X)/2,(seam.From.Z+seam.To.Z)/2)})
                        foreach(double offset in new[]{0d,-1e-7,1e-7,-1.01e-7,1.01e-7}){points.Add(new NavPoint(point.X+offset,point.Z));points.Add(new NavPoint(point.X,point.Z+offset));}
                    long scanChecks=0,indexChecks=0;
                    foreach(var point in points){
                        TerrainLocations.DisableOwnedIncidentForTests=true;TerrainLocations.IncidentCandidatesForTests=0;var expected=(ICollection)incident.Invoke(locations,new object[]{point});scanChecks+=TerrainLocations.IncidentCandidatesForTests;
                        TerrainLocations.DisableOwnedIncidentForTests=false;TerrainLocations.IncidentCandidatesForTests=0;var actual=(ICollection)incident.Invoke(locations,new object[]{point});indexChecks+=TerrainLocations.IncidentCandidatesForTests;
                        CollectionAssert.AreEqual(expected,actual,"exact seam identities and source order at "+point);
                    }
                    Assert.Less(indexChecks,scanChecks,"the existing paid grid reduces seam predicate work");
                    TestContext.WriteLine("OWNED_INCIDENT crimson="+crimson+" seams="+candidate.Seams.Count+" points="+points.Count+" scan_checks="+scanChecks+" indexed_checks="+indexChecks);
                    long retained=coldBudget.PhysicalBytes;var random=new Random(7110);var clearance=new NavClearanceProfile("index-proof",1,"ground",0,.58);
                    string Observe(NavPoint a,NavPoint b){bool located=candidate.TryLocate(a,clearance,out var from);bool ready=located&&candidate.TryTrace(from,b,clearance,out _,out _);candidate.TryLocate(b,clearance,out var to);return located+"/"+ready+"/"+PlayableWorldRestoreTests.Facts(from)+"/"+PlayableWorldRestoreTests.Facts(to)+"/"+(located&&candidate.TryTrace(from,b,clearance,out var reached,out var trace)?PlayableWorldRestoreTests.Facts(new{reached,trace}):"refused");}
                    int different=0;double oldMs=0,newMs=0;
                    for(int i=0;i<320;i++){
                        var a=new NavPoint(random.NextDouble()*128-64,random.NextDouble()*128-64);var b=i%2==0?new NavPoint(a.X+.8,a.Z-.8):new NavPoint(random.NextDouble()*128-64,random.NextDouble()*128-64);
                        TerrainLocations.DisableOwnedQueriesForTests=true;var timer=System.Diagnostics.Stopwatch.StartNew();string before=Observe(a,b);oldMs+=timer.Elapsed.TotalMilliseconds;
                        TerrainLocations.DisableOwnedQueriesForTests=false;timer.Restart();string after=Observe(a,b);newMs+=timer.Elapsed.TotalMilliseconds;
                        Assert.AreEqual(before,after,"pair "+i);if(before.Contains("True"))different++;
                    }
                    Assert.Greater(different,0);Assert.AreEqual(retained,coldBudget.PhysicalBytes,"queries retain no additional objects");
                    TestContext.WriteLine("OWNED_GRID before_ms="+oldMs+" indexed_ms="+newMs+" retained="+locations.OwnedQueryRetainedBytes);
                }
                CollectionAssert.AreEqual(bytes,Capture(coldBudget,coldRoot));
            }finally{TerrainLocations.DisableOwnedQueriesForTests=false;TerrainLocations.DisableOwnedIncidentForTests=false;TerrainLocations.IncidentCandidatesForTests=0;root.ReleaseOwner();coldRoot?.ReleaseOwner();restore.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(coldBudget.PhysicalBytes);}
        }
        [Test] public void OwnedColdPartitionPruningKeepsOriginalFootprintsSweepsAndOrderedTraceBits()
        {
            var map=new ThreeCrossingsMap(new ThreeCrossingsProfileData());
            var geometry=new NavGeometry(map.HalfExtent,System.Linq.Enumerable.ToArray(map.MovementBlockers),1);
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,981,1,out var scope));
            using(scope){var r=scope.OwnedReservation();var provider=PartitionNavigationProvider.CopyOwned(new PartitionNavigationProvider(map,geometry,null),r);
                try{
                    var locations=(TerrainLocations)typeof(PartitionNavigationProvider).GetField("locations",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(provider);
                    Assert.IsNull(typeof(TerrainLocations).GetField("boundaryIndex",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(locations),"exercise actual owned/cold BODY representation");
                    var pairs=new System.Collections.Generic.List<Tuple<NavPoint,NavPoint>>();var random=new Random(7108);
                    for(int i=0;i<240;i++){var a=new NavPoint(random.NextDouble()*128-64,random.NextDouble()*128-64);pairs.Add(Tuple.Create(a,i%2==0?new NavPoint(a.X+.8,a.Z-.8):new NavPoint(random.NextDouble()*128-64,random.NextDouble()*128-64)));}
                    foreach(var seam in provider.Seams){var a=seam.From;var b=seam.To;pairs.Add(Tuple.Create(a,b));pairs.Add(Tuple.Create(b,a));pairs.Add(Tuple.Create(a,a));}
                    foreach(double tiny in new[]{0d,1e-16,1e-12,1e-8})pairs.Add(Tuple.Create(new NavPoint(-35.2,-43.2),new NavPoint(-35.2+tiny,-43.2)));
                    long oldDistances=0,newDistances=0;int accepted=0,rejected=0;
                    foreach(double radius in new[]{0d,.58,.72}){var c=new NavClearanceProfile("owned-boundary-reference",1,"ground",0,radius);
                        foreach(var pair in pairs){
                            TerrainLocations.DisableUnindexedBoundaryPruningForTests=true;TerrainLocations.UnindexedBoundaryDistanceCallsForTests=0;
                            bool oldStart=provider.TryLocate(pair.Item1,c,out var oldFrom),oldEnd=provider.TryLocate(pair.Item2,c,out var oldTo);bool oldSweep=oldStart&&oldEnd&&provider.CanSweep(oldFrom,oldTo,c);
                            NavLocation oldReached=default;System.Collections.Generic.IReadOnlyList<NavTypedLeg> oldTrace=null;bool oldSuccess=oldStart&&provider.TryTrace(oldFrom,pair.Item2,c,out oldReached,out oldTrace);oldDistances+=TerrainLocations.UnindexedBoundaryDistanceCallsForTests;
                            TerrainLocations.DisableUnindexedBoundaryPruningForTests=false;TerrainLocations.UnindexedBoundaryDistanceCallsForTests=0;
                            bool newStart=provider.TryLocate(pair.Item1,c,out var newFrom),newEnd=provider.TryLocate(pair.Item2,c,out var newTo);bool newSweep=newStart&&newEnd&&provider.CanSweep(newFrom,newTo,c);
                            NavLocation newReached=default;System.Collections.Generic.IReadOnlyList<NavTypedLeg> newTrace=null;bool newSuccess=newStart&&provider.TryTrace(newFrom,pair.Item2,c,out newReached,out newTrace);newDistances+=TerrainLocations.UnindexedBoundaryDistanceCallsForTests;
                            Assert.AreEqual(oldStart,newStart);Assert.AreEqual(oldEnd,newEnd);Assert.AreEqual(oldFrom,newFrom);Assert.AreEqual(oldTo,newTo);Assert.AreEqual(oldSweep,newSweep);Assert.AreEqual(oldSuccess,newSuccess);Assert.AreEqual(oldReached,newReached);
                            Assert.AreEqual(PlayableWorldRestoreTests.Facts(oldTrace),PlayableWorldRestoreTests.Facts(newTrace));
                            if(oldTrace!=null)for(int n=0;n<oldTrace.Count;n++){var a=oldTrace[n];var b=newTrace[n];Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.Cost),BitConverter.DoubleToInt64Bits(b.Cost));Assert.True(NavTypedLeg.SamePhysicalLocation(a.From,b.From));Assert.True(NavTypedLeg.SamePhysicalLocation(a.To,b.To));Assert.AreSame(a.PortalId,b.PortalId);}
                            if(oldSuccess)accepted++;else rejected++;
                        }
                    }
                    Assert.Greater(accepted,0);Assert.Greater(rejected,0);Assert.Less(newDistances,oldDistances,"actual unindexed cold query drops irrelevant distance work");
                    TestContext.WriteLine("OWNED_BOUNDARY_REFERENCE accepted="+accepted+" rejected="+rejected+" old_distance_calls="+oldDistances+" pruned_distance_calls="+newDistances);
                }finally{TerrainLocations.DisableUnindexedBoundaryPruningForTests=false;TerrainLocations.UnindexedBoundaryDistanceCallsForTests=0;provider.StorageRoot.ReleaseOwner();}
            }
        }
        [Test] public void ReportFoundryGeometryQueryCostWithoutThreshold()
        {
            var map=new FoundryMap(new FoundryProfileData());
            var geometry=new NavGeometry(map.HalfExtent,System.Linq.Enumerable.ToArray(map.MovementBlockers),1);
            var provider=new PartitionNavigationProvider(map,geometry,null);
            var clearance=new NavClearanceProfile("geometry-cost",1,"ground",0,.58);
            var pairs=new System.Collections.Generic.List<Tuple<NavLocation,NavLocation>>();var random=new Random(7108);
            for(int i=0;i<2000&&pairs.Count<256;i++){
                var a=new NavPoint(random.NextDouble()*map.HalfExtent*2-map.HalfExtent,random.NextDouble()*map.HalfExtent*2-map.HalfExtent);
                var b=i%2==0?new NavPoint(a.X+.8,a.Z-.8):new NavPoint(random.NextDouble()*map.HalfExtent*2-map.HalfExtent,random.NextDouble()*map.HalfExtent*2-map.HalfExtent);
                if(provider.TryLocate(a,clearance,out var from)&&provider.TryLocate(b,clearance,out var to))pairs.Add(Tuple.Create(from,to));
            }
            Assert.AreEqual(256,pairs.Count);int sweep=0,trace=0;
            void Sweep(){foreach(var pair in pairs)if(provider.CanSweep(pair.Item1,pair.Item2,clearance))sweep++;}
            void Trace(){foreach(var pair in pairs)if(provider.TryGraphTraceValidated(pair.Item1,pair.Item2.Position,clearance,out _,out _))trace++;}
            Sweep();Trace();var watch=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<100;i++)Sweep();watch.Stop();double sweepMs=watch.Elapsed.TotalMilliseconds;
            watch.Restart();for(int i=0;i<100;i++)Trace();watch.Stop();
            Assert.Greater(sweep,0);Assert.Greater(trace,0);
            TestContext.WriteLine("FOUNDRY_GEOMETRY_25600 sweep_ms="+sweepMs+" trace_ms="+watch.Elapsed.TotalMilliseconds+" sweep_ready="+sweep+" trace_ready="+trace);
        }
        private static System.Collections.Generic.List<double> OriginalCuts(NavPoint a,NavPoint b,Tuple<NavPoint,NavPoint>[] edges)
        {
            double Cross(double x,double z,double xx,double zz)=>x*zz-z*xx;
            var cuts=new System.Collections.Generic.List<double>{0,1};double dx=b.X-a.X,dz=b.Z-a.Z,length=dx*dx+dz*dz;if(length==0)return cuts;
            foreach(var edge in edges){var c=edge.Item1;var d=edge.Item2;double ex=d.X-c.X,ez=d.Z-c.Z,den=Cross(dx,dz,ex,ez);
                if(Math.Abs(den)>1e-12){double t=Cross(c.X-a.X,c.Z-a.Z,ex,ez)/den,u=Cross(c.X-a.X,c.Z-a.Z,dx,dz)/den;if(t>=0&&t<=1&&u>=0&&u<=1)cuts.Add(t);}
                else if(Math.Abs(Cross(c.X-a.X,c.Z-a.Z,dx,dz))<=1e-10){foreach(var point in new[]{c,d}){double t=((point.X-a.X)*dx+(point.Z-a.Z)*dz)/length;if(t>0&&t<1)cuts.Add(t);}}
            }
            cuts.Sort();return System.Linq.Enumerable.ToList(System.Linq.Enumerable.Distinct(cuts));
        }
        [Test] public void BroadPhaseKeepsEveryOriginalCutIncludingVeryShortCollinearSegments()
        {
            var type=typeof(PartitionNavigationProvider).Assembly.GetType("Spacewars.Simulation.TerrainLocations",true);
            var cuts=(Func<NavPoint,NavPoint,Tuple<NavPoint,NavPoint>[],System.Collections.Generic.IReadOnlyList<double>>)type.GetMethod("Cuts",BindingFlags.Static|BindingFlags.NonPublic).CreateDelegate(typeof(Func<NavPoint,NavPoint,Tuple<NavPoint,NavPoint>[],System.Collections.Generic.IReadOnlyList<double>>));
            var random=new Random(7108);var edges=new Tuple<NavPoint,NavPoint>[100];
            for(int i=0;i<edges.Length;i++)edges[i]=Tuple.Create(new NavPoint(random.NextDouble()*128-64,random.NextDouble()*128-64),new NavPoint(random.NextDouble()*128-64,random.NextDouble()*128-64));
            for(int i=0;i<500;i++){
                var a=new NavPoint(random.NextDouble()*128-64,random.NextDouble()*128-64);var b=i%2==0?new NavPoint(a.X+.8,a.Z-.8):new NavPoint(random.NextDouble()*128-64,random.NextDouble()*128-64);
                CollectionAssert.AreEqual(OriginalCuts(a,b,edges),cuts(a,b,edges));
            }
            foreach(double length in new[]{0d,1e-16,1e-12,1e-8,.8,100d})foreach(double offset in new[]{0d,1e-12,1e-8,.01,10d}){
                var a=default(NavPoint);var b=new NavPoint(length,0);var e=new[]{Tuple.Create(new NavPoint(-100,offset),new NavPoint(length*.5,offset+1e-12)),Tuple.Create(new NavPoint(length*.25,-1),new NavPoint(length*.25,1))};
                CollectionAssert.AreEqual(OriginalCuts(a,b,e),cuts(a,b,e),"length="+length+" offset="+offset);
            }
        }
        [Test] public void CompactGraphTracingKeepsExactPortalLegsAndAllClearanceResults()
        {
            var map=new ThreeCrossingsMap(new ThreeCrossingsProfileData());var g=new NavGeometry(map.HalfExtent,System.Linq.Enumerable.ToArray(map.MovementBlockers),1);
            var provider=new PartitionNavigationProvider(map,g,null);var method=typeof(PartitionNavigationProvider).GetMethod("TryGraphTrace",BindingFlags.Instance|BindingFlags.NonPublic);var validated=typeof(PartitionNavigationProvider).GetMethod("TryGraphTraceValidated",BindingFlags.Instance|BindingFlags.NonPublic);int portals=0;
            foreach(double radius in new[]{.58,.72}){
                var clearance=new NavClearanceProfile("graph-check",1,"ground",0,radius);
                void Check(NavPoint a,NavPoint b){
                    if(!provider.TryLocate(a,clearance,out var from))return;
                    bool expected=provider.TryTrace(from,b,clearance,out var end,out var full);var args=new object[]{from,b,clearance,null,null};bool actual=(bool)method.Invoke(provider,args);
                    Assert.AreEqual(expected,actual);Assert.AreEqual(end,(NavLocation)args[3]);
                    if(provider.TryLocate(b,clearance,out _)){
                        var certified=new object[]{from,b,clearance,null,null};bool certifiedResult=(bool)validated.Invoke(provider,certified);
                        Assert.AreEqual(actual,certifiedResult);Assert.AreEqual((NavLocation)args[3],(NavLocation)certified[3]);
                        Assert.AreEqual(PlayableWorldRestoreTests.Facts(args[4]),PlayableWorldRestoreTests.Facts(certified[4]));
                    }
                    if(!expected)return;var compact=(System.Collections.Generic.IReadOnlyList<NavTypedLeg>)args[4];
                    if(System.Linq.Enumerable.Any(full,l=>l.Kind==NavTypedLegKind.Portal)){
                        portals++;Assert.AreEqual(PlayableWorldRestoreTests.Facts(full),PlayableWorldRestoreTests.Facts(compact),"Detailed crossing trace must remain exact.");
                    }else Assert.IsEmpty(compact,"Same-surface move data is reconstructed by the graph, not retained by its compiler.");
                }
                for(int x=-60;x<=60;x+=4)for(int z=-60;z<=60;z+=4){var a=new NavPoint(x,z);Check(a,new NavPoint(x+.8,z));Check(a,new NavPoint(x,z+.8));}
                foreach(var seam in provider.Seams){
                    if(!seam.Allowed)continue;double dx=seam.To.X-seam.From.X,dz=seam.To.Z-seam.From.Z,length=Math.Sqrt(dx*dx+dz*dz);if(length==0)continue;
                    var mid=new NavPoint((seam.From.X+seam.To.X)/2,(seam.From.Z+seam.To.Z)/2);var a=new NavPoint(mid.X-dz/length*.4,mid.Z+dx/length*.4);var b=new NavPoint(mid.X+dz/length*.4,mid.Z-dx/length*.4);Check(a,b);Check(b,a);
                }
            }
            Assert.Greater(portals,0);TestContext.WriteLine("EXACT_COMPACT_PORTAL_TRACES="+portals);
        }
        [Test] public void IndexedCutsKeepOriginalSourceOrderAndExactDoubleBits()
        {
            var map=new ThreeCrossingsMap(new ThreeCrossingsProfileData());var g=new NavGeometry(map.HalfExtent,System.Linq.Enumerable.ToArray(map.MovementBlockers),1);var provider=new PartitionNavigationProvider(map,g,null);
            var locations=typeof(PartitionNavigationProvider).GetField("locations",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(provider);var type=locations.GetType();
            var indexed=(Func<NavPoint,NavPoint,System.Collections.Generic.IReadOnlyList<double>>)type.GetMethod("IndexedCuts",BindingFlags.Instance|BindingFlags.NonPublic).CreateDelegate(typeof(Func<NavPoint,NavPoint,System.Collections.Generic.IReadOnlyList<double>>),locations);
            var edges=(Tuple<NavPoint,NavPoint>[])type.GetField("edges",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(locations);var random=new Random(7108);
            void Check(NavPoint a,NavPoint b){CollectionAssert.AreEqual(System.Linq.Enumerable.Select(OriginalCuts(a,b,edges),BitConverter.DoubleToInt64Bits),System.Linq.Enumerable.Select(indexed(a,b),BitConverter.DoubleToInt64Bits));}
            for(int i=0;i<500;i++){var a=new NavPoint(random.NextDouble()*140-70,random.NextDouble()*140-70);var b=i%2==0?new NavPoint(a.X+.8,a.Z-.8):new NavPoint(random.NextDouble()*140-70,random.NextDouble()*140-70);Check(a,b);}
            foreach(var edge in edges){Check(edge.Item1,edge.Item2);Check(edge.Item2,edge.Item1);}
            foreach(double length in new[]{0d,1e-16,1e-12,1e-8,.8,100d}){Check(default,new NavPoint(length,0));Check(new NavPoint(BitConverter.Int64BitsToDouble(long.MinValue),0),new NavPoint(length,0));}
        }
        [Test] public void BoundedMemoizationMatchesExactUncachedFootprintsAcrossEvictionAndRadii(){
            var map=new ThreeCrossingsMap(new ThreeCrossingsProfileData());
            var g=new NavGeometry(map.HalfExtent,System.Linq.Enumerable.ToArray(map.MovementBlockers),1);
            var provider=new PartitionNavigationProvider(map,g,null);
            var locations=typeof(PartitionNavigationProvider).GetField("locations",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(provider);
            var type=locations.GetType();var cached=(Func<NavPoint,double,bool>)type.GetMethod("Footprint",BindingFlags.Instance|BindingFlags.NonPublic).CreateDelegate(typeof(Func<NavPoint,double,bool>),locations);
            var exact=(Func<NavPoint,double,bool>)type.GetMethod("UncachedFootprint",BindingFlags.Instance|BindingFlags.NonPublic).CreateDelegate(typeof(Func<NavPoint,double,bool>),locations);
            var random=new Random(7108);var points=new NavPoint[5000];for(int i=0;i<points.Length;i++)points[i]=new NavPoint(random.NextDouble()*140-70,random.NextDouble()*140-70);
            foreach(double radius in new[]{0d,.58,.72,1.4})for(int pass=0;pass<2;pass++)foreach(var point in points)Assert.AreEqual(exact(point,radius),cached(point,radius));
            var entries=(IDictionary)type.GetField("footprints",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(locations);
            Assert.AreEqual(4096,entries.Count);Assert.GreaterOrEqual(provider.EstimatedRetainedBytes,128L*entries.Count,"Provider retention must charge the entire bounded footprint cache.");
        }
    }
}
