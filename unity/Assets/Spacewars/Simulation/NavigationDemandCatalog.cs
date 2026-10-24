using System;
namespace Spacewars.Simulation
{
    // Full immutable Partition samples only. No adjacency/components, no origin
    // or endpoint search state, and no claim to be a current rebind domain.
    internal sealed class NavDemandQueryCatalog : NavOwnedStorage,INavPhysicalBody
    {
        internal readonly NavScheduledRequest Recipe;
        internal readonly NavPagedList<NavLocation> Locations;
        internal readonly NavPackedSlots Grid;
        internal readonly int Width,Min;internal readonly NavPackedSlots Heads,Tails,Next;
        internal PartitionNavigationProvider Provider=>(PartitionNavigationProvider)Recipe.Provider;
        internal NavDemandQueryCatalog(NavScheduledRequest recipe,NavPagedList<NavLocation> locations,NavPackedSlots grid,int min,int width,Func<long,bool> reserve=null,bool birthOnly=false,NavPackedSlots heads=null,NavPackedSlots tails=null,NavPackedSlots next=null):base(256)
        {
            if(reserve?.Target is INavStorageReservation r){NavStorageAdmission.RequireOwned(r,recipe.StorageRoot);NavStorageAdmission.RequireOwned(r,locations);NavStorageAdmission.RequireOwned(r,grid);if(recipe.DemandQueryV3){NavStorageAdmission.RequireOwned(r,heads);NavStorageAdmission.RequireOwned(r,tails);NavStorageAdmission.RequireOwned(r,next);}}
            if(!recipe.DemandQueryV2||!locations.IsImmutableCaptureRoot||!grid.IsImmutableCaptureRoot)throw new ArgumentException("Catalog requires complete immutable owned samples.");
            if(recipe.DemandQueryV3&&(heads==null||tails==null||next==null||!heads.IsImmutableCaptureRoot||!tails.IsImmutableCaptureRoot||!next.IsImmutableCaptureRoot))throw new ArgumentException("Catalog requires complete immutable surface index.");
            using(var a=new NavStorageAllocation(reserve)){a.Reserve(256);a.Register(this);if(!birthOnly)Retain();}
            Recipe=recipe;Locations=locations;Grid=grid;Heads=heads;Tails=tails;Next=next;if(heads!=null){if(IsRegistered){RequireEdge(heads);RequireEdge(tails);RequireEdge(next);}heads.Retain();tails.Retain();next.Retain();}Min=min;Width=width;
            if(IsRegistered){RequireEdge(recipe.StorageRoot);RequireEdge(locations);RequireEdge(grid);}recipe.StorageRoot?.Retain();locations.Retain();grid.Retain();
        }
        internal bool Compatible(NavScheduledRequest r)=>!r.LazyPartition&&!Recipe.LazyPartition&&r.DemandQueryV6==Recipe.DemandQueryV6&&r.MetricContract==Recipe.MetricContract&&r.DemandQueryV5==Recipe.DemandQueryV5&&r.DemandQueryV4==Recipe.DemandQueryV4&&r.DemandQueryV2&&r.DemandQueryV3==Recipe.DemandQueryV3&&r.Provider.Binding==Recipe.Provider.Binding&&r.Profile.ExactProfileBinding==Recipe.Profile.ExactProfileBinding&&r.Profile.GridCell==Recipe.Profile.GridCell&&r.Profile.CostId==Recipe.Profile.CostId&&r.Profile.CostRevision==Recipe.Profile.CostRevision&&r.Clearance.Id==Recipe.Clearance.Id&&r.Clearance.Revision==Recipe.Clearance.Revision&&r.Clearance.MobilityClass==Recipe.Clearance.MobilityClass&&r.Clearance.Radius==Recipe.Clearance.Radius&&r.Limits.MaxSamples==Recipe.Limits.MaxSamples&&r.Limits.MaxNodes==Recipe.Limits.MaxNodes&&r.Limits.MaxArcs==Recipe.Limits.MaxArcs;
        // Partition.TryLocate/IsValid/trace consume radius, not MobilityRevision.
        // The current recipe retains its exact independent clearance metadata.
        internal long EstimatedRetainedBytes=>StorageBytes+Locations.CommittedStorageBytes(64)+Grid.EstimatedRetainedBytes+(Heads?.EstimatedRetainedBytes??0)+(Tails?.EstimatedRetainedBytes??0)+(Next?.EstimatedRetainedBytes??0);
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&Locations.IsImmutableCaptureRoot&&Grid.IsImmutableCaptureRoot;
        protected override void ReleaseEdges(){Recipe.StorageRoot?.Release();Locations.Release();Grid.Release();Heads?.Release();Tails?.Release();Next?.Release();}
        NavPhysicalTag INavPhysicalBody.PhysicalTag=>new NavPhysicalTag(Recipe.DemandQueryV3?NavPhysicalStorageKind.DemandCatalogV3:NavPhysicalStorageKind.DemandCatalogV2,value:Recipe.DemandQueryV6?NavPhysicalValueKind.ExactAstarPolicyV6:Recipe.DemandQueryV5?NavPhysicalValueKind.ExactCostPolicyV5:Recipe.DemandQueryV4?NavPhysicalValueKind.SeamBoundPolicyV4:NavPhysicalValueKind.None);
        void INavPhysicalBody.WritePhysicalBody(NavPhysicalBodyWriter w)
        {if(NavStorageLayout.ObjectBytesBound(GetType())>256)throw new ArgumentException("Catalog layout mismatch.");w.Output.Write(Min);w.Output.Write(Width);NavPhysicalMacro.WriteEdge(w,this,Recipe.StorageRoot);NavPhysicalMacro.WriteEdge(w,this,Locations);NavPhysicalMacro.WriteEdge(w,this,Grid);if(Recipe.DemandQueryV3){NavPhysicalMacro.WriteEdge(w,this,Heads);NavPhysicalMacro.WriteEdge(w,this,Tails);NavPhysicalMacro.WriteEdge(w,this,Next);}}
        internal sealed class Checkpoint
        {internal Checkpoint CopyLogical()=>(Checkpoint)MemberwiseClone();internal NavScheduledRequest Recipe;internal NavLocation[] Locations;internal int[][] Grid;internal int Min,Width,LocationCapacity,GridCapacity;internal int[][] Heads,Tails,Next;internal int HeadCapacity,TailCapacity,NextCapacity;}
        internal Checkpoint Save()=>new Checkpoint{Recipe=Recipe,Locations=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(Locations,x=>x.Detached())),Grid=Grid.Save(),Min=Min,Width=Width,LocationCapacity=Locations.DirectoryCapacity,GridCapacity=Grid.DirectoryCapacity,Heads=Heads?.Save(),Tails=Tails?.Save(),Next=Next?.Save(),HeadCapacity=Heads?.DirectoryCapacity??0,TailCapacity=Tails?.DirectoryCapacity??0,NextCapacity=Next?.DirectoryCapacity??0};
        internal static NavDemandQueryCatalog Restore(Checkpoint state)
        {
            if(state==null||state.Recipe==null||!state.Recipe.DemandQueryV2)throw new ArgumentException("Invalid catalog logical checkpoint.");
            var locations=NavPagedList<NavLocation>.Create();NavPackedSlots grid=null,h=null,t=null,n=null;
            try{foreach(var at in state.Locations)locations.Add(((PartitionNavigationProvider)state.Recipe.Provider).OwnedCheckpointLocation(at));locations.RestoreDirectoryCapacity(state.LocationCapacity,state.Recipe.Limits.MaxNodes,locations.Count);locations.Seal();grid=NavPackedSlots.Restore(state.Grid,checked(state.Width*state.Width),state.GridCapacity);grid.Seal();if(state.Recipe.DemandQueryV3){int count=((PartitionNavigationProvider)state.Recipe.Provider).DemandSurfaceCount;h=NavPackedSlots.Restore(state.Heads,count,state.HeadCapacity);t=NavPackedSlots.Restore(state.Tails,count,state.TailCapacity);n=NavPackedSlots.Restore(state.Next,locations.Count,state.NextCapacity);h.Seal();t.Seal();n.Seal();NavDemandQueryWork.ValidateSurfaceIndex((PartitionNavigationProvider)state.Recipe.Provider,locations,locations.Count,h,t,n);}return new NavDemandQueryCatalog(state.Recipe,locations,grid,state.Min,state.Width,heads:h,tails:t,next:n);}finally{locations.ReleaseOwner();grid?.ReleaseOwner();h?.ReleaseOwner();t?.ReleaseOwner();n?.ReleaseOwner();}
        }
        internal static NavDemandQueryCatalog ReadPhysicalBody(NavPhysicalBodyReader r,bool v3=false,bool v4=false,bool v5=false,bool v6=false)
        {
            int min=r.Input.ReadInt32(),width=r.Input.ReadInt32();var recipe=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(r);var locations=NavPhysicalMacro.ReadEdge<NavPagedList<NavLocation>>(r);var grid=NavPhysicalMacro.ReadEdge<NavPackedSlots>(r);
            if(!(recipe.Facade is NavScheduledRequest request)||!request.DemandQueryV2||request.DemandQueryV3!=v3||request.DemandQueryV4!=v4||request.DemandQueryV5!=v5||request.DemandQueryV6!=v6||!NavDemandQueryWork.Eligible(request))throw new ArgumentException("Catalog recipe policy/capacity mismatch.");
            double b=((PartitionNavigationProvider)request.Provider).HalfExtent/request.Profile.GridCell;
            if(min!=(int)Math.Ceiling(-b)||width!=(int)Math.Floor(b)-min+1||locations.Count>width*width||grid.CapacitySlots<width*width||grid.CapacitySlots>width*width+NavPackedSlots.PageSize)throw new ArgumentException("Catalog dimensions mismatch.");
            int next=0;for(int i=0;i<width*width;i++){int n=grid.Get(i)-1;if(n<0)continue;if(n!=next++||n>=locations.Count||locations[n].Position.X!=(min+i/width)*request.Profile.GridCell||locations[n].Position.Z!=(min+i%width)*request.Profile.GridCell||!NavTypedLeg.SamePhysicalLocation(locations[n],((PartitionNavigationProvider)request.Provider).OwnedCheckpointLocation(locations[n])))throw new ArgumentException("Catalog prefix/order/provider mismatch.");}
            if(next!=locations.Count)throw new ArgumentException("Catalog missing nodes.");for(int i=width*width;i<grid.CapacitySlots;i++)if(grid.Get(i)!=0)throw new ArgumentException("Catalog future grid payload.");
            var h=v3?NavPhysicalMacro.ReadEdge<NavPackedSlots>(r):null;var t=v3?NavPhysicalMacro.ReadEdge<NavPackedSlots>(r):null;var nextIndex=v3?NavPhysicalMacro.ReadEdge<NavPackedSlots>(r):null;if(v3){using(NavStorageScratch.Reserve(r.Reservation,4096L+locations.Count))NavDemandQueryWork.ValidateSurfaceIndex((PartitionNavigationProvider)request.Provider,locations,locations.Count,h,t,nextIndex);}
            return new NavDemandQueryCatalog(request,locations,grid,min,width,r.Reservation.TryReserve,true,h,t,nextIndex);
        }
    }
}
