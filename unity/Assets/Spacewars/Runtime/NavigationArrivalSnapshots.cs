using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Frozen capture leaf. It owns immutable roles copied from a coherent live
    // observation and never retains a mutable Session header or provider record.
    internal class NavigationArrivalAttemptSnapshot : NavOwnedStorage,INavPhysicalBody
    {
        internal readonly struct Values
        {
            internal readonly long Id,Group,Epoch,Retirement,InputProviderId;
            internal readonly int Candidate,Occupancy,Member,ReasonSelector;
            internal readonly bool Published,Retiring,LegacyProviderAccounting,RetiringCompute;
            internal readonly NavArrivalStatus Status;
            internal Values(long id,long group,long epoch,long retirement,long input,int candidate,int occupancy,int member,int reason,bool published,bool retiring,bool legacy,bool compute,NavArrivalStatus status)
            {Id=id;Group=group;Epoch=epoch;Retirement=retirement;InputProviderId=input;Candidate=candidate;Occupancy=occupancy;Member=member;ReasonSelector=reason;Published=published;Retiring=retiring;LegacyProviderAccounting=legacy;RetiringCompute=compute;Status=status;}
            internal void Validate()
            {
                if(Id<1||Group<1||Epoch<0||InputProviderId<1||Candidate<0||Candidate>64||Occupancy<0||Member<0||(uint)Status>(uint)NavArrivalStatus.CapacityExceeded||
                    (Retiring?Retirement<1||ReasonSelector<1||ReasonSelector>2:Retirement!=0||ReasonSelector!=0||RetiringCompute))throw new ArgumentException("Invalid arrival snapshot scalars.");
            }
            internal bool Same(Values other)=>Id==other.Id&&Group==other.Group&&Epoch==other.Epoch&&Retirement==other.Retirement&&InputProviderId==other.InputProviderId&&Candidate==other.Candidate&&Occupancy==other.Occupancy&&Member==other.Member&&ReasonSelector==other.ReasonSelector&&Published==other.Published&&Retiring==other.Retiring&&LegacyProviderAccounting==other.LegacyProviderAccounting&&RetiringCompute==other.RetiringCompute&&Status==other.Status;
        }
        private static readonly long Header=NavStorageLayout.ObjectBytesBound(typeof(NavigationArrivalAttemptSnapshot));
        private static long Layout(int working,int providers,bool anchor=false)=>checked((anchor?NavigationAnchoredArrivalAttemptSnapshot.AnchorHeader:Header)+64L+8L*working+8L*providers);
        internal Values Data{get;private set;}
        internal NavigationArrivalWork Work;
        internal NavigationHostArrivalIntents Intents;
        internal NavigationHostArrivalHistory History;
        internal NavOwnedText Frontier,Phase,InitialReason,RemovedReason;
        private readonly long[] workingIds;
        private readonly NavDeclarationStorage[] providerRoots;
        private bool observed,sealedRoot,released;
        internal int WorkingCount=>workingIds.Length;internal int ProviderCount=>providerRoots.Length;
        internal long WorkingId(int at)=>workingIds[at];internal NavDeclarationStorage ProviderRoot(int at)=>providerRoots[at];
        internal string CurrentReason=>Data.ReasonSelector==0?null:Data.ReasonSelector==1?InitialReason?.Value:RemovedReason?.Value;
        internal override bool IsImmutableCaptureRoot=>sealedRoot;
        internal bool UsesAnchor=>this is NavigationAnchoredArrivalAttemptSnapshot;
        internal virtual NavOwnedText FullFrontier=>null;
        protected virtual void AdoptFullFrontier(NavOwnedText text)=>throw new ArgumentException("Legacy snapshot cannot own an anchor witness.");
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.ArrivalAttemptSnapshot,value:UsesAnchor?NavPhysicalValueKind.OwnedArrivalAnchorV1:NavPhysicalValueKind.None);
        protected NavigationArrivalAttemptSnapshot(int working,int providers,bool owner,bool anchor=false):base(Layout(working,providers,anchor)){workingIds=new long[working];providerRoots=new NavDeclarationStorage[providers];if(owner)Retain();}
        internal static NavigationArrivalAttemptSnapshot AllocatePrepared(int working,int providers,INavStorageReservation reservation,bool owner=true,bool anchor=false)
        {
            if(working<1||providers<1||providers>working+1||working>AuthoritySlotMap<int,int>.MaximumSlots)throw new ArgumentException("Invalid arrival snapshot array shape.");
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(Layout(working,providers,anchor));return allocation.Register(anchor?(NavigationArrivalAttemptSnapshot)new NavigationAnchoredArrivalAttemptSnapshot(working,providers,owner):new NavigationArrivalAttemptSnapshot(working,providers,owner));}
        }
        private void Own(Values data,NavigationArrivalWork work,NavigationHostArrivalIntents intents,NavigationHostArrivalHistory history,NavOwnedText frontier,NavOwnedText phase,NavOwnedText initialReason,NavOwnedText removedReason)
        {
            if(observed)throw new InvalidOperationException("Arrival snapshot already observed.");data.Validate();Data=data;
            Work=NavPhysicalMacro.Acquire(this,work);Intents=NavPhysicalMacro.Acquire(this,intents);History=NavPhysicalMacro.Acquire(this,history);Frontier=NavPhysicalMacro.Acquire(this,frontier);Phase=NavPhysicalMacro.Acquire(this,phase);InitialReason=NavPhysicalMacro.Acquire(this,initialReason);RemovedReason=NavPhysicalMacro.Acquire(this,removedReason);observed=true;
        }
        // All allocations, including both payload arrays, precede the read gate.
        // Only concrete owned inputs are accepted here; no supplied enumerator
        // or collection callback participates in the coherent observation.
        internal void ObservePrepared(Values data,NavigationArrivalWork work,NavigationHostArrivalIntents intents,NavigationHostArrivalHistory history,NavOwnedText frontier,NavOwnedText phase,NavOwnedText initialReason,NavOwnedText removedReason,long[] ids,NavRetainedArray<ILayeredNavigationProvider> providers)
        {
            if(ids==null||providers==null||ids.Length!=WorkingCount||providers.Count!=ProviderCount)throw new ArgumentException("Arrival snapshot observation shape changed.");
            Own(data,work,intents,history,frontier,phase,initialReason,removedReason);
            for(int i=0;i<WorkingCount;i++)workingIds[i]=ids[i];
            for(int i=0;i<ProviderCount;i++){
                var provider=providers[i];var root=(provider as INavStorageFacade)?.StorageRoot as NavDeclarationStorage;
                if(root==null||!ReferenceEquals(root.Facade,provider))throw new ArgumentException("Arrival provider has no exact canonical owner.");providerRoots[i]=NavPhysicalMacro.Acquire(this,root);
            }
        }
        internal void PrepareCaptureAndSeal(INavStorageReservation reservation)
        {
            if(UsesAnchor){
                if(Work==null||!Work.UsesOwnedFrontierAnchor||FullFrontier!=null)throw new ArgumentException("Capture anchor witness roles are invalid.");
                using(NavStorageScratch.Reserve(reservation,Work.FullFrontierScratchBytes)){
                    var text=NavOwnedText.Copy(NavigationSession.ArrivalFrontier(Work),reservation);try{AdoptFullFrontier(text);}finally{text.ReleaseOwner();}
                }
            }
            ValidateAndSeal(reservation,true);
        }
        internal void SealPrepared(INavStorageReservation reservation)=>ValidateAndSeal(reservation,false);
        private void ValidateAndSeal(INavStorageReservation reservation,bool captureWitnessPrepared)
        {
            if(!observed||sealedRoot||Work==null||Intents==null||History==null||Frontier==null||Phase==null||RemovedReason==null||!Work.IsImmutableCaptureRoot||!Intents.IsImmutableCaptureRoot||!History.IsImmutableCaptureRoot||
                History.Count!=Data.Epoch||Work.LogicalEpoch!=Data.Epoch-1||Work.Status!=Data.Status||Work.CandidateCursor!=Data.Candidate||Work.OccupancyCursor!=Data.Occupancy||Work.MemberCursor!=Data.Member||
                Work.PhysicalInput.WorkingBindings.Count!=WorkingCount||Intents.Requests.Length!=WorkingCount||RemovedReason.Value!="gameplay-owner-removed"||Data.ReasonSelector==1&&InitialReason==null)throw new ArgumentException("Arrival snapshot role/epoch mismatch.");
            for(int i=0;i<WorkingCount;i++)if(workingIds[i]<1)throw new ArgumentException("Invalid working provider ID.");
            for(int i=0;i<ProviderCount;i++){
                var root=providerRoots[i];if(root==null||!root.IsImmutableCaptureRoot||!(root.Facade is ILayeredNavigationProvider))throw new ArgumentException("Invalid arrival provider role.");
                for(int j=0;j<i;j++)if(ReferenceEquals(root,providerRoots[j]))throw new ArgumentException("Duplicate distinct provider role.");
            }
            if(!ReferenceEquals(providerRoots[0],(Work.PhysicalInput.Provider as INavStorageFacade)?.StorageRoot))throw new ArgumentException("Input provider role differs from Work.");
            int expected=1;
            for(int i=0;i<WorkingCount;i++){
                var root=(Work.PhysicalInput.WorkingBindings[i].Provider as INavStorageFacade)?.StorageRoot;bool seen=ReferenceEquals(root,providerRoots[0]);
                for(int j=0;j<i&&!seen;j++)seen=ReferenceEquals(root,(Work.PhysicalInput.WorkingBindings[j].Provider as INavStorageFacade)?.StorageRoot);
                if(!seen){if(expected>=ProviderCount||!ReferenceEquals(root,providerRoots[expected++]))throw new ArgumentException("Distinct working provider role/order differs from Work.");}
            }
            if(expected!=ProviderCount)throw new ArgumentException("Unused arrival provider role.");
            if(UsesAnchor!=Work.UsesOwnedFrontierAnchor||UsesAnchor&&(FullFrontier==null||FullFrontier.Value.Length!=64)||!UsesAnchor&&FullFrontier!=null)throw new ArgumentException("Arrival snapshot anchor policy/witness mismatch.");
            using(NavStorageScratch.Reserve(reservation,checked(4096L+512L*(Work.Proposals.Count+Work.MemberOutcomes.Count))))if(Frontier.Value!=NavigationSession.LiveArrivalFrontier(Work)||Phase.Value!=Work.WorkPhase)throw new ArgumentException("Arrival snapshot frontier mismatch.");
            if(UsesAnchor&&!captureWitnessPrepared)using(NavStorageScratch.Reserve(reservation,Work.FullFrontierScratchBytes))if(FullFrontier.Value!=NavigationSession.ArrivalFrontier(Work))throw new ArgumentException("Arrival snapshot full witness mismatch.");
            sealedRoot=true;
        }
        private static void WriteValues(NavPhysicalBodyWriter writer,Values d)
        {var w=writer.Output;w.Write(d.Id);w.Write(d.Group);w.Write(d.Epoch);w.Write(d.Retirement);w.Write(d.InputProviderId);w.Write(d.Candidate);w.Write(d.Occupancy);w.Write(d.Member);w.Write(d.ReasonSelector);w.Write(d.Published);w.Write(d.Retiring);w.Write(d.LegacyProviderAccounting);w.Write(d.RetiringCompute);w.Write((int)d.Status);}
        private static Values ReadValues(NavPhysicalBodyReader reader)
        {var r=reader.Input;return new Values(r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),NavPhysicalMacro.ReadBoolean(reader),NavPhysicalMacro.ReadBoolean(reader),NavPhysicalMacro.ReadBoolean(reader),NavPhysicalMacro.ReadBoolean(reader),(NavArrivalStatus)r.ReadInt32());}
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!sealedRoot||StorageBytes!=NavStorageLayout.Charge(Layout(WorkingCount,ProviderCount,UsesAnchor)))throw new ArgumentException("Unsealed arrival snapshot.");Data.Validate();WriteValues(writer,Data);writer.Output.Write(WorkingCount);writer.Output.Write(ProviderCount);
            NavPhysicalMacro.WriteEdge(writer,this,Work);NavPhysicalMacro.WriteEdge(writer,this,Intents);NavPhysicalMacro.WriteEdge(writer,this,History);NavPhysicalMacro.WriteEdge(writer,this,Frontier);NavPhysicalMacro.WriteEdge(writer,this,Phase);NavPhysicalMacro.WriteEdge(writer,this,InitialReason,true);NavPhysicalMacro.WriteEdge(writer,this,RemovedReason);if(UsesAnchor)NavPhysicalMacro.WriteEdge(writer,this,FullFrontier);
            for(int i=0;i<WorkingCount;i++)writer.Output.Write(workingIds[i]);for(int i=0;i<ProviderCount;i++)NavPhysicalMacro.WriteEdge(writer,this,providerRoots[i]);
        }
        internal static NavigationArrivalAttemptSnapshot ReadPhysicalBody(NavPhysicalBodyReader reader,bool anchor=false)
        {
            var data=ReadValues(reader);data.Validate();int working=reader.Input.ReadInt32(),providers=reader.Input.ReadInt32();var root=AllocatePrepared(working,providers,reader.Reservation,false,anchor);
            var work=NavPhysicalMacro.ReadEdge<NavigationArrivalWork>(reader);var intents=NavPhysicalMacro.ReadEdge<NavigationHostArrivalIntents>(reader);var history=NavPhysicalMacro.ReadEdge<NavigationHostArrivalHistory>(reader);var frontier=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader);var phase=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader);var initial=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader,true);var removed=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader);
            root.Own(data,work,intents,history,frontier,phase,initial,removed);if(anchor)root.AdoptFullFrontier(NavPhysicalMacro.ReadEdge<NavOwnedText>(reader));for(int i=0;i<working;i++)root.workingIds[i]=reader.Input.ReadInt64();for(int i=0;i<providers;i++)root.providerRoots[i]=NavPhysicalMacro.Acquire(root,NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader));root.SealPrepared(reader.Reservation);return root;
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){Work?.Release();Intents?.Release();History?.Release();Frontier?.Release();FullFrontier?.Release();Phase?.Release();InitialReason?.Release();RemovedReason?.Release();for(int i=0;i<ProviderCount;i++){providerRoots[i]?.Release();providerRoots[i]=null;}}
    }
    // Separate concrete layout preserves the exact historical leaf charge.
    internal sealed class NavigationAnchoredArrivalAttemptSnapshot : NavigationArrivalAttemptSnapshot
    {
        internal static readonly long AnchorHeader=NavStorageLayout.ObjectBytesBound(typeof(NavigationAnchoredArrivalAttemptSnapshot));
        private NavOwnedText fullFrontier;
        internal override NavOwnedText FullFrontier=>fullFrontier;
        protected override void AdoptFullFrontier(NavOwnedText text){if(fullFrontier!=null||text==null)throw new ArgumentException("Missing/duplicate full arrival witness.");fullFrontier=NavPhysicalMacro.Acquire(this,text);}
        internal NavigationAnchoredArrivalAttemptSnapshot(int working,int providers,bool owner):base(working,providers,owner,true){}
    }
    internal sealed class NavigationArrivalProviderSnapshot : NavOwnedStorage,INavPhysicalBody
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationArrivalProviderSnapshot));
        internal long Id{get;private set;}internal int LogicalReferences{get;private set;}internal NavDeclarationStorage ProviderRoot{get;private set;}
        private bool sealedRoot,released;
        internal override bool IsImmutableCaptureRoot=>sealedRoot;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.ArrivalProviderSnapshot);
        private NavigationArrivalProviderSnapshot(bool owner):base(Layout){if(owner)Retain();}
        internal static NavigationArrivalProviderSnapshot AllocatePrepared(INavStorageReservation reservation,bool owner=true)
        {using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(Layout);return allocation.Register(new NavigationArrivalProviderSnapshot(owner));}}
        internal void ObservePrepared(long id,int references,NavDeclarationStorage provider)
        {
            if(sealedRoot||ProviderRoot!=null||id<1||references<1||provider==null||!provider.IsImmutableCaptureRoot||!(provider.Facade is ILayeredNavigationProvider))throw new ArgumentException("Invalid immutable provider record.");
            Id=id;LogicalReferences=references;ProviderRoot=NavPhysicalMacro.Acquire(this,provider);sealedRoot=true;
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {if(!sealedRoot||StorageBytes!=NavStorageLayout.Charge(Layout))throw new ArgumentException("Unsealed provider snapshot.");writer.Output.Write(Id);writer.Output.Write(LogicalReferences);NavPhysicalMacro.WriteEdge(writer,this,ProviderRoot);}
        internal static NavigationArrivalProviderSnapshot ReadPhysicalBody(NavPhysicalBodyReader reader)
        {long id=reader.Input.ReadInt64();int references=reader.Input.ReadInt32();var provider=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);var root=AllocatePrepared(reader.Reservation,false);root.ObservePrepared(id,references,provider);return root;}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){ProviderRoot?.Release();ProviderRoot=null;}
    }
    public sealed partial class NavigationSession
    {
        private sealed class ArrivalCatalogCapture : IDisposable
        {
            internal readonly NavigationLiveArrivalTables Source;
            internal readonly int Revision;
            internal readonly NavigationArrivalCatalog Catalog;
            private readonly NavigationLiveArrivalAttempt[] attempts;
            private readonly NavigationLiveArrivalProvider[] providers;
            private readonly int[] referenceIds;
            private IDisposable scratch;
            internal ArrivalCatalogCapture(NavigationLiveArrivalTables source,int revision,int ac,int rc,int ic,int a,int p,int l,int al,int rl,int il,INavStorageReservation r)
            {
                Source=source;Revision=revision;
                scratch=NavStorageScratch.Reserve(r,checked(NavStorageLayout.ObjectBytesBound(typeof(ArrivalCatalogCapture))+96L+8L*a+8L*p+((4L*p+7)/8*8)));
                try{attempts=new NavigationLiveArrivalAttempt[a];providers=new NavigationLiveArrivalProvider[p];referenceIds=new int[p];Catalog=NavigationArrivalCatalog.AllocatePrepared(ac,rc,ic,a,p,l,al,rl,il,r);}
                catch{scratch.Dispose();scratch=null;throw;}
            }
            internal void PinShapesObserved()
            {
                int n=0;for(int i=0;i<Source.Attempts.Capacity;i++)if(Source.Attempts.TryReadSlot(i,out _,out var value)){var owner=value.StorageOwner;if(owner==null||n==attempts.Length)throw new InvalidOperationException("Arrival capture header shape changed.");owner.Retain();attempts[n++]=owner;}
                if(n!=attempts.Length)throw new InvalidOperationException("Arrival capture header count changed.");
                n=0;for(int i=0;i<Source.Ids.Capacity;i++)if(Source.Ids.TryReadSlot(i,out _,out var value)){var owner=value.StorageOwner;if(owner==null||n==providers.Length)throw new InvalidOperationException("Arrival capture provider shape changed.");owner.Retain();providers[n++]=owner;}
                if(n!=providers.Length)throw new InvalidOperationException("Arrival capture provider count changed.");
                n=0;for(int i=0;i<Source.References.Capacity;i++)if(Source.References.TryReadSlot(i,out _,out var value)){
                    int found=-1;for(int j=0;j<providers.Length;j++)if(ReferenceEquals(value.StorageOwner,providers[j])){found=j;break;}
                    if(found<0||n==referenceIds.Length)throw new InvalidOperationException("Arrival capture provider indexes differ.");referenceIds[n++]=found;
                }
                if(n!=referenceIds.Length)throw new InvalidOperationException("Arrival capture reference count changed.");
            }
            internal void AllocateLeaves(INavStorageReservation r)
            {
                for(int i=0;i<attempts.Length;i++){var owner=attempts[i];var leaf=NavigationArrivalAttemptSnapshot.AllocatePrepared(owner.Record.WorkingProviderIds.Length,owner.Providers.Count,r,anchor:owner.Work.UsesOwnedFrontierAnchor);try{Catalog.ObserveAttempt(i,leaf);}finally{leaf.ReleaseStorage();}}
                for(int i=0;i<providers.Length;i++){var leaf=NavigationArrivalProviderSnapshot.AllocatePrepared(r);try{Catalog.ObserveProviderId(i,leaf);}finally{leaf.ReleaseStorage();}}
                for(int i=0;i<referenceIds.Length;i++)Catalog.ObserveReference(i,Catalog.ProviderId(referenceIds[i]));
            }
            internal void Observe(NavigationSession session)
            {
                if(!ReferenceEquals(session.liveArrivalTables,Source)||Source.Revision!=Revision||session.arrivalProjectionLeases.Count!=Catalog.LeaseCount)throw new InvalidOperationException("Arrival catalog capture source changed.");
                Catalog.ObserveValues(new NavigationArrivalCatalog.Values(session.Generation,session.requestSequence,session.arrivalSequence,session.arrivalCommitSequence,session.arrivalProviderSequence,session.arrivalLeaseSequence,session.arrivalAcquisitionSequence,session.arrivalRetirementSequence,session.arrivalReleaseSequence));
                for(int i=0;i<attempts.Length;i++){ObserveArrivalAttemptSnapshot(attempts[i].Record,Catalog.Attempt(i));Catalog.ObserveSortedAttempt(i,Source.SortedId(i));}
                for(int i=0;i<providers.Length;i++){var owner=providers[i];Catalog.ProviderId(i).ObservePrepared(owner.Record.Id,owner.Record.References,owner.ProviderRoot);}
                Catalog.ObserveLayout(0,Source.Attempts);Catalog.ObserveLayout(1,Source.References);Catalog.ObserveLayout(2,Source.Ids);
                int n=0;foreach(var lease in session.arrivalProjectionLeases.Values)Catalog.ObserveLease(n++,lease.Token,lease.ProviderId);
            }
            internal void Seal(INavStorageReservation r){for(int i=0;i<Catalog.AttemptCount;i++)Catalog.Attempt(i).PrepareCaptureAndSeal(r);Catalog.PrepareLeaseOrder();Catalog.SealPrepared(r);}
            public void Dispose(){Catalog?.ReleaseStorage();foreach(var owner in attempts)owner?.Release();foreach(var owner in providers)owner?.Release();Source.Release();scratch?.Dispose();scratch=null;}
        }
        private NavigationLiveArrivalTables liveArrivalTables;
        private ArrivalCatalogCapture PrepareArrivalCatalogCapture(INavStorageReservation r)
        {
            NavigationLiveArrivalTables source;int revision,ac,rc,ic,a,p,l,al,rl,il;
            using(EnterTransport()){
                source=liveArrivalTables;if(source==null)return null;source.Retain();revision=source.Revision;ac=source.Attempts.Capacity;rc=source.References.Capacity;ic=source.Ids.Capacity;a=source.Count;p=source.Ids.Count;l=arrivalProjectionLeases.Count;al=source.Attempts.LayoutLength;rl=source.References.LayoutLength;il=source.Ids.LayoutLength;
            }
            ArrivalCatalogCapture capture=null;
            try{
                capture=new ArrivalCatalogCapture(source,revision,ac,rc,ic,a,p,l,al,rl,il,r);
                using(EnterTransport()){if(!ReferenceEquals(liveArrivalTables,source)||source.Revision!=revision)throw new InvalidOperationException("Arrival capture source changed during preparation.");capture.PinShapesObserved();}
                capture.AllocateLeaves(r);return capture;
            }catch{if(capture!=null)capture.Dispose();else source.Release();throw;}
        }
        private static void ObserveArrivalAttemptSnapshot(ArrivalAttempt attempt,NavigationArrivalAttemptSnapshot snapshot)
        {
            var owner=attempt.StorageOwner;if(owner==null||!ReferenceEquals(owner.Record,attempt)||!ReferenceEquals(owner.Work,attempt.Work)||!ReferenceEquals(owner.History,attempt.History)||!ReferenceEquals(owner.Intents,attempt.OwnedIntents)||
                !ReferenceEquals(owner.Frontier.Value,attempt.Frontier)||!ReferenceEquals(owner.Phase.Value,attempt.SnapshotPhase))throw new InvalidOperationException("Arrival capture source roles changed.");
            int reason=0;if(attempt.Retiring){if(owner.Reason!=null&&ReferenceEquals(attempt.RetirementReason,owner.Reason.Value))reason=1;else if(ReferenceEquals(attempt.RetirementReason,owner.RemovedReason.Value))reason=2;else throw new InvalidOperationException("Arrival reason has no exact owned selector.");}
            var data=new NavigationArrivalAttemptSnapshot.Values(attempt.Id,attempt.Group,attempt.Epoch,attempt.Retirement,attempt.InputProviderId,attempt.SnapshotCandidate,attempt.SnapshotOccupancy,attempt.SnapshotMember,reason,attempt.Published,attempt.Retiring,attempt.LegacyProviderAccounting,attempt.Retiring&&attempt.Working||attempt.RestoredRetiringCompute,attempt.SnapshotStatus);
            snapshot.ObservePrepared(data,owner.Work,owner.Intents,owner.History,owner.Frontier,owner.Phase,owner.Reason,owner.RemovedReason,attempt.WorkingProviderIds,owner.Providers);
        }
        private void RestoreLiveArrivalCatalog(NavigationArrivalCatalog catalog,NavigationHostRoot host)
        {
            var d=catalog.Data;
            if(!physicalArrivalEnabled||host==null||d.World!=Generation||d.Requests!=requestSequence||d.Attempts!=arrivalSequence||d.Commits!=arrivalCommitSequence||d.Providers!=arrivalProviderSequence||d.Leases!=arrivalLeaseSequence||d.Acquisitions!=arrivalAcquisitionSequence||d.Retirements!=arrivalRetirementSequence||d.Releases!=arrivalReleaseSequence||catalog.AttemptCount!=arrivalAttempts.Count||catalog.ProviderCount!=arrivalProviderIds.Count||catalog.LeaseCount!=arrivalProjectionLeases.Count)throw new ArgumentException("Arrival BODY catalog sequences/counts differ from authority tail.");
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Decode,Generation,admissionPublicationIdentity,out var scope))throw new InvalidOperationException("Live arrival table decode admission refused.");
            using(scope){NavigationLiveArrivalTables tables=null;
                try{
                    var r=scope.OwnedReservation();tables=NavigationLiveArrivalTables.AllocatePrepared(catalog.AttemptCapacity,catalog.ReferenceCapacity,r,null,catalog.IdCapacity);tables.InitializeCatalogLayout(catalog);
                    int index=0;
                    for(int slot=0;slot<catalog.LayoutValue(2,0);slot++)if(!tables.Ids.IsPreparedHole(slot)){
                        var leaf=catalog.ProviderId(index++);if(!arrivalProviderIds.TryGetValue(leaf.Id,out var original)||original.References!=leaf.LogicalReferences||!ReferenceEquals((original.Provider as INavStorageFacade)?.StorageRoot,leaf.ProviderRoot))throw new ArgumentException("Arrival provider record differs from BODY catalog.");
                        var owner=NavigationLiveArrivalProvider.CreateOwned(leaf.Id,leaf.LogicalReferences,(ILayeredNavigationProvider)leaf.ProviderRoot.Facade,r);try{tables.Ids.AddAtPreparedSlot(slot,leaf.Id,owner.Record,owner);}finally{owner.ReleaseStorage();}
                    }
                    index=0;for(int slot=0;slot<catalog.LayoutValue(1,0);slot++)if(!tables.References.IsPreparedHole(slot)){
                        var leaf=catalog.Reference(index++);var record=tables.Ids[leaf.Id];tables.References.AddAtPreparedSlot(slot,record.Provider,record,record.StorageOwner);
                    }
                    index=0;for(int slot=0;slot<catalog.LayoutValue(0,0);slot++)if(!tables.Attempts.IsPreparedHole(slot)){
                        var leaf=catalog.Attempt(index++);if(!arrivalAttempts.TryGetValue(leaf.Data.Id,out var original)||original.Projections?.Count>0)throw new ArgumentException("Arrival attempt has no corresponding BODY recipe.");
                        var expected=new NavigationArrivalAttemptSnapshot.Values(original.Id,original.Group,original.Epoch,original.Retirement,original.InputProviderId,original.SnapshotCandidate,original.SnapshotOccupancy,original.SnapshotMember,leaf.Data.ReasonSelector,original.Published,original.Retiring,original.LegacyProviderAccounting,original.RestoredRetiringCompute,original.SnapshotStatus);
                        if(!expected.Same(leaf.Data)||original.Frontier!=leaf.Frontier.Value||original.SnapshotPhase!=leaf.Phase.Value||(original.RetirementReason??"")!=(leaf.CurrentReason??"")||!ReferenceEquals(original.Work,leaf.Work)||!ReferenceEquals(original.History,leaf.History)||!ReferenceEquals(original.Intents,leaf.Intents.Requests)||original.WorkingProviderIds.Length!=leaf.WorkingCount)throw new ArgumentException("Arrival live header roles differ from BODY catalog.");
                        for(int n=0;n<leaf.WorkingCount;n++)if(original.WorkingProviderIds[n]!=leaf.WorkingId(n))throw new ArgumentException("Arrival working provider IDs differ from BODY catalog.");
                        var owner=NavigationLiveArrivalAttempt.CreateOwned(original,leaf.Work,leaf.Intents,leaf.History,r,leaf);try{tables.Attempts.AddAtPreparedSlot(slot,original.Id,owner.Record,owner);}finally{owner.ReleaseStorage();}
                    }
                    for(int i=0;i<catalog.LeaseCount;i++)if(!arrivalProjectionLeases.TryGetValue(catalog.LeaseToken(i),out var lease)||lease.ProviderId!=catalog.LeaseProvider(i))throw new ArgumentException("Arrival lease differs from BODY catalog.");
                    tables.SealPrepared();scope.Attach(tables);scope.Commit(tables,_=>{liveArrivalTables=tables;arrivalAttempts=tables.Ordered;arrivalProviders=tables.References;arrivalProviderIds=tables.Ids;});tables=null;
                }finally{tables?.ReleaseStorage();}
            }
        }
        internal NavigationArrivalAttemptSnapshot CaptureArrivalAttemptSnapshotOwned(long id,INavStorageReservation reservation)
        {
            NavigationLiveArrivalAttempt owner;
            using(EnterTransport()){
                if(!arrivalAttempts.TryGetValue(id,out var attempt)||attempt.StorageOwner==null)throw new InvalidOperationException("Arrival capture needs its owned live header.");owner=attempt.StorageOwner;owner.Retain();
            }
            NavigationArrivalAttemptSnapshot snapshot=null;
            try{
                snapshot=NavigationArrivalAttemptSnapshot.AllocatePrepared(owner.Record.WorkingProviderIds.Length,owner.Providers.Count,reservation,anchor:owner.Work.UsesOwnedFrontierAnchor);
                using(EnterTransport()){
                    if(!arrivalAttempts.TryGetValue(id,out var current)||!ReferenceEquals(current.StorageOwner,owner))throw new InvalidOperationException("Arrival capture source was retired.");ObserveArrivalAttemptSnapshot(current,snapshot);
                }
                snapshot.PrepareCaptureAndSeal(reservation);return snapshot;
            }catch{snapshot?.ReleaseStorage();throw;}
            finally{owner.Release();}
        }
    }
}
