using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    internal static class NavPhysicalMacro
    {
        internal static bool ReadBoolean(NavPhysicalBodyReader reader)
        {byte value=reader.Input.ReadByte();if(value>1)throw new ArgumentException("Noncanonical physical macro Boolean.");return value==1;}
        internal static void WriteEdge(NavPhysicalBodyWriter writer,NavOwnedStorage parent,NavOwnedStorage child,bool optional=false)
        {
            if(child==null){if(!optional)throw new ArgumentException("Missing physical macro edge.");writer.Edge(null);return;}
            parent.RequireEdge(child);if(!child.IsImmutableCaptureRoot)throw new ArgumentException("Mutable physical macro edge.");writer.Edge(child);
        }
        internal static T ReadEdge<T>(NavPhysicalBodyReader reader,bool optional=false) where T:NavOwnedStorage
        {
            var child=reader.Edge<T>();if(child==null){if(!optional)throw new ArgumentException("Missing physical macro edge.");return null;}
            NavStorageAdmission.RequireOwned(reader.Reservation,child);if(!child.IsImmutableCaptureRoot)throw new ArgumentException("Mutable physical macro edge.");return child;
        }
        internal static T Acquire<T>(NavOwnedStorage parent,T child) where T:NavOwnedStorage
        {if(child!=null){parent.RequireEdge(child);child.Retain();}return child;}
        internal static void WriteMetadata(NavPhysicalBodyWriter writer,NavOwnedStorage parent,INavMetadataFacade facade)
        {
            if(facade==null)throw new ArgumentException("Missing physical macro metadata.");parent.RequireEdge(facade.StorageRoot);
            var reference=new NavMetadataReference(facade.StorageRoot,facade.MetadataRole);
            if(!ReferenceEquals(reference.Value,facade)||!facade.StorageRoot.IsImmutableCaptureRoot)throw new ArgumentException("Physical macro metadata selector contradiction.");reference.Write(writer);
        }
        internal static T ReadMetadata<T>(NavPhysicalBodyReader reader,NavMetadataRole role) where T:class,INavMetadataFacade
        {
            var reference=NavMetadataReference.Read(reader);if(reference.Role!=role||!(reference.Value is T value))throw new ArgumentException("Wrong physical macro metadata role.");
            NavMetadataOwnership.Require(value,reader.Reservation);if(!reference.Owner.IsImmutableCaptureRoot)throw new ArgumentException("Mutable physical macro metadata.");return value;
        }
        internal static bool SameArc(NavGraphArc a,NavGraphArc b)=>a.From==b.From&&a.To==b.To&&a.Cost==b.Cost&&a.Kind==b.Kind&&ReferenceEquals(a.PortalId,b.PortalId)&&ReferenceEquals(a.Trace,b.Trace)&&a.NameReference.Owner==b.NameReference.Owner&&a.NameReference.Role==b.NameReference.Role&&a.NameReference.Index==b.NameReference.Index;
    }
    public sealed partial class LayeredNavigationCompileWork : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>frozen&&IsRegistered&&preparedReservation==null&&pendingOutgoing==null;
        NavPhysicalTag INavPhysicalBody.PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Compiler);
        void INavPhysicalBody.WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(2048)||NavStorageLayout.ObjectBytesBound(GetType())>2048||!ReferenceEquals(ownedProvider,(provider as INavStorageFacade)?.StorageRoot))throw new ArgumentException("Physical compiler owner/layout contradiction.");
            var output=writer.Output;output.Write((int)Phase);output.Write(Consumed);
            output.Write(surface);output.Write(portal);output.Write(gridCursor);output.Write(neighbor);output.Write(end);output.Write(portalGridCursor);output.Write(portalArcCursor);output.Write(nodeCursor);output.Write(arcCursor);output.Write(wrapCursor);output.Write(gridCount);
            output.Write(x);output.Write(z);output.Write(minX);output.Write(maxX);output.Write(minZ);output.Write(maxZ);output.Write(sampled);output.Write(adjacencyStorageBytes);
            NavPhysicalMacro.WriteEdge(writer,this,ownedProvider);NavPhysicalMacro.WriteMetadata(writer,this,clearance);NavPhysicalMacro.WriteMetadata(writer,this,profile);NavPhysicalMacro.WriteMetadata(writer,this,limits);
            NavPhysicalMacro.WriteEdge(writer,this,partition,true);
            if(partition!=null){if(!ReferenceEquals(Graph,partition.Graph))throw new ArgumentException("Partition wrapper graph contradiction.");return;}
            NavPhysicalMacro.WriteEdge(writer,this,samples);NavPhysicalMacro.WriteEdge(writer,this,parent);NavPhysicalMacro.WriteEdge(writer,this,portalEnds);NavPhysicalMacro.WriteEdge(writer,this,qualifications);
            NavPhysicalMacro.WriteEdge(writer,this,sortedArcs);NavPhysicalMacro.WriteEdge(writer,this,arcs);NavPhysicalMacro.WriteEdge(writer,this,nodes);NavPhysicalMacro.WriteEdge(writer,this,grid);
            NavPhysicalMacro.WriteEdge(writer,this,outLists);NavPhysicalMacro.WriteEdge(writer,this,inLists);NavPhysicalMacro.WriteEdge(writer,this,outgoing);NavPhysicalMacro.WriteEdge(writer,this,incoming);NavPhysicalMacro.WriteEdge(writer,this,surfaceCosts);NavPhysicalMacro.WriteEdge(writer,this,Graph,true);
        }
        internal static LayeredNavigationCompileWork ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var input=reader.Input;var phase=(Stage)input.ReadInt32();int consumed=input.ReadInt32();
            int surface=input.ReadInt32(),portal=input.ReadInt32(),gridCursor=input.ReadInt32(),neighbor=input.ReadInt32(),end=input.ReadInt32(),portalGridCursor=input.ReadInt32(),portalArcCursor=input.ReadInt32(),nodeCursor=input.ReadInt32(),arcCursor=input.ReadInt32(),wrapCursor=input.ReadInt32(),gridCount=input.ReadInt32();
            long x=input.ReadInt64(),z=input.ReadInt64(),minX=input.ReadInt64(),maxX=input.ReadInt64(),minZ=input.ReadInt64(),maxZ=input.ReadInt64(),sampled=input.ReadInt64(),adjacency=input.ReadInt64();
            if(!Enum.IsDefined(typeof(Stage),phase)||adjacency<0||NavStorageLayout.ObjectBytesBound(typeof(LayeredNavigationCompileWork))>2048)throw new ArgumentException("Invalid physical compiler phase/layout.");
            var providerRoot=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);if(!(providerRoot.Facade is ILayeredNavigationProvider provider))throw new ArgumentException("Missing physical compiler provider.");
            var clearance=NavPhysicalMacro.ReadMetadata<NavClearanceProfile>(reader,NavMetadataRole.Clearance);var profile=NavPhysicalMacro.ReadMetadata<NavGraphProfile>(reader,NavMetadataRole.GraphProfile);var limits=NavPhysicalMacro.ReadMetadata<NavGraphCompileLimits>(reader,NavMetadataRole.CompileLimits);
            var partition=NavPhysicalMacro.ReadEdge<PartitionNavigationCompileWork>(reader,true);
            if(partition!=null){
            partition.RequireWrapperAssociation(provider,clearance,profile,limits);
            var wrapper=new LayeredNavigationCompileWork(provider,clearance,profile,limits,reader.Reservation.TryReserve,false,false,true){Phase=phase,Consumed=consumed,surface=surface,portal=portal,gridCursor=gridCursor,neighbor=neighbor,end=end,portalGridCursor=portalGridCursor,portalArcCursor=portalArcCursor,nodeCursor=nodeCursor,arcCursor=arcCursor,wrapCursor=wrapCursor,gridCount=gridCount,x=x,z=z,minX=minX,maxX=maxX,minZ=minZ,maxZ=maxZ,sampled=sampled,adjacencyStorageBytes=adjacency,frozen=true,preparedReservation=null};
                wrapper.partition=NavPhysicalMacro.Acquire(wrapper,partition);wrapper.Graph=partition.Graph;wrapper.ValidatePartitionWrapper();return wrapper;
            }
            var samples=NavPhysicalMacro.ReadEdge<NavPagedList<NavLocation>>(reader);
            var parent=NavPhysicalMacro.ReadEdge<NavPagedList<int>>(reader);
            var portalEnds=NavPhysicalMacro.ReadEdge<NavPagedList<NavPortalEndpoints>>(reader);
            var qualifications=NavPhysicalMacro.ReadEdge<NavPagedList<NavConnectorResult>>(reader);
            var sortedArcs=NavPhysicalMacro.ReadEdge<NavCowOrderedSet<NavGraphArc>>(reader);
            var arcs=NavPhysicalMacro.ReadEdge<NavPagedList<NavGraphArc>>(reader);
            var nodes=NavPhysicalMacro.ReadEdge<NavPagedList<NavGraphNode>>(reader);
            var grid=NavPhysicalMacro.ReadEdge<NavCowDictionary<NavGridCoordinate,int>>(reader);
            var outLists=NavPhysicalMacro.ReadEdge<NavCowDictionary<int,NavPagedList<NavGraphArc>>>(reader);
            var inLists=NavPhysicalMacro.ReadEdge<NavCowDictionary<int,NavPagedList<NavGraphArc>>>(reader);
            var outgoing=NavPhysicalMacro.ReadEdge<NavCowDictionary<int,IReadOnlyList<NavGraphArc>>>(reader);
            var incoming=NavPhysicalMacro.ReadEdge<NavCowDictionary<int,IReadOnlyList<NavGraphArc>>>(reader);
            var surfaceCosts=NavPhysicalMacro.ReadEdge<NavCowDictionary<NavNameKey,double>>(reader);
            var Graph=NavPhysicalMacro.ReadEdge<LayeredNavigationGraph>(reader,true);
            var work=new LayeredNavigationCompileWork(provider,clearance,profile,limits,reader.Reservation.TryReserve,false,false,true){Phase=phase,Consumed=consumed,surface=surface,portal=portal,gridCursor=gridCursor,neighbor=neighbor,end=end,portalGridCursor=portalGridCursor,portalArcCursor=portalArcCursor,nodeCursor=nodeCursor,arcCursor=arcCursor,wrapCursor=wrapCursor,gridCount=gridCount,x=x,z=z,minX=minX,maxX=maxX,minZ=minZ,maxZ=maxZ,sampled=sampled,adjacencyStorageBytes=adjacency,frozen=true,preparedReservation=null};
            work.samples=NavPhysicalMacro.Acquire(work,samples);work.parent=NavPhysicalMacro.Acquire(work,parent);
            work.portalEnds=NavPhysicalMacro.Acquire(work,portalEnds);work.qualifications=NavPhysicalMacro.Acquire(work,qualifications);
            work.sortedArcs=NavPhysicalMacro.Acquire(work,sortedArcs);work.arcs=NavPhysicalMacro.Acquire(work,arcs);work.nodes=NavPhysicalMacro.Acquire(work,nodes);work.grid=NavPhysicalMacro.Acquire(work,grid);
            work.outLists=NavPhysicalMacro.Acquire(work,outLists);work.inLists=NavPhysicalMacro.Acquire(work,inLists);
            work.outgoing=NavPhysicalMacro.Acquire(work,outgoing);work.incoming=NavPhysicalMacro.Acquire(work,incoming);work.surfaceCosts=NavPhysicalMacro.Acquire(work,surfaceCosts);work.Graph=NavPhysicalMacro.Acquire(work,Graph);
            work.borrowedGraph=true;using(NavStorageScratch.Reserve(reader.Reservation,checked(16384L+4L*work.samples.Count)))work.ValidatePhysicalCompiler();return work;
        }
        private void ValidatePartitionWrapper()
        {
            partition.RequireWrapperAssociation(provider,clearance,profile,limits);
            if(Consumed!=partition.Consumed||surface!=0||portal!=0||gridCursor!=0||neighbor!=0||end!=0||portalGridCursor!=0||portalArcCursor!=0||nodeCursor!=0||arcCursor!=0||wrapCursor!=0||gridCount!=0||x!=0||z!=0||minX!=0||maxX!=0||minZ!=0||maxZ!=0||sampled!=0||adjacencyStorageBytes!=0)throw new ArgumentException("Physical partition wrapper owner/cursor contradiction.");
            Stage expected=partition.Phase==PartitionNavigationCompileWork.Stage.Ready?Stage.Ready:partition.Phase==PartitionNavigationCompileWork.Stage.Arcs?Stage.GridEdges:partition.Phase==PartitionNavigationCompileWork.Stage.Components?Stage.Nodes:Stage.Samples;
            // Before the first Advance the wrapper has not projected Bounds yet.
            if(Phase!=expected&&!(Consumed==0&&partition.Phase==PartitionNavigationCompileWork.Stage.Bounds&&Phase==Stage.SurfaceBounds))throw new ArgumentException("Physical partition wrapper phase contradiction.");
        }
        private void ValidatePhysicalCompiler()
        {
            if(!(provider is LayeredNavigationProvider layered)||surface<0||surface>provider.Surfaces.Count||Phase==Stage.Samples&&surface==provider.Surfaces.Count||Phase>Stage.Samples&&surface!=provider.Surfaces.Count||samples.Count>limits.MaxNodes||parent.Count!=samples.Count||sampled<0||sampled>limits.MaxSamples||sortedArcs.Count+(long)arcs.Count>limits.MaxArcs||gridCount<0||gridCount>grid.Count||portal<0||portal>provider.Portals.Count||gridCursor<0||gridCursor>gridCount||neighbor<0||neighbor>1||end<0||end>1||portalGridCursor<0||portalGridCursor>gridCount||portalArcCursor<0||portalArcCursor>1||nodeCursor<0||nodeCursor>samples.Count||nodes.Count!=nodeCursor||arcCursor<0||arcCursor>arcs.Count||wrapCursor<0||wrapCursor>nodes.Count)throw new ArgumentException("Physical sparse compiler capacity/cursor contradiction.");
            if(Phase==Stage.Samples){var bounds=provider.Surfaces[surface].Area.Boundary;if(minX!=(long)Math.Ceiling(bounds.MinX/profile.GridCell)||maxX!=(long)Math.Floor(bounds.MaxX/profile.GridCell)||minZ!=(long)Math.Ceiling(bounds.MinZ/profile.GridCell)||maxZ!=(long)Math.Floor(bounds.MaxZ/profile.GridCell)||x<minX||x>maxX||z<minZ||z>maxZ)throw new ArgumentException("Physical compiler scan bounds contradiction.");}
            int costCount=Phase==Stage.SurfaceBounds?surface:Phase==Stage.Samples?surface+1:provider.Surfaces.Count;
            if(surfaceCosts.Count!=costCount)throw new ArgumentException("Physical compiler cost prefix contradiction.");
            int costAt=0;foreach(var pair in surfaceCosts){var declaration=provider.Surfaces[costAt++];if(pair.Key.Reference.Owner!=declaration.StorageRoot||pair.Key.Reference.Role!=NavNameRole.SurfaceId||pair.Key.Reference.Index!=-1||!ReferenceEquals(pair.Key.Value,declaration.Id)||pair.Value!=declaration.DistanceCost)throw new ArgumentException("Physical compiler cost owner contradiction.");}
            int gridAt=0;foreach(var pair in grid){if(pair.Value!=gridAt++||pair.Value>=samples.Count||!NavTypedLeg.SamePhysicalLocation(samples[pair.Value],new NavLocation(samples[pair.Value].Position,pair.Key.NameReference))||!NavTypedLeg.SamePhysicalLocation(samples[pair.Value],CanonicalLocation(provider,samples[pair.Value]))||samples[pair.Value].Position.X!=pair.Key.Item2*profile.GridCell||samples[pair.Value].Position.Z!=pair.Key.Item3*profile.GridCell)throw new ArgumentException("Physical compiler grid/sample prefix contradiction.");}
            if(Phase<=Stage.Samples&&samples.Count!=grid.Count||Phase<=Stage.Portals&&gridCount!=0||Phase>Stage.Portals&&gridCount!=grid.Count)throw new ArgumentException("Physical compiler frozen grid count contradiction.");
            int portalCount=Phase<Stage.Portals?0:Phase==Stage.Portals?portal:provider.Portals.Count;if(qualifications.Count!=portalCount||portalEnds.Count!=portalCount)throw new ArgumentException("Physical compiler qualification prefix contradiction.");
            int sampleAt=grid.Count;for(int n=0;n<portalCount;n++){
                var qualification=qualifications[n];qualification.RequireQualification(provider,n);var declaration=provider.Portals[n];var status=layered.QualificationStatus(declaration,clearance);if(qualification.Status!=status||qualification.Cost!=(status==NavConnectorStatus.Ready?declaration.TraversalCost:0))throw new ArgumentException("Physical compiler qualification semantics contradiction.");
                var pair=portalEnds[n];if(pair.Ready!=(status==NavConnectorStatus.Ready)||!pair.Ready&&(pair.Item1!=0||pair.Item2!=0))throw new ArgumentException("Physical compiler portal endpoint status contradiction.");
                if(pair.Ready){if(pair.Item1!=sampleAt||pair.Item2!=sampleAt+1||sampleAt+1>=samples.Count||!NavTypedLeg.SamePhysicalLocation(samples[sampleAt++],declaration.Entry)||!NavTypedLeg.SamePhysicalLocation(samples[sampleAt++],declaration.Exit))throw new ArgumentException("Physical compiler portal endpoint prefix contradiction.");}
            }
            if(sampleAt!=samples.Count)throw new ArgumentException("Physical compiler orphan sample.");
            for(int n=0;n<samples.Count;n++)if(!provider.IsValid(samples[n],clearance))throw new ArgumentException("Physical compiler invalid sample.");
            if(Phase<Stage.GridEdges&&(sortedArcs.Count!=0||arcs.Count!=0)||Phase<Stage.Nodes&&nodes.Count!=0||Phase<Stage.SortedArcs&&arcs.Count!=0||Phase<Stage.Adjacency&&arcCursor!=0||Phase<Stage.WrapAdjacency&&wrapCursor!=0||Phase>Stage.GridEdges&&(gridCursor!=gridCount||neighbor!=0)||Phase>Stage.PortalEdges&&(end!=0||portalGridCursor!=0)||Phase>Stage.PortalArcs&&portalArcCursor!=0||Phase>Stage.Nodes&&nodeCursor!=samples.Count||Phase>Stage.SortedArcs&&sortedArcs.Count!=0||Phase>Stage.Adjacency&&arcCursor!=arcs.Count||Phase==Stage.Ready&&wrapCursor!=nodes.Count||(Graph!=null)!=(Phase==Stage.Ready))throw new ArgumentException("Physical compiler phase prefix contradiction.");
            var expectedParents=new int[samples.Count];for(int n=0;n<expectedParents.Length;n++)expectedParents[n]=n;
            int Root(int n){while(expectedParents[n]!=n){expectedParents[n]=expectedParents[expectedParents[n]];n=expectedParents[n];}return n;}
            void CheckArc(NavGraphArc arc){
                if(arc.From<0||arc.From>=samples.Count||arc.To<0||arc.To>=samples.Count||arc.Trace!=null||double.IsNaN(arc.Cost)||double.IsInfinity(arc.Cost)||arc.Cost<0)throw new ArgumentException("Physical compiler arc shape contradiction.");
                var from=samples[arc.From];var to=samples[arc.To];bool processed=false;
                if(arc.Kind==NavGraphArcKind.Surface){
                    if(arc.PortalId!=null||arc.NameReference.Owner!=null||!provider.CanSweep(from,to,clearance)||arc.Cost!=LayeredNavigationProvider.Distance(from.Position,to.Position)*surfaceCosts[new NavNameKey(from.SurfaceId)])throw new ArgumentException("Physical compiler surface arc contradiction.");
                    int a=arc.From,b=arc.To;int grids=grid.Count;
                    if(a<grids&&b<grids){for(int orientation=0;orientation<2;orientation++){var pa=samples[a].Position;var pb=samples[b].Position;long gx=(long)Math.Round(pa.X/profile.GridCell),gz=(long)Math.Round(pa.Z/profile.GridCell);int d=pb.Equals(new NavPoint((gx+1)*profile.GridCell,gz*profile.GridCell))?0:pb.Equals(new NavPoint(gx*profile.GridCell,(gz+1)*profile.GridCell))?1:-1;if(from.SurfaceId==to.SurfaceId&&d>=0&&(Phase>Stage.GridEdges||a<gridCursor||a==gridCursor&&d<neighbor))processed=true;int swap=a;a=b;b=swap;}}
                    else if(Phase>=Stage.PortalEdges){int endpoint=a>=grids?a:b,ordinary=a>=grids?b:a;if(ordinary<grids&&LayeredNavigationProvider.Distance(from.Position,to.Position)<=Math.Sqrt(2)*profile.GridCell){for(int p=0;p<portalEnds.Count;p++){var pair=portalEnds[p];if(!pair.Ready)continue;int which=endpoint==pair.Item1?0:endpoint==pair.Item2?1:-1;if(which>=0&&(Phase>Stage.PortalEdges||p<portal||p==portal&&(which<end||which==end&&ordinary<portalGridCursor)))processed=true;}}}
                }else if(arc.Kind==NavGraphArcKind.Portal&&Phase>=Stage.PortalArcs){
                    for(int p=0;p<portalEnds.Count;p++){var pair=portalEnds[p];var declaration=provider.Portals[p];int d=pair.Ready&&arc.From==pair.Item1&&arc.To==pair.Item2?0:pair.Ready&&arc.From==pair.Item2&&arc.To==pair.Item1&&declaration.Direction==NavPortalDirection.Both?1:-1;
                        if(d>=0&&arc.NameReference.Owner==declaration.StorageRoot&&arc.NameReference.Role==NavNameRole.PortalId&&arc.NameReference.Index==-1&&ReferenceEquals(arc.PortalId,declaration.Id)&&arc.Cost==declaration.TraversalCost&&(Phase>Stage.PortalArcs||p<portal||p==portal&&d<portalArcCursor))processed=true;}
                }
                if(!processed)throw new ArgumentException("Physical compiler arc beyond processed prefix.");int ra=Root(arc.From),rb=Root(arc.To);if(ra!=rb)expectedParents[Math.Max(ra,rb)]=Math.Min(ra,rb);
            }
            NavGraphArc previous=default;bool any=false;foreach(var arc in arcs){if(any&&NavGraphArcComparer.Instance.Compare(previous,arc)>=0)throw new ArgumentException("Physical materialized arc order contradiction.");CheckArc(arc);previous=arc;any=true;}
            foreach(var arc in sortedArcs){if(any&&NavGraphArcComparer.Instance.Compare(previous,arc)>=0)throw new ArgumentException("Physical pending/materialized arc order contradiction.");CheckArc(arc);previous=arc;any=true;}
            int StoredRoot(int n){int steps=0;while(parent[n]!=n){if(parent[n]<0||parent[n]>=n||++steps>samples.Count)throw new ArgumentException("Physical compiler parent forest contradiction.");n=parent[n];}return n;}
            for(int n=0;n<samples.Count;n++)if(parent[n]<0||parent[n]>n||StoredRoot(n)!=Root(n))throw new ArgumentException("Physical compiler weak components contradiction.");
            for(int n=0;n<nodes.Count;n++)if(nodes[n].Id!=n||nodes[n].Component!=StoredRoot(n)||!NavTypedLeg.SamePhysicalLocation(nodes[n].Location,samples[n]))throw new ArgumentException("Physical compiler node prefix contradiction.");
            long adjacency=0;
            void CheckIndex(NavCowDictionary<int,NavPagedList<NavGraphArc>> staging,NavCowDictionary<int,IReadOnlyList<NavGraphArc>> published,bool outgoingDirection){
                int wrapped=0;long count=0;foreach(var pair in staging){if(pair.Key<0||pair.Key>=nodes.Count||pair.Value.Count==0)throw new ArgumentException("Physical compiler staging adjacency key contradiction.");adjacency=checked(adjacency+pair.Value.CommittedStorageBytes(128));int at=0;
                    int previousIndex=-1;foreach(var arc in pair.Value){int low=0,high=arcCursor-1,found=-1;while(low<=high){int middle=low+(high-low)/2,comparison=NavGraphArcComparer.Instance.Compare(arcs[middle],arc);if(comparison==0){found=middle;break;}if(comparison<0)low=middle+1;else high=middle-1;}if(found<=previousIndex||found<0||!NavPhysicalMacro.SameArc(arcs[found],arc)||(outgoingDirection?arc.From:arc.To)!=pair.Key)throw new ArgumentException("Physical compiler staging adjacency prefix contradiction.");previousIndex=found;at++;}count+=at;
                    if(pair.Key<wrapCursor){if(!published.TryGetValue(pair.Key,out var list)||!ReferenceEquals(pair.Value,list))throw new ArgumentException("Physical compiler published adjacency alias contradiction.");wrapped++;}
                }if(count!=arcCursor||published.Count!=wrapped)throw new ArgumentException("Physical compiler missing adjacency prefix.");
            }
            CheckIndex(outLists,outgoing,true);CheckIndex(inLists,incoming,false);if(adjacencyStorageBytes!=adjacency)throw new ArgumentException("Physical compiler adjacency diagnostic contradiction.");
            if(Graph!=null){Graph.Binding.RequireAssociation(provider);if(!ReferenceEquals(Graph.Binding.Clearance,clearance)||!ReferenceEquals(Graph.Binding.Profile,profile)||Graph.Nodes.Count!=nodes.Count||Graph.Arcs.Count!=arcs.Count||Graph.PortalQualifications.Count!=qualifications.Count)throw new ArgumentException("Physical compiler Ready graph association contradiction.");
                for(int n=0;n<nodes.Count;n++)if(Graph.Nodes[n].Id!=nodes[n].Id||Graph.Nodes[n].Component!=nodes[n].Component||!NavTypedLeg.SamePhysicalLocation(Graph.Nodes[n].Location,nodes[n].Location))throw new ArgumentException("Physical compiler Ready graph node contradiction.");
                for(int n=0;n<arcs.Count;n++)if(!NavPhysicalMacro.SameArc(Graph.Arcs[n],arcs[n]))throw new ArgumentException("Physical compiler Ready graph arc contradiction.");for(int n=0;n<qualifications.Count;n++)if(!ReferenceEquals(Graph.PortalQualifications[n],qualifications[n]))throw new ArgumentException("Physical compiler Ready qualification alias contradiction.");
            }
            ValidateConsumedPrefix();
        }
        private void ValidateConsumedPrefix()
        {
            long scan=0;int complete=Phase<=Stage.Samples?surface:provider.Surfaces.Count;
            long ScanSize(int at){var boundary=provider.Surfaces[at].Area.Boundary;long a=(long)Math.Ceiling(boundary.MinX/profile.GridCell),b=(long)Math.Floor(boundary.MaxX/profile.GridCell),c=(long)Math.Ceiling(boundary.MinZ/profile.GridCell),d=(long)Math.Floor(boundary.MaxZ/profile.GridCell);return b<a||d<c?0:checked((b-a+1)*(d-c+1));}
            for(int n=0;n<complete;n++)scan=checked(scan+ScanSize(n));
            long expectedSampled=Phase==Stage.Samples?checked(scan+ScanSize(surface)):scan;if(sampled!=expectedSampled)throw new ArgumentException("Physical compiler sampling counter contradiction.");
            void Check(long value){if(Consumed!=unchecked((int)value))throw new ArgumentException("Physical compiler consumed prefix contradiction.");}
            long expected=checked(complete+scan);
            if(Phase==Stage.SurfaceBounds){Check(expected);return;}if(Phase==Stage.Samples){Check(checked(expected+1+(x-minX)*(maxZ-minZ+1)+z-minZ));return;}
            expected++;if(Phase==Stage.Portals){Check(expected+portal);return;}expected=checked(expected+provider.Portals.Count+1);
            if(Phase==Stage.GridEdges){if(gridCursor==gridCount&&neighbor!=0)throw new ArgumentException("Physical compiler terminal neighbor cursor contradiction.");Check(expected+2L*gridCursor+neighbor);return;}expected=checked(expected+2L*gridCount+1);
            long PortalSteps(int count){long value=0;for(int n=0;n<count;n++)value=checked(value+(portalEnds[n].Ready?2L*(gridCount+1):1));return value;}
            if(Phase==Stage.PortalEdges){if(portal==provider.Portals.Count&&(end!=0||portalGridCursor!=0)||portal<provider.Portals.Count&&!portalEnds[portal].Ready&&(end!=0||portalGridCursor!=0))throw new ArgumentException("Physical compiler terminal portal cursor contradiction.");Check(checked(expected+PortalSteps(portal)+(long)end*(gridCount+1)+portalGridCursor));return;}expected=checked(expected+PortalSteps(provider.Portals.Count)+1);
            if(Phase==Stage.PortalArcs){if(portal==provider.Portals.Count&&portalArcCursor!=0)throw new ArgumentException("Physical compiler terminal portal arc contradiction.");Check(expected+2L*portal+portalArcCursor);return;}expected=checked(expected+2L*provider.Portals.Count+1);
            if(Phase==Stage.Nodes){Check(expected+nodeCursor);return;}expected=checked(expected+samples.Count+1);
            if(Phase==Stage.SortedArcs){Check(expected+arcs.Count);return;}expected=checked(expected+arcs.Count+1);
            if(Phase==Stage.Adjacency){Check(expected+arcCursor);return;}expected=checked(expected+arcs.Count+1);
            if(Phase==Stage.WrapAdjacency){Check(expected+wrapCursor);return;}Check(checked(expected+nodes.Count+1));
        }
    }
    internal sealed partial class PartitionNavigationCompileWork : INavPhysicalBody
    {
        internal void RequireWrapperAssociation(ILayeredNavigationProvider expectedProvider,NavClearanceProfile expectedClearance,NavGraphProfile expectedProfile,NavGraphCompileLimits expectedLimits)
        {if(!ReferenceEquals(provider,expectedProvider)||!ReferenceEquals(clearance,expectedClearance)||!ReferenceEquals(profile,expectedProfile)||!ReferenceEquals(limits,expectedLimits))throw new ArgumentException("Physical partition wrapper metadata contradiction.");}
        internal override bool IsImmutableCaptureRoot=>frozen&&IsRegistered&&preparedReservation==null;
        NavPhysicalTag INavPhysicalBody.PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.PartitionCompiler);
        void INavPhysicalBody.WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(1024)||NavStorageLayout.ObjectBytesBound(GetType())>1024||!ReferenceEquals(ownedProvider,provider.StorageRoot))throw new ArgumentException("Physical partition compiler owner/layout contradiction.");
            var output=writer.Output;output.Write((int)Phase);output.Write(Consumed);output.Write(min);output.Write(max);output.Write(width);output.Write(x);output.Write(z);output.Write(node);output.Write(direction);output.Write(componentCursor);output.Write(sampled);output.Write(traceBytes);
            NavPhysicalMacro.WriteEdge(writer,this,ownedProvider);NavPhysicalMacro.WriteMetadata(writer,this,clearance);NavPhysicalMacro.WriteMetadata(writer,this,profile);NavPhysicalMacro.WriteMetadata(writer,this,limits);
            NavPhysicalMacro.WriteEdge(writer,this,locations);NavPhysicalMacro.WriteEdge(writer,this,components);NavPhysicalMacro.WriteEdge(writer,this,arcSlots);NavPhysicalMacro.WriteEdge(writer,this,traces);NavPhysicalMacro.WriteEdge(writer,this,grid,true);NavPhysicalMacro.WriteEdge(writer,this,neighbors);NavPhysicalMacro.WriteEdge(writer,this,incoming);NavPhysicalMacro.WriteEdge(writer,this,traceSlots);NavPhysicalMacro.WriteEdge(writer,this,Graph,true);
        }
        internal static PartitionNavigationCompileWork ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var input=reader.Input;var phase=(Stage)input.ReadInt32();int consumed=input.ReadInt32(),min=input.ReadInt32(),max=input.ReadInt32(),width=input.ReadInt32(),x=input.ReadInt32(),z=input.ReadInt32(),node=input.ReadInt32(),direction=input.ReadInt32(),componentCursor=input.ReadInt32();long sampled=input.ReadInt64(),traceBytes=input.ReadInt64();
            if(!Enum.IsDefined(typeof(Stage),phase)||traceBytes<0||NavStorageLayout.ObjectBytesBound(typeof(PartitionNavigationCompileWork))>1024)throw new ArgumentException("Invalid physical partition compiler phase/layout.");
            var providerRoot=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);if(!(providerRoot.Facade is PartitionNavigationProvider provider))throw new ArgumentException("Missing physical partition compiler provider.");
            var clearance=NavPhysicalMacro.ReadMetadata<NavClearanceProfile>(reader,NavMetadataRole.Clearance);var profile=NavPhysicalMacro.ReadMetadata<NavGraphProfile>(reader,NavMetadataRole.GraphProfile);var limits=NavPhysicalMacro.ReadMetadata<NavGraphCompileLimits>(reader,NavMetadataRole.CompileLimits);
            var locations=NavPhysicalMacro.ReadEdge<NavPagedList<NavLocation>>(reader);
            var components=NavPhysicalMacro.ReadEdge<NavPagedList<int>>(reader);
            var arcSlots=NavPhysicalMacro.ReadEdge<NavPagedList<int>>(reader);
            var traces=NavPhysicalMacro.ReadEdge<NavPagedList<IReadOnlyList<NavTypedLeg>>>(reader);
            var grid=NavPhysicalMacro.ReadEdge<NavPackedSlots>(reader,true);
            var neighbors=NavPhysicalMacro.ReadEdge<NavPackedSlots>(reader);
            var incoming=NavPhysicalMacro.ReadEdge<NavPackedSlots>(reader);
            var traceSlots=NavPhysicalMacro.ReadEdge<NavPackedSlots>(reader);
            var Graph=NavPhysicalMacro.ReadEdge<LayeredNavigationGraph>(reader,true);
            var work=new PartitionNavigationCompileWork(provider,clearance,profile,limits,reader.Reservation.TryReserve,false,false,true){Phase=phase,Consumed=consumed,min=min,max=max,width=width,x=x,z=z,node=node,direction=direction,componentCursor=componentCursor,sampled=sampled,traceBytes=traceBytes,frozen=true,preparedReservation=null};
            work.locations=NavPhysicalMacro.Acquire(work,locations);work.components=NavPhysicalMacro.Acquire(work,components);work.arcSlots=NavPhysicalMacro.Acquire(work,arcSlots);work.traces=NavPhysicalMacro.Acquire(work,traces);work.grid=NavPhysicalMacro.Acquire(work,grid);
            work.neighbors=NavPhysicalMacro.Acquire(work,neighbors);work.incoming=NavPhysicalMacro.Acquire(work,incoming);work.traceSlots=NavPhysicalMacro.Acquire(work,traceSlots);work.Graph=NavPhysicalMacro.Acquire(work,Graph);work.borrowedGraph=true;
            using(NavStorageScratch.Reserve(reader.Reservation,checked(16384L+4L*work.locations.Count)))work.ValidatePhysicalCompiler(reader.Reservation);return work;
        }
        private void ValidatePhysicalCompiler(INavStorageReservation reservation)
        {
            int count=locations.Count;if(count>limits.MaxNodes||components.Count!=count||arcSlots.Count>limits.MaxArcs||node<0||node>count||direction<0||direction>3||componentCursor<0||componentCursor>count||(Graph!=null)!=(Phase==Stage.Ready)||(grid==null)!=(Phase==Stage.Ready))throw new ArgumentException("Physical partition compiler capacity/cursor contradiction.");
            if(Phase==Stage.Bounds){if(Consumed!=0||min!=0||max!=0||width!=0||x!=0||z!=0||node!=0||direction!=0||componentCursor!=0||sampled!=0||traceBytes!=0||count!=0||arcSlots.Count!=0||traces.Count!=0||grid.CapacitySlots!=0||neighbors.CapacitySlots!=0||incoming.CapacitySlots!=0||traceSlots.CapacitySlots!=0)throw new ArgumentException("Physical partition Bounds contradiction.");return;}
            double bound=provider.HalfExtent/profile.GridCell;int expectedMin=(int)Math.Ceiling(-bound),expectedMax=(int)Math.Floor(bound);long expectedWidth=(long)expectedMax-expectedMin+1,expectedSamples=checked(expectedWidth*expectedWidth);
            if(min!=expectedMin||max!=expectedMax||width!=expectedWidth||sampled!=expectedSamples||sampled>limits.MaxSamples||width<1||x<min||x>max+1||z<min||z>max||Phase==Stage.Samples&&x>max||Phase>Stage.Samples&&(x!=max+1||z!=min)||Phase<Stage.Arcs&&(node!=0||direction!=0||componentCursor!=0||arcSlots.Count!=0||traces.Count!=0)||Phase>Stage.Arcs&&(node!=count||direction!=0)||Phase<Stage.Components&&componentCursor!=0||Phase==Stage.Ready&&componentCursor!=count)throw new ArgumentException("Physical partition scan/phase prefix contradiction.");
            long processed=Phase==Stage.Samples?(long)(x-min)*width+z-min:sampled;
            long expectedConsumed=Phase==Stage.Samples?1+processed:Phase==Stage.Arcs?checked(1+sampled+4L*node+direction):Phase==Stage.Components?checked(2+sampled+4L*count+componentCursor):checked(sampled+5L*count+3);
            if(Consumed!=unchecked((int)expectedConsumed))throw new ArgumentException("Physical partition consumed prefix contradiction.");
            int locationAt=0;if(grid!=null){if(grid.CapacitySlots<processed)throw new ArgumentException("Missing physical partition grid prefix.");for(int n=0;n<grid.CapacitySlots;n++){
                    int id=grid.Get(n);if(n>=processed){if(id!=0)throw new ArgumentException("Physical partition grid beyond scan prefix.");continue;}if(id==0)continue;
                    if(id!=++locationAt||id>count)throw new ArgumentException("Physical partition grid/sample identity contradiction.");var point=new NavPoint((n/width+min)*profile.GridCell,(n%width+min)*profile.GridCell);if(!locations[id-1].Position.Equals(point))throw new ArgumentException("Physical partition grid coordinate contradiction.");
                }if(locationAt!=count)throw new ArgumentException("Physical partition orphan location.");}
            for(int n=0;n<count;n++){
                var location=locations[n];if(!provider.IsValid(location,clearance)||!NavTypedLeg.SamePhysicalLocation(location,provider.OwnedCheckpointLocation(location)))throw new ArgumentException("Physical partition location selector contradiction.");
                int gx=(int)Math.Round(location.Position.X/profile.GridCell),gz=(int)Math.Round(location.Position.Z/profile.GridCell);long at=(long)(gx-min)*width+gz-min;if(gx<min||gx>max||gz<min||gz>max||at>=processed||location.Position.X!=gx*profile.GridCell||location.Position.Z!=gz*profile.GridCell||n>0&&(locations[n-1].Position.X>location.Position.X||locations[n-1].Position.X==location.Position.X&&locations[n-1].Position.Z>=location.Position.Z))throw new ArgumentException("Physical partition sample order/prefix contradiction.");
            }
            int slots=checked(count*4),capacity=Math.Max(neighbors.CapacitySlots,Math.Max(incoming.CapacitySlots,traceSlots.CapacitySlots));if(neighbors.CapacitySlots<slots||incoming.CapacitySlots<slots||traceSlots.CapacitySlots<slots)throw new ArgumentException("Physical partition directional capacity contradiction.");
            var parents=new int[count];for(int n=0;n<count;n++)parents[n]=n;
            int Root(int n){while(parents[n]!=n){parents[n]=parents[parents[n]];n=parents[n];}return n;}
            long arcPrefix=Phase<Stage.Arcs?0:Phase==Stage.Arcs?4L*node+direction:slots;int arcAt=0,traceAt=0;long traceDiagnostic=0;
            for(int slot=0;slot<capacity;slot++){
                int next=slot<neighbors.CapacitySlots?neighbors.Get(slot):0,back=slot<incoming.CapacitySlots?incoming.Get(slot):0,trace=slot<traceSlots.CapacitySlots?traceSlots.Get(slot):0;
                if(slot>=slots){if(next!=0||back!=0||trace!=0)throw new ArgumentException("Hidden physical partition slot payload.");continue;}
                if(back<0||back>slots||back!=0&&(neighbors.Get(back-1)!=slot/4+1||((back-1)%4^1)!=slot%4))throw new ArgumentException("Physical partition incoming inverse contradiction.");
                if(slot>=arcPrefix&&(next!=0||trace!=0))throw new ArgumentException("Physical partition neighbor beyond processed prefix.");
                if(next==0){if(trace!=0)throw new ArgumentException("Physical partition trace without neighbor.");continue;}
                if(next<1||next>count||arcAt>=arcSlots.Count||arcSlots[arcAt++]!=slot||incoming.Get((next-1)*4+(slot%4^1))!=slot+1)throw new ArgumentException("Physical partition arc stream contradiction.");
                int from=slot/4,to=next-1,d=slot%4;var a=locations[from];var b=locations[to];int gx=(int)Math.Round(a.Position.X/profile.GridCell),gz=(int)Math.Round(a.Position.Z/profile.GridCell);if(!b.Position.Equals(new NavPoint((gx+(d==0?1:d==1?-1:0))*profile.GridCell,(gz+(d==2?1:d==3?-1:0))*profile.GridCell)))throw new ArgumentException("Physical partition cardinal neighbor contradiction.");
                IReadOnlyList<NavTypedLeg> stored=null;if(trace!=0){if(trace!=++traceAt||trace>traces.Count)throw new ArgumentException("Physical partition trace order contradiction.");stored=traces[trace-1];bool crossing=false;foreach(var leg in stored)if(leg.Kind==NavTypedLegKind.Portal)crossing=true;if(!crossing)throw new ArgumentException("Physical partition stored trace without crossing.");traceDiagnostic=checked(traceDiagnostic+128L+96L*stored.Count);}
                provider.VerifyPhysicalTrace(a,b,clearance,stored,reservation);int ra=Root(from),rb=Root(to);if(ra!=rb)parents[Math.Max(ra,rb)]=Math.Min(ra,rb);
            }
            if(arcAt!=arcSlots.Count||traceAt!=traces.Count||traceDiagnostic!=traceBytes)throw new ArgumentException("Physical partition orphan arc/trace diagnostic contradiction.");
            int StoredRoot(int n){while(components[n]!=n){if(components[n]<0||components[n]>=n)throw new ArgumentException("Physical partition component forest contradiction.");n=components[n];}return n;}
            for(int n=0;n<count;n++)if(components[n]<0||components[n]>n||StoredRoot(n)!=Root(n)||n<componentCursor&&components[n]!=Root(n))throw new ArgumentException("Physical partition weak component prefix contradiction.");
            if(Graph!=null){Graph.Binding.RequireAssociation(provider);if(!ReferenceEquals(Graph.Binding.Clearance,clearance)||!ReferenceEquals(Graph.Binding.Profile,profile)||Graph.Nodes.Count!=count||Graph.Arcs.Count!=arcSlots.Count)throw new ArgumentException("Physical partition Ready graph association contradiction.");
                for(int n=0;n<count;n++)if(Graph.Nodes[n].Id!=n||Graph.Nodes[n].Component!=components[n]||!NavTypedLeg.SamePhysicalLocation(Graph.Nodes[n].Location,locations[n]))throw new ArgumentException("Physical partition Ready graph node contradiction.");
                for(int n=0;n<arcSlots.Count;n++){int slot=arcSlots[n],trace=traceSlots.Get(slot);var arc=Graph.Arcs[n];if(arc.From!=slot/4||arc.To!=neighbors.Get(slot)-1||!ReferenceEquals(arc.Trace,trace==0?null:traces[trace-1]))throw new ArgumentException("Physical partition Ready graph arc contradiction.");}
            }
        }
    }
}
