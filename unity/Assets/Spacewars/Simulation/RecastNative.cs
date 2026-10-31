using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Spacewars.Simulation
{
    public enum RecastQueryStatus { Pending,Full,Partial,Unreachable,ResourceFailure,Invalid }
    // No native handle is persisted. A query pins its immutable map until released.
    internal static class RecastNative
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int Cancelled();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr Admit(ulong bytes);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void ReleaseCredit(IntPtr token);
        // Static delegate roots outlive every native SafeHandle. Each block's
        // token roots its original physical ledger until that block is freed.
        private static readonly Admit admit=AdmitAllocation;
        private static readonly ReleaseCredit release=ReleaseAllocation;
        [ThreadStatic] private static INavStorageReservation allocationOwner;
        [ThreadStatic] private static bool allocationDenied;
        internal static INavStorageReservation CurrentMemoryOwner=>allocationOwner;
        private static Exception releaseFailure;
        private static long releaseFaults;
        internal static long ReleaseFaults=>Interlocked.Read(ref releaseFaults);
        internal static void CheckReleaseFaults(){var failure=Volatile.Read(ref releaseFailure);if(failure!=null)throw new InvalidOperationException("Recast original-owner release failed.",failure);}
        internal sealed class CapacityRefused:InvalidOperationException {internal CapacityRefused():base("Recast physical allocation capacity refused.") {}}
        private sealed class NativeAllocation:NavOwnedStorage
        {
            internal static long Layout(long bytes)=>checked(NavStorageLayout.ObjectBytesBound(typeof(NativeAllocation))+16+bytes);
            internal NativeAllocation(long bytes):base(Layout(bytes)){}
            protected override void ReleaseEdges(){}
        }
        private static IntPtr AdmitAllocation(ulong bytes)
        {
            GCHandle handle=default;NativeAllocation storage=null;
            try{
                if(bytes>long.MaxValue||allocationOwner==null||!(allocationOwner is INavStorageLocalBirthAllocator local))return IntPtr.Zero;
                long size=(long)bytes;
                if(!allocationOwner.TryReserve(NavStorageLayout.Charge(NativeAllocation.Layout(size)))){allocationDenied=true;return IntPtr.Zero;}
                storage=new NativeAllocation(size);handle=GCHandle.Alloc(storage);
                // No outer birth journal: the native block is the sole owner,
                // so temporary bake frees return their credit immediately.
                if(!local.TryRegisterLocalBirth(storage))throw new InvalidOperationException("Recast allocation requires direct physical ownership.");
                return GCHandle.ToIntPtr(handle);
            }catch{if(handle.IsAllocated)handle.Free();storage?.Release();allocationDenied=true;return IntPtr.Zero;}
        }
        private static void ReleaseAllocation(IntPtr token)
        {
            // This runs on whichever thread releases the final native pin.
            // It must neither consult TLS nor invoke an expired scope callback.
            try{var handle=GCHandle.FromIntPtr(token);var storage=(NativeAllocation)handle.Target;handle.Free();storage.Release();}
            catch(Exception error){Interlocked.CompareExchange(ref releaseFailure,error,null);Interlocked.Increment(ref releaseFaults);}
        }
        internal readonly struct MemoryScope:IDisposable
        {
            private readonly INavStorageReservation previous;private readonly bool previousDenied;
            internal MemoryScope(INavStorageReservation owner){
                if(owner==null)throw new ArgumentNullException(nameof(owner));
                previous=allocationOwner;previousDenied=allocationDenied;CheckReleaseFaults();
                // Imports can fail before the plugin is available. Mutate TLS
                // only after native entry succeeds, so an unconstructed scope
                // cannot leave an expired managed allocation owner installed.
                if(sw_recast_memory_abi()!=1||sw_recast_memory_scope(admit,release)!=1)throw new NotSupportedException("Recast physical memory ABI mismatch.");
                allocationOwner=owner;allocationDenied=false;
            }
            public void Dispose(){try{sw_recast_memory_scope(previous==null?null:admit,previous==null?null:release);}finally{allocationOwner=previous;allocationDenied=previousDenied;}}
        }
        internal static MemoryScope Memory(INavStorageReservation owner)=>new MemoryScope(owner);
        internal static void CheckCapacity(){if(allocationDenied)throw new CapacityRefused();}
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sw_recast_layer_abi();
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr sw_recast_build_layered(float[] vertices,int vertexCount,int[] triangles,byte[] areas,int triangleCount,byte[] directed,float cs,float ch,float radius,float height,float climb,float slope,Cancelled cancelled,float[] links,byte[] linkAreas,byte[] directions,uint[] ids,int count);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern RecastQueryStatus sw_recast_copy_portals(RecastQueryHandle query,uint[] ids,int capacity,out int count);
        internal const string Library="spacewars_recast";
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sw_recast_memory_abi();
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sw_recast_memory_scope(Admit admit,ReleaseCredit release);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern ulong sw_recast_allocation_count();
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern ulong sw_recast_allocation_bytes();
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sw_recast_abi();
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr sw_recast_build(float[] vertices,int vertexCount,int[] triangles,byte[] areas,int triangleCount,byte[] directed,float cs,float ch,float radius,float height,float climb,float slope,Cancelled cancelled);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void sw_recast_map_free(IntPtr map);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr sw_recast_query(RecastMapHandle map,float[] start,float[] goal,int startArea,int goalArea,int maxNodes);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern RecastQueryStatus sw_recast_step(RecastQueryHandle query,int iterations,out int consumed);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern RecastQueryStatus sw_recast_copy(RecastQueryHandle query,float[] points,int capacity,out int count);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern RecastQueryStatus sw_recast_copy_areas(RecastQueryHandle query,byte[] areas,int capacity,out int count);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void sw_recast_query_free(IntPtr query);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sw_recast_live_maps();
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sw_recast_live_queries();
    }
    internal sealed class RecastMapHandle:SafeHandleZeroOrMinusOneIsInvalid
    {
        private NavOwnedStorage payload;
        internal RecastMapHandle(IntPtr value,NavOwnedStorage payload=null):base(true){SetHandle(value);if(!IsInvalid&&payload!=null){payload.Retain();this.payload=payload;}}
        protected override bool ReleaseHandle(){try{RecastNative.sw_recast_map_free(handle);}finally{payload?.Release();payload=null;}return true;}
    }
    internal sealed class RecastQueryHandle:SafeHandleZeroOrMinusOneIsInvalid
    {
        private RecastMapHandle map;private bool pinned;private NavOwnedStorage metadata;
        internal RecastQueryHandle(RecastMapHandle map,float[] start,float[] goal,int startArea,int goalArea,NavOwnedStorage metadata=null):base(true)
        {
            this.map=map;
            try{map.DangerousAddRef(ref pinned);SetHandle(RecastNative.sw_recast_query(map,start,goal,startArea,goalArea,16384));
                if(IsInvalid){RecastNative.CheckCapacity();throw new InvalidOperationException("Recast query resource refusal.");}if(metadata!=null){metadata.Retain();this.metadata=metadata;}}
            catch{if(pinned){map.DangerousRelease();pinned=false;}throw;}
        }
        protected override bool ReleaseHandle(){try{RecastNative.sw_recast_query_free(handle);}finally{if(pinned){map.DangerousRelease();pinned=false;}map=null;metadata?.Release();metadata=null;}return true;}
    }
}
