using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // The current ordinary Ready corridor keeps the solver's physical typed
    // route. Its legs already own canonical provider names, including partition
    // seam names. Group trunk/branch metadata needs its own owned extension.
    internal sealed class NavigationHostCorridor : NavOwnedStorage, INavPhysicalBody
    {
        private const long FacadeFloor=1024;
        private NavigationHostRequest requestRoot;
        private NavigationHostProvider providerRoot;
        private NavDeclarationStorage recipeRoot;
        private NavTypedRoute routeRoot;
        internal NavCorridorDescriptor Corridor {get;private set;}
        internal NavigationHostRequest RequestOwner=>requestRoot;
        internal NavigationHostProvider ProviderOwner=>providerRoot;
        internal NavTypedRoute TypedRoute=>routeRoot;
        internal NavScheduledRequest Recipe=>(NavScheduledRequest)recipeRoot.Facade;
        private bool released;
        internal override bool IsImmutableCaptureRoot=>Corridor!=null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.HostCorridor);

        private static long Charge(NavCorridorDescriptor value)
            =>checked(Math.Max(FacadeFloor,NavStorageLayout.ObjectBytesBound(typeof(NavigationHostCorridor)))+
                Math.Max(value.EstimatedRetainedBytes,
                    NavStorageLayout.ObjectBytesBound(typeof(NavCorridorDescriptor))+
                    32L+8L*value.Legs.Count+
                    (long)NavStorageLayout.ObjectBytesBound(typeof(NavCorridorLeg))*value.Legs.Count));
        private NavigationHostCorridor(long bytes,bool ownerReference):base(bytes)
        {if(ownerReference)Retain();}
        private static NavigationHostCorridor Allocate(long bytes,INavStorageReservation reservation,bool ownerReference)
        {
            using(var allocation=new NavStorageAllocationContext(reservation.TryReserve)){
                allocation.Reserve(bytes);var value=new NavigationHostCorridor(bytes,ownerReference);allocation.Register(value);return value;
            }
        }

        private static void ValidateBinding(NavigationHostRequest request,NavigationHostProvider provider,
            NavScheduledRequest recipe,NavTypedRoute route)
        {
            if(request==null||provider==null||recipe==null||route==null||route.Legs.Count==0||
                route.Legs.Count>WorldWire.MaxItems||!ReferenceEquals(recipe.Provider,provider.Provider)||
                !ReferenceEquals(recipe.Subscription,route.Subscription)||
                recipe.Subscription.WorldGeneration!=request.Request.Session||
                recipe.Subscription.RequestSequence!=request.Request.Request||
                recipe.Subscription.EntityId!=request.Request.Entity||
                !recipe.Subscription.Origin.Position.Equals(request.Request.Start)||
                !recipe.Subscription.AssignedEndpoint.Position.Equals(request.Request.Goal)||
                !ReferenceEquals(request.Request.Geometry,provider.Geometry))
                throw new ArgumentException("Ready corridor differs from its registered row.");
            NavTypedLeg previous=null;
            foreach(var leg in route.Legs){
                if(leg==null||previous!=null&&!previous.To.Equals(leg.From))
                    throw new ArgumentException("Discontinuous physical Ready route.");
                previous=leg;
            }
            if(!route.Legs[0].From.Equals(recipe.Subscription.Origin)||
                !previous.To.Equals(recipe.Subscription.AssignedEndpoint))
                throw new ArgumentException("Ready corridor endpoint changed.");
        }
        private static NavCorridorDescriptor Build(int topology,NavigationHostRequest request,
            NavigationHostProvider provider,NavScheduledRequest recipe,NavTypedRoute route)
        {
            ValidateBinding(request,provider,recipe,route);
            var id=recipe.Subscription;
            return new NavCorridorDescriptor(NavCorridorDescriptor.CurrentVersion,recipe.GraphKey,
                provider.Provider.Binding,recipe.Profile.ExactProfileBinding,provider.SurfaceProviderId,
                request.Request.HeldIdentity,topology,recipe.Clearance.Radius,recipe.Profile.GridCell,
                id.Provenance,id.WorldGeneration,id.GroupId,id.RootOrderRevision,id.CommandSequence,
                id.IssuedTick,id.ActivationRevision,id.EntityId,id.Incarnation,id.MobilityRevision,
                id.RequestSequence,request.Request.Order,id.Origin,id.AssignedEndpoint,
                route.Legs.Select(leg=>new NavCorridorLeg(leg.From,leg.To,leg.Kind,leg.PortalId,
                    leg.Cost,NavCorridorSection.IndividualConnector,0,0)));
        }
        private void Own(NavigationHostRequest request,NavigationHostProvider provider,
            NavDeclarationStorage recipe,NavTypedRoute route)
        {
            requestRoot=NavPhysicalMacro.Acquire(this,request);
            providerRoot=NavPhysicalMacro.Acquire(this,provider);
            recipeRoot=NavPhysicalMacro.Acquire(this,recipe);
            routeRoot=NavPhysicalMacro.Acquire(this,route);
        }
        internal static NavigationHostCorridor CreateOwned(NavigationHostRequest request,
            NavigationHostProvider provider,NavScheduledRequest recipe,NavTypedRoute route,
            INavStorageReservation reservation)
        {
            if(reservation==null)throw new ArgumentNullException(nameof(reservation));
            NavStorageAdmission.RequireOwned(reservation,request);
            NavStorageAdmission.RequireOwned(reservation,provider);
            NavStorageAdmission.RequireOwned(reservation,recipe?.StorageRoot);
            NavStorageAdmission.RequireOwned(reservation,route);
            ValidateBinding(request,provider,recipe,route);
            foreach(var leg in route.Legs)leg.RequireCanonicalProviderBinding(provider.Provider,reservation);
            using(NavStorageScratch.Reserve(reservation,checked(8192L+1024L*route.Legs.Count))){
                var value=Build(request.Request.BaseGeometry.Revision,request,provider,recipe,route);
                var owner=Allocate(Charge(value),reservation,true);
                try{owner.Own(request,provider,(NavDeclarationStorage)recipe.StorageRoot,route);
                    owner.Corridor=value;return owner;}
                catch{owner.ReleaseStorage();throw;}
            }
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(Corridor==null||!(recipeRoot.Facade is NavScheduledRequest recipe)||
                StorageBytes!=NavStorageLayout.Charge(Charge(Corridor))||
                Corridor.TopologyRevision!=requestRoot.Request.BaseGeometry.Revision||
                Corridor.GraphKey!=recipe.GraphKey||
                Corridor.ProviderBinding!=providerRoot.Provider.Binding||
                Corridor.ProfileBinding!=recipe.Profile.ExactProfileBinding||
                Corridor.SurfaceProviderId!=providerRoot.SurfaceProviderId||
                Corridor.HoldBinding!=requestRoot.Request.HeldIdentity||
                Corridor.Legs.Count!=routeRoot.Legs.Count)
                throw new ArgumentException("Physical Ready corridor layout contradiction.");
            ValidateBinding(requestRoot,providerRoot,recipe,routeRoot);
            var legs=Corridor.Legs;
            for(int n=0;n<routeRoot.Legs.Count;n++){
                var actual=routeRoot.Legs[n];var facade=legs[n];
                if(!facade.From.Equals(actual.From)||!facade.To.Equals(actual.To)||
                    facade.Kind!=actual.Kind||facade.PortalId!=actual.PortalId||
                    facade.Cost!=actual.Cost||facade.Section!=NavCorridorSection.IndividualConnector||
                    facade.Branch!=0||facade.DownstreamMergeLeg!=0)
                    throw new ArgumentException("Physical Ready corridor leg changed.");
            }
            writer.Output.Write(Corridor.TopologyRevision);
            NavPhysicalMacro.WriteEdge(writer,this,requestRoot);
            NavPhysicalMacro.WriteEdge(writer,this,providerRoot);
            NavPhysicalMacro.WriteEdge(writer,this,recipeRoot);
            NavPhysicalMacro.WriteEdge(writer,this,routeRoot);
        }
        internal static NavigationHostCorridor ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            int topology=reader.Input.ReadInt32();
            var request=NavPhysicalMacro.ReadEdge<NavigationHostRequest>(reader);
            var provider=NavPhysicalMacro.ReadEdge<NavigationHostProvider>(reader);
            var recipeRoot=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);
            var route=NavPhysicalMacro.ReadEdge<NavTypedRoute>(reader);
            if(!(recipeRoot.Facade is NavScheduledRequest recipe)||
                topology!=request.Request.BaseGeometry.Revision)
                throw new ArgumentException("Physical Ready corridor topology/recipe changed.");
            ValidateBinding(request,provider,recipe,route);
            foreach(var leg in route.Legs)leg.RequireCanonicalProviderBinding(provider.Provider,reader.Reservation);
            using(NavStorageScratch.Reserve(reader.Reservation,checked(8192L+1024L*route.Legs.Count))){
                var value=Build(topology,request,provider,recipe,route);
                var owner=Allocate(Charge(value),reader.Reservation,false);
                owner.Own(request,provider,recipeRoot,route);owner.Corridor=value;return owner;
            }
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){requestRoot?.Release();providerRoot?.Release();recipeRoot?.Release();routeRoot?.Release();}
    }
}
