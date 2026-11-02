using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class AiCombatMicroExecutionOwner
    {
        // Every allocation and fallible policy check precedes Native's first
        // write. The publication owns this slot through factual installation.
        internal sealed class PreparedInstall : IDisposable
        {
            readonly AiCombatMicroExecutionOwner registry;
            readonly Submission submission;
            readonly AiCombatMicroExecutionPathWork.Cursor[] retired;
            readonly AiCombatMicroExecutionStorage.Lease memory;
            bool entered,installed,disposed;
            internal PreparedInstall(AiCombatMicroExecutionOwner registry,Submission submission,AiCombatMicroExecutionPathWork.Cursor[] retired,AiCombatMicroExecutionStorage.Lease memory)
            {this.registry=registry;this.submission=submission;this.retired=retired;this.memory=memory;}
            internal bool TryValidateCurrent(ref long grant)
            {
                // Broken slot custody/state is an invariant fault. Live policy or
                // temporary guard admission unavailability is a normal refusal.
                if(disposed||entered||!registry.Owns(submission)||submission.State!=AiCombatMicroExecutionStatus.Ready||submission.Input.Source.InFlight!=null)throw new InvalidOperationException("Current micro publication is unavailable.");
                return registry.Current(submission,false,ref grant,out _);
            }
            internal void Validate(ref long grant)
            {if(!TryValidateCurrent(ref grant))throw new InvalidOperationException("Current micro publication is unavailable.");}
            internal void EnterNoThrow()
            {
                entered=true;submission.State=AiCombatMicroExecutionStatus.Executing;
                registry.operationFloors[submission.SourceIdentity].Value=submission.Input.NativeOperation;
            }
            internal void ApplyNoThrow()
            {
                for(int i=0;i<submission.Staged.Length;i++){
                    int entity=submission.Descriptors[i].Entity;
                    registry.committed[entity]=submission.Staged[i];submission.Staged[i]=null;
                }
                submission.State=AiCombatMicroExecutionStatus.Committed;installed=true;
            }
            public void Dispose()
            {
                if(disposed)return;
                if(entered&&!installed)throw new InvalidOperationException("Unresolved Native publication retains micro liability.");
                disposed=true;
                if(installed){foreach(var cursor in retired)cursor?.Dispose();registry.Close(submission);}
                memory.Dispose();
            }
        }
        internal PreparedInstall PrepareInstall(Issued issued,ref long grant)
        {
            if(!IssuedCurrent(issued,out var s)||s.State!=AiCombatMicroExecutionStatus.Ready||s.Match==null||!s.Match.Complete||!s.Match.Equal)return null;
            long cost=checked(64L+4L*s.Staged.Length);if(grant<cost||!storage.TryReserve(checked(512L+16L*s.Staged.Length),out var memory))return null;grant-=cost;
            try{
                committed.EnsureCapacity(checked(committed.Count+s.Staged.Length));
                var retired=new AiCombatMicroExecutionPathWork.Cursor[s.Staged.Length];
                for(int i=0;i<retired.Length;i++)committed.TryGetValue(s.Descriptors[i].Entity,out retired[i]);
                return new PreparedInstall(this,s,retired,memory);
            }catch{memory.Dispose();throw;}
        }
        internal MotionExecutionProposal ReadAcceptedSnapshot(Submission submission,ref long grant,AiCombatMicroExecutionStorage.Transaction allocation,out PlayableProfile profile)
        {
            if(!Owns(submission)||submission.Cold==null)throw new InvalidOperationException("No saved accepted promise.");
            if(!allocation.TryReserve(checked(8192L+16L*submission.Cold.Length)))throw new InvalidOperationException("Accepted snapshot decode admission refused.");
            using(var reader=new System.IO.BinaryReader(new System.IO.MemoryStream(submission.Cold,false))){reader.BaseStream.Position=24;var proposal=AiCombatMicroExecutionBindingWire.Read(reader,ref grant,allocation.TryReserve);profile=ReadAcceptedProfile(reader);return proposal;}
        }
        internal void CheckAcceptedSnapshot(Submission submission,MotionExecutionProposal projection)
        {
            using(var reader=new System.IO.BinaryReader(new System.IO.MemoryStream(submission.Cold,false))){reader.BaseStream.Position=24;AiCombatMicroExecutionBindingWire.Check(reader,projection);}
        }
        internal bool TryGetColdBinding(out Submission submission,out long operation,out long tick,out AiCombatMicroExecutionStatus state,string sourceIdentity=NavigationSession.GroundMotionSourceIdentity)
        {
            foreach(var row in submissions)if(row.Cold!=null&&row.SourceIdentity==sourceIdentity){
                if(row.Cold.Length<64)throw new ArgumentException("Truncated Native current binding.");submission=row;state=(AiCombatMicroExecutionStatus)BitConverter.ToInt32(row.Cold,0);operation=BitConverter.ToInt64(row.Cold,16);tick=BitConverter.ToInt64(row.Cold,56);return true;
            }
            submission=null;operation=tick=0;state=default;return false;
        }
        internal bool HasExecutionSubject(int entity)=>owner.ReservesExecutionSubject(entity);
    }
    internal sealed partial class PlayableDomain
    {
        internal string CombatExecutionBindingReadback(MotionExecutionPrimitive primitive)
        {
            int entity=checked((int)primitive.Binding.Entity);if(!TryActor(entity,out var n))return "actor missing";
            string pending="none";
            if(tacticalQueues.TryGetValue(entity,out var queue)&&queue.Pending!=null){var e=queue.Pending;pending=$"group={e.Issuance.GroupId} sequence={e.Issuance.Command.Sequence} activation={e.Stamp.Revision} request={e.AttemptRequest} order={e.AttemptOrder} kind={e.Issuance.Command.Kind}";}
            var native=navigation.PendingRequestFor(entity);string nativePending=native==null?"none":$"request={native.Request} order={native.Order} group={native.MemberIdentity?.GroupId} activation={native.MemberIdentity?.OrderRevision} membership={native.MemberIdentity?.MembershipRevision}";
            return $"native pending {nativePending}; actor group={n.InstalledGroupId} revision={n.InstalledRevision} request={n.InstalledRequest} order={n.InstalledOrder} incarnation={n.Incarnation} mobility={n.MobilityRevision} held={n.Held} at={n.Position.X:R},{n.Position.Z:R}/{n.Location.SurfaceId} heading={n.Heading:R}; pending {pending}";
        }
        internal PlayableProfile CombatExecutionAcceptedProfile(string id,int revision)
        {var accepted=Terms(revision);if(accepted.ProfileId!=id)throw new ArgumentException("Unknown accepted execution profile.");return accepted;}
    }
    internal sealed partial class PlayableAiOwnerLoop
    {
        internal bool ReservesExecutionSubject(int entity)=>combatMicro.Reserves(entity);
    }
}
