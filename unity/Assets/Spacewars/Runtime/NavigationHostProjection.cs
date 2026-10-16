using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // A committed projection step owns its exact cursor and paged output.
    // Forks copy only the page touched by the next leg or replay trace.
    internal sealed class NavigationHostProjection : NavOwnedStorage, INavPhysicalBody
    {
        private static readonly long LayoutBytes=Math.Max(256,
            NavStorageLayout.ObjectBytesBound(typeof(NavigationHostProjection)));
        private NavPagedList<NavPoint> points;
        private NavPagedList<NavNameReference> typed,observed;
        internal int LegCursor {get;private set;}
        internal int ReplayCursor {get;private set;}
        internal NavLocation Expected {get;private set;}
        internal NavLocation At {get;private set;}
        internal int PointCount=>points.Count;
        internal int TypedCount=>typed.Count;
        internal int ObservedCount=>observed.Count;
        internal NavPoint Point(int index)=>points[index];
        internal string TypedPortal(int index)=>typed[index].Resolve();
        internal string ObservedPortal(int index)=>observed[index].Resolve();
        private bool sealedProjection,released;
        internal override bool IsImmutableCaptureRoot=>sealedProjection;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.HostProjection);

        private NavigationHostProjection(bool ownerReference):base(LayoutBytes)
        {if(ownerReference)Retain();}
        private static NavigationHostProjection Allocate(INavStorageReservation reservation,bool ownerReference)
        {
            using(var allocation=new NavStorageAllocationContext(reservation.TryReserve)){
                allocation.Reserve(LayoutBytes);var value=new NavigationHostProjection(ownerReference);allocation.Register(value);return value;
            }
        }

        private void OwnLocations(NavLocation expected,NavLocation at)
        {
            expected.NameReference.Validate(expected.SurfaceId,this);
            at.NameReference.Validate(at.SurfaceId,this);
            expected.NameReference.Owner?.Retain();at.NameReference.Owner?.Retain();
            Expected=expected;At=at;
        }
        private void ChangeLocation(bool expected,NavLocation location)
        {
            location.NameReference.Validate(location.SurfaceId,this);
            location.NameReference.Owner?.Retain();
            if(expected){Expected.NameReference.Owner?.Release();Expected=location;}
            else{At.NameReference.Owner?.Release();At=location;}
        }
        internal static NavigationHostProjection CreateOwned(NavLocation origin,
            INavStorageReservation reservation)
        {
            if(reservation==null)throw new ArgumentNullException(nameof(reservation));
            origin.RequireName(reservation);
            var value=Allocate(reservation,true);
            try{
                value.points=NavPhysicalMacro.Acquire(value,NavPagedList<NavPoint>.CreateOwned(reservation,4));value.points.Release();
                value.typed=NavPhysicalMacro.Acquire(value,NavPagedList<NavNameReference>.CreateOwned(reservation,4));value.typed.Release();
                value.observed=NavPhysicalMacro.Acquire(value,NavPagedList<NavNameReference>.CreateOwned(reservation,4));value.observed.Release();
                value.OwnLocations(origin,origin);return value;
            }catch{value.ReleaseStorage();throw;}
        }
        internal NavigationHostProjection ForkOwned(INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,this);
            if(!sealedProjection)throw new InvalidOperationException("Mutable physical projection cannot fork.");
            var value=Allocate(reservation,true);
            try{
                value.points=NavPhysicalMacro.Acquire(value,points.ForkOwned(reservation));value.points.Release();
                value.typed=NavPhysicalMacro.Acquire(value,typed.ForkOwned(reservation));value.typed.Release();
                value.observed=NavPhysicalMacro.Acquire(value,observed.ForkOwned(reservation));value.observed.Release();
                value.OwnLocations(Expected,At);
                value.LegCursor=LegCursor;value.ReplayCursor=ReplayCursor;return value;
            }catch{value.ReleaseStorage();throw;}
        }
        internal void AdvanceLeg(NavTypedLeg leg,NavNameReference portal)
        {
            if(sealedProjection||leg==null||!leg.From.Equals(Expected))
                throw new ArgumentException("Invalid physical projection leg step.");
            if(leg.Kind==NavTypedLegKind.Surface){
                if(portal.Owner!=null)throw new ArgumentException("Surface leg has a portal owner.");
                points.Add(leg.To.Position);
            }else{
                if(leg.Kind!=NavTypedLegKind.Portal||portal.Owner==null||
                    !ReferenceEquals(portal.Resolve(),leg.PortalId))
                    throw new ArgumentException("Portal leg lost its canonical name.");
                typed.Add(portal);
            }
            ChangeLocation(true,leg.To);LegCursor++;
        }
        internal void AdvanceReplay(NavLocation reached,NavNameReference[] portals)
        {
            if(sealedProjection||portals==null||ReplayCursor>=points.Count)
                throw new ArgumentException("Invalid physical replay step.");
            foreach(var portal in portals){
                if(portal.Owner==null)throw new ArgumentException("Replay portal has no owner.");
                observed.Add(portal);
            }
            ChangeLocation(false,reached);ReplayCursor++;
        }
        internal NavPoint[] CopyPoints()=>points.ToArray();
        internal bool PortalsMatch()
        {
            if(typed.Count!=observed.Count)return false;
            // Typed selectors belong to the cached route provider; replay selectors
            // belong to the request provider. Each exact owner is independently
            // validated below. Equal provider bindings give their seam IDs the
            // same meaning, without rebinding either retained selector.
            for(int i=0;i<typed.Count;i++)if(!string.Equals(typed[i].Resolve(),observed[i].Resolve(),StringComparison.Ordinal))return false;
            return true;
        }
        internal void ValidateAgainst(NavigationHostRow row)
        {
            var route=row.Solved?.Route;
            if(route==null||LegCursor<0||LegCursor>route.Legs.Count||
                ReplayCursor<0||ReplayCursor>points.Count||
                points.Count>route.Legs.Count||typed.Count>route.Legs.Count||
                observed.Count>(row.Provider is PartitionNavigationProvider partition?
                    checked((long)partition.TracePortalBound*ReplayCursor):0)||
                !NavTypedLeg.SamePhysicalLocation(Expected,
                    LegCursor==0?row.Scheduled.Subscription.Origin:route.Legs[LegCursor-1].To))
                throw new ArgumentException("Physical projection cursor or endpoint changed.");
            int surfaces=0,portals=0;
            for(int i=0;i<LegCursor;i++){
                var leg=route.Legs[i];
                if(leg.Kind==NavTypedLegKind.Surface){
                    if(surfaces>=points.Count||!points[surfaces++].Equals(leg.To.Position))
                        throw new ArgumentException("Physical projection point prefix changed.");
                }else{
                    if(portals>=typed.Count||!ReferenceEquals(typed[portals++].Resolve(),leg.PortalId))
                        throw new ArgumentException("Physical projection portal prefix changed.");
                    var canonical=leg.CanonicalPortalName(row.Provider);
                    var saved=typed[portals-1];
                    if(!ReferenceEquals(canonical.Owner,saved.Owner)||canonical.Role!=saved.Role||canonical.Index!=saved.Index)
                        throw new ArgumentException("Physical projection portal owner changed.");
                }
            }
            if(surfaces!=points.Count||portals!=typed.Count)
                throw new ArgumentException("Physical projection prefix count changed.");
            if(!(row.Provider is PartitionNavigationProvider)&&
                (ReplayCursor!=0||observed.Count!=0||
                 !NavTypedLeg.SamePhysicalLocation(At,row.Scheduled.Subscription.Origin)))
                throw new ArgumentException("Flat projection has replay state.");
        }
        private static bool CanonicalSeamOwner(PartitionNavigationProvider provider,
            NavNameReference reference)
            =>reference.Role==NavNameRole.SeamId&&
              provider.Seams.Any(seam=>ReferenceEquals(seam.StorageRoot,reference.Owner)&&
                  ReferenceEquals(seam.Id,reference.Resolve()));
        internal void ValidateReplay(NavigationHostRow row,INavStorageReservation reservation)
        {
            ValidateAgainst(row);
            if(!(row.Provider is PartitionNavigationProvider partition))return;
            using(NavStorageScratch.Reserve(reservation,checked(512L+
                partition.TraceScratchBound+8L*partition.TracePortalBound))){
                var at=row.Scheduled.Subscription.Origin;
                int seen=0;
                for(int i=0;i<ReplayCursor;i++){
                    if(!partition.TryTrace(at,points[i],row.Scheduled.Clearance,
                        out var reached,out var trace))
                        throw new ArgumentException("Physical replay prefix no longer traces.");
                    foreach(var leg in trace.Where(value=>value.Kind==NavTypedLegKind.Portal)){
                        if(seen>=observed.Count||!CanonicalSeamOwner(partition,observed[seen])||
                            !ReferenceEquals(observed[seen++].Resolve(),leg.PortalId))
                            throw new ArgumentException("Physical replay portal prefix changed.");
                    }
                    at=reached;
                }
                if(seen!=observed.Count||!NavTypedLeg.SamePhysicalLocation(at,At))
                    throw new ArgumentException("Physical replay cursor location changed.");
            }
        }
        internal void SealPrepared()
        {
            if(sealedProjection)throw new InvalidOperationException("Physical projection is already sealed.");
            points.Seal();typed.Seal();observed.Seal();sealedProjection=true;
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!sealedProjection||StorageBytes!=NavStorageLayout.Charge(LayoutBytes))
                throw new ArgumentException("Mutable or undercharged physical projection BODY.");
            writer.Output.Write(LegCursor);writer.Output.Write(ReplayCursor);
            NavPhysicalNamedValue.WriteLocation(writer,Expected);
            NavPhysicalNamedValue.WriteLocation(writer,At);
            NavPhysicalMacro.WriteEdge(writer,this,points);
            NavPhysicalMacro.WriteEdge(writer,this,typed);
            NavPhysicalMacro.WriteEdge(writer,this,observed);
        }
        internal static NavigationHostProjection ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            int leg=reader.Input.ReadInt32(),replay=reader.Input.ReadInt32();
            var expected=NavPhysicalNamedValue.ReadLocation(reader);
            var at=NavPhysicalNamedValue.ReadLocation(reader);
            var points=NavPhysicalMacro.ReadEdge<NavPagedList<NavPoint>>(reader);
            var typed=NavPhysicalMacro.ReadEdge<NavPagedList<NavNameReference>>(reader);
            var observed=NavPhysicalMacro.ReadEdge<NavPagedList<NavNameReference>>(reader);
            if(leg<0||replay<0||replay>points.Count||!points.IsFrozen||
                !typed.IsFrozen||!observed.IsFrozen)
                throw new ArgumentException("Invalid physical projection BODY fields.");
            var value=Allocate(reader.Reservation,false);
            value.points=NavPhysicalMacro.Acquire(value,points);
            value.typed=NavPhysicalMacro.Acquire(value,typed);
            value.observed=NavPhysicalMacro.Acquire(value,observed);
            value.OwnLocations(expected,at);
            value.LegCursor=leg;value.ReplayCursor=replay;value.sealedProjection=true;
            return value;
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges()
        {points?.Release();typed?.Release();observed?.Release();Expected.NameReference.Owner?.Release();At.NameReference.Owner?.Release();}
    }
}
