using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Domain-thread consumer of the complete owning Native publication. Ordinary
    // receivers never acquire micro policy merely by sharing a movement frame.
    internal sealed class AiCombatMicroMotionAuthority : INavigationMotionAuthority
    {
        readonly PlayableDomain domain;
        readonly Func<IReadOnlyList<PlayableAiOwnerLoop>> currentOwners;
        readonly AiCombatMicroExecutionStorage storage=new AiCombatMicroExecutionStorage();
        readonly Dictionary<AiCombatMicroExecutionOwner,AiCombatMicroExecutionOwner.Source> sources=new Dictionary<AiCombatMicroExecutionOwner,AiCombatMicroExecutionOwner.Source>();
        Adoption pending;
        readonly List<Adoption> cleanup=new List<Adoption>();
        readonly Dictionary<AiCombatMicroExecutionOwner,Issuer> issuers=new Dictionary<AiCombatMicroExecutionOwner,Issuer>();
        internal long Publications{get;private set;}
        internal long AffectedPublications{get;private set;}
        internal bool HasPending=>pending!=null;
        [NonSerialized] internal Func<int,int> WorkGrantForTests;
        internal string LastRefusal{get;private set;}
        internal AiCombatMicroMotionAuthority(PlayableDomain domain,Func<IReadOnlyList<PlayableAiOwnerLoop>> owners)
        {this.domain=domain;currentOwners=owners;}
        sealed class Issuer : IAiCombatMicroNativeExecutionSource
        {
            readonly NavigationSession session;
            internal readonly Dictionary<long,NativeInput> Accepted=new Dictionary<long,NativeInput>();
            internal Issuer(NavigationSession session){this.session=session;}
            public bool TryRetainCommitted(long operation,out IAiCombatMicroNativeCommittedRecord record,out IDisposable retention)
            {
                record=null;retention=null;if(!Accepted.TryGetValue(operation,out var input)||!session.TryRetainMotionSource(operation,true,out var pin))return false;
                if(!SameTranscript(input.Pin.Proposal,pin.CommittedTranscript)){pin.Dispose();throw new InvalidOperationException("Native committed transcript differs from its accepted input.");}
                record=new Committed(this,operation,input.Proposal);retention=pin;return true;
            }
            sealed class Committed : IAiCombatMicroNativeCommittedRecord
            {
                public IAiCombatMicroNativeExecutionSource NativeOwner{get;} public long OperationId{get;} public MotionExecutionProposal Actual{get;}
                internal Committed(Issuer owner,long operation,MotionExecutionProposal actual){NativeOwner=owner;OperationId=operation;Actual=actual;}
            }
        }
        sealed class PinLease : IDisposable
        {
            NavigationSession.MotionSourcePin pin;
            internal PinLease(NavigationSession.MotionSourcePin pin){this.pin=pin;pin.Retain();}
            public void Dispose(){var old=pin;pin=null;old?.Release();}
        }
        sealed class NativeInput : IAiCombatMicroNativeInput
        {
            internal readonly NavigationSession.MotionSourcePin Pin;
            public IAiCombatMicroNativeExecutionSource NativeOwner{get;}
            public long OperationId=>Pin.Operation;
            public string SourceIdentity=>NavigationSession.GroundMotionSourceIdentity;
            public MotionExecutionProposal Proposal{get;}
            internal NativeInput(Issuer issuer,NavigationSession.MotionSourcePin pin,MotionExecutionProposal proposal){NativeOwner=issuer;Pin=pin;Proposal=proposal;}
            public bool TryRetain(out IDisposable retention){retention=null;if(!Pin.IsLive)return false;retention=new PinLease(Pin);return true;}
        }
        sealed class OwnerRow
        {
            internal PlayableAiOwnerLoop Owner;
            internal AiCombatMicroExecutionOwner Registry;
            internal Issuer Issuer;
            internal AiCombatMicroExecutionOwner.Input Input,Actual;
            internal AiCombatMicroExecutionOwner.Submission Submission;
            internal AiCombatMicroExecutionOwner.PreparedInstall Install;
        }
        sealed class Adoption : NavigationMotionAdoption
        {
            internal readonly AiCombatMicroMotionAuthority Authority;
            internal NavigationSession.MotionPublication Publication;
            internal bool KeepPending;internal long Tick;internal AiCombatMicroExecutionStorage.Lease Memory;
            internal NavigationSession.MotionSourcePin Pin;
            internal readonly List<OwnerRow> Rows=new List<OwnerRow>();
            internal readonly List<IDisposable> Resources=new List<IDisposable>();
            internal Exception Failure;bool installed,resourcesReleased,disposed;
            internal Adoption(AiCombatMicroMotionAuthority authority,NavigationSession.MotionPublication publication){Authority=authority;Publication=publication;Tick=authority.domain.Tick;}
            internal override void ValidateCurrent()
            {
                if(Failure!=null)throw new InvalidOperationException("Micro preadmission refused.",Failure);
                if(!SameTranscript(Pin.Proposal,Publication.Transcript))throw new InvalidOperationException("Complete Native motion changed.");
                foreach(var owner in Authority.currentOwners()){
                    if(!Authority.domain.Authorizes(owner.OwnerId))continue;
                    var affected=Authority.Affected(owner,Publication.Transcript);var row=Rows.FirstOrDefault(r=>ReferenceEquals(r.Registry,owner.ExecutionOwner));
                    if(affected.Count!=(row?.Input.Proposal.Count??0))throw new InvalidOperationException("Current whole receiver classification changed.");
                    for(int i=0;i<affected.Count;i++)if(row.Input.Proposal.Primitive(i).Binding.Entity!=Publication.Transcript[affected[i]].Before.Entity)throw new InvalidOperationException("Current micro receiver routing changed.");
                }
                long guard=long.MaxValue;
                foreach(var row in Rows)row.Install.Validate(ref guard);
            }
            internal override void ApplyCommittedNoThrow()
            {
                foreach(var row in Rows){row.Install.EnterNoThrow();row.Install.ApplyNoThrow();}
                installed=true;KeepPending=false;Authority.pending=null;Authority.Publications++;if(Rows.Count>0)Authority.AffectedPublications++;
            }
            public override void Dispose()
            {
                if(KeepPending)return;
                if(!Authority.cleanup.Contains(this))Authority.cleanup.Add(this);
                if(TryCleanup())Authority.cleanup.Remove(this);
            }
            internal bool TryCleanup()
            {
                if(disposed)return true;
                if(!resourcesReleased){
                    foreach(var row in Rows){
                        row.Install?.Dispose();row.Install=null;
                        if(!installed){if(row.Submission!=null)row.Registry.Cancel(row.Submission);if(row.Actual?.Live==true)row.Registry.CancelInput(row.Actual);if(row.Input?.Live==true)row.Registry.CancelInput(row.Input);}
                        if(Pin!=null)row.Issuer?.Accepted.Remove(Pin.Operation);
                    }
                    foreach(var resource in Resources)resource.Dispose();Resources.Clear();Memory?.Dispose();Memory=null;resourcesReleased=true;
                }
                if(Pin!=null){if(!Authority.domain.Navigation.TryRetireMotionSource(Pin))return false;Pin.Dispose();Pin=null;}
                disposed=true;return true;
            }
        }
        List<int> Affected(PlayableAiOwnerLoop owner,NavMotionTranscript transcript)
        {
            var affected=new List<int>();
            for(int i=0;i<transcript.Count;i++){
                var receiver=transcript[i];var actor=domain.CombatExecutionActor(receiver.Before.Entity);
                if(actor==null||actor.Health<=0||actor.Held||actor.Owner!=domain.OwnerFor(owner.OwnerId)||actor.Kind==PlayableEntityKind.Hawk||actor.OrderStamp?.Origin==PlayableOrderOrigin.Human||!owner.ReservesExecutionSubject(actor.Id))continue;
                // Pending route installation and an idle micro client do not
                // authorize future travel. A publication with no physical pose
                // delta needs no escape-cursor transition; the first actual
                // translation/turn is independently admitted with its full path.
                if(Point(receiver.Before.Position,receiver.After.Position)&&MotionExecutionBinding.Same(receiver.Before.Location,receiver.After.Location)&&MotionExecutionBinding.Same(receiver.Before.Heading,receiver.After.Heading)&&receiver.Sweep.PortalCount==0&&!receiver.Chord.Any(leg=>leg.Kind==NavTypedLegKind.Portal))continue;
                if(owner.RetainsCurrentExecutionStamp(domain,actor.Id))affected.Add(i);
            }
            return affected;
        }
        AiCombatMicroExecutionOwner.Source Source(OwnerRow row,ref long grant)
        {
            if(!issuers.TryGetValue(row.Registry,out var issuer)){issuer=new Issuer(domain.Navigation);issuers.Add(row.Registry,issuer);}row.Issuer=issuer;
            if(!sources.TryGetValue(row.Registry,out var source)){source=row.Registry.RegisterSource(issuer,NavigationSession.GroundMotionSourceIdentity,ref grant)??throw new InvalidOperationException("Micro source admission refused.");sources.Add(row.Registry,source);}return source;
        }
        public NavigationMotionAdoption Prepare(NavigationSession.MotionPublication publication)
        {
            if(pending!=null&&!SameTranscript(pending.Pin.Proposal,publication.Transcript)){
                pending.KeepPending=false;cleanup.Add(pending);pending=null;
            }
            var adoption=pending??new Adoption(this,publication);adoption.Publication=publication;adoption.Failure=null;
            try{
                if(adoption.Pin==null){
                    adoption.Pin=publication.AcceptSource();
                    if(!storage.TryReserve(checked(8192L+2048L*publication.Transcript.Count),out adoption.Memory))throw new InvalidOperationException("Whole receiver classification admission refused.");
                    if(!SameTranscript(adoption.Pin.Proposal,publication.Transcript))throw new InvalidOperationException("Incomplete Native source match.");
                    foreach(var owner in currentOwners()){
                        if(!domain.Authorizes(owner.OwnerId))continue;
                        var affected=Affected(owner,publication.Transcript);if(affected.Count==0)continue;
                        var row=new OwnerRow{Owner=owner,Registry=owner.ExecutionOwner};adoption.Rows.Add(row);long grant=long.MaxValue;var source=Source(row,ref grant);
                        var proposal=BuildProposal(owner,row.Registry,adoption.Pin.Proposal,affected,adoption.Tick,adoption.Resources);
                        var input=new NativeInput(row.Issuer,adoption.Pin,proposal);row.Issuer.Accepted.Add(adoption.Pin.Operation,input);
                        if(!row.Registry.TryRetainInput(source,input,ref grant,out row.Input)||!row.Registry.TrySubmit(row.Input,ref grant,out row.Submission))throw new InvalidOperationException("Micro proposal admission refused.");
                    }
                }else publication.SelectSource(adoption.Pin);
                // A bounded private AI work quantum; continuation is retained in
                // the existing paid Runtime submission/current codec. It borrows
                // neither Nav solver grants nor owner action/APM tokens.
                int remaining=WorkGrantForTests?.Invoke(4096)??4096;if(remaining<0||remaining>4096)throw new InvalidOperationException("Invalid test work admission.");bool ready=true;
                foreach(var row in adoption.Rows){
                    long grant=long.MaxValue;var registry=row.Registry;var state=registry.Status(row.Submission);
                    if(state==AiCombatMicroExecutionStatus.Pending&&remaining>0){int used=registry.Advance(row.Submission,remaining,ref grant);remaining-=used;state=registry.Status(row.Submission);}
                    if(state==AiCombatMicroExecutionStatus.Pending){ready=false;continue;}
                    if(!registry.TryIssue(row.Submission,ref grant,out var issued))throw new InvalidOperationException("Actual micro path denied or stale.");
                    if(state==AiCombatMicroExecutionStatus.Allowed){
                        var affected=Affected(row.Owner,publication.Transcript);
                        var actual=BuildProposal(row.Owner,registry,publication.Transcript,affected,adoption.Tick,adoption.Resources);
                        var source=Source(row,ref grant);
                        if(!registry.TryRetainInput(source,new NativeInput(row.Issuer,adoption.Pin,actual),ref grant,out row.Actual)||!registry.TryPrepareActual(issued,row.Actual,ref grant))throw new InvalidOperationException("Micro actual match admission refused.");state=registry.Status(row.Submission);
                    }
                    if(state==AiCombatMicroExecutionStatus.Matching&&remaining>0){int used=registry.Advance(row.Submission,remaining,ref grant);remaining-=used;state=registry.Status(row.Submission);}
                    if(state==AiCombatMicroExecutionStatus.Matching){ready=false;continue;}
                    if(state!=AiCombatMicroExecutionStatus.Ready)throw new InvalidOperationException("Actual whole motion values differ.");
                    if(row.Install==null)row.Install=registry.PrepareInstall(issued,ref grant)??throw new InvalidOperationException("Micro finalization admission refused.");
                }
                adoption.KeepPending=!ready;
                if(!ready){pending=adoption;adoption.Failure=new InvalidOperationException("Paid whole micro work remains pending.");}
                else{pending=null;LastRefusal=null;}
            }catch(Exception error){LastRefusal=error.ToString();adoption.Failure=error;adoption.KeepPending=false;pending=null;}
            // Deferred retirement/cancellation occurs only after Native closes
            // its publication phase, never from this policy callback.
            return adoption;
        }
        // This runs before normal Domain/Native clocks and command delivery.
        // The cold Runtime operation IDs supply the only Native lookup keys.
        internal void ServiceSources()
        {
            for(int i=cleanup.Count-1;i>=0;i--)if(cleanup[i].TryCleanup())cleanup.RemoveAt(i);
            foreach(var owner in currentOwners()){
                if(!domain.Authorizes(owner.OwnerId))continue;owner.BindExecutionDomain(domain);var registry=owner.ExecutionOwner;
                if(pending!=null)foreach(var row in pending.Rows)if(ReferenceEquals(row.Registry,registry))row.Owner=owner;
                while(registry.TryGetColdBinding(out var submission,out long operation,out long tick,out var state)){
                    if(pending!=null&&pending.Pin.Operation!=operation)throw new InvalidOperationException("Current aggregate contains different Native operations.");
                    if(pending==null){
                        if(!domain.Navigation.TryRetainMotionSource(operation,false,out var pin))throw new InvalidOperationException("Current Native motion source awaits owning rebind.");
                        pending=new Adoption(this,null){Pin=pin,Tick=tick,KeepPending=true};
                        if(!storage.TryReserve(checked(8192L+2048L*pin.Proposal.Count),out pending.Memory))throw new InvalidOperationException("Current aggregate liability refused.");
                    }
                    var row=new OwnerRow{Owner=owner,Registry=registry,Submission=submission};pending.Rows.Add(row);long grant=long.MaxValue;var source=Source(row,ref grant);
                    if(!storage.TryBegin(Math.Min(WorldWire.MaxItems,checked(8*pending.Pin.Proposal.Count+8)),out var snapshot))throw new InvalidOperationException("Saved value factory admission refused.");
                    MotionExecutionProposal proposal;
                    using(snapshot){
                        proposal=registry.ReadAcceptedSnapshot(submission,ref grant,snapshot,out var profile);
                        var indices=new List<int>();for(int i=0;i<proposal.Count;i++){
                            int found=-1;for(int j=0;j<pending.Pin.Proposal.Count;j++)if(pending.Pin.Proposal[j].Before.Entity==proposal.Primitive(i).Binding.Entity){found=j;break;}
                            if(found<0)throw new ArgumentException("Saved receiver is absent from the accepted Native graph.");indices.Add(found);
                        }
                        var projectionRoots=new List<IDisposable>();
                        try{var projection=BuildProposal(owner,registry,pending.Pin.Proposal,indices,tick,projectionRoots,proposal,profile);registry.CheckAcceptedSnapshot(submission,projection);}finally{foreach(var root in projectionRoots)root.Dispose();}
                        pending.Resources.Add(snapshot.Transfer());
                    }
                    var native=new NativeInput(row.Issuer,pending.Pin,proposal);row.Issuer.Accepted.Add(operation,native);
                    if(!registry.TryRetainInput(source,native,ref grant,out row.Input))throw new InvalidOperationException("Current input pin rebind refused.");
                    if(state==AiCombatMicroExecutionStatus.Matching||state==AiCombatMicroExecutionStatus.Ready||state==AiCombatMicroExecutionStatus.Executing){
                        // Production Matching starts only after the complete
                        // Native actual transcript equals this accepted source.
                        // TryRebind additionally compares every saved actual byte.
                        if(!registry.TryRetainInput(source,new NativeInput(row.Issuer,pending.Pin,proposal),ref grant,out row.Actual))throw new InvalidOperationException("Current actual pin rebind refused.");
                    }
                    if(!registry.TryRebind(submission,row.Input,row.Actual,ref grant))throw new InvalidOperationException("Current Runtime live liability awaits rebind.");
                    if(state==AiCombatMicroExecutionStatus.Executing){
                        if(pending.Pin.CommittedTranscript==null||!SameTranscript(pending.Pin.Proposal,pending.Pin.CommittedTranscript)||!registry.TryResumeCommitted(source,submission,out var ticket)||!registry.TryConsumeRestoredCommitted(source,ticket,ref grant,ref grant))throw new InvalidOperationException("Current Native committed fact awaits consume-only adoption.");
                    }
                }
                // The separate Air source rebind may still be outstanding.
                // Domain checks the combined Runtime gate after both phases.
            }
            if(pending!=null&&pending.Pin.CommittedTranscript!=null){pending.KeepPending=false;cleanup.Add(pending);pending=null;for(int i=cleanup.Count-1;i>=0;i--)if(cleanup[i].TryCleanup())cleanup.RemoveAt(i);}
            if(domain.Navigation.AwaitingMotionSources)throw new InvalidOperationException("Native source liabilities remain unbound before clock.");
        }
        internal void BeforeCapture()
        {for(int i=cleanup.Count-1;i>=0;i--)if(cleanup[i].TryCleanup())cleanup.RemoveAt(i);if(cleanup.Count!=0)throw new InvalidOperationException("Committed Native source retirement remains pending before capture.");}
        internal void Stop()
        {
            if(pending!=null){pending.KeepPending=false;cleanup.Add(pending);pending=null;}
            for(int i=cleanup.Count-1;i>=0;i--)if(cleanup[i].TryCleanup())cleanup.RemoveAt(i);
            if(cleanup.Count!=0)throw new InvalidOperationException("Native source release admission remains pending at stop.");
        }
        MotionExecutionProposal BuildProposal(PlayableAiOwnerLoop owner,AiCombatMicroExecutionOwner registry,NavMotionTranscript transcript,List<int> affected,long tick,List<IDisposable> resources,MotionExecutionProposal accepted=null,PlayableProfile acceptedProfile=null)
        {
            if(!storage.TryBegin(checked(4*affected.Count+1),out var transaction))throw new InvalidOperationException("Motion value storage refused.");
            using(transaction){
                long grant=long.MaxValue;var profile=acceptedProfile??owner.ExecutionProfile;
                long scratch=checked(8192L+1024L*affected.Count);foreach(int index in affected)scratch=checked(scratch+512L*(transcript[index].Chord.Count+transcript[index].Remaining.Count));
                if(!transaction.TryReserve(scratch))throw new InvalidOperationException("Motion value scratch refused.");
                var primitives=new MotionExecutionPrimitive[affected.Count];
                for(int j=0;j<affected.Count;j++){
                    var r=transcript[affected[j]];var before=r.Before;var after=r.After;
                    if(r.Continuation!=NavMotionContinuation.Ready||r.AcceptedRoute==null||r.ProductionException)throw new InvalidOperationException($"Affected motion lacks a complete certified continuation: entity={before.Entity} continuation={r.Continuation} route={r.AcceptedRoute!=null} production={r.ProductionException} from={before.Position} to={after.Position} heading={before.Heading:R}->{after.Heading:R} chord={r.Chord.Count} suffix={r.Remaining.Count} request={before.Request}->{after.Request}.");
                    AiCombatMicroPolicy.ExecutionDescriptor descriptor=null;
                    if(accepted==null&&!registry.TryDescribe(before.Entity,ref grant,out descriptor))throw new InvalidOperationException("Stale applied micro receiver.");
                    using(descriptor){
                        var s=r.AcceptedRoute.Subscription;
                        // Membership is a current group revision, distinct from
                        // this route's activation/order revision.
                        long membership=0;
                        if(accepted==null&&s.GroupId!=0){
                            if(!transaction.TryReserve(checked(8192L+domain.CombatExecutionUnitCount*(32L+NavStorageLayout.ObjectBytesBound(typeof(GroupMemberState))))))throw new InvalidOperationException("Current group binding snapshot refused.");
                            var group=domain.Navigation.GroupFor(before.Entity);
                            if(group==null||group.GroupId!=s.GroupId)throw new InvalidOperationException("Accepted route no longer belongs to the current group.");
                            membership=group.MembershipRevision;
                        }
                        var binding=accepted?.Primitive(j).Binding??new MotionExecutionBinding(transcript.Generation,s.GroupId,membership,s.RootOrderRevision,s.CommandSequence,before.Entity,before.Incarnation,before.Mobility,s.RequestSequence,after.Order,s.ActivationRevision,owner.OwnerId,s.StableKey,profile.ProfileId,"geometry:"+domain.Geometry.Revision,s.HoldBinding,descriptor.Identity,s.GroupId==0?MotionOwnIntentKind.Standalone:MotionOwnIntentKind.Grouped);
                        if(accepted!=null&&(binding.World!=transcript.Generation||binding.Owner!=owner.OwnerId||binding.Entity!=before.Entity||binding.Incarnation!=before.Incarnation||binding.Mobility!=before.Mobility||binding.Group!=s.GroupId||binding.RootOrder!=s.RootOrderRevision||binding.Command!=s.CommandSequence||binding.Request!=s.RequestSequence||binding.Order!=after.Order||binding.Activation!=s.ActivationRevision||binding.Route!=s.StableKey||binding.Profile!=profile.ProfileId||binding.Hold!=s.HoldBinding))throw new ArgumentException("Saved identity differs from its owning Native accepted source.");
                        var pieces=new List<MotionExecutionTracePiece>(r.Chord.Count+1);var provider=r.AcceptedRoute.Legs.Count==0?null:r.AcceptedRoute.Legs[0].RecastAuthorityProvider;
                        int surfaces=Math.Max(1,r.Chord.Count(l=>l.Kind==NavTypedLegKind.Surface)),surfaceIndex=0;double cut=0;
                        for(int k=0;k<Math.Max(1,r.Chord.Count);k++){
                            var leg=r.Chord.Count==0?null:r.Chord[k];var from=leg?.From??before.Location;var to=leg?.To??after.Location;
                            if(leg?.Kind==NavTypedLegKind.Portal){pieces.Add(new MotionExecutionTracePiece(MotionExecutionTraceKind.Portal,MotionExecutionLaw.SemanticPortalCrossing,from,to,null,0,0,0,cut,cut,leg.PortalId));continue;}
                            if(from.SurfaceId!=to.SurfaceId)throw new InvalidOperationException("Missing exact Native portal in ground chord.");
                            string support;double hx,hz,hc;Support(profile,provider,from,to,ref grant,out support,out hx,out hz,out hc);double next=++surfaceIndex/(double)surfaces;
                            pieces.Add(new MotionExecutionTracePiece(MotionExecutionTraceKind.Surface,MotionExecutionLaw.AffineSupportChord,from,to,support,hx,hz,hc,cut,next));cut=next;
                        }
                        if(surfaceIndex==0){string support;double hx,hz,hc;Support(profile,provider,after.Location,after.Location,ref grant,out support,out hx,out hz,out hc);pieces.Add(new MotionExecutionTracePiece(MotionExecutionTraceKind.Surface,MotionExecutionLaw.AffineSupportChord,after.Location,after.Location,support,hx,hz,hc,0,1));}
                        // The DTO branch begins at primitive.From. Preserve BOTH
                        // actual chord and every certified remaining suffix leg.
                        // This explicit detached path starts at leg0/progress0;
                        // the real motor cursor remains fully matched in Sweep.
                        // The controller is the whole Native frame (0), distinct
                        // from each receiver's own route/group binding.
                        var full=new List<NavTypedLeg>(r.Chord.Count+r.Remaining.Count);full.AddRange(r.Chord);full.AddRange(r.Remaining);
                        if(!MotionExecutionRemainingBranch.TryCreate(binding,before.Location,after.Endpoint.Value,0,0,full,ref grant,transaction.TryReserve,out var branch))throw new InvalidOperationException("Whole remaining branch admission refused.");
                        var kind=r.Cooperative?MotionExecutionKind.CooperativeYield:before.Position.Equals(after.Position)?MotionExecutionKind.PureTurnWait:MotionExecutionKind.OwnRoute;
                        if(!MotionExecutionPrimitive.TryCreate(binding,affected[j]+1,kind,tick,1d/30d,before.Location,after.Location,after.Endpoint,before.Radius,"ground",before.Heading,after.Heading,0,0,s.RequestSequence,pieces,ref grant,transaction.TryReserve,out primitives[j],branch))throw new InvalidOperationException("Motion primitive admission refused.");
                    }
                }
                if(!MotionExecutionProposal.TryCreate(transcript.Generation,0,0,transcript.Operation,tick,primitives,ref grant,transaction.TryReserve,out var result))throw new InvalidOperationException("Motion bundle admission refused.");resources.Add(transaction.Transfer());return result;
            }
        }
        static void Support(PlayableProfile p,ILayeredNavigationProvider provider,NavLocation from,NavLocation to,ref long preparation,out string id,out double x,out double z,out double constant)
        {
            long cost=checked(4L+(provider is PartitionNavigationProvider actual?actual.SupportLawCount:0));if(preparation<cost)throw new InvalidOperationException("Native law value preparation admission refused.");preparation-=cost;
            if(p.AuthoredMap==null&&p.UsesRecastGroundRoutes&&from.SurfaceId==NavLocation.FlatSurface&&to.SurfaceId==NavLocation.FlatSurface){id="flat-plane";x=z=constant=0;return;}
            // Borrow only the actual accepted source's immutable laws. This is an
            // identity projection, not polygon/overlap selection; the paid span
            // cursor proves complete ownership before any factual publication.
            var partition=provider as PartitionNavigationProvider;MapSupport selected=null;
            if(partition!=null)for(int i=0;i<partition.SupportLawCount;i++){
                var support=partition.SupportLawAt(i);if(support.SurfaceId!=from.SurfaceId)continue;
                if(selected!=null)throw new InvalidOperationException("Ambiguous retained Native support law.");selected=support;
            }
            if(selected==null)throw new InvalidOperationException("Unknown retained Native support law.");id=selected.Id;x=selected.Gradient.X;z=selected.Gradient.Z;constant=selected.Height-selected.Origin.X*x-selected.Origin.Z*z;
        }
        static bool Point(NavPoint a,NavPoint b)=>MotionExecutionBinding.Same(a.X,b.X)&&MotionExecutionBinding.Same(a.Z,b.Z);
        static bool State(NavMotionActorState a,NavMotionActorState b)=>a.Entity==b.Entity&&a.Team==b.Team&&a.Incarnation==b.Incarnation&&a.Mobility==b.Mobility&&a.Group==b.Group&&a.Revision==b.Revision&&a.Request==b.Request&&a.Order==b.Order&&Point(a.Position,b.Position)&&Point(a.Goal,b.Goal)&&MotionExecutionBinding.Same(a.Location,b.Location)&&a.Endpoint.HasValue==b.Endpoint.HasValue&&(!a.Endpoint.HasValue||MotionExecutionBinding.Same(a.Endpoint.Value,b.Endpoint.Value))&&MotionExecutionBinding.Same(a.Heading,b.Heading)&&MotionExecutionBinding.Same(a.Radius,b.Radius)&&MotionExecutionBinding.Same(a.Speed,b.Speed)&&MotionExecutionBinding.Same(a.TurnSpeed,b.TurnSpeed)&&a.Moving==b.Moving&&a.Held==b.Held&&a.CanYield==b.CanYield&&a.Outcome==b.Outcome&&a.RouteIndex==b.RouteIndex&&a.LocalIndex==b.LocalIndex&&a.NoProgressTicks==b.NoProgressTicks&&MotionExecutionBinding.Same(a.BlockedSeconds,b.BlockedSeconds);
        static bool Legs(IReadOnlyList<NavTypedLeg> a,IReadOnlyList<NavTypedLeg> b){if(a.Count!=b.Count)return false;for(int i=0;i<a.Count;i++)if(a[i].Kind!=b[i].Kind||a[i].PortalId!=b[i].PortalId||!MotionExecutionBinding.Same(a[i].From,b[i].From)||!MotionExecutionBinding.Same(a[i].To,b[i].To)||!MotionExecutionBinding.Same(a[i].Cost,b[i].Cost))return false;return true;}
        static bool Subscription(NavRouteSubscription a,NavRouteSubscription b)=>a.WorldGeneration==b.WorldGeneration&&a.GroupId==b.GroupId&&a.RootOrderRevision==b.RootOrderRevision&&a.CommandSequence==b.CommandSequence&&a.IssuedTick==b.IssuedTick&&a.ActivationRevision==b.ActivationRevision&&a.EntityId==b.EntityId&&a.Incarnation==b.Incarnation&&a.MemberOrderRevision==b.MemberOrderRevision&&a.MobilityRevision==b.MobilityRevision&&a.RequestSequence==b.RequestSequence&&a.Provenance==b.Provenance&&a.HoldBinding==b.HoldBinding&&MotionExecutionBinding.Same(a.Origin,b.Origin)&&MotionExecutionBinding.Same(a.AssignedEndpoint,b.AssignedEndpoint);
        internal static bool SameTranscript(NavMotionTranscript a,NavMotionTranscript b)
        {
            if(a.Generation!=b.Generation||a.Operation!=b.Operation||a.Count!=b.Count)return false;
            for(int i=0;i<a.Count;i++){
                var x=a[i];var y=b[i];
                if(!State(x.Before,y.Before)||!State(x.After,y.After)||x.Cooperative!=y.Cooperative||x.ProductionException!=y.ProductionException||x.Continuation!=y.Continuation||!ReferenceEquals(x.AcceptedRoute,y.AcceptedRoute)||!Legs(x.Chord,y.Chord)||!Legs(x.Remaining,y.Remaining))return false;
                // Route identity authenticates lifetime; full route values still match.
                if(x.AcceptedRoute!=null&&(!Legs(x.AcceptedRoute.Legs,y.AcceptedRoute.Legs)||!Subscription(x.AcceptedRoute.Subscription,y.AcceptedRoute.Subscription)||!MotionExecutionBinding.Same(x.AcceptedRoute.Cost,y.AcceptedRoute.Cost)))return false;
                var u=x.Sweep;var v=y.Sweep;
                if(u.Verified!=v.Verified||u.ProductionUnmapped!=v.ProductionUnmapped||!Point(u.From,v.From)||!Point(u.To,v.To)||u.FromSurface!=v.FromSurface||u.ToSurface!=v.ToSurface||!Point(u.FirstCrossing,v.FirstCrossing)||!Point(u.LastCrossing,v.LastCrossing)||u.FirstSeam!=v.FirstSeam||u.LastSeam!=v.LastSeam||u.FirstPortalLeg!=v.FirstPortalLeg||u.LastPortalLeg!=v.LastPortalLeg||u.PortalCount!=v.PortalCount||!Step(u.Before,v.Before)||!Step(u.After,v.After))return false;
            }
            return true;
        }
        static bool Step(NavRecastStepState a,NavRecastStepState b)=>a.Phase==b.Phase&&a.Cursor==b.Cursor&&MotionExecutionBinding.Same(a.Station,b.Station)&&MotionExecutionBinding.Same(a.HighWater,b.HighWater);
    }
    internal sealed partial class PlayableDomain
    {
        AiCombatMicroMotionAuthority combatMotionAuthority;
        AiCombatMicroAirMotionAuthority combatAirMotionAuthority;
        Func<IReadOnlyList<PlayableAiOwnerLoop>> combatExecutionOwners;
        internal void ConfigureCombatExecutionOwners(Func<IReadOnlyList<PlayableAiOwnerLoop>> owners)=>combatExecutionOwners=owners;
        internal void AssertCombatExecutionOperational()
        {if(combatExecutionOwners!=null)foreach(var owner in combatExecutionOwners())if(owner.AwaitingExecutionSources)throw new InvalidOperationException("Runtime execution sources await live rebind before clock/commands.");}
        internal AiCombatMicroMotionAuthority CombatMotionAuthority=>combatMotionAuthority;
        internal AiCombatMicroAirMotionAuthority CombatAirMotionAuthority=>combatAirMotionAuthority;
        internal void StopCombatMotionAuthority(){combatAirMotionAuthority?.Stop();combatMotionAuthority?.Stop();}
        internal void CaptureCombatMotionBoundary(){combatAirMotionAuthority?.BeforeCapture();combatMotionAuthority?.BeforeCapture();}
        internal void BindCombatMotionAuthority(Func<IReadOnlyList<PlayableAiOwnerLoop>> owners)
        {
            // Air has its own owning publication and no dependency on the ground
            // route backend selected for this match.
            if(combatAirMotionAuthority==null){combatAirMotionAuthority=new AiCombatMicroAirMotionAuthority(this,owners);BindAirMotionAuthority(combatAirMotionAuthority);}
            combatAirMotionAuthority.ServiceSources();
            if(!RoutingProfile.UsesRecastGroundRoutes){AssertCombatExecutionOperational();return;}
            if(combatMotionAuthority==null){combatMotionAuthority=new AiCombatMicroMotionAuthority(this,owners);Navigation.BindMotionAuthority(combatMotionAuthority);}
            combatMotionAuthority.ServiceSources();AssertCombatExecutionOperational();
        }
    }
    internal sealed partial class PlayableAiOwnerLoop
    {
        internal bool RetainsCurrentExecutionStamp(PlayableDomain domain,int entity)
        {
            if(!domain.TryReadCombatExecutionJobStamp(entity,domain.OwnerFor(OwnerId),out var stamp))return false;
            return combatMicro.RetainsAppliedExecutionStamp(entity,stamp);
        }
    }
    internal sealed partial class AiCombatMicroPolicy
    {
        internal bool RetainsAppliedExecutionStamp(int entity,PlayableOrderStamp stamp)=>clients.TryGetValue(entity,out var client)&&certificates.TryGetValue(entity,out var certificate)&&client.Sequence>0&&stamp!=null&&stamp.Origin==PlayableOrderOrigin.Ai&&stamp.Source==certificate.Source&&stamp.Sequence==certificate.Sequence;
    }
}
