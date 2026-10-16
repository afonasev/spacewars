using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed class NavigationAnswerMailboxBacking : NavOwnedStorage
    {
        private static long Layout(int capacity)=>checked(NavStorageLayout.ObjectBytesBound(typeof(NavigationAnswerMailboxBacking))+32L+8L*capacity);
        internal readonly NavigationAnswer[] Items;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationAnswerMailboxBacking(int capacity):base(Layout(capacity)){Items=new NavigationAnswer[capacity];Retain();}
        internal static NavigationAnswerMailboxBacking CreateOwned(int capacity,INavStorageReservation r)
        {using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout(capacity));return a.Register(new NavigationAnswerMailboxBacking(capacity));}}
        protected override void ReleaseEdges()=>Array.Clear(Items,0,Items.Length);
    }
}
