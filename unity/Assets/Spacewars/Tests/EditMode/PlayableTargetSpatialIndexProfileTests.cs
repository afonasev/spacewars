using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    // Profiling-only current API workload. This deliberately does not create a
    // PlayableRuntime or advance the domain; owner labels cover the eight domain slots.
    public sealed class PlayableTargetSpatialIndexProfileTests
    {
        private const int Seed=19092026;
        private const int UnitCount=400;
        private const int WarmupPhases=8;
        private const int MeasuredPhases=32;
        private const int Repetitions=3;
        private const int QueriesPerSelector=200;
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private const BindingFlags PublicInstance=BindingFlags.Instance|BindingFlags.Public;
        private static readonly CultureInfo Invariant=CultureInfo.InvariantCulture;

        private readonly struct TargetData
        {
            internal readonly bool Found; internal readonly NavPoint Position; internal readonly int Health;
            internal TargetData(bool found,NavPoint position,int health){Found=found;Position=position;Health=health;}
        }
        private readonly struct TargetEntry
        {
            internal readonly int Id; internal readonly bool Building;
            internal TargetEntry(int id,bool building){Id=id;Building=building;}
        }
        private sealed class Actor
        {
            internal int Id; internal object Unit; internal PlayableOwner Owner; internal NavPoint Position; internal double Range;
        }
        private sealed class World : IDisposable
        {
            internal PlayableDomain Domain; internal PlayableTargetSpatialIndex Index;
            internal Actor[] Actors; internal TargetEntry[] Targets; internal byte[] Before;
            internal Action Rebuild; internal Func<NavPoint,PlayableOwner,double,int> Ordinary;
            internal Func<object,NavPoint,double,int> Follow;
            internal Func<PlayableOwner,PlayableOwner,bool> Hostile;
            internal Func<PlayableOwner,int,bool> Visible;
            internal Func<int,TargetData> Target;
            internal string InputHash; internal string Distribution;
            public void Dispose(){if(Domain==null)return;Domain.Navigation.ReleaseCommittedHostRoot();Domain.DestroyOwnedNavigation();Domain=null;}
        }
        private sealed class Phase
        {
            internal double BuildMs,OrdinaryMs,FollowMs;
            internal long Allocated=-1;
            internal int Visited,Candidates;
            internal int[] Results;
            internal string Checksum;
        }

        [Test,Timeout(120000)]
        public void Frozen400LayoutsMatchCurrentFullScanAndPreserveAuthorityBytes()
        {
            foreach(string distribution in Distributions)
            using(var world=CreateWorld(distribution))
            {
                var setupHash=world.InputHash;
                world.Rebuild();
                world.Index.CombatPhase=true;
                try
                {
                    var results=RunQueries(world);
                    Assert.AreEqual(2*QueriesPerSelector,results.Length);
                    for(int i=0;i<results.Length;i++)
                    {
                        var actor=world.Actors[i];
                        int expected=Reference(world,actor.Position,actor.Owner,actor.Range,i>=QueriesPerSelector);
                        Assert.AreEqual(expected,results[i],distribution+" query="+i+" actor="+actor.Id);
                    }
                    int ordinarySelected=0,followSelected=0,ordinaryHostile=0,followHostile=0;
                    var burstTargetField=world.Actors[QueriesPerSelector].Unit.GetType().GetField("BurstTarget",PublicInstance);
                    Assert.NotNull(burstTargetField,distribution+" Unit.BurstTarget public field binding");
                    for(int i=0;i<QueriesPerSelector;i++)
                    {
                        if(results[i]!=0){ordinarySelected++;if(world.Hostile(world.Actors[i].Owner,TargetOwner(world,results[i])))ordinaryHostile++;}
                        int followIndex=i+QueriesPerSelector;
                        if(results[followIndex]!=0){followSelected++;if(world.Hostile(world.Actors[followIndex].Owner,TargetOwner(world,results[followIndex])))followHostile++;}
                        Assert.AreEqual(0,(int)burstTargetField.GetValue(world.Actors[followIndex].Unit),distribution+" Follow selector has no restored or active BurstTarget actor="+world.Actors[followIndex].Id);
                    }
                    Assert.Greater(ordinaryHostile,0,distribution+" ordinary selector must select at least one visible hostile");
                    Assert.Greater(followHostile,0,distribution+" Follow selector must select at least one visible hostile");
                    Assert.AreEqual(setupHash,HashInputs(world.Actors,distribution),distribution+" input freeze");
                    CollectionAssert.AreEqual(world.Before,Capture(world.Domain),distribution+" selector/build remain authority-neutral");
                    TestContext.WriteLine("TARGET_INDEX_FUNCTIONAL distribution="+distribution+" units=400 owners=8 api=standalone-domain seed="+Seed+" input_sha256="+setupHash+" queries=400 ordinary=200 follow=200 ordinary_selected="+ordinarySelected+" follow_selected="+followSelected+" ordinary_hostile="+ordinaryHostile+" follow_hostile="+followHostile+" checksum="+Checksum(results));
                }
                finally{world.Index.CombatPhase=false;}
            }
        }

        [Test,Explicit("CPU measurement requires this exact filter and exclusive runner."),Timeout(120000)]
        public void Frozen400LayoutsBuildAndAcquireCpuProfile()
        {
            var method=System.Diagnostics.Stopwatch.StartNew();
            bool allocationAvailable=CalibrateAllocationCounter();
            TestContext.WriteLine("TARGET_INDEX_CONFIG seed="+Seed+" units=400 owners=8(human-concept=4,ai-concept=4;labels-only) distributions=3 repetitions=3 warmup_phases=8 measured_phases=32 queries_per_phase=400 ordinary=200 follow=200 rebuilds_per_phase=1 stopwatch_frequency="+System.Diagnostics.Stopwatch.Frequency+" allocation_metric="+(allocationAvailable?"GetAllocatedBytesForCurrentThread":"NotRun_counter_unavailable")+" exclusive_editor=true full_combat_tick=false cadence_claim=false");
            foreach(string distribution in Distributions)
            {
                var distributionHashes=new List<string>();
                for(int repetition=0;repetition<Repetitions;repetition++)
                using(var world=CreateWorld(distribution))
                {
                    distributionHashes.Add(world.InputHash);
                    var warmChecksums=new string[WarmupPhases];
                    for(int i=0;i<WarmupPhases;i++)
                    {
                        var warm=RunPhase(world,false);
                        warmChecksums[i]=warm.Checksum;
                        CheckBudget(method,distribution,"warmup");
                    }
                    Assert.IsTrue(warmChecksums.All(x=>x==warmChecksums[0]),distribution+" warmup checksum stable");
                    var samples=new Phase[MeasuredPhases];
                    for(int i=0;i<MeasuredPhases;i++)
                    {
                        samples[i]=RunPhase(world,allocationAvailable);
                        Assert.AreEqual(warmChecksums[0],samples[i].Checksum,distribution+" measured checksum stable");
                        CheckBudget(method,distribution,"measured");
                    }
                    CollectionAssert.AreEqual(world.Before,Capture(world.Domain),distribution+" benchmark leaves authority bytes unchanged");
                    WriteSeries(distribution,repetition,world.InputHash,samples,allocationAvailable);
                }
                Assert.AreEqual(1,distributionHashes.Distinct().Count(),distribution+" repeats exact frozen inputs");
            }
            TestContext.WriteLine("TARGET_INDEX_CONTEXT frozen_A0_8ms_is_tick_cpu_p95_technical_target_not_full_tick_pass; measured_scope=index_rebuild_plus_400_direct_current_selectors_only");
        }

        private static readonly string[] Distributions={"sparse-grid","compact-cluster","cell-boundary-packed"};

        private static World CreateWorld(string distribution)
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;
            var domain=new PlayableDomain(profile,Seed,false);
            try
            {
            var initialBodies=domain.Navigation.Crowd.Units.ToArray();
            var initialRows=(IDictionary)Rows(domain,"units");
            var initialUnitIds=initialRows.Keys.Cast<int>().OrderBy(x=>x).ToArray();
            Assert.AreEqual(initialBodies.Length,initialRows.Count,distribution+" all authored initial units have navigation bodies");
            var candidates=CandidatePoints(distribution);
            var chosen=new List<NavPoint>(UnitCount);
            double radius=PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank);
            bool Free(NavPoint p)
            {
                if(!domain.Geometry.IsFree(p,radius))return false;
                foreach(var existing in domain.Navigation.Crowd.Units)if(Distance(existing.Position,p)<existing.Radius+radius)return false;
                foreach(var prior in chosen)if(Distance(prior,p)<2*radius)return false;
                return true;
            }
            foreach(var point in candidates)
            {
                if(!Free(point))continue;
                chosen.Add(point);
                if(chosen.Count==UnitCount)break;
            }
            if(chosen.Count!=UnitCount)throw new AssertionException(distribution+" cannot place 400 tanks on current ThreeCrossings authored geometry; placed="+chosen.Count+"; no profile/map adjustment permitted.");
            Assert.AreEqual(UnitCount,chosen.Distinct().Count(),distribution+" 400 distinct candidate centers");
            foreach(var point in chosen)Assert.IsTrue(domain.Geometry.IsFree(point,radius),distribution+" chosen center respects authored geometry and Tank radius");

            var spawn=typeof(PlayableDomain).GetMethod("SpawnUnit",Hidden);
            var actors=new Actor[UnitCount];
            for(int i=0;i<UnitCount;i++)
            {
                var owner=(PlayableOwner)(i%8);
                int id=(int)spawn.Invoke(domain,new object[]{chosen[i],owner,PlayableEntityKind.Tank});
                object unit=GetRow(domain,"units",id);
                actors[i]=new Actor{Id=id,Unit=unit,Owner=owner,Position=chosen[i],Range=PlayableUnitRules.Range(profile,PlayableEntityKind.Tank)};
            }
            var actualBodies=domain.Navigation.Crowd.Units.ToArray();
            var actualRows=(IDictionary)Rows(domain,"units");
            Assert.AreEqual(initialBodies.Length+UnitCount,actualBodies.Length,distribution+" includes all initial and 400 spawned navigation bodies");
            Assert.AreEqual(initialUnitIds.Length+UnitCount,actualRows.Count,distribution+" includes all initial and 400 spawned actor records");
            CollectionAssert.AreEquivalent(initialBodies.Select(x=>x.Id).Concat(actors.Select(x=>x.Id)).OrderBy(x=>x),actualBodies.Select(x=>x.Id).OrderBy(x=>x),distribution+" no initial navigation body is ignored");
            CollectionAssert.AreEquivalent(initialUnitIds.Concat(actors.Select(x=>x.Id)).OrderBy(x=>x),actualRows.Keys.Cast<int>().OrderBy(x=>x),distribution+" actor records exactly match initial plus 400 spawned IDs");
            for(int i=0;i<actualBodies.Length;i++)
            {
                var body=actualBodies[i];
                Assert.IsTrue(domain.Geometry.IsFree(body.Position,body.Radius),distribution+" body geometry id="+body.Id);
                for(int j=0;j<i;j++)Assert.GreaterOrEqual(Distance(body.Position,actualBodies[j].Position)+1e-9,body.Radius+actualBodies[j].Radius,distribution+" actual body pair clearance ids="+body.Id+","+actualBodies[j].Id);
            }
            RefreshAllEightVisions(domain);

            var units=(IDictionary)Rows(domain,"units");var buildings=(IDictionary)Rows(domain,"buildings");
            var targets=new List<TargetEntry>(UnitCount+buildings.Count);
            foreach(int id in units.Keys.Cast<int>().OrderBy(x=>x))targets.Add(new TargetEntry(id,false));
            foreach(int id in buildings.Keys.Cast<int>().OrderBy(x=>x))targets.Add(new TargetEntry(id,true));

            var index=(PlayableTargetSpatialIndex)typeof(PlayableDomain).GetField("targetSpatialIndex",Hidden).GetValue(domain);
            var rebuild=Expression.Lambda<Action>(Expression.Call(Expression.Constant(domain),typeof(PlayableDomain).GetMethod("RebuildTargetSpatialIndex",Hidden))).Compile();
            var ordinary=BindOrdinary(domain);
            var follow=BindFollow(domain,actors[0].Unit.GetType());
            var hostile=BindHostile(domain);
            var visible=BindVisible(domain);
            var target=BindTarget(domain);
            var world=new World{Domain=domain,Index=index,Actors=actors,Targets=targets.ToArray(),Rebuild=rebuild,Ordinary=ordinary,Follow=follow,Hostile=hostile,Visible=visible,Target=target,Distribution=distribution,InputHash=HashInputs(actors,distribution)};
            world.Before=Capture(domain);
            return world;
            }
            catch
            {
                domain.Navigation.ReleaseCommittedHostRoot();
                domain.DestroyOwnedNavigation();
                throw;
            }
        }

        private static IEnumerable<NavPoint> CandidatePoints(string distribution)
        {
            if(distribution=="sparse-grid")
            {
                const int side=41,total=side*side,step=733,offset=Seed%total;
                for(int i=0;i<total;i++){int k=(i*step+offset)%total;yield return new NavPoint(-60+(k%side)*3,-60+(k/side)*3);}
                yield break;
            }
            int grid=26;double spacing=1.5;double centerX=22,centerZ=-22;
            if(distribution=="cell-boundary-packed"){grid=30;centerX=16;centerZ=-16;}
            for(int i=0;i<grid*grid;i++)
            {
                int k=(i*677+Seed%(grid*grid))%(grid*grid);
                int x=k%grid,z=k/grid;
                yield return new NavPoint(centerX+(x-(grid-1)/2d)*spacing,centerZ+(z-(grid-1)/2d)*spacing);
            }
        }

        private static int[] RunQueries(World world)
        {
            var results=new int[QueriesPerSelector*2];
            for(int i=0;i<QueriesPerSelector;i++)results[i]=world.Ordinary(world.Actors[i].Position,world.Actors[i].Owner,world.Actors[i].Range);
            for(int i=0;i<QueriesPerSelector;i++){var actor=world.Actors[i+QueriesPerSelector];results[QueriesPerSelector+i]=world.Follow(actor.Unit,actor.Position,actor.Range);}
            return results;
        }

        private static Phase RunPhase(World world,bool allocationAvailable)
        {
            var phase=new Phase{Results=new int[2*QueriesPerSelector]};
            long allocStart=allocationAvailable?GC.GetAllocatedBytesForCurrentThread():0;
            long start=System.Diagnostics.Stopwatch.GetTimestamp();world.Rebuild();long stop=System.Diagnostics.Stopwatch.GetTimestamp();phase.BuildMs=(stop-start)*1000d/System.Diagnostics.Stopwatch.Frequency;
            world.Index.CombatPhase=true;
            try
            {
                var timer=System.Diagnostics.Stopwatch.StartNew();
                for(int i=0;i<QueriesPerSelector;i++){var a=world.Actors[i];phase.Results[i]=world.Ordinary(a.Position,a.Owner,a.Range);phase.Visited+=world.Index.VisitedEntries;phase.Candidates+=world.Index.Candidates.Count;}
                phase.OrdinaryMs=timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                for(int i=0;i<QueriesPerSelector;i++){var a=world.Actors[i+QueriesPerSelector];phase.Results[QueriesPerSelector+i]=world.Follow(a.Unit,a.Position,a.Range);phase.Visited+=world.Index.VisitedEntries;phase.Candidates+=world.Index.Candidates.Count;}
                phase.FollowMs=timer.Elapsed.TotalMilliseconds;
            }
            finally{world.Index.CombatPhase=false;}
            if(allocationAvailable)phase.Allocated=GC.GetAllocatedBytesForCurrentThread()-allocStart;
            phase.Checksum=Checksum(phase.Results);
            return phase;
        }

        private static int Reference(World world,NavPoint point,PlayableOwner owner,double range,bool follow)
        {
            int best=0,bestClass=2;double bestDistance=range;
            foreach(var entry in world.Targets)
            {
                if(!world.Visible(owner,entry.Id))continue;
                var target=world.Target(entry.Id);if(!target.Found)continue;
                double d=Distance(point,target.Position);int targetClass=follow?0:entry.Building?1:0;
                if(d<=range&&(best==0||targetClass<bestClass||targetClass==bestClass&&(d<bestDistance||d==bestDistance&&entry.Id<best)))
                {best=entry.Id;bestClass=targetClass;bestDistance=d;}
            }
            return best;
        }
        private static PlayableOwner TargetOwner(World world,int id)
        {
            var units=(IDictionary)Rows(world.Domain,"units");
            var buildings=(IDictionary)Rows(world.Domain,"buildings");
            object row=units.Contains(id)?units[id]:buildings.Contains(id)?buildings[id]:null;
            Assert.NotNull(row,"selected target id="+id+" must resolve to an existing unit or building row");
            var ownerField=row.GetType().GetField("Owner",PublicInstance);
            Assert.NotNull(ownerField,"target row type="+row.GetType().FullName+" Owner public field binding");
            return (PlayableOwner)ownerField.GetValue(row);
        }

        private static Func<NavPoint,PlayableOwner,double,int> BindOrdinary(PlayableDomain domain)
        {
            var parameters=new[]{Expression.Parameter(typeof(NavPoint),"p"),Expression.Parameter(typeof(PlayableOwner),"o"),Expression.Parameter(typeof(double),"r")};
            var call=Expression.Call(Expression.Constant(domain),typeof(PlayableDomain).GetMethod("NearestDefenseTarget",Hidden),parameters);
            return Expression.Lambda<Func<NavPoint,PlayableOwner,double,int>>(call,parameters).Compile();
        }

        private static Func<object,NavPoint,double,int> BindFollow(PlayableDomain domain,Type unitType)
        {
            var actor=Expression.Parameter(typeof(object),"actor");var point=Expression.Parameter(typeof(NavPoint),"point");var range=Expression.Parameter(typeof(double),"range");
            var call=Expression.Call(Expression.Constant(domain),typeof(PlayableDomain).GetMethod("FollowDefenseTarget",Hidden),Expression.Convert(actor,unitType),point,range);
            return Expression.Lambda<Func<object,NavPoint,double,int>>(call,actor,point,range).Compile();
        }
        private static Func<PlayableOwner,PlayableOwner,bool> BindHostile(PlayableDomain domain)
        {
            var method=typeof(PlayableDomain).GetMethod("Hostile",Hidden);
            Assert.NotNull(method,"PlayableDomain.Hostile private method binding");
            var left=Expression.Parameter(typeof(PlayableOwner),"left");var right=Expression.Parameter(typeof(PlayableOwner),"right");
            return Expression.Lambda<Func<PlayableOwner,PlayableOwner,bool>>(Expression.Call(Expression.Constant(domain),method,left,right),left,right).Compile();
        }
        private static Func<PlayableOwner,int,bool> BindVisible(PlayableDomain domain)
        {
            var owner=Expression.Parameter(typeof(PlayableOwner),"owner");var id=Expression.Parameter(typeof(int),"id");
            var call=Expression.Call(Expression.Constant(domain),typeof(PlayableDomain).GetMethod("VisibleTarget",Hidden),owner,id);
            return Expression.Lambda<Func<PlayableOwner,int,bool>>(call,owner,id).Compile();
        }
        private static Func<int,TargetData> BindTarget(PlayableDomain domain)
        {
            var id=Expression.Parameter(typeof(int),"id");var found=Expression.Variable(typeof(bool),"found");var position=Expression.Variable(typeof(NavPoint),"position");var health=Expression.Variable(typeof(int),"health");
            var call=Expression.Call(Expression.Constant(domain),typeof(PlayableDomain).GetMethod("Target",Hidden),id,position,health);
            var ctor=typeof(TargetData).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(bool),typeof(NavPoint),typeof(int)},null);
            var body=Expression.Block(new[]{found,position,health},Expression.Assign(found,call),Expression.New(ctor,found,position,health));
            return Expression.Lambda<Func<int,TargetData>>(body,id).Compile();
        }

        private static string HashInputs(Actor[] actors,string distribution)
        {
            var sb=new StringBuilder();sb.Append(distribution).Append('|').Append(Seed).Append('|').Append(UnitCount).Append('|').Append(QueriesPerSelector).Append('\n');
            foreach(var a in actors)sb.Append(a.Id).Append('|').Append((int)a.Owner).Append('|').Append(a.Position.X.ToString("R",Invariant)).Append('|').Append(a.Position.Z.ToString("R",Invariant)).Append('|').Append(a.Range.ToString("R",Invariant)).Append('|').Append((int)PlayableEntityKind.Tank).Append('\n');
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-","").ToLowerInvariant();
        }
        private static string Checksum(int[] results)
        {
            unchecked{ulong h=14695981039346656037UL;foreach(int value in results){h^=(uint)value;h*=1099511628211UL;}return h.ToString("x16",Invariant);}
        }
        private static byte[] Capture(PlayableDomain domain)=>(byte[])typeof(PlayableDomain).GetMethod("CaptureWorldBytes",Hidden).Invoke(domain,new object[]{Seed,"target-index-profile-v1"});
        private static void RefreshAllEightVisions(PlayableDomain domain)
        {
            const BindingFlags all=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            var buildings=(IDictionary)Rows(domain,"buildings");var actual=new List<KnownBuilding>();
            foreach(int id in buildings.Keys.Cast<int>().OrderBy(x=>x))
            {
                object row=buildings[id];var t=row.GetType();var owner=(PlayableOwner)t.GetField("Owner",all).GetValue(row);
                var kind=(PlayableBuildingKind)t.GetField("Kind",all).GetValue(row);var position=(NavPoint)t.GetField("Position",all).GetValue(row);
                double heading=(double)t.GetField("Heading",all).GetValue(row);
                actual.Add(new KnownBuilding(id,(int)owner,owner,kind,position,heading,false));
            }
            var visionMethod=typeof(PlayableDomain).GetMethod("Vision",Hidden);var sourceMethod=typeof(PlayableDomain).GetMethod("VisionSources",Hidden);
            for(int i=0;i<8;i++)
            {
                var owner=(PlayableOwner)i;var vision=(PlayableVision)visionMethod.Invoke(domain,new object[]{owner});
                var sources=(IEnumerable<VisionSource>)sourceMethod.Invoke(domain,new object[]{owner});vision.Refresh(sources,actual);
            }
        }
        private static double Distance(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        private static void Invoke(object target,string name)=>target.GetType().GetMethod(name,Hidden).Invoke(target,null);
        private static object Rows(object target,string name)=>target.GetType().GetField(name,Hidden).GetValue(target);
        private static object GetRow(object target,string name,int id)
        {var rows=Rows(target,name);return rows.GetType().GetProperty("Item").GetValue(rows,new object[]{id});}

        private static bool CalibrateAllocationCounter()
        {
            try{long before=GC.GetAllocatedBytesForCurrentThread();var escape=new byte[4096];Thread.MemoryBarrier();GC.KeepAlive(escape);long delta=GC.GetAllocatedBytesForCurrentThread()-before;return delta>=4096;}
            catch(Exception){return false;}
        }
        private static void CheckBudget(System.Diagnostics.Stopwatch method,string distribution,string phase)
        {if(method.Elapsed.TotalSeconds>60)throw new InconclusiveException("TARGET_INDEX_NOT_RUN reason=60s_method_body_budget_exceeded distribution="+distribution+" phase="+phase+" elapsed_seconds="+method.Elapsed.TotalSeconds.ToString("R",Invariant)+" partial_measurements_are_not_a_winner");}
        private static void WriteSeries(string distribution,int repetition,string hash,Phase[] samples,bool allocations)
        {
            double[] total=samples.Select(x=>x.BuildMs+x.OrdinaryMs+x.FollowMs).OrderBy(x=>x).ToArray();
            double[] build=samples.Select(x=>x.BuildMs).OrderBy(x=>x).ToArray();
            double[] ordinary=samples.Select(x=>x.OrdinaryMs).OrderBy(x=>x).ToArray();
            double[] follow=samples.Select(x=>x.FollowMs).OrderBy(x=>x).ToArray();
            long[] alloc=samples.Select(x=>x.Allocated).ToArray();
            string raw=string.Join(";",samples.Select(x=>x.BuildMs.ToString("R",Invariant)+"/"+x.OrdinaryMs.ToString("R",Invariant)+"/"+x.FollowMs.ToString("R",Invariant)+"/"+x.Candidates.ToString(Invariant)+"/"+x.Visited.ToString(Invariant)+"/"+x.Allocated.ToString(Invariant)+"/"+x.Checksum));
            TestContext.WriteLine("TARGET_INDEX_SERIES distribution="+distribution+" repetition="+repetition+" input_sha256="+hash+" rebuilds_per_phase=1 ordinary_queries=200 follow_queries=200 warmup=8 samples=32 p50_total_ms="+Quantile(total,.50).ToString("R",Invariant)+" p95_total_ms="+Quantile(total,.95).ToString("R",Invariant)+" p99_total_ms="+Quantile(total,.99).ToString("R",Invariant)+" max_total_ms="+total[total.Length-1].ToString("R",Invariant)+" p95_build_ms="+Quantile(build,.95).ToString("R",Invariant)+" p95_ordinary_ms="+Quantile(ordinary,.95).ToString("R",Invariant)+" p95_follow_ms="+Quantile(follow,.95).ToString("R",Invariant)+" candidates_total_min="+samples.Min(x=>x.Candidates)+" candidates_total_max="+samples.Max(x=>x.Candidates)+" visited_total_min="+samples.Min(x=>x.Visited)+" visited_total_max="+samples.Max(x=>x.Visited)+" allocation_phase="+(allocations?string.Join(",",alloc.Select(x=>x.ToString(Invariant))):"NotRun")+" raw_phase=buildMs/ordinaryMs/followMs/candidates/visited/allocBytes/checksum:"+raw);
        }
        private static double Quantile(double[] sorted,double p)=>sorted[Math.Max(0,(int)Math.Ceiling(p*sorted.Length)-1)];
    }
}
