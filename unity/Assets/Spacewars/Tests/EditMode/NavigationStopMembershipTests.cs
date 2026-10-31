using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationStopMembershipTests
    {
        static NavigationSession Create()
        {
            var s=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            for(int id=1;id<=3;id++)s.Crowd.Add(id,new NavPoint(-5,(id-2)*3));
            var command=new PlayableCommand(1,7,"player-1",PlayableCommandKind.Move,new[]{1,2,3},new NavPoint(5,0),origin:PlayableOrderOrigin.Human)
                .WithResolvedLocation(new NavLocation(new NavPoint(5,0),"flat"));
            s.PrepareCommandGroup(command,4,12,1);Assert.True(s.MoveGroup(new[]{1,2,3},new[]{new NavPoint(5,-3),new NavPoint(5,0),new NavPoint(5,3)}));s.FinishCommandGroup(true,new[]{1,2,3});return s;
        }
        static void Destroy(NavigationSession s)
        {s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);}
        static byte[] Bytes(NavigationSession s){using(var state=s.CaptureOwnedState())return WorldWire.Pack(w=>WorldWire.Write(w,state));}
        static PlayableCommand Command(GroupOrderState original,int id,long sequence=0)
            =>new PlayableCommand(1,sequence==0?original.CommandSequence:sequence,"player-1",original.Kind,new[]{id},original.OriginalGoal,origin:original.Origin)
                .WithResolvedLocation(original.OriginalLocation.Value);
        static void RebindStop(NavigationSession s,GroupOrderState original,int id,bool hold)
        {
            s.PrepareCommandGroup(Command(original,id),original.OrderRevision,20,1,original.GroupId,original.OrderRevision,original.IssuedTick);
            bool applied=false;try{applied=s.TryStopGroup(new[]{id},hold);Assert.True(applied);}finally{s.FinishCommandGroup(applied,new[]{id});}
        }
        static void AssertMembership(NavigationSession s,long group,int[] expected)
        {
            foreach(int id in expected){Assert.True(s.HasGroupMember(id));Assert.AreEqual(group,s.GroupFor(id).GroupId);CollectionAssert.AreEqual(expected,s.GroupFor(id).Members.Select(m=>m.Entity));}
            var slots=(NavigationCommandSlots<long,GroupOrderState>)typeof(NavigationSession).GetField("groups",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(s);
            var owner=(NavigationLiveGroup)slots.OwnerFor(group);
            CollectionAssert.AreEqual(expected,owner.ImmutableVersion.Members.Select(m=>((NavigationAuthorityMember)m).Data.Entity),"immutable ownership agrees with map and live members");
            Assert.AreEqual(expected.Length,s.GroupFor(expected[0]).Members.Select(m=>m.Slot).Distinct().Count(),"slots stay unique");
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(s),s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);
        }
        [TestCase(false)][TestCase(true)]
        public void SameIssuanceStopsPreservePeersOverlapAndExactColdState(bool hold)
        {
            var s=Create();try{
                var original=s.GroupFor(1);
                foreach(int id in new[]{1,2,1}){
                    var before=s.GroupFor(1);var peers=before.Members.Where(m=>m.Entity!=id).ToDictionary(m=>m.Entity,m=>new NavigationAuthorityMember.Values(m));
                    RebindStop(s,original,id,hold);AssertMembership(s,original.GroupId,new[]{1,2,3});var after=s.GroupFor(1);
                    Assert.Greater(after.MembershipRevision,before.MembershipRevision);Assert.AreEqual(original.CommandSequence,after.CommandSequence);Assert.AreEqual(original.OrderRevision,after.OrderRevision);Assert.AreEqual(original.MarchVersion,after.MarchVersion);Assert.AreEqual(original.MarchProfileRevision,after.MarchProfileRevision);Assert.AreEqual(original.MarchStretch,after.MarchStretch);Assert.False(after.MarchInitialized);Assert.Zero(after.MarchAnchor);
                    foreach(var peer in peers)Assert.True(peer.Value.BitwiseSame(new NavigationAuthorityMember.Values(after.Members.Single(m=>m.Entity==peer.Key))),"unselected member state is preserved");
                    Assert.True(s.Crowd.TryGet(id,out var actor));Assert.AreEqual(hold,actor.Held);
                    var bytes=Bytes(s);var cold=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
                    try{cold.RestoreState(WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState));AssertMembership(cold,original.GroupId,new[]{1,2,3});CollectionAssert.AreEqual(bytes,Bytes(cold),"current exact save/restore");}finally{Destroy(cold);}
                }
                // This is the former throw boundary; all mapped actors remain addressable.
                foreach(int id in new[]{1,2,3})s.SetFormationContact(id,true,21);
            }finally{Destroy(s);}
        }
        [Test] public void NewIssuanceStillSeparatesOnlyItsRecipients()
        {
            var s=Create();try{
                long old=s.GroupFor(1).GroupId;var command=new PlayableCommand(1,8,"player-1",PlayableCommandKind.Stop,new[]{1},origin:PlayableOrderOrigin.Human);
                s.PrepareCommandGroup(command,5,20,1);Assert.True(s.TryStopGroup(new[]{1},false));s.FinishCommandGroup(true,new[]{1});
                Assert.AreNotEqual(old,s.GroupFor(1).GroupId);AssertMembership(s,old,new[]{2,3});AssertMembership(s,s.GroupFor(1).GroupId,new[]{1});Assert.AreEqual(2,s.GroupOrders.Count);
            }finally{Destroy(s);}
        }
        [Test] public void ConflictingIssuanceCannotOverwriteSurvivingPeers()
        {
            var s=Create();try{
                var original=s.GroupFor(1);var bytes=Bytes(s);
                s.PrepareCommandGroup(Command(original,1,99),original.OrderRevision,20,1,original.GroupId,original.OrderRevision,original.IssuedTick);
                try{var error=Assert.Throws<InvalidOperationException>(()=>s.TryStopGroup(new[]{1},false));StringAssert.Contains("Issuance collision",error.Message);}finally{s.FinishCommandGroup(false,new[]{1});}
                AssertMembership(s,original.GroupId,new[]{1,2,3});CollectionAssert.AreEqual(bytes,Bytes(s),"rejected private preparation has no authority effects");
            }finally{Destroy(s);}
        }
    }
}
