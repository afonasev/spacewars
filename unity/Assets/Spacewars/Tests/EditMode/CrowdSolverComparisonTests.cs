using System;
using System.Globalization;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    // B0 experiment only: neither strategy is selected by a runtime switch.
    // All inputs, reachable candidates and guards are identical. ORCA constraints
    // are tested INSIDE the reachable set; no unconstrained output is clamped.
    public sealed class CrowdSolverComparisonTests
    {
        struct V {
            public double X,Z; public V(double x,double z){X=x;Z=z;}
            public static V operator +(V a,V b)=>new V(a.X+b.X,a.Z+b.Z);
            public static V operator -(V a,V b)=>new V(a.X-b.X,a.Z-b.Z);
            public static V operator *(V a,double n)=>new V(a.X*n,a.Z*n);
            public double Length=>Math.Sqrt(Dot(this,this));
        }
        struct Actor { public int Id;public V P,Goal,Velocity;public double Heading;public bool Fixed,Enemy; }
        struct Line { public V Point,Direction; }
        struct HalfPlane { public V Normal;public double Bound; }
        struct SolveWork { public long LinePairs,LineCircleHits,CandidateChecks,FeasibilityChecks,PlaneTests; }
        struct CpuBatch { public long Ticks,Bytes,NeighborReferences,NeighborOverflowRows,Candidates,TerrainChecks,TerrainRejects,Legal,Rejected,LineBuilds,LinePairs,LineCircleHits,LpCandidates,LpFeasibilityCalls,LpPlaneTests,LpInfeasible,FullDiskInfeasible,TurnInPlace,Blocked,StaticSweepChecks,JointRefusals,PairSweepChecks;public ulong Checksum;public bool Safe; }
        sealed class CpuSnapshot { public int Kind,Tick,Mirror,Reverse;public string Reason;public Actor[] Actors;public ulong Checksum;public int ActiveNeighborRows,MaxNeighborCount; }
        sealed class CpuWorkspace { public Actor[] Actors;public NavMotionBody[] Bodies;public NavMotionProposal[] Proposals;public int[] Neighbors,Counts;public V[] SelectedVelocity;public FiniteResult Result;public GeometryClearance Clearance;public NavigationProfile Profile; }
        sealed class FiniteResult {
            public long Candidates,Constraints,Guards,Legal,Rejected,NoProgress,Turns,Arrivals,JointRefusals,Overflows,TerrainChecks,TerrainRejects,LineBuilds,LinePairs,LineCircleHits,LpCandidateChecks,LpFeasibilityCalls,LpPlaneTests,LpInfeasible,FullDiskInfeasible,TurnInPlace,BlockedProposals,NeighborReferences,NeighborOverflowRows,StaticSweepChecks;
            public double Progress;public double[] PerActorProgress;public bool[] PerActorArrived;public int[] PerActorOverflow;public string ActorReport;
            public string Summary=>$"terrainClearanceCalls={TerrainChecks},terrainRejects={TerrainRejects},progress={Progress:R},arrivals={Arrivals},wait={NoProgress},turns={Turns},turnInPlace={TurnInPlace},clearanceCandidateCalls={Candidates},clearanceAccepted={Legal},clearanceRejected={Rejected},neighborReferences={NeighborReferences},neighborOverflowRows={NeighborOverflowRows},orcaHalfPlanesBuilt={LineBuilds},lpLinePairs={LinePairs},lpLineCircleCandidates={LineCircleHits},lpCandidates={LpCandidateChecks},lpFeasibilityCalls={LpFeasibilityCalls},lpPlaneTests={LpPlaneTests},reachableLpInfeasible={LpInfeasible},fullDiskLpInfeasible={FullDiskInfeasible},blocked={BlockedProposals},staticFutureSweepChecks={StaticSweepChecks},jointRefusals={JointRefusals},sweptGuards={Guards},overflow={Overflows},repairs=0";
        }
        sealed class Result {
            public long Candidates,Constraints,Guards,Legal,Rejected,NoProgress,Turns,Arrivals,JointRejects,Allocated,Overflows,StationaryWithFeasibleWitnesses;
            public double Progress,Cpu;public double[] PerActorProgress;public bool[] PerActorArrived;public string ActorReport,FirstFeasibleWitness;public int[] PerActorOverflow;
            public override string ToString()=>$"legal={Legal},rejected={Rejected},progress={Progress:R},arrivals={Arrivals},wait={NoProgress},turns={Turns},candidates={Candidates},constraints={Constraints},guards={Guards},jointRejects={JointRejects},overflow={Overflows},analyticStationaryWithFeasibleWitnesses={StationaryWithFeasibleWitnesses},repairs=0,allocated={Allocated},cpuMs={Cpu:R}";
        }
        // Frozen B0 experiment parameters; these are not runtime tuning defaults.
        const double Dt=1d/30,Radius=.72,Speed=4,Turn=4.5,Horizon=1.2;
        const int Ticks=240,Directions=16,SpeedBands=2,FastNeighbors=8;
        const int CpuWarmupRounds=256,CpuMeasuredPasses=128,CpuMeasuredSeries=7,CpuSnapshotCount=16;
        static object AllocationCalibrationEscape;static bool AllocationCounterAvailable;
        static double Dot(V a,V b)=>a.X*b.X+a.Z*b.Z;
        static double Det(V a,V b)=>a.X*b.Z-a.Z*b.X;
        static V Unit(V a)=>a.Length>1e-12?a*(1/a.Length):new V(1,0);
        static double Angle(double a){while(a>Math.PI)a-=2*Math.PI;while(a< -Math.PI)a+=2*Math.PI;return a;}
        static V Velocity(double angle,double speed)=>new V(Math.Cos(angle)*speed,Math.Sin(angle)*speed);
        static double Closest(V relative,V motion,double duration){double q=Dot(motion,motion);double t=q==0?0:Math.Max(0,Math.Min(duration,-Dot(relative,motion)/q));var p=relative+motion*t;return Dot(p,p);}
        static Line Orca(Actor a,Actor b){
            var p=b.P-a.P;var v=a.Velocity-b.Velocity;double d2=Dot(p,p),r=2*Radius,r2=r*r;V direction,u;
            double inv=1/(d2>r2?Horizon:Dt);var w=v-p*inv;double w2=Dot(w,w),dot=Dot(w,p);
            if(d2<=r2||dot<0&&dot*dot>r2*w2){var n=Unit(w);direction=new V(n.Z,-n.X);u=n*(r*inv-Math.Sqrt(w2));}
            else {double leg=Math.Sqrt(d2-r2);direction=Det(p,w)>0?new V(p.X*leg-p.Z*r,p.X*r+p.Z*leg)*(1/d2):new V(-p.X*leg-p.Z*r,p.X*r-p.Z*leg)*(1/d2);u=direction*Dot(v,direction)-v;}
            return new Line{Point=a.Velocity+u*(b.Fixed||b.Enemy?1:.5),Direction=direction};
        }

        static HalfPlane Plane(V normal,double bound)=>new HalfPlane{Normal=normal,Bound=bound};
        static bool Satisfies(V value,HalfPlane[] planes,int count,double radius,ref SolveWork work){
            work.FeasibilityChecks++;
            if(Dot(value,value)>radius*radius+1e-12)return false;
            for(int i=0;i<count;i++){work.PlaneTests++;if(Dot(planes[i].Normal,value)<planes[i].Bound-1e-10)return false;}
            return true;
        }
        static void Consider(V value,V preferred,HalfPlane[] planes,int count,double radius,ref SolveWork work,ref bool found,ref V best,ref double bestDistance){
            work.CandidateChecks++;if(!Satisfies(value,planes,count,radius,ref work))return;
            double dx=value.X-preferred.X,dz=value.Z-preferred.Z,distance=dx*dx+dz*dz;
            // Stable first-wins ties follow fixed constraint order (actor ID, then boundary pair).
            if(!found||distance<bestDistance-1e-12){found=true;best=value;bestDistance=distance;}
        }
        // Exact candidate enumeration for the closest point in a 2-D convex
        // intersection of half-planes and a speed disc. It is continuous; no
        // heading samples, unconstrained clamp, package, or runtime switch.
        static bool ClosestFeasible(V preferred,HalfPlane[] planes,int count,double radius,ref SolveWork work,out V best){
            best=default;bool found=false;double bestDistance=double.PositiveInfinity;
            Consider(preferred,preferred,planes,count,radius,ref work,ref found,ref best,ref bestDistance);
            Consider(default,preferred,planes,count,radius,ref work,ref found,ref best,ref bestDistance);
            for(int i=0;i<count;i++){
                var p=planes[i];double nn=Dot(p.Normal,p.Normal);if(nn<=1e-24)continue;
                var projected=preferred+p.Normal*((p.Bound-Dot(p.Normal,preferred))/nn);
                Consider(projected,preferred,planes,count,radius,ref work,ref found,ref best,ref bestDistance);
                double baseX=p.Normal.X*(p.Bound/nn),baseZ=p.Normal.Z*(p.Bound/nn),base2=baseX*baseX+baseZ*baseZ;
                if(base2<=radius*radius+1e-8){double offset=Math.Sqrt(Math.Max(0,radius*radius-base2)/nn);var tangent=new V(-p.Normal.Z,p.Normal.X);
                    var c1=new V(baseX+tangent.X*offset,baseZ+tangent.Z*offset);var c2=new V(baseX-tangent.X*offset,baseZ-tangent.Z*offset);
                    work.LineCircleHits+=2;Consider(c1,preferred,planes,count,radius,ref work,ref found,ref best,ref bestDistance);Consider(c2,preferred,planes,count,radius,ref work,ref found,ref best,ref bestDistance);
                }
                for(int j=i+1;j<count;j++){
                    work.LinePairs++;var q=planes[j];double det=Det(p.Normal,q.Normal);if(Math.Abs(det)<=1e-12)continue;
                    var crossing=new V((p.Bound*q.Normal.Z-p.Normal.Z*q.Bound)/det,(p.Normal.X*q.Bound-p.Bound*q.Normal.X)/det);
                    Consider(crossing,preferred,planes,count,radius,ref work,ref found,ref best,ref bestDistance);
                }
            }
            if(radius>0){var length=preferred.Length;if(length>radius+1e-12){var circle=preferred*(radius/length);Consider(circle,preferred,planes,count,radius,ref work,ref found,ref best,ref bestDistance);}}
            return found;
        }
        static void AddOrcaPlanes(Actor[] actors,int row,int[] neighbors,int count,HalfPlane[] planes,ref int planeCount,ref FiniteResult result){
            var ordered=new int[Math.Max(0,count)];for(int i=0;i<count;i++)ordered[i]=neighbors[row*NavCrowdMotion.NeighborCapacity+i];
            for(int i=1;i<ordered.Length;i++){int value=ordered[i],j=i-1;while(j>=0&&actors[ordered[j]].Id>actors[value].Id){ordered[j+1]=ordered[j];j--;}ordered[j+1]=value;}
            for(int i=0;i<ordered.Length;i++){
                var line=ContinuousOrcaLine(actors[row],actors[ordered[i]]);var normal=new V(-line.Direction.Z,line.Direction.X);
                planes[planeCount++]=Plane(normal,Dot(normal,line.Point));result.LineBuilds++;result.Constraints++;
            }
        }
        static bool IsActivePeer(Actor peer)=>(peer.Goal-peer.P).Length>.15&&!peer.Fixed;
        static Line ContinuousOrcaLine(Actor mover,Actor peer){
            // Match NavMotionBody.Active: arrived and fixed peers no longer
            // move, so the adapter assigns them full responsibility.
            if(!IsActivePeer(peer)){peer.Fixed=true;peer.Velocity=default;}
            return Orca(mover,peer);
        }
        static void AddReachableWedge(double heading,double turnLimit,HalfPlane[] planes,ref int count){
            var low=Velocity(Angle(heading-turnLimit),1);var high=Velocity(Angle(heading+turnLimit),1);
            planes[count++]=Plane(new V(-low.Z,low.X),0);
            planes[count++]=Plane(new V(high.Z,-high.X),0);
        }
        sealed class GeometryClearance : INavMotionClearance {
            internal Actor[] Actors;internal NavGeometry Geometry;internal FiniteResult Result;
            public bool ClearMotion(int row,NavPoint end){Result.Candidates++;Result.TerrainChecks++;var a=Actors[row];bool clear=Geometry.SegmentFree(new NavPoint(a.P.X,a.P.Z),end,Radius);if(!clear){Result.TerrainRejects++;Result.Rejected++;}else Result.Legal++;return clear;}
        }
        static bool InactivePeerSweepClear(Actor[] actors,int row,V velocity,double dt,FiniteResult result){
            var a=actors[row];for(int j=0;j<actors.Length;j++)if(j!=row&&!IsActivePeer(actors[j])){result.StaticSweepChecks++;var b=actors[j];if(Closest(a.P-b.P,velocity,dt)<4*Radius*Radius-1e-9)return false;}return true;
        }
        static bool TurnInPlaceFromOrcaAim(Actor[] actors,int row,HalfPlane[] fullPlanes,int fullCount,double maximum,double turnLimit,double dt,GeometryClearance clearance,FiniteResult result,ref NavMotionProposal proposal){
            var work=new SolveWork();bool full=ClosestFeasible(Unit(actors[row].Goal-actors[row].P)*maximum,fullPlanes,fullCount,maximum,ref work,out var aimVelocity);
            result.LpCandidateChecks+=work.CandidateChecks;result.LpFeasibilityCalls+=work.FeasibilityChecks;result.LpPlaneTests+=work.PlaneTests;result.LinePairs+=work.LinePairs;result.LineCircleHits+=work.LineCircleHits;
            if(!full){result.FullDiskInfeasible++;return false;}if(aimVelocity.Length<=1e-9)return false;
            var a=actors[row];var future=new NavPoint(a.P.X+aimVelocity.X*dt,a.P.Z+aimVelocity.Z*dt);
            if(!clearance.ClearMotion(row,future)||!InactivePeerSweepClear(actors,row,aimVelocity,dt,result))return false;
            proposal.Heading=NavCrowdMotion.Turn(a.Heading,Math.Atan2(aimVelocity.Z,aimVelocity.X),turnLimit);result.TurnInPlace++;return true;
        }
        static NavMotionProposal OrcaProposal(Actor[] actors,int row,int[] neighbors,int count,NavigationProfile profile,double dt,GeometryClearance clearance,FiniteResult result){
            var a=actors[row];var proposal=new NavMotionProposal{Position=new NavPoint(a.P.X,a.P.Z),Heading=a.Heading};
            if(a.Fixed||(a.Goal-a.P).Length<=.15)return proposal;
            if(count<0){proposal.Overflow=true;return proposal;}
            double maximum=Math.Min(Speed,(a.Goal-a.P).Length/dt),limit=Turn*dt;
            var desired=Unit(a.Goal-a.P)*maximum;
            var planes=new HalfPlane[count+2];int n=0;AddOrcaPlanes(actors,row,neighbors,count,planes,ref n,ref result);AddReachableWedge(a.Heading,limit,planes,ref n);
            var work=new SolveWork();bool reachable=ClosestFeasible(desired,planes,n,maximum,ref work,out var velocity);
            result.LpCandidateChecks+=work.CandidateChecks;result.LpFeasibilityCalls+=work.FeasibilityChecks;result.LpPlaneTests+=work.PlaneTests;result.LinePairs+=work.LinePairs;result.LineCircleHits+=work.LineCircleHits;
            bool futureAvailable=false;
            if(reachable&&velocity.Length>1e-9){
                var end=new NavPoint(a.P.X+velocity.X*dt,a.P.Z+velocity.Z*dt);
                futureAvailable=clearance.ClearMotion(row,end)&&InactivePeerSweepClear(actors,row,velocity,dt,result);
                if(futureAvailable){proposal.Position=end;proposal.Velocity=new NavPoint(velocity.X,velocity.Z);proposal.Heading=Angle(Math.Atan2(velocity.Z,velocity.X));}
            }
            // A zero translation or an empty reachable-cone intersection may
            // still yield a safe turn-in-place. Its aim comes from a separate
            // ORCA solve over the speed disc, never from clamping that velocity
            // into the unreachable cone and never by turning toward goal by default.
            if(!futureAvailable){
                var fullPlanes=new HalfPlane[count];int fullCount=0;AddOrcaPlanes(actors,row,neighbors,count,fullPlanes,ref fullCount,ref result);
                futureAvailable=TurnInPlaceFromOrcaAim(actors,row,fullPlanes,fullCount,maximum,limit,dt,clearance,result,ref proposal);
            }
            if(!reachable)result.LpInfeasible++;
            proposal.Blocked=!futureAvailable;
            bool activeNeighbor=false;for(int k=0;k<count;k++)if(IsActivePeer(actors[neighbors[row*NavCrowdMotion.NeighborCapacity+k]])){activeNeighbor=true;break;}
            proposal.StaticNoWinner=reachable&&!futureAvailable&&!activeNeighbor;
            return proposal;
        }
        static FiniteResult RunFinite(int kind,bool mirror,bool reverse,bool orca,int runTicks=Ticks,int captureTick=-1,Action<Actor[]> capture=null){
            var actors=Input(kind,mirror,reverse);for(int i=0;i<actors.Length;i++)for(int j=i+1;j<actors.Length;j++)Assert.GreaterOrEqual((actors[i].P-actors[j].P).Length,2*Radius,"frozen start full clearance");
            var profile=NavigationProfile.Default;Assert.AreEqual(Radius,profile.Radius);Assert.AreEqual(Speed,profile.TankSpeed);Assert.AreEqual(Turn,profile.TankTurnSpeed);
            Assert.AreEqual(Horizon,profile.CrowdLookaheadSeconds);Assert.AreEqual(FastNeighbors,profile.CrowdNeighborCap);
            var result=new FiniteResult{PerActorProgress=new double[actors.Length],PerActorArrived=new bool[actors.Length],PerActorOverflow=new int[actors.Length]};
            var geometry=new NavGeometry(16,new NavObstacle[0],1);var clearance=new GeometryClearance{Actors=actors,Geometry=geometry,Result=result};
            var bodies=new NavMotionBody[actors.Length];var proposals=new NavMotionProposal[actors.Length];var neighbors=new int[actors.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[actors.Length];
            var starts=new double[actors.Length];var selectedVelocity=new V[actors.Length];foreach(var a in actors)starts[a.Id-1]=(a.Goal-a.P).Length;
            for(int tick=0;tick<runTicks;tick++){
                if(tick==captureTick&&capture!=null)capture((Actor[])actors.Clone());
                for(int i=0;i<actors.Length;i++){var a=actors[i];bodies[i]=new NavMotionBody{Id=a.Id,Position=new NavPoint(a.P.X,a.P.Z),Target=new NavPoint(a.Goal.X,a.Goal.Z),Radius=Radius,Speed=Speed,TurnSpeed=Turn,Heading=a.Heading,Active=!a.Fixed&&(a.Goal-a.P).Length>.15};}
                NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,Dt);
                for(int i=0;i<actors.Length;i++){if(counts[i]<0){result.NeighborOverflowRows++;result.NeighborReferences+=profile.CrowdNeighborCap+1;}else result.NeighborReferences+=counts[i];}
                for(int i=0;i<actors.Length;i++)proposals[i]=orca?OrcaProposal(actors,i,neighbors,counts[i],profile,Dt,clearance,result):NavCrowdMotion.Select(bodies,i,neighbors,counts[i],profile,Dt,clearance);
                for(int i=0;i<actors.Length;i++)if(proposals[i].Blocked)result.BlockedProposals++;
                for(int i=0;i<actors.Length;i++)selectedVelocity[i]=new V(proposals[i].Velocity.X,proposals[i].Velocity.Z);
                NavCrowdMotion.ResolveJoint(bodies,proposals,Dt);
                for(int i=0;i<actors.Length;i++)if(selectedVelocity[i].Length>1e-10&&NavCrowdMotion.Length(proposals[i].Velocity)<=1e-10)result.JointRefusals++;
                for(int i=0;i<actors.Length;i++)for(int j=i+1;j<actors.Length;j++){
                    var vi=new V(proposals[i].Velocity.X,proposals[i].Velocity.Z);var vj=new V(proposals[j].Velocity.X,proposals[j].Velocity.Z);result.Guards++;
                    Assert.GreaterOrEqual(Closest(actors[i].P-actors[j].P,vi-vj,Dt),4*Radius*Radius-1e-9,"independent actual-dt swept pair guard");
                }
                for(int i=0;i<actors.Length;i++){
                    var a=actors[i];var p=proposals[i];var next=new V(p.Position.X,p.Position.Z);double turn=Math.Abs(Angle(p.Heading-a.Heading));
                    Assert.LessOrEqual(turn,Turn*Dt+1e-10,"bounded turn");Assert.LessOrEqual((next-a.P).Length,Math.Min(Speed,(a.Goal-a.P).Length/Dt)*Dt+1e-10,"bounded translation including arrival-distance cap");
                    result.TerrainChecks++;bool clear=geometry.SegmentFree(new NavPoint(a.P.X,a.P.Z),p.Position,Radius);Assert.True(clear,"post-joint swept terrain clearance");
                    if(p.Overflow){result.Overflows++;result.PerActorOverflow[a.Id-1]++;Assert.AreEqual(0d,p.Velocity.X);Assert.AreEqual(0d,p.Velocity.Z);Assert.AreEqual(a.P.X,next.X);Assert.AreEqual(a.P.Z,next.Z);Assert.AreEqual(a.Heading,p.Heading);}
                    if(a.Fixed){Assert.AreEqual(a.P.X,next.X,"fixed/HOLD x");Assert.AreEqual(a.P.Z,next.Z,"fixed/HOLD z");Assert.AreEqual(a.Heading,p.Heading,"fixed/HOLD heading");Assert.AreEqual(0d,p.Velocity.X);Assert.AreEqual(0d,p.Velocity.Z);}
                    else if((next-a.P).Length<=1e-12){result.NoProgress++;if(turn>1e-12)result.Turns++;}
                    a.P=next;a.Velocity=new V(p.Velocity.X,p.Velocity.Z);a.Heading=p.Heading;actors[i]=a;
                }
            }
            var report=new StringBuilder();foreach(var a in actors)if(!a.Fixed){double remaining=(a.Goal-a.P).Length;result.PerActorProgress[a.Id-1]=starts[a.Id-1]-remaining;result.PerActorArrived[a.Id-1]=remaining<=.15;result.Progress+=result.PerActorProgress[a.Id-1];if(remaining<=.15)result.Arrivals++;report.Append($"actor={a.Id}:progress={result.PerActorProgress[a.Id-1]:R},arrived={remaining<=.15},remaining={remaining:R},overflowTicks={result.PerActorOverflow[a.Id-1]};");}
            result.ActorReport=report.ToString();return result;
        }
        static void AssertSameFiniteOutcomes(FiniteResult a,FiniteResult b,string message){
            Assert.AreEqual(a.Progress,b.Progress,1e-9,message+" total progress");Assert.AreEqual(a.Arrivals,b.Arrivals,message+" arrivals");Assert.AreEqual(a.Overflows,b.Overflows,message+" overflow");
            Assert.AreEqual(a.Candidates,b.Candidates,message+" candidate work");Assert.AreEqual(a.Legal,b.Legal,message+" clearance accepts");Assert.AreEqual(a.Rejected,b.Rejected,message+" clearance rejects");Assert.AreEqual(a.Constraints,b.Constraints,message+" ORCA constraints");Assert.AreEqual(a.LineBuilds,b.LineBuilds,message+" ORCA half-plane work");Assert.AreEqual(a.LinePairs,b.LinePairs,message+" LP line pairs");Assert.AreEqual(a.LineCircleHits,b.LineCircleHits,message+" LP circle intersections");Assert.AreEqual(a.LpCandidateChecks,b.LpCandidateChecks,message+" LP candidate work");Assert.AreEqual(a.LpFeasibilityCalls,b.LpFeasibilityCalls,message+" LP feasibility calls");Assert.AreEqual(a.LpPlaneTests,b.LpPlaneTests,message+" LP plane tests");Assert.AreEqual(a.LpInfeasible,b.LpInfeasible,message+" reachable infeasible solves");Assert.AreEqual(a.FullDiskInfeasible,b.FullDiskInfeasible,message+" full-disc infeasible solves");
            Assert.AreEqual(a.NeighborReferences,b.NeighborReferences,message+" neighbor references");Assert.AreEqual(a.NeighborOverflowRows,b.NeighborOverflowRows,message+" neighbor overflow rows");Assert.AreEqual(a.NoProgress,b.NoProgress,message+" waits");Assert.AreEqual(a.Turns,b.Turns,message+" turns");Assert.AreEqual(a.TurnInPlace,b.TurnInPlace,message+" turn-in-place proposals");Assert.AreEqual(a.TerrainChecks,b.TerrainChecks,message+" terrain checks");Assert.AreEqual(a.TerrainRejects,b.TerrainRejects,message+" terrain rejects");Assert.AreEqual(a.BlockedProposals,b.BlockedProposals,message+" blocked proposals");Assert.AreEqual(a.StaticSweepChecks,b.StaticSweepChecks,message+" static future sweeps");Assert.AreEqual(a.JointRefusals,b.JointRefusals,message+" joint refusals");Assert.AreEqual(a.Guards,b.Guards,message+" swept guards");
            for(int i=0;i<a.PerActorProgress.Length;i++){Assert.AreEqual(a.PerActorProgress[i],b.PerActorProgress[i],1e-9,message+" per-actor progress");Assert.AreEqual(a.PerActorArrived[i],b.PerActorArrived[i],message+" per-actor arrival");Assert.AreEqual(a.PerActorOverflow[i],b.PerActorOverflow[i],message+" per-actor overflow");}
        }
        static ulong HashWord(ulong hash,ulong value){unchecked{for(int i=0;i<8;i++){hash^=(byte)value;hash*=1099511628211UL;value>>=8;}return hash;}}
        static ulong HashDouble(ulong hash,double value)=>HashWord(hash,unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));
        static ulong HashActors(Actor[] actors,int kind,int tick,int mirror,int reverse){
            ulong hash=14695981039346656037UL;hash=HashWord(hash,(ulong)kind);hash=HashWord(hash,(ulong)tick);hash=HashWord(hash,(ulong)mirror);hash=HashWord(hash,(ulong)reverse);hash=HashWord(hash,(ulong)actors.Length);
            for(int i=0;i<actors.Length;i++){var a=actors[i];hash=HashWord(hash,(ulong)a.Id);hash=HashDouble(hash,a.P.X);hash=HashDouble(hash,a.P.Z);hash=HashDouble(hash,a.Goal.X);hash=HashDouble(hash,a.Goal.Z);hash=HashDouble(hash,a.Velocity.X);hash=HashDouble(hash,a.Velocity.Z);hash=HashDouble(hash,a.Heading);hash=HashWord(hash,a.Fixed?1UL:0UL);hash=HashWord(hash,a.Enemy?1UL:0UL);}return hash;
        }
        static ulong HashProfile(NavigationProfile p){
            ulong hash=14695981039346656037UL;hash=HashDouble(hash,Dt);hash=HashDouble(hash,p.Radius);hash=HashDouble(hash,p.TankSpeed);hash=HashDouble(hash,p.TankTurnSpeed);hash=HashDouble(hash,p.CrowdLookaheadSeconds);hash=HashWord(hash,(ulong)p.CrowdHeadingIntervals);hash=HashWord(hash,(ulong)p.CrowdSpeedBands);hash=HashWord(hash,(ulong)p.CrowdNeighborCap);for(int i=0;i<p.ProfileId.Length;i++)hash=HashWord(hash,p.ProfileId[i]);return hash;
        }
        static string F(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
        static string SnapshotText(CpuSnapshot snapshot){
            var b=new StringBuilder();for(int i=0;i<snapshot.Actors.Length;i++){var a=snapshot.Actors[i];if(i>0)b.Append('|');b.Append("id=").Append(a.Id).Append(",p=").Append(F(a.P.X)).Append(':').Append(F(a.P.Z)).Append(",v=").Append(F(a.Velocity.X)).Append(':').Append(F(a.Velocity.Z)).Append(",goal=").Append(F(a.Goal.X)).Append(':').Append(F(a.Goal.Z)).Append(",heading=").Append(F(a.Heading)).Append(",fixed=").Append(a.Fixed?1:0).Append(",enemy=").Append(a.Enemy?1:0);}return b.ToString();
        }
        static CpuSnapshot[] CaptureCpuSnapshots(NavigationProfile profile,NavGeometry geometry){
            // Ticks and reasons are frozen in source before timing: solo turn tick 8;
            // four-way approach tick 36 before contact; fixed/enemy approach tick 24;
            // dense 12-body approach tick 48 near the first stationary obstruction.
            int[] ticks={8,36,24,48};string[] reasons={"solo early-turn after eight bounded updates","opposing crossing before contact at source tick 36","fixed and enemy peer approach at source tick 24","12-body dense approach near the first stationary obstruction at source tick 48"};
            var snapshots=new CpuSnapshot[CpuSnapshotCount];int cursor=0;
            for(int kind=0;kind<4;kind++)for(int mirror=0;mirror<2;mirror++)for(int reverse=0;reverse<2;reverse++){
                Actor[] captured=null;RunFinite(kind,mirror==1,reverse==1,false,ticks[kind]+1,ticks[kind],actors=>captured=actors);
                Assert.NotNull(captured,$"snapshot captured kind={kind},mirror={mirror},reverse={reverse}");
                var snapshot=new CpuSnapshot{Kind=kind,Tick=ticks[kind],Mirror=mirror,Reverse=reverse,Reason=reasons[kind],Actors=captured};snapshot.Checksum=HashActors(captured,kind,ticks[kind],mirror,reverse);
                var bodies=new NavMotionBody[captured.Length];var neighbors=new int[captured.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[captured.Length];
                for(int i=0;i<captured.Length;i++){var a=captured[i];bodies[i]=new NavMotionBody{Id=a.Id,Position=new NavPoint(a.P.X,a.P.Z),Target=new NavPoint(a.Goal.X,a.Goal.Z),Radius=Radius,Speed=Speed,TurnSpeed=Turn,Heading=a.Heading,Active=!a.Fixed&&(a.Goal-a.P).Length>.15};}
                NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,Dt);for(int i=0;i<counts.Length;i++){if(bodies[i].Active&&counts[i]!=0)snapshot.ActiveNeighborRows++;int neighborsForReport=counts[i]<0?profile.CrowdNeighborCap+1:counts[i];if(neighborsForReport>snapshot.MaxNeighborCount)snapshot.MaxNeighborCount=neighborsForReport;}
                if(kind==1)Assert.GreaterOrEqual(snapshot.ActiveNeighborRows,4,"crossing snapshot contains actual interacting active rows");
                if(kind==2)Assert.GreaterOrEqual(snapshot.ActiveNeighborRows,1,"fixed/enemy approach snapshot contains actual peer interaction");
                if(kind==3)Assert.GreaterOrEqual(snapshot.MaxNeighborCount,2,"dense snapshot is inside the authored crowd, not initial separated positions");
                // Frozen captured poses must still be physically clear at the source boundary.
                for(int i=0;i<captured.Length;i++)for(int j=i+1;j<captured.Length;j++)Assert.GreaterOrEqual((captured[i].P-captured[j].P).Length,2*Radius-1e-9,$"captured pose clearance kind={kind},tick={ticks[kind]}");
                snapshots[cursor++]=snapshot;
            }
            Assert.AreEqual(CpuSnapshotCount,cursor);return snapshots;
        }
        static CpuWorkspace MakeCpuWorkspace(CpuSnapshot snapshot,NavigationProfile profile,NavGeometry geometry){
            int n=snapshot.Actors.Length;var result=new FiniteResult();var actors=new Actor[n];var clearance=new GeometryClearance{Actors=actors,Geometry=geometry,Result=result};
            return new CpuWorkspace{Actors=actors,Bodies=new NavMotionBody[n],Proposals=new NavMotionProposal[n],Neighbors=new int[n*NavCrowdMotion.NeighborCapacity],Counts=new int[n],SelectedVelocity=new V[n],Result=result,Clearance=clearance,Profile=profile};
        }
        static void ResetCpuCounters(FiniteResult r){
            r.Candidates=r.Constraints=r.Guards=r.Legal=r.Rejected=r.NoProgress=r.Turns=r.Arrivals=r.JointRefusals=r.Overflows=r.TerrainChecks=r.TerrainRejects=r.LineBuilds=r.LinePairs=r.LineCircleHits=r.LpCandidateChecks=r.LpFeasibilityCalls=r.LpPlaneTests=r.LpInfeasible=r.FullDiskInfeasible=r.TurnInPlace=r.BlockedProposals=r.NeighborReferences=r.NeighborOverflowRows=r.StaticSweepChecks=0;
        }
        static bool CpuSnapshotStep(CpuSnapshot snapshot,CpuWorkspace w,bool orca,ref ulong checksum){
            var source=snapshot.Actors;Array.Copy(source,w.Actors,source.Length);w.Clearance.Actors=w.Actors;
            for(int i=0;i<w.Actors.Length;i++){var a=w.Actors[i];w.Bodies[i]=new NavMotionBody{Id=a.Id,Position=new NavPoint(a.P.X,a.P.Z),Target=new NavPoint(a.Goal.X,a.Goal.Z),Radius=Radius,Speed=Speed,TurnSpeed=Turn,Heading=a.Heading,Active=!a.Fixed&&(a.Goal-a.P).Length>.15};}
            NavCrowdMotion.Neighbors(w.Bodies,w.Neighbors,w.Counts,w.Profile,Dt);
            for(int i=0;i<w.Counts.Length;i++){if(w.Counts[i]<0){w.Result.NeighborOverflowRows++;w.Result.NeighborReferences+=w.Profile.CrowdNeighborCap+1;}else w.Result.NeighborReferences+=w.Counts[i];}
            for(int i=0;i<w.Actors.Length;i++)w.Proposals[i]=orca?OrcaProposal(w.Actors,i,w.Neighbors,w.Counts[i],w.Profile,Dt,w.Clearance,w.Result):NavCrowdMotion.Select(w.Bodies,i,w.Neighbors,w.Counts[i],w.Profile,Dt,w.Clearance);
            for(int i=0;i<w.Actors.Length;i++)w.SelectedVelocity[i]=new V(w.Proposals[i].Velocity.X,w.Proposals[i].Velocity.Z);
            NavCrowdMotion.ResolveJoint(w.Bodies,w.Proposals,Dt);bool safe=true;
            for(int i=0;i<w.Actors.Length;i++)if(w.SelectedVelocity[i].Length>1e-10&&NavCrowdMotion.Length(w.Proposals[i].Velocity)<=1e-10)w.Result.JointRefusals++;
            for(int i=0;i<w.Actors.Length;i++)for(int j=i+1;j<w.Actors.Length;j++){
                var vi=new V(w.Proposals[i].Velocity.X,w.Proposals[i].Velocity.Z);var vj=new V(w.Proposals[j].Velocity.X,w.Proposals[j].Velocity.Z);w.Result.Guards++;
                if(Closest(w.Actors[i].P-w.Actors[j].P,vi-vj,Dt)<4*Radius*Radius-1e-9)safe=false;
            }
            for(int i=0;i<w.Actors.Length;i++){
                var a=w.Actors[i];var p=w.Proposals[i];var next=new V(p.Position.X,p.Position.Z);double turn=Math.Abs(Angle(p.Heading-a.Heading));double maxStep=Math.Min(Speed,(a.Goal-a.P).Length/Dt)*Dt;
                w.Result.TerrainChecks++;if(!w.Clearance.Geometry.SegmentFree(new NavPoint(a.P.X,a.P.Z),p.Position,Radius))safe=false;
                if(turn>Turn*Dt+1e-10||(next-a.P).Length>maxStep+1e-10)safe=false;
                if(a.Fixed&&(Math.Abs(a.P.X-next.X)>1e-12||Math.Abs(a.P.Z-next.Z)>1e-12||Math.Abs(a.Heading-p.Heading)>1e-12||Math.Abs(p.Velocity.X)>1e-12||Math.Abs(p.Velocity.Z)>1e-12))safe=false;
                if(p.Overflow&&(Math.Abs(a.P.X-next.X)>1e-12||Math.Abs(a.P.Z-next.Z)>1e-12||Math.Abs(a.Heading-p.Heading)>1e-12||Math.Abs(p.Velocity.X)>1e-12||Math.Abs(p.Velocity.Z)>1e-12))safe=false;
                checksum=HashWord(checksum,(ulong)a.Id);checksum=HashDouble(checksum,next.X);checksum=HashDouble(checksum,next.Z);checksum=HashDouble(checksum,p.Velocity.X);checksum=HashDouble(checksum,p.Velocity.Z);checksum=HashDouble(checksum,p.Heading);checksum=HashWord(checksum,p.Overflow?1UL:0UL);checksum=HashWord(checksum,p.Blocked?1UL:0UL);
            }
            return safe;
        }
        static CpuBatch RunCpuBatch(CpuSnapshot[] snapshots,CpuWorkspace[] workspaces,bool orca,int passes){
            for(int i=0;i<workspaces.Length;i++)ResetCpuCounters(workspaces[i].Result);
            ulong checksum=14695981039346656037UL;bool safe=true;long allocationStart=AllocationCounterAvailable?GC.GetAllocatedBytesForCurrentThread():0;long clockStart=Stopwatch.GetTimestamp();
            for(int pass=0;pass<passes;pass++)for(int i=0;i<snapshots.Length;i++)if(!CpuSnapshotStep(snapshots[i],workspaces[i],orca,ref checksum))safe=false;
            long ticks=Stopwatch.GetTimestamp()-clockStart;long bytes=AllocationCounterAvailable?GC.GetAllocatedBytesForCurrentThread()-allocationStart:-1;var batch=new CpuBatch{Ticks=ticks,Bytes=bytes,Checksum=checksum,Safe=safe};
            for(int i=0;i<workspaces.Length;i++){var r=workspaces[i].Result;batch.NeighborReferences+=r.NeighborReferences;batch.NeighborOverflowRows+=r.NeighborOverflowRows;batch.Candidates+=r.Candidates;batch.TerrainChecks+=r.TerrainChecks;batch.TerrainRejects+=r.TerrainRejects;batch.Legal+=r.Legal;batch.Rejected+=r.Rejected;batch.LineBuilds+=r.LineBuilds;batch.LinePairs+=r.LinePairs;batch.LineCircleHits+=r.LineCircleHits;batch.LpCandidates+=r.LpCandidateChecks;batch.LpFeasibilityCalls+=r.LpFeasibilityCalls;batch.LpPlaneTests+=r.LpPlaneTests;batch.LpInfeasible+=r.LpInfeasible;batch.FullDiskInfeasible+=r.FullDiskInfeasible;batch.TurnInPlace+=r.TurnInPlace;batch.Blocked+=r.BlockedProposals;batch.StaticSweepChecks+=r.StaticSweepChecks;batch.JointRefusals+=r.JointRefusals;batch.PairSweepChecks+=r.Guards;}
            return batch;
        }
        static string CpuWorkText(CpuBatch b)=>$"neighbors={b.NeighborReferences},neighborOverflowRows={b.NeighborOverflowRows},terrainCalls={b.TerrainChecks},terrainRejects={b.TerrainRejects},clearanceCandidates={b.Candidates},clearanceAccepted={b.Legal},clearanceRejected={b.Rejected},orcaLines={b.LineBuilds},lpLinePairs={b.LinePairs},lpLineCircle={b.LineCircleHits},lpCandidates={b.LpCandidates},lpFeasibility={b.LpFeasibilityCalls},lpPlaneTests={b.LpPlaneTests},lpInfeasible={b.LpInfeasible},fullDiskInfeasible={b.FullDiskInfeasible},turnInPlace={b.TurnInPlace},blocked={b.Blocked},inactiveSweep={b.StaticSweepChecks},jointRefusals={b.JointRefusals},allPairSweeps={b.PairSweepChecks}";
        static void AssertCpuBatch(CpuBatch batch,bool orca,int series,string phase){
            string allocation=batch.Bytes<0?"NotRun_counter_unavailable":batch.Bytes.ToString(CultureInfo.InvariantCulture);TestContext.Out.WriteLine($"CPU_BATCH phase={phase},series={series},solver={(orca?"continuousORCA":"sampling")},ticks={batch.Ticks},frequency={Stopwatch.Frequency},milliseconds={batch.Ticks*1000d/Stopwatch.Frequency:R},allocBytes={allocation},checksum={batch.Checksum:x16},safe={batch.Safe},{CpuWorkText(batch)}");
            Assert.True(batch.Safe,$"CPU batch safety/clearance/pair guard failed phase={phase},series={series},solver={(orca?"ORCA":"sampling")}");Assert.AreNotEqual(0UL,batch.Checksum,$"CPU batch checksum phase={phase},series={series}");
        }
        static double Median(double[] values){var sorted=(double[])values.Clone();Array.Sort(sorted);return sorted[sorted.Length/2];}
        [Test] public void ContinuousOrcaLpCertificatesCoverHalfPlaneWedgeDiscAndEmptyIntersection(){
            var work=new SolveWork();var plane=new[]{Plane(new V(1,0),1)};
            Assert.True(ClosestFeasible(default,plane,1,4,ref work,out var halfPlane));Assert.AreEqual(1d,halfPlane.X,1e-9);Assert.AreEqual(0d,halfPlane.Z,1e-9);
            var wedge=new HalfPlane[2];int wedgeCount=0;AddReachableWedge(0,.15,wedge,ref wedgeCount);
            Assert.True(ClosestFeasible(new V(0,4),wedge,wedgeCount,4,ref work,out var turned));Assert.AreEqual(.15,Math.Atan2(turned.Z,turned.X),1e-9,"continuous wedge boundary, not a heading sample");Assert.LessOrEqual(turned.Length,4+1e-9);
            Assert.True(ClosestFeasible(new V(8,0),new HalfPlane[0],0,4,ref work,out var disc));Assert.AreEqual(4d,disc.X,1e-9);Assert.AreEqual(0d,disc.Z,1e-9);
            Assert.False(ClosestFeasible(default,new[]{Plane(new V(1,0),5)},1,4,ref work,out _),"empty half-plane/speed-disc intersection is explicit");
        }
        [Test] public void EmptyReachableOrcaSetTurnsInPlaceTowardFeasibleOrcaAimWithoutClampingVelocity(){
            var actor=new Actor{Id=1,P=new V(0,0),Goal=new V(6,0),Heading=0};var actors=new[]{actor};
            var fullPlanes=new[]{Plane(new V(0,1),1)};var reachablePlanes=new HalfPlane[3];reachablePlanes[0]=fullPlanes[0];int reachableCount=1;AddReachableWedge(actor.Heading,Turn*Dt,reachablePlanes,ref reachableCount);
            var work=new SolveWork();Assert.False(ClosestFeasible(new V(Speed,0),reachablePlanes,reachableCount,Speed,ref work,out _),"no translating velocity fits the bounded reachable cone");
            var geometry=new NavGeometry(16,new NavObstacle[0],1);var result=new FiniteResult();var clearance=new GeometryClearance{Actors=actors,Geometry=geometry,Result=result};
            var proposal=new NavMotionProposal{Position=new NavPoint(actor.P.X,actor.P.Z),Heading=actor.Heading};
            Assert.True(TurnInPlaceFromOrcaAim(actors,0,fullPlanes,1,Speed,Turn*Dt,Dt,clearance,result,ref proposal),"feasible full-disc ORCA aim has clear future availability");
            Assert.AreEqual(actor.P.X,proposal.Position.X);Assert.AreEqual(actor.P.Z,proposal.Position.Z);Assert.AreEqual(0d,proposal.Velocity.X);Assert.AreEqual(0d,proposal.Velocity.Z);
            Assert.AreEqual(Turn*Dt,proposal.Heading,1e-9,"turn remains bounded toward the ORCA aim");Assert.AreEqual(1,result.TurnInPlace);Assert.AreEqual(0,result.TerrainRejects);
        }
        [Test] public void NeighborOverflowRemainsAnExplicitStationaryOutcomeForBothProposalGenerators(){
            var profile=NavigationProfile.Default;var actors=new Actor[10];var bodies=new NavMotionBody[actors.Length];
            actors[0]=new Actor{Id=1,P=new V(0,0),Goal=new V(6,0),Heading=0};
            for(int i=1;i<actors.Length;i++){double angle=2*Math.PI*(i-1)/(actors.Length-1);var p=Velocity(angle,3);actors[i]=new Actor{Id=i+1,P=p,Goal=p+Velocity(angle,1),Heading=angle};}
            for(int i=0;i<actors.Length;i++){var a=actors[i];bodies[i]=new NavMotionBody{Id=a.Id,Position=new NavPoint(a.P.X,a.P.Z),Target=new NavPoint(a.Goal.X,a.Goal.Z),Radius=Radius,Speed=Speed,TurnSpeed=Turn,Heading=a.Heading,Active=true};}
            var neighbors=new int[actors.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[actors.Length];NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,Dt);Assert.Less(counts[0],0,"the shared neighbor classifier reports cap overflow");
            foreach(bool orca in new[]{false,true}){
                var result=new FiniteResult();var geometry=new NavGeometry(16,new NavObstacle[0],1);var clearance=new GeometryClearance{Actors=actors,Geometry=geometry,Result=result};
                var proposal=orca?OrcaProposal(actors,0,neighbors,counts[0],profile,Dt,clearance,result):NavCrowdMotion.Select(bodies,0,neighbors,counts[0],profile,Dt,clearance);
                Assert.True(proposal.Overflow,orca?"ORCA overflow":"sampling overflow");Assert.AreEqual(0d,proposal.Velocity.X);Assert.AreEqual(0d,proposal.Velocity.Z);Assert.AreEqual(actors[0].P.X,proposal.Position.X);Assert.AreEqual(actors[0].P.Z,proposal.Position.Z);Assert.AreEqual(actors[0].Heading,proposal.Heading);
            }
        }
        [Test] public void ArrivedNonFixedPeerGetsFullOrcaResponsibilityAndStationaryFutureSweep(){
            var mover=new Actor{Id=1,P=new V(0,0),Goal=new V(8,0),Velocity=new V(4,0)};
            var arrived=new Actor{Id=2,P=new V(1.5,0),Goal=new V(1.5,0),Velocity=new V(0,0)};
            Assert.False(arrived.Fixed);Assert.False(IsActivePeer(arrived),"arrival threshold matches NavMotionBody.Active");
            var stationaryCopy=arrived;stationaryCopy.Fixed=true;stationaryCopy.Velocity=default;
            var arrivedLine=ContinuousOrcaLine(mover,arrived);var fixedLine=Orca(mover,stationaryCopy);var oldHalfLine=Orca(mover,arrived);
            Assert.AreEqual(fixedLine.Point.X,arrivedLine.Point.X,1e-12,"arrived peer has full responsibility");Assert.AreEqual(fixedLine.Point.Z,arrivedLine.Point.Z,1e-12);
            Assert.AreNotEqual(oldHalfLine.Point.X,arrivedLine.Point.X,"adapter differs from historical half-responsibility line");
            Assert.False(InactivePeerSweepClear(new[]{mover,arrived},0,new V(4,0),Dt,new FiniteResult()),"arrived peer remains a stationary future collision hazard");
        }
        [Test] public void FrozenB0ContinuousConstrainedOrcaVersusSampling(){
            var report=new StringBuilder("B0 continuous constrained ORCA LP vs existing sampling; test-only; dt=1/30,ticks=240,radius=.72,speed=4,turn=4.5,horizon=1.2,headingIntervals=16,speedBands=2,neighbors=8; identical neighbor classification, empty authored-obstacle geometry and swept guards; active peers split ORCA responsibility, inactive fixed/arrived/HOLD/enemy peers take full local responsibility and stationary future sweep; zero/empty reachable result may turn in place only toward a feasible unconstrained ORCA aim after terrain+inactive-peer future sweep; no goal-turn fallback, no post-hoc velocity clamp, no CPU/allocation/performance claim\n");
            for(int kind=0;kind<4;kind++){
                FiniteResult unmirroredSampling=null,unmirroredOrca=null;
                for(int mirror=0;mirror<2;mirror++)foreach(bool orca in new[]{false,true}){
                    var forward=RunFinite(kind,mirror==1,false,orca);var reverse=RunFinite(kind,mirror==1,true,orca);
                    AssertSameFiniteOutcomes(forward,reverse,$"kind={kind},mirror={mirror},solver={(orca?"continuous ORCA":"sampling")} insertion-order independence");
                    if(mirror==0){if(orca)unmirroredOrca=forward;else unmirroredSampling=forward;}
                    else {
                        // Reflection is a frozen input transform. The actual
                        // output is data here, not an assumed reflection-invariance
                        // contract; report per-actor deltas for root review.
                        var original=orca?unmirroredOrca:unmirroredSampling;var delta=new StringBuilder();
                        for(int actor=0;actor<forward.PerActorProgress.Length;actor++)delta.Append($"actor={actor+1}:progressDelta={(forward.PerActorProgress[actor]-original.PerActorProgress[actor]):R},arrivalChanged={forward.PerActorArrived[actor]!=original.PerActorArrived[actor]};");
                        report.AppendLine($"reflection-diagnostic input={kind},solver={(orca?"continuous constrained ORCA LP":"existing reachable sampling")},progressDelta={(forward.Progress-original.Progress):R},arrivalDelta={forward.Arrivals-original.Arrivals};{delta}");
                    }
                    report.AppendLine($"input={kind},mirror={mirror},solver={(orca?"continuous constrained ORCA LP":"existing reachable sampling")},insertion=forward,{forward.Summary};{forward.ActorReport}");
                }
            }
            TestContext.Out.Write(report.ToString());
        }
        static ulong CpuWorkSignature(CpuBatch b){
            ulong h=14695981039346656037UL;h=HashWord(h,(ulong)b.NeighborReferences);h=HashWord(h,(ulong)b.NeighborOverflowRows);h=HashWord(h,(ulong)b.Candidates);h=HashWord(h,(ulong)b.TerrainChecks);h=HashWord(h,(ulong)b.TerrainRejects);h=HashWord(h,(ulong)b.Legal);h=HashWord(h,(ulong)b.Rejected);h=HashWord(h,(ulong)b.LineBuilds);h=HashWord(h,(ulong)b.LinePairs);h=HashWord(h,(ulong)b.LineCircleHits);h=HashWord(h,(ulong)b.LpCandidates);h=HashWord(h,(ulong)b.LpFeasibilityCalls);h=HashWord(h,(ulong)b.LpPlaneTests);h=HashWord(h,(ulong)b.LpInfeasible);h=HashWord(h,(ulong)b.FullDiskInfeasible);h=HashWord(h,(ulong)b.TurnInPlace);h=HashWord(h,(ulong)b.Blocked);h=HashWord(h,(ulong)b.StaticSweepChecks);h=HashWord(h,(ulong)b.JointRefusals);h=HashWord(h,(ulong)b.PairSweepChecks);return h;
        }
        static void CheckCpuBudget(long methodStart,string phase){
            long elapsed=Stopwatch.GetTimestamp()-methodStart;if(elapsed<=20L*Stopwatch.Frequency)return;
            TestContext.Out.WriteLine($"CPU_BENCHMARK_NOT_RUN reason=20s_method_body_budget_exceeded,phase={phase},elapsedTicks={elapsed},frequency={Stopwatch.Frequency}; partial measurements are not a winner and must not be compared");
            Assert.Fail($"CPU_BENCHMARK_NOT_RUN: 20-second method-body budget exceeded during {phase}; no partial winner claim");
        }
        [Test,Explicit("CPU timing requires the exact method filter and exclusive measurement runner.")] public void FrozenB0SolverCpuMicrobenchmark(){
            long methodStart=Stopwatch.GetTimestamp();var profile=NavigationProfile.Default;
            Assert.AreEqual(Radius,profile.Radius);Assert.AreEqual(Speed,profile.TankSpeed);Assert.AreEqual(Turn,profile.TankTurnSpeed);Assert.AreEqual(Horizon,profile.CrowdLookaheadSeconds);Assert.AreEqual(Directions,profile.CrowdHeadingIntervals);Assert.AreEqual(SpeedBands,profile.CrowdSpeedBands);Assert.AreEqual(FastNeighbors,profile.CrowdNeighborCap);
            var geometry=new NavGeometry(16,new NavObstacle[0],1);var snapshots=CaptureCpuSnapshots(profile,geometry);var profileHash=HashProfile(profile);
            for(int i=0;i<snapshots.Length;i++)TestContext.Out.WriteLine($"CPU_SNAPSHOT index={i},kind={snapshots[i].Kind},tick={snapshots[i].Tick},mirror={snapshots[i].Mirror},reverseInsertion={snapshots[i].Reverse},reason={snapshots[i].Reason},activeNeighborRows={snapshots[i].ActiveNeighborRows},maxNeighborCount={snapshots[i].MaxNeighborCount},profileChecksum={profileHash:x16},inputChecksum={snapshots[i].Checksum:x16},actors=[{SnapshotText(snapshots[i])}]");
            CheckCpuBudget(methodStart,"snapshot_capture");
            long calibrationStart=GC.GetAllocatedBytesForCurrentThread();var calibration=new byte[4096];AllocationCalibrationEscape=calibration;GC.KeepAlive(AllocationCalibrationEscape);long calibrationBytes=GC.GetAllocatedBytesForCurrentThread()-calibrationStart;AllocationCalibrationEscape=null;GC.KeepAlive(calibration);AllocationCounterAvailable=calibrationBytes>0;
            TestContext.Out.WriteLine($"CPU_ALLOCATION_CALIBRATION escapedArrayBytes={calibrationBytes},counterAvailable={AllocationCounterAvailable},interpretation={(AllocationCounterAvailable?"measured managed bytes are observational":"allocation metric NotRun; zero is not evidence of zero allocations")}");
            var sampleWorkspaces=new CpuWorkspace[snapshots.Length];var orcaWorkspaces=new CpuWorkspace[snapshots.Length];
            for(int i=0;i<snapshots.Length;i++){sampleWorkspaces[i]=MakeCpuWorkspace(snapshots[i],profile,geometry);orcaWorkspaces[i]=MakeCpuWorkspace(snapshots[i],profile,geometry);}
            TestContext.Out.WriteLine($"CPU_BENCHMARK_CONFIG snapshots={snapshots.Length},warmupRounds={CpuWarmupRounds},pairedSeries={CpuMeasuredSeries},passesPerSeries={CpuMeasuredPasses},snapshotStepsPerMeasuredBatch={CpuMeasuredPasses*CpuSnapshotCount},stopwatchFrequency={Stopwatch.Frequency},allocationCounterCalibrationBytes={calibrationBytes},allocationMetric={(AllocationCounterAvailable?"available":"NotRun")},timedScope=neighbor+proposal+terrainClearance+jointResolution+actualDtAllPairSweep+pureSafetyGuards; setup,snapshotCapture,serialization,assertions,checksumFormatting,andIO excluded");
            var warmSample=RunCpuBatch(snapshots,sampleWorkspaces,false,CpuWarmupRounds);AssertCpuBatch(warmSample,false,-1,"warmup256");
            var warmOrca=RunCpuBatch(snapshots,orcaWorkspaces,true,CpuWarmupRounds);AssertCpuBatch(warmOrca,true,-1,"warmup256");CheckCpuBudget(methodStart,"warmup");
            var sampleMs=new double[CpuMeasuredSeries];var orcaMs=new double[CpuMeasuredSeries];ulong sampleChecksum=0,orcaChecksum=0,sampleWork=0,orcaWork=0;
            for(int series=0;series<CpuMeasuredSeries;series++){
                bool sampleFirst=(series%2)==0;
                for(int order=0;order<2;order++){
                    bool runSampling=(order==0)==sampleFirst;var batch=runSampling?RunCpuBatch(snapshots,sampleWorkspaces,false,CpuMeasuredPasses):RunCpuBatch(snapshots,orcaWorkspaces,true,CpuMeasuredPasses);
                    bool orca=!runSampling;AssertCpuBatch(batch,orca,series,$"measured{(sampleFirst?"_sampling_first":"_orca_first")}");ulong work=CpuWorkSignature(batch);
                    if(runSampling){sampleMs[series]=batch.Ticks*1000d/Stopwatch.Frequency;if(series==0){sampleChecksum=batch.Checksum;sampleWork=work;}else{Assert.AreEqual(sampleChecksum,batch.Checksum,"sampling batch checksum repeats");Assert.AreEqual(sampleWork,work,"sampling work counters repeat");}}
                    else {orcaMs[series]=batch.Ticks*1000d/Stopwatch.Frequency;if(series==0){orcaChecksum=batch.Checksum;orcaWork=work;}else{Assert.AreEqual(orcaChecksum,batch.Checksum,"ORCA batch checksum repeats");Assert.AreEqual(orcaWork,work,"ORCA work counters repeat");}}
                    CheckCpuBudget(methodStart,$"series_{series}_{(orca?"orca":"sampling")}");
                }
            }
            Array.Sort(sampleMs);Array.Sort(orcaMs);
            TestContext.Out.WriteLine($"CPU_BENCHMARK_SUMMARY sampling_ms_median={sampleMs[CpuMeasuredSeries/2]:R},sampling_ms_min={sampleMs[0]:R},sampling_ms_max={sampleMs[CpuMeasuredSeries-1]:R},orca_ms_median={orcaMs[CpuMeasuredSeries/2]:R},orca_ms_min={orcaMs[0]:R},orca_ms_max={orcaMs[CpuMeasuredSeries-1]:R},sampleChecksum={sampleChecksum:x16},orcaChecksum={orcaChecksum:x16},sampleWorkSignature={sampleWork:x16},orcaWorkSignature={orcaWork:x16},allocationCounter={(AllocationCounterAvailable?"calibrated_observational":"NotRun_unavailable")}; allocations are observational only and zero is not evidence of zero allocations; counters instrument the timed path; no universal CPU or runtime winner claim");
            CheckCpuBudget(methodStart,"summary");
        }
        static Actor[] Input(int kind,bool mirror,bool reverse){
            Actor A(int id,double x,double z,double gx,double gz,bool fixedActor=false,bool enemy=false)=>new Actor{Id=id,P=new V(x,z),Goal=new V(gx,gz),Heading=Math.Atan2(gz-z,gx-x),Fixed=fixedActor,Enemy=enemy};
            Actor[] actors;
            if(kind==0){actors=new[]{A(1,-6,0,6,0)};actors[0].Heading=Math.PI/2;}
            else if(kind==1)actors=new[]{A(1,-6,-1,6,-1),A(2,-6,1,6,1),A(3,6,-1,-6,-1),A(4,6,1,-6,1)};
            else if(kind==2)actors=new[]{A(1,-6,0,6,0),A(2,0,0,0,0,true),A(3,1.6,2,1.6,2,true,true)};
            else {actors=new Actor[FastNeighbors+4];actors[0]=A(1,-6,0,6,0);for(int i=1;i<actors.Length;i++)actors[i]=A(i+1,-2.2+(i%3)*2.2,-4.4+(i/3)*2.2,-2.2+(i%3)*2.2,-4.4+(i/3)*2.2,true);}
            if(mirror)for(int i=0;i<actors.Length;i++){actors[i].P=new V(-actors[i].P.X,actors[i].P.Z);actors[i].Goal=new V(-actors[i].Goal.X,actors[i].Goal.Z);actors[i].Heading=Angle(Math.PI-actors[i].Heading);}
            if(reverse)Array.Reverse(actors);return actors;
        }
        // A tiny independent witness: at three fixed reachable headings solve
        // the ORCA linear speed interval analytically, then check actual dt
        // against the other joint proposals. It is diagnostic, never applied; an alternative is not proof that
        // the grid has no feasible point or that this greedy objective is optimal.
        static bool TryAnalyticWitness(Actor[] actors,NavMotionProposal[] proposals,int row,out double heading,out double speed)
        {
            heading=speed=0;var a=actors[row];if((a.Goal-a.P).Length<=.15)return false;
            foreach(double offset in new[]{-Turn*Dt/2,0,Turn*Dt/2}){
                double h=Angle(a.Heading+offset);var direction=Velocity(h,1);double low=0,high=Math.Min(Speed,(a.Goal-a.P).Length/Dt);bool feasible=true;
                for(int j=0;j<actors.Length;j++)if(j!=row){var b=actors[j];if((a.P-b.P).Length>2*Radius+2*Speed*Horizon)continue;var line=Orca(a,b);double constant=Det(line.Direction,line.Point),coefficient=Det(line.Direction,direction);
                    if(Math.Abs(coefficient)<1e-12){if(constant>1e-10){feasible=false;break;}}
                    else if(coefficient>0)low=Math.Max(low,(constant-1e-10)/coefficient);else high=Math.Min(high,(constant-1e-10)/coefficient);
                }
                if(!feasible||high<Math.Max(low,1e-6))continue;double v=(Math.Max(low,1e-6)+high)/2;var velocity=direction*v;
                for(int j=0;j<actors.Length;j++)if(j!=row&&Closest(a.P-actors[j].P,velocity-new V(proposals[j].Velocity.X,proposals[j].Velocity.Z),Dt)<4*Radius*Radius-1e-9){feasible=false;break;}
                if(!feasible)continue;heading=h;speed=v;return true;
            }return false;
        }
        sealed class ComparisonClearance : INavMotionClearance
        {
            internal Actor[] Actors;internal bool OrcaFilter;internal Result Result;internal int RejectedThisActor;
            public bool ClearMotion(int row,NavPoint end)
            {
                Result.Candidates++;var a=Actors[row];var velocity=new V((end.X-a.P.X)/Dt,(end.Z-a.P.Z)/Dt);
                if(OrcaFilter)for(int j=0;j<Actors.Length;j++)if(row!=j){
                    var b=Actors[j];double reach=2*Radius+2*Speed*Horizon;if((a.P-b.P).Length>reach)continue;
                    var line=Orca(a,b);Result.Constraints++;if(Det(line.Direction,line.Point-velocity)>1e-10){Result.Rejected++;RejectedThisActor++;return false;}
                }
                Result.Legal++;return true;
            }
        }
        static Result Run(int kind,bool mirror,bool reverse,bool orca){
            var actors=Input(kind,mirror,reverse);for(int i=0;i<actors.Length;i++)for(int j=i+1;j<actors.Length;j++)Assert.GreaterOrEqual((actors[i].P-actors[j].P).Length,2*Radius,"frozen start full clearance");
            var result=new Result{PerActorProgress=new double[actors.Length],PerActorArrived=new bool[actors.Length],PerActorOverflow=new int[actors.Length]};
            var bodies=new NavMotionBody[actors.Length];var proposals=new NavMotionProposal[actors.Length];var neighbors=new int[actors.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[actors.Length];
            var rejectedRows=new bool[actors.Length];var starts=new double[actors.Length];foreach(var a in actors)starts[a.Id-1]=(a.Goal-a.P).Length;
            var clearance=new ComparisonClearance{Actors=actors,OrcaFilter=orca,Result=result};var profile=NavigationProfile.Default;
            Assert.AreEqual(Directions,profile.CrowdHeadingIntervals);Assert.AreEqual(SpeedBands,profile.CrowdSpeedBands);Assert.AreEqual(Horizon,profile.CrowdLookaheadSeconds);Assert.AreEqual(FastNeighbors,profile.CrowdNeighborCap);
            long allocated=GC.GetAllocatedBytesForCurrentThread(),clock=Stopwatch.GetTimestamp();
            for(int tick=0;tick<Ticks;tick++){
                for(int i=0;i<actors.Length;i++){var a=actors[i];bodies[i]=new NavMotionBody{Id=a.Id,Position=new NavPoint(a.P.X,a.P.Z),Target=new NavPoint(a.Goal.X,a.Goal.Z),Radius=Radius,Speed=Speed,TurnSpeed=Turn,Heading=a.Heading,Active=!a.Fixed&&(a.Goal-a.P).Length>.15};}
                NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,Dt);
                for(int i=0;i<actors.Length;i++){clearance.RejectedThisActor=0;proposals[i]=NavCrowdMotion.Select(bodies,i,neighbors,counts[i],profile,Dt,clearance);rejectedRows[i]=clearance.RejectedThisActor>0;}
                NavCrowdMotion.ResolveJoint(bodies,proposals,Dt);
                // Independent analytic swept oracle over the actual executed dt.
                for(int i=0;i<actors.Length;i++)for(int j=i+1;j<actors.Length;j++){
                    var vi=new V(proposals[i].Velocity.X,proposals[i].Velocity.Z);var vj=new V(proposals[j].Velocity.X,proposals[j].Velocity.Z);result.Guards++;
                    Assert.GreaterOrEqual(Closest(actors[i].P-actors[j].P,vi-vj,Dt),4*Radius*Radius-1e-9);
                }
                for(int i=0;i<actors.Length;i++){
                    var p=proposals[i];if(p.Overflow){result.Overflows++;result.PerActorOverflow[actors[i].Id-1]++;}
                    if(orca&&!actors[i].Fixed&&counts[i]>=0&&rejectedRows[i]&&NavCrowdMotion.Length(p.Velocity)==0&&TryAnalyticWitness(actors,proposals,i,out double witnessHeading,out double witnessSpeed)){
                        result.StationaryWithFeasibleWitnesses++;if(result.FirstFeasibleWitness==null)result.FirstFeasibleWitness=$"tick={tick},actor={actors[i].Id},heading={witnessHeading:R},speed={witnessSpeed:R}";
                    }
                    var next=new V(p.Position.X,p.Position.Z);double turn=Math.Abs(Angle(p.Heading-actors[i].Heading));
                    Assert.LessOrEqual(turn,Turn*Dt+1e-10);Assert.LessOrEqual((next-actors[i].P).Length,Speed*Dt+1e-10);
                    if(actors[i].Fixed){Assert.AreEqual(actors[i].P.X,next.X);Assert.AreEqual(actors[i].P.Z,next.Z);Assert.AreEqual(actors[i].Heading,p.Heading);}
                    else if((next-actors[i].P).Length==0){result.NoProgress++;if(turn>0)result.Turns++;}
                    if(p.Blocked)result.JointRejects++;
                    actors[i].P=next;actors[i].Velocity=new V(p.Velocity.X,p.Velocity.Z);actors[i].Heading=p.Heading;
                }
            }
            result.Cpu=(Stopwatch.GetTimestamp()-clock)*1000d/Stopwatch.Frequency;result.Allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;var report=new StringBuilder();
            foreach(var a in actors)if(!a.Fixed){double remaining=(a.Goal-a.P).Length;result.PerActorProgress[a.Id-1]=starts[a.Id-1]-remaining;result.PerActorArrived[a.Id-1]=remaining<=.15;result.Progress+=result.PerActorProgress[a.Id-1];if(remaining<=.15)result.Arrivals++;report.Append($"actor={a.Id}:progress={result.PerActorProgress[a.Id-1]:R},arrived={remaining<=.15},remaining={remaining:R},overflowTicks={result.PerActorOverflow[a.Id-1]};");}
            result.ActorReport=report.ToString()+"firstAnalyticMovingAlternative="+(result.FirstFeasibleWitness??"none");return result;
        }
        [Test] public void FrozenB0ConstrainedOrcaVersusSampling(){
            Assert.AreEqual(NavigationProfile.Default.Radius,Radius);Assert.AreEqual(NavigationProfile.Default.TankSpeed,Speed);Assert.AreEqual(NavigationProfile.Default.TankTurnSpeed,Turn);
            var report=new StringBuilder("B0 frozen: dt=1/30,ticks=240,radius=.72,speed=4,turn=4.5,horizon=1.2,headingIntervals=16,speedBands=2,neighbors=8; repairs=0; preparation excluded; functional shared, all CPU/allocation numbers descriptive only; no performance or winner claim; X reflection x=-x,z=z,heading=pi-heading\n");
            for(int kind=0;kind<4;kind++)for(int mirror=0;mirror<2;mirror++)foreach(bool orca in new[]{false,true}){
                var forward=Run(kind,mirror==1,false,orca);var reverse=Run(kind,mirror==1,true,orca);
                Assert.AreEqual(forward.Progress,reverse.Progress,1e-9,"insertion independence");Assert.AreEqual(forward.Arrivals,reverse.Arrivals);Assert.AreEqual(forward.Legal,reverse.Legal);for(int i=0;i<forward.PerActorProgress.Length;i++){Assert.AreEqual(forward.PerActorProgress[i],reverse.PerActorProgress[i],1e-9,"per actor insertion independence");Assert.AreEqual(forward.PerActorArrived[i],reverse.PerActorArrived[i]);}
                report.AppendLine($"input={kind},mirror={mirror},solver={(orca?"ORCA-filtered discrete sampling":"sampling")},insertion=forward,{forward};{forward.ActorReport}");
                report.AppendLine($"input={kind},mirror={mirror},solver={(orca?"ORCA-filtered discrete sampling":"sampling")},insertion=reverse,{reverse};{reverse.ActorReport}");
            }
            TestContext.Out.Write(report.ToString());
        }
    }
}
