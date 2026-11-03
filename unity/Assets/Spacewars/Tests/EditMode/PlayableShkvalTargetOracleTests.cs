using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableShkvalTargetOracleTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private static double Distance(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        [TestCase(PlayableBuildingKind.Factory)][TestCase(PlayableBuildingKind.Hangar)]
        [TestCase(PlayableBuildingKind.SentryTurret)][TestCase(PlayableBuildingKind.RepairTower)][TestCase(PlayableBuildingKind.GaussTower)]
        public void BlastBoundsKeepContactsOnAnimatedAndPartiallyConstructedBuildings(PlayableBuildingKind kind)
        {
            var profile=PlayableProfile.Default;
            var center=new NavPoint(14,-7);
            var pose=BuildingCombatGeometry.Pose(profile,kind,false,center,1,heading:.73,opening:.8,platform:.6,constructionProgress:.4);
            if(TerritoryRules.Defensive(kind))
                Assert.True(BuildingCombatGeometry.TryPreparedTurretPose(profile,kind,center,1,.73,.61,.4,.12,out pose));
            foreach(var p in new[]{new BallisticPoint(14,2,-7),new BallisticPoint(15.4,1.1,-8.2),new BallisticPoint(5,.5,-11)}){
                var q=pose.Closest(p);
                double radius=Math.Sqrt((p.X-q.X)*(p.X-q.X)+(p.Y-q.Y)*(p.Y-q.Y)+(p.Z-q.Z)*(p.Z-q.Z));
                Assert.False(pose.OutsideBlastSquare(p.Ground,radius+1e-5));
                var building=new BallisticBody(7,PlayableOwner.Enemy,center,default,1,3,100,true,1,pose);
                Assert.True(PlayableBallistics.BlastHits(p,radius,building,Array.Empty<NavObstacle>(),2,new[]{building}),
                    "An explosion touching the posed surface must cause splash even outside the navigation footprint.");
                if(radius>.001)Assert.False(PlayableBallistics.BlastHits(p,radius-.001,building,Array.Empty<NavObstacle>(),2,new[]{building}));
            }
        }
        [TestCase(false)][TestCase(true)] public void ExplicitShotAndCurrentCapturePreserveCenteredFlight(bool guided)
        {
            var profile=PlayableProfile.Default;const int seed=19261019;const string source="native-shkval-target-oracle-v1";
            var c=OfflineParticipantAuthorityTests.Config(2,false,seed);var costs=new double[c.Starts.Count,c.Starts.Count];for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)costs[i,j]=c.RouteCost(i,j);
            var config=new OfflineMatchConfiguration(profile,source,c.MapIdentity,"native-current-shkval-capture",seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),costs,
                scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Shkval,new NavPoint(-13,-5),upgraded:guided),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-4,-5)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-4,-3.5))});
            var world=new PlayableDomain(profile,91,config);PlayableDomain restored=null;
            object Call(PlayableDomain d,string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,Hidden).Invoke(d,args);
            IDictionary Units(PlayableDomain d)=>(IDictionary)typeof(PlayableDomain).GetField("units",Hidden).GetValue(d);
            IList Shots(PlayableDomain d)=>(IList)typeof(PlayableDomain).GetField("projectiles",Hidden).GetValue(d);
            try{
                int Id(int scenario){var unit=Units(world).Values.Cast<object>().Single(u=>(int)u.GetType().GetField("ScenarioIndex").GetValue(u)==scenario);return (int)unit.GetType().GetField("Id").GetValue(unit);}
                int gun=Id(0),enemy=Id(1);
                foreach(object unit in Units(world).Values)unit.GetType().GetField("Reload").SetValue(unit,999d);
                var actor=Units(world)[gun];actor.GetType().GetField("Reload").SetValue(actor,0d);
                var victim=Units(world)[enemy];victim.GetType().GetField("Velocity").SetValue(victim,new NavPoint(.5,0));Call(world,"RefreshVision");
                Assert.AreEqual(enemy,(int)Call(world,"ArtilleryTarget",actor,new NavPoint(-13,-5)),"The nearby ally must not veto the closest enemy");
                Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(91,1,c.Roster[0].Id,PlayableCommandKind.Attack,new[]{gun},targetId:enemy),out _));
                Call(world,"AdvanceIndexedCombat",1d/30);
                var preparation=world.CaptureWorldBytes(seed,source);var preparing=PlayableDomain.RestoreConfiguredWorldBytes(preparation,profile,seed,source,config);
                try{CollectionAssert.AreEqual(preparation,preparing.CaptureWorldBytes(seed,source));for(int tick=0;tick<55;tick++){Call(preparing,"AdvanceIndexedCombat",1d/30);Call(world,"AdvanceIndexedCombat",1d/30);CollectionAssert.AreEqual(world.CaptureWorldBytes(seed,source),preparing.CaptureWorldBytes(seed,source),"Exact pre-first-shot elevation continuation");}}
                finally{preparing.Navigation.ReleaseCommittedHostRoot();preparing.DestroyOwnedNavigation();}
                for(int tick=0;tick<60&&Shots(world).Count==0;tick++)Call(world,"AdvanceIndexedCombat",1d/30);
                Assert.AreEqual(1,Shots(world).Count,"Explicit Attack must launch");
                var rocket=Shots(world)[0].GetType().GetField("Rocket").GetValue(Shots(world)[0]);var flight=(BallisticFlight)rocket.GetType().GetField("Flight").GetValue(rocket);
                var expectedEnd=new BallisticPoint(-4,PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank),-5);
                var launchBinding=(ShkvalLaunchSnapshot)rocket.GetType().GetField("Launch").GetValue(rocket);
                var physical=ShkvalLaunchGeometry.At(launchBinding.Ground,launchBinding.Hull,launchBinding.Turret,launchBinding.Pitch,guided,launchBinding.Barrel,profile.ShkvalModelRadius*profile.ShkvalModelScale/2.05,profile.ShkvalProjectileLength).Center;
                Assert.AreEqual(physical,flight.Start);Assert.AreEqual(expectedEnd,flight.End);Assert.AreEqual(new BallisticFlight(physical,expectedEnd,profile.ShkvalArcHeight,profile.ShkvalProjectileSpeed).Duration,flight.Duration);
                byte[] bytes=world.CaptureWorldBytes(seed,source);restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,seed,source,config);
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(seed,source));
                foreach(var d in new[]{world,restored}){Call(d,"RefreshVision");Call(d,"ReconcileWreckRoutes");}
                string Difference(object a,object b,string path,int depth){
                    if(depth>7||a==null||b==null||a.GetType()!=b.GetType())return path;
                    var t=a.GetType();if(t.IsPrimitive||t.IsEnum||a is string)return path+"="+a+"/"+b;
                    if(a is IEnumerable)return path+"[collection]";
                    return string.Join(";",t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Where(f=>!(bool)typeof(PlayableWorldRestoreTests).GetMethod("ExcludeField",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{f})).Where(f=>PlayableWorldRestoreTests.Facts(f.GetValue(a),true)!=PlayableWorldRestoreTests.Facts(f.GetValue(b),true)).Select(f=>Difference(f.GetValue(a),f.GetValue(b),path+"."+f.Name,depth+1)));
                }
                Assert.AreEqual(PlayableWorldRestoreTests.Facts(world,true),PlayableWorldRestoreTests.Facts(restored,true),Difference(world,restored,"domain",0));
                for(int tick=0;tick<40;tick++){
                    foreach(var d in new[]{world,restored}){Call(d,"AdvanceIndexedCombat",1d/30);Call(d,"AdvanceProjectiles",1d/30);Call(d,"RefreshVision");Call(d,"ReconcileWreckRoutes");}
                    Assert.AreEqual(PlayableWorldRestoreTests.Facts(world,true),PlayableWorldRestoreTests.Facts(restored,true),"Current Shkval authority suffix "+tick);
                }
                CollectionAssert.AreEqual(world.CaptureWorldBytes(seed,source),restored.CaptureWorldBytes(seed,source));
            }finally{foreach(var d in new[]{world,restored})if(d!=null){d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();}}
        }
        [TestCase(false)][TestCase(true)] public void MuzzleBindingAndFlightContinueExactlyAfterCurrentCapture(bool guided)
        {
            var profile=PlayableProfile.Default;const int seed=19261019;const string source="native-shkval-target-oracle-v1";
            var c=OfflineParticipantAuthorityTests.Config(2,false,seed);var costs=new double[c.Starts.Count,c.Starts.Count];for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)costs[i,j]=c.RouteCost(i,j);
            var config=new OfflineMatchConfiguration(profile,source,c.MapIdentity,"native-current-shkval-capture",seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),costs,
                scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Shkval,new NavPoint(-13,-5),upgraded:guided),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-4,-5)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-4,-3.5))});
            var world=new PlayableDomain(profile,91,config);PlayableDomain restored=null;
            object Call(PlayableDomain d,string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,Hidden).Invoke(d,args);
            IDictionary Units(PlayableDomain d)=>(IDictionary)typeof(PlayableDomain).GetField("units",Hidden).GetValue(d);
            IList Shots(PlayableDomain d)=>(IList)typeof(PlayableDomain).GetField("projectiles",Hidden).GetValue(d);
            try{
                int Id(int scenario){var unit=Units(world).Values.Cast<object>().Single(u=>(int)u.GetType().GetField("ScenarioIndex").GetValue(u)==scenario);return (int)unit.GetType().GetField("Id").GetValue(unit);}
                int gun=Id(0),enemy=Id(1);
                foreach(object unit in Units(world).Values)unit.GetType().GetField("Reload").SetValue(unit,999d);
                var actor=Units(world)[gun];actor.GetType().GetField("Reload").SetValue(actor,0d);
                var victim=Units(world)[enemy];victim.GetType().GetField("Velocity").SetValue(victim,new NavPoint(.5,0));Call(world,"RefreshVision");
                Assert.AreEqual(enemy,(int)Call(world,"ArtilleryTarget",actor,new NavPoint(-13,-5)),"The nearby ally must not veto the closest enemy");
                Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(91,1,c.Roster[0].Id,PlayableCommandKind.Attack,new[]{gun},targetId:enemy),out _));
                for(int tick=0;tick<60&&Shots(world).Count==0;tick++)Call(world,"AdvanceIndexedCombat",1d/30);
                Assert.AreEqual(1,Shots(world).Count,"Explicit Attack must launch");
                var rocket=Shots(world)[0].GetType().GetField("Rocket").GetValue(Shots(world)[0]);var flight=(BallisticFlight)rocket.GetType().GetField("Flight").GetValue(rocket);
                var expectedEnd=new BallisticPoint(-4,PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank),-5);
                var launchBinding=(ShkvalLaunchSnapshot)rocket.GetType().GetField("Launch").GetValue(rocket);
                var physical=ShkvalLaunchGeometry.At(launchBinding.Ground,launchBinding.Hull,launchBinding.Turret,launchBinding.Pitch,guided,launchBinding.Barrel,profile.ShkvalModelRadius*profile.ShkvalModelScale/2.05,profile.ShkvalProjectileLength).Center;
                Assert.AreEqual(physical,flight.Start);Assert.AreEqual(expectedEnd,flight.End);Assert.AreEqual(new BallisticFlight(physical,expectedEnd,profile.ShkvalArcHeight,profile.ShkvalProjectileSpeed).Duration,flight.Duration);
                byte[] bytes=world.CaptureWorldBytes(seed,source);restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,seed,source,config);
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(seed,source));
                var launch=(ShkvalLaunchSnapshot)rocket.GetType().GetField("Launch").GetValue(rocket);Assert.NotNull(launch);Assert.AreEqual(0,launch.Barrel);Assert.AreEqual(guided,launch.Upgraded);
                CollectionAssert.AreEqual((double[])actor.GetType().GetField("ShkvalCells").GetValue(actor),(double[])Units(restored)[gun].GetType().GetField("ShkvalCells").GetValue(Units(restored)[gun]),"Per-cell feed state survives capture");
                Assert.AreEqual(1,(int)Units(restored)[gun].GetType().GetField("BurstSequence").GetValue(Units(restored)[gun]));
Assert.AreEqual(PlayableWorldRestoreTests.Facts(Shots(world),true),PlayableWorldRestoreTests.Facts(Shots(restored),true));
                for(int tick=0;tick<40;tick++){
                    foreach(var d in new[]{world,restored}){Call(d,"AdvanceIndexedCombat",1d/30);Call(d,"AdvanceProjectiles",1d/30);Call(d,"RefreshVision");Call(d,"ReconcileWreckRoutes");}
                    Assert.AreEqual(PlayableWorldRestoreTests.Facts(Shots(world),true),PlayableWorldRestoreTests.Facts(Shots(restored),true),"Current Shkval authority suffix "+tick);
                }
                CollectionAssert.AreEqual(world.CaptureWorldBytes(seed,source),restored.CaptureWorldBytes(seed,source));
                foreach(var d in new[]{world,restored}){var u=Units(d)[gun];u.GetType().GetField("Reload").SetValue(u,0d);u.GetType().GetField("StoppedSeconds").SetValue(u,10d);Call(d,"AdvanceIndexedCombat",1d/30);}
                var next=Shots(restored).Cast<object>().Last();var nextRocket=next.GetType().GetField("Rocket").GetValue(next);Assert.AreEqual(1,((ShkvalLaunchSnapshot)nextRocket.GetType().GetField("Launch").GetValue(nextRocket)).Barrel);
                CollectionAssert.AreEqual(world.CaptureWorldBytes(seed,source),restored.CaptureWorldBytes(seed,source));
                // Magazine-only tail must survive after all in-flight projectiles are gone.
                Shots(world).Clear();Shots(restored).Clear();
                var magazineBytes=world.CaptureWorldBytes(seed,source);
                var magazineOnly=PlayableDomain.RestoreConfiguredWorldBytes(magazineBytes,profile,seed,source,config);
                try{CollectionAssert.AreEqual(magazineBytes,magazineOnly.CaptureWorldBytes(seed,source));}
                finally{magazineOnly.Navigation.ReleaseCommittedHostRoot();magazineOnly.DestroyOwnedNavigation();}

            }finally{foreach(var d in new[]{world,restored})if(d!=null){d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();}}
        }
        [Test,Timeout(120000)] public void TwoOrdinaryShkvalsMoveChooseLaunchAndResumeAfterTargetLoss()
        {
            var c=OfflineParticipantAuthorityTests.Config(2,false,19261019);var costs=new double[c.Starts.Count,c.Starts.Count];for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)costs[i,j]=c.RouteCost(i,j);
            var origins=new[]{new NavPoint(-18,6),new NavPoint(-18,10)};var goals=new[]{new NavPoint(5,6),new NavPoint(5,10)};
            var config=new OfflineMatchConfiguration(c.Profile,"native-two-shkval-target-oracle-v1","native-flat-eight-starts-v1","UnityHostRouteService",c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),costs,
                scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Shkval,origins[0]),new OfflineScenarioUnit(1,PlayableEntityKind.Shkval,origins[1],upgraded:true),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(3,8))});
            using(var host=new Spacewars.Presentation.UnityHostRouteService())using(var runtime=new PlayableRuntime(config,91,startPaused:true)){
                void Service(){host.Service(runtime,c.Profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(host.CertifiedPublication?.Failure);}
                bool Until(Func<bool> condition,double seconds){var timer=System.Diagnostics.Stopwatch.StartNew();while(timer.Elapsed.TotalSeconds<seconds){Service();if(condition())return true;System.Threading.Thread.Sleep(1);}return false;}
                Assert.True(Until(()=>runtime.IsPrepared&&runtime.Latest.Paused,30));
                var guns=runtime.ParticipantView(c.Roster[0].Id).Entities.Where(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Shkval).OrderBy(e=>e.Id).ToArray();Assert.AreEqual(2,guns.Length);Assert.False(guns[0].Upgraded);Assert.True(guns[1].Upgraded);
                int enemy=runtime.ParticipantView(c.Roster[1].Id).Entities.Single(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Tank).Id;
                runtime.RequestPause(false);Assert.True(Until(()=>!runtime.Latest.Paused,5));
                Assert.True(runtime.TrySubmit(new PlayableCommand(91,1,c.Roster[1].Id,PlayableCommandKind.Hold,new[]{enemy}).AsHuman()).Accepted);
                for(int i=0;i<2;i++)Assert.True(runtime.TrySubmit(new PlayableCommand(91,i+1,c.Roster[0].Id,PlayableCommandKind.AttackMove,new[]{guns[i].Id},goals[i]).AsHuman()).Accepted);
                var applied=new bool[2];var motion=new long[]{-1,-1};var choice=new long[]{-1,-1};var shot=new long[]{-1,-1};var resume=new long[]{-1,-1};var stops=new NavPoint[2];bool retreat=false;
                Assert.True(Until(()=>{
                    foreach(var receipt in runtime.DrainReceipts())if(receipt.OwnerId==c.Roster[0].Id&&receipt.Sequence>=1&&receipt.Sequence<=2&&receipt.Status==PlayableCommandStatus.Applied)applied[receipt.Sequence-1]=true;
                    var view=runtime.ParticipantView(c.Roster[0].Id);
                    for(int i=0;i<2;i++){
                        var gun=view.Entities.SingleOrDefault(e=>e.Id==guns[i].Id);Assert.NotNull(gun,"Both ordinary Shkvals must survive");
                        if(motion[i]<0&&Distance(gun.Position,origins[i])>.05){motion[i]=view.Tick;TestContext.WriteLine("motion gun="+i+" tick="+view.Tick);}
                        if(choice[i]<0&&gun.TargetId==enemy){choice[i]=view.Tick;TestContext.WriteLine("choice gun="+i+" tick="+view.Tick);}
                        if(shot[i]<0&&view.Projectiles.Any(p=>p.OwnerId==gun.Id&&p.TargetId==enemy)){shot[i]=view.Tick;stops[i]=gun.Position;TestContext.WriteLine("launch gun="+i+" tick="+view.Tick);}
                        if(shot[i]>=0&&resume[i]<0&&gun.TargetId==0&&gun.Moving&&Distance(gun.Position,stops[i])>.2){resume[i]=view.Tick;TestContext.WriteLine("resume gun="+i+" tick="+view.Tick);}
                    }
                    if(!retreat&&shot.All(t=>t>=0)){Assert.True(runtime.TrySubmit(new PlayableCommand(91,2,c.Roster[1].Id,PlayableCommandKind.Move,new[]{enemy},new NavPoint(3,27)).AsHuman()).Accepted);retreat=true;}
                    return applied.All(x=>x)&&resume.All(t=>t>=0);
                },60),"Two ordinary Shkvals must move, choose/launch and resume after target loss");
                var final=runtime.ParticipantView(c.Roster[0].Id);
                for(int i=0;i<2;i++){Assert.Greater(choice[i],motion[i]);Assert.Greater(shot[i],choice[i]);Assert.Greater(resume[i],shot[i]);var gun=final.Entities.Single(e=>e.Id==guns[i].Id);Assert.AreEqual(PlayableTacticalOrderKind.AttackMove,gun.CurrentOrder.Kind);Assert.AreEqual(goals[i],gun.CurrentOrder.Destination);}
            }
        }
    }
}
