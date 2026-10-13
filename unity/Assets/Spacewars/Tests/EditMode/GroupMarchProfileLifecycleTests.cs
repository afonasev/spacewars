using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class GroupMarchProfileLifecycleTests
    {
        private static NavigationSession ReadyPair(double stretch)
        {
            var geometry=new NavGeometry(30,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.ConfigureMarch(stretch,PlayableProfile.Default.Revision);
            session.Crowd.Add(1,new NavPoint(-10,-2),.72,2,4.5);
            session.Crowd.Add(2,new NavPoint(-10,2),.72,4,4.5);
            Assert.True(session.MoveGroup(new[]{2,1},new[]{new NavPoint(10,2),new NavPoint(10,-2)}));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
                for(int i=0;i<80&&session.PendingCount>0;i++){host.Service(binding,8);session.ApplyResults();}
            }
            Assert.AreEqual(2,session.AppliedResults);
            Assert.NotNull(session.InstalledExecutionFor(1));Assert.NotNull(session.InstalledExecutionFor(2));
            return session;
        }
        private static NavigationProfile NextNavigation(NavigationProfile p,int revision,string id=null)
            =>new NavigationProfile(id??p.ProfileId,revision,p.Radius,p.TankSpeed,p.TankTurnSpeed,p.GridCell,
                p.ArrivalTolerance,p.LocalDetourDistance,p.ForwardAlignmentAngle,p.CameraOrthoSize,p.CameraHeight,
                p.CameraOffsetZ,p.RepairRetrySeconds,p.ArrivalSlotSpacing,p.BlockedTimeoutSeconds,p.LocalGridCell);
        private static Type DomainType=>typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object NewDomain(PlayableProfile profile)
            =>Activator.CreateInstance(DomainType,BindingFlags.Instance|BindingFlags.NonPublic,null,
                new object[]{profile,1L,false},null);
        private static int Spawn(object domain,NavPoint point)
            =>(int)DomainType.GetMethod("SpawnUnit",Hidden).Invoke(domain,
                new object[]{point,PlayableOwner.Player,PlayableEntityKind.Explorer});
        private static NavigationSession DomainNavigation(object domain)
            =>(NavigationSession)DomainType.GetProperty("Navigation",Hidden).GetValue(domain);
        private static byte[] CaptureWorld(object domain)
            =>(byte[])DomainType.GetMethod("CaptureWorldBytes",Hidden).Invoke(domain,new object[]{7,"profile-lifecycle"});
        private static object RestoreWorld(byte[] bytes,PlayableProfile profile)
            =>DomainType.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{bytes,profile,7,"profile-lifecycle"});
        private static void Apply(object domain,PlayableProfile profile)
            =>DomainType.GetMethod("ApplyBalance",Hidden).Invoke(domain,new object[]{profile});
        private static void StepDomain(object domain)
            =>DomainType.GetMethod("Step",Hidden).Invoke(domain,new object[]{1d/30});
        private static byte[] NavigationBytes(NavigationSessionState state)
            =>WorldWire.Pack(w=>WorldWire.Write(w,state));
        private static PlayableProfile StretchProfile(PlayableProfile current,double stretch,int revision)
        {
            var data=current.CopyData();data.revision=revision;data.groupMarchMaximumStretch=stretch;
            return PlayableProfile.Create(data,current.AuthoredMap);
        }
        private static Dictionary<int,Tuple<string,int,double[]>> HistoricalBindings(NavigationSessionState state)
            =>state.InstalledExecutions.ToDictionary(x=>x.Entity,
                x=>Tuple.Create(x.ProfileId,x.ProfileRevision,(double[])x.ProfileValues.Clone()));
        private static void AssertProfileBindings(NavigationSessionState state,int currentRevision,
            Dictionary<int,Tuple<string,int,double[]>> historical)
        {
            Assert.AreEqual(currentRevision,state.Crowd.ProfileRevision);
            Assert.AreEqual(PlayableProfile.Default.ProfileId,state.Crowd.ProfileId);
            Assert.AreEqual(historical.Count,state.InstalledExecutions.Length);
            foreach(var row in state.InstalledExecutions){
                Assert.True(historical.TryGetValue(row.Entity,out var binding));
                Assert.AreEqual(binding.Item1,row.ProfileId,"Historical profile identity remains bound.");
                Assert.AreEqual(binding.Item2,row.ProfileRevision,"Historical profile revision remains bound.");
                CollectionAssert.AreEqual(binding.Item3,row.ProfileValues,"Historical unit-specific navigation values remain bound.");
            }
        }
        private static void AssertGroupIdentity(GroupOrderState expected,GroupOrderState actual)
        {
            Assert.AreEqual(expected.GroupId,actual.GroupId);Assert.AreEqual(expected.CommandSequence,actual.CommandSequence);
            Assert.AreEqual(expected.OriginalGoal,actual.OriginalGoal);Assert.AreEqual(expected.Kind,actual.Kind);
            Assert.AreEqual(expected.Members.Length,actual.Members.Length);
            foreach(var member in expected.Members){var row=actual.Members.Single(x=>x.Entity==member.Entity);
                Assert.AreEqual(member.Incarnation,row.Incarnation);Assert.AreEqual(member.OrderRevision,row.OrderRevision);
                Assert.AreEqual(member.Slot,row.Slot);Assert.AreEqual(member.Goal,row.Goal);}
        }

        [Test] public void CompatibleStretchRevisionPreservesInstalledV1PairAndAppliesOnNextStep()
        {
            var session=ReadyPair(.5);var before=session.CaptureState();
            var originalGroup=before.Groups.Single();var slots=originalGroup.Members.OrderBy(x=>x.Entity).Select(x=>x.Slot).ToArray();
            var order=originalGroup.CommandSequence;
            var next=NextNavigation(PlayableProfile.Default.Navigation,2);
            session.Rebind(next);session.ConfigureMarch(9,2);
            Assert.NotNull(session.InstalledExecutionFor(1));Assert.NotNull(session.InstalledExecutionFor(2));
            var after=session.CaptureState();AssertGroupIdentity(originalGroup,after.Groups.Single());Assert.AreEqual(order,after.Groups.Single().CommandSequence);
            CollectionAssert.AreEqual(slots,after.Groups.Single().Members.OrderBy(x=>x.Entity).Select(x=>x.Slot));
            Assert.True(after.InstalledExecutions.All(x=>!x.Detached));
            session.Step(1d/30);
            Assert.AreEqual(2,session.GroupFor(1).MarchProfileRevision);
            Assert.AreEqual(9,session.GroupFor(1).MarchStretch);
            Assert.NotNull(session.InstalledExecutionFor(1));Assert.NotNull(session.InstalledExecutionFor(2));
        }

        [Test] public void CompatibleApplyDuringVoluntaryWaitKeepsCapAndMovingWithoutRepair()
        {
            var session=ReadyPair(.5);for(int i=0;i<30;i++)session.Step(1d/30);
            var contact=typeof(NavigationSession).GetMethod("SetFormationContact",Hidden);
            contact.Invoke(session,new object[]{1,true,0L});
            for(int i=0;i<45;i++)session.Step(1d/30);
            contact.Invoke(session,new object[]{1,false,0L});
            var front=session.Crowd.Units.Single(u=>u.Id==2);var before=front.Position;
            int repairs=session.Crowd.RepairCount;var old=NextNavigation(PlayableProfile.Default.Navigation,2);
            session.Rebind(old);session.ConfigureMarch(.5,2);session.Step(1d/30);
            Assert.AreEqual(before,front.Position,"The front member remains voluntarily capped for the lagging peer.");
            Assert.True(front.Moving);Assert.AreEqual(NavigationOutcome.Moving,front.Outcome);
            Assert.AreEqual(repairs,session.Crowd.RepairCount);
            Assert.NotNull(session.InstalledExecutionFor(1));Assert.NotNull(session.InstalledExecutionFor(2));
        }

        [Test] public void FullWorldSaveAroundCompatibleApplyRoundTripsBytesAndContinuation()
        {
            var originalProfile=PlayableProfile.Default;var domain=NewDomain(originalProfile);
            // Use a live standard match with both headquarters. Add a second player
            // explorer in the open southwest lane so CheckOutcome remains authoritative
            // and every paired world step exercises an active V1 march.
            int first=Spawn(domain,new NavPoint(-12,-20)),second=Spawn(domain,new NavPoint(-12,-15));
            var navigation=DomainNavigation(domain);
            var geometry=(NavGeometry)DomainType.GetProperty("Geometry",Hidden).GetValue(domain);
            Assert.True(navigation.MoveGroup(new[]{second,first},new[]{new NavPoint(-7,-15),new NavPoint(-7,-20)}));
            using(var host=new UnityHostRouteService()){
                var binding=new PlayableRouteBinding(geometry,originalProfile,navigation.Admission,navigation.RoutePort);
                for(int i=0;i<80&&navigation.PendingCount>0;i++){host.Service(binding,8);navigation.ApplyResults();}
            }
            Assert.AreEqual(2,navigation.AppliedResults);
            Assert.AreEqual(PlayableMatchOutcome.Playing,
                (PlayableMatchOutcome)DomainType.GetProperty("Outcome",Hidden).GetValue(domain));
            RefreshSnapshots(domain,0);
            var preApply=navigation.CaptureState();var originalGroup=preApply.Groups.Single();
            var historicalBindings=HistoricalBindings(preApply);
            var next=StretchProfile(originalProfile,9,2);Apply(domain,next);
            Assert.NotNull(navigation.InstalledExecutionFor(first));Assert.NotNull(navigation.InstalledExecutionFor(second));
            AssertGroupIdentity(originalGroup,navigation.CaptureState().Groups.Single());
            RefreshSnapshots(domain,1);Assert.True(((PlayableSnapshot)DomainType.GetMethod("Snapshot",Hidden).Invoke(domain,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7})).Buildings.Any(b=>b.Kind==PlayableBuildingKind.Headquarters&&b.Health>0));
            void RoundTripAndContinue()
            {
                var bytes=CaptureWorld(domain);var restored=RestoreWorld(bytes,next);var restoredBytes=CaptureWorld(restored);
                CollectionAssert.AreEqual(bytes,restoredBytes);
                var current=navigation.CaptureState();var replay=DomainNavigation(restored).CaptureState();
                AssertProfileBindings(current,2,historicalBindings);AssertProfileBindings(replay,2,historicalBindings);
                AssertGroupIdentity(originalGroup,current.Groups.Single());AssertGroupIdentity(originalGroup,replay.Groups.Single());
                long tick=(long)DomainType.GetProperty("Tick",Hidden).GetValue(domain);
                StepDomain(domain);StepDomain(restored);
                foreach(var candidate in new[]{domain,restored}){
                    Assert.AreEqual(tick+1,(long)DomainType.GetProperty("Tick",Hidden).GetValue(candidate),
                        "Each paired domain step must execute rather than freeze.");
                    Assert.AreEqual(PlayableMatchOutcome.Playing,
                        (PlayableMatchOutcome)DomainType.GetProperty("Outcome",Hidden).GetValue(candidate),
                        "The live match must remain authoritative while continuation is compared.");
                    RefreshSnapshots(candidate,tick+1);
                    var snapshot=(PlayableSnapshot)DomainType.GetMethod("Snapshot",Hidden).Invoke(candidate,new object[]{tick+1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7});
                    Assert.True(snapshot.Buildings.Any(b=>b.Kind==PlayableBuildingKind.Headquarters&&b.Health>0),"A real headquarters remains alive in both domains.");
                    var active=DomainNavigation(candidate);
                    Assert.NotNull(active.InstalledExecutionFor(first));Assert.NotNull(active.InstalledExecutionFor(second));
                    Assert.True(active.Crowd.Units.Single(x=>x.Id==first).Moving);
                    Assert.True(active.Crowd.Units.Single(x=>x.Id==second).Moving);
                }
                var left=navigation.CaptureState();var right=DomainNavigation(restored).CaptureState();
                CollectionAssert.AreEqual(NavigationBytes(left),NavigationBytes(right),
                    "The complete navigation checkpoint must match after each paired world step.");
                foreach(int id in new[]{first,second}){
                    Assert.AreEqual(left.Crowd.Units.Single(x=>x.Id==id).Position,right.Crowd.Units.Single(x=>x.Id==id).Position);
                    Assert.AreEqual(left.Groups.Single().Members.Single(x=>x.Entity==id).Progress,
                        right.Groups.Single().Members.Single(x=>x.Entity==id).Progress);
                }
                var leftWorld=PlayableWorldState.Decode(CaptureWorld(domain));var rightWorld=PlayableWorldState.Decode(CaptureWorld(restored));
                CollectionAssert.AreEqual(leftWorld.Domain,rightWorld.Domain,"World domain section must remain byte-identical after continuation.");
                CollectionAssert.AreEqual(leftWorld.Navigation,rightWorld.Navigation,"World navigation section must remain byte-identical after continuation.");
                CollectionAssert.AreEqual(leftWorld.Binding,rightWorld.Binding,"World profile binding must remain byte-identical after continuation.");
                CollectionAssert.AreEqual(leftWorld.Participants,rightWorld.Participants,"World participant binding must remain byte-identical after continuation.");
                CollectionAssert.AreEqual(leftWorld.AiAuthority,rightWorld.AiAuthority,"World AI authority section must remain byte-identical after continuation.");
                CollectionAssert.AreEqual(leftWorld.RuntimeTransport,rightWorld.RuntimeTransport,"World runtime transport section must remain byte-identical after continuation.");
                Assert.AreEqual(leftWorld.Vision.Length,rightWorld.Vision.Length);
                for(int i=0;i<leftWorld.Vision.Length;i++)CollectionAssert.AreEqual(leftWorld.Vision[i],rightWorld.Vision[i],"World vision section "+i+" must remain byte-identical after continuation.");
                CollectionAssert.AreEqual(CaptureWorld(domain),CaptureWorld(restored),
                    "The complete world checkpoint must remain byte-identical after continuation.");
            }
            RoundTripAndContinue(); // Immediately after apply, before the next simulation step.
            RoundTripAndContinue(); // Again after one applied-profile step.
        }
        private static void RefreshSnapshots(object domain,long sequence)
        {
            var snapshot=DomainType.GetMethod("PlayerSnapshot",Hidden);
            foreach(PlayableOwner owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy})
                snapshot.Invoke(domain,new object[]{sequence,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner});
        }

        [Test] public void DetachedExecutionStaysDetachedAndOldPendingReplacementIsRejected()
        {
            var session=ReadyPair(.5);Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(12,-2),new NavPoint(12,2)}));
            Assert.True(session.Requests.TryDequeue(out var oldAnswerRequest));
            Assert.Null(session.InstalledExecutionFor(1),"The previous installed execution is detached by replacement.");
            session.Rebind(NextNavigation(PlayableProfile.Default.Navigation,2));session.ConfigureMarch(1,2);
            Assert.Null(session.InstalledExecutionFor(1));Assert.True(session.IsPending(1));
            Assert.True(session.Answers.TryEnqueue(new NavigationAnswer(oldAnswerRequest,new[]{oldAnswerRequest.Goal})));
            session.ApplyResults();Assert.AreEqual(1,session.RejectedResults);
            Assert.True(session.IsPending(1));Assert.Null(session.InstalledExecutionFor(1));
            Assert.True(session.Crowd.Units.Single(x=>x.Id==1).Moving);
        }

        [Test] public void IncompatibleNavigationProfileStillDetachesInstalledCorridor()
        {
            var session=ReadyPair(.5);var prior=session.InstalledExecutionFor(1);
            Assert.NotNull(prior);var old=PlayableProfile.Default.Navigation;
            var changed=NextNavigation(old,2,"different-navigation-profile");session.Rebind(changed);
            Assert.Null(session.InstalledExecutionFor(1));Assert.True(session.Crowd.Units.Single(x=>x.Id==1).Moving);
            Assert.True(session.CaptureState().InstalledExecutions.Single(x=>x.Entity==1).Detached);
            var state=WorldWire.Unpack(WorldWire.Pack(w=>WorldWire.Write(w,session.CaptureState())),
                WorldWire.ReadNavigationSessionState);
            var restored=new NavigationSession(1,new NavGeometry(30,Array.Empty<NavObstacle>(),1),changed);
            restored.ConfigureMarch(.5,2);restored.RestoreState(state);
            Assert.Null(restored.InstalledExecutionFor(1));Assert.True(restored.Crowd.Units.Single(x=>x.Id==1).Moving);

            var numericChange=new NavigationProfile(old.ProfileId,2,old.Radius,old.TankSpeed,old.TankTurnSpeed+.1,
                old.GridCell,old.ArrivalTolerance,old.LocalDetourDistance,old.ForwardAlignmentAngle,
                old.CameraOrthoSize,old.CameraHeight,old.CameraOffsetZ,old.RepairRetrySeconds,
                old.ArrivalSlotSpacing,old.BlockedTimeoutSeconds,old.LocalGridCell);
            var numericSession=ReadyPair(.5);numericSession.Rebind(numericChange);
            Assert.Null(numericSession.InstalledExecutionFor(1));
            Assert.True(numericSession.CaptureState().InstalledExecutions.Single(x=>x.Entity==1).Detached,
                "A numeric navigation-profile change retains the legacy detachment behavior.");
        }

        [Test] public void ChangedPerUnitNavigationValuesDetachDespiteCompatibleGlobalProfile()
        {
            var session=ReadyPair(.5);
            session.Crowd.SetMobility(2,4.25,4.5);
            session.Rebind(NextNavigation(PlayableProfile.Default.Navigation,2));
            Assert.NotNull(session.InstalledExecutionFor(1),"Unchanged per-unit profile binding remains active.");
            Assert.Null(session.InstalledExecutionFor(2),
                "The historical per-unit route profile no longer matches the actor's effective speed.");
            Assert.True(session.Crowd.Units.Single(x=>x.Id==2).Moving,
                "The existing movement intent survives detachment while its route is re-solved.");
            Assert.True(session.CaptureState().InstalledExecutions.Single(x=>x.Entity==2).Detached);
        }
    }
}
