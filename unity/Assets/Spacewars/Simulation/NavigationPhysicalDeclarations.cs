using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    internal static class NavPhysicalMapBinding<TKey,TValue>
    {
        internal static IComparer<TKey> Comparer=>typeof(TKey)==typeof(NavGraphArc)?(IComparer<TKey>)(object)NavGraphArcComparer.Instance:typeof(TKey)==typeof(string)?(IComparer<TKey>)(object)StringComparer.Ordinal:System.Collections.Generic.Comparer<TKey>.Default;
        private static bool PortalKey=>typeof(TKey)==typeof(string)&&typeof(TValue)==typeof(NavLayerPortal);
        private static bool SurfaceKey=>typeof(TKey)==typeof(string)&&typeof(TValue)==typeof(NavLayerSurface);
        internal static void Write(NavPhysicalBodyWriter writer,TKey key,TValue value)
        {
            if(SurfaceKey){
                var surface=(NavLayerSurface)(object)value;
                if(surface==null||!ReferenceEquals((string)(object)key,surface.Id))throw new ArgumentException("Physical surface-index key must use its value's canonical ID.");
                NavPhysicalValue<TValue>.Write(writer,value);return;
            }
            if(PortalKey){var portal=(NavLayerPortal)(object)value;if(portal==null||!ReferenceEquals((string)(object)key,portal.Id))throw new ArgumentException("Physical portal-index key must use its value's canonical ID.");NavPhysicalValue<TValue>.Write(writer,value);return;}
            NavPhysicalValue<TKey>.Write(writer,key);NavPhysicalValue<TValue>.Write(writer,value);
        }
        internal static void Read(NavPhysicalBodyReader reader,out TKey key,out TValue value)
        {
            if(SurfaceKey){
                value=NavPhysicalValue<TValue>.Read(reader);var surface=(NavLayerSurface)(object)value;if(surface==null)throw new ArgumentException("Missing physical surface-index value.");key=(TKey)(object)surface.Id;return;
            }
            if(PortalKey){value=NavPhysicalValue<TValue>.Read(reader);var portal=(NavLayerPortal)(object)value;if(portal==null)throw new ArgumentException("Missing physical portal-index value.");key=(TKey)(object)portal.Id;return;}
            key=NavPhysicalValue<TKey>.Read(reader);value=NavPhysicalValue<TValue>.Read(reader);
        }
    }
    internal readonly struct NavPhysicalTextPlan
    {
        internal readonly int Length;
        internal long Bytes=>32L+2L*Length;
        private NavPhysicalTextPlan(int length){Length=length;}
        internal static NavPhysicalTextPlan Read(NavPhysicalBodyReader reader)=>new NavPhysicalTextPlan(reader.Count(256));
        internal static void WritePlan(NavPhysicalBodyWriter writer,string text){NavContract.Text(text);writer.Output.Write(text.Length);}
        internal static void WriteData(NavPhysicalBodyWriter writer,string text){for(int n=0;n<text.Length;n++)writer.Output.Write((ushort)text[n]);}
        // The containing declaration reserves its recomputed full layout from
        // all length descriptors BEFORE any string, facade or child allocation.
        internal string ReadData(NavPhysicalBodyReader reader)=>ReadData(reader,true);
        internal string ReadRawData(NavPhysicalBodyReader reader)=>ReadData(reader,false);
        private string ReadData(NavPhysicalBodyReader reader,bool requireText)
        {
            using(var scratch=NavStorageScratch.Reserve(reader.Reservation,64L+2L*Length)){
                var chars=new char[Length];for(int n=0;n<Length;n++)chars[n]=(char)reader.Input.ReadUInt16();var value=new string(chars);return requireText?NavContract.Text(value):value;
            }
        }
    }
    internal sealed partial class NavDeclarationStorage : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>Facade is NavArrivalReachabilityProof||Facade is NavArrivalProposal||Facade is NavArrivalInput||Facade is NavArrivalOccupancySnapshot||Facade is NavArrivalCandidateSource||Facade is NavArrivalOccupant||Facade is NavigationProfile||Facade is NavArrivalWorkingBinding||Facade is NavArrivalMember||Facade is NavTraversalArea||Facade is NavLayerSurface||Facade is NavLayerPortal||Facade is LayeredNavigationProvider||Facade is NavGraphBinding||Facade is MapSupport||Facade is NavSurfaceTransition||Facade is NavPartitionSeam||Facade is NavTerminalRegion||Facade is NavScheduledRequest||Facade is TerrainLocations||Facade is TerrainLocations.Seam||Facade is PartitionNavigationProvider||Facade is NavRouteSubscription;
        public NavPhysicalTag PhysicalTag {
            get{
                if(Facade is NavArrivalReachabilityProof)return new NavPhysicalTag(NavPhysicalStorageKind.ArrivalProof);
                if(Facade is NavArrivalProposal)return new NavPhysicalTag(NavPhysicalStorageKind.ArrivalProposal);
                if(Facade is NavArrivalInput)return new NavPhysicalTag(NavPhysicalStorageKind.ArrivalInput);
                if(Facade is NavArrivalOccupancySnapshot)return new NavPhysicalTag(NavPhysicalStorageKind.ArrivalOccupancy);
                if(Facade is NavArrivalCandidateSource)return new NavPhysicalTag(NavPhysicalStorageKind.ArrivalSource);
                if(Facade is NavArrivalOccupant)return new NavPhysicalTag(NavPhysicalStorageKind.ArrivalOccupant);
                if(Facade is NavigationProfile profile)return new NavPhysicalTag(profile.HasCrowdV1Defaults?NavPhysicalStorageKind.ArrivalProfile:NavPhysicalStorageKind.ArrivalProfileCrowdV1);
                if(Facade is NavArrivalWorkingBinding)return new NavPhysicalTag(NavPhysicalStorageKind.ArrivalWorkingBinding);
                if(Facade is NavArrivalMember)return new NavPhysicalTag(NavPhysicalStorageKind.ArrivalMember);
                if(Facade is NavRouteSubscription)return new NavPhysicalTag(NavPhysicalStorageKind.SubscriptionMetadata);
                if(Facade is TerrainLocations)return new NavPhysicalTag(NavPhysicalStorageKind.TerrainLocations);
                if(Facade is TerrainLocations.Seam)return new NavPhysicalTag(NavPhysicalStorageKind.PrivatePartitionSeam);
                if(Facade is PartitionNavigationProvider)return new NavPhysicalTag(NavPhysicalStorageKind.PartitionProvider);
                if(Facade is NavScheduledRequest)return new NavPhysicalTag(NavPhysicalStorageKind.Recipe);
                if(Facade is NavTerminalRegion)return new NavPhysicalTag(NavPhysicalStorageKind.TerminalRegion);
                if(Facade is MapSupport)return new NavPhysicalTag(NavPhysicalStorageKind.MapSupport);
                if(Facade is NavSurfaceTransition)return new NavPhysicalTag(NavPhysicalStorageKind.SurfaceTransition);
                if(Facade is NavPartitionSeam)return new NavPhysicalTag(NavPhysicalStorageKind.PartitionSeam);
                if(Facade is NavGraphBinding)return new NavPhysicalTag(NavPhysicalStorageKind.GraphBinding);
                if(Facade is NavTraversalArea)return new NavPhysicalTag(NavPhysicalStorageKind.TraversalArea);
                if(Facade is NavLayerSurface)return new NavPhysicalTag(NavPhysicalStorageKind.LayerSurface);
                if(Facade is NavLayerPortal)return new NavPhysicalTag(NavPhysicalStorageKind.LayerPortal);
                if(Facade is LayeredNavigationProvider)return new NavPhysicalTag(NavPhysicalStorageKind.LayeredProvider);
                throw new ArgumentException("Unsupported physical declaration facade.");
            }
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(Facade is NavArrivalReachabilityProof proof){proof.WritePhysicalBody(writer,this);return;}
            if(Facade is NavArrivalProposal proposal){proposal.WritePhysicalBody(writer,this);return;}
            if(Facade is NavArrivalInput input){input.WritePhysicalBody(writer,this);return;}
            if(Facade is NavArrivalOccupancySnapshot occupancy){occupancy.WritePhysicalBody(writer,this);return;}
            if(Facade is NavArrivalCandidateSource source){source.WritePhysicalBody(writer,this);return;}
            if(Facade is NavArrivalOccupant occupant){occupant.WritePhysicalBody(writer,this);return;}
            if(Facade is NavigationProfile profile){profile.WritePhysicalBody(writer,this);return;}
            if(Facade is NavArrivalWorkingBinding workingBinding){workingBinding.WritePhysicalBody(writer,this);return;}
            if(Facade is NavArrivalMember member){member.WritePhysicalBody(writer,this);return;}
            if(Facade is NavRouteSubscription subscription){subscription.WritePhysicalBody(writer,this);return;}
            if(Facade is TerrainLocations locations){locations.WritePhysicalBody(writer,this);return;}
            if(Facade is TerrainLocations.Seam privateSeam){TerrainLocations.WriteSeamPhysicalBody(writer,this,privateSeam);return;}
            if(Facade is PartitionNavigationProvider partition){partition.WritePhysicalBody(writer,this);return;}
            if(Facade is NavScheduledRequest recipe){recipe.WritePhysicalBody(writer,this);return;}
            if(Facade is NavTerminalRegion region){region.WritePhysicalBody(writer,this);return;}
            if(Facade is MapSupport support){support.WritePhysicalBody(writer,this);return;}
            if(Facade is NavSurfaceTransition transition){transition.WritePhysicalBody(writer,this);return;}
            if(Facade is NavPartitionSeam seam){seam.WritePhysicalBody(writer,this);return;}
            if(Facade is NavGraphBinding binding){binding.WritePhysicalBody(writer,this);return;}
            if(Facade is NavTraversalArea area){
                if(StorageBytes!=NavStorageLayout.Charge(256)||First!=area.Boundary.StorageRoot||Second==null||Third!=null||Fourth!=null||Fifth!=null||Sixth!=null||Seventh!=null||Eighth!=null)throw new ArgumentException("Physical area ownership contradiction.");
                writer.Output.Write(area.HalfExtent);writer.Edge(First);writer.Edge(Second);return;
            }
            if(Facade is NavLayerSurface surface){
                if(StorageBytes!=NavStorageLayout.Charge(192+NavProviderCopy.TextBytes(surface.Id)+NavProviderCopy.TextBytes(surface.CombatFamily))||First!=surface.Area.StorageRoot||Second!=null||Third!=null||Fourth!=null||Fifth!=null||Sixth!=null||Seventh!=null||Eighth!=null)throw new ArgumentException("Physical surface ownership contradiction.");
                NavPhysicalTextPlan.WritePlan(writer,surface.Id);NavPhysicalTextPlan.WritePlan(writer,surface.CombatFamily);
                NavPhysicalTextPlan.WriteData(writer,surface.Id);NavPhysicalTextPlan.WriteData(writer,surface.CombatFamily);writer.Output.Write(surface.Height);writer.Output.Write(surface.DistanceCost);writer.Edge(First);return;
            }
            if(Facade is LayeredNavigationProvider provider){LayeredNavigationProvider.WritePhysicalBody(writer,this,provider);return;}
            if(Facade is NavLayerPortal portal){NavLayerPortal.WritePhysicalBody(writer,this,portal);return;}
            throw new ArgumentException("Unsupported physical declaration BODY.");
        }
    }
    public sealed partial class NavTraversalArea
    {
        private NavTraversalArea(NavPolygon boundary,NavGeometry geometry)
        {
            Boundary=boundary;this.geometry=geometry;Blockers=geometry.Obstacles;HalfExtent=geometry.HalfExtent;
            long estimate=2048+128L*boundary.Vertices.Count;foreach(var blocker in Blockers)estimate+=512+128L*(blocker.Polygon?.Vertices.Count??4);EstimatedRetainedBytes=estimate;
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            double extent=NavPhysicalGeometryValue.Number(reader);var polygon=reader.Edge<NavPolygon.OwnedBuffers>();var geometry=reader.Edge<NavGeometry.OwnedBuffers>();
            if(extent<=0||polygon?.Facade==null||geometry?.Facade==null||geometry.Facade.HalfExtent!=extent||geometry.Facade.Revision!=1||polygon.Facade.Vertices.Count>256||geometry.Facade.Obstacles.Count>2048)throw new ArgumentException("Physical area geometry identity/capacity contradiction.");
            foreach(var point in polygon.Facade.Vertices)if(Math.Abs(point.X)>extent||Math.Abs(point.Z)>extent)throw new ArgumentException("Physical area outside extent.");
            foreach(var blocker in geometry.Facade.Obstacles)if(blocker.Polygon!=null&&blocker.Polygon.Vertices.Count>256)throw new ArgumentException("Physical area blocker capacity contradiction.");
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(256);var storage=allocation.Register(new NavDeclarationStorage(256));storage.RequireEdge(polygon);storage.RequireEdge(geometry);polygon.Retain();storage.First=polygon;geometry.Retain();storage.Second=geometry;
                var facade=new NavTraversalArea(polygon.Facade,geometry.Facade){owned=storage};storage.Facade=facade;return storage;
            }
        }
    }
    public sealed partial class NavLayerSurface
    {
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var idPlan=NavPhysicalTextPlan.Read(reader);var familyPlan=NavPhysicalTextPlan.Read(reader);long bytes=192+idPlan.Bytes+familyPlan.Bytes;
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);string id=idPlan.ReadData(reader),family=familyPlan.ReadData(reader);double height=NavPhysicalGeometryValue.Number(reader),cost=NavPhysicalGeometryValue.Number(reader);
                var area=reader.Edge<NavDeclarationStorage>();if(!(area?.Facade is NavTraversalArea facade)||cost<=0)throw new ArgumentException("Physical surface area/cost contradiction.");
                var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.RequireEdge(area);area.Retain();storage.First=area;storage.Facade=new NavLayerSurface(id,family,height,cost,facade){owned=storage};return storage;
            }
        }
    }
}
