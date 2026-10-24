using System;
namespace Spacewars.Simulation
{
    internal sealed partial class NavDemandQueryWork
    {
        private NavCowDictionary<int,NavExactCost> exactDistance;
        private NavCowOrderedSet<NavExactQueueEntry> exactFrontier;
        private NavExactCost exactBest,exactRoutePrefix;
        // Fixed copy/scratch bound: <=64 AVL levels, <=8 concurrent 280-byte
        // value/entry copies per frame + 32 arithmetic temporaries and bounded frame overhead, rounded up.
        // Covered by existing pre-admitted step growth, never a free heap array.
        private const long ExactActionScratch=196608;
        private long ExactBytes=>(exactDistance?.CommittedStorageBytes(4)??0)+(exactFrontier?.CommittedStorageBytes??0);
        private long ExactGrowth=>request.DemandQueryV5?ExactActionScratch+exactDistance.MutationGrowthBytes(4)+2*exactFrontier.MutationGrowthBytes:0;
        private void InitializeExact(){if(!request.DemandQueryV5)return;exactDistance=NavCowDictionary<int,NavExactCost>.Create(reserve:reserve);exactFrontier=NavCowOrderedSet<NavExactQueueEntry>.Create(reserve:reserve);}
        private void ForkExact(NavDemandQueryWork f,Func<long,bool> admission){if(!request.DemandQueryV5)return;ForkHeuristic(f);f.exactBest=exactBest;f.exactRoutePrefix=exactRoutePrefix;f.exactDistance=exactDistance.ForkPrepared(admission);f.exactFrontier=exactFrontier.ForkPrepared(admission);}
        private NavExactQueueEntry ExactEntry(int node,NavExactCost cost)=>new NavExactQueueEntry{Node=node,Cost=request.DemandQueryV6?cost.Add(NodeHeuristic(node)):cost};
        internal NavExactCost ExactObjective=>request.DemandQueryV5&&Status==NavSolveStatus.Ready&&!boundedSelected?exactBest:throw new InvalidOperationException("No exact terminal objective.");
        private void StepExactConnector()
        {
            if(phase==Phase.Seed){
                if(starts.TrySuccessor(seed,out var next)){var value=NavExactCost.FromDouble(next.Value);exactDistance[next.Key]=value;exactFrontier.Add(ExactEntry(next.Key,value));seed=next.Key;}else phase=Phase.Pop;return;
            }
            if(phase==Phase.Pop){
                if(exactFrontier.Count==0||chosen>=0&&exactFrontier.Min.Cost>exactBest){if(chosen<0)Finish(NavSolveStatus.UnreachableInGraph);else{traceNode=chosen;phase=Phase.Trace;}return;}
                if(!exactFrontier.TryTakeMin(out var item))throw new InvalidOperationException("Exact frontier/label mismatch.");
                if(!exactDistance.TryGetValue(item.Node,out var known)||ExactEntry(item.Node,known).Cost!=item.Cost)throw new InvalidOperationException("Exact frontier/label mismatch.");
                observedExpansions++;ResetTerminalRectangle();current=item.Node;candidate=NodeLocation(current).SurfaceId!=request.Subscription.AssignedEndpoint.SurfaceId?request.Region.Candidates.Count:0;direction=0;phase=candidate==request.Region.Candidates.Count?Phase.Edges:Phase.Candidates;if(request.TerminalCostProof&&chosen>=0&&candidate==0)terminalBlocker=-2;if(request.LazyPartition)PrepareLazyMask();return;
            }
            if(phase==Phase.Candidates&&candidate==request.Region.Candidates.Count)phase=Phase.Edges;
            if(phase==Phase.Edges&&!pending&&(request.LazyPartition?lazyMask==0:direction==4)){phase=Phase.Pop;current=-1;if(corridorSource==null)StepExactConnector();return;}
            if(phase==Phase.Candidates){
                if(request.TerminalCostProof&&terminalBlocker==-2){terminalBlocker=-1;if(TerminalCostRejects())candidate=request.Region.Candidates.Count;return;}
                if(request.TerminalRectangleProof&&terminalBlocker>=0){StepTerminalRectangle();return;}
                int c=candidate++;if(!ends.TryGetValue(c,out double egress))return;var at=request.Region.Candidates[c];var node=NodeLocation(current);if(at.SurfaceId!=node.SurfaceId)return;
                var total=exactDistance[current].Add(Cost(node,at)).Add(egress);if(chosen>=0&&total>exactBest)return;
                observedTerminalSweeps++;if(request.TerminalRectangleProof){if(!Provider.CanSweep(node,at,request.Clearance,out int blocker)){if(!terminalAttempted&&blocker>=0&&candidate<request.Region.Candidates.Count)terminalBlocker=blocker;return;}}
                else if(!Sweep(node,at))return;
                if(chosen<0||total<exactBest||total==exactBest&&(current<chosen||current==chosen&&c<chosenCandidate)){exactBest=total;chosen=current;chosenCandidate=c;}return;
            }
            if(phase==Phase.Edges){
                if(!pending){phase=Phase.Pop;current=-1;return;}
                var arc=pendingArc;var value=exactDistance[current].Add(arc.Cost);
                bool had=exactDistance.TryGetValue(arc.To,out var old);
                if(!had||value<old){
                    if(!had&&exactDistance.Count==request.MaxLabels){Finish(NavSolveStatus.CapacityExceeded);return;}
                    if(had)exactFrontier.Remove(ExactEntry(arc.To,old));exactDistance[arc.To]=value;previous[arc.To]=arc;exactFrontier.Add(ExactEntry(arc.To,value));
                }
                NavStoragePayload<NavGraphArc>.Release(pendingArc);pendingArc=default;pending=false;return;
            }
            if(phase==Phase.Trace){if(previous.TryGetValue(traceNode,out var arc)){chain.Add(arc);traceNode=arc.From;}else phase=Phase.Reconstruct;return;}
            if(phase!=Phase.Reconstruct)throw new InvalidOperationException("Invalid exact query phase.");
            if(reconstruct==0){int first=chain.Count==0?chosen:chain[chain.Count-1].From;var cost=NavExactCost.FromDouble(starts[first]);AddSurface(request.Subscription.Origin,NodeLocation(first));exactRoutePrefix=cost;}
            else if(reconstruct<=chain.Count){var arc=chain[chain.Count-reconstruct];var total=exactRoutePrefix.Add(arc.Cost);if(arc.Trace!=null){Add(arc.Trace[reconstructLeg++]);if(reconstructLeg<arc.Trace.Count)return;reconstructLeg=0;}else AddSurface(NodeLocation(arc.From),NodeLocation(arc.To));exactRoutePrefix=total;}
            else if(reconstruct==chain.Count+1){var total=exactRoutePrefix.Add(Cost(NodeLocation(chosen),request.Region.Candidates[chosenCandidate]));AddSurface(NodeLocation(chosen),request.Region.Candidates[chosenCandidate]);exactRoutePrefix=total;}
            else{
                var total=exactRoutePrefix.Add(ends[chosenCandidate]);if(total!=exactBest)throw new InvalidOperationException("Exact reconstructed objective differs from best.");
                double finalFold=routeCost+Cost(request.Region.Candidates[chosenCandidate],request.Subscription.AssignedEndpoint);if(!NavPhysicalSolveValidation.Cost(finalFold))throw new ArgumentException("Public flattened route fold is not finite.");
                AddSurface(request.Region.Candidates[chosenCandidate],request.Subscription.AssignedEndpoint);exactRoutePrefix=total;Route=new NavTypedRoute(request.Subscription,legs,routeCost,NavStorageLayout.Before(reserve,128),true);Finish(NavSolveStatus.Ready);
            }reconstruct++;
        }
        private void ValidateExact()
        {
            if(distance.Count!=0||frontier.Count!=0||nodeBounds!=null||seamBoxes!=null)throw new ArgumentException("Exact query contains legacy solver state.");
            foreach(var p in exactDistance)if(p.Key<0||!PresentNode(p.Key)||!previous.ContainsKey(p.Key)&&(!starts.TryGetValue(p.Key,out var start)||NavExactCost.FromDouble(start)!=p.Value)||phase==Phase.Seed&&p.Key>seed)throw new ArgumentException("Exact distance/root contradiction.");
            foreach(var item in exactFrontier)if(item.Node<0||!PresentNode(item.Node)||!exactDistance.TryGetValue(item.Node,out var value)||item.Cost!=ExactEntry(item.Node,value).Cost)throw new ArgumentException("Exact frontier contradiction.");
            if(chosen>=0){if(!ends.TryGetValue(chosenCandidate,out var egress)||!exactDistance.TryGetValue(chosen,out var value)||value.Add(Cost(NodeLocation(chosen),request.Region.Candidates[chosenCandidate])).Add(egress)!=exactBest)throw new ArgumentException("Exact best candidate contradiction.");}
            else if(exactBest!=default)throw new ArgumentException("Unexpected exact best.");
            if(boundedSelected){ValidateBoundedRoute();return;}
            var prefix=default(NavExactCost);int legIndex=0;
            void Leg(NavTypedLeg expected){if(legIndex>=legs.Count||!ReferenceEquals(legs[legIndex++],expected))throw new ArgumentException("Exact reconstructed portal leg contradiction.");}
            void Surface(NavLocation from,NavLocation to){if(legIndex>=legs.Count)throw new ArgumentException("Missing exact surface leg.");var leg=legs[legIndex++];if(!leg.From.Equals(from)||!leg.To.Equals(to)||leg.Kind!=NavTypedLegKind.Surface||leg.PortalId!=null||leg.Cost!=Cost(from,to))throw new ArgumentException("Exact reconstructed surface leg contradiction.");}
            if(reconstruct>0){int first=chain.Count==0?chosen:chain[chain.Count-1].From;prefix=NavExactCost.FromDouble(starts[first]);Surface(request.Subscription.Origin,NodeLocation(first));}
            for(int i=1;i<=chain.Count;i++){var arc=chain[chain.Count-i];bool complete=i<reconstruct;int count=complete?(arc.Trace?.Count??1):i==reconstruct?reconstructLeg:0;if(arc.Trace!=null){for(int j=0;j<count;j++)Leg(arc.Trace[j]);}else if(count!=0)Surface(NodeLocation(arc.From),NodeLocation(arc.To));if(complete)prefix=prefix.Add(arc.Cost);}
            if(reconstruct>chain.Count+1){prefix=prefix.Add(Cost(NodeLocation(chosen),request.Region.Candidates[chosenCandidate]));Surface(NodeLocation(chosen),request.Region.Candidates[chosenCandidate]);}
            if(reconstruct>chain.Count+2){prefix=prefix.Add(ends[chosenCandidate]);Surface(request.Region.Candidates[chosenCandidate],request.Subscription.AssignedEndpoint);}
            if(prefix!=exactRoutePrefix||legIndex!=legs.Count||Status==NavSolveStatus.Ready&&prefix!=exactBest)throw new ArgumentException("Exact reconstruction objective prefix contradiction.");
        }
    }
}
