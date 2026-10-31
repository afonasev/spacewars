using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class CrossingsHydroworksLayoutTests
    {
        static PlayableProfile Profile=>PlayableProfile.ThreeCrossingsDefault;
        static OfflineParticipant[] Roster(int count)=>Enumerable.Range(0,count).Select(i=>new OfflineParticipant("p"+i,i+1,count==2?i+1:i<2?1:2,OfflineControl.Human)).ToArray();
        static OfflineMatchConfiguration Config(int count,int seed){var p=Profile;return ((ThreeCrossingsMap)p.AuthoredMap).Configuration(p,seed,Roster(count));}
        [TestCase(2)][TestCase(3)][TestCase(4)] public void SeededCornerBanksAndDiagonalsKeepStableIdentityAndVaryOrientation(int count)
        {
            var placements=new HashSet<string>();var diagonals=new HashSet<int>();
            for(int seed=0;seed<64;seed++){
                var c=Config(count,seed);var again=Config(count,seed);CollectionAssert.AreEqual(c.Assignments,again.Assignments);
                CollectionAssert.AreEqual(new[]{1,2,3,4},c.Starts.Select(s=>s.SiteId));
                Assert.AreEqual(count,c.Sites.Count(s=>s.Kind==PlayableBuildingKind.Headquarters));
                Assert.AreEqual(4-count,c.Sites.Count(s=>s.Kind==PlayableBuildingKind.Outpost));
                for(int a=0;a<count;a++)for(int b=a+1;b<count;b++){
                    var x=c.Starts[c.Assignments[a]].Position;var y=c.Starts[c.Assignments[b]].Position;
                    Assert.AreEqual(c.Roster[a].Team==c.Roster[b].Team,Math.Sign(x.X)==Math.Sign(y.X));
                    if(count==2){Assert.AreEqual(-x.Z,y.Z);Assert.AreEqual(-x.X,y.X);diagonals.Add(Math.Sign(x.X*x.Z));}
                }
                placements.Add(string.Join(",",c.Assignments));
            }
            Assert.GreaterOrEqual(placements.Count,count==2?4:count==3?8:8,"Sides, ally seats and unused corner must vary");
            if(count==2)Assert.AreEqual(2,diagonals.Count);
        }
        [TestCase(2)][TestCase(3)][TestCase(4)] public void ActualAuthorityAndCurrentSaveKeepHomesOutpostsAndConfig(int count)
        {
            var c=Config(count,7);var a=new OfflineParticipantAuthority(c,71);var bytes=a.CaptureBytes();
            using(var envelope=PlayableWorldState.Decode(bytes)){Assert.AreEqual(PlayableWorldState.CurrentVersion,envelope.Version);Assert.AreEqual(7,envelope.Seed);Assert.AreEqual(0,envelope.Tick);}
            var b=OfflineParticipantAuthority.Restore(bytes,c);
            CollectionAssert.AreEqual(bytes,b.CaptureBytes());
            foreach(var r in c.Roster){
                int i=Array.IndexOf(c.Roster.ToArray(),r);var view=a.View(r.Id);var home=c.Starts[c.Assignments[i]];
                Assert.AreEqual(home.SiteId,view.Buildings.Single(x=>x.Owner==view.Owner&&x.Kind==PlayableBuildingKind.Headquarters).SiteId);
                Assert.AreEqual(home.ExplorerAnchor,view.Entities.Single(x=>x.Owner==view.Owner).Position);
                var restored=b.View(r.Id);
                CollectionAssert.AreEqual(view.Sites.Select(x=>(x.Site.Id,x.Site.Kind,x.Owner,x.Claimant,x.Progress)),restored.Sites.Select(x=>(x.Site.Id,x.Site.Kind,x.Owner,x.Claimant,x.Progress)));
                foreach(var site in view.Sites.Where(s=>s.Site.Kind==PlayableBuildingKind.Outpost)){
                    Assert.IsNull(site.Owner);Assert.IsNull(site.Claimant);Assert.AreEqual(0,site.CenterId);Assert.AreEqual(0,site.Progress);
                }
            }
            var restart=Config(count,7);CollectionAssert.AreEqual(c.Assignments,restart.Assignments);
            CollectionAssert.AreEqual(c.Sites.Select(s=>(s.Id,s.Kind,s.Position)),restart.Sites.Select(s=>(s.Id,s.Kind,s.Position)));
        }
        [Test] public void EveryCornerReservesFullHqAndOutpostSlotsTurretsAndExits()
        {
            var p=Profile;var m=(ThreeCrossingsMap)p.AuthoredMap;
            foreach(var sites in new[]{m.ReservedSites(p),m.MatchSites(p,new[]{1,4}),m.MatchSites(p,new[]{2,3,4})}){
                var terrain=new NavGeometry(m.HalfExtent,m.MovementBlockers.ToArray(),1);var obstacles=m.MovementBlockers.ToList();
                foreach(var site in sites){
                    foreach(var reserve in new[]{(site.Position,TerritoryRules.Radius(p,site.Kind))}.Concat(site.Slots.Select(s=>(s.Position,Math.Max(p.HangarFootprintRadius,Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius)))))).Concat(site.DefenseSlots(p).Select(s=>(s.Position,p.TurretFootprintRadius)))){
                        Assert.True(terrain.IsFree(reserve.Item1,reserve.Item2*Math.Sqrt(2)+p.TankCollisionRadius),"terrain reserve site "+site.Id);
                        double y=m.SurfaceHeight(reserve.Item1);
                        foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1})Assert.AreEqual(y,m.SurfaceHeight(new NavPoint(reserve.Item1.X+sx*reserve.Item2,reserve.Item1.Z+sz*reserve.Item2)),1e-9);
                        obstacles.Add(new NavObstacle(reserve.Item1.X-reserve.Item2,reserve.Item1.Z-reserve.Item2,reserve.Item1.X+reserve.Item2,reserve.Item1.Z+reserve.Item2));
                    }
                }
                var built=new NavGeometry(m.HalfExtent,obstacles.ToArray(),2);var router=new SharedFlowRouter(built,p.Navigation);
                foreach(var site in sites)foreach(var slot in site.Slots){
                    var exit=FactoryReleaseRules.Exit(p,slot.Position,TerritoryRules.BuildingHeading,PlayableEntityKind.Tank);
                    Assert.True(built.IsFree(exit,p.TankCollisionRadius),"exit "+site.Id+":"+slot.Id);
                    Assert.IsNotEmpty(router.FindPath(exit,new NavPoint(-18,0)),"connected exit "+site.Id+":"+slot.Id);
                }
            }
        }
        [TestCase(2)][TestCase(3)][TestCase(4)] public void RealLobbyLaunchUsesAdaptiveConfiguration(int count)
        {
            var p=Profile;var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            while(setup.Participants.Count<count)Assert.IsNull(setup.AddParticipant(false,0,setup.Capacity(p)));
            Assert.IsNull(setup.Validate(p,true,true));
            using(var runtime=PlayableRuntime.CreateLobbyMatch(p,91,setup,startPaused:true)){
                Assert.AreEqual(count,runtime.LobbyConfiguration.Participants.Count);
                var expected=((ThreeCrossingsMap)p.AuthoredMap).Configuration(p,7,setup.Participants.Select((v,i)=>new OfflineParticipant(i==0?"player-1":"foundry-"+(i+1),i+1,v.Team,v.Human?OfflineControl.Human:OfflineControl.Ai)).ToArray());
                foreach(var r in expected.Roster){var view=runtime.ParticipantView(r.Id);int i=Array.IndexOf(expected.Roster.ToArray(),r);
                    Assert.AreEqual(expected.Starts[expected.Assignments[i]].SiteId,view.Buildings.Single(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Headquarters).SiteId);
                }
                Assert.AreEqual(0,runtime.Latest.Tick);
                runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,3000));
            }
        }
        [Test] public void UnseededLobbyPinsResolvedSeedForRestartWithoutMutatingDraft()
        {
            var p=Profile;var draft=new NativeLobbyConfiguration();
            using(var first=PlayableRuntime.CreateLobbyMatch(p,93,draft,startPaused:true)){
                var saved=first.LobbyConfiguration;Assert.True(saved.HasExplicitSeed);Assert.False(draft.HasExplicitSeed);
                using(var restart=PlayableRuntime.CreateLobbyMatch(p,94,saved,startPaused:true)){
                    Assert.AreEqual(saved.ExplicitSeed,restart.LobbyConfiguration.ExplicitSeed);
                    Assert.AreEqual(first.ParticipantView("player-1").Buildings.Single(b=>b.Kind==PlayableBuildingKind.Headquarters).SiteId,restart.ParticipantView("player-1").Buildings.Single(b=>b.Kind==PlayableBuildingKind.Headquarters).SiteId);
                    Assert.AreEqual(0,first.Latest.Tick);Assert.AreEqual(0,restart.Latest.Tick);
                    restart.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>restart.IsStopped,3000));
                }
                first.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>first.IsStopped,3000));
            }
        }
        [Test] public void AddingPlayersKeepsChosenTeamIdsAndRejectsTamperedBindings()
        {
            var p=Profile;var setup=new NativeLobbyConfiguration();setup.InitializeParticipants();setup.Participants[0].Team=3;setup.Participants[1].Team=7;
            Assert.IsNull(setup.AddParticipant(false,0,4));Assert.IsNull(setup.AddParticipant(false,0,4));Assert.IsNull(setup.Validate(p,true,true));
            CollectionAssert.AreEquivalent(new[]{3,7},setup.Participants.Select(v=>v.Team).Distinct());
            var c=((ThreeCrossingsMap)p.AuthoredMap).Configuration(p,7,Roster(2));
            var bad=c.Starts.Select(v=>new OfflineStart(v.Id,v.SiteId,v.Position,v.ExplorerAnchor,v.Heading,null)).ToArray();
            Assert.Throws<ArgumentException>(()=>new OfflineMatchConfiguration(p,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,7,c.Roster.ToArray(),bad,c.Sites.ToArray(),c.Obstacles.ToArray(),new double[4,4],terrain:p.AuthoredMap.Id));
            var a=new OfflineParticipantAuthority(c,71);var bytes=a.CaptureBytes();
            var other=((ThreeCrossingsMap)p.AuthoredMap).Configuration(p,8,Roster(2));
            Assert.Throws<ArgumentException>(()=>OfflineParticipantAuthority.Restore(bytes,other));
        }
        [Test] public void UnsupportedTeamsCannotLaunchOrResolve()
        {
            var p=Profile;var setup=new NativeLobbyConfiguration();setup.InitializeParticipants();
            setup.Participants.Add(new NativeLobbyParticipant{Team=3,Color=2});Assert.IsNotNull(setup.Validate(p,true,true));
            Assert.Throws<ArgumentException>(()=>((ThreeCrossingsMap)p.AuthoredMap).Configuration(p,7,new[]{new OfflineParticipant("a",1,1,OfflineControl.Human),new OfflineParticipant("b",2,2,OfflineControl.Ai),new OfflineParticipant("c",3,3,OfflineControl.Ai)}));
        }
        [Test] public void CenterClearWidthAndApproachesDoNotPinch()
        {
            var p=Profile;var m=(ThreeCrossingsMap)p.AuthoredMap;Assert.AreEqual(2*m.SideBridgeWidth,m.CentralBridgeWidth);
            foreach(double radius in new[]{p.ExplorerCollisionRadius,p.TankCollisionRadius,p.ShkvalCollisionRadius})
                foreach(double sign in new[]{-1d,0,1d})Assert.True(m.SupportsSweep(new NavPoint(-20,sign*(m.CentralBridgeWidth/2-radius)),new NavPoint(20,sign*(m.CentralBridgeWidth/2-radius)),radius));
            Assert.AreEqual(4,m.ReservedSites(p).Count(s=>s.Kind==PlayableBuildingKind.Headquarters));
        }
    }
}
