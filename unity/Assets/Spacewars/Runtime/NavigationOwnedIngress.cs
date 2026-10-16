using System;
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
            internal NavigationCommandSlots<int,NavigationRequest> SourcePending,Pending;
            internal NavOwnedStorage RetiredPending;
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
            private bool released,hostDestination,pendingDestination,registrationDestination,arrivalDestination,leaseDestination,requestDestination,recipeDestination,transportDestination;
            private PreparedIngress():base(Layout){MailboxRetired=new NavOwnedStorage[2];Retain();}
            internal static PreparedIngress Create(INavStorageReservation r){using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);return a.Register(new PreparedIngress());}}
            internal void SealDestinations()
            {
                Host.Retain();hostDestination=true;Pending.Retain();pendingDestination=true;
                Request.Retain();requestDestination=true;Recipe.Retain();recipeDestination=true;
                if(NewTransport){Request.Retain();transportDestination=true;}
                if(SourceRegistrations==null){Registrations.Retain();registrationDestination=true;}
                if(Arrival!=null){Arrival.Retain();arrivalDestination=true;Leases.Retain();leaseDestination=true;}
            }
            internal void TransferNoThrow(){hostDestination=pendingDestination=registrationDestination=arrivalDestination=leaseDestination=requestDestination=recipeDestination=transportDestination=false;Published=true;}
            public void Dispose(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>Dispose();
            protected override void ReleaseEdges()
            {
                foreach(var value in MailboxRetired)value?.Release();RetiredHost?.Release();RetiredPending?.Release();RetiredArrival?.Release();RetiredLeases?.Release();
                if(hostDestination)Host.Release();if(pendingDestination)Pending.Release();if(registrationDestination)Registrations.Release();if(arrivalDestination)Arrival.Release();if(leaseDestination)Leases.Release();
                if(requestDestination)Request.Release();if(recipeDestination)Recipe.Release();if(transportDestination)Request.Release();
                SourceHost?.Release();Host?.Release();SourceAdmission?.Release();Admission?.Release();SourcePending?.Release();Pending?.ReleaseStorage();
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
            if(!ReferenceEquals(committedHostRoot,c.SourceHost)||!ReferenceEquals(admissionOwner,c.SourceAdmission)||!IngressObservationCompatible(observed,ingress)||authorityOperationIdentity!=c.Operation||commandGroup!=null||admissionBatchDepth!=0||hostAllocationThread!=c.AllocationThread||certifiedHostAttachmentIdentity!=c.Attachment||!ReferenceEquals(certifiedHost,c.Worker)||!Admission.Contains(ingress)||!ReferenceEquals(c.Probe?probes:pending,c.SourcePending)||c.SourcePending.Revision!=c.PendingRevision||!(c.Probe?probes:pending).TryGetValue(ingress.Entity,out var active)||!ReferenceEquals(active,ingress)||!ReferenceEquals(liveRegistrations,c.SourceRegistrations)||RegisteredRequests.Count!=c.RegistrationCount||RegistrationIndex(c.Request.Request)>=0)return false;
            if(c.SourceRegistrations!=null&&c.SourceRegistrations.Revision!=c.RegistrationRevision)return false;
            if(c.Attempt!=null&&(!ReferenceEquals(liveArrivalTables,c.SourceArrival)||c.SourceArrival.Revision!=c.ArrivalRevision||!ReferenceEquals(liveArrivalLeaseTable,c.SourceLeases)||c.SourceLeases!=null&&c.SourceLeases.Revision!=c.LeaseRevision||arrivalLeaseSequence!=c.LeaseSequence||arrivalAcquisitionSequence!=c.AcquisitionSequence||!arrivalAttempts.TryGetValue(c.Attempt.Record.Id,out var attempt)||!ReferenceEquals(attempt.StorageOwner,c.Attempt)||c.Attempt.Revision!=c.AttemptRevision||attempt.Retiring||attempt.Reconstructing))return false;
            return true;
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
                if(!source.TryGetValue(ingress.Entity,out var active)||!ReferenceEquals(active,ingress))return false;
                if(liveRegistrations==null&&registeredRoutes.Count!=0)return null;
                if(attemptId!=0&&(liveArrivalTables==null||liveArrivalLeaseTable==null&&arrivalProjectionLeases.Count!=0))return null;
            }
            try{
                var r=scope.OwnedReservation();c=PreparedIngress.Create(r);c.Probe=ingress.Entity<0;var row=(NavigationHostRow)host.Rows[key];
                ValidateScheduledRoute(ingress,row.Scheduled,observed);
                c.Host=NavPhysicalMacro.Acquire(c,host);c.Request=NavPhysicalMacro.Acquire(c,row.RequestOwner);c.Recipe=NavPhysicalMacro.Acquire(c,row.RecipeOwner);
                using(EnterTransport()){
                    c.SourceHost=NavPhysicalMacro.Acquire(c,expected);c.SourceAdmission=NavPhysicalMacro.Acquire(c,admissionOwner);c.SourcePending=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavigationRequest>)(c.Probe?probes:pending));c.PendingRevision=c.SourcePending.Revision;c.Operation=authorityOperationIdentity;c.AllocationThread=hostAllocationThread;c.Worker=certifiedHost;c.Attachment=certifiedHostAttachmentIdentity;
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
                if(c.Attempt!=null){
                    c.Arrival=NavigationLiveArrivalTables.AllocatePrepared(c.SourceArrival.Attempts.Capacity,c.SourceArrival.References.Capacity,r,c.SourceArrival,c.SourceArrival.Ids.Capacity);
                    int capacity=NavigationLiveArrivalLeaseTable.GrowthCapacity(c.SourceLeases?.Tokens.Capacity??0,c.SourceLeases?.Count??0,r);c.Leases=NavigationLiveArrivalLeaseTable.AllocatePrepared(capacity,r,c.SourceLeases);
                    using(EnterTransport()){if(!IngressSourceCurrent(c,observed,ingress))throw new PhysicalIngressRefused("Physical lease source changed.");c.Arrival.CopySourceObserved(c.SourceArrival);if(c.SourceLeases!=null)c.Leases.CopySourceObserved(c.SourceLeases);}
                    if(row.ArrivalToken!=checked(c.LeaseSequence+1)||!c.Arrival.References.TryGetValue(row.Provider,out var provider))throw new InvalidOperationException("Physical lease provider/token changed.");
                    c.Arrival.IncrementProviderPreparing(row.Provider,r);
                    c.Lease=NavigationLiveArrivalLease.CreateOwned(new NavigationArrivalLeaseValues(row.ArrivalToken,checked(c.AcquisitionSequence+1),0,ingress.Session,attemptId,ingress.Request,ingress.Entity,ingress.MemberIdentity.Incarnation,ingress.Order,provider.Id,false),this,row.Provider,r);
                    c.Leases.AddPreparing(c.Lease);c.Leases.SealPrepared();c.Arrival.SealPrepared();
                }
                c.Admission=PrepareAdmissionProjection(groups,c.Probe?pending:c.Pending,requestSequence,checked(c.SourceAdmission.Value.PublicationIdentity+1),c.Operation,scope,c.SourceAdmission,sourceProbes:c.Probe?c.Pending:probes);
                c.SealDestinations();scope.Attach(c);
                Action<object> publish=_=>{long before=GC.GetAllocatedBytesForCurrentThread();using(var mailbox=Requests.EnterPreparedWrite()){
                    if(!IngressSourceCurrent(c,observed,ingress))throw new PhysicalIngressRefused("Prepared physical ingress lost its final source.");
                    if(scope.IsCancelled||!c.Registrations.CanAppend(c.Request,c.Recipe,c.RegistrationRevision,c.RegistrationCount)||!transportOwner.CanTransferPrepared(c.Request,c.NewTransport)||authorityOperationIdentity==long.MaxValue||OwnedIngressPublications==int.MaxValue)throw new InvalidOperationException("Prepared physical ingress lost its final source.");
                    mailbox.ValidateTransferOne(c.MailboxRevision,c.MailboxCount,ingress,requestTransferHandler,c.MailboxRetired);
                    c.RetiredHost=committedHostRoot;committedHostRoot=c.Host;if(c.Probe){c.RetiredPending=ownedProbes;ownedProbes=c.Pending;probes=c.Pending;}else{c.RetiredPending=groupCommandBacking.Pending;groupCommandBacking.Pending=c.Pending;pending=c.Pending;}
                    if(c.SourceRegistrations==null){liveRegistrations=c.Registrations;registeredRoutes=null;registeredGraphKeys=null;}c.Registrations.AppendNoThrow(c.Request,c.Recipe);
                    if(c.Arrival!=null){c.RetiredArrival=liveArrivalTables;liveArrivalTables=c.Arrival;arrivalAttempts=c.Arrival.Ordered;arrivalProviders=c.Arrival.References;arrivalProviderIds=c.Arrival.Ids;c.RetiredLeases=liveArrivalLeaseTable;liveArrivalLeaseTable=c.Leases;arrivalProjectionLeases=c.Leases.Ordered;arrivalRetirements=null;arrivalLeaseSequence=c.Lease.Record.Token;arrivalAcquisitionSequence=c.Lease.Record.Acquisition;}
                    transportOwner.TransferPreparedNoThrow(c.Request,c.NewTransport);mailbox.TakeOneNoThrow(c.MailboxRetired);if(PublishAdmissionOwnerNoThrow(c.Admission))c.Admission=null;authorityOperationIdentity++;OwnedIngressPublications++;c.TransferNoThrow();published=true;LastOwnedIngressPublicationAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                }};
                using(EnterTransport())scope.Publish(c,publish);scope.ResolveCommitted();return true;
            }finally{if(published&&!c.Published)throw new InvalidOperationException("Physical ingress did not transfer its destination edges.");c?.Dispose();}
        }
    }
}
