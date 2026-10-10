using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class NativeAiRetreatNavigationTests
    {
        [UnityTest] public IEnumerator Retreat_ActualThreeCrossingsRampRouteChangesSurfaceHeightThroughPermittedTransition()
        {
            var data=JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text);var map=new ThreeCrossingsMap(data);
            var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            var ramp=map.Supports.First(s=>s.Id.Contains("ramp"));var gradient=ramp.Gradient;double size=Math.Sqrt(gradient.X*gradient.X+gradient.Z*gradient.Z);
            var start=new NavPoint(ramp.Origin.X-gradient.X/size*p.Navigation.ArrivalSlotSpacing,ramp.Origin.Z-gradient.Z/size*p.Navigation.ArrivalSlotSpacing);
            var goal=new NavPoint(ramp.Origin.X+gradient.X/size*(data.rampLength+p.Navigation.ArrivalTolerance),ramp.Origin.Z+gradient.Z/size*(data.rampLength+p.Navigation.ArrivalTolerance));
            var geometry=new NavGeometry(p.ArenaHalfExtent,PlayableMap.StaticObstacles(p),1);var crowd=new NavCrowd(geometry,p.Navigation);
            using(var router=new UnityNavigationRouter(geometry,p.Navigation))
            {
                var path=router.FindPath(start,goal);Assert.IsNotEmpty(path);Assert.AreEqual(goal,path.Last());
                var vision=new PlayableVision(0,p.ArenaHalfExtent,p.ArenaHalfExtent,1,1);vision.Refresh(new[]{new VisionSource(start,p.ArenaHalfExtent*2)},Array.Empty<KnownBuilding>());
                var unit=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,start,p.TankHealth,false,0,0,0);
                var home=new PlayableBuildingSnapshot(50,PlayableOwner.Player,PlayableBuildingKind.Factory,goal,100,1,0,0,default);
                var proof=new PlayableRouteProof(1,PlayableOwner.Player,71,10,1,start,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,50,goal,goal,path);
                var o=PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,7,10,10,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,new[]{unit},new[]{home},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:new[]{proof}));
                var army=new AiArmyState{Id=1,OwnerId=o.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Phase=AiArmyPhase.Regrouping,Members=new[]{1},Reinforcements=Array.Empty<int>()};
                var action=AiTacticalExecutor.Withdrawal(o,army,p,AiProfile.Initial,1,stalled:true);Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);Assert.AreEqual(goal,action.Target);
                crowd.Add(1,start,p.TankCollisionRadius,p.TankSpeed,p.TankTurnSpeed);Assert.True(crowd.SetRoute(1,action.Target,path));bool raised=false;
                for(int tick=0;tick<1200&&crowd.Units[0].Outcome!=NavigationOutcome.Arrived;tick++)
                {
                    var before=crowd.Units[0].Position;crowd.Step(1d/30);var after=crowd.Units[0].Position;Assert.True(geometry.SegmentFree(before,after,p.TankCollisionRadius));Assert.True(map.SupportsSweep(before,after,p.TankCollisionRadius));raised|=map.SurfaceHeight(after)>map.SurfaceHeight(start)+p.Navigation.ArrivalTolerance;
                    Assert.LessOrEqual(Math.Abs(map.SurfaceHeight(after)-map.SurfaceHeight(before)),size*Math.Sqrt(Math.Pow(after.X-before.X,2)+Math.Pow(after.Z-before.Z,2))+1e-8);
                    if(tick%30==0)yield return null;
                }
                Assert.True(raised);Assert.AreEqual(NavigationOutcome.Arrived,crowd.Units[0].Outcome);Assert.Greater(map.SurfaceHeight(crowd.Units[0].Position),map.SurfaceHeight(start));
                TestContext.WriteLine("A3-09 actual native ramp "+ramp.Id+" startHeight="+map.SurfaceHeight(start)+" goalHeight="+map.SurfaceHeight(goal)+" routePoints="+path.Length+"; overlapping stacked floors remain unsupported by XZ authority");
            }
        }
    }
}
