using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode
{
    public sealed class CrimsonGroveMapTests
    {
        [TestCase(1.0)] [TestCase(1.25)]
        public void FullSiteEnvelopesAndBothRampFamiliesRemainTraversable(double scale){
            var map=new CrimsonGroveMap(new CrimsonGroveProfileData{scale=scale});
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("seat-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var c=map.Configuration(p,23102026,roster);
            Assert.AreEqual(8,c.Starts.Count);Assert.AreEqual(32,c.Sites.Count);
            Assert.AreEqual(12,c.Sites.Count(s=>s.Kind==PlayableBuildingKind.Mine));Assert.AreEqual(12,c.Sites.Count(s=>s.Kind==PlayableBuildingKind.Outpost));
            for(int i=0;i<8;i++){
                foreach(double angle in new[]{22.5+i*45,42.5+i*45}){
                    Assert.AreEqual(map.TerraceHeight/2,map.SurfaceHeight(map.Polar(angle,88)),1e-6,"ramp midpoint");
                    Assert.True(map.TryLocate(map.Polar(angle,78),p.TankCollisionRadius,out var at),"lower approach");
                    Assert.True(map.TryTraverse(at,map.Polar(angle,100),p.TankCollisionRadius,out var end),"up ramp "+angle);
                    Assert.True(map.TryTraverse(end,map.Polar(angle,78),p.TankCollisionRadius,out _),"down ramp "+angle);
                }
                Assert.False(map.SupportsSweep(map.Polar(12.5+i*45,88),map.Polar(12.5+i*45,100),p.ExplorerCollisionRadius),"no cliff shortcut");
                Assert.True(map.SupportsSweep(c.Starts[i].ExplorerAnchor,c.Sites.Single(s=>s.Id==17+i).Position,p.ExplorerCollisionRadius),"favored pocket approach");
            }
            foreach(var site in c.Sites)foreach(var slot in site.Slots)Assert.True(map.SupportsFootprint(slot.Position,p.FactoryFootprintRadius));
            Assert.True(PartitionNavigationProvider.SupportsTerrain(map));
        }
        [Test] public void OuterTongueBlocksFarNeighborButKeepsTheAuthoredBypass(){
            var map=new CrimsonGroveMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var route=new[]{new NavPoint(118.2565801614447,48.9834793427315),new NavPoint(96.97493321292154,45.22015400625484),new NavPoint(89.21121012821837,49.45058126512639),new NavPoint(81.46082202482387,61.38513236150892),new NavPoint(75.66610325295395,82.57506172273389)};
            for(int i=0;i<8;i++){
                double a=i*Math.PI/4;Func<NavPoint,NavPoint> rotate=q=>new NavPoint(q.X*Math.Cos(a)+q.Z*Math.Sin(a),q.Z*Math.Cos(a)-q.X*Math.Sin(a));
                Assert.False(map.SupportsSweep(rotate(route[0]),rotate(route[4]),p.TankCollisionRadius),"rock tongue must stop a direct far-neighbor attack");
                for(int j=1;j<route.Length;j++)Assert.True(map.SupportsSweep(rotate(route[j-1]),rotate(route[j]),p.TankCollisionRadius),"far-neighbor bypass segment "+i+"/"+j);
            }
        }
        [TestCase(false)] [TestCase(true)]
        public void LobbySupportsFfaAndContiguousFourPlayerTeams(bool teams){
            var map=new CrimsonGroveMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var setup=new NativeLobbyConfiguration{CrimsonGrove=true,HasExplicitSeed=true,ExplicitSeed=23102026};setup.InitializeParticipants();
            if(teams)for(int i=0;i<8;i++)setup.Participants[i].Team=i/4+1;
            Assert.AreEqual(8,setup.Capacity(p));Assert.IsNull(setup.Validate(p,true,true));
            using(var runtime=PlayableRuntime.CreateLobbyMatch(p,4,setup,true)){
                Assert.AreEqual(8,runtime.Latest.Participants.Count);Assert.IsNull(runtime.Latest.Failure);
                var views=runtime.Latest.Participants.Select(o=>runtime.ParticipantView(o.Id)).ToArray();
                var buildings=views.SelectMany(v=>v.Buildings.Where(b=>b.Owner==v.Owner));
                var entities=views.SelectMany(v=>v.Entities.Where(e=>e.Owner==v.Owner));
                var bases=buildings.Where(b=>b.Kind==PlayableBuildingKind.Headquarters).OrderBy(b=>b.SiteId).ToArray();Assert.AreEqual(8,bases.Length);
                if(teams){var ringTeams=bases.Select(b=>runtime.Latest.Participants[(int)b.Owner].Team).ToArray();Assert.AreEqual(2,Enumerable.Range(0,8).Count(i=>ringTeams[i]!=ringTeams[(i+1)%8]),"each team occupies one contiguous half-ring");}
                else Assert.AreEqual(8,runtime.Latest.Participants.Select(o=>o.Team).Distinct().Count());
                Assert.AreEqual(8,entities.Where(e=>e.Kind==PlayableEntityKind.Explorer).Select(e=>e.Position).Distinct().Count());
                Assert.IsInstanceOf<CrimsonGroveMap>(runtime.NavigationBinding.Admission.AuthoredTerrain);
                runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,3000));
            }
            var art=CrimsonGroveArtProfile.Load();art.Validate();Assert.IsNotEmpty(art.Parameters());
        }
    }
}
