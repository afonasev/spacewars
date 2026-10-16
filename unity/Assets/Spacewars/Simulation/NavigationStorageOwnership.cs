using System;
using System.Reflection;
using System.Threading;

namespace Spacewars.Simulation
{
    internal interface INavStorageFacade {NavOwnedStorage StorageRoot {get;}}
    internal interface INavStorageAllocator { void Register(NavOwnedStorage storage); }
    internal interface INavStorageLocalBirthAllocator { bool TryRegisterLocalBirth(NavOwnedStorage storage); }
    internal interface INavStorageReservation : INavStorageAllocator { bool TryReserve(long bytes); }
    internal interface INavStorageLedgerIdentity {bool Owns(NavOwnedStorage storage);object Identity {get;}}
    internal static class NavStorageAdmission
    {
        internal static void RequireOwned(INavStorageReservation reservation,NavOwnedStorage storage)
        {
            if(reservation==null)throw new ArgumentNullException(nameof(reservation));
            RequireLedger(reservation,storage);
        }
        internal static void RequireLedger(object reservation,NavOwnedStorage storage)
        {
            if(!(reservation is INavStorageLedgerIdentity identity)||storage==null||storage.References<=0||!identity.Owns(storage))throw new ArgumentException("Storage edge must belong to the same live allocation ledger.");
        }
    }
    internal interface INavStorageScratchAllocator { IDisposable ClaimScratch(long admittedBytes); }
    internal static class NavStorageScratch
    {
        internal const long LeaseBytes=64;
        internal static IDisposable Reserve(Func<long,bool> reserve,long scratchBytes)
        {
            if(!(reserve?.Target is INavStorageScratchAllocator allocator))throw new InvalidOperationException("Scratch requires its owning allocation scope.");
            long bytes=checked(LeaseBytes+scratchBytes);if(scratchBytes<0||!reserve(bytes))throw new InvalidOperationException("Scratch exceeds prepared reservation.");return allocator.ClaimScratch(bytes);
        }
        internal static IDisposable Reserve(INavStorageReservation reservation,long scratchBytes)
        {
            if(!(reservation is INavStorageScratchAllocator allocator))throw new InvalidOperationException("Scratch requires its owning allocation scope.");
            long bytes=checked(LeaseBytes+scratchBytes);if(scratchBytes<0||!reservation.TryReserve(bytes))throw new InvalidOperationException("Scratch exceeds prepared reservation.");
            return allocator.ClaimScratch(bytes);
        }
    }
    // Bind decorated admission explicitly to its physical allocator. The actual
    // owned factories consume this interface, never a naked forecasting callback.
    internal sealed class NavStorageReservation : INavStorageReservation, INavStorageScratchAllocator, INavStorageLedgerIdentity, INavStorageLocalBirthAllocator
    {
        private readonly Func<long,bool> reserve;private readonly INavStorageAllocator allocator;
        internal NavStorageReservation(Func<long,bool> reserve,INavStorageAllocator allocator)
        {this.reserve=reserve??throw new ArgumentNullException(nameof(reserve));this.allocator=allocator??throw new ArgumentNullException(nameof(allocator));}
        public bool TryReserve(long bytes)=>reserve(bytes);
        public void Register(NavOwnedStorage storage)=>allocator.Register(storage);
        public bool TryRegisterLocalBirth(NavOwnedStorage storage)=>allocator is INavStorageLocalBirthAllocator local&&local.TryRegisterLocalBirth(storage);
        public object Identity=>(allocator as INavStorageLedgerIdentity)?.Identity;
        public bool Owns(NavOwnedStorage storage)=>allocator is INavStorageLedgerIdentity identity&&identity.Owns(storage);
        public IDisposable ClaimScratch(long admittedBytes)
        {if(!(allocator is INavStorageScratchAllocator scratch))throw new InvalidOperationException("Missing physical scratch allocator.");return scratch.ClaimScratch(admittedBytes);}
    }
    internal interface INavStorageCaptureLane {bool TryEnterCapture(out long epoch);void ExitCapture(long epoch);}
    internal interface INavStorageLedger { void Released(long bytes); }
    public abstract class NavOwnedStorage
    {
        internal const long CaptureHeaderBytes=16;
        internal long CaptureEpoch;internal int CaptureId,CaptureTag;
        internal virtual bool IsImmutableCaptureRoot=>false;
        private int references=1;
        private INavStorageLedger ledger;
        internal NavOwnedStorage BirthNext;
        internal readonly long StorageBytes;
        internal NavOwnedStorage(long bytes){if(bytes<64)throw new ArgumentException("Invalid physical storage charge.");StorageBytes=NavStorageLayout.Charge(bytes);}
        internal int References=>Volatile.Read(ref references);
        internal bool IsRegistered=>ledger!=null;
        internal bool BelongsTo(INavStorageLedger owner)=>owner!=null&&ReferenceEquals(ledger,owner);
        internal void RequireEdge(NavOwnedStorage child)
        {
            if(child==null)throw new ArgumentException("Unowned physical payload edge.");
            if(child.References<=0)throw new InvalidOperationException("Invalid storage retain.");
            if(ledger!=null&&!child.BelongsTo(ledger))throw new ArgumentException("Physical payload edge belongs to another ledger.");
        }
        internal void BindLedger(INavStorageLedger value)
        {if(ledger!=null||value==null)throw new InvalidOperationException("Physical storage already registered.");ledger=value;}
        internal void Retain()
        {
            int current;
            do{current=Volatile.Read(ref references);if(current<=0||current==int.MaxValue)throw new InvalidOperationException("Invalid storage retain.");}
            while(Interlocked.CompareExchange(ref references,current+1,current)!=current);
        }
        internal void Release()
        {
            int count=Interlocked.Decrement(ref references);
            if(count<0)throw new InvalidOperationException("Duplicate physical storage release.");
            if(count!=0)return;
            ReleaseEdges();var owner=ledger;ledger=null;owner?.Released(StorageBytes);
        }
        internal virtual void ReleaseOwner()=>Release();
        protected abstract void ReleaseEdges();
    }
    // Register is allocation-credit conversion, not another reservation. Each
    // successful allocation has exactly one birth journal. AVL mutations can
    // release their temporary births before the outer preparation resolves.
    internal sealed class NavStorageAllocation : IDisposable
    {
        private readonly Func<long,bool> reserve;
        private readonly INavStorageAllocator allocator;
        private NavOwnedStorage births;
        private readonly bool localBirths;
        internal NavStorageAllocation(Func<long,bool> reserve,bool localBirths=false)
        {this.reserve=reserve;allocator=reserve?.Target as INavStorageAllocator;this.localBirths=localBirths;}
        internal void Reserve(long bytes)
        {if(reserve!=null&&!reserve(NavStorageLayout.Charge(bytes)))throw new InvalidOperationException("Physical storage exceeds prepared reservation.");}
        internal T Register<T>(T storage) where T:NavOwnedStorage
        {
            if(localBirths&&allocator is INavStorageLocalBirthAllocator local&&local.TryRegisterLocalBirth(storage)){storage.BirthNext=births;births=storage;}
            else if(allocator!=null)allocator.Register(storage);
            else{storage.BirthNext=births;births=storage;}
            return storage;
        }
        public void Dispose()
        {while(births!=null){var next=births.BirthNext;births.BirthNext=null;births.Release();births=next;}}
    }
    // Writable's common owned path registers directly with the allocation
    // owner. Legacy callbacks keep the original mutable birth journal so that
    // copied value contexts still share one set of intermediate births.
    internal readonly struct NavStorageAllocationContext : IDisposable
    {
        private readonly Func<long,bool> reserve;
        private readonly INavStorageAllocator allocator;
        private readonly NavStorageAllocation fallback;
        internal NavStorageAllocationContext(Func<long,bool> reserve)
        {this.reserve=reserve;allocator=reserve?.Target as INavStorageAllocator;fallback=allocator==null?new NavStorageAllocation(reserve):null;}
        internal void Reserve(long bytes)
        {if(fallback!=null){fallback.Reserve(bytes);return;}if(reserve!=null&&!reserve(NavStorageLayout.Charge(bytes)))throw new InvalidOperationException("Physical storage exceeds prepared reservation.");}
        internal void Register(NavOwnedStorage storage)
        {if(fallback!=null){fallback.Register(storage);return;}allocator.Register(storage);}
        public void Dispose()=>fallback?.Dispose();
    }
    internal static class NavStoragePayload<T>
    {
        // Select typed hooks once; wide struct keys/pages are not boxed per node.
        private static readonly Action<T> retain,release;private static readonly Action<T,NavOwnedStorage> validate;
        // The fallback value-type hooks below have no ownership work. Keep the
        // named/metadata/trace structs on their typed hooks, including validation.
        internal static readonly bool HasOwnership;
        static NavStoragePayload()
        {
            HasOwnership=true;
            if(typeof(T)==typeof(System.Collections.Generic.IReadOnlyList<NavGraphArc>)||typeof(T)==typeof(NavPagedList<NavGraphArc>)){
                validate=(value,parent)=>{if(value==null)return;if(!(value is NavOwnedStorage root)||!root.IsImmutableCaptureRoot)throw new ArgumentException("Adjacency payload requires an immutable owned list.");parent.RequireEdge(root);};
                retain=value=>{(value as NavOwnedStorage)?.Retain();};release=value=>{(value as NavOwnedStorage)?.Release();};
            }else if(typeof(T)==typeof(NavNameKey)){
                validate=(Action<T,NavOwnedStorage>)(object)new Action<NavNameKey,NavOwnedStorage>((key,parent)=>key.Reference.Validate(key.Value,parent));
                retain=(Action<T>)(object)new Action<NavNameKey>(key=>key.Reference.Owner?.Retain());
                release=(Action<T>)(object)new Action<NavNameKey>(key=>key.Reference.Owner?.Release());
            }else if(typeof(T)==typeof(NavLocation)){
                validate=(Action<T,NavOwnedStorage>)(object)new Action<NavLocation,NavOwnedStorage>((location,parent)=>NavNamedPayload.Validate(location,parent));
                retain=(Action<T>)(object)new Action<NavLocation>(location=>location.NameReference.Owner?.Retain());
                release=(Action<T>)(object)new Action<NavLocation>(location=>location.NameReference.Owner?.Release());
            }else if(typeof(T)==typeof(NavGridCoordinate)){
                validate=(Action<T,NavOwnedStorage>)(object)new Action<NavGridCoordinate,NavOwnedStorage>((grid,parent)=>NavNamedPayload.Validate(grid,parent));
                retain=(Action<T>)(object)new Action<NavGridCoordinate>(grid=>grid.NameReference.Owner?.Retain());
                release=(Action<T>)(object)new Action<NavGridCoordinate>(grid=>grid.NameReference.Owner?.Release());
            }else if(typeof(T)==typeof(NavGraphNode)){
                validate=(Action<T,NavOwnedStorage>)(object)new Action<NavGraphNode,NavOwnedStorage>((node,parent)=>NavNamedPayload.Validate(node,parent));
                retain=(Action<T>)(object)new Action<NavGraphNode>(node=>node.Location.NameReference.Owner?.Retain());
                release=(Action<T>)(object)new Action<NavGraphNode>(node=>node.Location.NameReference.Owner?.Release());
            }else if(typeof(T)==typeof(NavNameReference)){
                validate=(Action<T,NavOwnedStorage>)(object)new Action<NavNameReference,NavOwnedStorage>((reference,parent)=>reference.Validate(reference.Resolve(),parent));
                retain=(Action<T>)(object)new Action<NavNameReference>(reference=>reference.Owner?.Retain());
                release=(Action<T>)(object)new Action<NavNameReference>(reference=>reference.Owner?.Release());
            }else if(typeof(T)==typeof(NavMetadataReference)){
                validate=(Action<T,NavOwnedStorage>)(object)new Action<NavMetadataReference,NavOwnedStorage>((reference,parent)=>reference.Validate(parent));
                retain=(Action<T>)(object)new Action<NavMetadataReference>(reference=>reference.Owner?.Retain());
                release=(Action<T>)(object)new Action<NavMetadataReference>(reference=>reference.Owner?.Release());
            }else if(typeof(T)==typeof(NavPhysicalReference)){
                validate=(Action<T,NavOwnedStorage>)(object)new Action<NavPhysicalReference,NavOwnedStorage>((reference,parent)=>{if(reference.Storage!=null)parent.RequireEdge(reference.Storage);});
                retain=(Action<T>)(object)new Action<NavPhysicalReference>(reference=>reference.Storage?.Retain());
                release=(Action<T>)(object)new Action<NavPhysicalReference>(reference=>reference.Storage?.Release());
            }else if(typeof(T)==typeof(NavGraphArc)){
                validate=(Action<T,NavOwnedStorage>)(object)new Action<NavGraphArc,NavOwnedStorage>((arc,parent)=>{NavNamedPayload.Validate(arc,parent);});
                retain=(Action<T>)(object)new Action<NavGraphArc>(arc=>{arc.NameReference.Owner?.Retain();(arc.Trace as NavOwnedStorage??(arc.Trace as INavStorageFacade)?.StorageRoot)?.Retain();});
                release=(Action<T>)(object)new Action<NavGraphArc>(arc=>{arc.NameReference.Owner?.Release();(arc.Trace as NavOwnedStorage??(arc.Trace as INavStorageFacade)?.StorageRoot)?.Release();});
            }else if(typeof(T)==typeof(NavObstacle)){
                validate=(Action<T,NavOwnedStorage>)(object)new Action<NavObstacle,NavOwnedStorage>((obstacle,parent)=>{if(obstacle.Polygon!=null)parent.RequireEdge(obstacle.Polygon.StorageRoot);});
                retain=(Action<T>)(object)new Action<NavObstacle>(obstacle=>{(obstacle.Polygon as INavStorageFacade)?.StorageRoot?.Retain();});
                release=(Action<T>)(object)new Action<NavObstacle>(obstacle=>{(obstacle.Polygon as INavStorageFacade)?.StorageRoot?.Release();});
            }else if(typeof(T)==typeof(NavPackedFieldCell)){
                validate=(Action<T,NavOwnedStorage>)(object)new Action<NavPackedFieldCell,NavOwnedStorage>((cell,parent)=>{if((cell.Flags&2)!=0)NavStoragePayload<NavGraphArc>.Validate(cell.Next,parent);});
                retain=(Action<T>)(object)new Action<NavPackedFieldCell>(cell=>{if((cell.Flags&2)!=0)NavStoragePayload<NavGraphArc>.Retain(cell.Next);});
                release=(Action<T>)(object)new Action<NavPackedFieldCell>(cell=>{if((cell.Flags&2)!=0)NavStoragePayload<NavGraphArc>.Release(cell.Next);});
            }else if(typeof(T).IsValueType){HasOwnership=false;validate=(_,__)=>{};retain=_=>{};release=_=>{};}
            else{validate=(value,parent)=>{if(value is NavOwnedStorage storage)parent.RequireEdge(storage);else if(value is INavStorageFacade facade)parent.RequireEdge(facade.StorageRoot);};retain=value=>{if(value is NavOwnedStorage storage)storage.Retain();else if(value is INavStorageFacade facade)facade.StorageRoot?.Retain();};release=value=>{if(value is NavOwnedStorage storage)storage.Release();else if(value is INavStorageFacade facade)facade.StorageRoot?.Release();};}
        }
        internal static void Validate(T value,NavOwnedStorage parent)
        {if(parent.IsRegistered)validate(value,parent);}
        internal static void ValidateActive(T value,NavOwnedStorage parent)
        {Validate(value,parent);if(parent.IsRegistered)NavNamedPayload.RequireActive(value);}
        internal static void Retain(T value)=>retain(value);
        internal static void Release(T value)=>release(value);
    }
    internal static class NavStorageLayout
    {
        internal static long Charge(long bytes)=>checked(bytes+NavOwnedStorage.CaptureHeaderBytes);
        internal static Func<long,bool> Before(Func<long,bool> reserve,long bytes)
        {if(reserve!=null&&!reserve(Charge(bytes)))throw new InvalidOperationException("Physical storage exceeds prepared reservation.");return reserve;}
        internal static INavStorageReservation Before(INavStorageReservation reservation,long bytes)
        {if(reservation==null||!reservation.TryReserve(Charge(bytes)))throw new InvalidOperationException("Physical storage exceeds prepared reservation.");return reservation;}
        // Conservative managed x64 layout, including references inside structs.
        internal static int ValueBytes(Type type)
        {
            if(!type.IsValueType)return 8;
            if(type.IsEnum)return ValueBytes(Enum.GetUnderlyingType(type));
            if(type==typeof(bool)||type==typeof(byte)||type==typeof(sbyte))return 1;
            if(type==typeof(short)||type==typeof(ushort)||type==typeof(char))return 2;
            if(type==typeof(int)||type==typeof(uint)||type==typeof(float))return 4;
            if(type==typeof(long)||type==typeof(ulong)||type==typeof(double)||type==typeof(IntPtr)||type==typeof(UIntPtr))return 8;
            int size=0;
            foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)){
                int width=ValueBytes(field.FieldType),align=Math.Min(8,width);size=(size+align-1)/align*align+width;
            }
            return (size+7)/8*8;
        }
        internal static int ObjectBytesBound(Type type)
        {
            if(type.IsValueType)throw new ArgumentException("Object layout bound requires a class.");int bytes=16;
            for(Type at=type;at!=null;at=at.BaseType)foreach(var field in at.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)){int width=ValueBytes(field.FieldType);bytes=checked(bytes+((width+7)/8)*8);}
            return bytes;
        }
        internal static long NodeBytes<TKey,TValue>()=>Math.Max(128,((80L+ValueBytes(typeof(TKey))+ValueBytes(typeof(TValue))+7)/8)*8);
    }
}
