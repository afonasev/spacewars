using System;
namespace Spacewars.Simulation
{
    // Shared release-only policy. Team, Hold and ordinary cooperative-yield
    // eligibility deliberately do not participate. No damage or position writes.
    internal static class ProducerClearanceRules
    {
        internal static bool TryChain(NavMotionBody[] bodies,int root,NavPoint delta,int[] queue,int[] marks,Func<int,NavPoint,bool> clear,out int count)
        {
            count=1;for(int i=0;i<marks.Length;i++)marks[i]=0;
            marks[root]=1;queue[0]=root;
            for(int head=0;head<count;head++)
            {
                int row=queue[head];var body=bodies[row];
                if(body.YieldLabel!=0)return false;
                var end=new NavPoint(body.Position.X+delta.X,body.Position.Z+delta.Z);
                if(!clear(row,end))return false;
                for(int peer=0;peer<bodies.Length;peer++)
                {
                    if(marks[peer]!=0)continue;
                    double radius=body.Radius+bodies[peer].Radius;
                    // A swept disc, not endpoint occupancy: include every body
                    // encountered along the displacement, including long chains.
                    if(SegmentSquared(bodies[peer].Position,body.Position,end)>=radius*radius-1e-9)continue;
                    marks[peer]=1;queue[count++]=peer;
                }
            }
            // Every internal pair translates by exactly the same vector; its
            // separation is invariant. Every external pair passed the full sweep.
            return true;
        }
        private static double SegmentSquared(NavPoint p,NavPoint a,NavPoint b)
        {
            double x=b.X-a.X,z=b.Z-a.Z,length=x*x+z*z;
            double t=length==0?0:Math.Max(0,Math.Min(1,((p.X-a.X)*x+(p.Z-a.Z)*z)/length));
            x=p.X-a.X-t*x;z=p.Z-a.Z-t*z;return x*x+z*z;
        }
    }
}
