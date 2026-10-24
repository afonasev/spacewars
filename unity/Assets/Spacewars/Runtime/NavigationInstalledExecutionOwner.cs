using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Immutable historical edges. The Session-only Detached flag belongs to the
    // owned live record; it is deliberately outside this BODY payload.
    internal sealed class NavigationInstalledExecutionOwner : NavOwnedStorage,INavPhysicalBody
    {
        internal static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationInstalledExecutionOwner))+NavStorageLayout.ObjectBytesBound(typeof(NavigationInstalledExecutionState));
        internal InstalledGeometryCertificateV2 Certificate;internal NavGeometry CurrentGeometry=>Certificate?.Current??Request.Request.BaseGeometry;
        internal readonly int Entity;internal NavigationHostRequest Request;internal NavigationHostCorridor Corridor;internal NavRetainedArray<NavPoint> Route;
        internal NavigationInstalledExecutionState State{get;private set;}
        internal override bool IsImmutableCaptureRoot=>Request!=null&&Corridor!=null&&Route!=null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.InstalledExecution,value:Certificate==null?NavPhysicalValueKind.None:NavPhysicalValueKind.InstalledExecutionV2);
        private NavigationInstalledExecutionOwner(int entity,bool owned):base(Layout){Entity=entity;if(owned)Retain();}
        internal static NavigationInstalledExecutionOwner CarryOwned(NavigationInstalledExecutionOwner source,InstalledGeometryCertificateV2 certificate,INavStorageReservation reservation)=>Create(source.Entity,source.Request,source.Corridor,source.Route,false,reservation,true,certificate);
        private static void Validate(int entity,NavigationHostRequest request,NavigationHostCorridor corridor,NavRetainedArray<NavPoint> route,INavStorageReservation reservation)
        {
            if(request==null||corridor==null||route==null||entity!=request.Request.Entity||entity!=corridor.Corridor.Entity||!ReferenceEquals(request,corridor.RequestOwner))throw new ArgumentException("Installed historical identity mismatch.");
            NavStorageAdmission.RequireOwned(reservation,request);NavStorageAdmission.RequireOwned(reservation,corridor);NavStorageAdmission.RequireOwned(reservation,route);
            int at=0;foreach(var leg in corridor.TypedRoute.Legs)if(leg.Kind==NavTypedLegKind.Surface){if(at>=route.Count-1||!route[at++].Equals(leg.To.Position))throw new ArgumentException("Installed route differs from corridor.");}
            if(at+1!=route.Count||!route[at].Equals(request.Request.Goal))throw new ArgumentException("Installed endpoint differs from request.");
        }
        private static NavigationInstalledExecutionOwner Create(int entity,NavigationHostRequest request,NavigationHostCorridor corridor,NavRetainedArray<NavPoint> route,bool detached,INavStorageReservation reservation,bool owned,InstalledGeometryCertificateV2 certificate=null)
        {
            if(certificate!=null){NavStorageAdmission.RequireOwned(reservation,certificate);if(!certificate.Binds(request,corridor,route))throw new ArgumentException("Certificate does not bind installed proof.");}
            Validate(entity,request,corridor,route,reservation);
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(Layout);var value=allocation.Register(new NavigationInstalledExecutionOwner(entity,owned));
                try{
                    value.Certificate=NavPhysicalMacro.Acquire(value,certificate);value.Request=NavPhysicalMacro.Acquire(value,request);value.Corridor=NavPhysicalMacro.Acquire(value,corridor);value.Route=NavPhysicalMacro.Acquire(value,route);
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
        {if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(Layout))throw new ArgumentException("Invalid installed historical owner.");writer.Output.Write(Entity);NavPhysicalMacro.WriteEdge(writer,this,Request);NavPhysicalMacro.WriteEdge(writer,this,Corridor);NavPhysicalMacro.WriteEdge(writer,this,Route);if(Certificate!=null)NavPhysicalMacro.WriteEdge(writer,this,Certificate);}
        internal static NavigationInstalledExecutionOwner ReadPhysicalBody(NavPhysicalBodyReader reader,bool v2=false)
        {int entity=reader.Input.ReadInt32();var request=NavPhysicalMacro.ReadEdge<NavigationHostRequest>(reader);var corridor=NavPhysicalMacro.ReadEdge<NavigationHostCorridor>(reader);var route=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavPoint>>(reader);var certificate=v2?NavPhysicalMacro.ReadEdge<InstalledGeometryCertificateV2>(reader):null;return Create(entity,request,corridor,route,false,reader.Reservation,false,certificate);}
        protected override void ReleaseEdges(){Certificate?.Release();Certificate=null;Request?.Release();Corridor?.Release();Route?.Release();Request=null;Corridor=null;Route=null;State=null;}
    }
}
