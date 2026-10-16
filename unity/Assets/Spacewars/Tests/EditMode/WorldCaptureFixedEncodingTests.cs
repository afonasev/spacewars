using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class WorldCaptureFixedEncodingTests
    {
        const int SavedLength=6485479;
        static byte[] Saved(){var path=Environment.GetEnvironmentVariable("SPACEWARS_QA_CAPTURE_STATE");Assert.IsNotEmpty(path,"explicit durable fixture input required");var bytes=File.ReadAllBytes(path);Assert.AreEqual(SavedLength,bytes.Length);using(var h=SHA256.Create())Assert.AreEqual("407894fe3aabb4076f0ef2ed087fd5830f0eeb8854487bf8198695c081dbc0fc",BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());return bytes;}
        static PlayableWorldState Owned(byte[] saved,NavigationRetentionBudget budget)
        {
            var state=PlayableWorldState.Decode(saved);state.CaptureNavigation=new NavigationSessionState{CaptureBudget=budget};
            NavigationCaptureBytes Copy(byte[] bytes)=>NavigationCaptureBytes.CreateOwned(bytes.Length,budget,_=>(byte[])bytes.Clone());
            try{
                state.CaptureBinding=Copy(state.Binding);state.Binding=state.CaptureBinding.Bytes;state.CaptureParticipants=Copy(state.Participants);state.Participants=state.CaptureParticipants.Bytes;
                state.CaptureDomain=Copy(state.Domain);state.Domain=state.CaptureDomain.Bytes;state.CaptureNavigationBytes=Copy(state.Navigation);state.Navigation=state.CaptureNavigationBytes.Bytes;
                state.CaptureAi=Copy(state.AiAuthority);state.AiAuthority=state.CaptureAi.Bytes;state.CaptureTransport=Copy(state.RuntimeTransport);state.RuntimeTransport=state.CaptureTransport.Bytes;
                var vision=state.Vision;state.CaptureVision=NavigationCaptureVision.CreateOwned(vision.Length,budget,v=>{for(int i=0;i<vision.Length;i++)v.Set(i,Copy(vision[i]));});state.Vision=state.CaptureVision.Bytes;return state;
            }catch{state.Dispose();throw;}
        }
        static void Empty(NavigationRetentionBudget budget){Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        [Test,Explicit("Saved-state qualification requires SPACEWARS_QA_CAPTURE_STATE and an exact method filter.")] public void SavedOrdinaryEnvelopeUsesOneFinalOwnedBackingWithExactWireAndDigest()
        {
            var saved=Saved();var budget=new NavigationRetentionBudget();
            using(var state=Owned(saved,budget)){
                long stable=budget.PhysicalBytes;var owners=new[]{state.CaptureBinding,state.CaptureDomain,state.CaptureNavigationBytes,state.CaptureAi};
                var bytes=state.Encode();CollectionAssert.AreEqual(saved,bytes);CollectionAssert.AreEqual(WorldCaptureEncodingTests.Original(state),bytes);CollectionAssert.AreEqual(saved,PlayableWorldState.Decode(bytes).Encode());
                Assert.AreEqual(stable,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);foreach(var owner in owners)Assert.Greater(owner.References,0,"section owners remain retained");
            }Empty(budget);
        }
        [Test,Explicit("Saved-state qualification requires SPACEWARS_QA_CAPTURE_STATE and an exact method filter.")] public void KnownRemainingBudgetAdmitsFinalArrayAndScratchWhileOldGrowOverlapRefuses()
        {
            var saved=Saved();long available=2L*saved.Length+4*1024*1024;
            var budget=new NavigationRetentionBudget(LayeredNavigationScheduler.MaxRetainedBytes-NavigationRetentionBudget.RootBytes-available);
            using(var state=Owned(saved,budget)){
                long stable=budget.PhysicalBytes,remaining=LayeredNavigationScheduler.MaxRetainedBytes-budget.RetainedBytes;
                long lastRequest=0,lastHeld=0,lastPhysical=0,lastCommitted=0,calls=0;bool stringScratch=false;
                int stringCharge=checked(64+64+System.Text.Encoding.UTF8.GetByteCount(state.SourceIdentity));
                budget.AllocationAdmission=bytes=>{calls++;lastRequest=bytes;lastHeld=budget.HeldBytes;lastPhysical=budget.PhysicalBytes;lastCommitted=budget.CommittedBytes;if(bytes==stringCharge)stringScratch=true;return true;};
                try{
                    var write=(Action<BinaryWriter>)Delegate.CreateDelegate(typeof(Action<BinaryWriter>),state,typeof(PlayableWorldState).GetMethod("WriteEnvelope",BindingFlags.Instance|BindingFlags.NonPublic));
                    Assert.Throws<InvalidOperationException>(()=>{using(var old=WorldWire.PackOwned(write,budget)){}},"old dynamic capacity/ToArray path does not fit this unchanged cap");
                    long rejected=lastRequest,rejectedHeld=lastHeld,rejectedPhysical=lastPhysical;Assert.AreEqual(stable,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                    var bytes=state.Encode();CollectionAssert.AreEqual(saved,bytes);Assert.True(stringScratch,"fixed stream must preserve WorldWire.String admission");Assert.AreEqual(stable,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                    TestContext.Out.WriteLine($"fixed saved={saved.Length} remaining={remaining} stableSections={stable} committed={lastCommitted} oldRejectedRequest={rejected} oldHeld={rejectedHeld} oldPhysical={rejectedPhysical} callbackCalls={calls} peak={budget.PeakRetainedBytes}");
                }finally{budget.AllocationAdmission=null;}
            }Empty(budget);
        }
        [Test,Explicit("Saved-state qualification requires SPACEWARS_QA_CAPTURE_STATE and an exact method filter.")] public void FinalAllocationRefusalKeepsExistingSectionsAndReleasesAllPrivateCharges()
        {
            var saved=Saved();var budget=new NavigationRetentionBudget();
            using(var state=Owned(saved,budget)){
                long stable=budget.PhysicalBytes;var oldDomain=state.CaptureDomain;var oldNavigation=state.CaptureNavigationBytes;var oldDomainBytes=(byte[])state.Domain.Clone();var oldNavBytes=(byte[])state.Navigation.Clone();int denied=0;
                budget.AllocationAdmission=bytes=>{if(bytes>=saved.Length){denied++;return false;}return true;};
                try{Assert.Throws<InvalidOperationException>(()=>state.Encode());Assert.AreEqual(1,denied);Assert.AreSame(oldDomain,state.CaptureDomain);Assert.AreSame(oldNavigation,state.CaptureNavigationBytes);CollectionAssert.AreEqual(oldDomainBytes,state.Domain);CollectionAssert.AreEqual(oldNavBytes,state.Navigation);Assert.AreEqual(stable,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
                finally{budget.AllocationAdmission=null;}
                CollectionAssert.AreEqual(saved,state.Encode());
            }Empty(budget);
        }
        [Test] public void OwnedEnvelopeKeepsUtf8LengthSectionHashesAndStringScratchAdmission()
        {
            var plain=new PlayableWorldState{Seed=7,Generation=71,Tick=1,SourceIdentity="capture-ё-🙂",Binding=new byte[]{1},Participants=Array.Empty<byte>(),Domain=new byte[257],Navigation=new byte[]{8},AiAuthority=Array.Empty<byte>(),RuntimeTransport=Array.Empty<byte>(),Vision=new[]{new byte[]{2}}};
            var expected=WorldCaptureEncodingTests.Original(plain);var budget=new NavigationRetentionBudget();
            using(var state=Owned(expected,budget)){
                long stable=budget.PhysicalBytes;bool seenStringScratch=false;int stringCharge=checked(128+System.Text.Encoding.UTF8.GetByteCount(state.SourceIdentity));
                budget.AllocationAdmission=bytes=>{if(bytes==stringCharge)seenStringScratch=true;return true;};
                try{CollectionAssert.AreEqual(expected,state.Encode());Assert.True(seenStringScratch);Assert.AreEqual(stable,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
                finally{budget.AllocationAdmission=null;}
            }Empty(budget);
        }
    }
}
