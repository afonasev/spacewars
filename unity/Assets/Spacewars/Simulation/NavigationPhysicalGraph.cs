using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Spacewars.Simulation
{
    public sealed partial class LayeredNavigationGraph : INavPhysicalBody
    {
        internal static readonly long LayoutBase=Math.Max(512,NavStorageLayout.ObjectBytesBound(typeof(LayeredNavigationGraph))+NavStorageLayout.ObjectBytesBound(typeof(PartitionNodes))+NavStorageLayout.ObjectBytesBound(typeof(PartitionArcs))+NavStorageLayout.ObjectBytesBound(typeof(ReadOnlyCollection<NavConnectorResult>))+32);
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&ownedProvider!=null&&ownedBinding!=null&&BeforeAttachmentProbe==null&&outgoing==null&&incoming==null&&(ownedEdges==63&&partitionNeighbors==null||ownedEdges==(127<<6)&&partitionNeighbors!=null);
        NavPhysicalTag INavPhysicalBody.PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Graph);
        private static void RequireRoot(object ledger,object value)
        {var root=value as NavOwnedStorage;NavStorageAdmission.RequireLedger(ledger,root);if(!root.IsImmutableCaptureRoot)throw new ArgumentException("Graph input must be an immutable physical root.");}
        private static void RequireSparseInputs(object ledger,ILayeredNavigationProvider provider,NavGraphBinding binding,IReadOnlyList<NavGraphNode> nodes,IReadOnlyList<NavGraphArc> arcs,IReadOnlyList<NavConnectorResult> qualifications,IReadOnlyDictionary<int,IReadOnlyList<NavGraphArc>> outs,IReadOnlyDictionary<int,IReadOnlyList<NavGraphArc>> ins,IReadOnlyDictionary<NavNameKey,double> costs)
        {
            if(!(provider is LayeredNavigationProvider)||binding==null)throw new ArgumentException("Sparse physical graph needs a layered provider and binding.");binding.RequireAssociation(provider,ledger);
            RequireRoot(ledger,nodes);RequireRoot(ledger,arcs);RequireRoot(ledger,qualifications);RequireRoot(ledger,outs);RequireRoot(ledger,ins);RequireRoot(ledger,costs);
        }
        private static void RequirePackedInputs(object ledger,PartitionNavigationProvider provider,NavGraphBinding binding,IReadOnlyList<NavLocation> locations,IReadOnlyList<int> components,NavPackedSlots neighbors,NavPackedSlots ins,IReadOnlyList<int> slots,NavPackedSlots traceSlots,IReadOnlyList<IReadOnlyList<NavTypedLeg>> traces)
        {
            if(provider==null||binding==null)throw new ArgumentException("Packed physical graph needs a Partition provider and binding.");binding.RequireAssociation(provider,ledger);
            RequireRoot(ledger,locations);RequireRoot(ledger,components);RequireRoot(ledger,neighbors);RequireRoot(ledger,ins);RequireRoot(ledger,slots);RequireRoot(ledger,traceSlots);RequireRoot(ledger,traces);
        }
        void INavPhysicalBody.WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(LayoutBase)||ownedClearance!=null||ownedProfile!=null||(provider as INavStorageFacade)?.StorageRoot!=ownedProvider||Binding.StorageRoot!=ownedBinding)throw new ArgumentException("Physical graph owner/layout/callback contradiction.");Binding.RequireAssociation(provider);
            bool packed=partitionNeighbors!=null;writer.Output.Write(packed?(byte)1:(byte)0);writer.Edge(ownedProvider);writer.Edge(ownedBinding);
            void Edge(object field){var root=field as NavOwnedStorage;if(root==null||!root.IsImmutableCaptureRoot)throw new ArgumentException("Missing immutable graph container.");RequireEdge(root);writer.Edge(root);}
            if(packed){Edge(partitionLocations);Edge(partitionComponents);Edge(partitionNeighbors);Edge(partitionIncoming);Edge(partitionArcSlots);Edge(partitionTraceSlots);Edge(partitionTraces);}
            else{Edge(Nodes);Edge(Arcs);Edge(PortalQualifications);Edge(sparseOutgoing);Edge(sparseIncoming);Edge(surfaceCosts);}
        }
        // Decoder constructs only this aggregate and its non-owning packed views.
        // It acquires no returned-owner reference; the BODY caller handles that.
        private LayeredNavigationGraph(ILayeredNavigationProvider provider,NavGraphBinding binding,IReadOnlyList<NavGraphNode> nodes,IReadOnlyList<NavGraphArc> arcs,IReadOnlyList<NavConnectorResult> qualifications,IReadOnlyDictionary<int,IReadOnlyList<NavGraphArc>> outs,IReadOnlyDictionary<int,IReadOnlyList<NavGraphArc>> ins,IReadOnlyDictionary<NavNameKey,double> costs,IReadOnlyList<NavLocation> locations,IReadOnlyList<int> components,NavPackedSlots neighbors,NavPackedSlots incomingSlots,IReadOnlyList<int> arcSlots,NavPackedSlots traceSlots,IReadOnlyList<IReadOnlyList<NavTypedLeg>> traces):base(LayoutBase)
        {
            this.provider=provider;Binding=binding;sparseOutgoing=outs;sparseIncoming=ins;surfaceCosts=costs;partitionLocations=locations;partitionComponents=components;partitionNeighbors=neighbors;partitionIncoming=incomingSlots;partitionArcSlots=arcSlots;partitionTraceSlots=traceSlots;partitionTraces=traces;
            if(neighbors==null){Nodes=nodes;Arcs=arcs;PortalQualifications=qualifications;}
            else{
                Nodes=new PartitionNodes(locations,components);Arcs=new PartitionArcs(this);PortalQualifications=Array.AsReadOnly(Array.Empty<NavConnectorResult>());
                long traceBytes=0;foreach(var trace in traces){if(trace==null)throw new ArgumentException("Missing physical packed trace.");traceBytes=checked(traceBytes+128L+96L*trace.Count);}
                EstimatedRetainedBytes=checked(4096L+32L*((NavPagedList<NavLocation>)locations).Capacity+4L*((NavPagedList<int>)components).Capacity+neighbors.EstimatedRetainedBytes+incomingSlots.EstimatedRetainedBytes+traceSlots.EstimatedRetainedBytes+4L*((NavPagedList<int>)arcSlots).Capacity+8L*((NavPagedList<IReadOnlyList<NavTypedLeg>>)traces).Capacity+traceBytes+((NavPagedList<NavLocation>)locations).DirectoryRetainedBytes+((NavPagedList<int>)components).DirectoryRetainedBytes+((NavPagedList<int>)arcSlots).DirectoryRetainedBytes+((NavPagedList<IReadOnlyList<NavTypedLeg>>)traces).DirectoryRetainedBytes);
            }
            SupportsPackedField=PackedCapability(provider,Nodes);
        }
        internal static LayeredNavigationGraph ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            byte mode=reader.Input.ReadByte();if(mode>1)throw new ArgumentException("Unknown physical graph representation.");
            var providerRoot=reader.Edge<NavDeclarationStorage>();var bindingRoot=reader.Edge<NavDeclarationStorage>();var provider=providerRoot?.Facade as ILayeredNavigationProvider;var binding=bindingRoot?.Facade as NavGraphBinding;
            T Field<T>() where T:class{var root=reader.Edge();if(!(root is T result))throw new ArgumentException("Wrong physical graph container kind.");return result;}
            IReadOnlyList<NavGraphNode> nodes=null;IReadOnlyList<NavGraphArc> arcs=null;IReadOnlyList<NavConnectorResult> qualifications=null;IReadOnlyDictionary<int,IReadOnlyList<NavGraphArc>> outs=null,ins=null;IReadOnlyDictionary<NavNameKey,double> costs=null;
            NavPagedList<NavLocation> locations=null;NavPagedList<int> components=null,slots=null;NavPackedSlots neighbors=null,incomingSlots=null,traceSlots=null;NavPagedList<IReadOnlyList<NavTypedLeg>> traces=null;
            if(mode==0){nodes=Field<IReadOnlyList<NavGraphNode>>();arcs=Field<IReadOnlyList<NavGraphArc>>();qualifications=Field<IReadOnlyList<NavConnectorResult>>();outs=Field<IReadOnlyDictionary<int,IReadOnlyList<NavGraphArc>>>();ins=Field<IReadOnlyDictionary<int,IReadOnlyList<NavGraphArc>>>();costs=Field<IReadOnlyDictionary<NavNameKey,double>>();RequireSparseInputs(reader.Reservation,provider,binding,nodes,arcs,qualifications,outs,ins,costs);}
            else{locations=Field<NavPagedList<NavLocation>>();components=Field<NavPagedList<int>>();neighbors=Field<NavPackedSlots>();incomingSlots=Field<NavPackedSlots>();slots=Field<NavPagedList<int>>();traceSlots=Field<NavPackedSlots>();traces=Field<NavPagedList<IReadOnlyList<NavTypedLeg>>>();RequirePackedInputs(reader.Reservation,provider as PartitionNavigationProvider,binding,locations,components,neighbors,incomingSlots,slots,traceSlots,traces);}
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(LayoutBase);var result=allocation.Register(new LayeredNavigationGraph(provider,binding,nodes,arcs,qualifications,outs,ins,costs,locations,components,neighbors,incomingSlots,slots,traceSlots,traces));result.RetainProvider(binding);
                if(mode==0){result.OwnRoot(nodes,0);result.OwnRoot(arcs,1);result.OwnRoot(qualifications,2);result.OwnRoot(outs,3);result.OwnRoot(ins,4);result.OwnRoot(costs,5);}
                else{result.OwnRoot(locations,6);result.OwnRoot(components,7);result.OwnRoot(neighbors,8);result.OwnRoot(incomingSlots,9);result.OwnRoot(slots,10);result.OwnRoot(traceSlots,11);result.OwnRoot(traces,12);}
                result.ValidatePhysicalGraph(reader.Reservation);return result;
            }
        }
        private void ValidatePhysicalGraph(INavStorageReservation reservation)
        {
            int count=Nodes.Count;if(partitionComponents!=null&&partitionComponents.Count!=count)throw new ArgumentException("Physical packed component count contradiction.");if(count>300000||Arcs.Count>1200000)throw new ArgumentException("Physical graph capacity exceeded.");
            using(NavStorageScratch.Reserve(reservation,checked(8192+64L+4L*count))){
                var parents=new int[count];for(int n=0;n<count;n++)parents[n]=n;
                int Root(int n){while(parents[n]!=n){parents[n]=parents[parents[n]];n=parents[n];}return n;}
                void Union(int a,int b){a=Root(a);b=Root(b);if(a!=b)parents[Math.Max(a,b)]=Math.Min(a,b);}
                for(int n=0;n<count;n++){
                    var node=Nodes[n];if(node.Id!=n||node.Component<0||node.Component>=count||!provider.IsValid(node.Location,Binding.Clearance))throw new ArgumentException("Physical graph node identity/location contradiction.");
                    var expected=provider is PartitionNavigationProvider partition?partition.OwnedCheckpointLocation(node.Location):LayeredNavigationCompileWork.CanonicalLocation(provider,node.Location);
                    bool canonical=NavTypedLeg.SamePhysicalLocation(node.Location,expected);if(!canonical&&provider is LayeredNavigationProvider)foreach(var portal in provider.Portals)if(NavTypedLeg.SamePhysicalLocation(node.Location,portal.Entry)||NavTypedLeg.SamePhysicalLocation(node.Location,portal.Exit)){canonical=true;break;}if(!canonical)throw new ArgumentException("Physical graph node selector contradiction.");
                }
                if(partitionNeighbors==null)ValidateSparse(Union);else ValidatePacked(reservation,Union);
                for(int n=0;n<count;n++)if(Nodes[n].Component!=Root(n))throw new ArgumentException("Physical graph weak component contradiction.");
            }
        }
        private static bool SameArc(NavGraphArc a,NavGraphArc b)=>a.From==b.From&&a.To==b.To&&a.Cost==b.Cost&&a.Kind==b.Kind&&ReferenceEquals(a.PortalId,b.PortalId)&&ReferenceEquals(a.Trace,b.Trace)&&a.NameReference.Owner==b.NameReference.Owner&&a.NameReference.Role==b.NameReference.Role&&a.NameReference.Index==b.NameReference.Index;
        private void ValidateSparse(Action<int,int> union)
        {
            var layered=(LayeredNavigationProvider)provider;
            if(PortalQualifications.Count!=provider.Portals.Count||surfaceCosts.Count!=provider.Surfaces.Count)throw new ArgumentException("Physical graph qualification/cost count contradiction.");
            foreach(var pair in surfaceCosts){var surface=provider.Surfaces[0];bool found=false;foreach(var item in provider.Surfaces)if(item.Id==pair.Key.Value){surface=item;found=true;break;}if(!found||pair.Value!=surface.DistanceCost||pair.Key.Reference.Owner!=surface.StorageRoot||pair.Key.Reference.Role!=NavNameRole.SurfaceId||pair.Key.Reference.Index!=-1||!ReferenceEquals(pair.Key.Value,surface.Id))throw new ArgumentException("Physical graph surface cost selector contradiction.");}
            for(int n=0;n<PortalQualifications.Count;n++){var q=PortalQualifications[n];if(q==null)throw new ArgumentException("Missing physical graph qualification.");q.RequireQualification(provider,n);var status=layered.QualificationStatus(provider.Portals[n],Binding.Clearance);if(q.Status!=status||q.Cost!=(status==NavConnectorStatus.Ready?provider.Portals[n].TraversalCost:0))throw new ArgumentException("Physical graph qualification semantics contradiction.");}
            for(int n=0;n<Arcs.Count;n++){
                var arc=Arcs[n];if(arc.From<0||arc.From>=Nodes.Count||arc.To<0||arc.To>=Nodes.Count||double.IsNaN(arc.Cost)||double.IsInfinity(arc.Cost)||arc.Cost<0||arc.Trace!=null||n>0&&NavGraphArcComparer.Instance.Compare(Arcs[n-1],arc)>=0)throw new ArgumentException("Physical sparse arc shape/order contradiction.");
                var from=Nodes[arc.From].Location;var to=Nodes[arc.To].Location;
                if(arc.Kind==NavGraphArcKind.Surface){if(arc.PortalId!=null||arc.NameReference.Owner!=null||!provider.CanSweep(from,to,Binding.Clearance)||arc.Cost!=LayeredNavigationProvider.Distance(from.Position,to.Position)*SurfaceCost(from.SurfaceId))throw new ArgumentException("Physical surface arc qualification contradiction.");}
                else if(arc.Kind==NavGraphArcKind.Portal){
                    int at=-1;for(int p=0;p<provider.Portals.Count;p++)if(ReferenceEquals(provider.Portals[p].Id,arc.PortalId)){at=p;break;}if(at<0)throw new ArgumentException("Unknown physical portal arc.");var portal=provider.Portals[at];bool forward=from.Equals(portal.Entry)&&to.Equals(portal.Exit),reverse=from.Equals(portal.Exit)&&to.Equals(portal.Entry);
                    if(arc.NameReference.Owner!=portal.StorageRoot||arc.NameReference.Role!=NavNameRole.PortalId||arc.NameReference.Index!=-1||PortalQualifications[at].Status!=NavConnectorStatus.Ready||!forward&&!(reverse&&portal.Direction==NavPortalDirection.Both)||arc.Cost!=portal.TraversalCost)throw new ArgumentException("Physical portal arc owner/direction contradiction.");
                }else throw new ArgumentException("Unknown physical arc kind.");union(arc.From,arc.To);
            }
            void Adjacency(IReadOnlyDictionary<int,IReadOnlyList<NavGraphArc>> index,bool outgoing){
                long total=0;foreach(var pair in index){if(pair.Key<0||pair.Key>=Nodes.Count||!(pair.Value is NavOwnedStorage root)||!root.IsImmutableCaptureRoot)throw new ArgumentException("Physical adjacency key/list contradiction.");int previous=-1;
                    foreach(var arc in pair.Value){int low=0,high=Arcs.Count-1,at=-1;while(low<=high){int mid=low+(high-low)/2,cmp=NavGraphArcComparer.Instance.Compare(Arcs[mid],arc);if(cmp==0){at=mid;break;}if(cmp<0)low=mid+1;else high=mid-1;}
                        if(at<=previous||at<0||!SameArc(Arcs[at],arc)||(outgoing?arc.From:arc.To)!=pair.Key)throw new ArgumentException("Physical adjacency payload/order contradiction.");previous=at;total++;
                    }
                }if(total!=Arcs.Count)throw new ArgumentException("Physical adjacency omits global arcs.");
            }
            Adjacency(sparseOutgoing,true);Adjacency(sparseIncoming,false);
        }
        private void ValidatePacked(INavStorageReservation reservation,Action<int,int> union)
        {
            var partition=(PartitionNavigationProvider)provider;int nodes=Nodes.Count;int slots=checked(nodes*4);int capacity=Math.Max(partitionNeighbors.CapacitySlots,Math.Max(partitionIncoming.CapacitySlots,partitionTraceSlots.CapacitySlots));int maximum=((300000*4+NavPackedSlots.PageSize-1)/NavPackedSlots.PageSize)*NavPackedSlots.PageSize;
            if(partitionComponents.Count!=nodes||partitionNeighbors.CapacitySlots<slots||partitionIncoming.CapacitySlots<slots||partitionTraceSlots.CapacitySlots<slots||capacity>maximum)throw new ArgumentException("Physical packed graph slot capacity contradiction.");
            int arcAt=0,traceAt=0;double cell=Binding.Profile.GridCell;
            for(int n=0;n<nodes;n++){
                var point=partitionLocations[n].Position;int gx=(int)Math.Round(point.X/cell),gz=(int)Math.Round(point.Z/cell);if(point.X!=gx*cell||point.Z!=gz*cell||n>0&&(partitionLocations[n-1].Position.X>point.X||partitionLocations[n-1].Position.X==point.X&&partitionLocations[n-1].Position.Z>=point.Z))throw new ArgumentException("Physical packed graph grid/order contradiction.");
            }
            for(int slot=0;slot<capacity;slot++){
                int neighbor=slot<partitionNeighbors.CapacitySlots?partitionNeighbors.Get(slot):0,incoming=slot<partitionIncoming.CapacitySlots?partitionIncoming.Get(slot):0,trace=slot<partitionTraceSlots.CapacitySlots?partitionTraceSlots.Get(slot):0;
                if(slot>=slots){if(neighbor!=0||incoming!=0||trace!=0)throw new ArgumentException("Hidden physical packed slot payload.");continue;}
                if(incoming<0||incoming>slots||incoming!=0&&(partitionNeighbors.Get(incoming-1)!=slot/4+1||((incoming-1)%4^1)!=slot%4))throw new ArgumentException("Physical packed incoming inverse contradiction.");
                if(neighbor==0){if(trace!=0)throw new ArgumentException("Trace without physical neighbor.");continue;}
                if(neighbor<1||neighbor>nodes||arcAt>=partitionArcSlots.Count||partitionArcSlots[arcAt++]!=slot)throw new ArgumentException("Physical packed arc stream contradiction.");int from=slot/4,to=neighbor-1,d=slot%4;var a=partitionLocations[from];var b=partitionLocations[to];int gx=(int)Math.Round(a.Position.X/cell),gz=(int)Math.Round(a.Position.Z/cell);var expected=new NavPoint((gx+(d==0?1:d==1?-1:0))*cell,(gz+(d==2?1:d==3?-1:0))*cell);
                if(!b.Position.Equals(expected)||partitionIncoming.Get(to*4+(d^1))!=slot+1)throw new ArgumentException("Physical packed neighbor/cardinal inverse contradiction.");
                IReadOnlyList<NavTypedLeg> stored=null;if(trace!=0){if(trace!=++traceAt||trace>partitionTraces.Count)throw new ArgumentException("Physical packed trace stream contradiction.");stored=partitionTraces[trace-1];if(stored==null)throw new ArgumentException("Missing physical packed trace.");bool portal=false;foreach(var leg in stored)if(leg!=null&&leg.Kind==NavTypedLegKind.Portal)portal=true;if(!portal)throw new ArgumentException("Physical packed trace has no crossing.");}
                partition.VerifyPhysicalTrace(a,b,Binding.Clearance,stored,reservation);union(from,to);
            }
            if(arcAt!=partitionArcSlots.Count||traceAt!=partitionTraces.Count)throw new ArgumentException("Orphan physical packed arc/trace.");
        }
    }
}
