using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Pure derived results, owned by one domain. Fixed capacity; no wire/history state.
    // A collision only evicts work and never substitutes a result for another ray.
    internal sealed class PlayableGroundFireCache
    {
        private struct Entry
        {
            internal long AX,AZ,BX,BZ,Radius;
            internal bool Present,Clear;
        }
        internal long Hits,Misses; // Optional diagnostics, outside authority/checkpoint state.
        private readonly Entry[] entries=new Entry[1024];
        private PlayableProfile profile;
        private static long Bits(double value)=>BitConverter.DoubleToInt64Bits(value);
        private static int Slot(long ax,long az,long bx,long bz,long radius)
        {
            unchecked{
                ulong h=(ulong)ax;h=h*397^(ulong)az;h=h*397^(ulong)bx;h=h*397^(ulong)bz;h=h*397^(ulong)radius;
                // Mix the exponent/high mantissa too: integer map coordinates often
                // have zero low bits and must not all compete for the same slot.
                h^=h>>33;h*=0xff51afd7ed558ccdUL;h^=h>>33;h*=0xc4ceb9fe1a85ec53UL;h^=h>>33;
                return (int)h&1023;
            }
        }
        internal bool TryGet(PlayableProfile terms,NavPoint a,NavPoint b,double radius,out bool clear)
        {
            if(!ReferenceEquals(profile,terms)){Array.Clear(entries,0,entries.Length);profile=terms;}
            long ax=Bits(a.X),az=Bits(a.Z),bx=Bits(b.X),bz=Bits(b.Z),r=Bits(radius);
            var e=entries[Slot(ax,az,bx,bz,r)];clear=e.Clear;
            return e.Present&&e.AX==ax&&e.AZ==az&&e.BX==bx&&e.BZ==bz&&e.Radius==r;
        }
        internal void Store(PlayableProfile terms,NavPoint a,NavPoint b,double radius,bool clear)
        {
            if(!ReferenceEquals(profile,terms)){Array.Clear(entries,0,entries.Length);profile=terms;}
            long ax=Bits(a.X),az=Bits(a.Z),bx=Bits(b.X),bz=Bits(b.Z),r=Bits(radius);
            entries[Slot(ax,az,bx,bz,r)]=new Entry{Present=true,AX=ax,AZ=az,BX=bx,BZ=bz,Radius=r,Clear=clear};
        }
    }
}
