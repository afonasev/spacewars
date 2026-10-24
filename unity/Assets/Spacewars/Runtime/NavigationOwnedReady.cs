using System;
using System.Collections.Generic;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        internal long LastOwnedReadyPublicationAllocatedBytes{get;private set;}=-1;
        internal int OwnedReadyPublications{get;private set;}
        internal Action<NavOwnedStorage> ObserveOwnedReadyBeforeRetirement;
        private readonly struct ReadyActorFact
        {
            internal readonly int Id,Team;internal readonly long Incarnation,Mobility;internal readonly NavPoint Position;internal readonly double Radius;internal readonly bool Held,Moving;
            internal ReadyActorFact(NavUnit actor){Id=actor.Id;Team=actor.Team;Incarnation=actor.Incarnation;Mobility=actor.MobilityRevision;Position=actor.Position;Radius=actor.Radius;Held=actor.Held;Moving=actor.Moving;}
            internal bool Matches(NavUnit actor)=>Id==actor.Id&&Team==actor.Team&&Incarnation==actor.Incarnation&&Mobility==actor.MobilityRevision&&Held==actor.Held&&Moving==actor.Moving&&Bits(Position.X,actor.Position.X)&&Bits(Position.Z,actor.Position.Z)&&Bits(Radius,actor.Radius);
            private static bool Bits(double a,double b)=>BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);
        }
        // One actual Ready. Unsupported compatibility branches are selected
        // before this carrier writes anything. All retired references leave the
        // transport gate in this carrier, including mailbox and transport edges.
        private sealed class NavigationPreparedReady : NavOwnedStorage,IDisposable
        {
            private static long Align(long bytes)=>checked((bytes+7)/8*8);
            internal static long Layout(int actors,int attempts,int leases)=>checked(NavStorageLayout.ObjectBytesBound(typeof(NavigationPreparedReady))+9*32L+Align(actors*NavStorageLayout.ValueBytes(typeof(ReadyActorFact)))+8L*actors+16L*attempts+Align(4L*attempts)+Align(attempts)+16L*leases+16L);
            internal readonly ReadyActorFact[] Actors;internal readonly NavOwnedStorage[] ActorPins,IntentPins;
            internal readonly ArrivalAttempt[] Attempts;internal readonly int[] AttemptRevisions;internal readonly bool[] PrunedAttempts;internal readonly ArrivalProviderLease[] Leases;
            internal readonly long[] LeaseRequests;internal readonly NavOwnedStorage[] MailboxRetired;
            internal NavigationCommandSlots<int,NavigationRequest> SourcePending,NextPending,SourceAllocating;
            internal NavigationCommandSlots<long,NavigationArrivalReservation> SourceLedger,NextLedger;
            internal NavigationCommandSlots<int,NavSolveStatus> SourceFailures,NextFailures;
            internal NavigationCommandSlots<int,NavPoint> SourceGoals;internal NavigationCommandSlots<int,string> SourceIdentities;
            internal NavigationAdmissionStorage Admission;internal NavigationPreparedGroupWrite GroupWrite;
            internal NavigationPreparedAnswerNames Names;internal NavigationHostRoot Host;internal NavigationInstalledExecutionOwner OldInstalled;internal NavOwnedStorage RetiredPending,RetiredLedger,RetiredFailures,RetiredTransport,RetiredTracking;
            internal int PendingRevision,LedgerRevision,AllocatingRevision,FailureRevision,GoalRevision,IdentityRevision;internal long CorridorBytes,GeometryBytes,SourceCorridorBytes,SourceGeometryBytes,SourceAnswerBytes,SourceHostBytes,PostAnswerBytes;internal bool Detached,AnswerTracked,HasHistoricalLedgerRows;
            internal NavigationLiveArrivalTables SourceArrival,NextArrival,RetiredArrival;internal int ArrivalRevision;internal long SourceRetirementSequence,NextRetirementSequence;
            private bool pendingDestination,ledgerDestination,failureDestination,arrivalDestination,released;
            private NavigationPreparedReady(int actors,int attempts,int leases):base(Layout(actors,attempts,leases)){Actors=new ReadyActorFact[actors];ActorPins=new NavOwnedStorage[actors];Attempts=new ArrivalAttempt[attempts];AttemptRevisions=new int[attempts];PrunedAttempts=new bool[attempts];IntentPins=new NavOwnedStorage[attempts];Leases=new ArrivalProviderLease[leases];LeaseRequests=new long[leases];MailboxRetired=new NavOwnedStorage[2];Retain();}
            internal static NavigationPreparedReady Create(int actors,int attempts,int leases,INavStorageReservation reservation){using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(Layout(actors,attempts,leases));return allocation.Register(new NavigationPreparedReady(actors,attempts,leases));}}
            internal void SealDestinations(){if(NextArrival!=null){NextArrival.Retain();arrivalDestination=true;}NextPending.Retain();pendingDestination=true;if(NextLedger!=null){NextLedger.Retain();ledgerDestination=true;}if(NextFailures!=null){NextFailures.Retain();failureDestination=true;}}
            internal void TransferDestinations(){pendingDestination=ledgerDestination=failureDestination=arrivalDestination=false;}
            public void Dispose(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>Dispose();
            protected override void ReleaseEdges(){SourceArrival?.Release();RetiredArrival?.Release();if(arrivalDestination)NextArrival?.Release();NextArrival?.ReleaseStorage();foreach(var pin in ActorPins)pin?.Release();foreach(var pin in IntentPins)pin?.Release();foreach(var retired in MailboxRetired)retired?.Release();SourcePending?.Release();SourceLedger?.Release();SourceAllocating?.Release();SourceFailures?.Release();SourceGoals?.Release();SourceIdentities?.Release();Names?.Release();Host?.Release();OldInstalled?.Release();if(pendingDestination)NextPending?.Release();if(ledgerDestination)NextLedger?.Release();if(failureDestination)NextFailures?.Release();NextPending?.ReleaseStorage();NextLedger?.ReleaseStorage();NextFailures?.ReleaseStorage();Admission?.Release();GroupWrite?.ReleaseStorage();RetiredPending?.Release();RetiredLedger?.Release();RetiredFailures?.Release();RetiredTransport?.Release();RetiredTracking?.Release();}
        }
        private static bool ReadyContainsRequest(IDictionary<long,NavigationArrivalReservation> ledger,NavigationCommandSlots<int,NavigationRequest> allocating,long request)
        {if(ledger.ContainsKey(request))return true;for(int i=0;i<allocating.Capacity;i++)if(allocating.TryReadSlot(i,out _,out var value)&&value.Request==request)return true;return false;}
        private bool ReadyPruneIsNoOp(NavigationPreparedReady value,IDictionary<long,NavigationArrivalReservation> ledger)
        {
            for(int i=0;i<value.Attempts.Length;i++){
                var attempt=value.Attempts[i];if(!arrivalAttempts.TryGetValue(attempt.Id,out var actual)||!ReferenceEquals(actual,attempt)||!ReferenceEquals(attempt.OwnedIntents,value.IntentPins[i]))return false;
                bool present=false;foreach(var intent in attempt.Intents)if(ReadyContainsRequest(ledger,value.SourceAllocating,intent.Request)){present=true;break;}if(!present)return false;
            }
            for(int i=0;i<value.Leases.Length;i++){var lease=value.Leases[i];if(!arrivalProjectionLeases.TryGetValue(lease.Token,out var actual)||!ReferenceEquals(actual,lease)||lease.Request!=value.LeaseRequests[i]||!lease.Retiring&&!ReadyContainsRequest(ledger,value.SourceAllocating,lease.Request))return false;}
            return arrivalAttempts.Count==value.Attempts.Length&&arrivalProjectionLeases.Count==value.Leases.Length;
        }
        private static string ReadyHeldIdentity(NavigationPreparedReady value,int entity)
        {
            int team=0;bool found=false;foreach(var actor in value.Actors)if(actor.Id==entity){team=actor.Team;found=true;break;}if(!found)return "";
            var rows=new List<string>();var culture=System.Globalization.CultureInfo.InvariantCulture;
            foreach(var actor in value.Actors)if(actor.Id!=entity&&actor.Held&&actor.Team==team)rows.Add(actor.Id+":"+actor.Incarnation+":"+actor.Mobility+":"+actor.Team+":"+actor.Position.X.ToString("R",culture)+":"+actor.Position.Z.ToString("R",culture)+":"+actor.Radius.ToString("R",culture));
            return string.Join("|",rows);
        }
        private static bool ReadyHeldMatches(NavigationPreparedReady value,int entity,string binding)
        {var raw=ReadyHeldIdentity(value,entity);return binding==raw||raw.Length>256&&binding==NavContract.Digest(w=>{w.Write("held-binding-v1");w.Write(raw);});}
        private bool ReadySourceCurrent(NavigationPreparedReady value,NavigationPreparedAnswerNames names,bool requireNoOpPrune=true)
        {
            if(!ReferenceEquals(liveArrivalTables,value.SourceArrival)||value.SourceArrival!=null&&(value.SourceArrival.Revision!=value.ArrivalRevision||arrivalRetirementSequence!=value.SourceRetirementSequence))return false;
            if(value.SourceArrival!=null)for(int i=0;i<value.Attempts.Length;i++)if(value.Attempts[i].StorageOwner==null||value.Attempts[i].StorageOwner.Revision!=value.AttemptRevisions[i])return false;
            if(!physicalArrivalEnabled||hostAllocationThread!=0||admissionBatchDepth!=0||commandGroup!=null||value.AnswerTracked!=IsCorridorAnswerTracked(names.Answers[0])||!ReferenceEquals(value.SourceIdentities,movementIdentities)||value.SourceIdentities.Revision!=value.IdentityRevision||value.SourceCorridorBytes!=installedCorridorBytes||value.SourceGeometryBytes!=installedGeometryBytes||value.SourceAnswerBytes!=answerCorridorBytes||value.SourceHostBytes!=hostRetainedBytes||!AnswerNamesSourceCurrent(names)||!ReferenceEquals(value.Host,committedHostRoot)||!ReferenceEquals(value.SourcePending,pending)||value.SourcePending.Revision!=value.PendingRevision||!ReferenceEquals(value.SourceLedger,arrivalLedger)||value.SourceLedger.Revision!=value.LedgerRevision||!ReferenceEquals(value.SourceAllocating,allocating)||value.SourceAllocating.Revision!=value.AllocatingRevision||!ReferenceEquals(value.SourceFailures,technicalFailures)||value.SourceFailures.Revision!=value.FailureRevision||!ReferenceEquals(value.SourceGoals,retainedGoals)||value.SourceGoals.Revision!=value.GoalRevision||Crowd.Units.Count!=value.Actors.Length)return false;
            for(int i=0;i<value.Actors.Length;i++)if(!Crowd.TryGet(value.Actors[i].Id,out var actor)||!ReferenceEquals(actor.StorageOwner,value.ActorPins[i])||!value.Actors[i].Matches(actor))return false;
            var request=names.Answers[0].Request;return ReferenceEquals(installedExecutions.OwnerFor(request.Entity),value.OldInstalled)&&transportOwner.CanConsumePrepared(request)&&(!requireNoOpPrune||value.NextArrival!=null||ReadyPruneIsNoOp(value,value.NextLedger??value.SourceLedger));
        }
        // null: compatibility branch, false: preparation refused, true: published.
        private bool? TryPublishOwnedReady(NavigationPreparedAnswerNames names,double stepDt=0,NavMotionWorkspace motionWorkspace=null)
        {
            if(names==null||names.Answers.Length!=1||!names.HasQualification[0]||!names.Qualified[0]||names.Writes[0]==null)return null;
            var answer=names.Answers[0];var request=answer.Request;NavigationPreparedReady candidate=null;NavigationRetentionBudget.Scope scope=null;
            int actors,attempts,leases;long publication;bool published=false;
            using(EnterTransport()){
                if(!AnswerNamesSourceCurrent(names))return false;
                if(!physicalArrivalEnabled||hostAllocationThread!=0||admissionBatchDepth!=0||commandGroup!=null||!(pending is NavigationCommandSlots<int,NavigationRequest>)||!(allocating is NavigationCommandSlots<int,NavigationRequest>)||!(arrivalLedger is NavigationCommandSlots<long,NavigationArrivalReservation>)||!(technicalFailures is NavigationCommandSlots<int,NavSolveStatus>)||!(retainedGoals is NavigationCommandSlots<int,NavPoint>)||!(movementIdentities is NavigationCommandSlots<int,string>))return null;
                if(request.Session!=Generation||request.ProfileId!=profile.ProfileId||request.ProfileRevision!=profile.Revision||!(ReferenceEquals(request.BaseGeometry,navigationGeometry)||IsCurrentPhysicalAuthorityGeometry(request))||!CurrentMember(request)||!pending.TryGetValue(request.Entity,out var expected)||!ReferenceEquals(expected,request)||!transportOwner.CanConsumePrepared(request))return null;
                actors=Crowd.Units.Count;attempts=arrivalAttempts.Count;leases=arrivalProjectionLeases.Count;publication=admissionPublicationIdentity;
            }
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out scope))return false;
            using(scope)try{
                candidate=NavigationPreparedReady.Create(actors,attempts,leases,scope.OwnedReservation());candidate.Names=NavPhysicalMacro.Acquire(candidate,names);
                using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(8192L+4096L*actors+1024L*attempts+512L*leases))){
                    using(EnterTransport()){
                        if(!AnswerNamesSourceCurrent(names)||actors!=Crowd.Units.Count||attempts!=arrivalAttempts.Count||leases!=arrivalProjectionLeases.Count)return false;
                        candidate.SourcePending=NavPhysicalMacro.Acquire(candidate,(NavigationCommandSlots<int,NavigationRequest>)pending);candidate.PendingRevision=candidate.SourcePending.Revision;
                        candidate.SourceAllocating=NavPhysicalMacro.Acquire(candidate,(NavigationCommandSlots<int,NavigationRequest>)allocating);candidate.AllocatingRevision=candidate.SourceAllocating.Revision;
                        candidate.SourceLedger=NavPhysicalMacro.Acquire(candidate,(NavigationCommandSlots<long,NavigationArrivalReservation>)arrivalLedger);candidate.LedgerRevision=candidate.SourceLedger.Revision;
                        candidate.SourceFailures=NavPhysicalMacro.Acquire(candidate,(NavigationCommandSlots<int,NavSolveStatus>)technicalFailures);candidate.FailureRevision=candidate.SourceFailures.Revision;
                        candidate.SourceIdentities=NavPhysicalMacro.Acquire(candidate,(NavigationCommandSlots<int,string>)movementIdentities);candidate.IdentityRevision=candidate.SourceIdentities.Revision;
                        candidate.AnswerTracked=IsCorridorAnswerTracked(answer);candidate.PostAnswerBytes=answerCorridorBytes-(candidate.AnswerTracked?answer.Corridor.EstimatedRetainedBytes:0);
                        candidate.SourceGoals=NavPhysicalMacro.Acquire(candidate,(NavigationCommandSlots<int,NavPoint>)retainedGoals);candidate.GoalRevision=candidate.SourceGoals.Revision;
                        candidate.SourceCorridorBytes=installedCorridorBytes;candidate.SourceGeometryBytes=installedGeometryBytes;candidate.SourceAnswerBytes=answerCorridorBytes;candidate.SourceHostBytes=hostRetainedBytes;candidate.Detached=answer.Corridor.Group==0||!memberGroups.TryGetValue(request.Entity,out var currentGroup)||currentGroup!=answer.Corridor.Group;
                        candidate.Host=NavPhysicalMacro.Acquire(candidate,committedHostRoot);candidate.OldInstalled=NavPhysicalMacro.Acquire(candidate,installedExecutions.OwnerFor(request.Entity));
                        for(int i=0;i<actors;i++){var actor=Crowd.Units[i];candidate.Actors[i]=new ReadyActorFact(actor);candidate.ActorPins[i]=NavPhysicalMacro.Acquire(candidate,actor.StorageOwner);}
                        candidate.SourceArrival=NavPhysicalMacro.Acquire(candidate,liveArrivalTables);candidate.ArrivalRevision=liveArrivalTables?.Revision??0;candidate.SourceRetirementSequence=arrivalRetirementSequence;candidate.NextRetirementSequence=arrivalRetirementSequence;
                        int index=0;foreach(var pair in arrivalAttempts){candidate.AttemptRevisions[index]=pair.Value.StorageOwner?.Revision??0;candidate.Attempts[index]=pair.Value;if(pair.Value.OwnedIntents==null)return null;candidate.IntentPins[index++]=NavPhysicalMacro.Acquire(candidate,pair.Value.OwnedIntents);}
                        index=0;foreach(var pair in arrivalProjectionLeases){candidate.Leases[index]=pair.Value;candidate.LeaseRequests[index++]=pair.Value.Request;}
                    }
                    if(!ReadyHeldMatches(candidate,request.Entity,request.HeldIdentity))return null;
                    using(EnterTransport()){
                        if(!ReadySourceCurrent(candidate,names,false))return false;
                        if(!ReadyPruneIsNoOp(candidate,candidate.SourceLedger))return null;
                        // Read mutable authority only under this preparation
                        // read gate; its scratch was admitted before entry.
                        for(int i=0;i<candidate.SourceGoals.Capacity;i++)if(candidate.SourceGoals.TryReadSlot(i,out int entity,out _)&&!candidate.SourceAllocating.ContainsKey(entity)&&candidate.SourceIdentities.TryGetValue(entity,out var hold)&&!ReadyHeldMatches(candidate,entity,hold))return null;
                        for(int i=0;i<candidate.SourceLedger.Capacity;i++)if(candidate.SourceLedger.TryReadSlot(i,out _,out var row)&&row.Entity==request.Entity&&row.State==NavArrivalReservationKind.InstalledExecution)candidate.HasHistoricalLedgerRows=true;
                        long oldBytes=candidate.OldInstalled==null?0:candidate.OldInstalled.Corridor.Corridor.EstimatedRetainedBytes+16L*candidate.OldInstalled.Route.Count;
                        candidate.CorridorBytes=checked(installedCorridorBytes-oldBytes+answer.Corridor.EstimatedRetainedBytes+16L*(answer.RouteLength+1));
                        candidate.GeometryBytes=ProspectiveGeometryBytes(request.Entity,request);
                        if(hostRetainedBytes+candidate.PostAnswerBytes+candidate.CorridorBytes+candidate.GeometryBytes>LayeredNavigationScheduler.MaxRetainedBytes)return null;
                    }
                }
                if(!PrepareAnswerInstallDestination(names,0,scope))return null;
                candidate.NextPending=CopyCommandMap(candidate.SourcePending,0,scope,names.Operation,r=>r.StorageOwner,candidate.SourcePending.Capacity);candidate.RequireEdge(candidate.NextPending);candidate.NextPending.Remove(request.Entity);candidate.NextPending.Seal();
                bool hasReservation=candidate.SourceLedger.TryGetValue(request.Request,out var reservation);
                if(hasReservation||candidate.HasHistoricalLedgerRows){
                    if(hasReservation&&!(candidate.SourceLedger.OwnerFor(request.Request)is NavigationLiveArrivalReservation))return null;
                    candidate.NextLedger=CopyCommandMap(candidate.SourceLedger,0,scope,names.Operation,null,candidate.SourceLedger.Capacity);candidate.RequireEdge(candidate.NextLedger);
                    // InstallArrival removes prior installed rows in authority
                    // slot order. Preserve that exact free-slot LIFO history.
                    for(int i=0;i<candidate.SourceLedger.Capacity;i++)if(candidate.SourceLedger.TryReadSlot(i,out long key,out var old)&&old.Entity==request.Entity&&old.State==NavArrivalReservationKind.InstalledExecution)candidate.NextLedger.Remove(key);
                    if(hasReservation){var rowOwner=(NavigationLiveArrivalReservation)candidate.SourceLedger.OwnerFor(request.Request);var installedRow=NavigationLiveArrivalReservation.CopyWithStateOwned(rowOwner,NavArrivalReservationKind.InstalledExecution,scope.OwnedReservation());try{candidate.NextLedger.SetPreparing(request.Request,installedRow.Entry,installedRow);}finally{installedRow.ReleaseStorage();}}
                    candidate.NextLedger.Seal();
                    using(EnterTransport()){
                        if(!ReadySourceCurrent(candidate,names,false))return false;
                        if(!ReadyPruneIsNoOp(candidate,candidate.NextLedger)){
                            if(candidate.SourceArrival==null)return null;
                            for(int i=0;i<candidate.Leases.Length;i++)if(!candidate.Leases[i].Retiring&&!ReadyContainsRequest(candidate.NextLedger,candidate.SourceAllocating,candidate.Leases[i].Request))return null;
                            for(int i=0;i<candidate.Attempts.Length;i++){
                                var old=candidate.Attempts[i];bool present=false;foreach(var intent in old.Intents)if(ReadyContainsRequest(candidate.NextLedger,candidate.SourceAllocating,intent.Request)){present=true;break;}
                                if(present)continue;if(old.Working||old.Reconstructing||old.RestoredRetiringCompute||old.StorageOwner==null)return null;
                                for(int n=0;n<candidate.Leases.Length;n++)if(candidate.Leases[n].Attempt==old.Id)return null;
                                candidate.PrunedAttempts[i]=true;if(!old.Retiring)candidate.NextRetirementSequence=checked(candidate.NextRetirementSequence+1);
                            }
                        }
                    }
                }
                bool retiresAttempt=false;for(int i=0;i<candidate.PrunedAttempts.Length;i++)retiresAttempt|=candidate.PrunedAttempts[i];
                if(retiresAttempt){
                    var source=candidate.SourceArrival;candidate.NextArrival=NavigationLiveArrivalTables.AllocatePrepared(source.Attempts.Capacity,source.References.Capacity,scope.OwnedReservation(),source,source.Ids.Capacity);
                    using(EnterTransport()){if(!ReadySourceCurrent(candidate,names,false))return false;candidate.NextArrival.CopySourceObserved(source);}
                    for(int i=0;i<candidate.PrunedAttempts.Length;i++)if(candidate.PrunedAttempts[i]){var old=candidate.Attempts[i];foreach(var provider in old.OwnedProviders)candidate.NextArrival.DecrementProviderPreparing(provider,scope.OwnedReservation());candidate.NextArrival.RemoveAttemptPreparing(old);}
                    if(candidate.NextArrival.Ids.Count==0)candidate.NextArrival.ResetEmptyProvidersPreparing();candidate.NextArrival.SealPrepared();
                }
                if(candidate.SourceFailures.ContainsKey(request.Entity)){candidate.NextFailures=CopyCommandMap(candidate.SourceFailures,0,scope,names.Operation,null,candidate.SourceFailures.Capacity);candidate.RequireEdge(candidate.NextFailures);candidate.NextFailures.Remove(request.Entity);candidate.NextFailures.Seal();}
                NavigationAuthorityGroup version=null;NavigationAuthorityMember member=null;
                try{
                    var observation=names.GroupObservations[0];if(observation.Owner!=null){
                        GroupOrderState metadata;GroupMemberState changed;int index=-1;
                        using(NavStorageScratch.Reserve(scope.OwnedReservation(),NavStorageLayout.ObjectBytesBound(typeof(GroupOrderState))+NavStorageLayout.ObjectBytesBound(typeof(GroupMemberState)))){
                            using(EnterTransport()){if(!ReadySourceCurrent(candidate,names))return false;metadata=groups[answer.Corridor.Group].CopyHeader();var members=groups[answer.Corridor.Group].Members;for(int i=0;i<members.Length;i++)if(members[i].Entity==request.Entity){index=i;break;}if(index<0)return false;changed=members[index].Copy();}
                            if(metadata.MarchVersion==1){changed.Progress=0;changed.ProgressRequest=0;metadata.MarchAnchor=0;metadata.MarchInitialized=false;}
                            changed.Phase=changed.FormationReleased?GroupMemberPhase.Combat:GroupMemberPhase.March;
                            member=NavigationAuthorityMember.CopyWithValuesOwned((NavigationAuthorityMember)observation.Version.Members[index],new NavigationAuthorityMember.Values(changed),scope.OwnedReservation());
                            version=NavigationAuthorityGroup.CopyWithMemberAndMarchOwned(observation.Version,index,member,new NavigationAuthorityGroup.Values(metadata),scope.OwnedReservation());
                        }
                        candidate.GroupWrite=NavigationPreparedGroupWrite.CreateOwned(observation,version,index,scope);candidate.RequireEdge(candidate.GroupWrite);
                    }
                    candidate.Admission=PrepareAdmissionProjection(groups,candidate.NextPending,requestSequence,checked(publication+1),names.Operation,scope,names.ObservedAdmission,version);candidate.RequireEdge(candidate.Admission);
                }finally{version?.ReleaseStorage();member?.ReleaseStorage();}
                candidate.SealDestinations();scope.Attach(candidate);
                Action<object> publishReady=_=>{using(var mailbox=Answers.EnterPreparedWrite()){
                    if(!ReadySourceCurrent(candidate,names)||scope.IsCancelled||authorityOperationIdentity==long.MaxValue||AppliedResults==int.MaxValue||OwnedReadyPublications==int.MaxValue)throw new InvalidOperationException("Prepared Ready source changed.");
                    names.Writes[0].Validate(groupCommandBacking.CrowdNames);candidate.GroupWrite?.ValidateInstall(scope,ObserveLiveWriteToken());
                    if(names.BarrierCount==0)mailbox.ValidateTakeOne(names.MailboxRevision,names.SourceAnswerCount,candidate.MailboxRetired);
                    // Validated assignment block: no factory, callback, Release,
                    // collection growth, qualifier or compatibility publisher.
                    long managedBefore=GC.GetAllocatedBytesForCurrentThread();
                    if(candidate.NextArrival!=null){candidate.RetiredArrival=liveArrivalTables;liveArrivalTables=candidate.NextArrival;arrivalAttempts=candidate.NextArrival.Ordered;arrivalProviders=candidate.NextArrival.References;arrivalProviderIds=candidate.NextArrival.Ids;arrivalRetirementSequence=candidate.NextRetirementSequence;}
                    candidate.RetiredPending=groupCommandBacking.Pending;groupCommandBacking.Pending=candidate.NextPending;pending=candidate.NextPending;
                    if(candidate.NextLedger!=null){candidate.RetiredLedger=groupCommandBacking.Ledger;groupCommandBacking.Ledger=candidate.NextLedger;arrivalLedger=candidate.NextLedger;}
                    if(candidate.NextFailures!=null){candidate.RetiredFailures=groupCommandBacking.Failures;groupCommandBacking.Failures=candidate.NextFailures;technicalFailures=candidate.NextFailures;}
                    candidate.GroupWrite?.ApplyNoThrow();names.Routes[0].ApplyNoThrow();names.Writes[0].ApplyNoThrow(true);
                    var actor=names.Routes[0].Actor.Unit;actor.StorageOwner.SetInstalledNoThrow(names.Installed[0],candidate.Detached,null,out names.RetiredInstalled[0]);names.Installed[0]=null;
                    actor.FailedRequest=actor.FailedOrder=actor.FailedGroupId=actor.FailedRevision=0;actor.FailedGoal=null;actor.LastFailure=NavigationOutcome.Idle;actor.InstalledGroupId=request.MemberIdentity?.GroupId??0;actor.InstalledRevision=request.MemberIdentity?.OrderRevision??0;actor.InstalledRequest=request.Request;actor.InstalledOrder=request.Order;
                    installedCorridorBytes=candidate.CorridorBytes;installedGeometryBytes=candidate.GeometryBytes;candidate.RetiredTracking=ExtractAnswerChargeNoThrow(answer);answerCorridorBytes=candidate.PostAnswerBytes;AppliedResults++;LastCorridorFailure=null;
                    if(names.BarrierCount==0)mailbox.TakeOneNoThrow(candidate.MailboxRetired);else candidate.MailboxRetired[0]=TakeBarrierAnswerNoThrow(names.BarrierIndex);
                    candidate.RetiredTransport=transportOwner.ConsumePreparedNoThrow(request);if(PublishAdmissionOwnerNoThrow(candidate.Admission))candidate.Admission=null;authorityOperationIdentity++;candidate.TransferDestinations();OwnedReadyPublications++;published=true;LastOwnedReadyPublicationAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-managedBefore;
                }};
                try{using(EnterTransport()){
                if(stepDt>0)ValidateCompatibilityMotion(motionWorkspace);
                scope.Publish(candidate,publishReady);
                if(stepDt>0)AdmissionBatch(()=>StepMovementLocked(stepDt,motionWorkspace));
                }}finally{if(published){
                    // Publication is terminal before movement starts. Even a
                    // movement exception resolves the committed scope and emits
                    // its Ready notification after the outer gate is released.
                    scope.ResolveCommitted();
                    try{RouteActivated?.Invoke(request);ObserveOwnedReadyBeforeRetirement?.Invoke(candidate);}
                    finally{candidate.GroupWrite?.ReleaseRetired();SignalHost();}
                }}return true;
            }catch(InvalidOperationException){if(published)throw;return false;}finally{candidate?.Dispose();}
        }
    }
}
