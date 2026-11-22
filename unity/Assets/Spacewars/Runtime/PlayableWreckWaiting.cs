using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private int wreckRouteRevision=-1;
        private int wreckScanCursor,wreckScanRemaining,wreckActiveId,wreckActiveGeometry;
        private long wreckActiveOrder,wreckActiveSince,wreckScanSince;
        private bool wreckActivePermanent;
        private long wreckServiceTick=-1;

        private WreckRouteQueries tickWreckQueries;
        private bool WaitingForWreck(int id)=>units.TryGetValue(id,out var u)&&u.WreckWaiting&&u.CurrentOrder!=null&&u.LastOrder!=null&&u.CurrentOrder.CommandSequence==u.LastOrder.Sequence;
        private PlayableTacticalOrderSnapshot WreckWaitingOrder(Unit u)=>u.CurrentOrder;
        // One command/reconciliation observes one obstacle set. Survivors with the same
        // footprint and goal can continue the same exact flow field instead of rebuilding it.
        private sealed class WreckRouteQueries
        {
            private readonly PlayableDomain world;
            private NavGeometry geometry;
            private NavGeometry source;
            internal readonly NavigationProfile Profile;
            private NavGeometry permanent;
            private readonly Dictionary<(double Radius,int Layer),SharedFlowRouter> currentRouters=new Dictionary<(double Radius,int Layer),SharedFlowRouter>();
            private readonly Dictionary<(double Radius,int Layer),SharedFlowRouter> permanentRouters=new Dictionary<(double Radius,int Layer),SharedFlowRouter>();

            internal WreckRouteQueries(PlayableDomain world,bool reusePredicates=false)
            {
                this.world=world;
                source=world.Geometry;
                geometry=reusePredicates?SnapshotPrimitiveGeometry(source):source;
                Profile=world.profile.Navigation;
            }

            internal void ObserveGeometry(NavGeometry next)
            {
                if(ReferenceEquals(next,source))return;
                source=next;
                next=SnapshotPrimitiveGeometry(next,geometry);
                // Only untouched grid predicates survive. Fields and cached paths
                // belong to a single revision, even between impacts of the same volley.
                foreach(var key in currentRouters.Keys.ToArray())
                    currentRouters[key]=currentRouters[key].ForGeometry(ForLayer(next,key.Layer));
                geometry=next;
                permanent=null;
                permanentRouters.Clear();
            }

            // Wreck queries outlive a geometry publication within this tick. Keep
            // value footprints, never a router over a retired physical obstacle array.
            private static NavGeometry SnapshotPrimitiveGeometry(NavGeometry map,NavGeometry previous=null)
            {
                var values=new NavObstacle[map.Obstacles.Count];
                for(int i=0;i<values.Length;i++){
                    var o=map.Obstacles[i];
                    if(o.Polygon==null){values[i]=o;continue;}
                    if(previous!=null&&i<previous.Obstacles.Count&&o.GeometryEquals(previous.Obstacles[i]))values[i]=previous.Obstacles[i];
                    else values[i]=new NavObstacle(new NavPolygon(o.Polygon.Vertices.ToArray()),o.Bottom,o.Top);
                }
                return new NavGeometry(map.HalfExtent,values,map.Revision);
            }

            private static NavGeometry ForLayer(NavGeometry map,int layer)=>layer<0?map:new NavGeometry(map.HalfExtent,map.Obstacles.Where(o=>o.PhysicalLayer<0||o.PhysicalLayer==layer).ToArray(),map.Revision);
            private SharedFlowRouter Router(Dictionary<(double Radius,int Layer),SharedFlowRouter> routers,NavGeometry map,NavUnit actor)
            {
                // Grid size is fixed for this pass. Speed and turn rate do not enter FindPath.
                var key=(actor.Radius,world.navigation.Crowd.PhysicalLayer(actor.Location));
                if(!routers.TryGetValue(key,out var router))
                {
                    var terms=world.profile.Navigation.ForUnit(actor.Radius,actor.Speed,actor.TurnSpeed);
                    routers.Add(key,router=new SharedFlowRouter(ForLayer(map,key.Item2),terms));
                }
                router.TrimFields(2);return router;
            }

            internal bool TryClosed(NavUnit actor,NavPoint goal,ref int credits,ref bool permanentPhase,out bool closed)
            {
                closed=false;
                if(!permanentPhase){
                    if(!Router(currentRouters,geometry,actor).TryHasPath(actor.Position,goal,ref credits,out bool reachable))return false;
                    if(reachable)return true;
                    permanentPhase=true;
                }
                if(permanent==null){
                    var temporary=world.WreckObstacles().ToArray();
                    permanent=new NavGeometry(geometry.HalfExtent,geometry.Obstacles.Where(o=>!temporary.Any(w=>w.GeometryEquals(o))).ToArray(),geometry.Revision);
                }
                if(!Router(permanentRouters,permanent,actor).TryHasPath(actor.Position,goal,ref credits,out bool withoutWreck))return false;
                // A flat reachability proof must never invent a connection between
                // authored layers. Ordinary typed host service owns that decision.
                closed=withoutWreck;
                return true;
            }


        }

        private WreckRouteQueries QueriesForCurrentWrecks()
        {
            if(!wrecks.Values.Any(w=>w.Blocks(Tick)))return null;
            if(tickWreckQueries==null||!ReferenceEquals(tickWreckQueries.Profile,profile.Navigation))tickWreckQueries=new WreckRouteQueries(this,true);
            else tickWreckQueries.ObserveGeometry(Geometry);
            return tickWreckQueries;
        }
        private void RetryWreckWaits()=>ReconcileWreckRoutes();
        // Event handlers publish only a coalesced scan watermark. They perform no
        // reachability/search work. Preserve the cursor and oldest request on bursts.
        private void ReconcileWreckRoutes()
        {
            if(wreckRouteRevision==Geometry.Revision)return;
            wreckRouteRevision=Geometry.Revision;
            ScheduleWreckScan();
        }
        private void ScheduleWreckScan()
        {
            if(wreckScanRemaining==0&&wreckActiveId==0)wreckScanSince=Tick;
            wreckScanRemaining=Math.Max(wreckScanRemaining,units.SlotCount);
        }
        private sealed class WreckConnectorFailure
        {
            internal readonly long Request,Mobility;
            internal readonly int GeometryRevision,ProfileRevision;
            internal readonly NavLocation Origin;
            internal readonly string HoldBinding;

            internal WreckConnectorFailure(long request,long mobility,int geometryRevision,int profileRevision,NavLocation origin,string holdBinding)
            {
                Request=request;Mobility=mobility;GeometryRevision=geometryRevision;ProfileRevision=profileRevision;
                Origin=origin;HoldBinding=holdBinding;
            }
        }

        private void RevisitWreckFailure(NavigationRequest request,QueueExecution active)
        {
            if(!units.TryGetValue(request.Entity,out var unit))return;
            if(active!=null&&WaitingForWreck(unit.Id)&&!active.Interrupted&&active.Assigned.HasValue&&active.Assigned.Equals(request.GoalLocation)&&
                request.BaseGeometry.Revision==Geometry.Revision&&request.ProfileRevision==profile.Navigation.Revision&&
                navigation.TechnicalFailureFor(unit.Id)==NavSolveStatus.BlockedConnector&&TryActor(unit.Id,out var actor)&&
                FailureMatches(actor,active)&&request.StartLocation.HasValue&&request.StartLocation.Value.Equals(actor.Location)&&
                request.MemberIdentity.MobilityRevision==actor.MobilityRevision)
            {
                // The wreck scan's flat reachability can be open while the
                // authoritative connector still rejects this exact input.
                unit.WreckConnector=new WreckConnectorFailure(request.Request,actor.MobilityRevision,Geometry.Revision,
                    request.ProfileRevision,DomainLocation(request.StartLocation).Value,request.HeldIdentity);
                unit.WreckObservedRevision=Geometry.Revision;
                return;
            }
            if(wrecks.Count==0)return;
            unit.WreckObservedRevision=-1;unit.WreckFailureGeometry=Geometry.Revision;ScheduleWreckScan();
        }

        private void ObserveWreckConnectorInputs(Unit unit,NavUnit actor)
        {
            var failed=unit.WreckConnector;
            if(failed==null)return;
            bool waiting=WaitingForWreck(unit.Id);
            if(waiting&&failed.Request==actor.FailedRequest&&navigation.TechnicalFailureFor(unit.Id)==NavSolveStatus.BlockedConnector&&
                failed.Mobility==actor.MobilityRevision&&
                failed.GeometryRevision==Geometry.Revision&&failed.ProfileRevision==profile.Navigation.Revision&&
                failed.Origin.Equals(actor.Location)&&navigation.CurrentHeldBinding(failed.HoldBinding,unit.Id))return;
            unit.WreckConnector=null;
            if(!waiting)return;
            unit.WreckObservedRevision=-1;
            ScheduleWreckScan();
        }
        private bool NeedsWreckRouteCheck(Unit unit,NavUnit actor,QueueExecution execution)
        {
            if(execution==null||!execution.Assigned.HasValue||execution.Interrupted||execution.Stamp.Revision!=unit.LastOrder?.Revision||
                (execution.Issuance.Command.Kind!=PlayableCommandKind.Move&&execution.Issuance.Command.Kind!=PlayableCommandKind.AttackMove))return false;
            if(WaitingForWreck(unit.Id))return true;
            // A settled formation member may keep AttackMove active while the
            // squad fights. A wreck at the click point cannot undo its arrival.
            return !actor.Moving&&wrecks.Count>0&&!AtAssignedTacticalEndpoint(execution,actor);
        }
        private void ServiceWreckRoutes()
        {
            if(wreckServiceTick==Tick)return;wreckServiceTick=Tick;
            using var wreckReconciliationTiming=PerformanceProfile.Measure(ProfileStage.WreckReconcile);
            int credits=profile.WreckReconcileCellsPerTick,slots=profile.WreckReconcileSlotsPerTick,queries=profile.WreckReconcileQueriesPerTick;
            while(credits>0&&queries>0){
                if(wreckActiveId==0){
                    while(slots>0&&wreckScanRemaining>0&&units.SlotCount>0){
                        PerformanceProfile.Add(ProfileWork.WreckScannedSlots,1);slots--;wreckScanRemaining--;if(wreckScanCursor>=units.SlotCount)wreckScanCursor=0;
                        if(!units.TryReadSlot(wreckScanCursor++,out int id,out var u))continue;
                        if(u.WreckObservedRevision==Geometry.Revision||u.Air!=null||!TryActor(id,out var actor)||actor.Held||navigation.IsPending(id)||!tacticalQueues.TryGetValue(id,out var q))continue;
                        var e=q.Pending??q.Active;
                        if(!NeedsWreckRouteCheck(u,actor,e)){u.WreckObservedRevision=Geometry.Revision;continue;}
                        wreckActiveId=id;wreckActiveOrder=e.Stamp.Revision;wreckActiveSince=wreckScanSince;wreckActiveGeometry=Geometry.Revision;wreckActivePermanent=u.WreckFailureGeometry==Geometry.Revision&&e.TerminalFailure?.Outcome==NavigationOutcome.Unreachable&&actor.FailedRequest==e.AttemptRequest&&e.Assigned.Equals(e.AttemptLocation);break;
                    }
                    if(wreckActiveId==0)break;
                }
                if(!units.TryGetValue(wreckActiveId,out var unit)||unit.Air!=null||unit.LastOrder?.Revision!=wreckActiveOrder||!TryActor(wreckActiveId,out var current)||current.Held||!tacticalQueues.TryGetValue(wreckActiveId,out var queue)){wreckActiveId=0;continue;}
                var execution=queue.Pending??queue.Active;
                if(execution==null||execution.Stamp.Revision!=wreckActiveOrder||navigation.IsPending(unit.Id)||!NeedsWreckRouteCheck(unit,current,execution)){
                    wreckActiveId=0;continue;
                }
                if(wreckActiveGeometry!=Geometry.Revision){wreckActiveGeometry=Geometry.Revision;wreckActivePermanent=false;}
                queries--;PerformanceProfile.Add(ProfileWork.WreckQueries,1);
                PerformanceProfile.Maximum(ProfileWork.WreckMaxQueueAge,Tick-wreckActiveSince);
                var query=QueriesForCurrentWrecks();bool closed=false;
                var assigned=execution.Assigned.Value;
                int before=credits;bool done;
                using(PerformanceProfile.Measure(ProfileStage.WreckReachability))done=query==null||query.TryClosed(current,assigned.Position,ref credits,ref wreckActivePermanent,out closed);
                PerformanceProfile.Add(ProfileWork.WreckSearchCredits,before-credits);
                if(!done){
                    // An unstable/large query cannot own every subsequent tick.
                    // Keep the scan age/cursor and resume cached fields on its next turn.
                    if(Tick-wreckActiveSince>=profile.WreckResponseTicks){ScheduleWreckScan();wreckActiveId=0;}
                    break;
                }
                bool waiting=WaitingForWreck(unit.Id),committed=true;
                if(closed&&!waiting)committed=CommitInternalTactical(execution.Issuance,new[]{unit.Id},InternalTacticalKind.WreckWait,activationRevision:execution.Stamp.Revision,wreckSlot:assigned);
                else if(waiting&&!closed)committed=CommitInternalTactical(execution.Issuance,new[]{unit.Id},InternalTacticalKind.WreckResume,assigned.Position,execution.Stamp.Revision);
                if(!committed){ScheduleWreckScan();wreckActiveId=0;break;}
                unit.WreckObservedRevision=Geometry.Revision;wreckActiveId=0;
            }
        }
        private void WriteWreckScheduling(BinaryWriter w)
        {
            w.Write(0x57515331);w.Write(3);w.Write(wreckScanSince);w.Write(wreckRouteRevision);w.Write(wreckScanCursor);w.Write(wreckScanRemaining);w.Write(wreckActiveId);w.Write(wreckActiveGeometry);w.Write(wreckActiveOrder);w.Write(wreckActiveSince);w.Write(wreckServiceTick);
            WorldWire.Array(w,units.Values.ToArray(),u=>{
                w.Write(u.Id);w.Write(u.WreckObservedRevision);w.Write(u.WreckFailureGeometry);
                var rejected=u.WreckConnector;
                w.Write(rejected!=null);
                if(rejected==null)return;
                w.Write(rejected.Request);w.Write(rejected.Mobility);w.Write(rejected.GeometryRevision);w.Write(rejected.ProfileRevision);
                GroupOrderWire.WriteLocation(w,rejected.Origin);WorldWire.String(w,rejected.HoldBinding);
            });
        }
        private void ReadWreckScheduling(BinaryReader r)
        {
            if(r.ReadInt32()!=0x57515331)throw new ArgumentException("Invalid wreck scheduling tag.");
            int version=r.ReadInt32();
            if(version!=2&&version!=3)throw new ArgumentException("Invalid wreck scheduling version.");
            wreckScanSince=r.ReadInt64();wreckRouteRevision=r.ReadInt32();wreckScanCursor=r.ReadInt32();wreckScanRemaining=r.ReadInt32();wreckActiveId=r.ReadInt32();wreckActiveGeometry=r.ReadInt32();wreckActiveOrder=r.ReadInt64();wreckActiveSince=r.ReadInt64();wreckServiceTick=r.ReadInt64();
            if(wreckScanSince<0||wreckScanSince>Tick||wreckRouteRevision< -1||wreckRouteRevision>Geometry.Revision||wreckScanCursor<0||wreckScanCursor>units.SlotCount||wreckScanRemaining<0||wreckScanRemaining>units.SlotCount||wreckActiveId<0||wreckActiveGeometry<0||wreckActiveGeometry>Geometry.Revision||wreckActiveSince<0||wreckActiveSince>Tick||wreckServiceTick< -1||wreckServiceTick>Tick)throw new ArgumentException("Invalid wreck scheduling state.");
            var seen=new HashSet<int>();
            WorldWire.Array(r,()=>{
                int id=r.ReadInt32(),observed=r.ReadInt32(),failureGeometry=r.ReadInt32();
                if(!seen.Add(id)||!units.TryGetValue(id,out var unit)||observed< -1||observed>Geometry.Revision||
                    failureGeometry< -1||failureGeometry>Geometry.Revision)throw new ArgumentException("Invalid wreck scheduling member.");
                unit.WreckObservedRevision=observed;unit.WreckFailureGeometry=failureGeometry;
                if(version>=3&&WorldWire.Boolean(r)){
                    long request=r.ReadInt64(),mobility=r.ReadInt64();
                    int geometry=r.ReadInt32(),routingProfile=r.ReadInt32();
                    var origin=GroupOrderWire.ReadLocation(r);string hold=WorldWire.String(r);
                    if(request<=0||request>navigation.Admission.PublishedThroughRequestSequence||mobility<0||
                        geometry<0||geometry>Geometry.Revision||routingProfile<0||!origin.HasValue||hold==null)
                        throw new ArgumentException("Invalid wreck connector observation.");
                    unit.WreckConnector=new WreckConnectorFailure(request,mobility,geometry,routingProfile,origin.Value,hold);
                }
                return id;
            });
            if(seen.Count!=units.Count)throw new ArgumentException("Missing wreck scheduling members.");
            // Solver scratch restarts; queue position and first-service age are authority.
            wreckActivePermanent=false;
        }
    }
}
