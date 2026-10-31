using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Newtonsoft.Json;

internal static class RouteCpu
{
    [StructLayout(LayoutKind.Sequential)] private struct Native { internal double User,System;internal ulong Process,Thread; }
    [DllImport("libroute_cpu.dylib",CallingConvention=CallingConvention.Cdecl)] private static extern int route_cpu_read(out Native value);
    [DllImport("libroute_cpu.dylib",CallingConvention=CallingConvention.Cdecl)] private static extern int route_cpu_stamp_size();
    internal sealed class Stamp {public double User,System,Process,Thread;public int ReadIndex;public double Rusage=>User+System;}
    internal sealed class Interval {public double ProcessSeconds,RusageSeconds,UserSeconds,SystemSeconds,CallingThreadSeconds,ProviderDifferenceSeconds;}
    private static Stamp previous;private static int reads;
    public const double AgreementAbsoluteSeconds=.02,AgreementRelative=.005;
    internal static Stamp Read()
    {
        if(route_cpu_stamp_size()!=Marshal.SizeOf(typeof(Native)))throw new Exception("Native CPU ABI size mismatch");
        int error=route_cpu_read(out var n);if(error!=0)throw new Exception("getrusage failed errno="+error);
        var value=new Stamp{User=n.User,System=n.System,Process=n.Process/1e9,Thread=n.Thread/1e9,ReadIndex=++reads};
        if(previous!=null){if(value.User<previous.User||value.System<previous.System||value.Process<previous.Process||value.Thread<previous.Thread)throw new Exception("Native CPU counter rollback: "+JsonConvert.SerializeObject(new{previous,value}));Delta(previous,value);}
        previous=value;return value;
    }
    internal static Interval Delta(Stamp a,Stamp b)
    {
        var d=new Interval{ProcessSeconds=b.Process-a.Process,RusageSeconds=b.Rusage-a.Rusage,UserSeconds=b.User-a.User,SystemSeconds=b.System-a.System,CallingThreadSeconds=b.Thread-a.Thread};
        d.ProviderDifferenceSeconds=Math.Abs(d.ProcessSeconds-d.RusageSeconds);
        if(d.ProcessSeconds<0||d.RusageSeconds<0||d.CallingThreadSeconds<0||d.ProviderDifferenceSeconds>AgreementAbsoluteSeconds+AgreementRelative*Math.Max(d.ProcessSeconds,d.RusageSeconds))throw new Exception("Native CPU providers disagree: "+JsonConvert.SerializeObject(d));
        return d;
    }
    internal static int Probe(string output)
    {
        Directory.CreateDirectory(output);var stamps=new List<Stamp>();var intervals=new List<Interval>();
        var a=Read();stamps.Add(a);Thread.Sleep(100);var b=Read();stamps.Add(b);intervals.Add(Delta(a,b));
        var workers=new List<Thread>();for(int i=0;i<2;i++){var t=new Thread(()=>{var clock=Stopwatch.StartNew();while(clock.Elapsed.TotalMilliseconds<250)Thread.SpinWait(10000);});workers.Add(t);t.Start();}
        var active=Read();stamps.Add(active);foreach(var worker in workers){while(!worker.Join(20))stamps.Add(Read());}var end=Read();stamps.Add(end);var burn=Delta(active,end);intervals.Add(burn);
        if(burn.ProcessSeconds<.02)throw new Exception("CPU probe did not observe nonvacuous worker CPU");
        File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new{passed=true,provider="macOS CLOCK_PROCESS_CPUTIME_ID + getrusage; calling-thread clock",AgreementAbsoluteSeconds,AgreementRelative,stamps,intervals},Formatting.Indented));Console.WriteLine("CPU_CLOCK_PASS "+JsonConvert.SerializeObject(burn));return 0;
    }
}
