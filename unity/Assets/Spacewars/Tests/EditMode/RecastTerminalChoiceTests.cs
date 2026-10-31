using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class RecastTerminalChoiceTests
    {
        // Recorded87c121624 final poses/headings/assigned goals only. Rebuilt
        // map-static geometry, one remaining endpoint and no runtime group or
        // producer are explicit reconstruction; this is not a whole match save.
        private static NavCrowd Reconstructed()
        {
            var map=ThreeCrossingsMap.Default;var p=PlayableProfile.RecastGroupCorridorDefault;
            var crowd=new NavCrowd(new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),p.Navigation,map);
            var rows=new[]{
                new double[]{5,-46.182040674533887,41.949739218928201,0.66980605292331663,-46,46},
                new double[]{6,-44.49428022947847,42.573483635683601,0.66980605292331663,-49.600000000000001,42.399999999999999},
                new double[]{7,-45.739336063512212,43.335525822254219,-0.83019394707668348,-46,42.399999999999999},
                new double[]{8,-43.619383210275153,43.733833100136451,-0.83019394707668348,-42.399999999999999,42.399999999999999},
                new double[]{9,-46.074038704283623,44.86567159355188,-2.3301939470766828,-49.600000000000001,46},
                new double[]{10,-44.355613518069966,41.136497274126043,0.33481770094555963,-42.399999999999999,46},
                new double[]{11,-41.067866998521986,45.003029721324708,-2.3301939470766828,-49.600000000000001,49.600000000000001},
                new double[]{12,-39.493592899078962,45.106652738199685,-2.3301939470766828,-46,49.600000000000001},
                new double[]{13,-37.966477666923637,45.325695499171431,-2.3301939470766828,-42.399999999999999,49.600000000000001},
                new double[]{14,-42.233532403637014,44.152632431985374,-2.3301939470766828,-53.200000000000003,38.799999999999997},
            };
            foreach(var r in rows){int id=(int)r[0];var position=new NavPoint(r[1],r[2]);var goal=new NavPoint(r[4],r[5]);var a=crowd.Add(id,position,.72,4,4.5,new NavLocation(position,"deck/-1/1"),true);a.Team=1;a.Heading=r[3];Assert.True(crowd.SetRoute(id,goal,new[]{goal},new NavLocation(goal,"deck/-1/1")));a.routeIndex=1;}
            return crowd;
        }
        private static double Distance(NavPoint a,NavPoint b)=>NavCrowdMotion.Length(new NavPoint(a.X-b.X,a.Z-b.Z));
        [Test] public void LaterInitiatorCannotOverwriteAnEarlierCertifiedIndependentTurn()
        {
            var crowd=Reconstructed();var start=crowd.Units.Select(a=>a.Position).ToArray();double previous=crowd.Units[0].Heading;
            using(var ws=new NavMotionWorkspace(crowd,crowd.Units.Count,crowd.MotionMembership)){
                for(int i=0;i<8;i++){
                    var before=crowd.Units.Select(a=>a.Position).ToArray();crowd.Step(1d/30,ws);var a=crowd.Units[0];
                    Assert.Greater(a.Heading,previous,"Recorded baseline alternates .819806/.669806 because a later queue commandeers this own turn.");Assert.False(ws.Motion[0].Yielding);Assert.AreEqual(a.Goal,ws.Motion[0].Target);previous=a.Heading;
                    for(int n=0;n<crowd.Units.Count;n++){var b=crowd.Units[n];Assert.LessOrEqual(Distance(before[n],b.Position),b.Speed/30+1e-12);Assert.True(crowd.Geometry.SegmentFree(before[n],b.Position,b.Radius));for(int m=n+1;m<crowd.Units.Count;m++)Assert.GreaterOrEqual(NavCrowdMotion.ClosestSquared(new NavPoint(before[n].X-before[m].X,before[n].Z-before[m].Z),new NavPoint((b.Position.X-before[n].X-crowd.Units[m].Position.X+before[m].X)*30,(b.Position.Z-before[n].Z-crowd.Units[m].Position.Z+before[m].Z)*30),1d/30),Math.Pow(b.Radius+crowd.Units[m].Radius,2)-1e-9);}
                }
                for(int i=0;i<52;i++)crowd.Step(1d/30,ws);
                Assert.True(crowd.Units.Select((a,i)=>!a.Position.Equals(start[i])).Any(m=>m),"The reconstructed stationary cohort must obtain real safe translation, not just new headings.");
            }
        }
        [Test] public void ProtectedChoiceIsDerivedAndCurrentStateRestoreHasExactNextStep()
        {
            var crowd=Reconstructed();for(int i=0;i<8;i++)crowd.Step(1d/30);
            var state=crowd.CaptureState();var copy=new NavCrowd(crowd.Geometry,crowd.Profile,ThreeCrossingsMap.Default);copy.RestoreState(state);
            Assert.AreEqual(Newtonsoft.Json.JsonConvert.SerializeObject(state),Newtonsoft.Json.JsonConvert.SerializeObject(copy.CaptureState()));
            crowd.Step(1d/30);copy.Step(1d/30);
            Assert.AreEqual(Newtonsoft.Json.JsonConvert.SerializeObject(crowd.CaptureState()),Newtonsoft.Json.JsonConvert.SerializeObject(copy.CaptureState()),"Queue protection is recomputed from current source choices; no unsaved arbitration history.");
        }
        [Test] public void RemainingSixObtainIndependentTranslationWithoutDisplacingFourArrivedSeeds()
        {
            // Actual7d final poses/headings/goals/outcomes. Static geometry,
            // single-endpoint routes and derived terminal guards are rebuilt;
            // runtime group/Recast filter and remote actors are not reconstructed.
            var map=ThreeCrossingsMap.Default;var p=PlayableProfile.RecastGroupCorridorDefault;
            var session=new NavigationSession(801,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),p.Navigation,null,map);
            var rows=new[]{
                new double[]{5,-50.025867924789097,43.132298320043546,1.982273486464694,-46,46,1},
                new double[]{6,-48.453389830891659,43.837493520107003,-2.2009118207148934,-49.600000000000001,42.399999999999999,1},
                new double[]{7,-49.768004080424589,44.550108636788337,-1.9009118207148936,-46,42.399999999999999,1},
                new double[]{8,-47.584045222121773,45.07569293306959,2.2822734864646939,-42.399999999999999,42.399999999999999,1},
                new double[]{9,-49.507046797148902,46.003783501084037,-3.1009118207148929,-49.600000000000001,46,2},
                new double[]{10,-48.549352068881525,42.363793412948965,-1.9009118207148936,-42.399999999999999,46,1},
                new double[]{11,-49.49911775374305,49.57467165978457,2.895608727327097,-49.600000000000001,49.600000000000001,2},
                new double[]{12,-45.917108518480333,49.546217843991847,2.5660431186515047,-46,49.600000000000001,2},
                new double[]{13,-42.331338994471295,49.534985158997003,2.3834638944119422,-42.399999999999999,49.600000000000001,2},
                new double[]{14,-46.203076622700991,45.544250078736908,3.0273081416121514,-53.200000000000003,38.799999999999997,1},
            };
            try{
                foreach(var r in rows){var pos=new NavPoint(r[1],r[2]);var goal=new NavPoint(r[4],r[5]);var a=session.Crowd.Add((int)r[0],pos,.72,4,4.5,new NavLocation(pos,"deck/-1/1"),true);a.Team=1;a.Heading=r[3];Assert.True(session.Crowd.SetRoute(a.Id,goal,new[]{goal},new NavLocation(goal,"deck/-1/1")));a.Moving=(int)r[6]==1;a.Outcome=(NavigationOutcome)(int)r[6];a.routeIndex=a.Moving?1:2;}
                Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){
                    var frame=NavPreparedCrowdStep.Create(session.Crowd,10,scope.OwnedReservation());try{
                        frame.Observe();var starts=frame.After.Select(a=>a.Position).ToArray();var originallyMoving=frame.After.Select(a=>a.Moving).ToArray();var moved=new bool[10];
                        for(int tick=0;tick<60;tick++){
                            var before=frame.After.Select(a=>a.Position).ToArray();for(int i=0;i<10;i++){frame.Preferred[i]=frame.RecastReceiver[i]=4;frame.TerminalSeeds[i]=frame.After[i].Outcome==NavigationOutcome.Arrived;}
                            frame.Compute(1d/30);
                            for(int i=0;i<10;i++){var a=frame.After[i];moved[i]|=!a.Position.Equals(starts[i]);if(!originallyMoving[i]){Assert.AreEqual(starts[i],a.Position);Assert.AreEqual(NavigationOutcome.Arrived,a.Outcome);}Assert.LessOrEqual(Distance(before[i],a.Position),a.Speed/30+1e-12);Assert.True(session.Crowd.Geometry.SegmentFree(before[i],a.Position,a.Radius));Assert.True(map.TryTraverse(new NavLocation(before[i],"deck/-1/1"),a.Position,a.Radius,out var end));Assert.AreEqual("deck/-1/1",end.SurfaceId);
                                for(int k=i+1;k<10;k++){var b=frame.After[k];var relative=new NavPoint((a.Position.X-before[i].X-b.Position.X+before[k].X)*30,(a.Position.Z-before[i].Z-b.Position.Z+before[k].Z)*30);Assert.GreaterOrEqual(NavCrowdMotion.ClosestSquared(new NavPoint(before[i].X-before[k].X,before[i].Z-before[k].Z),relative,1d/30),Math.Pow(a.Radius+b.Radius,2)-1e-9);}
                            }
                        }
                        for(int i=0;i<10;i++)if(originallyMoving[i])Assert.True(moved[i],"Each of the recorded six must obtain real safe translation, not merely heading changes.");
                    }finally{frame.Release();}scope.Discard();
                }
            }finally{session.ReleaseCommittedHostRoot();}
        }
    }
}
