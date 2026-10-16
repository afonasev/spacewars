using System;

namespace Spacewars.Simulation
{
    // Short-lived compatibility/direct-step arrays. The owning Session admits
    // the complete layout before construction and before any answer mutation.
    internal sealed class NavMotionWorkspace : IDisposable
    {
        internal NavMotionBody[] Motion;internal NavMotionProposal[] Proposals;
        internal int[] Neighbors,NeighborCounts;
        private NavCrowd source;private readonly long membership;
        private IDisposable scratch,admission;
        internal static readonly long HeaderBytes=NavStorageLayout.ObjectBytesBound(typeof(NavMotionWorkspace));
        private static long Align(long bytes)=>checked((bytes+7)/8*8);
        internal static long ArrayBytes(int count)
        {
            if(count<0)throw new ArgumentOutOfRangeException(nameof(count));
            return checked(4*32L+Align(count*(long)NavStorageLayout.ValueBytes(typeof(NavMotionBody)))+Align(count*(long)NavStorageLayout.ValueBytes(typeof(NavMotionProposal)))+Align(4L*count)+Align(4L*count*NavCrowdMotion.NeighborCapacity));
        }
        internal NavMotionWorkspace(NavCrowd source,int count,long membership,IDisposable scratch=null,IDisposable admission=null)
        {
            this.source=source;this.membership=membership;this.scratch=scratch;this.admission=admission;
            Motion=new NavMotionBody[count];Proposals=new NavMotionProposal[count];Neighbors=new int[checked(count*NavCrowdMotion.NeighborCapacity)];NeighborCounts=new int[count];
        }
        internal bool SourceCurrent(NavCrowd crowd)=>ReferenceEquals(source,crowd)&&Motion!=null&&Motion.Length==crowd.Units.Count&&membership==crowd.MotionMembership;
        public void Dispose()
        {
            source=null;Motion=null;Proposals=null;Neighbors=NeighborCounts=null;
            var oldScratch=scratch;var oldAdmission=admission;scratch=admission=null;
            try{oldScratch?.Dispose();}finally{oldAdmission?.Dispose();}
        }
    }
}
