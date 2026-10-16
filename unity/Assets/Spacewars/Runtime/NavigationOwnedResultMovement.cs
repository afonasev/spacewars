using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private sealed partial class PreparedResultBatch
        {
            internal readonly NavigationLiveGroupObservation[] MarchSources;
            internal readonly bool[] MarchWrites;internal readonly NavigationAuthorityGroup[] PreMarchGroups;
            internal NavPreparedCrowdStep Frame;internal NavigationAdmissionStorage MovementAdmission;
            internal bool SuspendPending,MarchObserved,MarchConfigured,ComposedMovement,FrameObserved,MovementAdmissionChanged;
            internal double MarchStretch;internal int MarchRevision;internal CertifiedRouteKernel MovementWorker;internal long MovementAttachment;
            internal void ReleaseResultMovementEdges(){Frame?.Release();MovementAdmission?.Release();foreach(var version in PreMarchGroups)version?.ReleaseStorage();foreach(var source in MarchSources)source.ReleasePins();}
        }
        private static bool MarchRecordMatches(GroupOrderState record,NavigationAuthorityGroup version)
        {
            if(!new NavigationAuthorityGroup.Values(record).BitwiseSame(version.Data)||record.Members.Length!=version.Members.Count)return false;
            for(int i=0;i<record.Members.Length;i++)if(!new NavigationAuthorityMember.Values(record.Members[i]).BitwiseSame(((NavigationAuthorityMember)version.Members[i]).Data))return false;return true;
        }
        private bool ObserveResultMovementGroups(PreparedResultBatch c)
        {
            int at=0;for(int i=0;i<c.SourceGroups.Capacity;i++)if(c.SourceGroups.TryReadSlot(i,out long id,out var record)){
                if(!(c.SourceGroups.OwnerFor(id)is NavigationLiveGroup owner)||at==c.MarchSources.Length)return false;
                c.MarchSources[at++]=owner.ObserveForCopy(ObserveLiveWriteToken());
            }
            if(at!=c.MarchSources.Length)return false;
            for(int i=1;i<at;i++){var value=c.MarchSources[i];int j=i;while(j>0&&c.MarchSources[j-1].Version.Data.GroupId>value.Version.Data.GroupId){c.MarchSources[j]=c.MarchSources[j-1];j--;}c.MarchSources[j]=value;}
            c.SuspendPending=SuspendPendingMovement;c.MovementWorker=certifiedHost;c.MovementAttachment=certifiedHostAttachmentIdentity;c.MarchConfigured=marchConfigured;c.MarchStretch=marchStretch;c.MarchRevision=marchProfileRevision;c.MarchObserved=true;return true;
        }
        private bool ResultMovementSourcesCurrent(PreparedResultBatch c)
        {
            if(!c.MarchObserved)return true;
            if(c.SuspendPending!=SuspendPendingMovement||!ReferenceEquals(c.MovementWorker,certifiedHost)||c.MovementAttachment!=certifiedHostAttachmentIdentity||c.MarchConfigured!=marchConfigured||c.MarchStretch!=marchStretch||c.MarchRevision!=marchProfileRevision)return false;
            foreach(var observation in c.MarchSources)if(!observation.Owner.MatchesObservation(observation))return false;
            return !c.FrameObserved||RebindHostBoundaryQuiescent()&&ReferenceEquals(Crowd,c.Frame.SourceCrowd)&&c.Frame.SourceCurrent();
        }
        private bool PrepareResultMovement(PreparedResultBatch c,double dt,NavigationRetentionBudget.Scope scope)
        {
            if(c.ReadyCount==0&&c.FailureCount==0&&c.ReissueCount==0&&c.Operations.Length!=0&&!c.HasHeldPrefix||c.Allocating.Count!=0||Crowd.HasPreferredMotion)return true;
            using(EnterTransport()){
                if(!ResultBatchSourceCurrent(c))return false;if(!RebindHostBoundaryQuiescent())return true;
                foreach(var f in c.Attempts){var a=f.Owner.Record;if(!a.Retiring&&!a.Working&&!a.Reconstructing&&!a.Published&&a.SnapshotStatus!=NavArrivalStatus.Pending&&a.SnapshotStatus!=NavArrivalStatus.BudgetExhausted)return true;}
                foreach(var observation in c.MarchSources)if(!MarchRecordMatches(c.SourceGroups[observation.Version.Data.GroupId],observation.Version))return true;
            }
            int originalGroups=c.GroupCount;
            c.Frame=NavPreparedCrowdStep.Create(Crowd,c.Actors.Length,scope.OwnedReservation());
            using(EnterTransport()){if(!ResultBatchSourceCurrent(c))return false;c.Frame.Observe();c.FrameObserved=true;}
            foreach(var row in c.HeldRows)if(row.Changed){
                var f=row.Replacement.FailureSource;var actor=c.Frame.After[c.Frame.Find(f.Id)];
                if(!f.Held){actor.MobilityRevision=f.Mobility+1;actor.BindRouteBackingNoThrow(null,null);actor.Moving=actor.Held=false;actor.Outcome=NavigationOutcome.Stopped;c.Frame.ClearRoutes(actor);RestoreHeldFailureNoThrow(f,actor);}
                if(row.Eligible)ClearReissueFailureNoThrow(actor);
            }
            for(int i=0;i<c.Operations.Length;i++)if(c.Operations[i].Kind==ResultOperationKind.Ready){
                var op=c.Operations[i];var request=c.Names.Answers[i].Request;var actor=c.Frame.After[op.Actor];c.Names.Routes[i].ProjectInto(actor);
                actor.FailedRequest=actor.FailedOrder=actor.FailedGroupId=actor.FailedRevision=0;actor.FailedGoal=null;actor.LastFailure=NavigationOutcome.Idle;actor.InstalledGroupId=request.MemberIdentity?.GroupId??0;actor.InstalledRevision=request.MemberIdentity?.OrderRevision??0;actor.InstalledRequest=request.Request;actor.InstalledOrder=request.Order;
            }
            for(int i=0;i<c.Operations.Length;i++)if(c.Operations[i].Kind==ResultOperationKind.Failure){
                var op=c.Operations[i];var request=c.Names.Answers[i].Request;var actor=c.Frame.After[c.Frame.Find(request.Entity)];
                if(op.FailureReject){actor.MobilityRevision=op.FailureSource.Mobility+1;actor.BindRouteBackingNoThrow(null,null);actor.Moving=actor.Held=false;actor.Outcome=op.FailureOutcome;c.Frame.ClearRoutes(actor);}
                actor.FailedRequest=request.Request;actor.FailedOrder=request.Order;actor.FailedGroupId=request.MemberIdentity.GroupId;actor.FailedRevision=request.MemberIdentity.OrderRevision;actor.FailedGoal=request.GoalLocation;actor.LastFailure=op.FailureOutcome;
            }
            for(int i=0;i<c.Operations.Length;i++)if(c.Operations[i].Kind==ResultOperationKind.Reissue)ClearReissueFailureNoThrow(c.Frame.After[c.Frame.Find(c.Names.Answers[i].Request.Entity)]);
            var pending=c.NextPending??c.Pending;
            if(c.SuspendPending)foreach(var actor in c.Frame.After)if(pending.ContainsKey(actor.Id))actor.Moving=false;
            if(c.MarchConfigured)foreach(var observation in c.MarchSources){
                int group=-1;for(int i=0;i<c.GroupCount;i++)if(ReferenceEquals(c.GroupSources[i].Owner,observation.Owner)){group=i;break;}
                var previous=group<0?observation.Version:c.Groups[group];
                var next=GroupMarch.PrepareOwned(c.Frame,previous,c.ProjectedInstalled,c.ProjectedDetached,c.SourceMembers,c.MarchStretch,c.MarchRevision,dt,scope.OwnedReservation());
                if(next==null)continue;if(group<0){group=c.GroupCount++;c.GroupSources[group]=observation;}else c.PreMarchGroups[group]=c.Groups[group];c.Groups[group]=next;c.MarchWrites[group]=true;
            }
            using(NavStorageScratch.Reserve(scope.OwnedReservation(),RebindStagingScratchBound(c.Actors.Length,c.Operations.Length,navigationGeometry.Obstacles.Count)))c.Frame.Compute(dt);
            // Prove the complete member shape before changing private cleanup maps.
            // From this point a preparation refusal aborts the whole carrier.
            foreach(var observation in c.MarchSources)for(int i=0;i<observation.Version.Members.Count;i++){
                var member=((NavigationAuthorityMember)observation.Version.Members[i]).Data;int at=c.Frame.Find(member.Entity);
                if(at<0||c.Frame.After[at].Incarnation!=member.Incarnation)return AbandonResultMovement(c,originalGroups);
            }
            foreach(var actor in c.Frame.After)if(!actor.Moving&&!pending.ContainsKey(actor.Id)&&!c.Allocating.ContainsKey(actor.Id)){
                var ledger=c.NextLedger??c.Ledger;
                for(int i=0;i<ledger.Capacity;i++)if(ledger.TryReadSlot(i,out long key,out var row)&&row.Entity==actor.Id){
                    if(c.NextLedger==null)c.NextLedger=ResultBatchCopy(c,c.Ledger,scope);c.NextLedger.Remove(key);
                }
                if(!PrepareResultBatchPrune(c,scope))return false;
                if((c.NextReservations??c.Reservations).ContainsKey(actor.Id)){if(c.NextReservations==null)c.NextReservations=ResultBatchCopy(c,c.Reservations,scope);c.NextReservations.Remove(actor.Id);}
                if((c.NextGoals??c.Goals).ContainsKey(actor.Id)){if(c.NextGoals==null)c.NextGoals=ResultBatchCopy(c,c.Goals,scope);c.NextGoals.Remove(actor.Id);}
            }
            var reservations=c.NextReservations??c.Reservations;
            foreach(var observation in c.MarchSources){
                int group=-1;for(int i=0;i<c.GroupCount;i++)if(ReferenceEquals(c.GroupSources[i].Owner,observation.Owner)){group=i;break;}
                var previous=group<0?observation.Version:c.Groups[group];
                for(int i=0;i<previous.Members.Count;i++){
                    var original=(NavigationAuthorityMember)previous.Members[i];var d=original.Data;var actor=c.Frame.After[c.Frame.Find(d.Entity)];
                    var phase=d.FormationReleased?GroupMemberPhase.Combat:actor.Held?GroupMemberPhase.Held:pending.ContainsKey(actor.Id)?GroupMemberPhase.PendingRoute:actor.Moving?GroupMemberPhase.March:actor.Outcome==NavigationOutcome.Arrived?GroupMemberPhase.Arrived:actor.Outcome==NavigationOutcome.Unreachable?GroupMemberPhase.Unreachable:actor.Outcome==NavigationOutcome.Blocked||actor.Outcome==NavigationOutcome.Rejected?GroupMemberPhase.Blocked:GroupMemberPhase.Stopped;
                    int reservation=reservations.ContainsKey(actor.Id)?actor.Id:0;
                    if(d.MobilityRevision==actor.MobilityRevision&&d.Phase==phase&&d.Reservation==reservation)continue;
                    var values=new NavigationAuthorityMember.Values(d.Entity,d.Slot,d.Incarnation,d.OrderRevision,actor.MobilityRevision,phase,d.Goal,d.Location,d.HasLocation,d.HasGoal,d.FormationReleased,d.Branch,reservation,d.ContactTick,d.ProgressRequest,d.Progress);
                    var member=NavigationAuthorityMember.CopyWithValuesOwned(original,values,scope.OwnedReservation());NavigationAuthorityGroup next=null;
                    try{next=NavigationAuthorityGroup.CopyWithMemberAndMarchOwned(previous,i,member,previous.Data,scope.OwnedReservation());}finally{member.ReleaseStorage();}
                    if(group<0){group=c.GroupCount++;c.GroupSources[group]=observation;}else if(!c.MarchWrites[group])c.PreMarchGroups[group]=c.Groups[group];else c.Groups[group].ReleaseStorage();
                    c.Groups[group]=next;c.MarchWrites[group]=true;previous=next;
                }
            }
            using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(32L+c.GroupCount*(long)NavStorageLayout.ValueBytes(typeof(RebindGroup))))){
                var overlays=new RebindGroup[c.GroupCount];for(int i=0;i<overlays.Length;i++)overlays[i]=new RebindGroup{Source=c.GroupSources[i],Next=c.Groups[i]};
                c.MovementAdmission=PrepareAdmissionProjection(c.SourceGroups,pending,c.ProjectedSequence(requestSequence),checked(admissionPublicationIdentity+1),c.Names.Operation,scope,c.Names.ObservedAdmission,sourceProbes:c.NextProbes??c.Probes,rebindGroups:overlays,rebindGroupCount:overlays.Length);
            }
            c.ComposedMovement=true;return true;
        }
        private static bool AbandonResultMovement(PreparedResultBatch c,int originalGroups)
        {
            for(int i=0;i<c.GroupCount;i++)if(c.MarchWrites[i]){
                c.Groups[i].ReleaseStorage();c.Groups[i]=c.PreMarchGroups[i];c.PreMarchGroups[i]=null;c.MarchWrites[i]=false;if(i>=originalGroups)c.GroupSources[i]=default;
            }
            c.GroupCount=originalGroups;c.Frame.Release();c.Frame=null;c.FrameObserved=false;return true;
        }
        private void ValidateResultMovementInstall(PreparedResultBatch c,NavigationRetentionBudget.Scope scope,NavigationLiveWriteToken token)
        {
            if(!c.ComposedMovement)return;if(OwnedMovementPublications==int.MaxValue||!c.Frame.SourceCurrent())throw new InvalidOperationException("Projected movement source changed.");
            for(int i=0;i<c.GroupCount;i++)if(c.MarchWrites[i])c.GroupSources[i].Owner.ValidateMarchWrite(c.GroupSources[i],token);
        }
        private void ApplyResultMovementNoThrow(PreparedResultBatch c)
        {
            if(!c.ComposedMovement)return;
            for(int i=0;i<c.GroupCount;i++)if(c.MarchWrites[i]){if(c.RetiredGroups[i]==null)c.RetiredGroups[i]=c.GroupSources[i].Version;c.GroupSources[i].Owner.ApplyMovementMembersNoThrow(c.Groups[i]);}
            c.Frame.ApplyNoThrow();c.MovementAdmissionChanged=PublishAdmissionOwnerNoThrow(c.MovementAdmission);if(c.MovementAdmissionChanged)c.MovementAdmission=null;authorityOperationIdentity=c.Names.Operation+(c.Operations.Length==0?3:4)+c.ExtraOperations;OwnedMovementPublications++;
        }
    }
}
