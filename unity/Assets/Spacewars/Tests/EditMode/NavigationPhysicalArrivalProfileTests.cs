using System;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        [Test]
        public void BodyArrivalProfileAllFieldsAndWorkingBindingExactAliasesEveryNth()
        {
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var birth));var r=birth.OwnedReservation();
            var profile=NavigationProfile.CopyOwned(new NavigationProfile("arrival-owned",3,.4,4,3,1,.2,2,.2,17,30,-19,.7,1.7,8,.5),r);
            var provider=LayeredNavigationProvider.CopyOwned((LayeredNavigationProvider)NavigationAdmission.CertifiedFlatProvider(new NavGeometry(3,new NavObstacle[0],1)),r);
            var metadata=NavGraphMetadata.CopyOptionalUnowned(null,new NavGraphProfile(new string('b',64),"distance",1,1),null,r);
            var a=NavArrivalWorkingBinding.CreateOwned(4,provider,metadata.Profile,r);var b=NavArrivalWorkingBinding.CreateOwned(7,provider,metadata.Profile,r);
            var root=NavRetainedArray<NavOwnedStorage>.Copy(new[]{profile.StorageRoot,a.StorageRoot,b.StorageRoot,provider.StorageRoot,(NavOwnedStorage)metadata},birth.TryReserve);
            profile.StorageRoot.ReleaseOwner();a.StorageRoot.ReleaseOwner();b.StorageRoot.ReleaseOwner();provider.StorageRoot.ReleaseOwner();metadata.Release();birth.Attach(root);birth.Commit(root,_=>{});
            byte[] bytes=CaptureGraph(hot,root);long charge=Unique(root);Assert.AreEqual(charge,hot.PhysicalBytes);root.Release();root=null;profile=null;a=null;b=null;provider=null;metadata=null;Assert.Zero(hot.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});
            var p=(NavigationProfile)((NavDeclarationStorage)restored[0]).Facade;var first=(NavArrivalWorkingBinding)((NavDeclarationStorage)restored[1]).Facade;var second=(NavArrivalWorkingBinding)((NavDeclarationStorage)restored[2]).Facade;
            Assert.AreEqual("arrival-owned",p.ProfileId);Assert.AreEqual(3,p.Revision);CollectionAssert.AreEqual(new[]{.4,4,3,1,.2,2,.2,17,30,-19,.7,1.7,8,.5},new[]{p.Radius,p.TankSpeed,p.TankTurnSpeed,p.GridCell,p.ArrivalTolerance,p.LocalDetourDistance,p.ForwardAlignmentAngle,p.CameraOrthoSize,p.CameraHeight,p.CameraOffsetZ,p.RepairRetrySeconds,p.ArrivalSlotSpacing,p.BlockedTimeoutSeconds,p.LocalGridCell});Assert.AreSame(first.Provider,second.Provider);Assert.AreSame(first.Profile,second.Profile);Assert.AreSame(restored[3],((INavStorageFacade)first.Provider).StorageRoot);Assert.AreSame(restored[4],first.Profile.MetadataOwner);Assert.AreEqual(4,first.Entity);Assert.AreEqual(7,second.Entity);Assert.AreEqual(charge,cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
            var foreign=new NavigationRetentionBudget();Assert.True(foreign.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var rejectedBirth));Assert.Throws<ArgumentException>(()=>NavArrivalWorkingBinding.CreateOwned(9,first.Provider,first.Profile,rejectedBirth.OwnedReservation()));Assert.Throws<ArgumentException>(()=>NavigationProfile.CopyOwned(p,rejectedBirth.OwnedReservation()));Assert.Zero(foreign.PhysicalBytes);Assert.AreEqual(NavigationRetentionBudget.ScopeBytes,foreign.HeldBytes);rejectedBirth.Discard();
            foreach(bool copyProfile in new[]{false,true}){
                NavOwnedStorage Born(INavStorageReservation reservation)=>copyProfile?NavigationProfile.CopyOwned(p,reservation).StorageRoot:NavArrivalWorkingBinding.CreateOwned(9,first.Provider,first.Profile,reservation).StorageRoot;
                Assert.True(cold.TryOpenScope(NavigationPreparationKind.Admission,1,20,out var countedBirth));var sourceCounter=new Refusal(countedBirth,int.MaxValue);var born=Born(sourceCounter);countedBirth.Attach(born);int sourceCalls=sourceCounter.Calls;countedBirth.Discard();Assert.AreEqual(charge,cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
                for(int nth=1;nth<=sourceCalls;nth++){Assert.True(cold.TryOpenScope(NavigationPreparationKind.Admission,1,20+nth,out var sourceRejection));var refusal=new Refusal(sourceRejection,nth);Assert.Throws<InvalidOperationException>(()=>Born(refusal));Assert.AreEqual(nth,refusal.Calls);sourceRejection.Discard();Assert.AreEqual(charge,cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));}
            }
            restored.Release();restored=null;p=null;first=null;second=null;Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            var counted=new NavigationRetentionBudget();Assert.True(counted.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var counter=new Refusal(scope,int.MaxValue);var trial=Decode(bytes,counter);scope.Attach(trial);int calls=counter.Calls;scope.Discard();Assert.Zero(counted.PhysicalBytes);
            for(int nth=1;nth<=calls;nth++){var bad=new NavigationRetentionBudget();Assert.True(bad.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusal=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusal));Assert.AreEqual(nth,refusal.Calls);rejected.Discard();Assert.Zero(bad.PhysicalBytes);Assert.Zero(bad.HeldBytes);Assert.Zero(bad.ActiveScopes);}
        }
    }
}
