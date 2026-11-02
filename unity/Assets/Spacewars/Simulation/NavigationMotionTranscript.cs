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
        internal static NavMotionReceiver Create(NavPreparedCrowdStep frame,int row,INavStorageReservation r)
        {
            using(var allocation=new NavStorageAllocation(r.TryReserve)){
                allocation.Reserve(Bytes);var value=allocation.Register(new NavMotionReceiver(frame,row));value.Retain();
                try{
                    value.beforeLocation=PinName(value,value.Before.Location);value.beforeGoal=PinName(value,value.Before.Endpoint);
                    value.afterLocation=PinName(value,value.After.Location);value.afterGoal=PinName(value,value.After.Endpoint);
                    value.chord=NavPagedList<NavTypedLeg>.Create(r.TryReserve);value.remaining=NavPagedList<NavTypedLeg>.Create(r.TryReserve);
                    value.Build(frame,row,r);value.chord.Seal();value.remaining.Seal();return value;
                }catch{value.Release();throw;}
            }
        }
        private static NavOwnedStorage PinName(NavOwnedStorage owner,NavLocation? location)
        {var pin=location?.NameReference.Owner;return pin==null?null:NavPhysicalMacro.Acquire(owner,pin);}
        private static void Append(NavPagedList<NavTypedLeg> to,IReadOnlyList<NavTypedLeg> trace)
        {for(int i=0;i<trace.Count;i++)to.Add(trace[i]);}
        private void Build(NavPreparedCrowdStep frame,int row,INavStorageReservation r)
        {
            var route=frame.RecastRoutes[row];if(route==null){Continuation=NavMotionContinuation.NoAcceptedRoute;return;}
            AcceptedRoute=NavPhysicalMacro.Acquire(this,route);
            if(!Sweep.Verified||Sweep.ProductionUnmapped){Continuation=NavMotionContinuation.Unmapped;return;}
            Continuation=NavMotionContinuation.ChordRefused;
            if(!Sweep.From.Equals(Before.Position)||!Sweep.To.Equals(After.Position)||Sweep.FromSurface!=Before.Location.SurfaceId||Sweep.ToSurface!=After.Location.SurfaceId)return;
            if(!NavRecastIntervals.TryTraceOwned(route,Sweep.Before,Before.Location,After.Position,r,out var at,out _,out _,out var actual,frame.Working,After.Entity))return;
            try{if(!at.Equals(After.Location))return;Append(chord,actual);}finally{(actual as NavOwnedStorage)?.ReleaseOwner();}
            Continuation=NavMotionContinuation.SuffixRefused;
            var actor=frame.After[row];var state=Sweep.After;
            if(!After.Endpoint.HasValue||!After.Endpoint.Value.Equals(route.Subscription.AssignedEndpoint)||!After.Goal.Equals(After.Endpoint.Value.Position)||After.Request!=route.Subscription.RequestSequence)return;
            if(actor.localIndex<0||actor.localIndex>actor.localRoute.Length||actor.routeIndex<0||actor.routeIndex>actor.route.Length)return;
            bool Link(NavPoint target){
                if(!NavRecastIntervals.Advance(route,state,at,target,r,out var next,frame.Working,After.Entity))return false;
                if(!NavRecastIntervals.TryTraceOwned(route,state,at,target,r,out var reached,out _,out _,out var trace,frame.Working,After.Entity))return false;
                try{if(next.ToSurface!=reached.SurfaceId||!reached.Position.Equals(target))return false;Append(remaining,trace);at=reached;state=next.After;return true;}
                finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
            }
            for(int i=actor.localIndex;i<actor.localRoute.Length;i++)if(!Link(actor.localRoute[i]))return;
            // Keep the actual motor cursor. Qualifying a different bank/target
            // would not describe the candidate that the owner will install.
            if(actor.routeIndex<actor.route.Length&&!NavRecastIntervals.CanTarget(route,state,at,actor.routeIndex,r,frame.Working,After.Entity))return;
            for(int i=actor.routeIndex;i<actor.route.Length;i++)if(!Link(actor.route[i]))return;
            if(!at.Equals(After.Endpoint.Value)&&at.Position.Equals(After.Endpoint.Value.Position)&&!Link(After.Endpoint.Value.Position))return;
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
        internal int Count=>receivers.Length;
        internal NavMotionReceiver this[int row]=>receivers[row];
        private NavMotionTranscript(int count,long generation,long operation):base(checked(Bytes+32+8L*count))
        {receivers=new NavMotionReceiver[count];Generation=generation;Operation=operation;}
        internal static NavMotionTranscript Create(NavPreparedCrowdStep frame,long generation,long operation,INavStorageReservation r)
        {
            using(var allocation=new NavStorageAllocation(r.TryReserve)){
                allocation.Reserve(checked(Bytes+32+8L*frame.After.Length));var result=allocation.Register(new NavMotionTranscript(frame.After.Length,generation,operation));result.Retain();
                try{result.geometry=NavPhysicalMacro.Acquire(result,frame.GeometryPin);result.profile=NavPhysicalMacro.Acquire(result,frame.ProfilePin);
                    for(int i=0;i<result.Count;i++)result.receivers[i]=NavMotionReceiver.Create(frame,i,r);return result;
                }catch{result.Release();throw;}
            }
        }
        protected override void ReleaseEdges(){foreach(var receiver in receivers)receiver?.Release();geometry?.Release();profile?.Release();}
    }
}
