using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    internal enum NavExclusionProgress { Pending, Certified, Insufficient }

    internal enum NavExclusionStage { Descriptor, MinX, MaxX, MinZ, MaxZ, Complete }

    // Caller pins Obstacles and keeps that exact provider/geometry alive for the
    // lifetime of this input. The helper deliberately never copies or constructs it.
    internal readonly struct NavExclusionInput
    {
        internal readonly IReadOnlyList<NavObstacle> Obstacles;
        internal readonly string ProviderIdentity, GeometryIdentity;
        internal readonly double GridCell;
        internal readonly long MinXIndex, MaxXIndex, MinZIndex, MaxZIndex, Samples;
        internal readonly long MaxSamples, MaxNodes, MaxArcs, MaxLabels;

        internal NavExclusionInput(IReadOnlyList<NavObstacle> obstacles, string providerIdentity,
            string geometryIdentity, double gridCell, long minXIndex, long maxXIndex,
            long minZIndex, long maxZIndex, long samples, long maxSamples,
            long maxNodes, long maxArcs, long maxLabels)
        {
            Obstacles=obstacles;ProviderIdentity=providerIdentity;GeometryIdentity=geometryIdentity;
            GridCell=gridCell;MinXIndex=minXIndex;MaxXIndex=maxXIndex;MinZIndex=minZIndex;MaxZIndex=maxZIndex;
            Samples=samples;MaxSamples=maxSamples;MaxNodes=maxNodes;MaxArcs=maxArcs;MaxLabels=maxLabels;
        }
    }

    // Plain explicit continuation state. Binary-search [Low, High) and the
    // candidate descriptor are persisted by the caller as ordinary data.
    internal struct NavExclusionCursor
    {
        internal string ProviderIdentity, GeometryIdentity;
        internal double GridCell;
        internal long MinXIndex, MaxXIndex, MinZIndex, MaxZIndex, Samples;
        internal long MaxSamples, MaxNodes, MaxArcs, MaxLabels;
        internal int ObstacleIndex;
        internal NavExclusionStage Stage;
        internal long Low, High;
        internal double CandidateMinX, CandidateMaxX, CandidateMinZ, CandidateMaxZ;
        internal long XMin, XMax, ZMin, ZMax;
        internal long PaidOperations;
        internal NavExclusionProgress TerminalProgress;
        internal NavExclusionCertificate TerminalCertificate;
    }

    internal struct NavExclusionCertificate
    {
        internal string ProviderIdentity, GeometryIdentity;
        internal double GridCell, MinX, MaxX, MinZ, MaxZ;
        internal long MinXIndex, MaxXIndex, MinZIndex, MaxZIndex;
        internal long Samples, ExcludedCount, RemainingCount;
        internal long MaxSamples, MaxNodes, MaxArcs, MaxLabels;
        internal int ObstacleIndex;
    }

    internal static class NavigationRectangularExclusionCertificate
    {
        internal static NavExclusionCursor Start(in NavExclusionInput input)
        {
            if(!ValidInput(input))throw new ArgumentException("Invalid exclusion input.",nameof(input));
            var cursor=new NavExclusionCursor {
                ProviderIdentity=input.ProviderIdentity,GeometryIdentity=input.GeometryIdentity,GridCell=input.GridCell,
                MinXIndex=input.MinXIndex,MaxXIndex=input.MaxXIndex,MinZIndex=input.MinZIndex,MaxZIndex=input.MaxZIndex,
                Samples=input.Samples,MaxSamples=input.MaxSamples,MaxNodes=input.MaxNodes,MaxArcs=input.MaxArcs,MaxLabels=input.MaxLabels,
                Stage=NavExclusionStage.Descriptor
            };
            if(input.Samples>input.MaxSamples){cursor.Stage=NavExclusionStage.Complete;cursor.TerminalProgress=NavExclusionProgress.Insufficient;}
            return cursor;
        }

        internal static NavExclusionProgress Step(in NavExclusionInput input, ref NavExclusionCursor cursor,
            int operationBudget, out int consumed, out NavExclusionCertificate certificate)
        {
            consumed=0;certificate=default;
            if(operationBudget<0)throw new ArgumentOutOfRangeException(nameof(operationBudget));
            if(operationBudget==0)
            {
                if(cursor.Stage==NavExclusionStage.Complete){certificate=cursor.TerminalCertificate;return cursor.TerminalProgress;}
                return NavExclusionProgress.Pending;
            }
            if(!Matches(input,cursor)||!ValidInput(input)||!ValidCursor(input,cursor))
                throw new ArgumentException("Exclusion cursor does not match its pinned input.",nameof(cursor));
            if(cursor.Stage==NavExclusionStage.Complete){certificate=cursor.TerminalCertificate;return cursor.TerminalProgress;}

            while(consumed<operationBudget)
            {
                if(cursor.Stage==NavExclusionStage.Descriptor)
                {
                    if(cursor.ObstacleIndex>=input.Obstacles.Count){cursor.Stage=NavExclusionStage.Complete;cursor.Low=cursor.High=0;cursor.TerminalProgress=NavExclusionProgress.Insufficient;return cursor.TerminalProgress;}
                    NavObstacle obstacle=input.Obstacles[cursor.ObstacleIndex];
                    cursor.PaidOperations++;consumed++;
                    if(!Rectangle(obstacle)){cursor.ObstacleIndex++;continue;}
                    cursor.CandidateMinX=obstacle.MinX;cursor.CandidateMaxX=obstacle.MaxX;
                    cursor.CandidateMinZ=obstacle.MinZ;cursor.CandidateMaxZ=obstacle.MaxZ;
                    cursor.Stage=NavExclusionStage.MinX;cursor.Low=input.MinXIndex;cursor.High=input.MaxXIndex+1;
                    continue;
                }

                long mid=cursor.Low+(cursor.High-cursor.Low)/2;
                double product=(double)mid*input.GridCell;
                bool lower=cursor.Stage==NavExclusionStage.MinX||cursor.Stage==NavExclusionStage.MinZ;
                double bound=cursor.Stage==NavExclusionStage.MinX?cursor.CandidateMinX:
                    cursor.Stage==NavExclusionStage.MaxX?cursor.CandidateMaxX:
                    cursor.Stage==NavExclusionStage.MinZ?cursor.CandidateMinZ:cursor.CandidateMaxZ;
                bool after=lower?product>=bound:product>bound;
                if(after)cursor.High=mid;else cursor.Low=mid+1;
                cursor.PaidOperations++;consumed++;
                if(cursor.Low!=cursor.High)continue;

                long found=cursor.Low;
                switch(cursor.Stage)
                {
                    case NavExclusionStage.MinX:
                        cursor.XMin=found;cursor.Stage=NavExclusionStage.MaxX;ResetSearch(ref cursor,input.MinXIndex,input.MaxXIndex+1);break;
                    case NavExclusionStage.MaxX:
                        cursor.XMax=found-1;cursor.Stage=NavExclusionStage.MinZ;ResetSearch(ref cursor,input.MinZIndex,input.MaxZIndex+1);break;
                    case NavExclusionStage.MinZ:
                        cursor.ZMin=found;cursor.Stage=NavExclusionStage.MaxZ;ResetSearch(ref cursor,input.MinZIndex,input.MaxZIndex+1);break;
                    case NavExclusionStage.MaxZ:
                        cursor.ZMax=found-1;
                        if(TryCertificate(input,cursor,out certificate)){cursor.Stage=NavExclusionStage.Complete;cursor.Low=cursor.High=0;cursor.TerminalProgress=NavExclusionProgress.Certified;cursor.TerminalCertificate=certificate;return cursor.TerminalProgress;}
                        cursor.ObstacleIndex++;cursor.Stage=NavExclusionStage.Descriptor;cursor.Low=cursor.High=0;break;
                }
            }
            return NavExclusionProgress.Pending;
        }

        // Constant-work restore validation only: no descriptor scan, solver work,
        // provider calls, or cursor advancement. It also checks adjacent products
        // at each inclusive bound using the same binary64 k*GridCell operation.
        internal static bool Validate(in NavExclusionInput input, in NavExclusionCursor cursor,
            in NavExclusionCertificate certificate)
        {
            if(!ValidInput(input)||!Matches(input,cursor)||!ValidCursor(input,cursor))return false;
            if(cursor.Stage!=NavExclusionStage.Complete)
                return cursor.TerminalProgress==NavExclusionProgress.Pending&&cursor.TerminalCertificate.Equals(default(NavExclusionCertificate))&&certificate.Equals(default(NavExclusionCertificate));
            if(cursor.TerminalProgress!=NavExclusionProgress.Certified&&cursor.TerminalProgress!=NavExclusionProgress.Insufficient)return false;
            if(!CertificateEquals(cursor.TerminalCertificate,certificate))return false;
            if(certificate.Equals(default(NavExclusionCertificate)))
                return cursor.TerminalProgress==NavExclusionProgress.Insufficient&&
                    (input.Samples>input.MaxSamples?cursor.PaidOperations==0:cursor.ObstacleIndex==input.Obstacles.Count&&cursor.PaidOperations>=input.Obstacles.Count);
            if(cursor.TerminalProgress!=NavExclusionProgress.Certified)return false;
            if(cursor.ObstacleIndex!=certificate.ObstacleIndex)return false;
            if(!CertificateMatchesInput(input,certificate)||certificate.ObstacleIndex<0||certificate.ObstacleIndex>=input.Obstacles.Count)return false;
            if(cursor.XMin!=certificate.MinXIndex||cursor.XMax!=certificate.MaxXIndex||
               cursor.ZMin!=certificate.MinZIndex||cursor.ZMax!=certificate.MaxZIndex)return false;
            NavObstacle selected=input.Obstacles[certificate.ObstacleIndex];
            if(!Rectangle(selected)||selected.MinX!=certificate.MinX||selected.MaxX!=certificate.MaxX||selected.MinZ!=certificate.MinZ||selected.MaxZ!=certificate.MaxZ)return false;
            bool xIncluded=certificate.MinXIndex<=certificate.MaxXIndex,zIncluded=certificate.MinZIndex<=certificate.MaxZIndex;
            if(!ValidIntersectionAxis(certificate.MinXIndex,certificate.MaxXIndex,input.MinXIndex,input.MaxXIndex,input.GridCell,certificate.MinX,certificate.MaxX)||
               !ValidIntersectionAxis(certificate.MinZIndex,certificate.MaxZIndex,input.MinZIndex,input.MaxZIndex,input.GridCell,certificate.MinZ,certificate.MaxZ))return false;
            long area=0;
            if(xIncluded&&zIncluded)
                try{area=checked(checked(certificate.MaxXIndex-certificate.MinXIndex+1)*checked(certificate.MaxZIndex-certificate.MinZIndex+1));}
                catch(OverflowException){return false;}
            if(certificate.ExcludedCount<0||certificate.ExcludedCount>input.Samples||area!=certificate.ExcludedCount||
               certificate.RemainingCount!=input.Samples-certificate.ExcludedCount||certificate.RemainingCount<0)return false;
            if(!Fits(certificate.RemainingCount,input.MaxNodes,input.MaxArcs,input.MaxLabels))return false;
            if(!xIncluded||!zIncluded)return certificate.ExcludedCount==0;
            double x0=(double)certificate.MinXIndex*input.GridCell,x1=(double)certificate.MaxXIndex*input.GridCell;
            double z0=(double)certificate.MinZIndex*input.GridCell,z1=(double)certificate.MaxZIndex*input.GridCell;
            return Inside(x0,z0,certificate)&&Inside(x0,z1,certificate)&&Inside(x1,z0,certificate)&&Inside(x1,z1,certificate);
        }

        private static void ResetSearch(ref NavExclusionCursor cursor,long low,long high){cursor.Low=low;cursor.High=high;}
        private static bool TryCertificate(in NavExclusionInput input,in NavExclusionCursor cursor,out NavExclusionCertificate certificate)
        {
            certificate=default;
            bool xEmpty=cursor.XMin>cursor.XMax,zEmpty=cursor.ZMin>cursor.ZMax;
            long x0=cursor.XMin,x1=cursor.XMax,z0=cursor.ZMin,z1=cursor.ZMax;
            long excluded,remaining;
            try{excluded=xEmpty||zEmpty?0:checked(checked(x1-x0+1)*checked(z1-z0+1));remaining=checked(input.Samples-excluded);}
            catch(OverflowException){return false;}
            if(!Fits(remaining,input.MaxNodes,input.MaxArcs,input.MaxLabels))return false;
            certificate=new NavExclusionCertificate { ProviderIdentity=input.ProviderIdentity,GeometryIdentity=input.GeometryIdentity,
                GridCell=input.GridCell,MinX=cursor.CandidateMinX,MaxX=cursor.CandidateMaxX,MinZ=cursor.CandidateMinZ,MaxZ=cursor.CandidateMaxZ,
                MinXIndex=x0,MaxXIndex=x1,MinZIndex=z0,MaxZIndex=z1,Samples=input.Samples,ExcludedCount=excluded,RemainingCount=remaining,
                MaxSamples=input.MaxSamples,MaxNodes=input.MaxNodes,MaxArcs=input.MaxArcs,MaxLabels=input.MaxLabels,ObstacleIndex=cursor.ObstacleIndex };
            return true;
        }
        private static bool Fits(long remaining,long nodes,long arcs,long labels)=>remaining>=0&&remaining<=nodes&&remaining<=arcs/4&&remaining<=labels;
        private static bool Rectangle(NavObstacle obstacle)=>obstacle.Polygon==null&&obstacle.CircleRadius==0&&Finite(obstacle.MinX)&&Finite(obstacle.MinZ)&&Finite(obstacle.MaxX)&&Finite(obstacle.MaxZ)&&obstacle.MinX<=obstacle.MaxX&&obstacle.MinZ<=obstacle.MaxZ;
        private static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        private static bool ValidInput(in NavExclusionInput i)
        {
            if(i.Obstacles==null||string.IsNullOrEmpty(i.ProviderIdentity)||string.IsNullOrEmpty(i.GeometryIdentity)||!Finite(i.GridCell)||i.GridCell<=0)return false;
            if(i.MinXIndex==long.MinValue||i.MinZIndex==long.MinValue||i.MaxXIndex==long.MaxValue||i.MaxZIndex==long.MaxValue||i.MinXIndex>i.MaxXIndex||i.MinZIndex>i.MaxZIndex)return false;
            if(i.MaxSamples<0||i.MaxNodes<0||i.MaxArcs<0||i.MaxLabels<0||i.Samples<0)return false;
            try
            {
                long width=checked(i.MaxXIndex-i.MinXIndex+1),height=checked(i.MaxZIndex-i.MinZIndex+1);
                return checked(width*height)==i.Samples&&Finite((double)i.MinXIndex*i.GridCell)&&Finite((double)i.MaxXIndex*i.GridCell)&&
                    Finite((double)i.MinZIndex*i.GridCell)&&Finite((double)i.MaxZIndex*i.GridCell);
            }
            catch(OverflowException){return false;}
        }
        private static bool Matches(in NavExclusionInput i,in NavExclusionCursor c)=>c.ProviderIdentity==i.ProviderIdentity&&c.GeometryIdentity==i.GeometryIdentity&&c.GridCell.Equals(i.GridCell)&&
            c.MinXIndex==i.MinXIndex&&c.MaxXIndex==i.MaxXIndex&&c.MinZIndex==i.MinZIndex&&c.MaxZIndex==i.MaxZIndex&&c.Samples==i.Samples&&
            c.MaxSamples==i.MaxSamples&&c.MaxNodes==i.MaxNodes&&c.MaxArcs==i.MaxArcs&&c.MaxLabels==i.MaxLabels;
        private static bool ValidCursor(in NavExclusionInput input,in NavExclusionCursor c)
        {
            if(c.ObstacleIndex<0||c.ObstacleIndex>input.Obstacles.Count||c.PaidOperations<0||
               (int)c.Stage<(int)NavExclusionStage.Descriptor||(int)c.Stage>(int)NavExclusionStage.Complete)return false;
            if(c.Stage==NavExclusionStage.Descriptor)return c.Low==0&&c.High==0;
            if(c.Stage==NavExclusionStage.Complete)return c.Low==0&&c.High==0&&
                (c.TerminalProgress==NavExclusionProgress.Certified||c.TerminalProgress==NavExclusionProgress.Insufficient);
            if(c.ObstacleIndex>=input.Obstacles.Count||!Rectangle(input.Obstacles[c.ObstacleIndex]))return false;
            NavObstacle selected=input.Obstacles[c.ObstacleIndex];
            if(!Finite(c.CandidateMinX)||!Finite(c.CandidateMaxX)||!Finite(c.CandidateMinZ)||!Finite(c.CandidateMaxZ)||
                selected.MinX!=c.CandidateMinX||selected.MaxX!=c.CandidateMaxX||selected.MinZ!=c.CandidateMinZ||selected.MaxZ!=c.CandidateMaxZ)return false;
            if(c.Stage==NavExclusionStage.MinX)
                return ValidBracket(c.Low,c.High,input.MinXIndex,input.MaxXIndex,input.GridCell,c.CandidateMinX,true);
            if(c.Stage==NavExclusionStage.MaxX)
                return ValidLower(c.XMin,input.MinXIndex,input.MaxXIndex,input.GridCell,c.CandidateMinX)&&
                    ValidBracket(c.Low,c.High,input.MinXIndex,input.MaxXIndex,input.GridCell,c.CandidateMaxX,false);
            if(c.Stage==NavExclusionStage.MinZ)
                return ValidIntersectionAxis(c.XMin,c.XMax,input.MinXIndex,input.MaxXIndex,input.GridCell,c.CandidateMinX,c.CandidateMaxX)&&
                    ValidBracket(c.Low,c.High,input.MinZIndex,input.MaxZIndex,input.GridCell,c.CandidateMinZ,true);
            return ValidIntersectionAxis(c.XMin,c.XMax,input.MinXIndex,input.MaxXIndex,input.GridCell,c.CandidateMinX,c.CandidateMaxX)&&
                ValidLower(c.ZMin,input.MinZIndex,input.MaxZIndex,input.GridCell,c.CandidateMinZ)&&
                ValidBracket(c.Low,c.High,input.MinZIndex,input.MaxZIndex,input.GridCell,c.CandidateMaxZ,false);
        }
        private static bool ValidLower(long index,long domainMin,long domainMax,double grid,double bound)
        {
            if(index<domainMin||index>domainMax+1)return false;
            if(index==domainMax+1)return (double)domainMax*grid<bound;
            double value=(double)index*grid;
            return value>=bound&&(index==domainMin||(double)(index-1)*grid<bound);
        }
        private static bool ValidUpper(long index,long domainMin,long domainMax,double grid,double bound)
        {
            if(index>domainMax||index<domainMin&&domainMin==long.MinValue||domainMin>long.MinValue&&index<domainMin-1)return false;
            if(domainMin>long.MinValue&&index==domainMin-1)return (double)domainMin*grid>bound;
            double value=(double)index*grid;
            return value<=bound&&(index==domainMax||(double)(index+1)*grid>bound);
        }
        private static bool ValidBracket(long low,long high,long domainMin,long domainMax,double grid,double bound,bool lower)
        {
            if(low<domainMin||high>domainMax+1||low>=high)return false;
            bool lowSide=low==domainMin||(lower?(double)(low-1)*grid<bound:(double)(low-1)*grid<=bound);
            bool highSide=high==domainMax+1||(lower?(double)high*grid>=bound:(double)high*grid>bound);
            return lowSide&&highSide;
        }
        private static bool CertificateEquals(in NavExclusionCertificate a,in NavExclusionCertificate b)=>
            a.ProviderIdentity==b.ProviderIdentity&&a.GeometryIdentity==b.GeometryIdentity&&a.GridCell.Equals(b.GridCell)&&
            a.MinX.Equals(b.MinX)&&a.MaxX.Equals(b.MaxX)&&a.MinZ.Equals(b.MinZ)&&a.MaxZ.Equals(b.MaxZ)&&
            a.MinXIndex==b.MinXIndex&&a.MaxXIndex==b.MaxXIndex&&a.MinZIndex==b.MinZIndex&&a.MaxZIndex==b.MaxZIndex&&
            a.Samples==b.Samples&&a.ExcludedCount==b.ExcludedCount&&a.RemainingCount==b.RemainingCount&&
            a.MaxSamples==b.MaxSamples&&a.MaxNodes==b.MaxNodes&&a.MaxArcs==b.MaxArcs&&a.MaxLabels==b.MaxLabels&&a.ObstacleIndex==b.ObstacleIndex;
        private static bool CertificateMatchesInput(in NavExclusionInput i,in NavExclusionCertificate c)=>c.ProviderIdentity==i.ProviderIdentity&&c.GeometryIdentity==i.GeometryIdentity&&c.GridCell.Equals(i.GridCell)&&
            c.Samples==i.Samples&&c.MaxSamples==i.MaxSamples&&c.MaxNodes==i.MaxNodes&&c.MaxArcs==i.MaxArcs&&c.MaxLabels==i.MaxLabels;
        private static bool ValidIntersectionAxis(long lo,long hi,long domainLo,long domainHi,double grid,double min,double max)
        {
            if(!ValidLower(lo,domainLo,domainHi,grid,min)||!ValidUpper(hi,domainLo,domainHi,grid,max))return false;
            if(lo<=hi)return lo>=domainLo&&hi<=domainHi;
            return lo==hi+1;
        }
        private static bool Inside(double x,double z,in NavExclusionCertificate c)=>Finite(x)&&Finite(z)&&
            x>=c.MinX&&x<=c.MaxX&&z>=c.MinZ&&z<=c.MaxZ;
    }
}
