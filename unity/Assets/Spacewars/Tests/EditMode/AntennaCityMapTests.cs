using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class AntennaCityMapTests
    {
        static readonly MethodInfo Create=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.NativeLobbyMatch").GetMethod("Create",BindingFlags.Static|BindingFlags.NonPublic);
        static OfflineMatchConfiguration Config(PlayableProfile p,NativeLobbyConfiguration setup)=>(OfflineMatchConfiguration)Create.Invoke(null,new object[]{p,setup});
        static NativeLobbyConfiguration Setup(int count,int teamSize,int seed){var setup=new NativeLobbyConfiguration{AntennaCity=true,HasExplicitSeed=true,ExplicitSeed=seed};setup.InitializeParticipants();setup.Participants.RemoveRange(count,6-count);for(int i=0;i<count;i++)setup.Participants[i].Team=i/teamSize+1;return setup;}
        [Test] public void StreetBasesSitesAndConstructionExitsMatchV02()
        {
            var map=new AntennaCityMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var sites=map.Sites(p);
            Assert.AreEqual(27,sites.Length);Assert.AreEqual(6,sites.Count(s=>s.Kind==PlayableBuildingKind.Headquarters));Assert.AreEqual(6,sites.Count(s=>s.Kind==PlayableBuildingKind.Outpost));Assert.AreEqual(15,sites.Count(s=>s.Kind==PlayableBuildingKind.Mine));
            Assert.AreEqual(48,sites.Sum(s=>s.Slots.Count));Assert.GreaterOrEqual(map.Blocks.Count,60,"Dense authored city blocks");Assert.AreEqual(6,map.Supports.Count(s=>s.Id.Contains("ramp")));Assert.True(PartitionNavigationProvider.SupportsTerrain(map));
            double ordinary=Math.Max(p.HangarFootprintRadius,Math.Max(p.FactoryFootprintRadius,Math.Max(p.ScienceFootprintRadius,p.RefineryFootprintRadius)));
            foreach(var site in sites){Assert.True(map.SupportsFootprint(site.Position,site.Kind==PlayableBuildingKind.Mine?p.MineFootprintRadius:p.HeadquartersFootprintRadius),"site "+site.Id);Assert.AreEqual(site.Id>=25?4:0,map.SurfaceHeight(site.Position),1e-8);
                foreach(var slot in site.Slots){Assert.True(map.SupportsFootprint(slot.Position,ordinary+p.TankCollisionRadius),"slot "+site.Id+"/"+slot.Id);Assert.True(map.SupportsFootprint(new NavPoint(slot.Position.X,slot.Position.Z-p.FactoryExitDistance),p.TankCollisionRadius),"exit "+site.Id+"/"+slot.Id);}
                foreach(var slot in site.DefenseSlots(p))Assert.True(map.SupportsFootprint(slot.Position,p.TurretFootprintRadius*Math.Sqrt(2)+p.TankCollisionRadius),"defense "+site.Id+"/"+slot.Id);
            }
        }
        [TestCase(1d,4d)][TestCase(1.25d,5d)]
        public void EveryRampConnectsBothWaysAndCliffsNeverShortcut(double scale,double height)
        {
            var map=new AntennaCityMap(new AntennaCityProfileData{scale=scale,plazaHeight=height});double r=PlayableProfile.Default.TankCollisionRadius;
            for(int i=0;i<3;i++){
                foreach(int side in new[]{-1,1}){var lo=map.RampFoot(i,side,3);var hi=map.RampTop(i,side,3);Assert.AreEqual(0,map.SurfaceHeight(lo),1e-8);Assert.AreEqual(height,map.SurfaceHeight(hi),1e-8);Assert.True(map.SupportsSweep(lo,hi,r),"up "+i+"/"+side);Assert.True(map.SupportsSweep(hi,lo,r),"down "+i+"/"+side);}
                Assert.False(map.SupportsSweep(map.Local(-90+120*i,52,0,18),map.Local(-90+120*i,52,0,10),r),"plaza side cliff "+i);
            }
            Assert.False(map.SupportsFootprint(map.PointAt(0,171),r));Assert.False(map.SupportsFootprint(new NavPoint(0,0),r));
        }
        [Test] public void InnerCrossPassagesRetainTheirProfileClearance()
        {
            var map=new AntennaCityMap();double radius=map.DistrictPassageWidth/2-1;
            for(int i=0;i<6;i++){
                double angle=-90+60*i;
                // Check the two inter-block openings, outside the unchanged elevated plaza footprint.
                foreach(int side in new[]{-1,1})Assert.True(map.SupportsSweep(map.Local(angle,71.5,side*25,0),map.Local(angle,71.5,side*34,0),radius),"inner cross passage "+i+"/"+side);
            }
        }
        [Test] public void AllSixGroundCenterApproachesAvoidPlazasAndRemainBroad()
        {
            var map=new AntennaCityMap();double radius=map.SideStreetWidth/2-1;
            for(int start=0;start<6;start++){
                var path=map.CenterApproach(start);
                for(int i=1;i<path.Length;i++){
                    Assert.True(map.SupportsSweep(path[i-1],path[i],radius),"broad approach "+start+"/"+i);
                    for(int step=0;step<=20;step++){double t=step/20d;var p=new NavPoint(path[i-1].X+(path[i].X-path[i-1].X)*t,path[i-1].Z+(path[i].Z-path[i-1].Z)*t);Assert.AreEqual("city/street",map.SupportAt(p)?.Id,"independent ground approach "+start+"/"+i);Assert.Zero(map.SurfaceHeight(p));}
                }
                Assert.True(map.SupportsSweep(path.Last(),map.CenterApproach((start+1)%6).Last(),radius),"central ground loop");
            }
            double Area(NavObstacle b){var v=b.Footprint.Vertices;double area=0;for(int i=0;i<v.Count;i++)area+=v[i].X*v[(i+1)%v.Count].Z-v[(i+1)%v.Count].X*v[i].Z;return Math.Abs(area)/2;}
            Assert.Greater(map.Blocks.Sum(Area)/Area(map.Supports.Last().Bounds),.30,"Urban mass must replace the sparse open field");
        }
        [Test] public void TwoVersusTwoUsesAllThreeOppositeAdjacentPairRotations()
        {
            var map=new AntennaCityMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var seen=new HashSet<string>();
            for(int seed=0;seed<64;seed++){
                var setup=Setup(4,2,seed);Assert.IsNull(setup.Validate(p,true,true));var c=Config(p,setup);var ids=c.Assignments.Select(i=>c.Starts[i].SiteId-1).ToArray();
                int[] a=ids.Take(2).OrderBy(x=>x).ToArray(),b=ids.Skip(2).OrderBy(x=>x).ToArray();int delta=(a[1]-a[0]+6)%6;Assert.True(delta==1||delta==5,"Adjacent allies");CollectionAssert.AreEquivalent(a.Select(x=>(x+3)%6),b,"Opposite enemy pair");
                seen.Add(string.Join(",",ids.OrderBy(x=>x)));CollectionAssert.AreEqual(c.Assignments,Config(p,setup).Assignments,"seed repeat");
            }
            Assert.AreEqual(3,seen.Count,"All three rotations, not one fixed side");
        }
        [TestCase(6,1)][TestCase(6,2)][TestCase(6,3)][TestCase(4,2)]
        public void OrdinaryLobbyRosterAndCurrentSaveRetainMapAndStarts(int count,int teamSize)
        {
            var map=new AntennaCityMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var setup=Setup(count,teamSize,19);Assert.IsNull(setup.Validate(p,true,true));Assert.True(setup.Copy().AntennaCity);
            var c=Config(p,setup);var original=new OfflineParticipantAuthority(c,1);OfflineParticipantAuthority restored=null;
            try{var bytes=original.CaptureBytes();restored=OfflineParticipantAuthority.Restore(bytes,c);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());
                for(int i=0;i<count;i++){var v=original.View(c.Roster[i].Id);var home=v.Buildings.Single(b=>b.Owner==v.Owner&&b.Kind==PlayableBuildingKind.Headquarters);Assert.AreEqual(c.Starts[c.Assignments[i]].SiteId,home.SiteId);Assert.True(map.SupportsFootprint(c.Starts[c.Assignments[i]].ExplorerAnchor,p.ExplorerCollisionRadius));}
                var wrong=new AntennaCityMap(new AntennaCityProfileData{plazaHeight=3});var wrongConfig=Config(PlayableProfile.Create(p.CopyData(),wrong),setup);Assert.Throws<ArgumentException>(()=>OfflineParticipantAuthority.Restore(bytes,wrongConfig));
            }finally{original.Domain.DestroyOwnedNavigation();restored?.Domain.DestroyOwnedNavigation();}
        }
    }
}
