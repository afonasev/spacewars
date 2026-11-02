using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using Spacewars.Runtime;
using Spacewars.Simulation;

// Mechanical owning-boundary audit, NOT an AI policy implementation/permit.
internal sealed class MotionPublicationChecks : INavigationMotionAuthority,IDisposable
{
    private readonly NavigationSession session;
    internal int Prepared,Committed,Composed,Ready,Refused;
    internal int FirstTurningReady,FirstTranslatingReady;
    private readonly HashSet<int> turned=new HashSet<int>(),translated=new HashSet<int>();
    internal bool RefuseNext,DeferNext;
    private NavigationSession.MotionSourcePin pendingPin,forcedSource,resumeSource;
    private NavigationSession.MotionPublication retained;
    private NavigationSession.MotionSourcePin sourcePin;
    internal long LastOperation=>sourcePin?.Operation??0;
    internal MotionPublicationChecks(NavigationSession session){this.session=session;session.BindMotionAuthority(this);}
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    public NavigationMotionAdoption Prepare(NavigationSession.MotionPublication publication)
    {
        Prepared++;Require(!publication.Committed&&!publication.Aborted,"source already terminal");
        Require(publication.Transcript.Count==session.Crowd.Units.Count,"missing ordinary/idle receiver");
        if(RefuseNext){RefuseNext=false;Refused++;throw new InvalidOperationException("intentional prepublication resource refusal");}
        for(int i=0;i<publication.Transcript.Count;i++){
            var row=publication.Transcript[i];Require(row.Before.Entity==session.Crowd.Units[i].Id,"receiver order");
            if(row.Before.Heading!=row.After.Heading&&turned.Add(row.After.Entity)){
                Require(publication.IncludesResults&&row.Before.Request==0&&row.After.Request>0,"first turn is not the initial composed Ready");
                Require(row.Continuation==NavMotionContinuation.Ready&&row.AcceptedRoute!=null&&row.Remaining.Count>0,"initial composed turn has no accepted full typed continuation");FirstTurningReady++;
            }
            if(!row.Before.Position.Equals(row.After.Position)&&translated.Add(row.After.Entity)){
                Require(row.Continuation==NavMotionContinuation.Ready&&row.AcceptedRoute!=null&&row.Chord.Count>0&&row.Remaining.Count>0,"first translation has no accepted actual chord/full suffix");FirstTranslatingReady++;
            }
            if(row.Continuation!=NavMotionContinuation.Ready)continue;Ready++;
            var at=row.Before.Location;foreach(var leg in row.Chord){Require(leg.From.Equals(at),"chord gap");at=leg.To;}
            Require(at.Equals(row.After.Location),"chord does not end at actual final point");
            foreach(var leg in row.Remaining){Require(leg.From.Equals(at),"remaining gap");at=leg.To;}
            Require(at.Equals(row.AcceptedRoute.Subscription.AssignedEndpoint),"full suffix missing endpoint");
        }
        if(forcedSource!=null){publication.SelectSource(forcedSource);throw new Exception("committed source allowed physical replay");}
        NavigationSession.MotionSourcePin pin;
        if(resumeSource!=null){pin=resumeSource;RequireSameProposal(pin.Proposal,publication.Transcript);publication.SelectSource(pin);resumeSource=null;}else pin=publication.AcceptSource();
        if(DeferNext){DeferNext=false;pendingPin=pin;return null;}return new Adoption(this,publication,pin);
    }
    private NavigationCaptureBytes CaptureReceiver(NavMotionReceiver receiver)
    {
        Require(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Capture,session.Generation,0,out var scope),"pending match audit admission");
        using(scope){var bytes=NavigationCaptureBytes.Write(scope.OwnedReservation(),w=>NavPhysicalBodyWire.WriteTo(w,receiver,scope.OwnedReservation()));scope.Attach(bytes);try{scope.Commit(bytes,_=>{});return bytes;}catch{bytes.Dispose();throw;}}
    }
    private void RequireSameProposal(NavMotionTranscript proposal,NavMotionTranscript actual)
    {
        Require(proposal.Count==actual.Count,"pending whole receiver set changed");
        for(int i=0;i<proposal.Count;i++){
            Require(ReferenceEquals(proposal[i].AcceptedRoute,actual[i].AcceptedRoute),"pending accepted route identity changed");
            using(var a=CaptureReceiver(proposal[i]))using(var b=CaptureReceiver(actual[i]))Require(a.Bytes.SequenceEqual(b.Bytes),"pending full receiver/chord/suffix changed");
        }
    }
    private sealed class Adoption : NavigationMotionAdoption
    {
        private readonly MotionPublicationChecks owner;private readonly NavigationSession.MotionPublication source;
        private NavigationSession.MotionSourcePin pin;
        internal Adoption(MotionPublicationChecks owner,NavigationSession.MotionPublication source,NavigationSession.MotionSourcePin pin){this.owner=owner;this.source=source;this.pin=pin;}
        internal override void ValidateCurrent()
        {
            for(int i=0;i<source.Transcript.Count;i++){
                var before=source.Transcript[i].Before;var live=owner.session.Crowd.Units[i];
                Require(before.Position.Equals(live.Position)&&before.Heading==live.Heading&&before.Request==live.InstalledRequest,"first write preceded admission");
            }
            bool rejected=false;try{owner.session.Step(1d/30);}catch(InvalidOperationException){rejected=true;}Require(rejected,"reentrant clock accepted");
            rejected=false;try{using(var capture=owner.session.CaptureState()){} }catch(InvalidOperationException){rejected=true;}Require(rejected,"reentrant capture accepted");
        }
        internal override void ApplyCommittedNoThrow()
        {
            if(!source.Committed)throw new Exception("no owning COMMITTED receipt");
            for(int i=0;i<source.Transcript.Count;i++){
                var after=source.Transcript[i].After;var live=owner.session.Crowd.Units[i];
                if(!after.Position.Equals(live.Position)||after.Heading!=live.Heading||after.Request!=live.InstalledRequest||after.Outcome!=live.Outcome)throw new Exception("whole candidate not installed");
            }
            owner.Committed++;if(source.IncludesResults)owner.Composed++;
        }
        public override void Dispose()
        {
            if(!source.Committed){Require(SpinWait.SpinUntil(()=>owner.session.TryRetireMotionSource(pin),5000),"source retirement timeout");pin.Dispose();return;}
            if(owner.sourcePin!=null){Require(SpinWait.SpinUntil(()=>owner.session.TryRetireMotionSource(owner.sourcePin),5000),"source retirement timeout");owner.sourcePin.Dispose();}owner.sourcePin=pin;pin=null;
            source.Retain();var previous=owner.retained;owner.retained=source;previous?.Release();
        }
    }
    internal void VerifyTranscriptCodec()
    {
        Require(retained?.Committed==true,"no retained factual publication to encode");
        long physical=session.PhysicalHostBudget.PhysicalBytes;NavigationCaptureBytes bytes=null,again=null;NavMotionTranscript restored=null;
        try{
            Require(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Capture,session.Generation,0,out var capture),"transcript capture admission");
            using(capture){bytes=NavigationCaptureBytes.Write(capture.OwnedReservation(),w=>NavPhysicalBodyWire.WriteTo(w,retained.Transcript,capture.OwnedReservation()));capture.Attach(bytes);capture.Commit(bytes,_=>{});}
            Require(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Decode,session.Generation,0,out var decode),"transcript decode admission");
            using(decode){using(var r=new BinaryReader(new MemoryStream(bytes.Bytes,false))){restored=(NavMotionTranscript)NavPhysicalBodyWire.ReadFrom(r,decode.OwnedReservation());Require(r.BaseStream.Position==r.BaseStream.Length,"transcript tail");}decode.Attach(restored);decode.Commit(restored,_=>{});}
            Require(restored.Count==retained.Transcript.Count&&restored.Operation==retained.Transcript.Operation,"transcript source identity changed");
            Require(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Capture,session.Generation,0,out var recapture),"transcript recapture admission");
            using(recapture){again=NavigationCaptureBytes.Write(recapture.OwnedReservation(),w=>NavPhysicalBodyWire.WriteTo(w,restored,recapture.OwnedReservation()));recapture.Attach(again);recapture.Commit(again,_=>{});}
            Require(bytes.Bytes.SequenceEqual(again.Bytes),"current transcript BODY changed on cold restore");
            Console.WriteLine("MOTION standalone typed transcript current BODY exact PASS bytes="+bytes.Bytes.Length+" (standalone codec witness)");
        }finally{again?.Dispose();restored?.Release();bytes?.Dispose();}
        Require(physical==session.PhysicalHostBudget.PhysicalBytes,"transcript codec leaked physical liability");
    }
    internal void VerifySessionSources(PlayableProfile profile,NavGeometry geometry)
    {
        Require(sourcePin!=null,"no committed source");long committed=sourcePin.Operation;
        var before=session.Crowd.Units.Select(a=>a.CopyMovementHeader()).ToArray();DeferNext=true;session.Step(1d/30);
        Require(pendingPin!=null&&pendingPin.CommittedTranscript==null,"pending input not retained");
        for(int i=0;i<before.Length;i++)Require(before[i].SameMovementValues(session.Crowd.Units[i]),"pending changed physical state");
        long pending=pendingPin.Operation,watermark=session.MotionOperationWatermark;
        Require(pending>committed&&watermark==pending,"source IDs are not monotonic");
        byte[] bytes;using(var state=session.CaptureState())bytes=WorldWire.Pack(w=>WorldWire.Write(w,state));
        var cold=new NavigationSession(session.Generation,geometry,profile.Navigation){UsesRecastGroundRoutes=true};cold.ConfigureMarch(profile.GroupMarchMaximumStretch,profile.Revision);
        MotionPublicationChecks coldAudit=null;NavigationSession.MotionSourcePin cp=null,pp=null;
        try{
            using(var state=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState))cold.RestoreState(state);
            Require(cold.AwaitingMotionSources&&cold.MotionOperationWatermark==watermark,"cold source gate/watermark missing");
            bool rejected=false;try{cold.Step(1d/30);}catch(InvalidOperationException){rejected=true;}Require(rejected,"cold advanced before source rebind");
            rejected=false;try{cold.Move(1,new NavPoint(1,1));}catch(InvalidOperationException){rejected=true;}Require(rejected,"cold submitted command before source rebind");
            using(var state=cold.CaptureState())Require(bytes.SequenceEqual(WorldWire.Pack(w=>WorldWire.Write(w,state))),"pending/committed current BODY changed before rebind");
            Require(!cold.TryRetainMotionSource(pending,true,out _),"pending forged COMMITTED");
            long unboundBytes=cold.PhysicalHostBudget.PhysicalBytes;
            Require(cold.TryRetainMotionSource(committed,true,out cp)&&cp.CommittedTranscript!=null,"committed lookup unavailable");
            Require(cold.AwaitingMotionSources,"partial rebind cleared all promises");
            Require(cold.TryRetainMotionSource(pending,false,out pp)&&pp.CommittedTranscript==null&&!cold.AwaitingMotionSources,"pending rebind unavailable");
            Require(cold.PhysicalHostBudget.PhysicalBytes-unboundBytes==cp.StorageBytes+pp.StorageBytes,"rebound source pins have incorrect physical liability");
            pp.Dispose();pp=null;Require(cold.AwaitingMotionSources,"released binding kept gate open");Require(cold.TryRetainMotionSource(pending,false,out pp)&&!cold.AwaitingMotionSources,"reacquire pending source failed");
            Require(!ReferenceEquals(pp.Proposal,pendingPin.Proposal),"cold reused hot source");
            for(int i=0;i<pp.Proposal.Count;i++)if(pp.Proposal[i].AcceptedRoute!=null)Require(ReferenceEquals(pp.Proposal[i].AcceptedRoute,cp.CommittedTranscript[i].AcceptedRoute),"Session source route aliases split on decode");
            using(var state=cold.CaptureState())Require(bytes.SequenceEqual(WorldWire.Pack(w=>WorldWire.Write(w,state))),"rebind changed authoritative current bytes");
            coldAudit=new MotionPublicationChecks(cold);coldAudit.forcedSource=cp;
            var pose=cold.Crowd.Units.Select(a=>a.CopyMovementHeader()).ToArray();cold.Step(1d/30);
            for(int i=0;i<pose.Length;i++)Require(pose[i].SameMovementValues(cold.Crowd.Units[i]),"COMMITTED source physically replayed");
            coldAudit.forcedSource=null;
            coldAudit.resumeSource=pp;cold.Step(1d/30);Require(coldAudit.Committed==1&&cold.MotionOperationWatermark==watermark,"pending operation did not resume exactly once: commits="+coldAudit.Committed+" prepared="+coldAudit.Prepared+" op="+cold.MotionOperationWatermark+" expected="+watermark+" movement="+cold.LastOwnedMovementRefusalMessage+" result="+cold.LastResultBatchFailure+" path="+cold.LastNavigationStepPath);pp=null;
            Require(cold.TryRetainMotionSource(pending,true,out var receipt),"resumed pending COMMITTED lookup missing");receipt.Dispose();
            Require(cold.TryRetireMotionSource(cp),"cold source retirement refused");cp.Dispose();cp=null;
            Require(!cold.AwaitingMotionSources&&cold.MotionOperationWatermark==watermark,"retirement reset watermark");
            cold.Step(1d/30);Require(cold.MotionOperationWatermark>watermark,"cold reused an old operation ID");
            Console.WriteLine("MOTION Session pending+COMMITTED current BODY exact / preclock paid rebind / route alias / consume-only / exact pending resume / monotonic watermark PASS bytes="+bytes.Length);
        }finally{coldAudit?.Dispose();cp?.Dispose();pp?.Dispose();cold.ReleaseCommittedHostRoot();cold.DestroyNavigationAuthority();Require(cold.PhysicalHostBudget.PhysicalBytes==0&&cold.PhysicalHostBudget.ActiveScopes==0,"cold motion source leak");}
        Require(SpinWait.SpinUntil(()=>session.TryRetireMotionSource(pendingPin),5000),"hot pending retirement timeout");pendingPin.Dispose();pendingPin=null;
    }
    public void Dispose(){if(pendingPin!=null){Require(SpinWait.SpinUntil(()=>session.TryRetireMotionSource(pendingPin),5000),"pending retirement timeout");pendingPin.Dispose();pendingPin=null;}retained?.Release();retained=null;if(sourcePin!=null){Require(SpinWait.SpinUntil(()=>session.TryRetireMotionSource(sourcePin),5000),"source retirement timeout");sourcePin.Dispose();sourcePin=null;}}
}
