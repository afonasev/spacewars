using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NereidaMapTests
    {
        private static readonly NavClearanceProfile Tank = new NavClearanceProfile("nereida-tank",1,"ground",1,PlayableProfile.Default.TankCollisionRadius);
        [TestCase(1)] [TestCase(1.5)]
        public void FullBuildingEnvelopesAndFactoryExitsStayOnFlatTerrain(double scale)
        {
            var map=new NereidaMap(new NereidaProfileData{scale=scale});var p=PlayableProfile.Default;var sites=map.Sites(p);
            Assert.AreEqual(4,sites.Count(s=>s.Kind==PlayableBuildingKind.Headquarters));
            Assert.AreEqual(4,sites.Count(s=>s.Kind==PlayableBuildingKind.Outpost));
            Assert.AreEqual(12,sites.Count(s=>s.Kind==PlayableBuildingKind.Mine));
            var all=new List<Tuple<NavPoint,double>>();
            foreach(var site in sites) {
                Check(site.Position,TerritoryRules.Radius(p,site.Kind));
                foreach(var slot in site.Slots) {
                    Check(slot.Position,Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius)));
                    var exit=new NavPoint(slot.Position.X+Math.Cos(slot.Heading)*p.FactoryExitDistance,slot.Position.Z+Math.Sin(slot.Heading)*p.FactoryExitDistance);
                    Assert.True(map.TryLocate(exit,p.TankCollisionRadius,out _),"Factory exit "+site.Id+"/"+slot.Id);
                }
            }
            for(int i=0;i<all.Count;i++)for(int j=0;j<i;j++) {
                double gap=all[i].Item2+all[j].Item2+2*p.TankCollisionRadius;
                Assert.False(Math.Abs(all[i].Item1.X-all[j].Item1.X)<gap&&Math.Abs(all[i].Item1.Z-all[j].Item1.Z)<gap,"Overlapping site envelopes");
            }
            void Check(NavPoint point,double radius)
            {
                Assert.True(map.TryLocate(point,radius+p.TankCollisionRadius,out var at),"Unsupported building center");
                double height=map.SurfaceHeight(point);
                foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1}) {
                    var corner=new NavPoint(point.X+x*radius,point.Z+z*radius);
                    Assert.True(map.IsValidLocation(new NavLocation(corner,at.SurfaceId),0));
                    Assert.AreEqual(height,map.SurfaceHeight(corner),1e-9);
                }
                all.Add(Tuple.Create(point,radius));
            }
        }
        [Test] public void AllFourBridgesHaveIndependentTraversableLowerFloors()
        {
            var map=new NereidaMap(new NereidaProfileData());
            for(int q=0;q<4;q++) {
                var crossing=map.Point(q,0,42);
                var upper=new NavLocation(crossing,"bridge/"+q);var lower=new NavLocation(crossing,"ice");
                Assert.True(map.Provider.IsValid(upper,Tank));Assert.True(map.Provider.IsValid(lower,Tank));
                Assert.False(map.TryLocate(crossing,Tank.Radius,out _));Assert.IsNull(map.SupportAt(crossing));
                Assert.Throws<ArgumentException>(()=>map.SurfaceHeight(crossing));
                Assert.True(map.Provider.CanSweep(new NavLocation(map.Point(q,-24,42),upper.SurfaceId),new NavLocation(map.Point(q,24,42),upper.SurfaceId),Tank));
                Assert.True(map.Provider.CanSweep(new NavLocation(map.Point(q,0,20),"ice"),new NavLocation(map.Point(q,0,64),"ice"),Tank));
                Assert.False(map.Provider.CanSweep(lower,upper,Tank));
            }
            Assert.GreaterOrEqual(map.MiddleHeight-map.DeckThickness,4);
        }
        [Test] public void AuthoredConnectorsAreWideAndAllSectorsRemainConnectedWithoutBridges()
        {
            var map=new NereidaMap(new NereidaProfileData());
            foreach(var link in map.Provider.Portals)Assert.AreEqual(NavConnectorStatus.Ready,map.Provider.QualifyPortal(link.Id,Tank).Status,link.Id);
            var reached=new HashSet<string>{"ice"};bool changed=true;
            while(changed){changed=false;foreach(var link in map.Provider.Portals.Where(p=>!p.Id.StartsWith("bridge"))) {
                if(reached.Contains(link.Entry.SurfaceId))changed|=reached.Add(link.Exit.SurfaceId);
                if(reached.Contains(link.Exit.SurfaceId))changed|=reached.Add(link.Entry.SurfaceId);
            }}
            for(int q=0;q<4;q++) {
                Assert.True(reached.Contains("home/"+q));
                // Actual surface sweeps through the widened L corner join both home exits and the bridge routes.
                var corner=new NavLocation(map.Point(q,42,42),"terrace/"+q);
                foreach(var point in new[]{map.Point(q,42,98),map.Point(q,98,42),map.Point(q,36,42)})
                    Assert.True(map.Provider.CanSweep(corner,new NavLocation(point,corner.SurfaceId),Tank));
            }
        }
        [Test] public void WaterAndCliffShortcutsAreRejectedAndRampsMeetTheirLandings()
        {
            var map=new NereidaMap(new NereidaProfileData());
            Assert.False(map.TryLocate(new NavPoint(0,0),0,out _));Assert.False(map.TryLocate(new NavPoint(68,68),0,out _));
            Assert.False(map.Provider.CanSweep(new NavLocation(new NavPoint(0,-25),"ice"),new NavLocation(new NavPoint(0,25),"ice"),Tank));
            foreach(var ramp in map.Ramps) {
                var heights=ramp.Bounds.Footprint.Vertices.Select(ramp.HeightAt).ToArray();
                Assert.AreEqual(ramp.Id.StartsWith("ice")?0:map.MiddleHeight,heights.Min(),1e-8);
                Assert.AreEqual(ramp.Id.StartsWith("ice")?map.MiddleHeight:map.HomeHeight,heights.Max(),1e-8);
            }
        }
        [TestCase(1)] [TestCase(1.5)]
        public void LowerShoreMeshesAndNavigationAgreeAndDoNotRepeatByRotation(double scale)
        {
            var map=new NereidaMap(new NereidaProfileData{scale=scale});
            var lower=map.Supports.Where(s=>s.SurfaceId=="ice").ToArray();
            var contours=new List<string>();
            for(int q=0;q<4;q++) {
                var arm=lower.Single(s=>s.Id=="ice-arm/"+q);
                Assert.Greater(arm.Bounds.Footprint.Vertices.Count,12);
                // Compare in each sector's local frame, not the inherently distinct world positions.
                contours.Add(string.Join(";",arm.Bounds.Footprint.Vertices.Select(v=>{
                    var local=map.Point((4-q)%4,v.X/scale,v.Z/scale);
                    return local.X.ToString("R")+","+local.Z.ToString("R");})));
                Assert.True(map.Provider.CanSweep(new NavLocation(map.Point(q,0,20),"ice"),new NavLocation(map.Point(q,0,120),"ice"),Tank));
            }
            Assert.AreEqual(4,contours.Distinct().Count());
            // Sampling catches both invisible walkable water and visible but unsupported shore.
            for(double x=-142.75;x<143;x+=1.5)for(double z=-142.25;z<143;z+=1.5) {
                var point=new NavPoint(x*scale,z*scale);
                bool rendered=lower.Any(s=>s.Contains(point));
                Assert.AreEqual(rendered,map.IsValidLocation(new NavLocation(point,"ice"),0),"Lower shore mismatch at "+point);
            }
        }
        [Test] public void SeededPlacementsPreserveAllModesAndUseEveryStart()
        {
            var seen=new HashSet<int>();
            for(uint seed=0;seed<128;seed++)foreach(NereidaMode mode in Enum.GetValues(typeof(NereidaMode))) {
                var starts=NereidaMap.AssignStarts(mode,seed);CollectionAssert.AreEqual(starts,NereidaMap.AssignStarts(mode,seed));
                Assert.AreEqual(starts.Length,starts.Distinct().Count());foreach(int start in starts){Assert.That(start,Is.InRange(0,3));seen.Add(start);}
                if(mode==NereidaMode.Duel){Assert.AreEqual(2,starts.Length);Assert.AreEqual(2,(starts[1]-starts[0]+4)%4);}
                else {Assert.AreEqual(4,starts.Length);if(mode==NereidaMode.Teams){Assert.AreEqual(1,Math.Abs(starts[1]-starts[0])%2);Assert.AreEqual(1,Math.Abs(starts[3]-starts[2])%2);}}
            }
            Assert.AreEqual(4,seen.Count);
        }
        [Test] public void MetadataAndProfileRejectInvalidAndAmbiguousGeometry()
        {
            Assert.Throws<ArgumentException>(()=>new NereidaMap(new NereidaProfileData{id="wrong-map"}));
            foreach(var field in NereidaMap.Fields) {
                Assert.IsNotEmpty(field.Description);Assert.IsNotEmpty(field.Unit);Assert.Greater(field.Step,0);
                Assert.Throws<ArgumentOutOfRangeException>(()=>field.Write(new NereidaProfileData(),double.NaN));
            }
            var a=new NereidaMap(new NereidaProfileData());var b=new NereidaMap(new NereidaProfileData{middleHeight=7});
            Assert.AreNotEqual(a.Provider.Binding,b.Provider.Binding);
            Assert.Throws<InvalidOperationException>(()=>a.Headquarters(PlayableOwner.Player));
        }
        [Test] public void FourStationPrecinctsHaveDistinctIrregularBoundariesAndEquivalentProtectedCores()
        {
            var map=new NereidaMap(new NereidaProfileData());var p=PlayableProfile.Default;
            var homes=new List<string>();var terraces=new List<string>();
            for(int q=0;q<4;q++) {
                var home=map.Supports.Single(s=>s.Id=="home/"+q).Bounds.Footprint;
                var terrace=map.Supports.Single(s=>s.Id=="terrace/"+q).Bounds.Footprint;
                Assert.Greater(home.Vertices.Count,4,"Home cliff should not be rectangular: "+q);
                Assert.Greater(terrace.Vertices.Count,8,"Terrace edge should not be the repeated L-shaped rectangle: "+q);
                homes.Add(string.Join(";",home.Vertices.Select(v=>v.X.ToString("R")+","+v.Z.ToString("R"))));
                terraces.Add(string.Join(";",terrace.Vertices.Select(v=>v.X.ToString("R")+","+v.Z.ToString("R"))));
                var homeSite=map.Sites(p).Single(s=>s.Kind==PlayableBuildingKind.Headquarters&&s.Id==q+1);
                var fort=map.Sites(p).Single(s=>s.Kind==PlayableBuildingKind.Outpost&&s.Id==q+5);
                Assert.True(map.TryLocate(homeSite.Position,TerritoryRules.Radius(p,homeSite.Kind)+p.TankCollisionRadius,out _),"Home reserve "+q);
                Assert.True(map.TryLocate(fort.Position,TerritoryRules.Radius(p,fort.Kind)+p.TankCollisionRadius,out _),"Fort reserve "+q);
            }
            Assert.AreEqual(4,homes.Distinct().Count(),"Each station shoreline has its own authored contour.");
            Assert.AreEqual(4,terraces.Distinct().Count(),"Each terrace has its own authored contour.");
            Assert.AreEqual(4,map.Sites(p).Count(s=>s.Kind==PlayableBuildingKind.Headquarters));
            Assert.AreEqual(4,map.Sites(p).Count(s=>s.Kind==PlayableBuildingKind.Outpost));
            Assert.AreEqual(12,map.Sites(p).Count(s=>s.Kind==PlayableBuildingKind.Mine));
        }
    }
}
