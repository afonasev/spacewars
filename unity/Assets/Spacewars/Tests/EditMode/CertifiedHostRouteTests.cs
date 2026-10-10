using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CertifiedHostRouteTests
    {
        [Test] public void ZeroEntityAndZeroOrderProbeKeepCertifiedProvenanceAcrossCheckpoint()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(0,new NavPoint(-5,0));
            Assert.True(source.Move(0,new NavPoint(5,0)));
            Assert.NotNull(source.Probe(7,0,PlayableProfile.Default.Navigation,
                new NavPoint(-4,2),new NavPoint(4,2)));
            var bytes=WorldWire.Pack(w=>WorldWire.Write(w,source.CaptureState()));
            var state=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
            var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),
                PlayableProfile.Default.Navigation);
            restored.RestoreState(state);
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,restored.Admission,restored.RoutePort);
                for(int i=0;i<40;i++){host.Service(binding,4);restored.ApplyResults();}
            }
            Assert.AreEqual(1,restored.AppliedResults);
            Assert.AreEqual(0,restored.InstalledExecutionFor(0).Corridor.Entity);
            Assert.True(restored.TryProbeAnswer(out var probe));
            Assert.AreEqual(NavRouteProvenance.ProducerProbe,probe.Corridor.Provenance);
            Assert.AreEqual(0,probe.Corridor.Order);
        }

        [Test] public void OldNullMemberIdentityUsesExplicitLegacyCorridorFallback()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(0,new NavPoint(-5,0));
            Assert.True(source.Move(0,new NavPoint(5,0)));
            var state=source.CaptureState();
            Assert.AreEqual(1,state.Requests.Length);
            state.Requests[0].MemberIdentity=null; // supported pre-A1 checkpoint mode
            var bytes=WorldWire.Pack(w=>WorldWire.Write(w,state));
            var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),
                PlayableProfile.Default.Navigation);
            restored.RestoreState(WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,restored.Admission,restored.RoutePort);
                for(int i=0;i<40&&restored.PendingCount>0;i++){host.Service(binding,4);restored.ApplyResults();}
            }
            Assert.AreEqual(1,restored.AppliedResults);
            Assert.Null(restored.InstalledExecutionFor(0));
            Assert.AreEqual(0,restored.CaptureState().InstalledExecutions.Single().Corridor.Group);
            var replay=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),
                PlayableProfile.Default.Navigation);
            replay.RestoreState(WorldWire.Unpack(WorldWire.Pack(w=>WorldWire.Write(w,restored.CaptureState())),
                WorldWire.ReadNavigationSessionState));
            Assert.AreEqual(NavigationOutcome.Moving,replay.Crowd.Units.Single().Outcome);
        }

        [Test] public void MalformedCertifiedBindingCannotActivateReplacement()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(session.Move(1,new NavPoint(5,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                for(int i=0;i<40&&session.PendingCount>0;i++){host.Service(binding,4);session.ApplyResults();}
            }
            Assert.AreEqual(1,session.AppliedResults);
            var prior=session.InstalledExecutionFor(1);
            Assert.NotNull(prior);
            int activated=0;session.RouteActivated+=_=>activated++;
            Assert.True(session.Move(1,new NavPoint(7,1)));
            Assert.True(session.Requests.TryDequeue(out var request));
            var group=session.GroupFor(1);
            var origin=request.StartLocation.Value;var endpoint=request.GoalLocation.Value;
            var leg=new NavCorridorLeg(origin,endpoint,NavTypedLegKind.Surface,null,
                Math.Sqrt(Math.Pow(origin.Position.X-endpoint.Position.X,2)+
                    Math.Pow(origin.Position.Z-endpoint.Position.Z,2)),
                NavCorridorSection.IndividualConnector,0,0);
            var forged=new NavCorridorDescriptor(1,new string('0',64),new string('0',64),
                NavigationAdmission.ExactProfileBinding(request.Profile),NavLocation.FlatSurface,
                request.HeldIdentity,request.BaseGeometry.Revision,request.Profile.Radius,request.Profile.GridCell,
                NavRouteProvenance.CommandMember,request.Session,group.GroupId,group.OrderRevision,
                group.CommandSequence,group.IssuedTick,request.MemberIdentity.OrderRevision,
                request.Entity,request.MemberIdentity.Incarnation,request.MemberIdentity.MobilityRevision,
                request.Request,request.Order,origin,endpoint,new[]{leg});
            Assert.True(session.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal},
                NavSolveStatus.Ready,forged)));
            session.ApplyResults();
            Assert.AreEqual(0,activated);
            Assert.AreEqual(1,session.AppliedResults);
            Assert.True(session.Crowd.Units.Single().Moving);
            Assert.AreEqual(prior.Corridor.Request,session.InstalledExecutionFor(1).Corridor.Request);
        }

        [Test] public void TamperedReadyMailboxCorridorIsRejectedBeforeRestorePublication()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(source.Move(1,new NavPoint(5,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,source.Admission,source.RoutePort);
                for(int i=0;i<40&&source.Answers.Count==0;i++)host.Service(binding,4);
            }
            var state=source.CaptureState();
            Assert.AreEqual(1,state.AnswerMailbox.Length);
            Assert.NotNull(state.AnswerMailbox[0].Corridor);
            state.AnswerMailbox[0].Route=new[]{new NavPoint(1,1)};
            var target=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),
                PlayableProfile.Default.Navigation);
            Assert.Throws<ArgumentException>(()=>target.RestoreState(state));
        }

        [Test] public void StrippedCertifiedSidecarCannotActivateThroughPublicAnswerMailbox()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(session.Move(1,new NavPoint(5,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                for(int i=0;i<40&&session.Answers.Count==0;i++)host.Service(binding,4);
            }
            Assert.True(session.Answers.TryDequeue(out var certified));
            Assert.NotNull(certified.Corridor);
            Assert.True(session.Answers.TryEnqueue(new NavigationAnswer(certified.Request,
                certified.CopyRoute(),NavSolveStatus.Ready)));
            session.ApplyResults();
            Assert.AreEqual(0,session.AppliedResults);
            Assert.False(session.Crowd.Units.Single().Moving);
            Assert.Null(session.InstalledExecutionFor(1));
        }

        [Test] public void HostWorkQuantumWaitsForAuthorityRetentionGate()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(session.Move(1,new NavPoint(5,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                var held=new ManualResetEventSlim(false);
                var hostCalling=new ManualResetEventSlim(false);
                var worker=Task.Run(()=>session.RoutePort.RunHostStep(()=>{
                    held.Set();
                    Assert.True(hostCalling.Wait(TimeSpan.FromSeconds(5)));
                    Thread.Sleep(100); // Give the main-thread host a chance to contend.
                    Assert.AreEqual(1,session.Requests.Count);
                }));
                Assert.True(held.Wait(TimeSpan.FromSeconds(5)));
                hostCalling.Set();
                host.Service(binding,4);
                Assert.True(worker.Wait(TimeSpan.FromSeconds(10)));
                for(int i=0;i<40&&session.PendingCount>0;i++){host.Service(binding,4);session.ApplyResults();}
                Assert.AreEqual(1,session.AppliedResults);
            }
        }

        [Test] public void ProbeDequeueKeepsCorridorRetentionUnderAuthorityGate()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            Assert.NotNull(session.Probe(7,0,PlayableProfile.Default.Navigation,
                new NavPoint(-4,2),new NavPoint(4,2)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                for(int i=0;i<40&&session.CorridorRetainedBytes==0;i++){host.Service(binding,4);session.ApplyResults();}
                Assert.Greater(session.CorridorRetainedBytes,0);
                long before=session.CorridorRetainedBytes;
                var started=new ManualResetEventSlim(false);
                var finished=new ManualResetEventSlim(false);
                NavigationAnswer dequeued=null;bool received=false;
                Task worker=null;
                session.RoutePort.RunHostStep(()=>{
                    worker=Task.Run(()=>{started.Set();received=session.TryProbeAnswer(out dequeued);finished.Set();});
                    Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
                    Assert.False(finished.Wait(TimeSpan.FromMilliseconds(100)));
                    Assert.AreEqual(before,session.CorridorRetainedBytes);
                });
                Assert.True(worker.Wait(TimeSpan.FromSeconds(10)));
                Assert.True(received);Assert.NotNull(dequeued.Corridor);
                Assert.Less(session.CorridorRetainedBytes,before);
            }
        }

        [Test] public void HostClearReleasesNearCapReservationAfterOldWorldIsCleared()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(session.Move(1,new NavPoint(5,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                host.Service(binding,1);
                long reserved=LayeredNavigationScheduler.MaxRetainedBytes-1024;
                session.RoutePort.SetHostRetainedBytes(reserved);
                var held=new ManualResetEventSlim(false);
                var clearing=new ManualResetEventSlim(false);
                var worker=Task.Run(()=>session.RoutePort.RunHostStep(()=>{
                    held.Set();Assert.True(clearing.Wait(TimeSpan.FromSeconds(5)));
                    Thread.Sleep(100);
                    Assert.AreEqual(reserved,session.HostRetainedBytes);
                }));
                Assert.True(held.Wait(TimeSpan.FromSeconds(5)));
                clearing.Set();
                host.Dispose(); // The main-thread disposal waits for the old authority gate.
                Assert.True(worker.Wait(TimeSpan.FromSeconds(10)));
                Assert.Zero(session.HostRetainedBytes);
            }
        }

        [Test] public void ReadyCheckpointRejectsChangedActiveRoot()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(source.Move(1,new NavPoint(5,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,source.Admission,source.RoutePort);
                for(int i=0;i<40&&source.Answers.Count==0;i++)host.Service(binding,4);
            }
            var state=source.CaptureState();
            Assert.NotNull(state.AnswerMailbox.Single().Corridor);
            state.Groups.Single().CommandSequence++;
            Assert.Throws<ArgumentException>(()=>new NavigationSession(1,
                new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation)
                .RestoreState(state));
        }

        [Test] public void ReboundPortalIdCannotExposeInstalledAuthoredCorridor()
        {
            var map=ThreeCrossingsMap.Default;
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=PlayableProfile.ThreeCrossingsDefault;
            var source=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
            source.Crowd.Add(1,new NavPoint(-18,0),.58,4,4.5);
            Assert.True(source.Move(1,new NavPoint(18,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,profile,source.Admission,source.RoutePort);
                for(int i=0;i<40&&source.PendingCount>0;i++){host.Service(binding,8);source.ApplyResults();}
            }
            var state=source.CaptureState();
            var original=state.InstalledExecutions.Single().Corridor;
            Assert.True(original.Legs.Any(l=>l.Kind==NavTypedLegKind.Portal));
            bool changed=false;
            var legs=original.Legs.Select(l=>{
                if(l.Kind!=NavTypedLegKind.Portal||changed)return l;
                changed=true;return new NavCorridorLeg(l.From,l.To,l.Kind,new string('0',64),l.Cost,
                    l.Section,l.Branch,l.DownstreamMergeLeg);
            }).ToArray();
            state.InstalledExecutions[0].Corridor=new NavCorridorDescriptor(original.Version,
                original.GraphKey,original.ProviderBinding,original.ProfileBinding,original.SurfaceProviderId,
                original.HoldBinding,original.TopologyRevision,original.Radius,original.GridCell,
                original.Provenance,original.World,original.Group,original.RootRevision,
                original.CommandSequence,original.IssuedTick,original.ActivationRevision,original.Entity,
                original.Incarnation,original.Mobility,original.Request,original.Order,
                original.Origin,original.Endpoint,legs);
            Assert.Throws<ArgumentException>(()=>new NavigationSession(1,
                new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),profile.Navigation,
                terrain:map).RestoreState(state));
        }

        [Test] public void ProfileRebindDetachesOldCorridorWhilePreservingMotionCheckpoint()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(source.Move(1,new NavPoint(5,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,source.Admission,source.RoutePort);
                for(int i=0;i<40&&source.PendingCount>0;i++){host.Service(binding,4);source.ApplyResults();}
            }
            Assert.NotNull(source.InstalledExecutionFor(1));
            var changed=new NavigationProfile("profile-after-install",2,.72,4,4.5,.8,.15,3.6,.08);
            source.Rebind(changed);
            Assert.Null(source.InstalledExecutionFor(1));
            Assert.True(source.Crowd.Units.Single().Moving);
            var state=WorldWire.Unpack(WorldWire.Pack(w=>WorldWire.Write(w,source.CaptureState())),
                WorldWire.ReadNavigationSessionState);
            var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),changed);
            restored.RestoreState(state);
            Assert.True(restored.Crowd.Units.Single().Moving);
            Assert.True(restored.CaptureState().InstalledExecutions.Single().Detached);
            source.Step(1d/30);restored.Step(1d/30);
            Assert.AreEqual(source.Crowd.Units.Single().Position,restored.Crowd.Units.Single().Position);
        }

        [Test] public void ArrivedCorridorRestoresWithoutReactivatingMarch()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(1,new NavPoint(-2,0));
            Assert.True(source.Move(1,new NavPoint(2,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,source.Admission,source.RoutePort);
                for(int i=0;i<40&&source.PendingCount>0;i++){host.Service(binding,4);source.ApplyResults();}
            }
            for(int i=0;i<240&&source.Crowd.Units.Single().Moving;i++)source.Step(1d/30);
            Assert.AreEqual(NavigationOutcome.Arrived,source.Crowd.Units.Single().Outcome);
            var state=WorldWire.Unpack(WorldWire.Pack(w=>WorldWire.Write(w,source.CaptureState())),
                WorldWire.ReadNavigationSessionState);
            var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),
                PlayableProfile.Default.Navigation);
            restored.RestoreState(state);
            Assert.AreEqual(NavigationOutcome.Arrived,restored.Crowd.Units.Single().Outcome);
            Assert.Null(restored.InstalledExecutionFor(1));
            Assert.AreEqual(source.Crowd.Units.Single().Position,restored.Crowd.Units.Single().Position);
        }

        [Test] public void HistoricalArrivedCorridorSurvivesLaterAlliedHold()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(1,new NavPoint(-2,0));
            source.Crowd.Add(2,new NavPoint(-2,4));
            Assert.True(source.Move(1,new NavPoint(2,0)));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,source.Admission,source.RoutePort);
                for(int i=0;i<40&&source.PendingCount>0;i++){host.Service(binding,4);source.ApplyResults();}
            }
            for(int i=0;i<240&&source.Crowd.Units.Single(u=>u.Id==1).Moving;i++)source.Step(1d/30);
            Assert.AreEqual(NavigationOutcome.Arrived,source.Crowd.Units.Single(u=>u.Id==1).Outcome);
            source.Stop(2,true);
            var state=WorldWire.Unpack(WorldWire.Pack(w=>WorldWire.Write(w,source.CaptureState())),
                WorldWire.ReadNavigationSessionState);
            var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),
                PlayableProfile.Default.Navigation);
            restored.RestoreState(state);
            Assert.AreEqual(NavigationOutcome.Arrived,restored.Crowd.Units.Single(u=>u.Id==1).Outcome);
            Assert.Null(restored.InstalledExecutionFor(1));
            Assert.AreEqual(source.Crowd.Units.Single(u=>u.Id==1).Position,
                restored.Crowd.Units.Single(u=>u.Id==1).Position);
        }

        [Test] public void VersionFiveWireRestoresExplicitLegacyMotion()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            source.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(source.Move(1,new NavPoint(5,0)));
            Assert.True(source.Requests.TryDequeue(out var request));
            Assert.True(source.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal})));
            source.ApplyResults();
            var checkpoint=source.CaptureState();
            var bytes=WorldWire.Pack(w=>WorldWire.Write(w,checkpoint));
            int tag=-1;
            for(int i=0;i<bytes.Length-20;i++)if(bytes[i]==0x31&&bytes[i+1]==0x50&&
                bytes[i+2]==0x52&&bytes[i+3]==0x47){tag=i;break;}
            Assert.GreaterOrEqual(tag,0);
            Assert.AreEqual(7,BitConverter.ToInt32(bytes,tag+4));
            bytes[tag+4]=5; // v6 adds five empty sidecar arrays to this legacy state
            int marchTail=4+checkpoint.Groups.Sum(g=>37+12*g.Members.Length);
            var legacy=bytes.Take(bytes.Length-20-marchTail).ToArray();
            var state=WorldWire.Unpack(legacy,WorldWire.ReadNavigationSessionState);
            Assert.AreEqual(0,state.CorridorWireVersion);
            var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),
                PlayableProfile.Default.Navigation);
            restored.RestoreState(state);
            Assert.True(restored.Crowd.Units.Single().Moving);
            Assert.Null(restored.InstalledExecutionFor(1));
        }

        [Test] public void InstalledCorridorSurvivesPendingSubsetAndV6CheckpointWithoutSteeringOldMember()
        {
            var geometry=new NavGeometry(30,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-10,-2));session.Crowd.Add(2,new NavPoint(-10,2));
            Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(10,-2),new NavPoint(10,2)}));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                for(int i=0;i<80&&session.PendingCount>0;i++){host.Service(binding,8);session.ApplyResults();}
            }
            Assert.AreEqual(2,session.AppliedResults);
            var first=session.InstalledExecutionFor(1);
            Assert.NotNull(first);Assert.Greater(first.Corridor.Legs.Count,0);
            Assert.AreEqual(NavCorridorSection.IndividualConnector,first.Corridor.Legs[0].Section);
            Assert.True(session.MoveGroup(new[]{1},new[]{new NavPoint(12,-4)}));
            Assert.Null(session.InstalledExecutionFor(1));
            Assert.NotNull(session.InstalledExecutionFor(2));
            Assert.True(session.Crowd.Units.Single(u=>u.Id==1).Moving);
            var bytes=WorldWire.Pack(w=>WorldWire.Write(w,session.CaptureState()));
            var state=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
            Assert.AreEqual(1,state.CorridorWireVersion);
            Assert.AreEqual(2,state.InstalledExecutions.Length);
            Assert.True(state.InstalledExecutions.Single(x=>x.Entity==1).Detached);
            var restored=new NavigationSession(1,new NavGeometry(30,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            restored.RestoreState(state);
            Assert.Null(restored.InstalledExecutionFor(1));
            Assert.NotNull(restored.InstalledExecutionFor(2));
            Assert.AreEqual(session.Crowd.Units.Single(u=>u.Id==1).Goal,
                restored.Crowd.Units.Single(u=>u.Id==1).Goal);
            CollectionAssert.AreEqual(session.Crowd.Units.Single(u=>u.Id==1).Route,
                restored.Crowd.Units.Single(u=>u.Id==1).Route);
            session.Step(1d/30);restored.Step(1d/30);
            foreach(var id in new[]{1,2}){
                Assert.AreEqual(session.Crowd.Units.Single(u=>u.Id==id).Position,
                    restored.Crowd.Units.Single(u=>u.Id==id).Position);
                Assert.AreEqual(session.Crowd.Units.Single(u=>u.Id==id).Outcome,
                    restored.Crowd.Units.Single(u=>u.Id==id).Outcome);
            }
            Assert.True(restored.Requests.TryDequeue(out var replacement));
            Assert.True(restored.Answers.TryEnqueue(new NavigationAnswer(replacement,
                Array.Empty<NavPoint>(),NavSolveStatus.BlockedConnector)));
            restored.ApplyResults();
            Assert.True(restored.Crowd.Units.Single(u=>u.Id==1).Moving);
            Assert.Null(restored.InstalledExecutionFor(1));
            Assert.NotNull(restored.InstalledExecutionFor(2));
            Assert.AreEqual(NavSolveStatus.BlockedConnector,restored.TechnicalFailureFor(1));
        }

        [Test] public void AuthoredSingletonDirectSeamInstallsWithoutGraphOrNavMesh()
        {
            var map=ThreeCrossingsMap.Default;
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=PlayableProfile.ThreeCrossingsDefault;
            foreach(double radius in new[]{.58,.72}){
                var session=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
                var actor=session.Crowd.Add(1,new NavPoint(-18,0),radius,4,4.5);
                var goal=new NavPoint(18,0);
                Assert.True(session.Move(1,goal));
                Assert.AreEqual(1,session.GroupOrders.Single().Members.Length);
                using(var host=new UnityHostRouteService()){
                    var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
                    for(int i=0;i<16&&session.AppliedResults==0;i++){
                        host.Service(binding,64);session.ApplyResults();
                    }
                    Assert.AreEqual(1,session.AppliedResults,"radius "+radius+" pending="+session.PendingCount+
                        " queued="+session.Requests.Count+" registered="+session.RegisteredRouteCount+
                        " rejected="+session.RejectedResults+" technical="+session.TechnicalFailureFor(1)+
                        " ready="+host.CertifiedCounters.ReadyResults+" graph="+host.CertifiedCounters.GraphBuilds+
                        " projection="+host.LastProjectionFailure+" corridor="+session.LastCorridorFailure);
                    Assert.AreEqual(0,host.CertifiedCounters.GraphBuilds);
                    Assert.AreEqual(0,host.CertifiedCounters.PhysicalGraphWork);
                    Assert.AreEqual(0,host.NavMeshBuilds);
                    Assert.AreEqual(goal,actor.Goal);
                    Assert.AreEqual(NavigationOutcome.Moving,actor.Outcome);
                }
            }
        }

        [Test] public void FoundryCertifiedSingletonInstallsAtBothExistingClearances()
        {
            var map=new FoundryMap(new FoundryProfileData());
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            foreach(double radius in new[]{.58,.72}){
                var session=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
                var start=new NavPoint(0,0);var goal=new NavPoint(1,0);
                session.Crowd.Add(1,start,radius,4,4.5);
                Assert.True(session.Move(1,goal));
                using(var host=new UnityHostRouteService()){
                    var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
                    for(int i=0;i<40&&session.PendingCount>0;i++){host.Service(binding,4);session.ApplyResults();}
                    Assert.AreEqual(1,session.AppliedResults,"radius="+radius+" technical="+
                        session.TechnicalFailureFor(1)+" projection="+host.LastProjectionFailure);
                    Assert.AreEqual(0,host.CertifiedCounters.GraphBuilds);
                    Assert.AreEqual(0,host.NavMeshBuilds);
                }
            }
        }

        [Test] public void EightFiftyMemberGroupsAdmitAndReplayFourHundredOriginalInputs()
        {
            var geometry=new NavGeometry(40,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,new NavigationProfile());
            for(int group=0;group<8;group++){
                var ids=new int[50];var goals=new NavPoint[50];
                for(int member=0;member<50;member++){
                    int index=group*50+member,id=index+1;
                    ids[member]=id;
                    source.Crowd.Add(id,new NavPoint(-35+1.6*(index%20),-35+1.6*(index/20)));
                    goals[member]=new NavPoint(4+1.6*(index%20),4+1.6*(index/20));
                }
                Assert.True(source.MoveGroup(ids,goals),"group "+group);
            }
            Assert.AreEqual(400,source.Requests.Count);
            Assert.AreEqual(8,source.GroupOrders.Count);
            var saved=source.CaptureState();
            Assert.AreEqual(1,saved.TransportVersion);
            Assert.AreEqual(400,saved.RequestMailboxIndices.Length);
            var restored=new NavigationSession(1,new NavGeometry(40,Array.Empty<NavObstacle>(),1),new NavigationProfile());
            restored.RestoreState(saved);
            Assert.AreEqual(400,restored.Requests.Count);
            Assert.AreEqual(400,restored.PendingCount);
            saved.TransportVersion=0;
            Assert.Throws<ArgumentException>(()=>new NavigationSession(1,
                new NavGeometry(40,Array.Empty<NavObstacle>(),1),new NavigationProfile()).RestoreState(saved));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,source.Admission,source.RoutePort);
                for(int attempt=0;attempt<4096&&source.PendingCount>0;attempt++){
                    host.Service(binding,64);source.ApplyResults();
                }
                Assert.AreEqual(400,source.AppliedResults,"pending="+source.PendingCount+
                    " rejected="+source.RejectedResults+" unreachable="+source.UnreachableResults+
                    " projection="+host.LastProjectionFailure);
                Assert.AreEqual(0,source.PendingCount);
                Assert.LessOrEqual(host.CertifiedCounters.FieldBuilds,8,
                    "Eight groups in one clearance class cannot compile one field per exact slot.");
                Assert.LessOrEqual(host.CertifiedCounters.PeakQueue,4096);
                Assert.LessOrEqual(host.CertifiedCounters.CacheBytes,LayeredNavigationScheduler.MaxCacheBytes);
                Assert.LessOrEqual(host.CertifiedPeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                Assert.AreEqual(0,host.NavMeshBuilds);
            }
        }

        [Test] public void DistinctAssignedSlotsReuseOneActualTerminalField()
        {
            var map=ThreeCrossingsMap.Default;
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=PlayableProfile.ThreeCrossingsDefault;
            var session=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
            var ids=new[]{1,2};var goals=new[]{new NavPoint(18,-1.2),new NavPoint(18,1.2)};
            for(int i=0;i<2;i++)session.Crowd.Add(ids[i],new NavPoint(-18,i==0?-1.2:1.2),.58,4,4.5);
            Assert.True(session.MoveGroup(ids,goals));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
                for(int i=0;i<300&&session.PendingCount>0;i++){host.Service(binding,64);session.ApplyResults();}
                Assert.AreEqual(2,session.AppliedResults,"technical="+session.TechnicalFailureFor(1)+"/"+
                    session.TechnicalFailureFor(2)+" projection="+host.LastProjectionFailure);
                Assert.AreEqual(1,host.CertifiedCounters.FieldBuilds);
                Assert.AreEqual(0,host.NavMeshBuilds);
                Assert.AreEqual(0,session.PendingCount);
                Assert.AreEqual(goals[0],session.Crowd.Units.Single(u=>u.Id==1).Goal);
                Assert.AreEqual(goals[1],session.Crowd.Units.Single(u=>u.Id==2).Goal);
            }
        }

        [Test] public void FoundryMaximumScaleFiftyMemberDetourSharesOneFieldAndInstallsExactSlots()
        {
            var map=new FoundryMap(new FoundryProfileData{scale=1.5});
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var provider=new PartitionNavigationProvider(map,geometry,null);
            var clearance=new NavClearanceProfile("ground",1,"ground",0,.58);
            NavPoint[] Sites(NavPoint center){
                var found=new List<NavPoint>{center};
                for(int radius=1;radius<=20&&found.Count<50;radius++)
                    for(int z=-radius;z<=radius&&found.Count<50;z++)
                        for(int x=-radius;x<=radius&&found.Count<50;x++){
                            if(Math.Max(Math.Abs(x),Math.Abs(z))!=radius)continue;
                            var point=new NavPoint(center.X+x*1.6,center.Z+z*1.6);
                            if(provider.TryLocate(point,clearance,out var location)&&
                                provider.IsValid(location,clearance)&&geometry.IsFree(point,.58))found.Add(point);
                        }
                Assert.AreEqual(50,found.Count,"Not enough certified Foundry locations around "+center);
                return found.ToArray();
            }
            var starts=Sites(new NavPoint(0,0));
            var goals=Sites(map.Point(0,100));
            var ids=Enumerable.Range(1,50).ToArray();
            var session=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
            for(int i=0;i<50;i++)session.Crowd.Add(ids[i],starts[i],.58,4,4.5);
            Assert.True(session.MoveGroup(ids,goals));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
                for(int i=0;i<4096&&session.PendingCount>0;i++){host.Service(binding,32);session.ApplyResults();}
                Assert.AreEqual(50,session.AppliedResults,"pending="+session.PendingCount+
                    " field="+host.CertifiedCounters.FieldBuilds+" graph="+host.CertifiedCounters.GraphBuilds+
                    " projection="+host.LastProjectionFailure+" technical="+session.TechnicalFailureFor(1));
                Assert.AreEqual(1,host.CertifiedCounters.FieldBuilds);
                Assert.AreEqual(1,host.CertifiedCounters.GraphBuilds);
                Assert.AreEqual(0,host.NavMeshBuilds);
                Assert.LessOrEqual(host.CertifiedPeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                for(int i=0;i<50;i++)Assert.AreEqual(goals[i],session.Crowd.Units.Single(u=>u.Id==ids[i]).Goal);
            }
        }

        [Test] public void RestoredRoutePortKeepsMonotonicSolverEpochAndDiscardsStaleOldInput()
        {
            var map=new FoundryMap(new FoundryProfileData{scale=1.5});
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var provider=new PartitionNavigationProvider(map,geometry,null);
            var clearance=new NavClearanceProfile("ground",1,"ground",0,.58);
            NavPoint[] Sites(NavPoint center){
                var found=new List<NavPoint>{center};
                for(int radius=1;radius<=20&&found.Count<50;radius++)
                    for(int z=-radius;z<=radius&&found.Count<50;z++)
                        for(int x=-radius;x<=radius&&found.Count<50;x++){
                            if(Math.Max(Math.Abs(x),Math.Abs(z))!=radius)continue;
                            var point=new NavPoint(center.X+x*1.6,center.Z+z*1.6);
                            if(provider.TryLocate(point,clearance,out var location)&&
                                provider.IsValid(location,clearance)&&geometry.IsFree(point,.58))found.Add(point);
                        }
                Assert.AreEqual(50,found.Count,"Not enough certified Foundry locations around "+center);
                return found.ToArray();
            }
            var starts=Sites(new NavPoint(0,0));var goals=Sites(map.Point(0,100));
            var ids=Enumerable.Range(1,50).ToArray();
            var source=new NavigationSession(71,geometry,profile.Navigation,terrain:map);
            for(int i=0;i<ids.Length;i++)source.Crowd.Add(ids[i],starts[i],.58,4,4.5);
            Assert.True(source.MoveGroup(ids,goals));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,profile,source.Admission,source.RoutePort);
                for(int i=0;i<4096&&host.SolverEpoch==0;i++)host.Service(binding,4);
                Assert.Greater(host.SolverEpoch,0,"Real detour work must exhaust an epoch before switching authority.");
                Assert.Greater(source.RegisteredRouteCount,0);
                Assert.Greater(source.PendingCount,0,"Capture genuinely registered unfinished work.");
                long before=host.SolverEpoch;int oldAnswers=source.Answers.Count;
                var bytes=WorldWire.Pack(w=>WorldWire.Write(w,source.CaptureState()));
                var state=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
                var nextGeometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
                var restored=new NavigationSession(71,nextGeometry,profile.Navigation,terrain:map);
                restored.RestoreState(state);
                Assert.AreNotSame(source.RoutePort,restored.RoutePort);
                var nextGoals=starts.Reverse().ToArray();
                Assert.True(restored.MoveGroup(ids,nextGoals),"Replace restored inputs through ordinary authority.");
                var next=new PlayableRouteBinding(nextGeometry,profile,restored.Admission,restored.RoutePort);
                bool staleObserved=false;
                for(int i=0;i<4096&&restored.PendingCount>0;i++){
                    host.Service(next,8);
                    staleObserved|=restored.CaptureState().AnswerMailbox.Any(answer=>answer.Status==NavSolveStatus.Stale);
                    restored.ApplyResults();
                }
                Assert.GreaterOrEqual(host.SolverEpoch,before,"Port changes cannot rewind solver quota history.");
                Assert.AreEqual(0,restored.PendingCount);
                Assert.AreEqual(50,restored.AppliedResults,"Only current replacement orders are installed.");
                Assert.True(staleObserved,"Captured old requests are actually answered as typed stale.");
                Assert.AreEqual(50,restored.RejectedResults,"Every stale old input is rejected exactly once.");
                for(int i=0;i<ids.Length;i++){
                    var actor=restored.Crowd.Units.Single(u=>u.Id==ids[i]);
                    Assert.AreEqual(nextGoals[i],actor.Goal);
                    Assert.AreEqual(NavigationOutcome.Moving,actor.Outcome);
                }
                Assert.AreEqual(oldAnswers,source.Answers.Count,"No completion leaks back into the old authority.");
                Assert.LessOrEqual(host.CertifiedPeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                Assert.AreEqual(0,host.NavMeshBuilds);
            }
        }

        [Test] public void HeadlessAuthorityUsesSameCertifiedServiceAndFrozenBarrier()
        {
            var configuration=OfflineParticipantAuthorityTests.Config(2);
            var authority=new PlayableAuthorityTick(configuration,71);
            var actor=authority.Latest.Entities.First(e=>e.Owner==PlayableOwner.Player);
            var goal=new NavPoint(actor.Position.X+2,actor.Position.Z);
            Assert.True(authority.NavigationBinding.Geometry.IsFree(goal,configuration.Profile.Navigation.Radius));
            var receipt=authority.Apply(new PlayableCommand(71,1,configuration.Roster[0].Id,
                PlayableCommandKind.Move,new[]{actor.Id},goal).AsHuman());
            Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status);
            using(var host=new UnityHostRouteService()){
                bool advanced=false;
                for(int attempt=0;attempt<4096;attempt++){
                    if(authority.TryAdvance()){advanced=true;break;}
                    host.Service(authority,4);
                }
                Assert.True(advanced);
                Assert.Greater(host.CertifiedCounters.ConnectorWork,0);
                Assert.AreEqual(0,host.NavMeshBuilds);
                Assert.Greater(authority.Tick,0);
            }
            authority.Stop();
        }

        [Test] public void ReadyRouteWaitsBehindFullAnswerMailboxAndResumesWithoutReplanning()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,new NavigationProfile());
            session.Crowd.Add(1,new NavPoint(0,0));
            Assert.True(session.Move(1,new NavPoint(5,0)));
            var filler=new NavigationRequest(1,999,42,1,new NavigationProfile(),geometry,
                new NavPoint(0,0),new NavPoint(5,0));
            for(int i=0;i<NavMailbox<NavigationAnswer>.Capacity;i++)
                Assert.True(session.Answers.TryEnqueue(new NavigationAnswer(filler,new[]{filler.Goal})));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                for(int i=0;i<128&&session.RegisteredRouteCount==0;i++)host.Service(binding,1);
                Assert.AreEqual(1,session.RegisteredRouteCount);
                for(int i=0;i<128;i++)host.Service(binding,1);
                Assert.AreEqual(NavMailbox<NavigationAnswer>.Capacity,session.Answers.Count);
                Assert.AreEqual(1,session.RegisteredRouteCount);
                Assert.LessOrEqual(host.CertifiedPeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                Assert.True(session.Answers.TryDequeue(out _));
                host.Service(binding,1);
                Assert.AreEqual(0,session.RegisteredRouteCount);
                session.ApplyResults();
                Assert.AreEqual(1,session.AppliedResults);
                Assert.AreEqual(0,host.NavMeshBuilds);
            }
        }

        [Test] public void StaleSubsetHeadDoesNotBlockUnchangedSibling()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,new NavigationProfile());
            session.Crowd.Add(1,new NavPoint(-5,-1));session.Crowd.Add(2,new NavPoint(-5,1));
            Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(5,-1),new NavPoint(5,1)}));
            session.Stop(1,false);
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                for(int i=0;i<100&&session.PendingCount>0;i++){host.Service(binding,1);session.ApplyResults();}
                Assert.AreEqual(0,session.PendingCount);
                Assert.AreEqual(1,session.AppliedResults);
                Assert.False(session.Crowd.Units.Single(u=>u.Id==1).Moving);
                Assert.True(session.Crowd.Units.Single(u=>u.Id==2).Moving);
                Assert.AreEqual(0,session.RegisteredRouteCount);
            }
        }

        [Test] public void RegisteredSubsetCancellationAndSmallServiceChunksPreserveSiblingInstall()
        {
            var map=ThreeCrossingsMap.Default;
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=PlayableProfile.ThreeCrossingsDefault;
            NavigationSession Session(){
                var value=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
                value.Crowd.Add(1,new NavPoint(-20,18),.58,4,4.5);
                value.Crowd.Add(2,new NavPoint(-18,18),.58,4,4.5);
                Assert.True(value.MoveGroup(new[]{1,2},new[]{new NavPoint(18,18),new NavPoint(20,18)}));
                return value;
            }
            var narrow=Session();var wide=Session();
            using(var small=new UnityHostRouteService())using(var large=new UnityHostRouteService()){
                var smallBinding=new PlayableRouteBinding(geometry,profile,narrow.Admission,narrow.RoutePort);
                var largeBinding=new PlayableRouteBinding(geometry,profile,wide.Admission,wide.RoutePort);
                small.Service(smallBinding,1);
                large.Service(largeBinding,1);
                Assert.AreEqual(1,narrow.RegisteredRouteCount);
                Assert.AreEqual(1,wide.RegisteredRouteCount);
                narrow.Stop(1,false);wide.Stop(1,false);
                for(int i=0;i<4096&&(narrow.PendingCount>0||wide.PendingCount>0);i++){
                    if(narrow.PendingCount>0){small.Service(smallBinding,1);narrow.ApplyResults();}
                    if(wide.PendingCount>0){large.Service(largeBinding,64);wide.ApplyResults();}
                }
                Assert.AreEqual(0,narrow.PendingCount);
                Assert.AreEqual(0,wide.PendingCount);
                Assert.AreEqual(0,narrow.RegisteredRouteCount);
                Assert.AreEqual(0,wide.RegisteredRouteCount);
                Assert.False(narrow.Crowd.Units.Single(u=>u.Id==1).Moving);
                Assert.False(wide.Crowd.Units.Single(u=>u.Id==1).Moving);
                Assert.AreEqual(1,narrow.AppliedResults);
                Assert.AreEqual(1,wide.AppliedResults);
                var n=narrow.Crowd.Units.Single(u=>u.Id==2);
                var w=wide.Crowd.Units.Single(u=>u.Id==2);
                Assert.True(n.Moving);Assert.True(w.Moving);
                Assert.AreEqual(n.Goal,w.Goal);
                Assert.AreEqual(small.SolverEpoch,large.SolverEpoch);
                Assert.LessOrEqual(small.CertifiedPeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                Assert.LessOrEqual(large.CertifiedPeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
            }
        }

        [TestCase(NavSolveStatus.InvalidEndpoint)]
        [TestCase(NavSolveStatus.BlockedConnector)]
        [TestCase(NavSolveStatus.CapacityExceeded)]
        public void TechnicalFailureSurvivesV5AnswerAndInstalledFailureWire(NavSolveStatus status)
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var source=new NavigationSession(1,geometry,new NavigationProfile());
            source.Crowd.Add(1,new NavPoint(0,0));
            Assert.True(source.Move(1,new NavPoint(5,0)));
            Assert.True(source.Requests.TryDequeue(out var request));
            Assert.True(source.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>(),status)));
            var bytes=WorldWire.Pack(w=>WorldWire.Write(w,source.CaptureState()));
            var state=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
            Assert.AreEqual(1,state.TransportVersion);
            Assert.AreEqual(status,state.AnswerMailbox.Single().Status);
            var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),new NavigationProfile());
            restored.RestoreState(state);restored.ApplyResults();
            Assert.AreEqual(0,restored.UnreachableResults);
            Assert.AreEqual(NavigationOutcome.Rejected,restored.Crowd.Units.Single().Outcome);
            Assert.AreEqual(status,restored.TechnicalFailureFor(1));
            bytes=WorldWire.Pack(w=>WorldWire.Write(w,restored.CaptureState()));
            state=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
            Assert.AreEqual(status,state.TechnicalFailures.Single().Status);
        }
    }
}
