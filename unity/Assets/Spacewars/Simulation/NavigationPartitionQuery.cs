using System;

namespace Spacewars.Simulation
{
    public sealed partial class NavIncrementalSingletonRoute
    {
        // Query v1 keeps every certified terminal candidate. Only expanded graph
        // nodes pay their terminal probes; no N*C field is seeded beforehand.
        // Labels are forward g, queue order is (conservative g+h,node), terminal
        // order is (g+attachment+egress,node,candidate). Equal g keeps the first
        // predecessor in that stable queue/arc order. Legacy modes never use it.
        private double QueueCost(int node,double cost)
        {
            if(queryRegion==null)return cost;
            var provider=(PartitionNavigationProvider)graph.PhysicalProvider;
            if(!graph.SupportsPackedField)return cost;
            // Partition constructs every surface with DistanceCost=1. Its
            // cardinal arcs use Euclidean distance; seam traces partition the
            // same segment with zero-displacement, zero-cost surface changes
            // (TerrainLocations.TryTraceSegments). Thus even many surfaces
            // cannot shortcut the XZ metric. The concrete packed Partition
            // graph is required; other graph layouts fall back to h=0.
            // Widen downwards for the
            // simple path and its source-bounded trace subdivisions; this is a bound,
            // never an epsilon comparison of candidate costs or ties.
            double h=LayeredNavigationProvider.Distance(graph.Nodes[node].Location.Position,subscription.AssignedEndpoint.Position)*graph.SurfaceCost(subscription.AssignedEndpoint.SurfaceId);
            double margin=8.0*(graph.Nodes.Count+4)*(provider.TracePortalBound+4.0)*2.2204460492503131e-16;
            return Math.Max(0,(cost+h)*(1-margin));
        }
        private NavQueueEntry QueryEntry(int node,double cost)=>new NavQueueEntry{Node=node,Cost=QueueCost(node,cost)};
        private void ValidateQueryCursors()
        {
            int n=graph.Nodes.Count,c=queryRegion.Candidates.Count;
            bool valid=phase==Phase.Direct?cursor==0:
                phase==Phase.QueryValidate||phase==Phase.Endpoints?cursor>=0&&cursor<=c:
                phase==Phase.Origins?cursor>=0&&cursor<=n:
                phase==Phase.Search?!startSeeded?cursor>=-1&&cursor<n:cursor==0:
                phase==Phase.QueryCandidates||phase==Phase.Trace||phase==Phase.Reconstruct?cursor==0:
                phase==Phase.Done&&Status!=NavSolveStatus.Pending&&(Status!=NavSolveStatus.Ready||cursor==0);
            if(!valid)throw new ArgumentException("Query cursor contradicts its exact phase.");
        }
        private void ValidateQueryState(INavStorageReservation allocation)
        {
            ValidateQueryCursors();
            int n=graph.Nodes.Count,c=queryRegion.Candidates.Count;
            if(cursor< -1||cursor>Math.Max(n,c)||queryCandidate<0||queryCandidate>c||queryChosenCandidate< -1||queryChosenCandidate>=c||
               current< -1||current>=n||chosen< -1||chosen>=n||traceNode<0||traceNode>=Math.Max(1,n)||arcCursor<0||
               current>=0&&arcCursor>graph.Outgoing(current).Count||starts.Count>n||ends.Count>c||distance.Count>maxStates||previous.Count>distance.Count||
               frontier.Count>maxStates||chain.Count>n||reconstructCursor<0||reconstructCursor>chain.Count+3||reconstructLeg<0||
               (Status==NavSolveStatus.Pending)==(phase==Phase.Done)||(Route!=null)!=(Status==NavSolveStatus.Ready)||
               phase==Phase.QueryCandidates&&(current<0||!startSeeded)||
               (chosen<0)!=(queryChosenCandidate<0)||(chosen<0)!=double.IsPositiveInfinity(best))throw new ArgumentException("Invalid query state dimensions/status/cursors.");
            bool searching=phase==Phase.Search||phase==Phase.QueryCandidates||phase==Phase.Trace||phase==Phase.Reconstruct||phase==Phase.Done;
            bool failed=phase==Phase.Done&&Status!=NavSolveStatus.Ready;
            int originPrefix=phase==Phase.Origins?cursor:searching?n:0;
            for(int i=0;!failed&&i<originPrefix;i++){
                bool valid=NavPhysicalSolveValidation.Attachment(graph,subscription.Origin,i,allocation,out double cost);
                bool present=starts.TryGetValue(i,out double value);
                if(valid!=present||present&&cost!=value)throw new ArgumentException("Query origin prefix changed.");
            }
            foreach(var pair in starts)if(pair.Key<0||pair.Key>=(failed?n:originPrefix)||failed&&(!NavPhysicalSolveValidation.Attachment(graph,subscription.Origin,pair.Key,allocation,out double expected)||expected!=pair.Value))throw new ArgumentException("Query origin outside/changed prefix.");
            int endPrefix=phase==Phase.Direct||phase==Phase.QueryValidate?0:phase==Phase.Endpoints?cursor:c;
            for(int i=0;!failed&&i<endPrefix;i++){
                var at=queryRegion.Candidates[i];bool valid=NavPhysicalSolveValidation.Sweep(graph,at,subscription.AssignedEndpoint,allocation);
                bool present=ends.TryGetValue(i,out double value);double cost=LayeredNavigationProvider.Distance(at.Position,subscription.AssignedEndpoint.Position)*graph.SurfaceCost(at.SurfaceId);
                if(valid!=present||present&&cost!=value)throw new ArgumentException("Query egress prefix changed.");
            }
            foreach(var pair in ends)if(pair.Key<0||pair.Key>=(failed?c:endPrefix)||failed&&(!NavPhysicalSolveValidation.Sweep(graph,queryRegion.Candidates[pair.Key],subscription.AssignedEndpoint,allocation)||pair.Value!=LayeredNavigationProvider.Distance(queryRegion.Candidates[pair.Key].Position,subscription.AssignedEndpoint.Position)*graph.SurfaceCost(queryRegion.Candidates[pair.Key].SurfaceId)))throw new ArgumentException("Query egress outside/changed prefix.");
            foreach(var pair in distance)if(pair.Key<0||pair.Key>=n||!NavPhysicalSolveValidation.Cost(pair.Value)||!previous.ContainsKey(pair.Key)&&(!starts.TryGetValue(pair.Key,out double seed)||seed!=pair.Value))throw new ArgumentException("Invalid query label.");
            foreach(var pair in previous){NavPhysicalSolveValidation.Arc(graph,pair.Value);if(pair.Key!=pair.Value.To||!distance.TryGetValue(pair.Key,out double value)||!distance.TryGetValue(pair.Value.From,out double parent)||value<parent+pair.Value.Cost)throw new ArgumentException("Invalid query predecessor.");}
            foreach(var entry in frontier)if(entry.Node<0||entry.Node>=n||entry.Candidate!=0||!distance.TryGetValue(entry.Node,out double cost)||entry.Cost!=QueueCost(entry.Node,cost))throw new ArgumentException("Query frontier priority changed.");
            var seen=new int[n];int epoch=0;
            foreach(var pair in previous){int walk=pair.Key;epoch++;while(previous.TryGetValue(walk,out var arc)&&seen[walk]!=-1){if(seen[walk]==epoch)throw new ArgumentException("Query predecessor cycle.");seen[walk]=epoch;walk=arc.From;}walk=pair.Key;while(previous.TryGetValue(walk,out var arc)&&seen[walk]==epoch){seen[walk]=-1;walk=arc.From;}}
            int atNode=chosen;foreach(var arc in chain){NavPhysicalSolveValidation.Arc(graph,arc);if(arc.To!=atNode||!previous.TryGetValue(atNode,out var parent)||!NavPhysicalMacro.SameArc(parent,arc))throw new ArgumentException("Query reverse trace changed.");atNode=arc.From;}
            if(chain.Count>0&&atNode!=traceNode||phase==Phase.Trace&&atNode!=traceNode)throw new ArgumentException("Query trace cursor changed.");
            if(chosen>=0&&(!distance.ContainsKey(chosen)||!ends.ContainsKey(queryChosenCandidate)||!NavPhysicalSolveValidation.Sweep(graph,graph.Nodes[chosen].Location,queryRegion.Candidates[queryChosenCandidate],allocation)))throw new ArgumentException("Query selected terminal is not certified.");
            if(!searching&&(distance.Count!=0||previous.Count!=0||frontier.Count!=0||startSeeded)||phase==Phase.Search&&!startSeeded&&(current!=-1||previous.Count!=0))throw new ArgumentException("Query future search payload.");
            if(phase!=Phase.Trace&&phase!=Phase.Reconstruct&&phase!=Phase.Done&&chain.Count!=0||phase!=Phase.Reconstruct&&phase!=Phase.Done&&legs.Count!=0)throw new ArgumentException("Query future route payload.");
            if(phase==Phase.Search&&!startSeeded)foreach(var pair in distance)if(pair.Key>cursor||!starts.TryGetValue(pair.Key,out double seed)||seed!=pair.Value)throw new ArgumentException("Query seed prefix changed.");
            NavPhysicalSolveValidation.Legs(legs,out double sum);if(sum!=routeCost)throw new ArgumentException("Query emitted cost changed.");
            if(phase==Phase.Reconstruct||Status==NavSolveStatus.Ready)ValidatePhysicalReconstruction(allocation);
            if(Route!=null){if(!ReferenceEquals(Route.Subscription,subscription)||Route.Cost!=routeCost||Route.Legs.Count!=legs.Count)throw new ArgumentException("Query route association changed.");for(int i=0;i<legs.Count;i++)if(!ReferenceEquals(Route.Legs[i],legs[i]))throw new ArgumentException("Query route leg alias changed.");}
        }
        private void StepQuery()
        {
            if(phase==Phase.Direct){
                if(!graph.IsValid(queryRegion.Requested)){Finish(NavSolveStatus.InvalidEndpoint);return;}
                // Arrival already attempts the exact direct sweep. Its result is
                // not a licence to omit candidates from a routed query.
                phase=Phase.QueryValidate;cursor=0;return;
            }
            if(phase==Phase.QueryValidate){
                if(cursor==queryRegion.Candidates.Count){phase=Phase.Endpoints;cursor=0;return;}
                if(!graph.IsValid(queryRegion.Candidates[cursor++]))Finish(NavSolveStatus.InvalidEndpoint);
                return;
            }
            if(phase==Phase.Endpoints){
                if(cursor==queryRegion.Candidates.Count){
                    if(ends.Count==0){Finish(NavSolveStatus.BlockedConnector);return;}
                    phase=Phase.Origins;cursor=0;return;
                }
                int c=cursor++;var at=queryRegion.Candidates[c];
                                if(graph.CanSweep(at,subscription.AssignedEndpoint))
                    ends[c]=LayeredNavigationProvider.Distance(at.Position,subscription.AssignedEndpoint.Position)*graph.SurfaceCost(at.SurfaceId);
                return;
            }
            if(phase==Phase.Origins){
                if(cursor==graph.Nodes.Count){
                    if(starts.Count==0){Finish(NavSolveStatus.BlockedConnector);return;}
                    phase=Phase.Search;cursor=-1;return;
                }
                if(graph.TryAttach(subscription.Origin,cursor,out var attachment))starts[cursor]=attachment.Cost;
                cursor++;return;
            }
            if(phase==Phase.Search){
                if(!startSeeded){
                    if(++cursor<graph.Nodes.Count){
                        if(starts.TryGetValue(cursor,out double seed)){
                            if(distance.Count==maxStates){Finish(NavSolveStatus.CapacityExceeded);return;}
                            distance[cursor]=seed;frontier.Add(QueryEntry(cursor,seed));
                        }
                        return;
                    }
                    startSeeded=true;cursor=0;return;
                }
                if(current<0){
                    // Strict > retains every pending equal-priority contender.
                    if(frontier.Count==0||frontier.Min.Cost>best){
                        if(chosen<0)Finish(NavSolveStatus.UnreachableInGraph);
                        else{traceNode=chosen;phase=Phase.Trace;}return;
                    }
                    var entry=frontier.Min;frontier.Remove(entry);
                    if(!distance.TryGetValue(entry.Node,out double known)||entry.Cost!=QueueCost(entry.Node,known))return;
                    current=entry.Node;arcCursor=0;queryCandidate=0;phase=Phase.QueryCandidates;return;
                }
                var arcs=graph.Outgoing(current);
                if(arcCursor==arcs.Count){current=-1;return;}
                var arc=arcs[arcCursor++];double value=distance[current]+arc.Cost;
                if(!distance.TryGetValue(arc.To,out double old)||value<old){
                    if(!distance.ContainsKey(arc.To)&&distance.Count==maxStates){Finish(NavSolveStatus.CapacityExceeded);return;}
                    if(distance.ContainsKey(arc.To))frontier.Remove(QueryEntry(arc.To,old));
                    distance[arc.To]=value;previous[arc.To]=arc;frontier.Add(QueryEntry(arc.To,value));
                }
                return;
            }
            if(phase==Phase.QueryCandidates){
                if(queryCandidate==queryRegion.Candidates.Count){phase=Phase.Search;return;}
                int c=queryCandidate++;if(!ends.TryGetValue(c,out double egress))return;
                var at=queryRegion.Candidates[c];var node=graph.Nodes[current].Location;
                if(at.SurfaceId!=node.SurfaceId)return;
                double attachment=LayeredNavigationProvider.Distance(node.Position,at.Position)*graph.SurfaceCost(at.SurfaceId);
                double total=(distance[current]+attachment)+egress;
                if(total>best||!graph.CanSweep(node,at))return;
                if(total<best||total==best&&(current<chosen||current==chosen&&c<queryChosenCandidate)){
                    best=total;chosen=current;queryChosenCandidate=c;
                }
                return;
            }
            if(phase==Phase.Trace){
                if(previous.TryGetValue(traceNode,out var predecessor)){chain.Add(predecessor);traceNode=predecessor.From;}
                else phase=Phase.Reconstruct;
                return;
            }
            if(phase==Phase.Reconstruct){
                if(reconstructCursor==0){
                    int first=chain.Count==0?chosen:chain[chain.Count-1].From;
                    AddSurface(subscription.Origin,graph.Nodes[first].Location);
                }else if(reconstructCursor<=chain.Count){
                    var arc=chain[chain.Count-reconstructCursor];var trace=NavSolverLegs.ExpandedArc(graph,arc,reservation?.Target as INavStorageReservation);
                    var leg=trace[reconstructLeg++];try{Add(leg);}finally{if(arc.Trace==null)leg.ReleaseStorage();}
                    if(reconstructLeg<trace.Count)return;reconstructLeg=0;
                }else if(reconstructCursor==chain.Count+1)AddSurface(graph.Nodes[chosen].Location,queryRegion.Candidates[queryChosenCandidate]);
                else{
                    AddSurface(queryRegion.Candidates[queryChosenCandidate],subscription.AssignedEndpoint);
                    Route=new NavTypedRoute(subscription,legs,routeCost,NavStorageLayout.Before(reservation,128),true);Finish(NavSolveStatus.Ready);
                }
                reconstructCursor++;return;
            }
            throw new InvalidOperationException("Invalid query phase.");
        }
    }
}
