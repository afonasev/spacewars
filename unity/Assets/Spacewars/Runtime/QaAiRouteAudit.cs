#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // QA observer only: keys/counts, never memoized answers or authority state.
    public static class QaAiRouteAudit
    {
        internal const int MaximumProofProfilePairs=4096,MaximumOwners=8;
        private static readonly bool Requested=Array.IndexOf(Environment.GetCommandLineArgs(),"-q10Readouts")>=0&&Array.IndexOf(Environment.GetCommandLineArgs(),"-aiRouteAuditEvidence")>=0;
#if UNITY_INCLUDE_TESTS
        internal static bool? EnabledForTests;
#endif
        private static bool Enabled{
            get{
#if UNITY_INCLUDE_TESTS
                if(EnabledForTests.HasValue)return EnabledForTests.Value;
#endif
                return Requested;
            }
        }
        private static bool armed;private static long armedGeneration;private static string[] armedOwners;private static PlayableProfile armedProfile;
        internal static void Arm(long generation,string[] ownerIds,PlayableProfile profile){
            if(!Requested)return;lock(gate){if(armed)return;if(ownerIds==null||ownerIds.Length!=MaximumOwners)return;armedGeneration=generation;armedOwners=(string[])ownerIds.Clone();armedProfile=profile;System.Threading.Volatile.Write(ref armed,true);}
        }
        private static bool Eligible(PlayableAiObservation o,PlayableProfile p){
#if UNITY_INCLUDE_TESTS
            if(EnabledForTests.HasValue)return EnabledForTests.Value;
#endif
            if(!Requested||!System.Threading.Volatile.Read(ref armed)||o.Generation!=armedGeneration||!ReferenceEquals(p,armedProfile))return false;
            for(int i=0;i<armedOwners.Length;i++)if(armedOwners[i]==o.OwnerId)return true;return false;
        }
        [ThreadStatic] private static Scope current;
        private static readonly object gate=new object();private static readonly Totals[] owners=new Totals[MaximumOwners];private static int ownerCount,droppedOwners;
        public struct Totals
        {
            public string Owner,ProfileId;public int ProfileRevision,ProfileReference,MaxPairs,MaxBound;
            public long Generation,LastTick,Reviews,SafeCalls,WithdrawalCalls,UniquePairs,UniqueSafe,UniqueWithdrawal,RepeatedSafe,RepeatedWithdrawal,UntrackedCalls,ForeignObservationCalls,ForeignProfileCalls,NullRouteCalls,ObserverTicks,ReviewTicks;
            public double ObserverMilliseconds=>ObserverTicks*1000d/Stopwatch.Frequency;public double ReviewMilliseconds=>ReviewTicks*1000d/Stopwatch.Frequency;
        }
        private readonly struct Key : IEquatable<Key>
        {
            private readonly PlayableRouteProof route;private readonly PlayableProfile profile;
            internal Key(PlayableRouteProof r,PlayableProfile p){route=r;profile=p;}
            public bool Equals(Key other)=>ReferenceEquals(route,other.route)&&ReferenceEquals(profile,other.profile);
            public override bool Equals(object other)=>other is Key k&&Equals(k);
            public override int GetHashCode()=>unchecked((route==null?0:RuntimeHelpers.GetHashCode(route))*397^(profile==null?0:RuntimeHelpers.GetHashCode(profile)));
        }
        internal sealed class Scope : IDisposable
        {
            private readonly PlayableAiObservation observation;private readonly PlayableProfile profile;private readonly Scope parent;
            private Dictionary<Key,byte> seen;private readonly int bound;private readonly long began;private Totals totals;private bool disposed;
            internal Scope(PlayableAiObservation o,PlayableProfile p){
                long init=Stopwatch.GetTimestamp();began=init;observation=o;profile=p;parent=current;bound=(int)Math.Min(MaximumProofProfilePairs,Math.Max(1L,(long)o.RouteProofs.Count+1));
                totals=new Totals{Owner=o.OwnerId,Generation=o.Generation,LastTick=o.Tick,ProfileId=p.ProfileId,ProfileRevision=p.Revision,ProfileReference=RuntimeHelpers.GetHashCode(p),Reviews=1,MaxBound=bound};current=this;totals.ObserverTicks=Stopwatch.GetTimestamp()-init;
            }
            internal void Observe(PlayableAiObservation o,PlayableRouteProof r,PlayableProfile p,bool withdrawal){
                long begin=Stopwatch.GetTimestamp();try{
                    if(withdrawal)totals.WithdrawalCalls++;else totals.SafeCalls++;
                    if(!ReferenceEquals(o,observation)){totals.ForeignObservationCalls++;return;}
                    if(!ReferenceEquals(p,profile))totals.ForeignProfileCalls++;if(r==null)totals.NullRouteCalls++;
                    if(seen==null)seen=new Dictionary<Key,byte>(Math.Min(4,bound));
                    var key=new Key(r,p);byte bit=(byte)(withdrawal?2:1);
                    if(seen.TryGetValue(key,out byte state)){
                        if((state&bit)!=0){if(withdrawal)totals.RepeatedWithdrawal++;else totals.RepeatedSafe++;}
                        else{seen[key]=(byte)(state|bit);if(withdrawal)totals.UniqueWithdrawal++;else totals.UniqueSafe++;}
                    }else{
                        if(seen.Count==bound){totals.UntrackedCalls++;return;}seen.Add(key,bit);totals.UniquePairs++;if(withdrawal)totals.UniqueWithdrawal++;else totals.UniqueSafe++;
                    }
                }finally{totals.ObserverTicks+=Stopwatch.GetTimestamp()-begin;}
            }
            public void Dispose(){
                if(disposed)return;disposed=true;current=parent;totals.MaxPairs=seen?.Count??0;seen?.Clear();totals.ReviewTicks=Stopwatch.GetTimestamp()-began;
                long begin=Stopwatch.GetTimestamp();lock(gate){
                    int index=-1;for(int i=0;i<ownerCount;i++)if(owners[i].Owner==totals.Owner&&owners[i].Generation==totals.Generation){index=i;break;}
                    if(index<0){if(ownerCount==owners.Length){droppedOwners++;return;}index=ownerCount++;}
                    var old=owners[index];totals.Reviews+=old.Reviews;totals.SafeCalls+=old.SafeCalls;totals.WithdrawalCalls+=old.WithdrawalCalls;totals.UniquePairs+=old.UniquePairs;totals.UniqueSafe+=old.UniqueSafe;totals.UniqueWithdrawal+=old.UniqueWithdrawal;totals.RepeatedSafe+=old.RepeatedSafe;totals.RepeatedWithdrawal+=old.RepeatedWithdrawal;totals.UntrackedCalls+=old.UntrackedCalls;totals.ForeignObservationCalls+=old.ForeignObservationCalls;totals.ForeignProfileCalls+=old.ForeignProfileCalls;totals.NullRouteCalls+=old.NullRouteCalls;totals.ObserverTicks+=old.ObserverTicks+Stopwatch.GetTimestamp()-begin;totals.ReviewTicks+=old.ReviewTicks;totals.MaxPairs=Math.Max(totals.MaxPairs,old.MaxPairs);totals.MaxBound=Math.Max(totals.MaxBound,old.MaxBound);owners[index]=totals;
                }
            }
        }
        internal static Scope Begin(PlayableAiObservation o,PlayableProfile p)=>Enabled&&Eligible(o,p)?new Scope(o,p):null;
        internal static void Observe(PlayableAiObservation o,PlayableRouteProof r,PlayableProfile p,bool withdrawal)=>current?.Observe(o,r,p,withdrawal);
        public static Totals[] Snapshot(out int dropped){lock(gate){dropped=droppedOwners;var copy=new Totals[ownerCount];Array.Copy(owners,copy,ownerCount);return copy;}}
        public static int WriteCsv(string path){
            var rows=Snapshot(out int dropped);if(!Requested&&rows.Length==0)return 0;
            using(var w=new StreamWriter(path)){
                w.WriteLine("owner,generation,last_tick,profile,profile_revision,profile_reference,reviews,safe_calls,withdrawal_calls,unique_pairs_review_sum,unique_safe_review_sum,unique_withdrawal_review_sum,repeated_safe,repeated_withdrawal,untracked_calls,foreign_observation_calls,foreign_profile_calls,null_route_calls,max_pairs,max_bound,observer_wall_ms,review_wall_ms");
                foreach(var r in rows)w.WriteLine(string.Format(CultureInfo.InvariantCulture,"{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20:R},{21:R}",r.Owner,r.Generation,r.LastTick,r.ProfileId,r.ProfileRevision,r.ProfileReference,r.Reviews,r.SafeCalls,r.WithdrawalCalls,r.UniquePairs,r.UniqueSafe,r.UniqueWithdrawal,r.RepeatedSafe,r.RepeatedWithdrawal,r.UntrackedCalls,r.ForeignObservationCalls,r.ForeignProfileCalls,r.NullRouteCalls,r.MaxPairs,r.MaxBound,r.ObserverMilliseconds,r.ReviewMilliseconds));
                w.WriteLine("# owners="+rows.Length+",dropped="+dropped);
            }return dropped;
        }
#if UNITY_INCLUDE_TESTS
        internal static void ResetForTests(bool enabled){if(current!=null)throw new InvalidOperationException("Active QA scope.");lock(gate){Array.Clear(owners,0,owners.Length);ownerCount=droppedOwners=0;}EnabledForTests=enabled;}
#endif
    }
}
#endif
