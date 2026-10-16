using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Spacewars.Simulation
{
    // Same BinaryWriter transcript and SHA256 as NavContract.Digest, with no
    // MemoryStream/ToArray copy proportional to repeated declaration geometry.
    internal sealed class NavPhysicalDigestStream : Stream
    {
        private readonly SHA256 hash=SHA256.Create();
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush(){}
        public override void Write(byte[] buffer,int offset,int count)=>hash.TransformBlock(buffer,offset,count,buffer,offset);
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
        internal static void Require(Action<BinaryWriter> transcript,string expected)
        {
            using(var stream=new NavPhysicalDigestStream()){
                using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)){transcript(writer);writer.Flush();}
                stream.hash.TransformFinalBlock(Array.Empty<byte>(),0,0);string actual=BitConverter.ToString(stream.hash.Hash).Replace("-","").ToLowerInvariant();
                if(actual!=expected)throw new ArgumentException("Physical provider binding mismatch.");
            }
        }
        protected override void Dispose(bool disposing){if(disposing)hash.Dispose();base.Dispose(disposing);}
    }
    public sealed partial class LayeredNavigationProvider
    {
        internal static void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage,LayeredNavigationProvider provider)
        {
            long bytes=512+NavProviderCopy.TextBytes(provider.Id)+NavProviderCopy.TextBytes(provider.ExactMapBinding)+NavProviderCopy.TextBytes(provider.Binding);
            if(storage.StorageBytes!=NavStorageLayout.Charge(bytes)||storage.First!=provider.ownedSurfaces||storage.Second!=provider.ownedPortals||storage.Third!=provider.ownedSurfaceIndex||storage.Fourth!=provider.ownedPortalIndex||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null)throw new ArgumentException("Physical provider ownership contradiction.");
            NavPhysicalTextPlan.WritePlan(writer,provider.Id);NavPhysicalTextPlan.WritePlan(writer,provider.ExactMapBinding);NavPhysicalTextPlan.WritePlan(writer,provider.Binding);
            NavPhysicalTextPlan.WriteData(writer,provider.Id);NavPhysicalTextPlan.WriteData(writer,provider.ExactMapBinding);NavPhysicalTextPlan.WriteData(writer,provider.Binding);
            writer.Output.Write(provider.Revision);writer.Output.Write(provider.SemanticsVersion);writer.Output.Write(provider.TopologyRevision);writer.Edge(storage.First);writer.Edge(storage.Second);writer.Edge(storage.Third);writer.Edge(storage.Fourth);
        }
        private LayeredNavigationProvider(string id,int revision,int semantics,int topology,string exact,string binding,NavRetainedArray<NavLayerSurface> layers,NavRetainedArray<NavLayerPortal> links,NavCowDictionary<string,NavLayerSurface> surfaceIndex,NavCowDictionary<string,NavLayerPortal> portalIndex,long estimate)
        {Id=id;Revision=revision;SemanticsVersion=semantics;TopologyRevision=topology;ExactMapBinding=exact;Binding=binding;ownedSurfaces=layers;ownedPortals=links;ownedSurfaceIndex=surfaceIndex;ownedPortalIndex=portalIndex;Surfaces=layers;Portals=links;surfaces=surfaceIndex;portals=portalIndex;EstimatedRetainedBytes=estimate;}
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var idPlan=NavPhysicalTextPlan.Read(reader);var exactPlan=NavPhysicalTextPlan.Read(reader);var bindingPlan=NavPhysicalTextPlan.Read(reader);long bytes=512+idPlan.Bytes+exactPlan.Bytes+bindingPlan.Bytes;
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);string id=idPlan.ReadData(reader),exact=NavContract.Sha256(exactPlan.ReadData(reader)),binding=NavContract.Sha256(bindingPlan.ReadData(reader));
                int revision=NavContract.Revision(reader.Input.ReadInt32()),semantics=NavContract.Revision(reader.Input.ReadInt32()),topology=NavContract.Revision(reader.Input.ReadInt32());
                var layers=reader.Edge<NavRetainedArray<NavLayerSurface>>();var links=reader.Edge<NavRetainedArray<NavLayerPortal>>();var surfaceIndex=reader.Edge<NavCowDictionary<string,NavLayerSurface>>();var portalIndex=reader.Edge<NavCowDictionary<string,NavLayerPortal>>();
                if(layers==null||links==null||surfaceIndex==null||portalIndex==null||layers.Count<1||layers.Count>128||links.Count>128||surfaceIndex.Count!=layers.Count||portalIndex.Count!=links.Count)throw new ArgumentException("Physical provider declaration/index capacity contradiction.");
                long estimate=4096;
                for(int n=0;n<layers.Count;n++){
                    var surface=layers[n];if(surface==null||n>0&&StringComparer.Ordinal.Compare(layers[n-1].Id,surface.Id)>=0||!surfaceIndex.TryGetValue(surface.Id,out var indexed)||!ReferenceEquals(indexed,surface))throw new ArgumentException("Physical provider surface order/alias contradiction.");
                    estimate=checked(estimate+1024+surface.Area.EstimatedRetainedBytes);
                }
                for(int n=0;n<links.Count;n++){
                    var portal=links[n];if(portal==null||n>0&&StringComparer.Ordinal.Compare(links[n-1].Id,portal.Id)>=0||!portalIndex.TryGetValue(portal.Id,out var indexed)||!ReferenceEquals(indexed,portal)||!surfaceIndex.TryGetValue(portal.Entry.SurfaceId,out var entry)||!surfaceIndex.TryGetValue(portal.Exit.SurfaceId,out var exit)||!entry.Area.Contains(portal.Entry.Position,0)||!exit.Area.Contains(portal.Exit.Position,0)||!portal.Corridor.Sweep(portal.Entry.Position,portal.Exit.Position,0))throw new ArgumentException("Physical provider portal geometry/order/alias contradiction.");
                    estimate=checked(estimate+2048+512L*portal.MobilityClasses.Count+portal.Corridor.EstimatedRetainedBytes);
                }
                using(var scratch=NavStorageScratch.Reserve(reader.Reservation,8192))NavPhysicalDigestStream.Require(w=>{
                    w.Write("layered-provider-v1");w.Write(id);w.Write(revision);w.Write(semantics);w.Write(topology);w.Write(exact);w.Write(layers.Count);foreach(var surface in layers)surface.Write(w);w.Write(links.Count);foreach(var portal in links)portal.Write(w);
                },binding);
                var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.RequireEdge(layers);storage.RequireEdge(links);storage.RequireEdge(surfaceIndex);storage.RequireEdge(portalIndex);
                layers.Retain();storage.First=layers;links.Retain();storage.Second=links;surfaceIndex.Retain();storage.Third=surfaceIndex;portalIndex.Retain();storage.Fourth=portalIndex;
                storage.Facade=new LayeredNavigationProvider(id,revision,semantics,topology,exact,binding,layers,links,surfaceIndex,portalIndex,estimate){owned=storage};return storage;
            }
        }
    }
}
