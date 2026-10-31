using System;
using System.Linq;
using Spacewars.Presentation;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class RecastTerminalCoordinationTests
    {
        private static NavLocation At(double x,double z,string surface=NavLocation.FlatSurface)=>new NavLocation(new NavPoint(x,z),surface);
        [Test] public void UnequalBranchesAndOffRouteEndpointKeepAProgressingSlowestMember()
        {
            var shortBranch=new NavUnit(1,new NavPoint(0,0),.72,2,4.5,false);shortBranch.ReplaceRoute(new[]{new NavPoint(0,8)});
            var longBranch=new NavUnit(2,new NavPoint(0,0),.72,4,4.5,false);longBranch.ReplaceRoute(new[]{new NavPoint(0,12)});
            // Both old traveled cursors may have saturated at unequal lengths.
            // The short member is physically displaced eight metres from its slot.
            double shortProgress=GroupMarch.RemainingCoordinate(shortBranch),longProgress=GroupMarch.RemainingCoordinate(longBranch);
            Assert.AreEqual(-8,shortProgress);Assert.AreEqual(-12,longProgress);
            double minimum=Math.Min(shortProgress,longProgress),dt=1d/30;
            Assert.AreEqual(2,GroupMarch.MaxSpeed(4,2,longProgress,minimum,4,dt));
            Assert.AreEqual(0,GroupMarch.MaxSpeed(2,2,shortProgress,minimum,4,dt));
            longBranch.Position=new NavPoint(0,1);minimum=Math.Min(GroupMarch.RemainingCoordinate(shortBranch),GroupMarch.RemainingCoordinate(longBranch));
            Assert.AreEqual(2,GroupMarch.MaxSpeed(2,2,GroupMarch.RemainingCoordinate(shortBranch),minimum,4,dt),"Rear movement releases the waiting terminal branch without changing stretch or speed.");
            longBranch.ReplaceRoute(new[]{new NavPoint(0,1),new NavPoint(3,1),new NavPoint(3,5)});longBranch.routeIndex=1;
            Assert.AreEqual(-7,GroupMarch.RemainingCoordinate(longBranch),"Consumed origin legs do not enter the pacing coordinate.");
        }
        [Test] public void OnlyPhysicalAssignedSlotOnItsLayerQualifiesAndDisplacementOrHoldRevokesIt()
        {
            var geometry=new NavGeometry(30,Array.Empty<NavObstacle>(),1);var goal=At(10,0);
            var actor=new NavUnit(1,new NavPoint(9.9,0),.72,2,4.5,false){Location=At(9.9,0),Goal=goal.Position,GoalLocation=goal};
            bool Eligible()=>NavigationSession.RecastAssignedTerminal(actor,goal,geometry,null,.15);
            Assert.True(Eligible());actor.Position=new NavPoint(5,0);actor.Location=At(5,0);Assert.False(Eligible(),"Retained Arrived alone is insufficient after displacement.");
            actor.Position=new NavPoint(9.9,0);actor.Location=At(9.9,0,"other-floor");Assert.False(Eligible());
            actor.Location=At(9.9,0);actor.Held=true;Assert.False(Eligible());actor.Held=false;
            actor.Goal=new NavPoint(12,0);actor.GoalLocation=At(12,0);Assert.False(Eligible(),"A replacement goal cannot inherit old-slot protection.");
        }
        [Test] public void OwnedSlotCompletionDoesNotCompletePeerAndReplacementDetachesInstalledIdentity()
        {
            var geometry=new NavGeometry(30,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            try{
                session.ConfigureMarch(4,PlayableProfile.Default.Revision);
                session.Crowd.Add(1,new NavPoint(-10,-2),.72,2,4.5);
                session.Crowd.Add(2,new NavPoint(-10,2),.72,2,4.5);
                Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(10,-2),new NavPoint(10,2)}));
                using(var host=new UnityHostRouteService()){
                    var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                    for(int i=0;i<80&&session.PendingCount>0;i++){host.Service(binding,8);session.ApplyResults();}
                }
                Assert.AreEqual(2,session.AppliedResults);
                session.UsesRecastGroundRoutes=true;
                var first=session.Crowd.Units.Single(x=>x.Id==1);var peer=session.Crowd.Units.Single(x=>x.Id==2);
                first.Position=first.Goal;first.Location=first.GoalLocation.Value;
                session.Step(1d/30);
                Assert.AreEqual(NavigationOutcome.Arrived,first.Outcome);Assert.False(first.Moving);
                Assert.True(peer.Moving);Assert.AreEqual(NavigationOutcome.Moving,peer.Outcome);
                long oldGroup=session.GroupFor(1).GroupId;
                Assert.True(session.MoveGroup(new[]{1},new[]{new NavPoint(12,-2)}));
                Assert.Null(session.InstalledExecutionFor(1));
                Assert.AreNotEqual(oldGroup,session.GroupFor(1).GroupId);
                Assert.AreEqual(1,session.PendingCount,"Replacement remains pending, with no installed identity eligible for terminal protection.");
            }finally{session.ReleaseCommittedHostRoot();}
        }
        [Test] public void ProducerClearanceStillIncludesTerminalAndHeldBodies()
        {
            var bodies=new[]{new NavMotionBody{Id=1,Position=new NavPoint(0,0),Radius=.72,TerminalSeed=true},new NavMotionBody{Id=2,Position=new NavPoint(1.5,0),Radius=.72,TerminalSeed=true}};
            var queue=new int[2];var marks=new int[2];
            Assert.True(ProducerClearanceRules.TryChain(bodies,0,new NavPoint(.1,0),queue,marks,(row,end)=>true,out int count));
            Assert.AreEqual(2,count,"Individual terminal protection must not obstruct the separate production clearance contract.");
        }
    }
}
