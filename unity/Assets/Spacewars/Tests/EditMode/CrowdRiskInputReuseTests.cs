using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class CrowdRiskInputReuseTests
    {
        sealed class Clearance : INavMotionClearance
        {
            internal readonly List<long> Trace;
            internal readonly int Mode;
            internal int Calls;internal ulong Hash=1469598103934665603UL;
            internal Clearance(int mode,bool trace){Mode=mode;if(trace)Trace=new List<long>();}
            public bool ClearMotion(int actor,NavPoint end){
                long x=BitConverter.DoubleToInt64Bits(end.X),z=BitConverter.DoubleToInt64Bits(end.Z);
                Trace?.Add(actor);Trace?.Add(x);Trace?.Add(z);unchecked{Hash=(Hash^(ulong)actor)*1099511628211UL;Hash=(Hash^(ulong)x)*1099511628211UL;Hash=(Hash^(ulong)z)*1099511628211UL;}Calls++;
                return Mode==0||Mode==2&&Calls%3!=0||Mode==3&&end.X<=10&&end.Z>=-10;
            }
        }
        static byte[] ProposalBytes(NavMotionProposal[] proposals,Clearance clearance)
        {
            using(var s=new MemoryStream())using(var w=new BinaryWriter(s)){
                foreach(var p in proposals){w.Write(BitConverter.DoubleToInt64Bits(p.Position.X));w.Write(BitConverter.DoubleToInt64Bits(p.Position.Z));w.Write(BitConverter.DoubleToInt64Bits(p.Velocity.X));w.Write(BitConverter.DoubleToInt64Bits(p.Velocity.Z));w.Write(BitConverter.DoubleToInt64Bits(p.Heading));w.Write(p.Blocked);w.Write(p.Overflow);w.Write(p.Refuse);w.Write(p.StaticNoWinner);w.Write(p.IndependentTurn);}
                w.Write(clearance.Calls);w.Write(clearance.Hash);return s.ToArray();
            }
        }
        static NavMotionBody[] Bodies(int count)
        {
            var a=new NavMotionBody[count];for(int i=0;i<count;i++){
                int cohort=i/50,local=i%50;double angle=cohort*Math.PI/4;var p=new NavPoint(Math.Cos(angle)*88+local%7*2.2,Math.Sin(angle)*88+local/7*2.2);
                a[i]=new NavMotionBody{Id=i+1,Position=p,Target=new NavPoint(p.X+20,p.Z+7),Radius=.72,Speed=4,TurnSpeed=4.5,Active=i%7!=0,Potential=true,Heading=.2,Yielding=i%9==0};
            }return a;
        }
        static void Compare(NavMotionBody[] bodies,int[] neighbors,int count,int mode,NavPoint? goal,string label)
        {
            var a=new Clearance(mode,true);var b=new Clearance(mode,true);var profile=PlayableProfile.Default.Navigation;
            var old=NavCrowdMotion.SelectRiskPredecessorForQa(bodies,0,neighbors,count,profile,1d/30,a,goal);
            var next=NavCrowdMotion.Select(bodies,0,neighbors,count,profile,1d/30,b,goal);
            CollectionAssert.AreEqual(ProposalBytes(new[]{old},a),ProposalBytes(new[]{next},b),label+" proposal bits/flags and clearance hash/count");
            CollectionAssert.AreEqual(a.Trace,b.Trace,label+" exact ordered clearance arguments");
        }
        [Test] public void ExactProposalBitsFlagsTiesReverseAndClearanceOrder()
        {
            var bodies=Bodies(40);var neighbors=new int[40*32];for(int k=0;k<33;k++)neighbors[k]=k+1;
            for(int scenario=0;scenario<24;scenario++){
                bodies[0].Active=scenario!=0;bodies[0].Speed=scenario==1?0:scenario==2?-1:scenario==3?1e-150:4;
                bodies[0].Heading=scenario%3==0?Math.PI:.2;bodies[0].Target=scenario%4==0?bodies[0].Position:new NavPoint(bodies[0].Position.X+1,bodies[0].Position.Z);
                bodies[0].ReverseDistance=scenario%2==0?3:0;bodies[0].ReverseSpeedRatio=.5;bodies[0].ReverseConeRadians=.4;
                for(int i=1;i<bodies.Length;i++){bodies[i].Active=scenario%3!=0;bodies[i].Target=scenario%5==0?bodies[i].Position:new NavPoint(bodies[i].Position.X-10,bodies[i].Position.Z);}
                foreach(int count in new[]{-1,0,1,8,32,33})foreach(int mode in new[]{0,1,2,3})foreach(bool assigned in new[]{false,true})
                    Compare(bodies,neighbors,count,mode,assigned?bodies[0].Target:(NavPoint?)null,"scenario="+scenario+" count="+count+" mode="+mode+" assigned="+assigned);
            }
        }
        [Test] public void ExactRiskNanInfinitySignedZeroAndExtremeInputs()
        {
            double negzero=BitConverter.Int64BitsToDouble(long.MinValue),nan=BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000042UL));
            var values=new[]{0d,negzero,double.Epsilon,-double.Epsilon,1e-150,-1e-150,1e150,-1e150,double.MaxValue,double.MinValue,double.PositiveInfinity,double.NegativeInfinity,nan};
            foreach(double v in values)for(int field=0;field<5;field++){
                var bodies=Bodies(3);bodies[0].Active=true;var peer=bodies[1];
                if(field==0)peer.Position=new NavPoint(v,-v);if(field==1)peer.Target=new NavPoint(v,-v);if(field==2)peer.Radius=v;if(field==3)peer.Speed=v;if(field==4){peer.Active=false;peer.Target=new NavPoint(v,-v);}
                bodies[1]=peer;var neighbors=new int[96];neighbors[0]=1;neighbors[1]=2;
                Compare(bodies,neighbors,2,0,bodies[0].Target,"field="+field+" bits="+BitConverter.DoubleToInt64Bits(v));
            }
        }
        [Test] public void WholeSelectFixed400PredecessorCandidatePredecessor()
        {
            const int n=400,repeats=8;var bodies=Bodies(n);var neighbors=new int[n*32];var counts=new int[n];
            for(int row=0;row<n;row++){
                // Explicit bounded snapshot, unchanged for all A/B/A arms.
                counts[row]=row%11==0?0:row%5==0?8:32;
                for(int k=0;k<counts[row];k++)neighbors[row*32+k]=row/50*50+(row%50+k+1)%50;
            }
            var profile=PlayableProfile.Default.Navigation;var results=new NavMotionProposal[n];byte[] expected=null;
            var cpu=(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),typeof(PerformanceProfile).GetMethod("CpuNow",BindingFlags.Static|BindingFlags.NonPublic));
            string output=Environment.GetEnvironmentVariable("SPACEWARS_RISK_OUTPUT");if(output!=null)Directory.CreateDirectory(output);var metrics=new List<string>();
            long controlBefore=GC.GetAllocatedBytesForCurrentThread();var positive=new byte[1048576];long control=GC.GetAllocatedBytesForCurrentThread()-controlBefore;positive[0]=1;
            for(int arm=0;arm<3;arm++){
                bool candidate=arm==1;var clearance=new Clearance(0,false);
                for(int row=0;row<n;row++)results[row]=candidate?NavCrowdMotion.Select(bodies,row,neighbors,counts[row],profile,1d/30,clearance,bodies[row].Target):NavCrowdMotion.SelectRiskPredecessorForQa(bodies,row,neighbors,counts[row],profile,1d/30,clearance,bodies[row].Target);
                clearance.Calls=0;clearance.Hash=1469598103934665603UL;
                long allocated=GC.GetAllocatedBytesForCurrentThread(),c=cpu(),start=Stopwatch.GetTimestamp();
                for(int repeat=0;repeat<repeats;repeat++)for(int row=0;row<n;row++)results[row]=candidate?NavCrowdMotion.Select(bodies,row,neighbors,counts[row],profile,1d/30,clearance,bodies[row].Target):NavCrowdMotion.SelectRiskPredecessorForQa(bodies,row,neighbors,counts[row],profile,1d/30,clearance,bodies[row].Target);
                long ticks=Stopwatch.GetTimestamp()-start,usedCpu=cpu()-c,used=GC.GetAllocatedBytesForCurrentThread()-allocated;var bytes=ProposalBytes(results,clearance);
                if(expected==null)expected=bytes;else CollectionAssert.AreEqual(expected,bytes,"Whole Select final proposal bits and complete repeated clearance call hash/count");
                using(var sha=SHA256.Create()){
                    string metric=string.Format(CultureInfo.InvariantCulture,"arm={0},candidate={1},selects={2},wallMs={3:R},cpuMs={4:R},allocated={5},allocationControl={6},clearanceCalls={7},sha256={8}",arm,candidate,n*repeats,ticks*1000d/Stopwatch.Frequency,usedCpu/1000000d,used,control,clearance.Calls,BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());metrics.Add(metric);TestContext.WriteLine(metric);
                }
                if(output!=null)File.WriteAllBytes(Path.Combine(output,"arm"+arm+".proposals"),bytes);
            }
            if(output!=null)File.WriteAllLines(Path.Combine(output,"metrics.txt"),metrics);GC.KeepAlive(positive);
        }
    }
}
