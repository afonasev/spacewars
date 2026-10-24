using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static NavScheduledRequest V2Request()
        {
            var s=DemandRequest();return new NavScheduledRequest(s.Provider,s.Clearance,s.Profile,s.Limits,s.Region,s.Subscription,s.MaxLabels,query:true,demandQuery:true,demandQueryV2:true);
        }
        [Test] public void DemandV2CatalogReuseKeepsCurrentMobilityAndColdOwnership()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));var r=scope.OwnedReservation();
            var source=V2Request();var provider=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)source.Provider,r);var first=NavScheduledRequest.CopyOwned(source,provider,r);var work=new NavDemandQueryWork(first,scope.TryReserve);
            NavDemandQueryCatalog catalog=null;for(int n=0;(catalog=work.ExportCatalog(scope.TryReserve))==null;n++){Assert.Less(n,20000);if(work.CanConnector)work.StepConnector();else work.StepGraph();}
            Assert.NotNull(catalog);
            var q=source.Subscription;var nextSub=new NavRouteSubscription(q.WorldGeneration,q.GroupId,q.RootOrderRevision,q.CommandSequence,q.IssuedTick,q.ActivationRevision,2,q.Incarnation,q.MemberOrderRevision,2,2,q.Origin,q.AssignedEndpoint,q.HoldBinding,q.Provenance);
            var nextClearance=new NavClearanceProfile(source.Clearance.Id,source.Clearance.Revision,source.Clearance.MobilityClass,2,source.Clearance.Radius);
            var raw=new NavScheduledRequest(source.Provider,nextClearance,source.Profile,source.Limits,source.Region,nextSub,source.MaxLabels,query:true,demandQuery:true,demandQueryV2:true);var next=NavScheduledRequest.CopyOwned(raw,provider,r);
            Assert.True(catalog.Compatible(next));var second=new NavDemandQueryWork(next,scope.TryReserve,shared:catalog);Assert.AreEqual(work.SampleCount,second.SampleCount);Assert.AreEqual(2,next.Clearance.MobilityRevision);Assert.AreEqual(2,next.Subscription.RequestSequence);
            second.Seal();work.ReleaseOwner();catalog.ReleaseOwner();first.StorageRoot.ReleaseOwner();next.StorageRoot.ReleaseOwner();provider.StorageRoot.ReleaseOwner();scope.Attach(second);scope.Commit(second,_=>{});
            var bytes=CaptureGraph(budget,second);second.ReleaseOwner();Assert.Zero(budget.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,10000,out var decode));var restored=Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));restored.ReleaseOwner();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
        }
        private static NavArrivalInput CarryInput(bool added,long request)
        {
            var s=V2Request();var map=ThreeCrossingsMap.Default;var obstacles=map.MovementBlockers.Concat(added?new[]{new NavObstacle(-.1,-.1,.1,.1)}:Array.Empty<NavObstacle>()).ToArray();var provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,obstacles,map.Revision+(added?1:0)),null);
            var at=s.Subscription.Origin;var end=new NavLocation(new NavPoint(at.Position.X+.5,at.Position.Z),at.SurfaceId);Assert.True(provider.IsValid(end,s.Clearance));
            var member=NavigationArrivalTestFactory.Member(1,0,at,.25,request:request);var source=new NavArrivalCandidateSource("carry",1,end,new[]{end});return new NavArrivalInput("topology:"+provider.Binding,"carry-profile",provider,s.Profile,new NavigationProfile(),s.Limits,source,new[]{member},NavigationArrivalTestFactory.Occupancy(new NavArrivalOccupant(NavArrivalOccupantKind.PendingReservation,1,1,new NavLocation(new NavPoint(0,0),at.SurfaceId),.25,true,"request:"+request,NavArrivalReservationKind.PendingRequest,request)),4096,new[]{new NavArrivalWorkingBinding(member.EntityId,provider,s.Profile)},true);
        }
        private static NavigationArrivalWork OwnedCarry(NavArrivalInput source,INavStorageReservation r,int version=6)
        {
            var p=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)source.Provider,r);var metadata=NavGraphMetadata.CopyOptionalUnowned(source.Members[0].Clearance,source.Profile,source.Limits,r);var arrival=NavigationProfile.CopyOwned(source.ArrivalProfile,r);var candidates=NavArrivalCandidateSource.CopyOwned(source.Source,r);var rows=source.Occupancy.Occupants.Select(o=>NavArrivalOccupant.CopyOwned(o,r)).ToArray();var occupancy=NavArrivalOccupancySnapshot.CopyOwned(source.Occupancy,rows,r);foreach(var row in rows)row.StorageRoot.ReleaseOwner();var member=NavArrivalMember.CopyOwned(source.Members[0],metadata.Clearance,r);var binding=NavArrivalWorkingBinding.CreateOwned(member.EntityId,p,metadata.Profile,r);var input=NavArrivalInput.CopyOwned(source,p,metadata.Profile,arrival,metadata.Limits,candidates,new[]{member},occupancy,new[]{binding},r);
            try{var work=NavigationArrivalWork.CreateOwned(input,r);typeof(NavigationArrivalWork).GetMethod("SetContinuationPolicy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(work,new object[]{true,version==7,false});return work;}finally{input.StorageRoot.ReleaseOwner();p.StorageRoot.ReleaseOwner();metadata.Release();arrival.StorageRoot.ReleaseOwner();candidates.StorageRoot.ReleaseOwner();occupancy.StorageRoot.ReleaseOwner();member.StorageRoot.ReleaseOwner();binding.StorageRoot.ReleaseOwner();}
        }
        [Test] public void DemandV2HistoricalReadyMapsFreshRequestAndLogicalLifetime()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var birth));var old=OwnedCarry(CarryInput(false,100),birth.OwnedReservation());Assert.AreEqual(NavArrivalStatus.Ready,NavigationArrivalTestFactory.Complete(old));old.SealPrepared();birth.Attach(old);birth.Commit(old,_=>{});var oldProof=old.Proposals.Single().Proof;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var carry));var currentInput=CarryInput(true,200);var current=OwnedCarry(currentInput,carry.OwnedReservation());current.AttachHistorical(old,carry.OwnedReservation());current.Advance(0,1,new NavEpochQuota(0,0,0,0,0));var pending=current.Save();Assert.AreEqual(NavArrivalStatus.Ready,NavigationArrivalTestFactory.Complete(current));var proposal=current.Proposals.Single();string proofKey=proposal.Proof.StableKey;Assert.AreEqual(201,proposal.Proof.Subscription.RequestSequence);Assert.AreEqual(currentInput.Provider.Binding,proposal.Proof.ProviderBinding);Assert.AreNotEqual(oldProof.StableKey,proposal.Proof.StableKey);Assert.AreEqual(currentInput.Members[0].StableKey,proposal.Proof.MemberKey);Assert.AreEqual(6,current.Save().Version);
            var checkpoint=current.Save();current.SealPrepared();carry.Attach(current);carry.Commit(current,_=>{});old.ReleaseOwner();current.ReleaseOwner();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
            var continued=NavigationArrivalWork.Restore(currentInput,pending);try{Assert.AreEqual(NavArrivalStatus.Ready,NavigationArrivalTestFactory.Complete(continued));Assert.AreEqual(proofKey,continued.Proposals.Single().Proof.StableKey);}finally{continued.ReleaseOwner();}
            var logical=NavigationArrivalWork.Restore(currentInput,checkpoint);try{Assert.AreEqual(NavArrivalStatus.Ready,logical.Status);Assert.AreEqual(proofKey,logical.Proposals.Single().Proof.StableKey);var again=NavigationArrivalWork.Restore(currentInput,logical.Save());again.ReleaseOwner();}finally{logical.ReleaseOwner();}
        }
        [Test] public void DemandV2MonotoneCertificateRejectsRemovalAndExactLegacyRecipePolicy()
        {
            var old=(PartitionNavigationProvider)CarryInput(false,100).Provider;var added=(PartitionNavigationProvider)CarryInput(true,200).Provider;Assert.True(old.MonotoneHeaderTo(added));for(int i=0;i<old.MonotonePartCount;i++)Assert.True(old.MonotonePartTo(added,i));Assert.False(added.MonotoneHeaderTo(old));
            var legacy=DemandRequest();var current=V2Request();Assert.False(legacy.DemandQueryV2);Assert.True(current.DemandQueryV2);Assert.AreNotEqual(legacy.CompileKey,current.CompileKey);Assert.AreEqual(legacy.GraphKey,current.GraphKey);Assert.LessOrEqual(NavStorageLayout.ObjectBytesBound(typeof(NavDemandQueryCatalog)),256);Assert.LessOrEqual(NavStorageLayout.ObjectBytesBound(typeof(NavHistoricalArrivalState)),512);
        }
    }
}
