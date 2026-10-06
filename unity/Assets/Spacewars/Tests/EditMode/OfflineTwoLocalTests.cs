using System;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class OfflineTwoLocalTests
    {
        private static OfflineMatchConfiguration Config(bool allies=false)=>SourceFlatFixture.Load(string.Join("\n",new[]{"fixture"}.Concat(OfflineParticipantAuthorityTests.Rows().Select(r=>string.Join("|",r)))),PlayableProfile.Default,allies);
        private static LocalSeat[] Seats(string device2="gamepad:17")=>new[]{OfflineLocalSession.Bind("stable-keyboard","owner-11","keyboard+mouse:1:2"),OfflineLocalSession.Bind("stable-pad","owner-28",device2)};
        private static void Ready(OfflineLocalSession s){foreach(var seat in s.Seats){s.SampleDevice(seat.Id,true,true);s.Ready(seat.Id);}Assert.True(s.Resume());}
        private static void Stop(PlayableRuntime r){r.RequestStop();for(int i=0;i<100&&!r.IsStopped;i++)Thread.Sleep(10);Assert.True(r.IsStopped);}
        [TestCase(false)][TestCase(true)] public void ExactTwoSeatGenesisPositionsMatchExecutedSourceProjection(bool allies)
        {
            var c=Config(allies);var authority=new OfflineParticipantAuthority(c,71);
            var folder=new DirectoryInfo(Directory.GetCurrentDirectory());while(folder!=null&&!File.Exists(Path.Combine(folder.FullName,"unity/Tests/Fixtures/native-two-local-human/source-genesis.tsv")))folder=folder.Parent;Assert.NotNull(folder);
            var rows=File.ReadAllLines(Path.Combine(folder.FullName,"unity/Tests/Fixtures/native-two-local-human/source-genesis.tsv")).Skip(1).Select(line=>line.Split('|')).Where(row=>row[0]==(allies?"allies":"opponents")).ToArray();Assert.AreEqual(4,rows.Length);
            foreach(var row in rows){var view=authority.View(row[1]);NavPoint position=row[2]=="headquarters"?view.Buildings.Single(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Headquarters).Position:view.Entities.Single(e=>e.Owner==view.Owner&&e.Kind.ToString().ToLowerInvariant()==row[2]).Position;
                Assert.AreEqual(double.Parse(row[3],System.Globalization.CultureInfo.InvariantCulture),position.X);Assert.AreEqual(double.Parse(row[4],System.Globalization.CultureInfo.InvariantCulture),position.Z);
            }
        }
        [Test] public void DuplicateDevicesAndOwnersFailClosed()
        {
            var r=new PlayableRuntime(Config(),71,true);try{Assert.Throws<ArgumentException>(()=>new OfflineLocalSession(r,Seats("keyboard+mouse:1:2")));Assert.Throws<ArgumentException>(()=>new OfflineLocalSession(r,Seats("invented-device")));Assert.Throws<ArgumentException>(()=>new OfflineLocalSession(r,new[]{Seats()[0],OfflineLocalSession.Bind("other","owner-11","gamepad:1")}));}finally{Stop(r);}
        }
        [Test] public void DisconnectHeldReconnectRequiresNeutralAndDeliberateResume()
        {
            var r=new PlayableRuntime(Config(),71,true);try{
                using(var s=new OfflineLocalSession(r,Seats())){
                    Assert.False(s.Resume());Ready(s);var unit=s.Frame.Views["owner-28"].Entities.Single(e=>e.Owner==s.Frame.Views["owner-28"].Owner).Id;
                    s.SampleDevice("stable-pad",false,false);Assert.True(s.Paused);Assert.AreEqual("stable-pad",s.WaitingSeat);
                    s.SampleDevice("stable-keyboard",true,true);s.SampleDevice("stable-pad",true,false);
                    Assert.False(s.Resume());Assert.AreEqual("stable-pad",s.WaitingSeat);Assert.AreEqual(PlayableCommandStatus.Rejected,s.Submit("stable-pad",PlayableCommandKind.Move,new[]{unit},new NavPoint(0,0)).Status);
                    s.SampleDevice("stable-pad",true,true);s.Ready("stable-pad");Assert.True(s.Paused);Assert.True(s.Resume());
                    s.ReadFrame();Assert.AreEqual(0,s.Frame.Receipts.Count);
                }
            }finally{Stop(r);}
        }
        [TestCase(false)][TestCase(true)] public void SimultaneousEqualSequencesFanOutOnceWithOneCoherentTick(bool allies)
        {
            var r=new PlayableRuntime(Config(allies),71,true);try{using(var s=new OfflineLocalSession(r,Seats())){
                Ready(s);foreach(var seat in s.Seats){var view=s.Frame.Views[seat.OwnerId];var own=view.Entities.Single(e=>e.Owner==view.Owner);Assert.True(s.Submit(seat.Id,PlayableCommandKind.Hold,new[]{own.Id}).Accepted);}
                var receipts=new System.Collections.Generic.List<PlayableCommandReceipt>();
                for(int i=0;i<100&&receipts.Count<2;i++){Thread.Sleep(10);s.ReadFrame();foreach(var v in s.Frame.Views.Values){Assert.AreEqual(s.Frame.Tick,v.Tick);Assert.AreEqual(s.Frame.Sequence,v.Sequence);Assert.AreEqual(s.Frame.Generation,v.Generation);Assert.AreEqual(s.Frame.Views.First().Value.Paused,v.Paused);}foreach(var owner in s.OwnerReceipts.Keys){Assert.True(s.OwnerReceipts[owner].All(x=>x.OwnerId==owner));receipts.AddRange(s.OwnerReceipts[owner]);}}
                Assert.AreEqual(2,receipts.Count);Assert.True(receipts.All(r=>r.Sequence==1&&r.Status==PlayableCommandStatus.Applied));CollectionAssert.AreEquivalent(new[]{"owner-11","owner-28"},receipts.Select(x=>x.OwnerId));
                s.ReadFrame();Assert.True(s.OwnerReceipts.Values.All(x=>x.Count==0));
                var foreign=s.Frame.Views["owner-28"].Entities.Single(e=>e.Owner==s.Frame.Views["owner-28"].Owner);
                Assert.AreEqual(PlayableCommandStatus.InvalidEntity,s.Submit("stable-keyboard",PlayableCommandKind.Move,new[]{foreign.Id},new NavPoint(0,0)).Status);
            }}finally{Stop(r);}
        }
        [TestCase(false)][TestCase(true)] public void OwnerPrivateStateAndPersonalSharedMapStaySeparated(bool allies)
        {
            var c=Config(allies);var authority=new OfflineParticipantAuthority(c,71);var views=c.Roster.Take(2).Select(p=>authority.View(p.Id)).ToArray();
            var hq=views[1].Buildings.Single(b=>b.Owner==views[1].Owner&&b.Kind==PlayableBuildingKind.Headquarters);
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(71,1,"owner-28",PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:hq.SiteId,slotId:1,parentId:hq.Id,buildingKind:PlayableBuildingKind.Factory)).Status);
            OfflineParticipantAuthorityTests.Step(authority,600);views=c.Roster.Take(2).Select(p=>authority.View(p.Id)).ToArray();var factory=views[1].Buildings.Single(b=>b.Owner==views[1].Owner&&b.Kind==PlayableBuildingKind.Factory);
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(71,2,"owner-28",PlayableCommandKind.QueueExplorer,new[]{factory.Id})).Status);
            views=c.Roster.Take(2).Select(p=>authority.View(p.Id)).ToArray();Assert.NotNull(views[1].Buildings.Single(b=>b.Id==factory.Id).PrivateState);
            foreach(var b in views[0].Buildings.Where(b=>b.Owner!=views[0].Owner))Assert.Null(b.PrivateState);
            foreach(var e in views[0].Entities.Where(e=>e.Owner!=views[0].Owner)){Assert.Null(e.CurrentOrder);Assert.Null(e.OrderStamp);Assert.False(e.Held);Assert.AreEqual(0,e.TargetId);}
            CollectionAssert.AreEquivalent(views[1].Entities.Where(e=>e.Owner==views[1].Owner).Select(e=>e.Id),PlayableMapView.SelectOwnUnits(views[1],new NavPoint(-100,-100),new NavPoint(100,100)));
            var personal=OfflineMapProjection.Personal(views[0]);Assert.True(personal.All(m=>views[0].Entities.Any(e=>e.Id==m.Id)||views[0].Buildings.Any(b=>b.Id==m.Id)||views[0].Vision.KnownBuildings.Any(b=>b.Id==m.Id)));
            CollectionAssert.AreEquivalent(new[]{"Id","Owner","Position","Kind","State"},typeof(OfflineMapMarker).GetProperties().Select(p=>p.Name));
            var r=new PlayableRuntime(c,71,true);try{var shared=OfflineMapProjection.Shared(r.OfflineFrame,new[]{"owner-11","owner-28"});Assert.True(shared.Any(m=>m.Owner==PlayableOwner.Player));Assert.True(shared.Any(m=>m.Owner==PlayableOwner.Enemy));}finally{Stop(r);}
        }
        [TestCase(false)][TestCase(true)] public void IndependentTwoSeatWorldAndEachOwnerRestoreThroughSettlement(bool allies)
        {
            var c=Config(allies);var a=new OfflineParticipantAuthority(c,71);foreach(var p in c.Roster.Take(2)){var v=a.View(p.Id);var hq=v.Buildings.Single(b=>b.Owner==v.Owner&&b.Kind==PlayableBuildingKind.Headquarters);Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,p.Id,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:hq.SiteId,slotId:1,parentId:hq.Id,buildingKind:PlayableBuildingKind.Factory)).Status);}
            OfflineParticipantAuthorityTests.Step(a,600);foreach(var p in c.Roster.Take(2)){var v=a.View(p.Id);var factory=v.Buildings.Single(b=>b.Owner==v.Owner&&b.Kind==PlayableBuildingKind.Factory);Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,2,p.Id,PlayableCommandKind.QueueExplorer,new[]{factory.Id})).Status);}
            var bytes=a.CaptureBytes();var b=OfflineParticipantAuthority.Restore((byte[])bytes.Clone(),c);Array.Clear(bytes,0,bytes.Length);
            Assert.AreNotSame(OfflineParticipantAuthorityTests.Domain(a),OfflineParticipantAuthorityTests.Domain(b));
            var folder=new DirectoryInfo(Directory.GetCurrentDirectory());while(folder!=null&&!Directory.Exists(Path.Combine(folder.FullName,"unity/Tests/Fixtures/native-two-local-human")))folder=folder.Parent;Assert.NotNull(folder);
            {
            for(int tick=0;tick<3600;tick++){
                OfflineParticipantAuthorityTests.Deliver(a,tick);OfflineParticipantAuthorityTests.Deliver(b,tick);a.Step(1d/30);b.Step(1d/30);
                var original=PlayableWorldRestoreTests.Facts(OfflineParticipantAuthorityTests.Domain(a),true);var restored=PlayableWorldRestoreTests.Facts(OfflineParticipantAuthorityTests.Domain(b),true);Assert.AreEqual(original,restored,"authority tick "+tick);
                foreach(var p in c.Roster){original=PlayableWorldRestoreTests.Facts(a.View(p.Id));restored=PlayableWorldRestoreTests.Facts(b.View(p.Id));Assert.AreEqual(original,restored,"owner "+p.Id+" tick "+tick);}
                if(tick>=600&&c.Roster.Take(2).All(p=>a.View(p.Id).Buildings.Where(x=>x.Owner==a.View(p.Id).Owner&&x.Kind==PlayableBuildingKind.Factory).All(x=>x.QueueCount==0))){foreach(var p in c.Roster.Take(2))Assert.AreEqual(2,a.View(p.Id).Entities.Count(e=>e.Owner==a.View(p.Id).Owner));return;}
            }Assert.Fail("Seeded production must settle after >=600 independent ticks.");}
        }
    }
}
