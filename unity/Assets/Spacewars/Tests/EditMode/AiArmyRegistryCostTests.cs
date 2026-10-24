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
    public sealed class AiArmyRegistryCostTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static T Field<T>(AiArmyRegistry registry,string name)=>(T)typeof(AiArmyRegistry).GetField(name,F).GetValue(registry);
        static AiArmyRegistry Registry(int history)
        {
            var armies=new List<AiArmyState>();
            for(int i=0;i<history+2;i++){
                bool active=i>=history;int first=(i-history)*3+1;
                armies.Add(new AiArmyState{Id=i+1,OwnerId="player-1",TacticalOwner="army",Role=AiArmyRole.Attack,
                    Phase=active?AiArmyPhase.Advancing:AiArmyPhase.Disbanded,Members=active?Enumerable.Range(first,3).ToArray():Array.Empty<int>(),
                    Reinforcements=active?new[]{first+2}:Array.Empty<int>(),LeaderId=active?first:0,MinimumViableForce=3,CreatedTick=1,ProgressTick=10,
                    Anchor=new NavPoint(0,0),Rally=new NavPoint(8,8)});
            }
            return AiArmyRegistry.Restore(new AiArmyRegistryState{OwnerId="player-1",Generation=71,NextId=history+3,Armies=armies.ToArray()},AiProfile.Initial,AiDifficulty.Fighter,10);
        }
        static PlayableAiObservation Observation()
        {
            var p=PlayableProfile.Default;var entities=Enumerable.Range(1,6).Select(i=>new PlayableEntitySnapshot(i,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(i,0),100,false,0,0,0)).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,7,1,10,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,100,null,entities,Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
        }
        static void Original(AiArmyRegistry r)
        {
            var armies=Field<Dictionary<long,AiArmyState>>(r,"armies");var membership=Field<Dictionary<int,long>>(r,"membership");
            long nextId=Field<long>(r,"nextId");int majorCap=Field<int>(r,"majorCap"),scoutCap=Field<int>(r,"scoutCap");string OwnerId=r.OwnerId;
            long ArmyFor(int id)=>r.ArmyFor(id);
            bool Finite(NavPoint? point)=>!point.HasValue||!double.IsNaN(point.Value.X)&&!double.IsInfinity(point.Value.X)&&!double.IsNaN(point.Value.Z)&&!double.IsInfinity(point.Value.Z);
            var active=armies.Values.Where(a=>a.Phase!=AiArmyPhase.Disbanded).OrderBy(a=>a.Id).ToArray();var all=armies.Values.ToArray();
            if(nextId<1||all.Any(a=>a.Id<=0||a.Id>=nextId||a.OwnerId!=OwnerId||string.IsNullOrWhiteSpace(a.TacticalOwner)||!Enum.IsDefined(typeof(AiArmyRole),a.Role)||!Enum.IsDefined(typeof(AiArmyPhase),a.Phase)||a.CreatedTick<0||a.ProgressTick<a.CreatedTick||a.MinimumViableForce<1||!Finite(a.Anchor)||!Finite(a.Rally)||a.Reinforcements.Except(a.Members).Any()||a.Reinforcements.Any(x=>x<=0)||a.Reinforcements.Distinct().Count()!=a.Reinforcements.Length||!a.Reinforcements.SequenceEqual(a.Reinforcements.OrderBy(x=>x)))||active.Count(a=>a.Major)>majorCap||active.Count(a=>a.Role==AiArmyRole.Scout)>scoutCap||active.Any(a=>a.Members.Length==0||a.Members.Any(x=>x<=0)||!a.Members.Contains(a.LeaderId))||all.Where(a=>a.Phase==AiArmyPhase.Disbanded).Any(a=>a.Members.Length!=0||a.Reinforcements.Length!=0||a.LeaderId!=0)||active.SelectMany(a=>a.Members).Distinct().Count()!=active.Sum(a=>a.Members.Length)||membership.Count!=active.Sum(a=>a.Members.Length)||active.Any(a=>a.Members.Any(id=>ArmyFor(id)!=a.Id)))throw new InvalidOperationException("Army registry invariant");
        }
        static bool Accepts(Action action){try{action();return true;}catch(InvalidOperationException e){Assert.AreEqual("Army registry invariant",e.Message);return false;}}
        [Test] public void OriginalPredicateMatchesValidHistoryAndCorruptScalarMembershipCases()
        {
            var mutations=new Action<AiArmyRegistry,AiArmyState,Dictionary<int,long>>[]{
                (r,a,m)=>{},(r,a,m)=>a.Members=a.Members.Reverse().ToArray(),(r,a,m)=>a.Reinforcements=Array.Empty<int>(),
                (r,a,m)=>a.Id=0,(r,a,m)=>a.Id=r.Capture().NextId,(r,a,m)=>a.OwnerId="foreign",(r,a,m)=>a.TacticalOwner=" ",
                (r,a,m)=>a.Role=(AiArmyRole)(-1),(r,a,m)=>a.Role=(AiArmyRole)99,(r,a,m)=>a.Phase=(AiArmyPhase)(-1),(r,a,m)=>a.Phase=(AiArmyPhase)99,
                (r,a,m)=>a.CreatedTick=-1,(r,a,m)=>a.ProgressTick=0,(r,a,m)=>a.MinimumViableForce=0,
                (r,a,m)=>a.Anchor=new NavPoint(double.NaN,0),(r,a,m)=>a.Rally=new NavPoint(0,double.PositiveInfinity),
                (r,a,m)=>a.Reinforcements=new[]{1,99},(r,a,m)=>a.Reinforcements=new[]{0},(r,a,m)=>a.Reinforcements=new[]{1,1},(r,a,m)=>a.Reinforcements=new[]{2,1},
                (r,a,m)=>a.Members=Array.Empty<int>(),(r,a,m)=>{a.Reinforcements=Array.Empty<int>();a.Members=new[]{0,2,3};},
                (r,a,m)=>a.LeaderId=99,(r,a,m)=>a.Phase=AiArmyPhase.Disbanded,(r,a,m)=>a.Members=new[]{1,1,3},
                (r,a,m)=>m.Remove(1),(r,a,m)=>m[1]=999,(r,a,m)=>m.Add(99,a.Id),
                (r,a,m)=>{a.Members=new[]{1,1,3};m.Remove(2);m.Add(99,a.Id);},
                (r,a,m)=>typeof(AiArmyRegistry).GetField("nextId",F).SetValue(r,0L),
                (r,a,m)=>typeof(AiArmyRegistry).GetField("majorCap",F).SetValue(r,1),
                (r,a,m)=>{a.Role=AiArmyRole.Scout;typeof(AiArmyRegistry).GetField("scoutCap",F).SetValue(r,0);}
            };
            int accepted=0,rejected=0;
            foreach(int history in new[]{0,16,64})foreach(var mutate in mutations){
                var r=Registry(history);var row=Field<Dictionary<long,AiArmyState>>(r,"armies")[history+1];mutate(r,row,Field<Dictionary<int,long>>(r,"membership"));
                bool original=Accepts(()=>Original(r));Assert.AreEqual(original,Accepts(r.AssertInvariants),$"history={history} case={Array.IndexOf(mutations,mutate)}");if(original)accepted++;else rejected++;
            }
            Assert.AreEqual(9,accepted);Assert.AreEqual(87,rejected);
            foreach(var mutate in new Action<AiArmyState>[] {a=>a.LeaderId=1,a=>a.Members=new[]{1},a=>a.Reinforcements=new[]{1}}){
                var r=Registry(2);mutate(Field<Dictionary<long,AiArmyState>>(r,"armies")[1]);Assert.False(Accepts(()=>Original(r)));Assert.False(Accepts(r.AssertInvariants));
            }
            foreach(AiArmyRole role in Enum.GetValues(typeof(AiArmyRole)))foreach(AiArmyPhase phase in Enum.GetValues(typeof(AiArmyPhase))){
                var r=Registry(2);var row=Field<Dictionary<long,AiArmyState>>(r,"armies")[3];row.Role=role;row.Phase=phase;
                Assert.AreEqual(Accepts(()=>Original(r)),Accepts(r.AssertInvariants));
            }
        }
        [Test] public void ReportRegistryOperationsWithoutTimingThreshold()
        {
            const int count=2000;var observation=Observation();
            foreach(int history in new[]{0,16,64}){
                var r=Registry(history);r.AssertInvariants();r.Observe(observation);r.Capture();
                var watch=Stopwatch.StartNew();for(int i=0;i<count;i++)r.AssertInvariants();watch.Stop();double invariant=watch.Elapsed.TotalMilliseconds;
                watch.Restart();int rows=0;for(int i=0;i<count;i++)rows+=r.Capture().Armies.Length;watch.Stop();double capture=watch.Elapsed.TotalMilliseconds;
                watch.Restart();for(int i=0;i<count;i++)r.Observe(observation);watch.Stop();
                Assert.AreEqual(count*(history+2),rows);Assert.AreEqual(history+3,r.Capture().NextId);Assert.AreEqual(history+1,r.ArmyFor(1));
                TestContext.WriteLine($"REGISTRY_COST history={history} active=2 calls={count} invariant_ms={invariant:F6} capture_ms={capture:F6} observe_ms={watch.Elapsed.TotalMilliseconds:F6}");
            }
        }
    }
}
