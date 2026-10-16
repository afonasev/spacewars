using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationPrivateDecodeTests
    {
        static void Empty(NavigationRetentionBudget budget){Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        [Test,Explicit("Saved actual-world continuation requires exact filter and durable checkpoint.")]
        public void Saved1665RecaptureAndOneTickContinuationAreExact()
        {
            var saved=File.ReadAllBytes(Environment.GetEnvironmentVariable("SPACEWARS_QA_CAPTURE_STATE"));
            var config=(OfflineMatchConfiguration)typeof(NativeAiEconomyTests).GetMethod("RichEconomyConfig",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            PlayableAuthorityTick Restore()=>PlayableAuthorityTick.RestoreBytes(saved,config.Profile,config.Seed,config.SourceIdentity,config,Spacewars.Simulation.Ai.AiProfile.Initial,config.Roster.Where(p=>p.Control==OfflineControl.Ai).Select(p=>p.Id));
            PlayableAuthorityTick a=null,b=null;
            try{a=Restore();CollectionAssert.AreEqual(saved,a.CaptureBytes(),"immediate cold recapture");b=Restore();void Advance(PlayableAuthorityTick world){using(var host=new UnityHostRouteService()){for(int i=0;i<512;i++){if(world.TryAdvance()){Assert.AreEqual(1666,world.Tick);Assert.IsNull(world.Latest.Failure);return;}host.Service(world,64);}Assert.Fail("saved checkpoint one-tick route barrier");}}
                Advance(a);Advance(b);Assert.AreEqual(a.Tick,b.Tick);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes(),"one actual authority continuation");}
            finally{foreach(var world in new[]{a,b})if(world!=null){var budget=world.Domain.Navigation.PhysicalHostBudget;world.Stop();world.Domain.Navigation.DestroyNavigationAuthority();Empty(budget);}}
        }
        static byte[] SmallHost()
        {
            var a=new PlayableAuthorityTick(OfflineParticipantAuthorityTests.Config(2),71);
            try{return a.Domain.Navigation.CaptureState().HostAuthority;}
            finally{a.Stop();a.Domain.Navigation.DestroyNavigationAuthority();}
        }
        [TestCase("missing")][TestCase("swapped")][TestCase("foreign")][TestCase("same-ledger-other-transaction")][TestCase("disposed")]
        public void TrustedDecodeRejectsInvalidPrivateOwnerBeforeNestedAdmission(string defect)
        {
            var budget=new NavigationRetentionBudget();var otherBudget=defect=="same-ledger-other-transaction"?budget:new NavigationRetentionBudget();
            using(var transaction=new NavigationDecodeTransaction(budget))using(var other=new NavigationDecodeTransaction(otherBudget)){
                var owner=transaction.OwnValue(36,()=>new byte[]{1,2,3,4});var state=new NavigationSessionState{HostAuthority=owner.Value,DecodeHostOwner=owner};
                if(defect=="missing")state.DecodeHostOwner=null;
                if(defect=="swapped")state.HostAuthority=(byte[])owner.Value.Clone();
                if(defect=="foreign"||defect=="same-ledger-other-transaction"){state.DecodeHostOwner=other.OwnValue(36,()=>new byte[]{1,2,3,4});state.HostAuthority=state.DecodeHostOwner.Value;}
                if(defect=="disposed")transaction.Dispose();
                int calls=0;long before=budget.PhysicalBytes,held=budget.HeldBytes;budget.AllocationAdmission=_=>{calls++;return true;};
                try{Assert.Throws<ArgumentException>(()=>NavigationSession.DecodeDetachedHostAuthority(state,transaction,out _,out _,out _));Assert.Zero(calls);Assert.AreEqual(before,budget.PhysicalBytes);Assert.AreEqual(held,budget.HeldBytes);}
                finally{budget.AllocationAdmission=null;}
            }Empty(budget);if(!ReferenceEquals(budget,otherBudget))Empty(otherBudget);
        }
        [Test] public void PublicHostDecodeClonesBeforeCallerMutation()
        {
            var input=SmallHost();var expected=(byte[])input.Clone();var budget=new NavigationRetentionBudget();NavigationHostRoot host=null;NavigationAuthorityOwners authority=null;bool mutated=false;
            budget.AllocationAdmission=bytes=>{if(bytes==64+128L*512){input[0]^=127;mutated=true;}return true;};
            try{NavigationSession.DecodeDetachedHostAuthority(input,budget,out host,out authority);Assert.True(mutated);Assert.AreNotEqual(expected[0],input[0]);Assert.NotNull(host);}
            finally{budget.AllocationAdmission=null;host?.Release();authority?.Release();}Empty(budget);
        }
        [Test] public void NestedReconstructionRefusalRetainsPriorBytesAndReleasesTemporaryOwners()
        {
            var input=SmallHost();var expected=(byte[])input.Clone();var budget=new NavigationRetentionBudget();
            using(var transaction=new NavigationDecodeTransaction(budget)){
                var owner=transaction.OwnValue(32L+input.Length,()=>(byte[])input.Clone());var state=new NavigationSessionState{HostAuthority=owner.Value,DecodeHostOwner=owner};
                long physical=budget.PhysicalBytes,held=budget.HeldBytes;int refs=owner.References,denied=0;
                budget.AllocationAdmission=bytes=>{if(bytes==64+128L*512){denied++;return false;}return true;};
                try{Assert.Throws<InvalidOperationException>(()=>NavigationSession.DecodeDetachedHostAuthority(state,transaction,out _,out _,out _));Assert.AreEqual(1,denied);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(held,budget.HeldBytes);Assert.AreEqual(refs,owner.References);CollectionAssert.AreEqual(expected,state.HostAuthority);}
                finally{budget.AllocationAdmission=null;}
                NavigationSession.DecodeDetachedHostAuthority(state,transaction,out var host,out var authority,out _);host?.Release();authority?.Release();Assert.AreEqual(physical,budget.PhysicalBytes);
            }Empty(budget);
        }
        [Test] public void NestedBodyReconstructionRefusalReleasesPartialGraphAndRetainsPrivateInput()
        {
            var input=SmallHost();var expected=(byte[])input.Clone();var budget=new NavigationRetentionBudget();
            using(var transaction=new NavigationDecodeTransaction(budget)){
                var owner=transaction.OwnValue(32L+input.Length,()=>(byte[])input.Clone());var state=new NavigationSessionState{HostAuthority=owner.Value,DecodeHostOwner=owner};
                long physical=budget.PhysicalBytes,held=budget.HeldBytes;int refs=owner.References,afterScratch=0;bool inBody=false;long targetRequest=0;
                budget.AllocationAdmission=bytes=>{if(inBody&&++afterScratch==8)targetRequest=bytes;if(bytes==64+128L*512)inBody=true;return true;};
                try{NavigationSession.DecodeDetachedHostAuthority(state,transaction,out var host,out var authority,out _);host?.Release();authority?.Release();Assert.GreaterOrEqual(afterScratch,8);Assert.Greater(targetRequest,0);}
                finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(held,budget.HeldBytes);
                afterScratch=0;inBody=false;int denied=0;long partialPhysical=0;
                budget.AllocationAdmission=bytes=>{if(inBody&&++afterScratch==8){partialPhysical=budget.PhysicalBytes;denied++;return false;}if(bytes==64+128L*512)inBody=true;return true;};
                try{Assert.Throws<InvalidOperationException>(()=>NavigationSession.DecodeDetachedHostAuthority(state,transaction,out _,out _,out _));Assert.AreEqual(1,denied);Assert.Greater(partialPhysical,physical,"BODY reconstruction already owns a partial graph");Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(held,budget.HeldBytes);Assert.AreEqual(refs,owner.References);CollectionAssert.AreEqual(expected,state.HostAuthority);TestContext.Out.WriteLine($"targetBodyAllocation=8 request={targetRequest} partialPhysical={partialPhysical} priorPhysical={physical}");}
                finally{budget.AllocationAdmission=null;}
            }Empty(budget);
        }
        [Test,Explicit("Saved actual-world checkpoint requires SPACEWARS_QA_CAPTURE_STATE and exact filter.")]
        public void Saved1665RestoresWithoutSecondPrivateBodyClone()
        {
            var saved=File.ReadAllBytes(Environment.GetEnvironmentVariable("SPACEWARS_QA_CAPTURE_STATE"));
            using(var h=SHA256.Create())Assert.AreEqual("407894fe3aabb4076f0ef2ed087fd5830f0eeb8854487bf8198695c081dbc0fc",BitConverter.ToString(h.ComputeHash(saved)).Replace("-","").ToLowerInvariant());
            var config=(OfflineMatchConfiguration)typeof(NativeAiEconomyTests).GetMethod("RichEconomyConfig",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            PlayableAuthorityTick authority=null;var budget=new NavigationRetentionBudget();long requested=0,held=0,physical=0,committed=0,calls=0,peak=0;
            budget.AllocationAdmission=bytes=>{calls++;requested=bytes;held=budget.HeldBytes;physical=budget.PhysicalBytes;committed=budget.CommittedBytes;peak=Math.Max(peak,physical+held+committed+bytes);return true;};
            try{
                authority=PlayableAuthorityTick.RestoreBytes(saved,config.Profile,config.Seed,config.SourceIdentity,config,Spacewars.Simulation.Ai.AiProfile.Initial,config.Roster.Where(p=>p.Control==OfflineControl.Ai).Select(p=>p.Id),budget);
                Assert.AreEqual(1665,authority.Tick);
            }finally{
                budget.AllocationAdmission=null;TestContext.Out.WriteLine($"requested={requested} held={held} physical={physical} committed={committed} calls={calls} observedPeak={peak} ledgerPeak={budget.PeakRetainedBytes}");
                if(authority!=null){authority.Stop();authority.Domain.Navigation.DestroyNavigationAuthority();}TestContext.Out.WriteLine($"cleanupPhysical={budget.PhysicalBytes} cleanupHeld={budget.HeldBytes} cleanupScopes={budget.ActiveScopes}");Empty(budget);
            }
        }
    }
}
