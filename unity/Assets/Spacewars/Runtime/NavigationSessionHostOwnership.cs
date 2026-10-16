using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private sealed class PhysicalAnswerBackpressure : Exception {}
        internal sealed class PhysicalIngressRefused : InvalidOperationException {internal PhysicalIngressRefused(string message):base(message){}}
        // Initialized only when the physical host path claims this session.
        // A single ledger then owns both the committed root and all candidates.
        private NavigationRetentionBudget physicalHostBudget;
        private NavigationHostRoot committedHostRoot;

        internal bool HasPhysicalHostRoot {get{using(EnterTransport())return committedHostRoot!=null;}}
        internal bool UseLegacyHost {get{using(EnterTransport())return !physicalArrivalEnabled&&committedHostRoot==null;}}
        internal NavigationRetentionBudget PhysicalHostBudget
        {
            get{using(EnterTransport())return physicalHostBudget;}
        }
        internal bool TryOpenPhysicalHostScope(NavigationPreparationKind kind,long owner,long publication,
            out NavigationRetentionBudget.Scope scope)
        {
            NavigationRetentionBudget budget;
            using(EnterTransport()){
                if(physicalHostBudget==null)physicalHostBudget=new NavigationRetentionBudget();
                budget=physicalHostBudget;
            }
            return budget.TryOpenScope(kind,owner,publication,out scope);
        }
        // The caller releases its pin after traversal or after preparing a fork.
        // Capture never follows mutable in-flight dictionaries under the gate.
        internal NavigationHostRoot PinCommittedHostRoot()
        {
            using(EnterTransport()){
                var root=committedHostRoot;
                root?.Retain();return root;
            }
        }
        internal bool TryObservePhysicalIngress(out NavigationRequest request,out NavigationAdmissionPin observed)
        {
            using(EnterTransport()){
                RejectTransferCallbackReentry();
                if(commandGroup!=null||admissionBatchDepth!=0||
                    !Requests.TryPeek(out request)||!QuantumIngressAllowed(request)||!Admission.Contains(request)||IsArrivalRequest(request)){
                    request=null;observed=default;return false;
                }
                observed=new NavigationAdmissionPin(admissionOwner,admission);return true;
            }
        }
        // Scope.Attach and all preparation happen before entering transportGate.
        // The callback is a short, validated authority transition: ingress
        // removal/registration and root installation become capture-visible at
        // the same boundary. The old root and births release after the gate.
        internal void PublishPhysicalHostRoot(NavigationRetentionBudget.Scope scope,
            NavigationHostRoot expected,NavigationHostRoot candidate,Action publishAuthority)
        {
            if(scope==null||candidate==null||publishAuthority==null||!candidate.IsImmutableCaptureRoot)
                throw new ArgumentException("Missing sealed physical host publication.");
            NavigationHostRoot retired=null;
            scope.Publish(candidate,_=>{
                using(EnterTransport()){
                    RejectTransferCallbackReentry();
                    if(!ReferenceEquals(physicalHostBudget,scope.Identity)||
                       !ReferenceEquals(committedHostRoot,expected))
                        throw new PhysicalIngressRefused("Physical host publication lost its source root.");
                    publishAuthority();
                    candidate.Retain();retired=committedHostRoot;
                    committedHostRoot=candidate;
                }
            });
            try{scope.ResolveCommitted();}
            finally{retired?.Release();}
        }
        // The caller has already prepared and sealed scheduler Submit, provider,
        // row and FIFO outside transportGate. Only the exact mailbox handoff
        // and root replacement are performed at the authority boundary.
        internal void PublishRegisteredPhysicalHost(NavigationRetentionBudget.Scope scope,
            NavigationHostRoot expected,NavigationHostRoot candidate,NavigationAdmission observed,
            NavigationRequest ingress,NavNameKey key)
        {
            if(observed==null||ingress==null||candidate==null||
                !candidate.Rows.TryGetValue(key,out var entry)||!(entry is NavigationHostRow row)||
                !candidate.Requests.TryGetValue(key,out var requestOwner)||
                !(requestOwner is NavigationHostRequest physicalRequest)||
                !ReferenceEquals(physicalRequest.Request,row.Request)||
                row.Request.Session!=ingress.Session||row.Request.Request!=ingress.Request)
                throw new ArgumentException("Prepared physical host row differs from ingress.");
            var owned=PublishOwnedPhysicalIngress(scope,expected,candidate,observed,ingress,key,0);if(owned.HasValue){if(!owned.Value)throw new PhysicalIngressRefused("Owned physical ingress refused.");return;}
            using(EnterTransport())UseLegacyRegistrations();if(scope.Prepared==null)scope.Attach(candidate);
            ValidateScheduledRoute(ingress,row.Scheduled,observed);
            PublishPhysicalHostRoot(scope,expected,candidate,()=>{
                if(commandGroup!=null||admissionBatchDepth!=0||
                    Admission.Generation!=observed.Generation||
                    Admission.PublicationIdentity!=observed.PublicationIdentity||
                    !Admission.Contains(ingress)||registeredGraphKeys.ContainsKey(ingress))
                    throw new InvalidOperationException("Physical host ingress changed before publication.");
                if(!Requests.TryTransfer(head=>{
                    if(!ReferenceEquals(head,ingress))return false;
                    var canonical=physicalRequest.Request;
                    bool pendingOwner=pending.TryGetValue(ingress.Entity,out var pendingRequest)&&
                        ReferenceEquals(pendingRequest,ingress);
                    bool probeOwner=probes.TryGetValue(ingress.Entity,out var probeRequest)&&
                        ReferenceEquals(probeRequest,ingress);
                    if(!pendingOwner&&!probeOwner)return false;
                    registeredRoutes.Add(canonical);
                    try{
                        if(pendingOwner)pending[ingress.Entity]=canonical;
                        if(probeOwner)probes[ingress.Entity]=canonical;
                        registeredGraphKeys.Add(canonical,row.Scheduled.GraphKey);
                        PublishAdmission();
                        transportOwner.Transfer(canonical);return true;
                    }catch{
                        registeredGraphKeys.Remove(canonical);
                        if(pendingOwner)pending[ingress.Entity]=ingress;
                        if(probeOwner)probes[ingress.Entity]=ingress;
                        registeredRoutes.Remove(canonical);
                        PublishAdmission();
                        throw;
                    }
                },out var transferred)||!ReferenceEquals(transferred,ingress))
                    throw new InvalidOperationException("Physical host mailbox head changed before publication.");
            });
        }
        // Delivered retains the answer's owned route and corridor after its
        // active row is removed. The mailbox borrows that exact facade until
        // the host retires Delivered after authority consumption.
        internal bool PublishCompletedPhysicalHost(NavigationRetentionBudget.Scope scope,
            NavigationHostRoot expected,NavigationHostRoot candidate,NavNameKey key)
        {
            if(expected==null||candidate==null||
                !expected.Rows.TryGetValue(key,out var sourceEntry)||
                !(sourceEntry is NavigationHostRow sourceRow)||sourceRow.Answer==null||
                !candidate.Delivered.TryGetValue(key,out var deliveredEntry)||
                !ReferenceEquals(sourceEntry,deliveredEntry)||
                candidate.Rows.ContainsKey(key))
                throw new ArgumentException("Physical completion changed before publication.");
            var owned=PublishOwnedPhysicalCompletion(scope,expected,candidate,sourceRow);if(owned.HasValue)return owned.Value;
            if(scope.Prepared==null)scope.Attach(candidate);
            var answer=sourceRow.Answer;NavigationHostRequest retiredRegistrationRequest=null;NavDeclarationStorage retiredRegistrationRecipe=null;
            try{
                PublishPhysicalHostRoot(scope,expected,candidate,()=>{
                    RejectTransferCallbackReentry();
                    if(hostAllocationThread!=0)throw new PhysicalAnswerBackpressure();
                    int index=RegistrationIndex(answer.Request);
                    if(index<0||answer.Status==NavSolveStatus.Ready&&answer.Corridor==null||
                        answer.Corridor!=null&&
                        (!RegistrationGraphKey(answer.Request,out var graphKey)||
                         answer.Corridor.GraphKey!=graphKey||
                         answer.Corridor.ProviderBinding!=answer.Request.CertifiedProviderBinding))
                        throw new InvalidOperationException("Physical completion lost its registered authority.");
                    if(!Answers.TryEnqueue(answer))throw new PhysicalAnswerBackpressure();
                    // Match ordinary completion and cold mailbox/FIFO hydration.
                    // This compatibility publication still needs paid tracking
                    // preparation before it can claim a no-allocation final gate.
                    TrackCorridorAnswer(answer);
                    if(liveRegistrations!=null)liveRegistrations.ExtractNoThrow(index,out retiredRegistrationRequest,out retiredRegistrationRecipe);
                    else{registeredGraphKeys.Remove(answer.Request);registeredRoutes.RemoveAt(index);}
                    if(sourceRow.ArrivalToken!=0){
                        if(!arrivalProjectionLeases.TryGetValue(sourceRow.ArrivalToken,out var lease))throw new InvalidOperationException("Missing physical arrival owner at completion.");
                        ReleaseArrivalProjection(lease,"completion");
                    }
                });
                return true;
            }catch(PhysicalAnswerBackpressure){return false;}finally{retiredRegistrationRequest?.Release();retiredRegistrationRecipe?.Release();}
        }
        internal bool CanRetireDeliveredPhysicalAnswer(NavigationAnswer answer)
        {
            if(answer==null)throw new ArgumentNullException(nameof(answer));
            using(EnterTransport()){
                return !Answers.CopyItems().Contains(answer)&&
                    !(liveProbeAnswers!=null?liveProbeAnswers.Contains(answer):probeAnswers.Contains(answer))&&!barrierAnswers.Contains(answer)&&
                    !installedExecutions.Values.Any(state=>
                        ReferenceEquals(state.Corridor,answer.Corridor)||
                        ReferenceEquals(state.Profile,answer.Request.Profile)||
                        ReferenceEquals(state.MovementGeometry,answer.Request.Geometry)||
                        ReferenceEquals(state.BaseGeometry,answer.Request.BaseGeometry));
            }
        }
        private bool IsCurrentPhysicalAuthorityGeometry(NavigationRequest request)
        {
            if(request==null||committedHostRoot==null)return false;
            bool owned=false;
            foreach(var entry in committedHostRoot.Delivered)
                if(entry.Value is NavigationHostRow row&&ReferenceEquals(row.Request,request)){
                    owned=true;break;
                }
            if(!owned)return false;
            var candidate=request.BaseGeometry;
            if(candidate==null||candidate.HalfExtent!=navigationGeometry.HalfExtent||
                candidate.Revision!=navigationGeometry.Revision||
                candidate.Obstacles.Count!=navigationGeometry.Obstacles.Count)return false;
            for(int i=0;i<candidate.Obstacles.Count;i++)
                if(!candidate.Obstacles[i].GeometryEquals(navigationGeometry.Obstacles[i]))return false;
            return true;
        }
        internal void ReleaseCommittedHostRoot()
        {
            NavigationHostRoot retired;
            using(EnterTransport()){
                if(hostAllocationThread!=0)throw new InvalidOperationException("Cannot release allocating physical host.");
                retired=committedHostRoot;committedHostRoot=null;
            }
            retired?.Release();
        }
    }
}
