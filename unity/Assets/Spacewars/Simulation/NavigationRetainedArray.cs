using System;
using System.Collections;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    // Private immutable copy; public checkpoint arrays are never adopted or released.
    internal sealed partial class NavRetainedArray<T> : NavOwnedStorage, IReadOnlyList<T>
    {
        internal static readonly int ElementBytes=NavStorageLayout.ValueBytes(typeof(T));
        private readonly T[] values;private int populated;
        private NavRetainedArray(int count):base(96L+(long)ElementBytes*count)
        {values=new T[count];}
        private void Populate(IReadOnlyList<T> source)
        {for(int n=0;n<values.Length;n++){var value=source[n];NavStoragePayload<T>.ValidateActive(value,this);NavStoragePayload<T>.Retain(value);values[n]=value;populated++;}}
        internal static NavRetainedArray<T> Copy(IReadOnlyList<T> source,Func<long,bool> reserve=null)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));
            int count=source.Count;if(count<0)throw new ArgumentException("Invalid immutable input count.");
            // These arrays transport references to immutable adjacency roots.
            // Reject every supplied foreign/public/mutable/null root before the
            // destination header or backing array is admitted or constructed.
            if(reserve?.Target is INavStorageAllocator&&(typeof(T)==typeof(IReadOnlyList<NavGraphArc>)||typeof(T)==typeof(NavPagedList<NavGraphArc>))){
                for(int n=0;n<count;n++){
                    var root=source[n] as NavOwnedStorage;
                    if(root==null||!root.IsImmutableCaptureRoot||root.References<=0||!(reserve.Target is INavStorageLedgerIdentity identity)||!identity.Owns(root))throw new ArgumentException("Adjacency array input needs the same live immutable ledger.");
                }
            }
            using(var allocation=new NavStorageAllocation(reserve)){
                allocation.Reserve(96L+(long)ElementBytes*count);var result=allocation.Register(new NavRetainedArray<T>(count));result.Populate(source);result.Retain();return result;
            }
        }
        internal static NavRetainedArray<T> CopyAppending(IReadOnlyList<T> source,T tail,Func<long,bool> reserve)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));int count=checked(source.Count+1);
            using(var allocation=new NavStorageAllocation(reserve)){
                allocation.Reserve(96L+(long)ElementBytes*count);var result=allocation.Register(new NavRetainedArray<T>(count));
                for(int i=0;i<count;i++){var value=i==source.Count?tail:source[i];NavStoragePayload<T>.ValidateActive(value,result);NavStoragePayload<T>.Retain(value);result.values[i]=value;result.populated++;}result.Retain();return result;
            }
        }
        internal T[] ReadOnlyBacking=>values;
        public int Count=>values.Length;
        public T this[int index]=>values[index];
        protected override void ReleaseEdges(){if(NavStoragePayload<T>.HasOwnership)for(int n=0;n<populated;n++)NavStoragePayload<T>.Release(values[n]);}
        public IEnumerator<T> GetEnumerator(){foreach(var value in values)yield return value;}
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
