using System;
namespace Spacewars.Simulation
{
    public sealed partial class NavCrowd
    {
        // Derived from ready producers on the owning simulation thread. Copied
        // into prepared movement; no new persistent intent or route replacement.
        internal Func<NavUnit,NavPoint?> ProductionEscape;
        private bool ProductionSweep(int row,NavPoint end)
        {
            var actor=unitView[row];
            return geometry.SegmentFree(actor.Position,end,actor.Radius)&&Traverse(actor.Location,end,actor.Radius,out _);
        }
        private void PrepareProductionClearance(double dt,NavMotionBody[] bodies,int[] queue,int[] marks)
        {
            if(ProductionEscape==null)return;
            for(int root=0;root<bodies.Length;root++)
            {
                if(bodies[root].YieldLabel!=0)continue;
                var escape=ProductionEscape(unitView[root]);if(!escape.HasValue)continue;
                double angle=Math.Atan2(escape.Value.Z,escape.Value.X);
                // Reuse the profile's directional resolution. The half-circle
                // search always increases distance from the producing building.
                int directions=profile.CrowdHeadingIntervals;
                for(int sample=0;sample<directions;sample++)
                {
                    double offset=(sample%2==0?1:-1)*((sample+1)/2)*Math.PI/directions;
                    var direction=new NavPoint(Math.Cos(angle+offset),Math.Sin(angle+offset));
                    double step=bodies[root].Speed*dt;if(!(step>0))break;
                    var delta=new NavPoint(direction.X*step,direction.Z*step);
                    if(!ProducerClearanceRules.TryChain(bodies,root,delta,queue,marks,ProductionSweep,out int count))continue;
                    bool canMove=true,aligned=true;
                    for(int i=0;i<count;i++)
                    {
                        var body=bodies[queue[i]];step=Math.Min(step,body.Speed*dt);
                        if(!(body.Speed>0))canMove=false;
                        double heading=Turn(body.Heading,angle+offset,body.TurnSpeed*dt);
                        if(Math.Abs(Normalize(angle+offset-heading))>1e-9)aligned=false;
                    }
                    if(!canMove)continue;
                    for(int i=0;i<count;i++)
                    {
                        int row=queue[i];var body=bodies[row];
                        body.Active=body.Yielding=true;body.Potential=false;body.YieldLabel=-root-1;
                        body.Target=new NavPoint(body.Position.X+direction.X*step,body.Position.Z+direction.Z*step);
                        // All-or-none translation while members turn at their
                        // normal rate; route cursors, Hold and order survive.
                        body.Speed=aligned?step/dt:0;bodies[row]=body;
                    }
                    break;
                }
            }
        }
        private static void ProposeProductionClearance(double dt,NavMotionBody[] bodies,NavMotionProposal[] proposals,int[] neighborCounts)
        {
            for(int i=0;i<bodies.Length;i++)
            {
                var body=bodies[i];if(body.YieldLabel>=0)continue;
                var delta=new NavPoint(body.Target.X-body.Position.X,body.Target.Z-body.Position.Z);
                double heading=Turn(body.Heading,Math.Atan2(delta.Z,delta.X),body.TurnSpeed*dt);
                double speed=neighborCounts[i]<0?0:body.Speed;
                // Overflow contraction remains authoritative. A release chain
                // uses its slowest contracted member, and never bypasses q9.
                for(int peer=0;peer<bodies.Length;peer++)if(bodies[peer].YieldLabel==body.YieldLabel)speed=Math.Min(speed,neighborCounts[peer]<0?0:bodies[peer].Speed);
                double distance=Math.Sqrt(delta.X*delta.X+delta.Z*delta.Z),step=Math.Min(distance,speed*dt);
                bool moving=step>0&&distance>0;
                if(moving)delta=new NavPoint(delta.X*step/distance,delta.Z*step/distance);
                proposals[i]=new NavMotionProposal{Heading=heading,Position=moving?new NavPoint(body.Position.X+delta.X,body.Position.Z+delta.Z):body.Position,Velocity=moving?new NavPoint(delta.X/dt,delta.Z/dt):default};
            }
        }
    }
}
