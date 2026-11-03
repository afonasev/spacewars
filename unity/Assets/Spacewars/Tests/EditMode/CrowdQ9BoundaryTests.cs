using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CrowdQ9BoundaryTests
    {
        private const double Dt=1d/30d;
        private static NavigationProfile Profile(int cap)
        {
            var p=NavigationProfile.Default;
            return new NavigationProfile(p.ProfileId,p.Revision,p.Radius,p.TankSpeed,p.TankTurnSpeed,p.GridCell,p.ArrivalTolerance,p.LocalDetourDistance,p.ForwardAlignmentAngle,p.CameraOrthoSize,p.CameraHeight,p.CameraOffsetZ,p.RepairRetrySeconds,p.ArrivalSlotSpacing,p.BlockedTimeoutSeconds,p.LocalGridCell,p.CrowdHeadingIntervals,p.CrowdSpeedBands,p.CrowdLookaheadSeconds,cap);
        }
        private static int[] Constrain(NavMotionBody[] bodies,NavigationProfile profile,out int[] initial)
        {
            var neighbors=new int[bodies.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[bodies.Length];var labels=new int[bodies.Length];var original=new double[bodies.Length];
            NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,Dt,labels);initial=(int[])counts.Clone();
            NavCrowdMotion.ConstrainOverflowComponents(bodies,neighbors,counts,labels,original,profile,Dt);
            AssertActiveExactCounts(bodies,counts,profile,Dt);
            for(int i=0;i<bodies.Length;i++)if(bodies[i].Active||bodies[i].Potential)Assert.LessOrEqual(bodies[i].Speed,original[i],"effective speed cannot exceed original");
            return counts;
        }
        private static void AssertActiveExactCounts(NavMotionBody[] bodies,int[] counts,NavigationProfile profile,double dt)
        {
            foreach(int row in Enumerable.Range(0,bodies.Length).Where(i=>bodies[i].Active||bodies[i].Potential)){
                int exact=ExactCount(bodies,row,profile,dt);
                if(exact>profile.CrowdNeighborCap)Assert.AreEqual(-1,counts[row],"all-pair exact row exceeds cap "+bodies[row].Id);
                else Assert.AreEqual(exact,counts[row],"independent all-pair exact recount "+bodies[row].Id);
            }
        }
        private static int ExactCount(NavMotionBody[] bodies,int row,NavigationProfile profile,double dt)
        {
            var a=bodies[row];int count=0;double horizon=Math.Max(dt,profile.CrowdLookaheadSeconds);
            for(int peer=0;peer<bodies.Length;peer++)if(peer!=row){
                var b=bodies[peer];double reach=a.Radius+b.Radius+((a.Active||a.Potential?a.Speed:0)+(b.Active||b.Potential?b.Speed:0))*horizon;
                double x=a.Position.X-b.Position.X,z=a.Position.Z-b.Position.Z;
                if(x*x+z*z<=reach*reach)count++;
            }
            return count;
        }
        private static double PairBoundary(NavMotionBody a,NavMotionBody b,NavigationProfile profile)
        {
            double dx=a.Position.X-b.Position.X,dz=a.Position.Z-b.Position.Z;
            double speed=(a.Active||a.Potential?a.Speed:0)+(b.Active||b.Potential?b.Speed:0);
            return (Math.Sqrt(dx*dx+dz*dz)-(a.Radius+b.Radius))/(speed*Math.Max(Dt,profile.CrowdLookaheadSeconds));
        }
        private static NavMotionBody[] TiedCapOneBodies(bool reverse)
        {
            var bodies=new[]{
                new NavMotionBody{Id=1,Position=default,Target=new NavPoint(20,0),Radius=.72,Speed=4,TurnSpeed=4.5,Active=true},
                new NavMotionBody{Id=9,Position=new NavPoint(2,0),Radius=.72,Speed=17,Active=false},
                new NavMotionBody{Id=3,Position=new NavPoint(-2,0),Radius=.72,Speed=17,Active=false},
                new NavMotionBody{Id=5,Position=new NavPoint(4,0),Radius=.72,Speed=17,Active=false}
            };
            if(reverse)Array.Reverse(bodies);return bodies;
        }
        [TestCase(false)] [TestCase(true)] public void SafeOriginalSpeedReturnsToRemoteActorAndPotentialWithoutExceedingAnyExactRow(bool potential)
        {
            var profile=Profile(1);var forward=new[]{
                new NavMotionBody{Id=1,Position=default,Radius=.72,Speed=4,Active=true},
                new NavMotionBody{Id=2,Position=new NavPoint(10,0),Radius=.72,Speed=4,Active=!potential,Potential=potential},
                new NavMotionBody{Id=3,Position=new NavPoint(2,0),Radius=.72,Speed=99},
                new NavMotionBody{Id=4,Position=new NavPoint(-2.5,0),Radius=.72,Speed=99}
            };var reverse=forward.Reverse().ToArray();
            var counts=Constrain(forward,profile,out var initial);var reverseCounts=Constrain(reverse,profile,out _);
            Assert.AreEqual(-1,initial[0]);Assert.Less(forward[0].Speed,4,"Full restore into the two hazards must remain refused");Assert.Greater(forward[0].Speed,0);
            Assert.AreEqual(4,forward[1].Speed,"Remote accepted actor/receiver regains its complete original cap");
            Assert.True(counts.Where((c,i)=>forward[i].Active||forward[i].Potential).All(c=>c>=0&&c<=profile.CrowdNeighborCap));
            foreach(var body in forward){int twin=Array.FindIndex(reverse,b=>b.Id==body.Id);Assert.AreEqual(body.Speed,reverse[twin].Speed,"Entity ordering is stable across reversed arrays");Assert.AreEqual(counts[Array.FindIndex(forward,b=>b.Id==body.Id)],reverseCounts[twin]);}
            Assert.AreEqual(99,forward[2].Speed);Assert.AreEqual(99,forward[3].Speed);
        }
        [Test] public void ExistingIntermediateBandRecoversCapacityWhenFullSpeedHasTooManyHazards()
        {
            var profile=Profile(1);var bodies=new[]{
                new NavMotionBody{Id=1,Position=default,Radius=.72,Speed=4,Active=true},
                new NavMotionBody{Id=2,Position=new NavPoint(10,0),Radius=.72,Speed=4,Active=true},
                new NavMotionBody{Id=3,Position=new NavPoint(2,0),Radius=.72,Speed=99},
                new NavMotionBody{Id=4,Position=new NavPoint(-2.5,0),Radius=.72,Speed=99},
                new NavMotionBody{Id=5,Position=new NavPoint(15,0),Radius=.72,Speed=99},
                new NavMotionBody{Id=6,Position=new NavPoint(15.5,0),Radius=.72,Speed=99}
            };var counts=Constrain(bodies,profile,out var initial);
            Assert.AreEqual(-1,initial[0]);Assert.AreEqual(-1,initial[1]);Assert.Less(bodies[0].Speed,2);
            double priorBandFloor=4d*(profile.CrowdSpeedBands-1)/profile.CrowdSpeedBands;Assert.AreEqual(2,priorBandFloor,"The prior existing half-speed band remains the recorded floor");
            var floorTrial=(NavMotionBody[])bodies.Clone();floorTrial[1].Speed=priorBandFloor;Assert.LessOrEqual(ExactCount(floorTrial,1,profile,Dt),profile.CrowdNeighborCap,"The prior band is independently safe");
            Assert.Greater(bodies[1].Speed,priorBandFloor,"Certified incident headroom must improve the old entire band result");Assert.Less(bodies[1].Speed,4);
            var critical=bodies[5];double dx=bodies[1].Position.X-critical.Position.X,dz=bodies[1].Position.Z-critical.Position.Z;
            double ceiling=(Math.Sqrt(dx*dx+dz*dz)-(bodies[1].Radius+critical.Radius))/Math.Max(Dt,profile.CrowdLookaheadSeconds);
            Assert.LessOrEqual(bodies[1].Speed,ceiling);Assert.Less(ceiling-bodies[1].Speed,1e-12,"Only directed-rounding slack below the independently excluded critical hazard");
            var original=bodies[1];original.Speed=4;var trial=(NavMotionBody[])bodies.Clone();trial[1]=original;Assert.Greater(ExactCount(trial,1,profile,Dt),profile.CrowdNeighborCap);
            Assert.That(counts[1],Is.InRange(0,profile.CrowdNeighborCap));Assert.AreEqual(99,bodies[4].Speed);Assert.AreEqual(99,bodies[5].Speed);
        }
        [Test] public void CapOneUsesIndependentSecondBoundaryAndStableIdTiesAcrossReverseOrder()
        {
            var profile=Profile(1);var forward=TiedCapOneBodies(false);var reverse=TiedCapOneBodies(true);
            int sourceRow=Array.FindIndex(forward,b=>b.Id==1);var sorted=forward.Where(b=>b.Id!=1).Select(b=>new{b.Id,Boundary=PairBoundary(forward[sourceRow],b,profile)}).OrderBy(x=>x.Boundary).ThenBy(x=>x.Id).ToArray();
            var forwardCounts=Constrain(forward,profile,out var forwardInitial);var reverseCounts=Constrain(reverse,profile,out var reverseInitial);
            int forwardRow=Array.FindIndex(forward,b=>b.Id==1),reverseRow=Array.FindIndex(reverse,b=>b.Id==1);
            Assert.AreEqual(-1,forwardInitial[forwardRow]);Assert.AreEqual(-1,reverseInitial[reverseRow]);
            double expected=sorted[profile.CrowdNeighborCap].Boundary,actual=forward[forwardRow].Speed/4d;
            Assert.Greater(actual,0);Assert.LessOrEqual(actual,expected,"ratio is bounded by independently sorted exact pair boundaries");Assert.Less(expected-actual,1e-12,"only directed-rounding slack separates the independent boundary");
            Assert.That(forwardCounts[forwardRow],Is.InRange(0,profile.CrowdNeighborCap));Assert.That(reverseCounts[reverseRow],Is.InRange(0,profile.CrowdNeighborCap));
            foreach(var body in forward.Where(b=>b.Id!=1)){var twin=reverse.Single(b=>b.Id==body.Id);Assert.AreEqual(body.Speed,twin.Speed,"inactive hazards are unchanged independent of array order");}
            Assert.AreEqual(forward[forwardRow].Speed,reverse[reverseRow].Speed,"equal boundaries and stable entity-id ties produce identical q");
            Assert.AreEqual(forwardCounts[forwardRow],reverseCounts[reverseRow]);AssertActiveExactCounts(reverse,reverseCounts,profile,Dt);
        }
        [Test] public void CapacityThirtyTwoStoresOnlyThirtyTwoPeersAndKeepsTheThirtyThirdThresholdLocal()
        {
            var profile=Profile(32);var bodies=new NavMotionBody[34];bodies[0]=new NavMotionBody{Id=1,Position=default,Radius=.72,Speed=4,Target=new NavPoint(20,0),Active=true};
            for(int i=0;i<33;i++)bodies[i+1]=new NavMotionBody{Id=100+i,Position=new NavPoint(1.5+.14*i,0),Radius=.72,Speed=9,Active=false};
            var counts=Constrain(bodies,profile,out var initial);
            Assert.AreEqual(-1,initial[0]);Assert.AreEqual(32,counts[0],"all 33 original peers are considered; exactly one is excluded at the selected boundary");
            Assert.Greater(bodies[0].Speed,0);Assert.LessOrEqual(bodies[0].Speed,4);
            Assert.AreEqual(ExactCount(bodies,0,profile,Dt),counts[0]);
            for(int i=1;i<bodies.Length;i++)Assert.AreEqual(9d,bodies[i].Speed,"inactive hazards never receive the component scale");
        }
        [Test] public void TouchingZeroSpeedPeerIsPermanentAndConsumesOneCapacitySlot()
        {
            var profile=Profile(1);var bodies=new[]{
                new NavMotionBody{Id=1,Position=default,Radius=.72,Speed=4,Target=new NavPoint(10,0),Active=true},
                new NavMotionBody{Id=2,Position=new NavPoint(1.44,0),Radius=.72,Speed=99,Active=false},
                new NavMotionBody{Id=3,Position=new NavPoint(3,0),Radius=.72,Speed=99,Active=false}
            };
            var counts=Constrain(bodies,profile,out var initial);Assert.AreEqual(-1,initial[0]);Assert.AreEqual(1,counts[0]);Assert.Greater(bodies[0].Speed,0);Assert.Less(bodies[0].Speed,4);
            Assert.AreEqual(1,ExactCount(bodies,0,profile,Dt),"inclusive touching peer remains a permanent physical hazard at q=0");
        }
        [Test] public void SubUlpPairGapUsesVerifiedZeroBoundaryWithoutInventingARelevantPeer()
        {
            var profile=Profile(1);double touching=.72+.72,justOutside=BitIncrement(touching);
            var bodies=new[]{
                new NavMotionBody{Id=1,Position=default,Radius=.72,Speed=4,Target=new NavPoint(10,0),Active=true},
                new NavMotionBody{Id=2,Position=new NavPoint(justOutside,0),Radius=.72,Active=false},
                new NavMotionBody{Id=3,Position=new NavPoint(-justOutside,0),Radius=.72,Active=false}
            };
            var counts=Constrain(bodies,profile,out var initial);Assert.AreEqual(-1,initial[0]);Assert.AreEqual(0,bodies[0].Speed,"directed lower bound may conservatively reach exact q=0");Assert.AreEqual(0,counts[0]);Assert.AreEqual(0,ExactCount(bodies,0,profile,Dt));
        }
        [Test] public void NonFiniteOriginalSpeedRefusesReductionAndPreservesStationaryOverflowFallback()
        {
            var profile=Profile(8);var bodies=new NavMotionBody[10];bodies[0]=new NavMotionBody{Id=1,Position=default,Radius=.72,Speed=double.PositiveInfinity,Target=new NavPoint(10,0),Active=true};
            for(int i=1;i<bodies.Length;i++)bodies[i]=new NavMotionBody{Id=i+1,Position=new NavPoint(i*.2,0),Radius=.72,Speed=4,Active=false};
            var counts=Constrain(bodies,profile,out var initial);Assert.AreEqual(-1,initial[0]);Assert.AreEqual(double.PositiveInfinity,bodies[0].Speed);Assert.AreEqual(-1,counts[0]);
            var selected=NavCrowdMotion.Select(bodies,0,new int[bodies.Length*NavCrowdMotion.NeighborCapacity],-1,profile,Dt,new NeverClearance());Assert.True(selected.Overflow);Assert.AreEqual(bodies[0].Position,selected.Position);
        }
        [Test] public void OvercrowdedCurrentSaveRestoresColdAndMatchesWarmMovementCaptures()
        {
            const int units=12;var geometry=new NavGeometry(100,Array.Empty<NavObstacle>(),1);var profile=NavigationProfile.Default;var warm=new NavigationSession(71,geometry,profile);NavigationSession cold=null;
            try{
                for(int i=0;i<units;i++){double angle=2*Math.PI*i/units;int id=i+1;var point=new NavPoint(3.6*Math.Cos(angle),3.6*Math.Sin(angle));warm.Crowd.Add(id,point);var goal=new NavPoint(point.X+20,point.Z);Assert.True(warm.Crowd.SetRoute(id,goal,new[]{goal}));}
                var initial=warm.Crowd.Units.Select(u=>new NavMotionBody{Id=u.Id,Position=u.Position,Target=u.Goal,Heading=u.Heading,Radius=u.Radius,Speed=u.Speed,TurnSpeed=u.TurnSpeed,Active=u.Moving&&!u.Held}).ToArray();var initialNeighbors=new int[units*NavCrowdMotion.NeighborCapacity];var initialCounts=new int[units];NavCrowdMotion.Neighbors(initial,initialNeighbors,initialCounts,profile,Dt);Assert.True(initialCounts.Any(c=>c<0),"the frozen current-save source contains the dense overflow candidate");
                warm.Crowd.Step(Dt);var warmBytes=SessionBytes(warm);cold=new NavigationSession(71,geometry,profile);
                using(var saved=warm.CaptureOwnedState()){cold.RestoreState(saved);}
                CollectionAssert.AreEqual(warmBytes,SessionBytes(cold),"cold restore begins from the exact current paid save");
                long warmPhysical=warm.PhysicalHostBudget.PhysicalBytes,coldPhysical=cold.PhysicalHostBudget.PhysicalBytes;
                for(int step=0;step<12;step++){
                    warm.Crowd.Step(Dt);cold.Crowd.Step(Dt);CollectionAssert.AreEqual(SessionBytes(warm),SessionBytes(cold),"exact current save after warm/cold step "+step);
                    Assert.AreEqual(warmPhysical,warm.PhysicalHostBudget.PhysicalBytes);Assert.AreEqual(coldPhysical,cold.PhysicalHostBudget.PhysicalBytes);
                    Assert.Zero(warm.PhysicalHostBudget.HeldBytes);Assert.Zero(warm.PhysicalHostBudget.ActiveScopes);Assert.Zero(cold.PhysicalHostBudget.HeldBytes);Assert.Zero(cold.PhysicalHostBudget.ActiveScopes);
                }
            }finally{Destroy(cold);Destroy(warm);}
        }
        private static int[] IncidentOnly(NavMotionBody[] bodies,double[] ceilings,NavigationProfile profile){
            var neighbors=new int[bodies.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[bodies.Length];NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,Dt);
            typeof(NavCrowdMotion).GetMethod("ReclaimIncidentCapacity",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{bodies,counts,ceilings,profile,Dt});
            AssertActiveExactCounts(bodies,counts,profile,Dt);return counts;
        }
        [TestCase(false,false)] [TestCase(true,false)] [TestCase(false,true)] [TestCase(true,true)] public void IncidentCapacityRespectsFullActiveOrPotentialPeerAndReversedStorage(bool potential,bool overflow){
            var profile=Profile(1);var bodies=new[]{
                new NavMotionBody{Id=1,Position=default,Radius=.72,Speed=1,Active=true},
                new NavMotionBody{Id=2,Position=new NavPoint(5,0),Radius=.72,Speed=1,Active=!potential,Potential=potential},
                new NavMotionBody{Id=3,Position=new NavPoint(6,0),Radius=.72,Speed=99}
            };if(overflow)bodies=bodies.Concat(new[]{new NavMotionBody{Id=4,Position=new NavPoint(5,1),Radius=.72,Speed=99}}).ToArray();var reverse=bodies.Reverse().ToArray();var counts=IncidentOnly(bodies,bodies.Select(a=>a.Id==1?4d:a.Speed).ToArray(),profile);var reverseCounts=IncidentOnly(reverse,reverse.Select(a=>a.Id==1?4d:a.Speed).ToArray(),profile);
            Assert.Greater(bodies[0].Speed,1);Assert.Less(bodies[0].Speed,2,"full peer limits incoming edge before own available capacity is spent");Assert.AreEqual(0,counts[0]);Assert.AreEqual(overflow?-1:1,counts[1]);Assert.AreEqual(1,bodies[1].Speed);Assert.AreEqual(99,bodies[2].Speed);
            foreach(var a in bodies){int i=Array.FindIndex(reverse,b=>b.Id==a.Id);Assert.AreEqual(a.Speed,reverse[i].Speed);Assert.AreEqual(counts[Array.FindIndex(bodies,b=>b.Id==a.Id)],reverseCounts[i]);}
        }
        [Test] public void IncidentCapacityUsesOwnNextEdgeAndNeverExceedsOriginalCeiling(){
            var profile=Profile(1);var bodies=new[]{new NavMotionBody{Id=1,Radius=.72,Speed=.1,Active=true},new NavMotionBody{Id=2,Position=new NavPoint(2,0),Radius=.72,Speed=99},new NavMotionBody{Id=3,Position=new NavPoint(4,0),Radius=.72,Speed=99}};
            var counts=IncidentOnly(bodies,new[]{4d,99d,99d},profile);Assert.Greater(bodies[0].Speed,2);Assert.Less(bodies[0].Speed,2.14);Assert.AreEqual(1,counts[0]);Assert.AreEqual(99,bodies[1].Speed);Assert.AreEqual(99,bodies[2].Speed);
            var safe=new[]{new NavMotionBody{Id=1,Radius=.72,Speed=.1,Active=true},new NavMotionBody{Id=2,Position=new NavPoint(100,0),Radius=.72,Speed=99}};IncidentOnly(safe,new[]{4d,99d},profile);Assert.AreEqual(4,safe[0].Speed);
        }
        private static byte[] SessionBytes(NavigationSession session)
        {using(var state=session.CaptureOwnedState())return WorldWire.Pack(writer=>WorldWire.Write(writer,state));}
        private static void Destroy(NavigationSession session)
        {if(session==null)return;session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);}
        private static double BitIncrement(double value)
        {long bits=BitConverter.DoubleToInt64Bits(value);return BitConverter.Int64BitsToDouble(bits+1);}
        private sealed class NeverClearance:INavMotionClearance
        {public bool ClearMotion(int actor,NavPoint end)=>false;}
    }
}
