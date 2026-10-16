using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // A terminal answer owns its exact request; Ready also owns the physical
    // point array and typed corridor. Its public facade is charged here.
    internal sealed class NavigationHostAnswer : NavOwnedStorage, INavPhysicalBody
    {
        private static readonly long LayoutBase=Math.Max(256,
            NavStorageLayout.ObjectBytesBound(typeof(NavigationHostAnswer))+
            NavStorageLayout.ObjectBytesBound(typeof(NavigationAnswer))+32);
        private NavigationHostRequest requestRoot;
        private NavRetainedArray<NavPoint> routeRoot;
        private NavigationHostCorridor corridorRoot;
        internal NavigationAnswer Answer {get;private set;}
        internal NavigationHostRequest RequestOwner=>requestRoot;
        internal NavigationHostCorridor CorridorOwner=>corridorRoot;
        private bool released;
        internal override bool IsImmutableCaptureRoot=>Answer!=null;
        private bool LegacyReady=>Answer?.Status==NavSolveStatus.Ready&&routeRoot!=null&&corridorRoot==null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(LegacyReady?NavPhysicalStorageKind.LegacyProbeAnswer:NavPhysicalStorageKind.HostAnswer);
        private static long Charge(int count)=>checked(LayoutBase+16L*count);
        private NavigationHostAnswer(int count,bool ownerReference):base(Charge(count))
        {if(ownerReference)Retain();}
        private static NavigationHostAnswer Allocate(int count,INavStorageReservation reservation,bool ownerReference)
        {
            using(var allocation=new NavStorageAllocationContext(reservation.TryReserve)){
                allocation.Reserve(Charge(count));var value=new NavigationHostAnswer(count,ownerReference);allocation.Register(value);return value;
            }
        }

        internal static NavigationHostAnswer CreateTerminal(NavigationHostRequest request,NavSolveStatus status,
            INavStorageReservation reservation)
        {
            if(request==null||reservation==null||!Enum.IsDefined(typeof(NavSolveStatus),status)||
                status==NavSolveStatus.Pending||status==NavSolveStatus.Ready)
                throw new ArgumentException("Invalid terminal physical answer.");
            NavStorageAdmission.RequireOwned(reservation,request);
            var owner=Allocate(0,reservation,true);
            try{
                owner.requestRoot=NavPhysicalMacro.Acquire(owner,request);
                owner.Answer=new NavigationAnswer(request.Request,Array.Empty<NavPoint>(),status);owner.Answer.BindStorageOwner(owner);
                return owner;
            }catch{owner.ReleaseStorage();throw;}
        }
        internal static NavigationHostAnswer CreateReady(NavigationHostRequest request,
            NavigationHostCorridor corridor,NavPoint[] route,INavStorageReservation reservation)
        {
            if(request==null||corridor==null||route==null||route.Length==0||reservation==null||
                !ReferenceEquals(corridor.RequestOwner,request))
                throw new ArgumentException("Invalid Ready physical answer.");
            NavStorageAdmission.RequireOwned(reservation,request);
            NavStorageAdmission.RequireOwned(reservation,corridor);
            int at=0;
            foreach(var leg in corridor.TypedRoute.Legs)
                if(leg.Kind==NavTypedLegKind.Surface){
                    if(at==route.Length||!route[at++].Equals(leg.To.Position))
                        throw new ArgumentException("Ready points differ from physical route.");
                }
            if(at!=route.Length)throw new ArgumentException("Ready point count differs from physical route.");
            using(NavStorageScratch.Reserve(reservation,checked(256L+16L*route.Length))){
                NavRetainedArray<NavPoint> points=null;
                NavigationHostAnswer owner=null;
                try{
                    points=NavRetainedArray<NavPoint>.Copy(route,reservation.TryReserve);
                    owner=Allocate(points.Count,reservation,true);
                    owner.requestRoot=NavPhysicalMacro.Acquire(owner,request);
                    owner.routeRoot=NavPhysicalMacro.Acquire(owner,points);
                    owner.corridorRoot=NavPhysicalMacro.Acquire(owner,corridor);
                    owner.Answer=new NavigationAnswer(request.Request,points.ReadOnlyBacking,
                        NavSolveStatus.Ready,corridor.Corridor);owner.Answer.BindStorageOwner(owner);
                    return owner;
                }catch{owner?.ReleaseStorage();throw;}
                finally{points?.Release();}
            }
        }
        internal static NavigationHostAnswer CreateLegacyProbeReady(NavigationHostRequest request,NavPoint[] route,INavStorageReservation r)
        {
            if(request==null||request.Request.Entity>=0||request.Request.CertifiedProviderBinding!=null||route==null)throw new ArgumentException("Invalid explicit legacy producer receipt.");NavStorageAdmission.RequireOwned(r,request);
            NavRetainedArray<NavPoint> points=null;NavigationHostAnswer owner=null;
            try{points=NavRetainedArray<NavPoint>.Copy(route,r.TryReserve);owner=Allocate(points.Count,r,true);owner.requestRoot=NavPhysicalMacro.Acquire(owner,request);owner.routeRoot=NavPhysicalMacro.Acquire(owner,points);owner.Answer=new NavigationAnswer(request.Request,points.ReadOnlyBacking,NavSolveStatus.Ready);owner.Answer.BindStorageOwner(owner);return owner;}catch{owner?.ReleaseStorage();throw;}finally{points?.Release();}
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(Answer==null||!ReferenceEquals(Answer.Request,requestRoot.Request)||
                Answer.Status==NavSolveStatus.Pending||
                (Answer.Status==NavSolveStatus.Ready)!=(routeRoot!=null&&(corridorRoot!=null||LegacyReady))||
                LegacyReady&&(Answer.Request.Entity>=0||Answer.Request.CertifiedProviderBinding!=null)||
                (routeRoot==null&&(Answer.RouteLength!=0||Answer.Corridor!=null))||
                (routeRoot!=null&&((!LegacyReady&&routeRoot.Count==0)||Answer.RouteLength!=routeRoot.Count||
                    !ReferenceEquals(Answer.Corridor,corridorRoot?.Corridor)||
                    corridorRoot!=null&&!ReferenceEquals(corridorRoot.RequestOwner,requestRoot)))||
                StorageBytes!=NavStorageLayout.Charge(Charge(routeRoot?.Count??0)))
                throw new ArgumentException("Physical answer owner contradiction.");
            writer.Output.Write((int)Answer.Status);
            NavPhysicalMacro.WriteEdge(writer,this,requestRoot);
            NavPhysicalMacro.WriteEdge(writer,this,routeRoot,true);
            NavPhysicalMacro.WriteEdge(writer,this,corridorRoot,true);
        }
        internal static NavigationHostAnswer ReadPhysicalBody(NavPhysicalBodyReader reader,bool legacyProbe=false)
        {
            var status=(NavSolveStatus)reader.Input.ReadInt32();
            var request=NavPhysicalMacro.ReadEdge<NavigationHostRequest>(reader);
            var route=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavPoint>>(reader,true);
            var corridor=NavPhysicalMacro.ReadEdge<NavigationHostCorridor>(reader,true);
            if(!Enum.IsDefined(typeof(NavSolveStatus),status)||status==NavSolveStatus.Pending||
                legacyProbe&&(status!=NavSolveStatus.Ready||route==null||corridor!=null||request.Request.Entity>=0||request.Request.CertifiedProviderBinding!=null)||
                !legacyProbe&&((status==NavSolveStatus.Ready)!=(route!=null&&corridor!=null)||route!=null&&(route.Count==0||!ReferenceEquals(corridor.RequestOwner,request))))
                throw new ArgumentException("Invalid physical answer status or roots.");
            if(route!=null&&!legacyProbe){
                int at=0;
                foreach(var leg in corridor.TypedRoute.Legs)
                    if(leg.Kind==NavTypedLegKind.Surface){
                        if(at==route.Count||!route[at++].Equals(leg.To.Position))
                            throw new ArgumentException("Physical Ready route points changed.");
                    }
                if(at!=route.Count)throw new ArgumentException("Physical Ready route point count changed.");
            }
            var owner=Allocate(route?.Count??0,reader.Reservation,false);
            owner.requestRoot=NavPhysicalMacro.Acquire(owner,request);
            owner.routeRoot=NavPhysicalMacro.Acquire(owner,route);
            owner.corridorRoot=NavPhysicalMacro.Acquire(owner,corridor);
            owner.Answer=new NavigationAnswer(request.Request,route?.ReadOnlyBacking??Array.Empty<NavPoint>(),
                status,corridor?.Corridor);owner.Answer.BindStorageOwner(owner);
            return owner;
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){requestRoot?.Release();routeRoot?.Release();corridorRoot?.Release();}
    }
}
