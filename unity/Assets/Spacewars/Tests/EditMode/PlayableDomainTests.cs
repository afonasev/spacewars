using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableDomainTests
    {
        private static readonly Type DomainType=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private static readonly MethodInfo ApplyMethod=DomainType.GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly MethodInfo StepMethod=DomainType.GetMethod("Step",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly MethodInfo SnapshotMethod=DomainType.GetMethod("Snapshot",BindingFlags.Instance|BindingFlags.NonPublic);
        private static object NewDomain(){return Activator.CreateInstance(DomainType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{PlayableProfile.Default,1L},null);}
        private static PlayableSnapshot Snapshot(object domain){return (PlayableSnapshot)SnapshotMethod.Invoke(domain,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7});}
        private static PlayableCommandStatus Apply(object domain,PlayableCommand command){object[] args={command,null};return (PlayableCommandStatus)ApplyMethod.Invoke(domain,args);}
        private static void Seconds(object domain,int seconds){for(int i=0;i<seconds*30;i++)StepMethod.Invoke(domain,new object[]{1d/30d});}

        [Test] public void AttackApproachBehindWallHasLineOfFireToTarget()
        {
            var domain=NewDomain();var profile=PlayableProfile.Default;
            var method=DomainType.GetMethod("TryAttackApproach",BindingFlags.Instance|BindingFlags.NonPublic);
            var target=new NavPoint(-8,-9);object[] args={3,new NavPoint(6,-6),target,default(NavPoint)};
            Assert.IsTrue((bool)method.Invoke(domain,args));var approach=(NavPoint)args[3];
            var walls=new NavGeometry(profile.ArenaHalfExtent,PlayableMap.StaticObstacles(profile),1);
            Assert.IsTrue(walls.IsFree(approach,profile.TankCollisionRadius));
            Assert.IsTrue(walls.SegmentFree(approach,target,profile.TankProjectileCollisionRadius));
            Assert.LessOrEqual(Math.Sqrt(Math.Pow(approach.X-target.X,2)+Math.Pow(approach.Z-target.Z,2)),profile.TankRange);
        }

        [Test] public void TankRepositionsWhenItsShellClipsTheAuthoredRamp()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;
            var from=new NavPoint(44.253232951883739,29.829520707640366);
            var target=new NavPoint(43.991389536562842,39.8181326440815);
            var map=profile.AuthoredMap;
            var start=new BallisticPoint(from.X,map.SurfaceHeight(from)+map.DirectFireHeight,from.Z);
            var end=new BallisticPoint(target.X,map.SurfaceHeight(target)+map.DirectFireHeight,target.Z);
            Assert.True(PlayableBallistics.TerrainLineClear(start,end,0,map.Solids,profile.BallisticWallHeight,map.SurfaceHeight),"The center ray fits at the observed stuck firing position.");
            Assert.False(PlayableBallistics.TerrainLineClear(start,end,profile.TankProjectileCollisionRadius,map.Solids,profile.BallisticWallHeight,map.SurfaceHeight),"The actual shell clips the ramp.");
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var domain=Activator.CreateInstance(DomainType,flags,null,new object[]{profile,1L,false},null);
            DomainType.GetMethod("AddBuilding",flags).Invoke(domain,new object[]{PlayableOwner.Enemy,PlayableBuildingKind.Refinery,target,true});
            DomainType.GetMethod("RebuildGeometry",flags).Invoke(domain,null);
            int tank=(int)DomainType.GetMethod("SpawnUnit",flags).Invoke(domain,new object[]{from,PlayableOwner.Player,PlayableEntityKind.Tank});
            StepMethod.Invoke(domain,new object[]{1d/30d});
            int refinery=Snapshot(domain).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Refinery).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,Apply(domain,new PlayableCommand(1,1,"player-1",PlayableCommandKind.Attack,new[]{tank},targetId:refinery)));
            var nav=(NavigationSession)DomainType.GetProperty("Navigation",flags).GetValue(domain);
            bool routed=false;
            for(int tick=0;tick<300;tick++)
            {
                StepMethod.Invoke(domain,new object[]{1d/30d});
                while(nav.Requests.TryDequeue(out var request))
                {
                    routed=true;
                    var route=new SharedFlowRouter(request.Geometry,request.Profile).FindPath(request.Start,request.Goal);
                    nav.Answers.TryEnqueue(new NavigationAnswer(request,route));
                }
                var building=Snapshot(domain).Buildings.FirstOrDefault(b=>b.Id==refinery);
                if(building==null||building.Health<profile.RefineryHealth){Assert.True(routed,"Blocked fire must trigger a usable approach.");return;}
            }
            Assert.Fail("The tank must move to a clear firing position and damage the refinery.");
        }

        [Test] public void ReadyHeadquartersCreditsNormalizedIncomeEachSecond()
        {
            var domain=NewDomain(); Seconds(domain,1);
            Assert.AreEqual(PlayableProfile.Default.StartingCredits+PlayableProfile.Default.HeadquartersIncomePerPeriod/PlayableProfile.Default.IncomePeriodSeconds,Snapshot(domain).Credits);
        }

        [Test] public void FactoryKeepsPaidReadyTankWhenSpawnFootprintIsBlocked()
        {
            var domain=NewDomain(); long sequence=1;
            Assert.AreEqual(PlayableCommandStatus.Applied,Apply(domain,new PlayableCommand(1,sequence++,"player-1",PlayableCommandKind.BuildFactory,new int[0],default(NavPoint),0)));
            Seconds(domain,PlayableProfile.Default.FactoryBuildSeconds+10);
            int factory=Snapshot(domain).Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,Apply(domain,new PlayableCommand(1,sequence++,"player-1",PlayableCommandKind.QueueTank,new[]{factory})));
            Assert.AreEqual(PlayableCommandStatus.Applied,Apply(domain,new PlayableCommand(1,sequence++,"player-1",PlayableCommandKind.QueueTank,new[]{factory})));
            Seconds(domain,PlayableProfile.Default.TankProductionSeconds+1);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var buildings=(System.Collections.IDictionary)DomainType.GetField("buildings",flags).GetValue(domain);
            var nav=(NavigationSession)DomainType.GetProperty("Navigation",flags).GetValue(domain);
            var points=(NavPoint[])DomainType.GetMethod("FactoryExitCandidates",flags).Invoke(domain,new[]{buildings[factory],(object)PlayableEntityKind.Tank});
            int blocker=10000;
            foreach(var point in points)if(nav.Crowd.CanPlace(point,PlayableProfile.Default.TankCollisionRadius))
                nav.Crowd.Add(blocker++,point);
            Seconds(domain,PlayableProfile.Default.TankProductionSeconds+1);
            var queued=Snapshot(domain).Buildings.Single(b=>b.Id==factory);
            Assert.AreEqual(1,queued.QueueCount,"The second paid ready tank remains until its spawn footprint is free.");
            Assert.GreaterOrEqual(queued.ProductionProgress,.99d);
        }

        [Test] public void ShellUsesFixedStraightSweepAfterTurretHasAimed()
        {
            var domain=NewDomain();
            DomainType.GetMethod("SpawnPlayer",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(domain,new object[]{new NavPoint(10,-4),new NavPoint(10,-4)});
            Assert.AreEqual(PlayableCommandStatus.Applied,Apply(domain,new PlayableCommand(1,1,"player-1",PlayableCommandKind.Attack,new[]{7},new NavPoint(17,-4),targetId:3)));
            StepMethod.Invoke(domain,new object[]{1d/30d}); var first=Snapshot(domain).Projectiles.Single(p=>p.OwnerId==7);
            StepMethod.Invoke(domain,new object[]{1d/30d}); var second=Snapshot(domain).Projectiles.Single(p=>p.OwnerId==7);
            Assert.Greater(second.Position.X,first.Position.X);
            Assert.AreEqual(first.Position.Z,second.Position.Z,1e-9d,"A shell keeps its firing direction instead of homing toward a target.");
        }
    }
}
