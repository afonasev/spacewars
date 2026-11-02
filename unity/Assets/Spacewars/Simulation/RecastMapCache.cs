using System;
using System.Linq;
namespace Spacewars.Simulation
{
    // A derivative payload has a real physical owner, independent of the scope
    // that constructed it. It is intentionally absent from gameplay capture.
    internal sealed class RecastPayload:NavOwnedStorage,IDisposable
    {
        internal object Value;private NavOwnedStorage source;
        internal void DependOn(NavOwnedStorage value){if(value==null)return;RequireEdge(value);value.Retain();source=value;}
        private RecastPayload(long bytes):base(bytes){}
        internal static RecastPayload Allocate(INavStorageReservation r,long bytes){
            long total=checked(NavStorageLayout.ObjectBytesBound(typeof(RecastPayload))+bytes);
            if(!r.TryReserve(NavStorageLayout.Charge(total)))throw new RecastNative.CapacityRefused();
            var result=new RecastPayload(total);
            try{if(!(r is INavStorageLocalBirthAllocator allocator)||!allocator.TryRegisterLocalBirth(result))throw new InvalidOperationException("Recast derivative needs direct physical ownership.");return result;}
            catch{result.Release();throw;}
        }
        protected override void ReleaseEdges(){Value=null;source?.Release();source=null;}
        public void Dispose()=>Release();
    }
    internal sealed class RecastMapCache:IDisposable
    {
        internal sealed class Entry:NavOwnedStorage
        {
            internal Entry Next;internal IPlayableTerrain Terrain;internal NavGeometry Geometry;
            internal double Radius,Grid;internal string Profile,Held;internal int Revision;
            internal RecastNavigationMap Map;private NavOwnedStorage geometryOwner;
            private Entry(long bytes):base(bytes){}
            internal static Entry Create(INavStorageReservation r,IPlayableTerrain terrain,NavGeometry geometry,NavigationProfile profile,string held){
                long bytes=checked(NavStorageLayout.ObjectBytesBound(typeof(Entry))+48+2L*(profile.ProfileId.Length+(held?.Length??0)));
                if(!r.TryReserve(NavStorageLayout.Charge(bytes)))throw new RecastNative.CapacityRefused();
                var result=new Entry(bytes);
                try{
                    if(!(r is INavStorageLocalBirthAllocator allocator)||!allocator.TryRegisterLocalBirth(result))throw new InvalidOperationException("Recast cache needs direct physical ownership.");
                    result.Terrain=terrain;result.Geometry=geometry;result.Radius=profile.Radius;result.Grid=profile.LocalGridCell;result.Revision=profile.Revision;
                    using(var text=NavStorageScratch.Reserve(r,checked(64+2L*(profile.ProfileId.Length+(held?.Length??0))))){result.Profile=new string(profile.ProfileId.ToCharArray());result.Held=held==null?null:new string(held.ToCharArray());}
                    if(geometry.StorageRoot!=null){result.RequireEdge(geometry.StorageRoot);geometry.StorageRoot.Retain();result.geometryOwner=geometry.StorageRoot;}
                    return result;
                }catch{result.Release();throw;}
            }
            internal bool Matches(IPlayableTerrain terrain,NavGeometry geometry,NavigationProfile profile,string held)
                =>ReferenceEquals(Terrain,terrain)&&ReferenceEquals(Geometry,geometry)&&Radius==profile.Radius&&Grid==profile.LocalGridCell&&Revision==profile.Revision&&Profile==profile.ProfileId&&Held==held;
            protected override void ReleaseEdges(){Map?.Dispose();Map=null;geometryOwner?.Release();geometryOwner=null;Geometry=null;Terrain=null;Next=null;Profile=Held=null;}
        }
        internal readonly struct Pin:IDisposable
        {
            private readonly NavOwnedStorage owner;internal readonly RecastNavigationMap Map;
            private Pin(Entry entry){entry.Retain();owner=entry;Map=entry.Map;}
            public void Dispose()=>owner?.Release();
            internal static Pin Acquire(Entry entry)=>new Pin(entry);
        }
        private Entry first;
        private IPlayableTerrain foundationTerrain;
        private RecastAuthorityGeometry foundation;
        private RecastPayload foundationPayload;
        private NavGeometry publicGeometry;
        private RecastNavigationMap diagnosticCurrent;
        private bool disposed;
        internal long ColdBuilds{get;private set;}internal long WarmHits{get;private set;}
        internal long FoundationBuilds{get;private set;}internal long FoundationReuses{get;private set;}
        internal long PrepareCount{get;private set;}internal long GeometryDigests{get;private set;}
        private void Check(){if(disposed)throw new ObjectDisposedException(nameof(RecastMapCache));}
        private void EnsureFoundation(IPlayableTerrain terrain,NavGeometry geometry,NavigationProfile profile,INavStorageReservation r,Func<bool> cancelled){
            double halfExtent=terrain?.HalfExtent??geometry?.HalfExtent??throw new ArgumentNullException(nameof(geometry));
            Check();if(foundationPayload!=null)NavStorageAdmission.RequireOwned(r,foundationPayload);if(foundation!=null&&ReferenceEquals(foundationTerrain,terrain)&&foundation.SupportHalfExtent==halfExtent)return;
            RecastPayload payload=null;NavGeometry nextPublic=null;
            try{
                // Empty input is public support only. Every overlay later reuses
                // this exact precedence-clipped authored triangle foundation.
                using(var input=NavStorageScratch.Reserve(r,8192)){
                    var empty=new NavGeometry(halfExtent,null,terrain?.Revision??1);
                    using(var scratch=NavStorageScratch.Reserve(r,RecastAuthorityGeometry.ScratchBound(terrain,empty,true))){
                        var value=RecastAuthorityGeometry.Prepare(terrain,empty,profile,cancelled);GeometryDigests++;
                        payload=RecastPayload.Allocate(r,value.PayloadBytes);payload.Value=value;
                    }
                }
                long count=terrain?.MovementBlockers.Count()??geometry.Obstacles.Count;
                using(var scratch=NavStorageScratch.Reserve(r,checked(4096+512L*count*256))){
                    var source=terrain==null?geometry:new NavGeometry(terrain.HalfExtent,terrain.MovementBlockers.ToArray(),terrain.Revision);
                    nextPublic=NavGeometry.CopyOwned(source,r);
                }
                foundationPayload?.Dispose();publicGeometry?.StorageRoot.ReleaseOwner();
                foundationPayload=payload;payload=null;foundation=(RecastAuthorityGeometry)foundationPayload.Value;foundationTerrain=terrain;
                publicGeometry=nextPublic;nextPublic=null;FoundationBuilds++;
            }finally{payload?.Dispose();nextPublic?.StorageRoot.ReleaseOwner();}
        }
        internal void PreparePublic(IPlayableTerrain terrain,NavigationProfile profile,INavStorageReservation r,Func<bool> cancelled=null){
            if(terrain==null)throw new ArgumentNullException(nameof(terrain));
            PreparePublic(terrain,null,profile,r,cancelled);
        }
        internal void PreparePublic(IPlayableTerrain terrain,NavGeometry geometry,NavigationProfile profile,INavStorageReservation r,Func<bool> cancelled=null){
            EnsureFoundation(terrain,geometry,profile,r,cancelled);
            using var pin=Acquire(terrain,publicGeometry,profile,null,r,cancelled);
        }
        private bool PublicEquivalent(NavGeometry geometry){
            if(ReferenceEquals(geometry,publicGeometry))return true;
            if(geometry.Revision!=publicGeometry.Revision||geometry.HalfExtent!=publicGeometry.HalfExtent||geometry.Obstacles.Count!=publicGeometry.Obstacles.Count)return false;
            for(int i=0;i<geometry.Obstacles.Count;i++)if(!geometry.Obstacles[i].GeometryEquals(publicGeometry.Obstacles[i]))return false;
            return true;
        }
        internal Pin Acquire(IPlayableTerrain terrain,NavGeometry geometry,NavigationProfile profile,string held,INavStorageReservation r,Func<bool> cancelled=null){
            Check();if(r==null)throw new ArgumentNullException(nameof(r));if(foundationPayload!=null)NavStorageAdmission.RequireOwned(r,foundationPayload);
            if(cancelled?.Invoke()==true)throw new OperationCanceledException();
            // The cheap owner key precedes foundation work, clipping and digest.
            for(var entry=first;entry!=null;entry=entry.Next)if(entry.Matches(terrain,geometry,profile,held)){WarmHits++;return Pin.Acquire(entry);}
            EnsureFoundation(terrain,geometry,profile,r,cancelled);
            if(PublicEquivalent(geometry)){geometry=publicGeometry;held=null;
                for(var entry=first;entry!=null;entry=entry.Next)if(entry.Matches(terrain,geometry,profile,held)){WarmHits++;return Pin.Acquire(entry);}}
            Entry next=null;RecastPayload payload=null;
            try{
                next=Entry.Create(r,terrain,geometry,profile,held);
                RecastAuthorityGeometry input;
                using(var scratch=NavStorageScratch.Reserve(r,RecastAuthorityGeometry.ScratchBound(terrain,geometry,false))){
                    PrepareCount++;input=RecastAuthorityGeometry.PrepareFromFoundation(terrain,geometry,profile,foundation,cancelled);GeometryDigests++;FoundationReuses++;
                    long bytes=checked(input.PayloadBytes+NavStorageLayout.ObjectBytesBound(typeof(RecastNavigationMap))+NavStorageLayout.ObjectBytesBound(typeof(RecastMapHandle)));
                    payload=RecastPayload.Allocate(r,bytes);payload.Value=input;payload.DependOn(input.SourceStorageRoot);
                }
                using(var memory=RecastNative.Memory(r))next.Map=new RecastNavigationMap(input,cancelled,payload);
                payload.Dispose();payload=null;next.Next=first;first=next;next=null;ColdBuilds++;
                return Pin.Acquire(first);
            }finally{next?.Release();payload?.Dispose();}
        }
        // Eviction retires cache ownership only. Active contexts retain entries;
        // external queries additionally retain native map and geometry payload.
        internal void RetireUnused(){Entry previous=null,at=first;while(at!=null){var next=at.Next;if(at.References==1){if(previous==null)first=next;else previous.Next=next;at.Next=null;at.Release();}else previous=at;at=next;}}
        // Existing standalone data diagnostics have no physical Session. This
        // overload is not used by the runtime, and does not prove paid storage.
        internal RecastNavigationMap Acquire(RecastAuthorityGeometry geometry,Func<bool> cancelled=null){Check();if(diagnosticCurrent!=null&&diagnosticCurrent.Binding==geometry.Binding){WarmHits++;return diagnosticCurrent;}var next=new RecastNavigationMap(geometry,cancelled);diagnosticCurrent?.Dispose();diagnosticCurrent=next;ColdBuilds++;return next;}
        public void Dispose(){if(disposed)return;disposed=true;while(first!=null){var next=first.Next;first.Next=null;first.Release();first=next;}diagnosticCurrent?.Dispose();diagnosticCurrent=null;foundationPayload?.Dispose();foundationPayload=null;foundation=null;foundationTerrain=null;publicGeometry?.StorageRoot.ReleaseOwner();publicGeometry=null;}
    }
}
