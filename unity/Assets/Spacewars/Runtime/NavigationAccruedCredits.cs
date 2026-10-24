using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // The bank is its own immutable publication root. Its fixed long ring uses
    // existing owned COW pages; appending never changes a solver's Host identity.
    internal static class NavigationAccruedCredits
    {
        internal const int Maximum=600, Header=12, Length=Header+4*Maximum;
        internal const int Version=0,Generation=1,Watermark=2,Cap=3,Head=4,Count=5,
            Accepted=6,Promoted=7,Discarded=8,SaturationDrops=9,ActiveRevision=10,Reserved=11;
        internal readonly struct Credit
        {
            internal readonly long Tick,Prefix,Revision;internal readonly int Budget;
            internal Credit(long tick,long prefix,int budget,long revision){Tick=tick;Prefix=prefix;Budget=budget;Revision=revision;}
        }
        internal static NavPagedList<long> Create(long generation,int cap,INavStorageReservation reservation)
        {
            if(generation<1||cap<1||cap>Maximum)throw new ArgumentException("Invalid credit bank identity/cap.");
            var bank=NavPagedList<long>.CreateOwned(reservation,256);
            try{for(int i=0;i<Length;i++)bank.Add(0);bank[Version]=1;bank[Generation]=generation;bank[Cap]=cap;bank.Seal();return bank;}
            catch{bank.ReleaseStorage();throw;}
        }
        private static int Slot(long index)=>checked(Header+4*(int)index);
        internal static Credit First(NavPagedList<long> bank)
        {if(bank[Count]==0)throw new InvalidOperationException("Empty credit bank.");int i=Slot(bank[Head]);return new Credit(bank[i],bank[i+1],checked((int)bank[i+2]),bank[i+3]);}
        // Local transition only. Publication must revalidate the exact source and
        // authority operands. Allocation failure discards the whole private fork.
        internal static NavPagedList<long> Account(NavPagedList<long> source,long tick,long prefix,int budget,long revision,int cap,bool demand,INavStorageReservation reservation)
        {
            if(tick<1||prefix<0||budget<1||budget>64||revision<1||cap<1||cap>Maximum)throw new ArgumentException("Invalid authority credit operands.");
            var next=source.ForkOwned(reservation);
            try{
                long count=Math.Min(source[Count],cap),dropped=source[Count]-count;
                if(!demand){dropped=source[Count];count=0;}
                next[Cap]=cap;next[Count]=count;next[Discarded]=checked(source[Discarded]+dropped);
                if(demand&&tick>source[Watermark]){
                    next[Watermark]=tick;
                    if(count==cap)next[SaturationDrops]=checked(source[SaturationDrops]+1);
                    else{
                        int i=Slot((source[Head]+count)%Maximum);
                        next[i]=tick;next[i+1]=prefix;next[i+2]=budget;next[i+3]=revision;
                        next[Count]=count+1;next[Accepted]=checked(source[Accepted]+1);
                    }
                }
                next.Seal();return next;
            }catch{next.ReleaseStorage();throw;}
        }
        internal static NavPagedList<long> Promote(NavPagedList<long> source,INavStorageReservation reservation)
        {
            var credit=First(source);var next=source.ForkOwned(reservation);
            try{next[Head]=(source[Head]+1)%Maximum;next[Count]=source[Count]-1;next[Promoted]=checked(source[Promoted]+1);next[ActiveRevision]=credit.Revision;next.Seal();return next;}
            catch{next.ReleaseStorage();throw;}
        }
        // Full validation belongs to capture/decode/untrusted prepared inputs,
        // never the ordinary O(1) authority append or worker promotion path.
        internal static void Validate(NavPagedList<long> bank,NavigationHostRoot host,long generation,long prefix,long worldTick)
        {
            if(bank==null||host==null||!host.AccruedCredits||!bank.IsImmutableCaptureRoot||bank.Count!=Length||bank.PageSize!=256||bank.Capacity!=2560||bank.DirectoryCapacity!=16||bank[Version]!=1||bank[Generation]!=generation||generation<1||bank[Cap]<1||bank[Cap]>Maximum||bank[Head]<0||bank[Head]>=Maximum||bank[Count]<0||bank[Count]>bank[Cap]||bank[Reserved]!=0||bank[Watermark]<0||bank[Watermark]>checked(worldTick+1)||bank[Accepted]<0||bank[Promoted]<0||bank[Discarded]<0||bank[SaturationDrops]<0||bank[Accepted]!=checked(bank[Promoted]+bank[Discarded]+bank[Count])||bank[Promoted]!=host.QuantumSerial||bank[ActiveRevision]<0||(bank[Promoted]==0)!=(bank[ActiveRevision]==0)||host.AdmissionPrefix>prefix||host.QuantumTick>worldTick+1||host.QuantumTick>bank[Watermark]||host.TickQuantum&&(host.QuantumBudget<1||host.QuantumBudget>64)||host.TickQuantum&&host.QuantumGeneration!=generation||host.TickQuantum!=(bank[Promoted]>0))throw new ArgumentException("Credit bank/host authority contradiction.");
            long tick=host.QuantumTick,previous=host.AdmissionPrefix;
            for(int n=0;n<bank[Count];n++){
                int i=Slot((bank[Head]+n)%Maximum);long t=bank[i],p=bank[i+1],b=bank[i+2],r=bank[i+3];
                if(t<=tick||t>bank[Watermark]||p<previous||p>prefix||b<1||b>64||r<1)throw new ArgumentException("Invalid saved FIFO credit.");tick=t;previous=p;
            }
        }
    }
}
