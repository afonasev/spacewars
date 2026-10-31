// Dev/editor-only measurement adapter. Generated from the unchanged original
// method; inverse transformations reproduce that body byte-for-byte (evidence).
// No authoritative fields or semantic shortcuts; source guards and ownership stay.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal static class ContactPublishProfile
    {
        internal static bool Enabled=Environment.GetEnvironmentVariable("SPACEWARS_CONTACT_PUBLISH_PROFILE")=="1";
        internal static bool LegacyMemberPagesForQa; // Test-only predecessor, never serialized.
    }
    public sealed partial class NavigationSession
    {
        private bool? TryOwnedMemberStateProfiled(int entity,bool? visibleContact,long tick)
        {
            using var full=PerformanceProfile.Measure(ProfileStage.ContactOwnedTotal);
            NavigationLiveGroupObservation observation;NavigationHostRoot host;NavigationAuthorityOwners authority;
            NavigationAdmissionStorage observedAdmission;
            long operation,publication;int index;GroupOrderState metadata;GroupMemberState value;
            using(PerformanceProfile.Measure(ProfileStage.ContactSource))using(EnterTransport()){
                if(!memberGroups.TryGetValue(entity,out var groupId))return true;
                if(!(groups is NavigationCommandSlots<long,GroupOrderState> slots)||!(slots.OwnerFor(groupId)is NavigationLiveGroup owner))return null;
                var record=groups[groupId];index=Array.FindIndex(record.Members,m=>m.Entity==entity);if(index<0)throw new InvalidOperationException("Owned group lost member index.");
                if(!CanOwnAdmissionProjection())return false;
                observation=owner.ObserveForCopy(ObserveLiveWriteToken());host=committedHostRoot;authority=authorityOwners;host?.Retain();authority?.Retain();operation=authorityOperationIdentity;publication=admissionPublicationIdentity;
                metadata=record;value=record.Members[index];observedAdmission=admissionOwner;observedAdmission?.Retain();
            }
            try{
                NavigationRetentionBudget.Scope scope;
                using(PerformanceProfile.Measure(ProfileStage.ContactScopeAdmission)){
                    if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out scope))return false;
                }
                using(PerformanceProfile.Measure(ProfileStage.ContactScopeLifetime))using(scope){
                    NavigationAuthorityMember member=null;NavigationAuthorityGroup version=null;NavigationPreparedGroupWrite write=null;bool published=false,admissionPublished=false;
                    try{
                        using(PerformanceProfile.Measure(ProfileStage.ContactPrepareEnvelope))using(NavStorageScratch.Reserve(scope.OwnedReservation(),NavStorageLayout.ObjectBytesBound(typeof(GroupOrderState))+NavStorageLayout.ObjectBytesBound(typeof(GroupMemberState)))){
                            using(PerformanceProfile.Measure(ProfileStage.ContactDtoAndGuards)){
                            using(EnterTransport()){
                                if(operation!=authorityOperationIdentity||publication!=admissionPublicationIdentity||!observation.Owner.MatchesObservation(observation))return false;
                                metadata=metadata.CopyHeader();value=value.Copy();
                            }
                            if(visibleContact.HasValue){
                                if(value.FormationReleased!=visibleContact.Value){metadata.MarchAnchor=0;metadata.MarchInitialized=false;}
                                value.FormationReleased=visibleContact.Value;value.ContactTick=tick;value.ContactPhase="PostMovementVisibility";
                            }
                            using(EnterTransport()){
                                if(operation!=authorityOperationIdentity||!Crowd.TryGet(entity,out var actor)||actor.Incarnation!=value.Incarnation)return false;
                                value.MobilityRevision=actor.MobilityRevision;
                                if(allocating.ContainsKey(entity))value.Phase=GroupMemberPhase.Allocating;
                                else{value.Reservation=reservations.ContainsKey(entity)?entity:0;value.Phase=CurrentMemberPhase(entity,actor,value);}
                            }
                            if(!visibleContact.HasValue&&new NavigationAuthorityMember.Values(value).BitwiseSame(((NavigationAuthorityMember)observation.Version.Members[index]).Data)&&new NavigationAuthorityGroup.Values(metadata).BitwiseSame(observation.Version.Data))return true;
                            }
                            using(PerformanceProfile.Measure(ProfileStage.ContactMemberCopy)){
                            if(visibleContact.HasValue)member=NavigationAuthorityMember.CopyOwned(value,scope.OwnedReservation());
                            else member=NavigationAuthorityMember.CopyWithValuesOwned((NavigationAuthorityMember)observation.Version.Members[index],new NavigationAuthorityMember.Values(value),scope.OwnedReservation());
                            }
                            using(PerformanceProfile.Measure(ProfileStage.ContactGroupCow))version=NavigationAuthorityGroup.CopyWithMemberAndMarchOwned(observation.Version,index,member,new NavigationAuthorityGroup.Values(metadata),scope.OwnedReservation());
                        }
                        using(PerformanceProfile.Measure(ProfileStage.ContactWriteCarrier))write=NavigationPreparedGroupWrite.CreateOwned(observation,version,index,scope);
                        bool reuse;using(PerformanceProfile.Measure(ProfileStage.ContactAdmissionCheck))using(EnterTransport())reuse=operation==authorityOperationIdentity&&ReferenceEquals(observedAdmission,admissionOwner)&&admissionBatchDepth==0&&commandGroup==null&&observedAdmission!=null&&observedAdmission.Value.PublishedThroughRequestSequence==requestSequence&&observedAdmission.Groups.Count==groups.Count&&observedAdmission.Requests.Count==pending.Count+probes.Count&&observedAdmission.MatchesGroupReplacement(version);
                        using(PerformanceProfile.Measure(ProfileStage.ContactAdmissionPrepare)){
                        if(reuse){observedAdmission.Retain();write.Admission=observedAdmission;}
                        else write.Admission=PrepareAdmissionProjection(groups,pending,requestSequence,checked(publication+1),operation,scope,observedAdmission,version);
                        write.RequireEdge(write.Admission);scope.Attach(write);
                        }
                        using(PerformanceProfile.Measure(ProfileStage.ContactPublishEnvelope))scope.Publish(write,_=>{using(EnterTransport()){
                            using(PerformanceProfile.Measure(ProfileStage.ContactFinalGuards)){
                            if(operation!=authorityOperationIdentity||publication!=admissionPublicationIdentity||!ReferenceEquals(observedAdmission,admissionOwner)||!ReferenceEquals(host,committedHostRoot)||!ReferenceEquals(authority,authorityOwners)||scope.IsCancelled)throw new InvalidOperationException("Contact mutation lost observed authority.");
                            write.ValidateInstall(scope,ObserveLiveWriteToken());
                            }
                            using(PerformanceProfile.Measure(ProfileStage.ContactApply)){
                            write.ApplyNoThrow();admissionPublished=PublishAdmissionOwnerNoThrow(write.Admission);if(admissionPublished)write.Admission=null;authorityOperationIdentity=operation+1;published=true;
                            }
                        }});
                        using(PerformanceProfile.Measure(ProfileStage.ContactResolveRetire)){
                        scope.ResolveCommitted();write.ReleaseRetired();if(admissionPublished)SignalHost();
                        }return true;
                    }catch(InvalidOperationException){if(published)throw;return false;}
                    finally{using(PerformanceProfile.Measure(ProfileStage.ContactOwnerCleanup)){write?.ReleaseStorage();version?.ReleaseStorage();member?.ReleaseStorage();}}
                }
            }finally{using(PerformanceProfile.Measure(ProfileStage.ContactSourceRelease)){observation.ReleasePins();host?.Release();authority?.Release();observedAdmission?.Release();}}
        }
    }
}
#endif
