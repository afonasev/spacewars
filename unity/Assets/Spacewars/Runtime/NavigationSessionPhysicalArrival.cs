using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        // Physical attempts are prepared off transportGate. The gameplay
        // dictionaries borrow the root's immutable work/input facades.
        private bool physicalArrivalEnabled=true;
        internal bool TryObservePhysicalArrivalIngress(out NavigationRequest request,out NavigationAdmissionPin observed,
            out NavArrivalInput input,out NavArrivalProposal proposal,out long token,out long attemptId)
        {
            using(EnterTransport()){
                request=null;observed=default;input=null;proposal=null;token=0;attemptId=0;
                if(commandGroup!=null||admissionBatchDepth!=0||!Requests.TryPeek(out var head)||!QuantumIngressAllowed(head)||!Admission.Contains(head))return false;
                var attempt=arrivalAttempts.Values.SingleOrDefault(a=>a.Intents.Any(i=>i.Request==head.Request));
                if(attempt==null||!attempt.Work.IsRegistered||attempt.Retiring||attempt.Reconstructing)return false;
                proposal=attempt.Work.Proposals.SingleOrDefault(p=>p.Member.RequestSequence==head.Request);if(proposal==null)return false;
                request=head;observed=new NavigationAdmissionPin(admissionOwner,admission);input=attempt.Input;token=checked(arrivalLeaseSequence+1);attemptId=attempt.Id;return true;
            }
        }
        internal void PublishRegisteredPhysicalArrival(NavigationRetentionBudget.Scope scope,NavigationHostRoot expected,
            NavigationHostRoot candidate,NavigationAdmission observed,NavigationRequest ingress,NavNameKey key,long attemptId)
        {
            var owned=PublishOwnedPhysicalIngress(scope,expected,candidate,observed,ingress,key,attemptId);if(owned.HasValue){if(!owned.Value)throw new PhysicalIngressRefused("Owned physical arrival ingress refused.");return;}
            using(EnterTransport())UseLegacyRegistrations();if(scope.Prepared==null)scope.Attach(candidate);
            var row=(NavigationHostRow)candidate.Rows[key];
            ValidateScheduledRoute(ingress,row.Scheduled,observed);
            PublishPhysicalHostRoot(scope,expected,candidate,()=>{
                if(!arrivalAttempts.TryGetValue(attemptId,out var attempt)||attempt.Retiring||attempt.Reconstructing||
                    row.ArrivalToken!=arrivalLeaseSequence+1||Admission.PublicationIdentity!=observed.PublicationIdentity||!Admission.Contains(ingress))
                    throw new InvalidOperationException("Physical arrival ingress changed before publication.");
                if(!Requests.TryTransfer(head=>{
                    if(!ReferenceEquals(head,ingress)||!pending.TryGetValue(head.Entity,out var active)||!ReferenceEquals(active,head))return false;
                    var lease=new ArrivalProviderLease{Session=this,Token=row.ArrivalToken,Acquisition=checked(++arrivalAcquisitionSequence),World=head.Session,Attempt=attemptId,
                        Request=head.Request,Entity=head.Entity,Incarnation=head.MemberIdentity.Incarnation,Order=head.Order,Provider=row.Provider,ProviderId=RetainArrivalProvider(row.Provider),Attached=true,Reason=""};
                    lease.Binding=ProjectionLeaseBinding(lease);arrivalProjectionLeases.Add(lease.Token,lease);arrivalLeaseSequence=lease.Token;
                    registeredRoutes.Add(row.Request);registeredGraphKeys.Add(row.Request,row.Scheduled.GraphKey);pending[head.Entity]=row.Request;
                    PublishAdmission();return true;
                },out var transferred)||!ReferenceEquals(transferred,ingress))throw new InvalidOperationException("Physical arrival mailbox changed before publication.");
            });
        }
        internal bool RetirePhysicalArrivalOwner()
        {
            var source=PinCommittedHostRoot();NavigationHostRoot next=null;NavigationRetentionBudget.Scope scope=null;NavigationHostRequest retiredRequest=null;NavDeclarationStorage retiredRecipe=null;
            if(source==null)return false;
            try{
                ArrivalProviderLease lease;using(EnterTransport())lease=FirstRetiringArrivalLease;
                NavNameKey key=default;long obsolete=0;
                if(lease!=null){
                    foreach(var row in source.Rows)if(((NavigationHostRow)row.Value).ArrivalToken==lease.Token){key=row.Key;break;}
                    if(key.Value==null)throw new InvalidOperationException("Retiring physical arrival lost its row.");
                }else{
                    using(EnterTransport())foreach(var entry in source.ArrivalWork)if(!arrivalAttempts.ContainsKey(entry.Key)){obsolete=entry.Key;break;}
                    if(obsolete==0)return false;
                }
                if(!TryOpenPhysicalHostScope(NavigationPreparationKind.Arrival,source.Epoch,Admission.PublicationIdentity,out scope))return false;
                var reservation=scope.OwnedReservation();next=source.ForkOwned(reservation);
                if(lease!=null)next.StageRetireArrivalRow(key,reservation);else next.StageRetireArrivalWork(obsolete,reservation);
                var f=source.Frontier;
                next.SetFrontier(new CertifiedRouteKernel.PumpFrontier(f.Stage,f.OriginalBudget,f.ScansUsed,f.TransfersUsed,f.ReadyUsed,f.ProjectionUsed,f.ProjectionAllowance,f.SolveWorkLimit,f.SolveConsumed,f.AllocationFloor,f.PhaseUsed+1,f.ScanAllowance),source.Epoch,checked(source.Progress+1));
                next.SealPrepared();scope.Attach(next);
                PublishPhysicalHostRoot(scope,source,next,()=>{
                    if(lease!=null){
                        if(!lease.Retiring||!arrivalProjectionLeases.TryGetValue(lease.Token,out var current)||!ReferenceEquals(current,lease))throw new InvalidOperationException("Retiring arrival owner changed.");
                        var row=(NavigationHostRow)source.Rows[key];if(liveRegistrations!=null){int index=RegistrationIndex(row.Request);if(index<0)throw new InvalidOperationException("Retirement lost its registration.");liveRegistrations.ExtractNoThrow(index,out retiredRequest,out retiredRecipe);}else{registeredGraphKeys.Remove(row.Request);registeredRoutes.Remove(row.Request);}ReleaseArrivalProjection(lease,"retirement");
                    }else if(arrivalAttempts.ContainsKey(obsolete))throw new InvalidOperationException("Obsolete arrival regained an owner.");
                });return true;
            }finally{retiredRequest?.Release();retiredRecipe?.Release();next?.ReleaseStorage();scope?.Discard();source.Release();}
        }
        internal bool PreparePhysicalArrivalAttempt()
        {
            if(!physicalArrivalEnabled)return false;
            NavigationRetentionBudget.Scope scope=null;NavigationHostRoot source=null,next=null;
            NavArrivalInput input=null;NavigationArrivalWork work=null;NavigationPreparedArrivalObservation observation=null;
            NavigationLiveArrivalAttempt preparedAttempt=null;bool installedAttempt=false;
            NavigationLiveArrivalTables observedTables=null,preparedTables=null;int tableRevision=0;long providerSequence=0,preparedProviderSequence=0;
            try{
                if(!TryOpenPhysicalHostScope(NavigationPreparationKind.Arrival,Generation,Admission.PublicationIdentity,out scope))return false;
                var reservation=scope.OwnedReservation();
                long observationBytes;int rowCapacity,memberCapacity,actorCapacity,occupantCapacity;
                using(EnterTransport()){rowCapacity=allocating.Count;memberCapacity=groups.Count==0?0:groups.Values.Max(g=>g.Members.Length);actorCapacity=Crowd.Units.Count;occupantCapacity=checked(3*actorCapacity+pending.Count+arrivalLedger.Count);observationBytes=checked(8192L+2048L*rowCapacity+2048L*actorCapacity+256L*memberCapacity);}
                using var observationScratch=NavStorageScratch.Reserve(reservation,observationBytes);
                observation=NavigationPreparedArrivalObservation.CreateOwned(rowCapacity,memberCapacity,occupantCapacity,reservation);
                NavigationRequest[] rows;GroupOrderState group;NavigationAdmission observed;
                NavigationProfile observedProfile;NavGeometry observedGeometry;NavCrowd observedCrowd;NavRetainedArray<NavNameReference> observedNames;
                NavGeometry basis;long id,operation,attachment;CertifiedRouteKernel worker;
                using(EnterTransport()){
                    if(hostAllocationThread!=0||allocating.Count>rowCapacity||Crowd.Units.Count>actorCapacity||arrivalAttempts.Values.Any(a=>a.Working||!a.Retiring&&!a.Published))return false;
                    var first=allocating.Values.OrderBy(r=>r.Request).FirstOrDefault(r=>!arrivalAttempts.Values.Any(a=>!a.Retiring&&a.Intents.Any(i=>i.Request==r.Request)));
                    if(first==null||!QuantumIngressAllowed(first))return false;
                    rows=allocating.Values.Where(r=>r.MemberIdentity.GroupId==first.MemberIdentity.GroupId&&QuantumIngressAllowed(r)).OrderBy(r=>r.Request).ToArray();
                    var liveGroup=groups[first.MemberIdentity.GroupId];if(liveGroup.Members.Length>memberCapacity)return false;
                    group=liveGroup.Copy();basis=navigationGeometry;observed=Admission;id=checked(arrivalSequence+1);operation=authorityOperationIdentity;attachment=certifiedHostAttachmentIdentity;worker=certifiedHost;observedTables=liveArrivalTables;observedTables?.Retain();tableRevision=observedTables?.Revision??0;providerSequence=arrivalProviderSequence;
                    observedProfile=profile;observedGeometry=geometry;observedCrowd=Crowd;observedNames=terrainNameOwners;
                    observation.Observe(rows,group);observation.ObserveTerrain(terrain,observedNames);observation.Pin(observedProfile.StorageRoot);observation.Pin(observedGeometry.StorageRoot);observation.Pin(basis.StorageRoot);observation.Pin(observedNames);observation.Pin(admissionOwner);
                    var groupOwner=(groups as NavigationCommandSlots<long,GroupOrderState>)?.OwnerFor(group.GroupId) as NavigationLiveGroup;observation.Pin(groupOwner?.ImmutableVersion);
                    source=PinCommittedHostRoot();if(source==null)throw new InvalidOperationException("Physical arrival creator needs a committed Pump root.");observation.Pin(source);
                }
                // The paid occupancy/source snapshot remains usable while unrelated
                // actors move. Current A intent, HOLD and geometry still guard it;
                // publication/installation rechecks current endpoint occupancy.
                bool ObservationCurrent()=>ReferenceEquals(profile,observedProfile)&&ReferenceEquals(geometry,observedGeometry)&&ReferenceEquals(navigationGeometry,basis)&&ReferenceEquals(Crowd,observedCrowd)&&ReferenceEquals(terrainNameOwners,observedNames)&&rows.All(CurrentArrivalIntent);
                next=source.ForkOwned(reservation);
                double radius=rows.Max(r=>r.Profile.Radius);
                var terms=observedProfile.ForUnit(radius,observedProfile.TankSpeed,observedProfile.TankTurnSpeed);
                double spacing=Math.Max(terms.ArrivalSlotSpacing,4*radius+2*terms.ArrivalTolerance+2*terms.LocalGridCell);
                int limit=(int)Math.Ceiling(observedGeometry.HalfExtent*2/spacing);
                long bound=checked((2L*limit+1)*(2L*limit+1));
                using(var inputScratch=NavStorageScratch.Reserve(reservation,checked(4096L+128L*bound+1024L*rows.Length+1024L*actorCapacity))){
                    var candidates=new System.Collections.Generic.List<NavLocation>();
                    for(int ring=0;ring<=limit;ring++)for(int z=-ring;z<=ring;z++)for(int x=-ring;x<=ring;x++){
                        if(ring>0&&Math.Abs(x)!=ring&&Math.Abs(z)!=ring)continue;
                        var point=new NavPoint(group.OriginalGoal.X+x*spacing,group.OriginalGoal.Z+z*spacing);
                        if(observedGeometry.IsFree(point,0)&&observation.LocatePoint(point,out var at))candidates.Add(at);
                    }
                    if(candidates.Count==0)throw new InvalidOperationException("Accepted arrival has no geometric candidate.");
                    var canonical=next.StageArrivalIntents(id,rows,basis,reservation);
                    var members=canonical.Select(r=>new NavArrivalMember(r.Entity,r.MemberIdentity.Incarnation,r.Order,r.MemberIdentity.MobilityRevision,r.StartLocation.Value,
                        new NavClearanceProfile("ground",1,"ground",(int)r.MemberIdentity.MobilityRevision,r.Profile.Radius),Generation,group.GroupId,group.OrderRevision,group.CommandSequence,group.IssuedTick,
                        r.MemberIdentity.OrderRevision,r.MemberIdentity.OrderRevision,r.Request,r.HeldIdentity)).ToArray();
                    var bindings=new NavArrivalWorkingBinding[rows.Length];
                    // Temporary concrete providers are kept admitted until their
                    // complete owned copy is present in the same scope.
                    long construction=rows.Sum(r=>ProspectiveArrivalProviderBytes(r.Geometry));
                    using(NavStorageScratch.Reserve(reservation,construction)){
                        for(int n=0;n<rows.Length;n++)bindings[n]=new NavArrivalWorkingBinding(rows[n].Entity,ArrivalProvider(canonical[n].Geometry,canonical[n].HeldIdentity),
                            new NavGraphProfile(NavigationAdmission.ExactProfileBinding(canonical[n].Profile),"distance",1,rows[n].Profile.GridCell));
                        using(EnterTransport()){if(!ObservationCurrent()||checked(3*Crowd.Units.Count+pending.Count+arrivalLedger.Count)>occupantCapacity)return false;observation.ObserveOccupancy(CaptureArrivalOccupancySnapshot(members));}
                        var raw=new NavArrivalInput("geometry:"+basis.Revision,"profile:"+NavigationAdmission.ExactProfileBinding(observedProfile),bindings[0].Provider,bindings[0].Profile,terms,
                            new NavGraphCompileLimits(1048576,300000,1200000),new NavArrivalCandidateSource("authority-spiral",1,group.OriginalLocation??candidates[0],candidates),members,
                            observation.Occupancy,1048576,bindings,true);
                        input=NavigationArrivalPhysicalInput.CopyInitial(raw,reservation);
                    }
                    work=NavigationArrivalWork.CreateOwned(input,reservation);work.UseSessionProviderOwnership(true);work.SealPrepared();next.StageArrivalWork(id,work,reservation);
                    var rawAttempt=new ArrivalAttempt(true){Id=id,Group=group.GroupId,Input=input,Work=work,Intents=canonical,Epoch=0,History=(NavigationHostArrivalHistory)next.ArrivalHistory[id]};
                    UpdatePhysicalArrivalSnapshot(rawAttempt);
                    preparedAttempt=NavigationLiveArrivalAttempt.CreateOwned(rawAttempt,work,(NavigationHostArrivalIntents)next.ArrivalIntentSets[id],rawAttempt.History,reservation);
                    if(observedTables!=null){
                        int ac=Math.Max(4,Math.Max(observedTables.Attempts.Capacity,observedTables.Count+1));int pc=Math.Max(4,Math.Max(observedTables.Ids.Capacity,observedTables.Ids.Count+preparedAttempt.Providers.Count));
                        preparedTables=NavigationLiveArrivalTables.AllocatePrepared(ac,pc,reservation,observedTables);
                        using(EnterTransport()){if(!ReferenceEquals(liveArrivalTables,observedTables)||observedTables.Revision!=tableRevision||arrivalProviderSequence!=providerSequence)throw new InvalidOperationException("Arrival table source changed.");preparedTables.CopySourceObserved(observedTables);}
                        preparedProviderSequence=providerSequence;
                        for(int i=0;i<preparedAttempt.Providers.Count;i++){
                            var provider=preparedAttempt.Providers[i];if(preparedTables.References.ContainsKey(provider))throw new InvalidOperationException("Fresh physical input reused a live provider owner.");
                            var record=NavigationLiveArrivalProvider.CreateOwned(checked(++preparedProviderSequence),1,provider,reservation);try{preparedTables.AddProviderPreparing(record);}finally{record.ReleaseStorage();}
                        }
                        var created=preparedAttempt.Record;created.InputProviderId=preparedTables.References[created.Input.Provider].Id;for(int i=0;i<created.WorkingProviderIds.Length;i++)created.WorkingProviderIds[i]=preparedTables.References[created.Input.WorkingBindings[i].Provider].Id;
                        preparedTables.AddAttemptPreparing(preparedAttempt);preparedTables.SealPrepared();
                    }
                    next.SealPrepared();scope.Attach(next);inputScratch.Dispose();observationScratch.Dispose();
                    PublishPhysicalHostRoot(scope,source,next,()=>{
                        if(!ReferenceEquals(liveArrivalTables,observedTables)||observedTables!=null&&(observedTables.Revision!=tableRevision||arrivalProviderSequence!=providerSequence)||arrivalSequence+1!=id||attachment!=certifiedHostAttachmentIdentity||!ReferenceEquals(worker,certifiedHost)||commandGroup!=null||admissionBatchDepth!=0||hostAllocationThread!=0||
                            !ObservationCurrent())
                            throw new InvalidOperationException("Physical arrival input lost its authority observation.");
                        for(int n=0;n<rows.Length;n++)allocating[rows[n].Entity]=canonical[n];
                        var attempt=preparedAttempt.Record;
                        if(preparedTables!=null){liveArrivalTables=preparedTables;arrivalAttempts=preparedTables.Ordered;arrivalProviders=preparedTables.References;arrivalProviderIds=preparedTables.Ids;arrivalProviderSequence=preparedProviderSequence;}
                        else{OwnArrivalInput(attempt);arrivalAttempts.Add(id,attempt);}arrivalSequence=id;installedAttempt=true;
                    });
                    SignalHost();return true;
                }
            }catch(InvalidOperationException){if(installedAttempt)throw;return false;}
            finally{if(!installedAttempt)preparedTables?.ReleaseStorage();if(!installedAttempt||preparedTables!=null)preparedAttempt?.ReleaseStorage();if(installedAttempt&&preparedTables!=null)observedTables?.Release();observedTables?.Release();observation?.Release();work?.Release();input?.StorageRoot.Release();next?.ReleaseStorage();scope?.Discard();source?.Release();}
        }
        private static void UpdatePhysicalArrivalSnapshot(ArrivalAttempt attempt)
        {
            attempt.Frontier=ArrivalFrontier(attempt.Work);attempt.SnapshotStatus=attempt.Work.Status;
            attempt.SnapshotPhase=attempt.Work.WorkPhase;attempt.SnapshotCandidate=attempt.Work.CandidateCursor;
            attempt.SnapshotOccupancy=attempt.Work.OccupancyCursor;attempt.SnapshotMember=attempt.Work.MemberCursor;
        }
        internal bool AdvancePhysicalArrivalAuthority(int budget)
        {
            PreparePhysicalArrivalAttempt();
            ArrivalAttempt attempt;NavigationHostRoot source=null;ArrivalAdvance step;
            NavigationArrivalWork observedWork;NavigationHostArrivalHistory observedHistory;long observedEpoch;
            NavigationLiveArrivalAttempt observedAttemptOwner;
            using(EnterTransport()){
                attempt=arrivalAttempts.Values.FirstOrDefault(a=>a.Work.IsRegistered&&!a.Working&&!a.Retiring&&!a.Published&&
                    (a.SnapshotStatus==NavArrivalStatus.Pending||a.SnapshotStatus==NavArrivalStatus.BudgetExhausted));
                if(attempt==null)return false;
                observedWork=attempt.Work;observedHistory=attempt.History;observedEpoch=attempt.Epoch;
                observedAttemptOwner=attempt.StorageOwner;
                int allotment=checked(Math.Max(1,budget)*64);
                step=new ArrivalAdvance{Epoch=attempt.Epoch,Allocation=allotment,Graph=allotment,Field=allotment,Connectors=allotment,Bookkeeping=allotment,Maintenance=256,
                    // Idle-cache retirement can change this ordinary allowance
                    // before End refreshes its compatibility scalar. Read the
                    // current immutable root for v2 and for bytes-only restore.
                    External=64L+ArrivalRetainedBytes-attempt.Work.StructuralRetainedBytes+(TickQuantaEnabled?committedHostRoot.Scheduler.RetainedBytes:hostRetainedBytes)+installedCorridorBytes+installedGeometryBytes+answerCorridorBytes};
                // A live attempt observation is not an immutable worker pin.
                // Keep the exact sources alive even if a reentrant command or
                // authority destruction removes their Session edges.
                observedAttemptOwner?.Retain();observedWork.Retain();observedHistory.Retain();attempt.Working=true;observedAttemptOwner?.ChangedNoThrow();
            }
            NavigationHostRoot next=null;NavigationArrivalWork fork=null;NavigationHostArrivalHistory history=null;NavigationRetentionBudget.Scope scope=null;NavigationArrivalWork retiredWork=null;NavigationHostArrivalHistory retiredHistory=null;bool workPin=false,historyPin=false;
            NavOwnedText nextFrontier=null,nextPhase=null,retiredFrontier=null,retiredPhase=null;
            try{
                source=PinCommittedHostRoot();if(source==null)return false;
                if(!TryOpenPhysicalHostScope(NavigationPreparationKind.Arrival,attempt.Id,Admission.PublicationIdentity,out scope))return false;
                var reservation=scope.OwnedReservation();next=source.ForkOwned(reservation);fork=observedWork.ForkOwned(reservation);
                if(!fork.TryReserveAuthorityBytes(step.External))return false;
                fork.Advance(step.Epoch,step.Allocation,new NavEpochQuota(step.Graph,step.Field,step.Connectors,step.Bookkeeping,step.Maintenance));
                if(fork.LastAdvanceRetentionBlocked)return false;
                BeforeArrivalAdvanceCommit?.Invoke(fork.Status);fork.SealPrepared();history=observedHistory.AppendOwned(step,reservation);next.StageArrivalHistory(attempt.Id,history,reservation);next.StageArrivalWork(attempt.Id,fork,reservation);
                string frontier,phase;
                using(NavStorageScratch.Reserve(reservation,checked(4096L+512L*(fork.Proposals.Count+fork.MemberOutcomes.Count)))){frontier=ArrivalFrontier(fork);phase=fork.WorkPhase;}
                if(observedAttemptOwner!=null){nextFrontier=NavOwnedText.Copy(frontier,reservation);nextPhase=NavOwnedText.Copy(phase,reservation);frontier=nextFrontier.Value;phase=nextPhase.Value;}
                var status=fork.Status;int candidate=fork.CandidateCursor,occupancy=fork.OccupancyCursor,member=fork.MemberCursor;
                next.SetFrontier(source.Frontier,source.Epoch,checked(source.Progress+1));next.SealPrepared();scope.Attach(next);fork.Retain();workPin=true;history.Retain();historyPin=true;
                PublishPhysicalHostRoot(scope,source,next,()=>{
                    if(!arrivalAttempts.TryGetValue(attempt.Id,out var current)||!ReferenceEquals(current,attempt)||!ReferenceEquals(attempt.StorageOwner,observedAttemptOwner)||!attempt.Working||attempt.Epoch!=observedEpoch||
                        !ReferenceEquals(attempt.Work,observedWork)||!ReferenceEquals(attempt.History,observedHistory)||!ReferenceEquals(observedWork,source.ArrivalWork[attempt.Id])||!ReferenceEquals(observedHistory,source.ArrivalHistory[attempt.Id]))
                        throw new InvalidOperationException("Physical arrival advance lost its owner.");
                    retiredWork=attempt.OwnedWork;retiredHistory=attempt.OwnedHistory;attempt.OwnedWork=fork;attempt.OwnedHistory=history;workPin=historyPin=false;attempt.Work=fork;attempt.History=history;attempt.Epoch++;
                    if(observedAttemptOwner!=null){observedAttemptOwner.Work=fork;observedAttemptOwner.History=history;retiredFrontier=observedAttemptOwner.Frontier;retiredPhase=observedAttemptOwner.Phase;observedAttemptOwner.Frontier=nextFrontier;observedAttemptOwner.Phase=nextPhase;nextFrontier=nextPhase=null;}
                    attempt.Frontier=frontier;attempt.SnapshotPhase=phase;attempt.SnapshotStatus=status;attempt.SnapshotCandidate=candidate;attempt.SnapshotOccupancy=occupancy;attempt.SnapshotMember=member;
                    observedAttemptOwner?.ChangedNoThrow();
                });
                return true;
            }finally{nextFrontier?.Release();nextPhase?.Release();retiredFrontier?.Release();retiredPhase?.Release();if(workPin)fork?.Release();if(historyPin)history?.Release();retiredWork?.Release();retiredHistory?.Release();history?.Release();next?.ReleaseStorage();fork?.Release();scope?.Discard();source?.Release();try{using(EnterTransport()){attempt.Working=false;observedAttemptOwner?.ChangedNoThrow();PruneArrivalAttempts();}}finally{observedWork.Release();observedHistory.Release();observedAttemptOwner?.Release();}}
        }
    }
}
