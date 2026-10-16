using System;

namespace Spacewars.Simulation
{
    internal enum NavNameRole { None=0,Text,SurfaceId,PortalId,PortalEntrySurface,PortalExitSurface,SupportId,SupportSurface,SeamId,SeamFrom,SeamTo,ProviderBinding,ConnectorPrivateSlot,TransitionId,TransitionFrom,TransitionTo,RegionRequestedSurface,RegionCandidateSurface,SubscriptionOriginSurface,SubscriptionEndpointSurface,RecipeGraphKey,RecipeCompileKey,RecipeArtifactKey,SubscriptionStableKey,ArrivalMemberOriginSurface,ArrivalMemberStableKey,ArrivalMemberHoldBinding,ArrivalOccupantSurface,ArrivalOccupantTargetSurface,ArrivalOccupantIdentity,ArrivalOccupantStableKey,ArrivalSourceRequestedSurface,ArrivalSourceCandidateSurface,ArrivalSourceStableKey,ArrivalOccupancyStableKey,ArrivalInputTopologyIdentity,ArrivalInputProfileIdentity,ArrivalInputStableKey,ArrivalProofEndpointSurface,ArrivalProofStableKey }
    // Non-owning selector. Containers acquire the actual direct owner edge.
    // Owner declarations write their own inline names without a self-edge.
    internal readonly struct NavNameReference
    {
        internal readonly NavOwnedStorage Owner;internal readonly NavNameRole Role;internal readonly int Index;
        internal NavNameReference(NavOwnedStorage owner,NavNameRole role,int index=-1)
        {Owner=owner;Role=role;Index=index;Resolve();}
        internal string Resolve()
        {
            if(Owner==null&&Role==NavNameRole.None&&Index==0)return null;
            if(Owner==null||Owner.References<=0||Role==NavNameRole.None||Role!=NavNameRole.ConnectorPrivateSlot&&Role!=NavNameRole.RegionCandidateSurface&&Role!=NavNameRole.ArrivalSourceCandidateSurface&&Index!=-1)throw new ArgumentException("Invalid name owner/role/index.");
            if(Owner is NavOwnedText text&&Role==NavNameRole.Text)return text.Value;
            if(Owner is NavConnectorResult connector&&Role==NavNameRole.ConnectorPrivateSlot)return connector.PrivateNameSlot(Index);
            if(Owner is NavDeclarationStorage storage){
                var value=storage.Facade;if(!(value is INavStorageFacade facade)||!ReferenceEquals(facade.StorageRoot,storage))throw new ArgumentException("Name declaration facade identity contradiction.");
                if(value is NavArrivalReachabilityProof proof){if(Role==NavNameRole.ArrivalProofEndpointSurface)return proof.Endpoint.SurfaceId;if(Role==NavNameRole.ArrivalProofStableKey)return proof.StableKey;}
                if(value is NavArrivalInput input){switch(Role){case NavNameRole.ArrivalInputTopologyIdentity:return input.TopologyIdentity;case NavNameRole.ArrivalInputProfileIdentity:return input.ProfileIdentity;case NavNameRole.ArrivalInputStableKey:return input.StableKey;}}
                if(value is NavArrivalOccupancySnapshot occupancy&&Role==NavNameRole.ArrivalOccupancyStableKey)return occupancy.StableKey;
                if(value is NavArrivalCandidateSource source){if(Role==NavNameRole.ArrivalSourceRequestedSurface)return source.Requested.SurfaceId;if(Role==NavNameRole.ArrivalSourceCandidateSurface&&Index>=0&&Index<source.Candidates.Count)return source.Candidates[Index].SurfaceId;if(Role==NavNameRole.ArrivalSourceStableKey)return source.StableKey;}
                if(value is NavArrivalOccupant occupant){switch(Role){case NavNameRole.ArrivalOccupantSurface:return occupant.Location.SurfaceId;case NavNameRole.ArrivalOccupantTargetSurface:return occupant.ActorTarget?.SurfaceId;case NavNameRole.ArrivalOccupantIdentity:return occupant.Identity;case NavNameRole.ArrivalOccupantStableKey:return occupant.StableKey;}}
                if(value is NavArrivalMember member){switch(Role){case NavNameRole.ArrivalMemberOriginSurface:return member.Origin.SurfaceId;case NavNameRole.ArrivalMemberStableKey:return member.StableKey;case NavNameRole.ArrivalMemberHoldBinding:return member.HoldBinding;}}
                if(value is NavScheduledRequest recipe){switch(Role){case NavNameRole.RecipeGraphKey:return recipe.GraphKey;case NavNameRole.RecipeCompileKey:return recipe.CompileKey;case NavNameRole.RecipeArtifactKey:return recipe.ArtifactKey;case NavNameRole.SubscriptionStableKey:return recipe.Subscription.StableKey;}}
                if(value is NavRouteSubscription subscription){if(Role==NavNameRole.SubscriptionStableKey)return subscription.StableKey;if(Role==NavNameRole.SubscriptionOriginSurface)return subscription.Origin.SurfaceId;if(Role==NavNameRole.SubscriptionEndpointSurface)return subscription.AssignedEndpoint.SurfaceId;}
                if(value is NavTerminalRegion region){if(Role==NavNameRole.RegionRequestedSurface)return region.Requested.SurfaceId;if(Role==NavNameRole.RegionCandidateSurface&&Index>=0&&Index<region.Candidates.Count)return region.Candidates[Index].SurfaceId;}
                if(value is NavLayerSurface surface&&Role==NavNameRole.SurfaceId)return surface.Id;
                if(value is NavLayerPortal portal){switch(Role){case NavNameRole.PortalId:return portal.Id;case NavNameRole.PortalEntrySurface:return portal.Entry.SurfaceId;case NavNameRole.PortalExitSurface:return portal.Exit.SurfaceId;}}
                if(value is MapSupport support){switch(Role){case NavNameRole.SupportId:return support.Id;case NavNameRole.SupportSurface:return support.SurfaceId;}}
                if(value is NavPartitionSeam seam){switch(Role){case NavNameRole.SeamId:return seam.Id;case NavNameRole.SeamFrom:return seam.FromSurface;case NavNameRole.SeamTo:return seam.ToSurface;}}
                if(value is NavSurfaceTransition transition){switch(Role){case NavNameRole.TransitionId:return transition.Id;case NavNameRole.TransitionFrom:return transition.From;case NavNameRole.TransitionTo:return transition.To;}}
                if(value is ILayeredNavigationProvider provider&&Role==NavNameRole.ProviderBinding)return provider.Binding;
            }
            throw new ArgumentException("Wrong name owner kind/field role.");
        }
        internal void Validate(string name,NavOwnedStorage parent)
        {if(!ReferenceEquals(name,Resolve()))throw new ArgumentException("Physical name field identity contradiction.");if(Owner!=null)parent.RequireEdge(Owner);}
        internal void Require(INavStorageReservation reservation,string name)
        {if(Owner!=null)NavStorageAdmission.RequireOwned(reservation,Owner);if(!ReferenceEquals(name,Resolve()))throw new ArgumentException("Physical name field identity contradiction.");}
        internal void Write(NavPhysicalBodyWriter writer)
        {Resolve();writer.Edge(Owner);writer.Output.Write((int)Role);writer.Output.Write(Index);}
        internal static NavNameReference Read(NavPhysicalBodyReader reader)
        {
            var owner=reader.Edge();var role=(NavNameRole)reader.Input.ReadInt32();int index=reader.Input.ReadInt32();
            if(owner==null&&role==NavNameRole.None&&index==0)return default;return new NavNameReference(owner,role,index);
        }
    }
    internal readonly struct NavNameKey : IEquatable<NavNameKey>,IComparable<NavNameKey>
    {
        internal readonly string Value;internal readonly NavNameReference Reference;
        internal NavNameKey(string value){Value=value;Reference=default;}
        internal NavNameKey(NavNameReference reference){Reference=reference;Value=reference.Resolve();}
        internal static NavNameKey Surface(NavLayerSurface surface)=>surface.StorageRoot==null?new NavNameKey(surface.Id):new NavNameKey(new NavNameReference(surface.StorageRoot,NavNameRole.SurfaceId));
        internal static NavNameKey Artifact(NavScheduledRequest recipe)=>Recipe(recipe,NavNameRole.RecipeArtifactKey,recipe.ArtifactKey);
        internal static NavNameKey Compile(NavScheduledRequest recipe)=>Recipe(recipe,NavNameRole.RecipeCompileKey,recipe.CompileKey);
        private static NavNameKey Recipe(NavScheduledRequest recipe,NavNameRole role,string value)=>recipe.StorageRoot==null?new NavNameKey(value):new NavNameKey(new NavNameReference(recipe.StorageRoot,role));
        internal static NavNameKey Subscription(NavRouteSubscription value)=>value.MetadataOwner==null?new NavNameKey(value.StableKey):new NavNameKey(new NavNameReference(value.MetadataOwner,NavNameRole.SubscriptionStableKey));
        // Transient textual lookups preserve ordinal semantics. Insertions in
        // an owned container still require the exact qualified owner payload.
        public static implicit operator NavNameKey(string value)=>new NavNameKey(value);
        public static implicit operator string(NavNameKey value)=>value.Value;
        public int CompareTo(NavNameKey other)=>StringComparer.Ordinal.Compare(Value,other.Value);
        public bool Equals(NavNameKey other)=>StringComparer.Ordinal.Equals(Value,other.Value);
        public override bool Equals(object other)=>other is NavNameKey key&&Equals(key);
        public override int GetHashCode()=>Value==null?0:StringComparer.Ordinal.GetHashCode(Value);
    }
    internal static class NavPhysicalNamedValue
    {
        internal static void WriteLocation(NavPhysicalBodyWriter writer,NavLocation location)
        {if(!ReferenceEquals(location.SurfaceId,location.NameReference.Resolve()))throw new ArgumentException("Unowned physical location name.");NavContract.Point(writer.Output,location.Position);location.NameReference.Write(writer);}
        internal static NavLocation ReadLocation(NavPhysicalBodyReader reader)
        {var point=NavPhysicalGeometryValue.Point(reader);var name=NavNameReference.Read(reader);if(name.Owner==null){if(!point.Equals(default(NavPoint)))throw new ArgumentException("Invalid default physical location.");return default;}return new NavLocation(point,name);}
        internal static void WriteGrid(NavPhysicalBodyWriter writer,NavGridCoordinate grid)
        {if(!ReferenceEquals(grid.Item1,grid.NameReference.Resolve()))throw new ArgumentException("Unowned physical grid name.");writer.Output.Write(grid.Item2);writer.Output.Write(grid.Item3);grid.NameReference.Write(writer);}
        internal static NavGridCoordinate ReadGrid(NavPhysicalBodyReader reader)
        {long x=reader.Input.ReadInt64(),z=reader.Input.ReadInt64();var name=NavNameReference.Read(reader);if(name.Owner==null){if(x!=0||z!=0)throw new ArgumentException("Invalid default physical grid key.");return default;}return new NavGridCoordinate(new NavLocation(new NavPoint(0,0),name),x,z);}
    }
    internal static class NavNamedPayload
    {
        internal static void Validate(NavLocation location,NavOwnedStorage parent)
        {if(location.SurfaceId==null&&!location.Position.Equals(default(NavPoint)))throw new ArgumentException("Invalid default physical location.");location.NameReference.Validate(location.SurfaceId,parent);}
        internal static void Validate(NavGraphNode node,NavOwnedStorage parent)
        {if(node.Id<0||node.Id>=300000||node.Component<0||node.Component>=300000)throw new ArgumentException("Invalid physical graph node index.");Validate(node.Location,parent);}
        internal static void Validate(NavGridCoordinate grid,NavOwnedStorage parent)
        {if(grid.Item1==null&&(grid.Item2!=0||grid.Item3!=0))throw new ArgumentException("Invalid default physical grid key.");grid.NameReference.Validate(grid.Item1,parent);}
        internal static void Validate(NavGraphArc arc,NavOwnedStorage parent)
        {
            if(arc.Trace!=null)parent.RequireEdge(arc.Trace as NavOwnedStorage??(arc.Trace as INavStorageFacade)?.StorageRoot);
            if(arc.NameReference.Owner!=null){arc.NameReference.Validate(arc.PortalId,parent);if(arc.Kind!=NavGraphArcKind.Portal||arc.Trace!=null)throw new ArgumentException("Contradictory arc name ownership.");return;}
            arc.NameReference.Validate(null,parent);
            if(arc.PortalId==null){if(arc.Kind!=NavGraphArcKind.Surface)throw new ArgumentException("Missing portal arc name.");return;}
            if(arc.Kind!=NavGraphArcKind.Portal)throw new ArgumentException("Surface arc has portal name.");
            if(arc.Trace!=null)foreach(var leg in arc.Trace)if(leg!=null&&leg.Kind==NavTypedLegKind.Portal&&ReferenceEquals(leg.PortalId,arc.PortalId))return;
            throw new ArgumentException("Arc needs exact owned trace or direct name owner.");
        }
        internal static void RequireActive<T>(T value)
        {
            if((typeof(T)==typeof(System.Collections.Generic.IReadOnlyList<NavGraphArc>)||typeof(T)==typeof(NavPagedList<NavGraphArc>))&&value==null)throw new ArgumentException("Active adjacency payload requires its list root.");
            if(typeof(T)==typeof(NavNameKey)&&((NavNameKey)(object)value).Value==null||typeof(T)==typeof(NavLocation)&&((NavLocation)(object)value).SurfaceId==null||typeof(T)==typeof(NavGridCoordinate)&&((NavGridCoordinate)(object)value).Item1==null||typeof(T)==typeof(NavGraphNode)&&((NavGraphNode)(object)value).Location.SurfaceId==null)throw new ArgumentException("Active named payload requires a location/name.");
        }
    }
    public sealed partial class NavLayerSurface
    {
        internal NavLocation OwnedLocation(NavPoint point)=>StorageRoot==null?new NavLocation(point,Id):new NavLocation(point,new NavNameReference(StorageRoot,NavNameRole.SurfaceId));
    }
    public sealed partial class MapSupport
    {
        internal NavLocation OwnedLocation(NavPoint point)=>StorageRoot==null?new NavLocation(point,SurfaceId):new NavLocation(point,new NavNameReference(StorageRoot,NavNameRole.SupportSurface));
    }
    public sealed partial class NavLayerPortal
    {
        internal void BindOwnedLocations()
        {if(StorageRoot!=null){Entry=new NavLocation(Entry.Position,new NavNameReference(StorageRoot,NavNameRole.PortalEntrySurface));Exit=new NavLocation(Exit.Position,new NavNameReference(StorageRoot,NavNameRole.PortalExitSurface));}}
    }
    public sealed partial class NavConnectorResult
    {
        internal string PrivateNameSlot(int index)
        {if(ownedNames==null||index<0||index>=6||ownedNames[index].Role!=NavConnectorNameRole.Private&&ownedNames[index].Role!=NavConnectorNameRole.PrivateAlias)throw new ArgumentException("Not a private connector name slot.");return Name(index);}
    }
}
