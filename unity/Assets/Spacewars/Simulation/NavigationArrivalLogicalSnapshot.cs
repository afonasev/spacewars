using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    public sealed partial class PartitionNavigationProvider
    {
        internal PartitionNavigationProvider DetachedSnapshot()
        {
            NavObstacle Obstacle(NavObstacle b)=>b.Polygon==null?b:new NavObstacle(new NavPolygon(b.Polygon.Vertices.ToArray()),b.Bottom,b.Top);
            var supports=locations.Supports.Select(s=>new MapSupport(s.Id,Obstacle(s.Bounds),s.IsBridge,s.Height,s.Gradient,s.Origin,s.SurfaceId)).ToArray();
            var transitions=locations.Transitions.Select(t=>new NavSurfaceTransition(t.Id,t.From,t.To)).ToArray();
            var geometry=new NavGeometry(locations.Geometry.HalfExtent,locations.Geometry.Obstacles.Select(Obstacle).ToArray(),locations.Geometry.Revision);
            var result=new PartitionNavigationProvider(this,new TerrainLocations(geometry,supports,transitions));
            result.Seams=result.locations.Seams;result.Surfaces=Array.AsReadOnly(supports.Select(s=>new NavLayerSurface(s.SurfaceId,"ground",s.Height,1,new NavTraversalArea(s.Bounds.Footprint,HalfExtent,Array.Empty<NavObstacle>()))).ToArray());result.bySurface=result.Surfaces.ToDictionary(s=>s.Id,StringComparer.Ordinal);result.Portals=Array.AsReadOnly(Array.Empty<NavLayerPortal>());
            if(NavContract.Digest(result.WriteBindingTranscript)!=Binding)throw new ArgumentException("Detached Partition snapshot changed binding.");return result;
        }
    }
    public sealed partial class NavigationArrivalWork
    {
        internal sealed class HistoricalCheckpoint
        {
            internal int[] Removal,Page0Occupancy;internal NavArrivalInput Input;internal Checkpoint Work;internal long EpochBase;internal int Gate,Cursor,Member,Leg;internal NavTypedLeg[] Legs;internal NavScheduledRequest Recipe;
        }
        private sealed class LogicalDeclarations
        {
            private readonly Dictionary<ILayeredNavigationProvider,ILayeredNavigationProvider> providers=new Dictionary<ILayeredNavigationProvider,ILayeredNavigationProvider>();
            private readonly Dictionary<string,ILayeredNavigationProvider> bindings=new Dictionary<string,ILayeredNavigationProvider>();
            internal LogicalDeclarations(NavArrivalInput exact=null){if(exact!=null){bindings[exact.Provider.Binding]=exact.Provider;foreach(var b in exact.WorkingBindings)bindings[b.Provider.Binding]=b.Provider;}}
            private readonly Dictionary<NavScheduledRequest,NavScheduledRequest> recipes=new Dictionary<NavScheduledRequest,NavScheduledRequest>();
            internal ILayeredNavigationProvider Provider(ILayeredNavigationProvider p){if(!providers.TryGetValue(p,out var copy)){if(!(p is PartitionNavigationProvider partition))throw new ArgumentException("Historical snapshot requires concrete Partition.");copy=bindings.TryGetValue(p.Binding,out var exact)?exact:partition.DetachedSnapshot();providers.Add(p,copy);bindings[copy.Binding]=copy;if(!providers.ContainsKey(copy))providers.Add(copy,copy);}return copy;}
            private static NavGraphProfile Profile(NavGraphProfile p)=>new NavGraphProfile(p.ExactProfileBinding,p.CostId,p.CostRevision,p.GridCell);
            private static NavClearanceProfile Clearance(NavClearanceProfile p)=>new NavClearanceProfile(p.Id,p.Revision,p.MobilityClass,p.MobilityRevision,p.Radius);
            internal static NavTerminalRegion Region(NavTerminalRegion p)=>p==null?null:new NavTerminalRegion(p.Id,p.Revision,p.Requested.Detached(),p.Candidates.Select(x=>x.Detached()));
            internal NavScheduledRequest Recipe(NavScheduledRequest r)
            {
                if(r==null)return null;if(recipes.TryGetValue(r,out var value))return value;var q=r.Subscription;var sub=new NavRouteSubscription(q.WorldGeneration,q.GroupId,q.RootOrderRevision,q.CommandSequence,q.IssuedTick,q.ActivationRevision,q.EntityId,q.Incarnation,q.MemberOrderRevision,q.MobilityRevision,q.RequestSequence,q.Origin.Detached(),q.AssignedEndpoint.Detached(),q.HoldBinding,q.Provenance);
                value=new NavScheduledRequest(Provider(r.Provider),Clearance(r.Clearance),Profile(r.Profile),new NavGraphCompileLimits(r.Limits.MaxSamples,r.Limits.MaxNodes,r.Limits.MaxArcs),Region(r.Region),sub,r.MaxLabels,r.Singleton,r.Query,r.GroupQuery,r.DemandQuery,r.DemandQueryV2,r.DemandQueryV3,r.DemandQueryV4,r.DemandQueryV5,r.DemandQueryV6,r.LazyPartition,r.CertifiedLazyPartition,r.RectangularOrigins,r.TerminalRectangleProof,r.TerminalCornerProof,r.OriginShadowProof,r.TerminalCostProof,r.CertifiedGroupCorridor);recipes.Add(r,value);return value;
            }
            internal NavArrivalInput Input(NavArrivalInput input)
            {
                var members=input.Members.Select(m=>new NavArrivalMember(m.EntityId,m.Incarnation,m.Order,m.MobilityRevision,m.Origin.Detached(),Clearance(m.Clearance),m.WorldGeneration,m.GroupId,m.RootOrderRevision,m.CommandSequence,m.IssuedTick,m.ActivationRevision,m.MemberOrderRevision,m.RequestSequence,m.HoldBinding)).ToArray();
                var occupancy=new NavArrivalOccupancySnapshot(input.Occupancy.Generation,input.Occupancy.Occupants.Select(o=>new NavArrivalOccupant(o.Kind,o.ActorId,o.Incarnation,o.Location.Detached(),o.Radius,o.BlocksPlacement,o.Identity,o.ReservationKind,o.RequestSequence,o.HistoricalExecution,o.ActorTarget?.Detached())));
                var p=input.ArrivalProfile;return new NavArrivalInput(input.TopologyIdentity,input.ProfileIdentity,Provider(input.Provider),Profile(input.Profile),p.ForUnit(p.Radius,p.TankSpeed,p.TankTurnSpeed),new NavGraphCompileLimits(input.Limits.MaxSamples,input.Limits.MaxNodes,input.Limits.MaxArcs),new NavArrivalCandidateSource(input.Source.Id,input.Source.Revision,input.Source.Requested.Detached(),input.Source.Candidates.Select(x=>x.Detached())),members,occupancy,input.MaxLabels,input.WorkingBindings.Select(b=>new NavArrivalWorkingBinding(b.Entity,Provider(b.Provider),Profile(b.Profile))),input.IndependentMembers);
            }
            internal NavTypedRoute Route(NavTypedRoute route,NavScheduledRequest recipe)=>route==null?null:new NavTypedRoute(recipe.Subscription,route.Legs.Select(l=>l.Detached()).ToArray(),route.Cost);
            internal NavArrivalProposal Proposal(NavArrivalProposal proposal,NavArrivalInput input)
            {
                var member=input.Members.Single(m=>m.EntityId==proposal.Member.EntityId);var binding=input.WorkingFor(member);var r=Recipe(proposal.Proof.PhysicalRecipe??new NavScheduledRequest(binding.Provider,member.Clearance,binding.Profile,input.Limits,null,proposal.Proof.Subscription,input.MaxLabels,singleton:true));if(r.GraphKey!=proposal.Proof.GraphBinding)throw new ArgumentException("Historical logical proposal graph mismatch.");var route=Route(proposal.Route,r);var proof=new NavArrivalReachabilityProof(input,member,proposal.Endpoint.Detached(),r,route){LogicalRecipe=r};return new NavArrivalProposal(member,proposal.Endpoint.Detached(),route,proof);
            }
        }
        private static void CopyLogicalDeclarations(Checkpoint state,LogicalDeclarations copies,NavArrivalInput detached)
        {
            state.Requests=state.Requests.Select(copies.Recipe).ToArray();state.Routes=state.Routes.Select(row=>new RouteCheckpoint{Candidate=row.Candidate,Status=row.Status,Route=row.Route==null?null:copies.Route(row.Route,state.Requests[Array.IndexOf(state.RequestCandidates,row.Candidate)])}).ToArray();
            state.Assignments=state.Assignments.Select(p=>copies.Proposal(p,detached)).ToArray();
            if(state.PreparationRegions!=null)state.PreparationRegions=state.PreparationRegions.Select(LogicalDeclarations.Region).ToArray();state.PreparedClearances=state.PreparedClearances.Select(p=>new PreparedCheckpoint{ClearanceKey=p.ClearanceKey,CandidateIndices=p.CandidateIndices,Regions=p.Regions.Select(LogicalDeclarations.Region).ToArray()}).ToArray();
            var schedulerState=state.Scheduler=state.Scheduler.CopyLogical();schedulerState.ArtifactOwner=copies.Recipe(schedulerState.ArtifactOwner);schedulerState.CacheGraphRecipe=copies.Recipe(schedulerState.CacheGraphRecipe);
            foreach(var job in schedulerState.Jobs)if(job.Demand?.HeuristicCatalogRecipe!=null)job.Demand.HeuristicCatalogRecipe=copies.Recipe(job.Demand.HeuristicCatalogRecipe);
            if(schedulerState.LazyCatalog!=null){schedulerState.LazyCatalog=schedulerState.LazyCatalog.CopyLogical();schedulerState.LazyCatalog.Recipe=copies.Recipe(schedulerState.LazyCatalog.Recipe);}
            if(schedulerState.Jobs.Any(j=>j.Demand?.LazyStore!=null)){schedulerState.Jobs=(LayeredNavigationScheduler.JobState[])schedulerState.Jobs.Clone();for(int n=0;n<schedulerState.Jobs.Length;n++){var job=schedulerState.Jobs[n];if(job.Demand?.LazyStore==null)continue;job=job.CopyLogical();job.Demand=job.Demand.CopyLogical();job.Demand.LazyStore=job.Demand.LazyStore.CopyLogical();job.Demand.LazyStore.Recipe=copies.Recipe(job.Demand.LazyStore.Recipe);schedulerState.Jobs[n]=job;}}
            if(schedulerState.DemandCatalog!=null){schedulerState.DemandCatalog=schedulerState.DemandCatalog.CopyLogical();schedulerState.DemandCatalog.Recipe=copies.Recipe(schedulerState.DemandCatalog.Recipe);}
            schedulerState.CacheFields=schedulerState.CacheFields?.Select(f=>new LayeredNavigationScheduler.CacheFieldState{Recipe=copies.Recipe(f.Recipe),Field=f.Field,Incremental=f.Incremental,Work=f.Work}).ToArray();
            if(schedulerState.Ready!=null)schedulerState.Ready=schedulerState.Ready.Select(c=>{var recipe=state.Requests.Single(r=>r.Subscription.StableKey==c.Subscription.StableKey);return new NavScheduledCompletion(recipe,c.Status,copies.Route(c.Route,recipe),c.SubmittedEpoch,c.CompletedEpoch);}).ToArray();
        }
        private void SnapshotHistorical(Checkpoint state)
        {
            var copies=new LogicalDeclarations();CopyLogicalDeclarations(state,copies,copies.Input(input));
            if(historicalOracle!=null)state.Historical=new HistoricalCheckpoint{Page0Occupancy=page0OccupancyMode?Page0OccupancySnapshot():null,Removal=removalMode?RemovalSnapshot():null,Input=copies.Input(historicalOracle.input),Work=historicalOracle.Save(),EpochBase=historicalEpochBase,Gate=historicalGate,Cursor=historicalCursor,Member=historicalMember,Leg=historicalLeg,Legs=historicalLegs.Select(l=>l.Detached()).ToArray(),Recipe=copies.Recipe(historicalRecipe)};
        }
        private static Checkpoint RebindLogical(NavArrivalInput input,Checkpoint saved)
        {
            if(saved==null||saved.Version<6)return saved;var state=saved.CopyLogical();var copies=new LogicalDeclarations(input);CopyLogicalDeclarations(state,copies,input);if(saved.Historical!=null){var h=saved.Historical;state.Historical=new HistoricalCheckpoint{Page0Occupancy=h.Page0Occupancy==null?null:(int[])h.Page0Occupancy.Clone(),Removal=h.Removal==null?null:(int[])h.Removal.Clone(),Input=h.Input,Work=h.Work,EpochBase=h.EpochBase,Gate=h.Gate,Cursor=h.Cursor,Member=h.Member,Leg=h.Leg,Legs=h.Legs,Recipe=copies.Recipe(h.Recipe)};}return state;
        }
        private void RestoreHistorical(Checkpoint state)
        {
            if(state.Historical==null)return;if(!demandContinuationMode)throw new ArgumentException("Historical logical policy mismatch.");var saved=state.Historical;historicalOracle=Restore(saved.Input,saved.Work);historicalEpochBase=saved.EpochBase;historicalGate=saved.Gate;historicalCursor=saved.Cursor;historicalMember=saved.Member;historicalLeg=saved.Leg;if(removalMode)RestoreRemoval(saved.Removal);if(page0OccupancyMode)RestorePage0Occupancy(saved.Page0Occupancy);else if(saved.Page0Occupancy!=null)throw new ArgumentException("Legacy checkpoint has occupancy certificate.");historicalRecipe=saved.Recipe;historicalRecipe?.StorageRoot?.Retain();historicalLegs=NavPagedList<NavTypedLeg>.Create();historicalLegs.AddRange(saved.Legs);if(occupancyPrefixMode)ValidateHistoricalContinuation(false);
        }
    }
}
