using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Spacewars.Simulation.Ai;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationColdQueueRestoreTests
    {
        [Test,Explicit("Postopening6000 diagnostic requires SPACEWARS_QA_POSTOPENING_STATE and exact filter.")]
        public void SavedPostOpening6000DirectRestoreReportsAdmissionAndCleanup()
        {
            var bytes=File.ReadAllBytes(Environment.GetEnvironmentVariable("SPACEWARS_QA_POSTOPENING_STATE"));Assert.AreEqual(10691329,bytes.Length);
            using(var h=SHA256.Create())Assert.AreEqual("7114a1185cdb2ab2f82a7c61f3a52930504bdd7f5ba6900098158008623d502f",BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new MossHighlandMap());var roster=Enumerable.Range(0,4).Select(i=>new OfflineParticipant("foundry-"+(i+1),i+1,i/2+1,OfflineControl.Ai)).ToArray();
            var config=((MossHighlandMap)profile.AuthoredMap).Configuration(profile,7108,roster);config=(OfflineMatchConfiguration)typeof(NativeAiFourPlayerEconomyTests).GetMethod("WithReadyHome",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{config});
            var budget=new NavigationRetentionBudget();PlayableAuthorityTick world=null;long request=0,held=0,physical=0,committed=0,calls=0,peak=0;
            budget.AllocationAdmission=amount=>{calls++;request=amount;held=budget.HeldBytes;physical=budget.PhysicalBytes;committed=budget.CommittedBytes;peak=Math.Max(peak,amount+held+physical+committed);return true;};
            try{world=PlayableAuthorityTick.RestoreBytes(bytes,config.Profile,config.Seed,config.SourceIdentity,config,AiProfile.Initial,roster.Select(p=>p.Id),budget);Assert.AreEqual(6000,world.Tick);}
            finally{budget.AllocationAdmission=null;TestContext.Out.WriteLine($"post6000 request={request} held={held} physical={physical} committed={committed} calls={calls} requestedPeak={peak} admittedPeak={budget.PeakRetainedBytes}");if(world!=null){world.Stop();world.Domain.Navigation.DestroyNavigationAuthority();}TestContext.Out.WriteLine($"post6000 cleanupPhysical={budget.PhysicalBytes} cleanupHeld={budget.HeldBytes} cleanupScopes={budget.ActiveScopes}");Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        }
        [Test,Explicit("Exact postopening6000 recapture requires SPACEWARS_QA_POSTOPENING_STATE and exact filter.")]
        public void SavedPostOpening6000RestoresAndPublicRecapturesExactly()
        {
            var bytes=File.ReadAllBytes(Environment.GetEnvironmentVariable("SPACEWARS_QA_POSTOPENING_STATE"));Assert.AreEqual(10691329,bytes.Length);
            using(var h=SHA256.Create())Assert.AreEqual("7114a1185cdb2ab2f82a7c61f3a52930504bdd7f5ba6900098158008623d502f",BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new MossHighlandMap());var roster=Enumerable.Range(0,4).Select(i=>new OfflineParticipant("foundry-"+(i+1),i+1,i/2+1,OfflineControl.Ai)).ToArray();
            var config=((MossHighlandMap)profile.AuthoredMap).Configuration(profile,7108,roster);config=(OfflineMatchConfiguration)typeof(NativeAiFourPlayerEconomyTests).GetMethod("WithReadyHome",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{config});
            var budget=new NavigationRetentionBudget();PlayableAuthorityTick world=null;
            try{world=PlayableAuthorityTick.RestoreBytes(bytes,config.Profile,config.Seed,config.SourceIdentity,config,AiProfile.Initial,roster.Select(p=>p.Id),budget);CollectionAssert.AreEqual(bytes,world.CaptureBytes(),"whole-world public recapture after private catalog validation");}
            finally{if(world!=null){world.Stop();world.Domain.Navigation.DestroyNavigationAuthority();}Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        }
        [Test,Explicit("Durable Moss checkpoint6000 requires exact filter and SPACEWARS_QA_MOSS_STATE.")]
        public void SavedMoss6000WithUnretiredCancelledChaseRemainsRejected()
        {
            var bytes=File.ReadAllBytes(Environment.GetEnvironmentVariable("SPACEWARS_QA_MOSS_STATE"));
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new MossHighlandMap());
            var roster=Enumerable.Range(0,4).Select(i=>new OfflineParticipant("foundry-"+(i+1),i+1,i/2+1,OfflineControl.Ai)).ToArray();
            var config=((MossHighlandMap)profile.AuthoredMap).Configuration(profile,7108,roster);
            Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(bytes,config),"historical producer defect must not weaken queue validation");
        }
    }
}
