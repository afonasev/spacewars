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
        private static void WithDonorFixture(bool terminal,bool blockedDonor,bool direct,bool knowledgeMismatch,int count,Action<RecastAttachmentPreparation,NavigationRequest[],RecastMemberRoute[],RecastSpineProposal,PartitionNavigationProvider,RecastAuthorityGeometry,INavStorageReservation> check)
        {
            var map=PlayableProfile.RecastGroupCorridorDefault.AuthoredMap;var p=PlayableProfile.RecastGroupCorridorDefault.Navigation.ForUnit(.58,7,7.5);var obstacles=map.MovementBlockers.Concat(direct?Array.Empty<NavObstacle>():new[]{new NavObstacle(16,-2,18,2)}).ToArray();var geometry=new NavGeometry(map.HalfExtent,obstacles,map.Revision);var input=RecastAuthorityGeometry.Prepare(map,geometry,p);var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,81,0,out var scope));using(scope){var r=scope.OwnedReservation();var provider=PartitionNavigationProvider.CopyOwned(new PartitionNavigationProvider(map,geometry,""),r);RecastAttachmentPreparation prep=null;
                try{NavLocation At(NavPoint point)=>new NavLocation(point,"bank/east");var a=new NavPoint(terminal?12:14,0);var b=new NavPoint(terminal?14:12,0);var starts=new[]{terminal?new NavPoint(12,-4):new NavPoint(20,0),terminal?new NavPoint(12,4):blockedDonor?new NavPoint(14,6):new NavPoint(20,10),new NavPoint(22,-2)};var goals=new[]{terminal?new NavPoint(20,0):new NavPoint(12,-4),terminal?(blockedDonor?new NavPoint(14,6):new NavPoint(20,10)):new NavPoint(12,4),new NavPoint(12,6)};
                    var members=Enumerable.Range(0,count).Select(i=>new NavigationRequest(81,i+1,i+1,1,p,geometry,starts[i],goals[i],heldIdentity:knowledgeMismatch&&i==1?"different-current-knowledge":"",startLocation:At(starts[i]),goalLocation:At(goals[i]))).ToArray();double fan=members.Max(q=>Math.Sqrt(Math.Pow(q.Goal.X-b.X,2)+Math.Pow(q.Goal.Z-b.Z,2)))+p.Radius;
                    var spine=new RecastSpineProposal(new[]{new NavTypedLeg(At(a),At(b),2,NavTypedLegKind.Surface,null)},new string('a',64),input.Binding,1,new[]{0,1},b,fan);var baseline=members.Select(q=>RecastGroupPreparation.Attach(provider,q,spine,input)).ToArray();Assert.NotNull(baseline[1],"The donor must be an actual directly certified complete baseline");if(!direct)Assert.IsNull(baseline[0]);prep=RecastAttachmentPreparation.Create(count,r);check(prep,members,baseline,spine,provider,input,r);
                }finally{prep?.Dispose();((INavStorageFacade)provider).StorageRoot.Release();}scope.Discard();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        private static void CompleteAttachment(RecastAttachmentPreparation prep,NavigationRequest[] members,RecastMemberRoute[] baseline,RecastSpineProposal spine,PartitionNavigationProvider provider,RecastAuthorityGeometry input,INavStorageReservation r)
        {
            for(int step=0;step<100000&&!prep.Complete;step++){long before=prep.Work;prep.Step(members,baseline,spine,provider,input,r,256,null);Assert.LessOrEqual(prep.Work-before,256);}
            Assert.True(prep.Complete,"Bounded preparation must terminate");
            foreach(var path in prep.WorkingRoutes.Where(x=>x!=null)){var at=path.Legs[0].From;foreach(var leg in path.Legs){Assert.AreEqual(at,leg.From);Assert.True(members[0].Geometry.SegmentFree(at.Position,leg.To.Position,members[0].Profile.Radius));Assert.True(provider.TryTraverse(at,leg.To.Position,new NavClearanceProfile("ground",1,"ground",0,members[0].Profile.Radius),out var reached));Assert.AreEqual(leg.To,reached);at=reached;}}
        }
        [TestCase(false)][TestCase(true)] public void DirectMemberDonorAvoidsFieldAndPublishesCompleteSameSideBend(bool terminal)
        {
            WithDonorFixture(terminal,false,false,false,2,(prep,members,baseline,spine,provider,input,r)=>{CompleteAttachment(prep,members,baseline,spine,provider,input,r);var path=prep.WorkingRoutes[0];Assert.NotNull(path);Assert.AreSame(baseline[1],prep.WorkingRoutes[1]);Assert.True(path.Waypoints.Any(p=>p.Location.Position.Equals(terminal?members[1].Goal:members[1].Start)));Assert.AreEqual(members[0].Goal,path.Waypoints.Last().Location.Position);Assert.Less(prep.Work,1000,"Three-point reuse must avoid even the old field reset work");});
        }
        [Test] public void DirectPreferenceAndFailedDonorGridFallbackRemainIntact()
        {
            WithDonorFixture(false,false,true,false,2,(prep,members,baseline,spine,provider,input,r)=>{Assert.NotNull(baseline[0]);CompleteAttachment(prep,members,baseline,spine,provider,input,r);Assert.AreSame(baseline[0],prep.WorkingRoutes[0]);});
            WithDonorFixture(false,true,false,false,2,(prep,members,baseline,spine,provider,input,r)=>{CompleteAttachment(prep,members,baseline,spine,provider,input,r);Assert.NotNull(prep.WorkingRoutes[0]);Assert.Greater(prep.Work,1000,"An obstructed donor must leave the original field fallback active");Assert.Greater(prep.WorkingRoutes[0].OriginLegs,2);});
        }
        [Test] public void ReusedRecipientCannotBecomeRecursiveDonorAndKnowledgeMismatchCannotBorrow()
        {
            WithDonorFixture(false,false,false,false,3,(prep,members,baseline,spine,provider,input,r)=>{Assert.IsNull(baseline[2]);CompleteAttachment(prep,members,baseline,spine,provider,input,r);Assert.NotNull(prep.WorkingRoutes[2]);Assert.True(prep.WorkingRoutes[2].Waypoints.Any(p=>p.Location.Position.Equals(members[1].Start)));Assert.False(prep.WorkingRoutes[2].Waypoints.Any(p=>p.Location.Position.Equals(members[0].Start)));});
            WithDonorFixture(false,false,false,true,2,(prep,members,baseline,spine,provider,input,r)=>{CompleteAttachment(prep,members,baseline,spine,provider,input,r);Assert.NotNull(typeof(RecastAttachmentPreparation).GetField("field",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(prep),"Rejected donor must retain the independently qualified grid fallback");if(prep.WorkingRoutes[0]!=null)Assert.Greater(prep.WorkingRoutes[0].OriginLegs,2,"Different knowledge cannot borrow the three-point donor branch");});
        }
        [Test] public void DonorPreparationCancellationKeepsRecipientUnpublishedAndReleasesPaidOwners()
        {
            WithDonorFixture(false,false,false,false,2,(prep,members,baseline,spine,provider,input,r)=>{var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var phase=typeof(RecastAttachmentPreparation).GetField("stage",flags);var branch=typeof(RecastAttachmentPreparation).GetField("branch",flags);for(int i=0;i<1000&&phase.GetValue(prep).ToString()!="PeerCopy";i++){long work=prep.Work;prep.Step(members,baseline,spine,provider,input,r,4,null);Assert.LessOrEqual(prep.Work-work,4);}Assert.AreEqual("PeerCopy",phase.GetValue(prep).ToString());Assert.NotNull(branch.GetValue(prep),"New paid donor payload must exist at cancellation");Assert.False(prep.Complete);Assert.IsNull(prep.WorkingRoutes[0]);Assert.Throws<OperationCanceledException>(()=>prep.Step(members,baseline,spine,provider,input,r,256,()=>true));Assert.False(prep.Complete);Assert.IsNull(prep.WorkingRoutes[0]);});
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
