using System;

namespace Spacewars.Simulation
{
    public sealed partial class MapSupport
    {
        internal void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            if(storage.StorageBytes!=NavStorageLayout.Charge(384+NavProviderCopy.TextBytes(Id)+NavProviderCopy.TextBytes(SurfaceId))||storage.First!=Bounds.Polygon?.StorageRoot||storage.Second!=null||storage.Third!=null||storage.Fourth!=null||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null)throw new ArgumentException("Physical support ownership contradiction.");
            NavPhysicalTextPlan.WritePlan(writer,Id);NavPhysicalTextPlan.WritePlan(writer,SurfaceId);NavPhysicalTextPlan.WriteData(writer,Id);NavPhysicalTextPlan.WriteData(writer,SurfaceId);NavPhysicalGeometryValue.Write(writer,Bounds);writer.Output.Write(IsBridge);writer.Output.Write(Height);NavContract.Point(writer.Output,Gradient);NavContract.Point(writer.Output,Origin);
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var idPlan=NavPhysicalTextPlan.Read(reader);var surfacePlan=NavPhysicalTextPlan.Read(reader);long bytes=384+idPlan.Bytes+surfacePlan.Bytes;
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);string id=idPlan.ReadData(reader),surface=surfacePlan.ReadData(reader);var bounds=NavPhysicalGeometryValue.Read(reader);bool bridge=NavPhysicalValue<bool>.Read(reader);double height=NavPhysicalGeometryValue.Number(reader);var gradient=NavPhysicalGeometryValue.Point(reader);var origin=NavPhysicalGeometryValue.Point(reader);
                var storage=allocation.Register(new NavDeclarationStorage(bytes));if(bounds.Polygon!=null){storage.RequireEdge(bounds.Polygon.StorageRoot);bounds.Polygon.StorageRoot.Retain();storage.First=bounds.Polygon.StorageRoot;}
                storage.Facade=new MapSupport(id,bounds,bridge,height,gradient,origin,surface){owned=storage};return storage;
            }
        }
    }
    public sealed partial class NavSurfaceTransition
    {
        internal void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            if(storage.StorageBytes!=NavStorageLayout.Charge(192+NavProviderCopy.TextBytes(Id)+NavProviderCopy.TextBytes(From)+NavProviderCopy.TextBytes(To))||storage.First!=null||storage.Second!=null||storage.Third!=null||storage.Fourth!=null||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null)throw new ArgumentException("Physical transition ownership contradiction.");
            NavPhysicalTextPlan.WritePlan(writer,Id);NavPhysicalTextPlan.WritePlan(writer,From);NavPhysicalTextPlan.WritePlan(writer,To);NavPhysicalTextPlan.WriteData(writer,Id);NavPhysicalTextPlan.WriteData(writer,From);NavPhysicalTextPlan.WriteData(writer,To);
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var idPlan=NavPhysicalTextPlan.Read(reader);var fromPlan=NavPhysicalTextPlan.Read(reader);var toPlan=NavPhysicalTextPlan.Read(reader);long bytes=192+idPlan.Bytes+fromPlan.Bytes+toPlan.Bytes;
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);string id=idPlan.ReadData(reader),from=fromPlan.ReadData(reader),to=toPlan.ReadData(reader);var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.Facade=new NavSurfaceTransition(id,from,to){owned=storage};return storage;
            }
        }
    }
    public sealed partial class NavPartitionSeam
    {
        private NavPartitionSeam(string id,string fromSurface,string toSurface,NavPoint from,NavPoint to,bool allowed)
        {Id=id;FromSurface=fromSurface;ToSurface=toSurface;From=from;To=to;Allowed=allowed;}
        internal void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            if(storage.StorageBytes!=NavStorageLayout.Charge(256+NavProviderCopy.TextBytes(Id)+NavProviderCopy.TextBytes(FromSurface)+NavProviderCopy.TextBytes(ToSurface))||storage.First!=null||storage.Second!=null||storage.Third!=null||storage.Fourth!=null||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null)throw new ArgumentException("Physical seam ownership contradiction.");
            NavPhysicalTextPlan.WritePlan(writer,Id);NavPhysicalTextPlan.WritePlan(writer,FromSurface);NavPhysicalTextPlan.WritePlan(writer,ToSurface);NavPhysicalTextPlan.WriteData(writer,Id);NavPhysicalTextPlan.WriteData(writer,FromSurface);NavPhysicalTextPlan.WriteData(writer,ToSurface);NavContract.Point(writer.Output,From);NavContract.Point(writer.Output,To);writer.Output.Write(Allowed);
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var idPlan=NavPhysicalTextPlan.Read(reader);var fromPlan=NavPhysicalTextPlan.Read(reader);var toPlan=NavPhysicalTextPlan.Read(reader);long bytes=256+idPlan.Bytes+fromPlan.Bytes+toPlan.Bytes;
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);string id=NavContract.Sha256(idPlan.ReadData(reader)),fromSurface=fromPlan.ReadData(reader),toSurface=toPlan.ReadData(reader);var from=NavPhysicalGeometryValue.Point(reader);var to=NavPhysicalGeometryValue.Point(reader);bool allowed=NavPhysicalValue<bool>.Read(reader);
                using(var scratch=NavStorageScratch.Reserve(reader.Reservation,8192))NavPhysicalDigestStream.Require(w=>{w.Write("partition-seam-v1");w.Write(fromSurface);w.Write(toSurface);NavContract.Point(w,from);NavContract.Point(w,to);w.Write(allowed);},id);
                var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.Facade=new NavPartitionSeam(id,fromSurface,toSurface,from,to,allowed){owned=storage};return storage;
            }
        }
    }
}
