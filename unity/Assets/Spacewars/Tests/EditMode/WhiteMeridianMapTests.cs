using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class WhiteMeridianMapTests
    {
        private static PlayableProfile Profile()=>PlayableProfile.Create(PlayableProfile.Default.CopyData(),new WhiteMeridianMap());
        [Test] public void OriginalTopologySupportsEverySiteAndCompleteFactoryExit(){
            var p=Profile();var map=(WhiteMeridianMap)p.AuthoredMap;var sites=map.Sites(p);
            Assert.AreEqual(18,sites.Length);Assert.AreEqual(4,sites.Count(s=>s.Kind==PlayableBuildingKind.Headquarters));Assert.AreEqual(6,sites.Count(s=>s.Kind==PlayableBuildingKind.Outpost));Assert.AreEqual(8,sites.Count(s=>s.Kind==PlayableBuildingKind.Mine));
            var blockers=map.MovementBlockers.ToList();
            foreach(var site in sites){
                double r=TerritoryRules.Radius(p,site.Kind);Assert.True(map.SupportsFootprint(site.Position,r*Math.Sqrt(2)),"center "+site.Id);blockers.Add(new NavObstacle(site.Position.X-r,site.Position.Z-r,site.Position.X+r,site.Position.Z+r));
                Assert.AreEqual(site.Kind==PlayableBuildingKind.Headquarters?5:site.Kind==PlayableBuildingKind.Outpost?3:0,site.Slots.Count);
                foreach(var slot in site.Slots){r=Math.Max(p.FactoryFootprintRadius,Math.Max(p.ScienceFootprintRadius,p.RefineryFootprintRadius));Assert.True(map.SupportsFootprint(slot.Position,r*Math.Sqrt(2)),"slot "+site.Id+"/"+slot.Id);blockers.Add(new NavObstacle(slot.Position.X-r,slot.Position.Z-r,slot.Position.X+r,slot.Position.Z+r));}
            }
            var geometry=new NavGeometry(map.HalfExtent,blockers.ToArray(),1);
            foreach(var site in sites)foreach(var slot in site.Slots)foreach(var kind in new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval}){
                var q=new NavPoint(slot.Position.X+Math.Cos(slot.Heading)*p.FactoryExitDistance,slot.Position.Z+Math.Sin(slot.Heading)*p.FactoryExitDistance);double r=PlayableUnitRules.Radius(p,kind);
                Assert.True(map.SupportsFootprint(q,r),"unsupported exit "+site.Id+"/"+slot.Id+" "+q);Assert.True(geometry.IsFree(q,r),"occupied full-build exit "+site.Id+"/"+slot.Id);
            }
        }
        [Test] public void AuthoredMapBindsCertifiedHostAndPreservesRampSeams(){
            var p=Profile();var map=(WhiteMeridianMap)p.AuthoredMap;
            var roster=Enumerable.Range(0,4).Select(i=>new OfflineParticipant("seat-"+i,i+1,i<2?1:2,OfflineControl.Human)).ToArray();
            var authority=new PlayableAuthorityTick(map.Configuration(p,7108,roster),1);
            Assert.AreSame(map,authority.NavigationBinding.Admission.AuthoredTerrain,"ordinary host must select the certified worker");
            var provider=new PartitionNavigationProvider(map,authority.NavigationGeometry,null);
            var clearance=new NavClearanceProfile("meridian-explorer",1,"ground",1,PlayableUnitRules.Radius(p,PlayableEntityKind.Explorer));
            Assert.True(provider.TryLocate(map.Point(-58,20),clearance,out var lower));
            Assert.True(provider.TryTrace(lower,map.Point(-58,38),clearance,out var upper,out var legs));
            Assert.AreNotEqual(lower.SurfaceId,upper.SurfaceId);
            Assert.True(legs.Any(l=>l.Kind==NavTypedLegKind.Portal));
            Assert.True(provider.TryLocate(map.Point(-74,35),clearance,out var shelf));
            Assert.True(provider.TryTrace(shelf,map.Point(-74,-35),clearance,out _,out _));
            authority.Stop();
        }
        [Test] public void BothLevelsHaveOnlyTheIntendedConnections(){
            var p=Profile();var map=(WhiteMeridianMap)p.AuthoredMap;
            foreach(var kind in new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval}){
                double r=PlayableUnitRules.Radius(p,kind);
                foreach(int sx in new[]{-1,1}){
                    Assert.True(map.SupportsSweep(map.Point(sx*74,-50),map.Point(sx*74,50),r),"continuous upper shelf");
                    Assert.False(map.SupportsSweep(map.Point(sx*40,-20),map.Point(sx*40,20),r),"lower barrier");
                    Assert.False(map.SupportsSweep(map.Point(sx*68,15),map.Point(sx*74,15),r),"middle cliff");
                    for(int z=-6;z<=6;z++)Assert.False(map.SupportsSweep(map.Point(sx*68,z),map.Point(sx*74,z),r),"barrier-shelf closure");
                    foreach(int sz in new[]{-1,1}){
                        Assert.True(map.SupportsSweep(map.Point(sx*32,sz*50),map.Point(sx*48,sz*50),r),"inner ramp");
                        Assert.True(map.SupportsSweep(map.Point(sx*58,sz*20),map.Point(sx*58,sz*38),r),"home ramp");
                    }
                }
                var central=new[]{map.Point(-40,20),map.Point(-13,26),map.Point(-13,-20),map.Point(-40,-20)};
                for(int i=1;i<central.Length;i++)Assert.True(map.SupportsSweep(central[i-1],central[i],r),"central detour "+i);
                foreach(var q in central)Assert.True(map.SupportsFootprint(map.Point(-q.X,-q.Z),r));
            }
        }
        [Test] public void CentralBarrierHasOnlyWreckGapsAndSecondaryEntryMissesMine(){
            var p=Profile();var map=(WhiteMeridianMap)p.AuthoredMap;
            foreach(var kind in new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval}){
                double r=PlayableUnitRules.Radius(p,kind);
                foreach(int side in new[]{-1,1}){
                    Assert.True(map.SupportsSweep(map.Point(side*13,-22),map.Point(side*13,22),r),"wreck gap");
                    Assert.False(map.SupportsSweep(map.Point(side*31,-20),map.Point(side*31,20),r),"old end bypass closed");
                    Assert.True(map.SupportsSweep(map.Point(side*74,-50),map.Point(side*74,50),r),"allied upper route");
                    foreach(int z in new[]{-1,1}){
                        Assert.False(new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),1).IsFree(map.Point(side*40,z*38),r),"terrace shortcut occupied by ridge");
                        Assert.True(map.SupportsSweep(map.Point(side*58,z*20),map.Point(side*58,z*38),r),"straight secondary ramp");
                        Assert.False(map.SupportsSweep(map.Point(side*66,z*20),map.Point(side*66,z*38),r),"old broad ramp side closed");
                    }
                }
            }
            // Barrier footprints form a continuous chain from each shelf to its outer
            // wreck section. Sampling the full north/south line rejects all old bypasses.
            for(int x=-69;x<=69;x++){
                if(x>-(15+map.WreckSpread)&&x<-(6+map.MiddleWreckExtension)||x>6+map.MiddleWreckExtension&&x<15+map.WreckSpread)continue;
                Assert.False(map.SupportsSweep(map.Point(x,-22),map.Point(x,22),.25),"unexpected lower crossing at x="+x);
            }
        }
        [Test] public void B4LengthensCenterWithoutNarrowingGapsOrOpeningEndPockets(){
            var old=new WhiteMeridianMap(new WhiteMeridianProfileData{revision=2,wreckSpread=5,middleWreckExtension=0});
            var map=new WhiteMeridianMap();
            Assert.Greater(map.Wreck[1].MaxX-map.Wreck[1].MinX,old.Wreck[1].MaxX-old.Wreck[1].MinX);
            Assert.AreEqual(old.Wreck[1].MinX-old.Wreck[0].MaxX,map.Wreck[1].MinX-map.Wreck[0].MaxX,1e-9);
            Assert.AreEqual(old.Wreck[2].MinX-old.Wreck[1].MaxX,map.Wreck[2].MinX-map.Wreck[1].MaxX,1e-9);
            foreach(int side in new[]{-1,1}){
                Assert.True(map.SupportsSweep(map.Point(side*15,-7),map.Point(side*15,7),1.5));
                for(int x=23;x<=69;x++)Assert.False(map.SupportsSweep(map.Point(side*x,-22),map.Point(side*x,22),.25),"end pocket must not become a bypass");
            }
        }
        [Test] public void LobbyPairsAlliesAndStartsOnlyHeadquartersWithCurrentSaveBinding(){
            var p=Profile();var map=(WhiteMeridianMap)p.AuthoredMap;var lobby=new NativeLobbyConfiguration{WhiteMeridian=true};lobby.InitializeParticipants();Assert.IsNull(lobby.Validate(p,true,true));Assert.AreEqual(4,lobby.Capacity(p));
            foreach(int seed in new[]{7,7108,19092026}){
                var roster=Enumerable.Range(0,4).Select(i=>new OfflineParticipant("seat-"+i,i+1,i%2+1,OfflineControl.Human)).ToArray();var config=map.Configuration(p,seed,roster);
                foreach(var group in roster.GroupBy(r=>r.Team)){var starts=group.Select(r=>config.Starts[config.Assignments[Array.IndexOf(roster,r)]]).ToArray();Assert.AreEqual(Math.Sign(starts[0].Position.X),Math.Sign(starts[1].Position.X));Assert.AreNotEqual(starts[0].Position.Z,starts[1].Position.Z);}
                var authority=new OfflineParticipantAuthority(config,1);var bytes=authority.CaptureBytes();CollectionAssert.AreEqual(bytes,OfflineParticipantAuthority.Restore(bytes,config).CaptureBytes());
                foreach(var seat in roster){var v=authority.View(seat.Id);Assert.AreEqual(1,v.Buildings.Count(b=>b.Owner==v.Owner));Assert.AreEqual(PlayableBuildingKind.Headquarters,v.Buildings.Single(b=>b.Owner==v.Owner).Kind);}
            }
            lobby.Participants[3].Team=1;Assert.IsNotNull(lobby.Validate(p,true,true));
        }
    }
}
