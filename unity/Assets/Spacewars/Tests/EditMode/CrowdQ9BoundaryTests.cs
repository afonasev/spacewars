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
            for(int i=0;i<bodies.Length;i++)if(bodies[i].Active)Assert.LessOrEqual(bodies[i].Speed,original[i],"effective speed cannot exceed original");
            return counts;
        }
        private static void AssertActiveExactCounts(NavMotionBody[] bodies,int[] counts,NavigationProfile profile,double dt)
        {
            foreach(int row in Enumerable.Range(0,bodies.Length).Where(i=>bodies[i].Active)){
                int exact=ExactCount(bodies,row,profile,dt);
                if(exact>profile.CrowdNeighborCap)Assert.AreEqual(-1,counts[row],"all-pair exact row exceeds cap "+bodies[row].Id);
                else Assert.AreEqual(exact,counts[row],"independent all-pair exact recount "+bodies[row].Id);
            }
        }
        private static int ExactCount(NavMotionBody[] bodies,int row,NavigationProfile profile,double dt)
        {
            var a=bodies[row];int count=0;double horizon=Math.Max(dt,profile.CrowdLookaheadSeconds);
            for(int peer=0;peer<bodies.Length;peer++)if(peer!=row){
                var b=bodies[peer];double reach=a.Radius+b.Radius+((a.Active?a.Speed:0)+(b.Active?b.Speed:0))*horizon;
                double x=a.Position.X-b.Position.X,z=a.Position.Z-b.Position.Z;
                if(x*x+z*z<=reach*reach)count++;
            }
            return count;
        }
        private static double PairBoundary(NavMotionBody a,NavMotionBody b,NavigationProfile profile)
        {
            double dx=a.Position.X-b.Position.X,dz=a.Position.Z-b.Position.Z;
            double speed=(a.Active?a.Speed:0)+(b.Active?b.Speed:0);
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
