using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class OrbitalTerrainScanTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        Type type;object domain;PlayableProfile profile;OfflineMatchConfiguration config;long sequence;
        [SetUp] public void Setup()
        {
            var map=new AiTestMap();profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            config=map.Configuration(profile,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,1,OfflineControl.Human),new OfflineParticipant("player-3",3,2,OfflineControl.Human)});
            type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain");domain=Activator.CreateInstance(type,F,null,new object[]{profile,1L,config},null);
            type.GetMethod("CreditOrbital",F).Invoke(domain,new object[]{(PlayableOwner?)PlayableOwner.Player,(PlayableOwner)2,250d});
            Assert.AreEqual(PlayableCommandStatus.Applied,Apply(PlayableCommandKind.ChooseOrbital,default));
        }
        PlayableCommandStatus Apply(PlayableCommandKind kind,NavPoint point)=>(PlayableCommandStatus)type.GetMethod("Apply",F).Invoke(domain,new object[]{new PlayableCommand(1,++sequence,"player-1",kind,null,point,targetId:1),null});
        PlayableSnapshot View(PlayableOwner owner)=>(PlayableSnapshot)type.GetMethod("PlayerSnapshot",F).Invoke(domain,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner});
        void Advance(double seconds)=>type.GetMethod("AdvanceOrbital",F).Invoke(domain,new object[]{seconds});
        byte[] Bytes()=>(byte[])type.GetMethod("CaptureWorldBytes",F).Invoke(domain,new object[]{7,config.SourceIdentity});
        [Test] public void HiddenPointRevealsWholeCircleToTeamThenExpiresWithoutMovingUnits()
        {
            var target=new NavPoint(0,0);var before=View(PlayableOwner.Player);Assert.False(before.Vision.IsVisible(target));
            Assert.AreEqual(PlayableCommandStatus.Applied,Apply(PlayableCommandKind.UseOrbital,target));
            var own=View(PlayableOwner.Player);var ally=View(PlayableOwner.Enemy);var enemy=View((PlayableOwner)2);
            foreach(var v in new[]{own,ally}){Assert.True(v.Vision.IsVisible(target));Assert.True(v.Vision.IsVisible(new NavPoint(9.9,0)));Assert.Greater(v.ScanLines.Count,0);}
            Assert.False(enemy.Vision.IsVisible(target));Assert.Zero(enemy.ScanLines.Count,"hidden scan centre/geometry must not leak");
            CollectionAssert.AreEqual(before.Entities.Select(e=>e.Position),own.Entities.Select(e=>e.Position));
            Advance(2.99);Assert.True(View(PlayableOwner.Player).Vision.IsVisible(target));Advance(.01);
            var after=View(PlayableOwner.Player);Assert.False(after.Vision.IsVisible(target));Assert.Zero(after.ScanLines.Count);Assert.Greater(after.Vision.DiscoveredCells.Count,before.Vision.DiscoveredCells.Count);
        }
        [Test] public void InvalidTargetSpendsNothingAndActiveScanRestoresExactly()
        {
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Apply(PlayableCommandKind.UseOrbital,new NavPoint(145,0)));Assert.Zero(View(PlayableOwner.Player).Orbital.Abilities[0].Remaining);
            Assert.AreEqual(PlayableCommandStatus.Applied,Apply(PlayableCommandKind.UseOrbital,new NavPoint(0,0)));Advance(.75);
            var before=Bytes();var restored=OfflineParticipantAuthority.Restore(before,config);CollectionAssert.AreEqual(before,restored.CaptureBytes());
            Assert.True(restored.View("player-2").Vision.IsVisible(new NavPoint(0,0)));Assert.Greater(restored.View("player-1").ScanLines.Count,0);
            domain=typeof(OfflineParticipantAuthority).GetProperty("Domain",F).GetValue(restored);Advance(2.24);Assert.True(View(PlayableOwner.Player).Vision.IsVisible(new NavPoint(0,0)));Advance(.01);Assert.False(View(PlayableOwner.Player).Vision.IsVisible(new NavPoint(0,0)));
        }
        [Test] public void ScanExpiresIndependentlyAndPreservesOtherVisionSources()
        {
            var point=View(PlayableOwner.Player).Entities.First(e=>e.Owner==PlayableOwner.Player).Position;
            Assert.AreEqual(PlayableCommandStatus.Applied,Apply(PlayableCommandKind.UseOrbital,point));Advance(3);Assert.True(View(PlayableOwner.Player).Vision.IsVisible(point));Assert.Zero(View(PlayableOwner.Player).ScanLines.Count);
        }
        [Test] public void CurrentScanCheckpointContinuesEveryTickThroughExpiry()
        {
            var point=new NavPoint(0,0);Assert.AreEqual(PlayableCommandStatus.Applied,Apply(PlayableCommandKind.UseOrbital,point));
            var restored=OfflineParticipantAuthority.Restore(Bytes(),config);
            for(int i=0;i<120;i++){
                type.GetMethod("Step",F).Invoke(domain,new object[]{1d/30});restored.Step(1d/30);
                CollectionAssert.AreEqual(Bytes(),restored.CaptureBytes(),"current-format continuation tick "+i);
                foreach(var owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy,(PlayableOwner)2}){
                    var left=View(owner);var right=restored.View("player-"+((int)owner+1));
                    Assert.AreEqual(left.Vision.IsVisible(point),right.Vision.IsVisible(point),"team vision at "+i);CollectionAssert.AreEqual(left.ScanLines,right.ScanLines,"radar phase at "+i);CollectionAssert.AreEqual(left.ScanTriangles,right.ScanTriangles,"light field phase at "+i);
                }
            }
            Assert.False(View(PlayableOwner.Player).Vision.IsVisible(point));
        }
        [Test] public void ScanRemembersObservedBuildingAndShowsEffectToVisibleEnemy()
        {
            var building=View((PlayableOwner)2).Buildings.First(b=>b.Owner==(PlayableOwner)2);
            Assert.False(View(PlayableOwner.Player).Vision.IsVisible(building.Position));
            Assert.AreEqual(PlayableCommandStatus.Applied,Apply(PlayableCommandKind.UseOrbital,building.Position));
            Assert.True(View(PlayableOwner.Player).Buildings.Any(b=>b.Id==building.Id));Assert.Greater(View((PlayableOwner)2).ScanLines.Count,0);
            Advance(3);var after=View(PlayableOwner.Player);Assert.False(after.Vision.IsVisible(building.Position));Assert.True(after.Vision.KnownBuildings.Any(b=>b.Id==building.Id));Assert.True(View((PlayableOwner)2).Vision.IsVisible(building.Position));
        }
        [Test] public void OverlappingAlliedScansExpireIndependently()
        {
            var point=new NavPoint(0,0);Assert.AreEqual(PlayableCommandStatus.Applied,Apply(PlayableCommandKind.UseOrbital,point));Advance(1);
            type.GetMethod("CreditOrbital",F).Invoke(domain,new object[]{(PlayableOwner?)PlayableOwner.Enemy,(PlayableOwner)2,250d});type.GetMethod("ChooseOrbital",F).Invoke(domain,new object[]{PlayableOwner.Enemy,1});
            Assert.AreEqual(PlayableCommandStatus.Applied,type.GetMethod("UseOrbital",F).Invoke(domain,new object[]{PlayableOwner.Enemy,1,point}));
            Advance(2);Assert.True(View(PlayableOwner.Player).Vision.IsVisible(point));Advance(1);Assert.False(View(PlayableOwner.Player).Vision.IsVisible(point));
        }
        [Test] public void SectorFacesRequireOneConvexVisibleSource()
        {
            var sources=new[]{new VisionSource(new NavPoint(-3,0),1),new VisionSource(new NavPoint(3,0),1)};
            Assert.False(OrbitalScanVisibility.Covered(new NavPoint(-3,0),new NavPoint(3,0),new NavPoint(3,.5),sources),"Visible vertices must not bridge hidden space");
            Assert.True(OrbitalScanVisibility.Covered(new NavPoint(-3,0),new NavPoint(-3.5,0),new NavPoint(-3,.5),sources));
            Assert.False(OrbitalScanVisibility.Covered(new NavPoint(-3,0),new NavPoint(-3.5,0),new NavPoint(-3,2),sources));
            var circle=new[]{new VisionSource(new NavPoint(0,0),10)};
            for(int i=0;i<128;i++){double angle=i*Math.PI/64;Assert.True(OrbitalScanVisibility.Covered(new NavPoint(0,0),new NavPoint(Math.Cos(angle)*10,Math.Sin(angle)*10),new NavPoint(Math.Cos(angle+.01)*10,Math.Sin(angle+.01)*10),circle),"Tessellated circle boundary stays filled");}
            Assert.False(OrbitalScanVisibility.Covered(new NavPoint(0,0),new NavPoint(10.0001,0),new NavPoint(0,1),circle));
        }
        [Test] public void EffectFragmentsAreClippedToUnionOfVisibleCircles()
        {
            var sources=new[]{new VisionSource(new NavPoint(-3,0),1),new VisionSource(new NavPoint(3,0),1)};
            var lines=OrbitalScanVisibility.Clip(new NavPoint(-10,0),new NavPoint(10,0),.5,sources).ToArray();Assert.AreEqual(2,lines.Length);
            Assert.AreEqual(-4,lines[0].From.X,1e-9);Assert.AreEqual(-2,lines[0].To.X,1e-9);Assert.AreEqual(2,lines[1].From.X,1e-9);Assert.AreEqual(4,lines[1].To.X,1e-9);
            foreach(var segment in lines)for(int i=0;i<=10;i++){var point=new NavPoint(segment.From.X+(segment.To.X-segment.From.X)*i/10d,0);Assert.True(sources.Any(s=>Math.Abs(s.Position.X-point.X)<=s.Radius+1e-9));}
        }
    }
}
