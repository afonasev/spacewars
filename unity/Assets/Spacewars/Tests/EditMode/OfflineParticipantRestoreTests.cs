using System;
using System.Linq;
using System.Reflection;
using System.IO;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class OfflineParticipantRestoreTests
    {
        private static string Facts(object value,bool authority=false)=>PlayableWorldRestoreTests.Facts(value,authority);
        [TestCase(2,0)][TestCase(4,450)][TestCase(8,1000)]
        public void DetachedBytesMatchEveryTickAndOwnerThroughSeededSettlement(int owners,int cut)
        {
            var config=OfflineParticipantAuthorityTests.Config(owners);var a=new OfflineParticipantAuthority(config,71);long seq=1;
            foreach(var p in config.Roster){var view=a.View(p.Id);var hq=view.Buildings.Single(b=>b.Owner==view.Owner);Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(OfflineParticipantAuthorityTests.Send(a,seq++,p.Id,PlayableCommandKind.BuildAt,site:hq.SiteId,slot:1,parent:hq.Id,building:PlayableBuildingKind.ScientificCenter)).Status);}
            // Real construction and income fund the later orders; no injected wallets/readiness.
            OfflineParticipantAuthorityTests.Step(a,6000);
            foreach(var p in config.Roster){var view=a.View(p.Id);var hq=view.Buildings.Single(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Headquarters);Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(OfflineParticipantAuthorityTests.Send(a,seq++,p.Id,PlayableCommandKind.BuildAt,site:hq.SiteId,slot:2,parent:hq.Id)).Status);}
            OfflineParticipantAuthorityTests.Step(a,600);
            foreach(var p in config.Roster){var view=a.View(p.Id);int factory=view.Buildings.Single(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Factory).Id;int center=view.Buildings.Single(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.ScientificCenter).Id;Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(OfflineParticipantAuthorityTests.Send(a,seq++,p.Id,PlayableCommandKind.QueueTank,factory)).Status);
                foreach(PlayableResearchKind kind in new[]{PlayableResearchKind.TankChassis,PlayableResearchKind.ExplorerAssaultGuns,PlayableResearchKind.ShkvalGuidance})Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(OfflineParticipantAuthorityTests.Send(a,seq++,p.Id,PlayableCommandKind.QueueResearch,center,research:kind)).Status);
            }
            // Capture/navigation run in the same authority as paid research and production.
            var scout=a.View(config.Roster[0].Id).Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(OfflineParticipantAuthorityTests.Send(a,seq++,config.Roster[0].Id,PlayableCommandKind.Move,scout.Id,new NavPoint(0,0))).Status);
            OfflineParticipantAuthorityTests.Step(a,cut);
            var bytes=a.CaptureBytes();var b=OfflineParticipantAuthority.Restore((byte[])bytes.Clone(),config);Array.Clear(bytes,0,bytes.Length);
            Assert.AreNotSame(OfflineParticipantAuthorityTests.Domain(a),OfflineParticipantAuthorityTests.Domain(b));Assert.AreNotSame(a.Navigation,b.Navigation);
            var evidence=new DirectoryInfo(Directory.GetCurrentDirectory());while(evidence!=null&&!Directory.Exists(Path.Combine(evidence.FullName,"unity/Tests/Fixtures/native-offline-participant-authority")))evidence=evidence.Parent;Assert.NotNull(evidence);
            {
            for(int tick=0;tick<3600;tick++){
                OfflineParticipantAuthorityTests.Deliver(a,tick);OfflineParticipantAuthorityTests.Deliver(b,tick);a.Step(1d/30);b.Step(1d/30);
                var authorityA=Facts(OfflineParticipantAuthorityTests.Domain(a),true);var authorityB=Facts(OfflineParticipantAuthorityTests.Domain(b),true);Assert.AreEqual(authorityA,authorityB,"full authority tick "+tick);
                foreach(var p in config.Roster){var viewA=Facts(a.View(p.Id));var viewB=Facts(b.View(p.Id));Assert.AreEqual(viewA,viewB,"owner "+p.Id+" tick "+tick);}
                if(tick>=600&&config.Roster.All(p=>a.View(p.Id).OwnerResearch.Count==3&&a.View(p.Id).OwnerResearch.All(r=>r.Complete))){
                    foreach(var p in config.Roster){var v=a.View(p.Id);Assert.AreEqual(2,v.Entities.Count(e=>e.Owner==v.Owner));Assert.True(v.Entities.Any(e=>e.Owner==v.Owner&&e.Kind==PlayableEntityKind.Tank));Assert.AreEqual(0,v.Buildings.Single(x=>x.Owner==v.Owner&&x.Kind==PlayableBuildingKind.Factory).QueueCount);}
                    Assert.AreEqual(1,a.View(config.Roster[0].Id).Sites.Single(x=>x.Site.Id==9).Progress);return;
                }
            }
            Assert.Fail("Seeded research/production/capture must settle, not merely survive 600 ticks.");
            }
        }
    }
}
