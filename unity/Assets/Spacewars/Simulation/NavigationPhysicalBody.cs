using System;
using System.IO;

namespace Spacewars.Simulation
{
    internal static class NavPhysicalBodyWire
    {
        internal const int Magic=0x3142474e;
        // The enclosing transport owns and admits its output/private input.
        // This layer admits bounded traversal scratch and every reconstructed
        // physical allocation. It never borrows mutable public checkpoint DTOs.
        internal static void WriteTo(BinaryWriter output,NavOwnedStorage root,INavStorageReservation reservation)
        {
            if(output==null||!output.BaseStream.CanSeek)throw new ArgumentException("Physical BODY needs seekable admitted output.");
            var index=NavPhysicalCaptureIndex.CreateOwned(root,reservation);
            try{
                using(var scratch=NavStorageScratch.Reserve(reservation,128L*512)){
                    output.Write(Magic);long countPosition=output.BaseStream.Position;output.Write(0);
                    new NavPhysicalBodyWriter(output,index,reservation).Edge(root);int count=index.Finish();
                    long end=output.BaseStream.Position;output.BaseStream.Position=countPosition;output.Write(count);output.BaseStream.Position=end;
                }
            }finally{index.ReleaseStorage();}
        }
        internal static NavOwnedStorage ReadFrom(BinaryReader input,INavStorageReservation reservation)
            =>ReadFrom(input,reservation,null);
        internal static NavOwnedStorage ReadFrom(BinaryReader input,INavStorageReservation reservation,INavPhysicalBodyExtension extension)
        {
            if(input==null||input.ReadInt32()!=Magic)throw new ArgumentException("Unknown physical BODY envelope.");
            int count=input.ReadInt32();var resolution=NavPhysicalResolution.CreateOwned(count,reservation);
            try{
                using(var scratch=NavStorageScratch.Reserve(reservation,128L*512)){
                    var root=new NavPhysicalBodyReader(input,reservation,resolution,extension).Edge();
                    if(root==null||!root.IsImmutableCaptureRoot)throw new ArgumentException("Missing immutable physical BODY root.");
                    resolution.Finish();root.Retain();return root;
                }
            }finally{resolution.ReleaseStorage();}
        }
    }
    internal interface INavPhysicalBody
    {
        NavPhysicalTag PhysicalTag { get; }
        void WritePhysicalBody(NavPhysicalBodyWriter writer);
    }
    // The caller supplies the only additional reader for its own explicit tags.
    // Unknown tags never acquire a process-wide decoder or a fallback DTO path.
    internal interface INavPhysicalBodyExtension
    {
        NavOwnedStorage Read(NavPhysicalTag tag,NavPhysicalBodyReader reader);
    }
    // Records assign IDs on first preorder visit, but become resolvable only
    // after their complete postorder body. Back edges therefore fail closed.
    internal sealed class NavPhysicalBodyWriter
    {
        internal readonly BinaryWriter Output;
        private readonly NavPhysicalCaptureIndex index;
        internal readonly INavStorageReservation Reservation;
        internal System.Collections.Generic.IReadOnlyList<string> EmbeddedTexts;internal int EmbeddedCursor;
        private int depth;
        internal NavPhysicalBodyWriter(BinaryWriter output,NavPhysicalCaptureIndex index,INavStorageReservation reservation)
        {Output=output??throw new ArgumentNullException(nameof(output));this.index=index??throw new ArgumentNullException(nameof(index));Reservation=reservation??throw new ArgumentNullException(nameof(reservation));}
        internal void Edge(NavOwnedStorage storage)
        {
            if(storage==null){Output.Write((byte)0);return;}
            if(!(storage is INavPhysicalBody body))throw new ArgumentException("Unsupported physical BODY kind.");
            if(++depth>128)throw new ArgumentException("Physical BODY depth exceeded.");
            try{
                bool definition=index.Begin(storage,body.PhysicalTag,out int id);
                Output.Write(definition?(byte)2:(byte)1);Output.Write(id);Output.Write(body.PhysicalTag.Encoded);
                if(definition){body.WritePhysicalBody(this);index.Complete(storage,body.PhysicalTag);}
            }finally{depth--;}
        }
    }
    internal sealed class NavPhysicalBodyReader
    {
        internal readonly BinaryReader Input;
        internal readonly INavStorageReservation Reservation;
        private readonly NavPhysicalResolution resolution;
        internal readonly INavPhysicalBodyExtension Extension;
        internal long EmbeddedLengthPosition=-1;internal int EmbeddedCount,EmbeddedCursor;
        private int nextId,depth;
        internal NavPhysicalBodyReader(BinaryReader input,INavStorageReservation reservation,NavPhysicalResolution resolution,INavPhysicalBodyExtension extension=null)
        {Input=input??throw new ArgumentNullException(nameof(input));Reservation=reservation??throw new ArgumentNullException(nameof(reservation));this.resolution=resolution??throw new ArgumentNullException(nameof(resolution));Extension=extension;}
        internal NavOwnedStorage Edge()
        {
            byte marker=Input.ReadByte();if(marker==0)return null;
            if(marker!=1&&marker!=2)throw new ArgumentException("Invalid physical BODY marker.");
            int id=Input.ReadInt32();var tag=NavPhysicalTag.Decode(Input.ReadInt32());
            if(marker==1)return resolution.Resolve(id,tag);
            if(id!=nextId++)throw new ArgumentException("Noncanonical or duplicate physical BODY ID.");
            if(++depth>128)throw new ArgumentException("Physical BODY depth exceeded.");
            try{var storage=NavPhysicalBodyFactory.Read(tag,this);resolution.Define(id,tag,storage);return storage;}finally{depth--;}
        }
        internal T Edge<T>() where T:NavOwnedStorage
        {var storage=Edge();if(storage!=null&&!(storage is T))throw new ArgumentException("Wrong physical BODY edge type.");return (T)storage;}
        internal int Count(int maximum){int value=Input.ReadInt32();if(value<0||value>maximum)throw new ArgumentException("Invalid physical BODY capacity.");return value;}
    }
    internal static partial class NavPhysicalBodyFactory
    {
        internal static NavOwnedStorage Read(NavPhysicalTag tag,NavPhysicalBodyReader reader)
        {
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalWork)))return NavigationArrivalWork.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalPreparedClearance)))return NavigationArrivalWork.PreparedClearance.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalRouteRow)))return NavigationArrivalWork.RouteRow.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalProof)))return NavArrivalReachabilityProof.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalProposal)))return NavArrivalProposal.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalInput)))return NavArrivalInput.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalOccupancy)))return NavArrivalOccupancySnapshot.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalSource)))return NavArrivalCandidateSource.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalOccupant)))return NavArrivalOccupant.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalProfile)))return NavigationProfile.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalProfileCrowdV1,value:NavPhysicalValueKind.ArrivalProfileCorridor115V1)))return NavigationProfile.ReadPhysicalBody(reader,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalProfileCrowdV1)))return NavigationProfile.ReadPhysicalBody(reader,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalWorkingBinding)))return NavArrivalWorkingBinding.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalMember)))return NavArrivalMember.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Scheduler)))return LayeredNavigationScheduler.ReadPhysicalBody(reader);
            if(TryReadPersistent(tag,reader,out var persistent))return persistent;
            if(TryReadSchedulerPayload(tag,reader,out var payload))return payload;
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.SubscriptionMetadata)))return NavRouteSubscription.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.TypedRoute)))return NavTypedRoute.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Singleton)))return NavIncrementalSingletonRoute.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Field)))return NavSharedTerminalField.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.SharedConnector)))return NavIncrementalSharedRoute.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Compiler)))return LayeredNavigationCompileWork.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.PartitionCompiler)))return PartitionNavigationCompileWork.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Graph)))return LayeredNavigationGraph.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.PartitionEdges)))return NavPartitionEdges.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.PrivatePartitionSeam)))return TerrainLocations.ReadSeamPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.TerrainLocations)))return TerrainLocations.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.PartitionProvider)))return PartitionNavigationProvider.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.LazyPartitionStoreV2)))return NavLazyPartitionStore.ReadPhysicalBody(reader,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryRecipeV3,value:NavPhysicalValueKind.RectangleOriginsPolicyV9)))return NavScheduledRequest.ReadPhysicalBody(reader,true,false,true,true,true,false,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandQueryRecipeV3,value:NavPhysicalValueKind.RectangleOriginsPolicyV9)))return NavScheduledRequest.ReadPhysicalBody(reader,true,true,true,true,true,false,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryWorkV3,value:NavPhysicalValueKind.RectangleOriginsPolicyV9)))return NavDemandQueryWork.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandSchedulerV3,value:NavPhysicalValueKind.RectangleOriginsPolicyV9)))return LayeredNavigationScheduler.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV4,value:NavPhysicalValueKind.RectangleOriginsArrivalPolicyV11)))return NavHistoricalArrivalState.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV4,value:NavPhysicalValueKind.RectangleOriginsArrivalPolicyV11)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true,true,false,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.RectangleOriginsArrivalPolicyV11)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true,true,false,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryRecipeV3,value:NavPhysicalValueKind.CertifiedGroupCorridor115V1)))return NavScheduledRequest.ReadPhysicalBody(reader,true,false,true,true,true,false,true,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandQueryRecipeV3,value:NavPhysicalValueKind.CertifiedGroupCorridor115V1)))return NavScheduledRequest.ReadPhysicalBody(reader,true,true,true,true,true,false,true,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryWorkV3,value:NavPhysicalValueKind.CertifiedGroupCorridor115V1)))return NavDemandQueryWork.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandSchedulerV3,value:NavPhysicalValueKind.CertifiedGroupCorridor115V1)))return LayeredNavigationScheduler.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV4,value:NavPhysicalValueKind.CertifiedGroupArrival115V1)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true,true,false,true,true,true,true,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.CertifiedGroupArrival115V1)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true,true,false,true,true,true,true,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryRecipeV3,value:NavPhysicalValueKind.TerminalCostPolicyV13)))return NavScheduledRequest.ReadPhysicalBody(reader,true,false,true,true,true,false,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryRecipeV3,value:NavPhysicalValueKind.OriginShadowPolicyV12)))return NavScheduledRequest.ReadPhysicalBody(reader,true,false,true,true,true,false,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryRecipeV3,value:NavPhysicalValueKind.TerminalCornerPolicyV11)))return NavScheduledRequest.ReadPhysicalBody(reader,true,false,true,true,true,false,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryRecipeV3,value:NavPhysicalValueKind.TerminalRectanglePolicyV10)))return NavScheduledRequest.ReadPhysicalBody(reader,true,false,true,true,true,false,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandQueryRecipeV3,value:NavPhysicalValueKind.TerminalCostPolicyV13)))return NavScheduledRequest.ReadPhysicalBody(reader,true,true,true,true,true,false,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandQueryRecipeV3,value:NavPhysicalValueKind.OriginShadowPolicyV12)))return NavScheduledRequest.ReadPhysicalBody(reader,true,true,true,true,true,false,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandQueryRecipeV3,value:NavPhysicalValueKind.TerminalCornerPolicyV11)))return NavScheduledRequest.ReadPhysicalBody(reader,true,true,true,true,true,false,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandQueryRecipeV3,value:NavPhysicalValueKind.TerminalRectanglePolicyV10)))return NavScheduledRequest.ReadPhysicalBody(reader,true,true,true,true,true,false,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryWorkV3,value:NavPhysicalValueKind.TerminalCostPolicyV13)))return NavDemandQueryWork.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryWorkV3,value:NavPhysicalValueKind.OriginShadowPolicyV12)))return NavDemandQueryWork.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryWorkV3,value:NavPhysicalValueKind.TerminalCornerPolicyV11)))return NavDemandQueryWork.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryWorkV3,value:NavPhysicalValueKind.TerminalRectanglePolicyV10)))return NavDemandQueryWork.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandSchedulerV3,value:NavPhysicalValueKind.TerminalCostPolicyV13)))return LayeredNavigationScheduler.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandSchedulerV3,value:NavPhysicalValueKind.OriginShadowPolicyV12)))return LayeredNavigationScheduler.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandSchedulerV3,value:NavPhysicalValueKind.TerminalCornerPolicyV11)))return LayeredNavigationScheduler.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandSchedulerV3,value:NavPhysicalValueKind.TerminalRectanglePolicyV10)))return LayeredNavigationScheduler.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV4,value:NavPhysicalValueKind.Page0OccupancyArrivalPolicyV17)))return NavHistoricalArrivalState.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV4,value:NavPhysicalValueKind.Page0OccupancyArrivalPolicyV17)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true,true,false,true,true,true,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.Page0OccupancyArrivalPolicyV17)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true,true,false,true,true,true,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV4,value:NavPhysicalValueKind.TerminalCostArrivalPolicyV16)))return NavHistoricalArrivalState.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV4,value:NavPhysicalValueKind.OwnedArrivalAnchorPolicyV15)))return NavHistoricalArrivalState.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV4,value:NavPhysicalValueKind.OriginShadowArrivalPolicyV14)))return NavHistoricalArrivalState.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV4,value:NavPhysicalValueKind.TerminalCornerArrivalPolicyV13)))return NavHistoricalArrivalState.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV4,value:NavPhysicalValueKind.TerminalRectangleArrivalPolicyV12)))return NavHistoricalArrivalState.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV4,value:NavPhysicalValueKind.TerminalCostArrivalPolicyV16)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true,true,false,true,true,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV4,value:NavPhysicalValueKind.OwnedArrivalAnchorPolicyV15)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true,true,false,true,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV4,value:NavPhysicalValueKind.OriginShadowArrivalPolicyV14)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true,true,false,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV4,value:NavPhysicalValueKind.TerminalCornerArrivalPolicyV13)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true,true,false,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV4,value:NavPhysicalValueKind.TerminalRectangleArrivalPolicyV12)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true,true,false,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.TerminalCostArrivalPolicyV16)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true,true,false,true,true,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.OwnedArrivalAnchorPolicyV15)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true,true,false,true,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.OriginShadowArrivalPolicyV14)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true,true,false,true,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.TerminalCornerArrivalPolicyV13)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true,true,false,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.TerminalRectangleArrivalPolicyV12)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true,true,false,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ExclusionStateV1,value:NavPhysicalValueKind.OriginRectangleWorkV1)))return NavOriginRectangleWork.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ExclusionStateV1)))return NavOwnedExclusionState.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryRecipeV3,value:NavPhysicalValueKind.CertifiedLazyPartitionPolicyV8)))return NavScheduledRequest.ReadPhysicalBody(reader,true,false,true,true,true,false,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandQueryRecipeV3,value:NavPhysicalValueKind.CertifiedLazyPartitionPolicyV8)))return NavScheduledRequest.ReadPhysicalBody(reader,true,true,true,true,true,false,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryWorkV3,value:NavPhysicalValueKind.CertifiedLazyPartitionPolicyV8)))return NavDemandQueryWork.ReadPhysicalBody(reader,true,true,false,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandSchedulerV3,value:NavPhysicalValueKind.CertifiedLazyPartitionPolicyV8)))return LayeredNavigationScheduler.ReadPhysicalBody(reader,true,true,false,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV4,value:NavPhysicalValueKind.CertifiedLazyArrivalPolicyV10)))return NavHistoricalArrivalState.ReadPhysicalBody(reader,true,true,false,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV4,value:NavPhysicalValueKind.CertifiedLazyArrivalPolicyV10)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true,true,false,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.CertifiedLazyArrivalPolicyV10)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true,true,false,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.LazyPartitionStoreV1)))return NavLazyPartitionStore.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryRecipeV3,value:NavPhysicalValueKind.LazyPartitionPolicyV7)))return NavScheduledRequest.ReadPhysicalBody(reader,true,false,true,true,true,false,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandQueryRecipeV3,value:NavPhysicalValueKind.LazyPartitionPolicyV7)))return NavScheduledRequest.ReadPhysicalBody(reader,true,true,true,true,true,false,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryWorkV3,value:NavPhysicalValueKind.LazyPartitionPolicyV7)))return NavDemandQueryWork.ReadPhysicalBody(reader,true,true,false,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandSchedulerV3,value:NavPhysicalValueKind.LazyPartitionPolicyV7)))return LayeredNavigationScheduler.ReadPhysicalBody(reader,true,true,false,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV4,value:NavPhysicalValueKind.LazyArrivalPolicyV9)))return NavHistoricalArrivalState.ReadPhysicalBody(reader,true,true,false,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV4,value:NavPhysicalValueKind.LazyArrivalPolicyV9)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true,true,false,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.LazyArrivalPolicyV9)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true,true,false,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryRecipeV3,value:NavPhysicalValueKind.ExactAstarPolicyV6)))return NavScheduledRequest.ReadPhysicalBody(reader,true,false,true,true,true,false,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandQueryRecipeV3,value:NavPhysicalValueKind.ExactAstarPolicyV6)))return NavScheduledRequest.ReadPhysicalBody(reader,true,true,true,true,true,false,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandCatalogV3,value:NavPhysicalValueKind.ExactAstarPolicyV6)))return NavDemandQueryCatalog.ReadPhysicalBody(reader,true,false,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryWorkV3,value:NavPhysicalValueKind.ExactAstarPolicyV6)))return NavDemandQueryWork.ReadPhysicalBody(reader,true,true,false,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandSchedulerV3,value:NavPhysicalValueKind.ExactAstarPolicyV6)))return LayeredNavigationScheduler.ReadPhysicalBody(reader,true,true,false,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV4,value:NavPhysicalValueKind.ExactAstarArrivalPolicyV8)))return NavHistoricalArrivalState.ReadPhysicalBody(reader,true,true,false,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV4,value:NavPhysicalValueKind.ExactAstarArrivalPolicyV8)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true,true,false,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.ExactAstarArrivalPolicyV8)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true,true,false,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryRecipeV3,value:NavPhysicalValueKind.ExactCostPolicyV5)))return NavScheduledRequest.ReadPhysicalBody(reader,true,false,true,true,true,false,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandQueryRecipeV3,value:NavPhysicalValueKind.ExactCostPolicyV5)))return NavScheduledRequest.ReadPhysicalBody(reader,true,true,true,true,true,false,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandCatalogV3,value:NavPhysicalValueKind.ExactCostPolicyV5)))return NavDemandQueryCatalog.ReadPhysicalBody(reader,true,false,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryWorkV3,value:NavPhysicalValueKind.ExactCostPolicyV5)))return NavDemandQueryWork.ReadPhysicalBody(reader,true,true,false,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandSchedulerV3,value:NavPhysicalValueKind.ExactCostPolicyV5)))return LayeredNavigationScheduler.ReadPhysicalBody(reader,true,true,false,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV4,value:NavPhysicalValueKind.ExactArrivalPolicyV7)))return NavHistoricalArrivalState.ReadPhysicalBody(reader,true,true,false,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV4,value:NavPhysicalValueKind.ExactArrivalPolicyV7)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true,true,false,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.ExactArrivalPolicyV7)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true,true,false,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV4,value:NavPhysicalValueKind.RemovalPolicyV6)))return NavHistoricalArrivalState.ReadPhysicalBody(reader,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV4,value:NavPhysicalValueKind.RemovalPolicyV6)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.RemovalPolicyV6)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV4)))return NavHistoricalArrivalState.ReadPhysicalBody(reader,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV4)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV3)))return NavHistoricalArrivalState.ReadPhysicalBody(reader,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV3)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV3)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryRecipeV3,value:NavPhysicalValueKind.SeamBoundPolicyV4)))return NavScheduledRequest.ReadPhysicalBody(reader,true,false,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandQueryRecipeV3,value:NavPhysicalValueKind.SeamBoundPolicyV4)))return NavScheduledRequest.ReadPhysicalBody(reader,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandCatalogV3,value:NavPhysicalValueKind.SeamBoundPolicyV4)))return NavDemandQueryCatalog.ReadPhysicalBody(reader,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryWorkV3,value:NavPhysicalValueKind.SeamBoundPolicyV4)))return NavDemandQueryWork.ReadPhysicalBody(reader,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandSchedulerV3,value:NavPhysicalValueKind.SeamBoundPolicyV4)))return LayeredNavigationScheduler.ReadPhysicalBody(reader,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV4,value:NavPhysicalValueKind.SeamBoundPolicyV4)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.SeamBoundPolicyV4)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV4,value:NavPhysicalValueKind.SeamBoundPolicyV4)))return NavHistoricalArrivalState.ReadPhysicalBody(reader,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandCatalogV3)))return NavDemandQueryCatalog.ReadPhysicalBody(reader,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryRecipeV3)))return NavScheduledRequest.ReadPhysicalBody(reader,true,false,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandQueryRecipeV3)))return NavScheduledRequest.ReadPhysicalBody(reader,true,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryWorkV3)))return NavDemandQueryWork.ReadPhysicalBody(reader,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandSchedulerV3)))return LayeredNavigationScheduler.ReadPhysicalBody(reader,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HistoricalArrivalStateV2)))return NavHistoricalArrivalState.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandArrivalWorkV2)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV2)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandCatalogV2)))return NavDemandQueryCatalog.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryRecipeV2)))return NavScheduledRequest.ReadPhysicalBody(reader,true,false,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandQueryRecipeV2)))return NavScheduledRequest.ReadPhysicalBody(reader,true,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryWorkV2)))return NavDemandQueryWork.ReadPhysicalBody(reader,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandSchedulerV2)))return LayeredNavigationScheduler.ReadPhysicalBody(reader,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryWorkV1)))return NavDemandQueryWork.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryRecipeV1)))return NavScheduledRequest.ReadPhysicalBody(reader,true,false,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandQueryRecipeV1)))return NavScheduledRequest.ReadPhysicalBody(reader,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryArrivalWorkV1)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,false,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandQueryArrivalWorkV1)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Recipe)))return NavScheduledRequest.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.QueryRecipeV1)))return NavScheduledRequest.ReadPhysicalBody(reader,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.QuerySingletonV1)))return NavIncrementalSingletonRoute.ReadPhysicalBody(reader,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.QueryArrivalWorkV1)))return NavigationArrivalWork.ReadPhysicalBody(reader,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupQueryRecipeV1)))return NavScheduledRequest.ReadPhysicalBody(reader,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GroupQueryArrivalWorkV1)))return NavigationArrivalWork.ReadPhysicalBody(reader,true,true);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.TerminalRegion)))return NavTerminalRegion.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.MapSupport)))return MapSupport.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.SurfaceTransition)))return NavSurfaceTransition.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.PartitionSeam)))return NavPartitionSeam.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Connector)))return NavConnectorResult.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.GraphBinding)))return NavGraphBinding.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Metadata)))return NavGraphMetadata.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.LayeredProvider)))return LayeredNavigationProvider.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.LayerPortal)))return NavLayerPortal.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.TraversalArea)))return NavTraversalArea.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.LayerSurface)))return NavLayerSurface.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Polygon)))return NavPolygon.OwnedBuffers.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Geometry)))return NavGeometry.OwnedBuffers.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Metadata,value:NavPhysicalValueKind.Text)))return NavOwnedText.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Leg)))return NavTypedLeg.ReadPhysicalBody(reader);
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.PackedField)))return NavPackedFieldStorage.ReadPhysicalBody(reader);
            if(tag.Storage==NavPhysicalStorageKind.PackedSlots&&tag.Key==NavPhysicalValueKind.None&&tag.Value==NavPhysicalValueKind.None)return NavPackedSlots.ReadPhysicalBody(reader);
            if(tag.Storage==NavPhysicalStorageKind.FieldFrontier&&tag.Key==NavPhysicalValueKind.None&&tag.Value==NavPhysicalValueKind.None)return NavFieldFrontier.ReadPhysicalBody(reader);
            if(tag.Storage==NavPhysicalStorageKind.Queue&&tag.Key==NavPhysicalValueKind.None){
                if(tag.Value==NavPhysicalValueKind.GraphArc)return NavCowOrderedSet<NavGraphArc>.ReadPhysicalBody(reader);
                if(tag.Value==NavPhysicalValueKind.ExactQueueEntry)return NavCowOrderedSet<NavExactQueueEntry>.ReadPhysicalBody(reader);
                if(tag.Value==NavPhysicalValueKind.QueueEntry)return NavCowOrderedSet<NavQueueEntry>.ReadPhysicalBody(reader);
                if(tag.Value==NavPhysicalValueKind.Integer)return NavCowOrderedSet<int>.ReadPhysicalBody(reader);
            }
            if(tag.Key==NavPhysicalValueKind.None){
                switch(tag.Value){
                    case NavPhysicalValueKind.ReadOnlyArcList:return ReadValue<System.Collections.Generic.IReadOnlyList<NavGraphArc>>(tag,reader);
                    case NavPhysicalValueKind.PagedArcList:return ReadValue<NavPagedList<NavGraphArc>>(tag,reader);
                    case NavPhysicalValueKind.Location:return ReadValue<NavLocation>(tag,reader);
                    case NavPhysicalValueKind.GridCoordinate:return ReadValue<NavGridCoordinate>(tag,reader);
                    case NavPhysicalValueKind.GraphNode:return ReadValue<NavGraphNode>(tag,reader);
                    case NavPhysicalValueKind.PortalEndpoints:return ReadValue<NavPortalEndpoints>(tag,reader);
                    case NavPhysicalValueKind.NameKey:return ReadValue<NavNameKey>(tag,reader);
                    case NavPhysicalValueKind.NameReference:return ReadValue<NavNameReference>(tag,reader);
                    case NavPhysicalValueKind.ConnectorResult:return ReadValue<NavConnectorResult>(tag,reader);
                    case NavPhysicalValueKind.MetadataReference:return ReadValue<NavMetadataReference>(tag,reader);
                    case NavPhysicalValueKind.Integer:return ReadValue<int>(tag,reader);
                    case NavPhysicalValueKind.Unsigned:return ReadValue<uint>(tag,reader);
                case NavPhysicalValueKind.Long:return ReadValue<long>(tag,reader);
                    case NavPhysicalValueKind.Double:return ReadValue<double>(tag,reader);
                    case NavPhysicalValueKind.Boolean:return ReadValue<bool>(tag,reader);
                    case NavPhysicalValueKind.ExactCost:return ReadValue<NavExactCost>(tag,reader);
                    case NavPhysicalValueKind.ExactQueueEntry:return ReadValue<NavExactQueueEntry>(tag,reader);
                    case NavPhysicalValueKind.QueueEntry:return ReadValue<NavQueueEntry>(tag,reader);
                    case NavPhysicalValueKind.GraphArc:return ReadValue<NavGraphArc>(tag,reader);
                    case NavPhysicalValueKind.FieldCell:return ReadValue<NavPackedFieldCell>(tag,reader);
                    case NavPhysicalValueKind.Point:return ReadValue<NavPoint>(tag,reader);
                    case NavPhysicalValueKind.Obstacle:return ReadValue<NavObstacle>(tag,reader);
                    case NavPhysicalValueKind.ArrivalProposalDeclaration:return ReadValue<NavArrivalProposal>(tag,reader);
                    case NavPhysicalValueKind.TerminalRegionDeclaration:return ReadValue<NavTerminalRegion>(tag,reader);
                    case NavPhysicalValueKind.RetainedIntegerArray:return ReadValue<NavRetainedArray<int>>(tag,reader);
                    case NavPhysicalValueKind.OwnedText:return ReadValue<NavOwnedText>(tag,reader);
                    case NavPhysicalValueKind.ArrivalRouteRow:return ReadValue<NavigationArrivalWork.RouteRow>(tag,reader);
                    case NavPhysicalValueKind.ArrivalPreparedClearance:return ReadValue<NavigationArrivalWork.PreparedClearance>(tag,reader);
                    case NavPhysicalValueKind.ArrivalStatus:return ReadValue<NavArrivalStatus>(tag,reader);
                    case NavPhysicalValueKind.ArrivalMemberDeclaration:return ReadValue<NavArrivalMember>(tag,reader);
                    case NavPhysicalValueKind.ArrivalWorkingBindingDeclaration:return ReadValue<NavArrivalWorkingBinding>(tag,reader);
                    case NavPhysicalValueKind.ArrivalOccupantDeclaration:return ReadValue<NavArrivalOccupant>(tag,reader);
                    case NavPhysicalValueKind.SupportDeclaration:return ReadValue<MapSupport>(tag,reader);
                    case NavPhysicalValueKind.TransitionDeclaration:return ReadValue<NavSurfaceTransition>(tag,reader);
                    case NavPhysicalValueKind.PartitionSeamDeclaration:return ReadValue<NavPartitionSeam>(tag,reader);
                    case NavPhysicalValueKind.PrivatePartitionSeamDeclaration:return ReadValue<TerrainLocations.Seam>(tag,reader);
                    case NavPhysicalValueKind.SurfaceDeclaration:return ReadValue<NavLayerSurface>(tag,reader);
                    case NavPhysicalValueKind.PortalDeclaration:return ReadValue<NavLayerPortal>(tag,reader);
                    case NavPhysicalValueKind.Text:if(tag.Storage!=NavPhysicalStorageKind.Array||reader.EmbeddedLengthPosition<0)throw new ArgumentException("Unowned physical embedded string array.");return ReadValue<string>(tag,reader);
                    case NavPhysicalValueKind.Storage:return ReadValue<NavOwnedStorage>(tag,reader);
                    case NavPhysicalValueKind.TypedLeg:return ReadValue<NavTypedLeg>(tag,reader);
                    case NavPhysicalValueKind.LegTrace:return ReadValue<System.Collections.Generic.IReadOnlyList<NavTypedLeg>>(tag,reader);
                }
            }
            if(tag.Key==NavPhysicalValueKind.Text&&tag.Value==NavPhysicalValueKind.SurfaceDeclaration)return ReadMap<string,NavLayerSurface>(tag,reader);
            if(tag.Key==NavPhysicalValueKind.Text&&tag.Value==NavPhysicalValueKind.PortalDeclaration)return ReadMap<string,NavLayerPortal>(tag,reader);
            if(tag.Key==NavPhysicalValueKind.NameKey&&tag.Value==NavPhysicalValueKind.Storage)return ReadMap<NavNameKey,NavOwnedStorage>(tag,reader);
            if(tag.Key==NavPhysicalValueKind.NameKey&&tag.Value==NavPhysicalValueKind.Integer)return ReadMap<NavNameKey,int>(tag,reader);
            if(tag.Key==NavPhysicalValueKind.NameKey&&tag.Value==NavPhysicalValueKind.Double)return ReadMap<NavNameKey,double>(tag,reader);
            if(tag.Key==NavPhysicalValueKind.GraphArc&&tag.Value==NavPhysicalValueKind.Boolean)return ReadMap<NavGraphArc,bool>(tag,reader);
            if(tag.Key==NavPhysicalValueKind.GridCoordinate&&tag.Value==NavPhysicalValueKind.Integer)return ReadMap<NavGridCoordinate,int>(tag,reader);
            if(tag.Key==NavPhysicalValueKind.Integer){
                if(tag.Value==NavPhysicalValueKind.ReadOnlyArcList)return ReadMap<int,System.Collections.Generic.IReadOnlyList<NavGraphArc>>(tag,reader);
                if(tag.Value==NavPhysicalValueKind.PagedArcList)return ReadMap<int,NavPagedList<NavGraphArc>>(tag,reader);
                if(tag.Value==NavPhysicalValueKind.Integer)return ReadMap<int,int>(tag,reader);
                if(tag.Value==NavPhysicalValueKind.Boolean)return ReadMap<int,bool>(tag,reader);
                if(tag.Value==NavPhysicalValueKind.ExactCost)return ReadMap<int,NavExactCost>(tag,reader);
                if(tag.Value==NavPhysicalValueKind.Double)return ReadMap<int,double>(tag,reader);
                if(tag.Value==NavPhysicalValueKind.GraphArc)return ReadMap<int,NavGraphArc>(tag,reader);
            }
            if(tag.Key==NavPhysicalValueKind.Long&&tag.Value==NavPhysicalValueKind.Integer)return ReadMap<long,int>(tag,reader);
            if(tag.Key==NavPhysicalValueKind.Long&&tag.Value==NavPhysicalValueKind.Double)return ReadMap<long,double>(tag,reader);
            if(tag.Key==NavPhysicalValueKind.Long&&tag.Value==NavPhysicalValueKind.Storage)return ReadMap<long,NavOwnedStorage>(tag,reader);
            if(tag.Key==NavPhysicalValueKind.Long&&tag.Value==NavPhysicalValueKind.GraphArc)return ReadMap<long,NavGraphArc>(tag,reader);
            if(tag.Key==NavPhysicalValueKind.ExactQueueEntry&&tag.Value==NavPhysicalValueKind.Boolean)return ReadMap<NavExactQueueEntry,bool>(tag,reader);
            if(tag.Key==NavPhysicalValueKind.QueueEntry&&tag.Value==NavPhysicalValueKind.Boolean)return ReadMap<NavQueueEntry,bool>(tag,reader);
            if(tag.Value==NavPhysicalValueKind.AccruedAuthorityCreditsV1&&tag.Key==NavPhysicalValueKind.None&&
                (tag.Storage==NavPhysicalStorageKind.HostRootFairArrivalV1||tag.Storage==NavPhysicalStorageKind.HostEnvelopePublication)&&reader.Extension!=null)
                return reader.Extension.Read(tag,reader)??throw new ArgumentException("Host BODY extension rejected accrued credit tag.");
            if(tag.Value==NavPhysicalValueKind.ReplayOnlyProjectionV1&&tag.Key==NavPhysicalValueKind.None&&
                (tag.Storage==NavPhysicalStorageKind.HostRootFairArrivalV1||tag.Storage==NavPhysicalStorageKind.HostRow||tag.Storage==NavPhysicalStorageKind.HostProjection)&&reader.Extension!=null)
                return reader.Extension.Read(tag,reader)??throw new ArgumentException("Host BODY extension rejected Replay-only tag.");
            if(tag.Key==NavPhysicalValueKind.None&&reader.Extension!=null&&((tag.Storage==NavPhysicalStorageKind.AuthorityMember&&tag.Value==NavPhysicalValueKind.GroupMarchProgressV2)||(tag.Storage==NavPhysicalStorageKind.InstalledExecution&&(tag.Value==NavPhysicalValueKind.InstalledGeometryMemberV2||tag.Value==NavPhysicalValueKind.InstalledGeometryCertificateV2||tag.Value==NavPhysicalValueKind.InstalledExecutionV2))))return reader.Extension.Read(tag,reader)??throw new ArgumentException("V2 BODY extension rejected its tag.");
            if((tag.Storage==NavPhysicalStorageKind.HostEnvelopeTraffic||tag.Storage==NavPhysicalStorageKind.TrafficAuthority||tag.Storage==NavPhysicalStorageKind.AuthorityCatalogLedgerLayout||tag.Storage==NavPhysicalStorageKind.HostRootFairArrivalV1||tag.Storage==NavPhysicalStorageKind.HostRootQuantum||tag.Storage==NavPhysicalStorageKind.HostRoot||tag.Storage==NavPhysicalStorageKind.HostRequest||tag.Storage==NavPhysicalStorageKind.HostProvider||tag.Storage==NavPhysicalStorageKind.HostRow||tag.Storage==NavPhysicalStorageKind.HostAnswer||tag.Storage==NavPhysicalStorageKind.HostCorridor||tag.Storage==NavPhysicalStorageKind.HostProjection||tag.Storage==NavPhysicalStorageKind.HostArrivalIntents||tag.Storage==NavPhysicalStorageKind.HostArrivalHistory||tag.Storage==NavPhysicalStorageKind.HostAuthority||tag.Storage==NavPhysicalStorageKind.HostEnvelope||tag.Storage==NavPhysicalStorageKind.HostEnvelopePublication||tag.Storage==NavPhysicalStorageKind.AuthorityMember||tag.Storage==NavPhysicalStorageKind.AuthorityGroup||tag.Storage==NavPhysicalStorageKind.AuthorityCatalog||tag.Storage==NavPhysicalStorageKind.AuthorityCatalogTerrain||tag.Storage==NavPhysicalStorageKind.InstalledExecution||tag.Storage==NavPhysicalStorageKind.AuthorityCatalogRoutes||tag.Storage==NavPhysicalStorageKind.ArrivalAttemptSnapshot||tag.Storage==NavPhysicalStorageKind.ArrivalProviderSnapshot||tag.Storage==NavPhysicalStorageKind.ArrivalCatalog||tag.Storage==NavPhysicalStorageKind.ArrivalLeaseCatalog||tag.Storage==NavPhysicalStorageKind.LegacyProbeAnswer||tag.Storage==NavPhysicalStorageKind.AuthorityCatalogProbes||tag.Storage==NavPhysicalStorageKind.AuthorityCatalogAnswers||tag.Storage==NavPhysicalStorageKind.AuthorityCatalogLeases||tag.Storage==NavPhysicalStorageKind.ArrivalLeaseSnapshot||tag.Storage==NavPhysicalStorageKind.AuthorityCatalogArrival)&&tag.Key==NavPhysicalValueKind.None&&tag.Value==NavPhysicalValueKind.None&&reader.Extension!=null)
                return reader.Extension.Read(tag,reader)??throw new ArgumentException("Host BODY extension rejected its tag.");
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalAttemptSnapshot,value:NavPhysicalValueKind.OwnedArrivalAnchorV1))&&reader.Extension!=null)return reader.Extension.Read(tag,reader)??throw new ArgumentException("Owned arrival anchor extension rejected its tag.");
            throw new ArgumentException("Unsupported physical BODY factory.");
        }
        private static NavOwnedStorage ReadValue<T>(NavPhysicalTag tag,NavPhysicalBodyReader reader)
        {
            switch(tag.Storage){
                case NavPhysicalStorageKind.List:return NavPagedList<T>.ReadPhysicalBody(reader);
                case NavPhysicalStorageKind.Directory:return NavStorageDirectory<T>.ReadPhysicalBody(reader);
                case NavPhysicalStorageKind.DirectoryBlock:return NavStorageDirectory<T>.Block.ReadPhysicalBody(reader);
                case NavPhysicalStorageKind.Page:return NavStorageDirectory<T>.Page.ReadPhysicalBody(reader);
                case NavPhysicalStorageKind.Array:return NavRetainedArray<T>.ReadPhysicalBody(reader);
                default:throw new ArgumentException("Unsupported physical BODY container.");
            }
        }
        private static NavOwnedStorage ReadMap<TKey,TValue>(NavPhysicalTag tag,NavPhysicalBodyReader reader)
        {
            if(tag.Storage==NavPhysicalStorageKind.Map)return NavCowDictionary<TKey,TValue>.ReadPhysicalBody(reader);
            if(tag.Storage==NavPhysicalStorageKind.AvlNode)return NavCowDictionary<TKey,TValue>.Node.ReadPhysicalBody(reader);
            throw new ArgumentException("Unsupported physical BODY map.");
        }
    }
    internal static class NavPhysicalValue<T>
    {
        internal static NavPhysicalValueKind Kind {
            get{
                if(typeof(T)==typeof(System.Collections.Generic.IReadOnlyList<NavGraphArc>))return NavPhysicalValueKind.ReadOnlyArcList;
                if(typeof(T)==typeof(NavPagedList<NavGraphArc>))return NavPhysicalValueKind.PagedArcList;
                if(typeof(T)==typeof(NavLocation))return NavPhysicalValueKind.Location;
                if(typeof(T)==typeof(NavGridCoordinate))return NavPhysicalValueKind.GridCoordinate;
                if(typeof(T)==typeof(NavGraphNode))return NavPhysicalValueKind.GraphNode;
                if(typeof(T)==typeof(NavPortalEndpoints))return NavPhysicalValueKind.PortalEndpoints;
                if(typeof(T)==typeof(NavNameKey))return NavPhysicalValueKind.NameKey;
                if(typeof(T)==typeof(NavNameReference))return NavPhysicalValueKind.NameReference;
                if(typeof(T)==typeof(NavConnectorResult))return NavPhysicalValueKind.ConnectorResult;
                if(typeof(T)==typeof(NavMetadataReference))return NavPhysicalValueKind.MetadataReference;
                if(typeof(T)==typeof(LayeredNavigationScheduler.Job))return NavPhysicalValueKind.Job;
                if(typeof(T)==typeof(NavPersistentQueue<LayeredNavigationScheduler.Job>.Record))return NavPhysicalValueKind.JobQueueRecord;
                if(typeof(T)==typeof(NavScheduledCompletion))return NavPhysicalValueKind.Completion;
                if(typeof(T)==typeof(NavPersistentQueue<NavScheduledCompletion>.Record))return NavPhysicalValueKind.CompletionQueueRecord;
                if(typeof(T)==typeof(LayeredNavigationScheduler.CacheField))return NavPhysicalValueKind.CacheField;
                if(typeof(T)==typeof(NavPersistentQueue<LayeredNavigationScheduler.CacheField>.Record))return NavPhysicalValueKind.CacheFieldQueueRecord;
                if(typeof(T)==typeof(NavProviderReferences.Entry))return NavPhysicalValueKind.ProviderReference;
                if(typeof(T)==typeof(NavPersistentQueue<NavProviderReferences.Entry>.Record))return NavPhysicalValueKind.ProviderReferenceQueueRecord;
                if(typeof(T)==typeof(NavPersistentQueue<NavProviderReferences.Entry>))return NavPhysicalValueKind.ProviderReferenceQueue;
                if(typeof(T)==typeof(uint))return NavPhysicalValueKind.Unsigned;
                if(typeof(T)==typeof(NavArrivalProposal))return NavPhysicalValueKind.ArrivalProposalDeclaration;
                if(typeof(T)==typeof(NavTerminalRegion))return NavPhysicalValueKind.TerminalRegionDeclaration;
                if(typeof(T)==typeof(NavRetainedArray<int>))return NavPhysicalValueKind.RetainedIntegerArray;
                if(typeof(T)==typeof(NavOwnedText))return NavPhysicalValueKind.OwnedText;
                if(typeof(T)==typeof(NavigationArrivalWork.RouteRow))return NavPhysicalValueKind.ArrivalRouteRow;
                if(typeof(T)==typeof(NavigationArrivalWork.PreparedClearance))return NavPhysicalValueKind.ArrivalPreparedClearance;
                if(typeof(T)==typeof(NavArrivalStatus))return NavPhysicalValueKind.ArrivalStatus;
                if(typeof(T)==typeof(NavPersistentIndex<NavNameKey,NavigationArrivalWork.PreparedClearance>.Slot))return NavPhysicalValueKind.NamePreparedIndexSlot;
                if(typeof(T)==typeof(NavPersistentIndex<int,NavigationArrivalWork.RouteRow>.Slot))return NavPhysicalValueKind.IntegerArrivalRowIndexSlot;
                if(typeof(T)==typeof(NavPersistentIndex<long,NavArrivalStatus>.Slot))return NavPhysicalValueKind.LongArrivalStatusIndexSlot;
                if(typeof(T)==typeof(NavArrivalMember))return NavPhysicalValueKind.ArrivalMemberDeclaration;
                if(typeof(T)==typeof(NavArrivalWorkingBinding))return NavPhysicalValueKind.ArrivalWorkingBindingDeclaration;
                if(typeof(T)==typeof(NavArrivalOccupant))return NavPhysicalValueKind.ArrivalOccupantDeclaration;
                if(typeof(T)==typeof(NavScheduledRequest))return NavPhysicalValueKind.Request;
                if(typeof(T)==typeof(NavSubscriberLink))return NavPhysicalValueKind.SubscriberLink;
                if(typeof(T)==typeof(NavPersistentQueue<int>.Record))return NavPhysicalValueKind.IntegerQueueRecord;
                if(typeof(T)==typeof(NavPersistentQueue<NavNameKey>.Record))return NavPhysicalValueKind.NameQueueRecord;
                if(typeof(T)==typeof(NavPersistentQueue<NavScheduledRequest>.Record))return NavPhysicalValueKind.RequestQueueRecord;
                if(typeof(T)==typeof(NavPersistentQueue<NavTypedLeg>.Record))return NavPhysicalValueKind.LegQueueRecord;
                if(typeof(T)==typeof(NavPersistentIndex<NavNameKey,long>.Slot))return NavPhysicalValueKind.NameLongIndexSlot;
                if(typeof(T)==typeof(NavPersistentIndex<NavNameKey,NavSubscriberLink>.Slot))return NavPhysicalValueKind.NameSubscriberIndexSlot;
                if(typeof(T)==typeof(int))return NavPhysicalValueKind.Integer;
                if(typeof(T)==typeof(long))return NavPhysicalValueKind.Long;
                if(typeof(T)==typeof(double))return NavPhysicalValueKind.Double;
                if(typeof(T)==typeof(bool))return NavPhysicalValueKind.Boolean;
                if(typeof(T)==typeof(NavExactCost))return NavPhysicalValueKind.ExactCost;
                if(typeof(T)==typeof(NavExactQueueEntry))return NavPhysicalValueKind.ExactQueueEntry;
                if(typeof(T)==typeof(NavQueueEntry))return NavPhysicalValueKind.QueueEntry;
                if(typeof(T)==typeof(NavGraphArc))return NavPhysicalValueKind.GraphArc;
                if(typeof(T)==typeof(NavPackedFieldCell))return NavPhysicalValueKind.FieldCell;
                if(typeof(T)==typeof(NavPoint))return NavPhysicalValueKind.Point;
                if(typeof(T)==typeof(NavObstacle))return NavPhysicalValueKind.Obstacle;
                if(typeof(T)==typeof(MapSupport))return NavPhysicalValueKind.SupportDeclaration;
                if(typeof(T)==typeof(NavSurfaceTransition))return NavPhysicalValueKind.TransitionDeclaration;
                if(typeof(T)==typeof(NavPartitionSeam))return NavPhysicalValueKind.PartitionSeamDeclaration;
                if(typeof(T)==typeof(TerrainLocations.Seam))return NavPhysicalValueKind.PrivatePartitionSeamDeclaration;
                if(typeof(T)==typeof(NavLayerSurface))return NavPhysicalValueKind.SurfaceDeclaration;
                if(typeof(T)==typeof(NavLayerPortal))return NavPhysicalValueKind.PortalDeclaration;
                if(typeof(T)==typeof(string))return NavPhysicalValueKind.Text;
                if(typeof(T)==typeof(NavOwnedStorage))return NavPhysicalValueKind.Storage;
                if(typeof(T)==typeof(NavTypedLeg))return NavPhysicalValueKind.TypedLeg;
                if(typeof(T)==typeof(System.Collections.Generic.IReadOnlyList<NavTypedLeg>))return NavPhysicalValueKind.LegTrace;
                throw new ArgumentException("Unsupported physical BODY value type.");
            }
        }
        internal static void Write(NavPhysicalBodyWriter writer,T value)
        {
            switch(Kind){
                case NavPhysicalValueKind.ReadOnlyArcList:case NavPhysicalValueKind.PagedArcList:
                    var arcList=(object)value;if(arcList!=null&&(!(arcList is NavOwnedStorage listRoot)||!listRoot.IsImmutableCaptureRoot))throw new ArgumentException("Physical adjacency list needs immutable owned root.");writer.Edge(arcList as NavOwnedStorage);break;
                case NavPhysicalValueKind.Location:NavPhysicalNamedValue.WriteLocation(writer,(NavLocation)(object)value);break;
                case NavPhysicalValueKind.GridCoordinate:NavPhysicalNamedValue.WriteGrid(writer,(NavGridCoordinate)(object)value);break;
                case NavPhysicalValueKind.GraphNode:var node=(NavGraphNode)(object)value;writer.Output.Write(node.Id);writer.Output.Write(node.Component);NavPhysicalNamedValue.WriteLocation(writer,node.Location);break;
                case NavPhysicalValueKind.PortalEndpoints:var ends=(NavPortalEndpoints)(object)value;writer.Output.Write(ends.Ready);writer.Output.Write(ends.Item1);writer.Output.Write(ends.Item2);break;
                case NavPhysicalValueKind.NameKey:var key=(NavNameKey)(object)value;if(!ReferenceEquals(key.Value,key.Reference.Resolve()))throw new ArgumentException("Unowned physical name key.");key.Reference.Write(writer);break;
                case NavPhysicalValueKind.NameReference:((NavNameReference)(object)value).Write(writer);break;
                case NavPhysicalValueKind.ConnectorResult:writer.Edge((NavConnectorResult)(object)value);break;
                case NavPhysicalValueKind.MetadataReference:((NavMetadataReference)(object)value).Write(writer);break;
                case NavPhysicalValueKind.ArrivalStatus:writer.Output.Write((int)(NavArrivalStatus)(object)value);break;
                case NavPhysicalValueKind.Integer:writer.Output.Write((int)(object)value);break;
                case NavPhysicalValueKind.Unsigned:writer.Output.Write((uint)(object)value);break;
                case NavPhysicalValueKind.Long:writer.Output.Write((long)(object)value);break;
                case NavPhysicalValueKind.Double:writer.Output.Write((double)(object)value);break;
                case NavPhysicalValueKind.Boolean:writer.Output.Write((bool)(object)value);break;
                case NavPhysicalValueKind.ExactCost:case NavPhysicalValueKind.ExactQueueEntry:NavPhysicalExactWire<T>.Write(writer,value);break;
                case NavPhysicalValueKind.QueueEntry:var entry=(NavQueueEntry)(object)value;writer.Output.Write(entry.Cost);writer.Output.Write(entry.Node);writer.Output.Write(entry.Candidate);break;
                case NavPhysicalValueKind.Text:
                    var text=(string)(object)value;if(writer.EmbeddedTexts==null||writer.EmbeddedCursor>=writer.EmbeddedTexts.Count||!ReferenceEquals(writer.EmbeddedTexts[writer.EmbeddedCursor++],text))throw new ArgumentException("Unowned physical string payload.");NavPhysicalTextPlan.WriteData(writer,text);break;
                case NavPhysicalValueKind.ArrivalProposalDeclaration:case NavPhysicalValueKind.TerminalRegionDeclaration:
                case NavPhysicalValueKind.ArrivalMemberDeclaration:case NavPhysicalValueKind.ArrivalWorkingBindingDeclaration:case NavPhysicalValueKind.ArrivalOccupantDeclaration:
                case NavPhysicalValueKind.SupportDeclaration:case NavPhysicalValueKind.TransitionDeclaration:case NavPhysicalValueKind.PartitionSeamDeclaration:case NavPhysicalValueKind.PrivatePartitionSeamDeclaration:
                    var facade=(INavStorageFacade)(object)value;if(facade!=null&&facade.StorageRoot==null)throw new ArgumentException("Unowned physical partition facade.");writer.Edge(facade?.StorageRoot);break;
                case NavPhysicalValueKind.PortalDeclaration:var portal=(NavLayerPortal)(object)value;if(portal!=null&&portal.StorageRoot==null)throw new ArgumentException("Unowned physical portal facade.");writer.Edge(portal?.StorageRoot);break;
                case NavPhysicalValueKind.SurfaceDeclaration:var surface=(NavLayerSurface)(object)value;if(surface!=null&&surface.StorageRoot==null)throw new ArgumentException("Unowned physical surface facade.");writer.Edge(surface?.StorageRoot);break;
                case NavPhysicalValueKind.Point:var point=(NavPoint)(object)value;writer.Output.Write(point.X);writer.Output.Write(point.Z);break;
                case NavPhysicalValueKind.Obstacle:NavPhysicalGeometryValue.Write(writer,(NavObstacle)(object)value);break;
                case NavPhysicalValueKind.GraphArc:NavPhysicalGraphValue.WriteArc(writer,(NavGraphArc)(object)value);break;
                case NavPhysicalValueKind.FieldCell:var cell=(NavPackedFieldCell)(object)value;writer.Output.Write(cell.Distance);writer.Output.Write(cell.Flags);NavPhysicalGraphValue.WriteArc(writer,cell.Next);break;
                case NavPhysicalValueKind.Request:
                    var request=(NavScheduledRequest)(object)value;if(request!=null&&request.StorageRoot==null)throw new ArgumentException("Unowned physical request payload.");writer.Edge(request?.StorageRoot);break;
                case NavPhysicalValueKind.SubscriberLink:var link=(NavSubscriberLink)(object)value;writer.Output.Write(link.Item1);writer.Output.Write(link.Item2);break;
                case NavPhysicalValueKind.Job:case NavPhysicalValueKind.JobQueueRecord:case NavPhysicalValueKind.Completion:case NavPhysicalValueKind.CompletionQueueRecord:case NavPhysicalValueKind.CacheField:case NavPhysicalValueKind.CacheFieldQueueRecord:case NavPhysicalValueKind.ProviderReference:case NavPhysicalValueKind.ProviderReferenceQueueRecord:case NavPhysicalValueKind.ProviderReferenceQueue:
                case NavPhysicalValueKind.RetainedIntegerArray:case NavPhysicalValueKind.OwnedText:case NavPhysicalValueKind.ArrivalRouteRow:case NavPhysicalValueKind.ArrivalPreparedClearance:case NavPhysicalValueKind.NamePreparedIndexSlot:case NavPhysicalValueKind.IntegerArrivalRowIndexSlot:case NavPhysicalValueKind.LongArrivalStatusIndexSlot:
                case NavPhysicalValueKind.IntegerQueueRecord:case NavPhysicalValueKind.RequestQueueRecord:case NavPhysicalValueKind.LegQueueRecord:case NavPhysicalValueKind.NameQueueRecord:case NavPhysicalValueKind.NameLongIndexSlot:case NavPhysicalValueKind.NameSubscriberIndexSlot:
                case NavPhysicalValueKind.Storage:case NavPhysicalValueKind.TypedLeg:case NavPhysicalValueKind.LegTrace:writer.Edge((NavOwnedStorage)(object)value);break;
                default:throw new ArgumentException("Unsupported physical BODY value.");
            }
        }
        internal static T Read(NavPhysicalBodyReader reader)
        {
            switch(Kind){
                case NavPhysicalValueKind.ReadOnlyArcList:case NavPhysicalValueKind.PagedArcList:
                    var arcRoot=reader.Edge();if(arcRoot!=null&&(!(arcRoot is T)||!arcRoot.IsImmutableCaptureRoot))throw new ArgumentException("Wrong physical adjacency list kind/mutability.");return (T)(object)arcRoot;
                case NavPhysicalValueKind.Location:return (T)(object)NavPhysicalNamedValue.ReadLocation(reader);
                case NavPhysicalValueKind.GridCoordinate:return (T)(object)NavPhysicalNamedValue.ReadGrid(reader);
                case NavPhysicalValueKind.GraphNode:int id=reader.Count(299999),component=reader.Count(299999);return (T)(object)new NavGraphNode(id,NavPhysicalNamedValue.ReadLocation(reader),component);
                case NavPhysicalValueKind.PortalEndpoints:bool ready=NavPhysicalValue<bool>.Read(reader);int entry=reader.Count(299999),exit=reader.Count(299999);if(!ready&&(entry!=0||exit!=0))throw new ArgumentException("Invalid default portal endpoint pair.");return (T)(object)(ready?new NavPortalEndpoints(entry,exit):default);
                case NavPhysicalValueKind.NameKey:return (T)(object)new NavNameKey(NavNameReference.Read(reader));
                case NavPhysicalValueKind.NameReference:return (T)(object)NavNameReference.Read(reader);
                case NavPhysicalValueKind.ConnectorResult:return (T)(object)reader.Edge<NavConnectorResult>();
                case NavPhysicalValueKind.MetadataReference:return (T)(object)NavMetadataReference.Read(reader);
                case NavPhysicalValueKind.ArrivalStatus:int status=reader.Input.ReadInt32();if(!Enum.IsDefined(typeof(NavArrivalStatus),status))throw new ArgumentException("Unknown physical arrival status.");return (T)(object)(NavArrivalStatus)status;
                case NavPhysicalValueKind.Integer:return (T)(object)reader.Input.ReadInt32();
                case NavPhysicalValueKind.Unsigned:return (T)(object)reader.Input.ReadUInt32();
                case NavPhysicalValueKind.Long:return (T)(object)reader.Input.ReadInt64();
                case NavPhysicalValueKind.Double:return (T)(object)reader.Input.ReadDouble();
                case NavPhysicalValueKind.Boolean:byte b=reader.Input.ReadByte();if(b>1)throw new ArgumentException("Invalid physical boolean.");return (T)(object)(b==1);
                case NavPhysicalValueKind.ExactCost:case NavPhysicalValueKind.ExactQueueEntry:return NavPhysicalExactWire<T>.Read(reader);
                case NavPhysicalValueKind.QueueEntry:return (T)(object)new NavQueueEntry{Cost=reader.Input.ReadDouble(),Node=reader.Input.ReadInt32(),Candidate=reader.Input.ReadInt32()};
                case NavPhysicalValueKind.Text:
                    if(reader.EmbeddedLengthPosition<0||reader.EmbeddedCursor>=reader.EmbeddedCount)throw new ArgumentException("Unowned physical string payload.");
                    long position=reader.Input.BaseStream.Position;reader.Input.BaseStream.Position=reader.EmbeddedLengthPosition+4L*reader.EmbeddedCursor++;var plan=NavPhysicalTextPlan.Read(reader);reader.Input.BaseStream.Position=position;return (T)(object)plan.ReadData(reader);
                case NavPhysicalValueKind.ArrivalProposalDeclaration:case NavPhysicalValueKind.TerminalRegionDeclaration:
                case NavPhysicalValueKind.ArrivalMemberDeclaration:case NavPhysicalValueKind.ArrivalWorkingBindingDeclaration:case NavPhysicalValueKind.ArrivalOccupantDeclaration:
                case NavPhysicalValueKind.SupportDeclaration:case NavPhysicalValueKind.TransitionDeclaration:case NavPhysicalValueKind.PartitionSeamDeclaration:case NavPhysicalValueKind.PrivatePartitionSeamDeclaration:
                    var partition=reader.Edge<NavDeclarationStorage>();if(partition!=null&&!(partition.Facade is T))throw new ArgumentException("Wrong physical partition facade.");return (T)(partition?.Facade);
                case NavPhysicalValueKind.PortalDeclaration:var portal=reader.Edge<NavDeclarationStorage>();if(portal!=null&&!(portal.Facade is NavLayerPortal))throw new ArgumentException("Wrong physical portal facade.");return (T)(object)(portal?.Facade);
                case NavPhysicalValueKind.SurfaceDeclaration:var declaration=reader.Edge<NavDeclarationStorage>();if(declaration!=null&&!(declaration.Facade is NavLayerSurface))throw new ArgumentException("Wrong physical surface facade.");return (T)(object)(declaration?.Facade);
                case NavPhysicalValueKind.Point:return (T)(object)NavPhysicalGeometryValue.Point(reader);
                case NavPhysicalValueKind.Obstacle:return (T)(object)NavPhysicalGeometryValue.Read(reader);
                case NavPhysicalValueKind.GraphArc:return (T)(object)NavPhysicalGraphValue.ReadArc(reader);
                case NavPhysicalValueKind.FieldCell:return (T)(object)new NavPackedFieldCell{Distance=reader.Input.ReadDouble(),Flags=reader.Input.ReadByte(),Next=NavPhysicalGraphValue.ReadArc(reader)};
                case NavPhysicalValueKind.Request:var recipe=reader.Edge<NavDeclarationStorage>();if(recipe!=null&&!(recipe.Facade is NavScheduledRequest))throw new ArgumentException("Wrong physical request facade.");return (T)(recipe?.Facade);
                case NavPhysicalValueKind.SubscriberLink:return (T)(object)new NavSubscriberLink(reader.Input.ReadInt64(),reader.Input.ReadInt64());
                case NavPhysicalValueKind.Job:case NavPhysicalValueKind.JobQueueRecord:case NavPhysicalValueKind.Completion:case NavPhysicalValueKind.CompletionQueueRecord:case NavPhysicalValueKind.CacheField:case NavPhysicalValueKind.CacheFieldQueueRecord:case NavPhysicalValueKind.ProviderReference:case NavPhysicalValueKind.ProviderReferenceQueueRecord:case NavPhysicalValueKind.ProviderReferenceQueue:
                case NavPhysicalValueKind.RetainedIntegerArray:case NavPhysicalValueKind.OwnedText:case NavPhysicalValueKind.ArrivalRouteRow:case NavPhysicalValueKind.ArrivalPreparedClearance:case NavPhysicalValueKind.NamePreparedIndexSlot:case NavPhysicalValueKind.IntegerArrivalRowIndexSlot:case NavPhysicalValueKind.LongArrivalStatusIndexSlot:
                case NavPhysicalValueKind.IntegerQueueRecord:case NavPhysicalValueKind.RequestQueueRecord:case NavPhysicalValueKind.LegQueueRecord:case NavPhysicalValueKind.NameQueueRecord:case NavPhysicalValueKind.NameLongIndexSlot:case NavPhysicalValueKind.NameSubscriberIndexSlot:
                    var record=reader.Edge();if(record!=null&&!(record is T))throw new ArgumentException("Wrong physical persistent record payload.");return (T)(object)record;
                case NavPhysicalValueKind.Storage:return (T)(object)reader.Edge();
                case NavPhysicalValueKind.TypedLeg:return (T)(object)reader.Edge<NavTypedLeg>();
                case NavPhysicalValueKind.LegTrace:var trace=reader.Edge();if(trace!=null&&!(trace is System.Collections.Generic.IReadOnlyList<NavTypedLeg>))throw new ArgumentException("Wrong typed trace BODY.");return (T)(object)trace;
                default:throw new ArgumentException("Unsupported physical BODY value.");
            }
        }
    }
}
