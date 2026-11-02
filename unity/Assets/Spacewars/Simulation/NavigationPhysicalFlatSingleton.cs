using System;
using System.Linq;

namespace Spacewars.Simulation
{
    public sealed partial class NavIncrementalSingletonRoute
    {
        internal sealed class FlatCheckpoint
        {
            internal string RecipeKey;
            internal int Sample;
            internal bool Pending,HasEdge;
            internal NavGraphArc Edge;
            internal NavLocation[] Nodes;
        }
        private Checkpoint SaveFlat()=>new Checkpoint{
            Version=4,GraphKey=flatRecipe.CompileKey,SubscriptionKey=subscription.StableKey,
            Flat=new FlatCheckpoint{RecipeKey=flatRecipe.ArtifactKey,Sample=flatSample,Pending=flatPending,HasEdge=flatHasEdge,Edge=flatEdge,Nodes=flatNodes.ToArray()},
            Status=Status,Phase=(int)phase,Cursor=cursor,ArcCursor=arcCursor,Current=current,Chosen=chosen,ReconstructCursor=reconstructCursor,ReconstructLeg=reconstructLeg,TraceNode=traceNode,Best=best,RouteCost=routeCost,StartSeeded=startSeeded,
            Starts=starts.ToArray(),Ends=ends.ToArray(),Distance=distance.ToArray(),Previous=previous.ToArray(),Frontier=frontier.ToArray(),FrontierShape=frontier.SaveShape(),Legs=legs.Select(l=>l.Detached()).ToArray(),Chain=chain.ToArray()
        };
        internal static NavIncrementalSingletonRoute RestoreFlat(NavScheduledRequest recipe,Checkpoint s)
        {
            if(s==null||s.Version!=4||s.Flat==null||!FlatEligible(recipe)||s.Flat.RecipeKey!=recipe.ArtifactKey||s.GraphKey!=recipe.CompileKey||s.SubscriptionKey!=recipe.Subscription.StableKey||s.QueryRegionKey!=null)throw new ArgumentException("Stale flat N1 logical checkpoint.");
            var w=CreateFlat(recipe);
            try{
                w.Status=s.Status;w.phase=(Phase)s.Phase;w.cursor=s.Cursor;w.arcCursor=s.ArcCursor;w.current=s.Current;w.chosen=s.Chosen;w.reconstructCursor=s.ReconstructCursor;w.reconstructLeg=s.ReconstructLeg;w.traceNode=s.TraceNode;w.best=s.Best;w.routeCost=s.RouteCost;w.startSeeded=s.StartSeeded;
                w.flatSample=s.Flat.Sample;w.flatPending=s.Flat.Pending;w.flatHasEdge=s.Flat.HasEdge;w.flatEdge=s.Flat.Edge;
                foreach(var at in s.Flat.Nodes){var point=recipe.Provider.Surfaces[0].OwnedLocation(at.Position);if(!point.Equals(at))throw new ArgumentException("Foreign flat catalog surface.");w.flatGrid.Add(w.FlatCoordinate(point),w.flatNodes.Count);w.flatNodes.Add(point);}
                foreach(var x in s.Starts)w.starts.Add(x.Key,x.Value);foreach(var x in s.Ends)w.ends.Add(x.Key,x.Value);foreach(var x in s.Distance)w.distance.Add(x.Key,x.Value);foreach(var x in s.Previous)w.previous.Add(x.Key,x.Value);
                w.frontier.RestoreShape(s.FrontierShape,w.maxStates);if(!w.frontier.SequenceEqual(s.Frontier))throw new ArgumentException("Flat frontier tree mismatch.");
                w.legs.AddRange(s.Legs);w.chain.AddRange(s.Chain);
                if(w.Status==NavSolveStatus.Ready)w.Route=new NavTypedRoute(w.subscription,w.legs,w.routeCost);
                w.ValidateFlat();return w;
            }catch{w.ReleaseOwner();throw;}
        }
        private void WriteFlatBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(2048)||NavStorageLayout.ObjectBytesBound(GetType())>2048||!ReferenceEquals(ownedSubscription,subscription.MetadataOwner))throw new ArgumentException("Invalid flat singleton physical layout/ownership.");
            using(NavStorageScratch.Reserve(writer.Reservation,checked(8192L+4L*flatNodes.Count)))ValidateFlat();var w=writer.Output;
            w.Write(maxStates);w.Write((int)Status);w.Write((int)phase);w.Write(cursor);w.Write(arcCursor);w.Write(current);w.Write(chosen);w.Write(reconstructCursor);w.Write(reconstructLeg);w.Write(traceNode);w.Write(best);w.Write(routeCost);w.Write(startSeeded);
            w.Write(flatSample);w.Write(flatPending);w.Write(flatHasEdge);
            if(flatHasEdge){w.Write(flatEdge.From);w.Write(flatEdge.To);w.Write(flatEdge.Cost);}
            NavPhysicalMacro.WriteEdge(writer,this,flatRecipe.StorageRoot);NavPhysicalMacro.WriteMetadata(writer,this,subscription);
            NavPhysicalMacro.WriteEdge(writer,this,flatNodes);NavPhysicalMacro.WriteEdge(writer,this,flatGrid);
            NavPhysicalMacro.WriteEdge(writer,this,starts);NavPhysicalMacro.WriteEdge(writer,this,ends);NavPhysicalMacro.WriteEdge(writer,this,distance);NavPhysicalMacro.WriteEdge(writer,this,previous);NavPhysicalMacro.WriteEdge(writer,this,frontier);NavPhysicalMacro.WriteEdge(writer,this,legs);NavPhysicalMacro.WriteEdge(writer,this,chain);NavPhysicalMacro.WriteEdge(writer,this,Route,true);
        }
        internal static NavIncrementalSingletonRoute ReadFlatBody(NavPhysicalBodyReader reader)
        {
            var r=reader.Input;int maximum=r.ReadInt32();var status=(NavSolveStatus)r.ReadInt32();var p=(Phase)r.ReadInt32();int c=r.ReadInt32(),a=r.ReadInt32(),at=r.ReadInt32(),selected=r.ReadInt32(),rc=r.ReadInt32(),rl=r.ReadInt32(),trace=r.ReadInt32();double b=r.ReadDouble(),cost=r.ReadDouble();bool seeded=NavPhysicalMacro.ReadBoolean(reader);
            int sample=r.ReadInt32();bool pending=NavPhysicalMacro.ReadBoolean(reader),edge=NavPhysicalMacro.ReadBoolean(reader);
            var arc=edge?new NavGraphArc(r.ReadInt32(),r.ReadInt32(),r.ReadDouble(),NavGraphArcKind.Surface,null):default;
            var recipeRoot=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);var sub=NavPhysicalMacro.ReadMetadata<NavRouteSubscription>(reader,NavMetadataRole.Subscription);
            if(!(recipeRoot.Facade is NavScheduledRequest recipe)||!FlatEligible(recipe)||maximum!=recipe.MaxLabels||!ReferenceEquals(recipe.Subscription,sub))throw new ArgumentException("Flat singleton physical recipe/subscription mismatch.");
            var nodes=NavPhysicalMacro.ReadEdge<NavPagedList<NavLocation>>(reader);var grid=NavPhysicalMacro.ReadEdge<NavCowDictionary<NavGridCoordinate,int>>(reader);
            var starts=NavPhysicalMacro.ReadEdge<NavCowDictionary<int,double>>(reader);var ends=NavPhysicalMacro.ReadEdge<NavCowDictionary<int,double>>(reader);var distances=NavPhysicalMacro.ReadEdge<NavCowDictionary<int,double>>(reader);var previous=NavPhysicalMacro.ReadEdge<NavCowDictionary<int,NavGraphArc>>(reader);var frontier=NavPhysicalMacro.ReadEdge<NavCowOrderedSet<NavQueueEntry>>(reader);var legs=NavPhysicalMacro.ReadEdge<NavPagedList<NavTypedLeg>>(reader);var chain=NavPhysicalMacro.ReadEdge<NavPagedList<NavGraphArc>>(reader);var route=NavPhysicalMacro.ReadEdge<NavTypedRoute>(reader,true);
            var work=new NavIncrementalSingletonRoute(recipe,NavStorageLayout.Before(reader.Reservation.TryReserve,2048),false,true,true){Status=status,phase=p,cursor=c,arcCursor=a,current=at,chosen=selected,reconstructCursor=rc,reconstructLeg=rl,traceNode=trace,best=b,routeCost=cost,startSeeded=seeded,flatSample=sample,flatPending=pending,flatHasEdge=edge,flatEdge=arc,frozen=true};
            work.flatNodes=NavPhysicalMacro.Acquire(work,nodes);work.flatGrid=NavPhysicalMacro.Acquire(work,grid);work.starts=NavPhysicalMacro.Acquire(work,starts);work.ends=NavPhysicalMacro.Acquire(work,ends);work.distance=NavPhysicalMacro.Acquire(work,distances);work.previous=NavPhysicalMacro.Acquire(work,previous);work.frontier=NavPhysicalMacro.Acquire(work,frontier);work.legs=NavPhysicalMacro.Acquire(work,legs);work.chain=NavPhysicalMacro.Acquire(work,chain);work.Route=NavPhysicalMacro.Acquire(work,route);
            using(NavStorageScratch.Reserve(reader.Reservation,checked(8192L+4L*nodes.Count)))work.ValidateFlat();work.reservation=null;return work;
        }
        internal bool MatchesFlatJob(NavScheduledRequest r)=>IsFlat&&ReferenceEquals(flatRecipe,r)&&ReferenceEquals(subscription,r.Subscription)&&queryRegion==null&&graph==null;
        private bool FlatArcValid(NavGraphArc a)
        {
            if(a.From<0||a.To<0||a.From>=flatNodes.Count||a.To>=flatNodes.Count||a.From==a.To||a.Kind!=NavGraphArcKind.Surface||a.PortalId!=null||a.Trace!=null)return false;
            var x=FlatCoordinate(flatNodes[a.From]);var y=FlatCoordinate(flatNodes[a.To]);
            if(Math.Abs(x.Item2-y.Item2)+Math.Abs(x.Item3-y.Item3)!=1)return false;
            int low=Math.Min(a.From,a.To),high=Math.Max(a.From,a.To);
            return FlatSweep(flatNodes[low],flatNodes[high])&&a.Cost==FlatCost(flatNodes[low],flatNodes[high]);
        }
        private void ValidateFlat()
        {
            if(!IsFlat||graph!=null||queryRegion!=null||!FlatBounds(flatRecipe,out long x,out long z,out int width,out int height)||!Enum.IsDefined(typeof(NavSolveStatus),Status)||!Enum.IsDefined(typeof(Phase),phase)||phase==Phase.Direct||phase==Phase.QueryCandidates||phase==Phase.QueryValidate)throw new ArgumentException("Flat policy/phase mismatch.");
            if(!flatRecipe.Provider.IsValid(subscription.Origin,flatRecipe.Clearance)||!flatRecipe.Provider.IsValid(subscription.AssignedEndpoint,flatRecipe.Clearance))throw new ArgumentException("Invalid flat exact endpoints.");
            int count=flatNodes.Count;
            if(flatSample<0||flatSample>width*height||flatGrid.Count!=count||cursor< -1||cursor>count||arcCursor<0||arcCursor>3||current< -1||current>=count||chosen< -1||chosen>=count||reconstructCursor<0||reconstructCursor>chain.Count+3||reconstructLeg!=0||traceNode<0||traceNode>=Math.Max(1,count)||double.IsNaN(best)||best<0||!NavPhysicalSolveValidation.Cost(routeCost)||(Status==NavSolveStatus.Pending)==(phase==Phase.Done)||phase!=Phase.FlatCatalog&&flatSample!=width*height||phase==Phase.FlatCatalog&&(flatPending||startSeeded||starts.Count!=0||ends.Count!=0||distance.Count!=0)||flatHasEdge&&!flatPending||flatPending&&(phase!=Phase.Search||!startSeeded||current<0)||!flatPending&&flatHasEdge)throw new ArgumentException("Flat cursor/status contradiction.");
            if((phase==Phase.Origins||phase==Phase.Endpoints)&&cursor<0||phase==Phase.FlatCatalog&&cursor!=0||phase==Phase.Search&&startSeeded&&cursor!=0||(phase==Phase.Trace||phase==Phase.Reconstruct)&&cursor!=0)throw new ArgumentException("Flat phase-local cursor mismatch.");
            int valid=0;
            for(int i=0;i<flatSample;i++){
                var point=flatRecipe.Provider.Surfaces[0].OwnedLocation(new NavPoint((x+i/height)*flatRecipe.Profile.GridCell,(z+i%height)*flatRecipe.Profile.GridCell));
                if(!flatRecipe.Provider.IsValid(point,flatRecipe.Clearance))continue;
                if(valid>=count||!point.Equals(flatNodes[valid])||!flatGrid.TryGetValue(FlatCoordinate(point),out int id)||id!=valid++)throw new ArgumentException("Flat paid catalog prefix changed.");
            }
            if(valid!=count||flatHasEdge&&(!FlatArcValid(flatEdge)||flatEdge.From!=current))throw new ArgumentException("Flat catalog/pending proof mismatch.");
            if(flatPending){
                bool has=FlatNeighbor(current,arcCursor,out int to);bool pass=has&&FlatSweep(flatNodes[Math.Min(current,to)],flatNodes[Math.Max(current,to)]);
                if(pass!=flatHasEdge||pass&&(flatEdge.From!=current||flatEdge.To!=to))throw new ArgumentException("Flat pending neighbor order/proof mismatch.");
            }
            if(!flatHasEdge&&(flatEdge.From!=0||flatEdge.To!=0||flatEdge.Cost!=0||flatEdge.PortalId!=null||flatEdge.Trace!=null||flatEdge.NameReference.Owner!=null))throw new ArgumentException("Flat future edge payload.");
            bool failed=phase==Phase.Done&&Status!=NavSolveStatus.Ready;
            int startPrefix=phase==Phase.FlatCatalog?0:phase==Phase.Origins?cursor:count;
            int endPrefix=phase==Phase.FlatCatalog||phase==Phase.Origins?0:phase==Phase.Endpoints?cursor:count;
            if(!failed){ValidateFlatAttachments(starts,subscription.Origin,startPrefix);ValidateFlatAttachments(ends,subscription.AssignedEndpoint,endPrefix);}
            foreach(var pair in starts.Concat(ends))if(pair.Key<0||pair.Key>=count||!NavPhysicalSolveValidation.Cost(pair.Value))throw new ArgumentException("Flat attachment range/cost.");
            if(distance.Count>maxStates||frontier.Count>distance.Count||previous.Count>distance.Count||chain.Count>count)throw new ArgumentException("Flat label capacity.");
            foreach(var pair in distance)if(pair.Key<0||pair.Key>=count||!NavPhysicalSolveValidation.Cost(pair.Value)||!previous.ContainsKey(pair.Key)&&(!starts.TryGetValue(pair.Key,out double seedCost)||seedCost!=pair.Value))throw new ArgumentException("Flat distance/seed mismatch.");
            foreach(var pair in previous){var arc=pair.Value;if(pair.Key!=arc.To||!FlatArcValid(arc)||!distance.TryGetValue(arc.From,out double parent)||!distance.TryGetValue(arc.To,out double value)||value<parent+arc.Cost)throw new ArgumentException("Flat predecessor mismatch.");}
            var seen=new int[count];int epoch=0;
            foreach(var pair in previous){int walk=pair.Key;epoch++;while(previous.TryGetValue(walk,out var arc)&&seen[walk]!=-1){if(seen[walk]==epoch)throw new ArgumentException("Flat predecessor cycle.");seen[walk]=epoch;walk=arc.From;}walk=pair.Key;while(previous.TryGetValue(walk,out var settled)&&seen[walk]==epoch){seen[walk]=-1;walk=settled.From;}}
            foreach(var entry in frontier)if(entry.Candidate!=0||!distance.TryGetValue(entry.Node,out double value)||entry.Cost!=value)throw new ArgumentException("Flat frontier mismatch.");
            if(phase==Phase.Search&&!startSeeded&&(current!=-1||previous.Count!=0||flatPending))throw new ArgumentException("Flat seed cursor mismatch.");
            if(phase==Phase.Search&&!startSeeded)foreach(var pair in distance)if(pair.Key>cursor||!starts.TryGetValue(pair.Key,out double value)||value!=pair.Value)throw new ArgumentException("Flat future seed label.");
            if((chosen<0)!=double.IsPositiveInfinity(best)||chosen>=0&&(!distance.ContainsKey(chosen)||!ends.ContainsKey(chosen)))throw new ArgumentException("Flat terminal selection mismatch.");
            int node=chosen;foreach(var arc in chain){if(arc.To!=node||!previous.TryGetValue(node,out var up)||!NavPhysicalMacro.SameArc(up,arc))throw new ArgumentException("Flat trace mismatch.");node=arc.From;}
            if((chain.Count>0||phase==Phase.Trace)&&node!=traceNode)throw new ArgumentException("Flat trace cursor mismatch.");
            if(phase==Phase.FlatCatalog||phase==Phase.Origins||phase==Phase.Endpoints){if(distance.Count!=0||previous.Count!=0||frontier.Count!=0||chain.Count!=0||legs.Count!=0)throw new ArgumentException("Flat future work payload.");}
            NavPhysicalSolveValidation.Legs(legs,out double sum);if(sum!=routeCost||(Route!=null)!=(Status==NavSolveStatus.Ready))throw new ArgumentException("Flat route prefix/status mismatch.");
            if(Route!=null){if(!ReferenceEquals(Route.Subscription,subscription)||Route.Cost!=routeCost||Route.Legs.Count!=legs.Count||chosen<0)throw new ArgumentException("Flat Ready binding mismatch.");for(int i=0;i<legs.Count;i++)if(!ReferenceEquals(Route.Legs[i],legs[i]))throw new ArgumentException("Flat Ready leg alias mismatch.");}
            if(phase==Phase.Reconstruct||Status==NavSolveStatus.Ready){
                int expected=Status==NavSolveStatus.Ready?chain.Count+2:Math.Min(reconstructCursor,chain.Count+2);if(legs.Count!=expected)throw new ArgumentException("Flat reconstruction cursor mismatch.");
                int first=chain.Count==0?chosen:chain[chain.Count-1].From;
                for(int i=0;i<legs.Count;i++){
                    NavLocation from,to;double cost;
                    if(i==0){from=subscription.Origin;to=flatNodes[first];cost=FlatCost(from,to);}
                    else if(i<=chain.Count){var arc=chain[chain.Count-i];from=flatNodes[arc.From];to=flatNodes[arc.To];cost=arc.Cost;}
                    else{from=flatNodes[chosen];to=subscription.AssignedEndpoint;cost=FlatCost(from,to);}
                    var leg=legs[i];if(IsRegistered&&!leg.HasCanonicalProvider(((INavStorageFacade)flatRecipe.Provider).StorageRoot))throw new ArgumentException("Flat route provider alias mismatch.");if(!leg.From.Equals(from)||!leg.To.Equals(to)||leg.Cost!=cost||leg.Kind!=NavTypedLegKind.Surface||leg.PortalId!=null||!FlatSweep(from,to))throw new ArgumentException("Flat emitted leg mismatch.");
                }
            }
        }
        private void ValidateFlatAttachments(NavCowDictionary<int,double> map,NavLocation exact,int prefix)
        {
            int found=0;for(int i=0;i<prefix;i++){bool visible=FlatSweep(exact,flatNodes[i]);bool present=map.TryGetValue(i,out double cost);if(visible!=present||visible&&cost!=FlatCost(exact,flatNodes[i]))throw new ArgumentException("Flat attachment prefix changed.");if(present)found++;}
            if(map.Count!=found)throw new ArgumentException("Flat attachment future payload.");
        }
    }
}
