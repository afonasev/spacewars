using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    internal sealed partial class NavDeclarationStorage : NavOwnedStorage
    {
        internal NavOwnedStorage First,Second,Third,Fourth,Fifth,Sixth,Seventh,Eighth;internal object Facade;
        internal NavDeclarationStorage(long bytes):base(bytes){}
        protected override void ReleaseEdges(){Facade=null;First?.Release();Second?.Release();Third?.Release();Fourth?.Release();Fifth?.Release();Sixth?.Release();Seventh?.Release();Eighth?.Release();}
    }
    // This map preserves declaration/geometry identity while making private
    // admitted copies. It is transient scratch, never a per-node accounting map.
    internal sealed class NavProviderCopy
    {
        internal readonly INavStorageReservation Reservation;
        private readonly Dictionary<NavPolygon,NavPolygon> polygons=new Dictionary<NavPolygon,NavPolygon>();
        private readonly Dictionary<NavGeometry,NavGeometry> geometries=new Dictionary<NavGeometry,NavGeometry>();
        private readonly Dictionary<NavTraversalArea,NavTraversalArea> areas=new Dictionary<NavTraversalArea,NavTraversalArea>();
        private readonly Dictionary<NavLayerSurface,NavLayerSurface> surfaces=new Dictionary<NavLayerSurface,NavLayerSurface>();
        private readonly Dictionary<NavLayerPortal,NavLayerPortal> portals=new Dictionary<NavLayerPortal,NavLayerPortal>();
        internal NavProviderCopy(INavStorageReservation reservation){Reservation=reservation;}
        private bool SameLedger(NavOwnedStorage storage)=>storage!=null&&Reservation is INavStorageLedgerIdentity identity&&identity.Owns(storage);
        internal NavPolygon Polygon(NavPolygon source)
        {
            if(SameLedger(source.StorageRoot))return source;
            if(!polygons.TryGetValue(source,out var value)){value=NavPolygon.CopyOwned(source,Reservation);polygons.Add(source,value);value.StorageRoot.ReleaseOwner();}
            return value;
        }
        internal NavGeometry Geometry(NavGeometry source)
        {
            if(SameLedger(source.StorageRoot))return source;
            if(!geometries.TryGetValue(source,out var value)){value=NavGeometry.CopyOwned(source,Reservation,Polygon);geometries.Add(source,value);value.StorageRoot.ReleaseOwner();}
            return value;
        }
        internal NavTraversalArea Area(NavTraversalArea source)
        {
            if(SameLedger(source.StorageRoot))return source;
            if(!areas.TryGetValue(source,out var value)){value=NavTraversalArea.CopyOwned(source,this);areas.Add(source,value);value.StorageRoot.Release();}
            return value;
        }
        internal NavLayerSurface Surface(NavLayerSurface source)
        {
            if(SameLedger(source.StorageRoot))return source;
            if(!surfaces.TryGetValue(source,out var value)){value=NavLayerSurface.CopyOwned(source,this);surfaces.Add(source,value);value.StorageRoot.Release();}
            return value;
        }
        internal NavLayerPortal Portal(NavLayerPortal source)
        {
            if(SameLedger(source.StorageRoot))return source;
            if(!portals.TryGetValue(source,out var value)){value=NavLayerPortal.CopyOwned(source,this);portals.Add(source,value);value.StorageRoot.Release();}
            return value;
        }
        internal static long TextBytes(string value)=>value==null?0:32L+2L*value.Length;
        internal static string Text(string value)=>value==null?null:new string(value.AsSpan());
        internal static NavLocation Location(NavLocation value)=>new NavLocation(value.Position,Text(value.SurfaceId));
    }
    public sealed partial class NavTraversalArea
    {
        private NavDeclarationStorage owned;
        NavOwnedStorage INavStorageFacade.StorageRoot=>owned;
        internal NavOwnedStorage StorageRoot=>owned;
        private NavTraversalArea(NavTraversalArea source,NavPolygon boundary,NavGeometry geometry)
        {Boundary=boundary;this.geometry=geometry;Blockers=geometry.Obstacles;HalfExtent=source.HalfExtent;EstimatedRetainedBytes=source.EstimatedRetainedBytes;}
        internal static NavTraversalArea CopyOwned(NavTraversalArea source,NavProviderCopy copy)
        {
            var boundary=copy.Polygon(source.Boundary);var geometry=copy.Geometry(source.geometry);
            NavStorageAdmission.RequireOwned(copy.Reservation,boundary.StorageRoot);NavStorageAdmission.RequireOwned(copy.Reservation,geometry.StorageRoot);
            using(var allocation=new NavStorageAllocation(copy.Reservation.TryReserve)){
                allocation.Reserve(256);var storage=allocation.Register(new NavDeclarationStorage(256));var value=new NavTraversalArea(source,boundary,geometry){owned=storage};storage.Facade=value;
                boundary.StorageRoot.Retain();storage.First=boundary.StorageRoot;geometry.StorageRoot.Retain();storage.Second=geometry.StorageRoot;storage.Retain();return value;
            }
        }
    }
    public sealed partial class NavLayerSurface
    {
        private NavDeclarationStorage owned;
        NavOwnedStorage INavStorageFacade.StorageRoot=>owned;
        internal NavOwnedStorage StorageRoot=>owned;
        private NavLayerSurface(NavLayerSurface source,NavTraversalArea area,long bytes)
        {Id=NavProviderCopy.Text(source.Id);CombatFamily=NavProviderCopy.Text(source.CombatFamily);Height=source.Height;DistanceCost=source.DistanceCost;Area=area;}
        internal static NavLayerSurface CopyOwned(NavLayerSurface source,NavProviderCopy copy)
        {
            var area=copy.Area(source.Area);NavStorageAdmission.RequireOwned(copy.Reservation,area.StorageRoot);
            long bytes=checked(192+NavProviderCopy.TextBytes(source.Id)+NavProviderCopy.TextBytes(source.CombatFamily));
            using(var allocation=new NavStorageAllocation(copy.Reservation.TryReserve)){
                allocation.Reserve(bytes);var storage=allocation.Register(new NavDeclarationStorage(bytes));var value=new NavLayerSurface(source,area,bytes){owned=storage};storage.Facade=value;area.StorageRoot.Retain();storage.First=area.StorageRoot;storage.Retain();return value;
            }
        }
    }
    public sealed partial class NavLayerPortal
    {
        private NavDeclarationStorage owned;
        NavOwnedStorage INavStorageFacade.StorageRoot=>owned;
        internal NavOwnedStorage StorageRoot=>owned;
        private NavRetainedArray<string> ownedMobility;
        private NavLayerPortal(NavLayerPortal source,NavTraversalArea area,long bytes)
        {Id=NavProviderCopy.Text(source.Id);Entry=NavProviderCopy.Location(source.Entry);Exit=NavProviderCopy.Location(source.Exit);Direction=source.Direction;Open=source.Open;TraversalCost=source.TraversalCost;Corridor=area;}
        internal static NavLayerPortal CopyOwned(NavLayerPortal source,NavProviderCopy copy)
        {
            var area=copy.Area(source.Corridor);NavStorageAdmission.RequireOwned(copy.Reservation,area.StorageRoot);
            long bytes=checked(256+NavProviderCopy.TextBytes(source.Id)+NavProviderCopy.TextBytes(source.Entry.SurfaceId)+NavProviderCopy.TextBytes(source.Exit.SurfaceId));
            for(int n=0;n<source.MobilityClasses.Count;n++)bytes=checked(bytes+NavProviderCopy.TextBytes(source.MobilityClasses[n]));
            using(var allocation=new NavStorageAllocation(copy.Reservation.TryReserve)){
                allocation.Reserve(bytes);var storage=allocation.Register(new NavDeclarationStorage(bytes));var value=new NavLayerPortal(source,area,bytes){owned=storage};storage.Facade=value;value.BindOwnedLocations();area.StorageRoot.Retain();storage.First=area.StorageRoot;
                value.ownedMobility=NavRetainedArray<string>.Copy(new MobilityCopy(source.MobilityClasses),copy.Reservation.TryReserve);value.MobilityClasses=value.ownedMobility;storage.Second=value.ownedMobility;storage.Retain();return value;
            }
        }
        private sealed class MobilityCopy : IReadOnlyList<string>
        {
            private readonly IReadOnlyList<string> source;
            internal MobilityCopy(IReadOnlyList<string> source){this.source=source;}
            public int Count=>source.Count;public string this[int n]=>NavProviderCopy.Text(source[n]);
            public IEnumerator<string> GetEnumerator(){for(int n=0;n<Count;n++)yield return this[n];}
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
        }
    }
    public sealed partial class LayeredNavigationProvider
    {
        private NavDeclarationStorage owned;
        NavOwnedStorage INavStorageFacade.StorageRoot=>owned;
        internal NavOwnedStorage StorageRoot=>owned;
        private NavRetainedArray<NavLayerSurface> ownedSurfaces;
        private NavRetainedArray<NavLayerPortal> ownedPortals;
        private NavCowDictionary<string,NavLayerSurface> ownedSurfaceIndex;
        private NavCowDictionary<string,NavLayerPortal> ownedPortalIndex;
        private LayeredNavigationProvider(LayeredNavigationProvider source,long bytes)
        {Id=NavProviderCopy.Text(source.Id);Revision=source.Revision;SemanticsVersion=source.SemanticsVersion;TopologyRevision=source.TopologyRevision;ExactMapBinding=NavProviderCopy.Text(source.ExactMapBinding);Binding=NavProviderCopy.Text(source.Binding);EstimatedRetainedBytes=source.EstimatedRetainedBytes;}
        internal static LayeredNavigationProvider CopyOwned(LayeredNavigationProvider source,INavStorageReservation reservation)
        {
            if(source==null||reservation==null)throw new ArgumentNullException();
            // Declaration counts are bounded by the validated public provider.
            // Geometry identity maps and copy facades are held scratch from birth.
            long scratch=4096;
            foreach(var surface in source.Surfaces)scratch=checked(scratch+4096+256L*surface.Area.Blockers.Count);
            foreach(var portal in source.Portals)scratch=checked(scratch+4096+256L*portal.Corridor.Blockers.Count+64L*portal.MobilityClasses.Count);
            using(NavStorageScratch.Reserve(reservation,scratch))
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                long bytes=checked(512+NavProviderCopy.TextBytes(source.Id)+NavProviderCopy.TextBytes(source.ExactMapBinding)+NavProviderCopy.TextBytes(source.Binding));
                allocation.Reserve(bytes);var storage=allocation.Register(new NavDeclarationStorage(bytes));var value=new LayeredNavigationProvider(source,bytes){owned=storage};storage.Facade=value;var copy=new NavProviderCopy(reservation);
                value.ownedSurfaces=NavRetainedArray<NavLayerSurface>.Copy(new SurfaceCopy(source.Surfaces,copy),reservation.TryReserve);
                storage.First=value.ownedSurfaces;
                value.ownedPortals=NavRetainedArray<NavLayerPortal>.Copy(new PortalCopy(source.Portals,copy),reservation.TryReserve);
                storage.Second=value.ownedPortals;
                value.ownedSurfaceIndex=NavCowDictionary<string,NavLayerSurface>.CreateOwned(reservation,StringComparer.Ordinal,false);
                storage.Third=value.ownedSurfaceIndex;
                value.ownedPortalIndex=NavCowDictionary<string,NavLayerPortal>.CreateOwned(reservation,StringComparer.Ordinal,false);
                storage.Fourth=value.ownedPortalIndex;
                foreach(var surface in value.ownedSurfaces)value.ownedSurfaceIndex.Add(surface.Id,surface);
                foreach(var portal in value.ownedPortals)value.ownedPortalIndex.Add(portal.Id,portal);
                value.ownedSurfaceIndex.Seal();value.ownedPortalIndex.Seal();value.Surfaces=value.ownedSurfaces;value.Portals=value.ownedPortals;value.surfaces=value.ownedSurfaceIndex;value.portals=value.ownedPortalIndex;storage.Retain();return value;
            }
        }
        private sealed class SurfaceCopy : IReadOnlyList<NavLayerSurface>
        {
            private readonly IReadOnlyList<NavLayerSurface> source;private readonly NavProviderCopy copy;
            internal SurfaceCopy(IReadOnlyList<NavLayerSurface> source,NavProviderCopy copy){this.source=source;this.copy=copy;}
            public int Count=>source.Count;public NavLayerSurface this[int n]=>copy.Surface(source[n]);
            public IEnumerator<NavLayerSurface> GetEnumerator(){for(int n=0;n<Count;n++)yield return this[n];}
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        private sealed class PortalCopy : IReadOnlyList<NavLayerPortal>
        {
            private readonly IReadOnlyList<NavLayerPortal> source;private readonly NavProviderCopy copy;
            internal PortalCopy(IReadOnlyList<NavLayerPortal> source,NavProviderCopy copy){this.source=source;this.copy=copy;}
            public int Count=>source.Count;public NavLayerPortal this[int n]=>copy.Portal(source[n]);
            public IEnumerator<NavLayerPortal> GetEnumerator(){for(int n=0;n<Count;n++)yield return this[n];}
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
        }
    }
}
