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
        [Test] public void WideStagingRecoveryUsesAcceptedWaypointWhenTemporaryFrontierIsOccupied()
        {
            var map=ThreeCrossingsMap.Default;var profile=PlayableProfile.RecastGroupCorridorDefault.Navigation;
            var s=new NavigationSession(805,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),profile,null,map);
            try{
                var from=new NavPoint(12.697164442559522,17.816507576646746);var accepted=new NavPoint(12.80000114440918,35.400001525878906);var frontier=new NavPoint(11.6,from.Z);
                var own=s.Crowd.Add(48,from,.58,7,7.5,new NavLocation(from,"bank/east"));own.Heading=Math.PI;Assert.True(s.Crowd.SetRoute(48,accepted,new[]{accepted},new NavLocation(accepted,"bank/east")));
                foreach(var p in new[]{new NavPoint(11.6,17.439749167483455),new NavPoint(11.6,18.6)}){var peer=s.Crowd.Add(s.Crowd.Units.Count+100,p,.58,7,7.5,new NavLocation(p,"bank/east"));peer.Outcome=NavigationOutcome.Arrived;}
                Assert.True(s.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,805,1,out var scope));using(scope){var frame=NavPreparedCrowdStep.Create(s.Crowd,3,scope.OwnedReservation());try{
                    frame.Observe();int row=frame.Find(48);for(int tick=0;tick<15&&frame.After[row].localRoute.Length==0;tick++){frame.Formation[row]=new NavFormationFrameHint{Flags=NavFormationFrameHint.Guide|NavFormationFrameHint.StagingFeed,Target=frontier};frame.Compute(1d/30);}
                    Assert.Greater(frame.Working.RepairCount,0);Assert.Greater(frame.After[row].localRoute.Length,0,"Recovery must not repeatedly fail toward an occupied temporary holding point.");Assert.Greater(frame.After[row].noProgressTicks,0,"The real failure enters bounded recovery rather than policy-wait masking.");Assert.AreEqual(accepted,frame.After[row].Goal);Assert.Less(frame.After[row].noProgressTicks,150);Assert.True(frame.After[row].Moving);
                }finally{frame.Release();}scope.Discard();}
            }finally{s.ReleaseCommittedHostRoot();}
        }
        [Test] public void AdmittedRecoveryUsesAcceptedRouteAroundRecordedHoldingCurtain()
        {
            // Actual ordinary-bank1 actor34@1969 and every peer within repair reach.
            var map=ThreeCrossingsMap.Default;var profile=PlayableProfile.RecastGroupCorridorDefault.Navigation;
            var session=new NavigationSession(805,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),profile,null,map);
            try{var from=new NavPoint(16.30631606441674,39.077261495845306);var accepted=new NavPoint(-8.19999885559082,33.60000228881836);var guide=new NavPoint(3.9063160644167407,41.6);
                var own=session.Crowd.Add(34,from,.58,7,7.5,new NavLocation(from,"bank/east"));own.Heading=2.940885211069915;Assert.True(session.Crowd.SetRoute(34,accepted,new[]{accepted},new NavLocation(accepted,"bank/west")));
                foreach(var at in new[]{
                    new NavPoint(12.362118169702056,36.694672505225505),
                    new NavPoint(12.463321349782166,38.065520069037554),
                    new NavPoint(12.548847714207163,41.59046588421331),
                    new NavPoint(12.485480704044694,34.489088428633956),
                    new NavPoint(16.089563269041978,41.54253218842191),
                    new NavPoint(19.69443050674439,38.09829444540725),
                    new NavPoint(19.60366306038688,34.250842622475886),
                    new NavPoint(16.0,36.6),
                    new NavPoint(16.0,40.2),
                    new NavPoint(16.0,43.8),
                    new NavPoint(16.08569114875063,34.34493956596554),
                    new NavPoint(16.103228369978222,37.93342612488696),
                }){var peer=session.Crowd.Add(100+session.Crowd.Units.Count,at,.58,7,7.5,new NavLocation(at,"bank/east"));peer.Outcome=NavigationOutcome.Arrived;}
                Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,805,1,out var scope));using(scope){var frame=NavPreparedCrowdStep.Create(session.Crowd,session.Crowd.Units.Count,scope.OwnedReservation());try{frame.Observe();int row=frame.Find(34);for(int tick=0;tick<30&&frame.After[row].localRoute.Length==0;tick++){frame.Formation[row]=new NavFormationFrameHint{Flags=NavFormationFrameHint.Guide,Target=guide};frame.Compute(1d/30);}
                    Assert.Greater(frame.Working.RepairCount,0);Assert.Greater(frame.After[row].localRoute.Length,0,"An occupied temporary upper lane cannot replace accepted lower route intent during recovery.");Assert.AreEqual(accepted,frame.After[row].Goal);Assert.True(frame.After[row].Moving);Assert.Less(frame.After[row].noProgressTicks,150);
                    var at=frame.After[row].Location;foreach(var point in frame.After[row].localRoute){Assert.True(frame.Working.Geometry.SegmentFree(at.Position,point,.58));Assert.True(map.TryTraverse(at,point,.58,out at));}
                }finally{frame.Release();}scope.Discard();}
            }finally{session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);}
        }
        [Test] public void BankApproachWaitsOnOtherFloorsAndAheadOfActualRearFrontier()
        {
            var layout=new RecastFormationGuide.Layout(new NavPoint(8,38),new NavPoint(-8,38),new NavPoint(-1,0),16,-4.42,4.42,3.6,.58);
            var at=new NavLocation(new NavPoint(20,38),"lower");var behind=RecastFormationGuide.BankApproach(at,"lower",1,layout,-8);
            Assert.AreEqual(NavFormationFrameHint.Bound,behind.Flags);Assert.AreEqual(-8,behind.ForwardLimit);Assert.AreEqual(default(NavPoint),behind.Target,"Staging must retain accepted route intent, not a temporary parking target.");
            Assert.AreEqual(NavFormationFrameHint.Wait,RecastFormationGuide.BankApproach(new NavLocation(at.Position,"upper"),"lower",1,layout,-8).Flags);
            Assert.AreEqual(NavFormationFrameHint.Wait,RecastFormationGuide.BankApproach(at,"lower",1,layout,-12).Flags);
            Assert.AreEqual(NavFormationFrameHint.Wait,RecastFormationGuide.BankApproach(new NavLocation(new NavPoint(7.8,38),"bridge/north"),"bank/east",1,layout,-8).Flags);
            var rear=new NavLocation(new NavPoint(30,38),"lower");var first=RecastFormationGuide.BankApproach(rear,"lower",1,layout,-8,5,5,4);var second=RecastFormationGuide.BankApproach(rear,"lower",1,layout,-8,6,5,4);
            Assert.AreEqual(NavFormationFrameHint.Bound,first.Flags);Assert.AreEqual(NavFormationFrameHint.Bound,second.Flags);Assert.AreEqual(first.ForwardLimit-3.6,second.ForwardLimit);
            Assert.AreEqual(NavFormationFrameHint.Wait,RecastFormationGuide.BankApproach(rear,"lower",1,layout,-8,9,5,4).Flags,"Far future row may not compress at the frontier.");
        }
        [TestCase(0d,false)][TestCase(.7d,false)][TestCase(1.9d,false)]
        [TestCase(0d,true)][TestCase(.7d,true)][TestCase(1.9d,true)]
        public void OpposingGuideLanesKeepFrozenCountAndDisjointPhysicalSweeps(double angle,bool asymmetric)
        {
            var d=new NavPoint(Math.Cos(angle),Math.Sin(angle));var end=new NavPoint(16*d.X,16*d.Z);
            double low=asymmetric?-3.42:-4.42,high=asymmetric?5.42:4.42;
            var forward=new RecastFormationGuide.Layout(default,end,d,16,low,high,3.6,.58);
            var reverse=new RecastFormationGuide.Layout(end,default,new NavPoint(-d.X,-d.Z),16,-high,-low,3.6,.58);
            Assert.AreEqual(3,forward.Lanes);Assert.AreEqual(forward.Lanes,reverse.Lanes);Assert.AreEqual(3.6,forward.Pitch);
            for(int i=0;i<forward.Lanes;i++){
                var a=forward.Point(8,i);double lateral=a.X*(-d.Z)+a.Z*d.X;Assert.GreaterOrEqual(lateral,low-1e-12);Assert.LessOrEqual(lateral,high+1e-12);
                for(int j=0;j<reverse.Lanes;j++){
                    var b=reverse.Point(8,j);double dx=a.X-b.X,dz=a.Z-b.Z;
                    Assert.GreaterOrEqual(dx*dx+dz*dz,Math.Pow(2*.58,2)-1e-12,"Counterflow full straight sweeps must have separate footprint lanes");
                }
                if(i>0){var b=forward.Point(8,i-1);Assert.GreaterOrEqual(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2),Math.Pow(2*.58,2)-1e-12);}
            }
            var narrow=new RecastFormationGuide.Layout(default,end,d,16,-.1,.1,3.6,.58);
            var point=narrow.Point(8,0);Assert.AreEqual(8*d.X,point.X);Assert.AreEqual(8*d.Z,point.Z,"An unfit half must retain the certified original lane");
        }
        [TestCase(false)][TestCase(true)]
        public void DirectionalNorthGuideCentersRetainActualTypedFootprintClearance(bool reverse)
        {
            var p=PlayableProfile.RecastGroupCorridorDefault;var map=(ThreeCrossingsMap)p.AuthoredMap;
            var bounds=map.Supports.Single(s=>s.SurfaceId=="bridge/north").Bounds;double middle=(bounds.MinZ+bounds.MaxZ)/2,radius=p.ExplorerCollisionRadius;
            var a=new NavPoint(reverse?bounds.MaxX:bounds.MinX,middle);var b=new NavPoint(reverse?bounds.MinX:bounds.MaxX,middle);var d=new NavPoint(reverse?-1:1,0);
            double half=(bounds.MaxZ-bounds.MinZ)/2-radius;var layout=new RecastFormationGuide.Layout(a,b,d,bounds.MaxX-bounds.MinX,-half,half,p.Navigation.ArrivalSlotSpacing,radius);
            Assert.GreaterOrEqual(layout.Lanes,2);
            for(int lane=0;lane<layout.Lanes;lane++){
                var point=layout.Point(layout.Length/2,lane);
                Assert.True(map.IsValidLocation(new NavLocation(point,"bridge/north"),radius),"Actual typed full footprint refuses lane "+lane+" at "+point.X+","+point.Z);
            }
        }
        sealed class ShortTurnOnlyClearance:INavMotionClearance
        {
            readonly double reachSquared;internal ShortTurnOnlyClearance(double reach){reachSquared=reach*reach;}
            public bool ClearMotion(int actor,NavPoint end)=>end.X<=0&&end.X*end.X+end.Z*end.Z<=reachSquared;
        }
        [Test] public void RejectedLongStaticTurnForecastPreservesImmediatePhysicalMoveEvidence()
        {
            var profile=PlayableProfile.RecastGroupCorridorDefault.Navigation;const double dt=1d/30;const double speed=7;
            var bodies=new[]{new NavMotionBody{Id=39,Position=default,Target=new NavPoint(-10,0),Heading=0,Radius=.58,Speed=speed,TurnSpeed=7.5,Active=true},new NavMotionBody{Id=35,Position=new NavPoint(-2,0),Radius=.58,Speed=speed,Active=false}};
            var neighbors=new int[bodies.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[bodies.Length];NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,dt);
            var proposal=NavCrowdMotion.Select(bodies,0,neighbors,counts[0],profile,dt,new ShortTurnOnlyClearance(speed*dt),new NavPoint(-10,0));
            Assert.False(proposal.Blocked,"A refused longer turn forecast cannot erase an available physical dt endpoint.");Assert.AreEqual(default(NavPoint),proposal.Position);Assert.LessOrEqual(Math.Abs(NavCrowdMotion.Angle(proposal.Heading-bodies[0].Heading)),bodies[0].TurnSpeed*dt);
        }
        sealed class OpenFrontierClearance:INavMotionClearance {public bool ClearMotion(int actor,NavPoint end)=>true;}
        [Test] public void ActualReachedFrontierPeerIsStationaryBeforeSelectWhileRearActorRemainsActive()
        {
            var profile=PlayableProfile.RecastGroupCorridorDefault.Navigation;var layout=new RecastFormationGuide.Layout(new NavPoint(8,38),new NavPoint(-8,38),new NavPoint(-1,0),16,-4.42,4.42,3.6,.58);
            var peer=new NavPoint(11.600078891090615,34.3387591068665);var own=new NavPoint(12.039425859906714,33.26200717518965);
            var stopped=RecastFormationGuide.BankApproach(new NavLocation(peer,"bank/east"),"bank/east",1,layout,-3.6,9,9,4,profile.ArrivalTolerance);var rear=RecastFormationGuide.BankApproach(new NavLocation(own,"bank/east"),"bank/east",1,layout,-3.6,9,9,4,profile.ArrivalTolerance);
            Assert.AreEqual(NavFormationFrameHint.Wait,stopped.Flags);Assert.AreEqual(NavFormationFrameHint.Bound,rear.Flags,"Actual53 .439m from cap must not become a hidden voluntary wait.");
            var bodies=new[]{new NavMotionBody{Id=53,Position=own,Target=new NavPoint(11.439425859906715,33.06200717518965),Heading=2.7763996929958767,Radius=.58,Speed=1.8632401639911291,TurnSpeed=7.5,OwnPaceCap=7,Active=(rear.Flags&NavFormationFrameHint.Wait)==0},new NavMotionBody{Id=25,Position=peer,Target=peer,Radius=.58,Speed=7,Active=(stopped.Flags&NavFormationFrameHint.Wait)==0}};
            var neighbors=new int[2*NavCrowdMotion.NeighborCapacity];neighbors[0]=1;var q=NavCrowdMotion.Select(bodies,0,neighbors,1,profile,1d/30,new OpenFrontierClearance(),new NavPoint(-37.6,29.4));
            Assert.GreaterOrEqual(NavCrowdMotion.ClosestSquared(new NavPoint(own.X-peer.X,own.Z-peer.Z),q.Velocity,1d/30),Math.Pow(1.16,2)-1e-9,"Select must see the honest reached-cap stationary peer before final Joint.");Assert.True(bodies[0].Active);Assert.False(bodies[1].Active);
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
