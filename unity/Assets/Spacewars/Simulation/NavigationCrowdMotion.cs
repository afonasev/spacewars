using System;

namespace Spacewars.Simulation
{
    internal struct NavMotionBody
    {
        internal int Id;
        internal NavPoint Position,Target;
        internal double Heading,Radius,Speed,TurnSpeed;
        internal bool Active;
    }
    internal struct NavMotionProposal
    {
        internal NavPoint Position,Velocity;
        internal double Heading;
        internal bool Blocked,Overflow,Refuse;
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
        internal static void Neighbors(NavMotionBody[] bodies,int[] neighbors,int[] counts,NavigationProfile profile,double dt)
        {
            for(int i=0;i<bodies.Length;i++){
                int count=0;for(int j=0;j<bodies.Length;j++)if(i!=j){
                    double reach=bodies[i].Radius+bodies[j].Radius+(bodies[i].Speed+bodies[j].Speed)*Math.Max(dt,profile.CrowdLookaheadSeconds);
                    var d=Sub(bodies[i].Position,bodies[j].Position);if(d.X*d.X+d.Z*d.Z>reach*reach)continue;
                    if(count==profile.CrowdNeighborCap){count=-1;break;}neighbors[i*NeighborCapacity+count++]=j;
                }counts[i]=count;
            }
        }
        private static double Risk(NavMotionBody[] bodies,int row,NavPoint velocity,int[] neighbors,int count,double horizon)
        {
            double risk=0;var a=bodies[row];for(int k=0;k<count;k++){
                var b=bodies[neighbors[row*NeighborCapacity+k]];double r=a.Radius+b.Radius;
                double gap=ClosestSquared(Sub(a.Position,b.Position),Sub(velocity,Preferred(b)),horizon);
                risk=Math.Max(risk,Math.Max(0,1-gap/(r*r)));
            }return risk;
        }
        internal static NavMotionProposal Select(NavMotionBody[] bodies,int row,int[] neighbors,int count,NavigationProfile profile,double dt,INavMotionClearance clearance)
        {
            var a=bodies[row];var result=new NavMotionProposal{Position=a.Position,Heading=a.Heading};
            if(!a.Active||a.Speed<=0)return result;
            if(count<0){result.Overflow=true;return result;}
            var delta=Sub(a.Target,a.Position);double distance=Length(delta),aim=Math.Atan2(delta.Z,delta.X),limit=a.TurnSpeed*dt;
            double turning=Turn(a.Heading,aim,limit);result.Heading=turning;
            var preferred=Preferred(a);double bestRisk=Risk(bodies,row,default,neighbors,count,profile.CrowdLookaheadSeconds),bestError=a.Speed*a.Speed;
            bool futureStaticMove=false;
            // Steering aims include an explicit turn-in-place path to an
            // avoidance direction. Its eventual velocity is only a heuristic;
            // the proposal still executes bounded turn, then aligned forward.
            for(int direction=-2;direction<profile.CrowdHeadingIntervals;direction++){
                double steering=direction==-2?a.Heading:direction==-1?aim:Angle(aim-Math.PI+2*Math.PI*direction/profile.CrowdHeadingIntervals);
                double heading=Turn(a.Heading,steering,limit);
                bool aligned=Math.Abs(Angle(steering-heading))<=profile.ForwardAlignmentAngle;
                for(int band=1;band<=profile.CrowdSpeedBands;band++){
                    double speed=Math.Min(a.Speed,distance/dt)*band/profile.CrowdSpeedBands;
                    var eventual=Velocity(steering,speed);var velocity=aligned?Velocity(heading,speed):default;
                    var end=new NavPoint(a.Position.X+velocity.X*dt,a.Position.Z+velocity.Z*dt);
                    var futureEnd=new NavPoint(a.Position.X+eventual.X*dt,a.Position.Z+eventual.Z*dt);
                    if(!futureStaticMove&&clearance.ClearMotion(row,futureEnd)&&StaticPeersClear(bodies,row,neighbors,count,eventual,dt))futureStaticMove=true;
                    if(!clearance.ClearMotion(row,end))continue;
                    double risk=Risk(bodies,row,eventual,neighbors,count,profile.CrowdLookaheadSeconds);
                    double ex=eventual.X-preferred.X,ez=eventual.Z-preferred.Z,error=ex*ex+ez*ez;
                    // Stable clockwise enumeration breaks an exact reciprocal tie.
                    if(risk>bestRisk+1e-12||Math.Abs(risk-bestRisk)<=1e-12&&error>=bestError-1e-12)continue;
                    bestRisk=risk;bestError=error;result.Position=end;result.Velocity=velocity;result.Heading=heading;
                }
            }
            // A turn is a voluntary wait only when some eventual translated
            // endpoint is physically available. Enclosure keeps the old timeout.
            result.Blocked=!futureStaticMove;return result;
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
                if(ClosestSquared(Sub(bodies[i].Position,bodies[j].Position),Sub(proposals[i].Velocity,proposals[j].Velocity),dt)>=r*r-1e-9)continue;
                if(Length(proposals[i].Velocity)>0)proposals[i].Refuse=true;
                if(Length(proposals[j].Velocity)>0)proposals[j].Refuse=true;
            }
            for(int i=0;i<bodies.Length;i++)if(proposals[i].Refuse){proposals[i].Refuse=false;proposals[i].Position=bodies[i].Position;proposals[i].Velocity=default;changed=true;}
            }while(changed);
        }
    }
}
