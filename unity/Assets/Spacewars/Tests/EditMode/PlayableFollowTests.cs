using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableFollowTests
    {
        private static OfflineParticipantAuthority World(PlayableProfile profile=null,bool wall=false)
        {
            var c=OfflineParticipantAuthorityTests.Config(3);if(profile==null&&!wall)return new OfflineParticipantAuthority(c,71);profile=profile??PlayableProfile.Default;var costs=new double[c.Starts.Count,c.Starts.Count];for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)costs[i,j]=c.RouteCost(i,j);
            return new OfflineParticipantAuthority(new OfflineMatchConfiguration(profile,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),wall?c.Obstacles.Concat(new[]{new NavObstacle(-7.7,-14,-7.3,-6)}).ToArray():c.Obstacles.ToArray(),costs,c.Spectators.ToArray()),71);
        }
        private static int Spawn(OfflineParticipantAuthority a,PlayableOwner owner,PlayableEntityKind kind,NavPoint point)=>(int)OfflineParticipantAuthorityTests.Call(a,"SpawnUnit",point,owner,kind);
        private static object Unit(OfflineParticipantAuthority a,int id)=>((IDictionary)OfflineParticipantAuthorityTests.D.GetField("units",OfflineParticipantAuthorityTests.F).GetValue(OfflineParticipantAuthorityTests.Domain(a)))[id];
        private static T Field<T>(object value,string name)=>(T)value.GetType().GetField(name).GetValue(value);
        private static PlayableTacticalOrderSnapshot Order(OfflineParticipantAuthority a,int id)=>Field<PlayableTacticalOrderSnapshot>(Unit(a,id),"CurrentOrder");
        private static PlayableCommandReceipt Send(OfflineParticipantAuthority a,long seq,PlayableCommandKind kind,int[] ids,int target=0,NavPoint point=default(NavPoint),string owner="owner-11")=>a.Apply(new PlayableCommand(71,seq,owner,kind,ids,point,targetId:target).AsHuman());
        [Test] public void MixedSelfForeignAndMissingActorsPreserveRejectedProvenanceAndCyclesFailClosed()
        {
            var a=World();int leader=Spawn(a,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-10,-10)),follower=Spawn(a,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-12,-10)),ally=Spawn(a,PlayableOwner.Third,PlayableEntityKind.Tank,new NavPoint(-14,-10)),hostile=Spawn(a,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(12,12));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,1,PlayableCommandKind.Hold,new[]{leader}).Status);var stamp=Field<PlayableOrderStamp>(Unit(a,leader),"LastOrder");
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,2,PlayableCommandKind.Follow,new[]{leader,follower,ally,999999},leader).Status);
            Assert.AreSame(stamp,Field<PlayableOrderStamp>(Unit(a,leader),"LastOrder"));Assert.Null(Order(a,leader));Assert.Null(Order(a,ally));Assert.AreEqual(leader,Order(a,follower).TargetId);
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(a,3,PlayableCommandKind.Follow,new[]{leader},follower).Status);Assert.Null(Order(a,leader));
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(a,4,PlayableCommandKind.Follow,new[]{follower},hostile).Status);Assert.AreEqual(leader,Order(a,follower).TargetId);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,5,PlayableCommandKind.Follow,new[]{follower},ally).Status);Assert.AreEqual(ally,Order(a,follower).TargetId);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,6,PlayableCommandKind.Follow,new[]{ally},leader,owner:"owner-45").Status);var foreignStamp=Field<PlayableOrderStamp>(Unit(a,ally),"LastOrder");Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,6,PlayableCommandKind.Follow,new[]{follower,ally},leader).Status);Assert.AreSame(foreignStamp,Field<PlayableOrderStamp>(Unit(a,ally),"LastOrder"));
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void StationaryFollowFiresWithoutChaseAndPersistsAfterLeaderDeparture(PlayableEntityKind kind)
        {
            var a=World();int follower=Spawn(a,PlayableOwner.Player,kind,new NavPoint(-10,-10)),leader=Spawn(a,PlayableOwner.Third,PlayableEntityKind.Tank,new NavPoint(-10,-8)),enemy=Spawn(a,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-5,-10));
            // Prevent the target from shooting without changing health/weapon contracts.
            Unit(a,enemy).GetType().GetField("Reload").SetValue(Unit(a,enemy),1000d);Unit(a,leader).GetType().GetField("Reload").SetValue(Unit(a,leader),1000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,1,PlayableCommandKind.Follow,new[]{follower},leader).Status);
            OfflineParticipantAuthorityTests.Step(a,kind==PlayableEntityKind.Shkval?180:75);Assert.AreEqual(leader,Order(a,follower).TargetId);a.Navigation.Crowd.TryGet(follower,out var n);Assert.AreEqual(-10,n.Position.X,1e-8);Assert.AreEqual(-10,n.Position.Z,1e-8);Assert.Less(Field<int>(Unit(a,enemy),"Health"),PlayableProfile.Default.TankHealth);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,1,PlayableCommandKind.Move,new[]{leader},point:new NavPoint(-10,0),owner:"owner-45").Status);
            bool leaderMoved=false;for(int tick=0;tick<45;tick++){OfflineParticipantAuthorityTests.Deliver(a,tick);a.Step(1d/30);var velocity=Field<NavPoint>(Unit(a,leader),"Velocity");if(Math.Sqrt(velocity.X*velocity.X+velocity.Z*velocity.Z)>.0001){leaderMoved=true;break;}}Assert.True(leaderMoved,"Exercise actual leader velocity, not merely an accepted route or heading turn.");Assert.AreEqual(PlayableTacticalOrderKind.Follow,Order(a,follower).Kind);Assert.AreEqual(0,Field<int>(Unit(a,follower),"Target"));Assert.AreEqual(0,Field<int>(Unit(a,follower),"BurstRemaining"));
        }
        [Test] public void StaleAnswerAfterReplacementCannotReviveFollowRoute()
        {
            var a=World();int follower=Spawn(a,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-10,-10)),leader=Spawn(a,PlayableOwner.Third,PlayableEntityKind.Tank,new NavPoint(-10,-2));
            Send(a,1,PlayableCommandKind.Follow,new[]{follower},leader);a.Step(1d/30);Assert.True(a.Navigation.Requests.TryDequeue(out var request));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,2,PlayableCommandKind.Hold,new[]{follower}).Status);Assert.True(a.Navigation.Answers.TryEnqueue(new NavigationAnswer(request,new SharedFlowRouter(request.Geometry,request.Profile).FindPath(request.Start,request.Goal))));a.Step(1d/30);
            Assert.Null(Order(a,follower));a.Navigation.Crowd.TryGet(follower,out var n);Assert.True(n.Held);Assert.False(n.Moving);Assert.False(a.Navigation.IsPending(follower));
        }
        [TestCase(0)][TestCase(1)][TestCase(4)][TestCase(80)]
        public void DetachedWorldFollowMatchesEveryOwnerForSixHundredTicksAfterRestore(int cut)
        {
            var a=World();int follower=Spawn(a,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(-10,-10)),leader=Spawn(a,PlayableOwner.Third,PlayableEntityKind.Tank,new NavPoint(-10,-2));Send(a,1,PlayableCommandKind.Follow,new[]{follower},leader);
            OfflineParticipantAuthorityTests.Step(a,cut);var bytes=a.CaptureBytes();var b=OfflineParticipantAuthority.Restore((byte[])bytes.Clone(),a.Configuration);Array.Clear(bytes,0,bytes.Length);Assert.AreNotSame(a.Navigation,b.Navigation);
            for(int t=0;t<650;t++){
                if(t==160){Send(a,1,PlayableCommandKind.Move,new[]{leader},point:new NavPoint(-10,6),owner:"owner-45");Send(b,1,PlayableCommandKind.Move,new[]{leader},point:new NavPoint(-10,6),owner:"owner-45");}
                if(t==400){Send(a,2,PlayableCommandKind.Stop,new[]{follower});Send(b,2,PlayableCommandKind.Stop,new[]{follower});}
                if(t==420){Send(a,3,PlayableCommandKind.Follow,new[]{follower},leader);Send(b,3,PlayableCommandKind.Follow,new[]{follower},leader);}
                OfflineParticipantAuthorityTests.Deliver(a,t);OfflineParticipantAuthorityTests.Deliver(b,t);a.Step(1d/30);b.Step(1d/30);
                Assert.AreEqual(PlayableWorldRestoreTests.Facts(OfflineParticipantAuthorityTests.Domain(a),true),PlayableWorldRestoreTests.Facts(OfflineParticipantAuthorityTests.Domain(b),true),"authority tick "+t);
                foreach(var owner in a.Configuration.Roster)Assert.AreEqual(PlayableWorldRestoreTests.Facts(a.View(owner.Id)),PlayableWorldRestoreTests.Facts(b.View(owner.Id)),owner.Id+" tick "+t);
            }
            Assert.AreEqual(PlayableTacticalOrderKind.Follow,Order(a,follower).Kind);
        }
        [Test] public void StationaryFollowDoesNotResubmitRoutesAndAllocatedDeadLeaderRestoresBeforeCancellation()
        {
            var a=World();int follower=Spawn(a,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(-10,-10)),leader=Spawn(a,PlayableOwner.Third,PlayableEntityKind.Tank,new NavPoint(-10,-8));Send(a,1,PlayableCommandKind.Follow,new[]{follower},leader);OfflineParticipantAuthorityTests.Step(a,60);Assert.False(a.Navigation.Requests.TryDequeue(out _));
            OfflineParticipantAuthorityTests.Call(a,"Damage",leader,10000);var b=OfflineParticipantAuthority.Restore(a.CaptureBytes(),a.Configuration);Assert.AreEqual(leader,Order(b,follower).TargetId);
            a.Step(1d/30);b.Step(1d/30);Assert.Null(Order(a,follower));Assert.Null(Order(b,follower));Assert.False(a.Navigation.IsPending(follower));Assert.AreEqual(PlayableWorldRestoreTests.Facts(OfflineParticipantAuthorityTests.Domain(a),true),PlayableWorldRestoreTests.Facts(OfflineParticipantAuthorityTests.Domain(b),true));
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void FollowNeverAcquiresOrChasesEnemyOutsideNormalRange(PlayableEntityKind kind)
        {
            var a=World();int follower=Spawn(a,PlayableOwner.Player,kind,new NavPoint(-10,-10)),leader=Spawn(a,PlayableOwner.Third,PlayableEntityKind.Tank,new NavPoint(-10,-8));Spawn(a,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(12,12));Send(a,1,PlayableCommandKind.Follow,new[]{follower},leader);OfflineParticipantAuthorityTests.Step(a,90);
            Assert.AreEqual(leader,Order(a,follower).TargetId);Assert.AreEqual(0,Field<int>(Unit(a,follower),"Target"));a.Navigation.Crowd.TryGet(follower,out var n);Assert.AreEqual(-10,n.Position.X,1e-8);Assert.AreEqual(-10,n.Position.Z,1e-8);Assert.False(a.Navigation.IsPending(follower));
        }
        [Test] public void FollowContinuationIncludesPaidResearchProductionAndCaptureSettlement()
        {
            var a=World();long seq=1;
            foreach(var p in a.Configuration.Roster){var v=a.View(p.Id);var h=v.Buildings.Single(x=>x.Owner==v.Owner);Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(OfflineParticipantAuthorityTests.Send(a,seq++,p.Id,PlayableCommandKind.BuildAt,site:h.SiteId,slot:1,parent:h.Id,building:PlayableBuildingKind.ScientificCenter)).Status);}
            OfflineParticipantAuthorityTests.Step(a,6000);
            foreach(var p in a.Configuration.Roster){var v=a.View(p.Id);var h=v.Buildings.Single(x=>x.Owner==v.Owner&&x.Kind==PlayableBuildingKind.Headquarters);Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(OfflineParticipantAuthorityTests.Send(a,seq++,p.Id,PlayableCommandKind.BuildAt,site:h.SiteId,slot:2,parent:h.Id)).Status);}
            OfflineParticipantAuthorityTests.Step(a,600);
            foreach(var p in a.Configuration.Roster){var v=a.View(p.Id);var f=v.Buildings.Single(x=>x.Owner==v.Owner&&x.Kind==PlayableBuildingKind.Factory);var science=v.Buildings.Single(x=>x.Owner==v.Owner&&x.Kind==PlayableBuildingKind.ScientificCenter);Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(OfflineParticipantAuthorityTests.Send(a,seq++,p.Id,PlayableCommandKind.QueueTank,f.Id)).Status);foreach(var kind in new[]{PlayableResearchKind.TankChassis,PlayableResearchKind.ExplorerAssaultGuns,PlayableResearchKind.ShkvalGuidance})Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(OfflineParticipantAuthorityTests.Send(a,seq++,p.Id,PlayableCommandKind.QueueResearch,science.Id,research:kind)).Status);}
            var scout=a.View("owner-11").Entities.Single(x=>x.Owner==PlayableOwner.Player);int follower=Spawn(a,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(scout.Position.X,scout.Position.Z+2));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,seq++,PlayableCommandKind.Move,new[]{scout.Id},point:new NavPoint(0,0)).Status);Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,seq++,PlayableCommandKind.Follow,new[]{follower},scout.Id).Status);a.Step(1d/30);
            var bytes=a.CaptureBytes();var b=OfflineParticipantAuthority.Restore((byte[])bytes.Clone(),a.Configuration);Array.Clear(bytes,0,bytes.Length);bool settled=false;
            for(int t=0;t<3600;t++){
                OfflineParticipantAuthorityTests.Deliver(a,t);OfflineParticipantAuthorityTests.Deliver(b,t);a.Step(1d/30);b.Step(1d/30);
                Assert.AreEqual(PlayableWorldRestoreTests.Facts(OfflineParticipantAuthorityTests.Domain(a),true),PlayableWorldRestoreTests.Facts(OfflineParticipantAuthorityTests.Domain(b),true),"authority tick "+t);foreach(var p in a.Configuration.Roster)Assert.AreEqual(PlayableWorldRestoreTests.Facts(a.View(p.Id)),PlayableWorldRestoreTests.Facts(b.View(p.Id)),p.Id+" tick "+t);
                if(t>=650&&a.Configuration.Roster.All(p=>a.View(p.Id).OwnerResearch.Count==3&&a.View(p.Id).OwnerResearch.All(r=>r.Complete))){settled=true;break;}
            }
            Assert.True(settled,"Seeded obligations must actually complete after >=650 restored ticks.");foreach(var p in a.Configuration.Roster){var v=a.View(p.Id);Assert.True(v.Entities.Any(x=>x.Owner==v.Owner&&x.Kind==PlayableEntityKind.Tank));Assert.AreEqual(0,v.Buildings.Single(x=>x.Owner==v.Owner&&x.Kind==PlayableBuildingKind.Factory).QueueCount);}Assert.AreEqual(1,a.View("owner-11").Sites.Single(x=>x.Site.Id==9).Progress);Assert.AreEqual(PlayableTacticalOrderKind.Follow,Order(a,follower).Kind);
        }
        [Test] public void FollowKeepsBurstVictimAndMovementInterruptionAddsNoNewCooldown()
        {
            var a=World();int follower=Spawn(a,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(-10,-10)),leader=Spawn(a,PlayableOwner.Third,PlayableEntityKind.Tank,new NavPoint(-10,-8)),enemy=Spawn(a,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-5,-10));
            foreach(int id in new[]{leader,enemy})Unit(a,id).GetType().GetField("Reload").SetValue(Unit(a,id),1000d);
            Send(a,1,PlayableCommandKind.Follow,new[]{follower},leader);OfflineParticipantAuthorityTests.Step(a,3);Assert.Greater(Field<int>(Unit(a,follower),"BurstRemaining"),0);Assert.AreEqual(enemy,Field<int>(Unit(a,follower),"BurstTarget"));
            int nearer=Spawn(a,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-7,-10));Unit(a,nearer).GetType().GetField("Reload").SetValue(Unit(a,nearer),1000d);a.Step(1d/30);Assert.AreEqual(enemy,Field<int>(Unit(a,follower),"BurstTarget"),"Keep the valid in-progress source burst target even when another is closer.");
            // Move along existing leader heading so the next delivered route really moves.
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,1,PlayableCommandKind.Move,new[]{leader},point:new NavPoint(-5,-8),owner:"owner-45").Status);
            bool moved=false;for(int tick=0;tick<4;tick++){Assert.Greater(Field<int>(Unit(a,follower),"BurstRemaining"),0,"Exercise an active burst at movement onset.");OfflineParticipantAuthorityTests.Deliver(a,tick);a.Step(1d/30);var velocity=Field<NavPoint>(Unit(a,leader),"Velocity");if(Math.Sqrt(velocity.X*velocity.X+velocity.Z*velocity.Z)>.0001){moved=true;break;}}Assert.True(moved,"Exercise actual leader movement before its pending burst completes.");
            Assert.AreEqual(0,Field<int>(Unit(a,follower),"BurstRemaining"));Assert.AreEqual(0,Field<double>(Unit(a,follower),"Reload"),1e-9,"SOURCE removes Follow burst without inventing a pause.");
        }

        [Test] public void InvalidFollowBurstVictimClearsWithoutCooldownOrSameTickReacquisition()
        {
            var a=World();int follower=Spawn(a,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(-10,-10)),leader=Spawn(a,PlayableOwner.Third,PlayableEntityKind.Tank,new NavPoint(-10,-8)),enemy=Spawn(a,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-5,-10));
            foreach(int id in new[]{leader,enemy})Unit(a,id).GetType().GetField("Reload").SetValue(Unit(a,id),1000d);
            Send(a,1,PlayableCommandKind.Follow,new[]{follower},leader);OfflineParticipantAuthorityTests.Step(a,3);Assert.Greater(Field<int>(Unit(a,follower),"BurstRemaining"),0);int sequence=Field<int>(Unit(a,follower),"BurstSequence");
            int alternate=Spawn(a,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-7,-10));Unit(a,alternate).GetType().GetField("Reload").SetValue(Unit(a,alternate),1000d);OfflineParticipantAuthorityTests.Call(a,"Damage",enemy,10000);a.Step(1d/30);
            Assert.AreEqual(0,Field<int>(Unit(a,follower),"BurstRemaining"));Assert.AreEqual(0,Field<int>(Unit(a,follower),"Target"));Assert.AreEqual(sequence,Field<int>(Unit(a,follower),"BurstSequence"));Assert.AreEqual(0,Field<double>(Unit(a,follower),"Reload"),1e-9);
            a.Step(1d/30);Assert.AreEqual(alternate,Field<int>(Unit(a,follower),"Target"));Assert.Greater(Field<int>(Unit(a,follower),"BurstRemaining"),0);Assert.AreEqual(sequence+1,Field<int>(Unit(a,follower),"BurstSequence"));
        }

        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)]
        public void FollowLaunchCadenceIgnoresWallAdmissionWhileNativeCollisionRemainsPhysical(PlayableEntityKind kind)
        {
            var clear=World();var wall=World(wall:true);int follower=0,leader=0,enemy=0;
            foreach(var a in new[]{clear,wall}){
                follower=Spawn(a,PlayableOwner.Player,kind,new NavPoint(-10,-10));leader=Spawn(a,PlayableOwner.Third,PlayableEntityKind.Tank,new NavPoint(-10,-8));enemy=Spawn(a,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-5,-10));
                foreach(DictionaryEntry entry in (IDictionary)OfflineParticipantAuthorityTests.D.GetField("units",OfflineParticipantAuthorityTests.F).GetValue(OfflineParticipantAuthorityTests.Domain(a)))if((int)entry.Key!=follower)entry.Value.GetType().GetField("Reload").SetValue(entry.Value,1000d);
                Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,1,PlayableCommandKind.Follow,new[]{follower},leader).Status);
            }
            var allocator=OfflineParticipantAuthorityTests.D.GetField("nextProjectile",OfflineParticipantAuthorityTests.F);int initial=(int)allocator.GetValue(OfflineParticipantAuthorityTests.Domain(clear));bool completedBurst=false;
            for(int tick=0;tick<90;tick++){
                clear.Step(1d/30);wall.Step(1d/30);Assert.AreEqual(allocator.GetValue(OfflineParticipantAuthorityTests.Domain(clear)),allocator.GetValue(OfflineParticipantAuthorityTests.Domain(wall)),"Launch allocator before collision, tick "+tick);
                foreach(string field in new[]{"BurstRemaining","BurstSequence","Reload","BurstDelay"})Assert.AreEqual(Unit(clear,follower).GetType().GetField(field).GetValue(Unit(clear,follower)),Unit(wall,follower).GetType().GetField(field).GetValue(Unit(wall,follower)),field+" tick "+tick);
                completedBurst|=kind==PlayableEntityKind.Explorer&&Field<int>(Unit(wall,follower),"BurstSequence")>0&&Field<int>(Unit(wall,follower),"BurstRemaining")==0&&Field<double>(Unit(wall,follower),"Reload")>0;
            }
            Assert.Greater((int)allocator.GetValue(OfflineParticipantAuthorityTests.Domain(wall)),initial);if(kind==PlayableEntityKind.Explorer)Assert.True(completedBurst,"Complete an actual burst and enter its normal pause.");
            Assert.AreEqual(PlayableProfile.Default.TankHealth,Field<int>(Unit(wall,enemy),"Health"),"Existing native static collision is retained; source damage-through-wall parity remains OPEN.");Assert.Less(Field<int>(Unit(clear,enemy),"Health"),PlayableProfile.Default.TankHealth);
            wall.Navigation.Crowd.TryGet(follower,out var nav);Assert.AreEqual(-10,nav.Position.X,1e-8);Assert.AreEqual(-10,nav.Position.Z,1e-8);Assert.AreEqual(PlayableTacticalOrderKind.Follow,Order(wall,follower).Kind);Assert.False(wall.Navigation.Requests.TryDequeue(out _));
        }

        [Test] public void ShortSourceWaypointSettlesWithoutRequestChurnAndRestoresUntilGoalOrPositionChanges()
        {
            var d=PlayableFollowProfileTests.Data();d.followArrivalTolerance=.05;var a=World(PlayableProfile.Create(d));int follower=Spawn(a,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(-12.1,-8)),leader=Spawn(a,PlayableOwner.Third,PlayableEntityKind.Tank,new NavPoint(-10,-8));Send(a,1,PlayableCommandKind.Follow,new[]{follower},leader);OfflineParticipantAuthorityTests.Step(a,30);
            a.Navigation.Crowd.TryGet(follower,out var unit);Assert.AreEqual(-12.1,unit.Position.X,1e-8);Assert.AreEqual(NavigationOutcome.Arrived,unit.Outcome);Assert.False(a.Navigation.Requests.TryDequeue(out _));var b=OfflineParticipantAuthority.Restore(a.CaptureBytes(),a.Configuration);
            for(int t=0;t<650;t++){a.Step(1d/30);b.Step(1d/30);Assert.AreEqual(PlayableWorldRestoreTests.Facts(OfflineParticipantAuthorityTests.Domain(a),true),PlayableWorldRestoreTests.Facts(OfflineParticipantAuthorityTests.Domain(b),true));}Assert.False(a.Navigation.Requests.TryDequeue(out _));Assert.False(b.Navigation.Requests.TryDequeue(out _));
            // A physical displacement must invalidate the unchanged-goal shortcut.
            typeof(NavUnit).GetProperty("Position").SetValue(unit,new NavPoint(-12.3,-8));a.Step(1d/30);Assert.True(a.Navigation.Requests.TryDequeue(out var displaced));Assert.AreEqual(follower,displaced.Entity);
            // A changed leader goal must also issue a fresh real Follow route.
            Send(b,1,PlayableCommandKind.Move,new[]{leader},point:new NavPoint(-5,-8),owner:"owner-45");OfflineParticipantAuthorityTests.Deliver(b,0);b.Step(1d/30);b.Step(1d/30);Assert.True(b.Navigation.Requests.TryDequeue(out var changed));Assert.AreEqual(follower,changed.Entity);
        }
    }
}
