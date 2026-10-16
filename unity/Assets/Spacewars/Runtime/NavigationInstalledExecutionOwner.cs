using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Immutable historical edges. The Session-only Detached flag belongs to the
    // owned live record; it is deliberately outside this BODY payload.
    internal sealed class NavigationInstalledExecutionOwner : NavOwnedStorage,INavPhysicalBody
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationInstalledExecutionOwner))+NavStorageLayout.ObjectBytesBound(typeof(NavigationInstalledExecutionState));
        internal readonly int Entity;internal NavigationHostRequest Request;internal NavigationHostCorridor Corridor;internal NavRetainedArray<NavPoint> Route;
        internal NavigationInstalledExecutionState State{get;private set;}
        internal override bool IsImmutableCaptureRoot=>Request!=null&&Corridor!=null&&Route!=null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.InstalledExecution);
        private NavigationInstalledExecutionOwner(int entity,bool owned):base(Layout){Entity=entity;if(owned)Retain();}
        private static void Validate(int entity,NavigationHostRequest request,NavigationHostCorridor corridor,NavRetainedArray<NavPoint> route,INavStorageReservation reservation)
        {
            if(request==null||corridor==null||route==null||entity!=request.Request.Entity||entity!=corridor.Corridor.Entity||!ReferenceEquals(request,corridor.RequestOwner))throw new ArgumentException("Installed historical identity mismatch.");
            NavStorageAdmission.RequireOwned(reservation,request);NavStorageAdmission.RequireOwned(reservation,corridor);NavStorageAdmission.RequireOwned(reservation,route);
            int at=0;foreach(var leg in corridor.TypedRoute.Legs)if(leg.Kind==NavTypedLegKind.Surface){if(at>=route.Count-1||!route[at++].Equals(leg.To.Position))throw new ArgumentException("Installed route differs from corridor.");}
            if(at+1!=route.Count||!route[at].Equals(request.Request.Goal))throw new ArgumentException("Installed endpoint differs from request.");
        }
        private static NavigationInstalledExecutionOwner Create(int entity,NavigationHostRequest request,NavigationHostCorridor corridor,NavRetainedArray<NavPoint> route,bool detached,INavStorageReservation reservation,bool owned)
        {
            Validate(entity,request,corridor,route,reservation);
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(Layout);var value=allocation.Register(new NavigationInstalledExecutionOwner(entity,owned));
                try{
                    value.Request=NavPhysicalMacro.Acquire(value,request);value.Corridor=NavPhysicalMacro.Acquire(value,corridor);value.Route=NavPhysicalMacro.Acquire(value,route);
                    value.State=new NavigationInstalledExecutionState{StorageOwner=value,Entity=entity,Detached=false,Corridor=corridor.Corridor,InstalledRoute=route.ReadOnlyBacking,Profile=request.Request.Profile,MovementGeometry=request.Request.Geometry,BaseGeometry=request.Request.BaseGeometry,HeldIdentity=request.Request.HeldIdentity};return value;
                }catch{if(owned)value.Release();throw;}
            }
        }
        internal static NavigationInstalledExecutionOwner CreateOwned(NavigationHostAnswer answer,NavRetainedArray<NavPoint> route,bool detached,INavStorageReservation reservation)
        {
            if(answer?.CorridorOwner==null)throw new ArgumentException("Ready installed execution needs its exact host corridor owner.");NavStorageAdmission.RequireOwned(reservation,answer);
            return Create(answer.RequestOwner.Request.Entity,answer.RequestOwner,answer.CorridorOwner,route,detached,reservation,true);
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(Layout))throw new ArgumentException("Invalid installed historical owner.");writer.Output.Write(Entity);NavPhysicalMacro.WriteEdge(writer,this,Request);NavPhysicalMacro.WriteEdge(writer,this,Corridor);NavPhysicalMacro.WriteEdge(writer,this,Route);}
        internal static NavigationInstalledExecutionOwner ReadPhysicalBody(NavPhysicalBodyReader reader)
        {int entity=reader.Input.ReadInt32();var request=NavPhysicalMacro.ReadEdge<NavigationHostRequest>(reader);var corridor=NavPhysicalMacro.ReadEdge<NavigationHostCorridor>(reader);var route=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavPoint>>(reader);return Create(entity,request,corridor,route,false,reader.Reservation,false);}
        protected override void ReleaseEdges(){Request?.Release();Corridor?.Release();Route?.Release();Request=null;Corridor=null;Route=null;State=null;}
    }
}
