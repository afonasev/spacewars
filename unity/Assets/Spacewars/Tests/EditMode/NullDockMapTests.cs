using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class NullDockMapTests
    {
        [Test] public void LobbySelectionRetainsDuelAndTerrainBinding()
        {
            var map=new NullDockMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var lobby=new NativeLobbyConfiguration{NullDock=true,HasExplicitSeed=true,ExplicitSeed=26102026};
            lobby.InitializeParticipants();Assert.AreEqual(2,lobby.Capacity(p));Assert.AreEqual(2,lobby.Participants.Count);
            Assert.IsNull(lobby.Validate(p,true,true));Assert.True(lobby.Copy().NullDock);
            using(var runtime=PlayableRuntime.CreateLobbyMatch(p,1,lobby,startPaused:true)){
                Assert.True(runtime.LobbyConfiguration.NullDock);Assert.AreEqual(2,runtime.Latest.Participants.Count);
                Assert.Zero(runtime.Latest.Tick);Assert.IsNull(runtime.Latest.Failure);
            }
        }
        [Test] public void DefensiveRingsPreserveConstructionAndProductionClearance()
        {
            var map=new NullDockMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            double ordinary=Math.Max(p.HangarFootprintRadius,Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius)));
            foreach(var site in map.Sites(p).Where(s=>TerritoryRules.Center(s.Kind))){
                var before=site.Slots.Select(s=>s.Position).ToArray();var defense=site.DefenseSlots(p);
                Assert.AreEqual(site.Slots.Count,defense.Count);CollectionAssert.AreEqual(before,site.Slots.Select(s=>s.Position));
                double ring=Distance(site.Position,defense[0].Position);
                foreach(var pad in defense){
                    Assert.AreEqual(ring,Distance(site.Position,pad.Position),1e-9);
                    Assert.True(map.SupportsFootprint(pad.Position,p.TurretFootprintRadius*Math.Sqrt(2)+p.TankCollisionRadius));
                    foreach(var slot in site.Slots){
                        double dx=Math.Max(0,Math.Abs(pad.Position.X-slot.Position.X)-ordinary-p.TurretFootprintRadius);
                        double dz=Math.Max(0,Math.Abs(pad.Position.Z-slot.Position.Z)-ordinary-p.TurretFootprintRadius);
                        Assert.GreaterOrEqual(Math.Sqrt(dx*dx+dz*dz),p.TurretPassageWidth-1e-9);
                    }
                }
                var obstacles=map.MovementBlockers.Concat(site.Slots.Select(s=>new NavObstacle(s.Position.X-ordinary,s.Position.Z-ordinary,s.Position.X+ordinary,s.Position.Z+ordinary)))
                    .Concat(defense.Select(s=>new NavObstacle(s.Position.X-p.TurretFootprintRadius,s.Position.Z-p.TurretFootprintRadius,s.Position.X+p.TurretFootprintRadius,s.Position.Z+p.TurretFootprintRadius))).ToArray();
                var geometry=new NavGeometry(map.HalfExtent,obstacles,1);
                foreach(var slot in site.Slots)Assert.True(geometry.IsFree(new NavPoint(slot.Position.X,slot.Position.Z-p.FactoryExitDistance),p.TankCollisionRadius));
            }
        }
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
        [TestCase(2d,6d)] [TestCase(4d,4d)] [TestCase(1d,10d)]
        public void CurrentSaveRestoresExactMapBinding(double terrace,double module)
        {
            var map=new NullDockMap(new NullDockProfileData{terraceHeight=terrace,moduleHeight=module});
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var roster=new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("enemy-1",2,2,OfflineControl.Ai)};
            var config=map.Configuration(p,26102026,roster);var original=new OfflineParticipantAuthority(config,1);OfflineParticipantAuthority restored=null;
            try{
                // Current-format initial world, no commands, movement, construction or AI witness.
                var bytes=original.CaptureBytes();
                var loadedMap=new NullDockMap(new NullDockProfileData{terraceHeight=terrace,moduleHeight=module});
                var loadedConfig=loadedMap.Configuration(PlayableProfile.Create(p.CopyData(),loadedMap),26102026,roster);
                restored=OfflineParticipantAuthority.Restore(bytes,loadedConfig);
                CollectionAssert.AreEqual(bytes,restored.CaptureBytes());
                var terrain=(NullDockMap)restored.Configuration.Profile.AuthoredMap;
                Assert.AreEqual(map.Id,terrain.Id);Assert.AreEqual(terrace,terrain.TerraceHeight);Assert.AreEqual(module,terrain.ModuleHeight);
                CollectionAssert.AreEqual(original.View("player-1").Sites.Select(site=>site.Site.Position),restored.View("player-1").Sites.Select(site=>site.Site.Position));
                var wrongMap=new NullDockMap(new NullDockProfileData{terraceHeight=terrace,moduleHeight=module==10?6:10});
                var wrongConfig=wrongMap.Configuration(PlayableProfile.Create(p.CopyData(),wrongMap),26102026,roster);
                Assert.Throws<ArgumentException>(()=>OfflineParticipantAuthority.Restore(bytes,wrongConfig),"Do not reinterpret save under a different terrain profile");
            }finally{original.Domain.DestroyOwnedNavigation();restored?.Domain.DestroyOwnedNavigation();}
        }
        [Test] public void ApprovedSitesAndAllConstructionEnvelopesFit()
        {
            var map=new NullDockMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var sites=map.Sites(p);Assert.AreEqual(10,sites.Length);
            Assert.AreEqual(2,sites.Count(s=>s.Kind==PlayableBuildingKind.Headquarters));
            Assert.AreEqual(2,sites.Count(s=>s.Kind==PlayableBuildingKind.Outpost));
            Assert.AreEqual(6,sites.Count(s=>s.Kind==PlayableBuildingKind.Mine));
            Assert.AreEqual(16,sites.Sum(s=>s.Slots.Count));
            foreach(var site in sites)foreach(var slot in site.Slots)Assert.True(map.SupportsFootprint(slot.Position,p.FactoryFootprintRadius),"slot "+site.Id+"/"+slot.Id);
            Assert.True(PartitionNavigationProvider.SupportsTerrain(map));
            var roster=new[]{new OfflineParticipant("a",1,1,OfflineControl.Human),new OfflineParticipant("b",2,2,OfflineControl.Ai)};
            var config=map.Configuration(p,26102026,roster);Assert.AreEqual(2,config.Starts.Count);
            foreach(var start in config.Starts)Assert.True(map.SupportsFootprint(start.ExplorerAnchor,p.ExplorerCollisionRadius));
        }
        [Test] public void PocketMinesOccupyCornersWithClearApproaches()
        {
            var map=new NullDockMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var sites=map.Sites(p);
            Assert.AreEqual(map.Point(12,146),sites.Single(s=>s.Id==5).Position);
            Assert.AreEqual(map.Point(180,14),sites.Single(s=>s.Id==6).Position);
            foreach(var id in new[]{5,6}){
                var pos=sites.Single(s=>s.Id==id).Position;Assert.True(map.SupportsFootprint(pos,p.FactoryFootprintRadius));
                // Both pocket entrances retain a clear tank-width approach to the relocated mine reserve.
                var bend=id==5?map.Point(22,124):map.Point(170,36);
                var ramp=id==5?map.Point(30,110):map.Point(162,50);
                Assert.True(map.SupportsSweep(ramp,bend,p.TankCollisionRadius));
                Assert.True(map.SupportsSweep(bend,pos,p.TankCollisionRadius));
            }
        }
        [Test] public void EveryAuthoredRampConnectsBothDirections()
        {
            var map=new NullDockMap();double r=PlayableProfile.Default.TankCollisionRadius;
            var ends=new double[,]{{30,50,30,62},{30,110,30,98},{58,80,46,80},{162,50,162,62},{162,110,162,98},{134,80,146,80},{86,26,98,26},{166,24,154,24},{106,134,94,134},{26,136,38,136}};
            for(int i=0;i<10;i++){
                var lo=map.Point(ends[i,0],ends[i,1]);var hi=map.Point(ends[i,2],ends[i,3]);
                Assert.AreEqual(0,map.SurfaceHeight(lo),1e-6);Assert.AreEqual(2,map.SurfaceHeight(hi),1e-6);
                Assert.True(map.SupportsSweep(lo,hi,r),"up R"+(i+1));Assert.True(map.SupportsSweep(hi,lo,r),"down R"+(i+1));
            }
        }
        [Test] public void SpaceAndSealedPlatformGapsHaveNoShortcut()
        {
            var map=new NullDockMap();double r=PlayableProfile.Default.TankCollisionRadius;
            foreach(var point in new[]{map.Point(7,80),map.Point(80,7),map.Point(185,80),map.Point(110,153)}){
                Assert.IsNull(map.SupportAt(point));Assert.False(map.SupportsFootprint(point,r));
            }
            Assert.AreEqual(7,map.Modules.Count);
            Assert.False(map.SupportsSweep(map.Point(42,96),map.Point(42,112),r),"P6 seals A/F2");
            Assert.False(map.SupportsSweep(map.Point(150,48),map.Point(150,64),r),"P7 seals F1/B");
            Assert.False(map.SupportsSweep(map.Point(60,80),map.Point(132,80),r),"central diagonal blocks direct attack");
            Assert.False(map.SupportsSweep(map.Point(120,58),map.Point(120,46),r),"no F1 center ramp");
            Assert.False(map.SupportsSweep(map.Point(72,102),map.Point(72,114),r),"no F2 center ramp");
            foreach(var module in map.Modules)Assert.False(map.SupportsFootprint(module.Footprint.Vertices[0],r));
        }
    }
}
