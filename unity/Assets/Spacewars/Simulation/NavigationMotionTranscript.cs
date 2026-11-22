using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    internal enum NavMotionContinuation { NoAcceptedRoute, Unmapped, ChordRefused, SuffixRefused, Ready }

    // Values from the owning final frame, never a caller-supplied candidate.
    internal readonly partial struct NavMotionActorState
    {
        internal readonly int Entity,Team,RouteIndex,LocalIndex,NoProgressTicks;
        internal readonly long Incarnation,Mobility,Group,Revision,Request,Order;
        internal readonly NavPoint Position,Goal;
        internal readonly NavLocation Location;
        internal readonly NavLocation? Endpoint;
        internal readonly double Heading,Radius,Speed,TurnSpeed,BlockedSeconds;
        internal readonly bool Moving,Held,CanYield;
        internal readonly NavigationOutcome Outcome;
        internal NavMotionActorState(NavUnit actor)
        {
            Entity=actor.Id;Team=actor.Team;Incarnation=actor.Incarnation;Mobility=actor.MobilityRevision;
            Group=actor.InstalledGroupId;Revision=actor.InstalledRevision;Request=actor.InstalledRequest;Order=actor.InstalledOrder;
            Position=actor.Position;Location=actor.Location;Goal=actor.Goal;Endpoint=actor.GoalLocation;
            Heading=actor.Heading;Radius=actor.Radius;Speed=actor.Speed;TurnSpeed=actor.TurnSpeed;
            Moving=actor.Moving;Held=actor.Held;CanYield=actor.CanYield;Outcome=actor.Outcome;
            RouteIndex=actor.routeIndex;LocalIndex=actor.localIndex;NoProgressTicks=actor.noProgressTicks;BlockedSeconds=actor.blockedSeconds;
        }
    }

    // The accepted route and both emitted traces own their provider/name edges.
    // Failure is explicit: ordinary receivers are not silently classified as
    // micro, and an unmapped production displacement is never a certified path.
    internal sealed partial class NavMotionReceiver : NavOwnedStorage
    {
        private static readonly long Bytes=NavStorageLayout.ObjectBytesBound(typeof(NavMotionReceiver));
        // Derived memo metadata only, never accepted gameplay state or wire data.
        #if UNITY_INCLUDE_TESTS
        [ThreadStatic] internal static long CertifiedTerminalStepsForTests;
#endif
        internal int TailStart=-1,TailCursor;internal NavLocation TailAnchor;
        internal bool ReusedTail;
        internal readonly NavMotionActorState Before,After;
        internal readonly bool Cooperative,ProductionException;
        internal readonly NavRecastSweepReceipt Sweep;
        internal NavMotionContinuation Continuation{get;private set;}
        internal NavTypedRoute AcceptedRoute{get;private set;}
        internal IReadOnlyList<NavTypedLeg> Chord=>chord;
        internal IReadOnlyList<NavTypedLeg> Remaining=>remaining;
        private NavPagedList<NavTypedLeg> chord,remaining;
        private NavOwnedStorage beforeLocation,beforeGoal,afterLocation,afterGoal;
        private NavMotionReceiver(NavPreparedCrowdStep frame,int row):base(Bytes)
        {
            Before=new NavMotionActorState(frame.Before[row]);After=new NavMotionActorState(frame.After[row]);
            Cooperative=frame.Motion[row].Yielding;ProductionException=frame.Motion[row].YieldLabel<0;Sweep=frame.RecastReceipts[row];
        }
        internal static long ReceiverAndFirstTracePagesBytes
            =>NavStorageLayout.Charge(Bytes)+2*NavPagedList<NavTypedLeg>.StorageBytesWithFirstPage;

        internal static NavMotionReceiver Create(NavPreparedCrowdStep frame,int row,INavStorageReservation r,NavMotionProofMemo previous,NavPagedList<NavTypedLeg> emptyTrace,bool reuseTrace,NavTranscriptTraceBatch batch)
        {
            using(var allocation=new NavStorageAllocation(r.TryReserve)){
                allocation.Reserve(Bytes);var value=allocation.Register(new NavMotionReceiver(frame,row));value.Retain();
                try{
                    value.beforeLocation=PinName(value,value.Before.Location);value.beforeGoal=PinName(value,value.Before.Endpoint);
                    value.afterLocation=PinName(value,value.After.Location);value.afterGoal=PinName(value,value.After.Endpoint);
                    if(emptyTrace!=null){
                        value.chord=NavPhysicalMacro.Acquire(value,emptyTrace);
                        value.remaining=NavPhysicalMacro.Acquire(value,emptyTrace);
                        value.AcceptedRoute=NavPhysicalMacro.Acquire(value,frame.RecastRoutes[row]);
                        value.Continuation=value.AcceptedRoute==null?NavMotionContinuation.NoAcceptedRoute:NavMotionContinuation.Unmapped;
                        return value;
                    }
                    if(!reuseTrace||batch!=null)value.chord=NavPagedList<NavTypedLeg>.Create(r.TryReserve,pageSize:NavTypedLeg.TracePageSlots);
                    value.remaining=NavPagedList<NavTypedLeg>.Create(r.TryReserve,pageSize:NavTypedLeg.TracePageSlots);
                    value.Build(frame,row,r,previous,batch);
                    value.chord??=NavPagedList<NavTypedLeg>.Create(r.TryReserve,pageSize:NavTypedLeg.TracePageSlots);
                    if(!value.chord.IsFrozen)value.chord.Seal();
                    value.remaining.Seal();
                    return value;
                }catch{value.Release();throw;}
            }
        }
        private static NavOwnedStorage PinName(NavOwnedStorage owner,NavLocation? location)
        {var pin=location?.NameReference.Owner;return pin==null?null:NavPhysicalMacro.Acquire(owner,pin);}
        private static void Append(NavPagedList<NavTypedLeg> to,IReadOnlyList<NavTypedLeg> trace)
        {for(int i=0;i<trace.Count;i++)to.Add(trace[i]);}
        private void Build(NavPreparedCrowdStep frame,int row,INavStorageReservation r,NavMotionProofMemo previous,NavTranscriptTraceBatch batch)
        {
            var route=frame.RecastRoutes[row];if(route==null){Continuation=NavMotionContinuation.NoAcceptedRoute;return;}
            AcceptedRoute=NavPhysicalMacro.Acquire(this,route);
            if(!Sweep.Verified||Sweep.ProductionUnmapped){Continuation=NavMotionContinuation.Unmapped;return;}
            Continuation=NavMotionContinuation.ChordRefused;
            if(!Sweep.From.Equals(Before.Position)||!Sweep.To.Equals(After.Position)||Sweep.FromSurface!=Before.Location.SurfaceId||Sweep.ToSurface!=After.Location.SurfaceId)return;
            NavLocation at;
            if (batch == null || !batch.TryAppend(row,false,route,Sweep.Before.Cursor,Before.Location,After.Position,
                    frame.Working,After.Entity,r,chord,out at,After.Location)) {
                if(!NavRecastIntervals.TryTraceOwned(route,Sweep.Before,Before.Location,After.Position,r,out at,out _,out _,out var actual,frame.Working,After.Entity))return;
                try{
                    if(!at.Equals(After.Location))return;
                    // The partition already returned a complete sealed chord under this
                    // reservation. Keep its owning edge without copying the trace pages.
                    if(chord==null&&actual is NavPagedList<NavTypedLeg> owned&&owned.IsFrozen&&owned.IsImmutableCaptureRoot)
                        chord=NavPhysicalMacro.Acquire(this,owned);
                    else{
                        chord??=NavPagedList<NavTypedLeg>.Create(r.TryReserve,pageSize:NavTypedLeg.TracePageSlots);
                        Append(chord,actual);
                    }
                }finally{(actual as NavOwnedStorage)?.ReleaseOwner();}
            }
            Continuation=NavMotionContinuation.SuffixRefused;
            var actor=frame.After[row];var state=Sweep.After;
            if(!After.Endpoint.HasValue||!After.Endpoint.Value.Equals(route.Subscription.AssignedEndpoint)||!After.Goal.Equals(After.Endpoint.Value.Position)||After.Request!=route.Subscription.RequestSequence)return;
            if(actor.localIndex<0||actor.localIndex>actor.localRoute.Length||actor.routeIndex<0||actor.routeIndex>actor.route.Length)return;
            bool Link(NavPoint target){
                NavRecastSweepReceipt next;NavLocation reached;IReadOnlyList<NavTypedLeg> trace;
                if(frame.Working.StableSelectClearance){
                    if(!NavRecastIntervals.AdvanceOwned(route,state,at,target,r,out next,out reached,out trace,frame.Working,After.Entity))return false;
                }else{
                    // Hooked/unknown providers retain their original call path.
                    if(!NavRecastIntervals.Advance(route,state,at,target,r,out next,frame.Working,After.Entity))return false;
                    if(!NavRecastIntervals.TryTraceOwned(route,state,at,target,r,out reached,out _,out _,out trace,frame.Working,After.Entity))return false;
                }
                try{if(next.ToSurface!=reached.SurfaceId||!reached.Position.Equals(target))return false;Append(remaining,trace);at=reached;state=next.After;return true;}
                finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
            }
            for(int i=actor.localIndex;i<actor.localRoute.Length;i++)if(!Link(actor.localRoute[i]))return;
            // Keep the actual motor cursor. Qualifying a different bank/target
            // would not describe the candidate that the owner will install.
            if(actor.routeIndex<actor.route.Length&&!NavRecastIntervals.CanTarget(route,state,at,actor.routeIndex,r,frame.Working,After.Entity))return;
            bool traceRemaining=NavRecastIntervals.CanTraceRemaining(route,r,frame.Working);
            if(traceRemaining && actor.routeIndex==actor.route.Length-1 &&
                actor.route[actor.routeIndex].Equals(After.Endpoint.Value.Position) &&
                route.Legs[0].RecastAuthorityProvider is PartitionNavigationProvider provider &&
                state.Cursor>=0 && state.Cursor<route.Legs.Count && route.Legs[state.Cursor].From.SurfaceId==at.SurfaceId &&
                route.TrySection(provider,at,actor.route[actor.routeIndex],out var end) && end.Equals(After.Endpoint.Value)){
                // CanTarget above still checked this motor cursor. Only the wire-visible
                // clipped leg changes as the body moves inside the certified section.
                if(!((INavRecastSweepAuthority)frame.Working).AllowsRecastSweep(After.Entity,at,end.Position,route.RecastPlan.Spine.Radius))return;
                using var births=r is INavStorageLocalBirthAllocator?new NavStorageLocalBirths(r):null;
                var leg=NavTypedLeg.CreateOwned(new NavLocation(at.Position,at.SurfaceId),end,
                    LayeredNavigationProvider.Distance(at.Position,end.Position),NavTypedLegKind.Surface,null,births??r,provider);
                try{remaining.Add(leg);}finally{leg.ReleaseStorage();}
                if(!frame.Working.CompatibleArrival(end,After.Endpoint.Value,After.Radius))return;
                Continuation=NavMotionContinuation.Ready;
#if UNITY_INCLUDE_TESTS
                CertifiedTerminalStepsForTests++;
#endif
                return;
            }
            int tailCursor=state.Cursor;
            bool TailLink(NavPoint target){
                if(!traceRemaining)return Link(target);
                if (batch != null && batch.TryAppend(row,true,route,tailCursor,at,target,frame.Working,After.Entity,
                        r,remaining,out var prepared)) {
                    tailCursor=NavRecastIntervals.RemainingCursor(route,prepared,tailCursor,r,frame.Working,After.Entity);
                    at=prepared;
                    return true;
                }
                if(!NavRecastIntervals.TryTraceRemainingOwned(route,tailCursor,at,target,r,out var reached,out var cursor,out var trace,frame.Working,After.Entity))return false;
                try{if(!reached.Position.Equals(target))return false;Append(remaining,trace);at=reached;tailCursor=cursor;return true;}
                finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
            }
            for(int i=actor.routeIndex;i<actor.route.Length;i++){
                if(!TailLink(actor.route[i]))return;
                if(i==actor.routeIndex&&traceRemaining){
                    TailStart=remaining.Count;TailAnchor=at;TailCursor=tailCursor;
                    // Only the fixed waypoint-to-endpoint suffix may be borrowed.
                    // Chord, CanTarget and ACTUAL-pose-to-waypoint just ran fresh.
                    if(previous!=null&&previous.TryTail(frame,row,this,r,out var proof)){
                        for(int j=proof.TailStart;j<proof.Remaining.Count;j++)remaining.Add(proof.Remaining[j]);
                        ReusedTail=true;at=After.Endpoint.Value;break;
                    }
                }
            }
            if(!at.Equals(After.Endpoint.Value)&&at.Position.Equals(After.Endpoint.Value.Position)&&!TailLink(After.Endpoint.Value.Position))return;
            if(!at.Equals(After.Endpoint.Value)||!frame.Working.CompatibleArrival(at,After.Endpoint.Value,After.Radius))return;
            Continuation=NavMotionContinuation.Ready;
        }
        protected override void ReleaseEdges()
        {AcceptedRoute?.Release();chord?.Release();remaining?.Release();beforeLocation?.Release();beforeGoal?.Release();afterLocation?.Release();afterGoal?.Release();}
    }

    internal sealed partial class NavMotionTranscript : NavOwnedStorage
    {
        private static readonly long Bytes=NavStorageLayout.ObjectBytesBound(typeof(NavMotionTranscript));
        internal readonly long Generation,Operation;
        private readonly NavMotionReceiver[] receivers;
        private NavOwnedStorage geometry,profile;
        internal int ReusedTails{get;private set;}
        internal bool SamePinnedContext(NavMotionTranscript other)=>ReferenceEquals(geometry,other.geometry)&&ReferenceEquals(profile,other.profile);
        internal int Count=>receivers.Length;
        internal NavMotionReceiver this[int row]=>receivers[row];
        private NavMotionTranscript(int count,long generation,long operation):base(checked(Bytes+32+8L*count))
        {receivers=new NavMotionReceiver[count];Generation=generation;Operation=operation;}
        internal static long ReceiverHeadroom(int count)
            =>checked(NavStorageLayout.Charge(Bytes+32+8L*count)+count*NavMotionReceiver.ReceiverAndFirstTracePagesBytes);

        internal static NavMotionTranscript Create(NavPreparedCrowdStep frame,long generation,long operation,INavStorageReservation r,NavMotionProofMemo previous=null,bool shareEmptyTraces=false)
        {
            using(var allocation=new NavStorageAllocation(r.TryReserve)){
                allocation.Reserve(checked(Bytes+32+8L*frame.After.Length));var result=allocation.Register(new NavMotionTranscript(frame.After.Length,generation,operation));result.Retain();
                NavPagedList<NavTypedLeg> emptyTrace=null;
                try{result.geometry=NavPhysicalMacro.Acquire(result,frame.GeometryPin);result.profile=NavPhysicalMacro.Acquire(result,frame.ProfilePin);
                    if(previous!=null&&!previous.MatchesFrame(frame,r))previous=null;
                    using var batch=shareEmptyTraces?NavTranscriptTraceBatch.Prepare(frame,r):null;
                    for(int i=0;i<result.Count;i++){
                        var sweep=frame.RecastReceipts[i];
                        bool empty=shareEmptyTraces&&(frame.RecastRoutes[i]==null||!sweep.Verified||sweep.ProductionUnmapped);
                        if(empty&&emptyTrace==null){
                            emptyTrace=NavPagedList<NavTypedLeg>.Create(r.TryReserve,pageSize:NavTypedLeg.TracePageSlots);
                            emptyTrace.Seal();
                        }
                        var receiver=NavMotionReceiver.Create(frame,i,r,previous,empty?emptyTrace:null,shareEmptyTraces,batch);
                        result.receivers[i]=receiver;
                        if(receiver.ReusedTail)result.ReusedTails++;
                    }
                    return result;
                }catch{result.Release();throw;}finally{emptyTrace?.Release();}
            }
        }
        protected override void ReleaseEdges(){foreach(var receiver in receivers)receiver?.Release();geometry?.Release();profile?.Release();}
    }
    // One previous successful publication; only immutable routes/proof edges
    // survive, never a previous crowd frame or its reservation/working buffers.
    // This optional derived proof is discarded at capture and owns no decision.
    internal sealed class NavMotionProofMemo : NavOwnedStorage
    {
        private static readonly long Bytes=NavStorageLayout.ObjectBytesBound(typeof(NavMotionProofMemo));
        private NavCrowd source;private NavOwnedStorage geometry,profile;private NavMotionTranscript transcript;
        private readonly NavRetainedArray<NavPoint>[] globals;
        private NavMotionProofMemo(int count):base(checked(Bytes+32+8L*count)){globals=new NavRetainedArray<NavPoint>[count];}
        internal static bool Eligible(NavPreparedCrowdStep f)
        {
            if(!f.Working.StableSelectClearance)return false;
            // HOLD is dynamic. Until it has an independently versioned static
            // proof, any held actor forces the complete conservative path.
            for(int i=0;i<f.After.Length;i++)if(f.Before[i].Held||f.After[i].Held)return false;
            return true;
        }
        internal static NavMotionProofMemo TryCreate(NavPreparedCrowdStep f,NavMotionTranscript t,INavStorageReservation r)
        {
            if(!Eligible(f))return null;
            bool useful=false;for(int i=0;i<t.Count;i++)if(t[i].Continuation==NavMotionContinuation.Ready&&t[i].TailStart>=0&&t[i].TailStart<t[i].Remaining.Count&&f.Globals[i]!=null&&ReferenceEquals(f.After[i].route,f.Globals[i].ReadOnlyBacking)){useful=true;break;}
            long bytes=checked(Bytes+32+8L*t.Count);
            if(!useful||!r.TryReserve(NavStorageLayout.Charge(bytes)))return null;
            using(var a=new NavStorageAllocation(r.TryReserve)){
                var value=a.Register(new NavMotionProofMemo(t.Count));value.Retain();
                try{
                    value.source=f.SourceCrowd;value.geometry=NavPhysicalMacro.Acquire(value,f.GeometryPin);value.profile=NavPhysicalMacro.Acquire(value,f.ProfilePin);
                    value.transcript=NavPhysicalMacro.Acquire(value,t);
                    for(int i=0;i<t.Count;i++)if(f.Globals[i]!=null&&ReferenceEquals(f.After[i].route,f.Globals[i].ReadOnlyBacking))value.globals[i]=NavPhysicalMacro.Acquire(value,f.Globals[i]);
                    return value;
                }catch{value.Release();throw;}
            }
        }
        internal bool MatchesFrame(NavPreparedCrowdStep f,INavStorageReservation r)
            =>r is INavStorageLedgerIdentity ledger&&ledger.Owns(this)&&Eligible(f)&&
              ReferenceEquals(source,f.SourceCrowd)&&ReferenceEquals(geometry,f.GeometryPin)&&ReferenceEquals(profile,f.ProfilePin);
        internal bool TryTail(NavPreparedCrowdStep f,int row,NavMotionReceiver current,INavStorageReservation r,out NavMotionReceiver proof)
        {
            proof=null;int prior=-1;
            // Receivers are ordered by entity in the accepted Native transcript.
            int lo=0,hi=transcript.Count-1;while(lo<=hi){int mid=(lo+hi)/2,id=transcript[mid].After.Entity;if(id==current.After.Entity){prior=mid;break;}if(id<current.After.Entity)lo=mid+1;else hi=mid-1;}
            if(prior<0)return false;
            var old=transcript[prior];var a=current.After;var b=old.After;var actor=f.After[row];
            if(old.Continuation!=NavMotionContinuation.Ready||old.TailStart<0||old.TailStart>=old.Remaining.Count||globals[prior]==null||
               !ReferenceEquals(current.AcceptedRoute,old.AcceptedRoute)||!ReferenceEquals(actor.route,globals[prior].ReadOnlyBacking)||
               a.RouteIndex!=b.RouteIndex||a.Entity!=b.Entity||a.Incarnation!=b.Incarnation||a.Mobility!=b.Mobility||
               a.Request!=b.Request||a.Order!=b.Order||a.Group!=b.Group||a.Revision!=b.Revision||a.Radius!=b.Radius||
               !a.Endpoint.Equals(b.Endpoint)||!a.Goal.Equals(b.Goal)||current.TailCursor!=old.TailCursor||!current.TailAnchor.Equals(old.TailAnchor))return false;
            // Append retains each immutable typed/name/provider leg in the new
            // transcript. It never creates a chain back to historical frames.
            proof=old;return true;
        }
        protected override void ReleaseEdges(){foreach(var p in globals)p?.Release();geometry?.Release();profile?.Release();transcript?.Release();geometry=profile=null;transcript=null;source=null;}
    }
}
