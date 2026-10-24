using System;
using System.IO;
namespace Spacewars.Simulation
{
    // Exact query policies. Full binary64 exponent range, units 2^-1074.
    // <= int.MaxValue nodes => <2^32 atomic nonnegative weights per simple
    // candidate, each <2^2098 units. 2176 bits cover sum and temporary relax.
    // No heap scratch or variable precision. Every loop is <=34 iterations.
    internal struct NavExactCost : IComparable<NavExactCost>,IEquatable<NavExactCost>
    {
        internal const int Limbs=34,Bytes=272;
        private ulong v0,v1,v2,v3,v4,v5,v6,v7,v8,v9,v10,v11,v12,v13,v14,v15,v16,v17,v18,v19,v20,v21,v22,v23,v24,v25,v26,v27,v28,v29,v30,v31,v32,v33;
        internal ulong Limb(int n){switch(n){case 0:return v0;case 1:return v1;case 2:return v2;case 3:return v3;case 4:return v4;case 5:return v5;case 6:return v6;case 7:return v7;case 8:return v8;case 9:return v9;case 10:return v10;case 11:return v11;case 12:return v12;case 13:return v13;case 14:return v14;case 15:return v15;case 16:return v16;case 17:return v17;case 18:return v18;case 19:return v19;case 20:return v20;case 21:return v21;case 22:return v22;case 23:return v23;case 24:return v24;case 25:return v25;case 26:return v26;case 27:return v27;case 28:return v28;case 29:return v29;case 30:return v30;case 31:return v31;case 32:return v32;case 33:return v33;default:throw new ArgumentOutOfRangeException(nameof(n));}}
        private void Set(int n,ulong value){switch(n){case 0:v0=value;return;case 1:v1=value;return;case 2:v2=value;return;case 3:v3=value;return;case 4:v4=value;return;case 5:v5=value;return;case 6:v6=value;return;case 7:v7=value;return;case 8:v8=value;return;case 9:v9=value;return;case 10:v10=value;return;case 11:v11=value;return;case 12:v12=value;return;case 13:v13=value;return;case 14:v14=value;return;case 15:v15=value;return;case 16:v16=value;return;case 17:v17=value;return;case 18:v18=value;return;case 19:v19=value;return;case 20:v20=value;return;case 21:v21=value;return;case 22:v22=value;return;case 23:v23=value;return;case 24:v24=value;return;case 25:v25=value;return;case 26:v26=value;return;case 27:v27=value;return;case 28:v28=value;return;case 29:v29=value;return;case 30:v30=value;return;case 31:v31=value;return;case 32:v32=value;return;case 33:v33=value;return;default:throw new ArgumentOutOfRangeException(nameof(n));}}
        internal static NavExactCost FromDouble(double value)
        {
            ulong raw=unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
            if((raw>>63)!=0&&value!=0||((raw>>52)&2047)==2047)throw new ArgumentException("Exact cost needs finite nonnegative weight.");
            int exponent=(int)((raw>>52)&2047);ulong mantissa=raw&0xfffffffffffffUL;
            if(exponent!=0)mantissa|=1UL<<52;
            int shift=exponent==0?0:exponent-1,word=shift/64,offset=shift%64;
            var result=default(NavExactCost);result.Set(word,mantissa<<offset);
            if(offset>11)result.Set(word+1,mantissa>>(64-offset));return result;
        }
        internal NavExactCost Add(double value)=>Add(FromDouble(value));
        internal NavExactCost Add(NavExactCost value)
        {
            using var measured=PerformanceProfile.Sample(ProfileStage.ExactAdd);
            var result=default(NavExactCost);ulong carry=0;
            for(int n=0;n<Limbs;n++){ulong a=Limb(n),b=value.Limb(n),sum=unchecked(a+b),first=sum<a?1UL:0UL,withCarry=unchecked(sum+carry);carry=first|(withCarry<sum?1UL:0UL);result.Set(n,withCarry);}
            if(carry!=0)throw new ArgumentException("Exact path cost exceeded proven node/weight bound.");return result;
        }
        internal NavExactCost Subtract(NavExactCost value)
        {
            using var measured=PerformanceProfile.Sample(ProfileStage.ExactSubtract);
            var result=default(NavExactCost);ulong borrow=0;
            for(int n=0;n<Limbs;n++){ulong a=Limb(n),b=value.Limb(n),difference=unchecked(a-b),next=unchecked(difference-borrow);ulong first=a<b?1UL:0UL;borrow=first|(difference<borrow?1UL:0UL);result.Set(n,next);}
            if(borrow!=0)throw new ArgumentException("Negative exact difference.");return result;
        }
        internal NavExactCost CeilDivide2Pow20()
        {
            var result=default(NavExactCost);bool bump=(v0&0xfffffUL)!=0;
            for(int n=0;n<Limbs;n++){ulong word=(Limb(n)>>20)|(n+1<Limbs?Limb(n+1)<<44:0);if(bump){word=unchecked(word+1);bump=word==0;}result.Set(n,word);}return result;
        }
        internal static NavExactCost Difference(double a,double b)
        {
            var x=FromDouble(Math.Abs(a));var y=FromDouble(Math.Abs(b));
            return (a<0)!=(b<0)?x.Add(y):x>=y?x.Subtract(y):y.Subtract(x);
        }
        internal static NavExactCost ConservativeHeuristicScale(NavExactCost geometry)
        {
            var scaled=geometry.Subtract(geometry.CeilDivide2Pow20());
            // S=2^-498, bit576 in 2^-1074 units. Exact, no balance setting.
            var slack=default(NavExactCost);slack.v9=1;
            return scaled<=slack?default:scaled.Subtract(slack);
        }
                public int CompareTo(NavExactCost other)
        {
            using var measured=PerformanceProfile.Sample(ProfileStage.ExactCompare);
            // Same unsigned lexicographic order; avoid indexed Limb dispatch.
            if(v33!=other.v33)return v33<other.v33? -1:1;
            if(v32!=other.v32)return v32<other.v32? -1:1;
            if(v31!=other.v31)return v31<other.v31? -1:1;
            if(v30!=other.v30)return v30<other.v30? -1:1;
            if(v29!=other.v29)return v29<other.v29? -1:1;
            if(v28!=other.v28)return v28<other.v28? -1:1;
            if(v27!=other.v27)return v27<other.v27? -1:1;
            if(v26!=other.v26)return v26<other.v26? -1:1;
            if(v25!=other.v25)return v25<other.v25? -1:1;
            if(v24!=other.v24)return v24<other.v24? -1:1;
            if(v23!=other.v23)return v23<other.v23? -1:1;
            if(v22!=other.v22)return v22<other.v22? -1:1;
            if(v21!=other.v21)return v21<other.v21? -1:1;
            if(v20!=other.v20)return v20<other.v20? -1:1;
            if(v19!=other.v19)return v19<other.v19? -1:1;
            if(v18!=other.v18)return v18<other.v18? -1:1;
            if(v17!=other.v17)return v17<other.v17? -1:1;
            if(v16!=other.v16)return v16<other.v16? -1:1;
            if(v15!=other.v15)return v15<other.v15? -1:1;
            if(v14!=other.v14)return v14<other.v14? -1:1;
            if(v13!=other.v13)return v13<other.v13? -1:1;
            if(v12!=other.v12)return v12<other.v12? -1:1;
            if(v11!=other.v11)return v11<other.v11? -1:1;
            if(v10!=other.v10)return v10<other.v10? -1:1;
            if(v9!=other.v9)return v9<other.v9? -1:1;
            if(v8!=other.v8)return v8<other.v8? -1:1;
            if(v7!=other.v7)return v7<other.v7? -1:1;
            if(v6!=other.v6)return v6<other.v6? -1:1;
            if(v5!=other.v5)return v5<other.v5? -1:1;
            if(v4!=other.v4)return v4<other.v4? -1:1;
            if(v3!=other.v3)return v3<other.v3? -1:1;
            if(v2!=other.v2)return v2<other.v2? -1:1;
            if(v1!=other.v1)return v1<other.v1? -1:1;
            if(v0!=other.v0)return v0<other.v0? -1:1;
            return 0;
        }
        public bool Equals(NavExactCost other)=>CompareTo(other)==0;
        public override bool Equals(object other)=>other is NavExactCost value&&Equals(value);
        public override int GetHashCode()=>unchecked((int)v0^(int)v33);
        public static bool operator ==(NavExactCost a,NavExactCost b)=>a.Equals(b);
        public static bool operator !=(NavExactCost a,NavExactCost b)=>!a.Equals(b);
        public static bool operator <(NavExactCost a,NavExactCost b)=>a.CompareTo(b)<0;
        public static bool operator >(NavExactCost a,NavExactCost b)=>a.CompareTo(b)>0;
        public static bool operator <=(NavExactCost a,NavExactCost b)=>a.CompareTo(b)<=0;
        public static bool operator >=(NavExactCost a,NavExactCost b)=>a.CompareTo(b)>=0;
        internal void Write(BinaryWriter w){for(int n=0;n<Limbs;n++)w.Write(Limb(n));}
        internal static NavExactCost Read(BinaryReader r){var value=default(NavExactCost);for(int n=0;n<Limbs;n++)value.Set(n,r.ReadUInt64());if(value.Limb(33)>>18!=0)throw new ArgumentException("Exact value exceeds path-term bound.");return value;}
    }
    internal struct NavExactQueueEntry : IComparable<NavExactQueueEntry>
    {
        internal int Node;internal NavExactCost Cost;
        public int CompareTo(NavExactQueueEntry other){int c=Cost.CompareTo(other.Cost);return c!=0?c:Node.CompareTo(other.Node);}
        internal void Write(BinaryWriter w){w.Write(Node);Cost.Write(w);}
        internal static NavExactQueueEntry Read(BinaryReader r)=>new NavExactQueueEntry{Node=r.ReadInt32(),Cost=NavExactCost.Read(r)};
    }
    // Reference casts below cast delegates once per closed generic, never box
    // a 272-byte value at the generic wire boundary.
    internal static class NavPhysicalExactWire<T>
    {
        internal static readonly Action<NavPhysicalBodyWriter,T> Write=typeof(T)==typeof(NavExactCost)?(Action<NavPhysicalBodyWriter,T>)(object)(Action<NavPhysicalBodyWriter,NavExactCost>)WriteCost:typeof(T)==typeof(NavExactQueueEntry)?(Action<NavPhysicalBodyWriter,T>)(object)(Action<NavPhysicalBodyWriter,NavExactQueueEntry>)WriteEntry:null;
        internal static readonly Func<NavPhysicalBodyReader,T> Read=typeof(T)==typeof(NavExactCost)?(Func<NavPhysicalBodyReader,T>)(object)(Func<NavPhysicalBodyReader,NavExactCost>)ReadCost:typeof(T)==typeof(NavExactQueueEntry)?(Func<NavPhysicalBodyReader,T>)(object)(Func<NavPhysicalBodyReader,NavExactQueueEntry>)ReadEntry:null;
        private static void WriteCost(NavPhysicalBodyWriter w,NavExactCost v)=>v.Write(w.Output);
        private static NavExactCost ReadCost(NavPhysicalBodyReader r)=>NavExactCost.Read(r.Input);
        private static void WriteEntry(NavPhysicalBodyWriter w,NavExactQueueEntry v)=>v.Write(w.Output);
        private static NavExactQueueEntry ReadEntry(NavPhysicalBodyReader r)=>NavExactQueueEntry.Read(r.Input);
    }
}
