using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace Spacewars.Simulation
{
    // Opt-in diagnostics only. No Unity, gameplay state, callbacks, or per-call allocations.
    // Inclusive timings: children must never be added to their parent. CPU excludes waits/GC on other threads.
    public enum ProfileStage { Authority, Delivery, Domain, PlayerView, Ai, AiProjection, TeamBoard, TeamPublish,
        AiBudget, AiReview, AiRecord, Observation, Knowledge, Coverage, Vision, Snapshot, Routes, ObservedPath,
        Artillery, Contacts, GroundFire, Movement, Firing, Projectiles, Queues, Economy, RosterAccess, RosterBuild, RouteWorker, ValueLayout, ObjectLayout, HostFork, HostSeal, HostPublish, PhysicalSolve, PhysicalArrival, PhysicalProjection, VisionRefresh, VisionReuse, SnapshotUnits, SnapshotBuildings, SnapshotPublic, SnapshotRequests, SnapshotEnvelope, ContactDetect, ContactPublish, SchedulerAdvance, ArrivalFork, ArrivalAdvance, ArrivalSeal, ArrivalFrontier, DemandBookkeeping, DemandGraph, DemandConnector, RetentionReserve, RetentionRegister, RetentionRelease, TreeMutation, ExactAdd, ExactSubtract, ExactCompare, GeometrySweep, GeometryTrace, ArrivalSchedulerAdvance, AiReviewGuard, AiReviewFinance, AiReviewArmies, AiReviewFunding, AiReviewExpansion, AiReviewOpening, AiReviewDecision, SnapshotPayload, CrowdNeighbors, CrowdScale, CrowdSelect, CrowdYield, CrowdJoint, Presentation, WorldFog, WorldMemories, WorldSites, UnitMotion, WorldAnimation, WorldUnits, WorldBuildings, WorldContacts, WorldDestruction, WorldRemoval, WorldWrecks, WorldTrails, WorldRally, WorldProjectiles, ContactOwnedTotal, ContactSource, ContactScopeAdmission, ContactScopeLifetime, ContactPrepareEnvelope, ContactDtoAndGuards, ContactMemberCopy, ContactGroupCow, ContactWriteCarrier, ContactAdmissionCheck, ContactAdmissionPrepare, ContactPublishEnvelope, ContactFinalGuards, ContactApply, ContactResolveRetire, ContactOwnerCleanup, ContactSourceRelease, Count }
    public enum ProfileWork { OrdinaryConsumed, ArrivalConsumed, ArrivalCandidate, ArrivalOccupancy, ArrivalDirect, SnapshotUnits, SnapshotBuildings, ContactMembers, ArrivalRouteConsumed, Count }
    public static class PerformanceProfile
    {
        public static readonly bool Enabled=Environment.GetEnvironmentVariable("SPACEWARS_PRECISE_PROFILE")=="1";
        // Select coarse scopes before either clock read. This permits ordinary
        // runtime diagnosis without timing every geometry/layout predicate.
        private static readonly bool[] selectedStages=ReadStages();
        private static bool[] ReadStages()
        {
            string text=Environment.GetEnvironmentVariable("SPACEWARS_PROFILE_STAGES");
            if(!Enabled||string.IsNullOrWhiteSpace(text))return null;
            var result=new bool[(int)ProfileStage.Count];int count=0;
            foreach(string token in text.Split(new[]{','},StringSplitOptions.RemoveEmptyEntries)){
                if(!Enum.TryParse(token.Trim(),out ProfileStage stage)||(int)stage<0||stage>=ProfileStage.Count)throw new ArgumentException("Unknown CPU profile stage: "+token);
                result[(int)stage]=true;count++;
            }
            if(count==0)throw new ArgumentException("CPU profile stage selection is empty.");
            return result;
        }
        private static bool Selected(ProfileStage stage)=>Enabled&&(selectedStages==null||selectedStages[(int)stage]);
        private static readonly bool Darwin=RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        [DllImport("/usr/lib/libSystem.B.dylib")] private static extern ulong clock_gettime_nsec_np(int clock);
        private static long CpuNow()=>Darwin?(long)clock_gettime_nsec_np(16):-1; // macOS SDK CLOCK_THREAD_CPUTIME_ID
        [ThreadStatic] private static Accumulator current;
        private sealed class Accumulator
        {
            internal readonly long[] Count=new long[(int)ProfileStage.Count],Wall=new long[(int)ProfileStage.Count],Cpu=new long[(int)ProfileStage.Count],Max=new long[(int)ProfileStage.Count];
            internal readonly long[] Attempts=new long[(int)ProfileStage.Count];
            internal readonly long[] Work=new long[(int)ProfileWork.Count];
            internal long NextDump=Stopwatch.GetTimestamp()+10*Stopwatch.Frequency;
        }
        public readonly struct Scope : IDisposable
        {
            private readonly int stage;private readonly long wall,cpu;private readonly bool active;
            internal Scope(ProfileStage stage){active=Selected(stage);this.stage=(int)stage;wall=active?Stopwatch.GetTimestamp():0;cpu=active?CpuNow():-1;if(active&&current==null)current=new Accumulator();}
            public void Dispose(){if(!active)return;long cpuEnd=CpuNow(),elapsed=Stopwatch.GetTimestamp()-wall;var a=current;
                a.Count[stage]++;a.Wall[stage]+=elapsed;if(cpu>=0)a.Cpu[stage]+=cpuEnd-cpu;a.Max[stage]=Math.Max(a.Max[stage],elapsed);
                if((stage==(int)ProfileStage.Authority||stage==(int)ProfileStage.RouteWorker||stage==(int)ProfileStage.Presentation)&&Stopwatch.GetTimestamp()>=a.NextDump)Flush();}
        }
        public static Scope Measure(ProfileStage stage)=>new Scope(stage);
        // First and every 1024th invocation per stage/thread. Report attempts
        // separately: measured CPU is sampled, never an all-call total.
        public static Scope Sample(ProfileStage stage)
        {
            if(!Selected(stage))return default;
            if(current==null)current=new Accumulator();
            long attempt=++current.Attempts[(int)stage];
            return (attempt&1023)==1?new Scope(stage):default;
        }
        public static void Add(ProfileWork work,long amount)
        {if(!Enabled)return;if(current==null)current=new Accumulator();current.Work[(int)work]+=amount;}
        public static void Flush()
        {
            var a=current;if(!Enabled||a==null)return;long stamp=Stopwatch.GetTimestamp();a.NextDump=stamp+10*Stopwatch.Frequency;
            for(int i=0;i<(int)ProfileWork.Count;i++)if(a.Work[i]!=0)Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "PRECISE_WORK thread={0} stamp={1} work={2} total={3}",Thread.CurrentThread.ManagedThreadId,stamp,(ProfileWork)i,a.Work[i]));
            for(int i=0;i<(int)ProfileStage.Count;i++)if(a.Count[i]>0)Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "PRECISE_CPU thread={0} stamp={1} frequency={2} stage={3} calls={4} wall_ms={5:F6} cpu_ms={6:F6} max_ms={7:F6} attempts={8}",
                Thread.CurrentThread.ManagedThreadId,stamp,Stopwatch.Frequency,(ProfileStage)i,a.Count[i],a.Wall[i]*1000d/Stopwatch.Frequency,Darwin?a.Cpu[i]/1000000d:-1,a.Max[i]*1000d/Stopwatch.Frequency,a.Attempts[i]));
        }
    }
}
