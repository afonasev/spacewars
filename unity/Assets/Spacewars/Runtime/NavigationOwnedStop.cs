using System;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        internal bool TryStopGroup(int[] ids,bool hold)
        {
            var owned=TryOwnedStopGroup(ids,hold);if(owned.HasValue)return owned.Value;
            foreach(int id in ids)Stop(id,hold);return true;
        }
        private bool? TryOwnedStopGroup(int[] ids,bool hold,bool remove=false)
        {
            using(EnterTransport())if(!(groups is NavigationCommandSlots<long,GroupOrderState>)&&(groupCommandBacking!=null||!EmptyCommandStateForProbe()||Requests.Count!=0||!Requests.HasInitialPreparedBacking))return null;
            NavigationCommandSourcePins pins;NavigationHostRoot host;NavigationAuthorityOwners authority;long operation,publication,mailboxRevision;int mailboxCount;GroupOrderState context,observedContext;bool metadataContext;
            using(EnterTransport()){
                if(ids==null||ids.Length==0||preparedCommandGroup!=null||admissionBatchDepth!=0||remove&&(ids.Length!=1||hold||commandGroup!=null))return false;
                observedContext=commandGroup;metadataContext=observedContext!=null&&observedContext.Kind!=PlayableCommandKind.Stop&&observedContext.Kind!=PlayableCommandKind.Hold;context=metadataContext?null:observedContext;
                host=committedHostRoot;authority=authorityOwners;operation=authorityOperationIdentity;publication=admissionPublicationIdentity;Requests.ObserveState(out mailboxRevision,out mailboxCount);pins=new NavigationCommandSourcePins(host,authority,groupCommandBacking);
            }
            using(pins){
                if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))return false;
                bool staged=false;try{
                    NavigationGroupCommand candidate=null;NavigationCommandAuthority retired=null;
                    NavigationCommandSlots<long,GroupOrderState> ng=null;NavigationCommandSlots<int,long> nm=null,no=null;NavigationCommandSlots<int,NavigationRequest> np=null,na=null;
                    NavigationCommandSlots<int,NavPoint> nr=null,goal=null;NavigationCommandSlots<int,string> ni=null;NavigationCommandSlots<int,NavSolveStatus> nf=null;NavigationCommandSlots<long,NavigationArrivalReservation> nl=null;
                    try{
                        long corridors=0,geometryBytes=0;
                        using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(8192L+2048L*ids.Length+512L*installedExecutions.Count))){
                            NavUnit.PrepareStopBacking();
                            ids=(int[])ids.Clone();Array.Sort(ids);for(int i=1;i<ids.Length;i++)if(ids[i]==ids[i-1])return false;
                            int memberCapacity;using(EnterTransport()){if(operation!=authorityOperationIdentity)return false;int added=0;foreach(int id in ids)if(!memberGroups.ContainsKey(id))added++;memberCapacity=Math.Max(NavigationCommandLayout.Capacity(memberGroups),checked(memberGroups.Count+added));}
                            ng=CopyCommandMap(groups,context==null?0:1,scope,operation);nm=CopyCommandMap(memberGroups,0,scope,operation,exactCapacity:memberCapacity);no=CopyCommandMap(orders,ids.Length,scope,operation);np=CopyCommandMap(pending,0,scope,operation,r=>r.StorageOwner);na=CopyCommandMap(allocating,0,scope,operation,r=>r.StorageOwner);nr=CopyCommandMap(reservations,0,scope,operation);goal=CopyCommandMap(retainedGoals,0,scope,operation);ni=CopyCommandMap(movementIdentities,0,scope,operation);nf=CopyCommandMap(technicalFailures,0,scope,operation);nl=CopyCommandMap(arrivalLedger,0,scope,operation);
                            foreach(int entity in ids){if(!Crowd.TryGet(entity,out var actor)||actor.MobilityRevision==long.MaxValue)return false;bool hadOrder=no.TryGetValue(entity,out long order);if(!remove||hadOrder)no.SetPreparing(entity,checked(order+1));np.Remove(entity);na.Remove(entity);nr.Remove(entity);goal.Remove(entity);ni.Remove(entity);nf.Remove(entity);}
                            foreach(var row in arrivalLedger)if(Array.BinarySearch(ids,(int)row.Value.Entity)>=0)nl.Remove(row.Key);
                            candidate=NavigationGroupCommand.CreateOwned(Requests.InstanceCapacity,ids.Length,scope.OwnedReservation());
                            candidate.Groups=ng;candidate.MemberGroups=nm;candidate.Orders=no;candidate.Pending=np;candidate.Allocating=na;candidate.Reservations=nr;candidate.Goals=goal;candidate.Identities=ni;candidate.Failures=nf;candidate.Ledger=nl;candidate.OwnMaps();
                            using(NavStorageScratch.Reserve(scope.OwnedReservation(),32L+8L*mailboxCount))foreach(var queued in Requests.CopyItemsAtRevision(mailboxRevision))candidate.AppendMailbox(queued);
                            using(EnterTransport()){if(operation!=authorityOperationIdentity)throw new InvalidOperationException("Stop installed observation changed.");for(int i=0;i<ids.Length;i++){Crowd.TryGet(ids[i],out candidate.Actors[i]);candidate.ActorMobility[i]=candidate.Actors[i].MobilityRevision;installedExecutions.TryGetValue(ids[i],out var observedInstalled);candidate.ObserveInstalled(i,observedInstalled,installedExecutions.IsDetached(ids[i]));}}
                            if(remove){
                                candidate.IsRemoval=true;candidate.RemovalSourceCount=Crowd.Units.Count;
                                candidate.RemovalSpatial=Crowd.Units.Count>1?NavCrowdSpatialStorage.Create(Crowd.Units.Count-1,scope.TryReserve):null;
                                int entity=ids[0];
                                if(nm.TryGetValue(entity,out var previous)){
                                    var replacement=ng[previous].Copy();replacement.Members=replacement.Members.Where(m=>m.Entity!=entity).ToArray();replacement.MembershipRevision++;replacement.MarchAnchor=0;replacement.MarchInitialized=false;nm.Remove(entity);
                                    if(replacement.Members.Length==0)ng.Remove(previous);
                                    else{var birth=CopyCommandGroup(replacement,scope);try{ng.SetPreparing(previous,birth.PreparedRecord(scope),birth);candidate.Births[candidate.BirthCount++]=birth;}finally{birth.ReleaseStorage();}}
                                }
                            }
                            if(context==null&&!remove)foreach(long groupId in ids.Where(nm.ContainsKey).Select(id=>nm[id]).Distinct()){
                                var live=(NavigationLiveGroup)ng.OwnerFor(groupId);NavigationAuthorityGroup version;GroupOrderState header;
                                using(EnterTransport()){if(operation!=authorityOperationIdentity)return false;version=live.ImmutableVersion;version.Retain();header=ng[groupId].CopyHeader();}
                                try{
                                    header.MarchAnchor=0;header.MarchInitialized=false;
                                    foreach(int entity in ids.Where(id=>nm.TryGetValue(id,out var gid)&&gid==groupId)){
                                        int index=Enumerable.Range(0,version.Members.Count).Single(i=>((NavigationAuthorityMember)version.Members[i]).Data.Entity==entity);var previous=(NavigationAuthorityMember)version.Members[index];var v=previous.Data;
                                        var values=new NavigationAuthorityMember.Values(v.Entity,v.Slot,v.Incarnation,v.OrderRevision,checked(candidate.ActorMobility[Array.BinarySearch(ids,entity)]+1),v.FormationReleased?GroupMemberPhase.Combat:hold?GroupMemberPhase.Held:GroupMemberPhase.Stopped,v.Goal,v.Location,v.HasLocation,v.HasGoal,v.FormationReleased,v.Branch,0,v.ContactTick,0,0);
                                        var member=NavigationAuthorityMember.CopyWithValuesOwned(previous,values,scope.OwnedReservation());try{var next=NavigationAuthorityGroup.CopyWithMemberAndMarchOwned(version,index,member,new NavigationAuthorityGroup.Values(header),scope.OwnedReservation());version.Release();version=next;}finally{member.ReleaseStorage();}
                                    }
                                    var birth=NavigationLiveGroup.CreateOwned(version,scope);try{birth.SealForInstall(scope);ng.SetPreparing(groupId,birth.PreparedRecord(scope),birth);candidate.Births[candidate.BirthCount++]=birth;}finally{birth.ReleaseStorage();}
                                }finally{version.Release();}
                            }
                            if(context!=null){
                                foreach(int entity in ids)if(nm.TryGetValue(entity,out var previous)){
                                    var old=ng[previous];var replacement=old.Copy();replacement.Members=replacement.Members.Where(m=>m.Entity!=entity).ToArray();replacement.MembershipRevision++;replacement.MarchAnchor=0;replacement.MarchInitialized=false;nm.Remove(entity);
                                    if(replacement.Members.Length==0)ng.Remove(previous);else{var birth=CopyCommandGroup(replacement,scope);try{ng.SetPreparing(previous,birth.PreparedRecord(scope),birth);candidate.Births[candidate.BirthCount++]=birth;}finally{birth.ReleaseStorage();}}
                                }
                                var next=context.CopyHeader();next.Members=new GroupMemberState[ids.Length];if(marchConfigured){next.MarchVersion=1;next.MarchProfileRevision=marchProfileRevision;next.MarchStretch=marchStretch;}
                                for(int i=0;i<ids.Length;i++){var actor=candidate.Actors[i];next.Members[i]=new GroupMemberState{Entity=actor.Id,Incarnation=actor.Incarnation,MobilityRevision=checked(actor.MobilityRevision+1),OrderRevision=commandActivationRevision,Phase=hold?GroupMemberPhase.Held:GroupMemberPhase.Stopped};}
                                int slot=0;foreach(var member in SpatialSlotOrder(next.Members,next.OriginalGoal))member.Slot=slot++;
                                var live=CopyCommandGroup(next,scope);try{ng.SetPreparing(next.GroupId,live.PreparedRecord(scope),live);candidate.Births[candidate.BirthCount++]=live;}finally{live.ReleaseStorage();}
                                foreach(int entity in ids)nm.SetPreparing(entity,next.GroupId);
                            }
                            var seen=new System.Collections.Generic.HashSet<NavGeometry>();foreach(var row in installedExecutions.Values)if(Array.BinarySearch(ids,row.Entity)<0){corridors=checked(corridors+row.Corridor.EstimatedRetainedBytes+16L*row.InstalledRoute.Length);if(seen.Add(row.MovementGeometry))geometryBytes+=GeometryRetention(row.MovementGeometry);if(seen.Add(row.BaseGeometry))geometryBytes+=GeometryRetention(row.BaseGeometry);}
                            candidate.AdmissionOwner=PrepareAdmissionProjection(ng,np,requestSequence,checked(publication+1),operation,scope,admissionOwner);
                            int kept=0;for(int i=0;i<candidate.BirthCount;i++){var birth=candidate.Births[i];if(ReferenceEquals(ng.OwnerFor(birth.ImmutableVersion.Data.GroupId),birth))candidate.Births[kept++]=birth;else birth.AbortPrivate();}for(int i=kept;i<candidate.BirthCount;i++)candidate.Births[i]=null;candidate.BirthCount=kept;
                            candidate.Committed.CrowdNames=CaptureCrowdNameReferences(scope.OwnedReservation(),remove?ids[0]:0);foreach(var actor in candidate.Actors)candidate.Committed.CrowdNames.ClearFailedPreparing(actor.Id);
                            candidate.ExpectedHost=host;candidate.ExpectedAuthority=authority;candidate.PinObservation();candidate.Operation=operation;candidate.ExpectedPublication=publication;candidate.MailboxRevision=mailboxRevision;candidate.CommandContext=observedContext;candidate.IsStop=true;candidate.StopHold=hold;candidate.StopCorridorBytes=corridors;candidate.StopGeometryBytes=geometryBytes;candidate.GroupSequence=context==null?groupSequence:Math.Max(groupSequence,context.GroupId);ng.Seal();nm.Seal();no.Seal();np.Seal();na.Seal();nr.Seal();goal.Seal();ni.Seal();nf.Seal();nl.Seal();
                        }
                        ids=null;
                        scope.Attach(candidate);
                        if(context!=null){using(EnterTransport()){if(operation!=authorityOperationIdentity||!ReferenceEquals(context,commandGroup))return false;preparedCommandGroup=candidate;preparedCommandScope=scope;staged=true;}return true;}
                        PublishPreparedStop(candidate,scope,metadataContext);return true;
                    }catch(InvalidOperationException){if(candidate?.Published==true||remove)throw;return false;}
                    finally{if(!staged)candidate?.ReleaseStorage();ng?.ReleaseStorage();nm?.ReleaseStorage();no?.ReleaseStorage();np?.ReleaseStorage();na?.ReleaseStorage();nr?.ReleaseStorage();goal?.ReleaseStorage();ni?.ReleaseStorage();nf?.ReleaseStorage();nl?.ReleaseStorage();}
                }finally{if(!staged)scope.Dispose();}
            }
        }
        private void PublishPreparedStop(NavigationGroupCommand candidate,NavigationRetentionBudget.Scope scope,bool preserveCommandContext=false)
        {
            NavigationCommandAuthority retired=null;
            scope.Publish(candidate,_=>{using(EnterTransport())using(var mailbox=Requests.EnterPreparedWrite()){
                if(candidate.Operation!=authorityOperationIdentity||candidate.ExpectedPublication!=admissionPublicationIdentity||!ReferenceEquals(candidate.ExpectedHost,committedHostRoot)||!ReferenceEquals(candidate.ExpectedAuthority,authorityOwners)||!ReferenceEquals(candidate.CommandContext,commandGroup)||scope.IsCancelled)throw new InvalidOperationException("Stop lost observed authority.");
                if(candidate.IsRemoval&&(Crowd.Units.Count!=candidate.RemovalSourceCount||candidate.RemovalSourceCount>1&&(candidate.RemovalSpatial==null||candidate.RemovalSpatial.ActorCount!=candidate.RemovalSourceCount-1)))throw new InvalidOperationException("Removal lost prepared crowd index.");
                mailbox.Validate(candidate.MailboxRevision,candidate.Mailbox,candidate.MailboxCount);for(int i=0;i<candidate.BirthCount;i++)candidate.Births[i].ValidateInstall(scope);
                for(int i=0;i<candidate.Actors.Length;i++){if(!Crowd.TryGet(candidate.Actors[i].Id,out var actor)||!ReferenceEquals(actor,candidate.Actors[i])||actor.MobilityRevision!=candidate.ActorMobility[i])throw new InvalidOperationException("Stop actor changed.");installedExecutions.TryGetValue(candidate.Actors[i].Id,out var installed);if(!ReferenceEquals(installed,candidate.Installed[i])||installedExecutions.IsDetached(candidate.Actors[i].Id)!=candidate.InstalledDetached[i])throw new InvalidOperationException("Stop execution changed.");}
                for(int i=0;i<candidate.BirthCount;i++)candidate.Births[i].ActivateNoThrow(this);
                retired=groupCommandBacking;groupCommandBacking=candidate.Committed;groups=candidate.Groups;memberGroups=candidate.MemberGroups;orders=candidate.Orders;pending=candidate.Pending;allocating=candidate.Allocating;reservations=candidate.Reservations;retainedGoals=candidate.Goals;movementIdentities=candidate.Identities;technicalFailures=candidate.Failures;arrivalLedger=candidate.Ledger;groupSequence=candidate.GroupSequence;
                for(int i=0;i<candidate.Actors.Length;i++){var actor=candidate.Actors[i];actor.ApplyPreparedStopNoThrow(candidate.ActorMobility[i]+1,candidate.StopHold,out var global,out var local);candidate.RetiredActorRoutes[i*3]=global;candidate.RetiredActorRoutes[i*3+1]=local;actor.StorageOwner.SetInstalledNoThrow(null,false,null,out candidate.RetiredActorRoutes[i*3+2]);}
                if(candidate.IsRemoval){Crowd.RemovePrepared(candidate.Actors[0].Id,candidate.RemovalSpatial);candidate.RemovalSpatial=null;}
                installedCorridorBytes=candidate.StopCorridorBytes;installedGeometryBytes=candidate.StopGeometryBytes;if(PublishAdmissionOwnerNoThrow(candidate.AdmissionOwner))candidate.AdmissionOwner=null;authorityOperationIdentity=candidate.Operation+1;mailbox.InstallNoThrow(candidate.Mailbox,candidate.MailboxCount,candidate.Committed.MailboxOwners);candidate.Published=true;for(int i=0;i<candidate.BirthCount;i++)candidate.Births[i]=null;candidate.BirthCount=0;if(!preserveCommandContext){commandGroup=null;commandActivationRevision=0;}preparedCommandGroup=null;preparedCommandScope=null;
            }});
            scope.ResolveCommitted();candidate.ReleaseObservationPins();retired?.ReleaseStorage();PruneArrivalAttempts();SignalHost();foreach(var actor in candidate.Actors)MovementCancelled?.Invoke(actor.Id);
        }
    }
}
