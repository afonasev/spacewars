// QA-only, bounded clock/event observer for one ordinary threaded-host witness.
// This source is copied beside the temporary Army600 observer and never enters
// a runtime assembly. Callback work is primitive array writes only.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Spacewars.Runtime;
using Spacewars.Simulation;

internal static class OrdinaryHostProfileProbe
{
    [StructLayout(LayoutKind.Sequential)]
    struct Timespec { public long Seconds; public long Nanoseconds; }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint="clock_gettime", SetLastError=true)]
    static extern int ClockGetTime(int clock, out Timespec value);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint="clock_getres", SetLastError=true)]
    static extern int ClockGetResolution(int clock, out Timespec value);

    const int MonotonicClock=6, ProcessCpuClock=12, ThreadCpuClock=16;
    const int Capacity=262144;
    static readonly int[] Kinds=new int[Capacity], Threads=new int[Capacity], ClockErrors=new int[Capacity];
    static readonly long[] Mono=new long[Capacity], Process=new long[Capacity], Thread=new long[Capacity];
    static int count, overflow, preflightError, attached, preflightPassed;
    static int preflightManagedThread;
    static long resolutionMono,resolutionProcess,resolutionThread,calibrationMono,calibrationProcess,calibrationThread;
    static Action<string> savedService;
    static Action<NavArrivalStatus> savedBefore;
    static NavigationSession session;
    static readonly FieldInfo SessionField=typeof(NavigationRoutePort).GetField("session",BindingFlags.Instance|BindingFlags.NonPublic);
    static readonly FieldInfo ServiceField=typeof(NavigationSession).GetField("ArrivalServiceDiagnosticForQa",BindingFlags.Instance|BindingFlags.NonPublic);
    static readonly FieldInfo BeforeField=typeof(NavigationSession).GetField("BeforeArrivalAdvanceCommit",BindingFlags.Instance|BindingFlags.NonPublic);
    static readonly Action<string> ServiceDelegate=Service;
    static readonly Action<NavArrivalStatus> BeforeDelegate=Before;

    static long ReadClock(int clock, ref int error)
    {
        Timespec value;
        if(ClockGetTime(clock,out value)!=0||value.Seconds<0||value.Nanoseconds<0||value.Nanoseconds>=1000000000L||value.Seconds>(long.MaxValue-value.Nanoseconds)/1000000000L){error=clock;return 0;}
        return value.Seconds*1000000000L+value.Nanoseconds;
    }

    static bool Stamp(out long mono,out long process,out long thread,out int error)
    {
        error=0;mono=ReadClock(MonotonicClock,ref error);
        process=ReadClock(ProcessCpuClock,ref error);thread=ReadClock(ThreadCpuClock,ref error);
        return error==0;
    }

    // Called before attaching and before the first profiled Army request.
    internal static object Preflight()
    {
        if(IntPtr.Size!=8||Environment.Is64BitProcess==false){preflightError=1;return new{status="NotRun",reason="requires 64-bit process"};}
        long m0,p0,t0,m1,p1,t1;int e0=0,e1=0;
        int managed=Environment.CurrentManagedThreadId;
        if(!Stamp(out m0,out p0,out t0,out e0)||!Stamp(out m1,out p1,out t1,out e1)||
            e0!=0||e1!=0||managed!=Environment.CurrentManagedThreadId||m1<m0||p1<p0||t1<t0)
        {preflightError=e0!=0?e0:e1!=0?e1:2;return new{status="NotRun",reason="clock/thread preflight failed",error=preflightError};}
        // Fixed small empty-pair calibration. It is reported, never subtracted.
        int ea=0,eb=0,ec=0,ed=0,ex=0,ey=0;
        Timespec mr,pr,tr;
        if(ClockGetResolution(MonotonicClock,out mr)!=0||ClockGetResolution(ProcessCpuClock,out pr)!=0||ClockGetResolution(ThreadCpuClock,out tr)!=0||mr.Seconds<0||pr.Seconds<0||tr.Seconds<0||mr.Nanoseconds<0||pr.Nanoseconds<0||tr.Nanoseconds<0||mr.Nanoseconds>=1000000000L||pr.Nanoseconds>=1000000000L||tr.Nanoseconds>=1000000000L){preflightError=4;return new{status="NotRun",reason="clock resolution preflight failed",error=preflightError};}
        long a=ReadClock(MonotonicClock,ref ea),b=ReadClock(MonotonicClock,ref eb);
        long c=ReadClock(ProcessCpuClock,ref ec),d=ReadClock(ProcessCpuClock,ref ed);
        long x=ReadClock(ThreadCpuClock,ref ex),y=ReadClock(ThreadCpuClock,ref ey);
        if(ea!=0||eb!=0||ec!=0||ed!=0||ex!=0||ey!=0||b<a||d<c||y<x){preflightError=3;return new{status="NotRun",reason="clock calibration failed or negative delta",error=preflightError};}
        preflightManagedThread=managed;resolutionMono=mr.Seconds*1000000000L+mr.Nanoseconds;resolutionProcess=pr.Seconds*1000000000L+pr.Nanoseconds;resolutionThread=tr.Seconds*1000000000L+tr.Nanoseconds;calibrationMono=b-a;calibrationProcess=d-c;calibrationThread=y-x;
        Volatile.Write(ref preflightPassed,1);
        return new{status="PASS",architecture="arm64/64-bit",managedThread=managed,clockIds=new{monotonic=MonotonicClock,processCpu=ProcessCpuClock,threadCpu=ThreadCpuClock},clockResolution=new{monotonic=mr.Seconds*1000000000L+mr.Nanoseconds,processCpu=pr.Seconds*1000000000L+pr.Nanoseconds,threadCpu=tr.Seconds*1000000000L+tr.Nanoseconds},emptyPairNanoseconds=new{monotonic=b-a,process=d-c,thread=y-x},subtracted=false};
    }

    internal static bool PreflightPassed=>Volatile.Read(ref preflightPassed)==1;

    static int Kind(string name)
    {
        if(name=="advance-start")return 1;if(name=="scope-refused")return 2;
        if(name=="authority-bytes-refused")return 3;if(name=="advance-retention-blocked")return 4;
        if(name=="frontier-start")return 5;if(name=="frontier-end")return 6;
        if(name=="publish-start")return 7;if(name=="committed")return 8;
        if(name=="release-start")return 9;if(name=="release-end")return 10;
        if(name=="source-owner-refused")return 11;if(name=="closed-without-advance")return 12;
        if(name=="no-eligible")return 13;if(name=="prepared-admission-refused")return 14;
        return 0;
    }

    static void Record(int kind)
    {
        int index=Interlocked.Increment(ref count)-1;
        if(index<0||index>=Capacity){Interlocked.Exchange(ref overflow,1);return;}
        long m=0,p=0,t=0;int e=0;
        try{Stamp(out m,out p,out t,out e);Threads[index]=Environment.CurrentManagedThreadId;}
        catch{e=99;Interlocked.CompareExchange(ref preflightError,e,0);}
        Kinds[index]=kind;
        Mono[index]=m;Process[index]=p;Thread[index]=t;ClockErrors[index]=e;
        if(e!=0)Interlocked.CompareExchange(ref preflightError,e,0);
    }

    static void Service(string name){try{int kind=Kind(name);if(kind==0)Interlocked.CompareExchange(ref preflightError,97,0);Record(kind);}catch{Interlocked.CompareExchange(ref preflightError,96,0);}}
    static void Before(NavArrivalStatus status){try{Record(15);}catch{Interlocked.CompareExchange(ref preflightError,95,0);}}

    internal static void BeginCapture()=>Record(101);
    internal static void EndCapture()=>Record(102);
    internal static void BeginDecisionDto()=>Record(103);
    internal static void EndDecisionDto()=>Record(104);
    internal static void BeginObserverIo()=>Record(105);
    internal static void EndObserverIo()=>Record(106);
    internal static void GateRequested()=>Record(107);
    internal static void GateEntered()=>Record(108);
    internal static void GateExited()=>Record(109);
    internal static void ObservationEnd()=>Record(110);

    internal static void Attach(NavigationRoutePort port)
    {
        if(preflightError!=0||!PreflightPassed)throw new InvalidOperationException("Clock preflight failed; solver instrumentation not attached.");
        if(port==null||SessionField==null||ServiceField==null||BeforeField==null)throw new MissingMemberException("Arrival profile hook fields unavailable.");
        session=(NavigationSession)SessionField.GetValue(port);
        if(session==null)throw new InvalidOperationException("No navigation session to observe.");
        port.RunHostStep(()=>{
            savedService=(Action<string>)ServiceField.GetValue(session);
            savedBefore=(Action<NavArrivalStatus>)BeforeField.GetValue(session);
            if(savedService!=null||savedBefore!=null)throw new InvalidOperationException("Refusing to replace an existing QA arrival observer.");
            ServiceField.SetValue(session,ServiceDelegate);
            BeforeField.SetValue(session,BeforeDelegate);
            Volatile.Write(ref attached,1);
        });
    }

    internal static void Detach(NavigationRoutePort port)
    {
        if(Volatile.Read(ref attached)==0)return;
        port.RunHostStep(()=>{
            if(!ReferenceEquals(ServiceField.GetValue(session),ServiceDelegate)||
               !ReferenceEquals(BeforeField.GetValue(session),BeforeDelegate))
            {Interlocked.CompareExchange(ref preflightError,98,0);return;}
            ServiceField.SetValue(session,savedService);BeforeField.SetValue(session,savedBefore);
            Volatile.Write(ref attached,0);
        });
    }

    internal static object Snapshot()
    {
        int n=Math.Min(Volatile.Read(ref count),Capacity);
        var rows=new object[n];
        for(int i=0;i<n;i++)rows[i]=new{sequence=i,kind=Kinds[i],managedThread=Threads[i],monotonicNanoseconds=Mono[i],processCpuNanoseconds=Process[i],threadCpuNanoseconds=Thread[i],clockError=ClockErrors[i]};
        return new{schema="ordinary-host-profile-events-v1",status=Volatile.Read(ref overflow)==0&&preflightError==0&&PreflightPassed?"PASS":"PARTIAL",capacity=Capacity,count=n,overflow=Volatile.Read(ref overflow)!=0,preflightError,preflight=new{passed=PreflightPassed,managedThread=preflightManagedThread,architecture=IntPtr.Size==8?"64-bit":"not-64-bit",resolutionNanoseconds=new{monotonic=resolutionMono,processCpu=resolutionProcess,threadCpu=resolutionThread},emptyPairNanoseconds=new{monotonic=calibrationMono,processCpu=calibrationProcess,threadCpu=calibrationThread},subtracted=false},events=rows,clockIds=new{monotonic=MonotonicClock,processCpu=ProcessCpuClock,threadCpu=ThreadCpuClock},observerMeaning="CaptureRoot, DecisionDto, gate wait/hold and observer write/flush are same-thread event pairs through the post-worker-stop final causal drain; result JSON serialization/write is outside this stream. Clock reads are sequential; process CPU minus thread CPU is not GC attribution."};
    }
}
