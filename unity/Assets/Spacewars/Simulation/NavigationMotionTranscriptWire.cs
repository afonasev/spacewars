using System;

namespace Spacewars.Simulation
{
    internal readonly partial struct NavMotionActorState
    {
        // Live flat actors can carry the canonical implicit flat token. Preserve
        // that representation; do not invent an owned provider/name on capture.
        private static void WriteLocation(NavPhysicalBodyWriter writer,NavLocation location)
        {
            bool flat=location.NameReference.Owner==null&&location.SurfaceId==NavLocation.FlatSurface;
            writer.Output.Write(flat);if(flat){writer.Output.Write(location.Position.X);writer.Output.Write(location.Position.Z);}else NavPhysicalNamedValue.WriteLocation(writer,location);
        }
        private static NavLocation ReadLocation(NavPhysicalBodyReader reader)
        {return NavPhysicalMacro.ReadBoolean(reader)?new NavLocation(NavPhysicalGeometryValue.Point(reader),NavLocation.FlatSurface):NavPhysicalNamedValue.ReadLocation(reader);}
        internal void Write(NavPhysicalBodyWriter writer)
        {
            var w=writer.Output;w.Write(Entity);w.Write(Team);w.Write(Incarnation);w.Write(Mobility);w.Write(Group);w.Write(Revision);w.Write(Request);w.Write(Order);
            w.Write(Position.X);w.Write(Position.Z);w.Write(Goal.X);w.Write(Goal.Z);WriteLocation(writer,Location);w.Write(Endpoint.HasValue);if(Endpoint.HasValue)WriteLocation(writer,Endpoint.Value);
            w.Write(Heading);w.Write(Radius);w.Write(Speed);w.Write(TurnSpeed);w.Write(Moving);w.Write(Held);w.Write(CanYield);w.Write((int)Outcome);w.Write(RouteIndex);w.Write(LocalIndex);w.Write(NoProgressTicks);w.Write(BlockedSeconds);
        }
        internal static NavMotionActorState Read(NavPhysicalBodyReader reader)
        {
            var r=reader.Input;int entity=r.ReadInt32(),team=r.ReadInt32();long incarnation=r.ReadInt64(),mobility=r.ReadInt64(),group=r.ReadInt64(),revision=r.ReadInt64(),request=r.ReadInt64(),order=r.ReadInt64();
            double Number(){double v=r.ReadDouble();if(double.IsNaN(v)||double.IsInfinity(v))throw new ArgumentException("Nonfinite motion state.");return v;}
            var position=new NavPoint(Number(),Number());var goal=new NavPoint(Number(),Number());var location=ReadLocation(reader);NavLocation? endpoint=NavPhysicalMacro.ReadBoolean(reader)?ReadLocation(reader):(NavLocation?)null;
            double heading=Number(),radius=Number(),speed=Number(),turn=Number();bool moving=NavPhysicalMacro.ReadBoolean(reader),held=NavPhysicalMacro.ReadBoolean(reader),yield=NavPhysicalMacro.ReadBoolean(reader);var outcome=(NavigationOutcome)r.ReadInt32();int route=r.ReadInt32(),local=r.ReadInt32(),progress=r.ReadInt32();double blocked=Number();
            if(entity<0||incarnation<0||mobility<0||group<0||revision<0||request<0||order<0||radius<=0||speed<0||turn<0||route<0||local<0||progress<0||blocked<0||!position.Equals(location.Position)||!Enum.IsDefined(typeof(NavigationOutcome),outcome))throw new ArgumentException("Invalid motion actor source.");
            using(NavStorageScratch.Reserve(reader.Reservation,NavStorageLayout.ObjectBytesBound(typeof(NavUnit)))){
                var actor=new NavUnit(entity,position,radius,speed,turn,yield){Team=team,Incarnation=incarnation,MobilityRevision=mobility,InstalledGroupId=group,InstalledRevision=revision,InstalledRequest=request,InstalledOrder=order,Goal=goal,Location=location,GoalLocation=endpoint,Heading=heading,Moving=moving,Held=held,Outcome=outcome,routeIndex=route,localIndex=local,noProgressTicks=progress,blockedSeconds=blocked};return new NavMotionActorState(actor);
            }
        }
    }
    internal sealed partial class NavMotionReceiver : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&chord?.IsImmutableCaptureRoot==true&&remaining?.IsImmutableCaptureRoot==true;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.TypedRoute,value:NavPhysicalValueKind.MotionReceiverV1);
        private NavMotionReceiver(NavMotionActorState before,NavMotionActorState after,bool cooperative,bool production,NavRecastSweepReceipt sweep):base(Bytes)
        {Before=before;After=after;Cooperative=cooperative;ProductionException=production;Sweep=sweep;}
        private static void WriteStep(NavPhysicalBodyWriter writer,NavRecastStepState state)
        {writer.Output.Write(state.Phase);writer.Output.Write(state.Cursor);writer.Output.Write(state.Station);writer.Output.Write(state.HighWater);}
        private static NavRecastStepState ReadStep(NavPhysicalBodyReader reader)
        {var r=reader.Input;int phase=r.ReadInt32(),cursor=r.ReadInt32();double station=r.ReadDouble(),high=r.ReadDouble();if(phase<0||phase>3||cursor<0||double.IsNaN(station)||double.IsInfinity(station)||double.IsNaN(high)||double.IsInfinity(high))throw new ArgumentException("Invalid motion interval state.");return new NavRecastStepState(phase,cursor,station,high);}
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot)throw new ArgumentException("Unsealed motion source.");
            Before.Write(writer);After.Write(writer);var w=writer.Output;w.Write(Cooperative);w.Write(ProductionException);w.Write((int)Continuation);w.Write(Sweep.Verified);w.Write(Sweep.ProductionUnmapped);w.Write(Sweep.FromSurface!=null);w.Write(Sweep.ToSurface!=null);WriteStep(writer,Sweep.Before);WriteStep(writer,Sweep.After);
            w.Write(Sweep.From.X);w.Write(Sweep.From.Z);w.Write(Sweep.To.X);w.Write(Sweep.To.Z);w.Write(Sweep.FirstCrossing.X);w.Write(Sweep.FirstCrossing.Z);w.Write(Sweep.LastCrossing.X);w.Write(Sweep.LastCrossing.Z);w.Write(Sweep.FirstPortalLeg);w.Write(Sweep.LastPortalLeg);w.Write(Sweep.PortalCount);
            NavPhysicalMacro.WriteEdge(writer,this,AcceptedRoute,true);NavPhysicalMacro.WriteEdge(writer,this,chord);NavPhysicalMacro.WriteEdge(writer,this,remaining);
        }
        internal static NavMotionReceiver ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var before=NavMotionActorState.Read(reader);var after=NavMotionActorState.Read(reader);bool cooperative=NavPhysicalMacro.ReadBoolean(reader),production=NavPhysicalMacro.ReadBoolean(reader);var continuation=(NavMotionContinuation)reader.Input.ReadInt32();
            var sweep=new NavRecastSweepReceipt{Verified=NavPhysicalMacro.ReadBoolean(reader),ProductionUnmapped=NavPhysicalMacro.ReadBoolean(reader)};bool fromSurface=NavPhysicalMacro.ReadBoolean(reader),toSurface=NavPhysicalMacro.ReadBoolean(reader);sweep.Before=ReadStep(reader);sweep.After=ReadStep(reader);
            var r=reader.Input;sweep.From=new NavPoint(r.ReadDouble(),r.ReadDouble());sweep.To=new NavPoint(r.ReadDouble(),r.ReadDouble());sweep.FirstCrossing=new NavPoint(r.ReadDouble(),r.ReadDouble());sweep.LastCrossing=new NavPoint(r.ReadDouble(),r.ReadDouble());sweep.FirstPortalLeg=r.ReadInt32();sweep.LastPortalLeg=r.ReadInt32();sweep.PortalCount=r.ReadInt32();
            var route=NavPhysicalMacro.ReadEdge<NavTypedRoute>(reader,true);var chord=NavPhysicalMacro.ReadEdge<NavPagedList<NavTypedLeg>>(reader);var remaining=NavPhysicalMacro.ReadEdge<NavPagedList<NavTypedLeg>>(reader);
            if(!Enum.IsDefined(typeof(NavMotionContinuation),continuation)||before.Entity!=after.Entity||before.Incarnation!=after.Incarnation||sweep.PortalCount<0||sweep.FirstPortalLeg<-1||sweep.LastPortalLeg<-1||sweep.PortalCount>0&&(route==null||sweep.FirstPortalLeg<0||sweep.LastPortalLeg<0||sweep.FirstPortalLeg>=route.Legs.Count||sweep.LastPortalLeg>=route.Legs.Count))throw new ArgumentException("Invalid motion receiver identity/trace.");
            if(route==null&&continuation!=NavMotionContinuation.NoAcceptedRoute||route!=null&&continuation==NavMotionContinuation.NoAcceptedRoute)throw new ArgumentException("Motion route applicability mismatch.");
            sweep.FromSurface=fromSurface?before.Location.SurfaceId:null;sweep.ToSurface=toSurface?after.Location.SurfaceId:null;
            if(sweep.PortalCount>0){sweep.FirstSeam=route.Legs[sweep.FirstPortalLeg].PortalId;sweep.LastSeam=route.Legs[sweep.LastPortalLeg].PortalId;}
            if(continuation==NavMotionContinuation.Ready){
                if(!sweep.Verified||sweep.ProductionUnmapped||!sweep.From.Equals(before.Position)||!sweep.To.Equals(after.Position)||!after.Endpoint.HasValue||!after.Endpoint.Value.Equals(route.Subscription.AssignedEndpoint)||route.Subscription.RequestSequence!=after.Request)throw new ArgumentException("Motion accepted source binding mismatch.");
                var at=before.Location;foreach(var leg in chord){if(!leg.From.Equals(at))throw new ArgumentException("Motion chord gap.");at=leg.To;}if(!at.Equals(after.Location))throw new ArgumentException("Motion candidate mismatch.");foreach(var leg in remaining){if(!leg.From.Equals(at))throw new ArgumentException("Motion suffix gap.");at=leg.To;}if(!at.Equals(after.Endpoint.Value))throw new ArgumentException("Incomplete motion suffix.");
            }
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(Bytes);var value=allocation.Register(new NavMotionReceiver(before,after,cooperative,production,sweep));value.Continuation=continuation;value.AcceptedRoute=NavPhysicalMacro.Acquire(value,route);value.chord=NavPhysicalMacro.Acquire(value,chord);value.remaining=NavPhysicalMacro.Acquire(value,remaining);value.beforeLocation=PinName(value,before.Location);value.beforeGoal=PinName(value,before.Endpoint);value.afterLocation=PinName(value,after.Location);value.afterGoal=PinName(value,after.Endpoint);return value;
            }
        }
    }
    internal sealed partial class NavMotionTranscript : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>IsRegistered;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.TypedRoute,value:NavPhysicalValueKind.MotionTranscriptV1);
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            writer.Output.Write(Generation);writer.Output.Write(Operation);writer.Output.Write(Count);NavPhysicalMacro.WriteEdge(writer,this,geometry);NavPhysicalMacro.WriteEdge(writer,this,profile);
            foreach(var receiver in receivers)NavPhysicalMacro.WriteEdge(writer,this,receiver);
        }
        internal static NavMotionTranscript ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            long generation=reader.Input.ReadInt64(),operation=reader.Input.ReadInt64();int count=reader.Count(NavPhysicalCaptureIndex.MaxDefinitions);if(generation<0||operation<0)throw new ArgumentException("Invalid motion generation/operation.");var geometry=NavPhysicalMacro.ReadEdge<NavOwnedStorage>(reader);var profile=NavPhysicalMacro.ReadEdge<NavOwnedStorage>(reader);
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(checked(Bytes+32+8L*count));var value=allocation.Register(new NavMotionTranscript(count,generation,operation));value.geometry=NavPhysicalMacro.Acquire(value,geometry);value.profile=NavPhysicalMacro.Acquire(value,profile);
                for(int i=0;i<count;i++){var receiver=NavPhysicalMacro.ReadEdge<NavMotionReceiver>(reader);if(i>0&&receiver.Before.Entity<=value.receivers[i-1].Before.Entity)throw new ArgumentException("Motion receiver ordering mismatch.");value.receivers[i]=NavPhysicalMacro.Acquire(value,receiver);}return value;
            }
        }
    }
}
