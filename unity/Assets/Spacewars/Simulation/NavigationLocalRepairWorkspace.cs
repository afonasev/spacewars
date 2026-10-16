using System;
using System.Collections;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    // One finite lattice reused sequentially by repairing actors in a crowd pass.
    // The open array preserves insertion order and stable removal, including ties.
    internal sealed class NavLocalRepairWorkspace : NavOwnedStorage,IReadOnlyList<NavPoint>
    {
        internal struct Node
        {
            internal NavLocation Location;
            internal double Cost,Score,Side;
            internal int X,Z,Parent;
            internal bool Seen,Closed;
        }
        private static readonly long HeaderBytes=NavStorageLayout.ObjectBytesBound(typeof(NavLocalRepairWorkspace)),NodeBytes=NavStorageLayout.ValueBytes(typeof(Node));
        private static long Align(long bytes)=>checked((bytes+7)/8*8);
        private static int Slots(int radius)=>checked((2*radius+1)*(2*radius+1));
        private static long Layout(int radius)=>checked(HeaderBytes+4*32L+Align(Slots(radius)*NodeBytes)+Align(4L*Slots(radius))+32L*Slots(radius));
        internal readonly Node[] Nodes;
        internal readonly int[] Open;
        internal readonly NavPoint[] Reversed,Smoothed;
        internal readonly int Radius,Side;
        internal int OpenCount,RouteCount;
        private NavOwnedStorage surfaceNames;
        private NavLocalRepairWorkspace(int radius):base(Layout(radius))
        {
            Radius=radius;Side=checked(2*radius+1);int slots=Slots(radius);
            Nodes=new Node[slots];Open=new int[slots];Reversed=new NavPoint[slots];Smoothed=new NavPoint[slots];Retain();
        }
        internal static NavLocalRepairWorkspace Create(int radius,Func<long,bool> reserve,NavOwnedStorage names)
        {
            if(radius<0)throw new ArgumentOutOfRangeException(nameof(radius));
            if(names!=null){if(reserve!=null)NavStorageAdmission.RequireLedger(reserve.Target,names);names.Retain();}
            try{using(var allocation=new NavStorageAllocation(reserve)){
                allocation.Reserve(Layout(radius));var root=allocation.Register(new NavLocalRepairWorkspace(radius));
                root.surfaceNames=names;names=null;return root;
            }}finally{names?.Release();}
        }
        internal int Index(int x,int z)=>checked((x+Radius)*Side+z+Radius);
        internal void Reset(){Array.Clear(Nodes,0,Nodes.Length);OpenCount=RouteCount=0;}
        public int Count=>RouteCount;
        public NavPoint this[int index]=>index>=0&&index<RouteCount?Smoothed[index]:throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<NavPoint> GetEnumerator(){for(int i=0;i<RouteCount;i++)yield return Smoothed[i];}
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        protected override void ReleaseEdges(){surfaceNames?.Release();surfaceNames=null;}
    }
}
