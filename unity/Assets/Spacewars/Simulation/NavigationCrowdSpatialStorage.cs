using System;

namespace Spacewars.Simulation
{
    // Derived collision index. Actor rows refer to the crowd's actor list;
    // bucket links retain insertion order, including remove/append in one cell.
    // Capacity is canonical for actor count, independent of historical growth.
    internal sealed class NavCrowdSpatialStorage : NavOwnedStorage
    {
        private static readonly long HeaderBytes=NavStorageLayout.ObjectBytesBound(typeof(NavCrowdSpatialStorage));
        private static int Buckets(int actors){int n=1;while(n<checked(4*actors))n=checked(n*2);return n;}
        private static long ArrayBytes(int count,int width)=>checked(32L+((count*(long)width+7)/8)*8);
        internal static long Layout(int actors)=>checked(HeaderBytes+ArrayBytes(Buckets(actors),8)+3*ArrayBytes(Buckets(actors),4)+3*ArrayBytes(actors,4));
        internal readonly int ActorCount;
        private readonly long[] keys;
        private readonly int[] occupied,heads,tails,next,previous,actorBuckets;
        private NavCrowdSpatialStorage(int actors):base(Layout(actors))
        {ActorCount=actors;int count=Buckets(actors);keys=new long[count];occupied=new int[count];heads=new int[count];tails=new int[count];next=new int[actors];previous=new int[actors];actorBuckets=new int[actors];Reset();Retain();}
        internal static NavCrowdSpatialStorage Create(int actors,Func<long,bool> reserve)
        {if(actors<0)throw new ArgumentOutOfRangeException(nameof(actors));using(var allocation=new NavStorageAllocation(reserve)){allocation.Reserve(Layout(actors));return allocation.Register(new NavCrowdSpatialStorage(actors));}}
        internal void Reset(){Array.Clear(occupied,0,occupied.Length);for(int i=0;i<ActorCount;i++)next[i]=previous[i]=actorBuckets[i]=-1;}
        private int Find(long key)
        {int slot=unchecked((int)((uint)key*2654435761u^(uint)(key>>32)))&(keys.Length-1);while(occupied[slot]!=0&&keys[slot]!=key)slot=(slot+1)&(keys.Length-1);return slot;}
        internal int First(long key){int bucket=Find(key);return occupied[bucket]==0?-1:heads[bucket];}
        internal int Next(int actor)=>next[actor];
        internal void Add(int actor,long key)
        {
            int bucket=Find(key);if(occupied[bucket]==0){occupied[bucket]=1;keys[bucket]=key;heads[bucket]=tails[bucket]=-1;}
            int tail=tails[bucket];previous[actor]=tail;next[actor]=-1;actorBuckets[actor]=bucket;
            if(tail<0)heads[bucket]=actor;else next[tail]=actor;tails[bucket]=actor;
        }
        internal void Remove(int actor)
        {
            int bucket=actorBuckets[actor];if(bucket<0)return;int before=previous[actor],after=next[actor];
            if(before<0)heads[bucket]=after;else next[before]=after;if(after<0)tails[bucket]=before;else previous[after]=before;
            actorBuckets[actor]=previous[actor]=next[actor]=-1;
        }
        protected override void ReleaseEdges(){}
    }
}
