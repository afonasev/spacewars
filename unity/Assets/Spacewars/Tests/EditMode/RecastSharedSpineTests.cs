using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode
{
    public sealed class RecastSharedSpineTests
    {
        internal static NavigationSession Ready(out NavGeometry geometry)
        {
            var profile=PlayableProfile.RecastGroupCorridorDefault;var map=profile.AuthoredMap;
            geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var session=new NavigationSession(81,geometry,profile.Navigation,null,map){UsesRecastGroundRoutes=true};
            session.ConfigureMarch(4,profile.Revision);
            session.Crowd.Add(1,new NavPoint(-31,-46),.72,2,4.5,canYield:true);
            session.Crowd.Add(2,new NavPoint(-31,-43),.72,4,4.5,canYield:true);
            Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(-46,-46),new NavPoint(-46,-43)}));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
                for(int i=0;i<200&&session.AppliedResults<2;i++){host.Service(binding,8);session.ApplyResults();}
            }
            Assert.AreEqual(2,session.AppliedResults);return session;
        }
        [Test] public void PaidPublicationRetainsOneTypedSpineAndAcceptedConnectorSections()
        {
            var s=Ready(out _);try{
                var a=s.InstalledExecutionFor(1).Corridor;var b=s.InstalledExecutionFor(2).Corridor;
                Assert.NotNull(a.RecastPlan);Assert.AreSame(a.RecastPlan.Spine,b.RecastPlan.Spine);
                var p=a.RecastPlan;Assert.Greater(p.Spine.Legs.Count,0);
                Assert.True(a.Legs.Take(p.OriginLegs).All(l=>l.Section==NavCorridorSection.IndividualConnector));
                Assert.True(a.Legs.Skip(p.OriginLegs).Take(p.Exit-p.Join).All(l=>l.Section==NavCorridorSection.SharedTrunk));
                Assert.True(a.Legs.Skip(p.TerminalLeg).All(l=>l.Section==NavCorridorSection.TerminalConnector));
                Assert.GreaterOrEqual(p.Exit,p.Join);Assert.Greater(p.Spine.Station(p.Exit),p.Spine.Station(p.Join));
                var p2=b.RecastPlan;Assert.AreEqual(p.Spine.Binding,p2.Spine.Binding);
                Assert.Greater(p.Spine.References,1,"Shared immutable decision has real retained owning edges.");
            }finally{s.ReleaseCommittedHostRoot();}
        }
        [Test] public void CommonStationIgnoresUnrelatedConnectorLengthsAndUsesExistingStretch()
        {
            Assert.AreEqual(2,GroupMarch.MaxSpeed(4,2,0,0,4,1d/30));
            Assert.AreEqual(0,GroupMarch.MaxSpeed(4,2,4,0,4,1d/30));
            Assert.AreEqual(2,GroupMarch.MaxSpeed(4,2,4,.1,4,1d/30));
            // Origin and terminal movement are not compared by their branch lengths.
            var s=Ready(out _);try{s.Step(1d/30);foreach(var actor in s.Crowd.Units){var member=s.GroupFor(actor.Id).Members.Single(m=>m.Entity==actor.Id);Assert.Greater(member.Recast.Request,0);Assert.AreEqual(actor.InstalledRequest,member.Recast.Request);}}finally{s.ReleaseCommittedHostRoot();}
        }
    }
}
