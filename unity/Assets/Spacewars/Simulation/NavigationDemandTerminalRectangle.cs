using System;
using System.IO;
namespace Spacewars.Simulation
{
    internal sealed partial class NavDemandQueryWork
    {
        private const int TerminalRectangleScalarBytes=37;
        private NavHeuristicBox terminalBox;
        private bool terminalAttempted;
        private int terminalBlocker=-1;
        private void FoldTerminalCandidate(NavPoint p)
        {
            if(!request.TerminalRectangleProof)return;
            if(hCandidateCursor==1)terminalBox=new NavHeuristicBox{MinX=p.X,MaxX=p.X,MinZ=p.Z,MaxZ=p.Z};else terminalBox.Include(p);
        }
        private bool TerminalCostRejects()
        {
            // Existing metric/domain slack covers two rounded terminal segments.
            // Strict inequality preserves every exact objective and tie.
            if(Provider.SurfaceCost(NodeLocation(current).SurfaceId)!=1||!NavExactHeuristic.TryTerminalBound(NodeLocation(current).Position,request.Subscription.AssignedEndpoint.Position,HeuristicActive,request.MetricContract,out var lower))return false;
            return exactDistance[current].Add(lower)>exactBest;
        }
        private void ResetTerminalRectangle(){terminalAttempted=false;terminalBlocker=-1;}
        private void StepTerminalRectangle()
        {
            // This is a separate paid Connector action, at most once per Pop.
            // The retained index came from the preceding actual failed sweep.
            bool reject=Provider.RejectTerminalRectangle(NodeLocation(current).Position,terminalBox,request.Clearance.Radius,terminalBlocker,request.TerminalCornerProof);
            terminalAttempted=true;terminalBlocker=-1;
            if(reject)candidate=request.Region.Candidates.Count;
        }
        private void ForkTerminalRectangle(NavDemandQueryWork f){f.terminalBox=terminalBox;f.terminalAttempted=terminalAttempted;f.terminalBlocker=terminalBlocker;}
        private void WriteTerminalRectangle(BinaryWriter w){terminalBox.Write(w);w.Write(terminalAttempted);w.Write(terminalBlocker);}
        private void ReadTerminalRectangle(BinaryReader r){terminalBox=NavHeuristicBox.Read(r);byte b=r.ReadByte();if(b>1)throw new ArgumentException("Noncanonical terminal rectangle flag.");terminalAttempted=b==1;terminalBlocker=r.ReadInt32();}
        private void ValidateTerminalRectangle()
        {
            if(!request.TerminalRectangleProof){if(!terminalBox.Equals(default)||terminalAttempted||terminalBlocker!=-1)throw new ArgumentException("Unexpected terminal rectangle state.");return;}
            if(!request.RectangularOrigins||terminalBlocker< (request.TerminalCostProof?-2:-1)||terminalBlocker==-2&&(phase!=Phase.Candidates||candidate!=0||chosen<0||current<0||terminalAttempted)||terminalBlocker>=Provider.ExclusionObstacles.Count||terminalBlocker>=0&&(terminalAttempted||phase!=Phase.Candidates||candidate<=0||candidate>=request.Region.Candidates.Count||current<0)||phase==Phase.Catalog&&(terminalAttempted||terminalBlocker!=-1))throw new ArgumentException("Terminal rectangle continuation phase.");
            var expected=default(NavHeuristicBox);
            for(int i=0;i<hCandidateCursor;i++){var p=request.Region.Candidates[i].Position;if(i==0)expected=new NavHeuristicBox{MinX=p.X,MaxX=p.X,MinZ=p.Z,MaxZ=p.Z};else expected.Include(p);}
            if(!expected.Equals(terminalBox))throw new ArgumentException("Terminal rectangle paid candidate hull.");
        }
    }
}
