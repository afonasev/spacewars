using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class JointRefusalRecoveryTests
    {
        [TestCase(false)][TestCase(true)] public void AlignedSelectedStepDoesNotIntersectActualStationaryPeer(bool detour)
        {
            // Actual ordinary13 pre-clear pose/headings and peer28 holding pose.
            // Isolate the full-dt static-footprint invariant from forecast ranking.
            var position=new NavPoint(11.252490540005116,44.92127384954596);
            var bodies=new[]{new NavMotionBody{Id=13,Position=position,Target=detour?new NavPoint(11.852490540005116,45.121273849545965):new NavPoint(8,34.98857024149607),Heading=detour?-.07094852730207757:-1.8872450004729222,Radius=.58,Speed=detour?2.189948064565504:1.8583292191710978,TurnSpeed=7.5,Active=true},new NavMotionBody{Id=28,Position=new NavPoint(11.6,43.8),Radius=.58,Active=false,Potential=true,Speed=7}};
            bodies=bodies.Concat(new[]{new NavMotionBody{Id=19,Position=new NavPoint(12.56227293752776,46.24439643871587),Radius=.58},new NavMotionBody{Id=18,Position=new NavPoint(11.6,42.638256161968116),Radius=.58},new NavMotionBody{Id=31,Position=new NavPoint(12.43077945288659,47.4),Radius=.58},new NavMotionBody{Id=16,Position=new NavPoint(12.523042969250717,48.561099980850436),Radius=.58}}).ToArray();
            var neighbors=new int[bodies.Length*NavCrowdMotion.NeighborCapacity];for(int k=1;k<bodies.Length;k++)neighbors[k-1]=k;var profile=PlayableProfile.RecastGroupCorridorDefault.Navigation;
            var map=ThreeCrossingsMap.Default;var clearance=new NavCrowd(new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),profile,map);
            var unit=clearance.Add(13,position,.58,7,7.5,new NavLocation(position,"bank/east"));unit.ReplaceRoute(new[]{new NavPoint(8,34.98857024149607)});unit.Moving=true;unit.Goal=new NavPoint(-16,25.8);if(detour)unit.localRoute=new[]{bodies[0].Target};
            var proposal=NavCrowdMotion.Select(bodies,0,neighbors,bodies.Length-1,profile,1d/30,clearance,new NavPoint(-16,25.8));
            Assert.GreaterOrEqual(NavCrowdMotion.ClosestSquared(new NavPoint(position.X-bodies[1].Position.X,position.Z-bodies[1].Position.Z),proposal.Velocity,1d/30),Math.Pow(1.16,2)-1e-9,"A terrain-clear aligned candidate must also clear the actual stationary footprint, before Joint retracts it.");
            Assert.LessOrEqual(NavCrowdMotion.Length(proposal.Velocity),bodies[0].Speed+1e-12);Assert.LessOrEqual(Math.Abs(NavCrowdMotion.Angle(proposal.Heading-bodies[0].Heading)),bodies[0].TurnSpeed/30+1e-12);
        }
        sealed class RetreatClearance:INavMotionClearance {public bool ClearMotion(int actor,NavPoint end)=>end.X<=0&&Math.Abs(end.Z)<1e-10;}
        [TestCase(false)][TestCase(true)] public void ActivePeerDoesNotHideAnAlignedExhaustedLocalChoice(bool activePeer)
        {
            var bodies=new[]{new NavMotionBody{Id=13,Position=default,Target=new NavPoint(10,0),Radius=.58,Speed=7,TurnSpeed=7.5,Active=true},new NavMotionBody{Id=28,Position=new NavPoint(-10,10),Target=new NavPoint(-10,12),Radius=.58,Speed=7,Active=activePeer}};
            var neighbors=new int[2*NavCrowdMotion.NeighborCapacity];neighbors[0]=1;
            var q=NavCrowdMotion.Select(bodies,0,neighbors,1,PlayableProfile.RecastGroupCorridorDefault.Navigation,1d/30,new RetreatClearance(),bodies[0].Target);
            Assert.False(q.Blocked,"A terrain-clear retreat direction exists.");Assert.Zero(q.Heading);Assert.AreEqual(default(NavPoint),q.Velocity);
            Assert.True(q.StaticNoWinner,"A distant active peer cannot turn an aligned no-winner into an indefinite voluntary wait.");
        }
        [Test] public void ProvenanceRequiresActuallyRejectedTranslationAndIsFreshPerSelection()
        {
            var bodies=new[]{new NavMotionBody{Id=1,Position=new NavPoint(-1,0),Radius=.72,Active=true},new NavMotionBody{Id=2,Position=new NavPoint(1,0),Radius=.72,Active=true}};
            var proposals=new[]{new NavMotionProposal{Position=new NavPoint(0,0),Velocity=new NavPoint(1,0)},new NavMotionProposal{Position=new NavPoint(0,0),Velocity=new NavPoint(-1,0)}};
            NavCrowdMotion.ResolveJoint(bodies,proposals,1);foreach(var q in proposals){Assert.True(q.JointRefused);Assert.False(q.Blocked);Assert.AreEqual(default(NavPoint),q.Velocity);}
            proposals=new[]{new NavMotionProposal{Position=bodies[0].Position,Heading=.15,Refuse=true},new NavMotionProposal{Position=bodies[1].Position}};
            NavCrowdMotion.ResolveJoint(bodies,proposals,1);Assert.False(proposals[0].JointRefused,"A stale Refuse bit on a turn/wait is not actual rejected motion.");
            var fresh=NavCrowdMotion.Select(new[]{new NavMotionBody{Position=bodies[0].Position}},0,Array.Empty<int>(),0,NavigationProfile.Default,1,null);
            Assert.False(fresh.JointRefused,"Every row selection supplies fresh frame provenance.");
        }
        [Test] public void ValidPureTurnDoesNotStartRepairAndRealTranslationResetsExistingCounters()
        {
            var p=NavigationProfile.Default;var g=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var crowd=new NavCrowd(g,p);var a=crowd.Add(1,new NavPoint(0,0));Assert.True(crowd.SetRoute(1,new NavPoint(0,10),new[]{new NavPoint(0,10)}));
            crowd.Step(1d/30);Assert.AreEqual(new NavPoint(0,0),a.Position);Assert.Zero(a.noProgressTicks);Assert.Zero(a.blockedSeconds);
            var snapshot=crowd.CaptureState();var restored=new NavCrowd(g,p);restored.RestoreState(snapshot);crowd.Step(1d/30);restored.Step(1d/30);Assert.AreEqual(Newtonsoft.Json.JsonConvert.SerializeObject(crowd.CaptureState()),Newtonsoft.Json.JsonConvert.SerializeObject(restored.CaptureState()));
            a.Heading=Math.PI/2;a.noProgressTicks=3;a.blockedSeconds=.1;crowd.Step(1d/30);Assert.Greater(a.Position.Z,0);Assert.Zero(a.noProgressTicks);Assert.Zero(a.blockedSeconds);
        }
        [Test] public void RecordedOwnJointRefusalReachesExistingRepairWithoutDisplacingArrivedActors()
        {
            // Actualb7 ten final pose/heading/goal/outcome records. Owned frame,
            // static map/profile/terminal guards rebuilt; no full-world or typed
            // installed-route reconstruction is claimed.
            var map=ThreeCrossingsMap.Default;var p=PlayableProfile.RecastGroupCorridorDefault;var s=new NavigationSession(801,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),p.Navigation,null,map);
            var rows=new[]{
                new double[]{5,-50.922735048837076,42.692785458584403,1.5323399844483423,-46,46,1},
                new double[]{6,-49.468818971454475,42.420342454492349,-2.9877464154042443,-49.600000000000001,42.399999999999999,2},
                new double[]{7,-50.165811855848908,43.947384304305267,0.15377778983324833,-46,42.399999999999999,1},
                new double[]{8,-48.684043692760937,43.708300970636742,-0.2052619667101091,-42.399999999999999,42.399999999999999,1},
                new double[]{9,-49.656205412223436,45.907509848961077,1.0247410217828639,-49.600000000000001,46,2},
                new double[]{10,-48.732166230788543,40.839409024466399,0.19450703370357583,-42.399999999999999,46,1},
                new double[]{11,-47.963038961235071,42.058133409736186,2.4826047464201864,-49.600000000000001,49.600000000000001,1},
                new double[]{12,-46.22696329359561,42.321028256280947,1.3454702644184866,-46,49.600000000000001,1},
                new double[]{13,-42.330070025403295,49.53377579872928,2.3834052506482175,-42.399999999999999,49.600000000000001,2},
                new double[]{14,-47.270883190352805,43.326328448050752,-2.0041680616107538,-53.200000000000003,38.799999999999997,1},
            };
            try{foreach(var r in rows){var pos=new NavPoint(r[1],r[2]);var goal=new NavPoint(r[4],r[5]);var a=s.Crowd.Add((int)r[0],pos,.72,4,4.5,new NavLocation(pos,"deck/-1/1"),true);a.Team=1;a.Heading=r[3];Assert.True(s.Crowd.SetRoute(a.Id,goal,new[]{goal},new NavLocation(goal,"deck/-1/1")));a.Moving=(int)r[6]==1;a.Outcome=(NavigationOutcome)(int)r[6];a.routeIndex=a.Moving?1:2;}
                Assert.True(s.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var frame=NavPreparedCrowdStep.Create(s.Crowd,10,scope.OwnedReservation());try{frame.Observe();var starts=frame.After.Select(a=>a.Position).ToArray();bool refused=false;
                    for(int tick=0;tick<60;tick++){
                        var before=frame.After.Select(a=>a.Position).ToArray();for(int i=0;i<10;i++){frame.Preferred[i]=frame.RecastReceiver[i]=4;frame.TerminalSeeds[i]=frame.After[i].Outcome==NavigationOutcome.Arrived;}frame.Compute(1d/30);
                        refused|=frame.Proposals[0].JointRefused&&frame.After[0].noProgressTicks>0;
                        for(int i=0;i<10;i++){var a=frame.After[i];Assert.LessOrEqual(NavCrowdMotion.Length(new NavPoint(a.Position.X-before[i].X,a.Position.Z-before[i].Z)),a.Speed/30+1e-12);Assert.True(s.Crowd.Geometry.SegmentFree(before[i],a.Position,a.Radius));Assert.True(map.TryTraverse(new NavLocation(before[i],"deck/-1/1"),a.Position,a.Radius,out var end));Assert.AreEqual("deck/-1/1",end.SurfaceId);if(rows[i][6]==2){Assert.AreEqual(starts[i],a.Position);Assert.AreEqual(NavigationOutcome.Arrived,a.Outcome);}
                            for(int k=i+1;k<10;k++){var b=frame.After[k];var relative=new NavPoint((a.Position.X-before[i].X-b.Position.X+before[k].X)*30,(a.Position.Z-before[i].Z-b.Position.Z+before[k].Z)*30);Assert.GreaterOrEqual(NavCrowdMotion.ClosestSquared(new NavPoint(before[i].X-before[k].X,before[i].Z-before[k].Z),relative,1d/30),Math.Pow(a.Radius+b.Radius,2)-1e-9);}
                        }
                    }
                    Assert.True(refused,"Recorded first Joint rejection must survive inplace heading fallback into own progress handling.");Assert.Greater(frame.Working.RepairCount,0,"Existing retry profile is reachable without changing its threshold.");
                }finally{frame.Release();}scope.Discard();}
            }finally{s.ReleaseCommittedHostRoot();}
        }
    }
}
