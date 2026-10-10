using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class GroupMarchTests
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

        [Test] public void S02CurrentSlowestPaceVoluntaryWaitAndExactNextStepRestore()
        {
            var session=ReadyPair(.5);
            var original=session.GroupFor(1);
            Assert.AreEqual(1,original.MarchVersion);Assert.AreNotEqual(original.Members[0].Slot,original.Members[1].Slot);
            for(int i=0;i<30;i++)session.Step(1d/30);
            var slow=session.Crowd.Units.Single(u=>u.Id==1);var fast=session.Crowd.Units.Single(u=>u.Id==2);
            Assert.AreEqual(2,slow.Speed);Assert.AreEqual(4,fast.Speed);Assert.AreEqual(4.5,fast.TurnSpeed);
            Assert.LessOrEqual(fast.Position.X-slow.Position.X,.1,"The fast member must use the current slowest pace.");
            session.Crowd.SetSpeed(1,1);double beforeChange=fast.Position.X;
            session.Step(1d/30);
            Assert.LessOrEqual(fast.Position.X-beforeChange,1d/30+1e-8,
                "A mobility/research change must affect the next march tick.");
            var contact=typeof(NavigationSession).GetMethod("SetFormationContact",BindingFlags.Instance|BindingFlags.NonPublic);
            contact.Invoke(session,new object[]{1,true,0L});
            for(int i=0;i<45;i++)session.Step(1d/30);
            contact.Invoke(session,new object[]{1,false,0L});
            var before=session.Crowd.CaptureState().Units.Single(u=>u.Id==2);
            session.Step(1d/30);
            var after=session.Crowd.CaptureState().Units.Single(u=>u.Id==2);
            Assert.AreEqual(before.Position,after.Position,"The front member yields while the rear member catches up.");
            Assert.True(after.Moving);Assert.AreEqual(NavigationOutcome.Moving,after.Outcome);
            Assert.AreEqual(before.BlockedSeconds,after.BlockedSeconds);Assert.AreEqual(before.NoProgressTicks,after.NoProgressTicks);
            Assert.AreEqual(before.RouteIndex,after.RouteIndex);Assert.AreEqual(4,after.Speed);
            var bytes=WorldWire.Pack(w=>WorldWire.Write(w,session.CaptureState()));
            var restored=new NavigationSession(1,new NavGeometry(30,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            restored.ConfigureMarch(.5,PlayableProfile.Default.Revision);
            restored.RestoreState(WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState));
            CollectionAssert.AreEqual(bytes,WorldWire.Pack(w=>WorldWire.Write(w,restored.CaptureState())));
            session.Step(1d/30);restored.Step(1d/30);
            foreach(int id in new[]{1,2}){
                Assert.AreEqual(session.Crowd.Units.Single(u=>u.Id==id).Position,restored.Crowd.Units.Single(u=>u.Id==id).Position);
                Assert.AreEqual(session.GroupFor(id).Members.Single(m=>m.Entity==id).Progress,restored.GroupFor(id).Members.Single(m=>m.Entity==id).Progress);
            }
            Assert.True(session.MoveGroup(new[]{2},new[]{new NavPoint(12,2)}));
            Assert.Null(session.InstalledExecutionFor(2));
            Assert.True(session.Crowd.Units.Single(u=>u.Id==2).Moving);
            double detachedX=session.Crowd.Units.Single(u=>u.Id==2).Position.X;
            session.Step(1d/30);
            Assert.True(session.Crowd.Units.Single(u=>u.Id==1).Moving);
            Assert.Greater(session.Crowd.Units.Single(u=>u.Id==2).Position.X-detachedX,3d/30,
                "Pending singleton B cannot inherit A's voluntary wait.");
        }

        [TestCase(2)][TestCase(10)][TestCase(25)][TestCase(50)]
        public void S02SlotIdentityIsStableAcrossMembershipAndCheckpoint(int count)
        {
            var geometry=new NavGeometry(100,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.ConfigureMarch(4,PlayableProfile.Default.Revision);
            var ids=Enumerable.Range(1,count).ToArray();
            foreach(int id in ids)session.Crowd.Add(id,new NavPoint(-50+(id%10)*3,-30+(id/10)*3));
            var goals=ids.Select(id=>new NavPoint(30+(id%10)*3,-30+(id/10)*3)).ToArray();
            Assert.True(session.MoveGroup(ids.Reverse().ToArray(),goals.Reverse().ToArray()));
            var group=session.GroupFor(1);Assert.AreEqual(count,group.Members.Select(m=>m.Slot).Distinct().Count());
            var slots=group.Members.ToDictionary(m=>m.Entity,m=>m.Slot);
            var bytes=WorldWire.Pack(w=>WorldWire.Write(w,session.CaptureState()));
            var restored=new NavigationSession(1,new NavGeometry(100,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            restored.ConfigureMarch(4,PlayableProfile.Default.Revision);
            restored.RestoreState(WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState));
            foreach(int id in ids)Assert.AreEqual(slots[id],restored.GroupFor(id).Members.Single(m=>m.Entity==id).Slot);
            session.Stop(ids[0],false);
            foreach(int id in ids.Skip(1))Assert.AreEqual(slots[id],session.GroupFor(id).Members.Single(m=>m.Entity==id).Slot);
        }

        [Test] public void A30V6CheckpointKeepsCertifiedSidecarsInExplicitLegacyMotionMode()
        {
            var session=ReadyPair(.5);
            var current=WorldWire.Pack(w=>WorldWire.Write(w,session.CaptureState()));
            int tag=-1;for(int i=0;i<current.Length-7;i++)if(BitConverter.ToInt32(current,i)==0x47525031){tag=i;break;}
            Assert.GreaterOrEqual(tag,0);
            // v7 appends one march row: count + group fields + two member request identities.
            int marchTail=4+37+2*12;
            var legacy=current.Take(current.Length-marchTail).ToArray();
            BitConverter.GetBytes(6).CopyTo(legacy,tag+4);
            var state=WorldWire.Unpack(legacy,WorldWire.ReadNavigationSessionState);
            var restored=new NavigationSession(1,new NavGeometry(30,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            restored.ConfigureMarch(.5,PlayableProfile.Default.Revision);
            restored.RestoreState(state);
            Assert.AreEqual(0,restored.GroupFor(1).MarchVersion);
            Assert.NotNull(restored.InstalledExecutionFor(1));Assert.NotNull(restored.InstalledExecutionFor(2));
            for(int i=0;i<30;i++)restored.Step(1d/30);
            Assert.Greater(restored.Crowd.Units.Single(u=>u.Id==2).Position.X-
                restored.Crowd.Units.Single(u=>u.Id==1).Position.X,1);
        }

        [Test] public void UnequalOriginsAndCurvedRoutesUseRemainingGeometryNotDistanceFromStart()
        {
            NavLocation At(double x,double z)=>new NavLocation(new NavPoint(x,z),NavLocation.FlatSurface);
            NavCorridorDescriptor Route(int entity,NavLocation[] points)
            {
                var legs=Enumerable.Range(0,points.Length-1).Select(i=>new NavCorridorLeg(points[i],points[i+1],
                    NavTypedLegKind.Surface,null,1,NavCorridorSection.IndividualConnector,0,0)).ToArray();
                return new NavCorridorDescriptor(1,new string('0',64),new string('0',64),new string('0',64),
                    NavLocation.FlatSurface,null,1,.72,.8,NavRouteProvenance.CommandMember,
                    1,1,1,1,0,1,entity,entity,0,entity,1,points[0],points[points.Length-1],legs);
            }
            var rear=Route(1,new[]{At(-20,0),At(20,0)});
            var ahead=Route(2,new[]{At(-10,2),At(20,2)});
            double rearProgress=GroupMarch.Project(rear,rear.Origin,double.NegativeInfinity);
            double aheadProgress=GroupMarch.Project(ahead,ahead.Origin,double.NegativeInfinity);
            Assert.AreEqual(-40,rearProgress);Assert.AreEqual(-30,aheadProgress);
            double anchor=GroupMarch.Anchor(0,false,2,1d/30,Math.Min(rearProgress,aheadProgress));
            Assert.AreEqual(0,GroupMarch.MaxSpeed(4,2,aheadProgress,anchor,4,1d/30));
            // X alone is misleading: this actor starts farther forward but has
            // a longer typed bend before the terminal.
            var curved=Route(3,new[]{At(-5,2),At(-5,12),At(20,12),At(20,2)});
            double curvedProgress=GroupMarch.Project(curved,curved.Origin,double.NegativeInfinity);
            Assert.AreEqual(-45,curvedProgress);
            Assert.AreEqual(0,GroupMarch.MaxSpeed(4,2,rearProgress,curvedProgress,4,1d/30));
            Assert.AreEqual(2,GroupMarch.MaxSpeed(4,2,curvedProgress,curvedProgress,4,1d/30));
        }

        [Test] public void GovernedMarchFieldIsEditableWithoutOpeningNavigationGroup()
        {
            var field=PlayableProfileMetadata.Fields.Single(x=>x.Path=="groupMarch.maximumStretch");
            Assert.AreEqual("Group march",field.Group);Assert.AreEqual("m",field.Unit);
            Assert.AreEqual(4,field.Read(PlayableProfile.Default.CopyData()));Assert.True(NativeBalanceFields.Editable(field));
            Assert.False(NativeBalanceFields.Editable(PlayableProfileMetadata.Fields.Single(x=>x.Group=="Navigation")));
            var data=PlayableProfile.Default.CopyData();field.Write(data,.5);
            Assert.AreEqual(.5,PlayableProfile.Create(data).GroupMarchMaximumStretch);
            data.groupMarchMaximumStretch=.55;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            Assert.Throws<ArgumentOutOfRangeException>(()=>field.Write(data,100));
        }

        [Test] public void MalformedAnchorProgressAndRequestCannotRestoreCertifiedMarch()
        {
            var session=ReadyPair(.5);session.Step(1d/30);
            NavigationSession Restore(NavigationSessionState state){
                var result=new NavigationSession(1,new NavGeometry(30,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
                result.ConfigureMarch(.5,PlayableProfile.Default.Revision);result.RestoreState(state);return result;
            }
            NavigationSessionState Copy()=>WorldWire.Unpack(WorldWire.Pack(w=>WorldWire.Write(w,session.CaptureState())),WorldWire.ReadNavigationSessionState);
            Assert.NotNull(Restore(Copy()));
            var anchor=Copy();anchor.Groups[0].MarchAnchor=-1000000;
            Assert.Throws<ArgumentException>(()=>Restore(anchor));
            var progress=Copy();progress.Groups[0].Members[0].Progress=1000000;
            Assert.Throws<ArgumentException>(()=>Restore(progress));
            var request=Copy();request.Groups[0].Members[0].ProgressRequest=request.RequestSequence;
            if(request.Groups[0].Members[0].ProgressRequest==session.GroupFor(1).Members[0].ProgressRequest)
                request.Groups[0].Members[0].ProgressRequest=0;
            Assert.Throws<ArgumentException>(()=>Restore(request));
        }

        [Test] public void PriorV6WholeWorldPositionalTermsRestoreAsLegacyMarch()
        {
            var profile=PlayableProfile.Default;
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var domain=Activator.CreateInstance(type,flags,null,new object[]{profile,1L,false},null);
            int id=(int)type.GetMethod("SpawnUnit",flags).Invoke(domain,new object[]{new NavPoint(-12,-12),PlayableOwner.Player,PlayableEntityKind.Tank});
            var nav=(NavigationSession)type.GetProperty("Navigation",flags).GetValue(domain);
            var command=new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{id},new NavPoint(-8,-12)).AsHuman();
            Assert.AreEqual(PlayableCommandStatus.Applied,type.GetMethod("Apply",flags).Invoke(domain,new object[]{command,null}));
            var bytes=(byte[])type.GetMethod("CaptureWorldBytes",flags).Invoke(domain,new object[]{19,"legacy-march"});
            var world=PlayableWorldState.Decode(bytes);
            world.Binding=WorldWire.BindingLegacyMarch(profile);
            using(var input=new MemoryStream(world.Domain))using(var r=new BinaryReader(input))
            using(var output=new MemoryStream())using(var w=new BinaryWriter(output)){
                int terms=r.ReadInt32();w.Write(terms);
                for(int i=0;i<terms;i++){
                    w.Write(r.ReadInt32());WorldWire.String(w,WorldWire.String(r));
                    foreach(var field in PlayableProfileMetadata.Fields){
                        double value=r.ReadDouble();if(field.Path!="groupMarch.maximumStretch")w.Write(value);
                    }
                }
                w.Write(r.ReadBytes((int)(input.Length-input.Position)));
                world.Domain=output.ToArray();
            }
            int tag=-1;for(int i=0;i<world.Navigation.Length-7;i++)if(BitConverter.ToInt32(world.Navigation,i)==0x47525031){tag=i;break;}
            Assert.GreaterOrEqual(tag,0);
            const int singleGroupTail=4+37+12;
            world.Navigation=world.Navigation.Take(world.Navigation.Length-singleGroupTail).ToArray();
            BitConverter.GetBytes(6).CopyTo(world.Navigation,tag+4);
            var restored=type.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{world.Encode(),profile,19,"legacy-march"});
            var restoredNav=(NavigationSession)type.GetProperty("Navigation",flags).GetValue(restored);
            Assert.AreEqual(0,restoredNav.GroupFor(id).MarchVersion);
            Assert.AreEqual(1,restoredNav.PendingCount);
        }
    }
}
