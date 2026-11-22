using System;
namespace Spacewars.Simulation
{
    internal readonly struct NavQueryBox
    {
        internal readonly double Left,Bottom,Right,Top;
        internal NavQueryBox(NavPoint a,NavPoint b){Left=Math.Min(a.X,b.X);Bottom=Math.Min(a.Z,b.Z);Right=Math.Max(a.X,b.X);Top=Math.Max(a.Z,b.Z);}
    }
    internal sealed class NavQueryGrid
    {
        private readonly double half;
        private readonly int[] offsets,values;
        internal long Bytes=>Layout(values.Length);
        private static long Layout(int entries)=>checked(NavStorageLayout.ObjectBytesBound(typeof(NavQueryGrid))+32+4L*257+32+4L*entries);
        private static int Bucket(double coordinate,double half)=>(int)Math.Max(0,Math.Min(15,Math.Floor((coordinate+half)/(2*half)*16)));
        private static int Count(double half,int count,Func<int,NavQueryBox> box)
        {int total=0;for(int i=0;i<count;i++){var b=box(i);total=checked(total+(Bucket(b.Right,half)-Bucket(b.Left,half)+1)*(Bucket(b.Top,half)-Bucket(b.Bottom,half)+1));}return total;}
        internal static long Measure(double half,int count,Func<int,NavQueryBox> box)=>Layout(Count(half,count,box));
        internal NavQueryGrid(double half,int count,Func<int,NavQueryBox> box)
        {
            this.half=half;offsets=new int[257];
            for(int i=0;i<count;i++){var b=box(i);for(int x=Bucket(b.Left,half);x<=Bucket(b.Right,half);x++)for(int z=Bucket(b.Bottom,half);z<=Bucket(b.Top,half);z++)offsets[x*16+z+1]++;}
            for(int i=1;i<offsets.Length;i++)offsets[i]=checked(offsets[i]+offsets[i-1]);
            values=new int[offsets[256]];Span<int> cursor=stackalloc int[256];for(int i=0;i<256;i++)cursor[i]=offsets[i];
            for(int i=0;i<count;i++){var b=box(i);for(int x=Bucket(b.Left,half);x<=Bucket(b.Right,half);x++)for(int z=Bucket(b.Bottom,half);z<=Bucket(b.Top,half);z++)values[cursor[x*16+z]++]=i;}
        }
        internal static int LowestSetBit(ulong value)
        {
            int bit=0;
            if((value&0xffffffffUL)==0){bit+=32;value>>=32;}
            if((value&0xffffUL)==0){bit+=16;value>>=16;}
            if((value&0xffUL)==0){bit+=8;value>>=8;}
            if((value&0xfUL)==0){bit+=4;value>>=4;}
            if((value&3UL)==0){bit+=2;value>>=2;}
            if((value&1UL)==0)bit++;
            return bit;
        }
        internal Query Candidates(double left,double bottom,double right,double top)=>new Query(this,Bucket(left,half),Bucket(bottom,half),Bucket(right,half),Bucket(top,half));
        internal readonly struct Query
        {
            private readonly NavQueryGrid grid;private readonly int left,bottom,right,top;
            internal Query(NavQueryGrid grid,int left,int bottom,int right,int top){this.grid=grid;this.left=left;this.bottom=bottom;this.right=right;this.top=top;}
            public Enumerator GetEnumerator()=>new Enumerator(grid,left,bottom,right,top);
        }
        internal struct Enumerator
        {
            private readonly NavQueryGrid grid;private readonly int bottom,right,top;private int x,z,at,end;
            internal Enumerator(NavQueryGrid grid,int left,int bottom,int right,int top){this.grid=grid;this.bottom=bottom;this.right=right;this.top=top;x=left;z=bottom;at=grid.offsets[x*16+z];end=grid.offsets[x*16+z+1];Current=0;}
            public int Current{get;private set;}
            public bool MoveNext(){while(x<=right){if(at<end){Current=grid.values[at++];return true;}if(++z>top){z=bottom;x++;}if(x<=right){at=grid.offsets[x*16+z];end=grid.offsets[x*16+z+1];}}return false;}
        }
    }
}
