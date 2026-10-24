using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    internal sealed class NavMappedCopy<T> : IReadOnlyList<T>
    {
        private readonly IReadOnlyList<T> source;private readonly Func<T,T> copy;
        internal NavMappedCopy(IReadOnlyList<T> source,Func<T,T> copy){this.source=source;this.copy=copy;}
        public int Count=>source.Count;public T this[int n]=>copy(source[n]);
        public IEnumerator<T> GetEnumerator(){for(int n=0;n<Count;n++)yield return this[n];}
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    internal sealed class NavPartitionCopy
    {
        internal readonly NavProviderCopy Declarations;internal readonly INavStorageReservation Reservation;
        private readonly Dictionary<MapSupport,MapSupport> supports=new Dictionary<MapSupport,MapSupport>();
        private readonly Dictionary<NavSurfaceTransition,NavSurfaceTransition> transitions=new Dictionary<NavSurfaceTransition,NavSurfaceTransition>();
        private readonly Dictionary<NavPartitionSeam,NavPartitionSeam> seams=new Dictionary<NavPartitionSeam,NavPartitionSeam>();
        internal NavPartitionCopy(INavStorageReservation reservation){Reservation=reservation;Declarations=new NavProviderCopy(reservation);}
        internal bool SameLedger(NavOwnedStorage storage)=>storage!=null&&Reservation is INavStorageLedgerIdentity identity&&identity.Owns(storage);
        internal MapSupport Support(MapSupport source)
        {
            if(SameLedger(source.StorageRoot))return source;
            if(!supports.TryGetValue(source,out var value)){value=MapSupport.CopyOwned(source,this);supports.Add(source,value);value.StorageRoot.Release();}return value;
        }
        internal NavSurfaceTransition Transition(NavSurfaceTransition source)
        {
            if(SameLedger(source.StorageRoot))return source;
            if(!transitions.TryGetValue(source,out var value)){value=NavSurfaceTransition.CopyOwned(source,this);transitions.Add(source,value);value.StorageRoot.Release();}return value;
        }
        internal NavPartitionSeam Seam(NavPartitionSeam source)
        {
            if(SameLedger(source.StorageRoot))return source;
            if(!seams.TryGetValue(source,out var value)){value=NavPartitionSeam.CopyOwned(source,this);seams.Add(source,value);value.StorageRoot.Release();}return value;
        }
    }
    public sealed partial class MapSupport
    {
        private NavDeclarationStorage owned;NavOwnedStorage INavStorageFacade.StorageRoot=>owned;internal NavOwnedStorage StorageRoot=>owned;
        internal static MapSupport CopyOwned(MapSupport source,NavPartitionCopy copy)
        {
            var bounds=source.Bounds;var polygon=bounds.Polygon==null?null:copy.Declarations.Polygon(bounds.Polygon);
            if(polygon!=null)bounds=new NavObstacle(polygon,bounds.Bottom,bounds.Top);
            long bytes=checked(384+NavProviderCopy.TextBytes(source.Id)+NavProviderCopy.TextBytes(source.SurfaceId));
            using(var allocation=new NavStorageAllocation(copy.Reservation.TryReserve)){
                allocation.Reserve(bytes);var storage=allocation.Register(new NavDeclarationStorage(bytes));
                var value=new MapSupport(NavProviderCopy.Text(source.Id),bounds,source.IsBridge,source.Height,source.Gradient,source.Origin,NavProviderCopy.Text(source.SurfaceId)){owned=storage};storage.Facade=value;
                if(polygon!=null){polygon.StorageRoot.Retain();storage.First=polygon.StorageRoot;}storage.Retain();return value;
            }
        }
    }
    public sealed partial class NavSurfaceTransition
    {
        private NavDeclarationStorage owned;NavOwnedStorage INavStorageFacade.StorageRoot=>owned;internal NavOwnedStorage StorageRoot=>owned;
        internal static NavSurfaceTransition CopyOwned(NavSurfaceTransition source,NavPartitionCopy copy)
        {
            long bytes=checked(192+NavProviderCopy.TextBytes(source.Id)+NavProviderCopy.TextBytes(source.From)+NavProviderCopy.TextBytes(source.To));
            using(var allocation=new NavStorageAllocation(copy.Reservation.TryReserve)){
                allocation.Reserve(bytes);var storage=allocation.Register(new NavDeclarationStorage(bytes));
                var value=new NavSurfaceTransition(NavProviderCopy.Text(source.Id),NavProviderCopy.Text(source.From),NavProviderCopy.Text(source.To)){owned=storage};storage.Facade=value;storage.Retain();return value;
            }
        }
    }
    public sealed partial class NavPartitionSeam
    {
        private NavDeclarationStorage owned;NavOwnedStorage INavStorageFacade.StorageRoot=>owned;internal NavOwnedStorage StorageRoot=>owned;
        private NavPartitionSeam(NavPartitionSeam source)
        {Id=NavProviderCopy.Text(source.Id);FromSurface=NavProviderCopy.Text(source.FromSurface);ToSurface=NavProviderCopy.Text(source.ToSurface);From=source.From;To=source.To;Allowed=source.Allowed;}
        internal static NavPartitionSeam CopyOwned(NavPartitionSeam source,NavPartitionCopy copy)
        {
            long bytes=checked(256+NavProviderCopy.TextBytes(source.Id)+NavProviderCopy.TextBytes(source.FromSurface)+NavProviderCopy.TextBytes(source.ToSurface));
            using(var allocation=new NavStorageAllocation(copy.Reservation.TryReserve)){
                allocation.Reserve(bytes);var storage=allocation.Register(new NavDeclarationStorage(bytes));var value=new NavPartitionSeam(source){owned=storage};storage.Facade=value;storage.Retain();return value;
            }
        }
    }
    // Tuples here are private exclusive payloads, not shared storage references.
    // Charge each actual tuple (16-byte header + two 16-byte points), array and
    // root before construction; no unadmitted ToArray or arrangement rebuild.
    internal sealed partial class NavPartitionEdges : NavOwnedStorage
    {
        internal readonly Tuple<NavPoint,NavPoint>[] Values;
        private NavPartitionEdges(int count):base(96L+56L*count){Values=new Tuple<NavPoint,NavPoint>[count];}
        internal static NavPartitionEdges Copy(Tuple<NavPoint,NavPoint>[] source,INavStorageReservation reservation)
        {
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(96L+56L*source.Length);var value=allocation.Register(new NavPartitionEdges(source.Length));
                for(int n=0;n<source.Length;n++)value.Values[n]=Tuple.Create(source[n].Item1,source[n].Item2);
                value.Retain();return value;
            }
        }
        protected override void ReleaseEdges(){}
    }
    internal sealed partial class TerrainLocations
    {
        private NavDeclarationStorage owned;NavOwnedStorage INavStorageFacade.StorageRoot=>owned;internal NavOwnedStorage StorageRoot=>owned;
        private TerrainLocations(NavGeometry geometry){this.geometry=geometry;}
        private sealed class SeamCopy
        {
            private readonly NavPartitionCopy copy;private readonly Dictionary<Seam,Seam> aliases=new Dictionary<Seam,Seam>();
            internal SeamCopy(NavPartitionCopy copy){this.copy=copy;}
            internal Seam Copy(Seam source)
            {
                if(copy.SameLedger(source.Storage))return source;
                if(aliases.TryGetValue(source,out var prior))return prior;
                long bytes=checked(256+NavProviderCopy.TextBytes(source.A)+NavProviderCopy.TextBytes(source.B));
                using(var allocation=new NavStorageAllocation(copy.Reservation.TryReserve)){
                    allocation.Reserve(bytes);var storage=allocation.Register(new NavDeclarationStorage(bytes));var value=new Seam{A=NavProviderCopy.Text(source.A),B=NavProviderCopy.Text(source.B),From=source.From,To=source.To,Allowed=source.Allowed,Storage=storage};storage.Facade=value;aliases.Add(source,value);return value;
                }
            }
        }
        internal void RequireCopyInputs(INavStorageReservation reservation)
        {
            void Require(NavOwnedStorage root){if(root!=null)NavStorageAdmission.RequireOwned(reservation,root);}
            Require(owned);Require(geometry.StorageRoot);
            foreach(var obstacle in geometry.Obstacles)Require(obstacle.Polygon?.StorageRoot);
            foreach(var support in regions){Require(support.StorageRoot);Require(support.Bounds.Polygon?.StorageRoot);}
            foreach(var transition in Transitions)Require(transition.StorageRoot);
            foreach(var seam in seams)Require(seam.Storage);
            foreach(var seam in exterior)Require(seam.Storage);
            foreach(var seam in Seams)Require(seam.StorageRoot);
        }
        internal long CopyScratchBytes=>checked(4096+256L*(regions.Length+edges.Length+seams.Count+exterior.Count+Transitions.Count+Seams.Count)+256L*geometry.Obstacles.Count);
        internal static TerrainLocations CopyOwned(TerrainLocations source,NavPartitionCopy copy,TerrainLocations shared=null)
        {
            source.RequireCopyInputs(copy.Reservation);
            if(copy.SameLedger(source.owned)){source.owned.Retain();return source;}
            if(shared!=null){
                if(!copy.SameLedger(shared.owned)||!source.SameArrangement(shared))throw new ArgumentException("Shared partition arrangement differs or belongs to another ledger.");
                using(var allocation=new NavStorageAllocation(copy.Reservation.TryReserve)){
                    allocation.Reserve(512);var storage=allocation.Register(new NavDeclarationStorage(512));
                    var geometryCopy=copy.Declarations.Geometry(source.geometry);var value=new TerrainLocations(geometryCopy){owned=storage};storage.Facade=value;
                    geometryCopy.StorageRoot.Retain();storage.First=geometryCopy.StorageRoot;
                    NavOwnedStorage Share(NavOwnedStorage edge){edge.Retain();return edge;}
                    storage.Second=Share(shared.owned.Second);storage.Third=Share(shared.owned.Third);storage.Fourth=Share(shared.owned.Fourth);storage.Fifth=Share(shared.owned.Fifth);storage.Sixth=Share(shared.owned.Sixth);storage.Seventh=Share(shared.owned.Seventh);storage.Eighth=Share(shared.owned.Eighth);
                    value.regions=shared.regions;value.edges=shared.edges;value.byId=shared.byId;value.seams=shared.seams;value.exterior=shared.exterior;value.Transitions=shared.Transitions;value.Seams=shared.Seams;
                    storage.Retain();return value;
                }
            }
            var geometry=copy.Declarations.Geometry(source.geometry);
            using(var allocation=new NavStorageAllocation(copy.Reservation.TryReserve)){
                allocation.Reserve(512);var storage=allocation.Register(new NavDeclarationStorage(512));var value=new TerrainLocations(geometry){owned=storage};storage.Facade=value;geometry.StorageRoot.Retain();storage.First=geometry.StorageRoot;
                var regions=NavRetainedArray<MapSupport>.Copy(new NavMappedCopy<MapSupport>(source.regions,copy.Support),copy.Reservation.TryReserve);storage.Second=regions;value.regions=regions.ReadOnlyBacking;
                var edges=NavPartitionEdges.Copy(source.edges,copy.Reservation);storage.Third=edges;value.edges=edges.Values;
                var index=NavCowDictionary<NavNameKey,int>.CreateOwned(copy.Reservation,Comparer<NavNameKey>.Default,false);storage.Fourth=index;for(int n=0;n<value.regions.Length;n++)index.Add(new NavNameKey(new NavNameReference(value.regions[n].StorageRoot,NavNameRole.SupportSurface)),n);index.Seal();value.byId=index;
                var seamCopy=new SeamCopy(copy);var seams=NavRetainedArray<Seam>.Copy(new NavMappedCopy<Seam>(source.seams,seamCopy.Copy),copy.Reservation.TryReserve);storage.Fifth=seams;value.seams=seams;
                var exterior=NavRetainedArray<Seam>.Copy(new NavMappedCopy<Seam>(source.exterior,seamCopy.Copy),copy.Reservation.TryReserve);storage.Sixth=exterior;value.exterior=exterior;
                var transitions=NavRetainedArray<NavSurfaceTransition>.Copy(new NavMappedCopy<NavSurfaceTransition>(source.Transitions,copy.Transition),copy.Reservation.TryReserve);storage.Seventh=transitions;value.Transitions=transitions;
                var publicSeams=NavRetainedArray<NavPartitionSeam>.Copy(new NavMappedCopy<NavPartitionSeam>(source.Seams,copy.Seam),copy.Reservation.TryReserve);storage.Eighth=publicSeams;value.Seams=publicSeams;
                storage.Retain();return value;
            }
        }
    }
    public sealed partial class PartitionNavigationProvider
    {
        // Only a refused scratch credit is recoverable admission pressure.
        // Ownership, layout and allocation invariants still propagate normally.
        internal sealed class CopyScratchRefused : InvalidOperationException
        {internal CopyScratchRefused():base("Scratch exceeds prepared reservation.") {}}
        private static IDisposable ReserveCopyScratch(INavStorageReservation reservation,long bytes)
        {
            if(!NavStorageScratch.TryReserve(reservation,bytes,out var lease))throw new CopyScratchRefused();
            return lease;
        }
        private NavDeclarationStorage owned;NavOwnedStorage INavStorageFacade.StorageRoot=>owned;internal NavOwnedStorage StorageRoot=>owned;
        internal static readonly long LayoutBase=Math.Max(512,NavStorageLayout.ObjectBytesBound(typeof(NavDeclarationStorage))+NavStorageLayout.ObjectBytesBound(typeof(PartitionNavigationProvider)));
        private PartitionNavigationProvider(PartitionNavigationProvider source,TerrainLocations locations)
        {this.locations=locations;HalfExtent=source.HalfExtent;terrainId=NavProviderCopy.Text(source.terrainId);terrainRevision=source.terrainRevision;surfaceSemanticsVersion=source.surfaceSemanticsVersion;holdBinding=NavProviderCopy.Text(source.holdBinding);Binding=NavProviderCopy.Text(source.Binding);long supportVertices=0,polygonVertices=0;foreach(var support in locations.Supports)supportVertices=checked(supportVertices+(support.Bounds.Polygon?.Vertices.Count??4));foreach(var obstacle in locations.Geometry.Obstacles)polygonVertices=checked(polygonVertices+(obstacle.Polygon?.Vertices.Count??0));EstimatedRetainedBytes=RetainedPayloadEstimate(source.Surfaces.Count,supportVertices,locations.Seams.Count,locations.Transitions.Count,locations.Geometry.Obstacles.Count,polygonVertices);}
        internal static PartitionNavigationProvider CopyOwned(PartitionNavigationProvider source,INavStorageReservation reservation,PartitionNavigationProvider shared=null)
        {
            if(source==null||reservation==null)throw new ArgumentNullException();
            if(source.owned!=null)NavStorageAdmission.RequireOwned(reservation,source.owned);
            source.locations.RequireCopyInputs(reservation);
            foreach(var surface in source.Surfaces){
                if(surface.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,surface.StorageRoot);
                if(surface.Area.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,surface.Area.StorageRoot);
                if(surface.Area.Boundary.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,surface.Area.Boundary.StorageRoot);
                foreach(var blocker in surface.Area.Blockers)if(blocker.Polygon?.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,blocker.Polygon.StorageRoot);
            }
            if(shared!=null){NavStorageAdmission.RequireOwned(reservation,shared.owned);if(!source.SameArrangement(shared))throw new ArgumentException("Partition sharing requires the same full arrangement.");}
            long scratch=checked(source.locations.CopyScratchBytes+4096+256L*source.Surfaces.Count);
            foreach(var surface in source.Surfaces)scratch=checked(scratch+4096+256L*surface.Area.Blockers.Count);
            using(ReserveCopyScratch(reservation,scratch))
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                long bytes=checked(LayoutBase+NavProviderCopy.TextBytes(source.Binding)+NavProviderCopy.TextBytes(source.terrainId)+NavProviderCopy.TextBytes(source.holdBinding));allocation.Reserve(bytes);var storage=allocation.Register(new NavDeclarationStorage(bytes));var copy=new NavPartitionCopy(reservation);
                var locations=TerrainLocations.CopyOwned(source.locations,copy,shared?.locations);storage.First=locations.StorageRoot;var value=new PartitionNavigationProvider(source,locations){owned=storage};storage.Facade=value;
                if(shared!=null){
                    shared.owned.Second.Retain();storage.Second=shared.owned.Second;value.Surfaces=shared.Surfaces;
                    shared.owned.Third.Retain();storage.Third=shared.owned.Third;value.bySurface=shared.bySurface;
                    shared.owned.Fourth.Retain();storage.Fourth=shared.owned.Fourth;value.Portals=shared.Portals;
                    value.Seams=locations.Seams;using(ReserveCopyScratch(reservation,8192))NavPhysicalDigestStream.Require(value.WriteBindingTranscript,source.Binding);storage.Retain();return value;
                }
                var surfaces=NavRetainedArray<NavLayerSurface>.Copy(new NavMappedCopy<NavLayerSurface>(source.Surfaces,copy.Declarations.Surface),reservation.TryReserve);storage.Second=surfaces;value.Surfaces=surfaces;
                var index=NavCowDictionary<string,NavLayerSurface>.CreateOwned(reservation,StringComparer.Ordinal,false);storage.Third=index;foreach(var surface in surfaces)index.Add(surface.Id,surface);index.Seal();value.bySurface=index;
                var portals=NavRetainedArray<NavLayerPortal>.Copy(source.Portals,reservation.TryReserve);storage.Fourth=portals;value.Portals=portals;value.Seams=locations.Seams;using(ReserveCopyScratch(reservation,8192))NavPhysicalDigestStream.Require(value.WriteBindingTranscript,source.Binding);storage.Retain();return value;
            }
        }
    }
}
