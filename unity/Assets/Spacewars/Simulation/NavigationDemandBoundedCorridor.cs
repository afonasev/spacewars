using System;
using System.IO;
using System.Collections.Generic;
namespace Spacewars.Simulation
{
    // A feasible shared suffix never changes the independent exact search's keys,
    // incumbent or complete alternative set. Only a full stable cut can certify it.
    internal sealed partial class NavDemandQueryWork
    {
        private const int BoundedScalarBytes=2*NavExactCost.Bytes+8*4+3+16;
        private long observedExpansions,observedTerminalSweeps;
        internal long ExactExpansions=>observedExpansions;internal long ExactTerminalSweeps=>observedTerminalSweeps;
        private NavDemandQueryWork corridorSource;
        private NavExactCost corridorSuffix,corridorUpper;
        // No independent reconstruction has begun when selecting a suffix. The
        // existing prefix slot carries its frozen full-cut proof in this policy.
        private NavExactCost corridorLower=>boundedSelected?exactRoutePrefix:default;
        private int corridorScan, corridorBoundary=-1, corridorNode=-1, corridorEmit, corridorArc, corridorLeg, corridorChecks, corridorVisits;
        private bool corridorScanned,boundedSelected,corridorCutPaid;
        internal NavScheduledRequest BoundedRecipe=>request;
        internal bool CanDonateCorridor=>request.CertifiedGroupCorridor&&Status==NavSolveStatus.Ready&&!boundedSelected;
        internal bool UsedCertifiedCorridor=>boundedSelected&&Status==NavSolveStatus.Ready;
        internal NavExactCost PublishedObjective=>boundedSelected?corridorUpper:ExactObjective;
        internal NavExactCost PublishedLower=>boundedSelected?corridorLower:ExactObjective;
        internal int CorridorCertificateChecks=>corridorChecks;
        internal int CorridorScanActions=>corridorVisits;
        private bool CorridorCompatible(NavDemandQueryWork s)
        {
            if(s==null||!s.CanDonateCorridor||!request.CertifiedGroupCorridor||request.CompileKey!=s.request.CompileKey||request.Region.StableKey!=s.request.Region.StableKey||request.MaxLabels!=s.request.MaxLabels)return false;
            var a=request.Subscription;var b=s.request.Subscription;
            return a.WorldGeneration==b.WorldGeneration&&a.GroupId==b.GroupId&&a.RootOrderRevision==b.RootOrderRevision&&a.CommandSequence==b.CommandSequence&&a.ActivationRevision==b.ActivationRevision&&a.HoldBinding==b.HoldBinding&&request.Clearance.Radius==s.request.Clearance.Radius;
        }
        internal void AttachCorridor(NavDemandQueryWork source)
        {
            if(frozen||phase!=Phase.Catalog||corridorSource!=null)throw new ArgumentException("Corridor attachment phase.");
            if(!CorridorCompatible(source))return;
            if(IsRegistered)RequireEdge(source);source.Seal();source.Retain();corridorSource=source;
        }
        private void ForkBounded(NavDemandQueryWork f)
        {
            f.observedExpansions=observedExpansions;f.observedTerminalSweeps=observedTerminalSweeps;f.corridorSuffix=corridorSuffix;f.corridorUpper=corridorUpper;f.corridorScan=corridorScan;f.corridorBoundary=corridorBoundary;f.corridorNode=corridorNode;f.corridorEmit=corridorEmit;f.corridorArc=corridorArc;f.corridorLeg=corridorLeg;f.corridorChecks=corridorChecks;f.corridorVisits=corridorVisits;f.corridorCutPaid=corridorCutPaid;f.corridorScanned=corridorScanned;f.boundedSelected=boundedSelected;
            f.corridorSource=NavPhysicalMacro.Acquire(f,corridorSource);
        }
        internal static bool CertifiedRatio(NavExactCost u,NavExactCost l)
        {
            if(l==default)return false;
            try{return Multiply(u,100)<=Multiply(l,115);}catch(ArgumentException){return false;}
        }
        private static NavExactCost Multiply(NavExactCost value,int factor)
        {
            var sum=default(NavExactCost);while(factor!=0){if((factor&1)!=0)sum=sum.Add(value);factor>>=1;if(factor!=0)value=value.Add(value);}return sum;
        }
        private bool Forward(int node)
        {
            var p=NodeLocation(node).Position;var o=request.Subscription.Origin.Position;var e=request.Subscription.AssignedEndpoint.Position;
            // This is an admission restriction, not a metric/lower-bound proof.
            double x=p.X-o.X,z=p.Z-o.Z,dx=e.X-o.X,dz=e.Z-o.Z;
            double dot=x*dx+z*dz;return !double.IsInfinity(dot)&&!double.IsNaN(dot)&&dot>0&&!p.Equals(corridorSource.request.Subscription.Origin.Position);
        }
        private bool StepBounded()
        {
            if(!request.CertifiedGroupCorridor||corridorSource==null)return false;
            if(phase==Phase.BoundedReconstruct){EmitBounded();return true;}
            if(phase==Phase.Seed&&!corridorScanned){
                // Exhaustive Origins/Egress are complete before Seed. Each source
                // boundary is visited once and charged as one Connector action.
                int i=corridorScan;var d=corridorSource;corridorVisits++;
                if(i==0)corridorSuffix=NavExactCost.FromDouble(Cost(d.NodeLocation(d.chosen),request.Region.Candidates[d.chosenCandidate]));
                else corridorSuffix=corridorSuffix.Add(d.chain[i-1].Cost);
                int node=i==0?d.chosen:d.chain[i-1].From;
                if(i>0&&starts.TryGetValue(node,out double ingress)&&ends.TryGetValue(d.chosenCandidate,out double egress)&&Forward(node)){
                    var u=NavExactCost.FromDouble(ingress).Add(corridorSuffix).Add(egress);
                    if(corridorBoundary<0||u<corridorUpper||u==corridorUpper&&i>corridorBoundary){corridorUpper=u;corridorBoundary=i;corridorNode=node;}
                }
                corridorScan++;if(corridorScan>d.chain.Count)corridorScanned=true;return true;
            }
            if(phase!=Phase.Pop||!corridorScanned||corridorBoundary<0||pending||current!=-1||exactFrontier.Count==0)return false;
            // Comparison is its own paid action. A failed comparison then admits
            // exactly one ordinary pop before another complete-cut comparison.
            if(corridorCutPaid){corridorCutPaid=false;return false;}
            corridorCutPaid=true;
            var lower=exactFrontier.Min.Cost;if(chosen>=0&&exactBest<lower)lower=exactBest;corridorChecks++;
            if(!CertifiedRatio(corridorUpper,lower))return true;
            exactRoutePrefix=lower;boundedSelected=true;corridorArc=corridorBoundary-1;corridorEmit=0;corridorLeg=0;phase=Phase.BoundedReconstruct;return true;
        }
        private void EmitBounded()
        {
            var d=corridorSource;
            if(corridorEmit==0){AddSurface(request.Subscription.Origin,NodeLocation(corridorNode));corridorEmit=1;return;}
            if(corridorEmit==1&&corridorArc>=0){
                var arc=d.chain[corridorArc];if(arc.Trace!=null){Add(arc.Trace[corridorLeg++]);if(corridorLeg<arc.Trace.Count)return;corridorLeg=0;}else AddSurface(d.NodeLocation(arc.From),d.NodeLocation(arc.To));corridorArc--;return;
            }
            if(corridorEmit==1){AddSurface(d.NodeLocation(d.chosen),request.Region.Candidates[d.chosenCandidate]);corridorEmit=2;return;}
            if(corridorEmit==2){AddSurface(request.Region.Candidates[d.chosenCandidate],request.Subscription.AssignedEndpoint);corridorEmit=3;return;}
            Route=new NavTypedRoute(request.Subscription,legs,routeCost,NavStorageLayout.Before(reserve,128),true);Finish(NavSolveStatus.Ready);
        }
        private void WriteBounded(BinaryWriter w)
        {
            corridorSuffix.Write(w);corridorUpper.Write(w);
            w.Write(corridorScan);w.Write(corridorBoundary);w.Write(corridorNode);w.Write(corridorEmit);w.Write(corridorArc);w.Write(corridorLeg);w.Write(corridorChecks);w.Write(corridorVisits);w.Write(corridorScanned);w.Write(boundedSelected);w.Write(corridorCutPaid);w.Write(observedExpansions);w.Write(observedTerminalSweeps);
        }
        private void ReadBounded(BinaryReader r)
        {
            corridorSuffix=NavExactCost.Read(r);corridorUpper=NavExactCost.Read(r);
            corridorScan=r.ReadInt32();corridorBoundary=r.ReadInt32();corridorNode=r.ReadInt32();corridorEmit=r.ReadInt32();corridorArc=r.ReadInt32();corridorLeg=r.ReadInt32();corridorChecks=r.ReadInt32();corridorVisits=r.ReadInt32();byte scan=r.ReadByte(),selected=r.ReadByte(),paid=r.ReadByte();if(scan>1||selected>1||paid>1)throw new ArgumentException("Noncanonical corridor flag.");corridorScanned=scan==1;boundedSelected=selected==1;corridorCutPaid=paid==1;observedExpansions=r.ReadInt64();observedTerminalSweeps=r.ReadInt64();if(observedExpansions<0||observedTerminalSweeps<0)throw new ArgumentException("Invalid corridor work observations.");
        }
        private void ValidateBounded()
        {
            if(!request.CertifiedGroupCorridor){if(corridorSource!=null||boundedSelected||corridorScan!=0)throw new ArgumentException("Corridor state in exact policy.");return;}
            if(corridorSource==null){if(boundedSelected||corridorScan!=0||corridorBoundary!=-1||corridorNode!=-1)throw new ArgumentException("Corridor state lacks donor.");return;}
            var d=corridorSource;if(!CorridorCompatible(d)||corridorScan<0||corridorScan>d.chain.Count+1||corridorVisits!=corridorScan||corridorScanned!=(corridorScan==d.chain.Count+1)||corridorBoundary< -1||corridorBoundary>=corridorScan||corridorBoundary==0||corridorChecks<0)throw new ArgumentException("Corridor domain/paid cursor.");
            var suffix=NavExactCost.FromDouble(Cost(d.NodeLocation(d.chosen),request.Region.Candidates[d.chosenCandidate]));
            var best=default(NavExactCost);int boundary=-1,node=-1;
            for(int i=0;i<corridorScan;i++){
                if(i>0)suffix=suffix.Add(d.chain[i-1].Cost);int at=i==0?d.chosen:d.chain[i-1].From;
                if(i>0&&starts.TryGetValue(at,out double s)&&ends.TryGetValue(d.chosenCandidate,out double e)&&Forward(at)){var u=NavExactCost.FromDouble(s).Add(suffix).Add(e);if(boundary<0||u<=best){best=u;boundary=i;node=at;}}
            }
            if(corridorScan==0)suffix=default;
            if(suffix!=corridorSuffix||best!=corridorUpper||boundary!=corridorBoundary||node!=corridorNode||boundedSelected&&(corridorBoundary<1||!CertifiedRatio(corridorUpper,corridorLower)||!corridorScanned||pending||current!=-1||chain.Count!=0||phase!=Phase.BoundedReconstruct&&phase!=Phase.Done))throw new ArgumentException("Corridor feasible objective/selection contradiction.");
            if(boundedSelected){var l=exactFrontier.Min.Cost;if(chosen>=0&&exactBest<l)l=exactBest;if(l!=corridorLower)throw new ArgumentException("Corridor full cut binding changed.");}
        }
        private void ValidateBoundedRoute()
        {
            if(!boundedSelected)return;var d=corridorSource;int index=0;
            void Surface(NavLocation a,NavLocation b){if(index>=legs.Count)throw new ArgumentException("Missing corridor surface.");var l=legs[index++];if(!l.From.Equals(a)||!l.To.Equals(b)||l.Kind!=NavTypedLegKind.Surface||l.Cost!=Cost(a,b))throw new ArgumentException("Corridor surface binding.");}
            if(corridorEmit>0)Surface(request.Subscription.Origin,NodeLocation(corridorNode));
            for(int i=corridorBoundary-1;i>=0;i--){var arc=d.chain[i];int count=i>corridorArc?(arc.Trace?.Count??1):i==corridorArc?corridorLeg:0;if(arc.Trace!=null){for(int j=0;j<count;j++)if(index>=legs.Count||!ReferenceEquals(legs[index++],arc.Trace[j]))throw new ArgumentException("Corridor typed suffix alias.");}else if(count!=0)Surface(d.NodeLocation(arc.From),d.NodeLocation(arc.To));}
            if(corridorEmit>1)Surface(d.NodeLocation(d.chosen),request.Region.Candidates[d.chosenCandidate]);
            if(corridorEmit>2)Surface(request.Region.Candidates[d.chosenCandidate],request.Subscription.AssignedEndpoint);
            if(index!=legs.Count||corridorEmit<0||corridorEmit>3||corridorArc< -1||corridorArc>=corridorBoundary||corridorLeg<0||Status==NavSolveStatus.Ready&&corridorEmit!=3)throw new ArgumentException("Corridor reconstruction cursor.");
        }
        private Checkpoint SaveBounded(Dictionary<NavTypedLeg,NavTypedLeg> copies,Dictionary<NavDemandQueryWork,Checkpoint> aliases)
        {
            if(corridorSource==null)return null;var saved=corridorSource.SaveWithAliases(aliases);for(int i=0;i<corridorSource.legs.Count;i++)copies[corridorSource.legs[i]]=saved.Legs[i];return saved;
        }
        private void RestoreBounded(Checkpoint saved,Dictionary<NavTypedLeg,NavTypedLeg> copies,Dictionary<Checkpoint,NavDemandQueryWork> aliases)
        {
            if((saved.Corridor==null)!=(saved.CorridorRecipe==null)||saved.Corridor!=null&&!request.CertifiedGroupCorridor)throw new ArgumentException("Logical corridor recipe.");
            if(saved.Corridor==null)return;corridorSource=RestoreWithAliases(DetachSharedRecipe(saved.CorridorRecipe,request.Provider),saved.Corridor,aliases);corridorSource.Seal();for(int i=0;i<corridorSource.legs.Count;i++)copies[saved.Corridor.Legs[i]]=corridorSource.legs[i];
        }
    }
}
