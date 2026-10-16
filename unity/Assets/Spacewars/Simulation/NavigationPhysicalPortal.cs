using System;

namespace Spacewars.Simulation
{
    public sealed partial class NavLayerPortal
    {
        internal static void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage,NavLayerPortal portal)
        {
            long bytes=256+NavProviderCopy.TextBytes(portal.Id)+NavProviderCopy.TextBytes(portal.Entry.SurfaceId)+NavProviderCopy.TextBytes(portal.Exit.SurfaceId);
            foreach(var text in portal.MobilityClasses)bytes=checked(bytes+NavProviderCopy.TextBytes(text));
            if(storage.StorageBytes!=NavStorageLayout.Charge(bytes)||storage.First!=portal.Corridor.StorageRoot||storage.Second!=portal.ownedMobility||portal.ownedMobility==null||storage.Third!=null||storage.Fourth!=null||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null)throw new ArgumentException("Physical portal ownership contradiction.");
            NavPhysicalTextPlan.WritePlan(writer,portal.Id);NavPhysicalTextPlan.WritePlan(writer,portal.Entry.SurfaceId);NavPhysicalTextPlan.WritePlan(writer,portal.Exit.SurfaceId);
            writer.Output.Write(portal.MobilityClasses.Count);foreach(var text in portal.MobilityClasses)NavPhysicalTextPlan.WritePlan(writer,text);
            NavPhysicalTextPlan.WriteData(writer,portal.Id);NavPhysicalTextPlan.WriteData(writer,portal.Entry.SurfaceId);NavPhysicalTextPlan.WriteData(writer,portal.Exit.SurfaceId);
            writer.Output.Write(portal.Entry.Position.X);writer.Output.Write(portal.Entry.Position.Z);writer.Output.Write(portal.Exit.Position.X);writer.Output.Write(portal.Exit.Position.Z);writer.Output.Write((int)portal.Direction);writer.Output.Write(portal.Open);writer.Output.Write(portal.TraversalCost);writer.Edge(storage.First);
            var oldTexts=writer.EmbeddedTexts;int oldCursor=writer.EmbeddedCursor;writer.EmbeddedTexts=portal.MobilityClasses;writer.EmbeddedCursor=0;
            try{writer.Edge(storage.Second);if(writer.EmbeddedCursor!=portal.MobilityClasses.Count)throw new ArgumentException("Physical portal strings require their unique array definition.");}
            finally{writer.EmbeddedTexts=oldTexts;writer.EmbeddedCursor=oldCursor;}
        }
        private NavLayerPortal(string id,NavLocation entry,NavLocation exit,NavPortalDirection direction,bool open,double cost,NavTraversalArea corridor,NavRetainedArray<string> mobility,bool physical)
        {Id=id;Entry=entry;Exit=exit;Direction=direction;Open=open;TraversalCost=cost;Corridor=corridor;ownedMobility=mobility;MobilityClasses=mobility;}
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            if(!reader.Input.BaseStream.CanSeek)throw new ArgumentException("Physical portal descriptors need isolated seekable input.");
            var idPlan=NavPhysicalTextPlan.Read(reader);var entryPlan=NavPhysicalTextPlan.Read(reader);var exitPlan=NavPhysicalTextPlan.Read(reader);int count=reader.Count(int.MaxValue);
            if(count==0)throw new ArgumentException("Missing portal mobility metadata.");long descriptor=reader.Input.BaseStream.Position;
            long bytes=256+idPlan.Bytes+entryPlan.Bytes+exitPlan.Bytes;for(int n=0;n<count;n++)bytes=checked(bytes+NavPhysicalTextPlan.Read(reader).Bytes);
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);string id=idPlan.ReadData(reader),entryId=entryPlan.ReadData(reader),exitId=exitPlan.ReadData(reader);
                var entry=new NavLocation(NavPhysicalGeometryValue.Point(reader),entryId);var exit=new NavLocation(NavPhysicalGeometryValue.Point(reader),exitId);var direction=(NavPortalDirection)reader.Input.ReadInt32();byte open=reader.Input.ReadByte();double cost=NavPhysicalGeometryValue.Number(reader);
                if(entryId==exitId||!Enum.IsDefined(typeof(NavPortalDirection),direction)||open>1||cost<=0)throw new ArgumentException("Invalid physical portal semantics.");
                var area=reader.Edge<NavDeclarationStorage>();if(!(area?.Facade is NavTraversalArea corridor))throw new ArgumentException("Wrong physical portal area.");
                long oldPosition=reader.EmbeddedLengthPosition;int oldCount=reader.EmbeddedCount,oldCursor=reader.EmbeddedCursor;reader.EmbeddedLengthPosition=descriptor;reader.EmbeddedCount=count;reader.EmbeddedCursor=0;NavRetainedArray<string> mobility;
                try{mobility=reader.Edge<NavRetainedArray<string>>();if(mobility==null||mobility.Count!=count||reader.EmbeddedCursor!=count)throw new ArgumentException("Physical portal mobility shape/definition contradiction.");}
                finally{reader.EmbeddedLengthPosition=oldPosition;reader.EmbeddedCount=oldCount;reader.EmbeddedCursor=oldCursor;}
                for(int n=1;n<count;n++)if(StringComparer.Ordinal.Compare(mobility[n-1],mobility[n])>=0)throw new ArgumentException("Physical portal mobility ordering contradiction.");
                var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.RequireEdge(area);storage.RequireEdge(mobility);area.Retain();storage.First=area;mobility.Retain();storage.Second=mobility;
                storage.Facade=new NavLayerPortal(id,entry,exit,direction,open==1,cost,corridor,mobility,true){owned=storage};((NavLayerPortal)storage.Facade).BindOwnedLocations();return storage;
            }
        }
    }
}
