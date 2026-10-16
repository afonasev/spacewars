using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    internal sealed partial class NavPartitionEdges : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>true;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.PartitionEdges);
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(StorageBytes!=NavStorageLayout.Charge(96L+56L*Values.Length))throw new ArgumentException("Physical partition edge layout contradiction.");
            writer.Output.Write(Values.Length);foreach(var edge in Values){NavContract.Point(writer.Output,edge.Item1);NavContract.Point(writer.Output,edge.Item2);}
        }
        internal static NavPartitionEdges ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            int count=reader.Count(int.MaxValue);
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(checked(96L+56L*count));var value=allocation.Register(new NavPartitionEdges(count));
                for(int n=0;n<count;n++)value.Values[n]=Tuple.Create(NavPhysicalGeometryValue.Point(reader),NavPhysicalGeometryValue.Point(reader));return value;
            }
        }
    }
    internal sealed partial class TerrainLocations
    {
        internal static void WriteSeamPhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage,Seam value)
        {
            if(storage.StorageBytes!=NavStorageLayout.Charge(256+NavProviderCopy.TextBytes(value.A)+NavProviderCopy.TextBytes(value.B))||storage.First!=null||storage.Second!=null||storage.Third!=null||storage.Fourth!=null||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null)throw new ArgumentException("Physical private seam ownership contradiction.");
            writer.Output.Write(value.A!=null);if(value.A!=null)NavPhysicalTextPlan.WritePlan(writer,value.A);
            writer.Output.Write(value.B!=null);if(value.B!=null)NavPhysicalTextPlan.WritePlan(writer,value.B);
            if(value.A!=null)NavPhysicalTextPlan.WriteData(writer,value.A);if(value.B!=null)NavPhysicalTextPlan.WriteData(writer,value.B);
            NavContract.Point(writer.Output,value.From);NavContract.Point(writer.Output,value.To);writer.Output.Write(value.Allowed);
        }
        internal static NavDeclarationStorage ReadSeamPhysicalBody(NavPhysicalBodyReader reader)
        {
            bool hasA=NavPhysicalValue<bool>.Read(reader);var aPlan=hasA?NavPhysicalTextPlan.Read(reader):default;
            bool hasB=NavPhysicalValue<bool>.Read(reader);var bPlan=hasB?NavPhysicalTextPlan.Read(reader):default;
            long bytes=256+(hasA?aPlan.Bytes:0)+(hasB?bPlan.Bytes:0);
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);string a=hasA?aPlan.ReadData(reader):null,b=hasB?bPlan.ReadData(reader):null;
                var from=NavPhysicalGeometryValue.Point(reader);var to=NavPhysicalGeometryValue.Point(reader);bool allowed=NavPhysicalValue<bool>.Read(reader);
                if(a==b||from.Equals(to)||(a==null||b==null)&&allowed)throw new ArgumentException("Invalid physical private seam incidence.");
                var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.Facade=new Seam{Storage=storage,A=a,B=b,From=from,To=to,Allowed=allowed};return storage;
            }
        }
        internal void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            if(storage.StorageBytes!=NavStorageLayout.Charge(512)||storage.First!=geometry.StorageRoot||!(storage.Second is NavRetainedArray<MapSupport> supports)||!ReferenceEquals(supports.ReadOnlyBacking,regions)||!(storage.Third is NavPartitionEdges sourceEdges)||!ReferenceEquals(sourceEdges.Values,edges)||storage.Fourth!=byId||storage.Fifth!=seams||storage.Sixth!=exterior||storage.Seventh!=Transitions||storage.Eighth!=Seams)throw new ArgumentException("Physical TerrainLocations ownership contradiction.");
            writer.Edge(storage.First);writer.Edge(storage.Second);writer.Edge(storage.Third);writer.Edge(storage.Fourth);writer.Edge(storage.Fifth);writer.Edge(storage.Sixth);writer.Edge(storage.Seventh);writer.Edge(storage.Eighth);
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(512);var geometry=reader.Edge<NavGeometry.OwnedBuffers>();var supports=reader.Edge<NavRetainedArray<MapSupport>>();var edges=reader.Edge<NavPartitionEdges>();var index=reader.Edge<NavCowDictionary<NavNameKey,int>>();var seams=reader.Edge<NavRetainedArray<Seam>>();var exterior=reader.Edge<NavRetainedArray<Seam>>();var transitions=reader.Edge<NavRetainedArray<NavSurfaceTransition>>();var publicSeams=reader.Edge<NavRetainedArray<NavPartitionSeam>>();
                if(geometry?.Facade==null||supports==null||edges==null||index==null||seams==null||exterior==null||transitions==null||publicSeams==null)throw new ArgumentException("Missing physical TerrainLocations edge.");
                var storage=allocation.Register(new NavDeclarationStorage(512));
                void Own(NavOwnedStorage child){storage.RequireEdge(child);child.Retain();}
                Own(geometry);storage.First=geometry;Own(supports);storage.Second=supports;Own(edges);storage.Third=edges;Own(index);storage.Fourth=index;Own(seams);storage.Fifth=seams;Own(exterior);storage.Sixth=exterior;Own(transitions);storage.Seventh=transitions;Own(publicSeams);storage.Eighth=publicSeams;
                var value=new TerrainLocations(geometry.Facade){owned=storage,regions=supports.ReadOnlyBacking,edges=edges.Values,byId=index,seams=seams,exterior=exterior,Transitions=transitions,Seams=publicSeams};storage.Facade=value;
                value.ValidatePhysicalArrangement(reader.Reservation);return storage;
            }
        }
        // Replays only the existing boundary predicates for verification. No
        // arrangement, seam facade, index or replacement payload is constructed.
        internal void ValidatePhysicalArrangement(INavStorageReservation reservation)
        {
            long cuts=checked(2L*edges.Length+2);long rounded=4;while(rounded<cuts)rounded=checked(rounded*2);
            long scratch=checked(4096+2*(16*rounded+64*(3*cuts+4)+32L*edges.Length+256)+1024L*regions.Length);
            using(NavStorageScratch.Reserve(reservation,scratch)){
                if(byId.Count!=regions.Length||Seams.Count!=seams.Count)throw new ArgumentException("Physical partition index/seam count contradiction.");
                for(int n=0;n<regions.Length;n++){
                    var support=regions[n];if(support==null||support.SurfaceId==NavLocation.FlatSurface||!byId.TryGetValue(new NavNameKey(support.SurfaceId),out int at)||at!=n)throw new ArgumentException("Physical partition support index contradiction.");
                    for(int j=0;j<n;j++)if(regions[j].SurfaceId==support.SurfaceId)throw new ArgumentException("Repeated physical support key.");
                }
                foreach(var pair in byId)if(pair.Value<0||pair.Value>=regions.Length||pair.Key.Reference.Role!=NavNameRole.SupportSurface||pair.Key.Reference.Owner!=regions[pair.Value].StorageRoot||!ReferenceEquals(pair.Key.Value,regions[pair.Value].SurfaceId))throw new ArgumentException("Physical partition key owner contradiction.");
                for(int n=0;n<Transitions.Count;n++){
                    var transition=Transitions[n];if(transition==null||!byId.ContainsKey(new NavNameKey(transition.From))||!byId.ContainsKey(new NavNameKey(transition.To)))throw new ArgumentException("Unknown physical transition endpoint.");
                    for(int j=0;j<n;j++)if(Transitions[j].Id==transition.Id)throw new ArgumentException("Repeated physical transition ID.");
                }
                int edgeAt=0;foreach(var support in regions){
                    var vertices=support.Bounds.Footprint.Vertices;
                    for(int n=0;n<vertices.Count;n++)if(edgeAt>=edges.Length||!edges[edgeAt].Item1.Equals(vertices[n])||!edges[edgeAt++].Item2.Equals(vertices[(n+1)%vertices.Count]))throw new ArgumentException("Physical source edge stream contradiction.");
                }
                if(edgeAt!=edges.Length)throw new ArgumentException("Extra physical source edge.");
                int insideAt=0,outsideAt=0;
                foreach(var edge in edges){
                    var positions=Cuts(edge.Item1,edge.Item2,edges);
                    for(int n=1;n<positions.Count;n++){
                        if(positions[n]-positions[n-1]<1e-10)continue;
                        var a=Lerp(edge.Item1,edge.Item2,positions[n-1]);var b=Lerp(edge.Item1,edge.Item2,positions[n]);var mid=Lerp(a,b,.5);
                        double dx=b.X-a.X,dz=b.Z-a.Z,len=Math.Sqrt(dx*dx+dz*dz),epsilon=Math.Min(1e-7,len/8);
                        var left=Owner(new NavPoint(mid.X-dz/len*epsilon,mid.Z+dx/len*epsilon));var right=Owner(new NavPoint(mid.X+dz/len*epsilon,mid.Z-dx/len*epsilon));if(left==right)continue;
                        bool outside=left==null||right==null;bool allowed=false;
                        if(!outside){
                            bool declared=false;foreach(var t in Transitions)if(t.From==left.SurfaceId&&t.To==right.SurfaceId||t.From==right.SurfaceId&&t.To==left.SurfaceId){declared=true;break;}
                            allowed=declared&&Math.Abs(left.HeightAt(a)-right.HeightAt(a))<=1e-7&&Math.Abs(left.HeightAt(b)-right.HeightAt(b))<=1e-7;
                        }
                        var sequence=outside?exterior:seams;int at=outside?outsideAt++:insideAt++;
                        if(at>=sequence.Count)throw new ArgumentException("Missing physical partition boundary coverage.");var saved=sequence[at];
                        if(saved==null||saved.A!=left?.SurfaceId||saved.B!=right?.SurfaceId||!saved.From.Equals(a)||!saved.To.Equals(b)||saved.Allowed!=allowed)throw new ArgumentException("Physical partition boundary incidence/coverage contradiction.");
                        if(!outside){var published=Seams[at];if(published==null||published.FromSurface!=saved.A||published.ToSurface!=saved.B||!published.From.Equals(a)||!published.To.Equals(b)||published.Allowed!=allowed)throw new ArgumentException("Physical public/private seam contradiction.");}
                    }
                }
                if(insideAt!=seams.Count||outsideAt!=exterior.Count)throw new ArgumentException("Extra physical partition boundary coverage.");
            }
        }
    }
    public sealed partial class PartitionNavigationProvider
    {
        private PartitionNavigationProvider(string id,int revision,int semantics,string hold,double extent,string binding,TerrainLocations locations,NavRetainedArray<NavLayerSurface> surfaces,NavCowDictionary<string,NavLayerSurface> index,NavRetainedArray<NavLayerPortal> portals,long estimate)
        {terrainId=id;terrainRevision=revision;surfaceSemanticsVersion=semantics;holdBinding=hold;HalfExtent=extent;Binding=binding;this.locations=locations;Surfaces=surfaces;bySurface=index;Portals=portals;Seams=locations.Seams;EstimatedRetainedBytes=estimate;}
        internal void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            long bytes=LayoutBase+NavProviderCopy.TextBytes(terrainId)+NavProviderCopy.TextBytes(holdBinding)+NavProviderCopy.TextBytes(Binding);
            if(storage.StorageBytes!=NavStorageLayout.Charge(bytes)||storage.First!=locations.StorageRoot||storage.Second!=Surfaces||storage.Third!=bySurface||storage.Fourth!=Portals||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null||!ReferenceEquals(Seams,locations.Seams))throw new ArgumentException("Physical Partition provider ownership contradiction.");
            NavPhysicalTextPlan.WritePlan(writer,terrainId);writer.Output.Write(holdBinding!=null);if(holdBinding!=null)writer.Output.Write(holdBinding.Length);NavPhysicalTextPlan.WritePlan(writer,Binding);
            NavPhysicalTextPlan.WriteData(writer,terrainId);if(holdBinding!=null)NavPhysicalTextPlan.WriteData(writer,holdBinding);NavPhysicalTextPlan.WriteData(writer,Binding);
            writer.Output.Write(terrainRevision);writer.Output.Write(surfaceSemanticsVersion);writer.Output.Write(HalfExtent);writer.Edge(storage.First);writer.Edge(storage.Second);writer.Edge(storage.Third);writer.Edge(storage.Fourth);
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var idPlan=NavPhysicalTextPlan.Read(reader);bool hasHold=NavPhysicalValue<bool>.Read(reader);var holdPlan=hasHold?NavPhysicalTextPlan.Read(reader):default;var bindingPlan=NavPhysicalTextPlan.Read(reader);long bytes=LayoutBase+idPlan.Bytes+(hasHold?holdPlan.Bytes:0)+bindingPlan.Bytes;
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);string id=idPlan.ReadData(reader),hold=hasHold?holdPlan.ReadRawData(reader):null,binding=NavContract.Sha256(bindingPlan.ReadData(reader));int revision=NavContract.Revision(reader.Input.ReadInt32()),semantics=NavContract.Revision(reader.Input.ReadInt32());double extent=NavPhysicalGeometryValue.Number(reader);
                var locationsRoot=reader.Edge<NavDeclarationStorage>();var surfaces=reader.Edge<NavRetainedArray<NavLayerSurface>>();var index=reader.Edge<NavCowDictionary<string,NavLayerSurface>>();var portals=reader.Edge<NavRetainedArray<NavLayerPortal>>();var locations=locationsRoot?.Facade as TerrainLocations;
                if(locations==null||surfaces==null||index==null||portals==null||extent!=locations.Geometry.HalfExtent||surfaces.Count!=locations.Supports.Count||index.Count!=surfaces.Count||portals.Count!=0||locations.Geometry.Obstacles.Count>2048)throw new ArgumentException("Physical Partition declaration/extent contradiction.");
                long supportVertices=0,polygonVertices=0;
                using(NavStorageScratch.Reserve(reader.Reservation,8192)){
                    for(int n=0;n<surfaces.Count;n++){
                        var s=surfaces[n];var support=locations.Supports[n];var footprint=support.Bounds.Footprint;
                        if(s==null||s.Id!=support.SurfaceId||s.Height!=support.Height||s.CombatFamily!="ground"||s.DistanceCost!=1||s.Area.HalfExtent!=extent||s.Area.Blockers.Count!=0||s.Area.Boundary.Vertices.Count!=footprint.Vertices.Count||!index.TryGetValue(s.Id,out var indexed)||!ReferenceEquals(s,indexed))throw new ArgumentException("Physical Partition surface adapter contradiction.");
                        for(int j=0;j<footprint.Vertices.Count;j++)if(!s.Area.Boundary.Vertices[j].Equals(footprint.Vertices[j]))throw new ArgumentException("Physical Partition footprint contradiction.");
                        supportVertices=checked(supportVertices+(support.Bounds.Polygon?.Vertices.Count??4));
                    }
                    foreach(var obstacle in locations.Geometry.Obstacles){if(obstacle.Polygon!=null&&obstacle.Polygon.Vertices.Count>256)throw new ArgumentException("Physical Partition movement geometry capacity exceeded.");polygonVertices=checked(polygonVertices+(obstacle.Polygon?.Vertices.Count??0));}
                }
                long estimate=RetainedPayloadEstimate(surfaces.Count,supportVertices,locations.Seams.Count,locations.Transitions.Count,locations.Geometry.Obstacles.Count,polygonVertices);
                var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.RequireEdge(locationsRoot);storage.RequireEdge(surfaces);storage.RequireEdge(index);storage.RequireEdge(portals);
                locationsRoot.Retain();storage.First=locationsRoot;surfaces.Retain();storage.Second=surfaces;index.Retain();storage.Third=index;portals.Retain();storage.Fourth=portals;
                var value=new PartitionNavigationProvider(id,revision,semantics,hold,extent,binding,locations,surfaces,index,portals,estimate){owned=storage};storage.Facade=value;
                using(NavStorageScratch.Reserve(reader.Reservation,8192))NavPhysicalDigestStream.Require(value.WriteBindingTranscript,binding);return storage;
            }
        }
    }
}
