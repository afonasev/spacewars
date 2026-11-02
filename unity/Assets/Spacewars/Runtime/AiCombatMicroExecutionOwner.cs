using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Implemented by the actual Native input owner. A successful retain must pin
    // the exact immutable proposal and its complete upstream source graph until
    // disposal. Registration supplies identity/lifetime, never a policy verdict.
    internal interface IAiCombatMicroNativeCommittedRecord
    {
        IAiCombatMicroNativeExecutionSource NativeOwner {get;}
        long OperationId {get;}
        MotionExecutionProposal Actual {get;}
    }
    internal interface IAiCombatMicroNativeExecutionSource
    {
        // Sole Native issuer looks up its OWN completed atomic operation, then
        // retains that exact immutable committed record/source graph. A missing
        // record is pending, never an owner-policy decision.
        bool TryRetainCommitted(long operationId,out IAiCombatMicroNativeCommittedRecord record,out IDisposable retention);
    }
    internal interface IAiCombatMicroNativeInput
    {
        IAiCombatMicroNativeExecutionSource NativeOwner {get;}
        long OperationId {get;}
        string SourceIdentity {get;}
        MotionExecutionProposal Proposal {get;}
        bool TryRetain(out IDisposable retention);
    }
    internal enum AiCombatMicroExecutionStatus { Pending,Allowed,Matching,Ready,Executing,Committed,Cancelled,Denied,AwaitingSource }
    internal sealed partial class AiCombatMicroExecutionOwner:IDisposable
    {
        sealed class OperationFloor{internal long Value;internal readonly AiCombatMicroExecutionStorage.Lease Memory;internal OperationFloor(long value,AiCombatMicroExecutionStorage.Lease memory){Value=value;Memory=memory;}}
        internal sealed class Source
        {
            internal readonly AiCombatMicroExecutionOwner Registry;internal readonly IAiCombatMicroNativeExecutionSource NativeOwner;internal readonly string Identity;internal AiCombatMicroExecutionStorage.Lease Memory;internal Ticket InFlight;internal bool Live=true;
            internal Source(AiCombatMicroExecutionOwner registry,IAiCombatMicroNativeExecutionSource owner,string identity){Registry=registry;NativeOwner=owner;Identity=identity;}
        }
        internal sealed class Input
        {
            internal readonly AiCombatMicroExecutionOwner Registry;internal readonly Source Source;internal readonly MotionExecutionProposal Proposal;internal readonly long NativeOperation;
            internal IDisposable NativePin;internal AiCombatMicroExecutionStorage.Lease Root;internal bool Live=true;
            internal Input(AiCombatMicroExecutionOwner registry,Source source,MotionExecutionProposal proposal,long operation,IDisposable pin,AiCombatMicroExecutionStorage.Lease root){NativeOperation=operation;Registry=registry;Source=source;Proposal=proposal;NativePin=pin;Root=root;}
            internal void Release(){if(!Live)return;Live=false;NativePin.Dispose();NativePin=null;Root.Dispose();Root=null;Registry.inputs.Remove(this);}
        }
        internal readonly struct KnownThreat
        {
            internal readonly int Id,Kind;internal readonly PlayableOwner Owner;internal readonly NavPoint Position;
            internal KnownThreat(int id,int kind,PlayableOwner owner,NavPoint position){Id=id;Kind=kind;Owner=owner;Position=position;}
            internal KnownThreat(Spacewars.Simulation.Ai.AiKnownContact c){Id=c.Id;Kind=c.Kind;Owner=c.Owner;Position=c.Position;}
            internal void Write(BinaryWriter w){w.Write(Id);w.Write(Kind);w.Write((int)Owner);WorldWire.Write(w,Position);}
            internal bool Same(Spacewars.Simulation.Ai.AiKnownContact c)=>Id==c.Id&&Kind==c.Kind&&Owner==c.Owner&&MotionExecutionBinding.Same(Position.X,c.Position.X)&&MotionExecutionBinding.Same(Position.Z,c.Position.Z);
        }
        internal sealed class Submission
        {
            internal readonly long Id;internal string SourceIdentity;internal Input Input,Actual;
            internal KnownThreat[] Known;internal AiCombatMicroPolicy.ExecutionDescriptor[] Descriptors;
            internal AiCombatMicroExecutionPathWork[] Paths;internal AiCombatMicroExecutionPathWork.Cursor[] Staged;
            internal AiCombatMicroCoverScene Scene;internal AiCombatMicroExecutionStorage.Lease Memory;
            internal AiCombatMicroExecutionMatch Match;internal int Index;internal Issued Permit;internal Ticket ActiveTicket;internal IAiCombatMicroNativeCommittedRecord Receipt;internal IDisposable ReceiptPin;internal AiCombatMicroExecutionMatch ReceiptMatch;
            internal byte[] Cold;internal bool RestoredExecuting;internal AiCombatMicroExecutionStatus State;
            internal Submission(long id){Id=id;}
        }
        internal sealed class Issued
        {
            internal readonly AiCombatMicroExecutionOwner Registry;internal readonly Submission Submission;
            internal readonly MotionExecutionPermit Value;
            internal Issued(AiCombatMicroExecutionOwner registry,Submission submission,MotionExecutionPermit value){Registry=registry;Submission=submission;Value=value;}
        }
        internal sealed class Ticket
        {
            internal readonly AiCombatMicroExecutionOwner Registry;internal readonly Submission Submission;internal bool Used;
            internal Ticket(AiCombatMicroExecutionOwner registry,Submission submission){Registry=registry;Submission=submission;}
        }
        readonly AiCombatMicroExecutionStorage storage=new AiCombatMicroExecutionStorage();
        readonly Dictionary<string,OperationFloor> operationFloors=new Dictionary<string,OperationFloor>(StringComparer.Ordinal);readonly List<Input> inputs=new List<Input>();readonly List<Source> sources=new List<Source>();readonly List<Submission> submissions=new List<Submission>();
        readonly Dictionary<int,AiCombatMicroExecutionPathWork.Cursor> committed=new Dictionary<int,AiCombatMicroExecutionPathWork.Cursor>();
        PlayableAiOwnerLoop owner;PlayableDomain domain;bool disposed;long nextSubmission,nextPermit;
        internal AiCombatMicroExecutionOwner(PlayableAiOwnerLoop owner){this.owner=owner;}
        internal long Retained=>storage.Retained;
        // Restore is a pre-clock rebind phase. The compact dormant image cannot
        // create operational headroom: no new proposal, work or issue is admitted
        // until every accepted promise has its full state-derived live liability.
        internal bool AwaitingNativeSources=>submissions.Any(s=>s.Cold!=null);
        internal void Bind(PlayableAiOwnerLoop current,PlayableDomain currentDomain){if(disposed)throw new ObjectDisposedException(nameof(AiCombatMicroExecutionOwner));if(current==null||currentDomain==null||current.OwnerId!=owner.OwnerId||current.Generation!=owner.Generation)throw new ArgumentException("Foreign execution owner/domain.");owner=current;domain=currentDomain;ReconcileLifecycle();}
        // Explicit bounded lifecycle work; never hidden in a path Advance credit.
        // Cold accepted inputs are not inspected until Native rebind completes.
        long RetirementWork
        {
            get{long subjects=0;foreach(var s in submissions)subjects+=s.Input?.Proposal.Count??0;return checked(64L+4L*submissions.Count+committed.Count*(1152L+4L*submissions.Count+4L*subjects));}
        }
        internal bool TryReconcileCommitted(ref long grant)
        {
            Bound();if(committed.Count==0||AwaitingNativeSources)return true;
            long control=checked(64L+4L*submissions.Count);if(grant<control)return false;grant-=control;long subjects=0;foreach(var s in submissions)subjects+=s.Input?.Proposal.Count??0;
            long work=checked(committed.Count*(1152L+4L*submissions.Count+4L*subjects)),bytes=checked(512L+4L*committed.Count);
            if(grant<work||bytes>WorldWire.MaxBytes||!storage.TryReserve(bytes,out var scratch))return false;grant-=work;
            using(scratch){var retired=new int[committed.Count];int count=0;var own=domain.OwnerFor(owner.OwnerId);
                foreach(var pair in committed){bool executing=false;foreach(var s in submissions){if(s.State!=AiCombatMicroExecutionStatus.Executing||s.Input==null)continue;for(int i=0;i<s.Input.Proposal.Count;i++)if(s.Input.Proposal.Primitive(i).Binding.Entity==pair.Key){executing=true;break;}if(executing)break;}if(executing)continue;
                    if(!domain.TryReadCombatExecutionJobStamp(pair.Key,own,out var stamp)||!owner.RetainsExecutionJob(pair.Key,pair.Value.World,pair.Value.Intent,own,stamp))retired[count++]=pair.Key;
                }
                for(int i=0;i<count;i++){int entity=retired[i];var cursor=committed[entity];committed.Remove(entity);cursor.Dispose();}
            }
            return true;
        }
        internal bool ReconcileLifecycle(){if(domain==null||AwaitingNativeSources)return true;long grant=RetirementWork;return TryReconcileCommitted(ref grant);}
        void Bound(){if(disposed||domain==null)throw new InvalidOperationException("Execution issuer is not bound to a live domain.");}
        internal Source RegisterSource(IAiCombatMicroNativeExecutionSource nativeOwner,string identity,ref long grant)
        {
            Bound();long cost=checked(2048L+1024L*(sources.Count+submissions.Count));if(grant<cost)return null;grant-=cost;
            if(nativeOwner==null)throw new ArgumentNullException(nameof(nativeOwner));MotionExecutionBinding.Name(identity);
            if(AwaitingNativeSources&&!submissions.Any(s=>s.Cold!=null&&s.SourceIdentity==identity))throw new InvalidOperationException("Restore awaits its exact Native sources.");if(sources.Any(s=>s.Live&&s.Identity==identity))throw new InvalidOperationException("Native source already registered.");
            AiCombatMicroExecutionStorage.Lease floorMemory=null,memory=null;bool newFloor=!operationFloors.ContainsKey(identity);
            try{if(newFloor&&!storage.TryReserve(8192,out floorMemory))return null;if(!storage.TryReserve(2048,out memory)){floorMemory?.Dispose();return null;}var source=new Source(this,nativeOwner,identity){Memory=memory};if(newFloor)operationFloors.Add(identity,new OperationFloor(0,floorMemory));sources.Add(source);return source;}
            catch{memory?.Dispose();if(newFloor&&operationFloors.Remove(identity))floorMemory?.Dispose();else floorMemory?.Dispose();throw;}
        }
        void SourceCurrent(Source source){Bound();if(source==null||!source.Live||source.Registry!=this||!sources.Contains(source))throw new ArgumentException("Unregistered native source.");}
        internal bool TryRetainInput(Source source,IAiCombatMicroNativeInput native,ref long grant,out Input input)
        {
            input=null;long control=checked(2048L+4L*(sources.Count+submissions.Count+inputs.Count));if(grant<control)return false;grant-=control;SourceCurrent(source);if(native==null||!ReferenceEquals(native.NativeOwner,source.NativeOwner)||native.SourceIdentity!=source.Identity)throw new ArgumentException("Foreign Native input issuer.");bool restoring=submissions.Any(s=>s.Cold!=null&&s.SourceIdentity==source.Identity&&s.Cold.Length>=24&&BitConverter.ToInt64(s.Cold,16)==native.OperationId);if(AwaitingNativeSources&&!restoring||native.OperationId<=operationFloors[source.Identity].Value&&!restoring)return false;var p=native.Proposal;if(native.OperationId<1||p==null||p.World!=owner.Generation)throw new ArgumentException("Foreign Native input world.");
            long cost=32L+8L*p.Count,bytes=2048L+2048L*p.Count;for(int i=0;i<p.Count;i++){var a=p.Primitive(i);cost=checked(cost+4L*a.TraceCount+4L*(a.RemainingBranch?.Count??0));bytes=checked(bytes+1024L*a.TraceCount+1024L*(a.RemainingBranch?.Count??0));}
            if(grant<cost||!storage.TryReserve(bytes,out var root))return false;
            try{if(!native.TryRetain(out var pin)||pin==null){root.Dispose();return false;}input=new Input(this,source,p,native.OperationId,pin,root);inputs.Add(input);grant-=cost;return true;}catch{root.Dispose();throw;}
        }
        void InputCurrent(Input input){if(input==null||!input.Live||input.Registry!=this||!inputs.Contains(input))throw new ArgumentException("Unretained Native input.");SourceCurrent(input.Source);}
        bool Current(Submission s,bool after,ref long grant,out bool admitted)
        {
            admitted=false;Bound();if(s.Input==null||!s.Input.Live||!s.Input.Source.Live)return false;
            if(s.Input.NativeOperation<=operationFloors[s.SourceIdentity].Value&&!(s.State==AiCombatMicroExecutionStatus.Executing&&ReferenceEquals(s.Input.Source.InFlight,s.ActiveTicket))) {admitted=true;return false;}
            var knowledge=owner.ExecutionKnowledge;long sceneCost=checked(16L+domain.CombatExecutionBuildingCount*4L+4L*knowledge.Contacts.Count);if(grant<sceneCost)return false;grant-=sceneCost;
            admitted=true;if(!ReferenceEquals(s.Scene.Profile,owner.ExecutionProfile)||!domain.CombatExecutionSceneCurrent(owner.OwnerId,s.Scene))return false;
            int known=0;foreach(var contact in knowledge.Contacts){if(!RelevantThreat(contact))continue;if(known==s.Known.Length||!s.Known[known++].Same(contact))return false;}if(known!=s.Known.Length)return false;
            for(int i=0;i<s.Descriptors.Length;i++)if(!owner.ExecutionCurrent(domain,s.Descriptors[i],s.Input.Proposal.Primitive(i),after,storage,ref grant,out _,out admitted))return false;
            return true;
        }
        internal bool TryDescribe(int entity,ref long grant,out AiCombatMicroPolicy.ExecutionDescriptor descriptor){Bound();if(AwaitingNativeSources){descriptor=null;return false;}return owner.DescribeExecution(domain,entity,storage,ref grant,out descriptor);}
        internal bool TrySubmit(Input input,ref long preparationGrant,out Submission submission)=>TrySubmitCore(input,ref preparationGrant,out submission,false,false);
        bool TrySubmitCore(Input input,ref long preparationGrant,out Submission submission,bool afterCommit,bool rebinding)
        {
            submission=null;long control=checked(32L+4L*(sources.Count+submissions.Count+inputs.Count));if(preparationGrant<control)return false;preparationGrant-=control;InputCurrent(input);if(!rebinding&&!TryReconcileCommitted(ref preparationGrant))return false;if(AwaitingNativeSources&&!rebinding||!rebinding&&input.NativeOperation<=operationFloors[input.Source.Identity].Value)return false;if(submissions.Any(x=>ReferenceEquals(x.Input,input)||ReferenceEquals(x.Actual,input)))throw new InvalidOperationException("Native input already submitted.");var p=input.Proposal;long cost=checked(128L+16L*domain.CombatExecutionBuildingCount+32L*p.Count+4L*owner.ExecutionKnowledge.Contacts.Count+8L*p.Count*submissions.Sum(x=>x.Input?.Proposal.Count??0));if(preparationGrant<cost||!storage.TryReserve(checked(8192L+4096L*p.Count+1024L*domain.CombatExecutionBuildingCount+128L*owner.ExecutionKnowledge.Contacts.Count),out var memory))return false;
            var s=new Submission(checked(nextSubmission+1)){Input=input,SourceIdentity=input.Source.Identity,Memory=memory,State=AiCombatMicroExecutionStatus.Pending};
            try{
                preparationGrant-=cost;s.Known=owner.ExecutionKnowledge.Contacts.Where(RelevantThreat).Select(c=>new KnownThreat(c)).ToArray();s.Descriptors=new AiCombatMicroPolicy.ExecutionDescriptor[p.Count];s.Paths=new AiCombatMicroExecutionPathWork[p.Count];s.Staged=new AiCombatMicroExecutionPathWork.Cursor[p.Count];
                PlayableAiObservation observation=null;
                for(int i=0;i<p.Count;i++){
                    var primitive=p.Primitive(i);if(!owner.DescribeExecution(domain,checked((int)primitive.Binding.Entity),storage,ref preparationGrant,out var d))return Refuse(s);s.Descriptors[i]=d;
                    bool current=owner.ExecutionCurrent(domain,d,primitive,afterCommit,storage,ref preparationGrant,out var patient,out var guardAdmitted);
                    if(primitive.Binding.OwnerPolicy!=d.Identity||!current)throw new ArgumentException($"Execution proposal does not belong to the current applied policy: admitted={guardAdmitted} identity={primitive.Binding.OwnerPolicy==d.Identity} phase={d.Phase} descriptorSequence={d.Sequence} primitiveCommand={primitive.Binding.Command} primitiveGroup={primitive.Binding.Group} root={primitive.Binding.RootOrder} activation={primitive.Binding.Activation} request={primitive.Binding.Request} order={primitive.Binding.Order} tick={primitive.OwnerTick}/{domain.Tick} from={primitive.From.Position.X:R},{primitive.From.Position.Z:R}/{primitive.From.SurfaceId} heading={primitive.HeadingBefore:R}; {domain.CombatExecutionBindingReadback(primitive)}");
                    if(submissions.Any(x=>x.Input!=null&&Enumerable.Range(0,x.Input.Proposal.Count).Any(j=>x.Input.Proposal.Primitive(j).Binding.Entity==primitive.Binding.Entity)))throw new InvalidOperationException("Actor already has pending execution.");
                    s.Descriptors[i]=d;
                    if(observation==null){observation=domain.CombatExecutionInput(owner.OwnerId,owner.ExecutionSeed,patient);if(!TryScene(observation,ref preparationGrant,out s.Scene))return Refuse(s);}
                    // Each receiver gets its exact current unit; observation supplies
                    // only the common fog-safe building/known-threat input.
                    committed.TryGetValue(d.Entity,out var cursor);if(cursor!=null&&cursor.Intent!=d.Identity)cursor=null;
                    if(!AiCombatMicroExecutionPathWork.TryCreate(s.Scene,storage,primitive,observation,owner.ExecutionKnowledge,patient,d.Withdrawal,d.Identity,cursor,ref preparationGrant,out s.Paths[i]))return Refuse(s);
                }
                if(!storage.TryReserve(SubmissionBytes(p.Count,s.Scene.BuildingCount,s.Known.Length),out var canonical))return Refuse(s);s.Memory.Dispose();s.Memory=canonical;
                submissions.Add(s);nextSubmission=s.Id;submission=s;return true;
            }catch{DisposeSubmission(s);throw;}
        }
        static long SubmissionBytes(int receivers,int buildings,int known)=>checked(8192L+4096L*receivers+1024L*buildings+128L*known);
        bool RelevantThreat(Spacewars.Simulation.Ai.AiKnownContact c)=>c.Building&&domain.CombatExecutionHostile(c.Owner,owner.OwnerId)&&c.HealthAtLastSeen>0&&c.Confidence>0&&Enum.IsDefined(typeof(PlayableBuildingKind),c.Kind)&&TerritoryRules.ArmedDefense((PlayableBuildingKind)c.Kind);
        bool Refuse(Submission s){DisposeSubmission(s);return false;}
        bool TryScene(PlayableAiObservation observation,ref long grant,out AiCombatMicroCoverScene scene)=>TrySceneRecipe(owner.ExecutionProfile,observation,ref grant,out scene);
        bool TrySceneRecipe(PlayableProfile acceptedProfile,PlayableAiObservation observation,ref long grant,out AiCombatMicroCoverScene scene)
        {
            scene=null;AiCombatMicroExecutionStorage.Lease catalogRoot=null;
            try{
                if(!AiCombatSupportCatalog.TryBegin(acceptedProfile.AuthoredMap,"micro-owner-support",ref grant,b=>storage.TryReserve(b,out catalogRoot),out var builder))return false;
                while(!builder.Ready){if(grant<1)return false;grant-=builder.Advance(1);}var catalog=builder.Finish();
                if(acceptedProfile.AuthoredMap==null&&acceptedProfile.UsesRecastGroundRoutes){
                    if(!storage.TryReserve(1024,out var flatRoot))return false;catalogRoot.Dispose();catalogRoot=flatRoot;
                    double extent=acceptedProfile.ArenaHalfExtent;catalog=new AiCombatSupportCatalog(AiCombatSupportSelector.FirstDeclared,"micro-native-flat-v1",new[]{new MapSupport("flat-plane",new NavObstacle(-extent,-extent,extent,extent),false,surfaceId:NavLocation.FlatSurface)});
                }
                System.Collections.Generic.IReadOnlyList<NavObstacle> solids=observation==null?Array.Empty<NavObstacle>():acceptedProfile.AuthoredMap?.Solids;if(solids==null){long cost=4;if(grant<cost)return false;grant-=cost;solids=PlayableMap.SolidObstacles(acceptedProfile);}
                return AiCombatMicroCoverScene.TryCreate(acceptedProfile,catalog,catalogRoot,observation?.Buildings??Array.Empty<PlayableBuildingSnapshot>(),solids,ref grant,storage,out scene);
            }finally{catalogRoot?.Dispose();}
        }
        bool Owns(Submission s)=>s!=null&&submissions.Contains(s);
        internal AiCombatMicroExecutionStatus Status(Submission s){Bound();return Owns(s)?s.State:s?.State??AiCombatMicroExecutionStatus.Cancelled;}
        internal int Advance(Submission s,int grant,ref long currentGuardGrant)
        {
            if(grant<0)throw new ArgumentOutOfRangeException(nameof(grant));Bound();long control=checked(32L+4L*submissions.Count);if(currentGuardGrant<control)return 0;currentGuardGrant-=control;if(AwaitingNativeSources||!Owns(s)||s.Cold!=null||s.State==AiCombatMicroExecutionStatus.Executing)return 0;
            if(!Current(s,false,ref currentGuardGrant,out var admitted)){if(admitted)Cancel(s);return 0;}int used=0;
            while(used<grant&&s.State==AiCombatMicroExecutionStatus.Pending){
                if(s.Index==s.Paths.Length){s.State=AiCombatMicroExecutionStatus.Allowed;break;}var path=s.Paths[s.Index];
                if(!path.Complete){int n=path.Advance(grant-used);used+=n;if(n==0)break;continue;}
                if(!path.Supported||!path.Allowed){s.State=AiCombatMicroExecutionStatus.Denied;Close(s);break;}
                // Sorting/copying the actual cursor has its own actual grant.
                long copy=grant-used;if(!path.TryAcquireActualCursor(ref copy,out s.Staged[s.Index]))break;used+=checked((int)((grant-used)-copy));s.Index++;
            }
            if(s.State==AiCombatMicroExecutionStatus.Matching){int n=s.Match.Advance(grant-used);used+=n;if(s.Match.Complete){if(s.Match.Equal)s.State=AiCombatMicroExecutionStatus.Ready;else{s.State=AiCombatMicroExecutionStatus.Denied;Close(s);}}}
            return used;
        }
        internal bool TryIssue(Submission s,ref long guardGrant,out Issued issued)
        {
            issued=null;long control=checked(32L+4L*submissions.Count);if(guardGrant<control)return false;guardGrant-=control;if(AwaitingNativeSources||!Owns(s)||s.Cold!=null||s.State!=AiCombatMicroExecutionStatus.Allowed&&s.State!=AiCombatMicroExecutionStatus.Matching&&s.State!=AiCombatMicroExecutionStatus.Ready)return false;if(!Current(s,false,ref guardGrant,out var admitted)){if(admitted)Cancel(s);return false;}
            if(s.Permit==null){var value=new MotionExecutionPermit(s.Input.Proposal,checked(++nextPermit),s.Descriptors[0].Identity,"committed-actual-cursor");s.Permit=new Issued(this,s,value);}issued=s.Permit;return true;
        }
        bool IssuedCurrent(Issued issued,out Submission s){s=issued?.Submission;return issued!=null&&issued.Registry==this&&Owns(s)&&ReferenceEquals(s.Permit,issued);}
        internal bool TryPrepareActual(Issued issued,Input actual,ref long preparationGrant)
        {
            long control=checked(32L+4L*(sources.Count+inputs.Count+submissions.Count));if(preparationGrant<control)return false;preparationGrant-=control;InputCurrent(actual);if(!IssuedCurrent(issued,out var s)||s.State!=AiCombatMicroExecutionStatus.Allowed||actual.Source!=s.Input.Source||actual.NativeOperation!=s.Input.NativeOperation)return false;
            if(!AiCombatMicroExecutionMatch.TryCreate(s.Input.Proposal,actual.Proposal,s.Input.Root,actual.Root,storage,out var match))return false;s.Actual=actual;s.Match=match;s.State=AiCombatMicroExecutionStatus.Matching;return true;
        }
        internal bool TryBeginExecution(Issued issued,ref long guardGrant,out Ticket ticket)
        {ticket=null;long control=checked(32L+4L*submissions.Count);if(guardGrant<control)return false;guardGrant-=control;if(!IssuedCurrent(issued,out var s)||s.State!=AiCombatMicroExecutionStatus.Ready||s.Input.Source.InFlight!=null||!s.Actual.Live||!Current(s,false,ref guardGrant,out _))return false;if(guardGrant<2L*s.Staged.Length)return false;guardGrant-=2L*s.Staged.Length;committed.EnsureCapacity(checked(committed.Count+s.Staged.Length));s.State=AiCombatMicroExecutionStatus.Executing;ticket=new Ticket(this,s);s.ActiveTicket=ticket;s.Input.Source.InFlight=ticket;operationFloors[s.SourceIdentity].Value=s.Input.NativeOperation;return true;}
        // Called by the registered producer AFTER its atomic actual movement.
        // No public DTO or caller-supplied `committed` bool is a receipt. The
        // current Domain endpoint, source identity and once ticket authenticate it.
        internal bool TryConsumeCommitted(Source source,Ticket ticket,ref long receiptGrant,ref long guardGrant)=>TryConsumeCommittedCore(source,ticket,ref receiptGrant,ref guardGrant,false);
        internal bool TryConsumeRestoredCommitted(Source source,Ticket ticket,ref long receiptGrant,ref long guardGrant)
        {
            if(ticket?.Submission.RestoredExecuting!=true)throw new InvalidOperationException("Not a restored consume-only obligation.");
            return TryConsumeCommittedCore(source,ticket,ref receiptGrant,ref guardGrant,true);
        }
        bool TryConsumeCommittedCore(Source source,Ticket ticket,ref long receiptGrant,ref long guardGrant,bool restoredFact)
        {
            long control=checked(32L+4L*(sources.Count+submissions.Count));if(guardGrant<control)return false;guardGrant-=control;SourceCurrent(source);if(ticket==null||ticket.Registry!=this||ticket.Used||!Owns(ticket.Submission))return false;var s=ticket.Submission;
            if(s.State!=AiCombatMicroExecutionStatus.Executing||!ReferenceEquals(s.ActiveTicket,ticket)||s.Input.Source!=source||s.Actual.Source!=source||!s.Actual.Live)return false;
            if(s.Receipt==null){
                if(guardGrant<32)return false;guardGrant-=32;
                if(!source.NativeOwner.TryRetainCommitted(s.Actual.NativeOperation,out var record,out var pin))return false;
                if(record==null||pin==null){pin?.Dispose();throw new InvalidOperationException("Native committed record is not retained.");}
                if(!ReferenceEquals(record.NativeOwner,source.NativeOwner)||record.OperationId!=s.Actual.NativeOperation||record.Actual==null){pin.Dispose();throw new ArgumentException("Foreign Native committed operation.");}
                if(!AiCombatMicroExecutionMatch.TryCreate(s.Actual.Proposal,record.Actual,s.Actual.Root,s.Actual.Root,storage,out var receiptMatch)){pin.Dispose();return false;}
                s.Receipt=record;s.ReceiptPin=pin;s.ReceiptMatch=receiptMatch;
            }
            if(!s.ReceiptMatch.Complete){int work=(int)Math.Min(int.MaxValue,receiptGrant);int used=s.ReceiptMatch.Advance(work);receiptGrant-=used;if(!s.ReceiptMatch.Complete)return false;}
            if(!s.ReceiptMatch.Equal){ticket.Used=true;s.State=AiCombatMicroExecutionStatus.Denied;Close(s);return false;}
            if(restoredFact){
                // Cold COMMITTED is a factual obligation. The issuer's entire
                // transcript match and exact current pose authenticate it; a new
                // HP/core eligibility verdict cannot erase the past movement.
                long work=checked(16L*s.Actual.Proposal.Count);if(guardGrant<work)return false;guardGrant-=work;
                for(int i=0;i<s.Actual.Proposal.Count;i++){var p=s.Actual.Proposal.Primitive(i);var actor=domain.CombatExecutionActor(checked((int)p.Binding.Entity));if(actor==null||!MotionExecutionBinding.Same(actor.Location??new NavLocation(actor.Position,p.To.SurfaceId),p.To)||!MotionExecutionBinding.Same(actor.HullHeading,p.HeadingAfter))throw new InvalidOperationException("Restored committed pose differs from Native actual.");}
                committed.EnsureCapacity(checked(committed.Count+s.Staged.Length));
            }else if(!Current(s,true,ref guardGrant,out _))return false; // Preserve an unresolved ordinary component obligation.
            ticket.Used=true;
            for(int i=0;i<s.Staged.Length;i++){int entity=s.Descriptors[i].Entity;if(committed.TryGetValue(entity,out var old))old.Dispose();committed[entity]=s.Staged[i];s.Staged[i]=null;}
            s.State=AiCombatMicroExecutionStatus.Committed;Close(s);return true;
        }
        static bool UnresolvedExecution(Submission s)=>s.State==AiCombatMicroExecutionStatus.Executing||s.Cold!=null&&s.Cold.Length>=4&&(AiCombatMicroExecutionStatus)BitConverter.ToInt32(s.Cold,0)==AiCombatMicroExecutionStatus.Executing;
        internal void Cancel(Submission s){if(!Owns(s))return;if(UnresolvedExecution(s))throw new InvalidOperationException("Unresolved Native execution must be accounted before cancellation.");s.State=AiCombatMicroExecutionStatus.Cancelled;Close(s);}
        internal void Unregister(Source source){SourceCurrent(source);foreach(var pending in submissions)if(pending.SourceIdentity==source.Identity&&UnresolvedExecution(pending))throw new InvalidOperationException("Unresolved Native execution retains its registered source.");source.Live=false;foreach(var s in submissions.Where(s=>s.SourceIdentity==source.Identity).ToArray())Cancel(s);foreach(var input in inputs.Where(i=>i.Source==source).ToArray())input.Release();sources.Remove(source);source.Memory.Dispose();source.Memory=null;}
        internal void CancelInput(Input input){InputCurrent(input);foreach(var pending in submissions)if((ReferenceEquals(pending.Input,input)||ReferenceEquals(pending.Actual,input))&&UnresolvedExecution(pending))throw new InvalidOperationException("Unresolved Native execution retains its input.");foreach(var s in submissions.Where(s=>ReferenceEquals(s.Input,input)||ReferenceEquals(s.Actual,input)).ToArray())Cancel(s);input.Release();}
        void Close(Submission s){submissions.Remove(s);DisposeSubmission(s);}
        void DisposeSubmission(Submission s){if(s.Input!=null&&ReferenceEquals(s.Input.Source.InFlight,s.ActiveTicket))s.Input.Source.InFlight=null;s.ReceiptMatch?.Dispose();s.ReceiptMatch=null;s.ReceiptPin?.Dispose();s.ReceiptPin=null;s.Receipt=null;s.Match?.Dispose();s.Match=null;if(s.Descriptors!=null)foreach(var d in s.Descriptors)d?.Dispose();s.Descriptors=null;if(s.Paths!=null)foreach(var p in s.Paths)p?.Dispose();s.Paths=null;if(s.Staged!=null)foreach(var c in s.Staged)c?.Dispose();s.Staged=null;s.Scene?.Dispose();s.Scene=null;s.Memory?.Dispose();s.Memory=null;s.Input?.Release();if(s.Actual!=s.Input)s.Actual?.Release();s.Input=s.Actual=null;s.Cold=null;s.Known=null;s.ActiveTicket=null;s.Permit=null;}
        // Cold records retain exact work bytes. Fresh Native roots must explicitly
        // rebind the same source/proposal before any work or issue can continue.
        internal void Write(BinaryWriter w)
        {
            w.Write(2);w.Write(nextSubmission);w.Write(nextPermit);w.Write(operationFloors.Count);foreach(var floor in operationFloors.OrderBy(x=>x.Key,StringComparer.Ordinal)){WorldWire.String(w,floor.Key);w.Write(floor.Value.Value);}w.Write(committed.Count);foreach(var pair in committed.OrderBy(x=>x.Key)){w.Write(pair.Key);pair.Value.Write(w);}
            w.Write(submissions.Count);foreach(var s in submissions){w.Write(s.Id);WorldWire.String(w,s.SourceIdentity);if(s.Cold!=null){WorldWire.ByteArray(w,s.Cold);continue;}
                using(var counter=new CountStream())using(var measure=new BinaryWriter(counter,System.Text.Encoding.UTF8,true)){WriteSubmission(measure,s);long length=counter.Length;if(length>WorldWire.MaxBytes||!storage.TryReserve(checked(8192L+length),out var scratch))throw new InvalidOperationException("Execution codec scratch refused.");using(scratch){var bytes=new byte[checked((int)length)];using(var stream=new MemoryStream(bytes,0,bytes.Length,true,true))using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){WriteSubmission(writer,s);if(stream.Position!=length)throw new InvalidOperationException("Execution capture shape changed.");}WorldWire.ByteArray(w,bytes);}}}
        }
        sealed class CountStream:Stream
        {
            long length;public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;public override long Length=>length;public override long Position{get=>length;set=>throw new NotSupportedException();}public override void Flush(){}public override int Read(byte[] b,int o,int n)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int n){length=checked(length+n);if(length>WorldWire.MaxBytes)throw new InvalidOperationException("Execution image exceeds world ceiling.");}public override void WriteByte(byte b)=>Write(null,0,1);
        }
        void WriteSubmission(BinaryWriter w,Submission s)
        {
            w.Write((int)s.State);w.Write(s.Index);w.Write(s.Permit?.Value.PermitId??0);w.Write(s.Input.NativeOperation);AiCombatMicroExecutionBindingWire.Write(w,s.Input.Proposal);s.Scene.WriteBinding(w);w.Write(s.Known.Length);foreach(var known in s.Known)known.Write(w);w.Write(s.Descriptors.Length);
            for(int i=0;i<s.Descriptors.Length;i++){WriteDescriptor(w,s.Descriptors[i]);s.Paths[i].Write(w);w.Write(s.Staged[i]!=null);s.Staged[i]?.Write(w);}w.Write(s.Actual!=null);if(s.Actual!=null){w.Write(s.Actual.NativeOperation);AiCombatMicroExecutionBindingWire.Write(w,s.Actual.Proposal);s.Match.Write(w);}w.Write(s.Receipt!=null);if(s.Receipt!=null){AiCombatMicroExecutionBindingWire.Write(w,s.Receipt.Actual);s.ReceiptMatch.Write(w);}
        }
        static void WriteDescriptor(BinaryWriter w,AiCombatMicroPolicy.ExecutionDescriptor d){w.Write(d.Entity);w.Write(d.Tower);w.Write(d.Target);w.Write(d.Army);w.Write(d.Started);w.Write(d.Sequence);w.Write(d.Action);w.Write(d.Generation);w.Write((int)d.Phase);WorldWire.String(w,d.Identity);}
        internal static AiCombatMicroExecutionOwner Read(BinaryReader r,PlayableAiOwnerLoop owner)
        {
            if(r.ReadInt32()!=2)throw new ArgumentException("Unknown execution owner codec.");var result=new AiCombatMicroExecutionOwner(owner);try{result.nextSubmission=r.ReadInt64();result.nextPermit=r.ReadInt64();if(result.nextSubmission<0||result.nextPermit<0)throw new ArgumentException("Invalid execution allocator.");int floors=r.ReadInt32();if(floors<0||floors>WorldWire.MaxItems)throw new ArgumentException("Invalid Native operation floor count.");for(int i=0;i<floors;i++){if(!result.storage.TryReserve(8192,out var memory))throw new InvalidOperationException("Native operation floor admission refused.");try{string source=AiCombatMicroExecutionBindingWire.ReadName(r);long value=r.ReadInt64();if(value<0||result.operationFloors.ContainsKey(source))throw new ArgumentException("Invalid Native operation floor.");result.operationFloors.Add(source,new OperationFloor(value,memory));}catch{memory.Dispose();throw;}}int count=r.ReadInt32();if(count<0||count>WorldWire.MaxItems)throw new ArgumentException("Invalid committed count.");long grant=long.MaxValue;
                for(int i=0;i<count;i++){int entity=r.ReadInt32();var cursor=AiCombatMicroExecutionPathWork.Cursor.Read(r,result.storage,ref grant);if(entity!=cursor.Entity||result.committed.ContainsKey(entity)){cursor.Dispose();throw new ArgumentException("Duplicate/foreign committed actor.");}result.committed.Add(entity,cursor);}
                count=r.ReadInt32();if(count<0||count>WorldWire.MaxItems)throw new ArgumentException("Invalid pending count.");for(int i=0;i<count;i++){long id=r.ReadInt64();string source=AiCombatMicroExecutionBindingWire.ReadName(r);int length=r.ReadInt32();if(id<=0||id>result.nextSubmission||result.submissions.Any(s=>s.Id==id)||length<0||length>WorldWire.MaxBytes||!result.storage.TryReserve(checked(2048L+length),out var memory))throw new ArgumentException("Invalid pending execution record.");try{var bytes=r.ReadBytes(length);if(bytes.Length!=length)throw new EndOfStreamException();result.submissions.Add(new Submission(id){SourceIdentity=source,Cold=bytes,Memory=memory,State=AiCombatMicroExecutionStatus.AwaitingSource});}catch{memory.Dispose();throw;}}
                return result;
            }catch{result.Dispose();throw;}
        }
        PlayableProfile ReadAcceptedProfile(BinaryReader r)
        {
            long offset=r.BaseStream.Position;string id=AiCombatMicroExecutionBindingWire.ReadName(r);int revision=r.ReadInt32();r.BaseStream.Position=offset;
            if(owner.ExecutionProfile.ProfileId==id&&owner.ExecutionProfile.Revision==revision)return owner.ExecutionProfile;
            return domain.CombatExecutionAcceptedProfile(id,revision);
        }
        AiCombatMicroPolicy.ExecutionDescriptor ReadDescriptor(BinaryReader r,MotionExecutionPrimitive primitive,ref long grant)
        {
            if(grant<64||!storage.TryReserve(8192,out var memory))throw new InvalidOperationException("Saved descriptor liability refused.");grant-=64;
            try{
                int entity=r.ReadInt32(),tower=r.ReadInt32(),target=r.ReadInt32();long army=r.ReadInt64(),started=r.ReadInt64(),sequence=r.ReadInt64(),action=r.ReadInt64(),generation=r.ReadInt64();var phase=WorldWire.EnumValue<AiCombatMicroPhase>(r);string identity=AiCombatMicroExecutionBindingWire.ReadName(r);
                string expected=string.Join(":",new[]{"micro-execution",generation.ToString(System.Globalization.CultureInfo.InvariantCulture),entity.ToString(System.Globalization.CultureInfo.InvariantCulture),sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),action.ToString(System.Globalization.CultureInfo.InvariantCulture),started.ToString(System.Globalization.CultureInfo.InvariantCulture),((int)phase).ToString(System.Globalization.CultureInfo.InvariantCulture)});
                if(entity!=primitive.Binding.Entity||generation!=owner.Generation||primitive.Binding.World!=generation||primitive.Binding.Owner!=owner.OwnerId||primitive.Binding.OwnerPolicy!=identity||primitive.Binding.Command!=sequence||identity!=expected||sequence<1||action<1||army<0||tower<0||target<0||started<0||phase!=AiCombatMicroPhase.Withdraw&&phase!=AiCombatMicroPhase.Repair&&phase!=AiCombatMicroPhase.Return)throw new ArgumentException("Invalid saved applied descriptor binding.");
                return new AiCombatMicroPolicy.ExecutionDescriptor(entity,tower,target,army,started,sequence,action,generation,phase,identity,memory);
            }catch{memory.Dispose();throw;}
        }
        internal bool TryRebind(Submission s,Input input,Input actual,ref long lifecycleGrant)
        {
            InputCurrent(input);if(!Owns(s)||s.Cold==null||s.SourceIdentity!=input.Source.Identity)return false;
            // Rebind accepted immutable data, not a new live proposal. Current
            // health/orders/core/geometry may legitimately have changed since
            // acceptance; those deny future execution after restoration.
            if(lifecycleGrant<64||!storage.TryReserve(checked(8192L+4096L*input.Proposal.Count),out var initialMemory))return false;lifecycleGrant-=64;
            var fresh=new Submission(s.Id){Input=input,SourceIdentity=s.SourceIdentity,Memory=initialMemory};bool afterCommit=false;submissions.Add(fresh);
            try{
                using(var stream=new MemoryStream(s.Cold,false))using(var r=new BinaryReader(stream)){
                    var state=WorldWire.EnumValue<AiCombatMicroExecutionStatus>(r);int index=r.ReadInt32();long permit=r.ReadInt64();if(state!=AiCombatMicroExecutionStatus.Pending&&state!=AiCombatMicroExecutionStatus.Allowed&&state!=AiCombatMicroExecutionStatus.Matching&&state!=AiCombatMicroExecutionStatus.Ready&&state!=AiCombatMicroExecutionStatus.Executing||index<0||index>input.Proposal.Count||permit<0||permit>nextPermit)throw new ArgumentException("Invalid restored execution phase.");if(r.ReadInt64()!=input.NativeOperation)throw new ArgumentException("Cold Native operation mismatch.");AiCombatMicroExecutionBindingWire.Check(r,input.Proposal);
                    var acceptedProfile=ReadAcceptedProfile(r);
                    if(!TrySceneRecipe(acceptedProfile,null,ref lifecycleGrant,out var recipe))throw new InvalidOperationException("Saved profile/catalog recipe admission refused.");
                    try{fresh.Scene=AiCombatMicroCoverScene.ReadFrozen(r,recipe,storage,ref lifecycleGrant);}finally{recipe.Dispose();}
                    int knownCount=r.ReadInt32();if(knownCount<0||knownCount>WorldWire.MaxItems)throw new ArgumentException("Invalid saved known threat count.");
                    if(!storage.TryReserve(SubmissionBytes(input.Proposal.Count,fresh.Scene.BuildingCount,knownCount),out var canonical))throw new InvalidOperationException("Accepted submission liability refused.");fresh.Memory.Dispose();fresh.Memory=canonical;
                    fresh.Known=new KnownThreat[knownCount];for(int i=0;i<knownCount;i++){int id=r.ReadInt32(),kind=r.ReadInt32();var faction=WorldWire.EnumValue<PlayableOwner>(r);var position=WorldWire.ReadPoint(r);if(id<1||!Enum.IsDefined(typeof(PlayableBuildingKind),kind))throw new ArgumentException("Invalid saved static threat.");fresh.Known[i]=new KnownThreat(id,kind,faction,position);}
                    int count=r.ReadInt32();if(count!=input.Proposal.Count)throw new ArgumentException("Execution receiver mismatch.");
                    fresh.Descriptors=new AiCombatMicroPolicy.ExecutionDescriptor[count];fresh.Paths=new AiCombatMicroExecutionPathWork[count];fresh.Staged=new AiCombatMicroExecutionPathWork.Cursor[count];
                    for(int i=0;i<count;i++){
                        fresh.Descriptors[i]=ReadDescriptor(r,input.Proposal.Primitive(i),ref lifecycleGrant);
                        fresh.Paths[i]=AiCombatMicroExecutionPathWork.Read(r,fresh.Scene,storage,input.Proposal.Primitive(i),ref lifecycleGrant);if(WorldWire.Boolean(r))fresh.Staged[i]=AiCombatMicroExecutionPathWork.Cursor.Read(r,storage,ref lifecycleGrant);
                    }
                    afterCommit=state==AiCombatMicroExecutionStatus.Executing;
                    if(!afterCommit&&operationFloors[s.SourceIdentity].Value>=input.NativeOperation)throw new ArgumentException("Saved Native operation floor mismatch.");
                    bool hasActual=WorldWire.Boolean(r);if(hasActual){InputCurrent(actual);if(r.ReadInt64()!=actual.NativeOperation||actual.NativeOperation!=input.NativeOperation||actual.Source!=input.Source)throw new ArgumentException("Foreign cold actual source.");AiCombatMicroExecutionBindingWire.Check(r,actual.Proposal);fresh.Match=AiCombatMicroExecutionMatch.Read(r,input.Proposal,actual.Proposal,input.Root,actual.Root,storage);fresh.Actual=actual;}bool hasReceipt=WorldWire.Boolean(r);if(hasReceipt){if(state!=AiCombatMicroExecutionStatus.Executing||!input.Source.NativeOwner.TryRetainCommitted(actual.NativeOperation,out var receipt,out var pin)||receipt==null||pin==null)throw new InvalidOperationException("Native cold committed record not rebound.");fresh.Receipt=receipt;fresh.ReceiptPin=pin;if(!ReferenceEquals(receipt.NativeOwner,input.Source.NativeOwner)||receipt.OperationId!=actual.NativeOperation)throw new ArgumentException("Foreign cold committed operation.");AiCombatMicroExecutionBindingWire.Check(r,receipt.Actual);fresh.ReceiptMatch=AiCombatMicroExecutionMatch.Read(r,actual.Proposal,receipt.Actual,actual.Root,actual.Root,storage);}
                    if(stream.Position!=stream.Length||hasActual!=(state==AiCombatMicroExecutionStatus.Matching||state==AiCombatMicroExecutionStatus.Ready||state==AiCombatMicroExecutionStatus.Executing))throw new ArgumentException("Invalid execution continuation tail.");
                    for(int i=0;i<count;i++){bool staged=fresh.Staged[i]!=null;if(staged!=(i<index)||staged&&(!fresh.Paths[i].Complete||!fresh.Paths[i].Supported||!fresh.Paths[i].Allowed)||staged&&(fresh.Staged[i].Entity!=fresh.Descriptors[i].Entity||fresh.Staged[i].Intent!=fresh.Descriptors[i].Identity||!MotionExecutionBinding.Same(fresh.Staged[i].At,input.Proposal.Primitive(i).To)))throw new ArgumentException("Impossible staged execution prefix.");}if(state!=AiCombatMicroExecutionStatus.Pending&&index!=count||(state==AiCombatMicroExecutionStatus.Ready||state==AiCombatMicroExecutionStatus.Executing)&&(!fresh.Match.Complete||!fresh.Match.Equal))throw new ArgumentException("Unproven restored issue phase.");fresh.State=state;fresh.Index=index;if(permit>0)fresh.Permit=new Issued(this,fresh,new MotionExecutionPermit(input.Proposal,permit,fresh.Descriptors[0].Identity,"committed-actual-cursor"));
                }
                // All fallible checks/births precede ownership transfer. A failed
                // cold candidate is not an executing authority and may be discarded.
                Issued restoredPermit=fresh.Permit==null?null:new Issued(this,s,fresh.Permit.Value);Ticket restoredTicket=null;
                if(fresh.State==AiCombatMicroExecutionStatus.Executing){if(input.Source.InFlight!=null||operationFloors[s.SourceIdentity].Value!=input.NativeOperation)throw new ArgumentException("Duplicated cold Native execution.");restoredTicket=new Ticket(this,s);}
                submissions.Remove(fresh);var oldMemory=s.Memory;s.Memory=fresh.Memory;fresh.Memory=null;s.Input=fresh.Input;s.Actual=fresh.Actual;s.Known=fresh.Known;s.Descriptors=fresh.Descriptors;s.Paths=fresh.Paths;s.Staged=fresh.Staged;s.Scene=fresh.Scene;s.Match=fresh.Match;s.Receipt=fresh.Receipt;s.ReceiptPin=fresh.ReceiptPin;s.ReceiptMatch=fresh.ReceiptMatch;s.State=fresh.State;s.Index=fresh.Index;s.RestoredExecuting=afterCommit;s.Cold=null;s.Permit=restoredPermit;s.ActiveTicket=restoredTicket;if(restoredTicket!=null)input.Source.InFlight=restoredTicket;oldMemory.Dispose();return true;
            }catch(Exception error){try{DiscardUnpublishedCandidate(fresh);}catch(Exception cleanup){PlayableDomain.PreserveTacticalSecondary(error,cleanup);}throw;}
        }
        void DiscardUnpublishedCandidate(Submission candidate)
        {if(!Owns(candidate))return;candidate.State=AiCombatMicroExecutionStatus.Cancelled;Close(candidate);}
        internal bool TryResumeCommitted(Source source,Submission submission,out Ticket ticket){SourceCurrent(source);ticket=null;if(!Owns(submission)||submission.Cold!=null||submission.State!=AiCombatMicroExecutionStatus.Executing||submission.Input.Source!=source)return false;ticket=submission.ActiveTicket;return ticket!=null;}
        internal Submission AwaitingSource(string identity,long id)=>submissions.FirstOrDefault(s=>s.SourceIdentity==identity&&s.Id==id&&s.Cold!=null);
        public void Dispose(){if(disposed)return;disposed=true;foreach(var s in submissions.ToArray())DisposeSubmission(s);submissions.Clear();foreach(var cursor in committed.Values)cursor.Dispose();committed.Clear();foreach(var input in inputs.ToArray())input.Release();foreach(var source in sources){source.Live=false;source.Memory.Dispose();source.Memory=null;}sources.Clear();foreach(var floor in operationFloors.Values)floor.Memory.Dispose();operationFloors.Clear();domain=null;}
    }
    internal sealed partial class PlayableAiOwnerLoop
    {
        AiCombatMicroExecutionOwner executionOwner;bool executionRevoked,executionReviewClone;
        internal PlayableProfile ExecutionProfile=>profile;
        internal int ExecutionSeed=>opening.MatchSeed;
        internal Spacewars.Simulation.Ai.AiKnowledgeState ExecutionKnowledge=>knowledge.Capture();
        internal bool AwaitingExecutionSources=>executionOwner?.AwaitingNativeSources??false;
        internal AiCombatMicroExecutionOwner ExecutionOwner{get{if(executionRevoked||executionReviewClone||executionOwner==null)throw new InvalidOperationException("Owner has no live execution authority.");return executionOwner;}}
        internal void BindExecutionDomain(PlayableDomain domain){if(executionRevoked||executionReviewClone)throw new InvalidOperationException("Former owner execution authority was transferred.");if(!domain.Authorizes(OwnerId)){ReleaseExecutionAuthority();return;}if(executionOwner==null)executionOwner=new AiCombatMicroExecutionOwner(this);executionOwner.Bind(this,domain);}
        void ReleaseExecutionAuthority(){executionOwner?.Dispose();executionOwner=null;}
        internal void AdoptExecutionAuthority(PlayableAiOwnerLoop original,PlayableDomain domain){executionReviewClone=false;executionOwner?.Dispose();executionOwner=original.executionOwner;original.executionOwner=null;original.executionRevoked=true;if(executionOwner==null)executionOwner=new AiCombatMicroExecutionOwner(this);executionOwner.Bind(this,domain);}
        internal bool DescribeExecution(PlayableDomain domain,int entity,AiCombatMicroExecutionStorage storage,ref long grant,out AiCombatMicroPolicy.ExecutionDescriptor descriptor)=>combatMicro.TryDescribeExecution(domain,OwnerId,profile,nativeProfile,nativeConfig.Difficulty,armies,entity,storage,ref grant,out descriptor);
        internal bool ExecutionCurrent(PlayableDomain domain,AiCombatMicroPolicy.ExecutionDescriptor descriptor,MotionExecutionPrimitive primitive,bool committed,AiCombatMicroExecutionStorage storage,ref long grant,out PlayableEntitySnapshot patient,out bool admitted)=>combatMicro.ExecutionCurrent(domain,OwnerId,profile,nativeProfile,nativeConfig.Difficulty,armies,descriptor,primitive,committed,storage,ref grant,out patient,out admitted);
        internal bool RetainsExecutionJob(int entity,long generation,string identity,PlayableOwner owner,PlayableOrderStamp stamp)=>combatMicro.RetainsExecutionJob(entity,generation,identity,OwnerId,owner,stamp);
        void WriteExecutionState(BinaryWriter w){if(executionOwner!=null&&!executionReviewClone&&!executionOwner.ReconcileLifecycle())throw new InvalidOperationException("Execution retirement capture admission refused.");w.Write(executionOwner!=null);executionOwner?.Write(w);}
        void ReadExecutionState(BinaryReader r){if(WorldWire.Boolean(r))executionOwner=AiCombatMicroExecutionOwner.Read(r,this);}
    }
}
