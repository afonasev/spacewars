using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        internal long LastOwnedIngressPublicationAllocatedBytes{get;private set;}=-1;
        internal int OwnedIngressPublications{get;private set;}
        private sealed class PreparedIngress : NavOwnedStorage,IDisposable
        {
            private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(PreparedIngress))+48;
            internal readonly NavOwnedStorage[] MailboxRetired;
            internal NavigationHostRoot SourceHost,Host,RetiredHost;
            internal NavigationAdmissionStorage SourceAdmission,Admission;
            internal NavigationAdmission SourceAdmissionValue,LegacyAdmission;
            internal long Publication;
            internal NavigationCommandSlots<int,NavigationRequest> SourcePending,Pending;
            internal NavigationCommandSlots<int,string> SourceIdentities,Identities;
            internal int IdentityRevision;
            internal NavOwnedStorage RetiredPending,RetiredIdentities;
            internal NavigationLiveRegistrations SourceRegistrations,Registrations;
            internal NavigationHostRequest Request;
            internal NavDeclarationStorage Recipe;
            internal NavigationLiveArrivalTables SourceArrival,Arrival,RetiredArrival;
            internal NavigationLiveArrivalLeaseTable SourceLeases,Leases,RetiredLeases;
            internal NavigationLiveArrivalLease Lease;
            internal NavigationLiveArrivalAttempt Attempt;
            internal long Operation,MailboxRevision,LeaseSequence,AcquisitionSequence,Attachment;
            internal int AllocationThread;internal CertifiedRouteKernel Worker;
            internal int MailboxCount,PendingRevision,RegistrationRevision,RegistrationCount,ArrivalRevision,LeaseRevision,AttemptRevision;
            internal bool Probe,NewTransport,Published;
            private bool released,hostDestination,pendingDestination,identityDestination,registrationDestination,arrivalDestination,leaseDestination,requestDestination,recipeDestination,transportDestination;
            private PreparedIngress():base(Layout){MailboxRetired=new NavOwnedStorage[2];Retain();}
            internal static PreparedIngress Create(INavStorageReservation r){using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);return a.Register(new PreparedIngress());}}
            internal void SealDestinations()
            {
                Host.Retain();hostDestination=true;Pending.Retain();pendingDestination=true;
                if(Identities!=null){Identities.Retain();identityDestination=true;}
                Request.Retain();requestDestination=true;Recipe.Retain();recipeDestination=true;
                if(NewTransport){Request.Retain();transportDestination=true;}
                if(SourceRegistrations==null){Registrations.Retain();registrationDestination=true;}
                if(Arrival!=null){Arrival.Retain();arrivalDestination=true;Leases.Retain();leaseDestination=true;}
            }
            internal void TransferNoThrow(){hostDestination=pendingDestination=identityDestination=registrationDestination=arrivalDestination=leaseDestination=requestDestination=recipeDestination=transportDestination=false;Published=true;}
            public void Dispose(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>Dispose();
            protected override void ReleaseEdges()
            {
                foreach(var value in MailboxRetired)value?.Release();RetiredHost?.Release();RetiredPending?.Release();RetiredIdentities?.Release();RetiredArrival?.Release();RetiredLeases?.Release();
                if(hostDestination)Host.Release();if(pendingDestination)Pending.Release();if(identityDestination)Identities.Release();if(registrationDestination)Registrations.Release();if(arrivalDestination)Arrival.Release();if(leaseDestination)Leases.Release();
                if(requestDestination)Request.Release();if(recipeDestination)Recipe.Release();if(transportDestination)Request.Release();
                SourceHost?.Release();Host?.Release();SourceAdmission?.Release();Admission?.Release();SourcePending?.Release();Pending?.ReleaseStorage();SourceIdentities?.Release();Identities?.ReleaseStorage();
                SourceRegistrations?.Release();if(SourceRegistrations==null)Registrations?.ReleaseStorage();
                SourceArrival?.Release();Arrival?.ReleaseStorage();SourceLeases?.Release();Leases?.ReleaseStorage();Lease?.ReleaseStorage();Attempt?.Release();Request?.Release();Recipe?.Release();
            }
        }
        private bool IngressObservationCompatible(NavigationAdmission observed,NavigationRequest request)
        {
            var current=Admission;
            return observed!=null&&current!=null&&observed.Generation==current.Generation&&ReferenceEquals(observed.Geometry,current.Geometry)&&ReferenceEquals(observed.Profile,current.Profile)&&ReferenceEquals(observed.AuthoredTerrain,current.AuthoredTerrain)&&observed.SurfaceProviderId==current.SurfaceProviderId&&observed.SurfaceSemanticsVersion==current.SurfaceSemanticsVersion&&observed.Contains(request)&&current.Contains(request)&&(request.Entity<0||CurrentMember(request)&&CurrentHeldBinding(request.HeldIdentity,request.Entity));
        }
        private bool IngressSourceCurrent(PreparedIngress c,NavigationAdmission observed,NavigationRequest ingress)
        {
            if(!ReferenceEquals(Admission,c.SourceAdmissionValue)||admissionPublicationIdentity!=c.Publication||!ReferenceEquals(committedHostRoot,c.SourceHost)||!ReferenceEquals(admissionOwner,c.SourceAdmission)||!IngressObservationCompatible(observed,ingress)||authorityOperationIdentity!=c.Operation||commandGroup!=null||admissionBatchDepth!=0||hostAllocationThread!=c.AllocationThread||certifiedHostAttachmentIdentity!=c.Attachment||!ReferenceEquals(certifiedHost,c.Worker)||!Admission.Contains(ingress)||!ReferenceEquals(c.Probe?probes:pending,c.SourcePending)||c.SourcePending.Revision!=c.PendingRevision||!(c.Probe?probes:pending).TryGetValue(ingress.Entity,out var active)||!ReferenceEquals(active,ingress)||!ReferenceEquals(liveRegistrations,c.SourceRegistrations)||RegisteredRequests.Count!=c.RegistrationCount||RegistrationIndex(c.Request.Request)>=0)return false;
            if(c.SourceIdentities!=null&&(!ReferenceEquals(movementIdentities,c.SourceIdentities)||c.SourceIdentities.Revision!=c.IdentityRevision))return false;
            if(c.SourceRegistrations!=null&&c.SourceRegistrations.Revision!=c.RegistrationRevision)return false;
            if(c.Attempt!=null&&(!ReferenceEquals(liveArrivalTables,c.SourceArrival)||c.SourceArrival.Revision!=c.ArrivalRevision||!ReferenceEquals(liveArrivalLeaseTable,c.SourceLeases)||c.SourceLeases!=null&&c.SourceLeases.Revision!=c.LeaseRevision||arrivalLeaseSequence!=c.LeaseSequence||arrivalAcquisitionSequence!=c.AcquisitionSequence||!arrivalAttempts.TryGetValue(c.Attempt.Record.Id,out var attempt)||!ReferenceEquals(attempt.StorageOwner,c.Attempt)||c.Attempt.Revision!=c.AttemptRevision||attempt.Retiring||attempt.Reconstructing))return false;
            return true;
        }
        // Raw neighbours still require the existing transitional admission view.
        // Prepare that view before publication, while leases, provider references,
        // pending and host destinations remain the same controlled owned carrier.
        private NavigationAdmission PrepareIngressLegacyAdmission(PreparedIngress c,NavigationAdmission observed,NavigationRequest ingress,NavigationRetentionBudget.Scope scope)
        {
            long bytes;
            using(EnterTransport()){
                if(!IngressSourceCurrent(c,observed,ingress))throw new PhysicalIngressRefused("Legacy ingress view lost its source.");
                int members=0;foreach(var group in groups.Values)members=checked(members+group.Members.Length);
                bytes=checked(8192L+1024L*groups.Count+512L*members+64L*(pending.Count+probes.Count));
            }
            using(NavStorageScratch.Reserve(scope.OwnedReservation(),bytes))using(EnterTransport()){
                if(!IngressSourceCurrent(c,observed,ingress))throw new PhysicalIngressRefused("Legacy ingress view changed before copy.");
                var nextPending=c.Probe?pending:c.Pending;var nextProbes=c.Probe?c.Pending:probes;
                return new NavigationAdmission(Generation,requestSequence,terrain,terrain?.Id??NavLocation.FlatSurface,
                    terrain?.SurfaceSemanticsVersion??1,navigationGeometry,profile,
                    groups.Values.OrderBy(g=>g.GroupId).Select(g=>new NavigationAdmissionGroup(g,id=>nextPending.TryGetValue(id,out var request)?request:null)).ToArray(),
                    nextPending.Values.Concat(nextProbes.Values).OrderBy(r=>r.Request).ToArray(),checked(c.Publication+1));
            }
        }
        // null selects the explicit legacy/manual lane before any writes.
        private bool? PublishOwnedPhysicalIngress(NavigationRetentionBudget.Scope scope,NavigationHostRoot expected,
            NavigationHostRoot host,NavigationAdmission observed,NavigationRequest ingress,NavNameKey key,long attemptId)
        {
            PreparedIngress c=null;bool published=false;
            if(scope.Prepared!=null)return null;
            using(EnterTransport()){
                var source=ingress.Entity<0?probes:pending;
                if(!(source is NavigationCommandSlots<int,NavigationRequest>))return null;
                if(ingress.Entity>=0&&!(movementIdentities is NavigationCommandSlots<int,string>))return null;
                if(!source.TryGetValue(ingress.Entity,out var active)||!ReferenceEquals(active,ingress))return false;
                if(liveRegistrations==null&&registeredRoutes.Count!=0)return null;
                if(attemptId!=0&&(liveArrivalTables==null||liveArrivalLeaseTable==null&&arrivalProjectionLeases.Count!=0))return null;
            }
            try{
                var r=scope.OwnedReservation();c=PreparedIngress.Create(r);c.Probe=ingress.Entity<0;var row=(NavigationHostRow)host.Rows[key];
                ValidateScheduledRoute(ingress,row.Scheduled,observed);
                c.Host=NavPhysicalMacro.Acquire(c,host);c.Request=NavPhysicalMacro.Acquire(c,row.RequestOwner);c.Recipe=NavPhysicalMacro.Acquire(c,row.RecipeOwner);
                using(EnterTransport()){
                    c.SourceAdmissionValue=Admission;c.Publication=admissionPublicationIdentity;
                    c.SourceHost=NavPhysicalMacro.Acquire(c,expected);c.SourceAdmission=NavPhysicalMacro.Acquire(c,admissionOwner);c.SourcePending=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavigationRequest>)(c.Probe?probes:pending));c.PendingRevision=c.SourcePending.Revision;c.Operation=authorityOperationIdentity;c.AllocationThread=hostAllocationThread;c.Worker=certifiedHost;c.Attachment=certifiedHostAttachmentIdentity;
                    if(!c.Probe){c.SourceIdentities=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,string>)movementIdentities);c.IdentityRevision=c.SourceIdentities.Revision;}
                    c.SourceRegistrations=NavPhysicalMacro.Acquire(c,liveRegistrations);c.Registrations=c.SourceRegistrations;c.RegistrationRevision=c.SourceRegistrations?.Revision??0;c.RegistrationCount=RegisteredRequests.Count;
                    Requests.ObserveState(out c.MailboxRevision,out c.MailboxCount);c.NewTransport=c.Request.TransportOwner==null;
                    if(!IngressSourceCurrent(c,observed,ingress))throw new PhysicalIngressRefused("Physical ingress lost its observation source.");
                    if(attemptId!=0){
                        if(!arrivalAttempts.TryGetValue(attemptId,out var attempt)||attempt.StorageOwner==null)throw new InvalidOperationException("Physical ingress lost its owned attempt.");
                        c.Attempt=NavPhysicalMacro.Acquire(c,attempt.StorageOwner);c.AttemptRevision=c.Attempt.Revision;c.SourceArrival=NavPhysicalMacro.Acquire(c,liveArrivalTables);c.ArrivalRevision=c.SourceArrival.Revision;
                        c.SourceLeases=NavPhysicalMacro.Acquire(c,liveArrivalLeaseTable);c.LeaseRevision=c.SourceLeases?.Revision??0;c.LeaseSequence=arrivalLeaseSequence;c.AcquisitionSequence=arrivalAcquisitionSequence;
                    }
                    if(!IngressSourceCurrent(c,observed,ingress))throw new PhysicalIngressRefused("Physical ingress lost its preparation source.");
                }
                if(c.Registrations==null)c.Registrations=NavigationLiveRegistrations.CreateOwned(r);
                c.Pending=NavigationCommandSlots<int,NavigationRequest>.CreateOwned(c.SourcePending.Capacity,r);
                using(EnterTransport()){if(!IngressSourceCurrent(c,observed,ingress))throw new PhysicalIngressRefused("Physical ingress pending source changed.");c.Pending.CopyPreparedFrom(c.SourcePending);}
                c.Pending.SetPreparing(ingress.Entity,row.Request,row.RequestOwner);c.Pending.Seal();
                if(!c.Probe){
                    bool ownIdentity;using(EnterTransport()){
                        if(!IngressSourceCurrent(c,observed,ingress))throw new PhysicalIngressRefused("Physical identity source changed.");
                        if(!c.SourceIdentities.TryGetValue(ingress.Entity,out var identity)||identity!=row.Request.HeldIdentity)throw new InvalidOperationException("Physical ingress lost movement identity.");
                        ownIdentity=identity!=null&&!(c.SourceIdentities.OwnerFor(ingress.Entity) is NavOwnedText text&&text.Value==identity);
                    }
                    if(ownIdentity){
                        c.Identities=NavigationCommandSlots<int,string>.CreateOwned(c.SourceIdentities.Capacity,r);
                        using(EnterTransport()){if(!IngressSourceCurrent(c,observed,ingress))throw new PhysicalIngressRefused("Physical identity copy source changed.");c.Identities.CopyPreparedFrom(c.SourceIdentities);}
                        c.Identities.SetPreparing(ingress.Entity,row.Request.HeldIdentity,row.RequestOwner.HeldIdentityOwner);c.Identities.Seal();
                    }
                }
                if(c.Attempt!=null){
                    c.Arrival=NavigationLiveArrivalTables.AllocatePrepared(c.SourceArrival.Attempts.Capacity,c.SourceArrival.References.Capacity,r,c.SourceArrival,c.SourceArrival.Ids.Capacity);
                    int capacity=NavigationLiveArrivalLeaseTable.GrowthCapacity(c.SourceLeases?.Tokens.Capacity??0,c.SourceLeases?.Count??0,r);c.Leases=NavigationLiveArrivalLeaseTable.AllocatePrepared(capacity,r,c.SourceLeases);
                    using(EnterTransport()){if(!IngressSourceCurrent(c,observed,ingress))throw new PhysicalIngressRefused("Physical lease source changed.");c.Arrival.CopySourceObserved(c.SourceArrival);if(c.SourceLeases!=null)c.Leases.CopySourceObserved(c.SourceLeases);}
                    if(row.ArrivalToken!=checked(c.LeaseSequence+1)||!c.Arrival.References.TryGetValue(row.Provider,out var provider))throw new InvalidOperationException("Physical lease provider/token changed.");
                    c.Arrival.IncrementProviderPreparing(row.Provider,r);
                    c.Lease=NavigationLiveArrivalLease.CreateOwned(new NavigationArrivalLeaseValues(row.ArrivalToken,checked(c.AcquisitionSequence+1),0,ingress.Session,attemptId,ingress.Request,ingress.Entity,ingress.MemberIdentity.Incarnation,ingress.Order,provider.Id,false),this,row.Provider,r);
                    c.Leases.AddPreparing(c.Lease);c.Leases.SealPrepared();c.Arrival.SealPrepared();
                }
                // Paid off-gate preparation can race an ordinary authority command
                // after the last source check. Refuse only that typed stale read;
                // invariant/factory failures must still reach the caller.
                try{
                    bool owned;using(EnterTransport()){
                        if(!IngressSourceCurrent(c,observed,ingress))return false;
                        owned=CanOwnAdmissionProjection(c.Probe?pending:c.Pending,c.Probe?c.Pending:probes);
                    }
                    if(owned)c.Admission=PrepareAdmissionProjection(groups,c.Probe?pending:c.Pending,requestSequence,checked(c.Publication+1),c.Operation,scope,c.SourceAdmission,sourceProbes:c.Probe?c.Pending:probes);
                    else c.LegacyAdmission=PrepareIngressLegacyAdmission(c,observed,ingress,scope);
                }catch(StaleAdmissionProjection){return false;}
                c.SealDestinations();scope.Attach(c);
                Action<object> publish=_=>{long before=GC.GetAllocatedBytesForCurrentThread();using(var mailbox=Requests.EnterPreparedWrite()){
                    if(!IngressSourceCurrent(c,observed,ingress))throw new PhysicalIngressRefused("Prepared physical ingress lost its final source.");
                    if(scope.IsCancelled||!c.Registrations.CanAppend(c.Request,c.Recipe,c.RegistrationRevision,c.RegistrationCount)||!transportOwner.CanTransferPrepared(c.Request,c.NewTransport)||authorityOperationIdentity==long.MaxValue||OwnedIngressPublications==int.MaxValue)throw new InvalidOperationException("Prepared physical ingress lost its final source.");
                    mailbox.ValidateTransferOne(c.MailboxRevision,c.MailboxCount,ingress,requestTransferHandler,c.MailboxRetired);
                    c.RetiredHost=committedHostRoot;committedHostRoot=c.Host;if(c.Probe){c.RetiredPending=ownedProbes;ownedProbes=c.Pending;probes=c.Pending;}else{c.RetiredPending=groupCommandBacking.Pending;groupCommandBacking.Pending=c.Pending;pending=c.Pending;}
                    if(c.Identities!=null){c.RetiredIdentities=groupCommandBacking.Identities;groupCommandBacking.Identities=c.Identities;movementIdentities=c.Identities;}
                    if(c.SourceRegistrations==null){liveRegistrations=c.Registrations;registeredRoutes=null;registeredGraphKeys=null;}c.Registrations.AppendNoThrow(c.Request,c.Recipe);
                    if(c.Arrival!=null){c.RetiredArrival=liveArrivalTables;liveArrivalTables=c.Arrival;arrivalAttempts=c.Arrival.Ordered;arrivalProviders=c.Arrival.References;arrivalProviderIds=c.Arrival.Ids;c.RetiredLeases=liveArrivalLeaseTable;liveArrivalLeaseTable=c.Leases;arrivalProjectionLeases=c.Leases.Ordered;arrivalRetirements=null;arrivalLeaseSequence=c.Lease.Record.Token;arrivalAcquisitionSequence=c.Lease.Record.Acquisition;}
                    transportOwner.TransferPreparedNoThrow(c.Request,c.NewTransport);mailbox.TakeOneNoThrow(c.MailboxRetired);if(c.LegacyAdmission!=null){admissionPublicationIdentity=c.LegacyAdmission.PublicationIdentity;PublishLegacyAdmissionNoThrow(c.LegacyAdmission);}else if(PublishAdmissionOwnerNoThrow(c.Admission))c.Admission=null;authorityOperationIdentity++;OwnedIngressPublications++;c.TransferNoThrow();published=true;LastOwnedIngressPublicationAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                }};
                using(EnterTransport())scope.Publish(c,publish);scope.ResolveCommitted();return true;
            }finally{if(published&&!c.Published)throw new InvalidOperationException("Physical ingress did not transfer its destination edges.");c?.Dispose();}
        }
    }
}
