using System;

namespace Spacewars.Simulation
{
    internal struct NavMotionBody
    {
        internal int Id;
        internal int YieldLabel,OwnRouteIndex,OwnLocalIndex;
        internal bool OwnMoving;internal NavigationOutcome OwnOutcome;
        internal NavPoint Position,Target;
        internal double Heading,Radius,Speed,TurnSpeed;
        internal bool Active,Potential,Yielding,TerminalSeed;
    }
    internal struct NavMotionProposal
    {
        internal NavPoint Position,Velocity;
        internal double Heading;
        internal bool Blocked,Overflow,Refuse,StaticNoWinner;
        // Consumed before the second Joint; never wire or persistent state.
        internal bool IndependentTurn;
    }
    internal interface INavMotionClearance { bool ClearMotion(int actor,NavPoint end); }

    // Stateless reachable sampling. Predictions rank choices; only the jointly
    // executed dt sweeps decide peer safety. No previous velocity is authority.
    internal static class NavCrowdMotion
    {
        internal const int NeighborCapacity=32;
        internal static double Angle(double value){while(value>Math.PI)value-=2*Math.PI;while(value< -Math.PI)value+=2*Math.PI;return value;}
        internal static double Turn(double heading,double aim,double limit)=>Angle(heading+Math.Max(-limit,Math.Min(limit,Angle(aim-heading))));
        internal static double Length(NavPoint p)=>Math.Sqrt(p.X*p.X+p.Z*p.Z);
        private static NavPoint Sub(NavPoint a,NavPoint b)=>new NavPoint(a.X-b.X,a.Z-b.Z);
        private static NavPoint Velocity(double heading,double speed)=>new NavPoint(Math.Cos(heading)*speed,Math.Sin(heading)*speed);
        internal static double ClosestSquared(NavPoint separation,NavPoint relativeVelocity,double duration)
        {
            double q=relativeVelocity.X*relativeVelocity.X+relativeVelocity.Z*relativeVelocity.Z;
            double time=q==0?0:Math.Max(0,Math.Min(duration,-(separation.X*relativeVelocity.X+separation.Z*relativeVelocity.Z)/q));
            double x=separation.X+relativeVelocity.X*time,z=separation.Z+relativeVelocity.Z*time;return x*x+z*z;
        }
        private static NavPoint Preferred(NavMotionBody a)
        {var delta=Sub(a.Target,a.Position);double length=Length(delta);return !a.Active||length==0?default:new NavPoint(delta.X/length*a.Speed,delta.Z/length*a.Speed);}
        private static double NeighborReach(NavMotionBody a,NavMotionBody b,NavigationProfile profile,double dt)
        {return a.Radius+b.Radius+((a.Active||a.Potential?a.Speed:0)+(b.Active||b.Potential?b.Speed:0))*Math.Max(dt,profile.CrowdLookaheadSeconds);}
        private static bool NeighborRelevant(NavMotionBody a,NavMotionBody b,NavigationProfile profile,double dt)
        {var d=Sub(a.Position,b.Position);double reach=NeighborReach(a,b,profile,dt);return d.X*d.X+d.Z*d.Z<=reach*reach;}
        internal static void Neighbors(NavMotionBody[] bodies,int[] neighbors,int[] counts,NavigationProfile profile,double dt)
        {Neighbors(bodies,neighbors,counts,profile,dt,null);}
        internal static void Neighbors(NavMotionBody[] bodies,int[] neighbors,int[] counts,NavigationProfile profile,double dt,int[] components)
        {
            if(components!=null)for(int i=0;i<bodies.Length;i++)components[i]=i;
            for(int i=0;i<bodies.Length;i++){
                int count=0;for(int j=0;j<bodies.Length;j++)if(i!=j){
                    if(!NeighborRelevant(bodies[i],bodies[j],profile,dt))continue;
                    if(components!=null){int a=Root(components,i),b=Root(components,j);if(a!=b)components[b]=a;}
                    if(count<0)continue;
                    if(count==profile.CrowdNeighborCap){count=-1;if(components==null)break;continue;}
                    neighbors[i*NeighborCapacity+count++]=j;
                }counts[i]=count;
            }
            if(components!=null)for(int i=0;i<bodies.Length;i++)components[i]=Root(components,i);
        }
        internal static void ConstrainOverflowComponents(NavMotionBody[] bodies,int[] neighbors,int[] counts,int[] labels,double[] originalSpeeds,NavigationProfile profile,double dt)
        {
            for(int i=0;i<bodies.Length;i++)originalSpeeds[i]=bodies[i].Speed;
            bool hasOverflow=false;for(int i=0;i<bodies.Length;i++)if((bodies[i].Active||bodies[i].Potential)&&counts[i]<0){hasOverflow=true;break;}
            if(!hasOverflow)return; // Initial Neighbors already produced the exact rows; avoid a second all-pairs pass on ordinary frames.
            double horizon=profile==null?double.NaN:Math.Max(dt,profile.CrowdLookaheadSeconds);
            bool safe=profile!=null&&profile.CrowdNeighborCap>=0&&profile.CrowdNeighborCap<=NeighborCapacity&&Finite(dt)&&dt>0&&Finite(horizon)&&horizon>0;
            for(int i=0;i<bodies.Length&&safe;i++){
                var body=bodies[i];
                safe=Finite(body.Position.X)&&Finite(body.Position.Z)&&Finite(body.Radius)&&body.Radius>=0&&(!(body.Active||body.Potential)||Finite(originalSpeeds[i])&&originalSpeeds[i]>=0);
            }
            for(int root=0;root<bodies.Length;root++)if(labels[root]==root){
                bool overflow=false;for(int i=0;i<bodies.Length;i++)if(labels[i]==root&&(bodies[i].Active||bodies[i].Potential)&&counts[i]<0){overflow=true;break;}
                if(!overflow)continue;
                if(!safe||!TryComponentRatio(bodies,neighbors,labels,originalSpeeds,profile,dt,root,out double ratio))continue;
                bool scaleSafe=true;
                for(int i=0;i<bodies.Length;i++)if(labels[i]==root&&(bodies[i].Active||bodies[i].Potential)&&!TryScaledDown(originalSpeeds[i],ratio,out _)){scaleSafe=false;break;}
                if(!scaleSafe)continue;
                for(int i=0;i<bodies.Length;i++)if(labels[i]==root&&(bodies[i].Active||bodies[i].Potential))bodies[i].Speed=ScaledDown(originalSpeeds[i],ratio);
            }
            // The row scratch was used transiently by the bounded order statistic.
            // Rebuild every exact row once after all initial components were handled.
            Neighbors(bodies,neighbors,counts,profile,dt);
            bool restored=false;
            for(int root=0;root<bodies.Length;root++)if(labels[root]==root){
                bool overflow=false;for(int i=0;i<bodies.Length;i++)if(labels[i]==root&&(bodies[i].Active||bodies[i].Potential)&&counts[i]<0){overflow=true;break;}
                if(!overflow)continue;
                for(int i=0;i<bodies.Length;i++)if(labels[i]==root&&(bodies[i].Active||bodies[i].Potential))bodies[i].Speed=originalSpeeds[i];
                restored=true;
            }
            if(restored)Neighbors(bodies,neighbors,counts,profile,dt);
        }
        private static bool TryComponentRatio(NavMotionBody[] bodies,int[] neighbors,int[] labels,double[] originalSpeeds,NavigationProfile profile,double dt,int root,out double ratio)
        {
            ratio=1;bool found=false;
            for(int row=0;row<bodies.Length;row++)if(labels[row]==root&&(bodies[row].Active||bodies[row].Potential)){
                if(!TryRowRatio(bodies,neighbors,originalSpeeds,profile,dt,row,out double rowRatio))return false;
                ratio=Math.Min(ratio,rowRatio);found=true;
            }
            return found;
        }
        private static bool TryRowRatio(NavMotionBody[] bodies,int[] neighbors,double[] originalSpeeds,NavigationProfile profile,double dt,int row,out double ratio)
        {
            ratio=1;int cap=profile.CrowdNeighborCap,permanent=0;
            for(int peer=0;peer<bodies.Length;peer++)if(peer!=row&&NeighborAtScale(bodies,originalSpeeds,row,peer,0,profile,dt))permanent++;
            if(permanent>cap)return false;
            int kth=cap+1-permanent,removable=bodies.Length-1-permanent;
            if(kth>removable)return true;
            int keep=kth-1,kept=0,threshold=-1,offset=row*NeighborCapacity;
            for(int peer=0;peer<bodies.Length;peer++)if(peer!=row){
                if(NeighborAtScale(bodies,originalSpeeds,row,peer,0,profile,dt))continue;
                if(!TryPairBoundary(bodies,originalSpeeds,row,peer,profile,dt,out double boundary))return false;
                double thresholdBoundary=0;
                if(threshold>=0&&!TryPairBoundary(bodies,originalSpeeds,row,threshold,profile,dt,out thresholdBoundary))return false;
                if(keep==0){
                    if(threshold<0||CompareBoundary(boundary,peer,thresholdBoundary,threshold,bodies)<0)threshold=peer;
                    continue;
                }
                int last=kept<keep?-1:neighbors[offset+keep-1];double lastBoundary=0;
                if(last>=0&&!TryPairBoundary(bodies,originalSpeeds,row,last,profile,dt,out lastBoundary))return false;
                bool enters=last<0||CompareBoundary(boundary,peer,lastBoundary,last,bodies)<0;
                if(enters){
                    int evicted=last,insert=kept<keep?kept:keep-1;
                    while(insert>0){
                        int previous=neighbors[offset+insert-1];
                        if(!TryPairBoundary(bodies,originalSpeeds,row,previous,profile,dt,out double previousBoundary))return false;
                        if(CompareBoundary(boundary,peer,previousBoundary,previous,bodies)>=0)break;
                        if(insert<keep)neighbors[offset+insert]=previous;
                        insert--;
                    }
                    neighbors[offset+insert]=peer;if(kept<keep)kept++;
                    if(evicted>=0&&(threshold<0||CompareBoundary(lastBoundary,evicted,thresholdBoundary,threshold,bodies)<0))threshold=evicted;
                }else if(threshold<0||CompareBoundary(boundary,peer,thresholdBoundary,threshold,bodies)<0)threshold=peer;
            }
            if(threshold>=0){
                if(!TryPairBoundary(bodies,originalSpeeds,row,threshold,profile,dt,out ratio))return false;
            }
            return true;
        }
        private static int CompareBoundary(double a,int peerA,double b,int peerB,NavMotionBody[] bodies)
        {
            int c=a.CompareTo(b);if(c!=0)return c;c=bodies[peerA].Id.CompareTo(bodies[peerB].Id);return c!=0?c:peerA.CompareTo(peerB);
        }
        private static bool TryPairBoundary(NavMotionBody[] bodies,double[] originalSpeeds,int row,int peer,NavigationProfile profile,double dt,out double boundary)
        {
            boundary=0;
            if(NeighborAtScale(bodies,originalSpeeds,row,peer,0,profile,dt))return false;
            var a=bodies[row];var b=bodies[peer];double wi=a.Active||a.Potential?originalSpeeds[row]:0,wj=b.Active||b.Potential?originalSpeeds[peer]:0;
            var separation=Sub(a.Position,b.Position);double d2=separation.X*separation.X+separation.Z*separation.Z;
            double radius=a.Radius+b.Radius,horizon=Math.Max(dt,profile.CrowdLookaheadSeconds),speedSum=wi+wj;
            if(!Finite(d2)||d2<0||!Finite(radius)||!Finite(horizon)||horizon<=0)return false;
            if(!Finite(speedSum)||speedSum<0)return false;
            if(speedSum==0){boundary=1;return true;}
            double distanceInput=Math.Max(0,NextDown(d2));double root=Math.Sqrt(distanceInput),lowerDistanceRaw=NextDown(root);
            if(!Finite(distanceInput)||!Finite(root)||!Finite(lowerDistanceRaw))return false;
            double lowerDistance=Math.Max(0,lowerDistanceRaw),lowerGapRaw=NextDown(lowerDistance-radius);
            if(!Finite(lowerGapRaw))return false;
            double lowerGap=Math.Max(0,lowerGapRaw),lowerSpeedSum=NextUp(speedSum);
            if(!Finite(lowerSpeedSum)||lowerSpeedSum<=0)return false;
            double lowerReachRaw=NextDown(lowerGap/horizon);
            if(!Finite(lowerReachRaw))return false;
            double lowerReachSpeed=Math.Max(0,lowerReachRaw),quotientRaw=NextDown(lowerReachSpeed/lowerSpeedSum);
            if(!Finite(quotientRaw))return false;
            double quotient=quotientRaw;
            double candidate=Math.Max(0,Math.Min(1,quotient));
            if(!NeighborAtScale(bodies,originalSpeeds,row,peer,candidate,profile,dt)){boundary=candidate;return true;}
            // q=0 was checked false above, so zero is the only fixed safe fallback.
            boundary=0;return true;
        }
        private static bool NeighborAtScale(NavMotionBody[] bodies,double[] originalSpeeds,int row,int peer,double ratio,NavigationProfile profile,double dt)
        {
            // The exact predicate must see temporary scaled bodies only. Inactive
            // peers and rows in other components remain byte-for-byte untouched.
            var a=bodies[row];var b=bodies[peer];
            a.Speed=a.Active||a.Potential?ScaledDown(originalSpeeds[row],ratio):0;
            b.Speed=b.Active||b.Potential?ScaledDown(originalSpeeds[peer],ratio):0;
            return NeighborRelevant(a,b,profile,dt);
        }
        private static bool TryScaledDown(double speed,double ratio,out double result)
        {
            result=0;if(!Finite(speed)||speed<0||!Finite(ratio)||ratio<0||ratio>1)return false;
            if(ratio==0||speed==0)return true;
            if(ratio==1){result=speed;return true;}
            double product=speed*ratio;if(!Finite(product)||product<0)return false;
            result=Math.Max(0,NextDown(product));return Finite(result);
        }
        private static double ScaledDown(double speed,double ratio)
        {TryScaledDown(speed,ratio,out double result);return result;}
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        private static double NextDown(double value)
        {
            if(double.IsNaN(value)||value==double.NegativeInfinity)return value;if(value==0)return -double.Epsilon;
            long bits=BitConverter.DoubleToInt64Bits(value);return BitConverter.Int64BitsToDouble(bits+(value>0?-1:1));
        }
        private static double NextUp(double value)
        {
            if(double.IsNaN(value)||value==double.PositiveInfinity)return value;if(value==0)return double.Epsilon;
            long bits=BitConverter.DoubleToInt64Bits(value);return BitConverter.Int64BitsToDouble(bits+(value>0?1:-1));
        }
        private static int Root(int[] labels,int row)
        {int root=row;while(labels[root]!=root)root=labels[root];while(labels[row]!=row){int next=labels[row];labels[row]=root;row=next;}return root;}
        private static double Risk(NavMotionBody[] bodies,int row,NavPoint velocity,int[] neighbors,int count,double horizon)
        {
            double risk=0;var a=bodies[row];for(int k=0;k<count;k++){
                var b=bodies[neighbors[row*NeighborCapacity+k]];double r=a.Radius+b.Radius;
                double gap=ClosestSquared(Sub(a.Position,b.Position),Sub(velocity,Preferred(b)),horizon);
                risk=Math.Max(risk,Math.Max(0,1-gap/(r*r)));
            }return risk;
        }
        internal static NavMotionProposal Select(NavMotionBody[] bodies,int row,int[] neighbors,int count,NavigationProfile profile,double dt,INavMotionClearance clearance)
            =>Select(bodies,row,neighbors,count,profile,dt,clearance,null);
        // The crowd supplies the assigned goal before Joint. Diagnostic sampler
        // callers without an assigned goal receive no avoidance certificate.
        internal static NavMotionProposal Select(NavMotionBody[] bodies,int row,int[] neighbors,int count,NavigationProfile profile,double dt,INavMotionClearance clearance,NavPoint? assignedGoal)
        {
            var a=bodies[row];var result=new NavMotionProposal{Position=a.Position,Heading=a.Heading};
            if(!a.Active||a.Speed<=0)return result;
            if(count<0){result.Overflow=true;return result;}
            var delta=Sub(a.Target,a.Position);double distance=Length(delta),aim=Math.Atan2(delta.Z,delta.X),limit=a.TurnSpeed*dt;
            double turning=Turn(a.Heading,aim,limit);result.Heading=turning;
            var preferred=Preferred(a);double bestRisk=Risk(bodies,row,default,neighbors,count,profile.CrowdLookaheadSeconds),bestError=a.Speed*a.Speed;
            // Squared-velocity errors need a scale-relative comparison at tiny
            // composed speeds; retain the existing tolerance at normal speeds.
            double errorEpsilon=Math.Min(1e-12,a.Speed*a.Speed*1e-12);
            bool futureStaticMove=false,won=false;
            // Steering aims include an explicit turn-in-place path to an
            // avoidance direction. Its eventual velocity is only a heuristic;
            // the proposal still executes bounded turn, then aligned forward.
            // Rank the normal choices first. If prediction alone prefers a
            // wait or non-improving retreat, rank the same reachable improving choices
            // without treating wait's forecast as a physical safety veto.
            // Full-dt terrain and joint sweeps below remain authoritative.
            for(int pass=0;pass<2;pass++){
            if(pass==1){if(won&&bestError<a.Speed*a.Speed-errorEpsilon)break;bestRisk=double.PositiveInfinity;bestError=a.Speed*a.Speed;}
            for(int direction=-2;direction<profile.CrowdHeadingIntervals;direction++){
                double steering=direction==-2?a.Heading:direction==-1?aim:Angle(aim-Math.PI+2*Math.PI*direction/profile.CrowdHeadingIntervals);
                double heading=Turn(a.Heading,steering,limit);
                bool aligned=Math.Abs(Angle(steering-heading))<=profile.ForwardAlignmentAngle;
                for(int band=1;band<=profile.CrowdSpeedBands;band++){
                    double speed=Math.Min(a.Speed,distance/dt)*band/profile.CrowdSpeedBands;
                    var eventual=Velocity(steering,speed);var velocity=aligned?Velocity(heading,speed):default;
                    var end=new NavPoint(a.Position.X+velocity.X*dt,a.Position.Z+velocity.Z*dt);
                    var futureEnd=new NavPoint(a.Position.X+eventual.X*dt,a.Position.Z+eventual.Z*dt);
                    bool futureAvailable=false;
                    if(!aligned||!futureStaticMove)futureAvailable=clearance.ClearMotion(row,futureEnd)&&StaticPeersClear(bodies,row,neighbors,count,eventual,dt);
                    if(futureAvailable)futureStaticMove=true;
                    if(!aligned&&!futureAvailable)continue;
                    if(!clearance.ClearMotion(row,end))continue;
                    double risk=Risk(bodies,row,eventual,neighbors,count,profile.CrowdLookaheadSeconds);
                    double ex=eventual.X-preferred.X,ez=eventual.Z-preferred.Z,error=ex*ex+ez*ez;
                    if(pass==1&&error>=a.Speed*a.Speed-errorEpsilon)continue;
                    // Stable clockwise enumeration breaks an exact reciprocal tie.
                    if(risk>bestRisk+1e-12||Math.Abs(risk-bestRisk)<=1e-12&&error>=bestError-errorEpsilon)continue;
                    bestRisk=risk;bestError=error;result.Position=end;result.Velocity=velocity;result.Heading=heading;won=true;
                    // Set on EVERY winner replacement, using this winner's own
                    // futureAvailable, not the accumulated futureStaticMove.
                    result.IndependentTurn=assignedGoal.HasValue&&!aligned&&futureAvailable&&velocity.Equals(default(NavPoint))&&!heading.Equals(a.Heading)&&ImprovesGoal(a.Position,futureEnd,assignedGoal.Value);
                }
            }
            }
            // A turn is a voluntary wait only when some eventual translated
            // endpoint is physically available. Enclosure keeps the old timeout.
            result.Blocked=!futureStaticMove;
            bool activeNeighbor=false;for(int k=0;k<count;k++)if(bodies[neighbors[row*NeighborCapacity+k]].Active){activeNeighbor=true;break;}
            result.StaticNoWinner=!won&&turning.Equals(a.Heading)&&!result.Blocked&&!activeNeighbor;
            return result;
        }
        internal static bool ImprovesGoal(NavPoint from,NavPoint to,NavPoint goal)
        {
            if(!Finite(from.X)||!Finite(from.Z)||!Finite(to.X)||!Finite(to.Z)||!Finite(goal.X)||!Finite(goal.Z))return false;
            double bx=goal.X-from.X,bz=goal.Z-from.Z,ax=goal.X-to.X,az=goal.Z-to.Z,before=bx*bx+bz*bz,after=ax*ax+az*az;
            return Finite(before)&&Finite(after)&&after<before;
        }
        private static bool StaticPeersClear(NavMotionBody[] bodies,int row,int[] neighbors,int count,NavPoint velocity,double dt)
        {
            var a=bodies[row];for(int k=0;k<count;k++){var b=bodies[neighbors[row*NeighborCapacity+k]];if(b.Active)continue;double r=a.Radius+b.Radius;if(ClosestSquared(Sub(a.Position,b.Position),velocity,dt)<r*r-1e-9)return false;}return true;
        }
        internal static void ResolveJoint(NavMotionBody[] bodies,NavMotionProposal[] proposals,double dt)
        {
            // Peer refusal preserves Select's physical-blockage classification;
            // a wait for an active peer alone never starts collision repair.
            // Refusal changes a moving hazard into a stationary one. At most N
            // trajectories can be refused, so the fixed point is finite.
            bool changed;do{changed=false;for(int i=0;i<bodies.Length;i++)for(int j=i+1;j<bodies.Length;j++){
                double r=bodies[i].Radius+bodies[j].Radius;
                var separation=Sub(bodies[i].Position,bodies[j].Position);var vi=proposals[i].Velocity;var vj=proposals[j].Velocity;
                if(ClosestSquared(separation,Sub(vi,vj),dt)>=r*r-1e-9)continue;
                bool movingI=Length(vi)>0,movingJ=Length(vj)>0;
                if(movingI&&movingJ){
                    bool aloneI=ClosestSquared(separation,vi,dt)>=r*r-1e-9,aloneJ=ClosestSquared(separation,Sub(default,vj),dt)>=r*r-1e-9;
                    // Keep an already terrain-validated proposal only when the
                    // other footprint can wait safely. Stable entity identity
                    // resolves a reciprocal tie, never the frame row order.
                    if(aloneI&&(!aloneJ||bodies[i].Id<bodies[j].Id)){proposals[j].Refuse=true;continue;}
                    if(aloneJ){proposals[i].Refuse=true;continue;}
                }
                if(movingI)proposals[i].Refuse=true;
                if(movingJ)proposals[j].Refuse=true;
            }
            for(int i=0;i<bodies.Length;i++)if(proposals[i].Refuse){proposals[i].Refuse=false;proposals[i].Position=bodies[i].Position;proposals[i].Velocity=default;changed=true;}
            }while(changed);
        }
    }
}
