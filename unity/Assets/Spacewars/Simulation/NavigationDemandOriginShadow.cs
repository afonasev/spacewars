using System;
using System.IO;
namespace Spacewars.Simulation
{
    internal sealed partial class NavDemandQueryWork
    {
        private const int OriginShadowScalarBytes=16;
        private int shadowBlocker=-1,shadowStart,shadowLow,shadowHigh;
        private NavHeuristicBox OriginShadowBox(int last)
        {
            double x=(min+lazyX)*request.Profile.GridCell;
            return new NavHeuristicBox{MinX=x,MaxX=x,MinZ=(min+shadowStart)*request.Profile.GridCell,MaxZ=(min+last)*request.Profile.GridCell};
        }
        private void BeginOriginShadow(int blocker)
        {
            var obstacle=Provider.ExclusionObstacles[blocker];
            if(obstacle.Polygon!=null||obstacle.CircleRadius!=0)return;
            shadowBlocker=blocker;shadowStart=shadowLow=lazyZ;shadowHigh=lazyMaxZ;
        }
        private void ClearOriginShadow(){shadowBlocker=-1;shadowStart=shadowLow=shadowHigh=0;}
        private void StepOriginShadow()
        {
            // ONE rounded interval proof per paid Connector action. A failed
            // singleton falls back immediately; successful prefixes alone can
            // advance the cursor. No monotonicity or exact shadow is assumed.
            int last=shadowLow==shadowStart?shadowStart:shadowLow+(shadowHigh-shadowLow)/2;
            bool reject=Provider.RejectTerminalRectangle(request.Subscription.Origin.Position,OriginShadowBox(last),request.Clearance.Radius,shadowBlocker,true);
            if(reject)shadowLow=last+1;
            else if(shadowLow==shadowStart){ClearOriginShadow();return;}
            else shadowHigh=last-1;
            if(shadowLow>shadowHigh){lazyZ=shadowLow;ClearOriginShadow();}
            // Existing paid StepRow drains any crossed absence intervals and
            // ends this same row. Unknown cells were never made Absent.
        }
        private void ForkOriginShadow(NavDemandQueryWork f){f.shadowBlocker=shadowBlocker;f.shadowStart=shadowStart;f.shadowLow=shadowLow;f.shadowHigh=shadowHigh;}
        private void WriteOriginShadow(BinaryWriter w){w.Write(shadowBlocker);w.Write(shadowStart);w.Write(shadowLow);w.Write(shadowHigh);}
        private void ReadOriginShadow(BinaryReader r){shadowBlocker=r.ReadInt32();shadowStart=r.ReadInt32();shadowLow=r.ReadInt32();shadowHigh=r.ReadInt32();}
        private void ValidateOriginShadow()
        {
            if(shadowBlocker<0){if(shadowBlocker!=-1||shadowStart!=0||shadowLow!=0||shadowHigh!=0)throw new ArgumentException("Noncanonical empty origin shadow.");return;}
            if(!request.OriginShadowProof||phase!=Phase.Catalog||Status!=NavSolveStatus.Pending||lazyStage!=4||!CapacityReady||!OriginPreparationReady||!HeuristicReady||terminal!=2*request.Region.Candidates.Count||lazyX<lazyMinX||lazyX>lazyMaxX||lazyZ<lazyMinZ||lazyZ>lazyMaxZ||shadowStart!=lazyZ||shadowLow<shadowStart||shadowHigh<shadowLow||shadowHigh>lazyMaxZ||shadowLow==shadowStart&&shadowHigh!=lazyMaxZ||shadowBlocker>=Provider.ExclusionObstacles.Count)throw new ArgumentException("Origin shadow paid search cursor.");
            var obstacle=Provider.ExclusionObstacles[shadowBlocker];
            if(obstacle.Polygon!=null||obstacle.CircleRadius!=0||shadowLow>shadowStart&&!Provider.RejectTerminalRectangle(request.Subscription.Origin.Position,OriginShadowBox(shadowLow-1),request.Clearance.Radius,shadowBlocker,true))throw new ArgumentException("Origin shadow lacks certified prefix.");
        }
    }
}
