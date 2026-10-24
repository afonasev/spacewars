using System;

namespace Spacewars.Simulation
{
    internal enum NavRectangleProgress { Pending, Ready, Skipped }
    internal struct NavRectangleCursor
    {
        internal int Obstacle;
        internal NavExclusionCursor Work;
    }
    // A single-descriptor absence proof. Capacity selection V1 is unchanged:
    // these private generous bounds permit E=0, and are never a capacity claim.
    internal static class NavigationRectangleRangeProof
    {
        internal static NavExclusionInput Input(in NavExclusionInput i)=>new NavExclusionInput(i.Obstacles,i.ProviderIdentity,i.GeometryIdentity,
            i.GridCell,i.MinXIndex,i.MaxXIndex,i.MinZIndex,i.MaxZIndex,i.Samples,i.MaxSamples,i.Samples,checked(4*i.Samples),i.Samples);
        internal static NavRectangleCursor StartRectangle(in NavExclusionInput input,int obstacle)
        {
            var i=Input(input);
            if(obstacle<0||obstacle>=i.Obstacles.Count)throw new ArgumentOutOfRangeException(nameof(obstacle));
            var c=NavigationRectangularExclusionCertificate.Start(i);c.ObstacleIndex=obstacle;
            return new NavRectangleCursor{Obstacle=obstacle,Work=c};
        }
        internal static NavRectangleProgress Progress(in NavRectangleCursor c)=>c.Work.ObstacleIndex!=c.Obstacle?NavRectangleProgress.Skipped:
            c.Work.Stage==NavExclusionStage.Complete?NavRectangleProgress.Ready:NavRectangleProgress.Pending;
        internal static NavRectangleProgress StepRectangle(in NavExclusionInput input,ref NavRectangleCursor cursor,int budget,out int consumed,out NavExclusionCertificate range)
        {
            consumed=0;range=cursor.Work.TerminalCertificate;
            if(budget<0)throw new ArgumentOutOfRangeException(nameof(budget));
            if(budget==0)return Progress(cursor);
            if(!ValidateRectangle(input,cursor))throw new ArgumentException("Invalid single-rectangle cursor.");
            if(Progress(cursor)!=NavRectangleProgress.Pending)return Progress(cursor);
            // One descriptor or one binary iteration, even for a larger caller budget.
            NavigationRectangularExclusionCertificate.Step(Input(input),ref cursor.Work,1,out consumed,out range);
            return Progress(cursor);
        }
        internal static bool ValidateRectangle(in NavExclusionInput input,in NavRectangleCursor cursor)
        {
            if(cursor.Obstacle<0||cursor.Obstacle>=input.Obstacles.Count)return false;
            var c=cursor.Work;
            if(!NavigationRectangularExclusionCertificate.Validate(Input(input),c,c.TerminalCertificate))return false;
            if(c.ObstacleIndex==cursor.Obstacle)return c.TerminalProgress!=NavExclusionProgress.Insufficient;
            if(c.ObstacleIndex!=cursor.Obstacle+1||c.Stage!=NavExclusionStage.Descriptor||c.PaidOperations!=1)return false;
            var o=input.Obstacles[cursor.Obstacle];
            return o.Polygon!=null||o.CircleRadius!=0||!Finite(o.MinX)||!Finite(o.MaxX)||!Finite(o.MinZ)||!Finite(o.MaxZ)||o.MinX>o.MaxX||o.MinZ>o.MaxZ;
        }
        private static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
    }
}
