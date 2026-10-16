using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Mutable live slots are not BODY nodes. Capture emits immutable name roles;
    // hot and cold live slots use this same admitted concrete array factory.
    internal sealed class NavigationCrowdNameBacking : NavOwnedStorage
    {
        private static long Layout(int count)=>checked(NavStorageLayout.ObjectBytesBound(typeof(NavigationCrowdNameBacking))+64L+count*(4L+3L*NavStorageLayout.ValueBytes(typeof(NavNameReference))));
        internal readonly int[] Keys;internal readonly NavNameReference[] Names;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationCrowdNameBacking(int count):base(Layout(count)){Keys=new int[count];Names=new NavNameReference[count*3];Retain();}
        internal static NavigationCrowdNameBacking CopyOwned(int[] keys,NavNameReference[] names,INavStorageReservation reservation)
        {
            if(keys==null||names==null||names.Length!=keys.Length*3)throw new ArgumentException("Invalid crowd name backing shape.");
            foreach(var name in names)if(name.Owner!=null)NavStorageAdmission.RequireOwned(reservation,name.Owner);
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(Layout(keys.Length));var result=allocation.Register(new NavigationCrowdNameBacking(keys.Length));
                try{for(int i=0;i<keys.Length;i++){if(i>0&&keys[i-1]>=keys[i])throw new ArgumentException("Crowd name keys must be ordered and unique.");result.Keys[i]=keys[i];}
                    for(int i=0;i<names.Length;i++){names[i].Owner?.Retain();result.Names[i]=names[i];}return result;
                }catch{result.Release();throw;}
            }
        }
        internal int Find(int entity)=>Array.BinarySearch(Keys,entity);
        internal void ClearFailedPreparing(int entity){int index=Find(entity);if(index<0)return;var old=Names[index*3+2];Names[index*3+2]=default;old.Owner?.Release();}
        protected override void ReleaseEdges(){foreach(var name in Names)name.Owner?.Release();}
    }
    // All destination references are acquired before transport publication. Only
    // exact slot assignments happen in ApplyNoThrow; retirement follows the gate.
    internal sealed class NavigationPreparedCrowdNameWrite : NavOwnedStorage
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationPreparedCrowdNameWrite));
        internal NavigationCrowdNameBacking Backing;internal NavigationHostRequest RequestOwner;
        private NavNameReference expectedGoal,expectedFailure,success,failure,retiredGoal,retiredFailure;
        private int slot;private bool applied;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationPreparedCrowdNameWrite():base(Layout){Retain();}
        internal static NavigationPreparedCrowdNameWrite CreateOwned(NavigationCrowdNameBacking backing,int index,NavigationRequest request,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,backing);NavStorageAdmission.RequireOwned(reservation,request.StorageOwner);
            var next=request.GoalLocation.HasValue?request.GoalLocation.Value.NameReference:default;
            if(next.Owner!=null)NavStorageAdmission.RequireOwned(reservation,next.Owner);
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(Layout);var value=allocation.Register(new NavigationPreparedCrowdNameWrite());
                try{value.Backing=NavPhysicalMacro.Acquire(value,backing);value.RequestOwner=NavPhysicalMacro.Acquire(value,request.StorageOwner);value.slot=index*3;
                    value.expectedGoal=backing.Names[value.slot+1];value.expectedFailure=backing.Names[value.slot+2];
                    next.Owner?.Retain();value.success=next;next.Owner?.Retain();value.failure=next;return value;
                }catch{value.Release();throw;}
            }
        }
        private static bool Same(NavNameReference a,NavNameReference b)=>ReferenceEquals(a.Owner,b.Owner)&&a.Role==b.Role&&a.Index==b.Index;
        internal void Validate(NavigationCrowdNameBacking backing){if(applied||!ReferenceEquals(Backing,backing)||!Same(expectedGoal,backing.Names[slot+1])||!Same(expectedFailure,backing.Names[slot+2]))throw new InvalidOperationException("Prepared actor name source changed.");}
        internal void ApplyNoThrow(bool routeInstalled)
        {
            if(routeInstalled){retiredGoal=Backing.Names[slot+1];retiredFailure=Backing.Names[slot+2];Backing.Names[slot+1]=success;success=default;Backing.Names[slot+2]=default;}
            else{retiredFailure=Backing.Names[slot+2];Backing.Names[slot+2]=failure;failure=default;}applied=true;
        }
        internal void ApplyClearFailureNoThrow(){retiredFailure=Backing.Names[slot+2];Backing.Names[slot+2]=default;applied=true;}
        protected override void ReleaseEdges(){Backing?.Release();RequestOwner?.Release();success.Owner?.Release();failure.Owner?.Release();retiredGoal.Owner?.Release();retiredFailure.Owner?.Release();}
    }
    internal sealed class NavigationPreparedAnswerNames : NavOwnedStorage,IDisposable
    {
        private static long Layout(int count,int barrier)=>checked(NavStorageLayout.ObjectBytesBound(typeof(NavigationPreparedAnswerNames))+352L+8L*barrier+56L*count+2L*((count+7L)/8L*8L)+NavStorageLayout.ValueBytes(typeof(NavigationLiveGroupObservation))*count);
        internal bool LegacyProbeDestination,CapacityObserved,SingleCapacityExceeded;internal long CapacityHostBytes,CapacityInstalledBytes,CapacityGeometryBytes;
        internal readonly NavigationInstalledExecutionOwner[] Installed;
        internal readonly NavOwnedStorage[] RetiredInstalled;
        internal readonly NavOwnedStorage[] AnswerOwners;
        internal readonly NavPreparedUnitRoute[] Routes;
        internal readonly NavigationAnswer[] Answers,BarrierSource;internal readonly NavigationPreparedCrowdNameWrite[] Writes;
        internal readonly bool[] HasQualification,Qualified;internal readonly string[] QualificationFailures;
        internal readonly NavigationLiveGroupObservation[] GroupObservations;
        internal NavigationBarrierReceipt BarrierRoot;internal long BarrierRevision;
        internal NavigationAdmissionStorage ObservedAdmission;
        internal NavigationCommandSlots<NavigationAnswer,NavigationHostAnswer> Tracking;internal int TrackingRevision;internal long TrackingBytes;private bool released;
        internal long Operation,MailboxRevision;internal int BarrierCount,BarrierIndex,SourceAnswerCount;internal bool Single;internal NavigationCommandAuthority Backing;
        private NavigationPreparedAnswerNames(int count,int barrier):base(Layout(count,barrier)){BarrierSource=new NavigationAnswer[barrier];Installed=new NavigationInstalledExecutionOwner[count];RetiredInstalled=new NavOwnedStorage[count];AnswerOwners=new NavOwnedStorage[count];Routes=new NavPreparedUnitRoute[count];Answers=new NavigationAnswer[count];Writes=new NavigationPreparedCrowdNameWrite[count];HasQualification=new bool[count];Qualified=new bool[count];QualificationFailures=new string[count];GroupObservations=new NavigationLiveGroupObservation[count];Retain();}
        internal static NavigationPreparedAnswerNames CreateOwned(int count,int barrier,INavStorageReservation reservation){using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(Layout(count,barrier));return allocation.Register(new NavigationPreparedAnswerNames(count,barrier));}}
        internal NavigationBarrierReceipt TakeLegacyProbeDestination(NavigationAnswer answer){int index=Array.IndexOf(Answers,answer);var receipt=index<0?null:AnswerOwners[index] as NavigationBarrierReceipt;if(receipt!=null)AnswerOwners[index]=null;return receipt;}
        internal bool HasLegacyProbeDestination(NavigationAnswer answer){int index=Array.IndexOf(Answers,answer);return index>=0&&AnswerOwners[index] is NavigationBarrierReceipt;}
        internal NavigationPreparedCrowdNameWrite For(NavigationAnswer answer){for(int i=0;i<Answers.Length;i++)if(ReferenceEquals(Answers[i],answer))return Writes[i];return null;}
        internal NavPreparedUnitRoute RouteFor(NavigationAnswer answer){for(int i=0;i<Answers.Length;i++)if(ReferenceEquals(Answers[i],answer))return Routes[i];return null;}
        internal NavigationInstalledExecutionOwner TakeInstalled(NavigationAnswer answer,out int index){index=Array.IndexOf(Answers,answer);if(index<0)return null;var value=Installed[index];Installed[index]=null;return value;}
        internal bool TryQualified(NavigationAnswer answer,out bool valid,out string reason){int i=Array.IndexOf(Answers,answer);valid=i>=0&&Qualified[i];reason=i>=0?QualificationFailures[i]:null;return i>=0&&HasQualification[i];}
        public void Dispose(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>Dispose();
        protected override void ReleaseEdges(){BarrierRoot?.Release();Tracking?.Release();ObservedAdmission?.Release();foreach(var observation in GroupObservations)observation.ReleasePins();foreach(var value in Installed)value?.Release();foreach(var value in RetiredInstalled)value?.Release();Backing?.Release();foreach(var write in Writes)write?.Release();foreach(var route in Routes)route?.Dispose();foreach(var answer in AnswerOwners)answer?.Release();}
    }
    public sealed partial class NavigationSession
    {
        private NavRetainedArray<NavNameReference> terrainNameOwners;
        private void InitializeTerrainNames(NavRetainedArray<NavNameReference> shared=null)
        {
            if(shared!=null){if(!shared.BelongsTo(physicalHostBudget))throw new ArgumentException("Foreign terrain name pool.");shared.Retain();terrainNameOwners=shared;return;}
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,0,out var scope))throw new InvalidOperationException("Terrain name admission refused.");
            using(scope){int count=terrain?.Supports.Count??0;NavRetainedArray<NavNameReference> pool=null;
                using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(32L+count*NavStorageLayout.ValueBytes(typeof(NavNameReference))))){
                    var names=new NavNameReference[count];try{
                        for(int i=0;i<count;i++){var text=NavOwnedText.Copy(terrain.Supports[i].SurfaceId,scope.OwnedReservation());names[i]=new NavNameReference(text,NavNameRole.Text);}
                        pool=NavRetainedArray<NavNameReference>.Copy(names,scope.OwnedReservation().TryReserve);
                    }finally{foreach(var name in names)name.Owner?.Release();}
                }
                try{scope.Attach(pool);scope.Commit(pool,_=>terrainNameOwners=pool);}catch{pool?.Release();throw;}
            }
        }
        private void AdoptTerrainNames(NavigationAuthorityOwners source)
        {
            if(source?.HasCatalog!=true)return;var pool=source.TerrainNames;
            if(pool==null||pool.Count!=(terrain?.Supports.Count??0))throw new ArgumentException("Missing terrain name BODY roles.");
            for(int i=0;i<pool.Count;i++)if(pool[i].Resolve()!=terrain.Supports[i].SurfaceId)throw new ArgumentException("Terrain name BODY index/value mismatch.");
            if(!ReferenceEquals(pool,terrainNameOwners)){pool.Retain();var old=terrainNameOwners;terrainNameOwners=pool;Crowd.BindSurfaceNamesNoThrow(pool);old?.Release();}
        }
        // Answer/name preparation never drains transport or writes actor state.
        // A denied allocation keeps the original FIFO and all live bytes intact.
        private bool TryPrepareAnswerNames(out NavigationPreparedAnswerNames prepared)=>TryPrepareAnswerNamesBatch(out prepared,false);
        private bool TryPrepareAnswerNamesBatch(out NavigationPreparedAnswerNames prepared,bool single,bool stepEmpty=false)
        {
            prepared=null;int count,barrierCount,mailboxCount;long operation,revision;NavigationCommandAuthority backing;NavigationAdmissionStorage observedAdmission;
            using(EnterTransport()){
                barrierCount=barrierAnswers.Count;mailboxCount=Answers.Count;count=barrierCount+mailboxCount;if(count==0&&!stepEmpty)return true;if(single)count=1;
                operation=authorityOperationIdentity;revision=Answers.Revision;backing=groupCommandBacking;backing?.Retain();observedAdmission=admissionOwner;observedAdmission?.Retain();
            }
            try{
                if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,admissionPublicationIdentity,out var scope))return false;
                using(scope){NavigationPreparedAnswerNames candidate=null;
                    try{
                        candidate=NavigationPreparedAnswerNames.CreateOwned(count,barrierCount,scope.OwnedReservation());candidate.Operation=operation;candidate.MailboxRevision=revision;candidate.BarrierCount=barrierCount;candidate.Single=single;candidate.SourceAnswerCount=mailboxCount;
                        if(backing!=null)candidate.Backing=NavPhysicalMacro.Acquire(candidate,backing);
                        if(observedAdmission!=null)candidate.ObservedAdmission=NavPhysicalMacro.Acquire(candidate,observedAdmission);
                        using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(32L+8L*(single?0:mailboxCount)))){
                            using(EnterTransport()){
                                if(operation!=authorityOperationIdentity||!ReferenceEquals(backing,groupCommandBacking)||!ReferenceEquals(observedAdmission,admissionOwner)||barrierAnswers.Count!=barrierCount||Answers.Revision!=revision)return false;
                                candidate.LegacyProbeDestination=liveProbeAnswers==null;candidate.BarrierRoot=NavPhysicalMacro.Acquire(candidate,barrierPayloads);candidate.BarrierRevision=barrierPayloadRevision;candidate.Tracking=NavPhysicalMacro.Acquire(candidate,ownedAnswerTracking);candidate.TrackingRevision=ownedAnswerTracking?.Revision??0;candidate.TrackingBytes=answerCorridorBytes;
                                for(int i=0;i<barrierCount;i++)candidate.BarrierSource[i]=barrierAnswers[i];
                                if(single){
                                    if(barrierCount>0){int first=0;for(int i=1;i<barrierCount;i++)if(barrierAnswers[i].Request.Request<barrierAnswers[first].Request.Request)first=i;candidate.BarrierIndex=first;candidate.Answers[0]=barrierAnswers[first];}
                                    else if(!Answers.TryPeek(out candidate.Answers[0]))return false;
                                }else{
                                    for(int i=0;i<barrierCount;i++)candidate.Answers[i]=barrierAnswers[i];
                                    // Sort only the frozen barrier prefix before parallel
                                    // route/name arrays are prepared; mailbox order is FIFO.
                                    for(int i=1;i<barrierCount;i++){var value=candidate.Answers[i];int j=i;while(j>0&&candidate.Answers[j-1].Request.Request>value.Request.Request){candidate.Answers[j]=candidate.Answers[j-1];j--;}candidate.Answers[j]=value;}
                                    var queued=Answers.CopyItemsAtRevision(revision);for(int i=0;i<queued.Length;i++)candidate.Answers[barrierCount+i]=queued[i];
                                }
                                for(int i=0;i<count;i++){var owner=candidate.Answers[i].StorageOwner;if(owner!=null){NavStorageAdmission.RequireOwned(scope.OwnedReservation(),owner);owner.Retain();candidate.AnswerOwners[i]=owner;}}
                            }
                        }
                        for(int i=0;i<count;i++){
                            var answer=candidate.Answers[i];var request=answer.Request;int index=backing?.CrowdNames?.Find(request.Entity)??-1;
                            if(request.StorageOwner==null)continue;
                            if(request.Entity<0){
                                if(candidate.LegacyProbeDestination&&candidate.AnswerOwners[i] is NavigationHostAnswer payload){var receipt=NavigationBarrierReceipt.CreateOwned(payload,scope.OwnedReservation());candidate.AnswerOwners[i].Release();candidate.AnswerOwners[i]=receipt;}
                                if(answer.Status==NavSolveStatus.Ready)PrepareOwnedCorridorQualification(candidate,i,scope);continue;
                            }
                            if(index>=0)candidate.Writes[i]=NavigationPreparedCrowdNameWrite.CreateOwned(backing.CrowdNames,index,request,scope.OwnedReservation());
                            NavUnit actor;using(EnterTransport()){if(operation!=authorityOperationIdentity||!ReferenceEquals(backing,groupCommandBacking))return false;Crowd.TryGet(request.Entity,out actor);}
                            if(answer.Status==NavSolveStatus.Ready&&actor!=null){
                                if(count==1&&answer.Corridor!=null)using(NavStorageScratch.Reserve(scope.OwnedReservation(),RebindStagingScratchBound(Crowd.Units.Count,1,navigationGeometry.Obstacles.Count))){using(EnterTransport()){
                                    if(!AnswerNamesSourceCurrent(candidate))return false;candidate.CapacityHostBytes=hostRetainedBytes;candidate.CapacityInstalledBytes=installedCorridorBytes;candidate.CapacityGeometryBytes=installedGeometryBytes;
                                    long old=installedExecutions.TryGetValue(request.Entity,out var prior)?prior.Corridor.EstimatedRetainedBytes+16L*prior.InstalledRoute.Length:0;
                                    long consumed=IsCorridorAnswerTracked(answer)?answer.Corridor.EstimatedRetainedBytes:0;
                                    candidate.SingleCapacityExceeded=hostRetainedBytes+answerCorridorBytes-consumed+installedCorridorBytes-old+ProspectiveGeometryBytes(request.Entity,request)+answer.Corridor.EstimatedRetainedBytes+16L*(answer.RouteLength+1)>LayeredNavigationScheduler.MaxRetainedBytes;candidate.CapacityObserved=true;
                                }}
                                if(count==1&&!candidate.SingleCapacityExceeded)PrepareOwnedCorridorQualification(candidate,i,scope);
                            }
                        }
                        scope.Attach(candidate);scope.Publish(candidate,_=>{using(EnterTransport()){
                            if(!AnswerNamesSourceCurrent(candidate))throw new InvalidOperationException("Prepared answer names lost source.");
                        }});scope.ResolveCommitted();prepared=candidate;candidate=null;return true;
                    }catch(InvalidOperationException){return false;}
                    finally{candidate?.Dispose();}
                }
            }finally{backing?.Release();observedAdmission?.Release();}
        }
        private bool PrepareAnswerInstallDestination(NavigationPreparedAnswerNames names,int index,NavigationRetentionBudget.Scope scope)
        {
            var answer=names.Answers[index];var request=answer.Request;NavUnit actor;bool detached;
            using(EnterTransport()){if(!AnswerNamesSourceCurrent(names))return false;if(!Crowd.TryGet(request.Entity,out actor))return false;detached=answer.Corridor==null||answer.Corridor.Group==0||!memberGroups.TryGetValue(request.Entity,out var group)||group!=answer.Corridor.Group;}
            if(names.Routes[index]==null)names.Routes[index]=NavPreparedUnitRoute.PrepareOwned(Crowd,actor,request.Goal,answer.ReadOnlyRouteBacking,request.GoalLocation,scope.OwnedReservation());
            if(names.Routes[index]==null)return false;
            if(answer.StorageOwner?.CorridorOwner!=null&&names.Installed[index]==null)names.Installed[index]=NavigationInstalledExecutionOwner.CreateOwned(answer.StorageOwner,names.Routes[index].Route,detached,scope.OwnedReservation());return true;
        }
        private bool PrepareCompatibilityInstallDestinations(NavigationPreparedAnswerNames names)
        {
            if(names==null)return true;
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,admissionPublicationIdentity,out var scope))return false;
            using(scope)try{
                for(int i=0;i<names.Answers.Length;i++){var a=names.Answers[i];if(a.Request.Entity<0||a.Request.StorageOwner==null||a.Status!=NavSolveStatus.Ready||names.SingleCapacityExceeded||names.Installed[i]!=null)continue;
                    if(!PrepareAnswerInstallDestination(names,i,scope)){using(EnterTransport())if(!AnswerNamesSourceCurrent(names))return false;}
                }return true;
            }catch(InvalidOperationException){return false;}
            // Destinations belong to the existing private names carrier. Its
            // explicit references outlive this temporary scope's birth journal.
        }
        private bool AnswerNamesSourceCurrent(NavigationPreparedAnswerNames value,int trackingMutations=-1)
        {
            if(value==null)return Answers.Count==0&&barrierAnswers.Count==0;
            if(value.CapacityObserved&&(hostRetainedBytes!=value.CapacityHostBytes||installedCorridorBytes!=value.CapacityInstalledBytes||installedGeometryBytes!=value.CapacityGeometryBytes))return false;
            if(!ReferenceEquals(value.Tracking,ownedAnswerTracking)||(ownedAnswerTracking?.Revision??0)!=value.TrackingRevision||value.TrackingRevision>int.MaxValue-(trackingMutations<0?value.Answers.Length:trackingMutations)||value.TrackingBytes!=answerCorridorBytes)return false;
            if(value.LegacyProbeDestination&&liveProbeAnswers!=null||!ReferenceEquals(value.BarrierRoot,barrierPayloads)||value.BarrierRevision!=barrierPayloadRevision||barrierPayloadRevision==long.MaxValue||value.Operation!=authorityOperationIdentity||!ReferenceEquals(value.Backing,groupCommandBacking)||!ReferenceEquals(value.ObservedAdmission,admissionOwner)||value.MailboxRevision!=Answers.Revision||value.BarrierCount!=barrierAnswers.Count)return false;
            for(int i=0;i<value.BarrierCount;i++)if(!ReferenceEquals(value.BarrierSource[i],barrierAnswers[i]))return false;
            if(value.Single&&value.BarrierCount>0&&(value.BarrierIndex>=barrierAnswers.Count||!ReferenceEquals(value.Answers[0],barrierAnswers[value.BarrierIndex])))return false;
            foreach(var observation in value.GroupObservations)if(observation.Owner!=null&&!observation.Owner.MatchesObservation(observation))return false;
            try{foreach(var route in value.Routes)route?.Validate(Crowd);return true;}catch(InvalidOperationException){return false;}
        }
        private static NavNameReference OwnedOrFlatName(NavLocation? location)
        {
            if(!location.HasValue)return default;var name=location.Value.NameReference;
            if(name.Owner==null){if(location.Value.SurfaceId!=NavLocation.FlatSurface)throw new InvalidOperationException("Crowd surface has no owned name role.");return default;}
            return name;
        }
        private NavigationCrowdNameBacking CaptureCrowdNameReferences(INavStorageReservation reservation,int excludedEntity=0)
        {
            int count;using(EnterTransport())count=Crowd.Units.Count-(excludedEntity!=0&&Crowd.TryGet(excludedEntity,out _)?1:0);
            using(NavStorageScratch.Reserve(reservation,checked(64L+4L*count+3L*count*NavStorageLayout.ValueBytes(typeof(NavNameReference))))){
                var names=new NavNameReference[count*3];var keys=new int[count];int pinned=0;
                try{
                    using(EnterTransport()){int i=0;foreach(var u in Crowd.Units){if(u.Id==excludedEntity)continue;keys[i]=u.Id;names[i*3]=OwnedOrFlatName(u.Location);names[i*3+1]=OwnedOrFlatName(u.GoalLocation);names[i*3+2]=OwnedOrFlatName(u.FailedGoal);for(int n=0;n<3;n++){names[i*3+n].Owner?.Retain();pinned++;}i++;}if(i!=count)throw new InvalidOperationException("Crowd name source changed during preparation.");}
                    return NavigationCrowdNameBacking.CopyOwned(keys,names,reservation);
                }finally{for(int i=0;i<pinned;i++)names[i].Owner?.Release();}
            }
        }
        private void CaptureCrowdNameCatalog(NavigationSessionState state)
        {
            int count=state.Crowd.Units.Length;state.CaptureCrowdNames=new NavNameReference[count*3];state.CaptureCrowdPoints=new NavPoint[count*3];state.CaptureCrowdKeys=new int[count];state.CaptureCrowdFlags=new int[count];
            for(int i=0;i<count;i++){
                var u=state.Crowd.Units[i];state.CaptureCrowdKeys[i]=u.Id;state.CaptureCrowdFlags[i]=(u.GoalLocation.HasValue?1:0)|(u.FailedGoal.HasValue?2:0);
                var location=OwnedOrFlatName(u.Location);var goal=OwnedOrFlatName(u.GoalLocation);var failed=OwnedOrFlatName(u.FailedGoal);
                location.Owner?.Retain();state.CaptureCrowdNames[i*3]=location;
                goal.Owner?.Retain();state.CaptureCrowdNames[i*3+1]=goal;
                failed.Owner?.Retain();state.CaptureCrowdNames[i*3+2]=failed;
                state.CaptureCrowdPoints[i*3]=u.Location.Value.Position;state.CaptureCrowdPoints[i*3+1]=u.GoalLocation?.Position??default;state.CaptureCrowdPoints[i*3+2]=u.FailedGoal?.Position??default;
            }
        }
        private void BindRestoredCrowdNames(NavigationAuthorityOwners authority,NavigationSessionState state)
        {
            if(authority?.HasCatalog!=true){Crowd.BindLegacyRestoredSurfaceNames();return;}
            if(authority.CrowdKeys.Count!=state.Crowd.Units.Length)throw new ArgumentException("Crowd BODY role count mismatch.");
            NavLocation? Bind(int index,NavLocation? saved,bool present){
                var name=authority.CrowdNames[index];var point=authority.CrowdPoints[index];
                if(saved.HasValue!=present||!present&&(name.Owner!=null||point.X!=0||point.Z!=0))throw new ArgumentException("Crowd BODY optional role mismatch.");if(!present)return null;
                var expected=name.Owner==null?NavLocation.FlatSurface:name.Resolve();
                if(saved.Value.SurfaceId!=expected||BitConverter.DoubleToInt64Bits(saved.Value.Position.X)!=BitConverter.DoubleToInt64Bits(point.X)||BitConverter.DoubleToInt64Bits(saved.Value.Position.Z)!=BitConverter.DoubleToInt64Bits(point.Z))throw new ArgumentException("Crowd BODY name/point role mismatch.");
                return name.Owner==null?new NavLocation(point,NavLocation.FlatSurface):new NavLocation(point,name);
            }
            for(int i=0;i<state.Crowd.Units.Length;i++){
                var saved=state.Crowd.Units[i];int flags=authority.CrowdFlags[i];if(authority.CrowdKeys[i]!=saved.Id||(uint)flags>3||!Crowd.TryGet(saved.Id,out var actor))throw new ArgumentException("Crowd BODY key/flags mismatch.");
                actor.Location=Bind(i*3,saved.Location,true).Value;actor.GoalLocation=Bind(i*3+1,saved.GoalLocation,(flags&1)!=0);actor.FailedGoal=Bind(i*3+2,saved.FailedGoal,(flags&2)!=0);
            }
        }
    }
}
