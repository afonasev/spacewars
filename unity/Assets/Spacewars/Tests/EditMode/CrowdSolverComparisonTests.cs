using System;
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
        sealed class Result {
            public long Candidates,Constraints,Guards,Legal,Rejected,NoProgress,Turns,Arrivals,JointRejects,Allocated,Overflows,StationaryWithFeasibleWitnesses;
            public double Progress,Cpu;public double[] PerActorProgress;public bool[] PerActorArrived;public string ActorReport,FirstFeasibleWitness;public int[] PerActorOverflow;
            public override string ToString()=>$"legal={Legal},rejected={Rejected},progress={Progress:R},arrivals={Arrivals},wait={NoProgress},turns={Turns},candidates={Candidates},constraints={Constraints},guards={Guards},jointRejects={JointRejects},overflow={Overflows},analyticStationaryWithFeasibleWitnesses={StationaryWithFeasibleWitnesses},repairs=0,allocated={Allocated},cpuMs={Cpu:R}";
        }
        // Frozen B0 experiment parameters; these are not runtime tuning defaults.
        const double Dt=1d/30,Radius=.72,Speed=4,Turn=4.5,Horizon=1.2;
        const int Ticks=240,Directions=16,SpeedBands=2,FastNeighbors=8;
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
