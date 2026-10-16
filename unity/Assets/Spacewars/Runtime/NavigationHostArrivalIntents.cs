using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Owns the exact product-facing intent array as well as its physical
    // request edges. The Session borrows this immutable array after publish.
    internal sealed class NavigationHostArrivalIntents : NavOwnedStorage,INavPhysicalBody
    {
        private static long Layout(int count)=>checked(NavStorageLayout.ObjectBytesBound(typeof(NavigationHostArrivalIntents))+32L+8L*count);
        private NavRetainedArray<NavOwnedStorage> owners;
        internal NavigationRequest[] Requests {get;private set;}
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.HostArrivalIntents);
        internal override bool IsImmutableCaptureRoot=>owners!=null&&Requests!=null;
        private NavigationHostArrivalIntents(int count,bool retained):base(Layout(count))
        {if(retained)Retain();}
        private static NavigationHostArrivalIntents Allocate(int count,INavStorageReservation reservation,bool retained)
        {
            using(var allocation=new NavStorageAllocationContext(reservation.TryReserve)){
                allocation.Reserve(Layout(count));var value=new NavigationHostArrivalIntents(count,retained);allocation.Register(value);return value;
            }
        }

        private void Own(NavRetainedArray<NavOwnedStorage> values)
        {
            owners=NavPhysicalMacro.Acquire(this,values);Requests=new NavigationRequest[values.Count];
            for(int n=0;n<Requests.Length;n++){
                if(!(values[n] is NavigationHostRequest request))throw new ArgumentException("Invalid physical original intent view.");
                Requests[n]=request.Request;
            }
        }
        internal static NavigationHostArrivalIntents CreateOwned(NavOwnedStorage[] values,INavStorageReservation reservation)
        {
            foreach(var value in values)NavStorageAdmission.RequireOwned(reservation,value);
            var owner=Allocate(values.Length,reservation,true);NavRetainedArray<NavOwnedStorage> rows=null;
            try{rows=NavRetainedArray<NavOwnedStorage>.Copy(values,reservation.TryReserve);owner.Own(rows);return owner;}
            catch{owner.Release();throw;}finally{rows?.Release();}
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(StorageBytes!=NavStorageLayout.Charge(Layout(owners.Count))||Requests.Length!=owners.Count)throw new ArgumentException("Invalid physical intent view layout.");
            for(int n=0;n<Requests.Length;n++)if(!ReferenceEquals(Requests[n],((NavigationHostRequest)owners[n]).Request))throw new ArgumentException("Original intent view alias changed.");
            writer.Output.Write(Requests.Length);NavPhysicalMacro.WriteEdge(writer,this,owners);
        }
        internal static NavigationHostArrivalIntents ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            int count=reader.Count(WorldWire.MaxItems);var rows=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavOwnedStorage>>(reader);
            if(count==0||rows.Count!=count)throw new ArgumentException("Invalid original intent view shape.");
            var owner=Allocate(count,reader.Reservation,false);owner.Own(rows);return owner;
        }
        protected override void ReleaseEdges(){owners?.Release();Requests=null;}
    }
    // A preparation owns the private observation through the short authority
    // callback, then its birth/local references release outside transportGate.
    internal sealed class NavigationPreparedArrivalObservation : NavOwnedStorage
    {
        internal NavigationRequest[] Expected {get;private set;}
        internal GroupOrderState Group {get;private set;}
        internal NavArrivalOccupancySnapshot Occupancy {get;private set;}
        private readonly NavOwnedStorage[] pins;private int pinCount;
        private TerrainLocations pointLocations;private LayeredNavigationProvider pointLayers;
        private NavRetainedArray<NavNameReference> pointNames;private IPlayableTerrain compatibilityTerrain;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationPreparedArrivalObservation(long bytes,int pinCapacity):base(bytes){pins=new NavOwnedStorage[pinCapacity];Retain();}
        internal static long Layout(int rows,int members,int occupants)=>checked(NavStorageLayout.ObjectBytesBound(typeof(NavigationPreparedArrivalObservation))+64L+8L*rows+8L*(rows+8+2*occupants)+NavStorageLayout.ObjectBytesBound(typeof(GroupOrderState))+32L+8L*members+(long)NavStorageLayout.ObjectBytesBound(typeof(GroupMemberState))*members+160L+NavStorageLayout.ObjectBytesBound(typeof(NavArrivalOccupancySnapshot))+NavStorageLayout.ObjectBytesBound(typeof(System.Collections.ObjectModel.ReadOnlyCollection<NavArrivalOccupant>))+64L+(16L+NavStorageLayout.ObjectBytesBound(typeof(NavArrivalOccupant))+64L+2L*(256+64))*occupants);
        internal static NavigationPreparedArrivalObservation CreateOwned(int rows,int members,int occupants,INavStorageReservation reservation)
        {
            long bytes=Layout(rows,members,occupants);
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(bytes);return allocation.Register(new NavigationPreparedArrivalObservation(bytes,checked(rows+8+2*occupants)));}
        }
        // The carrier is allocated off gate. Exact source edges are acquired
        // under the source observation gate, before any borrowed value is read.
        internal void Pin(NavOwnedStorage root){if(root==null)return;RequireEdge(root);if(pinCount==pins.Length)throw new InvalidOperationException("Arrival observation pin capacity exceeded.");root.Retain();pins[pinCount++]=root;}
        internal void Observe(NavigationRequest[] rows,GroupOrderState group){Expected=rows;Group=group;foreach(var request in rows)Pin(request.StorageOwner);}
        internal void ObserveTerrain(IPlayableTerrain terrain,NavRetainedArray<NavNameReference> names)
        {
            pointNames=names;
            if(terrain==null)return;
            if(terrain is INavArrivalPointTerrain partition){pointLocations=partition.ArrivalLocations;Pin(pointLocations.StorageRoot);}
            else if(terrain is ILayeredPlayableTerrain layered&&layered.LayeredNavigation is LayeredNavigationProvider provider){pointLayers=provider;Pin(provider.StorageRoot);}
            else compatibilityTerrain=terrain;
        }
        internal bool LocatePoint(NavPoint point,out NavLocation location)
        {
            location=default;if(double.IsNaN(point.X)||double.IsInfinity(point.X)||double.IsNaN(point.Z)||double.IsInfinity(point.Z))return false;
            if(pointLocations!=null){if(!pointLocations.TryLocatePoint(point,out location))return false;}
            else if(pointLayers!=null){if(!pointLayers.TryLocatePoint(point,out location))return false;}
            else if(compatibilityTerrain!=null){if(!compatibilityTerrain.TryLocate(point,0,out location))return false;}
            else location=new NavLocation(point,NavLocation.FlatSurface);
            if(pointNames==null)return true;
            if(location.NameReference.Owner!=null){pointNames.RequireEdge(location.NameReference.Owner);return true;}
            if(location.SurfaceId==NavLocation.FlatSurface)return true;
            for(int n=0;n<pointNames.Count;n++)if(pointNames[n].Resolve()==location.SurfaceId){location=new NavLocation(point,pointNames[n]);return true;}
            throw new InvalidOperationException("Terrain returned an undeclared actor surface.");
        }
        internal void ObserveOccupancy(NavArrivalOccupancySnapshot value){Occupancy=value;foreach(var occupant in value.Occupants){Pin(occupant.Location.NameReference.Owner);Pin(occupant.ActorTarget?.NameReference.Owner);}}
        protected override void ReleaseEdges(){foreach(var root in pins)root?.Release();Expected=null;Group=null;Occupancy=null;pointLocations=null;pointLayers=null;pointNames=null;compatibilityTerrain=null;}
    }
}
