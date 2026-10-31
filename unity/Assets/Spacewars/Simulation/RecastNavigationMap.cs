using System;

namespace Spacewars.Simulation
{
    // Owner creates and publishes once, then replaces the whole map version.
    public sealed class RecastNavigationMap:IDisposable
    {
        internal readonly RecastMapHandle Handle;
        public RecastAuthorityGeometry Geometry{get;}
        public string Binding=>Geometry.Binding;
        public RecastNavigationMap(RecastAuthorityGeometry geometry,Func<bool> cancelled=null):this(geometry,cancelled,null){}
        internal RecastNavigationMap(RecastAuthorityGeometry geometry,Func<bool> cancelled,NavOwnedStorage payload)
        {
            Geometry=geometry??throw new ArgumentNullException(nameof(geometry));
            if(RecastNative.sw_recast_abi()!=1)throw new NotSupportedException("Recast native ABI mismatch.");
            // Height/climb/slope here are raster technical guards; authority TryTraverse
            // remains the gameplay predicate. No artificial step edges are introduced.
            Exception cancellationError=null;
            RecastNative.Cancelled cancellation=()=>{try{return cancelled?.Invoke()==true?1:0;}catch(Exception error){cancellationError=error;return 1;}};
            if(geometry.IsLayered&&RecastNative.sw_recast_layer_abi()!=1)throw new NotSupportedException("Recast layer ABI mismatch.");
            Handle=new RecastMapHandle(geometry.IsLayered?RecastNative.sw_recast_build_layered(geometry.Vertices,geometry.Vertices.Length/3,geometry.Triangles,geometry.Areas,geometry.Areas.Length,geometry.Directed,geometry.CellSize,geometry.CellHeight,geometry.Radius,geometry.CellHeight*2,geometry.CellHeight*2,89,cancellation,geometry.Links,geometry.LinkAreas,geometry.LinkDirections,geometry.LinkIds,geometry.LinkIds.Length):RecastNative.sw_recast_build(geometry.Vertices,geometry.Vertices.Length/3,geometry.Triangles,geometry.Areas,geometry.Areas.Length,geometry.Directed,geometry.CellSize,geometry.CellHeight,geometry.Radius,geometry.CellHeight*2,geometry.CellHeight*2,89,cancellation),payload);
            if(Handle.IsInvalid){Handle.Dispose();RecastNative.CheckCapacity();if(cancellationError!=null)throw new InvalidOperationException("Recast cancellation callback failed.",cancellationError);if(cancelled?.Invoke()==true)throw new OperationCanceledException();throw new InvalidOperationException("Recast map resource/preparation failure.");}
        }
        public RecastNavigationQuery Query(NavLocation start,NavLocation goal){
            RecastPayload payload=null;
            try{var r=RecastNative.CurrentMemoryOwner;if(r!=null)payload=RecastPayload.Allocate(r,checked(NavStorageLayout.ObjectBytesBound(typeof(RecastNavigationQuery))+NavStorageLayout.ObjectBytesBound(typeof(RecastQueryHandle))+512+4L*(start.SurfaceId.Length+goal.SurfaceId.Length)));return new RecastNavigationQuery(this,start,goal,payload);}
            finally{payload?.Dispose();}
        }
        public void Dispose()=>Handle.Dispose();
    }
    public readonly struct RecastRoutePoint
    {
        public RecastRoutePoint(NavLocation location,double height,string portalId=null){Location=location;Height=height;PortalId=portalId;}
        public NavLocation Location{get;}public double Height{get;}public string PortalId{get;}
    }
    public sealed class RecastNavigationQuery:IDisposable
    {
        private readonly RecastQueryHandle handle;private readonly RecastAuthorityGeometry geometry;private readonly NavLocation origin,endpoint,nativeOrigin,nativeEndpoint;
        public RecastQueryStatus Status{get;private set;}
        public long Iterations{get;private set;}private bool disposed;
        internal RecastNavigationQuery(RecastNavigationMap map,NavLocation start,NavLocation goal,NavOwnedStorage payload)
        {geometry=map.Geometry;origin=new NavLocation(start.Position,new string(start.SurfaceId.ToCharArray()));endpoint=new NavLocation(goal.Position,new string(goal.SurfaceId.ToCharArray()));nativeOrigin=geometry.NativeEndpoint(origin,endpoint);nativeEndpoint=geometry.NativeEndpoint(endpoint,origin);handle=new RecastQueryHandle(map.Handle,geometry.Point(nativeOrigin),geometry.Point(nativeEndpoint),geometry.Area(nativeOrigin.SurfaceId),geometry.Area(nativeEndpoint.SurfaceId),payload);}
        public RecastQueryStatus Step(int iterations)
        {
            if(disposed)throw new ObjectDisposedException(nameof(RecastNavigationQuery));
            if(iterations<1||iterations>4096)throw new ArgumentOutOfRangeException(nameof(iterations));
            if(Status!=RecastQueryStatus.Pending)return Status;
            Status=RecastNative.sw_recast_step(handle,iterations,out int consumed);Iterations+=consumed;return Status;
        }
        public RecastRoutePoint[] CopyTypedRoute()
        {
            if(disposed)throw new ObjectDisposedException(nameof(RecastNavigationQuery));
            if(Status!=RecastQueryStatus.Full)throw new InvalidOperationException("No complete Recast route.");
            if(geometry.DirectPhysical(origin,endpoint))return new[]{new RecastRoutePoint(origin,geometry.Point(origin)[1]),new RecastRoutePoint(endpoint,geometry.Point(endpoint)[1])};
            var data=new float[4096*3];var state=RecastNative.sw_recast_copy(handle,data,4096,out int count);
            if(state!=RecastQueryStatus.Full||count<1||count>4096)throw new InvalidOperationException("Recast result resource failure.");
            var areas=new byte[count];state=RecastNative.sw_recast_copy_areas(handle,areas,count,out int areaCount);
            if(state!=RecastQueryStatus.Full||areaCount!=count)throw new InvalidOperationException("Recast semantic result mismatch.");
            if(!geometry.IsLayered){var result=new RecastRoutePoint[count];for(int i=0;i<count;i++)result[i]=new RecastRoutePoint(new NavLocation(new NavPoint(data[i*3],data[i*3+2]),geometry.Surface(areas[i])),data[i*3+1]);return result;}
            var ids=new uint[count];if(RecastNative.sw_recast_copy_portals(handle,ids,count,out int portalCount)!=RecastQueryStatus.Full||portalCount!=count)throw new InvalidOperationException("Recast portal provenance mismatch.");
            var typed=new System.Collections.Generic.List<RecastRoutePoint>();string surface=null;
            var prefix=geometry.NativeAttachment(origin,nativeOrigin);if(prefix!=null)foreach(var leg in prefix)typed.Add(new RecastRoutePoint(leg.To,geometry.Point(leg.To)[1],leg.Kind==NavTypedLegKind.Portal?leg.PortalId:null));
            for(int i=0;i<count;i++){
                if(ids[i]!=0){var portal=geometry.Portal(ids[i]);bool forward=surface==portal.Entry.SurfaceId;if(!forward&&(surface!=portal.Exit.SurfaceId||portal.Direction!=NavPortalDirection.Both))throw new InvalidOperationException("Recast directed portal provenance mismatch.");var entry=forward?portal.Entry:portal.Exit;var exit=forward?portal.Exit:portal.Entry;
                    typed.Add(new RecastRoutePoint(entry,geometry.Point(entry)[1]));var expansion=geometry.ExpandPortal(ids[i],forward);if(expansion==null)typed.Add(new RecastRoutePoint(exit,geometry.Point(exit)[1],portal.Id));else foreach(var leg in expansion)typed.Add(new RecastRoutePoint(leg.To,geometry.Point(leg.To)[1],leg.Kind==NavTypedLegKind.Portal?leg.PortalId:null));surface=exit.SurfaceId;
                }else{var location=new NavLocation(new NavPoint(data[i*3],data[i*3+2]),geometry.Surface(areas[i]));typed.Add(new RecastRoutePoint(location,data[i*3+1]));surface=location.SurfaceId;}
            }
            var suffix=geometry.NativeAttachment(nativeEndpoint,endpoint);if(suffix!=null)foreach(var leg in suffix)typed.Add(new RecastRoutePoint(leg.To,geometry.Point(leg.To)[1],leg.Kind==NavTypedLegKind.Portal?leg.PortalId:null));
            if(typed.Count>8192)throw new InvalidOperationException("Recast typed point capacity exceeded.");return typed.ToArray();
        }
        public void Dispose(){if(disposed)return;disposed=true;handle.Dispose();}
    }
}
