using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationPrivateCaptureOutputTests
    {
        private PlayableDomain domain;private NavigationSession nav;private CertifiedRouteKernel kernel;
        [SetUp] public void SetUp()
        {
            domain=new PlayableDomain(PlayableProfile.Default,71,false);nav=domain.Navigation;kernel=new CertifiedRouteKernel();int id=(int)typeof(PlayableDomain).GetMethod("SpawnUnit",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(domain,new object[]{new NavPoint(-18,-14),PlayableOwner.Player,PlayableEntityKind.Tank});
            Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(71,1,"player-1",PlayableCommandKind.Move,new[]{id},new NavPoint(-10,-14)).AsHuman(),out _));
            for(int i=0;i<20000&&nav.PendingCount>0;i++){kernel.Pump(new PlayableRouteBinding(domain.Geometry,PlayableProfile.Default,nav.Admission,nav.RoutePort),4);nav.ApplyResults();}Assert.Zero(nav.PendingCount);
            using(var warm=domain.CaptureWorldState(7,"private-output-lifetime")){} // stabilize the actual published capture roots
        }
        [TearDown] public void TearDown()
        {kernel?.Clear();if(nav!=null){nav.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);}}
        private static byte[] Pack(NavigationSessionState state)=>WorldWire.Pack(w=>WorldWire.Write(w,state));
        [Test] public void PrivateWorldSectionKeepsExactFixedSnapshotAndRepeatedEnvelopeWhileOnlyOutputsEnd()
        {
            byte[] expected;using(var original=nav.CaptureOwnedState())expected=Pack(original);
            var budget=nav.PhysicalHostBudget;long baseline=budget.PhysicalBytes;NavigationCaptureDtoStorage dto;
            using(var state=domain.CaptureWorldState(7,"private-output-lifetime")){
                var capture=state.CaptureNavigation;dto=capture.CaptureDto;Assert.Null(capture.CaptureOutput);Assert.Null(capture.CaptureArrivalOutput);Assert.Null(capture.HostAuthority);Assert.Null(capture.ArrivalAuthority);
                Assert.AreSame(budget,capture.CaptureBudget);Assert.NotNull(dto);Assert.Greater(dto.References,0);Assert.NotNull(capture.CaptureRoot);Assert.Greater(capture.CaptureRoot.References,0);Assert.NotNull(capture.CaptureAuthority);Assert.Greater(capture.CaptureAuthority.References,0);
                Assert.NotNull(capture.CaptureArrivalCatalog);Assert.Greater(capture.CaptureArrivalCatalog.References,0);CertifiedPhysicalPumpTests.AssertCaptureDtoLayout(capture);
                CollectionAssert.AreEqual(expected,state.Navigation);var bytes=state.Encode();CollectionAssert.AreEqual(WorldCaptureEncodingTests.Original(state),bytes);CollectionAssert.AreEqual(bytes,state.Encode());
                CollectionAssert.AreEqual(expected,PlayableWorldState.Decode(bytes).Navigation);Assert.Greater(dto.References,0);Assert.AreSame(budget,capture.CaptureBudget);
            }
            Assert.Zero(dto.References);Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void FlushBoundaryReleasesExactlyBothOutputOwnersAndLeavesCatalogPinsUntilDispose()
        {
            var budget=nav.PhysicalHostBudget;long baseline=budget.PhysicalBytes;
            using(var capture=nav.CaptureOwnedState()){
                var expected=Pack(capture);var body=capture.CaptureOutput;var arrival=capture.CaptureArrivalOutput;var dto=capture.CaptureDto;var root=capture.CaptureRoot;var authority=capture.CaptureAuthority;var catalog=capture.CaptureArrivalCatalog;Assert.NotNull(body);Assert.NotNull(arrival);
                long released=body.StorageBytes+arrival.StorageBytes;long before=budget.PhysicalBytes;int roots=root.References,dtos=dto.References,catalogs=catalog.References;
                using(var section=WorldWire.PackOwned(w=>{WorldWire.Write(w,capture);w.Flush();capture.ReleasePrivateSerializedOutputs();Assert.AreEqual(before-released,budget.PhysicalBytes);Assert.Zero(body.References);Assert.Zero(arrival.References);Assert.Null(capture.HostAuthority);Assert.Null(capture.ArrivalAuthority);Assert.AreEqual(roots,root.References);Assert.AreEqual(dtos,dto.References);Assert.AreEqual(catalogs,catalog.References);Assert.AreSame(authority,capture.CaptureAuthority);Assert.AreSame(budget,capture.CaptureBudget);},budget)){
                    CollectionAssert.AreEqual(expected,section.Bytes);capture.ReleasePrivateSerializedOutputs();CertifiedPhysicalPumpTests.AssertCaptureDtoLayout(capture);Assert.Greater(dto.References,0);
                }
            }Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [TestCase(false)][TestCase(true)] public void PrivateWorldNavigationWriteOrFinalAllocationRefusalCleansAllPrivateOutputs(bool finalAllocation)
        {
            var budget=nav.PhysicalHostBudget;long finalCharge;
            using(var original=nav.CaptureOwnedState())using(var section=WorldWire.PackOwned(w=>WorldWire.Write(w,original),budget))finalCharge=section.StorageBytes;
            long baseline=budget.PhysicalBytes;int denied=0;
            try{budget.AllocationAdmission=bytes=>{
                var frames=new System.Diagnostics.StackTrace().GetFrames();bool world=frames.Any(f=>f.GetMethod().DeclaringType==typeof(PlayableDomain)&&f.GetMethod().Name=="CaptureWorldState");if(!world)return true;
                bool sectionWrite=frames.Any(f=>f.GetMethod().DeclaringType==typeof(WorldWire)&&f.GetMethod().Name=="Write"&&f.GetMethod().GetParameters().Any(p=>p.ParameterType==typeof(NavigationSessionState)));
                bool final=bytes==finalCharge&&frames.Any(f=>f.GetMethod().DeclaringType==typeof(NavigationCaptureBytes)&&f.GetMethod().Name=="Allocate");
                if(finalAllocation?final:sectionWrite&&frames.Any(f=>f.GetMethod().DeclaringType==typeof(NavigationCaptureStream)&&f.GetMethod().Name=="Grow")){denied++;return false;}return true;
            };Assert.Throws<InvalidOperationException>(()=>{using(var unexpected=domain.CaptureWorldState(7,"private-output-lifetime"))Assert.Fail("targeted allocation must refuse");});}
            finally{budget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied);Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            using(var state=domain.CaptureWorldState(7,"private-output-lifetime")){CollectionAssert.AreEqual(state.Encode(),state.Encode());}
            Assert.AreEqual(baseline,budget.PhysicalBytes);
        }
        [Test] public void PublicCaptureCallerMutationStillCannotChangeFreshAuthorityCapture()
        {
            var first=nav.CaptureState();var expected=Pack(first);Assert.IsNotEmpty(first.HostAuthority);Assert.IsNotEmpty(first.ArrivalAuthority);first.HostAuthority[0]^=1;first.ArrivalAuthority[0]^=1;first.Dispose();
            using(var next=nav.CaptureOwnedState()){Assert.NotNull(next.CaptureOutput);Assert.NotNull(next.CaptureArrivalOutput);CollectionAssert.AreEqual(expected,Pack(next));}
        }
    }
}
