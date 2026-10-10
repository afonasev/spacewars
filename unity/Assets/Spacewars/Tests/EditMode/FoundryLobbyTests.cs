using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class FoundryLobbyTests
    {
        private static PlayableProfile Profile(FoundryProfileData data=null)=>PlayableProfile.Create(PlayableProfile.Default.CopyData(),new FoundryMap(data??new FoundryProfileData()));
        [Test] public void OrdinaryLobbyCreatesSixDistinctTeamsOwnersAndHumanIngress()
        {
            var profile=Profile();var setup=new NativeLobbyConfiguration{Foundry=true,HasExplicitSeed=true,ExplicitSeed=7009,Difficulty=AiDifficulty.Veteran};
            using(var runtime=PlayableRuntime.CreateLobbyMatch(profile,9,setup,true))
            {
                var frame=runtime.OfflineFrame;Assert.AreEqual(6,frame.Views.Count);Assert.AreEqual(0,runtime.Latest.Tick);Assert.AreEqual(setup.Difficulty,runtime.EnemyAiConfig.Difficulty);
                for(int i=0;i<6;i++){var view=frame.Views.Values.Single(v=>(int)v.Owner==i);Assert.AreEqual(i<3?1:2,view.Team);Assert.AreEqual(6,view.Participants.Count);Assert.AreEqual(1,view.Entities.Count(e=>(int)e.Owner==i));Assert.AreEqual(1,view.Buildings.Count(b=>(int)b.Owner==i));Assert.AreEqual(profile.Headquarters((PlayableOwner)i),view.Buildings.Single(b=>(int)b.Owner==i).Position);}
                Assert.AreEqual(1,runtime.Latest.Participants.Count(p=>p.Control==OfflineControl.Human));Assert.AreEqual("player-1",runtime.Latest.OwnerId);
                runtime.RequestPause(false);Assert.True(SpinWait.SpinUntil(()=>!runtime.Latest.Paused&&runtime.Latest.Tick>0,3000));
                var own=runtime.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player);
                Assert.True(runtime.TrySubmit(new PlayableCommand(9,1,"player-1",PlayableCommandKind.Move,new[]{own.Id},new NavPoint(-44,94))).Accepted);
                var bot=frame.Views["foundry-2"].Entities.Single(e=>(int)e.Owner==1);
                Assert.AreEqual(PlayableCommandStatus.InvalidOwner,runtime.TrySubmit(new PlayableCommand(9,1,"foundry-2",PlayableCommandKind.Move,new[]{bot.Id},new NavPoint(0,80))).Status);
                runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));
            }
        }
        [Test] public void ApprovedDecksPocketOpeningsAndRiverAreSupportedAndBlocked()
        {
            var p=Profile();var m=(FoundryMap)p.AuthoredMap;
            foreach(var site in m.Sites(p).Where(s=>s.Kind!=PlayableBuildingKind.Mine))
            {
                Assert.AreEqual(m.RearHeight,m.SurfaceHeight(site.Position));
                foreach(var slot in site.Slots)Assert.AreEqual(m.RearHeight,m.SurfaceHeight(slot.Position));
            }
            foreach(int end in new[]{-1,1})
            {
                Assert.True(m.SupportsSweep(m.Point(-38,end*70),m.Point(-65,end*70),p.TankCollisionRadius));
                Assert.True(m.SupportsSweep(m.Point(-38,end*70),m.Point(-38,end*40),p.TankCollisionRadius));
                Assert.False(m.SupportsSweep(m.Point(-38,end*70),m.Point(-38,end*94),p.TankCollisionRadius));
                Assert.True(m.SupportsSweep(m.Point(38,end*70),m.Point(65,end*70),p.TankCollisionRadius));
                Assert.True(m.SupportsSweep(m.Point(38,end*70),m.Point(38,end*94),p.TankCollisionRadius));
                Assert.False(m.SupportsSweep(m.Point(38,end*70),m.Point(38,end*40),p.TankCollisionRadius));
            }
            var river=m.Lava.Single(l=>l.MinX>90);Assert.LessOrEqual(river.MinZ,-140);Assert.GreaterOrEqual(river.MaxZ,140);
            for(int z=-130;z<=130;z+=10)Assert.False(m.SupportsFootprint(m.Point(121,z),p.TankCollisionRadius));
            Assert.Throws<ArgumentException>(()=>Profile(new FoundryProfileData{rearHeight=5,flankHeight=4}));
        }
        [TestCase(1d)] [TestCase(1.5d)] public void PocketRockElbowsHaveNoOpenSlits(double scale)
        {
            var m=(FoundryMap)Profile(new FoundryProfileData{scale=scale}).AuthoredMap;
            var geometry=new NavGeometry(m.HalfExtent,m.Solids.ToArray(),m.Revision);
            foreach(int end in new[]{-1,1})
            {
                // Cover the joining strips, including the old two-metre gaps.
                for(double x=-27.75;x<-19;x+=.5)for(double z=84.25;z<89;z+=.5)
                    Assert.False(geometry.IsFree(m.Point(x,end*z),.01*scale),"left rock slit at "+x+","+end*z);
                for(double x=19.25;x<28;x+=.5)for(double z=52.25;z<(x<26?58:56);z+=.5)
                    Assert.False(geometry.IsFree(m.Point(x,end*z),.01*scale),"right rock slit at "+x+","+end*z);
            }
        }
        [Test] public void TeamOutcomeWaitsForAllThreeOpposingOwners()
        {
            var p=Profile();var config=((FoundryMap)p.AuthoredMap).Configuration(p,7);var authority=new OfflineParticipantAuthority(config,1);
            var domain=typeof(OfflineParticipantAuthority).GetField("domain",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(authority);var damage=domain.GetType().GetMethod("Damage",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            for(int i=3;i<6;i++)
            {
                var view=authority.View(config.Roster[i].Id);var home=view.Buildings.Single(b=>(int)b.Owner==i&&b.Kind==PlayableBuildingKind.Headquarters);damage.Invoke(domain,new object[]{home.Id,100000});authority.Step(1d/30);
                Assert.AreEqual(i==5?PlayableMatchOutcome.TeamWon:PlayableMatchOutcome.Playing,authority.View(config.Roster[0].Id).Outcome);
            }
            Assert.AreEqual(1,authority.WinnerTeam);Assert.AreEqual(6,authority.Result.Players.Count);Assert.False(authority.Result.Manual);
        }
    }
}
