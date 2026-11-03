using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class RelayMapLayoutTests
    {
        [TestCase("Crossings",2,4)][TestCase("Dock",2,4)][TestCase("Basalt",1,4)]
        [TestCase("Moss",4,4)][TestCase("Meridian",2,8)][TestCase("Foundry",2,12)]
        [TestCase("City",3,12)][TestCase("Grove",4,8)][TestCase("Polygon",4,8)]
        public void SelectedLobbyLayoutsPreserveEnvelopesAndPublicRelayTargets(string name,int relays,int mines)
        {
            IPlayableTerrain map;
            switch(name){case "Crossings":map=ThreeCrossingsMap.Default;break;case "Dock":map=new NullDockMap();break;
                case "Basalt":map=new BasaltCrownMap();break;case "Moss":map=new MossHighlandMap();break;
                case "Meridian":map=new WhiteMeridianMap();break;case "Foundry":map=new FoundryMap(new FoundryProfileData());break;
                case "City":map=new AntennaCityMap();break;case "Grove":map=new CrimsonGroveMap();break;default:map=new AiTestMap();break;}
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var sites=map.Sites(p);
            foreach(var blocker in map.MovementBlockers)Assert.DoesNotThrow(()=>{var polygon=blocker.Footprint;},name+" valid obstacle polygon");
            Assert.AreEqual(relays,sites.Count(s=>s.Kind==PlayableBuildingKind.RelayTower));Assert.AreEqual(mines,sites.Count(s=>s.Kind==PlayableBuildingKind.Mine));
            Assert.AreEqual(sites.Length,sites.Select(s=>s.Id).Distinct().Count());Assert.DoesNotThrow(()=>TerritoryRules.ValidateArena(p));
            foreach(var relay in sites.Where(s=>s.Kind==PlayableBuildingKind.RelayTower)){
                Assert.IsEmpty(relay.Slots);Assert.True(map.SupportsFootprint(relay.Position,p.RelayFootprintRadius*Math.Sqrt(2)+p.TankCollisionRadius));
                Assert.True(Enumerable.Range(0,16).Any(i=>map.SupportsFootprint(new NavPoint(relay.Position.X+Math.Cos(i*Math.PI/8)*3,relay.Position.Z+Math.Sin(i*Math.PI/8)*3),p.TankCollisionRadius)),"capture approach "+relay.Id);
            }
            if(map is NullDockMap dock){Assert.False(sites.Any(s=>s.Kind==PlayableBuildingKind.Mine&&(s.Position.Equals(dock.Point(82,72))||s.Position.Equals(dock.Point(110,88)))));}
        }
        [Test] public void BasaltEdgeMinesKeepFullEnvelopesAndOpenApproaches()
        {
            var map=new BasaltCrownMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            foreach(int sign in new[]{-1,1}){
                var mine=map.Sites(p).Single(s=>s.Id==(sign>0?9:10));Assert.AreEqual(PlayableBuildingKind.Mine,mine.Kind);
                var lane=new NavPoint(sign*16,sign*32);Assert.Greater(Math.Abs(mine.Position.Z-lane.Z),p.MineCaptureRadius+p.TankCollisionRadius);
                Assert.AreEqual(new NavPoint(sign*16,sign*42.375),mine.Position);
                var clearance=Math.Max(p.MineCaptureRadius+p.TankCollisionRadius,p.MineFootprintRadius*Math.Sqrt(2)+p.TankCollisionRadius);
                Assert.True(map.SupportsFootprint(mine.Position,clearance),"complete capture and build envelope");
                Assert.True(map.SupportsSweep(lane,mine.Position,p.TankCollisionRadius),"open approach without an artificial screen");
                Assert.True(map.SupportsSweep(new NavPoint(-32,sign*32),new NavPoint(32,sign*32),p.TankCollisionRadius),"through lane");
            }
        }
    }
}
