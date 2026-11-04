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
        private void RevisitWreckFailure(int id)
        {
            if(wrecks.Count==0||!units.TryGetValue(id,out var u))return;
            u.WreckObservedRevision=-1;u.WreckFailureGeometry=Geometry.Revision;ScheduleWreckScan();
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
                        if(e==null||e.Stamp.Revision!=u.LastOrder?.Revision||(e.Issuance.Command.Kind!=PlayableCommandKind.Move&&e.Issuance.Command.Kind!=PlayableCommandKind.AttackMove))continue;
                        if(actor.Moving&&!WaitingForWreck(id)){u.WreckObservedRevision=Geometry.Revision;continue;}
                        if(wrecks.Count==0&&!WaitingForWreck(id)){u.WreckObservedRevision=Geometry.Revision;continue;}
                        wreckActiveId=id;wreckActiveOrder=e.Stamp.Revision;wreckActiveSince=wreckScanSince;wreckActiveGeometry=Geometry.Revision;wreckActivePermanent=u.WreckFailureGeometry==Geometry.Revision&&e.TerminalFailure?.Outcome==NavigationOutcome.Unreachable&&actor.FailedRequest==e.AttemptRequest;break;
                    }
                    if(wreckActiveId==0)break;
                }
                if(!units.TryGetValue(wreckActiveId,out var unit)||unit.Air!=null||unit.LastOrder?.Revision!=wreckActiveOrder||!TryActor(wreckActiveId,out var current)||current.Held||!tacticalQueues.TryGetValue(wreckActiveId,out var queue)){wreckActiveId=0;continue;}
                var execution=queue.Pending??queue.Active;
                if(execution==null||execution.Stamp.Revision!=wreckActiveOrder){wreckActiveId=0;continue;}
                if(wreckActiveGeometry!=Geometry.Revision){wreckActiveGeometry=Geometry.Revision;wreckActivePermanent=false;}
                queries--;PerformanceProfile.Add(ProfileWork.WreckQueries,1);
                PerformanceProfile.Maximum(ProfileWork.WreckMaxQueueAge,Tick-wreckActiveSince);
                var query=QueriesForCurrentWrecks();bool closed=false;
                int before=credits;bool done;
                using(PerformanceProfile.Measure(ProfileStage.WreckReachability))done=query==null||query.TryClosed(current,execution.Issuance.Command.Target,ref credits,ref wreckActivePermanent,out closed);
                PerformanceProfile.Add(ProfileWork.WreckSearchCredits,before-credits);
                if(!done){
                    // An unstable/large query cannot own every subsequent tick.
                    // Keep the scan age/cursor and resume cached fields on its next turn.
                    if(Tick-wreckActiveSince>=profile.WreckResponseTicks){ScheduleWreckScan();wreckActiveId=0;}
                    break;
                }
                bool waiting=WaitingForWreck(unit.Id),committed=true;
                if(closed&&!waiting)committed=CommitInternalTactical(execution.Issuance,new[]{unit.Id},InternalTacticalKind.WreckWait,activationRevision:execution.Stamp.Revision);
                else if(waiting&&!closed)committed=CommitInternalTactical(execution.Issuance,new[]{unit.Id},InternalTacticalKind.Combat,execution.Issuance.Command.Target,execution.Stamp.Revision);
                if(!committed){ScheduleWreckScan();wreckActiveId=0;break;}
                unit.WreckObservedRevision=Geometry.Revision;wreckActiveId=0;
            }
        }
        private void WriteWreckScheduling(BinaryWriter w)
        {
            w.Write(0x57515331);w.Write(2);w.Write(wreckScanSince);w.Write(wreckRouteRevision);w.Write(wreckScanCursor);w.Write(wreckScanRemaining);w.Write(wreckActiveId);w.Write(wreckActiveGeometry);w.Write(wreckActiveOrder);w.Write(wreckActiveSince);w.Write(wreckServiceTick);
            WorldWire.Array(w,units.Values.ToArray(),u=>{w.Write(u.Id);w.Write(u.WreckObservedRevision);w.Write(u.WreckFailureGeometry);});
        }
        private void ReadWreckScheduling(BinaryReader r)
        {
            if(r.ReadInt32()!=0x57515331||r.ReadInt32()!=2)throw new ArgumentException("Invalid wreck scheduling version.");
            wreckScanSince=r.ReadInt64();wreckRouteRevision=r.ReadInt32();wreckScanCursor=r.ReadInt32();wreckScanRemaining=r.ReadInt32();wreckActiveId=r.ReadInt32();wreckActiveGeometry=r.ReadInt32();wreckActiveOrder=r.ReadInt64();wreckActiveSince=r.ReadInt64();wreckServiceTick=r.ReadInt64();
            if(wreckScanSince<0||wreckScanSince>Tick||wreckRouteRevision< -1||wreckRouteRevision>Geometry.Revision||wreckScanCursor<0||wreckScanCursor>units.SlotCount||wreckScanRemaining<0||wreckScanRemaining>units.SlotCount||wreckActiveId<0||wreckActiveGeometry<0||wreckActiveGeometry>Geometry.Revision||wreckActiveSince<0||wreckActiveSince>Tick||wreckServiceTick< -1||wreckServiceTick>Tick)throw new ArgumentException("Invalid wreck scheduling state.");
            var seen=new HashSet<int>();foreach(var row in WorldWire.Array(r,()=>new[]{r.ReadInt32(),r.ReadInt32(),r.ReadInt32()})){
                if(!seen.Add(row[0])||!units.TryGetValue(row[0],out var u)||row[1]< -1||row[1]>Geometry.Revision||row[2]< -1||row[2]>Geometry.Revision)throw new ArgumentException("Invalid wreck scheduling member.");u.WreckObservedRevision=row[1];u.WreckFailureGeometry=row[2];
            }
            if(seen.Count!=units.Count)throw new ArgumentException("Missing wreck scheduling members.");
            // Solver scratch restarts; queue position and first-service age are authority.
            wreckActivePermanent=false;
        }
    }
}
