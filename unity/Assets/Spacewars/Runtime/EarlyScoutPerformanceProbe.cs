#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Opt-in bounded diagnostics, outside gameplay/save state. No I/O on authority/host ticks.
    public sealed class EarlyScoutPerformanceProbe
    {
        public struct TickRow {public long Tick,Stamp,Allocated,Polls;public int Geometry,Obstacles,Pending,Gc0;public double ActiveMs,SleepMs,BarrierMs,CpuMs,AuthorityMs,DomainMs,AiMs,JournalMs,PublishMs;}
        public struct RequestRow {public long Tick,Stamp,Request,Order,Group,Revision;public int Actor,Geometry,Kind;public NavPoint Start,Goal;}
        private readonly TickRow[] ticks=new TickRow[4096];private readonly RequestRow[] requests=new RequestRow[8192];
        private int tickCount,requestCount;private long polls;private double sleepMs,barrierMs;
        internal EarlyScoutPerformanceProbe(PlayableDomain domain)
        {
            void Add(int kind,int actor,NavigationRequest q){int at=Interlocked.Increment(ref requestCount)-1;if(at>=requests.Length)return;
                requests[at]=new RequestRow{Tick=domain.Tick,Stamp=Stopwatch.GetTimestamp(),Actor=actor,Geometry=domain.Geometry.Revision,Kind=kind,Request=q?.Request??0,Order=q?.Order??0,Group=q?.MemberIdentity?.GroupId??0,Revision=q?.MemberIdentity?.OrderRevision??0,Start=q?.Start??default,Goal=q?.Goal??default};}
            domain.Navigation.RouteRequested+=q=>Add(0,q.Entity,q);domain.Navigation.RouteActivated+=q=>Add(1,q.Entity,q);
            domain.Navigation.RouteFailed+=(q,o)=>Add(10+(int)o,q.Entity,q);domain.Navigation.MovementCancelled+=id=>Add(2,id,null);
        }
        private long cpuAt;private readonly long[] phaseAt=new long[5];
        private static readonly ProfileStage[] phases={ProfileStage.Authority,ProfileStage.Domain,ProfileStage.Ai,ProfileStage.JournalCapture,ProfileStage.RuntimePublish};
        internal void BeginTick(){cpuAt=PerformanceProfile.ThreadCpuNanoseconds();for(int i=0;i<phases.Length;i++)phaseAt[i]=PerformanceProfile.ThreadWallTicks(phases[i]);}
        private double Phase(int i)=>(PerformanceProfile.ThreadWallTicks(phases[i])-phaseAt[i])*1000d/Stopwatch.Frequency;
        internal void Sleep(double ms){sleepMs+=ms;}
        internal void Barrier(double ms){barrierMs+=ms;polls++;}
        internal void Tick(PlayableDomain d,double active,long allocated){int at=tickCount++;if(at>=ticks.Length)return;
            ticks[at]=new TickRow{Tick=d.Tick,Stamp=Stopwatch.GetTimestamp(),Allocated=allocated==0?-1:allocated,Geometry=d.Geometry.Revision,Obstacles=d.Geometry.Obstacles.Count,Pending=d.Navigation.PendingCount,Gc0=GC.CollectionCount(0),ActiveMs=active,SleepMs=sleepMs,BarrierMs=barrierMs,Polls=polls,CpuMs=cpuAt<0?-1:(PerformanceProfile.ThreadCpuNanoseconds()-cpuAt)/1e6,AuthorityMs=Phase(0),DomainMs=Phase(1),AiMs=Phase(2),JournalMs=Phase(3),PublishMs=Phase(4)};}
        public object Capture()=>new{frequency=Stopwatch.Frequency,ticks=ticks.Take(Math.Min(tickCount,ticks.Length)).ToArray(),requests=requests.Take(Math.Min(requestCount,requests.Length)).ToArray(),tickDropped=Math.Max(0,tickCount-ticks.Length),requestDropped=Math.Max(0,requestCount-requests.Length),sleepMs,barrierMs,polls};
    }
    public sealed partial class PlayableRuntime
    {
        private EarlyScoutPerformanceProbe earlyScoutProbe;
        public EarlyScoutPerformanceProbe EnableEarlyScoutPerformanceProbe(){lock(gate)return earlyScoutProbe??(earlyScoutProbe=new EarlyScoutPerformanceProbe(domain));}
    }
}
#endif
