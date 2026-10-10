using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;

namespace Spacewars.Tests.EditMode
{
    public sealed class ThreeCrossingsTests
    {
        private static PlayableProfile Profile=>PlayableProfile.ThreeCrossingsDefault;
        [Test] public void SitesAndOddSlotRingsAreRotationallyEquivalent()
        {
            var p=Profile;var sites=TerritoryRules.Sites(p);Assert.AreEqual(10,sites.Length);Assert.AreEqual(6,sites.Count(s=>s.Kind==PlayableBuildingKind.Mine));
            for(int i=0;i<sites.Length;i+=2){var a=sites[i];var b=sites[i+1];Assert.AreEqual(a.Kind,b.Kind);Assert.AreEqual(-a.Position.X,b.Position.X);Assert.AreEqual(-a.Position.Z,b.Position.Z);for(int j=0;j<a.Slots.Count;j++){Assert.AreEqual(-a.Slots[j].Position.X,b.Slots[j].Position.X,1e-9);Assert.AreEqual(-a.Slots[j].Position.Z,b.Slots[j].Position.Z,1e-9);}}
            TerritoryRules.ValidateArena(p);
        }
        [Test] public void WaterHasNoSupportOrBallisticWallAndRocksBlockBoth()
        {
            var p=Profile;var m=(ThreeCrossingsMap)p.AuthoredMap;var movement=new NavGeometry(m.HalfExtent,PlayableMap.StaticObstacles(p),1);var solid=new NavGeometry(m.HalfExtent,PlayableMap.SolidObstacles(p),1);
            Assert.IsNull(m.SupportAt(new NavPoint(0,20)));Assert.IsFalse(movement.SegmentFree(new NavPoint(-15,20),new NavPoint(15,20),.72));Assert.IsTrue(solid.SegmentFree(new NavPoint(-8,20),new NavPoint(8,20),.06),"Water itself is not ballistic cover; authored bank rocks are cover.");
            Assert.IsFalse(movement.SegmentFree(new NavPoint(-40,0),new NavPoint(-20,0),.72));Assert.IsFalse(solid.SegmentFree(new NavPoint(-40,0),new NavPoint(-20,0),.06));
            foreach(var z in new[]{-m.CrossingZ,0,m.CrossingZ})Assert.IsTrue(m.SupportAt(new NavPoint(0,z)).IsBridge);
        }
        [Test] public void EveryClassCrossesThreeBridgesAndOnlyTwoPocketEntrances()
        {
            var p=Profile;var m=(ThreeCrossingsMap)p.AuthoredMap;
            foreach(double radius in new[]{p.TankCollisionRadius,p.ExplorerCollisionRadius,p.ShkvalCollisionRadius})
            {
                foreach(double z in new[]{-m.CrossingZ,0,m.CrossingZ}){Assert.IsTrue(m.SupportsSweep(new NavPoint(-20,z),new NavPoint(20,z),radius));Assert.IsTrue(m.SupportsSweep(new NavPoint(20,z),new NavPoint(-20,z),radius));}
                foreach(int side in new[]{-1,1}){double x=side*m.PocketX;foreach(int end in new[]{-1,1})Assert.IsTrue(m.SupportsSweep(new NavPoint(x,end*24),new NavPoint(x,end*6),radius));foreach(int wall in new[]{-1,1})Assert.IsFalse(m.SupportsSweep(new NavPoint(x,0),new NavPoint(x+wall*18,0),radius));}
            }
        }
        public static NavGeometry FullyBuilt(PlayableProfile p)
        {
            var blockers=PlayableMap.StaticObstacles(p).ToList();
            foreach(var site in TerritoryRules.Sites(p)){Add(site.Position,TerritoryRules.Radius(p,site.Kind));foreach(var slot in site.Slots)Add(slot.Position,Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius)));}
            return new NavGeometry(p.ArenaHalfExtent,blockers.ToArray(),2);
            void Add(NavPoint center,double radius)=>blockers.Add(new NavObstacle(center.X-radius,center.Z-radius,center.X+radius,center.Z+radius));
        }
        [Test] public void FullBuildingReservesHaveConnectedFactoryExitsAndMineApproaches()
        {
            var p=Profile;var geometry=FullyBuilt(p);var router=new SharedFlowRouter(geometry,p.Navigation);var target=new NavPoint(-16,0);
            foreach(var site in TerritoryRules.Sites(p))
            {
                foreach(var slot in site.Slots){var exit=new NavPoint(slot.Position.X+Math.Cos(slot.Heading)*p.FactoryExitDistance,slot.Position.Z+Math.Sin(slot.Heading)*p.FactoryExitDistance);Assert.IsTrue(geometry.IsFree(exit,p.TankCollisionRadius),"exit "+site.Id+":"+slot.Id);Assert.IsNotEmpty(router.FindPath(exit,target),"route "+site.Id+":"+slot.Id);}
                if(site.Kind==PlayableBuildingKind.Mine){var approach=new NavPoint(site.Position.X,site.Position.Z+p.MineFootprintRadius+p.Navigation.ArrivalSlotSpacing);Assert.IsNotEmpty(router.FindPath(approach,target),"mine "+site.Id);}
            }
        }
        [Test] public void MixedGroupsCrossReverseAndEnterBothPocketEndsWithSweptClearance()
        {
            var p=Profile;var m=(ThreeCrossingsMap)p.AuthoredMap;var geometry=FullyBuilt(p);
            foreach(double z in new[]{-m.CrossingZ,0,m.CrossingZ})RunGroup(p,geometry,new NavPoint(-18,z),new NavPoint(18,z),false);
            foreach(int side in new[]{-1,1})RunGroup(p,geometry,new NavPoint(side*m.PocketX,-24),new NavPoint(side*m.PocketX,24),true);
        }
        private static void RunGroup(PlayableProfile p,NavGeometry geometry,NavPoint from,NavPoint to,bool vertical)
        {
            var session=new NavigationSession(1,geometry,p.Navigation);var origins=new NavPoint[3];var goals=new NavPoint[3];
            for(int i=0;i<3;i++){// Offset the middle lane around the mine; existing arrival spacing is the group scale.
                double lane=(i-1)*p.Navigation.ArrivalSlotSpacing; if(vertical)lane=(i==0?-1.5:i==1?-.75:1.5)*p.Navigation.ArrivalSlotSpacing;
                origins[i]=new NavPoint(from.X+(vertical?lane:0),from.Z+(vertical?0:lane));goals[i]=new NavPoint(to.X+(vertical?lane:0),to.Z+(vertical?0:lane));
                var kind=i==0?PlayableEntityKind.Explorer:i==1?PlayableEntityKind.Tank:PlayableEntityKind.Shkval;
                session.Crowd.Add(i,origins[i],PlayableUnitRules.Radius(p,kind),PlayableUnitRules.Speed(p,kind),PlayableUnitRules.Turn(p,kind));}
            for(int leg=0;leg<2;leg++)
            {
                for(int i=0;i<3;i++)Assert.IsTrue(session.Move(i,leg==0?goals[i]:origins[i]));
                while(session.Requests.TryDequeue(out var r)){var route=new SharedFlowRouter(geometry,r.Profile).FindPath(r.Start,r.Goal);Assert.IsNotEmpty(route);session.Answers.TryEnqueue(new NavigationAnswer(r,route));}
                for(int tick=0;tick<2400;tick++){var before=session.Crowd.Units.Select(u=>u.Position).ToArray();session.Step(1d/30);for(int i=0;i<3;i++)Assert.IsTrue(geometry.SegmentFree(before[i],session.Crowd.Units[i].Position,session.Crowd.Units[i].Radius));if(session.Crowd.Units.All(u=>u.Outcome==NavigationOutcome.Arrived))break;}
                Assert.IsTrue(session.Crowd.Units.All(u=>u.Outcome==NavigationOutcome.Arrived),string.Join(",",session.Crowd.Units.Select(u=>u.Outcome.ToString())));
            }
        }
        [Test] public void CommonGroupOrderLeavesFullyBuiltHeadquartersAndTurnsAcrossBridge()
        {
            var p=Profile;var g=FullyBuilt(p);var session=new NavigationSession(1,g,p.Navigation);var site=TerritoryRules.Sites(p)[0];
            int id=0;foreach(var slot in site.Slots){var exit=new NavPoint(slot.Position.X+Math.Cos(slot.Heading)*p.FactoryExitDistance,slot.Position.Z+Math.Sin(slot.Heading)*p.FactoryExitDistance);var kind=id%3==0?PlayableEntityKind.Explorer:id%3==1?PlayableEntityKind.Tank:PlayableEntityKind.Shkval;session.Crowd.Add(id++,exit,PlayableUnitRules.Radius(p,kind),PlayableUnitRules.Speed(p,kind),PlayableUnitRules.Turn(p,kind));}
            var ids=session.Crowd.Units.Select(u=>u.Id).ToArray();var routers=new Dictionary<double,SharedFlowRouter>();
            foreach(var target in new[]{new NavPoint(-16,-38),new NavPoint(16,-38),new NavPoint(-16,-38)})
            {
                var goals=session.AllocateArrivalSlots(target,ids);Assert.AreEqual(ids.Length,goals.Length);Assert.IsTrue(session.MoveGroup(ids,goals));
                while(session.Requests.TryDequeue(out var request)){if(!routers.TryGetValue(request.Profile.Radius,out var router)){router=new SharedFlowRouter(g,request.Profile);routers.Add(request.Profile.Radius,router);}session.Answers.TryEnqueue(new NavigationAnswer(request,router.FindPath(request.Start,request.Goal)));}
                for(int tick=0;tick<3600;tick++){var before=session.Crowd.Units.Select(u=>u.Position).ToArray();session.Step(1d/30);foreach(var u in session.Crowd.Units){Assert.IsTrue(g.SegmentFree(before[u.Id],u.Position,u.Radius));foreach(var other in session.Crowd.Units)if(other.Id<u.Id)Assert.GreaterOrEqual(Math.Sqrt(Math.Pow(u.Position.X-other.Position.X,2)+Math.Pow(u.Position.Z-other.Position.Z,2)),u.Radius+other.Radius-1e-6);}if(session.Crowd.Units.All(u=>u.Outcome==NavigationOutcome.Arrived))break;}
                Assert.IsTrue(session.Crowd.Units.All(u=>u.Outcome==NavigationOutcome.Arrived),string.Join(",",session.Crowd.Units.Select(u=>u.Id+":"+u.Outcome)));
            }
        }
        [Test] public void AuthoredRuntimeUsesMappedStartsAndPreservesNativeRoster()
        {
            using(var runtime=new PlayableRuntime(Profile,1,19092026))
            {
                Assert.IsTrue(System.Threading.SpinWait.SpinUntil(()=>runtime.Latest.Tick>=3,2000));
                Assert.IsNull(runtime.Latest.Failure);Assert.AreEqual(-46,runtime.Latest.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Headquarters).Position.Z);
                var own=runtime.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player);Assert.AreEqual(PlayableEntityKind.Explorer,own.Kind);
                var objectives=TerritoryRules.PublicScoutObjectives(Profile,PlayableOwner.Player);Assert.AreEqual(1,objectives.Length);Assert.Greater(objectives[0].Approach.Z,0);
                Assert.IsTrue(runtime.NavigationGeometry.IsFree(own.Position,Profile.ExplorerCollisionRadius));
            }
        }
        [Test] public void MapMetadataCoversEveryTunableAndRejectsInvalidGeometry()
        {
            var fields=typeof(ThreeCrossingsProfileData).GetFields().Where(f=>f.FieldType==typeof(double)).ToArray();Assert.AreEqual(fields.Length,ThreeCrossingsMap.Fields.Count);
            foreach(var f in ThreeCrossingsMap.Fields){Assert.IsNotEmpty(f.Group);Assert.IsNotEmpty(f.Description);Assert.Greater(f.Step,0);var d=new ThreeCrossingsProfileData();f.Write(d,f.Maximum+f.Step);Assert.Throws<ArgumentException>(()=>new ThreeCrossingsMap(d));}
        }
    }
}
